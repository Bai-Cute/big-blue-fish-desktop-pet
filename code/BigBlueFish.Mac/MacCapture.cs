using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using SkiaSharp;

namespace BigBlueFish.Windows;

// macOS counterpart of BigBlueFish.Linux/LinuxCapture.cs. It keeps the same type and member names so the
// shared Avalonia pet compiles unchanged; X11 calls become CoreGraphics/AppKit, tesseract becomes Apple Vision.
internal static class LinuxCapture
{
    internal static async Task<CompanionWindowImage> CaptureAsync(IntPtr window, int maximum, bool fullScreen, CancellationToken token)
    {
        string file = Path.Combine(Path.GetTempPath(), "bigbluefish-capture-" + Guid.NewGuid().ToString("N") + ".png");
        // screencapture is the system tool behind macOS Screen Recording permission; -l reads only that window, excluding the pet.
        var start = new ProcessStartInfo("/usr/sbin/screencapture") { UseShellExecute = false, RedirectStandardError = true };
        foreach (var item in fullScreen || CompanionNative.IsDesktopWindow(window) ? ["-x", "-t", "png"] : new[] { "-x", "-o", "-t", "png", "-l" + window.ToInt64() }) start.ArgumentList.Add(item);
        start.ArgumentList.Add(file);
        try
        {
            using var process = LinuxProcesses.Start(start); using var cancel = token.Register(() => { try { process.Kill(true); } catch { } });
            var errors = process.StandardError.ReadToEndAsync(token); await process.WaitForExitAsync(token);
            if (process.ExitCode != 0 || !File.Exists(file)) throw new IOException("桌面截图未完成，请在“系统设置 › 隐私与安全性 › 录屏与系统录音”中允许蓝色大肥鱼。" + (await errors).Trim());
            using var bitmap = SKBitmap.Decode(file) ?? throw new IOException("桌面截图未返回有效图像。");
            if ((long)bitmap.Width * bitmap.Height > 150_000_000) throw new IOException("截图尺寸过大。");
            return CompanionNative.Encode(bitmap, maximum);
        }
        finally { File.Delete(file); }
    }
}

