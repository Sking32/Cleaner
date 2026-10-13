using Cleaner.App.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using Cleaner.Core.Data;

namespace Cleaner.App.ViewModels;

/// <summary>
/// Одна cleanup-операция для UI: локализованное имя, описание, флаг выбора.
/// </summary>
public partial class CleanupOperationViewModel : ObservableObject
{
    public bool IsEnabled { get; }
    public bool Recommended { get; }
    public string Consequence { get; }
    public string Applicability { get; }
    public string Reversibility { get; }
    public string Key { get; }
    public string Handler { get; }
    public string Category { get; }
    public string Level { get; }
    public string Hint { get; }
    public bool HasHint => !string.IsNullOrWhiteSpace(Hint);
    public string Name { get; }
    public string Description { get; }
    public string Summary => Description.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? Description;

    /// <summary>«Безопасно» / «Средний риск» / «Опасно» — для тултипа.</summary>
    public string LevelLabel => Level switch
    {
        "safe" => Loc.T("t.bezopasno"),
        "medium" => Loc.T("t.sredniy_risk"),
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
        "medium" => "!",
        "danger" => "!!",
        _ => "·"
    };

    [ObservableProperty]
    private bool _isChecked;

    public CleanupOperationViewModel(OperationEntry entry, string name, string description)
    {
        IsEnabled = entry.Enabled; Recommended = entry.Recommended;
        Consequence = Loc.T(entry.Enabled ? entry.ConsequenceKey : "cleanup.rule.disabled");
        Applicability = Loc.T(entry.Enabled ? entry.ApplicabilityKey : "cleanup.rule.disabled");
        Reversibility = entry.Enabled ? Loc.T(entry.ReversibilityKey) : "";
        Key = entry.Key;
        Handler = entry.Handler ?? "";
        Category = entry.Category ?? "";
        Level = entry.Level;
        Hint = entry.Hint ?? "";
        Name = name;
        Description = description;
        IsChecked = entry.Enabled && entry.DefaultChecked;
    }
}
