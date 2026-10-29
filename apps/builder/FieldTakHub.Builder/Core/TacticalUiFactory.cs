#nullable disable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FieldTakHub.Builder.Core
{
    /// <summary>
    /// Fabryka komponentów wizualnych w standardzie taktycznym C2 (Milsim Dark UI).
    /// Zapewnia spójną kolorystykę, czytelne obramowania i zero uciętych znaków w przyciskach.
    /// </summary>
    public static class TacticalUiFactory
    {
        public static readonly Color BgMainColor = Color.FromRgb(6, 13, 16);       // #060D10
        public static readonly Color BgSidebarColor = Color.FromRgb(10, 21, 27);   // #0A151B
        public static readonly Color BgCardColor = Color.FromRgb(13, 27, 35);      // #0D1B23
        public static readonly Color BgInputColor = Color.FromRgb(8, 18, 24);      // #081218
        public static readonly Color BorderCardColor = Color.FromRgb(27, 59, 75);  // #1B3B4B
        public static readonly Color AccentCyanColor = Color.FromRgb(31, 224, 197);// #1FE0C5
        public static readonly Color AccentGoldColor = Color.FromRgb(250, 208, 44);// #FAD02C
        public static readonly Color AccentGreenColor = Color.FromRgb(46, 204, 113);// #2ECC71
        public static readonly Color AccentRedColor = Color.FromRgb(235, 64, 52);  // #EB4034
        public static readonly Color TextPrimaryColor = Color.FromRgb(232, 241, 245);
        public static readonly Color TextMutedColor = Color.FromRgb(138, 168, 181);

        public static readonly SolidColorBrush BgMainBrush = CreateFrozenBrush(BgMainColor);
        public static readonly SolidColorBrush BgSidebarBrush = CreateFrozenBrush(BgSidebarColor);
        public static readonly SolidColorBrush BgCardBrush = CreateFrozenBrush(BgCardColor);
        public static readonly SolidColorBrush BgInputBrush = CreateFrozenBrush(BgInputColor);
        public static readonly SolidColorBrush BorderCardBrush = CreateFrozenBrush(BorderCardColor);
        public static readonly SolidColorBrush AccentCyanBrush = CreateFrozenBrush(AccentCyanColor);
        public static readonly SolidColorBrush AccentGoldBrush = CreateFrozenBrush(AccentGoldColor);
        public static readonly SolidColorBrush AccentGreenBrush = CreateFrozenBrush(AccentGreenColor);
        public static readonly SolidColorBrush AccentRedBrush = CreateFrozenBrush(AccentRedColor);
        public static readonly SolidColorBrush TextPrimaryBrush = CreateFrozenBrush(TextPrimaryColor);
        public static readonly SolidColorBrush TextMutedBrush = CreateFrozenBrush(TextMutedColor);

        private static SolidColorBrush CreateFrozenBrush(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public static Border CreateCardBorder(Brush customBorderBrush = null, Thickness? margin = null)
        {
            return new Border
            {
                Background = BgCardBrush,
                BorderBrush = customBorderBrush ?? BorderCardBrush,
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Margin = margin ?? new Thickness(0, 0, 0, 14)
            };
        }

        public static TextBlock CreateSectionHeader(string text, Brush foreground = null, double fontSize = 14)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = foreground ?? AccentCyanBrush,
                FontSize = fontSize,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap
            };
        }

        public static TextBlock CreateFieldLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = TextMutedBrush,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
        }

        public static TextBox CreateTextBox(string initialText = "", bool multiline = false, double height = 32, bool monoFont = false)
        {
            var tb = new TextBox
            {
                Text = initialText ?? "",
                Background = BgInputBrush,
                Foreground = TextPrimaryBrush,
                CaretBrush = AccentCyanBrush,
                BorderBrush = BorderCardBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 4, 8, 4),
                FontSize = 12.5,
                VerticalContentAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center
            };

            if (monoFont)
            {
                tb.FontFamily = new FontFamily("Consolas");
            }

            if (multiline)
            {
                tb.AcceptsReturn = true;
                tb.TextWrapping = TextWrapping.Wrap;
                tb.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                tb.MinHeight = height;
            }
            else
            {
                tb.Height = height;
            }

            return tb;
        }

        public static Button CreateActionButton(
            string text,
            Brush background,
            Brush foreground,
            Brush borderBrush = null,
            double height = 36,
            Thickness? margin = null)
        {
            return new Button
            {
                Content = text,
                Height = height,
                Padding = new Thickness(14, 6, 14, 6),
                Margin = margin ?? new Thickness(0, 4, 8, 4),
                Background = background,
                Foreground = foreground,
                BorderBrush = borderBrush ?? background,
                BorderThickness = new Thickness(1.5),
                FontWeight = FontWeights.Bold,
                FontSize = 12.5,
                Cursor = Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        /// <summary>
        /// Tworzy mały, kwadratowy przycisk akcji wiersza (np. usuwanie wiersza "−" lub dodawanie "+")
        /// z zerowym Paddingiem, dzięki czemu znak nigdy nie jest ucinany do dwukropka ":".
        /// </summary>
        public static Button CreateCompactIconBtn(string symbol, Brush bg, Brush border, double width = 32, double height = 30)
        {
            return new Button
            {
                Content = symbol,
                Width = width,
                Height = height,
                Padding = new Thickness(0),
                Margin = new Thickness(4, 0, 0, 0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = bg,
                Foreground = Brushes.White,
                BorderBrush = border,
                BorderThickness = new Thickness(1.2),
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
        }
    }
}
