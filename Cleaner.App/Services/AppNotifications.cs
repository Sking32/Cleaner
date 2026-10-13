using System;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace Cleaner.App.Services;

/// <summary>
/// Тонкая обёртка над WPF-UI Snackbar, чтобы VM не зависели от интерфейсов WPF-UI.
/// </summary>
public interface IAppNotifications
{
    void Success(string title, string message);
    void Error(string title, string message);
    void Warning(string title, string message);
    void Info(string title, string message);
}

public sealed class AppNotifications : IAppNotifications
{
    private readonly ISnackbarService _snackbar;

    public AppNotifications(ISnackbarService snackbar)
    {
        _snackbar = snackbar ?? throw new ArgumentNullException(nameof(snackbar));
    }

    public void Success(string title, string message)
        => Show(title, message, ControlAppearance.Success, SymbolRegular.CheckmarkCircle24, TimeSpan.FromSeconds(3));

    public void Error(string title, string message)
        => Show(title, message, ControlAppearance.Danger, SymbolRegular.ErrorCircle24, TimeSpan.FromSeconds(5));

    public void Warning(string title, string message)
        => Show(title, message, ControlAppearance.Caution, SymbolRegular.Warning24, TimeSpan.FromSeconds(4));

    public void Info(string title, string message)
        => Show(title, message, ControlAppearance.Info, SymbolRegular.Info24, TimeSpan.FromSeconds(3));

    private void Show(string title, string message, ControlAppearance appearance, SymbolRegular icon, TimeSpan timeout)
    {
        try
        {
            _snackbar.Show(title, message, appearance, new SymbolIcon(icon), timeout);
        }
        catch
        {
            // Snackbar — необязательная фича. Если презентер ещё не подключён, не падаем.
        }
    }
}