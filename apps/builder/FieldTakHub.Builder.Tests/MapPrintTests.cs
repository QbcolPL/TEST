using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FieldTakHub.Builder.Services;
using FieldTakHub.Builder.Services.MapImagery;
using FieldTakHub.Builder.Services.MapPrint;
using Xunit;

namespace FieldTakHub.Builder.Tests;

/// <summary>Testy obliczeń wydruku z ramką: WMM2025, siatka MGRS, podziałki, układ strony, PDF i mozaika kafelków.</summary>
public class MapPrintTests
{
    // Wartości wzorcowe z WMM2025_TestValues.txt (NOAA NCEI): rok, wysokość km, szer., dł., D, I, dD/dt.
    [Theory]
    [InlineData(2025.0, 28, 89, -121, -99.77, 88.47, 2.491706)]
    [InlineData(2025.5, 6, -36, -137, 20.28, -52.11, 0.025348)]
    [InlineData(2026.5, 14, 0, 80, -3.10, -17.15, 0.056431)]
    [InlineData(2027.5, 8, 62, 53, 19.39, 76.67, 0.099601)]
    [InlineData(2028.5, 28, 54, -120, 15.43, 73.74, -0.147623)]
    [InlineData(2029.5, 31, 13, -132, 9.04, 31.41, -0.039071)]
    public void Wmm2025_MatchesOfficialTestValues(double year, double altKm, double lat, double lon, double decl, double incl, double declPerYear)
    {
        var r = GeoMagneticModel.Compute(lat, lon, altKm, year);
        Assert.InRange(r.DeclinationDeg, decl - 0.02, decl + 0.02);
        Assert.InRange(r.InclinationDeg, incl - 0.02, incl + 0.02);
        Assert.InRange(r.AnnualChangeDeg, declPerYear - 0.002, declPerYear + 0.002);
    }

    [Fact]
    public void Wmm2025_ReproducesTrexSheetDeclination()
    {
        // Arkusz 16SDE 34 67 (Tennessee): „MN 3.6° W”, „0.06° W per year” w listopadzie 2026.
        var r = GeoMagneticModel.Compute(35.87, -87.71, 0.2, GeoMagneticModel.DecimalYear(new DateTime(2026, 11, 24)));
        Assert.Equal("3.6° W", MapGridMath.FormatEastWest(r.DeclinationDeg));
        Assert.Equal("0.06° W", MapGridMath.FormatEastWest(r.AnnualChangeDeg, 2));
        Assert.True(GeoMagneticModel.IsValidFor(2026.9));
        Assert.False(GeoMagneticModel.IsValidFor(2030.2));
    }

    [Fact]
    public void DecimalYear_CountsDaysOfYear()
    {
        Assert.Equal(2026.0, GeoMagneticModel.DecimalYear(new DateTime(2026, 1, 1)), 6);
        Assert.Equal(2026.0 + 181.0 / 365.0, GeoMagneticModel.DecimalYear(new DateTime(2026, 7, 1)), 6);
    }

    [Fact]
    public void GridMath_UtmZoneConvergenceAndBands()
    {
        Assert.Equal(34, MapGridMath.UtmZone(21.0122));
        Assert.Equal(33, MapGridMath.UtmZone(17.99));
        Assert.Equal(16, MapGridMath.UtmZone(-87.73));
        Assert.Equal(21.0, MapGridMath.CentralMeridian(34));
        Assert.Equal(-87.0, MapGridMath.CentralMeridian(16));
        Assert.InRange(MapGridMath.GridConvergenceDeg(35.8450, -87.7309, 16), -0.429, -0.427);
        Assert.InRange(MapGridMath.GridConvergenceDeg(49.9135, 19.0238, 34), -1.513, -1.511);
        Assert.Equal(0.0, MapGridMath.GridConvergenceDeg(52.0, 21.0, 34), 9);
        Assert.Equal('U', MapGridMath.LatitudeBand(52.23));
        Assert.Equal('S', MapGridMath.LatitudeBand(35.85));
    }

