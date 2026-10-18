using System.IO.Compression;
using System.Diagnostics;
using System.Windows.Automation;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;
using System.Reflection;
using BigBlueFish.Setup;
using BigBlueFish.Uninstaller;

namespace BigBlueFish.InstallerVerification;

internal static class Checks
{
    static int count;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        count++;
        Console.WriteLine("PASS " + message);
    }

    static void VerifyInstallerEntry(string executable)
    {
        var start = new ProcessStartInfo(Path.GetFullPath(executable)) { WorkingDirectory = Path.GetTempPath(), UseShellExecute = false };
        start.Environment["DOTNET_ROOT"] = Path.Combine(Path.GetTempPath(), "BigBlueFish-No-System-Dotnet");
        start.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
        using var process = Process.Start(start)!;
        try
        {
            var limit = DateTime.UtcNow.AddSeconds(20);
            do { Thread.Sleep(100); process.Refresh(); }
            while (!process.HasExited && process.MainWindowHandle == IntPtr.Zero && DateTime.UtcNow < limit);
            Check(!process.HasExited && process.MainWindowTitle.Contains("安装程序"), "actual setup starts installation window independently");
            Check(process.CloseMainWindow() && process.WaitForExit(5000), "cancelling actual installer leaves installation unchanged");
        }
        finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(5000); } }
    }

    static void VerifyUninstallerEntry(string target, bool explicitArguments)
    {
        var executable = Path.Combine(target, "Uninstall.exe");
        var start = new ProcessStartInfo(executable) { WorkingDirectory = Path.GetTempPath(), UseShellExecute = false };
        start.Environment["DOTNET_ROOT"] = Path.Combine(Path.GetTempPath(), "BigBlueFish-No-System-Dotnet");
        start.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
        if (explicitArguments) { start.ArgumentList.Add("--uninstall"); start.ArgumentList.Add(target); }
        using var process = Process.Start(start)!;
        try
        {
            var limit = DateTime.UtcNow.AddSeconds(20);
            do { Thread.Sleep(100); process.Refresh(); }
            while (!process.HasExited && process.MainWindowHandle == IntPtr.Zero && DateTime.UtcNow < limit);
            Check(!process.HasExited && process.MainWindowTitle == "卸载蓝色大肥鱼",
                explicitArguments ? "registered command opens actual uninstall window" : "zero-argument Uninstall.exe opens actual uninstall window from unrelated working directory");
            var window = AutomationElement.FromHandle(process.MainWindowHandle);
            var box = window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.CheckBox));
            Check(box != null && box.Current.Name == "保留模型和设置" && ((TogglePattern)box.GetCurrentPattern(TogglePattern.Pattern)).Current.ToggleState == ToggleState.Off,
                "actual standalone uninstall window has unchecked retain-data checkbox");
            Check(window.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                new PropertyCondition(AutomationElement.NameProperty, "卸载"))) != null, "actual window offers uninstall button");
            Check(process.CloseMainWindow() && process.WaitForExit(5000), "cancel closes actual uninstaller without executing removal");
            Check(File.Exists(executable) && File.Exists(Path.Combine(target, "preferences.json")), "cancelling uninstall preserves application and settings");
        }
        finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(5000); } }
    }

    static void VerifyUninstallWorker(string target, bool removeData)
    {
        // The production GUI also runs a copy outside the installation, so it can delete itself.
        var worker = target + "-worker-" + Guid.NewGuid().ToString("N") + ".exe";
        File.Copy(Path.Combine(target, "Uninstall.exe"), worker);
        var start = new ProcessStartInfo(worker) { WorkingDirectory = Path.GetTempPath(), UseShellExecute = false };
        start.ArgumentList.Add("--uninstall-worker"); start.ArgumentList.Add(target);
        if (removeData) start.ArgumentList.Add("--remove-data");
        using var process = Process.Start(start)!;
        try
        {
            var limit = DateTime.UtcNow.AddSeconds(30);
            do { Thread.Sleep(100); process.Refresh(); }
            while (!process.HasExited && process.MainWindowHandle == IntPtr.Zero && DateTime.UtcNow < limit);
            Check(!process.HasExited && process.MainWindowHandle != IntPtr.Zero, "actual uninstall worker reaches completion dialog");
            var dialog = AutomationElement.FromHandle(process.MainWindowHandle);
            Check(dialog.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "蓝色大肥鱼已卸载。")) != null,
                "actual standalone worker reports successful removal");
            var ok = dialog.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                new PropertyCondition(AutomationElement.NameProperty, "确定")));
            ((InvokePattern)ok!.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            Check(process.WaitForExit(5000) && process.ExitCode == 0, "actual standalone worker exits successfully");
        }
        finally
        {
            if (!process.HasExited) { process.Kill(); process.WaitForExit(5000); }
            File.Delete(worker);
        }
    }

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "--download-interrupt-worker") return DeploymentVerification.InterruptWorker(args[1]);
            if (args.Length == 2 && args[0] == "--entry-only")
            {
                VerifyUninstallerEntry(Path.GetFullPath(args[1]), explicitArguments: false);
                VerifyUninstallerEntry(Path.GetFullPath(args[1]), explicitArguments: true);
                Console.WriteLine($"RESULT {count} actual installed entry checks passed");
                return 0;
            }
            if (args.Length >= 2 && args[0] == "--ui-layout") return DeploymentVerification.Ui(args[1]);
            if (args.Length >= 2 && args[0] == "--deployment") return DeploymentVerification.Rules(args[1]).GetAwaiter().GetResult();
            if (args.Length >= 2 && args[0] == "--model-ui") return DeploymentVerification.ModelUi(args[1]);
            if (args.Length == 4 && args[0] == "--update-live") return UpdateVerification.Run(args[1], args[2], args[3]);
            if (args.Length == 2 && args[0] == "--model-download-live") return DeploymentVerification.LiveDownload(args[1]).GetAwaiter().GetResult();
            if (args.Length == 2 && args[0] == "--write-probe") return RestrictedUserProbe.WriteProbe(args[1]);
            var target = Path.GetFullPath(args[1]);
            // Local developer deployment uses the very same extractor as the GUI installer.
            // This branch creates no test fixtures and never downloads or changes a model.
            if (args.Length == 3 && args[2] == "--apply-local-update")
            {
                using var payloadStream = InstallerForm.OpenPayload(args[0]);
                using var payloadArchive = new ZipArchive(payloadStream, ZipArchiveMode.Read);
                InstallerForm.ExtractApplication(payloadArchive, target);
                InstallerForm.FinishInstallation(target, args[0]);
                Console.WriteLine($"Installed {InstallerForm.ProductVersion} {InstallerForm.InputMode} to {target}");
                return 0;
            }
            Directory.CreateDirectory(target);
            var settings = Path.Combine(target, "preferences.json");
            if (!File.Exists(settings)) File.WriteAllText(settings, "{\"WeatherRegionCode\":\"340104\"}");
            Directory.CreateDirectory(Path.Combine(target, "models"));
            var retained = Path.Combine(target, "models", "retention-fixture.txt");
            if (!File.Exists(retained)) File.WriteAllText(retained, "retained-model-marker");
            Directory.CreateDirectory(Path.Combine(target, "cache"));
            var weatherCache = Path.Combine(target, "cache", "weather.json");
            File.WriteAllText(weatherCache, "weather-retention-marker");
            var before = File.ReadAllBytes(settings);
            var legacyFiles = new[] { "VPet-Simulator.Windows.exe", "VPet-Simulator.Windows.dll",
                "VPet-Simulator.Windows.deps.json", "VPet-Simulator.Windows.runtimeconfig.json", "vpeticon.ico" };
            foreach (var legacy in legacyFiles) File.WriteAllText(Path.Combine(target, legacy), "old-application-marker");
            var legacyPath = Path.Combine(target, InstallationLifecycle.LegacyApplicationName);
            var newPath = Path.Combine(target, InstallationLifecycle.ApplicationName);
            Check(InstallationLifecycle.MigrateStartupCommand($"\"{legacyPath}\" --example", target) == $"\"{newPath}\" --example",
                "upgrade migrates enabled startup command and preserves arguments");
            Check(InstallationLifecycle.MigrateStartupCommand(legacyPath, target) == $"\"{newPath}\"",
                "upgrade migrates an unquoted legacy startup path");
            Check(InstallationLifecycle.MigrateStartupCommand($"\"{target}-other\\VPet-Simulator.Windows.exe\"", target)
                == $"\"{target}-other\\VPet-Simulator.Windows.exe\"", "upgrade leaves unrelated startup entry alone");
            using var versionStream = typeof(Checks).Assembly.GetManifestResourceStream(typeof(Checks).Assembly.GetManifestResourceNames().Single(n => n.EndsWith("Version.props")))!;
            string expectedVersion = XDocument.Load(versionStream).Descendants("Version").Single().Value;
            Check(InstallerForm.ProductVersion == expectedVersion, "installer product version matches Version.props");
            VerifyInstallerEntry(args[0]);
            using var stream = InstallerForm.OpenPayload(args[0]);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var names = archive.Entries.Select(e => e.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            bool mcpIncluded = InstallerForm.Edition == "Standard";
            foreach (var component in new[] { "BigBlueFish.Mcp.exe", "BigBlueFish.Mcp.dll", "BigBlueFish.Control.dll" })
                Check(names.Contains(component) == mcpIncluded, "MCP component matches installer edition: " + component);
            foreach (var required in new[] { "BigBlueFish.exe", "Uninstall.exe", "BigBlueFish.dll",
                "coreclr.dll", "hostfxr.dll",
                "PresentationFramework.dll", "runtime/llama-server.exe", "runtime/ggml-vulkan.dll", "runtime/mtmd.dll",
                "media/ffmpeg.exe", "media/FFmpeg-LICENSE.txt", "assets/fish/animations.json", "assets/fish/LICENSE.txt" })
                Check(names.Contains(required), "payload includes " + required);
            foreach (var obsolete in new[] { "VPet-Simulator.Core.dll", "VPet-Simulator.Windows.Interface.dll",
                "VPet-Simulator.Windows.exe", "VPet-Simulator.Windows.dll",
                "steam_api.dll", "steam_api64.dll", "Facepunch.Steamworks.Win64.dll", "LinePutScript.dll",
                "Panuon.WPF.dll", "Panuon.WPF.UI.dll", "NAudio.dll", "SkiaSharp.dll", "WpfAnimatedGif.dll" })
                Check(!names.Contains(obsolete), "payload excludes unused upstream component " + obsolete);
            foreach (var required in new[] { "runtime/msvcp140.dll", "runtime/vcruntime140.dll", "runtime/vcruntime140_1.dll", "runtime/visual-cpp-runtime.json", "licenses/Microsoft-Visual-Cpp-Runtime.txt" })
                Check(names.Contains(required), "payload includes native runtime dependency " + required);
            Check(names.Count(n => n.StartsWith("assets/fish/") && n.EndsWith(".webm")) == 106, "payload includes all 106 animations");
            Check(!names.Any(n => n.StartsWith("assets/pet/")), "old PNG animation pack is absent");
            Check(names.Contains("WinRT.Runtime.dll") == !InstallerForm.RequiresVision, "OCR projections match input mode");
            InstallerForm.ExtractApplication(archive, target);
            foreach (var legacy in legacyFiles) Check(!File.Exists(Path.Combine(target, legacy)), "upgrade removes old application file " + legacy);
            Check(File.ReadAllBytes(settings).SequenceEqual(before), "existing settings preserved");
            Check(File.ReadAllText(retained) == "retained-model-marker", "existing model directory preserved");
            Check(File.ReadAllText(weatherCache) == "weather-retention-marker", "existing weather cache preserved by update");
            foreach (var entry in archive.Entries.Where(e => e.Name.EndsWith(".dll") || e.Name.EndsWith(".exe")))
            {
                using var input = entry.Open();
                using var output = File.OpenRead(Path.Combine(target, entry.FullName));
                Check(SHA256.HashData(input).SequenceEqual(SHA256.HashData(output)), "extracted binary matches " + entry.FullName);
            }
            Check(File.Exists(Path.Combine(target, "WinRT.Runtime.dll")) == !InstallerForm.RequiresVision,
                "mode switch removes OCR-only projections");
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(target, "installation-manifest.json")));
            Check(manifest.RootElement.GetProperty("Version").GetString() == expectedVersion, "installed manifest version matches");
            Check(manifest.RootElement.GetProperty("Edition").GetString() == InstallerForm.Edition, "installed manifest edition matches");
            foreach (var component in new[] { "BigBlueFish.Mcp.exe", "BigBlueFish.Mcp.dll", "BigBlueFish.Control.dll" })
                Check(File.Exists(Path.Combine(target, component)) == mcpIncluded, "installed MCP component matches edition: " + component);
            Check(manifest.RootElement.GetProperty("InputMode").GetString() == InstallerForm.InputMode,
                "installed manifest mode matches");
            using var runtime = JsonDocument.Parse(File.ReadAllText(Path.Combine(target, "BigBlueFish.runtimeconfig.json")));
            Check(runtime.RootElement.GetProperty("runtimeOptions").TryGetProperty("includedFrameworks", out _),
                "application is self-contained");
            if (args.Contains("--lifecycle"))
            {
                var folder = new DirectoryInfo(target);
                var parentAcl = folder.Parent!.GetAccessControl().GetSecurityDescriptorBinaryForm();
                var acl = new DirectorySecurity();
                acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
                acl.SetOwner(admins);
                foreach (var sid in new[] { admins, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
                    acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                acl.AddAccessRule(new FileSystemAccessRule(users, FileSystemRights.ReadAndExecute, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                folder.SetAccessControl(acl);
                var protectedData = new FileSecurity();
                protectedData.SetAccessRuleProtection(true, false);
                protectedData.AddAccessRule(new FileSystemAccessRule(admins, FileSystemRights.FullControl, AccessControlType.Allow));
                protectedData.AddAccessRule(new FileSystemAccessRule(users, FileSystemRights.Read, AccessControlType.Allow));
                new FileInfo(retained).SetAccessControl(protectedData);
                Check(RestrictedUserProbe.Run(target) == 23, "ordinary token cannot write before installer ACL grant");
                string ownShortcut = target + "-own.lnk", unrelatedShortcut = target + "-other.lnk";
                string uninstallShortcut = target + "-uninstall.lnk";
                var createShortcut = typeof(InstallerForm).GetMethod("CreateShortcut", BindingFlags.Static | BindingFlags.NonPublic)!;
                createShortcut.Invoke(null, [ownShortcut, target, "BigBlueFish.exe", ""]);
                createShortcut.Invoke(null, [unrelatedShortcut, target + "-other", "BigBlueFish.exe", ""]);
                createShortcut.Invoke(null, [uninstallShortcut, target, "Uninstall.exe", $"--uninstall \"{target}\""]);
                dynamic shortcutShell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
                foreach (var pair in new[] { (ownShortcut, "BigBlueFish.exe"), (uninstallShortcut, "Uninstall.exe") })
                {
                    dynamic shortcut = shortcutShell.CreateShortcut(pair.Item1);
                    Check(((string)shortcut.IconLocation).Equals(Path.Combine(target, pair.Item2) + ",0", StringComparison.OrdinalIgnoreCase),
                        "shortcut uses embedded executable icon: " + pair.Item2);
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
                    using var icon = System.Drawing.Icon.ExtractAssociatedIcon(Path.Combine(target, pair.Item2));
                    Check(icon != null && icon.Width > 0, "Windows extracts embedded shortcut icon: " + pair.Item2);
                }
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcutShell);
                string startupName = "BigBlueFish-Verification-" + Guid.NewGuid().ToString("N");
                using (var startup = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                    startup.SetValue(startupName, $"\"{legacyPath}\" --example");
                InstallationLifecycle.Register(target, ownShortcut, unrelatedShortcut, uninstallShortcut);
                using (var startup = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                    Check(startup?.GetValue(startupName) as string == $"\"{newPath}\" --example", "registration migrates real Windows startup entry");
                InstallationLifecycle.GrantUserAccess(target);
                Check(RestrictedUserProbe.Run(target) == 0, "ordinary token creates modifies renames and deletes data after installer ACL grant");
                Check(folder.Parent.GetAccessControl().GetSecurityDescriptorBinaryForm().SequenceEqual(parentAcl), "installer leaves parent directory ACL unchanged");
                using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using (var application = machine.OpenSubKey(InstallationLifecycle.ApplicationPathKey))
                    Check(application?.GetValue("") as string == Path.Combine(target, "BigBlueFish.exe"), "application registered in Windows App Paths");
                string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + InstallationLifecycle.RegistrationName(target);
                using (var key = machine.OpenSubKey(keyPath))
                {
                    Check(key?.GetValue("DisplayVersion") as string == expectedVersion, "Windows uninstall registration has current version");
                    Check((key?.GetValue("UninstallString") as string)?.Contains("Uninstall.exe\" --uninstall") == true, "Windows uninstall command points to installed uninstaller");
                }
                Check(File.Exists(Path.Combine(target, "Uninstall.exe")), "standalone uninstaller installed");
                Check(FileVersionInfo.GetVersionInfo(Path.Combine(target, "Uninstall.exe")).ProductVersion == expectedVersion, "independent uninstaller version matches project");
                VerifyUninstallerEntry(target, explicitArguments: false);
                VerifyUninstallerEntry(target, explicitArguments: true);
                VerifyUninstallWorker(target, removeData: false);
                Check(!File.Exists(ownShortcut) && File.Exists(unrelatedShortcut), "uninstall removes own shortcut and preserves unrelated shortcut");
                Check(!File.Exists(uninstallShortcut), "uninstall removes its own Start menu shortcut");
                File.Delete(unrelatedShortcut);
                using (var startup = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                    Check(startup?.GetValue(startupName) == null, "uninstall clears this install's startup entry");
                Check(!File.Exists(Path.Combine(target, "BigBlueFish.exe")) && !File.Exists(Path.Combine(target, "Uninstall.exe")), "uninstall removes application and uninstaller");
                Check(File.ReadAllBytes(settings).SequenceEqual(before) && File.Exists(retained), "uninstall can preserve model and settings");
                Check(File.ReadAllText(weatherCache) == "weather-retention-marker", "retain-data uninstall preserves weather cache");
                using (var key = machine.OpenSubKey(keyPath)) Check(key == null, "uninstall removes Windows registration");
                using (var application = machine.OpenSubKey(InstallationLifecycle.ApplicationPathKey)) Check(application == null, "uninstall removes its own Windows application path");
                // Reinstall the same mode over retained data, then exercise complete removal.
                InstallerForm.ExtractApplication(archive, target);
                InstallationLifecycle.Register(target);
                VerifyUninstallWorker(target, removeData: true);
                Check(!Directory.Exists(target), "full uninstall removes app directory including model and settings");
                foreach (var prohibited in new[] { Path.GetPathRoot(target)!, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"D:\Program Files" })
                {
                    bool rejected = false;
                    try { InstallationLifecycle.ValidateDirectory(prohibited); } catch (IOException) { rejected = true; }
                    Check(rejected, "installer rejects changing ACL on parent directory " + prohibited);
                }
            }
            Console.WriteLine($"RESULT {count} installer checks passed ({InstallerForm.InputMode})");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
