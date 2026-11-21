using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BigBlueFish.Windows;
internal sealed class CompanionSpriteImage : Image
{
    internal const byte AlphaThreshold = 8;
    private static readonly Cursor palm = LoadCursor("Palm");
    private static readonly Cursor palmGrab = LoadCursor("PalmGrab");
    private byte[]? framePixels;
    private int frameWidth, frameHeight;

    internal CompanionSpriteImage() => Cursor = palm;

    internal static Cursor PalmGrab => palmGrab;

    /// <summary>Open palm while hovering, closed palm while the button is down.</summary>
    internal void SetPressed(bool pressed)
    {
        var next = pressed ? palmGrab : palm;
        if (!ReferenceEquals(Cursor, next)) Cursor = next;
        // WPF only re-evaluates the pointer shape on the next mouse move; without this the
        // grip would not show until the user moved while holding the button.
        Mouse.UpdateCursor();
    }

    // The cursors ship as resources, so a missing one is a packaging bug rather than a user
    // condition; --cursor verification fails if this falls back. Falling back to the system
    // hand keeps the pet usable if a trimmed build drops the resource.
    private static Cursor LoadCursor(string name)
    {
        try
        {
            var part = Application.GetResourceStream(new Uri($"/BigBlueFish;component/Res/Cursor/{name}.cur", UriKind.Relative));
            if (part != null) using (var stream = part.Stream) return new Cursor(stream);
        }
        catch (Exception)
        {
        }
        return Cursors.Hand;
    }

    internal void UpdatePixels(byte[]? pixels, int width, int height)
    {
        frameWidth = width;
        frameHeight = height;
        framePixels = pixels;
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters parameters)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return null;
        var pixels = framePixels;
        int w = frameWidth, h = frameHeight;
        if (pixels != null && w > 0 && h > 0 && pixels.Length >= w * h * 4)
        {
            int px = (int)(parameters.HitPoint.X * w / ActualWidth);
            int py = (int)(parameters.HitPoint.Y * h / ActualHeight);
            return px >= 0 && py >= 0 && px < w && py < h && pixels[(py * w + px) * 4 + 3] >= AlphaThreshold
                ? new PointHitTestResult(this, parameters.HitPoint) : null;
        }
        if (Source is not BitmapSource bitmap) return null;
        int x = (int)(parameters.HitPoint.X * bitmap.PixelWidth / ActualWidth);
        int y = (int)(parameters.HitPoint.Y * bitmap.PixelHeight / ActualHeight);
        if (x < 0 || y < 0 || x >= bitmap.PixelWidth || y >= bitmap.PixelHeight) return null;
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return pixel[3] >= AlphaThreshold ? new PointHitTestResult(this, parameters.HitPoint) : null;
    }
}
