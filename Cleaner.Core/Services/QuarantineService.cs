using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace Cleaner.Core.Services;

/// <summary>Запись карантина: куда положили, откуда взяли, сколько весит.</summary>
public sealed class QuarantineEntry
{
    [JsonPropertyName("originalPath")] public string OriginalPath { get; set; } = "";
    [JsonPropertyName("storedName")] public string StoredName { get; set; } = "";
    [JsonPropertyName("isDirectory")] public bool IsDirectory { get; set; }
    [JsonPropertyName("category")] public string Category { get; set; } = "General";
    [JsonPropertyName("sizeBytes")] public long SizeBytes { get; set; }
    [JsonPropertyName("when")] public string When { get; set; } = "";
    [JsonPropertyName("pending")] public bool Pending { get; set; }
    [JsonPropertyName("restorePath")] public string? RestorePath { get; set; }
}

internal sealed class QuarantineManifest
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = 1;
    [JsonPropertyName("sessionId")] public string SessionId { get; set; } = "";
    [JsonPropertyName("createdUtc")] public string CreatedUtc { get; set; } = "";
    [JsonPropertyName("entries")] public List<QuarantineEntry> Entries { get; set; } = new();
    [JsonPropertyName("deletionInProgress")] public bool DeletionInProgress { get; set; }
    [JsonPropertyName("journalSequence")] public long JournalSequence { get; set; }
}

/// <summary>Описание сессии карантина (для UI/CLI — «что лежит в карантине»).</summary>
public sealed class QuarantineSession
{
    public string Directory { get; init; } = "";
    public string Id { get; init; } = "";
    public DateTime CreatedUtc { get; init; }
    public int Count { get; init; }
    public long TotalSizeBytes { get; init; }
    public QuarantineHealth Health { get; init; }
    public IReadOnlyList<string> Issues { get; init; } = Array.Empty<string>();
    public bool CanRestore { get; init; } = true;
    public bool CanDelete => Health is QuarantineHealth.Healthy or QuarantineHealth.DeletionInterrupted;
}

public enum QuarantineHealth { Healthy, NeedsRecovery, InvalidManifest, UnrecordedFiles, Inaccessible, LinkRejected, DeletionInterrupted }
public readonly record struct QuarantineMoveResult(QuarantineEntry? Entry, string? Reason = null, string? Detail = null);
public readonly record struct QuarantineProgress(int Completed, int Total, string Item);

public readonly record struct QuarantineRestoreResult(
    int Restored,
    int Failed,
    IReadOnlyList<string> Errors)
{
    public bool Canceled { get; init; }
}

public readonly record struct QuarantineDeleteResult(
    int Deleted,
    int Failed,
    IReadOnlyList<string> Errors)
{
    public bool Canceled { get; init; }
}

public interface IQuarantineService
{
    void Checkpoint() { }
    QuarantineMoveResult MovePlanned(Cleaner.Core.Services.Cleanup.PlannedFile file, string category)
        => new(null, "unverifiable", "Verified quarantine is unavailable.");
    string SessionDirectory { get; }
    int Count { get; }
    long TotalSizeBytes { get; }
    IReadOnlyList<QuarantineEntry> Entries { get; }

    QuarantineEntry? MoveToQuarantine(string path, string category);
    QuarantineMoveResult MoveToQuarantineDetailed(string path, string category)
    {
        var entry = MoveToQuarantine(path, category);
        return new(entry, entry == null ? "quarantine_failed" : null);
    }
    QuarantineRestoreResult RestoreAll();
    int ClearAll();
}

/// <summary>
/// Карантин удалённых файлов. Файлы не удаляются сразу, а переносятся
/// в %LOCALAPPDATA%\Cleaner\Quarantine\&lt;session&gt;\ до ручного восстановления или удаления.
/// </summary>
public sealed partial class QuarantineService : IQuarantineService
{
    private static readonly JsonSerializerOptions ReadOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions WriteOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> Gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock;
    private QuarantineManifest _manifest;
    private readonly string _manifestPath;

    public string SessionDirectory { get; }

    public int Count
    {
        get { lock (_lock) { Reload(); return _manifest.Entries.Count; } }
    }

    public long TotalSizeBytes
    {
        get { lock (_lock) { Reload(); return _manifest.Entries.Sum(e => e.SizeBytes); } }
    }

    public IReadOnlyList<QuarantineEntry> Entries
    {
        get { lock (_lock) { Reload(); return _manifest.Entries.ToArray(); } }
    }

