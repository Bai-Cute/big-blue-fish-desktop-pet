using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BigBlueFish.Windows;
using BigBlueFish.Diagnostics;
using Application = System.Windows.Application;
using ComboBox = System.Windows.Controls.ComboBox;

internal static class SpeechFlowVerification
{
    const BindingFlags Access = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static object? Field(object target, string name) => target.GetType().GetField(name, Access)!.GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Access)!.SetValue(target, value);
    static object? Invoke(object target, string name, params object[] args)
    {
        var method = target.GetType().GetMethod(name, Access)!;
        return method.Invoke(target, args.Concat(method.GetParameters().Skip(args.Length).Select(p => p.DefaultValue)).ToArray());
    }
    static int checks;
    static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        checks++; Console.WriteLine("PASS " + message);
    }
    static void Pump(int milliseconds = 30)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Send) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    static void Wait(Func<bool> predicate, string message, int seconds = 35)
    {
        var timer = Stopwatch.StartNew();
        while (!predicate() && timer.Elapsed.TotalSeconds < seconds) Pump();
        Check(predicate(), message);
    }
    static IEnumerable<T> Children<T>(DependencyObject target) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(target); i++)
        {
            var child = VisualTreeHelper.GetChild(target, i);
            if (child is T item) yield return item;
            foreach (var item2 in Children<T>(child)) yield return item2;
        }
    }
    internal static Task<int> Run(string root)
    {
        var result = new TaskCompletionSource<int>();
        var thread = new Thread(() => result.SetResult(Verify(root)));
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return result.Task;
    }
    static int Verify(string root)
    {
        MainWindow? window = null;
        try
        {
            root = Path.GetFullPath(root);
            var app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Directory.SetCurrentDirectory(Path.GetFullPath(root));
            File.Copy(Path.Combine(AppContext.BaseDirectory, "BigBlueFish.ico"), "BigBlueFish.ico", true);
            // Isolated settings; real runtime/model/resources are junctions supplied by the caller.
            File.WriteAllText("preferences.json", "{\"ModelEnabled\":false,\"GpuEnabled\":true,\"StopModelOnBattery\":false,\"ForegroundEnabled\":true,\"PublicInfo\":false,\"Scale\":0.85,\"SpeechMinMinutes\":3,\"Left\":400,\"Top\":250}");
            var random = new Random(20261014);
            foreach (var range in CompanionPreferences.SpeechRanges)
            {
                var settings = new CompanionPreferences { SpeechMinMinutes = range.Min };
                var samples = Enumerable.Range(0, 1000).Select(_ => settings.NextSpeechDelay(random).TotalMinutes).ToArray();
                Check(samples.All(n => n >= range.Min && n <= range.Max), $"scheduled intervals stay within {range.Min}-{range.Max}");
                Check(samples.Min() < range.Min + .1 && samples.Max() > range.Max - .1, "both ends of selected range are sampled");
            }
            Check(CompanionPreferences.NormalizeSpeechMinimum(8) == 10 && CompanionPreferences.NormalizeSpeechMinimum(30) == 20,
                "old interval settings migrate to supported ranges");
            window = new MainWindow(); window.Show();
            Wait(() => (bool)Field(window, "companionReady")!, "actual animations load", 15);
            ((DispatcherTimer)Field(window, "companionTimer")!).Stop();
            var prefs = (CompanionPreferences)Field(window, "preferences")!;
            var brain = (CompanionBrain)Field(window, "brain")!;
            Invoke(window, "OpenCompanionSettings"); Pump(150);
            var settingsWindow = (Window)Field(window, "companionSettings")!;
            Children<System.Windows.Controls.Button>(settingsWindow).Single(b => (string?)b.Content == "日志控制台")
                .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); Pump(350);
            var logBox = Children<System.Windows.Controls.TextBox>(settingsWindow).Single(t => t.Name == "LogConsoleText");
            Check(logBox.IsReadOnly && logBox.Text.Contains("SettingsOpened"), "real settings log page shows previous events as read-only text");
            DiagnosticLog.Write(DiagnosticEvent.McpTool, kind: "say", state: "success"); Pump(350);
            Check(logBox.Text.Contains("kind=say; state=success"), "real log console refreshes new metadata without reopening settings");
            Check(Children<System.Windows.Controls.Button>(settingsWindow).Any(b => (string?)b.Content == "保存为 TXT…"), "TXT export control exists in settings");
            var consoleImage = new System.Windows.Media.Imaging.RenderTargetBitmap((int)settingsWindow.ActualWidth, (int)settingsWindow.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            consoleImage.Render((Visual)settingsWindow.Content);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(consoleImage));
            using (var imageFile = File.Create("log-console.png")) encoder.Save(imageFile);
            var sliders = Children<Slider>(settingsWindow).ToArray();
            var scaleSlider = sliders.Single(s => s.Minimum == .5 && s.Maximum == 2.0
                && System.Windows.Automation.AutomationProperties.GetName(s) != "文字气泡大小");
            Check(scaleSlider.TickFrequency == .05, "50%-200% settings slider uses 5% steps");
            foreach (var scale in new[] { .5, 2.0, .85 })
            {
                scaleSlider.Value = scale; Pump(100);
                Check(Math.Abs(prefs.Scale - scale) < .001, "scale takes effect and persists " + scale);
                window.Left = -800; window.Top = -800; Invoke(window, "ClampCompanion"); Pump();
                var body = CompanionEdgeLayout.PetBounds(window);
                var work = CompanionEdgeLayout.WorkArea(window);
                Check(window.Left + body.Left >= work.Left - 1 && window.Top + body.Top >= work.Top - 1,
                    "visible character stays inside desktop at scale " + scale);
            }
            Children<System.Windows.Controls.Button>(settingsWindow).Single(b => (string?)b.Content == "说话与天气")
                .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); Pump(100);
            var frequency = Children<ComboBox>(settingsWindow).Single(c => c.Items.Count == 6 && c.Items[0].ToString()!.StartsWith("说话间隔"));
            Check(frequency.Items.Cast<string>().SequenceEqual(new[] { "说话间隔：2–3 分钟", "说话间隔：3–5 分钟", "说话间隔：5–8 分钟", "说话间隔：10–16 分钟", "说话间隔：15–23 分钟", "说话间隔：20–30 分钟" }), "all six exact interval labels appear");
            frequency.SelectedIndex = 0; Pump();
            var remaining = ((DateTime)Field(window, "nextSpeech")! - DateTime.UtcNow).TotalMinutes;
            Check(remaining >= 1.99 && remaining <= 3, "changing range reschedules with the same selected bounds");
            prefs.ModelEnabled = true;
            brain.Stop();
            var warmup = brain.WarmUp();
            Check(brain.Busy, "real cold model load is in flight before test click");
            var watch = Stopwatch.StartNew();
            var task = (Task)Invoke(window, "SpeakCompanionTest")!;
            Check((bool)Field(window, "speechRequestPending")!, "test remains pending while real warmup is busy");
            Check(((FrameworkElement)Field(window, "loadingIndicator")!).Visibility == Visibility.Visible, "loading animation appears immediately");
            Check(ReferenceEquals(Field(window, "companionSettings"), settingsWindow), "open settings neither discard test nor close automatically");
            var duplicate = (Task)Invoke(window, "SpeakCompanionTest")!;
            Check(duplicate.IsCompleted && !task.IsCompleted, "repeated clicks coalesce into one existing request");
            double? first = null;
            Wait(() => {
                if (first == null && (bool)Field(window, "bubbleIsModelReply")!
                    && ((TextBlock)Field(window, "bubbleText")!).Text.Length > 0) first = watch.Elapsed.TotalSeconds;
                return task.IsCompleted;
            }, "real test finishes through production streaming handler", 45);
            task.GetAwaiter().GetResult(); warmup.GetAwaiter().GetResult();
            var text = ((TextBlock)Field(window, "bubbleText")!).Text;
            Check(text.Length > 0 && brain.Status == "本地模型就绪", "real model reply reaches the visible bubble");
            Console.WriteLine($"REAL BUBBLE first={first:0.000}s total={watch.Elapsed.TotalSeconds:0.000}s {text}");
            // A window disappearing between resolution and capture is a real capture failure.
            Set(window, "speechForegroundOverride", new IntPtr(-123));
            var failed = (Task)Invoke(window, "SpeechCountdownZeroAsync")!;
            Wait(() => failed.IsCompleted, "shared countdown pipeline generates real speech despite capture failure", 35);
            failed.GetAwaiter().GetResult();
            Check(DiagnosticLog.Snapshot().Any(x => x.Code == "ScreenReadFallback"), "fault injection reaches actual OCR fallback");
            var fallback = ((TextBlock)Field(window, "bubbleText")!).Text;
            Check(fallback.Length > 0 && brain.Status == "本地模型就绪", "capture failure still generates and displays actual local-model speech");
            Check(!fallback.Contains("未能") && !fallback.Contains("获取当前") && !fallback.Contains("读屏"), "fallback bubble does not report read-screen failure");
            Console.WriteLine("REAL FALLBACK " + fallback);
            Check(!(bool)Field(window, "speechRequestPending")! && ((FrameworkElement)Field(window, "loadingIndicator")!).Visibility != Visibility.Visible,
                "failure releases request gate and loading animation");
            var recovery = (Task)Invoke(window, "SpeakCompanionTest")!;
            Wait(() => recovery.IsCompleted, "test retries successfully after capture failure", 35);
            recovery.GetAwaiter().GetResult();
            Check(((TextBlock)Field(window, "bubbleText")!).Text.Length > 0 && brain.Status == "本地模型就绪", "subsequent real speech recovers without app restart");
            Console.WriteLine("REAL RECOVERY " + ((TextBlock)Field(window, "bubbleText")!).Text);
