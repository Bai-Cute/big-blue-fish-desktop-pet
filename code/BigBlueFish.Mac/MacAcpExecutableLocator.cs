using BigBlueFish.Agent;

namespace BigBlueFish.Windows;

internal static class MacAcpExecutableLocator
{
    internal static string ProviderAt(int index) => index >= 0 && index < HarnessCatalog.All.Count
        ? HarnessCatalog.All[index].Id : "opencode";

    internal static int IndexOf(string provider)
    {
        for (var i = 0; i < HarnessCatalog.All.Count; i++)
            if (HarnessCatalog.All[i].Id == provider) return i;
        return IndexOf("opencode");
    }

    internal static bool IsAdapterExecutable(string provider, string path) =>
        HarnessCatalog.Get(provider).AdapterCommand is { } command &&
        string.Equals(Path.GetFileNameWithoutExtension(path), command, StringComparison.OrdinalIgnoreCase);

    internal static string[] StartArguments(string provider, string path) => IsAdapterExecutable(provider, path)
        ? [] : HarnessCatalog.Get(provider).Arguments;

    internal static (string Path, string[] Arguments)? FindReadyCommand(string provider, string path,
        string? searchPath = null, string? userHome = null)
    {
        // A manually selected adapter wins; a selected CLI still uses an installed ACP adapter.
        if (IsAdapterExecutable(provider, path) && File.Exists(path)) return (path, []);
        if (provider == "antigravity" && Path.GetFileName(path).StartsWith("agy_acp_server", StringComparison.Ordinal) &&
            File.Exists(path) && AgentAdapterStore.AntigravityHelper(path) != null) return (path, []);
        var harness = HarnessCatalog.Get(provider);
        if (harness.AdapterCommand is { } adapter && FindCommand(adapter, searchPath, userHome) is { } installed)
            return (installed, []);
        if (provider == "antigravity" && FindCommand("agy_acp_server", searchPath, userHome) is { } official &&
            AgentAdapterStore.AntigravityHelper(official) != null) return (official, []);
        return null;
    }

    internal static string Resolve(string provider, string? configuredPath, string? searchPath = null, string? userHome = null) =>
        string.IsNullOrWhiteSpace(configuredPath) ? Find(provider, searchPath, userHome) : configuredPath.Trim();

    internal static string Find(string provider, string? searchPath = null, string? userHome = null)
    {
        var harness = HarnessCatalog.Get(provider);
        foreach (var command in harness.Commands)
            if (FindCommand(command, searchPath, userHome) is { } found) return found;
        return harness.AdapterCommand is { } adapter ? FindCommand(adapter, searchPath, userHome) ?? "" : "";
    }

    internal static string? FindCommand(string command, string? searchPath = null, string? userHome = null)
    {
        var home = userHome ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var folders = (searchPath ?? Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (searchPath == null)
        {
            folders.AddRange([
                "/opt/homebrew/bin", "/usr/local/bin", "/usr/bin",
                Path.Combine(home, ".local/bin"), Path.Combine(home, ".opencode/bin"),
                Path.Combine(home, ".npm-global/bin"), Path.Combine(home, ".volta/bin"),
                Path.Combine(home, ".bun/bin"), Path.Combine(home, ".local/share/pnpm"),
                Path.Combine(home, ".asdf/shims")
            ]);
            var nvm = Path.Combine(home, ".nvm/versions/node");
            try {
                if (Directory.Exists(nvm)) folders.AddRange(Directory.GetDirectories(nvm).OrderByDescending(x => x, StringComparer.Ordinal).Select(x => Path.Combine(x, "bin")));
            } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        var paths = string.Join(Path.PathSeparator, folders.Distinct(StringComparer.Ordinal));
        return HarnessCatalog.FindExecutable(command, paths);
    }
}
