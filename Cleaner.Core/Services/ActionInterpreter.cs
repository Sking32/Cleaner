using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cleaner.Core.Models;
using Cleaner.Core.Registry;
using Microsoft.Win32;

namespace Cleaner.Core.Services;

public readonly record struct ActionExecResult(bool Success, string? Error = null);
public readonly record struct StateCheckResult(bool Matches, string? Reason = null);

public interface IActionInterpreter
{
    /// <summary>Выполнить действие (apply или unapply — неважно).</summary>
    ActionExecResult Execute(TweakAction action, string tweakKey, string category);

    /// <summary>Проверить, соответствует ли система ожидаемому состоянию.</summary>
    StateCheckResult Check(TweakAction action);
}

public sealed class ActionInterpreter : IActionInterpreter
{
    private readonly IBackupService _backup;

    public ActionInterpreter(IBackupService backup)
    {
        _backup = backup ?? throw new ArgumentNullException(nameof(backup));
    }

    // ============================================================
    //  EXECUTE
    // ============================================================

    public ActionExecResult Execute(TweakAction action, string tweakKey, string category)
    {
        if (action == null) return new ActionExecResult(false, "action is null");

        try
        {
            return action.Type switch
            {
                "regSet" => RegSet(action, tweakKey, category),
                "regRemove" => RegRemove(action, tweakKey, category),
                "regRemoveWithBackup" => RegRemoveWithBackup(action, tweakKey, category),
                "regRestoreFromBackup" => RegRestoreFromBackup(action, tweakKey),
                "regDeleteKey" => RegDeleteKey(action),
                "regSetAllSubKeys" => RegSetAllSubKeys(action, tweakKey, category),
                "regRemoveAllSubKeys" => RegRemoveAllSubKeys(action, tweakKey, category),
                "regSetDynamicDate" => RegSetDynamicDate(action, tweakKey, category),
                "serviceSet" => ServiceSet(action, tweakKey, category),
                "commandRun" => CommandRun(action),
                "restartExplorer" => RestartExplorer(),
                _ => new ActionExecResult(false, $"unknown action type: {action.Type}")
            };
        }
        catch (Exception ex)
        {
            return new ActionExecResult(false, ex.Message);
        }
    }

    // ============================================================
    //  CHECK (state)
    // ============================================================

    public StateCheckResult Check(TweakAction action)
    {
        if (action == null) return new StateCheckResult(false, "action is null");

        try
        {
            return action.Type switch
            {
                "regGet" => RegGet(action),
                "regKeyExists" => RegKeyExists(action),
                "regNotExists" => RegNotExists(action),
                "regGetAllSubKeys" => RegGetAllSubKeys(action),
                "regGetDateInFuture" => RegGetDateInFuture(action),
                "serviceGet" => ServiceGet(action),
                "commandGet" => CommandGet(action),
                _ => new StateCheckResult(false, $"unknown state type: {action.Type}")
            };
        }
        catch (Exception ex)
        {
            return new StateCheckResult(false, ex.Message);
        }
    }

    // ============================================================
    //  ACTIONS
    // ============================================================

    private ActionExecResult RegSet(TweakAction a, string tweakKey, string category)
    {
        RequireHiveKey(a);

        _backup.SaveBeforeChange(tweakKey, a.Hive!, a.Key!, a.Name ?? "", category);

        var kind = ParseKind(a.Kind);
        var value = ConvertJsonValue(a.Value, kind);
        RegistryHelper.SetValue(a.Hive!, a.Key!, a.Name ?? "", value, kind, a.CreateKeyIfMissing);
        return new ActionExecResult(true);
    }

    private ActionExecResult RegRemove(TweakAction a, string tweakKey, string category)
    {
        RequireHiveKey(a);

        _backup.SaveBeforeChange(tweakKey, a.Hive!, a.Key!, a.Name ?? "", category);
        RegistryHelper.DeleteValue(a.Hive!, a.Key!, a.Name ?? "");
        return new ActionExecResult(true);
    }

