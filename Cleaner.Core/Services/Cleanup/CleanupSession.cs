using System;
using System.Collections.Generic;

namespace Cleaner.Core.Services.Cleanup;

/// <summary>Итог всей сессии очистки.</summary>
public sealed class CleanupSessionResult
{
    public long BytesFreed { get; init; }
    public long PreviewBytes { get; init; }
    public long BytesQuarantined { get; init; }
    public int FilesQuarantined { get; init; }
    public int FilesDeleted { get; init; }
    public int FilesSkipped { get; init; }
    public int OperationsRun { get; init; }
    public int OperationsFailed { get; init; }
    public TimeSpan Duration { get; init; }
    public IReadOnlyList<OperationOutcome> Outcomes { get; init; } = Array.Empty<OperationOutcome>();

    /// <summary>Была ли сессия отменена пользователем.</summary>
    public bool Canceled { get; init; }
    public IReadOnlyList<CleanupSkip> SkipDetails { get; init; } = Array.Empty<CleanupSkip>();
    public int OmittedSkipDetails { get; init; }
    public IReadOnlyList<CleanupPreviewFile> PreviewFiles { get; init; } = Array.Empty<CleanupPreviewFile>();
    public IReadOnlyDictionary<string, int> PreviewFileCounts { get; init; } = new Dictionary<string, int>();
    public int OmittedPreviewFiles { get; init; }
}

public sealed record CleanupPreviewFile(string OperationKey, string Path, long Size);

public sealed class OperationOutcome
{
    public string Key { get; init; } = "";
    public string Handler { get; init; } = "";
    public bool Success { get; init; }
    public bool Canceled { get; init; }
    public string? Message { get; init; }
    public long BytesFreed { get; init; }
    public long PreviewBytes { get; init; }
    public long BytesQuarantined { get; init; }
    public int FilesQuarantined { get; init; }
    public int FilesDeleted { get; init; }
    public int FilesSkipped { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public TimeSpan Duration { get; init; }
}
