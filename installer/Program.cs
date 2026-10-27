using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using BigBlueFish.Deployment;
using Microsoft.Win32;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using ProgressBar = System.Windows.Controls.ProgressBar;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace BigBlueFish.Setup;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var app = ModernUI.EnsureApplication();
        app.ShutdownMode = ShutdownMode.OnMainWindowClose;
        app.Run(new InstallerForm(args));
        return 0;
    }
}

internal sealed class InstallerForm : ModernWindow
{
#if COMPANION_OCR
    internal const bool RequiresVision = false;
    internal const string InputMode = "OCR";
#else
    internal const bool RequiresVision = true;
    internal const string InputMode = "Vision";
#endif
    internal static string ProductVersion => typeof(InstallerForm).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
    private const string ManifestName = "installation-manifest.json";
    private sealed record InstalledManifest(string Version, string InputMode, string[] Files, string Edition = "Standard");
#if COMPANION_SAFE
    internal const string Edition = "Safe";
    private const string InstallerTitle = "蓝色大肥鱼无MCP版安装程序";
#else
    internal const string Edition = "Standard";
    private const string InstallerTitle = "蓝色大肥鱼安装程序";
#endif
    private static readonly byte[] PayloadMarker = Encoding.ASCII.GetBytes("BIGBLUEFISH_PAYLOAD_V1");
    private readonly TextBox destination = new();
    private readonly TextBlock status = ModernUI.Text("");
    private readonly ProgressBar progress = new();
    private readonly Button install;
    private readonly bool updating;
    private readonly int waitPid;
    private bool working;

