using BigBlueFish.Windows;
using BigBlueFish.Agent;
using System.Text.Json;
using System.Diagnostics;

const string fixturePersonality = "你是大肥鱼测试伙伴。";
if (args.Contains("--bubble-layout")) { MacBubbleLayoutVerification.Run(); return; }
if (args.Contains("--acp-fixture")) {
    while (Console.ReadLine() is { } line) {
        using var document = JsonDocument.Parse(line); var message = document.RootElement;
        if (!message.TryGetProperty("method", out var method)) continue;
        var id = message.TryGetProperty("id", out var requestId) ? requestId.Clone() : default;
        object result = new { };
        switch (method.GetString()) {
            case "initialize": result = new { protocolVersion = 1, agentCapabilities = new { }, agentInfo = new { name = "fixture", version = "1" } }; break;
            case "session/new": result = new { sessionId = "mac-fixture" }; break;
            case "session/prompt":
                var promptText = message.GetProperty("params").GetProperty("prompt")[0].GetProperty("text").GetString() ?? "";
                var includesUserMessage = promptText.EndsWith("用户消息：\nhello", StringComparison.Ordinal) ||
                    promptText.EndsWith("用户消息：\nagain", StringComparison.Ordinal);
                if (!promptText.Contains(fixturePersonality, StringComparison.Ordinal) || !includesUserMessage) {
                    Console.WriteLine(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, error = new { code = -32602, message = "ACP prompt omitted personality or original message." } }));
                    continue;
                }
                Console.WriteLine(JsonSerializer.Serialize(new { jsonrpc = "2.0", method = "session/update", @params = new { sessionId = "wrong-session", update = new { sessionUpdate = "agent_message_chunk", content = new { type = "text", text = "IGNORE" } } } }));
                foreach (var part in new[] { "你好", " Mac" }) Console.WriteLine(JsonSerializer.Serialize(new { jsonrpc = "2.0", method = "session/update", @params = new { sessionId = "mac-fixture", update = new { sessionUpdate = "agent_message_chunk", content = new { type = "text", text = part } } } }));
                result = new { stopReason = "end_turn" }; break;
        }
        if (id.ValueKind != JsonValueKind.Undefined) Console.WriteLine(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }));
    }
    return;
}

static void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS: " + name); }
var pixels = new byte[640 * 360 * 4];
MacChatScrollStateVerification.Run();
MacChatRequestQueueVerification.Run();
MacChatEntryVerification.Run();
MacBubbleReplyPolicyVerification.Run();
pixels[(20 * 640 + 10) * 4 + 3] = 255;
var alpha = new byte[640 * 360];
Check(MacHitTest.CopyAlpha(pixels, alpha), "valid frame alpha copied");
Check(MacHitTest.Contains(alpha, 10.5, 20.5, 640, 360, false), "opaque pixel");
Check(!MacHitTest.Contains(alpha, 11.5, 20.5, 640, 360, false), "transparent pixel");
Check(MacHitTest.Contains(alpha, 629.5, 20.5, 640, 360, true), "mirrored pixel");
Check(MacHitTest.Contains(alpha, 5.25, 10.25, 320, 180, false), "scaled coordinates");
foreach (var point in new[] { (-1d, 20d), (640d, 20d), (10d, 360d), (double.NaN, 0d) })
    Check(!MacHitTest.Contains(alpha, point.Item1, point.Item2, 640, 360, false), "outside or invalid coordinate");
Check(!MacHitTest.Contains([], 0, 0, 640, 360, false), "missing frame");
Check(!MacHitTest.Contains(alpha, 0, 0, 0, 360, false), "invalid window size");
pixels[(20 * 640 + 10) * 4 + 3] = 7;
var nextAlpha = new byte[640 * 360];
Check(MacHitTest.CopyAlpha(pixels, nextAlpha) && !MacHitTest.Contains(nextAlpha, 10.5, 20.5, 640, 360, false), "transparent threshold");
Check(MacHitTest.Contains(alpha, 10.5, 20.5, 640, 360, false), "hit-test snapshot is independent of reused decoder frame");
Check(!MacHitTest.CopyAlpha([], alpha), "invalid decoder frame is rejected");
var original = Environment.CurrentDirectory;
var directory = Path.Combine(Path.GetTempPath(), "bbf-mac-verification-" + Guid.NewGuid());
Directory.CreateDirectory(directory);
try {
    Environment.CurrentDirectory = directory;
    var settings = new CompanionPreferences { Scale = 1.25, Left = 321, Top = 123 }; settings.Save();
    var restored = CompanionPreferences.Read();
    Check(restored.Scale == 1.25 && restored.Left == 321 && restored.Top == 123, "size and position persist");
    settings.Scale = 9; settings.Save();
    Check(CompanionPreferences.Read().Scale == 2, "restored scale bounded");
    File.WriteAllText("preferences.json", "broken");
    Check(CompanionPreferences.Read().Scale == 1.25, "backup recovers damaged preferences");
} finally { Environment.CurrentDirectory = original; Directory.Delete(directory, true); }

var workspace = Path.Combine(Path.GetTempPath(), "bbf-mac-acp-" + Guid.NewGuid());
Directory.CreateDirectory(workspace);
try {
    var executable = Environment.ProcessPath!;
    var arguments = new List<string>();
    if (Path.GetFileNameWithoutExtension(executable) == "dotnet") arguments.Add(typeof(MacAgentSession).Assembly.Location);
    arguments.Add("--acp-fixture");
    await using var session = new MacAgentSession();
    await session.Connect(AgentProcess.StartInfo(executable, arguments, workspace), workspace, (_, _) => Task.FromResult<string?>(null), CancellationToken.None);
    Check(session.Connected && session.SessionId == "mac-fixture", "ACP real subprocess handshake");
    Check(await session.Send("hello", CancellationToken.None, fixturePersonality) == "你好 Mac", "ACP prompt delivers configurable personality and original message");
    Check(await session.Send("again", CancellationToken.None, fixturePersonality) == "你好 Mac", "ACP multi-turn session continues to deliver personality");
    bool invalid = false; try { await session.Send(new string('x', 4001), CancellationToken.None); } catch (ArgumentException) { invalid = true; }
    Check(invalid, "oversized prompt rejected");
    await session.DisposeAsync();
    Check(!session.Connected, "ACP disconnect clears connection");
} finally { Directory.Delete(workspace, true); }

await MacLocalChatVerification.Run();
await MacModelDownloadVerification.Run();
var bubbleExecutable = Environment.ProcessPath!;
var bubbleArguments = new List<string>();
if (Path.GetFileNameWithoutExtension(bubbleExecutable) == "dotnet") bubbleArguments.Add(typeof(MacBubbleLayoutVerification).Assembly.Location);
bubbleArguments.Add("--bubble-layout");
var bubbleStart = new ProcessStartInfo(bubbleExecutable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
foreach (var argument in bubbleArguments) bubbleStart.ArgumentList.Add(argument);
using var bubbleTest = Process.Start(bubbleStart) ?? throw new InvalidOperationException("Could not start bubble layout verification.");
var bubbleOutput = bubbleTest.StandardOutput.ReadToEndAsync();
var bubbleError = bubbleTest.StandardError.ReadToEndAsync();
await bubbleTest.WaitForExitAsync();
Console.Write(await bubbleOutput);
if (bubbleTest.ExitCode != 0) throw new Exception(await bubbleError);
