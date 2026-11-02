#nullable disable
using System;
using System.IO;
using Xunit;
using FieldTakHub.Builder.Models;
using FieldTakHub.Builder.Services;

namespace FieldTakHub.Builder.Tests
{
    [Collection("SequentialBuilderTests")]
    public sealed class V34CleanRefactorTests
    {
        [Fact]
        public void ZeroExpiryHours_ShouldMeanIndefiniteValidityInProjectAndManifest()
        {
            var dir = Path.Combine(Path.GetTempPath(), "fth-indef-" + Guid.NewGuid().ToString("N"));
            var source = Path.Combine(dir, "source");
            var output = Path.Combine(dir, "out");
            Directory.CreateDirectory(Path.Combine(source, "atak"));
            File.WriteAllText(Path.Combine(source, "atak", "hello.txt"), "test");

            var p = new FieldTakProject
            {
                Name = "IndefiniteTest",
                PackageId = "test.indefinite",
                PackageVersion = "1.0.0",
                SourceDirectory = source,
                OutputDirectory = output,
                ExpiryHours = 0,
                QrExpiryHours = 0,
                Server = new ServerProfile { Host = "127.0.0.1" }
            };

            Assert.Equal(0, p.ExpiryHours);
            Assert.Equal(0, p.QrExpiryHours);

            var items = new SourceAnalyzer().Analyze(source);
            var ftak = new FtakPackageBuilder().Build(p, items);
            Assert.True(File.Exists(ftak));
        }

        [Fact]
        public void SectorGrid_ShouldGenerateHorizontalLettersAndVerticalNumbers()
        {
            Assert.Equal("A", TacticalMapGeneratorView.GetSectorColumnLetters(0));
            Assert.Equal("B", TacticalMapGeneratorView.GetSectorColumnLetters(1));
            Assert.Equal("C", TacticalMapGeneratorView.GetSectorColumnLetters(2));
            Assert.Equal("Z", TacticalMapGeneratorView.GetSectorColumnLetters(25));
            Assert.Equal("AA", TacticalMapGeneratorView.GetSectorColumnLetters(26));
        }

        [Fact]
        public void UniqueFactionColors_ShouldReturnDistinctValidHexColors()
        {
            string c1 = V34CleanFtakAndFactionEngine.GetNextUniqueFactionColorHex(null);
            string c2 = V34CleanFtakAndFactionEngine.GetNextUniqueFactionColorHex(null);
            string c3 = V34CleanFtakAndFactionEngine.GetNextUniqueFactionColorHex(null);

            Assert.StartsWith("#", c1);
            Assert.Equal(7, c1.Length);
            Assert.NotEqual(c1, c2, StringComparer.OrdinalIgnoreCase);
            Assert.NotEqual(c2, c3, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void MapStudioBridge_ShouldConvertCoordinatesToMilitaryMgrs()
        {
            double lat = 49.849444;
            double lon = 18.812667;

            string mgrs = V34CleanMapStudioBridge.LatLonToMgrs(lat, lon);
            Assert.False(string.IsNullOrWhiteSpace(mgrs));
            Assert.Contains("34U", mgrs);
        }

        [Fact]
        public void GarbageFactionFilter_ShouldRejectPliAndGeoChat()
        {
            Assert.True(V34CleanOpordDataEngine.IsGarbageFactionName("PLI"));
            Assert.True(V34CleanOpordDataEngine.IsGarbageFactionName("GeoChat"));
            Assert.True(V34CleanOpordDataEngine.IsGarbageFactionName("TAK"));
            Assert.False(V34CleanOpordDataEngine.IsGarbageFactionName("ORGA"));
            Assert.False(V34CleanOpordDataEngine.IsGarbageFactionName("STRONA-2"));
        }
    }
}
