using System;
using System.Linq;
using Microsoft.Win32;
using Cleaner.Core.Registry;
using Xunit;

namespace Cleaner.Core.Tests;

public class RegistryHelperTests : IDisposable
{
    private const string Hive = "HKCU";
    private const string RootKey = "Software";
    private readonly string _testKey;

    public RegistryHelperTests()
    {
        _testKey = "CleanerTest_" + Guid.NewGuid().ToString("N");
    }

    public void Dispose()
    {
        try { RegistryHelper.DeleteKey(Hive, $"{RootKey}\\{_testKey}", recursive: true); } catch { }
    }

    private string FullKey(string? suffix = null) =>
        string.IsNullOrEmpty(suffix) ? $"{RootKey}\\{_testKey}" : $"{RootKey}\\{_testKey}\\{suffix}";

    // ---------- ParseHive ----------

    [Theory]
    [InlineData("HKLM", RegistryHive.LocalMachine)]
    [InlineData("hklm", RegistryHive.LocalMachine)]
    [InlineData("HKCU", RegistryHive.CurrentUser)]
    [InlineData("HKEY_LOCAL_MACHINE", RegistryHive.LocalMachine)]
    [InlineData("HKCR", RegistryHive.ClassesRoot)]
    [InlineData("HKU", RegistryHive.Users)]
    [InlineData("HKCC", RegistryHive.CurrentConfig)]
    public void ParseHive_Recognizes_Known_Names(string input, RegistryHive expected)
    {
        Assert.Equal(expected, RegistryHelper.ParseHive(input));
    }

    [Fact]
    public void ParseHive_Throws_On_Unknown()
    {
        Assert.Throws<ArgumentException>(() => RegistryHelper.ParseHive("HKXX"));
    }

    // ---------- KeyExists ----------

    [Fact]
    public void KeyExists_False_For_Missing()
    {
        Assert.False(RegistryHelper.KeyExists(Hive, FullKey()));
    }

    [Fact]
    public void KeyExists_True_After_Create()
    {
        RegistryHelper.SetValue(Hive, FullKey(), "Dummy", 0, RegistryValueKind.DWord, createKeyIfMissing: true);
        Assert.True(RegistryHelper.KeyExists(Hive, FullKey()));
    }

    // ---------- SetValue / GetValue ----------

    [Fact]
    public void Roundtrip_DWord()
    {
        RegistryHelper.SetValue(Hive, FullKey(), "MyDword", 42, RegistryValueKind.DWord, true);
        Assert.Equal(42, RegistryHelper.GetValue(Hive, FullKey(), "MyDword"));
    }

    [Fact]
    public void Roundtrip_String()
    {
        RegistryHelper.SetValue(Hive, FullKey(), "MyStr", "hello", RegistryValueKind.String, true);
        Assert.Equal("hello", RegistryHelper.GetValue(Hive, FullKey(), "MyStr"));
    }

    [Fact]
    public void Roundtrip_QWord()
    {
        long big = long.MaxValue - 7;
        RegistryHelper.SetValue(Hive, FullKey(), "MyQword", big, RegistryValueKind.QWord, true);
        Assert.Equal(big, RegistryHelper.GetValue(Hive, FullKey(), "MyQword"));
    }

    [Fact]
    public void Roundtrip_Binary()
    {
        byte[] data = { 0xDE, 0xAD, 0xBE, 0xEF };
        RegistryHelper.SetValue(Hive, FullKey(), "MyBin", data, RegistryValueKind.Binary, true);
        var read = (byte[])RegistryHelper.GetValue(Hive, FullKey(), "MyBin")!;
        Assert.Equal(data, read);
    }

    [Fact]
    public void Roundtrip_MultiString()
    {
        string[] arr = { "one", "two", "three" };
        RegistryHelper.SetValue(Hive, FullKey(), "MyMulti", arr, RegistryValueKind.MultiString, true);
        var read = (string[])RegistryHelper.GetValue(Hive, FullKey(), "MyMulti")!;
        Assert.Equal(arr, read);
    }

