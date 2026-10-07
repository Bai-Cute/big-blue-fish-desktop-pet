using System.Diagnostics;
using System.IO.Compression;
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

    internal static void Register(string directory, string installer, params string[] shortcuts)
    {
        var target = ValidateDirectory(directory);
        CopyUninstaller(installer, Path.Combine(target, UninstallerName));
        var info = new InstallInfo(target, shortcuts, WindowsIdentity.GetCurrent().User!.Value);
        File.WriteAllText(Path.Combine(target, InfoName), JsonSerializer.Serialize(info));
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = machine.CreateSubKey(UninstallRoot + "\\" + RegistrationName(target));
        key.SetValue("DisplayName", "蓝色大肥鱼");
        key.SetValue("DisplayVersion", InstallerForm.ProductVersion);
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

    private static void CopyUninstaller(string installer, string destination)
    {
        using var input = File.OpenRead(installer);
        var marker = Encoding.ASCII.GetBytes("BIGBLUEFISH_PAYLOAD_V1");
        input.Seek(-8, SeekOrigin.End);
        var length = new byte[8];
        input.ReadExactly(length);
        long markerOffset = input.Length - 8 - marker.Length;
        input.Position = markerOffset;
        var actual = new byte[marker.Length];
        input.ReadExactly(actual);
        long remaining = markerOffset - BitConverter.ToInt64(length);
        if (!actual.SequenceEqual(marker) || remaining <= 0) throw new InvalidDataException("安装程序不完整。");
        input.Position = 0;
        using var output = File.Create(destination + ".partial");
        var buffer = new byte[1024 * 1024];
        while (remaining > 0)
        {
            int count = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (count == 0) throw new EndOfStreamException();
            output.Write(buffer, 0, count);
            remaining -= count;
        }
        output.Dispose();
        File.Move(destination + ".partial", destination, overwrite: true);
    }

    internal static void Uninstall(string directory, bool removeData)
    {
        var target = ValidateDirectory(directory);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(target, "installation-manifest.json")));
        var files = manifest.RootElement.GetProperty("Files").EnumerateArray().Select(x => x.GetString()!).ToArray();
        if (!files.Contains("VPet-Simulator.Windows.exe", StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("未找到蓝色大肥鱼的安装记录。");
        var info = JsonSerializer.Deserialize<InstallInfo>(File.ReadAllText(Path.Combine(target, InfoName)))!;
        if (!target.Equals(info.Directory, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("安装目录与记录不一致。");
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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileEx(string existing, string? destination, int flags);

    internal static Form CreateUninstallForm(string target)
    {
            var form = new Form { Text = "卸载蓝色大肥鱼", ClientSize = new Size(480, 210),
                StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false, MinimizeBox = false, Font = new Font("Microsoft YaHei UI", 10),
                AutoScaleDimensions = new SizeF(96, 96), AutoScaleMode = AutoScaleMode.Dpi,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 3, Padding = new Padding(24) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 3; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var label = new Label { Text = "准备卸载蓝色大肥鱼。勾选下方选项，可以保留模型和设置，方便以后重新安装。",
                AutoSize = true, MaximumSize = new Size(420, 0), Margin = new Padding(0, 0, 0, 18) };
            var data = new CheckBox { Text = "保留模型和设置", Checked = false, AutoSize = true, Margin = new Padding(0, 0, 0, 22) };
            var button = new Button { Text = "卸载", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(110, 38), Padding = new Padding(12, 4, 12, 4), Anchor = AnchorStyles.Right, Margin = Padding.Empty };
            button.Click += (_, _) =>
            {
                try
                {
                var temporary = Path.Combine(Path.GetTempPath(), "BigBlueFish-Uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
                File.Copy(Environment.ProcessPath!, temporary);
                var start = new ProcessStartInfo(temporary) { UseShellExecute = true, WorkingDirectory = Path.GetTempPath() };
                start.ArgumentList.Add("--uninstall-worker");
                start.ArgumentList.Add(target);
                if (!data.Checked) start.ArgumentList.Add("--remove-data");
                Process.Start(start);
                form.Close();
                }
                catch (Exception error)
                {
                    MessageBox.Show(form, error.Message, "蓝色大肥鱼卸载未完成", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            layout.Controls.Add(label, 0, 0);
            layout.Controls.Add(data, 0, 1);
            layout.Controls.Add(button, 0, 2);
            form.Controls.Add(layout);
            return form;
    }

    internal static int RunUninstall(string[] args)
    {
        try
        {
            if (args.Length < 2) throw new ArgumentException("未提供安装目录。");
            var target = ValidateDirectory(args[1]);
            if (args[0] == "--uninstall-worker")
            {
                Uninstall(target, args.Contains("--remove-data"));
                MoveFileEx(Environment.ProcessPath!, null, 4);
                MessageBox.Show("蓝色大肥鱼已卸载。", "蓝色大肥鱼", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            using var form = CreateUninstallForm(target);
            Application.Run(form);
            return 0;
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "蓝色大肥鱼卸载未完成", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
