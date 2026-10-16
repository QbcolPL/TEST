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
        public string PliStrategy { get; set; } = "Constant";
        public int ConstRateReliable { get; set; } = 0;
        public int ConstRateUnreliable { get; set; } = 1;
        public int DynRateMax { get; set; } = 2;
        public int DynRateMin { get; set; } = 20;
        public int StationaryReliable { get; set; } = 180;
        public int StationaryMesh { get; set; } = 30;
        public int TcpTimeout { get; set; } = 20;
        public bool DispatchCotExternal { get; set; } = true;
        public bool EnableMeshNonStreaming { get; set; } = true;
        public int ValidityHours { get; set; } = 0;

        public string MeshMode { get; set; } = "HYBRID";
        public string DeviceRole { get; set; } = "Auto";
        public string HardwareModel { get; set; } = "generic_meshtastic";
        public string LoraRegion { get; set; } = "EU_868";
        public string LoraModemPreset { get; set; } = "SHORT_FAST";
        public int HopLimit { get; set; } = 3;
        public bool EnablePli { get; set; } = true;
        public bool EnableGeoChat { get; set; } = true;
        public bool EnableOtsRelay { get; set; } = true;
        public bool EnableFileTransfer { get; set; } = false;
        public string ChannelName { get; set; } = "GGZS-TAK";
        public string ChannelUrl { get; set; } = "";
        public string Psk { get; set; } = TacticalConfigModel.GenerateRandomBase64Psk();

        public bool GatewayEnabled { get; set; } = false;
        public string GatewayProfile { get; set; } = "generic_meshtastic";
        public string GatewayMode { get; set; } = "ots_mqtt";
        public string GatewayProtocol { get; set; } = "mqtt_tls";
        public int GatewayPort { get; set; } = 8883;
        public string GatewayTopic { get; set; } = "opentakserver";
        public bool SecretsExternal { get; set; } = true;
        public string EnrollmentUri { get; set; } = "";
        public string OtsHost { get; set; } = "ggzstak.duckdns.org";
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
        public string DefaultChannelName { get; set; } = "GGZS-TAK";
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
        internal static readonly SolidColorBrush BrushGreenPlus = CreateFrozenBrush(34, 197, 94);
        internal static readonly SolidColorBrush BrushRedMinus = CreateFrozenBrush(239, 68, 68);

        internal static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        internal static SolidColorBrush BrushFromHex(string hex, Color fallback)
        {
            try { if (!string.IsNullOrWhiteSpace(hex)) { var c = (Color)ColorConverter.ConvertFromString(hex.Trim()); return CreateFrozenBrush(c.R, c.G, c.B); } } catch { }
            return CreateFrozenBrush(fallback.R, fallback.G, fallback.B);
        }

        private static readonly DependencyProperty IsHookedProperty =
            DependencyProperty.RegisterAttached("IsHooked", typeof(bool), typeof(TacticalConfigPanel), new PropertyMetadata(false));

        private static Style _darkComboBoxStyle, _darkComboBoxItemStyle;
        private static Button _injectedRefreshButton;
        private static FrameworkElement _factionsCardElement;
        private static WrapPanel _factionsWrapPanel;
        private static bool _syncingValidity;
        private static DispatcherTimer _previewTimer;
        private static readonly List<FactionProfileModel> _factions = new List<FactionProfileModel>();

        public static List<(string Name, string Channel, string ColorHex, string Psk)> GetConfiguredFactions()
        {
            EnsureDefaultFactions();
            return _factions.Select(f => (f.Name, f.ChannelName, f.ColorHex, f.Psk)).ToList();
        }

        private static void EnsureDefaultFactions()
        {
            if (_factions.Count > 0) return;
            _factions.Add(new FactionProfileModel { Name = "BLUFOR", ColorHex = "#2684FF", ChannelName = "GGZS-TAK" });
            _factions.Add(new FactionProfileModel { Name = "OPFOR", ColorHex = "#EB4034", ChannelName = "OPFOR-TAK" });
        }

        private const string DarkComboBoxXaml = @"
