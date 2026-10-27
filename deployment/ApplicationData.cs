using System;
using System.IO;
using System.Linq;

namespace BigBlueFish.Deployment;

// Program files stay immutable. Callers pass the install root explicitly;
// runtime relative paths are rooted in this data directory, never in the payload.
internal static class ApplicationData
{
    internal static string DirectoryFor(string installation) => Path.Combine(Path.GetFullPath(installation), "data");
    internal static bool IsDataFile(string relative)
    {
        var p = relative.Replace('\\', '/');
        return p.StartsWith("data/", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("cache/", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("preferences.json", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("model-selection.json", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("test-", StringComparison.OrdinalIgnoreCase)
            || p is "companion.lock" or "ready.status" or "news-last-day.txt" or "companion-errors.log"
                or "ocr-status.json" or "vision-status.json" or "vision-input.jpg" or "model-request.json";
    }

    // Moves (including multi-GB models) instead of copying. Re-entry after a
    // process kill resumes file by file. Existing destination data always wins;
    // a conflicting source is retained rather than destroying either version.
    internal static string Prepare(string installation)
    {
        var root = Path.GetFullPath(installation);
        var data = DirectoryFor(root);
        RejectLink(data);
        Directory.CreateDirectory(data);
        foreach (var name in new[] { "models", "cache" })
        {
            var source = Path.Combine(root, name);
            if (System.IO.Directory.Exists(source)) MoveDirectory(source, Path.Combine(data, name));
        }
        foreach (var source in System.IO.Directory.EnumerateFiles(root).Where(p => IsDataFile(Path.GetFileName(p))))
        {
            RejectLink(source);
            var destination = Path.Combine(data, Path.GetFileName(source));
            if (!File.Exists(destination)) File.Move(source, destination);
        }
        return data;
    }

    private static void MoveDirectory(string source, string destination)
    {
        RejectLink(source); RejectLink(destination);
        if (!System.IO.Directory.Exists(destination)) { System.IO.Directory.Move(source, destination); return; }
        foreach (var path in System.IO.Directory.EnumerateFileSystemEntries(source))
        {
            RejectLink(path);
            var target = Path.Combine(destination, Path.GetFileName(path));
            if (System.IO.Directory.Exists(path)) MoveDirectory(path, target);
            else if (!File.Exists(target)) File.Move(path, target);
        }
        if (!System.IO.Directory.EnumerateFileSystemEntries(source).Any()) System.IO.Directory.Delete(source);
    }

    private static void RejectLink(string path)
    {
        if ((File.Exists(path) || System.IO.Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("数据目录不能使用链接：" + path);
    }
}
