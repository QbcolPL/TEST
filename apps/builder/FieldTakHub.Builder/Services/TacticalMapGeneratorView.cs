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

    public class SheetFormatDef
    {
        public string Name { get; set; }
        public double WidthMm { get; set; }
        public double HeightMm { get; set; }
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
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Kind { get; set; } = "Marker"; // "Marker", "Dot", "AO", "Sector", "Route"
        public string SymbolCode { get; set; } = "NATO_HQ";
        public string Name { get; set; } = "HQ";
        public string ColorHex { get; set; } = "#2684FF";
        public List<(double Lat, double Lon)> Points { get; set; } = new List<(double Lat, double Lon)>();
    }

    public static class TacticalMapGeneratorView
    {
        private static readonly HttpClient Http = CreateHttpClient();
        private static readonly SemaphoreSlim TileThrottle = new SemaphoreSlim(6, 6);
        private static readonly ConcurrentDictionary<string, byte[]> RawTileCache = new ConcurrentDictionary<string, byte[]>();
        private static readonly ConcurrentDictionary<string, BitmapSource> DecodedImageCache = new ConcurrentDictionary<string, BitmapSource>();

        private static UIElement _originalWindowContent;
        private static Border _fullScreenOverlay, _sheetFrameBorder;
        private static Grid _viewportHostGrid, _mapCompositeGrid;
        private static Canvas _tileCanvas, _vectorCanvas;
        private static Rectangle _dimmerRect;
        private static Image _lidarOverlayImage;
        private static Button _tabBuilderBtn, _tabMapBtn, _btnToolPan, _btnToolMarker, _btnToolDot, _btnToolAo, _btnToolSector, _btnToolRoute;
        private static readonly List<Button> _scalePresetButtons = new List<Button>();
        private static TextBlock _statusText, _coordsBadge, _sheetInfoBadge;
        private static StackPanel _objectsListStack;
        private static CancellationTokenSource _renderCts;

        // Parametry arkusza, pozycji i skali
        private static double _centerLat = 49.9135, _centerLon = 19.0238, _dragStartLat, _dragStartLon;
        private static int _zoom = 16, _targetScaleDenominator = 5000;
        private static SheetFormatDef _selectedSheet = SheetFormats[0];
        private static string _sheetOrientation = "Poziomo";
        private static bool _showLidarSingleWms;

        // Parametry czytelności, siatki i opisów
        private static string _coordSystem = "MGRS / UTM (Siatka wojskowa w metrach)";
        private static string _subGridDivision = "Co 33 m (Keypad 1-9)";
        private static string _gridLabelsMode = "MGRS + Ćwiartki/Keypad";
        private static string _labelBgStyle = "Kaseton (Czarne tło)";
        private static double _gridThicknessMultiplier = 1.0;
        private static double _fontScaleMultiplier = 1.0;
        private static double _mainGridOpacity = 0.85;
        private static double _subGridOpacity = 0.75;
        private static double _mapDimmerOpacity = 0.0;
        private static double _lidarOpacity = 0.45;
        private static string _gridColorName = "Żółty";
        private static string _gridHaloMode = "Pełny czarny obrys";

        // Narzędzia, obiekty i maska poza terenem gry (AO)
        private static string _activeTool = "PAN"; // "PAN", "MARKER", "DOT", "AO", "SECTOR", "ROUTE"
        private static string _selectedObjectColorHex = "#2684FF";
        private static string _selectedNatoSymbolCode = "NATO_HQ";
        private static string _aoOutPattern = "Skośne linie (Hatch ///)";
        private static double _aoOutMaskOpacity = 0.45;
        private static readonly List<TacticalMapObjectItem> _mapObjects = new List<TacticalMapObjectItem>();
        private static TacticalMapObjectItem _activeDraftPoly;
        private static TextBox _objectNameInputBox, _scaleInputBox, _searchPlaceBox;

        // Elementy HUD (Północ, Skala, Legenda) — opcjonalne (Checkbox) + przesuwane (Drag & Drop)
        private static bool _showNorthArrow = true, _showScaleBar = true, _showLegend = true;
        private static string _northArrowPos = "Lewy górny róg", _scaleBarPos = "Lewy dolny róg", _legendPos = "Prawy górny róg";
        private static double? _northNormX, _northNormY, _scaleNormX, _scaleNormY, _legendNormX, _legendNormY;
        private static string _kmzGrgMode = "Przezroczysta siatka";

        // Stan przeciągania (Mapa / Znaczniki / Wierzchołki / HUD)
        private static bool _isPanningMap;
        private static Point _mouseDownPoint;
        private static TacticalMapObjectItem _draggedObject;
        private static int _draggedVertexIndex = -1;
        private static string _draggedHudId; // "NORTH", "SCALE", "LEGEND"
        private static FrameworkElement _draggedHudElement;
        private static Point _hudStartOffset;
        private static (double Lat, double Lon)? _rulerStart, _rulerEnd;
        private static TacticalMapLayerDef _activeLayer;

        public static readonly List<SheetFormatDef> SheetFormats = new List<SheetFormatDef>
        {
            new SheetFormatDef { Name = "A4 — Sztabowy (297 × 210 mm)", WidthMm = 297, HeightMm = 210 },
            new SheetFormatDef { Name = "A3 — Plakat Sztabowy (420 × 297 mm)", WidthMm = 420, HeightMm = 297 },
            new SheetFormatDef { Name = "A5 — Zeszytowy / Mapnik (210 × 148 mm)", WidthMm = 210, HeightMm = 148 },
            new SheetFormatDef { Name = "B5 — Zeszytowy Taktyczny (250 × 176 mm)", WidthMm = 250, HeightMm = 176 },
            new SheetFormatDef { Name = "A6 — Kieszonkowy / Admin Pouch (148 × 105 mm)", WidthMm = 148, HeightMm = 105 },
            new SheetFormatDef { Name = "B6 — Kieszonkowy (176 × 125 mm)", WidthMm = 176, HeightMm = 125 },
            new SheetFormatDef { Name = "Mapnik Naręczny Standard (150 × 100 mm)", WidthMm = 150, HeightMm = 100 },
            new SheetFormatDef { Name = "Mapnik Naręczny Kompakt (130 × 85 mm)", WidthMm = 130, HeightMm = 85 }
        };

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
            var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(16) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
            c.DefaultRequestHeaders.Referrer = new Uri("https://mapy.geoportal.gov.pl/");
            c.DefaultRequestHeaders.Accept.ParseAdd("image/avif,image/webp,image/apng,image/*,*/*;q=0.8");
            return c;
        }

        private static bool IsValidImagePayload(byte[] data)
        {
            if (data == null || data.Length < 128) return false;
            if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return true;
            if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) return true;
            if (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46) return true;
            if (data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46) return true;
            return false;
        }

        // Fizyczne wymiary arkusza (w mm) z uwzględnieniem orientacji Poziomo / Pionowo
        private static (double WidthMm, double HeightMm) GetActiveSheetDimensionsMm()
        {
            var s = _selectedSheet ?? SheetFormats[0];
            double w = Math.Max(s.WidthMm, s.HeightMm);
            double h = Math.Min(s.WidthMm, s.HeightMm);
            return _sheetOrientation == "Pionowo" ? (h, w) : (w, h);
        }

        // Dokładna rozdzielczość terenowa (m/px) dla wybranego formatu arkusza, orientacji i skali 1:X
        private static double GetExactMetersPerPixel()
        {
            var (wMm, _) = GetActiveSheetDimensionsMm();
            double canvasW = (_vectorCanvas != null && _vectorCanvas.ActualWidth > 60) ? _vectorCanvas.ActualWidth : 850.0;
            double realTerrainWidthMeters = (wMm / 1000.0) * _targetScaleDenominator;
            return Math.Max(0.04, realTerrainWidthMeters / canvasW);
        }

        private static double GetScaledTileSizePx()
        {
            double nativeTileMetersPerPx = (156543.03392 * Math.Cos(_centerLat * Math.PI / 180.0)) / Math.Pow(2.0, _zoom);
            double exactMetersPerPx = GetExactMetersPerPixel();
            return Math.Clamp(256.0 * (nativeTileMetersPerPx / exactMetersPerPx), 110.0, 640.0);
        }

        // Dopasowuje proporcje ramki arkusza na ekranie (WYSIWYG dla Poziomo / Pionowo i formatów mapników)
        private static void UpdateSheetViewportSize()
        {
            if (_viewportHostGrid == null || _sheetFrameBorder == null) return;
            double availW = Math.Max(240, _viewportHostGrid.ActualWidth - 28);
            double availH = Math.Max(240, _viewportHostGrid.ActualHeight - 44);

            var (wMm, hMm) = GetActiveSheetDimensionsMm();
            double aspect = wMm / hMm;

            double targetW = availW;
            double targetH = targetW / aspect;
            if (targetH > availH)
            {
                targetH = availH;
                targetW = targetH * aspect;
            }

            _sheetFrameBorder.Width = Math.Round(targetW);
            _sheetFrameBorder.Height = Math.Round(targetH);

            double realWM = (wMm / 1000.0) * _targetScaleDenominator;
            double realHM = (hMm / 1000.0) * _targetScaleDenominator;
            if (_sheetInfoBadge != null)
                _sheetInfoBadge.Text = $"📄 Arkusz: {_selectedSheet.Name.Split('—')[0].Trim()} ({_sheetOrientation}: {wMm:0}×{hMm:0} mm)  •  Skala 1:{_targetScaleDenominator:N0}  •  Wycinek terenu: {realWM:0} m × {realHM:0} m";
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
                    var rightInfo = new TextBlock { Text = "Field TAK Hub 2.4  •  Formaty A3/A4/A5/A6/Naręczne  •  Maska AO & Sektory  •  Drag & Drop", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
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
                UpdateSheetViewportSize();
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

            UpdateSheetViewportSize();
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
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(435) });
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var leftScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = TacticalConfigPanel.BrushCardBg, Padding = new Thickness(10) };
            var leftStack = new StackPanel { Margin = new Thickness(6) };

            // =====================================================================
            // SEKCJA 1: FORMATY (W TYM ZESZYTOWE I NARĘCZNE), ORIENTACJA WYSIWYG, SKALA 1:X & PODKŁAD
            // =====================================================================
            var sec1 = CreateSectionCard("1. FORMAT ARKUSZA (A3–A6 / MAPNIK), ORIENTACJA & SKALA 1:X");
            var searchRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
            _searchPlaceBox = (TextBox)TacticalConfigPanel.TextCtrl("Czechowice-Dziedzice", _ => { });
            var btnFind = MakeActionButton("🔍 Szukaj", true, async () => await SearchLocationNominatimAsync());
            btnFind.Height = 25; btnFind.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(_searchPlaceBox, 0); Grid.SetColumn(btnFind, 1);
            searchRow.Children.Add(_searchPlaceBox); searchRow.Children.Add(btnFind);
            sec1.Children.Add(TacticalConfigPanel.FieldBox("Wyszukaj miejscowość / teren gry:", searchRow));

            var fmtGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            fmtGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.65, GridUnitType.Star) });
            fmtGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var fmtCombo = new ComboBox { ItemsSource = SheetFormats, SelectedIndex = 0, Height = 26, FontSize = 10, Tag = "INJECTED" };
            TacticalConfigPanel.ApplyUnifiedComboBoxTheme(fmtCombo, win);
            fmtCombo.SelectionChanged += (_, __) =>
            {
                if (fmtCombo.SelectedItem is SheetFormatDef sf)
                {
                    _selectedSheet = sf;
                    UpdateSheetViewportSize();
                    _ = RenderMapTilesAsync();
                }
            };
            var fFmt = TacticalConfigPanel.FieldBox("Format arkusza / mapnika:", fmtCombo);
            var fOri = TacticalConfigPanel.FieldBox("Orientacja:", TacticalConfigPanel.ComboCtrl(new[] { "Poziomo", "Pionowo" }, _sheetOrientation, win, v =>
            {
                _sheetOrientation = v;
                UpdateSheetViewportSize();
                _ = RenderMapTilesAsync();
            }));
            Grid.SetColumn(fFmt, 0); Grid.SetColumn(fOri, 1);
            fmtGrid.Children.Add(fFmt); fmtGrid.Children.Add(fOri);
            sec1.Children.Add(fmtGrid);

            sec1.Children.Add(new TextBlock { Text = "Skala mapy (wpisz dowolną 1:X lub wybierz poniżej):", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 10, Margin = new Thickness(0, 2, 0, 3) });
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
            btnScale25k.Height = 24; btnScale25k.Margin = new Thickness(1, 0, 1, 6); btnScale25k.FontSize = 10;
            _scalePresetButtons.Add(btnScale25k);
            sec1.Children.Add(btnScale25k);

            var layerCombo = new ComboBox { ItemsSource = Layers, SelectedIndex = 0, Height = 26, FontSize = 10.5, Margin = new Thickness(0, 0, 0, 6), Tag = "INJECTED" };
            TacticalConfigPanel.ApplyUnifiedComboBoxTheme(layerCombo, win);
            layerCombo.SelectionChanged += (_, __) => { if (layerCombo.SelectedItem is TacticalMapLayerDef def) { _activeLayer = def; _ = RenderMapTilesAsync(); } };
            sec1.Children.Add(TacticalConfigPanel.FieldBox("Podkład bazowy (Geoportal WMS / Google / ESRI / OSM):", layerCombo));

            var lidarBox = new Border { Background = TacticalConfigPanel.BrushInputBg, BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 6, 8, 6) };
            var lidarStack = new StackPanel();
            var chkLidar = new CheckBox { Content = "Nałóż rzeźbę LiDAR ISOK (1 zapytanie WMS - bez dziur)", IsChecked = false, Foreground = TacticalConfigPanel.BrushAccent, FontWeight = FontWeights.SemiBold, FontSize = 10 };
            chkLidar.Checked += (_, __) => { _showLidarSingleWms = true; _ = RenderMapTilesAsync(); };
            chkLidar.Unchecked += (_, __) => { _showLidarSingleWms = false; if (_lidarOverlayImage != null) _lidarOverlayImage.Source = null; };
            lidarStack.Children.Add(chkLidar);
            lidarStack.Children.Add(MakeSliderRow("Przezroczystość rzeźby LiDAR ISOK:", 0.10, 0.95, _lidarOpacity, v => $"{(int)Math.Round(v * 100)}%", v =>
            {
                _lidarOpacity = v;
                if (_lidarOverlayImage != null) _lidarOverlayImage.Opacity = _lidarOpacity;
            }));
            lidarBox.Child = lidarStack;
            sec1.Children.Add(lidarBox);
            leftStack.Children.Add(WrapCard(sec1));

            // =====================================================================
            // SEKCJA 2: SIATKA I ZAAWANSOWANA CZYTELNOŚĆ (OPISY, CZCIONKA, KONTRAST PODKŁADU)
            // =====================================================================
            var sec2 = CreateSectionCard("2. SIATKA MGRS / KEYPAD & KONTROLA CZYTELNOŚCI MAPY");
            sec2.Children.Add(TacticalConfigPanel.FieldBox("System współrzędnych i typ siatki na mapie:", TacticalConfigPanel.ComboCtrl(new[] { "MGRS / UTM (Siatka wojskowa w metrach)", "Siatka Sektorowa (A1, B2, C3...)", "WGS84 (Stopnie geograficzne Lat/Lon)", "Brak siatki" }, _coordSystem, win, v => { _coordSystem = v; RedrawVectorsOnly(); })));

            var subGridRow = new UniformGrid { Columns = 2 };
            subGridRow.Children.Add(TacticalConfigPanel.FieldBox("Podział wewnątrz 100 m:", TacticalConfigPanel.ComboCtrl(new[] { "Co 33 m (Keypad 1-9)", "Co 50 m (Ćwiartki A,B,C,D)", "Brak podziału (Tylko 100 m)", "Siatka co 250 m", "Siatka co 500 m" }, _subGridDivision, win, v => { _subGridDivision = v; RedrawVectorsOnly(); })));
            subGridRow.Children.Add(TacticalConfigPanel.FieldBox("Opisy kratek 100 m:", TacticalConfigPanel.ComboCtrl(new[] { "MGRS + Ćwiartki/Keypad", "Tylko główne linie MGRS", "Brak opisów wewnątrz" }, _gridLabelsMode, win, v => { _gridLabelsMode = v; RedrawVectorsOnly(); })));
            sec2.Children.Add(subGridRow);

            // Wszystkie suwaki czytelności odświeżają warstwę wektorową natychmiastowo (0 ms opóźnienia!)
            sec2.Children.Add(MakeSliderRow("Grubość linii siatki (głównej i pomocniczej):", 0.5, 3.0, _gridThicknessMultiplier, v => $"{v:0.1}x", v => { _gridThicknessMultiplier = Math.Round(v, 1); RedrawVectorsOnly(); }));
            sec2.Children.Add(MakeSliderRow("Rozmiar / grubość czcionki współrzędnych (MGRS i Keypad):", 0.7, 2.0, _fontScaleMultiplier, v => $"{v:0.1}x", v => { _fontScaleMultiplier = Math.Round(v, 1); RedrawVectorsOnly(); }));
            sec2.Children.Add(MakeSliderRow("Widoczność (alfa) głównych linii i opisów (MGRS / WGS84):", 0.05, 1.0, _mainGridOpacity, v => $"{(int)Math.Round(v * 100)}%", v => { _mainGridOpacity = Math.Round(v, 2); RedrawVectorsOnly(); }));
            sec2.Children.Add(MakeSliderRow("Widoczność (alfa) podziałek i cyfr (Keypad 1-9 / Ćwiartki):", 0.0, 1.0, _subGridOpacity, v => $"{(int)Math.Round(v * 100)}%", v => { _subGridOpacity = Math.Round(v, 2); RedrawVectorsOnly(); }));
            sec2.Children.Add(MakeSliderRow("Przyciemnienie podkładu mapy (większy kontrast pod siatkę):", 0.0, 0.65, _mapDimmerOpacity, v => $"{(int)Math.Round(v * 100)}%", v =>
            {
                _mapDimmerOpacity = Math.Round(v, 2);
                if (_dimmerRect != null) _dimmerRect.Opacity = _mapDimmerOpacity;
            }));

            var colAndHaloRow = new UniformGrid { Columns = 3 };
            colAndHaloRow.Children.Add(TacticalConfigPanel.FieldBox("Kolor siatki:", TacticalConfigPanel.ComboCtrl(new[] { "Żółty", "Turkusowy", "Biały", "Czerwony", "Czarny" }, _gridColorName, win, v => { _gridColorName = v; RedrawVectorsOnly(); })));
            colAndHaloRow.Children.Add(TacticalConfigPanel.FieldBox("Obrys linii (Halo):", TacticalConfigPanel.ComboCtrl(new[] { "Pełny czarny obrys", "Półprzezroczysty obrys", "Brak obrysu" }, _gridHaloMode, win, v => { _gridHaloMode = v; RedrawVectorsOnly(); })));
            colAndHaloRow.Children.Add(TacticalConfigPanel.FieldBox("Tło opisów i cyfr:", TacticalConfigPanel.ComboCtrl(new[] { "Kaseton (Czarne tło)", "Tylko obrys Halo", "Czysty tekst" }, _labelBgStyle, win, v => { _labelBgStyle = v; RedrawVectorsOnly(); })));
            sec2.Children.Add(colAndHaloRow);
            leftStack.Children.Add(WrapCard(sec2));

            // =====================================================================
            // SEKCJA 3: ZNACZNIKI DRAG&DROP, TEREN GRY (ODWRÓCONA MASKA POZA AO) & SEKTORY KOLOROWE
            // =====================================================================
            var sec3 = CreateSectionCard("3. ZNACZNIKI (DRAG & DROP), TEREN GRY (MASKA AO) & SEKTORY");
            void UpdateToolButtonsVisual()
            {
                void StyleTool(Button b, bool active)
                {
                    if (b == null) return;
                    b.Background = active ? TacticalConfigPanel.BrushButtonAccent : TacticalConfigPanel.BrushInputBg;
                    b.Foreground = active ? TacticalConfigPanel.BrushDarkFg : Brushes.White;
                    b.BorderBrush = active ? TacticalConfigPanel.BrushAccent : TacticalConfigPanel.BrushInputBorder;
                }
                StyleTool(_btnToolPan, _activeTool == "PAN");
                StyleTool(_btnToolMarker, _activeTool == "MARKER");
                StyleTool(_btnToolDot, _activeTool == "DOT");
                StyleTool(_btnToolAo, _activeTool == "AO");
                StyleTool(_btnToolSector, _activeTool == "SECTOR");
                StyleTool(_btnToolRoute, _activeTool == "ROUTE");
            }

            _btnToolPan = MakeActionButton("✋ Przesuwaj Mapę / Przeciągaj Znaczniki i Legendę (Drag & Drop)", true, () => { _activeTool = "PAN"; _activeDraftPoly = null; UpdateToolButtonsVisual(); });
            _btnToolPan.Height = 30; _btnToolPan.Margin = new Thickness(0, 0, 0, 5);
            sec3.Children.Add(_btnToolPan);

            var toolsGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 6) };
            _btnToolMarker = MakeActionButton("🚩 Symbol NATO (Wektor)", false, () => { _activeTool = "MARKER"; _activeDraftPoly = null; UpdateToolButtonsVisual(); });
            _btnToolDot = MakeActionButton("⚫ Kropka (Kolor RGB)", false, () => { _activeTool = "DOT"; _activeDraftPoly = null; UpdateToolButtonsVisual(); });
            _btnToolAo = MakeActionButton("🛡️ Teren Gry (Maska poza AO)", false, () => { _activeTool = "AO"; _activeDraftPoly = null; if (_objectNameInputBox != null) _objectNameInputBox.Text = "Granica Terenu Gry"; UpdateToolButtonsVisual(); });
            _btnToolSector = MakeActionButton("⬠ Sektor / Strefa (Wypełniony)", false, () => { _activeTool = "SECTOR"; _activeDraftPoly = null; if (_objectNameInputBox != null) _objectNameInputBox.Text = $"Sektor-{_mapObjects.Count + 1}"; UpdateToolButtonsVisual(); });

            _btnToolMarker.Height = 28; _btnToolMarker.Margin = new Thickness(0, 0, 3, 4);
            _btnToolDot.Height = 28; _btnToolDot.Margin = new Thickness(3, 0, 0, 4);
            _btnToolAo.Height = 28; _btnToolAo.Margin = new Thickness(0, 0, 3, 4);
            _btnToolSector.Height = 28; _btnToolSector.Margin = new Thickness(3, 0, 0, 4);

            toolsGrid.Children.Add(_btnToolMarker); toolsGrid.Children.Add(_btnToolDot);
            toolsGrid.Children.Add(_btnToolAo); toolsGrid.Children.Add(_btnToolSector);
            sec3.Children.Add(toolsGrid);

            _btnToolRoute = MakeActionButton("〰 Rysuj Trasę / Linię Rozgraniczenia (PPM = zakończ rysowanie)", false, () => { _activeTool = "ROUTE"; _activeDraftPoly = null; if (_objectNameInputBox != null) _objectNameInputBox.Text = $"Trasa-{_mapObjects.Count + 1}"; UpdateToolButtonsVisual(); });
            _btnToolRoute.Height = 26; _btnToolRoute.Margin = new Thickness(0, 0, 0, 6);
            sec3.Children.Add(_btnToolRoute);
            UpdateToolButtonsVisual();

            // Ustawienia maski poza Terenem Gry (AO)
            var aoMaskRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            aoMaskRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
            aoMaskRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var fAoPat = TacticalConfigPanel.FieldBox("Oznaczenie strefy POZA Terenem Gry (AO):", TacticalConfigPanel.ComboCtrl(new[]
            {
                "Skośne linie (Hatch ///)",
                "Symbole X (Strefa No-Go)",
                "Jednolite tło RGB poza AO",
                "Tylko linia granicy AO"
            }, _aoOutPattern, win, v => { _aoOutPattern = v; RedrawVectorsOnly(); }));
            var fAoOp = MakeSliderRow("Krycie maski poza AO:", 0.10, 0.85, _aoOutMaskOpacity, v => $"{(int)Math.Round(v * 100)}%", v => { _aoOutMaskOpacity = Math.Round(v, 2); RedrawVectorsOnly(); });
            Grid.SetColumn(fAoPat, 0); Grid.SetColumn(fAoOp, 1);
            aoMaskRow.Children.Add(fAoPat); aoMaskRow.Children.Add(fAoOp);
            sec3.Children.Add(aoMaskRow);

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
                string picked = TacticalConfigPanel.ShowRgbColorPickerDialog(win, "Kolor obiektu / sektora / maski AO", _selectedObjectColorHex);
                if (!string.IsNullOrEmpty(picked))
                {
                    _selectedObjectColorHex = picked;
                    mapColorSwatch.Background = TacticalConfigPanel.BrushFromHex(picked, Color.FromRgb(38, 132, 255));
                    mapColorText.Text = $"Paleta RGB ({picked.ToUpperInvariant()})";
                }
            };

            var fNameBox = TacticalConfigPanel.FieldBox("Domyślna nazwa nowego obiektu:", _objectNameInputBox);
            var fRgbBtn = TacticalConfigPanel.FieldBox("Kolor obiektu / maski (RGB):", btnPickMapRgb);
            Grid.SetColumn(fNameBox, 0); Grid.SetColumn(fRgbBtn, 1);
            objCfgRow.Children.Add(fNameBox); objCfgRow.Children.Add(fRgbBtn);
            sec3.Children.Add(objCfgRow);

            sec3.Children.Add(new TextBlock { Text = "Naniesione obiekty (edytuj nazwę na żywo, kliknij kolor lub ✖):", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 10, Margin = new Thickness(0, 0, 0, 3) });
            var objListBorder = new Border { Background = TacticalConfigPanel.BrushInputBg, BorderBrush = TacticalConfigPanel.BrushInputBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), MinHeight = 52, MaxHeight = 135, Padding = new Thickness(6) };
            var objListScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _objectsListStack = new StackPanel();
            objListScroll.Content = _objectsListStack; objListBorder.Child = objListScroll;
            sec3.Children.Add(objListBorder);
            RefreshObjectsListUI(win);
            leftStack.Children.Add(WrapCard(sec3));

            // =====================================================================
            // SEKCJA 4: CHECKBOXY PÓŁNOCY / SKALI / LEGENDY (PRZESUWANE MYSZĄ!) & EKSPORT
            // =====================================================================
            var sec4 = CreateSectionCard("4. ELEMENTY MAPY (CHECKBOX + DRAG & DROP NA MAPIE) & EKSPORT");
            sec4.Children.Add(new TextBlock { Text = "Włącz wybrane elementy i przeciągnij je myszą w dowolne miejsce na mapie:", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 9.5, Margin = new Thickness(0, 0, 0, 4) });

            var hudChkGrid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 6) };
            CheckBox MakeHudChk(string label, bool init, Action<bool> onChg)
            {
                var c = new CheckBox { Content = label, IsChecked = init, Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.SemiBold };
                c.Checked += (_, __) => { onChg(true); RedrawVectorsOnly(); };
                c.Unchecked += (_, __) => { onChg(false); RedrawVectorsOnly(); };
                return c;
            }
            hudChkGrid.Children.Add(MakeHudChk("🧭 Północ (N)", _showNorthArrow, v => _showNorthArrow = v));
            hudChkGrid.Children.Add(MakeHudChk("📏 Podziałka", _showScaleBar, v => _showScaleBar = v));
            hudChkGrid.Children.Add(MakeHudChk("📋 Legenda", _showLegend, v => _showLegend = v));
            sec4.Children.Add(hudChkGrid);

            string[] corners = { "Lewy górny róg", "Prawy górny róg", "Lewy dolny róg", "Prawy dolny róg" };
            var posGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 8) };
            posGrid.Children.Add(TacticalConfigPanel.FieldBox("Pozycja Północy (lub przeciągnij):", TacticalConfigPanel.ComboCtrl(corners, _northArrowPos, win, v => { _northArrowPos = v; _northNormX = null; _northNormY = null; RedrawVectorsOnly(); })));
            posGrid.Children.Add(TacticalConfigPanel.FieldBox("Pozycja Podziałki (lub przeciągnij):", TacticalConfigPanel.ComboCtrl(corners, _scaleBarPos, win, v => { _scaleBarPos = v; _scaleNormX = null; _scaleNormY = null; RedrawVectorsOnly(); })));
            posGrid.Children.Add(TacticalConfigPanel.FieldBox("Pozycja Legendy (lub przeciągnij):", TacticalConfigPanel.ComboCtrl(corners, _legendPos, win, v => { _legendPos = v; _legendNormX = null; _legendNormY = null; RedrawVectorsOnly(); })));
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
            var btnLoadJson = MakeActionButton("📂 Wczytaj", false, () => LoadMapProjectJsonOrKml(win));
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

            // =====================================================================
            // PRAWA STRONA: KONTENER ARKUSZA WYSIWYG Z DWOMA WARSTWAMI PŁÓTNA
            // =====================================================================
            _viewportHostGrid = new Grid
            {
                Background = new SolidColorBrush(Color.FromRgb(4, 10, 12)),
                ClipToBounds = true
            };

            _sheetFrameBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(6, 15, 18)),
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(1.5),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                ClipToBounds = true
            };

            _mapCompositeGrid = new Grid { ClipToBounds = true, Background = new SolidColorBrush(Color.FromRgb(6, 15, 18)) };
            _tileCanvas = new Canvas { ClipToBounds = true, IsHitTestVisible = false };
            _dimmerRect = new Rectangle { Fill = Brushes.Black, Opacity = _mapDimmerOpacity, IsHitTestVisible = false };
            _lidarOverlayImage = new Image { Stretch = Stretch.Fill, Opacity = _lidarOpacity, IsHitTestVisible = false };
            _vectorCanvas = new Canvas { Background = Brushes.Transparent, ClipToBounds = true, Cursor = Cursors.Cross };

            _mapCompositeGrid.Children.Add(_tileCanvas);
            _mapCompositeGrid.Children.Add(_lidarOverlayImage);
            _mapCompositeGrid.Children.Add(_dimmerRect);
            _mapCompositeGrid.Children.Add(_vectorCanvas);
            _sheetFrameBorder.Child = _mapCompositeGrid;
            _viewportHostGrid.Children.Add(_sheetFrameBorder);

            // Górny pasek informacyjny o arkuszu i współrzędnych
            var topBadgesStack = new StackPanel { VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0), IsHitTestVisible = false };
            var sheetBadgeBorder = new Border { Background = new SolidColorBrush(Color.FromArgb(225, 8, 19, 22)), BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) };
            _sheetInfoBadge = new TextBlock { Text = "Arkusz A4 Poziomo", Foreground = TacticalConfigPanel.BrushAccent, FontSize = 10.5, FontWeight = FontWeights.Bold };
            sheetBadgeBorder.Child = _sheetInfoBadge;
            topBadgesStack.Children.Add(sheetBadgeBorder);

            var hudBorder = new Border { Background = new SolidColorBrush(Color.FromArgb(215, 8, 19, 22)), BorderBrush = TacticalConfigPanel.BrushTileBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Center };
            _coordsBadge = new TextBlock { Text = $"WGS84: {_centerLat:0.00000}, {_centerLon:0.00000}  |  Przeciągnij znaczniki, Północ lub Legendę myszą", Foreground = Brushes.White, FontSize = 10 };
            hudBorder.Child = _coordsBadge;
            topBadgesStack.Children.Add(hudBorder);
            _viewportHostGrid.Children.Add(topBadgesStack);

            // Obsługa zdarzeń myszy (Drag & Drop dla HUD, Znaczników, Wierzchołków oraz Przesuwanie Mapy)
            _vectorCanvas.MouseLeftButtonDown += (_, e) =>
            {
                if (_draggedHudId != null || _draggedObject != null) return;
                _isPanningMap = true;
                _mouseDownPoint = e.GetPosition(_vectorCanvas);
                _dragStartLat = _centerLat;
                _dragStartLon = _centerLon;
                _vectorCanvas.CaptureMouse();
            };

            _vectorCanvas.MouseRightButtonDown += (_, e) =>
            {
                if (_activeDraftPoly != null)
                {
                    _activeDraftPoly = null;
                    RefreshObjectsListUI(win);
                    RedrawVectorsOnly();
                    return;
                }
                Point p = e.GetPosition(_vectorCanvas);
                var pt = ScreenToLatLon(p.X, p.Y, _vectorCanvas.ActualWidth, _vectorCanvas.ActualHeight);
                if (_rulerStart == null || (_rulerStart != null && _rulerEnd != null)) { _rulerStart = pt; _rulerEnd = null; }
                else _rulerEnd = pt;
                RedrawVectorsOnly();
            };

            _vectorCanvas.MouseMove += (_, e) =>
            {
                double w = Math.Max(100, _vectorCanvas.ActualWidth);
                double h = Math.Max(100, _vectorCanvas.ActualHeight);
                Point p = e.GetPosition(_vectorCanvas);
                var (hoverLat, hoverLon) = ScreenToLatLon(p.X, p.Y, w, h);
                if (_coordsBadge != null)
                    _coordsBadge.Text = $"WGS84: {hoverLat:0.00000}, {hoverLon:0.00000}  |  Skala 1:{_targetScaleDenominator:N0} ({GetExactMetersPerPixel():0.00} m/px)  |  LPM = Przesuń element / mapę  |  PPM = Dalmierz / Zamknij poligon";

                // 1. Przeciąganie elementu HUD (Północ / Podziałka / Legenda)
                if (_draggedHudId != null && _draggedHudElement != null)
                {
                    double nx = Math.Clamp(p.X - _hudStartOffset.X, 4, Math.Max(10, w - _draggedHudElement.Width - 4));
                    double ny = Math.Clamp(p.Y - _hudStartOffset.Y, 4, Math.Max(10, h - _draggedHudElement.Height - 4));
                    Canvas.SetLeft(_draggedHudElement, nx);
                    Canvas.SetTop(_draggedHudElement, ny);
                    if (_draggedHudId == "NORTH") { _northNormX = nx / w; _northNormY = ny / h; }
                    else if (_draggedHudId == "SCALE") { _scaleNormX = nx / w; _scaleNormY = ny / h; }
                    else if (_draggedHudId == "LEGEND") { _legendNormX = nx / w; _legendNormY = ny / h; }
                    return;
                }

                // 2. Przeciąganie znacznika NATO / Kropki lub wierzchołka poligonu AO / Sektora
                if (_draggedObject != null && _draggedVertexIndex >= 0 && _draggedVertexIndex < _draggedObject.Points.Count)
                {
                    _draggedObject.Points[_draggedVertexIndex] = (hoverLat, hoverLon);
                    RedrawVectorsOnly();
                    return;
                }

                // 3. Przesuwanie całej mapy w trybie PAN
                if (_isPanningMap && _activeTool == "PAN")
                {
                    double dx = p.X - _mouseDownPoint.X, dy = p.Y - _mouseDownPoint.Y;
                    double scaledWorldSize = GetScaledTileSizePx() * Math.Pow(2, _zoom);
                    _centerLon = Math.Clamp(_dragStartLon - dx * (360.0 / scaledWorldSize), -180.0, 180.0);
                    _centerLat = Math.Clamp(_dragStartLat + dy * ((360.0 / scaledWorldSize) * Math.Cos(_dragStartLat * Math.PI / 180.0)), -85.0, 85.0);
                    _tileCanvas.RenderTransform = new TranslateTransform(dx, dy);
                    RedrawVectorsOnly();
                }
            };

            _vectorCanvas.MouseLeftButtonUp += (_, e) =>
            {
                if (_draggedHudId != null)
                {
                    _draggedHudId = null;
                    _draggedHudElement = null;
                    _vectorCanvas.ReleaseMouseCapture();
                    return;
                }
                if (_draggedObject != null)
                {
                    _draggedObject = null;
                    _draggedVertexIndex = -1;
                    _vectorCanvas.ReleaseMouseCapture();
                    RedrawVectorsOnly();
                    return;
                }
                if (!_isPanningMap) return;
                _isPanningMap = false;
                _vectorCanvas.ReleaseMouseCapture();
                _tileCanvas.RenderTransform = Transform.Identity;

                Point endPt = e.GetPosition(_vectorCanvas);
                if ((endPt - _mouseDownPoint).Length < 5.0 && _activeTool != "PAN")
                {
                    HandleMapClickToolAction(win, endPt);
                    RedrawVectorsOnly();
                }
                else
                {
                    _ = RenderMapTilesAsync();
                }
            };

            _vectorCanvas.MouseWheel += (_, e) =>
            {
                double factor = e.Delta > 0 ? 0.8 : 1.25;
                int newScale = (int)(Math.Round((_targetScaleDenominator * factor) / 100.0) * 100);
                ApplyScaleDenominator(Math.Clamp(newScale, 500, 100000));
            };

            _viewportHostGrid.SizeChanged += (_, __) =>
            {
                UpdateSheetViewportSize();
                _ = RenderMapTilesAsync();
            };

            Grid.SetColumn(_viewportHostGrid, 1);
            bodyGrid.Children.Add(_viewportHostGrid);
            Grid.SetRow(bodyGrid, 0);
            mainGrid.Children.Add(bodyGrid);

            var bottomBar = new Border { Background = TacticalConfigPanel.BrushInputBg, BorderBrush = TacticalConfigPanel.BrushCardBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(16, 0, 16, 0) };
            _statusText = new TextBlock { Text = "Gotowy. Orientacja Poziomo/Pionowo na żywo, formaty A3–A6 i Mapniki Naręczne, odwrócona maska Terenu Gry (AO), Sektory RGB oraz Drag & Drop.", Foreground = TacticalConfigPanel.BrushAccent, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            bottomBar.Child = _statusText; Grid.SetRow(bottomBar, 1); mainGrid.Children.Add(bottomBar);

            rootBorder.Child = mainGrid;
            return rootBorder;
        }

        private static UIElement MakeSliderRow(string label, double min, double max, double initVal, Func<double, string> fmt, Action<double> onVal)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 2, 0, 4) };
            var dock = new DockPanel { Margin = new Thickness(0, 0, 0, 1) };
            var valTxt = new TextBlock { Text = fmt(initVal), Foreground = TacticalConfigPanel.BrushAccent, FontWeight = FontWeights.Bold, FontSize = 10 };
            DockPanel.SetDock(valTxt, Dock.Right);
            dock.Children.Add(valTxt);
            dock.Children.Add(new TextBlock { Text = label, Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 9.5, TextTrimming = TextTrimming.CharacterEllipsis });
            sp.Children.Add(dock);

            var sl = new Slider { Minimum = min, Maximum = max, Value = initVal };
            sl.ValueChanged += (_, e) => { valTxt.Text = fmt(e.NewValue); onVal(e.NewValue); };
            sp.Children.Add(sl);
            return sp;
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

        private static void HandleMapClickToolAction(Window win, Point clickPt)
        {
            var (clickLat, clickLon) = ScreenToLatLon(clickPt.X, clickPt.Y, _vectorCanvas.ActualWidth, _vectorCanvas.ActualHeight);
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
            else if (_activeTool == "AO" || _activeTool == "SECTOR" || _activeTool == "ROUTE")
            {
                string kind = _activeTool == "AO" ? "AO" : (_activeTool == "SECTOR" ? "Sector" : "Route");
                if (_activeDraftPoly == null || _activeDraftPoly.Kind != kind)
                {
                    // Jeśli rysujemy nową główną granicę Terenu Gry (AO), możemy zastąpić poprzednią lub dodać kolejną
                    _activeDraftPoly = new TacticalMapObjectItem
                    {
                        Kind = kind,
                        SymbolCode = kind,
                        Name = kind == "AO" ? "Granica Terenu Gry (AO)" : objName,
                        ColorHex = _selectedObjectColorHex
                    };
                    _mapObjects.Add(_activeDraftPoly);
                }
                _activeDraftPoly.Points.Add((clickLat, clickLon));
            }
            RefreshObjectsListUI(win);
        }

        // Lista obiektów z edycją nazwy na żywo (TextBox) oraz zmianą koloru RGB
        private static void RefreshObjectsListUI(Window win)
        {
            if (_objectsListStack == null) return;
            _objectsListStack.Children.Clear();
            if (_mapObjects.Count == 0)
            {
                _objectsListStack.Children.Add(new TextBlock { Text = "Brak naniesionych obiektów", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 8) });
                return;
            }

            foreach (var obj in _mapObjects.ToList())
            {
                var captured = obj;
                var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });

                var btnCol = new Button
                {
                    Width = 18,
                    Height = 18,
                    Background = TacticalConfigPanel.BrushFromHex(captured.ColorHex, Color.FromRgb(38, 132, 255)),
                    BorderBrush = Brushes.White,
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    ToolTip = "Zmień kolor tego obiektu (Paleta RGB)"
                };
                btnCol.Click += (_, __) =>
                {
                    string picked = TacticalConfigPanel.ShowRgbColorPickerDialog(win, $"Kolor — {captured.Name}", captured.ColorHex);
                    if (!string.IsNullOrEmpty(picked))
                    {
                        captured.ColorHex = picked;
                        btnCol.Background = TacticalConfigPanel.BrushFromHex(picked, Color.FromRgb(38, 132, 255));
                        RedrawVectorsOnly();
                    }
                };
                Grid.SetColumn(btnCol, 0);
                row.Children.Add(btnCol);

                string badgeTxt = captured.Kind == "AO" ? "[AO]" : (captured.Kind == "Sector" ? "[SEKTOR]" : $"[{captured.SymbolCode.Replace("NATO_", "")}]");
                var kindLbl = new TextBlock { Text = badgeTxt, Foreground = TacticalConfigPanel.BrushAccent, FontSize = 9, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 4, 0) };
                Grid.SetColumn(kindLbl, 1);
                row.Children.Add(kindLbl);

                var editNameBox = new TextBox
                {
                    Text = captured.Name,
                    Height = 22,
                    FontSize = 10,
                    Padding = new Thickness(4, 0, 4, 0),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Background = TacticalConfigPanel.BrushItemBg,
                    Foreground = Brushes.White,
                    BorderBrush = TacticalConfigPanel.BrushInputBorder,
                    BorderThickness = new Thickness(1)
                };
                editNameBox.TextChanged += (_, __) =>
                {
                    captured.Name = editNameBox.Text;
                    RedrawVectorsOnly();
                };
                Grid.SetColumn(editNameBox, 2);
                row.Children.Add(editNameBox);

                var btnDel = new Button { Content = "✖", Width = 20, Height = 20, Margin = new Thickness(4, 0, 0, 0), Background = TacticalConfigPanel.BrushRedMinus, Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 9.5, FontWeight = FontWeights.Bold, Cursor = Cursors.Hand };
                btnDel.Click += (_, __) =>
                {
                    _mapObjects.Remove(captured);
                    if (_activeDraftPoly == captured) _activeDraftPoly = null;
                    RefreshObjectsListUI(win);
                    RedrawVectorsOnly();
                };
                Grid.SetColumn(btnDel, 3);
                row.Children.Add(btnDel);

                _objectsListStack.Children.Add(row);
            }
        }

        private static StackPanel CreateSectionCard(string title)
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = title, Foreground = TacticalConfigPanel.BrushAccent, FontWeight = FontWeights.Bold, FontSize = 11, Margin = new Thickness(0, 0, 0, 6) });
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

        // Pobiera i dekoduje obraz tylko raz, przechowując zamrożony BitmapSource w pamięci podręcznej
        private static async Task<BitmapSource> FetchDecodedImageAsync(string url, string fallbackUrl, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(url)) return null;
            if (DecodedImageCache.TryGetValue(url, out var cachedBmp)) return cachedBmp;

            byte[] validBytes = null;
            if (RawTileCache.TryGetValue(url, out var raw) && IsValidImagePayload(raw))
            {
                validBytes = raw;
            }
            else
            {
                await TileThrottle.WaitAsync(ct);
                try
                {
                    for (int attempt = 0; attempt < 2 && !ct.IsCancellationRequested; attempt++)
                    {
                        try
                        {
                            byte[] data = await Http.GetByteArrayAsync(url, ct);
                            if (IsValidImagePayload(data)) { validBytes = data; RawTileCache[url] = data; break; }
                        }
                        catch { }
                        if (attempt == 0) await Task.Delay(90, ct);
                    }

                    if (validBytes == null && !string.IsNullOrEmpty(fallbackUrl) && !ct.IsCancellationRequested)
                    {
                        try
                        {
                            byte[] fbData = await Http.GetByteArrayAsync(fallbackUrl, ct);
                            if (IsValidImagePayload(fbData)) { validBytes = fbData; RawTileCache[url] = fbData; }
                        }
                        catch { }
                    }
                }
                finally { TileThrottle.Release(); }
            }

            if (validBytes == null || ct.IsCancellationRequested) return null;

            try
            {
                // Kontrola rozmiaru pamięci podręcznej RAM
                if (DecodedImageCache.Count > 260) DecodedImageCache.Clear();
                if (RawTileCache.Count > 400) RawTileCache.Clear();

                var bmp = new BitmapImage();
                using (var mem = new MemoryStream(validBytes))
                {
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = mem;
                    bmp.EndInit();
                    bmp.Freeze();
                }
                DecodedImageCache[url] = bmp;
                return bmp;
            }
            catch { return null; }
        }

        // Błyskawiczne odświeżenie samej warstwy wektorowej (0 ms — bez pobierania i dekodowania kafelków!)
        private static void RedrawVectorsOnly()
        {
            if (_vectorCanvas == null || _fullScreenOverlay?.Visibility != Visibility.Visible) return;
            double w = _vectorCanvas.ActualWidth > 60 ? _vectorCanvas.ActualWidth : (_sheetFrameBorder?.Width ?? 800);
            double h = _vectorCanvas.ActualHeight > 60 ? _vectorCanvas.ActualHeight : (_sheetFrameBorder?.Height ?? 600);
            if (double.IsNaN(w) || w <= 60) w = 800;
            if (double.IsNaN(h) || h <= 60) h = 600;

            _vectorCanvas.Children.Clear();
            DrawInvertedAoMaskAndSectorsOverlay(w, h);
            DrawMilitaryMgrsAndSubGridOverlay(w, h);
            DrawTacticalMarkersAndRoutesOverlay(w, h);
            DrawInteractiveDistanceRulerOverlay(w, h);
            DrawDraggableCornerElements(w, h);
        }

        private static async Task RenderMapTilesAsync(bool transparentGrgOnly = false)
        {
            if (_tileCanvas == null || _vectorCanvas == null || _activeLayer == null || _fullScreenOverlay?.Visibility != Visibility.Visible) return;

            _renderCts?.Cancel();
            var cts = new CancellationTokenSource();
            _renderCts = cts;
            var ct = cts.Token;

            UpdateSheetViewportSize();
            double w = _sheetFrameBorder.Width > 60 ? _sheetFrameBorder.Width : 800;
            double h = _sheetFrameBorder.Height > 60 ? _sheetFrameBorder.Height : 600;
            double tilePx = GetScaledTileSizePx();

            // Najpierw natychmiast przerysuj wektory, aby interfejs reagował w 0 ms
            RedrawVectorsOnly();

            if (transparentGrgOnly)
            {
                _tileCanvas.Children.Clear();
                _lidarOverlayImage.Source = null;
                _dimmerRect.Visibility = Visibility.Collapsed;
                _mapCompositeGrid.Background = Brushes.Transparent;
                return;
            }

            _mapCompositeGrid.Background = new SolidColorBrush(Color.FromRgb(6, 15, 18));
            _dimmerRect.Visibility = Visibility.Visible;
            _dimmerRect.Opacity = _mapDimmerOpacity;

            try
            {
                Task<BitmapSource> lidarTask = Task.FromResult<BitmapSource>(null);
                if (_showLidarSingleWms && _activeLayer.Id != "geoportal_lidar")
                {
                    string lidarTemplate = Layers.First(l => l.Id == "geoportal_lidar").UrlTemplate;
                    lidarTask = FetchDecodedImageAsync(BuildFullViewportWmsUrl(lidarTemplate, w, h), null, ct);
                }

                if (_activeLayer.IsWmsEpsg3857)
                {
                    string primaryWmsUrl = BuildFullViewportWmsUrl(_activeLayer.UrlTemplate, w, h);
                    string fallbackWmsUrl = !string.IsNullOrEmpty(_activeLayer.FallbackUrlTemplate)
                        ? BuildFullViewportWmsUrl(_activeLayer.FallbackUrlTemplate, w, h)
                        : null;
                    var baseWmsTask = FetchDecodedImageAsync(primaryWmsUrl, fallbackWmsUrl, ct);
                    await Task.WhenAll(baseWmsTask, lidarTask);
                    if (ct.IsCancellationRequested) return;

                    _tileCanvas.Children.Clear();
                    if (baseWmsTask.Result != null)
                    {
                        var img = new Image { Source = baseWmsTask.Result, Width = w, Height = h, Stretch = Stretch.Fill };
                        Canvas.SetLeft(img, 0); Canvas.SetTop(img, 0);
                        _tileCanvas.Children.Add(img);
                    }
                    _lidarOverlayImage.Source = lidarTask.Result;
                    _lidarOverlayImage.Opacity = _lidarOpacity;
                }
                else
                {
                    var (centerTileX, centerTileY) = LatLonToTileXY(_centerLat, _centerLon, _zoom);
                    int minTx = (int)Math.Floor(centerTileX - (w / 2.0) / tilePx) - 1, maxTx = (int)Math.Floor(centerTileX + (w / 2.0) / tilePx) + 1;
                    int minTy = (int)Math.Floor(centerTileY - (h / 2.0) / tilePx) - 1, maxTy = (int)Math.Floor(centerTileY + (h / 2.0) / tilePx) + 1;
                    int maxIndex = (1 << _zoom) - 1;

                    var tileJobs = new List<(double ScreenX, double ScreenY, string Url)>();
                    for (int tx = minTx; tx <= maxTx; tx++)
                        for (int ty = minTy; ty <= maxTy; ty++)
                        {
                            if (ty < 0 || ty > maxIndex) continue;
                            int wrappedTx = ((tx % (maxIndex + 1)) + (maxIndex + 1)) % (maxIndex + 1);
                            tileJobs.Add(((w / 2.0) + (tx - centerTileX) * tilePx, (h / 2.0) + (ty - centerTileY) * tilePx, BuildTileUrl(_activeLayer, wrappedTx, ty, _zoom)));
                        }

                    var tilesTask = Task.WhenAll(tileJobs.Select(async job => (job.ScreenX, job.ScreenY, await FetchDecodedImageAsync(job.Url, null, ct))));
                    await Task.WhenAll(tilesTask, lidarTask);
                    if (ct.IsCancellationRequested) return;

                    _tileCanvas.Children.Clear();
                    foreach (var t in tilesTask.Result)
                    {
                        if (t.Item3 != null)
                        {
                            var img = new Image { Source = t.Item3, Width = tilePx + 0.7, Height = tilePx + 0.7, Stretch = Stretch.Fill };
                            Canvas.SetLeft(img, t.ScreenX); Canvas.SetTop(img, t.ScreenY);
                            _tileCanvas.Children.Add(img);
                        }
                    }
                    _lidarOverlayImage.Source = lidarTask.Result;
                    _lidarOverlayImage.Opacity = _lidarOpacity;
                }
            }
            catch { }
        }

        // =========================================================================
        // ODWRÓCONA MASKA TERENU GRY (AO) ORAZ KOLOROWE SEKTORY WEWNĘTRZNE
        // =========================================================================
        private static Brush CreateOutOfBoundsPatternBrush(Color c, double opacity, string patternMode)
        {
            byte bgAlpha = (byte)Math.Clamp((int)Math.Round(opacity * 200), 0, 235);
            byte lineAlpha = (byte)Math.Clamp((int)Math.Round(Math.Min(1.0, opacity + 0.25) * 255), 0, 255);
            var bgBrush = new SolidColorBrush(Color.FromArgb(bgAlpha, c.R, c.G, c.B)); bgBrush.Freeze();

            if (patternMode.StartsWith("Jednolite")) return bgBrush;
            if (patternMode.StartsWith("Tylko linia")) return Brushes.Transparent;

            var linePen = new Pen(new SolidColorBrush(Color.FromArgb(lineAlpha, c.R, c.G, c.B)), 1.6);
            linePen.Freeze();

            var dg = new DrawingGroup();
            dg.Children.Add(new GeometryDrawing(bgBrush, null, new RectangleGeometry(new Rect(0, 0, 20, 20))));
            dg.Children.Add(new GeometryDrawing(null, linePen, new LineGeometry(new Point(0, 20), new Point(20, 0))));
            if (patternMode.Contains("Symbole X"))
                dg.Children.Add(new GeometryDrawing(null, linePen, new LineGeometry(new Point(0, 0), new Point(20, 20))));

            var db = new DrawingBrush(dg)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 20, 20),
                ViewportUnits = BrushMappingMode.Absolute
            };
            db.Freeze();
            return db;
        }

        private static void DrawInvertedAoMaskAndSectorsOverlay(double w, double h)
        {
            // 1. Teren Gry (AO) — wnętrze przejrzyste, obszar POZA terenem gry zamaskowany kolorem RGB i wzorem
            foreach (var ao in _mapObjects.Where(o => o.Kind == "AO" && o.Points.Count >= 2))
            {
                Color c = Color.FromRgb(235, 64, 52);
                try { c = (Color)ColorConverter.ConvertFromString(ao.ColorHex); } catch { }
                var strokeBrush = TacticalConfigPanel.CreateFrozenBrush(c.R, c.G, c.B);

                var screenPts = ao.Points.Select(pt => LatLonToScreen(pt.Lat, pt.Lon, w, h)).ToList();
                if (screenPts.Count >= 3)
                {
                    var fig = new PathFigure { StartPoint = screenPts[0], IsClosed = true, IsFilled = true };
                    for (int i = 1; i < screenPts.Count; i++) fig.Segments.Add(new LineSegment(screenPts[i], true));
                    var aoPolyGeom = new PathGeometry(new[] { fig });

                    var fullRectGeom = new RectangleGeometry(new Rect(0, 0, w, h));
                    var invertedOutsideGeom = new CombinedGeometry(GeometryCombineMode.Exclude, fullRectGeom, aoPolyGeom);

                    var maskPath = new System.Windows.Shapes.Path
                    {
                        Data = invertedOutsideGeom,
                        Fill = CreateOutOfBoundsPatternBrush(c, _aoOutMaskOpacity, _aoOutPattern),
                        IsHitTestVisible = false
                    };
                    _vectorCanvas.Children.Add(maskPath);
                }

                // Wyraźny obrys samej granicy Terenu Gry (AO)
                var borderPoly = new Polygon
                {
                    Stroke = strokeBrush,
                    StrokeThickness = 2.8,
                    StrokeDashArray = new DoubleCollection { 6, 3 },
                    Fill = Brushes.Transparent,
                    IsHitTestVisible = false
                };
                foreach (var sp in screenPts) borderPoly.Points.Add(sp);
                _vectorCanvas.Children.Add(borderPoly);
                AddHaloLabel($"🛡️ {ao.Name}", screenPts[0].X + 8, screenPts[0].Y + 6, strokeBrush, 10 * _fontScaleMultiplier, 1.0);
                DrawDraggableVertexHandles(ao, screenPts, strokeBrush);
            }

            // 2. Sektory / Strefy wewnętrzne (wypełnione półprzezroczystym kolorem RGB)
            foreach (var sec in _mapObjects.Where(o => o.Kind == "Sector" && o.Points.Count >= 2))
            {
                Color c = Color.FromRgb(245, 184, 46);
                try { c = (Color)ColorConverter.ConvertFromString(sec.ColorHex); } catch { }
                var strokeBrush = TacticalConfigPanel.CreateFrozenBrush(c.R, c.G, c.B);
                var fillBrush = new SolidColorBrush(Color.FromArgb(85, c.R, c.G, c.B)); fillBrush.Freeze();

                var screenPts = sec.Points.Select(pt => LatLonToScreen(pt.Lat, pt.Lon, w, h)).ToList();
                var poly = new Polygon { Stroke = strokeBrush, Fill = fillBrush, StrokeThickness = 2.2, IsHitTestVisible = false };
                foreach (var sp in screenPts) poly.Points.Add(sp);
                _vectorCanvas.Children.Add(poly);

                double cx = screenPts.Average(p => p.X), cy = screenPts.Average(p => p.Y);
                AddHaloLabel(sec.Name, cx - 22, cy - 8, strokeBrush, 10 * _fontScaleMultiplier, 1.0);
                DrawDraggableVertexHandles(sec, screenPts, strokeBrush);
            }
        }

        private static void DrawDraggableVertexHandles(TacticalMapObjectItem obj, List<Point> screenPts, Brush strokeBrush)
        {
            for (int i = 0; i < screenPts.Count; i++)
            {
                int vIdx = i;
                var handle = new Ellipse
                {
                    Width = 10,
                    Height = 10,
                    Fill = Brushes.White,
                    Stroke = strokeBrush,
                    StrokeThickness = 2,
                    Cursor = Cursors.Hand,
                    ToolTip = $"Przeciągnij wierzchołek #{vIdx + 1} ({obj.Name})"
                };
                handle.MouseLeftButtonDown += (_, e) =>
                {
                    e.Handled = true;
                    _draggedObject = obj;
                    _draggedVertexIndex = vIdx;
                    _vectorCanvas.CaptureMouse();
                };
                Canvas.SetLeft(handle, screenPts[i].X - 5);
                Canvas.SetTop(handle, screenPts[i].Y - 5);
                _vectorCanvas.Children.Add(handle);
            }
        }

        private static Brush GetGridColorBrush(byte alpha = 230)
        {
            Color c = _gridColorName switch { "Turkusowy" => Color.FromArgb(alpha, 31, 224, 197), "Biały" => Color.FromArgb(alpha, 255, 255, 255), "Czerwony" => Color.FromArgb(alpha, 235, 64, 52), "Czarny" => Color.FromArgb(alpha, 15, 15, 15), _ => Color.FromArgb(alpha, 250, 208, 44) };
            var b = new SolidColorBrush(c); b.Freeze(); return b;
        }

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
                for (int c = 1; c < cols; c++) _vectorCanvas.Children.Add(new Line { X1 = c * cw, Y1 = 0, X2 = c * cw, Y2 = h, Stroke = mainBrush, StrokeThickness = 1.4 * _gridThicknessMultiplier, IsHitTestVisible = false });
                for (int r = 1; r < rows; r++) _vectorCanvas.Children.Add(new Line { X1 = 0, Y1 = r * ch, X2 = w, Y2 = r * ch, Stroke = mainBrush, StrokeThickness = 1.4 * _gridThicknessMultiplier, IsHitTestVisible = false });
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                        AddHaloLabel($"{(char)('A' + r)}{c + 1}", c * cw + 6, r * ch + 4, mainBrush, 10.5 * _fontScaleMultiplier, _mainGridOpacity);
                return;
            }

            int stepMeters = _subGridDivision.Contains("250") ? 250 : (_subGridDivision.Contains("500") ? 500 : 100);
            double stepPx = Math.Clamp(stepMeters / metersPerPixel, 64.0, 360.0);
            double mainThick = Math.Max(0.8, 1.45 * _gridThicknessMultiplier);
            double subThick = Math.Max(0.7, 0.95 * _gridThicknessMultiplier);
            double startX = (w / 2.0) % stepPx, startY = (h / 2.0) % stepPx;

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
                        if (useHalo) _vectorCanvas.Children.Add(new Line { X1 = sx, Y1 = 0, X2 = sx, Y2 = h, Stroke = subHaloBrush, StrokeThickness = subThick + 1.4, IsHitTestVisible = false });
                        _vectorCanvas.Children.Add(new Line { X1 = sx, Y1 = 0, X2 = sx, Y2 = h, Stroke = subBrush, StrokeThickness = subThick, StrokeDashArray = new DoubleCollection { 4, 2 }, IsHitTestVisible = false });
                    }
                }

                for (double y = startY - stepPx; y < h; y += stepPx)
                {
                    for (int j = 1; j < div; j++)
                    {
                        double sy = y + j * subStep;
                        if (sy < 0 || sy > h) continue;
                        if (useHalo) _vectorCanvas.Children.Add(new Line { X1 = 0, Y1 = sy, X2 = w, Y2 = sy, Stroke = subHaloBrush, StrokeThickness = subThick + 1.4, IsHitTestVisible = false });
                        _vectorCanvas.Children.Add(new Line { X1 = 0, Y1 = sy, X2 = w, Y2 = sy, Stroke = subBrush, StrokeThickness = subThick, StrokeDashArray = new DoubleCollection { 4, 2 }, IsHitTestVisible = false });
                    }
                }

                if (_gridLabelsMode.Contains("Ćwiartki/Keypad") && stepPx >= 64)
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

            if (mainAlpha > 4)
            {
                bool isWgs84 = _coordSystem.StartsWith("WGS84");
                for (double x = startX; x < w; x += stepPx)
                {
                    if (useHalo) _vectorCanvas.Children.Add(new Line { X1 = x, Y1 = 0, X2 = x, Y2 = h, Stroke = haloBrush, StrokeThickness = mainThick + 2.0, IsHitTestVisible = false });
                    _vectorCanvas.Children.Add(new Line { X1 = x, Y1 = 0, X2 = x, Y2 = h, Stroke = mainBrush, StrokeThickness = mainThick, IsHitTestVisible = false });
                    var (_, lon) = ScreenToLatLon(x, 20, w, h);
                    string lblTxt = isWgs84
                        ? $"{lon:0.0000}°E"
                        : $"{(((int)Math.Round(((lon + 180.0) % 6.0) * 111320.0 * Math.Cos(_centerLat * Math.PI / 180.0)) / 100) % 1000):D3}";
                    AddHaloLabel(lblTxt, x + 3, 6, mainBrush, 9.5 * _fontScaleMultiplier, _mainGridOpacity);
                }
                for (double y = startY; y < h; y += stepPx)
                {
                    if (useHalo) _vectorCanvas.Children.Add(new Line { X1 = 0, Y1 = y, X2 = w, Y2 = y, Stroke = haloBrush, StrokeThickness = mainThick + 2.0, IsHitTestVisible = false });
                    _vectorCanvas.Children.Add(new Line { X1 = 0, Y1 = y, X2 = w, Y2 = y, Stroke = mainBrush, StrokeThickness = mainThick, IsHitTestVisible = false });
                    var (lat, _) = ScreenToLatLon(20, y, w, h);
                    string lblTxt = isWgs84
                        ? $"{lat:0.0000}°N"
                        : $"{(((int)Math.Round(lat * 110574.0) / 100) % 1000):D3}";
                    AddHaloLabel(lblTxt, 6, y + 2, mainBrush, 9.5 * _fontScaleMultiplier, _mainGridOpacity);
                }
            }
        }

        private static void AddSubCellBadge(string text, double x, double y, Brush fg, double opacity)
        {
            double fSize = 8.5 * _fontScaleMultiplier;
            if (_labelBgStyle == "Czysty tekst")
            {
                var tb = new TextBlock { Text = text, Foreground = fg, FontSize = fSize, FontWeight = FontWeights.Bold, IsHitTestVisible = false };
                Canvas.SetLeft(tb, x); Canvas.SetTop(tb, y); _vectorCanvas.Children.Add(tb);
                return;
            }
            byte bgAlpha = (byte)Math.Clamp((int)Math.Round((_labelBgStyle.StartsWith("Kaseton") ? 160 : 75) * opacity), 0, 225);
            var b = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(bgAlpha, 0, 0, 0)),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(2.5, 0, 2.5, 0),
                IsHitTestVisible = false,
                Child = new TextBlock { Text = text, Foreground = fg, FontSize = fSize, FontWeight = FontWeights.Bold }
            };
            Canvas.SetLeft(b, x); Canvas.SetTop(b, y); _vectorCanvas.Children.Add(b);
        }

        private static void AddHaloLabel(string text, double x, double y, Brush fg, double fontSize, double opacity = 1.0)
        {
            if (_labelBgStyle == "Czysty tekst")
            {
                var tb = new TextBlock { Text = text, Foreground = fg, FontSize = fontSize, FontWeight = FontWeights.Bold, IsHitTestVisible = false };
                Canvas.SetLeft(tb, x); Canvas.SetTop(tb, y); _vectorCanvas.Children.Add(tb);
                return;
            }
            byte bgAlpha = (byte)Math.Clamp((int)Math.Round((_labelBgStyle.StartsWith("Kaseton") ? 190 : 95) * opacity), 0, 235);
            var b = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(bgAlpha, 5, 12, 15)),
                BorderBrush = _labelBgStyle.StartsWith("Kaseton") ? fg : Brushes.Transparent,
                BorderThickness = new Thickness(0.8),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 1, 4, 1),
                IsHitTestVisible = false,
                Child = new TextBlock { Text = text, Foreground = fg, FontSize = fontSize, FontWeight = FontWeights.Bold }
            };
            Canvas.SetLeft(b, x); Canvas.SetTop(b, y); _vectorCanvas.Children.Add(b);
        }

        private static void AttachMarkerDragHandler(UIElement el, TacticalMapObjectItem obj)
        {
            if (el is FrameworkElement fe)
            {
                fe.Cursor = Cursors.Hand;
                fe.ToolTip = $"Przeciągnij myszą, aby przesunąć: {obj.Name}";
            }
            el.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                _draggedObject = obj;
                _draggedVertexIndex = 0;
                _vectorCanvas.CaptureMouse();
            };
        }

        private static void DrawNatoVectorSymbolOnCanvas(Point pt, TacticalMapObjectItem obj)
        {
            string symbolCode = obj.SymbolCode ?? "NATO_HQ";
            string name = obj.Name;
            string colorHex = obj.ColorHex;

            Color c = Color.FromRgb(38, 132, 255);
            try { c = (Color)ColorConverter.ConvertFromString(colorHex); } catch { }

            var strokeBrush = TacticalConfigPanel.CreateFrozenBrush(c.R, c.G, c.B);
            var fillBrush = new SolidColorBrush(Color.FromArgb(185, c.R, c.G, c.B)); fillBrush.Freeze();
            var darkBgBrush = new SolidColorBrush(Color.FromArgb(225, 8, 16, 20)); darkBgBrush.Freeze();

            const double fw = 30.0, fh = 20.0;
            double left = pt.X - fw / 2.0, top = pt.Y - fh / 2.0;

            var markerContainer = new Canvas { Width = 44, Height = 44, Background = Brushes.Transparent };
            AttachMarkerDragHandler(markerContainer, obj);

            switch (symbolCode)
            {
                case "NATO_HQ":
                    double hqLeft = pt.X, hqTop = pt.Y - fh - 12;
                    _vectorCanvas.Children.Add(new Line { X1 = hqLeft, Y1 = hqTop, X2 = hqLeft, Y2 = pt.Y, Stroke = Brushes.Black, StrokeThickness = 4.2, IsHitTestVisible = false });
                    _vectorCanvas.Children.Add(new Line { X1 = hqLeft, Y1 = hqTop, X2 = hqLeft, Y2 = pt.Y, Stroke = strokeBrush, StrokeThickness = 2.4, IsHitTestVisible = false });
                    var hqRectHalo = new Rectangle { Width = fw + 2, Height = fh + 2, Stroke = Brushes.Black, StrokeThickness = 3.2, Fill = darkBgBrush };
                    AttachMarkerDragHandler(hqRectHalo, obj);
                    Canvas.SetLeft(hqRectHalo, hqLeft - 1); Canvas.SetTop(hqRectHalo, hqTop - 1); _vectorCanvas.Children.Add(hqRectHalo);
                    var hqRect = new Rectangle { Width = fw, Height = fh, Stroke = strokeBrush, StrokeThickness = 2.0, Fill = fillBrush };
                    AttachMarkerDragHandler(hqRect, obj);
                    Canvas.SetLeft(hqRect, hqLeft); Canvas.SetTop(hqRect, hqTop); _vectorCanvas.Children.Add(hqRect);
                    var hqTxt = new TextBlock { Text = "HQ", Foreground = Brushes.White, FontSize = 9.5, FontWeight = FontWeights.Bold, IsHitTestVisible = false };
                    Canvas.SetLeft(hqTxt, hqLeft + 6); Canvas.SetTop(hqTxt, hqTop + 3); _vectorCanvas.Children.Add(hqTxt);
                    AddHaloLabel(name, hqLeft + fw + 5, hqTop + 1, strokeBrush, 10 * _fontScaleMultiplier);
                    return;

                case "NATO_INF":
                    DrawStandardNatoBox(left, top, fw, fh, strokeBrush, fillBrush, darkBgBrush, obj);
                    _vectorCanvas.Children.Add(new Line { X1 = left, Y1 = top, X2 = left + fw, Y2 = top + fh, Stroke = Brushes.White, StrokeThickness = 1.8, IsHitTestVisible = false });
                    _vectorCanvas.Children.Add(new Line { X1 = left, Y1 = top + fh, X2 = left + fw, Y2 = top, Stroke = Brushes.White, StrokeThickness = 1.8, IsHitTestVisible = false });
                    break;

                case "NATO_RECON":
                    DrawStandardNatoBox(left, top, fw, fh, strokeBrush, fillBrush, darkBgBrush, obj);
                    _vectorCanvas.Children.Add(new Line { X1 = left, Y1 = top + fh, X2 = left + fw, Y2 = top, Stroke = Brushes.White, StrokeThickness = 2.0, IsHitTestVisible = false });
                    break;

                case "NATO_MED":
                    DrawStandardNatoBox(left, top, fw, fh, strokeBrush, fillBrush, darkBgBrush, obj);
                    _vectorCanvas.Children.Add(new Line { X1 = pt.X, Y1 = top, X2 = pt.X, Y2 = top + fh, Stroke = Brushes.White, StrokeThickness = 2.2, IsHitTestVisible = false });
                    _vectorCanvas.Children.Add(new Line { X1 = left, Y1 = pt.Y, X2 = left + fw, Y2 = pt.Y, Stroke = Brushes.White, StrokeThickness = 2.2, IsHitTestVisible = false });
                    break;

                case "NATO_COMMS":
                    DrawStandardNatoBox(left, top, fw, fh, strokeBrush, fillBrush, darkBgBrush, obj);
                    var bolt = new Polyline { Stroke = Brushes.White, StrokeThickness = 2.0, IsHitTestVisible = false, Points = new PointCollection { new Point(left + 6, top + fh - 3), new Point(pt.X - 1, top + 4), new Point(pt.X + 2, top + fh - 5), new Point(left + fw - 6, top + 3) } };
                    _vectorCanvas.Children.Add(bolt);
                    break;

                case "NATO_SUPPLY":
                    DrawStandardNatoBox(left, top, fw, fh, strokeBrush, fillBrush, darkBgBrush, obj);
                    _vectorCanvas.Children.Add(new Line { X1 = left, Y1 = top + fh - 5, X2 = left + fw, Y2 = top + fh - 5, Stroke = Brushes.White, StrokeThickness = 2.0, IsHitTestVisible = false });
                    break;

                case "NATO_OP":
                    var triHalo = new Polygon { Stroke = Brushes.Black, StrokeThickness = 3.4, Fill = darkBgBrush, Points = new PointCollection { new Point(pt.X, pt.Y - 13), new Point(pt.X - 13, pt.Y + 11), new Point(pt.X + 13, pt.Y + 11) } };
                    AttachMarkerDragHandler(triHalo, obj); _vectorCanvas.Children.Add(triHalo);
                    var tri = new Polygon { Stroke = strokeBrush, StrokeThickness = 2.0, Fill = fillBrush, Points = triHalo.Points };
                    AttachMarkerDragHandler(tri, obj); _vectorCanvas.Children.Add(tri);
                    var opDot = new Ellipse { Width = 5, Height = 5, Fill = Brushes.White, IsHitTestVisible = false };
                    Canvas.SetLeft(opDot, pt.X - 2.5); Canvas.SetTop(opDot, pt.Y); _vectorCanvas.Children.Add(opDot);
                    break;

                case "NATO_SNIPER":
                    var snHalo = new Ellipse { Width = 24, Height = 24, Stroke = Brushes.Black, StrokeThickness = 3.2, Fill = darkBgBrush };
                    AttachMarkerDragHandler(snHalo, obj); Canvas.SetLeft(snHalo, pt.X - 12); Canvas.SetTop(snHalo, pt.Y - 12); _vectorCanvas.Children.Add(snHalo);
                    var snCirc = new Ellipse { Width = 22, Height = 22, Stroke = strokeBrush, StrokeThickness = 2.0, Fill = fillBrush };
                    AttachMarkerDragHandler(snCirc, obj); Canvas.SetLeft(snCirc, pt.X - 11); Canvas.SetTop(snCirc, pt.Y - 11); _vectorCanvas.Children.Add(snCirc);
                    _vectorCanvas.Children.Add(new Line { X1 = pt.X - 14, Y1 = pt.Y, X2 = pt.X + 14, Y2 = pt.Y, Stroke = Brushes.White, StrokeThickness = 1.5, IsHitTestVisible = false });
                    _vectorCanvas.Children.Add(new Line { X1 = pt.X, Y1 = pt.Y - 14, X2 = pt.X, Y2 = pt.Y + 14, Stroke = Brushes.White, StrokeThickness = 1.5, IsHitTestVisible = false });
                    break;

                case "NATO_CP":
                    var shPts = new PointCollection { new Point(pt.X - 12, pt.Y - 11), new Point(pt.X + 12, pt.Y - 11), new Point(pt.X + 12, pt.Y + 2), new Point(pt.X, pt.Y + 13), new Point(pt.X - 12, pt.Y + 2) };
                    var cpHalo = new Polygon { Stroke = Brushes.Black, StrokeThickness = 3.2, Fill = darkBgBrush, Points = shPts };
                    AttachMarkerDragHandler(cpHalo, obj); _vectorCanvas.Children.Add(cpHalo);
                    var cpPoly = new Polygon { Stroke = strokeBrush, StrokeThickness = 2.0, Fill = fillBrush, Points = shPts };
                    AttachMarkerDragHandler(cpPoly, obj); _vectorCanvas.Children.Add(cpPoly);
                    var cpTxt = new TextBlock { Text = "CP", Foreground = Brushes.White, FontSize = 8.5, FontWeight = FontWeights.Bold, IsHitTestVisible = false };
                    Canvas.SetLeft(cpTxt, pt.X - 6); Canvas.SetTop(cpTxt, pt.Y - 7); _vectorCanvas.Children.Add(cpTxt);
                    break;

                case "NATO_LZ":
                    var lzHalo = new Ellipse { Width = 26, Height = 26, Stroke = Brushes.Black, StrokeThickness = 3.2, Fill = darkBgBrush };
                    AttachMarkerDragHandler(lzHalo, obj); Canvas.SetLeft(lzHalo, pt.X - 13); Canvas.SetTop(lzHalo, pt.Y - 13); _vectorCanvas.Children.Add(lzHalo);
                    var lzCirc = new Ellipse { Width = 24, Height = 24, Stroke = strokeBrush, StrokeThickness = 2.0, Fill = fillBrush };
                    AttachMarkerDragHandler(lzCirc, obj); Canvas.SetLeft(lzCirc, pt.X - 12); Canvas.SetTop(lzCirc, pt.Y - 12); _vectorCanvas.Children.Add(lzCirc);
                    var lzTxt = new TextBlock { Text = "LZ", Foreground = Brushes.White, FontSize = 9, FontWeight = FontWeights.Bold, IsHitTestVisible = false };
                    Canvas.SetLeft(lzTxt, pt.X - 6); Canvas.SetTop(lzTxt, pt.Y - 6); _vectorCanvas.Children.Add(lzTxt);
                    break;

                case "NATO_OBJ":
                    var dmPts = new PointCollection { new Point(pt.X, pt.Y - 14), new Point(pt.X + 14, pt.Y), new Point(pt.X, pt.Y + 14), new Point(pt.X - 14, pt.Y) };
                    var objHalo = new Polygon { Stroke = Brushes.Black, StrokeThickness = 3.2, Fill = darkBgBrush, Points = dmPts };
                    AttachMarkerDragHandler(objHalo, obj); _vectorCanvas.Children.Add(objHalo);
                    var objPoly = new Polygon { Stroke = strokeBrush, StrokeThickness = 2.0, Fill = fillBrush, Points = dmPts };
                    AttachMarkerDragHandler(objPoly, obj); _vectorCanvas.Children.Add(objPoly);
                    _vectorCanvas.Children.Add(new Line { X1 = pt.X - 7, Y1 = pt.Y, X2 = pt.X + 7, Y2 = pt.Y, Stroke = Brushes.White, StrokeThickness = 1.6, IsHitTestVisible = false });
                    _vectorCanvas.Children.Add(new Line { X1 = pt.X, Y1 = pt.Y - 7, X2 = pt.X, Y2 = pt.Y + 7, Stroke = Brushes.White, StrokeThickness = 1.6, IsHitTestVisible = false });
                    break;

                case "NATO_HAZARD":
                    var hzPts = new PointCollection { new Point(pt.X, pt.Y - 13), new Point(pt.X - 13, pt.Y + 11), new Point(pt.X + 13, pt.Y + 11) };
                    var hzHalo = new Polygon { Stroke = Brushes.Black, StrokeThickness = 3.4, Fill = darkBgBrush, Points = hzPts };
                    AttachMarkerDragHandler(hzHalo, obj); _vectorCanvas.Children.Add(hzHalo);
                    var hzPoly = new Polygon { Stroke = strokeBrush, StrokeThickness = 2.0, Fill = fillBrush, Points = hzPts };
                    AttachMarkerDragHandler(hzPoly, obj); _vectorCanvas.Children.Add(hzPoly);
                    var hzTxt = new TextBlock { Text = "!", Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.Bold, IsHitTestVisible = false };
                    Canvas.SetLeft(hzTxt, pt.X - 2.5); Canvas.SetTop(hzTxt, pt.Y - 5); _vectorCanvas.Children.Add(hzTxt);
                    break;

                default:
                    var dot = new Ellipse { Width = 14, Height = 14, Fill = strokeBrush, Stroke = Brushes.White, StrokeThickness = 2 };
                    AttachMarkerDragHandler(dot, obj);
                    Canvas.SetLeft(dot, pt.X - 7); Canvas.SetTop(dot, pt.Y - 7); _vectorCanvas.Children.Add(dot);
                    break;
            }

            AddHaloLabel(name, pt.X + 17, pt.Y - 9, strokeBrush, 10 * _fontScaleMultiplier);
        }

        private static void DrawStandardNatoBox(double left, double top, double w, double h, Brush strokeBrush, Brush fillBrush, Brush darkBgBrush, TacticalMapObjectItem obj)
        {
            var halo = new Rectangle { Width = w + 2, Height = h + 2, Stroke = Brushes.Black, StrokeThickness = 3.2, Fill = darkBgBrush };
            AttachMarkerDragHandler(halo, obj);
            Canvas.SetLeft(halo, left - 1); Canvas.SetTop(halo, top - 1); _vectorCanvas.Children.Add(halo);
            var rect = new Rectangle { Width = w, Height = h, Stroke = strokeBrush, StrokeThickness = 2.0, Fill = fillBrush };
            AttachMarkerDragHandler(rect, obj);
            Canvas.SetLeft(rect, left); Canvas.SetTop(rect, top); _vectorCanvas.Children.Add(rect);
        }

        private static void DrawTacticalMarkersAndRoutesOverlay(double w, double h)
        {
            foreach (var obj in _mapObjects.Where(o => o.Points.Count > 0 && o.Kind != "AO" && o.Kind != "Sector"))
            {
                var strokeBrush = TacticalConfigPanel.BrushFromHex(obj.ColorHex, Color.FromRgb(38, 132, 255));
                if (obj.Kind == "Route" && obj.Points.Count >= 2)
                {
                    var screenPts = obj.Points.Select(pt => LatLonToScreen(pt.Lat, pt.Lon, w, h)).ToList();
                    var pl = new Polyline { Stroke = strokeBrush, StrokeThickness = 2.8, StrokeDashArray = new DoubleCollection { 4, 2 }, IsHitTestVisible = false };
                    foreach (var sp in screenPts) pl.Points.Add(sp);
                    _vectorCanvas.Children.Add(pl);
                    AddHaloLabel(obj.Name, screenPts[0].X + 6, screenPts[0].Y - 12, strokeBrush, 9.5 * _fontScaleMultiplier);
                    DrawDraggableVertexHandles(obj, screenPts, strokeBrush);
                }
                else if (obj.Kind == "Dot")
                {
                    Point pt = LatLonToScreen(obj.Points[0].Lat, obj.Points[0].Lon, w, h);
                    var dot = new Ellipse { Width = 13, Height = 13, Fill = strokeBrush, Stroke = Brushes.White, StrokeThickness = 2 };
                    AttachMarkerDragHandler(dot, obj);
                    Canvas.SetLeft(dot, pt.X - 6.5); Canvas.SetTop(dot, pt.Y - 6.5); _vectorCanvas.Children.Add(dot);
                    AddHaloLabel(obj.Name, pt.X + 9, pt.Y - 8, strokeBrush, 9.5 * _fontScaleMultiplier);
                }
                else
                {
                    Point pt = LatLonToScreen(obj.Points[0].Lat, obj.Points[0].Lon, w, h);
                    DrawNatoVectorSymbolOnCanvas(pt, obj);
                }
            }
        }

        // Umieszcza element HUD (Północ / Skala / Legenda) i pozwala go przesuwać myszą (Drag & Drop)
        private static void PlaceDraggableHudElement(FrameworkElement el, string hudId, string cornerPreset, double? normX, double? normY, double viewW, double viewH, double elemW, double elemH)
        {
            double x = normX.HasValue
                ? Math.Clamp(normX.Value * viewW, 4, Math.Max(8, viewW - elemW - 4))
                : (cornerPreset.StartsWith("Lewy") ? 14 : Math.Max(14, viewW - elemW - 14));
            double y = normY.HasValue
                ? Math.Clamp(normY.Value * viewH, 4, Math.Max(8, viewH - elemH - 4))
                : (cornerPreset.Contains("górny") ? 14 : Math.Max(14, viewH - elemH - 14));

            el.Cursor = Cursors.SizeAll;
            el.ToolTip = "Przeciągnij myszą (Drag & Drop), aby ustawić w dowolnym miejscu na mapie";
            el.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                _draggedHudId = hudId;
                _draggedHudElement = el;
                Point p = e.GetPosition(_vectorCanvas);
                _hudStartOffset = new Point(p.X - Canvas.GetLeft(el), p.Y - Canvas.GetTop(el));
                _vectorCanvas.CaptureMouse();
            };

            Canvas.SetLeft(el, x);
            Canvas.SetTop(el, y);
            _vectorCanvas.Children.Add(el);
        }

        private static void DrawDraggableCornerElements(double w, double h)
        {
            var (wMm, _) = GetActiveSheetDimensionsMm();
            bool isCompactWrist = wMm <= 160;

            if (_showNorthArrow)
            {
                double nw = isCompactWrist ? 42 : 52, nh = isCompactWrist ? 50 : 62;
                var compass = new Border { Width = nw, Height = nh, Background = new SolidColorBrush(Color.FromArgb(225, 8, 19, 22)), BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(6) };
                var cs = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                cs.Children.Add(new TextBlock { Text = "N", Foreground = TacticalConfigPanel.BrushAccent, FontWeight = FontWeights.Bold, FontSize = isCompactWrist ? 11 : 14, HorizontalAlignment = HorizontalAlignment.Center });
                cs.Children.Add(new TextBlock { Text = "▲", Foreground = Brushes.White, FontSize = isCompactWrist ? 14 : 18, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -4, 0, -2) });
                cs.Children.Add(new TextBlock { Text = "MGRS", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 7.5, HorizontalAlignment = HorizontalAlignment.Center });
                compass.Child = cs;
                PlaceDraggableHudElement(compass, "NORTH", _northArrowPos, _northNormX, _northNormY, w, h, nw, nh);
            }

            if (_showScaleBar)
            {
                double metersPerPixel = GetExactMetersPerPixel();
                double chosen = 100;
                foreach (var d in new[] { 20.0, 50.0, 100.0, 200.0, 500.0, 1000.0, 2000.0, 5000.0 })
                {
                    double px = d / metersPerPixel;
                    if (px >= (isCompactWrist ? 55 : 75) && px <= (isCompactWrist ? 150 : 220)) { chosen = d; break; }
                }
                double barW = Math.Clamp(chosen / metersPerPixel, 55, 220);

                var sbBorder = new Border { Width = barW + 34, Height = 52, Background = new SolidColorBrush(Color.FromArgb(225, 8, 19, 22)), BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(6) };
                var cnv = new Canvas();
                var hdr = new TextBlock { Text = $"SKALA 1:{_targetScaleDenominator:N0}", Foreground = TacticalConfigPanel.BrushAccent, FontSize = 9.5, FontWeight = FontWeights.Bold };
                Canvas.SetLeft(hdr, 12); Canvas.SetTop(hdr, 4); cnv.Children.Add(hdr);
                double segW = barW / 4.0;
                for (int s = 0; s < 4; s++)
                {
                    var r = new Rectangle { Width = segW + 0.5, Height = 6.5, Fill = (s % 2 == 0) ? TacticalConfigPanel.BrushAccent : Brushes.White, Stroke = Brushes.Black, StrokeThickness = 0.7 };
                    Canvas.SetLeft(r, 12 + s * segW); Canvas.SetTop(r, 22); cnv.Children.Add(r);
                }
                var t0 = new TextBlock { Text = "0", Foreground = Brushes.White, FontSize = 8.5 }; Canvas.SetLeft(t0, 12); Canvas.SetTop(t0, 31); cnv.Children.Add(t0);
                var tHalf = new TextBlock { Text = $"{chosen / 2:0}m", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 8 }; Canvas.SetLeft(tHalf, 12 + barW / 2 - 10); Canvas.SetTop(tHalf, 31); cnv.Children.Add(tHalf);
                var t1 = new TextBlock { Text = $"{chosen:0} m", Foreground = Brushes.White, FontSize = 8.5, FontWeight = FontWeights.Bold }; Canvas.SetLeft(t1, 12 + barW - 22); Canvas.SetTop(t1, 31); cnv.Children.Add(t1);
                sbBorder.Child = cnv;
                PlaceDraggableHudElement(sbBorder, "SCALE", _scaleBarPos, _scaleNormX, _scaleNormY, w, h, barW + 34, 52);
            }

            if (_showLegend)
            {
                double lw = isCompactWrist ? 195 : 245, lh = isCompactWrist ? 58 : 70;
                var legBorder = new Border { Width = lw, Height = lh, Background = new SolidColorBrush(Color.FromArgb(225, 8, 19, 22)), BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 5, 8, 5) };
                var ls = new StackPanel();
                ls.Children.Add(new TextBlock { Text = $"MAPA ({_selectedSheet.Name.Split('—')[0].Trim()} | 1:{_targetScaleDenominator:N0})", Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = isCompactWrist ? 9 : 10 });
                ls.Children.Add(new TextBlock { Text = $"Siatka: {_coordSystem.Split(' ')[0]} | {_subGridDivision}", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 8.5, Margin = new Thickness(0, 1, 0, 3) });
                var fw = new WrapPanel();
                foreach (var f in TacticalConfigPanel.GetConfiguredFactions())
                {
                    var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0) };
                    sp.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(2), Background = TacticalConfigPanel.BrushFromHex(f.ColorHex, Color.FromRgb(38, 132, 255)), Margin = new Thickness(0, 0, 4, 0) });
                    sp.Children.Add(new TextBlock { Text = f.Name, Foreground = Brushes.White, FontSize = 8.5, FontWeight = FontWeights.Bold });
                    fw.Children.Add(sp);
                }
                ls.Children.Add(fw);
                legBorder.Child = ls;
                PlaceDraggableHudElement(legBorder, "LEGEND", _legendPos, _legendNormX, _legendNormY, w, h, lw, lh);
            }
        }

        private static void DrawInteractiveDistanceRulerOverlay(double w, double h)
        {
            if (_rulerStart == null || _rulerEnd == null) return;
            Point p1 = LatLonToScreen(_rulerStart.Value.Lat, _rulerStart.Value.Lon, w, h);
            Point p2 = LatLonToScreen(_rulerEnd.Value.Lat, _rulerEnd.Value.Lon, w, h);
            _vectorCanvas.Children.Add(new Line { X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y, Stroke = TacticalConfigPanel.BrushAccent, StrokeThickness = 2.2, StrokeDashArray = new DoubleCollection { 4, 2 }, IsHitTestVisible = false });
            double dLat = (_rulerEnd.Value.Lat - _rulerStart.Value.Lat) * Math.PI / 180.0, dLon = (_rulerEnd.Value.Lon - _rulerStart.Value.Lon) * Math.PI / 180.0;
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(_rulerStart.Value.Lat * Math.PI / 180.0) * Math.Cos(_rulerEnd.Value.Lat * Math.PI / 180.0) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            double dist = 6371000.0 * 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
            AddHaloLabel($"📏 {dist:0} m", (p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2 - 12, TacticalConfigPanel.BrushAccent, 10.5 * _fontScaleMultiplier);
        }

        private static byte[] CaptureMapCanvasPngBytes()
        {
            int w = (int)Math.Max(400, _mapCompositeGrid.ActualWidth), h = (int)Math.Max(300, _mapCompositeGrid.ActualHeight);
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(_mapCompositeGrid);
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
                    ["orientation"] = _sheetOrientation,
                    ["sheetFormat"] = _selectedSheet?.Name ?? "",
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

        private static void LoadMapProjectJsonOrKml(Window win)
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
                RefreshObjectsListUI(win);
                ApplyScaleDenominator(_targetScaleDenominator);
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

                double w = Math.Max(400, _vectorCanvas.ActualWidth), h = Math.Max(300, _vectorCanvas.ActualHeight);
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

                double w = Math.Max(400, _vectorCanvas.ActualWidth), h = Math.Max(300, _vectorCanvas.ActualHeight);
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

        // Eksport do PDF z obsługą orientacji Pion/Poziom oraz liniami cięcia (Crop Marks) dla formatów naręcznych i kieszonkowych!
        private static async Task ExportTacticalMapPdfAsync(Window win)
        {
            try
            {
                string sourceDir = TacticalConfigPanel.GetPrimarySourceDirectory(win);
                string parentDir = System.IO.Path.GetDirectoryName(System.IO.Path.TrimEndingDirectorySeparator(sourceDir)) ?? AppDomain.CurrentDomain.BaseDirectory;
                string outDir = System.IO.Path.Combine(parentDir, "release_maps"); Directory.CreateDirectory(outDir);

                bool landscape = _sheetOrientation == "Poziomo";
                var (wMm, hMm) = GetActiveSheetDimensionsMm();
                string cleanFmt = TacticalConfigPanel.SanitizeFileName(_selectedSheet.Name.Split('—')[0].Trim());
                string pdfPath = System.IO.Path.Combine(outDir, $"Mapa_{cleanFmt}_{_sheetOrientation}_1_{_targetScaleDenominator}.pdf");

                int pageW = landscape ? 1754 : 1240;
                int pageH = landscape ? 1240 : 1754;
                double hostPageWMm = landscape ? 297.0 : 210.0;
                double hostPageHMm = landscape ? 210.0 : 297.0;

                int mapW = (int)Math.Max(400, _mapCompositeGrid.ActualWidth);
                int mapH = (int)Math.Max(300, _mapCompositeGrid.ActualHeight);
                var mapRtb = new RenderTargetBitmap(mapW, mapH, 96, 96, PixelFormats.Pbgra32);
                mapRtb.Render(_mapCompositeGrid);

                var pageVisual = new DrawingVisual();
                using (DrawingContext dc = pageVisual.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, pageW, pageH));

                    // Jeśli wybrano format mniejszy niż A4 (np. A5, A6, Mapnik Naręczny), osadzamy go w skali 1:1 w milimetrach na kartce A4 ze znacznikami linii cięcia!
                    if (wMm < 285 && hMm < 285)
                    {
                        double drawW = (wMm / hostPageWMm) * pageW;
                        double drawH = (hMm / hostPageHMm) * pageH;
                        double ox = (pageW - drawW) / 2.0;
                        double oy = (pageH - drawH) / 2.0;

                        dc.DrawImage(mapRtb, new Rect(ox, oy, drawW, drawH));
                        var cropPen = new Pen(Brushes.Black, 1.5) { DashStyle = DashStyles.Dash };
                        dc.DrawRectangle(null, new Pen(Brushes.Black, 1.2), new Rect(ox, oy, drawW, drawH));

                        // Linie cięcia (Crop Marks) w narożnikach
                        double cm = 28;
                        dc.DrawLine(cropPen, new Point(ox - cm, oy), new Point(ox + drawW + cm, oy));
                        dc.DrawLine(cropPen, new Point(ox - cm, oy + drawH), new Point(ox + drawW + cm, oy + drawH));
                        dc.DrawLine(cropPen, new Point(ox, oy - cm), new Point(ox, oy + drawH + cm));
                        dc.DrawLine(cropPen, new Point(ox + drawW, oy - cm), new Point(ox + drawW, oy + drawH + cm));

                        TacticalConfigPanel.DrawCenteredText(dc, $"✂️ LINIA CIĘCIA MAPNIKA: {_selectedSheet.Name} ({_sheetOrientation}: {wMm:0} × {hMm:0} mm)  |  Skala dokładna 1:{_targetScaleDenominator:N0} (Drukuj na A4 w skali 100% / Rozmiar rzeczywisty)", 16, Brushes.Black, pageW / 2.0, oy - 36, true);
                    }
                    else
                    {
                        dc.DrawImage(mapRtb, new Rect(24, 24, pageW - 48, pageH - 48));
                    }
                }

                var fullRtb = new RenderTargetBitmap(pageW, pageH, 96, 96, PixelFormats.Pbgra32);
                fullRtb.Render(pageVisual);
                var jpgEnc = new JpegBitmapEncoder { QualityLevel = 94 };
                jpgEnc.Frames.Add(BitmapFrame.Create(fullRtb));
                using var jpgMs = new MemoryStream();
                jpgEnc.Save(jpgMs);
                byte[] jpegBytes = jpgMs.ToArray();

                await Task.Run(() => WriteSingleImagePdf(pdfPath, jpegBytes, pageW, pageH, landscape));
                if (_statusText != null) _statusText.Text = $"✓ Wygenerowano plik PDF ({cleanFmt} {_sheetOrientation}): {pdfPath}";
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
