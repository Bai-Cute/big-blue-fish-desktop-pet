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
            var ok = dialog.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
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
            if (args.Length == 2 && args[0] == "--entry-only")
            {
                VerifyUninstallerEntry(Path.GetFullPath(args[1]), explicitArguments: false);
                VerifyUninstallerEntry(Path.GetFullPath(args[1]), explicitArguments: true);
                Console.WriteLine($"RESULT {count} actual installed entry checks passed");
                return 0;
            }
            if (args.Length >= 2 && args[0] == "--ui-layout")
            {
                bool systemDpi = args.Contains("--system-dpi");
                Application.SetHighDpiMode(systemDpi ? HighDpiMode.PerMonitorV2 : HighDpiMode.DpiUnaware);
                Application.EnableVisualStyles();
                foreach (float scale in systemDpi ? new[] { 1f } : new[] { 1f, 2f })
                {
                    using var form = UninstallApplication.CreateUninstallForm(Path.GetTempPath());
                    form.Show();
                    Application.DoEvents();
                    form.Scale(new SizeF(scale, scale));
                    form.PerformLayout();
                    Console.WriteLine($"UI actual device DPI: {form.DeviceDpi}");
                    var table = (TableLayoutPanel)form.Controls[0];
                    table.PerformLayout();
                    var label = table.Controls.OfType<Label>().Single();
                    var checkbox = table.Controls.OfType<CheckBox>().Single();
                    var button = table.Controls.OfType<Button>().Single();
                    Check(label.Visible && checkbox.Visible && button.Visible, $"uninstall controls are actually visible at {scale * 100}% scale");
                    Check(!checkbox.Checked, $"retain-data checkbox unchecked at {scale * 100}% scale");
                    Check(label.Bottom <= checkbox.Top && checkbox.Bottom <= button.Top,
                        $"uninstall label checkbox and button do not overlap at {scale * 100}% scale");
                    Check(table.ClientRectangle.Contains(button.Bounds) && table.ClientRectangle.Contains(checkbox.Bounds),
                        $"uninstall controls fit inside the layout at {scale * 100}% scale");
                    using var bitmap = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    bitmap.Save(args[1] + $"-{scale * 100}.png");
                }
                Console.WriteLine($"RESULT {count} uninstall UI checks passed");
                return 0;
            }
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
            using var versionStream = typeof(Checks).Assembly.GetManifestResourceStream(typeof(Checks).Assembly.GetManifestResourceNames().Single(n => n.EndsWith("Version.props")))!;
            string expectedVersion = XDocument.Load(versionStream).Descendants("Version").Single().Value;
            Check(InstallerForm.ProductVersion == expectedVersion, "installer product version matches Version.props");
            VerifyInstallerEntry(args[0]);
            using var stream = InstallerForm.OpenPayload(args[0]);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var names = archive.Entries.Select(e => e.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var required in new[] { "VPet-Simulator.Windows.exe", "Uninstall.exe", "VPet-Simulator.Windows.dll",
                "VPet-Simulator.Core.dll", "VPet-Simulator.Windows.Interface.dll", "coreclr.dll", "hostfxr.dll",
                "PresentationFramework.dll", "runtime/llama-server.exe", "runtime/ggml-vulkan.dll", "runtime/mtmd.dll",
                "media/ffmpeg.exe", "media/FFmpeg-LICENSE.txt", "assets/fish/animations.json", "assets/fish/LICENSE.txt" })
                Check(names.Contains(required), "payload includes " + required);
            foreach (var required in new[] { "runtime/msvcp140.dll", "runtime/vcruntime140.dll", "runtime/vcruntime140_1.dll", "runtime/visual-cpp-runtime.json", "licenses/Microsoft-Visual-Cpp-Runtime.txt" })
                Check(names.Contains(required), "payload includes native runtime dependency " + required);
            Check(names.Count(n => n.StartsWith("assets/fish/") && n.EndsWith(".webm")) == 106, "payload includes all 106 animations");
            Check(!names.Any(n => n.StartsWith("assets/pet/")), "old PNG animation pack is absent");
            Check(names.Contains("WinRT.Runtime.dll") == !InstallerForm.RequiresVision, "OCR projections match input mode");
            InstallerForm.ExtractApplication(archive, target);
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
            Check(manifest.RootElement.GetProperty("InputMode").GetString() == InstallerForm.InputMode,
                "installed manifest mode matches");
            using var runtime = JsonDocument.Parse(File.ReadAllText(Path.Combine(target, "VPet-Simulator.Windows.runtimeconfig.json")));
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
                createShortcut.Invoke(null, [ownShortcut, target, "VPet-Simulator.Windows.exe", ""]);
                createShortcut.Invoke(null, [unrelatedShortcut, target + "-other", "VPet-Simulator.Windows.exe", ""]);
                createShortcut.Invoke(null, [uninstallShortcut, target, "Uninstall.exe", $"--uninstall \"{target}\""]);
                string startupName = "BigBlueFish-Verification-" + Guid.NewGuid().ToString("N");
                using (var startup = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                    startup.SetValue(startupName, $"\"{Path.Combine(target, "VPet-Simulator.Windows.exe")}\"");
                InstallationLifecycle.Register(target, ownShortcut, unrelatedShortcut, uninstallShortcut);
                InstallationLifecycle.GrantUserAccess(target);
                Check(RestrictedUserProbe.Run(target) == 0, "ordinary token creates modifies renames and deletes data after installer ACL grant");
                Check(folder.Parent.GetAccessControl().GetSecurityDescriptorBinaryForm().SequenceEqual(parentAcl), "installer leaves parent directory ACL unchanged");
                using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using (var application = machine.OpenSubKey(InstallationLifecycle.ApplicationPathKey))
                    Check(application?.GetValue("") as string == Path.Combine(target, "VPet-Simulator.Windows.exe"), "application registered in Windows App Paths");
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
                Check(!File.Exists(Path.Combine(target, "VPet-Simulator.Windows.exe")) && !File.Exists(Path.Combine(target, "Uninstall.exe")), "uninstall removes application and uninstaller");
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
