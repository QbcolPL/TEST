using System;
using System.Collections.Generic;
using System.Globalization;

namespace FieldTakHub.Builder.Services.MapPrint;

/// <summary>
/// Czyste obliczenia kartograficzne dla wydruku: strefa UTM, zbieżność południków,
/// oznaczenia MGRS, nazwa arkusza, opisy siatki i dobór podziałek liniowych.
/// Bez WPF — testowane w FieldTakHub.Builder.Tests.
/// </summary>
internal static class MapGridMath
{
    private const string LatitudeBands = "CDEFGHJKLMNPQRSTUVWXX";

    public static int UtmZone(double lon) => Math.Clamp((int)Math.Floor((lon + 180.0) / 6.0) + 1, 1, 60);

    public static double CentralMeridian(int zone) => (zone - 1) * 6.0 - 180.0 + 3.0;

    /// <summary>
    /// Zbieżność południków (kąt od północy geograficznej do północy siatki UTM), w stopniach.
    /// Dodatnia = północ siatki na wschód od północy geograficznej.
    /// </summary>
    public static double GridConvergenceDeg(double lat, double lon, int zone)
    {
        double dLon = (lon - CentralMeridian(zone)) * Math.PI / 180.0;
        double phi = lat * Math.PI / 180.0;
        return Math.Atan(Math.Tan(dLon) * Math.Sin(phi)) * 180.0 / Math.PI;
    }

    public static char LatitudeBand(double lat)
    {
        int idx = (int)Math.Floor((lat + 80.0) / 8.0);
        return LatitudeBands[Math.Clamp(idx, 0, LatitudeBands.Length - 1)];
    }

    /// <summary>Dwuliterowy identyfikator kwadratu 100 km MGRS (np. "DE").</summary>
    public static string Mgrs100kSquare(int zone, double easting, double northing)
    {
        int set = ((zone - 1) % 6) + 1;
        string colLetters = (set % 3) switch { 1 => "ABCDEFGH", 2 => "JKLMNPQR", _ => "STUVWXYZ" };
        string rowLetters = (set % 2 == 1) ? "ABCDEFGHJKLMNPQRSTUV" : "FGHJKLMNPQRSTUVABCDE";
        int colIdx = Math.Clamp((int)Math.Floor(easting / 100000.0) - 1, 0, 7);
        int rowIdx = (((int)Math.Floor(northing / 100000.0)) % 20 + 20) % 20;
        return $"{colLetters[colIdx]}{rowLetters[rowIdx]}";
    }

    /// <summary>
    /// Nazwa arkusza jak na mapach T.REX: strefa+pas, kwadrat 100 km i kilometrowy kwadrat siatki
    /// zawierający lewy dolny (SW) narożnik mapy, np. "34UDC 12 45".
    /// </summary>
    public static string SheetIdFromSouthWest(double lat, double lon)
    {
        int zone = UtmZone(lon);
        UtmGrid2DProjector.LatLonToUtm(lat, lon, zone, out double e, out double n);
        string square = Mgrs100kSquare(zone, e, n);
        int eKm = (int)Math.Floor(Mod(e, 100000.0) / 1000.0);
        int nKm = (int)Math.Floor(Mod(n, 100000.0) / 1000.0);
        return $"{zone}{LatitudeBand(lat)}{square} {eKm:D2} {nKm:D2}";
    }

    /// <summary>Pełne odniesienie MGRS z dokładnością do 1 m (np. "34U DC 12345 45678").</summary>
    public static string FullMgrs(double lat, double lon)
    {
        int zone = UtmZone(lon);
        UtmGrid2DProjector.LatLonToUtm(lat, lon, zone, out double e, out double n);
        int e5 = (int)Math.Floor(Mod(e, 100000.0));
        int n5 = (int)Math.Floor(Mod(n, 100000.0));
        return $"{zone}{LatitudeBand(lat)} {Mgrs100kSquare(zone, e, n)} {e5:D5} {n5:D5}";
    }

