using Microsoft.Win32;

namespace Cleaner.Core.Registry;

public interface IRegistryAccess
{
    object? GetValue(string hive, string key, string name);
    RegistryValueKind? GetValueKind(string hive, string key, string name);
    bool KeyExists(string hive, string key);
    bool ValueExists(string hive, string key, string name);
    void SetValue(string hive, string key, string name, object value, RegistryValueKind kind, bool createKeyIfMissing = false);
    bool DeleteValue(string hive, string key, string name);
    bool DeleteKey(string hive, string key, bool recursive = true);
    IReadOnlyList<string> GetSubKeyNames(string hive, string key);
    IReadOnlyList<string> GetValueNames(string hive, string key);
}

public sealed class WindowsRegistryAccess : IRegistryAccess
{
    public object? GetValue(string hive, string key, string name) => RegistryHelper.GetValue(hive, key, name);
    public RegistryValueKind? GetValueKind(string hive, string key, string name) => RegistryHelper.GetValueKind(hive, key, name);
    public bool KeyExists(string hive, string key) => RegistryHelper.KeyExists(hive, key);
    public bool ValueExists(string hive, string key, string name) => RegistryHelper.ValueExists(hive, key, name);
    public void SetValue(string hive, string key, string name, object value, RegistryValueKind kind, bool createKeyIfMissing = false)
        => RegistryHelper.SetValue(hive, key, name, value, kind, createKeyIfMissing);
    public bool DeleteValue(string hive, string key, string name) => RegistryHelper.DeleteValue(hive, key, name);
    public bool DeleteKey(string hive, string key, bool recursive = true) => RegistryHelper.DeleteKey(hive, key, recursive);
    public IReadOnlyList<string> GetSubKeyNames(string hive, string key) => RegistryHelper.GetSubKeyNames(hive, key);
    public IReadOnlyList<string> GetValueNames(string hive, string key) => RegistryHelper.GetValueNames(hive, key);
}
