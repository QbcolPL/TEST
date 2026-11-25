using System;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using FieldTakHub.Builder.Services;
using FieldTakHub.Builder.Services.Shell;
using Xunit;

namespace FieldTakHub.Builder.Tests;

/// <summary>Generator paczek .ftak, etap 1: paczka zgodna z ekranem, bez atrap, adresy Wi-Fi z tokenem.</summary>
public class PackageGeneratorStage1Tests
{
    private static readonly string Key32 = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
    private static readonly string Key16 = Convert.ToBase64String(Enumerable.Range(1, 16).Select(i => (byte)i).ToArray());

    [Theory]
    [InlineData("")]
    [InlineData("tak.example.org")]
    [InlineData("tak.example.org:8089")]
    [InlineData("10.0.0.5:8443")]
    [InlineData("  serwer-tak.pl  ")]
    public void ServerAddressAcceptsHostOrHostAndPort(string address)
    {
        Assert.Null(TakServerAddress.Describe(address, english: false));
    }

    [Theory]
    [InlineData("https://tak.example.org")]
    [InlineData("tak://tak.example.org")]
    [InlineData("tak.example.org/api")]
    [InlineData("tak example.org")]
    [InlineData("tak.example.org:99999")]
    [InlineData("tak.example.org:abc")]
    [InlineData(":8089")]
    [InlineData("fe80::1")]
    [InlineData(".tak.example.org")]
    [InlineData("tak..example.org")]
    [InlineData("admin@tak.example.org")]
    public void ServerAddressRejectsWhatThePhoneCannotUse(string address)
    {
        Assert.NotNull(TakServerAddress.Describe(address, english: false));
        Assert.NotNull(TakServerAddress.Describe(address, english: true));
    }

    [Fact]
    public void ChannelKeyKindsMatchMeshtastic()
    {
        Assert.Equal(ChannelKeyKind.Aes256, ChannelKeySpec.Classify(Key32));
        Assert.Equal(ChannelKeyKind.Aes128, ChannelKeySpec.Classify(Key16));
        Assert.Equal(ChannelKeyKind.PublicDefault, ChannelKeySpec.Classify("AQ=="));
        Assert.Equal(ChannelKeyKind.Missing, ChannelKeySpec.Classify(""));
        Assert.Equal(ChannelKeyKind.Missing, ChannelKeySpec.Classify(null));
        Assert.Equal(ChannelKeyKind.Invalid, ChannelKeySpec.Classify("to nie jest base64!"));
        Assert.Equal(ChannelKeyKind.WrongLength, ChannelKeySpec.Classify(Convert.ToBase64String(new byte[7])));

        Assert.True(ChannelKeySpec.IsPrivate(Key32));
        Assert.False(ChannelKeySpec.IsPrivate("AQ=="));
        Assert.Null(ChannelKeySpec.Describe(Key32, english: false));
        Assert.Contains("publiczny", ChannelKeySpec.Describe("", english: false));
        Assert.Contains("7 B", ChannelKeySpec.Describe(Convert.ToBase64String(new byte[7]), english: false));
        Assert.Contains("AES-128", ChannelKeySpec.OkText(Key16, english: false));
        Assert.Contains("AES-256", ChannelKeySpec.OkText(Key32, english: true));
    }

    [Fact]
    public void EnrollmentLinkNeedsHostUserAndToken()
    {
        Assert.Null(EnrollmentLinkSpec.Describe("", english: false));
        Assert.Null(EnrollmentLinkSpec.Describe("tak://com.atakmap.app/enroll?host=tak.example.org&username=jan&token=abc123", english: false));
        Assert.Contains("token", EnrollmentLinkSpec.Describe("tak://com.atakmap.app/enroll?host=tak.example.org&username=jan", english: false));
        Assert.NotNull(EnrollmentLinkSpec.Describe("https://tak.example.org/enroll", english: false));
    }

