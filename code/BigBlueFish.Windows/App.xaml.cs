using System;
using System.IO;
using System.Reflection;
using System.Windows;

namespace BigBlueFish.Windows;

public partial class App : Application
{
    public static string[] Args { get; set; } = [];

    public App()
    {
#if !DEBUG
        DispatcherUnhandledException += (_, e) => { e.Handled = true; RecordException(e.Exception, false); };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => RecordException((Exception)e.ExceptionObject, true);
#endif
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        try { Environment.CurrentDirectory = BigBlueFish.Deployment.ApplicationData.Prepare(AppContext.BaseDirectory); }
        catch (Exception error)
        {
            MessageBox.Show("无法准备大肥鱼的数据目录：" + error.Message, "蓝色大肥鱼", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1); return;
        }
        Args = e.Args;
        base.OnStartup(e);
    }

    private void RecordException(Exception exception, bool fatal)
    {
        try { File.AppendAllText("companion-errors.log", DateTime.Now.ToString("s") + " " + exception + Environment.NewLine); }
        catch { }
        if (fatal) Shutdown(1);
    }
}
