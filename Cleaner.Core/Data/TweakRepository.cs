using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cleaner.Core.Models;

namespace Cleaner.Core.Data;

/// <summary>
/// Загружает определения твиков из Data/tweaks.json.
/// Путь по умолчанию: AppContext.BaseDirectory/Data/tweaks.json
/// </summary>
public sealed class TweakRepository
{
    private readonly string _path;

    public IReadOnlyList<TweakDefinition> Tweaks { get; }

    public TweakRepository(string? dataDirectory = null)
    {
        dataDirectory ??= Path.Combine(AppContext.BaseDirectory, "Data");
        _path = Path.Combine(dataDirectory, "tweaks.json");

        if (!File.Exists(_path))
            throw new FileNotFoundException($"Tweaks file not found: {_path}", _path);

        var json = File.ReadAllText(_path);
        var doc = JsonSerializer.Deserialize<TweakDocument>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize {_path}");

        Tweaks = doc.Tweaks;
    }

    public TweakDefinition? GetByKey(string key)
        => Tweaks.FirstOrDefault(t => t.Key == key);

    public IReadOnlyList<TweakDefinition> GetByCategory(string category)
        => Tweaks.Where(t => t.Category == category).ToList();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}

internal sealed class TweakDocument
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("tweaks")]
    public List<TweakDefinition> Tweaks { get; init; } = new();
}