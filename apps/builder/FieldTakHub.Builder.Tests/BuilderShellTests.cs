using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using FieldTakHub.Builder.Services.Shell;
using Xunit;

namespace FieldTakHub.Builder.Tests;

/// <summary>Okno główne Buildera: stan projektu, skróty, archiwum map (etap 4).</summary>
public class BuilderShellTests
{
    private static JsonObject Workspace(string eventName = "OPERACJA", double lat = 50.06) => new JsonObject
    {
        ["schemaVersion"] = "2.4-draft",
        ["savedAt"] = "2026-11-25T07:00:00Z",
        ["eventName"] = eventName,
        ["lastSourceDir"] = "C:\\gra\\source",
        ["lastExportDir"] = "C:\\gra",
        ["factions"] = new JsonArray(new JsonObject { ["Name"] = "BLUFOR" }, new JsonObject { ["Name"] = "OPFOR" }),
        ["mapState"] = new JsonObject
        {
            ["centerLat"] = lat,
            ["centerLon"] = 19.94,
            ["zoom"] = 15,
            ["scale"] = 25000,
            ["selectedFactions"] = new JsonArray("BLUFOR", "OPFOR"),
            ["objects"] = new JsonArray(new JsonObject { ["kind"] = "Marker", ["name"] = "HQ" }),
            ["leftPanelTab"] = "MAP",
            ["searchQuery"] = ""
        }
    };

    [Fact]
    public void SignatureIsStableForTheSameContent()
    {
        Assert.Equal(ProjectSignature.Compute(Workspace()), ProjectSignature.Compute(Workspace()));
        Assert.Equal(64, ProjectSignature.Compute(Workspace()).Length);
        Assert.Equal("", ProjectSignature.Compute(null));
    }

    [Fact]
    public void SignatureIgnoresSaveTimeFoldersPanelTabSearchAndTileZoom()
    {
        string baseline = ProjectSignature.Compute(Workspace());
        var changed = Workspace();
        changed["savedAt"] = "2026-11-26T10:00:00Z";
        changed["lastSourceDir"] = "D:\\inny\\source";
        changed["lastExportDir"] = "D:\\inny";
        changed[SessionKeys.ProjectPath] = "C:\\gra\\projekt.fthproj";
        changed[SessionKeys.ProjectBaseline] = "ABC";
        var map = (JsonObject)changed["mapState"]!;
        map["leftPanelTab"] = "EXPORT";
        map["searchQuery"] = "Kraków";
        map["zoom"] = 12;
        map["selectedFactions"] = new JsonArray("OPFOR", "BLUFOR");
        Assert.Equal(baseline, ProjectSignature.Compute(changed));
    }

    [Fact]
    public void SignatureSeesRealChanges()
    {
        string baseline = ProjectSignature.Compute(Workspace());
        Assert.NotEqual(baseline, ProjectSignature.Compute(Workspace(eventName: "INNA")));
        Assert.NotEqual(baseline, ProjectSignature.Compute(Workspace(lat: 50.07)));

        var renamed = Workspace();
        ((JsonObject)((JsonArray)((JsonObject)renamed["mapState"]!)["objects"]!)[0]!)["name"] = "HQ-2";
        Assert.NotEqual(baseline, ProjectSignature.Compute(renamed));

        var oneSide = Workspace();
        ((JsonObject)oneSide["mapState"]!)["selectedFactions"] = new JsonArray("BLUFOR");
        Assert.NotEqual(baseline, ProjectSignature.Compute(oneSide));
    }

    [Fact]
    public void SignatureDoesNotChangeTheInput()
    {
        var workspace = Workspace();
        string before = workspace.ToJsonString();
        ProjectSignature.Compute(workspace);
        Assert.Equal(before, workspace.ToJsonString());
    }

