using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;

namespace BigBlueFish.Windows;

internal sealed partial class MacPetWindow : Window
{
    const string BubbleLayoutTestReply = "唔，给主人摸摸～鱼鱼乖乖低下头，小鲸鱼尾巴也跟着晃了晃。再摸一下嘛，嘿嘿💙";
    readonly CompanionPreferences preferences = CompanionPreferences.Read();
    readonly CompanionAnimationPolicy policy = CompanionAnimationPolicy.Load(Path.Combine(AppContext.BaseDirectory, "assets/fish"));
    readonly MacChatSettings chatSettings = MacChatSettings.Read();
    readonly MacLocalChat localChat = new();
    readonly Random random = new();
    readonly Image sprite = new() { Stretch = Stretch.Fill };
    readonly LinuxVideoPlayer player;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    readonly DispatcherTimer exitTimeout = new() { Interval = TimeSpan.FromSeconds(10) };
    readonly Window bubble;
    readonly TextBlock text = new() { TextWrapping = TextWrapping.Wrap, MaxWidth = 270, FontSize = 17, Foreground = new SolidColorBrush(Color.FromRgb(80,96,152)) };
    TrayIcon? tray;
    Window? settings;
    Action? saveSettingsAction;
    CancellationTokenSource? modelDownload;
    string? apiKey;
    string? apiKeyLoadError;
    byte[] alphaMask = new byte[640 * 360];
    bool hasAlphaMask, mirrored, dragging, dragged, exiting, closed, hidden, firstGreetingPending, skipClickRelease;
    bool contextMenuOpenedWhileAppActive;
    bool shutdownRequested;
    PixelPoint dragOrigin, windowOrigin;
    PixelPoint lastClickPoint;
    long lastClickAt;
    DateTime nextEvent, bubbleUntil;
    string phase = "Startup", decoderError = "";
    int ticks;
    internal double ChatWindowOpacity => chatSettings.WindowOpacity;

