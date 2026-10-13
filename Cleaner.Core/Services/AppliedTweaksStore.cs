using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cleaner.Core.Services;

/// <summary>
/// Хранит карту твиков, реально применённых Cleaner.
/// Отличается от state-check: state может совпасть "случайно" (пользователь применил вручную),
/// а здесь — только факты применения через Cleaner.
/// </summary>
public sealed class AppliedTweaksStore
{
    private readonly Dictionary<string, AppliedEntry> _applied = new();
    private readonly object _lock = new();

    public string FilePath { get; }

    public sealed class AppliedEntry
    {
        [JsonPropertyName("when")] public string When { get; set; } = "";
        [JsonPropertyName("applied")] public bool Applied { get; set; }
        [JsonPropertyName("requiresReboot")] public bool RequiresReboot { get; set; }
    }

    public AppliedTweaksStore(string? filePath = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir = Path.Combine(local, "Cleaner");
            Directory.CreateDirectory(dir);
            filePath = Path.Combine(dir, "opt-applied.json");
        }
        FilePath = filePath;
        Load();
    }

    public bool IsApplied(string tweakKey)
    {
        lock (_lock)
            return _applied.TryGetValue(tweakKey, out var e) && e.Applied;
    }

    public bool IsPendingReboot(string tweakKey)
    {
        lock (_lock)
            return _applied.TryGetValue(tweakKey, out var e) && e.RequiresReboot;
    }

    public void MarkApplied(string tweakKey, bool requiresReboot)
    {
        lock (_lock)
        {
            _applied[tweakKey] = new AppliedEntry
            {
                When = DateTime.UtcNow.ToString("o"),
                Applied = true,
                RequiresReboot = requiresReboot
            };
            SaveOrReload();
        }
    }

    public void MarkUnapplied(string tweakKey, bool requiresReboot = false)
    {
        lock (_lock)
        {
            _applied[tweakKey] = new AppliedEntry { Applied = false, RequiresReboot = requiresReboot, When = DateTime.UtcNow.ToString("o") };
            SaveOrReload();
        }
    }

    public void ClearRebootPending(string tweakKey)
    {
        lock (_lock)
        {
            if (_applied.TryGetValue(tweakKey, out var e))
            {
                e.RequiresReboot = false;
                SaveOrReload();
            }
        }
    }

    public void ClearAllRebootPending()
    {
        lock (_lock)
        {
            foreach (var e in _applied.Values) e.RequiresReboot = false;
            SaveOrReload();
        }
    }

    public void ClearAll()
    {
        lock (_lock)
        {
            _applied.Clear();
            SaveOrReload();
        }
    }

    public IReadOnlyList<string> GetAppliedKeys()
    {
        lock (_lock)
            return _applied.Where(kv => kv.Value.Applied).Select(kv => kv.Key).ToList();
    }

    public int PendingRebootCount
    {
        get
        {
            lock (_lock)
                return _applied.Values.Count(e => e.RequiresReboot);
        }
    }

    // ---------- Persistence ----------

    private void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var json = File.ReadAllText(FilePath);
            if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("Empty applied-tweak file.");

            var doc = JsonSerializer.Deserialize<Dictionary<string, AppliedEntry>>(json);
            if (doc == null || doc.Any(kv => string.IsNullOrWhiteSpace(kv.Key) || kv.Value == null))
                throw new InvalidDataException("Invalid applied-tweak entries.");

            lock (_lock)
            {
                _applied.Clear();
                foreach (var (k, v) in doc) _applied[k] = v;
            }
        }
        catch (Exception logEx) { throw new IOException("Cannot read applied-tweak state.", logEx); }
    }

    private void SaveOrReload()
    {
        try { Save(); }
        catch
        {
            _applied.Clear();
            Load();
            throw;
        }
    }
    private void Save()
    {
        AtomicFile.Write(FilePath, JsonSerializer.Serialize(_applied, new JsonSerializerOptions { WriteIndented = true }));
    }
}
