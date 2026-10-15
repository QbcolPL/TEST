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

        private static Style _darkComboBoxStyle;
        private static Style _darkComboBoxItemStyle;
        private static Button _injectedRefreshButton;
        private static TextBox _injectedPkgValidityBox;
        private static TextBox _injectedQrValidityBox;
        private static TextBlock _indefiniteStatusBadge;
        private static TextBlock _factionStatusLabel;
        private static bool _syncingValidity;
        private static FileSystemWatcher _sourceWatcher;
        private static string _watchedDir = "";

        private static readonly List<FactionProfileModel> _factions = new List<FactionProfileModel>();
        private static int _selectedFactionIndex;
        private static WrapPanel _factionTabHeadersPanel;
        private static Border _factionTabContentHost;

        public static List<(string Name, string Channel, string ColorHex, string Psk)> GetConfiguredFactions()
        {
            EnsureDefaultFactions();
            return _factions.Select(f => (f.Name, f.ChannelName, f.ColorHex, f.Psk)).ToList();
        }

        private static void EnsureDefaultFactions()
        {
            if (_factions.Count > 0) return;
            _factions.Add(new FactionProfileModel { Name = "BLUFOR", ColorHex = "#2684FF", ChannelName = "BLUFOR-TAK", EnrollmentUsername = "blufor_op" });
            _factions.Add(new FactionProfileModel { Name = "OPFOR", ColorHex = "#EB4034", ChannelName = "OPFOR-TAK", EnrollmentUsername = "opfor_op" });
            _factions.Add(new FactionProfileModel { Name = "ORGA", ColorHex = "#F5B82E", ChannelName = "ORGA-HQ", DeviceRole = "TAK", EnrollmentUsername = "orga_hq" });
        }

        private const string DarkComboBoxXaml = @"
