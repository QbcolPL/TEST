#pragma warning disable CS0414, CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625
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
        public BitmapSource Bitmap { get; set; }
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

        private static void ExecSql(IntPtr db, string sql) => sqlite3_exec(db, ToUtf8NullTerm(sql), IntPtr.Zero, IntPtr.Zero, out IntPtr _);

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

        // Warstwy bazowe z pełnym łańcuchem połączeń GUGiK (EPSG:2180 / EPSG:3857 / EPSG:4326)
        public static readonly List<TacticalMapLayerDef> Layers = new List<TacticalMapLayerDef>
        {
            new TacticalMapLayerDef { Id = "google_hyb", Name = "Google Satelita + Opisy", IsWmsEpsg3857 = false, UrlTemplate = "https://mt1.google.com/vt/lyrs=y&x={x}&y={y}&z={z}" },
            new TacticalMapLayerDef { Id = "google_sat", Name = "Google Satelita (Czysta Ortofotomapa)", IsWmsEpsg3857 = false, UrlTemplate = "https://mt1.google.com/vt/lyrs=s&x={x}&y={y}&z={z}" },
            new TacticalMapLayerDef { Id = "google_ter", Name = "Google Teren (Warstwice)", IsWmsEpsg3857 = false, UrlTemplate = "https://mt1.google.com/vt/lyrs=p&x={x}&y={y}&z={z}" },
            new TacticalMapLayerDef
            {
                Id = "geoportal_std",
                Name = "Geoportal Ortofotomapa Standard (PZGIK — Cała Polska)",
                IsWmsEpsg3857 = true,
                UrlTemplate = "https://mapy.geoportal.gov.pl/wss/service/PZGIK/ORTO/WMS/StandardResolution?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap&FORMAT=image/jpeg&TRANSPARENT=TRUE&LAYERS=Raster&STYLES=&CRS=EPSG:2180&BBOX={bbox2180_ne}&WIDTH={w}&HEIGHT={h}",
                FallbackUrlTemplate = "https://mapy.geoportal.gov.pl/wss/service/PZGIK/ORTO/WMS/StandardResolution?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&FORMAT=image/jpeg&LAYERS=Raster&STYLES=&SRS=EPSG:3857&BBOX={bbox}&WIDTH={w}&HEIGHT={h}",
                SecondFallbackUrlTemplate = "https://integracja.gugik.gov.pl/cgi-bin/KrajowaIntegracjaOrtofotomapy?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&FORMAT=image/jpeg&LAYERS=orto&STYLES=&SRS=EPSG:2180&BBOX={bbox2180_en}&WIDTH={w}&HEIGHT={h}"
            },
            new TacticalMapLayerDef
            {
                Id = "geoportal_hires",
                Name = "Geoportal Ortofotomapa HIGH-RES (Miasta 5cm + Auto-Standard)",
                IsWmsEpsg3857 = true,
                UrlTemplate = "https://mapy.geoportal.gov.pl/wss/service/PZGIK/ORTO/WMS/HighResolution?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap&FORMAT=image/jpeg&TRANSPARENT=TRUE&LAYERS=Raster&STYLES=&CRS=EPSG:2180&BBOX={bbox2180_ne}&WIDTH={w}&HEIGHT={h}",
                FallbackUrlTemplate = "https://mapy.geoportal.gov.pl/wss/service/PZGIK/ORTO/WMS/StandardResolution?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap&FORMAT=image/jpeg&TRANSPARENT=TRUE&LAYERS=Raster&STYLES=&CRS=EPSG:2180&BBOX={bbox2180_ne}&WIDTH={w}&HEIGHT={h}",
                SecondFallbackUrlTemplate = "https://mapy.geoportal.gov.pl/wss/service/PZGIK/ORTO/WMS/StandardResolution?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&FORMAT=image/jpeg&LAYERS=Raster&STYLES=&SRS=EPSG:3857&BBOX={bbox}&WIDTH={w}&HEIGHT={h}"
            },
            new TacticalMapLayerDef
            {
                Id = "geoportal_lidar",
                Name = "Geoportal LiDAR ISOK (Cieniowanie NMT GRID1 — PNG)",
                IsWmsEpsg3857 = true,
                // Kluczowe poprawki dla LiDAR: FORMAT=image/png, TRANSPARENT=TRUE, STYLES=default, CRS=EPSG:2180 WMS 1.3.0
                UrlTemplate = "https://mapy.geoportal.gov.pl/wss/service/PZGIK/NMT/GRID1/WMS/ShadedRelief?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap&FORMAT=image/png&TRANSPARENT=TRUE&LAYERS=Raster&STYLES=default&CRS=EPSG:2180&BBOX={bbox2180_ne}&WIDTH={w}&HEIGHT={h}",
                FallbackUrlTemplate = "https://mapy.geoportal.gov.pl/wss/service/PZGIK/NMT/GRID1/WMS/ShadedRelief?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&FORMAT=image/png&TRANSPARENT=TRUE&LAYERS=Raster&STYLES=default&SRS=EPSG:2180&BBOX={bbox2180_en}&WIDTH={w}&HEIGHT={h}",
                SecondFallbackUrlTemplate = "https://mapy.geoportal.gov.pl/wss/service/PZGIK/NMT/GRID1/WMS/ShadedRelief?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&FORMAT=image/png&TRANSPARENT=TRUE&LAYERS=Raster&STYLES=default&SRS=EPSG:3857&BBOX={bbox}&WIDTH={w}&HEIGHT={h}"
            },
            new TacticalMapLayerDef
            {
                Id = "geoportal_topo",
                Name = "Geoportal Mapa Topograficzna (TOPO / BDOT10k)",
                IsWmsEpsg3857 = true,
                UrlTemplate = "https://mapy.geoportal.gov.pl/wss/service/img/guest/TOPO/MapServer/WMSServer?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap&FORMAT=image/jpeg&LAYERS=Raster&STYLES=&CRS=EPSG:2180&BBOX={bbox2180_ne}&WIDTH={w}&HEIGHT={h}",
                FallbackUrlTemplate = "https://mapy.geoportal.gov.pl/wss/service/img/guest/TOPO/MapServer/WMSServer?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&FORMAT=image/jpeg&LAYERS=Raster&STYLES=&SRS=EPSG:2180&BBOX={bbox2180_en}&WIDTH={w}&HEIGHT={h}",
                SecondFallbackUrlTemplate = "https://mapy.geoportal.gov.pl/wss/ext/KrajowaIntegracjaMapTopograficznych?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&FORMAT=image/png&LAYERS=raster&STYLES=&SRS=EPSG:2180&BBOX={bbox2180_en}&WIDTH={w}&HEIGHT={h}"
            },
            new TacticalMapLayerDef { Id = "esri_sat", Name = "ESRI World Imagery (Ortofotomapa)", IsWmsEpsg3857 = false, UrlTemplate = "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}" },
            new TacticalMapLayerDef { Id = "osm_std", Name = "OpenStreetMap Standard", IsWmsEpsg3857 = false, UrlTemplate = "https://tile.openstreetmap.org/{z}/{x}/{y}.png" },
            new TacticalMapLayerDef { Id = "open_topomap", Name = "OpenTopoMap (Mapa Topograficzna)", IsWmsEpsg3857 = false, UrlTemplate = "https://a.tile.opentopomap.org/{z}/{x}/{y}.png" },
            new TacticalMapLayerDef { Id = "open_fotomap", Name = "OpenFotoMap / CyclOSM (Siatka Szlaków)", IsWmsEpsg3857 = false, UrlTemplate = "https://a.tile-cyclosm.openstreetmap.fr/cyclosm/{z}/{x}/{y}.png" }
        };

        private const double WmsOverscanFactor = 1.25;

        private static readonly HttpClient Http = CreateHttpClient();
        private static readonly SemaphoreSlim TileThrottle = new SemaphoreSlim(16, 16);
        private static readonly SemaphoreSlim WmsDedicatedThrottle = new SemaphoreSlim(6, 6);
        private static readonly ConcurrentDictionary<string, byte[]> RawTileCache = new ConcurrentDictionary<string, byte[]>();
        private static readonly ConcurrentDictionary<string, BitmapSource> DecodedImageCache = new ConcurrentDictionary<string, BitmapSource>();
        private static readonly ConcurrentDictionary<string, byte> PendingTileUrls = new ConcurrentDictionary<string, byte>();
        private static readonly ConcurrentDictionary<string, FormattedText> GlyphCache = new ConcurrentDictionary<string, FormattedText>();
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
        private static DateTime _lastWmsLivePanTriggerUtc = DateTime.MinValue;
        private static DispatcherTimer _tileWatchdogTimer;
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

        private static int _offlineMinZoom = 13;
        private static int _offlineMaxZoom = 17;
        private static string _offlineBoundsMode = "Widoczny arkusz mapy";
        private static string _offlineDbFormat = "MBTiles (.mbtiles — ATAK 5.x)";
        private static bool _bakeLidarInOffline = true;
        private static bool _includeVectorsWithOffline = true;

        private static string _coordSystem = "MGRS / UTM (Siatka wojskowa w metrach)";
        private static string _subGridDivision = "Co 33 m (Keypad 1-9)";
        private static string _gridLabelsMode = "MGRS + Ćwiartki/Keypad";
        private static string _labelBgStyle = "Kaseton (Kolor tła RGB)";
        private static double _gridThicknessMultiplier = 1.0;
        private static double _fontScaleMultiplier = 1.0;
        private static double _mainGridOpacity = 0.85;
        private static double _subGridOpacity = 0.85;
        private static double _subLabelBgOpacity = 0.82;
        private static double _mapDimmerOpacity = 0.0;
        private static double _lidarOpacity = 0.45;
        private static string _mainGridColorHex = "#FAD02C";
        private static string _subGridLineColorHex = "#1FE0C5";
        private static string _subLabelTextColorHex = "#FFFFFF";
        private static string _subLabelBgColorHex = "#061014";
        private static string _gridHaloMode = "Pełny czarny obrys";

        private static string _activeTool = "PAN";
        private static string _selectedObjectColorHex = "#2684FF";
        private static string _selectedNatoSymbolCode = "SHAPE_ARROW";
        private static bool _showAllObjectLabels = true;
        private static bool _newObjectShowLabelDefault = true;
        private static bool _showRotationHandlesOnMap = true;
        private static bool _isExportingCleanMap = false;
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

        private static bool _isPanningMap;
        private static Point _mouseDownPoint;
        private static Point _currentHoverScreenPt;
        private static bool _isHoveringCanvas;
        private static TacticalMapObjectItem _draggedObject;
        private static int _draggedVertexIndex = -1;
        private static Point _markerDragScreenOffset;
        private static Point _rotationCenterScreenPt;
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
                    MaxConnectionsPerServer = 28
                };
                var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
                c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
                c.DefaultRequestHeaders.Referrer = new Uri("https://mapy.geoportal.gov.pl/");
                c.DefaultRequestHeaders.Accept.ParseAdd("image/png,image/jpeg,image/*;q=0.9,*/*;q=0.5");
                return c;
            }
            catch
            {
                return new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            }
        }

        private static void EnsureTileWatchdogStarted()
        {
            if (_tileWatchdogTimer != null || Application.Current == null) return;
            _tileWatchdogTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
            _tileWatchdogTimer.Tick += (_, __) =>
            {
                if (_fullScreenOverlay?.Visibility != Visibility.Visible || _activeLayer == null) return;

                if (!_activeLayer.IsWmsEpsg3857 && _hasMissingVisibleTiles)
                {
                    RefreshTileCanvasesOnly();
                }

                if ((_activeLayer.IsWmsEpsg3857 || _showLidarSingleWms) && !_isWmsSheetFetchRunning && !_isPanningMap)
                {
                    bool needBase = _activeLayer.IsWmsEpsg3857 &&
                        (_wmsLoadedLayerId != _activeLayer.Id ||
                         _wmsLoadedScaleDenominator != _targetScaleDenominator ||
                         Math.Abs(_wmsLoadedCenterLat - _centerLat) > 0.00004 ||
                         Math.Abs(_wmsLoadedCenterLon - _centerLon) > 0.00004 ||
                         _geoAnchoredBaseSheets.Count == 0);

                    bool needLidar = _showLidarSingleWms && _activeLayer.Id != "geoportal_lidar" &&
                        (!_wmsLoadedLidarFlag ||
                         _wmsLoadedScaleDenominator != _targetScaleDenominator ||
                         Math.Abs(_wmsLoadedCenterLat - _centerLat) > 0.00004 ||
                         Math.Abs(_wmsLoadedCenterLon - _centerLon) > 0.00004 ||
                         _geoAnchoredLidarSheets.Count == 0);

                    if (needBase || needLidar)
                    {
                        _ = RenderMapTilesAsync();
                    }
                }
            };
            _tileWatchdogTimer.Start();
        }

        private static bool IsValidImagePayload(byte[] data)
        {
            if (data == null || data.Length < 128) return false;
            if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return true; // JPEG
            if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) return true; // PNG
            if (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46) return true; // GIF
            if (data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46) return true; // WEBP
            return false;
        }

        private static bool IsBlankWhiteWmsPayload(byte[] rawBytes, BitmapSource bmp)
        {
            if (rawBytes == null || rawBytes.Length < 256 || bmp == null || bmp.PixelWidth < 8 || bmp.PixelHeight < 8)
                return true;

            try
            {
                var converted = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
                int w = converted.PixelWidth, h = converted.PixelHeight;
                int stride = w * 4;
                byte[] pixels = new byte[h * stride];
                converted.CopyPixels(pixels, stride, 0);

                int gridN = 12;
                int pureWhiteOrTransparent = 0, totalSamples = 0;

                for (int r = 1; r < gridN; r++)
                {
                    int py = (h * r) / gridN;
                    for (int c = 1; c < gridN; c++)
                    {
                        int px = (w * c) / gridN;
                        int idx = py * stride + px * 4;
                        if (idx + 3 < pixels.Length)
                        {
                            byte b = pixels[idx], g = pixels[idx + 1], red = pixels[idx + 2], a = pixels[idx + 3];
                            totalSamples++;
                            if (a < 15 || (red >= 253 && g >= 253 && b >= 253))
                                pureWhiteOrTransparent++;
                        }
                    }
                }

                if (totalSamples == 0) return true;
                return ((double)pureWhiteOrTransparent / totalSamples) >= 0.88;
            }
            catch { return false; }
        }

        private static StackPanel BuildSection2GridAndColorsUI(Window win)
        {
            var sec2 = CreateSectionCard("2. SIATKA MGRS / WGS84 (NA LINII) & KOLORY RGB (KEYPAD / ĆWIARTKI)");
            sec2.Children.Add(TacticalConfigPanel.FieldBox("System współrzędnych i typ siatki na mapie:", TacticalConfigPanel.ComboCtrl(new[] { "MGRS / UTM (Siatka wojskowa w metrach)", "Siatka Sektorowa (A1, B2, C3...)", "WGS84 (Stopnie geograficzne Lat/Lon)", "Brak siatki" }, _coordSystem, win, v => { _coordSystem = v; RedrawVectorsOnly(); })));

            var subGridRow = new UniformGrid { Columns = 2 };
            subGridRow.Children.Add(TacticalConfigPanel.FieldBox("Podział wewnątrz oczek:", TacticalConfigPanel.ComboCtrl(new[] { "Co 33 m (Keypad 1-9)", "Co 50 m (Ćwiartki A,B,C,D)", "Brak podziału (Tylko 100 m)", "Siatka co 250 m", "Siatka co 500 m" }, _subGridDivision, win, v => { _subGridDivision = v; RedrawVectorsOnly(); })));
            subGridRow.Children.Add(TacticalConfigPanel.FieldBox("Opisy kratek:", TacticalConfigPanel.ComboCtrl(new[] { "MGRS + Ćwiartki/Keypad", "Tylko główne linie MGRS", "Brak opisów wewnątrz" }, _gridLabelsMode, win, v => { _gridLabelsMode = v; RedrawVectorsOnly(); })));
            sec2.Children.Add(subGridRow);

            UIElement MakeRgbPickerButton(string fieldTitle, string dialogTitle, Func<string> getter, Action<string> setter)
            {
                string initHex = getter();
                var swatch = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = TacticalConfigPanel.BrushFromHex(initHex, Colors.White), BorderBrush = Brushes.White, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
                var lbl = new TextBlock { Text = initHex.ToUpperInvariant(), Foreground = Brushes.White, FontSize = 9.5, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
                var st = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                st.Children.Add(swatch); st.Children.Add(lbl);

                var btn = new Button { Content = st, Height = 25, Background = TacticalConfigPanel.BrushInputBg, BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
                btn.Click += (s, e) =>
                {
                    string picked = TacticalConfigPanel.ShowRgbColorPickerDialog(win, dialogTitle, getter());
                    if (!string.IsNullOrEmpty(picked))
                    {
                        setter(picked);
                        swatch.Background = TacticalConfigPanel.BrushFromHex(picked, Colors.White);
                        lbl.Text = picked.ToUpperInvariant();
                        GlyphCache.Clear();
                        TacticalConfigPanel.MarkDirty();
                        _fastOverlayRenderer?.InvalidateVisual();
                    }
                };
                return TacticalConfigPanel.FieldBox(fieldTitle, btn);
            }

            var rgbGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 2, 0, 4) };
            rgbGrid.Children.Add(MakeRgbPickerButton("Kolor cyfr Keypad/Ćwiartek (RGB):", "Kolor cyfr i liter Keypad (1-9) / Ćwiartek (A-D)", () => _subLabelTextColorHex, v => _subLabelTextColorHex = v));
            rgbGrid.Children.Add(MakeRgbPickerButton("Kolor tła Keypad/Ćwiartek (RGB):", "Kolor tła (kasetonu) pod cyframi Keypad / Ćwiartek", () => _subLabelBgColorHex, v => _subLabelBgColorHex = v));
            rgbGrid.Children.Add(MakeRgbPickerButton("Kolor linii wewnętrznych (RGB):", "Kolor przerywanych linii podziałki Keypad / Ćwiartek", () => _subGridLineColorHex, v => _subGridLineColorHex = v));
            rgbGrid.Children.Add(MakeRgbPickerButton("Kolor siatki głównej MGRS (RGB):", "Kolor głównych linii i opisów MGRS / WGS84", () => _mainGridColorHex, v => _mainGridColorHex = v));
            sec2.Children.Add(rgbGrid);

            sec2.Children.Add(MakeSliderRow("Grubość linii siatki (głównej i pomocniczej):", 0.5, 3.0, _gridThicknessMultiplier, v => $"{v:0.1}x", v => { _gridThicknessMultiplier = Math.Round(v, 1); _fastOverlayRenderer?.InvalidateVisual(); }));
            sec2.Children.Add(MakeSliderRow("Rozmiar / grubość czcionki współrzędnych (MGRS i Keypad):", 0.7, 2.0, _fontScaleMultiplier, v => $"{v:0.1}x", v => { _fontScaleMultiplier = Math.Round(v, 1); GlyphCache.Clear(); _fastOverlayRenderer?.InvalidateVisual(); }));
            sec2.Children.Add(MakeSliderRow("Widoczność (alfa) głównych linii i opisów (MGRS / WGS84):", 0.05, 1.0, _mainGridOpacity, v => $"{(int)Math.Round(v * 100)}%", v => { _mainGridOpacity = Math.Round(v, 2); _fastOverlayRenderer?.InvalidateVisual(); }));
            sec2.Children.Add(MakeSliderRow("Widoczność (alfa) podziałki wewnętrznej (Keypad 1-9 / Ćwiartki):", 0.0, 1.0, _subGridOpacity, v => $"{(int)Math.Round(v * 100)}%", v => { _subGridOpacity = Math.Round(v, 2); _fastOverlayRenderer?.InvalidateVisual(); }));
            sec2.Children.Add(MakeSliderRow("Krycie tła (kasetonu) pod cyframi Keypad / Ćwiartek:", 0.0, 1.0, _subLabelBgOpacity, v => $"{(int)Math.Round(v * 100)}%", v => { _subLabelBgOpacity = Math.Round(v, 2); _fastOverlayRenderer?.InvalidateVisual(); }));
            sec2.Children.Add(MakeSliderRow("Przyciemnienie podkładu mapy (większy kontrast pod siatkę):", 0.0, 0.65, _mapDimmerOpacity, v => $"{(int)Math.Round(v * 100)}%", v =>
            {
                _mapDimmerOpacity = Math.Round(v, 2);
                if (_dimmerRect != null) _dimmerRect.Opacity = _mapDimmerOpacity;
            }));

            var haloAndBgRow = new UniformGrid { Columns = 2 };
            haloAndBgRow.Children.Add(TacticalConfigPanel.FieldBox("Obrys linii (Halo):", TacticalConfigPanel.ComboCtrl(new[] { "Pełny czarny obrys", "Półprzezroczysty obrys", "Brak obrysu" }, _gridHaloMode, win, v => { _gridHaloMode = v; _fastOverlayRenderer?.InvalidateVisual(); })));
            haloAndBgRow.Children.Add(TacticalConfigPanel.FieldBox("Styl opisów i cyfr:", TacticalConfigPanel.ComboCtrl(new[] { "Kaseton (Kolor tła RGB)", "Tylko obrys Halo", "Czysty tekst" }, _labelBgStyle, win, v => { _labelBgStyle = v; _fastOverlayRenderer?.InvalidateVisual(); })));
            sec2.Children.Add(haloAndBgRow);

            return sec2;
        }

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
            return Math.Max(0.04, realTerrainWidthMeters / Math.Max(100.0, canvasW));
        }

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

        private static (double Easting, double Northing) LatLonToEpsg2180(double lat, double lon)
        {
            const double a = 6378137.0;
            const double eccSquared = 0.00669438002290;
            const double k0 = 0.9993;
            const double lonOriginRad = 19.0 * Math.PI / 180.0;

            double latRad = lat * Math.PI / 180.0;
            double lonRad = lon * Math.PI / 180.0;

            double eccPrimeSquared = eccSquared / (1.0 - eccSquared);
            double N = a / Math.Sqrt(1.0 - eccSquared * Math.Sin(latRad) * Math.Sin(latRad));
            double T = Math.Tan(latRad) * Math.Tan(latRad);
            double C = eccPrimeSquared * Math.Cos(latRad) * Math.Cos(latRad);
            double A = Math.Cos(latRad) * (lonRad - lonOriginRad);

            double M = a * ((1.0 - eccSquared / 4.0 - 3.0 * eccSquared * eccSquared / 64.0) * latRad
                - (3.0 * eccSquared / 8.0 + 3.0 * eccSquared * eccSquared / 32.0) * Math.Sin(2.0 * latRad)
                + (15.0 * eccSquared * eccSquared / 256.0) * Math.Sin(4.0 * latRad));

            double easting = k0 * N * (A + (1.0 - T + C) * A * A * A / 6.0
                + (5.0 - 18.0 * T + T * T + 72.0 * C - 58.0 * eccPrimeSquared) * Math.Pow(A, 5) / 120.0) + 500000.0;

            double northing = k0 * (M + N * Math.Tan(latRad) * (A * A / 2.0
                + (5.0 - T + 9.0 * C + 4.0 * C * C) * Math.Pow(A, 4) / 24.0
                + (61.0 - 58.0 * T + T * T + 600.0 * C - 330.0 * eccPrimeSquared) * Math.Pow(A, 6) / 720.0)) - 5300000.0;

            return (easting, northing);
        }

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

        private static void UtmMetersToLatLon(double easting, double northing, int zone, out double lat, out double lon)
        {
            const double a = 6378137.0;
            const double eccSquared = 0.00669438;
            const double k0 = 0.9996;

            double eccPrimeSquared = eccSquared / (1.0 - eccSquared);
            double e1 = (1.0 - Math.Sqrt(1.0 - eccSquared)) / (1.0 + Math.Sqrt(1.0 - eccSquared));

            double x = easting - 500000.0;
            double y = northing;

            double lonOrigin = (zone - 1) * 6 - 180 + 3;
            double M = y / k0;
            double mu = M / (a * (1.0 - eccSquared / 4.0 - 3.0 * eccSquared * eccSquared / 64.0 - 5.0 * Math.Pow(eccSquared, 3) / 256.0));

            double phi1Rad = mu
                + (3.0 * e1 / 2.0 - 27.0 * Math.Pow(e1, 3) / 32.0) * Math.Sin(2.0 * mu)
                + (21.0 * e1 * e1 / 16.0 - 55.0 * Math.Pow(e1, 4) / 32.0) * Math.Sin(4.0 * mu)
                + (151.0 * Math.Pow(e1, 3) / 96.0) * Math.Sin(6.0 * mu);

            double N1 = a / Math.Sqrt(1.0 - eccSquared * Math.Sin(phi1Rad) * Math.Sin(phi1Rad));
            double T1 = Math.Tan(phi1Rad) * Math.Tan(phi1Rad);
            double C1 = eccPrimeSquared * Math.Cos(phi1Rad) * Math.Cos(phi1Rad);
            double R1 = a * (1.0 - eccSquared) / Math.Pow(1.0 - eccSquared * Math.Sin(phi1Rad) * Math.Sin(phi1Rad), 1.5);
            double D = x / (N1 * k0);

            double latRad = phi1Rad - (N1 * Math.Tan(phi1Rad) / R1) * (D * D / 2.0
                - (5.0 + 3.0 * T1 + 10.0 * C1 - 4.0 * C1 * C1 - 9.0 * eccPrimeSquared) * Math.Pow(D, 4) / 24.0
                + (61.0 + 90.0 * T1 + 298.0 * C1 + 45.0 * T1 * T1 - 252.0 * eccPrimeSquared - 3.0 * C1 * C1) * Math.Pow(D, 6) / 720.0);

            double lonRad = (D
                - (1.0 + 2.0 * T1 + C1) * Math.Pow(D, 3) / 6.0
                + (5.0 - 2.0 * C1 + 28.0 * T1 - 3.0 * C1 * C1 + 8.0 * eccPrimeSquared + 24.0 * T1 * T1) * Math.Pow(D, 5) / 120.0) / Math.Cos(phi1Rad);

            lat = latRad * 180.0 / Math.PI;
            lon = lonOrigin + lonRad * 180.0 / Math.PI;
        }

        public static (string FullMgrs, string ShortGrid100m, string KeypadAndQuarter, string UtmText, string WgsText) FormatTacticalPointCoordinates(double lat, double lon)
        {
            int zone = (int)Math.Floor((lon + 180.0) / 6.0) + 1;
            const double refEasting = 360000.0;
            const double refNorthing = 5530000.0;

            LatLonToUtmMeters(lat, lon, zone, out double rawE, out double rawN);
            double easting = rawE;
            double northing = rawN;
            for (int iter = 0; iter < 2; iter++)
            {
                UtmMetersToLatLon(easting, refNorthing, zone, out _, out double testLon);
                easting += (lon - testLon) * 111319.49 * Math.Cos(lat * Math.PI / 180.0);
                UtmMetersToLatLon(refEasting, northing, zone, out double testLat, out _);
                northing += (lat - testLat) * 111132.95;
            }

            string bands = "CDEFGHJKLMNPQRSTUVWXX";
            int bandIdx = Math.Clamp((int)Math.Floor((lat + 80.0) / 8.0), 0, bands.Length - 1);
            char latBand = bands[bandIdx];

            int set = ((zone - 1) % 6) + 1;
            string colLetters = (set % 3) switch
            {
                1 => "ABCDEFGH",
                2 => "JKLMNPQR",
                _ => "STUVWXYZ"
            };
            string rowLetters = (set % 2 == 1) ? "ABCDEFGHJKLMNPQRSTUV" : "FGHJKLMNPQRSTUVABCDE";

            int colIdx = Math.Clamp(((int)Math.Floor(easting / 100000.0)) - 1, 0, 7);
            int rowIdx = ((int)Math.Floor(northing / 100000.0)) % 20;
            if (rowIdx < 0) rowIdx += 20;
            string sq100k = $"{colLetters[colIdx]}{rowLetters[rowIdx]}";

            int e5 = ((int)Math.Floor(easting)) % 100000;
            if (e5 < 0) e5 += 100000;
            int n5 = ((int)Math.Floor(northing)) % 100000;
            if (n5 < 0) n5 += 100000;

            int stepM = Math.Max(100, _effectiveMgrsStepMeters);
            double remE = (easting % stepM + stepM) % stepM;
            double remN = (northing % stepM + stepM) % stepM;

            int kpCol = Math.Clamp((int)Math.Floor(remE / (stepM / 3.0)), 0, 2);
            int kpRowFromBottom = Math.Clamp((int)Math.Floor(remN / (stepM / 3.0)), 0, 2);
            int[,] kpMap = { { 1, 2, 3 }, { 4, 5, 6 }, { 7, 8, 9 } };
            int keypadNum = kpMap[kpRowFromBottom, kpCol];

            int qCol = remE >= (stepM / 2.0) ? 1 : 0;
            int qRowFromTop = remN >= (stepM / 2.0) ? 0 : 1;
            string[,] qMap = { { "A", "B" }, { "C", "D" } };
            string quarterChar = qMap[qRowFromTop, qCol];

            int e3 = (e5 / 100) % 1000;
            int n3 = (n5 / 100) % 1000;

            string fullMgrs = $"{zone}{latBand} {sq100k} {e5:D5} {n5:D5}";
            string shortGrid = $"{e3:D3} / {n3:D3}";
            string kpInfo = $"Keypad: {keypadNum}  |  Ćwiartka: {quarterChar}  (Kratka {e3:D3}/{n3:D3})";
            string utmTxt = $"{zone}{latBand}  {easting:0}m E   {northing:0}m N";
            string wgsTxt = string.Create(CultureInfo.InvariantCulture, $"{lat:0.000000}° N,  {lon:0.000000}° E");

            return (fullMgrs, shortGrid, kpInfo, utmTxt, wgsTxt);
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
            double minE = Math.Min(e1, e2), maxE = Math.Max(e1, e2);
            double minN = Math.Min(n1, n2), maxN = Math.Max(n1, n2);
            return string.Create(CultureInfo.InvariantCulture, $"{minN:0.00},{minE:0.00},{maxN:0.00},{maxE:0.00}");
        }

        private static string GetEpsg2180TileBBox(int tx, int ty, int zoom)
        {
            var (latNorth, lonWest) = TileXYToLatLon(tx, ty, zoom);
            var (latSouth, lonEast) = TileXYToLatLon(tx + 1, ty + 1, zoom);
            var (e1, n1) = LatLonToEpsg2180(latSouth, lonWest);
            var (e2, n2) = LatLonToEpsg2180(latNorth, lonEast);
            double minE = Math.Min(e1, e2), maxE = Math.Max(e1, e2);
            double minN = Math.Min(n1, n2), maxN = Math.Max(n1, n2);
            return string.Create(CultureInfo.InvariantCulture, $"{minE:0.00},{minN:0.00},{maxE:0.00},{maxN:0.00}");
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
            if (layerId == "geoportal_topo")
            {
                int px = (int)Math.Round(bboxSpanMeters / (13500.0 * 0.00028));
                return Math.Clamp(px, 220, 640).ToString();
            }
            if (layerId == "geoportal_lidar")
            {
                int px = (int)Math.Round(bboxSpanMeters / (5500.0 * 0.00028));
                return Math.Clamp(px, 256, 640).ToString();
            }
            int stdPx = zoom <= 13 ? 640 : (zoom == 14 ? 512 : 384);
            return stdPx.ToString();
        }

        private static string FillAllBBoxTokens(string template, string bbox3857, string bbox2180NE, string bbox2180EN, string bbox4326, string wStr, string hStr)
        {
            if (string.IsNullOrEmpty(template)) return null;
            return template
                .Replace("{bbox2180_ne}", bbox2180NE)
                .Replace("{bbox2180_en}", bbox2180EN)
                .Replace("{bbox2180}", bbox2180EN)
                .Replace("{bbox4326}", bbox4326)
                .Replace("{bbox}", bbox3857)
                .Replace("{w}", wStr)
                .Replace("{h}", hStr);
        }

        private static string BuildTileUrl(TacticalMapLayerDef layer, int tx, int ty, int zoom)
        {
            if (layer.IsWmsEpsg3857)
            {
                string b3857 = GetEpsg3857BBox(tx, ty, zoom);
                string b2180NE = GetEpsg2180TileBBoxNE(tx, ty, zoom);
                string b2180EN = GetEpsg2180TileBBox(tx, ty, zoom);
                string b4326 = GetEpsg4326TileBBox(tx, ty, zoom);
                string sz = GetOptimalWmsTilePixelSize(layer.Id, zoom);

                string tpl = (layer.Id == "geoportal_hires" && zoom <= 15)
                    ? layer.FallbackUrlTemplate
                    : layer.UrlTemplate;

                return FillAllBBoxTokens(tpl, b3857, b2180NE, b2180EN, b4326, sz, sz);
            }
            return layer.UrlTemplate.Replace("{z}", zoom.ToString()).Replace("{x}", tx.ToString()).Replace("{y}", ty.ToString());
        }

        private static string BuildFallbackTileUrl(TacticalMapLayerDef layer, int tx, int ty, int zoom)
        {
            if (layer == null || !layer.IsWmsEpsg3857 || string.IsNullOrEmpty(layer.FallbackUrlTemplate))
                return null;

            string b3857 = GetEpsg3857BBox(tx, ty, zoom);
            string b2180NE = GetEpsg2180TileBBoxNE(tx, ty, zoom);
            string b2180EN = GetEpsg2180TileBBox(tx, ty, zoom);
            string b4326 = GetEpsg4326TileBBox(tx, ty, zoom);
            string sz = GetOptimalWmsTilePixelSize(layer.Id, zoom);

            string fbTpl = (layer.Id == "geoportal_hires" && zoom <= 15)
                ? layer.SecondFallbackUrlTemplate
                : layer.FallbackUrlTemplate;

            return FillAllBBoxTokens(fbTpl, b3857, b2180NE, b2180EN, b4326, sz, sz);
        }

        private static (string PrimaryUrl, string Fallback1Url, string Fallback2Url, double NorthLat, double SouthLat, double EastLon, double WestLon) BuildGeoportalSheetUrls(
            TacticalMapLayerDef layer,
            double viewW,
            double viewH,
            double centerLat,
            double centerLon,
            int scaleDenom)
        {
            double overW = viewW * WmsOverscanFactor;
            double overH = viewH * WmsOverscanFactor;

            double tilePx = GetScaledTileSizePx();
            var (cx, cy) = LatLonToTileXY(centerLat, centerLon, _zoom);
            var (nwLat, nwLon) = TileXYToLatLon(cx - (overW / 2.0) / tilePx, cy - (overH / 2.0) / tilePx, _zoom);
            var (seLat, seLon) = TileXYToLatLon(cx + (overW / 2.0) / tilePx, cy + (overH / 2.0) / tilePx, _zoom);

            double northLat = Math.Max(nwLat, seLat);
            double southLat = Math.Min(nwLat, seLat);
            double eastLon = Math.Max(nwLon, seLon);
            double westLon = Math.Min(nwLon, seLon);

            var (minX3857, minY3857) = LatLonToEpsg3857(southLat, westLon);
            var (maxX3857, maxY3857) = LatLonToEpsg3857(northLat, eastLon);

            var (e1, n1) = LatLonToEpsg2180(southLat, westLon);
            var (e2, n2) = LatLonToEpsg2180(northLat, eastLon);
            double minE = Math.Min(e1, e2), maxE = Math.Max(e1, e2);
            double minN = Math.Min(n1, n2), maxN = Math.Max(n1, n2);

            double widthMeters2180 = Math.Max(40.0, maxE - minE);
            double aspect = Math.Max(0.25, overW / Math.Max(1.0, overH));

            double desiredW = Math.Clamp(overW * 1.15, 760.0, 1680.0);

            if (layer.Id == "geoportal_topo")
            {
                double minWForMaxScale = widthMeters2180 / (19000.0 * 0.00028);
                double maxWForMinScale = widthMeters2180 / (6800.0 * 0.00028);
                desiredW = Math.Clamp(desiredW, minWForMaxScale, Math.Max(minWForMaxScale, maxWForMinScale));
            }
            else if (layer.Id == "geoportal_lidar")
            {
                double minWForLidar = widthMeters2180 / (8200.0 * 0.00028);
                desiredW = Math.Max(desiredW, minWForLidar);
            }
            else
            {
                double minWForOrto = widthMeters2180 / (12000.0 * 0.00028);
                desiredW = Math.Max(desiredW, minWForOrto);
            }

            int reqW = Math.Clamp((int)Math.Round(desiredW), 380, 1920);
            int reqH = Math.Clamp((int)Math.Round(reqW / aspect), 380, 1920);
            if (reqH == 1920 && aspect < 1.0)
                reqW = Math.Clamp((int)Math.Round(reqH * aspect), 380, 1920);

            string bbox3857 = string.Create(CultureInfo.InvariantCulture, $"{minX3857:0.00},{minY3857:0.00},{maxX3857:0.00},{maxY3857:0.00}");
            string bbox2180NE = string.Create(CultureInfo.InvariantCulture, $"{minN:0.00},{minE:0.00},{maxN:0.00},{maxE:0.00}");
            string bbox2180EN = string.Create(CultureInfo.InvariantCulture, $"{minE:0.00},{minN:0.00},{maxE:0.00},{maxN:0.00}");
            string bbox4326 = string.Create(CultureInfo.InvariantCulture, $"{westLon:0.000000},{southLat:0.000000},{eastLon:0.000000},{northLat:0.000000}");
            string wStr = reqW.ToString();
            string hStr = reqH.ToString();

            bool skipHighRes = layer.Id == "geoportal_hires" && scaleDenom > 4500;
            string tpl1 = skipHighRes ? layer.FallbackUrlTemplate : layer.UrlTemplate;
            string tpl2 = skipHighRes ? layer.SecondFallbackUrlTemplate : layer.FallbackUrlTemplate;
            string tpl3 = skipHighRes
                ? Layers.First(l => l.Id == "geoportal_std").SecondFallbackUrlTemplate
                : layer.SecondFallbackUrlTemplate;

            string u1 = FillAllBBoxTokens(tpl1, bbox3857, bbox2180NE, bbox2180EN, bbox4326, wStr, hStr);
            string u2 = FillAllBBoxTokens(tpl2, bbox3857, bbox2180NE, bbox2180EN, bbox4326, wStr, hStr);
            string u3 = FillAllBBoxTokens(tpl3, bbox3857, bbox2180NE, bbox2180EN, bbox4326, wStr, hStr);

            return (u1, u2, u3, northLat, southLat, eastLon, westLon);
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

        private static BitmapSource DecodeBytesToFrozenBitmap(byte[] validBytes)
        {
            if (validBytes == null || validBytes.Length < 128) return null;
            try
            {
                var bmp = new BitmapImage();
                using (var mem = new MemoryStream(validBytes))
                {
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = mem;
                    bmp.EndInit();
                    bmp.Freeze();
                }
                return bmp;
            }
            catch { return null; }
        }

        private static async Task<BitmapSource> FetchDecodedImageAsync(string url, string fallbackUrl, CancellationToken ct, string secondFallbackUrl = null, bool checkBlankWhiteForPrimary = false)
        {
            if (string.IsNullOrEmpty(url)) return null;
            if (DecodedImageCache.TryGetValue(url, out var cachedBmp)) return cachedBmp;

            bool isWmsRequest = url.Contains("REQUEST=GetMap", StringComparison.OrdinalIgnoreCase);
            var sem = isWmsRequest ? WmsDedicatedThrottle : TileThrottle;

            await sem.WaitAsync(ct);
            try
            {
                if (DecodedImageCache.TryGetValue(url, out var secondCheck)) return secondCheck;

                string[] candidates = new[] { url, fallbackUrl, secondFallbackUrl }
                    .Where(u => !string.IsNullOrWhiteSpace(u))
                    .Distinct()
                    .ToArray();

                BitmapSource resolvedBmp = null;
                byte[] resolvedBytes = null;

                for (int cIdx = 0; cIdx < candidates.Length && !ct.IsCancellationRequested; cIdx++)
                {
                    string candidateUrl = candidates[cIdx];
                    for (int attempt = 0; attempt < 2 && !ct.IsCancellationRequested; attempt++)
                    {
                        try
                        {
                            byte[] data = await Http.GetByteArrayAsync(candidateUrl, ct);
                            if (IsValidImagePayload(data))
                            {
                                var bmp = DecodeBytesToFrozenBitmap(data);
                                if (bmp != null)
                                {
                                    if (cIdx == 0 && checkBlankWhiteForPrimary && candidates.Length > 1 && IsBlankWhiteWmsPayload(data, bmp))
                                    {
                                        break;
                                    }
                                    resolvedBmp = bmp;
                                    resolvedBytes = data;
                                    break;
                                }
                            }
                        }
                        catch { }
                        if (attempt == 0 && !ct.IsCancellationRequested)
                            await Task.Delay(80, ct);
                    }
                    if (resolvedBmp != null) break;
                }

                if (resolvedBmp != null)
                {
                    if (DecodedImageCache.Count > 700) DecodedImageCache.Clear();
                    if (RawTileCache.Count > 1500) RawTileCache.Clear();
                    DecodedImageCache[url] = resolvedBmp;
                    if (resolvedBytes != null) RawTileCache[url] = resolvedBytes;
                }
                return resolvedBmp;
            }
            finally
            {
                sem.Release();
            }
        }

        private static void RepositionGeoAnchoredSheetsOnCanvas(Canvas targetCanvas, List<GeoAnchoredWmsSheet> sheets, string requiredLayerId, double viewW, double viewH, double opacity)
        {
            if (targetCanvas == null) return;
            targetCanvas.RenderTransform = Transform.Identity;
            targetCanvas.Opacity = opacity;
            targetCanvas.Children.Clear();

            foreach (var sh in sheets)
            {
                if (sh.Bitmap == null || (!string.IsNullOrEmpty(requiredLayerId) && sh.LayerId != requiredLayerId))
                    continue;

                Point nwPt = LatLonToScreen(sh.NorthLat, sh.WestLon, viewW, viewH);
                Point sePt = LatLonToScreen(sh.SouthLat, sh.EastLon, viewW, viewH);

                double left = Math.Min(nwPt.X, sePt.X);
                double top = Math.Min(nwPt.Y, sePt.Y);
                double width = Math.Abs(sePt.X - nwPt.X);
                double height = Math.Abs(sePt.Y - nwPt.Y);

                if (left + width < -viewW || left > viewW * 2 || top + height < -viewH || top > viewH * 2)
                    continue;
                if (width < 20 || height < 20 || width > viewW * 12 || height > viewH * 12)
                    continue;

                var img = new Image
                {
                    Source = sh.Bitmap,
                    Width = width + 0.8,
                    Height = height + 0.8,
                    Stretch = Stretch.Fill
                };
                Canvas.SetLeft(img, left);
                Canvas.SetTop(img, top);
                targetCanvas.Children.Add(img);
            }
        }

        private static bool RenderLayerTilesSmoothly(Canvas targetCanvas, TacticalMapLayerDef layer, double w, double h, double opacity, CancellationToken ct)
        {
            if (targetCanvas == null || layer == null) return false;
            targetCanvas.RenderTransform = Transform.Identity;

            if (_lastRenderedTileZoom != _zoom)
            {
                _lastRenderedTileZoom = _zoom;
                _renderCts?.Cancel();
                _renderCts = new CancellationTokenSource();
                ct = _renderCts.Token;
                PendingTileUrls.Clear();
            }

            double tilePx = GetScaledTileSizePx();
            var (centerTileX, centerTileY) = LatLonToTileXY(_centerLat, _centerLon, _zoom);
            int minTx = (int)Math.Floor(centerTileX - (w / 2.0) / tilePx) - 1;
            int maxTx = (int)Math.Floor(centerTileX + (w / 2.0) / tilePx) + 1;
            int minTy = (int)Math.Floor(centerTileY - (h / 2.0) / tilePx) - 1;
            int maxTy = (int)Math.Floor(centerTileY + (h / 2.0) / tilePx) + 1;
            int maxIndex = (1 << _zoom) - 1;

            targetCanvas.Children.Clear();
            targetCanvas.Opacity = opacity;
            bool anyMissing = false;

            for (int tx = minTx; tx <= maxTx; tx++)
            {
                for (int ty = minTy; ty <= maxTy; ty++)
                {
                    if (ty < 0 || ty > maxIndex) continue;
                    int wrappedTx = ((tx % (maxIndex + 1)) + (maxIndex + 1)) % (maxIndex + 1);
                    double sx = (w / 2.0) + (tx - centerTileX) * tilePx;
                    double sy = (h / 2.0) + (ty - centerTileY) * tilePx;
                    string url = BuildTileUrl(layer, wrappedTx, ty, _zoom);

                    if (DecodedImageCache.TryGetValue(url, out var readyBmp))
                    {
                        var img = new Image { Source = readyBmp, Width = tilePx + 0.9, Height = tilePx + 0.9, Stretch = Stretch.Fill };
                        Canvas.SetLeft(img, sx);
                        Canvas.SetTop(img, sy);
                        targetCanvas.Children.Add(img);
                    }
                    else
                    {
                        anyMissing = true;

                        if (_zoom > 6)
                        {
                            int pZ = _zoom - 1;
                            int pTx = wrappedTx / 2, pTy = ty / 2;
                            string parentUrl = BuildTileUrl(layer, pTx, pTy, pZ);
                            if (DecodedImageCache.TryGetValue(parentUrl, out var parentBmp))
                            {
                                int cw = Math.Max(1, parentBmp.PixelWidth / 2);
                                int ch = Math.Max(1, parentBmp.PixelHeight / 2);
                                var crop = new CroppedBitmap(parentBmp, new Int32Rect((wrappedTx % 2) * cw, (ty % 2) * ch, cw, ch));
                                crop.Freeze();
                                var fallbackImg = new Image { Source = crop, Width = tilePx + 0.9, Height = tilePx + 0.9, Stretch = Stretch.Fill };
                                Canvas.SetLeft(fallbackImg, sx);
                                Canvas.SetTop(fallbackImg, sy);
                                targetCanvas.Children.Add(fallbackImg);
                            }
                        }

                        if (PendingTileUrls.TryAdd(url, 1))
                        {
                            string fbUrl = BuildFallbackTileUrl(layer, wrappedTx, ty, _zoom);
                            var localCt = ct;
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    var downloaded = await FetchDecodedImageAsync(url, fbUrl, localCt);
                                    if (downloaded != null && !localCt.IsCancellationRequested && Application.Current != null)
                                    {
                                        _ = Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                                        {
                                            if (_fullScreenOverlay?.Visibility == Visibility.Visible && !_activeLayer.IsWmsEpsg3857)
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
            return anyMissing;
        }

        private static void RefreshTileCanvasesOnly()
        {
            if (_tileCanvas == null || _activeLayer == null || _fullScreenOverlay?.Visibility != Visibility.Visible) return;
            EnsureTileWatchdogStarted();

            var (w, h) = GetExactCanvasSize();
            var ct = _renderCts?.Token ?? CancellationToken.None;

            if (_activeLayer.IsWmsEpsg3857)
            {
                _hasMissingVisibleTiles = false;
                RepositionGeoAnchoredSheetsOnCanvas(_tileCanvas, _geoAnchoredBaseSheets, _activeLayer.Id, w, h, 1.0);
            }
            else
            {
                _hasMissingVisibleTiles = RenderLayerTilesSmoothly(_tileCanvas, _activeLayer, w, h, 1.0, ct);
            }

            if (_lidarTileCanvas != null)
            {
                if (_showLidarSingleWms && _activeLayer.Id != "geoportal_lidar")
                {
                    RepositionGeoAnchoredSheetsOnCanvas(_lidarTileCanvas, _geoAnchoredLidarSheets, "geoportal_lidar", w, h, _lidarOpacity);
                }
                else
                {
                    _lidarTileCanvas.Children.Clear();
                }
            }

            if (_isPanningMap && (_activeLayer.IsWmsEpsg3857 || _showLidarSingleWms))
            {
                if ((DateTime.UtcNow - _lastWmsLivePanTriggerUtc).TotalMilliseconds >= 520 && !_isWmsSheetFetchRunning)
                {
                    _lastWmsLivePanTriggerUtc = DateTime.UtcNow;
                    _ = RenderMapTilesAsync();
                }
            }
        }

        private static void StoreGeoAnchoredSheet(List<GeoAnchoredWmsSheet> list, GeoAnchoredWmsSheet newSheet)
        {
            if (newSheet?.Bitmap == null) return;
            list.RemoveAll(s => s.LayerId != newSheet.LayerId);
            list.RemoveAll(s =>
                s.ScaleDenominator == newSheet.ScaleDenominator &&
                Math.Abs(s.CenterLat - newSheet.CenterLat) < 0.0001 &&
                Math.Abs(s.CenterLon - newSheet.CenterLon) < 0.0001);

            list.Add(newSheet);
            while (list.Count > 6)
                list.RemoveAt(0);
        }

        private static async Task RenderMapTilesAsync(bool transparentGrgOnly = false)
        {
            if (_tileCanvas == null || _interactiveCanvas == null || _activeLayer == null || _fullScreenOverlay?.Visibility != Visibility.Visible) return;

            if (_renderCts == null || _renderCts.IsCancellationRequested)
                _renderCts = new CancellationTokenSource();

            EnsureTileWatchdogStarted();
            UpdateSheetViewportSize();
            var (w, h) = GetExactCanvasSize();
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

            if (_activeLayer.IsWmsEpsg3857 || _showLidarSingleWms)
            {
                _wmsSheetCts?.Cancel();
                var wmsCts = new CancellationTokenSource();
                _wmsSheetCts = wmsCts;
                var ct = wmsCts.Token;

                double reqLat = _centerLat, reqLon = _centerLon;
                int reqScale = _targetScaleDenominator;
                var reqLayer = _activeLayer;
                bool reqLidar = _showLidarSingleWms && reqLayer.Id != "geoportal_lidar";

                try
                {
                    await Task.Delay(130, ct);
                    if (ct.IsCancellationRequested) return;

                    _isWmsSheetFetchRunning = true;

                    if (_statusText != null && (reqLayer.IsWmsEpsg3857 || reqLidar))
                    {
                        string lidarNote = reqLidar ? " + LiDAR ISOK (EPSG:2180 PNG)" : "";
                        _statusText.Text = $"⏳ Pobieranie podkładu Geoportal WMS ({reqLayer.Name.Split('(')[0].Trim()}{lidarNote} | Skala 1:{reqScale:N0})...";
                    }

                    Task<(BitmapSource Bmp, double N, double S, double E, double W)> baseTask = Task.FromResult<(BitmapSource, double, double, double, double)>((null, 0, 0, 0, 0));
                    if (reqLayer.IsWmsEpsg3857)
                    {
                        var bInfo = BuildGeoportalSheetUrls(reqLayer, w, h, reqLat, reqLon, reqScale);
                        bool checkWhite = reqLayer.Id == "geoportal_hires" && reqScale <= 4500;
                        baseTask = Task.Run(async () =>
                        {
                            var bmp = await FetchDecodedImageAsync(bInfo.PrimaryUrl, bInfo.Fallback1Url, ct, bInfo.Fallback2Url, checkWhite);
                            return (bmp, bInfo.NorthLat, bInfo.SouthLat, bInfo.EastLon, bInfo.WestLon);
                        }, ct);
                    }

                    Task<(BitmapSource Bmp, double N, double S, double E, double W)> lidarTask = Task.FromResult<(BitmapSource, double, double, double, double)>((null, 0, 0, 0, 0));
                    if (reqLidar)
                    {
                        var lidarLayerDef = Layers.First(l => l.Id == "geoportal_lidar");
                        var lInfo = BuildGeoportalSheetUrls(lidarLayerDef, w, h, reqLat, reqLon, reqScale);
                        lidarTask = Task.Run(async () =>
                        {
                            var bmp = await FetchDecodedImageAsync(lInfo.PrimaryUrl, lInfo.Fallback1Url, ct, lInfo.Fallback2Url, false);
                            return (bmp, lInfo.NorthLat, lInfo.SouthLat, lInfo.EastLon, lInfo.WestLon);
                        }, ct);
                    }

                    await Task.WhenAll(baseTask, lidarTask);
                    if (ct.IsCancellationRequested) return;

                    var (curW, curH) = GetExactCanvasSize();

                    if (reqLayer.IsWmsEpsg3857 && baseTask.Result.Bmp != null)
                    {
                        StoreGeoAnchoredSheet(_geoAnchoredBaseSheets, new GeoAnchoredWmsSheet
                        {
                            LayerId = reqLayer.Id,
                            NorthLat = baseTask.Result.N,
                            SouthLat = baseTask.Result.S,
                            EastLon = baseTask.Result.E,
                            WestLon = baseTask.Result.W,
                            CenterLat = reqLat,
                            CenterLon = reqLon,
                            ScaleDenominator = reqScale,
                            Bitmap = baseTask.Result.Bmp
                        });

                        _wmsLoadedCenterLat = reqLat;
                        _wmsLoadedCenterLon = reqLon;
                        _wmsLoadedScaleDenominator = reqScale;
                        _wmsLoadedLayerId = reqLayer.Id;

                        RepositionGeoAnchoredSheetsOnCanvas(_tileCanvas, _geoAnchoredBaseSheets, reqLayer.Id, curW, curH, 1.0);
                    }

                    if (reqLidar && lidarTask.Result.Bmp != null)
                    {
                        StoreGeoAnchoredSheet(_geoAnchoredLidarSheets, new GeoAnchoredWmsSheet
                        {
                            LayerId = "geoportal_lidar",
                            NorthLat = lidarTask.Result.N,
                            SouthLat = lidarTask.Result.S,
                            EastLon = lidarTask.Result.E,
                            WestLon = lidarTask.Result.W,
                            CenterLat = reqLat,
                            CenterLon = reqLon,
                            ScaleDenominator = reqScale,
                            Bitmap = lidarTask.Result.Bmp
                        });

                        if (!reqLayer.IsWmsEpsg3857)
                        {
                            _wmsLoadedCenterLat = reqLat;
                            _wmsLoadedCenterLon = reqLon;
                            _wmsLoadedScaleDenominator = reqScale;
                        }
                        _wmsLoadedLidarFlag = true;

                        RepositionGeoAnchoredSheetsOnCanvas(_lidarTileCanvas, _geoAnchoredLidarSheets, "geoportal_lidar", curW, curH, _lidarOpacity);
                    }
                    else if (!reqLidar)
                    {
                        _wmsLoadedLidarFlag = false;
                        _lidarTileCanvas?.Children.Clear();
                    }

                    if (_statusText != null)
                    {
                        string lidarOk = (reqLidar && lidarTask.Result.Bmp != null) ? " + LiDAR ISOK (EPSG:2180 PNG)" : "";
                        _statusText.Text = $"✓ Wczytano podkład: {reqLayer.Name.Split('(')[0].Trim()}{lidarOk} (Skala 1:{reqScale:N0}).";
                    }
                }
                catch { }
                finally
                {
                    if (_wmsSheetCts == wmsCts)
                        _isWmsSheetFetchRunning = false;
                }
            }
            else if (_lidarTileCanvas != null)
            {
                _wmsLoadedLidarFlag = false;
                _lidarTileCanvas.Children.Clear();
            }
        }

        private static FormattedText GetCachedGlyph(string text, double fontSize, Brush fg, string keyPrefix)
        {
            string key = $"{keyPrefix}_{text}_{fontSize:0.0}";
            if (GlyphCache.TryGetValue(key, out var ft)) return ft;
            if (GlyphCache.Count > 600) GlyphCache.Clear();
            var created = new FormattedText(text ?? "", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, BoldSegoeTypeface, fontSize, fg, 1.0);
            GlyphCache[key] = created;
            return created;
        }

        private static Brush CreateAlphaHexBrush(string hex, double opacity, Color fallback)
        {
            Color c = fallback;
            try { if (!string.IsNullOrWhiteSpace(hex)) c = (Color)ColorConverter.ConvertFromString(hex.Trim()); } catch { }
            byte a = (byte)Math.Clamp((int)Math.Round(opacity * 255), 0, 255);
            var b = new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));
            b.Freeze();
            return b;
        }

        private static void DrawFastBadgeOnContext(DrawingContext dc, string text, double x, double y, Brush fg, double fontSize, double opacity, string cachePrefix)
        {
            if (string.IsNullOrEmpty(text) || opacity <= 0.02) return;
            var ft = GetCachedGlyph(text, fontSize, fg, cachePrefix);
            if (_labelBgStyle != "Czysty tekst")
            {
                byte bgAlpha = (byte)Math.Clamp((int)Math.Round((_labelBgStyle.StartsWith("Kaseton") ? 205 : 110) * opacity), 0, 240);
                var bgBrush = new SolidColorBrush(Color.FromArgb(bgAlpha, 5, 12, 15)); bgBrush.Freeze();
                Pen borderPen = null;
                if (_labelBgStyle.StartsWith("Kaseton"))
                {
                    borderPen = new Pen(fg, 0.85); borderPen.Freeze();
                }
                dc.DrawRoundedRectangle(bgBrush, borderPen, new Rect(x - 3.5, y - 1, ft.Width + 7, ft.Height + 2), 2.5, 2.5);
            }
            dc.DrawText(ft, new Point(x, y));
        }

        private static void DrawCartographicLineLabelOnContext(DrawingContext dc, string text, double lineCoord, bool isVerticalLine, double canvasW, double canvasH, Brush fg, double fontSize, double opacity, string cachePrefix)
        {
            if (string.IsNullOrEmpty(text) || opacity <= 0.02) return;
            var ft = GetCachedGlyph(text, fontSize, fg, cachePrefix);

            double drawX, drawY;
            if (isVerticalLine)
            {
                drawX = lineCoord - ft.Width / 2.0;
                drawY = 6.0;
                if (drawX < 2.0 || drawX + ft.Width > canvasW - 2.0) return;
            }
            else
            {
                drawX = 6.0;
                drawY = lineCoord - ft.Height / 2.0;
                if (drawY < 2.0 || drawY + ft.Height > canvasH - 2.0) return;
            }

            byte bgAlpha = (byte)Math.Clamp((int)Math.Round((_labelBgStyle.StartsWith("Kaseton") ? 225 : 165) * opacity), 0, 245);
            var bgBrush = new SolidColorBrush(Color.FromArgb(bgAlpha, 6, 14, 18)); bgBrush.Freeze();
            Pen borderPen = null;
            if (_labelBgStyle.StartsWith("Kaseton"))
            {
                borderPen = new Pen(fg, 0.9); borderPen.Freeze();
            }
            dc.DrawRoundedRectangle(bgBrush, borderPen, new Rect(drawX - 4.0, drawY - 1.0, ft.Width + 8.0, ft.Height + 2.0), 3.0, 3.0);
            dc.DrawText(ft, new Point(drawX, drawY));
        }

        private static void DrawFastSubCellBadgeOnContext(DrawingContext dc, string text, double centerX, double centerY, Brush textBrush, Brush bgBrush, Pen boxBorderPen, Brush shadowBrush, double fontSize, string cachePrefix)
        {
            if (string.IsNullOrEmpty(text)) return;
            var ft = GetCachedGlyph(text, fontSize, textBrush, cachePrefix);
            double tx = centerX - ft.Width / 2.0;
            double ty = centerY - ft.Height / 2.0;

            if (_labelBgStyle != "Czysty tekst" && _subLabelBgOpacity > 0.02)
            {
                double padX = 4.0, padY = 1.5;
                dc.DrawRoundedRectangle(bgBrush, boxBorderPen, new Rect(tx - padX, ty - padY, ft.Width + padX * 2, ft.Height + padY * 2), 3.0, 3.0);
            }

            var shadowFt = GetCachedGlyph(text, fontSize, shadowBrush, cachePrefix + "_SH");
            dc.DrawText(shadowFt, new Point(tx + 0.9, ty + 0.9));
            dc.DrawText(ft, new Point(tx, ty));
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

                double mult = ao.SizeMultiplier > 0.1 ? ao.SizeMultiplier : 1.0;
                var borderPen = new Pen(strokeBrush, 2.8 * mult) { DashStyle = new DashStyle(new double[] { 5, 2.5 }, 0) };
                borderPen.Freeze();
                dc.DrawGeometry(null, borderPen, aoPolyGeom);

                if (_showAllObjectLabels && ao.ShowLabel && !string.IsNullOrWhiteSpace(ao.Name))
                    DrawFastBadgeOnContext(dc, $"🛡️ {ao.Name}", screenPts[0].X + 10, screenPts[0].Y + 6, strokeBrush, 10 * _fontScaleMultiplier * mult, 1.0, $"AO_{ao.ColorHex}");
            }

            foreach (var sec in _mapObjects.Where(o => o.Kind == "Sector" && o.Points.Count >= 2))
            {
                Color c = Color.FromRgb(245, 184, 46);
                try { c = (Color)ColorConverter.ConvertFromString(sec.ColorHex); } catch { }
                var strokeBrush = TacticalConfigPanel.CreateFrozenBrush(c.R, c.G, c.B);
                var fillBrush = new SolidColorBrush(Color.FromArgb(85, c.R, c.G, c.B)); fillBrush.Freeze();
                var screenPts = sec.Points.Select(pt => LatLonToScreen(pt.Lat, pt.Lon, w, h)).ToList();

                double mult = sec.SizeMultiplier > 0.1 ? sec.SizeMultiplier : 1.0;
                var fig = new PathFigure { StartPoint = screenPts[0], IsClosed = true, IsFilled = true };
                for (int i = 1; i < screenPts.Count; i++) fig.Segments.Add(new LineSegment(screenPts[i], true));
                var polyGeom = new PathGeometry(new[] { fig });
                var secPen = new Pen(strokeBrush, 2.2 * mult); secPen.Freeze();
                dc.DrawGeometry(fillBrush, secPen, polyGeom);

                if (_showAllObjectLabels && sec.ShowLabel && !string.IsNullOrWhiteSpace(sec.Name))
                {
                    double cx = screenPts.Average(p => p.X), cy = screenPts.Average(p => p.Y);
                    DrawFastBadgeOnContext(dc, sec.Name, cx + 14, cy - 8, strokeBrush, 10 * _fontScaleMultiplier * mult, 1.0, $"SEC_{sec.ColorHex}");
                }
            }

            foreach (var rt in _mapObjects.Where(o => o.Kind == "Route" && o.Points.Count >= 2))
            {
                double mult = rt.SizeMultiplier > 0.1 ? rt.SizeMultiplier : 1.0;
                var strokeBrush = TacticalConfigPanel.BrushFromHex(rt.ColorHex, Color.FromRgb(38, 132, 255));
                var screenPts = rt.Points.Select(pt => LatLonToScreen(pt.Lat, pt.Lon, w, h)).ToList();
                var rtPen = new Pen(strokeBrush, 2.8 * mult) { DashStyle = new DashStyle(new double[] { 4, 2 }, 0) };
                rtPen.Freeze();
                for (int i = 0; i < screenPts.Count - 1; i++)
                    dc.DrawLine(rtPen, screenPts[i], screenPts[i + 1]);

                if (_showAllObjectLabels && rt.ShowLabel && !string.IsNullOrWhiteSpace(rt.Name))
                    DrawFastBadgeOnContext(dc, rt.Name, screenPts[0].X + 8, screenPts[0].Y - 14, strokeBrush, 9.5 * _fontScaleMultiplier * mult, 1.0, $"RT_{rt.ColorHex}");
            }

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

            if (_coordSystem != "Brak siatki")
            {
                var mainBrush = CreateAlphaHexBrush(_mainGridColorHex, _mainGridOpacity, Color.FromRgb(250, 208, 44));
                var subLineBrush = CreateAlphaHexBrush(_subGridLineColorHex, _subGridOpacity, Color.FromRgb(31, 224, 197));
                var subTextBrush = CreateAlphaHexBrush(_subLabelTextColorHex, _subGridOpacity, Colors.White);
                var subBgBrush = CreateAlphaHexBrush(_subLabelBgColorHex, _subLabelBgOpacity * _subGridOpacity, Color.FromRgb(6, 16, 20));
                var subShadowBrush = CreateAlphaHexBrush("#000000", _subGridOpacity * 0.9, Colors.Black);

                Pen subBoxBorderPen = null;
                if (_labelBgStyle.StartsWith("Kaseton") && _subLabelBgOpacity > 0.05)
                {
                    subBoxBorderPen = new Pen(CreateAlphaHexBrush("#000000", _subGridOpacity * 0.85, Colors.Black), 0.9);
                    subBoxBorderPen.Freeze();
                }

                byte mainHaloAlpha = (byte)Math.Clamp((int)Math.Round((_gridHaloMode.StartsWith("Pełny") ? 220 : 130) * _mainGridOpacity), 0, 255);
                byte subHaloAlpha = (byte)Math.Clamp((int)Math.Round((_gridHaloMode.StartsWith("Pełny") ? 180 : 100) * _subGridOpacity), 0, 255);
                var haloBrush = new SolidColorBrush(Color.FromArgb(mainHaloAlpha, 0, 0, 0)); haloBrush.Freeze();
                var subHaloBrush = new SolidColorBrush(Color.FromArgb(subHaloAlpha, 0, 0, 0)); subHaloBrush.Freeze();
                bool useHalo = !_gridHaloMode.StartsWith("Brak");

                double mainThick = Math.Max(0.8, 1.45 * _gridThicknessMultiplier);
                double subThick = Math.Max(0.7, 0.95 * _gridThicknessMultiplier);
                var mainPen = new Pen(mainBrush, mainThick); mainPen.Freeze();
                var mainHaloPen = new Pen(haloBrush, mainThick + 1.8); mainHaloPen.Freeze();
                var subPen = new Pen(subLineBrush, subThick) { DashStyle = new DashStyle(new double[] { 4, 2 }, 0) }; subPen.Freeze();
                var subHaloPen = new Pen(subHaloBrush, subThick + 1.2); subHaloPen.Freeze();

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
                    var (nwLat, nwLon) = ScreenToLatLon(0, 0, w, h);
                    var (seLat, seLon) = ScreenToLatLon(w, h, w, h);
                    double lonSpan = Math.Max(0.0001, Math.Abs(seLon - nwLon));
                    double[] degSteps = { 0.0005, 0.001, 0.002, 0.005, 0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1.0 };
                    double dDeg = 0.005;
                    foreach (var ds in degSteps) { if (lonSpan / ds <= 10.0) { dDeg = ds; break; } }

                    if (_subGridOpacity > 0.02 && !_subGridDivision.StartsWith("Brak"))
                    {
                        int subDiv = _subGridDivision.Contains("50 m") ? 2 : 3;
                        double subDeg = dDeg / subDiv;
                        double firstSubLon = Math.Floor(Math.Min(nwLon, seLon) / dDeg) * dDeg;
                        for (double lon = firstSubLon; lon <= Math.Max(nwLon, seLon) + dDeg; lon += dDeg)
                        {
                            for (int i = 1; i < subDiv; i++)
                            {
                                double sx = LatLonToScreen(_centerLat, lon + i * subDeg, w, h).X;
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
                                double sy = LatLonToScreen(lat + j * subDeg, _centerLon, w, h).Y;
                                if (sy >= 0 && sy <= h)
                                {
                                    if (useHalo) dc.DrawLine(subHaloPen, new Point(0, sy), new Point(w, sy));
                                    dc.DrawLine(subPen, new Point(0, sy), new Point(w, sy));
                                }
                            }
                        }
                    }

                    if (_mainGridOpacity > 0.02)
                    {
                        string mainCacheKey = $"WGS_{_mainGridColorHex}_{_mainGridOpacity:0.00}";
                        double mainFontSz = 9.5 * _fontScaleMultiplier;
                        double startLon = Math.Floor(Math.Min(nwLon, seLon) / dDeg) * dDeg;
                        for (double lon = startLon; lon <= Math.Max(nwLon, seLon) + dDeg; lon += dDeg)
                        {
                            double sx = LatLonToScreen(_centerLat, lon, w, h).X;
                            if (sx < 0 || sx > w) continue;
                            if (useHalo) dc.DrawLine(mainHaloPen, new Point(sx, 0), new Point(sx, h));
                            dc.DrawLine(mainPen, new Point(sx, 0), new Point(sx, h));
                            DrawCartographicLineLabelOnContext(dc, $"{lon:0.0000}°E", sx, true, w, h, mainBrush, mainFontSz, _mainGridOpacity, mainCacheKey);
                        }

                        double startLat = Math.Floor(Math.Min(seLat, nwLat) / dDeg) * dDeg;
                        for (double lat = startLat; lat <= Math.Max(seLat, nwLat) + dDeg; lat += dDeg)
                        {
                            double sy = LatLonToScreen(lat, _centerLon, w, h).Y;
                            if (sy < 0 || sy > h) continue;
                            if (useHalo) dc.DrawLine(mainHaloPen, new Point(0, sy), new Point(w, sy));
                            dc.DrawLine(mainPen, new Point(0, sy), new Point(w, sy));
                            DrawCartographicLineLabelOnContext(dc, $"{lat:0.0000}°N", sy, false, w, h, mainBrush, mainFontSz, _mainGridOpacity, mainCacheKey);
                        }
                    }
                }
                else
                {
                    double metersPerPixel = GetExactMetersPerPixel();
                    int baseStepMeters = _subGridDivision.Contains("250") ? 250 : (_subGridDivision.Contains("500") ? 500 : 100);

                    int effectiveStepMeters = baseStepMeters;
                    while (effectiveStepMeters / metersPerPixel < 32.0 && effectiveStepMeters < 10000)
                        effectiveStepMeters *= 2;
                    _effectiveMgrsStepMeters = effectiveStepMeters;

                    int utmZone = (int)Math.Floor((_centerLon + 180.0) / 6.0) + 1;
                    const double refEasting = 360000.0;
                    const double refNorthing = 5530000.0;

                    var (nwLat, nwLon) = ScreenToLatLon(0, 0, w, h);
                    var (seLat, seLon) = ScreenToLatLon(w, h, w, h);
                    LatLonToUtmMeters(nwLat, nwLon, utmZone, out double westEasting, out double northNorthing);
                    LatLonToUtmMeters(seLat, seLon, utmZone, out double eastEasting, out double southNorthing);

                    double firstEasting = Math.Floor((Math.Min(westEasting, eastEasting) - effectiveStepMeters * 2) / effectiveStepMeters) * effectiveStepMeters;
                    double lastEasting = Math.Ceiling((Math.Max(westEasting, eastEasting) + effectiveStepMeters * 2) / effectiveStepMeters) * effectiveStepMeters;
                    double firstNorthing = Math.Floor((Math.Min(southNorthing, northNorthing) - effectiveStepMeters * 2) / effectiveStepMeters) * effectiveStepMeters;
                    double lastNorthing = Math.Ceiling((Math.Max(southNorthing, northNorthing) + effectiveStepMeters * 2) / effectiveStepMeters) * effectiveStepMeters;

                    double EastingToScreenX(double eastVal)
                    {
                        UtmMetersToLatLon(eastVal, refNorthing, utmZone, out _, out double lineLon);
                        return LatLonToScreen(_centerLat, lineLon, w, h).X;
                    }

                    double NorthingToScreenY(double northVal)
                    {
                        UtmMetersToLatLon(refEasting, northVal, utmZone, out double lineLat, out _);
                        return LatLonToScreen(lineLat, _centerLon, w, h).Y;
                    }

                    double sampleStepPx = Math.Abs(EastingToScreenX(firstEasting + effectiveStepMeters) - EastingToScreenX(firstEasting));

                    if ((_subGridDivision.Contains("50 m") || _subGridDivision.Contains("33 m")) && _subGridOpacity > 0.02 && sampleStepPx >= 42.0)
                    {
                        bool isQuarters = _subGridDivision.Contains("50 m");
                        int div = isQuarters ? 2 : 3;
                        double subStepMeters = (double)effectiveStepMeters / div;

                        for (double east = firstEasting; east <= lastEasting; east += effectiveStepMeters)
                        {
                            for (int i = 1; i < div; i++)
                            {
                                double sx = EastingToScreenX(east + i * subStepMeters);
                                if (sx < 0 || sx > w) continue;
                                if (useHalo) dc.DrawLine(subHaloPen, new Point(sx, 0), new Point(sx, h));
                                dc.DrawLine(subPen, new Point(sx, 0), new Point(sx, h));
                            }
                        }

                        for (double north = firstNorthing; north <= lastNorthing; north += effectiveStepMeters)
                        {
                            for (int j = 1; j < div; j++)
                            {
                                double sy = NorthingToScreenY(north + j * subStepMeters);
                                if (sy < 0 || sy > h) continue;
                                if (useHalo) dc.DrawLine(subHaloPen, new Point(0, sy), new Point(w, sy));
                                dc.DrawLine(subPen, new Point(0, sy), new Point(w, sy));
                            }
                        }

                        if (_gridLabelsMode.Contains("Ćwiartki/Keypad") && sampleStepPx >= 58.0)
                        {
                            string[,] keypad3x3 = { { "7", "8", "9" }, { "4", "5", "6" }, { "1", "2", "3" } };
                            string[,] quarters2x2 = { { "A", "B" }, { "C", "D" } };
                            string subCacheKey = $"SUB_{_subLabelTextColorHex}_{_subGridOpacity:0.00}";
                            double subFontSz = 8.8 * _fontScaleMultiplier;

                            for (double east = firstEasting; east <= lastEasting; east += effectiveStepMeters)
                            {
                                double cellLeftX = EastingToScreenX(east);
                                double cellRightX = EastingToScreenX(east + effectiveStepMeters);
                                if (cellRightX < 0 || cellLeftX > w) continue;
                                double cellW = cellRightX - cellLeftX;

                                for (double north = firstNorthing; north <= lastNorthing; north += effectiveStepMeters)
                                {
                                    double cellTopY = NorthingToScreenY(north + effectiveStepMeters);
                                    double cellBotY = NorthingToScreenY(north);
                                    if (cellBotY < 0 || cellTopY > h) continue;
                                    double cellH = cellBotY - cellTopY;

                                    for (int r = 0; r < div; r++)
                                    {
                                        for (int c = 0; c < div; c++)
                                        {
                                            double cx = cellLeftX + (c + 0.5) * (cellW / div);
                                            double cy = cellTopY + (r + 0.5) * (cellH / div);
                                            if (cx > 14 && cx < w - 14 && cy > 14 && cy < h - 14)
                                            {
                                                string subTxt = isQuarters ? quarters2x2[r, c] : keypad3x3[r, c];
                                                DrawFastSubCellBadgeOnContext(dc, subTxt, cx, cy, subTextBrush, subBgBrush, subBoxBorderPen, subShadowBrush, subFontSz, subCacheKey);
                                            }
                                        }
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
                            double x = EastingToScreenX(east);
                            if (x < 0 || x > w) continue;
                            if (useHalo) dc.DrawLine(mainHaloPen, new Point(x, 0), new Point(x, h));
                            dc.DrawLine(mainPen, new Point(x, 0), new Point(x, h));

                            int mgrsHundredMeters = ((int)Math.Round(east / 100.0)) % 1000;
                            if (mgrsHundredMeters < 0) mgrsHundredMeters += 1000;
                            DrawCartographicLineLabelOnContext(dc, $"{mgrsHundredMeters:D3}", x, true, w, h, mainBrush, mainFontSz, _mainGridOpacity, mainCacheKey);
                        }

                        for (double north = firstNorthing; north <= lastNorthing; north += effectiveStepMeters)
                        {
                            double y = NorthingToScreenY(north);
                            if (y < 0 || y > h) continue;
                            if (useHalo) dc.DrawLine(mainHaloPen, new Point(0, y), new Point(w, y));
                            dc.DrawLine(mainPen, new Point(0, y), new Point(w, y));

                            int mgrsHundredMeters = ((int)Math.Round(north / 100.0)) % 1000;
                            if (mgrsHundredMeters < 0) mgrsHundredMeters += 1000;
                            DrawCartographicLineLabelOnContext(dc, $"{mgrsHundredMeters:D3}", y, false, w, h, mainBrush, mainFontSz, _mainGridOpacity, mainCacheKey);
                        }
                    }
                }
            }

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
                ["subLabelBgOpacity"] = _subLabelBgOpacity,
                ["mapDimmerOpacity"] = _mapDimmerOpacity,
                ["mainGridColorHex"] = _mainGridColorHex,
                ["subGridLineColorHex"] = _subGridLineColorHex,
                ["subLabelTextColorHex"] = _subLabelTextColorHex,
                ["subLabelBgColorHex"] = _subLabelBgColorHex,
                ["gridHaloMode"] = _gridHaloMode,
                ["aoOutPattern"] = _aoOutPattern,
                ["aoOutMaskOpacity"] = _aoOutMaskOpacity,
                ["showAllObjectLabels"] = _showAllObjectLabels,
                ["showRotationHandlesOnMap"] = _showRotationHandlesOnMap,
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
                    ["sizeMultiplier"] = o.SizeMultiplier,
                    ["rotationDeg"] = o.RotationDeg,
                    ["points"] = pts
                });
            }
            root["objects"] = arr;
            ExportExtraMapUiState(root);
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
                if (double.TryParse(root["subLabelBgOpacity"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double slbo)) _subLabelBgOpacity = slbo;
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
                        double szMult = double.TryParse(node["sizeMultiplier"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double sm) ? Math.Clamp(sm, 0.35, 3.0) : 1.0;
                        double rotDeg = double.TryParse(node["rotationDeg"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double rd) ? ((rd % 360.0 + 360.0) % 360.0) : 0.0;

                        var item = new TacticalMapObjectItem
                        {
                            Kind = node["kind"]?.ToString() ?? "Marker",
                            SymbolCode = node["symbol"]?.ToString() ?? "NATO_HQ",
                            Name = node["name"]?.ToString() ?? "Punkt",
                            ColorHex = node["color"]?.ToString() ?? "#2684FF",
                            ShowLabel = shLbl,
                            SizeMultiplier = szMult,
                            RotationDeg = rotDeg
                        };
                        if (node["points"] is JsonArray pts)
                            foreach (var p in pts.OfType<JsonObject>())
                                if (double.TryParse(p["lat"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double pla) &&
                                    double.TryParse(p["lon"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double plo))
                                    item.Points.Add((pla, plo));
                        if (item.Points.Count > 0) _mapObjects.Add(item);
                    }
                }

                GlyphCache.Clear();
                ImportExtraMapUiState(root);
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