    private ActionExecResult RegRemoveWithBackup(TweakAction a, string tweakKey, string category)
    {
        RequireHiveKey(a);
        _backup.SaveBeforeChange(tweakKey, a.Hive!, a.Key!, a.Name ?? "", category);
        RegistryHelper.DeleteValue(a.Hive!, a.Key!, a.Name ?? "");
        return new ActionExecResult(true);
    }

    private ActionExecResult RegRestoreFromBackup(TweakAction a, string tweakKey)
    {
        var backupKey = string.IsNullOrEmpty(a.BackupKey) ? tweakKey : a.BackupKey!;
        var ok = _backup.RestoreByKey(backupKey);
        return ok
            ? new ActionExecResult(true)
            : new ActionExecResult(false, $"backup entry not found: {backupKey}");
    }

    private ActionExecResult RegDeleteKey(TweakAction a)
    {
        RequireHiveKey(a);
        // Для ClassicContext. Бэкап ветки не сохраняем — это редкий edge case.
        RegistryHelper.DeleteKey(a.Hive!, a.Key!, recursive: true);
        return new ActionExecResult(true);
    }

    private ActionExecResult RegSetAllSubKeys(TweakAction a, string tweakKey, string category)
    {
        RequireHiveKey(a);
        var kind = ParseKind(a.Kind);
        var value = ConvertJsonValue(a.Value, kind);

        var subs = RegistryHelper.GetSubKeyNames(a.Hive!, a.Key!);
        foreach (var sub in subs)
        {
            var full = $"{a.Key}\\{sub}";
            _backup.SaveBeforeChange($"{tweakKey}_{sub}", a.Hive!, full, a.Name ?? "", category);
            RegistryHelper.SetValue(a.Hive!, full, a.Name ?? "", value, kind, createKeyIfMissing: false);
        }
        return new ActionExecResult(true);
    }

    private ActionExecResult RegRemoveAllSubKeys(TweakAction a, string tweakKey, string category)
    {
        RequireHiveKey(a);
        var subs = RegistryHelper.GetSubKeyNames(a.Hive!, a.Key!);
        foreach (var sub in subs)
        {
            var full = $"{a.Key}\\{sub}";
            _backup.SaveBeforeChange($"{tweakKey}_{sub}", a.Hive!, full, a.Name ?? "", category);
            RegistryHelper.DeleteValue(a.Hive!, full, a.Name ?? "");
        }
        return new ActionExecResult(true);
    }

    private ActionExecResult RegSetDynamicDate(TweakAction a, string tweakKey, string category)
    {
        RequireHiveKey(a);
        var days = a.DaysFromNow ?? 0;
        var format = string.IsNullOrEmpty(a.Format) ? "yyyy-MM-ddTHH:mm:ssZ" : a.Format!;
        var dateStr = DateTime.UtcNow.AddDays(days).ToString(format, CultureInfo.InvariantCulture);

        _backup.SaveBeforeChange(tweakKey, a.Hive!, a.Key!, a.Name ?? "", category);
        RegistryHelper.SetValue(a.Hive!, a.Key!, a.Name ?? "", dateStr, RegistryValueKind.String, a.CreateKeyIfMissing);
        return new ActionExecResult(true);
    }

    private ActionExecResult ServiceSet(TweakAction a, string tweakKey, string category)
    {
        if (string.IsNullOrEmpty(a.ServiceName))
            return new ActionExecResult(false, "serviceName is required");
        if (!ServiceHelper.Exists(a.ServiceName!))
            return new ActionExecResult(false, $"service not found: {a.ServiceName}");

        var current = ServiceHelper.GetStartType(a.ServiceName!);
        if (current != null)
            _backup.SaveServiceBeforeChange($"svc_{a.ServiceName}", a.ServiceName!, current, category);

        if (a.StopFirst)
            ServiceHelper.Stop(a.ServiceName!);

        var ok = ServiceHelper.SetStartType(a.ServiceName!, a.StartupType ?? "Automatic");
        return ok ? new ActionExecResult(true) : new ActionExecResult(false, "sc.exe config failed");
    }

    private ActionExecResult CommandRun(TweakAction a)
    {
        if (string.IsNullOrEmpty(a.FileName))
            return new ActionExecResult(false, "fileName is required");

        var result = CommandRunner.Run(a.FileName!, a.Arguments, timeoutMs: 120000);
        return result.Success
            ? new ActionExecResult(true)
            : new ActionExecResult(false, $"exit={result.ExitCode}: {result.StdErr.Trim()}");
    }

