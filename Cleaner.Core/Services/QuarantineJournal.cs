using System.Text.Json;
using Cleaner.Core.Services.Cleanup;

namespace Cleaner.Core.Services;

public sealed partial class QuarantineService
{
    private string JournalPath => Path.Combine(SessionDirectory, "moves.jsonl");
    private sealed record MoveRecord(long Sequence, string Action, QuarantineEntry Entry);
    private long _cachedLength = -1;
    private DateTime _cachedWrite;
    private long _cachedJournalLength = -1;
    private DateTime _cachedJournalWrite;

    private bool SameCheckpoint()
    {
        var info = new FileInfo(_manifestPath);
        var journal = new FileInfo(JournalPath);
        return info.Exists && info.Length == _cachedLength && info.LastWriteTimeUtc == _cachedWrite
            && (journal.Exists ? journal.Length : 0) == _cachedJournalLength && (journal.Exists ? journal.LastWriteTimeUtc : default) == _cachedJournalWrite;
    }
    private void RememberCheckpoint()
    {
        var info = new FileInfo(_manifestPath); _cachedLength = info.Exists ? info.Length : -1;
        _cachedWrite = info.Exists ? info.LastWriteTimeUtc : default;
        var journal = new FileInfo(JournalPath); _cachedJournalLength = journal.Exists ? journal.Length : 0;
        _cachedJournalWrite = journal.Exists ? journal.LastWriteTimeUtc : default;
    }
    public void Checkpoint() { lock (_lock) { Reload(); SaveManifest(); } }
    private void FinishCheckpoint()
    {
        // The manifest has the last sequence, so an old journal can safely be replayed or removed.
        RejectLinks(JournalPath);
        if (File.Exists(JournalPath)) File.Delete(JournalPath);
        RememberCheckpoint();
    }
    private void AppendMove(string action, QuarantineEntry entry)
    {
        RejectLinks(JournalPath);
        var sequence = checked(_manifest.JournalSequence + 1);
        var bytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new MoveRecord(sequence, action, entry)) + "\n");
        using (var stream = new FileStream(JournalPath, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough))
        { stream.Write(bytes); stream.Flush(true); }
        _manifest.JournalSequence = sequence;
        RememberCheckpoint();
    }
    private static void ReplayJournal(string directory, QuarantineManifest manifest)
    {
        var path = Path.Combine(directory, "moves.jsonl");
        RejectLinks(path);
        if (!File.Exists(path)) return;
        var entries = manifest.Entries.ToDictionary(e => e.StoredName, StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(path))
        {
            // A torn record is preserved and blocks new mutations; never discard recovery evidence.
            var record = JsonSerializer.Deserialize<MoveRecord>(line) ?? throw new IOException("Invalid quarantine move journal.");
            if (record.Sequence <= manifest.JournalSequence) continue;
            if (record.Sequence != manifest.JournalSequence + 1 || record.Entry == null || record.Action is not ("upsert" or "remove"))
                throw new IOException("Invalid quarantine move journal sequence.");
            ValidateManifest(new QuarantineManifest { Entries = new() { record.Entry } });
            if (record.Action == "remove") entries.Remove(record.Entry.StoredName);
            else entries[record.Entry.StoredName] = record.Entry;
            manifest.JournalSequence = record.Sequence;
        }
        manifest.Entries = entries.Values.ToList();
    }

    public QuarantineMoveResult MovePlanned(PlannedFile file, string category)
    {
        lock (_lock)
        {
            Reload();
            if (_manifest.DeletionInProgress) throw new IOException("Finish interrupted quarantine deletion first.");
            var fullPath = Path.GetFullPath(file.Path);
            if (fullPath.Equals(SessionDirectory, StringComparison.OrdinalIgnoreCase) ||
                fullPath.StartsWith(SessionDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Cannot quarantine its own session.");
            try
            {
                using var handle = new VerifiedFile(fullPath, true, file.Identity);
                var storedName = Guid.NewGuid().ToString("N") + "_" + Path.GetFileName(fullPath);
                // Keep long source names valid in the quarantine directory.
                if (storedName.Length > 200) storedName = storedName[..200];
                var entry = new QuarantineEntry { OriginalPath = fullPath, StoredName = storedName, Category = category,
                    SizeBytes = file.Identity.Length, When = DateTime.UtcNow.ToString("o"), Pending = true };
                AppendMove("upsert", entry);
                _manifest.Entries.Add(entry);
                try { handle.Move(Path.Combine(SessionDirectory, storedName)); }
                catch
                {
                    AppendMove("remove", entry); _manifest.Entries.Remove(entry); throw;
                }
                entry.Pending = false;
                try { AppendMove("upsert", entry); }
                catch (Exception ex) { entry.Pending = true; CoreLog.Warn("Quarantine move recorded; final journal update pending", ex); }
                return new(entry);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { return new(null, FileFailure.Classify(ex), ex.Message); }
        }
    }
}
