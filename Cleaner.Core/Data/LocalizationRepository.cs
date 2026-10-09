using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Cleaner.Core.Data;

/// <summary>
/// Загружает строки локализации из Data/localization/{lang}.json.
/// </summary>
public sealed class LocalizationRepository
{
    private readonly Dictionary<string, string> _strings;
    private readonly string _language;

    public string Language => _language;

    public LocalizationRepository(string language = "ru", string? dataDirectory = null)
    {
        _language = language;
        dataDirectory ??= Path.Combine(AppContext.BaseDirectory, "Data");

        var path = Path.Combine(dataDirectory, "localization", $"{language}.json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Localization file not found: {path}", path);

        var json = File.ReadAllText(path);
        _strings = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
            ?? new Dictionary<string, string>();
    }

    public string this[string key]
        => _strings.TryGetValue(key, out var value) ? value : $"[{key}]";

    public string Get(string key) => this[key];

    public bool Has(string key) => _strings.ContainsKey(key);
}