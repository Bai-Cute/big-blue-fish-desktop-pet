using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net;
using System.Reflection;
using System.Text;
using VPet_Simulator.Windows;

internal static class Program
{
    private sealed class WeatherFixture : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                """{"current":{"time":"2026-10-07T08:00","temperature_2m":21,"apparent_temperature":20,"relative_humidity_2m":65,"weather_code":0,"wind_speed_10m":8},"daily":{"temperature_2m_min":[18],"temperature_2m_max":[25],"precipitation_probability_max":[10]}}""") });
    }
    private static int checks;
    private static void Check(bool value, string description)
    {
        if (!value) throw new Exception(description);
        Console.WriteLine("PASS " + description);
        checks++;
    }

    static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--fake-runner") return await FakeRunner(int.Parse(args[1]), args[2]);
        try
        {
            string root = Path.GetFullPath(args[0]);
            Directory.CreateDirectory(root);
            Environment.CurrentDirectory = root;
            if (args.Contains("--real-model"))
            {
                using var actual = new CompanionBrain { StopOnBattery = false };
                var text = await actual.Generate(false, false);
                Check(text.Length > 0 && text.Contains("主人"), "real local model generates text through production request and completion path");
                int pid = actual.RunnerPid!.Value;
                Check((await actual.Generate(false, false)).Length > 0 && actual.RunnerPid == pid, "real ready model remains resident and is reused");
                using (var process = Process.GetProcessById(pid)) { process.Kill(true); process.WaitForExit(); }
                Check((await actual.Generate(false, false)).Length > 0 && actual.RunnerPid != pid, "real model restarts after runner exits without app restart");
                actual.Stop();
                Check(actual.RunnerPid == null, "real model process is unloaded after test");
                Console.WriteLine($"RESULT {checks} real model checks passed ({CompanionBrain.InputMode}); response: {text}");
                return 0;
            }
            Directory.CreateDirectory("runtime");
            Directory.CreateDirectory("models");
            File.WriteAllText("runtime/llama-server.exe", "fault-injection fixture, never a release component");
            File.WriteAllText("models/Qwen3.5-4B-heretic-Q4_K_M.gguf", "fault-injection fixture");
            var modePath = Path.Combine(root, "runner-mode.txt");
            var children = new List<int>();
            Process? Start(ProcessStartInfo original)
            {
                int index = original.ArgumentList.IndexOf("--port");
                string port = original.ArgumentList[index + 1];
                original.FileName = Environment.ProcessPath!;
                original.ArgumentList.Clear();
                if (Path.GetFileNameWithoutExtension(original.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                    original.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
                foreach (var value in new[] { "--fake-runner", port, modePath }) original.ArgumentList.Add(value);
                var process = Process.Start(original)!;
                children.Add(process.Id);
                return process;
            }
            using var weatherClient = new HttpClient(new WeatherFixture());
            using var brain = new CompanionBrain(Start, TimeSpan.FromSeconds(5), weatherClient) { StopOnBattery = false };
            async Task Ensure(CancellationToken token = default) =>
                await (Task)typeof(CompanionBrain).GetMethod("EnsureRunner", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(brain, [token])!;
            static bool Exited(int pid)
            {
                try { using var process = Process.GetProcessById(pid); return process.HasExited || process.WaitForExit(3000); }
                catch (ArgumentException) { return true; }
            }

            File.WriteAllText(modePath, "loading");
            var failed = await brain.Generate(false, false);
            Check(failed.Length == 0 && brain.RunnerPid == null && !brain.Busy, "ordinary generation loading failure clears runner and busy state");
            Check(Exited(children[^1]), "failed loading child actually exits");
            File.WriteAllText(modePath, "ready");
            var recovered = await brain.Generate(false, false);
            Check(recovered.Length > 0 && brain.RunnerPid != null, "same brain recovers on next generation without restarting app");
            int healthyPid = brain.RunnerPid!.Value;
            await Ensure();
            Check(brain.RunnerPid == healthyPid, "healthy resident runner is reused after health check");
            File.WriteAllText(modePath, "unhealthy");
            try { await Ensure(); throw new Exception("Expected failed health/startup"); } catch (TimeoutException) { }
            Check(brain.RunnerPid == null && Exited(healthyPid) && Exited(children[^1]), "alive but unhealthy runner is replaced and failed replacement is cleaned");
            File.WriteAllText(modePath, "ready");
            await Ensure();
            Check(brain.RunnerPid != null, "health failure can recover on subsequent request");
            File.WriteAllText(modePath, "bad-generation");
            int badPid = brain.RunnerPid!.Value;
            Check((await brain.Generate(false, false)).Length == 0 && brain.RunnerPid == null && Exited(badPid), "local completion HTTP failure invalidates resident runner");
            File.WriteAllText(modePath, "ready");
            Check((await brain.Generate(false, false)).Length > 0, "completion failure recovers on next request");
            brain.Stop();
            File.WriteAllText(modePath, "loading");
            Check((await brain.GenerateWeather()).Length == 0 && brain.RunnerPid == null && !brain.Busy && Exited(children[^1]), "weather request loading failure clears runner");
            File.WriteAllText(modePath, "ready");
            Check((await brain.GenerateWeather()).Length > 0 && brain.RunnerPid != null, "weather request recovers on same brain after loading failure");
            brain.Stop();
            File.WriteAllText(modePath, "hang");
            using (var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(800)))
            {
                try { await Ensure(cancel.Token); throw new Exception("Expected cancellation"); } catch (OperationCanceledException) { }
            }
            Check(brain.RunnerPid == null && Exited(children[^1]), "cancelled loading kills the child even when health request hangs");
            File.WriteAllText(modePath, "exit");
            try { await Ensure(); throw new Exception("Expected exited child"); } catch (IOException) { }
            Check(brain.RunnerPid == null && Exited(children[^1]), "child startup exit releases process reference");
            File.WriteAllText(modePath, "loading");
            await brain.WarmUp();
            Check(brain.RunnerPid == null && !brain.Busy && Exited(children[^1]), "warmup failure continues to clean up correctly");
            File.WriteAllText(modePath, "ready");
            await brain.WarmUp();
            Check(brain.RunnerPid != null, "warmup retries successfully on same brain");
            brain.Stop();
            Check(children.All(Exited), "no injected child remains alive after stop");

            var content = "前台应用：Code\n窗口正文：蓝色大肥鱼项目，正在修改 CompanionBrain.cs";
            var request = CompanionBrain.SpeechRequest(content);
            Check(request.Contains(content) && request.Contains("已展开具体内容") && request.Contains("入口界面"), "SpeechRequest preserves current screen content and facts hierarchy");
            var ocr = CompanionBrain.OcrSpeechRequest("我：明天中午一起吃火锅，十二点见。");
            Check(ocr.Contains("明天中午一起吃火锅") && ocr.Contains("桌宠旁观者"), "OCR request preserves chat topic and observer viewpoint");
            Check(CompanionBrain.Persona.Contains("主体正文、编辑区、聊天记录") && CompanionBrain.Persona.Contains("桌面、新标签页、主页"), "Persona matches current content and entry-screen behavior");
            Console.WriteLine($"RESULT {checks} runner/prompt checks passed ({CompanionBrain.InputMode}); child responses are fault fixtures, not real model evaluation");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static async Task<int> FakeRunner(int port, string modePath)
    {
        if (File.ReadAllText(modePath) == "exit") return 1;
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        while (true)
        {
            var context = await listener.GetContextAsync();
            string mode = File.ReadAllText(modePath);
            if (mode == "hang") { await Task.Delay(Timeout.InfiniteTimeSpan); continue; }
            bool health = context.Request.Url!.AbsolutePath == "/health";
            bool failed = health ? mode is "loading" or "unhealthy" : mode == "bad-generation";
            context.Response.StatusCode = failed ? 503 : 200;
            string response = failed ? "{\"error\":\"loading failure fixture\"}" : health ? "{\"status\":\"ok\"}"
                : "data: {\"choices\":[{\"delta\":{\"content\":\"主人，小女仆来给你加油啦～\"}}]}\n\ndata: [DONE]\n\n";
            var bytes = Encoding.UTF8.GetBytes(response);
            context.Response.ContentType = health || failed ? "application/json" : "text/event-stream";
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }
    }
}
