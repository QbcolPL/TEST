#pragma warning disable CS0414, CS0649, CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FieldTakHub.Builder.Services
{
    // =========================================================================
    // 1. PEŁNY SŁOWNIK LOKALIZACJI INTERFEJSU (PL <-> ENG)
    // =========================================================================
    internal static class BuilderI18n
    {
        public static string CurrentLang { get; private set; } = LoadSavedLanguage();
        public static event Action<string> LanguageChanged;

        private static readonly Dictionary<string, string> PlToEn = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["📦 Konfigurator Paczek (.ftak)"] = "📦 Faction & Package Builder (.ftak)",
            ["📋 Generator Rozkazu (OPORD)"] = "📋 NATO / MilSim OPORD Builder",
            ["🗺️ Generator Map Taktycznych 1:1"] = "🗺️ 1:1 Tactical Map Studio",
            ["🔌 Masowy Programator USB LoRa"] = "🔌 Bulk USB LoRa Programmer",
            ["💾 Zapisz projekt roboczy (.fthproj)"] = "💾 Save Workspace (.fthproj)",
            ["📂 Wczytaj projekt (.fthproj)"] = "📂 Load Workspace (.fthproj)",
            ["☕ Wesprzyj twórcę"] = "☕ Support Creator",
            ["➕ Dodaj stronę"] = "➕ Add Faction",
            ["🗑 Usuń stronę"] = "🗑 Remove Faction",
            ["🖨 Plakat A4 Safezone (HTML)"] = "🖨 A4 Safezone Poster (HTML)",
            ["🔑 Generuj nowy PSK (AES-256)"] = "🔑 Generate New PSK (AES-256)",
            ["➕ Dodaj kanał"] = "➕ Add Channel",
            ["⚡ Buduj .FTAK dla WSZYSTKICH stron"] = "⚡ Build .FTAK for ALL Factions",
            ["📦 Buduj .FTAK dla aktywnej strony"] = "📦 Build .FTAK for Active Faction",
            ["📱 Pokaż Kod QR aktywnej strony (Wi-Fi / Offline)"] = "📱 Show Active Faction QR (Wi-Fi / Offline)",
            ["📂 Otwórz folder z plikiem .FTAK"] = "📂 Open .FTAK Output Folder",
            ["Zamknij"] = "Close",
            ["Anuluj"] = "Cancel",
            ["Nazwa wydarzenia / operacji"] = "Event / Operation Name",
            ["Nazwa strony konfliktu"] = "Faction / Side Name",
            ["Kolor taktyczny (HEX)"] = "Tactical Color (HEX)",
            ["Nazwa kanału Meshtastic"] = "Meshtastic Channel Name",
            ["Klucz szyfrowania PSK (Base64)"] = "Encryption Key PSK (Base64)",
            ["Region LoRa"] = "LoRa Region",
            ["Preset modemu LoRa"] = "LoRa Modem Preset",
            ["Limit skoków (Hop Limit)"] = "Hop Limit",
            ["Adres serwera OpenTAK (OTS Host)"] = "OpenTAKServer Address (OTS Host)",
            ["Telefon alarmowy ORGA / MEDEVAC"] = "Emergency Phone (ORGA / MEDEVAC)",
            ["Kanały Głosowe (PMR / VHF / UHF / DMR) — Ściąga Operacyjna"] = "Voice Radio Channels (PMR / VHF / UHF / DMR) — Field Cheat Sheet",
            ["Szukaj miejscowości lub współrzędnych"] = "Search town or coordinates",
            ["Skala mapy"] = "Map Scale",
            ["Orientacja arkusza"] = "Sheet Orientation",
            ["Format arkusza"] = "Sheet Format",
            ["Warstwa podkładowa"] = "Base Map Layer",
            ["Pokaż cieniowanie LiDAR (ISOK)"] = "Show LiDAR Hillshade (ISOK)",
            ["Przezroczystość LiDAR"] = "LiDAR Opacity",
            ["System współrzędnych siatki"] = "Grid Coordinate System",
            ["Eksportuj Mapę A4 (HTML / Druk PDF)"] = "Export A4 Map (HTML / Print PDF)",
            ["Eksportuj KMZ (ATAK Overlay)"] = "Export KMZ (ATAK Overlay)",
            ["Eksportuj GPX (Garmin / Suunto)"] = "Export GPX (Garmin / Suunto)",
            ["Buduj Mapę Offline (.mbtiles / .sqlitedb)"] = "Build Offline Map (.mbtiles / .sqlitedb)"
        };

        private static readonly Dictionary<string, string> EnToPl = PlToEn
            .GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Key, StringComparer.OrdinalIgnoreCase);

        public static string T(string plText, string enText) =>
            string.Equals(CurrentLang, "EN", StringComparison.OrdinalIgnoreCase) ? enText : plText;

        public static void ToggleLanguage(Window win)
        {
            SetLanguage(string.Equals(CurrentLang, "EN", StringComparison.OrdinalIgnoreCase) ? "PL" : "EN", win);
        }

        public static void SetLanguage(string langCode, Window win)
        {
            CurrentLang = string.Equals(langCode, "EN", StringComparison.OrdinalIgnoreCase) ? "EN" : "PL";
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FieldTakHub");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "ui_lang.txt"), CurrentLang, Encoding.UTF8);
            }
            catch { }

            if (win != null)
            {
                ApplyLanguageToVisualTree(win);
            }
            LanguageChanged?.Invoke(CurrentLang);
        }

        public static void ApplyLanguageToVisualTree(DependencyObject root)
        {
            if (root == null) return;
            bool toEn = string.Equals(CurrentLang, "EN", StringComparison.OrdinalIgnoreCase);
            var map = toEn ? PlToEn : EnToPl;

            var queue = new Queue<DependencyObject>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var curr = queue.Dequeue();
                if (curr is TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text))
                {
                    string trimmed = tb.Text.Trim();
                    if (map.TryGetValue(trimmed, out string translated))
                        tb.Text = translated;
                }
                else if (curr is ContentControl cc && cc.Content is string s && !string.IsNullOrWhiteSpace(s))
                {
                    string trimmed = s.Trim();
                    if (map.TryGetValue(trimmed, out string translated))
                        cc.Content = translated;
                }

                int count = 0;
                try { count = VisualTreeHelper.GetChildrenCount(curr); } catch { }
                for (int i = 0; i < count; i++)
                {
                    DependencyObject child = null;
                    try { child = VisualTreeHelper.GetChild(curr, i); } catch { }
                    if (child != null) queue.Enqueue(child);
                }
            }
        }

        private static string LoadSavedLanguage()
        {
            try
            {
                string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FieldTakHub", "ui_lang.txt");
                if (File.Exists(p))
                {
                    string raw = File.ReadAllText(p, Encoding.UTF8).Trim().ToUpperInvariant();
                    if (raw == "EN" || raw == "PL") return raw;
                }
            }
            catch { }
            return "PL";
        }
    }

    // =========================================================================
    // 2. GENERATOR SARNEG OPARTY NA ZAŁĄCZONEJ LIŚCIE 100 SŁÓW (sarneg_words_PL_EN.txt)
    // =========================================================================
    internal static class SarnegDictionaryService
    {
        private static readonly Random Rng = new Random();
        private static int _lastWordIndex = -1;

        // Dokładna lista 100 dziesięcioliterowych słów (PL + EN) z załączonego pliku sarneg_words_PL_EN.txt
        public static readonly string[] AttachedSarnegWordsList = new string[]
        {
            "absolutnie", "dostaniemy", "dwukrotnie", "faktycznie", "informacje",
            "instrukcje", "miasteczko", "miasteczku", "najgorszym", "najlepszym",
            "obrzydliwe", "obserwacji", "pierwszych", "podejrzany", "porucznika",
            "potwierdza", "prowadzimy", "prysznicem", "przeciwnym", "przedmioty",
            "przekonamy", "przyjaciel", "przystanek", "publicznej", "spodziewam",
            "sprawdzimy", "stworzenia", "stypendium", "szacunkiem", "tajemniczy",
            "trzymajcie", "utrzymanie", "zachodniej", "zostaniemy",
            "aftershock", "algorithms", "artichokes", "background", "bankruptcy",
            "beachfront", "benchmarks", "binoculars", "birthplace", "blacksmith",
            "blockheads", "blueprints", "boyfriends", "breakdowns", "campground",
            "compatible", "complaints", "cornfields", "decorating", "downstream",
            "earthlings", "exhausting", "flavorings", "flowcharts", "fruitcakes",
            "harvesting", "headstrong", "hyperlinks", "hypocrites", "importance",
            "journalism", "journalist", "lifeguards", "lumberjack", "manuscript",
            "matchboxes", "methodical", "microwaves", "misfortune", "nightclubs",
            "nightmares", "overtaking", "paintbrush", "pathfinder", "playground",
            "playthings", "podcasting", "precaution", "previously", "productive",
            "profitable", "purchasing", "regulation", "scheduling", "scrambling",
            "smartphone", "soundtrack", "stampeding", "stockpiled", "superwoman",
            "tambourine", "timberland", "trampoline", "truckloads", "wanderlust",
            "workplaces"
        };

        public static string NormalizePolishChars(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            return input.ToUpperInvariant()
                .Replace('Ą', 'A').Replace('Ć', 'C').Replace('Ę', 'E')
                .Replace('Ł', 'L').Replace('Ń', 'N').Replace('Ó', 'O')
                .Replace('Ś', 'S').Replace('Ź', 'Z').Replace('Ż', 'Z');
        }

        public static bool Has10UniqueLetters(string word)
        {
            string clean = NormalizePolishChars(word).Trim();
            if (clean.Length != 10) return false;
            return clean.Distinct().Count() == 10 && clean.All(char.IsLetter);
        }

        public static string DrawRandomSarnegWord()
        {
            string[] pool = AttachedSarnegWordsList;
            if (pool.Length == 0) return "PRZEDMIOTY";

            int idx = Rng.Next(pool.Length);
            if (pool.Length > 1 && idx == _lastWordIndex)
            {
                idx = (idx + 1) % pool.Length;
            }
            _lastWordIndex = idx;
            return NormalizePolishChars(pool[idx]);
        }

        public static string DrawRandomWord(bool english) => DrawRandomSarnegWord();
    }

    // =========================================================================
    // 3. SERWIS POGODOWO-ASTRONOMICZNY METOC + EKSTRAKCJA OBSZARU AO Z MAPY 1:1
    // =========================================================================
    internal sealed class MetocReportData
    {
        public string DateIso { get; set; } = "";
        public string LocationLabel { get; set; } = "";
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string ConditionText { get; set; } = "";
        public string TempRangeText { get; set; } = "";
        public string WindText { get; set; } = "";
        public string HumidityAndPrecipText { get; set; } = "";
        public string SunTimesText { get; set; } = "";
        public string MoonPhaseText { get; set; } = "";
        public string FullTacticalSummary { get; set; } = "";
    }

    internal static class OpordMetocWeatherService
    {
        public static readonly HttpClient Http = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "FieldTakHub-OPORD-METOC/3.2");
            return c;
        }

        public static (bool Found, double Lat, double Lon, string SourceDescription, string ExtractedPoiList) TryExtractAoCenterFromMapStudio(Window win)
        {
            var lats = new List<double>();
            var lons = new List<double>();
            var poiNames = new List<string>();
            string srcDesc = BuilderI18n.T("Obszar AO z Generatora Map 1:1", "AO from 1:1 Map Generator");

            try
            {
                string sourceDir = TacticalConfigPanel.GetPrimarySourceDirectory(win);
                if (Directory.Exists(sourceDir))
                {
                    var mapFiles = Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories)
                        .Where(f => f.EndsWith(".gpx", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".kml", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith("map_state.json", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".fthproj", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(File.GetLastWriteTimeUtc)
                        .ToList();

                    if (!string.IsNullOrWhiteSpace(TacticalConfigPanel.CurrentDraftProjectPath) && File.Exists(TacticalConfigPanel.CurrentDraftProjectPath))
                    {
                        mapFiles.Insert(0, TacticalConfigPanel.CurrentDraftProjectPath);
                    }

                    foreach (string mf in mapFiles.Distinct())
                    {
                        string raw = File.ReadAllText(mf, Encoding.UTF8);

                        var wptMatches = Regex.Matches(raw, @"<wpt\s+lat=""([0-9\.\-]+)""\s+lon=""([0-9\.\-]+)""[^>]*>(.*?)</wpt>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                        foreach (Match m in wptMatches)
                        {
                            if (double.TryParse(m.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double plat) &&
                                double.TryParse(m.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double plon))
                            {
                                lats.Add(plat);
                                lons.Add(plon);
                                var nameMatch = Regex.Match(m.Groups[3].Value, @"<name>(.*?)</name>", RegexOptions.IgnoreCase);
                                if (nameMatch.Success)
                                {
                                    string pName = WebUtility.HtmlDecode(nameMatch.Groups[1].Value.Trim());
                                    if (!string.IsNullOrWhiteSpace(pName) && !poiNames.Contains(pName))
                                        poiNames.Add("• " + pName + string.Format(CultureInfo.InvariantCulture, " ({0:0.0000}°N, {1:0.0000}°E)", plat, plon));
                                }
                            }
                        }

                        var jsonLatLon = Regex.Matches(raw, @"""lat""\s*:\s*([0-9\.\-]+)\s*,\s*""lon""\s*:\s*([0-9\.\-]+)", RegexOptions.IgnoreCase);
                        foreach (Match jm in jsonLatLon)
                        {
                            if (double.TryParse(jm.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double jlat) &&
                                double.TryParse(jm.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double jlon))
                            {
                                lats.Add(jlat);
                                lons.Add(jlon);
                            }
                        }

                        if (lats.Count > 0)
                        {
                            srcDesc = BuilderI18n.T("Obszar AO z Mapy (", "Map AO (") + Path.GetFileName(mf) + ")";
                            break;
                        }
                    }
                }
            }
            catch { }

            if (lats.Count > 0 && lons.Count > 0)
            {
                double avgLat = (lats.Min() + lats.Max()) / 2.0;
                double avgLon = (lons.Min() + lons.Max()) / 2.0;
                string poiSummary = poiNames.Count > 0 ? string.Join("\n", poiNames) : "";
                return (true, avgLat, avgLon, srcDesc, poiSummary);
            }

            return (false, 49.8494, 18.8127, "", "");
        }

        public static async Task<(bool Ok, double Lat, double Lon, string DisplayName)> GeocodeLocationAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return (false, 0, 0, "");
            try
            {
                string langParam = BuilderI18n.CurrentLang == "EN" ? "en" : "pl";
                string url = "https://geocoding-api.open-meteo.com/v1/search?name=" +
                             Uri.EscapeDataString(query.Trim()) + "&count=1&language=" + langParam + "&format=json";
                string json = await Http.GetStringAsync(url);
                if (JsonNode.Parse(json) is JsonObject root &&
                    root["results"] is JsonArray arr && arr.Count > 0 &&
                    arr[0] is JsonObject first)
                {
                    double lat = first["latitude"]?.GetValue<double>() ?? 0;
                    double lon = first["longitude"]?.GetValue<double>() ?? 0;
                    string name = first["name"]?.ToString() ?? query;
                    string admin = first["admin1"]?.ToString() ?? "";
                    string country = first["country_code"]?.ToString() ?? "";
                    string label = string.IsNullOrWhiteSpace(admin) ? name : (name + " (" + admin + ", " + country + ")");
                    return (true, lat, lon, label);
                }
            }
            catch { }
            return (false, 0, 0, "");
        }

        public static async Task<MetocReportData> FetchMetocReportAsync(double lat, double lon, string dateIso, string locationLabel)
        {
            DateTime targetDate;
            if (!DateTime.TryParseExact(dateIso?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out targetDate))
            {
                targetDate = DateTime.Today;
            }
            string cleanDate = targetDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            var moonInfo = CalculateMoonPhaseAndIllumination(targetDate);
            var fallbackSun = CalculateAstronomicalSunTimes(targetDate, lat, lon);

            string condText = BuilderI18n.T("Umiarkowane zachmurzenie, dobra widoczność", "Moderate cloud cover, good visibility");
            string tempText = "-- °C";
            string windText = "-- km/h";
            string humPrecipText = "-- % / 0.0 mm";
            string bmntStr = fallbackSun.Bmnt;
            string srStr = fallbackSun.Sunrise;
            string ssStr = fallbackSun.Sunset;
            string eentStr = fallbackSun.Eent;

            try
            {
                string latS = lat.ToString("0.0000", CultureInfo.InvariantCulture);
                string lonS = lon.ToString("0.0000", CultureInfo.InvariantCulture);

                double daysDiff = (targetDate.Date - DateTime.Today).TotalDays;
                string baseUrl = (daysDiff < -2)
                    ? "https://archive-api.open-meteo.com/v1/archive"
                    : "https://api.open-meteo.com/v1/forecast";

                string url = baseUrl +
                    "?latitude=" + latS +
                    "&longitude=" + lonS +
                    "&daily=weathercode,temperature_2m_max,temperature_2m_min,apparent_temperature_max,apparent_temperature_min,sunrise,sunset,precipitation_sum,precipitation_probability_max,windspeed_10m_max,windgusts_10m_max,winddirection_10m_dominant" +
                    "&hourly=relativehumidity_2m,cloudcover,surface_pressure" +
                    "&timezone=auto&start_date=" + cleanDate + "&end_date=" + cleanDate;

                string rawJson = await Http.GetStringAsync(url);
                if (JsonNode.Parse(rawJson) is JsonObject root && root["daily"] is JsonObject daily)
                {
                    int wmoCode = GetFirstInt(daily["weathercode"], 2);
                    double tMax = GetFirstDouble(daily["temperature_2m_max"], 15.0);
                    double tMin = GetFirstDouble(daily["temperature_2m_min"], 6.0);
                    double appMax = GetFirstDouble(daily["apparent_temperature_max"], tMax);
                    double appMin = GetFirstDouble(daily["apparent_temperature_min"], tMin);
                    double precipMm = GetFirstDouble(daily["precipitation_sum"], 0.0);
                    int precipProb = GetFirstInt(daily["precipitation_probability_max"], precipMm > 0.2 ? 65 : 10);
                    double windMaxKmh = GetFirstDouble(daily["windspeed_10m_max"], 12.0);
                    double gustsMaxKmh = GetFirstDouble(daily["windgusts_10m_max"], windMaxKmh * 1.4);
                    int windDeg = GetFirstInt(daily["winddirection_10m_dominant"], 270);

                    string apiSr = GetFirstString(daily["sunrise"], "");
                    string apiSs = GetFirstString(daily["sunset"], "");
                    if (DateTime.TryParse(apiSr, out DateTime parsedSr))
                    {
                        srStr = parsedSr.ToString("HH:mm");
                        bmntStr = parsedSr.AddMinutes(-34).ToString("HH:mm");
                    }
                    if (DateTime.TryParse(apiSs, out DateTime parsedSs))
                    {
                        ssStr = parsedSs.ToString("HH:mm");
                        eentStr = parsedSs.AddMinutes(34).ToString("HH:mm");
                    }

                    int avgHumidity = 72;
                    int avgClouds = 50;
                    double avgPressure = 1013.0;
                    if (root["hourly"] is JsonObject hourly)
                    {
                        avgHumidity = (int)Math.Round(GetAverageDouble(hourly["relativehumidity_2m"], 72.0));
                        avgClouds = (int)Math.Round(GetAverageDouble(hourly["cloudcover"], 50.0));
                        avgPressure = Math.Round(GetAverageDouble(hourly["surface_pressure"], 1013.0), 0);
                    }

                    string visEst = (wmoCode == 45 || wmoCode == 48)
                        ? BuilderI18n.T("Ograniczona (mgła < 300m)", "Reduced (fog < 300m)")
                        : (precipMm > 5.0 ? BuilderI18n.T("Umiarkowana (opady 1-3 km)", "Moderate (rain 1-3 km)") : BuilderI18n.T("Dobra (> 5 km)", "Good (> 5 km)"));

                    condText = DecodeWmoWeatherCode(wmoCode) + string.Format(
                        CultureInfo.InvariantCulture,
                        BuilderI18n.T(" | Zachmurzenie: {0}% | Widoczność: {1} | QNH: {2:0} hPa", " | Clouds: {0}% | Visibility: {1} | QNH: {2:0} hPa"),
                        avgClouds, visEst, avgPressure);

                    tempText = string.Format(
                        CultureInfo.InvariantCulture,
                        BuilderI18n.T("{0:0.0}°C .. {1:0.0}°C  (Odczuwalna: {2:0.0}°C .. {3:0.0}°C)", "{0:0.0}°C .. {1:0.0}°C  (Feels like: {2:0.0}°C .. {3:0.0}°C)"),
                        tMin, tMax, appMin, appMax);

                    double windMs = windMaxKmh / 3.6;
                    double gustsMs = gustsMaxKmh / 3.6;
                    string dirCardinal = DegreesToCardinal(windDeg);
                    windText = string.Format(
                        CultureInfo.InvariantCulture,
                        BuilderI18n.T(
                            "{0:0.0} km/h ({1:0.0} m/s)  |  Porywy: {2:0.0} km/h ({3:0.0} m/s)  |  Kierunek: {4}° ({5})",
                            "{0:0.0} km/h ({1:0.0} m/s)  |  Gusts: {2:0.0} km/h ({3:0.0} m/s)  |  Dir: {4}° ({5})"),
                        windMaxKmh, windMs, gustsMaxKmh, gustsMs, windDeg, dirCardinal);

                    humPrecipText = string.Format(
                        CultureInfo.InvariantCulture,
                        BuilderI18n.T(
                            "Wilgotność: ~{0}%  |  Opady: {1:0.0} mm (Szansa: {2}%)",
                            "Humidity: ~{0}%  |  Precip: {1:0.0} mm (Chance: {2}%)"),
                        avgHumidity, precipMm, precipProb);
                }
            }
            catch { }

            string sunLine = string.Format(
                BuilderI18n.T(
                    "BMNT (Świt): {0}  |  ☀️ Wschód (SR): {1}  |  🌇 Zachód (SS): {2}  |  EENT (Zmierzch NVG): {3}",
                    "BMNT (Dawn): {0}  |  ☀️ Sunrise (SR): {1}  |  🌇 Sunset (SS): {2}  |  EENT (Dusk NVG): {3}"),
                bmntStr, srStr, ssStr, eentStr);

            string locDisplay = string.IsNullOrWhiteSpace(locationLabel)
                ? string.Format(CultureInfo.InvariantCulture, "{0:0.0000}°N, {1:0.0000}°E", lat, lon)
                : string.Format(CultureInfo.InvariantCulture, "{0} ({1:0.0000}°N, {2:0.0000}°E)", locationLabel, lat, lon);

            var sb = new StringBuilder();
            sb.AppendLine(BuilderI18n.T(
                "🌦️ [PROGNOZA POGODY I DANE ŚWIETLNE METOC — " + cleanDate + "]",
                "🌦️ [METOC WEATHER & LIGHT REPORT — " + cleanDate + "]"));
            sb.AppendLine(BuilderI18n.T("• Rejon AO: ", "• AO Region: ") + locDisplay);
            sb.AppendLine(BuilderI18n.T("• Zjawiska i widoczność: ", "• Conditions & Visibility: ") + condText);
            sb.AppendLine(BuilderI18n.T("• Temperatura: ", "• Temperature: ") + tempText);
            sb.AppendLine(BuilderI18n.T("• Wiatr: ", "• Wind: ") + windText);
            sb.AppendLine(BuilderI18n.T("• Wilgotność i opady: ", "• Humidity & Precip: ") + humPrecipText);
            sb.AppendLine(BuilderI18n.T("• Słońce (BMNT / Wschód / Zachód / EENT): ", "• Sun (BMNT / SR / SS / EENT): ") + sunLine);
            sb.AppendLine(BuilderI18n.T("• Faza Księżyca i NVG: ", "• Moon Phase & NVG: ") + moonInfo.FullText);

            return new MetocReportData
            {
                DateIso = cleanDate,
                LocationLabel = locDisplay,
                Latitude = lat,
                Longitude = lon,
                ConditionText = condText,
                TempRangeText = tempText,
                WindText = windText,
                HumidityAndPrecipText = humPrecipText,
                SunTimesText = sunLine,
                MoonPhaseText = moonInfo.FullText,
                FullTacticalSummary = sb.ToString().TrimEnd()
            };
        }

        private static (string PhaseName, int IlluminationPct, string FullText) CalculateMoonPhaseAndIllumination(DateTime date)
        {
            DateTime knownNewMoon = new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);
            DateTime sampleUtc = new DateTime(date.Year, date.Month, date.Day, 22, 0, 0, DateTimeKind.Utc);
            double synodicMonth = 29.53058867;
            double daysSince = (sampleUtc - knownNewMoon).TotalDays;
            double cyclePos = ((daysSince % synodicMonth) + synodicMonth) % synodicMonth;
            double phaseFraction = cyclePos / synodicMonth;

            double illumFraction = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * phaseFraction));
            int illumPct = (int)Math.Round(illumFraction * 100.0);

            string phasePl;
            string phaseEn;
            string icon;
            if (phaseFraction < 0.04 || phaseFraction >= 0.96) { icon = "🌑"; phasePl = "Nów (New Moon)"; phaseEn = "New Moon"; }
            else if (phaseFraction < 0.21) { icon = "🌒"; phasePl = "Przybywający Sierp"; phaseEn = "Waxing Crescent"; }
            else if (phaseFraction < 0.29) { icon = "🌓"; phasePl = "Pierwsza Kwadra"; phaseEn = "First Quarter"; }
            else if (phaseFraction < 0.46) { icon = "🌔"; phasePl = "Przybywający Garb"; phaseEn = "Waxing Gibbous"; }
            else if (phaseFraction < 0.54) { icon = "🌕"; phasePl = "Pełnia (Full Moon)"; phaseEn = "Full Moon"; }
            else if (phaseFraction < 0.71) { icon = "🌖"; phasePl = "Ubywający Garb"; phaseEn = "Waning Gibbous"; }
            else if (phaseFraction < 0.79) { icon = "🌗"; phasePl = "Trzecia Kwadra"; phaseEn = "Last Quarter"; }
            else { icon = "🌘"; phasePl = "Ubywający Sierp"; phaseEn = "Waning Crescent"; }

            string nvgRating = illumPct >= 55
                ? BuilderI18n.T("Dobra widoczność pasywna NVG", "Good passive NVG visibility")
                : (illumPct >= 20 ? BuilderI18n.T("Umiarkowana jasność nocna", "Moderate ambient light") : BuilderI18n.T("Ciemna noc (zalecane IR)", "Dark night (IR recommended)"));

            string phaseLabel = BuilderI18n.T(phasePl, phaseEn);
            string full = string.Format(
                CultureInfo.InvariantCulture,
                BuilderI18n.T("{0} {1}  |  Iluminacja: {2}%  |  {3}", "{0} {1}  |  Illumination: {2}%  |  {3}"),
                icon, phaseLabel, illumPct, nvgRating);
            return (phaseLabel, illumPct, full);
        }

        private static (string Bmnt, string Sunrise, string Sunset, string Eent) CalculateAstronomicalSunTimes(DateTime date, double lat, double lon)
        {
            try
            {
                int dayOfYear = date.DayOfYear;
                double gamma = 2.0 * Math.PI / 365.0 * (dayOfYear - 1);
                double eqTime = 229.18 * (0.000075 + 0.001868 * Math.Cos(gamma) - 0.032077 * Math.Sin(gamma) - 0.014615 * Math.Cos(2 * gamma) - 0.040849 * Math.Sin(2 * gamma));
                double decl = 0.006918 - 0.399912 * Math.Cos(gamma) + 0.070257 * Math.Sin(gamma) - 0.006758 * Math.Cos(2 * gamma) + 0.000907 * Math.Sin(2 * gamma);
                double latRad = lat * Math.PI / 180.0;
                double zenithRad = 90.833 * Math.PI / 180.0;
                double cosHa = Math.Clamp((Math.Cos(zenithRad) / (Math.Cos(latRad) * Math.Cos(decl))) - Math.Tan(latRad) * Math.Tan(decl), -1.0, 1.0);
                double haDeg = Math.Acos(cosHa) * 180.0 / Math.PI;
                double tzOffsetHours = TimeZoneInfo.Local.GetUtcOffset(date).TotalHours;
                double srMinutes = 720.0 - 4.0 * (lon + haDeg) - eqTime + tzOffsetHours * 60.0;
                double ssMinutes = 720.0 - 4.0 * (lon - haDeg) - eqTime + tzOffsetHours * 60.0;

                TimeSpan srTs = TimeSpan.FromMinutes(Math.Clamp(srMinutes, 0, 1439));
                TimeSpan ssTs = TimeSpan.FromMinutes(Math.Clamp(ssMinutes, 0, 1439));
                TimeSpan bmntTs = TimeSpan.FromMinutes(Math.Clamp(srMinutes - 34, 0, 1439));
                TimeSpan eentTs = TimeSpan.FromMinutes(Math.Clamp(ssMinutes + 34, 0, 1439));

                return (
                    string.Format("{0:D2}:{1:D2}", bmntTs.Hours, bmntTs.Minutes),
                    string.Format("{0:D2}:{1:D2}", srTs.Hours, srTs.Minutes),
                    string.Format("{0:D2}:{1:D2}", ssTs.Hours, ssTs.Minutes),
                    string.Format("{0:D2}:{1:D2}", eentTs.Hours, eentTs.Minutes)
                );
            }
            catch { return ("05:45", "06:20", "18:10", "18:45"); }
        }

        private static string DecodeWmoWeatherCode(int code) => code switch
        {
            0 => BuilderI18n.T("☀️ Bezchmurnie", "☀️ Clear sky"),
            1 => BuilderI18n.T("🌤️ Przeważnie słonecznie", "🌤️ Mainly clear"),
            2 => BuilderI18n.T("⛅ Częściowe zachmurzenie", "⛅ Partly cloudy"),
            3 => BuilderI18n.T("☁️ Pochmurno", "☁️ Overcast"),
            45 or 48 => BuilderI18n.T("🌫️ Mgła", "🌫️ Fog"),
            51 or 53 or 55 => BuilderI18n.T("🌦️ Mżawka", "🌦️ Drizzle"),
            61 or 63 or 65 => BuilderI18n.T("🌧️ Opady deszczu", "🌧️ Rain"),
            71 or 73 or 75 or 77 => BuilderI18n.T("❄️ Opady śniegu", "❄️ Snowfall"),
            80 or 81 or 82 => BuilderI18n.T("🌧️ Przelotny deszcz", "🌧️ Rain showers"),
            95 or 96 or 99 => BuilderI18n.T("⛈️ Burza", "⛈️ Thunderstorm"),
            _ => BuilderI18n.T("⛅ Zmienne zachmurzenie", "⛅ Variable cloudiness")
        };

        private static string DegreesToCardinal(int deg)
        {
            string[] dirs = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            return dirs[(int)Math.Round(((deg % 360) + 360) % 360 / 45.0) % 8];
        }

        private static double GetFirstDouble(JsonNode n, double fb) { try { if (n is JsonArray a && a.Count > 0 && a[0] != null) return Convert.ToDouble(a[0].ToString(), CultureInfo.InvariantCulture); } catch { } return fb; }
        private static int GetFirstInt(JsonNode n, int fb) { try { if (n is JsonArray a && a.Count > 0 && a[0] != null) return (int)Math.Round(Convert.ToDouble(a[0].ToString(), CultureInfo.InvariantCulture)); } catch { } return fb; }
        private static string GetFirstString(JsonNode n, string fb) { try { if (n is JsonArray a && a.Count > 0 && a[0] != null) return a[0].ToString(); } catch { } return fb; }
        private static double GetAverageDouble(JsonNode n, double fb)
        {
            try
            {
                if (n is JsonArray a && a.Count > 0)
                {
                    var v = new List<double>();
                    foreach (var x in a) if (x != null && double.TryParse(x.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double d)) v.Add(d);
                    if (v.Count > 0) return v.Average();
                }
            }
            catch { }
            return fb;
        }
    }

    // =========================================================================
    // 4. MODELE DANYCH OPORD v3.2 (NIEZALEŻNE ZAKŁADKI DZIENNE + WYBÓR DNI DO GENEROWANIA)
    // =========================================================================
    internal sealed class OpordRosterRow
    {
        public string OperatorOrTeam { get; set; } = "ALFA-1";
        public string PrimaryRole { get; set; } = "Lider (Dowódca polowy)";
        public string SecondaryRole { get; set; } = "RTO (Radiooperator)";
        public string WeaponAndGear { get; set; } = "Szturm (Karabinek) + ATAK + Radio VHF";
    }

    internal sealed class OpordRoleDefinitionRow
    {
        public string RoleName { get; set; } = "Lider (Dowódca polowy)";
        public string Description { get; set; } = "Dowodzi zespołem w trakcie działań, podejmuje kluczowe decyzje, odpowiada za wykonanie zadań.";
        public string DutyMode { get; set; } = "Full-time";
    }

    internal sealed class OpordCodewordRow
    {
        public string CodeWord { get; set; } = "ZAMEK";
        public string CodeLetter { get; set; } = "A";
        public string CodeDigit { get; set; } = "1";
        public string DuressWord { get; set; } = "SPALONY";
    }

    internal sealed class OpordDayPlanModel
    {
        public string DayTitle { get; set; } = "Dzień 1";
        public bool IncludeInExport { get; set; } = true;
        public string DateIso { get; set; } = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        public string LocationName { get; set; } = "";
        public string LocationLat { get; set; } = "49.8494";
        public string LocationLon { get; set; } = "18.8127";
        public string TerrainAndWeather { get; set; } = "Wielkość AO: ok. 2x2 km. Rodzaj: las mieszany, polany, zabudowania leśne. Ukształtowanie: pagórkowate ze skarpami.";
        public string MetocCondition { get; set; } = "Brak pobranego raportu (kliknij przycisk METOC dla tego dnia)";
        public string MetocTemperature { get; set; } = "-";
        public string MetocWind { get; set; } = "-";
        public string MetocHumidityPrecip { get; set; } = "-";
        public string MetocSunTimes { get; set; } = "-";
        public string MetocMoonPhase { get; set; } = "-";
        public string DaySpecificTasks { get; set; } = "• 08:30 — Odprawa sztabowa i sprawdzenie łączności\n• 09:00 — Wejście w rejon działań\n• 14:00 — Rotacja i realizacja zadań głównych";
    }

    internal sealed class OpordParagraphModel
    {
        public string TargetFaction { get; set; } = "BLUFOR";
        public bool IsSecretClassification { get; set; } = false;
        public string Classification { get; set; } = "JAWNE / UNCLASSIFIED — MILSIM DRUŻYNOWY OPORD";
        public string OperationNumber { get; set; } = "OPORD 01-2026";
        public string OperationDateIso { get; set; } = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        public string LocationName { get; set; } = "";
        public string LocationLat { get; set; } = "49.8494";
        public string LocationLon { get; set; } = "18.8127";
        public string DtgIssued { get; set; } = DateTime.Now.ToString("ddHHmm'Z' MMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();
        public string IssuingHq { get; set; } = "TOC / SZTAB DRUŻYNY";
        public string MapReferences { get; set; } = "Mapa Taktyczna MGRS 1:5000 (W załączeniu .FTAK / .KMZ / .GPX)";

        public List<OpordDayPlanModel> Days { get; set; } = new List<OpordDayPlanModel>();
        public int ActiveDayIndex { get; set; } = 0;

        public string MetocCondition { get; set; } = "-";
        public string MetocTemperature { get; set; } = "-";
        public string MetocWind { get; set; } = "-";
        public string MetocHumidityPrecip { get; set; } = "-";
        public string MetocSunTimes { get; set; } = "-";
        public string MetocMoonPhase { get; set; } = "-";

        public string AreaOfInterestPoiDesc { get; set; } = "Punkty kluczowe (FOB, Respawn, OBJ ALPHA, LZ) zdefiniowane w załączonym pliku .KMZ (ATAK) oraz .GPX (Garmin/Suunto).";
        public string TerrainAndWeather { get; set; } = "Wielkość AO: ok. 2x2 km. Rodzaj: las mieszany, polany, zabudowania leśne. Ukształtowanie: pagórkowate ze skarpami.";
        public string EnemyForces { get; set; } = "1. Skład, dyspozycja i siła: Pluton lekki przeciwnika podzielony na patrole 4-6 osobowe.\n2. Lokalizacje i możliwości: Posterunki na skrzyżowaniach dróg.\n3. Kursy działań wroga (COA): Obrona rejonu centralnego i zasadzki.";
        public string FriendlyForces { get; set; } = "1. Misja i zamiar wyższego dowództwa (HQ): Opanowanie i kontrola sektora przed zmrokiem.\n2. Misja sąsiednich jednostek: Wsparcie na skrzydłach.";

        public string MissionLoreDesc { get; set; } = "Opis fabularny (opcjonalnie): Działania rozpoznawczo-dywersyjne na zapleczu ugrupowania przeciwnika.";
        public string MissionWho { get; set; } = "Drużyna zadaniowa BLUFOR";
        public string MissionWhat { get; set; } = "zlokalizuje i zabezpieczy kody sektorowe oraz utrzyma obiekt OBJ ALPHA";
        public string MissionWhen { get; set; } = "w godzinach operacyjnych 09:00 - 17:00";
        public string MissionWhere { get; set; } = "w wyznaczonym obszarze AO (siatka MGRS)";
        public string MissionWhy { get; set; } = "w celu uzyskania przewagi punktowej i odcięcia zaopatrzenia przeciwnika.";
        public string MissionFullStatement { get; set; } = "";
        public string AdditionalTasksSummary { get; set; } = "• Zadanie poboczne 1: Rozpoznanie stanowisk dowodzenia wroga (Foto).\n• Zadanie poboczne 2: Przejęcie pojemnika fabularnego (szczegóły w Załączniku 2).";

        public string CommandersIntent { get; set; } = "Jedziemy działać zespołowo, przećwiczyć nawigację MGRS/ATAK i zrealizować 100% zadań głównych bez zbędnych strat.";
        public string ConceptOfOperations { get; set; } = "Priorytety i styl działania: Skryte przemieszczanie czujką z przodu. Unikamy niepotrzebnego kontaktu ogniowego podczas marszu do celu.";
        public string TasksToSubordinateUnits { get; set; } = "• Sekcja Szturmowa: Wejście na obiekt i czyszczenie przedpola.\n• Wsparcie / DMR: Osłona skrzydeł.";
        public string CoordinatingAndRoe { get; set; } = "• Obowiązkowa ochrona wzroku przez cały czas trwania gry.\n• Weryfikacja pozycji sojuszników w ATAK.";
        public string SuccessionOfCommand { get; set; } = "Dowódca (Lider)  ->  1. Zastępca (Navi / RTO)  ->  2. Zastępca (Dowódca sekcji)";

        public bool EnableSquadRosterTable { get; set; } = true;
        public List<OpordRosterRow> SquadRoster { get; set; } = new List<OpordRosterRow>();

        public string PrimaryRadioFreq { get; set; } = "446.00625 MHz (PMR CH 1)";
        public string BackupRadioFreq { get; set; } = "446.03125 MHz (PMR CH 3)";
        public string HqRadioFreq { get; set; } = "446.09375 MHz (PMR CH 8)";

        public string TargetCodeNames { get; set; } = "• OBJ ALPHA = 'ZAMEK'   |   • Punkt Zbiórki (RP) = 'KUŹNIA'   |   • FOB / Respawn = 'BAZA'";
        public string SarnegKeyWord10 { get; set; } = "PRZEDMIOTY";
        public List<OpordCodewordRow> CodewordsTable { get; set; } = new List<OpordCodewordRow>();
        public string ChallengeAndPassword { get; set; } = "a) Werbalne (Wywołanie - Odpowiedź - Potwierdzenie): GROM - BŁYSK - TARCZA (Suma cyfr: 7)\nb) Niewerbalne (Wywołanie - Odzew): 2 błyski czerwonym światłem - 1 błysk + podniesienie lewej ręki";

        public string MedevacRules { get; set; } = "Pierwsze trafienie: opatrunek przez medyka lub członka drużyny (czas 2 min). Drugie trafienie: odejście na Respawn. W razie wypadku: przycisk 🚨 SOS MGRS w Field TAK Hub.";
        public string RespawnAndSupply { get; set; } = "Styl wyposażenia: Na 12h w terenie (woda min. 2L, racja żywnościowa, zapasowe baterie do radia i powerbank do ATAK).";
        public string SquadGearChecklist { get; set; } = "SZPEJLISTA: Okulary ochronne, czerwona szmatka (dead-rag), latarka czerwona, radio PMR/VHF, telefon z Field TAK Hub & ATAK-CIV, kompas.";
        public string SpecialEquipment { get; set; } = "Wyposażenie specjalne: Rekwizyty misyjne ze sztabu, znaczniki chemiczne (lightstick), aparat/telefon dla Fotografa.";
        public string TravelAndParking { get; set; } = "1. Parking: Zgodnie z pinezką zbiórki w paczce .GPX / .KMZ.\n2. Odległość z parkingu na miejsce gry: ok. 300 m.\n3. Transport: Auto 1, Auto 2.";
        public string GameRulesLink { get; set; } = "https://";

        public bool EnableRolesAnnex { get; set; } = false;
        public List<OpordRoleDefinitionRow> CustomRolesAnnex { get; set; } = new List<OpordRoleDefinitionRow>();
        public string Annex2TasksAnalysis { get; set; } = "ZAŁĄCZNIK 2 — OPRACOWANIE ZADAŃ:\n• Analiza priorytetów punktowych, okna czasowe na wykonanie misji specjalnych oraz podział zespołów na zadania równoległe.";
    }

    // =========================================================================
    // 5. ZARZĄDCA OPORD v3.2 — CZĘŚĆ 1 (LOGIKA, DYSK, LAN SERVER, HTML A4, DISCORD)
    // =========================================================================
    internal static partial class OpordBuilderManager
    {
        private static readonly Dictionary<string, OpordParagraphModel> ModelsByFaction = new Dictionary<string, OpordParagraphModel>(StringComparer.OrdinalIgnoreCase);
        private static string _activeOpordFaction = "BLUFOR";
        private static Border _cachedOpordOverlay;

        private static TcpListener _opordLanListener;
        private static volatile bool _opordLanServerRunning;
        private static int _opordLanPort = 8769;
        private static Window _lastActiveWindowRef;

        public static readonly (string RoleName, string Mode, string Description)[] DefaultMilsimRolesCatalog = new[]
        {
            ("Lider (Dowódca polowy)", "Full-time", "Dowodzi zespołem w trakcie działań, podejmuje kluczowe decyzje, odpowiada za wykonanie zadań."),
            ("Navi (Nawigator)", "Full-time", "Nadzoruje mapę, informacje o POI, wyznacza trasy, nadzoruje kierunek marszu."),
            ("RTO (Radiooperator)", "Full-time", "Odpowiada za kontakt ze sztabem, odbieranie rozkazów i podawanie kluczowych informacji, zna szyfrowanie."),
            ("Med (Medyk)", "Sytuacyjne", "Odpowiada za sprawy medyczne: leczenie, opatrunki, ilość dostępnych respów, karty ran itp."),
            ("Adiutant", "Sytuacyjne", "Szczegółowo analizuje treść zadań oraz dodatkowych informacji, spisuje kluczowe dane i punkty, przekazuje dowódcy."),
            ("LARP (Aktor)", "Sytuacyjne", "Osoba do larpowania, kontaktu z NPC oraz innymi osobami larpującymi, znająca fabułę gry."),
            ("Foto (Fotograf)", "Sytuacyjne", "Osoba odpowiedzialna za dokumentowanie działań (np. zdjęcia celów, kodów), wypełnianie dokumentów dot. misji."),
            ("Tragarz", "Sytuacyjne", "Osoba odpowiedzialna za przedmioty wymagane do zadań: transport i przygotowanie przedmiotów fabularnych."),
            ("O-O (Organizator Obrony)", "Sytuacyjne", "Odpowiada za ustanawianie obrony w trakcie postoju: rozstawienie na sektory, wysunięcie czujki, obstawianie wzgórz."),
            ("Czujka", "Przechodnie", "Przewodzi podczas marszu, koryguje trasę, szuka przejścia, wypatruje zagrożenia z przodu.")
        };

        public static OpordParagraphModel GetOrCreateModel(string factionName)
        {
            string key = string.IsNullOrWhiteSpace(factionName) ? "BLUFOR" : factionName.Trim();
            if (!ModelsByFaction.TryGetValue(key, out var model))
            {
                model = TryLoadSavedOpordFromDisk(key) ?? new OpordParagraphModel
                {
                    TargetFaction = key,
                    MissionWho = "Drużyna " + key
                };
                EnsureDefaultCollectionsInitialized(model);
                ModelsByFaction[key] = model;
            }
            else
            {
                EnsureDefaultCollectionsInitialized(model);
            }
            return model;
        }

        private static void EnsureDefaultCollectionsInitialized(OpordParagraphModel m)
        {
            if (string.IsNullOrWhiteSpace(m.MissionFullStatement))
                m.MissionFullStatement = Compose5WMissionText(m);

            if (m.Days == null || m.Days.Count == 0)
            {
                m.Days = new List<OpordDayPlanModel>
                {
                    new OpordDayPlanModel
                    {
                        DayTitle = "Dzień 1",
                        IncludeInExport = true,
                        DateIso = string.IsNullOrWhiteSpace(m.OperationDateIso)
                            ? DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                            : m.OperationDateIso,
                        LocationName = m.LocationName ?? "",
                        LocationLat = string.IsNullOrWhiteSpace(m.LocationLat) ? "49.8494" : m.LocationLat,
                        LocationLon = string.IsNullOrWhiteSpace(m.LocationLon) ? "18.8127" : m.LocationLon,
                        TerrainAndWeather = string.IsNullOrWhiteSpace(m.TerrainAndWeather)
                            ? "Wielkość AO: ok. 2x2 km. Rodzaj: las mieszany, polany, zabudowania leśne. Ukształtowanie: pagórkowate ze skarpami."
                            : m.TerrainAndWeather
                    }
                };
                m.ActiveDayIndex = 0;
            }
            else
            {
                foreach (var d in m.Days)
                {
                    if (string.IsNullOrWhiteSpace(d.LocationLat)) d.LocationLat = string.IsNullOrWhiteSpace(m.LocationLat) ? "49.8494" : m.LocationLat;
                    if (string.IsNullOrWhiteSpace(d.LocationLon)) d.LocationLon = string.IsNullOrWhiteSpace(m.LocationLon) ? "18.8127" : m.LocationLon;
                    if (string.IsNullOrWhiteSpace(d.TerrainAndWeather)) d.TerrainAndWeather = m.TerrainAndWeather ?? "";
                }
            }

            if (m.SquadRoster == null || m.SquadRoster.Count == 0)
            {
                m.SquadRoster = new List<OpordRosterRow>
                {
                    new OpordRosterRow { OperatorOrTeam = "ALFA-1", PrimaryRole = "Lider (Dowódca polowy)", SecondaryRole = "Adiutant", WeaponAndGear = "Karabinek szturmowy + ATAK" },
                    new OpordRosterRow { OperatorOrTeam = "ALFA-2", PrimaryRole = "Navi (Nawigator)", SecondaryRole = "Czujka", WeaponAndGear = "Karabinek szturmowy + Garmin/ATAK" },
                    new OpordRosterRow { OperatorOrTeam = "ALFA-3", PrimaryRole = "RTO (Radiooperator)", SecondaryRole = "Foto (Fotograf)", WeaponAndGear = "Karabinek szturmowy + Radio DualBand + LoRa" },
                    new OpordRosterRow { OperatorOrTeam = "ALFA-4", PrimaryRole = "Med (Medyk)", SecondaryRole = "O-O (Organizator Obrony)", WeaponAndGear = "DMR / Wsparcie + Pakiet Medyczny" }
                };
            }

            if (m.CustomRolesAnnex == null || m.CustomRolesAnnex.Count == 0)
            {
                m.CustomRolesAnnex = DefaultMilsimRolesCatalog.Select(r => new OpordRoleDefinitionRow
                {
                    RoleName = r.RoleName,
                    DutyMode = r.Mode,
                    Description = r.Description
                }).ToList();
            }

            if (m.CodewordsTable == null || m.CodewordsTable.Count == 0)
            {
                m.CodewordsTable = new List<OpordCodewordRow>
                {
                    new OpordCodewordRow { CodeWord = "KONTAKT / WRÓG", CodeLetter = "K", CodeDigit = "1", DuressWord = "CZERWONY" },
                    new OpordCodewordRow { CodeWord = "ODWRÓT / ZERWANIE", CodeLetter = "Z", CodeDigit = "2", DuressWord = "MGŁA" },
                    new OpordCodewordRow { CodeWord = "ZBIÓRKA NA FOB", CodeLetter = "B", CodeDigit = "5", DuressWord = "BURZA" }
                };
            }
        }

        public static string Compose5WMissionText(OpordParagraphModel m)
        {
            if (BuilderI18n.CurrentLang == "EN")
            {
                return (m.MissionWho + " will " + m.MissionWhat + " " + m.MissionWhen + " at " + m.MissionWhere + " in order to " + m.MissionWhy).Trim();
            }
            return (m.MissionWho + " " + m.MissionWhat + " " + m.MissionWhen + " w rejonie " + m.MissionWhere + ", " + m.MissionWhy).Trim();
        }

        public static void SaveAllOpordsToDisk(Window win)
        {
            try
            {
                string sourceDir = TacticalConfigPanel.GetPrimarySourceDirectory(win);
                foreach (var kv in ModelsByFaction)
                {
                    string facSlug = TacticalConfigPanel.SanitizeFileName(kv.Key).ToLowerInvariant();
                    string facDir = Path.Combine(sourceDir, "factions", facSlug);
                    Directory.CreateDirectory(facDir);

                    string json = JsonSerializer.Serialize(kv.Value, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(Path.Combine(facDir, "opord_" + facSlug + ".json"), json, Encoding.UTF8);

                    string html = GeneratePrintableOpordHtml(win, kv.Key, kv.Value);
                    File.WriteAllText(Path.Combine(facDir, "OPORD_" + facSlug.ToUpperInvariant() + ".html"), html, Encoding.UTF8);
                }
            }
            catch { }
        }

        private static OpordParagraphModel TryLoadSavedOpordFromDisk(string factionName)
        {
            try
            {
                string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FieldTakHub");
                string facSlug = TacticalConfigPanel.SanitizeFileName(factionName).ToLowerInvariant();
                string p = Path.Combine(appData, "opord_" + facSlug + ".json");
                if (File.Exists(p))
                {
                    return JsonSerializer.Deserialize<OpordParagraphModel>(File.ReadAllText(p, Encoding.UTF8));
                }
            }
            catch { }
            return null;
        }

        private static void PersistModelToAppData(OpordParagraphModel m)
        {
            try
            {
                string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FieldTakHub");
                Directory.CreateDirectory(appData);
                string facSlug = TacticalConfigPanel.SanitizeFileName(m.TargetFaction).ToLowerInvariant();
                string p = Path.Combine(appData, "opord_" + facSlug + ".json");
                File.WriteAllText(p, JsonSerializer.Serialize(m, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
            }
            catch { }
        }

        public static JsonObject TryGetFactionJsonObject(Window win, string factionName)
        {
            try
            {
                if (win != null)
                {
                    try { TacticalConfigPanel.SaveSessionStateAuto(win); } catch { }
                }

                var candidatePaths = new List<string>();
                if (!string.IsNullOrWhiteSpace(TacticalConfigPanel.CurrentDraftProjectPath) && File.Exists(TacticalConfigPanel.CurrentDraftProjectPath))
                {
                    candidatePaths.Add(TacticalConfigPanel.CurrentDraftProjectPath);
                }
                if (!string.IsNullOrWhiteSpace(BuilderCoreSidebarAndProjectEngine.CurrentProjectFilePath) && File.Exists(BuilderCoreSidebarAndProjectEngine.CurrentProjectFilePath))
                {
                    candidatePaths.Add(BuilderCoreSidebarAndProjectEngine.CurrentProjectFilePath);
                }

                string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FieldTakHub");
                if (Directory.Exists(appData))
                {
                    var recentFiles = Directory.GetFiles(appData, "*.*", SearchOption.TopDirectoryOnly)
                        .Where(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".fthproj", StringComparison.OrdinalIgnoreCase))
                        .Where(f => !Path.GetFileName(f).StartsWith("opord_", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(File.GetLastWriteTimeUtc);
                    candidatePaths.AddRange(recentFiles);
                }

                foreach (string path in candidatePaths.Distinct())
                {
                    try
                    {
                        string raw = File.ReadAllText(path, Encoding.UTF8);
                        if (JsonNode.Parse(raw) is JsonObject root)
                        {
                            var fArr = (root["factions"] ?? root["Factions"]) as JsonArray;
                            if (fArr != null && fArr.Count > 0)
                            {
                                foreach (var item in fArr.OfType<JsonObject>())
                                {
                                    string n = item["Name"]?.ToString() ?? item["name"]?.ToString() ?? "";
                                    if (n.Equals(factionName, StringComparison.OrdinalIgnoreCase))
                                        return item;
                                }
                                return fArr.OfType<JsonObject>().FirstOrDefault();
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }

        public static string EnsureOpordLanServerStarted(Window win)
        {
            _lastActiveWindowRef = win;
            if (!_opordLanServerRunning || _opordLanListener == null)
            {
                foreach (int port in new[] { 8769, 8770, 8771 })
                {
                    try
                    {
                        var listener = new TcpListener(IPAddress.Any, port);
                        listener.Start();
                        _opordLanListener = listener;
                        _opordLanPort = port;
                        _opordLanServerRunning = true;
                        _ = Task.Run(() => AcceptOpordLanClientsLoop(listener));
                        break;
                    }
                    catch { }
                }
            }

            string ip = GetLocalLanIp();
            return "http://" + ip + ":" + _opordLanPort;
        }

        private static async Task AcceptOpordLanClientsLoop(TcpListener listener)
        {
            while (_opordLanServerRunning)
            {
                try
                {
                    var client = await listener.AcceptTcpClientAsync();
                    _ = Task.Run(() => HandleOpordLanHttpClient(client));
                }
                catch
                {
                    if (!_opordLanServerRunning) break;
                }
            }
        }

        private static void HandleOpordLanHttpClient(TcpClient client)
        {
            using (client)
            {
                try
                {
                    using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true);
                    string reqLine = reader.ReadLine();
                    if (string.IsNullOrEmpty(reqLine)) return;

                    string[] parts = reqLine.Split(' ');
                    string path = parts.Length >= 2 ? Uri.UnescapeDataString(parts[1]) : "/";

                    string facName = _activeOpordFaction;
                    foreach (var kv in ModelsByFaction)
                    {
                        if (path.Contains(kv.Key, StringComparison.OrdinalIgnoreCase))
                        {
                            facName = kv.Key;
                            break;
                        }
                    }

                    var model = GetOrCreateModel(facName);
                    bool wantsJson = path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

                    byte[] bodyBytes;
                    string contentType;
                    if (wantsJson)
                    {
                        string json = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
                        bodyBytes = Encoding.UTF8.GetBytes(json);
                        contentType = "application/json; charset=utf-8";
                    }
                    else
                    {
                        string html = GeneratePrintableOpordHtml(_lastActiveWindowRef, facName, model);
                        bodyBytes = Encoding.UTF8.GetBytes(html);
                        contentType = "text/html; charset=utf-8";
                    }

                    string headers =
                        "HTTP/1.1 200 OK\r\n" +
                        "Content-Type: " + contentType + "\r\n" +
                        "Content-Length: " + bodyBytes.Length + "\r\n" +
                        "Connection: close\r\n\r\n";
                    byte[] hdrBytes = Encoding.UTF8.GetBytes(headers);
                    stream.Write(hdrBytes, 0, hdrBytes.Length);
                    stream.Write(bodyBytes, 0, bodyBytes.Length);
                    stream.Flush();
                }
                catch { }
            }
        }

        private static string GetLocalLanIp()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    {
                        string s = ip.ToString();
                        if (s.StartsWith("192.168.") || s.StartsWith("10.") || s.StartsWith("172."))
                            return s;
                    }
                }
            }
            catch { }
            return "127.0.0.1";
        }

        public static List<OpordDayPlanModel> GetSelectedDaysForExport(OpordParagraphModel m)
        {
            EnsureDefaultCollectionsInitialized(m);
            var selected = m.Days.Where(d => d.IncludeInExport).ToList();
            if (selected.Count == 0 && m.Days.Count > 0)
            {
                selected.Add(m.Days[Math.Clamp(m.ActiveDayIndex, 0, m.Days.Count - 1)]);
            }
            return selected;
        }

        public static string GeneratePrintableOpordHtml(Window win, string factionName, OpordParagraphModel m)
        {
            EnsureDefaultCollectionsInitialized(m);
            var exportedDays = GetSelectedDaysForExport(m);
            var primaryDay = exportedDays.FirstOrDefault() ?? m.Days[0];

            string eventName = win != null ? TacticalConfigPanel.GetEventNameFromWindow(win) : "OPERACJA";
            var facJson = TryGetFactionJsonObject(win, factionName);
            string colorHex = facJson?["ColorHex"]?.ToString() ?? "#2684FF";
            string chName = facJson?["ChannelName"]?.ToString() ?? (factionName + "-MESH");
            string region = facJson?["LoraRegion"]?.ToString() ?? "EU_868";
            string preset = facJson?["LoraModemPreset"]?.ToString() ?? "SHORT_FAST";
            string ots = facJson?["OtsHost"]?.ToString() ?? "Off-Grid Mesh";
            string phone = facJson?["EmergencyPhone"]?.ToString() ?? "112";

            string E(string s) => WebUtility.HtmlEncode(s ?? "").Replace("\n", "<br/>");

            var radioRowsHtml = new StringBuilder();
            radioRowsHtml.AppendLine("<tr><td><b>" + BuilderI18n.T("Kanał główny", "Primary Channel") + "</b></td><td>PMR/VHF</td><td colspan='2'><b>" + E(m.PrimaryRadioFreq) + "</b></td></tr>");
            radioRowsHtml.AppendLine("<tr><td><b>" + BuilderI18n.T("Kanał zapasowy", "Backup Channel") + "</b></td><td>PMR/VHF</td><td colspan='2'><b>" + E(m.BackupRadioFreq) + "</b></td></tr>");
            radioRowsHtml.AppendLine("<tr><td><b>" + BuilderI18n.T("Kanał HQ (Sztab)", "HQ Channel") + "</b></td><td>PMR/VHF</td><td colspan='2'><b>" + E(m.HqRadioFreq) + "</b></td></tr>");

            var vArr = (facJson?["VoiceChannels"] ?? facJson?["voiceChannels"]) as JsonArray;
            if (vArr != null && vArr.Count > 0)
            {
                foreach (var vo in vArr.OfType<JsonObject>())
                {
                    radioRowsHtml.AppendLine(
                        "<tr><td><b>" + E(vo["roleName"]?.ToString() ?? vo["RoleName"]?.ToString()) + "</b></td>" +
                        "<td>" + E(vo["bandType"]?.ToString() ?? vo["BandType"]?.ToString()) + "</td>" +
                        "<td><b>" + E(vo["freqMhz"]?.ToString() ?? vo["FreqMhz"]?.ToString()) + "</b></td>" +
                        "<td>" + E(vo["toneOrCode"]?.ToString() ?? vo["ToneOrCode"]?.ToString()) + "</td></tr>");
                }
            }

            string sarnegClean = SarnegDictionaryService.NormalizePolishChars(m.SarnegKeyWord10 ?? "PRZEDMIOTY").Trim().PadRight(10, 'X').Substring(0, 10);
            var sarnegLettersRow = new StringBuilder();
            var sarnegDigitsRow = new StringBuilder();
            for (int i = 0; i < 10; i++)
            {
                sarnegLettersRow.Append("<th style='text-align:center;font-size:13px;'>").Append(sarnegClean[i]).Append("</th>");
                sarnegDigitsRow.Append("<td style='text-align:center;font-weight:bold;'>").Append(i).Append("</td>");
            }

            string classBannerBg = m.IsSecretClassification ? "#ffebee" : "#f4f6f8";
            string classBannerBorder = m.IsSecretClassification ? "#d32f2f" : "#111";
            string classBannerColor = m.IsSecretClassification ? "#b71c1c" : "#111";

            var html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html><html lang='pl'><head><meta charset='utf-8'/>");
            html.AppendLine("<title>OPORD " + E(eventName) + " (" + E(factionName) + ")</title>");
            html.AppendLine("<style>");
            html.AppendLine("@page { size: A4 portrait; margin: 10mm; }");
            html.AppendLine("body { font-family: 'Segoe UI', Arial, sans-serif; color: #111; background: #fff; margin: 0; padding: 14px; font-size: 11.5px; line-height: 1.4; }");
            html.AppendLine(".classification { text-align: center; font-weight: bold; font-size: 12px; letter-spacing: 1.2px; border-top: 2px solid " + classBannerBorder + "; border-bottom: 2px solid " + classBannerBorder + "; color: " + classBannerColor + "; padding: 4px; margin-bottom: 10px; background: " + classBannerBg + "; }");
            html.AppendLine(".header-box { border: 2px solid #111; padding: 10px 14px; margin-bottom: 12px; display: flex; justify-content: space-between; align-items: center; }");
            html.AppendLine(".faction-badge { background: " + colorHex + "; color: #fff; font-weight: bold; padding: 6px 14px; border-radius: 4px; font-size: 15px; }");
            html.AppendLine("h1 { margin: 0 0 4px 0; font-size: 17px; text-transform: uppercase; }");
            html.AppendLine("h2 { background: #102229; color: #fff; padding: 5px 10px; font-size: 12.5px; margin: 12px 0 6px 0; text-transform: uppercase; }");
            html.AppendLine(".sub-p { margin: 4px 0 6px 8px; }");
            html.AppendLine(".mission-box { border: 2px solid " + colorHex + "; background: #f0f7ff; padding: 8px 12px; font-weight: bold; font-size: 12.5px; margin: 6px 0; }");
            html.AppendLine("table { width: 100%; border-collapse: collapse; margin-top: 5px; margin-bottom: 8px; font-size: 11px; }");
            html.AppendLine("th, td { border: 1px solid #333; padding: 4px 6px; text-align: left; }");
            html.AppendLine("th { background: #e6ecef; }");
            html.AppendLine(".no-print { margin-bottom: 12px; text-align: right; }");
            html.AppendLine(".btn-print { background: #102229; color: #1FE0C5; border: 2px solid #1FE0C5; padding: 8px 16px; font-weight: bold; cursor: pointer; border-radius: 6px; }");
            html.AppendLine("@media print { .no-print { display: none; } }");
            html.AppendLine("</style></head><body>");
            html.AppendLine("<div class='no-print'><button class='btn-print' onclick='window.print()'>" + BuilderI18n.T("🖨️ Drukuj OPORD / Zapisz jako PDF (A4)", "🖨️ Print OPORD / Save as PDF (A4)") + "</button></div>");
            html.AppendLine("<div class='classification'>" + E(m.Classification) + "</div>");
            html.AppendLine("<div class='header-box'><div>");
            html.AppendLine("<h1>OPORD " + E(eventName) + " — " + E(m.OperationNumber) + "</h1>");
            html.AppendLine("<div><b>" + BuilderI18n.T("Sztab (HQ):", "Issuing HQ:") + "</b> " + E(m.IssuingHq) + " &nbsp;|&nbsp; <b>DTG:</b> " + E(m.DtgIssued) + "</div>");
            html.AppendLine("<div><b>" + BuilderI18n.T("Rejon AO:", "AO Region:") + "</b> " + E(primaryDay.LocationName) + " (" + E(primaryDay.LocationLat) + "°N, " + E(primaryDay.LocationLon) + "°E) &nbsp;|&nbsp; <b>" + BuilderI18n.T("Mapy:", "Maps:") + "</b> " + E(m.MapReferences) + "</div>");
            html.AppendLine("</div><div class='faction-badge'>" + E(factionName) + "</div></div>");

            if (exportedDays.Count > 0)
            {
                html.AppendLine("<table><thead><tr><th style='width:18%'>" + BuilderI18n.T("📅 Dzień i Rejon", "📅 Day & AO") + "</th><th style='width:38%'>" + BuilderI18n.T("Pogoda METOC / Słońce / Księżyc", "METOC Weather / Sun / Moon") + "</th><th>" + BuilderI18n.T("Harmonogram, teren i zadania dnia", "Schedule, Terrain & Day Tasks") + "</th></tr></thead><tbody>");
                foreach (var d in exportedDays)
                {
                    html.AppendLine("<tr><td><b>" + E(d.DayTitle) + "</b><br/>" + E(d.DateIso) + "<br/><small>" + E(d.LocationName) + "</small></td>" +
                                    "<td>" + E(d.MetocCondition) + "<br/>Temp: " + E(d.MetocTemperature) + "<br/>" + E(d.MetocSunTimes) + "<br/>" + E(d.MetocMoonPhase) + "</td>" +
                                    "<td>" + E(d.DaySpecificTasks) + "<hr style='border:0;border-top:1px dashed #aaa;margin:4px 0;'/><b>" + BuilderI18n.T("Teren i METOC:", "Terrain & METOC:") + "</b><br/>" + E(d.TerrainAndWeather) + "</td></tr>");
                }
                html.AppendLine("</tbody></table>");
            }

            html.AppendLine("<h2>" + BuilderI18n.T("1. SYTUACJA", "1. SITUATION") + "</h2>");
            html.AppendLine("<div class='sub-p'><b>" + BuilderI18n.T("A. Obszar zainteresowania (Mapa, POI, KML/GPX):", "A. Area of Interest (Map, POI, KML/GPX):") + "</b><br/>" + E(m.AreaOfInterestPoiDesc) + "</div>");
            html.AppendLine("<div class='sub-p'><b>" + BuilderI18n.T("B. Obszar działań (Teren i Pogoda METOC):", "B. Area of Operations (Terrain & METOC):") + "</b><br/>" + E(primaryDay.TerrainAndWeather) + "</div>");
            html.AppendLine("<div class='sub-p'><b>" + BuilderI18n.T("C. Siły wroga (Skład, lokalizacje, COA):", "C. Enemy Forces (Composition, locations, COA):") + "</b><br/>" + E(m.EnemyForces) + "</div>");
            html.AppendLine("<div class='sub-p'><b>" + BuilderI18n.T("D. Siły przyjazne (Misja HQ i sąsiadów):", "D. Friendly Forces (HQ & adjacent units):") + "</b><br/>" + E(m.FriendlyForces) + "</div>");

            html.AppendLine("<h2>" + BuilderI18n.T("II. MISJA", "II. MISSION") + "</h2>");
            html.AppendLine("<div class='sub-p'><b>" + BuilderI18n.T("A. Opis fabularny:", "A. Story / Lore:") + "</b> " + E(m.MissionLoreDesc) + "</div>");
            html.AppendLine("<div class='mission-box'>" + BuilderI18n.T("B. Główne zadanie: ", "B. Primary Mission: ") + E(string.IsNullOrWhiteSpace(m.MissionFullStatement) ? Compose5WMissionText(m) : m.MissionFullStatement) + "</div>");
            html.AppendLine("<div class='sub-p'><b>" + BuilderI18n.T("C. Dodatkowe zadania:", "C. Additional Tasks:") + "</b><br/>" + E(m.AdditionalTasksSummary) + "</div>");

            html.AppendLine("<h2>" + BuilderI18n.T("III. WYKONANIE", "III. EXECUTION") + "</h2>");
            html.AppendLine("<div class='sub-p'><b>" + BuilderI18n.T("A. Zamiar dowódcy:", "A. Commander's Intent:") + "</b><br/>" + E(m.CommandersIntent) + "</div>");
            html.AppendLine("<div class='sub-p'><b>" + BuilderI18n.T("B. Priorytety i styl działania:", "B. Priorities & Style of Action:") + "</b><br/>" + E(m.ConceptOfOperations) + "</div>");
            html.AppendLine("<div class='sub-p'><b>" + BuilderI18n.T("C. Łańcuch dowodzenia:", "C. Chain of Command:") + "</b> <b>" + E(m.SuccessionOfCommand) + "</b></div>");

            if (m.EnableSquadRosterTable && m.SquadRoster.Count > 0)
            {
                html.AppendLine("<div class='sub-p'><b>" + BuilderI18n.T("D. Skład oddziału i podział ról:", "D. Squad Roster & Roles:") + "</b></div>");
                html.AppendLine("<table><thead><tr><th>" + BuilderI18n.T("Ksywa (Operator / Zespół)", "Callsign (Operator / Team)") + "</th><th>" + BuilderI18n.T("Rola 1", "Primary Role") + "</th><th>" + BuilderI18n.T("Rola 2 (opcjonalnie)", "Secondary Role") + "</th><th>" + BuilderI18n.T("Typ broni / Sprzęt", "Weapon / Equipment") + "</th></tr></thead><tbody>");
                foreach (var r in m.SquadRoster)
                {
                    html.AppendLine("<tr><td><b>" + E(r.OperatorOrTeam) + "</b></td><td>" + E(r.PrimaryRole) + "</td><td>" + E(r.SecondaryRole) + "</td><td>" + E(r.WeaponAndGear) + "</td></tr>");
                }
                html.AppendLine("</tbody></table>");
            }

            html.AppendLine("<div class='sub-p'><b>" + BuilderI18n.T("E. Tabela łączności", "E. Communications Table") + " (LoRa Meshtastic: " + E(chName) + " [" + E(region) + "/" + E(preset) + "] | OTS: " + E(ots) + " | SOS ORGA: " + E(phone) + "):</b></div>");
            html.AppendLine("<table><thead><tr><th>" + BuilderI18n.T("Rola / Kanał", "Role / Channel") + "</th><th>" + BuilderI18n.T("Pasmo", "Band") + "</th><th>" + BuilderI18n.T("Częstotliwość / Kanał", "Frequency / Channel") + "</th><th>" + BuilderI18n.T("Ton CTCSS / Kod DMR", "CTCSS Tone / DMR Code") + "</th></tr></thead><tbody>" + radioRowsHtml + "</tbody></table>");

            html.AppendLine("<div class='sub-p'><b>" + BuilderI18n.T("F. Sygnały, słowa kodowe i rozpoznawcze:", "F. Signals, Codewords & Challenge:") + "</b><br/><b>" + BuilderI18n.T("1. Kryptonimy celów:", "1. Objective Code Names:") + "</b> " + E(m.TargetCodeNames) + "</div>");
            html.AppendLine("<table><thead><tr><th colspan='10' style='text-align:center;'>" + BuilderI18n.T("2. Tabela krypto SARNEG główna (Słowo-klucz: ", "2. Main SARNEG Crypto Table (Keyword: ") + E(sarnegClean) + ")</th></tr><tr>" + sarnegLettersRow + "</tr></thead><tbody><tr>" + sarnegDigitsRow + "</tr></tbody></table>");
            html.AppendLine("<div class='sub-p'><b>" + BuilderI18n.T("3. Hasła rozpoznawcze (werbalne i niewerbalne):", "3. Challenge & Password (verbal & non-verbal):") + "</b><br/>" + E(m.ChallengeAndPassword) + "</div>");
            html.AppendLine("<div class='sub-p'><b>" + BuilderI18n.T("G. Leczenie i zasady Respawn:", "G. Medical & Respawn Rules:") + "</b><br/>" + E(m.MedevacRules) + "</div>");

            html.AppendLine("<h2>" + BuilderI18n.T("IV. LOGISTYKA", "IV. LOGISTICS") + "</h2>");
            html.AppendLine("<div class='sub-p'><b>" + BuilderI18n.T("A. Styl wyposażenia:", "A. Loadout Style:") + "</b> " + E(m.RespawnAndSupply) + "</div>");
            html.AppendLine("<div class='sub-p'><b>" + BuilderI18n.T("B. Wyposażenie oddziału (SZPEJLISTA):", "B. Squad Equipment Checklist:") + "</b><br/>" + E(m.SquadGearChecklist) + "</div>");
            html.AppendLine("<div class='sub-p'><b>" + BuilderI18n.T("C. Wyposażenie specjalne:", "C. Special Equipment:") + "</b><br/>" + E(m.SpecialEquipment) + "</div>");
            html.AppendLine("<div class='sub-p'><b>" + BuilderI18n.T("D. Dojazd (Parking, odległość, transport):", "D. Travel & Parking:") + "</b><br/>" + E(m.TravelAndParking) + "</div>");
            html.AppendLine("<div class='sub-p'><b>" + BuilderI18n.T("E. Regulamin gry:", "E. Game Rules:") + "</b> " + E(m.GameRulesLink) + "</div>");

            if (m.EnableRolesAnnex && m.CustomRolesAnnex.Count > 0)
            {
                html.AppendLine("<h2>" + BuilderI18n.T("ZAŁĄCZNIK 1. PODZIAŁ RÓL DRUŻYNOWYCH", "ANNEX 1. SQUAD ROLE DEFINITIONS") + "</h2>");
                html.AppendLine("<table><thead><tr><th style='width:22%'>" + BuilderI18n.T("Rola", "Role") + "</th><th>" + BuilderI18n.T("Opis obowiązków", "Duties Description") + "</th><th style='width:15%'>" + BuilderI18n.T("Zajęcie", "Duty Mode") + "</th></tr></thead><tbody>");
                foreach (var roleDef in m.CustomRolesAnnex)
                {
                    html.AppendLine("<tr><td><b>" + E(roleDef.RoleName) + "</b></td><td>" + E(roleDef.Description) + "</td><td>" + E(roleDef.DutyMode) + "</td></tr>");
                }
                html.AppendLine("</tbody></table>");
            }

            html.AppendLine("<h2>" + BuilderI18n.T("ZAŁĄCZNIK 2. OPRACOWANIE ZADAŃ", "ANNEX 2. TASKS BREAKDOWN") + "</h2>");
            html.AppendLine("<div class='sub-p'>" + E(m.Annex2TasksAnalysis) + "</div>");
            html.AppendLine("<div class='classification' style='margin-top:12px;'>" + E(m.Classification) + " — Field TAK Hub v3.2</div>");
            html.AppendLine("</body></html>");
            return html.ToString();
        }

        public static string GenerateDiscordMarkdownOpord(Window win, string factionName, OpordParagraphModel m)
        {
            EnsureDefaultCollectionsInitialized(m);
            var exportedDays = GetSelectedDaysForExport(m);
            string eventName = win != null ? TacticalConfigPanel.GetEventNameFromWindow(win) : "OPERACJA";
            var sb = new StringBuilder();
            sb.AppendLine("# 📋 " + m.OperationNumber + " — " + eventName + " [" + factionName + "]");
            sb.AppendLine("`" + m.Classification + "` | **DTG:** `" + m.DtgIssued + "` | **HQ:** `" + m.IssuingHq + "`");
            sb.AppendLine("---");

            foreach (var d in exportedDays)
            {
                sb.AppendLine("### 📅 " + d.DayTitle + " (" + d.DateIso + ") — " + d.LocationName);
                sb.AppendLine(d.DaySpecificTasks);
                sb.AppendLine("**METOC / Teren:**\n" + d.TerrainAndWeather);
                sb.AppendLine();
            }

            sb.AppendLine("## 1️⃣ " + BuilderI18n.T("SYTUACJA (SITUATION & METOC)", "SITUATION & METOC"));
            sb.AppendLine("- **A. AOI / POI:** " + m.AreaOfInterestPoiDesc);
            sb.AppendLine("- **C. Enemy Forces (COA):** " + m.EnemyForces);
            sb.AppendLine("- **D. Friendly Forces:** " + m.FriendlyForces);
            sb.AppendLine();
            sb.AppendLine("## 2️⃣ " + BuilderI18n.T("MISJA (MISSION)", "MISSION"));
            sb.AppendLine("- **A. Lore:** " + m.MissionLoreDesc);
            sb.AppendLine("> 🎯 **B. Primary Mission:** **" + Compose5WMissionText(m) + "**");
            sb.AppendLine("- **C. Additional Tasks:**\n" + m.AdditionalTasksSummary);
            sb.AppendLine();
            sb.AppendLine("## 3️⃣ " + BuilderI18n.T("WYKONANIE (EXECUTION)", "EXECUTION"));
            sb.AppendLine("- **A. Commander's Intent:** " + m.CommandersIntent);
            sb.AppendLine("- **B. Style of Action:** " + m.ConceptOfOperations);
            sb.AppendLine("- **C. Chain of Command:** `" + m.SuccessionOfCommand + "`");
            if (m.EnableSquadRosterTable && m.SquadRoster.Count > 0)
            {
                sb.AppendLine("**D. Squad Roster:**");
                sb.AppendLine("```text");
                foreach (var r in m.SquadRoster)
                {
                    sb.AppendLine(string.Format("{0,-12} | {1,-24} | {2,-20} | {3}", r.OperatorOrTeam, r.PrimaryRole, r.SecondaryRole, r.WeaponAndGear));
                }
                sb.AppendLine("```");
            }
            sb.AppendLine("**E. Comms Table:**");
            sb.AppendLine("```text");
            sb.AppendLine("• Primary: " + m.PrimaryRadioFreq);
            sb.AppendLine("• Backup:  " + m.BackupRadioFreq);
            sb.AppendLine("• HQ:      " + m.HqRadioFreq);
            sb.AppendLine(BuildLiveSignalSummaryText(win, factionName));
            sb.AppendLine("```");
            sb.AppendLine("**F. Signals & SARNEG:**");
            sb.AppendLine("- **Code Names:** " + m.TargetCodeNames);
            sb.AppendLine("```text\n" + FormatSarnegMatrixPreview(m.SarnegKeyWord10) + "\n```");
            sb.AppendLine("- **Challenge & Password:**\n" + m.ChallengeAndPassword);
            sb.AppendLine("- **G. MEDEVAC / Respawn:** " + m.MedevacRules);
            sb.AppendLine();
            sb.AppendLine("## 4️⃣ " + BuilderI18n.T("LOGISTYKA (LOGISTICS)", "LOGISTICS"));
            sb.AppendLine("- **A. Loadout:** " + m.RespawnAndSupply);
            sb.AppendLine("- **B. Checklist:** " + m.SquadGearChecklist);
            sb.AppendLine("- **C. Special Gear:** " + m.SpecialEquipment);
            sb.AppendLine("- **D. Travel & Parking:** " + m.TravelAndParking);
            sb.AppendLine("- **E. Rules:** " + m.GameRulesLink);
            if (m.EnableRolesAnnex && m.CustomRolesAnnex.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## 📎 " + BuilderI18n.T("ZAŁĄCZNIK 1. PODZIAŁ RÓL DRUŻYNOWYCH", "ANNEX 1. SQUAD ROLE DEFINITIONS"));
                foreach (var rd in m.CustomRolesAnnex)
                {
                    sb.AppendLine("- **" + rd.RoleName + "** (`" + rd.DutyMode + "`): " + rd.Description);
                }
            }
            return sb.ToString();
        }
    }
}
