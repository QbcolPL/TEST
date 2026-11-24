using System;
using System.Globalization;
using System.Linq;
using FieldTakHub.Builder.Services.MapEditor;
using Xunit;

namespace FieldTakHub.Builder.Tests;

/// <summary>Jednolity układ kart obiektów w generatorze map (lista obiektów i sektorów).</summary>
public class MapObjectListTests
{
    [Theory]
    [InlineData("Marker", true, false)]
    [InlineData("Dot", false, false)]
    [InlineData("AO", false, true)]
    [InlineData("Sector", false, true)]
    [InlineData("Route", false, true)]
    public void EveryKindGetsTheSameCardWithKindSpecificLastRow(string kind, bool rotation, bool geometry)
    {
        var layout = MapObjectCardSpec.For(kind, 10);
        Assert.Equal(rotation, layout.HasRotation);
        Assert.Equal(geometry, layout.HasGeometryTools);
        Assert.False(layout.HasRotation && layout.HasGeometryTools);
    }

    [Fact]
    public void SimplifyIsOfferedOnlyForDenseShapes()
    {
        Assert.False(MapObjectCardSpec.For("AO", 5).CanSimplify);
        Assert.True(MapObjectCardSpec.For("AO", 6).CanSimplify);
        Assert.True(MapObjectCardSpec.For("Route", 120).CanSimplify);
        Assert.False(MapObjectCardSpec.For("Dot", 120).CanSimplify);
    }

    [Fact]
    public void SizeLabelUsesOneFormatForEveryKind()
    {
        // Wcześniej format „0.2” dawał „12x” dla 1,0, a kropka pokazywała „14 px (11x)”.
        Assert.Equal("1.00×", MapObjectCardSpec.FormatSize("AO", 1.0, 14));
        Assert.Equal("1.00×", MapObjectCardSpec.FormatSize("Marker", 1.0, 14));
        Assert.Equal("1.25×", MapObjectCardSpec.FormatSize("Route", 1.25, 14));
        Assert.Equal("14 px · 1.00×", MapObjectCardSpec.FormatSize("Dot", 1.0, 14));
        Assert.Equal("5 px · 0.35×", MapObjectCardSpec.FormatSize("Dot", 0.35, 14));
        Assert.Equal("56 px · 2.60×", MapObjectCardSpec.FormatSize("Dot", 2.6, 30));
        Assert.Equal("1.00×", MapObjectCardSpec.FormatSize("Sector", 0.0, 14));
    }

    [Fact]
    public void LabelsDoNotDependOnSystemCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pl-PL");
            Assert.Equal("1.50×", MapObjectCardSpec.FormatSize("AO", 1.5, 14));
            Assert.Equal("21 px · 1.50×", MapObjectCardSpec.FormatSize("Dot", 1.5, 14));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void SizeAndRotationAreNormalizedForTheSliders()
    {
        Assert.Equal(1.0, MapObjectCardSpec.NormalizeSize(0.0));
        Assert.Equal(MapObjectCardSpec.MaxSizeMultiplier, MapObjectCardSpec.NormalizeSize(5.0));
        Assert.Equal(MapObjectCardSpec.MinSizeMultiplier, MapObjectCardSpec.NormalizeSize(0.2));
        Assert.Equal(1.3, MapObjectCardSpec.NormalizeSize(1.3));

        Assert.Equal(330.0, MapObjectCardSpec.NormalizeRotation(-30.0), 9);
        Assert.Equal(10.0, MapObjectCardSpec.NormalizeRotation(370.0), 9);
        Assert.Equal("0°", MapObjectCardSpec.FormatRotation(-0.0));
        Assert.Equal("0°", MapObjectCardSpec.FormatRotation(359.6));
        Assert.Equal("0°", MapObjectCardSpec.FormatRotation(360.0));
        Assert.Equal("45°", MapObjectCardSpec.FormatRotation(45.4));
        Assert.Equal("330°", MapObjectCardSpec.FormatRotation(-30.0));
    }

