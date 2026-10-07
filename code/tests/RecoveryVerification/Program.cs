using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Input;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using VPet_Simulator.Windows;

internal static class Program
{
    const BindingFlags Access = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static int checks;
    static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
        checks++;
    }

    static object Read(object obj, string name) => obj.GetType().GetField(name, Access)!.GetValue(obj)!;
    static object? Invoke(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Access)!.Invoke(obj, args);
    static void Set(object obj, string name, object value) => obj.GetType().GetProperty(name, Access)!.SetValue(obj, value);
    static T Property<T>(object obj, string name) => (T)obj.GetType().GetProperty(name, Access)!.GetValue(obj)!;
    static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T target) yield return target;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    sealed class WeatherFixture : HttpMessageHandler
    {
        internal int Requests;
        internal bool Fail;
        internal string LastUrl = "";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests++;
            LastUrl = request.RequestUri!.ToString();
            return Task.FromResult(new HttpResponseMessage(Fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"current":{"time":"2026-10-04T08:00","temperature_2m":21.5,"apparent_temperature":20.8,
                    "relative_humidity_2m":65,"weather_code":63,"wind_speed_10m":8.2},
                    "daily":{"temperature_2m_min":[18],"temperature_2m_max":[25],"precipitation_probability_max":[70]}}
                    """, Encoding.UTF8, "application/json")
            });
        }
    }

    static void VerifyWeather(Assembly rebuilt)
    {
        var regionsType = rebuilt.GetType("VPet_Simulator.Windows.CompanionRegions")!;
        var regions = ((System.Collections.IEnumerable)regionsType.GetField("All", Access)!.GetValue(null)!).Cast<object>().ToArray();
        Check(regions.Count(x => Property<int>(x, "Level") == 0) == 34, "Province coverage incomplete");
        Check(regions.Count(x => Property<int>(x, "Level") == 2) > 2800, "District coverage incomplete");
        var lookup = regions.ToDictionary(x => Property<string>(x, "Code"));
        Check(regions.Where(x => Property<int>(x, "Level") > 0).All(x => lookup.TryGetValue(Property<string>(x, "ParentCode"), out var parent)
            && Property<int>(parent, "Level") == Property<int>(x, "Level") - 1), "Invalid region parent chain");
        Check(regions.Where(x => Property<string>(x, "Code").StartsWith("34") && Property<int>(x, "Level") == 2)
            .All(x => x.GetType().GetProperty("Latitude")!.GetValue(x) != null), "Anhui coordinates incomplete");
        using var fixture = new WeatherFixture();
        using var client = new HttpClient(fixture);
        var weather = Activator.CreateInstance(rebuilt.GetType("VPet_Simulator.Windows.CompanionWeather")!, Access, null, new object[] { client }, null)!;
        string Context(string code, bool refresh) => ((Task<string>)Invoke(weather, "Context", code, refresh, CancellationToken.None)!).GetAwaiter().GetResult();
        string first = Context("340104", false);
        Check(first.Contains("蜀山区") && first.Contains("中雨") && first.Contains("21.5℃") && first.Contains("70%"), "Weather facts not parsed");
        Check(Context("340104", false) == first && fixture.Requests == 1, "Weather cache missed");
        string second = Context("110101", false);
        Check(second.Contains("东城区") && !second.Contains("合肥") && fixture.Requests == 2, "Old city's weather leaked after location change");
        Check(fixture.LastUrl.Contains("latitude=39.") && fixture.LastUrl.Contains("longitude=116."), "Forecast did not use selected coordinates");
        Context("110101", true);
        Check(fixture.Requests == 3, "Manual weather did not refresh");
        fixture.Fail = true;
        bool failed = false;
        try { Context("310115", false); } catch (HttpRequestException) { failed = true; }
        Check(failed, "Weather HTTP error hidden as valid weather");
        fixture.Fail = false;
        Check(Context("310115", false).Contains("浦东新区"), "Weather could not recover after an error");
        bool missing = false;
        try { Context("710101", false); } catch (IOException) { missing = true; }
        Check(missing, "Missing district coordinates silently replaced with another city");
    }

    static void VerifyScreenContext(Assembly rebuilt)
    {
        var native = rebuilt.GetType("VPet_Simulator.Windows.CompanionNative")!;
        var build = native.GetMethod("BuildForegroundContext", Access)!;
        var context = (string)build.Invoke(null, new object[]
        {
            "Code", "蓝色大肥鱼 0.3.0 新任务 - Visual Studio Code", "Chrome_WidgetWin_1",
            "蓝色大肥鱼-Git版", "ControlType.Document",
            new[] { "ControlType.TabItem：README.md", "ControlType.Document：正在编辑" }
        })!;
        Check(context.Contains("前台应用：Code") && context.Contains("窗口标题：蓝色大肥鱼 0.3.0 新任务 - Visual Studio Code"), "Foreground metadata was lost");
        Check(context.Contains("ControlType.TabItem：README.md") && context.Contains("ControlType.Document：正在编辑"), "Accessibility clues were lost");
        Check(context.Length < 1600, "Foreground context was not bounded");
        var brain = rebuilt.GetType("VPet_Simulator.Windows.CompanionBrain")!;
        var request = (string)brain.GetMethod("SpeechRequest", Access)!.Invoke(null, new object[] { context })!;
        Check(request.Contains(context) && request.Contains("本轮明确的画面内容"), "Speech request preserves current foreground context");
    }
    static void Pump()
    {
        if (Dispatcher.CurrentDispatcher.HasShutdownStarted)
            return;
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Send)
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    static void Wait(Func<bool> ready, int seconds, string message)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (!ready() && DateTime.UtcNow < until)
        {
            Pump();
            Thread.Sleep(25);
        }

        Check(ready(), message);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr h, out NativeRect r);
    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            Verify(args);
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            return 1;
        }
    }

    static void Verify(string[] args)
    {
        string source = Path.GetFullPath(args[0]), installed = Path.GetFullPath(args[1]);
        string sandbox = Path.Combine(source, ".verification");
        Directory.CreateDirectory(sandbox);
        Directory.SetCurrentDirectory(sandbox);
        File.Copy(Path.Combine(source, "VPet-Simulator.Windows", "vpeticon.ico"), Path.Combine(sandbox, "vpeticon.ico"), true);
        // The test uses separate preferences and never writes installation settings.
        File.WriteAllText("preferences.json", "{\"ModelEnabled\":false,\"PublicInfo\":false,\"ForegroundEnabled\":false,\"Scale\":0.65,\"Left\":400,\"Top\":300}");
        var rebuilt = typeof(App).Assembly;
        VerifyScreenContext(rebuilt);
        VerifyWeather(rebuilt);
        var gateType = rebuilt.GetType("VPet_Simulator.Windows.CompanionNewsGate")!;
        var start = new DateTime(2026, 10, 3, 8, 0, 0);
        string marker = Path.Combine(sandbox, "test-news-day.txt");
        File.Delete(marker);
        object Gate(DateTime t) => Activator.CreateInstance(gateType, Access, null, new object[] { t, marker }, null)!;
        void Speeches(object g)
        {
            for (int i = 0; i < 12; i++)
                Invoke(g, "RecordOrdinarySpeech");
        }

        var gate = Gate(start);
        Speeches(gate);
        Check(!(bool)Invoke(gate, "TryReserve", start.AddMinutes(119), 0.0)!, "News startup quiet period failed");
        Check(!(bool)Invoke(gate, "TryReserve", start.AddHours(2), 0.02)!, "News probability boundary failed");
        Check((bool)Invoke(gate, "TryReserve", start.AddHours(2), 0.019)!, "Eligible news rejected");
        Speeches(gate);
        Check(!(bool)Invoke(gate, "TryReserve", start.AddHours(5), 0.0)!, "Same-day news limit failed");
        var restarted = Gate(start.AddHours(6));
        Speeches(restarted);
        Check(!(bool)Invoke(restarted, "TryReserve", start.AddHours(9), 0.0)!, "Restart lost daily limit");
        Speeches(gate);
        Check((bool)Invoke(gate, "TryReserve", start.AddDays(1), 0.0)!, "Next-day news blocked");
        Check(!(bool)gateType.GetMethod("IsClearTitle", Access)!.Invoke(null, new object[] { "某旗舰即将发布：这次真的不一样" })!, "Vague headline accepted");
        App.Args = Array.Empty<string>();
        var app = new App();
        app.InitializeComponent();
        typeof(Application).GetField("_startupUri", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, null);
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Directory.SetCurrentDirectory(sandbox);
        var w = new MainWindow();
        w.Show();
        Wait(() => (bool)Read(w, "companionReady"), 30, "Animation load failed");
        Console.WriteLine("Animation loaded.");
        Read(w, "companionTimer").GetType().GetMethod("Stop")!.Invoke(Read(w, "companionTimer"), null);
        var preferences = Read(w, "preferences");
        var bubble = (Border)Read(w, "bubble");
        var text = (TextBlock)Read(w, "bubbleText");
        Invoke(w, "ShowCompanionBubble", "主人，天气正在查询～");
        Pump();
        var regressionPopup = w.OwnedWindows.Cast<Window>().Single();
        double shortHeight = regressionPopup.Height;
        string longWeather = "主人，云霄县现在大部晴朗，气温24.3度，体感挺热。今天最高能到28.5度，最低21.6度，降水概率为百分之十，出门可以带上水杯，记得补充水分哦。";
        var revealClock = System.Diagnostics.Stopwatch.StartNew();
        Invoke(w, "ShowCompanionBubble", longWeather);
        Pump();
        Check(regressionPopup.Height > shortHeight + 20, "Visible bubble retained the old short-text height; weather was clipped");
        var speech = Read(w, "speech");
        Check(Property<bool>(speech, "IsTyping") && text.Text.Length < longWeather.Length, "Speech did not reveal progressively");
        int initialCharacters = text.Text.Length;
        Wait(() => text.Text.Length > initialCharacters, 2, "Speech stalled after its initial fragment");
        double reservedHeight = regressionPopup.Height;
        Wait(() => !Property<bool>(speech, "IsTyping"), 8, "Speech took too long to finish");
        double revealSeconds = revealClock.Elapsed.TotalSeconds;
        Check(text.Text == longWeather && revealSeconds < 5, "Full reply was delayed or incomplete");
        Check(Math.Abs(regressionPopup.Height - reservedHeight) < 1, "Typing changed the reserved full-reply height");
        Check(regressionPopup.Height >= text.ActualHeight + bubble.Padding.Top + bubble.Padding.Bottom,
            "Final weather lines still clipped");
        DateTime dwellDeadline = (DateTime)Read(w, "bubbleUntil");
        Check((dwellDeadline - DateTime.UtcNow).TotalSeconds > 28, "Dwell timer started before speech finished");
        Invoke(w, "ShowCompanionBubble", "主人，鲸鱼👩‍💻小尾巴正在测试组合字符哦～");
        Pump();
        Invoke(w, "ShowCompanionBubble", "主人，替换后的短句～");
        Wait(() => !Property<bool>(speech, "IsTyping"), 4, "Replacement speech did not finish");
        Check(text.Text == "主人，替换后的短句～", "Old typing timer overwrote replacement speech");
        Console.WriteLine($"Typing regression: {longWeather.Length} characters in {revealSeconds:F2}s, full bubble reserved immediately.");
        var dpi = VisualTreeHelper.GetDpi(w);
        int corners = 0;
        foreach (double scale in new[]
        {
            0.65,
            1.0,
            1.6
        }

        )
        {
            preferences.GetType().GetProperty("Scale")!.SetValue(preferences, scale);
            Invoke(w, "ApplyCompanionScale", false);
            Pump();
            foreach (string sentence in new[]
            {
                "主人，我在这里～",
                string.Concat(Enumerable.Repeat("主人，认真工作的样子真棒～", 6))
            }

            )
            {
                Invoke(w, "ShowCompanionBubble", sentence);
                Pump();
                foreach (int corner in new[]
                {
                    0,
                    1,
                    2,
                    3
                }

                )
                {
                    var area = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(w).Handle).WorkingArea;
                    double size = 280 * scale;
                    double petLeft = (w.Width - size) / 2 + size * 37 / 220;
                    double petRight = (w.Width - size) / 2 + size * 183 / 220;
                    double petTop = w.Height - size + size * 15 / 220;
                    double petBottom = w.Height - size + size * 188 / 220;
                    w.Left = corner % 2 == 0 ? area.Left / dpi.DpiScaleX - petLeft - 30 : area.Right / dpi.DpiScaleX - petRight + 30;
                    w.Top = corner < 2 ? area.Top / dpi.DpiScaleY - petTop - 30 : area.Bottom / dpi.DpiScaleY - petBottom + 30;
                    Invoke(w, "ClampCompanion");
                    Pump();
                    var owned = w.OwnedWindows.Cast<Window>().ToArray();
                    if (owned.Length == 0)
                        throw new Exception("No owned bubble; parent=" + bubble.Parent + ", loaded=" + w.IsLoaded + ", visible=" + bubble.Visibility);
                    var popup = owned.Single();
                    Check(GetWindowRect(new WindowInteropHelper(popup).Handle, out var r), "Bubble native bounds unavailable");
                    Check(r.Left >= area.Left - 2 && r.Top >= area.Top - 2 && r.Right <= area.Right + 2 && r.Bottom <= area.Bottom + 2, "Bubble clipped at corner " + corner);
                    Check(popup.Width > 80 && popup.Height > 30 && text.ActualWidth > 30, "Bubble content has empty size");
                    Check(Math.Abs(w.Left + (corner % 2 == 0 ? petLeft : petRight) - (corner % 2 == 0 ? area.Left : area.Right) / dpi.DpiScaleX) < 1, "Horizontal docking failed");
                    Check(Math.Abs(w.Top + (corner < 2 ? petTop : petBottom) - (corner < 2 ? area.Top : area.Bottom) / dpi.DpiScaleY) < 1, "Vertical docking failed");
                    corners++;
                    Console.WriteLine("Corner case " + corners + " passed.");
                    if (scale == 0.65 && sentence.Length < 30)
                    {
                        var picture = new RenderTargetBitmap((int)Math.Ceiling(popup.Width * 2), (int)Math.Ceiling(popup.Height * 2), 192, 192, PixelFormats.Pbgra32);
                        picture.Render(bubble);
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(picture));
                        using var file = File.Create(Path.Combine(sandbox, $"bubble-corner-{corner}.png"));
                        encoder.Save(file);
                    }
                }
            }
        }

        var buttons = ((Grid)bubble.Child).Children.OfType<Button>().ToArray();
        Check(buttons.Length == 1, "Dismiss button missing");
        buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();
        Check(bubble.Visibility == Visibility.Collapsed, "Dismiss did not collapse bubble");
        Check(!Property<bool>(speech, "IsTyping"), "Dismiss did not cancel typing");
        Invoke(w, "ShowCompanionBubble", "主人，下一句也能正常出现～");
        Pump();
        Check(bubble.Visibility == Visibility.Visible, "Next bubble did not appear");
        w.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right)
            { RoutedEvent = UIElement.PreviewMouseRightButtonDownEvent });
        Pump();
        var contextMenu = (ContextMenu)Read(w, "companionContextMenu");
        Check(contextMenu.IsOpen && Read(w, "companionSettings") == null, "Right-click bypassed context menu");
        Check(contextMenu.Items.Cast<MenuItem>().Select(x => x.Header.ToString()).SequenceEqual(new[] { "设置", "天气", "吐字测试" }), "Right-click menu differs");
        ((MenuItem)contextMenu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Pump();
        var settings = (Window)Read(w, "companionSettings");
        Check(settings != null && !contextMenu.IsOpen, "Settings menu did not open settings");
        var combos = Descendants<ComboBox>(settings!).Where(x => x.Name.StartsWith("Weather")).ToDictionary(x => x.Name);
        Check(combos.Count == 3, "Three-level region selectors missing");
        combos["WeatherProvince"].SelectedValue = "11";
        Pump();
        Check((string)combos["WeatherCity"].SelectedValue == "1101" && (string)combos["WeatherDistrict"].SelectedValue == "110101", "Municipality cascade failed");
        combos["WeatherProvince"].SelectedValue = "31";
        combos["WeatherDistrict"].SelectedValue = "310115";
        Pump();
        Check(Property<string>(preferences, "WeatherRegionCode") == "310115", "District selection not applied");
        Check(File.ReadAllText("preferences.json").Contains("310115"), "District selection not saved");
        combos["WeatherProvince"].SelectedValue = "34";
        combos["WeatherCity"].SelectedValue = "3401";
        combos["WeatherDistrict"].SelectedValue = "340104";
        Pump();
        var batteryToggle = Descendants<CheckBox>(settings!).Single(x => x.Content.ToString() == "拔电时暂停并卸载模型");
        batteryToggle.IsChecked = false;
        batteryToggle.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
        Check(!Property<bool>(preferences, "StopModelOnBattery"), "Battery toggle did not update preference");
        using (var saved = JsonDocument.Parse(File.ReadAllText("preferences.json")))
            Check(!saved.RootElement.GetProperty("StopModelOnBattery").GetBoolean(), "Battery toggle not persisted");
        Pump();
        var image = new RenderTargetBitmap((int)settings!.ActualWidth, (int)settings.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(settings);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
        using (var stream = File.Create("settings.png")) png.Save(stream);
        settings.Close();
        Invoke(w, "OpenCompanionSettings");
        Pump();
        settings = (Window)Read(w, "companionSettings");
        Check(Descendants<ComboBox>(settings).Single(x => x.Name == "WeatherDistrict").SelectedValue.ToString() == "340104", "Reopen lost district selection");
        Check(Descendants<CheckBox>(settings).Single(x => x.Content.ToString() == "拔电时暂停并卸载模型").IsChecked == false, "Reopen lost battery setting");
        settings.Close();
        Pump();
        Invoke(w, "HideCompanion");
        Pump();
        Check(!w.IsVisible && !w.OwnedWindows.Cast<Window>().Any(x => x.IsVisible), "Hide did not hide bubble");
        Invoke(w, "RestoreCompanion");
        Pump();
        Check(w.IsVisible, "Restore failed");
        // Warm and generate through the same installed model, using separate runtime state.
        var brain = Read(w, "brain");
        var native = rebuilt.GetType("VPet_Simulator.Windows.CompanionNative")!;
        native.GetField("TestPowerOverride", Access)!.SetValue(null, false);
        Invoke(w, "ApplyCompanionPowerPolicy");
        Check(Property<bool>(brain, "CanRunOnCurrentPower"), "Disabled battery restriction still blocks model");
        Console.WriteLine("Warming model.");
        var warm = (System.Threading.Tasks.Task)Invoke(brain, "WarmUp")!;
        Wait(() => warm.IsCompleted, 90, "Model warm-up timed out");
        warm.GetAwaiter().GetResult();
        if ((string)brain.GetType().GetProperty("Status", Access)!.GetValue(brain)! != "本地模型已驻留")
        {
            var detail = (System.Threading.Tasks.Task)Invoke(brain, "EnsureRunner", CancellationToken.None)!;
            Wait(() => detail.IsCompleted, 90, "Detailed model load timed out");
            detail.GetAwaiter().GetResult();
            throw new Exception("Model did not become resident: " + brain.GetType().GetProperty("Status", Access)!.GetValue(brain));
        }

        Check(true, "Model is resident");
        var generated = (System.Threading.Tasks.Task<string>)Invoke(brain, "Generate", false, false)!;
        Wait(() => generated.IsCompleted, 100, "Generation timed out");
        var utterance = generated.GetAwaiter().GetResult();
        Check(utterance.Length > 0, "Battery-powered generation returned empty text");
        int pid = (int)brain.GetType().GetProperty("RunnerPid", Access)!.GetValue(brain)!;
        Invoke(w, "ApplyCompanionPowerPolicy");
        Check(Property<int?>(brain, "RunnerPid") == pid, "Battery restriction off unloaded resident model");
        Set(preferences, "ModelEnabled", true);
        ((MenuItem)contextMenu.Items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Wait(() => !(bool)Read(w, "weatherRequestPending"), 110, "Manual weather timed out");
        Wait(() => !Property<bool>(speech, "IsTyping"), 8, "Weather typing timed out");
        string weatherUtterance = text.Text;
        Check(weatherUtterance.Length > 10 && !weatherUtterance.Contains("暂时无法") && !weatherUtterance.Contains("正在查看")
            && System.Text.RegularExpressions.Regex.IsMatch(weatherUtterance, "[0-9一二三四五六七八九十百零两]"), "Weather menu did not produce natural-language weather: " + weatherUtterance);
        Check(Property<int?>(brain, "RunnerPid") == pid, "Weather did not reuse resident model");
        Console.WriteLine("Weather broadcast: " + weatherUtterance);
        Check(Property<bool>(brain, "UseGpu"), "GPU setting disabled during model test");
        ((MenuItem)contextMenu.Items[2]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check((bool)Read(w, "speechTestPending"), "Speech test bypassed model generation");
        Wait(() => !(bool)Read(w, "speechTestPending"), 110, "Model speech test timed out");
        Wait(() => !Property<bool>(speech, "IsTyping"), 8, "Model test typing timed out");
        string testUtterance = text.Text;
        Check(testUtterance.Length > 10 && !testUtterance.Contains("未能生成") && !testUtterance.Contains("正在等待"),
            "Speech test returned a placeholder rather than model text: " + testUtterance);
        Check(Property<int?>(brain, "RunnerPid") == pid, "Speech test did not use the resident GPU-configured model");
        Console.WriteLine("GPU model speech test: " + testUtterance);
        Set(preferences, "StopModelOnBattery", true);
        Invoke(w, "ApplyCompanionPowerPolicy");
        Check(!Property<bool>(brain, "CanRunOnCurrentPower"), "Enabled battery restriction did not block model");
        Wait(() =>
        {
            try
            {
                return System.Diagnostics.Process.GetProcessById(pid).HasExited;
            }
            catch (ArgumentException)
            {
                return true;
            }
        }, 10, "Model process survived power stop");
        var blocked = (Task<string>)Invoke(brain, "Generate", false, false)!;
        Check(blocked.GetAwaiter().GetResult() == "" && Property<int?>(brain, "RunnerPid") == null, "Battery restriction restarted model");
        native.GetField("TestPowerOverride", Access)!.SetValue(null, true);
        Invoke(w, "ApplyCompanionPowerPolicy");
        Check(Property<bool>(brain, "CanRunOnCurrentPower"), "Reconnect did not allow model");
        native.GetField("TestPowerOverride", Access)!.SetValue(null, null);
        w.Close();
        Pump();
        Check(!w.IsVisible, "Window did not close");
        var context = new AssemblyLoadContext("installed-reference", true);
        context.Resolving += (c, name) =>
        {
            string file = Path.Combine(installed, name.Name + ".dll");
            return File.Exists(file) ? c.LoadFromAssemblyPath(file) : null;
        };
        var original = context.LoadFromAssemblyPath(Path.Combine(installed, "VPet-Simulator.Windows.dll"));
        var oldBrain = original.GetType("VPet_Simulator.Windows.CompanionBrain")!;
        var newBrain = rebuilt.GetType(oldBrain.FullName!)!;
        var persona = (string)newBrain.GetField("Persona", Access)!.GetRawConstantValue()!;
        Check(persona.Contains("主体正文、编辑区、聊天记录") && persona.Contains("桌面、新标签页、主页"), "Persona distinguishes visible task content from entry screens");
        Check(Equals(oldBrain.GetField("NewsPersona", Access)!.GetRawConstantValue(), newBrain.GetField("NewsPersona", Access)!.GetRawConstantValue()), "NewsPersona differs from installed program");
        foreach (var name in new[]
        {
            "NewsRequest",
            "Clean"
        }

        )
            foreach (var value in new[]
            {
                "",
                "主人今天在写论文。",
                "<think>思考</think>主人，今天也加油～",
                new string ('?', 30),
                string.Concat(Enumerable.Repeat("主人，认真工作的样子真棒～", 20))
            }

            )
                Check(Equals(oldBrain.GetMethod(name, Access)!.Invoke(null, new[] { value }), newBrain.GetMethod(name, Access)!.Invoke(null, new[] { value })), name + " behavioral parity failed");

        var speechRequest = (string)newBrain.GetMethod("SpeechRequest", Access)!.Invoke(null, new[] { "前台应用：Code\n窗口标题：蓝色大肥鱼-Git版 - Visual Studio Code" })!;
        Check(speechRequest.Contains("蓝色大肥鱼-Git版 - Visual Studio Code") && speechRequest.Contains("已展开具体内容") && speechRequest.Contains("入口界面"), "SpeechRequest retains current context and scene guidance");
        File.WriteAllText(Path.Combine(sandbox, "result.json"), System.Text.Json.JsonSerializer.Serialize(new { passed = true, checks, corners,
            provinceCount = 34, modelGeneration = utterance, weatherGeneration = weatherUtterance,
            batteryPolicyBothModes = true, modelProcessExited = true, typingSeconds = revealSeconds,
            modelSpeechTest = testUtterance, gpuConfigured = true, installedReferenceHashes = "see provenance.json" }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS: {checks} checks; {corners} real-window corner layouts; both battery modes and weather broadcast passed.");
    }
}
