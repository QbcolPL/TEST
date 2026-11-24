using System;
using System.Globalization;
using FieldTakHub.Builder.Services.MapImagery;

namespace FieldTakHub.Builder.Services.MapPrint;

/// <summary>
/// Wyliczone dane arkusza: narożnik SW, nazwa MGRS, strefa UTM, zbieżność południków (PT względem PG),
/// deklinacja WMM2025 (PM względem PG) i kąt PT–PM liczony z wartości zaokrąglonych do 0,1° — tak jak na diagramie.
/// </summary>
internal sealed class MapSheetFacts
{
    public double CenterLat { get; init; }
    public double CenterLon { get; init; }
    public double SouthWestLat { get; init; }
    public double SouthWestLon { get; init; }
    public int UtmZone { get; init; }
    public double ConvergenceDeg { get; init; }
    public GeoMagneticModel.Result Magnetic { get; init; }
    public bool MagneticModelValid { get; init; }
    public double GridMagneticAngleDeg { get; init; }
    public string SheetId { get; init; } = "";
    public double GridIntervalMeters { get; init; }
    public int ScaleDenominator { get; init; }
    public DateTime Date { get; init; }

    /// <param name="mapView">Widok obszaru mapy (piksele dowolne — liczą się tylko narożniki i środek).</param>
    public static MapSheetFacts Compute(MercatorViewport mapView, int scaleDenominator, int gridIntervalSetting, DateTime date)
    {
        var (cLat, cLon) = mapView.ToLatLon(mapView.Width / 2.0, mapView.Height / 2.0);
        var (swLat, swLon) = mapView.ToLatLon(0.0, mapView.Height);
        int zone = MapGridMath.UtmZone(cLon);
        double gamma = MapGridMath.GridConvergenceDeg(cLat, cLon, zone);
        double year = GeoMagneticModel.DecimalYear(date);
        var magnetic = GeoMagneticModel.Compute(cLat, cLon, 0.0, year);
        return new MapSheetFacts
        {
            CenterLat = cLat,
            CenterLon = cLon,
            SouthWestLat = swLat,
            SouthWestLon = swLon,
            UtmZone = zone,
            ConvergenceDeg = gamma,
            Magnetic = magnetic,
            MagneticModelValid = GeoMagneticModel.IsValidFor(year),
            GridMagneticAngleDeg = Math.Round(magnetic.DeclinationDeg, 1) - Math.Round(gamma, 1),
            SheetId = MapGridMath.SheetIdFromSouthWest(swLat, swLon),
            GridIntervalMeters = gridIntervalSetting > 0 ? gridIntervalSetting : MapGridMath.AutoGridInterval(scaleDenominator),
            ScaleDenominator = scaleDenominator,
            Date = date
        };
    }

    /// <summary>Treść kodu QR dla wybranego trybu (null = bez kodu).</summary>
    public string? QrPayload(string mode, string customText)
    {
        string lat = CenterLat.ToString("0.000000", CultureInfo.InvariantCulture);
        string lon = CenterLon.ToString("0.000000", CultureInfo.InvariantCulture);
        string? payload = mode switch
        {
            var m when m.StartsWith("geo:", StringComparison.Ordinal) => $"geo:{lat},{lon}",
            "Google Maps" => $"https://www.google.com/maps/search/?api=1&query={lat},{lon}",
            "OpenStreetMap" => $"https://www.openstreetmap.org/?mlat={lat}&mlon={lon}#map=15/{lat}/{lon}",
            var m when m.StartsWith("MGRS", StringComparison.Ordinal) => MapGridMath.FullMgrs(CenterLat, CenterLon),
            "Własny tekst" => string.IsNullOrWhiteSpace(customText) ? null : customText.Trim(),
            _ => null
        };
        return payload;
    }
}

/// <summary>Stałe opisy ramki po polsku albo po angielsku (jak na arkuszach T.REX).</summary>
internal sealed record MapFrameTexts(
    string Scale, string Crs, string SouthWestLat, string SouthWestLon,
    string GmAngle, string GridNorth, string MagneticNorth, string PerYear, string NotToScale, string ModelExpired,
    string ScaleCheckFormat, string GeneratedFormat, string SheetOfFormat, string NearFormat, string DistanceFormat,
    string DefaultRightNote, string GridAndModelCredit)
{
    public static readonly MapFrameTexts Polish = new(
        "SKALA", "UKŁAD", "SZER. SW", "DŁ. SW",
        "KĄT PT–PM", "PT", "PM", "/rok", "nie w skali", "model WMM2025 nieaktualny",
        "Jeśli podziałka km nie ma {0} mm, skala wydruku jest błędna", "Deklinacja {0} · mapa z {1}", "arkusz {0} z {1}",
        "W POBLIŻU {0}", "{0} KM NA {1} OD {2}",
        "Mapa w odwzorowaniu Web Mercator (północ geograficzna u góry). Linie siatki UTM/MGRS są obrócone o zbieżność południków — patrz diagram PT/PM.",
        "Siatka UTM/MGRS (WGS84) · deklinacja WMM2025 (NOAA NCEI/BGS)");

    public static readonly MapFrameTexts English = new(
        "SCALE", "CRS", "SW LAT", "SW LON",
        "G-M ANGLE", "GN", "MN", "/yr", "not to scale", "WMM2025 model out of date",
        "If the km bar is not {0} mm, the scale is wrong", "Declination {0} · map generated {1}", "sheet {0} of {1}",
        "NEAR {0}", "{0} KM {1} OF {2}",
        "Web Mercator map (true north up). UTM/MGRS grid lines are rotated by grid convergence — see the GN/MN diagram.",
        "UTM/MGRS grid (WGS84) · declination WMM2025 (NOAA NCEI/BGS)");

    public static MapFrameTexts For(bool polish) => polish ? Polish : English;
}
