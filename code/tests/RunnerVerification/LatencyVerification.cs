using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using BigBlueFish.Windows;
using Application = System.Windows.Application;
using Brushes = System.Windows.Media.Brushes;

// A separate visible process supplies known screen content. The pet still resolves
// the real foreground HWND, captures it, OCRs it and streams a real model response.
internal static class LatencyVerification
{
    const BindingFlags Access = BindingFlags.Instance | BindingFlags.NonPublic;
    static object? Field(object target, string name) => target.GetType().GetField(name, Access)!.GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Access)!.SetValue(target, value);
    static object? Invoke(object target, string name) => target.GetType().GetMethod(name, Access)!.Invoke(target, null);
    static Task<int> Sta(Func<int> action)
    {
        var result = new TaskCompletionSource<int>();
        var thread = new Thread(() => { try { result.SetResult(action()); } catch (Exception e) { result.SetException(e); } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return result.Task;
    }
    static void Pump()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Send) { Interval = TimeSpan.FromMilliseconds(15) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    static void Wait(Func<bool> condition, int seconds, string message)
    {
        var clock = Stopwatch.StartNew();
        while (!condition() && clock.Elapsed.TotalSeconds < seconds) Pump();
        if (!condition()) throw new Exception(message);
    }
    internal static Task<int> Fixture(string root) => Sta(() =>
    {
        var app = new Application();
        var text = new TextBlock { FontSize = 18, Foreground = Brushes.Black, Background = Brushes.White,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(25) };
        var window = new Window { Title = "蓝色大肥鱼代码开发 · 首字时间验收", Width = 1120, Height = 840,
            Left = 20, Top = 20, Content = new ScrollViewer { Content = text }, Background = Brushes.White };
        string previous = "";
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        timer.Tick += (_, _) =>
        {
            if (File.Exists(Path.Combine(root, "fixture-stop"))) { window.Close(); return; }
            var scene = Path.Combine(root, "scene.txt");
            if (!File.Exists(scene)) return;
            var current = File.ReadAllText(scene);
            if (current == previous) return;
            previous = current; text.FontSize = current.StartsWith("密集页面") ? 14 : 18;
            text.Text = current; window.Activate();
            window.UpdateLayout();
            File.WriteAllText(Path.Combine(root, "fixture-ready"), current.Length.ToString());
        };
        window.Loaded += (_, _) =>
        {
            File.WriteAllText(Path.Combine(root, "fixture-hwnd"), new WindowInteropHelper(window).Handle.ToInt64().ToString());
            timer.Start();
        };
        app.Run(window); timer.Stop(); return 0;
    });
    internal static Task<int> Run(string root) => Sta(() => Verify(Path.GetFullPath(root)));
    static int Verify(string root)
    {
        MainWindow? fish = null; Process? fixture = null;
        var records = new List<object>();
        try
        {
            Directory.CreateDirectory(root);
            var app = new App(); app.InitializeComponent(); App.Args = ["--test-mode"];
            typeof(Application).GetField("_startupUri", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, null);
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Directory.SetCurrentDirectory(root);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "BigBlueFish.ico"), "BigBlueFish.ico", true);
            File.WriteAllText("preferences.json", """{"ModelEnabled":false,"GpuEnabled":true,"StopModelOnBattery":false,"ForegroundEnabled":true,"PublicInfo":false,"HideOnFullscreen":false,"CheckUpdatesOnStartup":false,"Scale":0.65,"Left":1100,"Top":700}""");
            fish = new MainWindow(); fish.Show();
            Wait(() => (bool)Field(fish, "companionReady")!, 25, "Animations did not load");
            ((DispatcherTimer)Field(fish, "companionTimer")!).Stop();
            Wait(() => (string)Field(fish, "animationPhase")! != "Startup", 15, "Greeting did not finish");
            App.Args = ["--test-mode"]; // WPF startup has now completed and assigned its command-line args.
            var prefs = (CompanionPreferences)Field(fish, "preferences")!;
            var brain = (CompanionBrain)Field(fish, "brain")!;
            prefs.ModelEnabled = true;
            var cold = Stopwatch.StartNew(); var warmup = brain.WarmUp();
            Wait(() => warmup.IsCompleted, 100, "Model loading timed out"); warmup.GetAwaiter().GetResult();
            if (brain.RunnerPid == null) throw new Exception(brain.Status);
            double coldSeconds = cold.Elapsed.TotalSeconds;
            using (var runner = Process.GetProcessById(brain.RunnerPid.Value))
                Console.WriteLine($"COLD model ready {coldSeconds:0.000}s PID {runner.Id}; {brain.Status}");
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            start.ArgumentList.Add("--latency-fixture"); start.ArgumentList.Add(root);
            fixture = Process.Start(start)!;
            Wait(() => File.Exists("fixture-hwnd"), 15, "Screen fixture did not start");
            var handle = new IntPtr(long.Parse(File.ReadAllText("fixture-hwnd")));
            string summary = "蓝色大肥鱼项目开发\n\n主人正在排查本地模型加载和首字等待时间，验证不同电脑上的运行表现。\n当前任务：修复显存分配，检查读屏、文字识别、流式气泡显示。\n";
            string[] rows = [
                "模型运行：保留用户已有模型和设置，检查健康状态与进程退出。",
                "读屏流程：捕获真正的前台窗口，识别界面文字并按位置整理。",
                "气泡显示：模型返回文字后立即显示，记录第一个字出现的时间。",
                "下载任务：暂停时保留未完成内容，重试后继续下载并校验。",
                "兼容检查：根据设备可用显存选择计算缓冲，避免显存不足。"
            ];
            for (int i = 0; i < 10; i++)
            {
                string scene = (i >= 8 ? "密集页面\n" : "") + $"场景 {i + 1}\n" + summary + string.Join("\n", Enumerable.Range(0, i >= 8 ? 40 : new[] { 0, 8, 24 }[i % 3])
                    .Select(n => $"{n + 1:00}. {rows[n % rows.Length]}"));
                File.WriteAllText("scene.txt", scene);
                Wait(() => File.Exists("fixture-ready") && File.ReadAllText("fixture-ready") == scene.Length.ToString()
                    && CompanionNative.GetForegroundWindow() == handle, 12, "Fixture is not the real foreground");
                // Allow the rendered text to reach the compositor; this is before the click clock starts.
                var render = Stopwatch.StartNew(); while (render.ElapsedMilliseconds < 180) Pump();
                var clock = Stopwatch.StartNew();
                var task = (Task)Invoke(fish, "SpeakCompanionTest")!;
                double? first = null;
                Wait(() =>
                {
                    if (first == null && (bool)Field(fish, "bubbleIsModelReply")!
                        && fish.CompanionBubble.Visibility == Visibility.Visible && fish.CompanionBubbleText.Text.Length > 0)
                        first = clock.Elapsed.TotalSeconds;
                    return task.IsCompleted;
                }, 100, "Speech did not complete");
                task.GetAwaiter().GetResult();
                if (first == null) throw new Exception("No actual bubble text: " + brain.Status);
                var diagnostic = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ocr-status.json"))).RootElement.Clone();
                if (diagnostic.GetProperty("windowHandle").GetInt64() != handle.ToInt64()
                    || !diagnostic.GetProperty("request").GetString()!.Contains("蓝色大肥鱼"))
                    throw new Exception("Production OCR did not read the intended visible content");
                var image = CompanionScreenCapture.Capture(handle, 2560, true)!;
                File.WriteAllBytes($"scene-{i + 1}.png", Convert.FromBase64String(image.DataUrl[(image.DataUrl.IndexOf(',') + 1)..]));
                var record = new { index = i + 1, coldSeconds, firstSeconds = first, totalSeconds = clock.Elapsed.TotalSeconds,
                    text = fish.CompanionBubbleText.Text, diagnostic, timings = brain.LastTimings };
                records.Add(record);
                File.WriteAllText("latency-results.json", JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"REAL SCREEN {i + 1} first={first:0.000}s total={clock.Elapsed.TotalSeconds:0.000}s words={diagnostic.GetProperty("wordCount")} {fish.CompanionBubbleText.Text}");
            }
            Console.WriteLine("RESULT 10 actual foreground/OCR/model/bubble measurements recorded; no canned model response");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally
        {
            File.WriteAllText(Path.Combine(root, "fixture-stop"), "stop");
            if (fixture != null) { fixture.WaitForExit(5000); fixture.Dispose(); }
            if (fish != null) { Set(fish, "exitReady", true); fish.Close(); }
        }
    }
}
