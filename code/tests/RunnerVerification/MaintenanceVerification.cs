using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BigBlueFish.Deployment;
using BigBlueFish.Windows;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;

internal static class MaintenanceVerification
{
    const BindingFlags Access = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static object? Field(object obj, string name) => obj.GetType().GetField(name, Access)!.GetValue(obj);
    static void Set(object obj, string name, object value) => obj.GetType().GetField(name, Access)!.SetValue(obj, value);
    static object? Invoke(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Access)!.Invoke(obj, args);
    static int checks;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
    static void Pump(int milliseconds = 100)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
    static IEnumerable<T> Children<T>(DependencyObject obj) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
        { var child = VisualTreeHelper.GetChild(obj, i); if (child is T item) yield return item; foreach (var nested in Children<T>(child)) yield return nested; }
    }
    internal static Task<int> Run(string root)
    {
        var result = new TaskCompletionSource<int>();
        var thread = new Thread(() => result.SetResult(Verify(root))); thread.SetApartmentState(ApartmentState.STA); thread.Start(); return result.Task;
    }
    static int Verify(string root)
    {
        MainWindow? window = null;
        try
        {
            // The fixture supplies its own data directory and MainWindow; production
            // App.OnStartup migration is exercised by installer/startup verification.
            var app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            root = Path.GetFullPath(root); Directory.CreateDirectory(root); Environment.CurrentDirectory = root;
            File.Copy(Path.Combine(AppContext.BaseDirectory, "BigBlueFish.ico"), "BigBlueFish.ico", true);
            File.WriteAllText("preferences.json", "{\"ModelEnabled\":true,\"StopModelOnBattery\":false,\"CheckUpdatesOnStartup\":false,\"HideOnFullscreen\":true,\"PublicInfo\":false}");
            ModelStore.Save(root, new ModelStore.Selection("none", null, null));
            Check(new CompanionPreferences().HideOnFullscreen && new CompanionPreferences().CheckUpdatesOnStartup, "new fullscreen and startup-update switches default on");
            window = new MainWindow(); window.Show(); Pump();
            Check((bool)Field(window, "companionReady")!, "no-model startup loads real animation resources");
            Check(((TextBlock)Field(window, "bubbleText")!).Text.Contains("鱼鱼给主人请安啦"), "no-model startup still shows time-based welcome");
            var brain = (CompanionBrain)Field(window, "brain")!;
            Set(window, "nextSpeech", DateTime.MinValue); Set(window, "nextWarmup", DateTime.MinValue); Pump(300);
            Check(!brain.ModelAvailable && brain.RunnerPid == null && !brain.Busy, "no-model mode does not preload a runner or attempt timed generation");
            var test = (Task)Invoke(window, "SpeakCompanionTest")!;
            Check(test.IsCompleted && brain.RunnerPid == null && !(bool)Field(window, "speechRequestPending")!, "speech test without model cannot start generation");
            Invoke(window, "OpenCompanionSettings"); Pump();
            var settings = (Window)Field(window, "companionSettings")!;
            var hide = Children<CheckBox>(settings).Single(c => (string?)c.Content == "全屏时自动隐藏桌宠");
            Set(window, "fullscreenHidden", true); window.Hide(); hide.IsChecked = false; hide.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent)); Pump();
            Check(window.IsVisible && !(bool)Field(window, "fullscreenHidden")! && !((CompanionPreferences)Field(window, "preferences")!).HideOnFullscreen, "turning off fullscreen hiding immediately restores an automatically hidden pet");
            Set(window, "hiddenByUser", true); Set(window, "fullscreenHidden", true); window.Hide(); hide.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent)); Pump();
            Check(!window.IsVisible, "fullscreen switch respects manual hiding");
            Set(window, "hiddenByUser", false); window.Show();
            Children<Button>(settings).Single(b => (string?)b.Content == "模型与供电").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            var install = Children<Button>(settings).Single(b => (string?)b.Content == "安装模型");
            Check(install.IsVisible, "missing-model settings exposes installation button");
            bool shared = false;
            var close = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            close.Tick += (_, _) =>
            {
                var dialog = app.Windows.OfType<ModelInstallWindow>().FirstOrDefault(); if (dialog == null) return;
                shared = true; close.Stop();
                dialog.Actions.Children.OfType<Button>().Single(b => (string?)b.Content == "先无模型启动").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            close.Start(); install.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var openedAt = System.Diagnostics.Stopwatch.StartNew();
            while (!shared && openedAt.Elapsed.TotalSeconds < 5) Pump();
            close.Stop(); Pump();
            Check(shared && ModelStore.ReadSelection(root)?.Source == "none", "settings invokes same real model installer and persists no-model selection");
            Set(window, "fullscreenHidden", false);
            Invoke(window, "RestoreCompanion"); Pump();
            Check(window.IsVisible && window.Topmost && !settings.Topmost, "pet and ordinary settings window keep their distinct Windows layer behavior");
            settings.Close(); window.Close(); Pump();
            Check(((TextBlock)Field(window, "bubbleText")!).Text == "主人再见~鱼鱼喜欢主人哦", "no-model close still shows farewell with real greeting animation");
            Console.WriteLine($"RESULT {checks} no-model and settings behavior checks passed"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { if (window != null) { Set(window, "exitReady", true); window.Close(); } }
    }
}
