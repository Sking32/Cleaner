using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cleaner.Core.Data;

public enum EntryKind { Section, Operation }

public sealed class OperationEntry
{
    [JsonPropertyName("kind")] public string Kind { get; init; } = "operation";
    [JsonPropertyName("key")] public string Key { get; init; } = "";
    [JsonPropertyName("category")] public string? Category { get; init; }
    [JsonPropertyName("level")] public string Level { get; init; } = "safe";
    [JsonPropertyName("defaultChecked")] public bool DefaultChecked { get; init; }
    [JsonPropertyName("hint")] public string Hint { get; init; } = "";
    [JsonPropertyName("handler")] public string? Handler { get; init; }
    [JsonPropertyName("paths")] public List<string> Paths { get; init; } = new();
    [JsonPropertyName("skipSubdirs")] public List<string> SkipSubdirs { get; init; } = new();
    [JsonPropertyName("command")] public string? Command { get; init; }
    [JsonPropertyName("registryKey")] public string? RegistryKey { get; init; }
    [JsonPropertyName("olderThanDays")] public int? OlderThanDays { get; init; }
    [JsonPropertyName("nameKey")] public string NameKey { get; init; } = "";
    [JsonPropertyName("descKey")] public string DescKey { get; init; } = "";

    [JsonIgnore] public bool IsSection => Kind == "section";
    [JsonIgnore] public bool IsOperation => Kind == "operation";
}

public sealed class OperationsRepository
{
    public IReadOnlyList<OperationEntry> Entries { get; }

    public OperationsRepository(string? dataDirectory = null)
    {
        dataDirectory ??= Path.Combine(AppContext.BaseDirectory, "Data");
        var path = Path.Combine(dataDirectory, "operations.json");
        if (!File.Exists(path)) throw new FileNotFoundException($"Operations file not found: {path}", path);

        var doc = JsonSerializer.Deserialize<OperationsDocument>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize {path}");
        Entries = doc.Entries;
    }

    public IEnumerable<OperationEntry> Sections => Entries.Where(e => e.IsSection);
    public IEnumerable<OperationEntry> Operations => Entries.Where(e => e.IsOperation);

    public OperationEntry? GetByKey(string key)
        => Entries.FirstOrDefault(e => e.Key == key);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}

internal sealed class OperationsDocument
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; init; }
    [JsonPropertyName("entries")] public List<OperationEntry> Entries { get; init; } = new();
}