    [Theory]
    [InlineData("https://meshtastic.org/e/#CgMSAQE", true)]
    [InlineData("https://meshtastic.org/e/?add=true#CgMSAQE", true)]
    [InlineData("HTTPS://MESHTASTIC.ORG/E/#abc", true)]
    [InlineData("https://meshtastic.org/e/", false)]
    [InlineData("http://example.org/#abc", false)]
    [InlineData("", false)]
    public void ChannelLinkFollowsThePhoneRule(string url, bool isLink)
    {
        Assert.Equal(isLink, MeshChannelLinkSpec.IsChannelLink(url));
        if (!isLink && url.Length > 0) Assert.NotNull(MeshChannelLinkSpec.Describe(url, english: false));
        if (isLink) Assert.Null(MeshChannelLinkSpec.Describe(url, english: false));
    }

    [Fact]
    public void ChannelNameIsCutAtElevenBytesWithoutBreakingLetters()
    {
        Assert.Equal("BLUFOR-MESH", ChannelNameSpec.Effective("BLUFOR-MESH"));
        Assert.Null(ChannelNameSpec.Describe("BLUFOR-MESH", english: false));
        Assert.Equal("MILSIM-TAK-", ChannelNameSpec.Effective("MILSIM-TAK-BLUE"));
        Assert.Contains("MILSIM-TAK-", ChannelNameSpec.Describe("MILSIM-TAK-BLUE", english: false));
        // Polska litera to 2 bajty: Ż Ó Ł = 6 B, potem „TA-ST” = 5 B.
        Assert.Equal("ŻÓŁTA-ST", ChannelNameSpec.Effective("ŻÓŁTA-STREFA"));
        Assert.Equal("", ChannelNameSpec.Effective(null));
    }

    [Fact]
    public void PositionReportingMapsToProfilesThePhoneKnows()
    {
        Assert.Equal("Dynamic", PositionReportingSpec.Normalize("dynamic"));
        Assert.Equal("Constant", PositionReportingSpec.Normalize("cokolwiek"));
        Assert.Equal("Dynamic", PositionReportingSpec.ToPliProfile("Dynamic"));
        Assert.Equal("ConstRateUnreliable", PositionReportingSpec.ToPliProfile("Constant"));
        Assert.Equal("ConstRateUnreliable", PositionReportingSpec.ToPliProfile(null));

        Assert.Equal(PositionReportingSpec.Options[1], PositionReportingSpec.Display("Dynamic"));
        Assert.Equal("Dynamic", PositionReportingSpec.FromDisplay(PositionReportingSpec.Options[1]));
        Assert.Equal("Constant", PositionReportingSpec.FromDisplay(PositionReportingSpec.Options[0]));
        // Pozycja przetłumaczona przez słownik BuilderI18n też wraca do modelu.
        Assert.Equal("Dynamic", PositionReportingSpec.FromDisplay("Dynamic — 2 to 20 s, depending on movement"));
    }

    [Theory]
    [InlineData("EU_868", "EU_868", 3)]
    [InlineData("eu_433", "EU_433", 2)]
    [InlineData("US_915", "US", 1)]
    [InlineData("US", "US", 1)]
    [InlineData("ANZ_915", "ANZ", 6)]
    [InlineData("UA_868", "UA_868", 15)]
    [InlineData("UNSET", "EU_868", 3)]
    [InlineData("bzdura", "EU_868", 3)]
    [InlineData("", "EU_868", 3)]
    public void LoraRegionsUseMeshtasticNamesAndNumbers(string input, string name, int code)
    {
        Assert.Equal(name, LoraRegionSpec.Normalize(input));
        Assert.Equal(code, LoraRegionSpec.Code(input));
    }

    [Fact]
    public void LoraRegionListOffersOnlyNamesTheCodersKnow()
    {
        foreach (string region in LoraRegionSpec.Options)
            Assert.Equal(region, LoraRegionSpec.Normalize(region));
    }

    [Theory]
    [InlineData("24", true, 24)]
    [InlineData("0", true, 0)]
    [InlineData(" 48 ", true, 48)]
    [InlineData("6h", true, 6)]
    [InlineData("876000", true, 876000)]
    [InlineData("876001", false, 0)]
    [InlineData("-5", false, 0)]
    [InlineData("abc", false, 0)]
    [InlineData("", false, 0)]
    public void ValidityHoursParse(string text, bool ok, int hours)
    {
        Assert.Equal(ok, PackageValiditySpec.TryParseHours(text, out int parsed));
        Assert.Equal(hours, parsed);
    }

