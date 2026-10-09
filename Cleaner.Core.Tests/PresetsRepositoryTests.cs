using System.Linq;
using Cleaner.Core.Data;
using Xunit;

namespace Cleaner.Core.Tests;

public class PresetsRepositoryTests
{
    [Fact]
    public void Loads_Three_Cleanup_Presets()
    {
        var repo = new PresetsRepository();
        Assert.Equal(3, repo.CleanupPresets.Count);
        Assert.True(repo.CleanupPresets.ContainsKey("soft"));
        Assert.True(repo.CleanupPresets.ContainsKey("std"));
        Assert.True(repo.CleanupPresets.ContainsKey("deep"));
    }

    [Fact]
    public void Loads_Six_Optimize_Presets()
    {
        var repo = new PresetsRepository();
        Assert.Equal(6, repo.OptimizePresets.Count);
        foreach (var key in new[] { "gamer", "privacy", "perf", "win11", "dev", "safe" })
            Assert.True(repo.OptimizePresets.ContainsKey(key), $"Missing preset: {key}");
    }

    [Fact]
    public void Deep_Cleanup_Includes_All_Std_Keys()
    {
        var repo = new PresetsRepository();
        var std = repo.CleanupPresets["std"].Keys.ToHashSet();
        var deep = repo.CleanupPresets["deep"].Keys.ToHashSet();

        foreach (var k in std)
            Assert.True(deep.Contains(k), $"Deep preset missing std key: {k}");
    }

    [Fact]
    public void Every_Preset_Key_Exists_In_Operations_Or_Tweaks()
    {
        var ops = new OperationsRepository();
        var tweaks = new TweakRepository();
        var repo = new PresetsRepository();

        var opKeys = ops.Operations.Select(o => o.Key).ToHashSet();
        var tweakKeys = tweaks.Tweaks.Select(t => t.Key).ToHashSet();

        foreach (var (name, preset) in repo.CleanupPresets)
            foreach (var k in preset.Keys)
                Assert.True(opKeys.Contains(k), $"Cleanup preset '{name}' has unknown key: {k}");

        foreach (var (name, preset) in repo.OptimizePresets)
            foreach (var k in preset.Keys)
                Assert.True(tweakKeys.Contains(k), $"Optimize preset '{name}' has unknown key: {k}");
    }

    [Fact]
    public void Every_Preset_Has_Localized_Name()
    {
        var repo = new PresetsRepository();
        var ru = new LocalizationRepository("ru");
        var en = new LocalizationRepository("en");

        foreach (var (_, preset) in repo.CleanupPresets)
        {
            Assert.True(ru.Has(preset.NameKey), $"RU missing: {preset.NameKey}");
            Assert.True(ru.Has(preset.ShortKey), $"RU missing: {preset.ShortKey}");
            Assert.True(en.Has(preset.NameKey), $"EN missing: {preset.NameKey}");
            Assert.True(en.Has(preset.ShortKey), $"EN missing: {preset.ShortKey}");
        }
        foreach (var (_, preset) in repo.OptimizePresets)
        {
            Assert.True(ru.Has(preset.NameKey), $"RU missing: {preset.NameKey}");
            Assert.True(en.Has(preset.NameKey), $"EN missing: {preset.NameKey}");
        }
    }

    [Fact]
    public void RebootTweaks_Count_Is_Expected()
    {
        var repo = new PresetsRepository();
        Assert.Equal(21, repo.RebootTweaks.Count);
        Assert.Contains("HAGS", repo.RebootTweaks);
        Assert.Contains("SysMain", repo.RebootTweaks);
        Assert.Contains("EndTask", repo.RebootTweaks);
    }

    [Fact]
    public void RebootTweaks_All_Exist_In_Tweaks()
    {
        var repo = new PresetsRepository();
        var tweaks = new TweakRepository();
        var keys = tweaks.Tweaks.Select(t => t.Key).ToHashSet();

        foreach (var k in repo.RebootTweaks)
            Assert.True(keys.Contains(k), $"Reboot tweak unknown: {k}");
    }

    [Fact]
    public void Conflicts_Are_Loaded()
    {
        var repo = new PresetsRepository();
        Assert.Equal(3, repo.Conflicts.Count);

        var hf = repo.FindConflicts(new[] { "hibernate", "FastStartup", "Cortana" }).ToList();
        Assert.Single(hf);
        Assert.Equal("hibernate", hf[0].A);
        Assert.Equal("FastStartup", hf[0].B);

        var none = repo.FindConflicts(new[] { "Cortana", "VisualFx" }).ToList();
        Assert.Empty(none);
    }

    [Fact]
    public void Conflict_Keys_Exist_In_Tweaks_Or_Operations()
    {
        var presets = new PresetsRepository();
        var tweaks = new TweakRepository();
        var ops = new OperationsRepository();

        var known = tweaks.Tweaks.Select(t => t.Key)
            .Concat(ops.Operations.Select(o => o.Key))
            .ToHashSet();

        foreach (var c in presets.Conflicts)
        {
            Assert.True(known.Contains(c.A), $"Conflict A unknown: {c.A}");
            Assert.True(known.Contains(c.B), $"Conflict B unknown: {c.B}");
        }
    }
}