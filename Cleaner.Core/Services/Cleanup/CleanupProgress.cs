namespace Cleaner.Core.Services.Cleanup;

/// <summary>Прогресс выполнения одной операции.</summary>
public readonly record struct CleanupProgress(
    string OperationKey,
    string Handler,
    string? CurrentPath,
    int FilesProcessed,
    long BytesFreedSoFar,
    string? Stage = null);