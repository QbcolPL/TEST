using System.Linq;
using System.Text.Json.Nodes;
using FieldTakHub.Builder.Services.MapEditor;
using Xunit;
using RangeSector = FieldTakHub.Builder.Services.TacticalMapGeneratorView.TacticalRangeSectorItem;

namespace FieldTakHub.Builder.Tests;

/// <summary>Wspólna lista obiektów i sektorów ognia (etap 2): teksty wierszy i zapis sektorów razem z mapą.</summary>
public class MapUnifiedListTests
{
    [Theory]
    [InlineData(0, "0 obiektów")]
    [InlineData(1, "1 obiekt")]
    [InlineData(2, "2 obiekty")]
    [InlineData(4, "4 obiekty")]
    [InlineData(5, "5 obiektów")]
    [InlineData(12, "12 obiektów")]
    [InlineData(14, "14 obiektów")]
    [InlineData(21, "21 obiektów")]
    [InlineData(22, "22 obiekty")]
    [InlineData(104, "104 obiekty")]
    [InlineData(112, "112 obiektów")]
    public void CountsUsePolishPlurals(int count, string expected)
    {
        Assert.Equal(expected, MapListText.Plural(count, "obiekt", "obiekty", "obiektów"));
    }

    [Fact]
    public void HeaderCountsObjectsAndFireSectors()
    {
        Assert.Equal("Na mapie nie ma jeszcze obiektów", MapListText.CountSummary(0, 0));
        Assert.Equal("Na mapie: 3 obiekty · 1 sektor ognia", MapListText.CountSummary(3, 1));
        Assert.Equal("Na mapie: 2 sektory ognia", MapListText.CountSummary(0, 2));
        Assert.Equal("Na mapie: 5 obiektów", MapListText.CountSummary(5, 0));
        Assert.Equal("1 obiekt · 5 sektorów ognia", MapListText.CountList(1, 5));
    }

    [Fact]
    public void ObjectRowsDescribeKindOrSymbol()
    {
        Assert.Equal("Trasa / linia · 4 punkty", MapListText.ObjectInfo(MapObjectKinds.Route, null, 4));
        Assert.Equal("Teren gry (AO) · 5 punktów", MapListText.ObjectInfo(MapObjectKinds.Ao, null, 5));
        Assert.Equal("Sektor / strefa · 1 punkt", MapListText.ObjectInfo(MapObjectKinds.Sector, null, 1));
        Assert.Equal("Kropka", MapListText.ObjectInfo(MapObjectKinds.Dot, "nieużywane", 1));
        Assert.Equal("Pluton", MapListText.ObjectInfo(MapObjectKinds.Marker, " Pluton ", 1));
        Assert.Equal("Znacznik NATO", MapListText.ObjectInfo(MapObjectKinds.Marker, null, 1));
    }

    [Fact]
    public void FireSectorRowsShowRangeDirectionAndHeight()
    {
        Assert.Equal("60 m · az. 45° · 75°", MapListText.FireSectorInfo(60, 60, 45, 75, 0));
        Assert.Equal("70 ➔ 74.2 m · az. 90° · 90° · ΔH +5 m", MapListText.FireSectorInfo(70, 74.2, 450, 90, 5));
        Assert.Equal("40 m · okrąg · ΔH -2.5 m", MapListText.FireSectorInfo(40, 40, 0, 360, -2.5));
        Assert.Equal("95 m · az. 330° · 30°", MapListText.FireSectorInfo(95, 95.02, -30, 30.4, 0));
    }

    private static RangeSector SampleSector() => new RangeSector
    {
        Name = "DMR-2",
        WeaponPreset = "DMR / Wyborowa (80 m)",
        BaseRadiusMeters = 80,
        AzimuthDeg = 135,
        SpreadAngleDeg = 40,
        FillOpacity = 0.31,
        ColorHex = "#FAD02C",
        ElevationDeltaMeters = -6.5,
        ShooterAltMasl = 212.4,
        TargetAltMasl = 218.9,
        IsVisible = false,
        OffsetEastMeters = 12.5,
        OffsetNorthMeters = -40.25,
        HasGeoAnchor = true,
        ShooterLat = 52.06931,
        ShooterLon = 19.48032
    };