    [Theory]
    [InlineData(0, "0 punktów")]
    [InlineData(1, "1 punkt")]
    [InlineData(2, "2 punkty")]
    [InlineData(4, "4 punkty")]
    [InlineData(5, "5 punktów")]
    [InlineData(12, "12 punktów")]
    [InlineData(22, "22 punkty")]
    [InlineData(25, "25 punktów")]
    public void PointCountUsesPolishPlurals(int count, string expected)
    {
        Assert.Equal(expected, MapObjectCardSpec.FormatPointCount(count));
    }

    [Fact]
    public void GeometryKindsConvertOnlyWhenThereAreEnoughPoints()
    {
        Assert.Equal(new[] { "Route" }, MapObjectKinds.AvailableGeometryKinds("Route", 2).ToArray());
        Assert.Equal(new[] { "AO", "Sector", "Route" }, MapObjectKinds.AvailableGeometryKinds("Route", 3).ToArray());
        Assert.Equal(new[] { "AO", "Sector", "Route" }, MapObjectKinds.AvailableGeometryKinds("AO", 4).ToArray());
        Assert.True(MapObjectKinds.CanConvert("AO", "Route", 3));
        Assert.False(MapObjectKinds.CanConvert("Route", "AO", 2));
        Assert.False(MapObjectKinds.CanConvert("Dot", "AO", 10));
        Assert.False(MapObjectKinds.CanConvert("AO", "Marker", 10));
        Assert.Equal(2, MapObjectKinds.MinimumPoints("Route"));
        Assert.Equal(3, MapObjectKinds.MinimumPoints("Sector"));
        Assert.Equal(1, MapObjectKinds.MinimumPoints("Dot"));
    }

    [Fact]
    public void GeometryKindNamesRoundTrip()
    {
        foreach (var kind in MapObjectKinds.GeometryKinds)
            Assert.Equal(kind, MapObjectKinds.FromDisplayName(MapObjectKinds.DisplayName(kind)));
        Assert.Null(MapObjectKinds.FromDisplayName("Kropka"));
        Assert.Null(MapObjectKinds.FromDisplayName(null));
        Assert.Equal(MapObjectKinds.GeometryKinds.Count, MapObjectKinds.GeometryKinds.Select(MapObjectKinds.DisplayName).Distinct().Count());
    }

    [Fact]
    public void DefaultLegendTextsMatchEarlierVersions()
    {
        // Legenda grupuje obiekty po tych tekstach — muszą zostać takie jak wcześniej.
        Assert.Equal("Granica Terenu Gry (strefa poza grą)", MapObjectKinds.DefaultLegendText("AO"));
        Assert.Equal("Sektor / Strefa taktyczna", MapObjectKinds.DefaultLegendText("Sector"));
        Assert.Equal("Trasa / Linia rozgraniczenia", MapObjectKinds.DefaultLegendText("Route"));
        Assert.Equal("Punkt orientacyjny", MapObjectKinds.DefaultLegendText("Dot"));
        Assert.Null(MapObjectKinds.DefaultLegendText("Marker"));
    }

    [Fact]
    public void ChangingKindReplacesOnlyTheDefaultLegendText()
    {
        string ao = MapObjectKinds.DefaultLegendText("AO")!;
        string route = MapObjectKinds.DefaultLegendText("Route")!;
        Assert.Equal(route, MapObjectKinds.LegendTextAfterKindChange(ao, ao, route));
        Assert.Equal(route, MapObjectKinds.LegendTextAfterKindChange("  " + ao + " ", ao, route));
        Assert.Equal(route, MapObjectKinds.LegendTextAfterKindChange("", ao, route));
        Assert.Equal(route, MapObjectKinds.LegendTextAfterKindChange(null, ao, route));
        Assert.Equal("Droga ewakuacji", MapObjectKinds.LegendTextAfterKindChange("Droga ewakuacji", ao, route));
    }
}
