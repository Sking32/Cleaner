using System.Text.Json;

namespace Cleaner.Core.Services;

public sealed partial class QuarantineService
{
    /// <summary>Read-only inspection. Never creates journals or changes recovery data.</summary>
    public static IReadOnlyList<QuarantineSession> InspectSessions(string? rootDirectory = null,
        bool verifyContents = false, CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(rootDirectory ?? GetRootDirectory());
        if (!Directory.Exists(root)) return Array.Empty<QuarantineSession>();
        var sessions = new List<QuarantineSession>();
        string[] directories;
        try { RejectLinks(root); directories = Directory.GetDirectories(root); }
        catch (IOException ex) { return new[] { InspectionFailure(root, ex) }; }
        catch (UnauthorizedAccessException ex) { return new[] { InspectionFailure(root, ex) }; }
        foreach (var directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (Gates.GetOrAdd(Path.GetFullPath(directory), _ => new object()))
            {
                try { sessions.Add(InspectSession(directory, verifyContents, cancellationToken)); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                { sessions.Add(InspectionFailure(directory, ex)); }
            }
        }
        return sessions.OrderByDescending(s => s.CreatedUtc).ToArray();
    }

    private static QuarantineSession InspectionFailure(string directory, Exception ex) => new()
    {
        Directory = directory, Id = Path.GetFileName(directory), CanRestore = false,
        Health = ex is JsonException or InvalidDataException ? QuarantineHealth.InvalidManifest : QuarantineHealth.Inaccessible,
        Issues = new[] { ex.Message }
    };

    private static QuarantineSession InspectSession(string directory, bool verifyContents, CancellationToken token)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            return new() { Directory = directory, Id = Path.GetFileName(directory), Health = QuarantineHealth.LinkRejected,
                CanRestore = false, Issues = new[] { "Linked session directory; inspection skipped." } };
        RejectLinks(directory);
        var created = Directory.GetCreationTimeUtc(directory);
        var manifestPath = Path.Combine(directory, "manifest.json");
        if (!File.Exists(manifestPath))
            return new() { Directory = directory, Id = Path.GetFileName(directory), CreatedUtc = created,
                Health = QuarantineHealth.UnrecordedFiles, CanRestore = false,
                Issues = new[] { "Missing manifest; manual recovery required." } };
        QuarantineManifest manifest;
        try { manifest = ReadManifest(directory); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new() { Directory = directory, Id = Path.GetFileName(directory), CreatedUtc = created,
                Health = QuarantineHealth.InvalidManifest, CanRestore = false, Issues = new[] { ex.Message } };
        }
        var health = QuarantineHealth.Healthy;
        var issues = new List<string>();
        void Issue(string message) { if (issues.Count < 100) issues.Add(message); }
        var known = manifest.Entries.Select(e => e.StoredName).Append("manifest.json").ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var unknown in Directory.GetFileSystemEntries(directory).Where(p => !known.Contains(Path.GetFileName(p))))
        {
            health = QuarantineHealth.UnrecordedFiles;
            Issue("Unrecorded item: " + Path.GetFileName(unknown));
        }
        foreach (var entry in manifest.Entries)
        {
            token.ThrowIfCancellationRequested();
            if (entry.Pending || entry.RestorePath != null)
            {
                if (health == QuarantineHealth.Healthy) health = QuarantineHealth.NeedsRecovery;
                Issue("Interrupted operation: " + entry.StoredName);
            }
            var stored = Path.Combine(directory, entry.StoredName);
            try
            {
                RejectLinks(stored);
                var attributes = File.GetAttributes(stored);
                if (((attributes & FileAttributes.Directory) != 0) != entry.IsDirectory)
                    throw new IOException("Stored item type differs from the manifest.");
                if (verifyContents && entry.IsDirectory) RejectTreeLinks(stored, token);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (health == QuarantineHealth.Healthy) health = QuarantineHealth.NeedsRecovery;
                Issue(entry.StoredName + ": " + ex.Message);
            }
        }
        return new()
        {
            Directory = directory, Id = string.IsNullOrEmpty(manifest.SessionId) ? Path.GetFileName(directory) : manifest.SessionId,
            CreatedUtc = DateTime.TryParse(manifest.CreatedUtc, out var date) ? date.ToUniversalTime() : created,
            Count = manifest.Entries.Count, TotalSizeBytes = manifest.Entries.Sum(e => e.SizeBytes),
            Health = health, Issues = issues
        };
    }
}
