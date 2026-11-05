using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BigBlueFish.Diagnostics;

public enum DiagnosticEvent
{
    AppStarted, AppStopped, SettingsOpened, SettingsClosed, SettingsChanged, VisibilityChanged,
    SessionRecovery, Interaction, AnimationStarted, AnimationCompleted, AnimationFailed, TrayState, AppFailed,
    SpeechRequested, SpeechSkipped, SpeechStarted, SpeechCompleted, SpeechCancelled, SpeechFailed,
    ScreenReadStarted, ScreenReadCompleted, ScreenReadFallback, BubbleShown, BubbleClosed,
    RunnerStarting, RunnerReady, RunnerStopped, RunnerFailed, RunnerCpuFallback,
    WeatherRefresh, WeatherCached, WeatherFailed, NewsFetch, NewsSelected, NewsFailed,
    AgentConnecting, AgentConnected, AgentDisconnected, AgentRequest, AgentMaterials, AgentReply,
    AgentCancelled, AgentFailed, AgentPermission, AgentConfiguration, AgentWorkspace, McpTool,
    LogExported, LogExportFailed
}

public sealed record DiagnosticEntry(long Sequence, DateTimeOffset Time, string Level, string Code, string Message, string Details)
{
    public override string ToString() => $"{Time:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level}] [{Code}] {Message}" + (Details.Length == 0 ? "" : " | " + Details);
}

