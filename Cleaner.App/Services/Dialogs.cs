using System;
using System.Threading.Tasks;
using Cleaner.App.Localization;

namespace Cleaner.App.Services;

public sealed record DialogRequest(string Title, string Message, string ConfirmText, string? CancelText, bool Danger);

/// <summary>
/// Диалоги подтверждения внутри окна приложения (вместо окон Windows).
/// Окно приложения подключает обработчик <see cref="Host"/>; без него используется MessageBox.
/// </summary>
public static class Dialogs
{
    internal static Func<DialogRequest, Task<bool>>? Host { get; set; }

    public static Task<bool> ConfirmAsync(string title, string message, bool danger = false, string? confirmText = null)
    {
        return Show(new DialogRequest(
            title, message,
            confirmText ?? Loc.T("ui.dialog.yes"),
            Loc.T("ui.dialog.cancel"),
            danger));
    }

    public static async Task InfoAsync(string title, string message)
    {
        await Show(new DialogRequest(title, message, Loc.T("ui.dialog.ok"), null, false));
    }

    private static async Task<bool> Show(DialogRequest request)
    {
        var host = Host;
        if (host != null)
        {
            try
            {
                return await host(request);
            }
            catch (Exception ex)
            {
                AppLog.Warn("Диалог внутри окна не сработал: " + ex.Message);
            }
        }

        var result = System.Windows.MessageBox.Show(
            request.Message,
            request.Title,
            request.CancelText == null ? System.Windows.MessageBoxButton.OK : System.Windows.MessageBoxButton.YesNo,
            request.Danger ? System.Windows.MessageBoxImage.Warning : System.Windows.MessageBoxImage.Question);

        return result == System.Windows.MessageBoxResult.Yes || result == System.Windows.MessageBoxResult.OK;
    }
}
