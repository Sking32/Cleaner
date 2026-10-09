using System;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Cleaner.App.ViewModels;

namespace Cleaner.App.Views;

public partial class CleanupPage : Page
{
    public CleanupPage(CleanupViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Loaded += (_, _) => AnimateIn();
    }

    private void AnimateIn()
    {
        Opacity = 0;

        var fade = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(280),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        BeginAnimation(OpacityProperty, fade);
    }
}