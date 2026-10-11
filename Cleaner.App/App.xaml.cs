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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppLog.PruneOld();
        RegisterGlobalExceptionHandlers();

        var elevated = AdminHelper.IsElevated();
        AppLog.Info($"Запуск программы. Права администратора: {elevated}");

        if (!elevated && !System.Diagnostics.Debugger.IsAttached)
        {
            var answer = System.Windows.MessageBox.Show(
                "Для полной очистки и оптимизации (службы, реестр, системные кэши) нужны права администратора.\n\n" +
                "Перезапустить программу от имени администратора?\n\n" +
                "Если выбрать «Нет», часть функций будет работать с ограничениями или не сработает.",
                "Cleaner",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);

            if (answer == System.Windows.MessageBoxResult.Yes && AdminHelper.TryRestartElevated())
            {
                Shutdown();
                return;
            }
        }

        var collection = new ServiceCollection();
        ConfigureServices(collection);
        _services = collection.BuildServiceProvider();
        AppServices.Provider = _services;

        ApplicationThemeManager.Apply(
            ApplicationTheme.Dark,
            WindowBackdropType.Mica,
            updateAccent: true);

        ApplicationAccentColorManager.ApplySystemAccent();

        var mainWindow = _services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            AppLog.Error("Необработанная ошибка интерфейса", args.Exception);
            System.Windows.MessageBox.Show(
                "Произошла непредвиденная ошибка. Программа продолжит работу.\n\n" +
                args.Exception.Message + "\n\n" +
                "Подробности записаны в журнал:\n" + AppLog.LogDirectory,
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
        services.AddSingleton<LocalizationRepository>(_ => new LocalizationRepository("ru"));

        // --- Core-сервисы ---
        services.AddSingleton<ISafetyService>(_ => new SafetyService(freshMinutes: 5));
        services.AddSingleton<IWhitelistService>(_ => new WhitelistService());
        services.AddSingleton<IBackupService>(_ => new BackupService());
        services.AddSingleton<AppliedTweaksStore>();
        services.AddSingleton<IActionInterpreter, ActionInterpreter>();
        services.AddSingleton<IOptimizeService, OptimizeService>();
        services.AddSingleton<IQuarantineService>(_ => new QuarantineService());

        services.AddSingleton<ICleanupService>(sp =>
            new CleanupService(sp.GetRequiredService<OperationsRepository>()));
        services.AddSingleton<ICleanupRunner, CleanupRunner>();

        // --- ViewModels ---
        services.AddSingleton<CleanupViewModel>();
        services.AddSingleton<OptimizeViewModel>();
        services.AddSingleton<PrivacyViewModel>();
        services.AddSingleton<SystemViewModel>();
        services.AddSingleton<DiskViewModel>();

        // --- Страницы и главное окно ---
        services.AddSingleton<MainWindow>();
        services.AddSingleton<CleanupPage>();
        services.AddSingleton<OptimizePage>();
        services.AddSingleton<SystemPage>();
        services.AddSingleton<PrivacyPage>();
        services.AddSingleton<DiskPage>();
    }
}