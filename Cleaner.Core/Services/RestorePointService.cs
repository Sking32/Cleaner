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

        var command = "$ErrorActionPreference='Stop'; $WarningPreference='Stop'; " +
            "$before=@(Get-ComputerRestorePoint | Select-Object -ExpandProperty SequenceNumber); " +
            "Checkpoint-Computer -Description '" + safe + "' -RestorePointType MODIFY_SETTINGS; " +
            "$new=@(Get-ComputerRestorePoint | Where-Object { $_.SequenceNumber -notin $before -and $_.Description -eq '" + safe + "' }); " +
            "if($new.Count -eq 0){throw 'Windows did not create a new restore point'}";

        var args = "-NoProfile -NonInteractive -EncodedCommand " +
            Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(command));

        try
        {
            var r = CommandRunner.Run("powershell.exe", args, timeoutMs);
            if (r.Success)
            {
                CoreLog.Info("Точка восстановления создана: " + safe);
                return new RestorePointResult(true, "Точка восстановления создана");
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
