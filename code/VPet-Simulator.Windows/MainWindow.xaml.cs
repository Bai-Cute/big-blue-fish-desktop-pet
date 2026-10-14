using System.Windows;

namespace VPet_Simulator.Windows;

// CompanionWindow owns the application lifecycle and native window behavior.
public partial class MainWindow : Window
{
    internal System.Windows.Forms.NotifyIcon notifyIcon = null!;
    public MainWindow() => InitializeCompanion();
}
