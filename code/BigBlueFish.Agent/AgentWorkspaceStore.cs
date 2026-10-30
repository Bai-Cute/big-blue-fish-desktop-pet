using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BigBlueFish.Agent;

public sealed record WorkspaceState(string Current, string[] Recent);

/// <summary>Persistent user files are shared across harnesses; conversation records remain outside them.</summary>
public sealed class AgentWorkspaceStore
{
    private readonly string root, statePath;
    private WorkspaceState state;
    private readonly object gate = new();
    public string Current { get { lock (gate) return state.Current; } }
    public string[] Recent { get { lock (gate) return state.Recent.ToArray(); } }
    public static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    public AgentWorkspaceStore(string root, string? defaultWorkspace = null)
    {
        this.root = Path.GetFullPath(root); statePath = Path.Combine(this.root, "workspace-state.json");
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrEmpty(documents)) documents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents");
        string fallback = Path.GetFullPath(defaultWorkspace ?? Path.Combine(documents, "蓝色大肥鱼工作区"));
        try
        {
            var saved = JsonSerializer.Deserialize<WorkspaceState>(File.ReadAllText(statePath));
            state = saved == null ? new(fallback, [fallback]) : new(Normalize(saved.Current), (saved.Recent ?? []).Where(p => !string.IsNullOrWhiteSpace(p) && Path.IsPathFullyQualified(p)).Select(Normalize).ToArray());
        }
        catch (Exception e) when (e is IOException or JsonException or ArgumentException) { state = new(fallback, [fallback]); }
    }
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) throw new ArgumentException("请选择完整的工作区路径。");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
    public void EnsureCurrent() => Directory.CreateDirectory(Current);
    public void Open(string path)
    {
        path = Normalize(path);
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException("工作区目录不存在：" + path);
        lock (gate)
        {
            var recent = new[] { path }.Concat(state.Recent.Where(p => !p.Equals(path, PathComparison))).Take(30).ToArray();
            var updated = new WorkspaceState(path, recent);
            AtomicText.Write(statePath, JsonSerializer.Serialize(updated)); state = updated;
        }
    }
    public void Create(string parent, string name)
    {
        parent = Normalize(parent);
        if (string.IsNullOrWhiteSpace(name) || name != name.Trim() || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.Contains('/') || name.Contains('\\')) throw new ArgumentException("请填写有效的工作区文件夹名称。");
        string path = Path.Combine(parent, name);
        if (Directory.Exists(path) || File.Exists(path)) throw new IOException("此名称已经存在，请使用“打开工作区”或更换名称。");
        Directory.CreateDirectory(path); Open(path);
    }
    public string SessionDirectory(string? workspace = null)
    {
        string path = Normalize(workspace ?? Current);
        if (OperatingSystem.IsWindows()) path = path.ToUpperInvariant();
        return Path.Combine(root, "sessions", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path))));
    }
    public string SessionPath(string harness, string? workspace = null) => Path.Combine(SessionDirectory(workspace), HarnessCatalog.Get(harness).Id + ".json");
    public void ValidateClear(string expected)
    {
        string path = Normalize(expected);
        if (!path.Equals(Normalize(Current), PathComparison)) throw new IOException("当前工作区已改变，请重新确认。");
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException("工作区已不存在。");
        string[] protectedRoots = [Path.GetPathRoot(path)!, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), AppContext.BaseDirectory];
        if (protectedRoots.Where(p => !string.IsNullOrWhiteSpace(p)).Any(p => path.Equals(Normalize(p), PathComparison))
            || root.Equals(path, PathComparison) || root.StartsWith(path + Path.DirectorySeparatorChar, PathComparison))
            throw new IOException("此目录是用户、系统或程序数据目录，不能作为清空目标。请使用专门的工作文件夹。");
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("请打开实际文件夹后再清空，当前路径是目录链接。");
    }
    public void Clear(string expected)
    {
        lock (gate)
        {
            ValidateClear(expected);
            string sessions = SessionDirectory();
            if (Directory.Exists(sessions)) foreach (var file in Directory.EnumerateFiles(sessions, "*.json")) File.Delete(file);
            // Delete only direct children of the confirmed directory. Recursive deletion does not follow directory links.
            foreach (var child in new DirectoryInfo(Current).EnumerateFileSystemInfos())
                if (child is DirectoryInfo directory) directory.Delete(true); else child.Delete();
        }
    }
}
