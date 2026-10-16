using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Button = System.Windows.Controls.Button;
using RadioButton = System.Windows.Controls.RadioButton;
using ProgressBar = System.Windows.Controls.ProgressBar;
using TextBox = System.Windows.Controls.TextBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace BigBlueFish.Deployment;

internal sealed class ModelInstallWindow : ModernWindow
{
    private readonly string directory;
    private ModelSource source = ModelStore.HuggingFace;
    private readonly RadioButton recommended = new() { Content = "Hugging Face（推荐）", GroupName = "ModelSource", IsChecked = true };
    private readonly RadioButton domestic = new() { Content = "国内源（稳定）", GroupName = "ModelSource" };
    private readonly RadioButton manual = new() { Content = "手动安装", GroupName = "ModelSource" };
    private readonly StackPanel manualPanel = new();
    private readonly TextBlock status = ModernUI.Text("");
    private readonly ProgressBar progress = new();
    private readonly Button install;
    private readonly Button skip;
    private CancellationTokenSource? download;
    private bool succeeded;
    private bool busy;
    private readonly HttpClient? client;
    private readonly Action<string>? openAddress;
    internal bool Completed { get; private set; }

    internal ModelInstallWindow(string installationDirectory, HttpClient? client = null, Action<string>? openAddress = null) : base("安装本地模型", "为鱼鱼准备本地模型", "两个源的模型有所不同，但是均适合本应用。自动安装会从 Hugging Face 或国内 ModelScope 下载约 2.7 GB 的模型。", 650, 630)
    {
        this.client = client; this.openAddress = openAddress;
        directory = Path.GetFullPath(installationDirectory); Directory.CreateDirectory(Path.Combine(directory, "models"));
        foreach (var option in new[] { recommended, domestic, manual }) Body.Children.Add(option);
        recommended.Checked += (_, _) => { source = ModelStore.HuggingFace; SetManual(false); };
        domestic.Checked += (_, _) => { source = ModelStore.Domestic; SetManual(false); };
        manual.Checked += (_, _) => SetManual(true);
        manualPanel.Children.Add(ModernUI.Text("请把下载好的 GGUF 模型放入："));
        manualPanel.Children.Add(new TextBox { Text = Path.Combine(directory, "models"), IsReadOnly = true, TextWrapping = TextWrapping.Wrap });
        var links = new WrapPanel();
        links.Children.Add(ModernUI.Button("打开模型文件夹", () => Open(Path.Combine(directory, "models"))));
        links.Children.Add(ModernUI.Button("国内模型页面", () => Open(ModelStore.Domestic.Page)));
        links.Children.Add(ModernUI.Button("Hugging Face 页面", () => Open(ModelStore.HuggingFace.Page)));
        links.Children.Add(ModernUI.Button("选择已下载文件", async () => await PickFile()));
        manualPanel.Children.Add(links); manualPanel.Visibility = Visibility.Collapsed;
        Body.Children.Add(manualPanel); Body.Children.Add(progress); Body.Children.Add(status);
        skip = ModernUI.Button("先无模型启动", () => FinishWithoutModel());
        install = ModernUI.Button("下载并安装", async () => await Install(), true);
        Actions.Children.Add(skip); Actions.Children.Add(install);
        Closing += (_, e) =>
        {
            if (download != null && !succeeded) { download.Cancel(); e.Cancel = true; status.Text = "正在停止下载…"; }
            else if (busy && !succeeded) e.Cancel = true;
        };
        Closed += (_, _) => download?.Dispose();
    }

    private void Open(string address)
    {
        try { if (openAddress != null) openAddress(address); else Process.Start(new ProcessStartInfo(address) { UseShellExecute = true }); }
        catch (Exception error) { ModernUI.Error(this, "未能打开", error); }
    }
    private void SetManual(bool value)
    {
        manualPanel.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        install.Content = value ? "我已放好，检查并继续" : "下载并安装";
        if (value) Open(source.Page);
    }
    private void SetBusy(bool busy)
    {
        this.busy = busy;
        recommended.IsEnabled = domestic.IsEnabled = manual.IsEnabled = install.IsEnabled = skip.IsEnabled = !busy;
        manualPanel.IsEnabled = !busy;
    }
    private void FinishWithoutModel()
    {
        ModelStore.Save(directory, new ModelStore.Selection("none", null, null));
        Completed = true; succeeded = true; Close();
    }
    private async Task PickFile()
    {
        var picker = new OpenFileDialog { Filter = "GGUF 模型 (*.gguf)|*.gguf|所有文件 (*.*)|*.*", InitialDirectory = Path.Combine(directory, "models"), CheckFileExists = true };
        if (picker.ShowDialog(this) == true) await Import(picker.FileName);
    }
    private async Task Import(string path)
    {
        SetBusy(true);
        try
        {
            if (!ModelStore.IsGguf(path)) throw new InvalidDataException("请选择有效的 GGUF 模型文件。");
            status.Text = "正在检查模型…"; progress.IsIndeterminate = true;
            var hash = await ModelStore.Hash(path);
            progress.IsIndeterminate = false;
            if (!ModelStore.Expected(hash) && ModernUI.Choice(this, "使用此模型？", "这不是本软件期待的模型，您可以尝试使用，但稳定性无法保证", "继续使用", "取消") != 0) return;
            await ModelStore.Import(directory, path, hash);
            Completed = true; succeeded = true; Close();
        }
        catch (Exception error) { ModernUI.Error(this, "模型安装未完成", error); }
        finally { progress.IsIndeterminate = false; SetBusy(false); }
    }
    private async Task Install()
    {
        if (manual.IsChecked == true)
        {
            var files = Directory.GetFiles(Path.Combine(directory, "models"), "*.gguf").Where(p => !Path.GetFileName(p).Contains("mmproj", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (files.Length == 1) await Import(files[0]); else await PickFile();
            return;
        }
        SetBusy(true); download = new CancellationTokenSource();
        try
        {
            var path = Path.Combine(directory, "models", source.FileName);
            status.Text = "正在从" + source.Label + "下载模型…";
            await ArtifactDownload.Download(source.Url, path, source.Sha256,
                new Progress<ArtifactDownload.Progress>(p => { progress.Value = p.Percent; status.Text = p.ToString(); }), download.Token, client);
            if (!ModelStore.IsGguf(path)) throw new InvalidDataException("下载文件不是有效的 GGUF 模型。");
            ModelStore.Save(directory, new ModelStore.Selection(source.Id, source.FileName, source.Sha256));
            Completed = true; succeeded = true; Close();
        }
        catch (OperationCanceledException) { status.Text = "下载已停止，可以重新选择。"; }
        catch (Exception)
        {
            int choice = ModernUI.Choice(this, "模型下载未完成", "您可以改善网络环境后重试，手动安装模型，或无模型启动。", "重试", "手动安装模型", "无模型启动");
            if (choice == 0) _ = Dispatcher.BeginInvoke(new Action(async () => await Install()));
            else if (choice == 1) { manual.IsChecked = true; }
            else if (choice == 2) FinishWithoutModel();
        }
        finally { download.Dispose(); download = null; SetBusy(false); }
    }
}
