using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FieldTakHub.Builder.Services.MapImagery;

namespace FieldTakHub.Builder.Services.MapPrint;

/// <summary>Wszystko, czego potrzeba do narysowania jednej strony wydruku z ramką.</summary>
internal sealed class MapFramePage
{
    public MapPrintSettings Settings { get; init; } = new MapPrintSettings();
    public MapSheetLayout Layout { get; init; } = MapSheetLayout.Compute(new MapPrintSettings());
    public MapSheetFacts Facts { get; init; } = new MapSheetFacts();
    /// <summary>Pikseli strony na milimetr (DPI / 25,4).</summary>
    public double PxPerMm { get; init; }
    /// <summary>Widok obszaru mapy w pikselach strony (rozmiar = <see cref="MapFrameRenderer.MapPixelRect"/>).</summary>
    public MercatorViewport MapView { get; init; }
    public BitmapSource? Imagery { get; init; }
    /// <summary>Obiekty z edytora (przezroczyste tło), wyrównane szerokością do obszaru mapy i wyśrodkowane w pionie.</summary>
    public BitmapSource? Objects { get; init; }
    public ImageSource? Logo { get; init; }
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string BasemapName { get; init; } = "";
    public string LeftMarginText { get; init; } = "";
    public string RightMarginText { get; init; } = "";
    public string PageNote { get; init; } = "";
    public string? QrPayload { get; init; }
}

