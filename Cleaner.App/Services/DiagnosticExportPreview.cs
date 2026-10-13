using System.Windows;
using System.Windows.Controls;
using Cleaner.App.Localization;
using Cleaner.Core.Services;

namespace Cleaner.App.Services;

internal static class DiagnosticExportPreview
{
    public static string? Show(object report, string prefix)
    {
        string? savedPath = null;
        var window = CreateWindow(report, prefix, path => savedPath = path);
        if (Application.Current?.MainWindow is Window owner && owner.IsVisible) window.Owner = owner;
        window.ShowDialog(); return savedPath;
    }

    // Construction is separate so the preview can be rendered without opening dialogs or saving files.
    internal static Window CreateWindow(object report, string prefix, Action<string> onSaved)
    {
        var window = new Window { Title = Loc.T("diagnostics.title"), Width = 720, Height = 560, MinWidth = 480, MinHeight = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Application.Current?.TryFindResource("BgBaseBrush") as System.Windows.Media.Brush,
            Foreground = Application.Current?.TryFindResource("TextPrimaryBrush") as System.Windows.Media.Brush };
        var layout = new DockPanel { Margin = new Thickness(20), Background = window.Background };
        var caption = new TextBlock { Text = Loc.T("diagnostics.hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(caption, Dock.Top); layout.Children.Add(caption);
        var choice = new CheckBox { Content = Loc.T("diagnostics.private"), Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(choice, Dock.Top); layout.Children.Add(choice);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var save = new Wpf.Ui.Controls.Button { Content = new TextBlock { Text = Loc.T("report.export"), Foreground = System.Windows.Media.Brushes.Black },
            Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
            Padding = new Thickness(14, 8, 14, 8), MinWidth = 100 };
        var cancel = new Wpf.Ui.Controls.Button { Content = Loc.T("t.otmena"), Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary,
            Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        buttons.Children.Add(save); buttons.Children.Add(cancel); DockPanel.SetDock(buttons, Dock.Bottom); layout.Children.Add(buttons);
        var preview = new Wpf.Ui.Controls.TextBox { Text = DiagnosticExport.CreateJson(report), IsReadOnly = true, TextWrapping = TextWrapping.NoWrap,
            Background = Application.Current?.TryFindResource("BgElevatedBrush") as System.Windows.Media.Brush,
            Foreground = window.Foreground,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        layout.Children.Add(preview); window.Content = layout;
        choice.Checked += (_, _) => preview.Text = DiagnosticExport.CreateJson(report, true);
        choice.Unchecked += (_, _) => preview.Text = DiagnosticExport.CreateJson(report);
        save.Click += (_, _) =>
        {
            try { var path = ReportExport.ChoosePath(prefix); if (path == null) return; AtomicFile.Write(path, preview.Text); onSaved(path); window.DialogResult = true; }
            catch (Exception ex) { caption.Text = Loc.T("settings.save.failed") + " " + ex.Message; }
        };
        return window;
    }
}
