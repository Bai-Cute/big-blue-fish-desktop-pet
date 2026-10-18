using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using SkiaSharp;

namespace BigBlueFish.Windows;

internal static class LinuxCapture
{
    internal static async Task<CompanionWindowImage> CaptureAsync(IntPtr window, int maximum, bool fullScreen, CancellationToken token)
    {
        if (!CompanionNative.Wayland) return await Task.Run(() => { token.ThrowIfCancellationRequested(); return CompanionNative.Capture(window, maximum, fullScreen); }, token);
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "runtime/BigBlueFish.PortalCapture")) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        if (!fullScreen) start.ArgumentList.Add("--foreground");
        using var process = LinuxProcesses.Start(start); using var cancel = token.Register(() => { try { process.Kill(true); } catch { } });
        var errors = process.StandardError.ReadToEndAsync(token); using var bytes = new MemoryStream();
        await process.StandardOutput.BaseStream.CopyToAsync(bytes, token); await process.WaitForExitAsync(token);
        if (process.ExitCode != 0) throw new IOException((await errors).Trim());
        using var bitmap = SKBitmap.Decode(bytes.ToArray()) ?? throw new IOException("桌面截图未返回有效图像。");
        if ((long)bitmap.Width * bitmap.Height > 150_000_000) throw new IOException("截图尺寸过大。");
        return CompanionNative.Encode(bitmap, maximum);
    }
}

