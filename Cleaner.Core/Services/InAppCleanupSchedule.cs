namespace Cleaner.Core.Services;

/// <summary>Interval while the app is open. No missed-run backlog or work on startup.</summary>
public sealed class InAppCleanupSchedule
{
    public static readonly IReadOnlyList<int> AllowedMinutes = new[] { 60, 360, 1440, 10080 };
    private int _interval;
    public DateTimeOffset? NextRun { get; private set; }
    public bool TryBegin(DateTimeOffset now, bool enabled, int minutes, bool busy)
    {
        if (!enabled || !AllowedMinutes.Contains(minutes)) { NextRun = null; _interval = 0; return false; }
        if (NextRun == null || _interval != minutes)
        { _interval = minutes; NextRun = now.AddMinutes(minutes); return false; }
        if (busy || now < NextRun.Value) return false;
        NextRun = now.AddMinutes(minutes);
        return true;
    }
}
