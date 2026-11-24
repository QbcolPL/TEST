using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FieldTakHub.Builder.Services.MapEditor;

/// <summary>
/// Wspólne kontrolki list w generatorze map (obiekty na mapie, sektory ognia). Każda karta ma te same elementy
/// w tej samej kolejności: [przełącznik] [● kolor] [ikony…] [nazwa] [− usuń]. Kolor jest zawsze kółkiem na początku
/// wiersza, a usuwanie — zawsze czerwonym „−” na jego końcu, niezależnie od koloru obiektu.
/// </summary>
internal static class MapEditorUi
{
    public const double LabelColumnWidth = 74;
    public const double ValueColumnWidth = 78;
    public const double ControlHeight = 22;
    public const double SwatchSize = 20;

    private static readonly Color DefaultColor = Color.FromRgb(38, 132, 255);
    private static readonly SolidColorBrush SwatchBorder = Frozen(Color.FromArgb(210, 255, 255, 255));
    private static readonly SolidColorBrush SelectedCardBackground = Frozen(Color.FromRgb(14, 40, 48));
    private static readonly SolidColorBrush HoverBackground = Frozen(Color.FromRgb(18, 48, 58));
    private static readonly SolidColorBrush DeleteHoverBackground = Frozen(Color.FromRgb(248, 113, 113));
    private static readonly SolidColorBrush SecondaryText = Frozen(Color.FromRgb(215, 228, 232));
    private static readonly FontFamily MonoFont = new FontFamily("Consolas");

    // Jawne style przycisków. Generator map jest częścią głównego okna, a App.xaml nadaje każdemu przyciskowi
    // Padding 12,8, Margin 4 i MinHeight 34 — w 24-pikselowym przycisku usuwania nie mieścił się wtedy znak „−”,
    // a kółko koloru robiło się owalem. Własny szablon trzyma też kolor tła po najechaniu myszą.
    private static readonly Style RoundButtonStyle = CreateFlatStyle(new CornerRadius(50));
    private static readonly Style SquareButtonStyle = CreateFlatStyle(new CornerRadius(4));

