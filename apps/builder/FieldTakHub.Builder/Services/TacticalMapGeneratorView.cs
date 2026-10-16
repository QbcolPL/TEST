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
using System.Text.Json;
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

    public class TacticalMapObjectItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Kind { get; set; } = "Marker"; // "Marker", "Dot", "AO", "Route"
        public string Name { get; set; } = "Punkt";
        public string ColorHex { get; set; } = "#2684FF";
        public List<(double Lat, double Lon)> Points { get; set; } = new List<(double Lat, double Lon)>();
    }

    public static class TacticalMapGeneratorView
    {
        private static readonly HttpClient Http = CreateHttpClient();
        private static readonly ConcurrentDictionary<string, byte[]> TileCache = new ConcurrentDictionary<string, byte[]>();

        private static UIElement _originalWindowContent;
        private static Border _fullScreenOverlay;
        private static Button _tabBuilderBtn, _tabMapBtn;
        private static Canvas _mapCanvas;
        private static TextBlock _statusText, _coordsBadge, _gridThicknessValueLabel;
        private static StackPanel _objectsListStack;

        // Parametry 1:1 z generatorem HTML
        private static double _centerLat = 49.9135, _centerLon = 19.0238;
        private static int _zoom = 15;
        private static int _targetScaleDenominator = 5000;
        private static string _sheetFormat = "A4 (297 × 210 mm)";
        private static string _sheetOrientation = "Poziomo";
        private static bool _showLidarSingleWms;

        // Sekcja 2: System współrzędnych i siatka
        private static string _coordSystem = "MGRS / UTM (Siatka wojskowa w metrach)";
        private static string _subGridDivision = "Co 50 m (Ćwiartki A,B,C,D)";
        private static string _gridLabelsMode = "MGRS + Ćwiartki/Keypad";
        private static double _gridThicknessMultiplier = 1.0;
        private static string _gridColorName = "Żółty";
        private static string _gridHaloMode = "Pełny czarny obrys";

        // Sekcja 3: Narzędzia (Przesuwaj Mapę, Znacznik Taktyczny, Kropka Kolor, Rysuj Obszar AO, Rysuj Trasę/Linię)
        private static string _activeTool = "PAN"; // "PAN", "MARKER", "DOT", "AO", "ROUTE"
        private static Button _btnToolPan, _btnToolMarker, _btnToolDot, _btnToolAo, _btnToolRoute;
        private static readonly List<TacticalMapObjectItem> _mapObjects = new List<TacticalMapObjectItem>();
        private static TacticalMapObjectItem _activeDraftPoly;
        private static string _selectedObjectColorHex = "#2684FF";
        private static TextBox _objectNameInputBox, _scaleInputBox, _searchPlaceBox;

        // Sekcja 4: Pozycje elementów mapy & Eksport
        private static string _northArrowPos = "Lewy górny róg";
        private static string _scaleBarPos = "Lewy dolny róg";
        private static string _legendPos = "Prawy górny róg";
        private static string _kmzGrgMode = "Przezroczysta siatka";

        private static bool _isDragging;
        private static Point _dragStartPoint;
        private static double _dragStartLat, _dragStartLon;
        private static (double Lat, double Lon)? _rulerStart, _rulerEnd;
        private static TacticalMapLayerDef _activeLayer;

        public static readonly List<TacticalMapLayerDef> Layers = new List<TacticalMapLayerDef>
        {
            new TacticalMapLayerDef { Id = "google_hyb", Name = "Google Satelita + Opisy", IsWmsEpsg3857 = false, UrlTemplate = "https://mt1.google.com/vt/lyrs=y&x={x}&y={y}&z={z}" },
            new TacticalMapLayerDef { Id = "google_sat", Name = "Google Satelita (Czysta Ortofotomapa)", IsWmsEpsg3857 = false, UrlTemplate = "https://mt1.google.com/vt/lyrs=s&x={x}&y={y}&z={z}" },
            new TacticalMapLayerDef { Id = "google_ter", Name = "Google Teren (Warstwice)", IsWmsEpsg3857 = false, UrlTemplate = "https://mt1.google.com/vt/lyrs=p&x={x}&y={y}&z={z}" },
            new TacticalMapLayerDef { Id = "geoportal_hires", Name = "Geoportal Ortofotomapa HIGH-RES", IsWmsEpsg3857 = true, UrlTemplate = "https://mapy.geoportal.gov.pl/wss/service/PZGIK/ORTO/WMS/HighResolution?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=Raster&STYLES=&SRS=EPSG:3857&BBOX={bbox}&WIDTH=256&HEIGHT=256&FORMAT=image/jpeg" },
            new TacticalMapLayerDef { Id = "geoportal_std", Name = "Geoportal Ortofotomapa Standard", IsWmsEpsg3857 = true, UrlTemplate = "https://mapy.geoportal.gov.pl/wss/service/PZGIK/ORTO/WMS/StandardResolution?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=Raster&STYLES=&SRS=EPSG:3857&BBOX={bbox}&WIDTH=256&HEIGHT=256&FORMAT=image/jpeg" },
            new TacticalMapLayerDef { Id = "geoportal_lidar", Name = "Geoportal LiDAR ISOK (Cieniowanie NMT)", IsWmsEpsg3857 = true, UrlTemplate = "https://mapy.geoportal.gov.pl/wss/service/PZGIK/NMT/GRID1/WMS/ShadedRelief?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=Raster&STYLES=&SRS=EPSG:3857&BBOX={bbox}&WIDTH=256&HEIGHT=256&FORMAT=image/jpeg" },
            new TacticalMapLayerDef { Id = "geoportal_topo", Name = "Geoportal Mapa Topograficzna (BDOT10k / Topo)", IsWmsEpsg3857 = true, UrlTemplate = "https://mapy.geoportal.gov.pl/wss/ext/KrajowaIntegracjaMapTopograficznych?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=raster&STYLES=&SRS=EPSG:3857&BBOX={bbox}&WIDTH=256&HEIGHT=256&FORMAT=image/png" },
            new TacticalMapLayerDef { Id = "esri_sat", Name = "ESRI World Imagery (Ortofotomapa)", IsWmsEpsg3857 = false, UrlTemplate = "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}" },
            new TacticalMapLayerDef { Id = "esri_topo", Name = "ESRI World Topographic Map", IsWmsEpsg3857 = false, UrlTemplate = "https://server.arcgisonline.com/ArcGIS/rest/services/World_Topo_Map/MapServer/tile/{z}/{y}/{x}" },
            new TacticalMapLayerDef { Id = "osm_std", Name = "OpenStreetMap Standard", IsWmsEpsg3857 = false, UrlTemplate = "https://tile.openstreetmap.org/{z}/{x}/{y}.png" },
            new TacticalMapLayerDef { Id = "open_topomap", Name = "OpenTopoMap (Mapa Topograficzna)", IsWmsEpsg3857 = false, UrlTemplate = "https://a.tile.opentopomap.org/{z}/{x}/{y}.png" },
            new TacticalMapLayerDef { Id = "open_fotomap", Name = "OpenFotoMap / CyclOSM (Siatka Szlaków)", IsWmsEpsg3857 = false, UrlTemplate = "https://a.tile-cyclosm.openstreetmap.fr/cyclosm/{z}/{x}/{y}.png" }
        };

        private static HttpClient CreateHttpClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(14) };
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
                    var rightInfo = new TextBlock { Text = "Field TAK Hub 2.4  •  Google / Geoportal Hi-Res / LiDAR ISOK / Topo / ESRI / OSM", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
                    DockPanel.SetDock(rightInfo, Dock.Right);
                    navDock.Children.Add(rightInfo);

                    var tabsStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    _tabBuilderBtn = new Button { Content = "📦 Konfigurator Paczek (.ftak)", Height = 30, Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(0, 0, 8, 0), Background = TacticalConfigPanel.BrushButtonAccent, Foreground = TacticalConfigPanel.BrushDarkFg, BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1), FontWeight = FontWeights.Bold, FontSize = 11.5, Cursor = Cursors.Hand };
                    _tabMapBtn = new Button { Content = "🗺️ Generator Map Taktycznych 1:1 (Pełny Ekran)", Height = 30, Padding = new Thickness(16, 0, 16, 0), Background = TacticalConfigPanel.BrushItemBg, Foreground = TacticalConfigPanel.BrushAccent, BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1), FontWeight = FontWeights.Bold, FontSize = 11.5, Cursor = Cursors.Hand };

                    _tabBuilderBtn.Click += (_, __) => HideFullScreenMapTab();
                    _tabMapBtn.Click += (_, __) => ShowFullScreenMapTab(win);

                    tabsStack.Children.Add(_tabBuilderBtn);
                    tabsStack.Children.Add(_tabMapBtn);
                    navDock.Children.Add(tabsStack); navBorder.Child = navDock;
                    Grid.SetRow(navBorder, 0); masterGrid.Children.Add(navBorder);

                    var contentHostGrid = new Grid();
                    contentHostGrid.Children.Add(_originalWindowContent);
                    _fullScreenOverlay = BuildFullScreenMapGeneratorUI(win);
                    _fullScreenOverlay.Visibility = Visibility.Collapsed;
                    contentHostGrid.Children.Add(_fullScreenOverlay);

                    Grid.SetRow(contentHostGrid, 1); masterGrid.Children.Add(contentHostGrid);
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

        private static void ApplyScaleDenominator(int scaleDenom)
        {
            _targetScaleDenominator = Math.Clamp(scaleDenom, 500, 250000);
            if (_scaleInputBox != null) _scaleInputBox.Text = _targetScaleDenominator.ToString();
            // Przeliczenie skali 1:X na poziom przybliżenia Zoom (96 DPI -> 1 px = 0.00026458 m)
            double targetMetersPerPx = _targetScaleDenominator * 0.0002645833;
            double equatorMetersPerPx = 156543.03392 * Math.Cos(_centerLat * Math.PI / 180.0);
            int computedZoom = (int)Math.Round(Math.Log(equatorMetersPerPx / targetMetersPerPx, 2.0));
            _zoom = Math.Clamp(computedZoom, 5, 19);
            _ = RenderMapTilesAsync();
        }

        private static Border BuildFullScreenMapGeneratorUI(Window win)
        {
            _activeLayer = Layers[0];
            var GoldBrush = TacticalConfigPanel.CreateFrozenBrush(242, 182, 45);
            var BlueBtnBrush = TacticalConfigPanel.CreateFrozenBrush(24, 119, 209);
            var PurpleBtnBrush = TacticalConfigPanel.CreateFrozenBrush(118, 43, 196);
            var OrangeBtnBrush = TacticalConfigPanel.CreateFrozenBrush(230, 96, 12);

            var rootBorder = new Border { Background = TacticalConfigPanel.BrushCardBg, Tag = "INJECTED_MAP_OVERLAY" };
            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });

            var bodyGrid = new Grid();
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(415) });
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var leftScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = TacticalConfigPanel.BrushCardBg, Padding = new Thickness(10) };
            var leftStack = new StackPanel { Margin = new Thickness(6) };

            // =====================================================================
            // SEKCJA 1 (1:1 ze zrzutu): FORMAT ARKUSZA, SKALA MAPY 1:X, PODKŁAD BAZOWY & LiDAR ISOK
            // =====================================================================
            var sec1 = CreateSectionCard("1. ARKUSZ, SKALA MAPY (1:X), PODKŁAD & LiDAR ISOK", GoldBrush);

            var searchRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            _searchPlaceBox = (TextBox)TacticalConfigPanel.TextCtrl("Czechowice-Dziedzice", _ => { });
            var btnFind = new Button { Content = "🔍 Szukaj", Height = 28, Margin = new Thickness(6, 0, 0, 0), Background = BlueBtnBrush, Foreground = Brushes.White, BorderThickness = new Thickness(0), FontWeight = FontWeights.Bold, Cursor = Cursors.Hand };
            btnFind.Click += async (_, __) => await SearchLocationNominatimAsync();
            Grid.SetColumn(_searchPlaceBox, 0); Grid.SetColumn(btnFind, 1);
            searchRow.Children.Add(_searchPlaceBox); searchRow.Children.Add(btnFind);
            sec1.Children.Add(TacticalConfigPanel.FieldBox("Wyszukaj miejscowość / teren gry:", searchRow));

            var fmtGrid = new UniformGrid { Columns = 2 };
            fmtGrid.Children.Add(TacticalConfigPanel.FieldBox("Format arkusza:", TacticalConfigPanel.ComboCtrl(new[] { "A4 (297 × 210 mm)", "A3 (420 × 297 mm)" }, _sheetFormat, win, v => _sheetFormat = v)));
            fmtGrid.Children.Add(TacticalConfigPanel.FieldBox("Orientacja:", TacticalConfigPanel.ComboCtrl(new[] { "Poziomo", "Pionowo" }, _sheetOrientation, win, v => _sheetOrientation = v)));
            sec1.Children.Add(fmtGrid);

            sec1.Children.Add(new TextBlock { Text = "Skala mapy (wpisz dowolną 1:X lub wybierz poniżej):", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 10.5, Margin = new Thickness(0, 4, 0, 4) });
            var scaleCustomGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            scaleCustomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            scaleCustomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            scaleCustomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
            scaleCustomGrid.Children.Add(new TextBlock { Text = "1 :", Foreground = GoldBrush, FontWeight = FontWeights.Bold, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
            _scaleInputBox = (TextBox)TacticalConfigPanel.TextCtrl("5000", _ => { });
            Grid.SetColumn(_scaleInputBox, 1); scaleCustomGrid.Children.Add(_scaleInputBox);
            var btnSetScale = new Button { Content = "Ustaw skalę", Height = 30, Margin = new Thickness(6, 0, 0, 0), Background = BlueBtnBrush, Foreground = Brushes.White, FontWeight = FontWeights.Bold, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
            btnSetScale.Click += (_, __) => { if (int.TryParse(_scaleInputBox.Text.Trim(), out int sc)) ApplyScaleDenominator(sc); };
            Grid.SetColumn(btnSetScale, 2); scaleCustomGrid.Children.Add(btnSetScale);
            sec1.Children.Add(scaleCustomGrid);

            var scalePresetsRow = new UniformGrid { Columns = 6, Margin = new Thickness(0, 0, 0, 4) };
            int[] presets = { 2500, 4000, 5000, 7500, 10000, 15000 };
            foreach (int p in presets)
            {
                int cap = p;
                var b = new Button { Content = $"1:{cap}", Height = 24, Margin = new Thickness(1), Background = TacticalConfigPanel.BrushInputBg, Foreground = Brushes.White, BorderBrush = TacticalConfigPanel.BrushInputBorder, BorderThickness = new Thickness(1), FontSize = 9.5, FontWeight = FontWeights.Bold, Cursor = Cursors.Hand };
                b.Click += (_, __) => ApplyScaleDenominator(cap);
                scalePresetsRow.Children.Add(b);
            }
            sec1.Children.Add(scalePresetsRow);

            var btnScale25k = new Button { Content = "1:25000", Height = 24, Margin = new Thickness(1, 0, 1, 8), Background = TacticalConfigPanel.BrushInputBg, Foreground = Brushes.White, BorderBrush = TacticalConfigPanel.BrushInputBorder, BorderThickness = new Thickness(1), FontSize = 10, FontWeight = FontWeights.Bold, Cursor = Cursors.Hand };
            btnScale25k.Click += (_, __) => ApplyScaleDenominator(25000);
            sec1.Children.Add(btnScale25k);

            var layerCombo = new ComboBox { ItemsSource = Layers, SelectedIndex = 0, Height = 30, FontSize = 11, Margin = new Thickness(0, 0, 0, 8), Tag = "INJECTED" };
            TacticalConfigPanel.ApplyUnifiedComboBoxTheme(layerCombo, win);
            layerCombo.SelectionChanged += (_, __) => { if (layerCombo.SelectedItem is TacticalMapLayerDef def) { _activeLayer = def; _ = RenderMapTilesAsync(); } };
            sec1.Children.Add(TacticalConfigPanel.FieldBox("Podkład bazowy:", layerCombo));

            var lidarBox = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(16, 38, 24)),
                BorderBrush = TacticalConfigPanel.CreateFrozenBrush(46, 160, 80),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8, 10, 8)
            };
            var chkLidar = new CheckBox
            {
                Content = "Nałóż rzeźbę LiDAR ISOK (1 zapytanie WMS - bez dziur)",
                IsChecked = false,
                Foreground = TacticalConfigPanel.CreateFrozenBrush(110, 235, 140),
                FontWeight = FontWeights.Bold,
                FontSize = 10.5
            };
            chkLidar.Checked += (_, __) => { _showLidarSingleWms = true; _ = RenderMapTilesAsync(); };
            chkLidar.Unchecked += (_, __) => { _showLidarSingleWms = false; _ = RenderMapTilesAsync(); };
            lidarBox.Child = chkLidar;
            sec1.Children.Add(lidarBox);
            leftStack.Children.Add(WrapCard(sec1));

            // =====================================================================
            // SEKCJA 2 (1:1 ze zrzutu): 2. SYSTEM WSPÓŁRZĘDNYCH I SIATKA
            // =====================================================================
            var sec2 = CreateSectionCard("2. SYSTEM WSPÓŁRZĘDNYCH I SIATKA", GoldBrush);
            sec2.Children.Add(TacticalConfigPanel.FieldBox("System współrzędnych i typ siatki na mapie:", TacticalConfigPanel.ComboCtrl(new[]
            {
                "MGRS / UTM (Siatka wojskowa w metrach)",
                "Siatka Sektorowa (A1, B2, C3...)",
                "WGS84 (Stopnie geograficzne Lat/Lon)",
                "Brak siatki"
            }, _coordSystem, win, v => { _coordSystem = v; _ = RenderMapTilesAsync(); })));

            var subGridRow = new UniformGrid { Columns = 2 };
            subGridRow.Children.Add(TacticalConfigPanel.FieldBox("Podział wewnątrz 100 m:", TacticalConfigPanel.ComboCtrl(new[]
            {
                "Co 50 m (Ćwiartki A,B,C,D)",
                "Co 33 m (Keypad 1-9)",
                "Brak podziału (Tylko 100 m)",
                "Siatka co 250 m",
                "Siatka co 500 m"
            }, _subGridDivision, win, v => { _subGridDivision = v; _ = RenderMapTilesAsync(); })));

            subGridRow.Children.Add(TacticalConfigPanel.FieldBox("Opisy kratek 100 m:", TacticalConfigPanel.ComboCtrl(new[]
            {
                "MGRS + Ćwiartki/Keypad",
                "Tylko główne linie MGRS",
                "Brak opisów wewnątrz"
            }, _gridLabelsMode, win, v => { _gridLabelsMode = v; _ = RenderMapTilesAsync(); })));
            sec2.Children.Add(subGridRow);

            var thickHeaderDock = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            _gridThicknessValueLabel = new TextBlock { Text = "1.0x", Foreground = GoldBrush, FontWeight = FontWeights.Bold, FontSize = 11 };
            DockPanel.SetDock(_gridThicknessValueLabel, Dock.Right);
            thickHeaderDock.Children.Add(_gridThicknessValueLabel);
            thickHeaderDock.Children.Add(new TextBlock { Text = "Grubość linii siatki:", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 10.5 });
            sec2.Children.Add(thickHeaderDock);

            var thickSlider = new Slider { Minimum = 0.5, Maximum = 3.0, Value = 1.0, Margin = new Thickness(0, 0, 0, 6) };
            thickSlider.ValueChanged += (_, e) =>
            {
                _gridThicknessMultiplier = Math.Round(e.NewValue, 1);
                if (_gridThicknessValueLabel != null) _gridThicknessValueLabel.Text = $"{_gridThicknessMultiplier:0.0}x";
                _ = RenderMapTilesAsync();
            };
            sec2.Children.Add(thickSlider);

            var colAndHaloRow = new UniformGrid { Columns = 2 };
            colAndHaloRow.Children.Add(TacticalConfigPanel.FieldBox("Kolor siatki:", TacticalConfigPanel.ComboCtrl(new[] { "Żółty", "Turkusowy", "Biały", "Czerwony", "Czarny" }, _gridColorName, win, v => { _gridColorName = v; _ = RenderMapTilesAsync(); })));
            colAndHaloRow.Children.Add(TacticalConfigPanel.FieldBox("Obrys kontrastowy (Halo):", TacticalConfigPanel.ComboCtrl(new[] { "Pełny czarny obrys", "Półprzezroczysty obrys", "Brak obrysu" }, _gridHaloMode, win, v => { _gridHaloMode = v; _ = RenderMapTilesAsync(); })));
            sec2.Children.Add(colAndHaloRow);
            leftStack.Children.Add(WrapCard(sec2));

            // =====================================================================
            // SEKCJA 3 (1:1 ze zrzutu): 3. ZNACZNIKI, KROPKI KOLOROWE I TEREN GRY
            // =====================================================================
            var sec3 = CreateSectionCard("3. ZNACZNIKI, KROPKI KOLOROWE I TEREN GRY", GoldBrush);

            void UpdateToolButtonsVisual()
            {
                void StyleTool(Button b, bool active)
                {
                    if (b == null) return;
                    b.Background = active ? GoldBrush : TacticalConfigPanel.BrushInputBg;
                    b.Foreground = active ? TacticalConfigPanel.BrushDarkFg : Brushes.White;
                    b.BorderBrush = active ? Brushes.White : TacticalConfigPanel.BrushInputBorder;
                }
                StyleTool(_btnToolPan, _activeTool == "PAN");
                StyleTool(_btnToolMarker, _activeTool == "MARKER");
                StyleTool(_btnToolDot, _activeTool == "DOT");
                StyleTool(_btnToolAo, _activeTool == "AO");
                StyleTool(_btnToolRoute, _activeTool == "ROUTE");
            }

            _btnToolPan = new Button { Content = "✋ Przesuwaj Mapę", Height = 32, Margin = new Thickness(0, 0, 0, 6), FontWeight = FontWeights.Bold, FontSize = 11.5, Cursor = Cursors.Hand };
            _btnToolPan.Click += (_, __) => { _activeTool = "PAN"; _activeDraftPoly = null; UpdateToolButtonsVisual(); };
            sec3.Children.Add(_btnToolPan);

            var toolsGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 6) };
            _btnToolMarker = new Button { Content = "📍 Znacznik Taktyczny", Height = 30, Margin = new Thickness(0, 0, 3, 4), FontWeight = FontWeights.Bold, FontSize = 10.5, Cursor = Cursors.Hand };
            _btnToolDot = new Button { Content = "⚫ Kropka (Kolor)", Height = 30, Margin = new Thickness(3, 0, 0, 4), FontWeight = FontWeights.Bold, FontSize = 10.5, Cursor = Cursors.Hand };
            _btnToolAo = new Button { Content = "⬠ Rysuj Obszar (AO)", Height = 30, Margin = new Thickness(0, 0, 3, 0), FontWeight = FontWeights.Bold, FontSize = 10.5, Cursor = Cursors.Hand };
            _btnToolRoute = new Button { Content = "〰 Rysuj Trasę/Linię", Height = 30, Margin = new Thickness(3, 0, 0, 0), FontWeight = FontWeights.Bold, FontSize = 10.5, Cursor = Cursors.Hand };

            _btnToolMarker.Click += (_, __) => { _activeTool = "MARKER"; _activeDraftPoly = null; UpdateToolButtonsVisual(); };
            _btnToolDot.Click += (_, __) => { _activeTool = "DOT"; _activeDraftPoly = null; UpdateToolButtonsVisual(); };
            _btnToolAo.Click += (_, __) => { _activeTool = "AO"; _activeDraftPoly = null; UpdateToolButtonsVisual(); };
            _btnToolRoute.Click += (_, __) => { _activeTool = "ROUTE"; _activeDraftPoly = null; UpdateToolButtonsVisual(); };

            toolsGrid.Children.Add(_btnToolMarker); toolsGrid.Children.Add(_btnToolDot);
            toolsGrid.Children.Add(_btnToolAo); toolsGrid.Children.Add(_btnToolRoute);
            sec3.Children.Add(toolsGrid);
            UpdateToolButtonsVisual();

            var objCfgRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            objCfgRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            objCfgRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            _objectNameInputBox = (TextBox)TacticalConfigPanel.TextCtrl("HQ / Respawn", _ => { });
            var colPickCombo = (ComboBox)TacticalConfigPanel.ComboCtrl(new[] { "#2684FF", "#EB4034", "#F5B82E", "#2ECC71", "#9B59B6", "#FFFFFF" }, _selectedObjectColorHex, win, v => _selectedObjectColorHex = v);
            Grid.SetColumn(_objectNameInputBox, 0); Grid.SetColumn(colPickCombo, 1);
            objCfgRow.Children.Add(TacticalConfigPanel.FieldBox("Nazwa obiektu:", _objectNameInputBox));
            objCfgRow.Children.Add(TacticalConfigPanel.FieldBox("Kolor obiektu:", colPickCombo));
            sec3.Children.Add(objCfgRow);

            sec3.Children.Add(new TextBlock { Text = "Naniesione obiekty (kliknij ✖ aby usunąć):", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 10.5, Margin = new Thickness(0, 0, 0, 4) });
            var objListBorder = new Border { Background = TacticalConfigPanel.BrushInputBg, BorderBrush = TacticalConfigPanel.BrushInputBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), MinHeight = 46, MaxHeight = 110, Padding = new Thickness(6) };
            var objListScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _objectsListStack = new StackPanel();
            objListScroll.Content = _objectsListStack;
            objListBorder.Child = objListScroll;
            sec3.Children.Add(objListBorder);
            RefreshObjectsListUI();
            leftStack.Children.Add(WrapCard(sec3));

            // =====================================================================
            // SEKCJA 4 (1:1 ze zrzutu): 4. ELEMENTY MAPY (PÓŁNOC / PODZIAŁKA / LEGENDA) & EKSPORT
            // =====================================================================
            var sec4 = CreateSectionCard("4. ELEMENTY MAPY (PÓŁNOC / PODZIAŁKA / LEGENDA) & EKSPORT", GoldBrush);
            string[] corners = { "Lewy górny róg", "Prawy górny róg", "Lewy dolny róg", "Prawy dolny róg", "Ukryj" };
            var posGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 8) };
            posGrid.Children.Add(TacticalConfigPanel.FieldBox("Znacznik Północy (N):", TacticalConfigPanel.ComboCtrl(corners, _northArrowPos, win, v => { _northArrowPos = v; _ = RenderMapTilesAsync(); })));
            posGrid.Children.Add(TacticalConfigPanel.FieldBox("Podziałka ze skalą na mapie:", TacticalConfigPanel.ComboCtrl(corners, _scaleBarPos, win, v => { _scaleBarPos = v; _ = RenderMapTilesAsync(); })));
            posGrid.Children.Add(TacticalConfigPanel.FieldBox("Legenda i Tabela na mapie:", TacticalConfigPanel.ComboCtrl(corners, _legendPos, win, v => { _legendPos = v; _ = RenderMapTilesAsync(); })));
            posGrid.Children.Add(TacticalConfigPanel.FieldBox("Tryb GRG w pliku .KMZ:", TacticalConfigPanel.ComboCtrl(new[] { "Przezroczysta siatka", "Pełny podkład + siatka", "Tylko podkład" }, _kmzGrgMode, win, v => _kmzGrgMode = v)));
            sec4.Children.Add(posGrid);

            var expRow1 = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 6) };
            var btnGpx = new Button { Content = "⌚ Pobierz .GPX\n(Garmin/Suunto)", Height = 40, Margin = new Thickness(0, 0, 4, 0), Background = PurpleBtnBrush, Foreground = Brushes.White, BorderThickness = new Thickness(0), FontWeight = FontWeights.Bold, FontSize = 11, Cursor = Cursors.Hand, HorizontalContentAlignment = HorizontalAlignment.Center };
            var btnKmz = new Button { Content = "📦 Pobierz .KMZ (ATAK)", Height = 40, Margin = new Thickness(4, 0, 0, 0), Background = OrangeBtnBrush, Foreground = Brushes.White, BorderThickness = new Thickness(0), FontWeight = FontWeights.Bold, FontSize = 11, Cursor = Cursors.Hand };
            btnGpx.Click += async (_, __) => await ExportStandaloneFilesAsync(win, "gpx");
            btnKmz.Click += async (_, __) => await ExportStandaloneFilesAsync(win, "kmz");
            expRow1.Children.Add(btnGpx); expRow1.Children.Add(btnKmz);
            sec4.Children.Add(expRow1);

            var expRow2 = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 6) };
            var btnPdf = new Button { Content = "🖨️ Drukuj / PDF", Height = 36, Margin = new Thickness(0, 0, 4, 0), Background = BlueBtnBrush, Foreground = Brushes.White, BorderThickness = new Thickness(0), FontWeight = FontWeights.Bold, FontSize = 11, Cursor = Cursors.Hand };
            var btnSaveJson = new Button { Content = "💾 Zapisz .json", Height = 36, Margin = new Thickness(2, 0, 2, 0), Background = TacticalConfigPanel.BrushInputBg, Foreground = Brushes.White, BorderBrush = TacticalConfigPanel.BrushInputBorder, BorderThickness = new Thickness(1), FontWeight = FontWeights.Bold, FontSize = 11, Cursor = Cursors.Hand };
            var btnLoadJson = new Button { Content = "📂 Wczytaj", Height = 36, Margin = new Thickness(4, 0, 0, 0), Background = TacticalConfigPanel.BrushInputBg, Foreground = Brushes.White, BorderBrush = TacticalConfigPanel.BrushInputBorder, BorderThickness = new Thickness(1), FontWeight = FontWeights.Bold, FontSize = 11, Cursor = Cursors.Hand };
            btnPdf.Click += async (_, __) => await ExportTacticalMapPdfAsync(win);
            btnSaveJson.Click += (_, __) => SaveMapProjectJson(win);
            btnLoadJson.Click += (_, __) => LoadMapProjectJsonOrKml();
            expRow2.Children.Add(btnPdf); expRow2.Children.Add(btnSaveJson); expRow2.Children.Add(btnLoadJson);
            sec4.Children.Add(expRow2);

            var btnAddToFtak = new Button { Content = "📦 Zapisz bezpośrednio do paczki .ftak (source/maps & atak)", Height = 32, Background = TacticalConfigPanel.BrushButtonAccent, Foreground = TacticalConfigPanel.BrushDarkFg, BorderThickness = new Thickness(0), FontWeight = FontWeights.Bold, FontSize = 11, Cursor = Cursors.Hand };
            btnAddToFtak.Click += async (_, __) => await ExportMapToFtakSourceAsync(win);
            sec4.Children.Add(btnAddToFtak);

            leftStack.Children.Add(WrapCard(sec4));
            leftScroll.Content = leftStack;
            Grid.SetColumn(leftScroll, 0);
            bodyGrid.Children.Add(leftScroll);

            // PŁÓTNO MAPY TAKTYCZNEJ
            var mapBorder = new Border { Background = new SolidColorBrush(Color.FromRgb(5, 12, 15)), BorderBrush = TacticalConfigPanel.BrushCardBorder, BorderThickness = new Thickness(1, 0, 0, 0), ClipToBounds = true };
            var mapOverlayGrid = new Grid();
            _mapCanvas = new Canvas { Background = new SolidColorBrush(Color.FromRgb(6, 15, 18)), ClipToBounds = true, Cursor = Cursors.Cross };

            _mapCanvas.MouseLeftButtonDown += (_, e) =>
            {
                _isDragging = true; _dragStartPoint = e.GetPosition(_mapCanvas);
                _dragStartLat = _centerLat; _dragStartLon = _centerLon;
                _mapCanvas.CaptureMouse();
            };

            _mapCanvas.MouseRightButtonDown += (_, e) =>
            {
                if (_activeDraftPoly != null) { _activeDraftPoly = null; RefreshObjectsListUI(); _ = RenderMapTilesAsync(); return; }
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
                if (_coordsBadge != null) _coordsBadge.Text = $"WGS84: {hoverLat:0.00000}, {hoverLon:0.00000}  |  Skala ~ 1:{_targetScaleDenominator} (Zoom {_zoom})  |  PPM = Dalmierz / Zamknij poligon";
                if (_isDragging && _activeTool == "PAN")
                {
                    double dx = p.X - _dragStartPoint.X, dy = p.Y - _dragStartPoint.Y;
                    double worldSize = 256.0 * Math.Pow(2, _zoom);
                    _centerLon = Math.Clamp(_dragStartLon - dx * (360.0 / worldSize), -180.0, 180.0);
                    _centerLat = Math.Clamp(_dragStartLat + dy * ((360.0 / worldSize) * Math.Cos(_dragStartLat * Math.PI / 180.0)), -85.0, 85.0);
                }
            };

            _mapCanvas.MouseLeftButtonUp += (_, e) =>
            {
                if (!_isDragging) return;
                _isDragging = false; _mapCanvas.ReleaseMouseCapture();
                Point endPt = e.GetPosition(_mapCanvas);
                if ((endPt - _dragStartPoint).Length < 5.0 && _activeTool != "PAN")
                    HandleMapClickToolAction(endPt);
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

            var hudBorder = new Border { Background = new SolidColorBrush(Color.FromArgb(215, 8, 19, 22)), BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(12), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
            _coordsBadge = new TextBlock { Text = $"WGS84: {_centerLat:0.00000}, {_centerLon:0.00000}  |  Zoom: {_zoom}", Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold };
            hudBorder.Child = _coordsBadge; mapOverlayGrid.Children.Add(hudBorder);

            mapBorder.Child = mapOverlayGrid; Grid.SetColumn(mapBorder, 1); bodyGrid.Children.Add(mapBorder);
            Grid.SetRow(bodyGrid, 0); mainGrid.Children.Add(bodyGrid);

            var bottomBar = new Border { Background = TacticalConfigPanel.BrushInputBg, BorderBrush = TacticalConfigPanel.BrushCardBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(16, 0, 16, 0) };
            _statusText = new TextBlock { Text = "Gotowy. Układ 1:1 z generatorem HTML: Skala 1:X, LiDAR ISOK (1 zapytanie WMS), Siatka MGRS co 50 m (Ćwiartki A,B,C,D), Halo, GRG .KMZ i Druk PDF.", Foreground = TacticalConfigPanel.BrushAccent, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            bottomBar.Child = _statusText; Grid.SetRow(bottomBar, 1); mainGrid.Children.Add(bottomBar);

            rootBorder.Child = mainGrid;
            return rootBorder;
        }

        private static void HandleMapClickToolAction(Point clickPt)
        {
            var (clickLat, clickLon) = ScreenToLatLon(clickPt.X, clickPt.Y, _mapCanvas.ActualWidth, _mapCanvas.ActualHeight);
            string objName = string.IsNullOrWhiteSpace(_objectNameInputBox?.Text) ? $"Obiekt-{_mapObjects.Count + 1}" : _objectNameInputBox.Text.Trim();

            if (_activeTool == "MARKER" || _activeTool == "DOT")
            {
                var item = new TacticalMapObjectItem
                {
                    Kind = _activeTool == "MARKER" ? "Marker" : "Dot",
                    Name = objName,
                    ColorHex = _selectedObjectColorHex
                };
                item.Points.Add((clickLat, clickLon));
                _mapObjects.Add(item);
                _objectNameInputBox.Text = $"Punkt-{_mapObjects.Count + 1}";
            }
            else if (_activeTool == "AO" || _activeTool == "ROUTE")
            {
                string kind = _activeTool == "AO" ? "AO" : "Route";
                if (_activeDraftPoly == null || _activeDraftPoly.Kind != kind)
                {
                    _activeDraftPoly = new TacticalMapObjectItem { Kind = kind, Name = objName, ColorHex = _selectedObjectColorHex };
                    _mapObjects.Add(_activeDraftPoly);
                    _objectNameInputBox.Text = $"{kind}-{_mapObjects.Count + 1}";
                }
                _activeDraftPoly.Points.Add((clickLat, clickLon));
            }
            RefreshObjectsListUI();
        }

        private static void RefreshObjectsListUI()
        {
            if (_objectsListStack == null) return;
            _objectsListStack.Children.Clear();
            if (_mapObjects.Count == 0)
            {
                _objectsListStack.Children.Add(new TextBlock { Text = "Brak naniesionych obiektów", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 8) });
                return;
            }

            foreach (var obj in _mapObjects.ToList())
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 3) };
                var btnDel = new Button
                {
                    Content = "✖",
                    Width = 22,
                    Height = 20,
                    Background = TacticalConfigPanel.BrushRedMinus,
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Cursor = Cursors.Hand
                };
                DockPanel.SetDock(btnDel, Dock.Right);
                var captured = obj;
                btnDel.Click += (_, __) => { _mapObjects.Remove(captured); if (_activeDraftPoly == captured) _activeDraftPoly = null; RefreshObjectsListUI(); _ = RenderMapTilesAsync(); };
                row.Children.Add(btnDel);

                row.Children.Add(new TextBlock
                {
                    Text = $"[{obj.Kind}] {obj.Name} ({obj.Points.Count} pkt)",
                    Foreground = Brushes.White,
                    FontSize = 10.5,
                    VerticalAlignment = VerticalAlignment.Center
                });
                _objectsListStack.Children.Add(row);
            }
        }

        private static StackPanel CreateSectionCard(string title, Brush headerBrush)
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = title, Foreground = headerBrush, FontWeight = FontWeights.Bold, FontSize = 11.5, Margin = new Thickness(0, 0, 0, 8) });
            return sp;
        }

        private static Border WrapCard(UIElement child) => new Border { Background = TacticalConfigPanel.BrushItemBg, BorderBrush = TacticalConfigPanel.BrushTileBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 10), Child = child };

        private static async Task SearchLocationNominatimAsync()
        {
            string q = _searchPlaceBox?.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(q)) return;
            try
            {
                string url = $"https://nominatim.openstreetmap.org/search?format=json&limit=1&q={Uri.EscapeDataString(q)}";
                var arr = JsonNode.Parse(await Http.GetStringAsync(url)) as JsonArray;
                if (arr != null && arr.Count > 0 && arr[0] is JsonObject first &&
                    double.TryParse(first["lat"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double la) &&
                    double.TryParse(first["lon"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double lo))
                {
                    _centerLat = la; _centerLon = lo;
                    if (_statusText != null) _statusText.Text = $"✓ Znaleziono: {first["display_name"]}";
                    await RenderMapTilesAsync();
                }
            }
            catch { }
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
            return (Math.Atan(Math.Sinh(Math.PI * (1.0 - 2.0 * y / n))) * 180.0 / Math.PI, x / n * 360.0 - 180.0);
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

        private static (double X, double Y) LatLonToEpsg3857(double lat, double lon)
        {
            double x = lon * 20037508.34 / 180.0;
            double y = Math.Log(Math.Tan((90.0 + lat) * Math.PI / 360.0)) / (Math.PI / 180.0) * 20037508.34 / 180.0;
            return (x, y);
        }

        private static string GetEpsg3857BBox(int tx, int ty, int zoom)
        {
            var (latNorth, lonWest) = TileXYToLatLon(tx, ty, zoom);
            var (latSouth, lonEast) = TileXYToLatLon(tx + 1, ty + 1, zoom);
            var (minX, minY) = LatLonToEpsg3857(latSouth, lonWest);
            var (maxX, maxY) = LatLonToEpsg3857(latNorth, lonEast);
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
            try { byte[] data = await Http.GetByteArrayAsync(url); if (data != null && data.Length > 100) { TileCache[url] = data; return data; } } catch { }
            return null;
        }

        private static async Task RenderMapTilesAsync(bool transparentGrgOnly = false)
        {
            if (_mapCanvas == null || _activeLayer == null || _fullScreenOverlay?.Visibility != Visibility.Visible) return;
            double w = _mapCanvas.ActualWidth > 100 ? _mapCanvas.ActualWidth : 900;
            double h = _mapCanvas.ActualHeight > 100 ? _mapCanvas.ActualHeight : 650;

            var (centerTileX, centerTileY) = LatLonToTileXY(_centerLat, _centerLon, _zoom);
            int minTx = (int)Math.Floor(centerTileX - (w / 2.0) / 256.0) - 1, maxTx = (int)Math.Floor(centerTileX + (w / 2.0) / 256.0) + 1;
            int minTy = (int)Math.Floor(centerTileY - (h / 2.0) / 256.0) - 1, maxTy = (int)Math.Floor(centerTileY + (h / 2.0) / 256.0) + 1;
            int maxIndex = (1 << _zoom) - 1;

            byte[] singleLidarBytes = null;
            List<(double ScreenX, double ScreenY, byte[] Bytes)> fetchedTiles = null;

            if (!transparentGrgOnly)
            {
                var tileJobs = new List<(int Tx, int Ty, double ScreenX, double ScreenY, string Url)>();
                for (int tx = minTx; tx <= maxTx; tx++)
                    for (int ty = minTy; ty <= maxTy; ty++)
                    {
                        if (ty < 0 || ty > maxIndex) continue;
                        int wrappedTx = ((tx % (maxIndex + 1)) + (maxIndex + 1)) % (maxIndex + 1);
                        tileJobs.Add((wrappedTx, ty, (w / 2.0) + (tx - centerTileX) * 256.0, (h / 2.0) + (ty - centerTileY) * 256.0, BuildTileUrl(_activeLayer, wrappedTx, ty, _zoom)));
                    }

                var tilesTask = Task.WhenAll(tileJobs.Select(async job => (job.ScreenX, job.ScreenY, await FetchTileBytesAsync(job.Url))));

                // 1 zapytanie WMS dla rzeźby LiDAR ISOK (bez dziur kafelkowych!)
                Task<byte[]> lidarTask = Task.FromResult<byte[]>(null);
                if (_showLidarSingleWms)
                {
                    var (nwLat, nwLon) = ScreenToLatLon(0, 0, w, h);
                    var (seLat, seLon) = ScreenToLatLon(w, h, w, h);
                    var (minX, minY) = LatLonToEpsg3857(seLat, nwLon);
                    var (maxX, maxY) = LatLonToEpsg3857(nwLat, seLon);
                    int reqW = Math.Clamp((int)w, 256, 1600), reqH = Math.Clamp((int)h, 256, 1600);
                    string singleWmsUrl = string.Create(CultureInfo.InvariantCulture, $"https://mapy.geoportal.gov.pl/wss/service/PZGIK/NMT/GRID1/WMS/ShadedRelief?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=Raster&STYLES=&SRS=EPSG:3857&BBOX={minX:0.00},{minY:0.00},{maxX:0.00},{maxY:0.00}&WIDTH={reqW}&HEIGHT={reqH}&FORMAT=image/jpeg");
                    lidarTask = FetchTileBytesAsync(singleWmsUrl);
                }

                await Task.WhenAll(tilesTask, lidarTask);
                fetchedTiles = tilesTask.Result.ToList();
                singleLidarBytes = lidarTask.Result;
            }

            _mapCanvas.Children.Clear();
            _mapCanvas.Background = transparentGrgOnly ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(6, 15, 18));

            if (!transparentGrgOnly && fetchedTiles != null)
            {
                foreach (var t in fetchedTiles)
                    if (t.Item3 != null) AddCanvasImage(t.Item3, t.ScreenX, t.ScreenY, 256.5, 256.5, 1.0);
                if (singleLidarBytes != null)
                    AddCanvasImage(singleLidarBytes, 0, 0, w, h, 0.45);
            }

            if (_kmzGrgMode != "Tylko podkład" || !transparentGrgOnly)
            {
                DrawMilitaryMgrsAndSubGridOverlay(w, h);
                DrawTacticalObjectsOverlay(w, h);
                DrawInteractiveDistanceRulerOverlay(w, h);
                DrawPositionedCornerElements(w, h);
            }
        }

        private static void AddCanvasImage(byte[] bytes, double x, double y, double width, double height, double opacity)
        {
            try
            {
                var bmp = new BitmapImage();
                using (var mem = new MemoryStream(bytes)) { bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.StreamSource = mem; bmp.EndInit(); bmp.Freeze(); }
                var img = new Image { Source = bmp, Width = width, Height = height, Stretch = Stretch.Fill, Opacity = opacity };
                Canvas.SetLeft(img, x); Canvas.SetTop(img, y); _mapCanvas.Children.Add(img);
            }
            catch { }
        }

        private static Brush GetGridColorBrush(byte alpha = 210)
        {
            Color c = _gridColorName switch { "Turkusowy" => Color.FromArgb(alpha, 31, 224, 197), "Biały" => Color.FromArgb(alpha, 255, 255, 255), "Czerwony" => Color.FromArgb(alpha, 235, 64, 52), "Czarny" => Color.FromArgb(alpha, 15, 15, 15), _ => Color.FromArgb(alpha, 245, 200, 40) };
            var b = new SolidColorBrush(c); b.Freeze(); return b;
        }

        private static void DrawMilitaryMgrsAndSubGridOverlay(double w, double h)
        {
            if (_coordSystem == "Brak siatki") return;
            var mainBrush = GetGridColorBrush(215);
            var subBrush = GetGridColorBrush(110);
            var haloBrush = new SolidColorBrush(Color.FromArgb(_gridHaloMode.StartsWith("Pełny") ? (byte)210 : (byte)110, 0, 0, 0));
            haloBrush.Freeze();
            bool useHalo = !_gridHaloMode.StartsWith("Brak");

            double metersPerPixel = Math.Max(0.05, (156543.03392 * Math.Cos(_centerLat * Math.PI / 180.0)) / Math.Pow(2.0, _zoom));
            int stepMeters = _subGridDivision.Contains("250") ? 250 : (_subGridDivision.Contains("500") ? 500 : 100);
            double stepPx = Math.Clamp(stepMeters / metersPerPixel, 65.0, 260.0);
            double mainThick = Math.Max(0.6, 1.2 * _gridThicknessMultiplier);

            double startX = (w / 2.0) % stepPx;
            double startY = (h / 2.0) % stepPx;

            for (double x = startX; x < w; x += stepPx)
            {
                if (useHalo) _mapCanvas.Children.Add(new Line { X1 = x, Y1 = 0, X2 = x, Y2 = h, Stroke = haloBrush, StrokeThickness = mainThick + 1.8 });
                _mapCanvas.Children.Add(new Line { X1 = x, Y1 = 0, X2 = x, Y2 = h, Stroke = mainBrush, StrokeThickness = mainThick });

                var (_, lon) = ScreenToLatLon(x, 20, w, h);
                int utmEast = (int)Math.Round(((lon + 180.0) % 6.0) * 111320.0 * Math.Cos(_centerLat * Math.PI / 180.0));
                AddHaloLabel($"{(utmEast / 100) % 1000:D3}", x + 3, 44, mainBrush, 9.5);
            }

            for (double y = startY; y < h; y += stepPx)
            {
                if (useHalo) _mapCanvas.Children.Add(new Line { X1 = 0, Y1 = y, X2 = w, Y2 = y, Stroke = haloBrush, StrokeThickness = mainThick + 1.8 });
                _mapCanvas.Children.Add(new Line { X1 = 0, Y1 = y, X2 = w, Y2 = y, Stroke = mainBrush, StrokeThickness = mainThick });

                var (lat, _) = ScreenToLatLon(20, y, w, h);
                int utmNorth = (int)Math.Round(lat * 110574.0);
                AddHaloLabel($"{(utmNorth / 100) % 1000:D3}", 8, y + 2, mainBrush, 9.5);
            }

            // Podział wewnętrzny kratki 100m: Co 50 m (Ćwiartki A,B,C,D) lub Co 33 m (Keypad 1-9)
            if (_coordSystem.StartsWith("MGRS") && (_subGridDivision.Contains("50 m") || _subGridDivision.Contains("33 m")))
            {
                bool isQuarters = _subGridDivision.Contains("50 m");
                int div = isQuarters ? 2 : 3;
                double subStep = stepPx / div;

                for (double x = startX; x < w; x += stepPx)
                {
                    for (int i = 1; i < div; i++)
                        _mapCanvas.Children.Add(new Line { X1 = x + i * subStep, Y1 = 0, X2 = x + i * subStep, Y2 = h, Stroke = subBrush, StrokeThickness = 0.7, StrokeDashArray = new DoubleCollection { 3, 3 } });
                }
                for (double y = startY; y < h; y += stepPx)
                {
                    for (int j = 1; j < div; j++)
                        _mapCanvas.Children.Add(new Line { X1 = 0, Y1 = y + j * subStep, X2 = w, Y2 = y + j * subStep, Stroke = subBrush, StrokeThickness = 0.7, StrokeDashArray = new DoubleCollection { 3, 3 } });
                }

                if (_gridLabelsMode.Contains("Ćwiartki/Keypad") && stepPx >= 80)
                {
                    string[] qLabels = { "A", "B", "C", "D" };
                    for (double x = startX; x < w - stepPx; x += stepPx)
                        for (double y = startY; y < h - stepPx; y += stepPx)
                        {
                            if (isQuarters)
                            {
                                AddHaloLabel(qLabels[0], x + subStep * 0.4, y + subStep * 0.3, subBrush, 8.5);
                                AddHaloLabel(qLabels[1], x + subStep * 1.4, y + subStep * 0.3, subBrush, 8.5);
                                AddHaloLabel(qLabels[2], x + subStep * 0.4, y + subStep * 1.3, subBrush, 8.5);
                                AddHaloLabel(qLabels[3], x + subStep * 1.4, y + subStep * 1.3, subBrush, 8.5);
                            }
                        }
                }
            }
        }

        private static void AddHaloLabel(string text, double x, double y, Brush fg, double fontSize)
        {
            var b = new Border { Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)), CornerRadius = new CornerRadius(2), Padding = new Thickness(2, 0, 2, 0), IsHitTestVisible = false };
            b.Child = new TextBlock { Text = text, Foreground = fg, FontSize = fontSize, FontWeight = FontWeights.Bold };
            Canvas.SetLeft(b, x); Canvas.SetTop(b, y);
            _mapCanvas.Children.Add(b);
        }

        private static void DrawTacticalObjectsOverlay(double w, double h)
        {
            foreach (var obj in _mapObjects)
            {
                if (obj.Points.Count == 0) continue;
                var strokeBrush = TacticalConfigPanel.BrushFromHex(obj.ColorHex, Color.FromRgb(38, 132, 255));
                Color c = Color.FromRgb(38, 132, 255); try { c = (Color)ColorConverter.ConvertFromString(obj.ColorHex); } catch { }
                var fillBrush = new SolidColorBrush(Color.FromArgb(65, c.R, c.G, c.B)); fillBrush.Freeze();

                if (obj.Kind == "AO" && obj.Points.Count >= 2)
                {
                    var poly = new Polygon { Stroke = strokeBrush, Fill = fillBrush, StrokeThickness = 2.5 };
                    foreach (var pt in obj.Points) poly.Points.Add(LatLonToScreen(pt.Lat, pt.Lon, w, h));
                    _mapCanvas.Children.Add(poly);
                    AddHaloLabel(obj.Name, poly.Points[0].X + 6, poly.Points[0].Y - 10, Brushes.White, 10);
                }
                else if (obj.Kind == "Route" && obj.Points.Count >= 2)
                {
                    var pl = new Polyline { Stroke = strokeBrush, StrokeThickness = 2.8, StrokeDashArray = new DoubleCollection { 4, 2 } };
                    foreach (var pt in obj.Points) pl.Points.Add(LatLonToScreen(pt.Lat, pt.Lon, w, h));
                    _mapCanvas.Children.Add(pl);
                }
                else
                {
                    Point pt = LatLonToScreen(obj.Points[0].Lat, obj.Points[0].Lon, w, h);
                    double sz = obj.Kind == "Dot" ? 10 : 14;
                    var dot = new Ellipse { Width = sz, Height = sz, Fill = strokeBrush, Stroke = Brushes.White, StrokeThickness = 2 };
                    Canvas.SetLeft(dot, pt.X - sz / 2); Canvas.SetTop(dot, pt.Y - sz / 2);
                    _mapCanvas.Children.Add(dot);
                    if (obj.Kind == "Marker") AddHaloLabel($"📍 {obj.Name}", pt.X + 10, pt.Y - 10, Brushes.White, 10.5);
                }
            }
        }

        private static void PlaceInCorner(FrameworkElement el, string corner, double viewW, double viewH, double elemW, double elemH)
        {
            if (corner == "Ukryj") return;
            double x = corner.StartsWith("Lewy") ? 16 : Math.Max(20, viewW - elemW - 16);
            double y = corner.Contains("górny") ? 16 : Math.Max(20, viewH - elemH - 16);
            Canvas.SetLeft(el, x); Canvas.SetTop(el, y);
            _mapCanvas.Children.Add(el);
        }

        private static void DrawPositionedCornerElements(double w, double h)
        {
            // 1. Znacznik Północy (N)
            if (_northArrowPos != "Ukryj")
            {
                var compass = new Border { Width = 52, Height = 62, Background = new SolidColorBrush(Color.FromArgb(220, 8, 19, 22)), BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(6), IsHitTestVisible = false };
                var cs = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                cs.Children.Add(new TextBlock { Text = "N", Foreground = TacticalConfigPanel.BrushAccent, FontWeight = FontWeights.Bold, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center });
                cs.Children.Add(new TextBlock { Text = "▲", Foreground = Brushes.White, FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -4, 0, -2) });
                cs.Children.Add(new TextBlock { Text = "MGRS", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center });
                compass.Child = cs;
                PlaceInCorner(compass, _northArrowPos, w, h, 52, 62);
            }

            // 2. Podziałka ze skalą na mapie
            if (_scaleBarPos != "Ukryj")
            {
                double metersPerPixel = Math.Max(0.05, (156543.03392 * Math.Cos(_centerLat * Math.PI / 180.0)) / Math.Pow(2.0, _zoom));
                double[] nice = { 50, 100, 200, 500, 1000, 2000, 5000 };
                double chosen = 200;
                foreach (var d in nice) { double px = d / metersPerPixel; if (px >= 80 && px <= 220) { chosen = d; break; } }
                double barW = Math.Clamp(chosen / metersPerPixel, 60, 240);
                int scaleRatio = (int)(Math.Round((metersPerPixel / 0.0002645833) / 50.0) * 50);

                var sbBorder = new Border { Width = barW + 32, Height = 54, Background = new SolidColorBrush(Color.FromArgb(225, 8, 19, 22)), BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(6), IsHitTestVisible = false };
                var cnv = new Canvas();
                var hdr = new TextBlock { Text = $"SKALA 1:{scaleRatio:N0}", Foreground = TacticalConfigPanel.BrushAccent, FontSize = 10, FontWeight = FontWeights.Bold };
                Canvas.SetLeft(hdr, 12); Canvas.SetTop(hdr, 4); cnv.Children.Add(hdr);

                double segW = barW / 4.0;
                for (int s = 0; s < 4; s++)
                {
                    var r = new Rectangle { Width = segW + 0.5, Height = 7, Fill = (s % 2 == 0) ? TacticalConfigPanel.BrushAccent : Brushes.White, Stroke = Brushes.Black, StrokeThickness = 0.7 };
                    Canvas.SetLeft(r, 12 + s * segW); Canvas.SetTop(r, 23); cnv.Children.Add(r);
                }
                var t0 = new TextBlock { Text = "0", Foreground = Brushes.White, FontSize = 9 }; Canvas.SetLeft(t0, 12); Canvas.SetTop(t0, 32); cnv.Children.Add(t0);
                var t1 = new TextBlock { Text = $"{chosen:0} m", Foreground = Brushes.White, FontSize = 9, FontWeight = FontWeights.Bold }; Canvas.SetLeft(t1, 12 + barW - 24); Canvas.SetTop(t1, 32); cnv.Children.Add(t1);
                sbBorder.Child = cnv;
                PlaceInCorner(sbBorder, _scaleBarPos, w, h, barW + 32, 54);
            }

            // 3. Legenda i Tabela na mapie
            if (_legendPos != "Ukryj")
            {
                var legBorder = new Border { Width = 235, Height = 68, Background = new SolidColorBrush(Color.FromArgb(225, 8, 19, 22)), BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 10, 6), IsHitTestVisible = false };
                var ls = new StackPanel();
                ls.Children.Add(new TextBlock { Text = $"MAPA TAKTYCZNA ({_sheetFormat.Split(' ')[0]})", Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 10.5 });
                ls.Children.Add(new TextBlock { Text = $"Siatka: {_coordSystem.Split(' ')[0]} | {_subGridDivision.Split('(')[0]}", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 9, Margin = new Thickness(0, 2, 0, 4) });
                var fw = new WrapPanel();
                foreach (var f in TacticalConfigPanel.GetConfiguredFactions())
                {
                    var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0) };
                    sp.Children.Add(new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(2), Background = TacticalConfigPanel.BrushFromHex(f.ColorHex, Color.FromRgb(38, 132, 255)), Margin = new Thickness(0, 0, 4, 0) });
                    sp.Children.Add(new TextBlock { Text = f.Name, Foreground = Brushes.White, FontSize = 9, FontWeight = FontWeights.Bold });
                    fw.Children.Add(sp);
                }
                ls.Children.Add(fw);
                legBorder.Child = ls;
                PlaceInCorner(legBorder, _legendPos, w, h, 235, 68);
            }
        }

        private static void DrawInteractiveDistanceRulerOverlay(double w, double h)
        {
            if (_rulerStart == null || _rulerEnd == null) return;
            Point p1 = LatLonToScreen(_rulerStart.Value.Lat, _rulerStart.Value.Lon, w, h);
            Point p2 = LatLonToScreen(_rulerEnd.Value.Lat, _rulerEnd.Value.Lon, w, h);
            var yb = TacticalConfigPanel.CreateFrozenBrush(245, 184, 46);
            _mapCanvas.Children.Add(new Line { X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y, Stroke = yb, StrokeThickness = 2.2, StrokeDashArray = new DoubleCollection { 4, 2 } });

            double dLat = (_rulerEnd.Value.Lat - _rulerStart.Value.Lat) * Math.PI / 180.0;
            double dLon = (_rulerEnd.Value.Lon - _rulerStart.Value.Lon) * Math.PI / 180.0;
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(_rulerStart.Value.Lat * Math.PI / 180.0) * Math.Cos(_rulerEnd.Value.Lat * Math.PI / 180.0) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            double dist = 6371000.0 * 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
            AddHaloLabel($"📏 {dist:0} m", (p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2 - 12, yb, 10.5);
        }

        private static byte[] CaptureMapCanvasPngBytes()
        {
            int w = (int)Math.Max(800, _mapCanvas.ActualWidth), h = (int)Math.Max(600, _mapCanvas.ActualHeight);
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32); rtb.Render(_mapCanvas);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
            using var ms = new MemoryStream(); enc.Save(ms); return ms.ToArray();
        }

        private static void SaveMapProjectJson(Window win)
        {
            try
            {
                var dlg = new SaveFileDialog { Title = "Zapisz projekt mapy (.json)", Filter = "Projekt mapy JSON (*.json)|*.json", FileName = "mapa_taktyczna.json" };
                if (dlg.ShowDialog() != true) return;
                var root = new JsonObject
                {
                    ["centerLat"] = _centerLat,
                    ["centerLon"] = _centerLon,
                    ["zoom"] = _zoom,
                    ["scale"] = _targetScaleDenominator,
                    ["format"] = _sheetFormat,
                    ["orientation"] = _sheetOrientation,
                    ["coordSystem"] = _coordSystem,
                    ["subGrid"] = _subGridDivision
                };
                var arr = new JsonArray();
                foreach (var o in _mapObjects)
                {
                    var pts = new JsonArray();
                    foreach (var p in o.Points) pts.Add(new JsonObject { ["lat"] = p.Lat, ["lon"] = p.Lon });
                    arr.Add(new JsonObject { ["kind"] = o.Kind, ["name"] = o.Name, ["color"] = o.ColorHex, ["points"] = pts });
                }
                root["objects"] = arr;
                File.WriteAllText(dlg.FileName, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
                if (_statusText != null) _statusText.Text = $"✓ Zapisano projekt mapy JSON: {dlg.FileName}";
            }
            catch { }
        }

        private static void LoadMapProjectJsonOrKml()
        {
            try
            {
                var dlg = new OpenFileDialog { Title = "Wczytaj projekt (.json / .kml / .gpx)", Filter = "Pliki mapy (*.json;*.kml;*.gpx)|*.json;*.kml;*.gpx" };
                if (dlg.ShowDialog() != true) return;
                string txt = File.ReadAllText(dlg.FileName, Encoding.UTF8);
                if (dlg.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    if (JsonNode.Parse(txt) is JsonObject root)
                    {
                        if (double.TryParse(root["centerLat"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double la)) _centerLat = la;
                        if (double.TryParse(root["centerLon"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double lo)) _centerLon = lo;
                        if (int.TryParse(root["zoom"]?.ToString(), out int zm)) _zoom = zm;
                        _mapObjects.Clear();
                        if (root["objects"] is JsonArray arr)
                        {
                            foreach (var node in arr.OfType<JsonObject>())
                            {
                                var item = new TacticalMapObjectItem { Kind = node["kind"]?.ToString() ?? "Marker", Name = node["name"]?.ToString() ?? "Punkt", ColorHex = node["color"]?.ToString() ?? "#2684FF" };
                                if (node["points"] is JsonArray pts)
                                    foreach (var p in pts.OfType<JsonObject>())
                                        if (double.TryParse(p["lat"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double pla) && double.TryParse(p["lon"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double plo))
                                            item.Points.Add((pla, plo));
                                if (item.Points.Count > 0) _mapObjects.Add(item);
                            }
                        }
                    }
                }
                else
                {
                    foreach (Match m in Regex.Matches(txt, @"<wpt[^>]*lat=""([0-9\.\-]+)""[^>]*lon=""([0-9\.\-]+)""", RegexOptions.IgnoreCase))
                        if (double.TryParse(m.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double la) && double.TryParse(m.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double lo))
                        { var it = new TacticalMapObjectItem { Kind = "Marker", Name = $"GPX-{_mapObjects.Count + 1}", ColorHex = _selectedObjectColorHex }; it.Points.Add((la, lo)); _mapObjects.Add(it); }
                }
                RefreshObjectsListUI();
                _ = RenderMapTilesAsync();
            }
            catch { }
        }

        private static async Task ExportMapToFtakSourceAsync(Window win)
        {
            try
            {
                string sourceDir = TacticalConfigPanel.GetPrimarySourceDirectory(win);
                string mapsDir = System.IO.Path.Combine(sourceDir, "maps"), atakDir = System.IO.Path.Combine(sourceDir, "atak");
                Directory.CreateDirectory(mapsDir); Directory.CreateDirectory(atakDir);

                bool grgTransparent = _kmzGrgMode.StartsWith("Przezroczysta");
                if (grgTransparent) await RenderMapTilesAsync(true);
                byte[] pngBytes = CaptureMapCanvasPngBytes();
                if (grgTransparent) await RenderMapTilesAsync(false);

                double w = Math.Max(800, _mapCanvas.ActualWidth), h = Math.Max(600, _mapCanvas.ActualHeight);
                var (north, west) = ScreenToLatLon(0, 0, w, h); var (south, east) = ScreenToLatLon(w, h, w, h);

                await Task.Run(() =>
                {
                    WriteKmzGroundOverlayArchive(System.IO.Path.Combine(mapsDir, "grg_map_overlay.kmz"), "Mapa_GRG_ATAK", pngBytes, north, south, east, west);
                    WriteGpxFile(System.IO.Path.Combine(atakDir, "punkty_taktyczne.gpx"), "Punkty_AO");
                });

                await TacticalConfigPanel.RefreshSourceSizeFromExternalAsync(win);
                if (_statusText != null) _statusText.Text = "✓ Zapisano mapę .KMZ (do kategorii Mapy) oraz punkty .GPX (do kategorii Dane taktyczne) w folderze source!";
            }
            catch { }
        }

        private static async Task ExportStandaloneFilesAsync(Window win, string format)
        {
            try
            {
                string sourceDir = TacticalConfigPanel.GetPrimarySourceDirectory(win);
                string parentDir = System.IO.Path.GetDirectoryName(System.IO.Path.TrimEndingDirectorySeparator(sourceDir)) ?? AppDomain.CurrentDomain.BaseDirectory;
                string outDir = System.IO.Path.Combine(parentDir, "release_maps"); Directory.CreateDirectory(outDir);

                bool grgTransparent = (format == "kmz" && _kmzGrgMode.StartsWith("Przezroczysta"));
                if (grgTransparent) await RenderMapTilesAsync(true);
                byte[] pngBytes = CaptureMapCanvasPngBytes();
                if (grgTransparent) await RenderMapTilesAsync(false);

                double w = Math.Max(800, _mapCanvas.ActualWidth), h = Math.Max(600, _mapCanvas.ActualHeight);
                var (north, west) = ScreenToLatLon(0, 0, w, h); var (south, east) = ScreenToLatLon(w, h, w, h);

                await Task.Run(() =>
                {
                    if (format == "kmz") WriteKmzGroundOverlayArchive(System.IO.Path.Combine(outDir, "Mapa_Taktyczna_ATAK.kmz"), "Mapa_AO", pngBytes, north, south, east, west);
                    else if (format == "gpx") WriteGpxFile(System.IO.Path.Combine(outDir, "Mapa_Taktyczna_Garmin.gpx"), "Mapa_AO");
                });

                if (_statusText != null) _statusText.Text = $"✓ Pobrano plik .{format.ToUpperInvariant()} do: {outDir}";
                TacticalConfigPanel.TryOpenFolderInExplorer(outDir);
            }
            catch { }
        }

        private static void WriteKmzGroundOverlayArchive(string kmzPath, string title, byte[] mapPngBytes, double north, double south, double east, double west)
        {
            if (File.Exists(kmzPath)) File.Delete(kmzPath);
            using var fs = new FileStream(kmzPath, FileMode.CreateNew, FileAccess.Write);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create, false, Encoding.UTF8);
            var imgEntry = zip.CreateEntry("files/grg_overlay.png", CompressionLevel.Optimal);
            using (var s = imgEntry.Open()) s.Write(mapPngBytes, 0, mapPngBytes.Length);

            var sb = new StringBuilder();
            sb.AppendLine(@"<?xml version=""1.0"" encoding=""UTF-8""?><kml xmlns=""http://www.opengis.net/kml/2.2""><Document>");
            sb.AppendLine($"  <name>{title}</name><GroundOverlay><name>{title}</name><Icon><href>files/grg_overlay.png</href></Icon>");
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"    <LatLonBox><north>{north:0.000000}</north><south>{south:0.000000}</south><east>{east:0.000000}</east><west>{west:0.000000}</west></LatLonBox></GroundOverlay>"));
            foreach (var o in _mapObjects.Where(x => x.Points.Count > 0))
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  <Placemark><name>{o.Name}</name><Point><coordinates>{o.Points[0].Lon:0.000000},{o.Points[0].Lat:0.000000},0</coordinates></Point></Placemark>"));
            sb.AppendLine("</Document></kml>");

            var kmlEntry = zip.CreateEntry("doc.kml", CompressionLevel.Optimal);
            using var sw = new StreamWriter(kmlEntry.Open(), Encoding.UTF8);
            sw.Write(sb.ToString());
        }

        private static void WriteGpxFile(string gpxPath, string title)
        {
            var sb = new StringBuilder();
            sb.AppendLine(@"<?xml version=""1.0"" encoding=""UTF-8""?><gpx version=""1.1"" creator=""Field TAK Hub Builder 2.4"" xmlns=""http://www.topografix.com/GPX/1/1"">");
            foreach (var o in _mapObjects.Where(x => x.Points.Count > 0))
            {
                if (o.Points.Count == 1)
                    sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  <wpt lat=\"{o.Points[0].Lat:0.000000}\" lon=\"{o.Points[0].Lon:0.000000}\"><name>{o.Name}</name></wpt>"));
                else
                {
                    sb.AppendLine($"  <rte><name>{o.Name}</name>");
                    foreach (var pt in o.Points) sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"    <rtept lat=\"{pt.Lat:0.000000}\" lon=\"{pt.Lon:0.000000}\" />"));
                    sb.AppendLine("  </rte>");
                }
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
                string outDir = System.IO.Path.Combine(parentDir, "release_maps"); Directory.CreateDirectory(outDir);

                bool landscape = _sheetOrientation == "Poziomo";
                string fmt = _sheetFormat.StartsWith("A3") ? "A3" : "A4";
                string pdfPath = System.IO.Path.Combine(outDir, $"Mapa_Taktyczna_{fmt}_1_{_targetScaleDenominator}.pdf");

                int pageW = landscape ? 1754 : 1240, pageH = landscape ? 1240 : 1754;
                int mapW = (int)Math.Max(800, _mapCanvas.ActualWidth), mapH = (int)Math.Max(600, _mapCanvas.ActualHeight);
                var mapRtb = new RenderTargetBitmap(mapW, mapH, 96, 96, PixelFormats.Pbgra32); mapRtb.Render(_mapCanvas);

                var pageVisual = new DrawingVisual();
                using (DrawingContext dc = pageVisual.RenderOpen())
                {
                    dc.DrawRectangle(TacticalConfigPanel.BrushCardBg, null, new Rect(0, 0, pageW, pageH));
                    dc.DrawImage(mapRtb, new Rect(30, 30, pageW - 60, pageH - 60));
                }

                var fullRtb = new RenderTargetBitmap(pageW, pageH, 96, 96, PixelFormats.Pbgra32); fullRtb.Render(pageVisual);
                var jpgEnc = new JpegBitmapEncoder { QualityLevel = 92 }; jpgEnc.Frames.Add(BitmapFrame.Create(fullRtb));
                using var jpgMs = new MemoryStream(); jpgEnc.Save(jpgMs);
                byte[] jpegBytes = jpgMs.ToArray();

                await Task.Run(() => WriteSingleImagePdf(pdfPath, jpegBytes, pageW, pageH, landscape));
                if (_statusText != null) _statusText.Text = $"✓ Zapisano gotowy do druku plik PDF ({fmt} {(_sheetOrientation)}): {pdfPath}";
                TacticalConfigPanel.TryOpenFolderInExplorer(outDir);
            }
            catch { }
        }

        private static void WriteSingleImagePdf(string pdfPath, byte[] jpegBytes, int pixelW, int pixelH, bool landscape)
        {
            using var fs = new FileStream(pdfPath, FileMode.Create, FileAccess.Write);
            void WriteAscii(string s) { byte[] b = Encoding.ASCII.GetBytes(s); fs.Write(b, 0, b.Length); }
            int pdfW = landscape ? 842 : 595, pdfH = landscape ? 595 : 842;

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
    }
}
