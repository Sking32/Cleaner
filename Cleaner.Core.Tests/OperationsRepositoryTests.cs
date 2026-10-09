using System.Linq;
using Cleaner.Core.Data;
using Xunit;

namespace Cleaner.Core.Tests;

public class OperationsRepositoryTests
{
    [Fact]
    public void Loads_All_56_Operations_And_9_Sections()
    {
        var repo = new OperationsRepository();
        Assert.Equal(56, repo.Operations.Count());
        Assert.Equal(9, repo.Sections.Count());
    }

    [Fact]
    public void Every_Operation_Has_Handler()
    {
        var repo = new OperationsRepository();
        foreach (var op in repo.Operations)
        {
            Assert.False(string.IsNullOrEmpty(op.Handler), $"{op.Key}: handler is empty");
        }
    }

    [Fact]
    public void Every_Operation_Has_Category()
    {
        var repo = new OperationsRepository();
        foreach (var op in repo.Operations)
        {
            Assert.False(string.IsNullOrEmpty(op.Category), $"{op.Key}: category is empty");
        }
    }

    [Fact]
    public void Every_Operation_Has_Localization()
    {
        var repo = new OperationsRepository();
        var ru = new LocalizationRepository("ru");
        var en = new LocalizationRepository("en");

        foreach (var op in repo.Operations)
        {
            Assert.True(ru.Has(op.NameKey), $"RU missing: {op.NameKey}");
            Assert.True(ru.Has(op.DescKey), $"RU missing: {op.DescKey}");
            Assert.True(en.Has(op.NameKey), $"EN missing: {op.NameKey}");
            Assert.True(en.Has(op.DescKey), $"EN missing: {op.DescKey}");
        }
    }

    [Fact]
    public void Known_Operations_Have_Expected_Handlers()
    {
        var repo = new OperationsRepository();

        Assert.Equal("PathCleanup", repo.GetByKey("temp_user")!.Handler);
        Assert.Equal("PathCleanup", repo.GetByKey("temp_sys")!.Handler);
        Assert.Equal("BrowserProfiles", repo.GetByKey("chrome")!.Handler);
        Assert.Equal("BrowserProfiles", repo.GetByKey("edge")!.Handler);
        Assert.Equal("FirefoxProfiles", repo.GetByKey("firefox")!.Handler);
        Assert.Equal("DismComponentCleanup", repo.GetByKey("winsxs")!.Handler);
        Assert.Equal("WindowsOld", repo.GetByKey("windowsold")!.Handler);
        Assert.Equal("ToggleHibernation", repo.GetByKey("hibernate")!.Handler);
    }

    [Fact]
    public void Every_Operation_Belongs_To_A_Category()
    {
        var repo = new OperationsRepository();
        var categories = repo.Operations.Select(o => o.Category).Distinct().ToList();

        Assert.Contains("quick", categories);
        Assert.Contains("reports", categories);
        Assert.Contains("syscache", categories);
        Assert.Contains("gfx", categories);
        Assert.Contains("browsers", categories);
        Assert.Contains("apps", categories);
        Assert.Contains("dev", categories);
        Assert.Contains("privacy", categories);
        Assert.Contains("heavy", categories);
    }
}