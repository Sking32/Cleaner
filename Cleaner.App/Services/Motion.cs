using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Cleaner.App.Services;

/// <summary>
/// Анимации интерфейса. Правила: только прозрачность и сдвиг (никаких анимаций размеров),
/// короткая длительность (≤ 250 мс), плавное торможение, без «пружин».
/// Уважает системную настройку «Показывать анимацию в Windows» (аналог prefers-reduced-motion).
/// </summary>
public static class Motion
{
    private static readonly TimeSpan EnterDuration = TimeSpan.FromMilliseconds(220);

    public static bool Enabled => UserSettings.GetAnimations() && SystemParameters.ClientAreaAnimation;

    /// <summary>Появление: fade + сдвиг вверх на 8 px. После окончания элемент возвращается в исходное состояние.</summary>
    public static void PlayEnter(FrameworkElement? element)
    {
        if (element == null || !Enabled) return;

        var duration = new Duration(EnterDuration);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var translate = new TranslateTransform(0, 0);
        element.RenderTransform = translate;

        var fade = new DoubleAnimation(0, 1, duration)
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop
        };
        var slide = new DoubleAnimation(8, 0, duration)
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop
        };

        element.BeginAnimation(UIElement.OpacityProperty, fade);
        translate.BeginAnimation(TranslateTransform.YProperty, slide);
    }
}
