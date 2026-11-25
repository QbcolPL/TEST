using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FieldTakHub.Builder.Services.MapEditor;

/// <summary>Rodzaje obiektów generatora map i zasady wspólne dla wszystkich wierszy listy obiektów.</summary>
internal static class MapObjectKinds
{
    public const string Marker = "Marker";
    public const string Dot = "Dot";
    public const string Ao = "AO";
    public const string Sector = "Sector";
    public const string Route = "Route";

    /// <summary>Rodzaje wielopunktowe w kolejności listy „Typ” w wierszu obiektu.</summary>
    public static readonly IReadOnlyList<string> GeometryKinds = new[] { Ao, Sector, Route };

    public static bool IsMultiPoint(string? kind) => kind == Ao || kind == Sector || kind == Route;

    /// <summary>Najmniejsza liczba wierzchołków, z którą obiekt danego rodzaju ma sens na mapie.</summary>
    public static int MinimumPoints(string? kind) => kind == Route ? 2 : (kind == Ao || kind == Sector) ? 3 : 1;

    public static string DisplayName(string? kind) => kind switch
    {
        Ao => "Teren gry (AO)",
        Sector => "Sektor / strefa",
        Route => "Trasa / linia",
        Dot => "Kropka",
        _ => "Znacznik NATO"
    };

    /// <summary>Rodzaj z nazwy wyświetlanej na liście „Typ” (null, gdy nazwa nie pasuje do żadnego rodzaju wielopunktowego).</summary>
    public static string? FromDisplayName(string? displayName) =>
        GeometryKinds.FirstOrDefault(k => string.Equals(DisplayName(k), displayName, StringComparison.Ordinal));

    /// <summary>Domyślny opis w legendzie. Dla znacznika NATO null — opis pochodzi z wybranego symbolu.</summary>
    public static string? DefaultLegendText(string? kind) => kind switch
    {
        Ao => "Granica Terenu Gry (strefa poza grą)",
        Sector => "Sektor / Strefa taktyczna",
        Route => "Trasa / Linia rozgraniczenia",
        Dot => "Punkt orientacyjny",
        _ => null
    };

    /// <summary>Czy obiekt wielopunktowy o <paramref name="pointCount"/> wierzchołkach może zostać zamieniony na <paramref name="targetKind"/>.</summary>
    public static bool CanConvert(string? fromKind, string? targetKind, int pointCount) =>
        IsMultiPoint(fromKind) && IsMultiPoint(targetKind) && pointCount >= MinimumPoints(targetKind);

    /// <summary>Rodzaje, które można wybrać na liście „Typ” (zawsze z bieżącym rodzajem).</summary>
    public static IReadOnlyList<string> AvailableGeometryKinds(string? currentKind, int pointCount) =>
        GeometryKinds.Where(k => k == currentKind || CanConvert(currentKind, k, pointCount)).ToList();

    /// <summary>
    /// Opis w legendzie po zmianie rodzaju: opis domyślny poprzedniego rodzaju (albo pusty) zamienia się na domyślny nowego,
    /// a opis wpisany przez użytkownika zostaje bez zmian.
    /// </summary>
    public static string LegendTextAfterKindChange(string? currentText, string? previousDefault, string newDefault)
    {
        string current = (currentText ?? "").Trim();
        if (current.Length == 0) return newDefault;
        return string.Equals(current, (previousDefault ?? "").Trim(), StringComparison.Ordinal) ? newDefault : currentText!;
    }
}

/// <summary>Które wiersze ma karta obiektu na liście — jedna definicja dla wszystkich rodzajów.</summary>
internal sealed record MapObjectCardLayout(bool HasRotation, bool HasGeometryTools, bool CanSimplify);

/// <summary>
/// Wspólny układ karty obiektu: wiersz nagłówka [etykieta] [kolor] [📍] [symbol] [nazwa] [− usuń],
/// „W legendzie”, „Wielkość”, a dalej „Obrót” (znacznik NATO) albo „Geometria” (AO, sektor, trasa).
/// </summary>
internal static class MapObjectCardSpec
{
    public const double MinSizeMultiplier = 0.35;
    public const double MaxSizeMultiplier = 2.60;

    /// <summary>Upraszczanie kształtu ma sens dopiero od tylu wierzchołków (np. gęsty ślad GPS).</summary>
    public const int SimplifyMinPoints = 6;

    /// <summary>Tolerancja upraszczania kształtu w metrach.</summary>
    public const double SimplifyToleranceMeters = 10.0;

    /// <summary>Granice średnicy kropki na mapie (tak samo jak przy rysowaniu).</summary>
    public const double MinDotPx = 4.0;
    public const double MaxDotPx = 56.0;

    public static MapObjectCardLayout For(string? kind, int pointCount) => new(
        HasRotation: kind == MapObjectKinds.Marker,
        HasGeometryTools: MapObjectKinds.IsMultiPoint(kind),
        CanSimplify: MapObjectKinds.IsMultiPoint(kind) && pointCount >= SimplifyMinPoints);

    public static double NormalizeSize(double multiplier) =>
        multiplier > 0.1 ? Math.Clamp(multiplier, MinSizeMultiplier, MaxSizeMultiplier) : 1.0;