internal sealed record CompanionWindowImage(string DataUrl, int Width, int Height)
{
    internal void SaveJpeg(string path) => File.WriteAllBytes(path, Convert.FromBase64String(DataUrl[(DataUrl.IndexOf(',') + 1)..]));
}
internal static class CompanionNative
{
    // Stands for the desktop, like the X11 root window: the frontmost app has no visible window.
    internal static readonly IntPtr Desktop = new(-1);
    internal static bool OnAC
    {
        get
        {
            if (App.Args.Contains("--test-mode") && App.Args.Contains("--test-battery")) return false;
            try {
                var info = IOPSCopyPowerSourcesInfo(); if (info == IntPtr.Zero) return true;
                try { var type = IOPSGetProvidingPowerSourceType(info); return type == IntPtr.Zero || Text(type) != "Battery Power"; }
                finally { CFRelease(info); }
            } catch { return true; }
        }
    }
    readonly record struct WindowInfo(long Id, int Pid, int Layer, string Owner, string Title, CGRect Bounds);
    static readonly IntPtr NumberKey = Key("kCGWindowNumber"), PidKey = Key("kCGWindowOwnerPID"), LayerKey = Key("kCGWindowLayer"),
        OwnerKey = Key("kCGWindowOwnerName"), NameKey = Key("kCGWindowName"), BoundsKey = Key("kCGWindowBounds");
    static IntPtr Key(string name) => CFStringCreateWithCString(IntPtr.Zero, name, 0x08000100);
    // Front-to-back list of on-screen application windows (layer 0).
    static List<WindowInfo> Windows()
    {
        var result = new List<WindowInfo>();
        var array = CGWindowListCopyWindowInfo(1 | 16, 0); if (array == IntPtr.Zero) return result;
        try
        {
            for (long i = 0, count = CFArrayGetCount(array); i < count; i++)
            {
                var item = CFArrayGetValueAtIndex(array, i);
                long Number(IntPtr key) { var value = CFDictionaryGetValue(item, key); return value != IntPtr.Zero && CFNumberGetValue(value, 4, out long number) ? number : 0; }
                string Field(IntPtr key) { var value = CFDictionaryGetValue(item, key); return value == IntPtr.Zero ? "" : Text(value); }
                var bounds = CFDictionaryGetValue(item, BoundsKey); CGRect rect = default;
                if (bounds != IntPtr.Zero) CGRectMakeWithDictionaryRepresentation(bounds, out rect);
                if (Number(LayerKey) == 0) result.Add(new(Number(NumberKey), (int)Number(PidKey), 0, Field(OwnerKey), Field(NameKey), rect));
            }
        }
        finally { CFRelease(array); }
        return result;
    }
    static int FrontmostPid()
    {
        var application = Send(Send(Class("NSWorkspace"), "sharedWorkspace"), "frontmostApplication");
        return application == IntPtr.Zero ? 0 : SendInt(application, "processIdentifier");
    }
    internal static IntPtr GetForegroundWindow()
    {
        int pid = FrontmostPid();
        if (pid == 0 || pid == Environment.ProcessId) return IntPtr.Zero;
        var window = Windows().FirstOrDefault(w => w.Pid == pid);
        return window.Id == 0 ? Desktop : new IntPtr(window.Id);
    }
    internal static bool IsDesktopWindow(IntPtr handle) => handle == IntPtr.Zero || handle == Desktop;
    internal static IntPtr ResolveForeground(IntPtr current, IntPtr previous)
    {
        var windows = Windows();
        bool Valid(IntPtr handle) => handle == Desktop || handle != IntPtr.Zero && windows.Any(w => w.Id == handle.ToInt64() && w.Pid != Environment.ProcessId);
        if (Valid(current)) return current;
        if (Valid(previous)) return previous;
        return Desktop;
    }
    internal static string ForegroundIdentity(IntPtr handle)
    {
        if (IsDesktopWindow(handle)) return "前台窗口：macOS 桌面\n画面是壁纸与快捷方式。";
        var window = Windows().FirstOrDefault(w => w.Id == handle.ToInt64());
        return $"前台应用：{window.Owner}\n窗口标题：{window.Title}";
    }
    internal static bool IsFullscreen()
    {
        var handle = GetForegroundWindow(); if (IsDesktopWindow(handle)) return false;
        var bounds = Windows().FirstOrDefault(w => w.Id == handle.ToInt64()).Bounds;
        var displays = new uint[16]; if (CGGetActiveDisplayList(16, displays, out uint count) != 0) return false;
        return displays.Take((int)count).Select(CGDisplayBounds).Any(d => bounds.X <= d.X && bounds.Y <= d.Y && bounds.X + bounds.Width >= d.X + d.Width && bounds.Y + bounds.Height >= d.Y + d.Height);
    }
    internal static double IdleSeconds => CGEventSourceSecondsSinceLastEventType(1, uint.MaxValue);
    internal static CompanionWindowImage Encode(SKBitmap bitmap, int maximum)
    {
        double scale = Math.Min(1d, maximum / (double)Math.Max(bitmap.Width, bitmap.Height));
        using var small = scale < 1 ? bitmap.Resize(new SKImageInfo(Math.Max(1, (int)(bitmap.Width * scale)), Math.Max(1, (int)(bitmap.Height * scale))), new SKSamplingOptions(SKCubicResampler.Mitchell)) : null;
        using var image = SKImage.FromBitmap(small ?? bitmap); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return new("data:image/png;base64," + Convert.ToBase64String(png.ToArray()), image.Width, image.Height);
    }
    // AppKit has no input-shape region like XShape: let clicks through whenever the cursor is over a transparent pixel.
    internal static void SetInputShape(IntPtr handle, byte[] pixels, int width, int height, double scale, bool mirror)
    {
        if (handle == IntPtr.Zero) return;
        var window = SendBool(handle, "respondsToSelector:", Selector("setIgnoresMouseEvents:")) ? handle : Send(handle, "window");
        if (window == IntPtr.Zero || Send(Class("NSEvent"), "pressedMouseButtons") != IntPtr.Zero) return; // never drop a press or drag in progress
        var mouse = SendPoint(Class("NSEvent"), "mouseLocation"); var frame = SendRect(window, "frame");
        if (frame.Width <= 0 || frame.Height <= 0) return;
        int x = (int)Math.Floor((mouse.X - frame.X) * width / frame.Width), y = (int)Math.Floor((frame.Y + frame.Height - mouse.Y) * height / frame.Height);
        if (mirror) x = width - 1 - x;
        bool opaque = x >= 0 && y >= 0 && x < width && y < height && pixels[(y * width + x) * 4 + 3] >= 8;
        SendBool(window, "setIgnoresMouseEvents:", !opaque);
    }
    static string Text(IntPtr value)
    {
        long length = CFStringGetLength(value); var buffer = new byte[length * 4 + 1];
        return CFStringGetCString(value, buffer, buffer.Length, 0x08000100) ? Encoding.UTF8.GetString(buffer, 0, Array.IndexOf(buffer, (byte)0)) : "";
    }
    static IntPtr Class(string name) => objc_getClass(name);
    static IntPtr Selector(string name) => sel_registerName(name);
    static IntPtr Send(IntPtr target, string selector) => target == IntPtr.Zero ? IntPtr.Zero : objc_msgSend(target, Selector(selector));
    static int SendInt(IntPtr target, string selector) => objc_msgSend_int(target, Selector(selector));
    static bool SendBool(IntPtr target, string selector, IntPtr argument) => objc_msgSend_bool(target, Selector(selector), argument);
    static void SendBool(IntPtr target, string selector, bool argument) => objc_msgSend_void(target, Selector(selector), argument);
    static CGPoint SendPoint(IntPtr target, string selector) => objc_msgSend_point(target, Selector(selector));
    static CGRect SendRect(IntPtr target, string selector) => objc_msgSend_rect(target, Selector(selector));
    [StructLayout(LayoutKind.Sequential)] internal struct CGPoint { public double X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct CGRect { public double X, Y, Width, Height; }
    const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics", CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation",
        IOKit = "/System/Library/Frameworks/IOKit.framework/IOKit", Objc = "/usr/lib/libobjc.A.dylib";
    [DllImport(CoreGraphics)] static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeTo);
    [DllImport(CoreGraphics)] [return: MarshalAs(UnmanagedType.I1)] static extern bool CGRectMakeWithDictionaryRepresentation(IntPtr dictionary, out CGRect rect);
    [DllImport(CoreGraphics)] static extern int CGGetActiveDisplayList(uint maximum, [Out] uint[] displays, out uint count);
    [DllImport(CoreGraphics)] static extern CGRect CGDisplayBounds(uint display);
    [DllImport(CoreGraphics)] static extern double CGEventSourceSecondsSinceLastEventType(int state, uint type);
    [DllImport(CoreFoundation)] static extern long CFArrayGetCount(IntPtr array);
    [DllImport(CoreFoundation)] static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, long index);
    [DllImport(CoreFoundation)] static extern IntPtr CFDictionaryGetValue(IntPtr dictionary, IntPtr key);
    [DllImport(CoreFoundation)] [return: MarshalAs(UnmanagedType.I1)] static extern bool CFNumberGetValue(IntPtr number, int type, out long value);
    [DllImport(CoreFoundation)] static extern long CFStringGetLength(IntPtr text);
    [DllImport(CoreFoundation)] [return: MarshalAs(UnmanagedType.I1)] static extern bool CFStringGetCString(IntPtr text, byte[] buffer, long size, uint encoding);
    [DllImport(CoreFoundation)] static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string text, uint encoding);
    [DllImport(CoreFoundation)] static extern void CFRelease(IntPtr value);
    [DllImport(IOKit)] static extern IntPtr IOPSCopyPowerSourcesInfo();
    [DllImport(IOKit)] static extern IntPtr IOPSGetProvidingPowerSourceType(IntPtr info);
    [DllImport(Objc)] static extern IntPtr objc_getClass(string name);
    [DllImport(Objc)] static extern IntPtr sel_registerName(string name);
    [DllImport(Objc)] static extern IntPtr objc_msgSend(IntPtr target, IntPtr selector);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern int objc_msgSend_int(IntPtr target, IntPtr selector);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] static extern bool objc_msgSend_bool(IntPtr target, IntPtr selector, IntPtr argument);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern void objc_msgSend_void(IntPtr target, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool argument);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern CGPoint objc_msgSend_point(IntPtr target, IntPtr selector);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern CGRect objc_msgSend_rect(IntPtr target, IntPtr selector);
}
internal sealed record CompanionOcrSnapshot(string Context, string Language, int Width, int Height, int WordCount,
    double CaptureMilliseconds, double RecognizeMilliseconds, CompanionOcrWord[] Words, long WindowHandle = 0, bool IsDesktop = false);
