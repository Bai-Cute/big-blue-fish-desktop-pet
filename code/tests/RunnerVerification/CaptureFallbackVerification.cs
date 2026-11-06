using BigBlueFish.Windows;
using System.IO;

internal static class CaptureFallbackVerification
{
    internal static async Task<int> Run(bool live)
    {
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
        int foreground = 0, screen = 0;
        Task<string?> Foreground() { foreground++; return Task.FromResult<string?>("foreground"); }
        Task<string?> Screen() { screen++; return Task.FromResult<string?>("screen"); }
        var result = await CompanionCaptureFallback.ReadAsync(Foreground, Screen, CancellationToken.None);
        Check(result.Image == "foreground" && !result.FullScreen && foreground == 1 && screen == 0, "foreground success does not capture full screen");
        result = await CompanionCaptureFallback.ReadAsync<string>(() => Task.FromResult<string?>(null), Screen, CancellationToken.None);
        Check(result.Image == "screen" && result.FullScreen && screen == 1, "empty foreground falls back to full screen");
        result = await CompanionCaptureFallback.ReadAsync<string>(() => throw new IOException("foreground unavailable"), Screen, CancellationToken.None);
        Check(result.Image == "screen" && result.FullScreen && screen == 2, "foreground capture exception falls back to full screen");
        result = await CompanionCaptureFallback.ReadAsync<string>(() => throw new IOException(), () => throw new IOException(), CancellationToken.None);
        Check(result.Image == null, "both failed captures return no image for ordinary speech generation");
        result = await CompanionCaptureFallback.ReadAsync<string>(() => Task.FromResult<string?>(null), () => Task.FromResult<string?>(null), CancellationToken.None);
        Check(result.Image == null, "both empty captures return no image");
        int before = screen;
        try { await CompanionCaptureFallback.ReadAsync<string>(() => throw new OperationCanceledException(), Screen, CancellationToken.None); throw new Exception("cancel swallowed"); }
        catch (OperationCanceledException) { Check(screen == before, "cancelled foreground does not start a screen capture"); }
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        before = foreground;
        try { await CompanionCaptureFallback.ReadAsync(Foreground, Screen, cancel.Token); throw new Exception("cancel swallowed"); }
        catch (OperationCanceledException) { Check(foreground == before, "pre-cancelled request captures nothing"); }
        using var during = new CancellationTokenSource(); before = screen;
        try { await CompanionCaptureFallback.ReadAsync<string>(() => { during.Cancel(); return Task.FromResult<string?>(null); }, Screen, during.Token); throw new Exception("cancel swallowed"); }
        catch (OperationCanceledException) { Check(screen == before, "cancellation between stages prevents a full-screen capture"); }
#if COMPANION_OCR
        if (live)
        {
            var actual = await CompanionOcr.ReadAsync(new IntPtr(-123), CancellationToken.None);
            Check(actual.WindowHandle == 0 && !actual.IsDesktop && actual.Context.StartsWith(CompanionCaptureFallback.ScreenIdentity), "actual invalid HWND produces full-screen OCR with truthful scene identity");
            Check(actual.Width > 0 && actual.Height > 0 && actual.Language.Length > 0, "actual desktop image passes through the installed Windows OCR engine");
        }
#endif
        Console.WriteLine($"RESULT {checks} capture fallback checks passed"); return 0;
    }
}
