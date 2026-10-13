using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cleaner.App.Services;

public sealed class ReleaseDocument
{
    public int SchemaVersion { get; set; }
    public List<ReleaseDefinition> Releases { get; set; } = new();
}
public sealed class ReleaseDefinition
{
    public string Version { get; set; } = "";
    public string? Date { get; set; }
    public Dictionary<string, ReleaseTranslation> Text { get; set; } = new();
}
public sealed class ReleaseTranslation
{
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public List<string> Added { get; set; } = new();
    public List<string> Changed { get; set; } = new();
    public List<string> Fixed { get; set; } = new();
}
public static class ReleaseCatalog
{
    public static IReadOnlyList<ReleaseDefinition> Load(string path, string currentVersion)
    {
        var doc = JsonSerializer.Deserialize<ReleaseDocument>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Empty release catalog.");
        if (doc.SchemaVersion != 1 || doc.Releases == null || doc.Releases.Count == 0 ||
            doc.Releases.Any(r => r == null || string.IsNullOrWhiteSpace(r.Version) || !Regex.IsMatch(r.Version, @"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$") ||
                (r.Date != null && !DateOnly.TryParseExact(r.Date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _)) ||
                r.Text == null || new[] { "ru", "en" }.Any(lang => !r.Text.TryGetValue(lang, out var text) || text == null ||
                    string.IsNullOrWhiteSpace(text.Title) || string.IsNullOrWhiteSpace(text.Summary) || text.Added == null || text.Changed == null || text.Fixed == null ||
                    text.Added.Concat(text.Changed).Concat(text.Fixed).Any(string.IsNullOrWhiteSpace))) ||
            doc.Releases.Select(r => r.Version).Distinct(StringComparer.Ordinal).Count() != doc.Releases.Count ||
            !doc.Releases.Any(r => r.Version == currentVersion))
            throw new InvalidDataException("Invalid release catalog or missing current version.");
        return doc.Releases.AsReadOnly();
    }
}
