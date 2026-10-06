using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace VPet_Simulator.Windows;

internal sealed record CompanionWindowImage(string DataUrl, int Width, int Height)
{
    internal void SaveJpeg(string path)
    {
        var comma = DataUrl.IndexOf(',');
        if (comma < 0)
            throw new InvalidDataException("窗口图像数据格式无效");
        File.WriteAllBytes(path, Convert.FromBase64String(DataUrl[(comma + 1)..]));
    }
}

internal static class CompanionScreenCapture
{
    private const uint RasterOperationSourceCopy = 0x00CC0020;
    private const uint PrintWindowRenderFullContent = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    private sealed class CaptureDpiScope : IDisposable
    {
        private readonly IntPtr previous;
        internal CaptureDpiScope()
        {
            // Window rectangles and PrintWindow/BitBlt must use the same physical
            // pixel coordinates, including captures made from a worker thread.
            try { previous = SetThreadDpiAwarenessContext(new IntPtr(-4)); }
            catch (EntryPointNotFoundException) { }
        }
        public void Dispose()
        {
            if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdc, int x, int y, int cx, int cy, IntPtr hdcSrc, int x1, int y1, uint rop);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

    internal static CompanionWindowImage? Capture(IntPtr handle, int maxDimension = 768, bool lossless = false)
    {
        using var dpi = new CaptureDpiScope();
        if (!CompanionNative.IsVisibleCaptureTarget(handle) || !CompanionNative.GetWindowRect(handle, out var rect))
            return null;

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width < 32 || height < 32 || width > 12000 || height > 12000)
            return null;

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            var target = graphics.GetHdc();
            var captured = PrintWindow(handle, target, PrintWindowRenderFullContent);
            graphics.ReleaseHdc(target);
            if (!captured)
            {
                using var screen = Graphics.FromImage(bitmap);
                var screenHdc = screen.GetHdc();
                var desktop = GetDC(IntPtr.Zero);
                var copied = BitBlt(screenHdc, 0, 0, width, height, desktop, rect.Left, rect.Top, RasterOperationSourceCopy);
                ReleaseDC(IntPtr.Zero, desktop);
                screen.ReleaseHdc(screenHdc);
                if (!copied)
                    return null;
            }
        }

        // 1280px screenshots make the Qwen vision prefill disproportionately slow on
        // the Intel Vulkan path. 768px preserves the application chrome and readable
        // large text while keeping the first-token wait substantially shorter.
        using var normalized = Resize(bitmap, maxDimension);
        using var output = new MemoryStream();
        normalized.Save(output, lossless ? ImageFormat.Png : ImageFormat.Jpeg);
        return new CompanionWindowImage(
            (lossless ? "data:image/png;base64," : "data:image/jpeg;base64,") + Convert.ToBase64String(output.ToArray()),
            normalized.Width,
            normalized.Height);
    }

    private static Bitmap Resize(Bitmap source, int maxDimension)
    {
        var scale = Math.Min(1d, Math.Min((double)maxDimension / source.Width, (double)maxDimension / source.Height));
        if (scale >= 1d)
            return new Bitmap(source);
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var result = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(result);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(source, new Rectangle(0, 0, width, height));
        return result;
    }
}
