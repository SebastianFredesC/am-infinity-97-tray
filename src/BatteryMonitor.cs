namespace AMInfinity97Tray;

internal enum LinkState
{
    Detecting, Missing, Multiple, WaitingInput, Active, PossibleSleep,
    Reading, ChangingPollingRate, NoReply, BatteryMissing, Error, Busy, Paused, Suspended, InputUnavailable, Unsupported
}

internal readonly record struct Sample(int? Percent, bool Charging, DateTimeOffset At, long Tick);
internal readonly record struct BatteryDisplay(Sample? Sample, bool Stale);
internal readonly record struct PollingRateDisplay(int? Hertz, bool Stale, int? Requested, bool Unconfirmed,
    bool CanChange, bool Unsupported = false);
internal readonly record struct MonitorDisplay(LinkState Link, BatteryDisplay Mouse, BatteryDisplay Spare,
    bool ManualPending, bool RateLimited, PollingRateDisplay PollingRate = default);
internal readonly record struct BatteryNotice(int Percent, AlertLevel Level);

internal sealed class BatteryMonitor : IDisposable
{
    private const int RateRequestWindowMs = 5000;
    private readonly DeviceWindow window;
    private readonly HidBatteryService hid;
    private readonly Diagnostics diagnostics;
    private readonly Preferences preferences;
    private readonly System.Windows.Forms.Timer timer = new();
    private readonly BatteryAlert mouseAlert;
    private readonly BatteryAlert spareAlert;
    private CancellationTokenSource? operation;
    private string? receiver;
    private Sample? mouse;
    private Sample? spare;
    private bool mouseFailed;
    private bool spareFailed;
    private bool paused;
    private bool suspended;
    private bool closed;
    private bool manualPending;
    private bool interactionVisible;
    private bool inputUnavailable;
    private int interval;
    private int discoveryFailures;
    private int accessFailures;
    private long discoveryAt;
    private long roundDeadline = long.MaxValue;
    private long startedAt;
    private long connectedAt;
    private long rearmAt;
    private long accessUntil;
    private long? lastAttempt;
    private long? activity;
    private int? pollingRate;
    private int? requestedRate;
    private int? unconfirmedRate;
    private long rateRequestDeadline;
    private bool rateStale;
    private bool rateUnsupported;
    private LinkState link = LinkState.Detecting;
    private LinkState? lastLoggedLink;
    private LinkState? problem;

    internal event Action<MonitorDisplay>? Changed;
    internal event Action<BatteryNotice?, BatteryNotice?>? Alert;
    internal bool Paused => paused;
    internal bool Notifications { get; set; }

    internal BatteryMonitor(DeviceWindow window, Diagnostics diagnostics, Preferences preferences)
    {
        this.window = window;
        this.diagnostics = diagnostics;
        this.preferences = preferences;
        interval = preferences.Interval;
        Notifications = preferences.Notifications;
        mouseAlert = new(preferences.LowPercent, preferences.CriticalPercent);
        spareAlert = new(preferences.LowPercent, preferences.CriticalPercent);
        hid = new(diagnostics);
        timer.Tick += (_, _) => Evaluate();
        window.Activity += OnActivity;
        window.ReceiverChanged += OnReceiverChanged;
        window.PowerChanged += OnPowerChanged;
    }

    internal void Start() => Schedule(Environment.TickCount64);

    internal void SetInterval(int seconds)
    {
        interval = seconds;
        Evaluate();
    }

    internal void RequestUpdate()
    {
        if (manualPending || link == LinkState.Reading)
            return;

        manualPending = true;
        diagnostics.Write("Actualización manual solicitada");
        Evaluate();
    }

    internal void SetInteractionVisible(bool visible)
    {
        if (closed || interactionVisible == visible)
            return;

        interactionVisible = visible;
        Evaluate();
    }

    internal void RequestPollingRate(int hertz)
    {
        long now = Environment.TickCount64;
        if (!CanChangePollingRate(now) || !PollingRateProtocol.CanSet(hertz))
            return;

        requestedRate = hertz;
        unconfirmedRate = null;
        rateRequestDeadline = now + RateRequestWindowMs;
        Evaluate();
    }

