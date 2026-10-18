using System.Diagnostics;
using System.Text.Json;
using BigBlueFish.Control;

int checks = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks++; }
var pipe = "BigBlueFish.Verification." + Guid.NewGuid().ToString("N");
using var server = new ControlServer((name, args, token) => Task.FromResult(ControlTools.Text(new { name, args })), pipe);
var bridge = Path.GetFullPath(args[0]);
Process Start() => Process.Start(new ProcessStartInfo(bridge) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
    ArgumentList = { "--pipe", pipe } })!;
using var process = Start();
var notices = new List<JsonElement>();
async Task<JsonElement> Request(int id, string method, object? parameters = null)
{
    await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters ?? new { } }));
    using var timeout = new CancellationTokenSource(10000);
    while (true)
    {
        var line = await process.StandardOutput.ReadLineAsync(timeout.Token) ?? throw new IOException("Bridge exited: " + await process.StandardError.ReadToEndAsync());
        using var doc = JsonDocument.Parse(line); var value = doc.RootElement.Clone();
        if (value.TryGetProperty("id", out var found) && found.ValueKind == JsonValueKind.Number && found.GetInt32() == id) return value;
        notices.Add(value);
    }
}
var response = await Request(1, "initialize", new { protocolVersion = "2025-11-25", capabilities = new { }, clientInfo = new { name = "verification", version = "1" } });
Check(response.GetProperty("result").GetProperty("protocolVersion").GetString() == "2025-11-25", "Protocol handshake");
Check(response.GetProperty("result").GetProperty("instructions").GetString()!.Contains("user_message"), "Handshake explains input event handling");
await process.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
response = await Request(2, "tools/list");
var tools = response.GetProperty("result").GetProperty("tools").EnumerateArray().ToArray();
Check(tools.Length == 9, "Nine tools");
foreach (var tool in tools) Check(tool.GetProperty("inputSchema").GetProperty("type").GetString() == "object", "Object schema");
var settingSchema = tools.Single(t => t.GetProperty("name").GetString() == "update_settings").GetProperty("inputSchema").GetProperty("properties").GetProperty("settings");
Check(!settingSchema.GetProperty("additionalProperties").GetBoolean(), "Settings schema rejects undocumented fields");
var settingProperties = settingSchema.GetProperty("properties");
Check(settingProperties.EnumerateObject().Select(p => p.Name).Order().SequenceEqual(ControlTools.SettingNames.Order()), "Every writable setting is documented exactly once");
Check(!settingProperties.TryGetProperty("McpEnabled", out _), "User-only MCP switch absent from writable schema");
foreach (var property in settingProperties.EnumerateObject()) Check(!string.IsNullOrWhiteSpace(property.Value.GetProperty("description").GetString()), "Setting description present: " + property.Name);
var interval = settingProperties.GetProperty("AnimationIntervalSeconds");
Check(interval.GetProperty("type").GetString()=="integer" && interval.GetProperty("minimum").GetInt32()==30 && interval.GetProperty("maximum").GetInt32()==300, "Animation interval schema matches runtime bounds");
Check(settingProperties.GetProperty("SpeechMinMinutes").GetProperty("enum").EnumerateArray().Select(v=>v.GetInt32()).SequenceEqual(new[]{2,3,5,10,15,20}), "Speech range enum documented");
foreach (var name in new[]{"Left","Top","LastBrushDate"}) Check(settingProperties.GetProperty(name).GetProperty("type").EnumerateArray().Any(t=>t.GetString()=="null"),"Nullable setting accurately documented: " + name);
response = await Request(3, "tools/call", new { name = "say", arguments = new { text = "主人好~原文含有英文、换行\n和标点。" } });
Check(response.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!.Contains("say"), "Tool result structure");
await Request(4, "resources/subscribe", new { uri = "bigbluefish://interactions" });
server.Publish("click", new { button = "left", x = 20, y = 30 });
response = await Request(5, "resources/read", new { uri = "bigbluefish://interactions" });
Check(response.GetProperty("result").GetProperty("contents")[0].GetProperty("text").GetString()!.Contains("click"), "Readable interaction history");
Check(notices.Any(n => n.TryGetProperty("method", out var m) && m.GetString() == "notifications/message"), "Interaction pushed without polling");
Check(notices.Any(n => n.TryGetProperty("method", out var m) && m.GetString() == "notifications/resources/updated"), "Subscription pushed");
response = await Request(6, "tools/call", new { name = "unknown", arguments = new { } });
Check(response.GetProperty("result").GetProperty("isError").GetBoolean(), "Unknown tool returns tool error");
response = await Request(7, "resources/read", new { uri = "other://wrong" });
Check(response.GetProperty("error").GetProperty("code").GetInt32() == -32602, "Unknown resource rejected");
response = await Request(8, "logging/setLevel", new { level = "not-a-level" });
Check(response.TryGetProperty("error", out _), "Invalid log level rejected");
response = await Request(9, "ping"); Check(response.TryGetProperty("result", out _), "Ping after errors");
await Request(10,"logging/setLevel",new{level="warning"}); int beforeNotices=notices.Count;
server.Publish("user_message",new{text="原文\n第二行<> &"});
response=await Request(11,"resources/read",new{uri="bigbluefish://interactions"});
var records=JsonDocument.Parse(response.GetProperty("result").GetProperty("contents")[0].GetProperty("text").GetString()!).RootElement;
Check(records[records.GetArrayLength()-1].GetProperty("detail").GetProperty("text").GetString()=="原文\n第二行<> &","Input resource contains literal submitted text");
Check(!notices.Skip(beforeNotices).Any(n=>n.TryGetProperty("method",out var m)&&m.GetString()=="notifications/message"),"Warning log level filters info interaction notifications");
Check(notices.Skip(beforeNotices).Any(n=>n.TryGetProperty("method",out var m)&&m.GetString()=="notifications/resources/updated"),"Resource subscription still works with info logs filtered");
Check(server.Connections == 1, "Connection tracked");
process.StandardInput.Close();
await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
Check(process.ExitCode == 0, "Bridge exits at stdin EOF");
for (int i = 0; i < 50 && server.Connections != 0; i++) await Task.Delay(20);
Check(server.Connections == 0, "Connection released");
using var second = Start();
await second.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-03-26\"}}");
var secondLine = await second.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
Check(secondLine!.Contains("2025-03-26"), "Reconnect and older protocol negotiation");
server.Dispose();
await second.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
Check(second.HasExited, "Disabling service disconnects bridge");
Console.WriteLine($"MCP protocol: {checks} checks passed.");
