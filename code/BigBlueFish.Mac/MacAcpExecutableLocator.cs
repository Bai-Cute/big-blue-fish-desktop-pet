using BigBlueFish.Agent;

namespace BigBlueFish.Windows;

internal static class MacAcpExecutableLocator
{
    internal static string Resolve(string provider, string? configuredPath, string? searchPath = null, string? userHome = null) =>
        string.IsNullOrWhiteSpace(configuredPath) ? Find(provider, searchPath, userHome) : configuredPath.Trim();

    internal static string Find(string provider, string? searchPath = null, string? userHome = null)
    {
        var command = provider switch { "claude" => "claude-agent-acp", "codex" => "codex-acp", _ => "opencode" };
        var home = userHome ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var folders = (searchPath ?? Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();
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
        return HarnessCatalog.FindExecutable(command, string.Join(Path.PathSeparator, folders.Distinct(StringComparer.Ordinal))) ?? "";
    }
}
