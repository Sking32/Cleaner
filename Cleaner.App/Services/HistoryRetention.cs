namespace Cleaner.App.Services;

public static class HistoryRetention
{
    // Keep the last 50 events plus up to 10 older cleanups needed by the chart.
    public static List<CleanupHistoryEntry> Prune(IEnumerable<CleanupHistoryEntry> source)
    {
        var ordered = source.Where(e => e != null).OrderByDescending(e => e.Time).ToArray();
        var chart = ordered.Where(e => e.IsChartEligible).Take(10).ToHashSet();
        return ordered.Where((entry, index) => index < 50 || chart.Contains(entry)).ToList();
    }
}
