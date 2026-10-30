using System.Text.Json;
using BigBlueFish.Agent;
using BigBlueFish.Control;

static class LiveProbe
{
    public static async Task<int> Run(string id, string root, bool prompt, bool configure = false)
    {
        root = Path.GetFullPath(root); Directory.CreateDirectory(root);
        string pipe = "BigBlueFish.LiveProbe." + Guid.NewGuid().ToString("N");
        string bridge = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../BigBlueFish.Mcp/bin/Debug/net10.0/BigBlueFish.Mcp.exe"));
        AgentCoordinator? host = null; int said = 0, guide = 0;
        using var server = new ControlServer((name, args, token) => {
            if (name == "say") { said++; host?.MarkSpoken(); Console.WriteLine("TOOL say: " + args.GetProperty("text").GetString()); return Task.FromResult(ControlTools.Text(new { displayed = true })); }
            if (name == "get_settings") return Task.FromResult(ControlTools.Text(new { settings = new { PuppetMode = false, AgentDailySpeech = false }, state = new { visible = true, action = "待机呼吸休闲" } }));
            if (name == "get_operation_guide") guide++;
            return Task.FromResult(ControlTools.Text(host!.ExecuteTool(name, args)));
        }, pipe);
        host = new AgentCoordinator(root, bridge, pipe, (permission, token) => {
            // Test workspace reads only. No blanket approval of shell execution or external writes.
            bool read = permission.Kind == "read" || permission.Title.Contains("read", StringComparison.OrdinalIgnoreCase) || permission.Title.Contains("读取") || permission.Title.Contains("get_operation_guide") || permission.Title.Contains("say")
                || permission.Title.Contains("\"text\"") && permission.Title.Contains("鱼鱼连上啦");
            string? choice = read ? permission.Options.FirstOrDefault(x => x.Kind == "allow_once")?.Id : null;
            Console.WriteLine("PERMISSION " + permission.Title + ": " + (choice ?? "cancelled"));
            AtomicText.Write(Path.Combine(root, "last-permission.json"), permission.Detail);
            return Task.FromResult(choice);
        }, Path.Combine(root, "workspace"));
        int Finish(int code, string summary)
        {
            AtomicText.Write(Path.Combine(root, "verification.json"), JsonSerializer.Serialize(new {
                harness = id, verifiedAt = DateTimeOffset.Now, exitCode = code, summary,
                executable = host.ExecutablePath, version = host.Version, sessionReady = host.Connected,
                toolReplies = said, guideToolCalls = guide, generationRequested = prompt, configuration = host.Configuration.Options, workspace = host.Workspaces.Current
            }, new JsonSerializerOptions { WriteIndented = true }));
            return code;
        }
        using (host)
        {
            host.ProtocolEvent += (method, data) => {
                if (data.TryGetProperty("update", out var update)) {
                    string kind = update.TryGetProperty("sessionUpdate", out var k) ? k.GetString() ?? "" : "";
                    if (kind == "tool_call" && update.TryGetProperty("title", out var title)) Console.WriteLine("TOOL ACTIVITY " + title.GetString());
                    if (kind == "notice") Console.WriteLine("NOTICE " + update.GetRawText()[..Math.Min(500, update.GetRawText().Length)]);
                }
            };
            string previous = "";
            host.Changed += () => {
                string message = host.Status + " " + host.Detail;
                if (message == previous) return; previous = message;
                Console.WriteLine(message);
            };
            host.Configure(id, new(true, false, false));
            var limit = DateTime.UtcNow.AddMinutes(6);
            while (!host.Connected && !host.Status.Contains("失败") && !host.Status.StartsWith("需要登录") && DateTime.UtcNow < limit) await Task.Delay(100);
            if (!host.Connected) { Console.WriteLine("RESULT unavailable or authentication required; not a successful compatibility test."); return Finish(3, host.Status); }
            Console.WriteLine("ACP connected; version=" + host.Version + "; restores=" + host.RestoresSessions);
            if (configure)
            {
                var controls = host.Configuration.Options;
                foreach (var option in controls)
                {
                    if (option.Type != "boolean" && !option.Choices.Any(c => c.Value == option.CurrentValue)) continue;
                    await host.SetConfiguration(option.Id, option.CurrentValue);
                    Console.WriteLine("CONFIG ACK " + option.Name + " = " + host.Configuration.Options.FirstOrDefault(o => o.Id == option.Id)?.CurrentValue);
                }
                Console.WriteLine("CONFIG selectors verified=" + controls.Length);
            }
            if (!prompt) { Console.WriteLine("RESULT ACP session handshake verified; generation not tested."); return Finish(0, "ACP session handshake only"); }
            host.SendUser("这是蓝色大肥鱼联调测试。请使用big-blue-fish技能，读取操作说明和人设，仅调用say工具显示一句“主人，鱼鱼连上啦~”，然后结束本轮。");
            limit = DateTime.UtcNow.AddMinutes(3);
            while (DateTime.UtcNow < limit && said == 0 && !host.Status.Contains("失败") && !host.Status.StartsWith("需要登录") && !host.Detail.StartsWith("本轮")) await Task.Delay(100);
            if (said == 0) { Console.WriteLine("RESULT no say tool call; real reply path not verified. guide calls=" + guide); return Finish(4, "No say tool call"); }
            while (host.Busy && DateTime.UtcNow < limit) await Task.Delay(100);
            Console.WriteLine("RESULT real Agent tool-only reply verified; guide calls=" + guide); return Finish(0, "Real Agent say tool reply");
        }
    }
}
