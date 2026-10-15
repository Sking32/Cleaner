using System.Dynamic;
using System.Diagnostics;
using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Markup;
using System.Windows.Data;
using Cleaner.App;
using Cleaner.App.Localization;
using Cleaner.App.Services;
using Cleaner.App.ViewModels;
using Cleaner.App.Views;
using Cleaner.Core.Data;
using Cleaner.Core.Services;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try { Render(args); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void Render(string[] args)
    {
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo != null && !File.Exists(Path.Combine(repo.FullName, "Cleaner.slnx"))) repo = repo.Parent;
        if (repo == null) throw new InvalidOperationException("Run the preview from the Cleaner checkout.");
        var root = Path.GetFullPath(Path.Combine(repo.FullName, ".test-sandbox", "visual-" + Guid.NewGuid().ToString("N")));
        for (var current = root; current != null; current = Path.GetDirectoryName(current))
            if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Preview output may not use junctions or symbolic links.");
        Directory.CreateDirectory(root);
        AppLog.UsePreviewDirectory(Path.Combine(root, "logs"));
        Environment.SetEnvironmentVariable("TEMP", root);
        Environment.SetEnvironmentVariable("TMP", root);
        var store = new AppSettingsStore(Path.Combine(root, "settings.json"));
        store.Load().Language = args.Contains("en") ? "en" : "ru";
        store.Load().Animations = false;
        store.Load().Theme = args.Contains("--light") ? "light" : "dark";
        if (!store.Save()) throw new InvalidOperationException("Cannot save isolated fixture preferences: " + store.LastError);
        UserSettings.UsePreviewStore(store);
        var bindingTrace = new BindingTraceListener();
        PresentationTraceSources.DataBindingSource.Listeners.Add(bindingTrace);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
        Console.WriteLine("Preview fixtures: " + root);
        // A plain Application has no Cleaner startup callback, state lease, prune, DI or elevation prompt.
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var appXaml = File.ReadAllText(Path.Combine(repo.FullName, "Cleaner.App", "App.xaml"));
        var start = appXaml.IndexOf("<ResourceDictionary>", StringComparison.Ordinal);
        var end = appXaml.LastIndexOf("</ResourceDictionary>", StringComparison.Ordinal) + "</ResourceDictionary>".Length;
        var resources = appXaml[start..end].Replace("<ResourceDictionary>", "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:ui=\"http://schemas.lepo.co/wpfui/2022/xaml\">")
            .Replace("Themes/Tokens.xaml", "/Cleaner.App;component/Themes/Tokens.xaml");
        app.Resources = (ResourceDictionary)XamlReader.Parse(resources); Console.WriteLine("Resources loaded");
        ApplicationThemeManager.Apply(ApplicationTheme.Dark);
        Appearance.Apply();
        if (args.Contains("--diagnostics-only"))
        {
            CheckDiagnosticPreview(root);
            File.WriteAllLines(Path.Combine(root, "binding-diagnostics.txt"), bindingTrace.Messages);
            if (bindingTrace.Messages.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, bindingTrace.Messages));
            Console.WriteLine(root); return;
        }
        var cleanup = new CleanupPage { DataContext = CleanupFixture() };
        var privacy = new PrivacyPage { DataContext = PrivacyFixture(root) };
        var optimize = new OptimizePage { DataContext = OptimizeFixture() };
        var startup = new StartupPage { DataContext = StartupFixture() };
        var system = new SystemPage { DataContext = SystemFixture() };
        var disk = new DiskPage { DataContext = DiskFixture(root) };
        var fixtureStartup = new FixtureStartup();
        var settings = new SettingsPage(new SettingsViewModel(fixtureStartup));
        var history = new HistoryPage(new HistoryViewModel(() => Enumerable.Range(0, 5).Select(i => CleanupHistoryEntry.FromReport(
            new Cleaner.Core.Services.Cleanup.CleanupReport { AppVersion = AppVersion.Current, CreatedUtc = DateTime.UtcNow.AddDays(-i), Result = new() {
                OperationsRun = 1, FilesQuarantined = 1, BytesQuarantined = (i + 1) * 1048576,
                FilesDeleted = i % 2, BytesFreed = i % 2 * 524288 } }, "applied")).ToArray()));
        var news = new WhatsNewPage(new WhatsNewViewModel());
        if (((WhatsNewViewModel)news.DataContext).HasError) throw new InvalidOperationException(((WhatsNewViewModel)news.DataContext).ErrorText);
        Console.WriteLine("Pages loaded");
        var fixturePoints = 0;
        var safetyVm = new FirstRunSafetyViewModel(() => true, () => throw new InvalidOperationException("Preview must never request elevation"),
            () => { fixturePoints++; return Task.FromResult(new RestorePointResult(true, "fixture")); }, UserSettings.AcknowledgeSafetyNotice);
        var window = new MainWindow(cleanup, optimize, startup, system, privacy, disk, settings, history, news, null!, new SnackbarService(), safetyVm);
        var frame = (Frame)window.FindName("RootFrame");
        var navigation = (ListBox)window.FindName("RootNavigation");
        var titleBar = (TitleBar)window.FindName("AppTitleBar");
        if (titleBar.Title != "Cleaner · " + AppVersion.Current || window.Title != titleBar.Title)
            throw new InvalidOperationException("The header and window title must show the assembly version.");
        if (navigation.Items.Cast<ListBoxItem>().Any(i => (string)i.Tag is "history" or "whatsnew") || navigation.Items.Count != 6)
            throw new InvalidOperationException("History and release notes must be separate from the six primary tools.");
        ((Border)window.FindName("StatusBarHost")).DataContext = new { CpuText = "CPU · 8%", RamText = "RAM · 6.7 / 16 GB", DiskText = "C: · 128 GB" };
        Console.WriteLine("Shell loaded");
        var initializedCaptionButtons = new HashSet<TitleBarButton>();
        // No Show(), Run(), startup code, real filesystem operations or OS mutations.
        foreach (var (tag, page) in new (string, Page)[] { ("cleanup", cleanup), ("privacy", privacy), ("optimize", optimize), ("startup", startup), ("system", system), ("disk", disk), ("settings", settings), ("history", history), ("whatsnew", news) })
        {
            if (args.Contains("--settings-layout-only") && tag != "settings") continue;
            Console.WriteLine("Rendering " + tag);
            navigation.SelectedItem = navigation.Items.Cast<ListBoxItem>().FirstOrDefault(i => (string)i.Tag == tag);
            frame.Navigate(page);
            Pump(500);
            var warmupVisual = (FrameworkElement)window.Content;
            warmupVisual.Measure(new Size(1360, 860)); warmupVisual.Arrange(new Rect(0, 0, 1360, 860));
            warmupVisual.UpdateLayout(); Pump(600);
            CheckPressedButtons(page);
            if (args.Contains("--bindings-only")) continue;
            foreach (var (width, height, scale) in new[] { (1360, 860, 1.0), (1120, 700, 1.0), (1360, 860, 2.0), (1120, 700, 2.0) })
            {
                var visual = (FrameworkElement)window.Content;
                visual.Measure(new Size(width, height)); visual.Arrange(new Rect(0, 0, width, height));
                visual.UpdateLayout();
                // WPF-UI initializes RenderButtonsForeground in Loaded. This headless host
                // never shows the Window; raise only the dependency's caption Loaded event,
                // without invoking Cleaner production startup or OS integration.
                foreach (var caption in Descendants(visual).OfType<TitleBarButton>())
                    if (initializedCaptionButtons.Add(caption)) caption.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, caption));
                if (tag == "cleanup")
                {
                    foreach (var name in new[] { "SelectAllButton", "SelectNoneButton" })
                    {
                        var button = (Wpf.Ui.Controls.Button)cleanup.FindName(name);
                        var bounds = button.TransformToAncestor(visual).TransformBounds(new Rect(button.RenderSize));
                        var expected = name == "SelectAllButton" ? "SelectAllCommand" : "SelectNoneCommand";
                        if (BindingOperations.GetBinding(button, Wpf.Ui.Controls.Button.CommandProperty)?.Path?.Path != expected ||
                            button.ActualHeight < 20 || bounds.Left < 0 || bounds.Right > width || bounds.Bottom > height)
                            throw new InvalidOperationException("Bulk selection button is clipped or incorrectly bound: " + name);
                    }
                }
                if (tag == "disk")
                {
                    var map = (FrameworkElement)disk.FindName("DiskMap");
                    if (map.ActualHeight < 70) throw new InvalidOperationException("The disk gauge leaves too little space for its map.");
                    var detail = Descendants(disk).OfType<System.Windows.Controls.TextBlock>().First(t =>
                        BindingOperations.GetBinding(t, System.Windows.Controls.TextBlock.TextProperty)?.Path?.Path == "MapSelectionText");
                    var mapBottom = map.TranslatePoint(new Point(0, map.ActualHeight), visual).Y;
                    if (mapBottom > detail.TranslatePoint(new Point(), visual).Y)
                        throw new InvalidOperationException("The disk map overlaps its full-path details at this window size.");
                }
                if (width == 1120)
                    foreach (var name in new[] { "HistoryButton", "WhatsNewButton", "SettingsButton" })
                    {
                        var button = (FrameworkElement)window.FindName(name);
                        var bounds = button.TransformToAncestor(visual).TransformBounds(new Rect(button.RenderSize));
                        if (button.ActualHeight < 20 || bounds.Top < 0 || bounds.Bottom > height || bounds.Right > width)
                            throw new InvalidOperationException("Secondary navigation is clipped: " + name);
                    }
                if (width == 1120 && tag is "cleanup" or "optimize" or "system")
                {
                    var rowStyle = app.Resources["CompactRowStyle"];
                    var visible = Descendants(page).OfType<Border>().Count(row =>
                    {
                        if (!ReferenceEquals(row.Style, rowStyle) || row.ActualHeight == 0) return false;
                        var parent = VisualTreeHelper.GetParent(row);
                        while (parent != null && parent is not ScrollViewer) parent = VisualTreeHelper.GetParent(parent);
                        if (parent is not ScrollViewer viewport) return false;
                        var bounds = row.TransformToAncestor(viewport).TransformBounds(new Rect(row.RenderSize));
                        return bounds.Top >= 0 && bounds.Bottom <= viewport.ActualHeight;
                    });
                    Console.WriteLine($"{tag}: {visible} complete rows visible at 1120x700.");
                    if (visible < (tag == "system" ? 7 : tag == "cleanup" ? 4 : 6))
                        throw new InvalidOperationException("The compact layout shows too few complete rows: " + tag);
                }
                Pump(500);
                visual.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)(width * scale), (int)(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                bitmap.Render(visual);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(root, $"{tag}-{width}x{height}-{scale:0}x.png"));
                encoder.Save(file);
            }
            if (tag == "cleanup")
            {
                var tabs = (TabControl)cleanup.FindName("OperationTabs");
                tabs.SelectedIndex = 1; Pump(200);
                var visual = (FrameworkElement)window.Content;
                visual.Measure(new Size(1120, 700)); visual.Arrange(new Rect(0, 0, 1120, 700)); visual.UpdateLayout();
                if (!Descendants(cleanup).OfType<System.Windows.Controls.TextBlock>().Any(t => t.Text == Loc.T("op.temp_user.name")))
                    throw new InvalidOperationException("Analysis result fixture was not displayed.");
                var bitmap = new RenderTargetBitmap(1120, 700, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var file = File.Create(Path.Combine(root, "cleanup-preview-1120x700-1x.png"))) encoder.Save(file);
                tabs.SelectedIndex = 2; SaveFixtureImage(window, root, "cleanup-categories");
                tabs.SelectedIndex = 0;
            }
            if (tag == "history" && !args.Contains("--bindings-only"))
            {
                SaveFixtureImage(window, root, "history-chart");
                var reportDetails = (Expander)history.FindName("HistoryDetails");
                reportDetails.IsExpanded = true;
                SaveFixtureImage(window, root, "history-details");
                reportDetails.IsExpanded = false;
                var original = history.DataContext; history.DataContext = new HistoryViewModel(() => Array.Empty<CleanupHistoryEntry>());
                SaveFixtureImage(window, root, "history-empty"); history.DataContext = original;
            }
            if (tag == "privacy" && !args.Contains("--bindings-only"))
            {
                var filesTab = Descendants(privacy).OfType<TabControl>().Single(t => t.Items.Cast<TabItem>().Any(i => Equals(i.Header, Loc.T("recovery.files.title"))));
                filesTab.SelectedItem = filesTab.Items.Cast<TabItem>().Single(i => Equals(i.Header, Loc.T("recovery.files.title")));
                SaveFixtureImage(window, root, "recovery-files");
                if (!Descendants(privacy).OfType<System.Windows.Controls.TextBlock>().Any(t => t.Text == "old-cache.tmp"))
                    throw new InvalidOperationException("Quarantine file details were not rendered.");
                filesTab.SelectedItem = filesTab.Items.Cast<TabItem>().Single(i => Equals(i.Header, Loc.T("design.session.details")));
                SaveFixtureImage(window, root, "recovery-details");
                filesTab.SelectedIndex = 0;
            }
            if (tag == "disk" && !args.Contains("--bindings-only"))
            {
                var gauge = Descendants(disk).OfType<Cleaner.App.Controls.DiskGauge>().Single();
                foreach (var percent in new[] { 0.0, 70, 90, 100, double.NaN })
                {
                    gauge.Percent = percent; SaveFixtureImage(window, root, "disk-gauge-" + (double.IsNaN(percent) ? "missing" : percent.ToString("0")));
                }
            }
            if (tag == "settings" && !args.Contains("--bindings-only"))
            {
                var scroll = Descendants(settings).OfType<ScrollViewer>().First();
                var heading = Descendants(settings).OfType<System.Windows.Controls.TextBlock>().First(t => t.Text == Loc.T("automation.title"));
                scroll.ScrollToVerticalOffset(heading.TranslatePoint(new Point(), (UIElement)scroll.Content).Y - 10); Pump(100);
                var visual = (FrameworkElement)window.Content;
                visual.Measure(new Size(1120, 700)); visual.Arrange(new Rect(0, 0, 1120, 700)); visual.UpdateLayout();
                var bitmap = new RenderTargetBitmap(1120, 700, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var file = File.Create(Path.Combine(root, "settings-automation-1120x700-1x.png"))) encoder.Save(file);
                scroll.ScrollToTop();
            }
        }
        if (args.Contains("--settings-layout-only"))
        {
            File.WriteAllLines(Path.Combine(root, "binding-diagnostics.txt"), bindingTrace.Messages);
            if (bindingTrace.Messages.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, bindingTrace.Messages));
            Console.WriteLine("Focused Settings layout render passed.");
            Console.WriteLine(root);
            app.Shutdown();
            return;
        }
        CheckDiagnosticPreview(root);
        CheckPressedButtons((FrameworkElement)window.Content);
        File.WriteAllLines(Path.Combine(root, "binding-diagnostics.txt"), bindingTrace.Messages);
        if (bindingTrace.Messages.Count > 0)
            throw new InvalidOperationException($"WPF binding diagnostics: {bindingTrace.Messages.Count}. First: {bindingTrace.Messages[0]}");
        Console.WriteLine("All WPF binding checks passed across all pages.");
        var settingsButton = (Wpf.Ui.Controls.Button)window.FindName("SettingsButton");
        if (settingsButton.Content != null || navigation.Items.Cast<ListBoxItem>().Any(i => (string)i.Tag == "settings"))
            throw new InvalidOperationException("Settings must have a separate icon-only button.");
        navigation.SelectedIndex = 0;
        Pump(100);
        settingsButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Pump(100);
        if (!ReferenceEquals(frame.Content, settings) || navigation.SelectedItem != null)
            throw new InvalidOperationException("The settings button did not navigate independently.");
        foreach (var (name, page) in new[] { ("HistoryButton", (Page)history), ("WhatsNewButton", news) })
        {
            navigation.SelectedIndex = 0; Pump(100);
            var button = (Wpf.Ui.Controls.Button)window.FindName(name);
            button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); Pump(100);
            if (!ReferenceEquals(frame.Content, page) || navigation.SelectedItem != null ||
                !ReferenceEquals(button.Background, app.Resources["AccentSoftBrush"]))
                throw new InvalidOperationException("Secondary navigation did not select its page: " + name);
        }
        if (Descendants(system).OfType<System.Windows.Controls.TextBlock>().Any(t =>
            BindingOperations.GetBinding(t, System.Windows.Controls.TextBlock.TextProperty)?.Path?.Path.StartsWith("StatusBar.", StringComparison.Ordinal) == true))
            throw new InvalidOperationException("System must not duplicate live metrics from the header.");
        var details = (Expander)system.FindName("SystemDetails");
        if (details.IsExpanded) throw new InvalidOperationException("System details must start collapsed.");
        details.IsExpanded = true;
        frame.Navigate(system); Pump(100);
        ((FrameworkElement)window.Content).UpdateLayout();
        details.IsExpanded = false;
        Console.WriteLine("Header version, secondary navigation and system details checks passed.");
        var settingsVm = (SettingsViewModel)settings.DataContext;
        var requestedTheme = UserSettings.GetTheme();
        var themeSelector = (ComboBox)settings.FindName("ThemeSelector");
        var cleanupTitle = Descendants(cleanup).OfType<System.Windows.Controls.TextBlock>().First(t => t.Text == Loc.T("t.ochistka"));
        var themeGauge = Descendants(disk).OfType<Cleaner.App.Controls.DiskGauge>().First();
        foreach (var theme in new[] { "light", "dark", requestedTheme })
        {
            themeSelector.SelectedValue = theme; Pump(100);
            var palette = Appearance.Palette(theme, UserSettings.GetAccent());
            frame.Navigate(cleanup); Pump(100);
            var titleColor = ((SolidColorBrush)cleanupTitle.Foreground).Color;
            frame.Navigate(disk); Pump(100);
            if (UserSettings.GetTheme() != theme || new AppSettingsStore(Path.Combine(root, "settings.json")).Load().Theme != theme ||
                titleColor != palette["TextPrimaryBrush"] ||
                ((SolidColorBrush)themeGauge.LabelBrush).Color != palette["TextPrimaryBrush"] ||
                ((SolidColorBrush)((Grid)window.Content).Background).Color != palette["BgBaseBrush"])
                throw new InvalidOperationException($"Live theme {theme}: pref={UserSettings.GetTheme()}, saved={new AppSettingsStore(Path.Combine(root, "settings.json")).Load().Theme}, global={app.Resources["TextPrimaryBrush"]}, title={titleColor}, gauge={themeGauge.LabelBrush}, window={window.Background}, expectedText={palette["TextPrimaryBrush"]}, expectedBg={palette["BgBaseBrush"]}");
        }
        Console.WriteLine("Dark/light selector, existing page resources and preference persistence passed.");
        settingsVm.StartWithWindows = true;
        if (!fixtureStartup.IsEnabled) throw new InvalidOperationException("Startup toggle did not update its in-memory fixture.");
        settingsVm.StartWithWindows = false;
        if (fixtureStartup.IsEnabled) throw new InvalidOperationException("Startup toggle did not turn off its in-memory fixture.");
        settingsVm.AutoCleanupInterval = "60"; settingsVm.AutoCleanupEnabled = true;
        if (!UserSettings.GetAutoCleanupEnabled() || UserSettings.GetAutoCleanupInterval() != 60)
            throw new InvalidOperationException("Automatic cleanup preferences did not save in the fixture profile.");
        settingsVm.AutoCleanupEnabled = false;
        window.StartAutoCleanup();
        if (typeof(MainWindow).GetField("_autoTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window) != null)
            throw new InvalidOperationException("The preview host must never start production automatic cleanup.");
        Console.WriteLine("Automation bindings passed with in-memory startup and isolated preferences; no real timer or registry.");
        settingsVm.Density = "comfortable";
        if ((Thickness)app.Resources["OperationRowPadding"] != new Thickness(14, 10, 14, 10))
            throw new InvalidOperationException("Comfortable density was not applied.");
        // Detached Frame pages refresh DynamicResource expressions when reattached.
        // Assert the visible page's density after the same navigation/layout lifecycle as the app.
        frame.Navigate(system); Pump(100);
        ((FrameworkElement)window.Content).UpdateLayout();
        var densityRow = Descendants(system).OfType<Border>().First(row => ReferenceEquals(row.Style, app.Resources["CompactRowStyle"]));
        if (densityRow.Padding != new Thickness(14, 10, 14, 10)) throw new InvalidOperationException("Existing rows did not react to density changes.");
        settingsVm.Density = "compact";
        Pump(100); ((FrameworkElement)window.Content).UpdateLayout();
        if (densityRow.Padding != new Thickness(10, 6, 10, 6)) throw new InvalidOperationException("Compact density was not restored.");
        settingsVm.Accent = "teal";
        settingsVm.ShowMetrics = false;
        settingsVm.StartPage = "privacy";
        settingsVm.Language = Loc.Language == "ru" ? "en" : "ru";
        var saved = new AppSettingsStore(Path.Combine(root, "settings.json")).Load();
        if (saved.Accent != "teal" || saved.ShowMetrics || saved.StartPage != "privacy" || saved.Language != settingsVm.Language
            || ((Border)window.FindName("StatusBarHost")).Visibility != Visibility.Collapsed || !settingsVm.RestartRequired)
            throw new InvalidOperationException("Settings did not update and persist in the fixture.");
        settingsVm.ResetHintsCommand.Execute(null);
        if (UserSettings.LastSaveError != null) throw new InvalidOperationException("Fixture preferences could not be saved.");
        Console.WriteLine("Settings interaction and persistence checks passed.");
        settingsVm.Language = "auto";
        if (new AppSettingsStore(Path.Combine(root, "settings.json")).Load().Language != null)
            throw new InvalidOperationException("Match Windows must remain automatic after saving.");
        frame.Navigate(cleanup); Pump(100);
        window.ShowFirstRunSafetyIfNeeded();
        var safetyOverlay = (Grid)window.FindName("SafetyOverlay");
        var safetyView = (FirstRunSafetyView)window.FindName("SafetyNotice");
        if (safetyOverlay.Visibility != Visibility.Visible || ((Grid)window.FindName("WorkspaceHost")).IsEnabled || fixturePoints != 0)
            throw new InvalidOperationException("First-run notice must block tools without starting restore operations.");
        foreach (var (width, height) in new[] { (1360, 860), (1120, 700), (1000, 640) })
        {
            var visual = (FrameworkElement)window.Content;
            visual.Measure(new Size(width, height)); visual.Arrange(new Rect(0, 0, width, height)); visual.UpdateLayout(); Pump(100);
            if (safetyOverlay.TransformToAncestor(visual).Transform(new Point(0, 0)).Y < titleBar.ActualHeight)
                throw new InvalidOperationException($"The safety notice must leave the window title bar accessible: overlayY={safetyOverlay.TranslatePoint(new Point(), visual).Y}, titleHeight={titleBar.ActualHeight}, titleBounds={titleBar.TransformToAncestor(visual).TransformBounds(new Rect(titleBar.RenderSize))}.");
            foreach (var name in new[] { "CreatePointButton", "ContinueButton" })
            {
                var button = (FrameworkElement)safetyView.FindName(name);
                var bounds = button.TransformToAncestor(visual).TransformBounds(new Rect(button.RenderSize));
                if (button.ActualHeight < 20 || bounds.Left < 0 || bounds.Right > width || bounds.Bottom > height)
                    throw new InvalidOperationException("Safety notice buttons are clipped: " + name);
            }
            if (!args.Contains("--bindings-only"))
            {
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(root, $"first-run-{width}x{height}-1x.png")); encoder.Save(file);
            }
        }
        CheckPressedButtons(safetyView);
        if (((Wpf.Ui.Controls.Button)safetyView.FindName("CreatePointButton")).Command != safetyVm.CreateRestorePointCommand ||
            ((Wpf.Ui.Controls.Button)safetyView.FindName("ContinueButton")).Command != safetyVm.ContinueCommand)
            throw new InvalidOperationException("Safety buttons must bind to the isolated fixture commands.");
        safetyVm.CreateRestorePointCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        if (fixturePoints != 1 || !safetyVm.HasRestorePoint || !UserSettings.NeedsSafetyNotice)
            throw new InvalidOperationException("Creating a fake point must not acknowledge the notice.");
        safetyVm.ContinueCommand.Execute(null); Pump(100);
        window.ShowFirstRunSafetyIfNeeded();
        if (safetyOverlay.Visibility != Visibility.Collapsed || !((Grid)window.FindName("WorkspaceHost")).IsEnabled || UserSettings.NeedsSafetyNotice)
            throw new InvalidOperationException("Acknowledged notice must not reappear on the next launch.");
        window.ShowFirstRunSafetyIfNeeded(force: true);
        if (safetyOverlay.Visibility != Visibility.Visible) throw new InvalidOperationException("An explicit elevated restart must reopen an acknowledged notice.");
        safetyVm.ContinueCommand.Execute(null);
        settingsVm.ShowSafetyNoticeCommand.Execute(null);
        if (safetyOverlay.Visibility != Visibility.Visible)
            throw new InvalidOperationException("Settings must reopen the safety notice.");
        safetyVm.ContinueCommand.Execute(null);
        Console.WriteLine("Isolated first-run notice, automatic language and fake restore point checks passed.");
        File.WriteAllLines(Path.Combine(root, "binding-diagnostics.txt"), bindingTrace.Messages);
        if (bindingTrace.Messages.Count > 0)
            throw new InvalidOperationException($"WPF binding diagnostics after interactions: {bindingTrace.Messages.Count}. First: {bindingTrace.Messages[0]}");
        Console.WriteLine(root);
        window.Close();
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Send) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static void CheckDiagnosticPreview(string root)
    {
        var type = typeof(MainWindow).Assembly.GetType("Cleaner.App.Services.DiagnosticExportPreview")!;
        var report = new { AppVersion = AppVersion.Current, Path = @"C:\Users\Fixture\private.tmp", FilesSkipped = 1, Errors = new[] { @"C:\Users\Fixture\private.tmp: busy" } };
        var dialog = (Window)type.GetMethod("CreateWindow", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null,
            new object[] { report, "fixture", new Action<string>(_ => throw new InvalidOperationException("Preview cannot save a real report.")) })!;
        var content = (FrameworkElement)dialog.Content;
        foreach (var (width, height) in new[] { (680, 500), (440, 320) })
        {
            content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout(); Pump(100);
            var box = Descendants(content).OfType<System.Windows.Controls.TextBox>().Single();
            if (box.Text.Contains("Fixture", StringComparison.Ordinal)) throw new InvalidOperationException("Diagnostic preview must start with private details hidden.");
            foreach (var button in Descendants(content).OfType<System.Windows.Controls.Button>())
                if (button.TranslatePoint(new Point(0, button.ActualHeight), content).Y > height)
                    throw new InvalidOperationException("Diagnostic action was clipped.");
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(root, $"diagnostics-{width}x{height}.png")); encoder.Save(file);
        }
        var checkbox = Descendants(content).OfType<CheckBox>().Single(); var preview = Descendants(content).OfType<System.Windows.Controls.TextBox>().Single();
        CheckPressedButtons(content);
        checkbox.IsChecked = true; if (!preview.Text.Contains("Fixture", StringComparison.Ordinal)) throw new InvalidOperationException("Private details opt-in did not work.");
        checkbox.IsChecked = false; if (preview.Text.Contains("Fixture", StringComparison.Ordinal)) throw new InvalidOperationException("Private details were not hidden again.");
        dialog.Close(); Console.WriteLine("Diagnostic preview: redaction and layout passed; no file dialog or export was opened.");
    }

    private static void SaveFixtureImage(MainWindow window, string root, string name)
    {
        Pump(120); var visual = (FrameworkElement)window.Content;
        visual.Measure(new Size(1120, 700)); visual.Arrange(new Rect(0, 0, 1120, 700)); visual.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1120, 700, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(root, name + "-1120x700-1x.png")); encoder.Save(file);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void CheckPressedButtons(FrameworkElement page)
    {
        // Change visual state only on fixture controls; do not raise clicks or execute commands.
        var hoverKey = (DependencyPropertyKey?)typeof(UIElement).GetField("IsMouseOverPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null)
            ?? throw new InvalidOperationException("Cannot simulate the isolated hover state.");
        var pressedKey = (DependencyPropertyKey?)typeof(System.Windows.Controls.Primitives.ButtonBase).GetField("IsPressedPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null)
            ?? throw new InvalidOperationException("Cannot simulate the isolated pressed state.");
        var buttons = Descendants(page).OfType<Wpf.Ui.Controls.Button>().Where(button => button.IsEnabled).ToArray();
        foreach (var button in buttons)
        {
            var wasHovered = button.IsMouseOver;
            var wasPressed = button.IsPressed;
            var normalForeground = button.Foreground;
            try
            {
                button.SetValue(hoverKey, true);
                button.SetValue(pressedKey, true);
                _ = button.Foreground;
                Pump(20);
                var expression = BindingOperations.GetBindingExpression(button, Control.ForegroundProperty);
                if (expression?.ParentBinding.Path?.Path == "PressedForeground")
                {
                    if (expression.Status != BindingStatus.Active || expression.ParentBinding.RelativeSource?.Mode != RelativeSourceMode.Self
                        || !ReferenceEquals(button.Foreground, button.PressedForeground))
                        throw new InvalidOperationException($"Broken pressed foreground binding: {expression.Status}; template parent: {button.TemplatedParent?.GetType().Name ?? "null"}.");
                }
            }
            finally
            {
                button.SetValue(pressedKey, wasPressed);
                button.SetValue(hoverKey, wasHovered);
            }
            if (!ReferenceEquals(button.Foreground, normalForeground))
                throw new InvalidOperationException("The button did not restore its normal text color.");
        }
        Console.WriteLine($"Checked {buttons.Length} fixture buttons in {page.GetType().Name}.");
    }

    private sealed class BindingTraceListener : TraceListener
    {
        public List<string> Messages { get; } = new();
        public override void Write(string? message) { if (message != null) Messages.Add(message); }
        public override void WriteLine(string? message) => Write(message);
    }

    private sealed class FixtureStartup : IStartupRegistration
    {
        public bool IsEnabled { get; private set; }
        public void SetEnabled(bool enabled) => IsEnabled = enabled;
    }

    private static dynamic Bag(string page)
    {
        var bag = new ExpandoObject(); var values = (IDictionary<string, object?>)bag;
        // All actions are inert. Commands cannot invoke production services from this host.
        var xaml = File.ReadAllText(Path.Combine(FindRepo(), "Cleaner.App", "Views", page + "Page.xaml"));
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(xaml, @"\{Binding (?:DataContext\.)?(\w+)"))
        {
            var key = m.Groups[1].Value;
            values[key] = key.EndsWith("Command") ? new RelayCommand(() => { }) : null;
        }
        values["ShowHelp"] = false; values["IsBusy"] = false; values["IsRunning"] = false; values["StatusText"] = Loc.T("t.gotovo");
        return bag;
    }
    private static string FindRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Cleaner.slnx"))) dir = dir.Parent;
        return dir!.FullName;
    }

    private static object CleanupFixture()
    {
        dynamic vm = Bag("Cleanup");
        var repo = new OperationsRepository(); var presets = new PresetsRepository(); var loc = new LocalizationRepository(Loc.Language);
        var remainingChecked = 12;
        vm.Presets = presets.CleanupPresets.Select(p => new CleanupPresetViewModel(p.Key, loc[p.Value.NameKey], loc[p.Value.ShortKey], loc[p.Value.NoteKey], p.Value.Keys)).ToArray();
        vm.Categories = repo.Sections.Select(s => new
        {
            Name = loc[s.NameKey], IconSymbol = SymbolRegular.Folder24,
            Operations = repo.Operations.Where(o => "sec." + o.Category == s.Key).Select(o =>
            {
                var item = new CleanupOperationViewModel(o, loc[o.NameKey], loc[o.DescKey].Replace("`n", "\n"));
                item.IsChecked = remainingChecked-- > 0;
                return item;
            }).ToArray()
        }).Where(c => c.Operations.Length > 0).ToArray();
        vm.CheckedCount = 12; vm.PreviewSizeText = "1.24 GB"; vm.CurrentStage = 0; vm.CanEditSelection = true;
        vm.ExpertMode = true;
        vm.CategoryChart = new[] { new CategoryChartItem("quick", Loc.T("section.sec.quick.name"), 1048576, 1048576), new CategoryChartItem("browsers", Loc.T("section.sec.browsers.name"), 524288, 1048576) };
        vm.HasCategoryChart = true;
        vm.FileModes = new[] { new CleanupModeChoice(Cleaner.Core.Services.Cleanup.CleanupFileMode.Quarantine, Loc.T("cleanup.mode.quarantine")),
            new CleanupModeChoice(Cleaner.Core.Services.Cleanup.CleanupFileMode.PermanentDelete, Loc.T("cleanup.mode.delete")) };
        vm.FileMode = Cleaner.Core.Services.Cleanup.CleanupFileMode.Quarantine;
        vm.FileModeHint = Loc.T("cleanup.mode.quarantine.hint");
        vm.RunButtonText = Loc.F("t.zapustit_2", 12); vm.HasPresetNote = false; vm.ResultSummary = "";
        vm.CustomPresets = Array.Empty<UserCleanupPreset>(); vm.PresetEditorOpen = false; vm.ResultTabIndex = 0; vm.HasResults = true;
        vm.PreviewNote = Loc.F("preview.sample.note", 0, 0); vm.CategorySummary = Loc.T("section.sec.quick.name") + ": 1 MB";
        vm.ResultRows = new[] { new CleanupDetailRow(new Cleaner.Core.Services.Cleanup.OperationOutcome { Key = "temp_user", Handler = "PathCleanup", Success = true, PreviewBytes = 1048576 }, repo, true) };
        vm.CompactSummary = Loc.F("preview.compact", 12, "1.24 GB");
        vm.BytesFreedText = ""; return vm;
    }
    private static object PrivacyFixture(string root)
    {
        dynamic vm = Bag("Privacy");
        var sessions = new[]
        {
            new QuarantineSessionItem(new QuarantineSession { Id = "preview-01", Directory = Path.Combine(root, "quarantine", "preview-01"), CreatedUtc = new DateTime(2026, 10, 13, 1, 32, 0, DateTimeKind.Utc), Count = 176, TotalSizeBytes = 1331440000 }),
            new QuarantineSessionItem(new QuarantineSession { Id = "preview-02", Directory = Path.Combine(root, "quarantine", "preview-02"), CreatedUtc = new DateTime(2026, 10, 12, 23, 18, 0, DateTimeKind.Utc), Count = 5, TotalSizeBytes = 34000000, Health = QuarantineHealth.NeedsRecovery, Issues = new[] { "Preview: recovery is required for a pending entry." }, CanRestore = true })
        };
        vm.FilteredSessions = sessions; vm.SelectedSession = sessions[0]; vm.HasSelectedSession = true;
        vm.HasNoVisibleSessions = false; vm.SessionSearchText = ""; vm.EmptySessionsText = Loc.T("t.karantin_pust");
        vm.FilteredFiles = new[] { new QuarantineFileItem(new QuarantineEntry { OriginalPath = @"C:\Example\old-cache.tmp", SizeBytes = 1048576 }) };
        vm.FileSearchText = ""; vm.PendingFilesOnly = false; vm.FileListStatus = Loc.F("recovery.files.count", 1);
        vm.QuarantineSummary = Loc.F("t.sessiy_obektov", 2, 181, "1.27 GB");
        vm.WhitelistRules = new[] { "C:\\Example\\Important\\*", "*.project" }; vm.HasWhitelistRules = true;
        vm.WhitelistPath = Path.Combine(root, "whitelist.json"); vm.NewRuleText = ""; return vm;
    }
    private static object OptimizeFixture()
    {
        dynamic vm = Bag("Optimize"); var loc = new LocalizationRepository(Loc.Language); var repo = new TweakRepository();
        vm.SubtitleText = Loc.F("ui.optimize.subtitle", repo.Tweaks.Count); vm.HasNoSearchResults = false;
        vm.SearchText = ""; vm.AppliedOnly = false; vm.HasPresetNote = false; vm.HasConflict = false; vm.HasPendingReboot = false;
        vm.Categories = repo.Tweaks.GroupBy(t => t.Category).Select(g => new
        {
            Name = g.Key switch { "perf" => Loc.T("t.proizvoditelnost"), "privacy" => Loc.T("t.privatnost"),
                "win11" => "Windows 11", "network" => Loc.T("t.set"), "maint" => Loc.T("t.obsluzhivanie"),
                "games" => Loc.T("t.igry"), _ => g.Key },
            IconSymbol = SymbolRegular.Settings24, CountText = "0 / " + g.Count(), HasVisibleTweaks = true,
            Tweaks = g.Select(t => new OptimizeTweakViewModel(t, loc[t.NameKey], loc[t.ShortKey], loc[t.DescKey], loc.Has(t.WarnKey) ? loc[t.WarnKey] : "", (_, _) => Task.CompletedTask)).ToArray()
        }).ToArray();
        vm.Presets = new PresetsRepository().OptimizePresets.Select(p => new OptimizePresetViewModel(p.Key, loc[p.Value.NameKey], p.Value.Keys)).ToArray(); return vm;
    }
    private static object StartupFixture()
    {
        dynamic vm = Bag("Startup"); vm.SearchText = ""; vm.HasNoPlans = false; vm.HasNoEntries = false;
        vm.Plans = new[] { new { DisplayName = "Balanced", Description = "Preview data", IsActive = true }, new { DisplayName = "High performance", Description = "Preview data", IsActive = false } };
        vm.FilteredEntries = new[] { new StartupPreviewItem() };
        return vm;
    }

    public sealed class StartupPreviewItem
    {
        public string Name => "Example application";
        public string Command => "C:\\Example\\App.exe";
        public string SourceText => "User";
        public string ImpactLevel => "low";
        public string ImpactText => "Low";
        public string ImpactHint => "Preview";
        public bool IsEnabled { get; set; } = true;
    }
    private static object SystemFixture()
    {
        dynamic vm = Bag("System"); vm.StatusBar = new { CpuPercentText = "8%", RamPercentText = "42%", DiskLabelText = "C:", DiskFreeText = "128 GB" };
        vm.WindowsVersion = "Windows 11"; vm.WindowsBuild = "Preview"; vm.Architecture = "X64"; vm.DotNetVersion = ".NET 10";
        vm.MachineName = "Preview PC"; vm.UserName = "Preview"; vm.TotalMemoryText = "16 GB"; vm.ProcessorCountText = "8"; vm.ProcessorIdentifier = "Preview CPU";
        vm.FilteredServices = Enumerable.Range(1, 24).Select(i => new
        {
            DisplayName = Loc.Language == "ru" ? "Пример службы " + i : "Example service " + i,
            Name = "PreviewService" + i, StatusText = i % 2 == 0 ? "Running" : "Stopped",
            StartTypeRu = "Manual", AdviceLevelName = i % 3 == 0 ? "system" : "recommended",
            AdviceNote = "Preview only", BadgeText = i % 3 == 0 ? "System" : "Optional",
            IsRunning = i % 2 == 0, CanStart = i % 2 != 0
        }).ToArray();
        vm.HasNoServices = false; vm.FoundText = "24"; vm.ServiceFilter = ""; vm.ServiceFilterMode = 0; vm.FilterModes = new[] { "All" }; return vm;
    }
    private static object DiskFixture(string root)
    {
        dynamic vm = Bag("Disk"); vm.CanSelectDrive = true; vm.IsScanning = false; vm.IsMutating = false;
        vm.ExpertMode = false;
        vm.Drives = new[] { new { Display = "Example drive · 512 GB", UsedPercent = 75.0 } }; vm.SelectedDrive = vm.Drives[0];
        vm.CapacityText = Loc.F("analytics.capacity", "384 GB", "128 GB", "512 GB");
        vm.TreemapNodes = new[] { new DiskTreeNode("Projects — a folder with a very long name", Path.Combine(root, "Projects"), 182L * 1073741824, true),
            new DiskTreeNode("Media", Path.Combine(root, "Media"), 96L * 1073741824, true), new DiskTreeNode("Downloads", Path.Combine(root, "Downloads"), 64L * 1073741824, true),
            new DiskTreeNode("very-long-file-name.log", Path.Combine(root, "fixture.log"), 18L * 1048576, false) };
        vm.MapSelectionText = vm.TreemapNodes[0].Name + "\n" + vm.TreemapNodes[0].Path;
        vm.HasNoTreemap = false; vm.IsTreemapBusy = false; vm.HasShadowText = false; vm.HasNoResults = false;
        vm.TopFolders = new[] {
            new { Name = "Projects — a folder with a very long name", Path = Path.Combine(root, "Projects"), SizeText = "182 GB" },
            new { Name = "Media", Path = Path.Combine(root, "Media"), SizeText = "96 GB" },
            new { Name = "Downloads", Path = Path.Combine(root, "Downloads"), SizeText = "64 GB" }
        };
        vm.LargeFiles = new[] {
            new { Path = Path.Combine(root, "Media", "example-video.mp4"), SizeText = "2.4 GB" },
            new { Path = Path.Combine(root, "Downloads", "example-archive.zip"), SizeText = "1.2 GB" }
        };
        vm.FileFilters = new[] { Loc.T("ui.disk.filter.all"), "> 100 MB", "> 1 GB" }; vm.FileFilterIndex = 0;
        vm.Breadcrumbs = new[] { new { Title = "Example drive" } }; vm.SummaryText = "Preview · 3 folders"; vm.ProgressText = ""; return vm;
    }
}
