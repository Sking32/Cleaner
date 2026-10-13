using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using Cleaner.App.Localization;
using Cleaner.Core.Services;

namespace Cleaner.App.Controls;

/// <summary>
/// Карта диска: площадь прямоугольника пропорциональна размеру папки или файла.
/// Щелчок по папке вызывает <see cref="ActivateCommand"/> (переход вглубь), при наведении — подсветка и подсказка.
/// </summary>
public sealed class TreemapView : FrameworkElement
{
    public static readonly DependencyProperty NodesProperty = DependencyProperty.Register(
        nameof(Nodes), typeof(IReadOnlyList<DiskTreeNode>), typeof(TreemapView),
        new PropertyMetadata(null, (d, _) => ((TreemapView)d).Rebuild()));

    public static readonly DependencyProperty ActivateCommandProperty = DependencyProperty.Register(
        nameof(ActivateCommand), typeof(ICommand), typeof(TreemapView));
    public static readonly DependencyProperty OpenLocationCommandProperty = DependencyProperty.Register(
        nameof(OpenLocationCommand), typeof(ICommand), typeof(TreemapView));
    public static readonly DependencyProperty SelectedNodeProperty = DependencyProperty.Register(
        nameof(SelectedNode), typeof(DiskTreeNode), typeof(TreemapView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public ICommand? OpenLocationCommand { get => (ICommand?)GetValue(OpenLocationCommandProperty); set => SetValue(OpenLocationCommandProperty, value); }
    public DiskTreeNode? SelectedNode { get => (DiskTreeNode?)GetValue(SelectedNodeProperty); set => SetValue(SelectedNodeProperty, value); }
    public TreemapView()
    {
        Focusable = true;
        ToolTipService.SetInitialShowDelay(this, 100);
        ToolTipService.SetShowDuration(this, 30000);
    }

    public IReadOnlyList<DiskTreeNode>? Nodes
    {
        get => (IReadOnlyList<DiskTreeNode>?)GetValue(NodesProperty);
        set => SetValue(NodesProperty, value);
    }

    public ICommand? ActivateCommand
    {
        get => (ICommand?)GetValue(ActivateCommandProperty);
        set => SetValue(ActivateCommandProperty, value);
    }

    private sealed record Cell(Rect Rect, DiskTreeNode Node, int ColorIndex);

    private static readonly Color[] Palette =
    {
        Color.FromRgb(0x33, 0x5B, 0x89), Color.FromRgb(0x28, 0x5D, 0x64),
        Color.FromRgb(0x3A, 0x4D, 0x70), Color.FromRgb(0x55, 0x4D, 0x78),
        Color.FromRgb(0x31, 0x5F, 0x50), Color.FromRgb(0x6A, 0x53, 0x38),
        Color.FromRgb(0x68, 0x48, 0x60), Color.FromRgb(0x48, 0x57, 0x6B)
    };

    private static readonly Typeface LabelFace = new("Segoe UI Variable Text, Segoe UI");

    private readonly List<Cell> _cells = new();
    private int _hover = -1;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height) ? 300 : availableSize.Height;
        return new Size(width, height);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Rebuild();
    }

    private void Rebuild()
    {
        _cells.Clear();
        _hover = -1;
        ToolTip = null;
        SetCurrentValue(SelectedNodeProperty, null);

        var nodes = Nodes;
        if (nodes is { Count: > 0 } && ActualWidth > 4 && ActualHeight > 4)
        {
            var rects = TreemapLayout.Compute(nodes.Select(n => (double)n.SizeBytes).ToList(), ActualWidth, ActualHeight);
            for (var i = 0; i < nodes.Count; i++)
            {
                var r = rects[i];
                if (r.Width < 2 || r.Height < 2) continue;

                var rect = new Rect(r.X + 1.5, r.Y + 1.5, Math.Max(r.Width - 3, 0), Math.Max(r.Height - 3, 0));
                _cells.Add(new Cell(rect, nodes[i], i % Palette.Length));
            }
        }

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        // Прозрачный фон нужен, чтобы мышь «видела» весь элемент.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        for (var i = 0; i < _cells.Count; i++)
        {
            var cell = _cells[i];
            var color = Palette[cell.ColorIndex];
            var hovered = i == _hover;

            byte alpha = cell.Node.IsDirectory ? (hovered ? (byte)255 : (byte)205) : (hovered ? (byte)190 : (byte)115);
            var fill = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
            fill.Freeze();

            Pen? pen = hovered ? new Pen(Brushes.White, 1.5) : null;
            dc.DrawRoundedRectangle(fill, pen, cell.Rect, 6, 6);

            if (cell.Rect.Width >= 24 && cell.Rect.Height >= 18)
                DrawLabel(dc, cell, dpi);
        }
    }

