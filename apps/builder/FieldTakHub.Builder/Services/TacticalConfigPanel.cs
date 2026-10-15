#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FieldTakHub.Builder.Services
{
    public class FactionDefinition
    {
        public string Name { get; set; } = "BLUFOR";
        public string ChannelName { get; set; } = "BLUFOR-TAK";
        public string Psk { get; set; } = "";
        public string ColorHex { get; set; } = "#2684FF";
        public string ColorLabel { get; set; } = "Niebieski";
    }

    public class TacticalConfigModel
    {
        public string LocationReportingStrategy { get; set; } = "Constant";
        public int ConstantReportingRateReliable { get; set; } = 0;
        public int ConstantReportingRateUnreliable { get; set; } = 1;
        public int DynamicReportingRateMinReliable { get; set; } = 20;
        public int DynamicReportingRateMaxReliable { get; set; } = 2;
        public int DynamicReportingRateStationaryReliable { get; set; } = 180;
        public int DynamicReportingRateStationaryUnreliable { get; set; } = 30;
        public int TcpConnectTimeout { get; set; } = 20;
        public int UdpNoDataTimeout { get; set; } = 30;
        public bool DispatchLocationCotExternal { get; set; } = true;
        public bool DispatchLocationCotExternalAtStart { get; set; } = true;
        public bool EnableNonStreamingConnections { get; set; } = true;
        public bool AutoDisableMeshSAWhenStreaming { get; set; } = false;

        public string LoraRegion { get; set; } = "EU_868";
        public string LoraModemPreset { get; set; } = "SHORT_FAST";
        public string DeviceRole { get; set; } = "Auto";
        public string HardwareModel { get; set; } = "generic_meshtastic";
        public string FirmwareMin { get; set; } = "2.7.26";
        public string FirmwareMax { get; set; } = "2.8.0.47";

        // 0 = Ważność bezterminowa
        public int PackageValidityHours { get; set; } = 0;
        public int QrValidityHours { get; set; } = 0;

        public string ActiveFaction { get; set; } = "ALL";
        public string DefaultChannelName { get; set; } = "BLUFOR-TAK";
        public string DefaultPsk { get; set; } = GenerateRandomBase64Psk();
        public string DefaultColorHex { get; set; } = "#2684FF";

        public static string GenerateRandomBase64Psk()
        {
            byte[] key = new byte[32];
            RandomNumberGenerator.Fill(key);
            return Convert.ToBase64String(key);
        }
    }

    public static class TacticalConfigPanel
    {
        public static TacticalConfigModel Current { get; set; } = new TacticalConfigModel();
        public static string LastKnownSourceDir { get; set; } = "";

        private static readonly SolidColorBrush BrushInputBg = CreateFrozenBrush(8, 19, 22);
        private static readonly SolidColorBrush BrushInputBorder = CreateFrozenBrush(30, 66, 74);
        private static readonly SolidColorBrush BrushItemBg = CreateFrozenBrush(15, 32, 37);
        private static readonly SolidColorBrush BrushRowBg = CreateFrozenBrush(11, 25, 29);
        private static readonly SolidColorBrush BrushCardBg = CreateFrozenBrush(9, 20, 24);
        private static readonly SolidColorBrush BrushCardBorder = CreateFrozenBrush(26, 58, 66);
        private static readonly SolidColorBrush BrushMutedText = CreateFrozenBrush(138, 168, 164);
        private static readonly SolidColorBrush BrushTileBorder = CreateFrozenBrush(28, 64, 72);
        private static readonly SolidColorBrush BrushAccent = CreateFrozenBrush(31, 224, 197);
        private static readonly SolidColorBrush BrushButtonAccent = CreateFrozenBrush(44, 229, 208);
        private static readonly SolidColorBrush BrushSwitchOff = CreateFrozenBrush(24, 44, 50);
        private static readonly SolidColorBrush BrushDarkFg = CreateFrozenBrush(6, 22, 24);
        private static readonly SolidColorBrush BrushDanger = CreateFrozenBrush(235, 64, 52);

        public static readonly (string Label, string Hex, SolidColorBrush Brush)[] TacticalPalette = new[]
        {
            ("Niebieski",     "#2684FF", CreateFrozenBrush(38, 132, 255)),
            ("Czerwony",      "#EB4034", CreateFrozenBrush(235, 64, 52)),
            ("Żółty",         "#F5B82E", CreateFrozenBrush(245, 184, 46)),
            ("Zielony",       "#2ECC71", CreateFrozenBrush(46, 204, 113)),
            ("Fioletowy",     "#9B59B6", CreateFrozenBrush(155, 89, 182)),
            ("Pomarańczowy",  "#E67E22", CreateFrozenBrush(230, 126, 34)),
            ("Turkusowy",     "#1FE0C5", CreateFrozenBrush(31, 224, 197)),
            ("Biały",         "#ECF0F1", CreateFrozenBrush(236, 240, 241))
        };

        private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        public static SolidColorBrush BrushFromHex(string hex)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(hex))
                {
                    var found = TacticalPalette.FirstOrDefault(p => p.Hex.Equals(hex, StringComparison.OrdinalIgnoreCase));
                    if (found.Brush != null) return found.Brush;
                    var c = (Color)ColorConverter.ConvertFromString(hex);
                    return CreateFrozenBrush(c.R, c.G, c.B);
                }
            }
            catch { }
            return BrushAccent;
        }

        private static readonly DependencyProperty IsHookedProperty =
            DependencyProperty.RegisterAttached("IsHooked", typeof(bool), typeof(TacticalConfigPanel), new PropertyMetadata(false));

        private static Style _darkComboBoxStyle = null;
        private static Style _darkComboBoxItemStyle = null;
        private static Button _injectedRefreshButton = null;
        private static TextBox _injectedPkgValidityBox = null;
        private static TextBox _injectedQrValidityBox = null;
        private static TextBlock _indefiniteStatusBadge = null;
        private static TextBlock _factionStatusLabel = null;
        private static bool _syncingValidity = false;

        private static readonly Regex WindowsPathRegex = new Regex(@"[A-Za-z]:[\\/][^\r\n""<>|?*]+", RegexOptions.Compiled);

        private class FactionRow
        {
            public TextBox NameBox;
            public TextBox ChannelBox;
            public TextBox PskBox;
            public string ColorHex = "#2684FF";
            public string ColorLabel = "Niebieski";
            public Border LeftAccentBar;
            public Border ColorSwatchDot;
            public TextBlock ColorNameText;
            public Border RowContainer;
        }

        private static readonly List<FactionRow> _factionRows = new List<FactionRow>();
        private static StackPanel _factionsContainer = null;

        public static List<FactionDefinition> GetConfiguredFactions()
        {
            var list = new List<FactionDefinition>();
            foreach (var r in _factionRows)
            {
                string n = r.NameBox?.Text?.Trim();
                if (!string.IsNullOrEmpty(n))
                {
                    list.Add(new FactionDefinition
                    {
                        Name = n,
                        ChannelName = string.IsNullOrWhiteSpace(r.ChannelBox?.Text) ? $"{n}-TAK" : r.ChannelBox.Text.Trim(),
                        Psk = string.IsNullOrWhiteSpace(r.PskBox?.Text) ? Current.DefaultPsk : r.PskBox.Text.Trim(),
                        ColorHex = r.ColorHex ?? "#2684FF",
                        ColorLabel = r.ColorLabel ?? "Niebieski"
                    });
                }
            }
            if (list.Count == 0)
            {
                list.Add(new FactionDefinition { Name = "BLUFOR", ChannelName = "BLUFOR-TAK", Psk = Current.DefaultPsk, ColorHex = "#2684FF", ColorLabel = "Niebieski" });
                list.Add(new FactionDefinition { Name = "OPFOR", ChannelName = "OPFOR-TAK", Psk = Current.DefaultPsk, ColorHex = "#EB4034", ColorLabel = "Czerwony" });
                list.Add(new FactionDefinition { Name = "ORGA", ChannelName = "ORGA-HQ", Psk = Current.DefaultPsk, ColorHex = "#F5B82E", ColorLabel = "Żółty" });
            }
            return list;
        }

        private const string DarkComboBoxXaml = @"
