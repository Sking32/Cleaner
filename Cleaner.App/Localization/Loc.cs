using System;
using System.Globalization;
using Cleaner.App.Services;
using Cleaner.Core.Data;

namespace Cleaner.App.Localization;

/// <summary>
/// Доступ к локализованным строкам из кода и XAML.
/// Язык берётся из настроек пользователя (ru / en), по умолчанию — язык системы.
/// Если ключа нет в выбранном языке, берётся русский текст.
/// </summary>
public static class Loc
{
    private static readonly object Gate = new();
    private static LocalizationRepository? _repo;
    private static LocalizationRepository? _fallback;

    public static string Language { get; private set; } = "ru";

    private static LocalizationRepository Repo()
    {
        lock (Gate)
        {
            if (_repo != null) return _repo;

            Language = UserSettings.GetLanguage();
            try
            {
                _repo = new LocalizationRepository(Language);
            }
            catch (Exception ex)
            {
                AppLog.Error("Не удалось загрузить локализацию: " + Language, ex);
                Language = "ru";
                _repo = new LocalizationRepository("ru");
            }
            return _repo;
        }
    }

    public static string T(string key)
    {
        var repo = Repo();
        if (repo.Has(key)) return repo[key];

        if (Language != "ru")
        {
            try
            {
                _fallback ??= new LocalizationRepository("ru");
                if (_fallback.Has(key)) return _fallback[key];
            }
            catch (Exception ex)
            {
                AppLog.Warn("Не удалось загрузить русский текст по умолчанию: " + ex.Message);
            }
        }

        return key;
    }

    public static string F(string key, params object?[] args)
    {
        var template = T(key);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }
}
