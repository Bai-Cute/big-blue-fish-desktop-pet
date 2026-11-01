using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using BigBlueFish.Windows;

internal sealed class BubbleVerificationApp : Application
{
    internal static Exception? Failure;
    internal static bool ShowWindow;

    static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        Console.WriteLine("PASS: " + label);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        const string initialReply = "嗨，主人！";
        const string reply = "晚上好呀，主人～今天过得怎么样？忙完了就歇一歇，鱼鱼陪你聊会儿。";
        var text = new TextBlock {
            Text = initialReply,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 270,
            FontSize = 17,
            FontFamily = new FontFamily("avares://MacVerification/Assets#LemiXiaoNaiPaoTi")
        };
        double initialTextHeight = MacBubbleLayout.Measure(text, initialReply, 270).Height;
        var frame = new LinuxBubble { Padding = new Thickness(16, 16, 16, 30), Child = text };
        var window = new Window {
            Title = "Bubble layout verification",
            WindowDecorations = WindowDecorations.None,
            Topmost = true,
            CanResize = false,
            ShowActivated = false,
            ShowInTaskbar = false,
            Opacity = ShowWindow ? 1 : 0,
            Background = Brushes.Transparent,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            Content = frame
        };
        var desktop = (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)!;
        desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        desktop.MainWindow = window;
        window.Opened += (_, _) => {
            Dispatcher.UIThread.Post(() => {
            try {
                Check(text.Bounds.Height >= initialTextHeight - .5, "native bubble lays out the initial short reply");
                var responseSize = MacBubbleLayout.Measure(text, reply, 270);
                var display = MacBubbleReplyPolicy.ForBubble(reply, MacChatSettings.DefaultBubbleCharacterLimit,
                    responseSize.Height + 46, 800);
                Check(display == reply, "screenshot reply remains eligible for complete bubble display");
                var size = MacBubbleLayout.Measure(text, display, 270);
                double measuredTextHeight = size.Height;
                window.Width = Math.Ceiling(size.Width + 32);
                window.Height = Math.Ceiling(measuredTextHeight + 46);
                window.UpdateLayout();
                Check(measuredTextHeight > initialTextHeight, "remeasuring a reused text block detects the longer reply lines");
                Check(window.Height >= measuredTextHeight + 46 - .5,
                    "resized native window retains the complete updated text measurement");
                Check(text.Bounds.Height >= measuredTextHeight - .5, "reused native bubble lays out all updated reply lines");
                Check(text.Text == reply, "native bubble window retains the complete reply text");
            } catch (Exception error) { Failure = error; }
            finally {
                if (ShowWindow) {
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
                    timer.Tick += (_, _) => { timer.Stop(); window.Close(); desktop.Shutdown(); };
                    timer.Start();
                } else { window.Close(); desktop.Shutdown(); }
            }
            }, DispatcherPriority.Background);
        };
        window.Show();
        window.Width = Math.Ceiling(text.DesiredSize.Width + 32);
        window.Height = Math.Ceiling(text.DesiredSize.Height + 46);
        base.OnFrameworkInitializationCompleted();
    }
}

internal static class MacBubbleLayoutVerification
{
    internal static void Run(bool showWindow = false)
    {
        BubbleVerificationApp.ShowWindow = showWindow;
        AppBuilder.Configure<BubbleVerificationApp>().UsePlatformDetect().StartWithClassicDesktopLifetime([]);
        if (BubbleVerificationApp.Failure is not null) throw BubbleVerificationApp.Failure;
    }
}
