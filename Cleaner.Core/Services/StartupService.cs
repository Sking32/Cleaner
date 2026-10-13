using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace Cleaner.Core.Services;

public enum StartupImpact { Unknown, Low, Medium, High }

public enum StartupSource
{
    CurrentUserRun,
    LocalMachineRun,
    LocalMachineRun32,
    UserStartupFolder,
    CommonStartupFolder
}

public sealed record StartupEntry(
    string Name,
    string Command,
    StartupSource Source,
    bool IsEnabled,
    string? FilePath,
    StartupImpact Impact,
    long FileSizeBytes);

/// <summary>
/// Автозагрузка Windows: чтение и включение/отключение так же, как в «Диспетчере задач»
/// (состояние хранится у текущего пользователя в StartupApproved, права администратора не нужны).
/// Влияние на запуск — приблизительная оценка по размеру файла программы.
/// </summary>
public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Run32Key = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedBase = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    public static IReadOnlyList<StartupEntry> GetEntries()
    {
        var list = new List<StartupEntry>();

        ReadRun(list, Microsoft.Win32.Registry.CurrentUser, RunKey, "Run", StartupSource.CurrentUserRun);
        ReadRun(list, Microsoft.Win32.Registry.LocalMachine, RunKey, "Run", StartupSource.LocalMachineRun);
        ReadRun(list, Microsoft.Win32.Registry.LocalMachine, Run32Key, "Run32", StartupSource.LocalMachineRun32);
        ReadFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupSource.UserStartupFolder);
        ReadFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), StartupSource.CommonStartupFolder);

        return list.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static bool SetEnabled(StartupEntry entry, bool enabled)
    {
        try
        {
            var approvedKey = ApprovedBase + ApprovedSubKey(entry.Source);
            var valueName = ApprovedValueName(entry);

            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(approvedKey, writable: true);
            if (key == null) return false;

            var data = new byte[12];
            data[0] = enabled ? (byte)0x02 : (byte)0x03;
            if (!enabled)
                BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);

            key.SetValue(valueName, data, RegistryValueKind.Binary);
            return true;
        }
        catch (Exception ex)
        {
            CoreLog.Warn("Автозагрузка: не удалось изменить состояние " + entry.Name, ex);
            return false;
        }
    }

    /// <summary>Выделяет путь к программе из строки запуска (с кавычками и аргументами или без).</summary>
    public static string ExtractExecutablePath(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return "";

        var text = Environment.ExpandEnvironmentVariables(command.Trim());

        if (text.StartsWith('"'))
        {
            var close = text.IndexOf('"', 1);
            return close > 1 ? text.Substring(1, close - 1) : text.Trim('"');
        }

        var exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe >= 0 ? text.Substring(0, exe + 4) : text;
    }

    /// <summary>Признак «отключено» в двоичном значении StartupApproved: нечётный первый байт.</summary>
    public static bool IsDisabledMarker(byte[]? data)
        => data is { Length: > 0 } && (data[0] & 1) == 1;

    public static StartupImpact EstimateImpact(long sizeBytes)
    {
        if (sizeBytes <= 0) return StartupImpact.Unknown;
        if (sizeBytes < 3L * 1024 * 1024) return StartupImpact.Low;
        if (sizeBytes < 40L * 1024 * 1024) return StartupImpact.Medium;
        return StartupImpact.High;
    }

    private static string ApprovedSubKey(StartupSource source) => source switch
    {
        StartupSource.LocalMachineRun32 => "Run32",
        StartupSource.UserStartupFolder or StartupSource.CommonStartupFolder => "StartupFolder",
        _ => "Run"
    };

    private static string ApprovedValueName(StartupEntry entry)
        => entry.Source is StartupSource.UserStartupFolder or StartupSource.CommonStartupFolder
            ? Path.GetFileName(entry.FilePath ?? entry.Name)
            : entry.Name;

    private static bool ReadEnabled(string approvedSubKey, string valueName)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(ApprovedBase + approvedSubKey);
            return !IsDisabledMarker(key?.GetValue(valueName) as byte[]);
        }
        catch (Exception ex)
        {
            CoreLog.Warn("Автозагрузка: не удалось прочитать состояние " + valueName, ex);
            return true;
        }
    }

    private static void ReadRun(List<StartupEntry> list, RegistryKey hive, string path, string approved, StartupSource source)
    {
        try
        {
            using var key = hive.OpenSubKey(path);
            if (key == null) return;

            foreach (var name in key.GetValueNames())
            {
                var command = key.GetValue(name) as string;
                if (string.IsNullOrWhiteSpace(command)) continue;

                var exe = ExtractExecutablePath(command);
                long size = 0;
                try
                {
                    if (File.Exists(exe)) size = new FileInfo(exe).Length;
                }
                catch (Exception ex)
                {
                    CoreLog.Warn("Автозагрузка: не удалось получить размер " + exe, ex);
                }

                list.Add(new StartupEntry(
                    name, command, source,
                    ReadEnabled(approved, name),
                    string.IsNullOrEmpty(exe) ? null : exe,
                    EstimateImpact(size), size));
            }
        }
        catch (Exception ex)
        {
            CoreLog.Warn("Автозагрузка: не удалось прочитать раздел реестра " + path, ex);
        }
    }

    private static void ReadFolder(List<StartupEntry> list, string folder, StartupSource source)
    {
        try
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;

            foreach (var file in Directory.EnumerateFiles(folder))
            {
                var fileName = Path.GetFileName(file);
                if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;

                long size = 0;
                var impact = StartupImpact.Unknown;
                if (file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    size = new FileInfo(file).Length;
                    impact = EstimateImpact(size);
                }

                list.Add(new StartupEntry(
                    Path.GetFileNameWithoutExtension(file), file, source,
                    ReadEnabled("StartupFolder", fileName),
                    file, impact, size));
            }
        }
        catch (Exception ex)
        {
            CoreLog.Warn("Автозагрузка: не удалось прочитать папку " + folder, ex);
        }
    }
}
