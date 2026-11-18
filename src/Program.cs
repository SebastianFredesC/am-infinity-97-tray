namespace AMInfinity97Tray;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Mutex instance;
        try
        {
            instance = new Mutex(false, @"Global\AMInfinity97BatteryTray.EAA13EB2", out bool first);
            if (!first)
            {
                instance.Dispose();
                return;
            }
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        using (instance)
        {
            ApplicationConfiguration.Initialize();
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            using var tray = new TrayContext(args.Contains("--diagnostico", StringComparer.OrdinalIgnoreCase));
            Application.Run(tray);
        }
    }
}
