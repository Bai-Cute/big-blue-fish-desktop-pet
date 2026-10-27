using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using BigBlueFish.Control;

internal static class SamplingVerification
{
    internal static async Task<int> Run()
    {
        int checks = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; }
        var executed = new ConcurrentQueue<string>();
        string pipe = "BigBlueFish.Sampling." + Guid.NewGuid().ToString("N");
        using var server = new ControlServer((name, args, token) => {
            token.ThrowIfCancellationRequested(); executed.Enqueue(name + ":" + args.GetRawText());
            return Task.FromResult(ControlTools.Text(new { accepted = true }));
        }, pipe, TimeSpan.FromMilliseconds(500));
        using var stream = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
        await stream.ConnectAsync(5000);
        using var reader = new StreamReader(stream, new UTF8Encoding(false), false, 4096, true);
        var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
        async Task Send(object value) => await writer.WriteLineAsync(JsonSerializer.Serialize(value));
        async Task<JsonElement> Read() {
            using var timeout = new CancellationTokenSource(5000);
            var line = await reader.ReadLineAsync(timeout.Token) ?? throw new IOException("Sampling connection closed");
            return JsonSerializer.Deserialize<JsonElement>(line);
        }
        async Task<JsonElement> Method(string method) {
            while (true) { var value = await Read(); if (value.TryGetProperty("method", out var found) && found.GetString() == method) return value; }
        }
        async Task Wait(Func<bool> ready) {
            var until = DateTime.UtcNow.AddSeconds(5);
            while (!ready() && DateTime.UtcNow < until) await Task.Delay(10);
            Check(ready(), "Sampling work completed within timeout");
        }
        await Send(new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { protocolVersion = "2025-11-25", capabilities = new { sampling = new { tools = new { } } } } });
        Check((await Read()).TryGetProperty("result", out _), "Sampling capability negotiated");
        await Send(new { jsonrpc = "2.0", method = "notifications/initialized" });
        await Wait(() => server.SamplingConnections == 1);
        server.Publish("user_message", new { text = "off" }, true);
        Check((await Read()).GetProperty("method").GetString() == "notifications/message", "Master off preserves event notification");
        await Send(new { jsonrpc = "2.0", id = 2, method = "ping" });
        Check((await Read()).GetProperty("id").GetInt32() == 2, "Master off produces no sampling request");
        server.SetWakeEnabled(true);
        server.Publish("user_message", new { text = "hello" }, true);
        var request = await Method("sampling/createMessage");
        Check(request.GetProperty("params").GetProperty("messages")[0].GetProperty("content").GetProperty("text").GetString()!.Contains("hello"), "Literal input reaches sampling client");
        Check(request.GetProperty("params").GetProperty("tools").GetArrayLength() == 9, "Tool capability supplies nine tools");
        await Send(new { jsonrpc = "2.0", id = request.GetProperty("id"), result = new { role = "assistant", model = "fixture", content = new { type = "text", text = "主人你好~" }, stopReason = "endTurn" } });
        await Wait(() => executed.Any(x => x.Contains("主人你好~") || x.Contains("say:")));
        Check(executed.Count == 1, "Final text displayed exactly once");
        server.Publish("click", new { }, true);
        request = await Method("sampling/createMessage");
        await Send(new { jsonrpc = "2.0", id = request.GetProperty("id"), result = new { role = "assistant", model = "fixture", content = new[] { new { type = "tool_use", id = "say-1", name = "say", input = new { text = "工具说话" } } }, stopReason = "toolUse" } });
        request = await Method("sampling/createMessage");
        var messages = request.GetProperty("params").GetProperty("messages");
        Check(messages.GetArrayLength() == 3 && messages[2].GetProperty("content")[0].GetProperty("type").GetString() == "tool_result", "Executed tool result returned to client");
        await Send(new { jsonrpc = "2.0", id = request.GetProperty("id"), result = new { role = "assistant", model = "fixture", content = new { type = "text", text = "duplicate" } } });
        await Wait(() => server.WakeStatus == "已完成");
        Check(executed.Count == 2, "Tool say suppresses duplicate final bubble");
        server.Publish("click", new { }, true); request = await Method("sampling/createMessage");
        await Send(new { jsonrpc = "2.0", id = request.GetProperty("id"), error = new { code = -1, message = "user rejected" } });
        await Wait(() => server.WakeStatus.Contains("user rejected"));
        Check(executed.Count == 2, "Rejected sampling does not speak");
        server.Publish("click", new { }, true); request = await Method("sampling/createMessage");
        var cancelled = await Method("notifications/cancelled");
        Check(cancelled.GetProperty("params").GetProperty("requestId").GetString() == request.GetProperty("id").GetString(), "Timeout sends matching cancellation");
        await Wait(() => server.WakeStatus.Contains("超时"));
        server.Publish("click", new { }, true); request = await Method("sampling/createMessage");
        await Send(new { jsonrpc = "2.0", method = "notifications/cancelled", @params = new { requestId = request.GetProperty("id") } });
        await Wait(() => server.WakeStatus == "请求已取消");
        server.Publish("click", new { }, true); request = await Method("sampling/createMessage");
        server.SetWakeEnabled(false);
        await Method("notifications/cancelled");
        await Wait(() => server.WakeStatus == "请求已取消");
        await Send(new { jsonrpc = "2.0", id = request.GetProperty("id"), result = new { content = new { type = "text", text = "stale" } } });
        await Send(new { jsonrpc = "2.0", id = 3, method = "ping" });
        Check((await Read()).GetProperty("id").GetInt32() == 3 && executed.Count == 2, "Late response after disabling cannot speak");
        server.SetWakeEnabled(true); server.Publish("click", new { }, true); await Method("sampling/createMessage");
        writer.Dispose(); reader.Dispose(); stream.Dispose();
        await Wait(() => server.Connections == 0);
        Check(executed.Count == 2, "Disconnect cancels outstanding sampling");

        using var plain = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
        await plain.ConnectAsync(5000);
        using var plainReader = new StreamReader(plain);
        using var plainWriter = new StreamWriter(plain) { AutoFlush = true };
        await plainWriter.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"capabilities\":{}}}");
        await plainReader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await plainWriter.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
        await Wait(() => server.Connections == 1);
        server.Publish("user_message", new { text = "plain" }, true);
        await plainReader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await plainWriter.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"ping\"}");
        var pong = JsonSerializer.Deserialize<JsonElement>((await plainReader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)))!);
        Check(pong.GetProperty("id").GetInt32() == 2 && server.SamplingConnections == 0, "Clients without sampling retain ordinary event transport only");
        return checks;
    }
}