    /// <summary>Opis suwaka „Wielkość” w jednym formacie dla każdego rodzaju („1.00×”); kropka dodatkowo w pikselach.</summary>
    public static string FormatSize(string? kind, double multiplier, double dotBasePx)
    {
        double m = multiplier > 0.1 ? multiplier : 1.0;
        string text = m.ToString("0.00", CultureInfo.InvariantCulture) + "×";
        if (kind != MapObjectKinds.Dot) return text;
        double px = Math.Clamp(Math.Round(dotBasePx * m), MinDotPx, MaxDotPx);
        return px.ToString("0", CultureInfo.InvariantCulture) + " px · " + text;
    }

    /// <summary>Kąt obrotu sprowadzony do zakresu 0–360° (uchwyt obrotu na mapie może zapisać wartość ujemną lub większą).</summary>
    public static double NormalizeRotation(double degrees)
    {
        if (double.IsNaN(degrees) || double.IsInfinity(degrees)) return 0.0;
        double normalized = degrees % 360.0;
        return normalized < 0 ? normalized + 360.0 : normalized + 0.0; // + 0.0 zamienia -0 na 0 (inaczej opis „-0°”)
    }

    public static string FormatRotation(double degrees) =>
        (Math.Round(NormalizeRotation(degrees)) % 360.0).ToString("0", CultureInfo.InvariantCulture) + "°";

    public static string FormatPointCount(int count) =>
        count.ToString(CultureInfo.InvariantCulture) + (count == 1 ? " punkt" : (count % 10 >= 2 && count % 10 <= 4 && (count % 100 < 10 || count % 100 >= 20) ? " punkty" : " punktów"));
}

/// <summary>
/// Teksty wspólnej listy „wszystko na mapie” (obiekty i sektory ognia): liczności z polską odmianą
/// i krótki opis w wierszu obok nazwy.
/// </summary>
internal static class MapListText
{
    /// <summary>„1 obiekt”, „3 obiekty”, „12 obiektów”, „22 obiekty”.</summary>
    public static string Plural(int count, string one, string few, string many)
    {
        int lastDigit = Math.Abs(count) % 10, lastTwo = Math.Abs(count) % 100;
        string word = count == 1 ? one : (lastDigit >= 2 && lastDigit <= 4 && (lastTwo < 10 || lastTwo >= 20) ? few : many);
        return count.ToString(CultureInfo.InvariantCulture) + " " + word;
    }

    /// <summary>„3 obiekty · 1 sektor ognia” (pomija zerowe części; pusty tekst, gdy nie ma nic).</summary>
    public static string CountList(int objects, int fireSectors)
    {
        var parts = new List<string>();
        if (objects > 0) parts.Add(Plural(objects, "obiekt", "obiekty", "obiektów"));
        if (fireSectors > 0) parts.Add(Plural(fireSectors, "sektor ognia", "sektory ognia", "sektorów ognia"));
        return string.Join(" · ", parts);
    }

    /// <summary>Nagłówek listy, np. „Na mapie: 3 obiekty · 1 sektor ognia”.</summary>
    public static string CountSummary(int objects, int fireSectors)
    {
        string list = CountList(objects, fireSectors);
        return list.Length == 0 ? "Na mapie nie ma jeszcze obiektów" : "Na mapie: " + list;
    }

    /// <summary>Opis obiektu w wierszu: symbol znacznika, „Kropka” albo rodzaj kształtu z liczbą punktów.</summary>
    public static string ObjectInfo(string? kind, string? natoSymbolLabel, int pointCount)
    {
        if (MapObjectKinds.IsMultiPoint(kind))
            return MapObjectKinds.DisplayName(kind) + " · " + MapObjectCardSpec.FormatPointCount(pointCount);
        if (kind == MapObjectKinds.Dot) return MapObjectKinds.DisplayName(kind);
        return string.IsNullOrWhiteSpace(natoSymbolLabel) ? MapObjectKinds.DisplayName(kind) : natoSymbolLabel!.Trim();
    }

    /// <summary>Opis sektora ognia w wierszu: zasięg (płaski ➔ efektywny), kierunek i szerokość, przewaga wysokości.</summary>
    public static string FireSectorInfo(double baseRadiusM, double effectiveRadiusM, double azimuthDeg, double spreadDeg, double elevationDeltaM)
    {
        var inv = CultureInfo.InvariantCulture;
        string range = Math.Abs(effectiveRadiusM - baseRadiusM) < 0.05
            ? baseRadiusM.ToString("0", inv) + " m"
            : baseRadiusM.ToString("0", inv) + " ➔ " + effectiveRadiusM.ToString("0.0", inv) + " m";
        string shape = spreadDeg >= 359.5
            ? "okrąg"
            : "az. " + MapObjectCardSpec.FormatRotation(azimuthDeg) + " · " + Math.Round(spreadDeg).ToString("0", inv) + "°";
        string height = Math.Abs(elevationDeltaM) >= 0.1 ? " · ΔH " + elevationDeltaM.ToString("+0.#;-0.#", inv) + " m" : "";
        return range + " · " + shape + height;
    }
}
