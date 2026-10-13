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

        if (doc.SchemaVersion != 1 || doc.Tweaks == null ||
            doc.Tweaks.Any(t => t == null || string.IsNullOrWhiteSpace(t.Key) || t.Apply == null || t.Unapply == null || t.State == null ||
                t.Apply.Concat(t.Unapply).Concat(t.State).Any(a => a == null || string.IsNullOrWhiteSpace(a.Type))) ||
            doc.Tweaks.GroupBy(t => t.Key, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            throw new InvalidDataException("Invalid tweak definitions or unsupported schema: " + _path);
        Tweaks = doc.Tweaks.AsReadOnly();
        foreach (var tweak in Tweaks)
            if (tweak.Compatibility is { } c && (c.MinWindowsBuild is <= 0 || c.MaxWindowsBuild is <= 0 ||
                c.MinWindowsBuild.HasValue && c.MaxWindowsBuild.HasValue && c.MinWindowsBuild > c.MaxWindowsBuild ||
                c.VerifiedWindowsBuilds == null || c.VerifiedWindowsBuilds.Any(b => b <= 0 ||
                    c.MinWindowsBuild.HasValue && b < c.MinWindowsBuild || c.MaxWindowsBuild.HasValue && b > c.MaxWindowsBuild)))
                throw new InvalidDataException("Invalid compatibility constraints: " + tweak.Key);
    }

    public TweakDefinition? GetByKey(string key)
        => Tweaks.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));

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
