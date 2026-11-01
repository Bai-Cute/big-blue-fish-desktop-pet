namespace BigBlueFish.Windows;

internal static class MacHitTest
{
    const int Width = 640, Height = 360;

    internal static bool CopyAlpha(byte[] rgba, byte[] alpha)
    {
        if (alpha.Length != Width * Height) return false;
        if (rgba.Length != Width * Height * 4)
        {
            Array.Clear(alpha, 0, alpha.Length);
            return false;
        }
        for (int source = 3, target = 0; target < alpha.Length; source += 4, target++)
            alpha[target] = rgba[source];
        return true;
    }

    internal static bool Contains(byte[] alpha, double x, double y, double width, double height, bool mirror)
    {
        if (alpha.Length != Width * Height || !double.IsFinite(x) || !double.IsFinite(y) ||
            !double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0 ||
            x < 0 || y < 0 || x >= width || y >= height) return false;
        int column = (int)(x / width * Width), row = (int)(y / height * Height);
        if (mirror) column = Width - 1 - column;
        return alpha[row * Width + column] >= 8;
    }
}
