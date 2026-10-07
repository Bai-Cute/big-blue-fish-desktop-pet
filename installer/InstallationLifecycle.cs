using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace BigBlueFish.Setup;

internal static class InstallationLifecycle
{
    internal const string UninstallerName = "Uninstall.exe";
    private const string InfoName = "uninstall-info.json";
    private const string UninstallRoot = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    internal const string ApplicationPathKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\BigBlueFish.exe";
    private sealed record InstallInfo(string Directory, string[] Shortcuts, string UserSid);

    internal static string ValidateDirectory(string directory)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var forbidden = new[] { Path.GetPathRoot(full), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Path.TrimEndingDirectorySeparator(Path.GetTempPath()) };
        if (forbidden.Any(p => !string.IsNullOrEmpty(p) && full.Equals(Path.TrimEndingDirectorySeparator(p), StringComparison.OrdinalIgnoreCase)))
            throw new IOException("请选择蓝色大肥鱼自己的安装文件夹。");
        if (Path.GetFileName(full).Equals("Program Files", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(full).Equals("Program Files (x86)", StringComparison.OrdinalIgnoreCase)
            || (Directory.Exists(full) && (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0))
            throw new IOException("请选择蓝色大肥鱼自己的安装文件夹。");
        return full;
    }

    internal static void GrantUserAccess(string directory)
    {
        var target = ValidateDirectory(directory);
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        var rule = new FileSystemAccessRule(users, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow);
        var folder = new DirectoryInfo(target);
        var acl = folder.GetAccessControl();
        acl.SetAccessRule(rule);
        folder.SetAccessControl(acl);
        // Existing data may have protected ACLs. Grant access there as well, without following links outside the app.
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var item in folder.EnumerateFileSystemInfos("*", options))
        {
            if (item is DirectoryInfo child)
            {
                var childAcl = child.GetAccessControl();
                if (!childAcl.AreAccessRulesProtected) continue;
                childAcl.SetAccessRule(rule);
                child.SetAccessControl(childAcl);
            }
            else if (item is FileInfo file)
            {
                var fileAcl = file.GetAccessControl();
                if (!fileAcl.AreAccessRulesProtected) continue;
                fileAcl.SetAccessRule(new FileSystemAccessRule(users, FileSystemRights.FullControl, AccessControlType.Allow));
                file.SetAccessControl(fileAcl);
            }
        }
    }

