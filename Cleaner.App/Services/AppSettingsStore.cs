using System.IO;
using System.Text.Json;
using Cleaner.Core.Services;

namespace Cleaner.App.Services;

public sealed class AppSettingsData
{
    public string? Language { get; set; }
    public int SafetyNoticeVersion { get; set; }
    public string Accent { get; set; } = "blue";
    public string StartPage { get; set; } = "cleanup";
    public bool Animations { get; set; } = true;
    public bool ShowMetrics { get; set; } = true;
    public string Density { get; set; } = "compact";
    public WindowPlacement? Window { get; set; }
    public List<UserCleanupPreset> CleanupPresets { get; set; } = new();
    public List<string> DismissedHints { get; set; } = new();
    public List<CleanupHistoryEntry> History { get; set; } = new();
}

/// <summary>Stores only Cleaner preferences; the path is explicit so tests never use a real profile.</summary>
public sealed class AppSettingsStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private AppSettingsData? _data;
    private bool _canSave = true;
    public string? LastError { get; private set; }

    public AppSettingsStore(string path) => _path = Path.GetFullPath(path);

    public AppSettingsData Load()
    {
        lock (_gate)
        {
            if (_data != null) return _data;
            try
            {
                if (File.Exists(_path))
                    _data = JsonSerializer.Deserialize<AppSettingsData>(File.ReadAllText(_path))
                        ?? throw new InvalidDataException("Empty settings document.");
            }
            catch
            {
                _canSave = false;
                LastError = "settings.unreadable";
            }
            _data ??= new AppSettingsData();
            _data.DismissedHints ??= new();
            _data.History ??= new();
            _data.History = _data.History.Where(entry => entry != null).Take(50).ToList();
            foreach (var entry in _data.History)
            {
                entry.Outcomes = (entry.Outcomes ?? new()).Where(o => o != null).ToList();
                if (entry.Report != null && entry.Report.Result == null) entry.Report = null;
                if (string.IsNullOrWhiteSpace(entry.Id)) entry.Id = Guid.NewGuid().ToString("N");
            }
            _data.CleanupPresets ??= new();
            _data.CleanupPresets = _data.CleanupPresets.Where(p => p != null && !string.IsNullOrWhiteSpace(p.Id)
                && !string.IsNullOrWhiteSpace(p.Name) && p.Keys != null).Take(30).ToList();
            if (_data.Density is not ("compact" or "comfortable")) _data.Density = "compact";
            if (_data.Accent is not ("blue" or "teal" or "violet")) _data.Accent = "blue";
            if (_data.StartPage is not ("cleanup" or "optimize" or "startup" or "system" or "privacy" or "disk" or "history" or "whatsnew")) _data.StartPage = "cleanup";
            return _data;
        }
    }

    public bool Save()
    {
        lock (_gate)
        {
            var data = Load();
            if (!_canSave) return false; // Keep the unreadable document available for manual recovery.
            try
            {
                AtomicFile.Write(_path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
                LastError = null;
                return true;
            }
            catch (Exception ex) { LastError = ex.Message; return false; }
        }
    }

    public UserCleanupPreset UpsertCleanupPreset(string name, IEnumerable<string> keys, IEnumerable<string> allowedKeys)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 60) throw new ArgumentException("presets.invalid.name");
        var allowed = allowedKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selection = keys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (selection.Count == 0 || selection.Any(key => !allowed.Contains(key))) throw new ArgumentException("presets.invalid.selection");
        lock (_gate)
        {
            var list = Load().CleanupPresets;
            var existing = list.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existing == null && list.Count >= 30) throw new InvalidOperationException("presets.limit");
            var preset = new UserCleanupPreset { Id = existing?.Id ?? Guid.NewGuid().ToString("N"), Name = name, Keys = selection };
            var previous = list.ToList();
            if (existing != null) list.Remove(existing);
            list.Add(preset);
            if (!Save()) { Load().CleanupPresets = previous; throw new IOException(LastError); }
            return preset;
        }
    }

    public bool DeleteCleanupPreset(string id)
    {
        lock (_gate)
        {
            var list = Load().CleanupPresets;
            var previous = list.ToList();
            if (list.RemoveAll(p => p.Id == id) == 0) return false;
            if (!Save()) { Load().CleanupPresets = previous; throw new IOException(LastError); }
            return true;
        }
    }
}

public sealed class UserCleanupPreset
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Keys { get; set; } = new();
}

public sealed class WindowPlacement
{
    public double Width { get; set; }
    public double Height { get; set; }
    public double? Left { get; set; }
    public double? Top { get; set; }
    public bool Maximized { get; set; }
}
