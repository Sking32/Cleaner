using System;
using System.Collections.Generic;
using System.Linq;
using Cleaner.Core.Data;
using Cleaner.Core.Models;

namespace Cleaner.Core.Services;

public readonly record struct ApplyResult(bool Success, string? Error = null);
public readonly record struct RollbackResult(bool Success, string? Error = null);

public interface IOptimizeService
{
    TweakRepository Tweaks { get; }

    /// <summary>Применить твик (выполнить все его apply-actions).</summary>
    ApplyResult Apply(string tweakKey);

    /// <summary>Снять твик (выполнить все его unapply-actions).</summary>
    ApplyResult Unapply(string tweakKey);

    /// <summary>Откатить твик к сохранённому значению через BackupService.</summary>
    RollbackResult Rollback(string tweakKey);

    /// <summary>Откатить ВСЕ твики, что попадали в бэкап.</summary>
    (int Restored, int Failed) RollbackAll();

    /// <summary>Проверить, соответствует ли система ожидаемому состоянию твика (state-check).</summary>
    bool GetState(string tweakKey);

    /// <summary>Применён ли твик именно через Cleaner.</summary>
    bool IsAppliedByCleaner(string tweakKey);

    /// <summary>Требуется ли перезагрузка для этого твика.</summary>
    bool IsPendingReboot(string tweakKey);

    /// <summary>Список ключей применённых твиков.</summary>
    IReadOnlyList<string> AppliedKeys { get; }

    int PendingRebootCount { get; }

    /// <summary>После перезагрузки вызывается — сбрасывает reboot-флаги.</summary>
    void ClearAllRebootPending();
}

public sealed class OptimizeService : IOptimizeService
{
    private readonly IActionInterpreter _interpreter;
    private readonly IBackupService _backup;
    private readonly AppliedTweaksStore _appliedStore;
    private readonly PresetsRepository _presets;
    private readonly object _operationLock = new();
    private readonly Func<TweakDefinition, TweakCompatibilityResult> _compatibility;

    public TweakRepository Tweaks { get; }

    public IReadOnlyList<string> AppliedKeys => _appliedStore.GetAppliedKeys();
    public int PendingRebootCount => _appliedStore.PendingRebootCount;

    public OptimizeService(
        TweakRepository tweaks,
        IActionInterpreter interpreter,
        IBackupService backup,
        AppliedTweaksStore appliedStore,
        PresetsRepository presets,
        Func<TweakDefinition, TweakCompatibilityResult>? compatibility = null,
        Func<bool>? expertMode = null)
    {
        Tweaks = tweaks ?? throw new ArgumentNullException(nameof(tweaks));
        _interpreter = interpreter ?? throw new ArgumentNullException(nameof(interpreter));
        _backup = backup ?? throw new ArgumentNullException(nameof(backup));
        _appliedStore = appliedStore ?? throw new ArgumentNullException(nameof(appliedStore));
        _presets = presets ?? throw new ArgumentNullException(nameof(presets));
        _compatibility = compatibility ?? (t => TweakCompatibilityService.Evaluate(t, expertMode: expertMode?.Invoke() == true));
    }

    // ---------- Apply / Unapply ----------

    public ApplyResult Apply(string tweakKey)
    {
        lock (_operationLock)
        {
            try { return ApplyCore(tweakKey); }
            catch (Exception ex)
            {
                // Persistence failures must not leave a successful-looking partial application.
                var rollback = Rollback(tweakKey);
                return new(false, ex.Message + (rollback.Success ? " (restored)" : "; recovery: " + rollback.Error));
            }
        }
    }

    private ApplyResult ApplyCore(string tweakKey)
    {
        var tweak = Tweaks.GetByKey(tweakKey);
        if (tweak == null) return new ApplyResult(false, $"tweak not found: {tweakKey}");
        if (tweak.Apply.Count == 0) return new(false, "Tweak has no apply actions.");

        var wasApplied = _appliedStore.IsApplied(tweak.Key);
        if (wasApplied) return new ApplyResult(true);
        var compatibility = _compatibility(tweak);
        if (!compatibility.CanApply) return new(false, compatibility.MessageKey);
        if (_backup.Keys.Any(k => k == tweakKey || k.StartsWith(tweakKey + "_", StringComparison.Ordinal)))
            return new(false, "Pending recovery data exists. Roll back this tweak before applying it again.");
        var overlap = _appliedStore.GetAppliedKeys().FirstOrDefault(key =>
        {
            var other = Tweaks.GetByKey(key);
            return other != null && tweak.Apply.Any(a => other.Apply.Any(b => SameTarget(a, b)));
        });
        if (overlap != null) return new(false, "Setting is already controlled by tweak: " + overlap);
        var conflicts = _presets.FindConflicts(_appliedStore.GetAppliedKeys().Append(tweak.Key)).ToList();
        if (conflicts.Count > 0) return new(false, "Conflicting tweaks: " + string.Join(", ", conflicts.Select(c => c.A + "/" + c.B)));

        foreach (var action in tweak.Apply)
        {
            var r = _interpreter.Execute(action, tweak.Key, tweak.Category);
            if (!r.Success)
            {
                var error = $"[{action.Type}] {r.Error}";
                CoreLog.Warn($"Твик {tweak.Key}: ошибка применения: {error}");

                // Не оставляем систему в «полуприменённом» состоянии.
                if (!wasApplied)
                {
                    try
                    {
                        var rb = Rollback(tweak.Key);
                        error += rb.Success ? " (изменения отменены)" : "; recovery: " + rb.Error;
                    }
                    catch (Exception rbEx)
                    {
                        CoreLog.Warn($"Твик {tweak.Key}: откат после ошибки не удался", rbEx);
                    }
                }

                return new ApplyResult(false, error);
            }
        }

        var requiresReboot = tweak.RequiresReboot || _presets.IsRebootTweak(tweak.Key) || tweak.Apply.Any(a => a.Type is "serviceSet" or "restartExplorer");
        _appliedStore.MarkApplied(tweak.Key, requiresReboot);

        return new ApplyResult(true);
    }

