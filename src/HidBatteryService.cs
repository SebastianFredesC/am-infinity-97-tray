using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AMInfinity97Tray;

internal enum HidStatus { Ready, Missing, Multiple, Busy, Error, Unsupported }
internal readonly record struct Discovery(HidStatus Status, string? Path = null);
internal readonly record struct BatteryRead(HidStatus Status, BatteryReply Mouse = default, BatteryReply Spare = default,
    int? PollingRate = null, bool? PollingRateSupported = null);

internal sealed class HidBatteryService(Diagnostics diagnostics)
{
    private const uint GetFeature = 0x000B0192;
    private const uint SetFeature = 0x000B0191;
    private const int FeatureReplyTimeoutMs = 950;
    private const int InputReplyTimeoutMs = 1800;
    private const int ReplySpacingMs = 45;
    private const int RateApplyDelayMs = 120;
    private const int HidSuccess = 0x00110000;
    internal static readonly Guid HidClass = new("4d1e55b2-f16f-11cf-88cb-001111000030");
    private static readonly PropertyKey UsagePage = new(new("cbf38310-4a17-4310-a1eb-247f0b67593b"), 2);

    internal static bool IsReceiverPath(string? path)
    {
        const string prefix = @"\\?\hid#vid_0e8d&pid_0703";
        return path is not null && path.Length > prefix.Length &&
            path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && path[prefix.Length] is '&' or '#';
    }

    // Configuration Manager reads cached interface properties, with no HID handle
    // or feature report. Discovery therefore also works during mouse inactivity.
    internal static Discovery Discover()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (CM_Get_Device_Interface_List_SizeW(out uint size, in HidClass, null, 0) != 0)
                return new(HidStatus.Error);
            var paths = new char[size];
            uint status = CM_Get_Device_Interface_ListW(in HidClass, null, paths, size, 0);
            if (status == 0x1A) // CR_BUFFER_SMALL: device list changed.
                continue;
            if (status != 0)
                return new(HidStatus.Error);

