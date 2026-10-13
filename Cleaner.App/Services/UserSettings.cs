using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Cleaner.App.Services;

/// <summary>
/// Пользовательские настройки: %LOCALAPPDATA%\Cleaner\settings.json
/// (язык интерфейса и закрытые подсказки). Никогда не бросает исключений.
/// </summary>
public sealed class CleanupHistoryEntry
{
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
    public static event Action? HintsReset;
    public static string? LastSaveError => Store.LastError;
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
    public static string GetStartPage() => Load().StartPage is "optimize" or "startup" or "system" or "privacy" or "disk" ? Load().StartPage : "cleanup";
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
        lock (Gate) { Load().StartPage = page is "optimize" or "startup" or "system" or "privacy" or "disk" ? page : "cleanup"; }
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
        var lang = Load().Language;
        if (lang == "ru" || lang == "en") return lang;

        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ru", StringComparison.OrdinalIgnoreCase)
            ? "ru"
            : "en";
    }

    public static void SetLanguage(string language)
    {
        if (language != "ru" && language != "en") return;
        lock (Gate)
        {
            Load().Language = language;
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
            if (list.Count > 50) list.RemoveRange(50, list.Count - 50);
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
