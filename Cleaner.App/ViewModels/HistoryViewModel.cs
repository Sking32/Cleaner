using Cleaner.App.Localization;
using Cleaner.App.Services;
using Cleaner.Core.Services.Cleanup;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;

namespace Cleaner.App.ViewModels;

public sealed class HistoryItem
{
    public CleanupHistoryEntry Entry { get; }
    public HistoryItem(CleanupHistoryEntry entry) => Entry = entry;
    public string TimeText => Entry.Time.ToLocalTime().ToString("g", System.Globalization.CultureInfo.GetCultureInfo(Loc.Language));
    public string StatusText => Loc.T("history.kind." + (Entry.Kind is "preview" or "declined" or "failed" or "canceled" ? Entry.Kind
        : Entry.Canceled ? "canceled" : Entry.OperationsFailed > 0 ? "partial" : Entry.Kind == "automatic" ? "automatic" : "applied"));
    public string SummaryText => Loc.F("history.summary", Entry.Operations, CleanupDetailRow.FormatBytes(Entry.BytesQuarantined), Entry.OperationsFailed);
    public bool HasError => !string.IsNullOrWhiteSpace(Entry.ErrorText);
    public string ErrorText => Entry.ErrorText ?? "";
    public bool HasSession => !string.IsNullOrWhiteSpace(Entry.QuarantineDirectory);
    public string SessionDirectory => Entry.QuarantineDirectory ?? "";
    public bool IsLegacy => Entry.Report == null;
    public string TotalsText => Entry.Report?.DryRun == true ? Loc.F("history.preview.totals", CleanupDetailRow.FormatBytes(Entry.Report.Result.PreviewBytes), Entry.FilesSkipped, Entry.DurationMs / 1000.0) : Loc.F("history.totals.full", CleanupDetailRow.FormatBytes(Entry.BytesFreed), Entry.FilesQuarantined, Entry.FilesDeleted, Entry.FilesSkipped, Entry.DurationMs / 1000.0);
    public IReadOnlyList<CleanupDetailRow> Outcomes => (Entry.Report?.Result.Outcomes ?? Entry.Outcomes).Where(o => o != null).Select(o => new CleanupDetailRow(o, null, Entry.Report?.DryRun == true, Entry.Report?.Result)).ToArray();
    public CleanupReport ExportReport => Entry.Report ?? new CleanupReport { CreatedUtc = Entry.Time.ToUniversalTime(), QuarantineDirectory = Entry.QuarantineDirectory,
        Result = new CleanupSessionResult { BytesFreed = Entry.BytesFreed, BytesQuarantined = Entry.BytesQuarantined, FilesQuarantined = Entry.FilesQuarantined,
            FilesDeleted = Entry.FilesDeleted, FilesSkipped = Entry.FilesSkipped, OperationsRun = Entry.Operations, OperationsFailed = Entry.OperationsFailed,
            Canceled = Entry.Canceled, Duration = TimeSpan.FromMilliseconds(Entry.DurationMs), Outcomes = Entry.Outcomes } };
}

public partial class HistoryViewModel : ObservableObject
{
    private readonly Func<IReadOnlyList<CleanupHistoryEntry>> _load;
    public ObservableCollection<HistoryItem> Entries { get; } = new();
    public ObservableCollection<HistoryItem> FilteredEntries { get; } = new();
    [ObservableProperty] private HistoryItem? _selectedEntry;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _statusText = "";
    public bool HasNoEntries => FilteredEntries.Count == 0;
    public bool HasSelection => SelectedEntry != null;
    public Action<string>? OpenSession { get; set; }
    public HistoryViewModel() : this(UserSettings.GetCleanupHistory) { }
    public HistoryViewModel(Func<IReadOnlyList<CleanupHistoryEntry>> load) { _load = load; Refresh(); }
    partial void OnSearchTextChanged(string value) => Filter();
    partial void OnSelectedEntryChanged(HistoryItem? value) { OnPropertyChanged(nameof(HasSelection)); ExportCommand.NotifyCanExecuteChanged(); OpenQuarantineCommand.NotifyCanExecuteChanged(); }
    [RelayCommand] private void Refresh()
    {
        var selectedId = SelectedEntry?.Entry.Id;
        Entries.Clear();
        foreach (var entry in _load()) Entries.Add(new HistoryItem(entry));
        Filter();
        SelectedEntry = FilteredEntries.FirstOrDefault(e => e.Entry.Id == selectedId) ?? FilteredEntries.FirstOrDefault();
    }
    private void Filter()
    {
        FilteredEntries.Clear();
        foreach (var item in Entries.Where(e => (e.TimeText + " " + e.StatusText + " " + e.Entry.Id + " " + string.Join(" ", e.Entry.Outcomes.Select(o => o.Key)))
            .Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase))) FilteredEntries.Add(item);
        if (SelectedEntry != null && !FilteredEntries.Contains(SelectedEntry)) SelectedEntry = FilteredEntries.FirstOrDefault();
        OnPropertyChanged(nameof(HasNoEntries));
    }
    private bool CanExport() => HasSelection;
    [RelayCommand(CanExecute = nameof(CanExport))] private void Export()
    {
        try { var path = ReportExport.ChoosePath("Cleaner-history"); if (path == null || SelectedEntry == null) return; SelectedEntry.ExportReport.Save(path); StatusText = Loc.F("report.saved", path); }
        catch (Exception ex) { StatusText = Loc.T("t.oshibka") + ex.Message; }
    }
    private bool CanOpenSession() => SelectedEntry?.HasSession == true;
    [RelayCommand(CanExecute = nameof(CanOpenSession))] private void OpenQuarantine() { if (SelectedEntry?.HasSession == true) OpenSession?.Invoke(SelectedEntry.SessionDirectory); }
}
