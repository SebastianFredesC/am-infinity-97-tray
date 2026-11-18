using System.Runtime.InteropServices;

namespace AMInfinity97Tray;

internal sealed class TrayIcon(nint window) : IDisposable
{
    private const uint MessageFlag = 1;
    private const uint IconFlag = 2;
    private const uint TipFlag = 4;
    private const uint InfoFlag = 0x10;
    private const uint ShowTipFlag = 0x80;
    private readonly Icon?[] icons = new Icon[4];
    private int band;
    private string tip = "";
    private bool added;
    internal bool Version4 { get; private set; }
    internal bool IsAdded => added;

    internal void Set(int colorBand, string tooltip)
    {
        uint flags = 0;
        if (band != colorBand)
            flags |= IconFlag;
        if (tip != tooltip)
            flags |= TipFlag | ShowTipFlag;
        band = colorBand;
        tip = tooltip;
        if (!added)
        {
            Add();
            return;
        }
        if (flags != 0)
        {
            var data = Describe(flags);
            if (!Shell_NotifyIconW(1, ref data))
                Add();
        }
    }

    internal void Add()
    {
        var data = Describe(MessageFlag | IconFlag | TipFlag | ShowTipFlag);
        added = Shell_NotifyIconW(0, ref data);
        Version4 = false;
        if (added)
        {
            data.Version = 4;
            Version4 = Shell_NotifyIconW(4, ref data);
        }
    }

    internal Point Anchor()
    {
        var identifier = new IconIdentifier { Size = Marshal.SizeOf<IconIdentifier>(), Window = window, Id = 1 };
        return Shell_NotifyIconGetRect(in identifier, out NativeRectangle rect) == 0
            ? new Point((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2)
            : Cursor.Position;
    }

    internal void Notify(string title, string message, bool warning)
    {
        if (!added)
            return;
        var data = Describe(InfoFlag);
        data.Title = title;
        data.Info = message;
        data.InfoFlags = warning ? 2u : 1u;
        Shell_NotifyIconW(1, ref data);
    }

    internal void RestoreFocus()
    {
        if (!added)
            return;
        var data = Describe(0);
        Shell_NotifyIconW(3, ref data); // NIM_SETFOCUS
    }

    private IconData Describe(uint flags) => new()
    {
        Size = Marshal.SizeOf<IconData>(), Window = window, Id = 1,
        Flags = flags, Callback = DeviceWindow.TrayMessage,
        Icon = (flags & IconFlag) != 0 ? (icons[band] ??= MakeIcon(band)).Handle : 0,
        Tip = tip.Length <= 127 ? tip : tip[..127],
        Info = "", Title = ""
    };

    private static Icon MakeIcon(int colorBand)
    {
        string name = colorBand switch
        {
            1 => "BatteryCritical",
            2 => "BatteryLow",
            3 => "Battery",
            _ => "BatteryUnknown"
        };
        using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream($"AMInfinity97Tray.Assets.{name}.ico")
            ?? throw new InvalidOperationException($"No se encontró el icono {name}.");
        return new Icon(stream, SystemInformation.SmallIconSize);
    }

    public void Dispose()
    {
        if (added)
        {
            var data = Describe(0);
            Shell_NotifyIconW(2, ref data);
            added = false;
        }
        foreach (var icon in icons)
            icon?.Dispose();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct IconData
    {
        internal int Size;
        internal nint Window;
        internal uint Id;
        internal uint Flags;
        internal uint Callback;
        internal nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Tip;
        internal uint State;
        internal uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string Info;
        internal uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string Title;
        internal uint InfoFlags;
        internal Guid Guid;
        internal nint BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconIdentifier
    {
        internal int Size;
        internal nint Window;
        internal uint Id;
        internal Guid Guid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        internal int Left, Top, Right, Bottom;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(uint message, ref IconData data);
    [DllImport("shell32.dll")]
    private static extern int Shell_NotifyIconGetRect(in IconIdentifier identifier, out NativeRectangle rectangle);
}
