using System.ComponentModel;

namespace AMInfinity97Tray;

internal sealed class TrayMenu : ContextMenuStrip
{
    private const int AppWindowStyle = 0x00040000;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Rectangle WorkArea { get; set; }

    internal TrayMenu()
    {
        ShowItemToolTips = false;
    }

    internal void ShowAt(Point anchor)
    {
        Size size = GetPreferredSize(Size.Empty);
        int y = anchor.Y >= WorkArea.Top + WorkArea.Height / 2 ? anchor.Y - size.Height : anchor.Y;
        Show(KeepInside(new Point(anchor.X - size.Width, y), size, WorkArea));
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle = (parameters.ExStyle & ~AppWindowStyle) | DeviceWindow.ToolWindowStyle;
            return parameters;
        }
    }

    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
    {
        // Submenus are positioned by their owning item. WinForms re-reads that
        // location in its own SetBoundsCore, even when an x/y is supplied here.
        if (OwnerItem is null && !WorkArea.IsEmpty)
        {
            Point position = KeepInside(new Point(x, y), new Size(width, height), WorkArea);
            x = position.X;
            y = position.Y;
        }
        base.SetBoundsCore(x, y, width, height, specified);
    }

    internal static Point KeepInside(Point point, Size size, Rectangle area) => new(
        Math.Clamp(point.X, area.Left, Math.Max(area.Left, area.Right - size.Width)),
        Math.Clamp(point.Y, area.Top, Math.Max(area.Top, area.Bottom - size.Height)));
}

internal sealed class TraySubmenuItem(string text, TrayMenu submenu) : ToolStripMenuItem(text)
{
    protected override Point DropDownLocation
    {
        get
        {
            ToolStrip? owner = GetCurrentParent();
            Rectangle area = submenu.WorkArea;
            if (owner is null || area.IsEmpty)
                return base.DropDownLocation;

            return Place(owner.Bounds, owner.RectangleToScreen(Bounds).Top, submenu.GetPreferredSize(Size.Empty), area);
        }
    }

    internal static Point Place(Rectangle root, int itemY, Size size, Rectangle area)
    {
        int right = area.Right - root.Right + 1;
        int left = root.Left - area.Left + 1;
        bool openRight = size.Width <= right || (size.Width > left && right >= left);
        int x = openRight ? root.Right - 1 : root.Left - size.Width + 1;
        return TrayMenu.KeepInside(new Point(x, itemY), size, area);
    }
}
