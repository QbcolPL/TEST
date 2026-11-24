using System.Collections.Generic;
using FieldTakHub.Builder.Services.MapEditor;
using Xunit;

namespace FieldTakHub.Builder.Tests;

/// <summary>Pierścień menu pod prawym przyciskiem, trafianie w kształty i opis pomiaru.</summary>
public class MapInteractionTests
{
    [Theory]
    [InlineData(0.0, -50.0, 0)]     // w górę
    [InlineData(40.0, -40.0, 1)]    // w prawo w górę
    [InlineData(50.0, 0.0, 2)]      // w prawo
    [InlineData(40.0, 40.0, 3)]
    [InlineData(0.0, 50.0, 4)]      // w dół
    [InlineData(-40.0, 40.0, 5)]
    [InlineData(-50.0, 0.0, 6)]     // w lewo
    [InlineData(-40.0, -40.0, 7)]
    [InlineData(10.0, -60.0, 0)]    // lekko w prawo od góry — nadal pozycja górna
    [InlineData(-10.0, -60.0, 0)]   // lekko w lewo od góry — też górna (granica 22.5°)
    public void RingIndexFollowsDirection(double dx, double dy, int expected)
    {
        Assert.Equal(expected, RadialMenuGeometry.IndexAt(dx, dy, 8, 26, double.MaxValue));
    }

    [Fact]
    public void RingCenterAndFarClicksSelectNothing()
    {
        Assert.Equal(-1, RadialMenuGeometry.IndexAt(5, -5, 8, 26, 200));       // środek = anuluj
        Assert.Equal(-1, RadialMenuGeometry.IndexAt(0, -250, 8, 26, 200));     // za daleko przy klikaniu
        Assert.Equal(0, RadialMenuGeometry.IndexAt(0, -250, 8, 26, double.MaxValue)); // gest: liczy się kierunek
        Assert.Equal(-1, RadialMenuGeometry.IndexAt(0, -50, 0, 26, 200));
    }

    [Fact]
    public void RingAnglesAndLabelSides()
    {
        Assert.Equal(0.0, RadialMenuGeometry.CenterAngleDeg(0, 8));
        Assert.Equal(135.0, RadialMenuGeometry.CenterAngleDeg(3, 8));
        var up = RadialMenuGeometry.Direction(0);
        Assert.Equal(0.0, up.X, 9);
        Assert.Equal(-1.0, up.Y, 9);
        var right = RadialMenuGeometry.Direction(90);
        Assert.Equal(1.0, right.X, 9);
        Assert.Equal(0.0, right.Y, 9);
        Assert.Equal(RadialLabelSide.Top, RadialMenuGeometry.LabelSide(0));
        Assert.Equal(RadialLabelSide.Right, RadialMenuGeometry.LabelSide(45));
        Assert.Equal(RadialLabelSide.Right, RadialMenuGeometry.LabelSide(135));
        Assert.Equal(RadialLabelSide.Bottom, RadialMenuGeometry.LabelSide(180));
        Assert.Equal(RadialLabelSide.Left, RadialMenuGeometry.LabelSide(270));
        Assert.Equal(RadialLabelSide.Left, RadialMenuGeometry.LabelSide(315));
    }

    [Fact]
    public void SegmentDistance()
    {
        var a = new ScreenPoint(0, 0);
        var b = new ScreenPoint(10, 0);
        Assert.Equal(5.0, MapHitTest.DistanceToSegment(new ScreenPoint(5, 5), a, b), 9);
        Assert.Equal(5.0, MapHitTest.DistanceToSegment(new ScreenPoint(-3, 4), a, b), 9);   // za końcem odcinka
        Assert.Equal(5.0, MapHitTest.DistanceToSegment(new ScreenPoint(3, 4), a, a), 9);    // odcinek zerowy
    }

    [Fact]
    public void AreasAreHitOnTheirEdgeOnlyAndRoutesOnTheirLine()
    {
        var square = new HitOutline(new[] { new ScreenPoint(0, 0), new ScreenPoint(100, 0), new ScreenPoint(100, 100), new ScreenPoint(0, 100) }, Closed: true);
        var route = new HitOutline(new[] { new ScreenPoint(20, 50), new ScreenPoint(80, 50) }, Closed: false);
        var outlines = new List<HitOutline> { square, route };

        Assert.Equal(0, MapHitTest.FindClosestOutline(new ScreenPoint(50, 3), outlines, 6));    // górna krawędź AO
        Assert.Equal(0, MapHitTest.FindClosestOutline(new ScreenPoint(2, 50), outlines, 6));    // odcinek zamykający
        Assert.Equal(1, MapHitTest.FindClosestOutline(new ScreenPoint(50, 53), outlines, 6));   // trasa
        Assert.Equal(-1, MapHitTest.FindClosestOutline(new ScreenPoint(50, 25), outlines, 6));  // wnętrze AO = menu mapy
        Assert.Equal(-1, MapHitTest.FindClosestOutline(new ScreenPoint(90, 50), outlines, 6));  // za końcem trasy, z dala od krawędzi

        var openSquare = new HitOutline(square.Points, Closed: false);
        Assert.Equal(-1, MapHitTest.FindClosestOutline(new ScreenPoint(2, 50), new List<HitOutline> { openSquare }, 1));
    }

    [Fact]
    public void LaterShapeWinsOnEqualDistance()
    {
        var first = new HitOutline(new[] { new ScreenPoint(0, 0), new ScreenPoint(100, 0) }, Closed: false);
        var second = new HitOutline(new[] { new ScreenPoint(0, 0), new ScreenPoint(100, 0) }, Closed: false);
        Assert.Equal(1, MapHitTest.FindClosestOutline(new ScreenPoint(50, 2), new List<HitOutline> { first, second }, 6));
    }

    [Theory]
    [InlineData(0.0, "0 m")]
    [InlineData(245.4, "245 m")]
    [InlineData(999.4, "999 m")]
    [InlineData(1000.0, "1.00 km")]
    [InlineData(1234.0, "1.23 km")]
    [InlineData(-5.0, "0 m")]
    public void DistanceIsShownInMetersThenKilometers(double meters, string expected)
    {
        Assert.Equal(expected, MapMeasure.FormatDistance(meters));
    }

    [Fact]
    public void MeasureLabelHasDistanceAzimuthAndDirection()
    {
        // 0.01° na wschód na równiku ≈ 1.11 km, azymut 90°.
        Assert.Equal("1.11 km · azymut 90° WSCH", MapMeasure.Label(0, 0, 0, 0.01));
        // Na północ: azymut 0°.
        Assert.Equal("111 m · azymut 0° PN", MapMeasure.Label(52.0, 21.0, 52.001, 21.0));
    }
}
