using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VPet_Simulator.Windows;

// Keep NotifyIcon's native menu input/dismissal behavior and style its painting.
internal sealed class CompanionTrayMenuRenderer : ToolStripProfessionalRenderer
{
    internal CompanionTrayMenuRenderer() : base(new Palette()) => RoundedEdges = false;

    private sealed class Palette : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.White;
        public override Color MenuBorder => Color.FromArgb(220, 229, 242);
        public override Color ImageMarginGradientBegin => Color.White;
        public override Color ImageMarginGradientMiddle => Color.White;
        public override Color ImageMarginGradientEnd => Color.White;
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected && !e.Item.Pressed) return;
        float radius = 7 * (e.ToolStrip?.DeviceDpi ?? 96) / 96f;
        var rectangle = new RectangleF(2, 1, e.Item.Width - 4, e.Item.Height - 2);
        using var outline = new GraphicsPath();
        float diameter = radius * 2;
        outline.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        outline.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        outline.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        outline.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        outline.CloseFigure();
        using var brush = new SolidBrush(e.Item.Pressed ? Color.FromArgb(220, 234, 255) : Color.FromArgb(237, 243, 252));
        var smoothing = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.FillPath(brush, outline);
        e.Graphics.SmoothingMode = smoothing;
    }
}
