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
    internal readonly List<PlannedFile> PlannedFiles = new();
    private readonly HashSet<string> _plannedPaths = new(StringComparer.OrdinalIgnoreCase);
    public bool CapturePlan { get; }
    public CleanupPlan? Plan { get; }
    public IApplicationActivity Activity { get; }
    public IFileVerifier FileVerifier { get; }
    public bool ExpertMode { get; }
    internal List<PlannedAction> PlannedActions { get; } = new();
    internal void RecordAction(Cleaner.Core.Data.OperationEntry operation) => PlannedActions.Add(PlannedAction.Capture(operation, Registry));
    public SafetyResult CheckSafety(string path, string operationKey) => MinimumFileAge > TimeSpan.Zero && operationKey is "temp_user" or "temp_sys"
        ? Safety.Check(path) : Safety.CheckForCleanup(path, operationKey);
    private readonly Dictionary<string, string[]> _approvedRoots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string[]> _patterns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TimeSpan> _ages = new(StringComparer.OrdinalIgnoreCase);
    internal void ConfigureRule(Cleaner.Core.Data.OperationEntry operation)
    {
        _patterns[operation.Key] = operation.FilePatterns.ToArray();
        _ages[operation.Key] = operation.OlderThanDays is > 0 ? TimeSpan.FromDays(operation.OlderThanDays.Value) : TimeSpan.Zero;
    }
    internal bool MatchesRule(string key, string path) => !_patterns.TryGetValue(key, out var patterns) || patterns.Length == 0 ||
        patterns.Any(pattern => System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, Path.GetFileName(path), true));
    internal TimeSpan MinimumAgeFor(string key) => _ages.TryGetValue(key, out var ruleAge) && ruleAge > MinimumFileAge ? ruleAge : MinimumFileAge;
    internal void ApproveRoots(string key, IEnumerable<string> roots)
        => _approvedRoots[key] = roots.Where(Path.IsPathFullyQualified).Where(p => !PathExpander.ContainsWildcard(p))
            .Select(Path.GetFullPath).ToArray();
    public bool TryRecordPreviewFile(string key, string path, long size)
    {
        if (CapturePlan)
        {
            if (PlannedFiles.Count >= 250000) throw new InvalidOperationException("Cleanup plan exceeds the safety limit; select fewer categories.");
            if (!_plannedPaths.Add(Path.GetFullPath(path))) { RecordSkip(key, path, "duplicate"); return false; }
            try
            {
                var fullPath = Path.GetFullPath(path);
                var root = _approvedRoots.GetValueOrDefault(key)?.FirstOrDefault(r => CleanupPlan.ContainsPath(r, fullPath));
                if (root == null) { RecordSkip(key, path, "unverifiable"); return false; }
                using var file = FileVerifier.Open(path);
                var age = MinimumAgeFor(key);
                if (age > TimeSpan.Zero && (DateTime.UtcNow - DateTime.FromFileTimeUtc(file.Identity.Created) < age ||
                    DateTime.UtcNow - DateTime.FromFileTimeUtc(file.Identity.Modified) < age))
                { RecordSkip(key, path, "too_recent"); return false; }
                PlannedFiles.Add(new(key, fullPath, file.Identity, root));
                size = file.Identity.Length;
            }
            catch (Exception ex) { RecordSkip(key, path, FileFailure.Classify(ex), ex.Message); return false; }
        }
        RecordPreviewFile(key, path, size); return true;
    }
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
        TimeSpan minimumFileAge = default,
        bool capturePlan = false,
        CleanupPlan? plan = null,
        IApplicationActivity? activity = null,
        IFileVerifier? fileVerifier = null,
        bool expertMode = false)
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
        CapturePlan = capturePlan; Plan = plan; Activity = activity ?? new NoApplicationActivity();
        ExpertMode = expertMode;
        FileVerifier = fileVerifier ?? new WindowsFileVerifier();
        if (capturePlan && !dryRun || plan != null && dryRun) throw new ArgumentException("Invalid cleanup plan context.");
    }

    /// <summary>Удобный helper для отчёта прогресса из handler'а.</summary>
    public void Report(string operationKey, string handler, string? currentPath,
                       int filesProcessed, long bytesFreedSoFar, string? stage = null)
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (stage != "done" && _lastProgress != 0 && System.Diagnostics.Stopwatch.GetElapsedTime(_lastProgress, now).TotalMilliseconds < 100) return;
        _lastProgress = now;
        Progress?.Report(new CleanupProgress(
            operationKey, handler, currentPath, filesProcessed, bytesFreedSoFar, stage));
    }

    public bool IsCancellationRequested => CancellationToken.IsCancellationRequested;
    private long _lastProgress;
}
