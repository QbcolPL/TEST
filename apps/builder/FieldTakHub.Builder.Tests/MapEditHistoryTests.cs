using System.Collections.Generic;
using System.Linq;
using FieldTakHub.Builder.Services;
using FieldTakHub.Builder.Services.MapEditor;
using Xunit;
using RangeSector = FieldTakHub.Builder.Services.TacticalMapGeneratorView.TacticalRangeSectorItem;

namespace FieldTakHub.Builder.Tests;

/// <summary>Cofanie i ponawianie zmian w generatorze map (Ctrl+Z / Ctrl+Y).</summary>
public class MapEditHistoryTests
{
    private static EditHistory<string> TextHistory() => new EditHistory<string>(s => s, 3);

    [Fact]
    public void UndoAndRedoWalkThroughCommittedStates()
    {
        var history = TextHistory();
        history.Reset("A");
        Assert.True(history.Commit("B"));
        Assert.True(history.Commit("C"));
        Assert.False(history.Commit("C"));          // bez zmian — bez nowego kroku

        Assert.Equal("B", history.Undo());
        Assert.Equal("A", history.Undo());
        Assert.Null(history.Undo());                 // nic więcej do cofnięcia
        Assert.Equal("B", history.Redo());
        Assert.Equal("C", history.Redo());
        Assert.Null(history.Redo());
        Assert.Equal("C", history.Baseline);
    }

    [Fact]
    public void NewChangeAfterUndoDropsRedo()
    {
        var history = TextHistory();
        history.Reset("A");
        history.Commit("B");
        history.Undo();
        Assert.True(history.CanRedo);
        Assert.True(history.Commit("X"));
        Assert.False(history.CanRedo);
        Assert.Equal("A", history.Undo());
    }

    [Fact]
    public void HistoryKeepsOnlyTheNewestSteps()
    {
        var history = TextHistory();
        history.Reset("0");
        foreach (var s in new[] { "1", "2", "3", "4", "5" }) history.Commit(s);
        Assert.Equal(3, history.UndoCount);
        Assert.Equal("4", history.Undo());
        Assert.Equal("3", history.Undo());
        Assert.Equal("2", history.Undo());
        Assert.Null(history.Undo());
    }

    [Fact]
    public void ResetStartsANewDocument()
    {
        var history = TextHistory();
        history.Reset("A");
        history.Commit("B");
        history.Reset("Z");
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Equal("Z", history.Baseline);
    }

    [Fact]
    public void RebaseKeepsRedoAvailable()
    {
        var history = TextHistory();
        history.Reset("A");
        history.Commit("B");
        Assert.Equal("A", history.Undo());
        history.Rebase("A+");                       // widok uzupełnił przywrócony stan
        Assert.False(history.Commit("A+"));         // to nie jest nowy krok…
        Assert.True(history.CanRedo);               // …więc „ponów” zostaje
        Assert.Equal("B", history.Redo());
        Assert.Equal("A+", history.Undo());
    }

    private static TacticalMapObjectItem Route() => new TacticalMapObjectItem
    {
        Kind = "Route",
        SymbolCode = "Route",
        Name = "Trasa-1",
        ColorHex = "#FF0000",
        Points = new List<(double Lat, double Lon)> { (50.0, 19.0), (50.001, 19.002) }
    };

    [Fact]
    public void SnapshotsAreIndependentCopiesWithTheSameIds()
    {
        var route = Route();
        var sector = new RangeSector { Name = "KM-1", BaseRadiusMeters = 70 };
        var state = MapEditSnapshots.Capture(new[] { route }, new[] { sector });

        route.Points.Add((50.002, 19.004));
        route.Name = "Zmieniona";
        sector.BaseRadiusMeters = 120;

        var restoredRoute = state.Objects.Single().ToItem();
        var restoredSector = state.Sectors.Single().ToItem();
        Assert.Equal(route.Id, restoredRoute.Id);
        Assert.Equal("Trasa-1", restoredRoute.Name);
        Assert.Equal(2, restoredRoute.Points.Count);
        Assert.Equal(sector.Id, restoredSector.Id);
        Assert.Equal(70.0, restoredSector.BaseRadiusMeters);

        restoredRoute.Points.Clear();
        Assert.Equal(2, state.Objects.Single().Points.Count);   // migawka się nie zmienia
    }

    [Fact]
    public void SignatureTracksContentChanges()
    {
        var route = Route();
        string before = MapEditSnapshots.Capture(new[] { route }, new RangeSector[0]).Signature;
        Assert.Equal(before, MapEditSnapshots.Capture(new[] { route }, new RangeSector[0]).Signature);

        route.Points[1] = (50.0011, 19.002);
        Assert.NotEqual(before, MapEditSnapshots.Capture(new[] { route }, new RangeSector[0]).Signature);

        var second = Route();
        string withTwo = MapEditSnapshots.Capture(new[] { route, second }, new RangeSector[0]).Signature;
        string reordered = MapEditSnapshots.Capture(new[] { second, route }, new RangeSector[0]).Signature;
        Assert.NotEqual(withTwo, reordered);    // kolejność też jest częścią stanu (legenda, rysowanie)
    }

    [Fact]
    public void PanningTheMapIsNotAnEditOfAnchoredSector()
    {
        var anchored = new RangeSector { HasGeoAnchor = true, ShooterLat = 50.1, ShooterLon = 19.2, OffsetEastMeters = 10, OffsetNorthMeters = 5 };
        string before = MapEditSnapshots.Capture(new TacticalMapObjectItem[0], new[] { anchored }).Signature;
        anchored.OffsetEastMeters = 250;   // przeliczone po przesunięciu mapy
        anchored.OffsetNorthMeters = -40;
        Assert.Equal(before, MapEditSnapshots.Capture(new TacticalMapObjectItem[0], new[] { anchored }).Signature);

        anchored.ShooterLat = 50.2;        // przesunięcie stanowiska — to już zmiana
        Assert.NotEqual(before, MapEditSnapshots.Capture(new TacticalMapObjectItem[0], new[] { anchored }).Signature);

        var free = new RangeSector { HasGeoAnchor = false, OffsetEastMeters = 10 };
        string freeBefore = MapEditSnapshots.Capture(new TacticalMapObjectItem[0], new[] { free }).Signature;
        free.OffsetEastMeters = 11;
        Assert.NotEqual(freeBefore, MapEditSnapshots.Capture(new TacticalMapObjectItem[0], new[] { free }).Signature);
    }

    [Fact]
    public void MapHistoryUndoesRealEdits()
    {
        var objects = new List<TacticalMapObjectItem> { Route() };
        var sectors = new List<RangeSector>();
        var history = MapEditSnapshots.CreateHistory();
        history.Reset(MapEditSnapshots.Capture(objects, sectors));

        objects.Clear();                                    // „Wyczyść listę”
        Assert.True(history.Commit(MapEditSnapshots.Capture(objects, sectors)));

        var restored = history.Undo();
        Assert.NotNull(restored);
        Assert.Single(restored!.Objects);
        Assert.Equal("Trasa-1", restored.Objects[0].Name);
    }
}
