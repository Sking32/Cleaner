using System.Windows;
using System.Windows.Controls;
using Cleaner.App.Views;
using Wpf.Ui.Controls;

namespace Cleaner.App;

public partial class MainWindow : FluentWindow
{
    private readonly CleanupPage _cleanupPage;
    private readonly OptimizePage _optimizePage;
    private readonly SystemPage _systemPage;
    private readonly PrivacyPage _privacyPage;
    private readonly DiskPage _diskPage;

    public MainWindow(
        CleanupPage cleanupPage,
        OptimizePage optimizePage,
        SystemPage systemPage,
        PrivacyPage privacyPage,
        DiskPage diskPage)
    {
        _cleanupPage = cleanupPage;
        _optimizePage = optimizePage;
        _systemPage = systemPage;
        _privacyPage = privacyPage;
        _diskPage = diskPage;

        InitializeComponent();

        RootNavigation.SelectionChanged += OnNavigationSelectionChanged;

        // Показываем первую страницу сразу, минуя SelectedItem.
        RootFrame.Navigate(_cleanupPage);
    }

    private void OnNavigationSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (RootNavigation.SelectedItem is not NavigationViewItem item)
            return;

        Page? page = (item.Tag as string) switch
        {
            "cleanup" => _cleanupPage,
            "optimize" => _optimizePage,
            "system" => _systemPage,
            "privacy" => _privacyPage,
            "disk" => _diskPage,
            _ => null
        };

        if (page is null) return;
        if (ReferenceEquals(RootFrame.Content, page)) return;

        RootFrame.Navigate(page);
    }
}