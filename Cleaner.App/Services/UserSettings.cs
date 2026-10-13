using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Cleaner.Core.Services;

namespace Cleaner.App.Services;

/// <summary>
/// Пользовательские настройки: %LOCALAPPDATA%\Cleaner\settings.json
/// (язык интерфейса и закрытые подсказки). Никогда не бросает исключений.
/// </summary>
public sealed class CleanupHistoryEntry
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsChartEligible => Report?.DryRun == false && Report.Result != null &&
        Kind is "applied" or "automatic" && (FilesDeleted > 0 || FilesQuarantined > 0) && !string.IsNullOrWhiteSpace(Report.AppVersion);
    public static CleanupHistoryEntry FromReport(Cleaner.Core.Services.Cleanup.CleanupReport report, string kind, string? error = null)
    {
        var r = report.Result;
        return new() { Time = report.CreatedUtc, Kind = kind, ErrorText = error, Report = report, QuarantineDirectory = report.QuarantineDirectory,
            BytesFreed = r.BytesFreed, BytesQuarantined = r.BytesQuarantined, FilesQuarantined = r.FilesQuarantined,
            Operations = r.OperationsRun, FilesDeleted = r.FilesDeleted, FilesSkipped = r.FilesSkipped, OperationsFailed = r.OperationsFailed,
            Canceled = r.Canceled || kind == "canceled", DurationMs = (long)r.Duration.TotalMilliseconds, Outcomes = r.Outcomes.ToList() };
    }
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = "applied";
    public string? ErrorText { get; set; }
    public string? QuarantineDirectory { get; set; }
    public Cleaner.Core.Services.Cleanup.CleanupReport? Report { get; set; }
    public DateTime Time { get; set; }
    public long BytesFreed { get; set; }
    public long BytesQuarantined { get; set; }
    public int FilesQuarantined { get; set; }
    public int Operations { get; set; }
    public int FilesDeleted { get; set; }
    public int FilesSkipped { get; set; }
    public int OperationsFailed { get; set; }
    public bool Canceled { get; set; }
    public long DurationMs { get; set; }
    public List<Cleaner.Core.Services.Cleanup.OperationOutcome> Outcomes { get; set; } = new();
}

public static class UserSettings
{
    private static readonly object Gate = new();
    private static AppSettingsStore Store = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cleaner", "settings.json"));
    public static event Action? Changed;
    public static event Action? DiskChanged;
    public static void NotifyDiskChanged() => DiskChanged?.Invoke();
    public static bool GetExpertMode() => Load().ExpertMode;
    public static bool SetExpertMode(bool enabled)
    {
        lock (Gate)
        {
            var previous = Load().ExpertMode; Load().ExpertMode = enabled;
            if (!Store.Save()) { Load().ExpertMode = previous; return false; }
        }
        Changed?.Invoke(); return true;
    }
    public static event Action? HintsReset;
    public static string? LastSaveError => Store.LastError;
    public static bool GetAutoCleanupEnabled() => Load().AutoCleanupEnabled;
    public static int GetAutoCleanupInterval() => Load().AutoCleanupIntervalMinutes;
    public static bool SetAutoCleanup(bool enabled, int minutes)
    {
        if (!InAppCleanupSchedule.AllowedMinutes.Contains(minutes)) return false;
        lock (Gate)
        {
            var data = Load();
            var previousEnabled = data.AutoCleanupEnabled;
            var previousMinutes = data.AutoCleanupIntervalMinutes;
            data.AutoCleanupEnabled = enabled; data.AutoCleanupIntervalMinutes = minutes;
            if (!Store.Save()) { data.AutoCleanupEnabled = previousEnabled; data.AutoCleanupIntervalMinutes = previousMinutes; return false; }
        }
        Changed?.Invoke();
        return true;
    }
    private static AppSettingsData Load() => Store.Load();
    // The visual preview host supplies a fixture before loading any XAML or view model.
    internal static void UsePreviewStore(AppSettingsStore store) { lock (Gate) { Store = store; } }

    private static void Save()
    {
        lock (Gate)
        {
            if (!Store.Save()) AppLog.Warn("Не удалось сохранить настройки: " + Store.LastError);
        }
        Changed?.Invoke();
    }

