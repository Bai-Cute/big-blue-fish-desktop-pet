using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using BigBlueFish.Deployment;
using Button = System.Windows.Controls.Button;

internal static partial class DeploymentVerification
{
    // Header-only synthetic bytes exercise transfers, never inference or model compatibility.
    static byte[] TransferFixture()
    {
        var bytes = new byte[6 * 1024 * 1024]; Fixture().CopyTo(bytes, 0);
        for (int i = 256; i < bytes.Length; i++) bytes[i] = (byte)(i * 31);
        return bytes;
    }
    sealed class SlowStream(byte[] bytes, int offset, bool fail, bool earlyEnd) : Stream
    {
        int position = offset;
        readonly int origin = offset;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(15, cancellationToken);
            if (position - origin >= 2 * 1024 * 1024)
            {
                if (fail) throw new IOException("injected connection interruption");
                if (earlyEnd) return 0;
            }
            int length = Math.Min(Math.Min(buffer.Length, 65536), bytes.Length - position);
            bytes.AsMemory(position, length).CopyTo(buffer); position += length; return length;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => bytes.Length;
        public override long Position { get => position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    static HttpResponseMessage TransferResponse(HttpRequestMessage request, byte[] bytes, bool slow, bool fail = false, bool earlyEnd = false)
    {
        int offset = checked((int)(request.Headers.Range?.Ranges.Single().From ?? 0));
        var response = new HttpResponseMessage(offset == 0 ? HttpStatusCode.OK : HttpStatusCode.PartialContent)
        {
            Content = slow ? new StreamContent(new SlowStream(bytes, offset, fail, earlyEnd)) : new ByteArrayContent(bytes[offset..])
        };
        response.Content.Headers.ContentLength = bytes.Length - offset;
        if (offset > 0) response.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, bytes.Length - 1, bytes.Length);
        return response;
    }
    internal static int InterruptWorker(string destination)
    {
        var bytes = TransferFixture();
        using var client = new HttpClient(new Transport(r => TransferResponse(r, bytes, true)));
        ArtifactDownload.Download("http://fixture/model", destination, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), client: client).GetAwaiter().GetResult();
        return 0;
    }
    static async Task InterruptionRules(string root)
    {
        var bytes = TransferFixture(); string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        async Task Resume(string path, string kind)
        {
            long offset = new FileInfo(path + ".partial").Length; bool range = false;
            Check(offset > 0 && offset < bytes.Length && !File.Exists(path), kind + " retains a partial file without installing it");
            using var client = new HttpClient(new Transport(r => { range = r.Headers.Range?.Ranges.Single().From == offset; return TransferResponse(r, bytes, false); }));
            await ArtifactDownload.Download("http://fixture/model", path, hash, client: client);
            Check(range && File.ReadAllBytes(path).SequenceEqual(bytes) && !File.Exists(path + ".partial"), kind + " resumes at actual persisted offset and validates final bytes");
        }
        foreach (bool earlyEnd in new[] { false, true })
        {
            string path = Path.Combine(root, earlyEnd ? "early-eof.gguf" : "network-error.gguf");
            using var client = new HttpClient(new Transport(r => TransferResponse(r, bytes, true, fail: !earlyEnd, earlyEnd: earlyEnd)));
            try { await ArtifactDownload.Download("http://fixture/model", path, hash, client: client); throw new Exception("Incomplete response accepted"); }
            catch (IOException) { }
            await Resume(path, earlyEnd ? "Premature response EOF" : "Network read exception");
        }
        string killed = Path.Combine(root, "process-killed.gguf");
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--download-interrupt-worker"); start.ArgumentList.Add(killed);
        using var child = Process.Start(start)!;
        try
        {
            var watch = Stopwatch.StartNew();
            while (!child.HasExited && (!File.Exists(killed + ".partial") || new FileInfo(killed + ".partial").Length == 0) && watch.Elapsed.TotalSeconds < 10) await Task.Delay(30);
            Check(!child.HasExited && File.Exists(killed + ".partial") && new FileInfo(killed + ".partial").Length > 0, "real download subprocess writes progress before abrupt termination");
            child.Kill(); await child.WaitForExitAsync();
        }
        finally { if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); } }
        await Resume(killed, "Abrupt process termination");
    }
    static void DownloadUi(string root)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var bytes = TransferFixture(); string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        void Until(Func<bool> condition)
        {
            var clock = Stopwatch.StartNew(); while (!condition() && clock.Elapsed.TotalSeconds < 10) Pump();
            if (!condition()) throw new TimeoutException("Download UI fixture did not settle");
        }
        for (int mode = 0; mode < 3; mode++)
        {
            string target = Path.Combine(root, "abandon-" + mode), path = Path.Combine(target, "models", "fixture.gguf");
            int requests = 0; long requestedOffset = -1; string? opened = null;
            using var client = new HttpClient(new Transport(r => { requestedOffset = r.Headers.Range?.Ranges.Single().From ?? 0; return TransferResponse(r, bytes, ++requests == 1); }));
            ModelInstallWindow Create()
            {
                var w = new ModelInstallWindow(target, client, address => opened = address);
                typeof(ModelInstallWindow).GetField("source", flags)!.SetValue(w, new ModelSource("fixture", "fixture", "fixture.gguf", "http://fixture/model", "http://fixture/page", hash));
                w.Show(); Pump(); return w;
            }
            var window = Create();
            Check(!Children<Button>(window).Any(b => b.Content?.ToString() == "暂停下载"), "model installer does not expose pause button");
            Task Begin() => (Task)typeof(ModelInstallWindow).GetMethod("Install", flags)!.Invoke(window, null)!;
            var transfer = Begin();
            Until(() => File.Exists(path + ".partial") && new FileInfo(path + ".partial").Length > 0);
            foreach (double width in new[] { 650d, 430d })
            {
                window.Width = width; window.UpdateLayout();
                foreach (var button in window.Actions.Children.OfType<Button>().Where(b => b.IsVisible))
                {
                    var bounds = button.TransformToAncestor(window).TransformBounds(new Rect(0, 0, button.ActualWidth, button.ActualHeight));
                    Check(bounds.Left >= 0 && bounds.Right <= window.ActualWidth && bounds.Bottom <= window.ActualHeight, "download footer fits window at width " + width + ": " + button.Content);
                }
            }
            window.Width = 650; window.UpdateLayout();
            void Decide(int choice)
            {
                bool selected = false;
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
                timer.Tick += (_, _) =>
                {
                    var dialog = System.Windows.Application.Current.Windows.Cast<Window>().FirstOrDefault(w => w.Title == "放弃自动安装？");
                    if (dialog == null) return;
                    timer.Stop(); selected = true;
                    Check(Children<System.Windows.Controls.TextBlock>(dialog).Any(t => t.Text == "放弃自动安装会删除已下载的部分模型，您可以手动安装模型。"), "abandon notice matches requested text");
                    var buttons = ((ModernWindow)dialog).Actions.Children.OfType<Button>().ToArray();
                    Check(buttons.Select(b => b.Content?.ToString()).SequenceEqual(new[] { "放弃自动安装", "不要放弃" }), "abandon notice has exactly the requested choices");
                    Check(File.Exists(path + ".partial"), "confirmation alone does not discard partial bytes");
                    if (choice < 0) dialog.Close(); else buttons[choice].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                };
                timer.Start();
                try { ((Button)typeof(ModelInstallWindow).GetField("abandon", flags)!.GetValue(window)!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                finally { timer.Stop(); }
                Check(selected, "abandon button opens actual confirmation window");
            }
            if (mode == 0)
            {
                Decide(1);
                Check(!transfer.IsCompleted && opened == null && File.Exists(path + ".partial"), "do not abandon continues the same download without opening manual mode");
                var existing = Fixture(); File.WriteAllBytes(path, existing);
                string other = Path.Combine(target, "models", "other.gguf.partial"); File.WriteAllBytes(other, existing);
                Decide(0); Until(() => transfer.IsCompleted); transfer.GetAwaiter().GetResult();
                Check(!File.Exists(path + ".partial") && File.ReadAllBytes(path).SequenceEqual(existing) && File.ReadAllBytes(other).SequenceEqual(existing), "confirmed abandonment deletes only active partial and preserves complete models and other downloads");
                Check(window.IsVisible && !window.Completed && opened == "http://fixture/page", "confirmed abandonment keeps installer open and opens source page");
                Check(Children<System.Windows.Controls.TextBox>(window).Any(t => t.Text == Path.Combine(target, "models") && t.IsVisible), "confirmed abandonment displays absolute manual installation directory");
                Check(((System.Windows.Controls.RadioButton)typeof(ModelInstallWindow).GetField("manual", flags)!.GetValue(window)!).IsChecked == true, "confirmed abandonment selects manual mode after transfer has stopped");
                Check(((Button)typeof(ModelInstallWindow).GetField("install", flags)!.GetValue(window)!).IsEnabled, "manual import is enabled after abandoned transfer releases file");
                window.Close(); Pump(); continue;
            }
            if (mode == 1)
            {
                Decide(-1); Until(() => transfer.IsCompleted); transfer.GetAwaiter().GetResult();
                Check(window.Completed && requests == 1 && File.ReadAllBytes(path).SequenceEqual(bytes), "closing confirmation leaves automatic download running to successful completion");
                window.Close(); Pump(); continue;
            }
            window.RequestClose();
            Until(() => transfer.IsCompleted); transfer.GetAwaiter().GetResult();
            long saved = new FileInfo(path + ".partial").Length;
            Check(saved > 0 && saved < bytes.Length && !File.Exists(path), "window close retains real downloaded bytes for resumption");
            Check(!window.IsVisible && !window.Completed, "closing during transfer waits for cancellation and closes without success");
            window = Create();
            var resumed = Begin(); Until(() => resumed.IsCompleted); resumed.GetAwaiter().GetResult();
            Check(requests == 2 && requestedOffset == saved && window.Completed && ModelStore.Resolve(target) == path && File.ReadAllBytes(path).SequenceEqual(bytes), "UI resume completes at saved offset without restarting bytes, mode " + mode);
            window.Close(); Pump();
        }
        string domestic = Path.Combine(root, "domestic-pending"); Directory.CreateDirectory(Path.Combine(domestic, "models"));
        File.WriteAllBytes(Path.Combine(domestic, "models", ModelStore.Domestic.FileName) + ".partial", bytes[..123]);
        var reopened = new ModelInstallWindow(domestic, openAddress: _ => { }); reopened.Show(); Pump();
        Check(((System.Windows.Controls.RadioButton)typeof(ModelInstallWindow).GetField("domestic", flags)!.GetValue(reopened)!).IsChecked == true && ((Button)typeof(ModelInstallWindow).GetField("install", flags)!.GetValue(reopened)!).Content?.ToString() == "继续下载", "reopening pending domestic download selects its source and resume action");
        reopened.Close(); Pump();
    }
}
