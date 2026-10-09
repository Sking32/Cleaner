using System;
using System.Collections.Generic;
using System.Linq;

namespace Cleaner.Core.Services.Cleanup;

/// <summary>Результат выполнения одной cleanup-операции (handler'а).</summary>
public sealed class CleanupOperationResult
{
    public bool Success { get; init; }
    public long BytesFreed { get; init; }
    public int FilesDeleted { get; init; }
    public int FilesSkipped { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public string? Message { get; init; }

    public static CleanupOperationResult Ok(long bytesFreed = 0, int filesDeleted = 0,
                                            int filesSkipped = 0, string? message = null)
        => new()
        {
            Success = true,
            BytesFreed = bytesFreed,
            FilesDeleted = filesDeleted,
            FilesSkipped = filesSkipped,
            Message = message
        };

    public static CleanupOperationResult Fail(string error, long bytesFreed = 0, int filesDeleted = 0)
        => new()
        {
            Success = false,
            BytesFreed = bytesFreed,
            FilesDeleted = filesDeleted,
            Errors = new[] { error },
            Message = error
        };

    public static CleanupOperationResult Fail(IEnumerable<string> errors, long bytesFreed = 0, int filesDeleted = 0)
    {
        var list = errors.ToList();
        return new CleanupOperationResult
        {
            Success = false,
            BytesFreed = bytesFreed,
            FilesDeleted = filesDeleted,
            Errors = list,
            Message = list.Count > 0 ? list[0] : "failed"
        };
    }
}