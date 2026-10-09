using System;
using System.Threading;

namespace Cleaner.Core.Services.Cleanup;

/// <summary>
/// Окружение, передаваемое в каждый <see cref="ICleanupHandler"/>.
/// Содержит safety/whitelist/quarantine + прогресс + токен отмены.
/// </summary>
public sealed class CleanupContext
{
    public ISafetyService Safety { get; }
    public IWhitelistService Whitelist { get; }
    public IQuarantineService Quarantine { get; }
    public IProgress<CleanupProgress>? Progress { get; }
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// Если true — handler ничего не удаляет, только считает что было бы удалено.
    /// Используется для preview и для CLI-режима без --apply.
    /// </summary>
    public bool DryRun { get; }

    public CleanupContext(
        ISafetyService safety,
        IWhitelistService whitelist,
        IQuarantineService quarantine,
        IProgress<CleanupProgress>? progress = null,
        CancellationToken cancellationToken = default,
        bool dryRun = false)
    {
        Safety = safety ?? throw new ArgumentNullException(nameof(safety));
        Whitelist = whitelist ?? throw new ArgumentNullException(nameof(whitelist));
        Quarantine = quarantine ?? throw new ArgumentNullException(nameof(quarantine));
        Progress = progress;
        CancellationToken = cancellationToken;
        DryRun = dryRun;
    }

    /// <summary>Удобный helper для отчёта прогресса из handler'а.</summary>
    public void Report(string operationKey, string handler, string? currentPath,
                       int filesProcessed, long bytesFreedSoFar, string? stage = null)
    {
        Progress?.Report(new CleanupProgress(
            operationKey, handler, currentPath, filesProcessed, bytesFreedSoFar, stage));
    }

    public bool IsCancellationRequested => CancellationToken.IsCancellationRequested;
}