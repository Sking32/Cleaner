using System.Globalization;

namespace Cleaner.App.Services;

public static class AppLanguage
{
    // Windows display language, rather than keyboard layout or date/number format.
    public static string Resolve(string? preference, CultureInfo windowsUiCulture) =>
        preference is "ru" or "en" ? preference :
        windowsUiCulture.TwoLetterISOLanguageName.Equals("ru", StringComparison.OrdinalIgnoreCase) ? "ru" : "en";
}