<Style xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
       xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" TargetType=""ComboBox"">
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
                    <ToggleButton Focusable=""False"" IsChecked=""{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}"" ClickMode=""Press"">
                        <ToggleButton.Template>
                            <ControlTemplate TargetType=""ToggleButton"">
                                <Border Background=""Transparent""><Grid><Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""28""/></Grid.ColumnDefinitions><Path Grid.Column=""1"" HorizontalAlignment=""Center"" VerticalAlignment=""Center"" Data=""M 0 0 L 4.5 4.5 L 9 0"" Stroke=""#FFFFFF"" StrokeThickness=""1.6"" /></Grid></Border>
                            </ControlTemplate>
                        </ToggleButton.Template>
                    </ToggleButton>
                    <ContentPresenter IsHitTestVisible=""False"" Content=""{TemplateBinding SelectionBoxItem}"" ContentTemplate=""{TemplateBinding SelectionBoxItemTemplate}"" Margin=""10,0,28,0"" VerticalAlignment=""Center"" HorizontalAlignment=""Left"" TextElement.Foreground=""#FFFFFF"" />
                    <TextBlock x:Name=""FallbackText"" IsHitTestVisible=""False"" Text=""{Binding Text, RelativeSource={RelativeSource TemplatedParent}}"" Foreground=""#FFFFFF"" Margin=""10,0,28,0"" VerticalAlignment=""Center"" HorizontalAlignment=""Left"" Visibility=""Collapsed"" />
                    <Popup Placement=""Bottom"" IsOpen=""{TemplateBinding IsDropDownOpen}"" AllowsTransparency=""True"" Focusable=""False"" PopupAnimation=""Slide"">
                        <Grid SnapsToDevicePixels=""True"" MinWidth=""{TemplateBinding ActualWidth}"" MaxHeight=""{TemplateBinding MaxDropDownHeight}"">
                            <Border Background=""#0F2025"" BorderBrush=""#1FE0C5"" BorderThickness=""1"" CornerRadius=""4"" Margin=""0,2,0,0"">
                                <ScrollViewer Margin=""2""><StackPanel IsItemsHost=""True"" /></ScrollViewer>
                            </Border>
                        </Grid>
                    </Popup>
                </Grid>
                <ControlTemplate.Triggers>
                    <Trigger Property=""IsMouseOver"" Value=""True""><Setter TargetName=""MainBorder"" Property=""BorderBrush"" Value=""#1FE0C5""/></Trigger>
                    <Trigger Property=""SelectionBoxItem"" Value=""{x:Null}""><Setter TargetName=""FallbackText"" Property=""Visibility"" Value=""Visible""/></Trigger>
                    <Trigger Property=""SelectionBoxItem"" Value=""""><Setter TargetName=""FallbackText"" Property=""Visibility"" Value=""Visible""/></Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>";

        internal static void ApplyUnifiedComboBoxTheme(ComboBox cb, Window win)
        {
            if (cb == null) return;
            try
            {
                string preserved = cb.Text;
                cb.IsEditable = false;
                cb.IsReadOnly = false;
                cb.Foreground = Brushes.White;
                cb.Background = BrushInputBg;
                cb.BorderBrush = BrushInputBorder;

                if (_darkComboBoxStyle == null)
                {
                    _darkComboBoxStyle = (Style)XamlReader.Parse(DarkComboBoxXaml);
                    _darkComboBoxStyle.Seal();
                }
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
                cb.Style = _darkComboBoxStyle;
                cb.ItemContainerStyle = _darkComboBoxItemStyle;

                foreach (var item in cb.Items)
                {
                    if (item is ComboBoxItem cbi) { cbi.Foreground = Brushes.White; cbi.Background = BrushItemBg; }
                }
                if (cb.SelectedItem == null && !string.IsNullOrWhiteSpace(preserved))
                {
                    foreach (var item in cb.Items)
                    {
                        string str = (item is ComboBoxItem ci) ? ci.Content?.ToString() : item?.ToString();
                        if (string.Equals(str, preserved, StringComparison.OrdinalIgnoreCase)) { cb.SelectedItem = item; break; }
                    }
                }
                if (cb.SelectedItem == null && cb.Items.Count > 0) cb.SelectedIndex = 0;

                if (win != null && !(bool)cb.GetValue(IsHookedProperty))
                {
                    cb.SetValue(IsHookedProperty, true);
                    cb.SelectionChanged += (_, __) => SyncNativeMeshtasticSelections(win);
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

        private static Border GetOuterCardBorder(DependencyObject startElement, int minTextBlocks = 4)
        {
            Border best = null;
            DependencyObject cur = startElement;
            while (cur != null)
            {
                if (cur is Border b)
                {
                    var tbs = new List<TextBlock>();
                    FindVisualChildren(b, tbs);
                    if (tbs.Count >= minTextBlocks) return b;
                    if (best == null && b.Background != null) best = b;
                }
                cur = VisualTreeHelper.GetParent(cur) ?? LogicalTreeHelper.GetParent(cur);
            }
            return best;
        }

        private static Border GetPreviewCardBorder(Window win)
        {
            var allBorders = new List<Border>();
            FindVisualChildren(win, allBorders);
            Border bestMatch = null;
            int smallestCount = int.MaxValue;

            foreach (var b in allBorders)
            {
                var tbs = new List<TextBlock>();
                FindVisualChildren(b, tbs);
                if (tbs.Count < 12) continue;

                bool hasTotal = tbs.Any(t => t.Text != null && t.Text.Trim().StartsWith("Pliki paczki", StringComparison.OrdinalIgnoreCase));
                bool hasMaps = tbs.Any(t => t.Text != null && t.Text.Trim().StartsWith("Mapy", StringComparison.OrdinalIgnoreCase));
                bool hasOther = tbs.Any(t => t.Text != null && t.Text.Trim().StartsWith("Pozostałe", StringComparison.OrdinalIgnoreCase));

                if (hasTotal && hasMaps && hasOther && tbs.Count < smallestCount)
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
                if (child is FrameworkElement fe && string.Equals(fe.Tag?.ToString(), tag, StringComparison.Ordinal)) return true;
            return false;
        }

        public static void AttachToMainWindow(Window win)
        {
            if (Application.Current == null || win == null) return;
            EnsureDefaultFactions();

            void Init()
            {
                try
                {
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
                            Border cardBorder = GetOuterCardBorder(tb, 4);
                            if (cardBorder != null)
                            {
                                if (cardBorder.Child is Panel innerPanel && !HasInjectedTag(innerPanel, "INJECTED_FW"))
                                    InjectMeshtasticFields(innerPanel, win);

                                if (VisualTreeHelper.GetParent(cardBorder) is Panel parentPanel && !HasInjectedTag(parentPanel, "INJECTED_ATAK"))
                                {
                                    int idx = parentPanel.Children.IndexOf(cardBorder);
                                    parentPanel.Children.Insert(idx, BuildAtakCard(win));
                                    parentPanel.Children.Insert(idx + 1, BuildTabbedFactionsCard(win));
                                }
                            }
                        }
                    }

                    Border previewBorder = GetPreviewCardBorder(win);
                    if (previewBorder != null && VisualTreeHelper.GetParent(previewBorder) is Panel previewParent && !HasInjectedTag(previewParent, "INJECTED_BTN"))
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
                        btnRefresh.Click += async (_, __) =>
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
                            Content = "🖨️ Eksportuj plakat startowy A4 aktywnej strony (PNG)",
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
                        btnPosterQuick.Click += async (_, __) =>
                        {
                            btnPosterQuick.IsEnabled = false;
                            await ExportSafezonePosterForCurrentAsync(win);
                            btnPosterQuick.IsEnabled = true;
                        };

                        int idx = previewParent.Children.IndexOf(previewBorder);
                        previewParent.Children.Insert(idx + 1, btnRefresh);
                        previewParent.Children.Insert(idx + 2, btnPosterQuick);
                    }

                    HookNativeValidityControls(win);
                    SyncNativeMeshtasticSelections(win);
                }
                catch { }
            }

            Init();

            if (!(bool)win.GetValue(IsHookedProperty))
            {
                win.SetValue(IsHookedProperty, true);
                win.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((_, __) => ApplyIndefiniteValidityBeforeAction(win)), true);
                win.Loaded += async (_, __) =>
                {
                    Init();
                    var res = await ForceSizeUpdateAsync(win);
                    if (_injectedRefreshButton != null)
                    {
                        _injectedRefreshButton.Content = $"Odśwież / Skalkuluj (Plików: {res.FileCount} | {FormatBytes(res.TotalBytes)})";
                        _injectedRefreshButton.ToolTip = $"Aktywny folder source: {res.SourcePath}";
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
                _injectedRefreshButton.ToolTip = $"Aktywny folder source: {res.SourcePath}";
            }
        }

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
            var titleDock = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var btnJumpMap = new Button
            {
                Content = "🗺️ Otwórz Generator Map (Pełny Ekran)",
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
            btnJumpMap.Click += (_, __) => TacticalMapGeneratorView.ShowFullScreenMapTab(win);
            titleDock.Children.Add(btnJumpMap);
            titleDock.Children.Add(new TextBlock
            {
                Text = "Generator Stron Konfliktu (Karty Stron: Meshtastic + OpenTAKServer Enrollment)",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center
            });
            stack.Children.Add(titleDock);

            stack.Children.Add(new TextBlock
            {
                Text = "Wybierz zakładkę strony konfliktu, aby skonfigurować jej kolor RGB, szyfrowany kanał Meshtastic (PSK) oraz parametry OpenTAKServer Enrollment.",
                Foreground = BrushMutedText,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            });

            _factionTabHeadersPanel = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            stack.Children.Add(_factionTabHeadersPanel);

            _factionTabContentHost = new Border
            {
                Background = BrushItemBg,
                BorderBrush = BrushTileBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 12)
            };
            stack.Children.Add(_factionTabContentHost);

            RebuildFactionTabsUI(win);

            var actionButtonsGrid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 2, 0, 6) };
            var btnInitFolders = new Button { Content = "📁 Przygotuj podfoldery KML stron", Height = 32, Margin = new Thickness(0, 0, 5, 0), Background = BrushItemBg, Foreground = Brushes.White, BorderBrush = BrushTileBorder, BorderThickness = new Thickness(1), FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand };
            var btnExportPoster = new Button { Content = "🖨️ Eksportuj plakat A4 strony (PNG)", Height = 32, Margin = new Thickness(3, 0, 3, 0), Background = BrushItemBg, Foreground = Brushes.White, BorderBrush = BrushAccent, BorderThickness = new Thickness(1), FontWeight = FontWeights.Bold, Cursor = Cursors.Hand };
            var btnBuildAllFactions = new Button { Content = "⚡ Buduj wszystkie strony (.ftak + QR)", Height = 32, Margin = new Thickness(5, 0, 0, 0), Background = BrushButtonAccent, Foreground = BrushDarkFg, BorderThickness = new Thickness(0), FontWeight = FontWeights.Bold, Cursor = Cursors.Hand };

            actionButtonsGrid.Children.Add(btnInitFolders);
            actionButtonsGrid.Children.Add(btnExportPoster);
            actionButtonsGrid.Children.Add(btnBuildAllFactions);
            stack.Children.Add(actionButtonsGrid);

            _factionStatusLabel = new TextBlock { Text = "Gotowy.", Foreground = BrushMutedText, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
            stack.Children.Add(_factionStatusLabel);

            btnInitFolders.Click += async (_, __) =>
            {
                string srcDir = GetPrimarySourceDirectory(win);
                var sides = _factions.Select(f => (SanitizeFileName(f.Name).ToLowerInvariant(), f.ColorHex)).ToList();
                await Task.Run(() => EnsureFactionFoldersInitialized(srcDir, sides));
                await ForceSizeUpdateAsync(win);
                _factionStatusLabel.Text = $"✓ Utworzono podfoldery KML dla {_factions.Count} stron w: {Path.Combine(srcDir, "factions")}";
                _factionStatusLabel.Foreground = BrushAccent;
                TryOpenFolderInExplorer(Path.Combine(srcDir, "factions"));
            };

            btnExportPoster.Click += async (_, __) => { btnExportPoster.IsEnabled = false; await ExportSafezonePosterForCurrentAsync(win); btnExportPoster.IsEnabled = true; };
            btnBuildAllFactions.Click += async (_, __) =>
            {
                btnBuildAllFactions.IsEnabled = false;
                string orig = btnBuildAllFactions.Content?.ToString();
                btnBuildAllFactions.Content = "Budowanie wszystkich stron...";
                await BuildDynamicFactionsAsync(win);
                btnBuildAllFactions.Content = orig;
                btnBuildAllFactions.IsEnabled = true;
            };

            card.Child = stack;
            return card;
        }

        private static void RebuildFactionTabsUI(Window win)
        {
            if (_factionTabHeadersPanel == null || _factionTabContentHost == null) return;
            EnsureDefaultFactions();
            _selectedFactionIndex = Math.Clamp(_selectedFactionIndex, 0, _factions.Count - 1);
            _factionTabHeadersPanel.Children.Clear();

            for (int i = 0; i < _factions.Count; i++)
            {
                int idx = i;
                var fac = _factions[i];
                bool sel = (idx == _selectedFactionIndex);

                var dot = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = BrushFromHex(fac.ColorHex, Color.FromRgb(38, 132, 255)), BorderBrush = Brushes.White, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
                var lbl = new TextBlock { Text = string.IsNullOrWhiteSpace(fac.Name) ? $"STRONA-{idx + 1}" : fac.Name, Foreground = sel ? BrushAccent : Brushes.White, FontWeight = FontWeights.Bold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(dot); sp.Children.Add(lbl);

                var tabBtn = new Button
                {
                    Content = sp,
                    Height = 32,
                    Padding = new Thickness(14, 0, 14, 0),
                    Margin = new Thickness(0, 0, 6, 4),
                    Background = sel ? BrushInputBg : BrushItemBg,
                    BorderBrush = sel ? BrushAccent : BrushTileBorder,
                    BorderThickness = new Thickness(sel ? 2 : 1),
                    Cursor = Cursors.Hand
                };
                tabBtn.Click += (_, __) => { _selectedFactionIndex = idx; SyncActiveFactionToDefaultModel(); RebuildFactionTabsUI(win); };
                _factionTabHeadersPanel.Children.Add(tabBtn);
            }

            string[] palette = { "#2ECC71", "#9B59B6", "#E67E22", "#1ABC9C", "#E84393", "#00CEC9" };
            var btnAdd = new Button { Content = "+ Dodaj stronę", Height = 32, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(2, 0, 6, 4), Background = BrushInputBg, Foreground = BrushAccent, BorderBrush = BrushAccent, BorderThickness = new Thickness(1), FontSize = 11.5, FontWeight = FontWeights.Bold, Cursor = Cursors.Hand };
            btnAdd.Click += (_, __) =>
            {
                int n = _factions.Count + 1;
                _factions.Add(new FactionProfileModel { Name = $"STRONA-{n}", ColorHex = palette[(n - 1) % palette.Length], ChannelName = $"TAK-CH{n}", EnrollmentUsername = $"strona{n}" });
                _selectedFactionIndex = _factions.Count - 1;
                SyncActiveFactionToDefaultModel();
                RebuildFactionTabsUI(win);
            };
            _factionTabHeadersPanel.Children.Add(btnAdd);

            var btnRem = new Button { Content = "− Usuń stronę", Height = 32, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 0, 4), Background = BrushInputBg, Foreground = Brushes.White, BorderBrush = BrushTileBorder, BorderThickness = new Thickness(1), FontSize = 11.5, FontWeight = FontWeights.Bold, Cursor = Cursors.Hand };
            btnRem.Click += (_, __) =>
            {
                if (_factions.Count <= 1) return;
                _factions.RemoveAt(_selectedFactionIndex);
                _selectedFactionIndex = Math.Clamp(_selectedFactionIndex, 0, _factions.Count - 1);
                SyncActiveFactionToDefaultModel();
                RebuildFactionTabsUI(win);
            };
            _factionTabHeadersPanel.Children.Add(btnRem);

            SyncActiveFactionToDefaultModel();
            _factionTabContentHost.Child = BuildSingleFactionTabContent(win, _factions[_selectedFactionIndex]);
        }

        private static UIElement BuildSingleFactionTabContent(Window win, FactionProfileModel fac)
        {
            var root = new StackPanel();
            root.Children.Add(new TextBlock { Text = $"1. Identyfikacja Strony, Kolor RGB oraz Warstwy KML ({fac.Name})", Foreground = BrushAccent, FontWeight = FontWeights.Bold, FontSize = 12, Margin = new Thickness(0, 0, 0, 8) });

            var topRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(185) });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });

            var nameBox = (TextBox)TextCtrl(fac.Name, val => { fac.Name = val.Trim(); SyncActiveFactionToDefaultModel(); });
            nameBox.FontWeight = FontWeights.Bold;
            nameBox.LostFocus += (_, __) => RebuildFactionTabsUI(win);
            var fName = FieldBox("Nazwa strony / frakcji:", nameBox);
            Grid.SetColumn(fName, 0); topRow.Children.Add(fName);

            var swatch = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(4), Background = BrushFromHex(fac.ColorHex, Color.FromRgb(38, 132, 255)), BorderBrush = Brushes.White, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            var cLbl = new TextBlock { Text = $"Paleta RGB ({fac.ColorHex.ToUpperInvariant()})", Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var cStack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            cStack.Children.Add(swatch); cStack.Children.Add(cLbl);

            var colorBtn = new Button { Content = cStack, Height = 28, Background = BrushInputBg, BorderBrush = BrushInputBorder, BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
            colorBtn.Click += (_, __) =>
            {
                string picked = ShowRgbColorPickerDialog(win, fac.Name, fac.ColorHex);
                if (!string.IsNullOrEmpty(picked)) { fac.ColorHex = picked; SyncActiveFactionToDefaultModel(); RebuildFactionTabsUI(win); }
            };
            var fCol = FieldBox("Kolor taktyczny strony (RGB):", colorBtn);
            Grid.SetColumn(fCol, 1); topRow.Children.Add(fCol);

            var btnKml = new Button { Content = $"📁 source/factions/{SanitizeFileName(fac.Name).ToLowerInvariant()}", Height = 28, Background = BrushInputBg, Foreground = Brushes.White, BorderBrush = BrushInputBorder, BorderThickness = new Thickness(1), FontSize = 10.5, Cursor = Cursors.Hand };
            btnKml.Click += async (_, __) =>
            {
                string srcDir = GetPrimarySourceDirectory(win);
                string sl = SanitizeFileName(fac.Name).ToLowerInvariant();
                await Task.Run(() => EnsureFactionFoldersInitialized(srcDir, new List<(string, string)> { (sl, fac.ColorHex) }));
                TryOpenFolderInExplorer(Path.Combine(srcDir, "factions", sl));
            };
            var fKml = FieldBox("Dedykowane nakładki KML:", btnKml);
            Grid.SetColumn(fKml, 2); topRow.Children.Add(fKml);
            root.Children.Add(topRow);

            root.Children.Add(new TextBlock { Text = $"2. Meshtastic — Kanał Szyfrowany LoRa ({fac.Name})", Foreground = BrushAccent, FontWeight = FontWeights.Bold, FontSize = 12, Margin = new Thickness(0, 4, 0, 8) });
            var meshRow1 = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 4) };
            meshRow1.Children.Add(FieldBox("Nazwa kanału LoRa:", TextCtrl(fac.ChannelName, v => { fac.ChannelName = v.Trim(); SyncActiveFactionToDefaultModel(); })));
            meshRow1.Children.Add(FieldBox("Region LoRa:", ComboCtrl(new[] { "EU_868", "US_915", "EU_433", "ANZ_915", "RU_864", "KR_920" }, fac.LoraRegion, win, v => { fac.LoraRegion = v; Current.LoraRegion = v; })));
            meshRow1.Children.Add(FieldBox("Modem Preset:", ComboCtrl(new[] { "SHORT_FAST", "LONG_FAST", "MEDIUM_FAST", "MEDIUM_SLOW", "LONG_SLOW" }, fac.LoraModemPreset, win, v => { fac.LoraModemPreset = v; Current.LoraModemPreset = v; })));
            meshRow1.Children.Add(FieldBox("Rola urządzenia:", ComboCtrl(new[] { "Auto", "TAK", "TAK_Tracker", "Client", "Router" }, fac.DeviceRole, win, v => { fac.DeviceRole = v; Current.DeviceRole = v; })));
            root.Children.Add(meshRow1);

            var pskGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            pskGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pskGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            var pskBox = (TextBox)TextCtrl(fac.Psk, v => { fac.Psk = v.Trim(); SyncActiveFactionToDefaultModel(); });
            pskBox.FontFamily = new FontFamily("Consolas");
            var fPsk = FieldBox("Klucz szyfrujący PSK kanału Meshtastic (AES-256 Base64):", pskBox);
            Grid.SetColumn(fPsk, 0); pskGrid.Children.Add(fPsk);

            var btnRoll = new Button { Content = "🎲 Losuj nowy klucz PSK", Height = 28, Margin = new Thickness(0, 14, 0, 6), Background = BrushInputBg, Foreground = BrushAccent, BorderBrush = BrushAccent, BorderThickness = new Thickness(1), FontSize = 11, FontWeight = FontWeights.Bold, Cursor = Cursors.Hand };
            btnRoll.Click += (_, __) => { fac.Psk = TacticalConfigModel.GenerateRandomBase64Psk(); pskBox.Text = fac.Psk; SyncActiveFactionToDefaultModel(); };
            Grid.SetColumn(btnRoll, 1); pskGrid.Children.Add(btnRoll);
            root.Children.Add(pskGrid);

            root.Children.Add(new TextBlock { Text = $"3. OpenTAKServer (OTS) & Certyfikaty Enrollment ({fac.Name})", Foreground = BrushAccent, FontWeight = FontWeights.Bold, FontSize = 12, Margin = new Thickness(0, 4, 0, 8) });
            var otsRow1 = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            otsRow1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            otsRow1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            otsRow1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });

            var fHost = FieldBox("Adres serwera OpenTAKServer (Host / IP):", TextCtrl(fac.OtsHost, v => fac.OtsHost = v.Trim()));
            var fSsl = FieldBox("Port SSL CoT:", IntCtrl(fac.OtsSslPort, v => fac.OtsSslPort = v));
            var fEnr = FieldBox("Port Enrollment:", IntCtrl(fac.OtsEnrollmentPort, v => fac.OtsEnrollmentPort = v));
            Grid.SetColumn(fHost, 0); Grid.SetColumn(fSsl, 1); Grid.SetColumn(fEnr, 2);
            otsRow1.Children.Add(fHost); otsRow1.Children.Add(fSsl); otsRow1.Children.Add(fEnr);
            root.Children.Add(otsRow1);

            var otsRow2 = new Grid();
            otsRow2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
            otsRow2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            otsRow2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var swEnr = TileSwitch("Włącz Auto-Enrollment OTS", fac.EnableAutoEnrollment, v => fac.EnableAutoEnrollment = v);
            swEnr.Margin = new Thickness(0, 10, 8, 6);
            var fUsr = FieldBox("Użytkownik / Grupa Enrollment:", TextCtrl(fac.EnrollmentUsername, v => fac.EnrollmentUsername = v.Trim()));
            var fPwd = FieldBox("Hasło Enrollment / Certyfikatu:", TextCtrl(fac.EnrollmentPassword, v => fac.EnrollmentPassword = v));
            Grid.SetColumn(swEnr, 0); Grid.SetColumn(fUsr, 1); Grid.SetColumn(fPwd, 2);
            otsRow2.Children.Add(swEnr); otsRow2.Children.Add(fUsr); otsRow2.Children.Add(fPwd);
            root.Children.Add(otsRow2);

            return root;
        }

        private static string ShowRgbColorPickerDialog(Window owner, string factionName, string initialHex)
        {
            Color initColor = Color.FromRgb(38, 132, 255);
            try { initColor = (Color)ColorConverter.ConvertFromString(initialHex); } catch { }
            byte r = initColor.R, g = initColor.G, b = initColor.B;
            string selectedHex = $"#{r:X2}{g:X2}{b:X2}";
            bool confirmed = false;

            var dlg = new Window { Title = $"Paleta RGB — {factionName}", Width = 410, Height = 350, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = owner, Background = BrushCardBg, Foreground = Brushes.White, ResizeMode = ResizeMode.NoResize, WindowStyle = WindowStyle.ToolWindow };
            var root = new StackPanel { Margin = new Thickness(16) };
            var previewBorder = new Border { Height = 40, CornerRadius = new CornerRadius(6), Background = CreateFrozenBrush(r, g, b), BorderBrush = BrushAccent, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 10) };
            var previewText = new TextBlock { Text = $"{factionName} ({selectedHex})", Foreground = Brushes.White, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            previewBorder.Child = previewText;
            root.Children.Add(previewBorder);

            var swatchesWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            string[] presets = { "#2684FF", "#EB4034", "#F5B82E", "#2ECC71", "#9B59B6", "#1FE0C5", "#E67E22", "#E84393", "#00CEC9", "#95A5A6" };
            Slider sR = null, sG = null, sB = null;
            bool updating = false;

            void UpdateFromRgb()
            {
                if (updating) return;
                updating = true;
                selectedHex = $"#{r:X2}{g:X2}{b:X2}";
                previewBorder.Background = CreateFrozenBrush(r, g, b);
                previewText.Text = $"{factionName} ({selectedHex})";
                if (sR != null) sR.Value = r;
                if (sG != null) sG.Value = g;
                if (sB != null) sB.Value = b;
                updating = false;
            }

            foreach (var pHex in presets)
            {
                var swBtn = new Button { Width = 30, Height = 22, Margin = new Thickness(0, 0, 6, 4), Background = BrushFromHex(pHex, initColor), BorderBrush = Brushes.White, BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
                string cap = pHex;
                swBtn.Click += (_, __) => { try { var c = (Color)ColorConverter.ConvertFromString(cap); r = c.R; g = c.G; b = c.B; UpdateFromRgb(); } catch { } };
                swatchesWrap.Children.Add(swBtn);
            }
            root.Children.Add(swatchesWrap);

            UIElement MakeSlider(string label, byte initVal, out Slider slOut, Action<byte> onVal)
            {
                var rg = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                rg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(65) });
                rg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
                var lbl = new TextBlock { Text = label, Foreground = Brushes.White, FontSize = 11 };
                var valTxt = new TextBlock { Text = initVal.ToString(), Foreground = BrushAccent, FontSize = 11, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right };
                var sl = new Slider { Minimum = 0, Maximum = 255, Value = initVal };
                sl.ValueChanged += (_, e) => { byte v = (byte)Math.Clamp((int)Math.Round(e.NewValue), 0, 255); valTxt.Text = v.ToString(); onVal(v); UpdateFromRgb(); };
                slOut = sl;
                Grid.SetColumn(lbl, 0); Grid.SetColumn(sl, 1); Grid.SetColumn(valTxt, 2);
                rg.Children.Add(lbl); rg.Children.Add(sl); rg.Children.Add(valTxt);
                return rg;
            }

            root.Children.Add(MakeSlider("Red (R):", r, out sR, v => r = v));
            root.Children.Add(MakeSlider("Green (G):", g, out sG, v => g = v));
            root.Children.Add(MakeSlider("Blue (B):", b, out sB, v => b = v));

            var btnBar = new UniformGrid { Columns = 2, Margin = new Thickness(0, 12, 0, 0) };
            var btnCancel = new Button { Content = "Anuluj", Height = 30, Margin = new Thickness(0, 0, 6, 0), Background = BrushItemBg, Foreground = Brushes.White, BorderBrush = BrushTileBorder, BorderThickness = new Thickness(1) };
            var btnOk = new Button { Content = "✓ Zastosuj kolor RGB", Height = 30, Margin = new Thickness(6, 0, 0, 0), Background = BrushButtonAccent, Foreground = BrushDarkFg, FontWeight = FontWeights.Bold, BorderThickness = new Thickness(0) };
            btnCancel.Click += (_, __) => dlg.Close();
            btnOk.Click += (_, __) => { confirmed = true; dlg.Close(); };
            btnBar.Children.Add(btnCancel); btnBar.Children.Add(btnOk);
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
                SyncNativeMeshtasticSelections(win);
                string sourceDir = GetPrimarySourceDirectory(win);
                string eventName = GetEventNameFromWindow(win);
                string parentDir = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(sourceDir)) ?? AppDomain.CurrentDomain.BaseDirectory;
                string outDir = Path.Combine(parentDir, "release_factions");
                Directory.CreateDirectory(outDir);

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
                        using var sha = SHA256.Create();
                        using var stream = File.OpenRead(ftakPath);
                        return Convert.ToHexString(sha.ComputeHash(stream));
                    });

                    string qrPayload = $"ftak://provision?event={Uri.EscapeDataString(eventName)}&faction={Uri.EscapeDataString(cleanName)}&channel={Uri.EscapeDataString(fac.ChannelName)}&ots={Uri.EscapeDataString(fac.OtsHost)}&region={fac.LoraRegion}&preset={fac.LoraModemPreset}&sha256={sha256.Substring(0, 16)}";
                    string posterPath = Path.Combine(outDir, $"Plakat_Safezone_A4_{eventName}_{cleanName}.png");
                    RenderSafezoneA4PosterPng(posterPath, eventName, fac, qrPayload, sha256);
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
            catch (Exception ex)
            {
                if (_factionStatusLabel != null) _factionStatusLabel.Text = $"Błąd: {ex.Message}";
            }
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
                Directory.CreateDirectory(outDir);

                string cleanName = SanitizeFileName(fac.Name);
                string ftakPath = Path.Combine(outDir, $"{eventName}_{cleanName}.ftak");
                string sha256 = await Task.Run(() =>
                {
                    WriteConfigsToDirectory(sourceDir, fac);
                    BuildFilteredFactionFtakArchive(sourceDir, ftakPath, fac, eventName);
                    using var sha = SHA256.Create();
                    using var stream = File.OpenRead(ftakPath);
                    return Convert.ToHexString(sha.ComputeHash(stream));
                });

                string qrPayload = $"ftak://provision?event={Uri.EscapeDataString(eventName)}&faction={Uri.EscapeDataString(cleanName)}&channel={Uri.EscapeDataString(fac.ChannelName)}&ots={Uri.EscapeDataString(fac.OtsHost)}&region={fac.LoraRegion}&preset={fac.LoraModemPreset}&sha256={sha256.Substring(0, 16)}";
                string posterPath = Path.Combine(outDir, $"Plakat_Safezone_A4_{eventName}_{cleanName}.png");
                RenderSafezoneA4PosterPng(posterPath, eventName, fac, qrPayload, sha256);
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
                    if (tb.Text != null && (tb.Text.Contains("Nazwa wydarzenia") || tb.Text.Contains("Event Name") || tb.Text.Contains("Nazwa paczki")))
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

        private static void EnsureFactionFoldersInitialized(string sourceDir, List<(string SideLower, string ColorHex)> sides)
        {
            try
            {
                string factionsRoot = Path.Combine(sourceDir, "factions");
                foreach (var item in sides)
                {
                    if (string.IsNullOrWhiteSpace(item.SideLower)) continue;
                    string sideDir = Path.Combine(factionsRoot, item.SideLower);
                    Directory.CreateDirectory(sideDir);
                    string sampleKml = Path.Combine(sideDir, $"strefa_{item.SideLower}.kml");
                    if (!File.Exists(sampleKml))
                    {
                        File.WriteAllText(sampleKml, $@"<?xml version=""1.0"" encoding=""UTF-8""?><kml xmlns=""http://www.opengis.net/kml/2.2""><Document><name>Warstwa {item.SideLower.ToUpperInvariant()}</name><Placemark><name>HQ {item.SideLower.ToUpperInvariant()}</name><Point><coordinates>19.0238,49.9135,0</coordinates></Point></Placemark></Document></kml>", Encoding.UTF8);
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
                string targetFactionPrefix = $"factions/{cleanName.ToLowerInvariant()}/";
                foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
                {
                    string fileName = Path.GetFileName(file);
                    if (fileName.StartsWith(".") || fileName.Equals("WORKSPACE_HELP.txt", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".fthproj", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string rel = Path.GetRelativePath(sourceDir, file).Replace('\\', '/');
                    string relLower = rel.ToLowerInvariant();
                    if (relLower.StartsWith("factions/") && !cleanName.Equals("ORGA", StringComparison.OrdinalIgnoreCase) && !relLower.StartsWith(targetFactionPrefix))
                        continue;

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
                ["expiresAt"] = (Current.PackageValidityHours == 0) ? DateTimeOffset.UtcNow.AddYears(100).ToString("o") : DateTimeOffset.UtcNow.AddHours(Current.PackageValidityHours).ToString("o")
            };

            var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
            using var ms = new StreamWriter(manifestEntry.Open(), Encoding.UTF8);
            ms.Write(manifestObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        private static void RenderSafezoneA4PosterPng(string outputPath, string eventName, FactionProfileModel fac, string qrPayload, string sha256Hex)
        {
            const int width = 1240, height = 1754;
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(BrushCardBg, null, new Rect(0, 0, width, height));
                dc.DrawRoundedRectangle(null, new Pen(BrushAccent, 4), new Rect(30, 30, width - 60, height - 60), 18, 18);
                dc.DrawRoundedRectangle(BrushFromHex(fac.ColorHex, Color.FromRgb(38, 132, 255)), null, new Rect(70, 70, width - 140, 120), 12, 12);

                DrawCenteredText(dc, "FIELD TAK HUB 2.4 — KARTA WDROŻENIOWA SAFEZONE", 24, Brushes.White, width / 2.0, 92, true);
                DrawCenteredText(dc, $"OPERACJA: {eventName.ToUpperInvariant()}   |   STRONA: {fac.Name.ToUpperInvariant()} ({fac.ColorHex.ToUpperInvariant()})", 32, Brushes.White, width / 2.0, 130, true);
                DrawCenteredText(dc, "SKANUJESZ  ➔  KONFIGURUJESZ  ➔  SPRAWDZASZ  ➔  JESTEŚ GOTOWY", 20, BrushAccent, width / 2.0, 220, true);

                double qrBoxSize = 620, qrBoxX = (width - qrBoxSize) / 2.0, qrBoxY = 275;
                dc.DrawRoundedRectangle(Brushes.White, new Pen(BrushAccent, 4), new Rect(qrBoxX, qrBoxY, qrBoxSize, qrBoxSize), 16, 16);
                DrawDeterministicQrMatrix(dc, qrPayload, qrBoxX + 45, qrBoxY + 45, qrBoxSize - 90);

                double stepsY = 935;
                dc.DrawRoundedRectangle(BrushItemBg, new Pen(BrushTileBorder, 2), new Rect(70, stepsY, width - 140, 340), 12, 12);
                DrawCenteredText(dc, "INSTRUKCJA SZYBKIEGO WDROŻENIA DLA UCZESTNIKA (3 KROKI)", 24, BrushAccent, width / 2.0, stepsY + 24, true);
                DrawLeftText(dc, "1. ZESKANUJ KOD QR: Otwórz aplikację Field TAK Hub i zeskanuj kod QR.", 20, Brushes.White, 110, stepsY + 95, true);
                DrawLeftText(dc, "2. PODŁĄCZ RADIO MESHTASTIC: Aplikacja sama wgra kanał i klucz PSK strony.", 20, Brushes.White, 110, stepsY + 170, true);
                DrawLeftText(dc, "3. ZIELONY EKRAN = GOTOWY:  ✓ ATAK gotowy   ✓ Meshtastic gotowy   ✓ Serwer dostępny.", 20, BrushAccent, 110, stepsY + 245, true);

                double techY = 1310;
                dc.DrawRoundedRectangle(BrushInputBg, new Pen(BrushInputBorder, 2), new Rect(70, techY, width - 140, 360), 12, 12);
                DrawCenteredText(dc, "PARAMETRY TECHNICZNE PACZKI, SERWERA OTS I RADIA LORA", 22, Brushes.White, width / 2.0, techY + 22, true);
                string validityText = Current.PackageValidityHours == 0 ? "BEZTERMINOWA (0)" : $"{Current.PackageValidityHours} godz.";
                DrawLeftText(dc, $"• Strona konfliktu:   {fac.Name} ({fac.ColorHex})   |   Kanał LoRa: {fac.ChannelName}", 20, Brushes.White, 110, techY + 80, true);
                DrawLeftText(dc, $"• Pasmo / Modem:      Region {fac.LoraRegion}   |   Preset {fac.LoraModemPreset}   |   Rola: {fac.DeviceRole}", 20, Brushes.White, 110, techY + 130, false);
                DrawLeftText(dc, $"• Serwer OTS:         {fac.OtsHost}:{fac.OtsSslPort} (Enrollment: {fac.OtsEnrollmentPort} / User: {fac.EnrollmentUsername})", 20, Brushes.White, 110, techY + 180, false);
                DrawLeftText(dc, $"• Ważność paczki:     {validityText}   |   SHA-256: {(sha256Hex.Length > 36 ? sha256Hex.Substring(0, 36) + "..." : sha256Hex)}", 18, BrushMutedText, 110, techY + 235, false);
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
            try { if (Directory.Exists(folderPath)) Process.Start(new ProcessStartInfo { FileName = folderPath, UseShellExecute = true }); } catch { }
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
                string n = (tb.Name ?? "").ToLowerInvariant();
                string t = (tb.ToolTip?.ToString() ?? "").ToLowerInvariant();
                if (n.Contains("valid") || n.Contains("expir") || n.Contains("ttl") || t.Contains("ważno") || t.Contains("wygas"))
                    result.Add(tb);
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
                tb.TextChanged += (_, __) =>
                {
                    if (_syncingValidity) return;
                    if (int.TryParse(tb.Text?.Trim(), out int v) && v >= 0 && v < 800000)
                    {
                        _syncingValidity = true;
                        try
                        {
                            Current.PackageValidityHours = v;
                            Current.QrValidityHours = v;
                            if (_injectedPkgValidityBox != null) _injectedPkgValidityBox.Text = v.ToString();
                            if (_injectedQrValidityBox != null) _injectedQrValidityBox.Text = v.ToString();
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
                    int valToSet = (tb.Name ?? "").Contains("qr", StringComparison.OrdinalIgnoreCase) ? Current.QrValidityHours : Current.PackageValidityHours;
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
                var zeros = FindNativeValidityTextBoxes(win).Where(tb => int.TryParse(tb.Text?.Trim(), out int v) && v == 0).ToList();
                if (Current.PackageValidityHours != 0 && Current.QrValidityHours != 0 && zeros.Count == 0) return;

                _syncingValidity = true;
                try { foreach (var tb in zeros) tb.Text = "876000"; } finally { _syncingValidity = false; }

                _ = win.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _syncingValidity = true;
                    try { foreach (var tb in zeros) { if (tb.Text == "876000") tb.Text = "0"; tb.BorderBrush = BrushAccent; } }
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
                    if (string.IsNullOrWhiteSpace(val)) continue;
                    string vl = val.ToLowerInvariant();
                    if (vl.Contains("tbeam") || vl.Contains("heltec") || vl.Contains("meshtracker") || vl.Contains("rak") || vl.Contains("generic")) Current.HardwareModel = val;
                    else if (vl == "auto" || vl.Contains("tak") || vl.Contains("client") || vl.Contains("router")) Current.DeviceRole = val;
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
                        string dirToAdd = string.Equals(Path.GetFileName(clean), "source", StringComparison.OrdinalIgnoreCase) ? clean : Path.Combine(clean, "source");
                        if (seen.Add(dirToAdd)) list.Add(dirToAdd);
                    }
                }
                catch { }
            }

            if (!string.IsNullOrWhiteSpace(LastKnownSourceDir)) AddCandidate(LastKnownSourceDir);
            if (win != null)
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                foreach (var f in win.GetType().GetFields(flags)) { try { if (f.GetValue(win) is string s) AddCandidate(s); } catch { } }
                var textBoxes = new List<TextBox>();
                FindVisualChildren(win, textBoxes);
                foreach (var tbx in textBoxes) AddCandidate(tbx.Text);
            }
            if (list.Count == 0) list.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "workspace", "source"));
            return list;
        }

        private class SourceStats
        {
            public string SourcePath = "";
            public int FileCount;
            public long TotalBytes, MapsBytes, PluginsBytes, ConfigBytes, TacticalBytes, OtherBytes;
        }

        private static void EnsureWatcherForDirectory(Window win, string sourceDir)
        {
            if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir) || string.Equals(_watchedDir, sourceDir, StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                _sourceWatcher?.Dispose();
                _watchedDir = sourceDir;
                _sourceWatcher = new FileSystemWatcher(sourceDir) { IncludeSubdirectories = true, EnableRaisingEvents = true };
                void OnChange(object s, FileSystemEventArgs e)
                {
                    if (e.Name != null && e.Name.EndsWith("tactical_config.json", StringComparison.OrdinalIgnoreCase)) return;
                    win?.Dispatcher?.BeginInvoke(new Action(async () =>
                    {
                        var st = await Task.Run(() => CalculateSourceFolderStats(sourceDir));
                        UpdatePreviewCardLabels(win, st);
                    }), DispatcherPriority.Background);
                }
                _sourceWatcher.Created += OnChange; _sourceWatcher.Deleted += OnChange; _sourceWatcher.Changed += OnChange;
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
                SyncNativeMeshtasticSelections(win);
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
                            Directory.CreateDirectory(sDir);
                            WriteConfigsToDirectory(sDir, activeFac);
                            var st = CalculateSourceFolderStats(sDir);
                            if (bestStats == null || st.FileCount > bestStats.FileCount || st.TotalBytes > bestStats.TotalBytes) bestStats = st;
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
                    if (string.IsNullOrEmpty(fileName) || fileName.StartsWith(".") || fileName.StartsWith("README", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".fthproj", StringComparison.OrdinalIgnoreCase) || fileName.Equals("WORKSPACE_HELP.txt", StringComparison.OrdinalIgnoreCase))
                        continue;

                    long len = 0;
                    try { len = new FileInfo(file).Length; } catch { continue; }

                    string rel = Path.GetRelativePath(sourceDir, file).Replace('\\', '/').ToLowerInvariant();
                    string ext = Path.GetExtension(fileName).ToLowerInvariant();
                    string nameLower = fileName.ToLowerInvariant();

                    stats.FileCount++;
                    stats.TotalBytes += len;

                    if (ext == ".apk" || rel.StartsWith("plugins/") || rel.StartsWith("wtyczki/"))
                        stats.PluginsBytes += len;
                    else if (rel.StartsWith("maps/") || rel.StartsWith("mapy/") || rel.StartsWith("imagery/") || rel.StartsWith("tiles/") ||
                             ext == ".mbtiles" || ext == ".sqlite" || ext == ".sqlitedb" || ext == ".tif" || ext == ".tiff" || ext == ".geotiff" ||
                             ((ext == ".kmz" || ext == ".xml" || ext == ".pdf") && (nameLower.Contains("map") || nameLower.Contains("orto") || nameLower.Contains("lidar") || nameLower.Contains("topo"))))
                        stats.MapsBytes += len;
                    else if (rel.StartsWith("config/") || rel.StartsWith("certs/") || rel.StartsWith("cert/") ||
                             ext == ".json" || ext == ".pref" || ext == ".cfg" || ext == ".conf" || ext == ".ini" ||
                             ext == ".p12" || ext == ".pem" || ext == ".crt" || ext == ".cer" || ext == ".key" || nameLower == "server.txt")
                        stats.ConfigBytes += len;
                    else if (rel.StartsWith("atak/") || rel.StartsWith("overlays/") || rel.StartsWith("factions/") || rel.StartsWith("data/") ||
                             ext == ".kml" || ext == ".kmz" || ext == ".gpx" || ext == ".cot" || ext == ".geojson" || ext == ".shp")
                        stats.TacticalBytes += len;
                    else
                        stats.OtherBytes += len;
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
            while (val >= 1024 && idx < units.Length - 1) { val /= 1024; idx++; }
            return idx == 0 ? $"{bytes} B" : $"{val:0.#} {units[idx]}";
        }

        private static void UpdatePreviewCardLabels(Window win, SourceStats stats)
        {
            if (win == null || stats == null) return;
            try
            {
                Border previewBorder = GetPreviewCardBorder(win);
                if (previewBorder == null) return;
                previewBorder.UpdateLayout();

                var cardTbs = new List<TextBlock>();
                FindVisualChildren(previewBorder, cardTbs);

                string[] rowLabels = { "Pliki paczki", "Mapy", "Pluginy", "Konfiguracja", "Dane taktyczne", "Pozostałe" };
                string[] rowValues = {
                    FormatBytes(stats.TotalBytes), FormatBytes(stats.MapsBytes), FormatBytes(stats.PluginsBytes),
                    FormatBytes(stats.ConfigBytes), FormatBytes(stats.TacticalBytes), FormatBytes(stats.OtherBytes)
                };

                bool IsLabel(string text)
                {
                    if (string.IsNullOrWhiteSpace(text)) return true;
                    string tr = text.Trim();
                    return tr.Contains("Podgląd zawartości", StringComparison.OrdinalIgnoreCase) || rowLabels.Any(l => tr.StartsWith(l, StringComparison.OrdinalIgnoreCase));
                }

                var assigned = new HashSet<TextBlock>();
                for (int r = 0; r < rowLabels.Length; r++)
                {
                    var labelTb = cardTbs.FirstOrDefault(t => t.Text != null && t.Text.Trim().StartsWith(rowLabels[r], StringComparison.OrdinalIgnoreCase));
                    if (labelTb == null) continue;

                    TextBlock matched = null;
                    var parent = VisualTreeHelper.GetParent(labelTb);
                    if (parent != null)
                    {
                        var pTbs = new List<TextBlock>();
                        FindVisualChildren(parent, pTbs);
                        if (pTbs.Count == 2) matched = pTbs.FirstOrDefault(x => x != labelTb && !IsLabel(x.Text));
                        else if (parent is Grid g && g.RowDefinitions.Count >= 6)
                        {
                            int tr = Grid.GetRow(labelTb);
                            foreach (UIElement ch in g.Children)
                                if (ch is TextBlock sib && sib != labelTb && Grid.GetRow(sib) == tr && !IsLabel(sib.Text)) { matched = sib; break; }
                        }
                    }

                    if (matched == null)
                    {
                        int idx = cardTbs.IndexOf(labelTb);
                        if (idx >= 0 && idx + 1 < cardTbs.Count && !IsLabel(cardTbs[idx + 1].Text) && !assigned.Contains(cardTbs[idx + 1]))
                            matched = cardTbs[idx + 1];
                    }

                    if (matched != null) { matched.Text = rowValues[r]; assigned.Add(matched); }
                }

                if (assigned.Count < 6)
                {
                    var vals = cardTbs.Where(t => !IsLabel(t.Text) && t.Text != null && t.Text.Length <= 24).ToList();
                    if (vals.Count == 6) for (int i = 0; i < 6; i++) vals[i].Text = rowValues[i];
                }
            }
            catch { }
        }

        private static UIElement BuildAtakCard(Window win)
        {
            var m = Current;
            var card = new Border { Background = BrushCardBg, BorderBrush = BrushCardBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 14), Tag = "INJECTED_ATAK" };
            var lStack = new StackPanel();
            lStack.Children.Add(new TextBlock { Text = "Konfiguracja ATAK-CIV (PLI, siec oraz waznosc paczki/QR)", Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 14, Margin = new Thickness(0, 0, 0, 4) });
            lStack.Children.Add(new TextBlock { Text = "Czysty profil ATAK bez wymuszania wtyczek. Wpisanie 0 w polach waznosci oznacza waznosc bezterminowa.", Foreground = BrushMutedText, FontSize = 11, Margin = new Thickness(0, 0, 0, 10) });

            var gAtak = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 8) };
            gAtak.Children.Add(FieldBox("Strategia PLI", ComboCtrl(new[] { "Constant", "Dynamic" }, m.LocationReportingStrategy, win, v => m.LocationReportingStrategy = v)));
            gAtak.Children.Add(FieldBox("Const Rel/LTE (s)", IntCtrl(m.ConstantReportingRateReliable, v => m.ConstantReportingRateReliable = v)));
            gAtak.Children.Add(FieldBox("Const Unrel/Mesh (s)", IntCtrl(m.ConstantReportingRateUnreliable, v => m.ConstantReportingRateUnreliable = v)));
            gAtak.Children.Add(FieldBox("Dynamic Max (s)", IntCtrl(m.DynamicReportingRateMaxReliable, v => m.DynamicReportingRateMaxReliable = v)));
            gAtak.Children.Add(FieldBox("Dynamic Min (s)", IntCtrl(m.DynamicReportingRateMinReliable, v => m.DynamicReportingRateMinReliable = v)));
            gAtak.Children.Add(FieldBox("Postoj Rel (s)", IntCtrl(m.DynamicReportingRateStationaryReliable, v => m.DynamicReportingRateStationaryReliable = v)));
            gAtak.Children.Add(FieldBox("Postoj Mesh (s)", IntCtrl(m.DynamicReportingRateStationaryUnreliable, v => m.DynamicReportingRateStationaryUnreliable = v)));
            gAtak.Children.Add(FieldBox("TCP/UDP Timeout (s)", IntCtrl(m.TcpConnectTimeout, v => m.TcpConnectTimeout = v)));
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
            gSw.Children.Add(TileSwitch("Wysylaj pozycje na zewnatrz", m.DispatchLocationCotExternal, v => m.DispatchLocationCotExternal = v));
            gSw.Children.Add(TileSwitch("Polaczenia bezstrumieniowe Mesh", m.EnableNonStreamingConnections, v => m.EnableNonStreamingConnections = v));
            lStack.Children.Add(gSw);

            card.Child = lStack;
            return card;
        }

        private static TextBox ValidityTextCtrl(int init, Action<int> onChg)
        {
            var tb = new TextBox { Text = init.ToString(), Height = 28, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 0, 6, 0), FontSize = 11, Background = BrushInputBg, Foreground = Brushes.White, BorderBrush = (init == 0) ? BrushAccent : BrushInputBorder, BorderThickness = new Thickness(1), Tag = "INJECTED_VALIDITY" };
            tb.TextChanged += (_, __) => { if (int.TryParse(tb.Text.Trim(), out int v) && v >= 0) { tb.BorderBrush = (v == 0) ? BrushAccent : BrushInputBorder; onChg(v); } };
            return tb;
        }

        private static void InjectMeshtasticFields(Panel parentPanel, Window win)
        {
            var loraGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 8, 0, 0), Tag = "INJECTED_FW" };
            loraGrid.Children.Add(FieldBox("LoRa Region", ComboCtrl(new[] { "EU_868", "US_915", "EU_433", "ANZ_915", "RU_864", "KR_920" }, Current.LoraRegion, win, v => Current.LoraRegion = v)));
            loraGrid.Children.Add(FieldBox("LoRa Modem Preset", ComboCtrl(new[] { "SHORT_FAST", "LONG_FAST", "LONG_SLOW", "MEDIUM_SLOW", "MEDIUM_FAST" }, Current.LoraModemPreset, win, v => Current.LoraModemPreset = v)));
            parentPanel.Children.Add(loraGrid);
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
            var cb = new ComboBox { ItemsSource = items, SelectedItem = init, Height = 28, FontSize = 11, VerticalContentAlignment = VerticalAlignment.Center, Tag = "INJECTED" };
            ApplyUnifiedComboBoxTheme(cb, win);
            cb.SelectionChanged += (_, __) => { if (cb.SelectedItem is string str) onChg(str); };
            return cb;
        }

        private static FrameworkElement TileSwitch(string title, bool init, Action<bool> onChg)
        {
            var b = new Border { Background = BrushInputBg, BorderBrush = BrushTileBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 8, 6) };
            var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var tb = new TextBlock { Text = title, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            var tr = new Border { Width = 36, Height = 20, CornerRadius = new CornerRadius(10), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
            var kn = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Margin = new Thickness(2) };
            bool state = init;
            void Upd() { tr.Background = state ? BrushAccent : BrushSwitchOff; kn.Background = state ? BrushDarkFg : BrushMutedText; kn.HorizontalAlignment = state ? HorizontalAlignment.Right : HorizontalAlignment.Left; }
            Upd(); tr.Child = kn;
            tr.MouseLeftButtonDown += (_, __) => { state = !state; Upd(); onChg(state); };
            Grid.SetColumn(tb, 0); Grid.SetColumn(tr, 1);
            g.Children.Add(tb); g.Children.Add(tr);
            b.Child = g;
            return b;
        }

        public static void WriteGeneratedConfigsIfInteractive(string tgt)
        {
            if (string.IsNullOrWhiteSpace(tgt) || tgt.Contains("fth-analyze-", StringComparison.OrdinalIgnoreCase)) return;
            LastKnownSourceDir = tgt;
            if (Application.Current == null) return;
            EnsureDefaultFactions();
            WriteConfigsToDirectory(tgt, _factions[Math.Clamp(_selectedFactionIndex, 0, _factions.Count - 1)]);
        }

        private static void WriteConfigsToDirectory(string tgt, FactionProfileModel fac = null)
        {
            if (string.IsNullOrWhiteSpace(tgt)) return;
            try
            {
                EnsureDefaultFactions();
                fac ??= _factions[Math.Clamp(_selectedFactionIndex, 0, _factions.Count - 1)];
                Directory.CreateDirectory(tgt);
                string jsonPath = Path.Combine(tgt, "tactical_config.json");

                JsonObject root = null;
                if (File.Exists(jsonPath)) { try { root = JsonNode.Parse(File.ReadAllText(jsonPath, Encoding.UTF8)) as JsonObject; } catch { } }
                root ??= new JsonObject();

                root["faction"] = fac.Name;
                root["factionColor"] = fac.ColorHex;
                root["packageValidityHours"] = Current.PackageValidityHours;
                root["qrValidityHours"] = Current.QrValidityHours;
                root["neverExpires"] = (Current.PackageValidityHours == 0);
                root["qrNeverExpires"] = (Current.QrValidityHours == 0);
                root["expiresAt"] = (Current.PackageValidityHours == 0) ? DateTimeOffset.UtcNow.AddYears(100).ToString("o") : DateTimeOffset.UtcNow.AddHours(Current.PackageValidityHours).ToString("o");

                JsonObject atak = (root.TryGetPropertyValue("atak", out var an) && an is JsonObject ea) ? ea : (JsonObject)(root["atak"] = new JsonObject());
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
                atak["enableNonStreamingConnections"] = Current.EnableNonStreamingConnections;

                JsonObject ots = (root.TryGetPropertyValue("openTakServer", out var on) && on is JsonObject eo) ? eo : (JsonObject)(root["openTakServer"] = new JsonObject());
                ots["host"] = fac.OtsHost;
                ots["sslPort"] = fac.OtsSslPort;
                ots["enrollmentPort"] = fac.OtsEnrollmentPort;
                ots["enableAutoEnrollment"] = fac.EnableAutoEnrollment;
                ots["enrollmentUsername"] = fac.EnrollmentUsername;
                ots["enrollmentPassword"] = fac.EnrollmentPassword;

                JsonObject mesh = (root.TryGetPropertyValue("meshtastic", out var mn) && mn is JsonObject em) ? em : (JsonObject)(root["meshtastic"] = new JsonObject());
                mesh["firmwareMin"] = Current.FirmwareMin;
                mesh["firmwareMax"] = Current.FirmwareMax;
                mesh["role"] = fac.DeviceRole;
                mesh["deviceRole"] = fac.DeviceRole;
                mesh["model"] = Current.HardwareModel;
                mesh["region"] = fac.LoraRegion;
                mesh["loraRegion"] = fac.LoraRegion;
                mesh["modemPreset"] = fac.LoraModemPreset;
                mesh["loraModemPreset"] = fac.LoraModemPreset;
                mesh["channelName"] = fac.ChannelName;
                mesh["psk"] = fac.Psk;
                mesh["usePreset"] = true;

                JsonObject lora = (mesh.TryGetPropertyValue("lora", out var ln) && ln is JsonObject el) ? el : (JsonObject)(mesh["lora"] = new JsonObject());
                lora["region"] = fac.LoraRegion;
                lora["modemPreset"] = fac.LoraModemPreset;
                lora["channelName"] = fac.ChannelName;
                lora["psk"] = fac.Psk;
                lora["usePreset"] = true;

                File.WriteAllText(jsonPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
            }
            catch { }
        }
    }
}
