using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace BigBlueFish.Agent;

public sealed record AgentPermission(string Title, string Detail, IReadOnlyList<AgentPermissionOption> Options, string Kind = "", string[]? Paths = null);
public sealed record AgentPermissionOption(string Id, string Name, string Kind);

/// <summary>ACP v1 over an owned UTF-8 JSON-RPC stdio process. Text notifications never execute pet actions.</summary>
public sealed class AcpConnection : IAsyncDisposable
{
    private readonly Process process;
    private readonly CancellationTokenSource stop = new();
    private readonly SemaphoreSlim write = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> pending = new();
    private readonly ConcurrentDictionary<string, string> toolTitles = new();
    private readonly Func<AgentPermission, CancellationToken, Task<string?>> permission;
    private readonly Task reader, errors;
    private long serial;
    private int disposed;
    private string errorTail = "";
    private readonly object errorGate = new();
    private readonly ProcessOwnership ownership;
    private readonly bool terminalAuthentication;
    public JsonElement Capabilities { get; private set; }
    public AgentAuthenticationMethod[] AuthenticationMethods { get; private set; } = [];
    public string AgentName { get; private set; } = "Agent";
    public string AgentVersion { get; private set; } = "";
    public bool Alive => !stop.IsCancellationRequested && !process.HasExited;
    public event Action<string, JsonElement>? Notification;
    public event Action<JsonElement>? SessionStateReceived;
    public event Action<string>? Disconnected;
    public string DiagnosticTail { get { lock (errorGate) return errorTail; } }

    public AcpConnection(ProcessStartInfo start, Func<AgentPermission, CancellationToken, Task<string?>> permission)
    {
        this.permission = permission;
        terminalAuthentication = AgentTerminalLogin.CanLaunch(start);
        process = new Process { StartInfo = start };
        if (!process.Start()) throw new IOException("未能启动 Agent。");
        ownership = new ProcessOwnership(process);
        errors = DrainErrors();
        reader = Read();
    }

    public async Task Initialize(CancellationToken token)
    {
        var result = await Request("initialize", new { protocolVersion = 1,
            clientInfo = new { name = "big-blue-fish", title = "蓝色大肥鱼", version = typeof(AcpConnection).Assembly.GetName().Version!.ToString(3) },
            clientCapabilities = new { fs = new { readTextFile = false, writeTextFile = false }, terminal = false, auth = new { terminal = terminalAuthentication },
                session = new { configOptions = new { boolean = new { } } } } }, token, TimeSpan.FromSeconds(45));
        if (!result.TryGetProperty("protocolVersion", out var version) || version.GetInt32() != 1)
            throw new IOException("Agent 未协商可用的 ACP v1 协议。");
        Capabilities = result.TryGetProperty("agentCapabilities", out var c) ? c.Clone() : JsonSerializer.SerializeToElement(new { });
        AuthenticationMethods = AgentAuthenticationMethod.Parse(result, terminalAuthentication);
        if (result.TryGetProperty("agentInfo", out var info))
        {
            if (info.TryGetProperty("name", out var n)) AgentName = n.GetString() ?? AgentName;
            if (info.TryGetProperty("version", out var v)) AgentVersion = v.GetString() ?? "";
        }
    }

    public AgentAuthenticationMethod[] AuthenticationChoices(AcpException error)
    {
        var choices = AgentAuthenticationMethod.Parse(error.ErrorData, terminalAuthentication);
        return choices.Length == 0 ? AuthenticationMethods : choices.Select(c => c with {
            ReplaceArguments = c.ReplaceArguments || AuthenticationMethods.Any(m => m.Id == c.Id && m.ReplaceArguments)
        }).ToArray();
    }

