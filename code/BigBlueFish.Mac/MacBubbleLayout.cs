using Avalonia;
using Avalonia.Controls;

namespace BigBlueFish.Windows;

internal static class MacBubbleLayout
{
    internal static Size Measure(TextBlock text, string message, double maximumWidth)
    {
        text.Text = message;
        text.InvalidateMeasure();
        text.Measure(new Size(maximumWidth, double.PositiveInfinity));
        return text.DesiredSize;
    }
}