    private ActionExecResult RestartExplorer()
    {
        ExplorerRestarter.Restart();
        return new ActionExecResult(true);
    }

    // ============================================================
    //  CHECKS
    // ============================================================

    private StateCheckResult RegGet(TweakAction a)
    {
        RequireHiveKey(a);

        var raw = RegistryHelper.GetValue(a.Hive!, a.Key!, a.Name ?? "");
        if (raw == null) return new StateCheckResult(false, "value not found");

        // lessOrEqualInt — особый случай (MenuShowDelay, ScanAvgCPULoadFactor)
        if (a.LessOrEqualInt.HasValue)
        {
            try
            {
                var n = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
                return n <= a.LessOrEqualInt.Value
                    ? new StateCheckResult(true)
                    : new StateCheckResult(false, $"{n} > {a.LessOrEqualInt}");
            }
            catch { return new StateCheckResult(false, "cannot convert to int"); }
        }

        if (a.Expected == null)
            return new StateCheckResult(false, "expected not provided");

        var expectedJson = a.Expected.Value;
        var kind = ParseKind(a.Kind);

        // Ожидаемое может быть числом, строкой или hex-строкой (Binary)
        bool matches = kind switch
        {
            RegistryValueKind.DWord when expectedJson.ValueKind == JsonValueKind.Number =>
                Convert.ToInt32(raw, CultureInfo.InvariantCulture) == expectedJson.GetInt32(),

            RegistryValueKind.QWord when expectedJson.ValueKind == JsonValueKind.Number =>
                Convert.ToInt64(raw, CultureInfo.InvariantCulture) == expectedJson.GetInt64(),

            RegistryValueKind.String or RegistryValueKind.ExpandString when expectedJson.ValueKind == JsonValueKind.String =>
                string.Equals(Convert.ToString(raw, CultureInfo.InvariantCulture),
                              expectedJson.GetString(), StringComparison.Ordinal),

            RegistryValueKind.Binary when expectedJson.ValueKind == JsonValueKind.String =>
                BinaryMatches(raw, expectedJson.GetString()!),

            _ => false
        };

        return matches
            ? new StateCheckResult(true)
            : new StateCheckResult(false, $"value mismatch: {raw}");
    }

    private StateCheckResult RegKeyExists(TweakAction a)
    {
        RequireHiveKey(a);
        return RegistryHelper.KeyExists(a.Hive!, a.Key!)
            ? new StateCheckResult(true)
            : new StateCheckResult(false, "key not found");
    }

    private StateCheckResult RegNotExists(TweakAction a)
    {
        RequireHiveKey(a);
        var exists = RegistryHelper.ValueExists(a.Hive!, a.Key!, a.Name ?? "");
        return !exists
            ? new StateCheckResult(true)
            : new StateCheckResult(false, "value still exists");
    }

    private StateCheckResult RegGetAllSubKeys(TweakAction a)
    {
        RequireHiveKey(a);
        if (a.Expected == null) return new StateCheckResult(false, "expected not provided");

        var expectedJson = a.Expected.Value;
        var kind = ParseKind(a.Kind);
        var subs = RegistryHelper.GetSubKeyNames(a.Hive!, a.Key!);

        foreach (var sub in subs)
        {
            var full = $"{a.Key}\\{sub}";
            var raw = RegistryHelper.GetValue(a.Hive!, full, a.Name ?? "");
            if (raw == null) continue;

            bool match = kind switch
            {
                RegistryValueKind.DWord when expectedJson.ValueKind == JsonValueKind.Number =>
                    Convert.ToInt32(raw, CultureInfo.InvariantCulture) == expectedJson.GetInt32(),
                _ => false
            };
            if (match) return new StateCheckResult(true);
        }
        return new StateCheckResult(false, "no matching subkey");
    }

