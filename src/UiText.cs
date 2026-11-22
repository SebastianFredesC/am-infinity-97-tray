using System.Globalization;
using System.Resources;

namespace AMInfinity97Tray;

internal sealed class UiText(string language)
{
    internal const string Title = "Angry Miao Infinity .97";
    internal const string Spanish = "es";
    internal const string English = "en";
    private static readonly ResourceManager Resources = new("AMInfinity97Tray.UiStrings", typeof(UiText).Assembly);
    internal static UiText Default { get; } = new(Spanish);
    private readonly CultureInfo culture = CultureInfo.GetCultureInfo(NormalizeLanguage(language));

    internal static string NormalizeLanguage(string? language) =>
        string.Equals(language, English, StringComparison.OrdinalIgnoreCase) ? English : Spanish;

    internal string this[string key] =>
        Resources.GetString(key, culture) ?? throw new MissingManifestResourceException($"Missing UI text: {key}.");

    internal string Link(LinkState state) => this[$"Link{state}"];
    internal string BatteryName(bool spare) => this[spare ? "SpareBattery" : "Mouse"];

    internal static string FormatRate(int hertz) => $"{hertz / 1000} kHz";

    internal string PollingRate(PollingRateDisplay rate)
    {
        string value = rate switch
        {
            { Unsupported: true } => this["PollingRateUnsupported"],
            { Hertz: int hertz } => PollingRateProtocol.CanSet(hertz) ? FormatRate(hertz) : this["PollingRateBelowRange"],
            _ => "—"
        };
        if (rate is { Hertz: not null, Stale: true, Unsupported: false })
            value += $" ({this["EarlierReading"]})";
        return $"{this["PollingRate"]} · {value}";
    }

    internal string PollingMessage(MonitorDisplay display)
    {
        if (display.PollingRate.Unconfirmed)
            return this["PollingRateUnconfirmed"];
        if (display.Link != LinkState.ChangingPollingRate && display.PollingRate.Requested is int hertz)
            return string.Format(culture, this["PollingRatePending"], FormatRate(hertz));
        return "";
    }

    internal string BatteryDetail(BatteryDisplay battery, bool spare) => battery.Sample switch
    {
        null => this["UnknownLevel"],
        { Percent: null } => this["BatteryAbsent"],
        _ when !spare => "",
        { Charging: true } => this["Charging"],
        { Percent: 100 } => this["Charged"],
        _ => this["NotCharging"]
    };

    internal string Time(BatteryDisplay battery) => battery.Sample is Sample sample
        ? string.Format(culture, this[battery.Stale ? "LastReading" : "ReadAt"], Clock(sample.At))
        : this["NoReading"];

    internal string ManualPending(MonitorDisplay display) => this[display.Link switch
    {
        LinkState.Paused => "ResumeToRefresh",
        LinkState.Missing => "WaitForReceiver",
        LinkState.Multiple => "ConnectOneReceiver",
        LinkState.Busy when display.RateLimited => "WaitForAccess",
        _ when display.RateLimited => "WaitBetweenReads",
        _ => "MoveMouseToRefresh"
    }];

    internal string Tooltip(MonitorDisplay display)
    {
        var last = display.Mouse.Sample?.At;
        if (display.Spare.Sample is Sample sample && (last is null || sample.At > last))
            last = sample.At;
        string time = last is DateTimeOffset at ? Clock(at) : "—";
        return $"{Title}\n{BatteryName(false)}: {Compact(display.Mouse, false)}\n" +
            $"{BatteryName(true)}: {Compact(display.Spare, true)}\n{this["LastUpdated"]}: {time}";
    }

    internal string AlertLine(BatteryNotice notice, bool spare) =>
        $"{BatteryName(spare)}: {notice.Percent}% ({this[notice.Level == AlertLevel.Critical ? "AlertCritical" : "AlertLow"]})";

    private string Clock(DateTimeOffset when) => when.ToString("HH:mm:ss", culture);

    private string Compact(BatteryDisplay battery, bool spare)
    {
        string value = BatteryDetail(battery, spare);
        if (battery.Sample is { Percent: int percent } sample)
        {
            value = spare && (sample.Charging || percent == 100)
                ? $"{percent}% ({value})"
                : $"{percent}%";
        }
        return battery.Stale ? $"{value} ({this["EarlierReading"]})" : value;
    }
}
