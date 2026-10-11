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
    public static bool TryRestartElevated()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return false;

            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
                Verb = "runas"
            });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
