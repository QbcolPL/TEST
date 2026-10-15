#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    public class FactionProfileModel
    {
        public string Name { get; set; } = "BLUFOR";
        public string ColorHex { get; set; } = "#2684FF";

        public string ChannelName { get; set; } = "BLUFOR-TAK";
        public string Psk { get; set; } = TacticalConfigModel.GenerateRandomBase64Psk();
        public string LoraRegion { get; set; } = "EU_868";
        public string LoraModemPreset { get; set; } = "SHORT_FAST";
        public string DeviceRole { get; set; } = "Auto";

        public string OtsHost { get; set; } = "ggzstak.duckdns.org";
        public int OtsSslPort { get; set; } = 8089;
        public int OtsEnrollmentPort { get; set; } = 8446;
        public bool EnableAutoEnrollment { get; set; } = true;
        public string EnrollmentUsername { get; set; } = "blufor_op";
        public string EnrollmentPassword { get; set; } = "";
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

        private static bool _classHandlerRegistered = false;
        private static Style _darkComboBoxStyle = null;
        private static Style _darkComboBoxItemStyle = null;
        private static Button _injectedRefreshButton = null;
        private static TextBlock _liveCategoryBreakdownText = null;
        private static TextBlock _factionStatusLabel = null;
        private static FileSystemWatcher _sourceWatcher = null;
        private static string _watchedDir = "";

        private static readonly List<FactionProfileModel> _factions = new List<FactionProfileModel>();
        private static int _selectedFactionIndex = 0;
        private static readonly List<WrapPanel> _factionTabHeadersPanels = new List<WrapPanel>();
        private static readonly List<Border> _factionTabContentHosts = new List<Border>();

        // Automatyczna inicjalizacja modułu przy starcie aplikacji WPF (gwarantuje 100% widoczności zakładek)
        [ModuleInitializer]
        internal static void RegisterGlobalWindowHook()
        {
            if (_classHandlerRegistered) return;
            _classHandlerRegistered = true;
            try
            {
                EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, args) =>
                {
                    if (sender is Window w && w.GetType().Name.Contains("MainWindow", StringComparison.OrdinalIgnoreCase))
                    {
                        AttachToMainWindow(w);
                    }
                }));
            }
            catch { }
        }

        public static List<FactionProfileModel> GetFactionsList()
        {
            EnsureDefaultFactions();
            return _factions;
        }

        public static List<(string Name, string Channel, string ColorHex, string Psk)> GetConfiguredFactions()
        {
            EnsureDefaultFactions();
            return _factions.Select(f => (f.Name, f.ChannelName, f.ColorHex, f.Psk)).ToList();
        }

        private static void EnsureDefaultFactions()
        {
            if (_factions.Count > 0) return;
            _factions.Add(new FactionProfileModel
            {
                Name = "BLUFOR",
                ColorHex = "#2684FF",
                ChannelName = "BLUFOR-TAK",
                Psk = TacticalConfigModel.GenerateRandomBase64Psk(),
                EnrollmentUsername = "blufor_op"
            });
            _factions.Add(new FactionProfileModel
            {
                Name = "OPFOR",
                ColorHex = "#EB4034",
                ChannelName = "OPFOR-TAK",
                Psk = TacticalConfigModel.GenerateRandomBase64Psk(),
                EnrollmentUsername = "opfor_op"
            });
            _factions.Add(new FactionProfileModel
            {
                Name = "ORGA",
                ColorHex = "#F5B82E",
                ChannelName = "ORGA-HQ",
                Psk = TacticalConfigModel.GenerateRandomBase64Psk(),
                DeviceRole = "TAK",
                EnrollmentUsername = "orga_hq"
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
                if (child is T t && !list.Contains(t)) list.Add(t);
                FindVisualChildren(child, list);
            }
        }

        internal static void FindLogicalAndVisualChildren<T>(DependencyObject depObj, List<T> list, HashSet<DependencyObject> visited = null) where T : DependencyObject
        {
            if (depObj == null) return;
            visited ??= new HashSet<DependencyObject>();
            if (!visited.Add(depObj)) return;

            if (depObj is T match && !list.Contains(match)) list.Add(match);

            try
            {
                int vCount = VisualTreeHelper.GetChildrenCount(depObj);
                for (int i = 0; i < vCount; i++)
                {
                    FindLogicalAndVisualChildren(VisualTreeHelper.GetChild(depObj, i), list, visited);
                }
            }
            catch { }

            try
            {
                foreach (var child in LogicalTreeHelper.GetChildren(depObj))
                {
                    if (child is DependencyObject dChild)
                        FindLogicalAndVisualChildren(dChild, list, visited);
                }
            }
            catch { }
        }

        private static Border GetOuterCardBorder(DependencyObject startElement, int minTextBlocks = 4)
        {
            Border bestBorder = null;
            DependencyObject cur = startElement;
            while (cur != null)
            {
                if (cur is Border b)
                {
                    var innerTbs = new List<TextBlock>();
                    FindLogicalAndVisualChildren(b, innerTbs);
                    if (innerTbs.Count >= minTextBlocks) return b;
                    if (bestBorder == null && b.Background != null) bestBorder = b;
                }
                cur = VisualTreeHelper.GetParent(cur) ?? LogicalTreeHelper.GetParent(cur);
            }
            return bestBorder;
        }

        private static Border GetPreviewCardBorder(Window win)
        {
            var allBorders = new List<Border>();
            FindLogicalAndVisualChildren(win, allBorders);
            Border bestMatch = null;
            int smallestCount = int.MaxValue;

            foreach (var b in allBorders)
            {
                var tbs = new List<TextBlock>();
                FindLogicalAndVisualChildren(b, tbs);
                if (tbs.Count < 6) continue;

                bool hasHeader = tbs.Any(t => t.Text != null && (t.Text.Contains("Podgląd zawartości", StringComparison.OrdinalIgnoreCase) || t.Text.Contains("Content preview", StringComparison.OrdinalIgnoreCase)));
                bool hasMaps = tbs.Any(t => t.Text != null && (t.Text.Trim().StartsWith("Mapy", StringComparison.OrdinalIgnoreCase) || t.Text.Trim().StartsWith("Maps", StringComparison.OrdinalIgnoreCase)));

                if ((hasHeader || hasMaps) && tbs.Count < smallestCount)
                {
                    smallestCount = tbs.Count;
                    bestMatch = b;
                }
            }
            return bestMatch;
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
                    // 1. Główny pasek 3 zakładek na samej górze okna (Paczka .ftak | Generator Stron Konfliktu | Generator Map Taktycznych)
                    TacticalMapGeneratorView.AttachMasterNavigationShell(win);

                    var combos = new List<ComboBox>();
                    FindLogicalAndVisualChildren(win, combos);
                    foreach (var c in combos) ApplyUnifiedComboBoxTheme(c, win);

                    var tbs = new List<TextBlock>();
                    FindLogicalAndVisualChildren(win, tbs);

                    bool factionsInjectedInline = false;

                    foreach (var tb in tbs)
                    {
                        if (tb.Text != null && (tb.Text.Contains("Konfiguracja Meshtastic", StringComparison.OrdinalIgnoreCase) || tb.Text.Contains("Meshtastic", StringComparison.OrdinalIgnoreCase)))
                        {
                            Border cardBorder = GetOuterCardBorder(tb, 3);
                            if (cardBorder != null)
                            {
                                var parentPanel = (VisualTreeHelper.GetParent(cardBorder) ?? LogicalTreeHelper.GetParent(cardBorder)) as Panel;
                                if (parentPanel != null && !HasInjectedTag(parentPanel, "INJECTED_ATAK"))
                                {
                                    int idx = parentPanel.Children.IndexOf(cardBorder);
                                    if (idx >= 0)
                                    {
                                        parentPanel.Children.Insert(idx, BuildAtakCard(win));
                                        parentPanel.Children.Insert(idx + 1, BuildTabbedFactionsCard(win));
                                        factionsInjectedInline = true;
                                    }
                                }
                            }
                        }
                    }

                    // Jeżeli użytkownik jest w Trybie Prostym (gdzie nie ma karty Meshtastic), wstrzyknij kartę Stron Konfliktu do głównego ScrollViewera
                    if (!factionsInjectedInline)
                    {
                        var scrolls = new List<ScrollViewer>();
                        FindLogicalAndVisualChildren(win, scrolls);
                        foreach (var sv in scrolls)
                        {
                            if (sv.Tag?.ToString() == "MAP_LEFT_SCROLL") continue;
                            if (sv.Content is Panel mainStack && !HasInjectedTag(mainStack, "INJECTED_FACTIONS") && !HasInjectedTag(mainStack, "INJECTED_ATAK"))
                            {
                                mainStack.Children.Add(BuildAtakCard(win));
                                mainStack.Children.Add(BuildTabbedFactionsCard(win));
                                break;
                            }
                        }
                    }

                    // Podpięcie rozbicia 6 kategorii pod kartą "Podgląd zawartości"
                    Border previewBorder = GetPreviewCardBorder(win);
                    if (previewBorder != null)
                    {
                        var previewParent = (VisualTreeHelper.GetParent(previewBorder) ?? LogicalTreeHelper.GetParent(previewBorder)) as Panel;
                        if (previewParent != null && !HasInjectedTag(previewParent, "INJECTED_BTN"))
                        {
                            var breakdownCard = new Border
                            {
                                Background = BrushInputBg,
                                BorderBrush = BrushAccent,
                                BorderThickness = new Thickness(1),
                                CornerRadius = new CornerRadius(6),
                                Padding = new Thickness(10, 8, 10, 8),
                                Margin = new Thickness(0, 6, 0, 6),
                                Tag = "INJECTED_BREAKDOWN_BOX"
                            };
                            _liveCategoryBreakdownText = new TextBlock
                            {
                                Text = "Analiza kategorii source...",
                                Foreground = Brushes.White,
                                FontSize = 11,
                                TextWrapping = TextWrapping.Wrap
                            };
                            breakdownCard.Child = _liveCategoryBreakdownText;

                            var btnRefresh = new Button
                            {
                                Content = "Odśwież / Skalkuluj zawartość (6 kategorii source)",
                                Height = 32,
                                Margin = new Thickness(0, 4, 0, 6),
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

                            int idx = previewParent.Children.IndexOf(previewBorder);
                            previewParent.Children.Insert(idx + 1, breakdownCard);
                            previewParent.Children.Insert(idx + 2, btnRefresh);
                        }
                    }
                }
                catch { }
            }

            InitializeEnhancements();

            if (!(bool)win.GetValue(IsHookedProperty))
            {
                win.SetValue(IsHookedProperty, true);
                win.Loaded += async (s, e) =>
                {
                    InitializeEnhancements();
                    var res = await ForceSizeUpdateAsync(win);
                    if (_injectedRefreshButton != null)
                    {
                        _injectedRefreshButton.Content = $"Odśwież / Skalkuluj (Plików: {res.FileCount} | {FormatBytes(res.TotalBytes)})";
                    }
                };
            }
        }

        public static async Task RefreshSourceSizeFromExternalAsync(Window win)
        {
            var res = await ForceSizeUpdateAsync(win);
            if (_injectedRefreshButton != null)
            {
                _injectedRefreshButton.Content = $"Odśwież / Skalkuluj (Plików: {res.FileCount} | {FormatBytes(res.TotalBytes)})";
            }
        }

        // =========================================================================
        // ZAKŁADKOWY GENERATOR STRON KONFLIKTU (DOSTĘPNY ZARÓWNO W GÓRNEJ ZAKŁADCE, JAK I W KONFIGURATORZE)
        // =========================================================================
        internal static UIElement BuildTabbedFactionsCard(Window win)
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

            var titleDock = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var btnJumpMap = new Button
            {
                Content = "🗺️ Przejdź do Generatora Map Taktycznych (1:1 HTML Suite)",
                Height = 28,
                Padding = new Thickness(12, 0, 12, 0),
                Background = BrushItemBg,
                Foreground = BrushAccent,
                BorderBrush = BrushAccent,
                BorderThickness = new Thickness(1),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            DockPanel.SetDock(btnJumpMap, Dock.Right);
            btnJumpMap.Click += (s, e) => TacticalMapGeneratorView.ShowFullScreenMapTab(win);
            titleDock.Children.Add(btnJumpMap);

            titleDock.Children.Add(new TextBlock
            {
                Text = "⚔️ Generator Stron Konfliktu (Karty Stron: BLUFOR / OPFOR / ORGA)",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center
            });
            stack.Children.Add(titleDock);

            stack.Children.Add(new TextBlock
            {
                Text = "Każda strona posiada własną zakładkę z kolorem RGB, szyfrowanym kanałem Meshtastic (PSK), ustawieniami OpenTAKServer (Enrollment) oraz odizolowanym folderem KML.",
                Foreground = BrushMutedText,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });

            var headersPanel = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var contentHost = new Border
            {
                Background = BrushItemBg,
                BorderBrush = BrushTileBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 12)
            };

            _factionTabHeadersPanels.Add(headersPanel);
            _factionTabContentHosts.Add(contentHost);

            stack.Children.Add(headersPanel);
            stack.Children.Add(contentHost);

            RebuildAllFactionTabsUI(win);

            var actionButtonsGrid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 2, 0, 6) };

            var btnInitFolders = new Button
            {
                Content = "📁 Przygotuj podfoldery KML stron",
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
                Content = "🖨️ Eksportuj plakat A4 strony (PNG)",
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

            actionButtonsGrid.Children.Add(btnInitFolders);
            actionButtonsGrid.Children.Add(btnExportPoster);
            actionButtonsGrid.Children.Add(btnBuildAllFactions);
            stack.Children.Add(actionButtonsGrid);

            _factionStatusLabel = new TextBlock
            {
                Text = "Gotowy. Wybierz zakładkę strony powyżej, skonfiguruj kanał szyfrowany Meshtastic oraz OpenTAKServer Enrollment, a następnie zbuduj paczki.",
                Foreground = BrushMutedText,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            };
            stack.Children.Add(_factionStatusLabel);

            btnInitFolders.Click += async (s, e) =>
            {
                string srcDir = GetPrimarySourceDirectory(win);
                var sides = _factions.Select(f => (SanitizeFileName(f.Name).ToLowerInvariant(), f.ColorHex)).ToList();
                await Task.Run(() => EnsureFactionFoldersInitialized(srcDir, sides));
                await ForceSizeUpdateAsync(win);
                if (_factionStatusLabel != null)
                {
                    _factionStatusLabel.Text = $"✓ Utworzono podfoldery KML dla {_factions.Count} stron w: {Path.Combine(srcDir, "factions")}";
                    _factionStatusLabel.Foreground = BrushAccent;
                }
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
                btnBuildAllFactions.Content = "Budowanie wszystkich stron...";
                await BuildDynamicFactionsAsync(win);
                btnBuildAllFactions.Content = origText;
                btnBuildAllFactions.IsEnabled = true;
            };

            card.Child = stack;
            return card;
        }

        private static void RebuildAllFactionTabsUI(Window win)
        {
            EnsureDefaultFactions();
            _selectedFactionIndex = Math.Clamp(_selectedFactionIndex, 0, _factions.Count - 1);
            SyncActiveFactionToDefaultModel();

            for (int pIdx = 0; pIdx < _factionTabHeadersPanels.Count; pIdx++)
            {
                var headersPanel = _factionTabHeadersPanels[pIdx];
                var contentHost = _factionTabContentHosts[pIdx];
                if (headersPanel == null || contentHost == null) continue;

                headersPanel.Children.Clear();

                for (int i = 0; i < _factions.Count; i++)
                {
                    int idx = i;
                    var fac = _factions[i];
                    bool isSelected = (idx == _selectedFactionIndex);

                    var dot = new Border
                    {
                        Width = 12,
                        Height = 12,
                        CornerRadius = new CornerRadius(6),
                        Background = BrushFromHex(fac.ColorHex, Color.FromRgb(38, 132, 255)),
                        BorderBrush = Brushes.White,
                        BorderThickness = new Thickness(1),
                        Margin = new Thickness(0, 0, 7, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    var lbl = new TextBlock
                    {
                        Text = string.IsNullOrWhiteSpace(fac.Name) ? $"STRONA-{idx + 1}" : fac.Name,
                        Foreground = isSelected ? BrushAccent : Brushes.White,
                        FontWeight = FontWeights.Bold,
                        FontSize = 12,
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    var tabStack = new StackPanel { Orientation = Orientation.Horizontal };
                    tabStack.Children.Add(dot);
                    tabStack.Children.Add(lbl);

                    var tabBtn = new Button
                    {
                        Content = tabStack,
                        Height = 32,
                        Padding = new Thickness(14, 0, 14, 0),
                        Margin = new Thickness(0, 0, 6, 4),
                        Background = isSelected ? BrushInputBg : BrushItemBg,
                        BorderBrush = isSelected ? BrushAccent : BrushTileBorder,
                        BorderThickness = new Thickness(isSelected ? 2 : 1),
                        Cursor = Cursors.Hand
                    };
                    tabBtn.Click += (_, __) =>
                    {
                        _selectedFactionIndex = idx;
                        RebuildAllFactionTabsUI(win);
                    };
                    headersPanel.Children.Add(tabBtn);
                }

                string[] defaultPalette = { "#2ECC71", "#9B59B6", "#E67E22", "#1ABC9C", "#E84393", "#00CEC9" };
                var btnAddFaction = new Button
                {
                    Content = "+ Dodaj stronę",
                    Height = 32,
                    Padding = new Thickness(12, 0, 12, 0),
                    Margin = new Thickness(2, 0, 6, 4),
                    Background = BrushInputBg,
                    Foreground = BrushAccent,
                    BorderBrush = BrushAccent,
                    BorderThickness = new Thickness(1),
                    FontSize = 11.5,
                    FontWeight = FontWeights.Bold,
                    Cursor = Cursors.Hand
                };
                btnAddFaction.Click += (_, __) =>
                {
                    int nextNum = _factions.Count + 1;
                    string hex = defaultPalette[(nextNum - 1) % defaultPalette.Length];
                    _factions.Add(new FactionProfileModel
                    {
                        Name = $"STRONA-{nextNum}",
                        ColorHex = hex,
                        ChannelName = $"TAK-CH{nextNum}",
                        Psk = TacticalConfigModel.GenerateRandomBase64Psk(),
                        EnrollmentUsername = $"strona{nextNum}"
                    });
                    _selectedFactionIndex = _factions.Count - 1;
                    RebuildAllFactionTabsUI(win);
                };
                headersPanel.Children.Add(btnAddFaction);

                var btnRemoveFaction = new Button
                {
                    Content = "− Usuń stronę",
                    Height = 32,
                    Padding = new Thickness(12, 0, 12, 0),
                    Margin = new Thickness(0, 0, 0, 4),
                    Background = BrushInputBg,
                    Foreground = Brushes.White,
                    BorderBrush = BrushTileBorder,
                    BorderThickness = new Thickness(1),
                    FontSize = 11.5,
                    FontWeight = FontWeights.Bold,
                    Cursor = Cursors.Hand
                };
                btnRemoveFaction.Click += (_, __) =>
                {
                    if (_factions.Count <= 1) return;
                    _factions.RemoveAt(_selectedFactionIndex);
                    _selectedFactionIndex = Math.Clamp(_selectedFactionIndex, 0, _factions.Count - 1);
                    RebuildAllFactionTabsUI(win);
                };
                headersPanel.Children.Add(btnRemoveFaction);

                contentHost.Child = BuildSingleFactionTabContent(win, _factions[_selectedFactionIndex]);
            }
        }

        private static UIElement BuildSingleFactionTabContent(Window win, FactionProfileModel fac)
        {
            var root = new StackPanel();

            root.Children.Add(new TextBlock
            {
                Text = $"1. Identyfikacja Strony, Kolor Taktyczny RGB oraz Warstwy KML ({fac.Name})",
                Foreground = BrushAccent,
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var topRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(185) });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });

            var nameBox = (TextBox)TextCtrl(fac.Name, val =>
            {
                fac.Name = val.Trim();
                SyncActiveFactionToDefaultModel();
            });
            nameBox.FontWeight = FontWeights.Bold;
            nameBox.LostFocus += (_, __) => RebuildAllFactionTabsUI(win);
            var nameField = FieldBox("Nazwa strony / frakcji (np. BLUFOR, OPFOR, ORGA):", nameBox);
            Grid.SetColumn(nameField, 0);
            topRow.Children.Add(nameField);

            var colorSwatch = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(4),
                Background = BrushFromHex(fac.ColorHex, Color.FromRgb(38, 132, 255)),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var colorLabel = new TextBlock
            {
                Text = $"Paleta RGB ({fac.ColorHex.ToUpperInvariant()})",
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            var colorStack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            colorStack.Children.Add(colorSwatch);
            colorStack.Children.Add(colorLabel);

            var colorBtn = new Button
            {
                Content = colorStack,
                Height = 28,
                Background = BrushInputBg,
                BorderBrush = BrushInputBorder,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            colorBtn.Click += (_, __) =>
            {
                string picked = ShowRgbColorPickerDialog(win, fac.Name, fac.ColorHex);
                if (!string.IsNullOrEmpty(picked))
                {
                    fac.ColorHex = picked;
                    RebuildAllFactionTabsUI(win);
                }
            };
            var colorField = FieldBox("Kolor taktyczny strony (RGB):", colorBtn);
            Grid.SetColumn(colorField, 1);
            topRow.Children.Add(colorField);

            var btnOpenFactionKml = new Button
            {
                Content = $"📁 source/factions/{SanitizeFileName(fac.Name).ToLowerInvariant()}",
                Height = 28,
                Background = BrushInputBg,
                Foreground = Brushes.White,
                BorderBrush = BrushInputBorder,
                BorderThickness = new Thickness(1),
                FontSize = 10.5,
                Cursor = Cursors.Hand
            };
            btnOpenFactionKml.Click += async (_, __) =>
            {
                string srcDir = GetPrimarySourceDirectory(win);
                string sideLower = SanitizeFileName(fac.Name).ToLowerInvariant();
                await Task.Run(() => EnsureFactionFoldersInitialized(srcDir, new List<(string, string)> { (sideLower, fac.ColorHex) }));
                TryOpenFolderInExplorer(Path.Combine(srcDir, "factions", sideLower));
            };
            var kmlField = FieldBox("Dedykowane nakładki KML strony:", btnOpenFactionKml);
            Grid.SetColumn(kmlField, 2);
            topRow.Children.Add(kmlField);
            root.Children.Add(topRow);

            root.Children.Add(new TextBlock
            {
                Text = $"2. Meshtastic — Kanał Szyfrowany LoRa ({fac.Name})",
                Foreground = BrushAccent,
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 8)
            });

            var meshRow1 = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 4) };
            meshRow1.Children.Add(FieldBox("Nazwa kanału szyfrowanego LoRa:", TextCtrl(fac.ChannelName, v => { fac.ChannelName = v.Trim(); SyncActiveFactionToDefaultModel(); })));
            meshRow1.Children.Add(FieldBox("Region LoRa:", ComboCtrl(new[] { "EU_868", "US_915", "EU_433", "ANZ_915", "RU_864", "KR_920" }, fac.LoraRegion, win, v => { fac.LoraRegion = v; Current.LoraRegion = v; })));
            meshRow1.Children.Add(FieldBox("Modem Preset:", ComboCtrl(new[] { "SHORT_FAST", "LONG_FAST", "MEDIUM_FAST", "MEDIUM_SLOW", "LONG_SLOW" }, fac.LoraModemPreset, win, v => { fac.LoraModemPreset = v; Current.LoraModemPreset = v; })));
            meshRow1.Children.Add(FieldBox("Rola urządzenia Meshtastic:", ComboCtrl(new[] { "Auto", "TAK", "TAK_Tracker", "Client", "Router" }, fac.DeviceRole, win, v => { fac.DeviceRole = v; Current.DeviceRole = v; })));
            root.Children.Add(meshRow1);

            var pskGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            pskGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pskGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });

            var pskBox = (TextBox)TextCtrl(fac.Psk, v => { fac.Psk = v.Trim(); SyncActiveFactionToDefaultModel(); });
            pskBox.FontFamily = new FontFamily("Consolas");
            var pskField = FieldBox("Klucz szyfrujący PSK kanału Meshtastic (AES-256 Base64 — pełna szerokość):", pskBox);
            Grid.SetColumn(pskField, 0);
            pskGrid.Children.Add(pskField);

            var btnRollPsk = new Button
            {
                Content = "🎲 Losuj nowy klucz PSK",
                Height = 28,
                Margin = new Thickness(0, 14, 0, 6),
                Background = BrushInputBg,
                Foreground = BrushAccent,
                BorderBrush = BrushAccent,
                BorderThickness = new Thickness(1),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            btnRollPsk.Click += (_, __) =>
            {
                fac.Psk = TacticalConfigModel.GenerateRandomBase64Psk();
                pskBox.Text = fac.Psk;
                SyncActiveFactionToDefaultModel();
            };
            Grid.SetColumn(btnRollPsk, 1);
            pskGrid.Children.Add(btnRollPsk);
            root.Children.Add(pskGrid);

            root.Children.Add(new TextBlock
            {
                Text = $"3. OpenTAKServer (OTS) & Certyfikaty Enrollment ({fac.Name})",
                Foreground = BrushAccent,
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 8)
            });

            var otsRow1 = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            otsRow1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            otsRow1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            otsRow1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });

            var hostField = FieldBox("Adres serwera OpenTAKServer (Host / IP / Domena):", TextCtrl(fac.OtsHost, v => fac.OtsHost = v.Trim()));
            var sslPortField = FieldBox("Port SSL CoT:", IntCtrl(fac.OtsSslPort, v => fac.OtsSslPort = v));
            var enrollPortField = FieldBox("Port Enrollment (HTTPS):", IntCtrl(fac.OtsEnrollmentPort, v => fac.OtsEnrollmentPort = v));
            Grid.SetColumn(hostField, 0);
            Grid.SetColumn(sslPortField, 1);
            Grid.SetColumn(enrollPortField, 2);
            otsRow1.Children.Add(hostField);
            otsRow1.Children.Add(sslPortField);
            otsRow1.Children.Add(enrollPortField);
            root.Children.Add(otsRow1);

            var otsRow2 = new Grid();
            otsRow2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            otsRow2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            otsRow2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var swEnroll = TileSwitch("Włącz Auto-Enrollment OTS", fac.EnableAutoEnrollment, v => fac.EnableAutoEnrollment = v);
            swEnroll.Margin = new Thickness(0, 10, 8, 6);
            var userField = FieldBox("Użytkownik / Grupa Enrollment (OTS):", TextCtrl(fac.EnrollmentUsername, v => fac.EnrollmentUsername = v.Trim()));
            var passField = FieldBox("Hasło Enrollment / Hasło certyfikatu (opcjonalnie):", TextCtrl(fac.EnrollmentPassword, v => fac.EnrollmentPassword = v));
            Grid.SetColumn(swEnroll, 0);
            Grid.SetColumn(userField, 1);
            Grid.SetColumn(passField, 2);
            otsRow2.Children.Add(swEnroll);
            otsRow2.Children.Add(userField);
            otsRow2.Children.Add(passField);
            root.Children.Add(otsRow2);

            return root;
        }

        internal static string ShowRgbColorPickerDialog(Window owner, string titleLabel, string initialHex)
        {
            Color initColor = Color.FromRgb(38, 132, 255);
            try { initColor = (Color)ColorConverter.ConvertFromString(initialHex); } catch { }

            byte r = initColor.R, g = initColor.G, b = initColor.B;
            string selectedHex = $"#{r:X2}{g:X2}{b:X2}";
            bool confirmed = false;

            var dlg = new Window
            {
                Title = $"Paleta RGB — {titleLabel}",
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
                Text = $"{titleLabel}   ({selectedHex})",
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
            string[] presets = { "#2684FF", "#EB4034", "#F5B82E", "#2ECC71", "#9B59B6", "#1FE0C5", "#E67E22", "#E84393", "#00CEC9", "#FFFFFF" };

            Slider sR = null, sG = null, sB = null;
            TextBox hexBox = null;
            bool updating = false;

            void UpdateFromRgb()
            {
                if (updating) return;
                updating = true;
                selectedHex = $"#{r:X2}{g:X2}{b:X2}";
                previewBorder.Background = CreateFrozenBrush(r, g, b);
                previewText.Text = $"{titleLabel}   ({selectedHex})";
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

        private static void SyncActiveFactionToDefaultModel()
        {
            EnsureDefaultFactions();
            var active = _factions[Math.Clamp(_selectedFactionIndex, 0, _factions.Count - 1)];
            Current.ActiveFaction = string.IsNullOrWhiteSpace(active.Name) ? "BLUFOR" : active.Name.Trim();
            Current.DefaultChannelName = string.IsNullOrWhiteSpace(active.ChannelName) ? "BLUFOR-TAK" : active.ChannelName.Trim();
            Current.DefaultPsk = string.IsNullOrWhiteSpace(active.Psk) ? Current.DefaultPsk : active.Psk.Trim();
            Current.DefaultFactionColorHex = string.IsNullOrWhiteSpace(active.ColorHex) ? "#2684FF" : active.ColorHex;
        }

        private static async Task BuildDynamicFactionsAsync(Window win)
        {
            try
            {
                EnsureDefaultFactions();
                string sourceDir = GetPrimarySourceDirectory(win);
                string eventName = GetEventNameFromWindow(win);
                string parentDir = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(sourceDir)) ?? AppDomain.CurrentDomain.BaseDirectory;
                string outDir = Path.Combine(parentDir, "release_factions");

                if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

                var allSides = _factions.Select(s => (SanitizeFileName(s.Name).ToLowerInvariant(), s.ColorHex)).ToList();

                foreach (var fac in _factions)
                {
                    string cleanName = SanitizeFileName(fac.Name);
                    string ftakPath = Path.Combine(outDir, $"{eventName}_{cleanName}.ftak");
                    string sha256 = await Task.Run(() =>
                    {
                        EnsureFactionFoldersInitialized(sourceDir, allSides);
                        WriteConfigsToDirectory(sourceDir, fac);
                        BuildFilteredFactionFtakArchive(sourceDir, ftakPath, fac, eventName);
                        return ComputeFileSha256(ftakPath);
                    });

                    string qrPayload = $"ftak://provision?event={Uri.EscapeDataString(eventName)}&faction={Uri.EscapeDataString(cleanName)}&color={Uri.EscapeDataString(fac.ColorHex)}&channel={Uri.EscapeDataString(fac.ChannelName)}&ots={Uri.EscapeDataString(fac.OtsHost)}&region={fac.LoraRegion}&preset={fac.LoraModemPreset}&sha256={sha256.Substring(0, 16)}";
                    string posterPath = Path.Combine(outDir, $"Plakat_Safezone_A4_{eventName}_{cleanName}.png");
                    RenderSafezoneA4PosterPng(win, posterPath, eventName, fac, qrPayload, sha256);
                }

                var activeFac = _factions[Math.Clamp(_selectedFactionIndex, 0, _factions.Count - 1)];
                await Task.Run(() => WriteConfigsToDirectory(sourceDir, activeFac));
                await ForceSizeUpdateAsync(win);

                if (_factionStatusLabel != null)
                {
                    _factionStatusLabel.Text = $"✓ Zbudowano {_factions.Count} paczek (.ftak) oraz {_factions.Count} plakatów A4 (PNG) w: {outDir}";
                    _factionStatusLabel.Foreground = BrushAccent;
                }

                TryOpenFolderInExplorer(outDir);
            }
            catch { }
        }

        private static async Task ExportSafezonePosterForCurrentAsync(Window win)
        {
            try
            {
                EnsureDefaultFactions();
                var fac = _factions[Math.Clamp(_selectedFactionIndex, 0, _factions.Count - 1)];

                string sourceDir = GetPrimarySourceDirectory(win);
                string eventName = GetEventNameFromWindow(win);
                string parentDir = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(sourceDir)) ?? AppDomain.CurrentDomain.BaseDirectory;
                string outDir = Path.Combine(parentDir, "release_factions");
                if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

                string cleanName = SanitizeFileName(fac.Name);
                string ftakPath = Path.Combine(outDir, $"{eventName}_{cleanName}.ftak");
                string sha256 = await Task.Run(() =>
                {
                    WriteConfigsToDirectory(sourceDir, fac);
                    BuildFilteredFactionFtakArchive(sourceDir, ftakPath, fac, eventName);
                    return ComputeFileSha256(ftakPath);
                });

                string qrPayload = $"ftak://provision?event={Uri.EscapeDataString(eventName)}&faction={Uri.EscapeDataString(cleanName)}&channel={Uri.EscapeDataString(fac.ChannelName)}&ots={Uri.EscapeDataString(fac.OtsHost)}&region={fac.LoraRegion}&preset={fac.LoraModemPreset}&sha256={sha256.Substring(0, 16)}";
                string posterPath = Path.Combine(outDir, $"Plakat_Safezone_A4_{eventName}_{cleanName}.png");
                RenderSafezoneA4PosterPng(win, posterPath, eventName, fac, qrPayload, sha256);

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
                FindLogicalAndVisualChildren(win, tbs);
                foreach (var tb in tbs)
                {
                    if (tb.Text != null && (tb.Text.Contains("Nazwa wydarzenia") || tb.Text.Contains("Event Name") || tb.Text.Contains("Nazwa paczki") || tb.Text.Contains("Package ID")))
                    {
                        var parent = VisualTreeHelper.GetParent(tb) ?? LogicalTreeHelper.GetParent(tb);
                        if (parent != null)
                        {
                            var siblings = new List<TextBox>();
                            FindLogicalAndVisualChildren(parent, siblings);
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

        internal static string HexToKmlColor(string hex, byte alpha = 255)
        {
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(hex);
                return $"{alpha:X2}{c.B:X2}{c.G:X2}{c.R:X2}";
            }
            catch { return $"{alpha:X2}ff8426"; }
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

        private static void BuildFilteredFactionFtakArchive(string sourceDir, string destinationFtakPath, FactionProfileModel fac, string eventName)
        {
            if (File.Exists(destinationFtakPath)) File.Delete(destinationFtakPath);

            string cleanName = SanitizeFileName(fac.Name);
            using var fs = new FileStream(destinationFtakPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Create, false, Encoding.UTF8);

            if (Directory.Exists(sourceDir))
            {
                var allFiles = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
                string targetFactionPrefix = $"factions/{cleanName.ToLowerInvariant()}/";

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

                    if (relLower.StartsWith("factions/") && !cleanName.Equals("ALL", StringComparison.OrdinalIgnoreCase) && !cleanName.Equals("ORGA", StringComparison.OrdinalIgnoreCase))
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
                ["packageId"] = $"{eventName.ToLowerInvariant()}-{cleanName.ToLowerInvariant()}",
                ["packageName"] = $"{eventName} ({fac.Name})",
                ["faction"] = fac.Name,
                ["factionColor"] = fac.ColorHex,
                ["channelName"] = fac.ChannelName,
                ["psk"] = fac.Psk,
                ["loraRegion"] = fac.LoraRegion,
                ["loraModemPreset"] = fac.LoraModemPreset,
                ["otsHost"] = fac.OtsHost,
                ["otsSslPort"] = fac.OtsSslPort,
                ["otsEnrollmentPort"] = fac.OtsEnrollmentPort,
                ["enableAutoEnrollment"] = fac.EnableAutoEnrollment,
                ["enrollmentUsername"] = fac.EnrollmentUsername,
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

        private static void RenderSafezoneA4PosterPng(Window win, string outputPath, string eventName, FactionProfileModel fac, string qrPayload, string sha256Hex)
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
                DrawLeftText(dc, "Sprawdź status końcowy:  ✓ ATAK gotowy   ✓ Meshtastic gotowy   ✓ Serwer dostępny.", 19, BrushAccent, 110, stepsY + 288, true);

                double techY = 1310;
                dc.DrawRoundedRectangle(BrushInputBg, new Pen(BrushInputBorder, 2), new Rect(70, techY, width - 140, 360), 12, 12);
                DrawCenteredText(dc, "PARAMETRY TECHNICZNE PACZKI, SERWERA OTS I RADIA LORA", 22, Brushes.White, width / 2.0, techY + 22, true);

                string validityText = Current.PackageValidityHours == 0 ? "BEZTERMINOWA (0)" : $"{Current.PackageValidityHours} godz.";
                DrawLeftText(dc, $"• Strona konfliktu:   {fac.Name}   |   Kolor: {fac.ColorHex}   |   Kanał LoRa: {fac.ChannelName}", 20, Brushes.White, 110, techY + 75, true);
                DrawLeftText(dc, $"• Pasmo / Modem:      Region {fac.LoraRegion}   |   Preset {fac.LoraModemPreset}   |   Rola: {fac.DeviceRole}", 20, Brushes.White, 110, techY + 118, false);
                DrawLeftText(dc, $"• Serwer OTS:         {fac.OtsHost}:{fac.OtsSslPort} (Enrollment: {fac.OtsEnrollmentPort} / User: {fac.EnrollmentUsername})", 20, Brushes.White, 110, techY + 161, false);
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
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                foreach (var f in win.GetType().GetFields(flags))
                {
                    try { if (f.GetValue(win) is string s) AddCandidate(s); } catch { }
                }
                var textBoxes = new List<TextBox>();
                FindLogicalAndVisualChildren(win, textBoxes);
                foreach (var tbx in textBoxes) AddCandidate(tbx.Text);
            }

            if (list.Count == 0)
            {
                list.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "workspace", "source"));
            }
            return list;
        }

        // =========================================================================
        // KALKULATOR ROZMIARU Z ROZBICIEM NA 6 KATEGORII
        // =========================================================================
        private class SourceStats
        {
            public string SourcePath = "";
            public int FileCount;
            public int MapsCount;
            public int PluginsCount;
            public int ConfigCount;
            public int TacticalCount;
            public int OtherCount;
            public long TotalBytes;
            public long MapsBytes;
            public long PluginsBytes;
            public long ConfigBytes;
            public long TacticalBytes;
            public long OtherBytes;
        }

        private static void EnsureWatcherForDirectory(Window win, string sourceDir)
        {
            if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir)) return;
            if (string.Equals(_watchedDir, sourceDir, StringComparison.OrdinalIgnoreCase)) return;

            try
            {
                _sourceWatcher?.Dispose();
                _watchedDir = sourceDir;
                _sourceWatcher = new FileSystemWatcher(sourceDir)
                {
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true
                };

                void OnFsChange(object sender, FileSystemEventArgs e)
                {
                    if (e.Name != null && e.Name.EndsWith("tactical_config.json", StringComparison.OrdinalIgnoreCase)) return;
                    win?.Dispatcher?.BeginInvoke(new Action(async () =>
                    {
                        var st = await Task.Run(() => CalculateSourceFolderStats(sourceDir));
                        UpdatePreviewCardLabels(win, st);
                    }), DispatcherPriority.Background);
                }

                _sourceWatcher.Created += OnFsChange;
                _sourceWatcher.Deleted += OnFsChange;
                _sourceWatcher.Changed += OnFsChange;
                _sourceWatcher.Renamed += (s, e) => OnFsChange(s, e);
            }
            catch { }
        }

        private static async Task<SourceStats> ForceSizeUpdateAsync(Window win)
        {
            var empty = new SourceStats();
            if (Application.Current == null || win == null) return empty;

            try
            {
                EnsureDefaultFactions();
                SyncActiveFactionToDefaultModel();
                var activeFac = _factions[Math.Clamp(_selectedFactionIndex, 0, _factions.Count - 1)];
                var sourceDirs = ResolveSourceDirectories(win);

                SourceStats bestStats = null;
                await Task.Run(() =>
                {
                    foreach (var sDir in sourceDirs)
                    {
                        try
                        {
                            if (!Directory.Exists(sDir)) Directory.CreateDirectory(sDir);
                            WriteConfigsToDirectory(sDir, activeFac);
                            var st = CalculateSourceFolderStats(sDir);
                            if (bestStats == null || st.FileCount > bestStats.FileCount || st.TotalBytes > bestStats.TotalBytes)
                                bestStats = st;
                        }
                        catch { }
                    }
                });

                bestStats ??= empty;
                EnsureWatcherForDirectory(win, bestStats.SourcePath);
                UpdatePreviewCardLabels(win, bestStats);
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
                    stats.TotalBytes += len;

                    if (ext == ".apk" || rel.StartsWith("plugins/") || rel.StartsWith("wtyczki/"))
                    {
                        stats.PluginsCount++;
                        stats.PluginsBytes += len;
                    }
                    else if (rel.StartsWith("maps/") || rel.StartsWith("mapy/") || rel.StartsWith("imagery/") || rel.StartsWith("tiles/") ||
                             ext == ".mbtiles" || ext == ".sqlite" || ext == ".sqlitedb" || ext == ".tif" || ext == ".tiff" || ext == ".geotiff" ||
                             ext == ".dt0" || ext == ".dt1" || ext == ".dt2" || ext == ".ecw" || ext == ".jp2" ||
                             ((ext == ".kmz" || ext == ".xml" || ext == ".pdf") && (nameLower.Contains("map") || nameLower.Contains("orto") || nameLower.Contains("lidar") || nameLower.Contains("topo"))))
                    {
                        stats.MapsCount++;
                        stats.MapsBytes += len;
                    }
                    else if (rel.StartsWith("config/") || rel.StartsWith("konfiguracja/") || rel.StartsWith("certs/") || rel.StartsWith("cert/") || rel.StartsWith("meshtastic/") ||
                             ext == ".json" || ext == ".pref" || ext == ".cfg" || ext == ".conf" || ext == ".ini" ||
                             ext == ".p12" || ext == ".pem" || ext == ".crt" || ext == ".cer" || ext == ".key" ||
                             nameLower == "server.txt" || nameLower == "manifest.xml")
                    {
                        stats.ConfigCount++;
                        stats.ConfigBytes