    /// <summary>
    /// Opis linii siatki: dla kroku od 1 km dwie cyfry kilometrów w kwadracie 100 km ("35"),
    /// dla mniejszych kroków trzy cyfry setek metrów ("347").
    /// </summary>
    public static string GridLineLabel(double coordinate, double intervalMeters)
    {
        double within = Mod(Math.Round(coordinate), 100000.0);
        if (intervalMeters >= 1000.0 - 1e-6)
            return ((int)Math.Round(within / 1000.0) % 100).ToString("D2", CultureInfo.InvariantCulture);
        return ((int)Math.Round(within / 100.0) % 1000).ToString("D3", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Automatyczny krok siatki dla skali 1:<paramref name="scaleDenominator"/>: 1 km dla map 1:25 000–1:60 000
    /// (jak na mapach wojskowych), gęściej dla planów 1:2 500–1:15 000, rzadziej dla map przeglądowych.
    /// </summary>
    public static double AutoGridInterval(double scaleDenominator)
    {
        if (scaleDenominator <= 3000) return 100.0;
        if (scaleDenominator <= 7500) return 200.0;
        if (scaleDenominator <= 15000) return 500.0;
        if (scaleDenominator <= 60000) return 1000.0;
        if (scaleDenominator <= 150000) return 5000.0;
        return 10000.0;
    }

    public static double Mod(double value, double modulus) => ((value % modulus) + modulus) % modulus;

    // ---------------------------------------------------------------- opis położenia

    /// <summary>Odległość po sferze (do opisu odległości arkusza od miejscowości).</summary>
    public static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        double dLat = (lat2 - lat1) * Math.PI / 180.0, dLon = (lon2 - lon1) * Math.PI / 180.0;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 6371000.0 * 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
    }

    /// <summary>Azymut 0–360° z punktu 1 do punktu 2.</summary>
    public static double InitialBearingDeg(double lat1, double lon1, double lat2, double lon2)
    {
        double p1 = lat1 * Math.PI / 180.0, p2 = lat2 * Math.PI / 180.0, dl = (lon2 - lon1) * Math.PI / 180.0;
        double y = Math.Sin(dl) * Math.Cos(p2);
        double x = Math.Cos(p1) * Math.Sin(p2) - Math.Sin(p1) * Math.Cos(p2) * Math.Cos(dl);
        return Mod(Math.Atan2(y, x) * 180.0 / Math.PI, 360.0);
    }

    /// <summary>Kierunek z 8-punktowej róży wiatrów: po polsku (PN, PN-WSCH, ...) albo po angielsku (N, NE, ...).</summary>
    public static string CompassPoint(double bearingDeg, bool polish)
    {
        string[] pl = { "PN", "PN-WSCH", "WSCH", "PD-WSCH", "PD", "PD-ZACH", "ZACH", "PN-ZACH" };
        string[] en = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        int idx = (int)Math.Round(Mod(bearingDeg, 360.0) / 45.0) % 8;
        return polish ? pl[idx] : en[idx];
    }

    /// <summary>Kąt z literą strony, np. "3.2° W" / "6.9° E"; zero bez litery.</summary>
    public static string FormatEastWest(double deg, int decimals = 1)
    {
        double rounded = Math.Round(Math.Abs(deg), decimals);
        string number = rounded.ToString("F" + decimals, CultureInfo.InvariantCulture) + "°";
        if (rounded == 0.0) return number;
        return number + (deg < 0 ? " W" : " E");
    }

    /// <summary>Skala z separatorem tysięcy: "1:25,000" (EN) albo "1:25 000" (PL).</summary>
    public static string FormatScale(int scaleDenominator, bool polish)
    {
        var nfi = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
        nfi.NumberGroupSeparator = polish ? "\u00A0" : ",";
        return "1:" + scaleDenominator.ToString("#,0", nfi);
    }

    /// <summary>Pełna wartość współrzędnej UTM z separatorem co 3 cyfry, np. "535 000".</summary>
    public static string FullGridLabel(double coordinate)
    {
        var nfi = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
        nfi.NumberGroupSeparator = "\u00A0";
        return Math.Round(coordinate).ToString("#,0", nfi);
    }

    // ---------------------------------------------------------------- podziałki

    /// <summary>Opis podziałki liniowej: długość całkowita w jednostkach, krok etykiet, liczba podziałów pierwszego odcinka.</summary>
    public readonly record struct ScaleBarSpec(double TotalUnits, double MajorStepUnits, int MinorDivisions, double LengthMm);

    /// <summary>
    /// Dobiera „okrągłą” podziałkę dla skali 1:<paramref name="scaleDenominator"/> i jednostki
    /// <paramref name="unitMeters"/> (1000 = km, 1609.344 = mila), nie dłuższą niż <paramref name="maxLengthMm"/>.
    /// Przy 1:25 000 i 100 mm daje 2,5 km — dokładnie jak na arkuszach T.REX.
    /// </summary>
    public static ScaleBarSpec NiceScaleBar(double scaleDenominator, double unitMeters, double maxLengthMm)
    {
        double unitsPerMm = scaleDenominator / 1000.0 / unitMeters;
        double maxUnits = maxLengthMm * unitsPerMm;
        double best = 0.0;
        foreach (double mantissa in new[] { 1.0, 1.5, 2.0, 2.5, 3.0, 4.0, 5.0, 6.0, 8.0 })
        {
            for (int exp = -3; exp <= 3; exp++)
            {
                double candidate = mantissa * Math.Pow(10, exp);
                if (candidate <= maxUnits + 1e-9 && candidate > best) best = candidate;
            }
        }
        if (best <= 0.0) best = maxUnits;

        double major = ChooseMajorStep(best);
        int minor = 5;
        double length = best / unitsPerMm;
        return new ScaleBarSpec(best, major, minor, length);
    }

    private static double ChooseMajorStep(double total)
    {
        foreach (int count in new[] { 5, 4, 3, 6, 2, 8 })
        {
            double step = total / count;
            if (IsRoundStep(step)) return step;
        }
        return total / 4.0;
    }

    private static bool IsRoundStep(double step)
    {
        if (step <= 0) return false;
        double exp = Math.Pow(10, Math.Floor(Math.Log10(step)));
        double mantissa = step / exp;
        foreach (double ok in new[] { 1.0, 2.0, 2.5, 5.0, 10.0 })
            if (Math.Abs(mantissa - ok) < 1e-6) return true;
        return false;
    }

    /// <summary>Liczba z minimalną potrzebną liczbą miejsc po przecinku (kropka dziesiętna), np. 0.5, 1, 2.5.</summary>
    public static string FormatUnits(double value)
    {
        double rounded = Math.Round(value, 3);
        return rounded.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>Kolejne wartości (np. wschodnie) będące wielokrotnością kroku w przedziale [min, max].</summary>
    public static IEnumerable<double> Multiples(double min, double max, double step)
    {
        if (step <= 0 || max < min) yield break;
        double start = Math.Ceiling(min / step) * step;
        for (double v = start; v <= max + 1e-6; v += step)
            yield return Math.Round(v, 6);
    }
}
