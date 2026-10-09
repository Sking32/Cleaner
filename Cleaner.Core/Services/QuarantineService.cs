using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

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
}

internal sealed class QuarantineManifest
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = 1;
    [JsonPropertyName("sessionId")] public string SessionId { get; set; } = "";
    [JsonPropertyName("createdUtc")] public string CreatedUtc { get; set; } = "";
    [JsonPropertyName("entries")] public List<QuarantineEntry> Entries { get; set; } = new();
}

/// <summary>Описание сессии карантина (для UI/CLI — «что лежит в карантине»).</summary>
public sealed class QuarantineSession
{
    public string Directory { get; init; } = "";
    public string Id { get; init; } = "";
    public DateTime CreatedUtc { get; init; }
    public int Count { get; init; }
    public long TotalSizeBytes { get; init; }
}

public readonly record struct QuarantineRestoreResult(
    int Restored,
    int Failed,
    IReadOnlyList<string> Errors);

public interface IQuarantineService
{
    string SessionDirectory { get; }
    int Count { get; }
    long TotalSizeBytes { get; }
    IReadOnlyList<QuarantineEntry> Entries { get; }

    QuarantineEntry? MoveToQuarantine(string path, string category);
    QuarantineRestoreResult RestoreAll();
    int ClearAll();
}

