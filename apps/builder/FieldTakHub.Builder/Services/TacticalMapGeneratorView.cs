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
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;

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

    public class TacticalMapShape
    {
        public string Name { get; set; }
        public string Faction { get; set; }
        public string ColorHex { get; set; }
        public string Kind { get; set; } // "Polygon", "Polyline", "Circle"
        public List<(double Lat, double Lon)> Points { get; set; } = new List<(double Lat, double Lon)>();
        public double RadiusMeters { get; set; } = 200;
    }

    public static class TacticalMapGeneratorView
    {
        private static readonly HttpClient Http = CreateHttpClient();
        private static readonly ConcurrentDictionary<string, byte[]> TileCache = new ConcurrentDictionary<string, byte[]>();

        private static UIElement _originalWindowContent;
        private static Border _fullScreenOverlay;
        private static Button _tabBuilderBtn;
        private static Button _tabFactionsBtn;
        private static Button _tabMapBtn;
        private static Canvas _mapCanvas;
        private static TextBlock _statusText;
        private static TextBlock _coordsBadge;
        private static ListBox _objectsListBox;

        private static double _centerLat = 49.9135;
        private static double _centerLon = 19.0238;
        private static int _zoom = 14;
        private static string _gridMode = "MGRS / UTM (Metryczna)";
        private static int _gridStepMeters = 500;
        private static string _gridColorName = "Turkusowy";
        private static bool _showScaleBar = true;
        private static bool _showCompassAndLegend = true;
        private static bool _showHybridLabels;
        private static bool _showLidarOverlay;
        private static double _lidarOpacity = 0.45;
        private static string _printOrientation = "A4 Poziomo (Landscape)";
        private static string _activeTool = "Pan / Punkt (LPM)";
        private static double _circleRadiusInput = 250;

        private static bool _isDragging;
        private static Point _dragStartPoint;
        private static double _dragStartLat, _dragStartLon;

        private static (double Lat, double Lon)? _rulerStart;
        private static (double Lat, double Lon)? _rulerEnd;

        private static readonly List<TacticalMapPin> _pins = new List<TacticalMapPin>();
        private static readonly List<TacticalMapShape> _shapes = new List<TacticalMapShape>();
        private static TacticalMapShape _currentDraftShape;

        private static TacticalMapLayerDef _activeLayer;
        private static TextBox _mapTitleBox, _searchQueryBox, _latInputBox, _lonInputBox, _pinNameBox;
        private static ComboBox _pinFactionCombo, _pinTypeCombo, _toolCombo;

        public static readonly List<TacticalMapLayerDef> Layers = new List<TacticalMapLayerDef>
        {
            new TacticalMapLayerDef { Id = "google_hyb", Name = "Google Maps — Hybryda (Satelita + Drogi i Nazwy)", IsWmsEpsg3857 = false, UrlTemplate = "https://mt1.google.com/vt/lyrs=y&x={x}&y={y}&z={z}" },
            new TacticalMapLayerDef { Id = "google_sat", Name = "Google Maps — Satelita (Ortofotomapa)", IsWmsEpsg3857 = false, UrlTemplate = "https://mt1.google.com/vt/lyrs=s&x={x}&y={y}&z={z}" },
            new TacticalMapLayerDef { Id = "google_ter", Name = "Google Maps — Teren (Warstwice / Fizyczna)", IsWmsEpsg3857 = false, UrlTemplate = "https://mt1.google.com/vt/lyrs=p&x={x}&y={y}&z={z}" },
            new TacticalMapLayerDef { Id = "google_map", Name = "Google Maps — Drogowa (Roadmap)", IsWmsEpsg3857 = false, UrlTemplate = "https://mt1.google.com/vt/lyrs=m&x={x}&y={y}&z={z}" },
            new TacticalMapLayerDef { Id = "geoportal_hires", Name = "Geoportal PL — Ortofotomapa HIGH-RES (Wysoka rozdz.)", IsWmsEpsg3857 = true, UrlTemplate = "https://mapy.geoportal.gov.pl/wss/service/PZGIK/ORTO/WMS/HighResolution?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=Raster&STYLES=&SRS=EPSG:3857&BBOX={bbox}&WIDTH=256&HEIGHT=256&FORMAT=image/jpeg" },
            new TacticalMapLayerDef { Id = "geoportal_std", Name = "Geoportal PL — Ortofotomapa Standard", IsWmsEpsg3857 = true, UrlTemplate = "https://mapy.geoportal.gov.pl/wss/service/PZGIK/ORTO/WMS/StandardResolution?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=Raster&STYLES=&SRS=EPSG:3857&BBOX={bbox}&WIDTH=256&HEIGHT=256&FORMAT=image/jpeg" },
            new TacticalMapLayerDef { Id = "geoportal_lidar", Name = "Geoportal PL — LIDAR (ISOK Cieniowanie NMT)", IsWmsEpsg3857 = true, UrlTemplate = "https://mapy.geoportal.gov.pl/wss/service/PZGIK/NMT/GRID1/WMS/ShadedRelief?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=Raster&STYLES=&SRS=EPSG:3857&BBOX={bbox}&WIDTH=256&HEIGHT=256&FORMAT=image/jpeg" },
            new TacticalMapLayerDef { Id = "geoportal_topo", Name = "Geoportal PL — Mapa Topograficzna (BDOT10k / Topo)", IsWmsEpsg3857 = true, UrlTemplate = "https://mapy.geoportal.gov.pl/wss/ext/KrajowaIntegracjaMapTopograficznych?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=raster&STYLES=&SRS=EPSG:3857&BBOX={bbox}&WIDTH=256&HEIGHT=256&FORMAT=image/png" },
            new TacticalMapLayerDef { Id = "esri_sat", Name = "ESRI — World Imagery (Ortofotomapa)", IsWmsEpsg3857 = false, UrlTemplate = "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}" },
            new TacticalMapLayerDef { Id = "esri_topo", Name = "ESRI — World Topographic Map", IsWmsEpsg3857 = false, UrlTemplate = "https://server.arcgisonline.com/ArcGIS/rest/services/World_Topo_Map/MapServer/tile/{z}/{y}/{x}" },
            new TacticalMapLayerDef { Id = "osm_std", Name = "OpenStreetMap — OSM Standard", IsWmsEpsg3857 = false, UrlTemplate = "https://tile.openstreetmap.org/{z}/{x}/{y}.png" },
            new TacticalMapLayerDef { Id = "open_topomap", Name = "OpenTopoMap — Mapa Topograficzna (Warstwice)", IsWmsEpsg3857 = false, UrlTemplate = "https://a.tile.opentopomap.org/{z}/{x}/{y}.png" },
            new TacticalMapLayerDef { Id = "open_ortomap", Name = "OpenOrtoMap / OpenAerial — Otwarta Fotomapa", IsWmsEpsg3857 = false, UrlTemplate = "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}" },
            new TacticalMapLayerDef { Id = "open_fotomap", Name = "OpenFotoMap / CyclOSM — Mapa Szlaków i Lasów", IsWmsEpsg3857 = false, UrlTemplate = "https://a.tile-cyclosm.openstreetmap.fr/cyclosm/{z}/{x}/{y}.png" }
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
                    var rightInfo = new TextBlock { Text = "Field TAK Hub 2.4  •  Google / Geoportal Hi-Res / LIDAR / Topo / ESRI / OSM", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
                    DockPanel.SetDock(rightInfo, Dock.Right);
                    navDock.Children.Add(rightInfo);

                    var tabsStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    _tabBuilderBtn = new Button { Content = "📦 Konfigurator Paczek (.ftak)", Height = 30, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(0, 0, 8, 0), Background = TacticalConfigPanel.BrushButtonAccent, Foreground = TacticalConfigPanel.BrushDarkFg, BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1), FontWeight = FontWeights.Bold, FontSize = 11.5, Cursor = Cursors.Hand };
                    _tabFactionsBtn = new Button { Content = "🚩 Strony Konfliktu (BLUFOR / OPFOR / OTS / Mesh)", Height = 30, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(0, 0, 8, 0), Background = TacticalConfigPanel.BrushItemBg, Foreground = Brushes.White, BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1), FontWeight = FontWeights.Bold, FontSize = 11.5, Cursor = Cursors.Hand };
                    _tabMapBtn = new Button { Content = "🗺️ Generator Map Taktycznych 1:1 (Pełny Ekran)", Height = 30, Padding = new Thickness(14, 0, 14, 0), Background = TacticalConfigPanel.BrushItemBg, Foreground = TacticalConfigPanel.BrushAccent, BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1), FontWeight = FontWeights.Bold, FontSize = 11.5, Cursor = Cursors.Hand };

                    _tabBuilderBtn.Click += (_, __) => HideFullScreenMapTab();
                    _tabFactionsBtn.Click += (_, __) => TacticalConfigPanel.ScrollToFactionsCard(win);
                    _tabMapBtn.Click += (_, __) => ShowFullScreenMapTab(win);

                    tabsStack.Children.Add(_tabBuilderBtn);
                    tabsStack.Children.Add(_tabFactionsBtn);
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
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(395) });
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var leftScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = TacticalConfigPanel.BrushCardBg, Padding = new Thickness(10) };
            var leftStack = new StackPanel { Margin = new Thickness(8) };

            // KARTA 1: Wyszukiwarka miejscowości Nominatim + Nawigacja WGS84
            var cardSearch = CreateSectionCard("1. Wyszukiwarka Terenu (Nominatim) & Obszar AO");
            _mapTitleBox = (TextBox)TacticalConfigPanel.TextCtrl("Operacja_Poligon_AO", _ => { });
            cardSearch.Children.Add(TacticalConfigPanel.FieldBox("Tytuł mapy / Kryptonim operacji (na arkuszu i w KMZ):", _mapTitleBox));

            var searchGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) });
            _searchQueryBox = (TextBox)TacticalConfigPanel.TextCtrl("Czechowice-Dziedzice", _ => { });
            Grid.SetColumn(_searchQueryBox, 0); searchGrid.Children.Add(_searchQueryBox);
            var btnSearch = MakeBtn("🔍 Szukaj", true, async () => await SearchLocationNominatimAsync());
            btnSearch.Height = 28; btnSearch.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(btnSearch, 1); searchGrid.Children.Add(btnSearch);
            cardSearch.Children.Add(TacticalConfigPanel.FieldBox("Wyszukaj miejscowość / las / adres:", searchGrid));

            var coordsGrid = new UniformGrid { Columns = 2 };
            _latInputBox = (TextBox)TacticalConfigPanel.TextCtrl(_centerLat.ToString("0.0000", CultureInfo.InvariantCulture), _ => { });
            _lonInputBox = (TextBox)TacticalConfigPanel.TextCtrl(_centerLon.ToString("0.0000", CultureInfo.InvariantCulture), _ => { });
            coordsGrid.Children.Add(TacticalConfigPanel.FieldBox("Szerokość (Lat):", _latInputBox));
            coordsGrid.Children.Add(TacticalConfigPanel.FieldBox("Długość (Lon):", _lonInputBox));
            cardSearch.Children.Add(coordsGrid);

            var zoomRow = new UniformGrid { Columns = 3, Margin = new Thickness(0, 2, 0, 0) };
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
            cardSearch.Children.Add(zoomRow);
            leftStack.Children.Add(WrapCard(cardSearch));

            // KARTA 2: Warstwy podkładowe + Cieniowanie LIDAR z suwakiem przezroczystości
            var cardLayer = CreateSectionCard("2. Warstwa Podkładowa & Nakładka LIDAR (ISOK)");
            var layerCombo = new ComboBox { ItemsSource = Layers, SelectedIndex = 0, Height = 28, FontSize = 11, Tag = "INJECTED" };
            TacticalConfigPanel.ApplyUnifiedComboBoxTheme(layerCombo, win);
            layerCombo.SelectionChanged += (_, __) => { if (layerCombo.SelectedItem is TacticalMapLayerDef def) { _activeLayer = def; _ = RenderMapTilesAsync(); } };
            cardLayer.Children.Add(TacticalConfigPanel.FieldBox("Mapa bazowa (Google / Geoportal Hi-Res / LIDAR / Topo / ESRI / OSM):", layerCombo));

            var chkLidar = new CheckBox { Content = "Nałóż półprzezroczysty LIDAR (Cieniowanie NMT ISOK)", IsChecked = false, Foreground = TacticalConfigPanel.BrushAccent, FontWeight = FontWeights.SemiBold, FontSize = 11, Margin = new Thickness(0, 4, 0, 4) };
            chkLidar.Checked += (_, __) => { _showLidarOverlay = true; _ = RenderMapTilesAsync(); };
            chkLidar.Unchecked += (_, __) => { _showLidarOverlay = false; _ = RenderMapTilesAsync(); };
            cardLayer.Children.Add(chkLidar);

            var lidarSlider = new Slider { Minimum = 0.1, Maximum = 0.9, Value = 0.45, Margin = new Thickness(0, 0, 0, 6) };
            lidarSlider.ValueChanged += (_, e) => { _lidarOpacity = e.NewValue; if (_showLidarOverlay) _ = RenderMapTilesAsync(); };
            cardLayer.Children.Add(TacticalConfigPanel.FieldBox("Przezroczystość nakładki LIDAR (10% – 90%):", lidarSlider));

            var chkHybrid = new CheckBox { Content = "Nałóż nazwy miejscowości i siatkę dróg (Hybrid)", IsChecked = false, Foreground = Brushes.White, FontSize = 11 };
            chkHybrid.Checked += (_, __) => { _showHybridLabels = true; _ = RenderMapTilesAsync(); };
            chkHybrid.Unchecked += (_, __) => { _showHybridLabels = false; _ = RenderMapTilesAsync(); };
            cardLayer.Children.Add(chkHybrid);
            leftStack.Children.Add(WrapCard(cardLayer));

            // KARTA 3: Siatka Taktyczna (MGRS/UTM / Sektorowa A1..H8 / WGS84), Skala i Kompas
            var cardGrid = CreateSectionCard("3. Siatka Taktyczna (MGRS / Sektorowa), Skala i Kompas");
            var gridRow1 = new UniformGrid { Columns = 2 };
            gridRow1.Children.Add(TacticalConfigPanel.FieldBox("Typ siatki taktycznej:", TacticalConfigPanel.ComboCtrl(new[] { "MGRS / UTM (Metryczna)", "Sektorowa MilSim (A1..H8)", "Geograficzna WGS84", "Brak siatki" }, _gridMode, win, v => { _gridMode = v; _ = RenderMapTilesAsync(); })));
            gridRow1.Children.Add(TacticalConfigPanel.FieldBox("Gęstość oczka (MGRS):", TacticalConfigPanel.ComboCtrl(new[] { "100", "250", "500", "1000" }, "500", win, v => { if (int.TryParse(v, out int st)) _gridStepMeters = st; _ = RenderMapTilesAsync(); })));
            cardGrid.Children.Add(gridRow1);

            var gridRow2 = new UniformGrid { Columns = 2 };
            gridRow2.Children.Add(TacticalConfigPanel.FieldBox("Kolor linii siatki:", TacticalConfigPanel.ComboCtrl(new[] { "Turkusowy", "Żółty", "Biały", "Czerwony", "Czarny" }, _gridColorName, win, v => { _gridColorName = v; _ = RenderMapTilesAsync(); })));
            gridRow2.Children.Add(TacticalConfigPanel.FieldBox("Orientacja arkusza PDF:", TacticalConfigPanel.ComboCtrl(new[] { "A4 Poziomo (Landscape)", "A4 Pionowo (Portrait)" }, _printOrientation, win, v => _printOrientation = v)));
            cardGrid.Children.Add(gridRow2);

            var chkScale = new CheckBox { Content = "Pokaż linijkę skali topograficznej, różę wiatrów (N) i legendę", IsChecked = true, Foreground = Brushes.White, FontSize = 11, Margin = new Thickness(0, 4, 0, 0) };
            chkScale.Checked += (_, __) => { _showScaleBar = true; _showCompassAndLegend = true; _ = RenderMapTilesAsync(); };
            chkScale.Unchecked += (_, __) => { _showScaleBar = false; _showCompassAndLegend = false; _ = RenderMapTilesAsync(); };
            cardGrid.Children.Add(chkScale);
            leftStack.Children.Add(WrapCard(cardGrid));

            // KARTA 4: Narzędzia Rysowania Taktycznego (Poligony AO, Trasy, Okręgi, Punkty, Dalmierz)
            var cardDraw = CreateSectionCard("4. Rysowanie Taktyczne (Strefy AO, Trasy, Okręgi, Punkty)");
            _toolCombo = (ComboBox)TacticalConfigPanel.ComboCtrl(new[]
            {
                "Pan / Punkt (LPM)",
                "Rysuj Poligon / Strefę AO (LPM)",
                "Rysuj Linię / Trasę (LPM)",
                "Rysuj Okrąg Zasięgu (LPM)",
                "Dalmierz / Linijka (LPM/PPM)"
            }, _activeTool, win, v =>
            {
                _activeTool = v;
                _currentDraftShape = null;
                _ = RenderMapTilesAsync();
            });
            cardDraw.Children.Add(TacticalConfigPanel.FieldBox("Aktywne narzędzie kursora na mapie:", _toolCombo));

            var pinFormGrid = new UniformGrid { Columns = 2 };
            _pinFactionCombo = new ComboBox { Height = 28, FontSize = 11, Tag = "INJECTED" };
            TacticalConfigPanel.ApplyUnifiedComboBoxTheme(_pinFactionCombo, win);
            RefreshFactionDropdown();
            _pinTypeCombo = (ComboBox)TacticalConfigPanel.ComboCtrl(new[] { "HQ (Sztab)", "Respawn / Baza", "MEDEVAC", "Strefa Zrzutu LZ", "Punkt Kontrolny CP", "Objective / Cel", "Granica AO / No-Go" }, "HQ (Sztab)", win, _ => { });
            pinFormGrid.Children.Add(TacticalConfigPanel.FieldBox("Przypisz do strony:", _pinFactionCombo));
            pinFormGrid.Children.Add(TacticalConfigPanel.FieldBox("Klasa obiektu:", _pinTypeCombo));
            cardDraw.Children.Add(pinFormGrid);

            var nameAndRadGrid = new Grid();
            nameAndRadGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            nameAndRadGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
            _pinNameBox = (TextBox)TacticalConfigPanel.TextCtrl("Obiekt-1", _ => { });
            var fObjName = TacticalConfigPanel.FieldBox("Nazwa punktu / strefy / trasy:", _pinNameBox);
            var fRad = TacticalConfigPanel.FieldBox("Promień (m):", TacticalConfigPanel.IntCtrl(250, v => _circleRadiusInput = Math.Max(10, v)));
            Grid.SetColumn(fObjName, 0); Grid.SetColumn(fRad, 1);
            nameAndRadGrid.Children.Add(fObjName); nameAndRadGrid.Children.Add(fRad);
            cardDraw.Children.Add(nameAndRadGrid);

            var polyActionsRow = new UniformGrid { Columns = 3, Margin = new Thickness(0, 2, 0, 4) };
            polyActionsRow.Children.Add(MakeBtn("✓ Zamknij poligon", true, () => { _currentDraftShape = null; RefreshObjectsList(); _ = RenderMapTilesAsync(); }));
            polyActionsRow.Children.Add(MakeBtn("↩ Cofnij ostatni", false, () =>
            {
                if (_shapes.Count > 0) _shapes.RemoveAt(_shapes.Count - 1);
                else if (_pins.Count > 0) _pins.RemoveAt(_pins.Count - 1);
                _currentDraftShape = null;
                RefreshObjectsList();
                _ = RenderMapTilesAsync();
            }));
            polyActionsRow.Children.Add(MakeBtn("📂 Import KML/GPX", false, () => ImportExternalKmlOrGpx()));
            cardDraw.Children.Add(polyActionsRow);

            _objectsListBox = new ListBox { Height = 80, Background = TacticalConfigPanel.BrushInputBg, Foreground = Brushes.White, BorderBrush = TacticalConfigPanel.BrushInputBorder, FontSize = 10.5, Margin = new Thickness(0, 2, 0, 6) };
            cardDraw.Children.Add(_objectsListBox);

            var clearRow = new UniformGrid { Columns = 2 };
            clearRow.Children.Add(MakeBtn("− Wyczyść obiekty", false, () => { _pins.Clear(); _shapes.Clear(); _currentDraftShape = null; RefreshObjectsList(); _ = RenderMapTilesAsync(); }));
            clearRow.Children.Add(MakeBtn("📏 Resetuj dalmierz", false, () => { _rulerStart = null; _rulerEnd = null; _ = RenderMapTilesAsync(); }));
            cardDraw.Children.Add(clearRow);
            leftStack.Children.Add(WrapCard(cardDraw));

            // KARTA 5: Kompletny Eksport (FTAK + KMZ + KML + GPX + PNG + PDF + Druk)
            var cardExport = CreateSectionCard("5. Eksport do Paczki .ftak / KMZ / KML / GPX / PDF / Druk");
            var btnAddToFtak = MakeBtn("📦 Zapisz mapę + strefy do paczki .ftak (source/maps & atak)", true, async () => await ExportMapToFtakSourceAsync(win));
            btnAddToFtak.Height = 34; btnAddToFtak.Margin = new Thickness(0, 0, 0, 6);
            cardExport.Children.Add(btnAddToFtak);

            var expRow1 = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 6) };
            expRow1.Children.Add(MakeBtn("🌐 Eksport KMZ", false, async () => await ExportStandaloneFilesAsync(win, "kmz")));
            expRow1.Children.Add(MakeBtn("📐 Eksport KML", false, async () => await ExportStandaloneFilesAsync(win, "kml")));
            expRow1.Children.Add(MakeBtn("🧭 Eksport GPX", false, async () => await ExportStandaloneFilesAsync(win, "gpx")));
            cardExport.Children.Add(expRow1);

            var expRow2 = new UniformGrid { Columns = 3 };
            expRow2.Children.Add(MakeBtn("🖼️ Obraz PNG", false, async () => await ExportStandaloneFilesAsync(win, "png")));
            expRow2.Children.Add(MakeBtn("📕 Zapisz PDF", true, async () => await ExportTacticalMapPdfAsync(win)));
            expRow2.Children.Add(MakeBtn("🖨️ Drukuj A4", false, () => PrintTacticalMap()));
            cardExport.Children.Add(expRow2);

            leftStack.Children.Add(WrapCard(cardExport));
            leftScroll.Content = leftStack;
            Grid.SetColumn(leftScroll, 0);
            bodyGrid.Children.Add(leftScroll);

            // Prawa część: Płótno Mapy
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
                if (_currentDraftShape != null)
                {
                    _currentDraftShape = null;
                    RefreshObjectsList();
                    _ = RenderMapTilesAsync();
                    return;
                }
                Point p = e.GetPosition(_mapCanvas);
                var pt = ScreenToLatLon(p.X, p.Y, _mapCanvas.ActualWidth, _mapCanvas.ActualHeight);
                if (_rulerStart == null || (_rulerStart != null && _rulerEnd != null)) { _rulerStart = pt; _rulerEnd = null; }
                else _rulerEnd = pt;
                _ = RenderMapTilesAsync();
            };

            _mapCanvas.MouseMove += (_, e) =>
            {
                Point p = e.GetPosition(_mapCanvas);
                var (hoverLat, hoverLon) = ScreenToLatLon(p.X, p.Y, _mapCanvas.ActualWidth, _mapCanvas.ActualHeight);
                if (_coordsBadge != null)
                    _coordsBadge.Text = $"WGS84: {hoverLat:0.00000}, {hoverLon:0.00000}  |  Zoom: {_zoom}  |  Narzędzie: {_activeTool}  |  PPM = Dalmierz / Zamknij poligon";

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
                    HandleMapClickToolAction(endPt);
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
            _coordsBadge = new TextBlock { Text = $"WGS84: {_centerLat:0.00000}, {_centerLon:0.00000}  |  Zoom: {_zoom}  |  LPM = Rysuj / Przesuń  |  PPM = Dalmierz", Foreground = Brushes.White, FontSize = 11.5, FontWeight = FontWeights.SemiBold };
            hudBorder.Child = _coordsBadge;
            mapOverlayGrid.Children.Add(hudBorder);

            mapBorder.Child = mapOverlayGrid;
            Grid.SetColumn(mapBorder, 1);
            bodyGrid.Children.Add(mapBorder);

            Grid.SetRow(bodyGrid, 0);
            mainGrid.Children.Add(bodyGrid);

            var bottomBar = new Border { Background = TacticalConfigPanel.BrushInputBg, BorderBrush = TacticalConfigPanel.BrushCardBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(16, 0, 16, 0) };
            _statusText = new TextBlock { Text = "Gotowy. Pełna funkcjonalność 1:1 z generatorem HTML: Wyszukiwarka Nominatim, Cieniowanie LIDAR, Siatka MGRS/Sektorowa, Poligony AO, Trasy i Eksport PDF/KMZ/GPX/FTAK.", Foreground = TacticalConfigPanel.BrushAccent, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
            bottomBar.Child = _statusText;
            Grid.SetRow(bottomBar, 1);
            mainGrid.Children.Add(bottomBar);

            rootBorder.Child = mainGrid;
            return rootBorder;
        }

        private static void HandleMapClickToolAction(Point clickPt)
        {
            var (clickLat, clickLon) = ScreenToLatLon(clickPt.X, clickPt.Y, _mapCanvas.ActualWidth, _mapCanvas.ActualHeight);
            string facName = _pinFactionCombo?.SelectedItem?.ToString() ?? "BLUFOR";
            var facInfo = TacticalConfigPanel.GetConfiguredFactions().FirstOrDefault(f => f.Name.Equals(facName, StringComparison.OrdinalIgnoreCase));
            string hex = string.IsNullOrEmpty(facInfo.ColorHex) ? "#1FE0C5" : facInfo.ColorHex;
            string pType = _pinTypeCombo?.SelectedItem?.ToString() ?? "HQ";
            string objName = string.IsNullOrWhiteSpace(_pinNameBox?.Text) ? $"Obiekt-{_pins.Count + _shapes.Count + 1}" : _pinNameBox.Text.Trim();

            if (_activeTool.Contains("Poligon") || _activeTool.Contains("Linię"))
            {
                string kind = _activeTool.Contains("Poligon") ? "Polygon" : "Polyline";
                if (_currentDraftShape == null || _currentDraftShape.Kind != kind)
                {
                    _currentDraftShape = new TacticalMapShape { Name = objName, Faction = facName, ColorHex = hex, Kind = kind };
                    _shapes.Add(_currentDraftShape);
                    _pinNameBox.Text = $"Strefa-{_shapes.Count + 1}";
                }
                _currentDraftShape.Points.Add((clickLat, clickLon));
                RefreshObjectsList();
            }
            else if (_activeTool.Contains("Okrąg"))
            {
                var circle = new TacticalMapShape
                {
                    Name = $"{objName} (R={_circleRadiusInput:0}m)",
                    Faction = facName,
                    ColorHex = hex,
                    Kind = "Circle",
                    RadiusMeters = _circleRadiusInput
                };
                circle.Points.Add((clickLat, clickLon));
                _shapes.Add(circle);
                _pinNameBox.Text = $"Okrąg-{_shapes.Count + 1}";
                RefreshObjectsList();
            }
            else if (_activeTool.Contains("Dalmierz"))
            {
                if (_rulerStart == null || (_rulerStart != null && _rulerEnd != null)) { _rulerStart = (clickLat, clickLon); _rulerEnd = null; }
                else _rulerEnd = (clickLat, clickLon);
            }
            else
            {
                _pins.Add(new TacticalMapPin { Name = objName, Faction = facName, ColorHex = hex, Lat = clickLat, Lon = clickLon, Type = pType });
                _pinNameBox.Text = $"Punkt-{_pins.Count + 1}";
                RefreshObjectsList();
            }
        }

        private static async Task SearchLocationNominatimAsync()
        {
            string q = _searchQueryBox?.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(q)) return;
            try
            {
                if (_statusText != null) _statusText.Text = $"Wyszukiwanie lokalizacji: {q}...";
                string url = $"https://nominatim.openstreetmap.org/search?format=json&limit=1&q={Uri.EscapeDataString(q)}";
                string json = await Http.GetStringAsync(url);
                var arr = JsonNode.Parse(json) as JsonArray;
                if (arr != null && arr.Count > 0 && arr[0] is JsonObject first)
                {
                    string latStr = first["lat"]?.ToString() ?? "";
                    string lonStr = first["lon"]?.ToString() ?? "";
                    string disp = first["display_name"]?.ToString() ?? q;
                    if (double.TryParse(latStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double la) &&
                        double.TryParse(lonStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double lo))
                    {
                        _centerLat = la; _centerLon = lo;
                        _latInputBox.Text = _centerLat.ToString("0.0000", CultureInfo.InvariantCulture);
                        _lonInputBox.Text = _centerLon.ToString("0.0000", CultureInfo.InvariantCulture);
                        if (_statusText != null) _statusText.Text = $"✓ Znaleziono: {disp} ({la:0.0000}, {lo:0.0000})";
                        await RenderMapTilesAsync();
                    }
                }
                else if (_statusText != null) _statusText.Text = $"⚠ Nie znaleziono lokalizacji dla: {q}";
            }
            catch (Exception ex) { if (_statusText != null) _statusText.Text = $"Błąd wyszukiwarki: {ex.Message}"; }
        }

        private static void ImportExternalKmlOrGpx()
        {
            try
            {
                var dlg = new OpenFileDialog
                {
                    Title = "Importuj punkty i trasy z pliku KML / GPX",
                    Filter = "Pliki taktyczne (*.kml;*.gpx)|*.kml;*.gpx|Wszystkie pliki (*.*)|*.*"
                };
                if (dlg.ShowDialog() != true) return;

                string content = File.ReadAllText(dlg.FileName, Encoding.UTF8);
                string facName = _pinFactionCombo?.SelectedItem?.ToString() ?? "BLUFOR";
                var facInfo = TacticalConfigPanel.GetConfiguredFactions().FirstOrDefault(f => f.Name.Equals(facName, StringComparison.OrdinalIgnoreCase));
                string hex = string.IsNullOrEmpty(facInfo.ColorHex) ? "#1FE0C5" : facInfo.ColorHex;

                int imported = 0;
                foreach (Match m in Regex.Matches(content, @"<wpt[^>]*lat=""([0-9\.\-]+)""[^>]*lon=""([0-9\.\-]+)""", RegexOptions.IgnoreCase))
                {
                    if (double.TryParse(m.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double la) &&
                        double.TryParse(m.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double lo))
                    {
                        _pins.Add(new TacticalMapPin { Name = $"GPX-{_pins.Count + 1}", Faction = facName, ColorHex = hex, Lat = la, Lon = lo, Type = "Import GPX" });
                        _centerLat = la; _centerLon = lo;
                        imported++;
                    }
                }

                foreach (Match m in Regex.Matches(content, @"<coordinates>\s*([0-9\.\-]+),([0-9\.\-]+)", RegexOptions.IgnoreCase))
                {
                    if (double.TryParse(m.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double lo) &&
                        double.TryParse(m.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double la))
                    {
                        _pins.Add(new TacticalMapPin { Name = $"KML-{_pins.Count + 1}", Faction = facName, ColorHex = hex, Lat = la, Lon = lo, Type = "Import KML" });
                        _centerLat = la; _centerLon = lo;
                        imported++;
                    }
                }

                RefreshObjectsList();
                _ = RenderMapTilesAsync();
                if (_statusText != null) _statusText.Text = $"✓ Zaimportowano {imported} punktów z pliku: {System.IO.Path.GetFileName(dlg.FileName)}";
            }
            catch { }
        }

        private static StackPanel CreateSectionCard(string title)
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 12, Margin = new Thickness(0, 0, 0, 6) });
            return sp;
        }

        private static Border WrapCard(UIElement child) => new Border { Background = TacticalConfigPanel.BrushItemBg, BorderBrush = TacticalConfigPanel.BrushTileBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 10), Child = child };

        private static Button MakeBtn(string text, bool accent, Action onClick)
        {
            var b = new Button { Content = text, Height = 28, Margin = new Thickness(2), Padding = new Thickness(6, 0, 6, 0), Background = accent ? TacticalConfigPanel.BrushButtonAccent : TacticalConfigPanel.BrushInputBg, Foreground = accent ? TacticalConfigPanel.BrushDarkFg : Brushes.White, BorderBrush = accent ? TacticalConfigPanel.BrushAccent : TacticalConfigPanel.BrushInputBorder, BorderThickness = new Thickness(1), FontSize = 10.5, FontWeight = FontWeights.Bold, Cursor = Cursors.Hand };
            b.Click += (_, __) => onClick();
            return b;
        }

        private static void RefreshObjectsList()
        {
            if (_objectsListBox == null) return;
            _objectsListBox.Items.Clear();
            foreach (var s in _shapes) _objectsListBox.Items.Add($"[{s.Faction}] {s.Kind}: {s.Name} (pkt: {s.Points.Count})");
            foreach (var p in _pins) _objectsListBox.Items.Add($"[{p.Faction}] {p.Name} ({p.Type}) — {p.Lat:0.0000}, {p.Lon:0.0000}");
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

            var lidarLayer = Layers.FirstOrDefault(l => l.Id == "geoportal_lidar");
            var tileJobs = new List<(int Tx, int Ty, double ScreenX, double ScreenY, string Url, string LidarUrl)>();
            for (int tx = minTx; tx <= maxTx; tx++)
            {
                for (int ty = minTy; ty <= maxTy; ty++)
                {
                    if (ty < 0 || ty > maxIndex) continue;
                    int wrappedTx = ((tx % (maxIndex + 1)) + (maxIndex + 1)) % (maxIndex + 1);
                    string lUrl = (_showLidarOverlay && lidarLayer != null && _activeLayer.Id != "geoportal_lidar") ? BuildTileUrl(lidarLayer, wrappedTx, ty, _zoom) : null;
                    tileJobs.Add((wrappedTx, ty, (w / 2.0) + (tx - centerTileX) * 256.0, (h / 2.0) + (ty - centerTileY) * 256.0, BuildTileUrl(_activeLayer, wrappedTx, ty, _zoom), lUrl));
                }
            }

            var fetchedTiles = await Task.WhenAll(tileJobs.Select(async job =>
            {
                byte[] bytes = await FetchTileBytesAsync(job.Url);
                byte[] lidarBytes = job.LidarUrl != null ? await FetchTileBytesAsync(job.LidarUrl) : null;
                byte[] hybBytes = (_showHybridLabels && !_activeLayer.Id.Equals("google_hyb")) ? await FetchTileBytesAsync($"https://mt1.google.com/vt/lyrs=h&x={job.Tx}&y={job.Ty}&z={_zoom}") : null;
                return (job.ScreenX, job.ScreenY, bytes, lidarBytes, hybBytes);
            }));

            _mapCanvas.Children.Clear();
            foreach (var t in fetchedTiles)
            {
                if (t.bytes != null) AddCanvasTile(t.bytes, t.ScreenX, t.ScreenY, 1.0);
                if (t.lidarBytes != null) AddCanvasTile(t.lidarBytes, t.ScreenX, t.ScreenY, _lidarOpacity);
                if (t.hybBytes != null) AddCanvasTile(t.hybBytes, t.ScreenX, t.ScreenY, 1.0);
            }

            DrawSelectedGridSystemOverlay(w, h);
            DrawTacticalShapesAndPinsOverlay(w, h);
            DrawInteractiveDistanceRulerOverlay(w, h);
            if (_showScaleBar) DrawTopographicScaleBarOverlay(w, h);
            if (_showCompassAndLegend) DrawCompassRoseAndLegendOverlay(w, h);
        }

        private static void AddCanvasTile(byte[] bytes, double x, double y, double opacity)
        {
            try
            {
                var bmp = new BitmapImage();
                using (var mem = new MemoryStream(bytes)) { bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.StreamSource = mem; bmp.EndInit(); bmp.Freeze(); }
                var img = new Image { Source = bmp, Width = 256.5, Height = 256.5, Stretch = Stretch.Fill, Opacity = opacity };
                Canvas.SetLeft(img, x); Canvas.SetTop(img, y);
                _mapCanvas.Children.Add(img);
            }
            catch { }
        }

        private static Brush GetGridColorBrush(byte alpha = 115)
        {
            Color c = _gridColorName switch
            {
                "Żółty" => Color.FromArgb(alpha, 245, 184, 46),
                "Biały" => Color.FromArgb(alpha, 255, 255, 255),
                "Czerwony" => Color.FromArgb(alpha, 235, 64, 52),
                "Czarny" => Color.FromArgb(alpha, 10, 10, 10),
                _ => Color.FromArgb(alpha, 31, 224, 197)
            };
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private static void DrawSelectedGridSystemOverlay(double w, double h)
        {
            if (_gridMode == "Brak siatki") return;
            var lineBrush = GetGridColorBrush(115);
            var textBrush = GetGridColorBrush(230);

            double metersPerPixel = Math.Max(0.05, (156543.03392 * Math.Cos(_centerLat * Math.PI / 180.0)) / Math.Pow(2.0, _zoom));

            if (_gridMode.StartsWith("Sektorowa"))
            {
                // Siatka sektorowa 8 x 8 (A1 .. H8)
                int cols = 8, rows = 8;
                double cellW = w / cols, cellH = h / rows;
                for (int c = 1; c < cols; c++)
                    _mapCanvas.Children.Add(new Line { X1 = c * cellW, Y1 = 0, X2 = c * cellW, Y2 = h, Stroke = lineBrush, StrokeThickness = 1.1 });
                for (int r = 1; r < rows; r++)
                    _mapCanvas.Children.Add(new Line { X1 = 0, Y1 = r * cellH, X2 = w, Y2 = r * cellH, Stroke = lineBrush, StrokeThickness = 1.1 });

                for (int r = 0; r < rows; r++)
                {
                    char rowChar = (char)('A' + r);
                    for (int c = 0; c < cols; c++)
                    {
                        var secLbl = new TextBlock { Text = $"{rowChar}{c + 1}", Foreground = textBrush, FontSize = 11, FontWeight = FontWeights.Bold };
                        Canvas.SetLeft(secLbl, c * cellW + 6);
                        Canvas.SetTop(secLbl, r * cellH + 4);
                        _mapCanvas.Children.Add(secLbl);
                    }
                }
            }
            else if (_gridMode.StartsWith("MGRS"))
            {
                double stepPx = Math.Clamp(_gridStepMeters / metersPerPixel, 45.0, 320.0);
                int idxX = 0;
                for (double x = (w / 2.0) % stepPx; x < w; x += stepPx)
                {
                    _mapCanvas.Children.Add(new Line { X1 = x, Y1 = 0, X2 = x, Y2 = h, Stroke = lineBrush, StrokeThickness = 0.9 });
                    var (_, lon) = ScreenToLatLon(x, 20, w, h);
                    int utmEast = (int)Math.Round(((lon + 180.0) % 6.0) * 111320.0 * Math.Cos(_centerLat * Math.PI / 180.0));
                    var lbl = new TextBlock { Text = $"{(utmEast / 100) % 1000:D3}", Foreground = textBrush, FontSize = 9.5, FontWeight = FontWeights.Bold };
                    Canvas.SetLeft(lbl, x + 3); Canvas.SetTop(lbl, 48);
                    _mapCanvas.Children.Add(lbl);
                    idxX++;
                }

                for (double y = (h / 2.0) % stepPx; y < h; y += stepPx)
                {
                    _mapCanvas.Children.Add(new Line { X1 = 0, Y1 = y, X2 = w, Y2 = y, Stroke = lineBrush, StrokeThickness = 0.9 });
                    var (lat, _) = ScreenToLatLon(20, y, w, h);
                    int utmNorth = (int)Math.Round(lat * 110574.0);
                    var lbl = new TextBlock { Text = $"{(utmNorth / 100) % 1000:D3}", Foreground = textBrush, FontSize = 9.5, FontWeight = FontWeights.Bold };
                    Canvas.SetLeft(lbl, 8); Canvas.SetTop(lbl, y + 2);
                    _mapCanvas.Children.Add(lbl);
                }
            }
            else
            {
                double step = 128.0;
                for (double x = (w / 2.0) % step; x < w; x += step)
                {
                    _mapCanvas.Children.Add(new Line { X1 = x, Y1 = 0, X2 = x, Y2 = h, Stroke = lineBrush, StrokeThickness = 0.8 });
                    var (_, lon) = ScreenToLatLon(x, 20, w, h);
                    var lbl = new TextBlock { Text = $"{lon:0.000}°E", Foreground = textBrush, FontSize = 9.5 };
                    Canvas.SetLeft(lbl, x + 3); Canvas.SetTop(lbl, 48); _mapCanvas.Children.Add(lbl);
                }
                for (double y = (h / 2.0) % step; y < h; y += step)
                {
                    _mapCanvas.Children.Add(new Line { X1 = 0, Y1 = y, X2 = w, Y2 = y, Stroke = lineBrush, StrokeThickness = 0.8 });
                    var (lat, _) = ScreenToLatLon(20, y, w, h);
                    var lbl = new TextBlock { Text = $"{lat:0.000}°N", Foreground = textBrush, FontSize = 9.5 };
                    Canvas.SetLeft(lbl, 8); Canvas.SetTop(lbl, y + 2); _mapCanvas.Children.Add(lbl);
                }
            }

            double cx = w / 2.0, cy = h / 2.0;
            _mapCanvas.Children.Add(new Line { X1 = cx - 14, Y1 = cy, X2 = cx + 14, Y2 = cy, Stroke = TacticalConfigPanel.BrushAccent, StrokeThickness = 1.5 });
            _mapCanvas.Children.Add(new Line { X1 = cx, Y1 = cy - 14, X2 = cx, Y2 = cy + 14, Stroke = TacticalConfigPanel.BrushAccent, StrokeThickness = 1.5 });
        }

        private static void DrawTacticalShapesAndPinsOverlay(double w, double h)
        {
            double metersPerPixel = Math.Max(0.05, (156543.03392 * Math.Cos(_centerLat * Math.PI / 180.0)) / Math.Pow(2.0, _zoom));

            foreach (var s in _shapes)
            {
                if (s.Points.Count == 0) continue;
                var strokeBrush = TacticalConfigPanel.BrushFromHex(s.ColorHex, Color.FromRgb(31, 224, 197));
                Color c = Color.FromRgb(31, 224, 197);
                try { c = (Color)ColorConverter.ConvertFromString(s.ColorHex); } catch { }
                var fillBrush = new SolidColorBrush(Color.FromArgb(65, c.R, c.G, c.B));
                fillBrush.Freeze();

                if (s.Kind == "Circle")
                {
                    Point centerPt = LatLonToScreen(s.Points[0].Lat, s.Points[0].Lon, w, h);
                    double radPx = Math.Max(8.0, s.RadiusMeters / metersPerPixel);
                    var el = new Ellipse { Width = radPx * 2, Height = radPx * 2, Fill = fillBrush, Stroke = strokeBrush, StrokeThickness = 2.2 };
                    Canvas.SetLeft(el, centerPt.X - radPx);
                    Canvas.SetTop(el, centerPt.Y - radPx);
                    _mapCanvas.Children.Add(el);

                    var lbl = new Border { Background = new SolidColorBrush(Color.FromArgb(210, 8, 19, 22)), BorderBrush = strokeBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(4, 1, 4, 1), Child = new TextBlock { Text = $"[{s.Faction}] {s.Name}", Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.Bold } };
                    Canvas.SetLeft(lbl, centerPt.X + 6); Canvas.SetTop(lbl, centerPt.Y - 10);
                    _mapCanvas.Children.Add(lbl);
                }
                else if (s.Kind == "Polygon" && s.Points.Count >= 2)
                {
                    var poly = new Polygon { Stroke = strokeBrush, Fill = fillBrush, StrokeThickness = 2.4 };
                    foreach (var pt in s.Points) poly.Points.Add(LatLonToScreen(pt.Lat, pt.Lon, w, h));
                    _mapCanvas.Children.Add(poly);

                    Point firstPt = poly.Points[0];
                    var lbl = new Border { Background = new SolidColorBrush(Color.FromArgb(210, 8, 19, 22)), BorderBrush = strokeBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(4, 1, 4, 1), Child = new TextBlock { Text = $"[{s.Faction}] {s.Name}", Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.Bold } };
                    Canvas.SetLeft(lbl, firstPt.X + 6); Canvas.SetTop(lbl, firstPt.Y - 10);
                    _mapCanvas.Children.Add(lbl);
                }
                else if (s.Points.Count >= 2)
                {
                    var pl = new Polyline { Stroke = strokeBrush, StrokeThickness = 2.8, StrokeDashArray = new DoubleCollection { 4, 2 } };
                    foreach (var pt in s.Points) pl.Points.Add(LatLonToScreen(pt.Lat, pt.Lon, w, h));
                    _mapCanvas.Children.Add(pl);
                }
            }

            foreach (var p in _pins)
            {
                Point pt = LatLonToScreen(p.Lat, p.Lon, w, h);
                if (pt.X < -50 || pt.X > w + 50 || pt.Y < -50 || pt.Y > h + 50) continue;

                var pinBrush = TacticalConfigPanel.BrushFromHex(p.ColorHex, Color.FromRgb(31, 224, 197));
                var dot = new Ellipse { Width = 14, Height = 14, Fill = pinBrush, Stroke = Brushes.White, StrokeThickness = 2 };
                Canvas.SetLeft(dot, pt.X - 7); Canvas.SetTop(dot, pt.Y - 7);
                _mapCanvas.Children.Add(dot);

                var tagBorder = new Border { Background = new SolidColorBrush(Color.FromArgb(220, 8, 19, 22)), BorderBrush = pinBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 2, 5, 2), Child = new TextBlock { Text = $"[{p.Faction}] {p.Name} ({p.Type})", Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.Bold } };
                Canvas.SetLeft(tagBorder, pt.X + 10); Canvas.SetTop(tagBorder, pt.Y - 10);
                _mapCanvas.Children.Add(tagBorder);
            }
        }

        private static void DrawCompassRoseAndLegendOverlay(double w, double h)
        {
            // Róża wiatrów / Strzałka Północy (Prawy Górny Róg)
            var compassBorder = new Border
            {
                Width = 54,
                Height = 64,
                Background = new SolidColorBrush(Color.FromArgb(220, 8, 19, 22)),
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(6),
                IsHitTestVisible = false
            };
            var cStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            cStack.Children.Add(new TextBlock { Text = "N", Foreground = TacticalConfigPanel.BrushAccent, FontWeight = FontWeights.Bold, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center });
            cStack.Children.Add(new TextBlock { Text = "▲", Foreground = Brushes.White, FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -4, 0, -2) });
            cStack.Children.Add(new TextBlock { Text = "GRID", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 8.5, HorizontalAlignment = HorizontalAlignment.Center });
            compassBorder.Child = cStack;
            Canvas.SetLeft(compassBorder, Math.Max(60, w - 70));
            Canvas.SetTop(compassBorder, 14);
            _mapCanvas.Children.Add(compassBorder);

            // Tabelka Sztabowa & Legenda Stron (Prawy Dolny Róg)
            var legendBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(225, 8, 19, 22)),
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 6, 10, 6),
                IsHitTestVisible = false
            };
            var lStack = new StackPanel();
            lStack.Children.Add(new TextBlock { Text = $"OPERACJA: {(_mapTitleBox?.Text ?? "MAPA AO").ToUpperInvariant()}", Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 10.5 });
            lStack.Children.Add(new TextBlock { Text = $"Siatka: {_gridMode} ({_gridStepMeters}m)", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 9.5, Margin = new Thickness(0, 1, 0, 4) });

            var factionsWrap = new WrapPanel();
            foreach (var f in TacticalConfigPanel.GetConfiguredFactions())
            {
                var chip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 2) };
                chip.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(2), Background = TacticalConfigPanel.BrushFromHex(f.ColorHex, Color.FromRgb(38, 132, 255)), Margin = new Thickness(0, 0, 4, 0) });
                chip.Children.Add(new TextBlock { Text = f.Name, Foreground = Brushes.White, FontSize = 9.5, FontWeight = FontWeights.SemiBold });
                factionsWrap.Children.Add(chip);
            }
            lStack.Children.Add(factionsWrap);
            legendBorder.Child = lStack;
            Canvas.SetLeft(legendBorder, Math.Max(60, w - 265));
            Canvas.SetTop(legendBorder, Math.Max(60, h - 78));
            _mapCanvas.Children.Add(legendBorder);
        }

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

            double boxW = barWidthPx + 36, boxH = 58;
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
            var hdr = new TextBlock { Text = $"SKALA ~ 1:{scaleRatio:N0}  ({metersPerPixel:0.0} m/px)", Foreground = TacticalConfigPanel.BrushAccent, FontSize = 10, FontWeight = FontWeights.Bold };
            Canvas.SetLeft(hdr, 12); Canvas.SetTop(hdr, 5);
            innerCanvas.Children.Add(hdr);

            int segments = 4;
            double segW = barWidthPx / segments, barY = 25;
            for (int s = 0; s < segments; s++)
            {
                var rect = new Rectangle { Width = segW + 0.5, Height = 8, Fill = (s % 2 == 0) ? TacticalConfigPanel.BrushAccent : Brushes.White, Stroke = Brushes.Black, StrokeThickness = 0.8 };
                Canvas.SetLeft(rect, 14 + s * segW); Canvas.SetTop(rect, barY);
                innerCanvas.Children.Add(rect);
            }

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
            Canvas.SetLeft(panelBorder, 16);
            Canvas.SetTop(panelBorder, Math.Max(60, h - boxH - 16));
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
                _mapCanvas.Children.Add(new Line { X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y, Stroke = yellowBrush, StrokeThickness = 2.4, StrokeDashArray = new DoubleCollection { 4, 2 } });

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
            double dLat = (lat2 - lat1) * Math.PI / 180.0, dLon = (lon2 - lon1) * Math.PI / 180.0;
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return R * 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
        }

        private static double ComputeBearingDegrees(double lat1, double lon1, double lat2, double lon2)
        {
            double phi1 = lat1 * Math.PI / 180.0, phi2 = lat2 * Math.PI / 180.0, dLon = (lon2 - lon1) * Math.PI / 180.0;
            double y = Math.Sin(dLon) * Math.Cos(phi2);
            double x = Math.Cos(phi1) * Math.Sin(phi2) - Math.Sin(phi1) * Math.Cos(phi2) * Math.Cos(dLon);
            return (Math.Atan2(y, x) * 180.0 / Math.PI + 360.0) % 360.0;
        }

        private static byte[] CaptureMapCanvasPngBytes()
        {
            int w = (int)Math.Max(800, _mapCanvas.ActualWidth), h = (int)Math.Max(600, _mapCanvas.ActualHeight);
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

                string kmzPath = System.IO.Path.Combine(mapsDir, $"{title}_map.kmz");
                string xmlPath = System.IO.Path.Combine(mapsDir, $"{_activeLayer.Id}_map.xml");
                string kmlPath = System.IO.Path.Combine(atakDir, $"{title}_strefy.kml");
                string gpxPath = System.IO.Path.Combine(atakDir, $"{title}_punkty.gpx");

                await Task.Run(() =>
                {
                    WriteKmzGroundOverlayArchive(kmzPath, title, pngBytes, north, south, east, west);
                    WriteAtakTileSourceXml(xmlPath, _activeLayer);
                    File.WriteAllText(kmlPath, BuildKmlDocumentString(title, false, 0, 0, 0, 0), Encoding.UTF8);
                    WriteGpxFile(gpxPath, title);
                });

                await TacticalConfigPanel.RefreshSourceSizeFromExternalAsync(win);
                if (_statusText != null) _statusText.Text = $"✓ Zapisano mapę KMZ i XML (w kategorii Mapy) oraz strefy KML i punkty GPX (w kategorii Dane taktyczne)!";
            }
            catch (Exception ex) { if (_statusText != null) _statusText.Text = $"Błąd: {ex.Message}"; }
        }

        private static async Task ExportStandaloneFilesAsync(Window win, string format)
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
                    if (format == "kmz") WriteKmzGroundOverlayArchive(System.IO.Path.Combine(outDir, $"{title}.kmz"), title, pngBytes, north, south, east, west);
                    else if (format == "kml") File.WriteAllText(System.IO.Path.Combine(outDir, $"{title}.kml"), BuildKmlDocumentString(title, false, 0, 0, 0, 0), Encoding.UTF8);
                    else if (format == "gpx") WriteGpxFile(System.IO.Path.Combine(outDir, $"{title}.gpx"), title);
                    else if (format == "png") File.WriteAllBytes(System.IO.Path.Combine(outDir, $"{title}.png"), pngBytes);
                });

                if (_statusText != null) _statusText.Text = $"✓ Wyeksportowano plik {format.ToUpperInvariant()} do: {outDir}";
                TacticalConfigPanel.TryOpenFolderInExplorer(outDir);
            }
            catch { }
        }

        private static string BuildKmlDocumentString(string title, bool includeGroundOverlay, double north, double south, double east, double west)
        {
            var sb = new StringBuilder();
            sb.AppendLine(@"<?xml version=""1.0"" encoding=""UTF-8""?><kml xmlns=""http://www.opengis.net/kml/2.2""><Document>");
            sb.AppendLine($"  <name>{title}</name>");
            if (includeGroundOverlay)
            {
                sb.AppendLine($"  <GroundOverlay><name>{title} ({_activeLayer?.Name})</name><Icon><href>files/map_overlay.png</href></Icon>");
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"    <LatLonBox><north>{north:0.000000}</north><south>{south:0.000000}</south><east>{east:0.000000}</east><west>{west:0.000000}</west></LatLonBox></GroundOverlay>"));
            }

            foreach (var s in _shapes)
            {
                if (s.Points.Count == 0) continue;
                sb.AppendLine($"  <Placemark><name>[{s.Faction}] {s.Name}</name>");
                if (s.Kind == "Polygon" && s.Points.Count >= 3)
                {
                    var coords = string.Join(" ", s.Points.Select(pt => string.Create(CultureInfo.InvariantCulture, $"{pt.Lon:0.000000},{pt.Lat:0.000000},0")));
                    var first = s.Points[0];
                    coords += string.Create(CultureInfo.InvariantCulture, $" {first.Lon:0.000000},{first.Lat:0.000000},0");
                    sb.AppendLine($"    <Polygon><outerBoundaryIs><LinearRing><coordinates>{coords}</coordinates></LinearRing></outerBoundaryIs></Polygon>");
                }
                else
                {
                    var coords = string.Join(" ", s.Points.Select(pt => string.Create(CultureInfo.InvariantCulture, $"{pt.Lon:0.000000},{pt.Lat:0.000000},0")));
                    sb.AppendLine($"    <LineString><coordinates>{coords}</coordinates></LineString>");
                }
                sb.AppendLine("  </Placemark>");
            }

            foreach (var p in _pins)
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  <Placemark><name>[{p.Faction}] {p.Name} ({p.Type})</name><Point><coordinates>{p.Lon:0.000000},{p.Lat:0.000000},0</coordinates></Point></Placemark>"));

            sb.AppendLine("</Document></kml>");
            return sb.ToString();
        }

        private static void WriteKmzGroundOverlayArchive(string kmzPath, string title, byte[] mapPngBytes, double north, double south, double east, double west)
        {
            if (File.Exists(kmzPath)) File.Delete(kmzPath);
            using var fs = new FileStream(kmzPath, FileMode.CreateNew, FileAccess.Write);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create, false, Encoding.UTF8);

            var imgEntry = zip.CreateEntry("files/map_overlay.png", CompressionLevel.Optimal);
            using (var s = imgEntry.Open()) s.Write(mapPngBytes, 0, mapPngBytes.Length);

            var kmlEntry = zip.CreateEntry("doc.kml", CompressionLevel.Optimal);
            using var sw = new StreamWriter(kmlEntry.Open(), Encoding.UTF8);
            sw.Write(BuildKmlDocumentString(title, true, north, south, east, west));
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
            foreach (var s in _shapes.Where(x => x.Points.Count >= 2))
            {
                sb.AppendLine($"  <rte><name>[{s.Faction}] {s.Name}</name>");
                foreach (var pt in s.Points)
                    sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"    <rtept lat=\"{pt.Lat:0.000000}\" lon=\"{pt.Lon:0.000000}\" />"));
                sb.AppendLine("  </rte>");
            }
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
                bool landscape = _printOrientation.Contains("Poziomo");
                string pdfPath = System.IO.Path.Combine(outDir, $"{title}_A4_{(landscape ? "LANDSCAPE" : "PORTRAIT")}.pdf");

                int pageW = landscape ? 1754 : 1240;
                int pageH = landscape ? 1240 : 1754;
                int mapW = (int)Math.Max(800, _mapCanvas.ActualWidth), mapH = (int)Math.Max(600, _mapCanvas.ActualHeight);
                var mapRtb = new RenderTargetBitmap(mapW, mapH, 96, 96, PixelFormats.Pbgra32);
                mapRtb.Render(_mapCanvas);

                var pageVisual = new DrawingVisual();
                using (DrawingContext dc = pageVisual.RenderOpen())
                {
                    dc.DrawRectangle(TacticalConfigPanel.BrushCardBg, null, new Rect(0, 0, pageW, pageH));
                    dc.DrawRoundedRectangle(null, new Pen(TacticalConfigPanel.BrushAccent, 4), new Rect(26, 26, pageW - 52, pageH - 52), 12, 12);
                    TacticalConfigPanel.DrawCenteredText(dc, $"MAPA TAKTYCZNA OPERACJI: {title.ToUpperInvariant()}", 28, Brushes.White, pageW / 2.0, 48, true);
                    TacticalConfigPanel.DrawCenteredText(dc, $"Warstwa: {_activeLayer?.Name}   |   Siatka: {_gridMode}   |   Środek WGS84: {_centerLat:0.00000}, {_centerLon:0.00000}   |   Zoom: {_zoom}", 16, TacticalConfigPanel.BrushAccent, pageW / 2.0, 88, false);

                    double imgBoxH = pageH - 210;
                    dc.DrawRectangle(Brushes.White, new Pen(TacticalConfigPanel.BrushAccent, 3), new Rect(58, 125, pageW - 116, imgBoxH));
                    dc.DrawImage(mapRtb, new Rect(60, 127, pageW - 120, imgBoxH - 4));

                    TacticalConfigPanel.DrawLeftText(dc, $"Obiektów taktycznych: {_pins.Count + _shapes.Count}   |   Podziałka skali i róża wiatrów na arkuszu   |   Field TAK Hub Builder 2.4 ({DateTime.Now:yyyy-MM-dd HH:mm})", 15, TacticalConfigPanel.BrushMutedText, 60, pageH - 65, false);
                }

                var fullRtb = new RenderTargetBitmap(pageW, pageH, 96, 96, PixelFormats.Pbgra32);
                fullRtb.Render(pageVisual);
                var jpgEnc = new JpegBitmapEncoder { QualityLevel = 92 };
                jpgEnc.Frames.Add(BitmapFrame.Create(fullRtb));
                using var jpgMs = new MemoryStream();
                jpgEnc.Save(jpgMs);
                byte[] jpegBytes = jpgMs.ToArray();

                await Task.Run(() => WriteSingleImagePdf(pdfPath, jpegBytes, pageW, pageH, landscape));
                if (_statusText != null) _statusText.Text = $"✓ Zapisano gotowy do druku plik PDF A4: {pdfPath}";
                TacticalConfigPanel.TryOpenFolderInExplorer(outDir);
            }
            catch (Exception ex) { if (_statusText != null) _statusText.Text = $"Błąd zapisu PDF: {ex.Message}"; }
        }

        private static void WriteSingleImagePdf(string pdfPath, byte[] jpegBytes, int pixelW, int pixelH, bool landscape)
        {
            using var fs = new FileStream(pdfPath, FileMode.Create, FileAccess.Write);
            void WriteAscii(string s) { byte[] b = Encoding.ASCII.GetBytes(s); fs.Write(b, 0, b.Length); }

            int pdfW = landscape ? 842 : 595;
            int pdfH = landscape ? 595 : 842;

            WriteAscii("%PDF-1.4\n");
            long obj1 = fs.Position; WriteAscii("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
            long obj2 = fs.Position; WriteAscii("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
            long obj3 = fs.Position; WriteAscii($"3 0 obj\n<< /Type /Page /Parent 2 0 R /Resources << /XObject << /Im0 4 0 R >> >> /MediaBox [0 0 {pdfW} {pdfH}] /Contents 5 0 R >>\nendobj\n");
            long obj4 = fs.Position; WriteAscii($"4 0 obj\n<< /Type /XObject /Subtype /Image /Width {pixelW} /Height {pixelH} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpegBytes.Length} >>\nstream\n");
            fs.Write(jpegBytes, 0, jpegBytes.Length);
            WriteAscii("\nendstream\nendobj\n");
            string contentCmd = $"q\n{pdfW} 0 0 {pdfH} 0 0 cm\n/Im0 Do\nQ\n";
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
