using System.Diagnostics;
using System.Text;
using System.Text.Json;
using BigBlueFish.Agent;

namespace BigBlueFish.Windows;

internal sealed class MacAgentSession : IAsyncDisposable
{
    AcpConnection? connection;
    StringBuilder? reply;
    readonly SemaphoreSlim turn = new(1, 1);
    internal bool Connected => connection?.Alive == true && SessionId.Length > 0;
    internal string SessionId { get; private set; } = "";
    internal async Task Connect(ProcessStartInfo start, string workspace, Func<AgentPermission, CancellationToken, Task<string?>> permission, CancellationToken token)
    {
        await DisposeAsync();
        var client = new AcpConnection(start, permission); connection = client;
        try {
            await client.Initialize(token);
            var result = await client.Request("session/new", new { cwd = Path.GetFullPath(workspace), mcpServers = Array.Empty<object>() }, token);
            SessionId = result.GetProperty("sessionId").GetString() ?? throw new IOException("Agent 未返回会话编号。");
            client.Notification += Receive;
        } catch { await DisposeAsync(); throw; }
    }
    void Receive(string method, JsonElement parameters)
    {
        if (method != "session/update" || !parameters.TryGetProperty("sessionId", out var id) || id.GetString() != SessionId ||
            !parameters.TryGetProperty("update", out var update) || !update.TryGetProperty("sessionUpdate", out var kind) || kind.GetString() != "agent_message_chunk" ||
            !update.TryGetProperty("content", out var content) || !content.TryGetProperty("type", out var type) || type.GetString() != "text" ||
            !content.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String) return;
        lock (turn) { if (reply != null) { if (reply.Length + text.GetString()!.Length > 64000) return; reply.Append(text.GetString()); } }
    }
    internal async Task<string> Send(string text, CancellationToken token, string? personality = null)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4000) throw new ArgumentException("消息需为 1–4000 个字符。");
        await turn.WaitAsync(token);
        try {
            var client = connection; if (!Connected || client == null) throw new IOException("请先连接 Agent。");
            lock (turn) reply = new();
            var prompt = MacChatPersonality.BuildAcpPrompt(personality, text);
            await client.Request("session/prompt", new { sessionId = SessionId, prompt = new[] { new { type = "text", text = prompt } } }, token, TimeSpan.FromMinutes(3));
            lock (turn) return reply!.ToString();
        } catch (OperationCanceledException) { await DisposeAsync(); throw; }
        catch (TimeoutException) { await DisposeAsync(); throw; }
        finally { lock (turn) reply = null; turn.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        var previous = connection; connection = null; SessionId = "";
        if (previous != null) { previous.Notification -= Receive; await previous.DisposeAsync(); }
    }
}
