using Cleaner.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cleaner.App.ViewModels;

/// <summary>
/// Общая основа страниц с подсказкой для новичков: баннер можно закрыть
/// (выбор запоминается) и снова открыть кнопкой «?».
/// </summary>
public abstract partial class HelpViewModelBase : ObservableObject
{
    private bool? _showHelp;

    protected abstract string HelpKey { get; }

    protected HelpViewModelBase()
    {
        UserSettings.HintsReset += () => { _showHelp = null; OnPropertyChanged(nameof(ShowHelp)); };
    }

    public bool ShowHelp
    {
        get => _showHelp ??= !UserSettings.IsHintDismissed(HelpKey);
        set
        {
            if (_showHelp == value) return;
            _showHelp = value;
            UserSettings.SetHintDismissed(HelpKey, !value);
            OnPropertyChanged();
        }
    }

    [RelayCommand]
    private void ToggleHelp() => ShowHelp = !ShowHelp;
}
