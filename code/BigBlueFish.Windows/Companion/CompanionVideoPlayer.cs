using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BigBlueFish.Windows;

internal sealed class CompanionVideoPlayer : IDisposable
{
    readonly Image image;
    readonly SemaphoreSlim serial = new(1);
    WriteableBitmap? bitmap;
    CancellationTokenSource? cancellation;
    volatile bool paused;
    long generation;
    bool disposed, hardwareFailed;
    string? displayedFile;
    double displayedSeconds;
    internal long Frames { get; private set; }
    internal ulong RenderedFrames => (ulong)Frames;
    internal double PositionSeconds { get; private set; }
    internal double PlaybackRate { get; private set; } = 1;
    internal double StartSeconds { get; private set; }
    internal string Backend { get; private set; } = "未运行";
    internal int OutputWidth { get; private set; }
    internal int OutputHeight { get; private set; }
    internal bool ForceSoftware { get; set; }
    internal bool Paused { get => paused; set => paused = value; }
    internal event Action<string>? Failed;
    internal event Action<double>? FramePresented;
    internal CompanionVideoPlayer(Image target) => image = target;
    internal void ResetHardwareFallback() => hardwareFailed = false;

    internal void Play(string file, bool mirrored, Action completed, double startSeconds = 0, double? endSeconds = null, double playbackRate = 1)
    {
        if (!double.IsFinite(startSeconds) || startSeconds < 0 || !double.IsFinite(playbackRate) || playbackRate <= 0
            || endSeconds.HasValue && (!double.IsFinite(endSeconds.Value) || endSeconds.Value <= startSeconds))
            throw new ArgumentOutOfRangeException(nameof(startSeconds));
        Stop();
        if (disposed) return;
        image.Opacity = 1;
        image.RenderTransformOrigin = new(.5, .5);
        image.RenderTransform = mirrored ? new ScaleTransform(-1, 1) : Transform.Identity;
        Frames = 0; PositionSeconds = StartSeconds = startSeconds; PlaybackRate = playbackRate;
        double raw = image.ActualWidth > 0 ? image.ActualWidth : image.Width;
        var pixels = raw * VisualTreeHelper.GetDpi(image).DpiScaleX;
        int width = Math.Clamp((int)Math.Ceiling((double.IsFinite(pixels) && pixels > 0 ? pixels : 640) / 128) * 128, 256, 1280);
        int height = width * 9 / 16;
        cancellation = new();
        var token = cancellation.Token;
        var id = Interlocked.Increment(ref generation);
        _ = Task.Run(async () =>
        {
            try
            {
                await serial.WaitAsync(token);
                try
                {
                    CompanionMedia.Initialize();
                    bool hardware = !ForceSoftware && !hardwareFailed && CompanionMedia.HardwareAvailable;
                    while (true)
                    {
                        try
                        {
                            await Decode(file, hardware, width, height, id, token, completed, startSeconds, endSeconds, playbackRate);
                            break;
                        }
                        catch (Exception ex) when (hardware && !token.IsCancellationRequested)
                        {
                            hardwareFailed = true;
                            hardware = false;
                            DiagnosticLog.Write(DiagnosticEvent.AnimationFailed, state: "hardware_fallback", error: ex);
                        }
                    }
                }
                finally { serial.Release(); }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                await image.Dispatcher.InvokeAsync(() => { if (id == Interlocked.Read(ref generation) && !disposed) Failed?.Invoke(ex.Message); });
            }
        });
    }