    public ApplyResult Unapply(string tweakKey)
    {
        var result = Rollback(tweakKey);
        return new(result.Success, result.Error);
    }

    // ---------- Rollback ----------

    public RollbackResult Rollback(string tweakKey)
    {
        lock (_operationLock)
        {
            try { return RollbackCore(tweakKey); }
            catch (Exception ex) { return new(false, ex.Message); }
        }
    }

    private RollbackResult RollbackCore(string tweakKey)
    {
        var tweak = Tweaks.GetByKey(tweakKey);
        if (tweak == null) return new RollbackResult(false, $"tweak not found: {tweakKey}");

        // Собираем все backup-ключи, что относятся к этому твику:
        //   - сам tweakKey
        //   - tweakKey_<suffix> (для regSetAllSubKeys)
        //   - svc_<serviceName> (для serviceSet)
        var candidates = new List<string>();
        var snapshot = _backup.Entries;
        if (snapshot.ContainsKey(tweakKey) && tweak.Apply.Count(a =>
                a.Type is "regSet" or "regRemove" or "regRemoveWithBackup" or "regSetDynamicDate") > 1)
            return new(false, "Legacy backup is incomplete. Original values cannot be reconstructed automatically; manual recovery required.");
        foreach (var k in snapshot.Keys)
        {
            if (k == tweakKey
                || k.StartsWith(tweakKey + "_", StringComparison.Ordinal))
                candidates.Add(k);
        }
        // serviceSet backup: svc_<name>
        foreach (var action in tweak.Apply)
        {
            if (action.Type == "serviceSet" && !string.IsNullOrEmpty(action.ServiceName))
            {
                var svcKey = "svc_" + action.ServiceName;
                if (snapshot.ContainsKey(svcKey) && !candidates.Contains(svcKey))
                    candidates.Add(svcKey);
            }
        }

        if (candidates.Count == 0)
            return _appliedStore.IsApplied(tweakKey)
                ? new(false, "Original state backup is missing; manual recovery required.")
                : new(true);

        var failed = 0;
        foreach (var key in candidates.AsEnumerable().Reverse())
        {
            if (!_backup.RestoreByKey(key, retainBackup: true)) failed++;
        }

        if (failed == 0)
        {
            _appliedStore.MarkUnapplied(tweak.Key, tweak.RequiresReboot || _presets.IsRebootTweak(tweak.Key));
            _backup.RemoveEntries(candidates);
            return new RollbackResult(true);
        }
        return new RollbackResult(false, $"failed to restore {failed} entries");
    }

    public (int Restored, int Failed) RollbackAll()
    {
        lock (_operationLock)
        {
            int restored = 0, failed = 0;
            var considered = new HashSet<string>(StringComparer.Ordinal);
            // Includes failed applications with recoverable entries, but never clears un-restored marks.
            foreach (var tweak in Tweaks.Tweaks)
            {
                var keys = _backup.Keys.Where(k => k == tweak.Key || k.StartsWith(tweak.Key + "_", StringComparison.Ordinal) ||
                    tweak.Apply.Any(a => a.Type == "serviceSet" && k == "svc_" + a.ServiceName)).ToArray();
                if (!_appliedStore.IsApplied(tweak.Key) && keys.Length == 0) continue;
                considered.UnionWith(keys);
                if (Rollback(tweak.Key).Success) restored++; else failed++;
            }
            // Recovery entries for removed/unknown definitions need manual recovery.
            failed += _backup.Keys.Count(k => !considered.Contains(k));
            return (restored, failed);
        }
    }

    // ---------- State ----------

    public bool GetState(string tweakKey)
    {
        var tweak = Tweaks.GetByKey(tweakKey);
        if (tweak == null) return false;
        if (tweak.State.Count == 0) return false;

        // Все state-действия должны совпасть (AND)
        foreach (var action in tweak.State)
        {
            var r = _interpreter.Check(action);
            if (!r.Matches) return false;
        }
        return true;
    }

    public bool IsAppliedByCleaner(string tweakKey) => _appliedStore.IsApplied(tweakKey);
    public bool IsPendingReboot(string tweakKey) => _appliedStore.IsPendingReboot(tweakKey);

    public void ClearAllRebootPending() => _appliedStore.ClearAllRebootPending();

    private static bool SameTarget(TweakAction a, TweakAction b)
        => a.Type.StartsWith("reg", StringComparison.Ordinal) && b.Type.StartsWith("reg", StringComparison.Ordinal)
            && string.Equals(a.Hive, b.Hive, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Key, b.Key, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)
            || a.Type == "serviceSet" && b.Type == "serviceSet"
                && string.Equals(a.ServiceName, b.ServiceName, StringComparison.OrdinalIgnoreCase);
}