#if !COMPANION_SAFE
            prefs.AgentEnabled = true; prefs.AgentDailySpeech = true;
            var weather = Field(brain, "weather")!;
            var entries = (System.Collections.IDictionary)Field(weather, "entries")!;
            entries[prefs.WeatherRegionCode] = new CompanionWeather.Entry(prefs.WeatherRegionCode, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "PRIVATE-WEATHER-CONTEXT");
            Invoke(window, "ShowCompanionNotice", "unchanged existing bubble", false, true);
            string prior = ((TextBlock)Field(window, "bubbleText")!).Text;
            var handoff = (Task<bool>)Invoke(window, "TryManualAgentSpeech", true)!;
            Wait(() => handoff.IsCompleted, "Agent weather preparation completes without a connected queue", 15);
            Check(handoff.Result && ((TextBlock)Field(window, "bubbleText")!).Text == prior, "Agent weather queue failure has no intermediate/error bubble");
            Check(DiagnosticLog.Snapshot().Any(x => x.Code == "AgentMaterials" && x.Details.Contains("kind=weather") && x.Details.Contains("state=blocked")), "silent weather handoff failure remains visible in diagnostics");
            Check(!DiagnosticLog.ExportText().Contains("PRIVATE-WEATHER-CONTEXT"), "weather text is excluded from shareable logs");
            using var agentProbe = new BigBlueFish.Agent.AgentCoordinator(Path.Combine(root, "agent-probe"), Path.Combine(root, "absent-bridge.exe"), "isolated-console-test", (_, _) => Task.FromResult<string?>(null), Path.Combine(root, "agent-workspace"));
            agentProbe.Configure("codex", new(true, false, true));
            Set(window, "agent", agentProbe);
            var acceptedWeather = (Task<bool>)Invoke(window, "TryManualAgentSpeech", true)!;
            Wait(() => acceptedWeather.IsCompleted, "Agent weather enters the real bounded event queue", 15);
            Check(DiagnosticLog.Snapshot().Any(x => x.Code == "AgentMaterials" && x.Details.Contains("kind=weather") && x.Details.Contains("state=success")), "weather queue acceptance is recorded");
            Check(((TextBlock)Field(window, "bubbleText")!).Text == prior, "accepted weather handoff does not replace the bubble");
            agentProbe.Configure("codex", new(false, false, false)); Set(window, "agent", null!);
            entries.Clear();
            ((System.Collections.IDictionary)Field(weather, "attempts")!)[prefs.WeatherRegionCode] = DateTimeOffset.UtcNow;
            var unavailableWeather = (Task<bool>)Invoke(window, "TryManualAgentSpeech", true)!;
            Wait(() => unavailableWeather.IsCompleted, "unavailable weather returns through the silent error path", 15);
            Check(((TextBlock)Field(window, "bubbleText")!).Text == prior && DiagnosticLog.Snapshot().Any(x => x.Code == "AgentFailed" && x.Details.Contains("kind=weather")), "weather preparation errors appear only in diagnostics");
            prefs.AgentEnabled = false; prefs.AgentDailySpeech = false;
#endif
            Console.WriteLine($"RESULT {checks} interval/scale/shared speech checks passed; real GPU model, no canned speech");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            if (window != null) { Set(window, "exitReady", true); window.Close(); }
        }
    }
}
