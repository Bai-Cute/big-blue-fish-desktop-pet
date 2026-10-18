using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace BigBlueFish.Windows;

internal static class Program
{
    [STAThread] public static void Main(string[] args)
    {
        App.Args = args;
        if (args.Length == 2 && args[0] == "--wait-for-exit" && int.TryParse(args[1], out int pid)) {
            try { using var old = System.Diagnostics.Process.GetProcessById(pid); if (!old.WaitForExit(30000)) return; } catch (ArgumentException) { }
        }
        var directory = Environment.GetEnvironmentVariable("BIGBLUEFISH_DATA") ?? Path.Combine(
            Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share"), "bigbluefish");
        Directory.CreateDirectory(directory); Environment.CurrentDirectory = directory;
        Build().StartWithClassicDesktopLifetime(args);
    }
    public static AppBuilder Build() => AppBuilder.Configure<App>().UsePlatformDetect()
        .With(new X11PlatformOptions { RenderingMode = [X11RenderingMode.Software] });
}
public sealed class App : Application
{
    public static string[] Args = [];
    public override void Initialize() {
        var theme = new FluentTheme(); theme.Palettes[Avalonia.Styling.ThemeVariant.Light] = new ColorPaletteResources { Accent = Avalonia.Media.Color.FromRgb(80,96,152) };
        Styles.Add(theme); RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        { desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown; desktop.MainWindow = new LinuxPetWindow(); }
        base.OnFrameworkInitializationCompleted();
    }
}
