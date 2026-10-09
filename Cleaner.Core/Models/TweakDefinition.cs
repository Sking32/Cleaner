using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Cleaner.Core.Models;

public sealed class TweakDefinition
{
    [JsonPropertyName("key")]
    public string Key { get; init; } = "";

    [JsonPropertyName("category")]
    public string Category { get; init; } = "";

    [JsonPropertyName("level")]
    public string Level { get; init; } = "safe";

    [JsonPropertyName("requiresReboot")]
    public bool RequiresReboot { get; init; }

    [JsonPropertyName("conflictsWith")]
    public List<string> ConflictsWith { get; init; } = new();

    [JsonPropertyName("nameKey")]
    public string NameKey { get; init; } = "";

    [JsonPropertyName("shortKey")]
    public string ShortKey { get; init; } = "";

    [JsonPropertyName("descKey")]
    public string DescKey { get; init; } = "";

    [JsonPropertyName("warnKey")]
    public string WarnKey { get; init; } = "";

    [JsonPropertyName("apply")]
    public List<TweakAction> Apply { get; init; } = new();

    [JsonPropertyName("unapply")]
    public List<TweakAction> Unapply { get; init; } = new();

    [JsonPropertyName("state")]
    public List<TweakAction> State { get; init; } = new();
}