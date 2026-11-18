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
    private readonly ToolStripMenuItem startup = new("Iniciar con Windows");
    private readonly ToolStripMenuItem notifications = new("Notificaciones de batería baja");
    private readonly ToolStripMenuItem pause = new("Pausar monitoreo");
    private readonly TraySubmenuItem intervals;
    private readonly System.Windows.Forms.Timer shellRetry = new() { Interval = 5000 };
    private BatteryPanel? panel;
    private Preferences preferences;
    private Point rightClickPoint;
    private long? rightClickAt;
    private MonitorDisplay display = new(LinkState.Detecting, default, default, false, false);

    internal TrayContext(bool diagnosticMode)
    {
        intervals = new("Intervalo de actualización", intervalMenu) { DropDown = intervalMenu };
        diagnostics = new(diagnosticMode);
        preferences = Preferences.Load(diagnostics);
        tray = new(window.Handle);
        monitor = new(window, diagnostics, preferences);
        monitor.Changed += Render;
        monitor.Alert += (critical, message) => tray.Notify(
            critical ? "Batería crítica · AM Infinity .97" : "Batería baja · AM Infinity .97", message, critical);
        window.TrayEvent += OnTrayEvent;
        window.ShellRestarted += AddTray;
        shellRetry.Tick += (_, _) => AddTray();
        ConfigureMenu();
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
        pause.Click += (_, _) =>
        {
            monitor.SetPaused(!monitor.Paused);
            pause.Checked = monitor.Paused;
            pause.Text = monitor.Paused ? "Reanudar monitoreo" : "Pausar monitoreo";
        };
        menu.Items.Add(pause);
        if (diagnostics.Enabled)
            menu.Items.Add("Guardar diagnóstico…", null, (_, _) => ExportDiagnostics());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => ExitThread());
        menu.Closed += (_, e) =>
        {
            if (e.CloseReason == ToolStripDropDownCloseReason.Keyboard)
                tray.RestoreFocus();
        };
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
            tray.Notify(DisplayText.Title, "Windows no permitió guardar el cambio.", true);
        }
    }

    private void ExportDiagnostics()
    {
        using var save = new SaveFileDialog
        {
            FileName = "AMInfinity97-diagnostico.txt",
            Filter = "Archivo de texto (*.txt)|*.txt",
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
            menu.WorkArea = intervalMenu.WorkArea = Screen.FromPoint(anchor).WorkingArea;
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
            panel.VisibleChanged += (_, _) => monitor.SetPanelVisible(panel.Visible);
        }
        panel.Render(display);
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
        display = value;
        tray.Set(MonitorPolicy.IconBand(value.Mouse.Sample?.Percent), DisplayText.Tooltip(value));
        shellRetry.Enabled = !tray.IsAdded;
        if (panel?.Visible == true)
            panel.Render(value);
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
            tray.Dispose();
            window.Dispose();
        }
        base.Dispose(disposing);
    }
}
