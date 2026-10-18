using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace BigBlueFish.Windows;

internal sealed class LinuxLoadingRing : Avalonia.Controls.Control
{
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    static readonly Color Blue = Color.FromRgb(80, 96, 152);
    internal LinuxLoadingRing() { Width = Height = 26; timer.Tick += (_, _) => { if (IsVisible) InvalidateVisual(); }; AttachedToVisualTree += (_, _) => timer.Start(); DetachedFromVisualTree += (_, _) => timer.Stop(); }
    public override void Render(DrawingContext context)
    {
        base.Render(context); double size = Math.Min(Bounds.Width, Bounds.Height), radius = size * .39, stroke = size * .095;
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2); double angle = clock.Elapsed.TotalSeconds * 360 / 1.15, sweep = 70 + 205 * (.5 - .5 * Math.Cos(clock.Elapsed.TotalSeconds * Math.PI / .85));
        context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(25, Blue.R, Blue.G, Blue.B)), stroke), center, radius, radius);
        Point At(double a) => new(center.X + radius * Math.Cos(a * Math.PI / 180), center.Y + radius * Math.Sin(a * Math.PI / 180));
        for (int i = 0; i < 64; i++) {
            double fraction = (i + 1d) / 64; var geometry = new StreamGeometry();
            using (var path = geometry.Open()) { path.BeginFigure(At(angle - sweep + sweep * i / 64), false); path.ArcTo(At(angle - sweep + sweep * (i + 1d) / 64 + .3), new Size(radius, radius), 0, false, SweepDirection.Clockwise); path.EndFigure(false); }
            context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)(35 + 220 * fraction), Blue.R, Blue.G, Blue.B)), stroke), geometry);
        }
        context.DrawEllipse(new SolidColorBrush(Blue), null, At(angle), stroke / 2, stroke / 2);
    }
}
internal sealed class LinuxBubble : Decorator
{
    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width - 1.5, h = Bounds.Height - 1.5, s = Padding.Left / 16, r = 24 * s, tail = 12 * s, body = h - tail, x = w * .65;
        if (w > r * 2 && body > r * 2) {
            var path = Geometry.Parse(FormattableString.Invariant($"M {r},0.75 H {w-r} Q {w},0.75 {w},{r} V {body-r} Q {w},{body} {w-r},{body} H {x+tail} L {x},{h} L {x-tail},{body} H {r} Q 0.75,{body} 0.75,{body-r} V {r} Q 0.75,0.75 {r},0.75 Z"));
            context.DrawGeometry(new SolidColorBrush(Color.FromRgb(255,251,244)), new Pen(new SolidColorBrush(Color.FromRgb(80,96,152)), 1.5 * s), path);
        }
        base.Render(context);
    }
}

