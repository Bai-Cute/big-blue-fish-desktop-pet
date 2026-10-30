using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;

namespace BigBlueFish.Agent;

public sealed record AgentMode(bool Enabled, bool Puppet, bool DailyCare);
public sealed record AgentSession(string SessionId, string Executable, string Workspace);
internal sealed record PetEvent(string Kind, object Detail, bool Fresh, long Epoch, TaskCompletionSource<bool>? Completion = null);

/// <summary>One fish-owned ACP session per harness. The host clock executes reservations; the Agent chooses them.</summary>
public sealed class AgentCoordinator : IDisposable
{
    private readonly string root, bridge, pipe;
    private readonly Func<AgentPermission, CancellationToken, Task<string?>> permission;
    private readonly SemaphoreSlim connecting = new(1, 1);
    private readonly SemaphoreSlim workspaceOperations = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource connectionLifetime = new();
    private CancellationTokenSource? activeTurn;
    private readonly Channel<PetEvent> events = Channel.CreateBounded<PetEvent>(new BoundedChannelOptions(32) { SingleReader = true });
    private readonly object gate = new();
    private readonly HashSet<string> coalesced = [];
    private AcpConnection? connection;
    private AgentMode mode = new(false, false, false);
    private string harnessId = "codex", status = "尚未连接", detail = "", sessionId = "", activeKind = "";
    private bool initialCare;
    private DateTimeOffset retryAfter;
    private string? guideFingerprint;
    private int disposed;
    private long epoch;
    private volatile bool changingWorkspace;
    private bool authenticationPending;
    private static readonly JsonSerializerOptions EventJson = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private long spoken, turnStartSpoken;
    private long toolActivity;
    public AgentWakeups Wakeups { get; }
    public AgentWorkspaceStore Workspaces { get; }
    public AgentSessionConfiguration Configuration { get; } = new();
    public bool ChangingWorkspace => changingWorkspace;
    public bool Connected => connection?.Alive == true && sessionId.Length > 0;
    public bool Busy { get; private set; }
    public bool AwaitingSpeech => Busy && Interlocked.Read(ref spoken) == Interlocked.Read(ref turnStartSpoken);
    public void MarkSpoken() => Interlocked.Increment(ref spoken);
    public string Status => status;
    public string Detail => detail;
    public string HarnessId => harnessId;
    public string SessionId => sessionId;
    public string? ExecutablePath { get; private set; }
    public string Version => connection?.AgentVersion ?? "";
    public string CurrentModel { get; private set; } = "由 Harness 配置";
    public bool RestoresSessions { get; private set; }
    public string? NextWake => Wakeups.List(harnessId, sessionId).FirstOrDefault()?.Due.ToLocalTime().ToString("MM-dd HH:mm:ss");
    public event Action? Changed;
    public event Action<string, JsonElement>? ProtocolEvent;
    public Func<CancellationToken, Task>? LocalSpeechRequested { get; set; }
    public AgentSkillStore Skills => new(root, HarnessCatalog.Get(harnessId), Workspaces.Current);

    public AgentCoordinator(string root, string bridge, string pipe, Func<AgentPermission, CancellationToken, Task<string?>> permission, string? defaultWorkspace = null)
    {
        this.root = Path.GetFullPath(root); this.bridge = Path.GetFullPath(bridge); this.pipe = pipe; this.permission = permission;
        Workspaces = new(this.root, defaultWorkspace); Wakeups = new(this.root); _ = Worker();
    }
    private void Report(string value, string extra = "") { status = value; detail = extra; Changed?.Invoke(); }
    public void Configure(string selected, AgentMode updated)
    {
        HarnessCatalog.Get(selected);
        bool reconnect = selected != harnessId || updated.Enabled != mode.Enabled;
        bool startsCare = updated.Enabled && updated.Puppet && (!mode.Puppet || updated.DailyCare != mode.DailyCare || reconnect);
        if (!updated.Enabled || !updated.Puppet) Wakeups.Clear(harnessId);
        if (reconnect)
        {
            Interlocked.Increment(ref epoch);
            connectionLifetime.Cancel(); connectionLifetime = new();
            var previous = Interlocked.Exchange(ref connection, null); if (previous != null) _ = previous.DisposeAsync();
            sessionId = ""; guideFingerprint = null; ExecutablePath = null; CurrentModel = "由 Harness 配置"; RestoresSessions = false; Configuration.Reset();
            lock (gate) { while (events.Reader.TryRead(out var abandoned)) abandoned.Completion?.TrySetCanceled(); coalesced.Clear(); }
        }
        harnessId = selected; mode = updated;
        if (!updated.Enabled || !updated.Puppet) initialCare = false;
        if (startsCare) initialCare = true;
        if (!updated.Enabled || !updated.DailyCare && activeKind is "daily_care" or "speech_test" or "weather_request" || !updated.Puppet && activeKind is "wakeup" or "start_care") activeTurn?.Cancel();
        if (!updated.Enabled) { Report("连接已关闭"); return; }
        if (reconnect && !changingWorkspace) Enqueue("connect", new { });
        else if (startsCare) Enqueue("start_care", new { purpose = "开始接管日常关心；请检查已有预约，安排后续唤醒。" });
        Changed?.Invoke();
    }

