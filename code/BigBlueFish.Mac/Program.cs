using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace BigBlueFish.Windows;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("此入口仅支持 macOS。");
        using var instance = MacInstanceLock.TryAcquire();
        if (instance == null) return;
        var directory = Environment.GetEnvironmentVariable("BIGBLUEFISH_DATA") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Application Support/BigBlueFish");
        Directory.CreateDirectory(directory); Environment.CurrentDirectory = directory;
        App.Args = args;
        AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
}

public sealed class App : Application
{
    internal static string[] Args = [];
    public override void Initialize() { Styles.Add(new FluentTheme()); RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light; }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
            desktop.MainWindow = new MacPetWindow();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
