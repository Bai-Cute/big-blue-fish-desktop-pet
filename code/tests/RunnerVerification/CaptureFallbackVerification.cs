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
            int oversizedWidth = checked((int)Windows.Media.Ocr.OcrEngine.MaxImageDimension + 128);
            using var bitmap = new System.Drawing.Bitmap(oversizedWidth, 160);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            using (var font = new System.Drawing.Font("Microsoft YaHei UI", 32))
            {
                graphics.Clear(System.Drawing.Color.White);
                graphics.DrawString("蓝色大肥鱼 正在测试文字识别", font, System.Drawing.Brushes.Black, 30, 45);
            }
            using var bytes = new MemoryStream(); bitmap.Save(bytes, System.Drawing.Imaging.ImageFormat.Png);
            var oversized = new CompanionWindowImage("data:image/png;base64," + Convert.ToBase64String(bytes.ToArray()), oversizedWidth, 160);
            var scaled = await CompanionOcr.RecognizeAsync(oversized, "synthetic OCR fixture", 0, CancellationToken.None);
            Console.WriteLine($"OCR dimension fixture input={oversizedWidth}x160 output={scaled.Width}x{scaled.Height} limit={Windows.Media.Ocr.OcrEngine.MaxImageDimension} words={scaled.WordCount}");
            Check(scaled.Width <= Windows.Media.Ocr.OcrEngine.MaxImageDimension && scaled.Height > 0 && scaled.WordCount > 0,
                "oversized direct OCR input is scaled to the engine limit and still recognizes actual text");
        }
#endif
        Console.WriteLine($"RESULT {checks} capture fallback checks passed"); return 0;
    }
}
