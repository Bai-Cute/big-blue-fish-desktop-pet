using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BigBlueFish.Windows;
internal sealed class CompanionSpriteImage : Image
{
    internal byte[]? InputPixels;
    internal int InputWidth,InputHeight;
    internal bool IsNativeInput,NativeLeftPressed;
    internal event System.Action? NativeCaptureLost;
    internal void CancelNativeCapture()=>NativeCaptureLost?.Invoke();
    protected override HitTestResult? HitTestCore(PointHitTestParameters parameters)
    {
        if(ActualWidth<=0||ActualHeight<=0)return null;
        if(InputPixels!=null)
        {
            int mx=(int)(parameters.HitPoint.X*InputWidth/ActualWidth),my=(int)(parameters.HitPoint.Y*InputHeight/ActualHeight);
            return mx>=0&&my>=0&&mx<InputWidth&&my<InputHeight&&InputPixels[(my*InputWidth+mx)*4+3]>=16?new PointHitTestResult(this,parameters.HitPoint):null;
        }
        if (Source is not BitmapSource bitmap) return null;
        int x = (int)(parameters.HitPoint.X * bitmap.PixelWidth / ActualWidth);
        int y = (int)(parameters.HitPoint.Y * bitmap.PixelHeight / ActualHeight);
        if (x < 0 || y < 0 || x >= bitmap.PixelWidth || y >= bitmap.PixelHeight) return null;
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return pixel[3] >= 16 ? new PointHitTestResult(this, parameters.HitPoint) : null;
    }
}