/// <summary>
/// Карантин удалённых файлов. Файлы не удаляются сразу, а переносятся
/// в %LOCALAPPDATA%\Cleaner\Quarantine\&lt;session&gt;\ и живут там N дней
/// (по умолчанию 24 ч — чистка через <see cref="ClearOldQuarantine"/>).
/// </summary>
public sealed class QuarantineService : IQuarantineService
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

    private readonly object _lock = new();
    private readonly QuarantineManifest _manifest;
    private readonly string _manifestPath;

    public string SessionDirectory { get; }

    public int Count
    {
        get { lock (_lock) return _manifest.Entries.Count; }
    }

    public long TotalSizeBytes
    {
        get { lock (_lock) return _manifest.Entries.Sum(e => e.SizeBytes); }
    }

    public IReadOnlyList<QuarantineEntry> Entries
    {
        get { lock (_lock) return _manifest.Entries.ToArray(); }
    }

    /// <summary>
    /// Если <paramref name="sessionDirectory"/> не задан — создаётся новая сессия
    /// в %LOCALAPPDATA%\Cleaner\Quarantine\&lt;id&gt;\. Если задан — используется он
    /// (для тестов и для работы с уже существующей сессией).
    /// </summary>
    public QuarantineService(string? sessionDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(sessionDirectory))
        {
            var root = GetRootDirectory();
            Directory.CreateDirectory(root);

            var id = GenerateSessionId();
            sessionDirectory = Path.Combine(root, id);
            Directory.CreateDirectory(sessionDirectory);

            _manifest = new QuarantineManifest
            {
                SessionId = id,
                CreatedUtc = DateTime.UtcNow.ToString("o"),
                Entries = new List<QuarantineEntry>()
            };
        }
        else
        {
            Directory.CreateDirectory(sessionDirectory);
            var manifestPath = Path.Combine(sessionDirectory, "manifest.json");

            if (File.Exists(manifestPath))
            {
                try
                {
                    var json = File.ReadAllText(manifestPath);
                    _manifest = JsonSerializer.Deserialize<QuarantineManifest>(json, ReadOpts)
                                ?? new QuarantineManifest();
                }
                catch
                {
                    _manifest = new QuarantineManifest();
                }
            }
            else
            {
                _manifest = new QuarantineManifest
                {
                    SessionId = Path.GetFileName(sessionDirectory),
                    CreatedUtc = DateTime.UtcNow.ToString("o"),
                    Entries = new List<QuarantineEntry>()
                };
            }
        }

        SessionDirectory = sessionDirectory;
        _manifestPath = Path.Combine(sessionDirectory, "manifest.json");

        // Свежая сессия — сразу сохраняем манифест,
        // чтобы ListSessions/ClearOldQuarantine видели её как сессию карантина.
        if (!File.Exists(_manifestPath))
            SaveManifest();
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
            var manifestPath = Path.Combine(sessionDir, "manifest.json");
            if (!File.Exists(manifestPath)) continue;

            try
            {
                var json = File.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize<QuarantineManifest>(json, ReadOpts);
                if (manifest == null) continue;

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
            catch { /* повреждённый манифест — пропускаем */ }
        }

        return list.OrderByDescending(s => s.CreatedUtc).ToList();
    }

    /// <summary>
    /// Восстановить файлы из указанной сессии. Если всё восстановлено —
    /// директория сессии удаляется. Если что-то не удалось — оставшиеся
    /// записи остаются в manifest.json.
    /// </summary>
    public static QuarantineRestoreResult RestoreFromQuarantine(string sessionDirectory)
    {
        if (string.IsNullOrWhiteSpace(sessionDirectory) || !Directory.Exists(sessionDirectory))
            return new QuarantineRestoreResult(0, 0, Array.Empty<string>());

        var manifestPath = Path.Combine(sessionDirectory, "manifest.json");
        if (!File.Exists(manifestPath))
            return new QuarantineRestoreResult(0, 0, Array.Empty<string>());

        QuarantineManifest manifest;
        try
        {
            var json = File.ReadAllText(manifestPath);
            manifest = JsonSerializer.Deserialize<QuarantineManifest>(json, ReadOpts)
                       ?? new QuarantineManifest();
        }
        catch (Exception ex)
        {
            return new QuarantineRestoreResult(0, 0, new[] { ex.Message });
        }

        int restored = 0, failed = 0;
        var errors = new List<string>();
        var remaining = new List<QuarantineEntry>();

        foreach (var entry in manifest.Entries)
        {
            var err = RestoreOne(sessionDirectory, entry);
            if (err == null) restored++;
            else { failed++; errors.Add(err); remaining.Add(entry); }
        }

        if (failed == 0)
        {
            try { File.Delete(manifestPath); } catch { }
            try { Directory.Delete(sessionDirectory, recursive: true); } catch { }
        }
        else
        {
            manifest.Entries = remaining;
            try
            {
                File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, WriteOpts));
            }
            catch { }
        }

        return new QuarantineRestoreResult(restored, failed, errors);
    }

    /// <summary>
    /// Удаляет все сессии старше <paramref name="olderThanDays"/> дней.
    /// Возвращает число удалённых сессий.
    /// </summary>
    public static int ClearOldQuarantine(int olderThanDays, string? rootDirectory = null)
    {
        if (olderThanDays < 0) olderThanDays = 0;

        var root = rootDirectory ?? GetRootDirectory();
        if (!Directory.Exists(root)) return 0;

        var cutoff = DateTime.UtcNow.AddDays(-olderThanDays);
        var cleared = 0;

        foreach (var sessionDir in Directory.GetDirectories(root))
        {
            var manifestPath = Path.Combine(sessionDir, "manifest.json");
            if (!File.Exists(manifestPath)) continue;   // не сессия карантина — не наша

            DateTime created;
            try
            {
                var json = File.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize<QuarantineManifest>(json, ReadOpts);
                created = manifest != null && DateTime.TryParse(manifest.CreatedUtc, out var dt)
                    ? dt.ToUniversalTime()
                    : Directory.GetCreationTimeUtc(sessionDir);
            }
            catch
            {
                created = Directory.GetCreationTimeUtc(sessionDir);
            }

            if (created < cutoff)
            {
                try
                {
                    Directory.Delete(sessionDir, recursive: true);
                    cleared++;
                }
                catch { }
            }
        }

        return cleared;
    }

    // ------------------------------------------------------------------
    //  INSTANCE
    // ------------------------------------------------------------------

    /// <summary>
    /// Перенести файл или папку в карантин текущей сессии.
    /// Возвращает запись или null, если путь не существует / не удалось перенести.
    /// </summary>
    public QuarantineEntry? MoveToQuarantine(string path, string category)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        string fullPath;
        try { fullPath = Path.GetFullPath(path); }
        catch { return null; }

        bool isDir;
        try { isDir = Directory.Exists(fullPath); }
        catch { return null; }

        if (!isDir && !File.Exists(fullPath)) return null;

        lock (_lock)
        {
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

            try
            {
                if (isDir) Directory.Move(fullPath, targetPath);
                else File.Move(fullPath, targetPath);
            }
            catch { return null; }

            var entry = new QuarantineEntry
            {
                OriginalPath = fullPath,
                StoredName = storedName,
                IsDirectory = isDir,
                Category = string.IsNullOrWhiteSpace(category) ? "General" : category,
                SizeBytes = size,
                When = DateTime.UtcNow.ToString("o")
            };

            _manifest.Entries.Add(entry);
            SaveManifest();
            return entry;
        }
    }

    /// <summary>Восстановить всё, что есть в текущей сессии.</summary>
    public QuarantineRestoreResult RestoreAll()
    {
        lock (_lock)
        {
            int restored = 0, failed = 0;
            var errors = new List<string>();
            var remaining = new List<QuarantineEntry>();

            foreach (var entry in _manifest.Entries)
            {
                var err = RestoreOne(SessionDirectory, entry);
                if (err == null) restored++;
                else { failed++; errors.Add(err); remaining.Add(entry); }
            }

            _manifest.Entries = remaining;

            if (failed == 0)
            {
                try { if (File.Exists(_manifestPath)) File.Delete(_manifestPath); } catch { }
                try { if (Directory.Exists(SessionDirectory)) Directory.Delete(SessionDirectory, recursive: true); } catch { }
            }
            else
            {
                SaveManifest();
            }

            return new QuarantineRestoreResult(restored, failed, errors);
        }
    }

    /// <summary>Удалить карантин текущей сессии безвозвратно.</summary>
    public int ClearAll()
    {
        lock (_lock)
        {
            var count = _manifest.Entries.Count;
            try
            {
                if (Directory.Exists(SessionDirectory))
                    Directory.Delete(SessionDirectory, recursive: true);
            }
            catch { }
            _manifest.Entries.Clear();
            return count;
        }
    }

    // ------------------------------------------------------------------
    //  INTERNALS
    // ------------------------------------------------------------------

    private void SaveManifest()
    {
        try
        {
            var json = JsonSerializer.Serialize(_manifest, WriteOpts);
            var tmp = _manifestPath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Copy(tmp, _manifestPath, overwrite: true);
            File.Delete(tmp);
        }
        catch { }
    }

    private static string? RestoreOne(string sessionDirectory, QuarantineEntry entry)
    {
        var stored = Path.Combine(sessionDirectory, entry.StoredName);
        if (!File.Exists(stored) && !Directory.Exists(stored))
            return $"stored item missing: {entry.StoredName}";

        try
        {
            var target = entry.OriginalPath;
            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

            if (File.Exists(target) || Directory.Exists(target))
                target = MakeUniquePath(target);

            if (entry.IsDirectory) Directory.Move(stored, target);
            else File.Move(stored, target);

            return null;
        }
        catch (Exception ex)
        {
            return $"{entry.StoredName}: {ex.Message}";
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