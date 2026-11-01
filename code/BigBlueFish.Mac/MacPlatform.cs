using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BigBlueFish.Windows;

internal static class MacPlatform
{
    internal static string Decoder()
    {
        var configured = Environment.GetEnvironmentVariable("BIGBLUEFISH_FFMPEG");
        if (!string.IsNullOrEmpty(configured)) {
            if (Path.IsPathRooted(configured) && File.Exists(configured)) return configured;
            throw new FileNotFoundException("BIGBLUEFISH_FFMPEG 必须指向可执行 FFmpeg 的绝对路径。", configured);
        }
        foreach (var path in new[] { Path.Combine(AppContext.BaseDirectory, "runtime/ffmpeg"), "/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg" })
            if (File.Exists(path)) return path;
        throw new FileNotFoundException("缺少 FFmpeg。请先运行 brew install ffmpeg，或设置 BIGBLUEFISH_FFMPEG 为 FFmpeg 绝对路径。");
    }
    [DllImport("libBigBlueFish.Mac.dylib")] internal static extern int bbf_pointer(IntPtr window, out double x, out double y);
    [DllImport("libBigBlueFish.Mac.dylib")] internal static extern void bbf_clickthrough(IntPtr window, int enabled);
    [DllImport("libBigBlueFish.Mac.dylib")] internal static extern int bbf_application_active();
}

// The decoder writes to an owned pipe and is killed and reaped by LinuxVideoPlayer. No Linux runner guard is used.
internal static class LinuxProcesses
{
    internal static Process Start(ProcessStartInfo start) => Process.Start(start) ?? throw new IOException("无法启动动画解码器。");
}
