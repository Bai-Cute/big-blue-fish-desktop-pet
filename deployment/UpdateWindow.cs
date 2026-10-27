using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using ProgressBar = System.Windows.Controls.ProgressBar;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace BigBlueFish.Deployment;

internal sealed class UpdateWindow : ModernWindow
{
    private readonly AvailableUpdate update;
    private readonly string directory;
    private readonly string dataDirectory;
    private readonly TextBlock status = ModernUI.Text("");
    private readonly ProgressBar progress = new();
    private readonly Button start;
    private CancellationTokenSource? download;
    private bool closeRequested, closed;
    internal bool InstallerStarted { get; private set; }

    internal UpdateWindow(AvailableUpdate update, string directory, string? dataDirectory = null) : base("更新蓝色大肥鱼", "鱼鱼有新版本啦", "可更新到 " + update.Version, 630, 480)
    {
        this.update = update; this.directory = Path.GetFullPath(directory);
        this.dataDirectory = dataDirectory ?? ApplicationData.DirectoryFor(directory);
        Body.Children.Add(ModernUI.Text(update.Notes)); Body.Children.Add(progress); Body.Children.Add(status);
        Actions.Children.Add(ModernUI.Button("稍后", () => { if (download != null) download.Cancel(); else Close(); }));
        start = ModernUI.Button("下载并更新", async () => await Install(), true); Actions.Children.Add(start);
        Closing += (_, e) => { if (download != null) { closeRequested = true; download.Cancel(); e.Cancel = true; } };
        Closed += (_, _) => { closed = true; download?.Cancel(); };
    }
    internal void RequestClose() { if (closed) return; closeRequested = true; download?.Cancel(); Close(); }
    private async Task Install()
    {
        if (download != null || closed || closeRequested) return;
        download = new CancellationTokenSource(); start.IsEnabled = false;
        try
        {
            var installer = Path.Combine(dataDirectory, "cache", "updates", update.Name);
            status.Text = "正在下载更新…";
            await ArtifactDownload.Download(update.Url, installer, update.Sha256,
                new Progress<ArtifactDownload.Progress>(p => { progress.Value = p.Percent; status.Text = p.ToString(); }), download.Token);
            // The installer waits for this exact app process to finish its normal exit.
            var process = new ProcessStartInfo(installer) { UseShellExecute = true, Verb = "runas", WorkingDirectory = directory };
            process.ArgumentList.Add("--update"); process.ArgumentList.Add(directory);
            process.ArgumentList.Add("--wait-pid"); process.ArgumentList.Add(Environment.ProcessId.ToString());
            Process.Start(process); InstallerStarted = true;
        }
        catch (OperationCanceledException) { status.Text = "下载已暂停，可以稍后继续。"; }
        catch (Exception error) { if (!closed && !closeRequested) ModernUI.Error(this, "更新未完成", error); }
        finally { download.Dispose(); download = null; start.IsEnabled = true; }
        if (InstallerStarted || closeRequested) Close();
    }
}
