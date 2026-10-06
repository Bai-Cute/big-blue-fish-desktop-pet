using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VPet_Simulator.Windows;

// One continuous outline joins the rounded body and its small pointer.
internal sealed class CompanionBubble : Border
{
    internal static readonly Color HairBlue = Color.FromRgb(80, 96, 152);
    internal const double PointerHeight = 9;
    private bool pointerOnTop;
    private double pointerX = double.NaN;

    internal CompanionBubble()
    {
        Background = new SolidColorBrush(Color.FromRgb(255, 251, 244));
        BorderBrush = new SolidColorBrush(HairBlue);
        BorderThickness = new Thickness(1.5);
        CornerRadius = new CornerRadius(24);
        UpdatePadding();
    }

    internal void PointAt(double x, bool onTop)
    {
        if (pointerOnTop != onTop)
        {
            pointerOnTop = onTop;
            UpdatePadding();
        }
        if (pointerX != x)
        {
            pointerX = x;
            InvalidateVisual();
        }
    }

    private void UpdatePadding()
    {
        Padding = new Thickness(18, 14 + (pointerOnTop ? PointerHeight : 0),
            14, 14 + (pointerOnTop ? 0 : PointerHeight));
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawing)
    {
        double inset = BorderThickness.Left / 2;
        double width = ActualWidth - inset * 2;
        double height = ActualHeight - PointerHeight - inset * 2;
        if (width <= 0 || height <= 0) return;
        double top = inset + (pointerOnTop ? PointerHeight : 0);
        double radius = Math.Min(CornerRadius.TopLeft, Math.Min(width, height) / 2);
        var body = new RectangleGeometry(new Rect(inset, top, width, height), radius, radius);
        double minimum = inset + radius + 8;
        double maximum = ActualWidth - minimum;
        double x = Math.Clamp(double.IsNaN(pointerX) ? ActualWidth / 2 : pointerX,
            Math.Min(minimum, ActualWidth / 2), Math.Max(maximum, ActualWidth / 2));
        double seam = pointerOnTop ? top + 1 : top + height - 1;
        double tip = pointerOnTop ? inset : ActualHeight - inset;
        var pointer = new StreamGeometry();
        using (var context = pointer.Open())
        {
            context.BeginFigure(new Point(x - 8, seam), true, true);
            context.LineTo(new Point(x, tip), true, false);
            context.LineTo(new Point(x + 8, seam), true, false);
        }
        var outline = new CombinedGeometry(GeometryCombineMode.Union, body, pointer);
        drawing.DrawGeometry(Background, new Pen(BorderBrush, BorderThickness.Left)
        { LineJoin = PenLineJoin.Round }, outline);
    }
}