    [Theory]
    [InlineData("C:\\Gry\\Operacja.fthproj", "Operacja.fthproj")]
    [InlineData("/home/gra/Operacja.fthproj", "Operacja.fthproj")]
    [InlineData("Operacja.fthproj", "Operacja.fthproj")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void FileNameWorksWithBothSlashes(string? path, string expected)
    {
        Assert.Equal(expected, ProjectTitleText.FileName(path));
    }

    [Fact]
    public void SidebarLabelShowsFileAndUnsavedMark()
    {
        Assert.Equal("Projekt: Operacja.fthproj", ProjectTitleText.SidebarLabel("C:\\Gry\\Operacja.fthproj", false, false));
        Assert.Equal("● Projekt: Operacja.fthproj", ProjectTitleText.SidebarLabel("C:\\Gry\\Operacja.fthproj", true, false));
        Assert.Equal("Projekt: nowy (jeszcze bez pliku)", ProjectTitleText.SidebarLabel("", false, false));
        Assert.Equal("● Project: new (no file yet)", ProjectTitleText.SidebarLabel(null, true, true));
        Assert.Contains("Ctrl+S", ProjectTitleText.SidebarTooltip("C:\\Gry\\Operacja.fthproj", true, false));
        Assert.Contains("C:\\Gry\\Operacja.fthproj", ProjectTitleText.SidebarTooltip("C:\\Gry\\Operacja.fthproj", false, false));
    }

    [Fact]
    public void WindowTitleUsesAsteriskForUnsavedChanges()
    {
        Assert.Equal("Operacja.fthproj — Field TAK Hub Builder 2.3.0", ProjectTitleText.WindowTitle("C:\\Gry\\Operacja.fthproj", false, "Field TAK Hub Builder 2.3.0", false));
        Assert.Equal("*Operacja.fthproj — Field TAK Hub Builder 2.3.0", ProjectTitleText.WindowTitle("C:\\Gry\\Operacja.fthproj", true, "Field TAK Hub Builder 2.3.0", false));
        Assert.Equal("*Nowy projekt — Field TAK Hub Builder", ProjectTitleText.WindowTitle("", true, "", false));
        Assert.Equal("New project — Field TAK Hub Builder", ProjectTitleText.WindowTitle(null, false, null, true));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 3)]
    public void AltDigitSelectsModule(int digit, int module)
    {
        Assert.Equal(module, ShellShortcuts.ModuleIndexForDigit(digit));
        Assert.Equal("Alt+" + digit, ShellShortcuts.ModuleKeys(module));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(9)]
    [InlineData(-1)]
    public void OtherDigitsSelectNoModule(int digit)
    {
        Assert.Null(ShellShortcuts.ModuleIndexForDigit(digit));
    }

    [Fact]
    public void CheatSheetListsEveryWindowShortcutOnce()
    {
        var keys = ShellShortcuts.WindowShortcuts.Select(s => s.Keys).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        foreach (var expected in new[] { "Ctrl+S", "Ctrl+Shift+S", "Ctrl+O", "Ctrl+N", "Alt+1", "Alt+2", "Alt+3", "Alt+4", "F1" })
            Assert.Contains(expected, keys);
        Assert.All(ShellShortcuts.WindowShortcuts, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Text(false)));
            Assert.False(string.IsNullOrWhiteSpace(s.Text(true)));
        });
        // Ctrl+cyfra należy do zakładek generatora map — okno główne jej nie używa.
        Assert.DoesNotContain(keys, k => k.StartsWith("Ctrl+") && k.Length == 6 && char.IsDigit(k[5]));
    }

    [Fact]
    public void MapsArchiveSitsNextToSourceNotInsideIt()
    {
        string project = Path.Combine(Path.GetTempPath(), "fth-gra");
        string source = Path.Combine(project, "source");
        var when = new DateTime(2026, 11, 25, 7, 15, 3);
        string expected = Path.Combine(project, "archiwum", "maps_20261125_071503");
        Assert.Equal(expected, MapsArchive.FolderFor(source, when));
        Assert.Equal(expected, MapsArchive.FolderFor(source + Path.DirectorySeparatorChar, when));
        Assert.False(MapsArchive.FolderFor(source, when)!.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        Assert.Null(MapsArchive.FolderFor(Path.GetPathRoot(Path.GetTempPath()), when));
        Assert.Null(MapsArchive.FolderFor("", when));
    }
}

/// <summary>Karty „Status paczki” i „Ostatnie paczki .ftak” w generatorze paczek (etap 4).</summary>
public class PackageStatusTests
{
    private static readonly FactionStatusEntry[] TwoSides = { new("BLUFOR", ""), new("OPFOR", "") };

    private static SourceFileEntry F(string path, long size = 1000) => new(path, size);

    [Theory]
    [InlineData("ATAK-CIV-5.4.0.apk", "Atak")]
    [InlineData("atak_civ.apk", "Atak")]
    [InlineData("Meshtastic-2.6.apk", "Meshtastic")]
    [InlineData("ATAK-Plugin-Meshtastic.apk", "Plugin")]
    [InlineData("datasync-plugin.apk", "Plugin")]
    [InlineData("plugins/ATAK-Plugin-VNS.apk", "Plugin")]
    public void ApkRolesFollowThePackageManifestRules(string name, string role)
    {
        // Typ ApkRole jest wewnętrzny — publiczna metoda testu nie może go mieć w sygnaturze.
        Assert.Equal(role, PackageStatusSpec.ClassifyApk(name).ToString());
    }

