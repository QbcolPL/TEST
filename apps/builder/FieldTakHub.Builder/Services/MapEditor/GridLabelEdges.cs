using System;
using System.Collections.Generic;
using System.Linq;

namespace FieldTakHub.Builder.Services.MapEditor;

/// <summary>Krawędzie arkusza mapy, przy których stoją opisy siatki — dowolne połączenie.</summary>
[Flags]
internal enum GridEdges
{
    None = 0,
    Top = 1,
    Bottom = 2,
    Left = 4,
    Right = 8,
    All = Top | Bottom | Left | Right
}

internal sealed record GridEdgeOption(GridEdges Edge, string Token, string Glyph, string Label, string Where);

/// <summary>
/// Opisy siatki przy krawędziach arkusza: numery linii MGRS, listwy sektorów (A, B, C / 1, 2, 3) i stopnie WGS84.
/// Opisy linii pionowych stoją przy górnej i/lub dolnej krawędzi, poziomych — przy lewej i/lub prawej.
/// Narożnik, w którym spotykają się opisy dwóch krawędzi, zostaje wolny (żaden opis nie zachodzi na drugi).
/// </summary>
internal static class GridLabelEdges
{
    /// <summary>Domyślnie wszystkie cztery krawędzie (jak na mapach topograficznych).</summary>
    public const GridEdges Default = GridEdges.All;

    public const string JsonKey = "gridLabelEdges";

    /// <summary>Odstęp opisu od krawędzi arkusza (px).</summary>
    public const double Inset = 6.0;

    public static readonly IReadOnlyList<GridEdgeOption> Options = new[]
    {
        new GridEdgeOption(GridEdges.Top, "top", "▲", "Góra", "górnej"),
        new GridEdgeOption(GridEdges.Bottom, "bottom", "▼", "Dół", "dolnej"),
        new GridEdgeOption(GridEdges.Left, "left", "◀", "Lewa", "lewej"),
        new GridEdgeOption(GridEdges.Right, "right", "▶", "Prawa", "prawej")
    };

    /// <summary>Zapis do projektu: „top,bottom,left,right”, „none” — bez krawędzi.</summary>
    public static string Serialize(GridEdges edges)
    {
        var tokens = Options.Where(o => (edges & o.Edge) != 0).Select(o => o.Token).ToList();
        return tokens.Count == 0 ? "none" : string.Join(",", tokens);
    }

    /// <summary>Odczyt z projektu; pusty albo nieznany zapis — <paramref name="fallback"/>.</summary>
    public static GridEdges Parse(string? text, GridEdges fallback = Default)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        string trimmed = text.Trim().ToLowerInvariant();
        if (trimmed == "none") return GridEdges.None;

        GridEdges result = GridEdges.None;
        bool recognised = false;
        foreach (string part in trimmed.Split(new[] { ',', ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var option = Options.FirstOrDefault(o => o.Token == part);
            if (option == null) continue;
            result |= option.Edge;
            recognised = true;
        }
        return recognised ? result : fallback;
    }

    public static GridEdges With(GridEdges edges, GridEdges edge, bool enabled) => enabled ? edges | edge : edges & ~edge;

    /// <summary>
    /// Lewy górny róg opisu linii siatki: <paramref name="lineCoord"/> to X linii pionowej (góra, dół)
    /// albo Y linii poziomej (lewa, prawa), w którym linia przecina pas opisów.
    /// </summary>
    public static (double X, double Y) LabelOrigin(GridEdges edge, double lineCoord, double labelWidth, double labelHeight,
        double canvasWidth, double canvasHeight, double inset = Inset) => edge switch
    {
        GridEdges.Top => (lineCoord - labelWidth / 2.0, inset),
        GridEdges.Bottom => (lineCoord - labelWidth / 2.0, canvasHeight - inset - labelHeight),
        GridEdges.Left => (inset, lineCoord - labelHeight / 2.0),
        GridEdges.Right => (canvasWidth - inset - labelWidth, lineCoord - labelHeight / 2.0),
        _ => throw new ArgumentOutOfRangeException(nameof(edge), edge, "Opis stoi przy jednej krawędzi.")
    };

    /// <summary>
    /// Czy opis mieści się wzdłuż swojej krawędzi i omija narożniki zajęte przez pasy opisów sąsiednich krawędzi:
    /// <paramref name="sideBand"/> — szerokość pasa przy lewej / prawej krawędzi, <paramref name="endBand"/> — wysokość pasa
    /// przy górnej / dolnej.
    /// </summary>
    public static bool FitsAlongEdge(GridEdges edge, double x, double y, double labelWidth, double labelHeight,
        double canvasWidth, double canvasHeight, GridEdges enabled, double sideBand, double endBand)
    {
        const double edgeGap = 2.0;
        if (edge == GridEdges.Top || edge == GridEdges.Bottom)
        {
            double minX = (enabled & GridEdges.Left) != 0 ? sideBand : edgeGap;
            double maxX = canvasWidth - ((enabled & GridEdges.Right) != 0 ? sideBand : edgeGap);
            return x >= minX && x + labelWidth <= maxX;
        }
        double minY = (enabled & GridEdges.Top) != 0 ? endBand : edgeGap;
        double maxY = canvasHeight - ((enabled & GridEdges.Bottom) != 0 ? endBand : edgeGap);
        return y >= minY && y + labelHeight <= maxY;
    }

    /// <summary>
    /// Ile miejsca przy krawędzi zajmują opisy aktywnej siatki — o tyle dalej od tej krawędzi stoją domyślnie
    /// znacznik Północy, podziałka i legenda (dopóki użytkownik ich nie przesunie).
    /// </summary>
    public static double Reserve(GridEdges enabled, GridEdges edge, bool mgrsLabels, bool sectorRulers, bool wgs84Labels, double fontScale)
    {
        if ((enabled & edge) == 0) return 0.0;
        bool vertical = edge == GridEdges.Left || edge == GridEdges.Right;
        double scale = Math.Clamp(fontScale, 0.5, 3.0);
        double reserve = 0.0;
        if (mgrsLabels) reserve = Math.Max(reserve, vertical ? 16.0 + 22.0 * scale : 10.0 + 13.0 * scale);
        if (sectorRulers) reserve = Math.Max(reserve, vertical ? 22.0 : 19.0);
        if (wgs84Labels) reserve = Math.Max(reserve, vertical ? 80.0 : 22.0);
        return reserve;
    }
}
