using System.Buffers.Binary;

namespace AMInfinity97Tray;

internal enum BatteryTarget { Mouse, Spare }
internal enum ReplyStatus { Pending, Level, Absent, Rejected, Invalid }
internal readonly record struct BatteryReply(ReplyStatus Status, byte Percent = 0, bool Charging = false);

// Battery commands from aminf-cli (MIT) and the official AM35 web configurator.
internal static class BatteryProtocol
{
    internal const byte ReportId = 0x14;
    internal const byte OutputReportId = 0x06;
    internal const byte InputReportId = 0x07;
    internal const int ReportLength = 62;
    internal const ushort UsagePage = 0xFF13;
    private const ushort MouseCommand = 12495;
    private const ushort SpareCommand = 12303;

    internal static byte[] Request(BatteryTarget target, int length, byte reportId = ReportId)
    {
        var report = new byte[length];
        report[0] = reportId;
        report[1] = 6;
        report[2] = target == BatteryTarget.Mouse && reportId == ReportId ? (byte)0x80 : (byte)0x00;
        report[3] = 0x05;
        report[4] = 0x5A;
        report[5] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(report.AsSpan(7), Command(target));
        return report;
    }

    internal static BatteryReply Parse(ReadOnlySpan<byte> report, BatteryTarget target, byte reportId = ReportId)
    {
        if (report.Length < 3 || report[0] != reportId)
            return new(ReplyStatus.Invalid);
        bool streaming = reportId == InputReportId;
        bool bareFrame = streaming && report[1] is 0x05 or 0x15 && report[2] is 0x5B or 0x5D;
        int start = bareFrame ? 1 : 3;
        if (!bareFrame)
        {
            if (report[1] == 0)
                return new(ReplyStatus.Pending);
            if (report[1] < 6 || report[1] > report.Length - start)
                return new(ReplyStatus.Invalid);
            if (streaming)
                report = report[..(start + report[1])];
        }

        var result = new BatteryReply(ReplyStatus.Pending);
        // Feature reports can include stacked frames after the first envelope.
        for (int offset = start; offset < report.Length && report[offset] != 0;)
        {
            var frame = report[offset..];
            if (frame.Length < 6 || (frame[0] != 0x05 && !(streaming && frame[0] == 0x15)) ||
                frame[1] is not (0x5B or 0x5D))
                return new(ReplyStatus.Invalid);
            int length = BinaryPrimitives.ReadUInt16LittleEndian(frame[2..]);
            if (length < 2 || length > frame.Length - 4 ||
                (!bareFrame && offset == start && length + 4 > report[1]))
                return new(ReplyStatus.Invalid);

            if (frame[1] == 0x5B && BinaryPrimitives.ReadUInt16LittleEndian(frame[4..]) == Command(target))
            {
                // Duplicate matching replies are ambiguous. There is no request sequence number.
                if (result.Status != ReplyStatus.Pending)
                    return new(ReplyStatus.Invalid);
                result = ParseBattery(frame.Slice(6, length - 2));
            }
            offset += length + 4;
        }
        return report[start] == 0 ? new(ReplyStatus.Invalid) : result;
    }

    private static BatteryReply ParseBattery(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
            return new(ReplyStatus.Invalid);
        if (payload[0] != 0)
            return new(ReplyStatus.Rejected);
        if (payload.Length < 5)
            return new(ReplyStatus.Invalid);
        if (payload[4] == 0)
            return new(ReplyStatus.Absent);
        if (payload[2] > 100)
            return new(ReplyStatus.Invalid);
        return new(ReplyStatus.Level, payload[2], payload[1] != 0);
    }

    private static ushort Command(BatteryTarget target) => target switch
    {
        BatteryTarget.Mouse => MouseCommand,
        BatteryTarget.Spare => SpareCommand,
        _ => throw new ArgumentOutOfRangeException(nameof(target))
    };
}
