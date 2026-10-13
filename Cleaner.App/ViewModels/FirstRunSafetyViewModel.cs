using Cleaner.App.Localization;
using Cleaner.App.Services;
using Cleaner.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows;

namespace Cleaner.App.ViewModels;

public partial class FirstRunSafetyViewModel : ObservableObject
{
    private readonly Func<bool> _isElevated;
    private readonly Func<Task<bool>> _restart;
    private readonly Func<Task<RestorePointResult>> _create;
    private readonly Func<bool> _acknowledge;
    public event Action? Completed;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateRestorePointCommand))]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private bool _isBusy;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateRestorePointCommand))]
    private bool _hasRestorePoint;
    [ObservableProperty] private string _statusText = "";

    public FirstRunSafetyViewModel() : this(AdminHelper.IsElevated, RestartAsync,
        () => SessionRestorePoint.TryCreateAsync("Before first use"), UserSettings.AcknowledgeSafetyNotice) { }

    internal FirstRunSafetyViewModel(Func<bool> isElevated, Func<Task<bool>> restart,
        Func<Task<RestorePointResult>> create, Func<bool> acknowledge)
    {
        _isElevated = isElevated; _restart = restart; _create = create; _acknowledge = acknowledge;
    }

    private static async Task<bool> RestartAsync()
    {
        if (!await Dialogs.ConfirmAsync(Loc.T("safety.title"), Loc.T("safety.restart.explain"),
                confirmText: Loc.T("safety.restart"))) return false;
        if (!AdminHelper.TryRestartElevated(showSafety: true)) return false;
        Application.Current.Shutdown();
        return true;
    }

    private bool CanCreate() => !IsBusy && !HasRestorePoint;
    private bool CanContinue() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateRestorePointAsync()
    {
        if (!CanCreate()) return;
        IsBusy = true;
        StatusText = Loc.T("safety.creating");
        try
        {
            if (!_isElevated())
            {
                StatusText = await _restart() ? Loc.T("safety.restarting") : Loc.T("safety.restart.canceled");
                return;
            }
            var result = await _create();
            HasRestorePoint = result.Success;
            StatusText = Loc.T(result.Success ? "safety.created" : "safety.failed");
        }
        catch (Exception ex)
        {
            AppLog.Warn("Не удалось создать точку из окна безопасности: " + ex.Message);
            StatusText = Loc.T("safety.failed");
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private void Continue()
    {
        if (IsBusy) return;
        if (!_acknowledge()) { StatusText = Loc.T("safety.save.failed"); return; }
        Completed?.Invoke();
    }
}
