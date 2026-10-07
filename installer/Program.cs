using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Drawing;
using System.Reflection;
using System.Text.Json;

namespace BigBlueFish.Setup;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new InstallerForm());
        return 0;
    }
}

internal sealed class InstallerForm : Form
{
#if COMPANION_OCR
    internal const bool RequiresVision = false;
    internal const string InputMode = "OCR";
    private const string ModeLabel = "OCR 版";
#else
    internal const bool RequiresVision = true;
    internal const string InputMode = "Vision";
    private const string ModeLabel = "视觉版";
#endif
    internal static string ProductVersion => typeof(InstallerForm).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
    private const string ManifestName = "installation-manifest.json";
    private sealed record InstalledManifest(string Version, string InputMode, string[] Files);
    private const string ModelName = "Qwen3.5-4B-heretic-Q4_K_M.gguf";
    private const string ModelUrl = "https://huggingface.co/Biomanticus/Qwen3.5-4B-heretic-gguf/resolve/b63ff4662e4863cfa005c9cd8ed34b87ed44b7e1/Qwen3.5-4B-heretic-f16_Q4_K_M.gguf?download=true";
    private const string ModelSha256 = "8485535a36c9f333574d08b650ad698ac02ec30752bd9cd87e493a3b7531bee1";
    private const string MmprojName = "Qwen3.5-4B-heretic.mmproj-f16.gguf";
    private const string MmprojUrl = "https://huggingface.co/mradermacher/Qwen3.5-4B-heretic-GGUF/resolve/0d92f575bfcb057411f3d4088c5eabed979a9b3f/Qwen3.5-4B-heretic.mmproj-f16.gguf?download=true";
    private const string MmprojSha256 = "E638DC8DE3B75309A190092BA006307759343B62AE0D21ED8359DF76B9B76C3B";
    private static readonly byte[] PayloadMarker = Encoding.ASCII.GetBytes("BIGBLUEFISH_PAYLOAD_V1");

    private readonly TextBox destination = new();
    private readonly Button install = new();
    private readonly ProgressBar progress = new();
    private readonly Label status = new();
    private readonly LinkLabel source = new();

    public InstallerForm()
    {
        Text = $"蓝色大肥鱼 · {ProductVersion} {ModeLabel}安装程序";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Microsoft YaHei UI", 10F);
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(660, 480);
        MinimumSize = new Size(676, 0);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 9,
            Padding = new Padding(32, 26, 32, 26)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        for (var row = 0; row < layout.RowCount; row++)
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);

