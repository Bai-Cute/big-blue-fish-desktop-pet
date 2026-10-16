using System;
using System.Diagnostics;
using System.Windows;
using System.Threading.Tasks;
using BigBlueFish.Deployment;

namespace BigBlueFish.Windows;

public partial class MainWindow
{
    private ModelInstallWindow? modelInstaller;
    private bool checkingUpdates;
    private UpdateWindow? updateWindow;
    private AvailableUpdate? availableUpdate;
    private string updateStatus = "";
    private void OpenModelInstaller()
    {
        if (exitAnimation || companionClosed) return;
        if (modelInstaller != null) { modelInstaller.Activate(); return; }
        brain.Stop();
        var dialog = modelInstaller = new ModelInstallWindow(Environment.CurrentDirectory);
        if (companionSettings?.IsVisible == true) dialog.Owner = companionSettings;
        dialog.ShowDialog();
        modelInstaller = null;
        brain.RefreshModelAvailability();
        nextWarmup = DateTime.MinValue;
        nextSpeech = DateTime.UtcNow.AddSeconds(20);
    }

    private async Task CheckForUpdates(bool interactive)
    {
        if (checkingUpdates || companionClosed || exitAnimation) return;
        checkingUpdates = true; updateStatus = "正在检查更新…";
        try
        {
            var version = typeof(MainWindow).Assembly.GetName().Version!.ToString(3);
            availableUpdate = await UpdateService.Check(version);
            if (companionClosed || exitAnimation) return;
            updateStatus = availableUpdate == null ? "当前已是最新版本" : "发现新版本 " + availableUpdate.Version;
            if (availableUpdate != null)
            {
                if (interactive) OpenUpdateWindow();
                else notifyIcon.ShowBalloonTip(8000, "蓝色大肥鱼有新版本啦", "点击下载并更新到 " + availableUpdate.Version, System.Windows.Forms.ToolTipIcon.Info);
            }
            else if (interactive) ModernUI.Choice(companionSettings, "检查更新", updateStatus, "确定");
        }
        catch (Exception error)
        {
            updateStatus = "暂时无法检查更新，可以稍后重试";
            if (interactive) ModernUI.Error(companionSettings, "检查更新未完成", error);
        }
        finally { checkingUpdates = false; }
    }

    private void OpenUpdateWindow()
    {
        if (availableUpdate == null || companionClosed || exitAnimation) return;
        if (updateWindow != null) { updateWindow.Activate(); return; }
        var dialog = updateWindow = new UpdateWindow(availableUpdate, Environment.CurrentDirectory);
        if (companionSettings?.IsVisible == true) dialog.Owner = companionSettings;
        dialog.ShowDialog();
        updateWindow = null;
        if (dialog.InstallerStarted) base.Close();
    }
}
