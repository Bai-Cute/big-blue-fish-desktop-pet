using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BigBlueFish.Control;
using BigBlueFish.Windows;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Brushes = System.Windows.Media.Brushes;

internal static class Program
{
    const BindingFlags Access = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static int checks, id;
    static MainWindow pet = null!;
    static object Read(string field) => (pet.GetType().GetField(field, Access)?.GetValue(pet) ?? pet.GetType().GetProperty(field, Access)!.GetValue(pet))!;
    static void Field(string field, object value) => pet.GetType().GetField(field, Access)!.SetValue(pet, value);
    static object? Invoke(string method, params object?[] values) => pet.GetType().GetMethod(method, Access)!.Invoke(pet, values);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out System.Drawing.Point point);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string? className, string title);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr WindowStyle(IntPtr window, int index);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left, Top, Right, Bottom; }
    static IntPtr Handle(Window window) => new System.Windows.Interop.WindowInteropHelper(window).Handle;
    static System.Windows.Point PetPointerTarget()
    {
        Wait(() => (long)Read("videoPlayer").GetType().GetProperty("Frames",Access)!.GetValue(Read("videoPlayer"))! > 3 && pet.IsVisible,8,"Visible decoded frame ready for physical pointer test");
        var bitmap=(BitmapSource)Read("videoPlayer").GetType().GetField("bitmap",Access)!.GetValue(Read("videoPlayer"))!;
        var pixels=new byte[640*360*4];bitmap.CopyPixels(pixels,640*4,0);
        var candidates=from y in Enumerable.Range(100,170) from x in Enumerable.Range(240,160)
                       where pixels[(y*640+x)*4+3]>220 && pixels[((y+3)*640+x+3)*4+3]>220 && pixels[((y-3)*640+x-3)*4+3]>220
                       orderby Math.Abs(x-320)+Math.Abs(y-190) select new System.Windows.Point(x,y);
        var pixel=candidates.First();var image=(System.Windows.Controls.Image)Read("videoImage");
        return image.PointToScreen(new System.Windows.Point(pixel.X*image.ActualWidth/640,pixel.Y*image.ActualHeight/360));
    }
    static void ClickFixture(IntPtr handle)
    {
        GetCursorPos(out var old);
        try {
            SetForegroundWindow(handle); Pump(60);
            if (!GetWindowRect(handle, out var bounds)) throw new Exception("Fixture window missing");
            Console.WriteLine($"CLICK fixture bounds={bounds.Left},{bounds.Top},{bounds.Right},{bounds.Bottom}, visible={IsWindowVisible(handle)}, minimized={IsIconic(handle)}");
            SetCursorPos(bounds.Left + 70, bounds.Top + 65); Pump(30);
            mouse_event(2,0,0,0,UIntPtr.Zero); Pump(20); mouse_event(4,0,0,0,UIntPtr.Zero); Pump(100);
        } finally { SetCursorPos(old.X, old.Y); }
        Check(GetForegroundWindow() == handle, $"Actual click focuses an independent external process (expected={handle}, actual={GetForegroundWindow()})");
    }
    static void OpenSettingsByMouse()
    {
        GetCursorPos(out var old);
        try {
            var point = PetPointerTarget();
            SetCursorPos((int)point.X,(int)point.Y); Pump(30);
            mouse_event(8,0,0,0,UIntPtr.Zero); Pump(20); mouse_event(16,0,0,0,UIntPtr.Zero); Pump(150);
            var menu = (ContextMenu)Read("companionContextMenu");
            Check(menu.IsOpen, "Real right-click opens pet menu");
            var item = menu.Items.Cast<MenuItem>().Single(i => i.Header?.ToString() == "设置");
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump(200);
            Check(!menu.IsOpen, "Settings menu command closes the menu");
        } finally { SetCursorPos(old.X,old.Y); }
    }
    static void VerifySettingsFocus(Client client)
    {
        using var other = Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, ArgumentList = { "--focus-fixture" } })!;
        try {
            Wait(() => { var found=FindWindow(null,"MCP 设置焦点 · 独立应用"); return found!=IntPtr.Zero && IsWindowVisible(found) && !IsIconic(found) && GetWindowRect(found,out var rect) && rect.Right-rect.Left>200; }, 10, "Independent ordinary window running");
            var external = FindWindow(null,"MCP 设置焦点 · 独立应用");
            Pump(300);
            ClickFixture(external);
            client.Data("update_settings", new { settings = new { AnimationIntervalSeconds = 60 } }); Pump(100);
            Check(pet.GetType().GetField("companionSettings", Access)!.GetValue(pet) == null && GetForegroundWindow() == external, "MCP updates while settings closed create no settings window and keep foreground");
            Invoke("OpenCompanionSettings"); Pump(150);
            var window = (Window)Read("companionSettings"); var handle = Handle(window);
            Descendants<Button>(window).Single(b => b.Content?.ToString() == "说话与天气").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(100);
            var scroll = Descendants<ScrollViewer>(window).First(v => v.Visibility == Visibility.Visible && v.Content is StackPanel);
            scroll.ScrollToVerticalOffset(70); Pump(100); double offset = scroll.VerticalOffset;
            window.Left = Math.Max(SystemParameters.WorkArea.Left + 400, window.Left); double left = window.Left, top = window.Top;
            ClickFixture(external);
            for (int i=0;i<12;i++) {
                client.Data("update_settings", new { settings = new { Scale = .85 + .01 * i, BubbleScale = 1.2, AnimationIntervalSeconds = 60 + i } }); Pump(80);
                Check(ReferenceEquals(Read("companionSettings"), window) && Handle(window) == handle && GetForegroundWindow() == external && !window.IsActive,
                    "Repeated MCP update keeps the original settings window in background " + i);
                Check(!window.Topmost && (WindowStyle(handle,-20).ToInt64() & 8) == 0 && window.IsEnabled,
                    "Native settings window remains non-topmost and enabled " + i);
            }
            var currentScroll = Descendants<ScrollViewer>(window).First(v => v.Visibility == Visibility.Visible && v.Content is StackPanel);
            Check(currentScroll.Content is StackPanel panel && panel.Children.OfType<TextBlock>().First().Text == "说话与天气" && Math.Abs(currentScroll.VerticalOffset-offset)<1,
                "Remote refresh keeps selected page and scroll position");
            Check(window.Left == left && window.Top == top, "Remote refresh keeps window location");
            Check(Descendants<Slider>(window).Single(s => System.Windows.Automation.AutomationProperties.GetName(s) == "文字气泡大小").Value == 1.2,
                "Remote value appears in existing controls");
            window.WindowState = WindowState.Minimized; Pump(100); ClickFixture(external);
            client.Data("update_settings", new { settings = new { SpeechMinMinutes = 5 } }); Pump(120);
            Check(window.WindowState == WindowState.Minimized && GetForegroundWindow() == external && Handle(window) == handle, "Remote update does not restore minimized settings");
            window.WindowState = WindowState.Maximized; Pump(100); ClickFixture(external);
            client.Data("update_settings", new { settings = new { AnimationIntervalSeconds = 70 } }); Pump(120);
            Check(window.WindowState == WindowState.Maximized && GetForegroundWindow() == external && Handle(window) == handle, "Remote update preserves maximized settings without activating");
            ClickFixture(external); Check((WindowStyle(handle,-20).ToInt64() & 8) == 0, "Other application remains usable after repeated updates");
            window.WindowState = WindowState.Minimized; Pump(); OpenSettingsByMouse();
            Check(window.WindowState == WindowState.Maximized && GetForegroundWindow() == handle, "Explicit menu opening restores the previously maximized settings and activates it");
            window.WindowState = WindowState.Normal; window.WindowState = WindowState.Minimized; Pump(); OpenSettingsByMouse();
            Check(window.WindowState == WindowState.Normal && GetForegroundWindow() == handle, "Explicit menu opening restores previously normal settings and activates it");
            Check(pet.Topmost && !pet.ShowActivated && (WindowStyle(Handle(pet),-20).ToInt64() & 0x08000008) == 0x08000008, "Pet keeps its own topmost non-activating policy");
            window.Close();
        } finally { if (!other.HasExited) { other.CloseMainWindow(); if (!other.WaitForExit(2000)) other.Kill(); } }
    }
    static void DoubleClickPet()
    {
        GetCursorPos(out var old);
        try {
            var point = PetPointerTarget();
            SetCursorPos((int)point.X, (int)point.Y); Pump(50);
            for (int i=0;i<2;i++) { mouse_event(2,0,0,0,UIntPtr.Zero); Pump(20); mouse_event(4,0,0,0,UIntPtr.Zero); Pump(80); }
        } finally { SetCursorPos(old.X, old.Y); }
    }
    static void DragPetByMouse()
    {
        GetCursorPos(out var old); double left=pet.Left, top=pet.Top;
        try {
            var start=PetPointerTarget();
            SetCursorPos((int)start.X,(int)start.Y); Pump(50); mouse_event(2,0,0,0,UIntPtr.Zero); Pump(50);
            SetCursorPos((int)start.X-50,(int)start.Y-25); Pump(150); mouse_event(4,0,0,0,UIntPtr.Zero); Pump(120);
            Check(Math.Abs(pet.Left-left)+Math.Abs(pet.Top-top)>5,"Real mouse drag moves pet while consent is pending");
        } finally { mouse_event(4,0,0,0,UIntPtr.Zero); SetCursorPos(old.X,old.Y); }
    }
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; Console.WriteLine($"PASS {checks}: {message}"); }
    static void Pump(int milliseconds = 30)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
    static T Complete<T>(Task<T> task, int seconds = 15)
    {
        var clock = Stopwatch.StartNew(); while (!task.IsCompleted && clock.Elapsed.TotalSeconds < seconds) Pump();
        if (!task.IsCompleted) throw new TimeoutException(); return task.GetAwaiter().GetResult();
    }
    static void Wait(Func<bool> condition, int seconds, string label)
    { var clock = Stopwatch.StartNew(); while (!condition() && clock.Elapsed.TotalSeconds < seconds) Pump(); Check(condition(), label); }
    static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is T t) yield return t; foreach (var nested in Descendants<T>(child)) yield return nested; } }
    static void Render(Visual root, string path, int width, int height)
    { var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render(root); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(path); encoder.Save(file); }
    sealed class Client : IDisposable
    {
        readonly NamedPipeClientStream pipe = new(".", ControlTools.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        readonly StreamReader reader; readonly StreamWriter writer;
        internal readonly List<JsonElement> Events = [];
        internal Client()
        {
            Complete(pipe.ConnectAsync(5000).ContinueWith(t => { t.GetAwaiter().GetResult(); return true; }));
            reader = new(pipe, new UTF8Encoding(false)); writer = new(pipe, new UTF8Encoding(false)) { AutoFlush = true };
            Request("initialize", new { protocolVersion = "2025-11-25", capabilities = new { }, clientInfo = new { name = "WPF verification", version = "1" } });
            Send("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
        }
        void Send(string line) => Complete(Task.Run(() => { writer.WriteLine(line); return true; }));
        internal JsonElement Request(string method, object args)
        {
            int request = ++id;
            Send(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = request, method, @params = args }));
            while (true)
            {
                var line = Complete(reader.ReadLineAsync()) ?? throw new IOException("MCP disconnected");
                using var doc = JsonDocument.Parse(line); var result = doc.RootElement.Clone();
                if (result.TryGetProperty("id", out var found) && found.GetInt32() == request) return result.GetProperty("result");
                Events.Add(result);
            }
        }
        internal JsonElement Call(string name, object? args = null) => Request("tools/call", new { name, arguments = args ?? new { } });
        internal JsonElement Data(string name, object? args = null)
        {
            var result = Call(name, args);
            if (result.TryGetProperty("isError", out var error) && error.GetBoolean()) throw new Exception(result.ToString());
            return JsonDocument.Parse(result.GetProperty("content")[0].GetProperty("text").GetString()!).RootElement.Clone();
        }
        public void Dispose() { pipe.Dispose(); }
    }
    [STAThread] static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--focus-fixture") {
            var fixtureApp = new System.Windows.Application();
            fixtureApp.Run(new Window { Title = "MCP 设置焦点 · 独立应用", Left = SystemParameters.WorkArea.Left+15, Top = SystemParameters.WorkArea.Top+15, Width = 340, Height = 180,
                Content = new TextBlock { Text = "普通应用：检查设置窗口不会抢占前台", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20) } }); return 0;
        }
        string sandbox = Path.GetFullPath(args[0]); Directory.CreateDirectory(sandbox); Environment.CurrentDirectory = sandbox;
        File.Copy(Path.Combine(AppContext.BaseDirectory, "BigBlueFish.ico"), Path.Combine(sandbox, "BigBlueFish.ico"), true);
        File.WriteAllText("preferences.json", "{\"ModelEnabled\":false,\"CheckUpdatesOnStartup\":false,\"PublicInfo\":false}");
        App.Args = ["--test-mode"]; var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; Environment.CurrentDirectory = sandbox;
        try
        {
            pet = new MainWindow(); pet.Show(); Wait(() => (bool)Read("companionReady"), 8, "Actual video initialized");
            Invoke("OpenCompanionSettings"); Pump(150);
            var settings = (Window)Read("companionSettings");
            Descendants<Button>(settings).Single(b => b.Content?.ToString() == "Agent 与 MCP").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            var toggle = Descendants<CheckBox>(settings).Single(c => c.Content?.ToString() == "开启 MCP 接口");
            bool sawWarning = false;
            var dialogTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            dialogTimer.Tick += (_, _) => {
                var dialog = app.Windows.Cast<Window>().FirstOrDefault(w => w.Title == "开启 MCP 接口"); if (dialog == null) return;
                sawWarning = Descendants<TextBlock>(dialog).Any(t => t.Text == ControlTools.PrivacyWarning);
                Render((Visual)dialog.Content, "mcp-warning.png", (int)dialog.ActualWidth, (int)dialog.ActualHeight);
                Descendants<Button>(dialog).Single(b => b.Content?.ToString() == "取消").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            dialogTimer.Start(); toggle.IsChecked = true; toggle.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            Wait(() => !(bool)Read("mcpConsentOpen"),5,"Asynchronous privacy confirmation finishes after cancellation"); dialogTimer.Stop();
            Check(sawWarning && toggle.IsChecked == false, "Privacy warning shown; cancellation leaves MCP off");
            var prefs = Read("preferences"); prefs.GetType().GetProperty("McpEnabled")!.SetValue(prefs, true); Invoke("InitializeMcp");
            prefs.GetType().GetProperty("McpChatEnabled")!.SetValue(prefs, true);
            Check(!(bool)Invoke("TryOpenMcpInput")!, "Chat input cannot open without a real initialized client");
            prefs.GetType().GetProperty("McpChatEnabled")!.SetValue(prefs, false);
            settings.Close(); Pump();
            using (var client = new Client())
            {
                var state = client.Data("get_settings"); Check(state.GetProperty("settings").GetProperty("McpEnabled").GetBoolean(), "MCP settings state");
                Check(client.Data("list_actions").GetArrayLength() == 106, "All 106 actions exposed");
                if (args.Contains("--settings-focus-only")) { VerifySettingsFocus(client); Field("exitReady", true); pet.Close(); Console.WriteLine($"MCP settings focus: {checks} checks passed."); return 0; }
                Check(!(bool)Invoke("TryOpenMcpInput")!, "Chat feature is off by default even when MCP is connected");
                client.Data("update_settings", new { settings = new { McpChatEnabled = true } });
                DoubleClickPet();
                Wait(() => pet.GetType().GetField("mcpInput", Access)!.GetValue(pet) != null, 4, "Actual double-click opens input with connected enabled MCP chat");
                var inputWindow = (Window)Read("mcpInput"); Pump(150);
                var input = Descendants<TextBox>(inputWindow).Single();
                const string hint = "通过鱼鱼和已连接的Agent说话";
                var placeholder = Descendants<TextBlock>(inputWindow).Single(t => t.Text == hint);
                Check(inputWindow.WindowStyle == System.Windows.WindowStyle.None && !inputWindow.ShowInTaskbar && inputWindow.Owner == pet && inputWindow.Topmost, "Input is a frameless pet attachment, without independent title bar or taskbar button");
                Check(inputWindow.Width <= 354 && inputWindow.Height <= 120 && placeholder.IsVisible && placeholder.Foreground == Brushes.Gray, "Compact input shows the exact gray placeholder while empty");
                Check(GetForegroundWindow() == Handle(inputWindow) && input.IsKeyboardFocused, "Explicit double-click gives only the editor keyboard focus");
                Render((Visual)inputWindow.Content, "mcp-input-empty.png", (int)inputWindow.ActualWidth, (int)inputWindow.ActualHeight);
                var area = CompanionEdgeLayout.WorkArea(pet);
                foreach (var scale in new[] { .5, 1d, 2d })
                    foreach (var corner in new[] { (area.Left, area.Top), (area.Right, area.Top), (area.Left, area.Bottom), (area.Right, area.Bottom) }) {
                        client.Data("update_settings", new { settings = new { Scale = scale, Left = corner.Item1, Top = corner.Item2 } }); Pump(100);
                        area = CompanionEdgeLayout.WorkArea(pet);
                        Check(inputWindow.Left >= area.Left && inputWindow.Top >= area.Top && inputWindow.Left+inputWindow.Width <= area.Right+1 && inputWindow.Top+inputWindow.Height <= area.Bottom+1, "Attached input fits work area at scale=" + scale + " corner=" + corner);
                    }
                client.Data("update_settings", new { settings = new { Scale = 1d, Left = area.Right-800, Top = area.Bottom-450 } }); Pump(100);
                double beforeX = inputWindow.Left, beforeY = inputWindow.Top;
                DragPetByMouse(); Pump(100);
                Check(Math.Abs(inputWindow.Left-beforeX)+Math.Abs(inputWindow.Top-beforeY)>5, "Input follows the actual dragged pet");
                input.Text = "暂存草稿"; Invoke("HideCompanion"); Pump(100);
                Check(!inputWindow.IsVisible && input.Text == "暂存草稿", "Pet hiding hides the attached input and retains draft");
                Invoke("RestoreCompanion"); Pump(150); inputWindow.Activate(); input.Focus();
                Check(inputWindow.IsVisible && input.Text == "暂存草稿", "Showing the pet restores the same attached input");
                const string submitted = "主人输入的原文\n第二行：ABC <> &，都原样传递~";
                input.Text = submitted;
                Pump(50);
                Check(!placeholder.IsVisible, "Placeholder disappears while typing");
                Render((Visual)inputWindow.Content, "mcp-input.png", (int)inputWindow.ActualWidth, (int)inputWindow.ActualHeight);
                keybd_event(0x11,0,0,UIntPtr.Zero); keybd_event(0x0D,0,0,UIntPtr.Zero); Pump(30); keybd_event(0x0D,0,2,UIntPtr.Zero); keybd_event(0x11,0,2,UIntPtr.Zero); Pump(100);
                client.Data("get_settings");
                Check(client.Events.Any(e => e.TryGetProperty("method", out var method) && method.GetString() == "notifications/message" && e.GetProperty("params").GetProperty("data").GetProperty("kind").GetString() == "user_message" && e.GetProperty("params").GetProperty("data").GetProperty("detail").GetProperty("text").GetString() == submitted), "User message pushed through actual MCP unchanged");
                Check(input.Text.Length == 0 && placeholder.IsVisible, "Actual Ctrl+Enter sends, clears input and restores placeholder");
                Check(pet.Topmost && !pet.ShowActivated && (WindowStyle(Handle(pet),-20).ToInt64() & 0x08000008) == 0x08000008, "Typing preserves pet topmost non-activating policy");
                keybd_event(0x1B,0,0,UIntPtr.Zero); keybd_event(0x1B,0,2,UIntPtr.Zero); Pump(100);
                Check(pet.GetType().GetField("mcpInput",Access)!.GetValue(pet) == null, "Escape closes the attached editor");
                if (args.Contains("--input-only")) { Field("exitReady", true); pet.Close(); Console.WriteLine($"Input UI: {checks} checks passed."); return 0; }
                var denied = client.Call("update_settings", new { settings = new { McpEnabled = false, Scale = 2 } });
                Check(denied.GetProperty("isError").GetBoolean(), "Agent cannot disable MCP");
                Check(client.Data("get_settings").GetProperty("settings").GetProperty("Scale").GetDouble() == 1, "Denied patch applied no fields");
                client.Data("update_settings", new { settings = new { BubbleScale = 1.3, Scale = .85, AnimationIntervalSeconds = 70 } });
                Check(File.ReadAllText("preferences.json").Contains("1.3"), "Settings persisted");
                const string raw = "主人，MCP传来的原文：新衣服、标点 & <>，都应原样显示~";
                client.Data("say", new { text = raw });
                Wait(() => ((TextBlock)Read("bubbleText")).Text == raw, 5, "External text displayed exactly without model");
                Check((bool)Read("externalBubble"), "External bubble is separate from model result");
                Render((Visual)Read("companionLayout"), "mcp-pet.png", (int)pet.Width, (int)pet.Height);
                Check(client.Data("get_settings").GetProperty("state").GetProperty("modelBusy").GetBoolean() == false, "No local inference for external text");
                var answer = AnswerPuppet(false, verifyDrag: true);
                var unconfirmed = client.Call("update_settings", new { settings = new { PuppetMode = true } }); answer.Stop();
                Check(unconfirmed.GetProperty("isError").GetBoolean(), "Agent-enabled puppet shows warning; cancellation leaves mode off");
                answer = AnswerPuppet(true); client.Data("update_settings", new { settings = new { PuppetMode = true } }); answer.Stop();
                Check((string)Read("currentAction") == "待机呼吸休闲", "Puppet starts fixed idle");
                Field("speechRequestPending", true); Field("weatherRequestPending", true);
                Field("dragging", true); Field("lastClickAt", DateTime.UtcNow);
                var beforeRecovery = (long)Read("speechRequestVersion");
                Invoke("RecoverCompanionSession"); Pump(100);
                Check(!(bool)Read("speechRequestPending") && !(bool)Read("weatherRequestPending") && !(bool)Read("dragging") && !pet.IsMouseCaptured,
                    "Resume recovery clears abandoned speech, weather and drag state");
                Check((long)Read("speechRequestVersion") > beforeRecovery && (DateTime)Read("lastClickAt") == DateTime.MinValue,
                    "Resume recovery invalidates stale completions and double-click state");
                Check((string)Read("currentAction") == "待机呼吸休闲" && pet.IsVisible,
                    "Resume recovery preserves fixed puppet idle and visible pet");
                DragPetByMouse(); Check((string)Read("currentAction") == "待机呼吸休闲","Actual drag preserves puppet idle");
                Invoke("ClickAnimation"); Check((string)Read("currentAction") == "待机呼吸休闲", "Click cannot switch puppet animation");
                var context = (ContextMenu)Read("companionContextMenu");
                Check(!((MenuItem)context.Items[1]).IsEnabled && !((MenuItem)context.Items[2]).IsEnabled, "Weather and speech-test disabled");
                client.Data("say_next", new { text = "下次节奏原文" }); Field("nextSpeech", DateTime.UtcNow.AddMinutes(-1)); Pump(300);
                Check(((TextBlock)Read("bubbleText")).Text == raw, "Puppet never consumes scheduled speech");
                client.Data("interrupt_action", new { action = "点击回应-元气挥手" }); Pump(250);
                Check((string)Read("currentAction") == "点击回应-元气挥手", "Immediate action starts");
                Invoke("FinishDragAnimation");
                Check((string)Read("currentAction") == "点击回应-元气挥手", "Puppet drag release does not truncate externally commanded action");
                client.Data("play_action", new { action = "吃白饭" });
                Check((string)Read("currentAction") == "点击回应-元气挥手", "Queued action does not truncate active clip");
                Wait(() => (string)Read("currentAction") == "吃白饭", 13, "Queued action starts at actual decoder completion");
                Wait(() => (string)Read("currentAction") == "待机呼吸休闲", 13, "Puppet returns to fixed idle after command");
                Wait(() => (long)Read("videoPlayer").GetType().GetProperty("Frames", Access)!.GetValue(Read("videoPlayer"))! >= 20, 3, "Puppet idle produces real frames");
                long frames = (long)Read("videoPlayer").GetType().GetProperty("Frames", Access)!.GetValue(Read("videoPlayer"))!;
                Wait(() => (long)Read("videoPlayer").GetType().GetProperty("Frames", Access)!.GetValue(Read("videoPlayer"))! < frames, 13, "Puppet idle loops at full clip boundary");
                Check((string)Read("currentAction") == "待机呼吸休闲", "Loop never chooses another idle or event");
                Invoke("PublishMcpInteraction", "click", new { button = "left" });
                Invoke("PublishMcpInteraction", "drag_start", new { Left = pet.Left });
                Invoke("PublishMcpInteraction", "drag_end", new { Left = pet.Left });
                client.Data("get_settings"); Check(client.Events.Count(e => e.TryGetProperty("method", out var method) && method.GetString() == "notifications/message") >= 3, "Click/drag automatically pushed on MCP connection");
                using var fixture = new Fixture(); fixture.Window.Show(); fixture.Window.Activate(); Pump(200);
                Field("lastExternalForeground", new System.Windows.Interop.WindowInteropHelper(fixture.Window).Handle);
                foreach (var name in new[] { "capture_foreground", "capture_screen" })
                {
                    var capture = client.Call(name, new { max_dimension = 1280 });
                    Check(capture.GetProperty("content")[1].GetProperty("mimeType").GetString() == "image/png", name + " returns image content");
                    var bytes = Convert.FromBase64String(capture.GetProperty("content")[1].GetProperty("data").GetString()!);
                    Check(bytes.Length > 1000 && bytes[0] == 137, name + " real PNG bytes");
                    File.WriteAllBytes(name + ".png", bytes);
                }
                client.Data("update_settings", new { settings = new { PuppetMode = false } });
                Check(((MenuItem)context.Items[1]).IsEnabled && ((MenuItem)context.Items[2]).IsEnabled, "Normal menu restored");
                Check((bool)Invoke("ConsumeMcpSpeech")!, "Queued original text available on return to cadence");
                Wait(() => ((TextBlock)Read("bubbleText")).Text == "下次节奏原文", 3, "Scheduled MCP text exact");
                answer = AnswerPuppet(true); client.Data("update_settings", new { settings = new { PuppetMode = true } }); answer.Stop();
                Invoke("TryOpenMcpInput"); Pump(100);
                Descendants<TextBox>((Window)Read("mcpInput")).Single().Text = "断线时保留这条草稿";
            }
            Pump(600);
            var disconnectedInput = (Window)Read("mcpInput");
            Check(!Descendants<Button>(disconnectedInput).Single(b => b.Content?.ToString() == "发送").IsEnabled, "Disconnect disables sending in the already-open input");
            Check(Descendants<TextBox>(disconnectedInput).Single().Text == "断线时保留这条草稿", "Disconnect preserves unsent draft");
            Check(!(bool)Invoke("TryOpenMcpInput")!, "Disconnected double-click cannot open chat");
            Check((bool)pet.GetType().GetProperty("PuppetActive", Access)!.GetValue(pet)! && (string)Read("currentAction") == "待机呼吸休闲", "Disconnect keeps puppet mode and fixed idle");
            Invoke("SetPuppetMode", false); Invoke("StopMcp");
            Check((string)Read("animationPhase") == "Idle", "Explicitly leaving puppet restores ordinary idle selection");
            Field("exitReady", true); pet.Close();
            Console.WriteLine($"MCP actual WPF integration: {checks} checks passed. {sandbox}"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); if (pet != null) { Field("exitReady", true); pet.Close(); } return 1; }
    }
    static DispatcherTimer AnswerPuppet(bool accept, bool verifyDrag = false)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        timer.Tick += (_, _) => {
            var dialog = System.Windows.Application.Current.Windows.Cast<Window>().FirstOrDefault(w => w.Title == "开启提线木偶模式");
            if (dialog == null) return;
            Check(Descendants<TextBlock>(dialog).Any(t => t.Text == ControlTools.PuppetWarning), "Actual puppet warning displayed");
            Render((Visual)dialog.Content, "puppet-warning.png", (int)dialog.ActualWidth, (int)dialog.ActualHeight);
            timer.Stop();
            Check(IsWindowEnabled(Handle(pet)) && IsWindowEnabled(Handle(dialog)),"Pending consent keeps native pet and notice windows enabled");
            if (verifyDrag) DragPetByMouse();
            Descendants<Button>(dialog).Single(b => b.Content?.ToString() == (accept ? "了解并开启" : "取消")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        timer.Start(); return timer;
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly Window Window = new() { Title = "MCP前台截图验证 · 1001", Width = 640, Height = 420, Background = Brushes.LimeGreen, Content = new TextBlock { Text = "蓝色大肥鱼 MCP 截图验证\n前台内容编号：1001", FontSize = 30, Margin = new Thickness(30) } };
        public void Dispose() => Window.Close();
    }
}