internal static class CompanionOcr
{
    internal static async Task<CompanionOcrSnapshot> ReadAsync(IntPtr handle, CancellationToken token)
    {
        var clock = Stopwatch.StartNew();
        var captured = await CompanionCaptureFallback.ReadAsync<CompanionWindowImage>(
            async () => await LinuxCapture.CaptureAsync(handle, 2560, false, token),
            async () => await LinuxCapture.CaptureAsync(handle, 2560, true, token), token);
        var image = captured.Image ?? throw new IOException("未能取得可用读屏画面");
        double captureTime = clock.Elapsed.TotalMilliseconds;
        var bytes = Convert.FromBase64String(image.DataUrl[(image.DataUrl.IndexOf(',') + 1)..]);
        // Apple Vision helper; it prints the same TSV layout as tesseract so parsing matches Linux.
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "runtime/BigBlueFish.MacOcr")) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        using var process = LinuxProcesses.Start(start);
        using var cancel = token.Register(() => { try { process.Kill(true); } catch { } });
        var output = process.StandardOutput.ReadToEndAsync(token); var error = process.StandardError.ReadToEndAsync(token);
        await process.StandardInput.BaseStream.WriteAsync(bytes, token); process.StandardInput.Close();
        await process.WaitForExitAsync(token); var text = await output;
        if (process.ExitCode != 0) throw new IOException("本地 OCR 未完成：" + await error);
        var words = new List<CompanionOcrWord>();
        foreach (var line in text.Split('\n').Skip(1))
        {
            var cells = line.TrimEnd('\r').Split('\t'); if (cells.Length < 12 || cells[0] != "5" || string.IsNullOrWhiteSpace(cells[11])) continue;
            if (double.TryParse(cells[6], out var x) && double.TryParse(cells[7], out var y) && double.TryParse(cells[8], out var width) && double.TryParse(cells[9], out var height)) words.Add(new(cells[11], x, y, width, height));
        }
        bool desktop = !captured.FullScreen && CompanionNative.IsDesktopWindow(handle);
        string identity = captured.FullScreen ? CompanionCaptureFallback.ScreenIdentity : CompanionNative.ForegroundIdentity(handle);
        string context = identity + "\n" + (desktop ? "画面是壁纸与快捷方式图标，没有展开的应用正文。" : CompanionOcrLayout.Format(words, image.Width, image.Height));
        return new(context, "zh-Hans+en-US", image.Width, image.Height, words.Count, captureTime, clock.Elapsed.TotalMilliseconds - captureTime, words.ToArray(), captured.FullScreen ? 0 : handle.ToInt64(), desktop);
    }
}
