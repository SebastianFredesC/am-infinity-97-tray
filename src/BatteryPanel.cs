using System.Drawing.Drawing2D;

namespace AMInfinity97Tray;

internal sealed class BatteryPanel : Form
{
    private readonly TrayPalette palette = new(SystemInformation.HighContrast);
    private readonly Label state = new() { AutoSize = true, UseMnemonic = false };
    private readonly Label stateDot = new() { AutoSize = true, Text = "●" };
    private readonly Label pending = new() { AutoSize = true, UseMnemonic = false, Visible = false };
    private readonly CloseButton close;
    private readonly RefreshButton refresh;
    private readonly BatteryCard mouse;
    private readonly BatteryCard spare;
    private readonly Font titleFont;
    private readonly Font valueFont;
    private Point anchor;

    internal event Action? RefreshRequested;

    internal BatteryPanel()
    {
        Text = UiText.Title;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        TopMost = true;
        Padding = new Padding(1);
        BackColor = palette.Border;
        ForeColor = palette.Text;
        titleFont = new Font(Font.FontFamily, 14, FontStyle.Bold);
        valueFont = new Font(Font.FontFamily, 25, FontStyle.Bold);
        close = new CloseButton(palette);
        refresh = new RefreshButton(palette);
        mouse = new BatteryCard(false, valueFont, palette);
        spare = new BatteryCard(true, valueFont, palette);

        var layout = new TableLayoutPanel
        {
            ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(15, 12, 15, 14), Dock = DockStyle.Fill,
            BackColor = palette.Background
        };

        layout.Controls.Add(BuildHeader(), 0, 0);
        layout.Controls.Add(BuildStatus(), 0, 1);
        var cards = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Margin = Padding.Empty };
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 169));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 10));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 169));
        cards.Controls.Add(mouse, 0, 0);
        cards.Controls.Add(spare, 2, 0);
        layout.Controls.Add(cards, 0, 2);

        layout.Controls.Add(BuildActions(), 0, 3);
        Controls.Add(layout);
        Deactivate += (_, _) => Hide();
    }

    private TableLayoutPanel BuildHeader()
    {
        var header = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Top, Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28));
        header.Controls.Add(new Label
        {
            Text = UiText.Title, Font = titleFont, AutoSize = true, UseMnemonic = false,
            Margin = Padding.Empty, Anchor = AnchorStyles.Left
        }, 0, 0);
        close.Click += (_, _) => Hide();
        header.Controls.Add(close, 1, 0);
        return header;
    }

    private sealed class CloseButton : Button
    {
        internal CloseButton(TrayPalette palette)
        {
            Anchor = AnchorStyles.None;
            Size = new Size(28, 26);
            Margin = Padding.Empty;
            FlatStyle = FlatStyle.Flat;
            BackColor = palette.Background;
            ForeColor = palette.Secondary;
            UseVisualStyleBackColor = false;
            FlatAppearance.BorderSize = 0;
            FlatAppearance.MouseOverBackColor = BackColor;
            FlatAppearance.MouseDownBackColor = BackColor;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            float x = (ClientSize.Width - 1) / 2f;
            float y = (ClientSize.Height - 1) / 2f;
            float scale = DeviceDpi / 96f;
            float arm = 3.5f * scale;
            using var pen = new Pen(ForeColor, 1.6f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.DrawLine(pen, x - arm, y - arm, x + arm, y + arm);
            e.Graphics.DrawLine(pen, x + arm, y - arm, x - arm, y + arm);
        }
    }

    private TableLayoutPanel BuildStatus()
    {
        var status = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 5, 0, 12) };
        status.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 16));
        state.ForeColor = palette.Secondary;
        state.MaximumSize = new Size(330, 0);
        state.Margin = Padding.Empty;
        stateDot.Margin = Padding.Empty;
        status.Controls.Add(stateDot, 0, 0);
        status.Controls.Add(state, 1, 0);
        return status;
    }

    private TableLayoutPanel BuildActions()
    {
        var actions = new TableLayoutPanel
        {
            ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 10, 0, 0),
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pending.ForeColor = palette.Secondary;
        pending.MaximumSize = new Size(214, 0);
        pending.Margin = new Padding(0, 0, 8, 0);
        pending.Anchor = AnchorStyles.Left;
        actions.Controls.Add(pending, 0, 0);

        refresh.Click += (_, _) => RefreshRequested?.Invoke();
        actions.Controls.Add(refresh, 1, 0);
        return actions;
    }

    private sealed class RefreshButton : Button
    {
        private readonly Color disabledText;

        internal RefreshButton(TrayPalette palette)
        {
            AutoSize = true;
            Margin = Padding.Empty;
            Padding = new Padding(7, 3, 7, 3);
            FlatStyle = FlatStyle.Flat;
            UseVisualStyleBackColor = false;
            BackColor = palette.Surface;
            ForeColor = palette.Text;
            FlatAppearance.BorderColor = palette.Border;
            FlatAppearance.MouseOverBackColor = palette.Selection;
            disabledText = palette.Secondary;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Enabled)
            {
                base.OnPaint(e);
                return;
            }

            e.Graphics.Clear(BackColor);
            using var border = new Pen(FlatAppearance.BorderColor);
            e.Graphics.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, disabledText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    internal void Render(MonitorDisplay display, UiText text)
    {
        SuspendLayout();
        state.Text = text.Link(display.Link);
        close.AccessibleName = text["ClosePanel"];
        refresh.Text = text["RefreshNow"];
        stateDot.ForeColor = display.Link switch
        {
            LinkState.Active => palette.Positive,
            LinkState.Reading => palette.Accent,
            LinkState.Busy or LinkState.Error or LinkState.NoReply => palette.Warning,
            _ => palette.Secondary
        };
        mouse.Render(display.Mouse, text);
        spare.Render(display.Spare, text);
        refresh.Enabled = !display.ManualPending && display.Link != LinkState.Reading;
        pending.Visible = display.ManualPending;
        if (display.ManualPending)
            pending.Text = text.ManualPending(display);
        ResumeLayout(true);
        if (Visible)
            PlaceAtAnchor();
    }

    internal void Present(Point anchor)
    {
        this.anchor = anchor;
        if (!IsHandleCreated)
        {
            Location = anchor;
            _ = Handle;
        }
        PlaceAtAnchor();
        Show();
        Activate();
        PlaceAtAnchor(); // The initial show can change the monitor DPI or preferred size.
    }

    private void PlaceAtAnchor()
    {
        PerformLayout();
        Rectangle space = Screen.FromPoint(anchor).WorkingArea;
        int x = Math.Clamp(anchor.X - Width / 2, space.Left, Math.Max(space.Left, space.Right - Width));
        int y = Math.Clamp(anchor.Y - (anchor.Y >= space.Top + space.Height / 2 ? Height + 8 : -8),
            space.Top, Math.Max(space.Top, space.Bottom - Height));
        Location = new Point(x, y);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ClassStyle |= 0x00020000; // CS_DROPSHADOW when enabled in Windows.
            return parameters;
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData != Keys.Escape)
            return base.ProcessCmdKey(ref msg, keyData);
        Hide();
        return true;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            titleFont.Dispose();
            valueFont.Dispose();
        }
    }
}