    [Fact]
    public void GridMath_SheetIdsMatchMgrs()
    {
        UtmGrid2DProjector.UtmToLatLon(434000.5, 3967000.5, 16, out double lat, out double lon);
        Assert.Equal("16SDE 34 67", MapGridMath.SheetIdFromSouthWest(lat, lon));
        Assert.Equal("34UEC 00 86", MapGridMath.SheetIdFromSouthWest(52.2297, 21.0122));
        Assert.Equal("34UCA 58 30", MapGridMath.SheetIdFromSouthWest(49.9135, 19.0238));
        Assert.Equal("34U EC 00833 86586", MapGridMath.FullMgrs(52.2297, 21.0122));
    }

    [Fact]
    public void GridMath_LabelsIntervalsAndFormats()
    {
        Assert.Equal("35", MapGridMath.GridLineLabel(535000, 1000));
        Assert.Equal("72", MapGridMath.GridLineLabel(3972000, 1000));
        Assert.Equal("355", MapGridMath.GridLineLabel(5835500, 500));
        Assert.Equal(1000, MapGridMath.AutoGridInterval(25000));
        Assert.Equal(1000, MapGridMath.AutoGridInterval(50000));
        Assert.Equal(500, MapGridMath.AutoGridInterval(10000));
        Assert.Equal(200, MapGridMath.AutoGridInterval(5000));
        Assert.Equal(100, MapGridMath.AutoGridInterval(2500));
        Assert.Equal("3.6° W", MapGridMath.FormatEastWest(-3.573));
        Assert.Equal("6.9° E", MapGridMath.FormatEastWest(6.94));
        Assert.Equal("0.0°", MapGridMath.FormatEastWest(-0.02));
        Assert.Equal("1:25,000", MapGridMath.FormatScale(25000, false));
        Assert.Equal("1:25 000", MapGridMath.FormatScale(25000, true));
        Assert.Equal("535 000", MapGridMath.FullGridLabel(535000.2));
        Assert.Equal("PD-ZACH", MapGridMath.CompassPoint(225, true));
        Assert.Equal("S", MapGridMath.CompassPoint(181, false));
        Assert.Equal(new[] { 1000.0, 2000.0, 3000.0 }, MapGridMath.Multiples(950, 3000, 1000).ToArray());
    }

    [Fact]
    public void ScaleBars_MatchTrexSheetAt25k()
    {
        var km = MapGridMath.NiceScaleBar(25000, 1000.0, 100.0);
        Assert.Equal(2.5, km.TotalUnits, 9);
        Assert.Equal(0.5, km.MajorStepUnits, 9);
        Assert.Equal(100.0, km.LengthMm, 6);

        var mi = MapGridMath.NiceScaleBar(25000, 1609.344, 100.0);
        Assert.Equal(1.5, mi.TotalUnits, 9);
        Assert.Equal(0.5, mi.MajorStepUnits, 9);
        Assert.InRange(mi.LengthMm, 96.5, 96.6);

        var small = MapGridMath.NiceScaleBar(10000, 1000.0, 100.0);
        Assert.Equal(1.0, small.TotalUnits, 9);
        Assert.Equal(0.2, small.MajorStepUnits, 9);
        Assert.Equal("2.5", MapGridMath.FormatUnits(2.5));
        Assert.Equal("1", MapGridMath.FormatUnits(1.0));
    }

    [Fact]
    public void SheetLayout_SplitsPageIntoMapFooterAndMargins()
    {
        var s = new MapPrintSettings();
        var layout = MapSheetLayout.Compute(s);
        Assert.Equal(210.0, layout.PageWidthMm);
        Assert.Equal(297.0, layout.PageHeightMm);
        Assert.Equal(new RectMm(8, 7, 195, 257), layout.MapRect);
        Assert.Equal(267.0, layout.FooterRect.Y, 9);
        Assert.Equal(24.0, layout.FooterRect.Height, 9);
        Assert.Equal(8.0, layout.LeftMarginRect.Width, 9);
        Assert.Equal(7.0, layout.RightMarginRect.Width, 9);
        var ground = layout.GroundSize(25000);
        Assert.Equal(4875.0, ground.WidthMeters, 6);
        Assert.Equal(6425.0, ground.HeightMeters, 6);

        s.Landscape = true;
        s.ShowFooter = false;
        var land = MapSheetLayout.Compute(s);
        Assert.Equal(297.0, land.PageWidthMm);
        Assert.Equal(210.0 - 7 - 6, land.MapRect.Height, 9);
        Assert.False(land.HasFooter);

        Assert.Equal((215.9, 279.4), MapSheetLayout.PaperSizeMm("Letter", 0, 0, false));
        Assert.Equal((420.0, 297.0), MapSheetLayout.PaperSizeMm("A3", 0, 0, true));
    }

