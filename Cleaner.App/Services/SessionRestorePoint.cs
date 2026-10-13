using Cleaner.App.Localization;
using System;
using System.Threading.Tasks;
using Cleaner.Core.Services;

namespace Cleaner.App.Services;

/// <summary>
/// Создаёт точку восстановления Windows не чаще одного раза за запуск программы.
/// При отсутствии подтверждённой точки восстановления останавливает опасную операцию.
/// </summary>
public static class SessionRestorePoint
{
    private static bool _created;
    private static readonly System.Threading.SemaphoreSlim Gate = new(1, 1);

    public static async Task<bool> EnsureAsync(string reason)
    {
        var result = await TryCreateAsync(reason);
        if (result.Success) return true;
        await Dialogs.InfoAsync("Cleaner", Loc.T("restore.failed") + "\n" + result.Message);
        return false;
    }

    public static async Task<RestorePointResult> TryCreateAsync(string reason)
    {
        await Gate.WaitAsync();
        try
        {
            if (_created) return new RestorePointResult(true, Loc.T("safety.created"));
            var result = AdminHelper.IsElevated()
                ? await Task.Run(() => RestorePointService.TryCreate("Cleaner: " + reason))
                : new RestorePointResult(false, Loc.T("restore.admin"));
            AppLog.Info($"Точка восстановления ({reason}): {result.Message}");
            if (result.Success) _created = true;
            return result;
        }
        finally { Gate.Release(); }
    }
}
