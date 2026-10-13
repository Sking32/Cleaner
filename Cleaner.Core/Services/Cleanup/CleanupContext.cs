using System;
using System.Threading;

namespace Cleaner.Core.Services.Cleanup;

public enum CleanupFileMode { Quarantine, PermanentDelete }

/// <summary>
/// Окружение, передаваемое в каждый <see cref="ICleanupHandler"/>.
/// Содержит safety/whitelist/quarantine + прогресс + токен отмены.
/// </summary>
public sealed class CleanupContext
{
    private readonly List<CleanupPreviewFile> _previewFiles = new();
    private readonly Dictionary<string, int> _previewCounts = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<CleanupPreviewFile> PreviewFiles => _previewFiles.ToArray();
    public IReadOnlyDictionary<string, int> PreviewFileCounts => new Dictionary<string, int>(_previewCounts, StringComparer.OrdinalIgnoreCase);
    public int OmittedPreviewFiles { get; private set; }
    public void RecordPreviewFile(string operationKey, string path, long size)
    {
        if (!DryRun) return;
        _previewCounts.TryGetValue(operationKey, out var count);
        _previewCounts[operationKey] = count + 1;
        if (count < 5 && _previewFiles.Count < 250) _previewFiles.Add(new(operationKey, path, size));
        else OmittedPreviewFiles++;
    }
    private readonly List<CleanupSkip> _skips = new();
    public IReadOnlyList<CleanupSkip> SkipDetails => _skips.ToArray();
    public int OmittedSkipDetails { get; private set; }
    public void RecordSkip(string operationKey, string path, string reason, string? detail = null)
    {
        if (_skips.Count < 250) _skips.Add(new(operationKey, path, reason, detail));
        else OmittedSkipDetails++;
    }
    public ISafetyService Safety { get; }
    public IWhitelistService Whitelist { get; }
    public IQuarantineService Quarantine { get; }
    public IProgress<CleanupProgress>? Progress { get; }
    public CancellationToken CancellationToken { get; }
    public ICommandExecutor Commands { get; }
    public Cleaner.Core.Registry.IRegistryAccess Registry { get; }

    /// <summary>
    /// Если true — handler ничего не удаляет, только считает что было бы удалено.
    /// Используется для preview и для CLI-режима без --apply.
    /// </summary>
    public bool DryRun { get; }
    public CleanupFileMode FileMode { get; }
    public TimeSpan MinimumFileAge { get; }

    public CleanupContext(
        ISafetyService safety,
        IWhitelistService whitelist,
        IQuarantineService quarantine,
        IProgress<CleanupProgress>? progress = null,
        CancellationToken cancellationToken = default,
        bool dryRun = false,
        ICommandExecutor? commands = null,
        Cleaner.Core.Registry.IRegistryAccess? registry = null,
        CleanupFileMode fileMode = CleanupFileMode.Quarantine,
        TimeSpan minimumFileAge = default)
    {
        Safety = safety ?? throw new ArgumentNullException(nameof(safety));
        Whitelist = whitelist ?? throw new ArgumentNullException(nameof(whitelist));
        Quarantine = quarantine ?? throw new ArgumentNullException(nameof(quarantine));
        Progress = progress;
        CancellationToken = cancellationToken;
        DryRun = dryRun;
        Commands = commands ?? new SystemCommandExecutor();
        Registry = registry ?? new Cleaner.Core.Registry.WindowsRegistryAccess();
        if (!Enum.IsDefined(fileMode)) throw new ArgumentOutOfRangeException(nameof(fileMode));
        if (minimumFileAge < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(minimumFileAge));
        FileMode = fileMode;
        MinimumFileAge = minimumFileAge;
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