    [Theory]
    [InlineData("maps/teren.mbtiles", true)]
    [InlineData("maps/teren.SQLITE", true)]
    [InlineData("factions/blufor/grg_map_blufor.kmz", true)]
    [InlineData("atak/punkty.gpx", true)]
    [InlineData("maps/ortofoto.tif", false)]
    [InlineData("datapackage/instrukcja.pdf", false)]
    [InlineData("", false)]
    public void PackagedExtensionsMatchTheBuilder(string path, bool packaged)
    {
        Assert.Equal(packaged, PackageStatusSpec.IsPackaged(path));
    }

    [Fact]
    public void EmptySourceIsStillReadyButSaysWhatIsMissing()
    {
        var status = PackageStatusSpec.Build(Array.Empty<SourceFileEntry>(), TwoSides, english: false, CultureInfo.InvariantCulture);
        Assert.Equal(StatusLevel.Ok, status.Level);
        Assert.Equal("Gotowa do generowania", status.Headline);
        Assert.Equal("2 · BLUFOR, OPFOR", status.Rows.Single(r => r.Label == "Strony konfliktu").Value);
        Assert.Equal(StatusLevel.Info, status.Rows.Single(r => r.Label == "ATAK (APK)").Level);
        Assert.Equal("brak", status.Rows.Single(r => r.Label == "Mapy offline").Value);
        Assert.Equal("brak — tylko mesh", status.Rows.Single(r => r.Label == "Serwer TAK").Value);
        Assert.DoesNotContain(status.Rows, r => r.Label == "Pominięte pliki");
    }

    [Fact]
    public void NoFactionMeansNoPackage()
    {
        var status = PackageStatusSpec.Build(new[] { F("maps/a.mbtiles") }, Array.Empty<FactionStatusEntry>(), english: false, CultureInfo.InvariantCulture);
        Assert.Equal(StatusLevel.Warning, status.Level);
        Assert.Equal(StatusLevel.Warning, status.Rows[0].Level);
        Assert.DoesNotContain(status.Rows, r => r.Label == "Serwer TAK");
    }

    [Fact]
    public void RowsCountWhatTheBuilderWillPack()
    {
        var files = new[]
        {
            F("ATAK/ATAK-CIV-5.4.0.apk"), F("Meshtastic/Meshtastic-2.6.apk"),
            F("plugins/ATAK-Plugin-Meshtastic.apk"), F("plugins/datasync.apk"), F("plugins/vns.apk"),
            F("maps/teren.mbtiles", 1024L * 1024 * 3), F("factions/blufor/mapa_offline_blufor.sqlite", 1024L * 1024),
            F("maps/grg_map_overlay.kmz"), F("atak/punkty_taktyczne.gpx"),
            F("certs/truststore.p12"),
            F("maps/ortofoto.tif"), F("datapackage/instrukcja.pdf"), F("maps/druga.tif"),
            F("config/server.txt"), F("factions/blufor/opord_blufor.json")
        };
        var status = PackageStatusSpec.Build(files, TwoSides, english: false, CultureInfo.InvariantCulture);
        string Value(string label) => status.Rows.Single(r => r.Label == label).Value;

        Assert.Equal("ATAK-CIV-5.4.0.apk", Value("ATAK (APK)"));
        Assert.Equal("Meshtastic-2.6.apk", Value("Meshtastic (APK)"));
        Assert.Equal("3 pliki .apk", Value("Pluginy ATAK"));
        Assert.Equal("2 pliki · 4 MB", Value("Mapy offline"));
        Assert.Equal("2 pliki", Value("Nakładki i trasy"));
        Assert.Equal("1 plik", Value("Certyfikaty i ustawienia"));
        // .txt i .json to pliki robocze — nie są zgłaszane; .tif i .pdf nie trafią do paczki.
        Assert.Equal("3 · .tif, .pdf", Value("Pominięte pliki"));
        Assert.Equal(StatusLevel.Warning, status.Rows.Single(r => r.Label == "Pominięte pliki").Level);
        Assert.Contains("pominięta", status.Hint);
    }