    [Fact]
    public void PrintSettings_RoundTripAndNormalize()
    {
        string path = Path.Combine(Path.GetTempPath(), "fth-print-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var s = new MapPrintSettings { PaperSize = "A3", Landscape = true, Subtitle = "Rejon: Łęczyca", SeriesColumns = 3, GridIntervalMeters = 500 };
            Assert.Null(s.Save(path));
            var back = MapPrintSettings.Load(path);
            Assert.Equal("A3", back.PaperSize);
            Assert.True(back.Landscape);
            Assert.Equal("Rejon: Łęczyca", back.Subtitle);
            Assert.Equal(3, back.SeriesColumns);
            Assert.Equal(500, back.GridIntervalMeters);

            File.WriteAllText(path, "{ to nie jest json");
            Assert.Equal("A4", MapPrintSettings.Load(path).PaperSize);
        }
        finally { File.Delete(path); }

        var bad = new MapPrintSettings { PaperSize = "B0", Dpi = 1200, GridIntervalMeters = 333, SeriesRows = 40, FooterHeightMm = double.NaN }.Normalize();
        Assert.Equal("A4", bad.PaperSize);
        Assert.Equal(300, bad.Dpi);
        Assert.Equal(0, bad.GridIntervalMeters);
        Assert.Equal(6, bad.SeriesRows);
        Assert.Equal(27.0, bad.FooterHeightMm);
    }

    [Fact]
    public void PdfWriter_ProducesValidMultiPageStructure()
    {
        byte[] fakeJpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 0xFF, 0xD9 };
        var pages = new[]
        {
            new PdfImagePage(fakeJpeg, 10, 20, 210, 297),
            new PdfImagePage(fakeJpeg, 20, 10, 297, 210)
        };
        using var ms = new MemoryStream();
        PdfImageDocumentWriter.Write(ms, pages, "Mapa – Łódź", "Field TAK Hub", new DateTime(2026, 11, 24, 12, 0, 0));
        byte[] bytes = ms.ToArray();
        string text = Encoding.Latin1.GetString(bytes);

        Assert.StartsWith("%PDF-1.4\n", text);
        Assert.Contains("/Count 2", text);
        Assert.Contains("/MediaBox [0 0 595.28 841.89]", text);
        Assert.Contains("/MediaBox [0 0 841.89 595.28]", text);
        Assert.Contains("/CreationDate (D:20261124120000)", text);
        Assert.EndsWith("%%EOF\n", text);

        int startxref = int.Parse(Regex.Match(text, @"startxref\n(\d+)\n").Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.StartsWith("xref\n0 10\n", text.Substring(startxref));
        var entries = Regex.Matches(text.Substring(startxref), @"(\d{10}) 00000 n \n");
        Assert.Equal(9, entries.Count);
        for (int i = 0; i < entries.Count; i++)
        {
            int offset = int.Parse(entries[i].Groups[1].Value, CultureInfo.InvariantCulture);
            Assert.StartsWith($"{i + 1} 0 obj\n", text.Substring(offset));
        }

        Assert.Throws<ArgumentException>(() => PdfImageDocumentWriter.Write(new MemoryStream(), new[] { new PdfImagePage(new byte[] { 1, 2, 3, 4 }, 1, 1, 10, 10) }, "x", "y", DateTime.Now));
        Assert.Equal("<FEFF0141>", PdfImageDocumentWriter.PdfTextString("Ł"));
    }

