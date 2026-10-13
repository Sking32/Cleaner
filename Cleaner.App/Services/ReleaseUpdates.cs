using System.Net.Http;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cleaner.App.Services;

public readonly record struct ReleaseVersion(Version Version, int Stage, int Number, int? Revision = null) : IComparable<ReleaseVersion>
{
    public static bool TryParse(string? text, out ReleaseVersion value)
    {
        value = default;
        if (text == null) return false;
        var match = Regex.Match(text, @"^v?(\d+\.\d+\.\d+)(?:-(beta|rc)\.(\d+)(?:\.(\d+))?)?$", RegexOptions.CultureInvariant);
        if (!match.Success || !System.Version.TryParse(match.Groups[1].Value, out var version)) return false;
        if (match.Groups[3].Success && !int.TryParse(match.Groups[3].Value, out _)) return false;
        if (match.Groups[4].Success && !int.TryParse(match.Groups[4].Value, out _)) return false;
        value = new(version, match.Groups[2].Value switch { "beta" => 0, "rc" => 1, _ => 2 }, match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0,
            match.Groups[4].Success ? int.Parse(match.Groups[4].Value) : null);
        return true;
    }
    public int CompareTo(ReleaseVersion other)
    {
        var version = Version.CompareTo(other.Version); if (version != 0) return version;
        var stage = Stage.CompareTo(other.Stage); if (stage != 0) return stage;
        var number = Number.CompareTo(other.Number); return number != 0 ? number : Nullable.Compare(Revision, other.Revision);
    }
}

internal static class ReleaseUpdates
{
    public const string OfficialPage = "https://github.com/Sking32/Cleaner/releases";
    public static async Task<string?> FindNewerAsync(string current, CancellationToken token = default)
    {
        if (!ReleaseVersion.TryParse(current, out var installed)) throw new InvalidDataException("Unknown installed version.");
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Cleaner/" + current);
        using var response = await client.GetAsync("https://api.github.com/repos/Sking32/Cleaner/releases?per_page=30", token);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        return document.RootElement.EnumerateArray().Where(release => !release.GetProperty("draft").GetBoolean())
            .Where(release => installed.Stage < 2 || !release.GetProperty("prerelease").GetBoolean())
            .Select(release => release.GetProperty("tag_name").GetString()).Where(tag => ReleaseVersion.TryParse(tag, out var version) && version.CompareTo(installed) > 0)
            .OrderByDescending(tag => { ReleaseVersion.TryParse(tag, out var version); return version; }).FirstOrDefault();
    }
}
