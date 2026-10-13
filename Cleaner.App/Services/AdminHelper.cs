using System.Diagnostics;
using System.Security.Principal;

namespace Cleaner.App.Services;

/// <summary>Проверка и запрос прав администратора.</summary>
public static class AdminHelper
{
    public static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Запускает новую копию программы с запросом UAC.
    /// Возвращает false, если пользователь отказался или запуск не удался.
    /// </summary>
    public static bool TryRestartElevated(bool showSafety = false)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return false;

            var start = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
                Verb = "runas"
            };
            // The elevated process must wait until this instance releases its state lease.
            start.ArgumentList.Add("--wait-for-parent");
            start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (showSafety) start.ArgumentList.Add("--show-safety");
            Process.Start(start);
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static bool WaitForParent(string[] args)
    {
        if (args.Length == 0 || args[0] != "--wait-for-parent") return true;
        if (args.Length is < 2 or > 3 || (args.Length == 3 && args[2] != "--show-safety") ||
            !int.TryParse(args[1], out var pid) || pid <= 0 || pid == Environment.ProcessId) return false;
        try
        {
            using var parent = Process.GetProcessById(pid);
            if (!string.Equals(parent.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) return false;
            return parent.WaitForExit(10_000);
        }
        catch (ArgumentException) { return true; } // Parent has already exited.
        catch { return false; }
    }
}