/// <summary>
/// Rysuje stronę mapy w stylu arkuszy taktycznych 1:25 000: linia ramki z podziałką, siatka UTM/MGRS z kreskami
/// co 1/10 oczka i opisami na białej obwódce, stopka (logo, nazwa arkusza, pola, podziałki km/mile, kąt PT–PM
/// z diagramem, QR) oraz pionowe opisy na marginesach. Rysowanie w pikselach strony (bez skalowania czcionek).
/// </summary>
internal static class MapFrameRenderer
{
    private static readonly FontFamily CondensedFamily = new FontFamily("Bahnschrift SemiBold Condensed, Bahnschrift, Arial Narrow, Segoe UI");
    private static readonly FontFamily MonoFamily = new FontFamily("Consolas, Courier New");
    private static readonly FontFamily BodyFamily = new FontFamily("Segoe UI, Arial");
    private static readonly Typeface TitleFace = new Typeface(CondensedFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Condensed);
    private static readonly Typeface LabelFace = new Typeface(CondensedFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Condensed);
    private static readonly Typeface MonoFace = new Typeface(MonoFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface MonoBoldFace = new Typeface(MonoFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Typeface BodyFace = new Typeface(BodyFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface GridLabelFace = new Typeface(new FontFamily("Bahnschrift SemiBold, Segoe UI Semibold, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    /// <summary>Prostokąt obszaru mapy w pikselach strony (zaokrąglony — ten sam rozmiar ma obraz podkładu).</summary>
    public static Int32Rect MapPixelRect(MapSheetLayout layout, double pxPerMm) => new Int32Rect(
        (int)Math.Round(layout.MapRect.X * pxPerMm), (int)Math.Round(layout.MapRect.Y * pxPerMm),
        Math.Max(1, (int)Math.Round(layout.MapRect.Width * pxPerMm)), Math.Max(1, (int)Math.Round(layout.MapRect.Height * pxPerMm)));

    public static BitmapSource Render(MapFramePage page)
    {
        var c = new FrameContext(page);
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, c.PagePxW, c.PagePxH));
            DrawMapContent(dc, c);
            if (c.S.DrawGrid) DrawGridLines(dc, c);
            if (c.S.DrawNeatLine) dc.DrawRectangle(null, c.NeatPen, c.Map);
            if (c.S.EdgeTicks) DrawEdgeTicks(dc, c);
            if (c.S.DrawGrid && c.LabelStyle != 2) DrawGridLabels(dc, c);
            DrawMarginTexts(dc, c);
            if (c.Layout.HasFooter) DrawFooter(dc, c);
        }
        var rtb = new RenderTargetBitmap(c.PagePxW, c.PagePxH, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    // ------------------------------------------------------------------ kontekst rysowania

    private sealed class GridLine
    {
        public double Value { get; init; }
        public bool IsMain { get; init; }
        public List<Point> Points { get; init; } = new List<Point>();
    }

    private sealed class FrameContext
    {
        public FrameContext(MapFramePage page)
        {
            Page = page;
            S = page.Settings;
            Layout = page.Layout;
            Facts = page.Facts;
            Texts = MapFrameTexts.For(S.PolishLabels);
            U = page.PxPerMm;
            PagePxW = Math.Max(1, (int)Math.Round(Layout.PageWidthMm * U));
            PagePxH = Math.Max(1, (int)Math.Round(Layout.PageHeightMm * U));
            var r = MapPixelRect(Layout, U);
            Map = new Rect(r.X, r.Y, r.Width, r.Height);
            LabelStyle = Math.Max(0, Array.IndexOf(MapPrintSettings.GridLabelStyles, S.GridLabelStyle));

            GridBrush = Freeze(new SolidColorBrush(ParseColor(S.GridColorHex, Colors.Black)));
            GridPen = Freeze(new Pen(GridBrush, Math.Max(0.6, S.GridLineWidthMm * U)));
            NeatPen = Freeze(new Pen(Brushes.Black, Math.Max(1.0, 0.3 * U)));
            ThinPen = Freeze(new Pen(Brushes.Black, Math.Max(0.6, 0.15 * U)));

            Zone = Facts.UtmZone > 0 ? Facts.UtmZone : MapGridMath.UtmZone(page.MapView.Center.Lon);
            Interval = Facts.GridIntervalMeters > 0 ? Facts.GridIntervalMeters : 1000.0;
            Tick = Interval / 10.0;

            double minE = double.MaxValue, maxE = double.MinValue, minN = double.MaxValue, maxN = double.MinValue;
            foreach (var (x, y) in new[] { (0.0, 0.0), (Map.Width, 0.0), (0.0, Map.Height), (Map.Width, Map.Height), (Map.Width / 2, 0.0), (Map.Width / 2, Map.Height), (0.0, Map.Height / 2), (Map.Width, Map.Height / 2) })
            {
                var (lat, lon) = page.MapView.ToLatLon(x, y);
                UtmGrid2DProjector.LatLonToUtm(lat, lon, Zone, out double e, out double n);
                minE = Math.Min(minE, e); maxE = Math.Max(maxE, e); minN = Math.Min(minN, n); maxN = Math.Max(maxN, n);
            }
            MinE = minE - Interval; MaxE = maxE + Interval; MinN = minN - Interval; MaxN = maxN + Interval;

            foreach (double e in MapGridMath.Multiples(MinE, MaxE, Tick))
                EastLines.Add(BuildLine(e, isEasting: true, IsMultiple(e, Interval)));
            foreach (double n in MapGridMath.Multiples(MinN, MaxN, Tick))
                NorthLines.Add(BuildLine(n, isEasting: false, IsMultiple(n, Interval)));
        }

        public MapFramePage Page { get; }
        public MapPrintSettings S { get; }
        public MapSheetLayout Layout { get; }
        public MapSheetFacts Facts { get; }
        public MapFrameTexts Texts { get; }
        public double U { get; }
        public int PagePxW { get; }
        public int PagePxH { get; }
        public Rect Map { get; }
        public int LabelStyle { get; }
        public Brush GridBrush { get; }
        public Pen GridPen { get; }
        public Pen NeatPen { get; }
        public Pen ThinPen { get; }
        public int Zone { get; }
        public double Interval { get; }
        public double Tick { get; }
        public double MinE { get; }
        public double MaxE { get; }
        public double MinN { get; }
        public double MaxN { get; }
        public List<GridLine> EastLines { get; } = new List<GridLine>();
        public List<GridLine> NorthLines { get; } = new List<GridLine>();

        public Point Project(double easting, double northing)
        {
            UtmGrid2DProjector.UtmToLatLon(easting, northing, Zone, out double lat, out double lon);
            var (x, y) = Page.MapView.ToPixel(lat, lon);
            return new Point(Map.X + x, Map.Y + y);
        }

        private GridLine BuildLine(double value, bool isEasting, bool isMain)
        {
            int segments = isMain ? 16 : 4;
            var pts = new List<Point>(segments + 1);
            for (int i = 0; i <= segments; i++)
            {
                double t = (double)i / segments;
                pts.Add(isEasting ? Project(value, MinN + (MaxN - MinN) * t) : Project(MinE + (MaxE - MinE) * t, value));
            }
            return new GridLine { Value = value, IsMain = isMain, Points = pts };
        }

        public string Label(double value) => LabelStyle == 1 ? MapGridMath.FullGridLabel(value) : MapGridMath.GridLineLabel(value, Interval);

        public double Mm(double millimeters) => millimeters * U;
    }

    private static bool IsMultiple(double value, double step) => Math.Abs(value / step - Math.Round(value / step)) < 1e-6;

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    private static Color ParseColor(string hex, Color fallback)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex) && ColorConverter.ConvertFromString(hex.Trim()) is Color c) return c;
        }
        catch (Exception ex) when (ex is FormatException || ex is NotSupportedException || ex is ArgumentException)
        {
            // Nieprawidłowy kolor w ustawieniach — zostaje kolor domyślny.
        }
        return fallback;
    }

