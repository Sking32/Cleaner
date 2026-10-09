using System;
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
    public static bool Stop(string serviceName) => RunSc($"stop \"{serviceName}\"");

    // ---------- internals ----------

    private static bool RunSc(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            p.WaitForExit(15000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }
}