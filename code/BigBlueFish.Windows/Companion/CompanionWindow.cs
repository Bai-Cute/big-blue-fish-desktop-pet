using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace BigBlueFish.Windows;
public partial class MainWindow
{
    private CompanionPreferences preferences = null !;
    private CompanionBrain brain = null !;
    private DispatcherTimer companionTimer = null !;
    private Border bubble = null !;
    private TextBlock bubbleText = null !;
    private Border loadingIndicator = null !;
    private CompanionLoadingRing loadingSpinner = null !;
    private CompanionSpeech speech = null!;
    private bool bubbleIsModelReply;
    private long speechRequestVersion;
    private Grid companionLayout = null !;
    private Window? companionSettings;
    private ContextMenu companionContextMenu = null!;
    private CompanionMenuDismissal? companionMenuDismissal;
    private bool weatherRequestPending;
    private bool speechRequestPending;
    private DateTime nextSpeech, nextAction, bubbleUntil;
    private bool companionReady, companionClosed, dragged, dragging, hiddenByUser, fullscreenHidden;
    private Point dragStart, windowStart, anchor;
    private readonly Random companionRandom = new();
    private DateTime lastFrame = DateTime.UtcNow;
    private DateTime nextWarmup = DateTime.MinValue;
    private bool hookInstalled;
    private DateTime diagnosticAt;
    private DateTime lastClickAt;
    private Point lastClickPoint;
    private DispatcherTimer? singleClickTimer;

    private void InvalidateSpeechRequests(bool stopRunner = false)
    {
        speechRequestVersion++;
        speechRequestPending = weatherRequestPending = false;
        if (stopRunner) brain.Stop(); else brain.CancelGeneration();
        EndModelSpeech();
    }

    private void RecoverCompanionSession()
    {
        InvalidateSpeechRequests(true);
        companionMenuDismissal?.Reset();
        singleClickTimer?.Stop();
        lastClickAt = DateTime.MinValue;
        if (IsMouseCaptured) ReleaseMouseCapture();
        dragging = false;
        CancelMovement();
        if (companionReady && !exitAnimation) PlayIdle();
        notifyIcon?.EnsureRegistered();
        nextWarmup = DateTime.MinValue;
        nextSpeech = DateTime.UtcNow.Add(preferences.NextSpeechDelay(companionRandom));
        UpdateCompanionVisibility();
    }

    private void UpdateCompanionVisibility()
    {
        fullscreenHidden = preferences.HideOnFullscreen && CompanionNative.IsFullscreen();
        bool show = !hiddenByUser && !fullscreenHidden;
        if (show && !IsVisible) { Show(); ClampCompanion(); }
        else if (!show && IsVisible) { InvalidateSpeechRequests(); Hide(); }
        if (videoPlayer != null) videoPlayer.Paused = !exitAnimation && !show;
    }
    private readonly IntPtr startupForeground = CompanionNative.GetForegroundWindow();
    private IntPtr lastExternalForeground;
    private IntPtr? speechForegroundOverride;
    private string currentAction = "idle";
    private System.Threading.Mutex? singleInstance;
    private FileStream? instanceLock;
    private double SpriteSize => 280 * preferences.Scale;
    internal double CompanionSpriteSize => SpriteSize;
    internal Border CompanionBubble => bubble;
    internal TextBlock CompanionBubbleText => bubbleText;
    internal double CompanionBubbleScale => preferences.BubbleScale;
    internal string CompanionBubbleLayoutText => speech?.FullText ?? bubbleText.Text;

