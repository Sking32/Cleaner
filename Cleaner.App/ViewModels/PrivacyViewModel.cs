using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cleaner.Core.Services;

namespace Cleaner.App.ViewModels;

public partial class PrivacyViewModel : ObservableObject
{
    private readonly IWhitelistService _whitelist;

    public ObservableCollection<string> WhitelistRules { get; } = new();
    public ObservableCollection<QuarantineSessionItem> QuarantineSessions { get; } = new();

    [ObservableProperty]
    private string _newRuleText = "";

    [ObservableProperty]
    private string _statusText = "Готово";

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

    public PrivacyViewModel(IWhitelistService whitelist)
    {
        _whitelist = whitelist ?? throw new ArgumentNullException(nameof(whitelist));
        WhitelistPath = _whitelist.FilePath;
        QuarantinePath = QuarantineService.GetRootDirectory();
        RefreshAll();
    }

    [RelayCommand]
    private void Refresh() => RefreshAll();

    private void RefreshAll()
    {
        WhitelistRules.Clear();
        foreach (var r in _whitelist.Rules)
            WhitelistRules.Add(r);
        OnPropertyChanged(nameof(HasWhitelistRules));

        QuarantineSessions.Clear();
        var sessions = QuarantineService.ListSessions();
        foreach (var s in sessions)
            QuarantineSessions.Add(new QuarantineSessionItem(s));
        OnPropertyChanged(nameof(HasQuarantineSessions));
        OnPropertyChanged(nameof(HasNoQuarantine));

        long totalBytes = sessions.Sum(s => s.TotalSizeBytes);
        int totalCount = sessions.Sum(s => s.Count);

        QuarantineSummary = sessions.Count == 0
            ? "Карантин пуст"
            : $"{sessions.Count} сессий • {totalCount} объектов • {FormatBytes(totalBytes)}";

        StatusText = "Состояние обновлено";
    }

    [RelayCommand(CanExecute = nameof(CanAddRule))]
    private void AddRule()
    {
        var text = NewRuleText.Trim();
        if (text.Length == 0) return;

        if (_whitelist.Add(text))
        {
            NewRuleText = "";
            StatusText = $"Правило добавлено: {text}";
            RefreshAll();
        }
        else
        {
            StatusText = "Такое правило уже есть";
        }
    }

    private bool CanAddRule() => !string.IsNullOrWhiteSpace(NewRuleText);

    partial void OnNewRuleTextChanged(string value)
    {
        AddRuleCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void RemoveRule(string? rule)
    {
        if (string.IsNullOrWhiteSpace(rule)) return;
        if (_whitelist.Remove(rule))
        {
            StatusText = $"Правило удалено: {rule}";
            RefreshAll();
        }
    }

    [RelayCommand]
    private void ClearRules()
    {
        _whitelist.Clear();
        StatusText = "Все правила удалены";
        RefreshAll();
    }

    [RelayCommand]
    private async Task RestoreSessionAsync(QuarantineSessionItem? session)
    {
        if (session is null) return;

        IsBusy = true;
        try
        {
            var result = await Task.Run(() =>
                QuarantineService.RestoreFromQuarantine(session.Directory));

            StatusText = result.Failed == 0
                ? $"Восстановлено: {result.Restored}"
                : $"Восстановлено: {result.Restored}, ошибок: {result.Failed}";
            RefreshAll();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void DeleteSession(QuarantineSessionItem? session)
    {
        if (session is null) return;

        try
        {
            if (System.IO.Directory.Exists(session.Directory))
                System.IO.Directory.Delete(session.Directory, recursive: true);
            StatusText = $"Удалено: {session.Id}";
            RefreshAll();
        }
        catch (Exception ex)
        {
            StatusText = "Ошибка: " + ex.Message;
        }
    }

    [RelayCommand]
    private void CleanOld()
    {
        var n = QuarantineService.ClearOldQuarantine(7);
        StatusText = n == 0 ? "Старых сессий нет" : $"Удалено старых сессий: {n}";
        RefreshAll();
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
    public string Directory { get; }
    public string Id { get; }
    public string CreatedText { get; }
    public int Count { get; }
    public string SizeText { get; }

    public QuarantineSessionItem(QuarantineSession s)
    {
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