/// <summary>Shareable metadata only. No API accepts prompt text, OCR, images or exception messages.</summary>
public static class DiagnosticLog
{
    public const int Capacity = 32768;
    static readonly object gate = new();
    static readonly Queue<DiagnosticEntry> entries = new();
    static readonly byte[] pathSalt = RandomNumberGenerator.GetBytes(16);
    static long sequence, revision;
    static string appDirectory = Path.GetFullPath(AppContext.BaseDirectory), dataDirectory = Path.GetFullPath(Environment.CurrentDirectory);
    static readonly HashSet<string> tokens = new(("start success failed cancelled blocked ready stopped hidden visible fullscreen user automatic test weather local agent "
        + "persona news screen image ocr text GPU CPU cache cold warm startup exit idle Event Idle Click Drag Greeting Daily Startup Exit External PuppetIdle "
        + "Outbound Return BetweenMoves click drag_start drag_end user_message daily_care speech_test weather_request wakeup start_care connect reconnect new_conversation "
        + "set_configuration authentication permission read write model mode thought_level other unavailable unsupported empty expired changed enabled disabled "
        + "Standard Safe OCR Vision Windows Linux Preview timeout first_content hardware_fallback notice manual busy power model_missing puppet "
        + "get_settings update_settings list_actions capture_foreground capture_screen play_action interrupt_action say say_next request_local_speech "
        + "get_operation_guide get_wakeups schedule_wakeup cancel_wakeup terminal oauth restored created "
        + "claude codex gemini dsh kimi qwen antigravity copilot cursor opencode cline codebuddy qoder trae kiro pi devin grok hermes openclaw minimax").Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
    static readonly HashSet<string> actions = LoadActions();
    static readonly Dictionary<string, string> previousSettings = new();
    static readonly HashSet<string> settingNames = new(("ModelEnabled GpuEnabled PublicInfo ForegroundEnabled StopModelOnBattery HideOnFullscreen CheckUpdatesOnStartup "
        + "AgentEnabled AgentHarnessId AgentDailySpeech PuppetMode AgentRespondToClick AgentRespondToDrag WeatherRegionCode Scale BubbleScale SpeechMinMinutes BubbleDurationIndex "
        + "AnimationIntervalSeconds BreakfastHour LunchHour DinnerHour LastBrushDate Left Top StartupEnabled "
        + "McpEnabled McpDoubleClickChat McpWakeEnabled McpWakeOnText McpWakeOnClick McpWakeOnDrag AgentWorkspaceId").Split(' '), StringComparer.Ordinal);
    static HashSet<string> LoadActions()
    {
        using var stream = typeof(DiagnosticLog).Assembly.GetManifestResourceStream("BuiltinActions.json")!;
        using var json = JsonDocument.Parse(stream);
        return json.RootElement.EnumerateArray().Select(x => x.GetProperty("Id").GetString()!).ToHashSet(StringComparer.Ordinal);
    }
    public static long Revision { get { lock (gate) return revision; } }
    public static DiagnosticEntry[] Snapshot() { lock (gate) return entries.ToArray(); }
    public static void Clear() { lock (gate) { entries.Clear(); revision++; } }
    public static void ConfigureDirectories(string app, string data)
    {
        lock (gate) { appDirectory = Path.GetFullPath(app); dataDirectory = Path.GetFullPath(data); }
    }
    public static void ObserveSettings(object settings)
    {
        try { ObserveSettingsCore(settings); } catch { /* Diagnostics cannot interrupt settings or pet operation. */ }
    }
    static void ObserveSettingsCore(object settings)
    {
        var changed = new List<string>();
        lock (gate)
        {
            foreach (var p in settings.GetType().GetProperties().Where(p => settingNames.Contains(p.Name)))
            {
                string hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(p.GetValue(settings))));
                if (!previousSettings.TryGetValue(p.Name, out var before) || before != hash) changed.Add(p.Name);
                previousSettings[p.Name] = hash;
            }
        }
        if (changed.Count > 0) Write(DiagnosticEvent.SettingsChanged, changed: changed);
    }
    static string Token(string value) => tokens.Contains(value) ? value : "other";
    public static void Write(DiagnosticEvent code, string? kind = null, string? state = null, int? count = null,
        int? chars = null, double? elapsedMs = null, bool? enabled = null, Exception? error = null,
        string? action = null, string? directory = null, IEnumerable<string>? changed = null)
    {
        try { WriteCore(code, kind, state, count, chars, elapsedMs, enabled, error, action, directory, changed); }
        catch { /* Logging is best effort and must not break the operation being diagnosed. */ }
    }
    static void WriteCore(DiagnosticEvent code, string? kind, string? state, int? count,
        int? chars, double? elapsedMs, bool? enabled, Exception? error,
        string? action, string? directory, IEnumerable<string>? changed)
    {
        var fields = new List<string>();
        if (kind != null) fields.Add("kind=" + Token(kind));
        if (state != null) fields.Add("state=" + Token(state));
        if (count != null) fields.Add("count=" + Math.Max(0, count.Value).ToString(CultureInfo.InvariantCulture));
        if (chars != null) fields.Add("chars=" + Math.Max(0, chars.Value).ToString(CultureInfo.InvariantCulture));
        if (elapsedMs != null && double.IsFinite(elapsedMs.Value)) fields.Add("elapsed_ms=" + Math.Max(0, elapsedMs.Value).ToString("0.##", CultureInfo.InvariantCulture));
        if (enabled != null) fields.Add("enabled=" + enabled.Value.ToString().ToLowerInvariant());
        if (action != null) fields.Add("action=" + (actions.Contains(action) ? action : "<unknown-action>"));
        if (directory != null) fields.Add("directory=" + DirectoryAlias(directory));
        if (changed != null) fields.Add("changed=" + string.Join(",", changed.Where(settingNames.Contains).Distinct().Order()));
        if (error != null)
        {
            fields.Add("exception=" + error.GetType().Name);
            fields.Add("hresult=0x" + error.HResult.ToString("X8", CultureInfo.InvariantCulture));
            if (error is System.ComponentModel.Win32Exception win32) fields.Add("win32=" + win32.NativeErrorCode);
            if (error is HttpRequestException http && http.StatusCode != null) fields.Add("http_status=" + (int)http.StatusCode);
            var methods = new StackTrace(error, false).GetFrames()?.Select(x => x.GetMethod())
                .Where(x => x?.DeclaringType?.Namespace?.StartsWith("BigBlueFish", StringComparison.Ordinal) == true)
                .Take(5).Select(x => x!.DeclaringType!.Name + "." + x.Name).ToArray();
            if (methods is { Length: > 0 }) fields.Add("at=" + string.Join(" > ", methods));
        }
        string level = error != null || state is "failed" or "unsupported" ? "WARN" : "INFO";
        lock (gate)
        {
            entries.Enqueue(new(++sequence, DateTimeOffset.Now, level, code.ToString(), Message(code), string.Join("; ", fields)));
            while (entries.Count > Capacity) entries.Dequeue();
            revision++;
        }
    }
    public static string DirectoryAlias(string directory)
    {
        string full;
        try { full = Path.GetFullPath(directory); } catch { return "<invalid-directory>"; }
        lock (gate)
        {
            foreach (var (basis, label) in new[] { (dataDirectory, "<Data>"), (appDirectory, "<App>") })
            {
                var relative = Path.GetRelativePath(basis, full);
                if (relative == ".") return label;
                if (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    var safe = new HashSet<string>(["data", "models", "cache", "agent", "adapters", "workspaces", "logs", "runtime", "media", "gstreamer", "assets", "fish"], StringComparer.OrdinalIgnoreCase);
                    return label + "/" + string.Join("/", relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries).Select(p => safe.Contains(p) ? p : "<private>"));
                }
            }
        }
        return "<ExternalDirectory#" + Convert.ToHexString(SHA256.HashData(pathSalt.Concat(Encoding.UTF8.GetBytes(full)).ToArray()))[..8] + ">";
    }
    public static string ExportText()
    {
        var snapshot = Snapshot();
        return "蓝色大肥鱼 · 排障日志\nVersion: " + typeof(DiagnosticLog).Assembly.GetName().Version!.ToString(3)
            + "\nOS: " + RuntimeInformation.OSDescription + "\nArchitecture: " + RuntimeInformation.ProcessArchitecture
            + "\nRuntime: " + RuntimeInformation.FrameworkDescription + "\nEntries: " + snapshot.Length + "/" + Capacity
            + "\n仅包含本次运行的排障元数据；目录已匿名化。\n\n" + string.Join(Environment.NewLine, snapshot.Select(x => x.ToString())) + Environment.NewLine;
    }
    public static void SaveText(string path) => File.WriteAllText(path, ExportText(), new UTF8Encoding(true));
    static string Message(DiagnosticEvent code) => code switch
    {
        DiagnosticEvent.AppStarted => "桌宠启动", DiagnosticEvent.AppStopped => "桌宠退出", DiagnosticEvent.AppFailed => "桌宠运行异常", DiagnosticEvent.TrayState => "托盘图标状态变化",
        DiagnosticEvent.SettingsOpened => "打开设置", DiagnosticEvent.SettingsClosed => "关闭设置", DiagnosticEvent.SettingsChanged => "设置已保存",
        DiagnosticEvent.VisibilityChanged => "桌宠可见状态变化", DiagnosticEvent.SessionRecovery => "恢复桌面会话并清理旧任务", DiagnosticEvent.Interaction => "收到桌宠交互",
        DiagnosticEvent.AnimationStarted => "开始播放动作", DiagnosticEvent.AnimationCompleted => "动作播放结束", DiagnosticEvent.AnimationFailed => "动画播放失败",
        DiagnosticEvent.SpeechRequested => "收到吐字请求", DiagnosticEvent.SpeechSkipped => "本次吐字未执行", DiagnosticEvent.SpeechStarted => "开始准备吐字",
        DiagnosticEvent.SpeechCompleted => "模型吐字生成完成", DiagnosticEvent.SpeechCancelled => "吐字已取消", DiagnosticEvent.SpeechFailed => "吐字生成失败",
        DiagnosticEvent.ScreenReadStarted => "开始读取前台画面", DiagnosticEvent.ScreenReadCompleted => "前台资料准备完成", DiagnosticEvent.ScreenReadFallback => "读屏不可用，静默使用剩余资料生成",
        DiagnosticEvent.BubbleShown => "文字已交给气泡显示", DiagnosticEvent.BubbleClosed => "气泡已关闭",
        DiagnosticEvent.RunnerStarting => "启动本地推理程序", DiagnosticEvent.RunnerReady => "本地模型已就绪", DiagnosticEvent.RunnerStopped => "推理程序已停止",
        DiagnosticEvent.RunnerFailed => "推理程序发生故障", DiagnosticEvent.RunnerCpuFallback => "切换到 CPU 兼容模式",
        DiagnosticEvent.WeatherRefresh => "开始刷新天气", DiagnosticEvent.WeatherCached => "使用天气缓存", DiagnosticEvent.WeatherFailed => "天气获取失败",
        DiagnosticEvent.NewsFetch => "获取科技新闻", DiagnosticEvent.NewsSelected => "本轮选用新闻资料", DiagnosticEvent.NewsFailed => "新闻获取失败",
        DiagnosticEvent.AgentConnecting => "开始连接 Agent", DiagnosticEvent.AgentConnected => "Agent 会话已连接", DiagnosticEvent.AgentDisconnected => "Agent 连接已关闭",
        DiagnosticEvent.AgentRequest => "向 Agent 提交事件", DiagnosticEvent.AgentMaterials => "准备或送交 Agent 资料", DiagnosticEvent.AgentReply => "Agent 本轮处理结束",
        DiagnosticEvent.AgentCancelled => "Agent 请求已取消", DiagnosticEvent.AgentFailed => "Agent 请求失败", DiagnosticEvent.AgentPermission => "Agent 请求权限或认证",
        DiagnosticEvent.AgentConfiguration => "调整 Agent 会话配置", DiagnosticEvent.AgentWorkspace => "Agent 工作区状态变化", DiagnosticEvent.McpTool => "收到 MCP 工具调用",
        DiagnosticEvent.LogExported => "排障日志已保存为 TXT", DiagnosticEvent.LogExportFailed => "排障日志保存失败", _ => code.ToString()
    };
}
