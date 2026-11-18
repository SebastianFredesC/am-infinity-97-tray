using System.Drawing.Drawing2D;

namespace AMInfinity97Tray;

internal sealed class TrayMenuRenderer(TrayPalette palette)
    : ToolStripProfessionalRenderer(new MenuColors(palette))
{
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? palette.Text : palette.Secondary;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = palette.Secondary;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        Rectangle bounds = e.ImageRectangle;
        int x = bounds.Left + bounds.Width / 2;
        int y = bounds.Top + bounds.Height / 2;
        float scale = Math.Max(1, bounds.Height / 16f);
        var saved = e.Graphics.Save();
        try
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(palette.Accent, 1.8f * scale)
            {
                StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round
            };
            e.Graphics.DrawLines(pen,
            [
                new PointF(x - 4 * scale, y),
                new PointF(x - scale, y + 3 * scale),
                new PointF(x + 5 * scale, y - 3 * scale)
            ]);
        }
        finally
        {
            e.Graphics.Restore(saved);
        }
    }

    private sealed class MenuColors(TrayPalette palette) : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => palette.Surface;
        public override Color MenuBorder => palette.Border;
        public override Color MenuItemBorder => palette.Selection;
        public override Color MenuItemSelected => palette.Selection;
        public override Color ImageMarginGradientBegin => palette.Surface;
        public override Color ImageMarginGradientMiddle => palette.Surface;
        public override Color ImageMarginGradientEnd => palette.Surface;
        public override Color SeparatorDark => palette.Border;
        public override Color SeparatorLight => palette.Border;
    }
}