    [Fact]
    public void ServerRowSummarisesHosts()
    {
        var oneHost = PackageStatusSpec.Build(null, new[] { new FactionStatusEntry("BLUFOR", "tak.example.org"), new FactionStatusEntry("OPFOR", "tak.example.org") }, false);
        Assert.Equal("tak.example.org", oneHost.Rows.Single(r => r.Label == "Serwer TAK").Value);
        Assert.Equal(StatusLevel.Ok, oneHost.Rows.Single(r => r.Label == "Serwer TAK").Level);

        var partly = PackageStatusSpec.Build(null, new[] { new FactionStatusEntry("BLUFOR", "a.example.org"), new FactionStatusEntry("OPFOR", "") }, false);
        Assert.Equal("1 z 2 stron", partly.Rows.Single(r => r.Label == "Serwer TAK").Value);
        Assert.Contains("OPFOR: bez serwera", partly.Rows.Single(r => r.Label == "Serwer TAK").Detail);
    }

    [Fact]
    public void EnglishTextsAreTranslated()
    {
        var status = PackageStatusSpec.Build(new[] { F("maps/x.tif") }, TwoSides, english: true, CultureInfo.InvariantCulture);
        Assert.Equal("Ready to build", status.Headline);
        Assert.Contains(status.Rows, r => r.Label == "Skipped files" && r.Value == "1 · .tif");
        Assert.Contains(status.Rows, r => r.Label == "TAK server" && r.Value == "none — mesh only");
    }

    [Theory]
    [InlineData(0, "plików")]
    [InlineData(1, "plik")]
    [InlineData(2, "pliki")]
    [InlineData(4, "pliki")]
    [InlineData(5, "plików")]
    [InlineData(12, "plików")]
    [InlineData(14, "plików")]
    [InlineData(22, "pliki")]
    [InlineData(104, "pliki")]
    [InlineData(112, "plików")]
    public void PolishPluralForms(int count, string expected)
    {
        Assert.Equal(expected, PackageStatusSpec.PolishForm(count, "plik", "pliki", "plików"));
    }

    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(512L, "512 B")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(1048576L, "1 MB")]
    [InlineData(1288490189L, "1.2 GB")]
    public void SizesUseBinaryUnits(long bytes, string expected)
    {
        Assert.Equal(expected, PackageStatusSpec.SizeText(bytes, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void LatestPackageIsTheNewestFile()
    {
        var now = new DateTime(2026, 11, 25, 15, 0, 0);
        var packages = new[]
        {
            new BuiltPackageEntry("C:\\gra\\out\\BLUFOR\\GRA_BLUFOR.ftak", 2 * 1024 * 1024, new DateTime(2026, 11, 25, 14, 32, 0)),
            new BuiltPackageEntry("C:\\gra\\out\\OPFOR\\GRA_OPFOR.ftak", 1024 * 1024, new DateTime(2026, 11, 25, 14, 31, 0)),
            new BuiltPackageEntry("C:\\gra\\out\\STARE\\GRA.ftak", 1024, new DateTime(2026, 11, 20, 9, 0, 0))
        };
        var text = LatestPackagesSpec.Describe(packages, now, english: false, CultureInfo.InvariantCulture);
        Assert.Equal("GRA_BLUFOR.ftak", text.Title);
        Assert.Equal("C:\\gra\\out\\BLUFOR", text.Folder);
        Assert.Equal("2 MB · dziś 14:32 · 2 inne paczki", text.Detail);

        var none = LatestPackagesSpec.Describe(Array.Empty<BuiltPackageEntry>(), now, english: false);
        Assert.Equal("— jeszcze nie zbudowano —", none.Title);
        Assert.Contains("out\\<STRONA>\\", none.Folder);
    }

    [Fact]
    public void WhenTextSaysTodayYesterdayOrDate()
    {
        var now = new DateTime(2026, 11, 25, 8, 0, 0);
        Assert.Equal("dziś 07:59", LatestPackagesSpec.WhenText(new DateTime(2026, 11, 25, 7, 59, 0), now, false));
        Assert.Equal("wczoraj 23:10", LatestPackagesSpec.WhenText(new DateTime(2026, 11, 24, 23, 10, 0), now, false));
        Assert.Equal("20.11.2026 09:00", LatestPackagesSpec.WhenText(new DateTime(2026, 11, 20, 9, 0, 0), now, false));
        Assert.Equal("yesterday 23:10", LatestPackagesSpec.WhenText(new DateTime(2026, 11, 24, 23, 10, 0), now, true));
    }
}
