using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace Cleaner.Core.Registry;

/// <summary>
/// Обёртка над Microsoft.Win32.Registry.
/// Принимает hive как строку (HKLM/HKCU/HKCR/HKU/HKCC), читает/пишет значения по kind.
/// </summary>
public static class RegistryHelper
{
    public static RegistryHive ParseHive(string hive) => hive.ToUpperInvariant() switch
    {
        "HKLM" or "HKEY_LOCAL_MACHINE" => RegistryHive.LocalMachine,
        "HKCU" or "HKEY_CURRENT_USER" => RegistryHive.CurrentUser,
        "HKCR" or "HKEY_CLASSES_ROOT" => RegistryHive.ClassesRoot,
        "HKU" or "HKEY_USERS" => RegistryHive.Users,
        "HKCC" or "HKEY_CURRENT_CONFIG" => RegistryHive.CurrentConfig,
        _ => throw new ArgumentException($"Unknown registry hive: {hive}")
    };

    public static RegistryKey OpenBaseKey(RegistryHive hive) =>
        RegistryKey.OpenBaseKey(hive, RegistryView.Default);

    public static bool KeyExists(string hive, string subKey)
    {
        using var baseKey = OpenBaseKey(ParseHive(hive));
        using var key = baseKey.OpenSubKey(subKey, writable: false);
        return key != null;
    }

    public static bool ValueExists(string hive, string subKey, string name)
    {
        using var baseKey = OpenBaseKey(ParseHive(hive));
        using var key = baseKey.OpenSubKey(subKey, writable: false);
        if (key == null) return false;

        var target = NormalizeName(name);
        return key.GetValueNames().Contains(target, StringComparer.OrdinalIgnoreCase);
    }

    public static object? GetValue(string hive, string subKey, string name)
    {
        using var baseKey = OpenBaseKey(ParseHive(hive));
        using var key = baseKey.OpenSubKey(subKey, writable: false);
        if (key == null) return null;
        return key.GetValue(NormalizeName(name));
    }

    /// <summary>Получить kind значения, или null, если значения нет.</summary>
    public static RegistryValueKind? GetValueKind(string hive, string subKey, string name)
    {
        using var baseKey = OpenBaseKey(ParseHive(hive));
        using var key = baseKey.OpenSubKey(subKey, writable: false);
        if (key == null) return null;
        try { return key.GetValueKind(NormalizeName(name)); }
        catch { return null; }
    }

    public static void SetValue(
        string hive,
        string subKey,
        string name,
        object value,
        RegistryValueKind kind,
        bool createKeyIfMissing = false)
    {
        using var baseKey = OpenBaseKey(ParseHive(hive));
        using var key = createKeyIfMissing
            ? baseKey.CreateSubKey(subKey, writable: true)
            : baseKey.OpenSubKey(subKey, writable: true);

        if (key == null)
            throw new InvalidOperationException($"Registry key not found: {hive}\\{subKey}");

        key.SetValue(NormalizeName(name), value, kind);
    }

    public static bool DeleteValue(string hive, string subKey, string name)
    {
        using var baseKey = OpenBaseKey(ParseHive(hive));
        using var key = baseKey.OpenSubKey(subKey, writable: true);
        if (key == null) return false;

        var target = NormalizeName(name);
        if (!key.GetValueNames().Contains(target, StringComparer.OrdinalIgnoreCase))
            return false;

        key.DeleteValue(target, throwOnMissingValue: false);
        return true;
    }

    public static bool DeleteKey(string hive, string subKey, bool recursive = true)
    {
        using var baseKey = OpenBaseKey(ParseHive(hive));
        try
        {
            if (recursive)
                baseKey.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
            else
                baseKey.DeleteSubKey(subKey, throwOnMissingSubKey: false);
            return true;
        }
        catch { return false; }
    }

    public static IReadOnlyList<string> GetSubKeyNames(string hive, string subKey)
    {
        using var baseKey = OpenBaseKey(ParseHive(hive));
        using var key = baseKey.OpenSubKey(subKey, writable: false);
        if (key == null) return Array.Empty<string>();
        return key.GetSubKeyNames();
    }

    /// <summary>
    /// Возвращает имена значений в указанной ветке (включая "" для (Default)).
    /// Пустой массив, если ветки нет.
    /// </summary>
    public static IReadOnlyList<string> GetValueNames(string hive, string subKey)
    {
        using var baseKey = OpenBaseKey(ParseHive(hive));
        using var key = baseKey.OpenSubKey(subKey, writable: false);
        if (key == null) return Array.Empty<string>();
        return key.GetValueNames();
    }

    private static string NormalizeName(string name) =>
        string.IsNullOrEmpty(name) ? "" : name;
}