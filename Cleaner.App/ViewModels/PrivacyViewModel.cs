using Cleaner.App.Localization;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using System.Text.Json;
using Cleaner.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cleaner.Core.Services;
using System.ComponentModel;
using System.Windows.Data;

namespace Cleaner.App.ViewModels;

public partial class PrivacyViewModel : HelpViewModelBase
{
    protected override string HelpKey => "privacy";

    private readonly IWhitelistService _whitelist;
    private readonly Cleaner.Core.Data.TweakRepository _tweaks;
    private readonly AppliedTweaksStore _applied;
    private CancellationTokenSource? _cts;
    private object? _lastReport;

    private IProgress<QuarantineProgress> OperationProgress() => new Progress<QuarantineProgress>(p =>
    {
        if (IsBusy) StatusText = Loc.F("quarantine.progress", p.Completed, p.Total, p.Item);
    });

    private void BeginOperation()
    {
        _cts = new CancellationTokenSource();
        IsBusy = true;
        CancelCommand.NotifyCanExecuteChanged();
    }

    private void EndOperation()
    {
        _cts?.Dispose(); _cts = null;
        IsBusy = false;
        CancelCommand.NotifyCanExecuteChanged();
        ExportReportCommand.NotifyCanExecuteChanged();
        UserSettings.NotifyDiskChanged();
    }

    private bool CanCancel() => IsBusy && _cts != null;
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _cts?.Cancel();

