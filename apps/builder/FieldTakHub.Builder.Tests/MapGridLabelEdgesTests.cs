using System.Linq;
using FieldTakHub.Builder.Services.MapEditor;
using Xunit;

namespace FieldTakHub.Builder.Tests;

/// <summary>Opisy siatki przy dowolnych krawędziach arkusza (generator map).</summary>
public class MapGridLabelEdgesTests
{
    [Fact]
    public void DefaultIsAllFourEdges()
    {
        Assert.Equal(GridEdges.All, GridLabelEdges.Default);
        Assert.Equal("top,bottom,left,right", GridLabelEdges.Serialize(GridEdges.All));
    }

    [Fact]
    public void EveryCombinationSurvivesSaveAndLoad()
    {
        for (int value = 0; value <= 15; value++)
        {
            var edges = (GridEdges)value;
            Assert.Equal(edges, GridLabelEdges.Parse(GridLabelEdges.Serialize(edges), GridEdges.All));
        }
    }

    [Fact]
    public void ParseToleratesSpacingCaseAndSeparators()
    {
        Assert.Equal(GridEdges.Top | GridEdges.Left, GridLabelEdges.Parse(" Top, LEFT "));
        Assert.Equal(GridEdges.Bottom | GridEdges.Right, GridLabelEdges.Parse("bottom;right"));
        Assert.Equal(GridEdges.None, GridLabelEdges.Parse("none"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("north,south")]
    public void UnknownOrMissingValueFallsBack(string? text)
    {
        Assert.Equal(GridEdges.Top | GridEdges.Left, GridLabelEdges.Parse(text, GridEdges.Top | GridEdges.Left));
        Assert.Equal(GridEdges.All, GridLabelEdges.Parse(text));
    }

    [Fact]
    public void WithTurnsSingleEdgesOnAndOff()
    {
        var edges = GridLabelEdges.With(GridEdges.All, GridEdges.Top, false);
        Assert.Equal(GridEdges.Bottom | GridEdges.Left | GridEdges.Right, edges);
        Assert.Equal(GridEdges.All, GridLabelEdges.With(edges, GridEdges.Top, true));
        Assert.Equal(GridEdges.Right, GridLabelEdges.With(GridEdges.None, GridEdges.Right, true));
    }

    [Fact]
    public void OptionsListTheFourEdgesInPanelOrder()
    {
        Assert.Equal(new[] { GridEdges.Top, GridEdges.Bottom, GridEdges.Left, GridEdges.Right }, GridLabelEdges.Options.Select(o => o.Edge).ToArray());
        Assert.Equal(4, GridLabelEdges.Options.Select(o => o.Token).Distinct().Count());
        Assert.All(GridLabelEdges.Options, o => Assert.False(string.IsNullOrWhiteSpace(o.Label + o.Glyph + o.Where)));
    }

    [Fact]
    public void LabelsSitInsideTheirEdge()
    {
        // Arkusz 800 × 600, opis 24 × 14, odstęp od krawędzi 6.
        Assert.Equal((88.0, 6.0), GridLabelEdges.LabelOrigin(GridEdges.Top, 100, 24, 14, 800, 600));
        Assert.Equal((88.0, 580.0), GridLabelEdges.LabelOrigin(GridEdges.Bottom, 100, 24, 14, 800, 600));
        Assert.Equal((6.0, 193.0), GridLabelEdges.LabelOrigin(GridEdges.Left, 200, 24, 14, 800, 600));
        Assert.Equal((770.0, 193.0), GridLabelEdges.LabelOrigin(GridEdges.Right, 200, 24, 14, 800, 600));
    }

    [Fact]
    public void CornersStayFreeWhereTwoEdgesMeet()
    {
        const double side = 40, end = 26;
        // Opis przy górnej krawędzi wchodzący w pas lewej: pominięty tylko wtedy, gdy lewa ma opisy.
        Assert.False(GridLabelEdges.FitsAlongEdge(GridEdges.Top, 20, 6, 24, 14, 800, 600, GridEdges.Top | GridEdges.Left, side, end));
        Assert.True(GridLabelEdges.FitsAlongEdge(GridEdges.Top, 20, 6, 24, 14, 800, 600, GridEdges.Top, side, end));
        // To samo przy prawej i przy dole.
        Assert.False(GridLabelEdges.FitsAlongEdge(GridEdges.Bottom, 750, 580, 24, 14, 800, 600, GridEdges.Bottom | GridEdges.Right, side, end));
        Assert.False(GridLabelEdges.FitsAlongEdge(GridEdges.Left, 6, 10, 24, 14, 800, 600, GridEdges.Left | GridEdges.Top, side, end));
        Assert.False(GridLabelEdges.FitsAlongEdge(GridEdges.Right, 770, 570, 24, 14, 800, 600, GridEdges.Right | GridEdges.Bottom, side, end));
        Assert.True(GridLabelEdges.FitsAlongEdge(GridEdges.Right, 770, 300, 24, 14, 800, 600, GridEdges.All, side, end));
        // Opis wychodzący poza arkusz — zawsze pominięty.
        Assert.False(GridLabelEdges.FitsAlongEdge(GridEdges.Top, 790, 6, 24, 14, 800, 600, GridEdges.Top, side, end));
    }

    [Fact]
    public void ReserveIsZeroForEdgesWithoutLabels()
    {
        Assert.Equal(0.0, GridLabelEdges.Reserve(GridEdges.Top, GridEdges.Left, mgrsLabels: true, sectorRulers: true, wgs84Labels: true, fontScale: 1.0));
        Assert.Equal(0.0, GridLabelEdges.Reserve(GridEdges.All, GridEdges.Left, mgrsLabels: false, sectorRulers: false, wgs84Labels: false, fontScale: 1.0));
    }

    [Fact]
    public void ReserveTakesTheWidestActiveGrid()
    {
        Assert.Equal(38.0, GridLabelEdges.Reserve(GridEdges.All, GridEdges.Left, true, false, false, 1.0));
        Assert.Equal(23.0, GridLabelEdges.Reserve(GridEdges.All, GridEdges.Top, true, false, false, 1.0));
        Assert.Equal(60.0, GridLabelEdges.Reserve(GridEdges.All, GridEdges.Right, true, false, false, 2.0));
        Assert.Equal(22.0, GridLabelEdges.Reserve(GridEdges.All, GridEdges.Right, false, true, false, 1.0));
        Assert.Equal(80.0, GridLabelEdges.Reserve(GridEdges.All, GridEdges.Left, true, true, true, 1.0));
        Assert.Equal(23.0, GridLabelEdges.Reserve(GridEdges.All, GridEdges.Bottom, true, true, true, 1.0));
    }
}
