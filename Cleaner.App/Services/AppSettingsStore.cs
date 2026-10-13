using System.IO;
using System.Text.Json;
using Cleaner.Core.Services;

namespace Cleaner.App.Services;

public sealed class AppSettingsData
{
    public string? Language { get; set; }
    public string Accent { get; set; } = "blue";
    public string StartPage { get; set; } = "cleanup";
    public bool Animations { get; set; } = true;
    public bool ShowMetrics { get; set; } = true;
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
            if (_data.Accent is not ("blue" or "teal" or "violet")) _data.Accent = "blue";
            if (_data.StartPage is not ("cleanup" or "optimize" or "startup" or "system" or "privacy" or "disk")) _data.StartPage = "cleanup";
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
}