    private static void DrawLabel(DrawingContext dc, Cell cell, double dpi)
    {
        var maxWidth = Math.Max(1, cell.Rect.Width - 10);
        var showSize = cell.Rect.Height >= 46;
        var fontSize = cell.Rect.Width < 70 || cell.Rect.Height < 30 ? 10 : 12;
        dc.PushClip(new RectangleGeometry(cell.Rect));

        var name = new FormattedText(cell.Node.Name, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            LabelFace, fontSize, Brushes.White, dpi)
        {
            MaxTextWidth = maxWidth,
            MaxTextHeight = Math.Max(1, Math.Min(showSize ? cell.Rect.Height - 28 : cell.Rect.Height - 4, 34)),
            Trimming = TextTrimming.CharacterEllipsis
        };

        var size = new FormattedText(FormatBytes(cell.Node.SizeBytes), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            LabelFace, 11, new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), dpi)
        {
            MaxTextWidth = maxWidth,
            MaxTextHeight = 16,
            Trimming = TextTrimming.CharacterEllipsis
        };

        dc.DrawText(name, new Point(cell.Rect.X + 5, cell.Rect.Y + 4));
        if (showSize) dc.DrawText(size, new Point(cell.Rect.X + 5, cell.Rect.Bottom - 19));
        dc.Pop();
    }

    private int HitTest(Point point)
    {
        for (var i = 0; i < _cells.Count; i++)
        {
            if (_cells[i].Rect.Contains(point)) return i;
        }
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var index = HitTest(e.GetPosition(this));
        if (index == _hover) return;

        _hover = index;
        if (index >= 0)
        {
            var node = _cells[index].Node;
            SetCurrentValue(SelectedNodeProperty, node);
            ToolTip = node.Name + "\n" + node.Path + "\n" + FormatBytes(node.SizeBytes);
            Cursor = node.IsDirectory ? Cursors.Hand : Cursors.Arrow;
        }
        else
        {
            ToolTip = null;
            Cursor = Cursors.Arrow;
        }

        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        ToolTip = null;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        var index = HitTest(e.GetPosition(this));
        if (index < 0) return;

        var node = _cells[index].Node;
        SetCurrentValue(SelectedNodeProperty, node);
        Focus();
        if (node.IsDirectory && ActivateCommand?.CanExecute(node) == true)
            ActivateCommand.Execute(node);
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        var index = HitTest(e.GetPosition(this));
        if (index < 0) return;
        SetCurrentValue(SelectedNodeProperty, _cells[index].Node);
        Focus();
        OpenContextMenu(_cells[index].Node);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if ((e.Key == Key.Apps || e.Key == Key.F10 && Keyboard.Modifiers == ModifierKeys.Shift) && SelectedNode != null)
        { OpenContextMenu(SelectedNode); e.Handled = true; }
    }

    private void OpenContextMenu(DiskTreeNode node)
    {
        var menu = new ContextMenu { PlacementTarget = this };
        menu.Items.Add(new MenuItem { Header = node.Name, IsEnabled = false });
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = Loc.T("disk.map.open.location"), Command = OpenLocationCommand,
            CommandParameter = node, IsEnabled = !string.IsNullOrWhiteSpace(node.Path) });
        ContextMenu = menu;
        menu.IsOpen = true;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        var i = 0;
        while (value >= 1024 && i < units.Length - 1)
        {
            value /= 1024;
            i++;
        }
        return value.ToString("0.##", CultureInfo.CurrentCulture) + " " + units[i];
    }
}