    private bool CanChangePollingRate(long now) =>
        !closed && !paused && !suspended && receiver is not null && !rateUnsupported &&
        !inputUnavailable && now >= accessUntil && requestedRate is null;

    private void RejectPendingRate()
    {
        if (requestedRate is int hertz)
        {
            unconfirmedRate = hertz;
            requestedRate = null;
        }
    }

    internal void SetPaused(bool value)
    {
        paused = value;
        InvalidateActivity();
        if (!paused && receiver is null)
            discoveryAt = Environment.TickCount64;
        diagnostics.Write(paused ? "Monitoreo en pausa" : "Monitoreo reanudado");
        Evaluate();
    }

    private void OnReceiverChanged()
    {
        InvalidateActivity();
        receiver = null;
        problem = null;
        rateUnsupported = false;
        discoveryAt = Environment.TickCount64 + 350;
        Evaluate();
    }

    private void OnPowerChanged(bool nowSuspended)
    {
        if (nowSuspended == suspended)
            return;
        suspended = nowSuspended;
        OnReceiverChanged();
        diagnostics.Write(suspended ? "Windows suspendido" : "Windows reanudado");
    }

    private void InvalidateActivity()
    {
        operation?.Cancel();
        roundDeadline = long.MaxValue;
        activity = null;
        window.Arm(false);
        window.ForgetRawDevice();
        mouseFailed = spareFailed = true;
        rateStale = true;
        RejectPendingRate();
    }

    private void OnActivity(long? observedAt)
    {
        if (observedAt is long observed)
        {
            activity = observed;
            rearmAt = 0;
        }
        else
        {
            rearmAt = Environment.TickCount64 + 250;
        }
        Evaluate();
    }

    private void Evaluate()
    {
        timer.Stop();
        if (closed)
            return;

        long now = Environment.TickCount64;
        long next = long.MaxValue;
        if (requestedRate is not null && (operation is null || link != LinkState.ChangingPollingRate))
        {
            if (now >= rateRequestDeadline)
                RejectPendingRate();
            else
                next = rateRequestDeadline;
        }
        long eligibleAt = MonitorPolicy.EarliestAttempt(lastAttempt, interval, manualPending, accessUntil);
        bool arm = false;
        if (suspended || paused)
        {
            link = suspended ? LinkState.Suspended : LinkState.Paused;
        }
        else if (operation is not null)
        {
            if (now >= roundDeadline)
            {
                InvalidateActivity();
                problem = LinkState.Error;
                link = LinkState.Error;
                diagnostics.Write("Plazo de la ronda HID agotado");
            }
            next = Math.Min(next, roundDeadline);
        }
        else if (now >= discoveryAt)
        {
            DiscoverAsync();
        }
        else if (receiver is not null)
        {
            next = Math.Min(next, discoveryAt);
            long attemptAt = requestedRate is not null ? accessUntil : eligibleAt;
            bool canTry = now >= attemptAt;
            if (canTry && MonitorPolicy.CanRead(now, activity, attemptAt, preferences.StabilizationMs))
            {
                ReadAsync(receiver, activity!.Value, now, requestedRate);
                next = roundDeadline;
            }
            else
            {
                if (problem is not null)
                    link = problem.Value;
                else if (inputUnavailable)
                    link = LinkState.InputUnavailable;
                else if (now >= (activity ?? connectedAt) + MonitorPolicy.PossibleSleepMs)
                    link = LinkState.PossibleSleep;
                else
                    link = activity.HasValue ? LinkState.Active : LinkState.WaitingInput;

                // User interaction can prepare physical activity for an on-demand command.
                // Sampling more often never shortens the automatic battery interval.
                long sampleAt = activity is long observed
                    ? observed + (canTry || manualPending || interactionVisible
                        ? MonitorPolicy.CommandWindowMs : MonitorPolicy.ActivitySampleMs)
                    : now;
                if (canTry && activity is long recent && now < recent + preferences.StabilizationMs)
                    next = Math.Min(next, recent + preferences.StabilizationMs);
                else if (now >= accessUntil)
                {
                    long armAt = Math.Max(sampleAt, rearmAt);
                    if (now >= armAt)
                        arm = true;
                    else
                        next = Math.Min(next, armAt);
                }
                if (!canTry)
                    next = Math.Min(next, attemptAt);
                if (accessUntil > now)
                    next = Math.Min(next, accessUntil);
                long sleepAt = (activity ?? connectedAt) + MonitorPolicy.PossibleSleepMs;
                if (sleepAt > now)
                    next = Math.Min(next, sleepAt);
            }
        }
        else
        {
            next = discoveryAt;
            link = problem ?? LinkState.Detecting;
        }

        if (!window.Arm(arm) && arm)
        {
            if (!inputUnavailable)
                diagnostics.Write("Raw Input no disponible");
            inputUnavailable = true;
            rearmAt = now + 5000;
            next = Math.Min(next, rearmAt);
            link = problem ?? LinkState.InputUnavailable;
        }
        else if (arm)
        {
            if (inputUnavailable && link == LinkState.InputUnavailable)
                link = LinkState.WaitingInput;
            inputUnavailable = false;
        }

        long maxAge = interval * 2000L;
        if (link == LinkState.PossibleSleep)
            mouseFailed = true;
        var mouseDisplay = Display(mouse, mouseFailed, now, maxAge, ref next);
        var spareDisplay = Display(spare, spareFailed, now, maxAge, ref next);
        if (diagnostics.Enabled && lastLoggedLink != link)
        {
            lastLoggedLink = link;
            diagnostics.Write(UiText.Default.Link(link));
        }
        var rate = new PollingRateDisplay(pollingRate, rateStale, requestedRate, unconfirmedRate is not null,
            CanChangePollingRate(now), rateUnsupported);
        Changed?.Invoke(new(link, mouseDisplay, spareDisplay, manualPending, now < eligibleAt, rate));
        if (next != long.MaxValue)
            Schedule(next);
    }

