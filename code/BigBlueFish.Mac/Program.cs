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
        string? testDataDirectory = null;
        string? testLockPath = null;
        if (args.Contains("--test-mode")) {
            var dataPath = TestArgument(args, "--test-data-directory");
            if (dataPath != null) testDataDirectory = Path.GetFullPath(dataPath);
            var lockPath = TestArgument(args, "--test-instance-lock");
            if (lockPath != null) testLockPath = Path.GetFullPath(lockPath);
        }
        using var instance = MacInstanceLock.TryAcquire(testLockPath);
        if (instance == null) return;
        var directory = testDataDirectory ?? Environment.GetEnvironmentVariable("BIGBLUEFISH_DATA") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Application Support/BigBlueFish");
        Directory.CreateDirectory(directory); Environment.CurrentDirectory = directory;
        App.Args = args;
        AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }

    static string? TestArgument(string[] args, string name)
    {
        int option = Array.IndexOf(args, name);
        if (option < 0) return null;
        if (option + 1 >= args.Length || string.IsNullOrWhiteSpace(args[option + 1]))
            throw new ArgumentException($"{name} requires a path.");
        return args[option + 1];
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
