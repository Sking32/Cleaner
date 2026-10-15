using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Automation.Peers;

namespace Cleaner.App.Controls;

/// <summary>Occupancy only, not a health or performance score.</summary>
public sealed class DiskGauge : FrameworkElement
{
    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(nameof(TrackBrush), typeof(Brush), typeof(DiskGauge),
        new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty LabelBrushProperty = DependencyProperty.Register(nameof(LabelBrush), typeof(Brush), typeof(DiskGauge),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public Brush LabelBrush { get => (Brush)GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }
    public DiskGauge()
    {
        SetResourceReference(TrackBrushProperty, "GaugeTrackBrush");
        SetResourceReference(LabelBrushProperty, "TextPrimaryBrush");
    }
    public static readonly DependencyProperty PercentProperty = DependencyProperty.Register(nameof(Percent), typeof(double), typeof(DiskGauge),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty NoDataTextProperty = DependencyProperty.Register(nameof(NoDataText), typeof(string), typeof(DiskGauge),
        new FrameworkPropertyMetadata("—", FrameworkPropertyMetadataOptions.AffectsRender));
    public double Percent { get => (double)GetValue(PercentProperty); set => SetValue(PercentProperty, value); }
    public string NoDataText { get => (string)GetValue(NoDataTextProperty); set => SetValue(NoDataTextProperty, value); }
    public static double ClampPercent(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 100) : double.NaN;
    public static Color OccupancyColor(double percent)
    {
        percent = ClampPercent(percent);
        if (double.IsNaN(percent)) return Color.FromRgb(100, 116, 139);
        var green = Color.FromRgb(52, 211, 153); var yellow = Color.FromRgb(251, 191, 36); var red = Color.FromRgb(248, 113, 113);
        var a = percent <= 70 ? green : yellow; var b = percent <= 70 ? yellow : red;
        var ratio = percent <= 70 ? percent / 70 : Math.Min(1, (percent - 70) / 20);
        return Color.FromRgb((byte)Math.Round(a.R + (b.R - a.R) * ratio), (byte)Math.Round(a.G + (b.G - a.G) * ratio), (byte)Math.Round(a.B + (b.B - a.B) * ratio));
    }
    protected override Size MeasureOverride(Size available) => new(Math.Min(200, double.IsInfinity(available.Width) ? 200 : available.Width), 116);
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var radius = Math.Max(0, Math.Min((ActualWidth - 20) / 2, ActualHeight - 24));
        if (radius <= 0) return;
        var center = new Point(ActualWidth / 2, radius + 10);
        void Arc(double percent, Brush brush)
        {
            if (percent <= 0) return;
            var angle = Math.PI * (1 - percent / 100);
            var end = new Point(center.X + radius * Math.Cos(angle), center.Y - radius * Math.Sin(angle));
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(center.X - radius, center.Y), false, false);
                ctx.ArcTo(end, new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
            }
            geometry.Freeze();
            dc.DrawGeometry(null, new Pen(brush, 10) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, geometry);
        }
        var value = ClampPercent(Percent);
        Arc(100, TrackBrush);
        if (double.IsFinite(value))
        {
            var fill = new LinearGradientBrush(OccupancyColor(0), OccupancyColor(value), new Point(0, 0), new Point(1, 0));
            if (value > 70) fill.GradientStops.Add(new GradientStop(OccupancyColor(70), 70 / value));
            Arc(value, fill);
        }
        var label = double.IsFinite(value) ? value.ToString("0.#", CultureInfo.CurrentCulture) + "%" : NoDataText;
        var text = new FormattedText(label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"),
            double.IsFinite(value) ? 26 : 14, LabelBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height - 2));
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);
}
