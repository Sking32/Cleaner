using System;
using System.IO;
using System.Linq;
using Cleaner.Core.Data;
using Cleaner.Core.Registry;
using Cleaner.Core.Services;
using Microsoft.Win32;
using Xunit;

namespace Cleaner.Core.Tests;

public class OptimizeServiceTests : IDisposable
{
    private readonly string _backupFile;
    private readonly string _appliedFile;
    private readonly BackupService _backup;
    private readonly AppliedTweaksStore _appliedStore;
    private readonly ActionInterpreter _interp;
    private readonly OptimizeService _svc;
    private readonly TweakRepository _tweaks;

    public OptimizeServiceTests()
    {
        _backupFile = Path.Combine(Path.GetTempPath(), "cleaner-opt-bak-" + Guid.NewGuid().ToString("N") + ".json");
        _appliedFile = Path.Combine(Path.GetTempPath(), "cleaner-opt-app-" + Guid.NewGuid().ToString("N") + ".json");

        _backup = new BackupService(_backupFile);
        _appliedStore = new AppliedTweaksStore(_appliedFile);
        _interp = new ActionInterpreter(_backup);
        _tweaks = new TweakRepository();
        var presets = new PresetsRepository();

        _svc = new OptimizeService(_tweaks, _interp, _backup, _appliedStore, presets);
    }

    public void Dispose()
    {
        try { if (File.Exists(_backupFile)) File.Delete(_backupFile); } catch { }
        try { if (File.Exists(_appliedFile)) File.Delete(_appliedFile); } catch { }
        try { var t = _backupFile + ".tmp"; if (File.Exists(t)) File.Delete(t); } catch { }
        try { var t = _appliedFile + ".tmp"; if (File.Exists(t)) File.Delete(t); } catch { }
    }

    // ---------- Apply / Unapply on HKCU tweaks ----------

    [Fact]
    public void Apply_VisualFx_Succeeds()
    {
        var r = _svc.Apply("VisualFx");
        Assert.True(r.Success, r.Error);
        Assert.True(_svc.IsAppliedByCleaner("VisualFx"));

        var v = RegistryHelper.GetValue(
            "HKCU",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects",
            "VisualFXSetting");
        Assert.Equal(2, v);

        _svc.Rollback("VisualFx");
    }

    [Fact]
    public void Apply_Unknown_Tweak_Fails()
    {
        var r = _svc.Apply("NoSuchTweak");
        Assert.False(r.Success);
        Assert.Contains("not found", r.Error);
    }

    [Fact]
    public void Unapply_Marks_Not_Applied()
    {
        var apply = _svc.Apply("VisualFx");
        Assert.True(apply.Success, apply.Error);
        Assert.True(_svc.IsAppliedByCleaner("VisualFx"));

        var unapply = _svc.Unapply("VisualFx");
        Assert.True(unapply.Success, unapply.Error);
        Assert.False(_svc.IsAppliedByCleaner("VisualFx"));

        _svc.Rollback("VisualFx");
    }

    // ---------- Rollback ----------

    [Fact]
    public void Rollback_Restores_OldValue()
    {
        RegistryHelper.SetValue(
            "HKCU",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects",
            "VisualFXSetting", 1, RegistryValueKind.DWord, true);

        var apply = _svc.Apply("VisualFx");
        Assert.True(apply.Success, apply.Error);

        var afterApply = RegistryHelper.GetValue(
            "HKCU",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects",
            "VisualFXSetting");
        Assert.Equal(2, afterApply);

        var rb = _svc.Rollback("VisualFx");
        Assert.True(rb.Success, rb.Error);

        var afterRollback = RegistryHelper.GetValue(
            "HKCU",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects",
            "VisualFXSetting");
        Assert.Equal(1, afterRollback);
        Assert.False(_svc.IsAppliedByCleaner("VisualFx"));
    }

    [Fact]
    public void Rollback_Without_Backup_Fails()
    {
        var r = _svc.Rollback("Cortana");
        Assert.False(r.Success);
    }

    // ---------- IsPendingReboot ----------

    [Fact]
    public void Apply_NonRebootTweak_NotPending()
    {
        var r = _svc.Apply("VisualFx");
        Assert.True(r.Success, r.Error);
        Assert.False(_svc.IsPendingReboot("VisualFx"));

        _svc.Rollback("VisualFx");
    }

    [Fact]
    public void ClearAllRebootPending_Resets_Flags()
    {
        _svc.Apply("VisualFx");
        _svc.ClearAllRebootPending();
        Assert.Equal(0, _svc.PendingRebootCount);
        _svc.Rollback("VisualFx");
    }

    // ---------- GetState ----------

    [Fact]
    public void GetState_True_After_Apply()
    {
        var apply = _svc.Apply("VisualFx");
        Assert.True(apply.Success, apply.Error);
        Assert.True(_svc.GetState("VisualFx"));

        _svc.Rollback("VisualFx");
    }

    [Fact]
    public void GetState_False_For_Unknown()
    {
        Assert.False(_svc.GetState("NoSuchTweak"));
    }

    // ---------- AppliedKeys ----------

    [Fact]
    public void AppliedKeys_Tracks_Multiple()
    {
        _svc.Apply("VisualFx");
        _svc.Apply("Transparency");

        var keys = _svc.AppliedKeys;
        Assert.Contains("VisualFx", keys);
        Assert.Contains("Transparency", keys);

        _svc.RollbackAll();
    }

    // ---------- RollbackAll ----------

    [Fact]
    public void RollbackAll_Restores_Everything()
    {
        // Готовим старое значение VisualFXSetting
        RegistryHelper.SetValue(
            "HKCU",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects",
            "VisualFXSetting", 1, RegistryValueKind.DWord, true);

        _svc.Apply("VisualFx");
        _svc.Apply("Transparency");

        var (restored, failed) = _svc.RollbackAll();
        Assert.True(restored >= 2);
        Assert.Equal(0, failed);

        var v = RegistryHelper.GetValue(
            "HKCU",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects",
            "VisualFXSetting");
        Assert.Equal(1, v);
        Assert.Empty(_svc.AppliedKeys);
    }

    // ---------- Persistence of applied-store ----------

    [Fact]
    public void AppliedStore_Survives_Reload()
    {
        _svc.Apply("VisualFx");

        var store2 = new AppliedTweaksStore(_appliedFile);
        Assert.True(store2.IsApplied("VisualFx"));

        _svc.Rollback("VisualFx");
    }
}