internal sealed record CompanionWindowImage(string DataUrl, int Width, int Height)
{
    internal void SaveJpeg(string path) => File.WriteAllBytes(path, Convert.FromBase64String(DataUrl[(DataUrl.IndexOf(',') + 1)..]));
}
internal static class CompanionNative
{
    static readonly object gate = new();
    static readonly IntPtr display = XOpenDisplay(IntPtr.Zero);
    static readonly XErrorHandler errorHandler = (_, _) => 0;
    static CompanionNative() { if (display != IntPtr.Zero) XSetErrorHandler(errorHandler); }
    internal static bool OnAC
    {
        get
        {
            if (App.Args.Contains("--test-mode") && App.Args.Contains("--test-battery")) return false;
            try {
                var supplies = Directory.GetDirectories("/sys/class/power_supply");
                bool battery = supplies.Any(p => File.Exists(p + "/type") && File.ReadAllText(p + "/type").Trim() == "Battery");
                return !battery || supplies.Any(p => File.Exists(p + "/online") && File.ReadAllText(p + "/online").Trim() == "1");
            } catch { return true; }
        }
    }
    internal static bool Wayland => Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") == "wayland";
    internal static ulong Root => display == IntPtr.Zero ? 0 : XDefaultRootWindow(display);
    static ulong Atom(string name) => XInternAtom(display, name, false);
    static byte[] Property(ulong window, string name, out int format)
    {
        format = 0;
        if (display == IntPtr.Zero) return [];
        if (XGetWindowProperty(display, window, Atom(name), 0, 4096, false, 0, out _, out format, out var count, out _, out var data) != 0 || data == IntPtr.Zero) return [];
        try { var bytes = new byte[checked((int)count * (format == 32 ? IntPtr.Size : Math.Max(1, format / 8)))]; Marshal.Copy(data, bytes, 0, bytes.Length); return bytes; }
        finally { XFree(data); }
    }
    static ulong[] Integers(ulong window, string property)
    {
        var bytes = Property(window, property, out var format); if (format != 32) return [];
        return Enumerable.Range(0, bytes.Length / IntPtr.Size).Select(i => IntPtr.Size == 8 ? BitConverter.ToUInt64(bytes, i * 8) : BitConverter.ToUInt32(bytes, i * 4)).ToArray();
    }
    internal static IntPtr GetForegroundWindow()
    {
        lock (gate) return display == IntPtr.Zero ? IntPtr.Zero : new IntPtr((long)(Integers(Root, "_NET_ACTIVE_WINDOW").FirstOrDefault()));
    }
    internal static bool IsDesktopWindow(IntPtr handle)
    {
        if (handle == IntPtr.Zero || (ulong)handle.ToInt64() == Root) return true;
        lock (gate) return display != IntPtr.Zero && Integers((ulong)handle.ToInt64(), "_NET_WM_WINDOW_TYPE").Contains(Atom("_NET_WM_WINDOW_TYPE_DESKTOP"));
    }
    internal static IntPtr ResolveForeground(IntPtr current, IntPtr previous)
    {
        lock (gate)
        {
            bool Valid(IntPtr handle) {
                if (handle == IntPtr.Zero || display == IntPtr.Zero) return false;
                ulong window = (ulong)handle.ToInt64();
                return XGetWindowAttributes(display, window, out var attributes) != 0 && attributes.map_state == 2
                    && !Integers(window, "_NET_WM_STATE").Contains(Atom("_NET_WM_STATE_HIDDEN"))
                    && Integers(window, "_NET_WM_PID").FirstOrDefault() != (ulong)Environment.ProcessId;
            }
            if (Valid(current)) return current;
            if (Valid(previous)) return previous;
            return new IntPtr((long)Root);
        }
    }
    internal static string ForegroundIdentity(IntPtr handle)
    {
        lock (gate) {
            if (IsDesktopWindow(handle)) return "前台窗口：Linux 桌面\n画面是壁纸与快捷方式。";
            var title = Encoding.UTF8.GetString(Property((ulong)handle.ToInt64(), "_NET_WM_NAME", out _)).TrimEnd('\0');
            if (title.Length == 0) title = Encoding.UTF8.GetString(Property((ulong)handle.ToInt64(), "WM_NAME", out _)).TrimEnd('\0');
            var application = Encoding.UTF8.GetString(Property((ulong)handle.ToInt64(), "WM_CLASS", out _)).Replace('\0', ' ').Trim();
            return $"前台应用：{application}\n窗口标题：{title}";
        }
    }
    internal static bool IsFullscreen()
    {
        lock (gate) {
            var window = GetForegroundWindow();
            return window != IntPtr.Zero && Integers((ulong)window.ToInt64(), "_NET_WM_STATE").Contains(Atom("_NET_WM_STATE_FULLSCREEN"));
        }
    }
    internal static double IdleSeconds
    {
        get {
            if (display == IntPtr.Zero) return 10;
            lock (gate) { var info = XScreenSaverAllocInfo(); if (info == IntPtr.Zero) return 0;
                try { XScreenSaverQueryInfo(display, Root, info); return Marshal.PtrToStructure<SaverInfo>(info).idle / 1000d; } finally { XFree(info); } }
        }
    }
    internal static CompanionWindowImage Capture(IntPtr handle, int maximum, bool full)
    {
        lock (gate)
        {
            if (display == IntPtr.Zero) throw new IOException("当前桌面未提供 X11 读屏连接。");
            if (Wayland) throw new IOException("当前为 Wayland 桌面，读屏需要桌面授权门户。");
            if (XGetWindowAttributes(display, Root, out var screen) == 0) throw new IOException("无法读取桌面尺寸。");
            int x = 0, y = 0, width = screen.width, height = screen.height, windowX = 0, windowY = 0;
            bool application = !full && !IsDesktopWindow(handle);
            if (application)
            {
                if (XGetWindowAttributes(display, (ulong)handle.ToInt64(), out var window) == 0 || window.map_state != 2) throw new IOException("前台窗口不可见。");
                XTranslateCoordinates(display, (ulong)handle.ToInt64(), Root, 0, 0, out x, out y, out _);
                windowX = x; windowY = y;
                width = Math.Min(window.width + Math.Min(0, x), screen.width - Math.Max(0, x));
                height = Math.Min(window.height + Math.Min(0, y), screen.height - Math.Max(0, y)); x = Math.Max(0, x); y = Math.Max(0, y);
            }
            if (width <= 0 || height <= 0 || (long)width * height > 150_000_000) throw new IOException("截图区域无效。");
            ulong pixmap = 0; bool redirected = false;
            IntPtr pointer;
            try {
                if (application) {
                    // Read the actual application's backing image, excluding our own topmost pet/bubble.
                    ulong drawable = (ulong)handle.ToInt64(); int sourceX = windowX, sourceY = windowY, border = 0;
                    if (XGetSelectionOwner(display, Atom("_NET_WM_CM_S0")) == 0) { XCompositeRedirectWindow(display, drawable, 0); redirected = true; XSync(display, false); }
                    else {
                        // A compositor redirects root children; reparenting window managers place the client inside a frame.
                        for (int depth = 0; depth < 32; depth++) {
                            if (XQueryTree(display, drawable, out _, out var parent, out var children, out _) == 0) break;
                            if (children != IntPtr.Zero) XFree(children);
                            if (parent == 0 || parent == Root) break;
                            drawable = parent;
                        }
                        XTranslateCoordinates(display, drawable, Root, 0, 0, out sourceX, out sourceY, out _);
                        if (XGetWindowAttributes(display, drawable, out var frame) != 0) border = frame.border;
                    }
                    pixmap = XCompositeNameWindowPixmap(display, drawable);
                    pointer = XGetImage(display, pixmap, x - sourceX + border, y - sourceY + border, (uint)width, (uint)height, ulong.MaxValue, 2);
                } else pointer = XGetImage(display, Root, x, y, (uint)width, (uint)height, ulong.MaxValue, 2);
            } finally {
                if (pixmap != 0) XFreePixmap(display, pixmap);
                if (redirected) XCompositeUnredirectWindow(display, (ulong)handle.ToInt64(), 0);
            }
            if (pointer == IntPtr.Zero) throw new IOException("无法读取桌面像素。");
            try
            {
                var image = Marshal.PtrToStructure<XImage>(pointer);
                if (image.bits_per_pixel != 32 || image.byte_order != 0) throw new IOException("当前桌面像素格式不受支持。");
                var pixels = new byte[image.bytes_per_line * height]; Marshal.Copy(image.data, pixels, 0, pixels.Length);
                using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
                for (int row = 0; row < height; row++) Marshal.Copy(pixels, row * image.bytes_per_line, bitmap.GetPixels() + row * bitmap.RowBytes, width * 4);
                return Encode(bitmap, maximum);
            }
            finally { XDestroyImage(pointer); }
        }
    }
    internal static CompanionWindowImage Encode(SKBitmap bitmap, int maximum)
    {
        double scale = Math.Min(1d, maximum / (double)Math.Max(bitmap.Width, bitmap.Height));
        using var small = scale < 1 ? bitmap.Resize(new SKImageInfo(Math.Max(1, (int)(bitmap.Width * scale)), Math.Max(1, (int)(bitmap.Height * scale))), new SKSamplingOptions(SKCubicResampler.Mitchell)) : null;
        using var image = SKImage.FromBitmap(small ?? bitmap); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return new("data:image/png;base64," + Convert.ToBase64String(png.ToArray()), image.Width, image.Height);
    }
    internal static void SetInputShape(IntPtr window, byte[] pixels, double scale, bool mirror)
    {
        if (display == IntPtr.Zero || window == IntPtr.Zero) return;
        var regions = new List<XRectangle>();
        for (int y = 0; y < 360; y++)
        {
            int start = -1;
            for (int x = 0; x <= 640; x++)
            {
                bool opaque = x < 640 && pixels[(y * 640 + x) * 4 + 3] >= 8;
                if (opaque && start < 0) start = x;
                if (!opaque && start >= 0) {
                    int left = mirror ? 640 - x : start;
                    regions.Add(new() { x = (short)Math.Floor(left * scale), y = (short)Math.Floor(y * scale), width = (ushort)Math.Max(1, Math.Ceiling((x - start) * scale)), height = (ushort)Math.Max(1, Math.Ceiling(scale)) }); start = -1;
                }
            }
        }
        lock (gate) { XShapeCombineRectangles(display, (ulong)window.ToInt64(), 2, 0, 0, regions.ToArray(), regions.Count, 0, 0); XFlush(display); }
    }
    [StructLayout(LayoutKind.Sequential)] struct XRectangle { public short x, y; public ushort width, height; }
    [DllImport("libXext.so.6")] static extern void XShapeCombineRectangles(IntPtr display, ulong window, int kind, int x, int y, XRectangle[] rectangles, int count, int operation, int ordering);
    [DllImport("libX11.so.6")] static extern int XFlush(IntPtr display);
    [DllImport("libX11.so.6")] static extern int XSync(IntPtr display, bool discard);
    [DllImport("libX11.so.6")] static extern ulong XGetSelectionOwner(IntPtr display, ulong selection);
    [DllImport("libX11.so.6")] static extern int XQueryTree(IntPtr display, ulong window, out ulong root, out ulong parent, out IntPtr children, out uint count);
    [DllImport("libX11.so.6")] static extern void XFreePixmap(IntPtr display, ulong pixmap);
    [DllImport("libXcomposite.so.1")] static extern ulong XCompositeNameWindowPixmap(IntPtr display, ulong window);
    [DllImport("libXcomposite.so.1")] static extern void XCompositeRedirectWindow(IntPtr display, ulong window, int update);
    [DllImport("libXcomposite.so.1")] static extern void XCompositeUnredirectWindow(IntPtr display, ulong window, int update);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int XErrorHandler(IntPtr display, IntPtr error);
    [DllImport("libX11.so.6")] static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")] static extern ulong XDefaultRootWindow(IntPtr display);
    [DllImport("libX11.so.6")] static extern ulong XInternAtom(IntPtr display, string name, bool onlyIfExists);
    [DllImport("libX11.so.6")] static extern int XGetWindowProperty(IntPtr display, ulong window, ulong property, long offset, long length, bool delete, ulong requested, out ulong actual, out int format, out ulong count, out ulong rest, out IntPtr data);
    [DllImport("libX11.so.6")] static extern int XFree(IntPtr pointer);
    [DllImport("libX11.so.6")] static extern IntPtr XSetErrorHandler(XErrorHandler handler);
    [DllImport("libX11.so.6")] static extern int XGetWindowAttributes(IntPtr display, ulong window, out Attributes attributes);
    [DllImport("libX11.so.6")] static extern int XTranslateCoordinates(IntPtr display, ulong source, ulong target, int x, int y, out int resultX, out int resultY, out ulong child);
    [DllImport("libX11.so.6")] static extern IntPtr XGetImage(IntPtr display, ulong drawable, int x, int y, uint width, uint height, ulong mask, int format);
    [DllImport("libX11.so.6")] static extern int XDestroyImage(IntPtr image);
    [DllImport("libXss.so.1")] static extern IntPtr XScreenSaverAllocInfo();
    [DllImport("libXss.so.1")] static extern int XScreenSaverQueryInfo(IntPtr display, ulong window, IntPtr info);
    [StructLayout(LayoutKind.Sequential)] struct Attributes { public int x, y, width, height, border, depth; public IntPtr visual; public ulong root; public int @class, bitGravity, winGravity, backing; public ulong planes, pixel; public int save; public ulong colormap; public int installed, map_state; public long allMasks, ownMasks, propagate; public int redirect; public IntPtr screen; }
    [StructLayout(LayoutKind.Sequential)] struct XImage { public int width, height, offset, format; public IntPtr data; public int byte_order, bitmap_unit, bit_order, pad, depth, bytes_per_line, bits_per_pixel; public ulong red_mask, green_mask, blue_mask; }
    [StructLayout(LayoutKind.Sequential)] struct SaverInfo { public ulong window; public int state, kind; public ulong tilOrSince, idle, eventMask; }
}
internal sealed record CompanionOcrSnapshot(string Context, string Language, int Width, int Height, int WordCount,
    double CaptureMilliseconds, double RecognizeMilliseconds, CompanionOcrWord[] Words, long WindowHandle = 0, bool IsDesktop = false);