    [Fact]
    public void ValidityFromProjectDefaultsToNoLimit()
    {
        Assert.Equal(24, PackageValiditySpec.FromJson(JsonValue.Create(24)));
        Assert.Equal(12, PackageValiditySpec.FromJson(JsonValue.Create("12")));
        Assert.Equal(0, PackageValiditySpec.FromJson(null));
        Assert.Equal(0, PackageValiditySpec.FromJson(JsonValue.Create("bzdura")));
    }

    [Fact]
    public void RetiredSamplePhoneIsDropped()
    {
        Assert.Equal("", SampleDataSpec.CleanEmergencyPhone("+48 600 000 000"));
        Assert.Equal("", SampleDataSpec.CleanEmergencyPhone("+48600000000"));
        Assert.Equal("+48 601 234 567", SampleDataSpec.CleanEmergencyPhone(" +48 601 234 567 "));
        Assert.Equal("", SampleDataSpec.CleanEmergencyPhone(null));
    }

    [Fact]
    public void OnlyUntouchedSampleOverlaysAreRecognised()
    {
        string sample = SampleDataSpec.SampleOverlayContent("blufor");
        // Dokładnie ta treść, którą tworzył dawny przycisk folderu nakładek (TacticalConfigPanel.EnsureFactionFoldersInitialized).
        Assert.Equal("<?xml version=\"1.0\" encoding=\"UTF-8\"?><kml xmlns=\"http://www.opengis.net/kml/2.2\"><Document><name>Warstwa BLUFOR</name>"
            + "<Placemark><name>HQ BLUFOR</name><Point><coordinates>19.0238,49.9135,0</coordinates></Point></Placemark></Document></kml>", sample);

        Assert.True(SampleDataSpec.IsGeneratedSampleOverlay("strefa_blufor.kml", "blufor", sample));
        Assert.True(SampleDataSpec.IsGeneratedSampleOverlay("STREFA_BLUFOR.KML", "blufor", "﻿" + sample));
        Assert.False(SampleDataSpec.IsGeneratedSampleOverlay("strefa_blufor.kml", "blufor", sample.Replace("19.0238", "19.5")));
        Assert.False(SampleDataSpec.IsGeneratedSampleOverlay("strefa_opfor.kml", "blufor", sample));
        Assert.False(SampleDataSpec.IsGeneratedSampleOverlay("strefa_blufor.kml", "blufor", null));
    }

    [Fact]
    public void OnlyUntouchedSampleVoiceChannelsAreRecognised()
    {
        var first = new (string?, string?, string?, string?)[]
        {
            ("Dowodzenie HQ", "PMR", "CH 1 (446.00625)", "CTCSS 67.0 Hz"),
            ("Taktyczny Alfa", "VHF/UHF", "145.500 MHz", "CTCSS 88.5 Hz"),
            ("Ratunkowy ORGA", "PMR", "CH 8 (446.09375)", "Brak tonu")
        };
        var second = new (string?, string?, string?, string?)[]
        {
            ("Dowodzenie HQ", "PMR", "CH 4 (446.04375)", "CTCSS 100.0 Hz"),
            ("Taktyczny Bravo", "DMR", "433.450 MHz", "CC1 TS1 TG99"),
            (" Ratunkowy ORGA ", "PMR", "CH 8 (446.09375)", "Brak tonu")
        };
        Assert.True(SampleDataSpec.IsRetiredSampleVoiceSet(first));
        Assert.True(SampleDataSpec.IsRetiredSampleVoiceSet(second));

        var edited = first.ToArray();
        edited[1] = ("Taktyczny Alfa", "VHF/UHF", "145.525 MHz", "CTCSS 88.5 Hz");
        Assert.False(SampleDataSpec.IsRetiredSampleVoiceSet(edited));
        Assert.False(SampleDataSpec.IsRetiredSampleVoiceSet(first.Take(2).ToArray()));
        Assert.False(SampleDataSpec.IsRetiredSampleVoiceSet(Enumerable.Reverse(first).ToArray()));
        Assert.False(SampleDataSpec.IsRetiredSampleVoiceSet(first.Concat(new (string?, string?, string?, string?)[] { ("Kanał 4", "PMR", "CH 2", "") }).ToArray()));
        Assert.False(SampleDataSpec.IsRetiredSampleVoiceSet(Array.Empty<(string?, string?, string?, string?)>()));
        Assert.False(SampleDataSpec.IsRetiredSampleVoiceSet(null));
    }

