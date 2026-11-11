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
        var directory = Environment.GetEnvironmentVariable("BIGBLUEFISH_DATA") ?? (OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Application Support/bigbluefish")
            : Path.Combine(Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share"), "bigbluefish"));
        Directory.CreateDirectory(directory); Environment.CurrentDirectory = directory;
        if (OperatingSystem.IsMacOS()) ImportLoginPath();
        Build().StartWithClassicDesktopLifetime(args);
    }
    // Finder starts apps with only the system PATH; Agent CLIs installed by npm or Homebrew are on the login shell's PATH.
    static void ImportLoginPath()
    {
        try {
            var start = new System.Diagnostics.ProcessStartInfo(Environment.GetEnvironmentVariable("SHELL") ?? "/bin/zsh") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var item in new[] { "-ilc", "printf '\\n<bbf-path>%s</bbf-path>' \"$PATH\"" }) start.ArgumentList.Add(item);
            using var shell = System.Diagnostics.Process.Start(start)!; shell.StandardInput.Close(); _ = shell.StandardError.ReadToEndAsync();
            var output = shell.StandardOutput.ReadToEndAsync();
            if (!shell.WaitForExit(5000)) { shell.Kill(true); return; }
            var match = System.Text.RegularExpressions.Regex.Match(output.Result, "<bbf-path>(.+)</bbf-path>");
            if (match.Success) Environment.SetEnvironmentVariable("PATH", match.Groups[1].Value);
        } catch (Exception) { }
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
