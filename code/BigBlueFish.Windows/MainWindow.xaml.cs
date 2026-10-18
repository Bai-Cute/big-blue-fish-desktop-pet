using System.Windows;

namespace BigBlueFish.Windows;

// CompanionWindow owns the application lifecycle and native window behavior.
public partial class MainWindow : Window
{
    internal CompanionTrayIcon notifyIcon = null!;
    public MainWindow() => InitializeCompanion();
}