    /// <summary>Karta jednego elementu listy. <paramref name="item"/> trafia do Tag — po nim odświeża się zaznaczenie.</summary>
    public static Border Card(object item, bool selected)
    {
        var card = new Border
        {
            Tag = item,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 6)
        };
        SetCardSelected(card, selected);
        return card;
    }

    public static void SetCardSelected(Border card, bool selected)
    {
        card.Background = selected ? SelectedCardBackground : TacticalConfigPanel.BrushItemBg;
        card.BorderBrush = selected ? TacticalConfigPanel.BrushAccent : TacticalConfigPanel.BrushTileBorder;
    }

    /// <summary>Wiersz nagłówka karty: [przełącznik] [● kolor] [ikony…] [treść] [− usuń].</summary>
    public static Grid HeaderRow(UIElement? toggle, UIElement swatch, IReadOnlyList<UIElement> icons, UIElement main, UIElement delete)
    {
        var row = new Grid { MinHeight = 26 };
        int column = 0;

        void AddAuto(UIElement element, double gapAfter)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (element is FrameworkElement fe)
            {
                fe.Margin = new Thickness(0, 0, gapAfter, 0);
                fe.VerticalAlignment = VerticalAlignment.Center;
            }
            Grid.SetColumn(element, column++);
            row.Children.Add(element);
        }

        if (toggle != null) AddAuto(toggle, 4);
        AddAuto(swatch, 6);
        foreach (var icon in icons) AddAuto(icon, 6);

        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (main is FrameworkElement mainElement)
        {
            mainElement.Margin = new Thickness(0, 0, 6, 0);
            mainElement.VerticalAlignment = VerticalAlignment.Center;
        }
        Grid.SetColumn(main, column++);
        row.Children.Add(main);

        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(delete, column);
        row.Children.Add(delete);
        return row;
    }

    /// <summary>Wiersz „Opis: [kontrolka] [wartość]” — kolumny opisu i wartości mają tę samą szerokość w każdej karcie.</summary>
    public static Grid FieldRow(string label, UIElement content, UIElement? value = null)
    {
        var row = new Grid { Margin = new Thickness(0, 5, 0, 0), MinHeight = ControlHeight };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelColumnWidth) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = value != null ? new GridLength(ValueColumnWidth) : new GridLength(0) });

        var caption = new TextBlock
        {
            Text = label,
            Foreground = TacticalConfigPanel.BrushMutedText,
            FontSize = 9.5,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(caption, 0);
        row.Children.Add(caption);

        Grid.SetColumn(content, 1);
        row.Children.Add(content);

        if (value != null)
        {
            Grid.SetColumn(value, 2);
            row.Children.Add(value);
        }
        return row;
    }

    public static TextBlock ValueText(string text) => new TextBlock
    {
        Text = text,
        Foreground = TacticalConfigPanel.BrushAccent,
        FontFamily = MonoFont,
        FontSize = 10,
        FontWeight = FontWeights.Bold,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis
    };

    /// <summary>Kółko w kolorze elementu. Kliknięcie przekazuje przycisk do <paramref name="onPick"/>, który otwiera paletę.</summary>
    public static Button ColorSwatch(string? hex, string tooltip, Action<Button> onPick, double size = SwatchSize)
    {
        var swatch = new Button
        {
            Style = RoundButtonStyle,
            Width = size,
            Height = size,
            Background = TacticalConfigPanel.BrushFromHex(hex, DefaultColor),
            BorderBrush = SwatchBorder,
            BorderThickness = new Thickness(1.5),
            Cursor = Cursors.Hand,
            ToolTip = tooltip,
            VerticalAlignment = VerticalAlignment.Center
        };
        AddHoverBorder(swatch, TacticalConfigPanel.BrushAccent);
        swatch.Click += (_, _) => onPick(swatch);
        return swatch;
    }

    public static void SetSwatchColor(Button swatch, string? hex, string tooltip)
    {
        swatch.Background = TacticalConfigPanel.BrushFromHex(hex, DefaultColor);
        swatch.ToolTip = tooltip;
    }

    /// <summary>Czerwony przycisk „−”: zawsze ostatni w wierszu i zawsze usuwa element.</summary>
    public static Button DeleteButton(string tooltip, Action onDelete)
    {
        var button = new Button
        {
            Style = SquareButtonStyle,
            Width = 24,
            Height = ControlHeight,
            Content = new TextBlock
            {
                Text = "−",
                Foreground = Brushes.White,
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, -2, 0, 0)
            },
            Background = TacticalConfigPanel.BrushRedMinus,
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            ToolTip = tooltip,
            VerticalAlignment = VerticalAlignment.Center
        };
        AddHoverBackground(button, DeleteHoverBackground);
        button.Click += (_, _) => onDelete();
        return button;
    }

    /// <summary>Mały przycisk z ikoną (np. 📍 „pokaż na mapie”).</summary>
    public static Button IconButton(string glyph, string tooltip, Action onClick)
    {
        var button = new Button
        {
            Style = SquareButtonStyle,
            Width = 26,
            Height = ControlHeight,
            Content = glyph,
            FontSize = 11,
            Foreground = TacticalConfigPanel.BrushAccent,
            Background = TacticalConfigPanel.BrushInputBg,
            BorderBrush = TacticalConfigPanel.BrushAccent,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            ToolTip = tooltip,
            VerticalAlignment = VerticalAlignment.Center
        };
        AddHoverBackground(button, HoverBackground);
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>Przycisk tekstowy w wierszu karty (np. „✂️ Uprość”). Nieaktywny jest przygaszony, a podpowiedź mówi dlaczego.</summary>
    public static Button TextButton(string text, string tooltip, bool enabled, Action onClick)
    {
        var button = new Button
        {
            Style = SquareButtonStyle,
            Height = ControlHeight,
            Padding = new Thickness(8, 0, 8, 0),
            Content = text,
            FontSize = 9.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = TacticalConfigPanel.BrushAccent,
            Background = TacticalConfigPanel.BrushInputBg,
            BorderBrush = TacticalConfigPanel.BrushAccent,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            ToolTip = tooltip,
            IsEnabled = enabled,
            Opacity = enabled ? 1.0 : 0.45,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetShowOnDisabled(button, true);
        button.IsEnabledChanged += (_, e) => button.Opacity = (bool)e.NewValue ? 1.0 : 0.45;
        AddHoverBackground(button, HoverBackground);
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>Przełącznik na początku karty: pole wyboru z krótkim symbolem, np. „Aa” (etykieta) albo „👁” (widoczność).</summary>
    public static CheckBox Toggle(string glyph, bool isChecked, string tooltip, Action<bool> onChanged)
    {
        var toggle = new CheckBox
        {
            IsChecked = isChecked,
            Content = new TextBlock
            {
                Text = glyph,
                Foreground = TacticalConfigPanel.BrushMutedText,
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            },
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            ToolTip = tooltip
        };
        toggle.Checked += (_, _) => onChanged(true);
        toggle.Unchecked += (_, _) => onChanged(false);
        return toggle;
    }

    /// <summary>Pole tekstowe karty: wyróżnione (nazwa) albo zwykłe (opis w legendzie).</summary>
    public static TextBox TextField(string text, string tooltip, bool emphasized) => new TextBox
    {
        Text = text,
        Height = emphasized ? 24 : ControlHeight,
        MinHeight = 0,
        Margin = new Thickness(0),
        FontSize = emphasized ? 11 : 10,
        FontWeight = emphasized ? FontWeights.Bold : FontWeights.Normal,
        Padding = new Thickness(6, 0, 6, 0),
        VerticalContentAlignment = VerticalAlignment.Center,
        Background = TacticalConfigPanel.BrushInputBg,
        Foreground = emphasized ? Brushes.White : SecondaryText,
        CaretBrush = TacticalConfigPanel.BrushAccent,
        BorderBrush = emphasized ? TacticalConfigPanel.BrushAccent : TacticalConfigPanel.BrushInputBorder,
        BorderThickness = new Thickness(1),
        ToolTip = tooltip
    };

    /// <summary>Suwak przeskakujący od razu w miejsce kliknięcia i podążający za przeciąganiem (jak pozostałe suwaki edytora).</summary>
    public static Slider ValueSlider(double min, double max, double value, double smallChange, Action<double> onChanged)
    {
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            IsMoveToPointEnabled = true,
            SmallChange = smallChange,
            LargeChange = smallChange * 4,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };

        void SetFromMouse(MouseEventArgs e)
        {
            if (slider.ActualWidth <= 8) return;
            slider.Value = min + Math.Clamp(e.GetPosition(slider).X / slider.ActualWidth, 0.0, 1.0) * (max - min);
        }

        slider.PreviewMouseLeftButtonDown += (_, e) => SetFromMouse(e);
        slider.PreviewMouseMove += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed) SetFromMouse(e); };
        slider.ValueChanged += (_, e) => onChanged(e.NewValue);
        return slider;
    }

    private static void AddHoverBorder(Control control, Brush hover)
    {
        Brush? normal = null;
        control.MouseEnter += (_, _) => { normal = control.BorderBrush; control.BorderBrush = hover; };
        control.MouseLeave += (_, _) => { if (normal != null) control.BorderBrush = normal; };
    }

    private static void AddHoverBackground(Control control, Brush hover)
    {
        Brush? normal = null;
        control.MouseEnter += (_, _) => { if (!control.IsEnabled) return; normal = control.Background; control.Background = hover; };
        control.MouseLeave += (_, _) => { if (normal != null) control.Background = normal; normal = null; };
    }

    private static Style CreateFlatStyle(CornerRadius radius)
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.TemplateProperty, CreateFlatTemplate(radius)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 0.0));
        style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 0.0));
        style.Seal();
        return style;
    }

    private static ControlTemplate CreateFlatTemplate(CornerRadius radius)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        border.SetValue(Border.CornerRadiusProperty, radius);
        border.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
        content.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        template.Seal();
        return template;
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
