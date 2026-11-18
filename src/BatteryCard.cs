using System.Drawing.Drawing2D;

namespace AMInfinity97Tray;

internal sealed class BatteryCard : TableLayoutPanel
{
    private readonly Label heading = new() { AutoSize = true, Margin = Padding.Empty };
    private readonly Label value = new() { AutoSize = true, Margin = Padding.Empty };
    private readonly Label detail = new() { AutoSize = true, Margin = new Padding(0, 7, 0, 0) };
    private readonly Label time = new() { AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
    private readonly LevelMeter meter;
    private readonly bool spare;
    private readonly TrayPalette palette;

    internal BatteryCard(bool spare, Font valueFont, TrayPalette palette)
    {
        this.spare = spare;
        this.palette = palette;
        meter = new LevelMeter(palette);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Dock = DockStyle.Fill;
        ColumnCount = 1;
        RowCount = 5;
        for (int row = 0; row < RowCount; row++)
            RowStyles.Add(new RowStyle(SizeType.AutoSize));
        RowStyles[3] = new RowStyle(SizeType.Absolute, 23);
        Margin = Padding.Empty;
        Padding = new Padding(12, 9, 12, 10);
        BackColor = palette.Surface;
        ForeColor = palette.Text;

        heading.ForeColor = palette.Secondary;
        Controls.Add(heading, 0, 0);
        value.Font = valueFont;
        Controls.Add(value, 0, 1);
        Controls.Add(meter, 0, 2);
        Controls.Add(detail, 0, 3);
        Controls.Add(time, 0, 4);
    }

    internal void Render(BatteryDisplay battery, UiText text)
    {
        int? percent = battery.Sample?.Percent;
        bool charging = spare && battery.Sample?.Charging == true && percent.HasValue;
        Color fill = charging ? palette.Accent : palette.Level(percent);
        heading.Text = text.BatteryName(spare);
        value.Text = percent is int number ? $"{number}%" : "—";
        value.ForeColor = battery.Stale || percent is null ? palette.Secondary : palette.Text;
        meter.SetLevel(percent, battery.Stale ? palette.Secondary : fill);
        detail.ForeColor = charging && !battery.Stale ? palette.Accent : palette.Secondary;
        detail.Text = text.BatteryDetail(battery, spare);
        detail.Visible = detail.Text.Length != 0;
        time.Text = text.Time(battery);
        time.ForeColor = battery.Stale ? palette.Warning : palette.Secondary;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(palette.Background);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var size = ClientSize;
        using var outline = new SolidBrush(palette.Border);
        using var surface = new SolidBrush(palette.Surface);
        TrayPalette.FillRounded(e.Graphics, outline, new RectangleF(0, 0, size.Width, size.Height), 10);
        TrayPalette.FillRounded(e.Graphics, surface, new RectangleF(1, 1, size.Width - 2, size.Height - 2), 9);
    }

    private sealed class LevelMeter : Control
    {
        private readonly TrayPalette palette;
        private int? percentage;
        private Color fill;

        internal LevelMeter(TrayPalette palette)
        {
            this.palette = palette;
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            DoubleBuffered = true;
            Anchor = AnchorStyles.Left | AnchorStyles.Right;
            Height = 6;
            Margin = new Padding(0, 3, 0, 2);
            TabStop = false;
        }

        internal void SetLevel(int? level, Color color)
        {
            if (level == percentage && color == fill)
                return;
            percentage = level;
            fill = color;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var track = new SolidBrush(palette.Track);
            TrayPalette.FillRounded(e.Graphics, track, ClientRectangle, Height / 2f);
            if (percentage is > 0)
            {
                using var brush = new SolidBrush(fill);
                float width = Math.Max(2, Width * (percentage.Value / 100f));
                TrayPalette.FillRounded(e.Graphics, brush, new RectangleF(0, 0, width, Height), Height / 2f);
            }
        }
    }
}
