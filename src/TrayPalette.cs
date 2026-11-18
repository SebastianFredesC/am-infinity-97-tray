using System.Drawing.Drawing2D;

namespace AMInfinity97Tray;

internal sealed class TrayPalette(bool highContrast)
{
    internal Color Background { get; } = highContrast ? SystemColors.Window : Color.FromArgb(20, 24, 32);
    internal Color Surface { get; } = highContrast ? SystemColors.Window : Color.FromArgb(30, 36, 47);
    internal Color Border { get; } = highContrast ? SystemColors.WindowText : Color.FromArgb(54, 65, 82);
    internal Color Track { get; } = highContrast ? SystemColors.GrayText : Color.FromArgb(56, 67, 84);
    internal Color Text { get; } = highContrast ? SystemColors.WindowText : Color.FromArgb(243, 246, 251);
    internal Color Secondary { get; } = highContrast ? SystemColors.WindowText : Color.FromArgb(178, 193, 212);
    internal Color Selection { get; } = highContrast ? SystemColors.Window : Color.FromArgb(43, 56, 74);
    internal Color Accent { get; } = highContrast ? SystemColors.WindowText : Color.FromArgb(125, 213, 252);
    internal Color Positive { get; } = highContrast ? SystemColors.WindowText : Color.FromArgb(115, 222, 172);
    internal Color Warning { get; } = highContrast ? SystemColors.WindowText : Color.FromArgb(255, 209, 133);
    internal Color Critical { get; } = highContrast ? SystemColors.WindowText : Color.FromArgb(255, 138, 148);

    internal Color Level(int? percent) => MonitorPolicy.IconBand(percent) switch
    {
        1 => Critical,
        2 => Warning,
        3 => Positive,
        _ => Secondary
    };

    internal static void FillRounded(Graphics graphics, Brush brush, RectangleF rect, float radius)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        float diameter = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        using var path = new GraphicsPath();
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        graphics.FillPath(brush, path);
    }
}
