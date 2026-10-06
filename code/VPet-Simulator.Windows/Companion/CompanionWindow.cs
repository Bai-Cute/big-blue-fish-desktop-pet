using LinePutScript;
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
using VPet_Simulator.Core;
using static VPet_Simulator.Core.GraphInfo;
using Forms = System.Windows.Forms;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace VPet_Simulator.Windows;
public partial class MainWindow
{
    private CompanionPreferences preferences = null !;
    private CompanionBrain brain = null !;
    private DispatcherTimer companionTimer = null !;
    private Border bubble = null !;
    private TextBlock bubbleText = null !;
    private Border loadingIndicator = null !;
    private Ellipse loadingSpinner = null !;
    private DispatcherTimer loadingTimer = null !;
    private double loadingAngle;
    private CompanionSpeech speech = null!;
    private bool bubbleIsModelReply;
    private long speechRequestVersion;
    private Grid companionLayout = null !;
    private Window? companionSettings;
    private ContextMenu companionContextMenu = null!;
    private bool weatherRequestPending;
    private DateTime nextSpeech, nextAction, bubbleUntil;
    private bool companionReady, companionClosed, dragged, dragging, hiddenByUser, fullscreenHidden;
    private Point dragStart, windowStart, anchor;
    private Point? destination;
    private readonly Random companionRandom = new();
    private DateTime lastFrame = DateTime.UtcNow;
    private DateTime nextWarmup = DateTime.MinValue;
    private bool hookInstalled;
    private DateTime diagnosticAt;
    private readonly IntPtr startupForeground = CompanionNative.GetForegroundWindow();
    private IntPtr lastExternalForeground;
    private IntPtr? speechForegroundOverride;
    private string currentAction = "idle";
    private DateTime actionUntil;
    private System.Threading.Mutex? singleInstance;
    private FileStream? instanceLock;
    private double SpriteSize => 280 * preferences.Scale;
    internal double CompanionSpriteSize => SpriteSize;
    internal Border CompanionBubble => bubble;
    internal TextBlock CompanionBubbleText => bubbleText;
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
        Set = new Setting(this, "gameconfig:|nofunction#true:|allowmove#true:|autochangewindow#true:|\nzoomlevel#0.56:|");
        InitializeComponent();
        System.Windows.Shell.WindowChrome.SetWindowChrome(this, null);
        AllowsTransparency = true;
        Background = null;
        WindowStyle = WindowStyle.None;
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        Title = "蓝色大肥鱼 · VPet";
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Manual;
        companionLayout = new Grid
        {
            Background = null
        };
        bubbleText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 15,
            Foreground = new SolidColorBrush(Color.FromRgb(49, 64, 94)),
            FontFamily = new FontFamily("Microsoft YaHei UI"),
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
                Foreground = new SolidColorBrush(Color.FromRgb(91, 116, 151)),
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
        bubble = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(244, 249, 255)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(160, 188, 225)),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(15, 10, 10, 10),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 300,
            Visibility = Visibility.Collapsed,
            Child = bubbleContent
        };
        companionLayout.Children.Add(bubble);
        loadingSpinner = new Ellipse
        {
            Width = 18,
            Height = 18,
            Stroke = new SolidColorBrush(Color.FromRgb(91, 116, 151)),
            StrokeThickness = 2.5,
            StrokeDashArray = new DoubleCollection { 2.2, 3.8 },
            RenderTransformOrigin = new Point(.5, .5),
            RenderTransform = new RotateTransform()
        };
        loadingIndicator = new Border
        {
            Width = 28,
            Height = 28,
            Padding = new Thickness(5),
            Background = new SolidColorBrush(Color.FromArgb(235, 244, 249, 255)),
            CornerRadius = new CornerRadius(14),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            Child = loadingSpinner
        };
        Panel.SetZIndex(loadingIndicator, 20);
        companionLayout.Children.Add(loadingIndicator);
        loadingTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(60)
        };
        loadingTimer.Tick += (_, _) =>
        {
            loadingAngle = (loadingAngle + 28) % 360;
            ((RotateTransform)loadingSpinner.RenderTransform).Angle = loadingAngle;
        };
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
        };
        BuildCompanionContextMenu();
        PreviewMouseRightButtonDown += (_, e) =>
        {
            e.Handled = true;
            lastExternalForeground = CompanionNative.ResolveSpeechForeground(CompanionNative.GetForegroundWindow(), lastExternalForeground);
            companionContextMenu.IsOpen = true;
        };
        PreviewMouseLeftButtonDown += CompanionDown;
        PreviewMouseMove += CompanionMove;
        PreviewMouseLeftButtonUp += CompanionUp;
        LostMouseCapture += (_, _) =>
        {
            dragging = false;
        };
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
    }

    private async Task LoadCompanionAnimations()
    {
        try
        {
            Core.Save = GameSavesData.GameSave;
            Core.Controller = new MWController(this);
            Core.Graph = new GraphCore(220, Dispatcher, new GraphCore.Config(new LpsDocument("touchhead:|px#100:|py#0:|sw#300:|sh#240:|\ntouchbody:|px#140:|py#200:|sw#240:|sh#260:|")));
            PNGAnimation.BlueWhaleTint = true;
            foreach (var folder in Directory.GetDirectories("assets/pet"))
            {
                var name = Path.GetFileName(folder).Replace("_0", "");
                var frames = new DirectoryInfo(folder).GetFiles("*.png").OrderBy(x => int.Parse(x.Name.Split('_')[0])).ToArray();
                var type = name == "idle" ? GraphType.Default : GraphType.Common;
                Core.Graph.AddGraph(new PNGAnimation(Core.Graph, Path.GetFullPath(folder), frames, new GraphInfo(name, type, AnimatType.Single, IGameSave.ModeType.Happy), false));
            }

            Main = new Main(Core)
            {
                Width = SpriteSize,
                Height = SpriteSize,
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Main.IsHitTestVisible = true;
            companionLayout.Children.Insert(0, Main);
            await Main.Load_2_WaitGraph();
            if (Main.ErrorMessage.Count > 0)
                throw new IOException("动画加载失败：" + Main.ErrorMessage[0]);
            Main.Load_4_Start();
            companionReady = true;
            File.WriteAllText("ready.status", "ready");
        }
        catch (Exception e)
        {
            File.WriteAllText("companion-errors.log", e.ToString());
            bubbleText.Text = "动画未能加载，请检查文件是否完整。";
            bubble.Visibility = Visibility.Visible;
        }
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
                brain.Stop();
            ApplyCompanionPowerPolicy();
            nextSpeech = DateTime.UtcNow.AddMinutes(preferences.SpeechMinMinutes);
        });
    }

    private void ApplyCompanionPowerPolicy()
    {
        if (brain == null || preferences == null) return;
        brain.StopOnBattery = preferences.StopModelOnBattery;
        if (!brain.CanRunOnCurrentPower)
        {
            brain.Stop();
            ClearCompanionModelReply();
        }
        nextWarmup = DateTime.MinValue;
    }

    private void CompanionDisplayChanged(object? s, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        ClampCompanion();
        anchor = new Point(Left, Top);
    });
    private void ApplyCompanionScale(bool save = true)
    {
        Width = Math.Max(320, SpriteSize + 20);
        Height = SpriteSize + 115;
        Set.ZoomLevel = SpriteSize / 500;
        if (Main != null)
        {
            Main.Width = SpriteSize;
            Main.Height = SpriteSize;
        }
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
            Math.Max(0, (Width - SpriteSize) / 2 + SpriteSize * .68),
            Math.Max(0, Height - SpriteSize + SpriteSize * .03),
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
        if (e.ChangedButton != MouseButton.Left || bubble.IsMouseOver)
            return;
        e.Handled = true;
        dragging = true;
        dragged = false;
        destination = null;
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
            PlayCompanion("drag");
        }

        Left = windowStart.X + dx;
        Top = windowStart.Y + dy;
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
        PlayCompanion(dragged ? "happy" : "blush");
        nextAction = DateTime.UtcNow.AddSeconds(20);
    }

    private void PlayCompanion(string name)
    {
        if (!companionReady)
            return;
        currentAction = name;
        actionUntil = DateTime.UtcNow.AddSeconds(name == "sleep" ? 18 : 8);
        Main.Display(name, AnimatType.Single, () => Main.DisplayNomal());
    }

    private void ResetBubbleDeadline()
    {
        var seconds = CompanionPreferences.BubbleDurations[preferences.BubbleDurationIndex];
        bubbleUntil = seconds == 0 ? DateTime.MaxValue : DateTime.UtcNow.AddSeconds(seconds);
    }

    private void ShowCompanionBubble(string text)
    {
        bubbleIsModelReply = true;
        bubbleUntil = DateTime.MaxValue;
        speech.Show(text, true);
    }

    private void ShowCompanionPartial(string text)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ShowCompanionPartial(text));
            return;
        }
        if (companionClosed || text.Length == 0)
            return;
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
        if (loadingIndicator == null || loadingTimer == null)
            return;
        ClearCompanionModelReply();
        loadingIndicator.Visibility = Visibility.Visible;
        loadingTimer.Start();
        CompanionEdgeLayout.Refresh(this);
    }

    private void EndModelSpeech()
    {
        if (loadingIndicator == null || loadingTimer == null)
            return;
        loadingTimer.Stop();
        loadingIndicator.Visibility = Visibility.Collapsed;
        CompanionEdgeLayout.Refresh(this);
    }

    private void ShowCompanionNotice(string text, bool animate = false)
    {
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
        var dt = Math.Min(.15, (now - lastFrame).TotalSeconds);
        lastFrame = now;
        if (companionClosed)
            return;
        if (App.Args.Contains("--test-mode") && now > diagnosticAt)
        {
            diagnosticAt = now.AddSeconds(1);
            var hwnd = new WindowInteropHelper(this).Handle;
            File.WriteAllText("test-status.json", System.Text.Json.JsonSerializer.Serialize(new { ready = companionReady, visible = IsVisible, hiddenByUser, fullscreenHidden, fullscreen = CompanionNative.IsFullscreen(), onAC = CompanionNative.OnAC, runner = brain.RunnerPid, busy = brain.Busy, status = brain.Status, hwnd = hwnd.ToInt64(), foreground = CompanionNative.GetForegroundWindow().ToInt64(), startupForeground = startupForeground.ToInt64(), style = CompanionNative.GetStyle(hwnd, -20).ToInt64(), Left, Top, Width, Height, bubble = bubbleText.Text, bubbleVisible = bubble.Visibility == Visibility.Visible, loadingVisible = loadingIndicator.Visibility == Visibility.Visible, bubbleDurationIndex = preferences.BubbleDurationIndex, bubbleRemainingSeconds = bubbleUntil == DateTime.MaxValue ? -1 : Math.Max(0, (bubbleUntil - now).TotalSeconds), action = currentAction }));
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
                        brain.Stop();
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
                    default:
                        if (command.StartsWith("action:"))
                            PlayCompanion(command[7..]);
                        break;
                }
            }
        }

        if (!brain.CanRunOnCurrentPower)
        {
            if (brain.Busy || brain.RunnerPid != null)
                brain.Stop();
            ClearCompanionModelReply();
        }
        else if (preferences.ModelEnabled && !brain.Busy && brain.RunnerPid == null && now >= nextWarmup)
        {
            nextWarmup = now.AddMinutes(2);
            _ = brain.WarmUp();
        }

        bool full = CompanionNative.IsFullscreen();
        if (full && !fullscreenHidden && !hiddenByUser)
        {
            fullscreenHidden = true;
            Hide();
            brain.CancelGeneration();
        }
        else if (!full && fullscreenHidden)
        {
            fullscreenHidden = false;
            if (!hiddenByUser)
            {
                Show();
                ClampCompanion();
            }
        }

        if (hiddenByUser || fullscreenHidden || !companionReady || dragging)
            return;
        if (bubble.Visibility == Visibility.Visible && now > bubbleUntil)
            bubble.Visibility = Visibility.Collapsed;
        if (destination is Point target)
        {
            var delta = target.X - Left;
            var step = Math.Sign(delta) * Math.Min(Math.Abs(delta), dt * 16);
            Core.Controller!.MoveWindows(step / Set.ZoomLevel, 0);
            if (Math.Abs(delta) < 1)
            {
                destination = null;
                Main.PetGrid.RenderTransform = Transform.Identity;
            }
        }

        if (now > nextAction && companionSettings == null)
        {
            nextAction = now.AddSeconds(companionRandom.Next(18, 38));
            var names = CompanionNative.OnAC ? new[]
            {
                "wave",
                "stretch",
                "happy",
                "sit",
                "sit_stretch",
                "eat",
                "music",
                "swim",
                "idle"
            }

            : new[]
            {
                "idle",
                "sit",
                "sleep",
                "stretch"
            };
            var action = names[companionRandom.Next(names.Length)];
            PlayCompanion(action);
            if (action == "swim" || (!CompanionNative.OnAC && companionRandom.Next(5) == 0))
            {
                var original = Left;
                Left = anchor.X + companionRandom.Next(-45, 46);
                ClampCompanion();
                destination = new Point(Left, Top);
                Left = original;
            }
        }

        if (now < nextSpeech || brain.Busy || weatherRequestPending || !preferences.ModelEnabled || !brain.CanRunOnCurrentPower || companionSettings != null)
            return;
        if (CompanionNative.IdleSeconds < 8 || CompanionNative.IdleSeconds > 600)
        {
            nextSpeech = now.AddSeconds(30);
            return;
        }

        await SpeechCountdownZeroAsync();
    }

    private async Task SpeechCountdownZeroAsync()
    {
        var foregroundHandle = speechForegroundOverride
            ?? CompanionNative.ResolveSpeechForeground(CompanionNative.GetForegroundWindow(), lastExternalForeground);
        speechForegroundOverride = null;
        if (brain.Busy || weatherRequestPending || !preferences.ModelEnabled || !brain.CanRunOnCurrentPower || companionSettings != null)
            return;

        nextSpeech = DateTime.UtcNow.AddMinutes(preferences.SpeechMinMinutes + companionRandom.Next(0, 8));
        long version = speechRequestVersion;
        BeginModelSpeech();
        bool streamed = false;
        string text;
        try
        {
            text = await brain.Generate(preferences.PublicInfo, preferences.ForegroundEnabled, foregroundHandle, partial =>
            {
                streamed = true;
                ShowCompanionPartial(partial);
            });
        }
        finally
        {
            EndModelSpeech();
        }
        if (version == speechRequestVersion && !companionClosed && !hiddenByUser && !fullscreenHidden && preferences.ModelEnabled && brain.CanRunOnCurrentPower && text.Length > 0)
        {
            if (streamed)
                FinishCompanionPartial(text);
            else
                ShowCompanionBubble(text);
            PlayCompanion("wave");
        }
        else if (streamed)
        {
            ClearCompanionModelReply();
        }
#if COMPANION_OCR
        else if (!companionClosed && !hiddenByUser && !fullscreenHidden && preferences.ModelEnabled && brain.CanRunOnCurrentPower)
        {
            ShowCompanionNotice("主人，" + brain.Status + "。");
        }
#endif
    }

    private void BuildCompanionTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Opening += (_, _) => lastExternalForeground = CompanionNative.ResolveSpeechForeground(CompanionNative.GetForegroundWindow(), lastExternalForeground);
        menu.Items.Add("显示桌宠", null, (_, _) => Dispatcher.Invoke(RestoreCompanion));
        menu.Items.Add("设置", null, (_, _) => Dispatcher.Invoke(OpenCompanionSettings));
        menu.Items.Add("天气", null, async (_, _) => await Dispatcher.InvokeAsync(SpeakCompanionWeather).Task.Unwrap());
        menu.Items.Add("吐字测试", null, async (_, _) => await Dispatcher.InvokeAsync(SpeakCompanionTest).Task.Unwrap());
        menu.Items.Add("关闭桌宠", null, (_, _) => Dispatcher.Invoke(() => base.Close()));
        notifyIcon = new Forms.NotifyIcon
        {
            Icon = new System.Drawing.Icon("vpeticon.ico"),
            Text = "蓝色大肥鱼",
            Visible = true,
            ContextMenuStrip = menu
        };
        notifyIcon.DoubleClick += (_, _) => Dispatcher.Invoke(RestoreCompanion);
    }

    private void RestoreCompanion()
    {
        hiddenByUser = false;
        Show();
        ClampCompanion();
        nextSpeech = DateTime.UtcNow.AddMinutes(preferences.SpeechMinMinutes);
    }

    private void HideCompanion()
    {
        hiddenByUser = true;
        speech.Cancel();
        if (brain.CanRunOnCurrentPower)
            brain.CancelGeneration();
        else
            brain.Stop();
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

    private void OpenCompanionSettings()
    {
        if (companionSettings != null)
        {
            companionSettings.Activate();
            return;
        }

        destination = null;
        var w = new Window
        {
            Title = "大肥鱼设置",
            Width = 460,
            SizeToContent = SizeToContent.Height,
            MaxHeight = Math.Max(400, SystemParameters.WorkArea.Height - 60),
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = new SolidColorBrush(Color.FromRgb(245, 249, 255)),
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 14,
            ShowInTaskbar = true
        };
        var p = new StackPanel
        {
            Margin = new Thickness(22)
        };
        w.Content = new ScrollViewer { Content = p, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        p.Children.Add(new TextBlock { Text = "蓝色大肥鱼", FontSize = 23, FontWeight = FontWeights.Bold, Foreground = Brushes.SteelBlue, Margin = new Thickness(0, 0, 0, 12) });
        void Toggle(string label, bool value, Action<bool> change)
        {
            var c = new CheckBox
            {
                Content = label,
                IsChecked = value,
                Margin = new Thickness(0, 7, 0, 7)
            };
            c.Click += (_, _) =>
            {
                change(c.IsChecked == true);
                SaveCompanion();
            };
            p.Children.Add(c);
        }

        Toggle("启用本地大模型", preferences.ModelEnabled, v =>
        {
            preferences.ModelEnabled = v;
            if (!v)
                brain.Stop();
            else
            {
                nextWarmup = DateTime.MinValue;
                nextSpeech = DateTime.UtcNow.AddSeconds(20);
            }
        });
        Toggle("拔电时暂停并卸载模型", preferences.StopModelOnBattery, v =>
        {
            preferences.StopModelOnBattery = v;
            ApplyCompanionPowerPolicy();
        });
        Toggle("使用显卡加速（关闭则使用 CPU）", preferences.GpuEnabled, v =>
        {
            preferences.GpuEnabled = v;
            brain.Stop();
            brain.UseGpu = v;
            nextWarmup = DateTime.MinValue;
        });
        Toggle("开机自动启动", StartupEnabled(), SetCompanionStartup);
        Toggle("自动获取天气和科技新闻", preferences.PublicInfo, v => preferences.PublicInfo = v);
        Toggle("感知当前应用（仅在本机处理）", preferences.ForegroundEnabled, v => preferences.ForegroundEnabled = v);
        AddCompanionRegionSelectors(p);
        var label = new TextBlock
        {
            Text = $"缩放：{preferences.Scale:P0}",
            Margin = new Thickness(0, 10, 0, 4)
        };
        p.Children.Add(label);
        var scale = new Slider
        {
            Minimum = .65,
            Maximum = 1.6,
            Value = preferences.Scale,
            TickFrequency = .05,
            IsSnapToTickEnabled = true
        };
        scale.ValueChanged += (_, _) =>
        {
            preferences.Scale = scale.Value;
            label.Text = $"缩放：{scale.Value:P0}";
            ApplyCompanionScale();
        };
        p.Children.Add(scale);
        var freq = new ComboBox
        {
            Margin = new Thickness(0, 8, 0, 8)
        };
        foreach (var n in new[]
        {
            3,
            8,
            15,
            30
        }

        )
            freq.Items.Add($"说话间隔：{n}–{n + 7} 分钟");
        freq.SelectedIndex = preferences.SpeechMinMinutes switch
        {
            3 => 0,
            15 => 2,
            30 => 3,
            _ => 1
        };
        freq.SelectionChanged += (_, _) =>
        {
            preferences.SpeechMinMinutes = new[]
            {
                3,
                8,
                15,
                30
            }[freq.SelectedIndex];
            nextSpeech = DateTime.UtcNow.AddMinutes(preferences.SpeechMinMinutes);
            SaveCompanion();
        };
        p.Children.Add(freq);
        var dwellLabel = new TextBlock
        {
            Text = "气泡驻留时间：" + CompanionPreferences.BubbleDurationLabels[preferences.BubbleDurationIndex],
            Margin = new Thickness(0, 8, 0, 4)
        };
        p.Children.Add(dwellLabel);
        var dwell = new Slider
        {
            Minimum = 0,
            Maximum = 7,
            Value = preferences.BubbleDurationIndex,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            SmallChange = 1,
            LargeChange = 1,
            TickPlacement = System.Windows.Controls.Primitives.TickPlacement.BottomRight,
            ToolTip = dwellLabel.Text
        };
        System.Windows.Automation.AutomationProperties.SetName(dwell, "气泡驻留时间");
        dwell.ValueChanged += (_, _) =>
        {
            preferences.BubbleDurationIndex = (int)Math.Round(dwell.Value);
            dwellLabel.Text = "气泡驻留时间：" + CompanionPreferences.BubbleDurationLabels[preferences.BubbleDurationIndex];
            dwell.ToolTip = dwellLabel.Text;
            if (bubble.Visibility == Visibility.Visible)
                ResetBubbleDeadline();
            SaveCompanion();
        };
        p.Children.Add(dwell);
        var status = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.SlateGray,
            Margin = new Thickness(0, 8, 0, 8)
        };
        p.Children.Add(status);
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        void UpdateStatus() => status.Text = (CompanionNative.OnAC ? "已插电" :
            preferences.StopModelOnBattery ? "电池供电 · 已按设置暂停模型" : "电池供电 · 允许模型运行") + "\n" + brain.Status;
        timer.Tick += (_, _) => UpdateStatus();
        UpdateStatus();
        timer.Start();
        void Button(string text, Action action)
        {
            var b = new Button
            {
                Content = text,
                Padding = new Thickness(8),
                Margin = new Thickness(0, 4, 0, 4)
            };
            b.Click += (_, _) => action();
            p.Children.Add(b);
        }

        Button("隐藏到任务栏通知区", HideCompanion);
        Button("回到右下角", () =>
        {
            var a = SystemParameters.WorkArea;
            Left = a.Right - Width - 8;
            Top = a.Bottom - Height + 12;
            ClampCompanion();
            anchor = new Point(Left, Top);
            SaveCompanion();
        });
        Button("关闭桌宠", () =>
        {
            w.Close();
            base.Close();
        });
        p.Children.Add(new TextBlock { Text = "右键可打开设置或播报天气；无聊天记录。\n基于 VPet · 素材采用社区鲸鱼娘动画", FontSize = 11, Foreground = Brushes.SlateGray, Margin = new Thickness(0, 10, 0, 0) });
        companionSettings = w;
        w.Closed += (_, _) =>
        {
            timer.Stop();
            companionSettings = null;
            SaveCompanion();
        };
        w.Show();
    }

    private void ShutdownCompanion()
    {
        if (companionClosed)
            return;
        companionClosed = true;
        companionContextMenu.IsOpen = false;
        companionTimer?.Stop();
        speech?.Dispose();
        brain?.Dispose();
        SaveCompanion();
        companionSettings?.Close();
        SystemEvents.PowerModeChanged -= CompanionPowerChanged;
        SystemEvents.DisplaySettingsChanged -= CompanionDisplayChanged;
        notifyIcon?.Dispose();
        Main?.Dispose();
        Core.Graph?.Dispose();
        instanceLock?.Dispose();
        singleInstance?.Dispose();
        System.Windows.Application.Current.Shutdown();
    }
}
