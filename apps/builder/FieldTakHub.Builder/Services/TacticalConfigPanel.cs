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

        // Zamrożone pędzle (brak wycieków pamięci w WPF)
        private static readonly SolidColorBrush BrushInputBg = CreateFrozenBrush(8, 19, 22);
        private static readonly SolidColorBrush BrushInputBorder = CreateFrozenBrush(30, 66, 74);
        private static readonly SolidColorBrush BrushItemBg = CreateFrozenBrush(15, 32, 37);
        private static readonly SolidColorBrush BrushCardBg = CreateFrozenBrush(9, 20, 24);
        private static readonly SolidColorBrush BrushCardBorder = CreateFrozenBrush(26, 58, 66);
        private static readonly SolidColorBrush BrushMutedText = CreateFrozenBrush(138, 168, 164);
        private static readonly SolidColorBrush BrushTileBorder = CreateFrozenBrush(28, 64, 72);
        private static readonly SolidColorBrush BrushAccent = CreateFrozenBrush(31, 224, 197);
        private static readonly SolidColorBrush BrushButtonAccent = CreateFrozenBrush(44, 229, 208);
        private static readonly SolidColorBrush BrushSwitchOff = CreateFrozenBrush(24, 44, 50);
        private static readonly SolidColorBrush BrushDarkFg = CreateFrozenBrush(6, 22, 24);
        private static readonly SolidColorBrush BrushBlufor = CreateFrozenBrush(38, 132, 255);
        private static readonly SolidColorBrush BrushOpfor = CreateFrozenBrush(235, 64, 52);
        private static readonly SolidColorBrush BrushOrga = CreateFrozenBrush(245, 184, 46);
        private static readonly SolidColorBrush BrushDanger = CreateFrozenBrush(235, 64, 52);

        private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
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
            public Grid UI;
        }

        private static readonly List<FactionRow> _factionRows = new List<FactionRow>();
        private static StackPanel _factionsContainer = null;

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

        private static Style GetDarkComboBoxStyle()
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

        private static Style GetDarkComboBoxItemStyle()
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

        private static void ApplyUnifiedComboBoxTheme(ComboBox cb, Window win)
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
                    cb.SelectionChanged += (s, e) => SyncNativeMeshtasticSelections(win);
                }
            }
            catch { }
        }

        private static void FindVisualChildren<T>(DependencyObject depObj, List<T> list) where T : DependencyObject
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
                                    parentPanel.Children.Insert(idx, BuildAtakCard(win));
                                    parentPanel.Children.Insert(idx + 1, BuildDynamicFactionsCard(win));
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

                                var btnPosterQuick = new Button
                                {
                                    Content = "🖨️ Eksportuj plakat startowy (A4 PNG)",
                                    Height = 30,
                                    Margin = new Thickness(0, 0, 0, 14),
                                    Background = BrushItemBg,
                                    Foreground = Brushes.White,
                                    BorderBrush = BrushAccent,
                                    BorderThickness = new Thickness(1),
                                    FontWeight = FontWeights.SemiBold,
                                    Cursor = Cursors.Hand,
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
                                parentPanel.Children.Insert(idx + 2, btnPosterQuick);
                            }
                        }
                    }

                    HookNativeValidityControls(win);
                    SyncNativeMeshtasticSelections(win);
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
        // DYNAMICZNY GENERATOR STRON KONFLIKTU (DODAWANIE / USUWANIE STRON + PLAKAT A4)
        // =========================================================================
        private static UIElement BuildDynamicFactionsCard(Window win)
        {
            var card = new Border
            {
                Background = BrushCardBg,
                BorderBrush = BrushAccent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 14),
                Tag = "INJECTED_FACTIONS"
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = "Generator Stron Konfliktu (Dynamiczny: 1, 2, 3, 4+ stron) & Plakat Safezone A4",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 4)
            });
            stack.Children.Add(new TextBlock
            {
                Text = "Dodawaj i usuwaj dowolną liczbę stron konfliktu. Każda strona otrzymuje własny kanał LoRa, klucz AES-256 PSK, osobny podfolder KML (source/factions/<strona>) oraz własną paczkę .ftak i plakat QR.",
                Foreground = BrushMutedText,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            });

            var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(105) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });

            TextBlock Hdr(string t) => new TextBlock { Text = t, Foreground = BrushMutedText, FontSize = 10.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Bottom };

            var h1 = Hdr("Nazwa Strony"); Grid.SetColumn(h1, 0); headerGrid.Children.Add(h1);
            var h2 = Hdr("Kanał LoRa"); Grid.SetColumn(h2, 1); headerGrid.Children.Add(h2);
            var h3 = Hdr("Klucz Szyfrowania PSK (AES-256 Base64)"); Grid.SetColumn(h3, 2); headerGrid.Children.Add(h3);
            stack.Children.Add(headerGrid);

            _factionsContainer = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            _factionRows.Clear();
            stack.Children.Add(_factionsContainer);

            AddFactionRow("BLUFOR", "BLUFOR-TAK", TacticalConfigModel.GenerateRandomBase64Psk());
            AddFactionRow("OPFOR", "OPFOR-TAK", TacticalConfigModel.GenerateRandomBase64Psk());
            AddFactionRow("ORGA", "ORGA-HQ", TacticalConfigModel.GenerateRandomBase64Psk());

            var topBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };

            var btnAddRow = new Button
            {
                Content = "➕ Dodaj stronę konfliktu",
                Height = 28,
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(12, 0, 12, 0),
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
                int nextNum = _factionRows.Count + 1;
                AddFactionRow($"STRONA-{nextNum}", $"TAK-CH{nextNum}", TacticalConfigModel.GenerateRandomBase64Psk());
            };

            var btnInitFolders = new Button
            {
                Content = "📁 Przygotuj podfoldery KML (source/factions/...)",
                Height = 28,
                Padding = new Thickness(12, 0, 12, 0),
                Background = BrushItemBg,
                Foreground = Brushes.White,
                BorderBrush = BrushTileBorder,
                BorderThickness = new Thickness(1),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand
            };

            topBar.Children.Add(btnAddRow);
            topBar.Children.Add(btnInitFolders);
            stack.Children.Add(topBar);

            var actionButtonsGrid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 2, 0, 6) };

            var btnRandomAllPsk = new Button
            {
                Content = "🎲 Losuj wszystkie PSK",
                Height = 32,
                Margin = new Thickness(0, 0, 5, 0),
                Background = BrushItemBg,
                Foreground = Brushes.White,
                BorderBrush = BrushTileBorder,
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand
            };

            var btnExportPoster = new Button
            {
                Content = "🖨️ Eksportuj plakat A4 (PNG)",
                Height = 32,
                Margin = new Thickness(3, 0, 3, 0),
                Background = BrushItemBg,
                Foreground = Brushes.White,
                BorderBrush = BrushAccent,
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };

            var btnBuildAllFactions = new Button
            {
                Content = "⚡ Buduj wszystkie strony (.ftak + QR)",
                Height = 32,
                Margin = new Thickness(5, 0, 0, 0),
                Background = BrushButtonAccent,
                Foreground = BrushDarkFg,
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };

            actionButtonsGrid.Children.Add(btnRandomAllPsk);
            actionButtonsGrid.Children.Add(btnExportPoster);
            actionButtonsGrid.Children.Add(btnBuildAllFactions);
            stack.Children.Add(actionButtonsGrid);

            _factionStatusLabel = new TextBlock
            {
                Text = "Gotowy do wygenerowania odizolowanych paczek .ftak oraz plakatów startowych Safezone A4.",
                Foreground = BrushMutedText,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            };
            stack.Children.Add(_factionStatusLabel);

            btnRandomAllPsk.Click += (s, e) =>
            {
                foreach (var row in _factionRows)
                {
                    row.PskBox.Text = TacticalConfigModel.GenerateRandomBase64Psk();
                }
                SyncFirstFactionToDefaultModel();
                _factionStatusLabel.Text = $"✓ Wygenerowano nowe klucze AES-256 (Base64) dla {_factionRows.Count} stron.";
                _factionStatusLabel.Foreground = BrushAccent;
            };

            btnInitFolders.Click += async (s, e) =>
            {
                string srcDir = GetPrimarySourceDirectory(win);
                var sides = _factionRows
                    .Select(r => SanitizeFileName(r.NameBox.Text.Trim()).ToLowerInvariant())
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Distinct()
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
                btnBuildAllFactions.Content = "Budowanie stron...";
                await BuildDynamicFactionsAsync(win);
                btnBuildAllFactions.Content = origText;
                btnBuildAllFactions.IsEnabled = true;
            };

            card.Child = stack;
            return card;
        }

        private static void AddFactionRow(string name, string channel, string psk)
        {
            if (_factionsContainer == null) return;
            var row = new FactionRow();
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(105) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });

            row.NameBox = (TextBox)TextCtrl(name, _ => SyncFirstFactionToDefaultModel());
            row.NameBox.Margin = new Thickness(0, 0, 6, 0);
            row.NameBox.FontWeight = FontWeights.Bold;
            Grid.SetColumn(row.NameBox, 0);
            grid.Children.Add(row.NameBox);

            row.ChannelBox = (TextBox)TextCtrl(channel, _ => SyncFirstFactionToDefaultModel());
            row.ChannelBox.Margin = new Thickness(0, 0, 6, 0);
            Grid.SetColumn(row.ChannelBox, 1);
            grid.Children.Add(row.ChannelBox);

            row.PskBox = (TextBox)TextCtrl(psk, _ => SyncFirstFactionToDefaultModel());
            row.PskBox.Margin = new Thickness(0, 0, 6, 0);
            Grid.SetColumn(row.PskBox, 2);
            grid.Children.Add(row.PskBox);

            var btnRnd = new Button
            {
                Content = "🎲",
                Height = 28,
                Background = BrushItemBg,
                Foreground = Brushes.White,
                BorderBrush = BrushTileBorder,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 4, 0),
                ToolTip = "Losuj nowy klucz PSK dla tej strony"
            };
            btnRnd.Click += (s, e) =>
            {
                row.PskBox.Text = TacticalConfigModel.GenerateRandomBase64Psk();
                SyncFirstFactionToDefaultModel();
            };
            Grid.SetColumn(btnRnd, 3);
            grid.Children.Add(btnRnd);

            var btnDel = new Button
            {
                Content = "🗑️",
                Height = 28,
                Background = BrushDanger,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = "Usuń tę stronę konfliktu"
            };
            btnDel.Click += (s, e) =>
            {
                if (_factionRows.Count <= 1)
                {
                    if (_factionStatusLabel != null)
                    {
                        _factionStatusLabel.Text = "⚠ Musi pozostać przynajmniej 1 strona na liście.";
                        _factionStatusLabel.Foreground = BrushOrga;
                    }
                    return;
                }
                _factionsContainer.Children.Remove(grid);
                _factionRows.Remove(row);
                SyncFirstFactionToDefaultModel();
            };
            Grid.SetColumn(btnDel, 4);
            grid.Children.Add(btnDel);

            row.UI = grid;
            _factionRows.Add(row);
            _factionsContainer.Children.Add(grid);
            SyncFirstFactionToDefaultModel();
        }

        private static void SyncFirstFactionToDefaultModel()
        {
            var first = _factionRows.FirstOrDefault();
            if (first != null)
            {
                Current.ActiveFaction = string.IsNullOrWhiteSpace(first.NameBox?.Text) ? "ALL" : first.NameBox.Text.Trim();
                Current.DefaultChannelName = string.IsNullOrWhiteSpace(first.ChannelBox?.Text) ? "TAK-MESH" : first.ChannelBox.Text.Trim();
                Current.DefaultPsk = string.IsNullOrWhiteSpace(first.PskBox?.Text) ? Current.DefaultPsk : first.PskBox.Text.Trim();
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

                var sidesToBuild = new List<(string Name, string Channel, string Psk)>();
                foreach (var row in _factionRows)
                {
                    string n = SanitizeFileName(row.NameBox.Text.Trim());
                    string c = string.IsNullOrWhiteSpace(row.ChannelBox.Text) ? $"{n}-TAK" : row.ChannelBox.Text.Trim();
                    string p = string.IsNullOrWhiteSpace(row.PskBox.Text) ? TacticalConfigModel.GenerateRandomBase64Psk() : row.PskBox.Text.Trim();
                    if (!string.IsNullOrEmpty(n)) sidesToBuild.Add((n, c, p));
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
                var allSidesLower = sidesToBuild.Select(s => s.Name.ToLowerInvariant()).Distinct().ToList();

                foreach (var side in sidesToBuild)
                {
                    Current.ActiveFaction = side.Name;
                    string ftakPath = Path.Combine(outDir, $"{eventName}_{side.Name}.ftak");
                    string sha256 = await Task.Run(() =>
                    {
                        EnsureFactionFoldersInitialized(sourceDir, allSidesLower);
                        WriteConfigsToDirectory(sourceDir, side.Name, side.Channel, side.Psk);
                        BuildFilteredFactionFtakArchive(sourceDir, ftakPath, side.Name, eventName, side.Channel, side.Psk);
                        return ComputeFileSha256(ftakPath);
                    });

                    string qrPayload = $"ftak://provision?event={Uri.EscapeDataString(eventName)}&faction={Uri.EscapeDataString(side.Name)}&channel={Uri.EscapeDataString(side.Channel)}&region={Current.LoraRegion}&preset={Current.LoraModemPreset}&sha256={sha256.Substring(0, 16)}";
                    string posterPath = Path.Combine(outDir, $"Plakat_Safezone_A4_{eventName}_{side.Name}.png");
                    RenderSafezoneA4PosterPng(win, posterPath, eventName, side.Name, side.Channel, qrPayload, sha256);
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

                string ftakPath = Path.Combine(outDir, $"{eventName}_{faction}.ftak");
                string sha256 = await Task.Run(() =>
                {
                    WriteConfigsToDirectory(sourceDir, faction, channel, psk);
                    BuildFilteredFactionFtakArchive(sourceDir, ftakPath, faction, eventName, channel, psk);
                    return ComputeFileSha256(ftakPath);
                });

                string qrPayload = ExtractExistingQrTextFromWindow(win)
                    ?? $"ftak://provision?event={Uri.EscapeDataString(eventName)}&faction={Uri.EscapeDataString(faction)}&channel={Uri.EscapeDataString(channel)}&region={Current.LoraRegion}&preset={Current.LoraModemPreset}&sha256={sha256.Substring(0, 16)}";

                string posterPath = Path.Combine(outDir, $"Plakat_Safezone_A4_{eventName}_{faction}.png");
                RenderSafezoneA4PosterPng(win, posterPath, eventName, faction, channel, qrPayload, sha256);

                if (_factionStatusLabel != null)
                {
                    _factionStatusLabel.Text = $"✓ Wyeksportowano plakat startowy A4 PNG: {posterPath}";
                    _factionStatusLabel.Foreground = BrushAccent;
                }

                TryOpenFolderInExplorer(outDir);
            }
            catch { }
        }

        private static string GetPrimarySourceDirectory(Window win)
        {
            var dirs = ResolveSourceDirectories(win);
            return dirs.FirstOrDefault(Directory.Exists) ?? dirs.FirstOrDefault() ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "workspace", "source");
        }

        private static string GetEventNameFromWindow(Window win)
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

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Operacja_TAK";
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }

        private static void EnsureFactionFoldersInitialized(string sourceDir, List<string> sidesLower)
        {
            try
            {
                string factionsRoot = Path.Combine(sourceDir, "factions");
                foreach (var side in sidesLower)
                {
                    if (string.IsNullOrWhiteSpace(side)) continue;
                    string sideDir = Path.Combine(factionsRoot, side);
                    if (!Directory.Exists(sideDir)) Directory.CreateDirectory(sideDir);

                    string sampleKml = Path.Combine(sideDir, $"strefa_{side}.kml");
                    if (!File.Exists(sampleKml))
                    {
                        string kmlContent = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<kml xmlns=""http://www.opengis.net/kml/2.2"">
  <Document>
    <name>Warstwa Taktyczna {side.ToUpperInvariant()}</name>
    <Placemark>
      <name>HQ {side.ToUpperInvariant()}</name>
      <description>Punkt dowodzenia strony {side.ToUpperInvariant()}</description>
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

        private static void BuildFilteredFactionFtakArchive(string sourceDir, string destinationFtakPath, string factionCode, string eventName, string channelName, string psk)
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

                    // Odfiltrowanie nakładek KML należących do innych stron konfliktu
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

        private static void RenderSafezoneA4PosterPng(Window win, string outputPath, string eventName, string factionCode, string channelName, string qrPayload, string sha256Hex)
        {
            const int width = 1240;
            const int height = 1754;

            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(BrushCardBg, null, new Rect(0, 0, width, height));
                dc.DrawRoundedRectangle(null, new Pen(BrushAccent, 4), new Rect(30, 30, width - 60, height - 60), 18, 18);

                Brush bannerBrush = BrushAccent;
                if (factionCode.Equals("BLUFOR", StringComparison.OrdinalIgnoreCase)) bannerBrush = BrushBlufor;
                else if (factionCode.Equals("OPFOR", StringComparison.OrdinalIgnoreCase)) bannerBrush = BrushOpfor;
                else if (factionCode.Equals("ORGA", StringComparison.OrdinalIgnoreCase)) bannerBrush = BrushOrga;
                else
                {
                    int hsh = Math.Abs(factionCode.GetHashCode());
                    bannerBrush = CreateFrozenBrush((byte)(hsh % 160 + 60), (byte)((hsh / 255) % 160 + 60), (byte)((hsh / 65025) % 160 + 60));
                }

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
                DrawLeftText(dc, $"• Strona konfliktu:   {factionCode}   (Kanał LoRa: {channelName})", 20, Brushes.White, 110, techY + 75, true);
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

        private static void TryOpenFolderInExplorer(string folderPath)
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
                            if (string.IsNullOrEmpty(clean)) continue;
                            clean = Path.TrimEndingDirectorySeparator(clean);
                        }

                        if (Directory.Exists(clean))
                        {
                            string folderName = Path.GetFileName(clean);
                            if (string.Equals(folderName, "source", StringComparison.OrdinalIgnoreCase))
                            {
                                AddCandidateDir(clean, true);
                            }
                            else
                            {
                                string subSource = Path.Combine(clean, "source");
                                if (Directory.Exists(subSource))
                                {
                                    AddCandidateDir(subSource, isHighPriorityHint);
                                }
                                else if (isHighPriorityHint || File.Exists(Path.Combine(clean, "project.json")) || Directory.GetFiles(clean, "*.fthproj").Length > 0)
                                {
                                    AddCandidateDir(subSource, isHighPriorityHint);
                                }
                            }
                        }
                    }
                    catch { }
                }
            }

            if (!string.IsNullOrWhiteSpace(LastKnownSourceDir))
            {
                EvaluateRawString(LastKnownSourceDir, true);
            }

            if (win != null)
            {
                var visitedObjs = new HashSet<object>();
                void InspectObject(object obj, int depth)
                {
                    if (obj == null || depth > 4 || !visitedObjs.Add(obj)) return;
                    Type t = obj.GetType();
                    if (t.IsPrimitive || t.IsEnum || obj is Brush || obj is Style || obj is Geometry || obj is ImageSource) return;

                    if (obj is string s)
                    {
                        EvaluateRawString(s, false);
                        return;
                    }
                    if (obj is DirectoryInfo di)
                    {
                        EvaluateRawString(di.FullName, true);
                        return;
                    }
                    if (obj is FileInfo fi)
                    {
                        EvaluateRawString(fi.FullName, true);
                        return;
                    }

                    if (depth > 0 && t.Namespace != null && t.Namespace.StartsWith("System.Windows")) return;

                    var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                    foreach (var field in t.GetFields(flags))
                    {
                        try
                        {
                            var val = field.GetValue(obj);
                            bool hp = field.Name.Contains("source", StringComparison.OrdinalIgnoreCase) ||
                                      field.Name.Contains("workspace", StringComparison.OrdinalIgnoreCase) ||
                                      field.Name.Contains("project", StringComparison.OrdinalIgnoreCase);
                            if (val is string strVal) EvaluateRawString(strVal, hp);
                            else if (val != null && depth < 4) InspectObject(val, depth + 1);
                        }
                        catch { }
                    }

                    foreach (var prop in t.GetProperties(flags))
                    {
                        if (prop.GetIndexParameters().Length > 0) continue;
                        try
                        {
                            var val = prop.GetValue(obj);
                            bool hp = prop.Name.Contains("source", StringComparison.OrdinalIgnoreCase) ||
                                      prop.Name.Contains("workspace", StringComparison.OrdinalIgnoreCase) ||
                                      prop.Name.Contains("project", StringComparison.OrdinalIgnoreCase);
                            if (val is string strVal) EvaluateRawString(strVal, hp);
                            else if (val != null && depth < 4 && t.Namespace != null && t.Namespace.StartsWith("FieldTakHub"))
                                InspectObject(val, depth + 1);
                        }
                        catch { }
                    }

                    if (t.Namespace != null && t.Namespace.StartsWith("FieldTakHub"))
                    {
                        foreach (var m in t.GetMethods(flags))
                        {
                            if (m.GetParameters().Length == 0 && m.ReturnType == typeof(string))
                            {
                                string mn = m.Name.ToLowerInvariant();
                                if (mn.Contains("source") || mn.Contains("workspace") || mn.Contains("project") || mn.Contains("path") || mn.Contains("dir"))
                                {
                                    try
                                    {
                                        if (m.Invoke(m.IsStatic ? null : obj, null) is string retPath)
                                            EvaluateRawString(retPath, true);
                                    }
                                    catch { }
                                }
                            }
                        }
                    }
                }

                InspectObject(win, 0);
                if (win.DataContext != null) InspectObject(win.DataContext, 0);

                var textBoxes = new List<TextBox>();
                FindVisualChildren(win, textBoxes);
                foreach (var tbx in textBoxes) EvaluateRawString(tbx.Text, true);

                var textBlocks = new List<TextBlock>();
                FindVisualChildren(win, textBlocks);
                foreach (var tb in textBlocks) EvaluateRawString(tb.Text, false);
            }

            string localApp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FieldTakHub");
            if (Directory.Exists(localApp))
            {
                try
                {
                    foreach (var d in Directory.GetDirectories(localApp, "source", SearchOption.AllDirectories))
                        AddCandidateDir(d, false);
                }
                catch { }
            }

            var combined = new List<string>();
            combined.AddRange(highPriorityDirs);
            combined.AddRange(normalDirs);

            if (combined.Count == 0)
            {
                string defaultSource = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "workspace", "source");
                combined.Add(defaultSource);
            }

            return combined;
        }

        private class SourceStats
        {
            public string SourcePath = "";
            public int FileCount;
            public long TotalBytes;
            public long MapsBytes;
            public long PluginsBytes;
            public long ConfigBytes;
            public long TacticalBytes;
            public long OtherBytes;
        }

        private static async Task<SourceStats> ForceSizeUpdateAsync(Window win)
        {
            var emptyStats = new SourceStats();
            if (Application.Current == null || win == null) return emptyStats;

            try
            {
                SyncNativeMeshtasticSelections(win);
                SyncFirstFactionToDefaultModel();
                var sourceDirs = ResolveSourceDirectories(win);

                SourceStats bestStats = null;
                await Task.Run(() =>
                {
                    foreach (var sDir in sourceDirs)
                    {
                        try
                        {
                            if (!Directory.Exists(sDir)) Directory.CreateDirectory(sDir);
                            WriteConfigsToDirectory(sDir);
                            var st = CalculateSourceFolderStats(sDir);
                            if (bestStats == null || st.FileCount > bestStats.FileCount || (st.FileCount == bestStats.FileCount && st.TotalBytes > bestStats.TotalBytes))
                            {
                                bestStats = st;
                            }
                        }
                        catch { }
                    }
                });

                if (bestStats == null) bestStats = emptyStats;

                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                foreach (var m in win.GetType().GetMethods(flags))
                {
                    if (m.GetParameters().Length != 0) continue;
                    string n = m.Name.ToLowerInvariant();
                    if ((n.Contains("preview") || n.Contains("analyz") || n.Contains("refresh") || n.Contains("recalc") || n.Contains("updatesource") || n.Contains("updatecontent") || n.Contains("updatesummary"))
                        && !n.Contains("build") && !n.Contains("gener") && !n.Contains("export") && !n.Contains("compile")
                        && !n.Contains("valid") && !n.Contains("publish") && !n.Contains("deploy") && !n.Contains("dialog")
                        && !n.Contains("open") && !n.Contains("new") && !n.Contains("save") && !n.Contains("import") && !n.Contains("sign"))
                    {
                        try
                        {
                            var res = m.Invoke(win, null);
                            if (res is Task t) await t;
                        }
                        catch { }
                    }
                }

                UpdatePreviewCardLabels(win, bestStats);

                _ = win.Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdatePreviewCardLabels(win, bestStats);
                }), DispatcherPriority.ApplicationIdle);

                return bestStats;
            }
            catch
            {
                return emptyStats;
            }
        }

        private static SourceStats CalculateSourceFolderStats(string sourceDir)
        {
            var stats = new SourceStats { SourcePath = sourceDir };
            if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir)) return stats;

            try
            {
                var files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    string fileName = Path.GetFileName(file);
                    if (string.IsNullOrEmpty(fileName)) continue;

                    if (fileName.StartsWith(".", StringComparison.Ordinal) ||
                        fileName.StartsWith("README", StringComparison.OrdinalIgnoreCase) ||
                        fileName.EndsWith(".fthproj", StringComparison.OrdinalIgnoreCase) ||
                        fileName.Equals("WORKSPACE_HELP.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    long len = 0;
                    try { len = new FileInfo(file).Length; } catch { continue; }

                    string rel = Path.GetRelativePath(sourceDir, file).Replace('\\', '/').ToLowerInvariant();
                    string ext = Path.GetExtension(fileName).ToLowerInvariant();

                    stats.FileCount++;
                    stats.TotalBytes += len;

                    if (ext == ".apk" || rel.StartsWith("plugins/"))
                    {
                        stats.PluginsBytes += len;
                    }
                    else if (ext == ".mbtiles" || ext == ".sqlite" || ext == ".sqlitedb" || ext == ".tif" || ext == ".tiff" || ext == ".geotiff" || rel.StartsWith("maps/"))
                    {
                        stats.MapsBytes += len;
                    }
                    else if (ext == ".json" || ext == ".pref" || ext == ".cfg" || ext == ".conf" || ext == ".ini" || ext == ".p12" || ext == ".pem" || ext == ".crt" || fileName.Equals("server.txt", StringComparison.OrdinalIgnoreCase) || rel.StartsWith("config/") || rel.StartsWith("certs/"))
                    {
                        stats.ConfigBytes += len;
                    }
                    else if (ext == ".kml" || ext == ".kmz" || ext == ".gpx" || ext == ".cot" || ext == ".geojson" || ext == ".shp" || rel.StartsWith("atak/") || rel.StartsWith("overlays/") || rel.StartsWith("data/") || rel.StartsWith("factions/"))
                    {
                        stats.TacticalBytes += len;
                    }
                    else
                    {
                        stats.OtherBytes += len;
                    }
                }
            }
            catch { }
            return stats;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] units = { "B", "KB", "MB", "GB" };
            double val = bytes;
            int idx = 0;
            while (val >= 1024 && idx < units.Length - 1)
            {
                val /= 1024;
                idx++;
            }
            return idx == 0 ? $"{bytes} B" : $"{val:0.#} {units[idx]}";
        }

        private static void UpdatePreviewCardLabels(Window win, SourceStats stats)
        {
            if (win == null || stats == null) return;
            try
            {
                var allTbs = new List<TextBlock>();
                FindVisualChildren(win, allTbs);

                Border previewBorder = null;
                foreach (var tb in allTbs)
                {
                    if (tb.Text != null && (tb.Text.Equals("Pliki paczki", StringComparison.OrdinalIgnoreCase) || tb.Text.Contains("Podgląd zawartości")))
                    {
                        previewBorder = GetOuterCardBorder(tb);
                        if (previewBorder != null) break;
                    }
                }
                if (previewBorder == null) return;

                var cardTbs = new List<TextBlock>();
                FindVisualChildren(previewBorder, cardTbs);

                string[] rowLabels = { "Pliki paczki", "Mapy", "Pluginy", "Konfiguracja", "Dane taktyczne", "Pozostałe" };
                string[] rowValues = {
                    FormatBytes(stats.TotalBytes),
                    FormatBytes(stats.MapsBytes),
                    FormatBytes(stats.PluginsBytes),
                    FormatBytes(stats.ConfigBytes),
                    FormatBytes(stats.TacticalBytes),
                    FormatBytes(stats.OtherBytes)
                };

                bool IsKnownLabelOrHeader(string text)
                {
                    if (string.IsNullOrWhiteSpace(text)) return true;
                    string tr = text.Trim();
                    if (tr.Contains("Podgląd zawartości", StringComparison.OrdinalIgnoreCase)) return true;
                    return rowLabels.Any(l => tr.StartsWith(l, StringComparison.OrdinalIgnoreCase));
                }

                int updatedCount = 0;

                for (int r = 0; r < rowLabels.Length; r++)
                {
                    string labelPrefix = rowLabels[r];
                    string valText = rowValues[r];

                    TextBlock labelTb = cardTbs.FirstOrDefault(t => t.Text != null && t.Text.Trim().StartsWith(labelPrefix, StringComparison.OrdinalIgnoreCase));
                    if (labelTb == null) continue;

                    TextBlock targetValueTb = null;
                    var parent = VisualTreeHelper.GetParent(labelTb);

                    if (parent is Grid grid)
                    {
                        int targetRow = Grid.GetRow(labelTb);
                        int labelCol = Grid.GetColumn(labelTb);
                        foreach (UIElement child in grid.Children)
                        {
                            if (child is TextBlock sib && sib != labelTb && Grid.GetRow(sib) == targetRow && Grid.GetColumn(sib) >= labelCol && !IsKnownLabelOrHeader(sib.Text))
                            {
                                targetValueTb = sib;
                                break;
                            }
                        }
                    }

                    if (targetValueTb == null && parent != null)
                    {
                        var rowChildren = new List<TextBlock>();
                        FindVisualChildren(parent, rowChildren);
                        if (rowChildren.Count == 2)
                        {
                            targetValueTb = rowChildren.FirstOrDefault(x => x != labelTb);
                        }
                    }

                    if (targetValueTb == null)
                    {
                        try
                        {
                            Point labelPt = labelTb.TranslatePoint(new Point(0, 0), previewBorder);
                            double bestDist = double.MaxValue;
                            foreach (var cand in cardTbs)
                            {
                                if (cand == labelTb || IsKnownLabelOrHeader(cand.Text)) continue;
                                Point candPt = cand.TranslatePoint(new Point(0, 0), previewBorder);
                                double dy = Math.Abs(candPt.Y - labelPt.Y);
                                if (dy <= 14 && candPt.X > labelPt.X + 10 && dy < bestDist)
                                {
                                    bestDist = dy;
                                    targetValueTb = cand;
                                }
                            }
                        }
                        catch { }
                    }

                    if (targetValueTb != null)
                    {
                        targetValueTb.Text = valText;
                        updatedCount++;
                    }
                }

                if (updatedCount < 6)
                {
                    var valueTbs = cardTbs.Where(t => !IsKnownLabelOrHeader(t.Text) && t.Text != null && t.Text.Length <= 20).ToList();
                    if (valueTbs.Count == 6)
                    {
                        for (int i = 0; i < 6; i++)
                        {
                            valueTbs[i].Text = rowValues[i];
                        }
                    }
                }
            }
            catch { }
        }

        // =========================================================================
        // KARTA ATAK-CIV ORAZ KONTROLKI LO-RA / FIRMWARE
        // =========================================================================
        private static UIElement BuildAtakCard(Window win)
        {
            var m = Current;
            var card = new Border { Background = BrushCardBg, BorderBrush = BrushCardBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 14), Tag = "INJECTED_ATAK" };
            var lStack = new StackPanel();
            lStack.Children.Add(new TextBlock { Text = "Konfiguracja ATAK-CIV (PLI, siec oraz waznosc paczki/QR)", Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 14, Margin = new Thickness(0, 0, 0, 4) });
            lStack.Children.Add(new TextBlock { Text = "Czysty profil ATAK bez wymuszania wtyczek. Wpisanie 0 w polach waznosci oznacza waznosc bezterminowa.", Foreground = BrushMutedText, FontSize = 11, Margin = new Thickness(0, 0, 0, 10) });

            var gAtak = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 8) };
            gAtak.Children.Add(FieldBox("Strategia PLI", ComboCtrl(new[] { "Constant", "Dynamic" }, m.LocationReportingStrategy, win, v => { m.LocationReportingStrategy = v; })));
            gAtak.Children.Add(FieldBox("Const Rel/LTE (s)", IntCtrl(m.ConstantReportingRateReliable, v => { m.ConstantReportingRateReliable = v; })));
            gAtak.Children.Add(FieldBox("Const Unrel/Mesh (s)", IntCtrl(m.ConstantReportingRateUnreliable, v => { m.ConstantReportingRateUnreliable = v; })));
            gAtak.Children.Add(FieldBox("Dynamic Max (s)", IntCtrl(m.DynamicReportingRateMaxReliable, v => { m.DynamicReportingRateMaxReliable = v; })));
            gAtak.Children.Add(FieldBox("Dynamic Min (s)", IntCtrl(m.DynamicReportingRateMinReliable, v => { m.DynamicReportingRateMinReliable = v; })));
            gAtak.Children.Add(FieldBox("Postoj Rel (s)", IntCtrl(m.DynamicReportingRateStationaryReliable, v => { m.DynamicReportingRateStationaryReliable = v; })));
            gAtak.Children.Add(FieldBox("Postoj Mesh (s)", IntCtrl(m.DynamicReportingRateStationaryUnreliable, v => { m.DynamicReportingRateStationaryUnreliable = v; })));
            gAtak.Children.Add(FieldBox("TCP/UDP Timeout (s)", IntCtrl(m.TcpConnectTimeout, v => { m.TcpConnectTimeout = v; })));
            lStack.Children.Add(gAtak);

            var gValidity = new UniformGrid { Columns = 2, Margin = new Thickness(0, 2, 0, 4) };
            _injectedPkgValidityBox = ValidityTextCtrl(m.PackageValidityHours, v =>
            {
                m.PackageValidityHours = v;
                SyncValidityToNativeControls(win);
            });
            _injectedQrValidityBox = ValidityTextCtrl(m.QrValidityHours, v =>
            {
                m.QrValidityHours = v;
                SyncValidityToNativeControls(win);
            });

            gValidity.Children.Add(FieldBox("Ważność paczki .ftak (godz., 0 = bezterminowo)", _injectedPkgValidityBox));
            gValidity.Children.Add(FieldBox("Ważność kodu QR (godz., 0 = bezterminowo)", _injectedQrValidityBox));
            lStack.Children.Add(gValidity);

            _indefiniteStatusBadge = new TextBlock
            {
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            };
            UpdateIndefiniteBadge();
            lStack.Children.Add(_indefiniteStatusBadge);

            var gSw = new UniformGrid { Columns = 2 };
            gSw.Children.Add(TileSwitch("Wysylaj pozycje na zewnatrz", m.DispatchLocationCotExternal, v => { m.DispatchLocationCotExternal = v; }));
            gSw.Children.Add(TileSwitch("Polaczenia bezstrumieniowe Mesh", m.EnableNonStreamingConnections, v => { m.EnableNonStreamingConnections = v; }));
            lStack.Children.Add(gSw);

            card.Child = lStack;
            return card;
        }

        private static TextBox ValidityTextCtrl(int init, Action<int> onChg)
        {
            var tb = new TextBox
            {
                Text = init.ToString(),
                Height = 28,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(6, 0, 6, 0),
                FontSize = 11,
                Background = BrushInputBg,
                Foreground = Brushes.White,
                BorderBrush = (init == 0) ? BrushAccent : BrushInputBorder,
                BorderThickness = new Thickness(1),
                Tag = "INJECTED_VALIDITY",
                ToolTip = "Wpisz 0, aby ważność była bezterminowa (brak wygasania)."
            };
            tb.TextChanged += (_, __) =>
            {
                if (int.TryParse(tb.Text.Trim(), out int v) && v >= 0)
                {
                    tb.BorderBrush = (v == 0) ? BrushAccent : BrushInputBorder;
                    onChg(v);
                }
            };
            return tb;
        }

        private static void InjectMeshtasticFields(Panel parentPanel, Window win)
        {
            var loraGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 8, 0, 0), Tag = "INJECTED_FW" };
            loraGrid.Children.Add(FieldBox("LoRa Region", ComboCtrl(new[] { "EU_868", "US_915", "EU_433", "ANZ_915", "RU_864", "KR_920" }, Current.LoraRegion, win, v => { Current.LoraRegion = v; })));
            loraGrid.Children.Add(FieldBox("LoRa Modem Preset", ComboCtrl(new[] { "SHORT_FAST", "LONG_FAST", "LONG_SLOW", "MEDIUM_SLOW", "MEDIUM_FAST" }, Current.LoraModemPreset, win, v => { Current.LoraModemPreset = v; })));
            parentPanel.Children.Add(loraGrid);

            var fwContainer = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            var labelMin = new TextBlock { Text = "Firmware MIN:", Foreground = BrushMutedText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var txtMin = new TextBox { Text = Current.FirmwareMin, Width = 70, Height = 24, Background = BrushInputBg, Foreground = Brushes.White, BorderBrush = BrushInputBorder, BorderThickness = new Thickness(1), VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 0, 4, 0) };
            txtMin.TextChanged += (_, __) => { Current.FirmwareMin = txtMin.Text; };

            var labelMax = new TextBlock { Text = "Firmware MAX:", Foreground = BrushMutedText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 8, 0) };
            var txtMax = new TextBox { Text = Current.FirmwareMax, Width = 70, Height = 24, Background = BrushInputBg, Foreground = Brushes.White, BorderBrush = BrushInputBorder, BorderThickness = new Thickness(1), VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 0, 4, 0) };
            txtMax.TextChanged += (_, __) => { Current.FirmwareMax = txtMax.Text; };

            fwContainer.Children.Add(labelMin);
            fwContainer.Children.Add(txtMin);
            fwContainer.Children.Add(labelMax);
            fwContainer.Children.Add(txtMax);
            parentPanel.Children.Add(fwContainer);
        }

        private static UIElement FieldBox(string label, UIElement ctrl)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 8, 6) };
            sp.Children.Add(new TextBlock { Text = label, Foreground = BrushMutedText, FontSize = 10.5, Margin = new Thickness(0, 0, 0, 3) });
            sp.Children.Add(ctrl);
            return sp;
        }

        private static UIElement TextCtrl(string init, Action<string> onChg)
        {
            var tb = new TextBox { Text = init, Height = 28, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 0, 6, 0), FontSize = 11, Background = BrushInputBg, Foreground = Brushes.White, BorderBrush = BrushInputBorder, BorderThickness = new Thickness(1) };
            tb.TextChanged += (_, __) => onChg(tb.Text);
            return tb;
        }

        private static UIElement IntCtrl(int init, Action<int> onChg) => TextCtrl(init.ToString(), s => { if (int.TryParse(s.Trim(), out int v)) onChg(v); });

        private static UIElement ComboCtrl(string[] items, string init, Window win, Action<string> onChg)
        {
            var cb = new ComboBox
            {
                ItemsSource = items,
                SelectedItem = init,
                Height = 28,
                FontSize = 11,
                VerticalContentAlignment = VerticalAlignment.Center,
                Tag = "INJECTED"
            };
            ApplyUnifiedComboBoxTheme(cb, win);
            cb.SelectionChanged += (s, e) => { if (cb.SelectedItem is string str) onChg(str); };
            return cb;
        }

        private static UIElement TileSwitch(string title, bool init, Action<bool> onChg)
        {
            var b = new Border { Background = BrushItemBg, BorderBrush = BrushTileBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 8, 6) };
            var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var tb = new TextBlock { Text = title, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            var tr = new Border { Width = 36, Height = 20, CornerRadius = new CornerRadius(10), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
            var kn = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Margin = new Thickness(2) };
            bool state = init;
            void Upd()
            {
                tr.Background = state ? BrushAccent : BrushSwitchOff;
                kn.Background = state ? BrushDarkFg : BrushMutedText;
                kn.HorizontalAlignment = state ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            }
            Upd(); tr.Child = kn;
            tr.MouseLeftButtonDown += (_, __) => { state = !state; Upd(); onChg(state); };
            Grid.SetColumn(tb, 0); Grid.SetColumn(tr, 1);
            g.Children.Add(tb); g.Children.Add(tr);
            b.Child = g;
            return b;
        }

        public static void WriteGeneratedConfigsIfInteractive(string tgt)
        {
            if (string.IsNullOrWhiteSpace(tgt)) return;
            if (tgt.Contains("fth-analyze-", StringComparison.OrdinalIgnoreCase)) return;
            LastKnownSourceDir = tgt;
            if (Application.Current == null) return;
            WriteConfigsToDirectory(tgt);
        }

        private static void WriteConfigsToDirectory(string tgt, string overrideFaction = null, string overrideChannel = null, string overridePsk = null)
        {
            if (string.IsNullOrWhiteSpace(tgt)) return;
            try
            {
                if (!Directory.Exists(tgt)) Directory.CreateDirectory(tgt);
                string jsonPath = Path.Combine(tgt, "tactical_config.json");

                JsonObject root = null;
                if (File.Exists(jsonPath))
                {
                    try
                    {
                        string existing = File.ReadAllText(jsonPath, Encoding.UTF8);
                        root = JsonNode.Parse(existing) as JsonObject;
                    }
                    catch { }
                }
                if (root == null) root = new JsonObject();

                SyncFirstFactionToDefaultModel();
                string activeFaction = overrideFaction ?? Current.ActiveFaction ?? "ALL";
                string activeChannel = overrideChannel ?? Current.DefaultChannelName ?? "BLUFOR-TAK";
                string activePsk = overridePsk ?? Current.DefaultPsk;

                root["faction"] = activeFaction;
                root["packageValidityHours"] = Current.PackageValidityHours;
                root["qrValidityHours"] = Current.QrValidityHours;
                root["neverExpires"] = (Current.PackageValidityHours == 0);
                root["qrNeverExpires"] = (Current.QrValidityHours == 0);
                root["expiresAt"] = (Current.PackageValidityHours == 0)
                    ? DateTimeOffset.UtcNow.AddYears(100).ToString("o")
                    : DateTimeOffset.UtcNow.AddHours(Current.PackageValidityHours).ToString("o");

                // 1. Sekcja ATAK
                JsonObject atak;
                if (root.TryGetPropertyValue("atak", out var atakNode) && atakNode is JsonObject existingAtak)
                {
                    atak = existingAtak;
                }
                else
                {
                    atak = new JsonObject();
                    root["atak"] = atak;
                }

                atak["locationReportingStrategy"] = Current.LocationReportingStrategy;
                atak["constantReportingRateReliable"] = Current.ConstantReportingRateReliable;
                atak["constantReportingRateUnreliable"] = Current.ConstantReportingRateUnreliable;
                atak["dynamicReportingRateMinReliable"] = Current.DynamicReportingRateMinReliable;
                atak["dynamicReportingRateMaxReliable"] = Current.DynamicReportingRateMaxReliable;
                atak["dynamicReportingRateStationaryReliable"] = Current.DynamicReportingRateStationaryReliable;
                atak["dynamicReportingRateStationaryUnreliable"] = Current.DynamicReportingRateStationaryUnreliable;
                atak["tcpConnectTimeout"] = Current.TcpConnectTimeout;
                atak["udpNoDataTimeout"] = Current.UdpNoDataTimeout;
                atak["dispatchLocationCotExternal"] = Current.DispatchLocationCotExternal;
                atak["dispatchLocationCotExternalAtStart"] = Current.DispatchLocationCotExternalAtStart;
                atak["enableNonStreamingConnections"] = Current.EnableNonStreamingConnections;
                atak["autoDisableMeshSAWhenStreaming"] = Current.AutoDisableMeshSAWhenStreaming;

                // 2. Sekcja MESHTASTIC + LORA + STRONA KONFLIKTU
                JsonObject mesh;
                if (root.TryGetPropertyValue("meshtastic", out var meshNode) && meshNode is JsonObject existingMesh)
                {
                    mesh = existingMesh;
                }
                else
                {
                    mesh = new JsonObject();
                    root["meshtastic"] = mesh;
                }

                mesh["firmwareMin"] = Current.FirmwareMin;
                mesh["firmwareMax"] = Current.FirmwareMax;
                mesh["role"] = Current.DeviceRole;
                mesh["deviceRole"] = Current.DeviceRole;
                mesh["model"] = Current.HardwareModel;
                mesh["hardwareModel"] = Current.HardwareModel;
                mesh["region"] = Current.LoraRegion;
                mesh["loraRegion"] = Current.LoraRegion;
                mesh["modemPreset"] = Current.LoraModemPreset;
                mesh["loraModemPreset"] = Current.LoraModemPreset;
                mesh["channelName"] = activeChannel;
                mesh["psk"] = activePsk;
                mesh["usePreset"] = true;

                JsonObject lora;
                if (mesh.TryGetPropertyValue("lora", out var loraNode) && loraNode is JsonObject existingLora)
                {
                    lora = existingLora;
                }
                else
                {
                    lora = new JsonObject();
                    mesh["lora"] = lora;
                }

                lora["region"] = Current.LoraRegion;
                lora["modemPreset"] = Current.LoraModemPreset;
                lora["channelName"] = activeChannel;
                lora["psk"] = activePsk;
                lora["usePreset"] = true;
                lora["hopLimit"] = 3;
                lora["txEnabled"] = true;

                JsonObject rootLora;
                if (root.TryGetPropertyValue("lora", out var rLoraNode) && rLoraNode is JsonObject existingRLora)
                {
                    rootLora = existingRLora;
                }
                else
                {
                    rootLora = new JsonObject();
                    root["lora"] = rootLora;
                }

                rootLora["region"] = Current.LoraRegion;
                rootLora["modemPreset"] = Current.LoraModemPreset;
                rootLora["channelName"] = activeChannel;
                rootLora["psk"] = activePsk;
                rootLora["usePreset"] = true;

                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(jsonPath, root.ToJsonString(options), Encoding.UTF8);
            }
            catch { }
        }
    }
}