using System;
using System.IO;
using System.Linq;
using Cleaner.Core.Registry;
using Cleaner.Core.Services;
using Microsoft.Win32;
using Xunit;

namespace Cleaner.Core.Tests;

public class BackupServiceTests : IDisposable
{
    private const string Hive = "HKCU";
    private const string TestRoot = "Software\\CleanerTest_Backup";
    private readonly string _backupFile;
    private readonly BackupService _svc;

    public BackupServiceTests()
    {
        _backupFile = Path.Combine(Path.GetTempPath(), "cleaner-backup-" + Guid.NewGuid().ToString("N") + ".json");
        _svc = new BackupService(_backupFile);
        try { RegistryHelper.DeleteKey(Hive, TestRoot, recursive: true); } catch { }
    }

    public void Dispose()
    {
        try { RegistryHelper.DeleteKey(Hive, TestRoot, recursive: true); } catch { }
        try { if (File.Exists(_backupFile)) File.Delete(_backupFile); } catch { }
        try { var tmp = _backupFile + ".tmp"; if (File.Exists(tmp)) File.Delete(tmp); } catch { }
    }

    [Fact]
    public void Saves_And_Restores_DWord()
    {
        RegistryHelper.SetValue(Hive, TestRoot, "Flag", 1, RegistryValueKind.DWord, true);
        _svc.SaveBeforeChange("flag", Hive, TestRoot, "Flag", "test");

        RegistryHelper.SetValue(Hive, TestRoot, "Flag", 0, RegistryValueKind.DWord, false);

        Assert.True(_svc.RestoreByKey("flag"));
        Assert.Equal(1, RegistryHelper.GetValue(Hive, TestRoot, "Flag"));
    }

    [Fact]
    public void Saves_And_Restores_String()
    {
        RegistryHelper.SetValue(Hive, TestRoot, "Name", "original", RegistryValueKind.String, true);
        _svc.SaveBeforeChange("name", Hive, TestRoot, "Name");

        RegistryHelper.SetValue(Hive, TestRoot, "Name", "modified", RegistryValueKind.String, false);
        Assert.True(_svc.RestoreByKey("name"));
        Assert.Equal("original", RegistryHelper.GetValue(Hive, TestRoot, "Name"));
    }

    [Fact]
    public void Saves_And_Restores_Binary()
    {
        byte[] original = { 0x01, 0x02, 0x03 };
        RegistryHelper.SetValue(Hive, TestRoot, "Blob", original, RegistryValueKind.Binary, true);
        _svc.SaveBeforeChange("blob", Hive, TestRoot, "Blob");

        byte[] changed = { 0xFF, 0xFF };
        RegistryHelper.SetValue(Hive, TestRoot, "Blob", changed, RegistryValueKind.Binary, false);

        Assert.True(_svc.RestoreByKey("blob"));
        var restored = (byte[])RegistryHelper.GetValue(Hive, TestRoot, "Blob")!;
        Assert.Equal(original, restored);
    }

    [Fact]
    public void Save_Is_Idempotent()
    {
        RegistryHelper.SetValue(Hive, TestRoot, "X", 100, RegistryValueKind.DWord, true);
        _svc.SaveBeforeChange("x", Hive, TestRoot, "X");

        RegistryHelper.SetValue(Hive, TestRoot, "X", 999, RegistryValueKind.DWord, false);
        _svc.SaveBeforeChange("x", Hive, TestRoot, "X");

        Assert.True(_svc.RestoreByKey("x"));
        Assert.Equal(100, RegistryHelper.GetValue(Hive, TestRoot, "X"));
    }

    [Fact]
    public void Saves_Remove_When_Value_Missing()
    {
        _svc.SaveBeforeChange("missing", Hive, TestRoot, "NoSuchValue");

        RegistryHelper.SetValue(Hive, TestRoot, "NoSuchValue", 5, RegistryValueKind.DWord, true);
        Assert.True(RegistryHelper.ValueExists(Hive, TestRoot, "NoSuchValue"));

        Assert.True(_svc.RestoreByKey("missing"));
        Assert.False(RegistryHelper.ValueExists(Hive, TestRoot, "NoSuchValue"));
    }

    [Fact]
    public void RestoreByKey_Returns_False_For_Unknown()
    {
        Assert.False(_svc.RestoreByKey("this-does-not-exist"));
    }

