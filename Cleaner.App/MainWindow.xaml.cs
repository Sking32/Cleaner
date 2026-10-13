using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Cleaner.App.Localization;
using Cleaner.App.Services;
using Cleaner.App.ViewModels;
using Cleaner.App.Views;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace Cleaner.App;

public partial class MainWindow : FluentWindow
{
    public static string ApplicationTitle => $"Cleaner · {AppVersion.Current}";

    private readonly CleanupPage _cleanupPage;
    private readonly OptimizePage _optimizePage;
    private readonly StartupPage _startupPage;
    private readonly SystemPage _systemPage;
    private readonly PrivacyPage _privacyPage;
    private readonly DiskPage _diskPage;
    private readonly SettingsPage _settingsPage;
    private readonly HistoryPage _historyPage;
    private readonly WhatsNewPage _whatsNewPage;
    private readonly FirstRunSafetyViewModel _firstRunSafety;
    private IInputElement? _focusBeforeSafety;
    private TaskCompletionSource<bool>? _dialogTcs;
    private IInputElement? _focusBeforeDialog;
    private List<PaletteCommand> _paletteAll = new();

    internal sealed record PaletteCommand(string Title, Action Run);

    public MainWindow(
        CleanupPage cleanupPage,
        OptimizePage optimizePage,
        StartupPage startupPage,
        SystemPage systemPage,
        PrivacyPage privacyPage,
        DiskPage diskPage,
        SettingsPage settingsPage,
        HistoryPage historyPage,
        WhatsNewPage whatsNewPage,
        StatusBarViewModel statusBar,
        ISnackbarService snackbarService,
        FirstRunSafetyViewModel firstRunSafety)
    {
        _cleanupPage = cleanupPage;
        _optimizePage = optimizePage;
        _startupPage = startupPage;
        _systemPage = systemPage;
        _privacyPage = privacyPage;
        _diskPage = diskPage;
        _settingsPage = settingsPage;
        _historyPage = historyPage;
        _whatsNewPage = whatsNewPage;
        _firstRunSafety = firstRunSafety;

        InitializeComponent();
        SafetyNotice.DataContext = _firstRunSafety;
        _firstRunSafety.PropertyChanged += (_, _) => UpdateNavigationAvailability();
        _firstRunSafety.Completed += CloseSafetyNotice;
        if (_settingsPage.DataContext is SettingsViewModel settings) settings.ShowSafety = ShowSafetyNotice;
        Title = ApplicationTitle;
        if (Application.Current is App)
        {
            var work = SystemParameters.WorkArea;
            var placement = WindowLayout.Fit(UserSettings.GetWindow(), work);
            MinWidth = Math.Min(1000, work.Width); MinHeight = Math.Min(640, work.Height);
            Width = placement.Width; Height = placement.Height; Left = placement.Left!.Value; Top = placement.Top!.Value;
            WindowStartupLocation = WindowStartupLocation.Manual;
            if (placement.Maximized) WindowState = WindowState.Maximized;
        }
        if (_historyPage.DataContext is HistoryViewModel history) history.OpenSession = directory =>
        {
            NavigateTo("privacy");
            if (_privacyPage.DataContext is PrivacyViewModel privacy)
            {
                privacy.SessionSearchText = "";
                privacy.SelectedSession = privacy.QuarantineSessions.FirstOrDefault(s => string.Equals(s.Directory, directory, StringComparison.OrdinalIgnoreCase));
                if (privacy.SelectedSession == null) privacy.StatusText = Loc.T("history.session.missing");
            }
        };
        foreach (var page in new Page[] { _cleanupPage, _optimizePage, _startupPage, _systemPage, _privacyPage, _diskPage, _settingsPage })
            if (page.DataContext is System.ComponentModel.INotifyPropertyChanged vm)
                vm.PropertyChanged += (_, _) => UpdateNavigationAvailability();
        UpdateNavigationAvailability();
        Closing += (_, e) =>
        {
            if (!IsOperationActive)
            {
                if (IsLoaded && Application.Current is App)
                {
                    var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
                    UserSettings.SetWindow(new WindowPlacement { Width = bounds.Width, Height = bounds.Height, Left = bounds.Left, Top = bounds.Top, Maximized = WindowState == WindowState.Maximized });
                }
                return;
            }
            e.Cancel = true;
            _ = Dialogs.InfoAsync("Cleaner", Loc.T("operation.wait"));
        };

        StatusBarHost.DataContext = statusBar;
        Dialogs.Host = ShowDialogAsync;
        PreviewKeyDown += OnWindowPreviewKeyDown;

        UpdatePreferences();
        UserSettings.Changed += UpdatePreferences;
        Closed += (_, _) => UserSettings.Changed -= UpdatePreferences;

        // Подключаем presenter к сервису — теперь IAppNotifications куда угодно.
        snackbarService.SetSnackbarPresenter(RootSnackbar);

        // Канал 1: стандартное событие SelectionChanged.
        RootNavigation.SelectionChanged += OnNavigationSelectionChanged;

        // Плавное появление страниц при переходах (fade + сдвиг вверх).
        RootFrame.Navigated += OnFrameNavigated;

        // Стартовая страница.
        NavigateTo(UserSettings.GetStartPage());
    }

