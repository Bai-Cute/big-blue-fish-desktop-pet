using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BigBlueFish.Windows;
using Application = System.Windows.Application;
using Brushes = System.Windows.Media.Brushes;
using Size = System.Windows.Size;

internal static class NewsVerification
{
    static int checks;
    static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        checks++; Console.WriteLine("PASS " + message);
    }

    internal static int Rules()
    {
        Check(CompanionNews.ShouldSelect(0) && CompanionNews.ShouldSelect(.029999)
            && !CompanionNews.ShouldSelect(.03) && !CompanionNews.ShouldSelect(.99), "three-percent selection boundaries");
        Check(CompanionNews.ShouldSelect(0) && CompanionNews.ShouldSelect(0), "consecutive opportunities are independent without calendar or speech-count gates");
        int selected = Enumerable.Range(0, 1_000_000).Count(i => CompanionNews.ShouldSelect(i / 1_000_000d));
        Check(selected == 30_000, "exactly 3 percent across stratified random inputs");
        var random = new Random(20261014);
        selected = Enumerable.Range(0, 1_000_000).Count(_ => CompanionNews.ShouldSelect(random.NextDouble()));
        Check(Math.Abs(selected - 30_000) < 1000, "seeded million-opportunity Monte Carlo agrees with 3 percent");
        Console.WriteLine($"MONTE CARLO {selected}/1000000");
        string item(string title, string date, string summary = "摘要") =>
            $"<item><title>{title}</title><pubDate>{date}</pubDate><description><![CDATA[{summary}]]></description></item>";
        var now = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
        var feed = "<rss><channel>" + item("新芯片疑似曝光？", now.ToString("r"), "<p>原文<b>传闻</b> &amp; 摘要</p><script>bad()</script>")
            + item("新芯片疑似曝光？", now.ToString("r")) + item("短标题", now.ToString("r"))
            + item("未来文章", now.AddHours(1).ToString("r")) + item("过期文章", now.AddDays(-3).ToString("r"))
            + item("", now.ToString("r")) + "</channel></rss>";
        var items = CompanionNews.ReadFeed(feed, now);
        Check(items.Length == 2 && items.Any(i => i.Title == "短标题"), "headlines no longer require length, keywords or certainty filters; freshness and deduplication retained");
        Check(items[0].Summary == "原文 传闻 & 摘要" && items[0].Context.Contains("IT之家"), "RSS summary is readable plain text with source and date");
        var messages = CompanionBrain.BuildMessages(CompanionBrain.NewsPersona, CompanionBrain.NewsRequest(items[0].Context));
        Check(messages.Length == 2 && messages[0].role == "system" && messages[1].role == "user", "news uses standard system and user roles");
        Check(!((string)messages[0].content).Contains("语气演示") && !((string)messages[0].content).Contains("新衣服")
            && !((string)messages[0].content).Contains("屏幕资料"), "news system prompt is independent from ordinary speech examples and screen guidance");
        Check(((string)messages[1].content).Contains(items[0].Summary), "news request preserves factual summary");
        string longer = "主人，" + string.Concat(Enumerable.Repeat("一条新闻", 30)) + "。请关注后续消息。";
        Check(CompanionBrain.Clean(longer).Length < 100,
            "news retains the ordinary bubble safety length limit");
        Console.WriteLine($"RESULT {checks} news rule and message checks passed");
        return 0;
    }

    const BindingFlags Access = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static object Field(object target, string name) => target.GetType().GetField(name, Access)!.GetValue(target)!;
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Access)!.SetValue(target, value);
    static object? Invoke(object target, string name) => target.GetType().GetMethod(name, Access)!.Invoke(target, null);
    static void Pump(int milliseconds = 25)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Send) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    static void Wait(Func<bool> condition, string message, int seconds = 110)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.Elapsed.TotalSeconds < seconds) Pump();
        Check(condition(), message);
    }
    static void Save(Visual visual, double width, double height, string path, double dpi = 96)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * dpi / 96), (int)Math.Ceiling(height * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    internal static Task<int> Live(string root)
    {
        var result = new TaskCompletionSource<int>();
        var thread = new Thread(() =>
        {
            MainWindow? window = null;
            try
            {
                root = Path.GetFullPath(root);
                var app = new App(); app.InitializeComponent();
                typeof(Application).GetField("_startupUri", Access)!.SetValue(app, null);
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                Directory.SetCurrentDirectory(root);
                File.Copy(Path.Combine(AppContext.BaseDirectory, "BigBlueFish.ico"), "BigBlueFish.ico", true);
                File.WriteAllText("preferences.json", "{\"ModelEnabled\":false,\"GpuEnabled\":true,\"StopModelOnBattery\":false,\"PublicInfo\":true,\"ForegroundEnabled\":true,\"Scale\":1,\"Left\":550,\"Top\":300}");
                App.Args = ["--test-mode"];
                window = new MainWindow(); window.Show();
                Wait(() => (bool)Field(window, "companionReady"), "production animation assets load", 20);
                ((DispatcherTimer)Field(window, "companionTimer")).Stop();
                App.Args = ["--test-mode"];
                ((CompanionBrain)Field(window, "brain")).Dispose();
                var brain = new CompanionBrain(Process.Start, TimeSpan.FromSeconds(60), newsRoll: () => 0) { StopOnBattery = false };
                Set(window, "brain", brain);
                ((CompanionPreferences)Field(window, "preferences")).ModelEnabled = true;
                // Invalid foreground proves that selected news is generated independently of screen/OCR input.
                Set(window, "speechForegroundOverride", new IntPtr(-123));
                var ring = (CompanionLoadingRing)Field(window, "loadingSpinner");
                var records = new List<object>();
                for (int i = 0; i < 4; i++)
                {
                    var watch = Stopwatch.StartNew();
                    var task = (Task)Invoke(window, "SpeechCountdownZeroAsync")!;
                    Check(ring.Running && ((FrameworkElement)Field(window, "loadingIndicator")).Visibility == Visibility.Visible, "shared speech entry starts refined loading ring immediately");
                    double angle = ring.Angle; Pump(180);
                    Check(ring.Angle != angle && ring.HasAnimatedProperties, "WPF animation clock advances ring smoothly");
                    if (i == 0)
                        Save((Visual)Field(window, "companionLayout"), window.Width, window.Height, Path.Combine(root, "loading-with-pet.png"), 192);
                    double? first = null;
                    Wait(() =>
                    {
                        if ((bool)Field(window, "bubbleIsModelReply") && ((TextBlock)Field(window, "bubbleText")).Text.Length > 0)
                            first ??= watch.Elapsed.TotalMilliseconds;
                        return task.IsCompleted;
                    }, "real RSS and model finish through production streaming bubble");
                    task.GetAwaiter().GetResult();
                    string text = ((TextBlock)Field(window, "bubbleText")).Text;
                    Console.WriteLine("REAL NEWS " + text);
                    Check(text.StartsWith("主人") && brain.Status == "本地模型就绪", "actual news reaches bubble and addresses owner");
                    Check(text.EnumerateRunes().Count() <= 80 && !text.EndsWith("…"), "actual news remains within a small bubble without ellipsis truncation");
                    Check(!ring.Running && !ring.HasAnimatedProperties, "completion releases all loading animation clocks");
                    var request = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "model-request.json")));
                    var payload = request.RootElement;
                    var system = payload.GetProperty("messages")[0].GetProperty("content").GetString();
                    string context = payload.GetProperty("messages")[1].GetProperty("content").GetString()!;
                    Check(system == CompanionBrain.NewsPersona && !context.Contains("屏幕资料") && context.Contains("新闻摘要"), "actual model request contains only independent news instructions and current RSS facts");
                    Check(payload.GetProperty("temperature").GetDouble() == .35 && payload.GetProperty("max_tokens").GetInt32() == 160,
                        "actual news request uses factual generation settings and compact output budget");
                    records.Add(new { context, text, firstMilliseconds = first, totalMilliseconds = watch.Elapsed.TotalMilliseconds });
                    File.WriteAllText(Path.Combine(root, "news-live-results.json"), JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
                    if (i == 0) Save((Visual)Field(window, "companionLayout"), window.Width, window.Height, Path.Combine(root, "news-with-pet.png"), 192);
                }
                // Render the actual control at standard and 225% DPI for visual review.
                foreach (int dpi in new[] { 96, 216 })
                {
                    var preview = new System.Windows.Controls.Border { Width = 112, Height = 112, Background = Brushes.White, Padding = new Thickness(8) };
                    var grid = new System.Windows.Controls.Primitives.UniformGrid { Rows = 2, Columns = 2 };
                    for (int i = 0; i < 4; i++) grid.Children.Add(new CompanionLoadingRing { Width = 28, Height = 28, Angle = i * 90, Sweep = 70 + i * 60 });
                    preview.Child = grid;
                    preview.Measure(new Size(112, 112)); preview.Arrange(new Rect(0, 0, 112, 112)); preview.UpdateLayout();
                    Save(preview, 112, 112, Path.Combine(root, $"loading-ring-{dpi}dpi.png"), dpi);
                }
                Console.WriteLine($"RESULT {checks} actual RSS/model/UI/loading checks passed");
                result.SetResult(0);
            }
            catch (Exception error) { Console.Error.WriteLine(error); result.SetResult(1); }
            finally
            {
                if (window != null) { Set(window, "exitReady", true); window.Close(); }
            }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return result.Task;
    }
}