    /// <summary>
    /// Если <paramref name="sessionDirectory"/> не задан — создаётся новая сессия
    /// в %LOCALAPPDATA%\Cleaner\Quarantine\&lt;id&gt;\. Если задан — используется он
    /// (для тестов и для работы с уже существующей сессией).
    /// </summary>
    public QuarantineService(string? sessionDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(sessionDirectory))
            sessionDirectory = Path.Combine(GetRootDirectory(), GenerateSessionId());
        SessionDirectory = Path.GetFullPath(sessionDirectory);
        _manifestPath = Path.Combine(SessionDirectory, "manifest.json");
        _lock = Gates.GetOrAdd(SessionDirectory, _ => new object());
        lock (_lock)
        {
            RejectLinks(SessionDirectory);
            Directory.CreateDirectory(SessionDirectory);
            if (File.Exists(_manifestPath)) _manifest = ReadManifest(SessionDirectory);
            else
            {
                if (Directory.GetFileSystemEntries(SessionDirectory).Length != 0)
                    throw new IOException("Unrecorded quarantine files require manual recovery.");
                _manifest = new QuarantineManifest { SessionId = Path.GetFileName(SessionDirectory), CreatedUtc = DateTime.UtcNow.ToString("o") };
                SaveManifest();
            }
        }
    }

    // ------------------------------------------------------------------
    //  STATIC
    // ------------------------------------------------------------------

    /// <summary>Корень карантина: %LOCALAPPDATA%\Cleaner\Quarantine.</summary>
    public static string GetRootDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(localAppData))
            localAppData = Path.Combine(Path.GetTempPath(), "Cleaner");
        return Path.Combine(localAppData, "Cleaner", "Quarantine");
    }

    /// <summary>Список всех сессий карантина (от свежих к старым).</summary>
    public static IReadOnlyList<QuarantineSession> ListSessions(string? rootDirectory = null)
    {
        var root = rootDirectory ?? GetRootDirectory();
        if (!Directory.Exists(root)) return Array.Empty<QuarantineSession>();

        var list = new List<QuarantineSession>();
        foreach (var sessionDir in Directory.GetDirectories(root))
        {
            if ((File.GetAttributes(sessionDir) & FileAttributes.ReparsePoint) != 0) continue;
            var manifestPath = Path.Combine(sessionDir, "manifest.json");
            if (!File.Exists(manifestPath)) continue;

            try
            {
                var manifest = ReadManifest(sessionDir);

                var created = DateTime.TryParse(manifest.CreatedUtc, out var dt)
                    ? dt.ToUniversalTime()
                    : Directory.GetCreationTimeUtc(sessionDir);

                list.Add(new QuarantineSession
                {
                    Directory = sessionDir,
                    Id = manifest.SessionId,
                    CreatedUtc = created,
                    Count = manifest.Entries.Count,
                    TotalSizeBytes = manifest.Entries.Sum(e => e.SizeBytes)
                });
            }
            catch (Exception logEx) { CoreLog.Warn("Карантин: повреждённый манифест пропущен", logEx); }
        }

        return list.OrderByDescending(s => s.CreatedUtc).ToList();
    }

    /// <summary>
    /// Восстановить файлы из указанной сессии. Если всё восстановлено —
    /// директория сессии удаляется. Если что-то не удалось — оставшиеся
    /// записи остаются в manifest.json.
    /// </summary>
    public static QuarantineRestoreResult RestoreFromQuarantine(string sessionDirectory,
        CancellationToken cancellationToken = default, IProgress<QuarantineProgress>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(sessionDirectory) || !Directory.Exists(sessionDirectory))
            return new QuarantineRestoreResult(0, 0, Array.Empty<string>());

        var manifestPath = Path.Combine(sessionDirectory, "manifest.json");
        if (!File.Exists(manifestPath))
            return new QuarantineRestoreResult(0, 0, Array.Empty<string>());

        try { return new QuarantineService(sessionDirectory).RestoreAll(cancellationToken, progress); }
        catch (Exception ex) { return new(0, 1, new[] { ex.Message }); }
    }

    /// <summary>Удаляет пустые сессии карантина (без записей). Возвращает число удалённых.</summary>
    public static int PruneEmptySessions(string? rootDirectory = null)
    {
        var removed = 0;
        foreach (var session in ListSessions(rootDirectory))
        {
            if (session.Count != 0) continue;

            try
            {
                lock (Gates.GetOrAdd(session.Directory, _ => new object()))
                {
                    var manifest = ReadManifest(session.Directory);
                    if (manifest.Entries.Count != 0 || Directory.GetFileSystemEntries(session.Directory).Any(p => Path.GetFileName(p) is not ("manifest.json" or "moves.jsonl"))) continue;
                    // Persist effective empty state before removing an append journal.
                    AtomicFile.Write(Path.Combine(session.Directory, "manifest.json"), JsonSerializer.Serialize(manifest, WriteOpts));
                    var journal = Path.Combine(session.Directory, "moves.jsonl");
                    RejectLinks(journal);
                    if (File.Exists(journal)) File.Delete(journal);
                    File.Delete(Path.Combine(session.Directory, "manifest.json"));
                    Directory.Delete(session.Directory, recursive: false);
                    removed++;
                }
            }
            catch (Exception ex)
            {
                CoreLog.Warn("Карантин: не удалось удалить пустую сессию", ex);
            }
        }
        return removed;
    }

    /// <summary>
    /// Удаляет все сессии старше <paramref name="olderThanDays"/> дней.
    /// Возвращает число удалённых сессий.
    /// </summary>
    public static int ClearOldQuarantine(int olderThanDays, string? rootDirectory = null)
    {
        var result = ClearOldQuarantineDetailed(olderThanDays, rootDirectory);
        foreach (var error in result.Errors) CoreLog.Warn("Карантин: " + error);
        return result.DeletedSessions;
    }

    // ------------------------------------------------------------------
    //  INSTANCE
    // ------------------------------------------------------------------

    /// <summary>
    /// Перенести файл или папку в карантин текущей сессии.
    /// Возвращает запись или null, если путь не существует / не удалось перенести.
    /// </summary>
    public QuarantineEntry? MoveToQuarantine(string path, string category)
        => MoveToQuarantineDetailed(path, category).Entry;

    public QuarantineMoveResult MoveToQuarantineDetailed(string path, string category)
    {
        if (string.IsNullOrWhiteSpace(path)) return new(null, "quarantine_failed", "Empty path.");

        string fullPath;
        try { fullPath = Path.GetFullPath(path); }
        catch (Exception ex) { return new(null, FileFailure.Classify(ex), ex.Message); }

        bool isDir;
        try { isDir = Directory.Exists(fullPath); }
        catch (Exception ex) { return new(null, FileFailure.Classify(ex), ex.Message); }

        if (!isDir && !File.Exists(fullPath)) return new(null, "missing");

        lock (_lock)
        {
            Reload();
            if (_manifest.DeletionInProgress) throw new IOException("Finish interrupted quarantine deletion before adding files.");
            RejectLinks(fullPath);
            if (fullPath.Equals(SessionDirectory, StringComparison.OrdinalIgnoreCase) ||
                fullPath.StartsWith(SessionDirectory.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) ||
                SessionDirectory.StartsWith(fullPath.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                throw new IOException("Cannot quarantine the session itself or its ancestors.");
            if (isDir) RejectTreeLinks(fullPath);
            var leaf = GetLeafName(fullPath);
            var index = _manifest.Entries.Count + 1;
            var storedName = MakeStoredName(index, leaf);
            var targetPath = Path.Combine(SessionDirectory, storedName);

            var suffix = 1;
            while (File.Exists(targetPath) || Directory.Exists(targetPath))
            {
                storedName = $"{index:D5}_{suffix}_{leaf}";
                targetPath = Path.Combine(SessionDirectory, storedName);
                suffix++;
            }

            long size;
            try { size = isDir ? ComputeDirectorySize(fullPath) : new FileInfo(fullPath).Length; }
            catch { size = 0; }

            var entry = new QuarantineEntry
            {
                OriginalPath = fullPath,
                StoredName = storedName,
                IsDirectory = isDir,
                Category = string.IsNullOrWhiteSpace(category) ? "General" : category,
                SizeBytes = size,
                When = DateTime.UtcNow.ToString("o"),
                Pending = true
            };

            _manifest.Entries.Add(entry);
            try { SaveManifest(); }
            catch { _manifest.Entries.Remove(entry); throw; }
            try
            {
                if (isDir) Directory.Move(fullPath, targetPath);
                else File.Move(fullPath, targetPath);
            }
            catch (Exception ex)
            {
                _manifest.Entries.Remove(entry);
                SaveManifest();
                return new(null, FileFailure.Classify(ex), ex.Message);
            }
            entry.Pending = false;
            // The durable intent already contains both paths if this final update fails.
            try { SaveManifest(); }
            catch (Exception ex) { entry.Pending = true; CoreLog.Warn("Quarantine move recorded; final journal update pending", ex); }
            return new(entry);
        }
    }

    /// <summary>Восстановить всё, что есть в текущей сессии.</summary>
    public QuarantineRestoreResult RestoreAll() => RestoreAll(default, null);

    public QuarantineRestoreResult RestoreAll(CancellationToken cancellationToken, IProgress<QuarantineProgress>? progress = null)
    {
        lock (_lock)
        {
            Reload();
            if (_manifest.DeletionInProgress) throw new IOException("This session has an interrupted permanent deletion; finish deletion first.");
            int restored = 0, failed = 0;
            var errors = new List<string>();
            var total = _manifest.Entries.Count;
            int completed = 0;

            foreach (var entry in _manifest.Entries.ToArray())
            {
                if (cancellationToken.IsCancellationRequested) break;
                progress?.Report(new(completed, total, entry.StoredName));
                try
                {
                    // Persist the exact destination before moving, allowing crash recovery.
                    if (entry.RestorePath == null)
                    {
                        entry.RestorePath = File.Exists(entry.OriginalPath) || Directory.Exists(entry.OriginalPath)
                            ? MakeUniquePath(entry.OriginalPath) : entry.OriginalPath;
                        SaveManifest();
                    }
                }
                catch (Exception ex) { failed++; errors.Add(ex.Message); break; }
                string? err;
                try { err = RestoreOne(SessionDirectory, entry, cancellationToken); }
                catch (OperationCanceledException) { break; }
                if (err == null)
                {
                    _manifest.Entries.Remove(entry);
                    try { SaveManifest(); restored++; }
                    catch (Exception ex) { _manifest.Entries.Add(entry); failed++; errors.Add(ex.Message); break; }
                }
                else { failed++; errors.Add(err); }
                progress?.Report(new(++completed, total, entry.StoredName));
            }
            if (!cancellationToken.IsCancellationRequested && failed == 0 && _manifest.Entries.Count == 0)
            {
                // Unknown files survive, even if no manifest entries remain.
                if (Directory.GetFileSystemEntries(SessionDirectory).All(p => Path.GetFileName(p) is "manifest.json" or "moves.jsonl"))
                {
                    try
                    {
                        ClearReadOnly(SessionDirectory);
                        File.Delete(_manifestPath);
                        Directory.Delete(SessionDirectory, recursive: false);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    { failed++; errors.Add(ex.Message); }
                }
            }

            return new QuarantineRestoreResult(restored, failed, errors) { Canceled = cancellationToken.IsCancellationRequested };
        }
    }

    /// <summary>Удалить карантин текущей сессии безвозвратно.</summary>
    public int ClearAll()
    {
        var result = ClearAllDetailed();
        if (result.Errors.Count != 0)
            throw new IOException(string.Join(Environment.NewLine, result.Errors));
        return result.Deleted;
    }

    /// <summary>Удаляет доступные записи, сохраняя в журнале всё, что удалить не удалось.</summary>
    public QuarantineDeleteResult ClearAllDetailed(CancellationToken cancellationToken = default,
        IProgress<QuarantineProgress>? progress = null)
    {
        lock (_lock)
        {
            Reload();
            if (cancellationToken.IsCancellationRequested)
                return new(0, 0, Array.Empty<string>()) { Canceled = true };
            if (_manifest.Entries.Any(e => e.Pending || e.RestorePath != null)) throw new IOException("Recover pending moves before deleting this session.");
            var known = _manifest.Entries.Select(e => e.StoredName).Append("manifest.json").Append("moves.jsonl").ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (Directory.GetFileSystemEntries(SessionDirectory).Any(p => !known.Contains(Path.GetFileName(p))))
                throw new IOException("Session contains unrecorded files; manual recovery required.");
            // Persist deletion intent. Missing entries after a crash can then be retried
            // without mistaking them for files lost during quarantine or restoration.
            var resumingDeletion = _manifest.DeletionInProgress;
            _manifest.DeletionInProgress = true;
            SaveManifest();
            int deleted = 0;
            var errors = new List<string>();
            var total = _manifest.Entries.Count;
            int completed = 0;
            bool checkpointFailed = false;
            foreach (var entry in _manifest.Entries.ToArray())
            {
                if (cancellationToken.IsCancellationRequested) break;
                progress?.Report(new(completed, total, entry.StoredName));
                var stored = Path.Combine(SessionDirectory, entry.StoredName);
                try
                {
                    RejectLinks(stored);
                    if (Directory.Exists(stored))
                    {
                        if (!entry.IsDirectory) throw new IOException("Stored item type differs from the manifest.");
                        RejectTreeLinks(stored, cancellationToken);
                        DeleteQuarantinedDirectory(stored, cancellationToken);
                    }
                    else if (File.Exists(stored))
                    {
                        if (entry.IsDirectory) throw new IOException("Stored item type differs from the manifest.");
                        DeleteQuarantinedFile(stored);
                    }
                    else if (!resumingDeletion) throw new IOException("Stored item missing before deletion: " + entry.StoredName);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errors.Add($"{entry.StoredName}: {ex.Message}");
                    progress?.Report(new(++completed, total, entry.StoredName));
                    continue;
                }
                _manifest.Entries.Remove(entry);
                deleted++;
                if (deleted % 64 == 0)
                {
                    try { SaveManifest(); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        errors.Add(ex.Message);
                        checkpointFailed = true;
                        break;
                    }
                }
                progress?.Report(new(++completed, total, entry.StoredName));
            }
            if (!checkpointFailed)
            {
                _manifest.DeletionInProgress = false;
                try { SaveManifest(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { _manifest.DeletionInProgress = true; checkpointFailed = true; errors.Add(ex.Message); }
            }
            if (!checkpointFailed && !cancellationToken.IsCancellationRequested && _manifest.Entries.Count == 0)
            {
                try
                {
                    RejectLinks(SessionDirectory);
                    ClearReadOnly(SessionDirectory);
                    File.Delete(_manifestPath);
                    Directory.Delete(SessionDirectory, recursive: false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errors.Add(ex.Message);
                }
            }
            return new QuarantineDeleteResult(deleted, errors.Count == 0 ? 0 : Math.Max(1, _manifest.Entries.Count), errors)
            { Canceled = cancellationToken.IsCancellationRequested };
        }
    }

    private static void ClearReadOnly(string path)
    {
        RejectLinks(path);
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReadOnly) != 0)
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
    }

    private static void DeleteQuarantinedFile(string path)
    {
        // Moves preserve source attributes, including those in older quarantine sessions.
        ClearReadOnly(path);
        File.Delete(path);
    }

    private static void DeleteQuarantinedDirectory(string root, CancellationToken cancellationToken)
    {
        // Delete bottom-up without following junctions or symbolic links.
        var pending = new Stack<(string Path, bool ChildrenVisited)>();
        pending.Push((root, false));
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (path, childrenVisited) = pending.Pop();
            RejectLinks(path);
            if (childrenVisited)
            {
                ClearReadOnly(path);
                Directory.Delete(path, recursive: false);
                continue;
            }
            pending.Push((path, true));
            foreach (var child in Directory.GetFileSystemEntries(path))
            {
                cancellationToken.ThrowIfCancellationRequested();
                RejectLinks(child);
                if (Directory.Exists(child)) pending.Push((child, false));
                else DeleteQuarantinedFile(child);
            }
        }
    }

    // ------------------------------------------------------------------
    //  INTERNALS
    // ------------------------------------------------------------------

    private void SaveManifest()
    {
        RejectLinks(_manifestPath);
        AtomicFile.Write(_manifestPath, JsonSerializer.Serialize(_manifest, WriteOpts));
        FinishCheckpoint();
    }

    private static string? RestoreOne(string sessionDirectory, QuarantineEntry entry, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateManifest(new QuarantineManifest { Entries = new() { entry } });
            RejectLinks(sessionDirectory);
            var stored = Path.Combine(sessionDirectory, entry.StoredName);
            RejectLinks(stored);
            if (!File.Exists(stored) && !Directory.Exists(stored))
                return (entry.RestorePath != null && (File.Exists(entry.RestorePath) || Directory.Exists(entry.RestorePath))) ||
                    entry.Pending && (File.Exists(entry.OriginalPath) || Directory.Exists(entry.OriginalPath))
                    ? null : $"stored item missing: {entry.StoredName}";
            var target = Path.GetFullPath(entry.RestorePath ?? entry.OriginalPath);
            RejectLinks(target);
            if (!new SafetyService(freshMinutes: 0, protectPersonalFolders: false).CheckForCleanup(target, entry.Category).IsSafe)
                return "Restoring protected system paths requires manual recovery.";
            cancellationToken.ThrowIfCancellationRequested();
            using var verified = VerifiedFile.OpenForRestore(stored, entry.IsDirectory);
            if (entry.IsDirectory) RejectTreeLinks(stored, cancellationToken);
            // Destination ancestors stay held through parent recreation and a no-overwrite
            // rename of the verified source handle. Failure keeps the recovery journal intact.
            verified.MoveForRestore(target);

            return null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return $"{entry.StoredName}: {ex.Message}";
        }
    }

    private void Reload()
    {
        RejectLinks(SessionDirectory);
        RejectLinks(_manifestPath);
        RejectLinks(JournalPath);
        if (SameCheckpoint()) return;
        if (File.Exists(_manifestPath)) { _manifest = ReadManifest(SessionDirectory); RememberCheckpoint(); }
        else
        {
            _manifest = new QuarantineManifest { SessionId = Path.GetFileName(SessionDirectory), CreatedUtc = DateTime.UtcNow.ToString("o") };
            Directory.CreateDirectory(SessionDirectory);
            if (Directory.GetFileSystemEntries(SessionDirectory).Length != 0) throw new IOException("Unrecorded quarantine files require manual recovery.");
            SaveManifest();
        }
    }

    private static QuarantineManifest ReadManifest(string directory)
    {
        RejectLinks(directory);
        RejectLinks(Path.Combine(directory, "manifest.json"));
        var manifest = JsonSerializer.Deserialize<QuarantineManifest>(File.ReadAllText(Path.Combine(directory, "manifest.json")), ReadOpts)
            ?? throw new IOException("Empty quarantine manifest.");
        ValidateManifest(manifest);
        ReplayJournal(directory, manifest);
        ValidateManifest(manifest);
        return manifest;
    }

    private static void ValidateManifest(QuarantineManifest manifest)
    {
        if (manifest.SchemaVersion != 1 || manifest.Entries == null || manifest.JournalSequence < 0) throw new IOException("Unsupported quarantine manifest.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in manifest.Entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.StoredName)
                || entry.StoredName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || entry.StoredName is "." or ".." || entry.StoredName.EndsWith('.') || entry.StoredName.EndsWith(' ')
                || entry.StoredName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)
                || entry.StoredName.Equals("moves.jsonl", StringComparison.OrdinalIgnoreCase)
                || !names.Add(entry.StoredName) || !Path.IsPathFullyQualified(entry.OriginalPath)
                || Path.GetPathRoot(entry.OriginalPath) == entry.OriginalPath || entry.SizeBytes < 0)
                throw new IOException("Invalid quarantine entry; recovery data preserved.");
            if (entry.RestorePath != null && !entry.RestorePath.Equals(entry.OriginalPath, StringComparison.OrdinalIgnoreCase)
                && (!entry.RestorePath.StartsWith(entry.OriginalPath + ".restored", StringComparison.OrdinalIgnoreCase)
                    || !entry.RestorePath[(entry.OriginalPath.Length + ".restored".Length)..].All(char.IsAsciiDigit)))
                throw new IOException("Invalid recovery destination.");
        }
    }

    private static void RejectLinks(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Quarantine paths must not contain links: " + current);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static void RejectTreeLinks(string root, CancellationToken cancellationToken = default)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var path in Directory.GetFileSystemEntries(pending.Pop()))
            {
                cancellationToken.ThrowIfCancellationRequested();
                RejectLinks(path);
                if (Directory.Exists(path)) pending.Push(path);
            }
        }
    }

    private static string MakeUniquePath(string path)
    {
        var candidate = path + ".restored";
        var counter = 1;
        while (File.Exists(candidate) || Directory.Exists(candidate))
        {
            candidate = $"{path}.restored{counter++}";
        }
        return candidate;
    }

    private static string GenerateSessionId()
    {
        var now = DateTime.UtcNow;
        var rand = Guid.NewGuid().ToString("N").Substring(0, 8);
        return $"{now:yyyyMMdd-HHmmss}-{rand}";
    }

    private static string GetLeafName(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? "item" : name;
    }

    private static string MakeStoredName(int index, string leaf)
        => $"{index:D5}_{leaf}";

    private static long ComputeDirectorySize(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Sum(f =>
                {
                    try { return new FileInfo(f).Length; }
                    catch { return 0L; }
                });
        }
        catch { return 0L; }
    }
}
