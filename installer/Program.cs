using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Drawing;

namespace BigBlueFish.Setup;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new InstallerForm());
    }
}

internal sealed class InstallerForm : Form
{
    private const string ModelName = "Qwen3.5-4B-heretic-Q4_K_M.gguf";
    private const string ModelUrl = "https://huggingface.co/Biomanticus/Qwen3.5-4B-heretic-gguf/resolve/b63ff4662e4863cfa005c9cd8ed34b87ed44b7e1/Qwen3.5-4B-heretic-f16_Q4_K_M.gguf?download=true";
    private const string ModelSha256 = "8485535a36c9f333574d08b650ad698ac02ec30752bd9cd87e493a3b7531bee1";
    private static readonly byte[] PayloadMarker = Encoding.ASCII.GetBytes("BIGBLUEFISH_PAYLOAD_V1");

    private readonly TextBox destination = new();
    private readonly Button install = new();
    private readonly ProgressBar progress = new();
    private readonly Label status = new();
    private readonly LinkLabel source = new();

    public InstallerForm()
    {
        Text = "蓝色大肥鱼 · 安装程序";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(660, 410);
        Font = new Font("Microsoft YaHei UI", 10F);

        var title = new Label
        {
            Text = "安装蓝色大肥鱼",
            Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(34, 26)
        };
        Controls.Add(title);

        var intro = new Label
        {
            Text = "这只可爱的桌宠会把程序和本地 4B 模型安装到电脑中。安装过程中需要联网下载模型。",
            AutoSize = false,
            Size = new Size(590, 46),
            Location = new Point(36, 72)
        };
        Controls.Add(intro);

        var folderLabel = new Label { Text = "安装位置", AutoSize = true, Location = new Point(36, 135) };
        Controls.Add(folderLabel);
        destination.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "蓝色大肥鱼");
        destination.Location = new Point(36, 162);
        destination.Size = new Size(510, 32);
        Controls.Add(destination);
        var browse = new Button { Text = "浏览…", Location = new Point(556, 161), Size = new Size(70, 33) };
        browse.Click += (_, _) => ChooseFolder();
        Controls.Add(browse);

        var model = new Label
        {
            Text = "模型约 2.7 GB。下载完成后会自动校验文件，校验通过才会继续安装。",
            AutoSize = false,
            Size = new Size(590, 28),
            Location = new Point(36, 215)
        };
        Controls.Add(model);
        source.Text = "模型下载源：Hugging Face（点击打开）";
        source.AutoSize = true;
        source.LinkColor = Color.FromArgb(55, 105, 170);
        source.Location = new Point(36, 246);
        source.Click += (_, _) => Process.Start(new ProcessStartInfo(ModelUrl) { UseShellExecute = true });
        Controls.Add(source);

        progress.Location = new Point(36, 292);
        progress.Size = new Size(590, 23);
        progress.Style = ProgressBarStyle.Continuous;
        Controls.Add(progress);
        status.Text = "准备安装";
        status.AutoSize = false;
        status.Size = new Size(590, 28);
        status.Location = new Point(36, 325);
        Controls.Add(status);

        install.Text = "开始安装";
        install.Size = new Size(130, 38);
        install.Location = new Point(496, 360);
        install.Click += async (_, _) => await InstallClickedAsync();
        Controls.Add(install);
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
        Directory.CreateDirectory(target);
        status.Text = "正在解包程序文件…";
        progress.Value = 0;
        using (var payload = OpenPayload())
        using (var archive = new ZipArchive(payload, ZipArchiveMode.Read, leaveOpen: false))
        {
            foreach (var entry in archive.Entries)
            {
                var path = Path.GetFullPath(Path.Combine(target, entry.FullName));
                if (!path.StartsWith(Path.GetFullPath(target) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("安装包包含无效路径。");
                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(path);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                entry.ExtractToFile(path, overwrite: true);
            }
        }

        var modelFolder = Path.Combine(target, "models");
        Directory.CreateDirectory(modelFolder);
        var modelPath = Path.Combine(modelFolder, ModelName);
        if (File.Exists(modelPath) && await MatchesHashAsync(modelPath))
        {
            status.Text = "模型已经存在，跳过下载。";
        }
        else
        {
            var partial = modelPath + ".partial";
            if (File.Exists(partial)) File.Delete(partial);
            await DownloadModelAsync(partial);
            if (!await MatchesHashAsync(partial))
                throw new InvalidDataException("模型 SHA-256 校验失败，未使用该文件。\n\n" + ModelSha256);
            File.Move(partial, modelPath, overwrite: true);
        }

        CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "蓝色大肥鱼.lnk"), target);
        var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "蓝色大肥鱼.lnk");
        Directory.CreateDirectory(Path.GetDirectoryName(startMenu)!);
        CreateShortcut(startMenu, target);
        progress.Value = 100;
    }

    private async Task DownloadModelAsync(string partial)
    {
        status.Text = "正在从 Hugging Face 下载本地 4B 模型…";
        using var client = new HttpClient { Timeout = TimeSpan.FromHours(2) };
        using var response = await client.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead);
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

    private static async Task<bool> MatchesHashAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash).Equals(ModelSha256, StringComparison.OrdinalIgnoreCase);
    }

    private static Stream OpenPayload()
    {
        var self = Environment.ProcessPath ?? throw new InvalidOperationException("无法定位安装程序。");
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

    private static void CreateShortcut(string shortcutPath, string target)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = Path.Combine(target, "VPet-Simulator.Windows.exe");
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
