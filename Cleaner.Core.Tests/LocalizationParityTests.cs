using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Cleaner.Core.Tests;

/// <summary>Русский и английский файлы локализации должны совпадать по ключам и параметрам {0}, {1}...</summary>
public class LocalizationParityTests
{
    private static Dictionary<string, string> Load(string language)
    {
        var dir = AppContext.BaseDirectory;
        string? path = null;
        for (var i = 0; i < 10 && dir != null; i++)
        {
            var candidate = Path.Combine(dir, "Cleaner.Core", "Data", "localization", language + ".json");
            if (File.Exists(candidate)) { path = candidate; break; }

            var direct = Path.Combine(dir, "Data", "localization", language + ".json");
            if (File.Exists(direct)) { path = direct; break; }

            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(path);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path!))!;
    }

    private static string Placeholders(string text) =>
        string.Join(",", Regex.Matches(text, @"\{(\d+)").Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x));

    [Fact]
    public void Ru_And_En_Have_Same_Keys()
    {
        var ru = Load("ru");
        var en = Load("en");

        Assert.Empty(ru.Keys.Except(en.Keys));
        Assert.Empty(en.Keys.Except(ru.Keys));
    }

    [Fact]
    public void Ru_And_En_Have_Same_Placeholders()
    {
        var ru = Load("ru");
        var en = Load("en");

        var broken = ru.Keys
            .Where(k => en.ContainsKey(k) && Placeholders(ru[k]) != Placeholders(en[k]))
            .ToList();

        Assert.Empty(broken);
    }

    [Fact]
    public void No_Value_Is_Empty()
    {
        foreach (var language in new[] { "ru", "en" })
        {
            var empty = Load(language).Where(p => string.IsNullOrWhiteSpace(p.Value)).Select(p => p.Key).ToList();
            Assert.Empty(empty);
        }
    }
}
