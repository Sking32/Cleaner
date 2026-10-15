using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace Cleaner.App.Services;

public static class Appearance
{
    internal static IReadOnlyDictionary<string, Color> Palette(string theme, string accentName = "blue")
    {
        var light = theme == "light";
        Color C(string value) => (Color)ColorConverter.ConvertFromString(value);
        var accent = accentName switch
        {
            "teal" => C(light ? "#087C70" : "#60D6C0"),
            "violet" => C(light ? "#7052BD" : "#B9A5F4"),
            _ => C(light ? "#126AC1" : "#72B9F0")
        };
        return new Dictionary<string, Color>
        {
            ["BgBaseBrush"] = C(light ? "#F7F9FC" : "#1B222A"),
            ["BgElevatedBrush"] = C(light ? "#FFFFFF" : "#232C36"),
            ["BgOverlayBrush"] = C(light ? "#EFF3F8" : "#2A3541"),
            ["BgHoverBrush"] = C(light ? "#E1EAF5" : "#344455"),
            ["BorderSubtleBrush"] = C(light ? "#D5DFEB" : "#374657"),
            ["BorderStrongBrush"] = C(light ? "#9DAFC4" : "#62778E"),
            ["TextPrimaryBrush"] = C(light ? "#172B42" : "#F3F6FA"),
            ["TextSecondaryBrush"] = C(light ? "#3E536C" : "#C2CFDD"),
            ["TextMutedBrush"] = C(light ? "#5C6E84" : "#A6B7C9"),
            ["AccentBrush"] = accent,
            ["AccentHoverBrush"] = accent,
            ["AccentSoftBrush"] = Color.FromArgb(light ? (byte)24 : (byte)32, accent.R, accent.G, accent.B),
            ["AccentGradientBrush"] = accent,
            ["PrimaryFillBrush"] = accent,
            ["PrimaryForegroundBrush"] = C(light ? "#FFFFFF" : "#102333"),
            ["SafeBrush"] = C(light ? "#14744A" : "#75D6A5"),
            ["CautionBrush"] = C(light ? "#806000" : "#EFCA70"),
            ["DangerBrush"] = C(light ? "#B52135" : "#FF9CAA"),
            ["InfoBrush"] = C(light ? "#126AC1" : "#79BEF4"),
            ["SidebarBrush"] = C(light ? "#EFF3F8" : "#222C37"),
            ["BadgeBrush"] = C(light ? "#E4EDF8" : "#304155"),
            ["RowHoverBrush"] = C(light ? "#ECF2FA" : "#2D3C4C"),
            ["CategoryIconBgBrush"] = C(light ? "#E5EDF8" : "#304155"),
            ["GaugeTrackBrush"] = C(light ? "#D9E2ED" : "#4A5C70")
        };
    }

    public static void Apply()
    {
        if (Application.Current is not { } application) return;
        var theme = UserSettings.GetTheme();
        var wpfTheme = theme == "light" ? ApplicationTheme.Light : ApplicationTheme.Dark;
        ApplicationThemeManager.Apply(wpfTheme);
        var palette = Palette(theme, UserSettings.GetAccent());
        var resources = application.Resources;
        ApplicationAccentColorManager.Apply(palette["AccentBrush"], wpfTheme);
        foreach (var (key, color) in palette) resources[key] = new SolidColorBrush(color);
        foreach (var (alias, key) in new[] {
            ("CardBgBrush", "BgElevatedBrush"), ("CardBorderBrush", "BorderSubtleBrush"),
            ("CardHoverBgBrush", "BgOverlayBrush"), ("TextTertiaryBrush", "TextMutedBrush"),
            ("SuccessBrush", "SafeBrush"), ("WarningBrush", "CautionBrush"),
            ("GradientCleanBrush", "PrimaryFillBrush"), ("AccentGlowBrush", "AccentSoftBrush") })
            resources[alias] = resources[key];
        resources["OperationRowPadding"] = UserSettings.GetDensity() == "comfortable"
            ? new Thickness(14, 10, 14, 10) : new Thickness(10, 6, 10, 6);
    }
}
