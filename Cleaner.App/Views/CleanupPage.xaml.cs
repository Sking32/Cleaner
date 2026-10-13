using System;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Cleaner.App.ViewModels;

namespace Cleaner.App.Views;

public partial class CleanupPage : Page
{
    public CleanupPage() { InitializeComponent(); SizeChanged += OnSizeChanged; }

    public CleanupPage(CleanupViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        SizeChanged += OnSizeChanged;
    }

    private void OnSizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
    {
        var compact = ActualWidth < 1050;
        SummaryColumn.Width = new System.Windows.GridLength(compact ? 0 : 208);
        SelectionSummary.Visibility = compact ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        CompactSummary.Visibility = compact ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        OperationTabs.Margin = new System.Windows.Thickness(0, 0, compact ? 0 : 20, 0);
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
