namespace AMInfinity97Tray;

internal static class MonitorPolicy
{
    internal const int MinimumInterval = 60;
    internal const int MaximumInterval = 3600;
    internal const long ManualIntervalMs = 5000;
    internal const long StabilizationMs = 350;
    internal const long CommandWindowMs = 2500;
    internal const long ActivitySampleMs = 15_000;
    internal const long PossibleSleepMs = 30_000;
    internal const int LowPercent = 20;
    internal const int CriticalPercent = 5;

    internal static bool CanRead(long now, long? activity, long eligibleAt, long stabilizationMs = StabilizationMs) =>
        activity is long observed && now >= eligibleAt &&
        now - observed >= stabilizationMs && CanSend(now, observed);

    internal static bool CanSend(long now, long activity) =>
        now >= activity && now - activity < CommandWindowMs;

    internal static long EarliestAttempt(long? lastAttempt, int interval, bool manualPending, long accessUntil)
    {
        long intervalMs = manualPending ? ManualIntervalMs : interval * 1000L;
        return Math.Max(lastAttempt is long last ? last + intervalMs : 0, accessUntil);
    }

    internal static long Backoff(int failures, int initialSeconds, int maximumSeconds) =>
        Math.Min(maximumSeconds * 1000L, initialSeconds * 1000L << Math.Clamp(failures - 1, 0, 10));

    internal static int IconBand(int? percentage) => percentage switch
    {
        null => 0,
        <= 10 => 1,
        <= 30 => 2,
        _ => 3
    };
}

internal enum AlertLevel { Normal, Low, Critical }

internal sealed class BatteryAlert(int lowPercent = MonitorPolicy.LowPercent, int criticalPercent = MonitorPolicy.CriticalPercent)
{
    private AlertLevel level;

    internal AlertLevel? Observe(BatteryReply reply)
    {
        if (reply.Status == ReplyStatus.Absent)
            level = AlertLevel.Normal;
        if (reply.Status != ReplyStatus.Level)
            return null;
        if (reply.Charging || reply.Percent > lowPercent + 3)
        {
            level = AlertLevel.Normal;
            return null;
        }

        if (reply.Percent <= criticalPercent && level != AlertLevel.Critical)
        {
            level = AlertLevel.Critical;
            return level;
        }
        if (level == AlertLevel.Critical && reply.Percent > criticalPercent + 3)
        {
            level = AlertLevel.Low;
            return null;
        }
        if (reply.Percent <= lowPercent && level == AlertLevel.Normal)
        {
            level = AlertLevel.Low;
            return level;
        }
        return null;
    }
}