    [Fact]
    public void DuplicateFactionNamesBlockTheBuild()
    {
        var problems = SideSettingsSpec.Check(new[]
        {
            new SideSettings("BLUFOR", Key32, "", "", "", "BLUFOR-MESH"),
            new SideSettings("blufor", Key32, "", "", "", "BLUE-MESH")
        }, english: false);
        Assert.Contains(problems, p => p.Blocking && p.Message.Contains("tę samą nazwę"));
        Assert.Equal("BLUFOR", SideSettingsSpec.FolderKey("blufor"));
    }

    [Fact]
    public void SideProblemsNameTheSideAndSkipKeyWhenTheLinkDecides()
    {
        var problems = SideSettingsSpec.Check(new[]
        {
            new SideSettings("BLUFOR", "", "https://tak.example.org", "", "", "BLUFOR-MESH"),
            new SideSettings("OPFOR", "", "", "", "https://meshtastic.org/e/#CgMSAQE", "OPFOR-MESH"),
            new SideSettings("ORGA", Key32, "tak.example.org:8089", "", "", "ORGA-HQ")
        }, english: false);

        Assert.All(problems, p => Assert.False(p.Blocking));
        Assert.Contains(problems, p => p.Side == "BLUFOR" && p.Message.Contains("publiczny"));
        Assert.Contains(problems, p => p.Side == "BLUFOR" && p.Message.Contains("http"));
        Assert.DoesNotContain(problems, p => p.Side == "OPFOR");
        Assert.DoesNotContain(problems, p => p.Side == "ORGA");
    }

    [Fact]
    public void UnknownFieldsAreNotChecked()
    {
        Assert.Empty(SideSettingsSpec.Check(new[] { new SideSettings("BLUFOR"), new SideSettings("OPFOR") }, english: false));
        Assert.Empty(SideSettingsSpec.Check(null, english: false));
    }

    [Fact]
    public void LanTokensAreRandomStableAndNotTheFactionName()
    {
        var tokens = new LanShareTokens();
        string blufor = tokens.TokenFor("BLUFOR");
        string opfor = tokens.TokenFor("OPFOR");

        Assert.Equal(32, blufor.Length);
        Assert.Matches("^[0-9a-f]{32}$", blufor);
        Assert.NotEqual(blufor, opfor);
        Assert.Equal(blufor, tokens.TokenFor("blufor"));

        Assert.True(tokens.TryResolve(blufor, out string faction));
        Assert.Equal("BLUFOR", faction);
        Assert.False(tokens.TryResolve("BLUFOR", out _));
        string upper = blufor.ToUpperInvariant();
        if (upper != blufor) Assert.False(tokens.TryResolve(upper, out _));
        Assert.False(tokens.TryResolve(null, out _));
        Assert.NotEqual(blufor, new LanShareTokens().TokenFor("BLUFOR"));
    }

