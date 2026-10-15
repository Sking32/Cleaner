using System.Text.Json;
using Cleaner.App.Services;
using Cleaner.App.ViewModels;
using System.Windows.Media;

namespace Cleaner.App.Tests;

[Collection("Preferences")]
public sealed class ThemeTests
{
    [Fact]
    public void FailedThemeSavePreservesPreviousPreferenceAndViewModelSelection()
    {
        var root = Path.Combine(Path.GetTempPath(), "CleanerThemeFailure_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "settings.json");
        File.WriteAllText(path, "{invalid");
        UserSettings.UsePreviewStore(new AppSettingsStore(path));
        var viewModel = new SettingsViewModel();
        viewModel.Theme = "light";
        Assert.Equal("dark", viewModel.Theme);
        Assert.Equal("dark", UserSettings.GetTheme());
        Assert.Equal("{invalid", File.ReadAllText(path));
        Assert.False(UserSettings.SetTheme("unknown"));
    }

    [Theory]
    [InlineData("light", "blue")]
    [InlineData("light", "teal")]
    [InlineData("light", "violet")]
    [InlineData("dark", "blue")]
    [InlineData("dark", "teal")]
    [InlineData("dark", "violet")]
    public void PalettePreservesReadableTextAndPrimaryActions(string theme, string accent)
    {
        var palette = Appearance.Palette(theme, accent);
        foreach (var text in new[] { "TextPrimaryBrush", "TextSecondaryBrush", "TextMutedBrush" })
            foreach (var surface in new[] { "BgBaseBrush", "BgElevatedBrush", "BgOverlayBrush" })
                Assert.True(Contrast(palette[text], palette[surface]) >= 4.5, theme + ": " + text + " on " + surface);
        Assert.True(Contrast(palette["PrimaryForegroundBrush"], palette["PrimaryFillBrush"]) >= 4.5);
    }

    private static double Contrast(Color first, Color second)
    {
        static double L(Color c)
        {
            static double V(byte component) { var n = component / 255.0; return n <= .04045 ? n / 12.92 : Math.Pow((n + .055) / 1.055, 2.4); }
            return .2126 * V(c.R) + .7152 * V(c.G) + .0722 * V(c.B);
        }
        var a = L(first); var b = L(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }
    [Theory]
    [InlineData("light", "light")]
    [InlineData("dark", "dark")]
    [InlineData("unknown", "dark")]
    [InlineData(null, "dark")]
    public void ThemePreferenceRoundTripsWithoutChangingOtherSettings(string? theme, string expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "CleanerTheme_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "settings.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new { Theme = theme, Accent = "teal", AutoCleanupEnabled = false }));
        var store = new AppSettingsStore(path);
        store.Load();
        Assert.True(store.Save());
        using var saved = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(expected, saved.RootElement.GetProperty("Theme").GetString());
        Assert.Equal("teal", saved.RootElement.GetProperty("Accent").GetString());
        Assert.False(saved.RootElement.GetProperty("AutoCleanupEnabled").GetBoolean());
    }
}
