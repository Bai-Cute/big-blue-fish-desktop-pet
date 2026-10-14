using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BigBlueFish.Windows;

// Drawn at the current DPI; WPF's animation clock replaces stepped timer rotation.
internal sealed class CompanionLoadingRing : FrameworkElement
{
    internal static readonly DependencyProperty AngleProperty = DependencyProperty.Register(
        nameof(Angle), typeof(double), typeof(CompanionLoadingRing),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    internal static readonly DependencyProperty SweepProperty = DependencyProperty.Register(
        nameof(Sweep), typeof(double), typeof(CompanionLoadingRing),
        new FrameworkPropertyMetadata(80d, FrameworkPropertyMetadataOptions.AffectsRender));

    internal double Angle { get => (double)GetValue(AngleProperty); set => SetValue(AngleProperty, value); }
    internal double Sweep { get => (double)GetValue(SweepProperty); set => SetValue(SweepProperty, value); }
    internal bool Running { get; private set; }

    internal void Start()
    {
        if (Running) return;
        Running = true;
        BeginAnimation(AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.15))
        { RepeatBehavior = RepeatBehavior.Forever });
        BeginAnimation(SweepProperty, new DoubleAnimation(70, 275, TimeSpan.FromSeconds(.85))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        });
    }

    internal void Stop()
    {
        BeginAnimation(AngleProperty, null);
        BeginAnimation(SweepProperty, null);
        Running = false;
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        double radius = size * .39, stroke = size * .095;
        var blue = CompanionBubble.HairBlue;
        context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(25, blue.R, blue.G, blue.B)), stroke), center, radius, radius);
        const int segments = 64;
        var sweep = Math.Clamp(Sweep, 1, 330);
        for (int i = 0; i < segments; i++)
        {
            double fraction = (i + 1d) / segments;
            double start = Angle - sweep + sweep * i / segments;
            double end = Angle - sweep + sweep * (i + 1d) / segments + .3;
            var brush = new SolidColorBrush(Color.FromArgb((byte)(35 + 220 * fraction), blue.R, blue.G, blue.B));
            var path = new StreamGeometry();
            using (var geometry = path.Open())
            {
                geometry.BeginFigure(At(start), false, false);
                geometry.ArcTo(At(end), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
            }
            path.Freeze();
            context.DrawGeometry(null, new Pen(brush, stroke), path);
        }
        context.DrawEllipse(new SolidColorBrush(blue), null, At(Angle), stroke / 2, stroke / 2);

        Point At(double degrees)
        {
            double radians = degrees * Math.PI / 180;
            return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
        }
    }
}
