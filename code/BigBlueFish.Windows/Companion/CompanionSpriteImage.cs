using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BigBlueFish.Windows;
internal sealed class CompanionSpriteImage : Image
{
    internal const byte AlphaThreshold = 8;
    private byte[]? framePixels;
    private int frameWidth, frameHeight;

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