        var title = new Label
        {
            Text = $"安装蓝色大肥鱼 · {ModeLabel}",
            Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12)
        };
        layout.Controls.Add(title, 0, 0);

        var intro = new Label
        {
            Text = RequiresVision
                ? "这只可爱的桌宠会把程序、本地 4B 模型和视觉投影组件安装到电脑中。安装过程中需要从 Hugging Face 联网下载模型与视觉组件。"
                : "这只可爱的桌宠会把程序和本地 4B 模型安装到电脑中，使用 Windows 的本地文字识别组件读取当前窗口。安装过程中需要从 Hugging Face 联网下载模型。",
            AutoSize = true,
            MaximumSize = new Size(596, 0),
            Margin = new Padding(0, 0, 0, 22)
        };
        layout.Controls.Add(intro, 0, 1);

        var folderLabel = new Label { Text = "安装位置", AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
        layout.Controls.Add(folderLabel, 0, 2);
        var folderRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 22)
        };
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        folderRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        destination.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "蓝色大肥鱼");
        destination.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        destination.Margin = new Padding(0, 0, 12, 0);
        folderRow.Controls.Add(destination, 0, 0);
        var browse = new Button
        {
            Text = "浏览…", AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(90, 40),
            Padding = new Padding(12, 4, 12, 4),
            Margin = Padding.Empty
        };
        browse.Click += (_, _) => ChooseFolder();
        folderRow.Controls.Add(browse, 1, 0);
        layout.Controls.Add(folderRow, 0, 3);

        var model = new Label
        {
            Text = RequiresVision
                ? "模型约 2.7 GB，视觉投影组件约 640 MB。已有文件校验通过后可以继续使用；下载完成后会自动校验文件并继续安装。"
                : "模型约 2.7 GB。已有模型校验通过后可以继续使用；下载完成后会自动校验文件并继续安装。",
            AutoSize = true,
            MaximumSize = new Size(596, 0),
            Margin = new Padding(0, 0, 0, 10)
        };
        layout.Controls.Add(model, 0, 4);
        source.Text = RequiresVision ? "模型与视觉组件下载源：Hugging Face（点击打开）"
            : "模型下载源：Hugging Face（点击打开）";
        source.AutoSize = true;
        source.LinkColor = Color.FromArgb(55, 105, 170);
        source.MaximumSize = new Size(596, 0);
        source.Margin = new Padding(0, 0, 0, 24);
        source.Click += (_, _) => Process.Start(new ProcessStartInfo(ModelUrl) { UseShellExecute = true });
        layout.Controls.Add(source, 0, 5);

        progress.Dock = DockStyle.Fill;
        progress.Height = 23;
        progress.Margin = new Padding(0, 0, 0, 12);
        progress.Style = ProgressBarStyle.Continuous;
        layout.Controls.Add(progress, 0, 6);
        status.Text = "准备安装";
        status.AutoSize = true;
        status.MaximumSize = new Size(596, 0);
        status.Margin = new Padding(0, 0, 0, 20);
        layout.Controls.Add(status, 0, 7);

        install.Text = "开始安装";
        install.AutoSize = true;
        install.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        install.MinimumSize = new Size(130, 42);
        install.Padding = new Padding(18, 6, 18, 6);
        install.Margin = Padding.Empty;
        install.Anchor = AnchorStyles.Right;
        install.Click += async (_, _) => await InstallClickedAsync();
        layout.Controls.Add(install, 0, 8);
        layout.Layout += (_, _) =>
        {
            var width = Math.Max(1, layout.ClientSize.Width - layout.Padding.Horizontal);
            foreach (var label in new Label[] { title, intro, model, source, status })
                if (label.MaximumSize.Width != width)
                    label.MaximumSize = new Size(width, 0);
        };
    }

    private void ChooseFolder()
    {
        using var dialog = new FolderBrowserDialog { SelectedPath = destination.Text };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            destination.Text = Path.Combine(dialog.SelectedPath, "蓝色大肥鱼");
    }

    private async Task InstallClickedAsync()
    {
        var target = destination.Text.Trim();
        if (target.Length == 0)
        {
            MessageBox.Show(this, "请选择安装位置。", "蓝色大肥鱼", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        install.Enabled = false;
        destination.Enabled = false;
        try
        {
            await InstallAsync(target);
            status.Text = "安装完成，正在启动蓝色大肥鱼…";
            var exe = Path.Combine(target, "VPet-Simulator.Windows.exe");
            Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = target, UseShellExecute = true });
            await Task.Delay(700);
            Close();
        }
        catch (Exception ex)
        {
            status.Text = "安装未完成。可以修正网络或安装位置后重试。";
            MessageBox.Show(this, ex.Message, "蓝色大肥鱼安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            install.Enabled = true;
            destination.Enabled = true;
        }
    }

    private async Task InstallAsync(string target)
    {
        target = InstallationLifecycle.ValidateDirectory(target);
        Directory.CreateDirectory(target);
        status.Text = "正在解包程序文件…";
        progress.Value = 0;
        using (var payload = OpenPayload())
        using (var archive = new ZipArchive(payload, ZipArchiveMode.Read, leaveOpen: false))
        {
            ExtractApplication(archive, target);
        }

        var modelFolder = Path.Combine(target, "models");
        Directory.CreateDirectory(modelFolder);
        await EnsureArtifactAsync(modelFolder, ModelName, ModelUrl, ModelSha256, "本地 4B 模型");
        if (RequiresVision)
            await EnsureArtifactAsync(modelFolder, MmprojName, MmprojUrl, MmprojSha256, "视觉投影组件");

        FinishInstallation(target, Environment.ProcessPath!);
        progress.Value = 100;
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
        static bool Preserved(string relative)
        {
            relative = relative.Replace('\\', '/');
            return relative.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith("cache/", StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith("preferences.json", StringComparison.OrdinalIgnoreCase)
                || relative is "news-last-day.txt" or "companion.lock" or "ready.status"
                    or "companion-errors.log" or "test-command.txt" or "test-status.json"
                    or "ocr-status.json" or "vision-status.json" or "vision-input.jpg";
        }
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
            "Microsoft.Windows.SDK.NET.dll", "WinRT.Runtime.dll", "INSTALL-README.txt", "Setup-Model.ps1" }))
        {
            if (current.Contains(relative) || Preserved(relative)) continue;
            var path = Resolve(relative);
            if (File.Exists(path)) File.Delete(path);
        }
        var manifest = new InstalledManifest(ProductVersion, InputMode,
            current.Where(p => !Preserved(p)).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray());
        File.WriteAllText(manifestPath + ".partial", JsonSerializer.Serialize(manifest));
        File.Move(manifestPath + ".partial", manifestPath, overwrite: true);
    }

    private async Task EnsureArtifactAsync(string folder, string name, string url, string sha256, string label)
    {
        var path = Path.Combine(folder, name);
        if (File.Exists(path) && await MatchesHashAsync(path, sha256))
        {
            status.Text = $"{label}已经存在，跳过下载。";
            return;
        }

        var partial = path + ".partial";
        if (File.Exists(partial)) File.Delete(partial);
        await DownloadArtifactAsync(partial, url, label);
        if (!await MatchesHashAsync(partial, sha256))
            throw new InvalidDataException($"{label} SHA-256 校验失败，未使用该文件。\n\n{sha256}");
        File.Move(partial, path, overwrite: true);
    }

    private async Task DownloadArtifactAsync(string partial, string url, string label)
    {
        status.Text = $"正在从 Hugging Face 下载{label}…";
        using var client = new HttpClient { Timeout = TimeSpan.FromHours(2) };
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? 0;
        await using var input = await response.Content.ReadAsStreamAsync();
        await using var output = File.Create(partial);
        var buffer = new byte[1024 * 1024];
        long read = 0;
        int count;
        while ((count = await input.ReadAsync(buffer)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, count));
            read += count;
            if (total > 0) progress.Value = (int)Math.Clamp(read * 100 / total, 0, 100);
        }
    }

    private static async Task<bool> MatchesHashAsync(string path, string expected)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash).Equals(expected, StringComparison.OrdinalIgnoreCase);
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

    private static void CreateShortcut(string shortcutPath, string target, string executableName = "VPet-Simulator.Windows.exe", string arguments = "")
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
            shortcut.IconLocation = Path.Combine(target, "vpeticon.ico") + ",0";
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
        private long left = remaining;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => remaining;
        public override long Position { get => remaining - left; set => throw new NotSupportedException(); }
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
    }
}