    [Fact]
    public void FireSectorsSurviveSavingAndLoading()
    {
        var original = SampleSector();
        string json = RangeSectorJson.ToJsonArray(new[] { original }).ToJsonString();
        var loaded = RangeSectorJson.FromJsonArray(JsonNode.Parse(json) as JsonArray).Single();

        Assert.Equal(original.Id, loaded.Id);
        Assert.Equal("DMR-2", loaded.Name);
        Assert.Equal("DMR / Wyborowa (80 m)", loaded.WeaponPreset);
        Assert.Equal(80.0, loaded.BaseRadiusMeters);
        Assert.Equal(135.0, loaded.AzimuthDeg);
        Assert.Equal(40.0, loaded.SpreadAngleDeg);
        Assert.Equal(0.31, loaded.FillOpacity);
        Assert.Equal("#FAD02C", loaded.ColorHex);
        Assert.Equal(-6.5, loaded.ElevationDeltaMeters);
        Assert.Equal(212.4, loaded.ShooterAltMasl);
        Assert.Equal(218.9, loaded.TargetAltMasl);
        Assert.False(loaded.IsVisible);
        Assert.Equal(12.5, loaded.OffsetEastMeters);
        Assert.Equal(-40.25, loaded.OffsetNorthMeters);
        Assert.True(loaded.HasGeoAnchor);
        Assert.Equal(52.06931, loaded.ShooterLat);
        Assert.Equal(19.48032, loaded.ShooterLon);
        Assert.Null(loaded.Visuals);
    }

    [Fact]
    public void LoadingClampsValuesToEditorRanges()
    {
        var json = JsonNode.Parse("""[{"baseRadiusM": 9999, "spreadDeg": 1, "fillOpacity": 2, "azimuthDeg": -90, "elevationDeltaM": 900}]""") as JsonArray;
        var sector = RangeSectorJson.FromJsonArray(json).Single();
        Assert.Equal(500.0, sector.BaseRadiusMeters);
        Assert.Equal(5.0, sector.SpreadAngleDeg);
        Assert.Equal(0.65, sector.FillOpacity);
        Assert.Equal(270.0, sector.AzimuthDeg);
        Assert.Equal(150.0, sector.ElevationDeltaMeters);
    }

    [Fact]
    public void LoadingSkipsBrokenEntriesAndKeepsDefaults()
    {
        var json = JsonNode.Parse("""[{"name": "A", "baseRadiusM": "85", "visible": "false"}, 5, null, "x", {"hasGeoAnchor": true, "shooterLat": 123, "shooterLon": 19}]""") as JsonArray;
        var sectors = RangeSectorJson.FromJsonArray(json);
        Assert.Equal(2, sectors.Count);

        var first = sectors[0];
        var defaults = new RangeSector();
        Assert.Equal("A", first.Name);
        Assert.Equal(85.0, first.BaseRadiusMeters);   // liczba zapisana jako tekst też się wczyta
        Assert.False(first.IsVisible);
        Assert.Equal(defaults.AzimuthDeg, first.AzimuthDeg);
        Assert.Equal(defaults.SpreadAngleDeg, first.SpreadAngleDeg);
        Assert.Equal(defaults.ColorHex, first.ColorHex);
        Assert.False(string.IsNullOrWhiteSpace(first.Id));

        Assert.False(sectors[1].HasGeoAnchor);         // szerokość 123° nie jest punktem na Ziemi
        Assert.NotEqual(first.Id, sectors[1].Id);
        Assert.Empty(RangeSectorJson.FromJsonArray(null));
    }

    [Fact]
    public void SavingNeverWritesNaN()
    {
        var broken = SampleSector();
        broken.OffsetEastMeters = double.NaN;
        broken.BaseRadiusMeters = double.PositiveInfinity;
        string json = RangeSectorJson.ToJsonArray(new[] { broken }).ToJsonString();   // NaN w JSON wywróciłby zapis sesji
        var loaded = RangeSectorJson.FromJsonArray(JsonNode.Parse(json) as JsonArray).Single();
        Assert.Equal(0.0, loaded.OffsetEastMeters);
        Assert.Equal(60.0, loaded.BaseRadiusMeters);
    }
}
