using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using BigBlueFish.Windows;

internal sealed class BubbleVerificationApp : Application
{
    internal static Exception? Failure;

    static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        Console.WriteLine("PASS: " + label);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        const string reply = "唔，给主人摸摸～鱼鱼乖乖低下头，小鲸鱼尾巴也跟着晃了晃。再摸一下嘛，嘿嘿💙";
        var text = new TextBlock {
            Text = reply,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 270,
            FontSize = 17,
            FontFamily = new FontFamily("avares://MacVerification/Assets#LemiXiaoNaiPaoTi")
        };
        text.Measure(new Size(270, double.PositiveInfinity));
        double measuredTextHeight = text.DesiredSize.Height;
        var frame = new LinuxBubble { Padding = new Thickness(16, 16, 16, 30), Child = text };
        var window = new Window {
            WindowDecorations = WindowDecorations.None,
            CanResize = false,
            ShowActivated = false,
            ShowInTaskbar = false,
            Opacity = 0,
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
                Check(measuredTextHeight > 20, "manual pre-measure detects multiple Chinese text lines");
                Check(window.Height >= measuredTextHeight + 46 - .5,
                    "window height retains the complete pre-show text measurement");
                Check(text.Bounds.Height >= measuredTextHeight - .5, "auto-sized native window lays out all reply lines");
                Check(text.Text == reply, "native bubble window retains the complete reply text");
            } catch (Exception error) { Failure = error; }
            finally { window.Close(); desktop.Shutdown(); }
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
    internal static void Run()
    {
        AppBuilder.Configure<BubbleVerificationApp>().UsePlatformDetect().StartWithClassicDesktopLifetime([]);
        if (BubbleVerificationApp.Failure is not null) throw BubbleVerificationApp.Failure;
    }
}
