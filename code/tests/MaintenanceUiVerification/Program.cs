using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using BigBlueFish.Windows;

static class Program
{
    const BindingFlags Access = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    [STAThread] static int Main(string[] args)
    {
        string directory = Path.GetFullPath(args[0]); Directory.CreateDirectory(directory);
        var app = new App { ShutdownMode = ShutdownMode.OnLastWindowClose };
        App.Args = ["--test-mode"]; Environment.CurrentDirectory = directory;
        File.Copy(Path.Combine(AppContext.BaseDirectory,"BigBlueFish.ico"),Path.Combine(directory,"BigBlueFish.ico"),true);
        File.WriteAllText("preferences.json","{\"ModelEnabled\":false,\"PublicInfo\":false,\"CheckUpdatesOnStartup\":false}");
        var pet = new MainWindow();
        object? Read(string name) => pet.GetType().GetField(name,Access)!.GetValue(pet);
        object? Invoke(string name) => pet.GetType().GetMethod(name,Access)!.Invoke(pet,null);
        Window? picker = null; bool trayClosed = false, reproduced = false, timedOut = false; int stage=0;
        var clock=Stopwatch.StartNew();
        var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_,_) => {
            try {
                if (clock.Elapsed.TotalSeconds>20) { timedOut=true; timer.Stop(); app.Shutdown(2); return; }
                if(stage==0 && Read("companionReady") is true) {
                    stage=1;
                    if(args.Contains("--owned")) Invoke("OpenCompanionSettings");
                    _=pet.Dispatcher.BeginInvoke(() => Invoke("OpenModelInstaller"));
                } else if(stage==1 && Read("modelInstaller") is Window window && window.IsVisible) {
                    picker=window; stage=2;
                    var tray=(CompanionTrayIcon)Read("notifyIcon")!;
                    tray.ContextMenuStrip!.Items[4].PerformClick(); trayClosed=true;
                } else if(stage==2 && !pet.IsVisible && picker?.IsVisible==true) {
                    try { Invoke("RestoreCompanion"); }
                    catch(TargetInvocationException error) when(error.InnerException is InvalidOperationException) { reproduced=true; }
                    Console.WriteLine("Old failure reproduced: pet closed="+!pet.IsVisible+", picker still open="+picker.IsVisible+", tray visible="+((CompanionTrayIcon)Read("notifyIcon")!).Visible+", restore throws="+reproduced);
                    timer.Stop(); app.Shutdown(reproduced ? 0 : 2);
                }
            } catch(Exception error) { Console.Error.WriteLine(error); timedOut=true; timer.Stop(); app.Shutdown(2); }
        };
        timer.Start(); app.Run(pet); timer.Stop();
        if(args.Contains("--expect-bug")) return reproduced ? 0 : 1;
        bool cleaned=Read("companionClosed") is true && !((CompanionTrayIcon)Read("notifyIcon")!).Visible;
        bool success=trayClosed && !reproduced && !timedOut && cleaned && picker?.IsVisible==false;
        Console.WriteLine("Maintenance shutdown: tray command="+trayClosed+", picker closed="+(picker?.IsVisible==false)+", lifecycle disposed="+cleaned+", completed seconds="+clock.Elapsed.TotalSeconds.ToString("0.00"));
        return success ? 0 : 1;
    }
}
