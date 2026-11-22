using System.Security;

namespace AMInfinity97Tray;

internal sealed class TrayContext : ApplicationContext
{
    private readonly DeviceWindow window = new();
    private readonly Diagnostics diagnostics;
    private readonly TrayIcon tray;
    private readonly BatteryMonitor monitor;
    private readonly TrayMenu menu = new();
    private readonly TrayMenu intervalMenu = new();
    private readonly TrayMenu languageMenu = new();
    private readonly TrayMenu pollingMenu = new();
    private readonly ToolStripMenuItem startup = new();
    private readonly ToolStripMenuItem notifications = new();
    private readonly ToolStripMenuItem pause = new();
    private readonly TraySubmenuItem intervals;
    private readonly TraySubmenuItem languages;
    private readonly TraySubmenuItem polling;
    private readonly ToolStripMenuItem spanish = new("Español");
    private readonly ToolStripMenuItem english = new("English");
    private readonly ToolStripMenuItem exit = new();
    private readonly System.Windows.Forms.Timer shellRetry = new() { Interval = 5000 };
    private BatteryPanel? panel;
    private ToolStripMenuItem? saveDiagnostics;
    private Preferences preferences;
    private UiText text;
    private Point rightClickPoint;
    private long? rightClickAt;
    private MonitorDisplay display = new(LinkState.Detecting, default, default, false, false);

    internal TrayContext(bool diagnosticMode)
    {
        intervals = new("", intervalMenu) { DropDown = intervalMenu };
        languages = new("", languageMenu) { DropDown = languageMenu };
        polling = new("", pollingMenu) { DropDown = pollingMenu, Enabled = false };
        diagnostics = new(diagnosticMode);
        preferences = Preferences.Load(diagnostics);
        text = new(preferences.Language);
        tray = new(window.Handle);
        monitor = new(window, diagnostics, preferences);
        monitor.Changed += Render;
        monitor.Alert += ShowBatteryAlert;
        window.TrayEvent += OnTrayEvent;
        window.ShellRestarted += AddTray;
        shellRetry.Tick += (_, _) => AddTray();
        ConfigureMenu();
        ApplyMenuText();
        ApplyMenuTheme();
        Render(display);
        monitor.Start();
    }

    private void ConfigureMenu()
    {
        startup.Checked = StartupStatus();
        startup.Click += (_, _) => TryPreference(() =>
        {
            Preferences.SetStartup(!startup.Checked);
            startup.Checked = StartupStatus();
        });
        menu.Items.Add(startup);
        notifications.Checked = preferences.Notifications;
        notifications.Click += (_, _) => TryPreference(() =>
        {
            SavePreferences(preferences with { Notifications = !preferences.Notifications });
            monitor.Notifications = notifications.Checked = preferences.Notifications;
        });
        menu.Items.Add(notifications);
        int[] periods = [60, 120, 300, 600, 1800, 3600];
        if (!periods.Contains(preferences.Interval))
            periods = periods.Append(preferences.Interval).Order().ToArray();
        foreach (int value in periods)
        {
            var item = new ToolStripMenuItem(value % 60 == 0 ? $"{value / 60} min" : $"{value} s")
            {
                Tag = value, Checked = value == preferences.Interval
            };
            item.Click += (_, _) => TryPreference(() =>
            {
                SavePreferences(preferences with { Interval = value });
                monitor.SetInterval(value);
                foreach (ToolStripMenuItem option in intervals.DropDownItems)
                    option.Checked = (int)option.Tag! == value;
            });
            intervals.DropDownItems.Add(item);
        }
        menu.Items.Add(intervals);
        foreach (int hertz in PollingRateProtocol.Options)
        {
            var item = new ToolStripMenuItem(UiText.FormatRate(hertz)) { Tag = hertz };
            item.Click += (_, _) => monitor.RequestPollingRate(hertz);
            pollingMenu.Items.Add(item);
        }
        menu.Items.Add(polling);
        spanish.Click += (_, _) => SelectLanguage(UiText.Spanish);
        english.Click += (_, _) => SelectLanguage(UiText.English);
        languageMenu.Items.AddRange([spanish, english]);
        menu.Items.Add(languages);
        pause.Click += (_, _) =>
        {
            monitor.SetPaused(!monitor.Paused);
            pause.Checked = monitor.Paused;
            UpdatePauseText();
        };
        menu.Items.Add(pause);
        if (diagnostics.Enabled)
        {
            saveDiagnostics = new();
            saveDiagnostics.Click += (_, _) => ExportDiagnostics();
            menu.Items.Add(saveDiagnostics);
        }
        menu.Items.Add(new ToolStripSeparator());
        exit.Click += (_, _) => ExitThread();
        menu.Items.Add(exit);
        menu.Opened += (_, _) => monitor.SetInteractionVisible(true);
        menu.Closed += (_, e) =>
        {
            monitor.SetInteractionVisible(panel?.Visible == true);
            if (e.CloseReason == ToolStripDropDownCloseReason.Keyboard)
                tray.RestoreFocus();
        };
    }

    private void SelectLanguage(string language) => TryPreference(() =>
    {
        if (preferences.Language == language)
            return;

        SavePreferences(preferences with { Language = language });
        text = new(language);
        ApplyMenuText();
        RefreshDisplay();
    });