<Style xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" TargetType=""ComboBox"">
    <Setter Property=""Foreground"" Value=""#FFFFFF""/><Setter Property=""Background"" Value=""#081316""/><Setter Property=""BorderBrush"" Value=""#1E424A""/><Setter Property=""BorderThickness"" Value=""1""/><Setter Property=""Padding"" Value=""6,0,6,0""/>
    <Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""ComboBox"">
        <Grid SnapsToDevicePixels=""True"">
            <Border x:Name=""MainBorder"" Background=""{TemplateBinding Background}"" BorderBrush=""{TemplateBinding BorderBrush}"" BorderThickness=""{TemplateBinding BorderThickness}"" CornerRadius=""4"" />
            <ToggleButton Focusable=""False"" IsChecked=""{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}"" ClickMode=""Press"">
                <ToggleButton.Template><ControlTemplate TargetType=""ToggleButton""><Border Background=""Transparent""><Grid><Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""20""/></Grid.ColumnDefinitions><Path Grid.Column=""1"" HorizontalAlignment=""Center"" VerticalAlignment=""Center"" Data=""M 0 0 L 4 4 L 8 0"" Stroke=""#FFFFFF"" StrokeThickness=""1.5"" /></Grid></Border></ControlTemplate></ToggleButton.Template>
            </ToggleButton>
            <ContentPresenter IsHitTestVisible=""False"" Content=""{TemplateBinding SelectionBoxItem}"" Margin=""6,0,20,0"" VerticalAlignment=""Center"" HorizontalAlignment=""Left"" TextElement.Foreground=""#FFFFFF"" />
            <TextBlock x:Name=""FallbackText"" IsHitTestVisible=""False"" Text=""{Binding Text, RelativeSource={RelativeSource TemplatedParent}}"" Foreground=""#FFFFFF"" Margin=""6,0,20,0"" VerticalAlignment=""Center"" HorizontalAlignment=""Left"" Visibility=""Collapsed"" />
            <Popup Placement=""Bottom"" IsOpen=""{TemplateBinding IsDropDownOpen}"" AllowsTransparency=""True"" Focusable=""False"" PopupAnimation=""Slide"">
                <Grid SnapsToDevicePixels=""True"" MinWidth=""{TemplateBinding ActualWidth}"" MaxHeight=""{TemplateBinding MaxDropDownHeight}"">
                    <Border Background=""#0F2025"" BorderBrush=""#1FE0C5"" BorderThickness=""1"" CornerRadius=""4"" Margin=""0,2,0,0""><ScrollViewer Margin=""2""><StackPanel IsItemsHost=""True"" /></ScrollViewer></Border>
                </Grid>
            </Popup>
        </Grid>
        <ControlTemplate.Triggers>
            <Trigger Property=""IsMouseOver"" Value=""True""><Setter TargetName=""MainBorder"" Property=""BorderBrush"" Value=""#1FE0C5""/></Trigger>
            <Trigger Property=""SelectionBoxItem"" Value=""{x:Null}""><Setter TargetName=""FallbackText"" Property=""Visibility"" Value=""Visible""/></Trigger>
            <Trigger Property=""SelectionBoxItem"" Value=""""><Setter TargetName=""FallbackText"" Property=""Visibility"" Value=""Visible""/></Trigger>
        </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
</Style>";

        internal static void ApplyUnifiedComboBoxTheme(ComboBox cb, Window win)
        {
            if (cb == null) return;
            try
            {
                string preserved = cb.Text;
                cb.IsEditable = false; cb.IsReadOnly = false; cb.Foreground = Brushes.White; cb.Background = BrushInputBg; cb.BorderBrush = BrushInputBorder;
                if (_darkComboBoxStyle == null) { _darkComboBoxStyle = (Style)XamlReader.Parse(DarkComboBoxXaml); _darkComboBoxStyle.Seal(); }
                if (_darkComboBoxItemStyle == null)
                {
                    var s = new Style(typeof(ComboBoxItem));
                    s.Setters.Add(new Setter(Control.BackgroundProperty, BrushItemBg));
                    s.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
                    s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5)));
                    s.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
                    s.Seal(); _darkComboBoxItemStyle = s;
                }
                cb.Style = _darkComboBoxStyle; cb.ItemContainerStyle = _darkComboBoxItemStyle;
                foreach (var item in cb.Items) if (item is ComboBoxItem cbi) { cbi.Foreground = Brushes.White; cbi.Background = BrushItemBg; }
                if (cb.SelectedItem == null && !string.IsNullOrWhiteSpace(preserved))
                    foreach (var item in cb.Items)
                        if (string.Equals((item is ComboBoxItem ci) ? ci.Content?.ToString() : item?.ToString(), preserved, StringComparison.OrdinalIgnoreCase)) { cb.SelectedItem = item; break; }
                if (cb.SelectedItem == null && cb.Items.Count > 0) cb.SelectedIndex = 0;
            }
            catch { }
        }

        internal static void FindVisualChildren<T>(DependencyObject depObj, List<T> list) where T : DependencyObject
        {
            if (depObj == null) return;
            var visited = new HashSet<DependencyObject>();
            void Walk(DependencyObject cur)
            {
                if (cur == null || !visited.Add(cur)) return;
                if (cur is T match && !list.Contains(match)) list.Add(match);
                try { int vc = VisualTreeHelper.GetChildrenCount(cur); for (int i = 0; i < vc; i++) Walk(VisualTreeHelper.GetChild(cur, i)); } catch { }
                try { foreach (var lc in LogicalTreeHelper.GetChildren(cur)) if (lc is DependencyObject dc) Walk(dc); } catch { }
            }
            Walk(depObj);
        }

        private static DependencyObject GetAnyParent(DependencyObject child)
        {
            if (child == null) return null;
            try { var vp = VisualTreeHelper.GetParent(child); if (vp != null) return vp; } catch { }
            try { return LogicalTreeHelper.GetParent(child); } catch { return null; }
        }

        private static bool IsInsideInjectedFactions(DependencyObject el)
        {
            DependencyObject cur = el;
            while (cur != null)
            {
                if (cur is FrameworkElement fe && (fe.Tag?.ToString() == "INJECTED_FACTIONS" || fe.Tag?.ToString() == "FACTION_PANEL_CARD")) return true;
                cur = GetAnyParent(cur);
            }
            return false;
        }

        private static Border FindEnclosingCardBorder(DependencyObject startEl)
        {
            Border bestBorder = null;
            DependencyObject cur = GetAnyParent(startEl);
            while (cur != null)
            {
                if (cur is Border b)
                {
                    if (b.Tag?.ToString() == "INJECTED_FACTIONS" || b.Tag?.ToString() == "FACTION_PANEL_CARD" || b.Tag?.ToString() == "FTH_MASTER_SHELL") break;
                    var tbs = new List<TextBlock>(); FindVisualChildren(b, tbs);
                    if (tbs.Count >= 1 && tbs.Count <= 35) bestBorder = b;
                }
                cur = GetAnyParent(cur);
            }
            return bestBorder;
        }

        public static void AttachToMainWindow(Window win)
        {
            if (Application.Current == null || win == null) return;
            EnsureDefaultFactions();

            void Init()
            {
                try
                {
                    var combos = new List<ComboBox>();
                    FindVisualChildren(win, combos);
                    foreach (var c in combos) ApplyUnifiedComboBoxTheme(c, win);

                    RemoveStandaloneLegacyCardsAndInsertFactions(win);

                    var allBtns = new List<Button>();
                    FindVisualChildren(win, allBtns);
                    foreach (var btn in allBtns)
                    {
                        if (IsInsideInjectedFactions(btn) || (bool)btn.GetValue(IsHookedProperty)) continue;
                        string bTxt = (btn.Content?.ToString() ?? "").ToLowerInvariant();
                        if ((bTxt.Contains("ftak") || bTxt.Contains("generuj paczk") || bTxt.Contains("zbuduj paczk") || bTxt.Contains("build package")) && !bTxt.Contains("konfigurator"))
                        {
                            btn.SetValue(IsHookedProperty, true);
                            btn.Click += async (_, __) => await BuildDynamicFactionsAsync(win);
                        }
                    }

                    var allBorders = new List<Border>();
                    FindVisualChildren(win, allBorders);
                    Border previewBorder = allBorders.Where(b =>
                    {
                        var tbs = new List<TextBlock>(); FindVisualChildren(b, tbs);
                        return tbs.Count >= 8 && tbs.Count <= 25 &&
                               tbs.Any(t => t.Text != null && t.Text.StartsWith("Pliki paczki", StringComparison.OrdinalIgnoreCase)) &&
                               tbs.Any(t => t.Text != null && t.Text.StartsWith("Mapy", StringComparison.OrdinalIgnoreCase));
                    }).OrderBy(b => { var l = new List<TextBlock>(); FindVisualChildren(b, l); return l.Count; }).FirstOrDefault();

                    if (previewBorder != null && GetAnyParent(previewBorder) is Panel previewParent && _injectedRefreshButton == null)
                    {
                        _injectedRefreshButton = new Button
                        {
                            Content = "Odśwież / Skalkuluj zawartość (folder source)",
                            Height = 32,
                            Margin = new Thickness(0, 8, 0, 14),
                            Background = BrushButtonAccent,
                            Foreground = BrushDarkFg,
                            FontWeight = FontWeights.Bold,
                            Cursor = Cursors.Hand,
                            BorderThickness = new Thickness(0),
                            Tag = "INJECTED_BTN"
                        };
                        Grid.SetRow(_injectedRefreshButton, Grid.GetRow(previewBorder));
                        Grid.SetColumn(_injectedRefreshButton, Grid.GetColumn(previewBorder));
                        Grid.SetColumnSpan(_injectedRefreshButton, Grid.GetColumnSpan(previewBorder));

                        _injectedRefreshButton.Click += async (_, __) =>
                        {
                            _injectedRefreshButton.IsEnabled = false;
                            var result = await ForceSizeUpdateAsync(win);
                            _injectedRefreshButton.Content = $"Odśwież / Skalkuluj (Plików: {result.FileCount} | {FormatBytes(result.TotalBytes)})";
                            _injectedRefreshButton.IsEnabled = true;
                        };
                        int idx = previewParent.Children.IndexOf(previewBorder);
                        previewParent.Children.Insert(idx + 1, _injectedRefreshButton);
                    }

                    TacticalMapGeneratorView.AttachFullScreenTab(win);
                    HookNativeValidityControls(win);
                }
                catch { }
            }

            Init();

            if (!(bool)win.GetValue(IsHookedProperty))
            {
                win.SetValue(IsHookedProperty, true);
                win.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((_, __) => ApplyIndefiniteValidityBeforeAction(win)), true);

                _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1400) };
                _previewTimer.Tick += async (_, __) =>
                {
                    try
                    {
                        RemoveStandaloneLegacyCardsAndInsertFactions(win);
                        string srcDir = GetPrimarySourceDirectory(win);
                        if (!string.IsNullOrEmpty(srcDir) && Directory.Exists(srcDir))
                        {
                            var st = await Task.Run(() => CalculateSourceFolderStats(srcDir));
                            UpdatePreviewCardLabels(win, st);
                            if (_injectedRefreshButton != null && _injectedRefreshButton.IsEnabled)
                                _injectedRefreshButton.Content = $"Odśwież / Skalkuluj (Plików: {st.FileCount} | {FormatBytes(st.TotalBytes)})";
                        }
                    }
                    catch { }
                };
                _previewTimer.Start();

                win.Loaded += async (_, __) =>
                {
                    Init();
                    var res = await ForceSizeUpdateAsync(win);
                    if (_injectedRefreshButton != null)
                        _injectedRefreshButton.Content = $"Odśwież / Skalkuluj (Plików: {res.FileCount} | {FormatBytes(res.TotalBytes)})";
                };
            }
        }

        private static void RemoveStandaloneLegacyCardsAndInsertFactions(Window win)
        {
            var tbs = new List<TextBlock>();
            FindVisualChildren(win, tbs);

            foreach (var tb in tbs)
            {
                if (tb.Text == null || IsInsideInjectedFactions(tb)) continue;
                string t = tb.Text.Trim();

                bool isMeshCard = t.Contains("Konfiguracja Meshtastic", StringComparison.OrdinalIgnoreCase) ||
                                  t.Contains("HYBRID · PLI", StringComparison.OrdinalIgnoreCase) ||
                                  t.Contains("Channel URL (opcjonalny)", StringComparison.OrdinalIgnoreCase);

                bool isGatewayOrEnrollCard = t.Contains("Gateway Meshtastic", StringComparison.OrdinalIgnoreCase) ||
                                             t.Contains("Enrollment OpenTAK", StringComparison.OrdinalIgnoreCase) ||
                                             t.Contains("krótkotrwały URI enrollmentu", StringComparison.OrdinalIgnoreCase) ||
                                             t.Contains("wdrożenie jednym", StringComparison.OrdinalIgnoreCase);

                if (isMeshCard || isGatewayOrEnrollCard)
                {
                    Border cardBorder = FindEnclosingCardBorder(tb);
                    if (cardBorder != null)
                    {
                        if (isMeshCard && _factionsCardElement == null)
                        {
                            Panel targetPanel = GetAnyParent(cardBorder) as Panel;
                            FrameworkElement refElement = cardBorder;

                            if (targetPanel is Grid && GetAnyParent(targetPanel) is StackPanel outerSp)
                            {
                                refElement = targetPanel;
                                targetPanel = outerSp;
                            }

                            if (targetPanel != null)
                            {
                                _factionsCardElement = (FrameworkElement)BuildWrappedFactionsSection(win);
                                Grid.SetRow(_factionsCardElement, Grid.GetRow(refElement));
                                Grid.SetColumn(_factionsCardElement, 0);
                                Grid.SetColumnSpan(_factionsCardElement, 10);

                                int idx = targetPanel.Children.IndexOf(refElement);
                                targetPanel.Children.Insert(Math.Max(0, idx), _factionsCardElement);
                            }
                        }

                        cardBorder.Visibility = Visibility.Collapsed;
                        if (GetAnyParent(cardBorder) is Panel p && p is not StackPanel && p.Children.Count <= 3)
                        {
                            bool allHidden = true;
                            foreach (UIElement ch in p.Children)
                                if (ch.Visibility == Visibility.Visible && ch != _factionsCardElement) allHidden = false;
                            if (allHidden && !p.Children.Contains(_factionsCardElement))
                                p.Visibility = Visibility.Collapsed;
                        }
                    }
                }
            }

            if (_factionsCardElement != null)
            {
                DependencyObject cur = _factionsCardElement;
                while (cur != null && cur != win)
                {
                    if (cur is FrameworkElement fe)
                    {
                        fe.HorizontalAlignment = HorizontalAlignment.Stretch;
                        if (GetAnyParent(fe) is Grid g && g.ColumnDefinitions.Count > 1 && Grid.GetColumn(fe) == 0)
                            Grid.SetColumnSpan(fe, g.ColumnDefinitions.Count);
                    }
                    cur = GetAnyParent(cur);
                }
            }
        }

        public static async Task RefreshSourceSizeFromExternalAsync(Window win)
        {
            var res = await ForceSizeUpdateAsync(win);
            if (_injectedRefreshButton != null)
                _injectedRefreshButton.Content = $"Odśwież / Skalkuluj (Plików: {res.FileCount} | {FormatBytes(res.TotalBytes)})";
        }

        private static UIElement BuildWrappedFactionsSection(Window win)
        {
            EnsureDefaultFactions();
            var wrapperStack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 14),
                Tag = "INJECTED_FACTIONS"
            };

            _factionsWrapPanel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            _factionsWrapPanel.SizeChanged += (_, e) => AdjustFactionCardsWidth(e.NewSize.Width);

            wrapperStack.Children.Add(_factionsWrapPanel);
            RebuildWrappedFactionPanels(win);
            return wrapperStack;
        }

        private static void AdjustFactionCardsWidth(double availableWidth)
        {
            if (_factionsWrapPanel == null || availableWidth <= 100) return;
            int columnsPerRow = availableWidth >= 1140 ? 3 : (availableWidth >= 700 ? 2 : 1);
            double cardWidth = Math.Clamp(Math.Floor((availableWidth - (columnsPerRow * 14.0)) / columnsPerRow), 320.0, 540.0);
            foreach (UIElement child in _factionsWrapPanel.Children)
                if (child is FrameworkElement fe) fe.Width = cardWidth;
        }

        private static void RebuildWrappedFactionPanels(Window win)
        {
            if (_factionsWrapPanel == null) return;
            EnsureDefaultFactions();
            _factionsWrapPanel.Children.Clear();
            for (int i = 0; i < _factions.Count; i++)
                _factionsWrapPanel.Children.Add(BuildSingleFactionCard(win, _factions[i], i));
            SyncActiveFactionToDefaultModel();
            if (_factionsWrapPanel.ActualWidth > 100)
                AdjustFactionCardsWidth(_factionsWrapPanel.ActualWidth);
        }

        private static Border CreateCornerIconButton(string symbol, Brush bgBrush, bool enabled, string tooltip, Action onClick)
        {
            var border = new Border
            {
                Width = 32,
                Height = 28,
                CornerRadius = new CornerRadius(6),
                Background = enabled ? bgBrush : BrushSwitchOff,
                BorderBrush = enabled ? Brushes.White : BrushTileBorder,
                BorderThickness = new Thickness(1),
                Cursor = enabled ? Cursors.Hand : Cursors.No,
                ToolTip = tooltip,
                Child = new TextBlock
                {
                    Text = symbol,
                    Foreground = Brushes.White,
                    FontSize = 17,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, -2, 0, 0)
                }
            };
            if (enabled) border.MouseLeftButtonDown += (_, e) => { e.Handled = true; onClick(); };
            return border;
        }

        private static UIElement BuildSingleFactionCard(Window win, FactionProfileModel fac, int index)
        {
            var outerCard = new Border
            {
                Width = 385,
                Background = BrushCardBg,
                BorderBrush = BrushAccent,
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 12, 12),
                Tag = "FACTION_PANEL_CARD"
            };

            var mainStack = new StackPanel();

            var topCornerBar = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            topCornerBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            topCornerBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topCornerBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            string[] palette = { "#F5B82E", "#2ECC71", "#9B59B6", "#E67E22", "#1ABC9C", "#E84393" };
            var btnPlus = CreateCornerIconButton("+", BrushGreenPlus, true, "Dodaj nową stronę konfliktu (otwiera identyczny panel po prawej stronie, a kolejne poniżej)", () =>
            {
                int nextNum = _factions.Count + 1;
                string newName = nextNum == 3 ? "ORGA" : $"STRONA-{nextNum}";
                string newCh = nextNum == 3 ? "ORGA-HQ" : $"TAK-CH{nextNum}";
                string hex = palette[(nextNum - 1) % palette.Length];
                _factions.Insert(index + 1, new FactionProfileModel
                {
                    Name = newName,
                    ColorHex = hex,
                    ChannelName = newCh,
                    Psk = TacticalConfigModel.GenerateRandomBase64Psk(),
                    PliStrategy = fac.PliStrategy,
                    LoraRegion = fac.LoraRegion,
                    LoraModemPreset = fac.LoraModemPreset,
                    OtsHost = fac.OtsHost
                });
                RebuildWrappedFactionPanels(win);
            });
            Grid.SetColumn(btnPlus, 0);
            topCornerBar.Children.Add(btnPlus);

            var centerStack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var headerDot = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = BrushFromHex(fac.ColorHex, Color.FromRgb(38, 132, 255)), BorderBrush = Brushes.White, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
            var headerTitle = new TextBlock { Text = $"Generator Stron — {fac.Name}", Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            centerStack.Children.Add(headerDot);
            centerStack.Children.Add(headerTitle);
            Grid.SetColumn(centerStack, 1);
            topCornerBar.Children.Add(centerStack);

            bool canRemove = _factions.Count > 1;
            var btnMinus = CreateCornerIconButton("−", BrushRedMinus, canRemove, canRemove ? $"Usuń stronę {fac.Name}" : "Wymagana jest minimum 1 strona konfliktu", () =>
            {
                if (_factions.Count <= 1) return;
                _factions.RemoveAt(index);
                RebuildWrappedFactionPanels(win);
            });
            Grid.SetColumn(btnMinus, 2);
            topCornerBar.Children.Add(btnMinus);
            mainStack.Children.Add(topCornerBar);

            var idCard = new Border { Background = BrushItemBg, BorderBrush = BrushTileBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 8) };
            var idGrid = new UniformGrid { Columns = 3 };

            var nameBox = (TextBox)TextCtrl(fac.Name, val =>
            {
                fac.Name = string.IsNullOrWhiteSpace(val) ? "STRONA" : val.Trim();
                headerTitle.Text = $"Generator Stron — {fac.Name}";
                SyncActiveFactionToDefaultModel();
            });
            nameBox.FontWeight = FontWeights.Bold;
            idGrid.Children.Add(FieldBox("Nazwa strony:", nameBox));

            var swatch = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = BrushFromHex(fac.ColorHex, Color.FromRgb(38, 132, 255)), BorderBrush = Brushes.White, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
            var cLbl = new TextBlock { Text = fac.ColorHex.ToUpperInvariant(), Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var cStack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            cStack.Children.Add(swatch); cStack.Children.Add(cLbl);
            var colorBtn = new Button { Content = cStack, Height = 26, Background = BrushInputBg, BorderBrush = BrushAccent, BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
            colorBtn.Click += (_, __) =>
            {
                string picked = ShowRgbColorPickerDialog(win, fac.Name, fac.ColorHex);
                if (!string.IsNullOrEmpty(picked))
                {
                    fac.ColorHex = picked;
                    swatch.Background = BrushFromHex(picked, Color.FromRgb(38, 132, 255));
                    headerDot.Background = BrushFromHex(picked, Color.FromRgb(38, 132, 255));
                    cLbl.Text = picked.ToUpperInvariant();
                    SyncActiveFactionToDefaultModel();
                }
            };
            idGrid.Children.Add(FieldBox("Kolor (RGB):", colorBtn));

            var btnKml = new Button { Content = $"📁 factions/{SanitizeFileName(fac.Name).ToLowerInvariant()}", Height = 26, Background = BrushInputBg, Foreground = Brushes.White, BorderBrush = BrushInputBorder, BorderThickness = new Thickness(1), FontSize = 9.5, Cursor = Cursors.Hand };
            btnKml.Click += async (_, __) =>
            {
                string srcDir = GetPrimarySourceDirectory(win);
                string sl = SanitizeFileName(fac.Name).ToLowerInvariant();
                await Task.Run(() => EnsureFactionFoldersInitialized(srcDir, new List<(string, string)> { (sl, fac.ColorHex) }));
                TryOpenFolderInExplorer(Path.Combine(srcDir, "factions", sl));
            };
            idGrid.Children.Add(FieldBox("Nakładki KML:", btnKml));
            idCard.Child = idGrid;
            mainStack.Children.Add(idCard);

            var atakCard = new Border { Background = BrushItemBg, BorderBrush = BrushTileBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(9), Margin = new Thickness(0, 0, 0, 8) };
            var atakStack = new StackPanel();
            atakStack.Children.Add(new TextBlock { Text = "Konfiguracja raportowania ATAK-CIV (PLI, sieć i ważność)", Foreground = BrushAccent, FontWeight = FontWeights.Bold, FontSize = 11.5 });
            atakStack.Children.Add(new TextBlock { Text = "Częstotliwości wysyłania pozycji GPS/PLI oraz ważność paczki/QR (0 = bezterminowo).", Foreground = BrushMutedText, FontSize = 9.5, Margin = new Thickness(0, 1, 0, 5) });

            var atakRow1 = new UniformGrid { Columns = 3 };
            atakRow1.Children.Add(FieldBox("Strategia PLI", ComboCtrl(new[] { "Constant", "Dynamic" }, fac.PliStrategy, win, v => { fac.PliStrategy = v; Current.LocationReportingStrategy = v; })));
            atakRow1.Children.Add(FieldBox("Const Rel/LTE (s)", IntCtrl(fac.ConstRateReliable, v => { fac.ConstRateReliable = v; Current.ConstantReportingRateReliable = v; })));
            atakRow1.Children.Add(FieldBox("Const Mesh (s)", IntCtrl(fac.ConstRateUnreliable, v => { fac.ConstRateUnreliable = v; Current.ConstantReportingRateUnreliable = v; })));
            atakRow1.Children.Add(FieldBox("Dynamic Max (s)", IntCtrl(fac.DynRateMax, v => { fac.DynRateMax = v; Current.DynamicReportingRateMaxReliable = v; })));
            atakRow1.Children.Add(FieldBox("Dynamic Min (s)", IntCtrl(fac.DynRateMin, v => { fac.DynRateMin = v; Current.DynamicReportingRateMinReliable = v; })));
            atakRow1.Children.Add(FieldBox("Postój LTE (s)", IntCtrl(fac.StationaryReliable, v => { fac.StationaryReliable = v; Current.DynamicReportingRateStationaryReliable = v; })));
            atakRow1.Children.Add(FieldBox("Postój Mesh (s)", IntCtrl(fac.StationaryMesh, v => { fac.StationaryMesh = v; Current.DynamicReportingRateStationaryUnreliable = v; })));
            atakRow1.Children.Add(FieldBox("TCP Timeout (s)", IntCtrl(fac.TcpTimeout, v => { fac.TcpTimeout = v; Current.TcpConnectTimeout = v; })));
            atakRow1.Children.Add(FieldBox("Ważność (0=bezterm.)", IntCtrl(fac.ValidityHours, v => { fac.ValidityHours = v; Current.PackageValidityHours = v; Current.QrValidityHours = v; })));
            atakStack.Children.Add(atakRow1);

            CheckBox MakeChk(string label, bool init, Action<bool> onChg)
            {
                var c = new CheckBox { Content = label, IsChecked = init, Foreground = Brushes.White, FontSize = 9.5, Margin = new Thickness(0, 2, 6, 2) };
                c.Checked += (_, __) => onChg(true); c.Unchecked += (_, __) => onChg(false);
                return c;
            }
            var atakFlags = new UniformGrid { Columns = 2, Margin = new Thickness(0, 2, 0, 0) };
            atakFlags.Children.Add(MakeChk("Wysyłaj pozycję CoT na zewnątrz", fac.DispatchCotExternal, v => { fac.DispatchCotExternal = v; Current.DispatchLocationCotExternal = v; }));
            atakFlags.Children.Add(MakeChk("Połączenia bezstrumieniowe Mesh", fac.EnableMeshNonStreaming, v => { fac.EnableMeshNonStreaming = v; Current.EnableNonStreamingConnections = v; }));
            atakStack.Children.Add(atakFlags);
            atakCard.Child = atakStack;
            mainStack.Children.Add(atakCard);

            var meshCard = new Border { Background = BrushItemBg, BorderBrush = BrushTileBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(9), Margin = new Thickness(0, 0, 0, 8) };
            var meshStack = new StackPanel();
            meshStack.Children.Add(new TextBlock { Text = "Konfiguracja Meshtastic", Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 11.5 });
            meshStack.Children.Add(new TextBlock { Text = "HYBRID · PLI · GeoChat · OTS Relay", Foreground = BrushMutedText, FontSize = 9.5, Margin = new Thickness(0, 1, 0, 5) });

            var meshRow1 = new UniformGrid { Columns = 3 };
            meshRow1.Children.Add(FieldBox("Tryb", ComboCtrl(new[] { "HYBRID", "PLUGIN_ONLY", "NATIVE_TAK", "OFF" }, fac.MeshMode, win, v => fac.MeshMode = v)));
            meshRow1.Children.Add(FieldBox("Rola urządzenia", ComboCtrl(new[] { "Auto", "TAK", "TAK_Tracker", "Client", "Router" }, fac.DeviceRole, win, v => { fac.DeviceRole = v; Current.DeviceRole = v; })));
            meshRow1.Children.Add(FieldBox("Model", ComboCtrl(new[] { "generic_meshtastic", "tbeam", "heltec_v3", "meshtracker_x1", "rak4631" }, fac.HardwareModel, win, v => { fac.HardwareModel = v; Current.HardwareModel = v; })));
            meshStack.Children.Add(meshRow1);

            var meshRow2 = new UniformGrid { Columns = 3 };
            meshRow2.Children.Add(FieldBox("Region", ComboCtrl(new[] { "EU_868", "US_915", "EU_433", "ANZ_915" }, fac.LoraRegion, win, v => { fac.LoraRegion = v; Current.LoraRegion = v; })));
            meshRow2.Children.Add(FieldBox("Modem preset", ComboCtrl(new[] { "SHORT_FAST", "LONG_FAST", "MEDIUM_FAST", "MEDIUM_SLOW" }, fac.LoraModemPreset, win, v => { fac.LoraModemPreset = v; Current.LoraModemPreset = v; })));
            meshRow2.Children.Add(FieldBox("Hop limit", IntCtrl(fac.HopLimit, v => fac.HopLimit = v)));
            meshStack.Children.Add(meshRow2);

            var flagsGrid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 2, 0, 4) };
            flagsGrid.Children.Add(MakeChk("PLI", fac.EnablePli, v => fac.EnablePli = v));
            flagsGrid.Children.Add(MakeChk("GeoChat", fac.EnableGeoChat, v => fac.EnableGeoChat = v));
            flagsGrid.Children.Add(MakeChk("OTS Relay", fac.EnableOtsRelay, v => fac.EnableOtsRelay = v));
            flagsGrid.Children.Add(MakeChk("File Transfer", fac.EnableFileTransfer, v => fac.EnableFileTransfer = v));
            meshStack.Children.Add(flagsGrid);

            var meshRow3 = new UniformGrid { Columns = 2 };
            var chNameBox = (TextBox)TextCtrl(fac.ChannelName, v => { fac.ChannelName = v.Trim(); SyncActiveFactionToDefaultModel(); });
            var chUrlBox = (TextBox)TextCtrl(fac.ChannelUrl, v => fac.ChannelUrl = v.Trim());
            meshRow3.Children.Add(FieldBox("Nazwa kanału", chNameBox));
            meshRow3.Children.Add(FieldBox("Channel URL (opcjonalny)", chUrlBox));
            meshStack.Children.Add(meshRow3);

            var pskGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            pskGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pskGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            var pskBox = (TextBox)TextCtrl(fac.Psk, v => { fac.Psk = v.Trim(); SyncActiveFactionToDefaultModel(); });
            pskBox.FontFamily = new FontFamily("Consolas");
            var fPsk = FieldBox("Klucz szyfrujący PSK (AES-256 Base64):", pskBox);
            Grid.SetColumn(fPsk, 0); pskGrid.Children.Add(fPsk);
            var btnRoll = new Button { Content = "🎲 Losuj PSK", Height = 26, Margin = new Thickness(0, 14, 0, 5), Background = BrushInputBg, Foreground = BrushAccent, BorderBrush = BrushAccent, BorderThickness = new Thickness(1), FontSize = 9.5, FontWeight = FontWeights.Bold, Cursor = Cursors.Hand };
            btnRoll.Click += (_, __) => { fac.Psk = TacticalConfigModel.GenerateRandomBase64Psk(); pskBox.Text = fac.Psk; SyncActiveFactionToDefaultModel(); };
            Grid.SetColumn(btnRoll, 1); pskGrid.Children.Add(btnRoll);
            meshStack.Children.Add(pskGrid);

            var meshStatusTxt = new TextBlock { Text = "Gotowy do zapisu plików lub budowy paczek .ftak i kodów QR.", Foreground = BrushMutedText, FontSize = 9, Margin = new Thickness(0, 0, 0, 4) };
            meshStack.Children.Add(meshStatusTxt);

            Button SmallActBtn(string cap, Action act)
            {
                var b = new Button { Content = cap, Height = 24, Margin = new Thickness(0, 0, 4, 0), Background = BrushInputBg, Foreground = Brushes.White, BorderBrush = BrushInputBorder, BorderThickness = new Thickness(1), FontSize = 9.5, Cursor = Cursors.Hand };
                b.Click += (_, __) => act();
                return b;
            }
            var meshBtnBar = new UniformGrid { Columns = 4 };
            meshBtnBar.Children.Add(SmallActBtn("Save", () => { WriteConfigsToDirectory(GetPrimarySourceDirectory(win), fac); meshStatusTxt.Text = $"✓ Zapisano konfigurację {fac.Name}."; meshStatusTxt.Foreground = BrushAccent; }));
            meshBtnBar.Children.Add(SmallActBtn("Open", () => TryOpenFolderInExplorer(GetPrimarySourceDirectory(win))));
            meshBtnBar.Children.Add(SmallActBtn("Copy", () => { try { Clipboard.SetText(!string.IsNullOrWhiteSpace(fac.ChannelUrl) ? fac.ChannelUrl : fac.Psk); meshStatusTxt.Text = "✓ Skopiowano do schowka."; } catch { } }));
            meshBtnBar.Children.Add(SmallActBtn("Clear", () => { chUrlBox.Text = ""; fac.ChannelUrl = ""; }));
            meshStack.Children.Add(meshBtnBar);
            meshCard.Child = meshStack;
            mainStack.Children.Add(meshCard);

            var bottomSplitGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 8) };

            var gwCard = new Border { Background = BrushItemBg, BorderBrush = BrushTileBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(8), Margin = new Thickness(0, 0, 4, 0) };
            var gwStack = new StackPanel();
            gwStack.Children.Add(new TextBlock { Text = "Gateway Meshtastic ↔ OTS", Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 10.5, Margin = new Thickness(0, 0, 0, 4) });
            gwStack.Children.Add(MakeChk("Gateway enabled", fac.GatewayEnabled, v => fac.GatewayEnabled = v));

            var gwRow1 = new UniformGrid { Columns = 2, Margin = new Thickness(0, 2, 0, 4) };
            gwRow1.Children.Add(TextCtrl(fac.GatewayProfile, v => fac.GatewayProfile = v));
            gwRow1.Children.Add(TextCtrl(fac.GatewayMode, v => fac.GatewayMode = v));
            gwStack.Children.Add(gwRow1);

            var gwRow2 = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            gwRow2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gwRow2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            gwRow2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
            var gwProto = TextCtrl(fac.GatewayProtocol, v => fac.GatewayProtocol = v);
            var gwPort = IntCtrl(fac.GatewayPort, v => fac.GatewayPort = v);
            var gwTopic = TextCtrl(fac.GatewayTopic, v => fac.GatewayTopic = v);
            Grid.SetColumn(gwProto, 0); Grid.SetColumn(gwPort, 1); Grid.SetColumn(gwTopic, 2);
            gwRow2.Children.Add(gwProto); gwRow2.Children.Add(gwPort); gwRow2.Children.Add(gwTopic);
            gwStack.Children.Add(gwRow2);
            gwStack.Children.Add(MakeChk("Secrets external", fac.SecretsExternal, v => fac.SecretsExternal = v));
            gwCard.Child = gwStack;
            bottomSplitGrid.Children.Add(gwCard);

            var enrCard = new Border { Background = BrushItemBg, BorderBrush = BrushTileBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(8), Margin = new Thickness(4, 0, 0, 0) };
            var enrStack = new StackPanel();
            enrStack.Children.Add(new TextBlock { Text = "Enrollment OpenTAK (1-skan)", Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 10.5, Margin = new Thickness(0, 0, 0, 2) });
            enrStack.Children.Add(new TextBlock { Text = "Wklej krótkotrwały URI enrollmentu OpenTAK.", Foreground = BrushMutedText, FontSize = 9, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });

            var enrUriBox = new TextBox { Text = fac.EnrollmentUri, Height = 44, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Background = BrushInputBg, Foreground = Brushes.White, BorderBrush = BrushInputBorder, BorderThickness = new Thickness(1), FontSize = 9.5, Padding = new Thickness(4), Margin = new Thickness(0, 0, 0, 6) };
            enrUriBox.TextChanged += (_, __) => fac.EnrollmentUri = enrUriBox.Text.Trim();
            enrStack.Children.Add(enrUriBox);

            var enrBtnBar = new UniformGrid { Columns = 2 };
            enrBtnBar.Children.Add(SmallActBtn("Save enrollment", () => WriteConfigsToDirectory(GetPrimarySourceDirectory(win), fac)));
            enrBtnBar.Children.Add(SmallActBtn("Clear", () => { enrUriBox.Text = ""; fac.EnrollmentUri = ""; }));
            enrStack.Children.Add(enrBtnBar);
            enrCard.Child = enrStack;
            bottomSplitGrid.Children.Add(enrCard);
            mainStack.Children.Add(bottomSplitGrid);

            var bottomBtnsGrid = new UniformGrid { Columns = 3 };
            var btnFolders = new Button { Content = "📁 Pliki stron", Height = 28, Margin = new Thickness(0, 0, 3, 0), Background = BrushItemBg, Foreground = Brushes.White, BorderBrush = BrushTileBorder, BorderThickness = new Thickness(1), FontSize = 10, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand };
            var btnPoster = new Button { Content = "🖨️ Plakat + QR", Height = 28, Margin = new Thickness(2, 0, 2, 0), Background = BrushItemBg, Foreground = Brushes.White, BorderBrush = BrushAccent, BorderThickness = new Thickness(1), FontSize = 10, FontWeight = FontWeights.Bold, Cursor = Cursors.Hand };
            var btnBuildAll = new Button { Content = "⚡ Buduj strony (.ftak)", Height = 28, Margin = new Thickness(3, 0, 0, 0), Background = BrushButtonAccent, Foreground = BrushDarkFg, BorderThickness = new Thickness(0), FontSize = 10, FontWeight = FontWeights.Bold, Cursor = Cursors.Hand };

            btnFolders.Click += async (_, __) =>
            {
                string srcDir = GetPrimarySourceDirectory(win);
                var sides = _factions.Select(f => (SanitizeFileName(f.Name).ToLowerInvariant(), f.ColorHex)).ToList();
                await Task.Run(() => { EnsureFactionFoldersInitialized(srcDir, sides); WriteConfigsToDirectory(srcDir, fac); });
                await ForceSizeUpdateAsync(win);
                meshStatusTxt.Text = $"✓ Przygotowano pliki konfiguracyjne i podfoldery KML dla {_factions.Count} stron.";
                meshStatusTxt.Foreground = BrushAccent;
            };
            btnPoster.Click += async (_, __) => { btnPoster.IsEnabled = false; await ExportSafezonePosterForFactionAsync(win, fac); btnPoster.IsEnabled = true; };
            btnBuildAll.Click += async (_, __) => { btnBuildAll.IsEnabled = false; await BuildDynamicFactionsAsync(win); btnBuildAll.IsEnabled = true; };

            bottomBtnsGrid.Children.Add(btnFolders); bottomBtnsGrid.Children.Add(btnPoster); bottomBtnsGrid.Children.Add(btnBuildAll);
            mainStack.Children.Add(bottomBtnsGrid);

            outerCard.Child = mainStack;
            return outerCard;
        }

        internal static string ShowRgbColorPickerDialog(Window owner, string factionName, string initialHex)
        {
            Color initColor = Color.FromRgb(38, 132, 255);
            try { initColor = (Color)ColorConverter.ConvertFromString(initialHex); } catch { }
            byte r = initColor.R, g = initColor.G, b = initColor.B;
            string selectedHex = $"#{r:X2}{g:X2}{b:X2}"; bool confirmed = false;

            var dlg = new Window { Title = $"Paleta RGB — {factionName}", Width = 410, Height = 360, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = owner, Background = BrushCardBg, Foreground = Brushes.White, ResizeMode = ResizeMode.NoResize, WindowStyle = WindowStyle.ToolWindow };
            var root = new StackPanel { Margin = new Thickness(16) };
            var previewBorder = new Border { Height = 38, CornerRadius = new CornerRadius(6), Background = CreateFrozenBrush(r, g, b), BorderBrush = BrushAccent, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 10) };
            var previewText = new TextBlock { Text = $"{factionName} ({selectedHex})", Foreground = Brushes.White, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            previewBorder.Child = previewText; root.Children.Add(previewBorder);

            var swatchesWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            string[] presets = { "#2684FF", "#EB4034", "#F5B82E", "#2ECC71", "#9B59B6", "#1FE0C5", "#E67E22", "#E84393", "#00CEC9", "#FFFFFF" };
            Slider sR = null, sG = null, sB = null; TextBox hexBox = null; bool updating = false;

            void UpdateFromRgb()
            {
                if (updating) return; updating = true;
                selectedHex = $"#{r:X2}{g:X2}{b:X2}";
                previewBorder.Background = CreateFrozenBrush(r, g, b); previewText.Text = $"{factionName} ({selectedHex})";
                if (sR != null) sR.Value = r; if (sG != null) sG.Value = g; if (sB != null) sB.Value = b;
                if (hexBox != null) hexBox.Text = selectedHex;
                updating = false;
            }

            foreach (var pHex in presets)
            {
                var swBtn = new Button { Width = 28, Height = 22, Margin = new Thickness(0, 0, 6, 4), Background = BrushFromHex(pHex, initColor), BorderBrush = Brushes.White, BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
                string cap = pHex;
                swBtn.Click += (_, __) => { try { var c = (Color)ColorConverter.ConvertFromString(cap); r = c.R; g = c.G; b = c.B; UpdateFromRgb(); } catch { } };
                swatchesWrap.Children.Add(swBtn);
            }
            root.Children.Add(swatchesWrap);

            UIElement MakeSlider(string label, byte initVal, out Slider slOut, Action<byte> onVal)
            {
                var rg = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                rg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
                rg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
                var lbl = new TextBlock { Text = label, Foreground = Brushes.White, FontSize = 11 };
                var valTxt = new TextBlock { Text = initVal.ToString(), Foreground = BrushAccent, FontSize = 11, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right };
                var sl = new Slider { Minimum = 0, Maximum = 255, Value = initVal };
                sl.ValueChanged += (_, e) => { byte v = (byte)Math.Clamp((int)Math.Round(e.NewValue), 0, 255); valTxt.Text = v.ToString(); onVal(v); UpdateFromRgb(); };
                slOut = sl; Grid.SetColumn(lbl, 0); Grid.SetColumn(sl, 1); Grid.SetColumn(valTxt, 2);
                rg.Children.Add(lbl); rg.Children.Add(sl); rg.Children.Add(valTxt);
                return rg;
            }
            root.Children.Add(MakeSlider("Red (R):", r, out sR, v => r = v));
            root.Children.Add(MakeSlider("Green (G):", g, out sG, v => g = v));
            root.Children.Add(MakeSlider("Blue (B):", b, out sB, v => b = v));

            hexBox = (TextBox)TextCtrl(selectedHex, txt =>
            {
                if (updating) return;
                try { var c = (Color)ColorConverter.ConvertFromString(txt.Trim()); r = c.R; g = c.G; b = c.B; UpdateFromRgb(); } catch { }
            });
            root.Children.Add(FieldBox("Kod koloru #HEX:", hexBox));

            var btnBar = new UniformGrid { Columns = 2, Margin = new Thickness(0, 6, 0, 0) };
            var btnCancel = new Button { Content = "Anuluj", Height = 28, Margin = new Thickness(0, 0, 6, 0), Background = BrushItemBg, Foreground = Brushes.White, BorderBrush = BrushTileBorder, BorderThickness = new Thickness(1) };
            var btnOk = new Button { Content = "✓ Zastosuj RGB", Height = 28, Margin = new Thickness(6, 0, 0, 0), Background = BrushButtonAccent, Foreground = BrushDarkFg, FontWeight = FontWeights.Bold, BorderThickness = new Thickness(0) };
            btnCancel.Click += (_, __) => dlg.Close(); btnOk.Click += (_, __) => { confirmed = true; dlg.Close(); };
            btnBar.Children.Add(btnCancel); btnBar.Children.Add(btnOk);
            root.Children.Add(btnBar);
            dlg.Content = root; dlg.ShowDialog();
            return confirmed ? selectedHex : null;
        }

        private static void SyncActiveFactionToDefaultModel()
        {
            EnsureDefaultFactions();
            var active = _factions[0];
            Current.ActiveFaction = string.IsNullOrWhiteSpace(active.Name) ? "BLUFOR" : active.Name.Trim();
            Current.DefaultChannelName = string.IsNullOrWhiteSpace(active.ChannelName) ? "GGZS-TAK" : active.ChannelName.Trim();
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
                string outDir = Path.Combine(parentDir, "release_factions"); Directory.CreateDirectory(outDir);

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
                        using var sha = SHA256.Create(); using var stream = File.OpenRead(ftakPath);
                        return Convert.ToHexString(sha.ComputeHash(stream));
                    });
                    string qrPayload = !string.IsNullOrWhiteSpace(fac.EnrollmentUri) ? fac.EnrollmentUri : $"ftak://provision?event={Uri.EscapeDataString(eventName)}&faction={Uri.EscapeDataString(cleanName)}&channel={Uri.EscapeDataString(fac.ChannelName)}&sha256={sha256.Substring(0, 16)}";
                    RenderStandaloneQrPng(Path.Combine(outDir, $"QR_{eventName}_{cleanName}.png"), qrPayload);
                    RenderSafezoneA4PosterPng(Path.Combine(outDir, $"Plakat_Safezone_A4_{eventName}_{cleanName}.png"), eventName, fac, qrPayload, sha256);
                }
                await Task.Run(() => WriteConfigsToDirectory(sourceDir, _factions[0]));
                await ForceSizeUpdateAsync(win);
                TryOpenFolderInExplorer(outDir);
            }
            catch { }
        }

        private static async Task ExportSafezonePosterForFactionAsync(Window win, FactionProfileModel fac)
        {
            try
            {
                string sourceDir = GetPrimarySourceDirectory(win);
                string eventName = GetEventNameFromWindow(win);
                string parentDir = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(sourceDir)) ?? AppDomain.CurrentDomain.BaseDirectory;
                string outDir = Path.Combine(parentDir, "release_factions"); Directory.CreateDirectory(outDir);

                string cleanName = SanitizeFileName(fac.Name);
                string ftakPath = Path.Combine(outDir, $"{eventName}_{cleanName}.ftak");
                string sha256 = await Task.Run(() =>
                {
                    WriteConfigsToDirectory(sourceDir, fac);
                    BuildFilteredFactionFtakArchive(sourceDir, ftakPath, fac, eventName);
                    using var sha = SHA256.Create(); using var stream = File.OpenRead(ftakPath);
                    return Convert.ToHexString(sha.ComputeHash(stream));
                });
                string qrPayload = !string.IsNullOrWhiteSpace(fac.EnrollmentUri) ? fac.EnrollmentUri : $"ftak://provision?event={Uri.EscapeDataString(eventName)}&faction={Uri.EscapeDataString(cleanName)}&channel={Uri.EscapeDataString(fac.ChannelName)}&sha256={sha256.Substring(0, 16)}";
                RenderStandaloneQrPng(Path.Combine(outDir, $"QR_{eventName}_{cleanName}.png"), qrPayload);
                RenderSafezoneA4PosterPng(Path.Combine(outDir, $"Plakat_Safezone_A4_{eventName}_{cleanName}.png"), eventName, fac, qrPayload, sha256);
                TryOpenFolderInExplorer(outDir);
            }
            catch { }
        }

        private static void RenderStandaloneQrPng(string qrOutPath, string qrPayload)
        {
            const int sz = 520;
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, sz, sz));
                DrawDeterministicQrMatrix(dc, qrPayload, 35, 35, sz - 70);
            }
            var rtb = new RenderTargetBitmap(sz, sz, 96, 96, PixelFormats.Pbgra32); rtb.Render(visual);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = new FileStream(qrOutPath, FileMode.Create, FileAccess.Write); enc.Save(fs);
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
                var tbs = new List<TextBlock>(); FindVisualChildren(win, tbs);
                foreach (var tb in tbs)
                    if (tb.Text != null && (tb.Text.Contains("Nazwa wydarzenia") || tb.Text.Contains("Nazwa paczki")))
                    {
                        var parent = GetAnyParent(tb);
                        if (parent != null)
                        {
                            var siblings = new List<TextBox>(); FindVisualChildren(parent, siblings);
                            var nonEmpty = siblings.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Text));
                            if (nonEmpty != null) return SanitizeFileName(nonEmpty.Text.Trim());
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
                    string sideDir = Path.Combine(factionsRoot, item.SideLower); Directory.CreateDirectory(sideDir);
                    string sampleKml = Path.Combine(sideDir, $"strefa_{item.SideLower}.kml");
                    if (!File.Exists(sampleKml))
                        File.WriteAllText(sampleKml, $@"<?xml version=""1.0"" encoding=""UTF-8""?><kml xmlns=""http://www.opengis.net/kml/2.2""><Document><name>Warstwa {item.SideLower.ToUpperInvariant()}</name><Placemark><name>HQ {item.SideLower.ToUpperInvariant()}</name><Point><coordinates>19.0238,49.9135,0</coordinates></Point></Placemark></Document></kml>", Encoding.UTF8);
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
                    if (fileName.StartsWith(".") || fileName.Equals("WORKSPACE_HELP.txt", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".fthproj", StringComparison.OrdinalIgnoreCase)) continue;
                    string rel = Path.GetRelativePath(sourceDir, file).Replace('\\', '/');
                    string relLower = rel.ToLowerInvariant();
                    if (relLower.StartsWith("factions/") && !cleanName.Equals("ORGA", StringComparison.OrdinalIgnoreCase) && !relLower.StartsWith(targetFactionPrefix)) continue;

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
                ["pliStrategy"] = fac.PliStrategy,
                ["loraRegion"] = fac.LoraRegion,
                ["loraModemPreset"] = fac.LoraModemPreset,
                ["createdAt"] = DateTimeOffset.UtcNow.ToString("o"),
                ["neverExpires"] = (fac.ValidityHours == 0),
                ["expiresAt"] = (fac.ValidityHours == 0) ? DateTimeOffset.UtcNow.AddYears(100).ToString("o") : DateTimeOffset.UtcNow.AddHours(fac.ValidityHours).ToString("o")
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
                DrawCenteredText(dc, $"OPERACJA: {eventName.ToUpperInvariant()}   |   STRONA: {fac.Name.ToUpperInvariant()}", 32, Brushes.White, width / 2.0, 130, true);
                double qrBoxSize = 620, qrBoxX = (width - qrBoxSize) / 2.0, qrBoxY = 260;
                dc.DrawRoundedRectangle(Brushes.White, new Pen(BrushAccent, 4), new Rect(qrBoxX, qrBoxY, qrBoxSize, qrBoxSize), 16, 16);
                DrawDeterministicQrMatrix(dc, qrPayload, qrBoxX + 45, qrBoxY + 45, qrBoxSize - 90);
                DrawCenteredText(dc, $"Kanał: {fac.ChannelName}  |  PLI: {fac.PliStrategy}  |  Region: {fac.LoraRegion} ({fac.LoraModemPreset})  |  SHA-256: {sha256Hex.Substring(0, Math.Min(20, sha256Hex.Length))}...", 19, BrushAccent, width / 2.0, 940, true);
            }
            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); rtb.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var outStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write); encoder.Save(outStream);
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
            const int modules = 29; double cell = size / modules;
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
            var result = new List<TextBox>(); if (win == null) return result;
            var allTbs = new List<TextBox>(); FindVisualChildren(win, allTbs);
            foreach (var tb in allTbs)
            {
                string n = (tb.Name ?? "").ToLowerInvariant(), t = (tb.ToolTip?.ToString() ?? "").ToLowerInvariant();
                if (n.Contains("valid") || n.Contains("expir") || n.Contains("ttl") || t.Contains("ważno") || t.Contains("wygas")) result.Add(tb);
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
                        Current.PackageValidityHours = v; Current.QrValidityHours = v;
                        tb.BorderBrush = (v == 0) ? BrushAccent : BrushInputBorder;
                    }
                };
            }
        }

        private static void ApplyIndefiniteValidityBeforeAction(Window win)
        {
            if (win == null) return;
            try
            {
                var zeros = FindNativeValidityTextBoxes(win).Where(tb => int.TryParse(tb.Text?.Trim(), out int v) && v == 0).ToList();
                if (zeros.Count == 0) return;
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
                var textBoxes = new List<TextBox>(); FindVisualChildren(win, textBoxes);
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

        private static async Task<SourceStats> ForceSizeUpdateAsync(Window win)
        {
            var empty = new SourceStats();
            if (Application.Current == null || win == null) return empty;
            try
            {
                EnsureDefaultFactions(); SyncActiveFactionToDefaultModel();
                var activeFac = _factions[0];
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
                    if (string.IsNullOrEmpty(fileName) || fileName.StartsWith(".") || fileName.StartsWith("README", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".fthproj", StringComparison.OrdinalIgnoreCase) || fileName.Equals("WORKSPACE_HELP.txt", StringComparison.OrdinalIgnoreCase)) continue;
                    long len = 0; try { len = new FileInfo(file).Length; } catch { continue; }
                    string rel = Path.GetRelativePath(sourceDir, file).Replace('\\', '/').ToLowerInvariant();
                    string ext = Path.GetExtension(fileName).ToLowerInvariant();
                    string nameLower = fileName.ToLowerInvariant();

                    stats.FileCount++; stats.TotalBytes += len;
                    if (ext == ".apk" || rel.StartsWith("plugins/") || rel.StartsWith("wtyczki/")) stats.PluginsBytes += len;
                    else if (rel.StartsWith("maps/") || rel.StartsWith("mapy/") || rel.StartsWith("imagery/") || rel.StartsWith("tiles/") || ext == ".mbtiles" || ext == ".sqlite" || ext == ".sqlitedb" || ext == ".tif" || ext == ".tiff" || ext == ".geotiff" || ((ext == ".kmz" || ext == ".xml" || ext == ".pdf" || ext == ".png" || ext == ".jpg") && (nameLower.Contains("map") || nameLower.Contains("orto") || nameLower.Contains("lidar") || nameLower.Contains("topo") || nameLower.Contains("grg")))) stats.MapsBytes += len;
                    else if (rel.StartsWith("config/") || rel.StartsWith("certs/") || rel.StartsWith("cert/") || ext == ".json" || ext == ".pref" || ext == ".cfg" || ext == ".conf" || ext == ".ini" || ext == ".p12" || ext == ".pem" || ext == ".crt" || ext == ".cer" || ext == ".key" || nameLower == "server.txt") stats.ConfigBytes += len;
                    else if (rel.StartsWith("atak/") || rel.StartsWith("overlays/") || rel.StartsWith("factions/") || rel.StartsWith("data/") || ext == ".kml" || ext == ".kmz" || ext == ".gpx" || ext == ".cot" || ext == ".geojson" || ext == ".shp") stats.TacticalBytes += len;
                    else stats.OtherBytes += len;
                }
            }
            catch { }
            return stats;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] units = { "B", "KB", "MB", "GB" };
            double val = bytes; int idx = 0;
            while (val >= 1024 && idx < units.Length - 1) { val /= 1024; idx++; }
            return idx == 0 ? $"{bytes} B" : $"{val:0.#} {units[idx]}";
        }

        private static void UpdatePreviewCardLabels(Window win, SourceStats stats)
        {
            if (win == null || stats == null) return;
            try
            {
                var cardTbs = new List<TextBlock>();
                FindVisualChildren(win, cardTbs);
                string[] rowLabels = { "Pliki paczki", "Mapy", "Pluginy", "Konfiguracja", "Dane taktyczne", "Pozostałe" };
                string[] rowValues = { FormatBytes(stats.TotalBytes), FormatBytes(stats.MapsBytes), FormatBytes(stats.PluginsBytes), FormatBytes(stats.ConfigBytes), FormatBytes(stats.TacticalBytes), FormatBytes(stats.OtherBytes) };

                bool IsLabel(string text)
                {
                    if (string.IsNullOrWhiteSpace(text)) return true;
                    string tr = text.Trim();
                    return tr.Contains("Podgląd zawartości", StringComparison.OrdinalIgnoreCase) || rowLabels.Any(l => tr.StartsWith(l, StringComparison.OrdinalIgnoreCase));
                }

                for (int r = 0; r < rowLabels.Length; r++)
                {
                    var labelTb = cardTbs.FirstOrDefault(t => t.Text != null && t.Text.Trim().Equals(rowLabels[r], StringComparison.OrdinalIgnoreCase));
                    if (labelTb == null) continue;
                    var parent = GetAnyParent(labelTb);
                    if (parent != null)
                    {
                        var pTbs = new List<TextBlock>(); FindVisualChildren(parent, pTbs);
                        if (pTbs.Count == 2)
                        {
                            var valTb = pTbs.FirstOrDefault(x => x != labelTb);
                            if (valTb != null) { valTb.Text = rowValues[r]; continue; }
                        }
                        if (parent is Grid g)
                        {
                            int tr = Grid.GetRow(labelTb);
                            foreach (UIElement ch in g.Children)
                                if (ch is TextBlock sib && sib != labelTb && Grid.GetRow(sib) == tr && !IsLabel(sib.Text)) { sib.Text = rowValues[r]; break; }
                        }
                    }
                    int idx = cardTbs.IndexOf(labelTb);
                    if (idx >= 0 && idx + 1 < cardTbs.Count && !IsLabel(cardTbs[idx + 1].Text))
                        cardTbs[idx + 1].Text = rowValues[r];
                }
            }
            catch { }
        }

        internal static UIElement FieldBox(string label, UIElement ctrl)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 5, 4) };
            sp.Children.Add(new TextBlock { Text = label, Foreground = BrushMutedText, FontSize = 9.5, Margin = new Thickness(0, 0, 0, 2), TextTrimming = TextTrimming.CharacterEllipsis });
            sp.Children.Add(ctrl);
            return sp;
        }

        internal static UIElement TextCtrl(string init, Action<string> onChg)
        {
            var tb = new TextBox { Text = init, Height = 25, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(5, 0, 5, 0), FontSize = 10.5, Background = BrushInputBg, Foreground = Brushes.White, BorderBrush = BrushInputBorder, BorderThickness = new Thickness(1) };
            tb.TextChanged += (_, __) => onChg(tb.Text);
            return tb;
        }

        internal static UIElement IntCtrl(int init, Action<int> onChg) => TextCtrl(init.ToString(), s => { if (int.TryParse(s.Trim(), out int v)) onChg(v); });

        internal static UIElement ComboCtrl(string[] items, string init, Window win, Action<string> onChg)
        {
            var cb = new ComboBox { ItemsSource = items, SelectedItem = init, Height = 25, FontSize = 10.5, VerticalContentAlignment = VerticalAlignment.Center, Tag = "INJECTED" };
            ApplyUnifiedComboBoxTheme(cb, win);
            cb.SelectionChanged += (_, __) => { if (cb.SelectedItem is string str) onChg(str); };
            return cb;
        }

        public static void WriteGeneratedConfigsIfInteractive(string tgt)
        {
            if (string.IsNullOrWhiteSpace(tgt) || tgt.Contains("fth-analyze-", StringComparison.OrdinalIgnoreCase)) return;
            LastKnownSourceDir = tgt;
            if (Application.Current == null) return;
            EnsureDefaultFactions();
            WriteConfigsToDirectory(tgt, _factions[0]);
        }

        private static void WriteConfigsToDirectory(string tgt, FactionProfileModel fac = null)
        {
            if (string.IsNullOrWhiteSpace(tgt)) return;
            try
            {
                EnsureDefaultFactions(); fac ??= _factions[0];
                Directory.CreateDirectory(tgt);
                string jsonPath = Path.Combine(tgt, "tactical_config.json");
                JsonObject root = null;
                if (File.Exists(jsonPath)) { try { root = JsonNode.Parse(File.ReadAllText(jsonPath, Encoding.UTF8)) as JsonObject; } catch { } }
                root ??= new JsonObject();

                root["faction"] = fac.Name;
                root["factionColor"] = fac.ColorHex;
                root["packageValidityHours"] = fac.ValidityHours;
                root["qrValidityHours"] = fac.ValidityHours;
                root["neverExpires"] = (fac.ValidityHours == 0);
                root["expiresAt"] = (fac.ValidityHours == 0) ? DateTimeOffset.UtcNow.AddYears(100).ToString("o") : DateTimeOffset.UtcNow.AddHours(fac.ValidityHours).ToString("o");

                JsonObject atak = (root.TryGetPropertyValue("atak", out var an) && an is JsonObject ea) ? ea : (JsonObject)(root["atak"] = new JsonObject());
                atak["locationReportingStrategy"] = fac.PliStrategy;
                atak["constantReportingRateReliable"] = fac.ConstRateReliable;
                atak["constantReportingRateUnreliable"] = fac.ConstRateUnreliable;
                atak["dynamicReportingRateMinReliable"] = fac.DynRateMin;
                atak["dynamicReportingRateMaxReliable"] = fac.DynRateMax;
                atak["dynamicReportingRateStationaryReliable"] = fac.StationaryReliable;
                atak["dynamicReportingRateStationaryUnreliable"] = fac.StationaryMesh;
                atak["tcpConnectTimeout"] = fac.TcpTimeout;
                atak["dispatchLocationCotExternal"] = fac.DispatchCotExternal;
                atak["enableNonStreamingConnections"] = fac.EnableMeshNonStreaming;

                JsonObject ots = (root.TryGetPropertyValue("openTakServer", out var on) && on is JsonObject eo) ? eo : (JsonObject)(root["openTakServer"] = new JsonObject());
                ots["host"] = fac.OtsHost; ots["enrollmentUri"] = fac.EnrollmentUri; ots["gatewayEnabled"] = fac.GatewayEnabled;

                JsonObject mesh = (root.TryGetPropertyValue("meshtastic", out var mn) && mn is JsonObject em) ? em : (JsonObject)(root["meshtastic"] = new JsonObject());
                mesh["mode"] = fac.MeshMode; mesh["role"] = fac.DeviceRole; mesh["model"] = fac.HardwareModel;
                mesh["region"] = fac.LoraRegion; mesh["modemPreset"] = fac.LoraModemPreset; mesh["hopLimit"] = fac.HopLimit;
                mesh["channelName"] = fac.ChannelName; mesh["channelUrl"] = fac.ChannelUrl; mesh["psk"] = fac.Psk;

                JsonObject lora = (mesh.TryGetPropertyValue("lora", out var ln) && ln is JsonObject el) ? el : (JsonObject)(mesh["lora"] = new JsonObject());
                lora["region"] = fac.LoraRegion; lora["modemPreset"] = fac.LoraModemPreset; lora["channelName"] = fac.ChannelName; lora["psk"] = fac.Psk;

                File.WriteAllText(jsonPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
            }
            catch { }
        }
    }
}
