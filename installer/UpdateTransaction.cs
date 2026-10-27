using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace BigBlueFish.Setup;

// Existing program files are moved temporarily, not copied into a permanent backup.
// Model/settings files are never part of the transaction.
internal static class UpdateTransaction
{
    private const string JournalName = ".update-state.json";
    private const string PreviousName = ".update-previous";
    private const string StageName = ".update-next";
    private sealed record Journal(string Phase, string[] NewFiles);
    private static string Resolve(string root, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("更新文件路径无效。");
        return path;
    }
    private static bool Preserved(string relative) => BigBlueFish.Deployment.ApplicationData.IsDataFile(relative);
    private static void Write(string target, Journal journal)
    {
        var path = Path.Combine(target, JournalName);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(journal));
        File.Move(path + ".tmp", path, true);
    }
    internal static void Recover(string target)
    {
        var path = Path.Combine(target, JournalName);
        if (!File.Exists(path)) return;
        var journal = JsonSerializer.Deserialize<Journal>(File.ReadAllText(path))!;
        var previous = Path.Combine(target, PreviousName);
        if (journal.Phase != "committed")
        {
            if (journal.Phase == "installing")
            {
                foreach (var relative in journal.NewFiles.Where(p => !Preserved(p)))
                { var file = Resolve(target, relative); if (File.Exists(file)) File.Delete(file); }
                Write(target, journal with { Phase = "restoring" });
            }
            if (Directory.Exists(previous))
                foreach (var file in Directory.GetFiles(previous, "*", SearchOption.AllDirectories))
                {
                    var restored = Resolve(target, Path.GetRelativePath(previous, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(restored)!); File.Move(file, restored, true);
                }
        }
        Clean(target, previous, Path.Combine(target, StageName));
        File.Delete(path);
    }
    internal static void Apply(string target, ZipArchive archive, Action install, Action? verify = null)
    {
        target = InstallationLifecycle.ValidateDirectory(target); Directory.CreateDirectory(target); Recover(target);
        var stage = Path.Combine(target, StageName); var previous = Path.Combine(target, PreviousName);
        var newFiles = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name) && !Preserved(e.FullName)).Select(e => e.FullName).Append("installation-manifest.json").ToArray();
        try
        {
            if (Directory.Exists(stage)) Clean(target, stage);
            Directory.CreateDirectory(stage);
            foreach (var entry in archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name) && !Preserved(e.FullName)))
            {
                var path = Resolve(stage, entry.FullName); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                entry.ExtractToFile(path, true);
            }
            var manifest = Path.Combine(target, "installation-manifest.json");
            using var oldManifest = File.Exists(manifest) ? JsonDocument.Parse(File.ReadAllText(manifest)) : null;
            var oldFiles = oldManifest?.RootElement.GetProperty("Files").EnumerateArray().Select(e => e.GetString()!).ToArray() ?? [];
            var moved = oldFiles.Concat(newFiles).Where(p => !Preserved(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            Write(target, new Journal("moving", newFiles));
            foreach (var relative in moved)
            {
                var original = Resolve(target, relative);
                if (!File.Exists(original)) continue;
                var held = Resolve(previous, relative); Directory.CreateDirectory(Path.GetDirectoryName(held)!);
                File.Move(original, held);
            }
            Write(target, new Journal("installing", newFiles));
            install(); verify?.Invoke();
            Write(target, new Journal("committed", newFiles));
            Recover(target);
        }
        catch
        {
            Recover(target);
            if (File.Exists(Path.Combine(target, "BigBlueFish.exe")))
            {
                try { InstallerForm.FinishInstallation(target, Environment.ProcessPath!); } catch { }
            }
            throw;
        }
        finally
        {
            if (!File.Exists(Path.Combine(target, JournalName))) Clean(target, stage);
        }
    }
    private static void Clean(string target, params string[] directories)
    {
        foreach (var directory in directories)
        {
            if (!Path.GetFullPath(directory).StartsWith(Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("更新临时目录无效。");
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
