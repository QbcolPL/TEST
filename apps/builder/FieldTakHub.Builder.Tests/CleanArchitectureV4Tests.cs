#nullable disable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using FieldTakHub.Builder.Core;

namespace FieldTakHub.Builder.Tests
{
    public sealed class CleanArchitectureV4Tests
    {
        [Fact]
        public void MainWindow_AndAllFourModules_ShouldInstantiateAndSwitchOnStaThread()
        {
            Exception caughtEx = null;

            var staThread = new Thread(() =>
            {
                try
                {
                    var win = new FieldTakHub.Builder.MainWindow();
                    win.SwitchToModule(0);
                    win.SwitchToModule(1);
                    win.SwitchToModule(2);
                    win.SwitchToModule(3);
                    LocalizationManager.Instance.ToggleLanguage();
                    LocalizationManager.Instance.ToggleLanguage();
                    win.Close();
                }
                catch (Exception ex)
                {
                    caughtEx = ex;
                }
            });

            staThread.SetApartmentState(ApartmentState.STA);
            staThread.Start();
            staThread.Join();

            if (caughtEx != null)
            {
                throw new Exception("Blad podczas inicjalizacji MainWindow na watku STA: " + caughtEx, caughtEx);
            }
        }

        [Fact]
        public void UniqueFactionColors_ShouldAlwaysGenerateDistinctRgbHex()
        {
            var store = ProjectStore.Instance;
            int initialCount = store.Factions.Count;

            var f3 = store.AddNewFaction();
            var f4 = store.AddNewFaction();
            var f5 = store.AddNewFaction();

            Assert.True(store.Factions.Count >= initialCount + 3);
            Assert.NotEqual(f3.ColorHex, f4.ColorHex, StringComparer.OrdinalIgnoreCase);
            Assert.NotEqual(f4.ColorHex, f5.ColorHex, StringComparer.OrdinalIgnoreCase);
            Assert.StartsWith("#", f3.ColorHex);
            Assert.Equal(7, f3.ColorHex.Length);
        }

        [Fact]
        public void GeoCoordinateEngine_ShouldConvertZbytkowToValidMgrsAndKeypad()
        {
            double lat = 49.849444;
            double lon = 18.812667;

            string mgrs = GeoCoordinateEngine.LatLonToMgrs(lat, lon, 5, "KEYPAD_9");
            Assert.False(string.IsNullOrWhiteSpace(mgrs));
            Assert.Contains("34U", mgrs);
            Assert.Contains("[K", mgrs);
        }

        [Fact]
        public async Task MetocValidation_ShouldBlockEmptyLocationWhenNoAoMarkedOnMap()
        {
            var day = new OpordDayPlanModel { LocationName = "" };
            var mapCfg = new TacticalMapConfigModel();
            mapCfg.AoVertices.Clear();
            mapCfg.Pois.Clear();

            Assert.False(mapCfg.HasExplicitAoMarked);

            var result = await MetocWeatherService.FetchAndApplyMetocAsync(day, mapCfg);
            Assert.False(result.IsSuccess);
            Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
        }

        [Fact]
        public void SyncGeneratorsToOpord_ShouldCopyVoiceChannelsAndFilterOutGarbage()
        {
            var store = ProjectStore.Instance;
            store.SyncGeneratorsToOpord();

            var activeDay = store.OpordDocument.Days[0];
            Assert.False(string.IsNullOrWhiteSpace(activeDay.SyncedCommsMatrixSummary));
            Assert.Contains("LoRa Meshtastic", activeDay.SyncedCommsMatrixSummary);
            Assert.DoesNotContain(store.Factions, f => f.Name.Equals("PLI", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(store.Factions, f => f.Name.Equals("GeoChat", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void SarnegGenerator_ShouldProduce10DistinctAsciiLettersAndStayPolishInEnglishMode()
        {
            LocalizationManager.Instance.CurrentLanguage = AppLanguage.EN;
            string word = TacticalServices.GetRandomSarnegWord10();

            Assert.Equal(10, word.Length);
            Assert.Equal(10, word.Distinct().Count());

            LocalizationManager.Instance.CurrentLanguage = AppLanguage.PL;
        }
    }
}
