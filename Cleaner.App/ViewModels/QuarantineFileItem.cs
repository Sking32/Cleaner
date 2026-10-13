using Cleaner.Core.Services;
using Cleaner.App.Localization;
using System.IO;

namespace Cleaner.App.ViewModels;

public sealed class QuarantineFileItem(QuarantineEntry entry)
{
    public string Name => Path.GetFileName(entry.OriginalPath);
    public string OriginalPath => entry.OriginalPath;
    public string Category => entry.Category;
    public string SizeText => CleanupDetailRow.FormatBytes(entry.SizeBytes);
    public bool Pending => entry.Pending || entry.RestorePath != null;
    public string StateText => Loc.T(Pending ? "recovery.files.pending" : "recovery.files.stored");
    public bool Matches(string search, bool pendingOnly) => (!pendingOnly || Pending) &&
        (Name + " " + OriginalPath + " " + Category).Contains(search.Trim(), StringComparison.OrdinalIgnoreCase);
}