    [Fact]
    public void Viewport_RoundTripsAndPicksTileZoom()
    {
        var view = MercatorViewport.FromCenter(52.2297, 21.0122, 16, 290.0, 900, 700);
        var (x, y) = view.ToPixel(52.2297, 21.0122);
        Assert.Equal(450.0, x, 6);
        Assert.Equal(350.0, y, 6);
        var (lat, lon) = view.ToLatLon(123.0, 456.0);
        var (x2, y2) = view.ToPixel(lat, lon);
        Assert.Equal(123.0, x2, 6);
        Assert.Equal(456.0, y2, 6);

        Assert.Equal(16, view.TileZoomFor(256, 0, 19, roundUp: false));
        Assert.Equal(17, view.TileZoomFor(256, 0, 19, roundUp: true));
        Assert.Equal(16, view.TileZoomFor(512, 0, 19, roundUp: true));
        var print = view.Scaled(2.6);
        Assert.Equal(18, print.TileZoomFor(256, 0, 19, roundUp: true));
        Assert.Equal(17, print.TileZoomFor(256, 0, 17, roundUp: true));

        var shifted = view.Shifted(100, -50);
        var (sx, sy) = shifted.ToPixel(view.ToLatLon(550, 300).Lat, view.ToLatLon(550, 300).Lon);
        Assert.Equal(450.0, sx, 6);
        Assert.Equal(350.0, sy, 6);
    }

    [Fact]
    public void Mosaic_PlacesTilesAtTheirMercatorPosition()
    {
        // Widok 200×200 px na środku styku czterech kafelków poziomu 2 (100 px na kafelek).
        var view = new MercatorViewport(2, 1.0, 1.0, 100.0, 200, 200);
        var range = view.TileRange(2);
        Assert.Equal((0, 0, 1, 1), range);

        var grid = new TileGrid(2, range.X0, range.Y0, range.X1, range.Y1, 64);
        grid.Set(0, 0, Solid(64, 255, 0, 0));
        grid.Set(1, 0, Solid(64, 0, 255, 0));
        grid.Set(0, 1, Solid(64, 0, 0, 255));
        grid.Set(1, 1, Solid(128, 255, 255, 255));   // inny rozmiar — zostanie przeskalowany
        Assert.Equal(4, grid.LoadedCount);

        var img = MercatorTileMosaic.Render(view, grid);
        Assert.Equal(200, img.Width);
        Assert.Equal((255, 0, 0, 255), Px(img, 50, 50));
        Assert.Equal((0, 255, 0, 255), Px(img, 150, 50));
        Assert.Equal((0, 0, 255, 255), Px(img, 50, 150));
        Assert.Equal((255, 255, 255, 255), Px(img, 150, 150));
        Assert.Equal((255, 0, 0, 255), Px(img, 98, 98));

        var partial = new TileGrid(2, 0, 0, 1, 1, 64);
        partial.Set(0, 0, Solid(64, 10, 20, 30));
        var holes = MercatorTileMosaic.Render(view, partial);
        Assert.Equal(0, Px(holes, 150, 150).A);
        Assert.Equal((10, 20, 30, 255), Px(holes, 40, 40));
    }

    [Fact]
    public void PixelOps_FillNoDataBlendAndDarken()
    {
        var hi = Solid(4, 250, 250, 250);
        hi.Pixels[0] = 30; hi.Pixels[1] = 20; hi.Pixels[2] = 10;   // BGRA: piksel (0,0) = R10 G20 B30
        var merged = MercatorTileMosaic.FillNoDataFrom(hi, Solid(2, 1, 2, 3));
        Assert.Equal((10, 20, 30, 255), Px(merged, 0, 0));
        Assert.Equal((1, 2, 3, 255), Px(merged, 3, 3));
        Assert.Equal(1.0, MercatorTileMosaic.NoDataRatio(Solid(8, 255, 255, 255)));

        var baseImg = Solid(2, 200, 100, 0);
        var overlay = Solid(2, 0, 0, 0);
        MercatorTileMosaic.BlendOver(baseImg, overlay, 0.5);
        Assert.Equal((100, 50, 0, 255), Px(baseImg, 1, 1));
        MercatorTileMosaic.Darken(baseImg, 0.5);
        Assert.Equal((50, 25, 0, 255), Px(baseImg, 0, 0));
    }

