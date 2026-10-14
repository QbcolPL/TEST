#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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

        // 0 = Ważność bezterminowa (paczka .ftak oraz kod QR nigdy nie wygasają)
        public int PackageValidityHours { get; set; } = 0;
        public int QrValidityHours { get; set; } = 0;
    }

    public static class TacticalConfigPanel
    {
        public static TacticalConfigModel Current { get; set; } = new TacticalConfigModel();
        public static string LastKnownSourceDir { get; set; } = "";

        // ZAMROŻONE PĘDZLE (Brak wycieków pamięci GDI / Dispatcher w WPF)
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
        private static bool _syncingValidity = false;

        private static readonly Regex WindowsPathRegex = new Regex(@"[A-Za-z]:[\\/][^\r\n""<>|?*]+", RegexOptions.Compiled);

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
                    if (innerTbs.Count >= 6)
                    {
                        return b;
                    }
                    if (bestBorder == null && b.Background != null)
                    {
                        bestBorder = b;
                    }
                }
                cur = VisualTreeHelper.GetParent(cur) ?? LogicalTreeHelper.GetParent(cur);
            }
            return bestBorder;
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
                                    Margin = new Thickness(0, 8, 0, 14),
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

                                int idx = parentPanel.Children.IndexOf(previewBorder);
                                parentPanel.Children.Insert(idx + 1, btnRefresh);
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

                // Przechwytywanie kliknięć przycisków Buduj / Generuj QR, aby 0 zawsze dawało ważność bezterminową
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

        // Wykrywa natywne pola ważności paczki .ftak oraz QR w oknie MainWindow i spina je z obsługą 0 = Bezterminowo
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

                if (int.TryParse(tb.Text?.Trim(), out int initVal) && initVal >= 0)
                {
                    if (_injectedPkgValidityBox != null && Current.PackageValidityHours == 0 && initVal > 0)
                    {
                        Current.PackageValidityHours = initVal;
                        Current.QrValidityHours = initVal;
                    }
                }

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

            // Obsługa ewentualnych ComboBoxów z czasem ważności
            var combos = new List<ComboBox>();
            FindVisualChildren(win, combos);
            foreach (var cb in combos)
            {
                if (cb.Tag?.ToString() == "INJECTED") continue;
                string name = (cb.Name ?? "").ToLowerInvariant();
                if (name.Contains("valid") || name.Contains("expir") || name.Contains("ttl"))
                {
                    bool hasZero = false;
                    foreach (var item in cb.Items)
                    {
                        string s = (item is ComboBoxItem cbi) ? cbi.Content?.ToString() : item?.ToString();
                        if (s != null && (s == "0" || s.StartsWith("0 ") || s.Contains("Bezterminow", StringComparison.OrdinalIgnoreCase)))
                        {
                            hasZero = true;
                            break;
                        }
                    }
                    if (!hasZero && cb.ItemsSource == null)
                    {
                        cb.Items.Insert(0, new ComboBoxItem { Content = "0 (Bezterminowo)", Foreground = Brushes.White, Background = BrushItemBg });
                    }
                }
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

        // Zapewnia, że przy wartości 0 wewnętrzny generator paczki .ftak i kodu QR otrzymuje 100 lat ważności (brak wygasania)
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

                // Jeżeli natywne pole ma "0", na ułamek sekundy przekazujemy 876000h (100 lat), aby ominąć każdą blokadę <= 0 w MainWindow
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

                // Ustawienie właściwości ważności w modelu projektu w pamięci (MainWindow / FieldTakProject)
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

                // Przywrócenie "0" w kontrolkach UI po wykonaniu akcji kliknięcia
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

        private static bool HasInjectedTag(Panel panel, string tag)
        {
            foreach (UIElement child in panel.Children)
            {
                if (child is FrameworkElement fe && string.Equals(fe.Tag?.ToString(), tag, StringComparison.Ordinal))
                    return true;
            }
            return false;
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
                    else if (ext == ".kml" || ext == ".kmz" || ext == ".gpx" || ext == ".cot" || ext == ".geojson" || ext == ".shp" || rel.StartsWith("atak/") || rel.StartsWith("overlays/") || rel.StartsWith("data/"))
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

            // Sekcja ważności paczki .ftak oraz kodu QR (0 = Bezterminowo)
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

        private static void WriteConfigsToDirectory(string tgt)
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

                // 0. Ważność paczki .ftak oraz kodu QR (0 = Bezterminowo)
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

                // 2. Sekcja MESHTASTIC + LORA
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
                rootLora["usePreset"] = true;

                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(jsonPath, root.ToJsonString(options), Encoding.UTF8);
            }
            catch { }
        }
    }
}