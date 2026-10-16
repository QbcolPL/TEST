#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
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
        public string FallbackUrlTemplate { get; set; }
        public override string ToString() => Name;
    }

    public class NatoSymbolOption
    {
        public string Code { get; set; }
        public string Label { get; set; }
        public override string ToString() => Label;
    }

    public class TacticalMapObjectItem
    {
        public string Kind { get; set; } = "Marker";
        public string SymbolCode { get; set; } = "NATO_HQ";
        public string Name { get; set; } = "HQ";
        public string ColorHex { get; set; } = "#2684FF";
        public List<(double Lat, double Lon)> Points { get; set; } = new List<(double Lat, double Lon)>();
    }

    public static class TacticalMapGeneratorView
    {
        private static readonly HttpClient Http = CreateHttpClient();
        private static readonly SemaphoreSlim TileThrottle = new SemaphoreSlim(6, 6);
        private static readonly ConcurrentDictionary<string, byte[]> TileCache = new ConcurrentDictionary<string, byte[]>();

        private static UIElement _originalWindowContent;
        private static Border _fullScreenOverlay;
        private static Button _tabBuilderBtn, _tabMapBtn, _btnToolPan, _btnToolMarker, _btnToolDot, _btnToolAo, _btnToolRoute;
        private static readonly List<Button> _scalePresetButtons = new List<Button>();
        private static Canvas _mapCanvas;
        private static TextBlock _statusText, _coordsBadge, _gridThicknessValueLabel, _mainGridOpacityValueLabel, _subGridOpacityValueLabel;
        private static StackPanel _objectsListStack;

        private static double _centerLat = 49.9135, _centerLon = 19.0238, _gridThicknessMultiplier = 1.0, _dragStartLat, _dragStartLon;
        private static double _mainGridOpacity = 0.85;
        private static double _subGridOpacity = 0.75;
        private static int _zoom = 16, _targetScaleDenominator = 5000;
        private static string _sheetFormat = "A4 (297 × 210 mm)", _sheetOrientation = "Poziomo";
        private static bool _showLidarSingleWms, _isDragging;
        private static string _coordSystem = "MGRS / UTM (Siatka wojskowa w metrach)", _subGridDivision = "Co 33 m (Keypad 1-9)", _gridLabelsMode = "MGRS + Ćwiartki/Keypad", _gridColorName = "Żółty", _gridHaloMode = "Pełny czarny obrys";
        private static string _activeTool = "PAN", _selectedObjectColorHex = "#2684FF", _selectedNatoSymbolCode = "NATO_HQ";
        private static string _northArrowPos = "Lewy górny róg", _scaleBarPos = "Lewy dolny róg", _legendPos = "Prawy górny róg", _kmzGrgMode = "Przezroczysta siatka";

        private static Point _dragStartPoint;
        private static (double Lat, double Lon)? _rulerStart, _rulerEnd;
        private static readonly List<TacticalMapObjectItem> _mapObjects = new List<TacticalMapObjectItem>();
        private static TacticalMapObjectItem _activeDraftPoly;
        private static TacticalMapLayerDef _activeLayer;
        private static TextBox _objectNameInputBox, _scaleInputBox, _searchPlaceBox;

        public static readonly List<NatoSymbolOption> NatoSymbols = new List<NatoSymbolOption>
        {
            new NatoSymbolOption { Code = "NATO_HQ",     Label = "🚩 NATO HQ — Sztab / Dowództwo" },
            new NatoSymbolOption { Code = "NATO_INF",    Label = "⚔️ NATO Piechota / Drużyna [X]" },
            new NatoSymbolOption { Code = "NATO_RECON",  Label = "🔭 NATO Zwiad / Recon [/]" },
            new NatoSymbolOption { Code = "NATO_MED",    Label = "➕ NATO Punkt Medyczny / MEDEVAC [+]" },
            new NatoSymbolOption { Code = "NATO_COMMS",  Label = "📡 NATO Węzeł Łączności / Przekaźnik" },
            new NatoSymbolOption { Code = "NATO_SUPPLY", Label = "📦 NATO Punkt Amunicyjny / Logistyka" },
            new NatoSymbolOption { Code = "NATO_OP",     Label = "👁️ NATO Punkt Obserwacyjny [△]" },
            new NatoSymbolOption { Code = "NATO_SNIPER", Label = "🎯 NATO Snajper / Strzelec [⊕]" },
            new NatoSymbolOption { Code = "NATO_CP",     Label = "🛡️ NATO Punkt Kontrolny / Respawn" },
            new NatoSymbolOption { Code = "NATO_LZ",     Label = "🚁 NATO Strefa Lądowania / Zrzutu (LZ)" },
            new NatoSymbolOption { Code = "NATO_OBJ",    Label = "💠 NATO Cel Operacji (Objective)" },
            new NatoSymbolOption { Code = "NATO_HAZARD", Label = "⚠️ NATO Zagrożenie / Strefa No-Go [!]" }
        };

        public static readonly List<TacticalMapLayerDef> Layers = new List<TacticalMapLayerDef>
        {
            new TacticalMapLayerDef { Id = "google_hyb", Name = "Google Satelita + Opisy", IsWmsEpsg3857 = false, UrlTemplate = "https://mt1.google.com/vt/lyrs=y&x={x}&y={y}&z={z}" },
            new TacticalMapLayerDef { Id = "google_sat", Name = "Google Satelita (Czysta Ortofotomapa)", IsWmsEpsg3857 = false, UrlTemplate = "https://mt1.google.com/vt/lyrs=s&x={x}&y={y}&z={z}" },
            new TacticalMapLayerDef { Id = "google_ter", Name = "Google Teren (Warstwice)", IsWmsEpsg3857 = false, UrlTemplate = "https://mt1.google.com/vt/lyrs=p&x={x}&y={y}&z={z}" },
            new TacticalMapLayerDef
            {
                Id = "geoportal_hires",
                Name = "Geoportal Ortofotomapa HIGH-RES (PZGIK)",
                IsWmsEpsg3857 = true,
                UrlTemplate = "https://mapy.geoportal.gov.pl/wss/service/PZGIK/ORTO/WMS/HighResolution?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=Raster&STYLES=&SRS=EPSG:3857&BBOX={bbox}&WIDTH={w}&HEIGHT={h}&FORMAT=image/jpeg",
                FallbackUrlTemplate = "https://mapy.geoportal.gov.pl/wss/service/PZGIK/ORTO/WMS/StandardResolution?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=Raster&STYLES=&SRS=EPSG:3857&BBOX={bbox}&WIDTH={w}&HEIGHT={h}&FORMAT=image/jpeg"
            },
            new TacticalMapLayerDef
            {
                Id = "geoportal_std",
                Name = "Geoportal Ortofotomapa Standard (PZGIK)",
                IsWmsEpsg3857 = true,
                UrlTemplate = "https://mapy.geoportal.gov.pl/wss/service/PZGIK/ORTO/WMS/StandardResolution?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=Raster&STYLES=&SRS=EPSG:3857&BBOX={bbox}&WIDTH={w}&HEIGHT={h}&FORMAT=image/jpeg"
            },
            new TacticalMapLayerDef
            {
                Id = "geoportal_lidar",
                Name = "Geoportal LiDAR ISOK (Cieniowanie NMT)",
                IsWmsEpsg3857 = true,
                UrlTemplate = "https://mapy.geoportal.gov.pl/wss/service/PZGIK/NMT/GRID1/WMS/ShadedRelief?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=Raster&STYLES=&SRS=EPSG:3857&BBOX={bbox}&WIDTH={w}&HEIGHT={h}&FORMAT=image/jpeg"
            },
            new TacticalMapLayerDef
            {
                Id = "geoportal_topo",
                Name = "Geoportal Mapa Topograficzna (TOPO / BDOT10k)",
                IsWmsEpsg3857 = true,
                UrlTemplate = "https://mapy.geoportal.gov.pl/wss/service/img/guest/TOPO/MapServer/WMSServer?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=Raster&STYLES=&SRS=EPSG:3857&BBOX={bbox}&WIDTH={w}&HEIGHT={h}&FORMAT=image/jpeg",
                FallbackUrlTemplate = "https://mapy.geoportal.gov.pl/wss/ext/KrajowaIntegracjaMapTopograficznych?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=raster&STYLES=&SRS=EPSG:3857&BBOX={bbox}&WIDTH={w}&HEIGHT={h}&FORMAT=image/png"
            },
            new TacticalMapLayerDef { Id = "esri_sat", Name = "ESRI World Imagery (Ortofotomapa)", IsWmsEpsg3857 = false, UrlTemplate = "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}" },
            new TacticalMapLayerDef { Id = "osm_std", Name = "OpenStreetMap Standard", IsWmsEpsg3857 = false, UrlTemplate = "https://tile.openstreetmap.org/{z}/{x}/{y}.png" },
            new TacticalMapLayerDef { Id = "open_topomap", Name = "OpenTopoMap (Mapa Topograficzna)", IsWmsEpsg3857 = false, UrlTemplate = "https://a.tile.opentopomap.org/{z}/{x}/{y}.png" },
            new TacticalMapLayerDef { Id = "open_fotomap", Name = "OpenFotoMap / CyclOSM (Siatka Szlaków)", IsWmsEpsg3857 = false, UrlTemplate = "https://a.tile-cyclosm.openstreetmap.fr/cyclosm/{z}/{x}/{y}.png" }
        };

        private static HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                ServerCertificateCustomValidationCallback = (_, __, ___, ____) => true,
                MaxConnectionsPerServer = 12
            };
            var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(18) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
            c.DefaultRequestHeaders.Referrer = new Uri("https://mapy.geoportal.gov.pl/");
            c.DefaultRequestHeaders.Accept.ParseAdd("image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8");
            return c;
        }

        // Weryfikuje nagłówki binarne obrazu (odrzuca komunikaty błędów XML ServiceException zwracane przez Geoportal)
        private static bool IsValidImagePayload(byte[] data)
        {
            if (data == null || data.Length < 128) return false;
            // JPEG: FF D8 FF
            if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return true;
            // PNG: 89 50 4E 47
            if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) return true;
            // GIF / WebP / BMP
            if (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46) return true;
            if (data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46) return true;
            return false;
        }

        private static double GetExactMetersPerPixel()
        {
            return Math.Max(0.05, _targetScaleDenominator * 0.000264583333);
        }

        private static double GetScaledTileSizePx()
        {
            double nativeTileMetersPerPx = (156543.03392 * Math.Cos(_centerLat * Math.PI / 180.0)) / Math.Pow(2.0, _zoom);
            double exactMetersPerPx = GetExactMetersPerPixel();
            return Math.Clamp(256.0 * (nativeTileMetersPerPx / exactMetersPerPx), 110.0, 620.0);
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
                    var rightInfo = new TextBlock { Text = "Field TAK Hub 2.4  •  Geoportal WMS Engine  •  Symbole NATO APP-6  •  Przezroczystość MGRS/Keypad", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
                    DockPanel.SetDock(rightInfo, Dock.Right);
                    navDock.Children.Add(rightInfo);

                    var tabsStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    _tabBuilderBtn = new Button { Content = "📦 Konfigurator Paczek (.ftak)", Height = 30, Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(0, 0, 8, 0), Background = TacticalConfigPanel.BrushButtonAccent, Foreground = TacticalConfigPanel.BrushDarkFg, BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1), FontWeight = FontWeights.Bold, FontSize = 11.5, Cursor = Cursors.Hand };
                    _tabMapBtn = new Button { Content = "🗺️ Generator Map Taktycznych 1:1 (Pełny Ekran)", Height = 30, Padding = new Thickness(16, 0, 16, 0), Background = TacticalConfigPanel.BrushItemBg, Foreground = TacticalConfigPanel.BrushAccent, BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1), FontWeight = FontWeights.Bold, FontSize = 11.5, Cursor = Cursors.Hand };

                    _tabBuilderBtn.Click += (_, __) => HideFullScreenMapTab();
                    _tabMapBtn.Click += (_, __) => ShowFullScreenMapTab(win);

                    tabsStack.Children.Add(_tabBuilderBtn); tabsStack.Children.Add(_tabMapBtn);
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
                ApplyScaleDenominator(_targetScaleDenominator);
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
            if (_scaleInputBox != null && _scaleInputBox.Text != _targetScaleDenominator.ToString())
                _scaleInputBox.Text = _targetScaleDenominator.ToString();

            double targetMetersPerPx = GetExactMetersPerPixel();
            double equatorMetersPerPx = 156543.03392 * Math.Cos(_centerLat * Math.PI / 180.0);
            _zoom = Math.Clamp((int)Math.Round(Math.Log(equatorMetersPerPx / targetMetersPerPx, 2.0)), 5, 19);

            foreach (var b in _scalePresetButtons)
            {
                bool isCurrent = b.Content?.ToString() == $"1:{_targetScaleDenominator}";
                b.Background = isCurrent ? TacticalConfigPanel.BrushButtonAccent : TacticalConfigPanel.BrushInputBg;
                b.Foreground = isCurrent ? TacticalConfigPanel.BrushDarkFg : Brushes.White;
                b.BorderBrush = isCurrent ? TacticalConfigPanel.BrushAccent : TacticalConfigPanel.BrushInputBorder;
            }

            _ = RenderMapTilesAsync();
        }

        private static Border BuildFullScreenMapGeneratorUI(Window win)
        {
            _activeLayer = Layers[0];

            var rootBorder = new Border { Background = TacticalConfigPanel.BrushCardBg, Tag = "INJECTED_MAP_OVERLAY" };
            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });

            var bodyGrid = new Grid();
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(425) });
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var leftScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = TacticalConfigPanel.BrushCardBg, Padding = new Thickness(10) };
            var leftStack = new StackPanel { Margin = new Thickness(6) };

            // SEKCJA 1: ARKUSZ, DOKŁADNA SKALA MAPY 1:X, PODKŁAD & LiDAR ISOK
            var sec1 = CreateSectionCard("1. ARKUSZ, DOKŁADNA SKALA MAPY (1:X), PODKŁAD & LiDAR ISOK");
            var searchRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
            _searchPlaceBox = (TextBox)TacticalConfigPanel.TextCtrl("Czechowice-Dziedzice", _ => { });
            var btnFind = MakeActionButton("🔍 Szukaj", true, async () => await SearchLocationNominatimAsync());
            btnFind.Height = 25; btnFind.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(_searchPlaceBox, 0); Grid.SetColumn(btnFind, 1);
            searchRow.Children.Add(_searchPlaceBox); searchRow.Children.Add(btnFind);
            sec1.Children.Add(TacticalConfigPanel.FieldBox("Wyszukaj miejscowość / teren gry:", searchRow));

            var fmtGrid = new UniformGrid { Columns = 2 };
            fmtGrid.Children.Add(TacticalConfigPanel.FieldBox("Format arkusza:", TacticalConfigPanel.ComboCtrl(new[] { "A4 (297 × 210 mm)", "A3 (420 × 297 mm)" }, _sheetFormat, win, v => _sheetFormat = v)));
            fmtGrid.Children.Add(TacticalConfigPanel.FieldBox("Orientacja:", TacticalConfigPanel.ComboCtrl(new[] { "Poziomo", "Pionowo" }, _sheetOrientation, win, v => _sheetOrientation = v)));
            sec1.Children.Add(fmtGrid);

            sec1.Children.Add(new TextBlock { Text = "Skala mapy (wpisz dowolną 1:X lub wybierz poniżej):", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 10, Margin = new Thickness(0, 4, 0, 3) });
            var scaleCustomGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            scaleCustomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            scaleCustomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            scaleCustomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
            scaleCustomGrid.Children.Add(new TextBlock { Text = "1 :", Foreground = TacticalConfigPanel.BrushAccent, FontWeight = FontWeights.Bold, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center });
            _scaleInputBox = (TextBox)TacticalConfigPanel.TextCtrl("5000", _ => { });
            Grid.SetColumn(_scaleInputBox, 1); scaleCustomGrid.Children.Add(_scaleInputBox);
            var btnSetScale = MakeActionButton("Ustaw skalę", true, () => { if (int.TryParse(_scaleInputBox.Text.Trim(), out int sc)) ApplyScaleDenominator(sc); });
            btnSetScale.Height = 25; btnSetScale.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(btnSetScale, 2); scaleCustomGrid.Children.Add(btnSetScale);
            sec1.Children.Add(scaleCustomGrid);

            _scalePresetButtons.Clear();
            var scalePresetsRow = new UniformGrid { Columns = 6, Margin = new Thickness(0, 0, 0, 4) };
            foreach (int p in new[] { 2500, 4000, 5000, 7500, 10000, 15000 })
            {
                int cap = p;
                var b = MakeActionButton($"1:{cap}", cap == 5000, () => ApplyScaleDenominator(cap));
                b.Height = 24; b.Margin = new Thickness(1); b.FontSize = 9.5;
                _scalePresetButtons.Add(b);
                scalePresetsRow.Children.Add(b);
            }
            sec1.Children.Add(scalePresetsRow);
            var btnScale25k = MakeActionButton("1:25000", false, () => ApplyScaleDenominator(25000));
            btnScale25k.Height = 24; btnScale25k.Margin = new Thickness(1, 0, 1, 8); btnScale25k.FontSize = 10;
            _scalePresetButtons.Add(btnScale25k);
            sec1.Children.Add(btnScale25k);

            var layerCombo = new ComboBox { ItemsSource = Layers, SelectedIndex = 0, Height = 26, FontSize = 10.5, Margin = new Thickness(0, 0, 0, 8), Tag = "INJECTED" };
            TacticalConfigPanel.ApplyUnifiedComboBoxTheme(layerCombo, win);
            layerCombo.SelectionChanged += (_, __) => { if (layerCombo.SelectedItem is TacticalMapLayerDef def) { _activeLayer = def; _ = RenderMapTilesAsync(); } };
            sec1.Children.Add(TacticalConfigPanel.FieldBox("Podkład bazowy (Geoportal WMS Single-Request / Google / ESRI / OSM):", layerCombo));

            var lidarBox = new Border { Background = TacticalConfigPanel.BrushInputBg, BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 7, 10, 7) };
            var chkLidar = new CheckBox { Content = "Nałóż rzeźbę LiDAR ISOK (1 zapytanie WMS - bez dziur)", IsChecked = false, Foreground = TacticalConfigPanel.BrushAccent, FontWeight = FontWeights.SemiBold, FontSize = 10.5 };
            chkLidar.Checked += (_, __) => { _showLidarSingleWms = true; _ = RenderMapTilesAsync(); };
            chkLidar.Unchecked += (_, __) => { _showLidarSingleWms = false; _ = RenderMapTilesAsync(); };
            lidarBox.Child = chkLidar;
            sec1.Children.Add(lidarBox);
            leftStack.Children.Add(WrapCard(sec1));

            // SEKCJA 2: SYSTEM WSPÓŁRZĘDNYCH I SIATKA + SUWAKI GRUBOŚCI I PRZEZROCZYSTOŚCI (GŁÓWNEJ ORAZ POMOCNICZEJ KEYPAD/ĆWIARTKI)
            var sec2 = CreateSectionCard("2. SYSTEM WSPÓŁRZĘDNYCH, GRUBOŚĆ & PRZEZROCZYSTOŚĆ SIATEK");
            sec2.Children.Add(TacticalConfigPanel.FieldBox("System współrzędnych i typ siatki na mapie:", TacticalConfigPanel.ComboCtrl(new[] { "MGRS / UTM (Siatka wojskowa w metrach)", "Siatka Sektorowa (A1, B2, C3...)", "WGS84 (Stopnie geograficzne Lat/Lon)", "Brak siatki" }, _coordSystem, win, v => { _coordSystem = v; _ = RenderMapTilesAsync(); })));

            var subGridRow = new UniformGrid { Columns = 2 };
            subGridRow.Children.Add(TacticalConfigPanel.FieldBox("Podział wewnątrz 100 m:", TacticalConfigPanel.ComboCtrl(new[] { "Co 33 m (Keypad 1-9)", "Co 50 m (Ćwiartki A,B,C,D)", "Brak podziału (Tylko 100 m)", "Siatka co 250 m", "Siatka co 500 m" }, _subGridDivision, win, v => { _subGridDivision = v; _ = RenderMapTilesAsync(); })));
            subGridRow.Children.Add(TacticalConfigPanel.FieldBox("Opisy kratek 100 m:", TacticalConfigPanel.ComboCtrl(new[] { "MGRS + Ćwiartki/Keypad", "Tylko główne linie MGRS", "Brak opisów wewnątrz" }, _gridLabelsMode, win, v => { _gridLabelsMode = v; _ = RenderMapTilesAsync(); })));
            sec2.Children.Add(subGridRow);

            // Suwak 1: Grubość linii siatki
            var thickHeaderDock = new DockPanel { Margin = new Thickness(0, 2, 0, 1) };
            _gridThicknessValueLabel = new TextBlock { Text = "1.0x", Foreground = TacticalConfigPanel.BrushAccent, FontWeight = FontWeights.Bold, FontSize = 10 };
            DockPanel.SetDock(_gridThicknessValueLabel, Dock.Right);
            thickHeaderDock.Children.Add(_gridThicknessValueLabel);
            thickHeaderDock.Children.Add(new TextBlock { Text = "Grubość linii siatki (głównej i pomocniczej):", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 9.5 });
            sec2.Children.Add(thickHeaderDock);

            var thickSlider = new Slider { Minimum = 0.5, Maximum = 3.0, Value = 1.0, Margin = new Thickness(0, 0, 0, 4) };
            thickSlider.ValueChanged += (_, e) => { _gridThicknessMultiplier = Math.Round(e.NewValue, 1); if (_gridThicknessValueLabel != null) _gridThicknessValueLabel.Text = $"{_gridThicknessMultiplier:0.0}x"; _ = RenderMapTilesAsync(); };
            sec2.Children.Add(thickSlider);

            // Suwak 2: Przezroczystość głównych linii geograficznych (MGRS / WGS84 / Sektorowa)
            var mainOpDock = new DockPanel { Margin = new Thickness(0, 2, 0, 1) };
            _mainGridOpacityValueLabel = new TextBlock { Text = "85%", Foreground = TacticalConfigPanel.BrushAccent, FontWeight = FontWeights.Bold, FontSize = 10 };
            DockPanel.SetDock(_mainGridOpacityValueLabel, Dock.Right);
            mainOpDock.Children.Add(_mainGridOpacityValueLabel);
            mainOpDock.Children.Add(new TextBlock { Text = "Widoczność (alfa) głównych linii i opisów (MGRS / WGS84):", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 9.5 });
            sec2.Children.Add(mainOpDock);

            var mainOpSlider = new Slider { Minimum = 0.05, Maximum = 1.0, Value = 0.85, Margin = new Thickness(0, 0, 0, 4) };
            mainOpSlider.ValueChanged += (_, e) =>
            {
                _mainGridOpacity = Math.Round(e.NewValue, 2);
                if (_mainGridOpacityValueLabel != null) _mainGridOpacityValueLabel.Text = $"{(int)Math.Round(_mainGridOpacity * 100)}%";
                _ = RenderMapTilesAsync();
            };
            sec2.Children.Add(mainOpSlider);

            // Suwak 3: Przezroczystość podziałek pomocniczych (Ćwiartki A-D / Keypad 1-9 — zarówno linie jak i cyfry/litery)
            var subOpDock = new DockPanel { Margin = new Thickness(0, 2, 0, 1) };
            _subGridOpacityValueLabel = new TextBlock { Text = "75%", Foreground = TacticalConfigPanel.BrushAccent, FontWeight = FontWeights.Bold, FontSize = 10 };
            DockPanel.SetDock(_subGridOpacityValueLabel, Dock.Right);
            subOpDock.Children.Add(_subGridOpacityValueLabel);
            subOpDock.Children.Add(new TextBlock { Text = "Widoczność (alfa) podziałek Ćwiartek i Keypad (linie + cyfry):", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 9.5 });
            sec2.Children.Add(subOpDock);

            var subOpSlider = new Slider { Minimum = 0.0, Maximum = 1.0, Value = 0.75, Margin = new Thickness(0, 0, 0, 6) };
            subOpSlider.ValueChanged += (_, e) =>
            {
                _subGridOpacity = Math.Round(e.NewValue, 2);
                if (_subGridOpacityValueLabel != null) _subGridOpacityValueLabel.Text = $"{(int)Math.Round(_subGridOpacity * 100)}%";
                _ = RenderMapTilesAsync();
            };
            sec2.Children.Add(subOpSlider);

            var colAndHaloRow = new UniformGrid { Columns = 2 };
            colAndHaloRow.Children.Add(TacticalConfigPanel.FieldBox("Kolor siatki:", TacticalConfigPanel.ComboCtrl(new[] { "Żółty", "Turkusowy", "Biały", "Czerwony", "Czarny" }, _gridColorName, win, v => { _gridColorName = v; _ = RenderMapTilesAsync(); })));
            colAndHaloRow.Children.Add(TacticalConfigPanel.FieldBox("Obrys kontrastowy (Halo):", TacticalConfigPanel.ComboCtrl(new[] { "Pełny czarny obrys", "Półprzezroczysty obrys", "Brak obrysu" }, _gridHaloMode, win, v => { _gridHaloMode = v; _ = RenderMapTilesAsync(); })));
            sec2.Children.Add(colAndHaloRow);
            leftStack.Children.Add(WrapCard(sec2));

            // SEKCJA 3: ZNACZNIKI WEKTOROWE NATO APP-6, KROPKI KOLOROWE I TEREN GRY
            var sec3 = CreateSectionCard("3. ZNACZNIKI WEKTOROWE NATO (APP-6), KOLOR RGB & TEREN GRY");
            void UpdateToolButtonsVisual()
            {
                void StyleTool(Button b, bool active)
                {
                    if (b == null) return;
                    b.Background = active ? TacticalConfigPanel.BrushButtonAccent : TacticalConfigPanel.BrushInputBg;
                    b.Foreground = active ? TacticalConfigPanel.BrushDarkFg : Brushes.White;
                    b.BorderBrush = active ? TacticalConfigPanel.BrushAccent : TacticalConfigPanel.BrushInputBorder;
                }
                StyleTool(_btnToolPan, _activeTool == "PAN"); StyleTool(_btnToolMarker, _activeTool == "MARKER");
                StyleTool(_btnToolDot, _activeTool == "DOT"); StyleTool(_btnToolAo, _activeTool == "AO"); StyleTool(_btnToolRoute, _activeTool == "ROUTE");
            }

            _btnToolPan = MakeActionButton("✋ Przesuwaj Mapę", true, () => { _activeTool = "PAN"; _activeDraftPoly = null; UpdateToolButtonsVisual(); });
            _btnToolPan.Height = 30; _btnToolPan.Margin = new Thickness(0, 0, 0, 5);
            sec3.Children.Add(_btnToolPan);

            var toolsGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 6) };
            _btnToolMarker = MakeActionButton("🚩 Symbol NATO (Wektor)", false, () => { _activeTool = "MARKER"; _activeDraftPoly = null; UpdateToolButtonsVisual(); });
            _btnToolDot = MakeActionButton("⚫ Kropka (Kolor RGB)", false, () => { _activeTool = "DOT"; _activeDraftPoly = null; UpdateToolButtonsVisual(); });
            _btnToolAo = MakeActionButton("⬠ Rysuj Obszar (AO)", false, () => { _activeTool = "AO"; _activeDraftPoly = null; UpdateToolButtonsVisual(); });
            _btnToolRoute = MakeActionButton("〰 Rysuj Trasę/Linię", false, () => { _activeTool = "ROUTE"; _activeDraftPoly = null; UpdateToolButtonsVisual(); });

            _btnToolMarker.Height = 28; _btnToolMarker.Margin = new Thickness(0, 0, 3, 4);
            _btnToolDot.Height = 28; _btnToolDot.Margin = new Thickness(3, 0, 0, 4);
            _btnToolAo.Height = 28; _btnToolAo.Margin = new Thickness(0, 0, 3, 0);
            _btnToolRoute.Height = 28; _btnToolRoute.Margin = new Thickness(3, 0, 0, 0);

            toolsGrid.Children.Add(_btnToolMarker); toolsGrid.Children.Add(_btnToolDot);
            toolsGrid.Children.Add(_btnToolAo); toolsGrid.Children.Add(_btnToolRoute);
            sec3.Children.Add(toolsGrid);
            UpdateToolButtonsVisual();

            var natoCombo = new ComboBox { ItemsSource = NatoSymbols, SelectedIndex = 0, Height = 26, FontSize = 10.5, Margin = new Thickness(0, 0, 0, 4), Tag = "INJECTED" };
            TacticalConfigPanel.ApplyUnifiedComboBoxTheme(natoCombo, win);
            natoCombo.SelectionChanged += (_, __) =>
            {
                if (natoCombo.SelectedItem is NatoSymbolOption opt)
                {
                    _selectedNatoSymbolCode = opt.Code;
                    if (_objectNameInputBox != null) _objectNameInputBox.Text = opt.Code.Replace("NATO_", "");
                }
            };
            sec3.Children.Add(TacticalConfigPanel.FieldBox("Kształt wektorowego znacznika taktycznego (Standard NATO APP-6):", natoCombo));

            var objCfgRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            objCfgRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            objCfgRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(155) });
            _objectNameInputBox = (TextBox)TacticalConfigPanel.TextCtrl("HQ", _ => { });

            var mapColorSwatch = new Border { Width = 13, Height = 13, CornerRadius = new CornerRadius(3), Background = TacticalConfigPanel.BrushFromHex(_selectedObjectColorHex, Color.FromRgb(38, 132, 255)), BorderBrush = Brushes.White, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            var mapColorText = new TextBlock { Text = $"Paleta RGB ({_selectedObjectColorHex})", Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
            var mapColorStack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            mapColorStack.Children.Add(mapColorSwatch); mapColorStack.Children.Add(mapColorText);

            var btnPickMapRgb = new Button { Content = mapColorStack, Height = 25, Background = TacticalConfigPanel.BrushInputBg, BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
            btnPickMapRgb.Click += (_, __) =>
            {
                string picked = TacticalConfigPanel.ShowRgbColorPickerDialog(win, "Kolor znacznika taktycznego NATO", _selectedObjectColorHex);
                if (!string.IsNullOrEmpty(picked))
                {
                    _selectedObjectColorHex = picked;
                    mapColorSwatch.Background = TacticalConfigPanel.BrushFromHex(picked, Color.FromRgb(38, 132, 255));
                    mapColorText.Text = $"Paleta RGB ({picked.ToUpperInvariant()})";
                }
            };

            var fNameBox = TacticalConfigPanel.FieldBox("Etykieta na mapie (np. HQ-1):", _objectNameInputBox);
            var fRgbBtn = TacticalConfigPanel.FieldBox("Kolor znacznika (RGB):", btnPickMapRgb);
            Grid.SetColumn(fNameBox, 0); Grid.SetColumn(fRgbBtn, 1);
            objCfgRow.Children.Add(fNameBox); objCfgRow.Children.Add(fRgbBtn);
            sec3.Children.Add(objCfgRow);

            sec3.Children.Add(new TextBlock { Text = "Naniesione obiekty (kliknij ✖ aby usunąć):", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 10, Margin = new Thickness(0, 0, 0, 3) });
            var objListBorder = new Border { Background = TacticalConfigPanel.BrushInputBg, BorderBrush = TacticalConfigPanel.BrushInputBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), MinHeight = 44, MaxHeight = 105, Padding = new Thickness(6) };
            var objListScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _objectsListStack = new StackPanel();
            objListScroll.Content = _objectsListStack; objListBorder.Child = objListScroll;
            sec3.Children.Add(objListBorder);
            RefreshObjectsListUI();
            leftStack.Children.Add(WrapCard(sec3));

            // SEKCJA 4: ELEMENTY MAPY (PÓŁNOC / PODZIAŁKA / LEGENDA) & EKSPORT
            var sec4 = CreateSectionCard("4. ELEMENTY MAPY (PÓŁNOC / PODZIAŁKA / LEGENDA) & EKSPORT");
            string[] corners = { "Lewy górny róg", "Prawy górny róg", "Lewy dolny róg", "Prawy dolny róg", "Ukryj" };
            var posGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 8) };
            posGrid.Children.Add(TacticalConfigPanel.FieldBox("Znacznik Północy (N):", TacticalConfigPanel.ComboCtrl(corners, _northArrowPos, win, v => { _northArrowPos = v; _ = RenderMapTilesAsync(); })));
            posGrid.Children.Add(TacticalConfigPanel.FieldBox("Podziałka ze skalą na mapie:", TacticalConfigPanel.ComboCtrl(corners, _scaleBarPos, win, v => { _scaleBarPos = v; _ = RenderMapTilesAsync(); })));
            posGrid.Children.Add(TacticalConfigPanel.FieldBox("Legenda i Tabela na mapie:", TacticalConfigPanel.ComboCtrl(corners, _legendPos, win, v => { _legendPos = v; _ = RenderMapTilesAsync(); })));
            posGrid.Children.Add(TacticalConfigPanel.FieldBox("Tryb GRG w pliku .KMZ:", TacticalConfigPanel.ComboCtrl(new[] { "Przezroczysta siatka", "Pełny podkład + siatka", "Tylko podkład" }, _kmzGrgMode, win, v => _kmzGrgMode = v)));
            sec4.Children.Add(posGrid);

            var expRow1 = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 6) };
            var btnGpx = MakeActionButton("⌚ Pobierz .GPX (Garmin/ATAK)", false, async () => await ExportStandaloneFilesAsync(win, "gpx"));
            var btnKmz = MakeActionButton("📦 Pobierz .KMZ (GRG ATAK)", false, async () => await ExportStandaloneFilesAsync(win, "kmz"));
            btnGpx.Height = 32; btnGpx.Margin = new Thickness(0, 0, 3, 0); btnGpx.BorderBrush = TacticalConfigPanel.BrushAccent;
            btnKmz.Height = 32; btnKmz.Margin = new Thickness(3, 0, 0, 0); btnKmz.BorderBrush = TacticalConfigPanel.BrushAccent;
            expRow1.Children.Add(btnGpx); expRow1.Children.Add(btnKmz);
            sec4.Children.Add(expRow1);

            var expRow2 = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 6) };
            var btnPdf = MakeActionButton("🖨️ Drukuj / PDF", false, async () => await ExportTacticalMapPdfAsync(win));
            var btnSaveJson = MakeActionButton("💾 Zapisz .json", false, () => SaveMapProjectJson());
            var btnLoadJson = MakeActionButton("📂 Wczytaj", false, () => LoadMapProjectJsonOrKml());
            btnPdf.Height = 30; btnPdf.Margin = new Thickness(0, 0, 3, 0); btnPdf.BorderBrush = TacticalConfigPanel.BrushAccent;
            btnSaveJson.Height = 30; btnSaveJson.Margin = new Thickness(2, 0, 2, 0);
            btnLoadJson.Height = 30; btnLoadJson.Margin = new Thickness(3, 0, 0, 0);
            expRow2.Children.Add(btnPdf); expRow2.Children.Add(btnSaveJson); expRow2.Children.Add(btnLoadJson);
            sec4.Children.Add(expRow2);

            var btnAddToFtak = MakeActionButton("⚡ Zapisz bezpośrednio do paczki .ftak (source/maps & atak)", true, async () => await ExportMapToFtakSourceAsync(win));
            btnAddToFtak.Height = 32;
            sec4.Children.Add(btnAddToFtak);

            leftStack.Children.Add(WrapCard(sec4));
            leftScroll.Content = leftStack; Grid.SetColumn(leftScroll, 0); bodyGrid.Children.Add(leftScroll);

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
                if (_coordsBadge != null) _coordsBadge.Text = $"WGS84: {hoverLat:0.00000}, {hoverLon:0.00000}  |  Dokładna Skala 1:{_targetScaleDenominator:N0} ({GetExactMetersPerPixel():0.00} m/px)  |  PPM = Dalmierz / Zamknij poligon";
                if (_isDragging && _activeTool == "PAN")
                {
                    double dx = p.X - _dragStartPoint.X, dy = p.Y - _dragStartPoint.Y;
                    double scaledWorldSize = GetScaledTileSizePx() * Math.Pow(2, _zoom);
                    _centerLon = Math.Clamp(_dragStartLon - dx * (360.0 / scaledWorldSize), -180.0, 180.0);
                    _centerLat = Math.Clamp(_dragStartLat + dy * ((360.0 / scaledWorldSize) * Math.Cos(_dragStartLat * Math.PI / 180.0)), -85.0, 85.0);
                }
            };
            _mapCanvas.MouseLeftButtonUp += (_, e) =>
            {
                if (!_isDragging) return;
                _isDragging = false; _mapCanvas.ReleaseMouseCapture();
                Point endPt = e.GetPosition(_mapCanvas);
                if ((endPt - _dragStartPoint).Length < 5.0 && _activeTool != "PAN") HandleMapClickToolAction(endPt);
                _ = RenderMapTilesAsync();
            };
            _mapCanvas.MouseWheel += (_, e) =>
            {
                double factor = e.Delta > 0 ? 0.8 : 1.25;
                int newScale = (int)(Math.Round((_targetScaleDenominator * factor) / 100.0) * 100);
                ApplyScaleDenominator(Math.Clamp(newScale, 500, 100000));
            };
            _mapCanvas.SizeChanged += (_, __) => { _ = RenderMapTilesAsync(); };
            mapOverlayGrid.Children.Add(_mapCanvas);

            var hudBorder = new Border { Background = new SolidColorBrush(Color.FromArgb(220, 8, 19, 22)), BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(12), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
            _coordsBadge = new TextBlock { Text = $"WGS84: {_centerLat:0.00000}, {_centerLon:0.00000}  |  Skala 1:{_targetScaleDenominator:N0}", Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold };
            hudBorder.Child = _coordsBadge; mapOverlayGrid.Children.Add(hudBorder);

            mapBorder.Child = mapOverlayGrid; Grid.SetColumn(mapBorder, 1); bodyGrid.Children.Add(mapBorder);
            Grid.SetRow(bodyGrid, 0); mainGrid.Children.Add(bodyGrid);

            var bottomBar = new Border { Background = TacticalConfigPanel.BrushInputBg, BorderBrush = TacticalConfigPanel.BrushCardBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(16, 0, 16, 0) };
            _statusText = new TextBlock { Text = "Gotowy. Pobieranie WMS z Geoportalu (1 zapytanie + automatyczny fallback) oraz niezależna regulacja przezroczystości siatki głównej i pomocniczej.", Foreground = TacticalConfigPanel.BrushAccent, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            bottomBar.Child = _statusText; Grid.SetRow(bottomBar, 1); mainGrid.Children.Add(bottomBar);

            rootBorder.Child = mainGrid;
            return rootBorder;
        }

        private static Button MakeActionButton(string text, bool isPrimaryAccent, Action onClick)
        {
            var b = new Button
            {
                Content = text,
                Background = isPrimaryAccent ? TacticalConfigPanel.BrushButtonAccent : TacticalConfigPanel.BrushInputBg,
                Foreground = isPrimaryAccent ? TacticalConfigPanel.BrushDarkFg : Brushes.White,
                BorderBrush = isPrimaryAccent ? TacticalConfigPanel.BrushAccent : TacticalConfigPanel.BrushInputBorder,
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.Bold,
                FontSize = 10.5,
                Cursor = Cursors.Hand
            };
            b.Click += (_, __) => onClick();
            return b;
        }

        private static void HandleMapClickToolAction(Point clickPt)
        {
            var (clickLat, clickLon) = ScreenToLatLon(clickPt.X, clickPt.Y, _mapCanvas.ActualWidth, _mapCanvas.ActualHeight);
            string objName = string.IsNullOrWhiteSpace(_objectNameInputBox?.Text) ? $"Punkt-{_mapObjects.Count + 1}" : _objectNameInputBox.Text.Trim();

            if (_activeTool == "MARKER" || _activeTool == "DOT")
            {
                var item = new TacticalMapObjectItem
                {
                    Kind = _activeTool == "MARKER" ? "Marker" : "Dot",
                    SymbolCode = _activeTool == "MARKER" ? _selectedNatoSymbolCode : "DOT",
                    Name = objName,
                    ColorHex = _selectedObjectColorHex
                };
                item.Points.Add((clickLat, clickLon));
                _mapObjects.Add(item);
            }
            else if (_activeTool == "AO" || _activeTool == "ROUTE")
            {
                string kind = _activeTool == "AO" ? "AO" : "Route";
                if (_activeDraftPoly == null || _activeDraftPoly.Kind != kind)
                {
                    _activeDraftPoly = new TacticalMapObjectItem { Kind = kind, SymbolCode = kind, Name = objName, ColorHex = _selectedObjectColorHex };
                    _mapObjects.Add(_activeDraftPoly);
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
                var btnDel = new Button { Content = "✖", Width = 22, Height = 20, Background = TacticalConfigPanel.BrushRedMinus, Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 10, FontWeight = FontWeights.Bold, Cursor = Cursors.Hand };
                DockPanel.SetDock(btnDel, Dock.Right);
                var captured = obj;
                btnDel.Click += (_, __) => { _mapObjects.Remove(captured); if (_activeDraftPoly == captured) _activeDraftPoly = null; RefreshObjectsListUI(); _ = RenderMapTilesAsync(); };
                row.Children.Add(btnDel);

                var sw = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(2), Background = TacticalConfigPanel.BrushFromHex(obj.ColorHex, Color.FromRgb(38, 132, 255)), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
                row.Children.Add(sw);
                row.Children.Add(new TextBlock { Text = $"[{obj.SymbolCode.Replace("NATO_", "")}] {obj.Name} ({obj.ColorHex})", Foreground = Brushes.White, FontSize = 10, VerticalAlignment = VerticalAlignment.Center });
                _objectsListStack.Children.Add(row);
            }
        }

        private static StackPanel CreateSectionCard(string title)
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = title, Foreground = TacticalConfigPanel.BrushAccent, FontWeight = FontWeights.Bold, FontSize = 11.5, Margin = new Thickness(0, 0, 0, 8) });
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
            double tilePx = GetScaledTileSizePx();
            var (cx, cy) = LatLonToTileXY(_centerLat, _centerLon, _zoom);
            return TileXYToLatLon(cx + (screenX - viewW / 2.0) / tilePx, cy + (screenY - viewH / 2.0) / tilePx, _zoom);
        }

        private static Point LatLonToScreen(double lat, double lon, double viewW, double viewH)
        {
            double tilePx = GetScaledTileSizePx();
            var (cx, cy) = LatLonToTileXY(_centerLat, _centerLon, _zoom);
            var (px, py) = LatLonToTileXY(lat, lon, _zoom);
            return new Point(viewW / 2.0 + (px - cx) * tilePx, viewH / 2.0 + (py - cy) * tilePx);
        }

        private static (double X, double Y) LatLonToEpsg3857(double lat, double lon) => (lon * 20037508.34 / 180.0, Math.Log(Math.Tan((90.0 + lat) * Math.PI / 360.0)) / (Math.PI / 180.0) * 20037508.34 / 180.0);

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
            if (layer.IsWmsEpsg3857)
                return layer.UrlTemplate.Replace("{bbox}", GetEpsg3857BBox(tx, ty, zoom)).Replace("{w}", "256").Replace("{h}", "256");
            return layer.UrlTemplate.Replace("{z}", zoom.ToString()).Replace("{x}", tx.ToString()).Replace("{y}", ty.ToString());
        }

        private static string BuildFullViewportWmsUrl(string template, double w, double h)
        {
            var (nwLat, nwLon) = ScreenToLatLon(0, 0, w, h);
            var (seLat, seLon) = ScreenToLatLon(w, h, w, h);
            var (minX, minY) = LatLonToEpsg3857(seLat, nwLon);
            var (maxX, maxY) = LatLonToEpsg3857(nwLat, seLon);
            int reqW = Math.Clamp((int)Math.Round(w), 256, 1650);
            int reqH = Math.Clamp((int)Math.Round(h), 256, 1650);
            string bbox = string.Create(CultureInfo.InvariantCulture, $"{minX:0.00},{minY:0.00},{maxX:0.00},{maxY:0.00}");
            return template.Replace("{bbox}", bbox).Replace("{w}", reqW.ToString()).Replace("{h}", reqH.ToString());
        }

        // Niezawodne pobieranie obrazu z kontrolą sygnatury binarnej JPEG/PNG, limitem połączeń i automatycznym ponawianiem
        private static async Task<byte[]> FetchTileBytesAsync(string url, string fallbackUrl = null)
        {
            if (string.IsNullOrEmpty(url)) return null;
            if (TileCache.TryGetValue(url, out var cached) && IsValidImagePayload(cached)) return cached;

            await TileThrottle.WaitAsync();
            try
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    try
                    {
                        byte[] data = await Http.GetByteArrayAsync(url);
                        if (IsValidImagePayload(data))
                        {
                            TileCache[url] = data;
                            return data;
                        }
                    }
                    catch { }
                    if (attempt == 0) await Task.Delay(120);
                }

                if (!string.IsNullOrEmpty(fallbackUrl))
                {
                    try
                    {
                        byte[] fbData = await Http.GetByteArrayAsync(fallbackUrl);
                        if (IsValidImagePayload(fbData))
                        {
                            TileCache[url] = fbData;
                            return fbData;
                        }
                    }
                    catch { }
                }
            }
            finally
            {
                TileThrottle.Release();
            }
            return null;
        }

        private static async Task RenderMapTilesAsync(bool transparentGrgOnly = false)
        {
            if (_mapCanvas == null || _activeLayer == null || _fullScreenOverlay?.Visibility != Visibility.Visible) return;
            double w = _mapCanvas.ActualWidth > 100 ? _mapCanvas.ActualWidth : 900;
            double h = _mapCanvas.ActualHeight > 100 ? _mapCanvas.ActualHeight : 650;
            double tilePx = GetScaledTileSizePx();

            byte[] singleBaseWmsBytes = null;
            byte[] singleLidarBytes = null;
            List<(double ScreenX, double ScreenY, byte[] Bytes)> fetchedTiles = null;

            if (!transparentGrgOnly)
            {
                Task<byte[]> lidarTask = Task.FromResult<byte[]>(null);
                if (_showLidarSingleWms && _activeLayer.Id != "geoportal_lidar")
                {
                    string lidarTemplate = Layers.First(l => l.Id == "geoportal_lidar").UrlTemplate;
                    lidarTask = FetchTileBytesAsync(BuildFullViewportWmsUrl(lidarTemplate, w, h));
                }

                // Dla podkładów Geoportal WMS: pobieramy 1 pełny arkusz WMS na cały ekran (brak odrzuconych kafelków i dziur!)
                if (_activeLayer.IsWmsEpsg3857)
                {
                    string primaryWmsUrl = BuildFullViewportWmsUrl(_activeLayer.UrlTemplate, w, h);
                    string fallbackWmsUrl = !string.IsNullOrEmpty(_activeLayer.FallbackUrlTemplate)
                        ? BuildFullViewportWmsUrl(_activeLayer.FallbackUrlTemplate, w, h)
                        : null;
                    var baseWmsTask = FetchTileBytesAsync(primaryWmsUrl, fallbackWmsUrl);
                    await Task.WhenAll(baseWmsTask, lidarTask);
                    singleBaseWmsBytes = baseWmsTask.Result;
                    singleLidarBytes = lidarTask.Result;
                }
                else
                {
                    var (centerTileX, centerTileY) = LatLonToTileXY(_centerLat, _centerLon, _zoom);
                    int minTx = (int)Math.Floor(centerTileX - (w / 2.0) / tilePx) - 1, maxTx = (int)Math.Floor(centerTileX + (w / 2.0) / tilePx) + 1;
                    int minTy = (int)Math.Floor(centerTileY - (h / 2.0) / tilePx) - 1, maxTy = (int)Math.Floor(centerTileY + (h / 2.0) / tilePx) + 1;
                    int maxIndex = (1 << _zoom) - 1;

                    var tileJobs = new List<(int Tx, int Ty, double ScreenX, double ScreenY, string Url)>();
                    for (int tx = minTx; tx <= maxTx; tx++)
                        for (int ty = minTy; ty <= maxTy; ty++)
                        {
                            if (ty < 0 || ty > maxIndex) continue;
                            int wrappedTx = ((tx % (maxIndex + 1)) + (maxIndex + 1)) % (maxIndex + 1);
                            tileJobs.Add((wrappedTx, ty, (w / 2.0) + (tx - centerTileX) * tilePx, (h / 2.0) + (ty - centerTileY) * tilePx, BuildTileUrl(_activeLayer, wrappedTx, ty, _zoom)));
                        }

                    var tilesTask = Task.WhenAll(tileJobs.Select(async job => (job.ScreenX, job.ScreenY, await FetchTileBytesAsync(job.Url))));
                    await Task.WhenAll(tilesTask, lidarTask);
                    fetchedTiles = tilesTask.Result.ToList();
                    singleLidarBytes = lidarTask.Result;
                }
            }

            _mapCanvas.Children.Clear();
            _mapCanvas.Background = transparentGrgOnly ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(6, 15, 18));

            if (!transparentGrgOnly)
            {
                if (singleBaseWmsBytes != null)
                    AddCanvasImage(singleBaseWmsBytes, 0, 0, w, h, 1.0);
                else if (fetchedTiles != null)
                    foreach (var t in fetchedTiles)
                        if (t.Item3 != null) AddCanvasImage(t.Item3, t.ScreenX, t.ScreenY, tilePx + 0.6, tilePx + 0.6, 1.0);

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

        private static Brush GetGridColorBrush(byte alpha = 230)
        {
            Color c = _gridColorName switch { "Turkusowy" => Color.FromArgb(alpha, 31, 224, 197), "Biały" => Color.FromArgb(alpha, 255, 255, 255), "Czerwony" => Color.FromArgb(alpha, 235, 64, 52), "Czarny" => Color.FromArgb(alpha, 15, 15, 15), _ => Color.FromArgb(alpha, 250, 208, 44) };
            var b = new SolidColorBrush(c); b.Freeze(); return b;
        }

        // =========================================================================
        // SIATKA WOJSKOWA MGRS / WGS84 Z NIEZALEŻNĄ PRZEZROCZYSTOŚCIĄ LINII GŁÓWNYCH ORAZ POMOCNICZYCH (KEYPAD 1-9 / ĆWIARTKI A-D)
        // =========================================================================
        private static void DrawMilitaryMgrsAndSubGridOverlay(double w, double h)
        {
            if (_coordSystem == "Brak siatki") return;

            byte mainAlpha = (byte)Math.Clamp((int)Math.Round(_mainGridOpacity * 255), 0, 255);
            byte subAlpha = (byte)Math.Clamp((int)Math.Round(_subGridOpacity * 255), 0, 255);

            var mainBrush = GetGridColorBrush(mainAlpha);
            var subBrush = GetGridColorBrush(subAlpha);

            byte mainHaloAlpha = (byte)Math.Clamp((int)Math.Round((_gridHaloMode.StartsWith("Pełny") ? 220 : 130) * _mainGridOpacity), 0, 255);
            byte subHaloAlpha = (byte)Math.Clamp((int)Math.Round((_gridHaloMode.StartsWith("Pełny") ? 185 : 105) * _subGridOpacity), 0, 255);

            var haloBrush = new SolidColorBrush(Color.FromArgb(mainHaloAlpha, 0, 0, 0)); haloBrush.Freeze();
            var subHaloBrush = new SolidColorBrush(Color.FromArgb(subHaloAlpha, 0, 0, 0)); subHaloBrush.Freeze();
            bool useHalo = !_gridHaloMode.StartsWith("Brak");

            double metersPerPixel = GetExactMetersPerPixel();

            if (_coordSystem.StartsWith("Sektorowa") && mainAlpha > 5)
            {
                int cols = 8, rows = 8; double cw = w / cols, ch = h / rows;
                for (int c = 1; c < cols; c++) _mapCanvas.Children.Add(new Line { X1 = c * cw, Y1 = 0, X2 = c * cw, Y2 = h, Stroke = mainBrush, StrokeThickness = 1.4 * _gridThicknessMultiplier });
                for (int r = 1; r < rows; r++) _mapCanvas.Children.Add(new Line { X1 = 0, Y1 = r * ch, X2 = w, Y2 = r * ch, Stroke = mainBrush, StrokeThickness = 1.4 * _gridThicknessMultiplier });
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                        AddHaloLabel($"{(char)('A' + r)}{c + 1}", c * cw + 6, r * ch + 4, mainBrush, 10.5, _mainGridOpacity);
                return;
            }

            int stepMeters = _subGridDivision.Contains("250") ? 250 : (_subGridDivision.Contains("500") ? 500 : 100);
            double stepPx = Math.Clamp(stepMeters / metersPerPixel, 72.0, 340.0);
            double mainThick = Math.Max(0.8, 1.45 * _gridThicknessMultiplier);
            double subThick = Math.Max(0.7, 0.95 * _gridThicknessMultiplier);
            double startX = (w / 2.0) % stepPx, startY = (h / 2.0) % stepPx;

            // 1. Podziałka pomocnicza wewnątrz kratki 100 m: Co 33 m (Keypad 1-9) lub Co 50 m (Ćwiartki A,B,C,D)
            if (_coordSystem.StartsWith("MGRS") && (_subGridDivision.Contains("50 m") || _subGridDivision.Contains("33 m")) && subAlpha > 4)
            {
                bool isQuarters = _subGridDivision.Contains("50 m");
                int div = isQuarters ? 2 : 3;
                double subStep = stepPx / div;

                for (double x = startX - stepPx; x < w; x += stepPx)
                {
                    for (int i = 1; i < div; i++)
                    {
                        double sx = x + i * subStep;
                        if (sx < 0 || sx > w) continue;
                        if (useHalo) _mapCanvas.Children.Add(new Line { X1 = sx, Y1 = 0, X2 = sx, Y2 = h, Stroke = subHaloBrush, StrokeThickness = subThick + 1.4 });
                        _mapCanvas.Children.Add(new Line { X1 = sx, Y1 = 0, X2 = sx, Y2 = h, Stroke = subBrush, StrokeThickness = subThick, StrokeDashArray = new DoubleCollection { 4, 2 } });
                    }
                }

                for (double y = startY - stepPx; y < h; y += stepPx)
                {
                    for (int j = 1; j < div; j++)
                    {
                        double sy = y + j * subStep;
                        if (sy < 0 || sy > h) continue;
                        if (useHalo) _mapCanvas.Children.Add(new Line { X1 = 0, Y1 = sy, X2 = w, Y2 = sy, Stroke = subHaloBrush, StrokeThickness = subThick + 1.4 });
                        _mapCanvas.Children.Add(new Line { X1 = 0, Y1 = sy, X2 = w, Y2 = sy, Stroke = subBrush, StrokeThickness = subThick, StrokeDashArray = new DoubleCollection { 4, 2 } });
                    }
                }

                if (_gridLabelsMode.Contains("Ćwiartki/Keypad") && stepPx >= 68)
                {
                    string[,] keypad3x3 = { { "7", "8", "9" }, { "4", "5", "6" }, { "1", "2", "3" } };
                    string[,] quarters2x2 = { { "A", "B" }, { "C", "D" } };

                    for (double x = startX; x < w - stepPx * 0.6; x += stepPx)
                    {
                        for (double y = startY; y < h - stepPx * 0.6; y += stepPx)
                        {
                            for (int r = 0; r < div; r++)
                            {
                                for (int c = 0; c < div; c++)
                                {
                                    string subTxt = isQuarters ? quarters2x2[r, c] : keypad3x3[r, c];
                                    double cx = x + (c + 0.5) * subStep - 6;
                                    double cy = y + (r + 0.5) * subStep - 7;
                                    AddSubCellBadge(subTxt, cx, cy, subBrush, _subGridOpacity);
                                }
                            }
                        }
                    }
                }
            }

            // 2. Główne linie geograficzne (MGRS / WGS84) oraz opisy główne
            if (mainAlpha > 4)
            {
                bool isWgs84 = _coordSystem.StartsWith("WGS84");
                for (double x = startX; x < w; x += stepPx)
                {
                    if (useHalo) _mapCanvas.Children.Add(new Line { X1 = x, Y1 = 0, X2 = x, Y2 = h, Stroke = haloBrush, StrokeThickness = mainThick + 2.0 });
                    _mapCanvas.Children.Add(new Line { X1 = x, Y1 = 0, X2 = x, Y2 = h, Stroke = mainBrush, StrokeThickness = mainThick });
                    var (_, lon) = ScreenToLatLon(x, 20, w, h);
                    string lblTxt = isWgs84
                        ? $"{lon:0.0000}°E"
                        : $"{(((int)Math.Round(((lon + 180.0) % 6.0) * 111320.0 * Math.Cos(_centerLat * Math.PI / 180.0)) / 100) % 1000):D3}";
                    AddHaloLabel(lblTxt, x + 3, 44, mainBrush, 9.5, _mainGridOpacity);
                }
                for (double y = startY; y < h; y += stepPx)
                {
                    if (useHalo) _mapCanvas.Children.Add(new Line { X1 = 0, Y1 = y, X2 = w, Y2 = y, Stroke = haloBrush, StrokeThickness = mainThick + 2.0 });
                    _mapCanvas.Children.Add(new Line { X1 = 0, Y1 = y, X2 = w, Y2 = y, Stroke = mainBrush, StrokeThickness = mainThick });
                    var (lat, _) = ScreenToLatLon(20, y, w, h);
                    string lblTxt = isWgs84
                        ? $"{lat:0.0000}°N"
                        : $"{(((int)Math.Round(lat * 110574.0) / 100) % 1000):D3}";
                    AddHaloLabel(lblTxt, 8, y + 2, mainBrush, 9.5, _mainGridOpacity);
                }
            }
        }

        private static void AddSubCellBadge(string text, double x, double y, Brush fg, double opacity)
        {
            byte bgAlpha = (byte)Math.Clamp((int)Math.Round(155 * opacity), 0, 220);
            var b = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(bgAlpha, 0, 0, 0)),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(2.5, 0, 2.5, 0),
                IsHitTestVisible = false,
                Child = new TextBlock { Text = text, Foreground = fg, FontSize = 8.5, FontWeight = FontWeights.Bold }
            };
            Canvas.SetLeft(b, x); Canvas.SetTop(b, y); _mapCanvas.Children.Add(b);
        }

        private static void AddHaloLabel(string text, double x, double y, Brush fg, double fontSize, double opacity = 1.0)
        {
            byte bgAlpha = (byte)Math.Clamp((int)Math.Round(185 * opacity), 0, 230);
            var b = new Border { Background = new SolidColorBrush(Color.FromArgb(bgAlpha, 5, 12, 15)), BorderBrush = fg, BorderThickness = new Thickness(0.8), CornerRadius = new CornerRadius(3), Padding = new Thickness(4, 1, 4, 1), IsHitTestVisible = false, Child = new TextBlock { Text = text, Foreground = fg, FontSize = fontSize, FontWeight = FontWeights.Bold } };
            Canvas.SetLeft(b, x); Canvas.SetTop(b, y); _mapCanvas.Children.Add(b);
        }

        private static void DrawNatoVectorSymbolOnCanvas(Point pt, string symbolCode, string name, string colorHex)
        {
            Color c = Color.FromRgb(38, 132, 255);
            try { c = (Color)ColorConverter.ConvertFromString(colorHex); } catch { }

            var strokeBrush = TacticalConfigPanel.CreateFrozenBrush(c.R, c.G, c.B);
            var fillBrush = new SolidColorBrush(Color.FromArgb(175, c.R, c.G, c.B)); fillBrush.Freeze();
            var darkBgBrush = new SolidColorBrush(Color.FromArgb(220, 8, 16, 20)); darkBgBrush.Freeze();

            const double fw = 30.0, fh = 20.0;
            double left = pt.X - fw / 2.0, top = pt.Y - fh / 2.0;

            switch (symbolCode)
            {
                case "NATO_HQ":
                    double hqLeft = pt.X, hqTop = pt.Y - fh - 12;
                    _mapCanvas.Children.Add(new Line { X1 = hqLeft, Y1 = hqTop, X2 = hqLeft, Y2 = pt.Y, Stroke = Brushes.Black, StrokeThickness = 4.2 });
                    _mapCanvas.Children.Add(new Line { X1 = hqLeft, Y1 = hqTop, X2 = hqLeft, Y2 = pt.Y, Stroke = strokeBrush, StrokeThickness = 2.4 });
                    var hqRectHalo = new Rectangle { Width = fw + 2, Height = fh + 2, Stroke = Brushes.Black, StrokeThickness = 3.2, Fill = darkBgBrush };
                    Canvas.SetLeft(hqRectHalo, hqLeft - 1); Canvas.SetTop(hqRectHalo, hqTop - 1); _mapCanvas.Children.Add(hqRectHalo);
                    var hqRect = new Rectangle { Width = fw, Height = fh, Stroke = strokeBrush, StrokeThickness = 2.0, Fill = fillBrush };
                    Canvas.SetLeft(hqRect, hqLeft); Canvas.SetTop(hqRect, hqTop); _mapCanvas.Children.Add(hqRect);
                    var hqTxt = new TextBlock { Text = "HQ", Foreground = Brushes.White, FontSize = 9.5, FontWeight = FontWeights.Bold };
                    Canvas.SetLeft(hqTxt, hqLeft + 6); Canvas.SetTop(hqTxt, hqTop + 3); _mapCanvas.Children.Add(hqTxt);
                    AddHaloLabel(name, hqLeft + fw + 5, hqTop + 1, Brushes.White, 10);
                    return;

                case "NATO_INF":
                    DrawStandardNatoBox(left, top, fw, fh, strokeBrush, fillBrush, darkBgBrush);
                    _mapCanvas.Children.Add(new Line { X1 = left, Y1 = top, X2 = left + fw, Y2 = top + fh, Stroke = Brushes.White, StrokeThickness = 1.8 });
                    _mapCanvas.Children.Add(new Line { X1 = left, Y1 = top + fh, X2 = left + fw, Y2 = top, Stroke = Brushes.White, StrokeThickness = 1.8 });
                    break;

                case "NATO_RECON":
                    DrawStandardNatoBox(left, top, fw, fh, strokeBrush, fillBrush, darkBgBrush);
                    _mapCanvas.Children.Add(new Line { X1 = left, Y1 = top + fh, X2 = left + fw, Y2 = top, Stroke = Brushes.White, StrokeThickness = 2.0 });
                    break;

                case "NATO_MED":
                    DrawStandardNatoBox(left, top, fw, fh, strokeBrush, fillBrush, darkBgBrush);
                    _mapCanvas.Children.Add(new Line { X1 = pt.X, Y1 = top, X2 = pt.X, Y2 = top + fh, Stroke = Brushes.White, StrokeThickness = 2.2 });
                    _mapCanvas.Children.Add(new Line { X1 = left, Y1 = pt.Y, X2 = left + fw, Y2 = pt.Y, Stroke = Brushes.White, StrokeThickness = 2.2 });
                    break;

                case "NATO_COMMS":
                    DrawStandardNatoBox(left, top, fw, fh, strokeBrush, fillBrush, darkBgBrush);
                    var bolt = new Polyline { Stroke = Brushes.White, StrokeThickness = 2.0, Points = new PointCollection { new Point(left + 6, top + fh - 3), new Point(pt.X - 1, top + 4), new Point(pt.X + 2, top + fh - 5), new Point(left + fw - 6, top + 3) } };
                    _mapCanvas.Children.Add(bolt);
                    break;

                case "NATO_SUPPLY":
                    DrawStandardNatoBox(left, top, fw, fh, strokeBrush, fillBrush, darkBgBrush);
                    _mapCanvas.Children.Add(new Line { X1 = left, Y1 = top + fh - 5, X2 = left + fw, Y2 = top + fh - 5, Stroke = Brushes.White, StrokeThickness = 2.0 });
                    break;

                case "NATO_OP":
                    var triHalo = new Polygon { Stroke = Brushes.Black, StrokeThickness = 3.4, Fill = darkBgBrush, Points = new PointCollection { new Point(pt.X, pt.Y - 13), new Point(pt.X - 13, pt.Y + 11), new Point(pt.X + 13, pt.Y + 11) } };
                    _mapCanvas.Children.Add(triHalo);
                    var tri = new Polygon { Stroke = strokeBrush, StrokeThickness = 2.0, Fill = fillBrush, Points = triHalo.Points };
                    _mapCanvas.Children.Add(tri);
                    var opDot = new Ellipse { Width = 5, Height = 5, Fill = Brushes.White };
                    Canvas.SetLeft(opDot, pt.X - 2.5); Canvas.SetTop(opDot, pt.Y); _mapCanvas.Children.Add(opDot);
                    break;

                case "NATO_SNIPER":
                    var snHalo = new Ellipse { Width = 24, Height = 24, Stroke = Brushes.Black, StrokeThickness = 3.2, Fill = darkBgBrush };
                    Canvas.SetLeft(snHalo, pt.X - 12); Canvas.SetTop(snHalo, pt.Y - 12); _mapCanvas.Children.Add(snHalo);
                    var snCirc = new Ellipse { Width = 22, Height = 22, Stroke = strokeBrush, StrokeThickness = 2.0, Fill = fillBrush };
                    Canvas.SetLeft(snCirc, pt.X - 11); Canvas.SetTop(snCirc, pt.Y - 11); _mapCanvas.Children.Add(snCirc);
                    _mapCanvas.Children.Add(new Line { X1 = pt.X - 14, Y1 = pt.Y, X2 = pt.X + 14, Y2 = pt.Y, Stroke = Brushes.White, StrokeThickness = 1.5 });
                    _mapCanvas.Children.Add(new Line { X1 = pt.X, Y1 = pt.Y - 14, X2 = pt.X, Y2 = pt.Y + 14, Stroke = Brushes.White, StrokeThickness = 1.5 });
                    break;

                case "NATO_CP":
                    var shPts = new PointCollection { new Point(pt.X - 12, pt.Y - 11), new Point(pt.X + 12, pt.Y - 11), new Point(pt.X + 12, pt.Y + 2), new Point(pt.X, pt.Y + 13), new Point(pt.X - 12, pt.Y + 2) };
                    _mapCanvas.Children.Add(new Polygon { Stroke = Brushes.Black, StrokeThickness = 3.2, Fill = darkBgBrush, Points = shPts });
                    _mapCanvas.Children.Add(new Polygon { Stroke = strokeBrush, StrokeThickness = 2.0, Fill = fillBrush, Points = shPts });
                    var cpTxt = new TextBlock { Text = "CP", Foreground = Brushes.White, FontSize = 8.5, FontWeight = FontWeights.Bold };
                    Canvas.SetLeft(cpTxt, pt.X - 6); Canvas.SetTop(cpTxt, pt.Y - 7); _mapCanvas.Children.Add(cpTxt);
                    break;

                case "NATO_LZ":
                    var lzHalo = new Ellipse { Width = 26, Height = 26, Stroke = Brushes.Black, StrokeThickness = 3.2, Fill = darkBgBrush };
                    Canvas.SetLeft(lzHalo, pt.X - 13); Canvas.SetTop(lzHalo, pt.Y - 13); _mapCanvas.Children.Add(lzHalo);
                    var lzCirc = new Ellipse { Width = 24, Height = 24, Stroke = strokeBrush, StrokeThickness = 2.0, Fill = fillBrush };
                    Canvas.SetLeft(lzCirc, pt.X - 12); Canvas.SetTop(lzCirc, pt.Y - 12); _mapCanvas.Children.Add(lzCirc);
                    var lzTxt = new TextBlock { Text = "LZ", Foreground = Brushes.White, FontSize = 9, FontWeight = FontWeights.Bold };
                    Canvas.SetLeft(lzTxt, pt.X - 6); Canvas.SetTop(lzTxt, pt.Y - 6); _mapCanvas.Children.Add(lzTxt);
                    break;

                case "NATO_OBJ":
                    var dmPts = new PointCollection { new Point(pt.X, pt.Y - 14), new Point(pt.X + 14, pt.Y), new Point(pt.X, pt.Y + 14), new Point(pt.X - 14, pt.Y) };
                    _mapCanvas.Children.Add(new Polygon { Stroke = Brushes.Black, StrokeThickness = 3.2, Fill = darkBgBrush, Points = dmPts });
                    _mapCanvas.Children.Add(new Polygon { Stroke = strokeBrush, StrokeThickness = 2.0, Fill = fillBrush, Points = dmPts });
                    _mapCanvas.Children.Add(new Line { X1 = pt.X - 7, Y1 = pt.Y, X2 = pt.X + 7, Y2 = pt.Y, Stroke = Brushes.White, StrokeThickness = 1.6 });
                    _mapCanvas.Children.Add(new Line { X1 = pt.X, Y1 = pt.Y - 7, X2 = pt.X, Y2 = pt.Y + 7, Stroke = Brushes.White, StrokeThickness = 1.6 });
                    break;

                case "NATO_HAZARD":
                    var hzPts = new PointCollection { new Point(pt.X, pt.Y - 13), new Point(pt.X - 13, pt.Y + 11), new Point(pt.X + 13, pt.Y + 11) };
                    _mapCanvas.Children.Add(new Polygon { Stroke = Brushes.Black, StrokeThickness = 3.4, Fill = darkBgBrush, Points = hzPts });
                    _mapCanvas.Children.Add(new Polygon { Stroke = strokeBrush, StrokeThickness = 2.0, Fill = fillBrush, Points = hzPts });
                    var hzTxt = new TextBlock { Text = "!", Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.Bold };
                    Canvas.SetLeft(hzTxt, pt.X - 2.5); Canvas.SetTop(hzTxt, pt.Y - 5); _mapCanvas.Children.Add(hzTxt);
                    break;

                default:
                    var dot = new Ellipse { Width = 12, Height = 12, Fill = strokeBrush, Stroke = Brushes.White, StrokeThickness = 2 };
                    Canvas.SetLeft(dot, pt.X - 6); Canvas.SetTop(dot, pt.Y - 6); _mapCanvas.Children.Add(dot);
                    break;
            }

            AddHaloLabel(name, pt.X + 17, pt.Y - 9, strokeBrush, 10);
        }

        private static void DrawStandardNatoBox(double left, double top, double w, double h, Brush strokeBrush, Brush fillBrush, Brush darkBgBrush)
        {
            var halo = new Rectangle { Width = w + 2, Height = h + 2, Stroke = Brushes.Black, StrokeThickness = 3.2, Fill = darkBgBrush };
            Canvas.SetLeft(halo, left - 1); Canvas.SetTop(halo, top - 1); _mapCanvas.Children.Add(halo);
            var rect = new Rectangle { Width = w, Height = h, Stroke = strokeBrush, StrokeThickness = 2.0, Fill = fillBrush };
            Canvas.SetLeft(rect, left); Canvas.SetTop(rect, top); _mapCanvas.Children.Add(rect);
        }

        private static void DrawTacticalObjectsOverlay(double w, double h)
        {
            foreach (var obj in _mapObjects.Where(o => o.Points.Count > 0))
            {
                var strokeBrush = TacticalConfigPanel.BrushFromHex(obj.ColorHex, Color.FromRgb(38, 132, 255));
                Color c = Color.FromRgb(38, 132, 255); try { c = (Color)ColorConverter.ConvertFromString(obj.ColorHex); } catch { }
                var fillBrush = new SolidColorBrush(Color.FromArgb(65, c.R, c.G, c.B)); fillBrush.Freeze();

                if (obj.Kind == "AO" && obj.Points.Count >= 2)
                {
                    var poly = new Polygon { Stroke = strokeBrush, Fill = fillBrush, StrokeThickness = 2.5 };
                    foreach (var pt in obj.Points) poly.Points.Add(LatLonToScreen(pt.Lat, pt.Lon, w, h));
                    _mapCanvas.Children.Add(poly);
                    AddHaloLabel(obj.Name, poly.Points[0].X + 6, poly.Points[0].Y - 10, strokeBrush, 10);
                }
                else if (obj.Kind == "Route" && obj.Points.Count >= 2)
                {
                    var pl = new Polyline { Stroke = strokeBrush, StrokeThickness = 2.8, StrokeDashArray = new DoubleCollection { 4, 2 } };
                    foreach (var pt in obj.Points) pl.Points.Add(LatLonToScreen(pt.Lat, pt.Lon, w, h));
                    _mapCanvas.Children.Add(pl);
                }
                else if (obj.Kind == "Dot")
                {
                    Point pt = LatLonToScreen(obj.Points[0].Lat, obj.Points[0].Lon, w, h);
                    var dot = new Ellipse { Width = 12, Height = 12, Fill = strokeBrush, Stroke = Brushes.White, StrokeThickness = 2 };
                    Canvas.SetLeft(dot, pt.X - 6); Canvas.SetTop(dot, pt.Y - 6); _mapCanvas.Children.Add(dot);
                    AddHaloLabel(obj.Name, pt.X + 9, pt.Y - 8, strokeBrush, 9.5);
                }
                else
                {
                    Point pt = LatLonToScreen(obj.Points[0].Lat, obj.Points[0].Lon, w, h);
                    DrawNatoVectorSymbolOnCanvas(pt, obj.SymbolCode ?? "NATO_HQ", obj.Name, obj.ColorHex);
                }
            }
        }

        private static void PlaceInCorner(FrameworkElement el, string corner, double viewW, double viewH, double elemW, double elemH)
        {
            if (corner == "Ukryj") return;
            Canvas.SetLeft(el, corner.StartsWith("Lewy") ? 16 : Math.Max(20, viewW - elemW - 16));
            Canvas.SetTop(el, corner.Contains("górny") ? 16 : Math.Max(20, viewH - elemH - 16));
            _mapCanvas.Children.Add(el);
        }

        private static void DrawPositionedCornerElements(double w, double h)
        {
            if (_northArrowPos != "Ukryj")
            {
                var compass = new Border { Width = 52, Height = 62, Background = new SolidColorBrush(Color.FromArgb(220, 8, 19, 22)), BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(6), IsHitTestVisible = false };
                var cs = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                cs.Children.Add(new TextBlock { Text = "N", Foreground = TacticalConfigPanel.BrushAccent, FontWeight = FontWeights.Bold, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center });
                cs.Children.Add(new TextBlock { Text = "▲", Foreground = Brushes.White, FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -4, 0, -2) });
                cs.Children.Add(new TextBlock { Text = "MGRS", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center });
                compass.Child = cs; PlaceInCorner(compass, _northArrowPos, w, h, 52, 62);
            }

            if (_scaleBarPos != "Ukryj")
            {
                double metersPerPixel = GetExactMetersPerPixel();
                double chosen = 200;
                foreach (var d in new[] { 20.0, 50.0, 100.0, 200.0, 500.0, 1000.0, 2000.0, 5000.0 }) { double px = d / metersPerPixel; if (px >= 75 && px <= 230) { chosen = d; break; } }
                double barW = Math.Clamp(chosen / metersPerPixel, 60, 240);

                var sbBorder = new Border { Width = barW + 36, Height = 54, Background = new SolidColorBrush(Color.FromArgb(225, 8, 19, 22)), BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(6), IsHitTestVisible = false };
                var cnv = new Canvas();
                var hdr = new TextBlock { Text = $"SKALA 1:{_targetScaleDenominator:N0}", Foreground = TacticalConfigPanel.BrushAccent, FontSize = 10, FontWeight = FontWeights.Bold };
                Canvas.SetLeft(hdr, 12); Canvas.SetTop(hdr, 4); cnv.Children.Add(hdr);
                double segW = barW / 4.0;
                for (int s = 0; s < 4; s++)
                {
                    var r = new Rectangle { Width = segW + 0.5, Height = 7, Fill = (s % 2 == 0) ? TacticalConfigPanel.BrushAccent : Brushes.White, Stroke = Brushes.Black, StrokeThickness = 0.7 };
                    Canvas.SetLeft(r, 12 + s * segW); Canvas.SetTop(r, 23); cnv.Children.Add(r);
                }
                var t0 = new TextBlock { Text = "0", Foreground = Brushes.White, FontSize = 9 }; Canvas.SetLeft(t0, 12); Canvas.SetTop(t0, 32); cnv.Children.Add(t0);
                var tHalf = new TextBlock { Text = $"{chosen / 2:0}m", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 8.5 }; Canvas.SetLeft(tHalf, 12 + barW / 2 - 10); Canvas.SetTop(tHalf, 32); cnv.Children.Add(tHalf);
                var t1 = new TextBlock { Text = $"{chosen:0} m", Foreground = Brushes.White, FontSize = 9, FontWeight = FontWeights.Bold }; Canvas.SetLeft(t1, 12 + barW - 22); Canvas.SetTop(t1, 32); cnv.Children.Add(t1);
                sbBorder.Child = cnv; PlaceInCorner(sbBorder, _scaleBarPos, w, h, barW + 36, 54);
            }

            if (_legendPos != "Ukryj")
            {
                var legBorder = new Border { Width = 245, Height = 68, Background = new SolidColorBrush(Color.FromArgb(225, 8, 19, 22)), BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 10, 6), IsHitTestVisible = false };
                var ls = new StackPanel();
                ls.Children.Add(new TextBlock { Text = $"MAPA TAKTYCZNA ({_sheetFormat.Split(' ')[0]} | 1:{_targetScaleDenominator:N0})", Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 10 });
                ls.Children.Add(new TextBlock { Text = $"Siatka: {_coordSystem.Split(' ')[0]} | {_subGridDivision}", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 9, Margin = new Thickness(0, 2, 0, 4) });
                var fw = new WrapPanel();
                foreach (var f in TacticalConfigPanel.GetConfiguredFactions())
                {
                    var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0) };
                    sp.Children.Add(new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(2), Background = TacticalConfigPanel.BrushFromHex(f.ColorHex, Color.FromRgb(38, 132, 255)), Margin = new Thickness(0, 0, 4, 0) });
                    sp.Children.Add(new TextBlock { Text = f.Name, Foreground = Brushes.White, FontSize = 9, FontWeight = FontWeights.Bold });
                    fw.Children.Add(sp);
                }
                ls.Children.Add(fw); legBorder.Child = ls;
                PlaceInCorner(legBorder, _legendPos, w, h, 245, 68);
            }
        }

        private static void DrawInteractiveDistanceRulerOverlay(double w, double h)
        {
            if (_rulerStart == null || _rulerEnd == null) return;
            Point p1 = LatLonToScreen(_rulerStart.Value.Lat, _rulerStart.Value.Lon, w, h);
            Point p2 = LatLonToScreen(_rulerEnd.Value.Lat, _rulerEnd.Value.Lon, w, h);
            _mapCanvas.Children.Add(new Line { X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y, Stroke = TacticalConfigPanel.BrushAccent, StrokeThickness = 2.2, StrokeDashArray = new DoubleCollection { 4, 2 } });
            double dLat = (_rulerEnd.Value.Lat - _rulerStart.Value.Lat) * Math.PI / 180.0, dLon = (_rulerEnd.Value.Lon - _rulerStart.Value.Lon) * Math.PI / 180.0;
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(_rulerStart.Value.Lat * Math.PI / 180.0) * Math.Cos(_rulerEnd.Value.Lat * Math.PI / 180.0) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            double dist = 6371000.0 * 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
            AddHaloLabel($"📏 {dist:0} m", (p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2 - 12, TacticalConfigPanel.BrushAccent, 10.5);
        }

        private static byte[] CaptureMapCanvasPngBytes()
        {
            int w = (int)Math.Max(800, _mapCanvas.ActualWidth), h = (int)Math.Max(600, _mapCanvas.ActualHeight);
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32); rtb.Render(_mapCanvas);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
            using var ms = new MemoryStream(); enc.Save(ms); return ms.ToArray();
        }

        private static void SaveMapProjectJson()
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
                    ["mainGridOpacity"] = _mainGridOpacity,
                    ["subGridOpacity"] = _subGridOpacity
                };
                var arr = new JsonArray();
                foreach (var o in _mapObjects)
                {
                    var pts = new JsonArray();
                    foreach (var p in o.Points) pts.Add(new JsonObject { ["lat"] = p.Lat, ["lon"] = p.Lon });
                    arr.Add(new JsonObject { ["kind"] = o.Kind, ["symbol"] = o.SymbolCode, ["name"] = o.Name, ["color"] = o.ColorHex, ["points"] = pts });
                }
                root["objects"] = arr;
                File.WriteAllText(dlg.FileName, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
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
                if (dlg.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && JsonNode.Parse(txt) is JsonObject root)
                {
                    if (double.TryParse(root["centerLat"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double la)) _centerLat = la;
                    if (double.TryParse(root["centerLon"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double lo)) _centerLon = lo;
                    if (int.TryParse(root["scale"]?.ToString(), out int sc)) _targetScaleDenominator = sc;
                    _mapObjects.Clear();
                    if (root["objects"] is JsonArray arr)
                        foreach (var node in arr.OfType<JsonObject>())
                        {
                            var item = new TacticalMapObjectItem { Kind = node["kind"]?.ToString() ?? "Marker", SymbolCode = node["symbol"]?.ToString() ?? "NATO_HQ", Name = node["name"]?.ToString() ?? "Punkt", ColorHex = node["color"]?.ToString() ?? "#2684FF" };
                            if (node["points"] is JsonArray pts)
                                foreach (var p in pts.OfType<JsonObject>())
                                    if (double.TryParse(p["lat"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double pla) && double.TryParse(p["lon"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double plo))
                                        item.Points.Add((pla, plo));
                            if (item.Points.Count > 0) _mapObjects.Add(item);
                        }
                }
                else
                {
                    foreach (Match m in Regex.Matches(txt, @"<wpt[^>]*lat=""([0-9\.\-]+)""[^>]*lon=""([0-9\.\-]+)""", RegexOptions.IgnoreCase))
                        if (double.TryParse(m.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double la) && double.TryParse(m.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double lo))
                        { var it = new TacticalMapObjectItem { Kind = "Marker", SymbolCode = _selectedNatoSymbolCode, Name = $"GPX-{_mapObjects.Count + 1}", ColorHex = _selectedObjectColorHex }; it.Points.Add((la, lo)); _mapObjects.Add(it); }
                }
                RefreshObjectsListUI(); ApplyScaleDenominator(_targetScaleDenominator);
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
                    WriteGpxFile(System.IO.Path.Combine(atakDir, "punkty_taktyczne.gpx"));
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
                    else if (format == "gpx") WriteGpxFile(System.IO.Path.Combine(outDir, "Mapa_Taktyczna_Garmin.gpx"));
                });
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
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  <Placemark><name>[{o.SymbolCode.Replace("NATO_", "")}] {o.Name}</name><Point><coordinates>{o.Points[0].Lon:0.000000},{o.Points[0].Lat:0.000000},0</coordinates></Point></Placemark>"));
            sb.AppendLine("</Document></kml>");
            var kmlEntry = zip.CreateEntry("doc.kml", CompressionLevel.Optimal);
            using var sw = new StreamWriter(kmlEntry.Open(), Encoding.UTF8); sw.Write(sb.ToString());
        }

        private static void WriteGpxFile(string gpxPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine(@"<?xml version=""1.0"" encoding=""UTF-8""?><gpx version=""1.1"" creator=""Field TAK Hub Builder 2.4"" xmlns=""http://www.topografix.com/GPX/1/1"">");
            foreach (var o in _mapObjects.Where(x => x.Points.Count > 0))
            {
                if (o.Points.Count == 1)
                    sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  <wpt lat=\"{o.Points[0].Lat:0.000000}\" lon=\"{o.Points[0].Lon:0.000000}\"><name>[{o.SymbolCode.Replace("NATO_", "")}] {o.Name}</name></wpt>"));
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
                    dc.DrawImage(mapRtb, new Rect(24, 24, pageW - 48, pageH - 48));
                }
                var fullRtb = new RenderTargetBitmap(pageW, pageH, 96, 96, PixelFormats.Pbgra32); fullRtb.Render(pageVisual);
                var jpgEnc = new JpegBitmapEncoder { QualityLevel = 92 }; jpgEnc.Frames.Add(BitmapFrame.Create(fullRtb));
                using var jpgMs = new MemoryStream(); jpgEnc.Save(jpgMs);
                byte[] jpegBytes = jpgMs.ToArray();

                await Task.Run(() => WriteSingleImagePdf(pdfPath, jpegBytes, pageW, pageH, landscape));
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
