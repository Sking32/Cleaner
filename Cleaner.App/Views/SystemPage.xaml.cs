using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Cleaner.App.Localization;
using Cleaner.App.Services;
using Cleaner.App.ViewModels;

namespace Cleaner.App.Views;

public partial class SystemPage : Page
{
    private readonly IAppNotifications? _notify;

    public SystemPage() => InitializeComponent();

    public SystemPage(SystemViewModel vm, IAppNotifications notify)
    {
        _notify = notify;
        InitializeComponent();
        DataContext = vm;
    }

    /// <summary>Клик по значению (версия Windows, имя компьютера и т.д.) копирует его в буфер обмена.</summary>
    private void OnCopyValueClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBlock block || string.IsNullOrWhiteSpace(block.Text)) return;

        try
        {
            Clipboard.SetText(block.Text);
            _notify?.Success(Loc.T("ui.copied"), block.Text);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Не удалось скопировать в буфер обмена: " + ex.Message);
        }
    }
}
