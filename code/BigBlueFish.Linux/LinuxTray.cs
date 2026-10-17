using Avalonia.Controls;
using Avalonia.Threading;

namespace BigBlueFish.Windows;

internal sealed partial class LinuxPetWindow
{
    TrayIcon? tray;
    void RestorePet() { hidden = false; Show(); player.Paused = false; Clamp(); }
    void InitializeTray()
    {
        var menu = new NativeMenu();
        NativeMenuItem Item(string title, Action action) { var item = new NativeMenuItem(title); item.Click += (_, _) => action(); menu.Items.Add(item); return item; }
        Item("显示桌宠", RestorePet); Item("设置", OpenSettings);
        var weather = Item("天气", async () => await Speak(true)); var speech = Item("吐字测试", async () => await Speak(false)); Item("关闭桌宠", Close);
        tray = new TrayIcon { Icon = new WindowIcon(Path.Combine(AppContext.BaseDirectory, "BigBlueFish.ico")), ToolTipText = "蓝色大肥鱼", Menu = menu, IsVisible = true };
        tray.Clicked += (_, _) => RestorePet();
        TrayIcon.SetIcons(Avalonia.Application.Current!, new TrayIcons { tray });
        var status = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        status.Tick += (_, _) => { weather.IsEnabled = speech.IsEnabled = !Puppet; };
        status.Start(); Closed += (_, _) => { status.Stop(); tray.Dispose(); };
    }
}
