using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
#if !COMPANION_SAFE
using BigBlueFish.Control;
using BigBlueFish.Deployment;
#endif

namespace BigBlueFish.Windows;

public partial class MainWindow
{
    private bool externalBubble { get; set; }
#if COMPANION_SAFE
    private bool PuppetActive => false;
    private bool HasScheduledMcpSpeech => false;
    private void InitializeMcp() { preferences.McpEnabled = false; preferences.PuppetMode = false; }
    private void StopMcp() { }
    private bool PlayQueuedMcpAction() => false;
    private void PublishMcpInteraction(string kind, object detail) { }
    private bool ConsumeMcpSpeech() => false;
    private Action? AddMcpSettings(StackPanel panel, Window owner) => null;
    private bool TryOpenMcpInput() => false;
#else
    private ControlServer? mcpServer;
    private readonly Queue<string> mcpActions = new();
    private string? mcpNextSpeech;
    private bool mcpConsentOpen;
    private Window? mcpConsentWindow;
    private Window? mcpInput;
    private bool McpInputAvailable => preferences.McpEnabled && preferences.McpChatEnabled && (mcpServer?.Connections ?? 0) > 0;
    private bool PuppetActive => preferences.McpEnabled && preferences.PuppetMode;
    private bool HasScheduledMcpSpeech => mcpNextSpeech != null;

