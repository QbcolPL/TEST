using System;
using System.IO;
using System.Text.Json;

namespace FieldTakHub.Builder.Services.MapPrint;

/// <summary>
/// Ustawienia wydruku kartograficznego z ramką (arkusz w stylu map taktycznych 1:25 000).
/// Zapisywane w %LOCALAPPDATA%\FieldTakHub\map-print-settings.json — wspólne dla wszystkich projektów.
/// </summary>
internal sealed class MapPrintSettings
{
    public static readonly string[] PaperSizes = { "A4", "A3", "A5", "Letter", "Własny" };
    public static readonly string[] GridLabelStyles = { "Dwie cyfry km (35)", "Pełne metry (535 000)", "Bez opisów" };
    public static readonly string[] QrModes = { "geo: (współrzędne środka)", "Google Maps", "OpenStreetMap", "MGRS środka (tekst)", "Własny tekst", "Brak" };
    public static readonly string[] LogoModes = { "Logo Field TAK Hub", "Własny plik", "Brak" };
    public static readonly string[] TitleModes = { "Nazwa arkusza MGRS", "Własny tytuł" };
    public static readonly int[] GridIntervals = { 0, 100, 200, 500, 1000, 2000, 5000, 10000 };
    public static readonly int[] DpiOptions = { 150, 200, 300 };

    // ---- strona
    public string PaperSize { get; set; } = "A4";
    public double CustomPaperWidthMm { get; set; } = 210;
    public double CustomPaperHeightMm { get; set; } = 297;
    public bool Landscape { get; set; }
    public double MarginLeftMm { get; set; } = 8;
    public double MarginRightMm { get; set; } = 7;
    public double MarginTopMm { get; set; } = 7;
    public double MarginBottomMm { get; set; } = 6;
    public bool ShowFooter { get; set; } = true;
    public double FooterHeightMm { get; set; } = 27;
    public int Dpi { get; set; } = 300;
    public int JpegQuality { get; set; } = 90;
    public bool PolishLabels { get; set; } = true;

    // ---- ramka i siatka kartograficzna
    public bool DrawNeatLine { get; set; } = true;
    public bool DrawGrid { get; set; } = true;
    public int GridIntervalMeters { get; set; }
    public bool GridSubdivisionTicks { get; set; } = true;
    public bool EdgeTicks { get; set; } = true;
    public string GridLabelStyle { get; set; } = GridLabelStyles[0];
    public bool GridLabelsInside { get; set; } = true;
    public bool GridLabelsOutside { get; set; }
    public string GridColorHex { get; set; } = "#000000";
    public double GridLineWidthMm { get; set; } = 0.2;
    public double GridLabelSizeMm { get; set; } = 3.2;

