using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BigBlueFish.Windows;

internal sealed class MacLocalChat : IAsyncDisposable
{
    internal const int MaxPromptLength = 4000;
    internal const int MaxHistoryCharacters = 12000;
    readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = Timeout.InfiniteTimeSpan };
    readonly List<ChatMessage> history = new();
    Process? llama;
    Uri? llamaEndpoint;
    string? activeFingerprint;
    string? activeGgufFingerprint;
    long historyGeneration;
    bool disposed;

    sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    internal async Task<string> Send(string text, MacChatSettings settings, string? apiKey, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxPromptLength)
            throw new ArgumentException("消息需为 1–4000 个字符。", nameof(text));
        settings.Normalize();
        var fingerprint = Fingerprint(settings, apiKey);
        if (activeFingerprint != null && activeFingerprint != fingerprint) {
            history.Clear();
            historyGeneration++;
        }
        activeFingerprint = fingerprint;
        long requestGeneration = historyGeneration;
        if (settings.Backend != "gguf" && llama != null) await StopGguf();

        Uri endpoint = settings.Backend switch
        {
            "api" => ValidateEndpoint(settings.Endpoint),
            "gguf" => await EnsureGguf(settings, token),
            _ => throw new InvalidOperationException("ACP 对话由 MacAgentSession 处理。")
        };
        int conversationLimit = Math.Max(MaxPromptLength, MaxHistoryCharacters - settings.PersonalityPrompt.Length);
        var messages = history.Append(new ChatMessage("user", text)).ToList();
        TrimHistory(messages, conversationLimit);
        var responses = settings.Backend == "api" && settings.ApiProtocol == "responses";
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, responses ? "responses" : "chat/completions"));
        if (settings.Backend == "api" && !string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        // Our managed llama-server is started with this alias; keep cloud model IDs out
        // of its requests when the user switches from the default DeepSeek API.
        var requestModel = settings.Backend == "gguf" ? "local" : settings.Model;
        object requestBody;
        if (responses) {
            requestBody = string.IsNullOrWhiteSpace(settings.PersonalityPrompt)
                ? new { model = requestModel, input = messages, stream = false }
                : new { model = requestModel, instructions = settings.PersonalityPrompt, input = messages, stream = false };
        } else {
            var chatMessages = string.IsNullOrWhiteSpace(settings.PersonalityPrompt)
                ? messages.ToArray()
                : messages.Prepend(new ChatMessage("system", settings.PersonalityPrompt)).ToArray();
            requestBody = new { model = requestModel, messages = chatMessages, stream = false };
        }
        var payload = JsonSerializer.Serialize(requestBody);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (!response.IsSuccessStatusCode)
            throw new IOException($"模型请求失败（HTTP {(int)response.StatusCode}）。");
        await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: timeout.Token);
        var reply = responses ? ExtractResponseReply(document.RootElement) : ExtractReply(document.RootElement);
        if (reply.Length == 0) throw new IOException("模型返回了空回复。");
        token.ThrowIfCancellationRequested();
        if (requestGeneration != historyGeneration) throw new OperationCanceledException(token);
        history.Add(new ChatMessage("user", text));
        history.Add(new ChatMessage("assistant", reply));
        TrimHistory(history, conversationLimit);
        return reply;
    }

    internal void Reset() { historyGeneration++; history.Clear(); }

    static string Fingerprint(MacChatSettings settings, string? apiKey) => string.Join("\n", settings.Backend, settings.Endpoint, settings.Model, settings.ApiProtocol,
        settings.LlamaServerExecutable, settings.GgufPath, settings.PersonalityPrompt, CredentialFingerprint(apiKey));

    static string CredentialFingerprint(string? apiKey)
    {
        if (string.IsNullOrEmpty(apiKey)) return "";
        var bytes = Encoding.UTF8.GetBytes(apiKey);
        try { return Convert.ToHexString(SHA256.HashData(bytes)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    static void TrimHistory(List<ChatMessage> messages, int maximumCharacters)
    {
        int length = messages.Sum(message => message.Content.Length);
        while (length > maximumCharacters && messages.Count > 2)
        {
            length -= messages[0].Content.Length + messages[1].Content.Length;
            messages.RemoveRange(0, 2);
        }
        if (length <= maximumCharacters || messages.Count == 0) return;
        var last = messages[^1];
        int keep = Math.Max(0, maximumCharacters - (length - last.Content.Length));
        if (last.Content.Length > keep) messages[^1] = last with { Content = last.Content[..keep] };
    }

    internal static Uri ValidateEndpoint(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme is not ("http" or "https") ||
            endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 ||
            (endpoint.Scheme == "http" && !IsLoopback(endpoint.Host)))
            throw new ArgumentException("API 地址必须使用 HTTPS；本机回环地址可使用 HTTP。", nameof(value));
        return new UriBuilder(endpoint) { Path = endpoint.AbsolutePath.TrimEnd('/') + "/" }.Uri;
    }

    static bool IsLoopback(string host) => host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);

    async Task<Uri> EnsureGguf(MacChatSettings settings, CancellationToken token)
    {
        if (!Path.IsPathRooted(settings.LlamaServerExecutable) || !File.Exists(settings.LlamaServerExecutable))
            throw new FileNotFoundException("请在设置中填写 llama-server 的绝对路径。", settings.LlamaServerExecutable);
        if (!Path.IsPathRooted(settings.GgufPath) || !File.Exists(settings.GgufPath))
            throw new FileNotFoundException("请在设置中填写 GGUF 模型的绝对路径。", settings.GgufPath);
        var fingerprint = string.Join("\n", settings.LlamaServerExecutable, settings.GgufPath);
        if (llama is { HasExited: false } && llamaEndpoint != null && activeGgufFingerprint == fingerprint && await IsHealthy(llamaEndpoint, token)) return llamaEndpoint;
        await StopGguf();
        var portProbe = new TcpListener(IPAddress.Loopback, 0);
        portProbe.Start();
        var port = ((IPEndPoint)portProbe.LocalEndpoint).Port;
        portProbe.Stop();
        var start = new ProcessStartInfo(settings.LlamaServerExecutable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in new[] { "-m", settings.GgufPath, "--alias", "local", "--host", "127.0.0.1", "--port", port.ToString(), "--no-webui" }) start.ArgumentList.Add(arg);
        llama = Process.Start(start) ?? throw new IOException("无法启动 llama-server。");
        activeGgufFingerprint = fingerprint;
        _ = Drain(llama.StandardOutput); _ = Drain(llama.StandardError);
        llamaEndpoint = new Uri($"http://127.0.0.1:{port}/v1/");
        try
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                token.ThrowIfCancellationRequested();
                if (llama.HasExited) throw new IOException("llama-server 启动失败。");
                if (await IsHealthy(llamaEndpoint, token)) return llamaEndpoint;
                await Task.Delay(250, token);
            }
            throw new TimeoutException("等待 llama-server 就绪超时。");
        }
        catch { await StopGguf(); throw; }
    }

    async Task<bool> IsHealthy(Uri endpoint, CancellationToken token)
    {
        try { using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(1000); using var response = await http.GetAsync(new Uri(endpoint, "../health"), timeout.Token); return response.IsSuccessStatusCode; }
        catch (HttpRequestException) { return false; }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return false; }
    }

    static async Task Drain(StreamReader reader) { try { await reader.ReadToEndAsync(); } catch (ObjectDisposedException) { } }

    static string ExtractReply(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0) return "";
        var choice = choices[0];
        if (choice.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content)) return ExtractContent(content);
        return choice.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String ? text.GetString() ?? "" : "";
    }

    static string ExtractResponseReply(JsonElement root)
    {
        if (root.TryGetProperty("output_text", out var direct) && direct.ValueKind == JsonValueKind.String)
            return direct.GetString() ?? "";
        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array) return "";
        var result = new StringBuilder();
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var type) || type.GetString() != "message" ||
                !item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) continue;
            if (item.TryGetProperty("role", out var role) && role.GetString() != "assistant") continue;
            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("type", out var partType) && partType.GetString() != "output_text") continue;
                if (part.TryGetProperty("text", out var partText) && partText.ValueKind == JsonValueKind.String)
                    result.Append(partText.GetString());
            }
        }
        return result.ToString();
    }

    static string ExtractContent(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String) return content.GetString() ?? "";
        if (content.ValueKind != JsonValueKind.Array) return "";
        var builder = new StringBuilder();
        foreach (var item in content.EnumerateArray()) if (item.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String) builder.Append(text.GetString());
        return builder.ToString();
    }

    async Task StopGguf()
    {
        var process = Interlocked.Exchange(ref llama, null);
        llamaEndpoint = null;
        activeGgufFingerprint = null;
        if (process == null) return;
        try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
        try { await process.WaitForExitAsync(); } catch (InvalidOperationException) { }
        process.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        await StopGguf();
        http.Dispose();
    }
}
