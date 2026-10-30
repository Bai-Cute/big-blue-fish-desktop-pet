using System.Globalization;
using System.Text.Json;
using Avalonia.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
#if !COMPANION_SAFE
using BigBlueFish.Control;
using BigBlueFish.Agent;
#endif

namespace BigBlueFish.Windows;

internal sealed partial class LinuxPetWindow
{
#if COMPANION_SAFE
    static bool McpAvailable => false;
    bool AgentDailyActive => false;
    void TickAgent(DateTime now) { }
    void ConfigureAgent() { }
    bool DispatchAgentDaily() => false;
    void InitializeMcp() { Preferences.McpEnabled = false; Preferences.PuppetMode = false; Preferences.McpWakeEnabled = false; }
    void StopMcp() { }
    void PublishInteraction(string kind, object detail) { }
    bool TryOpenMcpInput() => false;
#else
    static bool McpAvailable => true;
    ControlServer? mcpServer;
    AgentCoordinator? agent;
    readonly string agentPipe = ControlTools.PipeName + "." + Guid.NewGuid().ToString("N");
    bool AgentDailyActive => Preferences.AgentEnabled && Preferences.AgentDailySpeech;
    void ConfigureAgent() { agent?.Configure(Preferences.AgentHarnessId, new(Preferences.AgentEnabled, Preferences.PuppetMode, Preferences.AgentDailySpeech)); if (!AgentDailyActive) nextWarmup = DateTime.MinValue; }
    void TickAgent(DateTime now)
    {
        agent?.Tick(new DateTimeOffset(now), !hidden && !fullscreenHidden && !dragging && !closed && phase != "Exit");
        if (!generating) loading.IsVisible = agent?.AwaitingSpeech == true && !hidden && !fullscreenHidden;
    }
    bool DispatchAgentDaily()
    {
        if (!AgentDailyActive) return false;
        nextSpeech = DateTime.UtcNow.Add(Preferences.NextSpeechDelay(random)); agent?.DailyCare(); return true;
    }
    Window? mcpInput;
    bool McpInputAvailable => Preferences.AgentEnabled;
    void InitializeMcp()
    {
        if (!Preferences.AgentEnabled) return;
        mcpServer ??= new ControlServer(ExecuteMcp, agentPipe);
        agent ??= new AgentCoordinator(Path.Combine(Environment.CurrentDirectory, "agent"), Path.Combine(AppContext.BaseDirectory, "BigBlueFish.Mcp"), agentPipe, RequestAgentPermission,
            App.Args.Contains("--test-mode") ? Path.Combine(Environment.CurrentDirectory, "agent-test-workspace") : null);
        if (!HarnessCatalog.All.Any(x => x.Id == Preferences.AgentHarnessId)) Preferences.AgentHarnessId = "codex";
        agent.LocalSpeechRequested = token => Dispatcher.UIThread.InvokeAsync(() => RequestAgentLocalSpeech(token));
        ConfigureAgent();
        if (AgentDailyActive) brain.Stop();
    }
    void StopMcp() { consentWindow?.Close(); mcpInput?.Close(); agent?.Dispose(); agent = null; mcpServer?.Dispose(); mcpServer = null; externalActions.Clear(); nextExternalText = null; }
    void PublishInteraction(string kind, object detail)
    {
        mcpServer?.Publish(kind, detail);
        if (kind == "click" && Preferences.AgentRespondToClick || kind == "drag_end" && Preferences.AgentRespondToDrag) agent?.Interaction(kind, detail);
    }
    void SetPuppet(bool enabled)
    {
        if (Preferences.PuppetMode == enabled) return;
        Preferences.PuppetMode = enabled; speechVersion++; if (AgentDailyActive) brain.Stop(); else brain.CancelGeneration(); loading.IsVisible = false; moveRule = null;
        if (!externalSpeech) { typing = false; bubbleWindow.Hide(); }
        ConfigureAgent(); Idle();
        if (!enabled) { nextSpeech = DateTime.UtcNow.Add(Preferences.NextSpeechDelay(random)); nextEvent = DateTime.UtcNow.AddSeconds(Preferences.AnimationIntervalSeconds); nextWarmup = DateTime.MinValue; }
    }
    async Task RequestAgentLocalSpeech(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Preferences.AgentEnabled || AgentDailyActive) throw new InvalidOperationException("本地吐字需要连接 Agent 并关闭 AgentDailySpeech。");
        if (!Preferences.ModelEnabled || !brain.ModelAvailable || !brain.CanRunOnCurrentPower) throw new InvalidOperationException("本地模型未启用、尚未安装或已按供电设置暂停。");
        if (generating || closed || phase == "Exit" || modelInstalling || hidden || fullscreenHidden) throw new InvalidOperationException("鱼鱼当前忙碌或隐藏，请稍后再触发本地吐字。");
        using var registration = token.Register(() => Dispatcher.UIThread.Post(() => { speechVersion++; brain.CancelGeneration(); }));
        await Speak(false, true); token.ThrowIfCancellationRequested();
    }
    Dictionary<string, object?> CurrentSettings()
    {
        var result = typeof(CompanionPreferences).GetProperties().Where(p => !Attribute.IsDefined(p, typeof(System.Text.Json.Serialization.JsonIgnoreAttribute))).ToDictionary(p => p.Name, p => p.GetValue(Preferences));
        result["StartupEnabled"] = StartupEnabled; return result;
    }
    async Task<object> ExecuteMcp(string name, JsonElement args, CancellationToken token)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () => {
            token.ThrowIfCancellationRequested();
            if (closed || phase == "Exit" || !Preferences.McpEnabled) throw new InvalidOperationException("MCP 接口已关闭或桌宠正在退出。");
            switch (name)
            {
                case "get_settings": return ControlTools.Text(new { settings = CurrentSettings(), state = new { visible = IsVisible, hiddenByUser = hidden, fullscreenHidden, action = currentAction, animationPhase = phase, queuedActions = externalActions.ToArray(), queuedSpeech = nextExternalText, connections = mcpServer?.Connections ?? 0, inputOpen = mcpInput?.IsVisible == true, inputAvailable = McpInputAvailable, agentConnected = agent?.Connected == true, agentStatus = agent?.Status, agentSession = agent?.SessionId, nextWake = agent?.NextWake, modelAvailable = brain.ModelAvailable, modelBusy = brain.Busy, modelStatus = brain.Status } });
                case "update_settings": await UpdateSettings(args, token); return ControlTools.Text(new { settings = CurrentSettings() });
                case "request_local_speech": await RequestAgentLocalSpeech(token); return ControlTools.Text(new { generated = modelReply && bubbleWindow.IsVisible });
                case "list_actions": return ControlTools.Text(policy.Rules.Select(r => new { action = r.Id, pool = r.Pool, weight = r.Weight, duration_seconds = r.Pool == "Greeting" ? 10 / 1.5 : 10 }));
                case "capture_foreground": case "capture_screen":
                    var handle = CompanionNative.ResolveForeground(CompanionNative.GetForegroundWindow(), lastForeground);
                    var identity = name == "capture_foreground" ? CompanionNative.ForegroundIdentity(handle) : "全部显示器组成的完整桌面";
                    var limit = ControlTools.ImageLimit(args, name == "capture_foreground" ? 2560 : 4096);
                    var image = await LinuxCapture.CaptureAsync(handle, limit, name == "capture_screen", token);
                    token.ThrowIfCancellationRequested();
                    if (!Preferences.McpEnabled) throw new OperationCanceledException();
                    return ControlTools.Image(image.DataUrl, new { target = name, identity, image.Width, image.Height, capturedAt = DateTimeOffset.UtcNow });
                case "play_action": case "interrupt_action":
                    string action = ControlTools.RequiredText(args, "action", 200);
                    if (!policy.Rules.Any(r => r.Id == action)) throw new ArgumentException("动作不存在。");
                    if (name == "interrupt_action") { externalActions.Clear(); moveRule = null; Play(policy.Find(action), "External"); }
                    else { if (externalActions.Count >= 100) throw new InvalidOperationException("待播放动作队列已满。"); externalActions.Enqueue(action); }
                    return ControlTools.Text(new { action, started = name == "interrupt_action", queued = externalActions.Count });
                case "say": case "say_next":
                    string text = ControlTools.RequiredText(args, "text");
                    agent?.MarkSpoken();
                    if (name == "say") SayExternal(text); else nextExternalText = text;
                    return ControlTools.Text(new { displayed = name == "say", pending = name == "say_next", text });
                case "get_operation_guide": case "get_wakeups": case "schedule_wakeup": case "cancel_wakeup":
                    return ControlTools.Text(agent?.ExecuteTool(name, args) ?? throw new InvalidOperationException("Agent 尚未连接。"));
                default: throw new ArgumentException("未知 MCP 工具。");
            }
        });
    }
    async Task UpdateSettings(JsonElement args, CancellationToken token)
    {
        if (!args.TryGetProperty("settings", out var patch) || patch.ValueKind != JsonValueKind.Object) throw new ArgumentException("settings 必须为对象。");
        var values = new Dictionary<string, object?>();
        foreach (var property in patch.EnumerateObject())
        {
            if (!ControlTools.SettingNames.Contains(property.Name)) throw new ArgumentException(property.Name == "McpEnabled" ? "MCP 接口只能由用户在设置中开启或关闭。" : "未知设置：" + property.Name);
            var type = property.Name == "StartupEnabled" ? typeof(bool) : typeof(CompanionPreferences).GetProperty(property.Name)!.PropertyType;
            values[property.Name] = JsonSerializer.Deserialize(property.Value.GetRawText(), type);
        }
        foreach (var item in values)
        {
            bool Range(double low, double high) => item.Value is IConvertible c && double.IsFinite(c.ToDouble(null)) && c.ToDouble(null) >= low && c.ToDouble(null) <= high;
            bool valid = item.Key switch {
                "Scale" or "BubbleScale" => Range(.5, 2), "AnimationIntervalSeconds" => Range(30, 300),
                "BreakfastHour" or "LunchHour" or "DinnerHour" => Range(0, 23), "BubbleDurationIndex" => Range(0, 7),
                "SpeechMinMinutes" => item.Value is int minutes && CompanionPreferences.SpeechRanges.Any(r => r.Min == minutes),
                "WeatherRegionCode" => item.Value is string region && CompanionRegions.District(region).Code == region,
                "LastBrushDate" => item.Value == null || item.Value is string date && DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                "Left" or "Top" => item.Value == null || Range(-1000000, 1000000), _ => item.Value is bool };
            if (!valid) throw new ArgumentException("设置值无效：" + item.Key);
        }
        if (values.TryGetValue("PuppetMode", out var puppet) && puppet is true && !Preferences.PuppetMode
            && !await Confirm(ControlTools.BehaviorControlTitle, ControlTools.PuppetWarning, "了解并开启", token)) throw new OperationCanceledException("用户取消让 Agent 安排日常关心与动作。");
        token.ThrowIfCancellationRequested(); if (!Preferences.McpEnabled) throw new OperationCanceledException();
        foreach (var item in values)
            if (item.Key == "StartupEnabled") SetStartup((bool)item.Value!);
            else if (item.Key != "PuppetMode") typeof(CompanionPreferences).GetProperty(item.Key)!.SetValue(Preferences, item.Value);
        if (values.TryGetValue("PuppetMode", out puppet)) SetPuppet((bool)puppet!);
        var position = Position;
        if (values.TryGetValue("Left", out var left) && left is double x) position = new((int)x, position.Y);
        if (values.TryGetValue("Top", out var top) && top is double y) position = new(position.X, (int)y);
        ApplySettings(position);
        ConfigureAgent();
        if (!Preferences.McpChatEnabled) mcpInput?.Close();
        refreshLinuxSettings?.Invoke();
    }
    bool TryOpenMcpInput()
    {
        if (!McpInputAvailable || closed || phase == "Exit") return false;
        if (mcpInput != null) { mcpInput.Show(this); mcpInput.Activate(); return true; }
        const string hint = "通过鱼鱼和已连接的Agent说话";
        var text = new TextBox { AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxLength = 4000,
            Height = 56, Padding = new Thickness(0, 2, 22, 0), FontSize = 14, Foreground = Hair,
            Background = Avalonia.Media.Brushes.Transparent, BorderThickness = new Thickness(0), PlaceholderText = hint };
        Avalonia.Automation.AutomationProperties.SetName(text, hint);
        var editor = new Grid(); editor.Children.Add(text);
        var status = new TextBlock { Text = "Ctrl+Enter 发送", Foreground = Avalonia.Media.Brushes.SlateGray, FontSize = 11,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(0, 0, 8, 0) };
        var panel = new StackPanel { Spacing = 4 }; panel.Children.Add(editor);
        var frame = new Border { Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(255, 251, 244)),
            BorderBrush = Hair, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(12), Child = panel };
        var dialog = new Window { Title = "蓝色大肥鱼 · 输入", Width = 354, Height = 120,
            WindowDecorations = WindowDecorations.None, CanResize = false, ShowInTaskbar = false, ShowActivated = false,
            Topmost = true, Background = Avalonia.Media.Brushes.Transparent, TransparencyLevelHint = [WindowTransparencyLevel.Transparent], Content = frame };
        var close = new Button { Content = "×", Width = 20, Height = 20, Padding = new Thickness(0), FontSize = 16,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Foreground = Hair, Background = Avalonia.Media.Brushes.Transparent, BorderThickness = new Thickness(0) };
        Avalonia.Controls.ToolTip.SetTip(close, "收起输入框"); close.Click += (_, _) => dialog.Close(); editor.Children.Add(close);
        void Send() { if (agent?.Connected != true || string.IsNullOrWhiteSpace(text.Text) || !agent.SendUser(text.Text)) return; PublishInteraction("user_message", new { text = text.Text }); text.Text = ""; status.Text = "已发送给 Agent"; text.Focus(); }
        var send = ActionButton("发送", Send); send.MinWidth = 58; send.Padding = new Thickness(12, 5); send.FontSize = 12;
        var actions = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") }; actions.Children.Add(status); actions.Children.Add(send); Grid.SetColumn(send, 1); panel.Children.Add(actions);
        text.TextChanged += (_,_) => send.IsEnabled = agent?.Connected == true && !string.IsNullOrWhiteSpace(text.Text);
        dialog.AddHandler(InputElement.KeyDownEvent, (_,e) => {
            if (e.Key == Key.Escape) { e.Handled = true; dialog.Close(); }
            else if(e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Control) { e.Handled = true; Send(); }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        bool positioning = false;
        void Follow()
        {
            if (positioning || mcpInput != dialog) return;
            positioning = true;
            try {
                if (!IsVisible) { dialog.Hide(); return; }
                double scale = DesktopScaling;
                var area = (Screens.ScreenFromWindow(this) ?? Screens.Primary)?.WorkingArea ?? new PixelRect(0, 0, 1280, 720);
                dialog.Width = Math.Min(354, Math.Max(160, area.Width / scale - 16));
                int width = (int)Math.Ceiling(dialog.Width * scale), height = (int)Math.Ceiling(dialog.Height * scale), gap = (int)(8 * scale);
                int x = Position.X + (int)(SpriteSize * 208 / 360 * scale) - width - gap;
                if (x < area.X + gap) x = Position.X + (int)(SpriteSize * 428 / 360 * scale) + gap;
                int y = Position.Y + (int)(SpriteSize * 336 / 360 * scale) - height;
                dialog.Position = new(Math.Clamp(x, area.X + gap, Math.Max(area.X + gap, area.Right - width - gap)),
                    Math.Clamp(y, area.Y + gap, Math.Max(area.Y + gap, area.Bottom - height - gap)));
                if (!dialog.IsVisible) dialog.Show(this);
            } finally { positioning = false; }
        }
        EventHandler<PixelPointEventArgs> moved = (_, _) => Follow();
        EventHandler<AvaloniaPropertyChangedEventArgs> changed = (_, e) => {
            if (e.Property == IsVisibleProperty || e.Property == WidthProperty || e.Property == HeightProperty) Follow();
        };
        PositionChanged += moved; PropertyChanged += changed;
        var clock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        clock.Tick += (_,_) => {
            if (!Preferences.McpEnabled || !Preferences.McpChatEnabled) { dialog.Close(); return; }
            Follow();
            send.IsEnabled = agent?.Connected == true && !string.IsNullOrWhiteSpace(text.Text);
            if (agent?.Connected != true) status.Text = agent?.Status ?? "Agent 尚未连接";
            else if (status.Text != "已发送给 Agent") status.Text = agent.Busy ? "Agent 正在处理…" : "Ctrl+Enter 发送";
        };
        mcpInput = dialog; dialog.Closed += (_,_) => { clock.Stop(); PositionChanged -= moved; PropertyChanged -= changed; mcpInput = null; }; dialog.Opened += (_,_) => { text.Focus(); send.IsEnabled = false; };
        clock.Start(); Follow(); dialog.Activate(); return true;
    }
#endif
}
