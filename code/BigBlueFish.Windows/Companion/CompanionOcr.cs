#if COMPANION_OCR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace BigBlueFish.Windows;

internal sealed record CompanionOcrSnapshot(string Context, string Language, int Width, int Height,
    int WordCount, double CaptureMilliseconds, double RecognizeMilliseconds, CompanionOcrWord[] Words,
    long WindowHandle = 0, bool IsDesktop = false);

internal static class CompanionOcr
{
    internal static async Task<CompanionOcrSnapshot> ReadAsync(IntPtr handle, CancellationToken token)
    {
        return await Task.Run(async () =>
        {
            token.ThrowIfCancellationRequested();
            var watch = Stopwatch.StartNew();
            var image = CompanionScreenCapture.Capture(handle, Math.Min(2560, (int)OcrEngine.MaxImageDimension), true)
                ?? throw new IOException("未能读取当前窗口画面");
            var identity = CompanionNative.ForegroundIdentity(handle);
            double captureMs = watch.Elapsed.TotalMilliseconds;
            var result = await RecognizeAsync(image, identity, captureMs, token, CompanionNative.IsDesktopWindow(handle)).ConfigureAwait(false);
            return result with { WindowHandle = handle.ToInt64() };
        }, token).ConfigureAwait(false);
    }

    internal static async Task<CompanionOcrSnapshot> RecognizeAsync(CompanionWindowImage image,
        string identity, double captureMs, CancellationToken token, bool desktop = false)
    {
        var watch = new Stopwatch();
        var languages = OcrEngine.AvailableRecognizerLanguages;
        var language = languages.FirstOrDefault(x => x.LanguageTag.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            ?? languages.FirstOrDefault(x => x.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            ?? languages.FirstOrDefault();
        if (language == null)
            throw new IOException("Windows 尚未安装文字识别语言，请在语言设置中安装简体中文文字识别");
        var engine = OcrEngine.TryCreateFromLanguage(new Language(language.LanguageTag))
            ?? throw new IOException("无法启动本地文字识别");
        var bytes = Convert.FromBase64String(image.DataUrl[(image.DataUrl.IndexOf(',') + 1)..]);
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync().AsTask(token).ConfigureAwait(false);
            writer.DetachStream();
        }
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(token).ConfigureAwait(false);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore)
            .AsTask(token).ConfigureAwait(false);
        watch.Restart();
        var result = await engine.RecognizeAsync(bitmap).AsTask(token).ConfigureAwait(false);
        double recognizeMs = watch.Elapsed.TotalMilliseconds;
        var words = result.Lines.SelectMany(x => x.Words).Select(x => new CompanionOcrWord(
            x.Text, x.BoundingRect.X, x.BoundingRect.Y, x.BoundingRect.Width, x.BoundingRect.Height)).ToArray();
        // Desktop shortcut captions are background, not an open document or task.
        // Small shadowed labels produce unreliable OCR; keep the captured scene identity
        // rather than presenting those fragments as the owner's current topic.
        var context = desktop
            ? identity + "\n画面是壁纸与快捷方式图标，没有展开的应用正文。"
            : identity + "\n" + CompanionOcrLayout.Format(words, image.Width, image.Height);
        return new CompanionOcrSnapshot(context, language.LanguageTag, image.Width, image.Height,
            words.Length, captureMs, recognizeMs, words, IsDesktop: desktop);
    }
}
#endif
