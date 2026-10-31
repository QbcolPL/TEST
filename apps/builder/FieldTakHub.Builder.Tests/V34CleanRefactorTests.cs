#nullable disable
using System;
using System.IO;
using Xunit;
using FieldTakHub.Builder.Services;

namespace FieldTakHub.Builder.Tests
{
    public sealed class V34CleanRefactorTests
    {
        [Fact]
        public void SharedFolderStructure_ShouldCreateApkPluginsMapsAndOutDirectories()
        {
            string wspolneDir = V34CleanFtakAndFactionEngine.EnsureSharedFolderStructure();
            Assert.True(Directory.Exists(wspolneDir));
            Assert.True(Directory.Exists(Path.Combine(wspolneDir, "apk")));
            Assert.True(Directory.Exists(Path.Combine(wspolneDir, "plugins")));
            Assert.True(Directory.Exists(Path.Combine(wspolneDir, "maps")));

            string orgaOut = V34CleanFtakAndFactionEngine.EnsureFactionOutputDir("ORGA");
            Assert.True(Directory.Exists(orgaOut));
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
            // Zbytków / Strumień: 49°50'58.0"N 18°48'45.6"E
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
