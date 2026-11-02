#pragma warning disable CS8619
#pragma warning disable CS0414, CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
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
using Path = System.IO.Path;

namespace FieldTakHub.Builder.Services
{
    public class TacticalMapLayerDef
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public bool IsWmsEpsg3857 { get; set; }
        public string UrlTemplate { get; set; }
        public string FallbackUrlTemplate { get; set; }
        public string SecondFallbackUrlTemplate { get; set; }
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
        public string Kind { get; set; } = "Marker";
        public string SymbolCode { get; set; } = "NATO_HQ";
        public string Name { get; set; } = "HQ";
        public string ColorHex { get; set; } = "#2684FF";
        public bool ShowLabel { get; set; } = true;
        public double SizeMultiplier { get; set; } = 1.0;
        public double RotationDeg { get; set; } = 0.0;
        public List<(double Lat, double Lon)> Points { get; set; } = new List<(double Lat, double Lon)>();
    }

    internal sealed class GeoAnchoredWmsSheet
    {
        public string LayerId { get; set; } = "";
        public double NorthLat { get; set; }
        public double SouthLat { get; set; }
        public double EastLon { get; set; }
        public double WestLon { get; set; }
        public double CenterLat { get; set; }
        public double CenterLon { get; set; }
        public int ScaleDenominator { get; set; }
        public bool IsDraftResolution { get; set; }
        public BitmapSource Bitmap { get; set; }
        public double AppliedBoost { get; set; } = -1.0;
        public BitmapSource BrightenedBitmap { get; set; }
    }

    internal static class NativeSqliteWriter
    {
        private const string SqliteDll = "winsqlite3.dll";
        [DllImport(SqliteDll, CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_open(byte[] filenameUtf8, out IntPtr db);
        [DllImport(SqliteDll, CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_close(IntPtr db);
        [DllImport(SqliteDll, CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_exec(IntPtr db, byte[] sqlUtf8, IntPtr callback, IntPtr arg, out IntPtr errMsg);
        [DllImport(SqliteDll, CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_prepare_v2(IntPtr db, byte[] sqlUtf8, int nByte, out IntPtr stmt, IntPtr pzTail);
        [DllImport(SqliteDll, CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_bind_int(IntPtr stmt, int index, int val);
        [DllImport(SqliteDll, CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_bind_blob(IntPtr stmt, int index, byte[] data, int nData, IntPtr destructor);
        [DllImport(SqliteDll, CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_step(IntPtr stmt);
        [DllImport(SqliteDll, CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_reset(IntPtr stmt);
        [DllImport(SqliteDll, CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_finalize(IntPtr stmt);

        private static byte[] ToUtf8NullTerm(string s) => Encoding.UTF8.GetBytes((s ?? "") + "\0");
        private static void ExecSql(IntPtr db, string sql) => sqlite3_exec(db, ToUtf8NullTerm(sql), IntPtr.Zero, IntPtr.Zero, out IntPtr _);

        public static void WriteMbTilesDatabase(string filePath, string mapTitle, int minZoom, int maxZoom, double north, double south, double east, double west, List<(int Z, int X, int Y, byte[] Data)> tiles)
        {
            if (File.Exists(filePath)) File.Delete(filePath);
            if (sqlite3_open(ToUtf8NullTerm(filePath), out IntPtr db) != 0) return;
            try
            {
                ExecSql(db, "PRAGMA synchronous = OFF; PRAGMA journal_mode = MEMORY;");
                ExecSql(db, "CREATE TABLE metadata (name text, value text); CREATE TABLE tiles (zoom_level integer, tile_column integer, tile_row integer, tile_data blob); CREATE UNIQUE INDEX tile_index on tiles (zoom_level, tile_column, tile_row);");
                string bounds = string.Create(CultureInfo.InvariantCulture, $"{west:0.000000},{south:0.000000},{east:0.000000},{north:0.000000}");
                string center = string.Create(CultureInfo.InvariantCulture, $"{(west + east) / 2.0:0.000000},{(south + north) / 2.0:0.000000},{minZoom}");
                string safeTitle = (mapTitle ?? "FieldTakHub_Map").Replace("'", "");
                ExecSql(db, $"BEGIN TRANSACTION; INSERT INTO metadata VALUES ('name','{safeTitle}'),('type','baselayer'),('version','1.2'),('description','Field TAK Hub 2.4 Offline Map'),('format','jpg'),('bounds','{bounds}'),('center','{center}'),('minzoom','{minZoom}'),('maxzoom','{maxZoom}');");
                byte[] insSql = ToUtf8NullTerm("INSERT OR REPLACE INTO tiles (zoom_level, tile_column, tile_row, tile_data) VALUES (?, ?, ?, ?);");
                if (sqlite3_prepare_v2(db, insSql, insSql.Length, out IntPtr stmt, IntPtr.Zero) == 0)
                {
                    try
                    {
                        foreach (var t in tiles)
                        {
                            if (t.Data == null || t.Data.Length == 0) continue;
                            sqlite3_bind_int(stmt, 1, t.Z); sqlite3_bind_int(stmt, 2, t.X); sqlite3_bind_int(stmt, 3, (1 << t.Z) - 1 - t.Y);
                            sqlite3_bind_blob(stmt, 4, t.Data, t.Data.Length, IntPtr.Zero); sqlite3_step(stmt); sqlite3_reset(stmt);
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
                ExecSql(db, $"CREATE TABLE info (minzoom INTEGER, maxzoom INTEGER); CREATE TABLE tiles (x int, y int, z int, s int, image blob, PRIMARY KEY (x,y,z,s)); CREATE INDEX IND on tiles (x,y,z,s); BEGIN TRANSACTION; INSERT INTO info VALUES ({17 - maxZoom}, {17 - minZoom});");
                byte[] insSql = ToUtf8NullTerm("INSERT OR REPLACE INTO tiles (x, y, z, s, image) VALUES (?, ?, ?, 0, ?);");
                if (sqlite3_prepare_v2(db, insSql, insSql.Length, out IntPtr stmt, IntPtr.Zero) == 0)
                {
                    try
                    {
                        foreach (var t in tiles)
                        {
                            if (t.Data == null || t.Data.Length == 0) continue;
                            sqlite3_bind_int(stmt, 1, t.X); sqlite3_bind_int(stmt, 2, t.Y); sqlite3_bind_int(stmt, 3, 17 - t.Z);
                            sqlite3_bind_blob(stmt, 4, t.Data, t.Data.Length, IntPtr.Zero); sqlite3_step(stmt); sqlite3_reset(stmt);
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
            _renderCallback = renderCallback; IsHitTestVisible = false; SnapsToDevicePixels = true;
        }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if (ActualWidth > 10 && ActualHeight > 10) _renderCallback?.Invoke(dc, ActualWidth, ActualHeight);
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
            new NatoSymbolOption { Code = "SHAPE_ARROW",    ShortBadge = "➤",   NatoMeaning = "Strzałka kierunkowa / Kierunek natarcia", Label = "➤ Strzałka kierunkowa (Obracana 360°)" },
            new NatoSymbolOption { Code = "SHAPE_TENT",     ShortBadge = "⛺",  NatoMeaning = "Namiot / Baza polowa / Respawn / Safezone", Label = "⛺ Namiot / Obóz / Respawn" },
            new NatoSymbolOption { Code = "SHAPE_CROSS",    ShortBadge = "✖",   NatoMeaning = "Krzyżyk taktyczny / Punkt celowania",      Label = "✖ Krzyżyk (Cross)" },
            new NatoSymbolOption { Code = "SHAPE_CIRCLE",   ShortBadge = "⭕",  NatoMeaning = "Koło / Punkt orientacyjny",                Label = "⭕ Koło / Okrąg" },
            new NatoSymbolOption { Code = "SHAPE_SQUARE",   ShortBadge = "⬛",  NatoMeaning = "Kwadrat / Obiekt umocniony",               Label = "⬛ Kwadrat" },
            new NatoSymbolOption { Code = "SHAPE_RECT",     ShortBadge = "▬",   NatoMeaning = "Prostokąt / Budynek / Pojazd",             Label = "▬ Prostokąt (Obracany 360°)" },
            new NatoSymbolOption { Code = "SHAPE_DIAMOND",  ShortBadge = "◆",   NatoMeaning = "Romb / Punkt kluczowy",                    Label = "◆ Romb" },
            new NatoSymbolOption { Code = "SHAPE_TRIANGLE", ShortBadge = "▲",   NatoMeaning = "Trójkąt ostrzegawczy / Stanowisko",        Label = "▲ Trójkąt (Obracany 360°)" },
            new NatoSymbolOption { Code = "NATO_HQ",        ShortBadge = "HQ",  NatoMeaning = "Sztab / Dowództwo (stopka masztu)",        Label = "🚩 [HQ] NATO Sztab / Dowództwo (Flaga)" },
            new NatoSymbolOption { Code = "NATO_INF",       ShortBadge = "X",   NatoMeaning = "Piechota / Drużyna szturmowa",             Label = "[X] NATO Piechota / Drużyna" },
            new NatoSymbolOption { Code = "NATO_RECON",     ShortBadge = "/",   NatoMeaning = "Zwiad / Rozpoznanie (Recon)",              Label = "[/] NATO Zwiad / Recon" },
            new NatoSymbolOption { Code = "NATO_MED",       ShortBadge = "+",   NatoMeaning = "Punkt Medyczny / MEDEVAC",                 Label = "[+] NATO Punkt Medyczny / MEDEVAC" },
            new NatoSymbolOption { Code = "NATO_COMMS",     ShortBadge = "⚡",  NatoMeaning = "Węzeł Łączności / Przekaźnik",             Label = "[⚡] NATO Węzeł Łączności / LoRa" },
            new NatoSymbolOption { Code = "NATO_SUPPLY",    ShortBadge = "SUP", NatoMeaning = "Punkt Amunicyjny / Logistyka",             Label = "[SUP] NATO Punkt Amunicyjny / Logistyka" },
            new NatoSymbolOption { Code = "NATO_OP",        ShortBadge = "△",   NatoMeaning = "Punkt Obserwacyjny (OP)",                  Label = "[△] NATO Punkt Obserwacyjny (OP)" },
            new NatoSymbolOption { Code = "NATO_SNIPER",    ShortBadge = "⊕",   NatoMeaning = "Snajper / Strzelec Wyborowy",              Label = "[⊕] NATO Snajper / Strzelec" },
            new NatoSymbolOption { Code = "NATO_CP",        ShortBadge = "CP",  NatoMeaning = "Punkt Kontrolny / Checkpoint",             Label = "[CP] NATO Punkt Kontrolny / Checkpoint" },
            new NatoSymbolOption { Code = "NATO_LZ",        ShortBadge = "LZ",  NatoMeaning = "Strefa Lądowania / Zrzutu",                Label = "[LZ] NATO Strefa Lądowania / Zrzutu" },
            new NatoSymbolOption { Code = "NATO_OBJ",       ShortBadge = "OBJ", NatoMeaning = "Cel Operacji (Objective)",                 Label = "[OBJ] NATO Cel Operacji (Objective)" },
            new NatoSymbolOption { Code = "NATO_HAZARD",    ShortBadge = "!",   NatoMeaning = "Zagrożenie / Strefa No-Go",                Label = "[!] NATO Zagrożenie / Strefa No-Go" }
        };

        public static readonly List<TacticalMapLayerDef> Layers = new List<TacticalMapLayerDef>
        {
            new TacticalMapLayerDef { Id = "google_hyb", Name = "Google Satelita + Opisy", IsWmsEpsg3857 = false, UrlTemplate = "https://mt1.google.com/vt/lyrs=y&x={x}&y={y}&z={z}" },
            new TacticalMapLayerDef { Id = "google_sat", Name = "Google Satelita (Czysta Ortofotomapa)", IsWmsEpsg3857 = false, UrlTemplate = "https://mt1.google.com/vt/lyrs=s&x={x}&y={y}&z={z}" },
            new TacticalMapLayerDef { Id = "google_ter", Name = "Google Teren (Warstwice)", IsWmsEpsg3857 = false, UrlTemplate = "https://mt1.google.com/vt/lyrs=p&x={x}&y={y}&z={z}" },
            new TacticalMapLayerDef
            {
                Id = "geoportal_std", Name = "Geoportal Ortofotomapa Standard (PZGIK — Cała Polska)", IsWmsEpsg3857 = true,
                UrlTemplate = "https://mapy.geoportal.gov.pl/wss/service/PZGIK/ORTO/WMS/StandardResolution?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&FORMAT=image/jpeg&LAYERS=Raster&STYLES=&SRS=EPSG:3857&BBOX={bbox}&WIDTH={w}&HEIGHT={h}",
                FallbackUrlTemplate = "https://mapy.geoportal.gov.pl/wss/service/PZGIK/ORTO/WMS/StandardResolution?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap&FORMAT=image/jpeg&LAYERS=Raster&STYLES=&CRS=EPSG:2180&BBOX={bbox2180_ne}&WIDTH={w}&HEIGHT={h}",
                SecondFallbackUrlTemplate = "https://services.arcgisonline.com/arcgis/rest/services/World_Imagery/MapServer/export?bbox={bbox}&bboxSR=3857&imageSR=3857&size={w},{h}&format=jpg&f=image"
            },
            new TacticalMapLayerDef
            {
                Id = "geoportal_hires", Name = "Geoportal Ortofotomapa HIGH-RES (Miasta 5cm + Auto-Standard)", IsWmsEpsg3857 = true,
                UrlTemplate = "https://mapy.geoportal.gov.pl/wss/service/PZGIK/ORTO/WMS/HighResolution?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&FORMAT=image/jpeg&LAYERS=Raster&STYLES=&SRS=EPSG:3857&BBOX={bbox}&WIDTH={w}&HEIGHT={h}",
                FallbackUrlTemplate = "https://mapy.geoportal.gov.pl/wss/service/PZGIK/ORTO/WMS/StandardResolution?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&FORMAT=image/jpeg&LAYERS=Raster&STYLES=&SRS=EPSG:3857&BBOX={bbox}&WIDTH={w}&HEIGHT={h}",
                SecondFallbackUrlTemplate = "https://services.arcgisonline.com/arcgis/rest/services/World_Imagery/MapServer/export?bbox={bbox}&bboxSR=3857&imageSR=3857&size={w},{h}&format=jpg&f=image"
            },
            new TacticalMapLayerDef
            {
                Id = "geoportal_lidar", Name = "Geoportal LiDAR ISOK (Cieniowanie NMT GRID1 + Auto-Kontrast 3D)", IsWmsEpsg3857 = true,
                UrlTemplate = "https://mapy.geoportal.gov.pl/wss/service/PZGIK/NMT/GRID1/WMS/ShadedRelief?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap&FORMAT=image/png&TRANSPARENT=TRUE&LAYERS=Raster&STYLES=&CRS=EPSG:2180&BBOX={bbox2180_ne}&WIDTH={w}&HEIGHT={h}",
                FallbackUrlTemplate = "https://mapy.geoportal.gov.pl/wss/service/PZGIK/NMT/GRID1/WMS/ShadedRelief?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap&FORMAT=image/png&TRANSPARENT=TRUE&LAYERS=Raster&STYLES=&CRS=CRS:84&BBOX={bbox4326}&WIDTH={w}&HEIGHT={h}",
                SecondFallbackUrlTemplate = "https://ibasemaps-api.arcgis.com/arcgis/rest/services/Elevation/World_Hillshade/MapServer/export?bbox={bbox}&bboxSR=3857&imageSR=3857&size={w},{h}&format=png&transparent=true&f=image"
            },
            new TacticalMapLayerDef
            {
                Id = "geoportal_topo", Name = "Geoportal Mapa Topograficzna (TOPO / BDOT10k)", IsWmsEpsg3857 = true,
                UrlTemplate = "https://mapy.geoportal.gov.pl/wss/service/img/guest/TOPO/MapServer/WMSServer?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&FORMAT=image/jpeg&LAYERS=Raster&STYLES=&SRS=EPSG:3857&BBOX={bbox}&WIDTH={w}&HEIGHT={h}",
                FallbackUrlTemplate = "https://mapy.geoportal.gov.pl/wss/service/img/guest/TOPO/MapServer/WMSServer?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap&FORMAT=image/jpeg&LAYERS=Raster&STYLES=&CRS=EPSG:2180&BBOX={bbox2180_ne}&WIDTH={w}&HEIGHT={h}",
                SecondFallbackUrlTemplate = "https://services.arcgisonline.com/arcgis/rest/services/World_Topo_Map/MapServer/export?bbox={bbox}&bboxSR=3857&imageSR=3857&size={w},{h}&format=jpg&f=image"
            },
            new TacticalMapLayerDef { Id = "esri_sat", Name = "ESRI World Imagery (Ortofotomapa)", IsWmsEpsg3857 = false, UrlTemplate = "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}" },
            new TacticalMapLayerDef { Id = "osm_std", Name = "OpenStreetMap Standard", IsWmsEpsg3857 = false, UrlTemplate = "https://tile.openstreetmap.org/{z}/{x}/{y}.png" },
            new TacticalMapLayerDef { Id = "open_topomap", Name = "OpenTopoMap (Mapa Topograficzna)", IsWmsEpsg3857 = false, UrlTemplate = "https://a.tile.opentopomap.org/{z}/{x}/{y}.png" },
            new TacticalMapLayerDef { Id = "open_fotomap", Name = "OpenFotoMap / CyclOSM (Siatka Szlaków)", IsWmsEpsg3857 = false, UrlTemplate = "https://a.tile-cyclosm.openstreetmap.fr/cyclosm/{z}/{x}/{y}.png" }
        };

        private const double WmsOverscanFactor = 1.12;
        private static HttpClient Http => GeoportalLidarEngine.FastHttp;
        private static readonly SemaphoreSlim TileThrottle = new SemaphoreSlim(16, 16);
        private static readonly SemaphoreSlim WmsDedicatedThrottle = new SemaphoreSlim(6, 6);
        private static readonly ConcurrentDictionary<string, byte[]> RawTileCache = new ConcurrentDictionary<string, byte[]>();
        private static readonly ConcurrentDictionary<string, BitmapSource> DecodedImageCache = new ConcurrentDictionary<string, BitmapSource>();
        private static readonly ConcurrentDictionary<string, BitmapSource> BrightenedTileCache = new ConcurrentDictionary<string, BitmapSource>();
        private static readonly ConcurrentDictionary<string, byte> PendingTileUrls = new ConcurrentDictionary<string, byte>();
        private static readonly ConcurrentDictionary<string, FormattedText> GlyphCache = new ConcurrentDictionary<string, FormattedText>();
        private static readonly ConcurrentDictionary<string, Brush> FrozenBrushCache = new ConcurrentDictionary<string, Brush>();
        private static readonly ConcurrentDictionary<string, Pen> FrozenPenCache = new ConcurrentDictionary<string, Pen>();
        private static readonly Typeface BoldSegoeTypeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        private static readonly List<GeoAnchoredWmsSheet> _geoAnchoredBaseSheets = new List<GeoAnchoredWmsSheet>();
        private static readonly List<GeoAnchoredWmsSheet> _geoAnchoredLidarSheets = new List<GeoAnchoredWmsSheet>();

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
        private static CancellationTokenSource _renderCts, _wmsSheetCts, _offlineDownloadCts;
        private static DateTime _lastPanTileRefreshUtc = DateTime.MinValue;
        private static DispatcherTimer _tileWatchdogTimer, _brightnessDebounceTimer;
        private static volatile bool _hasMissingVisibleTiles;
        private static volatile bool _isWmsSheetFetchRunning;
        private static int _lastRenderedTileZoom = -1;

        private static double _centerLat = 49.9135, _centerLon = 19.0238, _dragStartLat, _dragStartLon;
        private static double _wmsLoadedCenterLat = -999, _wmsLoadedCenterLon = -999;
        private static int _wmsLoadedScaleDenominator = -1;
        private static string _wmsLoadedLayerId = "";
        private static bool _wmsLoadedLidarFlag = false;

        private static int _zoom = 16, _targetScaleDenominator = 5000, _effectiveMgrsStepMeters = 100;
        private static SheetFormatDef _selectedSheet = SheetFormats[0];
        private static string _sheetOrientation = "Poziomo";
        private static bool _showLidarSingleWms;

        private static int _offlineMinZoom = 13, _offlineMaxZoom = 17;
        private static string _offlineBoundsMode = "Widoczny arkusz mapy";
        private static string _offlineDbFormat = "MBTiles (.mbtiles — ATAK 5.x)";
        private static bool _bakeLidarInOffline = true, _includeVectorsWithOffline = true;

        private static string _coordSystem = "MGRS / UTM (Siatka wojskowa w metrach)";
        private static string _subGridDivision = "Co 33 m (Keypad 1-9)";
        private static string _gridLabelsMode = "MGRS + Ćwiartki/Keypad";
        private static string _labelBgStyle = "Kaseton (Kolor tła RGB)";
        private static double _gridThicknessMultiplier = 1.0, _fontScaleMultiplier = 1.0;
        private static double _mainGridOpacity = 0.85, _subGridOpacity = 0.85, _subLabelBgOpacity = 0.82;
        private static double _mapBrightnessBoost = 0.0, _mapDimmerOpacity = 0.0, _lidarOpacity = 0.55;
        private static string _mainGridColorHex = "#FAD02C", _subGridLineColorHex = "#1FE0C5", _subLabelTextColorHex = "#FFFFFF", _subLabelBgColorHex = "#061014";
        private static string _gridHaloMode = "Pełny czarny obrys";

        private static string _activeTool = "PAN", _selectedObjectColorHex = "#2684FF", _selectedNatoSymbolCode = "SHAPE_ARROW";
        private static bool _showAllObjectLabels = true, _newObjectShowLabelDefault = true, _showRotationHandlesOnMap = true, _isExportingCleanMap = false;
        private static string _aoOutPattern = "Skośne linie (Hatch ///)";
        private static double _aoOutMaskOpacity = 0.45;
        private static readonly List<TacticalMapObjectItem> _mapObjects = new List<TacticalMapObjectItem>();
        private static TacticalMapObjectItem _activeDraftPoly;
        private static TextBox _objectNameInputBox, _scaleInputBox, _searchPlaceBox;
        private static ComboBox _fmtComboBox, _oriComboBox, _layerComboBox;

        private static TacticalMapObjectItem _inspectedObject;
        private static int _inspectedVertexIndex = 0;
        private static TextBlock _inspectorTitleLabel, _inspectorMgrsLabel, _inspectorWgsUtmLabel;

        private static bool _showNorthArrow = true, _showScaleBar = true, _showLegend = true, _showNatoSymbolsInLegend = true;
        private static string _northArrowPos = "Lewy górny róg", _scaleBarPos = "Lewy dolny róg", _legendPos = "Prawy górny róg";
        private static double? _northNormX, _northNormY, _scaleNormX, _scaleNormY, _legendNormX, _legendNormY;
        private static string _kmzGrgMode = "Przezroczysta siatka";

        private static bool _isPanningMap, _isHoveringCanvas;
        private static Point _mouseDownPoint, _currentHoverScreenPt, _markerDragScreenOffset, _rotationCenterScreenPt, _hudStartOffset;
        private static TacticalMapObjectItem _draggedObject;
        private static int _draggedVertexIndex = -1;
        private static (double Lat, double Lon) _wholeShapeDragStartLatLon;
        private static List<(double Lat, double Lon)> _wholeShapeOriginalPoints;
        private static string _draggedHudId;
        private static FrameworkElement _draggedHudElement;
        private static (double Lat, double Lon)? _rulerStart, _rulerEnd;
        private static TacticalMapLayerDef _activeLayer = Layers[0];

        private static void EnsureTileWatchdogStarted()
        {
            if (_tileWatchdogTimer != null || Application.Current == null) return;
            _tileWatchdogTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1400) };
            _tileWatchdogTimer.Tick += (_, __) =>
            {
                if (_fullScreenOverlay?.Visibility != Visibility.Visible || _activeLayer == null || _isPanningMap) return;
                if (!_activeLayer.IsWmsEpsg3857 && _hasMissingVisibleTiles && PendingTileUrls.IsEmpty) RefreshTileCanvasesOnly();
                if ((_activeLayer.IsWmsEpsg3857 || _showLidarSingleWms) && !_isWmsSheetFetchRunning)
                {
                    bool moved = _wmsLoadedScaleDenominator != _targetScaleDenominator ||
                                 Math.Abs(_wmsLoadedCenterLat - _centerLat) > 0.00005 ||
                                 Math.Abs(_wmsLoadedCenterLon - _centerLon) > 0.00005;

                    bool needBase = _activeLayer.IsWmsEpsg3857 && (_wmsLoadedLayerId != _activeLayer.Id || moved);
                    bool needLidar = _showLidarSingleWms && _activeLayer.Id != "geoportal_lidar" && (!_wmsLoadedLidarFlag || moved);

                    if (needBase || needLidar) _ = RenderMapTilesAsync();
                }
            };
            _tileWatchdogTimer.Start();
        }

        private static bool IsValidImagePayload(byte[] data) => GeoportalLidarEngine.IsValidImageBytes(data);

        private static BitmapSource GetGammaBrightenedTile(string url, BitmapSource rawBmp)
        {
            if (rawBmp == null || _mapBrightnessBoost <= 0.01) return rawBmp;
            int boostKey = (int)Math.Round(_mapBrightnessBoost * 100);
            string key = $"{url}_B{boostKey}";
            if (BrightenedTileCache.TryGetValue(key, out var cached)) return cached;
            var lifted = GeoportalLidarEngine.ApplyGammaAndBrightnessToBitmap(rawBmp, _mapBrightnessBoost) ?? rawBmp;
            if (BrightenedTileCache.Count > 500) BrightenedTileCache.Clear();
            BrightenedTileCache[key] = lifted;
            return lifted;
        }

        private static void TriggerLiveBrightnessRefresh()
        {
            GeoportalLidarEngine.ActiveBrightnessBoost = _mapBrightnessBoost;
            if (_brightnessDebounceTimer == null)
            {
                _brightnessDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(35) };
                _brightnessDebounceTimer.Tick += (_, __) =>
                {
                    _brightnessDebounceTimer.Stop();
                    BrightenedTileCache.Clear();
                    RefreshTileCanvasesOnly();
                };
            }
            _brightnessDebounceTimer.Stop();
            _brightnessDebounceTimer.Start();
        }

        private static StackPanel BuildSection2GridAndColorsUI(Window win)
        {
            var sec2 = CreateSectionCard("2. SIATKA MGRS / WGS84 (NA LINII), ROZJAŚNIANIE LASU & KOLORY RGB");
            sec2.Children.Add(TacticalConfigPanel.FieldBox("System współrzędnych i typ siatki na mapie:", TacticalConfigPanel.ComboCtrl(new[] { "MGRS / UTM (Siatka wojskowa w metrach)", "Siatka Sektorowa (A1, B2, C3...)", "WGS84 (Stopnie geograficzne Lat/Lon)", "Brak siatki" }, _coordSystem, win, v => { _coordSystem = v; RedrawVectorsOnly(); })));
            var subGridRow = new UniformGrid { Columns = 2 };
            subGridRow.Children.Add(TacticalConfigPanel.FieldBox("Podział wewnątrz oczek:", TacticalConfigPanel.ComboCtrl(new[] { "Co 33 m (Keypad 1-9)", "Co 50 m (Ćwiartki A,B,C,D)", "Brak podziału (Tylko 100 m)", "Siatka co 250 m", "Siatka co 500 m" }, _subGridDivision, win, v => { _subGridDivision = v; RedrawVectorsOnly(); })));
            subGridRow.Children.Add(TacticalConfigPanel.FieldBox("Opisy kratek:", TacticalConfigPanel.ComboCtrl(new[] { "MGRS + Ćwiartki/Keypad", "Tylko główne linie MGRS", "Brak opisów wewnątrz" }, _gridLabelsMode, win, v => { _gridLabelsMode = v; RedrawVectorsOnly(); })));
            sec2.Children.Add(subGridRow);

            UIElement MakeRgbBtn(string fTitle, string dTitle, Func<string> getter, Action<string> setter)
            {
                string hex = getter();
                var sw = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = TacticalConfigPanel.BrushFromHex(hex, Colors.White), BorderBrush = Brushes.White, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
                var lbl = new TextBlock { Text = hex.ToUpperInvariant(), Foreground = Brushes.White, FontSize = 9.5, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
                var st = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center }; st.Children.Add(sw); st.Children.Add(lbl);
                var btn = new Button { Content = st, Height = 25, Background = TacticalConfigPanel.BrushInputBg, BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
                btn.Click += (s, e) => { string p = TacticalConfigPanel.ShowRgbColorPickerDialog(win, dTitle, getter()); if (!string.IsNullOrEmpty(p)) { setter(p); sw.Background = TacticalConfigPanel.BrushFromHex(p, Colors.White); lbl.Text = p.ToUpperInvariant(); GlyphCache.Clear(); TacticalConfigPanel.MarkDirty(); _fastOverlayRenderer?.InvalidateVisual(); } };
                return TacticalConfigPanel.FieldBox(fTitle, btn);
            }

            var rgbGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 2, 0, 4) };
            rgbGrid.Children.Add(MakeRgbBtn("Kolor cyfr Keypad/Ćwiartek (RGB):", "Kolor cyfr Keypad (1-9) / Ćwiartek (A-D)", () => _subLabelTextColorHex, v => _subLabelTextColorHex = v));
            rgbGrid.Children.Add(MakeRgbBtn("Kolor tła Keypad/Ćwiartek (RGB):", "Kolor tła pod cyframi Keypad / Ćwiartek", () => _subLabelBgColorHex, v => _subLabelBgColorHex = v));
            rgbGrid.Children.Add(MakeRgbBtn("Kolor linii wewnętrznych (RGB):", "Kolor linii podziałki Keypad / Ćwiartek", () => _subGridLineColorHex, v => _subGridLineColorHex = v));
            rgbGrid.Children.Add(MakeRgbBtn("Kolor siatki głównej MGRS (RGB):", "Kolor głównych linii i opisów MGRS / WGS84", () => _mainGridColorHex, v => _mainGridColorHex = v));
            sec2.Children.Add(rgbGrid);

            sec2.Children.Add(MakeSliderRow("Grubość linii siatki (głównej i pomocniczej):", 0.5, 3.0, _gridThicknessMultiplier, v => $"{v:0.1}x", v => { _gridThicknessMultiplier = Math.Round(v, 1); _fastOverlayRenderer?.InvalidateVisual(); }));
            sec2.Children.Add(MakeSliderRow("Rozmiar / grubość czcionki współrzędnych (MGRS i Keypad):", 0.7, 2.0, _fontScaleMultiplier, v => $"{v:0.1}x", v => { _fontScaleMultiplier = Math.Round(v, 1); GlyphCache.Clear(); _fastOverlayRenderer?.InvalidateVisual(); }));
            sec2.Children.Add(MakeSliderRow("Widoczność (alfa) głównych linii i opisów (MGRS / WGS84):", 0.05, 1.0, _mainGridOpacity, v => $"{(int)Math.Round(v * 100)}%", v => { _mainGridOpacity = Math.Round(v, 2); _fastOverlayRenderer?.InvalidateVisual(); }));
            sec2.Children.Add(MakeSliderRow("Widoczność (alfa) podziałki wewnętrznej (Keypad 1-9 / Ćwiartki):", 0.0, 1.0, _subGridOpacity, v => $"{(int)Math.Round(v * 100)}%", v => { _subGridOpacity = Math.Round(v, 2); _fastOverlayRenderer?.InvalidateVisual(); }));
            sec2.Children.Add(MakeSliderRow("Krycie tła (kasetonu) pod cyframi Keypad / Ćwiartek:", 0.0, 1.0, _subLabelBgOpacity, v => $"{(int)Math.Round(v * 100)}%", v => { _subLabelBgOpacity = Math.Round(v, 2); _fastOverlayRenderer?.InvalidateVisual(); }));

            // NOWY SUWAK ROZJAŚNIANIA PODKŁADU MAPY (GAMMA SHADOW-LIFT DLA CIEMNEGO LASU I WYDRUKÓW)
            sec2.Children.Add(MakeSliderRow("☀️ Rozjaśnienie podkładu mapy (Ciemny las / Pod wydruk):", 0.0, 0.85, _mapBrightnessBoost, v => $"+{(int)Math.Round(v * 100)}%", v =>
            {
                _mapBrightnessBoost = Math.Round(v, 2);
                TriggerLiveBrightnessRefresh();
            }));

            sec2.Children.Add(MakeSliderRow("Przyciemnienie podkładu mapy (większy kontrast pod siatkę):", 0.0, 0.65, _mapDimmerOpacity, v => $"{(int)Math.Round(v * 100)}%", v =>
            {
                _mapDimmerOpacity = Math.Round(v, 2);
                if (_dimmerRect != null) _dimmerRect.Opacity = _mapDimmerOpacity;
            }));

            var haloRow = new UniformGrid { Columns = 2 };
            haloRow.Children.Add(TacticalConfigPanel.FieldBox("Obrys linii (Halo):", TacticalConfigPanel.ComboCtrl(new[] { "Pełny czarny obrys", "Półprzezroczysty obrys", "Brak obrysu" }, _gridHaloMode, win, v => { _gridHaloMode = v; _fastOverlayRenderer?.InvalidateVisual(); })));
            haloRow.Children.Add(TacticalConfigPanel.FieldBox("Styl opisów i cyfr:", TacticalConfigPanel.ComboCtrl(new[] { "Kaseton (Kolor tła RGB)", "Tylko obrys Halo", "Czysty tekst" }, _labelBgStyle, win, v => { _labelBgStyle = v; _fastOverlayRenderer?.InvalidateVisual(); })));
            sec2.Children.Add(haloRow);
            return sec2;
        }

        private static (double W, double H) GetExactCanvasSize()
        {
            if (_interactiveCanvas != null && _interactiveCanvas.ActualWidth > 60 && _interactiveCanvas.ActualHeight > 60) return (_interactiveCanvas.ActualWidth, _interactiveCanvas.ActualHeight);
            if (_sheetFrameBorder != null && _sheetFrameBorder.Width > 60 && _sheetFrameBorder.Height > 60) return (Math.Max(60, _sheetFrameBorder.Width - 3.0), Math.Max(60, _sheetFrameBorder.Height - 3.0));
            return (820.0, 580.0);
        }

        private static (double WidthMm, double HeightMm) GetActiveSheetDimensionsMm()
        {
            var s = _selectedSheet ?? SheetFormats[0];
            double w = Math.Max(s.WidthMm, s.HeightMm), h = Math.Min(s.WidthMm, s.HeightMm);
            return _sheetOrientation == "Pionowo" ? (h, w) : (w, h);
        }

        private static double GetExactMetersPerPixel()
        {
            var (wMm, _) = GetActiveSheetDimensionsMm();
            var (canvasW, _) = GetExactCanvasSize();
            return Math.Max(0.04, ((wMm / 1000.0) * _targetScaleDenominator) / Math.Max(100.0, canvasW));
        }

        private static double GetScaledTileSizePx() =>
            256.0 * (((156543.03392 * Math.Cos(_centerLat * Math.PI / 180.0)) / Math.Pow(2.0, _zoom)) / GetExactMetersPerPixel());

        private static (double X, double Y) LatLonToTileXY(double lat, double lon, int zoom)
        {
            double latRad = Math.Clamp(lat, -85.0511, 85.0511) * Math.PI / 180.0, n = Math.Pow(2.0, zoom);
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

        private static (double X, double Y) LatLonToEpsg3857(double lat, double lon) => GeoportalLidarEngine.LatLonToEpsg3857(lat, lon);
        private static (double Easting, double Northing) LatLonToEpsg2180(double lat, double lon) => GeoportalLidarEngine.LatLonToEpsg2180(lat, lon);

        public static (string FullMgrs, string ShortGrid100m, string KeypadAndQuarter, string UtmText, string WgsText) FormatTacticalPointCoordinates(double lat, double lon)
        {
            int zone = (int)Math.Floor((lon + 180.0) / 6.0) + 1;
            UtmGrid2DProjector.LatLonToUtm(lat, lon, zone, out double easting, out double northing);

            string bands = "CDEFGHJKLMNPQRSTUVWXX";
            char latBand = bands[Math.Clamp((int)Math.Floor((lat + 80.0) / 8.0), 0, bands.Length - 1)];
            int set = ((zone - 1) % 6) + 1;
            string colLetters = (set % 3) switch { 1 => "ABCDEFGH", 2 => "JKLMNPQR", _ => "STUVWXYZ" };
            string rowLetters = (set % 2 == 1) ? "ABCDEFGHJKLMNPQRSTUV" : "FGHJKLMNPQRSTUVABCDE";

            int colIdx = Math.Clamp(((int)Math.Floor(easting / 100000.0)) - 1, 0, 7);
            int rowIdx = (((int)Math.Floor(northing / 100000.0)) % 20 + 20) % 20;
            string sq100k = $"{colLetters[colIdx]}{rowLetters[rowIdx]}";

            int e5 = (((int)Math.Floor(easting)) % 100000 + 100000) % 100000;
            int n5 = (((int)Math.Floor(northing)) % 100000 + 100000) % 100000;

            int stepM = Math.Max(100, _effectiveMgrsStepMeters);
            double remE = (easting % stepM + stepM) % stepM, remN = (northing % stepM + stepM) % stepM;
            int kpCol = Math.Clamp((int)Math.Floor(remE / (stepM / 3.0)), 0, 2);
            int kpRowFromBottom = Math.Clamp((int)Math.Floor(remN / (stepM / 3.0)), 0, 2);
            int[,] kpMap = { { 1, 2, 3 }, { 4, 5, 6 }, { 7, 8, 9 } };
            int keypadNum = kpMap[kpRowFromBottom, kpCol];

            string[,] qMap = { { "A", "B" }, { "C", "D" } };
            string quarterChar = qMap[remN >= (stepM / 2.0) ? 0 : 1, remE >= (stepM / 2.0) ? 1 : 0];
            int e3 = (e5 / 100) % 1000, n3 = (n5 / 100) % 1000;

            return (
                $"{zone}{latBand} {sq100k} {e5:D5} {n5:D5}",
                $"{e3:D3} / {n3:D3}",
                $"Keypad: {keypadNum}  |  Ćwiartka: {quarterChar}  (Kratka {e3:D3}/{n3:D3})",
                $"{zone}{latBand}  {easting:0}m E   {northing:0}m N",
                string.Create(CultureInfo.InvariantCulture, $"{lat:0.000000}° N,  {lon:0.000000}° E")
            );
        }

        private static string GetEpsg3857BBox(int tx, int ty, int zoom)
        {
            var (latNorth, lonWest) = TileXYToLatLon(tx, ty, zoom);
            var (latSouth, lonEast) = TileXYToLatLon(tx + 1, ty + 1, zoom);
            var (minX, minY) = LatLonToEpsg3857(latSouth, lonWest);
            var (maxX, maxY) = LatLonToEpsg3857(latNorth, lonEast);
            return string.Create(CultureInfo.InvariantCulture, $"{minX:0.00},{minY:0.00},{maxX:0.00},{maxY:0.00}");
        }

        private static string GetEpsg2180TileBBoxNE(int tx, int ty, int zoom)
        {
            var (latNorth, lonWest) = TileXYToLatLon(tx, ty, zoom);
            var (latSouth, lonEast) = TileXYToLatLon(tx + 1, ty + 1, zoom);
            var (e1, n1) = LatLonToEpsg2180(latSouth, lonWest);
            var (e2, n2) = LatLonToEpsg2180(latNorth, lonEast);
            return string.Create(CultureInfo.InvariantCulture, $"{Math.Min(n1, n2):0.00},{Math.Min(e1, e2):0.00},{Math.Max(n1, n2):0.00},{Math.Max(e1, e2):0.00}");
        }

        private static string GetEpsg2180TileBBox(int tx, int ty, int zoom)
        {
            var (latNorth, lonWest) = TileXYToLatLon(tx, ty, zoom);
            var (latSouth, lonEast) = TileXYToLatLon(tx + 1, ty + 1, zoom);
            var (e1, n1) = LatLonToEpsg2180(latSouth, lonWest);
            var (e2, n2) = LatLonToEpsg2180(latNorth, lonEast);
            return string.Create(CultureInfo.InvariantCulture, $"{Math.Min(e1, e2):0.00},{Math.Min(n1, n2):0.00},{Math.Max(e1, e2):0.00},{Math.Max(n1, n2):0.00}");
        }

        private static string GetEpsg4326TileBBox(int tx, int ty, int zoom)
        {
            var (latNorth, lonWest) = TileXYToLatLon(tx, ty, zoom);
            var (latSouth, lonEast) = TileXYToLatLon(tx + 1, ty + 1, zoom);
            return string.Create(CultureInfo.InvariantCulture, $"{lonWest:0.000000},{latSouth:0.000000},{lonEast:0.000000},{latNorth:0.000000}");
        }

        private static string GetOptimalWmsTilePixelSize(string layerId, int zoom)
        {
            double bboxSpanMeters = 26000000.0 / Math.Pow(2.0, zoom);
            if (layerId == "geoportal_topo") return Math.Clamp((int)Math.Round(bboxSpanMeters / (13500.0 * 0.00028)), 220, 640).ToString();
            if (layerId == "geoportal_lidar") return Math.Clamp((int)Math.Round(bboxSpanMeters / (4500.0 * 0.00028)), 384, 768).ToString();
            return (zoom <= 13 ? 512 : (zoom == 14 ? 448 : 384)).ToString();
        }

        private static string FillAllBBoxTokens(string template, string bbox3857, string bbox2180NE, string bbox2180EN, string bbox4326, string wStr, string hStr) =>
            string.IsNullOrEmpty(template) ? null : template.Replace("{bbox2180_ne}", bbox2180NE).Replace("{bbox2180_en}", bbox2180EN).Replace("{bbox2180}", bbox2180EN).Replace("{bbox4326}", bbox4326).Replace("{bbox}", bbox3857).Replace("{w}", wStr).Replace("{h}", hStr);

        private static string BuildTileUrl(TacticalMapLayerDef layer, int tx, int ty, int zoom)
        {
            if (layer.IsWmsEpsg3857)
            {
                string sz = GetOptimalWmsTilePixelSize(layer.Id, zoom);
                string tpl = (layer.Id == "geoportal_hires" && zoom <= 15) ? layer.FallbackUrlTemplate : layer.UrlTemplate;
                return FillAllBBoxTokens(tpl, GetEpsg3857BBox(tx, ty, zoom), GetEpsg2180TileBBoxNE(tx, ty, zoom), GetEpsg2180TileBBox(tx, ty, zoom), GetEpsg4326TileBBox(tx, ty, zoom), sz, sz);
            }
            return layer.UrlTemplate.Replace("{z}", zoom.ToString()).Replace("{x}", tx.ToString()).Replace("{y}", ty.ToString());
        }

        private static string BuildFallbackTileUrl(TacticalMapLayerDef layer, int tx, int ty, int zoom)
        {
            if (layer == null || !layer.IsWmsEpsg3857 || string.IsNullOrEmpty(layer.FallbackUrlTemplate)) return null;
            string sz = GetOptimalWmsTilePixelSize(layer.Id, zoom);
            string fbTpl = (layer.Id == "geoportal_hires" && zoom <= 15) ? layer.SecondFallbackUrlTemplate : layer.FallbackUrlTemplate;
            return FillAllBBoxTokens(fbTpl, GetEpsg3857BBox(tx, ty, zoom), GetEpsg2180TileBBoxNE(tx, ty, zoom), GetEpsg2180TileBBox(tx, ty, zoom), GetEpsg4326TileBBox(tx, ty, zoom), sz, sz);
        }

        private static (string PrimaryUrl, string Fallback1Url, string Fallback2Url, double NorthLat, double SouthLat, double EastLon, double WestLon) BuildGeoportalSheetUrls(
            TacticalMapLayerDef layer, double viewW, double viewH, double centerLat, double centerLon, int scaleDenom)
        {
            double overW = viewW * WmsOverscanFactor, overH = viewH * WmsOverscanFactor, tilePx = GetScaledTileSizePx();
            var (cx, cy) = LatLonToTileXY(centerLat, centerLon, _zoom);
            var (nwLat, nwLon) = TileXYToLatLon(cx - (overW / 2.0) / tilePx, cy - (overH / 2.0) / tilePx, _zoom);
            var (seLat, seLon) = TileXYToLatLon(cx + (overW / 2.0) / tilePx, cy + (overH / 2.0) / tilePx, _zoom);

            double northLat = Math.Max(nwLat, seLat), southLat = Math.Min(nwLat, seLat), eastLon = Math.Max(nwLon, seLon), westLon = Math.Min(nwLon, seLon);
            var (minX3857, minY3857) = LatLonToEpsg3857(southLat, westLon);
            var (maxX3857, maxY3857) = LatLonToEpsg3857(northLat, eastLon);
            var (e1, n1) = LatLonToEpsg2180(southLat, westLon);
            var (e2, n2) = LatLonToEpsg2180(northLat, eastLon);
            double minE = Math.Min(e1, e2), maxE = Math.Max(e1, e2), minN = Math.Min(n1, n2), maxN = Math.Max(n1, n2);

            double widthMeters2180 = Math.Max(40.0, maxE - minE), aspect = Math.Max(0.25, overW / Math.Max(1.0, overH));
            double desiredW = Math.Clamp(overW * 0.90, 480.0, 980.0);

            if (layer.Id == "geoportal_topo")
            {
                double minW = widthMeters2180 / (19000.0 * 0.00028), maxW = widthMeters2180 / (6800.0 * 0.00028);
                desiredW = Math.Clamp(desiredW, minW, Math.Max(minW, maxW));
            }
            else if (layer.Id == "geoportal_lidar")
            {
                desiredW = Math.Clamp(widthMeters2180 / (3200.0 * 0.00028), 512.0, 1480.0);
            }

            int reqW = Math.Clamp((int)Math.Round(desiredW), 380, 1180), reqH = Math.Clamp((int)Math.Round(reqW / aspect), 300, 1180);
            if (reqH == 1180 && aspect < 1.0) reqW = Math.Clamp((int)Math.Round(reqH * aspect), 380, 1180);

            string b3857 = string.Create(CultureInfo.InvariantCulture, $"{minX3857:0.00},{minY3857:0.00},{maxX3857:0.00},{maxY3857:0.00}");
            string b2180NE = string.Create(CultureInfo.InvariantCulture, $"{minN:0.00},{minE:0.00},{maxN:0.00},{maxE:0.00}");
            string b2180EN = string.Create(CultureInfo.InvariantCulture, $"{minE:0.00},{minN:0.00},{maxE:0.00},{maxN:0.00}");
            string b4326 = string.Create(CultureInfo.InvariantCulture, $"{westLon:0.000000},{southLat:0.000000},{eastLon:0.000000},{northLat:0.000000}");
            string wStr = reqW.ToString(), hStr = reqH.ToString();

            if ((layer.Id == "geoportal_std" || layer.Id == "geoportal_hires") && scaleDenom > 26000)
            {
                string overviewUrl = FillAllBBoxTokens(layer.SecondFallbackUrlTemplate, b3857, b2180NE, b2180EN, b4326, wStr, hStr);
                string gugikUrl = FillAllBBoxTokens(layer.FallbackUrlTemplate, b3857, b2180NE, b2180EN, b4326, wStr, hStr);
                return (overviewUrl, gugikUrl, null, northLat, southLat, eastLon, westLon);
            }

            bool skipHi = layer.Id == "geoportal_hires" && scaleDenom > 4500;
            string t1 = skipHi ? layer.FallbackUrlTemplate : layer.UrlTemplate;
            string t2 = skipHi ? layer.SecondFallbackUrlTemplate : layer.FallbackUrlTemplate;
            string t3 = skipHi ? Layers.First(l => l.Id == "geoportal_std").SecondFallbackUrlTemplate : layer.SecondFallbackUrlTemplate;

            return (
                FillAllBBoxTokens(t1, b3857, b2180NE, b2180EN, b4326, wStr, hStr),
                FillAllBBoxTokens(t2, b3857, b2180NE, b2180EN, b4326, wStr, hStr),
                FillAllBBoxTokens(t3, b3857, b2180NE, b2180EN, b4326, wStr, hStr),
                northLat, southLat, eastLon, westLon
            );
        }

        private static Point ApplyShift45DegreeSnap(Point anchorPt, Point rawTargetPt, out bool isSnapped, out int snappedAngleDeg)
        {
            isSnapped = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift); snappedAngleDeg = 0;
            if (!isSnapped) return rawTargetPt;
            double dx = rawTargetPt.X - anchorPt.X, dy = rawTargetPt.Y - anchorPt.Y, dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist < 2.0) return rawTargetPt;
            double snappedRad = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4.0)) * (Math.PI / 4.0);
            snappedAngleDeg = ((int)Math.Round(snappedRad * 180.0 / Math.PI) % 360 + 360) % 360;
            return new Point(anchorPt.X + dist * Math.Cos(snappedRad), anchorPt.Y + dist * Math.Sin(snappedRad));
        }

        private static BitmapSource DecodeBytesToFrozenBitmap(byte[] validBytes)
        {
            if (validBytes == null || validBytes.Length < 128) return null;
            try
            {
                var bmp = new BitmapImage();
                using (var mem = new MemoryStream(validBytes)) { bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.StreamSource = mem; bmp.EndInit(); bmp.Freeze(); }
                return bmp;
            }
            catch { return null; }
        }

        private static async Task<BitmapSource> FetchDecodedImageAsync(string url, string fallbackUrl, CancellationToken ct, string secondFallbackUrl = null, bool checkBlankWhiteForPrimary = true)
        {
            if (string.IsNullOrEmpty(url)) return null;
            if (DecodedImageCache.TryGetValue(url, out var cachedBmp)) return cachedBmp;

            byte[] diskBytes = GeoportalLidarEngine.TryReadDiskCache(url);
            if (diskBytes != null)
            {
                var diskBmp = DecodeBytesToFrozenBitmap(diskBytes);
                if (diskBmp != null) { DecodedImageCache[url] = diskBmp; RawTileCache[url] = diskBytes; return diskBmp; }
            }

            bool isWms = url.Contains("REQUEST=GetMap", StringComparison.OrdinalIgnoreCase) || url.Contains("/MapServer/export", StringComparison.OrdinalIgnoreCase);
            var sem = isWms ? WmsDedicatedThrottle : TileThrottle;

            await sem.WaitAsync(ct);
            try
            {
                if (DecodedImageCache.TryGetValue(url, out var secCheck)) return secCheck;
                string[] candidates = new[] { url, fallbackUrl, secondFallbackUrl }.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct().ToArray();
                BitmapSource resolvedBmp = null; byte[] resolvedBytes = null;

                for (int i = 0; i < candidates.Length && !ct.IsCancellationRequested; i++)
                {
                    try
                    {
                        using var reqCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        reqCts.CancelAfter(isWms ? 4200 : 5500);

                        byte[] data = await Http.GetByteArrayAsync(candidates[i], reqCts.Token);
                        if (IsValidImagePayload(data))
                        {
                            var bmp = DecodeBytesToFrozenBitmap(data);
                            if (bmp != null)
                            {
                                if (isWms && GeoportalLidarEngine.IsEmptyOrFlatLidarBitmap(bmp))
                                    continue;
                                resolvedBmp = bmp;
                                resolvedBytes = data;
                                break;
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        if (ct.IsCancellationRequested) break;
                    }
                    catch { }
                }

                if (resolvedBmp != null && resolvedBytes != null)
                {
                    if (DecodedImageCache.Count > 700) DecodedImageCache.Clear();
                    if (RawTileCache.Count > 1400) RawTileCache.Clear();
                    DecodedImageCache[url] = resolvedBmp; RawTileCache[url] = resolvedBytes;
                    GeoportalLidarEngine.SaveDiskCacheAsync(url, resolvedBytes);
                }
                return resolvedBmp;
            }
            finally { sem.Release(); }
        }

        private static void RepositionGeoAnchoredSheetsOnCanvas(Canvas targetCanvas, List<GeoAnchoredWmsSheet> sheets, string requiredLayerId, double viewW, double viewH, double opacity)
        {
            if (targetCanvas == null) return;
            targetCanvas.Visibility = Visibility.Visible; targetCanvas.RenderTransform = Transform.Identity; targetCanvas.Opacity = opacity; targetCanvas.Children.Clear();
            var scalingMode = _isPanningMap ? BitmapScalingMode.LowQuality : BitmapScalingMode.HighQuality;
            RenderOptions.SetBitmapScalingMode(targetCanvas, scalingMode);

            bool applyBoost = (targetCanvas == _tileCanvas && _mapBrightnessBoost > 0.01);

            foreach (var sh in sheets)
            {
                if (sh.Bitmap == null || (!string.IsNullOrEmpty(requiredLayerId) && sh.LayerId != requiredLayerId)) continue;
                Point nwPt = LatLonToScreen(sh.NorthLat, sh.WestLon, viewW, viewH), sePt = LatLonToScreen(sh.SouthLat, sh.EastLon, viewW, viewH);
                double left = Math.Min(nwPt.X, sePt.X), top = Math.Min(nwPt.Y, sePt.Y), width = Math.Abs(sePt.X - nwPt.X), height = Math.Abs(sePt.Y - nwPt.Y);
                if (left + width < -viewW * 2 || left > viewW * 3 || top + height < -viewH * 2 || top > viewH * 3 || width < 12 || height < 12 || width > viewW * 25 || height > viewH * 25) continue;

                BitmapSource displayBmp = sh.Bitmap;
                if (applyBoost)
                {
                    if (sh.BrightenedBitmap == null || Math.Abs(sh.AppliedBoost - _mapBrightnessBoost) > 0.005)
                    {
                        sh.BrightenedBitmap = GeoportalLidarEngine.ApplyGammaAndBrightnessToBitmap(sh.Bitmap, _mapBrightnessBoost) ?? sh.Bitmap;
                        sh.AppliedBoost = _mapBrightnessBoost;
                    }
                    displayBmp = sh.BrightenedBitmap;
                }

                var img = new Image { Source = displayBmp, Width = width + 0.8, Height = height + 0.8, Stretch = Stretch.Fill, IsHitTestVisible = false };
                RenderOptions.SetBitmapScalingMode(img, scalingMode);
                Canvas.SetLeft(img, left); Canvas.SetTop(img, top); targetCanvas.Children.Add(img);
            }
        }

        private static bool RenderLayerTilesSmoothly(Canvas targetCanvas, TacticalMapLayerDef layer, double w, double h, double opacity, CancellationToken ct)
        {
            if (targetCanvas == null || layer == null) return false;
            targetCanvas.RenderTransform = Transform.Identity;
            RenderOptions.SetBitmapScalingMode(targetCanvas, _isPanningMap ? BitmapScalingMode.LowQuality : BitmapScalingMode.HighQuality);

            if (_lastRenderedTileZoom != _zoom)
            {
                _lastRenderedTileZoom = _zoom; _renderCts?.Cancel(); _renderCts = new CancellationTokenSource(); ct = _renderCts.Token; PendingTileUrls.Clear();
            }

            double tilePx = GetScaledTileSizePx();
            var (centerTileX, centerTileY) = LatLonToTileXY(_centerLat, _centerLon, _zoom);
            int minTx = (int)Math.Floor(centerTileX - (w / 2.0) / tilePx) - 1, maxTx = (int)Math.Floor(centerTileX + (w / 2.0) / tilePx) + 1;
            int minTy = (int)Math.Floor(centerTileY - (h / 2.0) / tilePx) - 1, maxTy = (int)Math.Floor(centerTileY + (h / 2.0) / tilePx) + 1;
            int maxIndex = (1 << _zoom) - 1;

            targetCanvas.Children.Clear(); targetCanvas.Opacity = opacity; bool anyMissing = false;
            for (int tx = minTx; tx <= maxTx; tx++)
            {
                for (int ty = minTy; ty <= maxTy; ty++)
                {
                    if (ty < 0 || ty > maxIndex) continue;
                    int wrappedTx = ((tx % (maxIndex + 1)) + (maxIndex + 1)) % (maxIndex + 1);
                    double sx = (w / 2.0) + (tx - centerTileX) * tilePx, sy = (h / 2.0) + (ty - centerTileY) * tilePx;
                    string url = BuildTileUrl(layer, wrappedTx, ty, _zoom);

                    if (DecodedImageCache.TryGetValue(url, out var readyBmp))
                    {
                        var dispBmp = GetGammaBrightenedTile(url, readyBmp);
                        var img = new Image { Source = dispBmp, Width = tilePx + 0.9, Height = tilePx + 0.9, Stretch = Stretch.Fill };
                        Canvas.SetLeft(img, sx); Canvas.SetTop(img, sy); targetCanvas.Children.Add(img);
                    }
                    else
                    {
                        anyMissing = true;
                        if (_zoom > 6)
                        {
                            string parentUrl = BuildTileUrl(layer, wrappedTx / 2, ty / 2, _zoom - 1);
                            if (DecodedImageCache.TryGetValue(parentUrl, out var parentBmp))
                            {
                                var dispParent = GetGammaBrightenedTile(parentUrl, parentBmp);
                                int cw = Math.Max(1, dispParent.PixelWidth / 2), ch = Math.Max(1, dispParent.PixelHeight / 2);
                                var crop = new CroppedBitmap(dispParent, new Int32Rect((wrappedTx % 2) * cw, (ty % 2) * ch, cw, ch)); crop.Freeze();
                                var fbImg = new Image { Source = crop, Width = tilePx + 0.9, Height = tilePx + 0.9, Stretch = Stretch.Fill };
                                Canvas.SetLeft(fbImg, sx); Canvas.SetTop(fbImg, sy); targetCanvas.Children.Add(fbImg);
                            }
                        }

                        if (!_isPanningMap && PendingTileUrls.TryAdd(url, 1))
                        {
                            string fbUrl = BuildFallbackTileUrl(layer, wrappedTx, ty, _zoom);
                            var localCt = ct;
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    var dl = await FetchDecodedImageAsync(url, fbUrl, localCt);
                                    if (dl != null && !localCt.IsCancellationRequested && Application.Current != null)
                                        _ = Application.Current.Dispatcher.BeginInvoke(new Action(() => { if (_fullScreenOverlay?.Visibility == Visibility.Visible && !_activeLayer.IsWmsEpsg3857) RefreshTileCanvasesOnly(); }), DispatcherPriority.Background);
                                }
                                finally { PendingTileUrls.TryRemove(url, out byte _); }
                            });
                        }
                    }
                }
            }
            return anyMissing;
        }

        private static void RefreshTileCanvasesOnly()
        {
            if (_tileCanvas == null || _activeLayer == null || _fullScreenOverlay?.Visibility != Visibility.Visible) return;
            EnsureTileWatchdogStarted();
            var (w, h) = GetExactCanvasSize();
            var ct = _renderCts?.Token ?? CancellationToken.None;

            if (_activeLayer.IsWmsEpsg3857) { _hasMissingVisibleTiles = false; RepositionGeoAnchoredSheetsOnCanvas(_tileCanvas, _geoAnchoredBaseSheets, _activeLayer.Id, w, h, 1.0); }
            else _hasMissingVisibleTiles = RenderLayerTilesSmoothly(_tileCanvas, _activeLayer, w, h, 1.0, ct);

            if (_lidarTileCanvas != null)
            {
                if (_showLidarSingleWms && _activeLayer.Id != "geoportal_lidar") RepositionGeoAnchoredSheetsOnCanvas(_lidarTileCanvas, _geoAnchoredLidarSheets, "geoportal_lidar", w, h, _lidarOpacity);
                else _lidarTileCanvas.Children.Clear();
            }
        }

        private static void StoreGeoAnchoredSheet(List<GeoAnchoredWmsSheet> list, GeoAnchoredWmsSheet newSheet)
        {
            if (newSheet?.Bitmap == null) return;
            list.RemoveAll(s => s.LayerId != newSheet.LayerId || (s.ScaleDenominator == newSheet.ScaleDenominator && Math.Abs(s.CenterLat - newSheet.CenterLat) < 0.0001 && Math.Abs(s.CenterLon - newSheet.CenterLon) < 0.0001));
            list.Add(newSheet);
            while (list.Count > 6) list.RemoveAt(0);
        }

        private static async Task RenderMapTilesAsync(bool transparentGrgOnly = false)
        {
            if (_tileCanvas == null || _interactiveCanvas == null || _activeLayer == null || _fullScreenOverlay?.Visibility != Visibility.Visible) return;
            if (_renderCts == null || _renderCts.IsCancellationRequested) _renderCts = new CancellationTokenSource();

            EnsureTileWatchdogStarted(); UpdateSheetViewportSize();
            var (w, h) = GetExactCanvasSize(); RedrawVectorsOnly();

            if (transparentGrgOnly)
            {
                _tileCanvas.Children.Clear(); _lidarTileCanvas?.Children.Clear();
                _dimmerRect.Visibility = Visibility.Collapsed; _mapCompositeGrid.Background = Brushes.Transparent;
                return;
            }

            _mapCompositeGrid.Background = TacticalConfigPanel.BrushCardBg;
            _dimmerRect.Visibility = Visibility.Visible; _dimmerRect.Opacity = _mapDimmerOpacity;
            RefreshTileCanvasesOnly();

            if (_activeLayer.IsWmsEpsg3857 || _showLidarSingleWms)
            {
                _wmsSheetCts?.Cancel();
                var wmsCts = new CancellationTokenSource(); _wmsSheetCts = wmsCts; var ct = wmsCts.Token;
                _isWmsSheetFetchRunning = true;

                double reqLat = _centerLat, reqLon = _centerLon; int reqScale = _targetScaleDenominator;
                var reqLayer = _activeLayer; bool reqLidarOverlay = _showLidarSingleWms && reqLayer.Id != "geoportal_lidar";

                try
                {
                    await Task.Delay(75, ct);
                    if (ct.IsCancellationRequested) return;

                    var bInfo = BuildGeoportalSheetUrls(reqLayer, w, h, reqLat, reqLon, reqScale);

                    _wmsLoadedCenterLat = reqLat;
                    _wmsLoadedCenterLon = reqLon;
                    _wmsLoadedScaleDenominator = reqScale;
                    _wmsLoadedLayerId = reqLayer.Id;

                    Task basePipeline = Task.CompletedTask;
                    if (reqLayer.IsWmsEpsg3857)
                    {
                        if (_statusText != null) _statusText.Text = $"⏳ Pobieranie: {reqLayer.Name.Split('(')[0].Trim()} (Skala 1:{reqScale:N0})...";
                        basePipeline = Task.Run(async () =>
                        {
                            try
                            {
                                BitmapSource baseBmp;
                                if (reqLayer.Id == "geoportal_lidar")
                                {
                                    baseBmp = await GeoportalLidarEngine.FetchCompositeLidarSheetAsync(
                                        GeoportalLidarEngine.LidarHttp, bInfo.NorthLat, bInfo.SouthLat, bInfo.EastLon, bInfo.WestLon, w * WmsOverscanFactor, h * WmsOverscanFactor, false, ct);
                                }
                                else if (reqLayer.Id == "geoportal_hires" && reqScale <= 4500)
                                {
                                    var hiBmp = await FetchDecodedImageAsync(bInfo.PrimaryUrl, null, ct, null, false);
                                    if (hiBmp == null || GeoportalLidarEngine.HasPartialWhiteNoDataZone(hiBmp, 0.05))
                                    {
                                        var stdBmp = await FetchDecodedImageAsync(bInfo.Fallback1Url, bInfo.Fallback2Url, ct, null, true);
                                        baseBmp = (hiBmp != null && stdBmp != null)
                                            ? GeoportalLidarEngine.MergeHighResWithStandardFallback(hiBmp, stdBmp)
                                            : (stdBmp ?? hiBmp);
                                    }
                                    else
                                    {
                                        baseBmp = hiBmp;
                                    }
                                }
                                else
                                {
                                    baseBmp = await FetchDecodedImageAsync(bInfo.PrimaryUrl, bInfo.Fallback1Url, ct, bInfo.Fallback2Url, true);
                                }

                                if (!ct.IsCancellationRequested && Application.Current != null)
                                {
                                    await Application.Current.Dispatcher.InvokeAsync(() =>
                                    {
                                        if (ct.IsCancellationRequested) return;
                                        var (curW, curH) = GetExactCanvasSize();
                                        if (baseBmp != null)
                                        {
                                            StoreGeoAnchoredSheet(_geoAnchoredBaseSheets, new GeoAnchoredWmsSheet
                                            {
                                                LayerId = reqLayer.Id, NorthLat = bInfo.NorthLat, SouthLat = bInfo.SouthLat, EastLon = bInfo.EastLon, WestLon = bInfo.WestLon,
                                                CenterLat = reqLat, CenterLon = reqLon, ScaleDenominator = reqScale, Bitmap = baseBmp
                                            });
                                            RepositionGeoAnchoredSheetsOnCanvas(_tileCanvas, _geoAnchoredBaseSheets, reqLayer.Id, curW, curH, 1.0);
                                            if (_statusText != null && !reqLidarOverlay)
                                            {
                                                string extra = reqLayer.Id == "geoportal_lidar" ? $" [{GeoportalLidarEngine.LastLidarDiagnosticStatus}]" : "";
                                                _statusText.Text = $"✓ Wczytano: {reqLayer.Name.Split('(')[0].Trim()}{extra} (Skala 1:{reqScale:N0}).";
                                            }
                                        }
                                        else if (_statusText != null && !reqLidarOverlay)
                                        {
                                            _statusText.Text = $"⚠️ Serwer WMS nie zwrócił obrazu dla skali 1:{reqScale:N0} — kliknij '🔄 Odśwież' lub przybliż widok.";
                                        }
                                    });
                                }
                            }
                            catch { }
                        }, ct);
                    }

                    Task lidarPipeline = Task.CompletedTask;
                    if (reqLidarOverlay)
                    {
                        lidarPipeline = Task.Run(async () =>
                        {
                            try
                            {
                                var lidarBmp = await GeoportalLidarEngine.FetchCompositeLidarSheetAsync(
                                    GeoportalLidarEngine.LidarHttp, bInfo.NorthLat, bInfo.SouthLat, bInfo.EastLon, bInfo.WestLon, w * WmsOverscanFactor, h * WmsOverscanFactor, true, ct);

                                if (!ct.IsCancellationRequested && Application.Current != null)
                                {
                                    await Application.Current.Dispatcher.InvokeAsync(() =>
                                    {
                                        if (ct.IsCancellationRequested) return;
                                        var (curW, curH) = GetExactCanvasSize();
                                        _wmsLoadedLidarFlag = true;
                                        if (lidarBmp != null)
                                        {
                                            StoreGeoAnchoredSheet(_geoAnchoredLidarSheets, new GeoAnchoredWmsSheet
                                            {
                                                LayerId = "geoportal_lidar", NorthLat = bInfo.NorthLat, SouthLat = bInfo.SouthLat, EastLon = bInfo.EastLon, WestLon = bInfo.WestLon,
                                                CenterLat = reqLat, CenterLon = reqLon, ScaleDenominator = reqScale, Bitmap = lidarBmp
                                            });
                                            RepositionGeoAnchoredSheetsOnCanvas(_lidarTileCanvas, _geoAnchoredLidarSheets, "geoportal_lidar", curW, curH, _lidarOpacity);
                                            if (_statusText != null)
                                                _statusText.Text = $"✓ Wczytano: {reqLayer.Name.Split('(')[0].Trim()} + LiDAR [{GeoportalLidarEngine.LastLidarDiagnosticStatus}] (1:{reqScale:N0}).";
                                        }
                                    });
                                }
                            }
                            catch { }
                        }, ct);
                    }
                    else { _wmsLoadedLidarFlag = false; _lidarTileCanvas?.Children.Clear(); }

                    await Task.WhenAll(basePipeline, lidarPipeline);
                }
                catch { }
                finally { if (_wmsSheetCts == wmsCts) _isWmsSheetFetchRunning = false; }
            }
            else if (_lidarTileCanvas != null) { _wmsLoadedLidarFlag = false; _lidarTileCanvas.Children.Clear(); }
        }

        private static FormattedText GetCachedGlyph(string text, double fontSize, Brush fg, string keyPrefix)
        {
            string key = $"{keyPrefix}_{text}_{fontSize:0.0}";
            if (GlyphCache.TryGetValue(key, out var ft)) return ft;
            if (GlyphCache.Count > 800) GlyphCache.Clear();
            var created = new FormattedText(text ?? "", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, BoldSegoeTypeface, fontSize, fg, 1.0);
            GlyphCache[key] = created; return created;
        }

        private static Brush CreateAlphaHexBrush(string hex, double opacity, Color fallback)
        {
            byte a = (byte)Math.Clamp((int)Math.Round(opacity * 255), 0, 255);
            string cacheKey = $"{hex}_{a}_{fallback.R}_{fallback.G}_{fallback.B}";
            if (FrozenBrushCache.TryGetValue(cacheKey, out var cached)) return cached;
            Color c = fallback; try { if (!string.IsNullOrWhiteSpace(hex)) c = (Color)ColorConverter.ConvertFromString(hex.Trim()); } catch { }
            var b = new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B)); b.Freeze();
            if (FrozenBrushCache.Count > 500) FrozenBrushCache.Clear();
            FrozenBrushCache[cacheKey] = b; return b;
        }

        private static Brush GetCachedRgbaBrush(byte a, byte r, byte g, byte b)
        {
            string key = $"RGBA_{a}_{r}_{g}_{b}";
            if (FrozenBrushCache.TryGetValue(key, out var cached)) return cached;
            var br = new SolidColorBrush(Color.FromArgb(a, r, g, b)); br.Freeze();
            FrozenBrushCache[key] = br; return br;
        }

        private static Pen GetCachedSolidPen(string key, Brush brush, double thickness)
        {
            string fullKey = $"{key}_{thickness:0.00}";
            if (FrozenPenCache.TryGetValue(fullKey, out var cached)) return cached;
            var p = new Pen(brush, thickness); p.Freeze();
            if (FrozenPenCache.Count > 400) FrozenPenCache.Clear();
            FrozenPenCache[fullKey] = p; return p;
        }

        private static void DrawFastBadgeOnContext(DrawingContext dc, string text, double x, double y, Brush fg, double fontSize, double opacity, string cachePrefix)
        {
            if (string.IsNullOrEmpty(text) || opacity <= 0.02) return;
            var ft = GetCachedGlyph(text, fontSize, fg, cachePrefix);
            if (_labelBgStyle != "Czysty tekst")
            {
                byte bgAlpha = (byte)Math.Clamp((int)Math.Round((_labelBgStyle.StartsWith("Kaseton") ? 205 : 110) * opacity), 0, 240);
                var bgBrush = GetCachedRgbaBrush(bgAlpha, 5, 12, 15);
                Pen borderPen = _labelBgStyle.StartsWith("Kaseton") ? GetCachedSolidPen($"BDG_{cachePrefix}", fg, 0.85) : null;
                dc.DrawRoundedRectangle(bgBrush, borderPen, new Rect(x - 3.5, y - 1, ft.Width + 7, ft.Height + 2), 2.5, 2.5);
            }
            dc.DrawText(ft, new Point(x, y));
        }

        private static void DrawCartographicLineLabelOnContext(DrawingContext dc, string text, double lineCoord, bool isVerticalLine, double canvasW, double canvasH, Brush fg, double fontSize, double opacity, string cachePrefix)
        {
            if (string.IsNullOrEmpty(text) || opacity <= 0.02) return;
            var ft = GetCachedGlyph(text, fontSize, fg, cachePrefix);
            double drawX = isVerticalLine ? (lineCoord - ft.Width / 2.0) : 6.0;
            double drawY = isVerticalLine ? 6.0 : (lineCoord - ft.Height / 2.0);
            if (isVerticalLine && (drawX < 2.0 || drawX + ft.Width > canvasW - 2.0)) return;
            if (!isVerticalLine && (drawY < 2.0 || drawY + ft.Height > canvasH - 2.0)) return;

            byte bgAlpha = (byte)Math.Clamp((int)Math.Round((_labelBgStyle.StartsWith("Kaseton") ? 225 : 165) * opacity), 0, 245);
            var bgBrush = GetCachedRgbaBrush(bgAlpha, 6, 14, 18);
            Pen borderPen = _labelBgStyle.StartsWith("Kaseton") ? GetCachedSolidPen($"CARTO_{cachePrefix}", fg, 0.9) : null;
            dc.DrawRoundedRectangle(bgBrush, borderPen, new Rect(drawX - 4.0, drawY - 1.0, ft.Width + 8.0, ft.Height + 2.0), 3.0, 3.0);
            dc.DrawText(ft, new Point(drawX, drawY));
        }

        private static void DrawFastSubCellBadgeOnContext(DrawingContext dc, string text, double centerX, double centerY, Brush textBrush, Brush bgBrush, Pen boxBorderPen, Brush shadowBrush, double fontSize, string cachePrefix)
        {
            if (string.IsNullOrEmpty(text)) return;
            var ft = GetCachedGlyph(text, fontSize, textBrush, cachePrefix);
            double tx = centerX - ft.Width / 2.0, ty = centerY - ft.Height / 2.0;
            if (_labelBgStyle != "Czysty tekst" && _subLabelBgOpacity > 0.02)
                dc.DrawRoundedRectangle(bgBrush, boxBorderPen, new Rect(tx - 4.0, ty - 1.5, ft.Width + 8.0, ft.Height + 3.0), 3.0, 3.0);
            var shadowFt = GetCachedGlyph(text, fontSize, shadowBrush, cachePrefix + "_SH");
            dc.DrawText(shadowFt, new Point(tx + 0.9, ty + 0.9));
            dc.DrawText(ft, new Point(tx, ty));
        }

        private static Brush CreateOutOfBoundsPatternBrush(Color c, double opacity, string patternMode)
        {
            byte bgAlpha = (byte)Math.Clamp((int)Math.Round(opacity * 200), 0, 235);
            byte lineAlpha = (byte)Math.Clamp((int)Math.Round(Math.Min(1.0, opacity + 0.25) * 255), 0, 255);
            string patKey = $"PAT_{c.R}_{c.G}_{c.B}_{bgAlpha}_{lineAlpha}_{patternMode}";
            if (FrozenBrushCache.TryGetValue(patKey, out var cached)) return cached;

            var bgBrush = GetCachedRgbaBrush(bgAlpha, c.R, c.G, c.B);
            if (patternMode.StartsWith("Jednolite")) return bgBrush;
            if (patternMode.StartsWith("Tylko linia")) return Brushes.Transparent;

            var linePen = new Pen(GetCachedRgbaBrush(lineAlpha, c.R, c.G, c.B), 1.6); linePen.Freeze();
            var dg = new DrawingGroup();
            dg.Children.Add(new GeometryDrawing(bgBrush, null, new RectangleGeometry(new Rect(0, 0, 20, 20))));
            dg.Children.Add(new GeometryDrawing(null, linePen, new LineGeometry(new Point(0, 20), new Point(20, 0))));
            if (patternMode.Contains("Symbole X")) dg.Children.Add(new GeometryDrawing(null, linePen, new LineGeometry(new Point(0, 0), new Point(20, 20))));
            var db = new DrawingBrush(dg) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 20, 20), ViewportUnits = BrushMappingMode.Absolute };
            db.Freeze(); FrozenBrushCache[patKey] = db; return db;
        }

        private static void RenderFastGridAndMasksOnContext(DrawingContext dc, double w, double h)
        {
            foreach (var ao in _mapObjects.Where(o => o.Kind == "AO" && o.Points.Count >= 2))
            {
                Color c = Color.FromRgb(235, 64, 52); try { c = (Color)ColorConverter.ConvertFromString(ao.ColorHex); } catch { }
                var strokeBrush = TacticalConfigPanel.CreateFrozenBrush(c.R, c.G, c.B);
                var screenPts = ao.Points.Select(pt => LatLonToScreen(pt.Lat, pt.Lon, w, h)).ToList();
                var fig = new PathFigure { StartPoint = screenPts[0], IsClosed = screenPts.Count >= 3, IsFilled = true };
                for (int i = 1; i < screenPts.Count; i++) fig.Segments.Add(new LineSegment(screenPts[i], true));
                var aoPolyGeom = new PathGeometry(new[] { fig });
                if (screenPts.Count >= 3)
                    dc.DrawGeometry(CreateOutOfBoundsPatternBrush(c, _aoOutMaskOpacity, _aoOutPattern), null, new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, w, h)), aoPolyGeom));
                double mult = ao.SizeMultiplier > 0.1 ? ao.SizeMultiplier : 1.0;
                var borderPen = new Pen(strokeBrush, 2.8 * mult) { DashStyle = new DashStyle(new double[] { 5, 2.5 }, 0) }; borderPen.Freeze();
                dc.DrawGeometry(null, borderPen, aoPolyGeom);
                if (_showAllObjectLabels && ao.ShowLabel && !string.IsNullOrWhiteSpace(ao.Name))
                    DrawFastBadgeOnContext(dc, $"🛡️ {ao.Name}", screenPts[0].X + 10, screenPts[0].Y + 6, strokeBrush, 10 * _fontScaleMultiplier * mult, 1.0, $"AO_{ao.ColorHex}");
            }

            foreach (var sec in _mapObjects.Where(o => o.Kind == "Sector" && o.Points.Count >= 2))
            {
                Color c = Color.FromRgb(245, 184, 46); try { c = (Color)ColorConverter.ConvertFromString(sec.ColorHex); } catch { }
                var strokeBrush = TacticalConfigPanel.CreateFrozenBrush(c.R, c.G, c.B);
                var fillBrush = GetCachedRgbaBrush(85, c.R, c.G, c.B);
                var screenPts = sec.Points.Select(pt => LatLonToScreen(pt.Lat, pt.Lon, w, h)).ToList();
                double mult = sec.SizeMultiplier > 0.1 ? sec.SizeMultiplier : 1.0;
                var fig = new PathFigure { StartPoint = screenPts[0], IsClosed = true, IsFilled = true };
                for (int i = 1; i < screenPts.Count; i++) fig.Segments.Add(new LineSegment(screenPts[i], true));
                dc.DrawGeometry(fillBrush, GetCachedSolidPen($"SEC_{sec.ColorHex}", strokeBrush, 2.2 * mult), new PathGeometry(new[] { fig }));
                if (_showAllObjectLabels && sec.ShowLabel && !string.IsNullOrWhiteSpace(sec.Name))
                    DrawFastBadgeOnContext(dc, sec.Name, screenPts.Average(p => p.X) + 14, screenPts.Average(p => p.Y) - 8, strokeBrush, 10 * _fontScaleMultiplier * mult, 1.0, $"SEC_{sec.ColorHex}");
            }

            foreach (var rt in _mapObjects.Where(o => o.Kind == "Route" && o.Points.Count >= 2))
            {
                double mult = rt.SizeMultiplier > 0.1 ? rt.SizeMultiplier : 1.0;
                var strokeBrush = TacticalConfigPanel.BrushFromHex(rt.ColorHex, Color.FromRgb(38, 132, 255));
                var screenPts = rt.Points.Select(pt => LatLonToScreen(pt.Lat, pt.Lon, w, h)).ToList();
                var rtPen = new Pen(strokeBrush, 2.8 * mult) { DashStyle = new DashStyle(new double[] { 4, 2 }, 0) }; rtPen.Freeze();
                for (int i = 0; i < screenPts.Count - 1; i++) dc.DrawLine(rtPen, screenPts[i], screenPts[i + 1]);
                if (_showAllObjectLabels && rt.ShowLabel && !string.IsNullOrWhiteSpace(rt.Name))
                    DrawFastBadgeOnContext(dc, rt.Name, screenPts[0].X + 8, screenPts[0].Y - 14, strokeBrush, 9.5 * _fontScaleMultiplier * mult, 1.0, $"RT_{rt.ColorHex}");
            }

            if (_isHoveringCanvas && _activeDraftPoly != null && _activeDraftPoly.Points.Count > 0 && (_activeTool == "ROUTE" || _activeTool == "AO" || _activeTool == "SECTOR"))
            {
                var lastPt = _activeDraftPoly.Points[_activeDraftPoly.Points.Count - 1];
                Point pStart = LatLonToScreen(lastPt.Lat, lastPt.Lon, w, h);
                Point pEnd = ApplyShift45DegreeSnap(pStart, _currentHoverScreenPt, out bool snapped, out int deg);
                var previewBrush = snapped ? TacticalConfigPanel.BrushAccent : TacticalConfigPanel.BrushFromHex(_activeDraftPoly.ColorHex, Color.FromRgb(31, 224, 197));
                var previewPen = new Pen(previewBrush, 2.2) { DashStyle = new DashStyle(new double[] { 3, 2 }, 0) }; previewPen.Freeze();
                dc.DrawLine(previewPen, pStart, pEnd); dc.DrawEllipse(previewBrush, null, pEnd, 4, 4);
                if (snapped) DrawFastBadgeOnContext(dc, $"📐 SNAP {deg}° (SHIFT)", pEnd.X + 12, pEnd.Y - 18, TacticalConfigPanel.BrushAccent, 9.5, 1.0, "SNAP_DEG");
            }

            if (_coordSystem != "Brak siatki")
            {
                var mainBrush = CreateAlphaHexBrush(_mainGridColorHex, _mainGridOpacity, Color.FromRgb(250, 208, 44));
                var subLineBrush = CreateAlphaHexBrush(_subGridLineColorHex, _subGridOpacity, Color.FromRgb(31, 224, 197));
                var subTextBrush = CreateAlphaHexBrush(_subLabelTextColorHex, _subGridOpacity, Colors.White);
                var subBgBrush = CreateAlphaHexBrush(_subLabelBgColorHex, _subLabelBgOpacity * _subGridOpacity, Color.FromRgb(6, 16, 20));
                var subShadowBrush = CreateAlphaHexBrush("#000000", _subGridOpacity * 0.9, Colors.Black);

                Pen subBoxBorderPen = (_labelBgStyle.StartsWith("Kaseton") && _subLabelBgOpacity > 0.05)
                    ? GetCachedSolidPen($"SUBBOX_{_subGridOpacity:0.00}", CreateAlphaHexBrush("#000000", _subGridOpacity * 0.85, Colors.Black), 0.9) : null;

                byte mainHaloAlpha = (byte)Math.Clamp((int)Math.Round((_gridHaloMode.StartsWith("Pełny") ? 220 : 130) * _mainGridOpacity), 0, 255);
                byte subHaloAlpha = (byte)Math.Clamp((int)Math.Round((_gridHaloMode.StartsWith("Pełny") ? 180 : 100) * _subGridOpacity), 0, 255);
                var haloBrush = GetCachedRgbaBrush(mainHaloAlpha, 0, 0, 0);
                var subHaloBrush = GetCachedRgbaBrush(subHaloAlpha, 0, 0, 0);
                bool useHalo = !_gridHaloMode.StartsWith("Brak");

                double mainThick = Math.Max(0.8, 1.45 * _gridThicknessMultiplier), subThick = Math.Max(0.7, 0.95 * _gridThicknessMultiplier);
                var mainPen = GetCachedSolidPen($"MP_{_mainGridColorHex}_{_mainGridOpacity:0.00}", mainBrush, mainThick);
                var mainHaloPen = GetCachedSolidPen($"MHP_{mainHaloAlpha}", haloBrush, mainThick + 1.8);
                var subHaloPen = GetCachedSolidPen($"SHP_{subHaloAlpha}", subHaloBrush, subThick + 1.2);
                var subPen = new Pen(subLineBrush, subThick) { DashStyle = new DashStyle(new double[] { 4, 2 }, 0) }; subPen.Freeze();

                if (_coordSystem.StartsWith("Sektorowa") && _mainGridOpacity > 0.02)
                {
                    int cols = 8, rows = 8; double cw = w / cols, ch = h / rows;
                    for (int c = 1; c < cols; c++) dc.DrawLine(mainPen, new Point(c * cw, 0), new Point(c * cw, h));
                    for (int r = 1; r < rows; r++) dc.DrawLine(mainPen, new Point(0, r * ch), new Point(w, r * ch));
                    for (int r = 0; r < rows; r++)
                        for (int c = 0; c < cols; c++)
                            DrawFastBadgeOnContext(dc, $"{(char)('A' + r)}{c + 1}", c * cw + 6, r * ch + 4, mainBrush, 10 * _fontScaleMultiplier, _mainGridOpacity, $"MAIN_{_mainGridColorHex}_{_mainGridOpacity:0.00}");
                }
                else if (_coordSystem.StartsWith("WGS84"))
                {
                    var (nwLat, nwLon) = ScreenToLatLon(0, 0, w, h); var (seLat, seLon) = ScreenToLatLon(w, h, w, h);
                    double lonSpan = Math.Max(0.0001, Math.Abs(seLon - nwLon)), dDeg = 0.005;
                    foreach (var ds in new[] { 0.0005, 0.001, 0.002, 0.005, 0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1.0 }) { if (lonSpan / ds <= 10.0) { dDeg = ds; break; } }

                    if (_mainGridOpacity > 0.02)
                    {
                        string mainCacheKey = $"WGS_{_mainGridColorHex}_{_mainGridOpacity:0.00}"; double mainFontSz = 9.5 * _fontScaleMultiplier;
                        for (double lon = Math.Floor(Math.Min(nwLon, seLon) / dDeg) * dDeg; lon <= Math.Max(nwLon, seLon) + dDeg; lon += dDeg)
                        {
                            double sx = LatLonToScreen(_centerLat, lon, w, h).X; if (sx < 0 || sx > w) continue;
                            if (useHalo) dc.DrawLine(mainHaloPen, new Point(sx, 0), new Point(sx, h));
                            dc.DrawLine(mainPen, new Point(sx, 0), new Point(sx, h));
                            DrawCartographicLineLabelOnContext(dc, $"{lon:0.0000}°E", sx, true, w, h, mainBrush, mainFontSz, _mainGridOpacity, mainCacheKey);
                        }
                        for (double lat = Math.Floor(Math.Min(seLat, nwLat) / dDeg) * dDeg; lat <= Math.Max(seLat, nwLat) + dDeg; lat += dDeg)
                        {
                            double sy = LatLonToScreen(lat, _centerLon, w, h).Y; if (sy < 0 || sy > h) continue;
                            if (useHalo) dc.DrawLine(mainHaloPen, new Point(0, sy), new Point(w, sy));
                            dc.DrawLine(mainPen, new Point(0, sy), new Point(w, sy));
                            DrawCartographicLineLabelOnContext(dc, $"{lat:0.0000}°N", sy, false, w, h, mainBrush, mainFontSz, _mainGridOpacity, mainCacheKey);
                        }
                    }
                }
                else
                {
                    double metersPerPixel = GetExactMetersPerPixel();
                    int effectiveStepMeters = _subGridDivision.Contains("250") ? 250 : (_subGridDivision.Contains("500") ? 500 : 100);
                    while (effectiveStepMeters / metersPerPixel < 32.0 && effectiveStepMeters < 10000) effectiveStepMeters *= 2;
                    _effectiveMgrsStepMeters = effectiveStepMeters;

                    int utmZone = (int)Math.Floor((_centerLon + 180.0) / 6.0) + 1;
                    UtmGrid2DProjector.ComputeSheetUtmEnvelope((sx, sy) => ScreenToLatLon(sx, sy, w, h), w, h, utmZone, effectiveStepMeters, out double firstEasting, out double lastEasting, out double firstNorthing, out double lastNorthing);

                    Point UtmToScreenPt(double eastVal, double northVal)
                    {
                        UtmGrid2DProjector.UtmToLatLon(eastVal, northVal, utmZone, out double ptLat, out double ptLon);
                        return LatLonToScreen(ptLat, ptLon, w, h);
                    }

                    double midE = (firstEasting + lastEasting) * 0.5, midN = (firstNorthing + lastNorthing) * 0.5;
                    double sampleStepPx = (UtmToScreenPt(midE + effectiveStepMeters, midN) - UtmToScreenPt(midE, midN)).Length;

                    if ((_subGridDivision.Contains("50 m") || _subGridDivision.Contains("33 m")) && _subGridOpacity > 0.02 && sampleStepPx >= 42.0)
                    {
                        bool isQuarters = _subGridDivision.Contains("50 m");
                        int div = isQuarters ? 2 : 3;
                        double subStepMeters = (double)effectiveStepMeters / div;

                        for (double east = firstEasting; east <= lastEasting; east += effectiveStepMeters)
                            for (int i = 1; i < div; i++)
                            {
                                Point pTop = UtmToScreenPt(east + i * subStepMeters, lastNorthing), pBot = UtmToScreenPt(east + i * subStepMeters, firstNorthing);
                                if (Math.Max(pTop.X, pBot.X) < -20 || Math.Min(pTop.X, pBot.X) > w + 20) continue;
                                if (useHalo) dc.DrawLine(subHaloPen, pTop, pBot);
                                dc.DrawLine(subPen, pTop, pBot);
                            }

                        for (double north = firstNorthing; north <= lastNorthing; north += effectiveStepMeters)
                            for (int j = 1; j < div; j++)
                            {
                                Point pLeft = UtmToScreenPt(firstEasting, north + j * subStepMeters), pRight = UtmToScreenPt(lastEasting, north + j * subStepMeters);
                                if (Math.Max(pLeft.Y, pRight.Y) < -20 || Math.Min(pLeft.Y, pRight.Y) > h + 20) continue;
                                if (useHalo) dc.DrawLine(subHaloPen, pLeft, pRight);
                                dc.DrawLine(subPen, pLeft, pRight);
                            }

                        if (_gridLabelsMode.Contains("Ćwiartki/Keypad") && sampleStepPx >= 58.0)
                        {
                            string[,] keypad3x3 = { { "7", "8", "9" }, { "4", "5", "6" }, { "1", "2", "3" } };
                            string[,] quarters2x2 = { { "A", "B" }, { "C", "D" } };
                            string subCacheKey = $"SUB_{_subLabelTextColorHex}_{_subGridOpacity:0.00}";
                            double subFontSz = 8.8 * _fontScaleMultiplier;

                            for (double east = firstEasting; east < lastEasting; east += effectiveStepMeters)
                                for (double north = firstNorthing; north < lastNorthing; north += effectiveStepMeters)
                                {
                                    Point cPt = UtmToScreenPt(east + effectiveStepMeters * 0.5, north + effectiveStepMeters * 0.5);
                                    if (cPt.X < -sampleStepPx || cPt.X > w + sampleStepPx || cPt.Y < -sampleStepPx || cPt.Y > h + sampleStepPx) continue;
                                    for (int r = 0; r < div; r++)
                                    {
                                        double subN = north + (div - 0.5 - r) * subStepMeters;
                                        for (int c = 0; c < div; c++)
                                        {
                                            Point subPt = UtmToScreenPt(east + (c + 0.5) * subStepMeters, subN);
                                            if (subPt.X > 14 && subPt.X < w - 14 && subPt.Y > 14 && subPt.Y < h - 14)
                                                DrawFastSubCellBadgeOnContext(dc, isQuarters ? quarters2x2[r, c] : keypad3x3[r, c], subPt.X, subPt.Y, subTextBrush, subBgBrush, subBoxBorderPen, subShadowBrush, subFontSz, subCacheKey);
                                        }
                                    }
                                }
                        }
                    }

                    if (_mainGridOpacity > 0.02)
                    {
                        string mainCacheKey = $"MAIN_{_mainGridColorHex}_{_mainGridOpacity:0.00}";
                        double mainFontSz = 9.5 * _fontScaleMultiplier;

                        for (double east = firstEasting; east <= lastEasting; east += effectiveStepMeters)
                        {
                            Point pTop = UtmToScreenPt(east, lastNorthing), pBot = UtmToScreenPt(east, firstNorthing);
                            if (Math.Max(pTop.X, pBot.X) < -20 || Math.Min(pTop.X, pBot.X) > w + 20) continue;
                            if (useHalo) dc.DrawLine(mainHaloPen, pTop, pBot);
                            dc.DrawLine(mainPen, pTop, pBot);
                            if (UtmGrid2DProjector.TryGetVerticalLineLabelPoint(pTop, pBot, 14.0, w, out double labelX))
                            {
                                int mgrsH = (((int)Math.Round(east / 100.0)) % 1000 + 1000) % 1000;
                                DrawCartographicLineLabelOnContext(dc, $"{mgrsH:D3}", labelX, true, w, h, mainBrush, mainFontSz, _mainGridOpacity, mainCacheKey);
                            }
                        }

                        for (double north = firstNorthing; north <= lastNorthing; north += effectiveStepMeters)
                        {
                            Point pLeft = UtmToScreenPt(firstEasting, north), pRight = UtmToScreenPt(lastEasting, north);
                            if (Math.Max(pLeft.Y, pRight.Y) < -20 || Math.Min(pLeft.Y, pRight.Y) > h + 20) continue;
                            if (useHalo) dc.DrawLine(mainHaloPen, pLeft, pRight);
                            dc.DrawLine(mainPen, pLeft, pRight);
                            if (UtmGrid2DProjector.TryGetHorizontalLineLabelPoint(pLeft, pRight, 18.0, h, out double labelY))
                            {
                                int mgrsH = (((int)Math.Round(north / 100.0)) % 1000 + 1000) % 1000;
                                DrawCartographicLineLabelOnContext(dc, $"{mgrsH:D3}", labelY, false, w, h, mainBrush, mainFontSz, _mainGridOpacity, mainCacheKey);
                            }
                        }
                    }
                }
            }

            if (_rulerStart != null && _rulerEnd != null)
            {
                Point p1 = LatLonToScreen(_rulerStart.Value.Lat, _rulerStart.Value.Lon, w, h), p2 = LatLonToScreen(_rulerEnd.Value.Lat, _rulerEnd.Value.Lon, w, h);
                var rPen = new Pen(TacticalConfigPanel.BrushAccent, 2.2) { DashStyle = new DashStyle(new double[] { 4, 2 }, 0) }; rPen.Freeze();
                dc.DrawLine(rPen, p1, p2);
                double dLat = (_rulerEnd.Value.Lat - _rulerStart.Value.Lat) * Math.PI / 180.0, dLon = (_rulerEnd.Value.Lon - _rulerStart.Value.Lon) * Math.PI / 180.0;
                double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(_rulerStart.Value.Lat * Math.PI / 180.0) * Math.Cos(_rulerEnd.Value.Lat * Math.PI / 180.0) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
                double dist = 6371000.0 * 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
                DrawFastBadgeOnContext(dc, $"📏 {dist:0} m", (p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2 - 14, TacticalConfigPanel.BrushAccent, 10.5 * _fontScaleMultiplier, 1.0, "RULER");
            }
        }

        public static JsonObject ExportMapStateToJson()
        {
            var root = new JsonObject
            {
                ["centerLat"] = _centerLat, ["centerLon"] = _centerLon, ["zoom"] = _zoom, ["scale"] = _targetScaleDenominator,
                ["orientation"] = _sheetOrientation, ["sheetFormat"] = _selectedSheet?.Name ?? "", ["layerId"] = _activeLayer?.Id ?? "google_hyb",
                ["showLidar"] = _showLidarSingleWms, ["lidarOpacity"] = _lidarOpacity, ["coordSystem"] = _coordSystem, ["subGridDivision"] = _subGridDivision,
                ["gridLabelsMode"] = _gridLabelsMode, ["labelBgStyle"] = _labelBgStyle, ["gridThickness"] = _gridThicknessMultiplier, ["fontScale"] = _fontScaleMultiplier,
                ["mainGridOpacity"] = _mainGridOpacity, ["subGridOpacity"] = _subGridOpacity, ["subLabelBgOpacity"] = _subLabelBgOpacity,
                ["mapBrightnessBoost"] = _mapBrightnessBoost, ["mapDimmerOpacity"] = _mapDimmerOpacity,
                ["mainGridColorHex"] = _mainGridColorHex, ["subGridLineColorHex"] = _subGridLineColorHex, ["subLabelTextColorHex"] = _subLabelTextColorHex, ["subLabelBgColorHex"] = _subLabelBgColorHex,
                ["gridHaloMode"] = _gridHaloMode, ["aoOutPattern"] = _aoOutPattern, ["aoOutMaskOpacity"] = _aoOutMaskOpacity, ["showAllObjectLabels"] = _showAllObjectLabels,
                ["showRotationHandlesOnMap"] = _showRotationHandlesOnMap, ["showNorthArrow"] = _showNorthArrow, ["showScaleBar"] = _showScaleBar, ["showLegend"] = _showLegend, ["showNatoSymbolsInLegend"] = _showNatoSymbolsInLegend
            };
            var facArr = new JsonArray(); foreach (var fn in _selectedMapFactions) facArr.Add(fn); root["selectedFactions"] = facArr;
            var arr = new JsonArray();
            foreach (var o in _mapObjects)
            {
                var pts = new JsonArray(); foreach (var p in o.Points) pts.Add(new JsonObject { ["lat"] = p.Lat, ["lon"] = p.Lon });
                arr.Add(new JsonObject { ["kind"] = o.Kind, ["symbol"] = o.SymbolCode, ["name"] = o.Name, ["color"] = o.ColorHex, ["showLabel"] = o.ShowLabel, ["sizeMultiplier"] = o.SizeMultiplier, ["rotationDeg"] = o.RotationDeg, ["points"] = pts });
            }
            root["objects"] = arr; ExportExtraMapUiState(root); return root;
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
                if (root["sheetFormat"] != null) { var foundSf = SheetFormats.FirstOrDefault(s => s.Name.Equals(root["sheetFormat"].ToString(), StringComparison.OrdinalIgnoreCase)); if (foundSf != null) _selectedSheet = foundSf; }
                if (root["layerId"] != null) { var foundLayer = Layers.FirstOrDefault(l => l.Id.Equals(root["layerId"].ToString(), StringComparison.OrdinalIgnoreCase)); if (foundLayer != null) _activeLayer = foundLayer; }
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
                if (double.TryParse(root["subLabelBgOpacity"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double slbo)) _subLabelBgOpacity = slbo;
                if (double.TryParse(root["mapBrightnessBoost"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double mbb))
                {
                    _mapBrightnessBoost = Math.Clamp(mbb, 0.0, 0.85);
                    GeoportalLidarEngine.ActiveBrightnessBoost = _mapBrightnessBoost;
                }
                if (double.TryParse(root["mapDimmerOpacity"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double mdo)) _mapDimmerOpacity = mdo;
                if (root["mainGridColorHex"] != null) _mainGridColorHex = root["mainGridColorHex"].ToString();
                if (root["subGridLineColorHex"] != null) _subGridLineColorHex = root["subGridLineColorHex"].ToString();
                if (root["subLabelTextColorHex"] != null) _subLabelTextColorHex = root["subLabelTextColorHex"].ToString();
                if (root["subLabelBgColorHex"] != null) _subLabelBgColorHex = root["subLabelBgColorHex"].ToString();
                if (root["gridHaloMode"] != null) _gridHaloMode = root["gridHaloMode"].ToString();
                if (root["aoOutPattern"] != null) _aoOutPattern = root["aoOutPattern"].ToString();
                if (double.TryParse(root["aoOutMaskOpacity"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double aom)) _aoOutMaskOpacity = aom;
                if (bool.TryParse(root["showAllObjectLabels"]?.ToString(), out bool sal)) _showAllObjectLabels = sal;
                if (bool.TryParse(root["showRotationHandlesOnMap"]?.ToString(), out bool srh)) _showRotationHandlesOnMap = srh;
                if (bool.TryParse(root["showNorthArrow"]?.ToString(), out bool sna)) _showNorthArrow = sna;
                if (bool.TryParse(root["showScaleBar"]?.ToString(), out bool ssb)) _showScaleBar = ssb;
                if (bool.TryParse(root["showLegend"]?.ToString(), out bool slg)) _showLegend = slg;
                if (bool.TryParse(root["showNatoSymbolsInLegend"]?.ToString(), out bool sns)) _showNatoSymbolsInLegend = sns;

                if (root["selectedFactions"] is JsonArray fArr) { _selectedMapFactions.Clear(); foreach (var item in fArr) if (item != null) _selectedMapFactions.Add(item.ToString()); }
                if (root["objects"] is JsonArray arr)
                {
                    _mapObjects.Clear();
                    foreach (var node in arr.OfType<JsonObject>())
                    {
                        bool shLbl = !bool.TryParse(node["showLabel"]?.ToString(), out bool parsedLbl) || parsedLbl;
                        double szMult = double.TryParse(node["sizeMultiplier"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double sm) ? Math.Clamp(sm, 0.35, 3.0) : 1.0;
                        double rotDeg = double.TryParse(node["rotationDeg"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double rd) ? ((rd % 360.0 + 360.0) % 360.0) : 0.0;
                        var item = new TacticalMapObjectItem { Kind = node["kind"]?.ToString() ?? "Marker", SymbolCode = node["symbol"]?.ToString() ?? "NATO_HQ", Name = node["name"]?.ToString() ?? "Punkt", ColorHex = node["color"]?.ToString() ?? "#2684FF", ShowLabel = shLbl, SizeMultiplier = szMult, RotationDeg = rotDeg };
                        if (node["points"] is JsonArray pts)
                            foreach (var p in pts.OfType<JsonObject>())
                                if (double.TryParse(p["lat"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double pla) && double.TryParse(p["lon"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double plo))
                                    item.Points.Add((pla, plo));
                        if (item.Points.Count > 0) _mapObjects.Add(item);
                    }
                }

                GlyphCache.Clear(); BrightenedTileCache.Clear(); ImportExtraMapUiState(root);
                if (_fmtComboBox != null) _fmtComboBox.SelectedItem = _selectedSheet;
                if (_oriComboBox != null) _oriComboBox.SelectedItem = _sheetOrientation;
                if (_layerComboBox != null) _layerComboBox.SelectedItem = _activeLayer;
                if (_hostWindow != null) RefreshObjectsListUI(_hostWindow);
                RebuildFactionCheckboxesUI(); ApplyScaleDenominator(_targetScaleDenominator);
            }
            catch { }
        }
    }
}