    [Fact]
    public void SetValue_Throws_When_Key_Missing_And_NoCreate()
    {
        Assert.Throws<InvalidOperationException>(() =>
            RegistryHelper.SetValue(Hive, FullKey("nope"), "X", 1, RegistryValueKind.DWord, createKeyIfMissing: false));
    }

    [Fact]
    public void GetValue_Null_For_Missing()
    {
        Assert.Null(RegistryHelper.GetValue(Hive, FullKey(), "NotHere"));
    }

    // ---------- GetValueKind ----------

    [Fact]
    public void GetValueKind_Returns_DWord()
    {
        RegistryHelper.SetValue(Hive, FullKey(), "K", 1, RegistryValueKind.DWord, true);
        Assert.Equal(RegistryValueKind.DWord, RegistryHelper.GetValueKind(Hive, FullKey(), "K"));
    }

    [Fact]
    public void GetValueKind_Null_For_Missing()
    {
        Assert.Null(RegistryHelper.GetValueKind(Hive, FullKey(), "Nope"));
    }

    // ---------- ValueExists ----------

    [Fact]
    public void ValueExists_Correct()
    {
        RegistryHelper.SetValue(Hive, FullKey(), "Present", 1, RegistryValueKind.DWord, true);
        Assert.True(RegistryHelper.ValueExists(Hive, FullKey(), "Present"));
        Assert.False(RegistryHelper.ValueExists(Hive, FullKey(), "Absent"));
    }

    // ---------- DeleteValue ----------

    [Fact]
    public void DeleteValue_Removes_Value()
    {
        RegistryHelper.SetValue(Hive, FullKey(), "ToDelete", 99, RegistryValueKind.DWord, true);
        Assert.True(RegistryHelper.DeleteValue(Hive, FullKey(), "ToDelete"));
        Assert.False(RegistryHelper.ValueExists(Hive, FullKey(), "ToDelete"));
    }

    [Fact]
    public void DeleteValue_Returns_False_If_Missing()
    {
        Assert.False(RegistryHelper.DeleteValue(Hive, FullKey("none"), "X"));
    }

    // ---------- DeleteKey ----------

    [Fact]
    public void DeleteKey_Recursive_Removes_Tree()
    {
        RegistryHelper.SetValue(Hive, FullKey("sub\\a"), "V", 1, RegistryValueKind.DWord, true);
        RegistryHelper.SetValue(Hive, FullKey("sub\\b\\c"), "V", 2, RegistryValueKind.DWord, true);

        Assert.True(RegistryHelper.DeleteKey(Hive, FullKey(), recursive: true));
        Assert.False(RegistryHelper.KeyExists(Hive, FullKey()));
    }

    [Fact]
    public void DeleteKey_Returns_True_On_Missing()
    {
        Assert.True(RegistryHelper.DeleteKey(Hive, FullKey("notthere"), recursive: true));
    }

    // ---------- GetSubKeyNames ----------

    [Fact]
    public void GetSubKeyNames_Returns_Children()
    {
        RegistryHelper.SetValue(Hive, FullKey("alpha"), "V", 1, RegistryValueKind.DWord, true);
        RegistryHelper.SetValue(Hive, FullKey("beta"), "V", 1, RegistryValueKind.DWord, true);
        RegistryHelper.SetValue(Hive, FullKey("gamma"), "V", 1, RegistryValueKind.DWord, true);

        var names = RegistryHelper.GetSubKeyNames(Hive, FullKey());
        Assert.Contains("alpha", names);
        Assert.Contains("beta", names);
        Assert.Contains("gamma", names);
    }

    [Fact]
    public void GetSubKeyNames_Empty_For_Missing()
    {
        Assert.Empty(RegistryHelper.GetSubKeyNames(Hive, FullKey("nothere")));
    }

    // ---------- Case-insensitivity of value names ----------

    [Fact]
    public void Value_Name_Is_Case_Insensitive()
    {
        RegistryHelper.SetValue(Hive, FullKey(), "MixedCase", 7, RegistryValueKind.DWord, true);
        Assert.True(RegistryHelper.ValueExists(Hive, FullKey(), "mixedcase"));
        Assert.True(RegistryHelper.ValueExists(Hive, FullKey(), "MIXEDCASE"));
    }
}