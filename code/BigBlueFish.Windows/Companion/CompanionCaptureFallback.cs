using System;
using System.Threading;
using System.Threading.Tasks;

namespace BigBlueFish.Windows;

internal readonly record struct CompanionCaptureResult<T>(T? Image, bool FullScreen) where T : class;

internal static class CompanionCaptureFallback
{
    internal const string ScreenIdentity = "本轮画面来自全部显示器的全屏截图，可能同时包含前台应用、后台窗口和桌面。";

    internal static async Task<CompanionCaptureResult<T>> ReadAsync<T>(Func<Task<T?>> foreground,
        Func<Task<T?>> screen, CancellationToken token) where T : class
    {
        foreach (bool fullScreen in new[] { false, true })
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var image = await (fullScreen ? screen() : foreground()).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (image != null) return new(image, fullScreen);
                DiagnosticLog.Write(DiagnosticEvent.ScreenReadFallback, kind: fullScreen ? "screen" : "foreground", state: "empty");
            }
            catch (Exception error) when (error is not OperationCanceledException && !token.IsCancellationRequested)
            {
                DiagnosticLog.Write(DiagnosticEvent.ScreenReadFallback, kind: fullScreen ? "screen" : "foreground", error: error);
            }
        }
        return new(null, false);
    }
}
