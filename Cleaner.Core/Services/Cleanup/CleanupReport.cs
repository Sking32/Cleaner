using System.Text.Json;

namespace Cleaner.Core.Services.Cleanup;

/// <summary>Local diagnostic export. Contains no machine name, registry snapshots or file contents.</summary>
public sealed class CleanupReport
{
    public int SchemaVersion { get; init; } = 1;
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
    public string RuntimeVersion { get; init; } = Environment.Version.ToString();
    public string WindowsVersion { get; init; } = Environment.OSVersion.Version.ToString();
    public bool DryRun { get; init; }
    public CleanupFileMode FileMode { get; init; }
    public string? AppVersion { get; init; }
    public string? QuarantineDirectory { get; init; }
    public CleanupSessionResult Result { get; init; } = new();

    /// <summary>Errors and skipped-item details may contain private paths; export only on request.</summary>
    public void Save(string path) => AtomicFile.Write(path, JsonSerializer.Serialize(this,
        new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
}

public readonly record struct CleanupSkip(string OperationKey, string Path, string Reason, string? Detail = null);