    async Task Decode(string file, bool hardware, int width, int height, long id, CancellationToken token, Action completed, double start, double? end, double rate)
    {
        if (!hardware)
        {
            file = Path.Combine(Path.GetDirectoryName(file)!, "compat", Path.GetFileName(file));
            width = 640; height = 360;
            if (!File.Exists(file)) throw new IOException("兼容动画素材缺失：" + Path.GetFileName(file));
        }
        using var stream = new CompanionMediaStream(file, hardware, width, height, start, end, rate);
        token.ThrowIfCancellationRequested();
        Backend = stream.Description;
        OutputWidth = width; OutputHeight = height;
        stream.SetPaused(paused);
        bool wasPaused = paused;
        long presented = 0;
        bool verifiedVisible = !hardware;
        int silentFrames = 0;
        byte[][] pixelBuffers = [new byte[width * height * 4], new byte[width * height * 4]];
        int slot = 0;

        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (wasPaused != paused) { wasPaused = paused; stream.SetPaused(paused); }
            if (paused) { await Task.Delay(40, token); continue; }
            double seconds;
            byte[] currentPixels = pixelBuffers[slot];
            using (var frame = stream.Read())
            {
                if (frame == null) { if (stream.Ended) break; continue; }
                seconds = frame.Seconds;
                bool hasVisible = ExtractFrame(frame, currentPixels, !verifiedVisible);
                if (!verifiedVisible)
                {
                    if (hasVisible) verifiedVisible = true;
                    else if (++silentFrames >= 5) throw new IOException("GPU 硬解未输出可见动画像素。");
                }
            }
            slot ^= 1;
            await image.Dispatcher.InvokeAsync(() =>
            {
                if (id != Interlocked.Read(ref generation) || disposed) return;
                Present(currentPixels, width, height);
                Frames = ++presented;
                PositionSeconds = seconds;
                displayedFile = file;
                displayedSeconds = seconds;
                FramePresented?.Invoke(PositionSeconds);
            }, System.Windows.Threading.DispatcherPriority.Render);
        }
        if (presented == 0) throw new IOException("动画没有生成可显示的帧。");
        await image.Dispatcher.InvokeAsync(() => { if (id == Interlocked.Read(ref generation) && !disposed) completed(); });
    }

    static unsafe bool ExtractFrame(CompanionMediaFrame frame, byte[] pixels, bool checkVisible)
    {
        bool visible = false;
        fixed (byte* dstBase = pixels)
        {
            for (int y = 0; y < frame.Height; y++)
            {
                uint* source = (uint*)((byte*)frame.Data + y * frame.Stride);
                uint* target = (uint*)(dstBase + y * frame.Width * 4);
                int x = 0;
                if (Vector256.IsHardwareAccelerated)
                {
                    for (; x <= frame.Width - 8; x += 8)
                    {
                        var vec = Vector256.Load(source + x);
                        var alpha = Vector256.ShiftRightLogical(vec, 24);
                        var transparent = Vector256.LessThan(alpha, Vector256.Create(8u));
                        (vec & ~(transparent & Vector256.Create(0xff000000u))).Store(target + x);
                        if (checkVisible && !visible && Vector256.LessThan(Vector256.Create(15u), alpha) != Vector256<uint>.Zero)
                            visible = true;
                    }
                }
                for (; x < frame.Width; x++)
                {
                    uint p = source[x];
                    uint a = p >> 24;
                    target[x] = a < 8 ? p & 0x00ffffffu : p;
                    if (checkVisible && a >= 16) visible = true;
                }
            }
        }
        return visible;
    }

    unsafe void Present(byte[] pixels, int width, int height)
    {
        if (bitmap == null || bitmap.PixelWidth != width || bitmap.PixelHeight != height)
        {
            bitmap = new(width, height, 96, 96, PixelFormats.Bgra32, null);
            image.Source = bitmap;
        }
        bitmap.Lock();
        try
        {
            fixed (byte* srcBase = pixels)
            {
                for (int y = 0; y < height; y++)
                    Buffer.MemoryCopy(srcBase + y * width * 4, (byte*)bitmap.BackBuffer + y * bitmap.BackBufferStride,
                        bitmap.BackBufferStride, width * 4);
            }
            bitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
        }
        finally { bitmap.Unlock(); }
        if (image is CompanionSpriteImage sprite) sprite.UpdatePixels(pixels, width, height);
    }

    internal void Stop()
    {
        Interlocked.Increment(ref generation);
        cancellation?.Cancel();
        cancellation?.Dispose();
        cancellation = null;
    }

    internal Task<BitmapSource?> CaptureSnapshotAsync()
    {
        if (!Backend.StartsWith("GPU") || displayedFile == null)
        {
            var copy = bitmap?.Clone();
            copy?.Freeze();
            return Task.FromResult<BitmapSource?>(copy);
        }
        string file = displayedFile;
        double seconds = displayedSeconds;
        return Task.Run<BitmapSource?>(() =>
        {
            using var capture = new CompanionMediaStream(file, true, 1280, 720, seconds, null, 1);
            capture.SetPaused(false);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                using var frame = capture.Read();
                if (frame == null) { if (capture.Ended) break; continue; }
                var pixels = new byte[frame.Width * frame.Height * 4];
                for (int y = 0; y < frame.Height; y++)
                    Marshal.Copy(frame.Data + y * frame.Stride, pixels, y * frame.Width * 4, frame.Width * 4);
                var result = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null, pixels, frame.Width * 4);
                result.Freeze();
                return result;
            }
            throw new IOException("未能取得动画截图。");
        });
    }

    internal void RenderSnapshot(RenderTargetBitmap target, FrameworkElement layout, BitmapSource? snapshot)
    {
        _ = snapshot;
        target.Render(layout);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Stop();
    }
}