    private void ApplyMenuText()
    {
        startup.Text = text["StartWithWindows"];
        notifications.Text = text["LowBatteryNotifications"];
        intervals.Text = text["RefreshInterval"];
        polling.Text = text["PollingRate"];
        languages.Text = text["Language"];
        spanish.Checked = preferences.Language == UiText.Spanish;
        english.Checked = preferences.Language == UiText.English;
        UpdatePauseText();
        if (saveDiagnostics is not null)
            saveDiagnostics.Text = text["SaveDiagnostics"];
        exit.Text = text["Exit"];
    }

    private void UpdatePauseText() => pause.Text = text[monitor.Paused ? "ResumeMonitoring" : "PauseMonitoring"];

    private void ShowBatteryAlert(BatteryNotice? mouse, BatteryNotice? spare)
    {
        var lines = new List<string>(2);
        if (mouse is BatteryNotice mouseNotice)
            lines.Add(text.AlertLine(mouseNotice, false));
        if (spare is BatteryNotice spareNotice)
            lines.Add(text.AlertLine(spareNotice, true));
        bool critical = mouse?.Level == AlertLevel.Critical || spare?.Level == AlertLevel.Critical;
        tray.Notify(text[critical ? "BatteryCriticalTitle" : "BatteryLowTitle"],
            string.Join(Environment.NewLine, lines), critical);
    }

    private void SavePreferences(Preferences updated)
    {
        updated.Save();
        preferences = updated;
    }

    private void ApplyMenuTheme()
    {
        if (SystemInformation.HighContrast)
            return;
        var renderer = new TrayMenuRenderer(new TrayPalette(false)) { RoundedEdges = false };
        menu.Renderer = renderer;
        intervalMenu.Renderer = renderer;
        languageMenu.Renderer = renderer;
        pollingMenu.Renderer = renderer;
    }

    private bool StartupStatus()
    {
        try { return Preferences.StartupEnabled(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
        {
            diagnostics.Write($"Inicio con Windows: {error.GetType().Name}");
            return false;
        }
    }

    private void TryPreference(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
        {
            diagnostics.Write($"Preferencias: {error.GetType().Name}");
            tray.Notify(UiText.Title, text["SaveError"], true);
        }
    }

    private void ExportDiagnostics()
    {
        using var save = new SaveFileDialog
        {
            FileName = text["DiagnosticsFileName"],
            Filter = text["DiagnosticsFileFilter"],
            DefaultExt = "txt"
        };
        if (save.ShowDialog() == DialogResult.OK)
            TryPreference(() => File.WriteAllLines(save.FileName, diagnostics.Snapshot()));
    }

    private void OnTrayEvent(int code, Point point)
    {
        if (tray.Version4 && code is 0x0204 or 0x0205) // Right mouse button in a v4 tray callback.
        {
            rightClickPoint = point;
            rightClickAt = Environment.TickCount64;
        }
        else if ((tray.Version4 && code is 0x400 or 0x401) || (!tray.Version4 && code == 0x0202))
            ShowPanel();
        else if ((tray.Version4 && code == 0x007B) || (!tray.Version4 && code == 0x0205))
        {
            Point anchor = !tray.Version4 ? Cursor.Position
                : rightClickAt is long clicked && Environment.TickCount64 - clicked <= 750 ? rightClickPoint
                : tray.Anchor();
            rightClickAt = null;
            panel?.Hide();
            startup.Checked = StartupStatus();
            menu.WorkArea = intervalMenu.WorkArea = languageMenu.WorkArea = pollingMenu.WorkArea = Screen.FromPoint(anchor).WorkingArea;
            window.PrepareTrayMenu(anchor);
            menu.ShowAt(anchor);
        }
    }

    private void ShowPanel()
    {
        if (panel is null)
        {
            panel = new();
            panel.RefreshRequested += monitor.RequestUpdate;
            panel.VisibleChanged += (_, _) => monitor.SetInteractionVisible(panel.Visible || menu.Visible);
        }
        panel.Render(display, text);
        panel.Present(tray.Anchor());
    }

    private void AddTray()
    {
        tray.Add();
        shellRetry.Enabled = !tray.IsAdded;
    }

    private void Render(MonitorDisplay value)
    {
        if (display == value && tray.IsAdded)
            return;
        if (display.PollingRate != value.PollingRate)
        {
            polling.Enabled = value.PollingRate.CanChange;
            foreach (ToolStripMenuItem option in pollingMenu.Items)
                option.Checked = !value.PollingRate.Stale && !value.PollingRate.Unsupported &&
                    (int)option.Tag! == value.PollingRate.Hertz;
        }
        display = value;
        RefreshDisplay();
    }

    private void RefreshDisplay()
    {
        tray.Set(MonitorPolicy.IconBand(display.Mouse.Sample?.Percent), text.Tooltip(display));
        shellRetry.Enabled = !tray.IsAdded;
        if (panel?.Visible == true)
            panel.Render(display, text);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            monitor.Dispose();
            shellRetry.Dispose();
            panel?.Dispose();
            menu.Dispose();
            intervalMenu.Dispose();
            languageMenu.Dispose();
            pollingMenu.Dispose();
            tray.Dispose();
            window.Dispose();
        }
        base.Dispose(disposing);
    }
}
