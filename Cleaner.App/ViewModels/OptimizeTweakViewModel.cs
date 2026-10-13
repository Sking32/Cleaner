using Cleaner.App.Localization;
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
    public bool IsCompatible { get; }
    public string CompatibilityText { get; }
    public string CompatibilityLabel { get; }

    /// <summary>Полная метка уровня — для тултипа.</summary>
    public string LevelLabel => Level switch
    {
        "safe" => Loc.T("t.bezopasno"),
        "caution" => Loc.T("t.ostorozhno"),
        "danger" => Loc.T("t.opasno"),
        _ => Level
    };

    /// <summary>Понятное объяснение уровня риска — для тултипа.</summary>
    public string LevelHint => Level switch
    {
        "safe" => Loc.T("level.safe.hint"),
        "medium" => Loc.T("level.medium.hint"),
        "caution" => Loc.T("level.caution.hint"),
        "danger" => Loc.T("level.danger.hint"),
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
    [ObservableProperty] private bool _isVisible = true;

    [ObservableProperty]
    private bool _isPendingReboot;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _lastError;
    public bool HasError => !string.IsNullOrWhiteSpace(LastError);
    partial void OnLastErrorChanged(string? value) => OnPropertyChanged(nameof(HasError));

    /// <summary>UI-признак: разрешено ли переключение (не бизи).</summary>
    public bool CanToggle => !IsBusy && !IsOperationBusy && (IsApplied || IsCompatible);

    [ObservableProperty] private bool _isOperationBusy;
    partial void OnIsOperationBusyChanged(bool value) => OnPropertyChanged(nameof(CanToggle));

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
        var compatibility = Cleaner.Core.Services.TweakCompatibilityService.Evaluate(def);
        IsCompatible = compatibility.CanApply;
        CompatibilityText = Loc.T(compatibility.MessageKey);
        CompatibilityLabel = Loc.T(!compatibility.CanApply ? "compatibility.label.unsupported"
            : compatibility.Verified ? "compatibility.label.verified" : "compatibility.label.unverified");
        _toggle = toggle ?? throw new ArgumentNullException(nameof(toggle));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanToggle));
    }

    partial void OnIsAppliedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanToggle));
        if (_suppressToggle) return;
        // Пользователь кликнул ToggleSwitch → вызываем родителя.
        _ = RunToggleAsync(value);
    }

    private async Task RunToggleAsync(bool apply)
    {
        if (IsBusy || IsOperationBusy || apply && !IsCompatible) { SetAppliedSilently(!apply, IsPendingReboot); return; }

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