    internal InstallerForm(string[]? arguments = null) : base(InstallerTitle, "让鱼鱼住进你的桌面", "安装桌宠、动画资源和本地运行环境。", 650, 550)
    {
        var args = arguments ?? [];
        updating = args.Length >= 2 && args[0] == "--update";
        int waitIndex = Array.IndexOf(args, "--wait-pid");
        if (waitIndex >= 0 && waitIndex + 1 < args.Length) int.TryParse(args[waitIndex + 1], out waitPid);
        destination.Text = updating ? Path.GetFullPath(args[1]) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "蓝色大肥鱼");
        destination.IsReadOnly = updating;
        Body.Children.Add(ModernUI.Text("安装位置")); Body.Children.Add(destination);
        var browse = ModernUI.Button("选择文件夹", () =>
        {
            var picker = new OpenFolderDialog { Title = "选择安装位置", InitialDirectory = Path.GetDirectoryName(destination.Text) };
            if (picker.ShowDialog(this) == true) destination.Text = Path.Combine(picker.FolderName, "蓝色大肥鱼");
        });
        browse.IsEnabled = !updating; Body.Children.Add(browse);
        Body.Children.Add(progress); Body.Children.Add(status);
        install = ModernUI.Button(updating ? "更新蓝色大肥鱼" : "安装蓝色大肥鱼", async () => await InstallClickedAsync(), true);
        install.HorizontalAlignment = HorizontalAlignment.Right; Actions.Children.Add(install);
        Closing += (_, e) => { if (working) e.Cancel = true; };
        if (updating) Loaded += async (_, _) => await InstallClickedAsync();
    }

    private async Task InstallClickedAsync()
    {
        if (working) return;
        working = true; install.IsEnabled = false; destination.IsEnabled = false;
        string? installedTarget = null;
        bool applicationStarted = false;
        System.Threading.Mutex? installLock = null;
        bool ownsLock = false;
        try
        {
            var target = InstallationLifecycle.ValidateDirectory(destination.Text.Trim());
            installedTarget = target;
            var identity = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(target.ToUpperInvariant())))[..24];
            installLock = new System.Threading.Mutex(false, "Local\\BigBlueFish.Install." + identity);
            try { ownsLock = installLock.WaitOne(0); } catch (System.Threading.AbandonedMutexException) { ownsLock = true; }
            if (!ownsLock) throw new IOException("此目录已有一个安装程序正在工作，请等待它完成。");
            if (updating)
            {
                status.Text = "正在等待桌宠退出…";
                if (waitPid > 0)
                {
                    try { using var previous = Process.GetProcessById(waitPid); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40)); await previous.WaitForExitAsync(timeout.Token); }
                    catch (ArgumentException) { }
                }
            }
            EnsureNotRunning(target);
            status.Text = "正在安装程序文件…"; progress.IsIndeterminate = true;
            await Task.Run(() =>
            {
                UpdateTransaction.Recover(target);
                using var stream = OpenPayload(); using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                if (File.Exists(Path.Combine(target, ManifestName)))
                    UpdateTransaction.Apply(target, archive, () => { ExtractApplication(archive, target); FinishInstallation(target, Environment.ProcessPath!); },
                        updating ? () => { LaunchAndVerify(target); applicationStarted = true; } : null);
                else { ExtractApplication(archive, target); FinishInstallation(target, Environment.ProcessPath!); }
            });
            progress.IsIndeterminate = false;
            if (!updating && ModelStore.Resolve(ApplicationData.DirectoryFor(target)) == null && ModelStore.ReadSelection(ApplicationData.DirectoryFor(target))?.Source != "none")
            {
                var model = new ModelInstallWindow(ApplicationData.DirectoryFor(target)) { Owner = this };
                model.ShowDialog();
                if (!model.Completed) ModelStore.Save(ApplicationData.DirectoryFor(target), new ModelStore.Selection("none", null, null));
            }
            status.Text = "安装完成，正在启动蓝色大肥鱼…"; progress.Value = 100;
            if (!applicationStarted) Launch(target);
            working = false; Close();
        }
        catch (Exception error)
        {
            if (updating && ownsLock && installedTarget != null && File.Exists(Path.Combine(installedTarget, "BigBlueFish.exe")))
            {
                try { EnsureNotRunning(installedTarget); Launch(installedTarget); } catch { }
            }
            status.Text = "安装未完成，可以重试。";
            ModernUI.Error(this, "安装未完成", error);
        }
        finally
        {
            if (ownsLock) installLock!.ReleaseMutex();
            installLock?.Dispose();
            working = false; install.IsEnabled = true; destination.IsEnabled = !updating; progress.IsIndeterminate = false;
        }
    }

    private static Process Launch(string target) => Process.Start(new ProcessStartInfo(Path.Combine(target, "BigBlueFish.exe"))
        { WorkingDirectory = target, UseShellExecute = true }) ?? throw new IOException("未能启动蓝色大肥鱼。");

    internal static void LaunchAndVerify(string target)
    {
        var ready = Path.Combine(ApplicationData.DirectoryFor(target), "ready.status");
        if (File.Exists(ready)) File.Delete(ready);
        using var process = Launch(target);
        try
        {
            var watch = Stopwatch.StartNew();
            while (!process.HasExited && !File.Exists(ready) && watch.Elapsed < TimeSpan.FromSeconds(30)) System.Threading.Thread.Sleep(100);
            if (process.HasExited || !File.Exists(ready)) throw new IOException("新版本未能正常启动，正在恢复原版本。");
            System.Threading.Thread.Sleep(1000);
            if (process.HasExited) throw new IOException("新版本启动后退出，正在恢复原版本。");
        }
        catch
        {
            if (!process.HasExited) { process.Kill(true); process.WaitForExit(5000); }
            throw;
        }
    }

    private static void EnsureNotRunning(string target)
    {
        foreach (var process in Process.GetProcessesByName("BigBlueFish"))
        {
            using (process)
            {
                try { if (string.Equals(process.MainModule?.FileName, Path.Combine(target, "BigBlueFish.exe"), StringComparison.OrdinalIgnoreCase))
                        throw new IOException("请先退出此目录中正在运行的蓝色大肥鱼，再继续安装。"); }
                catch (System.ComponentModel.Win32Exception) { }
            }
        }
    }

    internal static void FinishInstallation(string target, string installerPath)
    {
        var desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "蓝色大肥鱼.lnk");
        var group = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "蓝色大肥鱼");
        var startMenu = Path.Combine(group, "蓝色大肥鱼.lnk");
        var uninstallMenu = Path.Combine(group, "卸载蓝色大肥鱼.lnk");
        Directory.CreateDirectory(group);
        var legacyStartMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "蓝色大肥鱼.lnk");
        InstallationLifecycle.Register(target, desktop, startMenu, uninstallMenu, legacyStartMenu);
        CreateShortcut(desktop, target);
        CreateShortcut(startMenu, target);
        CreateShortcut(uninstallMenu, target, InstallationLifecycle.UninstallerName, $"--uninstall \"{target}\"");
        ApplicationData.Prepare(target);
        InstallationLifecycle.GrantUserAccess(target);
    }

    // The manifest owns application files only. Settings, models and caches survive
    // both upgrades and mode changes; files exclusive to an old payload are removed.
    internal static void ExtractApplication(ZipArchive archive, string target)
    {
        target = Path.GetFullPath(target);
        Directory.CreateDirectory(target);
        string Resolve(string relative)
        {
            var path = Path.GetFullPath(Path.Combine(target, relative));
            if (!path.StartsWith(target.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("安装包包含无效路径。");
            return path;
        }
        static bool Preserved(string relative) => ApplicationData.IsDataFile(relative);
        var manifestPath = Path.Combine(target, ManifestName);
        var previous = File.Exists(manifestPath)
            ? JsonSerializer.Deserialize<InstalledManifest>(File.ReadAllText(manifestPath))?.Files ?? [] : [];
        var entries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToArray();
        var current = new HashSet<string>(entries.Select(e => e.FullName.Replace('\\', '/')),
            StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (Preserved(entry.FullName)) continue;
            var path = Resolve(entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            entry.ExtractToFile(path, overwrite: true);
        }
        // Older installers had no manifest. These are the known OCR-only runtime
        // projections and the old generated installation instructions.
        foreach (var relative in previous.Concat(new[] {
            "Microsoft.Windows.SDK.NET.dll", "WinRT.Runtime.dll", "INSTALL-README.txt", "Setup-Model.ps1",
            "VPet-Simulator.Windows.exe", "VPet-Simulator.Windows.dll", "VPet-Simulator.Windows.deps.json",
            "VPet-Simulator.Windows.runtimeconfig.json", "VPet-Simulator.Windows.pdb", "vpeticon.ico" }))
        {
            if (current.Contains(relative) || Preserved(relative)) continue;
            var path = Resolve(relative);
            if (File.Exists(path)) File.Delete(path);
        }
        var manifest = new InstalledManifest(ProductVersion, InputMode,
            current.Where(p => !Preserved(p)).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray(), Edition);
        File.WriteAllText(manifestPath + ".partial", JsonSerializer.Serialize(manifest));
        File.Move(manifestPath + ".partial", manifestPath, overwrite: true);
    }

    internal static Stream OpenPayload(string? installerPath = null)
    {
        var self = installerPath ?? Environment.ProcessPath ?? throw new InvalidOperationException("无法定位安装程序。");
        var stream = new FileStream(self, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length < 8 + PayloadMarker.Length) throw new InvalidDataException("安装程序不完整。");
        stream.Seek(-8, SeekOrigin.End);
        Span<byte> lengthBytes = stackalloc byte[8];
        stream.ReadExactly(lengthBytes);
        var length = BitConverter.ToInt64(lengthBytes);
        var markerOffset = stream.Length - 8 - PayloadMarker.Length;
        var payloadOffset = markerOffset - length;
        if (length <= 0 || markerOffset < 0 || payloadOffset < 0) throw new InvalidDataException("安装程序数据不完整。");
        stream.Seek(markerOffset, SeekOrigin.Begin);
        var marker = new byte[PayloadMarker.Length];
        stream.ReadExactly(marker);
        if (!marker.AsSpan().SequenceEqual(PayloadMarker)) throw new InvalidDataException("安装程序数据标记不匹配。");
        stream.Seek(payloadOffset, SeekOrigin.Begin);
        return new LimitedStream(stream, length);
    }

    private static void CreateShortcut(string shortcutPath, string target, string executableName = "BigBlueFish.exe", string arguments = "")
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = Path.Combine(target, executableName);
            shortcut.Arguments = arguments;
            shortcut.WorkingDirectory = target;
            shortcut.IconLocation = "\"" + Path.Combine(target, "BigBlueFish.ico") + "\",0";
            shortcut.Description = "蓝色大肥鱼桌宠";
            shortcut.Save();
        }
        catch
        {
            // A shortcut is convenient, but it must not make an otherwise complete install fail.
        }
    }

    private sealed class LimitedStream(Stream inner, long remaining) : Stream
    {
        private readonly long length = remaining;
        private long left = remaining;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => length - left; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            if (left == 0) return 0;
            var take = (int)Math.Min(buffer.Length, left);
            var n = inner.Read(buffer[..take]);
            left -= n;
            return n;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (left == 0) return 0;
            var take = (int)Math.Min(buffer.Length, left);
            var n = await inner.ReadAsync(buffer[..take], cancellationToken);
            left -= n;
            return n;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Flush() { }
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }
}
