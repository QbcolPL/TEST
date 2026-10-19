#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

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
            sqlite3_exec(db, ToUtf8NullTerm(sql), IntPtr.Zero, IntPtr.Zero, out IntPtr _);
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

    public static partial class TacticalMapGeneratorView
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
            new NatoSymbolOption { Code = "NATO_HQ",     ShortBadge = "HQ",  NatoMeaning = "Sztab / Dowództwo (stopka masztu)", Label = "🚩 [HQ] NATO Sztab / Dowództwo (Flaga)" },
            new NatoSymbolOption { Code = "NATO_INF",    ShortBadge = "X",   NatoMeaning = "Piechota / Drużyna szturmowa",      Label = "[X] NATO Piechota / Drużyna" },
            new NatoSymbolOption { Code = "NATO_RECON",  ShortBadge = "/",   NatoMeaning = "Zwiad / Rozpoznanie (Recon)",       Label = "[/] NATO Zwiad / Recon" },
            new NatoSymbolOption { Code = "NATO_MED",    ShortBadge = "+",   NatoMeaning = "Punkt Medyczny / MEDEVAC",          Label = "[+] NATO Punkt Medyczny / MEDEVAC" },
            new NatoSymbolOption { Code = "NATO_COMMS",  ShortBadge = "⚡",  NatoMeaning = "Węzeł Łączności / Przekaźnik",      Label = "[⚡] NATO Węzeł Łączności / LoRa" },
            new NatoSymbolOption { Code = "NATO_SUPPLY", ShortBadge = "SUP", NatoMeaning = "Punkt Amunicyjny / Logistyka",      Label = "[SUP] NATO Punkt Amunicyjny / Logistyka" },
            new NatoSymbolOption { Code = "NATO_OP",     ShortBadge = "△",   NatoMeaning = "Punkt Obserwacyjny (OP)",           Label = "[△] NATO Punkt Obserwacyjny (OP)" },
            new NatoSymbolOption { Code = "NATO_SNIPER", ShortBadge = "⊕",   NatoMeaning = "Snajper / Strzelec Wyborowy",       Label = "[⊕] NATO Snajper / Strzelec" },
            new NatoSymbolOption { Code = "NATO_CP",     ShortBadge = "CP",  NatoMeaning = "Punkt Kontrolny / Respawn",         Label = "[CP] NATO Punkt Kontrolny / Respawn" },
            new NatoSymbolOption { Code = "NATO_LZ",     ShortBadge = "LZ",  NatoMeaning = "Strefa Lądowania / Zrzutu",         Label = "[LZ] NATO Strefa Lądowania / Zrzutu" },
            new NatoSymbolOption { Code = "NATO_OBJ",    ShortBadge = "OBJ", NatoMeaning = "Cel Operacji (Objective)",          Label = "[OBJ] NATO Cel Operacji (Objective)" },
            new NatoSymbolOption { Code = "NATO_HAZARD", ShortBadge = "!",   NatoMeaning = "Zagrożenie / Strefa No-Go",         Label = "[!] NATO Zagrożenie / Strefa No-Go" }
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
        private static readonly SemaphoreSlim TileThrottle = new SemaphoreSlim(8, 8);
        private static readonly ConcurrentDictionary<string, byte[]> RawTileCache = new ConcurrentDictionary<string, byte[]>();
        private static readonly ConcurrentDictionary<string, BitmapSource> DecodedImageCache = new ConcurrentDictionary<string, BitmapSource>();
        private static readonly ConcurrentDictionary<string, byte> PendingTileUrls = new ConcurrentDictionary<string, byte>();
        private static readonly ConcurrentDictionary<string, FormattedText> GlyphCache = new ConcurrentDictionary<string, FormattedText>();
        private static readonly Typeface BoldSegoeTypeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

        private static Window _hostWindow;
        private static UIElement _originalWindowContent;
        private static Border _fullScreenOverlay, _sheetFrameBorder;
        private static Grid _viewportHostGrid, _mapCompositeGrid;
        private static Canvas _tileCanvas, _lidarTileCanvas, _interactiveCanvas;
        private static FastTacticalOverlayRenderer _fastOverlayRenderer;
        private static Rectangle _dimmerRect;
        private static Button _tabBuilderBtn, _tabMapBtn, _btnToolPan, _btnToolMarker, _btnToolDot, _btnToolAo, _btnToolSector, _btnToolRoute;
        private static readonly List<Button> _scalePresetButtons = new List<Button>();
        private static TextBlock _statusText, _coordsBadge, _sheetInfoBadge, _offlineEstimateLabel, _offlineProgressLabel;
        private static ProgressBar _offlineProgressBar;
        private static StackPanel _objectsListStack;
        private static WrapPanel _factionsCheckboxesWrap;
        private static readonly HashSet<string> _selectedMapFactions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static CancellationTokenSource _renderCts, _offlineDownloadCts;
        private static DateTime _lastPanTileRefreshUtc = DateTime.MinValue;

        private static double _centerLat = 49.9135, _centerLon = 19.0238, _dragStartLat, _dragStartLon;
        private static int _zoom = 16, _targetScaleDenominator = 5000, _effectiveMgrsStepMeters = 100;
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
        private static ComboBox _fmtComboBox, _oriComboBox, _layerComboBox;

        private static bool _showNorthArrow = true, _showScaleBar = true, _showLegend = true, _showNatoSymbolsInLegend = true;
        private static string _northArrowPos = "Lewy górny róg", _scaleBarPos = "Lewy dolny róg", _legendPos = "Prawy górny róg";
        private static double? _northNormX, _northNormY, _scaleNormX, _scaleNormY, _legendNormX, _legendNormY;
        private static string _kmzGrgMode = "Przezroczysta siatka";

        private static bool _isPanningMap;
        private static Point _mouseDownPoint;
        private static Point _currentHoverScreenPt;
        private static bool _isHoveringCanvas;
        private static TacticalMapObjectItem _draggedObject;
        private static int _draggedVertexIndex = -1;
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
                    ServerCertificateCustomValidationCallback = (s, c, ch, e) => true,
                    MaxConnectionsPerServer = 16
                };
                var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
                c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
                c.DefaultRequestHeaders.Referrer = new Uri("https://mapy.geoportal.gov.pl/");
                c.DefaultRequestHeaders.Accept.ParseAdd("image/avif,image/webp,image/apng,image/*,*/*;q=0.8");
                return c;
            }
            catch
            {
                return new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
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

        // =========================================================================
        // MATEMATYKA GEODEZYJNA: DOKŁADNA SKALA 1:X, UKŁAD UTM / MGRS ORAZ WGS84
        // =========================================================================
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

        // Wylicza rzeczywistą liczbę metrów terenu przypadającą na 1 piksel ekranu dla wybranego formatu (w mm) i skali 1:X
        private static double GetExactMetersPerPixel()
        {
            var (wMm, _) = GetActiveSheetDimensionsMm();
            var (canvasW, _) = GetExactCanvasSize();
            double realTerrainWidthMeters = (wMm / 1000.0) * _targetScaleDenominator;
            return Math.Max(0.04, realTerrainWidthMeters / Math.Max(100.0, canvasW));
        }

        // Rozmiar pojedynczego kafelka WebMercator (256 px) przeskalowany tak, aby mapa miała dokładnie skalę 1:X
        private static double GetScaledTileSizePx()
        {
            double nativeTileMetersPerPx = (156543.03392 * Math.Cos(_centerLat * Math.PI / 180.0)) / Math.Pow(2.0, _zoom);
            double exactMetersPerPx = GetExactMetersPerPixel();
            return 256.0 * (nativeTileMetersPerPx / exactMetersPerPx);
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

        private static (double X, double Y) LatLonToEpsg3857(double lat, double lon) =>
            (lon * 20037508.34 / 180.0, Math.Log(Math.Tan((90.0 + lat) * Math.PI / 360.0)) / (Math.PI / 180.0) * 20037508.34 / 180.0);

        // Dokładna transformacja geodezyjna WGS84 (Lat/Lon) -> UTM (Easting, Northing w metrach) dla siatki MGRS
        private static void LatLonToUtmMeters(double lat, double lon, int forcedZone, out double easting, out double northing)
        {
            const double a = 6378137.0;
            const double eccSquared = 0.00669438;
            const double k0 = 0.9996;

            double latRad = lat * Math.PI / 180.0;
            double lonRad = lon * Math.PI / 180.0;
            double lonOriginRad = ((forcedZone - 1) * 6 - 180 + 3) * Math.PI / 180.0;

            double eccPrimeSquared = eccSquared / (1.0 - eccSquared);
            double N = a / Math.Sqrt(1.0 - eccSquared * Math.Sin(latRad) * Math.Sin(latRad));
            double T = Math.Tan(latRad) * Math.Tan(latRad);
            double C = eccPrimeSquared * Math.Cos(latRad) * Math.Cos(latRad);
            double A = Math.Cos(latRad) * (lonRad - lonOriginRad);

            double M = a * ((1.0 - eccSquared / 4.0 - 3.0 * eccSquared * eccSquared / 64.0) * latRad
                - (3.0 * eccSquared / 8.0 + 3.0 * eccSquared * eccSquared / 32.0) * Math.Sin(2.0 * latRad)
                + (15.0 * eccSquared * eccSquared / 256.0) * Math.Sin(4.0 * latRad));

            easting = k0 * N * (A + (1.0 - T + C) * A * A * A / 6.0
                + (5.0 - 18.0 * T + T * T + 72.0 * C - 58.0 * eccPrimeSquared) * Math.Pow(A, 5) / 120.0) + 500000.0;

            northing = k0 * (M + N * Math.Tan(latRad) * (A * A / 2.0
                + (5.0 - T + 9.0 * C + 4.0 * C * C) * Math.Pow(A, 4) / 24.0
                + (61.0 - 58.0 * T + T * T + 600.0 * C - 330.0 * eccPrimeSquared) * Math.Pow(A, 6) / 720.0));
            if (lat < 0) northing += 10000000.0;
        }

        private static string GetEpsg3857BBox(int tx, int ty, int zoom)
        {
            var (latNorth, lonWest) = TileXYToLatLon(tx, ty, zoom);
            var (latSouth, lonEast) = TileXYToLatLon(tx + 1, ty + 1, zoom);
            var (minX, minY) = LatLonToEpsg3857(latSouth, lonWest);
            var (maxX, maxY) = LatLonToEpsg3857(latNorth, lonEast);
            return string.Create(CultureInfo.InvariantCulture, $"{minX:0.00},{minY:0.00},{maxX:0.00},{maxY:0.00}");
        }

        // Dla podkładów WMS z Geoportalu używamy bloków 512x512 px (4x mniej zapytań HTTP i płynne buforowanie kafelków w RAM)
        private static string BuildTileUrl(TacticalMapLayerDef layer, int tx, int ty, int zoom)
        {
            if (layer.IsWmsEpsg3857)
                return layer.UrlTemplate.Replace("{bbox}", GetEpsg3857BBox(tx, ty, zoom)).Replace("{w}", "512").Replace("{h}", "512");
            return layer.UrlTemplate.Replace("{z}", zoom.ToString()).Replace("{x}", tx.ToString()).Replace("{y}", ty.ToString());
        }

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
            double stepRad = Math.PI / 4.0;
            double snappedRad = Math.Round(angleRad / stepRad) * stepRad;
            snappedAngleDeg = ((int)Math.Round(snappedRad * 180.0 / Math.PI) % 360 + 360) % 360;

            return new Point(anchorPt.X + dist * Math.Cos(snappedRad), anchorPt.Y + dist * Math.Sin(snappedRad));
        }

        // =========================================================================
        // PŁYNNE PRZEWIJANIE MAPY Z DOCZYTYWANIEM W TLE I PAMIĘCIĄ PODRĘCZNĄ RAM
        // =========================================================================
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
                    if (DecodedImageCache.TryGetValue(url, out var secondCheck)) return secondCheck;
                    for (int attempt = 0; attempt < 2 && !ct.IsCancellationRequested; attempt++)
                    {
                        try
                        {
                            byte[] data = await Http.GetByteArrayAsync(url, ct);
                            if (IsValidImagePayload(data)) { validBytes = data; RawTileCache[url] = data; break; }
                        }
                        catch { }
                        if (attempt == 0) await Task.Delay(75, ct);
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
                if (DecodedImageCache.Count > 550) DecodedImageCache.Clear();
                if (RawTileCache.Count > 1500) RawTileCache.Clear();

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

        // Natychmiastowo rozmieszcza już pobrane w pamięci RAM kafelki (60 FPS podczas ruchu myszą) i zleca w tle dociągnięcie brakujących z buforem wyprzedzającym
        private static void RenderLayerTilesSmoothly(Canvas targetCanvas, TacticalMapLayerDef layer, double w, double h, double opacity, CancellationToken ct)
        {
            if (targetCanvas == null || layer == null) return;

            // Dla WMS z Geoportalu stosujemy poziom zoomu o 1 oczko niższy (kafelki 512x512 px), co daje 4x mniej zapytań i brak blokad serwera!
            int effectiveZoom = layer.IsWmsEpsg3857 ? Math.Max(5, _zoom - 1) : _zoom;
            double zoomScaleFactor = Math.Pow(2.0, _zoom - effectiveZoom);
            double tilePx = GetScaledTileSizePx() * zoomScaleFactor;

            var (centerTileX, centerTileY) = LatLonToTileXY(_centerLat, _centerLon, effectiveZoom);
            // Overscan = 1 kafelek zapasu z każdej strony poza krawędzią ekranu (płynne wchodzenie kafelków przy przewijaniu)
            int minTx = (int)Math.Floor(centerTileX - (w / 2.0) / tilePx) - 1;
            int maxTx = (int)Math.Floor(centerTileX + (w / 2.0) / tilePx) + 1;
            int minTy = (int)Math.Floor(centerTileY - (h / 2.0) / tilePx) - 1;
            int maxTy = (int)Math.Floor(centerTileY + (h / 2.0) / tilePx) + 1;
            int maxIndex = (1 << effectiveZoom) - 1;

            targetCanvas.Children.Clear();
            targetCanvas.Opacity = opacity;

            for (int tx = minTx; tx <= maxTx; tx++)
            {
                for (int ty = minTy; ty <= maxTy; ty++)
                {
                    if (ty < 0 || ty > maxIndex) continue;
                    int wrappedTx = ((tx % (maxIndex + 1)) + (maxIndex + 1)) % (maxIndex + 1);
                    double sx = (w / 2.0) + (tx - centerTileX) * tilePx;
                    double sy = (h / 2.0) + (ty - centerTileY) * tilePx;
                    string url = BuildTileUrl(layer, wrappedTx, ty, effectiveZoom);

                    if (DecodedImageCache.TryGetValue(url, out var readyBmp))
                    {
                        var img = new Image { Source = readyBmp, Width = tilePx + 0.8, Height = tilePx + 0.8, Stretch = Stretch.Fill };
                        Canvas.SetLeft(img, sx);
                        Canvas.SetTop(img, sy);
                        targetCanvas.Children.Add(img);
                    }
                    else
                    {
                        // Jeśli w pamięci RAM jest kafelek z poprzedniego przybliżenia (Parent Tile), wyświetl go tymczasowo zamiast pustej dziury!
                        if (effectiveZoom > 6)
                        {
                            int pZ = effectiveZoom - 1;
                            int pTx = wrappedTx / 2, pTy = ty / 2;
                            string parentUrl = BuildTileUrl(layer, pTx, pTy, pZ);
                            if (DecodedImageCache.TryGetValue(parentUrl, out var parentBmp))
                            {
                                var crop = new CroppedBitmap(parentBmp, new Int32Rect((wrappedTx % 2) * (parentBmp.PixelWidth / 2), (ty % 2) * (parentBmp.PixelHeight / 2), parentBmp.PixelWidth / 2, parentBmp.PixelHeight / 2));
                                crop.Freeze();
                                var fallbackImg = new Image { Source = crop, Width = tilePx + 0.8, Height = tilePx + 0.8, Stretch = Stretch.Fill };
                                Canvas.SetLeft(fallbackImg, sx);
                                Canvas.SetTop(fallbackImg, sy);
                                targetCanvas.Children.Add(fallbackImg);
                            }
                        }

                        // Zleć pobranie brakującego kafelka w tle bez blokowania interfejsu
                        if (PendingTileUrls.TryAdd(url, 1))
                        {
                            string fbUrl = (!string.IsNullOrEmpty(layer.FallbackUrlTemplate) && layer.IsWmsEpsg3857)
                                ? layer.FallbackUrlTemplate.Replace("{bbox}", GetEpsg3857BBox(wrappedTx, ty, effectiveZoom)).Replace("{w}", "512").Replace("{h}", "512")
                                : null;

                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    var downloaded = await FetchDecodedImageAsync(url, fbUrl, ct);
                                    if (downloaded != null && !ct.IsCancellationRequested && Application.Current != null)
                                    {
                                        _ = Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                                        {
                                            if (_fullScreenOverlay?.Visibility == Visibility.Visible)
                                                RefreshTileCanvasesOnly();
                                        }), DispatcherPriority.Background);
                                    }
                                }
                                finally
                                {
                                    PendingTileUrls.TryRemove(url, out byte _);
                                }
                            });
                        }
                    }
                }
            }
        }

        private static void RefreshTileCanvasesOnly()
        {
            if (_tileCanvas == null || _activeLayer == null || _fullScreenOverlay?.Visibility != Visibility.Visible) return;
            var (w, h) = GetExactCanvasSize();
            var ct = _renderCts?.Token ?? CancellationToken.None;

            RenderLayerTilesSmoothly(_tileCanvas, _activeLayer, w, h, 1.0, ct);
            if (_lidarTileCanvas != null)
            {
                if (_showLidarSingleWms && _activeLayer.Id != "geoportal_lidar")
                {
                    var lidarDef = Layers.First(l => l.Id == "geoportal_lidar");
                    RenderLayerTilesSmoothly(_lidarTileCanvas, lidarDef, w, h, _lidarOpacity, ct);
                }
                else
                {
                    _lidarTileCanvas.Children.Clear();
                }
            }
        }

        private static async Task RenderMapTilesAsync(bool transparentGrgOnly = false)
        {
            if (_tileCanvas == null || _interactiveCanvas == null || _activeLayer == null || _fullScreenOverlay?.Visibility != Visibility.Visible) return;

            if (_renderCts == null || _renderCts.IsCancellationRequested)
                _renderCts = new CancellationTokenSource();

            UpdateSheetViewportSize();
            RedrawVectorsOnly();

            if (transparentGrgOnly)
            {
                _tileCanvas.Children.Clear();
                _lidarTileCanvas?.Children.Clear();
                _dimmerRect.Visibility = Visibility.Collapsed;
                _mapCompositeGrid.Background = Brushes.Transparent;
                return;
            }

            _mapCompositeGrid.Background = TacticalConfigPanel.BrushCardBg;
            _dimmerRect.Visibility = Visibility.Visible;
            _dimmerRect.Opacity = _mapDimmerOpacity;

            RefreshTileCanvasesOnly();
            await Task.CompletedTask;
        }

        // =========================================================================
        // NISKOPOZIOMOWY SILNIK DRAWINGCONTEXT: GEODEZYJNA SIATKA MGRS/UTM & WGS84 ZSYNCHRONIZOWANA ZE SKALĄ 1:X
        // =========================================================================
        private static FormattedText GetCachedGlyph(string text, double fontSize, Brush fg, string keyPrefix)
        {
            string key = $"{keyPrefix}_{text}_{fontSize:0.0}";
            if (GlyphCache.TryGetValue(key, out var ft)) return ft;
            if (GlyphCache.Count > 600) GlyphCache.Clear();
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

            // 3b. Podgląd rysowanego odcinka na żywo z przyciąganiem SHIFT co 45°
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

            // 4. GEODEZYJNA SIATKA WSPÓŁRZĘDNYCH ZAKOTWICZONA DO ZIEMI I ZSYNCHRONIZOWANA ZE SKALĄ 1:X
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
                else if (_coordSystem.StartsWith("WGS84"))
                {
                    // PRAWDZIWA SIATKA STOPNIOWA WGS84 (Południki i Równoleżniki zakotwiczone do stopni geograficznych!)
                    var (nwLat, nwLon) = ScreenToLatLon(0, 0, w, h);
                    var (seLat, seLon) = ScreenToLatLon(w, h, w, h);
                    double lonSpan = Math.Max(0.0001, Math.Abs(seLon - nwLon));
                    double[] degSteps = { 0.0005, 0.001, 0.002, 0.005, 0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1.0 };
                    double dDeg = 0.005;
                    foreach (var ds in degSteps) { if (lonSpan / ds <= 10.0) { dDeg = ds; break; } }

                    // Podziałka pomocnicza wewnątrz oczek stopniowych (na 2 lub 3 części)
                    if (subAlpha > 4 && !_subGridDivision.StartsWith("Brak"))
                    {
                        int subDiv = _subGridDivision.Contains("50 m") ? 2 : 3;
                        double subDeg = dDeg / subDiv;
                        double firstSubLon = Math.Floor(Math.Min(nwLon, seLon) / dDeg) * dDeg;
                        for (double lon = firstSubLon; lon <= Math.Max(nwLon, seLon) + dDeg; lon += dDeg)
                        {
                            for (int i = 1; i < subDiv; i++)
                            {
                                double sLon = lon + i * subDeg;
                                double sx = LatLonToScreen(_centerLat, sLon, w, h).X;
                                if (sx >= 0 && sx <= w)
                                {
                                    if (useHalo) dc.DrawLine(subHaloPen, new Point(sx, 0), new Point(sx, h));
                                    dc.DrawLine(subPen, new Point(sx, 0), new Point(sx, h));
                                }
                            }
                        }

                        double firstSubLat = Math.Floor(Math.Min(seLat, nwLat) / dDeg) * dDeg;
                        for (double lat = firstSubLat; lat <= Math.Max(seLat, nwLat) + dDeg; lat += dDeg)
                        {
                            for (int j = 1; j < subDiv; j++)
                            {
                                double sLat = lat + j * subDeg;
                                double sy = LatLonToScreen(sLat, _centerLon, w, h).Y;
                                if (sy >= 0 && sy <= h)
                                {
                                    if (useHalo) dc.DrawLine(subHaloPen, new Point(0, sy), new Point(w, sy));
                                    dc.DrawLine(subPen, new Point(0, sy), new Point(w, sy));
                                }
                            }
                        }
                    }

                    if (mainAlpha > 4)
                    {
                        string mainCacheKey = $"WGS_{_gridColorName}_{mainAlpha}";
                        double mainFontSz = 9.5 * _fontScaleMultiplier;
                        double startLon = Math.Floor(Math.Min(nwLon, seLon) / dDeg) * dDeg;
                        for (double lon = startLon; lon <= Math.Max(nwLon, seLon) + dDeg; lon += dDeg)
                        {
                            double sx = LatLonToScreen(_centerLat, lon, w, h).X;
                            if (sx < 0 || sx > w) continue;
                            if (useHalo) dc.DrawLine(mainHaloPen, new Point(sx, 0), new Point(sx, h));
                            dc.DrawLine(mainPen, new Point(sx, 0), new Point(sx, h));
                            DrawFastBadgeOnContext(dc, $"{lon:0.0000}°E", sx + 4, 6, mainBrush, mainFontSz, _mainGridOpacity, mainCacheKey);
                        }

                        double startLat = Math.Floor(Math.Min(seLat, nwLat) / dDeg) * dDeg;
                        for (double lat = startLat; lat <= Math.Max(seLat, nwLat) + dDeg; lat += dDeg)
                        {
                            double sy = LatLonToScreen(lat, _centerLon, w, h).Y;
                            if (sy < 0 || sy > h) continue;
                            if (useHalo) dc.DrawLine(mainHaloPen, new Point(0, sy), new Point(w, sy));
                            dc.DrawLine(mainPen, new Point(0, sy), new Point(w, sy));
                            DrawFastBadgeOnContext(dc, $"{lat:0.0000}°N", 6, sy + 3, mainBrush, mainFontSz, _mainGridOpacity, mainCacheKey);
                        }
                    }
                }
                else
                {
                    // PRAWDZIWA GEODEZYJNA SIATKA MGRS / UTM W METRACH (100% zgodna z Podziałką Skali i zakotwiczona do Ziemi!)
                    double metersPerPixel = GetExactMetersPerPixel();
                    int baseStepMeters = _subGridDivision.Contains("250") ? 250 : (_subGridDivision.Contains("500") ? 500 : 100);

                    // Jeśli przy dużym oddaleniu (np. 1:25000) kratka 100 m miałaby mniej niż 28 px, automatycznie dobieramy czytelny wielokrotny krok metryczny
                    int effectiveStepMeters = baseStepMeters;
                    while (effectiveStepMeters / metersPerPixel < 32.0 && effectiveStepMeters < 10000)
                        effectiveStepMeters *= 2;
                    _effectiveMgrsStepMeters = effectiveStepMeters;

                    double stepPx = effectiveStepMeters / metersPerPixel; // Bez sztucznego Math.Clamp! Dokładnie 1:1 ze skalą mapy!

                    int utmZone = (int)Math.Floor((_centerLon + 180.0) / 6.0) + 1;
                    LatLonToUtmMeters(_centerLat, _centerLon, utmZone, out double centerEasting, out double centerNorthing);

                    double westEasting = centerEasting - (w / 2.0) * metersPerPixel;
                    double eastEasting = centerEasting + (w / 2.0) * metersPerPixel;
                    double northNorthing = centerNorthing + (h / 2.0) * metersPerPixel;
                    double southNorthing = centerNorthing - (h / 2.0) * metersPerPixel;

                    double firstEasting = Math.Floor(westEasting / effectiveStepMeters) * effectiveStepMeters;
                    double firstNorthing = Math.Floor(southNorthing / effectiveStepMeters) * effectiveStepMeters;

                    // 1. Podziałka pomocnicza wewnątrz kratki MGRS: Keypad 1-9 (3x3) lub Ćwiartki A-D (2x2)
                    if ((_subGridDivision.Contains("50 m") || _subGridDivision.Contains("33 m")) && subAlpha > 4 && stepPx >= 42.0)
                    {
                        bool isQuarters = _subGridDivision.Contains("50 m");
                        int div = isQuarters ? 2 : 3;
                        double subStepMeters = (double)effectiveStepMeters / div;
                        double subStepPx = stepPx / div;

                        for (double east = firstEasting; east <= eastEasting + effectiveStepMeters; east += effectiveStepMeters)
                        {
                            double baseX = (w / 2.0) + (east - centerEasting) / metersPerPixel;
                            for (int i = 1; i < div; i++)
                            {
                                double sx = baseX + i * subStepPx;
                                if (sx < 0 || sx > w) continue;
                                if (useHalo) dc.DrawLine(subHaloPen, new Point(sx, 0), new Point(sx, h));
                                dc.DrawLine(subPen, new Point(sx, 0), new Point(sx, h));
                            }
                        }

                        for (double north = firstNorthing; north <= northNorthing + effectiveStepMeters; north += effectiveStepMeters)
                        {
                            double baseY = (h / 2.0) - (north - centerNorthing) / metersPerPixel;
                            for (int j = 1; j < div; j++)
                            {
                                double sy = baseY - j * subStepPx;
                                if (sy < 0 || sy > h) continue;
                                if (useHalo) dc.DrawLine(subHaloPen, new Point(0, sy), new Point(w, sy));
                                dc.DrawLine(subPen, new Point(0, sy), new Point(w, sy));
                            }
                        }

                        if (_gridLabelsMode.Contains("Ćwiartki/Keypad") && stepPx >= 64.0)
                        {
                            string[,] keypad3x3 = { { "7", "8", "9" }, { "4", "5", "6" }, { "1", "2", "3" } };
                            string[,] quarters2x2 = { { "A", "B" }, { "C", "D" } };
                            string subCacheKey = $"SUB_{_gridColorName}_{subAlpha}";
                            double subFontSz = 8.5 * _fontScaleMultiplier;

                            for (double east = firstEasting; east <= eastEasting; east += effectiveStepMeters)
                            {
                                double cellLeftX = (w / 2.0) + (east - centerEasting) / metersPerPixel;
                                for (double north = firstNorthing; north <= northNorthing; north += effectiveStepMeters)
                                {
                                    double cellTopY = (h / 2.0) - ((north + effectiveStepMeters) - centerNorthing) / metersPerPixel;
                                    if (cellLeftX + stepPx < 0 || cellLeftX > w || cellTopY + stepPx < 0 || cellTopY > h) continue;

                                    for (int r = 0; r < div; r++)
                                    {
                                        for (int c = 0; c < div; c++)
                                        {
                                            double cx = cellLeftX + (c + 0.5) * subStepPx - 4;
                                            double cy = cellTopY + (r + 0.5) * subStepPx - 6;
                                            if (cx > 4 && cx < w - 14 && cy > 4 && cy < h - 14)
                                            {
                                                string subTxt = isQuarters ? quarters2x2[r, c] : keypad3x3[r, c];
                                                DrawFastBadgeOnContext(dc, subTxt, cx, cy, subBrush, subFontSz, _subGridOpacity, subCacheKey);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // 2. Główne linie metryczne MGRS / UTM oraz ich prawdziwe oznaczenia 100m (Easting / Northing)
                    if (mainAlpha > 4)
                    {
                        string mainCacheKey = $"MAIN_{_gridColorName}_{mainAlpha}";
                        double mainFontSz = 9.5 * _fontScaleMultiplier;

                        for (double east = firstEasting; east <= eastEasting; east += effectiveStepMeters)
                        {
                            double x = (w / 2.0) + (east - centerEasting) / metersPerPixel;
                            if (x < 0 || x > w) continue;
                            if (useHalo) dc.DrawLine(mainHaloPen, new Point(x, 0), new Point(x, h));
                            dc.DrawLine(mainPen, new Point(x, 0), new Point(x, h));

                            int mgrsHundredMeters = ((int)Math.Round(east / 100.0)) % 1000;
                            if (mgrsHundredMeters < 0) mgrsHundredMeters += 1000;
                            DrawFastBadgeOnContext(dc, $"{mgrsHundredMeters:D3}", x + 4, 6, mainBrush, mainFontSz, _mainGridOpacity, mainCacheKey);
                        }

                        for (double north = firstNorthing; north <= northNorthing; north += effectiveStepMeters)
                        {
                            double y = (h / 2.0) - (north - centerNorthing) / metersPerPixel;
                            if (y < 0 || y > h) continue;
                            if (useHalo) dc.DrawLine(mainHaloPen, new Point(0, y), new Point(w, y));
                            dc.DrawLine(mainPen, new Point(0, y), new Point(w, y));

                            int mgrsHundredMeters = ((int)Math.Round(north / 100.0)) % 1000;
                            if (mgrsHundredMeters < 0) mgrsHundredMeters += 1000;
                            DrawFastBadgeOnContext(dc, $"{mgrsHundredMeters:D3}", 6, y + 3, mainBrush, mainFontSz, _mainGridOpacity, mainCacheKey);
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

        // =========================================================================
        // SERIALIZACJA STANU MAPY DO AUTO-SESJI ORAZ PLIKU PROJEKTU (.FTHPROJ / .JSON)
        // =========================================================================
        public static JsonObject ExportMapStateToJson()
        {
            var root = new JsonObject
            {
                ["centerLat"] = _centerLat,
                ["centerLon"] = _centerLon,
                ["zoom"] = _zoom,
                ["scale"] = _targetScaleDenominator,
                ["orientation"] = _sheetOrientation,
                ["sheetFormat"] = _selectedSheet?.Name ?? "",
                ["layerId"] = _activeLayer?.Id ?? "google_hyb",
                ["showLidar"] = _showLidarSingleWms,
                ["lidarOpacity"] = _lidarOpacity,
                ["coordSystem"] = _coordSystem,
                ["subGridDivision"] = _subGridDivision,
                ["gridLabelsMode"] = _gridLabelsMode,
                ["labelBgStyle"] = _labelBgStyle,
                ["gridThickness"] = _gridThicknessMultiplier,
                ["fontScale"] = _fontScaleMultiplier,
                ["mainGridOpacity"] = _mainGridOpacity,
                ["subGridOpacity"] = _subGridOpacity,
                ["mapDimmerOpacity"] = _mapDimmerOpacity,
                ["gridColorName"] = _gridColorName,
                ["gridHaloMode"] = _gridHaloMode,
                ["aoOutPattern"] = _aoOutPattern,
                ["aoOutMaskOpacity"] = _aoOutMaskOpacity,
                ["showAllObjectLabels"] = _showAllObjectLabels,
                ["showNorthArrow"] = _showNorthArrow,
                ["showScaleBar"] = _showScaleBar,
                ["showLegend"] = _showLegend,
                ["showNatoSymbolsInLegend"] = _showNatoSymbolsInLegend
            };

            var facArr = new JsonArray();
            foreach (var fn in _selectedMapFactions) facArr.Add(fn);
            root["selectedFactions"] = facArr;

            var arr = new JsonArray();
            foreach (var o in _mapObjects)
            {
                var pts = new JsonArray();
                foreach (var p in o.Points) pts.Add(new JsonObject { ["lat"] = p.Lat, ["lon"] = p.Lon });
                arr.Add(new JsonObject
                {
                    ["kind"] = o.Kind,
                    ["symbol"] = o.SymbolCode,
                    ["name"] = o.Name,
                    ["color"] = o.ColorHex,
                    ["showLabel"] = o.ShowLabel,
                    ["points"] = pts
                });
            }
            root["objects"] = arr;
            return root;
        }

        public static void ImportMapStateFromJson(JsonObject root)
        {
            if (root == null) return;
            try
            {
                if (double.TryParse(root["centerLat"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double la)) _centerLat = la;
                if (double.TryParse(root["centerLon"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double lo)) _centerLon = lo;
                if (int.TryParse(root["scale"]?.ToString(), out int sc)) _targetScaleDenominator = Math.Clamp(sc, 500, 250000);
                if (root["orientation"] != null) _sheetOrientation = root["orientation"].ToString();
                if (root["sheetFormat"] != null)
                {
                    string sfName = root["sheetFormat"].ToString();
                    var foundSf = SheetFormats.FirstOrDefault(s => s.Name.Equals(sfName, StringComparison.OrdinalIgnoreCase));
                    if (foundSf != null) _selectedSheet = foundSf;
                }
                if (root["layerId"] != null)
                {
                    string lid = root["layerId"].ToString();
                    var foundLayer = Layers.FirstOrDefault(l => l.Id.Equals(lid, StringComparison.OrdinalIgnoreCase));
                    if (foundLayer != null) _activeLayer = foundLayer;
                }
                if (bool.TryParse(root["showLidar"]?.ToString(), out bool sl)) _showLidarSingleWms = sl;
                if (double.TryParse(root["lidarOpacity"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double lop)) _lidarOpacity = lop;
                if (root["coordSystem"] != null) _coordSystem = root["coordSystem"].ToString();
                if (root["subGridDivision"] != null) _subGridDivision = root["subGridDivision"].ToString();
                if (root["gridLabelsMode"] != null) _gridLabelsMode = root["gridLabelsMode"].ToString();
                if (root["labelBgStyle"] != null) _labelBgStyle = root["labelBgStyle"].ToString();
                if (double.TryParse(root["gridThickness"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double gt)) _gridThicknessMultiplier = gt;
                if (double.TryParse(root["fontScale"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double fs)) _fontScaleMultiplier = fs;
                if (double.TryParse(root["mainGridOpacity"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double mgo)) _mainGridOpacity = mgo;
                if (double.TryParse(root["subGridOpacity"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double sgo)) _subGridOpacity = sgo;
                if (double.TryParse(root["mapDimmerOpacity"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double mdo)) _mapDimmerOpacity = mdo;
                if (root["gridColorName"] != null) _gridColorName = root["gridColorName"].ToString();
                if (root["gridHaloMode"] != null) _gridHaloMode = root["gridHaloMode"].ToString();
                if (root["aoOutPattern"] != null) _aoOutPattern = root["aoOutPattern"].ToString();
                if (double.TryParse(root["aoOutMaskOpacity"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double aom)) _aoOutMaskOpacity = aom;
                if (bool.TryParse(root["showAllObjectLabels"]?.ToString(), out bool sal)) _showAllObjectLabels = sal;
                if (bool.TryParse(root["showNorthArrow"]?.ToString(), out bool sna)) _showNorthArrow = sna;
                if (bool.TryParse(root["showScaleBar"]?.ToString(), out bool ssb)) _showScaleBar = ssb;
                if (bool.TryParse(root["showLegend"]?.ToString(), out bool slg)) _showLegend = slg;
                if (bool.TryParse(root["showNatoSymbolsInLegend"]?.ToString(), out bool sns)) _showNatoSymbolsInLegend = sns;

                if (root["selectedFactions"] is JsonArray fArr)
                {
                    _selectedMapFactions.Clear();
                    foreach (var item in fArr) if (item != null) _selectedMapFactions.Add(item.ToString());
                }

                if (root["objects"] is JsonArray arr)
                {
                    _mapObjects.Clear();
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
                                if (double.TryParse(p["lat"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double pla) &&
                                    double.TryParse(p["lon"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double plo))
                                    item.Points.Add((pla, plo));
                        if (item.Points.Count > 0) _mapObjects.Add(item);
                    }
                }

                if (_fmtComboBox != null) _fmtComboBox.SelectedItem = _selectedSheet;
                if (_oriComboBox != null) _oriComboBox.SelectedItem = _sheetOrientation;
                if (_layerComboBox != null) _layerComboBox.SelectedItem = _activeLayer;
                if (_hostWindow != null) RefreshObjectsListUI(_hostWindow);
                RebuildFactionCheckboxesUI();
                ApplyScaleDenominator(_targetScaleDenominator);
            }
            catch { }
        }
    }
}
