using System;
using System.Threading.Tasks;
using Cleaner.Core.Services;

namespace Cleaner.App.Services;

/// <summary>
/// Создаёт точку восстановления Windows не чаще одного раза за запуск программы.
/// Если нет прав администратора — молча пропускает (с записью в журнал).
/// </summary>
public static class SessionRestorePoint
{
    private static bool _requested;

    public static async Task EnsureAsync(string reason)
    {
        if (_requested) return;

        if (!AdminHelper.IsElevated())
        {
            AppLog.Info("Точка восстановления пропущена: нет прав администратора");
            return;
        }

        _requested = true;
        var result = await Task.Run(() => RestorePointService.TryCreate("Cleaner: " + reason));
        AppLog.Info($"Точка восстановления ({reason}): {result.Message}");
    }
}
