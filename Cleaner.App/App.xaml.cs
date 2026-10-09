using Cleaner.App.Services;
using Cleaner.App.Views;
using Cleaner.Core.Data;
using Cleaner.Core.Services;
using Cleaner.Core.Services.Cleanup;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Windows;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Cleaner.App;

public partial class App : Application
{
    private IServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var collection = new ServiceCollection();
        ConfigureServices(collection);
        _services = collection.BuildServiceProvider();
        AppServices.Provider = _services;

        ApplicationThemeManager.Apply(
            ApplicationTheme.Dark,
            WindowBackdropType.Mica,
            updateAccent: true);

        var mainWindow = _services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_services is IDisposable d) d.Dispose();
        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<TweakRepository>();
        services.AddSingleton<OperationsRepository>();
        services.AddSingleton<PresetsRepository>();
        services.AddSingleton<LocalizationRepository>(_ => new LocalizationRepository("ru"));

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

        services.AddSingleton<MainWindow>();
        services.AddSingleton<CleanupPage>();
        services.AddSingleton<OptimizePage>();
        services.AddSingleton<SystemPage>();
        services.AddSingleton<PrivacyPage>();
        services.AddSingleton<DiskPage>();
    }
}