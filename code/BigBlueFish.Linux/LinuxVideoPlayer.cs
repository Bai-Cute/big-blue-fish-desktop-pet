using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace BigBlueFish.Windows;

internal sealed class LinuxVideoPlayer(Image image) : IDisposable
{
    readonly WriteableBitmap bitmap = new(new PixelSize(640, 360), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
    CancellationTokenSource? current;
    Process? decoder;
    long generation;
    internal long Frames { get; private set; }
    internal double PositionSeconds { get; private set; }
    internal double LastReadMs, LastUiMs, LastCopyMs, LastShapeMs;
    internal volatile bool Paused;
    internal event Action<double>? FramePresented;
    internal event Action<byte[], bool>? PixelsPresented;
    internal event Action<string>? Failed;
    internal void Play(string path, bool mirror, Action completed, double startSeconds = 0, double? endSeconds = null, double rate = 1)
    {
        current?.Cancel(); current?.Dispose(); try { decoder?.Kill(true); } catch { }
        current = new(); var token = current.Token; var id = ++generation;
        Frames = 0; PositionSeconds = startSeconds;
        image.Source = bitmap;
        image.RenderTransformOrigin = new(.5, .5, RelativeUnit.Relative);
        image.RenderTransform = new Avalonia.Media.ScaleTransform(mirror ? -1 : 1, 1);
        var command = new ProcessStartInfo("/usr/bin/ffmpeg") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-v", "error", "-nostdin", "-threads", "1", "-c:v", "libvpx-vp9" }) command.ArgumentList.Add(arg);
        if (startSeconds > 0) { command.ArgumentList.Add("-ss"); command.ArgumentList.Add(startSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture)); }
        command.ArgumentList.Add("-i"); command.ArgumentList.Add(path);
        if (endSeconds.HasValue) { command.ArgumentList.Add("-t"); command.ArgumentList.Add((endSeconds.Value - startSeconds).ToString("R", System.Globalization.CultureInfo.InvariantCulture)); }
        foreach (var arg in new[] { "-an", "-sn", "-f", "rawvideo", "-pix_fmt", "bgra", "pipe:1" }) command.ArgumentList.Add(arg);
        try { decoder = LinuxProcesses.Start(command); var process = decoder; _ = Task.Run(() => Decode(process, id, token, startSeconds, rate, mirror, completed)); }
        catch (Exception error) { Failed?.Invoke(error.Message); }
    }
    async Task Decode(Process process, long id, CancellationToken token, double start, double rate, bool mirror, Action completed)
    {
        var errors = process.StandardError.ReadToEndAsync(token);
        try
        {
            byte[] bytes = new byte[640 * 360 * 4]; var clock = Stopwatch.StartNew(); int count = 0;
            async Task Wait(double target) { while (Paused || clock.Elapsed.TotalSeconds < target) { if (Paused) clock.Stop(); else clock.Start(); await Task.Delay(5, token); } clock.Start(); }
            while (true)
            {
                await Wait(count / (24d * rate)); int offset = 0; var sample = Stopwatch.StartNew();
                while (offset < bytes.Length) { int read = await process.StandardOutput.BaseStream.ReadAsync(bytes.AsMemory(offset), token); if (read == 0) break; offset += read; }
                if (offset == 0) break; if (offset != bytes.Length) throw new IOException("动画帧不完整。");
                LastReadMs = sample.Elapsed.TotalMilliseconds; sample.Restart();
                for (int i = 3; i < bytes.Length; i += 4) if (bytes[i] < 8) bytes[i] = 0;
                await Dispatcher.UIThread.InvokeAsync(() => {
                    if (id != generation) return;
                    var copy = Stopwatch.StartNew();
                    using (var target = bitmap.Lock()) for (int row = 0; row < 360; row++) Marshal.Copy(bytes, row * 640 * 4, target.Address + row * target.RowBytes, 640 * 4);
                    LastCopyMs = copy.Elapsed.TotalMilliseconds; copy.Restart();
                    image.InvalidateVisual(); Frames = ++count; PositionSeconds = start + (count - 1) / 24d; PixelsPresented?.Invoke(bytes, mirror); LastShapeMs = copy.Elapsed.TotalMilliseconds; FramePresented?.Invoke(PositionSeconds);
                });
                LastUiMs = sample.Elapsed.TotalMilliseconds;
            }
            await Wait(count / (24d * rate)); await process.WaitForExitAsync(token);
            if (process.ExitCode != 0 || count == 0) throw new IOException("动画解码失败：" + await errors);
            await Dispatcher.UIThread.InvokeAsync(() => { if (id == generation) completed(); });
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { await Dispatcher.UIThread.InvokeAsync(() => { if (id == generation) Failed?.Invoke(error.Message); }); }
        finally { try { if (!process.HasExited) process.Kill(true); } catch { } process.Dispose(); try { await errors; } catch { } }
    }
    public void Dispose() { generation++; current?.Cancel(); try { decoder?.Kill(true); } catch { } bitmap.Dispose(); }
}
