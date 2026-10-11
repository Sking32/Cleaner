using System;

namespace Cleaner.Core.Services;

public readonly record struct RestorePointResult(bool Success, string Message);

/// <summary>
/// Создание точки восстановления Windows (best-effort).
/// Нужны права администратора и включённая защита системы.
/// Windows по умолчанию не создаёт более одной точки в 24 часа — тогда запрос просто пропускается.
/// </summary>
public static class RestorePointService
{
    public static RestorePointResult TryCreate(string description, int timeoutMs = 120_000)
    {
        var safe = (description ?? "Cleaner").Replace("'", "").Replace("\"", "");
        if (safe.Length > 100) safe = safe.Substring(0, 100);

        var command =
            "Enable-ComputerRestore -Drive ($env:SystemDrive + '\\'); " +
            "Checkpoint-Computer -Description '" + safe + "' -RestorePointType MODIFY_SETTINGS";

        var args = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + command + "\"";

        try
        {
            var r = CommandRunner.Run("powershell.exe", args, timeoutMs);
            if (r.Success)
            {
                CoreLog.Info("Точка восстановления запрошена: " + safe);
                return new RestorePointResult(true, "Точка восстановления запрошена");
            }

            CoreLog.Warn("Не удалось создать точку восстановления: " + r.StdErr.Trim());
            return new RestorePointResult(false, "Точку восстановления создать не удалось");
        }
        catch (Exception ex)
        {
            CoreLog.Warn("Ошибка создания точки восстановления", ex);
            return new RestorePointResult(false, "Точку восстановления создать не удалось");
        }
    }
}