    // ---- stopka
    public string LogoMode { get; set; } = LogoModes[0];
    public string LogoFilePath { get; set; } = "";
    public string TitleMode { get; set; } = TitleModes[0];
    public string CustomTitle { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string UrlLine { get; set; } = "";
    public bool ShowGeneratedLine { get; set; } = true;
    public bool ShowInfoScale { get; set; } = true;
    public bool ShowInfoCrs { get; set; } = true;
    public bool ShowInfoSouthWest { get; set; } = true;
    public string Custom1Label { get; set; } = "PODKŁAD";
    public string Custom1Value { get; set; } = "";
    public string Custom2Label { get; set; } = "";
    public string Custom2Value { get; set; } = "";
    public bool ShowKmBar { get; set; } = true;
    public bool ShowMileBar { get; set; }
    public bool ShowScaleCheckNote { get; set; } = true;
    public bool ShowDeclination { get; set; } = true;
    public string QrMode { get; set; } = QrModes[0];
    public string QrCustomText { get; set; } = "";
    public bool ShowLeftMarginText { get; set; } = true;
    public string LeftMarginText { get; set; } = "";
    public bool ShowRightMarginText { get; set; } = true;
    public string RightMarginText { get; set; } = "";

    // ---- treść z ekranu edytora
    public bool IncludeMapObjects { get; set; } = true;
    public bool IncludeScreenGrids { get; set; }
    public bool IncludeScreenNorthArrow { get; set; }
    public bool IncludeScreenScaleBar { get; set; }
    public bool IncludeScreenLegend { get; set; } = true;

    // ---- seria arkuszy
    public int SeriesColumns { get; set; } = 1;
    public int SeriesRows { get; set; } = 1;
    public double SeriesOverlapPercent { get; set; } = 5;

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FieldTakHub", "map-print-settings.json");

    public MapPrintSettings Clone() => (MapPrintSettings)MemberwiseClone();

    /// <summary>Przycina wartości do bezpiecznych zakresów (np. po ręcznej edycji pliku JSON).</summary>
    public MapPrintSettings Normalize()
    {
        if (Array.IndexOf(PaperSizes, PaperSize) < 0) PaperSize = "A4";
        CustomPaperWidthMm = Clamp(CustomPaperWidthMm, 80, 1200, 210);
        CustomPaperHeightMm = Clamp(CustomPaperHeightMm, 80, 1200, 297);
        MarginLeftMm = Clamp(MarginLeftMm, 0, 40, 8);
        MarginRightMm = Clamp(MarginRightMm, 0, 40, 7);
        MarginTopMm = Clamp(MarginTopMm, 0, 40, 7);
        MarginBottomMm = Clamp(MarginBottomMm, 0, 40, 6);
        FooterHeightMm = Clamp(FooterHeightMm, 16, 60, 27);
        if (Array.IndexOf(DpiOptions, Dpi) < 0) Dpi = 300;
        JpegQuality = Math.Clamp(JpegQuality, 60, 100);
        if (Array.IndexOf(GridIntervals, GridIntervalMeters) < 0) GridIntervalMeters = 0;
        if (Array.IndexOf(GridLabelStyles, GridLabelStyle) < 0) GridLabelStyle = GridLabelStyles[0];
        if (Array.IndexOf(QrModes, QrMode) < 0) QrMode = QrModes[0];
        if (Array.IndexOf(LogoModes, LogoMode) < 0) LogoMode = LogoModes[0];
        if (Array.IndexOf(TitleModes, TitleMode) < 0) TitleMode = TitleModes[0];
        GridLineWidthMm = Clamp(GridLineWidthMm, 0.05, 1.0, 0.2);
        GridLabelSizeMm = Clamp(GridLabelSizeMm, 1.5, 8.0, 3.2);
        if (string.IsNullOrWhiteSpace(GridColorHex)) GridColorHex = "#000000";
        SeriesColumns = Math.Clamp(SeriesColumns, 1, 6);
        SeriesRows = Math.Clamp(SeriesRows, 1, 6);
        SeriesOverlapPercent = Clamp(SeriesOverlapPercent, 0, 30, 5);
        LogoFilePath ??= "";
        CustomTitle ??= "";
        Subtitle ??= "";
        UrlLine ??= "";
        Custom1Label ??= "";
        Custom1Value ??= "";
        Custom2Label ??= "";
        Custom2Value ??= "";
        QrCustomText ??= "";
        LeftMarginText ??= "";
        RightMarginText ??= "";
        return this;
    }

    public static MapPrintSettings Load(string? path = null)
    {
        try
        {
            string file = path ?? DefaultPath;
            if (File.Exists(file))
            {
                var loaded = JsonSerializer.Deserialize<MapPrintSettings>(File.ReadAllText(file), JsonDefaults.Options);
                if (loaded != null) return loaded.Normalize();
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException || ex is NotSupportedException)
        {
            // Uszkodzony lub niedostępny plik ustawień — wracamy do wartości domyślnych.
        }
        return new MapPrintSettings();
    }

    /// <summary>Zapisuje ustawienia; zwraca komunikat błędu albo null.</summary>
    public string? Save(string? path = null)
    {
        try
        {
            string file = path ?? DefaultPath;
            string? dir = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(file, JsonSerializer.Serialize(this, JsonDefaults.Options));
            return null;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
        {
            return ex.Message;
        }
    }

    private static double Clamp(double v, double min, double max, double fallback) =>
        double.IsNaN(v) || double.IsInfinity(v) ? fallback : Math.Clamp(v, min, max);
}
