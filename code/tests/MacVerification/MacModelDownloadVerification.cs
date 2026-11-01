using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using BigBlueFish.Deployment;
using BigBlueFish.Windows;

internal static class MacModelDownloadVerification
{
    static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        Console.WriteLine("PASS: " + name);
    }

    internal static async Task Run()
    {
        Check(MacModelDownload.Sources.Count == 2 &&
            MacModelDownload.Sources[0].Sha256 == ModelStore.HuggingFace.Sha256 &&
            MacModelDownload.Sources[1].Sha256 == ModelStore.Domestic.Sha256,
            "macOS uses original pinned 4B model catalog");
        await VerifyResume(true);
        await VerifyResume(false);
        await VerifyInvalidGguf();
        await VerifyCancellation();
    }

    static byte[] Fixture()
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);
        writer.Write(0x46554747u); // GGUF
        writer.Write(3u);
        writer.Write(1ul);
        writer.Write(1ul);
        writer.Write(new byte[4096]);
        return buffer.ToArray();
    }

    static (HttpListener Listener, string Url) StartServer()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        return (listener, $"http://127.0.0.1:{port}/fixture.gguf");
    }

    static ModelSource Source(string url, byte[] bytes) => new("fixture", "fixture", "fixture.gguf", url, url, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());

    static async Task VerifyResume(bool supportsRange)
    {
        var data = Fixture();
        var root = Path.Combine(Path.GetTempPath(), "bbf-mac-model-" + Guid.NewGuid());
        var folder = Path.Combine(root, "models");
        Directory.CreateDirectory(folder);
        var partial = Path.Combine(folder, "fixture.gguf.partial");
        await File.WriteAllBytesAsync(partial, data[..128]);
        var (listener, url) = StartServer();
        using (listener) try
        {
            var server = Task.Run(async () =>
            {
                var request = await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(5));
                Check(request.Request.Headers["Range"] == "bytes=128-", "resume sends byte range");
                var payload = supportsRange ? data[128..] : data;
                if (supportsRange)
                {
                    request.Response.StatusCode = 206;
                    request.Response.AddHeader("Content-Range", $"bytes 128-{data.Length - 1}/{data.Length}");
                }
                request.Response.ContentLength64 = payload.Length;
                await request.Response.OutputStream.WriteAsync(payload);
                request.Response.Close();
            });
            var target = await MacModelDownload.DownloadAsync(Source(url, data), root);
            await server;
            Check((await File.ReadAllBytesAsync(target)).SequenceEqual(data), supportsRange
                ? "range resume reconstructs GGUF" : "range ignored restarts GGUF safely");
            Check(!File.Exists(partial) && ModelStore.ReadSelection(root) is { Source: "fixture", FileName: "fixture.gguf" },
                "verified model saved with selection");
        }
        finally { Directory.Delete(root, true); }
    }

    static async Task VerifyInvalidGguf()
    {
        var data = new byte[128];
        var root = Path.Combine(Path.GetTempPath(), "bbf-mac-model-" + Guid.NewGuid());
        var (listener, url) = StartServer();
        using (listener) try
        {
            var server = Task.Run(async () =>
            {
                var request = await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(5));
                request.Response.ContentLength64 = data.Length;
                await request.Response.OutputStream.WriteAsync(data);
                request.Response.Close();
            });
            var invalid = false;
            try { await MacModelDownload.DownloadAsync(Source(url, data), root); }
            catch (InvalidDataException) { invalid = true; }
            await server;
            Check(invalid && ModelStore.ReadSelection(root) == null &&
                !File.Exists(Path.Combine(root, "models/fixture.gguf")),
                "invalid GGUF is removed and never selected");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    static async Task VerifyCancellation()
    {
        var data = Fixture();
        var root = Path.Combine(Path.GetTempPath(), "bbf-mac-model-" + Guid.NewGuid());
        var (listener, url) = StartServer();
        using (listener) try
        {
            using var firstChunkSent = new SemaphoreSlim(0, 1);
            var server = Task.Run(async () =>
            {
                var request = await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(5));
                request.Response.ContentLength64 = data.Length;
                await request.Response.OutputStream.WriteAsync(data.AsMemory(0, 128));
                await request.Response.OutputStream.FlushAsync();
                firstChunkSent.Release();
                await Task.Delay(300);
                request.Response.Close();
            });
            using var cancel = new CancellationTokenSource();
            var task = MacModelDownload.DownloadAsync(Source(url, data), root, token: cancel.Token);
            await firstChunkSent.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(50);
            cancel.Cancel();
            var cancelled = false;
            try { await task; } catch (OperationCanceledException) { cancelled = true; }
            await server;
            Check(cancelled && File.Exists(Path.Combine(root, "models/fixture.gguf.partial")) &&
                ModelStore.ReadSelection(root) == null, "cancel preserves partial without selecting model");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
