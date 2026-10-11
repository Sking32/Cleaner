using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Cleaner.Core.Models;

namespace Cleaner.App.ViewModels;

/// <summary>
/// Колбэк, который TweakVM дёргает при переключении ToggleSwitch.
/// Родитель (OptimizeViewModel) сам решает, что делать (Apply/Unapply + обработка ошибок).
/// </summary>
public delegate Task ToggleTweakDelegate(OptimizeTweakViewModel tweak, bool apply);

/// <summary>
/// Один твик оптимизации для UI: локализованные тексты, уровень, флаг «применён»,
/// флаг «требует перезагрузки», бизи-флаг на время операции.
/// </summary>
public partial class OptimizeTweakViewModel : ObservableObject
{
    private readonly ToggleTweakDelegate _toggle;
    private bool _suppressToggle;

    public string Key { get; }
    public string Category { get; }
    public string Level { get; }
    public bool RequiresReboot { get; }

    public string Name { get; }
    public string Short { get; }
    public string Description { get; }
    public string Warn { get; }

    public bool HasWarn => !string.IsNullOrWhiteSpace(Warn);
    public bool HasShort => !string.IsNullOrWhiteSpace(Short);

    /// <summary>Полная метка уровня — для тултипа.</summary>
    public string LevelLabel => Level switch
    {
        "safe" => "Безопасно",
        "caution" => "Осторожно",
        "danger" => "Опасно",
        _ => Level
    };

    /// <summary>Компактная метка уровня для бейджа.</summary>
    public string LevelShort => Level switch
    {
        "safe" => "✓",
        "caution" => "!",
        "danger" => "!!",
        _ => "·"
    };

    [ObservableProperty]
    private bool _isApplied;

    [ObservableProperty]
    private bool _isPendingReboot;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _lastError;

    /// <summary>UI-признак: разрешено ли переключение (не бизи).</summary>
    public bool CanToggle => !IsBusy;

    public OptimizeTweakViewModel(
        TweakDefinition def,
        string name,
        string shortText,
        string description,
        string warn,
        ToggleTweakDelegate toggle)
    {
        Key = def.Key;
        Category = def.Category;
        Level = def.Level;
        RequiresReboot = def.RequiresReboot;
        Name = name;
        Short = shortText;
        Description = description;
        Warn = warn;
        _toggle = toggle ?? throw new ArgumentNullException(nameof(toggle));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanToggle));
    }

    partial void OnIsAppliedChanged(bool value)
    {
        if (_suppressToggle) return;
        // Пользователь кликнул ToggleSwitch → вызываем родителя.
        _ = RunToggleAsync(value);
    }

    private async Task RunToggleAsync(bool apply)
    {
        if (IsBusy) return;

        IsBusy = true;
        LastError = null;
        try
        {
            await _toggle(this, apply);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Программно установить состояние БЕЗ вызова сервиса (используется при Refresh).
    /// </summary>
    public void SetAppliedSilently(bool applied, bool pendingReboot)
    {
        _suppressToggle = true;
        try
        {
            IsApplied = applied;
            IsPendingReboot = pendingReboot;
        }
        finally
        {
            _suppressToggle = false;
        }
    }
}