    private void InitializeCompanion()
    {
        singleInstance = new System.Threading.Mutex(true, "Local\\BlueWhaleCompanion", out var first);
        if (!first)
        {
            System.Windows.Application.Current.Shutdown();
            return;
        }

        instanceLock = File.Open("companion.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        preferences = CompanionPreferences.Read();
        if (CompanionNative.IsExternalForegroundCandidate(startupForeground))
            lastExternalForeground = startupForeground;
        brain = new CompanionBrain
        {
            UseGpu = preferences.GpuEnabled,
            StopOnBattery = preferences.StopModelOnBattery,
            WeatherRegionCode = preferences.WeatherRegionCode
        };
        InitializeComponent();
        System.Windows.Shell.WindowChrome.SetWindowChrome(this, null);
        AllowsTransparency = true;
        Background = null;
        WindowStyle = WindowStyle.None;
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        Title = "蓝色大肥鱼";
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Manual;
        companionLayout = new Grid
        {
            Background = null
        };
        bubbleText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 17,
            Foreground = new SolidColorBrush(global::BigBlueFish.Windows.CompanionBubble.HairBlue),
            FontFamily = new FontFamily(new Uri("pack://application:,,,/BigBlueFish;component/"),
                "./Res/Font/#乐米小奶泡体"),
            Focusable = false
        };
        var bubbleContent = new Grid();
        bubbleText.Margin = new Thickness(0, 0, 26, 0);
        bubbleContent.Children.Add(bubbleText);
        var dismiss = new Button
        {
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Focusable = false,
            IsTabStop = false,
            Cursor = Cursors.Hand,
            ToolTip = "关闭气泡",
            Content = new TextBlock
            {
                Text = "\uE711",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 10,
                Foreground = new SolidColorBrush(global::BigBlueFish.Windows.CompanionBubble.HairBlue),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        System.Windows.Automation.AutomationProperties.SetName(dismiss, "关闭气泡");
        dismiss.Click += (_, e) =>
        {
            e.Handled = true;
            speech.Cancel();
            bubble.Visibility = Visibility.Collapsed;
        };
        bubbleContent.Children.Add(dismiss);
        bubble = new CompanionBubble
        {
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 300,
            Visibility = Visibility.Collapsed,
            Child = bubbleContent
        };
        companionLayout.Children.Add(bubble);
        ApplyCompanionBubbleScale(false);
        loadingSpinner = new CompanionLoadingRing
        {
            Width = 24,
            Height = 24
        };
        loadingIndicator = new Border
        {
            Width = 34,
            Height = 34,
            Padding = new Thickness(4),
            Background = new SolidColorBrush(Color.FromArgb(248, 255, 251, 244)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(35, 80, 96, 152)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(17),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            { Color = global::BigBlueFish.Windows.CompanionBubble.HairBlue, BlurRadius = 7, ShadowDepth = 1, Opacity = .16 },
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            Child = loadingSpinner
        };
        Panel.SetZIndex(loadingIndicator, 20);
        companionLayout.Children.Add(loadingIndicator);
        speech = new CompanionSpeech(bubbleText, bubble, () => CompanionEdgeLayout.Refresh(this), ResetBubbleDeadline);
        Content = companionLayout;
        ApplyCompanionScale(false);
        var a = SystemParameters.WorkArea;
        Left = preferences.Left ?? (a.Right - Width - 12);
        Top = preferences.Top ?? (a.Bottom - Height + 15);
        ClampCompanion();
        anchor = new Point(Left, Top);
        SourceInitialized += (_, _) => InstallCompanionHook();
        Loaded += async (_, _) =>
        {
            InstallCompanionHook();
            await LoadCompanionAnimations();
            if (preferences.CheckUpdatesOnStartup && !App.Args.Contains("--test-mode"))
            {
                await Task.Delay(3000);
                await CheckForUpdates(false);
            }
        };
        BuildCompanionContextMenu();
        PreviewMouseRightButtonDown += (_, e) =>
        {
            e.Handled = true;
            if (exitAnimation) return;
            lastExternalForeground = CompanionNative.ResolveSpeechForeground(CompanionNative.GetForegroundWindow(), lastExternalForeground);
            PublishMcpInteraction("click", new { button = "right", Left, Top });
            companionContextMenu.IsOpen = true;
        };
        PreviewMouseLeftButtonDown += CompanionDown;
        PreviewMouseMove += CompanionMove;
        PreviewMouseLeftButtonUp += CompanionUp;
        LostMouseCapture += (_, _) =>
        {
            if (dragging) { dragging = false; if (dragged) PublishMcpInteraction("drag_end", new { Left, Top, cancelled = true }); FinishDragAnimation(); }
        };
        Closing += CompanionClosing;
        Closed += (_, _) => ShutdownCompanion();
        BuildCompanionTray();
        SystemEvents.PowerModeChanged += CompanionPowerChanged;
        SystemEvents.DisplaySettingsChanged += CompanionDisplayChanged;
        companionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };
        companionTimer.Tick += CompanionTick;
        nextSpeech = DateTime.UtcNow.AddSeconds(45);
        nextAction = DateTime.UtcNow.AddSeconds(12);
        companionTimer.Start();
        InitializeMcp();
    }

    private Task LoadCompanionAnimations()
    {
        try { StartVideoAnimations(); }
        catch (Exception e)
        {
            File.WriteAllText("companion-errors.log", e.ToString());
            ShowCompanionNotice("动画未能加载，请检查文件是否完整。");
        }
        return Task.CompletedTask;
    }

    private void InstallCompanionHook()
    {
        if (hookInstalled)
            return;
        var h = new WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero)
            return;
        hookInstalled = true;
        CompanionNative.SetStyle(h, -20, new IntPtr(CompanionNative.GetStyle(h, -20).ToInt64() | 0x08000000L | 0x80L));
        HwndSource.FromHwnd(h)?.AddHook(CompanionWndProc);
    }

    private IntPtr CompanionWndProc(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == 0x21)
        {
            handled = true;
            return new IntPtr(3);
        } // MA_NOACTIVATE, still deliver clicks.

        if (msg == 0x11) exitReady = true; // System session end need not wait for an animation.
        if (msg == 0x218)
            ApplyCompanionPowerPolicy();
        return IntPtr.Zero;
    }

