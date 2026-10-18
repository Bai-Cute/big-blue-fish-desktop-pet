using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace BigBlueFish.Control;

// Native local transport, exposed to MCP clients by the bundled stdio bridge.
public sealed class ControlServer : IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly ConcurrentDictionary<long, Session> sessions = new();
    private readonly Func<string, JsonElement, CancellationToken, Task<object>> execute;
    private readonly ConcurrentQueue<object> recent = new();
    private readonly string pipe;
    private long nextSession, sequence;
    public string? LastConnectionError { get; private set; }
    public int Connections => sessions.Values.Count(s => s.Initialized && !s.Closed);
    public ControlServer(Func<string, JsonElement, CancellationToken, Task<object>> execute, string? pipeName = null)
    { this.execute = execute; pipe = pipeName ?? ControlTools.PipeName; _ = Task.Run(Accept); }
    private async Task Accept()
    {
        while (!lifetime.IsCancellationRequested)
        {
            NamedPipeServerStream? stream = null;
            try
            {
                stream = new(pipe, PipeDirection.InOut, 16, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await stream.WaitForConnectionAsync(lifetime.Token);
                var id = Interlocked.Increment(ref nextSession);
                var session = new Session(this, stream, lifetime.Token); stream = null;
                sessions[id] = session;
                _ = RunSession(id, session);
            }
            catch (OperationCanceledException) { break; }
            catch (IOException) { if (!lifetime.IsCancellationRequested) await Task.Delay(100, lifetime.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing); }
            finally { stream?.Dispose(); }
        }
    }
    private async Task RunSession(long id, Session session)
    {
        try { await session.Run(); }
        catch (OperationCanceledException) { }
        catch (Exception error) { LastConnectionError = error.GetType().Name + ": " + error.Message; }
        finally { sessions.TryRemove(id, out _); session.Dispose(); }
    }
    public void Publish(string kind, object detail)
    {
        var message = new { sequence = Interlocked.Increment(ref sequence), time = DateTimeOffset.UtcNow, kind, detail };
        recent.Enqueue(message); while (recent.Count > 128) recent.TryDequeue(out _);
        foreach (var session in sessions.Values) session.Publish(message);
    }
    public void Dispose()
    {
        if (lifetime.IsCancellationRequested) return;
        lifetime.Cancel(); foreach (var session in sessions.Values) session.Dispose();
    }
    private sealed class Session : IDisposable
    {
        private readonly ControlServer owner;
        private readonly Stream stream;
        private readonly CancellationTokenSource stop;
        private readonly Channel<string> output = Channel.CreateBounded<string>(new BoundedChannelOptions(128) { SingleReader = true });
        private readonly ConcurrentDictionary<string, CancellationTokenSource> requests = new();
        public volatile bool Initialized;
        private int disposed;
        private bool negotiated, subscribed;
        private int logLevel = 1;
        public bool Closed => stop.IsCancellationRequested;
        public Session(ControlServer owner, Stream stream, CancellationToken token) { this.owner = owner; this.stream = stream; stop = CancellationTokenSource.CreateLinkedTokenSource(token); }
        private void Send(object value)
        {
            if (!output.Writer.TryWrite(JsonSerializer.Serialize(value))) Dispose(); // Disconnect a stalled reader; never silently lose an interaction.
        }
        private void Result(JsonElement id, object value) => Send(new { jsonrpc = "2.0", id, result = value });
        private void Error(JsonElement? id, int code, string message) => Send(new { jsonrpc = "2.0", id, error = new { code, message } });
        public void Publish(object message)
        {
            if (!Initialized || Closed) return;
            if (logLevel <= 1) Send(new { jsonrpc = "2.0", method = "notifications/message", @params = new { level = "info", logger = "bigbluefish.interaction", data = message } });
            if (subscribed) Send(new { jsonrpc = "2.0", method = "notifications/resources/updated", @params = new { uri = "bigbluefish://interactions" } });
        }
        public async Task Run()
        {
            var writer = Write();
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), false, 4096, true);
            var character = new char[1];
            try
            {
                while (!Closed)
                {
                    // Input parameters never contain screenshot bytes. Limit unframed input before allocating a whole line.
                    var line = new StringBuilder();
                    while (true)
                    {
                        if (await reader.ReadAsync(character.AsMemory(), stop.Token) == 0) return;
                        int ch = character[0];
                        if (ch == '\n') break;
                        if (line.Length >= 1024 * 1024) throw new IOException("MCP 请求过大。");
                        line.Append((char)ch);
                    }
                    if (line.Length == 0) continue;
                    JsonElement request;
                    try { using var doc = JsonDocument.Parse(line.ToString()); request = doc.RootElement.Clone(); }
                    catch (JsonException) { Error(null, -32700, "Invalid JSON"); continue; }
                    try { await Handle(request); }
                    catch (Exception error) when (error is InvalidOperationException or KeyNotFoundException or FormatException)
                    { Error(request.TryGetProperty("id", out var requestId) ? requestId : null, -32602, "Invalid params"); }
                }
            }
            finally { Dispose(); await writer.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing); }
        }
        private async Task Write()
        {
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
            try
            {
                await foreach (var line in output.Reader.ReadAllAsync(stop.Token))
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); timeout.CancelAfter(TimeSpan.FromSeconds(10));
                    await writer.WriteLineAsync(line.AsMemory(), timeout.Token);
                }
            }
            catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException)
            { if (!Closed) owner.LastConnectionError = error.GetType().Name + ": " + error.Message; }
            finally { Dispose(); }
        }
        private Task Handle(JsonElement request)
        {
            if (request.ValueKind != JsonValueKind.Object || !request.TryGetProperty("method", out var methodValue) || methodValue.ValueKind != JsonValueKind.String)
            { Error(null, -32600, "Invalid Request"); return Task.CompletedTask; }
            var method = methodValue.GetString()!;
            var hasId = request.TryGetProperty("id", out var id);
            var args = request.TryGetProperty("params", out var p) ? p : JsonSerializer.SerializeToElement(new { });
            if (!hasId)
            {
                if (method == "notifications/initialized" && negotiated) Initialized = true;
                if (method == "notifications/cancelled" && args.TryGetProperty("requestId", out var cancelled) && requests.TryGetValue(cancelled.ToString(), out var source)) source.Cancel();
                return Task.CompletedTask;
            }
            if (method == "initialize")
            {
                negotiated = true;
                var version = args.TryGetProperty("protocolVersion", out var requested) && requested.GetString() == "2025-03-26" ? "2025-03-26" : "2025-11-25";
                Result(id, new { protocolVersion = version, capabilities = new { tools = new { listChanged = false }, logging = new { }, resources = new { subscribe = true, listChanged = false } },
                    serverInfo = new { name = "BigBlueFish", version = typeof(ControlServer).Assembly.GetName().Version!.ToString(3) },
                    instructions = "控制用户本机的蓝色大肥鱼。先读get_settings与工具schema；AnimationIntervalSeconds只接受30–300整数秒，SpeechMinMinutes是枚举档位。say/say_next显示你提供的原文，不能把成功回执当作已读或播放完成。点击、拖动及双击输入的user_message（detail.text为原文）通过notifications/message推送，logger为bigbluefish.interaction、level为info；需接收info日志，或订阅并读取bigbluefish://interactions。客户端自行安排收到事件后的模型调用，本服务不会自动启动你的Agent轮次。资源只保留最近128条内存事件，按sequence去重，断线不保证补齐。截图会把屏幕内容交给当前客户端；ForegroundEnabled仅控制内置读屏，不限制MCP截图。开启PuppetMode需真实用户确认，等待期间其他控制与拖动可继续；模式在断开后保持，需显式关闭才能恢复自主功能。修改设置不会打开或激活设置窗口。多个客户端共享状态和队列，没有独占锁。" });
                return Task.CompletedTask;
            }
            if (method == "ping") { Result(id, new { }); return Task.CompletedTask; }
            if (!Initialized) { Error(id, -32002, "MCP 尚未初始化。"); return Task.CompletedTask; }
            switch (method)
            {
                case "tools/list": Result(id, new { tools = ControlTools.Definitions }); break;
                case "resources/list": Result(id, new { resources = new[] { new { uri = "bigbluefish://interactions", name = "用户交互与输入", mimeType = "application/json" } } }); break;
                case "resources/read":
                    if (!args.TryGetProperty("uri", out var uri) || uri.GetString() != "bigbluefish://interactions") Error(id, -32602, "未知资源。");
                    else Result(id, new { contents = new[] { new { uri = "bigbluefish://interactions", mimeType = "application/json", text = JsonSerializer.Serialize(owner.recent.ToArray()) } } });
                    break;
                case "resources/subscribe": case "resources/unsubscribe":
                    if (!args.TryGetProperty("uri", out var subscription) || subscription.GetString() != "bigbluefish://interactions") Error(id, -32602, "未知资源。");
                    else { subscribed = method.EndsWith("/subscribe", StringComparison.Ordinal); Result(id, new { }); }
                    break;
                case "logging/setLevel":
                    string[] levels = ["debug", "info", "notice", "warning", "error", "critical", "alert", "emergency"];
                    var level = args.TryGetProperty("level", out var l) ? Array.IndexOf(levels, l.GetString()) : -1;
                    if (level < 0) Error(id, -32602, "未知日志级别。"); else { logLevel = level; Result(id, new { }); }
                    break;
                case "tools/call":
                    _ = Call(id.Clone(), args.Clone()); break;
                default: Error(id, -32601, "Method not found"); break;
            }
            return Task.CompletedTask;
        }
        private async Task Call(JsonElement id, JsonElement args)
        {
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            if (!requests.TryAdd(id.ToString(), cancel)) { Error(id, -32600, "重复的请求编号。"); return; }
            try
            {
                var name = args.GetProperty("name").GetString()!;
                if (!ControlTools.Definitions.Any(t => JsonSerializer.SerializeToElement(t).GetProperty("name").GetString() == name)) throw new ArgumentException("未知工具。");
                var parameters = args.TryGetProperty("arguments", out var a) ? a : JsonSerializer.SerializeToElement(new { });
                Result(id, await owner.execute(name, parameters, cancel.Token));
            }
            catch (OperationCanceledException) { Result(id, ControlTools.Error("操作已取消。")); }
            catch (Exception e) { Result(id, ControlTools.Error(e.Message)); }
            finally { requests.TryRemove(id.ToString(), out _); }
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            stop.Cancel(); output.Writer.TryComplete(); foreach (var source in requests.Values) source.Cancel(); stream.Dispose();
        }
    }
}
