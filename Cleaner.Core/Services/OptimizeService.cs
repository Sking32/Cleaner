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

    public TweakRepository Tweaks { get; }

    public IReadOnlyList<string> AppliedKeys => _appliedStore.GetAppliedKeys();
    public int PendingRebootCount => _appliedStore.PendingRebootCount;

    public OptimizeService(
        TweakRepository tweaks,
        IActionInterpreter interpreter,
        IBackupService backup,
        AppliedTweaksStore appliedStore,
        PresetsRepository presets)
    {
        Tweaks = tweaks ?? throw new ArgumentNullException(nameof(tweaks));
        _interpreter = interpreter ?? throw new ArgumentNullException(nameof(interpreter));
        _backup = backup ?? throw new ArgumentNullException(nameof(backup));
        _appliedStore = appliedStore ?? throw new ArgumentNullException(nameof(appliedStore));
        _presets = presets ?? throw new ArgumentNullException(nameof(presets));
    }

    // ---------- Apply / Unapply ----------

    public ApplyResult Apply(string tweakKey)
    {
        var tweak = Tweaks.GetByKey(tweakKey);
        if (tweak == null) return new ApplyResult(false, $"tweak not found: {tweakKey}");

        foreach (var action in tweak.Apply)
        {
            var r = _interpreter.Execute(action, tweak.Key, tweak.Category);
            if (!r.Success)
                return new ApplyResult(false, $"[{action.Type}] {r.Error}");
        }

        var requiresReboot = tweak.RequiresReboot || _presets.IsRebootTweak(tweak.Key);
        _appliedStore.MarkApplied(tweak.Key, requiresReboot);

        return new ApplyResult(true);
    }

    public ApplyResult Unapply(string tweakKey)
    {
        var tweak = Tweaks.GetByKey(tweakKey);
        if (tweak == null) return new ApplyResult(false, $"tweak not found: {tweakKey}");

        foreach (var action in tweak.Unapply)
        {
            var r = _interpreter.Execute(action, tweak.Key, tweak.Category);
            if (!r.Success)
                return new ApplyResult(false, $"[{action.Type}] {r.Error}");
        }

        _appliedStore.MarkUnapplied(tweak.Key);
        return new ApplyResult(true);
    }

    // ---------- Rollback ----------

    public RollbackResult Rollback(string tweakKey)
    {
        var tweak = Tweaks.GetByKey(tweakKey);
        if (tweak == null) return new RollbackResult(false, $"tweak not found: {tweakKey}");

        // Собираем все backup-ключи, что относятся к этому твику:
        //   - сам tweakKey
        //   - tweakKey_<suffix> (для regSetAllSubKeys)
        //   - svc_<serviceName> (для serviceSet)
        var candidates = new List<string>();
        var snapshot = _backup.Entries;
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
            return new RollbackResult(false, "nothing to roll back for this tweak");

        var failed = 0;
        foreach (var key in candidates)
        {
            if (!_backup.RestoreByKey(key)) failed++;
        }

        if (failed == 0)
        {
            _appliedStore.MarkUnapplied(tweak.Key);
            return new RollbackResult(true);
        }
        return new RollbackResult(false, $"failed to restore {failed} entries");
    }

    public (int Restored, int Failed) RollbackAll()
    {
        var (restored, failed) = _backup.RestoreAll();
        if (failed == 0)
            _appliedStore.ClearAll();
        return (restored, failed);
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
}