    private void OnFrameNavigated(object sender, System.Windows.Navigation.NavigationEventArgs e)
    {
        foreach (var (button, page) in new[] { (SettingsButton, (Page)_settingsPage), (HistoryButton, _historyPage), (WhatsNewButton, _whatsNewPage) })
        {
            var selected = ReferenceEquals(e.Content, page);
            button.SetResourceReference(BackgroundProperty, selected ? "AccentSoftBrush" : "SidebarBrush");
            button.SetResourceReference(ForegroundProperty, selected ? "AccentBrush" : "TextSecondaryBrush");
            if (selected) RootNavigation.SelectedItem = null;
        }
        Motion.PlayEnter(e.Content as FrameworkElement);
        switch ((e.Content as Page)?.DataContext)
        {
            case PrivacyViewModel privacy: privacy.RefreshCommand.Execute(null); break;
            case OptimizeViewModel optimize: optimize.RefreshCommand.Execute(null); break;
            case HistoryViewModel history: history.RefreshCommand.Execute(null); break;
            case SystemViewModel system: system.RefreshCommand.Execute(null); break;
        }
    }

    private void UpdatePreferences()
    {
        StatusBarHost.Visibility = UserSettings.GetShowMetrics() ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ShowFirstRunSafetyIfNeeded(bool force = false)
    {
        // Initial view models may still be reading services/startup entries. The first-run
        // notice must appear immediately and block interaction during those reads too.
        if ((force || UserSettings.NeedsSafetyNotice) && SafetyOverlay.Visibility != Visibility.Visible) OpenSafetyNotice();
    }

    public void ShowSafetyNotice()
    {
        if (IsOperationActive || DialogOverlay.Visibility == Visibility.Visible) return;
        OpenSafetyNotice();
    }

    private void OpenSafetyNotice()
    {
        ClosePalette();
        _focusBeforeSafety = Keyboard.FocusedElement;
        WorkspaceHost.IsEnabled = false;
        SafetyOverlay.Visibility = Visibility.Visible;
        UpdateNavigationAvailability();
        ((FrameworkElement)SafetyNotice.FindName("ContinueButton")).Focus();
    }

    private void CloseSafetyNotice()
    {
        SafetyOverlay.Visibility = Visibility.Collapsed;
        WorkspaceHost.IsEnabled = true;
        UpdateNavigationAvailability();
        _focusBeforeSafety?.Focus();
    }

    private void OnNavigationSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RootNavigation.SelectedItem is ListBoxItem item) NavigateByTag(item.Tag as string);
    }

    private void OnOpenPaletteClick(object sender, RoutedEventArgs e) => OpenPalette();

    private void OnSettingsClick(object sender, RoutedEventArgs e) => NavigateTo("settings");

    private void OnHistoryClick(object sender, RoutedEventArgs e) => NavigateTo("history");

    private void OnWhatsNewClick(object sender, RoutedEventArgs e) => NavigateTo("whatsnew");

    private void UpdateNavigationAvailability()
    {
        RootNavigation.IsEnabled = SettingsButton.IsEnabled = HistoryButton.IsEnabled = WhatsNewButton.IsEnabled =
            !IsOperationActive && SafetyOverlay.Visibility != Visibility.Visible;
    }

    /// <summary>Переход на страницу с подсветкой пункта меню (для палитры команд).</summary>
    private void NavigateTo(string tag)
    {
        if (IsOperationActive && RootFrame.Content != null) return;
        try
        {
            foreach (var item in RootNavigation.Items)
            {
                if (item is ListBoxItem nvi)
                    nvi.IsSelected = string.Equals(nvi.Tag as string, tag, StringComparison.Ordinal);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("Не удалось подсветить пункт меню: " + ex.Message);
        }

        NavigateByTag(tag);
    }

    private void NavigateByTag(string? tag)
    {
        if (IsOperationActive && RootFrame.Content != null) return;
        Page? page = tag switch
        {
            "cleanup" => _cleanupPage,
            "optimize" => _optimizePage,
            "startup" => _startupPage,
            "system" => _systemPage,
            "privacy" => _privacyPage,
            "disk" => _diskPage,
            "settings" => _settingsPage,
            "history" => _historyPage,
            "whatsnew" => _whatsNewPage,
            _ => null
        };

        if (page is null) return;
        if (ReferenceEquals(RootFrame.Content, page)) return;

        RootFrame.Navigate(page);
    }

    private bool IsOperationActive =>
        _firstRunSafety.IsBusy ||
        (_cleanupPage.DataContext as CleanupViewModel)?.IsRunning == true ||
        (_optimizePage.DataContext as OptimizeViewModel)?.IsBusy == true ||
        (_privacyPage.DataContext as PrivacyViewModel)?.IsBusy == true ||
        (_systemPage.DataContext as SystemViewModel)?.IsBusy == true ||
        (_startupPage.DataContext as StartupViewModel)?.IsBusy == true ||
        (_diskPage.DataContext as DiskViewModel)?.IsScanning == true ||
        (_diskPage.DataContext as DiskViewModel)?.IsMutating == true;

    // ===================== Диалог подтверждения =====================

    private Task<bool> ShowDialogAsync(DialogRequest request)
    {
        _dialogTcs?.TrySetResult(false);
        _dialogTcs = new TaskCompletionSource<bool>();

        DialogTitle.Text = request.Title;
        DialogMessage.Text = request.Message;
        DialogConfirm.Content = request.ConfirmText;
        DialogConfirm.Appearance = request.Danger ? ControlAppearance.Danger : ControlAppearance.Primary;
        DialogConfirm.Foreground = request.Danger ? System.Windows.Media.Brushes.White
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x10, 0x14, 0x1C));
        DialogCancel.Content = request.CancelText ?? "";
        DialogCancel.Visibility = request.CancelText == null ? Visibility.Collapsed : Visibility.Visible;

        _focusBeforeDialog = Keyboard.FocusedElement;
        DialogOverlay.Visibility = Visibility.Visible;
        DialogConfirm.Focus();

        return _dialogTcs.Task;
    }

    private void CloseDialog(bool result)
    {
        DialogOverlay.Visibility = Visibility.Collapsed;
        var tcs = _dialogTcs;
        _dialogTcs = null;
        tcs?.TrySetResult(result);
        _focusBeforeDialog?.Focus();
    }

    private void OnDialogConfirm(object sender, RoutedEventArgs e) => CloseDialog(true);

    private void OnDialogCancel(object sender, RoutedEventArgs e) => CloseDialog(false);

    private void OnDialogKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        CloseDialog(false);
        e.Handled = true;
    }

    // ===================== Командная палитра (Ctrl+K) =====================

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (DialogOverlay.Visibility == Visibility.Visible || SafetyOverlay.Visibility == Visibility.Visible) return;

            if (PaletteOverlay.Visibility == Visibility.Visible) ClosePalette();
            else OpenPalette();
            e.Handled = true;
        }
    }

    private List<PaletteCommand> BuildPaletteCommands()
    {
        string Go(string name) => Loc.F("ui.palette.go", name);

        return new List<PaletteCommand>
        {
            new(Go(Loc.T("t.ochistka")), () => NavigateTo("cleanup")),
            new(Go(Loc.T("t.optimizaciya")), () => NavigateTo("optimize")),
            new(Go(Loc.T("ui.startup.nav")), () => NavigateTo("startup")),
            new(Go(Loc.T("t.sistema")), () => NavigateTo("system")),
            new(Go(Loc.T("t.privatnost")), () => NavigateTo("privacy")),
            new(Go(Loc.T("t.disk")), () => NavigateTo("disk")),
            new(Go(Loc.T("settings.title")), () => NavigateTo("settings")),
            new(Loc.T("ui.palette.run_cleanup"), () =>
            {
                NavigateTo("cleanup");
                if (_cleanupPage.DataContext is CleanupViewModel vm && vm.RunCommand.CanExecute(null))
                    vm.RunCommand.Execute(null);
            }),
            new(Loc.T("ui.palette.analyze"), () =>
            {
                NavigateTo("disk");
                if (_diskPage.DataContext is DiskViewModel vm && vm.ScanCommand.CanExecute(null))
                    vm.ScanCommand.Execute(null);
            }),
            new(Loc.T("ui.palette.history"), () => NavigateTo("history")),
            new(Go(Loc.T("news.title")), () => NavigateTo("whatsnew"))
        };
    }

    private void OpenPalette()
    {
        if (IsOperationActive || SafetyOverlay.Visibility == Visibility.Visible) return;
        _paletteAll = BuildPaletteCommands();
        PaletteInput.Text = "";
        PaletteList.ItemsSource = _paletteAll;
        PaletteList.SelectedIndex = 0;
        PaletteOverlay.Visibility = Visibility.Visible;
        PaletteInput.Focus();
    }

    private void ClosePalette()
    {
        PaletteOverlay.Visibility = Visibility.Collapsed;
    }

    private void RunPaletteSelection()
    {
        if (IsOperationActive) { ClosePalette(); return; }
        if (PaletteList.SelectedItem is not PaletteCommand command) return;
        ClosePalette();
        command.Run();
    }

    private void OnPaletteTextChanged(object sender, TextChangedEventArgs e)
    {
        var query = PaletteInput.Text.Trim();
        var items = query.Length == 0
            ? _paletteAll
            : _paletteAll.Where(c => c.Title.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        PaletteList.ItemsSource = items;
        PaletteList.SelectedIndex = items.Count > 0 ? 0 : -1;
    }

    private void OnPaletteKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                ClosePalette();
                e.Handled = true;
                break;
            case Key.Enter:
                RunPaletteSelection();
                e.Handled = true;
                break;
            case Key.Down:
                if (PaletteList.Items.Count > 0)
                    PaletteList.SelectedIndex = Math.Min(PaletteList.SelectedIndex + 1, PaletteList.Items.Count - 1);
                e.Handled = true;
                break;
            case Key.Up:
                if (PaletteList.Items.Count > 0)
                    PaletteList.SelectedIndex = Math.Max(PaletteList.SelectedIndex - 1, 0);
                e.Handled = true;
                break;
        }
    }

    private void OnPaletteItemClick(object sender, MouseButtonEventArgs e) => RunPaletteSelection();

    private void OnPaletteBackdropClick(object sender, MouseButtonEventArgs e) => ClosePalette();

    private void OnPaletteCardClick(object sender, MouseButtonEventArgs e) => e.Handled = true;

    // ===================== История очисток =====================

    private async Task ShowHistoryAsync()
    {
        var history = UserSettings.GetCleanupHistory();
        var text = history.Count == 0
            ? Loc.T("ui.history.empty")
            : string.Join("\n", history.Take(10).Select(h =>
                Loc.F("ui.history.line", h.Time.ToString("dd.MM.yyyy HH:mm"), FormatBytes(h.BytesQuarantined > 0 ? h.BytesQuarantined : h.BytesFreed), h.Operations)));

        await Dialogs.InfoAsync(Loc.T("ui.history.title"), text);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        var i = 0;
        while (value >= 1024 && i < units.Length - 1)
        {
            value /= 1024;
            i++;
        }
        return value.ToString("0.##") + " " + units[i];
    }
}
