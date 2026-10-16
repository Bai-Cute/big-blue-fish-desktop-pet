using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using BigBlueFish.Deployment;
using BigBlueFish.Setup;

internal static class UpdateVerification
{
    static int checks;
    static void Check(bool value, string description) { if (!value) throw new Exception(description); checks++; Console.WriteLine("PASS " + description); }
    static void Wait(Func<bool> condition, int seconds, string description)
    {
        var watch = Stopwatch.StartNew(); while (!condition() && watch.Elapsed.TotalSeconds < seconds) Thread.Sleep(100); Check(condition(), description);
    }
    static Process? FindApp(string target) => Process.GetProcessesByName("BigBlueFish").FirstOrDefault(p =>
    { try { return string.Equals(p.MainModule?.FileName, Path.Combine(target, "BigBlueFish.exe"), StringComparison.OrdinalIgnoreCase); } catch { return false; } });
    internal static int Run(string setup, string oldSetup, string target)
    {
        setup = Path.GetFullPath(setup); oldSetup = Path.GetFullPath(oldSetup); target = InstallationLifecycle.ValidateDirectory(target);
        if (Directory.Exists(target)) throw new IOException("Update integration fixture requires a fresh directory.");
        Process? previous = null; Process? updater = null; Process? current = null;
        try
        {
            Directory.CreateDirectory(target);
            using (var oldStream = InstallerForm.OpenPayload(oldSetup))
            using (var archive = new ZipArchive(oldStream, ZipArchiveMode.Read))
            {
                archive.ExtractToDirectory(target);
                File.WriteAllText(Path.Combine(target, "installation-manifest.json"), JsonSerializer.Serialize(new
                    { Version = "0.4.2", InputMode = "OCR", Files = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).Select(e => e.FullName).ToArray() }));
            }
            Directory.CreateDirectory(Path.Combine(target, "models"));
            File.WriteAllText(Path.Combine(target, "models", "retention-marker.txt"), "existing model data");
            File.WriteAllText(Path.Combine(target, "preferences.json"), "{\"ModelEnabled\":false,\"PublicInfo\":false,\"CheckUpdatesOnStartup\":false,\"WeatherRegionCode\":\"340104\",\"Scale\":0.8}");
            ModelStore.Save(target, new ModelStore.Selection("none", null, null));
            InstallationLifecycle.Register(target);
            var start = new ProcessStartInfo(Path.Combine(target, "BigBlueFish.exe")) { WorkingDirectory = target, UseShellExecute = false };
            start.ArgumentList.Add("--test-mode"); start.Environment["DOTNET_ROOT"] = Path.Combine(target, "absent-system-runtime"); start.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
            previous = Process.Start(start)!;
            Wait(() => File.Exists(Path.Combine(target, "ready.status")), 20, "real 0.4.2 self-contained installation starts before update");
            var run = new ProcessStartInfo(setup) { UseShellExecute = false, WorkingDirectory = target };
            run.ArgumentList.Add("--update"); run.ArgumentList.Add(target); run.ArgumentList.Add("--wait-pid"); run.ArgumentList.Add(previous.Id.ToString());
            run.Environment["DOTNET_ROOT"] = Path.Combine(target, "absent-system-runtime"); run.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
            updater = Process.Start(run)!;
            Thread.Sleep(1000);
            Check(!updater.HasExited && !previous.HasExited && FileVersionInfo.GetVersionInfo(Path.Combine(target, "BigBlueFish.exe")).ProductVersion == "0.4.2", "actual update installer waits instead of replacing a running old app");
            File.WriteAllText(Path.Combine(target, "test-command.txt"), "exit");
            Check(previous.WaitForExit(12000), "old production app completes farewell and exits for update");
            var preferences = File.ReadAllBytes(Path.Combine(target, "preferences.json"));
            var selection = File.ReadAllBytes(Path.Combine(target, "model-selection.json"));
            Check(updater.WaitForExit(45000) && updater.ExitCode == 0, "actual self-contained WPF updater finishes and exits");
            current = FindApp(target);
            Check(current != null && !current.HasExited && File.Exists(Path.Combine(target, "ready.status")), "actual update restarts the new production app successfully");
            Check(FileVersionInfo.GetVersionInfo(Path.Combine(target, "BigBlueFish.exe")).ProductVersion == "0.5.0", "running update files have 0.5.0 product version");
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(target, "installation-manifest.json")));
            Check(manifest.RootElement.GetProperty("Version").GetString() == "0.5.0", "actual update manifest aligns with installed version");
            Check(preferences.SequenceEqual(File.ReadAllBytes(Path.Combine(target, "preferences.json"))) && selection.SequenceEqual(File.ReadAllBytes(Path.Combine(target, "model-selection.json"))) && File.ReadAllText(Path.Combine(target, "models", "retention-marker.txt")) == "existing model data", "actual update preserves original settings, no-model choice and model data");
            Check(!Directory.Exists(Path.Combine(target, ".update-previous")) && !File.Exists(Path.Combine(target, ".update-state.json")), "actual update removes temporary prior program only after new startup verification");
            var clr = current!.Modules.Cast<ProcessModule>().Single(m => m.ModuleName.Equals("coreclr.dll", StringComparison.OrdinalIgnoreCase));
            Check(clr.FileName.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "new app loads its own packaged .NET rather than system runtime");
            current.Kill(true); current.WaitForExit(5000); current.Dispose(); current = null;
            var hash = SHA256.HashData(File.ReadAllBytes(Path.Combine(target, "BigBlueFish.exe")));
            using var stream = InstallerForm.OpenPayload(setup); using var next = new ZipArchive(stream, ZipArchiveMode.Read);
            bool failed = false;
            try
            {
                UpdateTransaction.Apply(target, next, () =>
                {
                    InstallerForm.ExtractApplication(next, target);
                    File.WriteAllText(Path.Combine(target, "BigBlueFish.exe"), "injected invalid executable");
                }, () => InstallerForm.LaunchAndVerify(target));
            }
            catch { failed = true; }
            Check(failed && SHA256.HashData(File.ReadAllBytes(Path.Combine(target, "BigBlueFish.exe"))).SequenceEqual(hash), "failed actual process startup rolls program executable back completely");
            Check(ModelStore.ReadSelection(target)?.Source == "none", "failed startup restoration also preserves no-model choice");
            Console.WriteLine($"RESULT {checks} actual packaged upgrade and startup-recovery checks passed"); return 0;
        }
        finally
        {
            foreach (var process in new[] { previous, updater, current })
                if (process != null) { try { if (!process.HasExited) { process.Kill(true); process.WaitForExit(5000); } } catch { } process.Dispose(); }
            using var remaining = FindApp(target); if (remaining != null) { remaining.Kill(true); remaining.WaitForExit(5000); }
            if (File.Exists(Path.Combine(target, "uninstall-info.json"))) InstallationLifecycle.Uninstall(target, true);
        }
    }
}