    [Fact]
    public void SnapshotTakesTheCurrentStateNotAFileOnDisk()
    {
        var workspace = new JsonObject
        {
            ["eventName"] = "Jesienna Gra",
            ["packageValidityHours"] = 24,
            ["factions"] = new JsonArray(
                new JsonObject
                {
                    ["Name"] = "NIEBIESCY",
                    ["ColorHex"] = "#2684FF",
                    ["ChannelName"] = "BLUE-MESH",
                    ["Psk"] = Key32,
                    ["LoraRegion"] = "US_915",
                    ["HopLimit"] = 4,
                    ["OtsHost"] = " tak.example.org:8089 ",
                    ["EnrollmentUri"] = "",
                    ["EmergencyPhone"] = "+48 600 000 000",
                    ["PliStrategy"] = "Dynamic",
                    ["VoiceChannels"] = new JsonArray(new JsonObject { ["roleName"] = "Dowodzenie", ["bandType"] = "PMR", ["freqMhz"] = "CH 3", ["toneOrCode"] = "" })
                },
                new JsonObject { ["Name"] = "CZERWONI", ["Psk"] = Key16, ["VoiceChannels"] = new JsonArray() })
        };

        var snapshot = WorkspaceSnapshotSpec.FromWorkspaceJson(workspace, "Operacja_TAK");
        Assert.Equal("Jesienna Gra", snapshot.EventName);
        Assert.Equal(2, snapshot.Factions.Count);

        var blue = snapshot.Factions[0];
        Assert.Equal("NIEBIESCY", blue.Name);
        Assert.Equal(Key32, blue.Psk);
        Assert.Equal("US", blue.LoraRegion);
        Assert.Equal(4, blue.HopLimit);
        Assert.Equal("tak.example.org:8089", blue.OtsHost);
        Assert.Equal("", blue.EmergencyPhone);
        Assert.Equal("Dynamic", blue.PliProfile);
        Assert.Single(blue.VoiceChannels);
        Assert.Equal("CH 3", blue.VoiceChannels[0].FreqMhz);

        var red = snapshot.Factions[1];
        Assert.Equal("ConstRateUnreliable", red.PliProfile);
        Assert.Empty(red.VoiceChannels);
        Assert.Equal("EU_868", red.LoraRegion);
    }

    [Fact]
    public void SnapshotFallsBackToTheWindowEventName()
    {
        var noName = WorkspaceSnapshotSpec.FromWorkspaceJson(new JsonObject { ["factions"] = new JsonArray() }, "Operacja_TAK");
        Assert.Equal("Operacja_TAK", noName.EventName);
        Assert.Empty(noName.Factions);

        var nothing = WorkspaceSnapshotSpec.FromWorkspaceJson(null, "Gra");
        Assert.Equal("Gra", nothing.EventName);
    }

    [Fact]
    public void ZeroValidityDoesNotMarkOlderProjectsAsChanged()
    {
        var older = new JsonObject { ["eventName"] = "GRA", ["factions"] = new JsonArray() };
        var withZero = new JsonObject { ["eventName"] = "GRA", ["packageValidityHours"] = 0, ["factions"] = new JsonArray() };
        var withDay = new JsonObject { ["eventName"] = "GRA", ["packageValidityHours"] = 24, ["factions"] = new JsonArray() };

        Assert.Equal(ProjectSignature.Compute(older), ProjectSignature.Compute(withZero));
        Assert.NotEqual(ProjectSignature.Compute(older), ProjectSignature.Compute(withDay));
    }

    [Fact]
    public void StatusCardShowsSideProblems()
    {
        var good = PackageStatusSpec.Build(null, new[]
        {
            new FactionStatusEntry("BLUFOR", "tak.example.org", ChannelKey: Key32, EnrollmentLink: "", ChannelLink: "", ChannelName: "BLUFOR-MESH"),
            new FactionStatusEntry("OPFOR", "", ChannelKey: Key16, EnrollmentLink: "", ChannelLink: "", ChannelName: "OPFOR-MESH")
        }, english: false, CultureInfo.InvariantCulture);
        Assert.Equal(StatusLevel.Ok, good.Level);
        Assert.DoesNotContain(good.Rows, r => r.Label == "Do poprawienia");

        var noKey = PackageStatusSpec.Build(null, new[]
        {
            new FactionStatusEntry("BLUFOR", "", ChannelKey: "", EnrollmentLink: "", ChannelLink: "", ChannelName: "BLUFOR-MESH")
        }, english: false, CultureInfo.InvariantCulture);
        var row = noKey.Rows.Single(r => r.Label == "Do poprawienia");
        Assert.Equal(StatusLevel.Warning, row.Level);
        Assert.Contains("BLUFOR", row.Value);
        Assert.Contains("publiczny", row.Detail);
        Assert.Equal("Gotowa do generowania", noKey.Headline);

        var twins = PackageStatusSpec.Build(null, new[]
        {
            new FactionStatusEntry("BLUFOR", "", ChannelKey: Key32),
            new FactionStatusEntry("Blufor", "", ChannelKey: Key32)
        }, english: false, CultureInfo.InvariantCulture);
        Assert.Equal(StatusLevel.Warning, twins.Level);
        Assert.Equal("Popraw przed budowaniem", twins.Headline);
    }
}
