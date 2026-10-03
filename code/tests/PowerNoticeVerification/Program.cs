using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using VPet_Simulator.Windows;

internal static class Program
{
    const BindingFlags Access = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const string Notice = "主人，当前设置为笔记本离电暂停模型，可以在设置里调节哦。";
    static int checks;
    static object Read(object o, string n) => o.GetType().GetField(n, Access)!.GetValue(o)!;
    static T Property<T>(object o, string n) => (T)o.GetType().GetProperty(n, Access)!.GetValue(o)!;
    static void Set(object o, string n, object v) => o.GetType().GetProperty(n, Access)!.SetValue(o, v);
    static object? Invoke(object o, string n, params object[] a) => o.GetType().GetMethod(n, Access)!.Invoke(o, a);
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
    static void Pump(int milliseconds = 50)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Send) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    static void Wait(Func<bool> condition, int seconds, string message)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.Elapsed.TotalSeconds < seconds) Pump();
        Check(condition(), message);
    }
    [STAThread]
    static int Main(string[] args)
    {
        MainWindow? window = null;
        try
        {
            string sandbox = Path.GetFullPath(args[0]);
            var app = new App(); app.InitializeComponent();
            typeof(Application).GetField("_startupUri", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, null);
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Directory.SetCurrentDirectory(sandbox);
            File.WriteAllText("preferences.json", "{\"ModelEnabled\":true,\"GpuEnabled\":false,\"StopModelOnBattery\":true,\"PublicInfo\":false,\"ForegroundEnabled\":false,\"BubbleDurationIndex\":0}");
            var native = typeof(App).Assembly.GetType("VPet_Simulator.Windows.CompanionNative")!;
            void Power(bool value) => native.GetField("TestPowerOverride", Access)!.SetValue(null, value);
            Power(false);
            window = new MainWindow(); window.Show();
            Wait(() => (bool)Read(window, "companionReady"), 30, "Pet did not load");
            var timer = (DispatcherTimer)Read(window, "companionTimer"); timer.Stop();
            var bubble = (Border)Read(window, "bubble");
            var text = (TextBlock)Read(window, "bubbleText");
            var speech = Read(window, "speech");
            var preferences = Read(window, "preferences");
            var brain = Read(window, "brain");
            void Tick() => Invoke(window, "CompanionTick", null!, EventArgs.Empty);
            void Steady(int ms)
            {
                var clock = Stopwatch.StartNew();
                while (clock.ElapsedMilliseconds < ms) { Tick(); Pump(40); }
            }
            ((Task)Invoke(window, "SpeakCompanionTest")!).GetAwaiter().GetResult();
            Tick(); Pump();
            if (args.Contains("--expect-bug"))
            {
                Check(bubble.Visibility == Visibility.Collapsed, "Original disappearing notice was not reproduced");
                Check(Property<int?>(brain, "RunnerPid") == null, "Blocked request started a model");
                Console.WriteLine("REPRODUCED: battery pause notice collapsed on the next main tick.");
                return 0;
            }
            Check(Property<bool>(speech, "IsTyping"), "Fixed notice did not type progressively");
            Check(bubble.Visibility == Visibility.Visible, "Test notice disappeared on the next tick");
            Steady(1600);
            Check(text.Text == Notice && !Property<bool>(speech, "IsTyping"), "Test notice text differs from the approved sentence");
            Check(window.OwnedWindows.Cast<Window>().Single().IsVisible, "Actual notice window hidden");
            Steady(1000);
            Check(bubble.Visibility == Visibility.Visible, "Notice did not remain after typing");
            Check(Property<int?>(brain, "RunnerPid") == null && !Property<bool>(brain, "Busy"), "Battery notice triggered model inference");
            Steady(4500);
            Check(bubble.Visibility == Visibility.Collapsed, "Five-second dwell did not expire");
            ((Task)Invoke(window, "SpeakCompanionWeather")!).GetAwaiter().GetResult();
            Steady(1800);
            Check(text.Text == Notice && bubble.Visibility == Visibility.Visible, "Weather notice did not survive power checks");
            Invoke(window, "ApplyCompanionPowerPolicy"); Steady(500);
            Check(bubble.Visibility == Visibility.Visible, "Repeated power notification hid the notice");
            Set(preferences, "BubbleDurationIndex", 7);
            ((Task)Invoke(window, "SpeakCompanionTest")!).GetAwaiter().GetResult();
            Steady(2000);
            Check((DateTime)Read(window, "bubbleUntil") == DateTime.MaxValue, "Stay-until-next-message setting lost");
            Set(preferences, "BubbleDurationIndex", 0);
            Set(preferences, "ModelEnabled", false);
            ((Task)Invoke(window, "SpeakCompanionWeather")!).GetAwaiter().GetResult(); Steady(300);
            Check(text.Text.Contains("启用本地大模型") && bubble.Visibility == Visibility.Visible, "Disabled-model notice disappeared");
            Set(preferences, "ModelEnabled", true);
            Power(true); Invoke(window, "ApplyCompanionPowerPolicy");
            var request = (Task)Invoke(window, "SpeakCompanionTest")!;
            Wait(() => request.IsCompleted, 110, "Real CPU model test timed out"); request.GetAwaiter().GetResult();
            Check(Property<bool>(speech, "IsTyping"), "Real model output did not use normal typing");
            Wait(() => !Property<bool>(speech, "IsTyping"), 8, "Real model typing did not finish");
            string generated = text.Text;
            Check(generated.Length > 10 && !generated.Contains("未能生成") && generated != Notice, "Expected real model output: " + generated);
            int runner = Property<int?>(brain, "RunnerPid")!.Value;
            Steady(1000);
            Check(bubble.Visibility == Visibility.Visible, "Real model output disappeared before its dwell");
            Power(false); Invoke(window, "ApplyCompanionPowerPolicy"); Steady(200);
            Check(bubble.Visibility == Visibility.Collapsed && !Property<bool>(speech, "IsTyping"), "Power pause did not clear model reply");
            Check(Property<int?>(brain, "RunnerPid") == null, "Power pause did not unload model");
            ((Task)Invoke(window, "SpeakCompanionTest")!).GetAwaiter().GetResult(); Steady(1600);
            Check(text.Text == Notice && bubble.Visibility == Visibility.Visible, "Pause notice failed after unloading a real model");
            File.WriteAllText("power-notice-result.json", JsonSerializer.Serialize(new { date = "2026-10-04", checks, passed = true, exactNotice = Notice, generated, runner, simulatedPower = true, realCpuInference = true }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"PASS: {checks} checks. Real CPU output: {generated}");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally { window?.Close(); }
    }
}
