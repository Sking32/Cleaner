using System;
using System.IO;
using System.Text.Json;
using Cleaner.Core.Models;
using Cleaner.Core.Registry;
using Cleaner.Core.Services;
using Microsoft.Win32;
using Xunit;

namespace Cleaner.Core.Tests;

public class ActionInterpreterTests : IDisposable
{
    private const string Hive = "HKCU";
    private const string RootKey = "Software\\CleanerTest_Interp";

    private readonly BackupService _backup;
    private readonly ActionInterpreter _interp;
    private readonly string _backupFile;

    public ActionInterpreterTests()
    {
        _backupFile = Path.Combine(Path.GetTempPath(), "cleaner-ai-" + Guid.NewGuid().ToString("N") + ".json");
        _backup = new BackupService(_backupFile);
        _interp = new ActionInterpreter(_backup);
        try { RegistryHelper.DeleteKey(Hive, RootKey, recursive: true); } catch { }
    }

    public void Dispose()
    {
        try { RegistryHelper.DeleteKey(Hive, RootKey, recursive: true); } catch { }
        try { if (File.Exists(_backupFile)) File.Delete(_backupFile); } catch { }
        try { var t = _backupFile + ".tmp"; if (File.Exists(t)) File.Delete(t); } catch { }
    }

    // ---------- helpers ----------

    private static JsonElement Json(object? o) =>
        JsonSerializer.SerializeToElement(o);

    private TweakAction Action(string type, string? name = null, object? value = null, object? expected = null,
                               string? kind = null, bool createKey = false, int? lessOrEq = null)
        => new()
        {
            Type = type,
            Hive = Hive,
            Key = RootKey,
            Name = name,
            Kind = kind,
            Value = value == null ? null : Json(value),
            Expected = expected == null ? null : Json(expected),
            CreateKeyIfMissing = createKey,
            LessOrEqualInt = lessOrEq
        };

    // ---------- regSet ----------

    [Fact]
    public void RegSet_DWord_Writes_Value()
    {
        var r = _interp.Execute(Action("regSet", "Foo", 42, kind: "DWord", createKey: true), "test", "test");
        Assert.True(r.Success, r.Error);
        Assert.Equal(42, RegistryHelper.GetValue(Hive, RootKey, "Foo"));
    }

    [Fact]
    public void RegSet_String_Writes_Value()
    {
        var r = _interp.Execute(Action("regSet", "Foo", "hello", kind: "String", createKey: true), "test", "test");
        Assert.True(r.Success, r.Error);
        Assert.Equal("hello", RegistryHelper.GetValue(Hive, RootKey, "Foo"));
    }

