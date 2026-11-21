using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace BigBlueFish.Windows;

// The public x64 GStreamer 1.x C ABI. No Python runtime or external player is used.
internal static class CompanionMedia
{
    const string Core = "bbf-gst", App = "bbf-gst-app", Glib = "bbf-glib", Video = "bbf-gst-video";
    static readonly object gate = new();
    static bool initialized;
    static IntPtr dllDirectory;
    internal const ulong Second = 1_000_000_000;
    internal static bool HardwareAvailable { get; private set; }
    // macOS: VideoToolbox (vtdec, in the applemedia plugin) decodes the colour and the alpha stream of each clip on the
    // media engine; CompanionMediaStream pairs them with alphacombine (Apple Silicon decodes VP9 in hardware).
    internal static string HardwareFactory => OperatingSystem.IsWindows() ? "d3d12vp9alphadecodebin" : OperatingSystem.IsMacOS() ? "vtdec" : "vavp9alphadecodebin";
    internal static string RuntimeDirectory => Path.Combine(AppContext.BaseDirectory, "media", "gstreamer");
    // Windows and macOS ship a pinned GStreamer subset; Linux uses the distribution packages.
    static bool Bundled => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    static CompanionMedia() => NativeLibrary.SetDllImportResolver(typeof(CompanionMedia).Assembly, Resolve);
    static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? path)
    {
        var file = name switch
        {
            Core => OperatingSystem.IsWindows() ? "gstreamer-1.0-0.dll" : OperatingSystem.IsMacOS() ? "lib/libgstreamer-1.0.0.dylib" : "libgstreamer-1.0.so.0",
            App => OperatingSystem.IsWindows() ? "gstapp-1.0-0.dll" : OperatingSystem.IsMacOS() ? "lib/libgstapp-1.0.0.dylib" : "libgstapp-1.0.so.0",
            Glib => OperatingSystem.IsWindows() ? "glib-2.0-0.dll" : OperatingSystem.IsMacOS() ? "lib/libglib-2.0.0.dylib" : "libglib-2.0.so.0",
            Video => OperatingSystem.IsWindows() ? "gstvideo-1.0-0.dll" : OperatingSystem.IsMacOS() ? "lib/libgstvideo-1.0.0.dylib" : "libgstvideo-1.0.so.0",
            _ => null
        };
        if (file == null) return IntPtr.Zero;
        return NativeLibrary.Load(Bundled ? Path.Combine(RuntimeDirectory, file) : file);
    }

    internal static void Initialize()
    {
        lock (gate)
        {
            if (initialized) return;
            if (IntPtr.Size != 8) throw new PlatformNotSupportedException("动画组件需要64位进程。");
            string[] keys = ["GST_PLUGIN_SYSTEM_PATH_1_0", "GST_PLUGIN_PATH_1_0", "GST_REGISTRY_1_0", "GST_REGISTRY_FORK"];
            var prior = Array.ConvertAll(keys, Environment.GetEnvironmentVariable);
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    if (!SetDefaultDllDirectories(0x1000)) throw new IOException("无法准备动画组件加载路径。");
                    dllDirectory = AddDllDirectory(RuntimeDirectory);
                    if (dllDirectory == IntPtr.Zero) throw new IOException("无法加载随包动画组件目录。");
                }
                if (Bundled)
                {
                    // Only the pinned, bundled plugins participate in Windows and macOS playback.
                    Environment.SetEnvironmentVariable("GST_PLUGIN_SYSTEM_PATH_1_0", "");
                    Environment.SetEnvironmentVariable("GST_PLUGIN_PATH_1_0", "");
                    var cache = Path.Combine(Environment.CurrentDirectory, "cache", "gstreamer");
                    Directory.CreateDirectory(cache);
                    Environment.SetEnvironmentVariable("GST_REGISTRY_1_0", Path.Combine(cache, "registry.bin"));
                }
                Environment.SetEnvironmentVariable("GST_REGISTRY_FORK", "no");
                gst_init(IntPtr.Zero, IntPtr.Zero);
                if (Bundled)
                {
                    // The hardware decoder's plugin is optional: without it the compatibility clips play on the CPU.
                    string hardwarePlugin = OperatingSystem.IsMacOS() ? "applemedia" : "d3d12";
                    foreach (var plugin in new[] { "coreelements", "app", "matroska", "videoparsersbad", "vpx", "codecalpha", "videoconvertscale", "videorate", hardwarePlugin })
                    {
                        var handle = gst_plugin_load_file(Path.Combine(RuntimeDirectory, "plugins", OperatingSystem.IsWindows() ? "gst" + plugin + ".dll" : "libgst" + plugin + ".dylib"), out var error);
                        if (handle == IntPtr.Zero && plugin == hardwarePlugin) { if (error != IntPtr.Zero) Error(error); continue; }
                        if (handle == IntPtr.Zero) throw new IOException("动画组件加载失败：" + plugin + "：" + Error(error));
                        gst_object_unref(handle);
                    }
                }
                var factory = gst_element_factory_find(HardwareFactory);
                // vtdec is there on any Mac; VideoToolbox would decode VP9 in software where the media engine has no VP9.
                HardwareAvailable = factory != IntPtr.Zero && (!OperatingSystem.IsMacOS() || HardwareVp9OnMac());
                if (factory != IntPtr.Zero) gst_object_unref(factory);
                initialized = true;
            }
            finally
            {
                // Child CLI agents retain the user's multimedia environment.
                for (int i = 0; i < keys.Length; i++) Environment.SetEnvironmentVariable(keys[i], prior[i]);
            }
        }
    }

    // VP9 is a supplemental VideoToolbox decoder, registered on request (macOS 11 and later).
    static bool HardwareVp9OnMac()
    {
        const uint Vp9 = 0x76703039; // 'vp09'
        try { VTRegisterSupplementalVideoDecoderIfAvailable(Vp9); return VTIsHardwareDecodeSupported(Vp9); }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }
    const string VideoToolbox = "/System/Library/Frameworks/VideoToolbox.framework/VideoToolbox";
    [DllImport(VideoToolbox)] static extern void VTRegisterSupplementalVideoDecoderIfAvailable(uint codecType);
    [DllImport(VideoToolbox)] [return: MarshalAs(UnmanagedType.U1)] static extern bool VTIsHardwareDecodeSupported(uint codecType);

    internal static string Error(IntPtr error)
    {
        if (error == IntPtr.Zero) return "未知动画错误";
        try { return Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(error, 8)) ?? "未知动画错误"; }
        finally { g_error_free(error); }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MapInfo
    {
        internal IntPtr Memory; internal int Flags; internal IntPtr Data;
        internal nuint Size, MaxSize;
        internal IntPtr User0, User1, User2, User3, Reserved0, Reserved1, Reserved2, Reserved3;
    }

    // GstMiniObject is 64 bytes on both supported x64 platforms; GstBuffer.pts follows pool.
    internal static double Timestamp(IntPtr buffer) => unchecked((ulong)Marshal.ReadInt64(buffer, 72)) is var ns && ns != ulong.MaxValue ? ns / (double)Second : 0;
    internal static string Quote(string value) => "\"" + value.Replace("\\", "/").Replace("\"", "\\\"") + "\"";
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr AddDllDirectory(string directory);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetDefaultDllDirectories(uint flags);
    [DllImport(Core, CallingConvention = CallingConvention.Cdecl)] static extern void gst_init(IntPtr argc, IntPtr argv);
    [DllImport(Core, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr gst_plugin_load_file([MarshalAs(UnmanagedType.LPUTF8Str)] string file, out IntPtr error);
    [DllImport(Core, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr gst_element_factory_find([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Core, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr gst_parse_launch([MarshalAs(UnmanagedType.LPUTF8Str)] string pipeline, out IntPtr error);
    [DllImport(Core, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr gst_bin_get_by_name(IntPtr bin, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Core, CallingConvention = CallingConvention.Cdecl)] internal static extern int gst_element_set_state(IntPtr element, int state);
    [DllImport(Core, CallingConvention = CallingConvention.Cdecl)] internal static extern int gst_element_get_state(IntPtr element, out int state, out int pending, ulong timeout);
    [DllImport(Core, CallingConvention = CallingConvention.Cdecl)] internal static extern int gst_element_seek(IntPtr element, double rate, int format, int flags, int startType, long start, int stopType, long stop);
    [DllImport(Core, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr gst_element_get_bus(IntPtr element);
    [DllImport(Core, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr gst_bus_pop_filtered(IntPtr bus, uint types);
    [DllImport(Core, CallingConvention = CallingConvention.Cdecl)] internal static extern void gst_message_parse_error(IntPtr message, out IntPtr error, out IntPtr debug);
    [DllImport(Core, CallingConvention = CallingConvention.Cdecl)] internal static extern void gst_object_unref(IntPtr obj);
    [DllImport(Core, CallingConvention = CallingConvention.Cdecl)] internal static extern void gst_mini_object_unref(IntPtr obj);
    [DllImport(Core, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr gst_sample_get_buffer(IntPtr sample);
    [DllImport(Core, CallingConvention = CallingConvention.Cdecl)] internal static extern int gst_buffer_map(IntPtr buffer, out MapInfo info, int flags);
    [DllImport(Core, CallingConvention = CallingConvention.Cdecl)] internal static extern void gst_buffer_unmap(IntPtr buffer, ref MapInfo info);
    [DllImport(Video, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr gst_buffer_get_video_meta(IntPtr buffer);
    [DllImport(App, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr gst_app_sink_try_pull_sample(IntPtr sink, ulong timeout);
    [DllImport(App, CallingConvention = CallingConvention.Cdecl)] internal static extern int gst_app_sink_is_eos(IntPtr sink);
    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)] static extern void g_error_free(IntPtr error);
    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)] internal static extern void g_free(IntPtr memory);
}

internal sealed class CompanionMediaStream : IDisposable
{
    readonly object sync = new();
    IntPtr pipeline, sink, bus;
    bool disposed, eos;
    internal int Width { get; }
    internal int Height { get; }
    internal bool Hardware { get; }
    // macOS hardware frames are AV12 (Y, interleaved CbCr and alpha planes, CompanionMediaFrame.Offsets) at the clip's size.
    internal bool Planar { get; }
    internal string Description => Hardware ? "GPU VP9 + alpha" : "CPU VP9 + alpha (24 fps)";

    internal CompanionMediaStream(string file, bool hardware, int width, int height, double start, double? end, double rate)
    {
        CompanionMedia.Initialize();
        Hardware = hardware; Width = width; Height = height;
        var decoder = hardware ? CompanionMedia.HardwareFactory : "vp9alphadecodebin";
        var transform = hardware
            ? (OperatingSystem.IsWindows()
                ? "d3d12convert ! video/x-raw(memory:D3D12Memory),format=BGRA,width=" + width + ",height=" + height + " ! d3d12download"
                : "vapostproc")
            : "videoconvertscale";
        const string frames = "appsink name=frames sync=true max-buffers=1 drop=true wait-on-eos=false max-lateness=40000000 qos=true";
        var text = $"filesrc location={CompanionMedia.Quote(file)} ! matroskademux ! vp9parse ! {decoder} ! {transform} ! video/x-raw,format=BGRA,width={width},height={height} ! {frames}";
        if (hardware && OperatingSystem.IsMacOS())
        {
            // VideoToolbox decodes both streams; alphacombine pairs each colour frame with its alpha frame. Converting the
            // result to BGRA here (videoconvertscale) would cost a quarter of a CPU core at 1280x720 and 60 fps, so the
            // caller converts it with Accelerate (MacVideoFrames).
            Planar = true;
            text = $"filesrc location={CompanionMedia.Quote(file)} ! matroskademux ! vp9parse ! codecalphademux name=d "
                + "d.src ! queue ! vtdec ! video/x-raw,format=NV12 ! a.sink d.alpha ! queue ! vtdec ! video/x-raw,format=NV12 ! a.alpha "
                + $"alphacombine name=a ! video/x-raw,format=AV12,width={width},height={height} ! {frames}";
        }
        pipeline = CompanionMedia.gst_parse_launch(text, out var error);
        try
        {
            if (error != IntPtr.Zero) throw new IOException(CompanionMedia.Error(error));
            if (pipeline == IntPtr.Zero) throw new IOException("动画管线未创建。");
            sink = CompanionMedia.gst_bin_get_by_name(pipeline, "frames");
            bus = CompanionMedia.gst_element_get_bus(pipeline);
            if (CompanionMedia.gst_element_set_state(pipeline, 3) == 0) throw new IOException("动画预加载失败。");
            if (CompanionMedia.gst_element_get_state(pipeline, out _, out _, 8 * CompanionMedia.Second) == 0)
                throw new IOException(ReadError() ?? "动画解码未能就绪。");
            if (start > 0 || end.HasValue || rate != 1)
                if (CompanionMedia.gst_element_seek(sink, rate, 3, 3, 1, (long)(start * CompanionMedia.Second), end.HasValue ? 1 : 0, end.HasValue ? (long)(end.Value * CompanionMedia.Second) : 0) == 0)
                    throw new IOException("动画片段定位失败。");
        }
        catch { Dispose(); throw; }
    }

    internal void SetPaused(bool paused)
    {
        lock (sync) { if (!disposed) CompanionMedia.gst_element_set_state(pipeline, paused ? 3 : 4); }
    }

    internal CompanionMediaFrame? Read()
    {
        lock (sync)
        {
            if (disposed || eos) return null;
            var sample = CompanionMedia.gst_app_sink_try_pull_sample(sink, 200_000_000);
            if (sample == IntPtr.Zero)
            {
                var error = ReadError();
                if (error != null) throw new IOException(error);
                if (sink != IntPtr.Zero && CompanionMedia.gst_app_sink_is_eos(sink) != 0) eos = true;
                return null;
            }
            return new(sample, Width, Height, Planar);
        }
    }

    internal bool Ended
    {
        get
        {
            lock (sync)
            {
                if (disposed || eos) return true;
                var error = ReadError();
                if (error != null) throw new IOException(error);
                if (sink != IntPtr.Zero && CompanionMedia.gst_app_sink_is_eos(sink) != 0) eos = true;
                return eos;
            }
        }
    }

    string? ReadError()
    {
        if (bus == IntPtr.Zero) return null;
        while (true)
        {
            var m = CompanionMedia.gst_bus_pop_filtered(bus, 3);
            if (m == IntPtr.Zero) return null;
            try
            {
                if (Marshal.ReadInt32(m, 64) == 1) { eos = true; continue; }
                CompanionMedia.gst_message_parse_error(m, out var error, out var debug);
                if (debug != IntPtr.Zero) CompanionMedia.g_free(debug);
                return CompanionMedia.Error(error);
            }
            finally { CompanionMedia.gst_mini_object_unref(m); }
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            if (pipeline != IntPtr.Zero) CompanionMedia.gst_element_set_state(pipeline, 1);
            if (sink != IntPtr.Zero) CompanionMedia.gst_object_unref(sink);
            if (bus != IntPtr.Zero) CompanionMedia.gst_object_unref(bus);
            if (pipeline != IntPtr.Zero) CompanionMedia.gst_object_unref(pipeline);
            pipeline = sink = bus = IntPtr.Zero;
        }
    }
}

internal sealed class CompanionMediaFrame : IDisposable
{
    IntPtr sample, buffer;
    CompanionMedia.MapInfo map;
    internal IntPtr Data => map.Data;
    internal int Stride { get; }
    internal int Width { get; }
    internal int Height { get; }
    internal double Seconds { get; }
    // Planar (AV12) frames: where the Y, CbCr and alpha planes start in Data, and their row lengths in bytes.
    internal long[] Offsets { get; } = [];
    internal int[] Strides { get; } = [];

    internal CompanionMediaFrame(IntPtr sample, int width, int height, bool planar = false)
    {
        this.sample = sample; Width = width; Height = height;
        buffer = CompanionMedia.gst_sample_get_buffer(sample);
        try
        {
            Seconds = CompanionMedia.Timestamp(buffer);
            if (CompanionMedia.gst_buffer_map(buffer, out map, 1) == 0) throw new IOException("动画帧映射失败。");
            if (planar) { (Offsets, Strides) = Planes(width, height); return; }
            Stride = checked((int)(map.Size / (nuint)height));
            if (Stride < width * 4 || map.Size < (nuint)(Stride * height)) throw new IOException("动画帧尺寸不完整。");
        }
        catch { Dispose(); throw; }
    }

    // The plane layout from the buffer's GstVideoMeta (offsets are into the whole buffer, which the map joins), or the
    // packed layout when it has none. GstVideoMeta (GStreamer 1.x, 64-bit): GstMeta (16 bytes), the buffer, flags,
    // format, id, width, height, n_planes (at 44), gsize offset[4] (at 48), gint stride[4] (at 80).
    (long[], int[]) Planes(int width, int height)
    {
        long[] offsets; int[] strides;
        var meta = CompanionMedia.gst_buffer_get_video_meta(buffer);
        if (meta != IntPtr.Zero && Marshal.ReadInt32(meta, 44) == 3)
        {
            offsets = [Marshal.ReadInt64(meta, 48), Marshal.ReadInt64(meta, 56), Marshal.ReadInt64(meta, 64)];
            strides = [Marshal.ReadInt32(meta, 80), Marshal.ReadInt32(meta, 84), Marshal.ReadInt32(meta, 88)];
        }
        else { offsets = [0, (long)width * height, (long)width * height * 3 / 2]; strides = [width, width, width]; }
        int[] rows = [height, height / 2, height];
        for (int i = 0; i < 3; i++)
            if (offsets[i] < 0 || strides[i] < width || (ulong)(offsets[i] + (long)strides[i] * rows[i]) > (ulong)map.Size)
                throw new IOException("动画帧尺寸不完整。");
        return (offsets, strides);
    }

    public void Dispose()
    {
        if (sample == IntPtr.Zero) return;
        if (map.Data != IntPtr.Zero) CompanionMedia.gst_buffer_unmap(buffer, ref map);
        CompanionMedia.gst_mini_object_unref(sample);
        sample = IntPtr.Zero;
    }
}
