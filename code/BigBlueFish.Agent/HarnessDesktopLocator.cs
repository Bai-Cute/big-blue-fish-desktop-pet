using System.Diagnostics;
using Microsoft.Win32;

namespace BigBlueFish.Agent;

/// <summary>
/// Launches the CLI that ships inside an installed desktop application instead of requiring the same
/// CLI on PATH. The desktop app carries its own Electron runtime, so the caller starts that executable
/// with <c>ELECTRON_RUN_AS_NODE=1</c> and the bundled entry script as the first argument.
/// Discovery and validation are read-only file probes; nothing is started while detecting.
/// </summary>
public static class HarnessDesktopLocator
{
    public const string RuntimeManifest = "desktop-runtime.json";
    public const string RuntimeDirectory = "dsh";
    public const string HarnessPackage = "@deepseek-ai/dsh";
    public const string AcpPackage = "@deepseek-ai/dsh-acp-app";
    public const string ApplicationName = "DeepSeek Harness";

    static readonly object gate = new();
    static readonly Dictionary<string, (string? Executable, DateTimeOffset At)> detected = new(StringComparer.Ordinal);
    public static void Refresh() { lock (gate) detected.Clear(); }

    /// <summary>Find the installed desktop application whose app.asar contains <paramref name="bundleEntry"/>.</summary>
    public static string? Find(string bundleEntry)
    {
        // The connection page asks twice while drawing one list; reading a large asar index twice is wasted work.
        lock (gate)
            if (detected.TryGetValue(bundleEntry, out var cached) && DateTimeOffset.UtcNow - cached.At < TimeSpan.FromSeconds(5))
                return cached.Executable;
        string? executable = Candidates().FirstOrDefault(candidate => File.Exists(candidate) && TryValidate(candidate, bundleEntry, out _));
        lock (gate) detected[bundleEntry] = (executable, DateTimeOffset.UtcNow);
        return executable;
    }

    /// <summary>Absolute path of the bundled ACP entry script inside the application's app.asar.</summary>
    public static string BundlePath(string executable, string bundleEntry) =>
        Path.Combine(ResourcesDirectory(executable), "app.asar", bundleEntry.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Directory holding app.asar: the app's resources folder, which differs on macOS bundles.</summary>
    public static string ResourcesDirectory(string executable)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(executable)) ?? ".";
        return OperatingSystem.IsMacOS()
            ? Path.GetFullPath(Path.Combine(folder, "..", "Resources"))
            : Path.Combine(folder, "resources");
    }

    /// <summary>The bundled entry script first, then the harness's own arguments.</summary>
    public static string[] Arguments(string executable, string bundleEntry, IEnumerable<string> harnessArguments) =>
        [BundlePath(executable, bundleEntry), .. harnessArguments];

    /// <summary>Confirm that this application really carries the harness package and its ACP bridge.</summary>
    public static bool TryValidate(string executable, string bundleEntry, out string? reason)
    {
        string archivePath = Path.Combine(ResourcesDirectory(executable), "app.asar");
        if (!File.Exists(archivePath)) { reason = "缺少 app.asar"; return false; }
        using AsarArchive? archive = AsarArchive.Open(archivePath);
        if (archive == null) { reason = "app.asar 无法读取"; return false; }
        if (!archive.Contains(RuntimeDirectory + "/" + RuntimeManifest)) { reason = "缺少 " + RuntimeManifest; return false; }
        if (!archive.Contains(RuntimeDirectory + "/node_modules/" + HarnessPackage + "/package.json"))
        { reason = "安装目录缺少 " + HarnessPackage; return false; }
        if (!archive.Contains(RuntimeDirectory + "/node_modules/" + AcpPackage + "/package.json"))
        { reason = "此版本不含 ACP 组件"; return false; }
        if (!archive.Contains(bundleEntry)) { reason = "缺少 ACP 入口"; return false; }
        reason = null; return true;
    }

    /// <summary>Explain a failed detection without pretending the user did something wrong.</summary>
    public static string NotFoundMessage(HarnessDefinition harness)
    {
        if (harness.DesktopBundle == null)
            return "未找到 " + harness.Name + "，请先按官方说明安装，并确认程序位于 PATH 中。";
        foreach (string executable in Candidates())
        {
            if (!File.Exists(executable)) continue;
            if (TryValidate(executable, harness.DesktopBundle, out string? reason)) continue;
            return "找到 " + executable + "，但它不能提供 ACP（" + reason + "）。请升级 DeepSeek Harness 桌面版，或安装 dsh 命令行。";
        }
        return "未找到 " + harness.Name + "。请安装 DeepSeek Harness 桌面版（免命令行安装），或安装 dsh 命令行。";
    }

    static IEnumerable<string> Candidates()
    {
        foreach (string executable in InstalledExecutables())
            if (File.Exists(executable)) yield return executable;
        foreach (string executable in RunningExecutables()) yield return executable;
    }

    static IEnumerable<string> InstalledExecutables()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (string folder in WindowsInstallFolders())
                yield return Path.Combine(folder, ApplicationName + ".exe");
            yield break;
        }
        if (OperatingSystem.IsMacOS())
        {
            foreach (string application in MacApplications())
                yield return Path.Combine(application, "Contents", "MacOS", ApplicationName);
            yield break;
        }
        foreach (string binary in new[] { "deepseek-harness", "DeepSeek Harness" })
            yield return Path.Combine("/opt", ApplicationName, binary);
        yield return Path.Combine("/opt", "deepseek-harness", "deepseek-harness");
    }

    static IEnumerable<string> WindowsInstallFolders()
    {
        string? local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (!string.IsNullOrEmpty(local)) yield return Path.Combine(local, "Programs", ApplicationName);
        foreach (string variable in new[] { "ProgramFiles", "ProgramFiles(x86)" })
        {
            string? value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrEmpty(value)) yield return Path.Combine(value, ApplicationName);
        }
        if (RegistryInstallFolder() is { } installed) yield return installed;
    }

    static IEnumerable<string> MacApplications() =>
        [Path.Combine("/Applications", ApplicationName + ".app"),
         Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications", ApplicationName + ".app")];

    /// <summary>Running desktop instances cover custom install folders and AppImage mounts.</summary>
    static IEnumerable<string> RunningExecutables()
    {
        Process[] processes;
        try { processes = Process.GetProcessesByName(ApplicationName); }
        catch (Exception) { return []; }
        var found = new List<string>();
        foreach (var process in processes)
        {
            using (process)
            {
                try { if (process.MainModule is { FileName: { Length: > 0 } file }) found.Add(file); }
                catch (Exception) { }
            }
        }
        return found;
    }

    static string? RegistryInstallFolder()
    {
        return QueryRegistry();
    }

    /// <summary>Match DisplayName first, then read InstallLocation even in an arbitrarily named directory.</summary>
    static string? QueryRegistry()
    {
        if (!OperatingSystem.IsWindows()) return null;
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall == null) continue;
                foreach (string name in uninstall.GetSubKeyNames())
                {
                    try
                    {
                        using var key = uninstall.OpenSubKey(name);
                        if (key?.GetValue("DisplayName") is not string display || !display.StartsWith(ApplicationName, StringComparison.OrdinalIgnoreCase)) continue;
                        if (key.GetValue("InstallLocation") is not string location) continue;
                        string path = Environment.ExpandEnvironmentVariables(location.Trim().Trim('"'));
                        if (File.Exists(Path.Combine(path, ApplicationName + ".exe"))) return path;
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        }
        return null;
    }
}
