using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.ServiceProcess;

namespace Cleaner.Core.Services;

/// <summary>
/// Обёртка над Windows-службами.
/// GetStartType — через ServiceController (read-only).
/// SetStartType, Start, Stop — через sc.exe (без Start-Service — чтобы не зависнуть как PS 5.1).
/// </summary>
public static class ServiceHelper
{
    /// <summary>
    /// Службы, остановка или отключение которых может сломать Windows
    /// (сбой входа в систему, отказ сети, зависание оболочки, синий экран).
    /// </summary>
    private static readonly HashSet<string> ProtectedServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "RpcSs", "RpcEptMapper", "DcomLaunch", "LSM", "SamSs", "Winmgmt", "EventLog",
        "PlugPlay", "Power", "ProfSvc", "gpsvc", "BrokerInfrastructure", "CoreMessaging",
        "SystemEventsBroker", "Schedule", "UserManager", "StateRepository", "nsi", "BFE",
        "mpssvc", "Themes", "TimeBrokerSvc", "KeyIso", "CryptSvc", "Wcmsvc", "DispBrokerDesktopSvc"
    };

    public static bool IsProtected(string serviceName)
        => !string.IsNullOrWhiteSpace(serviceName) && ProtectedServices.Contains(serviceName.Trim());

    public static bool Exists(string serviceName)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            _ = sc.Status;  // триггерит запрос — если службы нет, бросит исключение
            return true;
        }
        catch { return false; }
    }

    /// <summary>Возвращает "Automatic", "Manual", "Disabled", "Boot", "System" или null, если службы нет.</summary>
    public static string? GetStartType(string serviceName)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            return sc.StartType switch
            {
                ServiceStartMode.Automatic => "Automatic",
                ServiceStartMode.Manual => "Manual",
                ServiceStartMode.Disabled => "Disabled",
                ServiceStartMode.Boot => "Boot",
                ServiceStartMode.System => "System",
                _ => "Unknown"
            };
        }
        catch { return null; }
    }

    /// <summary>Возвращает "Running", "Stopped", "Paused" и т.п. или null, если службы нет.</summary>
    public static string? GetStatus(string serviceName)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            return sc.Status.ToString();
        }
        catch { return null; }
    }

    public static bool SetStartType(string serviceName, string startType)
    {
        if (IsProtected(serviceName) &&
            string.Equals(startType, "Disabled", StringComparison.OrdinalIgnoreCase))
        {
            CoreLog.Warn($"Отключение защищённой службы запрещено: {serviceName}");
            return false;
        }

        var scArg = startType?.ToLowerInvariant() switch
        {
            "automatic" or "auto" => "auto",
            "manual" => "demand",
            "disabled" => "disabled",
            _ => "auto"
        };
        return RunSc($"config \"{serviceName}\" start= {scArg}");
    }

    public static bool Start(string serviceName) => RunSc($"start \"{serviceName}\"");
    public static bool Stop(string serviceName)
    {
        if (IsProtected(serviceName))
        {
            CoreLog.Warn($"Остановка защищённой службы запрещена: {serviceName}");
            return false;
        }
        return RunSc($"stop \"{serviceName}\"");
    }

    // ---------- internals ----------

    private static bool RunSc(string arguments)
    {
        // CommandRunner читает stdout/stderr и убивает процесс по таймауту.
        return CommandRunner.Run("sc.exe", arguments, timeoutMs: 15000).Success;
    }
}