    // ------------------------------------------------------------------ mapa i siatka

    private static void DrawMapContent(DrawingContext dc, FrameContext c)
    {
        dc.PushClip(new RectangleGeometry(c.Map));
        if (c.Page.Imagery != null) dc.DrawImage(c.Page.Imagery, c.Map);
        if (c.Page.Objects != null && c.Page.Objects.PixelWidth > 0)
        {
            double h = c.Page.Objects.PixelHeight * (c.Map.Width / c.Page.Objects.PixelWidth);
            dc.DrawImage(c.Page.Objects, new Rect(c.Map.X, c.Map.Y + (c.Map.Height - h) / 2.0, c.Map.Width, h));
        }
        dc.Pop();
    }

    private static Geometry Polyline(IReadOnlyList<Point> points)
    {
        var g = new StreamGeometry();
        using (StreamGeometryContext ctx = g.Open())
        {
            ctx.BeginFigure(points[0], false, false);
            ctx.PolyLineTo(points.Skip(1).ToList(), true, false);
        }
        g.Freeze();
        return g;
    }

    private static void DrawGridLines(DrawingContext dc, FrameContext c)
    {
        dc.PushClip(new RectangleGeometry(c.Map));
        foreach (var line in c.EastLines.Concat(c.NorthLines).Where(l => l.IsMain))
            dc.DrawGeometry(null, c.GridPen, Polyline(line.Points));

        double tickMm = c.Tick / c.Facts.ScaleDenominator * 1000.0;
        if (c.S.GridSubdivisionTicks && tickMm >= 1.5)
        {
            double half = c.Mm(0.6);
            foreach (var line in c.EastLines.Where(l => l.IsMain))
                foreach (double n in MapGridMath.Multiples(c.MinN, c.MaxN, c.Tick))
                    if (!IsMultiple(n, c.Interval)) DrawCrossTick(dc, c, c.Project(line.Value, n), c.Project(line.Value, n - c.Tick / 2), c.Project(line.Value, n + c.Tick / 2), half);
            foreach (var line in c.NorthLines.Where(l => l.IsMain))
                foreach (double e in MapGridMath.Multiples(c.MinE, c.MaxE, c.Tick))
                    if (!IsMultiple(e, c.Interval)) DrawCrossTick(dc, c, c.Project(e, line.Value), c.Project(e - c.Tick / 2, line.Value), c.Project(e + c.Tick / 2, line.Value), half);
        }
        dc.Pop();
    }

    private static void DrawCrossTick(DrawingContext dc, FrameContext c, Point p, Point before, Point after, double half)
    {
        if (!c.Map.Contains(p)) return;
        double tx = after.X - before.X, ty = after.Y - before.Y, len = Math.Sqrt(tx * tx + ty * ty);
        if (len < 1e-6) return;
        double nx = -ty / len * half, ny = tx / len * half;
        dc.DrawLine(c.GridPen, new Point(p.X - nx, p.Y - ny), new Point(p.X + nx, p.Y + ny));
    }

