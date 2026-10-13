using System;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Cleaner.App.ViewModels;

namespace Cleaner.App.Views;

public partial class OptimizePage : Page
{
    public OptimizePage() => InitializeComponent();

    public OptimizePage(OptimizeViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void AnimateIn()
    {
        // Fade
        Opacity = 0;
        var fade = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(320),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(OpacityProperty, fade);

        // Slide up
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
