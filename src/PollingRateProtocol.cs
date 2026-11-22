using System.Buffers.Binary;

namespace AMInfinity97Tray;

// AM35 commands verified by aminf-cli over the receiver's feature report.
internal static class PollingRateProtocol
{
    private const ushort ReadCommand = 0x3011;
    private const ushort WriteCommand = 0x3010;

    internal static ReadOnlySpan<int> Options => [1000, 2000, 4000, 8000];

    internal static bool CanSet(int hertz) => Options.Contains(hertz);

    internal static byte[] ReadRequest(int length) =>
        BatteryProtocol.BuildRequest(ReadCommand, true, length, BatteryProtocol.ReportId);

    internal static byte[] WriteRequest(int length, int hertz)
    {
        if (!CanSet(hertz))
            throw new ArgumentOutOfRangeException(nameof(hertz));

        Span<byte> data = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(data, (ushort)hertz);
        return BatteryProtocol.BuildRequest(WriteCommand, true, length, BatteryProtocol.ReportId, data);
    }

    internal static (ReplyStatus Status, int? Hertz) Parse(ReadOnlySpan<byte> report)
    {
        var status = BatteryProtocol.MatchReply(report, ReadCommand, BatteryProtocol.ReportId, out var payload);
        if (status != ReplyStatus.Level)
            return (status, null);
        if (payload.IsEmpty)
            return (ReplyStatus.Invalid, null);
        if (payload[0] != 0)
            return (ReplyStatus.Rejected, null);
        if (payload.Length < 3)
            return (ReplyStatus.Invalid, null);

        int hertz = BinaryPrimitives.ReadUInt16LittleEndian(payload[1..]);
        return CanSet(hertz) || hertz is 125 or 250 or 500
            ? (ReplyStatus.Level, hertz)
            : (ReplyStatus.Invalid, null);
    }
}