    private static bool TryCrossHorizontal(IReadOnlyList<Point> pts, double y, out double x)
    {
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            Point a = pts[i], b = pts[i + 1];
            if ((a.Y - y) * (b.Y - y) <= 0 && Math.Abs(b.Y - a.Y) > 1e-9)
            {
                x = a.X + (y - a.Y) / (b.Y - a.Y) * (b.X - a.X);
                return true;
            }
        }
        x = 0;
        return false;
    }

    private static bool TryCrossVertical(IReadOnlyList<Point> pts, double x, out double y)
    {
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            Point a = pts[i], b = pts[i + 1];
            if ((a.X - x) * (b.X - x) <= 0 && Math.Abs(b.X - a.X) > 1e-9)
            {
                y = a.Y + (x - a.X) / (b.X - a.X) * (b.Y - a.Y);
                return true;
            }
        }
        y = 0;
        return false;
    }

    /// <summary>Kreski na zewnątrz linii ramki: co 1/10 oczka krótkie, przy liniach siatki dłuższe.</summary>
    private static void DrawEdgeTicks(DrawingContext dc, FrameContext c)
    {
        double tickMm = c.Tick / c.Facts.ScaleDenominator * 1000.0;
        bool subdivisions = tickMm >= 1.5;
        Rect m = c.Map;
        foreach (var line in c.EastLines)
        {
            if (!line.IsMain && !subdivisions) continue;
            double len = c.Mm(line.IsMain ? 2.5 : 1.2);
            if (TryCrossHorizontal(line.Points, m.Top, out double xt) && xt >= m.Left && xt <= m.Right) dc.DrawLine(c.GridPen, new Point(xt, m.Top), new Point(xt, m.Top - len));
            if (TryCrossHorizontal(line.Points, m.Bottom, out double xb) && xb >= m.Left && xb <= m.Right) dc.DrawLine(c.GridPen, new Point(xb, m.Bottom), new Point(xb, m.Bottom + len));
        }
        foreach (var line in c.NorthLines)
        {
            if (!line.IsMain && !subdivisions) continue;
            double len = c.Mm(line.IsMain ? 2.5 : 1.2);
            if (TryCrossVertical(line.Points, m.Left, out double yl) && yl >= m.Top && yl <= m.Bottom) dc.DrawLine(c.GridPen, new Point(m.Left, yl), new Point(m.Left - len, yl));
            if (TryCrossVertical(line.Points, m.Right, out double yr) && yr >= m.Top && yr <= m.Bottom) dc.DrawLine(c.GridPen, new Point(m.Right, yr), new Point(m.Right + len, yr));
        }
    }

    private static void DrawGridLabels(DrawingContext dc, FrameContext c)
    {
        double em = c.Mm(c.S.GridLabelSizeMm);
        double inset = c.Mm(Math.Max(2.5, c.S.GridLabelSizeMm * 1.3));
        double cornerGap = c.Mm(c.S.GridLabelSizeMm * 2.2);
        var halo = Freeze(new Pen(Brushes.White, Math.Max(1.0, c.S.GridLabelSizeMm * 0.28 * c.U)) { LineJoin = PenLineJoin.Round });
        Rect m = c.Map;

        foreach (var line in c.EastLines.Where(l => l.IsMain))
        {
            string label = c.Label(line.Value);
            if (c.S.GridLabelsInside)
            {
                foreach (double y in new[] { m.Top + inset, m.Bottom - inset })
                    if (TryCrossHorizontal(line.Points, y, out double x) && x > m.Left + cornerGap && x < m.Right - cornerGap)
                        DrawHaloText(dc, label, em, c.GridBrush, halo, new Point(x, y), 0);
            }
            if (c.S.GridLabelsOutside && TryCrossHorizontal(line.Points, m.Top, out double xo) && xo > m.Left && xo < m.Right)
            {
                var ft = Text(label, GridLabelFace, em * 0.85, c.GridBrush);
                dc.DrawText(ft, new Point(xo - ft.WidthIncludingTrailingWhitespace / 2.0, m.Top - c.Mm(3.0) - ft.Height));
            }
        }

        foreach (var line in c.NorthLines.Where(l => l.IsMain))
        {
            string label = c.Label(line.Value);
            if (c.S.GridLabelsInside)
            {
                foreach (double x in new[] { m.Left + inset, m.Right - inset })
                    if (TryCrossVertical(line.Points, x, out double y) && y > m.Top + cornerGap && y < m.Bottom - cornerGap)
                        DrawHaloText(dc, label, em, c.GridBrush, halo, new Point(x, y), -90);
            }
            if (c.S.GridLabelsOutside && TryCrossVertical(line.Points, m.Left, out double yo) && yo > m.Top && yo < m.Bottom)
            {
                var ft = Text(label, GridLabelFace, em * 0.85, c.GridBrush);
                var anchor = new Point(m.Left - c.Mm(3.0) - ft.Height / 2.0, yo);
                dc.PushTransform(new RotateTransform(-90, anchor.X, anchor.Y));
                dc.DrawText(ft, new Point(anchor.X - ft.WidthIncludingTrailingWhitespace / 2.0, anchor.Y - ft.Height / 2.0));
                dc.Pop();
            }
        }
    }

    private static void DrawHaloText(DrawingContext dc, string text, double emPx, Brush brush, Pen halo, Point center, double angle)
    {
        var ft = Text(text, GridLabelFace, emPx, brush);
        double capHeight = GridLabelFace.CapsHeight * emPx;
        var origin = new Point(center.X - ft.WidthIncludingTrailingWhitespace / 2.0, center.Y - (ft.Baseline - capHeight / 2.0));
        Geometry geometry = ft.BuildGeometry(origin);
        if (Math.Abs(angle) > 0.01) dc.PushTransform(new RotateTransform(angle, center.X, center.Y));
        dc.DrawGeometry(null, halo, geometry);
        dc.DrawGeometry(brush, null, geometry);
        if (Math.Abs(angle) > 0.01) dc.Pop();
    }

    // ------------------------------------------------------------------ marginesy

    private static void DrawMarginTexts(DrawingContext dc, FrameContext c)
    {
        double em = c.Mm(2.1);
        if (c.S.ShowLeftMarginText && !string.IsNullOrWhiteSpace(c.Page.LeftMarginText) && c.Layout.LeftMarginRect.Width >= 3.0)
            DrawVerticalText(dc, c, c.Page.LeftMarginText, em, c.Mm(c.Layout.LeftMarginRect.CenterX));
        if (c.S.ShowRightMarginText && !string.IsNullOrWhiteSpace(c.Page.RightMarginText) && c.Layout.RightMarginRect.Width >= 3.0)
            DrawVerticalText(dc, c, c.Page.RightMarginText, em, c.Mm(c.Layout.RightMarginRect.CenterX));
    }

    /// <summary>Tekst czytany od dołu do góry, zaczynający się przy dolnej krawędzi mapy.</summary>
    private static void DrawVerticalText(DrawingContext dc, FrameContext c, string text, double emPx, double centerX)
    {
        var ft = Text(text, BodyFace, emPx, Brushes.Black);
        ft.MaxTextWidth = Math.Max(10.0, c.Map.Height);
        ft.MaxLineCount = 1;
        ft.Trimming = TextTrimming.CharacterEllipsis;
        var anchor = new Point(centerX, c.Map.Bottom);
        dc.PushTransform(new RotateTransform(-90, anchor.X, anchor.Y));
        dc.DrawText(ft, new Point(anchor.X, anchor.Y - ft.Height / 2.0));
        dc.Pop();
    }

    // ------------------------------------------------------------------ stopka

    private static void DrawFooter(DrawingContext dc, FrameContext c)
    {
        RectMm f = c.Layout.FooterRect;
        double right = f.Right;

        if (!string.IsNullOrWhiteSpace(c.Page.QrPayload))
        {
            double size = Math.Min(14.5, f.Height - 1.0);
            try
            {
                QrService.DrawQr(dc, c.Page.QrPayload!, c.Mm(right - size), c.Mm(f.Y), c.Mm(size));
                right -= size + 1.8;
            }
            catch (InvalidOperationException)
            {
                // Treść za długa na kod QR — stopka bez kodu.
            }
        }

        if (c.S.ShowDeclination) right = DrawDeclinationBlock(dc, c, right) - 2.0;
        double left = DrawTitleBlock(dc, c, f.X + f.Width * 0.42) + 3.5;
        if (right - left > 20.0) DrawInfoAndScaleBars(dc, c, left, right);
    }

    /// <summary>Logo, nazwa arkusza i wyrównane do prawej linie: podtytuł, adres, informacja o deklinacji. Zwraca prawą krawędź (mm).</summary>
    private static double DrawTitleBlock(DrawingContext dc, FrameContext c, double maxRight)
    {
        RectMm f = c.Layout.FooterRect;
        double x = f.X;
        if (c.Page.Logo != null && c.Page.Logo.Height > 0)
        {
            double h = 7.4, w = Math.Min(h * 2.0, h * c.Page.Logo.Width / c.Page.Logo.Height);
            dc.DrawImage(c.Page.Logo, new Rect(c.Mm(x), c.Mm(f.Y + 0.2), c.Mm(w), c.Mm(h)));
            x += w + 1.2;
        }

        double titleRight = x;
        if (!string.IsNullOrWhiteSpace(c.Page.Title))
        {
            double em = 8.0;
            var ft = Text(c.Page.Title, TitleFace, c.Mm(em), Brushes.Black);
            double available = Math.Max(10.0, maxRight - x);
            if (ft.WidthIncludingTrailingWhitespace > c.Mm(available))
                ft = Text(c.Page.Title, TitleFace, c.Mm(em * available / (ft.WidthIncludingTrailingWhitespace / c.U)), Brushes.Black);
            DrawAtBaseline(dc, ft, c.Mm(x), c.Mm(f.Y + 6.9));
            titleRight = x + ft.WidthIncludingTrailingWhitespace / c.U;
        }

        var lines = new List<(string Text, Typeface Face, double Em, double Baseline)>();
        if (!string.IsNullOrWhiteSpace(c.Page.Subtitle)) lines.Add((c.Page.Subtitle, LabelFace, 2.9, 10.9));
        if (!string.IsNullOrWhiteSpace(c.S.UrlLine)) lines.Add((c.S.UrlLine.Trim(), BodyFace, 2.0, 14.2));
        if (c.S.ShowGeneratedLine)
        {
            string generated = string.Format(CultureInfo.InvariantCulture, c.Texts.GeneratedFormat, GeoMagneticModel.ModelName, c.Facts.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(c.Page.PageNote)) generated += " · " + c.Page.PageNote;
            lines.Add((generated, BodyFace, 2.0, 17.0));
        }

        double maxWidth = Math.Max(10.0, maxRight - f.X);
        double blockRight = titleRight;
        foreach (var line in lines)
            blockRight = Math.Max(blockRight, f.X + Math.Min(maxWidth, Measure(c, line.Text, line.Face, line.Em)));
        blockRight = Math.Min(blockRight, Math.Max(titleRight, maxRight));

        foreach (var line in lines)
        {
            var ft = Text(line.Text, line.Face, c.Mm(line.Em), Brushes.Black);
            ft.MaxTextWidth = c.Mm(blockRight - f.X);
            ft.MaxLineCount = 1;
            ft.Trimming = TextTrimming.CharacterEllipsis;
            DrawAtBaseline(dc, ft, c.Mm(blockRight) - ft.WidthIncludingTrailingWhitespace, c.Mm(f.Y + line.Baseline));
        }
        return blockRight;
    }

    private static void DrawInfoAndScaleBars(DrawingContext dc, FrameContext c, double left, double right)
    {
        RectMm f = c.Layout.FooterRect;
        var fields = new List<(string Label, string Value)>();
        if (c.S.ShowInfoScale) fields.Add((c.Texts.Scale, MapGridMath.FormatScale(c.Facts.ScaleDenominator, c.S.PolishLabels)));
        if (c.S.ShowInfoCrs) fields.Add((c.Texts.Crs, "WGS84"));
        if (c.S.ShowInfoSouthWest)
        {
            fields.Add((c.Texts.SouthWestLat, c.Facts.SouthWestLat.ToString("0.0000", CultureInfo.InvariantCulture)));
            fields.Add((c.Texts.SouthWestLon, c.Facts.SouthWestLon.ToString("0.0000", CultureInfo.InvariantCulture)));
        }
        string custom1 = string.IsNullOrWhiteSpace(c.S.Custom1Value) ? c.Page.BasemapName : c.S.Custom1Value.Trim();
        if (!string.IsNullOrWhiteSpace(c.S.Custom1Label) && !string.IsNullOrWhiteSpace(custom1)) fields.Add((c.S.Custom1Label.Trim(), custom1));
        if (!string.IsNullOrWhiteSpace(c.S.Custom2Label) && !string.IsNullOrWhiteSpace(c.S.Custom2Value)) fields.Add((c.S.Custom2Label.Trim(), c.S.Custom2Value.Trim()));

        double x = left;
        foreach (var (label, value) in fields)
        {
            double available = right - x;
            if (available < 10.0) break;
            double width = Math.Max(Measure(c, label, LabelFace, 2.4), Measure(c, value, MonoFace, 2.6));
            var labelText = Text(label, LabelFace, c.Mm(2.4), Brushes.Black);
            var valueText = Text(value, MonoFace, c.Mm(2.6), Brushes.Black);
            if (width > available)
            {
                foreach (var ft in new[] { labelText, valueText })
                {
                    ft.MaxTextWidth = c.Mm(available);
                    ft.MaxLineCount = 1;
                    ft.Trimming = TextTrimming.CharacterEllipsis;
                }
                width = available;
            }
            DrawAtBaseline(dc, labelText, c.Mm(x), c.Mm(f.Y + 2.3));
            DrawAtBaseline(dc, valueText, c.Mm(x), c.Mm(f.Y + 5.5));
            x += width + 3.2;
        }

        // Jednostka za ostatnią kreską („2.5 km”) może wejść nad blok PT/PM — na tej wysokości jest tam wolne miejsce.
        double unitRoom = Measure(c, "2.5", MonoFace, 2.3) / 2.0 + Measure(c, " km", MonoFace, 2.3) + 0.6;
        double maxLength = Math.Min(100.0, right - left - unitRoom + 4.0);
        if (maxLength < 20.0) return;
        if (c.S.ShowKmBar)
        {
            var km = MapGridMath.NiceScaleBar(c.Facts.ScaleDenominator, 1000.0, maxLength);
            DrawScaleBar(dc, c, left, f.Y + 9.4, km, "km", labelsAbove: true);
            if (c.S.ShowScaleCheckNote)
            {
                string note = string.Format(CultureInfo.InvariantCulture, c.Texts.ScaleCheckFormat, MapGridMath.FormatUnits(Math.Round(km.LengthMm, 1)));
                var ft = Text(note, BodyFace, c.Mm(2.1), Brushes.Black);
                DrawAtBaseline(dc, ft, c.Mm(left + km.LengthMm) - ft.WidthIncludingTrailingWhitespace, c.Mm(f.Y + 13.4));
            }
        }
        if (c.S.ShowMileBar)
        {
            var mi = MapGridMath.NiceScaleBar(c.Facts.ScaleDenominator, 1609.344, maxLength);
            DrawScaleBar(dc, c, left, f.Y + 14.6, mi, "mi", labelsAbove: false);
        }
    }

    /// <summary>Podziałka jak na arkuszach T.REX: pierwszy odcinek podzielony na 5, dalej odcinki naprzemiennie biało-czarne.</summary>
    private static void DrawScaleBar(DrawingContext dc, FrameContext c, double x0, double top, MapGridMath.ScaleBarSpec spec, string unit, bool labelsAbove)
    {
        const double height = 1.6;
        double mmPerUnit = spec.LengthMm / spec.TotalUnits;
        int majors = Math.Max(1, (int)Math.Round(spec.TotalUnits / spec.MajorStepUnits));
        for (int i = 0; i < majors; i++)
        {
            if (i == 0)
            {
                for (int j = 0; j < spec.MinorDivisions; j++)
                {
                    double xa = x0 + j * spec.MajorStepUnits / spec.MinorDivisions * mmPerUnit;
                    double xb = x0 + (j + 1) * spec.MajorStepUnits / spec.MinorDivisions * mmPerUnit;
                    dc.DrawRectangle(j % 2 == 0 ? Brushes.Black : Brushes.White, null, new Rect(c.Mm(xa), c.Mm(top), c.Mm(xb - xa), c.Mm(height)));
                }
            }
            else
            {
                double xa = x0 + i * spec.MajorStepUnits * mmPerUnit;
                dc.DrawRectangle(i % 2 == 1 ? Brushes.White : Brushes.Black, null, new Rect(c.Mm(xa), c.Mm(top), c.Mm(spec.MajorStepUnits * mmPerUnit), c.Mm(height)));
            }
        }
        dc.DrawRectangle(null, c.ThinPen, new Rect(c.Mm(x0), c.Mm(top), c.Mm(spec.LengthMm), c.Mm(height)));

        double baseline = labelsAbove ? top - 0.6 : top + height + 2.6;
        for (int i = 0; i <= majors; i++)
        {
            double value = i * spec.MajorStepUnits;
            double xs = x0 + value * mmPerUnit;
            var ft = Text(MapGridMath.FormatUnits(value), MonoFace, c.Mm(2.3), Brushes.Black);
            DrawAtBaseline(dc, ft, c.Mm(xs) - ft.WidthIncludingTrailingWhitespace / 2.0, c.Mm(baseline));
            if (i == majors)
            {
                var unitText = Text(unit, MonoFace, c.Mm(2.3), Brushes.Black);
                DrawAtBaseline(dc, unitText, c.Mm(xs + 0.6) + ft.WidthIncludingTrailingWhitespace / 2.0, c.Mm(baseline));
            }
        }
    }

    /// <summary>Kąt PT–PM, linie PT/PM/zmiana roczna i diagram (PG pionowo, PT i PM z przesadzonym kątem). Zwraca lewą krawędź (mm).</summary>
    private static double DrawDeclinationBlock(DrawingContext dc, FrameContext c, double right)
    {
        RectMm f = c.Layout.FooterRect;
        MapFrameTexts t = c.Texts;
        double gamma = c.Facts.ConvergenceDeg, declination = c.Facts.Magnetic.DeclinationDeg;
        const double length = 14.5, labelEm = 2.2;

        var arrows = new[] { (Angle: 0.0, Label: "", Half: false), (Angle: Exaggerate(gamma), Label: t.GridNorth, Half: false), (Angle: Exaggerate(declination), Label: t.MagneticNorth, Half: true) };
        double labelHalf = Math.Max(Measure(c, t.GridNorth, LabelFace, labelEm), Measure(c, t.MagneticNorth, LabelFace, labelEm)) / 2.0 + 0.3;
        double leftExtent = labelHalf, rightExtent = labelHalf;
        foreach (var a in arrows)
        {
            double dx = Math.Sin(a.Angle * Math.PI / 180.0) * length;
            leftExtent = Math.Max(leftExtent, -dx + labelHalf);
            rightExtent = Math.Max(rightExtent, dx + labelHalf);
        }
        double originX = right - rightExtent, originY = f.Y + 20.0;

        foreach (var a in arrows)
        {
            double r = a.Angle * Math.PI / 180.0;
            double dirX = Math.Sin(r), dirY = -Math.Cos(r);
            var origin = new Point(c.Mm(originX), c.Mm(originY));
            var tip = new Point(c.Mm(originX + dirX * length), c.Mm(originY + dirY * length));
            dc.DrawLine(c.ThinPen, origin, tip);
            if (string.IsNullOrEmpty(a.Label))
            {
                DrawStar(dc, new Point(tip.X, tip.Y - c.Mm(0.9)), c.Mm(1.0));
                continue;
            }

            double headLength = c.Mm(1.4), headWidth = c.Mm(0.6);
            var basePoint = new Point(tip.X - dirX * headLength, tip.Y - dirY * headLength);
            double perpX = -dirY, perpY = dirX;                 // prostopadła w prawo od kierunku strzałki
            double side = a.Angle < 0 ? -1.0 : 1.0;             // półgrot po zewnętrznej stronie
            var head = new StreamGeometry();
            using (StreamGeometryContext g = head.Open())
            {
                g.BeginFigure(tip, true, true);
                if (a.Half)
                {
                    g.LineTo(new Point(basePoint.X + perpX * headWidth * side, basePoint.Y + perpY * headWidth * side), true, false);
                    g.LineTo(basePoint, true, false);
                }
                else
                {
                    g.LineTo(new Point(basePoint.X + perpX * headWidth, basePoint.Y + perpY * headWidth), true, false);
                    g.LineTo(new Point(basePoint.X - perpX * headWidth, basePoint.Y - perpY * headWidth), true, false);
                }
            }
            head.Freeze();
            dc.DrawGeometry(Brushes.Black, null, head);

            var label = Text(a.Label, LabelFace, c.Mm(labelEm), Brushes.Black);
            DrawAtBaseline(dc, label, tip.X - label.WidthIncludingTrailingWhitespace / 2.0, tip.Y - c.Mm(0.5));
        }

        var lines = new List<string>
        {
            $"{t.GridNorth} {MapGridMath.FormatEastWest(gamma)}",
            $"{t.MagneticNorth} {MapGridMath.FormatEastWest(declination)}",
            MapGridMath.FormatEastWest(c.Facts.Magnetic.AnnualChangeDeg, 2) + t.PerYear,
            t.NotToScale
        };
        if (!c.Facts.MagneticModelValid) lines.Add(t.ModelExpired);
        string angle = MapGridMath.FormatEastWest(c.Facts.GridMagneticAngleDeg);
        double textWidth = Math.Max(Measure(c, t.GmAngle, LabelFace, 2.4), Measure(c, angle, MonoBoldFace, 2.9));
        foreach (string line in lines) textWidth = Math.Max(textWidth, Measure(c, line, BodyFace, 2.1));
        double x = originX - leftExtent - 1.2 - textWidth;

        DrawAtBaseline(dc, Text(t.GmAngle, LabelFace, c.Mm(2.4), Brushes.Black), c.Mm(x), c.Mm(f.Y + 2.3));
        DrawAtBaseline(dc, Text(angle, MonoBoldFace, c.Mm(2.9), Brushes.Black), c.Mm(x), c.Mm(f.Y + 5.6));
        for (int i = 0; i < lines.Count; i++)
            DrawAtBaseline(dc, Text(lines[i], BodyFace, c.Mm(2.1), Brushes.Black), c.Mm(x), c.Mm(f.Y + 12.0 + 2.5 * i));
        return x;
    }

    /// <summary>Kąt na diagramie „nie w skali”: małe kąty powiększone, kolejność i strona zachowane.</summary>
    internal static double Exaggerate(double degrees) =>
        Math.Abs(degrees) < 0.05 ? 0.0 : Math.Sign(degrees) * Math.Min(12.0, 3.0 + 2.5 * Math.Pow(Math.Abs(degrees), 0.8));

    private static void DrawStar(DrawingContext dc, Point center, double radius)
    {
        var star = new StreamGeometry();
        using (StreamGeometryContext g = star.Open())
        {
            for (int i = 0; i < 10; i++)
            {
                double r = i % 2 == 0 ? radius : radius * 0.42;
                double a = -Math.PI / 2.0 + i * Math.PI / 5.0;
                var p = new Point(center.X + r * Math.Cos(a), center.Y + r * Math.Sin(a));
                if (i == 0) g.BeginFigure(p, true, true);
                else g.LineTo(p, true, false);
            }
        }
        star.Freeze();
        dc.DrawGeometry(Brushes.Black, null, star);
    }

    // ------------------------------------------------------------------ tekst

    private static FormattedText Text(string text, Typeface face, double emPx, Brush brush) =>
        new FormattedText(text ?? "", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, Math.Max(0.5, emPx), brush, 1.0);

    private static double Measure(FrameContext c, string text, Typeface face, double emMm) =>
        Text(text, face, c.Mm(emMm), Brushes.Black).WidthIncludingTrailingWhitespace / c.U;

    private static void DrawAtBaseline(DrawingContext dc, FormattedText ft, double x, double baselineY) =>
        dc.DrawText(ft, new Point(x, baselineY - ft.Baseline));
}
