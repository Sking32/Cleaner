using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

        // Канал 1: стандартное событие SelectionChanged.
        RootNavigation.SelectionChanged += OnNavigationSelectionChanged;

        // Канал 2: прямая подписка на клик по каждому пункту (страховка).
        Loaded += OnLoaded;

        // Стартовая страница.
        RootFrame.Navigate(_cleanupPage);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        foreach (var item in RootNavigation.MenuItems)
        {
            if (item is NavigationViewItem nvi)
                nvi.PreviewMouseLeftButtonUp += OnNavigationItemClick;
        }
    }

    private void OnNavigationSelectionChanged(object sender, RoutedEventArgs e)
    {
        var selected = RootNavigation.SelectedItem;
        System.Diagnostics.Debug.WriteLine(
            $"[MainWindow] SelectionChanged: {selected?.GetType().FullName ?? "null"}");

        if (selected is NavigationViewItem nvi)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow]   tag = {nvi.Tag}");
            NavigateByTag(nvi.Tag as string);
        }
    }

    private void OnNavigationItemClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is NavigationViewItem nvi)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] ItemClick: tag = {nvi.Tag}");
            NavigateByTag(nvi.Tag as string);
        }
    }

    private void NavigateByTag(string? tag)
    {
        Page? page = tag switch
        {
            "cleanup" => _cleanupPage,
            "optimize" => _optimizePage,
            "system" => _systemPage,
            "privacy" => _privacyPage,
            "disk" => _diskPage,
            _ => null
        };

        System.Diagnostics.Debug.WriteLine(
            $"[MainWindow] Navigate: tag={tag}, page={page?.GetType().Name ?? "null"}");

        if (page is null) return;
        if (ReferenceEquals(RootFrame.Content, page)) return;

        RootFrame.Navigate(page);
    }
}