    internal static string RegistrationName(string directory) => "BigBlueFish-" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ValidateDirectory(directory).ToUpperInvariant())))[..16];

    internal static void Register(string directory, params string[] shortcuts)
    {
        var target = ValidateDirectory(directory);
        if (!File.Exists(Path.Combine(target, UninstallerName)))
            throw new IOException("安装文件中缺少卸载程序。");
        var info = new InstallInfo(target, shortcuts, WindowsIdentity.GetCurrent().User!.Value);
        File.WriteAllText(Path.Combine(target, InfoName), JsonSerializer.Serialize(info));
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = machine.CreateSubKey(UninstallRoot + "\\" + RegistrationName(target));
        key.SetValue("DisplayName", "蓝色大肥鱼");
        key.SetValue("DisplayVersion", typeof(InstallationLifecycle).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion);
        key.SetValue("Publisher", "Bai-Cute");
        key.SetValue("InstallLocation", target);
        key.SetValue("DisplayIcon", Path.Combine(target, "vpeticon.ico"));
        key.SetValue("URLInfoAbout", "https://github.com/Bai-Cute/big-blue-fish-desktop-pet");
        key.SetValue("UninstallString", $"\"{Path.Combine(target, UninstallerName)}\" --uninstall \"{target}\"");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
        using var application = machine.CreateSubKey(ApplicationPathKey);
        application.SetValue("", Path.Combine(target, "VPet-Simulator.Windows.exe"));
        application.SetValue("Path", target);
    }

    internal static string ValidateInstallation(string directory)
    {
        var target = ValidateDirectory(directory);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(target, "installation-manifest.json")));
        if (!manifest.RootElement.GetProperty("Files").EnumerateArray()
            .Any(x => string.Equals(x.GetString(), "VPet-Simulator.Windows.exe", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("未找到蓝色大肥鱼的安装记录。");
        var info = JsonSerializer.Deserialize<InstallInfo>(File.ReadAllText(Path.Combine(target, InfoName)))!;
        if (!target.Equals(info.Directory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("安装目录与记录不一致。");
        return target;
    }

    internal static void Uninstall(string directory, bool removeData)
    {
        var target = ValidateInstallation(directory);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(target, "installation-manifest.json")));
        var files = manifest.RootElement.GetProperty("Files").EnumerateArray().Select(x => x.GetString()!).ToArray();
        var info = JsonSerializer.Deserialize<InstallInfo>(File.ReadAllText(Path.Combine(target, InfoName)))!;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                string? path;
                try { path = process.MainModule?.FileName; } catch { continue; }
                if (path == null || !IsWithin(target, path) || process.Id == Environment.ProcessId) continue;
                if (process.ProcessName == "VPet-Simulator.Windows" && process.CloseMainWindow()) process.WaitForExit(3000);
                if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(5000); }
            }
        }
        var executable = Path.Combine(target, "VPet-Simulator.Windows.exe");
        foreach (var shortcut in info.Shortcuts)
            RemoveShortcut(shortcut, executable);
        var menuGroup = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "蓝色大肥鱼");
        if (Directory.Exists(menuGroup) && !Directory.EnumerateFileSystemEntries(menuGroup).Any()) Directory.Delete(menuGroup);
        foreach (var sid in Registry.Users.GetSubKeyNames().Where(s => s.StartsWith("S-1-5-21-") && !s.EndsWith("_Classes")))
        {
            using var user = Registry.Users.OpenSubKey(sid + @"\Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            foreach (var name in user?.GetValueNames() ?? [])
                if ((user!.GetValue(name) as string)?.Contains(executable, StringComparison.OrdinalIgnoreCase) == true)
                    user.DeleteValue(name, throwOnMissingValue: false);
        }
        foreach (var relative in files.Concat(new[] { UninstallerName, InfoName, "installation-manifest.json" }))
        {
            var path = Path.GetFullPath(Path.Combine(target, relative));
            if (!IsWithin(target, path)) throw new InvalidDataException("安装记录中的文件路径无效。");
            if (File.Exists(path)) File.Delete(path);
        }
        if (removeData)
        {
            foreach (var name in new[] { "models", "cache" })
            {
                var path = Path.Combine(target, name);
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            }
            foreach (var pattern in new[] { "preferences.json*", "news-last-day.txt*", "companion.lock", "ready.status", "companion-errors.log", "test-*.json", "test-command.txt", "test-render.png", "test-settings.png", "ocr-status.json", "vision-status.json", "vision-input.jpg" })
                foreach (var path in Directory.EnumerateFiles(target, pattern)) File.Delete(path);
        }
        DeleteEmptyDirectories(target);
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        machine.DeleteSubKeyTree(UninstallRoot + "\\" + RegistrationName(target), throwOnMissingSubKey: false);
        bool ownsApplicationPath;
        using (var application = machine.OpenSubKey(ApplicationPathKey))
            ownsApplicationPath = string.Equals(application?.GetValue("") as string, executable, StringComparison.OrdinalIgnoreCase);
        if (ownsApplicationPath) machine.DeleteSubKeyTree(ApplicationPathKey, throwOnMissingSubKey: false);
    }

    private static bool IsWithin(string directory, string path) => path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static void DeleteEmptyDirectories(string directory)
    {
        foreach (var child in Directory.EnumerateDirectories(directory))
            if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) DeleteEmptyDirectories(child);
        if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
    }

    private static void RemoveShortcut(string path, string executable)
    {
        if (!File.Exists(path)) return;
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null) return;
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(path);
        if (string.Equals((string)shortcut.TargetPath, executable, StringComparison.OrdinalIgnoreCase)
            || string.Equals((string)shortcut.TargetPath, Path.Combine(Path.GetDirectoryName(executable)!, UninstallerName), StringComparison.OrdinalIgnoreCase)) File.Delete(path);
        Marshal.FinalReleaseComObject(shortcut);
        Marshal.FinalReleaseComObject(shell);
    }

}
