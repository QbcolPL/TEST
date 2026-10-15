#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625
using System;
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
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FieldTakHub.Builder.Services
{
    public class FactionConfigProfile
    {
        public string Name { get; set; } = "BLUFOR";
        public string ColorHex { get; set; } = "#2684FF";

        // Meshtastic
        public string ChannelName { get; set; } = "BLUFOR-TAK";
        public string Psk { get; set; } = TacticalConfigModel.GenerateRandomBase64Psk();
        public string LoraRegion { get; set; } = "EU_868";
        public string LoraModemPreset { get; set; } = "SHORT_FAST";
        public string DeviceRole { get; set; } = "Auto";
        public string HardwareModel { get; set; } = "generic_meshtastic";
        public string FirmwareMin { get; set; } = "2.7.26";
        public string FirmwareMax { get; set; } = "2.8.0.47";

        // OpenTAKServer (OTS) + Enrollment
        public string OtsHost { get; set; } = "ggzstak.duckdns.org";
        public int OtsSslPort { get; set; } = 8089;
        public int OtsEnrollPort { get; set; } = 8443;
        public string OtsEnrollmentMode { get; set; } = "Auto-Enrollment (Login + Hasło OTS)";
        public string OtsUsername { get; set; } = "";
        public string OtsPasswordOrToken { get; set; } = "";
        public string OtsGroupName { get; set; } = "BLUFOR";
        public bool OtsUseSsl { get; set; } = true;
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

        public int PackageValidityHours { get; set; } = 0;
        public int QrValidityHours { get; set; } = 0;

        public string ActiveFaction { get; set; } = "BLUFOR";
        public string DefaultChannelName { get; set; } = "BLUFOR-TAK";
        public string DefaultPsk { get; set; } = GenerateRandomBase64Psk();
        public string DefaultFactionColorHex { get; set; } = "#2684FF";

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

        internal static readonly SolidColorBrush BrushInputBg = CreateFrozenBrush(8, 19, 22);
        internal static readonly SolidColorBrush BrushInputBorder = CreateFrozenBrush(30, 66, 74);
        internal static readonly SolidColorBrush BrushItemBg = CreateFrozenBrush(15, 32, 37);
        internal static readonly SolidColorBrush BrushCardBg = CreateFrozenBrush(9, 20, 24);
        internal static readonly SolidColorBrush BrushCardBorder = CreateFrozenBrush(26, 58, 66);
        internal static readonly SolidColorBrush BrushMutedText = CreateFrozenBrush(138, 168, 164);
        internal static readonly SolidColorBrush BrushTileBorder = CreateFrozenBrush(28, 64, 72);
        internal static readonly SolidColorBrush BrushAccent = CreateFrozenBrush(31, 224, 197);
        internal static readonly SolidColorBrush BrushButtonAccent = CreateFrozenBrush(44, 229, 208);
        internal static readonly SolidColorBrush BrushSwitchOff = CreateFrozenBrush(24, 44, 50);
        internal static readonly SolidColorBrush BrushDarkFg = CreateFrozenBrush(6, 22, 24);

        internal static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        internal static SolidColorBrush BrushFromHex(string hex, Color fallback)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(hex))
                {
                    var c = (Color)ColorConverter.ConvertFromString(hex.Trim());
                    return CreateFrozenBrush(c.R, c.G, c.B);
                }
            }
            catch { }
            return CreateFrozenBrush(fallback.R, fallback.G, fallback.B);
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

        // Kolekcja profili stron konfliktu obsługiwana przez zakładki (karty)
        private static readonly List<FactionConfigProfile> _factions = new List<FactionConfigProfile>();
        private static int _selectedFactionIndex = 0;
        private static WrapPanel _factionTabHeaderStrip = null;
        private static Border _factionTabContentHost = null;

        public static List<(string Name, string Channel, string ColorHex, string Psk)> GetConfiguredFactions()
        {
            EnsureDefaultFactions();
            return _factions.Select(f => (f.Name, f.ChannelName, f.ColorHex, f.Psk)).ToList();
        }

        private static void EnsureDefaultFactions()
        {
            if (_factions.Count > 0) return;
            _factions.Add(new FactionConfigProfile
            {
                Name = "BLUFOR",
                ColorHex = "#2684FF",
                ChannelName = "BLUFOR-TAK",
                OtsGroupName = "BLUFOR",
                OtsUsername = "blufor_op",
                Psk = TacticalConfigModel.GenerateRandomBase64Psk()
            });
            _factions.Add(new FactionConfigProfile
            {
                Name = "OPFOR",
                ColorHex = "#EB4034",
                ChannelName = "OPFOR-TAK",
                OtsGroupName = "OPFOR",
                OtsUsername = "opfor_op",
                Psk = TacticalConfigModel.GenerateRandomBase64Psk()
            });
            _factions.Add(new FactionConfigProfile
            {
                Name = "ORGA",
                ColorHex = "#F5B82E",
                ChannelName = "ORGA-HQ",
                OtsGroupName = "ORGA",
                OtsUsername = "orga_hq",
                Psk = TacticalConfigModel.GenerateRandomBase64Psk()
            });
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
    <Setter Property=""Template"">
        <Setter.Value>
            <ControlTemplate TargetType=""ComboBox"">
                <Grid SnapsToDevicePixels=""True"">
                    <Border x:Name=""MainBorder"" Background=""{TemplateBinding Background}"" BorderBrush=""{TemplateBinding BorderBrush}"" BorderThickness=""{TemplateBinding BorderThickness}"" CornerRadius=""4"" />
                    <ToggleButton x:Name=""ToggleButton"" Focusable=""False"" IsChecked=""{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}"" ClickMode=""Press"">
                        <ToggleButton.Template>
                            <ControlTemplate TargetType=""ToggleButton"">
                                <Border Background=""Transparent"" BorderThickness=""0"">
                                    <Grid>
                                        <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""28""/></Grid.ColumnDefinitions>
                                        <Path Grid.Column=""1"" HorizontalAlignment=""Center"" VerticalAlignment=""Center"" Data=""M 0 0 L 4.5 4.5 L 9 0"" Stroke=""#FFFFFF"" StrokeThickness=""1.6"" />
                                    </Grid>
                                </Border>
                            </ControlTemplate>
                        </ToggleButton.Template>
                    </ToggleButton>
                    <ContentPresenter x:Name=""ContentSite"" IsHitTestVisible=""False"" Content=""{TemplateBinding SelectionBoxItem}"" ContentTemplate=""{TemplateBinding SelectionBoxItemTemplate}"" Margin=""10,0,28,0"" VerticalAlignment=""Center"" HorizontalAlignment=""Left"" TextElement.Foreground=""#FFFFFF"" />
                    <TextBlock x:Name=""FallbackText"" IsHitTestVisible=""False"" Text=""{Binding Text, RelativeSource={RelativeSource TemplatedParent}}"" Foreground=""#FFFFFF"" Margin=""10,0,28,0"" VerticalAlignment=""Center"" HorizontalAlignment=""Left"" Visibility=""Collapsed"" />
                    <Popup x:Name=""Popup"" Placement=""Bottom"" IsOpen=""{TemplateBinding IsDropDownOpen}"" AllowsTransparency=""True"" Focusable=""False"" PopupAnimation=""Slide"">
                        <Grid Name=""DropDown"" SnapsToDevicePixels=""True"" MinWidth=""{TemplateBinding ActualWidth}"" MaxHeight=""{TemplateBinding MaxDropDownHeight}"">
                            <Border Background=""#0F2025"" BorderBrush=""#1FE0C5"" BorderThickness=""1"" CornerRadius=""4"" Margin=""0,2,0,0"">
                                <ScrollViewer Margin=""2"" SnapsToDevicePixels=""True"">
                                    <StackPanel IsItemsHost=""True"" KeyboardNavigation.DirectionalNavigation=""Contained"" />
                                </ScrollViewer>
                            </Border>
                        </Grid>
                    </Popup>
                </Grid>
                <ControlTemplate.Triggers>
                    <Trigger Property=""IsMouseOver"" Value=""True""><Setter TargetName=""MainBorder"" Property=""BorderBrush"" Value=""#1FE0C5""/></Trigger>
                    <Trigger Property=""IsDropDownOpen"" Value=""True""><Setter TargetName=""MainBorder"" Property=""BorderBrush"" Value=""#1FE0C5""/></Trigger>
                    <Trigger Property=""SelectionBoxItem"" Value=""{x:Null}""><Setter TargetName=""FallbackText"" Property=""Visibility"" Value=""Visible""/></Trigger>
                    <Trigger Property=""SelectionBoxItem"" Value=""""><Setter TargetName=""FallbackText"" Property=""Visibility"" Value=""Visible""/></Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>";

        internal static Style GetDarkComboBoxStyle()
        {
            if (_darkComboBoxStyle == null)
            {
                try { _darkComboBoxStyle = (Style)XamlReader.Parse(DarkComboBoxXaml); _darkComboBoxStyle.Seal(); } catch { }
            }
            return _darkComboBoxStyle;
        }

        internal static Style GetDarkComboBoxItemStyle()
        {
            if (_darkComboBoxItemStyle == null)
            {
                var s = new Style(typeof(ComboBoxItem));
                s.Setters.Add(new Setter(Control.BackgroundProperty, BrushItemBg));
                s.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
                s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5)));
                s.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
                s.Seal();
                _darkComboBoxItemStyle = s;
            }
            return _darkComboBoxItemStyle;
        }

        internal static void ApplyUnifiedComboBoxTheme(ComboBox cb, Window win)
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
                if (cb.SelectedItem == null && cb.Items.Count > 0) cb.SelectedIndex = 0;

                if (win != null && !(bool)cb.GetValue(IsHookedProperty))
                {
                    cb.SetValue(IsHookedProperty, true);
                    cb.SelectionChanged += (s, e) => SyncNativeMeshtasticSelections(win);
                }
            }
            catch { }
        }

        internal static void FindVisualChildren<T>(DependencyObject depObj, List<T> list) where T : DependencyObject
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
                if (cur is Border b && b.Background != null)
                {
                    bestBorder = b;
                    if (VisualTreeHelper.GetParent(b) is Panel p && p.Children.Count > 1)
                        return b;
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
            EnsureDefaultFactions();

            void InitializeEnhancements()
            {
                try
                {
                    // 1. Gwarantowany górny pasek zakładek (Konfigurator Paczki vs Pełnoekranowy Generator Map)
                    TacticalMapGeneratorView.AttachFullScreenTab(win);

                    var combos = new List<ComboBox>();
                    FindVisualChildren(win, combos);
                    foreach (var c in combos) ApplyUnifiedComboBoxTheme(c, win);

                    var tbs = new List<TextBlock>();
                    FindVisualChildren(win, tbs);

                    foreach (var tb in tbs)
                    {
                        if (tb.Text != null && tb.Text.Contains("Konfiguracja Meshtastic"))
                        {
                            Border cardBorder = GetOuterCardBorder(tb);
                            if (cardBorder != null && VisualTreeHelper.GetParent(cardBorder) is Panel parentPanel && !HasInjectedTag(parentPanel, "INJECTED_ATAK"))
                            {
                                int idx = parentPanel.Children.IndexOf(cardBorder);
                                parentPanel.Children.Insert(idx, BuildAtakCard(win));
                                parentPanel.Children.Insert(idx + 1, BuildTabbedFactionsCard(win));
                            }
                        }

                        if (tb.Text != null && (tb.Text.Contains("Podgląd zawartości") || tb.Text.Equals("Pliki paczki", StringComparison.OrdinalIgnoreCase)))
                        {
                            Border previewBorder = FindPreviewCardContainer(tb);
                            if (previewBorder != null && VisualTreeHelper.GetParent(previewBorder) is Panel parentPanel && !HasInjectedTag(parentPanel, "INJECTED_BTN"))
                            {
                                var btnRefresh = new Button
                                {
                                    Content = "🔄 Odśwież / Skalkuluj zawartość (folder source)",
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
                                    btnRefresh.Content = $"🔄 Odśwież / Skalkuluj (Plików: {result.FileCount} | {FormatBytes(result.TotalBytes)})";
                                    btnRefresh.ToolTip = BuildStatsTooltip(result);
                                    btnRefresh.IsEnabled = true;
                                };

                                var btnPosterQuick = new Button
                                {
                                    Content = "🖨️ Eksportuj plakat startowy A4 (PNG)",
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
                                    await ExportSafezonePosterForActiveFactionAsync(win);
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
                win.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((s, e) => ApplyIndefiniteValidityBeforeAction(win)), true);
                win.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler((s, e) =>
                {
                    if (e.Key == Key.Enter || e.Key == Key.Space) ApplyIndefiniteValidityBeforeAction(win);
                }), true);

                win.Loaded += async (s, e) =>
                {
                    InitializeEnhancements();
                    var res = await ForceSizeUpdateAsync(win);
                    if (_injectedRefreshButton != null)
                    {
                        _injectedRefreshButton.Content = $"🔄 Odśwież / Skalkuluj (Plików: {res.FileCount} | {FormatBytes(res.TotalBytes)})";
                        _injectedRefreshButton.ToolTip = BuildStatsTooltip(res);
                    }
                };
            }
        }

        public static async Task RefreshSourceSizeFromExternalAsync(Window win)
        {
            var res = await ForceSizeUpdateAsync(win);
            if (_injectedRefreshButton != null)
            {
                _injectedRefreshButton.Content = $"🔄 Odśwież / Skalkuluj (Plików: {res.FileCount} | {FormatBytes(res.TotalBytes)})";
                _injectedRefreshButton.ToolTip = BuildStatsTooltip(res);
            }
        }

        // =========================================================================
        // 1. ZAKŁADKOWY GENERATOR STRON KONFLIKTU (KARTY STRON: MESHTASTIC + OTS ENROLLMENT)
        // =========================================================================
        private static UIElement BuildTabbedFactionsCard(Window win)
        {
            EnsureDefaultFactions();

            var card = new Border
            {
                Background = BrushCardBg,
                BorderBrush = BrushAccent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 14),
                Tag = "INJECTED_FACTIONS"
            };

            var stack = new StackPanel();

            // Nagłówek sekcji
            stack.Children.Add(new TextBlock
            {
                Text = "Strony Konfliktu — Karty Konfiguracji (Meshtastic + OpenTAKServer Enrollment + KML)",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 4)
            });
            stack.Children.Add(new TextBlock
            {
                Text = "Wybierz zakładkę strony konfliktu poniżej. Każda strona posiada własny kolor RGB, szyfrowany kanał Meshtastic (PSK), ustawienia OpenTAKServer z Enrollmentem oraz osobny folder warstw KML.",
                Foreground = BrushMutedText,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });

            // Pasek zakładek (Kart stron) + przyciski [ + Dodaj stronę ] i [ − Usuń stronę ]
            _factionTabHeaderStrip = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 0)
            };
            stack.Children.Add(_factionTabHeaderStrip);

            // Kontener na zawartość wybranej zakładki strony
            _factionTabContentHost = new Border
            {
                Background = BrushItemBg,
                BorderBrush = BrushTileBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(0, 8, 8, 8),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 12)
            };
            stack.Children.Add(_factionTabContentHost);

            // Dolny pasek akcji dla stron konfliktu
            var actionGrid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 6) };

            var btnBuildSingle = new Button
            {
                Content = "⚡ Buduj aktywną stronę (.ftak + A4)",
                Height = 32,
                Margin = new Thickness(0, 0, 5, 0),
                Background = BrushItemBg,
                Foreground = BrushAccent,
                BorderBrush = BrushAccent,
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };

            var btnBuildAll = new Button
            {
                Content = "🚀 Buduj WSZYSTKIE strony (.ftak + QR)",
                Height = 32,
                Margin = new Thickness(3, 0, 3, 0),
                Background = BrushButtonAccent,
                Foreground = BrushDarkFg,
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };

            var btnOpenMap = new Button
            {
                Content = "🗺️ Otwórz Generator Map (Pełny Ekran)",
                Height = 32,
                Margin = new Thickness(5, 0, 0, 0),
                Background = BrushItemBg,
                Foreground = Brushes.White,
                BorderBrush = BrushTileBorder,
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand
            };

            actionGrid.Children.Add(btnBuildSingle);
            actionGrid.Children.Add(btnBuildAll);
            actionGrid.Children.Add(btnOpenMap);
            stack.Children.Add(actionGrid);

            _factionStatusLabel = new TextBlock
            {
                Text = "Gotowy. Wybierz kartę strony konfliktu lub kliknij 'Buduj WSZYSTKIE strony'.",
                Foreground = BrushMutedText,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            };
            stack.Children.Add(_factionStatusLabel);

            btnBuildSingle.Click += async (s, e) =>
            {
                btnBuildSingle.IsEnabled = false;
                await ExportSafezonePosterForActiveFactionAsync(win);
                btnBuildSingle.IsEnabled = true;
            };

            btnBuildAll.Click += async (s, e) =>
            {
                btnBuildAll.IsEnabled = false;
                string orig = btnBuildAll.Content?.ToString();
                btnBuildAll.Content = "Budowanie wszystkich stron...";
                await BuildAllFactionsAsync(win);
                btnBuildAll.Content = orig;
                btnBuildAll.IsEnabled = true;
            };

            btnOpenMap.Click += (s, e) => TacticalMapGeneratorView.ShowFullScreenMapTab(win);

            RebuildFactionTabsUI(win);

            card.Child = stack;
            return card;
        }

        private static void RebuildFactionTabsUI(Window win)
        {
            if (_factionTabHeaderStrip == null || _factionTabContentHost == null) return;
            EnsureDefaultFactions();

            if (_selectedFactionIndex < 0 || _selectedFactionIndex >= _factions.Count)
                _selectedFactionIndex = 0;

            _factionTabHeaderStrip.Children.Clear();

            for (int i = 0; i < _factions.Count; i++)
            {
                int capturedIdx = i;
                var fac = _factions[i];
                bool isSelected = (i == _selectedFactionIndex);

                var dot = new Border
                {
                    Width = 11,
                    Height = 11,
                    CornerRadius = new CornerRadius(6),
                    Background = BrushFromHex(fac.ColorHex, Color.FromRgb(38, 132, 255)),
                    BorderBrush = Brushes.White,
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 0, 7, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };

                var lbl = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(fac.Name) ? $"STRONA-{i + 1}" : fac.Name.ToUpperInvariant(),
                    Foreground = isSelected ? BrushAccent : Brushes.White,
                    FontWeight = isSelected ? FontWeights.Bold : FontWeights.SemiBold,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var tabContent = new StackPanel { Orientation = Orientation.Horizontal };
                tabContent.Children.Add(dot);
                tabContent.Children.Add(lbl);

                var tabBtn = new Button
                {
                    Content = tabContent,
                    Height = 32,
                    Padding = new Thickness(14, 0, 14, 0),
                    Margin = new Thickness(0, 0, 4, 0),
                    Background = isSelected ? BrushItemBg : BrushInputBg,
                    BorderBrush = isSelected ? BrushAccent : BrushInputBorder,
                    BorderThickness = isSelected ? new Thickness(1, 2, 1, 0) : new Thickness(1, 1, 1, 1),
                    Cursor = Cursors.Hand
                };
                tabBtn.Click += (s, e) =>
                {
                    _selectedFactionIndex = capturedIdx;
                    SyncActiveFactionToModel();
                    RebuildFactionTabsUI(win);
                };
                _factionTabHeaderStrip.Children.Add(tabBtn);
            }

            // Przycisk [ + Dodaj stronę ]
            string[] defaultPalette = { "#2ECC71", "#9B59B6", "#E67E22", "#1ABC9C", "#E84393", "#00CEC9" };
            var btnAddTab = new Button
            {
                Content = "+ Dodaj stronę",
                Height = 30,
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(4, 1, 4, 0),
                Background = BrushInputBg,
                Foreground = BrushAccent,
                BorderBrush = BrushInputBorder,
                BorderThickness = new Thickness(1),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                ToolTip = "Dodaj nową zakładkę strony konfliktu"
            };
            btnAddTab.Click += (s, e) =>
            {
                int n = _factions.Count + 1;
                string col = defaultPalette[(n - 1) % defaultPalette.Length];
                _factions.Add(new FactionConfigProfile
                {
                    Name = $"STRONA-{n}",
                    ColorHex = col,
                    ChannelName = $"TAK-CH{n}",
                    OtsGroupName = $"STRONA-{n}",
                    OtsUsername = $"op_{n}",
                    Psk = TacticalConfigModel.GenerateRandomBase64Psk()
                });
                _selectedFactionIndex = _factions.Count - 1;
                SyncActiveFactionToModel();
                RebuildFactionTabsUI(win);
            };
            _factionTabHeaderStrip.Children.Add(btnAddTab);

            // Spójny przycisk [ − Usuń stronę ]
            var btnRemoveTab = new Button
            {
                Content = "− Usuń stronę",
                Height = 30,
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(0, 1, 0, 0),
                Background = BrushInputBg,
                Foreground = Brushes.White,
                BorderBrush = BrushInputBorder,
                BorderThickness = new Thickness(1),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                ToolTip = "Usuń aktualnie wybraną zakładkę strony konfliktu"
            };
            btnRemoveTab.Click += (s, e) =>
            {
                if (_factions.Count <= 1)
                {
                    if (_factionStatusLabel != null)
                    {
                        _factionStatusLabel.Text = "⚠ Musi pozostać przynajmniej jedna zakładka strony konfliktu.";
                        _factionStatusLabel.Foreground = BrushAccent;
                    }
                    return;
                }
                _factions.RemoveAt(_selectedFactionIndex);
                if (_selectedFactionIndex >= _factions.Count) _selectedFactionIndex = _factions.Count - 1;
                SyncActiveFactionToModel();
                RebuildFactionTabsUI(win);
            };
            _factionTabHeaderStrip.Children.Add(btnRemoveTab);

            // Zbudowanie zawartości dla aktywnej zakładki
            var activeFaction = _factions[_selectedFactionIndex];
            SyncActiveFactionToModel();
            _factionTabContentHost.Child = BuildSingleFactionTabEditor(win, activeFaction);
        }

        private static UIElement BuildSingleFactionTabEditor(Window win, FactionConfigProfile fac)
        {
            var panel = new StackPanel();

            // --- SEKCJA A: IDENTYFIKACJA STRONY, KOLOR RGB ORAZ FOLDER KML ---
            panel.Children.Add(CreateSubSectionHeader($"1. Identyfikacja strony ({fac.Name}) & Warstwy Taktyczne KML", fac.ColorHex));

            var idGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            idGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            idGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            idGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });

            var nameBox = (TextBox)TextCtrl(fac.Name, val =>
            {
                fac.Name = val.Trim();
                SyncActiveFactionToModel();
            });
            nameBox.LostFocus += (_, __) => RebuildFactionTabsUI(win);
            var nameField = FieldBox("Nazwa strony konfliktu (ID):", nameBox);
            Grid.SetColumn(nameField, 0);
            idGrid.Children.Add(nameField);

            // Przycisk wyboru koloru RGB
            var swatch = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(3),
                Background = BrushFromHex(fac.ColorHex, Color.FromRgb(38, 132, 255)),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var hexText = new TextBlock
            {
                Text = $"Kolor RGB ({fac.ColorHex.ToUpperInvariant()})",
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            var colorBtnStack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            colorBtnStack.Children.Add(swatch);
            colorBtnStack.Children.Add(hexText);

            var btnColor = new Button
            {
                Content = colorBtnStack,
                Height = 28,
                Background = BrushInputBg,
                BorderBrush = BrushInputBorder,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            btnColor.Click += (s, e) =>
            {
                string picked = ShowRgbColorPickerDialog(win, fac.Name, fac.ColorHex);
                if (!string.IsNullOrEmpty(picked))
                {
                    fac.ColorHex = picked;
                    SyncActiveFactionToModel();
                    RebuildFactionTabsUI(win);
                }
            };
            var colorField = FieldBox("Kolor taktyczny strony (Paleta RGB):", btnColor);
            Grid.SetColumn(colorField, 1);
            idGrid.Children.Add(colorField);

            var btnOpenKmlFolder = new Button
            {
                Content = $"📁 Folder KML (factions/{SanitizeFileName(fac.Name).ToLowerInvariant()})",
                Height = 28,
                Background = BrushInputBg,
                Foreground = BrushAccent,
                BorderBrush = BrushInputBorder,
                BorderThickness = new Thickness(1),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand
            };
            btnOpenKmlFolder.Click += async (s, e) =>
            {
                string srcDir = GetPrimarySourceDirectory(win);
                string sideLower = SanitizeFileName(fac.Name).ToLowerInvariant();
                await Task.Run(() => EnsureFactionFoldersInitialized(srcDir, new List<(string, string)> { (sideLower, fac.ColorHex) }));
                await ForceSizeUpdateAsync(win);
                TryOpenFolderInExplorer(Path.Combine(srcDir, "factions", sideLower));
            };
            var kmlField = FieldBox("Dedykowane nakładki KML strony:", btnOpenKmlFolder);
            Grid.SetColumn(kmlField, 2);
            idGrid.Children.Add(kmlField);

            panel.Children.Add(idGrid);

            // --- SEKCJA B: MESHTASTIC (KANAŁ SZYFROWANY STRONY) ---
            panel.Children.Add(CreateSubSectionHeader("2. Konfiguracja Meshtastic — Szyfrowany Kanał LoRa Strony", "#1FE0C5"));

            var meshRow1 = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 4) };
            meshRow1.Children.Add(FieldBox("Nazwa kanału LoRa:", TextCtrl(fac.ChannelName, v => { fac.ChannelName = v; SyncActiveFactionToModel(); })));
            meshRow1.Children.Add(FieldBox("Region LoRa:", ComboCtrl(new[] { "EU_868", "US_915", "EU_433", "ANZ_915", "RU_864", "KR_920" }, fac.LoraRegion, win, v => { fac.LoraRegion = v; Current.LoraRegion = v; })));
            meshRow1.Children.Add(FieldBox("Preset modemu LoRa:", ComboCtrl(new[] { "SHORT_FAST", "LONG_FAST", "MEDIUM_FAST", "MEDIUM_SLOW", "LONG_SLOW" }, fac.LoraModemPreset, win, v => { fac.LoraModemPreset = v; Current.LoraModemPreset = v; })));
            meshRow1.Children.Add(FieldBox("Rola urządzenia:", ComboCtrl(new[] { "Auto", "TAK", "CLIENT", "CLIENT_MUTE", "TRACKER", "ROUTER" }, fac.DeviceRole, win, v => { fac.DeviceRole = v; Current.DeviceRole = v; })));
            panel.Children.Add(meshRow1);

            // Pełnej szerokości wiersz na Klucz szyfrujący PSK (nic nie ucina!)
            var pskGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            pskGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pskGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            pskGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            pskGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });

            var pskBox = (TextBox)TextCtrl(fac.Psk, v => { fac.Psk = v; SyncActiveFactionToModel(); });
            var pskField = FieldBox("Klucz szyfrujący kanału Meshtastic PSK (AES-256 Base64):", pskBox);
            Grid.SetColumn(pskField, 0);
            pskGrid.Children.Add(pskField);

            var btnGenPsk = new Button
            {
                Content = "🎲 Losuj nowy klucz PSK",
                Height = 28,
                Margin = new Thickness(0, 0, 8, 6),
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = BrushInputBg,
                Foreground = BrushAccent,
                BorderBrush = BrushAccent,
                BorderThickness = new Thickness(1),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            btnGenPsk.Click += (s, e) =>
            {
                fac.Psk = TacticalConfigModel.GenerateRandomBase64Psk();
                pskBox.Text = fac.Psk;
                SyncActiveFactionToModel();
            };
            Grid.SetColumn(btnGenPsk, 1);
            pskGrid.Children.Add(btnGenPsk);

            var fwMinField = FieldBox("Firmware MIN:", TextCtrl(fac.FirmwareMin, v => { fac.FirmwareMin = v; Current.FirmwareMin = v; }));
            Grid.SetColumn(fwMinField, 2);
            pskGrid.Children.Add(fwMinField);

            var fwMaxField = FieldBox("Firmware MAX:", TextCtrl(fac.FirmwareMax, v => { fac.FirmwareMax = v; Current.FirmwareMax = v; }));
            Grid.SetColumn(fwMaxField, 3);
            pskGrid.Children.Add(fwMaxField);

            panel.Children.Add(pskGrid);

            // --- SEKCJA C: OPENTAKSERVER (OTS) Z ENROLLMENTEM DLA STRONY ---
            panel.Children.Add(CreateSubSectionHeader("3. Ustawienia OpenTAKServer (OTS) & Auto-Enrollment dla Strony", "#1FE0C5"));

            var otsRow1 = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            otsRow1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
            otsRow1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            otsRow1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            otsRow1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });

            var hostField = FieldBox("Adres serwera OpenTAKServer (Host / Domena):", TextCtrl(fac.OtsHost, v => fac.OtsHost = v));
            Grid.SetColumn(hostField, 0);
            otsRow1.Children.Add(hostField);

            var sslPortField = FieldBox("Port SSL CoT:", IntCtrl(fac.OtsSslPort, v => fac.OtsSslPort = v));
            Grid.SetColumn(sslPortField, 1);
            otsRow1.Children.Add(sslPortField);

            var enrollPortField = FieldBox("Port Enrollmentu:", IntCtrl(fac.OtsEnrollPort, v => fac.OtsEnrollPort = v));
            Grid.SetColumn(enrollPortField, 2);
            otsRow1.Children.Add(enrollPortField);

            var modeField = FieldBox("Tryb autoryzacji i Enrollmentu OTS:", ComboCtrl(new[]
            {
                "Auto-Enrollment (Login + Hasło OTS)",
                "Certyfikat strony (.p12 z paczki)",
                "Tylko połączenie TCP/SSL"
            }, fac.OtsEnrollmentMode, win, v => fac.OtsEnrollmentMode = v));
            Grid.SetColumn(modeField, 3);
            otsRow1.Children.Add(modeField);
            panel.Children.Add(otsRow1);

            var otsRow2 = new UniformGrid { Columns = 3 };
            otsRow2.Children.Add(FieldBox("Login Enrollmentu OTS (Użytkownik):", TextCtrl(fac.OtsUsername, v => fac.OtsUsername = v)));
            otsRow2.Children.Add(FieldBox("Hasło Enrollmentu / Token OTS:", TextCtrl(fac.OtsPasswordOrToken, v => fac.OtsPasswordOrToken = v)));
            otsRow2.Children.Add(FieldBox("Grupa na serwerze OTS (Marti Group):", TextCtrl(fac.OtsGroupName, v => fac.OtsGroupName = v)));
            panel.Children.Add(otsRow2);

            return panel;
        }

        private static UIElement CreateSubSectionHeader(string text, string accentHex)
        {
            var border = new Border
            {
                BorderBrush = BrushFromHex(accentHex, Color.FromRgb(31, 224, 197)),
                BorderThickness = new Thickness(3, 0, 0, 0),
                Padding = new Thickness(8, 2, 0, 2),
                Margin = new Thickness(0, 6, 0, 6)
            };
            border.Child = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 11.5
            };
            return border;
        }

        private static void SyncActiveFactionToModel()
        {
            EnsureDefaultFactions();
            if (_selectedFactionIndex < 0 || _selectedFactionIndex >= _factions.Count) _selectedFactionIndex = 0;
            var f = _factions[_selectedFactionIndex];
            Current.ActiveFaction = string.IsNullOrWhiteSpace(f.Name) ? "BLUFOR" : f.Name.Trim();
            Current.DefaultChannelName = string.IsNullOrWhiteSpace(f.ChannelName) ? "BLUFOR-TAK" : f.ChannelName.Trim();
            Current.DefaultPsk = string.IsNullOrWhiteSpace(f.Psk) ? Current.DefaultPsk : f.Psk.Trim();
            Current.DefaultFactionColorHex = string.IsNullOrWhiteSpace(f.ColorHex) ? "#2684FF" : f.ColorHex.Trim();
            Current.LoraRegion = f.LoraRegion;
            Current.LoraModemPreset = f.LoraModemPreset;
            Current.DeviceRole = f.DeviceRole;
            Current.FirmwareMin = f.FirmwareMin;
            Current.FirmwareMax = f.FirmwareMax;
        }

        private static string ShowRgbColorPickerDialog(Window owner, string factionName, string initialHex)
        {
            Color initColor = Color.FromRgb(38, 132, 255);
            try { initColor = (Color)ColorConverter.ConvertFromString(initialHex); } catch { }

            byte r = initColor.R, g = initColor.G, b = initColor.B;
            string selectedHex = $"#{r:X2}{g:X2}{b:X2}";
            bool confirmed = false;

            var dlg = new Window
            {
                Title = $"Paleta RGB — Kolor strony: {factionName}",
                Width = 420,
                Height = 365,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                Background = BrushCardBg,
                Foreground = Brushes.White,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow
            };

            var root = new StackPanel { Margin = new Thickness(16) };

            var previewBorder = new Border
            {
                Height = 42,
                CornerRadius = new CornerRadius(6),
                Background = CreateFrozenBrush(r, g, b),
                BorderBrush = BrushAccent,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 12)
            };
            var previewText = new TextBlock
            {
                Text = $"{factionName}   ({selectedHex})",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            previewBorder.Child = previewText;
            root.Children.Add(previewBorder);

            root.Children.Add(new TextBlock { Text = "Szybkie kolory taktyczne:", Foreground = BrushMutedText, FontSize = 11, Margin = new Thickness(0, 0, 0, 4) });
            var swatchesWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            string[] presets = { "#2684FF", "#EB4034", "#F5B82E", "#2ECC71", "#9B59B6", "#1FE0C5", "#E67E22", "#E84393", "#00CEC9", "#95A5A6" };

            Slider sR = null, sG = null, sB = null;
            TextBox hexBox = null;
            bool updating = false;

            void UpdateFromRgb()
            {
                if (updating) return;
                updating = true;
                selectedHex = $"#{r:X2}{g:X2}{b:X2}";
                previewBorder.Background = CreateFrozenBrush(r, g, b);
                previewText.Text = $"{factionName}   ({selectedHex})";
                if (hexBox != null) hexBox.Text = selectedHex;
                if (sR != null) sR.Value = r;
                if (sG != null) sG.Value = g;
                if (sB != null) sB.Value = b;
                updating = false;
            }

            foreach (var pHex in presets)
            {
                var swBtn = new Button
                {
                    Width = 32,
                    Height = 24,
                    Margin = new Thickness(0, 0, 6, 4),
                    Background = BrushFromHex(pHex, initColor),
                    BorderBrush = Brushes.White,
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    ToolTip = pHex
                };
                string captured = pHex;
                swBtn.Click += (_, __) =>
                {
                    try
                    {
                        var c = (Color)ColorConverter.ConvertFromString(captured);
                        r = c.R; g = c.G; b = c.B;
                        UpdateFromRgb();
                    }
                    catch { }
                };
                swatchesWrap.Children.Add(swBtn);
            }
            root.Children.Add(swatchesWrap);

            UIElement MakeChannelSlider(string label, byte initVal, out Slider slider, Action<byte> onVal)
            {
                var rowGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

                var lbl = new TextBlock { Text = label, Foreground = Brushes.White, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
                var valTxt = new TextBlock { Text = initVal.ToString(), Foreground = BrushAccent, FontSize = 11, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                var sl = new Slider { Minimum = 0, Maximum = 255, Value = initVal, VerticalAlignment = VerticalAlignment.Center };
                sl.ValueChanged += (_, e) =>
                {
                    byte v = (byte)Math.Clamp((int)Math.Round(e.NewValue), 0, 255);
                    valTxt.Text = v.ToString();
                    onVal(v);
                    UpdateFromRgb();
                };
                slider = sl;
                Grid.SetColumn(lbl, 0); Grid.SetColumn(sl, 1); Grid.SetColumn(valTxt, 2);
                rowGrid.Children.Add(lbl); rowGrid.Children.Add(sl); rowGrid.Children.Add(valTxt);
                return rowGrid;
            }

            root.Children.Add(MakeChannelSlider("Red (R):", r, out sR, v => r = v));
            root.Children.Add(MakeChannelSlider("Green (G):", g, out sG, v => g = v));
            root.Children.Add(MakeChannelSlider("Blue (B):", b, out sB, v => b = v));

            var hexGrid = new Grid { Margin = new Thickness(0, 4, 0, 14) };
            hexGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            hexGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hexGrid.Children.Add(new TextBlock { Text = "Kod HEX RGB:", Foreground = BrushMutedText, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
            hexBox = new TextBox
            {
                Text = selectedHex,
                Height = 26,
                Background = BrushInputBg,
                Foreground = Brushes.White,
                BorderBrush = BrushInputBorder,
                BorderThickness = new Thickness(1),
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(6, 0, 6, 0)
            };
            hexBox.TextChanged += (_, __) =>
            {
                if (updating) return;
                try
                {
                    var c = (Color)ColorConverter.ConvertFromString(hexBox.Text.Trim());
                    r = c.R; g = c.G; b = c.B;
                    UpdateFromRgb();
                }
                catch { }
            };
            Grid.SetColumn(hexBox, 1);
            hexGrid.Children.Add(hexBox);
            root.Children.Add(hexGrid);

            var btnBar = new UniformGrid { Columns = 2 };
            var btnCancel = new Button { Content = "Anuluj", Height = 30, Margin = new Thickness(0, 0, 6, 0), Background = BrushItemBg, Foreground = Brushes.White, BorderBrush = BrushTileBorder, BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
            var btnOk = new Button { Content = "✓ Zastosuj kolor RGB", Height = 30, Margin = new Thickness(6, 0, 0, 0), Background = BrushButtonAccent, Foreground = BrushDarkFg, FontWeight = FontWeights.Bold, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
            btnCancel.Click += (_, __) => dlg.Close();
            btnOk.Click += (_, __) => { confirmed = true; dlg.Close(); };
            btnBar.Children.Add(btnCancel);
            btnBar.Children.Add(btnOk);
            root.Children.Add(btnBar);

            dlg.Content = root;
            dlg.ShowDialog();
            return confirmed ? selectedHex : null;
        }

        private static async Task BuildAllFactionsAsync(Window win)
        {
            try
            {
                EnsureDefaultFactions();
                string sourceDir = GetPrimarySourceDirectory(win);
                string eventName = GetEventNameFromWindow(win);
                string parentDir = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(sourceDir)) ?? AppDomain.CurrentDomain.BaseDirectory;
                string outDir = Path.Combine(parentDir, "release_factions");
                Directory.CreateDirectory(outDir);

                var allSides = _factions.Select(f => (SanitizeFileName(f.Name).ToLowerInvariant(), f.ColorHex)).ToList();

                foreach (var fac in _factions)
                {
                    string safeName = SanitizeFileName(fac.Name);
                    string ftakPath = Path.Combine(outDir, $"{eventName}_{safeName}.ftak");
                    string sha256 = await Task.Run(() =>
                    {
                        EnsureFactionFoldersInitialized(sourceDir, allSides);
                        WriteConfigsToDirectory(sourceDir, fac);
                        BuildFilteredFactionFtakArchive(sourceDir, ftakPath, fac, eventName);
                        return ComputeFileSha256(ftakPath);
                    });

                    string qrPayload = $"ftak://provision?event={Uri.EscapeDataString(eventName)}&faction={Uri.EscapeDataString(safeName)}&color={Uri.EscapeDataString(fac.ColorHex)}&channel={Uri.EscapeDataString(fac.ChannelName)}&ots={Uri.EscapeDataString(fac.OtsHost)}:{fac.OtsSslPort}&region={fac.LoraRegion}&preset={fac.LoraModemPreset}&sha256={sha256.Substring(0, 16)}";
                    string posterPath = Path.Combine(outDir, $"Plakat_Safezone_A4_{eventName}_{safeName}.png");
                    RenderSafezoneA4PosterPng(win, posterPath, eventName, fac, qrPayload, sha256);
                }

                SyncActiveFactionToModel();
                await Task.Run(() => WriteConfigsToDirectory(sourceDir, _factions[_selectedFactionIndex]));
                await ForceSizeUpdateAsync(win);

                if (_factionStatusLabel != null)
                {
                    _factionStatusLabel.Text = $"✓ Zbudowano {_factions.Count} paczek (.ftak) oraz {_factions.Count} plakatów A4 (PNG) w: {outDir}";
                    _factionStatusLabel.Foreground = BrushAccent;
                }

                TryOpenFolderInExplorer(outDir);
            }
            catch (Exception ex)
            {
                if (_factionStatusLabel != null)
                {
                    _factionStatusLabel.Text = $"Błąd generowania stron: {ex.Message}";
                    _factionStatusLabel.Foreground = BrushMutedText;
                }
            }
        }

        private static async Task ExportSafezonePosterForActiveFactionAsync(Window win)
        {
            try
            {
                EnsureDefaultFactions();
                SyncActiveFactionToModel();
                var fac = _factions[Math.Clamp(_selectedFactionIndex, 0, _factions.Count - 1)];

                string sourceDir = GetPrimarySourceDirectory(win);
                string eventName = GetEventNameFromWindow(win);
                string parentDir = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(sourceDir)) ?? AppDomain.CurrentDomain.BaseDirectory;
                string outDir = Path.Combine(parentDir, "release_factions");
                Directory.CreateDirectory(outDir);

                string safeName = SanitizeFileName(fac.Name);
                string ftakPath = Path.Combine(outDir, $"{eventName}_{safeName}.ftak");
                string sha256 = await Task.Run(() =>
                {
                    WriteConfigsToDirectory(sourceDir, fac);
                    BuildFilteredFactionFtakArchive(sourceDir, ftakPath, fac, eventName);
                    return ComputeFileSha256(ftakPath);
                });

                string qrPayload = $"ftak://provision?event={Uri.EscapeDataString(eventName)}&faction={Uri.EscapeDataString(safeName)}&color={Uri.EscapeDataString(fac.ColorHex)}&channel={Uri.EscapeDataString(fac.ChannelName)}&ots={Uri.EscapeDataString(fac.OtsHost)}:{fac.OtsSslPort}&region={fac.LoraRegion}&preset={fac.LoraModemPreset}&sha256={sha256.Substring(0, 16)}";
                string posterPath = Path.Combine(outDir, $"Plakat_Safezone_A4_{eventName}_{safeName}.png");
                RenderSafezoneA4PosterPng(win, posterPath, eventName, fac, qrPayload, sha256);

                if (_factionStatusLabel != null)
                {
                    _factionStatusLabel.Text = $"✓ Zbudowano paczkę i plakat A4 dla strony {safeName}: {posterPath}";
                    _factionStatusLabel.Foreground = BrushAccent;
                }

                TryOpenFolderInExplorer(outDir);
            }
            catch { }
        }

        internal static string GetPrimarySourceDirectory(Window win)
        {
            var dirs = ResolveSourceDirectories(win);
            return dirs.FirstOrDefault(Directory.Exists) ?? dirs.FirstOrDefault() ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "workspace", "source");
        }

        internal static string GetEventNameFromWindow(Window win)
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

        internal static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Operacja_TAK";
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        private static string HexToKmlColor(string hex)
        {
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(hex);
                return $"ff{c.B:X2}{c.G:X2}{c.R:X2}";
            }
            catch { return "ffff8426"; }
        }

        private static void EnsureFactionFoldersInitialized(string sourceDir, List<(string SideLower, string ColorHex)> sides)
        {
            try
            {
                string factionsRoot = Path.Combine(sourceDir, "factions");
                foreach (var item in sides)
                {
                    if (string.IsNullOrWhiteSpace(item.SideLower)) continue;
                    string sideDir = Path.Combine(factionsRoot, item.SideLower);
                    if (!Directory.Exists(sideDir)) Directory.CreateDirectory(sideDir);

                    string sampleKml = Path.Combine(sideDir, $"strefa_{item.SideLower}.kml");
                    if (!File.Exists(sampleKml))
                    {
                        string kmlCol = HexToKmlColor(item.ColorHex);
                        string kmlContent = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<kml xmlns=""http://www.opengis.net/kml/2.2"">
  <Document>
    <name>Warstwa Taktyczna {item.SideLower.ToUpperInvariant()}</name>
    <Style id=""factionStyle""><IconStyle><color>{kmlCol}</color></IconStyle><LineStyle><color>{kmlCol}</color><width>3</width></LineStyle></Style>
    <Placemark>
      <name>HQ {item.SideLower.ToUpperInvariant()}</name>
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

        private static void BuildFilteredFactionFtakArchive(string sourceDir, string destinationFtakPath, FactionConfigProfile fac, string eventName)
        {
            if (File.Exists(destinationFtakPath)) File.Delete(destinationFtakPath);

            using var fs = new FileStream(destinationFtakPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Create, false, Encoding.UTF8);

            string factionCode = SanitizeFileName(fac.Name);

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
                        if (!relLower.StartsWith(targetFactionPrefix)) continue;
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
                ["factionColor"] = fac.ColorHex,
                ["channelName"] = fac.ChannelName,
                ["psk"] = fac.Psk,
                ["loraRegion"] = fac.LoraRegion,
                ["loraModemPreset"] = fac.LoraModemPreset,
                ["otsHost"] = fac.OtsHost,
                ["otsSslPort"] = fac.OtsSslPort,
                ["otsEnrollPort"] = fac.OtsEnrollPort,
                ["otsEnrollmentMode"] = fac.OtsEnrollmentMode,
                ["otsGroupName"] = fac.OtsGroupName,
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
            return Convert.ToHexString(sha.ComputeHash(stream));
        }

        private static void RenderSafezoneA4PosterPng(Window win, string outputPath, string eventName, FactionConfigProfile fac, string qrPayload, string sha256Hex)
        {
            const int width = 1240;
            const int height = 1754;

            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(BrushCardBg, null, new Rect(0, 0, width, height));
                dc.DrawRoundedRectangle(null, new Pen(BrushAccent, 4), new Rect(30, 30, width - 60, height - 60), 18, 18);

                Brush bannerBrush = BrushFromHex(fac.ColorHex, Color.FromRgb(38, 132, 255));
                dc.DrawRoundedRectangle(bannerBrush, null, new Rect(70, 70, width - 140, 120), 12, 12);
                DrawCenteredText(dc, "FIELD TAK HUB 2.4 — KARTA WDROŻENIOWA SAFEZONE", 24, Brushes.White, width / 2.0, 92, true);
                DrawCenteredText(dc, $"OPERACJA: {eventName.ToUpperInvariant()}   |   STRONA: {fac.Name.ToUpperInvariant()} ({fac.ColorHex.ToUpperInvariant()})", 32, Brushes.White, width / 2.0, 130, true);

                DrawCenteredText(dc, "SKANUJESZ  ➔  KONFIGURUJESZ  ➔  SPRAWDZASZ  ➔  JESTEŚ GOTOWY", 20, BrushAccent, width / 2.0, 220, true);

                double qrBoxSize = 620;
                double qrBoxX = (width - qrBoxSize) / 2.0;
                double qrBoxY = 275;
                dc.DrawRoundedRectangle(Brushes.White, new Pen(BrushAccent, 4), new Rect(qrBoxX, qrBoxY, qrBoxSize, qrBoxSize), 16, 16);
                DrawDeterministicQrMatrix(dc, qrPayload, qrBoxX + 45, qrBoxY + 45, qrBoxSize - 90);

                double stepsY = 935;
                dc.DrawRoundedRectangle(BrushItemBg, new Pen(BrushTileBorder, 2), new Rect(70, stepsY, width - 140, 340), 12, 12);
                DrawCenteredText(dc, "INSTRUKCJA SZYBKIEGO WDROŻENIA DLA UCZESTNIKA (3 KROKI)", 24, BrushAccent, width / 2.0, stepsY + 24, true);

                DrawLeftText(dc, "1. ZESKANUJ KOD QR:", 22, Brushes.White, 110, stepsY + 85, true);
                DrawLeftText(dc, "Otwórz aplikację Field TAK Hub i zeskanuj powyższy kod QR (lub użyj trybu P2P Hotspot od kolegi).", 19, BrushMutedText, 110, stepsY + 118, false);
                DrawLeftText(dc, "2. PODŁĄCZ RADIO MESHTASTIC (LORA):", 22, Brushes.White, 110, stepsY + 170, true);
                DrawLeftText(dc, "Włącz Bluetooth lub podepnij kabel USB. Aplikacja sama rozpozna model i wgra klucze strony.", 19, BrushMutedText, 110, stepsY + 203, false);
                DrawLeftText(dc, "3. ZIELONY EKRAN = GOTOWY DO GRY:", 22, Brushes.White, 110, stepsY + 255, true);
                DrawLeftText(dc, "Sprawdź status końcowy:  ✓ ATAK gotowy   ✓ Meshtastic gotowy   ✓ Serwer OTS dostępny.", 19, BrushAccent, 110, stepsY + 288, true);

                double techY = 1310;
                dc.DrawRoundedRectangle(BrushInputBg, new Pen(BrushInputBorder, 2), new Rect(70, techY, width - 140, 360), 12, 12);
                DrawCenteredText(dc, "PARAMETRY TECHNICZNE PACZKI, SERWERA OTS I RADIA LORA", 22, Brushes.White, width / 2.0, techY + 22, true);

                string validityText = Current.PackageValidityHours == 0 ? "BEZTERMINOWA (0)" : $"{Current.PackageValidityHours} godz.";
                DrawLeftText(dc, $"• Strona konfliktu:   {fac.Name} ({fac.ColorHex})   |   Kanał LoRa: {fac.ChannelName}", 20, Brushes.White, 110, techY + 75, true);
                DrawLeftText(dc, $"• Serwer OTS:         {fac.OtsHost}:{fac.OtsSslPort} (Enrollment: {fac.OtsEnrollPort} | Grupa: {fac.OtsGroupName})", 20, Brushes.White, 110, techY + 118, false);
                DrawLeftText(dc, $"• Pasmo / Modem:      Region {fac.LoraRegion}   |   Preset {fac.LoraModemPreset}   |   Rola {fac.DeviceRole}", 20, Brushes.White, 110, techY + 161, false);
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

        internal static void DrawCenteredText(DrawingContext dc, string text, double fontSize, Brush brush, double centerX, double y, bool bold)
        {
            var ft = new FormattedText(text ?? "", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal), fontSize, brush, 1.0);
            dc.DrawText(ft, new Point(centerX - ft.Width / 2.0, y));
        }

        internal static void DrawLeftText(DrawingContext dc, string text, double fontSize, Brush brush, double x, double y, bool bold)
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

        internal static void TryOpenFolderInExplorer(string folderPath)
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
        // 3. NAPRAWIONY KALKULATOR I PODGLĄD ZAWARTOŚCI PACZKI (ROZBICIE NA KATEGORIE)
        // =========================================================================
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

        private static string BuildStatsTooltip(SourceStats s)
        {
            return $"Aktywny folder source: {s.SourcePath}\n" +
                   $"• Pliki paczki (Suma): {FormatBytes(s.TotalBytes)} ({s.FileCount} plików)\n" +
                   $"• Mapy: {FormatBytes(s.MapsBytes)}\n" +
                   $"• Pluginy (.apk): {FormatBytes(s.PluginsBytes)}\n" +
                   $"• Konfiguracja: {FormatBytes(s.ConfigBytes)}\n" +
                   $"• Dane taktyczne (KML/KMZ/GPX/CoT): {FormatBytes(s.TacticalBytes)}\n" +
                   $"• Pozostałe: {FormatBytes(s.OtherBytes)}";
        }

        private static Border FindPreviewCardContainer(DependencyObject startElement)
        {
            Border bestBorder = null;
            DependencyObject cur = startElement;
            while (cur != null)
            {
                if (cur is Border b)
                {
                    var tbs = new List<TextBlock>();
                    FindVisualChildren(b, tbs);
                    bool hasTotal = tbs.Any(t => t.Text != null && t.Text.Trim().StartsWith("Pliki paczki", StringComparison.OrdinalIgnoreCase));
                    bool hasOther = tbs.Any(t => t.Text != null && t.Text.Trim().StartsWith("Pozostałe", StringComparison.OrdinalIgnoreCase));
                    if (hasTotal && hasOther) return b;
                    if (bestBorder == null && tbs.Count >= 10) bestBorder = b;
                }
                cur = VisualTreeHelper.GetParent(cur) ?? LogicalTreeHelper.GetParent(cur);
            }
            return bestBorder;
        }

        private static List<string> ResolveSourceDirectories(Window win)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<string>();

            void AddCandidate(string raw)
            {
                if (string.IsNullOrWhiteSpace(raw) || raw.Length < 3) return;
                try
                {
                    string clean = Path.TrimEndingDirectorySeparator(raw.Trim().Trim('"'));
                    if (!Path.IsPathRooted(clean)) return;
                    if (File.Exists(clean)) clean = Path.GetDirectoryName(clean) ?? "";
                    if (Directory.Exists(clean))
                    {
                        string dirToAdd = string.Equals(Path.GetFileName(clean), "source", StringComparison.OrdinalIgnoreCase)
                            ? clean
                            : Path.Combine(clean, "source");
                        if (seen.Add(dirToAdd)) list.Add(dirToAdd);
                    }
                }
                catch { }
            }

            if (!string.IsNullOrWhiteSpace(LastKnownSourceDir)) AddCandidate(LastKnownSourceDir);

            if (win != null)
            {
                var visited = new HashSet<object>();
                void InspectObj(object obj, int depth)
                {
                    if (obj == null || depth > 3 || !visited.Add(obj)) return;
                    Type t = obj.GetType();
                    if (t.IsPrimitive || t.IsEnum || obj is Brush || obj is Style) return;
                    if (obj is string str) { AddCandidate(str); return; }
                    if (depth > 0 && t.Namespace != null && t.Namespace.StartsWith("System.Windows")) return;

                    var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                    foreach (var f in t.GetFields(flags))
                    {
                        try
                        {
                            var v = f.GetValue(obj);
                            if (v is string sv) AddCandidate(sv);
                            else if (v != null && t.Namespace != null && t.Namespace.StartsWith("FieldTakHub")) InspectObj(v, depth + 1);
                        }
                        catch { }
                    }
                    foreach (var p in t.GetProperties(flags))
                    {
                        if (p.GetIndexParameters().Length > 0) continue;
                        try
                        {
                            var v = p.GetValue(obj);
                            if (v is string sv) AddCandidate(sv);
                        }
                        catch { }
                    }
                }

                InspectObj(win, 0);

                var textBoxes = new List<TextBox>();
                FindVisualChildren(win, textBoxes);
                foreach (var tbx in textBoxes) AddCandidate(tbx.Text);

                var textBlocks = new List<TextBlock>();
                FindVisualChildren(win, textBlocks);
                foreach (var tb in textBlocks) AddCandidate(tb.Text);
            }

            if (list.Count == 0)
            {
                list.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "workspace", "source"));
            }
            return list;
        }

        private static async Task<SourceStats> ForceSizeUpdateAsync(Window win)
        {
            var empty = new SourceStats();
            if (Application.Current == null || win == null) return empty;

            try
            {
                SyncNativeMeshtasticSelections(win);
                SyncActiveFactionToModel();
                var sourceDirs = ResolveSourceDirectories(win);
                var activeProfile = _factions[Math.Clamp(_selectedFactionIndex, 0, _factions.Count - 1)];

                SourceStats bestStats = null;
                await Task.Run(() =>
                {
                    foreach (var sDir in sourceDirs)
                    {
                        try
                        {
                            if (!Directory.Exists(sDir)) Directory.CreateDirectory(sDir);
                            WriteConfigsToDirectory(sDir, activeProfile);
                            var st = CalculateSourceFolderStats(sDir);
                            if (bestStats == null || st.FileCount > bestStats.FileCount)
                                bestStats = st;
                        }
                        catch { }
                    }
                });

                bestStats ??= empty;
                UpdatePreviewCardLabels(win, bestStats);

                _ = win.Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdatePreviewCardLabels(win, bestStats);
                }), DispatcherPriority.ApplicationIdle);

                return bestStats;
            }
            catch { return empty; }
        }

        private static SourceStats CalculateSourceFolderStats(string sourceDir)
        {
            var stats = new SourceStats { SourcePath = sourceDir };
            if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir)) return stats;

            try
            {
                foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
                {
                    string fileName = Path.GetFileName(file);
                    if (string.IsNullOrEmpty(fileName) ||
                        fileName.StartsWith(".", StringComparison.Ordinal) ||
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
                    string nameLower = fileName.ToLowerInvariant();

                    stats.FileCount++;

                    // 1. PLUGIN: wyłącznie pliki .apk (lub pliki binarne w katalogu plugins/)
                    if (ext == ".apk" || (rel.StartsWith("plugins/") && ext != ".kml" && ext != ".kmz" && ext != ".gpx" && ext != ".mbtiles" && ext != ".sqlite" && ext != ".json" && ext != ".pref"))
                    {
                        stats.PluginsBytes += len;
                    }
                    // 2. MAPY: bazy kafelków, GeoTIFF, źródła map XML oraz cały podfolder maps/
                    else if (ext == ".mbtiles" || ext == ".sqlite" || ext == ".sqlitedb" || ext == ".tif" || ext == ".tiff" || ext == ".geotiff" ||
                             ext == ".dt0" || ext == ".dt1" || ext == ".dt2" || ext == ".ovr" || ext == ".ecw" || ext == ".jp2" ||
                             rel.StartsWith("maps/") || rel.StartsWith("mapy/") || rel.StartsWith("imagery/") ||
                             (ext == ".xml" && (nameLower.Contains("map") || nameLower.Contains("tile") || nameLower.Contains("online"))) ||
                             (ext == ".kmz" && (rel.StartsWith("maps/") || nameLower.Contains("mapa") || nameLower.Contains("orto") || nameLower.Contains("lidar") || nameLower.Contains("topo"))))
                    {
                        stats.MapsBytes += len;
                    }
                    // 3. KONFIGURACJA: profile JSON/PREF, certyfikaty .p12/.pem/.crt, server.txt oraz foldery config/ i certs/
                    else if (ext == ".json" || ext == ".pref" || ext == ".cfg" || ext == ".conf" || ext == ".ini" ||
                             ext == ".p12" || ext == ".pfx" || ext == ".pem" || ext == ".crt" || ext == ".cer" || ext == ".key" || ext == ".ovpn" ||
                             nameLower == "server.txt" || rel.StartsWith("config/") || rel.StartsWith("certs/") || rel.StartsWith("cert/"))
                    {
                        stats.ConfigBytes += len;
                    }
                    // 4. DANE TAKTYCZNE: warstwy KML/KMZ, ślady GPX, obiekty CoT, GeoJSON, Shapefile oraz foldery atak/, overlays/, factions/
                    else if (ext == ".kml" || ext == ".kmz" || ext == ".gpx" || ext == ".cot" || ext == ".geojson" ||
                             ext == ".shp" || ext == ".shx" || ext == ".dbf" || ext == ".csv" ||
                             rel.StartsWith("atak/") || rel.StartsWith("overlays/") || rel.StartsWith("factions/") || rel.StartsWith("data/"))
                    {
                        stats.TacticalBytes += len;
                    }
                    // 5. POZOSTAŁE
                    else
                    {
                        stats.OtherBytes += len;
                    }
                }

                stats.TotalBytes = stats.MapsBytes + stats.PluginsBytes + stats.ConfigBytes + stats.TacticalBytes + stats.OtherBytes;
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
            while (val >= 1024 && idx < units.Length - 1) { val /= 1024; idx++; }
            return idx == 0 ? $"{bytes} B" : $"{val:0.#} {units[idx]}";
        }

        // Precyzyjne przypisanie każdego wiersza w "Podgląd zawartości" do jego bezpośredniego TextBlocka wartości
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
                        previewBorder = FindPreviewCardContainer(tb);
                        if (previewBorder != null) break;
                    }
                }

                var searchScope = previewBorder != null ? new List<TextBlock>() : allTbs;
                if (previewBorder != null) FindVisualChildren(previewBorder, searchScope);

                var rows = new (string LabelPrefix, string FormattedVal)[]
                {
                    ("Pliki paczki", FormatBytes(stats.TotalBytes)),
                    ("Mapy", FormatBytes(stats.MapsBytes)),
                    ("Pluginy", FormatBytes(stats.PluginsBytes)),
                    ("Konfiguracja", FormatBytes(stats.ConfigBytes)),
                    ("Dane taktyczne", FormatBytes(stats.TacticalBytes)),
                    ("Pozostałe", FormatBytes(stats.OtherBytes))
                };

                bool IsKnownLabel(string text)
                {
                    if (string.IsNullOrWhiteSpace(text)) return true;
                    string tr = text.Trim();
                    if (tr.Contains("Podgląd zawartości", StringComparison.OrdinalIgnoreCase)) return true;
                    return rows.Any(r => tr.StartsWith(r.LabelPrefix, StringComparison.OrdinalIgnoreCase));
                }

                foreach (var row in rows)
                {
                    int labelIdx = searchScope.FindIndex(t => t.Text != null && t.Text.Trim().StartsWith(row.LabelPrefix, StringComparison.OrdinalIgnoreCase));
                    if (labelIdx < 0) continue;

                    TextBlock labelTb = searchScope[labelIdx];
                    TextBlock targetValTb = null;

                    // 1. W drzewie XAML wartość stoi bezpośrednio po etykiecie (searchScope[labelIdx + 1])
                    if (labelIdx + 1 < searchScope.Count && !IsKnownLabel(searchScope[labelIdx + 1].Text))
                    {
                        targetValTb = searchScope[labelIdx + 1];
                    }

                    // 2. Fallback po współrzędnej Y na ekranie (gdyby kolumny były zdefiniowane osobno w XAML)
                    if (targetValTb == null && previewBorder != null)
                    {
                        try
                        {
                            Point lblPt = labelTb.TranslatePoint(new Point(0, 0), previewBorder);
                            double bestDist = double.MaxValue;
                            foreach (var cand in searchScope)
                            {
                                if (cand == labelTb || IsKnownLabel(cand.Text)) continue;
                                Point cPt = cand.TranslatePoint(new Point(0, 0), previewBorder);
                                double dy = Math.Abs(cPt.Y - lblPt.Y);
                                if (dy <= 12 && cPt.X > lblPt.X + 10 && dy < bestDist)
                                {
                                    bestDist = dy;
                                    targetValTb = cand;
                                }
                            }
                        }
                        catch { }
                    }

                    if (targetValTb != null)
                    {
                        targetValTb.Text = row.FormattedVal;
                        targetValTb.ToolTip = BuildStatsTooltip(stats);
                    }
                }
            }
            catch { }
        }

        // =========================================================================
        // KARTA ATAK-CIV ORAZ WAŻNOŚĆ BEZTERMINOWA (0 = BEZTERMINOWO)
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
            _injectedPkgValidityBox = ValidityTextCtrl(m.PackageValidityHours, v => { m.PackageValidityHours = v; SyncValidityToNativeControls(win); });
            _injectedQrValidityBox = ValidityTextCtrl(m.QrValidityHours, v => { m.QrValidityHours = v; SyncValidityToNativeControls(win); });

            gValidity.Children.Add(FieldBox("Ważność paczki .ftak (godz., 0 = bezterminowo)", _injectedPkgValidityBox));
            gValidity.Children.Add(FieldBox("Ważność kodu QR (godz., 0 = bezterminowo)", _injectedQrValidityBox));
            lStack.Children.Add(gValidity);

            _indefiniteStatusBadge = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
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
                Tag = "INJECTED_VALIDITY"
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
                if (name.Contains("valid") || name.Contains("expir") || name.Contains("ttl") || toolTip.Contains("ważno") || toolTip.Contains("wygas"))
                {
                    result.Add(tb);
                }
            }
            return result;
        }

        private static void HookNativeValidityControls(Window win)
        {
            foreach (var tb in FindNativeValidityTextBoxes(win))
            {
                if ((bool)tb.GetValue(IsHookedProperty)) continue;
                tb.SetValue(IsHookedProperty, true);
                tb.ToolTip = "Wpisz 0, aby ustawić ważność bezterminową.";
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
                                if (_injectedPkgValidityBox != null) _injectedPkgValidityBox.Text = v.ToString();
                                if (_injectedQrValidityBox != null) _injectedQrValidityBox.Text = v.ToString();
                            }
                            tb.BorderBrush = (v == 0) ? BrushAccent : BrushInputBorder;
                            UpdateIndefiniteBadge();
                        }
                        finally { _syncingValidity = false; }
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
                foreach (var tb in FindNativeValidityTextBoxes(win))
                {
                    string name = (tb.Name ?? "").ToLowerInvariant();
                    int valToSet = name.Contains("qr") ? Current.QrValidityHours : Current.PackageValidityHours;
                    tb.Text = valToSet.ToString();
                    tb.BorderBrush = (valToSet == 0) ? BrushAccent : BrushInputBorder;
                }
                UpdateIndefiniteBadge();
            }
            catch { }
            finally { _syncingValidity = false; }
        }

        private static void UpdateIndefiniteBadge()
        {
            if (_indefiniteStatusBadge == null) return;
            if (Current.PackageValidityHours == 0 && Current.QrValidityHours == 0)
            {
                _indefiniteStatusBadge.Text = "✓ Ważność: BEZTERMINOWA (Paczka .ftak oraz kod QR nigdy nie wygasają)";
                _indefiniteStatusBadge.Foreground = BrushAccent;
            }
            else
            {
                _indefiniteStatusBadge.Text = $"Czas wygaśnięcia — Paczka .ftak: {Current.PackageValidityHours}h | Kod QR: {Current.QrValidityHours}h (0 = bezterminowo)";
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

                if (Current.PackageValidityHours != 0 && Current.QrValidityHours != 0 && boxesRestoredToZero.Count == 0) return;

                _syncingValidity = true;
                try { foreach (var tb in boxesRestoredToZero) tb.Text = "876000"; }
                finally { _syncingValidity = false; }

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
                    }
                    finally { _syncingValidity = false; }
                }), DispatcherPriority.ApplicationIdle);
            }
            catch { }
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
                        Current.HardwareModel = val;
                }
            }
            catch { }
        }

        internal static UIElement FieldBox(string label, UIElement ctrl)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 8, 6) };
            sp.Children.Add(new TextBlock { Text = label, Foreground = BrushMutedText, FontSize = 10.5, Margin = new Thickness(0, 0, 0, 3) });
            sp.Children.Add(ctrl);
            return sp;
        }

        internal static UIElement TextCtrl(string init, Action<string> onChg)
        {
            var tb = new TextBox { Text = init, Height = 28, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 0, 6, 0), FontSize = 11, Background = BrushInputBg, Foreground = Brushes.White, BorderBrush = BrushInputBorder, BorderThickness = new Thickness(1) };
            tb.TextChanged += (_, __) => onChg(tb.Text);
            return tb;
        }

        internal static UIElement IntCtrl(int init, Action<int> onChg) => TextCtrl(init.ToString(), s => { if (int.TryParse(s.Trim(), out int v)) onChg(v); });

        internal static UIElement ComboCtrl(string[] items, string init, Window win, Action<string> onChg)
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
            EnsureDefaultFactions();
            WriteConfigsToDirectory(tgt, _factions[Math.Clamp(_selectedFactionIndex, 0, _factions.Count - 1)]);
        }

        private static void WriteConfigsToDirectory(string tgt, FactionConfigProfile fac)
        {
            if (string.IsNullOrWhiteSpace(tgt) || fac == null) return;
            try
            {
                if (!Directory.Exists(tgt)) Directory.CreateDirectory(tgt);
                string jsonPath = Path.Combine(tgt, "tactical_config.json");

                JsonObject root = null;
                if (File.Exists(jsonPath))
                {
                    try { root = JsonNode.Parse(File.ReadAllText(jsonPath, Encoding.UTF8)) as JsonObject; } catch { }
                }
                root ??= new JsonObject();

                root["faction"] = fac.Name;
                root["factionColor"] = fac.ColorHex;
                root["packageValidityHours"] = Current.PackageValidityHours;
                root["qrValidityHours"] = Current.QrValidityHours;
                root["neverExpires"] = (Current.PackageValidityHours == 0);
                root["qrNeverExpires"] = (Current.QrValidityHours == 0);
                root["expiresAt"] = (Current.PackageValidityHours == 0)
                    ? DateTimeOffset.UtcNow.AddYears(100).ToString("o")
                    : DateTimeOffset.UtcNow.AddHours(Current.PackageValidityHours).ToString("o");

                // Sekcja ATAK
                JsonObject atak;
                if (root.TryGetPropertyValue("atak", out var atakNode) && atakNode is JsonObject existingAtak) atak = existingAtak;
                else { atak = new JsonObject(); root["atak"] = atak; }

                atak["locationReportingStrategy"] = Current.LocationReportingStrategy;
                atak["constantReportingRateReliable"] = Current.ConstantReportingRate
