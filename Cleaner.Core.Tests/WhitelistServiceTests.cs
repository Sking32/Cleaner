using System;
using System.IO;
using Cleaner.Core.Services;
using Xunit;

namespace Cleaner.Core.Tests;

public class WhitelistServiceTests : IDisposable
{
    private readonly string _root;
    private readonly string _whitelistFile;

    public WhitelistServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "CleanerWlTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _whitelistFile = Path.Combine(_root, "whitelist.json");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private WhitelistService NewSvc() => new(_whitelistFile);

    // ---------- Базовая загрузка / сохранение ----------

    [Fact]
    public void Loads_Empty_When_File_Missing()
    {
        Assert.False(File.Exists(_whitelistFile));
        var svc = NewSvc();
        Assert.Empty(svc.Rules);
    }

    [Fact]
    public void Add_Persists_To_File()
    {
        var svc = NewSvc();
        Assert.True(svc.Add("*.log"));
        Assert.True(File.Exists(_whitelistFile));

        var svc2 = NewSvc();
        Assert.Single(svc2.Rules);
        Assert.Equal("*.log", svc2.Rules[0]);
    }

    [Fact]
    public void Add_Duplicate_Returns_False()
    {
        var svc = NewSvc();
        Assert.True(svc.Add("*.log"));
        Assert.False(svc.Add("*.log"));
        Assert.False(svc.Add("*.LOG")); // case-insensitive
        Assert.Single(svc.Rules);
    }

    [Fact]
    public void Remove_Removes_Rule()
    {
        var svc = NewSvc();
        svc.Add("*.log");
        svc.Add("*.tmp");

        Assert.True(svc.Remove("*.log"));
        Assert.Single(svc.Rules);
        Assert.False(svc.Remove("no-such-rule"));
    }

    [Fact]
    public void Clear_Removes_All()
    {
        var svc = NewSvc();
        svc.Add("*.log");
        svc.Add("*.tmp");
        svc.Clear();
        Assert.Empty(svc.Rules);
    }

    // ---------- Точный путь ----------

    [Fact]
    public void Exact_Path_Is_Whitelisted()
    {
        var file = Path.Combine(_root, "important.docx");
        var svc = NewSvc();
        svc.Add(file);

        Assert.True(svc.IsWhitelisted(file));
        Assert.False(svc.IsWhitelisted(Path.Combine(_root, "other.docx")));
    }

    [Fact]
    public void Exact_Path_Is_Case_Insensitive()
    {
        var file = Path.Combine(_root, "Docs", "File.txt");
        var svc = NewSvc();
        svc.Add(file);

        Assert.True(svc.IsWhitelisted(file.ToUpperInvariant()));
        Assert.True(svc.IsWhitelisted(file.ToLowerInvariant()));
    }

    [Fact]
    public void Exact_Path_Matches_Trailing_Slash_Insensitively()
    {
        var dir = Path.Combine(_root, "MyFolder");
        var svc = NewSvc();
        svc.Add(dir);

        Assert.True(svc.IsWhitelisted(dir + "\\"));
        Assert.True(svc.IsWhitelisted(dir + "/"));
    }

    // ---------- Glob ----------

    [Fact]
    public void Glob_Extension_Matches_Any_Dir()
    {
        var svc = NewSvc();
        svc.Add("*.log");

        Assert.True(svc.IsWhitelisted(Path.Combine(_root, "a.log")));
        Assert.True(svc.IsWhitelisted(Path.Combine(_root, "sub", "b.log")));
        Assert.False(svc.IsWhitelisted(Path.Combine(_root, "a.txt")));
    }

    [Fact]
    public void Glob_Question_Mark_Matches_Single_Char()
    {
        var svc = NewSvc();
        svc.Add("temp_??.txt");

        Assert.True(svc.IsWhitelisted(Path.Combine(_root, "temp_01.txt")));
        Assert.False(svc.IsWhitelisted(Path.Combine(_root, "temp_001.txt")));
    }

    [Fact]
    public void Glob_Folder_Prefix_Matches_Nested()
    {
        var svc = NewSvc();
        var folder = Path.Combine(_root, "Projects");
        svc.Add(folder + "\\*");

        Assert.True(svc.IsWhitelisted(Path.Combine(folder, "a.txt")));
        Assert.True(svc.IsWhitelisted(Path.Combine(folder, "sub", "b.txt")));
        Assert.False(svc.IsWhitelisted(Path.Combine(_root, "Other", "a.txt")));
    }

    // ---------- Regex ----------

    [Fact]
    public void Regex_Rule_Matches()
    {
        var svc = NewSvc();
        svc.Add(@"re:\.bak$");

        Assert.True(svc.IsWhitelisted(Path.Combine(_root, "old.bak")));
        Assert.False(svc.IsWhitelisted(Path.Combine(_root, "old.txt")));
    }

    [Fact]
    public void Regex_Rule_Is_Case_Insensitive()
    {
        var svc = NewSvc();
        svc.Add(@"re:^.*important.*$");

        Assert.True(svc.IsWhitelisted(Path.Combine(_root, "IMPORTANT_data")));
        Assert.True(svc.IsWhitelisted(Path.Combine(_root, "very-important")));
    }

    [Fact]
    public void Invalid_Regex_Does_Not_Throw_And_Does_Not_Match()
    {
        var svc = NewSvc();
        svc.Add(@"re:([unclosed");

        // Не должно бросить
        Assert.False(svc.IsWhitelisted(Path.Combine(_root, "whatever.txt")));
    }

    // ---------- Краевые случаи ----------

    [Fact]
    public void Null_Or_Empty_Path_Is_Not_Whitelisted()
    {
        var svc = NewSvc();
        svc.Add("*");

        Assert.False(svc.IsWhitelisted(""));
        Assert.False(svc.IsWhitelisted("   "));
        Assert.False(svc.IsWhitelisted(null!));
    }

    [Fact]
    public void Add_Empty_Rule_Returns_False()
    {
        var svc = NewSvc();
        Assert.False(svc.Add(""));
        Assert.False(svc.Add("   "));
        Assert.Empty(svc.Rules);
    }

    [Fact]
    public void Rules_Persist_Across_Reload()
    {
        var svc1 = NewSvc();
        svc1.Add("*.log");
        svc1.Add(@"re:\.bak$");
        svc1.Add(Path.Combine(_root, "keep.me"));

        var svc2 = NewSvc();
        Assert.Equal(3, svc2.Rules.Count);
        Assert.True(svc2.IsWhitelisted(Path.Combine(_root, "x.log")));
        Assert.True(svc2.IsWhitelisted(Path.Combine(_root, "x.bak")));
        Assert.True(svc2.IsWhitelisted(Path.Combine(_root, "keep.me")));
        Assert.False(svc2.IsWhitelisted(Path.Combine(_root, "other.txt")));
    }
}