using System;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Cleaner.App.ViewModels;
using Cleaner.App.Services;

namespace Cleaner.App.Views;

public partial class DiskPage : Page
{
    private readonly System.Windows.Threading.DispatcherTimer _capacityTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    public DiskPage() => InitializeComponent();

    public DiskPage(DiskViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        _capacityTimer.Tick += async (_, _) => await vm.RefreshCapacityAsync();
        Loaded += async (_, _) => { UserSettings.DiskChanged += RefreshCapacity; _capacityTimer.Start(); await vm.RefreshCapacityAsync(); };
        Unloaded += (_, _) => { _capacityTimer.Stop(); UserSettings.DiskChanged -= RefreshCapacity; };
    }
    private async void RefreshCapacity() { if (DataContext is DiskViewModel vm) await vm.RefreshCapacityAsync(); }

    private void AnimateIn()
    {
        Opacity = 0;
        var fade = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(320),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(OpacityProperty, fade);

        var translate = new TranslateTransform(0, 18);
        RenderTransform = translate;
        var slide = new DoubleAnimation
        {
            From = 18,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(380),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        translate.BeginAnimation(TranslateTransform.YProperty, slide);
    }
}