    public Task<JsonElement> Request(string method, object parameters, CancellationToken token, TimeSpan? timeout = null) => RequestCore(method, parameters, token, timeout ?? TimeSpan.FromSeconds(60));
    private async Task<JsonElement> RequestCore(string method, object parameters, CancellationToken token, TimeSpan timeout)
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        string id = "fish-" + Interlocked.Increment(ref serial);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = completion;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, stop.Token);
        deadline.CancelAfter(timeout);
        try
        {
            await Send(new { jsonrpc = "2.0", id, method, @params = parameters }, deadline.Token);
            try { return await completion.Task.WaitAsync(deadline.Token); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested && !stop.IsCancellationRequested)
            { throw new TimeoutException("Agent 请求超时：" + method); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested && stop.IsCancellationRequested)
            { throw new IOException("Agent 进程已断开。" + DiagnosticTail); }
        }
        finally { pending.TryRemove(id, out _); }
    }

    public Task Notify(string method, object parameters, CancellationToken token) => Send(new { jsonrpc = "2.0", method, @params = parameters }, token);
    private async Task Send(object message, CancellationToken token)
    {
        await write.WaitAsync(token);
        try
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), token);
            await process.StandardInput.FlushAsync(token);
        }
        finally { write.Release(); }
    }

    private async Task Read()
    {
        string reason = "Agent 已断开。";
        try
        {
            // Bounded records protect against a broken CLI emitting an unending line.
            var buffer = new char[8192]; var line = new System.Text.StringBuilder();
            while (!stop.IsCancellationRequested)
            {
                int count = await process.StandardOutput.ReadAsync(buffer.AsMemory(), stop.Token);
                if (count == 0) break;
                for (int i = 0; i < count; i++)
                {
                    char ch = buffer[i];
                    if (ch != '\n')
                    {
                        if (line.Length >= 8 * 1024 * 1024) throw new IOException("ACP 消息超过8MiB。");
                        line.Append(ch); continue;
                    }
                    var value = line.ToString(); line.Clear();
                    if (string.IsNullOrWhiteSpace(value)) continue;
                    JsonElement msg;
                    try { using var doc = JsonDocument.Parse(value); msg = doc.RootElement.Clone(); }
                    catch (JsonException) { RememberError(value); continue; }
                    if (msg.ValueKind != JsonValueKind.Object) continue;
                    if (msg.TryGetProperty("method", out var method))
                    {
                        var p = msg.TryGetProperty("params", out var parameters) ? parameters.Clone() : JsonSerializer.SerializeToElement(new { });
                        if (msg.TryGetProperty("id", out var requestId)) _ = HandleRequest(requestId.Clone(), method.GetString() ?? "", p);
                        else
                        {
                            if (method.GetString() == "session/update" && p.TryGetProperty("update", out var update)
                                && update.TryGetProperty("sessionUpdate", out var category) && category.GetString() == "tool_call"
                                && update.TryGetProperty("toolCallId", out var callId) && update.TryGetProperty("title", out var title))
                            {
                                if (toolTitles.Count >= 128) toolTitles.Clear();
                                toolTitles[callId.GetString() ?? ""] = title.GetString() ?? "";
                            }
                            try { Notification?.Invoke(method.GetString() ?? "", p); }
                            catch (Exception e) { RememberError(e.Message); }
                        }
                    }
                    else if (msg.TryGetProperty("id", out var id) && pending.TryGetValue(id.ToString(), out var completion))
                    {
                        if (msg.TryGetProperty("error", out var error)) completion.TrySetException(new AcpException(error));
                        else if (msg.TryGetProperty("result", out var result))
                        {
                            if (result.ValueKind == JsonValueKind.Object && (result.TryGetProperty("configOptions", out _) || result.TryGetProperty("modes", out _) || result.TryGetProperty("models", out _)))
                                SessionStateReceived?.Invoke(result);
                            completion.TrySetResult(result.Clone());
                        }
                    }
                }
            }
            reason = "Agent 已退出或关闭连接。" + DiagnosticTail;
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { reason = e.Message; }
        finally
        {
            stop.Cancel();
            foreach (var request in pending.Values) request.TrySetException(new IOException(reason));
            if (disposed == 0) Disconnected?.Invoke(reason);
        }
    }

    private async Task HandleRequest(JsonElement id, string method, JsonElement args)
    {
        try
        {
            if (method != "session/request_permission")
            {
                await Send(new { jsonrpc = "2.0", id, error = new { code = -32601, message = "Client capability not advertised: " + method } }, stop.Token); return;
            }
            var call = args.GetProperty("toolCall");
            string title = call.TryGetProperty("title", out var t) ? t.GetString() ?? "Agent 请求操作" : "Agent 请求操作";
            if (call.TryGetProperty("toolCallId", out var callId) && toolTitles.TryRemove(callId.GetString() ?? "", out var knownTitle)
                && (title.StartsWith('{') || title.Length == 0 || title == "Agent 请求操作")) title = knownTitle;
            string detail = call.GetRawText();
            var options = args.GetProperty("options").EnumerateArray().Select(o => new AgentPermissionOption(
                o.GetProperty("optionId").GetString()!, o.GetProperty("name").GetString()!, o.GetProperty("kind").GetString()!)).ToArray();
            string kind = call.TryGetProperty("kind", out var category) ? category.GetString() ?? "" : "";
            string[] paths = call.TryGetProperty("locations", out var locations) && locations.ValueKind == JsonValueKind.Array
                ? locations.EnumerateArray().Where(x => x.TryGetProperty("path", out _)).Select(x => x.GetProperty("path").GetString() ?? "").ToArray() : [];
            string? selected = await permission(new AgentPermission(title, detail, options, kind, paths), stop.Token);
            object outcome = selected != null && options.Any(o => o.Id == selected)
                ? new { outcome = "selected", optionId = selected } : new { outcome = "cancelled" };
            await Send(new { jsonrpc = "2.0", id, result = new { outcome } }, stop.Token);
        }
        catch (Exception e)
        {
            try { await Send(new { jsonrpc = "2.0", id, error = new { code = -32603, message = e.Message } }, stop.Token); }
            catch (Exception) { }
        }
    }

    private async Task DrainErrors()
    {
        try
        {
            var buffer = new char[2048]; int n;
            while ((n = await process.StandardError.ReadAsync(buffer.AsMemory(), stop.Token)) > 0) RememberError(new string(buffer, 0, n));
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
    }
    private void RememberError(string value)
    {
        lock (errorGate) { errorTail += value; if (errorTail.Length > 2048) errorTail = errorTail[^2048..]; }
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        stop.Cancel();
        try { process.StandardInput.Close(); } catch (Exception) { }
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (Exception) { }
        ownership.Dispose();
        try { await Task.WhenAll(reader, errors).WaitAsync(TimeSpan.FromSeconds(3)); } catch (Exception) { }
        process.Dispose();
    }
}

public sealed class AcpException : IOException
{
    public int Code { get; }
    public string ProtocolMessage { get; }
    public JsonElement ErrorData { get; }
    public bool AuthenticationRequired => Code == -32000
        || ErrorData.ValueKind == JsonValueKind.Object && ErrorData.TryGetProperty("errorKind", out var kind) && kind.GetString() == "authentication_failed"
        || ProtocolMessage.Contains("authentication required", StringComparison.OrdinalIgnoreCase)
        || ProtocolMessage.Contains("login required", StringComparison.OrdinalIgnoreCase)
        || ProtocolMessage.Contains("not logged in", StringComparison.OrdinalIgnoreCase);
    public AcpException(JsonElement error) : base(Describe(error))
    { Code = error.TryGetProperty("code", out var c) && c.TryGetInt32(out var n) ? n : -32603; ProtocolMessage = error.TryGetProperty("message", out var m) ? m.GetString() ?? "" : ""; ErrorData = error.TryGetProperty("data", out var data) ? data.Clone() : default; }
    private static string Describe(JsonElement error)
    {
        string message = error.TryGetProperty("message", out var m) ? m.GetString() ?? "ACP error" : "ACP error";
        if (error.TryGetProperty("data", out var data) && data.ValueKind != JsonValueKind.Null)
        {
            string details = data.ValueKind == JsonValueKind.String ? data.GetString() ?? "" : data.GetRawText();
            message += "\n" + details[..Math.Min(2000, details.Length)];
        }
        return message;
    }
    public bool SessionMissing => !AuthenticationRequired && (Code == -32002 || ProtocolMessage.Contains("session", StringComparison.OrdinalIgnoreCase)
        && (ProtocolMessage.Contains("not found", StringComparison.OrdinalIgnoreCase) || ProtocolMessage.Contains("unknown", StringComparison.OrdinalIgnoreCase)
            || ProtocolMessage.Contains("does not exist", StringComparison.OrdinalIgnoreCase) || ProtocolMessage.Contains("expired", StringComparison.OrdinalIgnoreCase)
            || ProtocolMessage.Contains("invalid session", StringComparison.OrdinalIgnoreCase) || ProtocolMessage.Contains("deleted", StringComparison.OrdinalIgnoreCase)));
}
