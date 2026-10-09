using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cleaner.Core.Models;

public sealed class TweakAction
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "";

    // --- Реестр ---
    [JsonPropertyName("hive")]
    public string? Hive { get; init; }

    [JsonPropertyName("key")]
    public string? Key { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("kind")]
    public string? Kind { get; init; }

    [JsonPropertyName("value")]
    public JsonElement? Value { get; init; }

    [JsonPropertyName("expected")]
    public JsonElement? Expected { get; init; }

    [JsonPropertyName("createKeyIfMissing")]
    public bool CreateKeyIfMissing { get; init; }

    [JsonPropertyName("lessOrEqualInt")]
    public int? LessOrEqualInt { get; init; }

    // --- Службы ---
    [JsonPropertyName("serviceName")]
    public string? ServiceName { get; init; }

    [JsonPropertyName("startupType")]
    public string? StartupType { get; init; }

    [JsonPropertyName("expectedStartupType")]
    public string? ExpectedStartupType { get; init; }

    [JsonPropertyName("stopFirst")]
    public bool StopFirst { get; init; }

    // --- Внешние команды ---
    [JsonPropertyName("fileName")]
    public string? FileName { get; init; }

    [JsonPropertyName("arguments")]
    public string? Arguments { get; init; }

    [JsonPropertyName("outputContains")]
    public string? OutputContains { get; init; }

    // --- Backup-aware действия ---
    [JsonPropertyName("backupKey")]
    public string? BackupKey { get; init; }

    // --- Динамическая дата ---
    [JsonPropertyName("daysFromNow")]
    public int? DaysFromNow { get; init; }

    [JsonPropertyName("format")]
    public string? Format { get; init; }
}