internal static class CompanionOcr
{
    internal static async Task<CompanionOcrSnapshot> ReadAsync(IntPtr handle, CancellationToken token)
    {
        var clock = Stopwatch.StartNew();
        var image = await LinuxCapture.CaptureAsync(handle, 2560, false, token);
        double captureTime = clock.Elapsed.TotalMilliseconds;
        var bytes = Convert.FromBase64String(image.DataUrl[(image.DataUrl.IndexOf(',') + 1)..]);
        var start = new ProcessStartInfo("/usr/bin/tesseract") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var item in new[] { "stdin", "stdout", "-l", "chi_sim+eng", "tsv" }) start.ArgumentList.Add(item);
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
        bool desktop = !CompanionNative.Wayland && CompanionNative.IsDesktopWindow(handle);
        string context = (CompanionNative.Wayland ? "当前桌面授权的前台窗口" : CompanionNative.ForegroundIdentity(handle)) + "\n" + (desktop ? "画面是壁纸与快捷方式图标，没有展开的应用正文。" : CompanionOcrLayout.Format(words, image.Width, image.Height));
        return new(context, "chi_sim+eng", image.Width, image.Height, words.Count, captureTime, clock.Elapsed.TotalMilliseconds - captureTime, words.ToArray(), handle.ToInt64(), desktop);
    }
}

