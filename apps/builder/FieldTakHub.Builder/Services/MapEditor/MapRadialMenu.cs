using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace FieldTakHub.Builder.Services.MapEditor;

/// <summary>Pozycja pierścienia: opis, podpowiedź, ikona (tworzona przy otwarciu) i akcja.</summary>
internal sealed record RadialMenuEntry(string Label, string Tooltip, Func<FrameworkElement> CreateIcon, Action Invoke);

/// <summary>
/// Pierścień menu rysowany na nakładce nad mapą. Sam niczego nie wykonuje: nakładka podaje mu położenie myszy
/// (<see cref="IndexAt"/>, <see cref="SetHover"/>) i uruchamia wskazaną pozycję. Pozycja 0 jest u góry.
/// </summary>
internal sealed class MapRadialMenu
{
    public const double InnerRadius = 26;
    public const double OuterRadius = 86;
    private const double LabelGap = 8;
    private const double LabelReach = 112;

    /// <summary>Do tej odległości od środka kliknięcie wybiera pozycję (z opisem włącznie); dalej zamyka menu.</summary>
    public const double ClickReach = OuterRadius + LabelGap + LabelReach;

    private static readonly SolidColorBrush WedgeFill = Frozen(Color.FromArgb(236, 10, 24, 30));
    private static readonly SolidColorBrush WedgeHoverFill = Frozen(Color.FromArgb(245, 16, 72, 68));
    private static readonly SolidColorBrush LabelBackground = Frozen(Color.FromArgb(232, 8, 19, 22));

    private readonly IReadOnlyList<RadialMenuEntry> _entries;
    private readonly Path[] _wedges;
    private readonly Border[] _labels;
    private readonly TextBlock[] _labelTexts;

    public Canvas View { get; }
    public double Width { get; }
    public double Height { get; }
    public int HoverIndex { get; private set; } = -1;

    public MapRadialMenu(IReadOnlyList<RadialMenuEntry> entries)
    {
        _entries = entries;
        Width = 2 * (OuterRadius + LabelGap + LabelReach);
        Height = 2 * (OuterRadius + LabelGap + 28);
        View = new Canvas { Width = Width, Height = Height };

        int count = entries.Count;
        _wedges = new Path[count];
        _labels = new Border[count];
        _labelTexts = new TextBlock[count];
        var center = new Point(Width / 2.0, Height / 2.0);
        double half = 180.0 / Math.Max(1, count) - 1.5;

        for (int i = 0; i < count; i++)
        {
            double angle = RadialMenuGeometry.CenterAngleDeg(i, count);
            var wedge = new Path
            {
                Data = Wedge(center, InnerRadius, OuterRadius, angle - half, angle + half),
                Fill = WedgeFill,
                Stroke = TacticalConfigPanel.BrushTileBorder,
                StrokeThickness = 1,
                ToolTip = entries[i].Tooltip
            };
            _wedges[i] = wedge;
            View.Children.Add(wedge);

            var icon = entries[i].CreateIcon();
            icon.IsHitTestVisible = false;
            icon.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var (ux, uy) = RadialMenuGeometry.Direction(angle);
            double iconRadius = (InnerRadius + OuterRadius) / 2.0;
            Canvas.SetLeft(icon, center.X + ux * iconRadius - icon.DesiredSize.Width / 2.0);
            Canvas.SetTop(icon, center.Y + uy * iconRadius - icon.DesiredSize.Height / 2.0);
            View.Children.Add(icon);

            var text = new TextBlock { Text = entries[i].Label, Foreground = Brushes.White, FontSize = 11.5, FontWeight = FontWeights.SemiBold };
            var label = new Border
            {
                Background = LabelBackground,
                BorderBrush = TacticalConfigPanel.BrushTileBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Child = text,
                IsHitTestVisible = false
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = label.DesiredSize;
            double ax = center.X + ux * (OuterRadius + LabelGap), ay = center.Y + uy * (OuterRadius + LabelGap);
            switch (RadialMenuGeometry.LabelSide(angle))
            {
                case RadialLabelSide.Top: Canvas.SetLeft(label, ax - size.Width / 2.0); Canvas.SetTop(label, ay - size.Height); break;
                case RadialLabelSide.Bottom: Canvas.SetLeft(label, ax - size.Width / 2.0); Canvas.SetTop(label, ay); break;
                case RadialLabelSide.Right: Canvas.SetLeft(label, ax); Canvas.SetTop(label, ay - size.Height / 2.0); break;
                default: Canvas.SetLeft(label, ax - size.Width); Canvas.SetTop(label, ay - size.Height / 2.0); break;
            }
            _labels[i] = label;
            _labelTexts[i] = text;
            View.Children.Add(label);
        }

        var hub = new Ellipse
        {
            Width = 2 * (InnerRadius - 3),
            Height = 2 * (InnerRadius - 3),
            Fill = WedgeFill,
            Stroke = TacticalConfigPanel.BrushTileBorder,
            StrokeThickness = 1,
            ToolTip = "Zamknij (Esc)"
        };
        Canvas.SetLeft(hub, center.X - hub.Width / 2.0);
        Canvas.SetTop(hub, center.Y - hub.Height / 2.0);
        View.Children.Add(hub);
        var close = new TextBlock { Text = "✕", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 12, IsHitTestVisible = false };
        close.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(close, center.X - close.DesiredSize.Width / 2.0);
        Canvas.SetTop(close, center.Y - close.DesiredSize.Height / 2.0);
        View.Children.Add(close);
    }

    public RadialMenuEntry Entry(int index) => _entries[index];

    /// <summary>Pozycja pod punktem (współrzędne względem <see cref="View"/>) albo -1.</summary>
    public int IndexAt(Point pointInView, double maxRadius) =>
        RadialMenuGeometry.IndexAt(pointInView.X - Width / 2.0, pointInView.Y - Height / 2.0, _entries.Count, InnerRadius, maxRadius);

    public void SetHover(int index)
    {
        if (index == HoverIndex) return;
        if (HoverIndex >= 0 && HoverIndex < _wedges.Length) ApplyHover(HoverIndex, false);
        HoverIndex = index;
        if (index >= 0 && index < _wedges.Length) ApplyHover(index, true);
    }

    private void ApplyHover(int index, bool hover)
    {
        _wedges[index].Fill = hover ? WedgeHoverFill : WedgeFill;
        _wedges[index].Stroke = hover ? TacticalConfigPanel.BrushAccent : TacticalConfigPanel.BrushTileBorder;
        _labels[index].BorderBrush = hover ? TacticalConfigPanel.BrushAccent : TacticalConfigPanel.BrushTileBorder;
        _labelTexts[index].Foreground = hover ? TacticalConfigPanel.BrushAccent : Brushes.White;
    }

    private static Geometry Wedge(Point center, double innerRadius, double outerRadius, double fromDeg, double toDeg)
    {
        Point At(double radius, double angleDeg)
        {
            var (ux, uy) = RadialMenuGeometry.Direction(angleDeg);
            return new Point(center.X + ux * radius, center.Y + uy * radius);
        }

        var figure = new PathFigure { StartPoint = At(outerRadius, fromDeg), IsClosed = true, IsFilled = true };
        figure.Segments.Add(new ArcSegment(At(outerRadius, toDeg), new Size(outerRadius, outerRadius), 0, false, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(At(innerRadius, toDeg), true));
        figure.Segments.Add(new ArcSegment(At(innerRadius, fromDeg), new Size(innerRadius, innerRadius), 0, false, SweepDirection.Counterclockwise, true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