    [Fact]
    public void RegSet_Binary_Parses_Hex_String()
    {
        var r = _interp.Execute(Action("regSet", "Blob", "DEADBEEF", kind: "Binary", createKey: true), "test", "test");
        Assert.True(r.Success, r.Error);
        var bytes = (byte[])RegistryHelper.GetValue(Hive, RootKey, "Blob")!;
        Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, bytes);
    }

    [Fact]
    public void RegSet_Creates_Backup_Entry()
    {
        _interp.Execute(Action("regSet", "BackedUp", 1, kind: "DWord", createKey: true), "tweakX", "perf");
        Assert.Contains("tweakX", _backup.Keys);
    }

    // ---------- regRemove ----------

    [Fact]
    public void RegRemove_Deletes_Value_And_Backs_Up()
    {
        RegistryHelper.SetValue(Hive, RootKey, "ToDel", 7, RegistryValueKind.DWord, true);

        var r = _interp.Execute(Action("regRemove", "ToDel"), "tweakY", "test");
        Assert.True(r.Success, r.Error);
        Assert.False(RegistryHelper.ValueExists(Hive, RootKey, "ToDel"));
        Assert.Contains("tweakY", _backup.Keys);

        _backup.RestoreByKey("tweakY");
        Assert.Equal(7, RegistryHelper.GetValue(Hive, RootKey, "ToDel"));
    }

    // ---------- regDeleteKey ----------

    [Fact]
    public void RegDeleteKey_Removes_Tree()
    {
        RegistryHelper.SetValue(Hive, RootKey + "\\sub", "V", 1, RegistryValueKind.DWord, true);

        var r = _interp.Execute(new TweakAction { Type = "regDeleteKey", Hive = Hive, Key = RootKey + "\\sub" }, "t", "t");
        Assert.True(r.Success, r.Error);
        Assert.False(RegistryHelper.KeyExists(Hive, RootKey + "\\sub"));
    }

    // ---------- regSetAllSubKeys ----------

    [Fact]
    public void RegSetAllSubKeys_Writes_To_Each_Child()
    {
        RegistryHelper.SetValue(Hive, RootKey + "\\a", "Dummy", 0, RegistryValueKind.DWord, true);
        RegistryHelper.SetValue(Hive, RootKey + "\\b", "Dummy", 0, RegistryValueKind.DWord, true);
        RegistryHelper.SetValue(Hive, RootKey + "\\c", "Dummy", 0, RegistryValueKind.DWord, true);

        var r = _interp.Execute(
            Action("regSetAllSubKeys", "Marker", 99, kind: "DWord"),
            "tweakZ", "test");
        Assert.True(r.Success, r.Error);

        Assert.Equal(99, RegistryHelper.GetValue(Hive, RootKey + "\\a", "Marker"));
        Assert.Equal(99, RegistryHelper.GetValue(Hive, RootKey + "\\b", "Marker"));
        Assert.Equal(99, RegistryHelper.GetValue(Hive, RootKey + "\\c", "Marker"));
    }

    // ---------- regRemoveAllSubKeys ----------

    [Fact]
    public void RegRemoveAllSubKeys_Deletes_From_Each_Child()
    {
        RegistryHelper.SetValue(Hive, RootKey + "\\x", "N", 5, RegistryValueKind.DWord, true);
        RegistryHelper.SetValue(Hive, RootKey + "\\y", "N", 5, RegistryValueKind.DWord, true);

        var r = _interp.Execute(Action("regRemoveAllSubKeys", "N"), "t", "t");
        Assert.True(r.Success, r.Error);

        Assert.False(RegistryHelper.ValueExists(Hive, RootKey + "\\x", "N"));
        Assert.False(RegistryHelper.ValueExists(Hive, RootKey + "\\y", "N"));
    }

    // ---------- regSetDynamicDate ----------

    [Fact]
    public void RegSetDynamicDate_Writes_Date_In_Future()
    {
        var action = new TweakAction
        {
            Type = "regSetDynamicDate",
            Hive = Hive,
            Key = RootKey,
            Name = "Expiry",
            DaysFromNow = 35,
            Format = "yyyy-MM-ddTHH:mm:ssZ",
            CreateKeyIfMissing = true
        };

        var r = _interp.Execute(action, "t", "t");
        Assert.True(r.Success, r.Error);

        var raw = (string?)RegistryHelper.GetValue(Hive, RootKey, "Expiry");
        Assert.NotNull(raw);
        var dt = DateTime.Parse(raw!);
        Assert.True(dt > DateTime.UtcNow.AddDays(30));
    }

    // ---------- commandRun / commandGet ----------

    [Fact]
    public void CommandRun_Echo_Succeeds()
    {
        var action = new TweakAction { Type = "commandRun", FileName = "cmd.exe", Arguments = "/c echo hi" };
        var r = _interp.Execute(action, "t", "t");
        Assert.True(r.Success, r.Error);
    }

    [Fact]
    public void CommandGet_Detects_Output()
    {
        var action = new TweakAction
        {
            Type = "commandGet",
            FileName = "cmd.exe",
            Arguments = "/c echo HelloMarker",
            OutputContains = "HelloMarker"
        };
        var r = _interp.Check(action);
        Assert.True(r.Matches, r.Reason);
    }

    [Fact]
    public void CommandGet_Fails_When_Not_Contains()
    {
        var action = new TweakAction
        {
            Type = "commandGet",
            FileName = "cmd.exe",
            Arguments = "/c echo hi",
            OutputContains = "NotThere"
        };
        var r = _interp.Check(action);
        Assert.False(r.Matches);
    }

    // ---------- CHECK: regGet ----------

    [Fact]
    public void Check_RegGet_Matches_DWord()
    {
        RegistryHelper.SetValue(Hive, RootKey, "V", 1, RegistryValueKind.DWord, true);
        Assert.True(_interp.Check(Action("regGet", "V", expected: 1, kind: "DWord")).Matches);
        Assert.False(_interp.Check(Action("regGet", "V", expected: 2, kind: "DWord")).Matches);
    }

    [Fact]
    public void Check_RegGet_Matches_String()
    {
        RegistryHelper.SetValue(Hive, RootKey, "S", "Deny", RegistryValueKind.String, true);
        Assert.True(_interp.Check(Action("regGet", "S", expected: "Deny", kind: "String")).Matches);
        Assert.False(_interp.Check(Action("regGet", "S", expected: "Allow", kind: "String")).Matches);
    }

    [Fact]
    public void Check_RegGet_LessOrEqual()
    {
        RegistryHelper.SetValue(Hive, RootKey, "N", 30, RegistryValueKind.DWord, true);
        Assert.True(_interp.Check(Action("regGet", "N", kind: "DWord", lessOrEq: 50)).Matches);
        Assert.False(_interp.Check(Action("regGet", "N", kind: "DWord", lessOrEq: 20)).Matches);
    }

    [Fact]
    public void Check_RegKeyExists()
    {
        RegistryHelper.SetValue(Hive, RootKey, "X", 1, RegistryValueKind.DWord, true);
        var a = new TweakAction { Type = "regKeyExists", Hive = Hive, Key = RootKey };
        Assert.True(_interp.Check(a).Matches);

        var missing = new TweakAction { Type = "regKeyExists", Hive = Hive, Key = RootKey + "\\nope" };
        Assert.False(_interp.Check(missing).Matches);
    }

    [Fact]
    public void Check_RegNotExists()
    {
        var a = new TweakAction { Type = "regNotExists", Hive = Hive, Key = RootKey, Name = "NotHere" };
        Assert.True(_interp.Check(a).Matches);

        RegistryHelper.SetValue(Hive, RootKey, "Exists", 1, RegistryValueKind.DWord, true);
        var b = new TweakAction { Type = "regNotExists", Hive = Hive, Key = RootKey, Name = "Exists" };
        Assert.False(_interp.Check(b).Matches);
    }

    // ---------- unknown type ----------

    [Fact]
    public void Execute_Unknown_Type_Fails()
    {
        var r = _interp.Execute(new TweakAction { Type = "gibberish" }, "t", "t");
        Assert.False(r.Success);
        Assert.Contains("unknown", r.Error);
    }

    [Fact]
    public void Check_Unknown_Type_Fails()
    {
        var r = _interp.Check(new TweakAction { Type = "gibberish" });
        Assert.False(r.Matches);
    }

    // ---------- ParseHexBytes ----------

    [Theory]
    [InlineData("DEADBEEF", new byte[] { 0xDE, 0xAD, 0xBE, 0xEF })]
    [InlineData("de ad be ef", new byte[] { 0xDE, 0xAD, 0xBE, 0xEF })]
    [InlineData("DE,AD,BE,EF", new byte[] { 0xDE, 0xAD, 0xBE, 0xEF })]
    public void ParseHexBytes_Parses_Formats(string input, byte[] expected)
    {
        Assert.Equal(expected, ActionInterpreter.ParseHexBytes(input));
    }

    [Fact]
    public void ParseHexBytes_Throws_On_Odd_Length()
    {
        Assert.Throws<ArgumentException>(() => ActionInterpreter.ParseHexBytes("ABC"));
    }
}