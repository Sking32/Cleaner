using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cleaner.Core.Data;

public sealed class CleanupPreset
{
    [JsonPropertyName("nameKey")] public string NameKey { get; init; } = "";
    [JsonPropertyName("shortKey")] public string ShortKey { get; init; } = "";
    [JsonPropertyName("noteKey")] public string NoteKey { get; init; } = "";
    [JsonPropertyName("keys")] public List<string> Keys { get; init; } = new();
}

public sealed class OptimizePreset
{
    [JsonPropertyName("nameKey")] public string NameKey { get; init; } = "";
    [JsonPropertyName("keys")] public List<string> Keys { get; init; } = new();
}

public sealed class ConflictDefinition
{
    [JsonPropertyName("a")] public string A { get; init; } = "";
    [JsonPropertyName("b")] public string B { get; init; } = "";
    [JsonPropertyName("messageKey")] public string MessageKey { get; init; } = "";
}

internal sealed class PresetsDocument
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; init; }
    [JsonPropertyName("cleanupPresets")] public Dictionary<string, CleanupPreset> CleanupPresets { get; init; } = new();
    [JsonPropertyName("optimizePresets")] public Dictionary<string, OptimizePreset> OptimizePresets { get; init; } = new();
    [JsonPropertyName("rebootTweaks")] public List<string> RebootTweaks { get; init; } = new();
    [JsonPropertyName("conflicts")] public List<ConflictDefinition> Conflicts { get; init; } = new();
}

public sealed class PresetsRepository
{
    public IReadOnlyDictionary<string, CleanupPreset> CleanupPresets { get; }
    public IReadOnlyDictionary<string, OptimizePreset> OptimizePresets { get; }
    public IReadOnlyList<string> RebootTweaks { get; }
    public IReadOnlyList<ConflictDefinition> Conflicts { get; }

    public PresetsRepository(string? dataDirectory = null)
    {
        dataDirectory ??= Path.Combine(AppContext.BaseDirectory, "Data");
        var path = Path.Combine(dataDirectory, "presets.json");
        if (!File.Exists(path)) throw new FileNotFoundException($"Presets file not found: {path}", path);

        var doc = JsonSerializer.Deserialize<PresetsDocument>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize {path}");

        CleanupPresets = doc.CleanupPresets;
        OptimizePresets = doc.OptimizePresets;
        RebootTweaks = doc.RebootTweaks;
        Conflicts = doc.Conflicts;
    }

    public bool IsRebootTweak(string key) => RebootTweaks.Contains(key);

    /// <summary>Возвращает список конфликтов, если оба твика в наборе.</summary>
    public IEnumerable<ConflictDefinition> FindConflicts(IEnumerable<string> selectedKeys)
    {
        var set = new HashSet<string>(selectedKeys);
        return Conflicts.Where(c => set.Contains(c.A) && set.Contains(c.B));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}