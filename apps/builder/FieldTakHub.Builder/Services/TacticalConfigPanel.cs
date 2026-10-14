#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;

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
    }

    public static class TacticalConfigPanel
    {
        public static TacticalConfigModel Current { get; set; } = new TacticalConfigModel();
        public static string LastKnownSourceDir { get; set; } = "";

        private static bool _fwFieldsAdded = false;
        private static bool _atakCardAdded = false;
        private static bool _refreshBtnAdded = false;
        private static Style _darkComboBoxStyle = null;

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
                }
                catch { }
            }
            return _darkComboBoxStyle;
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
                cb.Background = new SolidColorBrush(Color.FromRgb(8, 19, 22));
                cb.BorderBrush = new SolidColorBrush(Color.FromRgb(30, 66, 74));

                var style = GetDarkComboBoxStyle();
                if (style != null) cb.Style = style;

                var itemStyle = new Style(typeof(ComboBoxItem));
                itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(15, 32, 37))));
                itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
                itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5)));
                itemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
                cb.ItemContainerStyle = itemStyle;

                // Ujednolicenie istniejących elementów ComboBoxItem na biały tekst
                foreach (var item in cb.Items)
                {
                    if (item is ComboBoxItem cbi)
                    {
                        cbi.Foreground = Brushes.White;
                        cbi.Background = new SolidColorBrush(Color.FromRgb(15, 32, 37));
                    }
                }

                // Jeśli przed wyłączeniem IsEditable wybrany był tekst, upewnij się że SelectedItem jest ustawiony
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

                cb.SelectionChanged += (s, e) =>
                {
                    SyncNativeMeshtasticSelections(win);
                };
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

        private static T GetParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            if (parentObject is T parent) return parent;
            return GetParent<T>(parentObject);
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
                            Border cardBorder = GetParent<Border>(tb);
                            if (cardBorder != null)
                            {
                                if (!_fwFieldsAdded && cardBorder.Child is Panel innerPanel)
                                {
                                    InjectMeshtasticFields(innerPanel, win);
                                    _fwFieldsAdded = true;
                                }

                                if (!_atakCardAdded && VisualTreeHelper.GetParent(cardBorder) is Panel parentPanel)
                                {
                                    int idx = parentPanel.Children.IndexOf(cardBorder);
                                    parentPanel.Children.Insert(idx, BuildAtakCard(win));
                                    _atakCardAdded = true;
                                }
                            }
                        }

                        if (tb.Text != null && tb.Text.Contains("Podgląd zawartości"))
                        {
                            Border previewBorder = GetParent<Border>(tb);
                            if (previewBorder != null)
                            {
                                if (VisualTreeHelper.GetParent(previewBorder) is Panel parentPanel && !_refreshBtnAdded)
                                {
                                    var btnRefresh = new Button
                                    {
                                        Content = "Odśwież / Skalkuluj zawartość (folder source)",
                                        Height = 32,
                                        Margin = new Thickness(0, 8, 0, 14),
                                        Background = new SolidColorBrush(Color.FromRgb(44, 229, 208)),
                                        Foreground = new SolidColorBrush(Color.FromRgb(6, 22, 24)),
                                        FontWeight = FontWeights.Bold,
                                        Cursor = System.Windows.Input.Cursors.Hand,
                                        BorderThickness = new Thickness(0),
                                        Tag = "INJECTED_BTN"
                                    };
                                    btnRefresh.Click += async (s, e) =>
                                    {
                                        btnRefresh.Content = "Przeliczanie zawartości source...";
                                        btnRefresh.IsEnabled = false;
                                        await ForceSizeUpdateAsync(win);
                                        btnRefresh.Content = "Odśwież / Skalkuluj zawartość (folder source)";
                                        btnRefresh.IsEnabled = true;
                                    };

                                    int idx = parentPanel.Children.IndexOf(previewBorder);
                                    parentPanel.Children.Insert(idx + 1, btnRefresh);
                                    _refreshBtnAdded = true;
                                }
                            }
                        }
                    }

                    SyncNativeMeshtasticSelections(win);
                }
                catch { }
            }

            InitializeEnhancements();
            win.Loaded += async (s, e) =>
            {
                InitializeEnhancements();
                await ForceSizeUpdateAsync(win);
            };
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
            var sourceDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void TryAddSourceCandidate(string rawPath)
            {
                if (string.IsNullOrWhiteSpace(rawPath) || rawPath.Length < 3) return;
                try
                {
                    string clean = rawPath.Trim().Trim('"');
                    if (!Path.IsPathRooted(clean)) return;

                    if (Directory.Exists(clean))
                    {
                        if (string.Equals(Path.GetFileName(clean), "source", StringComparison.OrdinalIgnoreCase))
                        {
                            sourceDirs.Add(clean);
                        }
                        else
                        {
                            string subSource = Path.Combine(clean, "source");
                            if (Directory.Exists(subSource) || File.Exists(Path.Combine(clean, "project.json")) || Directory.GetFiles(clean, "*.fthproj").Length > 0)
                            {
                                sourceDirs.Add(subSource);
                            }
                        }
                    }
                    else if (File.Exists(clean))
                    {
                        string parent = Path.GetDirectoryName(clean);
                        if (!string.IsNullOrEmpty(parent))
                        {
                            string subSource = Path.Combine(parent, "source");
                            sourceDirs.Add(subSource);
                        }
                    }
                }
                catch { }
            }

            if (!string.IsNullOrWhiteSpace(LastKnownSourceDir))
            {
                TryAddSourceCandidate(LastKnownSourceDir);
            }

            // 1. Skanowanie pól i właściwości Code-Behind okna MainWindow (oraz obiektów potomnych np. _project)
            if (win != null)
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var objectsToInspect = new List<object> { win };
                if (win.DataContext != null) objectsToInspect.Add(win.DataContext);

                foreach (var field in win.GetType().GetFields(flags))
                {
                    try
                    {
                        var val = field.GetValue(win);
                        if (val is string strVal) TryAddSourceCandidate(strVal);
                        else if (val != null && val.GetType().Namespace != null && val.GetType().Namespace.StartsWith("FieldTakHub"))
                            objectsToInspect.Add(val);
                    }
                    catch { }
                }

                foreach (var obj in objectsToInspect)
                {
                    foreach (var prop in obj.GetType().GetProperties(flags))
                    {
                        if (prop.GetIndexParameters().Length > 0) continue;
                        try
                        {
                            var val = prop.GetValue(obj);
                            if (val is string strVal) TryAddSourceCandidate(strVal);
                        }
                        catch { }
                    }
                    foreach (var field in obj.GetType().GetFields(flags))
                    {
                        try
                        {
                            if (field.GetValue(obj) is string strVal) TryAddSourceCandidate(strVal);
                        }
                        catch { }
                    }
                }

                // 2. Skanowanie wszystkich TextBoxów w oknie pod kątem wpisanej ścieżki Workspace/Source
                var textBoxes = new List<TextBox>();
                FindVisualChildren(win, textBoxes);
                foreach (var tbx in textBoxes)
                {
                    TryAddSourceCandidate(tbx.Text);
                }
            }

            // 3. Domyślny katalog roboczy, jeśli żaden zewnętrzny projekt nie został jeszcze otwarty
            if (sourceDirs.Count == 0)
            {
                string baseD = AppDomain.CurrentDomain.BaseDirectory;
                string defaultSource = Path.Combine(baseD, "workspace", "source");
                sourceDirs.Add(defaultSource);
            }

            return sourceDirs.ToList();
        }

        private static async Task ForceSizeUpdateAsync(Window win)
        {
            if (Application.Current == null || win == null) return;

            try
            {
                SyncNativeMeshtasticSelections(win);
                var sourceDirs = ResolveSourceDirectories(win);

                await Task.Run(() =>
                {
                    foreach (var sDir in sourceDirs)
                    {
                        try
                        {
                            if (!Directory.Exists(sDir)) Directory.CreateDirectory(sDir);
                            WriteConfigsToDirectory(sDir);
                        }
                        catch { }
                    }
                });

                // 1. Wywołanie natywnych metod odświeżających w MainWindow (Code-Behind), z wykluczeniem Build/Validate
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                foreach (var m in win.GetType().GetMethods(flags))
                {
                    if (m.GetParameters().Length != 0) continue;
                    string n = m.Name.ToLowerInvariant();
                    if ((n.Contains("preview") || n.Contains("analyz") || n.Contains("refresh") || n.Contains("recalc") || n.Contains("updatesource") || n.Contains("updatecontent"))
                        && !n.Contains("build") && !n.Contains("gener") && !n.Contains("export") && !n.Contains("pack")
                        && !n.Contains("valid") && !n.Contains("publish") && !n.Contains("deploy") && !n.Contains("dialog")
                        && !n.Contains("open") && !n.Contains("new") && !n.Contains("save") && !n.Contains("import") && !n.Contains("sign"))
                    {
                        try { m.Invoke(win, null); } catch { }
                    }
                }

                // 2. Bezpośrednie przeliczenie plików w folderze source i aktualizacja sekcji "Podgląd zawartości"
                string activeSourceDir = sourceDirs.FirstOrDefault(Directory.Exists) ?? sourceDirs.FirstOrDefault();
                if (!string.IsNullOrEmpty(activeSourceDir) && Directory.Exists(activeSourceDir))
                {
                    var stats = await Task.Run(() => CalculateSourceFolderStats(activeSourceDir));
                    UpdatePreviewCardLabels(win, stats);
                }
            }
            catch { }
        }

        private class SourceStats
        {
            public long TotalBytes;
            public long MapsBytes;
            public long PluginsBytes;
            public long ConfigBytes;
            public long TacticalBytes;
            public long OtherBytes;
        }

        private static SourceStats CalculateSourceFolderStats(string sourceDir)
        {
            var stats = new SourceStats();
            if (!Directory.Exists(sourceDir)) return stats;

            try
            {
                var files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    string fileName = Path.GetFileName(file);
                    if (string.IsNullOrEmpty(fileName)) continue;

                    // Ignorujemy pliki pomocnicze workspace tak jak SourceFileFilter
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
            try
            {
                var tbs = new List<TextBlock>();
                FindVisualChildren(win, tbs);
                Border previewBorder = null;
                foreach (var tb in tbs)
                {
                    if (tb.Text != null && tb.Text.Contains("Podgląd zawartości"))
                    {
                        previewBorder = GetParent<Border>(tb);
                        if (previewBorder != null) break;
                    }
                }
                if (previewBorder == null) return;

                var cardTbs = new List<TextBlock>();
                FindVisualChildren(previewBorder, cardTbs);

                void SetRowValue(string labelPrefix, string formattedSize)
                {
                    for (int i = 0; i < cardTbs.Count; i++)
                    {
                        var t = cardTbs[i];
                        if (t.Text != null && t.Text.Trim().StartsWith(labelPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            // Szukamy sąsiedniego TextBlocka w tym samym wierszu (Grid / DockPanel / StackPanel)
                            var parent = VisualTreeHelper.GetParent(t);
                            if (parent != null)
                            {
                                int children = VisualTreeHelper.GetChildrenCount(parent);
                                for (int c = 0; c < children; c++)
                                {
                                    if (VisualTreeHelper.GetChild(parent, c) is TextBlock sib && sib != t)
                                    {
                                        sib.Text = formattedSize;
                                        return;
                                    }
                                }
                            }
                            if (i + 1 < cardTbs.Count)
                            {
                                cardTbs[i + 1].Text = formattedSize;
                                return;
                            }
                        }
                    }
                }

                SetRowValue("Pliki paczki", FormatBytes(stats.TotalBytes));
                SetRowValue("Mapy", FormatBytes(stats.MapsBytes));
                SetRowValue("Pluginy", FormatBytes(stats.PluginsBytes));
                SetRowValue("Konfiguracja", FormatBytes(stats.ConfigBytes));
                SetRowValue("Dane taktyczne", FormatBytes(stats.TacticalBytes));
                SetRowValue("Pozostałe", FormatBytes(stats.OtherBytes));
            }
            catch { }
        }

        private static UIElement BuildAtakCard(Window win)
        {
            var m = Current;
            var card = new Border { Background = new SolidColorBrush(Color.FromRgb(9, 20, 24)), BorderBrush = new SolidColorBrush(Color.FromRgb(26, 58, 66)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 14) };
            var lStack = new StackPanel();
            lStack.Children.Add(new TextBlock { Text = "Konfiguracja ATAK-CIV (PLI i siec)", Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 14, Margin = new Thickness(0, 0, 0, 4) });
            lStack.Children.Add(new TextBlock { Text = "Czysty profil ATAK bez wymuszania wtyczek. Zdefiniuj czestotliwosci odswiezania PLI.", Foreground = new SolidColorBrush(Color.FromRgb(138, 168, 164)), FontSize = 11, Margin = new Thickness(0, 0, 0, 10) });

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

            var gSw = new UniformGrid { Columns = 2 };
            gSw.Children.Add(TileSwitch("Wysylaj pozycje na zewnatrz", m.DispatchLocationCotExternal, v => { m.DispatchLocationCotExternal = v; }));
            gSw.Children.Add(TileSwitch("Polaczenia bezstrumieniowe Mesh", m.EnableNonStreamingConnections, v => { m.EnableNonStreamingConnections = v; }));
            lStack.Children.Add(gSw);

            card.Child = lStack;
            return card;
        }

        private static void InjectMeshtasticFields(Panel parentPanel, Window win)
        {
            var loraGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 8, 0, 0) };
            loraGrid.Children.Add(FieldBox("LoRa Region", ComboCtrl(new[] { "EU_868", "US_915", "EU_433", "ANZ_915", "RU_864", "KR_920" }, Current.LoraRegion, win, v => { Current.LoraRegion = v; })));
            loraGrid.Children.Add(FieldBox("LoRa Modem Preset", ComboCtrl(new[] { "SHORT_FAST", "LONG_FAST", "LONG_SLOW", "MEDIUM_SLOW", "MEDIUM_FAST" }, Current.LoraModemPreset, win, v => { Current.LoraModemPreset = v; })));
            parentPanel.Children.Add(loraGrid);

            var fwContainer = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            var labelMin = new TextBlock { Text = "Firmware MIN:", Foreground = new SolidColorBrush(Color.FromRgb(138, 168, 164)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var txtMin = new TextBox { Text = Current.FirmwareMin, Width = 70, Height = 24, Background = new SolidColorBrush(Color.FromRgb(8, 19, 22)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(30, 66, 74)), BorderThickness = new Thickness(1), VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 0, 4, 0) };
            txtMin.TextChanged += (_, __) => { Current.FirmwareMin = txtMin.Text; };

            var labelMax = new TextBlock { Text = "Firmware MAX:", Foreground = new SolidColorBrush(Color.FromRgb(138, 168, 164)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 8, 0) };
            var txtMax = new TextBox { Text = Current.FirmwareMax, Width = 70, Height = 24, Background = new SolidColorBrush(Color.FromRgb(8, 19, 22)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(30, 66, 74)), BorderThickness = new Thickness(1), VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 0, 4, 0) };
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
            sp.Children.Add(new TextBlock { Text = label, Foreground = new SolidColorBrush(Color.FromRgb(138, 168, 164)), FontSize = 10.5, Margin = new Thickness(0, 0, 0, 3) });
            sp.Children.Add(ctrl);
            return sp;
        }

        private static UIElement TextCtrl(string init, Action<string> onChg)
        {
            var tb = new TextBox { Text = init, Height = 28, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 0, 6, 0), FontSize = 11, Background = new SolidColorBrush(Color.FromRgb(8, 19, 22)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(30, 66, 74)), BorderThickness = new Thickness(1) };
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
            var b = new Border { Background = new SolidColorBrush(Color.FromRgb(15, 32, 37)), BorderBrush = new SolidColorBrush(Color.FromRgb(28, 64, 72)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 8, 6) };
            var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var tb = new TextBlock { Text = title, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            var tr = new Border { Width = 36, Height = 20, CornerRadius = new CornerRadius(10), Cursor = System.Windows.Input.Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
            var kn = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Margin = new Thickness(2) };
            bool state = init;
            void Upd()
            {
                tr.Background = state ? new SolidColorBrush(Color.FromRgb(31, 224, 197)) : new SolidColorBrush(Color.FromRgb(24, 44, 50));
                kn.Background = state ? new SolidColorBrush(Color.FromRgb(6, 22, 24)) : new SolidColorBrush(Color.FromRgb(138, 168, 164));
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
            LastKnownSourceDir = tgt;
            if (Application.Current == null) return; // Chroni testy jednostkowe xUnit przed modyfikacją folderu tymczasowego
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

                // 1. Sekcja ATAK
                JsonObject atak = (root.TryGetPropertyValue("atak", out var atakNode) && atakNode is JsonObject existingAtak)
                    ? existingAtak
                    : new JsonObject();
                root["atak"] = atak;

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

                // 2. Sekcja MESHTASTIC + LORA (Pełna kompatybilność z aplikacją Android 2.4)
                JsonObject mesh = (root.TryGetPropertyValue("meshtastic", out var meshNode) && meshNode is JsonObject existingMesh)
                    ? existingMesh
                    : new JsonObject();
                root["meshtastic"] = mesh;

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

                JsonObject lora = (mesh.TryGetPropertyValue("lora", out var loraNode) && loraNode is JsonObject existingLora)
                    ? existingLora
                    : new JsonObject();
                mesh["lora"] = lora;

                lora["region"] = Current.LoraRegion;
                lora["modemPreset"] = Current.LoraModemPreset;
                lora["usePreset"] = true;
                lora["hopLimit"] = 3;
                lora["txEnabled"] = true;

                // Zapis również w głównym węźle "lora" dla wstecznej kompatybilności parsera Android
                JsonObject rootLora = (root.TryGetPropertyValue("lora", out var rLoraNode) && rLoraNode is JsonObject existingRLora)
                    ? existingRLora
                    : new JsonObject();
                root["lora"] = rootLora;
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