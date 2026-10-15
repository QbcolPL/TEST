#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace FieldTakHub.Builder.Services
{
    public class TacticalMapLayerDef
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public bool IsWmsEpsg3857 { get; set; }
        public string UrlTemplate { get; set; }
        public override string ToString() => Name;
    }

    public class TacticalMapPin
    {
        public string Name { get; set; }
        public string Faction { get; set; }
        public string ColorHex { get; set; }
        public double Lat { get; set; }
        public double Lon { get; set; }
        public string Type { get; set; }
    }

    public static class TacticalMapGeneratorView
    {
        private static readonly HttpClient Http = CreateHttpClient();
        private static readonly ConcurrentDictionary<string, byte[]> TileCache = new ConcurrentDictionary<string, byte[]>();

        private static UIElement _originalWindowContent;
        private static Border _fullScreenOverlay;
        private static Button _tabBuilderBtn;
        private static Button _tabMapBtn;
        private static Canvas _mapCanvas;
        private static TextBlock _statusText;
        private static TextBlock _coordsBadge;
        private static ListBox _pinsListBox;

        private static double _centerLat = 49.9135;
        private static double _centerLon = 19.0238;
        private static int _zoom = 14;
        private static bool _showGrid = true;
        private static bool _showScaleBar = true;
        private static bool _showHybridLabels;
        private static bool _isDragging;
        private static Point _dragStartPoint;
        private static double _dragStartLat, _dragStartLon;

        // Interaktywna linijka pomiarowa (PPM)
        private static (double Lat, double Lon)? _rulerStart;
        private static (double Lat, double Lon)? _rulerEnd;

        private static readonly List<TacticalMapPin> _pins = new List<TacticalMapPin>();
        private static TacticalMapLayerDef _activeLayer;
        private static TextBox _mapTitleBox, _latInputBox, _lonInputBox, _pinNameBox;
        private static ComboBox _pinFactionCombo, _pinTypeCombo;

        public static readonly List<TacticalMapLayerDef> Layers = new List<TacticalMapLayerDef>
        {
            new TacticalMapLayerDef { Id = "google_hyb", Name = "Google Maps — Hybryda (Satelita + Drogi)", IsWmsEpsg3857 = false, UrlTemplate = "https://mt1.google.com/vt/lyrs=y&x={x}&y={y}&z={z}" },
            new TacticalMapLayerDef { Id = "google_sat", Name = "Google Maps — Satelita (Orto)", IsWmsEpsg3857 = false, UrlTemplate = "https://mt1.google.com/vt/lyrs=s&x={x}&y={y}&z={z}" },
            new TacticalMapLayerDef { Id = "google_ter", Name = "Google Maps — Teren / Topo", IsWmsEpsg3857 = false, UrlTemplate = "https://mt1.google.com/vt/lyrs=p&x={x}&y={y}&z={z}" },
            new TacticalMapLayerDef { Id = "geoportal_hires", Name = "Geoportal PL — Ortofotomapa HIGH-RES (Wysoka rozdz.)", IsWmsEpsg3857 = true, UrlTemplate = "https://mapy.geoportal.gov.pl/wss/service/PZGIK/ORTO/WMS/HighResolution?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=Raster&STYLES=&SRS=EPSG:3857&BBOX={bbox}&WIDTH=256&HEIGHT=256&FORMAT=image/jpeg" },
            new TacticalMapLayerDef { Id = "geoportal_std", Name = "Geoportal PL — Ortofotomapa Standard", IsWmsEpsg3857 = true, UrlTemplate = "https://mapy.geoportal.gov.pl/wss/service/PZGIK/ORTO/WMS/StandardResolution?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=Raster&STYLES=&SRS=EPSG:3857&BBOX={bbox}&WIDTH=256&HEIGHT=256&FORMAT=image/jpeg" },
            new TacticalMapLayerDef { Id = "geoportal_lidar", Name = "Geoportal PL — LIDAR (ISOK Cieniowanie NMT)", IsWmsEpsg3857 = true, UrlTemplate = "https://mapy.geoportal.gov.pl/wss/service/PZGIK/NMT/GRID1/WMS/ShadedRelief?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=Raster&STYLES=&SRS=EPSG:3857&BBOX={bbox}&WIDTH=256&HEIGHT=256&FORMAT=image/jpeg" },
            new TacticalMapLayerDef { Id = "geoportal_topo", Name = "Geoportal PL — Mapa Topograficzna (BDOT10k / Topo)", IsWmsEpsg3857 = true, UrlTemplate = "https://mapy.geoportal.gov.pl/wss/ext/KrajowaIntegracjaMapTopograficznych?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=raster&STYLES=&SRS=EPSG:3857&BBOX={bbox}&WIDTH=256&HEIGHT=256&FORMAT=image/png" },
            new TacticalMapLayerDef { Id = "esri_sat", Name = "ESRI — World Imagery (Ortofotomapa)", IsWmsEpsg3857 = false, UrlTemplate = "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}" },
            new TacticalMapLayerDef { Id = "esri_topo", Name = "ESRI — World Topographic Map", IsWmsEpsg3857 = false, UrlTemplate = "https://server.arcgisonline.com/ArcGIS/rest/services/World_Topo_Map/MapServer/tile/{z}/{y}/{x}" },
            new TacticalMapLayerDef { Id = "osm_std", Name = "OpenStreetMap — OSM Standard", IsWmsEpsg3857 = false, UrlTemplate = "https://tile.openstreetmap.org/{z}/{x}/{y}.png" },
            new TacticalMapLayerDef { Id = "open_topomap", Name = "OpenTopoMap — Mapa Topograficzna (Warstwice)", IsWmsEpsg3857 = false, UrlTemplate = "https://a.tile.opentopomap.org/{z}/{x}/{y}.png" },
            new TacticalMapLayerDef { Id = "open_fotomap", Name = "OpenFotoMap / CyclOSM — Taktyczna Siatka Szlaków", IsWmsEpsg3857 = false, UrlTemplate = "https://a.tile-cyclosm.openstreetmap.fr/cyclosm/{z}/{x}/{y}.png" }
        };

        private static HttpClient CreateHttpClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("FieldTakHubBuilder/2.4 (Windows NT 10.0; Win64; x64)");
            return c;
        }

        public static void AttachFullScreenTab(Window win)
        {
            if (Application.Current == null || win == null) return;
            try
            {
                if (win.Content is FrameworkElement existingFe && existingFe.Tag?.ToString() == "FTH_MASTER_SHELL") return;
                if (win.Content is UIElement currentContent)
                {
                    _originalWindowContent = currentContent;
                    win.Content = null;

                    var masterGrid = new Grid { Tag = "FTH_MASTER_SHELL", Background = TacticalConfigPanel.BrushCardBg };
                    masterGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
                    masterGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                    var navBorder = new Border { Background = TacticalConfigPanel.BrushInputBg, BorderBrush = TacticalConfigPanel.BrushCardBorder, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(16, 0, 16, 0) };
                    var navDock = new DockPanel();
                    var rightInfo = new TextBlock { Text = "Field TAK Hub 2.4  •  Google / Geoportal Hi-Res / LIDAR / Topo / ESRI / OSM + Linijka Skali", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
                    DockPanel.SetDock(rightInfo, Dock.Right);
                    navDock.Children.Add(rightInfo);

                    var tabsStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    _tabBuilderBtn = new Button { Content = "📦 Konfigurator Paczek (.ftak) — Tryb Prosty / Zaawansowany", Height = 30, Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(0, 0, 8, 0), Background = TacticalConfigPanel.BrushButtonAccent, Foreground = TacticalConfigPanel.BrushDarkFg, BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1), FontWeight = FontWeights.Bold, FontSize = 11.5, Cursor = Cursors.Hand };
                    _tabMapBtn = new Button { Content = "🗺️ Generator Map Taktycznych (Pełny Ekran — KMZ / GPX / PDF / FTAK)", Height = 30, Padding = new Thickness(16, 0, 16, 0), Background = TacticalConfigPanel.BrushItemBg, Foreground = TacticalConfigPanel.BrushAccent, BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1), FontWeight = FontWeights.Bold, FontSize = 11.5, Cursor = Cursors.Hand };

                    _tabBuilderBtn.Click += (_, __) => HideFullScreenMapTab();
                    _tabMapBtn.Click += (_, __) => ShowFullScreenMapTab(win);

                    tabsStack.Children.Add(_tabBuilderBtn);
                    tabsStack.Children.Add(_tabMapBtn);
                    navDock.Children.Add(tabsStack);
                    navBorder.Child = navDock;
                    Grid.SetRow(navBorder, 0);
                    masterGrid.Children.Add(navBorder);

                    var contentHostGrid = new Grid();
                    contentHostGrid.Children.Add(_originalWindowContent);
                    _fullScreenOverlay = BuildFullScreenMapGeneratorUI(win);
                    _fullScreenOverlay.Visibility = Visibility.Collapsed;
                    contentHostGrid.Children.Add(_fullScreenOverlay);

                    Grid.SetRow(contentHostGrid, 1);
                    masterGrid.Children.Add(contentHostGrid);
                    win.Content = masterGrid;
                }
            }
            catch { }
        }

        public static void ShowFullScreenMapTab(Window win)
        {
            if (_fullScreenOverlay == null && win != null) AttachFullScreenTab(win);
            if (_fullScreenOverlay != null)
            {
                if (_originalWindowContent != null) _originalWindowContent.Visibility = Visibility.Collapsed;
                _fullScreenOverlay.Visibility = Visibility.Visible;
                if (_tabMapBtn != null) { _tabMapBtn.Background = TacticalConfigPanel.BrushButtonAccent; _tabMapBtn.Foreground = TacticalConfigPanel.BrushDarkFg; }
                if (_tabBuilderBtn != null) { _tabBuilderBtn.Background = TacticalConfigPanel.BrushItemBg; _tabBuilderBtn.Foreground = Brushes.White; }
                RefreshFactionDropdown();
                _ = RenderMapTilesAsync();
            }
        }

        public static void HideFullScreenMapTab()
        {
            if (_fullScreenOverlay != null)
            {
                _fullScreenOverlay.Visibility = Visibility.Collapsed;
                if (_originalWindowContent != null) _originalWindowContent.Visibility = Visibility.Visible;
                if (_tabBuilderBtn != null) { _tabBuilderBtn.Background = TacticalConfigPanel.BrushButtonAccent; _tabBuilderBtn.Foreground = TacticalConfigPanel.BrushDarkFg; }
                if (_tabMapBtn != null) { _tabMapBtn.Background = TacticalConfigPanel.BrushItemBg; _tabMapBtn.Foreground = TacticalConfigPanel.BrushAccent; }
            }
        }

        private static void RefreshFactionDropdown()
        {
            if (_pinFactionCombo == null) return;
            var factions = TacticalConfigPanel.GetConfiguredFactions().Select(f => f.Name).ToArray();
            _pinFactionCombo.ItemsSource = factions;
            if (_pinFactionCombo.SelectedItem == null && factions.Length > 0) _pinFactionCombo.SelectedIndex = 0;
        }

        private static Border BuildFullScreenMapGeneratorUI(Window win)
        {
            _activeLayer = Layers[0];
            var rootBorder = new Border { Background = TacticalConfigPanel.BrushCardBg, Tag = "INJECTED_MAP_OVERLAY" };
            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });

            var bodyGrid = new Grid();
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(370) });
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var leftScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = TacticalConfigPanel.BrushCardBg, Padding = new Thickness(14) };
            var leftStack = new StackPanel { Margin = new Thickness(12) };

            var cardLayer = CreateSectionCard("1. Warstwa Podkładowa & Linijka Skali");
            var layerCombo = new ComboBox { ItemsSource = Layers, SelectedIndex = 0, Height = 30, FontSize = 11, Margin = new Thickness(0, 0, 0, 8), Tag = "INJECTED" };
            TacticalConfigPanel.ApplyUnifiedComboBoxTheme(layerCombo, win);
            layerCombo.SelectionChanged += (_, __) => { if (layerCombo.SelectedItem is TacticalMapLayerDef def) { _activeLayer = def; _ = RenderMapTilesAsync(); } };
            cardLayer.Children.Add(TacticalConfigPanel.FieldBox("Źródło kafelków / WMS:", layerCombo));

            var chkGrid = new CheckBox { Content = "Siatka taktyczna współrzędnych (WGS84)", IsChecked = true, Foreground = Brushes.White, FontSize = 11, Margin = new Thickness(0, 4, 0, 4) };
            chkGrid.Checked += (_, __) => { _showGrid = true; _ = RenderMapTilesAsync(); };
            chkGrid.Unchecked += (_, __) => { _showGrid = false; _ = RenderMapTilesAsync(); };
            cardLayer.Children.Add(chkGrid);

            var chkScale = new CheckBox { Content = "Linijka skali topograficznej + Dalmierz (PPM)", IsChecked = true, Foreground = TacticalConfigPanel.BrushAccent, FontWeight = FontWeights.SemiBold, FontSize = 11, Margin = new Thickness(0, 0, 0, 4) };
            chkScale.Checked += (_, __) => { _showScaleBar = true; _ = RenderMapTilesAsync(); };
            chkScale.Unchecked += (_, __) => { _showScaleBar = false; _ = RenderMapTilesAsync(); };
            cardLayer.Children.Add(chkScale);

            var chkHybrid = new CheckBox { Content = "Nakładka hybrydowa dróg i nazw miejscowości", IsChecked = false, Foreground = Brushes.White, FontSize = 11 };
            chkHybrid.Checked += (_, __) => { _showHybridLabels = true; _ = RenderMapTilesAsync(); };
            chkHybrid.Unchecked += (_, __) => { _showHybridLabels = false; _ = RenderMapTilesAsync(); };
            cardLayer.Children.Add(chkHybrid);
            leftStack.Children.Add(WrapCard(cardLayer));

            var cardNav = CreateSectionCard("2. Obszar Operacji & Zoom");
            _mapTitleBox = (TextBox)TacticalConfigPanel.TextCtrl("Mapa_Taktyczna_AO", _ => { });
            cardNav.Children.Add(TacticalConfigPanel.FieldBox("Nazwa arkusza mapy / operacji:", _mapTitleBox));

            var coordsGrid = new UniformGrid { Columns = 2 };
            _latInputBox = (TextBox)TacticalConfigPanel.TextCtrl(_centerLat.ToString("0.0000", CultureInfo.InvariantCulture), _ => { });
            _lonInputBox = (TextBox)TacticalConfigPanel.TextCtrl(_centerLon.ToString("0.0000", CultureInfo.InvariantCulture), _ => { });
            coordsGrid.Children.Add(TacticalConfigPanel.FieldBox("Szerokość (Lat):", _latInputBox));
            coordsGrid.Children.Add(TacticalConfigPanel.FieldBox("Długość (Lon):", _lonInputBox));
            cardNav.Children.Add(coordsGrid);

            var zoomRow = new UniformGrid { Columns = 3, Margin = new Thickness(0, 4, 0, 0) };
            zoomRow.Children.Add(MakeBtn("− Zoom", false, () => { _zoom = Math.Max(5, _zoom - 1); _ = RenderMapTilesAsync(); }));
            zoomRow.Children.Add(MakeBtn("🎯 Idź do Lat/Lon", true, () =>
            {
                if (double.TryParse(_latInputBox.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double la) &&
                    double.TryParse(_lonInputBox.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double lo))
                {
                    _centerLat = Math.Clamp(la, -85.0, 85.0); _centerLon = Math.Clamp(lo, -180.0, 180.0);
                    _ = RenderMapTilesAsync();
                }
            }));
            zoomRow.Children.Add(MakeBtn("+ Zoom", false, () => { _zoom = Math.Min(19, _zoom + 1); _ = RenderMapTilesAsync(); }));
            cardNav.Children.Add(zoomRow);
            leftStack.Children.Add(WrapCard(cardNav));

            var cardPins = CreateSectionCard("3. Punkty Taktyczne (LPM) & Pomiar Odległości (PPM)");
            var pinFormGrid = new UniformGrid { Columns = 2 };
            _pinFactionCombo = new ComboBox { Height = 28, FontSize = 11, Tag = "INJECTED" };
            TacticalConfigPanel.ApplyUnifiedComboBoxTheme(_pinFactionCombo, win);
            RefreshFactionDropdown();
            _pinTypeCombo = (ComboBox)TacticalConfigPanel.ComboCtrl(new[] { "HQ (Sztab)", "Respawn / Baza", "Punkt Medyczny", "Cel / Objective", "Strefa Zrzutu", "Punkt Kontrolny" }, "HQ (Sztab)", win, _ => { });
            pinFormGrid.Children.Add(TacticalConfigPanel.FieldBox("Strona konfliktu:", _pinFactionCombo));
            pinFormGrid.Children.Add(TacticalConfigPanel.FieldBox("Typ punktu:", _pinTypeCombo));
            cardPins.Children.Add(pinFormGrid);

            _pinNameBox = (TextBox)TacticalConfigPanel.TextCtrl("Punkt-1", _ => { });
            cardPins.Children.Add(TacticalConfigPanel.FieldBox("Etykieta nowego punktu (LPM = punkt | PPM = linijka pomiaru):", _pinNameBox));

            _pinsListBox = new ListBox { Height = 85, Background = TacticalConfigPanel.BrushInputBg, Foreground = Brushes.White, BorderBrush = TacticalConfigPanel.BrushInputBorder, FontSize = 11, Margin = new Thickness(0, 2, 0, 6) };
            cardPins.Children.Add(_pinsListBox);

            var clearRow = new UniformGrid { Columns = 2 };
            clearRow.Children.Add(MakeBtn("− Wyczyść punkty", false, () => { _pins.Clear(); RefreshPinsList(); _ = RenderMapTilesAsync(); }));
            clearRow.Children.Add(MakeBtn("📏 Resetuj pomiar PPM", false, () => { _rulerStart = null; _rulerEnd = null; _ = RenderMapTilesAsync(); }));
            cardPins.Children.Add(clearRow);
            leftStack.Children.Add(WrapCard(cardPins));

            var cardExport = CreateSectionCard("4. Eksport do Paczki .ftak / KMZ / GPX / PDF");
            var btnAddToFtak = MakeBtn("📦 Zapisz mapę + warstwy do paczki .ftak (source/maps)", true, async () => await ExportMapToFtakSourceAsync(win));
            btnAddToFtak.Height = 34; btnAddToFtak.Margin = new Thickness(0, 0, 0, 6);
            cardExport.Children.Add(btnAddToFtak);

            var expRow1 = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 6) };
            expRow1.Children.Add(MakeBtn("🌐 Eksportuj KMZ (ATAK)", false, async () => await ExportStandaloneKmzAndGpxAsync(win, true, false)));
            expRow1.Children.Add(MakeBtn("🧭 Eksportuj GPX", false, async () => await ExportStandaloneKmzAndGpxAsync(win, false, true)));
            cardExport.Children.Add(expRow1);

            var expRow2 = new UniformGrid { Columns = 2 };
            expRow2.Children.Add(MakeBtn("📄 Zapisz mapę PDF (A4)", false, async () => await ExportTacticalMapPdfAsync(win)));
            expRow2.Children.Add(MakeBtn("🖨️ Drukuj mapę A4", false, () => PrintTacticalMap()));
            cardExport.Children.Add(expRow2);

            leftStack.Children.Add(WrapCard(cardExport));
            leftScroll.Content = leftStack;
            Grid.SetColumn(leftScroll, 0);
            bodyGrid.Children.Add(leftScroll);

            var mapBorder = new Border { Background = new SolidColorBrush(Color.FromRgb(5, 12, 15)), BorderBrush = TacticalConfigPanel.BrushCardBorder, BorderThickness = new Thickness(1, 0, 0, 0), ClipToBounds = true };
            var mapOverlayGrid = new Grid();
            _mapCanvas = new Canvas { Background = new SolidColorBrush(Color.FromRgb(6, 15, 18)), ClipToBounds = true, Cursor = Cursors.Cross };

            _mapCanvas.MouseLeftButtonDown += (_, e) =>
            {
                _isDragging = true;
                _dragStartPoint = e.GetPosition(_mapCanvas);
                _dragStartLat = _centerLat; _dragStartLon = _centerLon;
                _mapCanvas.CaptureMouse();
            };

            _mapCanvas.MouseRightButtonDown += (_, e) =>
            {
                Point p = e.GetPosition(_mapCanvas);
                var pt = ScreenToLatLon(p.X, p.Y, _mapCanvas.ActualWidth, _mapCanvas.ActualHeight);
                if (_rulerStart == null || (_rulerStart != null && _rulerEnd != null))
                {
                    _rulerStart = pt;
                    _rulerEnd = null;
                }
                else
                {
                    _rulerEnd = pt;
                }
                _ = RenderMapTilesAsync();
            };

            _mapCanvas.MouseMove += (_, e) =>
            {
                Point p = e.GetPosition(_mapCanvas);
                var (hoverLat, hoverLon) = ScreenToLatLon(p.X, p.Y, _mapCanvas.ActualWidth, _mapCanvas.ActualHeight);
                if (_coordsBadge != null)
                    _coordsBadge.Text = $"WGS84: {hoverLat:0.00000}, {hoverLon:0.00000}  |  Zoom: {_zoom}  |  LPM = Punkt/Przesuń  |  PPM = Linijka pomiaru";

                if (_isDragging)
                {
                    double dx = p.X - _dragStartPoint.X, dy = p.Y - _dragStartPoint.Y;
                    double worldSize = 256.0 * Math.Pow(2, _zoom);
                    _centerLon = Math.Clamp(_dragStartLon - dx * (360.0 / worldSize), -180.0, 180.0);
                    _centerLat = Math.Clamp(_dragStartLat + dy * ((360.0 / worldSize) * Math.Cos(_dragStartLat * Math.PI / 180.0)), -85.0, 85.0);
                    _latInputBox.Text = _centerLat.ToString("0.0000", CultureInfo.InvariantCulture);
                    _lonInputBox.Text = _centerLon.ToString("0.0000", CultureInfo.InvariantCulture);
                }
            };

            _mapCanvas.MouseLeftButtonUp += (_, e) =>
            {
                if (!_isDragging) return;
                _isDragging = false;
                _mapCanvas.ReleaseMouseCapture();
                Point endPt = e.GetPosition(_mapCanvas);
                if ((endPt - _dragStartPoint).Length < 5.0)
                {
                    var (clickLat, clickLon) = ScreenToLatLon(endPt.X, endPt.Y, _mapCanvas.ActualWidth, _mapCanvas.ActualHeight);
                    string facName = _pinFactionCombo?.SelectedItem?.ToString() ?? "BLUFOR";
                    var facInfo = TacticalConfigPanel.GetConfiguredFactions().FirstOrDefault(f => f.Name.Equals(facName, StringComparison.OrdinalIgnoreCase));
                    string hex = string.IsNullOrEmpty(facInfo.ColorHex) ? "#1FE0C5" : facInfo.ColorHex;
                    string pType = _pinTypeCombo?.SelectedItem?.ToString() ?? "HQ";
                    string pName = string.IsNullOrWhiteSpace(_pinNameBox?.Text) ? $"Punkt-{_pins.Count + 1}" : _pinNameBox.Text.Trim();

                    _pins.Add(new TacticalMapPin { Name = pName, Faction = facName, ColorHex = hex, Lat = clickLat, Lon = clickLon, Type = pType });
                    _pinNameBox.Text = $"Punkt-{_pins.Count + 1}";
                    RefreshPinsList();
                }
                _ = RenderMapTilesAsync();
            };

            _mapCanvas.MouseWheel += (_, e) =>
            {
                int oldZoom = _zoom;
                _zoom = e.Delta > 0 ? Math.Min(19, _zoom + 1) : Math.Max(5, _zoom - 1);
                if (_zoom != oldZoom) _ = RenderMapTilesAsync();
            };

            _mapCanvas.SizeChanged += (_, __) => { _ = RenderMapTilesAsync(); };
            mapOverlayGrid.Children.Add(_mapCanvas);

            var hudBorder = new Border { Background = new SolidColorBrush(Color.FromArgb(215, 8, 19, 22)), BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(14), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
            _coordsBadge = new TextBlock { Text = $"WGS84: {_centerLat:0.00000}, {_centerLon:0.00000}  |  Zoom: {_zoom}  |  LPM = Punkt/Przesuń  |  PPM = Linijka pomiaru", Foreground = Brushes.White, FontSize = 11.5, FontWeight = FontWeights.SemiBold };
            hudBorder.Child = _coordsBadge;
            mapOverlayGrid.Children.Add(hudBorder);

            mapBorder.Child = mapOverlayGrid;
            Grid.SetColumn(mapBorder, 1);
            bodyGrid.Children.Add(mapBorder);

            Grid.SetRow(bodyGrid, 0);
            mainGrid.Children.Add(bodyGrid);

            var bottomBar = new Border { Background = TacticalConfigPanel.BrushInputBg, BorderBrush = TacticalConfigPanel.BrushCardBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(16, 0, 16, 0) };
            _statusText = new TextBlock { Text = "Gotowy. Linijka skali widoczna w lewym dolnym rogu mapy. Kliknij PPM w dwa punkty na mapie, aby zmierzyć dystans i azymut.", Foreground = TacticalConfigPanel.BrushAccent, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
            bottomBar.Child = _statusText;
            Grid.SetRow(bottomBar, 1);
            mainGrid.Children.Add(bottomBar);

            rootBorder.Child = mainGrid;
            return rootBorder;
        }

        private static StackPanel CreateSectionCard(string title)
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 12.5, Margin = new Thickness(0, 0, 0, 8) });
            return sp;
        }

        private static Border WrapCard(UIElement child) => new Border { Background = TacticalConfigPanel.BrushItemBg, BorderBrush = TacticalConfigPanel.BrushTileBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 12), Child = child };

        private static Button MakeBtn(string text, bool accent, Action onClick)
        {
            var b = new Button { Content = text, Height = 30, Margin = new Thickness(2), Padding = new Thickness(8, 0, 8, 0), Background = accent ? TacticalConfigPanel.BrushButtonAccent : TacticalConfigPanel.BrushInputBg, Foreground = accent ? TacticalConfigPanel.BrushDarkFg : Brushes.White, BorderBrush = accent ? TacticalConfigPanel.BrushAccent : TacticalConfigPanel.BrushInputBorder, BorderThickness = new Thickness(1), FontSize = 11, FontWeight = FontWeights.Bold, Cursor = Cursors.Hand };
            b.Click += (_, __) => onClick();
            return b;
        }

        private static void RefreshPinsList()
        {
            if (_pinsListBox == null) return;
            _pinsListBox.Items.Clear();
            foreach (var p in _pins) _pinsListBox.Items.Add($"[{p.Faction}] {p.Name} ({p.Type}) — {p.Lat:0.0000}, {p.Lon:0.0000}");
        }

        private static (double X, double Y) LatLonToTileXY(double lat, double lon, int zoom)
        {
            double latRad = Math.Clamp(lat, -85.0511, 85.0511) * Math.PI / 180.0;
            double n = Math.Pow(2.0, zoom);
            return ((lon + 180.0) / 360.0 * n, (1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * n);
        }

        private static (double Lat, double Lon) TileXYToLatLon(double x, double y, int zoom)
        {
            double n = Math.Pow(2.0, zoom);
            double lon = x / n * 360.0 - 180.0;
            double lat = Math.Atan(Math.Sinh(Math.PI * (1.0 - 2.0 * y / n))) * 180.0 / Math.PI;
            return (lat, lon);
        }

        private static (double Lat, double Lon) ScreenToLatLon(double screenX, double screenY, double viewW, double viewH)
        {
            var (cx, cy) = LatLonToTileXY(_centerLat, _centerLon, _zoom);
            return TileXYToLatLon(cx + (screenX - viewW / 2.0) / 256.0, cy + (screenY - viewH / 2.0) / 256.0, _zoom);
        }

        private static Point LatLonToScreen(double lat, double lon, double viewW, double viewH)
        {
            var (cx, cy) = LatLonToTileXY(_centerLat, _centerLon, _zoom);
            var (px, py) = LatLonToTileXY(lat, lon, _zoom);
            return new Point(viewW / 2.0 + (px - cx) * 256.0, viewH / 2.0 + (py - cy) * 256.0);
        }

        private static string GetEpsg3857BBox(int tx, int ty, int zoom)
        {
            var (latNorth, lonWest) = TileXYToLatLon(tx, ty, zoom);
            var (latSouth, lonEast) = TileXYToLatLon(tx + 1, ty + 1, zoom);
            (double mx, double my) ToMercator(double lat, double lon) => (lon * 20037508.34 / 180.0, Math.Log(Math.Tan((90.0 + lat) * Math.PI / 360.0)) / (Math.PI / 180.0) * 20037508.34 / 180.0);
            var (minX, minY) = ToMercator(latSouth, lonWest);
            var (maxX, maxY) = ToMercator(latNorth, lonEast);
            return string.Create(CultureInfo.InvariantCulture, $"{minX:0.00},{minY:0.00},{maxX:0.00},{maxY:0.00}");
        }

        private static string BuildTileUrl(TacticalMapLayerDef layer, int tx, int ty, int zoom)
        {
            if (layer.IsWmsEpsg3857) return layer.UrlTemplate.Replace("{bbox}", GetEpsg3857BBox(tx, ty, zoom));
            return layer.UrlTemplate.Replace("{z}", zoom.ToString()).Replace("{x}", tx.ToString()).Replace("{y}", ty.ToString());
        }

        private static async Task<byte[]> FetchTileBytesAsync(string url)
        {
            if (TileCache.TryGetValue(url, out var cached)) return cached;
            try
            {
                byte[] data = await Http.GetByteArrayAsync(url);
                if (data != null && data.Length > 100) { TileCache[url] = data; return data; }
            }
            catch { }
            return null;
        }

        private static async Task RenderMapTilesAsync()
        {
            if (_mapCanvas == null || _activeLayer == null || _fullScreenOverlay?.Visibility != Visibility.Visible) return;
            double w = _mapCanvas.ActualWidth > 100 ? _mapCanvas.ActualWidth : 900;
            double h = _mapCanvas.ActualHeight > 100 ? _mapCanvas.ActualHeight : 650;

            var (centerTileX, centerTileY) = LatLonToTileXY(_centerLat, _centerLon, _zoom);
            int minTx = (int)Math.Floor(centerTileX - (w / 2.0) / 256.0) - 1;
            int maxTx = (int)Math.Floor(centerTileX + (w / 2.0) / 256.0) + 1;
            int minTy = (int)Math.Floor(centerTileY - (h / 2.0) / 256.0) - 1;
            int maxTy = (int)Math.Floor(centerTileY + (h / 2.0) / 256.0) + 1;
            int maxIndex = (1 << _zoom) - 1;

            var tileJobs = new List<(int Tx, int Ty, double ScreenX, double ScreenY, string Url)>();
            for (int tx = minTx; tx <= maxTx; tx++)
            {
                for (int ty = minTy; ty <= maxTy; ty++)
                {
                    if (ty < 0 || ty > maxIndex) continue;
                    int wrappedTx = ((tx % (maxIndex + 1)) + (maxIndex + 1)) % (maxIndex + 1);
                    tileJobs.Add((wrappedTx, ty, (w / 2.0) + (tx - centerTileX) * 256.0, (h / 2.0) + (ty - centerTileY) * 256.0, BuildTileUrl(_activeLayer, wrappedTx, ty, _zoom)));
                }
            }

            var fetchedTiles = await Task.WhenAll(tileJobs.Select(async job =>
            {
                byte[] bytes = await FetchTileBytesAsync(job.Url);
                byte[] hybBytes = (_showHybridLabels && !_activeLayer.Id.Equals("google_hyb")) ? await FetchTileBytesAsync($"https://mt1.google.com/vt/lyrs=h&x={job.Tx}&y={job.Ty}&z={_zoom}") : null;
                return (job.ScreenX, job.ScreenY, bytes, hybBytes);
            }));

            _mapCanvas.Children.Clear();
            foreach (var t in fetchedTiles)
            {
                if (t.bytes != null) AddCanvasTile(t.bytes, t.ScreenX, t.ScreenY);
                if (t.hybBytes != null) AddCanvasTile(t.hybBytes, t.ScreenX, t.ScreenY);
            }

            if (_showGrid) DrawTacticalGridOverlay(w, h);
            DrawTacticalPinsOverlay(w, h);
            DrawInteractiveDistanceRulerOverlay(w, h);
            if (_showScaleBar) DrawTopographicScaleBarOverlay(w, h);
        }

        private static void AddCanvasTile(byte[] bytes, double x, double y)
        {
            try
            {
                var bmp = new BitmapImage();
                using (var mem = new MemoryStream(bytes)) { bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.StreamSource = mem; bmp.EndInit(); bmp.Freeze(); }
                var img = new Image { Source = bmp, Width = 256.5, Height = 256.5, Stretch = Stretch.Fill };
                Canvas.SetLeft(img, x); Canvas.SetTop(img, y);
                _mapCanvas.Children.Add(img);
            }
            catch { }
        }

        private static void DrawTacticalGridOverlay(double w, double h)
        {
            var gridBrush = new SolidColorBrush(Color.FromArgb(90, 31, 224, 197));
            gridBrush.Freeze();
            double step = 128.0;

            for (double x = (w / 2.0) % step; x < w; x += step)
            {
                _mapCanvas.Children.Add(new Line { X1 = x, Y1 = 0, X2 = x, Y2 = h, Stroke = gridBrush, StrokeThickness = 0.8 });
                var (_, lon) = ScreenToLatLon(x, 20, w, h);
                var lbl = new TextBlock { Text = $"{lon:0.000}°E", Foreground = TacticalConfigPanel.BrushAccent, FontSize = 9.5 };
                Canvas.SetLeft(lbl, x + 3); Canvas.SetTop(lbl, 48); _mapCanvas.Children.Add(lbl);
            }

            for (double y = (h / 2.0) % step; y < h; y += step)
            {
                _mapCanvas.Children.Add(new Line { X1 = 0, Y1 = y, X2 = w, Y2 = y, Stroke = gridBrush, StrokeThickness = 0.8 });
                var (lat, _) = ScreenToLatLon(20, y, w, h);
                var lbl = new TextBlock { Text = $"{lat:0.000}°N", Foreground = TacticalConfigPanel.BrushAccent, FontSize = 9.5 };
                Canvas.SetLeft(lbl, 8); Canvas.SetTop(lbl, y + 2); _mapCanvas.Children.Add(lbl);
            }

            double cx = w / 2.0, cy = h / 2.0;
            _mapCanvas.Children.Add(new Line { X1 = cx - 15, Y1 = cy, X2 = cx + 15, Y2 = cy, Stroke = TacticalConfigPanel.BrushAccent, StrokeThickness = 1.5 });
            _mapCanvas.Children.Add(new Line { X1 = cx, Y1 = cy - 15, X2 = cx, Y2 = cy + 15, Stroke = TacticalConfigPanel.BrushAccent, StrokeThickness = 1.5 });
        }

        // =========================================================================
        // LINIJKA SKALI TOPOGRAFICZNEJ ORAZ INTERAKTYWNY DALMIERZ (PPM)
        // =========================================================================
        private static void DrawTopographicScaleBarOverlay(double w, double h)
        {
            double metersPerPixel = (156543.03392 * Math.Cos(_centerLat * Math.PI / 180.0)) / Math.Pow(2.0, _zoom);
            if (metersPerPixel <= 0.0001) return;

            double[] niceDistances = { 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000, 50000 };
            double chosenMeters = 500;
            foreach (var d in niceDistances)
            {
                double px = d / metersPerPixel;
                if (px >= 85 && px <= 240) { chosenMeters = d; break; }
            }

            double barWidthPx = Math.Clamp(chosenMeters / metersPerPixel, 60.0, 260.0);
            int scaleRatio = (int)(Math.Round((metersPerPixel / 0.0002645833) / 50.0) * 50);
            string distLabel = chosenMeters >= 1000 ? $"{chosenMeters / 1000.0:0.#} km" : $"{chosenMeters:0} m";
            string halfLabel = chosenMeters >= 1000 ? $"{(chosenMeters / 2000.0):0.##} km" : $"{(chosenMeters / 2.0):0} m";

            double boxW = barWidthPx + 36;
            double boxH = 58;
            double leftX = 16;
            double topY = Math.Max(60, h - boxH - 16);

            var panelBorder = new Border
            {
                Width = boxW,
                Height = boxH,
                Background = new SolidColorBrush(Color.FromArgb(225, 8, 19, 22)),
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(6),
                IsHitTestVisible = false
            };

            var innerCanvas = new Canvas();

            var hdr = new TextBlock
            {
                Text = $"SKALA ~ 1:{scaleRatio:N0}  ({metersPerPixel:0.0} m/px)",
                Foreground = TacticalConfigPanel.BrushAccent,
                FontSize = 10,
                FontWeight = FontWeights.Bold
            };
            Canvas.SetLeft(hdr, 12); Canvas.SetTop(hdr, 5);
            innerCanvas.Children.Add(hdr);

            // 4-segmentowa podziałka liniowa (naprzemienne segmenty)
            int segments = 4;
            double segW = barWidthPx / segments;
            double barY = 25;
            for (int s = 0; s < segments; s++)
            {
                var rect = new Rectangle
                {
                    Width = segW + 0.5,
                    Height = 8,
                    Fill = (s % 2 == 0) ? TacticalConfigPanel.BrushAccent : Brushes.White,
                    Stroke = Brushes.Black,
                    StrokeThickness = 0.8
                };
                Canvas.SetLeft(rect, 14 + s * segW);
                Canvas.SetTop(rect, barY);
                innerCanvas.Children.Add(rect);
            }

            // Znaczniki pionowe (0, 1/2, 1/1)
            double[] ticks = { 0, barWidthPx / 2.0, barWidthPx };
            string[] tickTexts = { "0", halfLabel, distLabel };
            for (int t = 0; t < ticks.Length; t++)
            {
                double tx = 14 + ticks[t];
                innerCanvas.Children.Add(new Line { X1 = tx, Y1 = barY - 3, X2 = tx, Y2 = barY + 11, Stroke = Brushes.White, StrokeThickness = 1.4 });
                var tLbl = new TextBlock { Text = tickTexts[t], Foreground = Brushes.White, FontSize = 9.5, FontWeight = FontWeights.SemiBold };
                Canvas.SetLeft(tLbl, t == 0 ? tx : (t == 1 ? tx - 14 : tx - 24));
                Canvas.SetTop(tLbl, barY + 11);
                innerCanvas.Children.Add(tLbl);
            }

            panelBorder.Child = innerCanvas;
            Canvas.SetLeft(panelBorder, leftX);
            Canvas.SetTop(panelBorder, topY);
            _mapCanvas.Children.Add(panelBorder);
        }

        private static void DrawInteractiveDistanceRulerOverlay(double w, double h)
        {
            if (_rulerStart == null) return;
            Point p1 = LatLonToScreen(_rulerStart.Value.Lat, _rulerStart.Value.Lon, w, h);
            var yellowBrush = TacticalConfigPanel.CreateFrozenBrush(245, 184, 46);

            var c1 = new Ellipse { Width = 10, Height = 10, Fill = yellowBrush, Stroke = Brushes.Black, StrokeThickness = 1.5 };
            Canvas.SetLeft(c1, p1.X - 5); Canvas.SetTop(c1, p1.Y - 5);
            _mapCanvas.Children.Add(c1);

            if (_rulerEnd != null)
            {
                Point p2 = LatLonToScreen(_rulerEnd.Value.Lat, _rulerEnd.Value.Lon, w, h);
                var line = new Line { X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y, Stroke = yellowBrush, StrokeThickness = 2.4, StrokeDashArray = new DoubleCollection { 4, 2 } };
                _mapCanvas.Children.Add(line);

                var c2 = new Ellipse { Width = 10, Height = 10, Fill = yellowBrush, Stroke = Brushes.Black, StrokeThickness = 1.5 };
                Canvas.SetLeft(c2, p2.X - 5); Canvas.SetTop(c2, p2.Y - 5);
                _mapCanvas.Children.Add(c2);

                double distMeters = ComputeHaversineMeters(_rulerStart.Value.Lat, _rulerStart.Value.Lon, _rulerEnd.Value.Lat, _rulerEnd.Value.Lon);
                double bearingDeg = ComputeBearingDegrees(_rulerStart.Value.Lat, _rulerStart.Value.Lon, _rulerEnd.Value.Lat, _rulerEnd.Value.Lon);
                int mils = (int)Math.Round(bearingDeg * 6400.0 / 360.0);
                string distStr = distMeters >= 1000 ? $"{distMeters / 1000.0:0.00} km ({distMeters:0} m)" : $"{distMeters:0.0} m";

                var badge = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(230, 8, 19, 22)),
                    BorderBrush = yellowBrush,
                    BorderThickness = new Thickness(1.5),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(8, 3, 8, 3),
                    IsHitTestVisible = false,
                    Child = new TextBlock { Text = $"📏 Pomiar: {distStr}  |  Azymut: {bearingDeg:0.0}° ({mils} mil)", Foreground = yellowBrush, FontSize = 11, FontWeight = FontWeights.Bold }
                };
                Canvas.SetLeft(badge, (p1.X + p2.X) / 2.0 + 8);
                Canvas.SetTop(badge, (p1.Y + p2.Y) / 2.0 - 14);
                _mapCanvas.Children.Add(badge);
            }
        }

        private static double ComputeHaversineMeters(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371000.0;
            double dLat = (lat2 - lat1) * Math.PI / 180.0;
            double dLon = (lon2 - lon1) * Math.PI / 180.0;
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return R * 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
        }

        private static double ComputeBearingDegrees(double lat1, double lon1, double lat2, double lon2)
        {
            double phi1 = lat1 * Math.PI / 180.0, phi2 = lat2 * Math.PI / 180.0;
            double dLon = (lon2 - lon1) * Math.PI / 180.0;
            double y = Math.Sin(dLon) * Math.Cos(phi2);
            double x = Math.Cos(phi1) * Math.Sin(phi2) - Math.Sin(phi1) * Math.Cos(phi2) * Math.Cos(dLon);
            return (Math.Atan2(y, x) * 180.0 / Math.PI + 360.0) % 360.0;
        }

        private static void DrawTacticalPinsOverlay(double w, double h)
        {
            foreach (var p in _pins)
            {
                Point pt = LatLonToScreen(p.Lat, p.Lon, w, h);
                if (pt.X < -50 || pt.X > w + 50 || pt.Y < -50 || pt.Y > h + 50) continue;

                var pinBrush = TacticalConfigPanel.BrushFromHex(p.ColorHex, Color.FromRgb(31, 224, 197));
                var dot = new Ellipse { Width = 14, Height = 14, Fill = pinBrush, Stroke = Brushes.White, StrokeThickness = 2 };
                Canvas.SetLeft(dot, pt.X - 7); Canvas.SetTop(dot, pt.Y - 7);
                _mapCanvas.Children.Add(dot);

                var tagBorder = new Border { Background = new SolidColorBrush(Color.FromArgb(220, 8, 19, 22)), BorderBrush = pinBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 2, 5, 2), Child = new TextBlock { Text = $"[{p.Faction}] {p.Name}", Foreground = Brushes.White, FontSize = 10.5, FontWeight = FontWeights.Bold } };
                Canvas.SetLeft(tagBorder, pt.X + 10); Canvas.SetTop(tagBorder, pt.Y - 10);
                _mapCanvas.Children.Add(tagBorder);
            }
        }

        private static byte[] CaptureMapCanvasPngBytes()
        {
            int w = (int)Math.Max(800, _mapCanvas.ActualWidth);
            int h = (int)Math.Max(600, _mapCanvas.ActualHeight);
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(_mapCanvas);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return ms.ToArray();
        }

        private static async Task ExportMapToFtakSourceAsync(Window win)
        {
            try
            {
                string sourceDir = TacticalConfigPanel.GetPrimarySourceDirectory(win);
                string mapsDir = System.IO.Path.Combine(sourceDir, "maps");
                string atakDir = System.IO.Path.Combine(sourceDir, "atak");
                Directory.CreateDirectory(mapsDir); Directory.CreateDirectory(atakDir);

                string title = TacticalConfigPanel.SanitizeFileName(_mapTitleBox?.Text ?? "Mapa_AO");
                byte[] pngBytes = CaptureMapCanvasPngBytes();
                double w = Math.Max(800, _mapCanvas.ActualWidth), h = Math.Max(600, _mapCanvas.ActualHeight);
                var (north, west) = ScreenToLatLon(0, 0, w, h);
                var (south, east) = ScreenToLatLon(w, h, w, h);

                string kmzPath = System.IO.Path.Combine(mapsDir, $"{title}.kmz");
                string xmlPath = System.IO.Path.Combine(mapsDir, $"{_activeLayer.Id}_online.xml");
                string gpxPath = System.IO.Path.Combine(atakDir, $"{title}_punkty.gpx");

                await Task.Run(() =>
                {
                    WriteKmzGroundOverlayArchive(kmzPath, title, pngBytes, north, south, east, west);
                    WriteAtakTileSourceXml(xmlPath, _activeLayer);
                    WriteGpxFile(gpxPath, title);
                });

                await TacticalConfigPanel.RefreshSourceSizeFromExternalAsync(win);
                if (_statusText != null) _statusText.Text = $"✓ Zapisano mapę KMZ ({kmzPath}), źródło online XML oraz punkty GPX bezpośrednio do folderu source paczki .ftak!";
            }
            catch (Exception ex) { if (_statusText != null) _statusText.Text = $"Błąd: {ex.Message}"; }
        }

        private static async Task ExportStandaloneKmzAndGpxAsync(Window win, bool exportKmz, bool exportGpx)
        {
            try
            {
                string sourceDir = TacticalConfigPanel.GetPrimarySourceDirectory(win);
                string parentDir = System.IO.Path.GetDirectoryName(System.IO.Path.TrimEndingDirectorySeparator(sourceDir)) ?? AppDomain.CurrentDomain.BaseDirectory;
                string outDir = System.IO.Path.Combine(parentDir, "release_maps");
                Directory.CreateDirectory(outDir);

                string title = TacticalConfigPanel.SanitizeFileName(_mapTitleBox?.Text ?? "Mapa_AO");
                byte[] pngBytes = CaptureMapCanvasPngBytes();
                double w = Math.Max(800, _mapCanvas.ActualWidth), h = Math.Max(600, _mapCanvas.ActualHeight);
                var (north, west) = ScreenToLatLon(0, 0, w, h);
                var (south, east) = ScreenToLatLon(w, h, w, h);

                await Task.Run(() =>
                {
                    if (exportKmz) WriteKmzGroundOverlayArchive(System.IO.Path.Combine(outDir, $"{title}.kmz"), title, pngBytes, north, south, east, west);
                    if (exportGpx) WriteGpxFile(System.IO.Path.Combine(outDir, $"{title}.gpx"), title);
                });

                if (_statusText != null) _statusText.Text = $"✓ Wyeksportowano pliki KMZ/GPX do folderu: {outDir}";
                TacticalConfigPanel.TryOpenFolderInExplorer(outDir);
            }
            catch { }
        }

        private static void WriteKmzGroundOverlayArchive(string kmzPath, string title, byte[] mapPngBytes, double north, double south, double east, double west)
        {
            if (File.Exists(kmzPath)) File.Delete(kmzPath);
            using var fs = new FileStream(kmzPath, FileMode.CreateNew, FileAccess.Write);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create, false, Encoding.UTF8);

            var imgEntry = zip.CreateEntry("files/map_overlay.png", CompressionLevel.Optimal);
            using (var s = imgEntry.Open()) s.Write(mapPngBytes, 0, mapPngBytes.Length);

            var sb = new StringBuilder();
            sb.AppendLine(@"<?xml version=""1.0"" encoding=""UTF-8""?><kml xmlns=""http://www.opengis.net/kml/2.2""><Document>");
            sb.AppendLine($"  <name>{title}</name><GroundOverlay><name>{title} ({_activeLayer?.Name})</name><Icon><href>files/map_overlay.png</href></Icon>");
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"    <LatLonBox><north>{north:0.000000}</north><south>{south:0.000000}</south><east>{east:0.000000}</east><west>{west:0.000000}</west></LatLonBox></GroundOverlay>"));
            foreach (var p in _pins)
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  <Placemark><name>[{p.Faction}] {p.Name}</name><Point><coordinates>{p.Lon:0.000000},{p.Lat:0.000000},0</coordinates></Point></Placemark>"));
            sb.AppendLine("</Document></kml>");

            var kmlEntry = zip.CreateEntry("doc.kml", CompressionLevel.Optimal);
            using var sw = new StreamWriter(kmlEntry.Open(), Encoding.UTF8);
            sw.Write(sb.ToString());
        }

        private static void WriteAtakTileSourceXml(string xmlPath, TacticalMapLayerDef layer)
        {
            if (layer == null) return;
            string url = layer.UrlTemplate.Replace("&", "&amp;");
            File.WriteAllText(xmlPath, $@"<?xml version=""1.0"" encoding=""UTF-8""?><customMapSource><name>FTH - {layer.Name}</name><minZoom>5</minZoom><maxZoom>19</maxZoom><tileType>jpg</tileType><tileUpdate>None</tileUpdate><url>{url}</url></customMapSource>", Encoding.UTF8);
        }

        private static void WriteGpxFile(string gpxPath, string title)
        {
            var sb = new StringBuilder();
            sb.AppendLine(@"<?xml version=""1.0"" encoding=""UTF-8""?><gpx version=""1.1"" creator=""Field TAK Hub Builder 2.4"" xmlns=""http://www.topografix.com/GPX/1/1"">");
            sb.AppendLine($"  <metadata><name>{title}</name></metadata>");
            foreach (var p in _pins)
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  <wpt lat=\"{p.Lat:0.000000}\" lon=\"{p.Lon:0.000000}\"><name>[{p.Faction}] {p.Name}</name><type>{p.Type}</type></wpt>"));
            sb.AppendLine("</gpx>");
            File.WriteAllText(gpxPath, sb.ToString(), Encoding.UTF8);
        }

        private static async Task ExportTacticalMapPdfAsync(Window win)
        {
            try
            {
                string sourceDir = TacticalConfigPanel.GetPrimarySourceDirectory(win);
                string parentDir = System.IO.Path.GetDirectoryName(System.IO.Path.TrimEndingDirectorySeparator(sourceDir)) ?? AppDomain.CurrentDomain.BaseDirectory;
                string outDir = System.IO.Path.Combine(parentDir, "release_maps");
                Directory.CreateDirectory(outDir);

                string title = TacticalConfigPanel.SanitizeFileName(_mapTitleBox?.Text ?? "Mapa_AO");
                string pdfPath = System.IO.Path.Combine(outDir, $"{title}_A4.pdf");

                const int pageW = 1240, pageH = 1754;
                int mapW = (int)Math.Max(800, _mapCanvas.ActualWidth), mapH = (int)Math.Max(600, _mapCanvas.ActualHeight);
                var mapRtb = new RenderTargetBitmap(mapW, mapH, 96, 96, PixelFormats.Pbgra32);
                mapRtb.Render(_mapCanvas);

                var pageVisual = new DrawingVisual();
                using (DrawingContext dc = pageVisual.RenderOpen())
                {
                    dc.DrawRectangle(TacticalConfigPanel.BrushCardBg, null, new Rect(0, 0, pageW, pageH));
                    dc.DrawRoundedRectangle(null, new Pen(TacticalConfigPanel.BrushAccent, 4), new Rect(30, 30, pageW - 60, pageH - 60), 14, 14);
                    TacticalConfigPanel.DrawCenteredText(dc, $"MAPA TAKTYCZNA OPERACJI: {title.ToUpperInvariant()}", 30, Brushes.White, pageW / 2.0, 60, true);
                    TacticalConfigPanel.DrawCenteredText(dc, $"Podkład: {_activeLayer?.Name}   |   Środek WGS84: {_centerLat:0.00000}, {_centerLon:0.00000}   |   Zoom: {_zoom}", 18, TacticalConfigPanel.BrushAccent, pageW / 2.0, 105, false);
                    dc.DrawRectangle(Brushes.White, new Pen(TacticalConfigPanel.BrushAccent, 3), new Rect(68, 150, pageW - 136, 1340));
                    dc.DrawImage(mapRtb, new Rect(70, 152, pageW - 140, 1336));
                    TacticalConfigPanel.DrawLeftText(dc, $"Punktów taktycznych na mapie: {_pins.Count}   |   Podziałka skali w lewym dolnym rogu mapy   |   Field TAK Hub 2.4 ({DateTime.Now:yyyy-MM-dd HH:mm})", 17, TacticalConfigPanel.BrushMutedText, 70, 1525, false);
                }

                var fullRtb = new RenderTargetBitmap(pageW, pageH, 96, 96, PixelFormats.Pbgra32);
                fullRtb.Render(pageVisual);
                var jpgEnc = new JpegBitmapEncoder { QualityLevel = 92 };
                jpgEnc.Frames.Add(BitmapFrame.Create(fullRtb));
                using var jpgMs = new MemoryStream();
                jpgEnc.Save(jpgMs);
                byte[] jpegBytes = jpgMs.ToArray();

                await Task.Run(() => WriteSingleImagePdf(pdfPath, jpegBytes, pageW, pageH));
                if (_statusText != null) _statusText.Text = $"✓ Zapisano gotowy do druku plik PDF A4 z linijką skali: {pdfPath}";
                TacticalConfigPanel.TryOpenFolderInExplorer(outDir);
            }
            catch (Exception ex) { if (_statusText != null) _statusText.Text = $"Błąd zapisu PDF: {ex.Message}"; }
        }

        private static void WriteSingleImagePdf(string pdfPath, byte[] jpegBytes, int pixelW, int pixelH)
        {
            using var fs = new FileStream(pdfPath, FileMode.Create, FileAccess.Write);
            void WriteAscii(string s) { byte[] b = Encoding.ASCII.GetBytes(s); fs.Write(b, 0, b.Length); }

            WriteAscii("%PDF-1.4\n");
            long obj1 = fs.Position; WriteAscii("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
            long obj2 = fs.Position; WriteAscii("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
            long obj3 = fs.Position; WriteAscii("3 0 obj\n<< /Type /Page /Parent 2 0 R /Resources << /XObject << /Im0 4 0 R >> >> /MediaBox [0 0 595 842] /Contents 5 0 R >>\nendobj\n");
            long obj4 = fs.Position; WriteAscii($"4 0 obj\n<< /Type /XObject /Subtype /Image /Width {pixelW} /Height {pixelH} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpegBytes.Length} >>\nstream\n");
            fs.Write(jpegBytes, 0, jpegBytes.Length);
            WriteAscii("\nendstream\nendobj\n");
            string contentCmd = "q\n595 0 0 842 0 0 cm\n/Im0 Do\nQ\n";
            long obj5 = fs.Position; WriteAscii($"5 0 obj\n<< /Length {contentCmd.Length} >>\nstream\n{contentCmd}endstream\nendobj\n");
            long xrefPos = fs.Position;
            WriteAscii($"xref\n0 6\n0000000000 65535 f \n{obj1:D10} 00000 n \n{obj2:D10} 00000 n \n{obj3:D10} 00000 n \n{obj4:D10} 00000 n \n{obj5:D10} 00000 n \ntrailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xrefPos}\n%%EOF\n");
        }

        private static void PrintTacticalMap()
        {
            try
            {
                var dlg = new PrintDialog();
                if (dlg.ShowDialog() == true && _mapCanvas != null) dlg.PrintVisual(_mapCanvas, _mapTitleBox?.Text ?? "Mapa Taktyczna Field TAK Hub");
            }
            catch { }
        }
    }
}
