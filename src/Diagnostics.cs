namespace AMInfinity97Tray;

internal sealed class Diagnostics(bool enabled)
{
    private readonly Queue<string>? lines = enabled ? new(256) : null;
    internal bool Enabled => lines is not null;

    internal void Write(string message)
    {
        if (lines is null)
            return;
        lock (lines)
        {
            if (lines.Count == 256)
                lines.Dequeue();
            lines.Enqueue($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}  {message}");
        }
    }

    internal string[] Snapshot()
    {
        if (lines is null)
            return [];
        lock (lines)
            return lines.ToArray();
    }
}
