using System.Linq;
using FieldTakHub.Builder.Services.MapEditor;
using Xunit;

namespace FieldTakHub.Builder.Tests;

/// <summary>Panel generatora map w zakładkach i jedno menu „Eksport…” (etap 3).</summary>
public class MapLeftPanelTests
{
    [Fact]
    public void TabsFollowTheWorkflowOrder()
    {
        Assert.Equal(new[] { "Mapa", "Obiekty", "Siatka", "Legenda", "Eksport" }, LeftPanelTabs.All.Select(t => t.Title).ToArray());
        Assert.Equal(LeftPanelTabs.All.Count, LeftPanelTabs.All.Select(t => t.Id).Distinct().Count());
        for (int i = 0; i < LeftPanelTabs.All.Count; i++)
            Assert.Contains($"(Ctrl+{i + 1})", LeftPanelTabs.All[i].Tooltip);
    }

    [Theory]
    [InlineData(1, LeftPanelTabs.MapTab)]
    [InlineData(2, LeftPanelTabs.ObjectsTab)]
    [InlineData(3, LeftPanelTabs.GridTab)]
    [InlineData(4, LeftPanelTabs.LegendTab)]
    [InlineData(5, LeftPanelTabs.ExportTab)]
    public void CtrlDigitSelectsTab(int digit, string expected)
    {
        Assert.Equal(expected, LeftPanelTabs.FromDigit(digit));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(9)]
    [InlineData(-1)]
    public void OtherDigitsSelectNoTab(int digit)
    {
        Assert.Null(LeftPanelTabs.FromDigit(digit));
    }

    [Fact]
    public void SavedTabFallsBackToMap()
    {
        Assert.Equal(LeftPanelTabs.ExportTab, LeftPanelTabs.Normalize("EXPORT"));
        Assert.Equal(LeftPanelTabs.MapTab, LeftPanelTabs.Normalize(null));
        Assert.Equal(LeftPanelTabs.MapTab, LeftPanelTabs.Normalize(""));
        Assert.Equal(LeftPanelTabs.MapTab, LeftPanelTabs.Normalize("SECTION_4"));
    }

    [Fact]
    public void ExportMenuHasEveryFormatOnce()
    {
        var ids = ExportCatalog.All.Select(o => o.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(9, ids.Count);
        Assert.All(ExportCatalog.All, o =>
        {
            Assert.False(string.IsNullOrWhiteSpace(o.Glyph));
            Assert.False(string.IsNullOrWhiteSpace(o.Title));
            Assert.False(string.IsNullOrWhiteSpace(o.Note));
        });
    }

    [Fact]
    public void ExportGroupsKeepTheirOrder()
    {
        var menu = ExportCatalog.Grouped(exportTabOnly: false);
        Assert.Equal(new[] { ExportCatalog.FilesGroup, ExportCatalog.PackageGroup, ExportCatalog.OfflineGroup }, menu.Select(g => g.Key).ToArray());
        Assert.Equal(ExportCatalog.Kmz, menu[0].First().Id);

        // W zakładce Eksport mapa offline ma własną kartę z ustawieniami i postępem — bez powtórzeń na liście.
        var tab = ExportCatalog.Grouped(exportTabOnly: true);
        Assert.Equal(new[] { ExportCatalog.FilesGroup, ExportCatalog.PackageGroup }, tab.Select(g => g.Key).ToArray());
        Assert.DoesNotContain(tab.SelectMany(g => g), o => o.Id == ExportCatalog.OfflineFile || o.Id == ExportCatalog.FtakOffline);
        Assert.Contains(tab.SelectMany(g => g), o => o.Id == ExportCatalog.FtakSheet);
    }
}