    private static BatteryDisplay Display(Sample? sample, bool failed, long now, long maxAge, ref long next)
    {
        long expires = sample is Sample value ? value.Tick + maxAge : 0;
        if (!failed && now < expires)
        {
            next = Math.Min(next, expires);
            return new(sample, false);
        }
        return new(sample, sample.HasValue);
    }

    private void Schedule(long at)
    {
        timer.Interval = (int)Math.Clamp(at - Environment.TickCount64, 15, int.MaxValue);
        timer.Start();
    }

    private async void DiscoverAsync()
    {
        using var current = operation = new();
        if (receiver is null)
            link = problem ?? LinkState.Detecting;
        Discovery result;
        try
        {
            result = await Task.Run(HidBatteryService.Discover)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.ForceYielding);
        }
        catch (Exception error)
        {
            diagnostics.Write($"Descubrimiento: {error.GetType().Name}");
            result = new(HidStatus.Error);
        }
        operation = null;
        if (closed)
            return;
        if (!current.IsCancellationRequested)
        {
            if (result.Status == HidStatus.Ready)
            {
                if (receiver is null)
                {
                    diagnostics.Write("Receptor .97 detectado");
                    connectedAt = Environment.TickCount64;
                    problem = null;
                }
                receiver = result.Path;
                discoveryFailures = 0;
                discoveryAt = window.HasDeviceNotifications ? long.MaxValue : Environment.TickCount64 + 120_000;
            }
            else
            {
                receiver = null;
                activity = null;
                mouseFailed = spareFailed = true;
                rateStale = true;
                RejectPendingRate();
                problem = result.Status switch
                {
                    HidStatus.Missing => LinkState.Missing,
                    HidStatus.Multiple => LinkState.Multiple,
                    _ => LinkState.Error
                };
                ScheduleRediscovery();
            }
        }
        Evaluate();
    }

    private async void ReadAsync(string path, long observed, long now, int? rateToSet)
    {
        using var current = operation = new();
        if (rateToSet is null)
        {
            lastAttempt = startedAt = now;
            manualPending = false;
        }
        roundDeadline = now + 3000;
        link = rateToSet is null ? LinkState.Reading : LinkState.ChangingPollingRate;
        BatteryRead result;
        try
        {
            result = await Task.Run(() => hid.Read(path, observed, current.Token, rateToSet))
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.ForceYielding);
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            result = default;
        }
        catch (Exception error)
        {
            diagnostics.Write($"Lectura: {error.GetType().Name}");
            result = new(HidStatus.Error);
        }
        operation = null;
        if (closed)
            return;
        roundDeadline = long.MaxValue;
        if (!current.IsCancellationRequested)
        {
            if (rateToSet is int hertz)
                AcceptRateChange(result, hertz);
            else
                Accept(result);
        }
        Evaluate();
    }

    private void AcceptRateChange(BatteryRead result, int requested)
    {
        requestedRate = null;
        unconfirmedRate = result.PollingRate == requested ? null : requested;
        AcceptPollingRate(result);
        if (result.Status == HidStatus.Busy)
            problem = LinkState.Busy;
        else if (result.Status == HidStatus.Missing)
            problem = LinkState.Missing;
        else if (result.Status == HidStatus.Ready && problem == LinkState.Busy)
            problem = null;
        AcceptAccess(result.Status);
    }

    private void AcceptPollingRate(BatteryRead result)
    {
        if (result.PollingRateSupported is bool supported)
            rateUnsupported = !supported;
        rateStale = result.PollingRate is null;
        if (result.PollingRate is int hertz)
        {
            pollingRate = hertz;
            if (unconfirmedRate == hertz)
                unconfirmedRate = null;
        }
        if (rateUnsupported)
            RejectPendingRate();
    }

    private void Accept(BatteryRead result)
    {
        var seen = DateTimeOffset.Now;
        Apply(result.Mouse, ref mouse, ref mouseFailed, seen);
        Apply(result.Spare, ref spare, ref spareFailed, seen);
        AcceptPollingRate(result);
        problem = result.Status switch
        {
            HidStatus.Busy => LinkState.Busy,
            HidStatus.Missing => LinkState.Missing,
            HidStatus.Unsupported => LinkState.Unsupported,
            HidStatus.Error => LinkState.Error,
            _ when result.Mouse.Status == ReplyStatus.Pending => LinkState.NoReply,
            _ when result.Mouse.Status == ReplyStatus.Absent => LinkState.BatteryMissing,
            _ when result.Mouse.Status != ReplyStatus.Level ||
                result.Spare.Status is ReplyStatus.Invalid or ReplyStatus.Rejected => LinkState.Error,
            _ => null
        };
        AcceptAccess(result.Status);
        var mouseNotice = mouseAlert.Observe(result.Mouse);
        var spareNotice = spareAlert.Observe(result.Spare);
        if (Notifications && (mouseNotice.HasValue || spareNotice.HasValue))
        {
            Alert?.Invoke(
                mouseNotice is AlertLevel mouseLevel ? new BatteryNotice(result.Mouse.Percent, mouseLevel) : null,
                spareNotice is AlertLevel spareLevel ? new BatteryNotice(result.Spare.Percent, spareLevel) : null);
        }
    }

    private void AcceptAccess(HidStatus status)
    {
        if (status == HidStatus.Busy)
        {
            accessUntil = Environment.TickCount64 +
                MonitorPolicy.Backoff(++accessFailures, preferences.BusyStartSeconds, preferences.BusyMaxSeconds);
            RejectPendingRate();
        }
        else
        {
            accessFailures = 0;
        }

        if (status == HidStatus.Missing)
        {
            receiver = null;
            activity = null;
            mouseFailed = spareFailed = true;
            rateUnsupported = false;
            RejectPendingRate();
            ScheduleRediscovery();
        }
    }

    private void Apply(BatteryReply reply, ref Sample? slot, ref bool failed, DateTimeOffset time)
    {
        failed = reply.Status is not (ReplyStatus.Level or ReplyStatus.Absent);
        if (!failed)
            slot = new(reply.Status == ReplyStatus.Level ? reply.Percent : null, reply.Charging, time, startedAt);
    }

    private void ScheduleRediscovery() => discoveryAt = Environment.TickCount64 +
        MonitorPolicy.Backoff(++discoveryFailures, preferences.ReconnectStartSeconds, preferences.ReconnectMaxSeconds);

    public void Dispose()
    {
        closed = true;
        operation?.Cancel();
        window.Activity -= OnActivity;
        window.ReceiverChanged -= OnReceiverChanged;
        window.PowerChanged -= OnPowerChanged;
        window.Arm(false);
        timer.Dispose();
    }
}
