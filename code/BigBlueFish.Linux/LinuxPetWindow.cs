using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace BigBlueFish.Windows;

internal sealed partial class LinuxPetWindow : Window
{
    internal readonly CompanionPreferences Preferences = CompanionPreferences.Read();
    readonly CompanionAnimationPolicy policy = CompanionAnimationPolicy.Load(Path.Combine(AppContext.BaseDirectory, "assets/fish"));
    readonly CompanionBrain brain;
    readonly Random random = new();
    readonly Image sprite = new() { Stretch = Stretch.Fill };
    readonly LinuxVideoPlayer player;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    readonly Window bubbleWindow;
    readonly TextBlock bubbleText = new() { TextWrapping = TextWrapping.Wrap };
    readonly LinuxBubble bubbleFrame;
    readonly LinuxLoadingRing loading = new() { IsVisible = false };
    readonly Stopwatch typingClock = new();
    readonly Queue<string> externalActions = new();
    readonly Mutex mutex;
    string currentAction = "", phase = "Startup", fullText = "";
    string? nextExternalText;
    DateTime nextSpeech = DateTime.UtcNow.AddSeconds(45), nextEvent = DateTime.MaxValue, nextWarmup;
    DateTime bubbleUntil = DateTime.MaxValue;
    IntPtr lastForeground;
    bool closed, exitReady, hidden, fullscreenHidden, dragging, dragged, generating, streamed, externalSpeech, typing, modelReply, greetingNoticeActive;
    long speechVersion;
    int[] textBoundaries = [];
    PixelPoint dragOrigin, windowOrigin, moveOrigin;
    PixelPoint lastClickPoint;
    long lastClickAt;
    double moveStart, moveEnd;
    CompanionAnimationRule? moveRule;
    bool moveMirrored;
    Window? settings;
    bool modelInstalling;
    static readonly IBrush Hair = new SolidColorBrush(Color.FromRgb(80, 96, 152));
    internal bool Puppet => McpAvailable && Preferences.McpEnabled && Preferences.PuppetMode;
    double SpriteSize => 280 * Preferences.Scale;