<Style xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
       xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
       TargetType=""ComboBox"">
    <Setter Property=""Foreground"" Value=""#FFFFFF""/>
    <Setter Property=""Background"" Value=""#081316""/>
    <Setter Property=""BorderBrush"" Value=""#1E424A""/>
    <Setter Property=""BorderThickness"" Value=""1""/>
    <Setter Property=""Padding"" Value=""8,0,8,0""/>
    <Setter Property=""ScrollViewer.HorizontalScrollBarVisibility"" Value=""Auto""/>
    <Setter Property=""ScrollViewer.VerticalScrollBarVisibility"" Value=""Auto""/>
    <Setter Property=""ScrollViewer.CanContentScroll"" Value=""True""/>
    <Setter Property=""Template"">
        <Setter.Value>
            <ControlTemplate TargetType=""ComboBox"">
                <Grid SnapsToDevicePixels=""True"">
                    <Border x:Name=""MainBorder""
                            Background=""{TemplateBinding Background}""
                            BorderBrush=""{TemplateBinding BorderBrush}""
                            BorderThickness=""{TemplateBinding BorderThickness}""
                            CornerRadius=""4"" />
                    <ToggleButton x:Name=""ToggleButton""
                                  Focusable=""False""
                                  IsChecked=""{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}""
                                  ClickMode=""Press"">
                        <ToggleButton.Template>
                            <ControlTemplate TargetType=""ToggleButton"">
                                <Border Background=""Transparent"" BorderThickness=""0"">
                                    <Grid>
                                        <Grid.ColumnDefinitions>
                                            <ColumnDefinition Width=""*""/>
                                            <ColumnDefinition Width=""28""/>
                                        </Grid.ColumnDefinitions>
                                        <Path Grid.Column=""1""
                                              HorizontalAlignment=""Center""
                                              VerticalAlignment=""Center""
                                              Data=""M 0 0 L 4.5 4.5 L 9 0""
                                              Stroke=""#FFFFFF""
                                              StrokeThickness=""1.6"" />
                                    </Grid>
                                </Border>
                            </ControlTemplate>
                        </ToggleButton.Template>
                    </ToggleButton>
                    <ContentPresenter x:Name=""ContentSite""
                                      IsHitTestVisible=""False""
                                      Content=""{TemplateBinding SelectionBoxItem}""
                                      ContentTemplate=""{TemplateBinding SelectionBoxItemTemplate}""
                                      ContentTemplateSelector=""{TemplateBinding ItemTemplateSelector}""
                                      Margin=""10,0,28,0""
                                      VerticalAlignment=""Center""
                                      HorizontalAlignment=""Left""
                                      TextElement.Foreground=""#FFFFFF"" />
                    <TextBlock x:Name=""FallbackText""
                               IsHitTestVisible=""False""
                               Text=""{Binding Text, RelativeSource={RelativeSource TemplatedParent}}""
                               Foreground=""#FFFFFF""
                               Margin=""10,0,28,0""
                               VerticalAlignment=""Center""
                               HorizontalAlignment=""Left""
                               Visibility=""Collapsed"" />
                    <Popup x:Name=""Popup""
                           Placement=""Bottom""
                           IsOpen=""{TemplateBinding IsDropDownOpen}""
                           AllowsTransparency=""True""
                           Focusable=""False""
                           PopupAnimation=""Slide"">
                        <Grid Name=""DropDown""
                              SnapsToDevicePixels=""True""
                              MinWidth=""{TemplateBinding ActualWidth}""
                              MaxHeight=""{TemplateBinding MaxDropDownHeight}"">
                            <Border x:Name=""DropDownBorder""
                                    Background=""#0F2025""
                                    BorderBrush=""#1FE0C5""
                                    BorderThickness=""1""
                                    CornerRadius=""4""
                                    Margin=""0,2,0,0"">
                                <ScrollViewer Margin=""2"" SnapsToDevicePixels=""True"">
                                    <StackPanel IsItemsHost=""True"" KeyboardNavigation.DirectionalNavigation=""Contained"" />
                                </ScrollViewer>
                            </Border>
                        </Grid>
                    </Popup>
                </Grid>
                <ControlTemplate.Triggers>
                    <Trigger Property=""IsMouseOver"" Value=""True"">
                        <Setter TargetName=""MainBorder"" Property=""BorderBrush"" Value=""#1FE0C5""/>
                    </Trigger>
                    <Trigger Property=""IsDropDownOpen"" Value=""True"">
                        <Setter TargetName=""MainBorder"" Property=""BorderBrush"" Value=""#1FE0C5""/>
                    </Trigger>
                    <Trigger Property=""SelectionBoxItem"" Value=""{x:Null}"">
                        <Setter TargetName=""FallbackText"" Property=""Visibility"" Value=""Visible""/>
                    </Trigger>
                    <Trigger Property=""SelectionBoxItem"" Value="""">
                        <Setter TargetName=""FallbackText"" Property=""Visibility"" Value=""Visible""/>
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>";

        public static Style GetDarkComboBoxStyle()
        {
            if (_darkComboBoxStyle == null)
            {
                try
                {
                    _darkComboBoxStyle = (Style)XamlReader.Parse(DarkComboBoxXaml);
                    _darkComboBoxStyle.Seal();
                }
                catch { }
            }
            return _darkComboBoxStyle;
        }

        public static Style GetDarkComboBoxItemStyle()
        {
            if (_darkComboBoxItemStyle == null)
            {
                var itemStyle = new Style(typeof(ComboBoxItem));
                itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, BrushItemBg));
                itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
                itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5)));
                itemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
                itemStyle.Seal();
                _darkComboBoxItemStyle = itemStyle;
            }
            return _darkComboBoxItemStyle;
        }

        public static void ApplyUnifiedComboBoxTheme(ComboBox cb, Window win)
        {
            if (cb == null) return;
            try
            {
                string preservedText = cb.Text;
                cb.IsEditable = false;
                cb.IsReadOnly = false;
                cb.Foreground = Brushes.White;
                cb.Background = BrushInputBg;
                cb.BorderBrush = BrushInputBorder;

                var style = GetDarkComboBoxStyle();
                if (style != null) cb.Style = style;
                cb.ItemContainerStyle = GetDarkComboBoxItemStyle();

                foreach (var item in cb.Items)
                {
                    if (item is ComboBoxItem cbi)
                    {
                        cbi.Foreground = Brushes.White;
                        cbi.Background = BrushItemBg;
                    }
                }

                if (cb.SelectedItem == null && !string.IsNullOrWhiteSpace(preservedText))
                {
                    foreach (var item in cb.Items)
                    {
                        string itemStr = (item is ComboBoxItem ci) ? ci.Content?.ToString() : item?.ToString();
                        if (string.Equals(itemStr, preservedText, StringComparison.OrdinalIgnoreCase))
                        {
                            cb.SelectedItem = item;
                            break;
                        }
                    }
                }
                if (cb.SelectedItem == null && cb.Items.Count > 0)
                {
                    cb.SelectedIndex = 0;
                }

                if (!(bool)cb.GetValue(IsHookedProperty))
                {
                    cb.SetValue(IsHookedProperty, true);
                    if (win != null)
                    {
                        cb.SelectionChanged += (s, e) => SyncNativeMeshtasticSelections(win);
                    }
                }
            }
            catch { }
        }

        public static void FindVisualChildren<T>(DependencyObject depObj, List<T> list) where T : DependencyObject
        {
            if (depObj == null) return;
            int count = VisualTreeHelper.GetChildrenCount(depObj);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                if (child is T t) list.Add(t);
                FindVisualChildren(child, list);
            }
        }

        private static Border GetOuterCardBorder(DependencyObject startElement)
        {
            Border bestBorder = null;
            DependencyObject cur = startElement;
            while (cur != null)
            {
                if (cur is Border b)
                {
                    var innerTbs = new List<TextBlock>();
                    FindVisualChildren(b, innerTbs);
                    if (innerTbs.Count >= 6) return b;
                    if (bestBorder == null && b.Background != null) bestBorder = b;
                }
                cur = VisualTreeHelper.GetParent(cur) ?? LogicalTreeHelper.GetParent(cur);
            }
            return bestBorder;
        }

        private static bool HasInjectedTag(Panel panel, string tag)
        {
            foreach (UIElement child in panel.Children)
            {
                if (child is FrameworkElement fe && string.Equals(fe.Tag?.ToString(), tag, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        public static void AttachToMainWindow(Window win)
        {
            if (Application.Current == null || win == null) return;

            void InitializeEnhancements()
            {
                try
                {
                    var combos = new List<ComboBox>();
                    FindVisualChildren(win, combos);
                    foreach (var c in combos)
                    {
                        ApplyUnifiedComboBoxTheme(c, win);
                    }

                    var tbs = new List<TextBlock>();
                    FindVisualChildren(win, tbs);

                    foreach (var tb in tbs)
                    {
                        if (tb.Text != null && tb.Text.Contains("Konfiguracja Meshtastic"))
                        {
                            Border cardBorder = GetOuterCardBorder(tb);
                            if (cardBorder != null)
                            {
                                if (cardBorder.Child is Panel innerPanel && !HasInjectedTag(innerPanel, "INJECTED_FW"))
                                {
                                    InjectMeshtasticFields(innerPanel, win);
                                }

                                if (VisualTreeHelper.GetParent(cardBorder) is Panel parentPanel && !HasInjectedTag(parentPanel, "INJECTED_ATAK"))
                                {
                                    int idx = parentPanel.Children.IndexOf(cardBorder);
                                    var atakCard = BuildAtakCard(win);
                                    var factionsCard = BuildDynamicFactionsCard(win);

                                    if (parentPanel is Grid pGrid && pGrid.ColumnDefinitions.Count > 1)
                                    {
                                        Grid.SetColumnSpan(factionsCard, pGrid.ColumnDefinitions.Count);
                                    }

                                    parentPanel.Children.Insert(idx, atakCard);
                                    parentPanel.Children.Insert(idx + 1, factionsCard);
                                }
                            }
                        }

                        if (tb.Text != null && (tb.Text.Contains("Podgląd zawartości") || tb.Text.Equals("Pliki paczki", StringComparison.OrdinalIgnoreCase)))
                        {
                            Border previewBorder = GetOuterCardBorder(tb);
                            if (previewBorder != null && VisualTreeHelper.GetParent(previewBorder) is Panel parentPanel && !HasInjectedTag(parentPanel, "INJECTED_BTN"))
                            {
                                var btnRefresh = new Button
                                {
                                    Content = "Odśwież / Skalkuluj zawartość (folder source)",
                                    Height = 32,
                                    Margin = new Thickness(0, 8, 0, 6),
                                    Background = BrushButtonAccent,
                                    Foreground = BrushDarkFg,
                                    FontWeight = FontWeights.Bold,
                                    Cursor = Cursors.Hand,
                                    BorderThickness = new Thickness(0),
                                    HorizontalAlignment = HorizontalAlignment.Stretch,
                                    Tag = "INJECTED_BTN"
                                };
                                _injectedRefreshButton = btnRefresh;

                                btnRefresh.Click += async (s, e) =>
                                {
                                    btnRefresh.Content = "Przeliczanie zawartości source...";
                                    btnRefresh.IsEnabled = false;
                                    var result = await ForceSizeUpdateAsync(win);
                                    btnRefresh.Content = $"Odśwież / Skalkuluj (Plików: {result.FileCount} | {FormatBytes(result.TotalBytes)})";
                                    btnRefresh.ToolTip = $"Aktywny folder source: {result.SourcePath}";
                                    btnRefresh.IsEnabled = true;
                                };

                                var btnOpenMapGen = new Button
                                {
                                    Content = "🗺️ Otwórz Generator Map Taktycznych (Pełny Ekran)",
                                    Height = 30,
                                    Margin = new Thickness(0, 0, 0, 6),
                                    Background = BrushItemBg,
                                    Foreground = BrushAccent,
                                    BorderBrush = BrushAccent,
                                    BorderThickness = new Thickness(1),
                                    FontWeight = FontWeights.Bold,
                                    Cursor = Cursors.Hand,
                                    HorizontalAlignment = HorizontalAlignment.Stretch,
                                    Tag = "INJECTED_MAP_QUICK"
                                };
                                btnOpenMapGen.Click += (s, e) => TacticalMapGeneratorView.OpenFullScreen(win);

                                var btnPosterQuick = new Button
                                {
                                    Content = "🖨️ Eksportuj plakat startowy (A4 PNG)",
                                    Height = 30,
                                    Margin = new Thickness(0, 0, 0, 14),
                                    Background = BrushItemBg,
                                    Foreground = Brushes.White,
                                    BorderBrush = BrushTileBorder,
                                    BorderThickness = new Thickness(1),
                                    FontWeight = FontWeights.SemiBold,
                                    Cursor = Cursors.Hand,
                                    HorizontalAlignment = HorizontalAlignment.Stretch,
                                    Tag = "INJECTED_POSTER_QUICK"
                                };
                                btnPosterQuick.Click += async (s, e) =>
                                {
                                    btnPosterQuick.IsEnabled = false;
                                    await ExportSafezonePosterForCurrentAsync(win);
                                    btnPosterQuick.IsEnabled = true;
                                };

                                int idx = parentPanel.Children.IndexOf(previewBorder);
                                parentPanel.Children.Insert(idx + 1, btnRefresh);
                                parentPanel.Children.Insert(idx + 2, btnOpenMapGen);
                                parentPanel.Children.Insert(idx + 3, btnPosterQuick);
                            }
                        }
                    }

                    HookNativeValidityControls(win);
                    SyncNativeMeshtasticSelections(win);

                    // Podpięcie pełnoekranowej zakładki Generatora Map Taktycznych
                    TacticalMapGeneratorView.AttachToMainWindow(win);
                }
                catch { }
            }

            InitializeEnhancements();

            if (!(bool)win.GetValue(IsHookedProperty))
            {
                win.SetValue(IsHookedProperty, true);

                win.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((s, e) =>
                {
                    ApplyIndefiniteValidityBeforeAction(win);
                }), true);

                win.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler((s, e) =>
                {
                    if (e.Key == Key.Enter || e.Key == Key.Space)
                    {
                        ApplyIndefiniteValidityBeforeAction(win);
                    }
                }), true);

                win.Loaded += async (s, e) =>
                {
                    InitializeEnhancements();
                    var res = await ForceSizeUpdateAsync(win);
                    if (_injectedRefreshButton != null)
                    {
                        _injectedRefreshButton.Content = $"Odśwież / Skalkuluj (Plików: {res.FileCount} | {FormatBytes(res.TotalBytes)})";
                        _injectedRefreshButton.ToolTip = $"Aktywny folder source: {res.SourcePath}";
                    }
                };
            }
        }

        // =========================================================================
        // PRZEBUDOWANY GENERATOR STRON KONFLIKTU (WYBÓR KOLORU, ZNAK "-", SZEROKI UKŁAD BEZ UCIĘĆ)
        // =========================================================================
        private static FrameworkElement BuildDynamicFactionsCard(Window win)
        {
            var card = new Border
            {
                Background = BrushCardBg,
                BorderBrush = BrushAccent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 14),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Tag = "INJECTED_FACTIONS"
            };

            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
            stack.Children.Add(new TextBlock
            {
                Text = "Generator Stron Konfliktu (Dynamiczny: 1, 2, 3, 4+ stron)",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            });
            stack.Children.Add(new TextBlock
            {
                Text = "Dodawaj i usuwaj dowolną liczbę stron konfliktu. Wybierz kolor taktyczny, nazwę kanału LoRa oraz pełny klucz szyfrujący AES-256 PSK. Każda strona otrzymuje osobny podfolder KML (source/factions/<strona>), paczkę .ftak i plakat QR.",
                Foreground = BrushMutedText,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            });

            _factionsContainer = new StackPanel { Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Stretch };
            _factionRows.Clear();
            stack.Children.Add(_factionsContainer);

            AddFactionRow("BLUFOR", "BLUFOR-TAK", TacticalConfigModel.GenerateRandomBase64Psk(), "#2684FF", "Niebieski");
            AddFactionRow("OPFOR", "OPFOR-TAK", TacticalConfigModel.GenerateRandomBase64Psk(), "#EB4034", "Czerwony");
            AddFactionRow("ORGA", "ORGA-HQ", TacticalConfigModel.GenerateRandomBase64Psk(), "#F5B82E", "Żółty");

            // Wiersz 1 przycisków pomocniczych (2 szerokie kolumny — nic się nie ucina)
            var rowButtons1 = new UniformGrid { Columns = 2, Margin = new Thickness(0, 2, 0, 6), HorizontalAlignment = HorizontalAlignment.Stretch };

            var btnAddRow = new Button
            {
                Content = "+ Dodaj stronę konfliktu",
                Height = 30,
                Margin = new Thickness(0, 0, 4, 0),
                Background = BrushItemBg,
                Foreground = BrushAccent,
                BorderBrush = BrushAccent,
                BorderThickness = new Thickness(1),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            btnAddRow.Click += (s, e) =>
            {
                int nextIdx = _factionRows.Count;
                var pal = TacticalPalette[nextIdx % TacticalPalette.Length];
                AddFactionRow($"STRONA-{nextIdx + 1}", $"TAK-CH{nextIdx + 1}", TacticalConfigModel.GenerateRandomBase64Psk(), pal.Hex, pal.Label);
            };

            var btnInitFolders = new Button
            {
                Content = "📁 Przygotuj podfoldery KML",
                Height = 30,
                Margin = new Thickness(4, 0, 0, 0),
                Background = BrushItemBg,
                Foreground = Brushes.White,
                BorderBrush = BrushTileBorder,
                BorderThickness = new Thickness(1),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                ToolTip = "Tworzy strukturę source/factions/<strona> z przykładowymi plikami KML"
            };

            rowButtons1.Children.Add(btnAddRow);
            rowButtons1.Children.Add(btnInitFolders);
            stack.Children.Add(rowButtons1);

            // Wiersz 2 przycisków pomocniczych
            var rowButtons2 = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Stretch };

            var btnRandomAllPsk = new Button
            {
                Content = "🎲 Losuj wszystkie klucze PSK",
                Height = 30,
                Margin = new Thickness(0, 0, 4, 0),
                Background = BrushItemBg,
                Foreground = Brushes.White,
                BorderBrush = BrushTileBorder,
                BorderThickness = new Thickness(1),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand
            };

            var btnExportPoster = new Button
            {
                Content = "🖨️ Eksportuj plakat A4 (PNG)",
                Height = 30,
                Margin = new Thickness(4, 0, 0, 0),
                Background = BrushItemBg,
                Foreground = Brushes.White,
                BorderBrush = BrushAccent,
                BorderThickness = new Thickness(1),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };

            rowButtons2.Children.Add(btnRandomAllPsk);
            rowButtons2.Children.Add(btnExportPoster);
            stack.Children.Add(rowButtons2);

            // Główny przycisk akcji na pełną szerokość karty
            var btnBuildAllFactions = new Button
            {
                Content = "⚡ Buduj wszystkie strony (.ftak + Plakaty QR)",
                Height = 34,
                Margin = new Thickness(0, 2, 0, 6),
                Background = BrushButtonAccent,
                Foreground = BrushDarkFg,
                BorderThickness = new Thickness(0),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            stack.Children.Add(btnBuildAllFactions);

            _factionStatusLabel = new TextBlock
            {
                Text = "Gotowy do wygenerowania odizolowanych paczek .ftak oraz plakatów startowych Safezone A4.",
                Foreground = BrushMutedText,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            };
            stack.Children.Add(_factionStatusLabel);

            btnRandomAllPsk.Click += (s, e) =>
            {
                foreach (var row in _factionRows)
                {
                    row.PskBox.Text = TacticalConfigModel.GenerateRandomBase64Psk();
                }
                SyncFirstFactionToDefaultModel();
                _factionStatusLabel.Text = $"✓ Wygenerowano nowe 256-bitowe klucze szyfrujące PSK dla {_factionRows.Count} stron.";
                _factionStatusLabel.Foreground = BrushAccent;
            };

            btnInitFolders.Click += async (s, e) =>
            {
                string srcDir = GetPrimarySourceDirectory(win);
                var sides = _factionRows
                    .Select(r => (Name: SanitizeFileName(r.NameBox.Text.Trim()).ToLowerInvariant(), ColorHex: r.ColorHex))
                    .Where(x => !string.IsNullOrEmpty(x.Name))
                    .ToList();
                await Task.Run(() => EnsureFactionFoldersInitialized(srcDir, sides));
                await ForceSizeUpdateAsync(win);
                _factionStatusLabel.Text = $"✓ Utworzono podfoldery KML dla {sides.Count} stron w: {Path.Combine(srcDir, "factions")}";
                _factionStatusLabel.Foreground = BrushAccent;
                TryOpenFolderInExplorer(Path.Combine(srcDir, "factions"));
            };

            btnExportPoster.Click += async (s, e) =>
            {
                btnExportPoster.IsEnabled = false;
                await ExportSafezonePosterForCurrentAsync(win);
                btnExportPoster.IsEnabled = true;
            };

            btnBuildAllFactions.Click += async (s, e) =>
            {
                btnBuildAllFactions.IsEnabled = false;
                string origText = btnBuildAllFactions.Content?.ToString();
                btnBuildAllFactions.Content = "Budowanie paczek i plakatów dla wszystkich stron...";
                await BuildDynamicFactionsAsync(win);
                btnBuildAllFactions.Content = origText;
                btnBuildAllFactions.IsEnabled = true;
            };

            card.Child = stack;
            return card;
        }

        private static void AddFactionRow(string name, string channel, string psk, string colorHex, string colorLabel)
        {
            if (_factionsContainer == null) return;

            var row = new FactionRow
            {
                ColorHex = colorHex,
                ColorLabel = colorLabel
            };

            var rowBorder = new Border
            {
                Background = BrushRowBg,
                BorderBrush = BrushTileBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 8),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            row.RowContainer = rowBorder;

            var outerGrid = new Grid();
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Lewy pasek akcentu w kolorze wybranej strony konfliktu
            var accentBar = new Border
            {
                Background = BrushFromHex(colorHex),
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 0, 8, 0)
            };
            row.LeftAccentBar = accentBar;
            Grid.SetColumn(accentBar, 0);
            outerGrid.Children.Add(accentBar);

            var contentStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
            Grid.SetColumn(contentStack, 1);
            outerGrid.Children.Add(contentStack);

            // POZIOM 1 WIERSZA: [Kolor Strony] | [Nazwa Strony] | [Kanał LoRa] | [Przycisk "-"]
            var topLine = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            topLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
            topLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
            topLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });

            // Selektor koloru (kliknięcie przełącza na kolejny kolor z palety taktycznej lub otwiera menu kontekstowe)
            var colorBtn = new Button
            {
                Height = 28,
                Background = BrushInputBg,
                BorderBrush = BrushInputBorder,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Padding = new Thickness(6, 0, 6, 0),
                Margin = new Thickness(0, 0, 6, 0),
                ToolTip = "Kliknij, aby zmienić kolor strony konfliktu"
            };
            var colorBtnPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var swatchDot = new Border
            {
                Width = 12,
                Height = 12,
                CornerRadius = new CornerRadius(6),
                Background = BrushFromHex(colorHex),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var colorText = new TextBlock
            {
                Text = colorLabel,
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            row.ColorSwatchDot = swatchDot;
            row.ColorNameText = colorText;
            colorBtnPanel.Children.Add(swatchDot);
            colorBtnPanel.Children.Add(colorText);
            colorBtn.Content = colorBtnPanel;

            // Menu wyboru konkretnego koloru + szybkie przełączanie kliknięciem
            var ctxMenu = new ContextMenu
            {
                Background = BrushItemBg,
                BorderBrush = BrushAccent,
                BorderThickness = new Thickness(1)
            };
            foreach (var palItem in TacticalPalette)
            {
                var mi = new MenuItem
                {
                    Header = $"{palItem.Label} ({palItem.Hex})",
                    Foreground = Brushes.White,
                    Background = BrushItemBg,
                    Icon = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = palItem.Brush }
                };
                string pHex = palItem.Hex;
                string pLab = palItem.Label;
                var pBrush = palItem.Brush;
                mi.Click += (s, e) =>
                {
                    row.ColorHex = pHex;
                    row.ColorLabel = pLab;
                    row.ColorSwatchDot.Background = pBrush;
                    row.LeftAccentBar.Background = pBrush;
                    row.ColorNameText.Text = pLab;
                    SyncFirstFactionToDefaultModel();
                };
                ctxMenu.Items.Add(mi);
            }
            colorBtn.ContextMenu = ctxMenu;
            colorBtn.Click += (s, e) =>
            {
                int curIdx = Array.FindIndex(TacticalPalette, p => p.Hex.Equals(row.ColorHex, StringComparison.OrdinalIgnoreCase));
                var next = TacticalPalette[(curIdx + 1) % TacticalPalette.Length];
                row.ColorHex = next.Hex;
                row.ColorLabel = next.Label;
                row.ColorSwatchDot.Background = next.Brush;
                row.LeftAccentBar.Background = next.Brush;
                row.ColorNameText.Text = next.Label;
                SyncFirstFactionToDefaultModel();
            };

            var colWrap = FieldBox("Kolor strony", colorBtn);
            colWrap.Margin = new Thickness(0);
            Grid.SetColumn(colWrap, 0);
            topLine.Children.Add(colWrap);

            row.NameBox = (TextBox)TextCtrl(name, _ => SyncFirstFactionToDefaultModel());
            row.NameBox.FontWeight = FontWeights.Bold;
            var nameWrap = FieldBox("Nazwa Strony", row.NameBox);
            nameWrap.Margin = new Thickness(0, 0, 6, 0);
            Grid.SetColumn(nameWrap, 1);
            topLine.Children.Add(nameWrap);

            row.ChannelBox = (TextBox)TextCtrl(channel, _ => SyncFirstFactionToDefaultModel());
            var chWrap = FieldBox("Kanał LoRa", row.ChannelBox);
            chWrap.Margin = new Thickness(0, 0, 6, 0);
            Grid.SetColumn(chWrap, 2);
            topLine.Children.Add(chWrap);

            // Spójny przycisk usuwania ze znakiem minus "−"
            var btnDel = new Button
            {
                Content = "−",
                Height = 28,
                Width = 30,
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = BrushItemBg,
                Foreground = Brushes.White,
                BorderBrush = BrushInputBorder,
                BorderThickness = new Thickness(1),
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                ToolTip = "Usuń tę stronę konfliktu"
            };
            btnDel.MouseEnter += (s, e) => { btnDel.BorderBrush = BrushDanger; btnDel.Foreground = BrushDanger; };
            btnDel.MouseLeave += (s, e) => { btnDel.BorderBrush = BrushInputBorder; btnDel.Foreground = Brushes.White; };
            btnDel.Click += (s, e) =>
            {
                if (_factionRows.Count <= 1)
                {
                    if (_factionStatusLabel != null)
                    {
                        _factionStatusLabel.Text = "⚠ Musi pozostać przynajmniej 1 strona konfliktu na liście.";
                        _factionStatusLabel.Foreground = BrushPaletteYellow();
                    }
                    return;
                }
                _factionsContainer.Children.Remove(rowBorder);
                _factionRows.Remove(row);
                SyncFirstFactionToDefaultModel();
            };
            Grid.SetColumn(btnDel, 3);
            topLine.Children.Add(btnDel);

            contentStack.Children.Add(topLine);

            // POZIOM 2 WIERSZA: Pełnej szerokości Klucz Szyfrujący PSK (AES-256 Base64) + Przycisk "Losuj PSK"
            var bottomLine = new Grid();
            bottomLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottomLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });

            row.PskBox = (TextBox)TextCtrl(psk, _ => SyncFirstFactionToDefaultModel());
            row.PskBox.FontFamily = new FontFamily("Consolas");
            row.PskBox.FontSize = 11;
            var pskWrap = FieldBox("Klucz szyfrujący PSK (AES-256 Base64)", row.PskBox);
            pskWrap.Margin = new Thickness(0, 0, 6, 0);
            Grid.SetColumn(pskWrap, 0);
            bottomLine.Children.Add(pskWrap);

            var btnRnd = new Button
            {
                Content = "🎲 Losuj",
                Height = 28,
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = BrushItemBg,
                Foreground = BrushAccent,
                BorderBrush = BrushTileBorder,
                BorderThickness = new Thickness(1),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                ToolTip = "Wygeneruj nowy klucz AES-256 dla tej strony"
            };
            btnRnd.Click += (s, e) =>
            {
                row.PskBox.Text = TacticalConfigModel.GenerateRandomBase64Psk();
                SyncFirstFactionToDefaultModel();
            };
            Grid.SetColumn(btnRnd, 1);
            bottomLine.Children.Add(btnRnd);

            contentStack.Children.Add(bottomLine);

            rowBorder.Child = outerGrid;
            _factionRows.Add(row);
            _factionsContainer.Children.Add(rowBorder);
            SyncFirstFactionToDefaultModel();
        }

        private static SolidColorBrush BrushPaletteYellow() => TacticalPalette[2].Brush;

        private static void SyncFirstFactionToDefaultModel()
        {
            var first = _factionRows.FirstOrDefault();
            if (first != null)
            {
                Current.ActiveFaction = string.IsNullOrWhiteSpace(first.NameBox?.Text) ? "ALL" : first.NameBox.Text.Trim();
                Current.DefaultChannelName = string.IsNullOrWhiteSpace(first.ChannelBox?.Text) ? "TAK-MESH" : first.ChannelBox.Text.Trim();
                Current.DefaultPsk = string.IsNullOrWhiteSpace(first.PskBox?.Text) ? Current.DefaultPsk : first.PskBox.Text.Trim();
                Current.DefaultColorHex = string.IsNullOrWhiteSpace(first.ColorHex) ? "#2684FF" : first.ColorHex;
            }
        }

        private static async Task BuildDynamicFactionsAsync(Window win)
        {
            try
            {
                SyncNativeMeshtasticSelections(win);
                string sourceDir = GetPrimarySourceDirectory(win);
                string eventName = GetEventNameFromWindow(win);
                string parentDir = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(sourceDir)) ?? AppDomain.CurrentDomain.BaseDirectory;
                string outDir = Path.Combine(parentDir, "release_factions");

                if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

                var sidesToBuild = new List<(string Name, string Channel, string Psk, string ColorHex)>();
                foreach (var row in _factionRows)
                {
                    string n = SanitizeFileName(row.NameBox.Text.Trim());
                    string c = string.IsNullOrWhiteSpace(row.ChannelBox.Text) ? $"{n}-TAK" : row.ChannelBox.Text.Trim();
                    string p = string.IsNullOrWhiteSpace(row.PskBox.Text) ? TacticalConfigModel.GenerateRandomBase64Psk() : row.PskBox.Text.Trim();
                    string col = string.IsNullOrWhiteSpace(row.ColorHex) ? "#2684FF" : row.ColorHex;
                    if (!string.IsNullOrEmpty(n)) sidesToBuild.Add((n, c, p, col));
                }

                if (sidesToBuild.Count == 0)
                {
                    if (_factionStatusLabel != null)
                    {
                        _factionStatusLabel.Text = "⚠ Brak zdefiniowanych stron do zbudowania.";
                        _factionStatusLabel.Foreground = BrushDanger;
                    }
                    return;
                }

                string prevFaction = Current.ActiveFaction;
                var allSidesMeta = sidesToBuild.Select(s => (Name: s.Name.ToLowerInvariant(), ColorHex: s.ColorHex)).ToList();

                foreach (var side in sidesToBuild)
                {
                    Current.ActiveFaction = side.Name;
                    string ftakPath = Path.Combine(outDir, $"{eventName}_{side.Name}.ftak");
                    string sha256 = await Task.Run(() =>
                    {
                        EnsureFactionFoldersInitialized(sourceDir, allSidesMeta);
                        WriteConfigsToDirectory(sourceDir, side.Name, side.Channel, side.Psk, side.ColorHex);
                        BuildFilteredFactionFtakArchive(sourceDir, ftakPath, side.Name, eventName, side.Channel, side.Psk, side.ColorHex);
                        return ComputeFileSha256(ftakPath);
                    });

                    string qrPayload = $"ftak://provision?event={Uri.EscapeDataString(eventName)}&faction={Uri.EscapeDataString(side.Name)}&color={Uri.EscapeDataString(side.ColorHex)}&channel={Uri.EscapeDataString(side.Channel)}&region={Current.LoraRegion}&preset={Current.LoraModemPreset}&sha256={sha256.Substring(0, 16)}";
                    string posterPath = Path.Combine(outDir, $"Plakat_Safezone_A4_{eventName}_{side.Name}.png");
                    RenderSafezoneA4PosterPng(win, posterPath, eventName, side.Name, side.Channel, qrPayload, sha256, side.ColorHex);
                }

                Current.ActiveFaction = prevFaction;
                SyncFirstFactionToDefaultModel();
                await Task.Run(() => WriteConfigsToDirectory(sourceDir));
                await ForceSizeUpdateAsync(win);

                if (_factionStatusLabel != null)
                {
                    _factionStatusLabel.Text = $"✓ Zbudowano {sidesToBuild.Count} paczek (.ftak) oraz {sidesToBuild.Count} plakatów A4 (PNG) w: {outDir}";
                    _factionStatusLabel.Foreground = BrushAccent;
                }

                TryOpenFolderInExplorer(outDir);
            }
            catch (Exception ex)
            {
                if (_factionStatusLabel != null)
                {
                    _factionStatusLabel.Text = $"Błąd generowania stron: {ex.Message}";
                    _factionStatusLabel.Foreground = BrushDanger;
                }
            }
        }

        private static async Task ExportSafezonePosterForCurrentAsync(Window win)
        {
            try
            {
                SyncNativeMeshtasticSelections(win);
                SyncFirstFactionToDefaultModel();
                string sourceDir = GetPrimarySourceDirectory(win);
                string eventName = GetEventNameFromWindow(win);
                string parentDir = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(sourceDir)) ?? AppDomain.CurrentDomain.BaseDirectory;
                string outDir = Path.Combine(parentDir, "release_factions");
                if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

                string faction = SanitizeFileName(string.IsNullOrWhiteSpace(Current.ActiveFaction) ? "BLUFOR" : Current.ActiveFaction);
                string channel = Current.DefaultChannelName;
                string psk = Current.DefaultPsk;
                string colorHex = Current.DefaultColorHex;

                string ftakPath = Path.Combine(outDir, $"{eventName}_{faction}.ftak");
                string sha256 = await Task.Run(() =>
                {
                    WriteConfigsToDirectory(sourceDir, faction, channel, psk, colorHex);
                    BuildFilteredFactionFtakArchive(sourceDir, ftakPath, faction, eventName, channel, psk, colorHex);
                    return ComputeFileSha256(ftakPath);
                });

                string qrPayload = ExtractExistingQrTextFromWindow(win)
                    ?? $"ftak://provision?event={Uri.EscapeDataString(eventName)}&faction={Uri.EscapeDataString(faction)}&channel={Uri.EscapeDataString(channel)}&region={Current.LoraRegion}&preset={Current.LoraModemPreset}&sha256={sha256.Substring(0, 16)}";

                string posterPath = Path.Combine(outDir, $"Plakat_Safezone_A4_{eventName}_{faction}.png");
                RenderSafezoneA4PosterPng(win, posterPath, eventName, faction, channel, qrPayload, sha256, colorHex);

                if (_factionStatusLabel != null)
                {
                    _factionStatusLabel.Text = $"✓ Wyeksportowano plakat startowy A4 PNG: {posterPath}";
                    _factionStatusLabel.Foreground = BrushAccent;
                }

                TryOpenFolderInExplorer(outDir);
            }
            catch { }
        }

        public static string GetPrimarySourceDirectory(Window win)
        {
            var dirs = ResolveSourceDirectories(win);
            return dirs.FirstOrDefault(Directory.Exists) ?? dirs.FirstOrDefault() ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "workspace", "source");
        }

        public static string GetEventNameFromWindow(Window win)
        {
            if (win == null) return "Operacja_TAK";
            try
            {
                var tbs = new List<TextBlock>();
                FindVisualChildren(win, tbs);
                foreach (var tb in tbs)
                {
                    if (tb.Text != null && (tb.Text.Contains("Nazwa wydarzenia") || tb.Text.Contains("Event Name") || tb.Text.Contains("Nazwa paczki") || tb.Text.Contains("Package ID")))
                    {
                        var parent = VisualTreeHelper.GetParent(tb);
                        if (parent != null)
                        {
                            var siblings = new List<TextBox>();
                            FindVisualChildren(parent, siblings);
                            var nonEmpty = siblings.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Text));
                            if (nonEmpty != null) return SanitizeFileName(nonEmpty.Text.Trim());
                        }
                    }
                }
            }
            catch { }
            return "Operacja_TAK";
        }

        public static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Operacja_TAK";
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }

        public static string HexToKmlColor(string hex, byte alpha = 220)
        {
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(hex ?? "#2684FF");
                return $"{alpha:x2}{c.B:x2}{c.G:x2}{c.R:x2}";
            }
            catch
            {
                return "dcff8426";
            }
        }

        private static void EnsureFactionFoldersInitialized(string sourceDir, List<(string Name, string ColorHex)> sides)
        {
            try
            {
                string factionsRoot = Path.Combine(sourceDir, "factions");
                foreach (var side in sides)
                {
                    if (string.IsNullOrWhiteSpace(side.Name)) continue;
                    string sideDir = Path.Combine(factionsRoot, side.Name);
                    if (!Directory.Exists(sideDir)) Directory.CreateDirectory(sideDir);

                    string sampleKml = Path.Combine(sideDir, $"strefa_{side.Name}.kml");
                    if (!File.Exists(sampleKml))
                    {
                        string kmlCol = HexToKmlColor(side.ColorHex, 255);
                        string kmlContent = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<kml xmlns=""http://www.opengis.net/kml/2.2"">
  <Document>
    <name>Warstwa Taktyczna {side.Name.ToUpperInvariant()}</name>
    <Style id=""factionStyle"">
      <IconStyle><color>{kmlCol}</color></IconStyle>
      <LineStyle><color>{kmlCol}</color><width>3</width></LineStyle>
    </Style>
    <Placemark>
      <name>HQ {side.Name.ToUpperInvariant()}</name>
      <description>Punkt dowodzenia strony {side.Name.ToUpperInvariant()} ({side.ColorHex})</description>
      <styleUrl>#factionStyle</styleUrl>
      <Point><coordinates>19.0238,49.9135,0</coordinates></Point>
    </Placemark>
  </Document>
</kml>";
                        File.WriteAllText(sampleKml, kmlContent, Encoding.UTF8);
                    }
                }
            }
            catch { }
        }

        private static void BuildFilteredFactionFtakArchive(string sourceDir, string destinationFtakPath, string factionCode, string eventName, string channelName, string psk, string colorHex)
        {
            if (File.Exists(destinationFtakPath)) File.Delete(destinationFtakPath);

            using var fs = new FileStream(destinationFtakPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Create, false, Encoding.UTF8);

            if (Directory.Exists(sourceDir))
            {
                var allFiles = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
                string targetFactionPrefix = $"factions/{factionCode.ToLowerInvariant()}/";

                foreach (var file in allFiles)
                {
                    string fileName = Path.GetFileName(file);
                    if (fileName.StartsWith(".", StringComparison.Ordinal) ||
                        fileName.Equals("WORKSPACE_HELP.txt", StringComparison.OrdinalIgnoreCase) ||
                        fileName.EndsWith(".fthproj", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string rel = Path.GetRelativePath(sourceDir, file).Replace('\\', '/');
                    string relLower = rel.ToLowerInvariant();

                    if (relLower.StartsWith("factions/") && !factionCode.Equals("ALL", StringComparison.OrdinalIgnoreCase) && !factionCode.Equals("ORGA", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!relLower.StartsWith(targetFactionPrefix))
                            continue;
                    }

                    var entry = archive.CreateEntry($"source/{rel}", CompressionLevel.Optimal);
                    using var entryStream = entry.Open();
                    using var fileStream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    fileStream.CopyTo(entryStream);
                }
            }

            var manifestObj = new JsonObject
            {
                ["schemaVersion"] = "2.4",
                ["packageId"] = $"{eventName.ToLowerInvariant()}-{factionCode.ToLowerInvariant()}",
                ["packageName"] = $"{eventName} ({factionCode})",
                ["faction"] = factionCode,
                ["factionColor"] = colorHex ?? "#2684FF",
                ["channelName"] = channelName,
                ["psk"] = psk,
                ["loraRegion"] = Current.LoraRegion,
                ["loraModemPreset"] = Current.LoraModemPreset,
                ["createdAt"] = DateTimeOffset.UtcNow.ToString("o"),
                ["neverExpires"] = (Current.PackageValidityHours == 0),
                ["expiresAt"] = (Current.PackageValidityHours == 0)
                    ? DateTimeOffset.UtcNow.AddYears(100).ToString("o")
                    : DateTimeOffset.UtcNow.AddHours(Current.PackageValidityHours).ToString("o")
            };

            var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
            using (var ms = new StreamWriter(manifestEntry.Open(), Encoding.UTF8))
            {
                ms.Write(manifestObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
        }

        private static string ComputeFileSha256(string filePath)
        {
            if (!File.Exists(filePath)) return "BRAK-PLIKU";
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            byte[] hash = sha.ComputeHash(stream);
            return Convert.ToHexString(hash);
        }

        private static string ExtractExistingQrTextFromWindow(Window win)
        {
            if (win == null) return null;
            try
            {
                var tbs = new List<TextBox>();
                FindVisualChildren(win, tbs);
                foreach (var tb in tbs)
                {
                    if (!string.IsNullOrWhiteSpace(tb.Text) && (tb.Text.StartsWith("http://") || tb.Text.StartsWith("https://") || tb.Text.StartsWith("ftak://")))
                    {
                        return tb.Text.Trim();
                    }
                }
            }
            catch { }
            return null;
        }

        private static ImageSource TryGetQrImageSource(Window win, string payload)
        {
            try
            {
                var qrType = typeof(TacticalConfigPanel).Assembly.GetTypes()
                    .FirstOrDefault(t => t.Name.Contains("QrService", StringComparison.OrdinalIgnoreCase));
                if (qrType != null)
                {
                    object instance = null;
                    if (!qrType.IsAbstract)
                    {
                        try { instance = Activator.CreateInstance(qrType); } catch { }
                    }
                    foreach (var m in qrType.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
                    {
                        var pars = m.GetParameters();
                        if (pars.Length == 1 && pars[0].ParameterType == typeof(string))
                        {
                            var res = m.Invoke(m.IsStatic ? null : instance, new object[] { payload });
                            if (res is ImageSource imgSrc) return imgSrc;
                            if (res is byte[] pngBytes && pngBytes.Length > 0)
                            {
                                var bmp = new BitmapImage();
                                using var mem = new MemoryStream(pngBytes);
                                bmp.BeginInit();
                                bmp.CacheOption = BitmapCacheOption.OnLoad;
                                bmp.StreamSource = mem;
                                bmp.EndInit();
                                bmp.Freeze();
                                return bmp;
                            }
                        }
                    }
                }
            }
            catch { }

            if (win != null)
            {
                try
                {
                    var imgs = new List<Image>();
                    FindVisualChildren(win, imgs);
                    foreach (var im in imgs)
                    {
                        if (im.Source != null && im.ActualWidth >= 90 && Math.Abs(im.ActualWidth - im.ActualHeight) < 20)
                        {
                            string n = (im.Name ?? "").ToLowerInvariant();
                            if (n.Contains("qr")) return im.Source;
                        }
                    }
                }
                catch { }
            }

            return null;
        }

        private static void RenderSafezoneA4PosterPng(Window win, string outputPath, string eventName, string factionCode, string channelName, string qrPayload, string sha256Hex, string colorHex)
        {
            const int width = 1240;
            const int height = 1754;

            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(BrushCardBg, null, new Rect(0, 0, width, height));
                dc.DrawRoundedRectangle(null, new Pen(BrushAccent, 4), new Rect(30, 30, width - 60, height - 60), 18, 18);

                Brush bannerBrush = BrushFromHex(colorHex);
                dc.DrawRoundedRectangle(bannerBrush, null, new Rect(70, 70, width - 140, 120), 12, 12);
                DrawCenteredText(dc, "FIELD TAK HUB 2.4 — KARTA WDROŻENIOWA SAFEZONE", 24, Brushes.White, width / 2.0, 92, true);
                DrawCenteredText(dc, $"OPERACJA: {eventName.ToUpperInvariant()}   |   STRONA: {factionCode.ToUpperInvariant()}", 34, Brushes.White, width / 2.0, 130, true);

                DrawCenteredText(dc, "SKANUJESZ  ➔  KONFIGURUJESZ  ➔  SPRAWDZASZ  ➔  JESTEŚ GOTOWY", 20, BrushAccent, width / 2.0, 220, true);

                double qrBoxSize = 620;
                double qrBoxX = (width - qrBoxSize) / 2.0;
                double qrBoxY = 275;
                dc.DrawRoundedRectangle(Brushes.White, new Pen(BrushAccent, 4), new Rect(qrBoxX, qrBoxY, qrBoxSize, qrBoxSize), 16, 16);

                ImageSource qrImg = TryGetQrImageSource(win, qrPayload);
                if (qrImg != null)
                {
                    dc.DrawImage(qrImg, new Rect(qrBoxX + 35, qrBoxY + 35, qrBoxSize - 70, qrBoxSize - 70));
                }
                else
                {
                    DrawDeterministicQrMatrix(dc, qrPayload, qrBoxX + 45, qrBoxY + 45, qrBoxSize - 90);
                }

                double stepsY = 935;
                dc.DrawRoundedRectangle(BrushItemBg, new Pen(BrushTileBorder, 2), new Rect(70, stepsY, width - 140, 340), 12, 12);
                DrawCenteredText(dc, "INSTRUKCJA SZYBKIEGO WDROŻENIA DLA UCZESTNIKA (3 KROKI)", 24, BrushAccent, width / 2.0, stepsY + 24, true);

                DrawLeftText(dc, "1. ZESKANUJ KOD QR:", 22, Brushes.White, 110, stepsY + 85, true);
                DrawLeftText(dc, "Otwórz aplikację Field TAK Hub i zeskanuj powyższy kod QR (lub użyj trybu P2P Hotspot od kolegi).", 19, BrushMutedText, 110, stepsY + 118, false);

                DrawLeftText(dc, "2. PODŁĄCZ RADIO MESHTASTIC (LORA):", 22, Brushes.White, 110, stepsY + 170, true);
                DrawLeftText(dc, "Włącz Bluetooth lub podepnij kabel USB. Aplikacja sama rozpozna model i wgra klucze strony.", 19, BrushMutedText, 110, stepsY + 203, false);

                DrawLeftText(dc, "3. ZIELONY EKRAN = GOTOWY DO GRY:", 22, Brushes.White, 110, stepsY + 255, true);
                DrawLeftText(dc, "Sprawdź status końcowy:  ✓ ATAK gotowy   ✓ Meshtastic gotowy   ✓ Serwer dostępny.", 19, BrushAccent, 110, stepsY + 288, true);

                double techY = 1310;
                dc.DrawRoundedRectangle(BrushInputBg, new Pen(BrushInputBorder, 2), new Rect(70, techY, width - 140, 360), 12, 12);
                DrawCenteredText(dc, "PARAMETRY TECHNICZNE PACZKI I RADIA LORA", 22, Brushes.White, width / 2.0, techY + 22, true);

                string validityText = Current.PackageValidityHours == 0 ? "BEZTERMINOWA (0)" : $"{Current.PackageValidityHours} godz.";
                DrawLeftText(dc, $"• Strona konfliktu:   {factionCode} ({colorHex})   |   Kanał LoRa: {channelName}", 20, Brushes.White, 110, techY + 75, true);
                DrawLeftText(dc, $"• Pasmo / Modem:      Region {Current.LoraRegion}   |   Preset {Current.LoraModemPreset}", 20, Brushes.White, 110, techY + 118, false);
                DrawLeftText(dc, $"• Sprzęt / Firmware:  Rola {Current.DeviceRole}   |   FW {Current.FirmwareMin} – {Current.FirmwareMax}", 20, Brushes.White, 110, techY + 161, false);
                DrawLeftText(dc, $"• Strategia PLI ATAK: {Current.LocationReportingStrategy}   |   Ważność paczki: {validityText}", 20, Brushes.White, 110, techY + 204, false);
                DrawLeftText(dc, $"• SHA-256 (.ftak):    {(sha256Hex.Length > 48 ? sha256Hex.Substring(0, 48) + "..." : sha256Hex)}", 17, BrushMutedText, 110, techY + 255, false);
                DrawLeftText(dc, $"• Wygenerowano:       {DateTime.Now:yyyy-MM-dd HH:mm}   |   Field TAK Hub Builder 2.4", 16, BrushMutedText, 110, techY + 295, false);
            }

            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var outStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            encoder.Save(outStream);
        }

        private static void DrawCenteredText(DrawingContext dc, string text, double fontSize, Brush brush, double centerX, double y, bool bold)
        {
            var ft = new FormattedText(text ?? "", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal), fontSize, brush, 1.0);
            dc.DrawText(ft, new Point(centerX - ft.Width / 2.0, y));
        }

        private static void DrawLeftText(DrawingContext dc, string text, double fontSize, Brush brush, double x, double y, bool bold)
        {
            var ft = new FormattedText(text ?? "", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal), fontSize, brush, 1.0);
            dc.DrawText(ft, new Point(x, y));
        }

        private static void DrawDeterministicQrMatrix(DrawingContext dc, string payload, double startX, double startY, double size)
        {
            const int modules = 29;
            double cell = size / modules;
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload ?? "FieldTakHub"));

            bool IsFinder(int r, int c) => (r < 7 && c < 7) || (r < 7 && c >= modules - 7) || (r >= modules - 7 && c < 7);

            void DrawFinder(int rOff, int cOff)
            {
                for (int r = 0; r < 7; r++)
                    for (int c = 0; c < 7; c++)
                        if ((r == 0 || r == 6 || c == 0 || c == 6) || (r >= 2 && r <= 4 && c >= 2 && c <= 4))
                            dc.DrawRectangle(Brushes.Black, null, new Rect(startX + (cOff + c) * cell, startY + (rOff + r) * cell, cell + 0.4, cell + 0.4));
            }

            DrawFinder(0, 0); DrawFinder(0, modules - 7); DrawFinder(modules - 7, 0);

            int bitIdx = 0;
            for (int r = 0; r < modules; r++)
                for (int c = 0; c < modules; c++)
                    if (!IsFinder(r, c) && r != 7 && c != 7)
                    {
                        if (((hash[(bitIdx / 8) % hash.Length] >> (bitIdx % 8)) & 1) == 1)
                            dc.DrawRectangle(Brushes.Black, null, new Rect(startX + c * cell, startY + r * cell, cell + 0.4, cell + 0.4));
                        bitIdx++;
                    }
        }

        public static void TryOpenFolderInExplorer(string folderPath)
        {
            try
            {
                if (Directory.Exists(folderPath))
                {
                    Process.Start(new ProcessStartInfo { FileName = folderPath, UseShellExecute = true });
                }
            }
            catch { }
        }

        // =========================================================================
        // WAŻNOŚĆ BEZTERMINOWA (0 = BEZTERMINOWO)
        // =========================================================================
        private static List<TextBox> FindNativeValidityTextBoxes(Window win)
        {
            var result = new List<TextBox>();
            if (win == null) return result;

            var allTbs = new List<TextBox>();
            FindVisualChildren(win, allTbs);

            foreach (var tb in allTbs)
            {
                if (tb.Tag?.ToString() == "INJECTED_VALIDITY") continue;

                string name = (tb.Name ?? "").ToLowerInvariant();
                string toolTip = (tb.ToolTip?.ToString() ?? "").ToLowerInvariant();
                string nearbyLabel = "";

                var parent = VisualTreeHelper.GetParent(tb);
                if (parent != null)
                {
                    var labelBlocks = new List<TextBlock>();
                    FindVisualChildren(parent, labelBlocks);
                    nearbyLabel = string.Join(" ", labelBlocks.Select(b => b.Text ?? "")).ToLowerInvariant();
                }

                bool isValidityField =
                    name.Contains("valid") || name.Contains("expir") || name.Contains("ttl") || name.Contains("waznosc") || name.Contains("lifetime") ||
                    toolTip.Contains("ważno") || toolTip.Contains("wazno") || toolTip.Contains("wygas") || toolTip.Contains("expir") || toolTip.Contains("valid") ||
                    nearbyLabel.Contains("ważno") || nearbyLabel.Contains("wazno") || nearbyLabel.Contains("wygas") || nearbyLabel.Contains("wygaś") ||
                    nearbyLabel.Contains("ttl") || nearbyLabel.Contains("czas życia") || nearbyLabel.Contains("validity") || nearbyLabel.Contains("expiration");

                if (isValidityField)
                {
                    result.Add(tb);
                }
            }
            return result;
        }

        private static void HookNativeValidityControls(Window win)
        {
            var nativeBoxes = FindNativeValidityTextBoxes(win);
            foreach (var tb in nativeBoxes)
            {
                if ((bool)tb.GetValue(IsHookedProperty)) continue;
                tb.SetValue(IsHookedProperty, true);

                tb.ToolTip = "Wpisz 0, aby ustawić ważność bezterminową (brak wygasania paczki .ftak oraz kodu QR).";

                tb.TextChanged += (s, e) =>
                {
                    if (_syncingValidity) return;
                    if (int.TryParse(tb.Text?.Trim(), out int v) && v >= 0)
                    {
                        _syncingValidity = true;
                        try
                        {
                            if (v == 0 || v < 800000)
                            {
                                Current.PackageValidityHours = v;
                                Current.QrValidityHours = v;
                                if (_injectedPkgValidityBox != null && _injectedPkgValidityBox.Text != v.ToString())
                                    _injectedPkgValidityBox.Text = v.ToString();
                                if (_injectedQrValidityBox != null && _injectedQrValidityBox.Text != v.ToString())
                                    _injectedQrValidityBox.Text = v.ToString();
                            }
                            tb.BorderBrush = (v == 0) ? BrushAccent : BrushInputBorder;
                            UpdateIndefiniteBadge();
                        }
                        finally
                        {
                            _syncingValidity = false;
                        }
                    }
                };
            }
        }

        private static void SyncValidityToNativeControls(Window win)
        {
            if (win == null || _syncingValidity) return;
            _syncingValidity = true;
            try
            {
                var nativeBoxes = FindNativeValidityTextBoxes(win);
                foreach (var tb in nativeBoxes)
                {
                    string name = (tb.Name ?? "").ToLowerInvariant();
                    int valToSet = name.Contains("qr") ? Current.QrValidityHours : Current.PackageValidityHours;
                    tb.Text = valToSet.ToString();
                    tb.BorderBrush = (valToSet == 0) ? BrushAccent : BrushInputBorder;
                }
                UpdateIndefiniteBadge();
            }
            catch { }
            finally
            {
                _syncingValidity = false;
            }
        }

        private static void UpdateIndefiniteBadge()
        {
            if (_indefiniteStatusBadge == null) return;
            if (Current.PackageValidityHours == 0 && Current.QrValidityHours == 0)
            {
                _indefiniteStatusBadge.Text = "✓ Ważność: BEZTERMINOWA (Paczka .ftak oraz kod QR nigdy nie wygasają)";
                _indefiniteStatusBadge.Foreground = BrushAccent;
            }
            else if (Current.PackageValidityHours == 0 || Current.QrValidityHours == 0)
            {
                string pkgStr = Current.PackageValidityHours == 0 ? "Bezterminowa" : $"{Current.PackageValidityHours}h";
                string qrStr = Current.QrValidityHours == 0 ? "Bezterminowy" : $"{Current.QrValidityHours}h";
                _indefiniteStatusBadge.Text = $"✓ Ważność paczki .ftak: {pkgStr} | Ważność kodu QR: {qrStr}";
                _indefiniteStatusBadge.Foreground = BrushAccent;
            }
            else
            {
                _indefiniteStatusBadge.Text = $"Czas wygaśnięcia — Paczka .ftak: {Current.PackageValidityHours}h | Kod QR: {Current.QrValidityHours}h (wpisz 0 dla bezterminowej)";
                _indefiniteStatusBadge.Foreground = BrushMutedText;
            }
        }

        private static void ApplyIndefiniteValidityBeforeAction(Window win)
        {
            if (win == null) return;
            try
            {
                var nativeBoxes = FindNativeValidityTextBoxes(win);
                var boxesRestoredToZero = new List<TextBox>();

                foreach (var tb in nativeBoxes)
                {
                    if (int.TryParse(tb.Text?.Trim(), out int v) && v == 0)
                    {
                        Current.PackageValidityHours = 0;
                        Current.QrValidityHours = 0;
                        boxesRestoredToZero.Add(tb);
                    }
                }

                bool isIndefinite = (Current.PackageValidityHours == 0 || Current.QrValidityHours == 0 || boxesRestoredToZero.Count > 0);
                if (!isIndefinite) return;

                _syncingValidity = true;
                try
                {
                    foreach (var tb in boxesRestoredToZero)
                    {
                        tb.Text = "876000";
                    }
                }
                finally
                {
                    _syncingValidity = false;
                }

                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var targets = new List<object> { win };
                if (win.DataContext != null) targets.Add(win.DataContext);

                foreach (var f in win.GetType().GetFields(flags))
                {
                    try
                    {
                        var fv = f.GetValue(win);
                        if (fv != null && fv.GetType().Namespace != null && fv.GetType().Namespace.StartsWith("FieldTakHub"))
                            targets.Add(fv);
                    }
                    catch { }
                }

                foreach (var obj in targets)
                {
                    foreach (var prop in obj.GetType().GetProperties(flags))
                    {
                        if (!prop.CanWrite || prop.GetIndexParameters().Length > 0) continue;
                        string pn = prop.Name.ToLowerInvariant();
                        if (pn.Contains("expir") || pn.Contains("valid") || pn.Contains("ttl"))
                        {
                            try
                            {
                                if (prop.PropertyType == typeof(int))
                                {
                                    var curVal = (int)prop.GetValue(obj);
                                    if (curVal <= 0) prop.SetValue(obj, pn.Contains("day") ? 36500 : 876000);
                                }
                                else if (prop.PropertyType == typeof(long))
                                {
                                    var curVal = (long)prop.GetValue(obj);
                                    if (curVal <= 0) prop.SetValue(obj, pn.Contains("day") ? 36500L : 876000L);
                                }
                                else if (prop.PropertyType == typeof(double))
                                {
                                    var curVal = (double)prop.GetValue(obj);
                                    if (curVal <= 0) prop.SetValue(obj, pn.Contains("day") ? 36500.0 : 876000.0);
                                }
                                else if (prop.PropertyType == typeof(DateTimeOffset) || prop.PropertyType == typeof(DateTimeOffset?))
                                {
                                    prop.SetValue(obj, DateTimeOffset.UtcNow.AddYears(100));
                                }
                            }
                            catch { }
                        }
                    }
                }

                _ = win.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _syncingValidity = true;
                    try
                    {
                        foreach (var tb in boxesRestoredToZero)
                        {
                            if (tb.Text == "876000") tb.Text = "0";
                            tb.BorderBrush = BrushAccent;
                        }
                        if (_injectedPkgValidityBox != null && Current.PackageValidityHours == 0)
                            _injectedPkgValidityBox.Text = "0";
                        if (_injectedQrValidityBox != null && Current.QrValidityHours == 0)
                            _injectedQrValidityBox.Text = "0";
                        UpdateIndefiniteBadge();
                    }
                    finally
                    {
                        _syncingValidity = false;
                    }
                }), DispatcherPriority.ApplicationIdle);
            }
            catch { }
        }

        // =========================================================================
        // KALKULATOR ROZMIARU FOLDERU SOURCE (PODGLĄD ZAWARTOŚCI)
        // =========================================================================
        public static async Task RefreshSourceSizeFromExternalAsync(Window win)
        {
            if (win == null) return;
            var res = await ForceSizeUpdateAsync(win);
            if (_injectedRefreshButton != null)
            {
                _injectedRefreshButton.Content = $"Odśwież / Skalkuluj (Plików: {res.FileCount} | {FormatBytes(res.TotalBytes)})";
                _injectedRefreshButton.ToolTip = $"Aktywny folder source: {res.SourcePath}";
            }
        }

        private static void SyncNativeMeshtasticSelections(Window win)
        {
            if (win == null) return;
            try
            {
                var combos = new List<ComboBox>();
                FindVisualChildren(win, combos);
                foreach (var cb in combos)
                {
                    if (cb.Tag?.ToString() == "INJECTED") continue;
                    string val = (cb.SelectedItem is ComboBoxItem cbi) ? cbi.Content?.ToString() : cb.SelectedItem?.ToString();
                    if (string.IsNullOrWhiteSpace(val)) val = cb.Text;
                    if (string.IsNullOrWhiteSpace(val)) continue;

                    string vLower = val.ToLowerInvariant();
                    if (vLower.Contains("tbeam") || vLower.Contains("heltec") || vLower.Contains("meshtracker") || vLower.Contains("rak") || vLower.Contains("generic") || vLower == "other")
                    {
                        Current.HardwareModel = val;
                    }
                    else if (vLower == "auto" || vLower.Contains("tak") || vLower.Contains("client") || vLower.Contains("router") || vLower.Contains("tracker"))
                    {
                        Current.DeviceRole = val;
                    }
                    else if (vLower.StartsWith("eu_") || vLower.StartsWith("us_") || vLower.StartsWith("anz") || vLower.StartsWith("ru_") || vLower.StartsWith("kr_"))
                    {
                        Current.LoraRegion = val;
                    }
                    else if (vLower.Contains("_fast") || vLower.Contains("_slow") || vLower.Contains("_moderate") || vLower.Contains("_turbo"))
                    {
                        Current.LoraModemPreset = val;
                    }
                }
            }
            catch { }
        }

        private static List<string> ResolveSourceDirectories(Window win)
        {
            var highPriorityDirs = new List<string>();
            var normalDirs = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddCandidateDir(string dir, bool highPriority)
            {
                if (string.IsNullOrWhiteSpace(dir)) return;
                try
                {
                    string normalized = Path.GetFullPath(Path.TrimEndingDirectorySeparator(dir.Trim()));
                    if (seen.Add(normalized))
                    {
                        if (highPriority) highPriorityDirs.Add(normalized);
                        else normalDirs.Add(normalized);
                    }
                }
                catch { }
            }

            void EvaluateRawString(string rawText, bool isHighPriorityHint)
            {
                if (string.IsNullOrWhiteSpace(rawText) || rawText.Length < 3) return;

                var candidates = new List<string> { rawText.Trim().Trim('"') };
                foreach (Match m in WindowsPathRegex.Matches(rawText))
                {
                    if (m.Success) candidates.Add(m.Value.Trim().Trim('"', ',', ';', ')'));
                }

                foreach (var candidate in candidates)
                {
                    try
                    {
                        if (!Path.IsPathRooted(candidate)) continue;
                        string clean = Path.TrimEndingDirectorySeparator(candidate);

                        if (File.Exists(clean))
                        {
                            clean = Path.GetDirectoryName(clean);