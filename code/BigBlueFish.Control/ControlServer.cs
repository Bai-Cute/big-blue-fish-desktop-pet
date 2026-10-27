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
    private volatile bool wakeEnabled;
    private readonly TimeSpan samplingTimeout;
    public int SamplingConnections => sessions.Values.Count(s => s.Initialized && !s.Closed && s.Sampling);
    public string WakeStatus { get; private set; } = "尚未请求";
    public void SetWakeEnabled(bool enabled)
    {
        wakeEnabled = enabled;
        if (!enabled) foreach (var session in sessions.Values) session.CancelSampling();
    }
    public int Connections => sessions.Values.Count(s => s.Initialized && !s.Closed);
    public ControlServer(Func<string, JsonElement, CancellationToken, Task<object>> execute, string? pipeName = null, TimeSpan? samplingTimeout = null)
    { this.execute = execute; this.samplingTimeout = samplingTimeout ?? TimeSpan.FromSeconds(60); pipe = pipeName ?? ControlTools.PipeName; _ = Task.Run(Accept); }
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
    public void Publish(string kind, object detail, bool wake = false)
    {
        var message = new { sequence = Interlocked.Increment(ref sequence), time = DateTimeOffset.UtcNow, kind, detail };
        recent.Enqueue(message); while (recent.Count > 128) recent.TryDequeue(out _);
        foreach (var session in sessions.Values) {
            session.Publish(message);
            if (wake && wakeEnabled) session.Wake(message);
        }
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
        public bool Sampling { get; private set; }
        private bool samplingTools;
        private long nextOutbound;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> outbound = new();
        private CancellationTokenSource? wakeCancellation;
        private readonly object wakeGate = new();
        private readonly Channel<object> wakeQueue = Channel.CreateBounded<object>(new BoundedChannelOptions(16) { SingleReader = false });

        public void CancelSampling()
        {
            lock (wakeGate) wakeCancellation?.Cancel();
            while (wakeQueue.Reader.TryRead(out _)) { }
        }
        public void Wake(object message)
        {
            if (!Initialized || Closed || !Sampling) return;
            if (!wakeQueue.Writer.TryWrite(message)) owner.WakeStatus = "触发队列已满，本次未请求";
        }
        private async Task WakeLoop()
        {
            try {
                await foreach (var message in wakeQueue.Reader.ReadAllAsync(stop.Token))
                {
                    if (!owner.wakeEnabled) continue;
                    using var cancel = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                    lock (wakeGate) { wakeCancellation = cancel; if (!owner.wakeEnabled) cancel.Cancel(); }
                    try { await Sample(message, cancel.Token); }
                    catch (OperationCanceledException) { owner.WakeStatus = Closed ? "客户端已断开" : "请求已取消"; }
                    catch (Exception e) { owner.WakeStatus = e.GetType().Name + ": " + e.Message; }
                    finally { lock (wakeGate) if (wakeCancellation == cancel) wakeCancellation = null; }
                }
            } catch (OperationCanceledException) { }
        }
        private async Task<JsonElement> SampleRequest(object parameters, CancellationToken token)
        {
            string id = "fish-sampling-" + Interlocked.Increment(ref nextOutbound);
            var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            outbound[id] = completion;
            try {
                token.ThrowIfCancellationRequested();
                Send(new { jsonrpc = "2.0", id, method = "sampling/createMessage", @params = parameters });
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(owner.samplingTimeout);
                try { return await completion.Task.WaitAsync(timeout.Token); }
                catch (OperationCanceledException) {
                    if (completion.Task.IsCanceled) throw;
                    Send(new { jsonrpc = "2.0", method = "notifications/cancelled", @params = new { requestId = id, reason = "请求已取消或超时" } });
                    if (!token.IsCancellationRequested) throw new TimeoutException("客户端 sampling 响应超时。");
                    throw;
                }
            } finally { outbound.TryRemove(id, out _); }
        }
        private async Task Sample(object message, CancellationToken token)
        {
            var messages = new List<object> { new { role = "user", content = new { type = "text", text =
                "蓝色大肥鱼收到用户交互：" + JsonSerializer.Serialize(message) + "\n请处理这次交互。最终文字会作为鱼鱼的气泡显示；也可通过提供的工具控制鱼鱼。" } } };
            bool spoke = false;
            for (int turn = 0; turn < 8; turn++)
            {
                var parameters = new Dictionary<string, object> { ["messages"] = messages, ["maxTokens"] = 1024 };
                if (samplingTools) { parameters["tools"] = ControlTools.Definitions; parameters["toolChoice"] = new { mode = "auto" }; }
                owner.WakeStatus = "等待客户端响应";
                var response = await SampleRequest(parameters, token);
                token.ThrowIfCancellationRequested();
                if (!owner.wakeEnabled) return;
                var raw = response.GetProperty("content");
                var blocks = raw.ValueKind == JsonValueKind.Array ? raw.EnumerateArray().Select(x => x.Clone()).ToArray() : new[] { raw.Clone() };
                var calls = blocks.Where(x => x.TryGetProperty("type", out var t) && t.GetString() == "tool_use").ToArray();
                if (calls.Length == 0)
                {
                    string text = string.Concat(blocks.Where(x => x.TryGetProperty("type", out var t) && t.GetString() == "text").Select(x => x.GetProperty("text").GetString()));
                    if (!spoke && !string.IsNullOrWhiteSpace(text))
                    {
                        if (text.Length > 4000) text = text[..4000];
                        await owner.execute("say", JsonSerializer.SerializeToElement(new { text }), token);
                    }
                    owner.WakeStatus = "已完成"; return;
                }
                if (!samplingTools) throw new IOException("客户端未协商 sampling.tools 却返回工具调用。");
                if (calls.Length > 16) throw new IOException("单次工具调用过多。");
                messages.Add(new { role = "assistant", content = blocks });
                var results = new List<object>();
                foreach (var call in calls)
                {
                    token.ThrowIfCancellationRequested();
                    var name = call.GetProperty("name").GetString()!;
                    object result;
                    try {
                        if (!ControlTools.Definitions.Any(x => JsonSerializer.SerializeToElement(x).GetProperty("name").GetString() == name)) throw new ArgumentException("未知工具。");
                        result = await owner.execute(name, call.GetProperty("input"), token);
                        if (name == "say") spoke = true;
                    } catch (OperationCanceledException) { throw; }
                    catch (Exception e) { result = ControlTools.Error(e.Message); }
                    var value = JsonSerializer.SerializeToElement(result);
                    results.Add(new { type = "tool_result", toolUseId = call.GetProperty("id").GetString(), content = value.GetProperty("content"),
                        isError = value.TryGetProperty("isError", out var error) && error.ValueKind == JsonValueKind.True });
                }
                messages.Add(new { role = "user", content = results });
            }
            owner.WakeStatus = "已达到单次触发的8轮上限";
        }
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
            var sampler = WakeLoop();
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
            finally { Dispose(); await writer.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing); await sampler.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing); }
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
            if (request.ValueKind != JsonValueKind.Object) { Error(null, -32600, "Invalid Request"); return Task.CompletedTask; }
            if (!request.TryGetProperty("method", out var methodValue))
            {
                if (request.TryGetProperty("id", out var responseId) && outbound.TryGetValue(responseId.ToString(), out var completion))
                {
                    if (request.TryGetProperty("result", out var result)) completion.TrySetResult(result.Clone());
                    else if (request.TryGetProperty("error", out var error)) completion.TrySetException(new IOException("客户端拒绝 sampling：" + error.GetRawText()));
                }
                return Task.CompletedTask;
            }
            if (methodValue.ValueKind != JsonValueKind.String) { Error(null, -32600, "Invalid Request"); return Task.CompletedTask; }
            var method = methodValue.GetString()!;
            var hasId = request.TryGetProperty("id", out var id);
            var args = request.TryGetProperty("params", out var p) ? p : JsonSerializer.SerializeToElement(new { });
            if (!hasId)
            {
                if (method == "notifications/initialized" && negotiated) Initialized = true;
                if (method == "notifications/cancelled" && args.TryGetProperty("requestId", out var cancelled))
                {
                    if (requests.TryGetValue(cancelled.ToString(), out var source))
                        try { source.Cancel(); } catch (ObjectDisposedException) { }
                    if (outbound.TryGetValue(cancelled.ToString(), out var pending)) pending.TrySetCanceled();
                }
                return Task.CompletedTask;
            }
            if (method == "initialize")
            {
                if (negotiated) { Error(id, -32600, "已完成初始化协商。"); return Task.CompletedTask; }
                negotiated = true;
                if (args.TryGetProperty("capabilities", out var capabilities) && capabilities.TryGetProperty("sampling", out var sampling) && sampling.ValueKind == JsonValueKind.Object)
                { Sampling = true; samplingTools = sampling.TryGetProperty("tools", out var tools) && tools.ValueKind == JsonValueKind.Object; }
                var version = args.TryGetProperty("protocolVersion", out var requested) && requested.GetString() == "2025-03-26" ? "2025-03-26" : "2025-11-25";
                Result(id, new { protocolVersion = version, capabilities = new { tools = new { listChanged = false }, logging = new { }, resources = new { subscribe = true, listChanged = false } },
                    serverInfo = new { name = "BigBlueFish", version = typeof(ControlServer).Assembly.GetName().Version!.ToString(3) },
                    instructions = "控制用户本机的蓝色大肥鱼。先读get_settings与工具schema；AnimationIntervalSeconds只接受30–300整数秒，SpeechMinMinutes是枚举档位。say/say_next显示你提供的原文，不能把成功回执当作已读或播放完成。点击、拖动及双击输入的user_message（detail.text为原文）通过notifications/message推送，logger为bigbluefish.interaction、level为info；需接收info日志，或订阅并读取bigbluefish://interactions。默认只推送事件。用户开启McpWakeEnabled后，按McpWakeOnText/Click/Drag决定是否向支持sampling的客户端请求sampling/createMessage；仅调用客户端采样能力，不保证启动其完整Agent任务。支持sampling.tools时可执行有界工具轮次，否则返回文字直接显示；客户端可拒绝。资源只保留最近128条内存事件，按sequence去重，断线不保证补齐。截图会把屏幕内容交给当前客户端；ForegroundEnabled仅控制内置读屏，不限制MCP截图。开启PuppetMode需真实用户确认，等待期间其他控制与拖动可继续；模式在断开后保持，需显式关闭才能恢复自主功能。修改设置不会打开或激活设置窗口。多个客户端共享状态和队列，没有独占锁。" });
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
            stop.Cancel(); wakeQueue.Writer.TryComplete(); CancelSampling(); output.Writer.TryComplete();
            foreach (var source in requests.Values) try { source.Cancel(); } catch (ObjectDisposedException) { }
            foreach (var pending in outbound.Values) pending.TrySetCanceled();
            stream.Dispose();
        }
    }
}