    [Fact]
    public void SheetFacts_ReproduceTrexMarginalInformation()
    {
        // Widok, którego lewy dolny róg leży w narożniku kwadratu 16SDE 34 67 (arkusz T.REX, listopad 2026).
        UtmGrid2DProjector.UtmToLatLon(434000.5, 3967000.5, 16, out double swLat, out double swLon);
        var view = MercatorViewport.FromCenter(swLat, swLon, 16, 300.0, 1000, 1200).Shifted(500, -600);
        var facts = MapSheetFacts.Compute(view, 25000, 0, new DateTime(2026, 11, 24));

        Assert.Equal("16SDE 34 67", facts.SheetId);
        Assert.Equal(16, facts.UtmZone);
        Assert.Equal(1000, facts.GridIntervalMeters);
        Assert.Equal("0.4° W", MapGridMath.FormatEastWest(facts.ConvergenceDeg));
        Assert.Equal("3.6° W", MapGridMath.FormatEastWest(facts.Magnetic.DeclinationDeg));
        Assert.Equal("3.2° W", MapGridMath.FormatEastWest(facts.GridMagneticAngleDeg));
        Assert.True(facts.MagneticModelValid);
        Assert.InRange(facts.SouthWestLat, swLat - 1e-6, swLat + 1e-6);

        Assert.StartsWith("geo:35.8", facts.QrPayload(MapPrintSettings.QrModes[0], ""));
        Assert.StartsWith("16S DE", facts.QrPayload(MapPrintSettings.QrModes[3], ""));
        Assert.Equal("abc", facts.QrPayload("Własny tekst", " abc "));
        Assert.Null(facts.QrPayload("Własny tekst", ""));
        Assert.Null(facts.QrPayload("Brak", "x"));
    }

    [Fact]
    public void NorthDiagram_ExaggeratesAnglesButKeepsOrderAndSide()
    {
        Assert.Equal(0.0, MapFrameRenderer.Exaggerate(0.01));
        double gn = MapFrameRenderer.Exaggerate(-0.4), mn = MapFrameRenderer.Exaggerate(-3.6);
        Assert.True(gn < 0 && mn < gn);
        Assert.True(MapFrameRenderer.Exaggerate(6.2) > 0);
        Assert.Equal(12.0, MapFrameRenderer.Exaggerate(45.0));
    }

    [Fact]
    public void DiskCacheTrim_RemovesOldestFilesFirst()
    {
        string dir = Path.Combine(Path.GetTempPath(), "fth-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            for (int i = 0; i < 5; i++)
            {
                string file = Path.Combine(dir, $"t{i}.bin");
                File.WriteAllBytes(file, new byte[1000]);
                File.SetLastWriteTimeUtc(file, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(i));
            }
            Assert.Equal(2000, GeoportalLidarEngine.TrimDiskCache(dir, 3500, 2500));
            Assert.False(File.Exists(Path.Combine(dir, "t0.bin")));
            Assert.False(File.Exists(Path.Combine(dir, "t2.bin")));
            Assert.True(File.Exists(Path.Combine(dir, "t3.bin")));
            Assert.True(File.Exists(Path.Combine(dir, "t4.bin")));
            Assert.Equal(2000, GeoportalLidarEngine.TrimDiskCache(dir, 3500, 2500));
        }
        finally { Directory.Delete(dir, true); }
    }

    private static BgraImage Solid(int size, byte r, byte g, byte b)
    {
        var img = new BgraImage(size, size);
        for (int i = 0; i < img.Pixels.Length; i += 4)
        {
            img.Pixels[i] = b; img.Pixels[i + 1] = g; img.Pixels[i + 2] = r; img.Pixels[i + 3] = 255;
        }
        return img;
    }

    private static (int R, int G, int B, int A) Px(BgraImage img, int x, int y)
    {
        int i = (y * img.Width + x) * 4;
        return (img.Pixels[i + 2], img.Pixels[i + 1], img.Pixels[i], img.Pixels[i + 3]);
    }
}
