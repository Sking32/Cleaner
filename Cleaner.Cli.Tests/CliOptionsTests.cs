using System;
using System.IO;
using Cleaner.Cli;
using Xunit;

namespace Cleaner.Cli.Tests;

public class CliOptionsTests
{
    // ---------- базовое ----------

    [Fact]
    public void Empty_Args_Shows_Help_Via_Validate()
    {
        var opts = CliOptions.Parse(Array.Empty<string>());
        Assert.False(opts.ShowHelp);
        Assert.NotNull(opts.Validate());
    }

    [Fact]
    public void Help_Flag()
    {
        Assert.True(CliOptions.Parse(new[] { "--help" }).ShowHelp);
        Assert.True(CliOptions.Parse(new[] { "-h" }).ShowHelp);
        Assert.True(CliOptions.Parse(new[] { "/?" }).ShowHelp);
    }

    // ---------- preset ----------

    [Fact]
    public void Preset_Parsed()
    {
        var opts = CliOptions.Parse(new[] { "--preset", "soft" });
        Assert.Equal("soft", opts.Preset);
        Assert.Null(opts.Validate());
    }

    [Fact]
    public void Preset_Empty_Is_Treated_As_Null()
    {
        var opts = CliOptions.Parse(new[] { "--preset" });
        Assert.Null(opts.Preset);
    }

    // ---------- operations ----------

    [Fact]
    public void Operations_Comma_Separated()
    {
        var opts = CliOptions.Parse(new[] { "--operations", "temp_user,temp_sys,dns" });
        Assert.Equal(3, opts.Operations.Count);
        Assert.Contains("temp_user", opts.Operations);
        Assert.Contains("dns", opts.Operations);
    }

    [Fact]
    public void Operations_Semicolon_Also_Works()
    {
        var opts = CliOptions.Parse(new[] { "--operations", "a;b;c" });
        Assert.Equal(3, opts.Operations.Count);
    }

    [Fact]
    public void Operations_Trim_Whitespace()
    {
        var opts = CliOptions.Parse(new[] { "--operations", " a , b , c " });
        Assert.Equal(3, opts.Operations.Count);
        Assert.Contains("a", opts.Operations);
        Assert.Contains("b", opts.Operations);
        Assert.Contains("c", opts.Operations);
    }

    [Fact]
    public void Operations_Alias_Ops_Works()
    {
        var opts = CliOptions.Parse(new[] { "--ops", "a,b" });
        Assert.Equal(2, opts.Operations.Count);
    }

    // ---------- флаги ----------

    [Fact]
    public void Flags_Parsed()
    {
        var opts = CliOptions.Parse(new[]
        {
            "--preset", "std",
            "--dry-run", "--silent", "--json", "--no-relaunch"
        });
        Assert.True(opts.DryRun);
        Assert.True(opts.Silent);
        Assert.True(opts.Json);
        Assert.True(opts.NoRelaunch);
    }

    [Fact]
    public void Quiet_Flag_Parsed()
    {
        var opts = CliOptions.Parse(new[] { "--preset", "soft", "--quiet" });
        Assert.True(opts.Quiet);
    }

    // ---------- log / webhook ----------

    [Fact]
    public void Log_And_Webhook_Parsed()
    {
        var opts = CliOptions.Parse(new[]
        {
            "--preset", "soft",
            "--log", @"C:\logs\x.log",
            "--webhook", "https://example.com/hook"
        });
        Assert.Equal(@"C:\logs\x.log", opts.LogPath);
        Assert.Equal("https://example.com/hook", opts.WebhookUrl);
    }

    // ---------- конфликты ----------

    [Fact]
    public void Preset_And_Operations_Conflict()
    {
        var opts = CliOptions.Parse(new[] { "--preset", "soft", "--operations", "dns" });
        Assert.NotNull(opts.Validate());
    }

    [Fact]
    public void No_Action_Is_Error()
    {
        var opts = CliOptions.Parse(new[] { "--dry-run" });
        Assert.NotNull(opts.Validate());
    }

    // ---------- merge-tools ----------

    [Fact]
    public void MergeTweaks_Parsed_With_Positional()
    {
        var opts = CliOptions.Parse(new[] { "--merge-tweaks", "a.json", "b.json" });
        Assert.True(opts.MergeTweaks);
        Assert.Equal(2, opts.Positional.Count);
        Assert.Null(opts.Validate());
    }

    [Fact]
    public void MergeTweaks_Requires_Two_Positional()
    {
        var opts = CliOptions.Parse(new[] { "--merge-tweaks", "a.json" });
        Assert.NotNull(opts.Validate());
    }

    [Fact]
    public void MergeStrings_And_MergeOperations_Flags()
    {
        Assert.True(CliOptions.Parse(new[] { "--merge-strings", "a", "b" }).MergeStrings);
        Assert.True(CliOptions.Parse(new[] { "--merge-operations", "a", "b" }).MergeOperations);
    }

    // ---------- list-operations ----------

    [Fact]
    public void List_Operations_Flag()
    {
        var opts = CliOptions.Parse(new[] { "--list-operations" });
        Assert.True(opts.ListOperations);
        Assert.Null(opts.Validate());
    }

    // ---------- ignored unknown ----------

    [Fact]
    public void Unknown_Flags_Are_Ignored()
    {
        var opts = CliOptions.Parse(new[] { "--preset", "soft", "--gibberish" });
        Assert.Equal("soft", opts.Preset);
        Assert.Empty(opts.Positional);
    }

    // ---------- OutputWriter ----------

    [Fact]
    public void OutputWriter_Json_Emits_Valid_Json()
    {
        var sw = new StringWriter();
        using var w = new OutputWriter(sw, logPath: null, quiet: false);
        w.WriteJson(new { a = 1, b = "x" });
        var s = sw.ToString();
        Assert.Contains("\"a\"", s);
        Assert.Contains("\"b\"", s);
    }

    [Fact]
    public void OutputWriter_Quiet_Suppresses_Line()
    {
        var sw = new StringWriter();
        using var w = new OutputWriter(sw, logPath: null, quiet: true);
        w.Line("should be suppressed");
        Assert.DoesNotContain("should be suppressed", sw.ToString());
    }

    [Fact]
    public void OutputWriter_Important_Always_Emits()
    {
        var sw = new StringWriter();
        using var w = new OutputWriter(sw, logPath: null, quiet: true);
        w.Important("always");
        Assert.Contains("always", sw.ToString());
    }

    // ---------- FormatBytes ----------

    [Fact]
    public void FormatBytes_Works()
    {
        Assert.Equal("0 B", CliRunner.FormatBytes(0));
        Assert.Equal("1.00 KB", CliRunner.FormatBytes(1024));
        Assert.Equal("1.00 MB", CliRunner.FormatBytes(1024 * 1024));
        Assert.Equal("1.00 GB", CliRunner.FormatBytes(1024L * 1024 * 1024));
    }
}