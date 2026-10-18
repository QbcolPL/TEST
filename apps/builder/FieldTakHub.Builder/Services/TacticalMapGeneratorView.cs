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
using System.Runtime.InteropServices;
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
using System.Windows.Threading;
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
        public string ShortBadge { get; set; }
        public string NatoMeaning { get; set; }
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
        public bool ShowLabel { get; set; } = true;
        public List<(double Lat, double Lon)> Points { get; set; } = new List<(double Lat, double Lon)>();
    }

    internal static class NativeSqliteWriter
    {
        private const string SqliteDll = "winsqlite3.dll";

        [DllImport(SqliteDll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_open(byte[] filenameUtf8, out IntPtr db);

        [DllImport(SqliteDll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_close(IntPtr db);

        [DllImport(SqliteDll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_exec(IntPtr db, byte[] sqlUtf8, IntPtr callback, IntPtr arg, out IntPtr errMsg);

        [DllImport(SqliteDll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_prepare_v2(IntPtr db, byte[] sqlUtf8, int nByte, out IntPtr stmt, IntPtr pzTail);

        [DllImport(SqliteDll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_bind_int(IntPtr stmt, int index, int val);

        [DllImport(SqliteDll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_bind_blob(IntPtr stmt, int index, byte[] data, int nData, IntPtr destructor);

        [DllImport(SqliteDll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_step(IntPtr stmt);

        [DllImport(SqliteDll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_reset(IntPtr stmt);

        [DllImport(SqliteDll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_finalize(IntPtr stmt);

        private static byte[] ToUtf8NullTerm(string s) => Encoding.UTF8.GetBytes((s ?? "") + "\0");

        private static void ExecSql(IntPtr db, string sql)
        {
            sqlite3_exec(db, ToUtf8NullTerm(sql), IntPtr.Zero, IntPtr.Zero, out _);
        }

        public static void WriteMbTilesDatabase(string filePath, string mapTitle, int minZoom, int maxZoom, double north, double south, double east, double west, List<(int Z, int X, int Y, byte[] Data)> tiles)
        {
            if (File.Exists(filePath)) File.Delete(filePath);
            if (sqlite3_open(ToUtf8NullTerm(filePath), out IntPtr db) != 0) return;
            try
            {
                ExecSql(db, "PRAGMA synchronous = OFF; PRAGMA journal_mode = MEMORY;");
                ExecSql(db, "CREATE TABLE metadata (name text, value text);");
                ExecSql(db, "CREATE TABLE tiles (zoom_level integer, tile_column integer, tile_row integer, tile_data blob);");
                ExecSql(db, "CREATE UNIQUE INDEX tile_index on tiles (zoom_level, tile_column, tile_row);");

                string bounds = string.Create(CultureInfo.InvariantCulture, $"{west:0.000000},{south:0.000000},{east:0.000000},{north:0.000000}");
                string center = string.Create(CultureInfo.InvariantCulture, $"{(west + east) / 2.0:0.000000},{(south + north) / 2.0:0.000000},{minZoom}");
                string safeTitle = (mapTitle ?? "FieldTakHub_Map").Replace("'", "");

                ExecSql(db, "BEGIN TRANSACTION;");
                ExecSql(db, $"INSERT INTO metadata VALUES ('name', '{safeTitle}');");
                ExecSql(db, "INSERT INTO metadata VALUES ('type', 'baselayer');");
                ExecSql(db, "INSERT INTO metadata VALUES ('version', '1.2');");
                ExecSql(db, "INSERT INTO metadata VALUES ('description', 'Field TAK Hub 2.4 Offline Map');");
                ExecSql(db, "INSERT INTO metadata VALUES ('format', 'jpg');");
                ExecSql(db, $"INSERT INTO metadata VALUES ('bounds', '{bounds}');");
                ExecSql(db, $"INSERT INTO metadata VALUES ('center', '{center}');");
                ExecSql(db, $"INSERT INTO metadata VALUES ('minzoom', '{minZoom}');");
                ExecSql(db, $"INSERT INTO metadata VALUES ('maxzoom', '{maxZoom}');");

                byte[] insSql = ToUtf8NullTerm("INSERT OR REPLACE INTO tiles (zoom_level, tile_column, tile_row, tile_data) VALUES (?, ?, ?, ?);");
                if (sqlite3_prepare_v2(db, insSql, insSql.Length, out IntPtr stmt, IntPtr.Zero) == 0)
                {
                    try
                    {
                        foreach (var t in tiles)
                        {
                            if (t.Data == null || t.Data.Length == 0) continue;
                            int tmsY = (1 << t.Z) - 1 - t.Y;
                            sqlite3_bind_int(stmt, 1, t.Z);
                            sqlite3_bind_int(stmt, 2, t.X);
                            sqlite3_bind_int(stmt, 3, tmsY);
                            sqlite3_bind_blob(stmt, 4, t.Data, t.Data.Length, IntPtr.Zero);
                            sqlite3_step(stmt);
                            sqlite3_reset(stmt);
                        }
                    }
                    finally { sqlite3_finalize(stmt); }
                }
                ExecSql(db, "COMMIT;");
            }
            finally { sqlite3_close(db); }
        }

        public static void WriteAtakMobacSqliteDatabase(string filePath, int minZoom, int maxZoom, List<(int Z, int X, int Y, byte[] Data)> tiles)
        {
            if (File.Exists(filePath)) File.Delete(filePath);
            if (sqlite3_open(ToUtf8NullTerm(filePath), out IntPtr db) != 0) return;
            try
            {
                ExecSql(db, "PRAGMA synchronous = OFF; PRAGMA journal_mode = MEMORY;");
                ExecSql(db, "CREATE TABLE info (minzoom INTEGER, maxzoom INTEGER);");
                ExecSql(db, "CREATE TABLE tiles (x int, y int, z int, s int, image blob, PRIMARY KEY (x,y,z,s));");
                ExecSql(db, "CREATE INDEX IND on tiles (x,y,z,s);");

                ExecSql(db, "BEGIN TRANSACTION;");
                ExecSql(db, $"INSERT INTO info VALUES ({17 - maxZoom}, {17 - minZoom});");

                byte[] insSql = ToUtf8NullTerm("INSERT OR REPLACE INTO tiles (x, y, z, s, image) VALUES (?, ?, ?, 0, ?);");
                if (sqlite3_prepare_v2(db, insSql, insSql.Length, out IntPtr stmt, IntPtr.Zero) == 0)
                {
                    try
                    {
                        foreach (var t in tiles)
                        {
                            if (t.Data == null || t.Data.Length == 0) continue;
                            int bigPlanetZ = 17 - t.Z;
                            sqlite3_bind_int(stmt, 1, t.X);
                            sqlite3_bind_int(stmt, 2, t.Y);
                            sqlite3_bind_int(stmt, 3, bigPlanetZ);
                            sqlite3_bind_blob(stmt, 4, t.Data, t.Data.Length, IntPtr.Zero);
                            sqlite3_step(stmt);
                            sqlite3_reset(stmt);
                        }
                    }
                    finally { sqlite3_finalize(stmt); }
                }
                ExecSql(db, "COMMIT;");
            }
            finally { sqlite3_close(db); }
        }
    }

    internal sealed class FastTacticalOverlayRenderer : FrameworkElement
    {
        private readonly Action<DrawingContext, double, double> _renderCallback;
        public FastTacticalOverlayRenderer(Action<DrawingContext, double, double> renderCallback)
        {
            _renderCallback = renderCallback;
            IsHitTestVisible = false;
            SnapsToDevicePixels = true;
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if (ActualWidth > 10 && ActualHeight > 10)
                _renderCallback?.Invoke(dc, ActualWidth, ActualHeight);
        }
    }

    public static class TacticalMapGeneratorView
    {
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
            new NatoSymbolOption { Code = "NATO_HQ",     ShortBadge = "HQ",  NatoMeaning = "Sztab / Dowództwo",             Label = "[HQ] NATO Sztab / Dowództwo" },
            new NatoSymbolOption { Code = "NATO_INF",    ShortBadge = "X",   NatoMeaning = "Piechota / Drużyna szturmowa",  Label = "[X] NATO Piechota / Drużyna" },
            new NatoSymbolOption { Code = "NATO_RECON",  ShortBadge = "/",   NatoMeaning = "Zwiad / Rozpoznanie (Recon)",   Label = "[/] NATO Zwiad / Recon" },
            new NatoSymbolOption { Code = "NATO_MED",    ShortBadge = "+",   NatoMeaning = "Punkt Medyczny / MEDEVAC",      Label = "[+] NATO Punkt Medyczny / MEDEVAC" },
            new NatoSymbolOption { Code = "NATO_COMMS",  ShortBadge = "⚡",  NatoMeaning = "Węzeł Łączności / Przekaźnik",  Label = "[⚡] NATO Węzeł Łączności / LoRa" },
            new NatoSymbolOption { Code = "NATO_SUPPLY", ShortBadge = "SUP", NatoMeaning = "Punkt Amunicyjny / Logistyka",  Label = "[SUP] NATO Punkt Amunicyjny / Logistyka" },
            new NatoSymbolOption { Code = "NATO_OP",     ShortBadge = "△",   NatoMeaning = "Punkt Obserwacyjny (OP)",       Label = "[△] NATO Punkt Obserwacyjny (OP)" },
            new NatoSymbolOption { Code = "NATO_SNIPER", ShortBadge = "⊕",   NatoMeaning = "Snajper / Strzelec Wyborowy",   Label = "[⊕] NATO Snajper / Strzelec" },
            new NatoSymbolOption { Code = "NATO_CP",     ShortBadge = "CP",  NatoMeaning = "Punkt Kontrolny / Respawn",     Label = "[CP] NATO Punkt Kontrolny / Respawn" },
            new NatoSymbolOption { Code = "NATO_LZ",     ShortBadge = "LZ",  NatoMeaning = "Strefa Lądowania / Zrzutu",     Label = "[LZ] NATO Strefa Lądowania / Zrzutu" },
            new NatoSymbolOption { Code = "NATO_OBJ",    ShortBadge = "OBJ", NatoMeaning = "Cel Operacji (Objective)",      Label = "[OBJ] NATO Cel Operacji (Objective)" },
            new NatoSymbolOption { Code = "NATO_HAZARD", ShortBadge = "!",   NatoMeaning = "Zagrożenie / Strefa No-Go",     Label = "[!] NATO Zagrożenie / Strefa No-Go" }
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

        private static readonly HttpClient Http = CreateHttpClient();
        private static readonly SemaphoreSlim TileThrottle = new SemaphoreSlim(6, 6);
        private static readonly ConcurrentDictionary<string, byte[]> RawTileCache = new ConcurrentDictionary<string, byte[]>();
        private static readonly ConcurrentDictionary<string, BitmapSource> DecodedImageCache = new ConcurrentDictionary<string, BitmapSource>();
        private static readonly ConcurrentDictionary<string, FormattedText> GlyphCache = new ConcurrentDictionary<string, FormattedText>();
        private static readonly Typeface BoldSegoeTypeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

        private static Window _hostWindow;
        private static UIElement _originalWindowContent;
        private static Border _fullScreenOverlay, _sheetFrameBorder;
        private static Grid _viewportHostGrid, _mapCompositeGrid;
        private static Canvas _tileCanvas, _interactiveCanvas;
        private static FastTacticalOverlayRenderer _fastOverlayRenderer;
        private static Rectangle _dimmerRect;
        private static Image _lidarOverlayImage;
        private static Button _tabBuilderBtn, _tabMapBtn, _btnToolPan, _btnToolMarker, _btnToolDot, _btnToolAo, _btnToolSector, _btnToolRoute;
        private static readonly List<Button> _scalePresetButtons = new List<Button>();
        private static TextBlock _statusText, _coordsBadge, _sheetInfoBadge, _offlineEstimateLabel, _offlineProgressLabel;
        private static ProgressBar _offlineProgressBar;
        private static StackPanel _objectsListStack;
        private static WrapPanel _factionsCheckboxesWrap;
        private static readonly HashSet<string> _selectedMapFactions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static CancellationTokenSource _renderCts, _offlineDownloadCts;

        private static double _centerLat = 49.9135, _centerLon = 19.0238, _dragStartLat, _dragStartLon;
        private static int _zoom = 16, _targetScaleDenominator = 5000;
        private static SheetFormatDef _selectedSheet = SheetFormats[0];
        private static string _sheetOrientation = "Poziomo";
        private static bool _showLidarSingleWms;

        private static int _offlineMinZoom = 13;
        private static int _offlineMaxZoom = 17;
        private static string _offlineBoundsMode = "Widoczny arkusz mapy";
        private static string _offlineDbFormat = "MBTiles (.mbtiles — ATAK 5.x)";
        private static bool _bakeLidarInOffline = true;
        private static bool _includeVectorsWithOffline = true;

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

        private static string _activeTool = "PAN";
        private static string _selectedObjectColorHex = "#2684FF";
        private static string _selectedNatoSymbolCode = "NATO_HQ";
        private static bool _showAllObjectLabels = true;
        private static bool _newObjectShowLabelDefault = true;
        private static string _aoOutPattern = "Skośne linie (Hatch ///)";
        private static double _aoOutMaskOpacity = 0.45;
        private static readonly List<TacticalMapObjectItem> _mapObjects = new List<TacticalMapObjectItem>();
        private static TacticalMapObjectItem _activeDraftPoly;
        private static TextBox _objectNameInputBox, _scaleInputBox, _searchPlaceBox;

        private static bool _showNorthArrow = true, _showScaleBar = true, _showLegend = true, _showNatoSymbolsInLegend = true;
        private static string _northArrowPos = "Lewy górny róg", _scaleBarPos = "Lewy dolny róg", _legendPos = "Prawy górny róg";
        private static double? _northNormX, _northNormY, _scaleNormX, _scaleNormY, _legendNormX, _legendNormY;
        private static string _kmzGrgMode = "Przezroczysta siatka";

        // Stan interakcji myszy, przesuwania całych stref (-2), pojedynczych wierzchołków (>=0) i przyciągania Shift co 45 stopni
        private static bool _isPanningMap;
        private static Point _mouseDownPoint;
        private static Point _currentHoverScreenPt;
        private static bool _isHoveringCanvas;
        private static TacticalMapObjectItem _draggedObject;
        private static int _draggedVertexIndex = -1; // >= 0: pojedynczy wierzchołek/znacznik, -2: przesuwanie całej strefy/sektora/trasy
        private static Point _markerDragScreenOffset;
        private static (double Lat, double Lon) _wholeShapeDragStartLatLon;
        private static List<(double Lat, double Lon)> _wholeShapeOriginalPoints;
        private static string _draggedHudId;
        private static FrameworkElement _draggedHudElement;
        private static Point _hudStartOffset;
        private static (double Lat, double Lon)? _rulerStart, _rulerEnd;
        private static TacticalMapLayerDef _activeLayer = Layers[0];

        private static HttpClient CreateHttpClient()
        {
            try
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
            catch
            {
                return new HttpClient { Timeout = TimeSpan.FromSeconds(16) };
            }
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

        // Zwraca rzeczywiste wymiary płótna wektorowego (eliminuje przesunięcie o grubość ramki Border)
        private static (double W, double H) GetExactCanvasSize()
        {
            if (_interactiveCanvas != null && _interactiveCanvas.ActualWidth > 60 && _interactiveCanvas.ActualHeight > 60)
                return (_interactiveCanvas.ActualWidth, _interactiveCanvas.ActualHeight);
            if (_sheetFrameBorder != null && _sheetFrameBorder.Width > 60 && _sheetFrameBorder.Height > 60)
                return (Math.Max(60, _sheetFrameBorder.Width - 3.0), Math.Max(60, _sheetFrameBorder.Height - 3.0));
            return (820.0, 580.0);
        }

        private static (double WidthMm, double HeightMm) GetActiveSheetDimensionsMm()
        {
            var s = _selectedSheet ?? SheetFormats[0];
            double w = Math.Max(s.WidthMm, s.HeightMm);
            double h = Math.Min(s.WidthMm, s.HeightMm);
            return _sheetOrientation == "Pionowo" ? (h, w) : (w, h);
        }

        private static double GetExactMetersPerPixel()
        {
            var (wMm, _) = GetActiveSheetDimensionsMm();
            var (canvasW, _) = GetExactCanvasSize();
            double realTerrainWidthMeters = (wMm / 1000.0) * _targetScaleDenominator;
            return Math.Max(0.04, realTerrainWidthMeters / canvasW);
        }

        private static double GetScaledTileSizePx()
        {
            double nativeTileMetersPerPx = (156543.03392 * Math.Cos(_centerLat * Math.PI / 180.0)) / Math.Pow(2.0, _zoom);
            double exactMetersPerPx = GetExactMetersPerPixel();
            return Math.Clamp(256.0 * (nativeTileMetersPerPx / exactMetersPerPx), 110.0, 640.0);
        }

        // Przyciąga punkt docelowy do wielokrotności 45 stopni (0°, 45°, 90°, 135°, 180°...) względem punktu bazowego, gdy wciśnięty jest Shift
        private static Point ApplyShift45DegreeSnap(Point anchorPt, Point rawTargetPt, out bool isSnapped, out int snappedAngleDeg)
        {
            isSnapped = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            snappedAngleDeg = 0;
            if (!isSnapped) return rawTargetPt;

            double dx = rawTargetPt.X - anchorPt.X;
            double dy = rawTargetPt.Y - anchorPt.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist < 2.0) return rawTargetPt;

            double angleRad = Math.Atan2(dy, dx);
            double stepRad = Math.PI / 4.0; // 45 stopni
            double snappedRad = Math.Round(angleRad / stepRad) * stepRad;
            snappedAngleDeg = ((int)Math.Round(snappedRad * 180.0 / Math.PI) % 360 + 360) % 360;

            return new Point(anchorPt.X + dist * Math.Cos(snappedRad), anchorPt.Y + dist * Math.Sin(snappedRad));
        }

        private static void UpdateSheetViewportSize()
        {
            if (_viewportHostGrid == null || _sheetFrameBorder == null) return;
            double hostW = _viewportHostGrid.ActualWidth > 300
                ? _viewportHostGrid.ActualWidth
                : Math.Max(720, (_hostWindow?.ActualWidth ?? 1280) - 460);
            double hostH = _viewportHostGrid.ActualHeight > 250
                ? _viewportHostGrid.ActualHeight
                : Math.Max(540, (_hostWindow?.ActualHeight ?? 800) - 120);

            double availW = Math.Max(260, hostW - 28);
            double availH = Math.Max(260, hostH - 56);

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

            UpdateOfflineTileEstimateUI();
        }

        private static Border CreateRedMinusDeleteButton(string tooltip, Action onClick)
        {
            var border = new Border
            {
                Width = 24,
                Height = 22,
                CornerRadius = new CornerRadius(5),
                Background = TacticalConfigPanel.BrushRedMinus,
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = tooltip,
                Margin = new Thickness(4, 0, 0, 0),
                Child = new TextBlock
                {
                    Text = "−",
                    Foreground = Brushes.White,
                    FontSize = 15,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, -2, 0, 0)
                }
            };
            border.MouseLeftButtonDown += (_, e) => { e.Handled = true; onClick(); };
            return border;
        }

        private static void RebuildFactionCheckboxesUI()
        {
            if (_factionsCheckboxesWrap == null) return;
            var configured = TacticalConfigPanel.GetConfiguredFactions();
            if (_selectedMapFactions.Count == 0 && configured.Count > 0)
                _selectedMapFactions.Add(configured[0].Name);

            _factionsCheckboxesWrap.Children.Clear();
            foreach (var fac in configured)
            {
                string fName = fac.Name;
                bool isChecked = _selectedMapFactions.Contains(fName);

                var badgeStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                badgeStack.Children.Add(new Border
                {
                    Width = 10,
                    Height = 10,
                    CornerRadius = new CornerRadius(2),
                    Background = TacticalConfigPanel.BrushFromHex(fac.ColorHex, Color.FromRgb(38, 132, 255)),
                    BorderBrush = Brushes.White,
                    BorderThickness = new Thickness(0.8),
                    Margin = new Thickness(0, 0, 5, 0)
                });
                badgeStack.Children.Add(new TextBlock { Text = fName, Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.Bold });

                var chk = new CheckBox
                {
                    Content = badgeStack,
                    IsChecked = isChecked,
                    Margin = new Thickness(0, 2, 12, 2),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Cursor = Cursors.Hand
                };
                chk.Checked += (_, __) => { _selectedMapFactions.Add(fName); RedrawVectorsOnly(); };
                chk.Unchecked += (_, __) => { _selectedMapFactions.Remove(fName); RedrawVectorsOnly(); };
                _factionsCheckboxesWrap.Children.Add(chk);
            }
        }

        private static List<(string Name, string Channel, string ColorHex, string Psk)> GetActiveMapFactions()
        {
            var all = TacticalConfigPanel.GetConfiguredFactions();
            var filtered = all.Where(f => _selectedMapFactions.Contains(f.Name)).ToList();
            return filtered.Count > 0 ? filtered : all.Take(1).ToList();
        }

        public static void AttachFullScreenTab(Window win)
        {
            if (Application.Current == null || win == null) return;
            _hostWindow = win;
            try
            {
                if (win.Content is FrameworkElement existingFe && existingFe.Tag?.ToString() == "FTH_MASTER_SHELL") return;
                if (win.Content is UIElement currentContent)
                {
                    var overlay = BuildFullScreenMapGeneratorUI(win);
                    overlay.Visibility = Visibility.Collapsed;

                    _originalWindowContent = currentContent;
                    win.Content = null;
                    _fullScreenOverlay = overlay;

                    var masterGrid = new Grid { Tag = "FTH_MASTER_SHELL", Background = TacticalConfigPanel.BrushCardBg };
                    masterGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });
                    masterGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                    var navBorder = new Border { Background = TacticalConfigPanel.BrushInputBg, BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(0, 0, 0, 1.5), Padding = new Thickness(16, 0, 16, 0) };
                    var navDock = new DockPanel();
                    var rightInfo = new TextBlock { Text = "Field TAK Hub 2.4  •  Silnik 60 FPS  •  Shift = Kąt co 45°  •  Przesuwanie Całych Stref (✥)", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
                    DockPanel.SetDock(rightInfo, Dock.Right);
                    navDock.Children.Add(rightInfo);

                    var tabsStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    _tabBuilderBtn = new Button { Content = "📦 Konfigurator Paczek (.ftak)", Height = 32, Padding = new Thickness(18, 0, 18, 0), Margin = new Thickness(0, 0, 10, 0), Background = TacticalConfigPanel.BrushButtonAccent, Foreground = TacticalConfigPanel.BrushDarkFg, BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1), FontWeight = FontWeights.Bold, FontSize = 12, Cursor = Cursors.Hand };
                    _tabMapBtn = new Button { Content = "🗺️ Generator Map Taktycznych 1:1 (Pełny Ekran)", Height = 32, Padding = new Thickness(18, 0, 18, 0), Background = TacticalConfigPanel.BrushItemBg, Foreground = TacticalConfigPanel.BrushAccent, BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1.5), FontWeight = FontWeights.Bold, FontSize = 12, Cursor = Cursors.Hand };

                    _tabBuilderBtn.Click += (_, __) => HideFullScreenMapTab();
                    _tabMapBtn.Click += (_, __) => ShowFullScreenMapTab(win);

                    tabsStack.Children.Add(_tabBuilderBtn); tabsStack.Children.Add(_tabMapBtn);
                    navDock.Children.Add(tabsStack); navBorder.Child = navDock;
                    Grid.SetRow(navBorder, 0); masterGrid.Children.Add(navBorder);

                    var contentHostGrid = new Grid();
                    contentHostGrid.Children.Add(_originalWindowContent);
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

                RebuildFactionCheckboxesUI();
                UpdateSheetViewportSize();
                ApplyScaleDenominator(_targetScaleDenominator);
                _ = _fullScreenOverlay.Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdateSheetViewportSize();
                    _ = RenderMapTilesAsync();
                }), DispatcherPriority.Loaded);
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
            _selectedSheet = SheetFormats[0];

            var rootBorder = new Border { Background = TacticalConfigPanel.BrushCardBg, Tag = "INJECTED_MAP_OVERLAY" };
            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });

            var bodyGrid = new Grid();
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(440) });
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var leftScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = TacticalConfigPanel.BrushCardBg, Padding = new Thickness(10) };
            var leftStack = new StackPanel { Margin = new Thickness(6) };

            // =====================================================================
            // SEKCJA 1: STRONY KONFLIKTU MAPY, FORMAT ARKUSZA (A3–A6 / MAPNIK), ORIENTACJA & SKALA 1:X
            // =====================================================================
            var sec1 = CreateSectionCard("1. STRONY KONFLIKTU, FORMAT ARKUSZA (A3–A6 / MAPNIK) & SKALA 1:X");

            var facSelectBorder = new Border { Background = TacticalConfigPanel.BrushInputBg, BorderBrush = TacticalConfigPanel.BrushInputBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 0, 6) };
            var facSelectStack = new StackPanel();
            var facHeaderDock = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var btnSyncFacs = MakeActionButton("🔄 Odśwież strony", false, () => { RebuildFactionCheckboxesUI(); RedrawVectorsOnly(); });
            btnSyncFacs.Height = 20; btnSyncFacs.FontSize = 9; btnSyncFacs.Padding = new Thickness(6, 0, 6, 0);
            DockPanel.SetDock(btnSyncFacs, Dock.Right);
            facHeaderDock.Children.Add(btnSyncFacs);
            facHeaderDock.Children.Add(new TextBlock { Text = "Strony konfliktu, których dotyczy mapa (zaznacz):", Foreground = TacticalConfigPanel.BrushAccent, FontSize = 10, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
            facSelectStack.Children.Add(facHeaderDock);

            _factionsCheckboxesWrap = new WrapPanel();
            RebuildFactionCheckboxesUI();
            facSelectStack.Children.Add(_factionsCheckboxesWrap);
            facSelectBorder.Child = facSelectStack;
            sec1.Children.Add(facSelectBorder);

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
            // SEKCJA 2: SIATKA MGRS / KEYPAD & KONTROLA CZYTELNOŚCI MAPY
            // =====================================================================
            var sec2 = CreateSectionCard("2. SIATKA MGRS / KEYPAD & KONTROLA CZYTELNOŚCI MAPY");
            sec2.Children.Add(TacticalConfigPanel.FieldBox("System współrzędnych i typ siatki na mapie:", TacticalConfigPanel.ComboCtrl(new[] { "MGRS / UTM (Siatka wojskowa w metrach)", "Siatka Sektorowa (A1, B2, C3...)", "WGS84 (Stopnie geograficzne Lat/Lon)", "Brak siatki" }, _coordSystem, win, v => { _coordSystem = v; _fastOverlayRenderer?.InvalidateVisual(); })));

            var subGridRow = new UniformGrid { Columns = 2 };
            subGridRow.Children.Add(TacticalConfigPanel.FieldBox("Podział wewnątrz 100 m:", TacticalConfigPanel.ComboCtrl(new[] { "Co 33 m (Keypad 1-9)", "Co 50 m (Ćwiartki A,B,C,D)", "Brak podziału (Tylko 100 m)", "Siatka co 250 m", "Siatka co 500 m" }, _subGridDivision, win, v => { _subGridDivision = v; _fastOverlayRenderer?.InvalidateVisual(); })));
            subGridRow.Children.Add(TacticalConfigPanel.FieldBox("Opisy kratek 100 m:", TacticalConfigPanel.ComboCtrl(new[] { "MGRS + Ćwiartki/Keypad", "Tylko główne linie MGRS", "Brak opisów wewnątrz" }, _gridLabelsMode, win, v => { _gridLabelsMode = v; _fastOverlayRenderer?.InvalidateVisual(); })));
            sec2.Children.Add(subGridRow);

            sec2.Children.Add(MakeSliderRow("Grubość linii siatki (głównej i pomocniczej):", 0.5, 3.0, _gridThicknessMultiplier, v => $"{v:0.1}x", v => { _gridThicknessMultiplier = Math.Round(v, 1); _fastOverlayRenderer?.InvalidateVisual(); }));
            sec2.Children.Add(MakeSliderRow("Rozmiar / grubość czcionki współrzędnych (MGRS i Keypad):", 0.7, 2.0, _fontScaleMultiplier, v => $"{v:0.1}x", v => { _fontScaleMultiplier = Math.Round(v, 1); GlyphCache.Clear(); _fastOverlayRenderer?.InvalidateVisual(); }));
            sec2.Children.Add(MakeSliderRow("Widoczność (alfa) głównych linii i opisów (MGRS / WGS84):", 0.05, 1.0, _mainGridOpacity, v => $"{(int)Math.Round(v * 100)}%", v => { _mainGridOpacity = Math.Round(v, 2); _fastOverlayRenderer?.InvalidateVisual(); }));
            sec2.Children.Add(MakeSliderRow("Widoczność (alfa) podziałek i cyfr (Keypad 1-9 / Ćwiartki):", 0.0, 1.0, _subGridOpacity, v => $"{(int)Math.Round(v * 100)}%", v => { _subGridOpacity = Math.Round(v, 2); _fastOverlayRenderer?.InvalidateVisual(); }));
            sec2.Children.Add(MakeSliderRow("Przyciemnienie podkładu mapy (większy kontrast pod siatkę):", 0.0, 0.65, _mapDimmerOpacity, v => $"{(int)Math.Round(v * 100)}%", v =>
            {
                _mapDimmerOpacity = Math.Round(v, 2);
                if (_dimmerRect != null) _dimmerRect.Opacity = _mapDimmerOpacity;
            }));

            var colAndHaloRow = new UniformGrid { Columns = 3 };
            colAndHaloRow.Children.Add(TacticalConfigPanel.FieldBox("Kolor siatki:", TacticalConfigPanel.ComboCtrl(new[] { "Żółty", "Turkusowy", "Biały", "Czerwony", "Czarny" }, _gridColorName, win, v => { _gridColorName = v; GlyphCache.Clear(); _fastOverlayRenderer?.InvalidateVisual(); })));
            colAndHaloRow.Children.Add(TacticalConfigPanel.FieldBox("Obrys linii (Halo):", TacticalConfigPanel.ComboCtrl(new[] { "Pełny czarny obrys", "Półprzezroczysty obrys", "Brak obrysu" }, _gridHaloMode, win, v => { _gridHaloMode = v; _fastOverlayRenderer?.InvalidateVisual(); })));
            colAndHaloRow.Children.Add(TacticalConfigPanel.FieldBox("Tło opisów i cyfr:", TacticalConfigPanel.ComboCtrl(new[] { "Kaseton (Czarne tło)", "Tylko obrys Halo", "Czysty tekst" }, _labelBgStyle, win, v => { _labelBgStyle = v; _fastOverlayRenderer?.InvalidateVisual(); })));
            sec2.Children.Add(colAndHaloRow);
            leftStack.Children.Add(WrapCard(sec2));

            // =====================================================================
            // SEKCJA 3: ZNACZNIKI (CENTROWANE 1:1), PRZESUWANIE CAŁYCH STREF (✥), ETYKIETY & SHIFT CO 45°
            // =====================================================================
            var sec3 = CreateSectionCard("3. ZNACZNIKI (1:1), PRZESUWANIE STREF (✥), ETYKIETY & SHIFT 45°");
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

            _btnToolPan = MakeActionButton("✋ Przesuwaj Mapę / Przeciągaj Znaczniki i Całe Strefy (✥)", true, () => { _activeTool = "PAN"; _activeDraftPoly = null; _fastOverlayRenderer?.InvalidateVisual(); UpdateToolButtonsVisual(); });
            _btnToolPan.Height = 30; _btnToolPan.Margin = new Thickness(0, 0, 0, 5);
            sec3.Children.Add(_btnToolPan);

            var toolsGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 6) };
            _btnToolMarker = MakeActionButton("🚩 Symbol NATO (Środek 1:1)", false, () => { _activeTool = "MARKER"; _activeDraftPoly = null; _fastOverlayRenderer?.InvalidateVisual(); UpdateToolButtonsVisual(); });
            _btnToolDot = MakeActionButton("⚫ Kropka (Kolor RGB)", false, () => { _activeTool = "DOT"; _activeDraftPoly = null; _fastOverlayRenderer?.InvalidateVisual(); UpdateToolButtonsVisual(); });
            _btnToolAo = MakeActionButton("🛡️ Teren Gry (Maska poza AO)", false, () => { _activeTool = "AO"; _activeDraftPoly = null; if (_objectNameInputBox != null) _objectNameInputBox.Text = "Granica Terenu Gry"; _fastOverlayRenderer?.InvalidateVisual(); UpdateToolButtonsVisual(); });
            _btnToolSector = MakeActionButton("⬠ Sektor / Strefa (Wypełniony)", false, () => { _activeTool = "SECTOR"; _activeDraftPoly = null; if (_objectNameInputBox != null) _objectNameInputBox.Text = $"Sektor-{_mapObjects.Count + 1}"; _fastOverlayRenderer?.InvalidateVisual(); UpdateToolButtonsVisual(); });

            _btnToolMarker.Height = 28; _btnToolMarker.Margin = new Thickness(0, 0, 3, 4);
            _btnToolDot.Height = 28; _btnToolDot.Margin = new Thickness(3, 0, 0, 4);
            _btnToolAo.Height = 28; _btnToolAo.Margin = new Thickness(0, 0, 3, 4);
            _btnToolSector.Height = 28; _btnToolSector.Margin = new Thickness(3, 0, 0, 4);

            toolsGrid.Children.Add(_btnToolMarker); toolsGrid.Children.Add(_btnToolDot);
            toolsGrid.Children.Add(_btnToolAo); toolsGrid.Children.Add(_btnToolSector);
            sec3.Children.Add(toolsGrid);

            _btnToolRoute = MakeActionButton("〰 Rysuj Trasę / Linię (Przytrzymaj SHIFT = kąt co 45° / 90°)", false, () => { _activeTool = "ROUTE"; _activeDraftPoly = null; if (_objectNameInputBox != null) _objectNameInputBox.Text = $"Trasa-{_mapObjects.Count + 1}"; _fastOverlayRenderer?.InvalidateVisual(); UpdateToolButtonsVisual(); });
            _btnToolRoute.Height = 26; _btnToolRoute.Margin = new Thickness(0, 0, 0, 6);
            sec3.Children.Add(_btnToolRoute);
            UpdateToolButtonsVisual();

            var aoMaskRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            aoMaskRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
            aoMaskRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var fAoPat = TacticalConfigPanel.FieldBox("Oznaczenie strefy POZA Terenem Gry (AO):", TacticalConfigPanel.ComboCtrl(new[]
            {
                "Skośne linie (Hatch ///)",
                "Symbole X (Strefa No-Go)",
                "Jednolite tło RGB poza AO",
                "Tylko linia granicy AO"
            }, _aoOutPattern, win, v => { _aoOutPattern = v; _fastOverlayRenderer?.InvalidateVisual(); }));
            var fAoOp = MakeSliderRow("Krycie maski poza AO:", 0.10, 0.85, _aoOutMaskOpacity, v => $"{(int)Math.Round(v * 100)}%", v => { _aoOutMaskOpacity = Math.Round(v, 2); _fastOverlayRenderer?.InvalidateVisual(); });
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
                    if (_objectNameInputBox != null) _objectNameInputBox.Text = opt.ShortBadge;
                }
            };
            sec3.Children.Add(TacticalConfigPanel.FieldBox("Kształt wektorowego znacznika taktycznego (Standard NATO APP-6):", natoCombo));

            var objCfgRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
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

            // Globalne i domyślne przełączniki widoczności etykiet dla wszystkich typów obiektów
            var lblTogglesGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 6) };
            var chkGlobalLabels = new CheckBox
            {
                Content = "Pokazuj etykiety nazw na mapie",
                IsChecked = _showAllObjectLabels,
                Foreground = TacticalConfigPanel.BrushAccent,
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                ToolTip = "Włącza lub wyłącza wyświetlanie etykiet tekstowych przy wszystkich obiektach (NATO, kropki, strefy, trasy)"
            };
            chkGlobalLabels.Checked += (_, __) => { _showAllObjectLabels = true; RedrawVectorsOnly(); };
            chkGlobalLabels.Unchecked += (_, __) => { _showAllObjectLabels = false; RedrawVectorsOnly(); };

            var chkNewObjLabel = new CheckBox
            {
                Content = "Domyślnie etykieta dla nowych",
                IsChecked = _newObjectShowLabelDefault,
                Foreground = Brushes.White,
                FontSize = 9.5,
                ToolTip = "Określa, czy nowo stawiane obiekty mają mieć domyślnie włączoną etykietę"
            };
            chkNewObjLabel.Checked += (_, __) => _newObjectShowLabelDefault = true;
            chkNewObjLabel.Unchecked += (_, __) => _newObjectShowLabelDefault = false;

            lblTogglesGrid.Children.Add(chkGlobalLabels);
            lblTogglesGrid.Children.Add(chkNewObjLabel);
            sec3.Children.Add(lblTogglesGrid);

            sec3.Children.Add(new TextBlock { Text = "Naniesione obiekty ([✓] = pokaż/ukryj etykietę, edytuj nazwę, kolor lub −):", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 9.5, Margin = new Thickness(0, 0, 0, 3) });
            var objListBorder = new Border { Background = TacticalConfigPanel.BrushInputBg, BorderBrush = TacticalConfigPanel.BrushInputBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), MinHeight = 52, MaxHeight = 140, Padding = new Thickness(6) };
            var objListScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _objectsListStack = new StackPanel();
            objListScroll.Content = _objectsListStack; objListBorder.Child = objListScroll;
            sec3.Children.Add(objListBorder);
            RefreshObjectsListUI(win);
            leftStack.Children.Add(WrapCard(sec3));

            // =====================================================================
            // SEKCJA 4: ELEMENTY MAPY (SŁOWNIK NATO W LEGENDZIE + DRAG & DROP) & EKSPORT ARKUSZA
            // =====================================================================
            var sec4 = CreateSectionCard("4. ELEMENTY MAPY (SŁOWNIK NATO W LEGENDZIE + DRAG & DROP) & EKSPORT");
            var hudChkGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 6) };
            CheckBox MakeHudChk(string label, bool init, Action<bool> onChg)
            {
                var c = new CheckBox { Content = label, IsChecked = init, Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 4, 4) };
                c.Checked += (_, __) => { onChg(true); RedrawVectorsOnly(); };
                c.Unchecked += (_, __) => { onChg(false); RedrawVectorsOnly(); };
                return c;
            }
            hudChkGrid.Children.Add(MakeHudChk("🧭 Znacznik Północy (N)", _showNorthArrow, v => _showNorthArrow = v));
            hudChkGrid.Children.Add(MakeHudChk("📏 Podziałka Skali", _showScaleBar, v => _showScaleBar = v));
            hudChkGrid.Children.Add(MakeHudChk("📋 Legenda na mapie", _showLegend, v => _showLegend = v));
            hudChkGrid.Children.Add(MakeHudChk("🎖️ Opis symboli NATO w legendzie", _showNatoSymbolsInLegend, v => _showNatoSymbolsInLegend = v));
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
            btnGpx.Height = 30; btnGpx.Margin = new Thickness(0, 0, 3, 0); btnGpx.BorderBrush = TacticalConfigPanel.BrushAccent;
            btnKmz.Height = 30; btnKmz.Margin = new Thickness(3, 0, 0, 0); btnKmz.BorderBrush = TacticalConfigPanel.BrushAccent;
            expRow1.Children.Add(btnGpx); expRow1.Children.Add(btnKmz);
            sec4.Children.Add(expRow1);

            var expRow2 = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 6) };
            var btnPdf = MakeActionButton("🖨️ Drukuj / PDF", false, async () => await ExportTacticalMapPdfAsync(win));
            var btnSaveJson = MakeActionButton("💾 Zapisz .json", false, () => SaveMapProjectJson());
            var btnLoadJson = MakeActionButton("📂 Wczytaj", false, () => LoadMapProjectJsonOrKml(win));
            btnPdf.Height = 28; btnPdf.Margin = new Thickness(0, 0, 3, 0); btnPdf.BorderBrush = TacticalConfigPanel.BrushAccent;
            btnSaveJson.Height = 28; btnSaveJson.Margin = new Thickness(2, 0, 2, 0);
            btnLoadJson.Height = 28; btnLoadJson.Margin = new Thickness(3, 0, 0, 0);
            expRow2.Children.Add(btnPdf); expRow2.Children.Add(btnSaveJson); expRow2.Children.Add(btnLoadJson);
            sec4.Children.Add(expRow2);

            var btnAddToFtak = MakeActionButton("⚡ Zapisz arkusz .KMZ / .GPX do paczki .ftak (dla zaznaczonych stron)", true, async () => await ExportMapToFtakSourceAsync(win));
            btnAddToFtak.Height = 30;
            sec4.Children.Add(btnAddToFtak);
            leftStack.Children.Add(WrapCard(sec4));

            // =====================================================================
            // SEKCJA 5: GENERATOR MAP OFFLINE MULTI-ZOOM (.MBTILES / .SQLITE DLA ATAK)
            // =====================================================================
            var sec5 = CreateSectionCard("5. GENERATOR MAP OFFLINE MULTI-ZOOM (.MBTILES / .SQLITE DLA ATAK)");
            sec5.Children.Add(new TextBlock
            {
                Text = "Pobiera piramidę kafelków na wielu przybliżeniach (np. Zoom 13–17) i buduje bazę danych SQLite do pracy w 100% offline w lesie bez zasięgu LTE.",
                Foreground = TacticalConfigPanel.BrushMutedText,
                FontSize = 9.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            });

            var zoomRangeGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 4) };
            zoomRangeGrid.Children.Add(TacticalConfigPanel.FieldBox("Od Zoom (Min — ogólny):", TacticalConfigPanel.ComboCtrl(new[] { "11", "12", "13", "14", "15", "16" }, _offlineMinZoom.ToString(), win, v =>
            {
                if (int.TryParse(v, out int z)) { _offlineMinZoom = z; if (_offlineMaxZoom < _offlineMinZoom) _offlineMaxZoom = _offlineMinZoom; UpdateOfflineTileEstimateUI(); }
            })));
            zoomRangeGrid.Children.Add(TacticalConfigPanel.FieldBox("Do Zoom (Max — detale):", TacticalConfigPanel.ComboCtrl(new[] { "14", "15", "16", "17", "18", "19" }, _offlineMaxZoom.ToString(), win, v =>
            {
                if (int.TryParse(v, out int z)) { _offlineMaxZoom = Math.Max(_offlineMinZoom, z); UpdateOfflineTileEstimateUI(); }
            })));
            sec5.Children.Add(zoomRangeGrid);

            var fmtAndBoundsGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 4) };
            fmtAndBoundsGrid.Children.Add(TacticalConfigPanel.FieldBox("Obszar pobierania:", TacticalConfigPanel.ComboCtrl(new[] { "Widoczny arkusz mapy", "Granica Terenu Gry (AO)" }, _offlineBoundsMode, win, v => { _offlineBoundsMode = v; UpdateOfflineTileEstimateUI(); })));
            fmtAndBoundsGrid.Children.Add(TacticalConfigPanel.FieldBox("Format bazy danych ATAK:", TacticalConfigPanel.ComboCtrl(new[] { "MBTiles (.mbtiles — ATAK 5.x)", "ATAK / MOBAC (.sqlite)", "Oba (.mbtiles + .sqlite)" }, _offlineDbFormat, win, v => _offlineDbFormat = v)));
            sec5.Children.Add(fmtAndBoundsGrid);

            var chkBakeLidar = new CheckBox
            {
                Content = "Wypal rzeźbę LiDAR ISOK bezpośrednio na kafelkach offline (jeśli LiDAR włączony)",
                IsChecked = _bakeLidarInOffline,
                Foreground = Brushes.White,
                FontSize = 9.5,
                Margin = new Thickness(0, 2, 0, 3)
            };
            chkBakeLidar.Checked += (_, __) => _bakeLidarInOffline = true;
            chkBakeLidar.Unchecked += (_, __) => _bakeLidarInOffline = false;
            sec5.Children.Add(chkBakeLidar);

            var chkIncludeVec = new CheckBox
            {
                Content = "Dołącz automatycznie warstwy wektorowe (.KMZ / .GPX) obok bazy offline",
                IsChecked = _includeVectorsWithOffline,
                Foreground = Brushes.White,
                FontSize = 9.5,
                Margin = new Thickness(0, 0, 0, 6)
            };
            chkIncludeVec.Checked += (_, __) => _includeVectorsWithOffline = true;
            chkIncludeVec.Unchecked += (_, __) => _includeVectorsWithOffline = false;
            sec5.Children.Add(chkIncludeVec);

            var estBorder = new Border { Background = TacticalConfigPanel.BrushInputBg, BorderBrush = TacticalConfigPanel.BrushTileBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 0, 6) };
            var estStack = new StackPanel();
            _offlineEstimateLabel = new TextBlock { Text = "Do pobrania: wyliczanie...", Foreground = TacticalConfigPanel.BrushAccent, FontSize = 10, FontWeight = FontWeights.Bold };
            _offlineProgressLabel = new TextBlock { Text = "Gotowy do budowy bazy offline.", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 9.5, Margin = new Thickness(0, 2, 0, 4) };
            _offlineProgressBar = new ProgressBar { Minimum = 0, Maximum = 100, Value = 0, Height = 8, Background = TacticalConfigPanel.BrushItemBg, Foreground = TacticalConfigPanel.BrushButtonAccent, BorderThickness = new Thickness(0) };
            estStack.Children.Add(_offlineEstimateLabel);
            estStack.Children.Add(_offlineProgressLabel);
            estStack.Children.Add(_offlineProgressBar);
            estBorder.Child = estStack;
            sec5.Children.Add(estBorder);

            var offBtnsRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            offBtnsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            offBtnsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

            var btnDownloadStandaloneOffline = MakeActionButton("📥 Pobierz bazę Offline (do release_maps)", false, async () => await BuildMultiZoomOfflineDatabaseAsync(win, false));
            btnDownloadStandaloneOffline.Height = 30; btnDownloadStandaloneOffline.Margin = new Thickness(0, 0, 4, 0); btnDownloadStandaloneOffline.BorderBrush = TacticalConfigPanel.BrushAccent;

            var btnCancelOffline = MakeActionButton("⏹ Przerwij", false, () => _offlineDownloadCts?.Cancel());
            btnCancelOffline.Height = 30; btnCancelOffline.Background = TacticalConfigPanel.BrushRedMinus; btnCancelOffline.BorderBrush = Brushes.White;

            Grid.SetColumn(btnDownloadStandaloneOffline, 0); Grid.SetColumn(btnCancelOffline, 1);
            offBtnsRow.Children.Add(btnDownloadStandaloneOffline); offBtnsRow.Children.Add(btnCancelOffline);
            sec5.Children.Add(offBtnsRow);

            var btnBuildOfflineToFtak = MakeActionButton("⚡ Buduj bazę Offline (.mbtiles / .sqlite) prosto do paczki .ftak", true, async () => await BuildMultiZoomOfflineDatabaseAsync(win, true));
            btnBuildOfflineToFtak.Height = 32;
            sec5.Children.Add(btnBuildOfflineToFtak);

            leftStack.Children.Add(WrapCard(sec5));
            leftScroll.Content = leftStack; Grid.SetColumn(leftScroll, 0); bodyGrid.Children.Add(leftScroll);

            // =====================================================================
            // PRAWA STRONA: SZYBKI SILNIK 60 FPS (_fastOverlayRenderer + _interactiveCanvas)
            // =====================================================================
            _viewportHostGrid = new Grid
            {
                Background = TacticalConfigPanel.BrushInputBg,
                ClipToBounds = true
            };

            _sheetFrameBorder = new Border
            {
                Width = 820,
                Height = 580,
                Background = TacticalConfigPanel.BrushCardBg,
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(1.5),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                ClipToBounds = true
            };

            _mapCompositeGrid = new Grid { ClipToBounds = true, Background = TacticalConfigPanel.BrushCardBg };
            _tileCanvas = new Canvas { ClipToBounds = true, IsHitTestVisible = false };
            _dimmerRect = new Rectangle { Fill = Brushes.Black, Opacity = _mapDimmerOpacity, IsHitTestVisible = false };
            _lidarOverlayImage = new Image { Stretch = Stretch.Fill, Opacity = _lidarOpacity, IsHitTestVisible = false };
            _fastOverlayRenderer = new FastTacticalOverlayRenderer(RenderFastGridAndMasksOnContext);
            _interactiveCanvas = new Canvas { Background = Brushes.Transparent, ClipToBounds = true, Cursor = Cursors.Cross };

            _mapCompositeGrid.Children.Add(_tileCanvas);
            _mapCompositeGrid.Children.Add(_lidarOverlayImage);
            _mapCompositeGrid.Children.Add(_dimmerRect);
            _mapCompositeGrid.Children.Add(_fastOverlayRenderer);
            _mapCompositeGrid.Children.Add(_interactiveCanvas);
            _sheetFrameBorder.Child = _mapCompositeGrid;
            _viewportHostGrid.Children.Add(_sheetFrameBorder);

            var topBadgesStack = new StackPanel { VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0), IsHitTestVisible = false };
            var sheetBadgeBorder = new Border { Background = new SolidColorBrush(Color.FromArgb(225, 8, 19, 22)), BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) };
            _sheetInfoBadge = new TextBlock { Text = "Arkusz A4 Poziomo", Foreground = TacticalConfigPanel.BrushAccent, FontSize = 10.5, FontWeight = FontWeights.Bold };
            sheetBadgeBorder.Child = _sheetInfoBadge;
            topBadgesStack.Children.Add(sheetBadgeBorder);

            var hudBorder = new Border { Background = new SolidColorBrush(Color.FromArgb(215, 8, 19, 22)), BorderBrush = TacticalConfigPanel.BrushTileBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Center };
            _coordsBadge = new TextBlock { Text = $"WGS84: {_centerLat:0.00000}, {_centerLon:0.00000}  |  SHIFT = Rysowanie co 45°  |  Uchwyt ✥ = Przesuń całą strefę", Foreground = Brushes.White, FontSize = 10 };
            hudBorder.Child = _coordsBadge;
            topBadgesStack.Children.Add(hudBorder);
            _viewportHostGrid.Children.Add(topBadgesStack);

            _interactiveCanvas.MouseEnter += (_, __) => _isHoveringCanvas = true;
            _interactiveCanvas.MouseLeave += (_, __) => { _isHoveringCanvas = false; if (_activeDraftPoly != null) _fastOverlayRenderer?.InvalidateVisual(); };

            _interactiveCanvas.MouseLeftButtonDown += (_, e) =>
            {
                if (_draggedHudId != null || _draggedObject != null) return;
                _isPanningMap = true;
                _mouseDownPoint = e.GetPosition(_interactiveCanvas);
                _dragStartLat = _centerLat;
                _dragStartLon = _centerLon;
                _interactiveCanvas.CaptureMouse();
            };

            _interactiveCanvas.MouseRightButtonDown += (_, e) =>
            {
                if (_activeDraftPoly != null)
                {
                    _activeDraftPoly = null;
                    RefreshObjectsListUI(win);
                    RedrawVectorsOnly();
                    UpdateOfflineTileEstimateUI();
                    return;
                }
                var (w, h) = GetExactCanvasSize();
                Point p = e.GetPosition(_interactiveCanvas);
                var pt = ScreenToLatLon(p.X, p.Y, w, h);
                if (_rulerStart == null || (_rulerStart != null && _rulerEnd != null)) { _rulerStart = pt; _rulerEnd = null; }
                else _rulerEnd = pt;
                _fastOverlayRenderer?.InvalidateVisual();
            };

            _interactiveCanvas.MouseMove += (_, e) =>
            {
                var (w, h) = GetExactCanvasSize();
                Point p = e.GetPosition(_interactiveCanvas);
                _currentHoverScreenPt = p;
                var (hoverLat, hoverLon) = ScreenToLatLon(p.X, p.Y, w, h);
                if (_coordsBadge != null)
                    _coordsBadge.Text = $"WGS84: {hoverLat:0.00000}, {hoverLon:0.00000}  |  Skala 1:{_targetScaleDenominator:N0} ({GetExactMetersPerPixel():0.00} m/px)  |  SHIFT = Kąt co 45°  |  ✥ = Przesuń strefę";

                // 1. Przeciąganie elementu HUD (Północ / Skala / Legenda)
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

                // 2. Przeciąganie CAŁEJ strefy / sektora / trasy za centralny uchwyt ✥ (_draggedVertexIndex == -2)
                if (_draggedObject != null && _draggedVertexIndex == -2 && _wholeShapeOriginalPoints != null)
                {
                    double dLat = hoverLat - _wholeShapeDragStartLatLon.Lat;
                    double dLon = hoverLon - _wholeShapeDragStartLatLon.Lon;
                    for (int i = 0; i < _wholeShapeOriginalPoints.Count && i < _draggedObject.Points.Count; i++)
                    {
                        _draggedObject.Points[i] = (
                            Math.Clamp(_wholeShapeOriginalPoints[i].Lat + dLat, -85.0, 85.0),
                            Math.Clamp(_wholeShapeOriginalPoints[i].Lon + dLon, -180.0, 180.0));
                    }
                    RedrawVectorsOnly();
                    return;
                }

                // 3. Przeciąganie pojedynczego wierzchołka lub znacznika (z zachowaniem offsetu kliknięcia i opcjonalnym Shift 45°)
                if (_draggedObject != null && _draggedVertexIndex >= 0 && _draggedVertexIndex < _draggedObject.Points.Count)
                {
                    Point targetScreenPt = new Point(p.X - _markerDragScreenOffset.X, p.Y - _markerDragScreenOffset.Y);

                    // Jeśli przeciągamy wierzchołek wielokąta/trasy z wciśniętym klawiszem Shift, przyciągnij kąt co 45° względem sąsiedniego punktu
                    if (_draggedObject.Points.Count >= 2 && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                    {
                        int refIdx = _draggedVertexIndex > 0 ? _draggedVertexIndex - 1 : 1;
                        Point refScreenPt = LatLonToScreen(_draggedObject.Points[refIdx].Lat, _draggedObject.Points[refIdx].Lon, w, h);
                        targetScreenPt = ApplyShift45DegreeSnap(refScreenPt, targetScreenPt, out _, out _);
                    }

                    _draggedObject.Points[_draggedVertexIndex] = ScreenToLatLon(targetScreenPt.X, targetScreenPt.Y, w, h);
                    RedrawVectorsOnly();
                    return;
                }

                // 4. Podgląd rysowanego odcinka na żywo (z przyciąganiem Shift co 45°)
                if (_activeDraftPoly != null && _activeDraftPoly.Points.Count > 0)
                {
                    _fastOverlayRenderer?.InvalidateVisual();
                }

                // 5. Przesuwanie mapy (60 FPS)
                if (_isPanningMap && _activeTool == "PAN")
                {
                    double dx = p.X - _mouseDownPoint.X, dy = p.Y - _mouseDownPoint.Y;
                    double scaledWorldSize = GetScaledTileSizePx() * Math.Pow(2, _zoom);
                    _centerLon = Math.Clamp(_dragStartLon - dx * (360.0 / scaledWorldSize), -180.0, 180.0);
                    _centerLat = Math.Clamp(_dragStartLat + dy * ((360.0 / scaledWorldSize) * Math.Cos(_dragStartLat * Math.PI / 180.0)), -85.0, 85.0);
                    _tileCanvas.RenderTransform = new TranslateTransform(dx, dy);
                    _fastOverlayRenderer?.InvalidateVisual();
                }
            };

            _interactiveCanvas.MouseLeftButtonUp += (_, e) =>
            {
                if (_draggedHudId != null)
                {
                    _draggedHudId = null;
                    _draggedHudElement = null;
                    _interactiveCanvas.ReleaseMouseCapture();
                    return;
                }
                if (_draggedObject != null)
                {
                    _draggedObject = null;
                    _draggedVertexIndex = -1;
                    _wholeShapeOriginalPoints = null;
                    _interactiveCanvas.ReleaseMouseCapture();
                    RedrawVectorsOnly();
                    UpdateOfflineTileEstimateUI();
                    return;
                }
                if (!_isPanningMap) return;
                _isPanningMap = false;
                _interactiveCanvas.ReleaseMouseCapture();
                _tileCanvas.RenderTransform = Transform.Identity;

                Point endPt = e.GetPosition(_interactiveCanvas);
                if ((endPt - _mouseDownPoint).Length < 5.0 && _activeTool != "PAN")
                {
                    HandleMapClickToolAction(win, endPt);
                    RedrawVectorsOnly();
                    UpdateOfflineTileEstimateUI();
                }
                else
                {
                    RedrawVectorsOnly();
                    UpdateOfflineTileEstimateUI();
                    _ = RenderMapTilesAsync();
                }
            };

            _interactiveCanvas.MouseWheel += (_, e) =>
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
            _statusText = new TextBlock { Text = "Gotowy. Ikony centrowane 1:1 w miejscu kliknięcia, przesuwanie całych stref (uchwyt ✥), wyłączanie etykiet oraz rysowanie co 45° z klawiszem SHIFT.", Foreground = TacticalConfigPanel.BrushAccent, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            bottomBar.Child = _statusText; Grid.SetRow(bottomBar, 1); mainGrid.Children.Add(bottomBar);

            UpdateOfflineTileEstimateUI();
            rootBorder.Child = mainGrid;
            return rootBorder;
        }

        // =========================================================================
        // SILNIK POBIERANIA I BUDOWY MAP OFFLINE MULTI-ZOOM (.MBTILES / .SQLITE)
        // =========================================================================
        private static (double North, double South, double East, double West) GetTargetOfflineBoundsLatLon()
        {
            var aoPoly = _mapObjects.FirstOrDefault(o => o.Kind == "AO" && o.Points.Count >= 3);
            if (_offlineBoundsMode.Contains("AO") && aoPoly != null)
            {
                double n = aoPoly.Points.Max(p => p.Lat);
                double s = aoPoly.Points.Min(p => p.Lat);
                double e = aoPoly.Points.Max(p => p.Lon);
                double w = aoPoly.Points.Min(p => p.Lon);
                return (n, s, e, w);
            }

            var (vw, vh) = GetExactCanvasSize();
            var (nwLat, nwLon) = ScreenToLatLon(0, 0, vw, vh);
            var (seLat, seLon) = ScreenToLatLon(vw, vh, vw, vh);
            return (Math.Max(nwLat, seLat), Math.Min(nwLat, seLat), Math.Max(nwLon, seLon), Math.Min(nwLon, seLon));
        }

        private static List<(int Z, int X, int Y)> CalculateOfflineTilePyramidCoords(int minZ, int maxZ, out double north, out double south, out double east, out double west)
        {
            (north, south, east, west) = GetTargetOfflineBoundsLatLon();
            var list = new List<(int Z, int X, int Y)>();
            int zStart = Math.Min(minZ, maxZ);
            int zEnd = Math.Max(minZ, maxZ);

            for (int z = zStart; z <= zEnd; z++)
            {
                var (x1, y1) = LatLonToTileXY(north, west, z);
                var (x2, y2) = LatLonToTileXY(south, east, z);
                int maxIdx = (1 << z) - 1;
                int minTx = Math.Clamp((int)Math.Floor(Math.Min(x1, x2)), 0, maxIdx);
                int maxTx = Math.Clamp((int)Math.Floor(Math.Max(x1, x2)), 0, maxIdx);
                int minTy = Math.Clamp((int)Math.Floor(Math.Min(y1, y2)), 0, maxIdx);
                int maxTy = Math.Clamp((int)Math.Floor(Math.Max(y1, y2)), 0, maxIdx);

                for (int tx = minTx; tx <= maxTx; tx++)
                {
                    for (int ty = minTy; ty <= maxTy; ty++)
                    {
                        list.Add((z, tx, ty));
                        if (list.Count >= 4500) return list;
                    }
                }
            }
            return list;
        }

        private static void UpdateOfflineTileEstimateUI()
        {
            if (_offlineEstimateLabel == null) return;
            try
            {
                var coords = CalculateOfflineTilePyramidCoords(_offlineMinZoom, _offlineMaxZoom, out _, out _, out _, out _);
                double estMb = (coords.Count * 26.0) / 1024.0;
                string capNote = coords.Count >= 4500 ? " [Limit bezpieczeństwa 4500]" : "";
                _offlineEstimateLabel.Text = $"Do pobrania: {coords.Count} kafelków (Zoom {_offlineMinZoom}–{_offlineMaxZoom}){capNote}  •  ~{estMb:0.0} MB";
            }
            catch { }
        }

        private static byte[] BlendBaseAndLidarTilesOnUiThread(byte[] baseBytes, byte[] lidarBytes, double lidarAlpha)
        {
            try
            {
                BitmapImage bmpBase = new BitmapImage();
                using (var ms1 = new MemoryStream(baseBytes)) { bmpBase.BeginInit(); bmpBase.CacheOption = BitmapCacheOption.OnLoad; bmpBase.StreamSource = ms1; bmpBase.EndInit(); bmpBase.Freeze(); }

                BitmapImage bmpLidar = new BitmapImage();
                using (var ms2 = new MemoryStream(lidarBytes)) { bmpLidar.BeginInit(); bmpLidar.CacheOption = BitmapCacheOption.OnLoad; bmpLidar.StreamSource = ms2; bmpLidar.EndInit(); bmpLidar.Freeze(); }

                var dv = new DrawingVisual();
                using (DrawingContext dc = dv.RenderOpen())
                {
                    dc.DrawImage(bmpBase, new Rect(0, 0, 256, 256));
                    dc.PushOpacity(lidarAlpha);
                    dc.DrawImage(bmpLidar, new Rect(0, 0, 256, 256));
                    dc.Pop();
                }
                var rtb = new RenderTargetBitmap(256, 256, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                var enc = new JpegBitmapEncoder { QualityLevel = 90 };
                enc.Frames.Add(BitmapFrame.Create(rtb));
                using var outMs = new MemoryStream();
                enc.Save(outMs);
                return outMs.ToArray();
            }
            catch { return baseBytes; }
        }

        private static async Task<byte[]> FetchSingleOfflineTileBytesAsync(TacticalMapLayerDef layer, int tx, int ty, int zoom, CancellationToken ct)
        {
            string url = BuildTileUrl(layer, tx, ty, zoom);
            if (RawTileCache.TryGetValue(url, out var cached) && IsValidImagePayload(cached))
                return cached;

            string fallbackUrl = (!string.IsNullOrEmpty(layer.FallbackUrlTemplate) && layer.IsWmsEpsg3857)
                ? layer.FallbackUrlTemplate.Replace("{bbox}", GetEpsg3857BBox(tx, ty, zoom)).Replace("{w}", "256").Replace("{h}", "256")
                : null;

            await TileThrottle.WaitAsync(ct);
            try
            {
                for (int attempt = 0; attempt < 2 && !ct.IsCancellationRequested; attempt++)
                {
                    try
                    {
                        byte[] data = await Http.GetByteArrayAsync(url, ct);
                        if (IsValidImagePayload(data))
                        {
                            if (RawTileCache.Count < 1500) RawTileCache[url] = data;
                            return data;
                        }
                    }
                    catch { }
                    if (attempt == 0) await Task.Delay(80, ct);
                }

                if (!string.IsNullOrEmpty(fallbackUrl) && !ct.IsCancellationRequested)
                {
                    try
                    {
                        byte[] fb = await Http.GetByteArrayAsync(fallbackUrl, ct);
                        if (IsValidImagePayload(fb))
                        {
                            if (RawTileCache.Count < 1500) RawTileCache[url] = fb;
                            return fb;
                        }
                    }
                    catch { }
                }
            }
            finally { TileThrottle.Release(); }
            return null;
        }

        private static async Task BuildMultiZoomOfflineDatabaseAsync(Window win, bool saveDirectlyToFtakSource)
        {
            _offlineDownloadCts?.Cancel();
            var cts = new CancellationTokenSource();
            _offlineDownloadCts = cts;
            var ct = cts.Token;

            try
            {
                var coords = CalculateOfflineTilePyramidCoords(_offlineMinZoom, _offlineMaxZoom, out double north, out double south, out double east, out double west);
                if (coords.Count == 0) return;

                if (_offlineProgressBar != null) { _offlineProgressBar.Maximum = coords.Count; _offlineProgressBar.Value = 0; }
                if (_offlineProgressLabel != null) _offlineProgressLabel.Text = $"Pobieranie {coords.Count} kafelków (Zoom {_offlineMinZoom}–{_offlineMaxZoom})...";

                var lidarLayer = Layers.First(l => l.Id == "geoportal_lidar");
                bool doBakeLidar = _bakeLidarInOffline && _showLidarSingleWms && _activeLayer.Id != "geoportal_lidar";

                var downloadedTiles = new ConcurrentBag<(int Z, int X, int Y, byte[] Data)>();
                int completed = 0;

                var jobs = coords.Select(async coord =>
                {
                    if (ct.IsCancellationRequested) return;
                    byte[] baseData = await FetchSingleOfflineTileBytesAsync(_activeLayer, coord.X, coord.Y, coord.Z, ct);
                    if (baseData != null && doBakeLidar && !ct.IsCancellationRequested)
                    {
                        byte[] lidarData = await FetchSingleOfflineTileBytesAsync(lidarLayer, coord.X, coord.Y, coord.Z, ct);
                        if (lidarData != null && Application.Current != null)
                        {
                            baseData = await Application.Current.Dispatcher.InvokeAsync(
                                () => BlendBaseAndLidarTilesOnUiThread(baseData, lidarData, _lidarOpacity),
                                DispatcherPriority.Background);
                        }
                    }
                    if (baseData != null)
                        downloadedTiles.Add((coord.Z, coord.X, coord.Y, baseData));

                    int done = Interlocked.Increment(ref completed);
                    if (done % 5 == 0 || done == coords.Count)
                    {
                        _ = Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (_offlineProgressBar != null) _offlineProgressBar.Value = done;
                            if (_offlineProgressLabel != null) _offlineProgressLabel.Text = $"Pobrano {done} / {coords.Count} kafelków ({(done * 100) / Math.Max(1, coords.Count)}%)...";
                        }));
                    }
                });

                await Task.WhenAll(jobs);
                if (ct.IsCancellationRequested)
                {
                    if (_offlineProgressLabel != null) _offlineProgressLabel.Text = "⏹ Przerwano pobieranie mapy offline.";
                    return;
                }

                var finalTilesList = downloadedTiles.ToList();
                string sourceDir = TacticalConfigPanel.GetPrimarySourceDirectory(win);
                string eventName = TacticalConfigPanel.GetEventNameFromWindow(win);
                var allConfigured = TacticalConfigPanel.GetConfiguredFactions();
                var targetFacs = GetActiveMapFactions();
                string facSuffix = string.Join("_", targetFacs.Select(f => TacticalConfigPanel.SanitizeFileName(f.Name)));
                string baseFileName = $"Offline_{eventName}_{facSuffix}_Z{_offlineMinZoom}_{_offlineMaxZoom}";

                if (_offlineProgressLabel != null) _offlineProgressLabel.Text = $"Zapisywanie {finalTilesList.Count} kafelków do bazy SQLite...";

                bool writeMbtiles = _offlineDbFormat.Contains(".mbtiles") || _offlineDbFormat.StartsWith("Oba");
                bool writeSqlite = _offlineDbFormat.Contains(".sqlite") || _offlineDbFormat.StartsWith("Oba");

                string folderToOpen = "";

                if (saveDirectlyToFtakSource)
                {
                    await Task.Run(() =>
                    {
                        if (targetFacs.Count > 0 && targetFacs.Count < allConfigured.Count)
                        {
                            foreach (var fac in targetFacs)
                            {
                                string sl = TacticalConfigPanel.SanitizeFileName(fac.Name).ToLowerInvariant();
                                string facDir = System.IO.Path.Combine(sourceDir, "factions", sl);
                                Directory.CreateDirectory(facDir);
                                if (writeMbtiles)
                                    NativeSqliteWriter.WriteMbTilesDatabase(System.IO.Path.Combine(facDir, $"mapa_offline_{sl}.mbtiles"), $"{eventName}_{fac.Name}", _offlineMinZoom, _offlineMaxZoom, north, south, east, west, finalTilesList);
                                if (writeSqlite)
                                    NativeSqliteWriter.WriteAtakMobacSqliteDatabase(System.IO.Path.Combine(facDir, $"mapa_offline_{sl}.sqlite"), _offlineMinZoom, _offlineMaxZoom, finalTilesList);
                                folderToOpen = facDir;
                            }
                        }
                        else
                        {
                            string mapsDir = System.IO.Path.Combine(sourceDir, "maps");
                            Directory.CreateDirectory(mapsDir);
                            if (writeMbtiles)
                                NativeSqliteWriter.WriteMbTilesDatabase(System.IO.Path.Combine(mapsDir, $"{baseFileName}.mbtiles"), eventName, _offlineMinZoom, _offlineMaxZoom, north, south, east, west, finalTilesList);
                            if (writeSqlite)
                                NativeSqliteWriter.WriteAtakMobacSqliteDatabase(System.IO.Path.Combine(mapsDir, $"{baseFileName}.sqlite"), _offlineMinZoom, _offlineMaxZoom, finalTilesList);
                            folderToOpen = mapsDir;
                        }
                    });

                    if (_includeVectorsWithOffline)
                        await ExportMapToFtakSourceAsync(win);
                    else
                        await TacticalConfigPanel.RefreshSourceSizeFromExternalAsync(win);

                    if (_offlineProgressLabel != null) _offlineProgressLabel.Text = $"✓ Zapisano bazę offline ({finalTilesList.Count} kafelków) w folderze source paczki .ftak!";
                    if (_statusText != null) _statusText.Text = $"✓ Zbudowano wielowarstwową mapę offline ({finalTilesList.Count} kafelków, Zoom {_offlineMinZoom}–{_offlineMaxZoom}) bezpośrednio do paczki .ftak!";
                }
                else
                {
                    string parentDir = System.IO.Path.GetDirectoryName(System.IO.Path.TrimEndingDirectorySeparator(sourceDir)) ?? AppDomain.CurrentDomain.BaseDirectory;
                    string outDir = System.IO.Path.Combine(parentDir, "release_maps");
                    Directory.CreateDirectory(outDir);
                    folderToOpen = outDir;

                    await Task.Run(() =>
                    {
                        if (writeMbtiles)
                            NativeSqliteWriter.WriteMbTilesDatabase(System.IO.Path.Combine(outDir, $"{baseFileName}.mbtiles"), eventName, _offlineMinZoom, _offlineMaxZoom, north, south, east, west, finalTilesList);
                        if (writeSqlite)
                            NativeSqliteWriter.WriteAtakMobacSqliteDatabase(System.IO.Path.Combine(outDir, $"{baseFileName}.sqlite"), _offlineMinZoom, _offlineMaxZoom, finalTilesList);
                    });

                    if (_includeVectorsWithOffline)
                        await ExportStandaloneFilesAsync(win, "kmz");
                    else
                        TacticalConfigPanel.TryOpenFolderInExplorer(folderToOpen);

                    if (_offlineProgressLabel != null) _offlineProgressLabel.Text = $"✓ Wyeksportowano bazę offline ({finalTilesList.Count} kafelków) do release_maps!";
                    if (_statusText != null) _statusText.Text = $"✓ Zapisano bazę offline ({baseFileName}) w folderze: {outDir}";
                }
            }
            catch (Exception ex)
            {
                if (_offlineProgressLabel != null) _offlineProgressLabel.Text = $"Błąd zapisu: {ex.Message}";
            }
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

            var sl = new Slider
            {
                Minimum = min,
                Maximum = max,
                Value = initVal,
                IsMoveToPointEnabled = true,
                SmallChange = (max - min) / 100.0,
                LargeChange = (max - min) / 20.0,
                Cursor = Cursors.Hand
            };

            void SetSliderFromMouse(MouseEventArgs e)
            {
                if (sl.ActualWidth <= 8) return;
                double ratio = Math.Clamp(e.GetPosition(sl).X / sl.ActualWidth, 0.0, 1.0);
                sl.Value = min + ratio * (max - min);
            }

            sl.PreviewMouseLeftButtonDown += (_, e) => SetSliderFromMouse(e);
            sl.PreviewMouseMove += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed) SetSliderFromMouse(e); };
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

        // Dodaje nowy punkt / wierzchołek z uwzględnieniem dokładnego rozmiaru płótna oraz przyciągania Shift co 45 stopni
        private static void HandleMapClickToolAction(Window win, Point clickPt)
        {
            var (w, h) = GetExactCanvasSize();
            string objName = string.IsNullOrWhiteSpace(_objectNameInputBox?.Text) ? $"Punkt-{_mapObjects.Count + 1}" : _objectNameInputBox.Text.Trim();

            if (_activeTool == "MARKER" || _activeTool == "DOT")
            {
                var (clickLat, clickLon) = ScreenToLatLon(clickPt.X, clickPt.Y, w, h);
                var item = new TacticalMapObjectItem
                {
                    Kind = _activeTool == "MARKER" ? "Marker" : "Dot",
                    SymbolCode = _activeTool == "MARKER" ? _selectedNatoSymbolCode : "DOT",
                    Name = objName,
                    ColorHex = _selectedObjectColorHex,
                    ShowLabel = _newObjectShowLabelDefault
                };
                item.Points.Add((clickLat, clickLon));
                _mapObjects.Add(item);
            }
            else if (_activeTool == "AO" || _activeTool == "SECTOR" || _activeTool == "ROUTE")
            {
                string kind = _activeTool == "AO" ? "AO" : (_activeTool == "SECTOR" ? "Sector" : "Route");
                if (_activeDraftPoly == null || _activeDraftPoly.Kind != kind)
                {
                    _activeDraftPoly = new TacticalMapObjectItem
                    {
                        Kind = kind,
                        SymbolCode = kind,
                        Name = kind == "AO" ? "Granica Terenu Gry (AO)" : objName,
                        ColorHex = _selectedObjectColorHex,
                        ShowLabel = _newObjectShowLabelDefault
                    };
                    _mapObjects.Add(_activeDraftPoly);
                }

                Point effectivePt = clickPt;
                if (_activeDraftPoly.Points.Count > 0)
                {
                    var lastLatLon = _activeDraftPoly.Points[_activeDraftPoly.Points.Count - 1];
                    Point lastScreenPt = LatLonToScreen(lastLatLon.Lat, lastLatLon.Lon, w, h);
                    effectivePt = ApplyShift45DegreeSnap(lastScreenPt, clickPt, out _, out _);
                }

                var (ptLat, ptLon) = ScreenToLatLon(effectivePt.X, effectivePt.Y, w, h);
                _activeDraftPoly.Points.Add((ptLat, ptLon));
            }
            RefreshObjectsListUI(win);
        }

        // Lista naniesionych obiektów z indywidualnym checkboxem widoczności etykiety [✓], edycją nazwy, kolorem i czerwonym minusem "−"
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
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) }); // Checkbox etykiety
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) }); // Kolor RGB
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) }); // Typ
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Nazwa
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) }); // Usuń (−)

                var chkShowLbl = new CheckBox
                {
                    IsChecked = captured.ShowLabel,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Cursor = Cursors.Hand,
                    ToolTip = "Pokaż / ukryj etykietę tekstową tego obiektu na mapie"
                };
                chkShowLbl.Checked += (_, __) => { captured.ShowLabel = true; RedrawVectorsOnly(); };
                chkShowLbl.Unchecked += (_, __) => { captured.ShowLabel = false; RedrawVectorsOnly(); };
                Grid.SetColumn(chkShowLbl, 0);
                row.Children.Add(chkShowLbl);

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
                Grid.SetColumn(btnCol, 1);
                row.Children.Add(btnCol);

                string badgeTxt = captured.Kind == "AO" ? "[AO]" : (captured.Kind == "Sector" ? "[SEKTOR]" : $"[{captured.SymbolCode.Replace("NATO_", "")}]");
                var kindLbl = new TextBlock { Text = badgeTxt, Foreground = TacticalConfigPanel.BrushAccent, FontSize = 9, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 4, 0) };
                Grid.SetColumn(kindLbl, 2);
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
                Grid.SetColumn(editNameBox, 3);
                row.Children.Add(editNameBox);

                var btnMinusDel = CreateRedMinusDeleteButton($"Usuń obiekt: {captured.Name}", () =>
                {
                    _mapObjects.Remove(captured);
                    if (_activeDraftPoly == captured) _activeDraftPoly = null;
                    RefreshObjectsListUI(win);
                    RedrawVectorsOnly();
                    UpdateOfflineTileEstimateUI();
                });
                Grid.SetColumn(btnMinusDel, 4);
                row.Children.Add(btnMinusDel);

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
                    UpdateOfflineTileEstimateUI();
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
                if (DecodedImageCache.Count > 260) DecodedImageCache.Clear();
                if (RawTileCache.Count > 1200) RawTileCache.Clear();

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

        private static FormattedText GetCachedGlyph(string text, double fontSize, Brush fg, string keyPrefix)
        {
            string key = $"{keyPrefix}_{text}_{fontSize:0.0}";
            if (GlyphCache.TryGetValue(key, out var ft)) return ft;
            if (GlyphCache.Count > 500) GlyphCache.Clear();
            var created = new FormattedText(text ?? "", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, BoldSegoeTypeface, fontSize, fg, 1.0);
            GlyphCache[key] = created;
            return created;
        }

        private static void DrawFastBadgeOnContext(DrawingContext dc, string text, double x, double y, Brush fg, double fontSize, double opacity, string cachePrefix)
        {
            if (string.IsNullOrEmpty(text) || opacity <= 0.02) return;
            var ft = GetCachedGlyph(text, fontSize, fg, cachePrefix);
            if (_labelBgStyle != "Czysty tekst")
            {
                byte bgAlpha = (byte)Math.Clamp((int)Math.Round((_labelBgStyle.StartsWith("Kaseton") ? 175 : 85) * opacity), 0, 230);
                var bgBrush = new SolidColorBrush(Color.FromArgb(bgAlpha, 5, 12, 15)); bgBrush.Freeze();
                Pen borderPen = null;
                if (_labelBgStyle.StartsWith("Kaseton"))
                {
                    borderPen = new Pen(fg, 0.7); borderPen.Freeze();
                }
                dc.DrawRoundedRectangle(bgBrush, borderPen, new Rect(x - 3, y - 1, ft.Width + 6, ft.Height + 2), 2.5, 2.5);
            }
            dc.DrawText(ft, new Point(x, y));
        }

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

        private static void RenderFastGridAndMasksOnContext(DrawingContext dc, double w, double h)
        {
            // 1. Teren Gry (AO) — odwrócona maska poza obszarem gry
            foreach (var ao in _mapObjects.Where(o => o.Kind == "AO" && o.Points.Count >= 2))
            {
                Color c = Color.FromRgb(235, 64, 52);
                try { c = (Color)ColorConverter.ConvertFromString(ao.ColorHex); } catch { }
                var strokeBrush = TacticalConfigPanel.CreateFrozenBrush(c.R, c.G, c.B);
                var screenPts = ao.Points.Select(pt => LatLonToScreen(pt.Lat, pt.Lon, w, h)).ToList();

                var fig = new PathFigure { StartPoint = screenPts[0], IsClosed = screenPts.Count >= 3, IsFilled = true };
                for (int i = 1; i < screenPts.Count; i++) fig.Segments.Add(new LineSegment(screenPts[i], true));
                var aoPolyGeom = new PathGeometry(new[] { fig });

                if (screenPts.Count >= 3)
                {
                    var fullRectGeom = new RectangleGeometry(new Rect(0, 0, w, h));
                    var invertedOutsideGeom = new CombinedGeometry(GeometryCombineMode.Exclude, fullRectGeom, aoPolyGeom);
                    dc.DrawGeometry(CreateOutOfBoundsPatternBrush(c, _aoOutMaskOpacity, _aoOutPattern), null, invertedOutsideGeom);
                }

                var borderPen = new Pen(strokeBrush, 2.8) { DashStyle = new DashStyle(new double[] { 5, 2.5 }, 0) };
                borderPen.Freeze();
                dc.DrawGeometry(null, borderPen, aoPolyGeom);

                if (_showAllObjectLabels && ao.ShowLabel && !string.IsNullOrWhiteSpace(ao.Name))
                    DrawFastBadgeOnContext(dc, $"🛡️ {ao.Name}", screenPts[0].X + 10, screenPts[0].Y + 6, strokeBrush, 10 * _fontScaleMultiplier, 1.0, $"AO_{ao.ColorHex}");
            }

            // 2. Sektory / Strefy kolorowe
            foreach (var sec in _mapObjects.Where(o => o.Kind == "Sector" && o.Points.Count >= 2))
            {
                Color c = Color.FromRgb(245, 184, 46);
                try { c = (Color)ColorConverter.ConvertFromString(sec.ColorHex); } catch { }
                var strokeBrush = TacticalConfigPanel.CreateFrozenBrush(c.R, c.G, c.B);
                var fillBrush = new SolidColorBrush(Color.FromArgb(85, c.R, c.G, c.B)); fillBrush.Freeze();
                var screenPts = sec.Points.Select(pt => LatLonToScreen(pt.Lat, pt.Lon, w, h)).ToList();

                var fig = new PathFigure { StartPoint = screenPts[0], IsClosed = true, IsFilled = true };
                for (int i = 1; i < screenPts.Count; i++) fig.Segments.Add(new LineSegment(screenPts[i], true));
                var polyGeom = new PathGeometry(new[] { fig });
                var secPen = new Pen(strokeBrush, 2.2); secPen.Freeze();
                dc.DrawGeometry(fillBrush, secPen, polyGeom);

                if (_showAllObjectLabels && sec.ShowLabel && !string.IsNullOrWhiteSpace(sec.Name))
                {
                    double cx = screenPts.Average(p => p.X), cy = screenPts.Average(p => p.Y);
                    DrawFastBadgeOnContext(dc, sec.Name, cx + 14, cy - 8, strokeBrush, 10 * _fontScaleMultiplier, 1.0, $"SEC_{sec.ColorHex}");
                }
            }

            // 3. Trasy / Linie
            foreach (var rt in _mapObjects.Where(o => o.Kind == "Route" && o.Points.Count >= 2))
            {
                var strokeBrush = TacticalConfigPanel.BrushFromHex(rt.ColorHex, Color.FromRgb(38, 132, 255));
                var screenPts = rt.Points.Select(pt => LatLonToScreen(pt.Lat, pt.Lon, w, h)).ToList();
                var rtPen = new Pen(strokeBrush, 2.8) { DashStyle = new DashStyle(new double[] { 4, 2 }, 0) };
                rtPen.Freeze();
                for (int i = 0; i < screenPts.Count - 1; i++)
                    dc.DrawLine(rtPen, screenPts[i], screenPts[i + 1]);

                if (_showAllObjectLabels && rt.ShowLabel && !string.IsNullOrWhiteSpace(rt.Name))
                    DrawFastBadgeOnContext(dc, rt.Name, screenPts[0].X + 8, screenPts[0].Y - 14, strokeBrush, 9.5 * _fontScaleMultiplier, 1.0, $"RT_{rt.ColorHex}");
            }

            // 3b. Podgląd rysowanego odcinka na żywo (Rubber-band) z przyciąganiem kąta co 45° przy wciśniętym klawiszu SHIFT
            if (_isHoveringCanvas && _activeDraftPoly != null && _activeDraftPoly.Points.Count > 0 && (_activeTool == "ROUTE" || _activeTool == "AO" || _activeTool == "SECTOR"))
            {
                var lastPt = _activeDraftPoly.Points[_activeDraftPoly.Points.Count - 1];
                Point pStart = LatLonToScreen(lastPt.Lat, lastPt.Lon, w, h);
                Point pEnd = ApplyShift45DegreeSnap(pStart, _currentHoverScreenPt, out bool snapped, out int deg);

                var previewBrush = snapped ? TacticalConfigPanel.BrushAccent : TacticalConfigPanel.BrushFromHex(_activeDraftPoly.ColorHex, Color.FromRgb(31, 224, 197));
                var previewPen = new Pen(previewBrush, 2.2) { DashStyle = new DashStyle(new double[] { 3, 2 }, 0) };
                previewPen.Freeze();
                dc.DrawLine(previewPen, pStart, pEnd);
                dc.DrawEllipse(previewBrush, null, pEnd, 4, 4);

                if (snapped)
                    DrawFastBadgeOnContext(dc, $"📐 SNAP {deg}° (SHIFT)", pEnd.X + 12, pEnd.Y - 18, TacticalConfigPanel.BrushAccent, 9.5, 1.0, "SNAP_DEG");
            }

            // 4. Siatka wojskowa MGRS / Sektorowa / WGS84 oraz Keypad 1-9 / Ćwiartki A-D
            if (_coordSystem != "Brak siatki")
            {
                byte mainAlpha = (byte)Math.Clamp((int)Math.Round(_mainGridOpacity * 255), 0, 255);
                byte subAlpha = (byte)Math.Clamp((int)Math.Round(_subGridOpacity * 255), 0, 255);
                var mainBrush = GetGridColorBrush(mainAlpha);
                var subBrush = GetGridColorBrush(subAlpha);

                byte mainHaloAlpha = (byte)Math.Clamp((int)Math.Round((_gridHaloMode.StartsWith("Pełny") ? 220 : 130) * _mainGridOpacity), 0, 255);
                byte subHaloAlpha = (byte)Math.Clamp((int)Math.Round((_gridHaloMode.StartsWith("Pełny") ? 180 : 100) * _subGridOpacity), 0, 255);
                var haloBrush = new SolidColorBrush(Color.FromArgb(mainHaloAlpha, 0, 0, 0)); haloBrush.Freeze();
                var subHaloBrush = new SolidColorBrush(Color.FromArgb(subHaloAlpha, 0, 0, 0)); subHaloBrush.Freeze();
                bool useHalo = !_gridHaloMode.StartsWith("Brak");

                double mainThick = Math.Max(0.8, 1.45 * _gridThicknessMultiplier);
                double subThick = Math.Max(0.7, 0.95 * _gridThicknessMultiplier);
                var mainPen = new Pen(mainBrush, mainThick); mainPen.Freeze();
                var mainHaloPen = new Pen(haloBrush, mainThick + 1.8); mainHaloPen.Freeze();
                var subPen = new Pen(subBrush, subThick) { DashStyle = new DashStyle(new double[] { 4, 2 }, 0) }; subPen.Freeze();
                var subHaloPen = new Pen(subHaloBrush, subThick + 1.2); subHaloPen.Freeze();

                if (_coordSystem.StartsWith("Sektorowa") && mainAlpha > 5)
                {
                    int cols = 8, rows = 8; double cw = w / cols, ch = h / rows;
                    for (int c = 1; c < cols; c++) dc.DrawLine(mainPen, new Point(c * cw, 0), new Point(c * cw, h));
                    for (int r = 1; r < rows; r++) dc.DrawLine(mainPen, new Point(0, r * ch), new Point(w, r * ch));
                    for (int r = 0; r < rows; r++)
                        for (int c = 0; c < cols; c++)
                            DrawFastBadgeOnContext(dc, $"{(char)('A' + r)}{c + 1}", c * cw + 6, r * ch + 4, mainBrush, 10 * _fontScaleMultiplier, _mainGridOpacity, $"MAIN_{_gridColorName}_{mainAlpha}");
                }
                else
                {
                    double metersPerPixel = GetExactMetersPerPixel();
                    int stepMeters = _subGridDivision.Contains("250") ? 250 : (_subGridDivision.Contains("500") ? 500 : 100);
                    double stepPx = Math.Clamp(stepMeters / metersPerPixel, 64.0, 360.0);
                    double startX = (w / 2.0) % stepPx, startY = (h / 2.0) % stepPx;

                    if (_coordSystem.StartsWith("MGRS") && (_subGridDivision.Contains("50 m") || _subGridDivision.Contains("33 m")) && subAlpha > 4)
                    {
                        bool isQuarters = _subGridDivision.Contains("50 m");
                        int div = isQuarters ? 2 : 3;
                        double subStep = stepPx / div;

                        for (double x = startX - stepPx; x < w; x += stepPx)
                            for (int i = 1; i < div; i++)
                            {
                                double sx = x + i * subStep;
                                if (sx < 0 || sx > w) continue;
                                if (useHalo) dc.DrawLine(subHaloPen, new Point(sx, 0), new Point(sx, h));
                                dc.DrawLine(subPen, new Point(sx, 0), new Point(sx, h));
                            }

                        for (double y = startY - stepPx; y < h; y += stepPx)
                            for (int j = 1; j < div; j++)
                            {
                                double sy = y + j * subStep;
                                if (sy < 0 || sy > h) continue;
                                if (useHalo) dc.DrawLine(subHaloPen, new Point(0, sy), new Point(w, sy));
                                dc.DrawLine(subPen, new Point(0, sy), new Point(w, sy));
                            }

                        if (_gridLabelsMode.Contains("Ćwiartki/Keypad") && stepPx >= 64)
                        {
                            string[,] keypad3x3 = { { "7", "8", "9" }, { "4", "5", "6" }, { "1", "2", "3" } };
                            string[,] quarters2x2 = { { "A", "B" }, { "C", "D" } };
                            string subCacheKey = $"SUB_{_gridColorName}_{subAlpha}";
                            double subFontSz = 8.5 * _fontScaleMultiplier;

                            for (double x = startX; x < w - stepPx * 0.6; x += stepPx)
                                for (double y = startY; y < h - stepPx * 0.6; y += stepPx)
                                    for (int r = 0; r < div; r++)
                                        for (int c = 0; c < div; c++)
                                        {
                                            string subTxt = isQuarters ? quarters2x2[r, c] : keypad3x3[r, c];
                                            DrawFastBadgeOnContext(dc, subTxt, x + (c + 0.5) * subStep - 4, y + (r + 0.5) * subStep - 6, subBrush, subFontSz, _subGridOpacity, subCacheKey);
                                        }
                        }
                    }

                    if (mainAlpha > 4)
                    {
                        bool isWgs84 = _coordSystem.StartsWith("WGS84");
                        string mainCacheKey = $"MAIN_{_gridColorName}_{mainAlpha}";
                        double mainFontSz = 9.5 * _fontScaleMultiplier;

                        for (double x = startX; x < w; x += stepPx)
                        {
                            if (useHalo) dc.DrawLine(mainHaloPen, new Point(x, 0), new Point(x, h));
                            dc.DrawLine(mainPen, new Point(x, 0), new Point(x, h));
                            var (_, lon) = ScreenToLatLon(x, 20, w, h);
                            string lblTxt = isWgs84
                                ? $"{lon:0.0000}°E"
                                : $"{(((int)Math.Round(((lon + 180.0) % 6.0) * 111320.0 * Math.Cos(_centerLat * Math.PI / 180.0)) / 100) % 1000):D3}";
                            DrawFastBadgeOnContext(dc, lblTxt, x + 5, 6, mainBrush, mainFontSz, _mainGridOpacity, mainCacheKey);
                        }
                        for (double y = startY; y < h; y += stepPx)
                        {
                            if (useHalo) dc.DrawLine(mainHaloPen, new Point(0, y), new Point(w, y));
                            dc.DrawLine(mainPen, new Point(0, y), new Point(w, y));
                            var (lat, _) = ScreenToLatLon(20, y, w, h);
                            string lblTxt = isWgs84
                                ? $"{lat:0.0000}°N"
                                : $"{(((int)Math.Round(lat * 110574.0) / 100) % 1000):D3}";
                            DrawFastBadgeOnContext(dc, lblTxt, 6, y + 3, mainBrush, mainFontSz, _mainGridOpacity, mainCacheKey);
                        }
                    }
                }
            }

            // 5. Dalmierz (PPM)
            if (_rulerStart != null && _rulerEnd != null)
            {
                Point p1 = LatLonToScreen(_rulerStart.Value.Lat, _rulerStart.Value.Lon, w, h);
                Point p2 = LatLonToScreen(_rulerEnd.Value.Lat, _rulerEnd.Value.Lon, w, h);
                var rPen = new Pen(TacticalConfigPanel.BrushAccent, 2.2) { DashStyle = new DashStyle(new double[] { 4, 2 }, 0) };
                rPen.Freeze();
                dc.DrawLine(rPen, p1, p2);
                double dLat = (_rulerEnd.Value.Lat - _rulerStart.Value.Lat) * Math.PI / 180.0, dLon = (_rulerEnd.Value.Lon - _rulerStart.Value.Lon) * Math.PI / 180.0;
                double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(_rulerStart.Value.Lat * Math.PI / 180.0) * Math.Cos(_rulerEnd.Value.Lat * Math.PI / 180.0) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
                double dist = 6371000.0 * 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
                DrawFastBadgeOnContext(dc, $"📏 {dist:0} m", (p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2 - 14, TacticalConfigPanel.BrushAccent, 10.5 * _fontScaleMultiplier, 1.0, "RULER");
            }
        }

        private static void RedrawVectorsOnly()
        {
            _fastOverlayRenderer?.InvalidateVisual();
            if (_interactiveCanvas == null || _fullScreenOverlay?.Visibility != Visibility.Visible) return;
            var (w, h) = GetExactCanvasSize();

            _interactiveCanvas.Children.Clear();

            foreach (var polyObj in _mapObjects.Where(o => (o.Kind == "AO" || o.Kind == "Sector" || o.Kind == "Route") && o.Points.Count > 0))
            {
                var strokeBrush = TacticalConfigPanel.BrushFromHex(polyObj.ColorHex, Color.FromRgb(38, 132, 255));
                var screenPts = polyObj.Points.Select(pt => LatLonToScreen(pt.Lat, pt.Lon, w, h)).ToList();
                DrawDraggableVertexAndWholeZoneHandles(polyObj, screenPts, strokeBrush, w, h);
            }

            DrawTacticalMarkersOverlay(w, h);
            DrawDraggableCornerElements(w, h);
        }

        private static async Task RenderMapTilesAsync(bool transparentGrgOnly = false)
        {
            if (_tileCanvas == null || _interactiveCanvas == null || _activeLayer == null || _fullScreenOverlay?.Visibility != Visibility.Visible) return;

            _renderCts?.Cancel();
            var cts = new CancellationTokenSource();
            _renderCts = cts;
            var ct = cts.Token;

            UpdateSheetViewportSize();
            var (w, h) = GetExactCanvasSize();
            double tilePx = GetScaledTileSizePx();

            RedrawVectorsOnly();

            if (transparentGrgOnly)
            {
                _tileCanvas.Children.Clear();
                _lidarOverlayImage.Source = null;
                _dimmerRect.Visibility = Visibility.Collapsed;
                _mapCompositeGrid.Background = Brushes.Transparent;
                return;
            }

            _mapCompositeGrid.Background = TacticalConfigPanel.BrushCardBg;
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

        // Rysuje zarówno uchwyty pojedynczych wierzchołków (białe kółka), jak i centralny uchwyt ✥ do przesuwania CAŁEJ strefy/sektora/trasy
        private static void DrawDraggableVertexAndWholeZoneHandles(TacticalMapObjectItem obj, List<Point> screenPts, Brush strokeBrush, double viewW, double viewH)
        {
            for (int i = 0; i < screenPts.Count; i++)
            {
                int vIdx = i;
                Point vPt = screenPts[i];
                var handle = new Ellipse
                {
                    Width = 11,
                    Height = 11,
                    Fill = Brushes.White,
                    Stroke = strokeBrush,
                    StrokeThickness = 2.2,
                    Cursor = Cursors.Hand,
                    ToolTip = $"Przeciągnij punkt krańcowy #{vIdx + 1} ({obj.Name})  |  Przytrzymaj SHIFT = kąt co 45°"
                };
                handle.MouseLeftButtonDown += (_, e) =>
                {
                    e.Handled = true;
                    _draggedObject = obj;
                    _draggedVertexIndex = vIdx;
                    Point mousePt = e.GetPosition(_interactiveCanvas);
                    _markerDragScreenOffset = new Point(mousePt.X - vPt.X, mousePt.Y - vPt.Y);
                    _interactiveCanvas.CaptureMouse();
                };
                Canvas.SetLeft(handle, vPt.X - 5.5);
                Canvas.SetTop(handle, vPt.Y - 5.5);
                _interactiveCanvas.Children.Add(handle);
            }

            // Centralny uchwyt ✥ do przesuwania całej strefy / sektora / trasy
            if (screenPts.Count >= 2)
            {
                double cx = screenPts.Average(p => p.X);
                double cy = screenPts.Average(p => p.Y);
                var moveAllBadge = new Border
                {
                    Width = 22,
                    Height = 22,
                    CornerRadius = new CornerRadius(11),
                    Background = new SolidColorBrush(Color.FromArgb(225, 8, 19, 22)),
                    BorderBrush = strokeBrush,
                    BorderThickness = new Thickness(1.8),
                    Cursor = Cursors.SizeAll,
                    ToolTip = $"Przeciągnij tutaj (✥), aby przesunąć CAŁY obiekt: {obj.Name}",
                    Child = new TextBlock
                    {
                        Text = "✥",
                        Foreground = Brushes.White,
                        FontSize = 12,
                        FontWeight = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, -1, 0, 0)
                    }
                };
                moveAllBadge.MouseLeftButtonDown += (_, e) =>
                {
                    e.Handled = true;
                    _draggedObject = obj;
                    _draggedVertexIndex = -2; // -2 oznacza przesuwanie całej strefy/sektora/trasy
                    Point p = e.GetPosition(_interactiveCanvas);
                    _wholeShapeDragStartLatLon = ScreenToLatLon(p.X, p.Y, viewW, viewH);
                    _wholeShapeOriginalPoints = obj.Points.ToList();
                    _interactiveCanvas.CaptureMouse();
                };
                Canvas.SetLeft(moveAllBadge, cx - 11);
                Canvas.SetTop(moveAllBadge, cy - 11);
                _interactiveCanvas.Children.Add(moveAllBadge);
            }
        }

        private static Brush GetGridColorBrush(byte alpha = 230)
        {
            Color c = _gridColorName switch { "Turkusowy" => Color.FromArgb(alpha, 31, 224, 197), "Biały" => Color.FromArgb(alpha, 255, 255, 255), "Czerwony" => Color.FromArgb(alpha, 235, 64, 52), "Czarny" => Color.FromArgb(alpha, 15, 15, 15), _ => Color.FromArgb(alpha, 250, 208, 44) };
            var b = new SolidColorBrush(c); b.Freeze(); return b;
        }

        private static void AddHaloLabel(string text, double x, double y, Brush fg, double fontSize)
        {
            var b = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(195, 5, 12, 15)),
                BorderBrush = fg,
                BorderThickness = new Thickness(0.8),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 1, 4, 1),
                IsHitTestVisible = false,
                Child = new TextBlock { Text = text, Foreground = fg, FontSize = fontSize, FontWeight = FontWeights.Bold }
            };
            Canvas.SetLeft(b, x); Canvas.SetTop(b, y); _interactiveCanvas.Children.Add(b);
        }

        private static void AttachCenteredMarkerDragHandler(UIElement el, TacticalMapObjectItem obj, Point exactMarkerCenterScreenPt)
        {
            if (el is FrameworkElement fe)
            {
                fe.Cursor = Cursors.SizeAll;
                fe.ToolTip = $"Przeciągnij myszą, aby przesunąć: {obj.Name} (środek 1:1)";
            }
            el.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                _draggedObject = obj;
                _draggedVertexIndex = 0;
                Point mousePt = e.GetPosition(_interactiveCanvas);
                // Zapamiętujemy dokładny offset względem środka ikony, aby ikona nie skakała narożnikiem do kursora!
                _markerDragScreenOffset = new Point(mousePt.X - exactMarkerCenterScreenPt.X, mousePt.Y - exactMarkerCenterScreenPt.Y);
                _interactiveCanvas.CaptureMouse();
            };
        }

        // =========================================================================
        // RYSOWANIE ZNAKÓW WEKTOROWYCH NATO APP-6 WYCENTROWANYCH 1:1 W MIEJSCU KLIKNIĘCIA (pt.X, pt.Y)
        // =========================================================================
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

            // Stały kontener 36x28 px, którego środek (cx = 18, cy = 14) pokrywa się w 100% ze współrzędną kliknięcia (pt.X, pt.Y)
            const double boxW = 36.0, boxH = 28.0;
            const double cx = 18.0, cy = 14.0;
            const double fw = 28.0, fh = 18.0;
            double left = cx - fw / 2.0, top = cy - fh / 2.0;

            var iconCanvas = new Canvas
            {
                Width = boxW,
                Height = boxH,
                Background = Brushes.Transparent
            };
            AttachCenteredMarkerDragHandler(iconCanvas, obj, pt);

            void AddBoxToIconCanvas()
            {
                var halo = new Rectangle { Width = fw + 2, Height = fh + 2, Stroke = Brushes.Black, StrokeThickness = 3.0, Fill = darkBgBrush };
                Canvas.SetLeft(halo, left - 1); Canvas.SetTop(halo, top - 1); iconCanvas.Children.Add(halo);
                var rect = new Rectangle { Width = fw, Height = fh, Stroke = strokeBrush, StrokeThickness = 2.0, Fill = fillBrush };
                Canvas.SetLeft(rect, left); Canvas.SetTop(rect, top); iconCanvas.Children.Add(rect);
            }

            switch (symbolCode)
            {
                case "NATO_HQ":
                    // Ramka HQ wycentrowana w (cx, cy) + maszt sztabowy po lewej stronie
                    AddBoxToIconCanvas();
                    iconCanvas.Children.Add(new Line { X1 = left, Y1 = top, X2 = left, Y2 = boxH, Stroke = Brushes.Black, StrokeThickness = 3.8 });
                    iconCanvas.Children.Add(new Line { X1 = left, Y1 = top, X2 = left, Y2 = boxH, Stroke = strokeBrush, StrokeThickness = 2.2 });
                    var hqTxt = new TextBlock { Text = "HQ", Foreground = Brushes.White, FontSize = 9.5, FontWeight = FontWeights.Bold };
                    Canvas.SetLeft(hqTxt, cx - 7); Canvas.SetTop(hqTxt, cy - 6); iconCanvas.Children.Add(hqTxt);
                    break;

                case "NATO_INF":
                    AddBoxToIconCanvas();
                    iconCanvas.Children.Add(new Line { X1 = left, Y1 = top, X2 = left + fw, Y2 = top + fh, Stroke = Brushes.White, StrokeThickness = 1.8 });
                    iconCanvas.Children.Add(new Line { X1 = left, Y1 = top + fh, X2 = left + fw, Y2 = top, Stroke = Brushes.White, StrokeThickness = 1.8 });
                    break;

                case "NATO_RECON":
                    AddBoxToIconCanvas();
                    iconCanvas.Children.Add(new Line { X1 = left, Y1 = top + fh, X2 = left + fw, Y2 = top, Stroke = Brushes.White, StrokeThickness = 2.0 });
                    break;

                case "NATO_MED":
                    AddBoxToIconCanvas();
                    iconCanvas.Children.Add(new Line { X1 = cx, Y1 = top, X2 = cx, Y2 = top + fh, Stroke = Brushes.White, StrokeThickness = 2.2 });
                    iconCanvas.Children.Add(new Line { X1 = left, Y1 = cy, X2 = left + fw, Y2 = cy, Stroke = Brushes.White, StrokeThickness = 2.2 });
                    break;

                case "NATO_COMMS":
                    AddBoxToIconCanvas();
                    var bolt = new Polyline { Stroke = Brushes.White, StrokeThickness = 2.0, Points = new PointCollection { new Point(left + 5, top + fh - 3), new Point(cx - 1, top + 3), new Point(cx + 2, top + fh - 4), new Point(left + fw - 5, top + 3) } };
                    iconCanvas.Children.Add(bolt);
                    break;

                case "NATO_SUPPLY":
                    AddBoxToIconCanvas();
                    iconCanvas.Children.Add(new Line { X1 = left, Y1 = top + fh - 4, X2 = left + fw, Y2 = top + fh - 4, Stroke = Brushes.White, StrokeThickness = 2.0 });
                    break;

                case "NATO_OP":
                    var triPts = new PointCollection { new Point(cx, cy - 11), new Point(cx - 12, cy + 10), new Point(cx + 12, cy + 10) };
                    iconCanvas.Children.Add(new Polygon { Stroke = Brushes.Black, StrokeThickness = 3.2, Fill = darkBgBrush, Points = triPts });
                    iconCanvas.Children.Add(new Polygon { Stroke = strokeBrush, StrokeThickness = 2.0, Fill = fillBrush, Points = triPts });
                    var opDot = new Ellipse { Width = 4, Height = 4, Fill = Brushes.White };
                    Canvas.SetLeft(opDot, cx - 2); Canvas.SetTop(opDot, cy); iconCanvas.Children.Add(opDot);
                    break;

                case "NATO_SNIPER":
                    var snHalo = new Ellipse { Width = 22, Height = 22, Stroke = Brushes.Black, StrokeThickness = 3.0, Fill = darkBgBrush };
                    Canvas.SetLeft(snHalo, cx - 11); Canvas.SetTop(snHalo, cy - 11); iconCanvas.Children.Add(snHalo);
                    var snCirc = new Ellipse { Width = 20, Height = 20, Stroke = strokeBrush, StrokeThickness = 2.0, Fill = fillBrush };
                    Canvas.SetLeft(snCirc, cx - 10); Canvas.SetTop(snCirc, cy - 10); iconCanvas.Children.Add(snCirc);
                    iconCanvas.Children.Add(new Line { X1 = cx - 13, Y1 = cy, X2 = cx + 13, Y2 = cy, Stroke = Brushes.White, StrokeThickness = 1.5 });
                    iconCanvas.Children.Add(new Line { X1 = cx, Y1 = cy - 13, X2 = cx, Y2 = cy + 13, Stroke = Brushes.White, StrokeThickness = 1.5 });
                    break;

                case "NATO_CP":
                    var shPts = new PointCollection { new Point(cx - 11, cy - 10), new Point(cx + 11, cy - 10), new Point(cx + 11, cy + 2), new Point(cx, cy + 11), new Point(cx - 11, cy + 2) };
                    iconCanvas.Children.Add(new Polygon { Stroke = Brushes.Black, StrokeThickness = 3.0, Fill = darkBgBrush, Points = shPts });
                    iconCanvas.Children.Add(new Polygon { Stroke = strokeBrush, StrokeThickness = 2.0, Fill = fillBrush, Points = shPts });
                    var cpTxt = new TextBlock { Text = "CP", Foreground = Brushes.White, FontSize = 8.5, FontWeight = FontWeights.Bold };
                    Canvas.SetLeft(cpTxt, cx - 6); Canvas.SetTop(cpTxt, cy - 6); iconCanvas.Children.Add(cpTxt);
                    break;

                case "NATO_LZ":
                    var lzHalo = new Ellipse { Width = 24, Height = 24, Stroke = Brushes.Black, StrokeThickness = 3.0, Fill = darkBgBrush };
                    Canvas.SetLeft(lzHalo, cx - 12); Canvas.SetTop(lzHalo, cy - 12); iconCanvas.Children.Add(lzHalo);
                    var lzCirc = new Ellipse { Width = 22, Height = 22, Stroke = strokeBrush, StrokeThickness = 2.0, Fill = fillBrush };
                    Canvas.SetLeft(lzCirc, cx - 11); Canvas.SetTop(lzCirc, cy - 11); iconCanvas.Children.Add(lzCirc);
                    var lzTxt = new TextBlock { Text = "LZ", Foreground = Brushes.White, FontSize = 8.5, FontWeight = FontWeights.Bold };
                    Canvas.SetLeft(lzTxt, cx - 5.5); Canvas.SetTop(lzTxt, cy - 5.5); iconCanvas.Children.Add(lzTxt);
                    break;

                case "NATO_OBJ":
                    var dmPts = new PointCollection { new Point(cx, cy - 12), new Point(cx + 12, cy), new Point(cx, cy + 12), new Point(cx - 12, cy) };
                    iconCanvas.Children.Add(new Polygon { Stroke = Brushes.Black, StrokeThickness = 3.0, Fill = darkBgBrush, Points = dmPts });
                    iconCanvas.Children.Add(new Polygon { Stroke = strokeBrush, StrokeThickness = 2.0, Fill = fillBrush, Points = dmPts });
                    iconCanvas.Children.Add(new Line { X1 = cx - 6, Y1 = cy, X2 = cx + 6, Y2 = cy, Stroke = Brushes.White, StrokeThickness = 1.6 });
                    iconCanvas.Children.Add(new Line { X1 = cx, Y1 = cy - 6, X2 = cx, Y2 = cy + 6, Stroke = Brushes.White, StrokeThickness = 1.6 });
                    break;

                case "NATO_HAZARD":
                    var hzPts = new PointCollection { new Point(cx, cy - 11), new Point(cx - 12, cy + 10), new Point(cx + 12, cy + 10) };
                    iconCanvas.Children.Add(new Polygon { Stroke = Brushes.Black, StrokeThickness = 3.2, Fill = darkBgBrush, Points = hzPts });
                    iconCanvas.Children.Add(new Polygon { Stroke = strokeBrush, StrokeThickness = 2.0, Fill = fillBrush, Points = hzPts });
                    var hzTxt = new TextBlock { Text = "!", Foreground = Brushes.White, FontSize = 10.5, FontWeight = FontWeights.Bold };
                    Canvas.SetLeft(hzTxt, cx - 2); Canvas.SetTop(hzTxt, cy - 4); iconCanvas.Children.Add(hzTxt);
                    break;

                default:
                    var dot = new Ellipse { Width = 14, Height = 14, Fill = strokeBrush, Stroke = Brushes.White, StrokeThickness = 2 };
                    Canvas.SetLeft(dot, cx - 7); Canvas.SetTop(dot, cy - 7); iconCanvas.Children.Add(dot);
                    break;
            }

            // Osadzenie kontenera dokładnie na środku punktu (pt.X - cx, pt.Y - cy)
            Canvas.SetLeft(iconCanvas, pt.X - cx);
            Canvas.SetTop(iconCanvas, pt.Y - cy);
            _interactiveCanvas.Children.Add(iconCanvas);

            if (_showAllObjectLabels && obj.ShowLabel && !string.IsNullOrWhiteSpace(name))
                AddHaloLabel(name, pt.X + 16, pt.Y - 9, strokeBrush, 10 * _fontScaleMultiplier);
        }

        private static void DrawTacticalMarkersOverlay(double w, double h)
        {
            foreach (var obj in _mapObjects.Where(o => o.Points.Count > 0 && (o.Kind == "Marker" || o.Kind == "Dot")))
            {
                var strokeBrush = TacticalConfigPanel.BrushFromHex(obj.ColorHex, Color.FromRgb(38, 132, 255));
                Point pt = LatLonToScreen(obj.Points[0].Lat, obj.Points[0].Lon, w, h);
                if (obj.Kind == "Dot")
                {
                    var dot = new Ellipse { Width = 14, Height = 14, Fill = strokeBrush, Stroke = Brushes.White, StrokeThickness = 2 };
                    AttachCenteredMarkerDragHandler(dot, obj, pt);
                    Canvas.SetLeft(dot, pt.X - 7.0); Canvas.SetTop(dot, pt.Y - 7.0);
                    _interactiveCanvas.Children.Add(dot);

                    if (_showAllObjectLabels && obj.ShowLabel && !string.IsNullOrWhiteSpace(obj.Name))
                        AddHaloLabel(obj.Name, pt.X + 9, pt.Y - 8, strokeBrush, 9.5 * _fontScaleMultiplier);
                }
                else
                {
                    DrawNatoVectorSymbolOnCanvas(pt, obj);
                }
            }
        }

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
                Point p = e.GetPosition(_interactiveCanvas);
                _hudStartOffset = new Point(p.X - Canvas.GetLeft(el), p.Y - Canvas.GetTop(el));
                _interactiveCanvas.CaptureMouse();
            };

            Canvas.SetLeft(el, x);
            Canvas.SetTop(el, y);
            _interactiveCanvas.Children.Add(el);
        }

        private static void DrawDraggableCornerElements(double w, double h)
        {
            var (wMm, _) = GetActiveSheetDimensionsMm();
            bool isCompactWrist = wMm <= 160;

            if (_showNorthArrow)
            {
                double nw = isCompactWrist ? 42 : 52, nh = isCompactWrist ? 50 : 62;
                var compass = new Border { Width = nw, Height = nh, Background = new SolidColorBrush(Color.FromArgb(228, 8, 19, 22)), BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(6) };
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

                var sbBorder = new Border { Width = barW + 34, Height = 52, Background = new SolidColorBrush(Color.FromArgb(228, 8, 19, 22)), BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(6) };
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
                double lw = isCompactWrist ? 215 : 275;
                var ls = new StackPanel();
                ls.Children.Add(new TextBlock { Text = $"MAPA TAKTYCZNA ({_selectedSheet.Name.Split('—')[0].Trim()} | 1:{_targetScaleDenominator:N0})", Foreground = TacticalConfigPanel.BrushAccent, FontWeight = FontWeights.Bold, FontSize = isCompactWrist ? 9 : 10 });
                ls.Children.Add(new TextBlock { Text = $"Siatka: {_coordSystem.Split(' ')[0]} | {_subGridDivision}", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 8.5, Margin = new Thickness(0, 1, 0, 4) });

                var activeFacs = GetActiveMapFactions();
                if (activeFacs.Count > 0)
                {
                    var fw = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
                    fw.Children.Add(new TextBlock { Text = "Strona: ", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 8.5, Margin = new Thickness(0, 0, 4, 0) });
                    foreach (var f in activeFacs)
                    {
                        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0) };
                        sp.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(2), Background = TacticalConfigPanel.BrushFromHex(f.ColorHex, Color.FromRgb(38, 132, 255)), BorderBrush = Brushes.White, BorderThickness = new Thickness(0.6), Margin = new Thickness(0, 0, 4, 0) });
                        sp.Children.Add(new TextBlock { Text = f.Name, Foreground = Brushes.White, FontSize = 8.5, FontWeight = FontWeights.Bold });
                        fw.Children.Add(sp);
                    }
                    ls.Children.Add(fw);
                }

                int dynamicRows = 0;
                if (_showNatoSymbolsInLegend)
                {
                    ls.Children.Add(new Border { Height = 1, Background = TacticalConfigPanel.BrushTileBorder, Margin = new Thickness(0, 2, 0, 4) });
                    ls.Children.Add(new TextBlock { Text = "OZNACZENIA I SYMBOLE NATO (APP-6):", Foreground = TacticalConfigPanel.BrushAccent, FontSize = 8.5, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 3) });

                    void AddLegendSymbolRow(string badge, string desc, string hex)
                    {
                        dynamicRows++;
                        var r = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2.5) };
                        var b = new Border
                        {
                            MinWidth = 28,
                            Height = 14,
                            CornerRadius = new CornerRadius(2),
                            Background = TacticalConfigPanel.BrushFromHex(hex, Color.FromRgb(38, 132, 255)),
                            BorderBrush = Brushes.White,
                            BorderThickness = new Thickness(0.8),
                            Margin = new Thickness(0, 0, 6, 0),
                            Padding = new Thickness(3, 0, 3, 0),
                            Child = new TextBlock { Text = badge, Foreground = Brushes.White, FontSize = 7.5, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                        };
                        r.Children.Add(b);
                        r.Children.Add(new TextBlock { Text = desc, Foreground = Brushes.White, FontSize = 8, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = lw - 52 });
                        ls.Children.Add(r);
                    }

                    if (_mapObjects.Count > 0)
                    {
                        foreach (var obj in _mapObjects.Take(isCompactWrist ? 5 : 9))
                        {
                            if (obj.Kind == "AO")
                                AddLegendSymbolRow("AO", $"{obj.Name} — Granica Terenu Gry", obj.ColorHex);
                            else if (obj.Kind == "Sector")
                                AddLegendSymbolRow("SEK", $"{obj.Name} — Sektor / Strefa taktyczna", obj.ColorHex);
                            else if (obj.Kind == "Route")
                                AddLegendSymbolRow("───", $"{obj.Name} — Trasa / Linia rozgraniczenia", obj.ColorHex);
                            else if (obj.Kind == "Dot")
                                AddLegendSymbolRow("●", $"{obj.Name} — Punkt orientacyjny", obj.ColorHex);
                            else
                            {
                                var natoDef = NatoSymbols.FirstOrDefault(n => n.Code == obj.SymbolCode) ?? NatoSymbols[0];
                                AddLegendSymbolRow(natoDef.ShortBadge, $"{obj.Name} — {natoDef.NatoMeaning}", obj.ColorHex);
                            }
                        }
                    }
                    else
                    {
                        AddLegendSymbolRow("HQ", "Sztab / Dowództwo strony", "#2684FF");
                        AddLegendSymbolRow("X", "Piechota / Drużyna szturmowa", "#2684FF");
                        AddLegendSymbolRow("+", "Punkt Medyczny / MEDEVAC", "#2ECC71");
                        AddLegendSymbolRow("CP", "Punkt Kontrolny / Respawn", "#F5B82E");
                        AddLegendSymbolRow("AO", "Granica Terenu Gry (strefa poza grą)", "#EB4034");
                    }
                }

                double lh = 64 + (_showNatoSymbolsInLegend ? (22 + dynamicRows * 17) : 0);
                var legBorder = new Border
                {
                    Width = lw,
                    Height = lh,
                    Background = new SolidColorBrush(Color.FromArgb(228, 8, 19, 22)),
                    BorderBrush = TacticalConfigPanel.BrushAccent,
                    BorderThickness = new Thickness(1.2),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 6, 8, 6),
                    Child = ls
                };
                PlaceDraggableHudElement(legBorder, "LEGEND", _legendPos, _legendNormX, _legendNormY, w, h, lw, lh);
            }
        }

        private static byte[] CaptureMapCanvasPngBytes()
        {
            var (cw, ch) = GetExactCanvasSize();
            int w = (int)Math.Max(400, cw), h = (int)Math.Max(300, ch);
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
                    ["subGridOpacity"] = _subGridOpacity,
                    ["showAllObjectLabels"] = _showAllObjectLabels
                };
                var facArr = new JsonArray();
                foreach (var fn in _selectedMapFactions) facArr.Add(fn);
                root["selectedFactions"] = facArr;

                var arr = new JsonArray();
                foreach (var o in _mapObjects)
                {
                    var pts = new JsonArray();
                    foreach (var p in o.Points) pts.Add(new JsonObject { ["lat"] = p.Lat, ["lon"] = p.Lon });
                    arr.Add(new JsonObject { ["kind"] = o.Kind, ["symbol"] = o.SymbolCode, ["name"] = o.Name, ["color"] = o.ColorHex, ["showLabel"] = o.ShowLabel, ["points"] = pts });
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
                    if (bool.TryParse(root["showAllObjectLabels"]?.ToString(), out bool sal)) _showAllObjectLabels = sal;
                    if (root["selectedFactions"] is JsonArray fArr)
                    {
                        _selectedMapFactions.Clear();
                        foreach (var item in fArr) if (item != null) _selectedMapFactions.Add(item.ToString());
                        RebuildFactionCheckboxesUI();
                    }
                    _mapObjects.Clear();
                    if (root["objects"] is JsonArray arr)
                        foreach (var node in arr.OfType<JsonObject>())
                        {
                            bool shLbl = true;
                            if (bool.TryParse(node["showLabel"]?.ToString(), out bool parsedLbl)) shLbl = parsedLbl;
                            var item = new TacticalMapObjectItem
                            {
                                Kind = node["kind"]?.ToString() ?? "Marker",
                                SymbolCode = node["symbol"]?.ToString() ?? "NATO_HQ",
                                Name = node["name"]?.ToString() ?? "Punkt",
                                ColorHex = node["color"]?.ToString() ?? "#2684FF",
                                ShowLabel = shLbl
                            };
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
                        { var it = new TacticalMapObjectItem { Kind = "Marker", SymbolCode = _selectedNatoSymbolCode, Name = $"GPX-{_mapObjects.Count + 1}", ColorHex = _selectedObjectColorHex, ShowLabel = _newObjectShowLabelDefault }; it.Points.Add((la, lo)); _mapObjects.Add(it); }
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
                var allConfigured = TacticalConfigPanel.GetConfiguredFactions();
                var targetFacs = GetActiveMapFactions();

                bool grgTransparent = _kmzGrgMode.StartsWith("Przezroczysta");
                if (grgTransparent) await RenderMapTilesAsync(true);
                byte[] pngBytes = CaptureMapCanvasPngBytes();
                if (grgTransparent) await RenderMapTilesAsync(false);

                var (w, h) = GetExactCanvasSize();
                var (north, west) = ScreenToLatLon(0, 0, w, h); var (south, east) = ScreenToLatLon(w, h, w, h);

                await Task.Run(() =>
                {
                    if (targetFacs.Count > 0 && targetFacs.Count < allConfigured.Count)
                    {
                        foreach (var fac in targetFacs)
                        {
                            string sl = TacticalConfigPanel.SanitizeFileName(fac.Name).ToLowerInvariant();
                            string facDir = System.IO.Path.Combine(sourceDir, "factions", sl);
                            Directory.CreateDirectory(facDir);
                            WriteKmzGroundOverlayArchive(System.IO.Path.Combine(facDir, $"grg_map_{sl}.kmz"), $"Mapa_GRG_{fac.Name}", pngBytes, north, south, east, west);
                            WriteGpxFile(System.IO.Path.Combine(facDir, $"punkty_{sl}.gpx"));
                        }
                    }
                    else
                    {
                        string mapsDir = System.IO.Path.Combine(sourceDir, "maps"), atakDir = System.IO.Path.Combine(sourceDir, "atak");
                        Directory.CreateDirectory(mapsDir); Directory.CreateDirectory(atakDir);
                        WriteKmzGroundOverlayArchive(System.IO.Path.Combine(mapsDir, "grg_map_overlay.kmz"), "Mapa_GRG_ATAK", pngBytes, north, south, east, west);
                        WriteGpxFile(System.IO.Path.Combine(atakDir, "punkty_taktyczne.gpx"));
                    }
                });
                await TacticalConfigPanel.RefreshSourceSizeFromExternalAsync(win);
                string targetNames = string.Join(", ", targetFacs.Select(f => f.Name));
                if (_statusText != null) _statusText.Text = $"✓ Zapisano mapę .KMZ oraz punkty .GPX w folderze source dla stron: {targetNames}!";
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

                var (w, h) = GetExactCanvasSize();
                var (north, west) = ScreenToLatLon(0, 0, w, h); var (south, east) = ScreenToLatLon(w, h, w, h);
                string facSuffix = string.Join("_", GetActiveMapFactions().Select(f => TacticalConfigPanel.SanitizeFileName(f.Name)));

                await Task.Run(() =>
                {
                    if (format == "kmz") WriteKmzGroundOverlayArchive(System.IO.Path.Combine(outDir, $"Mapa_Taktyczna_ATAK_{facSuffix}.kmz"), $"Mapa_{facSuffix}", pngBytes, north, south, east, west);
                    else if (format == "gpx") WriteGpxFile(System.IO.Path.Combine(outDir, $"Mapa_Taktyczna_Garmin_{facSuffix}.gpx"));
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
            {
                var natoDef = NatoSymbols.FirstOrDefault(n => n.Code == o.SymbolCode);
                string desc = natoDef != null ? natoDef.NatoMeaning : o.Kind;
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  <Placemark><name>[{o.SymbolCode.Replace("NATO_", "")}] {o.Name}</name><description>{desc}</description><Point><coordinates>{o.Points[0].Lon:0.000000},{o.Points[0].Lat:0.000000},0</coordinates></Point></Placemark>"));
            }
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
                {
                    var natoDef = NatoSymbols.FirstOrDefault(n => n.Code == o.SymbolCode);
                    string desc = natoDef != null ? natoDef.NatoMeaning : o.Kind;
                    sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  <wpt lat=\"{o.Points[0].Lat:0.000000}\" lon=\"{o.Points[0].Lon:0.000000}\"><name>[{o.SymbolCode.Replace("NATO_", "")}] {o.Name}</name><desc>{desc}</desc></wpt>"));
                }
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
                var (wMm, hMm) = GetActiveSheetDimensionsMm();
                string cleanFmt = TacticalConfigPanel.SanitizeFileName(_selectedSheet.Name.Split('—')[0].Trim());
                string facSuffix = string.Join("_", GetActiveMapFactions().Select(f => TacticalConfigPanel.SanitizeFileName(f.Name)));
                string pdfPath = System.IO.Path.Combine(outDir, $"Mapa_{cleanFmt}_{_sheetOrientation}_{facSuffix}_1_{_targetScaleDenominator}.pdf");

                int pageW = landscape ? 1754 : 1240;
                int pageH = landscape ? 1240 : 1754;
                double hostPageWMm = landscape ? 297.0 : 210.0;
                double hostPageHMm = landscape ? 210.0 : 297.0;

                var (cw, ch) = GetExactCanvasSize();
                int mapW = (int)Math.Max(400, cw);
                int mapH = (int)Math.Max(300, ch);
                var mapRtb = new RenderTargetBitmap(mapW, mapH, 96, 96, PixelFormats.Pbgra32);
                mapRtb.Render(_mapCompositeGrid);

                var pageVisual = new DrawingVisual();
                using (DrawingContext dc = pageVisual.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, pageW, pageH));

                    if (wMm < 285 && hMm < 285)
                    {
                        double drawW = (wMm / hostPageWMm) * pageW;
                        double drawH = (hMm / hostPageHMm) * pageH;
                        double ox = (pageW - drawW) / 2.0;
                        double oy = (pageH - drawH) / 2.0;

                        dc.DrawImage(mapRtb, new Rect(ox, oy, drawW, drawH));
                        var cropPen = new Pen(Brushes.Black, 1.5) { DashStyle = DashStyles.Dash };
                        dc.DrawRectangle(null, new Pen(Brushes.Black, 1.2), new Rect(ox, oy, drawW, drawH));

                        double cm = 28;
                        dc.DrawLine(cropPen, new Point(ox - cm, oy), new Point(ox + drawW + cm, oy));
                        dc.DrawLine(cropPen, new Point(ox - cm, oy + drawH), new Point(ox + drawW + cm, oy + drawH));
                        dc.DrawLine(cropPen, new Point(ox, oy - cm), new Point(ox, oy + drawH + cm));
                        dc.DrawLine(cropPen, new Point(ox + drawW, oy - cm), new Point(ox + drawW, oy + drawH + cm));

                        TacticalConfigPanel.DrawCenteredText(dc, $"✂️ LINIA CIĘCIA MAPNIKA: {_selectedSheet.Name} ({_sheetOrientation}: {wMm:0} × {hMm:0} mm)  |  Strona: {facSuffix}  |  Skala 1:{_targetScaleDenominator:N0}", 15, Brushes.Black, pageW / 2.0, oy - 34, true);
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
                if (_statusText != null) _statusText.Text = $"✓ Wygenerowano plik PDF ({cleanFmt} {_sheetOrientation} - {facSuffix}): {pdfPath}";
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
