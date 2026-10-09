using System.Linq;
using Cleaner.Core.Data;
using Xunit;

namespace Cleaner.Core.Tests;

public class TweakRepositoryTests
{
    [Fact]
    public void Loads_All_62_Tweaks_From_Json()
    {
        var repo = new TweakRepository();
        Assert.Equal(62, repo.Tweaks.Count);
        Assert.Contains(repo.Tweaks, t => t.Key == "Cortana");
        Assert.Contains(repo.Tweaks, t => t.Key == "VisualFx");
        Assert.Contains(repo.Tweaks, t => t.Key == "SysMain");
        Assert.Contains(repo.Tweaks, t => t.Key == "HAGS");
        Assert.Contains(repo.Tweaks, t => t.Key == "GamePowerThrottling");
    }

    [Fact]
    public void Every_Tweak_Has_Localization_Keys()
    {
        var repo = new TweakRepository();
        var ru = new LocalizationRepository("ru");
        var en = new LocalizationRepository("en");

        foreach (var t in repo.Tweaks)
        {
            Assert.True(ru.Has(t.NameKey), $"RU missing: {t.NameKey}");
            Assert.True(ru.Has(t.ShortKey), $"RU missing: {t.ShortKey}");
            Assert.True(ru.Has(t.DescKey), $"RU missing: {t.DescKey}");
            Assert.True(en.Has(t.NameKey), $"EN missing: {t.NameKey}");
            Assert.True(en.Has(t.ShortKey), $"EN missing: {t.ShortKey}");
            Assert.True(en.Has(t.DescKey), $"EN missing: {t.DescKey}");
        }
    }

    [Fact]
    public void All_Categories_Are_Represented()
    {
        var repo = new TweakRepository();
        var categories = repo.Tweaks.Select(t => t.Category).Distinct().ToList();
        Assert.Contains("perf", categories);
        Assert.Contains("privacy", categories);
        Assert.Contains("win11", categories);
        Assert.Contains("network", categories);
        Assert.Contains("maint", categories);
        Assert.Contains("games", categories);
    }

    [Fact]
    public void Cortana_Has_Expected_Structure()
    {
        var repo = new TweakRepository();
        var cortana = repo.GetByKey("Cortana");

        Assert.NotNull(cortana);
        Assert.Equal("privacy", cortana!.Category);
        Assert.Equal("safe", cortana.Level);
        Assert.False(cortana.RequiresReboot);
        Assert.Single(cortana.Apply);
        Assert.Equal("regSet", cortana.Apply[0].Type);
        Assert.Equal("HKLM", cortana.Apply[0].Hive);
        Assert.Equal("DWord", cortana.Apply[0].Kind);
    }

    [Fact]
    public void SysMain_Requires_Reboot()
    {
        var repo = new TweakRepository();
        var sysmain = repo.GetByKey("SysMain");

        Assert.NotNull(sysmain);
        Assert.True(sysmain!.RequiresReboot);
        Assert.Equal("caution", sysmain.Level);
        Assert.Equal("serviceSet", sysmain.Apply[0].Type);
    }

    [Fact]
    public void GetByKey_Returns_Null_For_Unknown()
    {
        var repo = new TweakRepository();
        Assert.Null(repo.GetByKey("ThisTweakDoesNotExist"));
    }

    [Fact]
    public void GetByCategory_Filters_Correctly()
    {
        var repo = new TweakRepository();
        var perf = repo.GetByCategory("perf");

        Assert.NotEmpty(perf);
        Assert.All(perf, t => Assert.Equal("perf", t.Category));
    }
}

public class LocalizationRepositoryTests
{
    [Fact]
    public void Loads_Russian_Strings()
    {
        var loc = new LocalizationRepository("ru");
        Assert.Equal("Отключить Cortana", loc["tweak.Cortana.name"]);
        Assert.Equal("Голосовой помощник выключен", loc["tweak.Cortana.short"]);
    }

    [Fact]
    public void Loads_English_Strings()
    {
        var loc = new LocalizationRepository("en");
        Assert.Equal("Disable Cortana", loc["tweak.Cortana.name"]);
        Assert.Equal("Voice assistant off", loc["tweak.Cortana.short"]);
    }

    [Fact]
    public void Unknown_Key_Returns_Marker()
    {
        var loc = new LocalizationRepository("ru");
        Assert.Equal("[no.such.key]", loc["no.such.key"]);
    }
}