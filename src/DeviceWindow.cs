using System.Runtime.InteropServices;

namespace AMInfinity97Tray;

internal sealed class DeviceWindow : NativeWindow, IDisposable
{
    internal const int TrayMessage = 0x8001;
    internal const int ToolWindowStyle = 0x00000080;
    private readonly uint taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
    private readonly nint deviceNotification;
    private bool armed;
    private nint lastRawDevice;
    private bool lastRawDeviceMatches;

    internal event Action<long?>? Activity;
    internal event Action? ReceiverChanged;
    internal event Action<bool>? PowerChanged;
    internal event Action? ShellRestarted;
    internal event Action<int, Point>? TrayEvent;
    internal bool HasDeviceNotifications => deviceNotification != 0;

    internal DeviceWindow()
    {
        CreateHandle(new CreateParams
        {
            Caption = "AM Infinity .97 Tray", Style = unchecked((int)0x80000000), ExStyle = ToolWindowStyle
        });
        var filter = new BroadcastInterface
        {
            Size = Marshal.SizeOf<BroadcastInterface>(),
            Type = 5, // DBT_DEVTYP_DEVICEINTERFACE
            Class = HidBatteryService.HidClass
        };
        deviceNotification = RegisterDeviceNotificationW(Handle, ref filter, 0);
    }

    internal bool Arm(bool value)
    {
        if (armed == value)
            return true;
        var registration = new RawRegistration
        {
            UsagePage = 1,
            Usage = 2, // Generic Desktop / Mouse. No keyboard or legacy suppression.
            Flags = value ? 0x100u : 1u, // RIDEV_INPUTSINK / RIDEV_REMOVE
            Window = value ? Handle : 0
        };
        if (!RegisterRawInputDevices(in registration, 1, (uint)Marshal.SizeOf<RawRegistration>()))
            return false;
        armed = value;
        return true;
    }

    internal void ForgetRawDevice()
    {
        lastRawDevice = 0;
        lastRawDeviceMatches = false;
    }

    internal void PrepareTrayMenu(Point anchor)
    {
        const uint moveOnly = 0x0001 | 0x0004 | 0x0010; // No resize, stacking or activation during the move.
        SetWindowPos(Handle, 0, anchor.X, anchor.Y, 0, 0, moveOnly);
        SetForegroundWindow(Handle);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == TrayMessage)
        {
            long point = m.WParam.ToInt64();
            TrayEvent?.Invoke((int)(m.LParam.ToInt64() & 0xFFFF),
                new Point(unchecked((short)point), unchecked((short)(point >> 16))));
        }
        else if (m.Msg == taskbarCreated && taskbarCreated != 0)
        {
            ShellRestarted?.Invoke();
        }
        else if (m.Msg == 0x00FF && armed) // WM_INPUT
        {
            ObserveInput(m.LParam);
        }
        else if (m.Msg == 0x0219 && (m.WParam == 0x8000 || m.WParam == 0x8004) && m.LParam != 0
            && Marshal.ReadInt32(m.LParam, 4) == 5)
        {
            string? path = Marshal.PtrToStringUni(m.LParam + 28);
            if (HidBatteryService.IsReceiverPath(path))
                ReceiverChanged?.Invoke();
        }
        else if (m.Msg == 0x0218) // WM_POWERBROADCAST
        {
            if (m.WParam == 4)
                PowerChanged?.Invoke(true);
            else if (m.WParam == 0x12 || m.WParam == 7)
                PowerChanged?.Invoke(false);
        }
        base.WndProc(ref m);
    }

    private unsafe void ObserveInput(nint rawInput)
    {
        uint size = (uint)sizeof(RawMouseInput);
        uint age = unchecked((uint)(Environment.TickCount - GetMessageTime()));
        uint received = GetRawInputData(rawInput, 0x10000003, out var input, ref size, 24);
        // Disarm on every sample, even when it belongs to another mouse. A short
        // cooldown for those samples keeps an unrelated 8 kHz stream inexpensive.
        if (!Arm(false))
            return;

        if (received == (uint)sizeof(RawMouseInput) && input.Type == 0 && input.Device != 0 && age < 1000)
        {
            if (lastRawDevice != input.Device)
            {
                lastRawDevice = input.Device;
                uint length = 512;
                char* name = stackalloc char[(int)length];
                uint count = GetRawInputDeviceInfoW(input.Device, 0x20000007, name, ref length);
                lastRawDeviceMatches = count is > 0 and <= 512 &&
                    HidBatteryService.IsReceiverPath(new string(name, 0, (int)count).TrimEnd('\0'));
            }
            if (lastRawDeviceMatches && (input.X != 0 || input.Y != 0 || input.ButtonFlags != 0))
            {
                Activity?.Invoke(Environment.TickCount64 - age);
                return;
            }
        }
        Activity?.Invoke(null);
    }

    public void Dispose()
    {
        Arm(false);
        if (deviceNotification != 0)
            UnregisterDeviceNotification(deviceNotification);
        DestroyHandle();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawRegistration
    {
        internal ushort UsagePage;
        internal ushort Usage;
        internal uint Flags;
        internal nint Window;
    }

    [StructLayout(LayoutKind.Explicit, Size = 48)]
    private struct RawMouseInput
    {
        [FieldOffset(0)] internal uint Type;
        [FieldOffset(8)] internal nint Device;
        [FieldOffset(28)] internal ushort ButtonFlags;
        [FieldOffset(36)] internal int X;
        [FieldOffset(40)] internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BroadcastInterface
    {
        internal int Size;
        internal int Type;
        internal int Reserved;
        internal Guid Class;
        internal char Name;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices(in RawRegistration devices, uint count, uint size);
    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(nint rawInput, uint command, out RawMouseInput data,
        ref uint size, uint headerSize);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern unsafe uint GetRawInputDeviceInfoW(nint device, uint command, char* data, ref uint size);
    [DllImport("user32.dll")]
    private static extern int GetMessageTime();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint RegisterDeviceNotificationW(nint window, ref BroadcastInterface filter, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterDeviceNotification(nint handle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint RegisterWindowMessageW(string message);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);
}
