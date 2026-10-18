using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BigBlueFish.Deployment;
using BigBlueFish.Setup;
using BigBlueFish.Uninstaller;
using Window = System.Windows.Window;
using CheckBox = System.Windows.Controls.CheckBox;
using Button = System.Windows.Controls.Button;
using Size = System.Windows.Size;

internal static partial class DeploymentVerification
{
    static int count;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); count++; Console.WriteLine("PASS " + message); }
    static byte[] Fixture()
    {
        using var stream = new MemoryStream(); using var binary = new BinaryWriter(stream);
        binary.Write(0x46554747u); binary.Write(3u); binary.Write(1ul); binary.Write(1ul); binary.Write("GGUF header-only transport fixture, not model weights");
        return stream.ToArray();
    }
    sealed class Transport(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }
    internal static async Task<int> Rules(string root)
    {
        root = Path.GetFullPath(root); Directory.CreateDirectory(root);
        var target = Path.Combine(root, "model-tests"); Directory.CreateDirectory(Path.Combine(target, "models"));
        var path = Path.Combine(target, "models", "Qwen3.5-4B-heretic-f16_Q4_K_M.gguf");
        File.WriteAllBytes(path, Fixture());
        Check(ModelStore.Resolve(target) == path, "original Hugging Face download filename is recognized directly");
        var imported = Path.Combine(root, "custom-name.gguf"); File.WriteAllBytes(imported, Fixture());
        string hash = await ModelStore.Hash(imported);
        Check(!ModelStore.Expected(hash), "unknown-model fixture is distinguishable from both expected hashes");
        await ModelStore.Import(target, imported, hash);
        Check(ModelStore.Resolve(target) == Path.Combine(target, "models", "custom-name.gguf") && ModelStore.ReadSelection(target)?.Source == "manual", "accepted manual model keeps actual filename and selection across reads");
        ModelStore.Save(target, new ModelStore.Selection("none", null, null));
        Check(ModelStore.Resolve(target) == null, "explicit no-model selection remains effective even when old GGUF files exist");
        File.WriteAllText(imported, "<html>not GGUF</html>");
        Check(!ModelStore.IsGguf(imported), "gguf suffix alone cannot pass type validation");
        var download = Path.Combine(root, "download.bin"); var bytes = Fixture();
        File.WriteAllBytes(download + ".partial", bytes[..10]);
        bool ranged = false;
        using var partialClient = new HttpClient(new Transport(r =>
        {
            ranged = r.Headers.Range?.Ranges.First().From == 10;
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(bytes[10..]) };
            response.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(10, bytes.Length - 1, bytes.Length); return response;
        }));
        await ArtifactDownload.Download("http://fixture/model", download, hash, client: partialClient);
        Check(ranged && File.ReadAllBytes(download).SequenceEqual(bytes), "interrupted model/update download resumes with a byte range and verifies complete hash");
        File.WriteAllBytes(download + ".partial", bytes[..5]); File.Delete(download);
        using var ignoresRange = new HttpClient(new Transport(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
        await ArtifactDownload.Download("http://fixture/model", download, hash, client: ignoresRange);
        Check(File.ReadAllBytes(download).SequenceEqual(bytes), "server that ignores resume is restarted without corrupt concatenation");
        File.Delete(download);
        try { await ArtifactDownload.Download("http://fixture/model", download, new string('0', 64), client: ignoresRange); throw new Exception("hash mismatch accepted"); }
        catch (InvalidDataException) { Check(!File.Exists(download), "failed verified download never replaces installed destination"); }
        string release = JsonSerializer.Serialize(new { draft = false, prerelease = false, tag_name = "v0.5.1", body = "测试更新", assets = new[] { new { name = "BigBlueFish-v0.5.1-OCR-Setup.exe", browser_download_url = "http://fixture/installer", digest = "sha256:" + hash } } });
        using var updates = new HttpClient(new Transport(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(release) }));
        Check((await UpdateService.Check("0.5.0", client: updates))?.Sha256 == hash, "public release metadata selects matching OCR installer and SHA256");
        Check(await UpdateService.Check("0.5.1", client: updates) == null && await UpdateService.Check("0.5.2", client: updates) == null, "update check never offers equal version or downgrade");
        var transaction = Path.Combine(root, "transaction"); Directory.CreateDirectory(Path.Combine(transaction, "models"));
        File.WriteAllText(Path.Combine(transaction, "main.bin"), "old program");
        File.WriteAllText(Path.Combine(transaction, "preferences.json"), "user settings");
        File.WriteAllText(Path.Combine(transaction, "models", "user.gguf"), "user model");
        File.WriteAllText(Path.Combine(transaction, "model-selection.json"), "manual choice");
        File.WriteAllText(Path.Combine(transaction, "installation-manifest.json"), "{\"Files\":[\"main.bin\"]}");
        using var zipBytes = new MemoryStream();
        using (var zip = new ZipArchive(zipBytes, ZipArchiveMode.Create, true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("main.bin").Open())) writer.Write("new program");
            using (var writer = new StreamWriter(zip.CreateEntry("new.bin").Open())) writer.Write("new component");
        }
        zipBytes.Position = 0;
        using var archive = new ZipArchive(zipBytes, ZipArchiveMode.Read);
        try
        {
            UpdateTransaction.Apply(transaction, archive, () => { File.WriteAllText(Path.Combine(transaction, "main.bin"), "broken"); File.WriteAllText(Path.Combine(transaction, "new.bin"), "partial"); throw new IOException("injected failed update"); });
            throw new Exception("failed update accepted");
        }
        catch (IOException) { }
        Check(File.ReadAllText(Path.Combine(transaction, "main.bin")) == "old program" && !File.Exists(Path.Combine(transaction, "new.bin")), "failed file replacement restores old program and removes incomplete new files");
        Check(File.ReadAllText(Path.Combine(transaction, "preferences.json")) == "user settings" && File.ReadAllText(Path.Combine(transaction, "model-selection.json")) == "manual choice" && File.ReadAllText(Path.Combine(transaction, "models", "user.gguf")) == "user model", "failed update preserves settings, model and selected source");
        UpdateTransaction.Apply(transaction, archive, () => archive.ExtractToDirectory(transaction, true));
        Check(File.ReadAllText(Path.Combine(transaction, "main.bin")) == "new program" && !Directory.Exists(Path.Combine(transaction, ".update-previous")), "successful transaction installs new program and cleans temporary previous files");
        await InterruptionRules(root);
        Console.WriteLine($"RESULT {count} deployment rules and fault checks passed; transport bytes are fixtures"); return 0;
    }
    internal static async Task<int> LiveDownload(string path)
    {
        path = Path.GetFullPath(path);
        if (!path.StartsWith(Path.GetFullPath(".work") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || (File.GetAttributes(Path.GetDirectoryName(path)!) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Live download fixture must be a separate model in this repository's .work directory.");
        if (File.Exists(path))
        {
            Check(await ModelStore.Hash(path) == ModelStore.Domestic.Sha256, "live resume fixture starts with verified downloaded original model");
            File.Move(path, path + ".partial");
            using var partial = File.OpenWrite(path + ".partial"); partial.SetLength(partial.Length - 1048576);
        }
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await ArtifactDownload.Download(ModelStore.Domestic.Url, path, ModelStore.Domestic.Sha256);
        Check(await ModelStore.Hash(path) == ModelStore.Domestic.Sha256 && ModelStore.IsGguf(path), "production downloader resumes from real domestic source and verifies whole 2.7 GB model");
        Console.WriteLine($"RESULT {count} live domestic download checks passed in {watch.Elapsed.TotalSeconds:0.00}s"); return 0;
    }
    static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); if (child is T item) yield return item; foreach (var next in Children<T>(child)) yield return next; }
    }
    static void Pump()
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
    internal static int ModelUi(string root)
    {
        root = Path.GetFullPath(root); Directory.CreateDirectory(root); ModernUI.EnsureApplication();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var bytes = Fixture();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        for (int choice = 0; choice < 3; choice++)
        {
            int requests = 0; string? opened = null;
            using var http = new HttpClient(new Transport(_ => ++requests == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
            var target = Path.Combine(root, "failure-" + choice);
            var window = new ModelInstallWindow(target, http, address => opened = address);
            typeof(ModelInstallWindow).GetField("source", flags)!.SetValue(window,
                new ModelSource("fixture", "transport fixture", "fixture.gguf", "http://fixture/model", "http://fixture/page", hash));
            window.Show(); Pump();
            bool selected = false;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            timer.Tick += (_, _) =>
            {
                var dialog = System.Windows.Application.Current.Windows.Cast<Window>().FirstOrDefault(w => w.Title == "模型下载未完成");
                if (dialog == null) return;
                var buttons = ((ModernWindow)dialog).Actions.Children.OfType<Button>().ToArray();
                Check(buttons.Select(b => b.Content).SequenceEqual(new[] { "重试", "手动安装模型", "无模型启动" }), "download failure offers exactly three functional exits");
                selected = true; timer.Stop(); buttons[choice].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            timer.Start();
            var task = (Task)typeof(ModelInstallWindow).GetMethod("Install", flags)!.Invoke(window, null)!;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while ((!selected || !task.IsCompleted || (choice == 0 && !window.Completed)) && watch.Elapsed.TotalSeconds < 10) Pump();
            timer.Stop(); task.GetAwaiter().GetResult();
            Check(selected, "download failure dialog is actionable");
            if (choice == 0) Check(window.Completed && requests == 2 && ModelStore.Resolve(target) != null, "retry finishes real download/import flow with injected transport bytes");
            if (choice == 1)
            {
                Check(window.IsVisible && !window.Completed && opened == "http://fixture/page", "manual fallback keeps window open and opens chosen source page");
                Check(Children<System.Windows.Controls.TextBox>(window).Any(t => t.Text == Path.Combine(target, "models") && t.IsVisible), "manual fallback shows exact absolute installation models path");
            }
            if (choice == 2) Check(window.Completed && ModelStore.ReadSelection(target)?.Source == "none", "no-model failure exit saves choice and completes installation");
            window.Close(); Pump();
        }
        var manualTarget = Path.Combine(root, "manual");
        var manual = new ModelInstallWindow(manualTarget, openAddress: _ => { }); manual.Show(); Pump();
        var imported = Path.Combine(root, "my-model.gguf"); File.WriteAllBytes(imported, bytes);
        bool consent = false;
        var consentTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        consentTimer.Tick += (_, _) =>
        {
            var dialog = System.Windows.Application.Current.Windows.Cast<Window>().FirstOrDefault(w => w.Title == "使用此模型？");
            if (dialog == null) return;
            Check(Children<System.Windows.Controls.TextBlock>(dialog).Any(t => t.Text == "这不是本软件期待的模型，您可以尝试使用，但稳定性无法保证"), "manual unknown hash uses approved non-blocking warning");
            consent = true; consentTimer.Stop();
            ((ModernWindow)dialog).Actions.Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        consentTimer.Start();
        var import = (Task)typeof(ModelInstallWindow).GetMethod("Import", flags)!.Invoke(manual, [imported])!;
        var limit = System.Diagnostics.Stopwatch.StartNew(); while (!import.IsCompleted && limit.Elapsed.TotalSeconds < 10) Pump();
        consentTimer.Stop(); import.GetAwaiter().GetResult();
        Check(consent && manual.Completed && ModelStore.Resolve(manualTarget)?.EndsWith("my-model.gguf") == true, "manual model continues after consent and preserves actual filename");
        DownloadUi(root);
        Console.WriteLine($"RESULT {count} real WPF model-install interactions passed; HTTP payload is a fixture"); return 0;
    }
    internal static int Ui(string root)
    {
        root = Path.GetFullPath(root); Directory.CreateDirectory(root); ModernUI.EnsureApplication();
        Window[] windows = [new InstallerForm(), UninstallApplication.CreateUninstallForm(Path.Combine(root, "install")), new ModelInstallWindow(Path.Combine(root, "install")), new UpdateWindow(new AvailableUpdate("0.5.1", "BigBlueFish.exe", "https://example.invalid/installer", new string('0', 64), "新版完善了桌面陪伴。"), Path.Combine(root, "install"))];
        for (int i = 0; i < windows.Length; i++)
        {
            var window = windows[i]; window.Show(); Pump();
            var buttons = Children<Button>((DependencyObject)window.Content).Where(b => b.IsVisible).ToArray();
            Check(buttons.Length > 0 && buttons.All(b => b.ActualHeight >= 40), "modern action buttons retain usable height: " + window.Title);
            foreach (var button in ((ModernWindow)window).Actions.Children.OfType<Button>())
            {
                var rect = button.TransformToAncestor(window).TransformBounds(new Rect(0, 0, button.ActualWidth, button.ActualHeight));
                Check(rect.Bottom <= window.ActualHeight && rect.Top >= 0, "fixed footer keeps action visible: " + button.Content);
            }
            if (i == 1) Check(Children<CheckBox>((DependencyObject)window.Content).Single().IsChecked == false, "retain model/settings remains unchecked by default");
            foreach (double dpi in new[] { 96d, 144d, 216d })
            {
                var bitmap = new RenderTargetBitmap((int)(window.ActualWidth * dpi / 96), (int)(window.ActualHeight * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
                bitmap.Render((Visual)window.Content); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(root, $"window-{i}-{dpi}.png")); encoder.Save(file);
            }
            window.Close(); Pump();
        }
        Console.WriteLine($"RESULT {count} WPF deployment layout checks passed"); return 0;
    }
}
