using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Wpf.Ui.Controls;

namespace Cleaner.App.ViewModels;

/// <summary>
/// Категория твиков оптимизации (perf / privacy / win11 / network / maint / games).
/// </summary>
public sealed class OptimizeCategoryViewModel : INotifyPropertyChanged
{
    public string Key { get; }
    public string Name { get; }
    public SymbolRegular IconSymbol { get; }

    public ObservableCollection<OptimizeTweakViewModel> Tweaks { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public OptimizeCategoryViewModel(string key, string name, SymbolRegular icon)
    {
        Key = key;
        Name = name;
        IconSymbol = icon;
    }

    public int AppliedCount => Tweaks.Count(t => t.IsApplied);
    public int TotalCount => Tweaks.Count;
    public bool HasVisibleTweaks => Tweaks.Any(t => t.IsVisible);

    /// <summary>Текст вида «3 / 12» — для шапки категории.</summary>
    public string CountText => TotalCount == 0 ? "" : $"{AppliedCount} / {TotalCount}";

    public void RefreshCounters()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasVisibleTweaks)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AppliedCount)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalCount)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CountText)));
    }
}