    private void InitializeMcp()
    {
        if (preferences.McpEnabled) mcpServer ??= new ControlServer(ExecuteMcp);
        if (PuppetActive) brain.Stop();
    }
    private void StopMcp()
    {
        mcpConsentWindow?.Close();
        mcpInput?.Close();
        mcpServer?.Dispose(); mcpServer = null;
        mcpActions.Clear(); mcpNextSpeech = null;
    }
    private void SetPuppetMode(bool enabled)
    {
        if (preferences.PuppetMode == enabled) return;
        preferences.PuppetMode = enabled;
        speechRequestVersion++; brain.Stop(); EndModelSpeech(); EndGreetingNotice();
        if (!externalBubble) { speech.Cancel(); bubble.Visibility = Visibility.Collapsed; }
        CancelMovement();
        if (enabled && companionReady) PlayAnimation(animationPolicy.Find("待机呼吸休闲"), "PuppetIdle");
        RefreshMcpMenuAvailability();
        if (!enabled)
        {
            nextSpeech = DateTime.UtcNow.Add(preferences.NextSpeechDelay(companionRandom));
            nextAction = DateTime.UtcNow.AddSeconds(preferences.AnimationIntervalSeconds);
            nextWarmup = DateTime.MinValue;
            PlayIdle();
        }
    }
    private void PublishMcpInteraction(string kind, object detail) => mcpServer?.Publish(kind, detail);
    private bool PlayQueuedMcpAction()
    {
        if (mcpActions.Count == 0) return false;
        CancelMovement();
        PlayAnimation(animationPolicy.Find(mcpActions.Dequeue()), "External");
        return true;
    }
    private void RefreshMcpMenuAvailability()
    {
        if (companionContextMenu != null) foreach (int index in new[] { 1, 2 }) if (companionContextMenu.Items[index] is MenuItem item) item.IsEnabled = !PuppetActive;
        if (notifyIcon?.ContextMenuStrip != null) foreach (int index in new[] { 2, 3 }) notifyIcon.ContextMenuStrip.Items[index].Enabled = !PuppetActive;
    }
    private void ShowMcpSpeech(string text)
    {
        speechRequestVersion++; brain.CancelGeneration(); EndModelSpeech(); EndGreetingNotice();
        externalBubble = true;
        bubbleIsModelReply = false;
        bubbleUntil = DateTime.MaxValue;
        speech.Show(text, true);
    }
    private bool ConsumeMcpSpeech()
    {
        if (mcpNextSpeech == null) return false;
        var text = mcpNextSpeech; mcpNextSpeech = null;
        nextSpeech = DateTime.UtcNow.Add(preferences.NextSpeechDelay(companionRandom));
        ShowMcpSpeech(text);
        return true;
    }
    private async Task<object> ExecuteMcp(string name, JsonElement args, CancellationToken token)
    {
        // All mutations, scheduling and target selection run on the ordinary WPF dispatcher.
        return await Dispatcher.InvokeAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            if (!preferences.McpEnabled || companionClosed || exitAnimation) throw new InvalidOperationException("MCP 接口已关闭或桌宠正在退出。");
            switch (name)
            {
                case "get_settings":
                    return ControlTools.Text(new { settings = CurrentMcpSettings(), state = new {
                        visible = IsVisible, hiddenByUser, fullscreenHidden, action = currentAction, animationPhase,
                        queuedActions = mcpActions.ToArray(), queuedSpeech = mcpNextSpeech, connections = mcpServer?.Connections ?? 0, inputOpen = mcpInput?.IsVisible == true, inputAvailable = McpInputAvailable,
                        modelAvailable = brain.ModelAvailable, modelBusy = brain.Busy, modelStatus = brain.Status } });
                case "update_settings":
                    await UpdateMcpSettings(args, token); return ControlTools.Text(new { settings = CurrentMcpSettings() });
                case "list_actions":
                    return ControlTools.Text(animationPolicy.Rules.Select(r => new { action = r.Id, pool = r.Pool, weight = r.Weight, duration_seconds = r.Pool == "Greeting" ? 10 / 1.5 : 10 }));
                case "capture_foreground": case "capture_screen":
                    var handle = CompanionNative.ResolveSpeechForeground(CompanionNative.GetForegroundWindow(), lastExternalForeground);
                    var identity = name == "capture_foreground" ? CompanionNative.ForegroundIdentity(handle) : "全部显示器组成的完整桌面";
                    var limit = ControlTools.ImageLimit(args, name == "capture_foreground" ? 2560 : 4096);
                    var image = await Task.Run(() => {
                        token.ThrowIfCancellationRequested();
                        return name == "capture_foreground" ? CompanionScreenCapture.Capture(handle, limit, true) : CompanionScreenCapture.CaptureScreen(limit);
                    }, token);
                    token.ThrowIfCancellationRequested();
                    if (!preferences.McpEnabled) throw new OperationCanceledException();
                    if (image == null) throw new IOException("未能截取屏幕。");
                    return ControlTools.Image(image.DataUrl, new { target = name, identity, image.Width, image.Height, capturedAt = DateTimeOffset.UtcNow });
                case "play_action": case "interrupt_action":
                    var action = ControlTools.RequiredText(args, "action", 200);
                    if (!companionReady || !animationPolicy.Rules.Any(r => r.Id == action)) throw new ArgumentException("动作不存在或播放器尚未就绪。");
                    if (name == "interrupt_action")
                    {
                        mcpActions.Clear(); CancelMovement();
                        PlayAnimation(animationPolicy.Find(action), "External");
                    }
                    else
                    {
                        if (mcpActions.Count >= 100) throw new InvalidOperationException("待播放动作队列已满。");
                        mcpActions.Enqueue(action);

                    }
                    return ControlTools.Text(new { action, started = name == "interrupt_action" || animationPhase == "External" && currentAction == action, queued = mcpActions.Count });
                case "say": case "say_next":
                    var text = ControlTools.RequiredText(args, "text");
                    if (name == "say") ShowMcpSpeech(text); else mcpNextSpeech = text;
                    return ControlTools.Text(new { displayed = name == "say", pending = name == "say_next", text });
                default: throw new ArgumentException("未知 MCP 工具。");
            }
        }).Task.Unwrap();
    }
    private Dictionary<string, object?> CurrentMcpSettings()
    {
        var result = typeof(CompanionPreferences).GetProperties().ToDictionary(p => p.Name, p => p.GetValue(preferences));
        result["StartupEnabled"] = StartupEnabled();
        return result;
    }
    private async Task UpdateMcpSettings(JsonElement args, CancellationToken token)
    {
        if (!args.TryGetProperty("settings", out var patch) || patch.ValueKind != JsonValueKind.Object) throw new ArgumentException("settings 必须为对象。");
        var values = new Dictionary<string, object?>();
        foreach (var property in patch.EnumerateObject())
        {
            if (!ControlTools.SettingNames.Contains(property.Name)) throw new ArgumentException(property.Name == "McpEnabled" ? "MCP 接口只能由用户在设置中开启或关闭。" : "未知设置：" + property.Name);
            if (property.Name == "StartupEnabled") { values[property.Name] = property.Value.GetBoolean(); continue; }
            var field = typeof(CompanionPreferences).GetProperty(property.Name)!;
            values[property.Name] = JsonSerializer.Deserialize(property.Value.GetRawText(), field.PropertyType);
        }
        // Validate the entire patch before applying any part of it.
        foreach (var item in values)
        {
            bool Range(double low, double high) => item.Value is IConvertible c && double.IsFinite(c.ToDouble(null)) && c.ToDouble(null) >= low && c.ToDouble(null) <= high;
            bool valid = item.Key switch {
                "Scale" or "BubbleScale" => Range(.5, 2),
                "AnimationIntervalSeconds" => Range(30, 300),
                "BreakfastHour" or "LunchHour" or "DinnerHour" => Range(0, 23),
                "BubbleDurationIndex" => Range(0, 7),
                "SpeechMinMinutes" => item.Value is int minutes && CompanionPreferences.SpeechRanges.Any(r => r.Min == minutes),
                "WeatherRegionCode" => item.Value is string region && CompanionRegions.District(region).Code == region,
                "LastBrushDate" => item.Value == null || item.Value is string date && DateTime.TryParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _),
                "Left" or "Top" => item.Value == null || Range(-1000000, 1000000),
                _ => item.Value is bool };
            if (!valid) throw new ArgumentException("设置值无效：" + item.Key);
        }
        if (values.TryGetValue("PuppetMode", out var puppet) && puppet is true && !preferences.PuppetMode
            && !await ConfirmMcp(companionSettings, "开启提线木偶模式", ControlTools.PuppetWarning, token)) throw new OperationCanceledException("用户取消开启提线木偶模式。");
        token.ThrowIfCancellationRequested();
        if (!preferences.McpEnabled) throw new OperationCanceledException();
        bool oldGpu = preferences.GpuEnabled, oldModel = preferences.ModelEnabled;
        string oldRegion = preferences.WeatherRegionCode;
        foreach (var item in values)
        {
            if (item.Key == "StartupEnabled") SetCompanionStartup((bool)item.Value!);
            else if (item.Key != "PuppetMode") typeof(CompanionPreferences).GetProperty(item.Key)!.SetValue(preferences, item.Value);
        }
        if (values.TryGetValue("PuppetMode", out puppet)) SetPuppetMode((bool)puppet!);
        if (oldGpu != preferences.GpuEnabled || oldModel && !preferences.ModelEnabled) { speechRequestVersion++; brain.Stop(); }
        brain.UseGpu = preferences.GpuEnabled; brain.WeatherRegionCode = preferences.WeatherRegionCode;
        if (oldRegion != preferences.WeatherRegionCode) { speechRequestVersion++; brain.CancelGeneration(); }
        ApplyCompanionPowerPolicy(); ApplyCompanionScale(false); ApplyCompanionBubbleScale(false);
        if (values.TryGetValue("Left", out var left) && left is double x) Left = x;
        if (values.TryGetValue("Top", out var top) && top is double y) Top = y;
        ClampCompanion(); anchor = new(Left, Top);
        if (!preferences.HideOnFullscreen && fullscreenHidden) { fullscreenHidden = false; if (!hiddenByUser) Show(); }
        if (values.ContainsKey("SpeechMinMinutes")) nextSpeech = DateTime.UtcNow.Add(preferences.NextSpeechDelay(companionRandom));
        if (values.ContainsKey("AnimationIntervalSeconds")) nextAction = DateTime.UtcNow.AddSeconds(preferences.AnimationIntervalSeconds);
        if (values.ContainsKey("BubbleDurationIndex") && bubble.Visibility == Visibility.Visible) ResetBubbleDeadline();
        SaveCompanion();
        refreshCompanionSettings?.Invoke();
    }
    private Action AddMcpSettings(StackPanel panel, Window owner)
    {
        var enabled = new CheckBox { Content = "开启 MCP 接口", IsChecked = preferences.McpEnabled, Margin = new(0, 12, 0, 4) };
        panel.Children.Add(enabled);
        panel.Children.Add(new TextBlock { Text = "开启后行为可被其他 Agent 控制", Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap });
        var extra = new StackPanel { Visibility = preferences.McpEnabled ? Visibility.Visible : Visibility.Collapsed, Margin = new(0, 8, 0, 0) };
        var puppet = new CheckBox { Content = "提线木偶模式", IsChecked = preferences.PuppetMode, Margin = new(0, 7, 0, 4) };
        extra.Children.Add(puppet);
        extra.Children.Add(new TextBlock { Text = "关闭大肥鱼自身的吐字和动作切换逻辑，完全由外部 Agent 接管", Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap });
        var chat = new CheckBox { Content = "双击与鱼鱼对话", IsChecked = preferences.McpChatEnabled, Margin = new(0, 16, 0, 4) };
        chat.Click += (_, _) => { preferences.McpChatEnabled = chat.IsChecked == true; if (!preferences.McpChatEnabled) mcpInput?.Close(); SaveCompanion(); };
        extra.Children.Add(chat);
        extra.Children.Add(new TextBlock { Text = "仅在 MCP 客户端已连接时生效，输入内容通过 MCP 发送给已连接的 Agent。", Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap });
        var config = new Button { Content = "复制 MCP 连接配置", Margin = new(0, 12, 0, 8) };
        config.Click += (_, _) => {
            var command = Path.Combine(AppContext.BaseDirectory, "BigBlueFish.Mcp.exe");
            Clipboard.SetText(JsonSerializer.Serialize(new { mcpServers = new Dictionary<string, object> { ["big-blue-fish"] = new { command, args = Array.Empty<string>() } } }, new JsonSerializerOptions { WriteIndented = true }));
            config.Content = "连接配置已复制";
        };
        extra.Children.Add(config);
        var status = new TextBlock { Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap };
        extra.Children.Add(status);
        var clock = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        clock.Tick += (_, _) => status.Text = $"已连接 {mcpServer?.Connections ?? 0} 个 MCP 客户端";
        clock.Start();
        panel.Children.Add(extra);
        enabled.Click += async (_, _) => {
            enabled.IsEnabled = false;
            try {
            bool on = enabled.IsChecked == true;
            if (on && !await ConfirmMcp(owner, "开启 MCP 接口", ControlTools.PrivacyWarning)) { enabled.IsChecked = false; return; }
            if (on && !File.Exists(Path.Combine(AppContext.BaseDirectory, "BigBlueFish.Mcp.exe"))) {
                ModernUI.Choice(owner, "MCP 组件未就绪", "请使用包含 MCP 组件的完整标准版安装包。", "确定"); enabled.IsChecked = false; return;
            }
            if (!on) SetPuppetMode(false);
            preferences.McpEnabled = on;
            if (on) InitializeMcp(); else StopMcp();
            extra.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            puppet.IsChecked = preferences.PuppetMode; SaveCompanion();
            } finally { enabled.IsEnabled = true; }
        };
        puppet.Click += async (_, _) => {
            puppet.IsEnabled = false;
            try {
            bool on = puppet.IsChecked == true;
            if (on && !await ConfirmMcp(owner, "开启提线木偶模式", ControlTools.PuppetWarning)) { puppet.IsChecked = false; return; }
            SetPuppetMode(on); SaveCompanion();
            } finally { puppet.IsEnabled = true; }
        };
        return clock.Stop;
    }
    private async Task<bool> ConfirmMcp(Window? owner, string title, string message, CancellationToken token = default)
    {
        if (mcpConsentOpen) throw new InvalidOperationException("请先处理已打开的 MCP 提示窗口。");
        mcpConsentOpen = true;
        try
        {
            var dialog = new ModernWindow(title, title, message, 640, 460);
            if (owner?.IsVisible == true) { dialog.Owner = owner; dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
            bool accepted = false;
            var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            dialog.Closed += (_, _) => finished.TrySetResult(accepted && !token.IsCancellationRequested);
            mcpConsentWindow = dialog;
            dialog.Actions.Children.Add(ModernUI.Button("了解并开启", () => { accepted = true; dialog.Close(); }, true));
            dialog.Actions.Children.Add(ModernUI.Button("取消", dialog.Close));
            using var registration = token.Register(() => Dispatcher.BeginInvoke(() => { accepted = false; dialog.Close(); }));
            dialog.Show();
            return await finished.Task;
        }
        finally { mcpConsentWindow = null; mcpConsentOpen = false; }
    }
    private bool TryOpenMcpInput()
    {
        if (!McpInputAvailable || exitAnimation || companionClosed) return false;
        if (mcpInput != null) { ActivateCompanionSettings(mcpInput); return true; }
        var window = new ModernWindow("与鱼鱼对话", "想和鱼鱼说些什么？", "通过已连接的 Agent 与鱼鱼说话。", 570, 480);
        var text = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 4000, Height = 130, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(12) };
        var status = new TextBlock { Foreground = Brushes.SlateGray, Margin = new Thickness(0, 12, 0, 0) };
        window.Body.Children.Add(text); window.Body.Children.Add(status);
        void Send()
        {
            if (!McpInputAvailable || string.IsNullOrWhiteSpace(text.Text)) return;
            PublishMcpInteraction("user_message", new { text = text.Text });
            text.Clear(); status.Text = "已发送给 Agent"; text.Focus();
        }
        var send = ModernUI.Button("发送 · Ctrl+Enter", Send, true); window.Actions.Children.Add(send); window.Actions.Children.Add(ModernUI.Button("关闭", window.Close));
        text.TextChanged += (_, _) => send.IsEnabled = McpInputAvailable && !string.IsNullOrWhiteSpace(text.Text);
        text.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control) { e.Handled = true; Send(); } };
        var clock = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        clock.Tick += (_, _) => {
            if (!preferences.McpEnabled || !preferences.McpChatEnabled) { window.Close(); return; }
            send.IsEnabled = McpInputAvailable && !string.IsNullOrWhiteSpace(text.Text);
            if (!McpInputAvailable) status.Text = "Agent 已断开，重新连接后可发送。";
            else if (status.Text.StartsWith("Agent 已断开")) status.Text = "Agent 已连接";
        };
        clock.Start(); mcpInput = window;
        window.Closed += (_, _) => { clock.Stop(); mcpInput = null; };
        window.Loaded += (_, _) => { text.Focus(); send.IsEnabled = false; };
        window.Show(); return true;
    }
#endif
}
