using System.Security;
using System.Text.Json;
using Microsoft.Win32;

namespace AMInfinity97Tray;

internal sealed record Preferences(int Interval = 60, bool Notifications = true,
    int StabilizationMs = (int)MonitorPolicy.StabilizationMs,
    int ReconnectStartSeconds = 5, int ReconnectMaxSeconds = 120,
    int BusyStartSeconds = 120, int BusyMaxSeconds = 900,
    int LowPercent = MonitorPolicy.LowPercent, int CriticalPercent = MonitorPolicy.CriticalPercent)
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "AMInfinity97Tray";
    private static readonly string Folder = Path.Join(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), RunName);
    private static readonly string FilePath = Path.Join(Folder, "preferences.json");

    internal static Preferences Load(Diagnostics diagnostics)
    {
        try
        {
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length <= 4096)
            {
                var preferences = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FilePath));
                if (preferences is not null)
                    return preferences.Validate();
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            diagnostics.Write($"Preferencias: {error.GetType().Name}");
        }
        return new();
    }

    private Preferences Validate()
    {
        int reconnectStart = Math.Clamp(ReconnectStartSeconds, 5, 120);
        int busyStart = Math.Clamp(BusyStartSeconds, 60, 900);
        int low = Math.Clamp(LowPercent, 1, 95);
        return this with
        {
            Interval = Math.Clamp(Interval, MonitorPolicy.MinimumInterval, MonitorPolicy.MaximumInterval),
            StabilizationMs = Math.Clamp(StabilizationMs, 200, 1000),
            ReconnectStartSeconds = reconnectStart,
            ReconnectMaxSeconds = Math.Clamp(ReconnectMaxSeconds, reconnectStart, 900),
            BusyStartSeconds = busyStart,
            BusyMaxSeconds = Math.Clamp(BusyMaxSeconds, busyStart, 3600),
            LowPercent = low,
            CriticalPercent = Math.Clamp(CriticalPercent, 0, low - 1)
        };
    }

    internal void Save()
    {
        Directory.CreateDirectory(Folder);
        string temp = FilePath + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(this));
            File.Move(temp, FilePath, overwrite: true);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    internal static bool StartupEnabled()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunPath);
        return run?.GetValue(RunName) is string { Length: > 0 };
    }

    internal static void SetStartup(bool enabled)
    {
        using var run = Registry.CurrentUser.CreateSubKey(RunPath)
            ?? throw new SecurityException("No se pudo acceder al inicio de sesión.");
        if (enabled)
            run.SetValue(RunName, $"\"{Application.ExecutablePath}\"", RegistryValueKind.String);
        else
            run.DeleteValue(RunName, throwOnMissingValue: false);
    }
}