    private StateCheckResult RegGetDateInFuture(TweakAction a)
    {
        RequireHiveKey(a);
        var raw = RegistryHelper.GetValue(a.Hive!, a.Key!, a.Name ?? "") as string;
        if (string.IsNullOrEmpty(raw)) return new StateCheckResult(false, "value not found");

        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var dt))
            return dt > DateTime.UtcNow
                ? new StateCheckResult(true)
                : new StateCheckResult(false, "date in past");

        return new StateCheckResult(false, "cannot parse date");
    }

    private StateCheckResult ServiceGet(TweakAction a)
    {
        if (string.IsNullOrEmpty(a.ServiceName))
            return new StateCheckResult(false, "serviceName is required");

        var current = ServiceHelper.GetStartType(a.ServiceName!);
        if (current == null) return new StateCheckResult(false, "service not found");

        var expected = a.ExpectedStartupType ?? "Automatic";
        return string.Equals(current, expected, StringComparison.OrdinalIgnoreCase)
            ? new StateCheckResult(true)
            : new StateCheckResult(false, $"{current} != {expected}");
    }

    private StateCheckResult CommandGet(TweakAction a)
    {
        if (string.IsNullOrEmpty(a.FileName))
            return new StateCheckResult(false, "fileName is required");
        if (string.IsNullOrEmpty(a.OutputContains))
            return new StateCheckResult(false, "outputContains is required");

        var result = CommandRunner.Run(a.FileName!, a.Arguments, timeoutMs: 30000);
        var needle = a.OutputContains!;
        var hay = result.Combined;

        return hay.Contains(needle, StringComparison.OrdinalIgnoreCase)
            ? new StateCheckResult(true)
            : new StateCheckResult(false, $"output does not contain '{needle}'");
    }

    // ============================================================
    //  HELPERS
    // ============================================================

    private static void RequireHiveKey(TweakAction a)
    {
        if (string.IsNullOrEmpty(a.Hive)) throw new ArgumentException("hive is required");
        if (string.IsNullOrEmpty(a.Key)) throw new ArgumentException("key is required");
    }

    public static RegistryValueKind ParseKind(string? kind) => kind?.ToUpperInvariant() switch
    {
        "DWORD" => RegistryValueKind.DWord,
        "QWORD" => RegistryValueKind.QWord,
        "STRING" => RegistryValueKind.String,
        "EXPANDSTRING" => RegistryValueKind.ExpandString,
        "MULTISTRING" => RegistryValueKind.MultiString,
        "BINARY" => RegistryValueKind.Binary,
        _ => RegistryValueKind.DWord
    };

    public static object ConvertJsonValue(JsonElement? element, RegistryValueKind kind)
    {
        if (element == null)
            throw new ArgumentException("value is required");

        var e = element.Value;

        return kind switch
        {
            RegistryValueKind.DWord => e.ValueKind == JsonValueKind.Number ? e.GetInt32() : int.Parse(e.GetString()!, CultureInfo.InvariantCulture),
            RegistryValueKind.QWord => e.ValueKind == JsonValueKind.Number ? e.GetInt64() : long.Parse(e.GetString()!, CultureInfo.InvariantCulture),
            RegistryValueKind.String or RegistryValueKind.ExpandString => e.GetString() ?? "",
            RegistryValueKind.MultiString => e.ValueKind == JsonValueKind.Array
                ? e.EnumerateArray().Select(x => x.GetString() ?? "").ToArray()
                : new[] { e.GetString() ?? "" },
            RegistryValueKind.Binary => ParseHexBytes(e.GetString() ?? ""),
            _ => e.GetString() ?? ""
        };
    }

    public static byte[] ParseHexBytes(string hex)
    {
        // Формат: "9012038010000000" или "DE AD BE EF" или "de,ad,be,ef"
        var cleaned = Regex.Replace(hex, @"[\s,]", "");
        if (cleaned.Length % 2 != 0) throw new ArgumentException($"invalid hex string: {hex}");
        var bytes = new byte[cleaned.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(cleaned.Substring(i * 2, 2), 16);
        return bytes;
    }

    private static bool BinaryMatches(object raw, string expectedHex)
    {
        try
        {
            var rawBytes = raw as byte[];
            if (rawBytes == null) return false;
            var expectedBytes = ParseHexBytes(expectedHex);
            return rawBytes.AsSpan().SequenceEqual(expectedBytes);
        }
        catch { return false; }
    }
}