    [Fact]
    public void RestoreAll_Restores_Everything()
    {
        RegistryHelper.SetValue(Hive, TestRoot, "A", 1, RegistryValueKind.DWord, true);
        RegistryHelper.SetValue(Hive, TestRoot, "B", "one", RegistryValueKind.String, true);
        _svc.SaveBeforeChange("a", Hive, TestRoot, "A");
        _svc.SaveBeforeChange("b", Hive, TestRoot, "B");

        RegistryHelper.SetValue(Hive, TestRoot, "A", 99, RegistryValueKind.DWord, false);
        RegistryHelper.SetValue(Hive, TestRoot, "B", "changed", RegistryValueKind.String, false);

        var (restored, failed) = _svc.RestoreAll();
        Assert.Equal(2, restored);
        Assert.Equal(0, failed);
        Assert.Equal(1, RegistryHelper.GetValue(Hive, TestRoot, "A"));
        Assert.Equal("one", RegistryHelper.GetValue(Hive, TestRoot, "B"));
        Assert.Empty(_svc.Keys);
    }

    [Fact]
    public void RestoreAll_Empty_Backup_Returns_Zero()
    {
        var (restored, failed) = _svc.RestoreAll();
        Assert.Equal(0, restored);
        Assert.Equal(0, failed);
    }

    [Fact]
    public void Backup_Survives_Reload()
    {
        RegistryHelper.SetValue(Hive, TestRoot, "Persist", 42, RegistryValueKind.DWord, true);
        _svc.SaveBeforeChange("persist", Hive, TestRoot, "Persist");

        var svc2 = new BackupService(_backupFile);
        Assert.Contains("persist", svc2.Keys);

        RegistryHelper.SetValue(Hive, TestRoot, "Persist", 0, RegistryValueKind.DWord, false);
        Assert.True(svc2.RestoreByKey("persist"));
        Assert.Equal(42, RegistryHelper.GetValue(Hive, TestRoot, "Persist"));
    }

    [Fact]
    public void Clear_Empties_Backup()
    {
        RegistryHelper.SetValue(Hive, TestRoot, "X", 1, RegistryValueKind.DWord, true);
        _svc.SaveBeforeChange("x", Hive, TestRoot, "X");
        Assert.NotEmpty(_svc.Keys);

        _svc.Clear();
        Assert.Empty(_svc.Keys);
    }

    [Fact]
    public void SaveServiceBeforeChange_Records_StartType()
    {
        _svc.SaveServiceBeforeChange("svc_spooler", "Spooler", "Automatic", "test");
        Assert.Contains("svc_spooler", _svc.Keys);

        var entry = _svc.Entries["svc_spooler"];
        Assert.Equal("Service", entry.Type);
        Assert.Equal("Spooler", entry.Name);
    }

    [Fact]
    public void ExportRegToFile_Creates_File()
    {
        RegistryHelper.SetValue(Hive, TestRoot, "E1", 5, RegistryValueKind.DWord, true);
        RegistryHelper.SetValue(Hive, TestRoot, "E2", "hello", RegistryValueKind.String, true);
        _svc.SaveBeforeChange("e1", Hive, TestRoot, "E1");
        _svc.SaveBeforeChange("e2", Hive, TestRoot, "E2");

        var regPath = Path.Combine(Path.GetTempPath(), "cleaner-test-" + Guid.NewGuid().ToString("N") + ".reg");
        try
        {
            var result = _svc.ExportRegToFile(regPath);
            Assert.NotNull(result);
            Assert.True(File.Exists(regPath));

            var content = File.ReadAllText(regPath);
            Assert.Contains("Windows Registry Editor Version 5.00", content);
            Assert.Contains("E1", content);
            Assert.Contains("E2", content);
            Assert.Contains("HKEY_CURRENT_USER", content);
        }
        finally
        {
            try { if (File.Exists(regPath)) File.Delete(regPath); } catch { }
        }
    }

    [Fact]
    public void ExportRegToFile_Empty_Backup_Produces_Header_Only()
    {
        var regPath = Path.Combine(Path.GetTempPath(), "cleaner-empty-" + Guid.NewGuid().ToString("N") + ".reg");
        try
        {
            var result = _svc.ExportRegToFile(regPath);
            Assert.NotNull(result);
            var content = File.ReadAllText(regPath);
            Assert.Contains("Windows Registry Editor Version 5.00", content);
        }
        finally
        {
            try { if (File.Exists(regPath)) File.Delete(regPath); } catch { }
        }
    }
}