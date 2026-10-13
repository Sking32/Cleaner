using System.Globalization;
using System.Text.RegularExpressions;

namespace Cleaner.Core.Services;

public sealed record LogMaintenanceFile(string Path, FileIdentity Identity);
public sealed record LogMaintenanceResult(int Deleted, int Skipped, long BytesDeleted);

/// <summary>Only Cleaner daily logs from completed days, never settings, recovery data or today's open log.</summary>
public sealed class ApplicationLogMaintenance(string directory, IFileVerifier? verifier = null)
{
    private readonly string _directory = Path.GetFullPath(directory);
    private readonly IFileVerifier _verifier = verifier ?? new WindowsFileVerifier();
    private static readonly Regex LogName = new(@"^cleaner-(\d{4}-\d{2}-\d{2})\.log$", RegexOptions.CultureInvariant);
    private static bool IsOldLogName(string path)
    {
        var match = LogName.Match(Path.GetFileName(path));
        return match.Success && DateTime.TryParseExact(match.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date) && date < DateTime.Today;
    }
    public IReadOnlyList<LogMaintenanceFile> Analyze()
    {
        var files = new List<LogMaintenanceFile>();
        if (!Directory.Exists(_directory)) return files.AsReadOnly();
        foreach (var path in Directory.EnumerateFiles(_directory))
        {
            if (!IsOldLogName(path)) continue;
            try
            {
                if (File.GetLastWriteTime(path) >= DateTime.Today) continue;
                using var file = _verifier.Open(path);
                files.Add(new(path, file.Identity));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        return files.AsReadOnly();
    }
    public LogMaintenanceResult Apply(IReadOnlyList<LogMaintenanceFile> plan)
    {
        int deleted = 0, skipped = 0; long bytes = 0;
        foreach (var candidate in plan)
        {
            if (!string.Equals(Path.GetDirectoryName(candidate.Path), _directory, StringComparison.OrdinalIgnoreCase) || !IsOldLogName(candidate.Path)) { skipped++; continue; }
            try
            {
                if (File.GetLastWriteTime(candidate.Path) >= DateTime.Today) { skipped++; continue; }
                using var file = _verifier.Open(candidate.Path, true, candidate.Identity);
                file.Delete(); deleted++; bytes += candidate.Identity.Length;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { skipped++; }
        }
        return new(deleted, skipped, bytes);
    }
}