    private void CompanionPowerChanged(object sender, PowerModeChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (companionClosed)
                return;
            if (e.Mode == PowerModes.Suspend)
                InvalidateSpeechRequests(true);
            else if (e.Mode == PowerModes.Resume)
                RecoverCompanionSession();
            ApplyCompanionPowerPolicy();
            nextSpeech = DateTime.UtcNow.Add(preferences.NextSpeechDelay(companionRandom));
        });
    }

    private void ApplyCompanionPowerPolicy()
    {
        if (brain == null || preferences == null) return;
        brain.StopOnBattery = preferences.StopModelOnBattery;
        if (!brain.CanRunOnCurrentPower)
        {
            InvalidateSpeechRequests(true);
            ClearCompanionModelReply();
        }
        nextWarmup = DateTime.MinValue;
    }

    private void CompanionDisplayChanged(object? s, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        ClampCompanion();
        anchor = new Point(Left, Top);
    });
    private void ApplyCompanionBubbleScale(bool save = true)
    {
        bubble.LayoutTransform = new ScaleTransform(preferences.BubbleScale, preferences.BubbleScale);
        CompanionEdgeLayout.Refresh(this);
        if (save) SaveCompanion();
    }

    private void ApplyCompanionScale(bool save = true)
    {
        Width = SpriteSize * 640 / 360 + 20;
        Height = SpriteSize + 35;
        if (videoImage != null) { videoImage.Width = SpriteSize * 640 / 360; videoImage.Height = SpriteSize; }
        PositionLoadingIndicator();

        if (save)
        {
            ClampCompanion();
            anchor = new Point(Left, Top);
            SaveCompanion();
        }
    }

    private void PositionLoadingIndicator()
    {
        if (loadingIndicator == null || preferences == null)
            return;
        // The idle sprite's visible head is around the upper middle of the
        // transparent sprite. Keep the indicator just above its right side.
        loadingIndicator.Margin = new Thickness(
            Math.Max(0, (Width - SpriteSize * 640 / 360) / 2 + SpriteSize * 412 / 360),
            Math.Max(0, Height - SpriteSize + SpriteSize * 50 / 360),
            0,
            0);
    }

    private void ClampCompanion() => CompanionEdgeLayout.Clamp(this);
    private void SaveCompanion()
    {
        if (preferences == null)
            return;
        preferences.Left = Left;
        preferences.Top = Top;
        try
        {
            preferences.Save();
        }
        catch
        {
        }
    }

    private void CompanionDown(object sender, MouseButtonEventArgs e)
    {
        if (exitAnimation || e.ChangedButton != MouseButton.Left || bubble.IsMouseOver)
            return;
        e.Handled = true;
        CompanionNative.GetCursorPos(out var clickPosition);
        var clickNow = DateTime.UtcNow;
        bool doubleClick = McpInputAvailable && (clickNow - lastClickAt).TotalMilliseconds <= Forms.SystemInformation.DoubleClickTime
            && Math.Abs(clickPosition.X - lastClickPoint.X) <= Forms.SystemInformation.DoubleClickSize.Width
            && Math.Abs(clickPosition.Y - lastClickPoint.Y) <= Forms.SystemInformation.DoubleClickSize.Height;
        if (doubleClick && TryOpenMcpInput()) {
            singleClickTimer?.Stop(); lastClickAt = DateTime.MinValue;
            PublishMcpInteraction("click", new { button = "left", count = 2, Left, Top });
            return;
        }
        dragging = true;
        dragged = false;
        CancelMovement();
        CompanionNative.GetCursorPos(out var p);
        dragStart = new Point(p.X, p.Y);
        windowStart = new Point(Left, Top);
        CaptureMouse();
    }

    private void CompanionMove(object sender, MouseEventArgs e)
    {
        if (!dragging || e.LeftButton != MouseButtonState.Pressed)
            return;
        CompanionNative.GetCursorPos(out var p);
        var dpi = VisualTreeHelper.GetDpi(this);
        var dx = (p.X - dragStart.X) / dpi.DpiScaleX;
        var dy = (p.Y - dragStart.Y) / dpi.DpiScaleY;
        if (Math.Abs(dx) + Math.Abs(dy) > 4 && !dragged)
        {
            dragged = true;
            PublishMcpInteraction("drag_start", new { Left, Top });
            if (!PuppetActive) PlayCompanion("drag");
        }

        if (dragged)
        {
            Left = windowStart.X + dx;
            Top = windowStart.Y + dy;
        }
        e.Handled = true;
    }

    private void CompanionUp(object sender, MouseButtonEventArgs e)
    {
        if (!dragging)
            return;
        e.Handled = true;
        dragging = false;
        ReleaseMouseCapture();
        ClampCompanion();
        anchor = new Point(Left, Top);
        SaveCompanion();
        if (dragged) { PublishMcpInteraction("drag_end", new { Left, Top, cancelled = false }); FinishDragAnimation(); }
        else
        {
            PublishMcpInteraction("click", new { button = "left", Left, Top });
            CompanionNative.GetCursorPos(out var point);
            lastClickAt = DateTime.UtcNow; lastClickPoint = new Point(point.X, point.Y);
            if (McpInputAvailable)
            {
                singleClickTimer?.Stop();
                singleClickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Forms.SystemInformation.DoubleClickTime) };
                singleClickTimer.Tick += (_, _) => { singleClickTimer.Stop(); if (!companionClosed && !hiddenByUser) ClickAnimation(); };
                singleClickTimer.Start();
            }
            else ClickAnimation();
        }
    }

    private void PlayCompanion(string name)
    {
        if (!companionReady || exitAnimation) return;
        if (name == "drag") { CancelMovement(); PlayAnimation(animationPolicy.Find("被鼠标拖拽悬空反馈"), "Drag"); return; }
        var id = name switch { "idle" => "待机呼吸休闲", "wave" => "点击回应-元气挥手", "angry" => "点击回应-傲娇生气", "sleep" => "原地小憩沉眠", _ => name };
        CancelMovement();
        var rule = animationPolicy.Rules.FirstOrDefault(r => r.Id == id);
        if (rule != null) PlayAnimation(rule, "Preview");
    }

    private void ResetBubbleDeadline()
    {
        var seconds = CompanionPreferences.BubbleDurations[preferences.BubbleDurationIndex];
        bubbleUntil = seconds == 0 ? DateTime.MaxValue : DateTime.UtcNow.AddSeconds(seconds);
    }

    private void ShowCompanionBubble(string text)
    {
        if (PuppetActive) return;
        externalBubble = false;
        bubbleIsModelReply = true;
        bubbleUntil = DateTime.MaxValue;
        speech.Show(text, true);
    }

    private void ShowCompanionPartial(string text, long? expectedVersion = null)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ShowCompanionPartial(text, expectedVersion));
            return;
        }
        if (companionClosed || exitAnimation || PuppetActive || text.Length == 0 || expectedVersion.HasValue && expectedVersion != speechRequestVersion)
            return;
        externalBubble = false;
        bubbleIsModelReply = true;
        bubbleUntil = DateTime.MaxValue;
        speech.ShowPartial(text);
    }

    private void FinishCompanionPartial(string text)
    {
        ShowCompanionPartial(text);
        ResetBubbleDeadline();
    }

    private void BeginModelSpeech()
    {
        if (loadingIndicator == null || loadingSpinner == null)
            return;
        ClearCompanionModelReply();
        loadingIndicator.Visibility = Visibility.Visible;
        loadingSpinner.Start();
        CompanionEdgeLayout.Refresh(this);
    }

    private void EndModelSpeech()
    {
        if (loadingIndicator == null || loadingSpinner == null)
            return;
        loadingSpinner.Stop();
        loadingIndicator.Visibility = Visibility.Collapsed;
        CompanionEdgeLayout.Refresh(this);
    }

    private void ShowCompanionNotice(string text, bool animate = false)
    {
        if (PuppetActive) return;
        externalBubble = false;
        bubbleIsModelReply = false;
        bubbleUntil = DateTime.MaxValue;
        speech.Show(text, animate);
    }

    private void ClearCompanionModelReply()
    {
        if (!bubbleIsModelReply) return;
        speech.Cancel();
        bubble.Visibility = Visibility.Collapsed;
    }

    private async void CompanionTick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var foreground = CompanionNative.GetForegroundWindow();
        if (CompanionNative.IsExternalForegroundCandidate(foreground))
            lastExternalForeground = foreground;
        var gap = (now - lastFrame).TotalSeconds;
        var dt = Math.Min(.15, Math.Max(0, gap));
        lastFrame = now;
        if (companionClosed || exitAnimation)
            return;
        if (gap > 30 || gap < -1) RecoverCompanionSession();
        if (preferences.PublicInfo) brain.RefreshWeather();
        if (App.Args.Contains("--test-mode") && now > diagnosticAt)
        {
            diagnosticAt = now.AddSeconds(1);
            var hwnd = new WindowInteropHelper(this).Handle;
            File.WriteAllText("test-status.json", System.Text.Json.JsonSerializer.Serialize(new { ready = companionReady, visible = IsVisible, hiddenByUser, fullscreenHidden, fullscreen = CompanionNative.IsFullscreen(), onAC = CompanionNative.OnAC, runner = brain.RunnerPid, busy = brain.Busy, status = brain.Status, hwnd = hwnd.ToInt64(), foreground = CompanionNative.GetForegroundWindow().ToInt64(), startupForeground = startupForeground.ToInt64(), style = CompanionNative.GetStyle(hwnd, -20).ToInt64(), Left, Top, Width, Height, bubble = bubbleText.Text, bubbleVisible = bubble.Visibility == Visibility.Visible, loadingVisible = loadingIndicator.Visibility == Visibility.Visible, bubbleDurationIndex = preferences.BubbleDurationIndex, bubbleRemainingSeconds = bubbleUntil == DateTime.MaxValue ? -1 : Math.Max(0, (bubbleUntil - now).TotalSeconds), action = currentAction, animationPhase, animationFrames = videoPlayer?.Frames, animationSeconds = videoPlayer?.PositionSeconds, animationInterval = preferences.AnimationIntervalSeconds, workRecognitionEnabled = false }));
            if (File.Exists("test-command.txt"))
            {
                var command = File.ReadAllText("test-command.txt").Trim();
                File.Delete("test-command.txt");
                switch (command)
                {
                    case "settings":
                        OpenCompanionSettings();
                        break;
                    case "weather":
                        _ = SpeakCompanionWeather();
                        break;
                    case "speech-test":
                        _ = SpeakCompanionTest();
                        break;
                    case "close-settings":
                        companionSettings?.Close();
                        break;
                    case "bubble":
                        ShowCompanionBubble("主人，小女仆今天也元气满满地来报到啦～");
                        break;
                    case "bubble-next":
                        ShowCompanionBubble("主人，下一句已经送到啦，小尾巴也跟着摇起来～");
                        break;
                    case "battery":
                        CompanionNative.TestPowerOverride = false;
                        ApplyCompanionPowerPolicy();
                        break;
                    case "ac":
                        CompanionNative.TestPowerOverride = true;
                        nextWarmup = DateTime.MinValue;
                        break;
                    case "real-power":
                        CompanionNative.TestPowerOverride = null;
                        break;
                    case "hide":
                        HideCompanion();
                        break;
                    case "restore":
                        RestoreCompanion();
                        break;
                    case "exit":
                        base.Close();
                        return;
                    case "generate":
                        nextSpeech = now;
                        break;
                    case "inspectable":
                        ShowInTaskbar = true;
                        CompanionNative.SetStyle(hwnd, -20, new IntPtr(CompanionNative.GetStyle(hwnd, -20).ToInt64() & ~0x80L));
                        break;
                    case "normal-style":
                        ShowInTaskbar = false;
                        CompanionNative.SetStyle(hwnd, -20, new IntPtr(CompanionNative.GetStyle(hwnd, -20).ToInt64() | 0x08000000L | 0x80L));
                        break;
                    case "model-off":
                        preferences.ModelEnabled = false;
                        InvalidateSpeechRequests(true);
                        break;
                    case "model-on":
                        preferences.ModelEnabled = true;
                        nextWarmup = DateTime.MinValue;
                        break;
                    case "render":
                        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Width, (int)Height, 96, 96, PixelFormats.Pbgra32);
                        bitmap.Render(companionLayout);
                        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                        using (var stream = File.Create("test-render.png"))
                            encoder.Save(stream);
                        break;
                    case "render-settings":
                        if (companionSettings != null)
                        {
                            var settingsBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)companionSettings.ActualWidth, (int)companionSettings.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                            settingsBitmap.Render((Visual)companionSettings.Content);
                            var settingsEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                            settingsEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(settingsBitmap));
                            using var settingsStream = File.Create("test-settings.png");
                            settingsEncoder.Save(settingsStream);
                        }
                        break;
                    default:
                        if (command.StartsWith("event:"))
                            StartAnimationEvent(animationPolicy.Find(command[6..]));
                        else if (command.StartsWith("action:"))
                            PlayCompanion(command[7..]);
                        break;
                }
            }
        }

        if (!brain.CanRunOnCurrentPower)
        {
            if (brain.Busy || brain.RunnerPid != null)
                InvalidateSpeechRequests(true);
            ClearCompanionModelReply();
        }
        else if (!PuppetActive && modelInstaller == null && preferences.ModelEnabled && brain.ModelAvailable && !speechRequestPending && !brain.Busy && brain.RunnerPid == null && now >= nextWarmup)
        {
            nextWarmup = now.AddMinutes(2);
            _ = brain.WarmUp();
        }

        UpdateCompanionVisibility();
        if (exitAnimation || hiddenByUser || fullscreenHidden || !companionReady || dragging)
            return;
        if (bubble.Visibility == Visibility.Visible && now > bubbleUntil)
            bubble.Visibility = Visibility.Collapsed;
        if (PuppetActive || now < nextSpeech || companionSettings != null || modelInstaller != null
            || !HasScheduledMcpSpeech && (speechRequestPending || brain.Busy || weatherRequestPending || !preferences.ModelEnabled || !brain.ModelAvailable || !brain.CanRunOnCurrentPower))
            return;
        if (CompanionNative.IdleSeconds < 8 || CompanionNative.IdleSeconds > 600)
        {
            nextSpeech = now.AddSeconds(30);
            return;
        }

        if (!ConsumeMcpSpeech()) await SpeechCountdownZeroAsync();
    }

    private async Task SpeechCountdownZeroAsync()
    {
        var foregroundHandle = speechForegroundOverride
            ?? CompanionNative.ResolveSpeechForeground(CompanionNative.GetForegroundWindow(), lastExternalForeground);
        speechForegroundOverride = null;
        if (PuppetActive || speechRequestPending || companionClosed || exitAnimation || modelInstaller != null || !preferences.ModelEnabled || !brain.ModelAvailable || !brain.CanRunOnCurrentPower)
            return;

        speechRequestPending = true;
        long version = speechRequestVersion;
        BeginModelSpeech();
        bool streamed = false;
        string text = "";
        try
        {
            // Both countdown and test use this gate. A warm-up or weather task
            // delays the request instead of silently discarding the test click.
            var deadline = DateTime.UtcNow.AddSeconds(100);
            while ((brain.Busy || weatherRequestPending) && DateTime.UtcNow < deadline
                && version == speechRequestVersion && !companionClosed && !exitAnimation && !hiddenByUser && !fullscreenHidden
                && preferences.ModelEnabled && brain.CanRunOnCurrentPower)
                await Task.Delay(100);
            if (version != speechRequestVersion || brain.Busy || weatherRequestPending || companionClosed || exitAnimation || hiddenByUser || fullscreenHidden
                || !preferences.ModelEnabled || !brain.CanRunOnCurrentPower)
                return;
            nextSpeech = DateTime.UtcNow.Add(preferences.NextSpeechDelay(companionRandom));
            BeginModelSpeech(); // A preceding weather request may have ended its own loading indicator.
            text = await brain.Generate(preferences.PublicInfo, preferences.ForegroundEnabled, foregroundHandle, partial =>
            {
                streamed = true;
                ShowCompanionPartial(partial, version);
            });
        }
        finally
        {
            if (version == speechRequestVersion) { speechRequestPending = false; EndModelSpeech(); }
        }
        if (version == speechRequestVersion && !companionClosed && !hiddenByUser && !fullscreenHidden && preferences.ModelEnabled && brain.CanRunOnCurrentPower && text.Length > 0)
        {
            if (streamed)
                FinishCompanionPartial(text);
            else
                ShowCompanionBubble(text);
        }
        else if (streamed && version == speechRequestVersion)
        {
            ClearCompanionModelReply();
        }
        // Empty/failed generation is silent for automatic speech and tests alike.
    }

    private void BuildCompanionTray()
    {
        var menu = new CompanionTrayMenu();
        menu.Opening += (_, _) => {
            lastExternalForeground = CompanionNative.ResolveSpeechForeground(CompanionNative.GetForegroundWindow(), lastExternalForeground);
            menu.Items[0].Text = IsVisible ? "隐藏桌宠" : "显示桌宠";
            foreach (int index in new[] { 2, 3 }) if (menu.Items.Count > index) menu.Items[index].Enabled = !PuppetActive;
        };
        menu.Items.Add("显示桌宠", null, (_, _) => Dispatcher.Invoke(() => { if (IsVisible) HideCompanion(); else RestoreCompanion(); }));
        menu.Items.Add("设置", null, (_, _) => Dispatcher.Invoke(OpenCompanionSettings));
        menu.Items.Add("天气", null, async (_, _) => await Dispatcher.InvokeAsync(SpeakCompanionWeather).Task.Unwrap());
        menu.Items.Add("吐字测试", null, async (_, _) => await Dispatcher.InvokeAsync(SpeakCompanionTest).Task.Unwrap());
        menu.Items.Add("关闭桌宠", null, (_, _) => Dispatcher.Invoke(() => base.Close()));
        string[] icons = ["\uE7F4", "\uE713", "\uE706", "\uE8F2", "\uE8BB"];
        for (int index = 0; index < menu.Items.Count; index++) menu.Items[index].Tag = icons[index];
        using var sourceIcon = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "BigBlueFish.ico"));
        notifyIcon = new CompanionTrayIcon
        {
            Icon = new System.Drawing.Icon(sourceIcon, new System.Drawing.Size(32, 32)),
            Text = "蓝色大肥鱼",
            ContextMenuStrip = menu
        };
        notifyIcon.DoubleClick += (_, _) => Dispatcher.Invoke(RestoreCompanion);
        notifyIcon.BalloonTipClicked += (_, _) => Dispatcher.Invoke(OpenUpdateWindow);
        notifyIcon.Visible = true;
    }

    private void RestoreCompanion()
    {
        if (companionClosed || exitAnimation || exitReady) return;
        hiddenByUser = false;
        UpdateCompanionVisibility();
        ClampCompanion();
        nextSpeech = DateTime.UtcNow.Add(preferences.NextSpeechDelay(companionRandom));
    }

    private void HideCompanion()
    {
        if (companionClosed || exitAnimation || exitReady) return;
        hiddenByUser = true;
        singleClickTimer?.Stop();
        companionMenuDismissal?.Reset();
        speech.Cancel();
        InvalidateSpeechRequests(!brain.CanRunOnCurrentPower);
        bubble.Visibility = Visibility.Collapsed;
        Hide();
        companionSettings?.Close();
    }

    private const string StartupName = "BlueWhaleCompanion";
    private static bool StartupEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return k?.GetValue(StartupName) != null;
    }

    private void SetCompanionStartup(bool enabled)
    {
        using var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled)
            k.SetValue(StartupName, "\"" + Environment.ProcessPath + "\"");
        else
            k.DeleteValue(StartupName, false);
    }

    private void ShutdownCompanion()
    {
        if (companionClosed)
            return;
        companionClosed = true;
        modelInstaller?.RequestClose();
        updateWindow?.RequestClose();
        StopMcp();
        companionContextMenu.IsOpen = false;
        companionMenuDismissal?.Dispose();
        companionTimer?.Stop();
        singleClickTimer?.Stop();
        videoPlayer?.Dispose();
        loadingSpinner?.Stop();
        speech?.Dispose();
        brain?.Dispose();
        SaveCompanion();
        companionSettings?.Close();
        SystemEvents.PowerModeChanged -= CompanionPowerChanged;
        SystemEvents.DisplaySettingsChanged -= CompanionDisplayChanged;
        if (notifyIcon != null) { notifyIcon.Visible = false; notifyIcon.Dispose(); }
        instanceLock?.Dispose();
        singleInstance?.Dispose();
        System.Windows.Application.Current.Shutdown();
    }
}
