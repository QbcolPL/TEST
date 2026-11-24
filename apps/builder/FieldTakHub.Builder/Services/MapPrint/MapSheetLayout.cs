using System;

namespace FieldTakHub.Builder.Services.MapPrint;

/// <summary>Prostokąt w milimetrach (początek w lewym górnym rogu strony).</summary>
internal readonly record struct RectMm(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double CenterX => X + Width / 2.0;
    public double CenterY => Y + Height / 2.0;
}

/// <summary>
/// Podział strony wydruku: obszar mapy (ramka), pas stopki oraz marginesy boczne na pionowe opisy.
/// Obszar mapy w milimetrach jest jednocześnie formatem arkusza w edytorze — dzięki temu ekran pokazuje
/// dokładnie ten wycinek terenu, który trafi na papier.
/// </summary>
internal sealed class MapSheetLayout
{
    public const double FooterGapMm = 3.0;

    public double PageWidthMm { get; private init; }
    public double PageHeightMm { get; private init; }
    public RectMm MapRect { get; private init; }
    public RectMm FooterRect { get; private init; }
    public RectMm LeftMarginRect { get; private init; }
    public RectMm RightMarginRect { get; private init; }
    public bool HasFooter => FooterRect.Height > 0.5;

    public static (double Width, double Height) PaperSizeMm(string paper, double customWidth, double customHeight, bool landscape)
    {
        (double w, double h) = paper switch
        {
            "A3" => (297.0, 420.0),
            "A5" => (148.0, 210.0),
            "Letter" => (215.9, 279.4),
            "Własny" => (Math.Min(customWidth, customHeight), Math.Max(customWidth, customHeight)),
            _ => (210.0, 297.0)
        };
        return landscape ? (h, w) : (w, h);
    }

    public static MapSheetLayout Compute(MapPrintSettings s)
    {
        if (s == null) throw new ArgumentNullException(nameof(s));
        var (pw, ph) = PaperSizeMm(s.PaperSize, s.CustomPaperWidthMm, s.CustomPaperHeightMm, s.Landscape);

        double footer = s.ShowFooter ? s.FooterHeightMm : 0.0;
        double mapW = Math.Max(40.0, pw - s.MarginLeftMm - s.MarginRightMm);
        double mapH = Math.Max(40.0, ph - s.MarginTopMm - s.MarginBottomMm - footer);
        var map = new RectMm(s.MarginLeftMm, s.MarginTopMm, mapW, mapH);

        var footerRect = footer > 0
            ? new RectMm(map.X, map.Bottom + FooterGapMm, map.Width, Math.Max(0.0, footer - FooterGapMm))
            : new RectMm(map.X, map.Bottom, map.Width, 0.0);

        return new MapSheetLayout
        {
            PageWidthMm = pw,
            PageHeightMm = ph,
            MapRect = map,
            FooterRect = footerRect,
            LeftMarginRect = new RectMm(0.0, map.Y, Math.Max(0.0, map.X), map.Height),
            RightMarginRect = new RectMm(map.Right, map.Y, Math.Max(0.0, pw - map.Right), map.Height)
        };
    }

    /// <summary>Teren na arkuszu w metrach (szerokość × wysokość obszaru mapy) dla skali 1:<paramref name="scaleDenominator"/>.</summary>
    public (double WidthMeters, double HeightMeters) GroundSize(int scaleDenominator) =>
        (MapRect.Width / 1000.0 * scaleDenominator, MapRect.Height / 1000.0 * scaleDenominator);
}
