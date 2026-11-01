using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using BigBlueFish.Windows;

internal static class MacLocalChatVerification
{
    static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        Console.WriteLine("PASS: " + label);
    }

    internal static async Task Run()
    {
        VerifyAcpExecutableDiscovery();
        var original = Environment.CurrentDirectory;
        var folder = Path.Combine(Path.GetTempPath(), "bbf-mac-local-chat-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try {
            Environment.CurrentDirectory = folder;
            var defaults = new MacChatSettings();
            Check(defaults.Endpoint == "https://api.deepseek.com" && defaults.Model == "deepseek-flash" && defaults.ApiProtocol == "chat",
                "official DeepSeek URL and current model are defaults");
            Check(defaults.PersonalityPrompt == MacChatSettings.DefaultPersonalityPrompt && defaults.PersonalityPrompt.Contains("蓝色大肥鱼", StringComparison.Ordinal),
                "built-in Big Blue Fish personality is enabled by default");
            Check(defaults.PersonalityPrompt.Contains("主人", StringComparison.Ordinal) && defaults.PersonalityPrompt.Contains("大白饭", StringComparison.Ordinal) &&
                defaults.PersonalityPrompt.Contains("好奇", StringComparison.Ordinal) && defaults.PersonalityPrompt.Contains("一两句", StringComparison.Ordinal) &&
                defaults.PersonalityPrompt.Contains("不为了扮演", StringComparison.Ordinal) && defaults.PersonalityPrompt.Contains("蕾丝女仆发饰", StringComparison.Ordinal) &&
                defaults.PersonalityPrompt.Contains("鲸鱼尾巴", StringComparison.Ordinal) && defaults.PersonalityPrompt.Contains("白围裙", StringComparison.Ordinal),
                "built-in personality includes the project's full character traits and voice");
            File.WriteAllText("mac-chat-settings.json", "{\"PersonalityPrompt\":\"你是“蓝色大肥鱼”的桌面伙伴，昵称“大肥鱼”或“鱼鱼”。语气温柔、亲切、自然，认真回应用户，不夸张、不刻意卖萌。优先使用中文，回答清楚简洁。\\n用户问你是谁时，自称大肥鱼（蓝色大肥鱼桌宠）；不要主动自称 ChatGPT、OpenAI、Claude、Codex 或其他厂商/模型。若用户明确询问底层模型或服务来源，请说明回复由当前配置的 API、本机模型或 ACP Agent 提供，不要编造。\\n只依据当前对话和实际可用能力回答；不要声称能看见屏幕、记住未提供的事情、具有真实感受或已经执行未执行的动作。\"}");
            Check(MacChatSettings.Read().PersonalityPrompt == MacChatSettings.DefaultPersonalityPrompt,
                "previous built-in personality upgrades to the full character card");
            var settings = new MacChatSettings { Backend = "api", Endpoint = "http://127.0.0.1:1234/v1", Model = "test-model", PersonalityPrompt = "你是测试用的大肥鱼。" };
            settings.Save();
            var saved = MacChatSettings.Read();
            Check(saved.Backend == "api" && saved.Endpoint == settings.Endpoint && saved.Model == settings.Model && saved.ApiProtocol == "chat" &&
                saved.PersonalityPrompt == settings.PersonalityPrompt, "API configuration and personality round trip");
            settings.ApiProtocol = "responses"; settings.Save();
            Check(MacChatSettings.Read().ApiProtocol == "responses", "Responses API selection persists");
            settings.ApiProtocol = "chat";
            Check(!File.ReadAllText("mac-chat-settings.json").Contains("apiKey", StringComparison.OrdinalIgnoreCase), "API credentials are not persisted");
            var gguf = new MacChatSettings { Backend = "gguf", LlamaServerExecutable = "/opt/homebrew/bin/llama-server", GgufPath = "/tmp/fish.gguf", Model = "fish", WindowOpacity = .72 };
            gguf.Save();
            var restoredGguf = MacChatSettings.Read();
            Check(restoredGguf.Backend == "gguf" && restoredGguf.LlamaServerExecutable == gguf.LlamaServerExecutable && restoredGguf.GgufPath == gguf.GgufPath && restoredGguf.WindowOpacity == .72, "GGUF path and chat opacity persist");
            var acp = new MacChatSettings { Backend = "acp", AcpProvider = "codex", AcpExecutable = "/usr/local/bin/codex-acp", AcpConsent = true, WindowOpacity = 9 };
            acp.Save();
            var restoredAcp = MacChatSettings.Read();
            Check(restoredAcp.Backend == "acp" && restoredAcp.AcpProvider == "codex" && restoredAcp.AcpExecutable == acp.AcpExecutable && restoredAcp.AcpConsent && restoredAcp.WindowOpacity == 1, "ACP configuration persists and opacity is bounded");
            var acpPrompt = MacChatPersonality.BuildAcpPrompt(acp.PersonalityPrompt, "你好");
            Check(acpPrompt.Contains(acp.PersonalityPrompt, StringComparison.Ordinal) && acpPrompt.EndsWith("用户消息：\n你好", StringComparison.Ordinal),
                "ACP prompt includes personality and original user message");
            Check(MacChatPersonality.BuildAcpPrompt("", "hello") == "hello", "empty personality leaves ACP prompt unchanged");
            File.WriteAllText("mac-chat-settings.json", "broken");
            Check(MacChatSettings.Read().Backend == "gguf", "backup recovers damaged chat settings");
            Check(MacLocalChat.ValidateEndpoint("https://api.deepseek.com").AbsoluteUri == "https://api.deepseek.com/", "official DeepSeek base URL preserved");
            Check(MacLocalChat.ValidateEndpoint("https://api.example.org/custom/v1").AbsoluteUri == "https://api.example.org/custom/v1/", "custom HTTPS path preserved");
            var rejected = false;
            try { MacLocalChat.ValidateEndpoint("http://api.example.org/v1"); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "remote HTTP API endpoint rejected");
            rejected = false;
            try { MacLocalChat.ValidateEndpoint("https://user:secret@api.example.org/v1"); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "userinfo in API endpoint rejected");
            await using var client = new MacLocalChat();
            using var listener = new HttpListener();
            var portProbe = new TcpListener(IPAddress.Loopback, 0);
            portProbe.Start();
            var port = ((IPEndPoint)portProbe.LocalEndpoint).Port;
            portProbe.Stop();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            var requests = new List<JsonElement>();
            var server = Task.Run(async () => {
                for (int i = 1; i <= 4; i++) {
                    var context = await listener.GetContextAsync();
                    using var document = await JsonDocument.ParseAsync(context.Request.InputStream);
                    requests.Add(document.RootElement.Clone());
                    var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = $"reply-{i}" } } } }));
                    context.Response.ContentType = "application/json";
                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes);
                    context.Response.Close();
                }
            });
            settings.Endpoint = $"http://127.0.0.1:{port}/v1";
            settings.PersonalityPrompt = "请扮演测试大肥鱼。";
            Check(await client.Send("first", settings, null, CancellationToken.None) == "reply-1", "first local chat turn");
            Check(await client.Send("second", settings, null, CancellationToken.None) == "reply-2", "second local chat turn");
            Check(requests[0].GetProperty("messages")[0].GetProperty("role").GetString() == "system" &&
                requests[0].GetProperty("messages")[0].GetProperty("content").GetString() == settings.PersonalityPrompt,
                "Chat Completions injects editable system personality");
            Check(requests[1].GetProperty("messages").GetArrayLength() == 4, "local chat retains prior turn with system personality");
            var firstMessage = requests[1].GetProperty("messages")[1];
            Check(firstMessage.GetProperty("role").GetString() == "user" && firstMessage.GetProperty("content").GetString() == "first", "OpenAI message uses lowercase role and content fields");
            Check(requests[1].GetProperty("messages")[2].GetProperty("role").GetString() == "assistant", "OpenAI history includes assistant role");
            client.Reset();
            Check(await client.Send("fresh", settings, null, CancellationToken.None) == "reply-3", "reset local chat turn");
            Check(requests[2].GetProperty("messages").GetArrayLength() == 2, "reset clears local chat history but keeps system personality");
            Check(await client.Send("account-change", settings, "different-key", CancellationToken.None) == "reply-4", "local chat accepts a changed API credential");
            Check(requests[3].GetProperty("messages").GetArrayLength() == 2 &&
                requests[3].GetProperty("messages")[1].GetProperty("content").GetString() == "account-change",
                "changing API credentials does not send prior conversation context");
            await server;
            await VerifyResetWhileRequestCompletes();
            await VerifyResponsesApi();
            await VerifyHistoryBudget();
        } finally { Environment.CurrentDirectory = original; Directory.Delete(folder, true); }
    }

    static async Task VerifyResetWhileRequestCompletes()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var firstRequest = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRequest = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstResponse = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = Task.Run(async () => {
            var first = await listener.GetContextAsync();
            using (var document = await JsonDocument.ParseAsync(first.Request.InputStream))
                firstRequest.TrySetResult(document.RootElement.Clone());
            await releaseFirstResponse.Task;
            var oldReply = Encoding.UTF8.GetBytes("{\"choices\":[{\"message\":{\"content\":\"stale-reply\"}}]}");
            first.Response.ContentType = "application/json";
            first.Response.ContentLength64 = oldReply.Length;
            await first.Response.OutputStream.WriteAsync(oldReply);
            first.Response.Close();

            var second = await listener.GetContextAsync();
            using (var document = await JsonDocument.ParseAsync(second.Request.InputStream))
                secondRequest.TrySetResult(document.RootElement.Clone());
            var newReply = Encoding.UTF8.GetBytes("{\"choices\":[{\"message\":{\"content\":\"fresh-reply\"}}]}");
            second.Response.ContentType = "application/json";
            second.Response.ContentLength64 = newReply.Length;
            await second.Response.OutputStream.WriteAsync(newReply);
            second.Response.Close();
        });
        await using var client = new MacLocalChat();
        var settings = new MacChatSettings { Backend = "api", Endpoint = $"http://127.0.0.1:{port}/v1", Model = "test-model", PersonalityPrompt = "" };
        var staleTurn = client.Send("before-reset", settings, null, CancellationToken.None);
        var firstBody = await firstRequest.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(firstBody.GetProperty("messages").GetArrayLength() == 1, "first reset-race request starts with no history");
        client.Reset();
        releaseFirstResponse.TrySetResult(true);
        bool staleTurnCancelled = false;
        try { await staleTurn; } catch (OperationCanceledException) { staleTurnCancelled = true; }
        Check(staleTurnCancelled, "in-flight reply is discarded after reset");
        Check(await client.Send("after-reset", settings, null, CancellationToken.None) == "fresh-reply", "new turn succeeds after reset-race response");
        await server;
        var secondMessages = (await secondRequest.Task).GetProperty("messages");
        Check(secondMessages.GetArrayLength() == 1 && secondMessages[0].GetProperty("content").GetString() == "after-reset",
            "in-flight response after reset is not re-added to local chat history");
    }

    static async Task VerifyResponsesApi()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var received = new List<(string Path, string? Authorization, JsonElement Body)>();
        var server = Task.Run(async () => {
            for (int turn = 1; turn <= 2; turn++)
            {
                var context = await listener.GetContextAsync();
                using var document = await JsonDocument.ParseAsync(context.Request.InputStream);
                received.Add((context.Request.Url!.AbsolutePath, context.Request.Headers["Authorization"], document.RootElement.Clone()));
                var payload = JsonSerializer.Serialize(new { output = new[] { new { type = "message", role = "assistant", content = new[] { new { type = "output_text", text = $"response-{turn}" } } } } });
                var bytes = Encoding.UTF8.GetBytes(payload);
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        });
        await using var client = new MacLocalChat();
        var settings = new MacChatSettings { Backend = "api", ApiProtocol = "responses", Endpoint = $"http://127.0.0.1:{port}/v1", Model = "custom", PersonalityPrompt = "你是 Responses 测试人格。" };
        Check(await client.Send("one", settings, "test-key", CancellationToken.None) == "response-1", "Responses API first turn");
        Check(await client.Send("two", settings, "test-key", CancellationToken.None) == "response-2", "Responses API second turn");
        await server;
        Check(received.All(x => x.Path == "/v1/responses"), "Responses API posts to configured base path");
        Check(received.All(x => x.Authorization == "Bearer test-key"), "Responses API sends configured bearer token");
        Check(received.All(x => x.Body.GetProperty("instructions").GetString() == settings.PersonalityPrompt), "Responses API sends configured personality instructions");
        Check(received[0].Body.GetProperty("input")[0].GetProperty("role").GetString() == "user", "Responses API uses input messages");
        Check(received[1].Body.GetProperty("input").GetArrayLength() == 3 &&
            received[1].Body.GetProperty("input")[1].GetProperty("role").GetString() == "assistant",
            "Responses API sends full conversation history");
    }

    static async Task VerifyHistoryBudget()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var received = new List<JsonElement>();
        const int turnCount = 6;
        var server = Task.Run(async () => {
            for (int turn = 0; turn < turnCount; turn++)
            {
                var context = await listener.GetContextAsync();
                using var document = await JsonDocument.ParseAsync(context.Request.InputStream);
                received.Add(document.RootElement.Clone());
                var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
                    choices = new[] { new { message = new { content = new string('r', 2500) } } }
                }));
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        });
        await using var client = new MacLocalChat();
        var settings = new MacChatSettings {
            Backend = "api", Endpoint = $"http://127.0.0.1:{port}/v1", Model = "test-model", PersonalityPrompt = ""
        };
        string latest = "";
        for (int turn = 0; turn < turnCount; turn++)
        {
            latest = $"turn-{turn}-" + new string('u', 3990);
            await client.Send(latest, settings, null, CancellationToken.None);
        }
        await server;
        var lastMessages = received[^1].GetProperty("messages");
        var requestCharacters = lastMessages.EnumerateArray().Sum(m => m.GetProperty("content").GetString()!.Length);
        Check(requestCharacters <= MacLocalChat.MaxHistoryCharacters, "local chat request stays within the configured context budget");
        Check(lastMessages.EnumerateArray().Any(m => m.GetProperty("content").GetString() == latest),
            "bounded local chat history always retains the latest user message");
    }

    static void VerifyAcpExecutableDiscovery()
    {
        var folder = Path.Combine(Path.GetTempPath(), "bbf-mac-acp-path-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try {
            foreach (var command in new[] { "opencode", "claude-agent-acp", "codex-acp" })
            {
                var executable = Path.Combine(folder, command);
                File.WriteAllText(executable, "#!/bin/sh\nexit 0\n");
                File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            }
            foreach (var (provider, command) in new[] {
                ("opencode", "opencode"), ("claude", "claude-agent-acp"), ("codex", "codex-acp")
            }) {
                Check(MacAcpExecutableLocator.Find(provider, folder, folder) == Path.Combine(folder, command),
                    $"ACP {provider} executable discovered on PATH");
                Check(MacAcpExecutableLocator.Resolve(provider, "/custom/agent", folder, folder) == "/custom/agent",
                    $"ACP {provider} manual path remains selected");
            }
        } finally { Directory.Delete(folder, true); }
    }
}
