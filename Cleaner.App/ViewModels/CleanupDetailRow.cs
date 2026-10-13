using Cleaner.App.Localization;
using Cleaner.Core.Data;
using Cleaner.Core.Services.Cleanup;
using System.Globalization;

namespace Cleaner.App.ViewModels;

public sealed class CleanupDetailRow
{
    public OperationOutcome Outcome { get; }
    public string Name { get; }
    public string MethodText { get; }
    public string SizeText { get; }
    public string StatusText { get; }
    public string DetailsText { get; }
    public string CategoryText { get; }
    public CleanupDetailRow(OperationOutcome outcome, OperationsRepository? repository, bool preview, CleanupSessionResult? session = null)
    {
        Outcome = outcome;
        var op = repository?.GetByKey(outcome.Key);
        CategoryText = op?.Category != null ? Loc.T("section.sec." + op.Category + ".name") : "";
        Name = op != null ? Loc.T(op.NameKey) : Loc.T("op." + outcome.Key + ".name");
        if (Name.StartsWith("op.", StringComparison.Ordinal)) Name = outcome.Key;
        var handler = !string.IsNullOrEmpty(outcome.Handler) ? outcome.Handler : op?.Handler ?? "";
        MethodText = Loc.T(handler.Length == 0 ? "preview.method.unknown" : IsQuarantined(handler) ? "preview.method.quarantine"
            : handler is "FontCache" or "WindowsUpdateCache" ? "preview.method.mixed" : "preview.method.system");
        SizeText = preview && outcome.PreviewBytes == 0 && !IsQuarantined(handler) ? Loc.T("preview.unknown.size") : FormatBytes(preview ? outcome.PreviewBytes : outcome.BytesQuarantined + outcome.BytesFreed);
        StatusText = Loc.T(outcome.Canceled ? "history.kind.canceled" : !outcome.Success ? "history.kind.failed" : preview ? "history.kind.preview" : "history.kind.applied");
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(outcome.Message)) lines.Add(outcome.Message);
        lines.AddRange(outcome.Errors ?? Array.Empty<string>());
        if (session != null)
        {
            if (session.PreviewFileCounts?.TryGetValue(outcome.Key, out var count) == true) lines.Add(Loc.F("preview.file.count", count));
            lines.AddRange((session.PreviewFiles ?? Array.Empty<CleanupPreviewFile>()).Where(f => f != null && f.OperationKey == outcome.Key).Select(f => f.Path + " · " + FormatBytes(f.Size)));
            lines.AddRange((session.SkipDetails ?? Array.Empty<CleanupSkip>()).Where(f => f.OperationKey == outcome.Key).Select(f => Loc.T("preview.skip." + f.Reason) + ": " + f.Path));
        }
        DetailsText = lines.Count > 0 ? string.Join(Environment.NewLine, lines) : Loc.T("preview.no.details");
    }
    public static bool IsQuarantined(string handler) => handler is "PathCleanup" or "PathCleanupRecursive" or "MultiPathCleanup" or "BrowserProfiles" or "FirefoxProfiles" or "SmartDownloads" or "ObsLogs" or "JumpLists" or "ActivityHistory" or "ThumbnailCache";
    public static string FormatBytes(long bytes)
    {
        double value = Math.Max(0, bytes); var units = new[] { "B", "KB", "MB", "GB", "TB" }; var index = 0;
        while (value >= 1024 && index < units.Length - 1) { value /= 1024; index++; }
        return value.ToString("0.##", CultureInfo.InvariantCulture) + " " + units[index];
    }
}