    internal LinuxPetWindow()
    {
        mutex = new Mutex(true, "BigBlueFish.Linux." + Environment.UserName, out var first);
        if (!first) Environment.Exit(0);
        brain = new() { UseGpu = Preferences.GpuEnabled, StopOnBattery = Preferences.StopModelOnBattery, WeatherRegionCode = Preferences.WeatherRegionCode };
        Title = "蓝色大肥鱼"; WindowDecorations = WindowDecorations.None; ShowInTaskbar = false; ShowActivated = false;
        Topmost = true; CanResize = false; Background = Brushes.Transparent; TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        var canvas = new Grid(); canvas.Children.Add(sprite); canvas.Children.Add(loading); Content = canvas;
        loading.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left; loading.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        player = new(sprite); player.FramePresented += MoveWithAnimation;
        PositionChanged += (_, _) => MoveBubble();
        player.PixelsPresented += (pixels, mirror) => CompanionNative.SetInputShape(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero, pixels, Width * DesktopScaling / 640, mirror);
        player.Failed += error => { File.AppendAllText("companion-errors.log", DateTime.Now.ToString("s") + " " + error + "\n"); if (phase == "Exit") CloseImmediately(); };
        bubbleText.FontFamily = new FontFamily("avares://BigBlueFish.Linux/Assets#LemiXiaoNaiPaoTi");
        bubbleText.Foreground = Hair;
        var close = new Button { Content = "×", Padding = new Thickness(3), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        close.Click += (_, _) => { typing = false; bubbleWindow!.Hide(); };
        var bubbleGrid = new Grid(); bubbleGrid.Children.Add(bubbleText); bubbleGrid.Children.Add(close); bubbleText.Margin = new Thickness(0, 0, 24, 0);
        bubbleFrame = new LinuxBubble { Child = bubbleGrid, Padding = new Thickness(16,16,16,30) };
        bubbleWindow = new Window { Title = "蓝色大肥鱼 · 对话", WindowDecorations = WindowDecorations.None, ShowInTaskbar = false, ShowActivated = false,
            Topmost = true, CanResize = false, Background = Brushes.Transparent, TransparencyLevelHint = [WindowTransparencyLevel.Transparent], Content = bubbleFrame };
        Rescale();
        var screen = Screens.Primary?.WorkingArea ?? new PixelRect(0, 0, 1280, 720);
        Position = new((int)(Preferences.Left ?? screen.Right - Width * DesktopScaling), (int)(Preferences.Top ?? screen.Bottom - Height * DesktopScaling)); Clamp();
        Opened += async (_, _) => {
            if (Puppet) Play(policy.Find("待机呼吸休闲"), "PuppetIdle");
            else { Play(policy.Find("女仆屈膝礼仪"), "Startup"); Welcome(); }
            InitializeMcp(); InitializeTray(); timer.Start();
            if (!File.Exists("model-selection.json") && !brain.ModelAvailable && !App.Args.Contains("--test-mode")) await InstallModel();
            if (Preferences.CheckUpdatesOnStartup && !App.Args.Contains("--test-mode")) _ = CheckUpdates(false);
        };
        PointerPressed += (_, e) => {
            var point = e.GetCurrentPoint(this);
            if (point.Properties.IsRightButtonPressed) { PublishInteraction("click", new { button = "right", Position.X, Position.Y }); OpenMenu(); e.Handled = true; return; }
            if (!point.Properties.IsLeftButtonPressed) return;
            if (e.ClickCount == 2 && TryOpenMcpInput()) { lastClickAt = 0; PublishInteraction("click", new { button = "left", count = 2, Position.X, Position.Y }); e.Handled = true; return; }
            dragging = true; dragged = false; moveRule = null; windowOrigin = Position;
            var screenPoint = this.PointToScreen(point.Position); dragOrigin = screenPoint;
            e.Pointer.Capture(this); e.Handled = true;
        };
        PointerMoved += (_, e) => {
            if (!dragging) return;
            var screenPoint = this.PointToScreen(e.GetPosition(this)); int dx = screenPoint.X - dragOrigin.X, dy = screenPoint.Y - dragOrigin.Y;
            if (!dragged && Math.Abs(dx) + Math.Abs(dy) > 4 * DesktopScaling) {
                dragged = true; PublishInteraction("drag_start", new { Position.X, Position.Y }); if (!Puppet) Play(policy.Find("被鼠标拖拽悬空反馈"), "Drag");
            }
            Position = new(windowOrigin.X + dx, windowOrigin.Y + dy); MoveBubble();
        };
        PointerReleased += (_, e) => {
            if (!dragging) return; dragging = false; e.Pointer.Capture(null); Clamp(); Save();
            if (dragged) { lastClickAt = 0; PublishInteraction("drag_end", new { Position.X, Position.Y, cancelled = false }); if (!Puppet && !PlayQueued()) Idle(); }
            else {
                PublishInteraction("click", new { button = "left", Position.X, Position.Y });
                var click = this.PointToScreen(e.GetPosition(this)); long at = Environment.TickCount64;
                bool twice = lastClickAt != 0 && at - lastClickAt <= 500 && Math.Abs(click.X-lastClickPoint.X)+Math.Abs(click.Y-lastClickPoint.Y) <= 8;
                lastClickAt = at; lastClickPoint = click;
                if (twice && TryOpenMcpInput()) lastClickAt = 0;
                else if (!Puppet) Play(policy.Choose("Click", Context(), random), "Click");
            }
            e.Handled = true;
        };
        PointerCaptureLost += (_, _) => { if (dragging) { dragging = false; if (dragged) PublishInteraction("drag_end", new { Position.X, Position.Y, cancelled = true }); if (!Puppet) Idle(); } };
        Closing += (_, e) => {
            Save();
            CloseModelInstaller();
            if (exitReady || closed || Puppet) return;
            e.Cancel = true; if (phase == "Exit") return;
            speechVersion++; brain.Stop(); moveRule = null; settings?.Close(); hidden = false; fullscreenHidden = false; Show(); player.Paused = false;
            Play(policy.Find("女仆屈膝礼仪"), "Exit"); ShowText("主人再见~鱼鱼喜欢主人哦", false, true);
        };
        Closed += (_, _) => { closed = true; timer.Stop(); StopMcp(); brain.Dispose(); player.Dispose(); bubbleWindow.Close(); settings?.Close(); Save(); mutex.Dispose(); (Avalonia.Application.Current!.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)!.Shutdown(); };
        timer.Tick += async (_, _) => await Tick();
    }
    CompanionAnimationContext Context() => new(DateTime.Now, BreakfastHour: Preferences.BreakfastHour, LunchHour: Preferences.LunchHour, DinnerHour: Preferences.DinnerHour);
    void Rescale()
    {
        Width = SpriteSize * 640 / 360; Height = SpriteSize; sprite.Width = Width; sprite.Height = Height;
        loading.Margin = new Thickness(SpriteSize * 412 / 360, SpriteSize * 50 / 360, 0, 0); MoveBubble();
    }
    PixelPoint Clamp(PixelPoint? requested = null)
    {
        var area = (Screens.ScreenFromWindow(this) ?? Screens.Primary)?.WorkingArea ?? new PixelRect(0, 0, 1280, 720);
        double s = DesktopScaling, left = SpriteSize * 208 / 360 * s, top = SpriteSize * 58 / 360 * s, right = SpriteSize * 428 / 360 * s, bottom = SpriteSize * 336 / 360 * s;
        var target = requested ?? Position;
        var result = new PixelPoint((int)Math.Clamp(target.X, area.X - left, Math.Max(area.X - left, area.Right - right)), (int)Math.Clamp(target.Y, area.Y - top, Math.Max(area.Y - top, area.Bottom - bottom)));
        Position = result; MoveBubble(); return result;
    }
    void Save() { if (closed) return; Preferences.Left = Position.X; Preferences.Top = Position.Y; Preferences.Save(); }
    void Play(CompanionAnimationRule rule, string nextPhase, bool mirror = false)
    {
        if (closed) return;
        if (phase == "Startup" && nextPhase != "Startup") EndWelcome();
        currentAction = rule.Id; phase = nextPhase;
        player.Play(Path.Combine(AppContext.BaseDirectory, "assets/fish", rule.File), mirror, Finished,
            nextPhase == "Drag" ? 1.5 : 0, nextPhase == "Drag" ? 6 : null, rule.Pool == "Greeting" ? 1.5 : 1);
    }
    void Idle() => Play(Puppet ? policy.Find("待机呼吸休闲") : policy.Choose("Idle", Context(), random), Puppet ? "PuppetIdle" : "Idle");
    bool PlayQueued() { if (externalActions.Count == 0) return false; moveRule = null; Play(policy.Find(externalActions.Dequeue()), "External"); return true; }
    void Finished()
    {
        if (closed) return;
        if (phase == "Exit") { CloseImmediately(); return; }
        if (!dragging && !hidden && !fullscreenHidden && PlayQueued()) return;
        if (Puppet) { Idle(); return; }
        if (phase == "Drag") return;
        if (phase == "Outbound" && moveRule != null) { Position = new((int)moveEnd, Position.Y); Play(policy.Choose("Idle", Context(), random), "BetweenMoves"); return; }
        if (phase == "BetweenMoves" && moveRule != null) { moveStart = Position.X; moveEnd = moveOrigin.X; Play(moveRule, "Return", !moveMirrored); return; }
        if (phase == "Return") { Position = new(moveOrigin.X, Position.Y); moveRule = null; }
        if (phase == "Startup") { EndWelcome(); nextEvent = DateTime.UtcNow.AddSeconds(Preferences.AnimationIntervalSeconds); }
        if (dragging || hidden || fullscreenHidden || settings != null) { Idle(); return; }
        var now = DateTime.Now;
        if (now.Hour == 23 && Preferences.LastBrushDate != now.ToString("yyyy-MM-dd")) { Preferences.LastBrushDate = now.ToString("yyyy-MM-dd"); Save(); Play(policy.Find("晨间刷牙"), "Daily"); return; }
        if (DateTime.UtcNow >= nextEvent) { nextEvent = DateTime.UtcNow.AddSeconds(Preferences.AnimationIntervalSeconds); Event(policy.Choose("Event", Context(), random)); } else Idle();
    }
    void Event(CompanionAnimationRule rule)
    {
        if (rule.MoveKind == null) { Play(rule, "Event"); return; }
        moveRule = rule; moveOrigin = Position; moveStart = Position.X;
        Position = new(Position.X - (int)(70 * Preferences.Scale * DesktopScaling), Position.Y); Clamp(); moveEnd = Position.X;
        if (Math.Abs(moveEnd - moveStart) < 15 * DesktopScaling) { Position = new((int)(moveStart + 70 * Preferences.Scale * DesktopScaling), Position.Y); Clamp(); moveEnd = Position.X; }
        Position = moveOrigin; moveMirrored = moveEnd > moveStart; Play(rule, "Outbound", moveMirrored);
    }
    void MoveWithAnimation(double seconds)
    {
        if (moveRule == null || phase is not ("Outbound" or "Return")) return;
        double lead = moveRule.MoveKind == "Run" ? 1.75 : 2, tail = moveRule.MoveKind == "Run" ? 4.8 : 2;
        double fraction = Math.Clamp((seconds - lead) / (10 - lead - tail), 0, 1);
        Position = new((int)(moveStart + (moveEnd - moveStart) * fraction), Position.Y); MoveBubble();
    }
    void Welcome()
    {
        string period = DateTime.Now.Hour switch { >= 5 and < 11 => "早上", >= 11 and < 14 => "中午", >= 14 and < 18 => "下午", _ => "晚上" };
        ShowText($"主人{period}好，鱼鱼给主人请安啦~", false, true);
        greetingNoticeActive = true;
    }
    void EndWelcome() { if (greetingNoticeActive) bubbleWindow.Hide(); greetingNoticeActive = false; }
    void ShowText(string text, bool external, bool immediate = false, bool fromModel = false)
    {
        greetingNoticeActive = false;
        fullText = text; textBoundaries = StringInfo.ParseCombiningCharacters(text); externalSpeech = external; modelReply = fromModel;
        typing = !immediate && textBoundaries.Length > 1; typingClock.Restart(); bubbleUntil = DateTime.MaxValue;
        bubbleText.Text = typing ? text[..textBoundaries[1]] : text;
        MoveBubble(); if (!hidden && !fullscreenHidden) bubbleWindow.Show(); if (!typing && phase is not ("Startup" or "Exit")) ResetDwell();
    }
    void Partial(string text, long expectedVersion)
    {
        Dispatcher.UIThread.Post(() => {
            if (expectedVersion != speechVersion || Puppet || closed || hidden || fullscreenHidden || !Preferences.ModelEnabled || !brain.CanRunOnCurrentPower) return;
            greetingNoticeActive = false; streamed = true; fullText = text; typing = false; externalSpeech = false; modelReply = true; bubbleText.Text = text; bubbleUntil = DateTime.MaxValue;
            MoveBubble(); bubbleWindow.Show();
        });
    }
    void ResetDwell() { int duration = CompanionPreferences.BubbleDurations[Preferences.BubbleDurationIndex]; bubbleUntil = duration == 0 ? DateTime.MaxValue : DateTime.UtcNow.AddSeconds(duration); }
    void MoveBubble()
    {
        if (bubbleFrame == null || bubbleWindow == null) return;
        double scale = Preferences.BubbleScale;
        bubbleText.FontSize = 17 * scale; bubbleText.Margin = new(0, 0, 24 * scale, 0); bubbleText.MaxWidth = 270 * scale;
        bubbleFrame.Padding = new(16 * scale, 16 * scale, 16 * scale, 30 * scale);
        string prefix = bubbleText.Text ?? ""; bubbleText.Text = fullText;
        bubbleFrame.Measure(new Size(320 * scale, double.PositiveInfinity)); var size = bubbleFrame.DesiredSize; bubbleText.Text = prefix;
        var area = (Screens.ScreenFromWindow(this) ?? Screens.Primary)?.WorkingArea ?? new PixelRect(0, 0, 1280, 720);
        bubbleWindow.Width = Math.Min(size.Width, area.Width / DesktopScaling - 16); bubbleWindow.Height = Math.Min(size.Height, area.Height / DesktopScaling - 16);
        int width = (int)(bubbleWindow.Width * DesktopScaling), height = (int)(bubbleWindow.Height * DesktopScaling);
        int x = Position.X + (int)(Width * DesktopScaling / 2) - width / 2, y = Position.Y + (int)(SpriteSize * 58 / 360 * DesktopScaling) - height - 10;
        if (y < area.Y) y = Position.Y + (int)(SpriteSize * 336 / 360 * DesktopScaling) + 10;
        bubbleWindow.Position = new(Math.Clamp(x, area.X + 8, Math.Max(area.X + 8, area.Right - width - 8)), Math.Clamp(y, area.Y + 8, Math.Max(area.Y + 8, area.Bottom - height - 8)));
    }
    async Task Tick()
    {
        if (closed || phase == "Exit") return;
        await TestCommands(); if (closed || phase == "Exit") return;
        var now = DateTime.UtcNow;
        lastForeground = CompanionNative.ResolveForeground(CompanionNative.GetForegroundWindow(), lastForeground);
        if (Preferences.PublicInfo) brain.RefreshWeather();
        if (!brain.CanRunOnCurrentPower) {
            if (brain.RunnerPid != null || brain.Busy) { speechVersion++; brain.Stop(); loading.IsVisible = false; }
            if (modelReply) bubbleWindow.Hide();
        }
        else if (!Puppet && !modelInstalling && Preferences.ModelEnabled && brain.ModelAvailable && !generating && !brain.Busy && brain.RunnerPid == null && now >= nextWarmup) { nextWarmup = now.AddMinutes(2); _ = brain.WarmUp(); }
        bool full = Preferences.HideOnFullscreen && CompanionNative.IsFullscreen();
        if (full && !fullscreenHidden && !hidden) { fullscreenHidden = true; Hide(); bubbleWindow.Hide(); speechVersion++; brain.CancelGeneration(); }
        else if (!full && fullscreenHidden) { fullscreenHidden = false; if (!hidden) { Show(); Clamp(); } }
        player.Paused = hidden || fullscreenHidden;
        if (hidden || fullscreenHidden) return;
        if (typing)
        {
            int count = Math.Min(textBoundaries.Length, 1 + (int)(typingClock.Elapsed.TotalSeconds * 28));
            bubbleText.Text = count >= textBoundaries.Length ? fullText : fullText[..textBoundaries[count]];
            if (count == textBoundaries.Length) { typing = false; ResetDwell(); }
        }
        if (now >= bubbleUntil) bubbleWindow.Hide();
        if (Puppet || dragging || settings != null || modelInstalling || now < nextSpeech) return;
        if (nextExternalText == null && (generating || brain.Busy || !Preferences.ModelEnabled || !brain.ModelAvailable || !brain.CanRunOnCurrentPower)) return;
        if (CompanionNative.IdleSeconds < 8 || CompanionNative.IdleSeconds > 600) { nextSpeech = now.AddSeconds(30); return; }
        if (nextExternalText != null) { string text = nextExternalText; nextExternalText = null; SayExternal(text); nextSpeech = now.Add(Preferences.NextSpeechDelay(random)); }
        else await Speak(false);
    }
    async Task Speak(bool weather)
    {
        if (Puppet || generating || closed || phase == "Exit" || modelInstalling) return;
        if (!brain.ModelAvailable) { await InstallModel(); return; }
        if (!Preferences.ModelEnabled) { ShowText("主人，请先在设置里启用本地大模型哦~", false); return; }
        if (!brain.CanRunOnCurrentPower) { ShowText("主人，当前设置为笔记本离电暂停模型，可以在设置里调节哦。", false); return; }
        generating = true; long version = ++speechVersion; streamed = false; loading.IsVisible = true;
        try
        {
            var until = DateTime.UtcNow.AddSeconds(100);
            while (brain.Busy && DateTime.UtcNow < until && version == speechVersion) await Task.Delay(100);
            if (brain.Busy || version != speechVersion || Puppet || hidden || fullscreenHidden) return;
            nextSpeech = DateTime.UtcNow.Add(Preferences.NextSpeechDelay(random));
            var text = weather ? await brain.GenerateWeather(p => Partial(p, version)) : await brain.Generate(Preferences.PublicInfo, Preferences.ForegroundEnabled, lastForeground, p => Partial(p, version));
            if (version != speechVersion || Puppet || hidden || fullscreenHidden || closed || !Preferences.ModelEnabled || !brain.CanRunOnCurrentPower) return;
            if (text.Length > 0) { if (streamed) { fullText = text; bubbleText.Text = text; ResetDwell(); } else ShowText(text, false, fromModel: true); }
            else if (streamed && !externalSpeech) bubbleWindow.Hide();
        }
        finally { generating = false; loading.IsVisible = false; }
    }
    void SayExternal(string text) { speechVersion++; brain.CancelGeneration(); loading.IsVisible = false; ShowText(text, true); }
    void OpenMenu()
    {
        var menu = new ContextMenu();
        MenuItem Item(string title, Action action, bool available = true) { var item = new MenuItem { Header = title, IsEnabled = available }; item.Click += (_, _) => action(); return item; }
        menu.ItemsSource = new[] { Item("设置", OpenSettings), Item("天气", async () => await Speak(true), !Puppet), Item("吐字测试", async () => await Speak(false), !Puppet),
            Item(hidden ? "显示桌宠" : "隐藏桌宠", () => { if (hidden) { hidden = false; Show(); Clamp(); } else { hidden = true; Hide(); bubbleWindow.Hide(); speechVersion++; brain.CancelGeneration(); } }), Item("关闭桌宠", Close) };
        ContextMenu = menu; menu.Open(this);
    }
    void CloseImmediately() { exitReady = true; Close(); }
}



