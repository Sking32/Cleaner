using Cleaner.App.Localization;
using Cleaner.App.Services;
using Cleaner.App.ViewModels;
using Cleaner.App.Views;
using Cleaner.Core.Data;
using Cleaner.Core.Services;
using Cleaner.Core.Services.Cleanup;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Windows;
using Wpf.Ui;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Cleaner.App;

public partial class App : Application
{
    private IServiceProvider? _services;
    private CleanerStateLease? _stateLease;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!AdminHelper.WaitForParent(e.Args))
        {
            System.Windows.MessageBox.Show(Loc.T("startup.handoff.failed"), "Cleaner", System.Windows.MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(1);
            return;
        }

        AppLog.PruneOld();
        RegisterGlobalExceptionHandlers();

        CoreLog.Sink = (level, message, ex) =>
        {
            if (level == "ERROR") AppLog.Error(message, ex);
            else if (level == "WARN") AppLog.Warn(ex == null ? message : message + Environment.NewLine + ex);
            else AppLog.Info(message);
        };

        var elevated = AdminHelper.IsElevated();
        AppLog.Info($"Запуск программы. Права администратора: {elevated}");

        try
        {
            _stateLease = new CleanerStateLease();
            var pruned = QuarantineService.PruneEmptySessions();
            if (pruned > 0) AppLog.Info($"Удалено пустых сессий карантина: {pruned}");
        }
        catch (Exception pruneEx)
        {
            AppLog.Warn("Не удалось удалить пустые сессии карантина: " + pruneEx.Message);
            System.Windows.MessageBox.Show(pruneEx.Message, "Cleaner", System.Windows.MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        try
        {
            var collection = new ServiceCollection();
            ConfigureServices(collection);
            _services = collection.BuildServiceProvider();
            AppServices.Provider = _services;

            ApplicationThemeManager.Apply(
                ApplicationTheme.Dark,
                WindowBackdropType.Mica,
                updateAccent: true);

            // Единый акцент приложения (а не системный цвет Windows).
            Appearance.Apply();

            var mainWindow = _services.GetRequiredService<MainWindow>();
            mainWindow.Show();
            mainWindow.ShowFirstRunSafetyIfNeeded(force: e.Args.Contains("--show-safety"));
            mainWindow.StartAutoCleanup();
            if (e.Args.Contains("--startup") && !UserSettings.NeedsSafetyNotice) mainWindow.WindowState = WindowState.Minimized;
        }
        catch (Exception ex)
        {
            AppLog.Error("Не удалось запустить Cleaner; данные восстановления сохранены", ex);
            System.Windows.MessageBox.Show(Loc.T("startup.failed") + "\n\n" + ex.Message,
                "Cleaner", System.Windows.MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            AppLog.Error("Необработанная ошибка интерфейса", args.Exception);
            System.Windows.MessageBox.Show(
                Loc.T("t.proizoshla_nepredvidennaya_oshibka_programma") +
                args.Exception.Message + "\n\n" +
                Loc.T("t.podrobnosti_zapisany_v_zhurnal") + AppLog.LogDirectory,
                "Cleaner",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            AppLog.Error("Критическая необработанная ошибка", args.ExceptionObject as Exception);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Error("Необработанная ошибка в фоновой задаче", args.Exception);
            args.SetObserved();
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_services is IDisposable d) d.Dispose();
        _stateLease?.Dispose();
        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // --- Инфраструктура UI ---
        services.AddSingleton<ISnackbarService, SnackbarService>();
        services.AddSingleton<IAppNotifications, AppNotifications>();

        // --- Данные ---
        services.AddSingleton<TweakRepository>();
        services.AddSingleton<OperationsRepository>();
        services.AddSingleton<PresetsRepository>();
        services.AddSingleton<LocalizationRepository>(_ => new LocalizationRepository(UserSettings.GetLanguage()));

        // --- Core-сервисы ---
        services.AddSingleton<ISafetyService>(_ => new SafetyService(freshMinutes: 5,
            extraForbidden: new[] { AppContext.BaseDirectory, System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cleaner") }));
        services.AddSingleton<IWhitelistService>(_ => new WhitelistService());
        services.AddSingleton<IBackupService>(_ => new BackupService());
        services.AddSingleton<AppliedTweaksStore>();
        services.AddSingleton<IActionInterpreter, ActionInterpreter>();
        services.AddSingleton<IOptimizeService>(sp => new OptimizeService(sp.GetRequiredService<TweakRepository>(),
            sp.GetRequiredService<IActionInterpreter>(), sp.GetRequiredService<IBackupService>(),
            sp.GetRequiredService<AppliedTweaksStore>(), sp.GetRequiredService<PresetsRepository>(), expertMode: UserSettings.GetExpertMode));
        services.AddSingleton<IQuarantineService>(_ => new QuarantineService());
        services.AddSingleton<IStartupRegistration>(_ => new StartupRegistration(new Cleaner.Core.Registry.WindowsRegistryAccess(), Environment.ProcessPath ?? ""));

        services.AddSingleton<ICleanupService>(sp =>
            new CleanupService(sp.GetRequiredService<OperationsRepository>()));
        services.AddSingleton<ICleanupRunner, CleanupRunner>();

        // --- ViewModels ---
        services.AddSingleton<CleanupViewModel>();
        services.AddSingleton<OptimizeViewModel>();
        services.AddSingleton<PrivacyViewModel>();
        services.AddSingleton<SystemViewModel>();
        services.AddSingleton<DiskViewModel>();
        services.AddSingleton<StatusBarViewModel>();
        services.AddSingleton<StartupViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<WhatsNewViewModel>();
        services.AddSingleton<FirstRunSafetyViewModel>();

        // --- Страницы и главное окно ---
        services.AddSingleton<MainWindow>();
        services.AddSingleton<CleanupPage>();
        services.AddSingleton<OptimizePage>();
        services.AddSingleton<StartupPage>();
        services.AddSingleton<SystemPage>();
        services.AddSingleton<PrivacyPage>();
        services.AddSingleton<DiskPage>();
        services.AddSingleton<SettingsPage>();
        services.AddSingleton<HistoryPage>();
        services.AddSingleton<WhatsNewPage>();
    }
}
