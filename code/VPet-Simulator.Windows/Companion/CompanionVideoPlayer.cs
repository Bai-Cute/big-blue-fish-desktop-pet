using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VPet_Simulator.Windows;

// Streaming decoder: one raw frame in flight, original alpha and 24 fps, no temp images.
internal sealed class CompanionVideoPlayer : IDisposable
{
    internal const int VideoWidth = 640, VideoHeight = 360, Fps = 24;
    private readonly Image image;
    private readonly CompanionJob job = new();
    private readonly WriteableBitmap bitmap = new(VideoWidth, VideoHeight, 96, 96, PixelFormats.Bgra32, null);
    private CancellationTokenSource? cancellation;
    private Process? decoder;
    private volatile bool paused;
    private long generation;
    private bool disposed;
    internal long Frames { get; private set; }
    internal double PositionSeconds { get; private set; }
    internal bool Paused { get => paused; set => paused = value; }
    internal event Action<string>? Failed;
    internal event Action<double>? FramePresented;
    internal CompanionVideoPlayer(Image target) { image = target; image.Source = bitmap; }
    internal void Play(string file, bool mirrored, Action completed)
    {
        Stop();
        if (disposed) return;
        image.RenderTransformOrigin = new(.5, .5);
        image.RenderTransform = mirrored ? new ScaleTransform(-1, 1) : Transform.Identity;
        Frames = 0; PositionSeconds = 0;
        cancellation = new();
        var token = cancellation.Token;
        var id = ++generation;
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "media", "ffmpeg.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-v", "error", "-nostdin", "-threads", "1", "-c:v", "libvpx-vp9", "-i", file, "-an", "-sn", "-f", "rawvideo", "-pix_fmt", "bgra", "pipe:1" }) start.ArgumentList.Add(arg);
        var process = new Process { StartInfo = start };
        try
        {
            process.Start(); decoder = process;
            if (!job.Attach(process)) throw new IOException("无法关联动画解码进程");
            _ = Task.Run(() => Decode(process, id, token, completed));
        }
        catch (Exception ex)
        {
            Stop(); process.Dispose(); Failed?.Invoke(ex.Message);
        }
    }
    private async Task Decode(Process process, long id, CancellationToken token, Action completed)
    {
        var errors = process.StandardError.ReadToEndAsync(token);
        try
        {
            var data = new byte[VideoWidth * VideoHeight * 4];
            var clock = Stopwatch.StartNew();
            int count = 0;
            async Task WaitUntil(double target)
            {
                while (paused || clock.Elapsed.TotalSeconds < target)
                {
                    if (paused) clock.Stop(); else clock.Start();
                    await Task.Delay(5, token);
                }
                clock.Start();
            }
            while (true)
            {
                await WaitUntil(count / (double)Fps);
                int offset = 0;
                while (offset < data.Length)
                {
                    int read = await process.StandardOutput.BaseStream.ReadAsync(data.AsMemory(offset), token);
                    if (read == 0) break;
                    offset += read;
                }
                if (offset == 0) break;
                if (offset != data.Length) throw new IOException("动画帧数据不完整");
                // VP9 alpha can contain tiny compression residue in nominally empty pixels.
                // Clear it so the layered window remains click-through around the character.
                for (int alpha = 3; alpha < data.Length; alpha += 4)
                    if (data[alpha] < 8) data[alpha] = 0;
                // Never reuse the frame buffer before the dispatcher has copied it.
                await image.Dispatcher.InvokeAsync(() =>
                {
                    if (id != generation || disposed) return;
                    bitmap.WritePixels(new System.Windows.Int32Rect(0, 0, VideoWidth, VideoHeight), data, VideoWidth * 4, 0);
                    Frames = ++count; PositionSeconds = (count - 1) / (double)Fps;
                    FramePresented?.Invoke(PositionSeconds);
                }, System.Windows.Threading.DispatcherPriority.Render, token);
            }
            await WaitUntil(count / (double)Fps); // Give the final frame its full display interval.
            await process.WaitForExitAsync(token);
            var error = await errors;
            if (process.ExitCode != 0 || count == 0) throw new IOException("动画解码失败：" + error);
            await image.Dispatcher.InvokeAsync(() => { if (id == generation && !disposed) completed(); });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await image.Dispatcher.InvokeAsync(() => { if (id == generation && !disposed) Failed?.Invoke(ex.Message); });
        }
        finally
        {
            try { if (!process.HasExited) process.Kill(); } catch { }
            process.Dispose();
            if (ReferenceEquals(decoder, process)) decoder = null;
            try { await errors; } catch { }
        }
    }
    internal void Stop()
    {
        generation++;
        cancellation?.Cancel(); cancellation?.Dispose(); cancellation = null;
        try { if (decoder != null && !decoder.HasExited) decoder.Kill(); } catch { }
        decoder = null;
    }
    public void Dispose() { if (disposed) return; disposed = true; Stop(); job.Dispose(); }
}
