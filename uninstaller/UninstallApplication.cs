using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using BigBlueFish.Setup;
using BigBlueFish.Deployment;
using CheckBox = System.Windows.Controls.CheckBox;

namespace BigBlueFish.Uninstaller;

internal static class UninstallApplication
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileEx(string existing, string? destination, int flags);

    internal static Window CreateUninstallForm(string target)
    {
        var window = new ModernWindow("卸载蓝色大肥鱼", "和鱼鱼暂时告别", "勾选下方选项，可以保留模型和设置，方便以后重新安装。", 590, 350);
        var retain = new CheckBox { Content = "保留模型和设置", IsChecked = false };
        window.Body.Children.Add(retain);
        window.Actions.Children.Add(ModernUI.Button("取消", () => window.Close()));
        window.Actions.Children.Add(ModernUI.Button("卸载", () =>
        {
            try
            {
                var temporary = Path.Combine(Path.GetTempPath(), "BigBlueFish-Uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
                File.Copy(Environment.ProcessPath!, temporary);
                var start = new ProcessStartInfo(temporary) { UseShellExecute = true, WorkingDirectory = Path.GetTempPath() };
                start.ArgumentList.Add("--uninstall-worker"); start.ArgumentList.Add(target);
                if (retain.IsChecked != true) start.ArgumentList.Add("--remove-data");
                Process.Start(start); window.Close();
            }
            catch (Exception error) { ModernUI.Error(window, "卸载未完成", error); }
        }, true));
        return window;
    }

    internal static int Run(string[] args)
    {
        ModernUI.EnsureApplication();
        try
        {
            bool worker = args.Length > 0 && args[0] == "--uninstall-worker";
            if (args.Length > 0 && (args.Length < 2 || args[0] is not ("--uninstall" or "--uninstall-worker")))
                throw new ArgumentException("未提供安装目录。");
            var target = InstallationLifecycle.ValidateInstallation(args.Length == 0 ? Path.GetDirectoryName(Environment.ProcessPath!)! : args[1]);
            if (worker)
            {
                InstallationLifecycle.Uninstall(target, args.Contains("--remove-data"));
                MoveFileEx(Environment.ProcessPath!, null, 4);
                ModernUI.Choice(null, "蓝色大肥鱼", "蓝色大肥鱼已卸载。", "确定");
                return 0;
            }
            CreateUninstallForm(target).ShowDialog(); return 0;
        }
        catch (Exception error) { ModernUI.Error(null, "蓝色大肥鱼卸载未完成", error); return 1; }
    }
}
