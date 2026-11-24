using System;
using System.Collections.Generic;
using System.Globalization;
using FieldTakHub.Builder.Services.MapPrint;

namespace FieldTakHub.Builder.Services.MapEditor;

/// <summary>Strona, po której stoi opis pozycji pierścienia menu.</summary>
internal enum RadialLabelSide { Top, Right, Bottom, Left }

/// <summary>
/// Geometria pierścienia menu otwieranego prawym przyciskiem na mapie. Pozycja 0 jest u góry,
/// kolejne idą zgodnie z ruchem wskazówek zegara; współrzędne ekranowe (oś Y w dół).
/// </summary>
internal static class RadialMenuGeometry
{
    /// <summary>Kąt środka pozycji w stopniach: 0 = góra, 90 = prawo.</summary>
    public static double CenterAngleDeg(int index, int count) => count <= 0 ? 0.0 : index * 360.0 / count;

    /// <summary>Wektor jednostkowy w kierunku kąta (ekran: Y w dół).</summary>
    public static (double X, double Y) Direction(double angleDeg)
    {
        double rad = angleDeg * Math.PI / 180.0;
        return (Math.Sin(rad), -Math.Cos(rad));
    }

    /// <summary>
    /// Pozycja pod punktem (dx, dy) względem środka pierścienia albo -1, gdy punkt jest w środku (anuluj)
    /// lub dalej niż <paramref name="maxRadius"/>. Liczy się tylko kierunek — jak w menu gestów.
    /// </summary>
    public static int IndexAt(double dx, double dy, int count, double innerRadius, double maxRadius)
    {
        if (count <= 0 || double.IsNaN(dx) || double.IsNaN(dy)) return -1;
        double r = Math.Sqrt(dx * dx + dy * dy);
        if (r < innerRadius || r > maxRadius) return -1;
        double angle = Math.Atan2(dx, -dy) * 180.0 / Math.PI;
        double sector = 360.0 / count;
        double shifted = ((angle + sector / 2.0) % 360.0 + 360.0) % 360.0;
        return Math.Min(count - 1, (int)(shifted / sector));
    }

    public static RadialLabelSide LabelSide(double angleDeg)
    {
        double a = ((angleDeg % 360.0) + 360.0) % 360.0;
        if (a < 22.5 || a > 337.5) return RadialLabelSide.Top;
        if (a > 157.5 && a < 202.5) return RadialLabelSide.Bottom;
        return a < 180.0 ? RadialLabelSide.Right : RadialLabelSide.Left;
    }
}

/// <summary>Punkt na ekranie mapy (piksele płótna).</summary>
internal readonly record struct ScreenPoint(double X, double Y);

/// <summary>Obrys obiektu na ekranie: trasa (otwarta) albo obszar — AO lub sektor (zamknięty).</summary>
internal sealed record HitOutline(IReadOnlyList<ScreenPoint> Points, bool Closed);

/// <summary>Trafianie kursorem w kształty rysowane na mapie.</summary>
internal static class MapHitTest
{
    public static double DistanceToSegment(ScreenPoint p, ScreenPoint a, ScreenPoint b)
    {
        double vx = b.X - a.X, vy = b.Y - a.Y;
        double lengthSq = vx * vx + vy * vy;
        double t = lengthSq <= 1e-12 ? 0.0 : Math.Clamp(((p.X - a.X) * vx + (p.Y - a.Y) * vy) / lengthSq, 0.0, 1.0);
        double cx = a.X + t * vx - p.X, cy = a.Y + t * vy - p.Y;
        return Math.Sqrt(cx * cx + cy * cy);
    }

    /// <summary>Odległość od linii obrysu (dla obszaru — także od odcinka zamykającego).</summary>
    public static double DistanceToOutline(ScreenPoint p, IReadOnlyList<ScreenPoint> points, bool closed)
    {
        if (points == null || points.Count == 0) return double.PositiveInfinity;
        if (points.Count == 1) return DistanceToSegment(p, points[0], points[0]);
        double best = double.PositiveInfinity;
        for (int i = 0; i < points.Count - 1; i++)
            best = Math.Min(best, DistanceToSegment(p, points[i], points[i + 1]));
        if (closed && points.Count > 2)
            best = Math.Min(best, DistanceToSegment(p, points[points.Count - 1], points[0]));
        return best;
    }

    /// <summary>
    /// Indeks obrysu najbliższego kursorowi (w granicach <paramref name="tolerance"/>) albo -1.
    /// Wnętrze obszaru nie jest trafieniem — prawy klik wewnątrz terenu gry otwiera menu mapy, nie menu AO.
    /// Przy równej odległości wygrywa obiekt narysowany później (leży wyżej).
    /// </summary>
    public static int FindClosestOutline(ScreenPoint p, IReadOnlyList<HitOutline> outlines, double tolerance)
    {
        int best = -1;
        double bestDistance = double.PositiveInfinity;
        for (int i = 0; i < outlines.Count; i++)
        {
            double d = DistanceToOutline(p, outlines[i].Points, outlines[i].Closed);
            if (d <= tolerance && d <= bestDistance)
            {
                best = i;
                bestDistance = d;
            }
        }
        return best;
    }
}

/// <summary>Opis pomiaru na mapie: odległość i azymut z kierunkiem.</summary>
internal static class MapMeasure
{
    public static string FormatDistance(double meters)
    {
        if (double.IsNaN(meters) || meters < 0) meters = 0;
        return meters < 1000.0
            ? Math.Round(meters).ToString("0", CultureInfo.InvariantCulture) + " m"
            : (meters / 1000.0).ToString("0.00", CultureInfo.InvariantCulture) + " km";
    }

    public static string Label(double lat1, double lon1, double lat2, double lon2)
    {
        double meters = MapGridMath.HaversineMeters(lat1, lon1, lat2, lon2);
        double bearing = MapGridMath.InitialBearingDeg(lat1, lon1, lat2, lon2);
        int degrees = (int)Math.Round(bearing) % 360;
        return $"{FormatDistance(meters)} · azymut {degrees.ToString(CultureInfo.InvariantCulture)}° {MapGridMath.CompassPoint(bearing, polish: true)}";
    }
}
