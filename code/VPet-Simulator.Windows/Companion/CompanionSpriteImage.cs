using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VPet_Simulator.Windows;
internal sealed class CompanionSpriteImage : Image
{
    protected override HitTestResult? HitTestCore(PointHitTestParameters parameters)
    {
        if (Source is not BitmapSource bitmap || ActualWidth <= 0 || ActualHeight <= 0) return null;
        int x = (int)(parameters.HitPoint.X * bitmap.PixelWidth / ActualWidth);
        int y = (int)(parameters.HitPoint.Y * bitmap.PixelHeight / ActualHeight);
        if (x < 0 || y < 0 || x >= bitmap.PixelWidth || y >= bitmap.PixelHeight) return null;
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return pixel[3] >= 16 ? new PointHitTestResult(this, parameters.HitPoint) : null;
    }
}
