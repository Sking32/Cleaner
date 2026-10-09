using CommunityToolkit.Mvvm.ComponentModel;
using Cleaner.Core.Data;

namespace Cleaner.App.ViewModels;

/// <summary>
/// Одна cleanup-операция для UI: локализованное имя, описание, флаг выбора.
/// </summary>
public partial class CleanupOperationViewModel : ObservableObject
{
    public string Key { get; }
    public string Handler { get; }
    public string Category { get; }
    public string Level { get; }
    public string Hint { get; }
    public string Name { get; }
    public string Description { get; }

    /// <summary>«Безопасно» / «Средний риск» / «Опасно» — для тултипа.</summary>
    public string LevelLabel => Level switch
    {
        "safe" => "Безопасно",
        "medium" => "Средний риск",
        "danger" => "Опасно",
        _ => Level
    };

    /// <summary>Компактная метка уровня для бейджа.</summary>
    public string LevelShort => Level switch
    {
        "safe" => "✓",
        "medium" => "!",
        "danger" => "!!",
        _ => "·"
    };

    [ObservableProperty]
    private bool _isChecked;

    public CleanupOperationViewModel(OperationEntry entry, string name, string description)
    {
        Key = entry.Key;
        Handler = entry.Handler ?? "";
        Category = entry.Category ?? "";
        Level = entry.Level;
        Hint = entry.Hint ?? "";
        Name = name;
        Description = description;
        IsChecked = entry.DefaultChecked;
    }
}