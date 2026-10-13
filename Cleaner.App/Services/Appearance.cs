using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace Cleaner.App.Services;

public static class Appearance
{
    public static void Apply()
    {
        var accent = UserSettings.GetAccent() switch
        {
            "teal" => Color.FromRgb(0x4C, 0xD6, 0xC0),
            "violet" => Color.FromRgb(0xB3, 0xA0, 0xFF),
            _ => Color.FromRgb(0x78, 0xAE, 0xFF)
        };
        var resources = Application.Current.Resources;
        resources["AccentBrush"] = new SolidColorBrush(accent);
        resources["AccentHoverBrush"] = new SolidColorBrush(accent);
        resources["AccentSoftBrush"] = new SolidColorBrush(Color.FromArgb(28, accent.R, accent.G, accent.B));
        resources["AccentGradientBrush"] = new SolidColorBrush(accent);
        ApplicationAccentColorManager.Apply(accent, ApplicationTheme.Dark);
    }
}