    private bool CanExportReport() => !IsBusy && _lastReport != null;
    [RelayCommand(CanExecute = nameof(CanExportReport))]
    private void ExportReport()
    {
        try
        {
            if (_lastReport == null) return;
            var path = DiagnosticExportPreview.Show(_lastReport, "Cleaner-quarantine");
            if (path == null || _lastReport == null) return;
            StatusText = Loc.F("report.saved", path);
        }
        catch (Exception ex) { StatusText = Loc.T("t.oshibka") + ex.Message; }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task InspectQuarantineAsync()
    {
        BeginOperation();
        try
        {
            var token = _cts!.Token;
            var sessions = await Task.Run(() => QuarantineService.InspectSessions(verifyContents: true, cancellationToken: token));
            _lastReport = new { SchemaVersion = 1, CreatedUtc = DateTime.UtcNow, Operation = "inspection", Sessions = sessions };
            RefreshAll(sessions);
            StatusText = Loc.F("quarantine.inspected", sessions.Count, sessions.Count(s => s.Health != QuarantineHealth.Healthy));
        }
        catch (OperationCanceledException) { StatusText = Loc.T("t.otmeneno"); }
        catch (Exception ex) { StatusText = Loc.T("t.oshibka") + ex.Message; }
        finally { EndOperation(); }
    }

    [ObservableProperty]
    private int _protectionPercent;

    public string ProtectionText => Loc.F("ui.privacy.protection", ProtectionPercent);

    public ObservableCollection<string> WhitelistRules { get; } = new();
    public ObservableCollection<QuarantineSessionItem> QuarantineSessions { get; } = new();
    public ICollectionView FilteredSessions { get; }
    [ObservableProperty] private string _sessionSearchText = "";
    public bool HasNoVisibleSessions => FilteredSessions.IsEmpty;
    public string EmptySessionsText => Loc.T(string.IsNullOrWhiteSpace(SessionSearchText) ? "t.karantin_pust" : "design.no.results");
    partial void OnSessionSearchTextChanged(string value)
    {
        FilteredSessions.Refresh();
        OnPropertyChanged(nameof(HasNoVisibleSessions));
        OnPropertyChanged(nameof(EmptySessionsText));
    }
    [ObservableProperty] private QuarantineSessionItem? _selectedSession;
    public bool HasSelectedSession => SelectedSession != null;
    partial void OnSelectedSessionChanged(QuarantineSessionItem? value)
    {
        OnPropertyChanged(nameof(HasSelectedSession));
        _ = LoadSelectedFilesAsync();
    }
    public ObservableCollection<QuarantineFileItem> SessionFiles { get; } = new();
    public ICollectionView FilteredFiles { get; }
    [ObservableProperty] private string _fileSearchText = "";
    [ObservableProperty] private bool _pendingFilesOnly;
    [ObservableProperty] private string _fileListStatus = "";
    private int _fileLoadRevision;
    partial void OnFileSearchTextChanged(string value) => FilteredFiles.Refresh();
    partial void OnPendingFilesOnlyChanged(bool value) => FilteredFiles.Refresh();
    public async Task LoadSelectedFilesAsync()
    {
        var revision = ++_fileLoadRevision; var session = SelectedSession;
        SessionFiles.Clear(); FileListStatus = "";
        if (session == null) return;
        try
        {
            var entries = await Task.Run(() => QuarantineService.ReadEntries(session.Directory));
            if (revision != _fileLoadRevision) return;
            foreach (var entry in entries) SessionFiles.Add(new(entry));
            FileListStatus = Loc.F("recovery.files.count", entries.Count);
        }
        catch (Exception) { if (revision == _fileLoadRevision) FileListStatus = Loc.T("recovery.files.failed"); }
    }

    [ObservableProperty]
    private string _newRuleText = "";

    [ObservableProperty]
    private string _statusText = Loc.T("t.gotovo");

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _whitelistPath = "";

    [ObservableProperty]
    private string _quarantinePath = "";

    [ObservableProperty]
    private string _quarantineSummary = "";

    public bool HasWhitelistRules => WhitelistRules.Count > 0;
    public bool HasQuarantineSessions => QuarantineSessions.Count > 0;
    public bool HasNoQuarantine => QuarantineSessions.Count == 0;

    public PrivacyViewModel(IWhitelistService whitelist, Cleaner.Core.Data.TweakRepository tweaks, AppliedTweaksStore applied)
    {
        _whitelist = whitelist ?? throw new ArgumentNullException(nameof(whitelist));
        _tweaks = tweaks ?? throw new ArgumentNullException(nameof(tweaks));
        _applied = applied ?? throw new ArgumentNullException(nameof(applied));
        FilteredFiles = CollectionViewSource.GetDefaultView(SessionFiles);
        FilteredFiles.Filter = item => item is QuarantineFileItem file && file.Matches(FileSearchText, PendingFilesOnly);
        FilteredSessions = CollectionViewSource.GetDefaultView(QuarantineSessions);
        FilteredSessions.Filter = item => item is QuarantineSessionItem session &&
            (SessionSearchText.Trim().Length == 0 || (session.Id + " " + session.CreatedText + " " + session.HealthText)
                .Contains(SessionSearchText.Trim(), StringComparison.OrdinalIgnoreCase));
        WhitelistPath = _whitelist.FilePath;
        QuarantinePath = QuarantineService.GetRootDirectory();
        RefreshAll();
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private void Refresh() { RefreshAll(); StatusText = Loc.T("t.sostoyanie_obnovleno"); }
    private bool CanRun() => !IsBusy;
    partial void OnIsBusyChanged(bool value)
    {
        RefreshCommand.NotifyCanExecuteChanged();
        RestoreSessionCommand.NotifyCanExecuteChanged();
        DeleteSessionCommand.NotifyCanExecuteChanged();
        CleanOldCommand.NotifyCanExecuteChanged();
        ClearRulesCommand.NotifyCanExecuteChanged();
        RemoveRuleCommand.NotifyCanExecuteChanged();
        AddRuleCommand.NotifyCanExecuteChanged();
        InspectQuarantineCommand.NotifyCanExecuteChanged();
        ExportReportCommand.NotifyCanExecuteChanged();
    }

    private void UpdateProtection()
    {
        var all = _tweaks.GetByCategory("privacy");
        var applied = all.Count(t => _applied.IsApplied(t.Key));
        ProtectionPercent = all.Count == 0 ? 0 : (int)Math.Round(applied * 100.0 / all.Count);
        OnPropertyChanged(nameof(ProtectionText));
    }

    private void RefreshAll(IReadOnlyList<QuarantineSession>? inspected = null)
    {
        UpdateProtection();
        WhitelistRules.Clear();
        foreach (var r in _whitelist.Rules)
            WhitelistRules.Add(r);
        OnPropertyChanged(nameof(HasWhitelistRules));

        var selectedId = SelectedSession?.Id;
        QuarantineSessions.Clear();
        var sessions = (inspected ?? QuarantineService.InspectSessions()).Where(x => x.Count > 0 || x.Health != QuarantineHealth.Healthy).ToList();
        foreach (var s in sessions)
            QuarantineSessions.Add(new QuarantineSessionItem(s));
        SelectedSession = FilteredSessions.Cast<QuarantineSessionItem>().FirstOrDefault(s => s.Id == selectedId)
            ?? FilteredSessions.Cast<QuarantineSessionItem>().FirstOrDefault();
        OnPropertyChanged(nameof(HasNoVisibleSessions));
        OnPropertyChanged(nameof(HasQuarantineSessions));
        OnPropertyChanged(nameof(HasNoQuarantine));

        long totalBytes = sessions.Sum(s => s.TotalSizeBytes);
        int totalCount = sessions.Sum(s => s.Count);

        QuarantineSummary = sessions.Count == 0
            ? Loc.T("t.karantin_pust")
            : Loc.F("t.sessiy_obektov", sessions.Count, totalCount, FormatBytes(totalBytes));

    }

    [RelayCommand(CanExecute = nameof(CanAddRule))]
    private void AddRule()
    {
        var text = NewRuleText.Trim();
        if (text.Length == 0) return;

        if (_whitelist.Add(text))
        {
            NewRuleText = "";
            StatusText = Loc.F("t.pravilo_dobavleno", text);
            RefreshAll();
        }
        else
        {
            StatusText = Loc.T("t.takoe_pravilo_uzhe_est");
        }
    }

    private bool CanAddRule() => !IsBusy && !string.IsNullOrWhiteSpace(NewRuleText);

    partial void OnNewRuleTextChanged(string value)
    {
        AddRuleCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private void RemoveRule(string? rule)
    {
        if (string.IsNullOrWhiteSpace(rule)) return;
        if (_whitelist.Remove(rule))
        {
            StatusText = Loc.F("t.pravilo_udaleno", rule);
            RefreshAll();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private void ClearRules()
    {
        _whitelist.Clear();
        StatusText = Loc.T("t.vse_pravila_udaleny");
        RefreshAll();
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RestoreSessionAsync(QuarantineSessionItem? session)
    {
        if (session is null || IsBusy || !session.CanRestore) return;

        BeginOperation();
        try
        {
            var token = _cts!.Token;
            var progress = OperationProgress();
            var result = await Task.Run(() =>
                QuarantineService.RestoreFromQuarantine(session.Directory, token, progress));
            _lastReport = new { SchemaVersion = 1, CreatedUtc = DateTime.UtcNow, Operation = "restore", session.Directory, Result = result };

            StatusText = result.Canceled ? Loc.T("quarantine.canceled") : result.Failed == 0
                ? Loc.F("t.vosstanovleno", result.Restored)
                : Loc.F("t.vosstanovleno_oshibok", result.Restored, result.Failed);
            RefreshAll();
        }
        finally
        {
            EndOperation();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task DeleteSessionAsync(QuarantineSessionItem? session)
    {
        if (session is null || IsBusy || !session.CanDelete) return;
        BeginOperation();

        try
        {
            if (!await Dialogs.ConfirmAsync("Cleaner", Loc.T("quarantine.delete.confirm"), danger: true)) return;
            var token = _cts!.Token;
            var progress = OperationProgress();
            var result = await Task.Run(() => new QuarantineService(session.Directory).ClearAllDetailed(token, progress));
            _lastReport = new { SchemaVersion = 1, CreatedUtc = DateTime.UtcNow, Operation = "delete", session.Directory, Result = result };
            StatusText = result.Canceled ? Loc.T("quarantine.canceled") : result.Errors.Count == 0
                ? Loc.F("t.udaleno", session.Id)
                : Loc.F("quarantine.delete.partial", result.Deleted, result.Failed, result.Errors[0]);
            RefreshAll();
        }
        catch (Exception ex)
        {
            StatusText = Loc.T("t.oshibka") + ex.Message;
            RefreshAll();
        }
        finally { EndOperation(); }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task CleanOldAsync()
    {
        if (IsBusy) return;
        BeginOperation();
        try
        {
            if (!await Dialogs.ConfirmAsync("Cleaner", Loc.T("quarantine.old.confirm"), danger: true)) return;
            var token = _cts!.Token;
            var progress = OperationProgress();
            var result = await Task.Run(() => QuarantineService.ClearOldQuarantineDetailed(7, cancellationToken: token, progress: progress));
            _lastReport = new { SchemaVersion = 1, CreatedUtc = DateTime.UtcNow, Operation = "deleteOld", Result = result };
            StatusText = result.Canceled ? Loc.T("quarantine.canceled")
                : Loc.F("quarantine.sweep", result.DeletedSessions, result.FailedSessions);
            RefreshAll();
        }
        catch (Exception ex) { StatusText = Loc.T("t.oshibka") + ex.Message; }
        finally { EndOperation(); }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return $"{v.ToString("F2", CultureInfo.InvariantCulture)} {units[u]}";
    }
}

public sealed class QuarantineSessionItem
{
    public bool CanRestore { get; }
    public bool CanDelete { get; }
    public string HealthText { get; }
    public string IssueText { get; }
    public bool HasIssues => !string.IsNullOrWhiteSpace(IssueText);
    public string Directory { get; }
    public string Id { get; }
    public string CreatedText { get; }
    public int Count { get; }
    public string CountText => Loc.F("ui.objects_count", Count);
    public string SizeText { get; }

    public QuarantineSessionItem(QuarantineSession s)
    {
        CanRestore = s.CanRestore;
        CanDelete = s.CanDelete;
        HealthText = Loc.T("quarantine.health." + s.Health.ToString().ToLowerInvariant());
        IssueText = string.Join(Environment.NewLine, s.Issues);
        Directory = s.Directory;
        Id = s.Id;
        CreatedText = s.CreatedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
        Count = s.Count;
        SizeText = FormatSize(s.TotalSizeBytes);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return $"{v.ToString("F2", CultureInfo.InvariantCulture)} {units[u]}";
    }
}
