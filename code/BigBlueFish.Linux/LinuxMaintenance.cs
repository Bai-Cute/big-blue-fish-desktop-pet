using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using BigBlueFish.Deployment;

namespace BigBlueFish.Windows;

internal sealed partial class LinuxPetWindow
{
    bool updateChecking;
    static void OpenUrl(string url) { var start = new ProcessStartInfo("/usr/bin/xdg-open") { UseShellExecute = false }; start.ArgumentList.Add(url); Process.Start(start); }
    async Task InstallModel()
    {
        if (modelInstalling) return; modelInstalling = true; brain.Stop(); speechVersion++;
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = "安装本地模型", FontSize = 24 });
        panel.Children.Add(Paragraph("选择下载来源，或导入已下载的 GGUF 模型。两个源的模型有所不同，但是均适合本应用。"));
        var dialog = Dialog("蓝色大肥鱼 · 安装模型", panel, 680, 550);
        var status = Paragraph("模型目录：" + Path.Combine(Environment.CurrentDirectory, "models")); panel.Children.Add(status);
        var progress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 8 }; panel.Children.Add(progress);
        var choices = new WrapPanel(); panel.Children.Add(choices); var retry = new WrapPanel { IsVisible = false }; panel.Children.Add(retry);
        using var cancel = new CancellationTokenSource(); dialog.Closing += (_, _) => cancel.Cancel(); bool busy = false;
        async Task Manual()
        {
            OpenUrl(ModelStore.HuggingFace.Page);
            status.Text = "请将模型放入：" + Path.Combine(Environment.CurrentDirectory, "models") + "\n然后选择已下载的 GGUF 模型文件。";
            var files = await dialog.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "选择 GGUF 模型", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("GGUF 模型") { Patterns = ["*.gguf"] }] });
            if (files.Count == 0) return;
            string? path = files[0].TryGetLocalPath();
            if (path == null || !ModelStore.IsGguf(path)) { status.Text = "请选择有效的 GGUF 模型文件。"; return; }
            busy = true; choices.IsEnabled = retry.IsEnabled = false;
            try {
                status.Text = "正在校验模型文件…"; string hash = await ModelStore.Hash(path, cancel.Token);
                if (!ModelStore.Expected(hash) && !await Confirm("使用此模型", "这不是本软件期待的模型，您可以尝试使用，但稳定性无法保证", "继续使用", cancel.Token)) return;
                await ModelStore.Import(Environment.CurrentDirectory, path, hash, cancel.Token); brain.RefreshModelAvailability(); dialog.Close();
            } catch (OperationCanceledException) { } catch (Exception error) { status.Text = error.Message; }
            finally { busy = false; choices.IsEnabled = retry.IsEnabled = true; }
        }
        ModelSource selected = ModelStore.HuggingFace;
        async Task Download(ModelSource source)
        {
            if (busy) return; busy = true; selected = source; choices.IsEnabled = false; retry.IsVisible = false;
            try {
                string target = Path.Combine(Environment.CurrentDirectory, "models", source.FileName);
                await ArtifactDownload.Download(source.Url, target, source.Sha256, new Progress<ArtifactDownload.Progress>(p => { progress.Value = p.Percent; status.Text = p.ToString(); }), cancel.Token);
                if (!ModelStore.IsGguf(target)) throw new IOException("下载文件不是有效的 GGUF 模型。");
                ModelStore.Save(Environment.CurrentDirectory, new(source.Id, source.FileName, source.Sha256)); brain.RefreshModelAvailability(); dialog.Close();
            } catch (OperationCanceledException) { } catch (Exception error) { status.Text = "下载未完成：" + error.Message + "\n您可以改善网络环境后重试、手动安装模型，或无模型启动。"; retry.IsVisible = true; }
            finally { busy = false; choices.IsEnabled = true; }
        }
        void Without() { ModelStore.Save(Environment.CurrentDirectory, new("none", null, null)); brain.RefreshModelAvailability(); dialog.Close(); }
        foreach (var source in ModelStore.Sources) choices.Children.Add(ActionButton(source.Label, async () => await Download(source)));
        choices.Children.Add(ActionButton("手动安装", async () => await Manual())); choices.Children.Add(ActionButton("无模型启动", Without));
        retry.Children.Add(ActionButton("重试", async () => await Download(selected))); retry.Children.Add(ActionButton("手动安装模型", async () => await Manual())); retry.Children.Add(ActionButton("无模型启动", Without));
        try { await dialog.ShowDialog(settings ?? this); }
        finally { modelInstalling = false; nextWarmup = DateTime.MinValue; }
    }
    async Task CheckUpdates(bool manual)
    {
        if (updateChecking) return; updateChecking = true;
        try {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BigBlueFish/" + typeof(LinuxPetWindow).Assembly.GetName().Version!.ToString(3));
            using var json = JsonDocument.Parse(await client.GetStringAsync("https://api.github.com/repos/Bai-Cute/big-blue-fish-desktop-pet/releases/latest"));
            var root = json.RootElement; string tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v'), out var latest) || latest <= typeof(LinuxPetWindow).Assembly.GetName().Version) { if (manual) await Confirm("检查更新", "当前已经是最新版本。", "知道了"); return; }
#if COMPANION_SAFE
            string asset = $"BigBlueFish-v{latest.ToString(3)}-Safe-Linux-x64.deb";
#else
            string asset = $"BigBlueFish-v{latest.ToString(3)}-Linux-x64.deb";
#endif
            var assets = root.GetProperty("assets").EnumerateArray().ToArray();
            string? Url(string name) => assets.FirstOrDefault(a => a.GetProperty("name").GetString() == name).TryGetProperty("browser_download_url", out var value) ? value.GetString() : null;
            string? url = Url(asset), checksum = Url(asset + ".sha256"); if (url == null || checksum == null) throw new IOException("该版本的 Linux 安装包尚未就绪。");
            if (!await Confirm("发现新版本", "发现蓝色大肥鱼 " + latest.ToString(3) + "，是否下载并安装更新？", "下载更新")) return;
            string hash = (await client.GetStringAsync(checksum)).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();
            if (!Regex.IsMatch(hash, "^[a-f0-9]{64}$")) throw new IOException("更新校验信息无效。");
            string package = Path.Combine(Environment.CurrentDirectory, "cache/updates", asset);
            await ArtifactDownload.Download(url, package, hash);
            var start = new ProcessStartInfo("/usr/bin/pkexec") { UseShellExecute = false };
            foreach (string item in new[] { "/usr/bin/apt-get", "install", "-y", package }) start.ArgumentList.Add(item);
            using var installer = Process.Start(start) ?? throw new IOException("无法启动系统安装器。");
            await installer.WaitForExitAsync(); if (installer.ExitCode != 0) throw new IOException("更新安装未完成，可再次检查更新后重试。");
            var restart = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "BigBlueFish.Linux")) { UseShellExecute = false };
            restart.ArgumentList.Add("--wait-for-exit"); restart.ArgumentList.Add(Environment.ProcessId.ToString()); Process.Start(restart); CloseImmediately();
        } catch (Exception error) { if (manual) await Confirm("检查更新", error.Message, "知道了"); }
        finally { updateChecking = false; }
    }
}

