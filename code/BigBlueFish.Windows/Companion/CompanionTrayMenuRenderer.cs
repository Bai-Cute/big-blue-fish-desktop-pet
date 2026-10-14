using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BigBlueFish.Windows;

// NotifyIcon keeps native positioning, keyboard navigation and outside-click dismissal.
internal sealed class CompanionTrayMenu : ContextMenuStrip
{
    private Font? menuFont;
    internal CompanionTrayMenu()
    {
        Renderer = new CompanionTrayMenuRenderer();
        ShowImageMargin = false;
        ShowCheckMargin = false;
        BackColor = Color.White;
        ForeColor = Color.FromArgb(49, 64, 94);
    }

    protected override void OnOpening(System.ComponentModel.CancelEventArgs e)
    {
        _ = Handle;
        ApplyMetrics();
        base.OnOpening(e);
    }

    protected override void OnOpened(EventArgs e)
    {
        ApplyMetrics();
        PerformLayout();
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width)),
            Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height)));
        base.OnOpened(e);
    }

    private void ApplyMetrics()
    {
        float scale = DeviceDpi / 96f;
        if (menuFont == null || Font.Unit != GraphicsUnit.Pixel || Math.Abs(Font.Size - 14 * scale) > .01f)
        {
            var previous = menuFont;
            menuFont = new Font("Microsoft YaHei UI", 14 * scale, GraphicsUnit.Pixel);
            Font = menuFont;
            previous?.Dispose();
        }
        Padding = new Padding((int)(7 * scale));
        foreach (ToolStripItem item in Items)
        {
            var text = TextRenderer.MeasureText(item.Text, Font);
            item.AutoSize = false;
            item.Margin = new Padding(0, (int)(2 * scale), 0, (int)(2 * scale));
            item.Size = new Size(Math.Max((int)(194 * scale), text.Width + (int)(66 * scale)),
                Math.Max((int)(38 * scale), text.Height + (int)(16 * scale)));
        }
        int width = 0, height = Padding.Vertical;
        foreach (ToolStripItem item in Items)
        {
            width = Math.Max(width, item.Width);
            height += item.Height + item.Margin.Vertical;
        }
        MinimumSize = new Size(width + Padding.Horizontal, height);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) { menuFont?.Dispose(); menuFont = null; }
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (Width <= 0 || Height <= 0) return;
        using var shape = CompanionTrayMenuRenderer.Outline(new RectangleF(0, 0, Width, Height), 12 * DeviceDpi / 96f);
        var previous = Region;
        Region = new Region(shape);
        previous?.Dispose();
    }
}

internal sealed class CompanionTrayMenuRenderer : ToolStripProfessionalRenderer
{
    internal CompanionTrayMenuRenderer() : base(new Palette()) => RoundedEdges = false;

    private sealed class Palette : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.White;
        public override Color MenuBorder => Color.FromArgb(220, 229, 242);
    }

    internal static GraphicsPath Outline(RectangleF rectangle, float radius)
    {
        var path = new GraphicsPath();
        float diameter = Math.Min(radius * 2, Math.Min(rectangle.Width, rectangle.Height));
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => e.Graphics.Clear(Color.White);

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        if (e.ToolStrip == null || e.ToolStrip.Width <= 1 || e.ToolStrip.Height <= 1) return;
        float scale = e.ToolStrip.DeviceDpi / 96f;
        using var path = Outline(new RectangleF(.5f, .5f, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), 12 * scale);
        using var pen = new Pen(Color.FromArgb(220, 229, 242), scale);
        var smoothing = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawPath(pen, path);
        e.Graphics.SmoothingMode = smoothing;
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected && !e.Item.Pressed) return;
        float scale = (e.ToolStrip?.DeviceDpi ?? 96) / 96f;
        using var outline = Outline(new RectangleF(0, 0, e.Item.Width - 1, e.Item.Height - 1), 7 * scale);
        using var brush = new SolidBrush(e.Item.Pressed ? Color.FromArgb(220, 234, 255) : Color.FromArgb(237, 243, 252));
        var smoothing = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.FillPath(brush, outline);
        e.Graphics.SmoothingMode = smoothing;
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        float scale = (e.ToolStrip?.DeviceDpi ?? 96) / 96f;
        var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        if (e.Item.Tag is string glyph)
        {
            using var iconFont = new Font("Segoe MDL2 Assets", 16 * scale, GraphicsUnit.Pixel);
            TextRenderer.DrawText(e.Graphics, glyph, iconFont,
                new Rectangle((int)(12 * scale), 0, (int)(24 * scale), e.Item.Height),
                Color.FromArgb(80, 96, 152), flags);
        }
        TextRenderer.DrawText(e.Graphics, e.Text, e.Item.Font,
            new Rectangle((int)(44 * scale), 0, e.Item.Width - (int)(56 * scale), e.Item.Height),
            e.Item.Enabled ? Color.FromArgb(49, 64, 94) : SystemColors.GrayText, flags);
    }
}
