using Cleaner.App.Localization;
using Cleaner.App.Services;

namespace Cleaner.App.ViewModels;

public sealed record CategoryChartItem(string Key, string Name, long Bytes, long Maximum)
{
    public double Percent => Maximum > 0 ? Math.Clamp(Bytes * 100.0 / Maximum, 0, 100) : 0;
    public string SizeText => CleanupDetailRow.FormatBytes(Bytes);
}

public sealed record HistoryChartItem(CleanupHistoryEntry Entry, long Maximum)
{
    public string DateText => Entry.Time.ToLocalTime().ToString("g", System.Globalization.CultureInfo.GetCultureInfo(Loc.Language));
    public double DeletedPercent => Maximum > 0 ? Math.Clamp(Entry.BytesFreed * 100.0 / Maximum, 0, 100) : 0;
    public double QuarantinedPercent => Maximum > 0 ? Math.Clamp(Entry.BytesQuarantined * 100.0 / Maximum, 0, 100) : 0;
    public string DeletedText => Loc.T("analytics.deleted") + ": " + CleanupDetailRow.FormatBytes(Entry.BytesFreed);
    public string QuarantinedText => Loc.T("analytics.quarantined") + ": " + CleanupDetailRow.FormatBytes(Entry.BytesQuarantined);
}