    public bool SendUser(string text) => text.Length is > 0 and <= 4000 && !string.IsNullOrWhiteSpace(text) && mode.Enabled && !changingWorkspace
        && Enqueue("user_message", new { text });
    public bool DailyCare() => mode.Enabled && mode.DailyCare && !mode.Puppet && Enqueue("daily_care", new { purpose = "日常说话时间到了，请结合当前情境关心主人。" });
    public bool ManualSpeech(bool weather, string weatherContext = "") => mode.Enabled && mode.DailyCare && !changingWorkspace
        && Enqueue(weather ? "weather_request" : "speech_test", new { purpose = weather ? "主人手动请求天气播报，请根据提供的天气资料组织话语并调用say显示。" : "主人手动触发一次吐字测试，请结合当前情境自然关心主人，并调用say显示。", weatherContext });
    public bool Interaction(string kind, object data) => mode.Enabled && Enqueue(kind, data);
    public bool Reconnect() => mode.Enabled && Enqueue("reconnect", new { });
    public bool NewConversation() => mode.Enabled && Enqueue("new_conversation", new { }, true);
    public void Cancel() => activeTurn?.Cancel();
    public Task SetConfiguration(string id, string value)
    {
        if (!Connected || changingWorkspace) throw new InvalidOperationException("请等待 Agent 连接完成。");
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue("set_configuration", new KeyValuePair<string, string>(id, value), completion: completion);
        return completion.Task;
    }
    public Task OpenWorkspace(string path) => ChangeWorkspace(() => Workspaces.Open(path));
    public Task CreateWorkspace(string parent, string name) => ChangeWorkspace(() => Workspaces.Create(parent, name));
    public Task ClearWorkspace(string expected)
    {
        Workspaces.ValidateClear(expected);
        return ChangeWorkspace(() => {
            string folder = Workspaces.SessionDirectory();
            if (Directory.Exists(folder)) foreach (var record in Directory.EnumerateFiles(folder, "*.json"))
            {
                try { var saved = JsonSerializer.Deserialize<AgentSession>(File.ReadAllText(record)); if (saved != null) Wakeups.Clear(Path.GetFileNameWithoutExtension(record), saved.SessionId); }
                catch (Exception error) when (error is IOException or JsonException) { }
            }
            Workspaces.Clear(expected);
        });
    }
    private async Task ChangeWorkspace(Action mutation)
    {
        await workspaceOperations.WaitAsync(lifetime.Token);
        changingWorkspace = true;
        try
        {
            Interlocked.Increment(ref epoch); activeTurn?.Cancel();
            connectionLifetime.Cancel(); connectionLifetime = new();
            lock (gate) { while (events.Reader.TryRead(out var abandoned)) abandoned.Completion?.TrySetCanceled(); coalesced.Clear(); }
            Report("正在切换工作区…");
            await DropConnection();
            await connecting.WaitAsync(lifetime.Token);
            try
            {
                await Task.Run(() => { lifetime.Token.ThrowIfCancellationRequested(); mutation(); Skills.Install(); }, lifetime.Token);
                lifetime.Token.ThrowIfCancellationRequested(); initialCare = mode.Puppet;
                Report("工作区已准备", Workspaces.Current);
            }
            finally { connecting.Release(); }
        }
        finally
        {
            changingWorkspace = false; workspaceOperations.Release();
            if (mode.Enabled && disposed == 0) Enqueue("connect", new { });
            Changed?.Invoke();
        }
    }
    public void Tick(DateTimeOffset now, bool canDeliver)
    {
        if (!mode.Enabled || !canDeliver || disposed != 0 || changingWorkspace) return;
        if (!Connected && now >= retryAfter) { retryAfter = now.AddSeconds(30); Enqueue("connect", new { }); }
        if (!mode.Puppet || !Connected) return;
        var due = Wakeups.PeekDue(harnessId, sessionId, now);
        if (due.Length > 0) Enqueue("wakeup", new { });
    }
    private bool Enqueue(string kind, object data, bool fresh = false, TaskCompletionSource<bool>? completion = null)
    {
        lock (gate)
        {
            bool combine = kind is "connect" or "reconnect" or "new_conversation" or "daily_care" or "wakeup" or "start_care";
            if (combine && !coalesced.Add(kind)) return true;
            if (events.Writer.TryWrite(new(kind, data, fresh, Interlocked.Read(ref epoch), completion))) return true;
            if (combine) coalesced.Remove(kind);
            completion?.TrySetException(new IOException("请求队列已满，请稍后再试。"));
            Report("请求队列已满，请稍后再试。"); return false;
        }
    }
    private async Task Worker()
    {
        try
        {
            await foreach (var request in events.Reader.ReadAllAsync(lifetime.Token))
            {
                lock (gate) coalesced.Remove(request.Kind);
                if (!mode.Enabled || changingWorkspace || request.Epoch != Interlocked.Read(ref epoch)) { request.Completion?.TrySetCanceled(); continue; }
                if (request.Kind is "speech_test" or "weather_request" && !mode.DailyCare
                    || request.Kind is "daily_care" && (!mode.DailyCare || mode.Puppet)
                    || request.Kind is "wakeup" or "start_care" && !mode.Puppet) continue;
                using var turn = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, connectionLifetime.Token);
                activeTurn = turn; activeKind = request.Kind;
                try
                {
                    if (request.Kind == "reconnect") await DropConnection();
                    await Connect(request.Fresh, turn.Token);
                    turn.Token.ThrowIfCancellationRequested();
                    if (request.Epoch != Interlocked.Read(ref epoch)) throw new OperationCanceledException();
                    if (request.Kind == "set_configuration")
                    {
                        Busy = true; Changed?.Invoke();
                        var selection = (KeyValuePair<string, string>)request.Detail;
                        await Configuration.Apply(connection!, sessionId, selection.Key, selection.Value, turn.Token);
                        CurrentModel = Configuration.Model;
                        Report("已连接 · " + HarnessCatalog.Get(harnessId).Name, "Agent 配置已更新。");
                        request.Completion?.TrySetResult(true); continue;
                    }
                    if (request.Kind is "connect" or "reconnect" or "new_conversation")
                    {
                        if (initialCare && mode.Puppet) Enqueue("start_care", new { purpose = "开始接管日常关心；检查预约并安排后续唤醒。" });
                        continue;
                    }
                    object payload = request.Detail;
                    if (request.Kind == "wakeup")
                    {
                        var due = Wakeups.PeekDue(harnessId, sessionId, DateTimeOffset.Now);
                        if (due.Length == 0) continue;
                        payload = new { reservations = due.Select(x => new { x.Id, x.Purpose, x.Due }).ToArray() };
                        // Dispatch at most once, even if the Agent disconnects after executing say.
                        foreach (var job in due) Wakeups.Acknowledge(job, DateTimeOffset.Now);
                        if (!mode.DailyCare)
                        {
                            Busy = true; Report("正在请求本地模型吐字…");
                            if (LocalSpeechRequested == null) throw new IOException("宿主尚未提供本地吐字入口。");
                            await LocalSpeechRequested(turn.Token);
                            Report("已连接 · " + HarnessCatalog.Get(harnessId).Name, "已按 Agent 预约调用本地模型。");
                            continue;
                        }
                    }
                    if (request.Kind == "start_care") initialCare = false;
                    string fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Skills.Guide())));
                    string setup = guideFingerprint != fingerprint
                        ? $"请使用 big-blue-fish 技能。技能已安装于 {Skills.SkillPath}，同目录 persona.md 为当前人设。请重新读取，当前文件中的人设取代本对话以前的鱼鱼人设；空文件表示使用主人已有的Agent设定。读取工具不可用时调用 get_operation_guide 获取内容。让鱼鱼发言时调用 say，普通回答留在 Agent 对话中。\n" : "请继续使用 big-blue-fish 技能处理本事件。\n";
                    string text = "来源：蓝色大肥鱼桌宠软件\n" + setup + "事件：" + request.Kind + "\n本地时间：" + DateTimeOffset.Now.ToString("O")
                        + "\n当前模式：" + JsonSerializer.Serialize(mode) + "\n事件内容：" + JsonSerializer.Serialize(payload, EventJson);
                    text += mode.Puppet && !mode.DailyCare
                        ? "\n日常关心由你安排时机、由本地模型编写。使用 schedule_wakeup 预约，预约到期由鱼鱼调用本地模型；现在触发使用 request_local_speech。主人主动对话仍用 say 回答。"
                        : "\n日常关心使用当前设置的编写者；由你编写的话通过 say 显示。";
                    var current = connection!;
                    long toolsBeforePrompt = Interlocked.Read(ref toolActivity);
                    Interlocked.Exchange(ref turnStartSpoken, Interlocked.Read(ref spoken));
                    Busy = true; Report(HarnessCatalog.Get(harnessId).Name + " 正在处理…");
                    try
                    {
                        JsonElement result;
                        try { result = await current.Request("session/prompt", new { sessionId, prompt = new[] { new { type = "text", text } } }, turn.Token, TimeSpan.FromMinutes(5)); }
                        catch (AcpException error) when (error.AuthenticationRequired && Interlocked.Read(ref spoken) == Interlocked.Read(ref turnStartSpoken)
                            && Interlocked.Read(ref toolActivity) == toolsBeforePrompt)
                        {
                            await DropConnection(); await Connect(false, turn.Token); current = connection!;
                            result = await current.Request("session/prompt", new { sessionId, prompt = new[] { new { type = "text", text } } }, turn.Token, TimeSpan.FromMinutes(5));
                        }
                        if (!result.TryGetProperty("stopReason", out var reason)) throw new IOException("Agent 未返回 ACP 轮次结束原因。");
                        guideFingerprint = fingerprint;
                        string completion = Interlocked.Read(ref spoken) == Interlocked.Read(ref turnStartSpoken)
                            ? "本轮结束，未调用说话工具。" : "本轮处理完成；双击鱼鱼即可对话。";
                        Report("已连接 · " + HarnessCatalog.Get(harnessId).Name, reason.GetString() == "end_turn" ? completion : "本轮已停止：" + reason.GetString());
                    }
                    catch (Exception) when (turn.IsCancellationRequested)
                    {
                        try { await current.Notify("session/cancel", new { sessionId }, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2)); } catch (Exception) { }
                        // A cancelled turn can still be draining tools; replace its owned process before accepting another.
                        await DropConnection(); throw;
                    }
                    catch (TimeoutException)
                    {
                        try { await current.Notify("session/cancel", new { sessionId }, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2)); } catch (Exception) { }
                        await DropConnection(); throw;
                    }
                }
                catch (OperationCanceledException) { request.Completion?.TrySetCanceled(); if (authenticationPending) retryAfter = DateTimeOffset.MaxValue; if (mode.Enabled) Report("请求已取消"); }
                catch (Exception error)
                {
                    request.Completion?.TrySetException(error);
                    bool authentication = authenticationPending || error is AgentAuthenticationCancelledException || error is AcpException acp && acp.AuthenticationRequired || error.Message.Contains("auth", StringComparison.OrdinalIgnoreCase) || error.Message.Contains("login", StringComparison.OrdinalIgnoreCase);
                    retryAfter = authentication ? DateTimeOffset.MaxValue : DateTimeOffset.Now.AddMinutes(2);
                    Report(authentication ? "需要登录 · " + HarnessCatalog.Get(harnessId).Name : "连接或请求失败", error.Message);
                }
                finally { authenticationPending = false; Busy = false; activeTurn = null; activeKind = ""; Changed?.Invoke(); }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task Connect(bool fresh, CancellationToken token)
    {
        await connecting.WaitAsync(token);
        try
        {
            if (fresh)
            {
                Wakeups.Clear(harnessId); await DropConnection();
                string stateFile = Workspaces.SessionPath(harnessId);
                if (File.Exists(stateFile)) File.Delete(stateFile);
                initialCare = mode.Puppet;
            }
            if (Connected) return;
            var harness = HarnessCatalog.Get(harnessId);
            var adapters = new AgentAdapterStore(root);
            string executable = adapters.FindExecutable(harness) ?? (harness.Id == "antigravity" ? "" : throw new IOException("未找到 " + harness.Name + "，请先按官方说明安装，并确认程序位于 PATH 中。"));
            if (!File.Exists(bridge)) throw new IOException("安装包缺少内部 MCP 组件，请重新安装完整标准版。");
            ExecutablePath = executable;
            Workspaces.EnsureCurrent(); Workspaces.Open(Workspaces.Current);
            var skills = Skills; skills.Install();
            var command = await adapters.Resolve(harness, executable, token, s => Report(s));
            if (harness.Id == "antigravity") executable = command.Path;
            ExecutablePath = executable;
            token.ThrowIfCancellationRequested();
            var start = AgentProcess.StartInfo(command.Path, command.Arguments, skills.Workspace);
            // The actual installed CLI is preferred by adapters when they spawn it.
            start.Environment["PATH"] = Path.GetDirectoryName(executable) + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
            if (harness.Id == "codex") start.Environment["CODEX_PATH"] = executable;
            if (harness.Id == "antigravity" && AgentAdapterStore.AntigravityHelper(command.Path) is { } helper)
                start.Environment["ANTIGRAVITY_HARNESS_PATH"] = helper;
            Report("正在启动 " + harness.Name + "…");
            var client = CreateConnection(start);
            try
            {
                await client.Initialize(token);
                token.ThrowIfCancellationRequested();
                bool authenticationAttempted = false;
                async Task<JsonElement> SessionRequest(string method, object parameters)
                {
                    try { return await client.Request(method, parameters, token); }
                    catch (AcpException error) when (error.AuthenticationRequired && !authenticationAttempted && client.AuthenticationChoices(error).Length > 0)
                    {
                        authenticationAttempted = true;
                        authenticationPending = true;
                        var choices = client.AuthenticationChoices(error);
                        Report("等待认证 · " + harness.Name, "请选择此 Agent 提供的登录方式；已有登录由官方组件复用。");
                        string? selected = await permission(new AgentPermission("选择 " + harness.Name + " 的登录方式",
                            "现有 CLI 登录与 ACP 认证方式分别处理。请选择你正在使用的账号方式，后续认证由 Agent 官方组件完成。\n"
                                + string.Join("\n", choices.Select(c => c.Name + "：" + c.Description + (c.Type == "terminal"
                                    ? (c.ReplaceArguments ? "\n将打开登录终端；登录参数：" : "\n将打开登录终端；追加参数：") + JsonSerializer.Serialize(c.Arguments) + "\n环境变量：" + JsonSerializer.Serialize(c.Environment) : ""))),
                            choices.Select(c => new AgentPermissionOption(c.Id, c.Name, "authentication")).ToArray(), "authentication"), token);
                        token.ThrowIfCancellationRequested();
                        var methodChoice = choices.FirstOrDefault(c => c.Id == selected);
                        if (methodChoice == null) throw new AgentAuthenticationCancelledException();
                        Report("正在认证 · " + harness.Name, "正在通过官方组件认证；如浏览器打开，请完成登录。可用取消当前请求停止等待。");
                        if (methodChoice.Type == "terminal")
                        {
                            connection = null; await client.DisposeAsync();
                            using var loginDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                            loginDeadline.CancelAfter(TimeSpan.FromMinutes(5));
                            var loginBase = methodChoice.ReplaceArguments ? AgentProcess.StartInfo(command.Path, [], skills.Workspace) : start;
                            foreach (var pair in start.Environment) loginBase.Environment[pair.Key] = pair.Value;
                            await AgentTerminalLogin.Run(loginBase, methodChoice, loginDeadline.Token);
                            client = CreateConnection(start); await client.Initialize(token);
                        }
                        else await client.Request("authenticate", new { methodId = selected }, token, TimeSpan.FromMinutes(5));
                        var authenticated = await client.Request(method, parameters, token);
                        authenticationPending = false;
                        return authenticated;
                    }
                }
                object[] servers = [new { name = "big-blue-fish", command = bridge, args = new[] { "--pipe", pipe }, env = Array.Empty<object>() }];
                string stateFile = Workspaces.SessionPath(harnessId, skills.Workspace);
                AgentSession? saved = null;
                try { saved = JsonSerializer.Deserialize<AgentSession>(File.ReadAllText(stateFile)); }
                catch (Exception e) when (e is IOException or JsonException) { }
                bool load = client.Capabilities.TryGetProperty("loadSession", out var l) && l.ValueKind == JsonValueKind.True;
                bool resume = client.Capabilities.TryGetProperty("sessionCapabilities", out var s) && s.TryGetProperty("resume", out var resumeCapability)
                    && resumeCapability.ValueKind is JsonValueKind.Object or JsonValueKind.True;
                RestoresSessions = load || resume;
                sessionId = "";
                if (saved != null && string.Equals(saved.Executable, executable, AgentWorkspaceStore.PathComparison)
                    && string.Equals(saved.Workspace, skills.Workspace, AgentWorkspaceStore.PathComparison) && RestoresSessions)
                {
                    Report("正在恢复上次的鱼鱼对话…");
                    try
                    {
                        var restored = await SessionRequest(resume ? "session/resume" : "session/load", new { sessionId = saved.SessionId, cwd = skills.Workspace, mcpServers = servers });
                        token.ThrowIfCancellationRequested();
                        sessionId = saved.SessionId;
                    }
                    catch (AcpException e) when (e.SessionMissing) { Wakeups.Clear(harnessId, saved.SessionId); }
                }
                bool created = sessionId.Length == 0;
                if (created)
                {
                    if (saved != null) Wakeups.Clear(harnessId, saved.SessionId);
                    Report("正在建立鱼鱼专用对话…");
                    var result = await SessionRequest("session/new", new { cwd = skills.Workspace, mcpServers = servers });
                    token.ThrowIfCancellationRequested();
                    sessionId = result.GetProperty("sessionId").GetString() ?? throw new IOException("Agent 未返回对话编号。");
                    if (sessionId.Length == 0) throw new IOException("Agent 返回了空对话编号。");
                    AtomicText.Write(stateFile, JsonSerializer.Serialize(new AgentSession(sessionId, executable, skills.Workspace)));
                    guideFingerprint = null;
                }
                token.ThrowIfCancellationRequested();
                retryAfter = DateTimeOffset.MinValue;
                Report("已连接 · " + harness.Name, created && saved != null ? "已建立新对话。" : !RestoresSessions ? "双击鱼鱼即可对话；此 Agent 未提供重启后恢复会话的能力。" : "正在使用鱼鱼专用对话，双击鱼鱼即可输入。");
            }
            catch { if (connection == client) { connection = null; sessionId = ""; } await client.DisposeAsync(); throw; }
        }
        finally { connecting.Release(); }
    }
    private AcpConnection CreateConnection(ProcessStartInfo start)
    {
        var client = new AcpConnection(start, HandlePermission); connection = client;
        client.SessionStateReceived += data => { if (connection == client) { Configuration.Read(data); CurrentModel = Configuration.Model; Changed?.Invoke(); } };
        client.Notification += (method, data) => {
            if (connection != client) return;
            if (method == "session/update" && data.TryGetProperty("update", out var update)
                && (!data.TryGetProperty("sessionId", out var incomingSession) || sessionId.Length == 0 || incomingSession.GetString() == sessionId))
            {
                if (update.TryGetProperty("sessionUpdate", out var category) && category.GetString() == "tool_call") Interlocked.Increment(ref toolActivity);
                Configuration.Notification(update); CurrentModel = Configuration.Model; Changed?.Invoke();
            }
            ProtocolEvent?.Invoke(method, data);
        };
        client.Disconnected += reason => { if (connection == client) { sessionId = ""; Report("Agent 已断开", reason); } };
        return client;
    }
    private async Task DropConnection()
    {
        var old = Interlocked.Exchange(ref connection, null); sessionId = ""; guideFingerprint = null; CurrentModel = "由 Harness 配置"; Configuration.Reset();
        if (old != null) await old.DisposeAsync();
    }
    private Task<string?> HandlePermission(AgentPermission request, CancellationToken token)
    {
        // Enabling the connection already authorizes the fish's own tools. Other filesystem/shell work asks the user.
        string[] tools = ["get_settings", "update_settings", "list_actions", "capture_foreground", "capture_screen", "play_action", "interrupt_action", "say", "say_next", "request_local_speech", "get_operation_guide", "get_wakeups", "schedule_wakeup", "cancel_wakeup"];
        bool own = tools.Any(t => request.Title.Contains("mcp__big-blue-fish__" + t, StringComparison.Ordinal)
            || request.Title.Contains("@big-blue-fish/" + t, StringComparison.Ordinal)
            || request.Title.Equals("big-blue-fish_" + t, StringComparison.Ordinal)
            || request.Title.Equals(t + " (big-blue-fish MCP Server)", StringComparison.Ordinal));
        // CodeBuddy wraps deferred MCP calls in a generic tool; the qualified target remains in rawInput.
        try
        {
            using var document = JsonDocument.Parse(request.Detail);
            if (document.RootElement.TryGetProperty("rawInput", out var input) && input.ValueKind == JsonValueKind.Object
                && input.TryGetProperty("toolName", out var target) && target.ValueKind == JsonValueKind.String)
                own |= tools.Any(t => target.GetString() == "mcp__big-blue-fish__" + t);
        }
        catch (JsonException) { }
        string skillRoot = Path.GetFullPath(Skills.DirectoryPath) + Path.DirectorySeparatorChar;
        bool skillRead = request.Kind == "read" && request.Paths is { Length: > 0 } && request.Paths.All(p =>
        {
            try { return Path.GetFullPath(p).StartsWith(skillRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal); }
            catch (Exception) { return false; }
        });
        own |= skillRead || request.Title.StartsWith("\"big-blue-fish\":", StringComparison.Ordinal);
        if (own && request.Options.FirstOrDefault(x => x.Kind == "allow_once") is { } allow) return Task.FromResult<string?>(allow.Id);
        Report("等待你批准操作", request.Title);
        return permission(request, token);
    }

    public object ExecuteTool(string name, JsonElement args)
    {
        if (name == "get_operation_guide") { Skills.Install(); return new { guide = Skills.Guide(), skillPath = Skills.SkillPath, personaPath = Skills.PersonaPath }; }
        if (name == "get_wakeups") return new { reservations = Wakeups.List(harnessId, sessionId), active = mode.Enabled && mode.Puppet };
        if (name == "cancel_wakeup") return new { cancelled = Wakeups.Cancel(harnessId, sessionId, args.GetProperty("id").GetString()!) };
        if (name != "schedule_wakeup") throw new ArgumentException("未知预约工具。");
        if (!mode.Enabled || !mode.Puppet || !Connected) throw new InvalidOperationException("预约关心需要 PuppetMode 为 true，并连接 Agent。");
        string purpose = args.GetProperty("purpose").GetString()!;
        int delay = args.GetProperty("delay_seconds").GetInt32();
        int? repeat = args.TryGetProperty("repeat_seconds", out var r) && r.ValueKind != JsonValueKind.Null ? r.GetInt32() : null;
        string? id = args.TryGetProperty("id", out var i) ? i.GetString() : null;
        return Wakeups.Schedule(harnessId, sessionId, purpose, delay, repeat, id, DateTimeOffset.Now);
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel(); connectionLifetime.Cancel(); activeTurn?.Cancel(); events.Writer.TryComplete();
        lock (gate) while (events.Reader.TryRead(out var abandoned)) abandoned.Completion?.TrySetCanceled();
        var old = Interlocked.Exchange(ref connection, null); if (old != null) _ = old.DisposeAsync();
    }
}