            string? receiver = null;
            foreach (string path in new string(paths).Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!IsReceiverPath(path))
                    continue;
                uint propertySize = sizeof(ushort);
                uint propertyStatus = CM_Get_Device_Interface_PropertyW(path, in UsagePage,
                    out uint type, out ushort page, ref propertySize, 0);
                if (propertyStatus != 0 || type != 5 || propertySize != sizeof(ushort))
                    return new(HidStatus.Error);
                if (page != BatteryProtocol.UsagePage)
                    continue;
                if (receiver is not null)
                    return new(HidStatus.Multiple);
                receiver = path;
            }
            return receiver is null ? new(HidStatus.Missing) : new(HidStatus.Ready, receiver);
        }
        return new(HidStatus.Error);
    }

    internal BatteryRead Read(string path, long activity, CancellationToken token, int? requestedRate = null)
    {
        token.ThrowIfCancellationRequested();
        if (!MonitorPolicy.CanSend(Environment.TickCount64, activity))
            return new(HidStatus.Ready);

        BatteryReply mouse = default;
        BatteryReply spare = default;
        try
        {
            // A shared handle is held only for this round. Access denial is left
            // to the controller's backoff, with no alternate open attempts.
            using var device = CreateFileW(path, 0xC0000000, 3, 0, 3, 0x40000000, 0);
            if (device.IsInvalid)
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            var transport = GetTransport(device);
            if (transport is null)
                return new(HidStatus.Unsupported);

            using var completion = new EventWaitHandle(false, EventResetMode.ManualReset);
            WaitHandle[] waits = [completion, token.WaitHandle];
            if (requestedRate is int hertz)
            {
                if (!transport.Value.Feature)
                    return new(HidStatus.Unsupported, PollingRateSupported: false);

                long deadline = Math.Min(Environment.TickCount64 + FeatureReplyTimeoutMs, activity + MonitorPolicy.CommandWindowMs);
                byte[] request = PollingRateProtocol.WriteRequest(transport.Value.OutputLength, hertz);
                diagnostics.Write($"Solicitud de polling rate: {hertz} Hz");
                if (!Transfer(device, completion, waits, Operation.SetFeature, request, deadline, token, out _))
                    return new(HidStatus.Ready, PollingRateSupported: true);

                // A successful USB transfer only confirms delivery. Read back the setting
                // after it has had time to apply, without retrying the write.
                if (token.WaitHandle.WaitOne(RateApplyDelayMs))
                    token.ThrowIfCancellationRequested();
                int? confirmed = QueryRate(device, completion, waits, transport.Value, activity, token);
                return new(HidStatus.Ready, PollingRate: confirmed, PollingRateSupported: true);
            }

            mouse = QueryBattery(device, completion, waits, BatteryTarget.Mouse, transport.Value, activity, token);
            if (mouse.Status != ReplyStatus.Level)
                return new(HidStatus.Ready, mouse, PollingRateSupported: transport.Value.Feature);
            spare = QueryBattery(device, completion, waits, BatteryTarget.Spare, transport.Value, activity, token);
            if (!transport.Value.Feature)
                return new(HidStatus.Ready, mouse, spare, PollingRateSupported: false);

            int? rate = null;
            try
            {
                rate = QueryRate(device, completion, waits, transport.Value, activity, token);
            }
            catch (Win32Exception error)
            {
                diagnostics.Write($"Polling rate HID Win32={error.NativeErrorCode} ({error.Message})");
            }
            return new(HidStatus.Ready, mouse, spare, rate, PollingRateSupported: true);
        }
        catch (Win32Exception error)
        {
            diagnostics.Write($"HID Win32={error.NativeErrorCode} ({error.Message})");
            return new(error.NativeErrorCode switch
            {
                2 or 3 or 6 or 21 or 433 or 1167 or 1617 => HidStatus.Missing,
                5 or 32 or 33 or 170 => HidStatus.Busy,
                _ => HidStatus.Error
            }, mouse, spare);
        }
    }

    private BatteryReply QueryBattery(SafeFileHandle device, EventWaitHandle completion, WaitHandle[] waits,
        BatteryTarget target, Transport transport, long activity, CancellationToken token)
    {
        var request = BatteryProtocol.Request(target, transport.OutputLength, transport.OutputId);
        var result = Query<BatteryReply>(device, completion, waits, transport, activity, token, request, target.ToString(), bytes =>
        {
            var parsed = BatteryProtocol.Parse(bytes, target, transport.InputId);
            return (parsed.Status, parsed);
        });
        diagnostics.Write($"Respuesta {target}: {result.Status}, nivel={result.Value.Percent}, carga={result.Value.Charging}");
        return result.Value with { Status = result.Status };
    }

    private int? QueryRate(SafeFileHandle device, EventWaitHandle completion, WaitHandle[] waits,
        Transport transport, long activity, CancellationToken token)
    {
        var request = PollingRateProtocol.ReadRequest(transport.OutputLength);
        var result = Query<int?>(device, completion, waits, transport, activity, token, request, "PollingRate", PollingRateProtocol.Parse);
        diagnostics.Write($"Respuesta PollingRate: {result.Status}, Hz={result.Value}");
        return result.Value;
    }

    private delegate (ReplyStatus Status, T Value) ReplyParser<T>(ReadOnlySpan<byte> report);

    private (ReplyStatus Status, T Value) Query<T>(SafeFileHandle device, EventWaitHandle completion, WaitHandle[] waits,
        Transport transport, long activity, CancellationToken token, byte[] request, string target, ReplyParser<T> parse)
    {
        long now = Environment.TickCount64;
        if (!MonitorPolicy.CanSend(now, activity))
            return default;
        long deadline = Math.Min(now + (transport.Feature ? FeatureReplyTimeoutMs : InputReplyTimeoutMs),
            activity + MonitorPolicy.CommandWindowMs);
        diagnostics.Write($"Consulta {target} (actividad hace {now - activity} ms)");
        var response = new byte[transport.InputLength];
        int firstRead = 0;
        if (transport.Feature)
        {
            if (!Transfer(device, completion, waits, Operation.SetFeature, request, deadline, token, out _))
                return default;
        }
        else if (!ExchangeReports(device, completion, waits, request, response, deadline, token, out firstRead))
        {
            diagnostics.Write($"Respuesta {target}: sin informe de entrada a tiempo");
            return default;
        }

        bool invalidResponse = false;
        for (int attempt = 0; attempt < 40 && Environment.TickCount64 < deadline; attempt++)
        {
            if (transport.Feature && token.WaitHandle.WaitOne(ReplySpacingMs))
                token.ThrowIfCancellationRequested();

            if (transport.Feature)
            {
                Array.Clear(response);
                response[0] = transport.InputId;
            }
            var receive = transport.Feature ? Operation.GetFeature : Operation.Read;
            int received = firstRead;
            firstRead = 0;
            if (received == 0 && !Transfer(device, completion, waits, receive, response, deadline, token, out received))
                break;

            var bytes = response.AsSpan(0, Math.Min(received, response.Length));
            var reply = parse(bytes);
            if (reply.Status == ReplyStatus.Invalid)
            {
                if (!invalidResponse && diagnostics.Enabled)
                    diagnostics.Write($"Respuesta {target}: trama no válida, bytes={received}, encabezado={Convert.ToHexString(bytes[..Math.Min(9, bytes.Length)])}");
                invalidResponse = true;
                continue;
            }
            if (reply.Status != ReplyStatus.Pending)
                return reply;
        }
        diagnostics.Write($"Respuesta {target}: sin respuesta a tiempo");
        return (invalidResponse ? ReplyStatus.Invalid : ReplyStatus.Pending, default!);
    }

    // Start listening before the single write, as a reply can arrive as soon as
    // the output transfer completes. The read and write belong to the same query.
    private static unsafe bool ExchangeReports(SafeFileHandle device, EventWaitHandle writeCompletion, WaitHandle[] writeWaits,
        byte[] request, byte[] response, long deadline, CancellationToken token, out int received)
    {
        token.ThrowIfCancellationRequested();
        using var readCompletion = new EventWaitHandle(false, EventResetMode.ManualReset);
        var read = new Overlapped { Event = readCompletion.SafeWaitHandle.DangerousGetHandle() };
        fixed (byte* bytes = response)
        {
            bool pending = !BeginTransfer(device, Operation.Read, bytes, response.Length, &read, out received);
            try
            {
                if (!Transfer(device, writeCompletion, writeWaits, Operation.Write, request, deadline, token, out int written))
                    return false;
                if (written != request.Length)
                    throw new Win32Exception(29, $"Write incompleto ({written}/{request.Length})");
                return !pending || WaitForTransfer(device, Operation.Read, [readCompletion, token.WaitHandle],
                    deadline, token, &read, ref pending, out received);
            }
            finally
            {
                if (pending)
                    CancelTransfer(device, &read);
            }
        }
    }

    private static unsafe bool Transfer(SafeFileHandle device, EventWaitHandle completion, WaitHandle[] waits,
        Operation kind, byte[] buffer, long deadline, CancellationToken token, out int transferred)
    {
        token.ThrowIfCancellationRequested();
        transferred = 0;
        if (Environment.TickCount64 >= deadline)
            return false;

        completion.Reset();
        var operation = new Overlapped { Event = completion.SafeWaitHandle.DangerousGetHandle() };
        fixed (byte* bytes = buffer)
        {
            bool pending = !BeginTransfer(device, kind, bytes, buffer.Length, &operation, out transferred);
            try
            {
                return !pending || WaitForTransfer(device, kind, waits, deadline, token, &operation, ref pending, out transferred);
            }
            finally
            {
                if (pending)
                    CancelTransfer(device, &operation);
            }
        }
    }

    private static unsafe bool BeginTransfer(SafeFileHandle device, Operation kind, byte* bytes, int length,
        Overlapped* operation, out int transferred)
    {
        bool completed = kind switch
        {
            Operation.Read => ReadFile(device, bytes, length, out transferred, operation),
            Operation.Write => WriteFile(device, bytes, length, out transferred, operation),
            Operation.GetFeature => DeviceIoControl(device, GetFeature, bytes, length, bytes, length, out transferred, operation),
            _ => DeviceIoControl(device, SetFeature, bytes, length, null, 0, out transferred, operation)
        };
        if (completed)
            return true;
        int error = Marshal.GetLastPInvokeError();
        if (error != 997) // ERROR_IO_PENDING
            throw new Win32Exception(error, kind.ToString());
        return false;
    }

    private static unsafe bool WaitForTransfer(SafeFileHandle device, Operation kind, WaitHandle[] waits,
        long deadline, CancellationToken token, Overlapped* operation, ref bool pending, out int transferred)
    {
        transferred = 0;
        int remaining = (int)Math.Max(0, deadline - Environment.TickCount64);
        if (WaitHandle.WaitAny(waits, remaining) != 0)
        {
            token.ThrowIfCancellationRequested();
            return false;
        }
        pending = false;
        if (!GetOverlappedResult(device, operation, out transferred, false))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), kind.ToString());
        return true;
    }

    private static unsafe void CancelTransfer(SafeFileHandle device, Overlapped* operation)
    {
        CancelIoEx(device, operation);
        // The worker must keep the buffer pinned until the driver acknowledges cancellation.
        GetOverlappedResult(device, operation, out _, true);
    }

    private Transport? GetTransport(SafeFileHandle device)
    {
        if (!HidD_GetPreparsedData(device, out nint data))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        try
        {
            if (HidP_GetCaps(data, out HidCaps caps) != HidSuccess || caps.UsagePage != BatteryProtocol.UsagePage)
                return null;

            diagnostics.Write($"Descriptor HID FF13: entrada={caps.InputLength}, salida={caps.OutputLength}, características={caps.FeatureLength}");
            // Firmware 1.8.4 still advertises 06/07, but its battery commands
            // use the feature report. Choose before sending any request.
            if (HasReport(data, 2, BatteryProtocol.ReportId, caps.FeatureLength, caps.FeatureValueCaps))
            {
                diagnostics.Write("Transporte HID: informe de características 14");
                return new(true, caps.FeatureLength, BatteryProtocol.ReportId, caps.FeatureLength, BatteryProtocol.ReportId);
            }
            if (HasReport(data, 0, BatteryProtocol.InputReportId, caps.InputLength, caps.InputValueCaps) &&
                HasReport(data, 1, BatteryProtocol.OutputReportId, caps.OutputLength, caps.OutputValueCaps))
            {
                diagnostics.Write("Transporte HID: salida 06 / entrada 07");
                return new(false, caps.OutputLength, BatteryProtocol.OutputReportId, caps.InputLength, BatteryProtocol.InputReportId);
            }
            return null;
        }
        finally
        {
            HidD_FreePreparsedData(data);
        }
    }

    private static bool HasReport(nint descriptor, int kind, byte id, ushort length, ushort count)
    {
        if (length < BatteryProtocol.ReportLength || count == 0)
            return false;
        var values = new HidValueCaps[count];
        if (HidP_GetValueCaps(kind, values, ref count, descriptor) != HidSuccess)
            return false;
        for (int index = 0; index < count; index++)
        {
            if (values[index].ReportId == id)
                return true;
        }
        return false;
    }

    private enum Operation { Read, Write, GetFeature, SetFeature }
    private readonly record struct Transport(bool Feature, int OutputLength, byte OutputId, int InputLength, byte InputId);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct PropertyKey(Guid Format, uint Id);

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct HidCaps
    {
        [FieldOffset(2)] internal ushort UsagePage;
        [FieldOffset(4)] internal ushort InputLength;
        [FieldOffset(6)] internal ushort OutputLength;
        [FieldOffset(8)] internal ushort FeatureLength;
        [FieldOffset(48)] internal ushort InputValueCaps;
        [FieldOffset(54)] internal ushort OutputValueCaps;
        [FieldOffset(60)] internal ushort FeatureValueCaps;
    }

    [StructLayout(LayoutKind.Explicit, Size = 72)]
    private struct HidValueCaps
    {
        [FieldOffset(2)] internal byte ReportId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Overlapped
    {
        internal nuint Internal;
        internal nuint InternalHigh;
        internal uint Offset;
        internal uint OffsetHigh;
        internal nint Event;
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_Interface_List_SizeW(out uint length, in Guid interfaceClass,
        string? deviceId, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_Interface_ListW(in Guid interfaceClass, string? deviceId,
        [Out] char[] buffer, uint length, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_Interface_PropertyW(string path, in PropertyKey key,
        out uint type, out ushort value, ref uint size, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, nint security,
        uint disposition, uint flags, nint template);
    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle device, out nint data);
    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_FreePreparsedData(nint data);
    [DllImport("hid.dll")]
    private static extern int HidP_GetCaps(nint data, out HidCaps caps);
    [DllImport("hid.dll")]
    private static extern int HidP_GetValueCaps(int kind, [Out] HidValueCaps[] values, ref ushort count, nint data);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern unsafe bool ReadFile(SafeFileHandle device, byte* buffer, int length,
        out int transferred, Overlapped* operation);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern unsafe bool WriteFile(SafeFileHandle device, byte* buffer, int length,
        out int transferred, Overlapped* operation);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern unsafe bool DeviceIoControl(SafeFileHandle device, uint code, byte* input,
        int inputLength, byte* output, int outputLength, out int transferred, Overlapped* operation);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern unsafe bool GetOverlappedResult(SafeFileHandle device, Overlapped* operation,
        out int transferred, [MarshalAs(UnmanagedType.Bool)] bool wait);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern unsafe bool CancelIoEx(SafeFileHandle device, Overlapped* operation);
}