    public static string GetAccent() => Load().Accent is "teal" or "violet" ? Load().Accent : "blue";
    public static string GetStartPage() => Load().StartPage is "optimize" or "startup" or "system" or "privacy" or "disk" or "history" or "whatsnew" ? Load().StartPage : "cleanup";
    public static string GetDensity() => Load().Density;
    public static void SetDensity(string value) { lock (Gate) { Load().Density = value == "comfortable" ? value : "compact"; } Save(); }
    public static WindowPlacement? GetWindow() => Load().Window;
    public static void SetWindow(WindowPlacement value) { lock (Gate) { Load().Window = value; } Save(); }
    public static IReadOnlyList<UserCleanupPreset> GetCleanupPresets() => Load().CleanupPresets.ToArray();
    public static UserCleanupPreset SaveCleanupPreset(string name, IEnumerable<string> keys, IEnumerable<string> allowed)
    {
        UserCleanupPreset preset;
        lock (Gate) { preset = Store.UpsertCleanupPreset(name, keys, allowed); }
        Changed?.Invoke(); return preset;
    }
    public static void DeleteCleanupPreset(string id) { lock (Gate) { Store.DeleteCleanupPreset(id); } Changed?.Invoke(); }
    public static bool GetAnimations() => Load().Animations;
    public static bool GetShowMetrics() => Load().ShowMetrics;
    public static void SetAppearance(string accent, bool animations, bool showMetrics)
    {
        lock (Gate)
        {
            Load().Accent = accent is "teal" or "violet" ? accent : "blue";
            Load().Animations = animations;
            Load().ShowMetrics = showMetrics;
        }
        Save();
    }
    public static void SetStartPage(string page)
    {
        lock (Gate) { Load().StartPage = page is "optimize" or "startup" or "system" or "privacy" or "disk" or "history" or "whatsnew" ? page : "cleanup"; }
        Save();
    }
    public static void ResetHints()
    {
        lock (Gate) { Load().DismissedHints.Clear(); }
        Save();
        HintsReset?.Invoke();
    }

    /// <summary>"ru" или "en": сохранённый выбор, иначе язык системы (русский → ru, остальные → en).</summary>
    public static string GetLanguage()
    {
        return AppLanguage.Resolve(Load().Language, CultureInfo.CurrentUICulture);
    }

    public static string GetLanguagePreference() => Load().Language is "ru" or "en" ? Load().Language! : "auto";

    public static bool NeedsSafetyNotice => Load().SafetyNoticeVersion < 1;

    public static bool AcknowledgeSafetyNotice()
    {
        lock (Gate)
        {
            var data = Load();
            var previous = data.SafetyNoticeVersion;
            data.SafetyNoticeVersion = Math.Max(previous, 1);
            if (!Store.Save())
            {
                data.SafetyNoticeVersion = previous;
                AppLog.Warn("Не удалось сохранить ознакомление с безопасностью: " + Store.LastError);
                return false;
            }
        }
        Changed?.Invoke();
        return true;
    }

    public static void SetLanguage(string language)
    {
        if (language is not ("ru" or "en" or "auto")) return;
        lock (Gate)
        {
            Load().Language = language == "auto" ? null : language;
        }
        Save();
    }

    public static bool IsHintDismissed(string key)
    {
        lock (Gate)
        {
            return Load().DismissedHints.Contains(key);
        }
    }

    public static void SetHintDismissed(string key, bool dismissed)
    {
        lock (Gate)
        {
            var list = Load().DismissedHints;
            if (dismissed)
            {
                if (!list.Contains(key)) list.Add(key);
            }
            else
            {
                list.Remove(key);
            }
        }
        Save();
    }

    public static void AddCleanup(CleanupHistoryEntry entry)
    {
        lock (Gate)
        {
            var list = Load().History;
            list.Insert(0, entry);
            Load().History = HistoryRetention.Prune(list);
        }
        Save();
    }

    public static IReadOnlyList<CleanupHistoryEntry> GetCleanupHistory()
    {
        lock (Gate)
        {
            return new List<CleanupHistoryEntry>(Load().History);
        }
    }
}
