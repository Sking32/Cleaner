using System.Collections.ObjectModel;
using Wpf.Ui.Controls;

namespace Cleaner.App.ViewModels;

/// <summary>
/// Секция cleanup-операций (из operations.json: sec.quick, sec.reports, …).
/// </summary>
public sealed class CleanupCategoryViewModel
{
    public string Key { get; }
    public string Name { get; }

    /// <summary>Иконка категории (Fluent-иконка из WPF-UI).</summary>
    public SymbolRegular IconSymbol { get; }

    public ObservableCollection<CleanupOperationViewModel> Operations { get; } = new();

    public CleanupCategoryViewModel(string key, string name, SymbolRegular iconSymbol)
    {
        Key = key;
        Name = name;
        IconSymbol = iconSymbol;
    }
}