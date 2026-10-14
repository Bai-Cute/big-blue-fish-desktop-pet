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
        Environment.CurrentDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
#if !DEBUG
        DispatcherUnhandledException += (_, e) => { e.Handled = true; RecordException(e.Exception, false); };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => RecordException((Exception)e.ExceptionObject, true);
#endif
    }

    protected override void OnStartup(StartupEventArgs e)
    {
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