    internal MacPetWindow()
    {
        try { apiKey = MacApiKeyStore.Read(); }
        catch (IOException error) { apiKeyLoadError = error.Message; }
        Title = "蓝色大肥鱼"; WindowDecorations = WindowDecorations.None; ShowInTaskbar = false; ShowActivated = false;
        Topmost = true; CanResize = false; Background = Brushes.Transparent; TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Content = new Viewbox { Stretch = Stretch.Fill, Child = sprite };
        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
        text.FontFamily = new FontFamily("avares://BigBlueFish.Mac/Assets#LemiXiaoNaiPaoTi");
        bubble = new Window { Title = "蓝色大肥鱼 · 对话", WindowDecorations = WindowDecorations.None, ShowInTaskbar = false,
            ShowActivated = false, Topmost = true, CanResize = false, Background = Brushes.Transparent,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            Content = new LinuxBubble { Padding = new Thickness(16,16,16,30), Child = text } };
        string decoder;
        try { decoder = MacPlatform.Decoder(); }
        catch (FileNotFoundException error) { decoder = "/nonexistent/ffmpeg"; decoderError = error.Message; }
        player = new(sprite, decoder, rgba: true);
        player.PixelsPresented += (frame, mirror) => { hasAlphaMask = MacHitTest.CopyAlpha(frame, alphaMask); mirrored = mirror; };
        player.Failed += error => { decoderError = error; Log(error); if (exiting) FinishExit(); else Say("动画解码失败，请检查 FFmpeg。右键菜单或状态栏可以退出。"); };
        ResizePet();
        var area = Screens.Primary?.WorkingArea ?? new PixelRect(0,0,1280,720);
        Position = new((int)(preferences.Left ?? area.Right - Width * DesktopScaling), (int)(preferences.Top ?? area.Bottom - Height * DesktopScaling)); Clamp();
        PositionChanged += (_, _) => MoveBubble();
        Opened += (_, _) => {
            ResizePet();
            InitializeTray(); timer.Start();
            if (decoderError.Length > 0) { Say(decoderError); return; }
            if (App.Args.Contains("--test-mode") && App.Args.Contains("--bubble-layout")) {
                SayChatReply(BubbleLayoutTestReply);
                return;
            }
            firstGreetingPending = true;
            Play(policy.Find("女仆屈膝礼仪"), "Startup");
        };
        PointerPressed += (_, e) => {
            var point = e.GetCurrentPoint(this);
            if (point.Properties.IsRightButtonPressed) { OpenMenu(); e.Handled = true; return; }
            if (!point.Properties.IsLeftButtonPressed || exiting) return;
            if (e.ClickCount == 2) { skipClickRelease = true; dragging = false; lastClickAt = 0; OpenAgent(); e.Handled = true; return; }
            dragging = true; dragged = false; dragOrigin = this.PointToScreen(point.Position); windowOrigin = Position; e.Pointer.Capture(this);
        };
        PointerMoved += (_, e) => {
            if (!dragging) return; var point = this.PointToScreen(e.GetPosition(this)); int dx = point.X - dragOrigin.X, dy = point.Y - dragOrigin.Y;
            if (!dragged && Math.Abs(dx) + Math.Abs(dy) > 4 * DesktopScaling) { dragged = true; Play(policy.Find("被鼠标拖拽悬空反馈"), "Drag"); }
            if (dragged) Position = new(windowOrigin.X + dx, windowOrigin.Y + dy);
        };
        PointerReleased += (_, e) => {
            if (skipClickRelease) { skipClickRelease = false; e.Handled = true; return; }
            if (!dragging) return; dragging = false; e.Pointer.Capture(null); Clamp(); Save();
            if (dragged) { lastClickAt = 0; Idle(); return; }
            var point = this.PointToScreen(e.GetPosition(this)); long now = Environment.TickCount64;
            bool twice = lastClickAt != 0 && now - lastClickAt <= 500 &&
                Math.Abs(point.X - lastClickPoint.X) + Math.Abs(point.Y - lastClickPoint.Y) <= 8;
            lastClickAt = twice ? 0 : now; lastClickPoint = point;
            if (twice) OpenAgent(); else Play(policy.Choose("Click", Context(), random), "Click");
        };
        PointerCaptureLost += (_, _) => { if (dragging) { dragging = false; Clamp(); Save(); Idle(); } };
        Closing += (_, e) => { LifecycleLog("Closing event; exiting=" + exiting + ", closed=" + closed); if (closed) return; e.Cancel = true; Exit(); };
        Closed += (_, _) => {
            exitTimeout.Stop();
            try {
                LifecycleLog("Closed event"); modelDownload?.Cancel(); timer.Stop(); player.Dispose();
                _ = localChat.DisposeAsync().AsTask(); tray?.Dispose(); bubble.Close(); settings?.Close();
            }
            catch (Exception error) { try { Log(error.ToString()); } catch { } }
            finally { ShutdownApplication(); }
        };
        timer.Tick += (_, _) => Tick();
        exitTimeout.Tick += (_, _) => FinishExit();
    }
    static void LifecycleLog(string message) => File.AppendAllText("mac-lifecycle.log", DateTime.Now.ToString("s") + " " + message + Environment.NewLine);
    static void Log(string message) => File.AppendAllText("companion-errors.log", DateTime.Now.ToString("s") + " " + message + Environment.NewLine);
    CompanionAnimationContext Context() => new(DateTime.Now, BreakfastHour: preferences.BreakfastHour, LunchHour: preferences.LunchHour, DinnerHour: preferences.DinnerHour);
    void Play(CompanionAnimationRule rule, string next) { if (closed) return; phase = next; player.Play(Path.Combine(AppContext.BaseDirectory, "assets/fish", rule.File), false, Finished, next == "Drag" ? 1.5 : 0, next == "Drag" ? 6 : null, rule.Pool == "Greeting" ? 1.5 : 1); }
    void Idle() => Play(policy.Choose("Idle", Context(), random), "Idle");
    void Finished()
    {
        if (closed) return; if (exiting) { FinishExit(); return; } if (dragging) return;
        if (phase == "Startup") nextEvent = DateTime.UtcNow.AddSeconds(preferences.AnimationIntervalSeconds);
        if (DateTime.UtcNow >= nextEvent) { nextEvent = DateTime.UtcNow.AddSeconds(preferences.AnimationIntervalSeconds); Play(policy.Choose("Event", Context(), random), "Event"); } else Idle();
    }
    void ResizePet() { Height = 280 * preferences.Scale; Width = Height * 640 / 360; sprite.Width = Width; sprite.Height = Height; }
    void Clamp()
    {
        var area = (Screens.ScreenFromWindow(this) ?? Screens.Primary)?.WorkingArea ?? new PixelRect(0,0,1280,720);
        double s = Height * DesktopScaling / 360;
        Position = new((int)Math.Clamp(Position.X, area.X - 208*s, Math.Max(area.X - 208*s, area.Right - 428*s)),
            (int)Math.Clamp(Position.Y, area.Y - 58*s, Math.Max(area.Y - 58*s, area.Bottom - 336*s)));
    }
    void Save()
    {
        preferences.Left = Position.X; preferences.Top = Position.Y;
        try { preferences.Save(); } catch (IOException error) { Log(error.Message); Say("设置保存失败：" + error.Message); }
    }
    void Say(string message)
    {
        var size = MacBubbleLayout.Measure(text, message, 270);
        bubble.Width = Math.Ceiling(size.Width + 32);
        bubble.Height = Math.Ceiling(size.Height + 46);
        bubble.Show();
        bubble.UpdateLayout();
        MoveBubble();
        bubbleUntil = DateTime.UtcNow.AddSeconds(8);
    }
    void SayChatReply(string response)
    {
        var size = MacBubbleLayout.Measure(text, response, 270);
        var area = (Screens.ScreenFromWindow(this) ?? Screens.Primary)?.WorkingArea ?? new PixelRect(0, 0, 1280, 720);
        string display = MacBubbleReplyPolicy.ForBubble(response, chatSettings.BubbleCharacterLimit,
            size.Height + 46, area.Height / DesktopScaling);
        Say(display);
    }
    void MoveBubble()
    {
        if (bubble == null) return;
        var area = (Screens.ScreenFromWindow(this) ?? Screens.Primary)?.WorkingArea ?? new PixelRect(0, 0, 1280, 720);
        int width = (int)Math.Ceiling(bubble.Width * DesktopScaling), height = (int)Math.Ceiling(bubble.Height * DesktopScaling);
        int x = Position.X + (int)(Width * DesktopScaling * .32);
        int y = Position.Y + (int)(Height * DesktopScaling * .16) - 110;
        var target = new PixelPoint(
            Math.Clamp(x, area.X + 8, Math.Max(area.X + 8, area.Right - width - 8)),
            Math.Clamp(y, area.Y + 8, Math.Max(area.Y + 8, area.Bottom - height - 8)));
        if (bubble.Position != target) bubble.Position = target;
    }
    void Restore() { hidden = false; Show(); player.Paused = false; Clamp(); }
    void HidePet() { hidden = true; Hide(); bubble.Hide(); player.Paused = true; }
    async void Exit()
    {
        if (exiting) return; exiting = true; firstGreetingPending = false;
        exitTimeout.Start();
        try {
            try { await DisconnectAgent(); } catch (Exception error) { LogExitError("关闭 ACP", error); }
            try { await localChat.DisposeAsync(); } catch (Exception error) { LogExitError("关闭本机模型", error); }
            chat?.Close(); settings?.Close(); Save(); Restore();
            if (decoderError.Length > 0) { FinishExit(); return; }
            Play(policy.Find("女仆屈膝礼仪"), "Exit"); Say("主人再见～鱼鱼喜欢主人哦");
        }
        catch (Exception error) { LogExitError("完成关闭", error); FinishExit(); }
    }
    static void LogExitError(string operation, Exception error) { try { Log(operation + "失败：" + error); } catch { } }
    void FinishExit()
    {
        if (closed) return;
        closed = true; exitTimeout.Stop();
        try { Close(); }
        finally { ShutdownApplication(); }
    }
    void ShutdownApplication()
    {
        if (shutdownRequested) return;
        shutdownRequested = true;
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }
    void OpenMenu()
    {
        MenuItem Item(string label, Action action) { var item = new MenuItem { Header = label }; item.Click += (_, _) => action(); return item; }
        var menu = new ContextMenu { ItemsSource = new[] { Item("设置", OpenSettings), Item("打开对话", OpenAgent), Item("隐藏桌宠", HidePet), Item("关闭桌宠", Exit) } };
        menu.Opened += (_, _) => contextMenuOpenedWhileAppActive = MacPlatform.bbf_application_active() != 0;
        menu.Closed += (_, _) => {
            contextMenuOpenedWhileAppActive = false;
        };
        ContextMenu = menu;
        menu.Open(this);
    }
    void InitializeTray()
    {
        var menu = new NativeMenu();
        foreach (var (label, action) in new (string, Action)[] { ("显示桌宠", Restore), ("设置", OpenSettings), ("打开对话", OpenAgent), ("关闭桌宠", Exit) }) {
            var item = new NativeMenuItem(label); item.Click += (_, _) => action(); menu.Items.Add(item);
        }
        tray = new TrayIcon { Icon = new WindowIcon(Path.Combine(AppContext.BaseDirectory, "TrayIcon.png")), ToolTipText = "蓝色大肥鱼", Menu = menu, IsVisible = true };
        TrayIcon.SetIcons(Application.Current!, new TrayIcons { tray });
    }
    void OpenSettings()
    {
        if (settings != null) { settings.Activate(); return; }
        var draft = new MacChatSettings {
            Backend = chatSettings.Backend, AcpProvider = chatSettings.AcpProvider, AcpExecutable = chatSettings.AcpExecutable,
            Endpoint = chatSettings.Endpoint, Model = chatSettings.Model, ApiProtocol = chatSettings.ApiProtocol, LlamaServerExecutable = chatSettings.LlamaServerExecutable,
            GgufPath = chatSettings.GgufPath, AcpConsent = chatSettings.AcpConsent, WindowOpacity = chatSettings.WindowOpacity,
            BubbleCharacterLimit = chatSettings.BubbleCharacterLimit, PersonalityPrompt = chatSettings.PersonalityPrompt
        };
        var draftScale = preferences.Scale;
        var draftApiKey = apiKey;
        // A saved path is an explicit override. Discovery only fills an unset path.
        draft.AcpExecutable = MacAcpExecutableLocator.Resolve(draft.AcpProvider, draft.AcpExecutable);
        var acpPaths = new Dictionary<string, string>(StringComparer.Ordinal) { [draft.AcpProvider] = draft.AcpExecutable };
        var ink = new SolidColorBrush(Color.FromRgb(39, 49, 59));
        var muted = new SolidColorBrush(Color.FromRgb(105, 116, 126));
        var accent = new SolidColorBrush(Color.FromRgb(38, 137, 153));
        var divider = new SolidColorBrush(Color.FromRgb(226, 231, 235));
        var heading = new TextBlock { Text = "蓝色大肥鱼", FontSize = 21, FontWeight = FontWeight.SemiBold, Foreground = ink };
        var subtitle = new TextBlock { Text = "桌宠外观与对话服务", FontSize = 12, Foreground = muted };
        var panel = new StackPanel { Margin = new Thickness(24, 18, 24, 20), Spacing = 8, Background = Brushes.White };
        panel.Children.Add(heading); panel.Children.Add(subtitle);
        void Section(string label)
        {
            panel.Children.Add(new TextBlock { Text = label, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = ink, Margin = new Thickness(0, 9, 0, 0) });
            panel.Children.Add(new Border { Height = 1, Background = divider, Margin = new Thickness(0, 1, 0, 2) });
        }
        Section("桌宠外观");
        var scaleLabel = new TextBlock { Text = $"桌宠大小  ·  {Math.Round(draftScale * 100):0}%", FontSize = 12, Foreground = ink };
        panel.Children.Add(scaleLabel);
        var scale = new Slider { Minimum = 50, Maximum = 200, Value = preferences.Scale * 100, TickFrequency = 5, IsSnapToTickEnabled = true };
        scale.ValueChanged += (_, _) => { draftScale = scale.Value / 100; scaleLabel.Text = $"桌宠大小  ·  {Math.Round(scale.Value):0}%"; }; panel.Children.Add(scale);
        var opacityLabel = new TextBlock { Text = $"对话窗口透明度  ·  {Math.Round(draft.WindowOpacity * 100):0}%", FontSize = 12, Foreground = ink, Margin = new Thickness(0, 2, 0, 0) };
        panel.Children.Add(opacityLabel);
        var opacity = new Slider { Minimum = 65, Maximum = 100, Value = chatSettings.WindowOpacity * 100, TickFrequency = 5, IsSnapToTickEnabled = true };
        opacity.ValueChanged += (_, _) => { draft.WindowOpacity = opacity.Value / 100; opacityLabel.Text = $"对话窗口透明度  ·  {Math.Round(opacity.Value):0}%"; }; panel.Children.Add(opacity);

        Section("聊天气泡");
        var bubbleLimitRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        bubbleLimitRow.Children.Add(new TextBlock { Text = "直接显示阈值（字）", FontSize = 12, Foreground = ink, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        var bubbleLimit = new NumericUpDown {
            Minimum = MacChatSettings.MinimumBubbleCharacterLimit, Maximum = MacChatSettings.MaximumBubbleCharacterLimit,
            Increment = 10, Value = draft.BubbleCharacterLimit, FormatString = "0", Width = 130, Height = 34,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
        };
        bubbleLimit.ValueChanged += (_, _) => draft.BubbleCharacterLimit = (int)(bubbleLimit.Value ?? MacChatSettings.DefaultBubbleCharacterLimit);
        Grid.SetColumn(bubbleLimit, 1); bubbleLimitRow.Children.Add(bubbleLimit);
        panel.Children.Add(bubbleLimitRow);
        panel.Children.Add(new TextBlock {
            Text = "超过阈值或气泡高度不适合当前屏幕时，气泡提示查看聊天窗；聊天记录保留全文。",
            FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = muted
        });

        Section("大肥鱼人格");
        panel.Children.Add(new TextBlock {
            Text = "用于 API 与本机模型；发送到 ACP Agent 时会附带在每轮消息中。ACP Agent 自身的系统设定仍可能优先。",
            FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = muted
        });
        var personalityHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        personalityHeader.Children.Add(new TextBlock { Text = "角色提示词", FontSize = 12, Foreground = ink, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        var resetPersonality = new Button {
            Content = "恢复默认", Padding = new Thickness(9, 4), Background = Brushes.White,
            Foreground = accent, BorderBrush = divider, BorderThickness = new Thickness(1)
        };
        Grid.SetColumn(resetPersonality, 1); personalityHeader.Children.Add(resetPersonality);
        var personality = new TextBox {
            Text = draft.PersonalityPrompt, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            MinHeight = 118, MaxLength = 4000, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Top,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, Padding = new Thickness(9, 8),
            Foreground = ink, Background = Brushes.White, BorderBrush = divider, BorderThickness = new Thickness(1)
        };
        personality.TextChanged += (_, _) => draft.PersonalityPrompt = personality.Text ?? "";
        resetPersonality.Click += (_, _) => personality.Text = MacChatSettings.DefaultPersonalityPrompt;
        panel.Children.Add(personalityHeader); panel.Children.Add(personality);

        Section("对话服务");
        var backend = new ComboBox { ItemsSource = new[] { "API 接口", "本机 GGUF 模型", "ACP Agent" }, SelectedIndex = draft.Backend switch { "gguf" => 1, "acp" => 2, _ => 0 }, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, Height = 34 };
        panel.Children.Add(backend);
        var backendFields = new StackPanel { Spacing = 6 };
        panel.Children.Add(backendFields);
        var modelSection = new StackPanel { Spacing = 7, Margin = new Thickness(0, 5, 0, 0) };
        panel.Children.Add(modelSection);
        TextBox Field(string label, string value, bool password = false)
        {
            backendFields.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = ink });
            var box = new TextBox { Text = value, PlaceholderText = label, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, Height = 34, Padding = new Thickness(9, 6) };
            if (password) box.PasswordChar = '•';
            backendFields.Children.Add(box); return box;
        }
        TextBox? endpoint = null, model = null, key = null, llama = null, gguf = null, acp = null;
        ComboBox? provider = null, protocol = null; CheckBox? consent = null;
        string? renderedBackend = null;
        void RenderModelDownload()
        {
            modelSection.Children.Clear();
            if (backend.SelectedIndex != 1) return;
            modelSection.Children.Add(new TextBlock { Text = "原仓库 4B 模型", FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = ink, Margin = new Thickness(0, 3, 0, 0) });
            modelSection.Children.Add(new TextBlock { Text = "Qwen3.5-4B · Q4_K_M GGUF，约 2.7 GB。Hugging Face 为 Heretic 版，国内源为原版 Qwen。下载完成后仍需填写本机 llama-server 路径。", FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = muted });
            var source = new ComboBox { ItemsSource = MacModelDownload.Sources.Select(x => x.Label).ToArray(), SelectedIndex = 0, IsEnabled = modelDownload == null, Height = 34 };
            modelSection.Children.Add(source);
            var status = new TextBlock { Text = modelDownload == null ? "选择来源后点击下载；文件会保存到应用数据目录。" : "模型正在下载；可暂停并保留进度。", FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = muted };
            var progress = new ProgressBar { Minimum = 0, Maximum = 100, IsVisible = modelDownload != null };
            var download = new Button { Content = modelDownload == null ? "下载 4B 模型" : "暂停下载", Background = accent, Foreground = Brushes.White, Padding = new Thickness(15, 7), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
            download.Click += async (_, _) => {
                if (modelDownload != null) { modelDownload.Cancel(); return; }
                var selected = MacModelDownload.Sources[Math.Clamp(source.SelectedIndex, 0, MacModelDownload.Sources.Count - 1)];
                var operation = modelDownload = new CancellationTokenSource();
                source.IsEnabled = false; download.Content = "暂停下载"; status.Text = "准备下载…"; progress.IsVisible = true;
                try {
                    var reporter = new Progress<MacModelProgress>(value => {
                        if (!ReferenceEquals(modelDownload, operation) || settings?.IsVisible != true) return;
                        progress.Value = value.Percent;
                        status.Text = $"已下载 {value.Received / 1048576d:0.0} MB" + (value.Total > 0 ? $" / {value.Total / 1048576d:0.0} MB" : "");
                    });
                    var path = await MacModelDownload.DownloadAsync(selected.Id, Environment.CurrentDirectory, reporter, operation.Token);
                    if (operation.IsCancellationRequested || settings?.IsVisible != true) return;
                    draft.GgufPath = path; if (gguf != null) gguf.Text = path;
                    progress.Value = 100; status.Text = "下载与 SHA-256 校验完成。点击“保存设置”启用；如未安装 llama-server，请先填写它的路径。";
                } catch (OperationCanceledException) { status.Text = "已暂停；下次点击可继续下载。"; }
                catch (Exception error) { status.Text = "下载失败：" + error.Message; }
                finally {
                    if (ReferenceEquals(modelDownload, operation)) modelDownload = null;
                    operation.Dispose(); source.IsEnabled = true; download.Content = "下载 / 继续 4B 模型";
                }
            };
            modelSection.Children.Add(download); modelSection.Children.Add(progress); modelSection.Children.Add(status);
            var existing = BigBlueFish.Deployment.ModelStore.Resolve(Environment.CurrentDirectory);
            if (existing != null && string.IsNullOrWhiteSpace(draft.GgufPath)) { draft.GgufPath = existing; if (gguf != null) gguf.Text = existing; }
        }
        void CaptureBackendFields()
        {
            if (renderedBackend == null) return;
            // Keep the path paired with the provider even while switching backends.
            if (acp != null) { draft.AcpProvider = provider?.SelectedIndex switch { 1 => "claude", 2 => "codex", _ => "opencode" }; draft.AcpExecutable = acp.Text ?? ""; acpPaths[draft.AcpProvider] = draft.AcpExecutable; }
            if (endpoint != null) draft.Endpoint = endpoint.Text ?? "";
            if (model != null) draft.Model = model.Text ?? "";
            if (protocol != null) draft.ApiProtocol = protocol.SelectedIndex == 1 ? "responses" : "chat";
            if (key != null) draftApiKey = key.Text;
            if (llama != null) draft.LlamaServerExecutable = llama.Text ?? "";
            if (gguf != null) draft.GgufPath = gguf.Text ?? "";
            if (consent != null) draft.AcpConsent = consent.IsChecked == true;
        }
        void RenderBackend()
        {
            CaptureBackendFields(); backendFields.Children.Clear(); endpoint = model = key = llama = gguf = acp = null; provider = protocol = null; consent = null;
            renderedBackend = backend.SelectedIndex switch { 1 => "gguf", 2 => "acp", _ => "api" };
            if (backend.SelectedIndex == 0) {
                backendFields.Children.Add(new TextBlock { Text = "接口格式", FontSize = 12, Foreground = ink });
                protocol = new ComboBox { ItemsSource = new[] { "OpenAI 兼容（Chat Completions）", "OpenAI Responses" }, SelectedIndex = draft.ApiProtocol == "responses" ? 1 : 0, Height = 34 };
                backendFields.Children.Add(protocol);
                endpoint = Field("Base URL（可填写 HTTPS 服务地址）", draft.Endpoint);
                model = Field("模型名称", draft.Model);
                key = Field("API Key（保存在 macOS 钥匙串）", draftApiKey ?? "", true);
                backendFields.Children.Add(new TextBlock { Text = "API Key 仅保存在 macOS 钥匙串。", FontSize = 11, Foreground = muted });
                if (apiKeyLoadError != null) backendFields.Children.Add(new TextBlock { Text = apiKeyLoadError, FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(180, 60, 50)), TextWrapping = TextWrapping.Wrap });
            }
            else if (backend.SelectedIndex == 1) { llama = Field("llama-server 绝对路径", draft.LlamaServerExecutable); gguf = Field("GGUF 模型绝对路径", draft.GgufPath); }
            else {
                provider = new ComboBox { ItemsSource = new[] { "OpenCode ACP", "Claude ACP", "Codex ACP" }, SelectedIndex = draft.AcpProvider switch { "claude" => 1, "codex" => 2, _ => 0 }, Height = 34 }; backendFields.Children.Add(new TextBlock { Text = "Agent 类型", FontSize = 12, Foreground = ink }); backendFields.Children.Add(provider);
                var pathHelp = new TextBlock { Foreground = muted, TextWrapping = TextWrapping.Wrap, FontSize = 11 };
                void UpdateAcpPath()
                {
                    var selected = provider.SelectedIndex switch { 1 => "claude", 2 => "codex", _ => "opencode" };
                    if (selected != draft.AcpProvider) {
                        acpPaths[draft.AcpProvider] = acp?.Text ?? draft.AcpExecutable;
                        draft.AcpProvider = selected;
                        draft.AcpExecutable = acpPaths.TryGetValue(selected, out var previous) ? previous : MacAcpExecutableLocator.Find(selected);
                        if (acp != null) acp.Text = draft.AcpExecutable;
                    }
                    pathHelp.Text = string.IsNullOrWhiteSpace(draft.AcpExecutable)
                        ? "未找到对应 ACP 程序；安装后可重新打开设置，或手动填写绝对路径。"
                        : "已填入可执行文件路径；可手动修改。";
                }
                acp = Field("ACP 可执行文件绝对路径", draft.AcpExecutable);
                acp.TextChanged += (_, _) => { draft.AcpExecutable = acp.Text ?? ""; acpPaths[draft.AcpProvider] = draft.AcpExecutable; pathHelp.Text = "可手动修改；保存后使用此路径。"; };
                provider.SelectionChanged += (_, _) => UpdateAcpPath();
                UpdateAcpPath();
                backendFields.Children.Add(pathHelp);
                consent = new CheckBox { Content = "我理解 Agent 可能读写文件、运行命令或联网", IsChecked = draft.AcpConsent, Foreground = ink, FontSize = 12, Margin = new Thickness(0, 3, 0, 0) }; backendFields.Children.Add(consent);
            }
        }
        backend.SelectionChanged += (_, _) => { RenderBackend(); RenderModelDownload(); }; RenderBackend(); RenderModelDownload();
        void CommitSettings()
        {
            CaptureBackendFields();
            draft.Backend = backend.SelectedIndex switch { 1 => "gguf", 2 => "acp", _ => "api" };
            try {
                // A blank key removes the stored credential. Save it before committing the
                // visible settings so a Keychain failure leaves the draft open.
                if (draftApiKey != apiKey) MacApiKeyStore.Save(draftApiKey);
                draft.Save();
            }
            catch (IOException error) {
                // Keychain errors may be handled here; never place secret values in logs.
                Log(error.Message); Say("设置保存失败：" + error.Message); return;
            }
            chatSettings.Backend = draft.Backend; chatSettings.Endpoint = draft.Endpoint; chatSettings.Model = draft.Model; chatSettings.ApiProtocol = draft.ApiProtocol;
            chatSettings.LlamaServerExecutable = draft.LlamaServerExecutable; chatSettings.GgufPath = draft.GgufPath;
            chatSettings.AcpProvider = draft.AcpProvider; chatSettings.AcpExecutable = draft.AcpExecutable;
            chatSettings.AcpConsent = draft.AcpConsent; chatSettings.WindowOpacity = draft.WindowOpacity;
            chatSettings.BubbleCharacterLimit = draft.BubbleCharacterLimit;
            chatSettings.PersonalityPrompt = draft.PersonalityPrompt;
            apiKey = draftApiKey; apiKeyLoadError = null; preferences.Scale = draftScale; ResizePet(); Clamp(); ApplyChatOpacity(chatSettings.WindowOpacity); SetChatStatus(BackendStatus());
            Save(); settings?.Close();
        }
        saveSettingsAction = CommitSettings;
        var saveSettings = new Button { Content = "保存设置", Background = accent, Foreground = Brushes.White, Padding = new Thickness(20, 8), MinWidth = 92, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        saveSettings.Click += (_, _) => CommitSettings();
        var footer = new Border { Background = Brushes.White, BorderBrush = divider, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(24, 10), Child = saveSettings };
        var layout = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Background = Brushes.White };
        layout.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto });
        Grid.SetRow(footer, 1); layout.Children.Add(footer);
        settings = new Window { Title = "蓝色大肥鱼 · 设置", Width = 560, Height = 700, MinWidth = 480, MinHeight = 540, Background = Brushes.White, Opacity = 1, TransparencyLevelHint = [WindowTransparencyLevel.None], FontSize = 13, Content = layout, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        settings.Closed += (_, _) => { modelDownload?.Cancel(); settings = null; saveSettingsAction = null; }; settings.Show(); settings.Activate();
    }
    void Tick()
    {
        if (closed) return;
        if (ContextMenu?.IsOpen == true && contextMenuOpenedWhileAppActive && MacPlatform.bbf_application_active() == 0)
            ContextMenu.Close();
        var handle = TryGetPlatformHandle();
        if (handle != null && handle.Handle != IntPtr.Zero && MacPlatform.bbf_pointer(handle.Handle, out var x, out var y) != 0)
            MacPlatform.bbf_clickthrough(handle.Handle, dragging || !hasAlphaMask || ContextMenu?.IsOpen == true || MacHitTest.Contains(alphaMask, x, y, Width, Height, mirrored) ? 0 : 1);
        if (firstGreetingPending && phase == "Startup" && player.Frames > 0 && Bounds.Width > 0 && Bounds.Height > 0) {
            firstGreetingPending = false;
            Say("主人好，鱼鱼在 Mac 上给主人请安啦～");
        }
        if (bubble.IsVisible) MoveBubble();
        if (DateTime.UtcNow >= bubbleUntil) bubble.Hide();
        if (!App.Args.Contains("--test-mode")) return;
        if (++ticks % 25 == 0) File.WriteAllText("test-status.json", JsonSerializer.Serialize(new { phase, frames = player.Frames, decoderError, hidden, applicationActive = MacPlatform.bbf_application_active() != 0, contextMenuOpen = ContextMenu?.IsOpen == true, contextMenuOpenedWhileAppActive, settingsOpen = settings?.IsVisible == true, settingsOpacity = settings?.Opacity ?? 0, settingsBackground = settings?.Background?.ToString() ?? "", agentOpen = chat?.IsVisible == true, agentConnected = agentSession.Connected, chatWidth = chat?.Bounds.Width ?? 0, chatHeight = chat?.Bounds.Height ?? 0, chatOpacity = chat?.Opacity ?? ChatWindowOpacity, scale = preferences.Scale, desktopScaling = DesktopScaling, x = Position.X, y = Position.Y, width = Width, height = Height, boundsWidth = Bounds.Width, boundsHeight = Bounds.Height, spriteWidth = sprite.Bounds.Width, spriteHeight = sprite.Bounds.Height, bubbleText = text.Text, bubbleTextLength = text.Text?.Length ?? 0, bubbleTextDesiredWidth = text.DesiredSize.Width, bubbleTextDesiredHeight = text.DesiredSize.Height, bubbleTextWidth = text.Bounds.Width, bubbleTextHeight = text.Bounds.Height, bubbleWidth = bubble.Bounds.Width, bubbleHeight = bubble.Bounds.Height, bubbleX = bubble.Position.X, bubbleY = bubble.Position.Y, handle = handle?.HandleDescriptor }));
        if (!File.Exists("test-command.txt")) return; var command = File.ReadAllText("test-command.txt").Trim(); File.Delete("test-command.txt");
        switch (command) { case "context-menu": OpenMenu(); break; case "settings": OpenSettings(); break; case "close-settings": settings?.Close(); break; case "save-settings": saveSettingsAction?.Invoke(); break; case "agent": OpenAgent(); break; case "hide": HidePet(); break; case "show": Restore(); break; case "exit": Exit(); break; }
    }
}
