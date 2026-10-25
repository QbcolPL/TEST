#pragma warning disable CS0414, CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625
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
    // 1. DWUJĘZYCZNY SILNIK LOKALIZACJI INTERFEJSU (PL <-> ENG)
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
            ["Nazwa strony konfliktu"] = "Faction / Side Name",
            ["Kolor taktyczny (HEX)"] = "Tactical Color (HEX)",
            ["Nazwa kanału Meshtastic"] = "Meshtastic Channel Name",
            ["Klucz szyfrowania PSK (Base64)"] = "Encryption Key PSK (Base64)",
            ["Region LoRa"] = "LoRa Region",
            ["Preset modemu LoRa"] = "LoRa Modem Preset",
            ["Limit skoków (Hop Limit)"] = "Hop Limit",
            ["Adres serwera OpenTAK (OTS Host)"] = "OpenTAKServer Address (OTS Host)",
            ["Telefon alarmowy ORGA / MEDEVAC"] = "Emergency Phone (ORGA / MEDEVAC)",
            ["Kanały Głosowe (PMR / VHF / UHF / DMR) — Ściąga Operacyjna"] = "Voice Radio Channels (PMR / VHF / UHF / DMR) — Field Cheat Sheet"
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
    // 2. GENERATOR SŁÓW 10-LITEROWYCH SARNEG (SŁOWNIK PL / EN + ONLINE API)
    // =========================================================================
    internal static class SarnegDictionaryService
    {
        private static readonly Random Rng = new Random();
        private static int _lastPlIndex = -1;
        private static int _lastEnIndex = -1;

        // Słownik polskich 10-literowych słów bez powtarzających się liter (izogramy 0-9 dla SARNEG)
        private static readonly string[] Polish10LetterIsograms = new string[]
        {
            "SZTURMOWCA", "SZTURMOWCY", "SZTURMOWEJ", "STRZELNICA", "UZBROJENIA",
            "LACZNIKOWY", "DYWERSANCI", "PULKOWNICY", "CZOLGISTKA", "KOMENDATUR",
            "BLYSKAWICE", "PRZEDMIOTY", "SKORUPIDLA", "MASKOWNICE", "KONSPIRACJ",
            "CHMURZASTY", "GRZMOTNELY", "SKRZYDLATE", "PRZEBOJAMI", "OKRAZENIUM",
            "WSPORNIKEM", "ZABUDOWANI", "rozpatruje".ToUpperInvariant(), "SCHRONISKA"
        };

        // Słownik angielskich 10-literowych słów bez powtarzających się liter (izogramy 0-9 dla SARNEG)
        private static readonly string[] English10LetterIsograms = new string[]
        {
            "PATHFINDER", "SUBMACHINE", "FLASHPOINT", "OUTFLANKED", "BINOCULARS",
            "DECRYPTING", "AFTERSHOCK", "GUNPOWDERS", "BLACKSMITH", "BACKGROUND",
            "DOWNSTREAM", "NIGHTMARES", "SHOCKWAVES", "IMPORTANCE", "COMPLETING",
            "LUMBERJACK", "AMPERSANDS", "REGULATION", " AUTHORIZED".Trim(), "DESTROYING"
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

        public static string DrawRandomWord(bool english)
        {
            string[] pool = english
                ? English10LetterIsograms.Where(Has10UniqueLetters).ToArray()
                : Polish10LetterIsograms.Where(Has10UniqueLetters).ToArray();

            if (pool.Length == 0)
            {
                return english ? "PATHFINDER" : "SZTURMOWCA";
            }

            int idx = Rng.Next(pool.Length);
            if (english && pool.Length > 1 && idx == _lastEnIndex)
                idx = (idx + 1) % pool.Length;
            if (!english && pool.Length > 1 && idx == _lastPlIndex)
                idx = (idx + 1) % pool.Length;

            if (english) _lastEnIndex = idx;
            else _lastPlIndex = idx;

            return NormalizePolishChars(pool[idx]);
        }

        public static async Task<string> DrawRandomWordWithOnlineSupportAsync(bool english, HttpClient http)
        {
            if (english && http != null)
            {
                try
                {
                    // Pobierz losowe 10-literowe słowa ze słownika Datamuse i wybierz izogram (10 unikalnych liter)
                    char startChar = "ABCDEFGHJKLMNPQRSTUVW"[Rng.Next(21)];
                    string url = "https://api.datamuse.com/words?sp=" + startChar + "?????????&max=40";
                    string json = await http.GetStringAsync(url);
                    if (JsonNode.Parse(json) is JsonArray arr && arr.Count > 0)
                    {
                        var validOnline = new List<string>();
                        foreach (var item in arr)
                        {
                            string w = item?["word"]?.ToString() ?? "";
                            if (Has10UniqueLetters(w))
                            {
                                validOnline.Add(NormalizePolishChars(w));
                            }
                        }
                        if (validOnline.Count > 0)
                        {
                            return validOnline[Rng.Next(validOnline.Count)];
                        }
                    }
                }
                catch { }
            }
            return DrawRandomWord(english);
        }
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
            c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "FieldTakHub-OPORD-METOC/3.1");
            return c;
        }

        public static (bool Found, double Lat, double Lon, string SourceDescription, string ExtractedPoiList) TryExtractAoCenterFromMapStudio(Window win)
        {
            var lats = new List<double>();
            var lons = new List<double>();
            var poiNames = new List<string>();
            string srcDesc = "Obszar AO z Generatora Map 1:1";

            try
            {
                string sourceDir = TacticalConfigPanel.GetPrimarySourceDirectory(win);
                if (Directory.Exists(sourceDir))
                {
                    var mapFiles = Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories)
                        .Where(f => f.EndsWith(".gpx", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".kml", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith("map_state.json", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(File.GetLastWriteTimeUtc)
                        .ToList();

                    foreach (string mf in mapFiles)
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

                        var coordMatches = Regex.Matches(raw, @"([0-9]{1,2}\.[0-9]{3,8}),([0-9]{1,2}\.[0-9]{3,8})");
                        foreach (Match cm in coordMatches)
                        {
                            if (double.TryParse(cm.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double v1) &&
                                double.TryParse(cm.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double v2))
                            {
                                if (v2 >= 45.0 && v2 <= 60.0 && v1 >= 10.0 && v1 <= 30.0)
                                {
                                    lats.Add(v2);
                                    lons.Add(v1);
                                }
                                else if (v1 >= 45.0 && v1 <= 60.0 && v2 >= 10.0 && v2 <= 30.0)
                                {
                                    lats.Add(v1);
                                    lons.Add(v2);
                                }
                            }
                        }

                        if (lats.Count > 0)
                        {
                            srcDesc = "Obszar AO z Mapy (" + Path.GetFileName(mf) + ")";
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
                string url = "https://geocoding-api.open-meteo.com/v1/search?name=" +
                             Uri.EscapeDataString(query.Trim()) + "&count=1&language=pl&format=json";
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

                    condText = DecodeWmoWeatherCode(wmoCode) + string.Format(CultureInfo.InvariantCulture, " | Zachmurzenie: {0}% | Widoczność: {1} | QNH: {2:0} hPa", avgClouds, visEst, avgPressure);
                    tempText = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:0.0}°C .. {1:0.0}°C  (Odczuwalna: {2:0.0}°C .. {3:0.0}°C)",
                        tMin, tMax, appMin, appMax);

                    double windMs = windMaxKmh / 3.6;
                    double gustsMs = gustsMaxKmh / 3.6;
                    string dirCardinal = DegreesToCardinal(windDeg);
                    windText = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:0.0} km/h ({1:0.0} m/s)  |  Porywy: {2:0.0} km/h ({3:0.0} m/s)  |  Kierunek: {4}° ({5})",
                        windMaxKmh, windMs, gustsMaxKmh, gustsMs, windDeg, dirCardinal);

                    humPrecipText = string.Format(
                        CultureInfo.InvariantCulture,
                        "Wilgotność: ~{0}%  |  Opady: {1:0.0} mm (Szansa: {2}%)",
                        avgHumidity, precipMm, precipProb);
                }
            }
            catch { }

            string sunLine = string.Format(
                "BMNT (Świt): {0}  |  ☀️ Wschód (SR): {1}  |  🌇 Zachód (SS): {2}  |  EENT (Zmierzch NVG): {3}",
                bmntStr, srStr, ssStr, eentStr);

            string locDisplay = string.IsNullOrWhiteSpace(locationLabel)
                ? string.Format(CultureInfo.InvariantCulture, "{0:0.0000}°N, {1:0.0000}°E", lat, lon)
                : string.Format(CultureInfo.InvariantCulture, "{0} ({1:0.0000}°N, {2:0.0000}°E)", locationLabel, lat, lon);

            var sb = new StringBuilder();
            sb.AppendLine("🌦️ [PROGNOZA POGODY I DANE ŚWIETLNE METOC — " + cleanDate + "]");
            sb.AppendLine("• Rejon AO: " + locDisplay);
            sb.AppendLine("• Zjawiska i widoczność: " + condText);
            sb.AppendLine("• Temperatura: " + tempText);
            sb.AppendLine("• Wiatr: " + windText);
            sb.AppendLine("• Wilgotność i opady: " + humPrecipText);
            sb.AppendLine("• Słońce (BMNT / Wschód / Zachód / EENT): " + sunLine);
            sb.AppendLine("• Faza Księżyca i NVG: " + moonInfo.FullText);

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
            if (phaseFraction < 0.04 || phaseFraction >= 0.96)
            {
                icon = "🌑"; phasePl = "Nów (New Moon)"; phaseEn = "New Moon";
            }
            else if (phaseFraction < 0.21)
            {
                icon = "🌒"; phasePl = "Przybywający Sierp (Waxing Crescent)"; phaseEn = "Waxing Crescent";
            }
            else if (phaseFraction < 0.29)
            {
                icon = "🌓"; phasePl = "Pierwsza Kwadra (First Quarter)"; phaseEn = "First Quarter";
            }
            else if (phaseFraction < 0.46)
            {
                icon = "🌔"; phasePl = "Przybywający Garb (Waxing Gibbous)"; phaseEn = "Waxing Gibbous";
            }
            else if (phaseFraction < 0.54)
            {
                icon = "🌕"; phasePl = "Pełnia (Full Moon)"; phaseEn = "Full Moon";
            }
            else if (phaseFraction < 0.71)
            {
                icon = "🌖"; phasePl = "Ubywający Garb (Waning Gibbous)"; phaseEn = "Waning Gibbous";
            }
            else if (phaseFraction < 0.79)
            {
                icon = "🌗"; phasePl = "Trzecia Kwadra (Last Quarter)"; phaseEn = "Last Quarter";
            }
            else
            {
                icon = "🌘"; phasePl = "Ubywający Sierp (Waning Crescent)"; phaseEn = "Waning Crescent";
            }

            string nvgRating = illumPct >= 55
                ? BuilderI18n.T("Dobra widoczność pasywna NVG", "Good passive NVG visibility")
                : (illumPct >= 20
                    ? BuilderI18n.T("Umiarkowana jasność nocna", "Moderate ambient light")
                    : BuilderI18n.T("Ciemna noc (zalecane doświetlenie IR w lesie)", "Dark night (IR illuminator recommended under canopy)"));

            string phaseLabel = BuilderI18n.T(phasePl, phaseEn);
            string full = string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1}  |  Iluminacja: {2}%  |  {3}",
                icon, phaseLabel, illumPct, nvgRating);

            return (phaseLabel, illumPct, full);
        }

        private static (string Bmnt, string Sunrise, string Sunset, string Eent) CalculateAstronomicalSunTimes(DateTime date, double lat, double lon)
        {
            try
            {
                int dayOfYear = date.DayOfYear;
                double gamma = 2.0 * Math.PI / 365.0 * (dayOfYear - 1);
                double eqTime = 229.18 * (0.000075 + 0.001868 * Math.Cos(gamma) - 0.032077 * Math.Sin(gamma)
                                - 0.014615 * Math.Cos(2 * gamma) - 0.040849 * Math.Sin(2 * gamma));
                double decl = 0.006918 - 0.399912 * Math.Cos(gamma) + 0.070257 * Math.Sin(gamma)
                              - 0.006758 * Math.Cos(2 * gamma) + 0.000907 * Math.Sin(2 * gamma)
                              - 0.002697 * Math.Cos(3 * gamma) + 0.00148 * Math.Sin(3 * gamma);

                double latRad = lat * Math.PI / 180.0;
                double zenithRad = 90.833 * Math.PI / 180.0;
                double cosHa = (Math.Cos(zenithRad) / (Math.Cos(latRad) * Math.Cos(decl))) - Math.Tan(latRad) * Math.Tan(decl);
                cosHa = Math.Clamp(cosHa, -1.0, 1.0);
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
            catch
            {
                return ("05:45", "06:20", "18:10", "18:45");
            }
        }

        private static string DecodeWmoWeatherCode(int code)
        {
            return code switch
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
        }

        private static string DegreesToCardinal(int deg)
        {
            string[] dirs = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            int idx = (int)Math.Round(((deg % 360) + 360) % 360 / 45.0) % 8;
            return dirs[idx];
        }

        private static double GetFirstDouble(JsonNode node, double fallback)
        {
            try
            {
                if (node is JsonArray arr && arr.Count > 0 && arr[0] != null)
                    return Convert.ToDouble(arr[0].ToString(), CultureInfo.InvariantCulture);
            }
            catch { }
            return fallback;
        }

        private static int GetFirstInt(JsonNode node, int fallback)
        {
            try
            {
                if (node is JsonArray arr && arr.Count > 0 && arr[0] != null)
                    return (int)Math.Round(Convert.ToDouble(arr[0].ToString(), CultureInfo.InvariantCulture));
            }
            catch { }
            return fallback;
        }

        private static string GetFirstString(JsonNode node, string fallback)
        {
            try
            {
                if (node is JsonArray arr && arr.Count > 0 && arr[0] != null)
                    return arr[0].ToString();
            }
            catch { }
            return fallback;
        }

        private static double GetAverageDouble(JsonNode node, double fallback)
        {
            try
            {
                if (node is JsonArray arr && arr.Count > 0)
                {
                    var vals = new List<double>();
                    foreach (var item in arr)
                    {
                        if (item != null && double.TryParse(item.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double d))
                            vals.Add(d);
                    }
                    if (vals.Count > 0) return vals.Average();
                }
            }
            catch { }
            return fallback;
        }
    }

    // =========================================================================
    // 4. MODELE DANYCH OPORD v3.1 (Z OPCJONALNYM EDYTOWALNYM ZAŁĄCZNIKIEM 1 RÓL)
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
        public string DateIso { get; set; } = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        public string MetocCondition { get; set; } = "Brak pobranego raportu (kliknij przycisk '🌦️ Pobierz Pogodę, Słońce i Księżyc METOC')";
        public string MetocTemperature { get; set; } = "-";
        public string MetocWind { get; set; } = "-";
        public string MetocHumidityPrecip { get; set; } = "-";
        public string MetocSunTimes { get; set; } = "-";
        public string MetocMoonPhase { get; set; } = "-";
        public string DaySpecificTasks { get; set; } = "• 08:30 — Odprawa sztabowa i sprawdzenie łączności\n• 09:00 — Wejście w rejon działań (Faza Dzienno-Rozpoznawcza)\n• 14:00 — Rotacja na punkcie zaopatrzenia i realizacja zadań głównych";
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

        // I. SYTUACJA
        public string AreaOfInterestPoiDesc { get; set; } = "Punkty kluczowe (FOB, Respawn, OBJ ALPHA, LZ) zdefiniowane w załączonym pliku .KMZ (ATAK) oraz .GPX (Garmin/Suunto).";
        public string TerrainAndWeather { get; set; } = "Wielkość AO: ok. 2x2 km. Rodzaj: las mieszany, polany, zabudowania leśne. Ukształtowanie: pagórkowate ze skarpami. Przeszkody terenowe: cieki wodne, rów melioracyjny, drogi leśne.";
        public string EnemyForces { get; set; } = "1. Skład, dyspozycja i siła: Pluton lekki przeciwnika podzielony na patrole 4-6 osobowe.\n2. Lokalizacje i możliwości: Posterunki na skrzyżowaniach dróg, łączność radiowa, dymy.\n3. Kursy działań wroga (COA): Obrona rejonu centralnego i zasadzki na szlakach podejścia.";
        public string FriendlyForces { get; set; } = "1. Misja i zamiar wyższego dowództwa (HQ): Opanowanie i kontrola sektora przed zmrokiem.\n2. Misja sąsiednich jednostek: Sąsiednie drużyny wiążą siły przeciwnika na skrzydłach.";

        // II. MISJA
        public string MissionLoreDesc { get; set; } = "Opis fabularny (opcjonalnie): Działania rozpoznawczo-dywersyjne na zapleczu ugrupowania przeciwnika.";
        public string MissionWho { get; set; } = "Drużyna zadaniowa BLUFOR";
        public string MissionWhat { get; set; } = "zlokalizuje i zabezpieczy kody sektorowe oraz utrzyma obiekt OBJ ALPHA";
        public string MissionWhen { get; set; } = "w godzinach operacyjnych 09:00 - 17:00";
        public string MissionWhere { get; set; } = "w wyznaczonym obszarze AO (siatka MGRS)";
        public string MissionWhy { get; set; } = "w celu uzyskania przewagi punktowej i odcięcia zaopatrzenia przeciwnika.";
        public string MissionFullStatement { get; set; } = "";
        public string AdditionalTasksSummary { get; set; } = "• Zadanie poboczne 1: Rozpoznanie stanowisk dowodzenia wroga (Foto).\n• Zadanie poboczne 2: Przejęcie pojemnika fabularnego (szczegóły w Załączniku 2).";

        // III. WYKONANIE
        public string CommandersIntent { get; set; } = "Jedziemy działać zespołowo, przećwiczyć nawigację MGRS/ATAK i zrealizować 100% zadań głównych bez zbędnych strat.";
        public string ConceptOfOperations { get; set; } = "Priorytety i styl działania: Skryte przemieszczanie czujką z przodu. Unikamy niepotrzebnego kontaktu ogniowego podczas marszu do celu. Walczymy wyłącznie przy przewadze ogniowej lub bezpośrednio o obiekt zadaniowy.";
        public string TasksToSubordinateUnits { get; set; } = "• Sekcja Szturmowa: Wejście na obiekt i czyszczenie przedpola.\n• Wsparcie / DMR: Osłona skrzydeł i obserwacja dróg podejścia.";
        public string CoordinatingAndRoe { get; set; } = "• Obowiązkowa ochrona wzroku przez cały czas trwania gry.\n• Weryfikacja pozycji sojuszników w ATAK przed otwarciem ognia.";
        public string SuccessionOfCommand { get; set; } = "Dowódca (Lider)  ->  1. Zastępca (Navi / RTO)  ->  2. Zastępca (Dowódca sekcji)";

        public bool EnableSquadRosterTable { get; set; } = true;
        public List<OpordRosterRow> SquadRoster { get; set; } = new List<OpordRosterRow>();

        public string PrimaryRadioFreq { get; set; } = "446.00625 MHz (PMR CH 1)";
        public string BackupRadioFreq { get; set; } = "446.03125 MHz (PMR CH 3)";
        public string HqRadioFreq { get; set; } = "446.09375 MHz (PMR CH 8)";

        public string TargetCodeNames { get; set; } = "• OBJ ALPHA = 'ZAMEK'   |   • Punkt Zbiórki (RP) = 'KUŹNIA'   |   • FOB / Respawn = 'BAZA'";
        public string SarnegKeyWord10 { get; set; } = "SZTURMOWCA";
        public List<OpordCodewordRow> CodewordsTable { get; set; } = new List<OpordCodewordRow>();
        public string ChallengeAndPassword { get; set; } = "a) Werbalne (Wywołanie - Odpowiedź - Potwierdzenie): GROM - BŁYSK - TARCZA (Suma cyfr: 7)\nb) Niewerbalne (Wywołanie - Odzew): 2 błyski czerwonym światłem - 1 błysk + podniesienie lewej ręki";

        // III.G Leczenie i IV. LOGISTYKA
        public string MedevacRules { get; set; } = "Pierwsze trafienie: opatrunek przez medyka lub członka drużyny (czas 2 min). Drugie trafienie: odejście na Respawn. W razie realnego wypadku: użyć przycisku 🚨 SOS MGRS w aplikacji Field TAK Hub.";
        public string RespawnAndSupply { get; set; } = "Styl wyposażenia: Na 12h w terenie (woda min. 2L, racja żywnościowa, zapasowe baterie do radia i powerbank do ATAK).";
        public string SquadGearChecklist { get; set; } = "SZPEJLISTA: Okulary ochronne, czerwona szmatka (dead-rag), gwizdek/latarka czerwona na noc, radio PMR/VHF z zapasową baterią, telefon z aplikacją Field TAK Hub & ATAK-CIV, kompas, długopis wodoodporny.";
        public string SpecialEquipment { get; set; } = "Wyposażenie specjalne: Rekwizyty misyjne ze sztabu, znaczniki chemiczne (lightstick), aparat/telefon dla Fotografa.";
        public string TravelAndParking { get; set; } = "1. Parking: Zgodnie z pinezką zbiórki w paczce .GPX / .KMZ.\n2. Odległość z parkingu na miejsce gry: ok. 300 m.\n3. Transport (kto z kim): Auto 1 (Lider + 3 os.), Auto 2 (Zastępca + 3 os.).";
        public string GameRulesLink { get; set; } = "https://";

        // Opcjonalny i edytowalny Załącznik 1. Podział ról drużynowych
        public bool EnableRolesAnnex { get; set; } = false;
        public List<OpordRoleDefinitionRow> CustomRolesAnnex { get; set; } = new List<OpordRoleDefinitionRow>();

        // Załącznik 2. Szczegółowe opracowanie zadań
        public string Annex2TasksAnalysis { get; set; } = "ZAŁĄCZNIK 2 — OPRACOWANIE ZADAŃ:\n• Analiza priorytetów punktowych, okna czasowe na wykonanie misji specjalnych oraz podział zespołów na zadania równoległe.";
    }

    internal static class OpordBuilderManager
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
                        DateIso = string.IsNullOrWhiteSpace(m.OperationDateIso)
                            ? DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                            : m.OperationDateIso
                    }
                };
                m.ActiveDayIndex = 0;
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

        // =========================================================================
        // 5. WIDOK GENERATORA OPORD v3.1
        // =========================================================================
        public static Border CreateOrGetOpordOverlayView(Window win)
        {
            _lastActiveWindowRef = win;
            if (_cachedOpordOverlay != null)
            {
                return _cachedOpordOverlay;
            }

            var rootBorder = new Border
            {
                Tag = "INJECTED_OPORD_OVERLAY",
                Background = new SolidColorBrush(Color.FromRgb(7, 17, 21)),
                Padding = new Thickness(18, 14, 18, 18),
                Visibility = Visibility.Collapsed
            };

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            var mainStack = new StackPanel();
            scroll.Content = mainStack;
            rootBorder.Child = scroll;

            void RebuildOpordForm()
            {
                mainStack.Children.Clear();
                var configuredFactions = TacticalConfigPanel.GetConfiguredFactions();
                if (configuredFactions.Count > 0 && !configuredFactions.Any(f => f.Name.Equals(_activeOpordFaction, StringComparison.OrdinalIgnoreCase)))
                {
                    _activeOpordFaction = configuredFactions[0].Name;
                }

                var model = GetOrCreateModel(_activeOpordFaction);
                model.ActiveDayIndex = Math.Clamp(model.ActiveDayIndex, 0, Math.Max(0, model.Days.Count - 1));
                var activeDay = model.Days[model.ActiveDayIndex];
                model.OperationDateIso = activeDay.DateIso;

                string eventName = TacticalConfigPanel.GetEventNameFromWindow(win);
                SyncModelRadioChannelsFromFactionGenerator(win, _activeOpordFaction, model);

                // -----------------------------------------------------------------
                // PASEK NAGŁÓWKOWY GENERATORA OPORD v3.1 + DYSTRYBUCJA LAN / WI-FI
                // -----------------------------------------------------------------
                var topCard = new Border
                {
                    Background = TacticalConfigPanel.BrushCardBg,
                    BorderBrush = TacticalConfigPanel.BrushAccent,
                    BorderThickness = new Thickness(2),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(16, 12, 16, 12),
                    Margin = new Thickness(0, 0, 0, 10)
                };

                var topStack = new StackPanel();
                topStack.Children.Add(new TextBlock
                {
                    Text = BuilderI18n.T(
                        "📋 GENERATOR ROZKAZU BOJOWEGO MILSIM / NATO (OPORD v3.1 + SARNEG + METOC) — " + eventName,
                        "📋 MILSIM / NATO OPORD v3.1 BUILDER (SARNEG + METOC + SQUAD ROSTER) — " + eventName),
                    Foreground = TacticalConfigPanel.BrushAccent,
                    FontWeight = FontWeights.Bold,
                    FontSize = 14.5,
                    Margin = new Thickness(0, 0, 0, 4)
                });

                topStack.Children.Add(new TextBlock
                {
                    Text = BuilderI18n.T(
                        "Wzór drużynowy MilSim i NATO: przełącznik klauzuli JAWNE / TAJNE, wielodniowe zakładki (+ / -), inteligentny moduł pogodowy METOC (automatycznie pobierający AO z Mapy), Tabela Składu (+ / -), generator 10-literowych słów SARNEG (PL/EN) oraz opcjonalny, edytowalny Załącznik 1 Ról.",
                        "Complete MilSim & NATO OPORD suite: UNCLASSIFIED / SECRET toggle, multi-day tabs (+ / -), smart METOC weather (auto-pulling Map AO), Squad Table (+ / -), 10-letter SARNEG word generator (PL/EN), and optional editable Roles Annex."),
                    Foreground = TacticalConfigPanel.BrushMutedText,
                    FontSize = 10.5,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 10)
                });

                var actionBar = new Grid();
                actionBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
                actionBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
                actionBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
                actionBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
                actionBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });

                var facCombo = new ComboBox
                {
                    Height = 32,
                    FontSize = 11.5,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 6, 0),
                    Tag = "INJECTED"
                };
                TacticalConfigPanel.ApplyUnifiedComboBoxTheme(facCombo, win);
                foreach (var cf in configuredFactions)
                    facCombo.Items.Add(cf.Name);
                if (facCombo.Items.Count == 0) facCombo.Items.Add("BLUFOR");
                facCombo.SelectedItem = _activeOpordFaction;
                facCombo.SelectionChanged += (_, __) =>
                {
                    if (facCombo.SelectedItem is string s && !string.IsNullOrWhiteSpace(s) && s != _activeOpordFaction)
                    {
                        _activeOpordFaction = s;
                        RebuildOpordForm();
                    }
                };
                Grid.SetColumn(facCombo, 0);
                actionBar.Children.Add(facCombo);

                var btnExportHtml = new Button
                {
                    Content = BuilderI18n.T("🖨️ Drukuj OPORD A4 (HTML / PDF)", "🖨️ Print A4 OPORD (HTML / PDF)"),
                    Height = 32,
                    Margin = new Thickness(0, 0, 6, 0),
                    Background = TacticalConfigPanel.BrushButtonAccent,
                    Foreground = TacticalConfigPanel.BrushDarkFg,
                    FontWeight = FontWeights.Bold,
                    FontSize = 10.5,
                    Cursor = Cursors.Hand
                };
                btnExportHtml.Click += (_, __) =>
                {
                    PersistModelToAppData(model);
                    SaveAllOpordsToDisk(win);
                    string html = GeneratePrintableOpordHtml(win, _activeOpordFaction, model);
                    string outPath = Path.Combine(Path.GetTempPath(), "OPORD_" + TacticalConfigPanel.SanitizeFileName(eventName) + "_" + TacticalConfigPanel.SanitizeFileName(_activeOpordFaction) + ".html");
                    File.WriteAllText(outPath, html, Encoding.UTF8);
                    try { Process.Start(new ProcessStartInfo(outPath) { UseShellExecute = true }); } catch { }
                };
                Grid.SetColumn(btnExportHtml, 1);
                actionBar.Children.Add(btnExportHtml);

                var btnShareLanQr = new Button
                {
                    Content = BuilderI18n.T("📡 Udostępnij OPORD (Wi-Fi / LAN QR)", "📡 Share OPORD (Wi-Fi / LAN QR)"),
                    Height = 32,
                    Margin = new Thickness(0, 0, 6, 0),
                    Background = new SolidColorBrush(Color.FromRgb(22, 54, 66)),
                    Foreground = TacticalConfigPanel.BrushAccent,
                    BorderBrush = TacticalConfigPanel.BrushAccent,
                    BorderThickness = new Thickness(1.4),
                    FontWeight = FontWeights.Bold,
                    FontSize = 10.5,
                    Cursor = Cursors.Hand
                };
                btnShareLanQr.Click += (_, __) =>
                {
                    PersistModelToAppData(model);
                    SaveAllOpordsToDisk(win);
                    ShowOpordLanDistributionModal(win, _activeOpordFaction, model);
                };
                Grid.SetColumn(btnShareLanQr, 2);
                actionBar.Children.Add(btnShareLanQr);

                var btnCopyDiscord = new Button
                {
                    Content = BuilderI18n.T("💬 Kopiuj na Discorda (Markdown)", "💬 Copy for Discord (Markdown)"),
                    Height = 32,
                    Margin = new Thickness(0, 0, 6, 0),
                    Background = TacticalConfigPanel.BrushItemBg,
                    Foreground = Brushes.White,
                    BorderBrush = TacticalConfigPanel.BrushCardBorder,
                    BorderThickness = new Thickness(1),
                    FontWeight = FontWeights.Bold,
                    FontSize = 10.5,
                    Cursor = Cursors.Hand
                };
                btnCopyDiscord.Click += (_, __) =>
                {
                    PersistModelToAppData(model);
                    SaveAllOpordsToDisk(win);
                    string md = GenerateDiscordMarkdownOpord(win, _activeOpordFaction, model);
                    Clipboard.SetText(md);
                    MessageBox.Show(
                        win,
                        BuilderI18n.T(
                            "Skopiowano kompletny rozkaz OPORD strony " + _activeOpordFaction + " do schowka! Możesz wkleić go na Discordzie (Ctrl+V).",
                            "Copied complete OPORD for " + _activeOpordFaction + " to clipboard!"),
                        "OPORD -> Discord Markdown",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                };
                Grid.SetColumn(btnCopyDiscord, 3);
                actionBar.Children.Add(btnCopyDiscord);

                var btnSaveToFtak = new Button
                {
                    Content = BuilderI18n.T("📦 Zapisz w .FTAK", "📦 Save to .FTAK"),
                    Height = 32,
                    Background = TacticalConfigPanel.BrushItemBg,
                    Foreground = TacticalConfigPanel.BrushAccent,
                    BorderBrush = TacticalConfigPanel.BrushAccent,
                    BorderThickness = new Thickness(1),
                    FontWeight = FontWeights.Bold,
                    FontSize = 10.5,
                    Cursor = Cursors.Hand
                };
                btnSaveToFtak.Click += (_, __) =>
                {
                    PersistModelToAppData(model);
                    SaveAllOpordsToDisk(win);
                    MessageBox.Show(
                        win,
                        BuilderI18n.T(
                            "Zapisano rozkaz OPORD_" + _activeOpordFaction + " w katalogu wyjściowym frakcji!",
                            "Saved OPORD_" + _activeOpordFaction + " to the faction output folder!"),
                        "OPORD Saved",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                };
                Grid.SetColumn(btnSaveToFtak, 4);
                actionBar.Children.Add(btnSaveToFtak);

                topStack.Children.Add(actionBar);
                topCard.Child = topStack;
                mainStack.Children.Add(topCard);

                // -----------------------------------------------------------------
                // PASEK ZAKŁADEK KOLEJNYCH DNI OPERACJI (WEKTOROWY ZIELONY "+" I CZERWONY "-")
                // -----------------------------------------------------------------
                var daysBarBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(10, 24, 30)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(28, 62, 74)),
                    BorderThickness = new Thickness(1.5),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 0, 0, 10)
                };

                var daysDock = new DockPanel();
                daysDock.Children.Add(new TextBlock
                {
                    Text = BuilderI18n.T("📅 DNI OPERACJI (ZAKŁADKI DNI + / -):", "📅 OPERATION DAYS (DAY TABS + / -):"),
                    Foreground = TacticalConfigPanel.BrushAccent,
                    FontWeight = FontWeights.Bold,
                    FontSize = 11.5,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0)
                });

                var daysWrap = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
                for (int dIdx = 0; dIdx < model.Days.Count; dIdx++)
                {
                    int capturedIdx = dIdx;
                    var dayObj = model.Days[dIdx];
                    bool isSel = (dIdx == model.ActiveDayIndex);

                    var dayTabBtn = new Button
                    {
                        Content = "📆 " + dayObj.DayTitle + " (" + dayObj.DateIso + ")",
                        Height = 28,
                        Padding = new Thickness(12, 2, 12, 2),
                        Margin = new Thickness(0, 0, 6, 0),
                        Background = isSel ? TacticalConfigPanel.BrushButtonAccent : TacticalConfigPanel.BrushInputBg,
                        Foreground = isSel ? TacticalConfigPanel.BrushDarkFg : Brushes.White,
                        BorderBrush = isSel ? TacticalConfigPanel.BrushAccent : TacticalConfigPanel.BrushInputBorder,
                        BorderThickness = new Thickness(1.2),
                        FontWeight = FontWeights.Bold,
                        FontSize = 11,
                        Cursor = Cursors.Hand
                    };
                    dayTabBtn.Click += (_, __) =>
                    {
                        model.ActiveDayIndex = capturedIdx;
                        RebuildOpordForm();
                    };
                    daysWrap.Children.Add(dayTabBtn);
                }

                var btnAddDayPlus = CreateTacticalPlusMinusButton(true, BuilderI18n.T("Dodaj kolejny dzień operacji (+)", "Add next operation day (+)"), () =>
                {
                    DateTime lastDt = DateTime.Today;
                    if (model.Days.Count > 0 && DateTime.TryParseExact(model.Days.Last().DateIso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
                    {
                        lastDt = parsed.AddDays(1);
                    }
                    int nextNum = model.Days.Count + 1;
                    model.Days.Add(new OpordDayPlanModel
                    {
                        DayTitle = BuilderI18n.T("Dzień " + nextNum, "Day " + nextNum),
                        DateIso = lastDt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        DaySpecificTasks = BuilderI18n.T("• Zadania i harmonogram dla Dnia " + nextNum + "...", "• Tasks and schedule for Day " + nextNum + "...")
                    });
                    model.ActiveDayIndex = model.Days.Count - 1;
                    PersistModelToAppData(model);
                    RebuildOpordForm();
                });
                daysWrap.Children.Add(btnAddDayPlus);

                var btnRemoveDayMinus = CreateTacticalPlusMinusButton(false, BuilderI18n.T("Usuń aktywny dzień operacji (-)", "Remove active operation day (-)"), () =>
                {
                    if (model.Days.Count <= 1) return;
                    model.Days.RemoveAt(model.ActiveDayIndex);
                    for (int i = 0; i < model.Days.Count; i++)
                    {
                        model.Days[i].DayTitle = BuilderI18n.T("Dzień " + (i + 1), "Day " + (i + 1));
                    }
                    model.ActiveDayIndex = Math.Clamp(model.ActiveDayIndex - 1, 0, model.Days.Count - 1);
                    PersistModelToAppData(model);
                    RebuildOpordForm();
                });
                daysWrap.Children.Add(btnRemoveDayMinus);

                daysDock.Children.Add(daysWrap);
                daysBarBorder.Child = daysDock;
                mainStack.Children.Add(daysBarBorder);

                // -----------------------------------------------------------------
                // 0. NAGŁÓWEK ROZKAZU + PRZEŁĄCZNIK KLAUZULI "JAWNE / TAJNE"
                // -----------------------------------------------------------------
                var metaCard = CreateSectionCard(BuilderI18n.T(
                    "0. NAGŁÓWEK ROZKAZU, KLAUZULA TAJNOŚCI (JAWNE / TAJNE) ORAZ HARMONOGRAM: " + activeDay.DayTitle.ToUpperInvariant(),
                    "0. ORDER HEADER, CLASSIFICATION (UNCLASSIFIED / SECRET) & SCHEDULE: " + activeDay.DayTitle.ToUpperInvariant()));
                var metaStack = (StackPanel)metaCard.Child;

                // Przełącznik JAWNE / TAJNE nad polami nagłówka
                var classToggleRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
                classToggleRow.Children.Add(new TextBlock
                {
                    Text = BuilderI18n.T("Klauzula dokumentu OPORD:", "OPORD Security Classification:"),
                    Foreground = TacticalConfigPanel.BrushMutedText,
                    FontWeight = FontWeights.Bold,
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0)
                });

                TextBox classificationTextBoxRef = null;
                var btnJawne = new Button
                {
                    Content = BuilderI18n.T("🔓 JAWNE (UNCLASSIFIED)", "🔓 UNCLASSIFIED (JAWNE)"),
                    Height = 28,
                    Padding = new Thickness(14, 2, 14, 2),
                    Margin = new Thickness(0, 0, 6, 0),
                    FontWeight = FontWeights.Bold,
                    FontSize = 10.5,
                    Cursor = Cursors.Hand
                };
                var btnTajne = new Button
                {
                    Content = BuilderI18n.T("🔒 TAJNE (SECRET — TYLKO DLA STRONY)", "🔒 SECRET (TAJNE — FACTION EYES ONLY)"),
                    Height = 28,
                    Padding = new Thickness(14, 2, 14, 2),
                    FontWeight = FontWeights.Bold,
                    FontSize = 10.5,
                    Cursor = Cursors.Hand
                };

                void UpdateSecretToggleVisuals()
                {
                    bool isSec = model.IsSecretClassification || model.Classification.Contains("TAJNE", StringComparison.OrdinalIgnoreCase) || model.Classification.Contains("SECRET", StringComparison.OrdinalIgnoreCase);
                    model.IsSecretClassification = isSec;

                    btnJawne.Background = !isSec ? new SolidColorBrush(Color.FromRgb(22, 92, 54)) : TacticalConfigPanel.BrushInputBg;
                    btnJawne.Foreground = Brushes.White;
                    btnJawne.BorderBrush = !isSec ? new SolidColorBrush(Color.FromRgb(46, 204, 113)) : TacticalConfigPanel.BrushInputBorder;
                    btnJawne.BorderThickness = new Thickness(1.5);

                    btnTajne.Background = isSec ? new SolidColorBrush(Color.FromRgb(115, 24, 24)) : TacticalConfigPanel.BrushInputBg;
                    btnTajne.Foreground = Brushes.White;
                    btnTajne.BorderBrush = isSec ? new SolidColorBrush(Color.FromRgb(235, 64, 52)) : TacticalConfigPanel.BrushInputBorder;
                    btnTajne.BorderThickness = new Thickness(1.5);
                }

                btnJawne.Click += (_, __) =>
                {
                    model.IsSecretClassification = false;
                    model.Classification = BuilderI18n.T(
                        "JAWNE / UNCLASSIFIED — MILSIM DRUŻYNOWY OPORD",
                        "UNCLASSIFIED / JAWNE — MILSIM SQUAD OPORD");
                    if (classificationTextBoxRef != null) classificationTextBoxRef.Text = model.Classification;
                    UpdateSecretToggleVisuals();
                    PersistModelToAppData(model);
                };

                btnTajne.Click += (_, __) =>
                {
                    model.IsSecretClassification = true;
                    model.Classification = BuilderI18n.T(
                        "TAJNE / SECRET — TYLKO DO WIADOMOŚCI STRONY " + _activeOpordFaction.ToUpperInvariant(),
                        "SECRET / TAJNE — " + _activeOpordFaction.ToUpperInvariant() + " EYES ONLY");
                    if (classificationTextBoxRef != null) classificationTextBoxRef.Text = model.Classification;
                    UpdateSecretToggleVisuals();
                    PersistModelToAppData(model);
                };

                var toggleBtnsWrap = new StackPanel { Orientation = Orientation.Horizontal };
                toggleBtnsWrap.Children.Add(btnJawne);
                toggleBtnsWrap.Children.Add(btnTajne);
                classToggleRow.Children.Add(toggleBtnsWrap);
                metaStack.Children.Add(classToggleRow);

                var metaGrid = new UniformGrid { Columns = 4 };
                var classBoxPanel = CreateLabeledTextBox(BuilderI18n.T(" Nagłówek Klasyfikacji", "Classification Banner"), model.Classification, v => { model.Classification = v; UpdateSecretToggleVisuals(); PersistModelToAppData(model); });
                classificationTextBoxRef = FindFirstDescendant<TextBox>(classBoxPanel);
                UpdateSecretToggleVisuals();

                metaGrid.Children.Add(classBoxPanel);
                metaGrid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("Nr Rozkazu / Strzelanki", "Order / Event No."), model.OperationNumber, v => { model.OperationNumber = v; PersistModelToAppData(model); }));
                metaGrid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("Grupa Data-Czas (DTG)", "Date-Time Group (DTG)"), model.DtgIssued, v => { model.DtgIssued = v; PersistModelToAppData(model); }));
                metaGrid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("Sztab wydający (HQ)", "Issuing HQ"), model.IssuingHq, v => { model.IssuingHq = v; PersistModelToAppData(model); }));
                metaStack.Children.Add(metaGrid);

                metaStack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("Harmonogram i zadania dla aktywnej zakładki (" + activeDay.DayTitle + " — " + activeDay.DateIso + "):", "Schedule & tasks for active day tab (" + activeDay.DayTitle + " — " + activeDay.DateIso + "):"),
                    activeDay.DaySpecificTasks, 50, v => { activeDay.DaySpecificTasks = v; PersistModelToAppData(model); }));
                mainStack.Children.Add(metaCard);

                // -----------------------------------------------------------------
                // I. SYTUACJA (Z POJEDYNCZYM INTELIGENTNYM PRZYCISKIEM METOC + AUTO AO Z MAPY)
                // -----------------------------------------------------------------
                var sec1 = CreateSectionCard(BuilderI18n.T("I. SYTUACJA (1. SITUATION — OBSZAR ZAINTERESOWANIA, TEREN, METOC, WRÓG, SOJUSZNICY)", "I. SITUATION (AOI, TERRAIN, LIVE METOC, ENEMY, FRIENDLY FORCES)"));
                var sec1Stack = (StackPanel)sec1.Child;

                var aoiBoxPanel = CreateLabeledMultilineBox(
                    BuilderI18n.T("A. Obszar zainteresowania (1. Mapa terenu gry, 2. Opis punktów na mapie POI, 3. Pliki punktów KML / GPX):", "A. Area of Interest (1. Game Map, 2. Map Points / POI description, 3. KML / GPX files):"),
                    model.AreaOfInterestPoiDesc, 50, v => { model.AreaOfInterestPoiDesc = v; PersistModelToAppData(model); });
                var aoiTextBox = FindFirstDescendant<TextBox>(aoiBoxPanel);
                sec1Stack.Children.Add(aoiBoxPanel);

                // JEDEN ZINTEGROWANY PASEK I PRZYCISK METOC
                var metocBar = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(12, 31, 38)),
                    BorderBrush = TacticalConfigPanel.BrushAccent,
                    BorderThickness = new Thickness(1.5),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 10, 12, 10),
                    Margin = new Thickness(0, 4, 0, 8)
                };
                var metocStack = new StackPanel();
                metocStack.Children.Add(new TextBlock
                {
                    Text = BuilderI18n.T(
                        "🌦️ B.2. PROGNOZA POGODY, SŁOŃCE I FAZA KSIĘŻYCA (METOC — AUTOMATYCZNE ZACIĄGANIE OBSZARU AO Z MAPY LUB MIEJSCOWOŚCI)",
                        "🌦️ B.2. WEATHER, SUNRISE/SUNSET & MOON PHASE (METOC — AUTO-PULLING MAP AO OR TOWN)"),
                    Foreground = TacticalConfigPanel.BrushAccent,
                    FontWeight = FontWeights.Bold,
                    FontSize = 11.5,
                    Margin = new Thickness(0, 0, 0, 6)
                });

                var metocInputsGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                metocInputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(125) });
                metocInputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
                metocInputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                metocInputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                metocInputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });

                var dateBoxPanel = CreateLabeledTextBox(
                    BuilderI18n.T("Data (" + activeDay.DayTitle + ")", "Date (" + activeDay.DayTitle + ")"),
                    activeDay.DateIso,
                    v => { activeDay.DateIso = v; model.OperationDateIso = v; PersistModelToAppData(model); });
                Grid.SetColumn(dateBoxPanel, 0);
                metocInputsGrid.Children.Add(dateBoxPanel);

                var cityBoxPanel = CreateLabeledTextBox(
                    BuilderI18n.T("Miejscowość (puste = automatyczne AO z Mapy)", "Town (blank = auto Map AO)"),
                    model.LocationName,
                    v => { model.LocationName = v; PersistModelToAppData(model); });
                var cityTextBox = FindFirstDescendant<TextBox>(cityBoxPanel);
                Grid.SetColumn(cityBoxPanel, 1);
                metocInputsGrid.Children.Add(cityBoxPanel);

                var latBoxPanel = CreateLabeledTextBox("Lat (°N)", model.LocationLat, v => { model.LocationLat = v; PersistModelToAppData(model); });
                var latTextBox = FindFirstDescendant<TextBox>(latBoxPanel);
                Grid.SetColumn(latBoxPanel, 2);
                metocInputsGrid.Children.Add(latBoxPanel);

                var lonBoxPanel = CreateLabeledTextBox("Lon (°E)", model.LocationLon, v => { model.LocationLon = v; PersistModelToAppData(model); });
                var lonTextBox = FindFirstDescendant<TextBox>(lonBoxPanel);
                Grid.SetColumn(lonBoxPanel, 3);
                metocInputsGrid.Children.Add(lonBoxPanel);

                TextBox terrainWeatherTextBox = null;
                var metocStatusText = new TextBlock
                {
                    Text = "☀️ Słońce: " + activeDay.MetocSunTimes + "   |   🌙 Księżyc: " + activeDay.MetocMoonPhase + "\n🌡️ Pogoda: " + activeDay.MetocCondition + "   |   💨 Wiatr: " + activeDay.MetocWind,
                    Foreground = Brushes.White,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 10.5,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0)
                };

                var btnFetchUnifiedMetoc = new Button
                {
                    Content = BuilderI18n.T("🌦️ Pobierz Pogodę, Słońce i Księżyc (METOC / AO)", "🌦️ Fetch Weather, Sun & Moon (METOC / AO)"),
                    Height = 28,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 0, 4),
                    Background = TacticalConfigPanel.BrushButtonAccent,
                    Foreground = TacticalConfigPanel.BrushDarkFg,
                    FontWeight = FontWeights.Bold,
                    FontSize = 10.5,
                    Cursor = Cursors.Hand
                };

                btnFetchUnifiedMetoc.Click += async (_, __) =>
                {
                    btnFetchUnifiedMetoc.IsEnabled = false;
                    try
                    {
                        double lat = 49.8494;
                        double lon = 18.8127;
                        double.TryParse((model.LocationLat ?? "").Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out lat);
                        double.TryParse((model.LocationLon ?? "").Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out lon);

                        string trimmedCity = (model.LocationName ?? "").Trim();
                        bool isAutoMapLabel = string.IsNullOrWhiteSpace(trimmedCity) || trimmedCity.StartsWith("Obszar AO z", StringComparison.OrdinalIgnoreCase);

                        if (!isAutoMapLabel)
                        {
                            var geo = await OpordMetocWeatherService.GeocodeLocationAsync(trimmedCity);
                            if (geo.Ok)
                            {
                                lat = geo.Lat;
                                lon = geo.Lon;
                                model.LocationLat = lat.ToString("0.0000", CultureInfo.InvariantCulture);
                                model.LocationLon = lon.ToString("0.0000", CultureInfo.InvariantCulture);
                                model.LocationName = geo.DisplayName;
                                if (latTextBox != null) latTextBox.Text = model.LocationLat;
                                if (lonTextBox != null) lonTextBox.Text = model.LocationLon;
                                if (cityTextBox != null) cityTextBox.Text = model.LocationName;
                            }
                        }
                        else
                        {
                            // Brak wpisanej miejscowości -> automatycznie pobierz środek obszaru AO zaznaczonego w Generatorze Map 1:1
                            var aoRes = OpordMetocWeatherService.TryExtractAoCenterFromMapStudio(win);
                            if (aoRes.Found)
                            {
                                lat = aoRes.Lat;
                                lon = aoRes.Lon;
                                model.LocationLat = lat.ToString("0.0000", CultureInfo.InvariantCulture);
                                model.LocationLon = lon.ToString("0.0000", CultureInfo.InvariantCulture);
                                model.LocationName = aoRes.SourceDescription;
                                if (latTextBox != null) latTextBox.Text = model.LocationLat;
                                if (lonTextBox != null) lonTextBox.Text = model.LocationLon;
                                if (cityTextBox != null) cityTextBox.Text = model.LocationName;

                                if (!string.IsNullOrWhiteSpace(aoRes.ExtractedPoiList) && aoiTextBox != null && !model.AreaOfInterestPoiDesc.Contains(aoRes.ExtractedPoiList))
                                {
                                    model.AreaOfInterestPoiDesc = model.AreaOfInterestPoiDesc.TrimEnd() + "\n" + aoRes.ExtractedPoiList;
                                    aoiTextBox.Text = model.AreaOfInterestPoiDesc;
                                }
                            }
                        }

                        var rep = await OpordMetocWeatherService.FetchMetocReportAsync(lat, lon, activeDay.DateIso, model.LocationName);
                        activeDay.MetocCondition = rep.ConditionText;
                        activeDay.MetocTemperature = rep.TempRangeText;
                        activeDay.MetocWind = rep.WindText;
                        activeDay.MetocHumidityPrecip = rep.HumidityAndPrecipText;
                        activeDay.MetocSunTimes = rep.SunTimesText;
                        activeDay.MetocMoonPhase = rep.MoonPhaseText;

                        model.MetocCondition = rep.ConditionText;
                        model.MetocTemperature = rep.TempRangeText;
                        model.MetocWind = rep.WindText;
                        model.MetocHumidityPrecip = rep.HumidityAndPrecipText;
                        model.MetocSunTimes = rep.SunTimesText;
                        model.MetocMoonPhase = rep.MoonPhaseText;

                        metocStatusText.Text =
                            "☀️ " + rep.SunTimesText + "\n" +
                            "🌙 " + rep.MoonPhaseText + "\n" +
                            "🌡️ Temp: " + rep.TempRangeText + "  |  " + rep.HumidityAndPrecipText + "\n" +
                            "💨 Wiatr: " + rep.WindText;

                        string currentTerrain = (model.TerrainAndWeather ?? "").Trim();
                        int oldIdx = currentTerrain.IndexOf("🌦️ [", StringComparison.Ordinal);
                        if (oldIdx >= 0)
                        {
                            currentTerrain = currentTerrain.Substring(0, oldIdx).TrimEnd();
                        }
                        model.TerrainAndWeather = string.IsNullOrWhiteSpace(currentTerrain)
                            ? rep.FullTacticalSummary
                            : (currentTerrain + "\n\n" + rep.FullTacticalSummary);

                        if (terrainWeatherTextBox != null)
                        {
                            terrainWeatherTextBox.Text = model.TerrainAndWeather;
                        }
                        PersistModelToAppData(model);
                    }
                    finally
                    {
                        btnFetchUnifiedMetoc.IsEnabled = true;
                    }
                };
                Grid.SetColumn(btnFetchUnifiedMetoc, 4);
                metocInputsGrid.Children.Add(btnFetchUnifiedMetoc);

                metocStack.Children.Add(metocInputsGrid);
                metocStack.Children.Add(metocStatusText);
                metocBar.Child = metocStack;
                sec1Stack.Children.Add(metocBar);

                var terrainBoxPanel = CreateLabeledMultilineBox(
                    BuilderI18n.T("B.1 & B.2. Obszar działań — Analiza terenu (wielkość, las/pola/budynki, ukształtowanie, przeszkody) + Pogoda METOC:", "B.1 & B.2. Area of Operations — Terrain analysis (size, cover, elevation, obstacles) + METOC Weather:"),
                    model.TerrainAndWeather, 82, v => { model.TerrainAndWeather = v; PersistModelToAppData(model); });
                terrainWeatherTextBox = FindFirstDescendant<TextBox>(terrainBoxPanel);
                sec1Stack.Children.Add(terrainBoxPanel);

                sec1Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("C. Siły wroga (1. Skład, dyspozycja i siła, 2. Lokalizacje i możliwości, 3. Kursy działań wroga — COA):", "C. Enemy Forces (1. Composition & strength, 2. Locations & capabilities, 3. Courses of Action — COA):"),
                    model.EnemyForces, 60, v => { model.EnemyForces = v; PersistModelToAppData(model); }));

                sec1Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("D. Siły przyjazne (1. Misja i zamiar wyższego dowództwa HQ, 2. Misja sąsiednich jednostek):", "D. Friendly Forces (1. Higher HQ mission & intent, 2. Adjacent units mission):"),
                    model.FriendlyForces, 50, v => { model.FriendlyForces = v; PersistModelToAppData(model); }));
                mainStack.Children.Add(sec1);

                // -----------------------------------------------------------------
                // II. MISJA
                // -----------------------------------------------------------------
                var sec2 = CreateSectionCard(BuilderI18n.T("II. MISJA (2. MISSION — OPIS FABULARNY, GŁÓWNE ZADANIE 5W, ZADANIA DODATKOWE)", "II. MISSION (LORE, PRIMARY 5W TASK, ADDITIONAL TASKS)"));
                var sec2Stack = (StackPanel)sec2.Child;

                sec2Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("A. Opis fabularny (nieobowiązkowy):", "A. Story / Lore Background (optional):"),
                    model.MissionLoreDesc, 42, v => { model.MissionLoreDesc = v; PersistModelToAppData(model); }));

                var w5Grid = new UniformGrid { Columns = 5, Margin = new Thickness(0, 0, 0, 6) };
                TextBox fullMissionBox = null;

                void AutoUpdate5W()
                {
                    model.MissionFullStatement = Compose5WMissionText(model);
                    if (fullMissionBox != null) fullMissionBox.Text = model.MissionFullStatement;
                    PersistModelToAppData(model);
                }

                w5Grid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("KTO (Oddział)", "WHO (Unit)"), model.MissionWho, v => { model.MissionWho = v; AutoUpdate5W(); }));
                w5Grid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("CO (Główne zadanie)", "WHAT (Primary Task)"), model.MissionWhat, v => { model.MissionWhat = v; AutoUpdate5W(); }));
                w5Grid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("KIEDY (Czas)", "WHEN (Time)"), model.MissionWhen, v => { model.MissionWhen = v; AutoUpdate5W(); }));
                w5Grid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("GDZIE (Sektor / MGRS)", "WHERE (Grid / AO)"), model.MissionWhere, v => { model.MissionWhere = v; AutoUpdate5W(); }));
                w5Grid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("DLACZEGO (Cel)", "WHY (Purpose)"), model.MissionWhy, v => { model.MissionWhy = v; AutoUpdate5W(); }));
                sec2Stack.Children.Add(w5Grid);

                var fullMissionPanel = CreateLabeledMultilineBox(
                    BuilderI18n.T("B. Główne zadanie (np. zbieranie kodów / utrzymanie sektora):", "B. Primary Mission Statement:"),
                    model.MissionFullStatement, 44, v => { model.MissionFullStatement = v; PersistModelToAppData(model); });
                fullMissionBox = FindFirstDescendant<TextBox>(fullMissionPanel);
                sec2Stack.Children.Add(fullMissionPanel);

                sec2Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("C. Dodatkowe zadania (lista zadań — szczegółowa analiza w Załączniku 2 na dole):", "C. Additional Tasks (task list — detailed analysis in Annex 2 below):"),
                    model.AdditionalTasksSummary, 48, v => { model.AdditionalTasksSummary = v; PersistModelToAppData(model); }));
                mainStack.Children.Add(sec2);

                // -----------------------------------------------------------------
                // III. WYKONANIE (Z WEKTOROWYMI PRZYCISKAMI +/- ORAZ GENERATOREM SŁÓW SARNEG PL/EN)
                // -----------------------------------------------------------------
                var sec3 = CreateSectionCard(BuilderI18n.T("III. WYKONANIE (3. EXECUTION — ZAMIAR, STYL DZIAŁANIA, TABELA SKŁADU I RÓL, ŁĄCZNOŚĆ, GENERATOR SARNEG)", "III. EXECUTION (INTENT, STYLE OF ACTION, SQUAD ROSTER TABLE, COMMS, SARNEG WORD GENERATOR)"));
                var sec3Stack = (StackPanel)sec3.Child;

                sec3Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("A. Zamiar dowódcy (Cel wyjazdu: wygrana / dobra zabawa / trening taktyczny / priorytet):", "A. Commander's Intent (Primary mindset & goal of the operation):"),
                    model.CommandersIntent, 46, v => { model.CommandersIntent = v; PersistModelToAppData(model); }));

                sec3Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("B. Priorytety i styl działania (Czy unikamy kontaktu i zrywamy, czy walczymy? Warunki podjęcia walki):", "B. Priorities & Style of Action (Avoid contact & break vs engage; engagement criteria):"),
                    model.ConceptOfOperations, 50, v => { model.ConceptOfOperations = v; PersistModelToAppData(model); }));

                sec3Stack.Children.Add(CreateLabeledTextBox(
                    BuilderI18n.T("C. Łańcuch dowodzenia (Dowódca -> Zastępca 1 -> Zastępca 2):", "C. Chain of Command (Commander -> Deputy 1 -> Deputy 2):"),
                    model.SuccessionOfCommand, v => { model.SuccessionOfCommand = v; PersistModelToAppData(model); }));

                // III.D TABELA SKŁADU ODDZIAŁU (Z WEKTOROWYMI PRZYCISKAMI + / -)
                var rosterCard = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(10, 24, 30)),
                    BorderBrush = TacticalConfigPanel.BrushAccent,
                    BorderThickness = new Thickness(1.5),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 10, 12, 10),
                    Margin = new Thickness(0, 6, 0, 10)
                };
                var rosterOuterStack = new StackPanel();

                var rosterHeaderDock = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var rosterToggleCheck = new CheckBox
                {
                    IsChecked = model.EnableSquadRosterTable,
                    Content = BuilderI18n.T(
                        "  D. Aktywuj Tabelę Składu Oddziału, Podziału Ról i Sprzętu (Operator/Zespół | Rola 1 | Rola 2 | Broń/Sprzęt)",
                        "  D. Enable Squad Roster, Roles & Equipment Table (Operator/Team | Role 1 | Role 2 | Weapon/Gear)"),
                    Foreground = TacticalConfigPanel.BrushAccent,
                    FontWeight = FontWeights.Bold,
                    FontSize = 11.5,
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = Cursors.Hand
                };
                rosterHeaderDock.Children.Add(rosterToggleCheck);

                var rosterButtonsPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                DockPanel.SetDock(rosterButtonsPanel, Dock.Right);

                var btnAddOperatorPlus = CreateTacticalPlusMinusButton(true, BuilderI18n.T("Dodaj wiersz Operatora / Zespołu (+)", "Add Operator / Team row (+)"), () =>
                {
                    int nextNum = model.SquadRoster.Count + 1;
                    model.SquadRoster.Add(new OpordRosterRow
                    {
                        OperatorOrTeam = "ALFA-" + nextNum,
                        PrimaryRole = "Czujka",
                        SecondaryRole = "Tragarz",
                        WeaponAndGear = "Karabinek szturmowy + Radio"
                    });
                    PersistModelToAppData(model);
                    RebuildOpordForm();
                });
                rosterButtonsPanel.Children.Add(btnAddOperatorPlus);

                var btnRemoveLastOperatorMinus = CreateTacticalPlusMinusButton(false, BuilderI18n.T("Usuń ostatni wiersz tabeli (-)", "Remove last table row (-)"), () =>
                {
                    if (model.SquadRoster.Count > 0)
                    {
                        model.SquadRoster.RemoveAt(model.SquadRoster.Count - 1);
                        PersistModelToAppData(model);
                        RebuildOpordForm();
                    }
                });
                rosterButtonsPanel.Children.Add(btnRemoveLastOperatorMinus);
                rosterHeaderDock.Children.Add(rosterButtonsPanel);
                rosterOuterStack.Children.Add(rosterHeaderDock);

                var rosterTableContainer = new StackPanel
                {
                    Visibility = model.EnableSquadRosterTable ? Visibility.Visible : Visibility.Collapsed
                };

                rosterToggleCheck.Checked += (_, __) =>
                {
                    model.EnableSquadRosterTable = true;
                    rosterTableContainer.Visibility = Visibility.Visible;
                    PersistModelToAppData(model);
                };
                rosterToggleCheck.Unchecked += (_, __) =>
                {
                    model.EnableSquadRosterTable = false;
                    rosterTableContainer.Visibility = Visibility.Collapsed;
                    PersistModelToAppData(model);
                };

                var colHdrGrid = new Grid { Margin = new Thickness(0, 2, 0, 4) };
                colHdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
                colHdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
                colHdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
                colHdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
                colHdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

                string[] colNames = new[]
                {
                    BuilderI18n.T("Operator / Zespół (Ksywa)", "Operator / Team (Callsign)"),
                    BuilderI18n.T("Rola 1 (Załącznik 1)", "Primary Role (Annex 1)"),
                    BuilderI18n.T("Rola 2 (Opcjonalnie)", "Secondary Role (Optional)"),
                    BuilderI18n.T("Typ broni i sprzęt (Szturm / DMR / Snajper)", "Weapon Type & Equipment"),
                    ""
                };
                for (int c = 0; c < colNames.Length; c++)
                {
                    var tb = new TextBlock
                    {
                        Text = colNames[c],
                        Foreground = TacticalConfigPanel.BrushMutedText,
                        FontSize = 10,
                        FontWeight = FontWeights.Bold
                    };
                    Grid.SetColumn(tb, c);
                    colHdrGrid.Children.Add(tb);
                }
                rosterTableContainer.Children.Add(colHdrGrid);

                string[] roleOptions = model.CustomRolesAnnex.Select(r => r.RoleName).Concat(new[] { "Szturmowiec", "Strzelec wyborowy (DMR)", "Snajper", "Wsparcie (KM)", "Brak" }).Distinct().ToArray();

                for (int rIdx = 0; rIdx < model.SquadRoster.Count; rIdx++)
                {
                    int capturedRowIdx = rIdx;
                    var rowModel = model.SquadRoster[rIdx];

                    var rowGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

                    var tbOp = CreateCompactGridTextBox(rowModel.OperatorOrTeam, v => { rowModel.OperatorOrTeam = v; PersistModelToAppData(model); });
                    Grid.SetColumn(tbOp, 0);
                    rowGrid.Children.Add(tbOp);

                    var cbRole1 = CreateEditableRoleComboBox(win, roleOptions, rowModel.PrimaryRole, v => { rowModel.PrimaryRole = v; PersistModelToAppData(model); });
                    Grid.SetColumn(cbRole1, 1);
                    rowGrid.Children.Add(cbRole1);

                    var cbRole2 = CreateEditableRoleComboBox(win, roleOptions, rowModel.SecondaryRole, v => { rowModel.SecondaryRole = v; PersistModelToAppData(model); });
                    Grid.SetColumn(cbRole2, 2);
                    rowGrid.Children.Add(cbRole2);

                    var tbGear = CreateCompactGridTextBox(rowModel.WeaponAndGear, v => { rowModel.WeaponAndGear = v; PersistModelToAppData(model); });
                    Grid.SetColumn(tbGear, 3);
                    rowGrid.Children.Add(tbGear);

                    var btnDelRow = CreateTacticalPlusMinusButton(false, BuilderI18n.T("Usuń ten wiersz (-)", "Remove this row (-)"), () =>
                    {
                        model.SquadRoster.RemoveAt(capturedRowIdx);
                        PersistModelToAppData(model);
                        RebuildOpordForm();
                    });
                    Grid.SetColumn(btnDelRow, 4);
                    rowGrid.Children.Add(btnDelRow);

                    rosterTableContainer.Children.Add(rowGrid);
                }

                rosterOuterStack.Children.Add(rosterTableContainer);
                rosterCard.Child = rosterOuterStack;
                sec3Stack.Children.Add(rosterCard);

                // III.E TABELA ŁĄCZNOŚCI
                var commsHeaderDock = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
                commsHeaderDock.Children.Add(new TextBlock
                {
                    Text = BuilderI18n.T(
                        "E. Tabela łączności (Kanał główny, zapasowy, HQ + automatyczna lista z Generatora Stron):",
                        "E. Communications Table (Primary, Backup, HQ + live channels from Faction Generator):"),
                    Foreground = TacticalConfigPanel.BrushAccent,
                    FontWeight = FontWeights.Bold,
                    FontSize = 11.5,
                    VerticalAlignment = VerticalAlignment.Center
                });

                var btnSyncRadioNow = new Button
                {
                    Content = BuilderI18n.T("🔄 Odśwież częstotliwości z Generatora Stron", "🔄 Sync Frequencies from Faction Generator"),
                    Height = 26,
                    Padding = new Thickness(10, 2, 10, 2),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Background = new SolidColorBrush(Color.FromRgb(18, 48, 58)),
                    Foreground = TacticalConfigPanel.BrushAccent,
                    BorderBrush = TacticalConfigPanel.BrushAccent,
                    BorderThickness = new Thickness(1),
                    FontWeight = FontWeights.Bold,
                    FontSize = 10,
                    Cursor = Cursors.Hand
                };
                DockPanel.SetDock(btnSyncRadioNow, Dock.Right);
                btnSyncRadioNow.Click += (_, __) =>
                {
                    SyncModelRadioChannelsFromFactionGenerator(win, _activeOpordFaction, model, forceOverwrite: true);
                    PersistModelToAppData(model);
                    RebuildOpordForm();
                };
                commsHeaderDock.Children.Add(btnSyncRadioNow);
                sec3Stack.Children.Add(commsHeaderDock);

                var radio3Grid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 6) };
                radio3Grid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("Kanał główny", "Primary Channel"), model.PrimaryRadioFreq, v => { model.PrimaryRadioFreq = v; PersistModelToAppData(model); }));
                radio3Grid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("Kanał zapasowy", "Backup Channel"), model.BackupRadioFreq, v => { model.BackupRadioFreq = v; PersistModelToAppData(model); }));
                radio3Grid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("Kanał HQ (Sztab)", "HQ Channel"), model.HqRadioFreq, v => { model.HqRadioFreq = v; PersistModelToAppData(model); }));
                sec3Stack.Children.Add(radio3Grid);

                var liveSignalInfo = new Border
                {
                    Background = TacticalConfigPanel.BrushInputBg,
                    BorderBrush = TacticalConfigPanel.BrushAccent,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                liveSignalInfo.Child = new TextBlock
                {
                    Text = BuildLiveSignalSummaryText(win, _activeOpordFaction),
                    Foreground = TacticalConfigPanel.BrushAccent,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 10.5,
                    TextWrapping = TextWrapping.Wrap
                };
                sec3Stack.Children.Add(liveSignalInfo);

                // -----------------------------------------------------------------
                // III.F SYGNAŁY + GENERATOR 10-LITEROWYCH SŁÓW SARNEG (PL / EN + WŁASNE SŁOWA)
                // -----------------------------------------------------------------
                sec3Stack.Children.Add(CreateLabeledTextBox(
                    BuilderI18n.T("F.1. Kryptonimy celów i punktów na mapie:", "F.1. Objective & Waypoint Code Names:"),
                    model.TargetCodeNames, v => { model.TargetCodeNames = v; PersistModelToAppData(model); }));

                var sarnegCard = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(10, 24, 30)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(250, 208, 44)),
                    BorderThickness = new Thickness(1.2),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 4, 0, 8)
                };
                var sarnegStack = new StackPanel();
                sarnegStack.Children.Add(new TextBlock
                {
                    Text = BuilderI18n.T(
                        "🔐 F.2. TABELA KRYPTO SARNEG (SZYFROWANIE WSPÓŁRZĘDNYCH 0–9) + GENERATOR SŁÓW 10-LITEROWYCH (PL / EN)",
                        "🔐 F.2. SARNEG CRYPTO TABLE (0–9 GRID ENCRYPTION) + 10-LETTER WORD GENERATOR (PL / EN)"),
                    Foreground = new SolidColorBrush(Color.FromRgb(250, 208, 44)),
                    FontWeight = FontWeights.Bold,
                    FontSize = 11,
                    Margin = new Thickness(0, 0, 0, 6)
                });

                var sarnegPreviewLabel = new TextBlock
                {
                    Text = FormatSarnegMatrixPreview(model.SarnegKeyWord10),
                    Foreground = Brushes.White,
                    FontFamily = new FontFamily("Consolas"),
                    FontWeight = FontWeights.Bold,
                    FontSize = 12,
                    Margin = new Thickness(0, 4, 0, 4)
                };

                var sarnegInputRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                sarnegInputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
                sarnegInputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
                sarnegInputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });

                TextBox sarnegKeyTextBox = null;
                var sarnegKeyBoxPanel = CreateLabeledTextBox(
                    BuilderI18n.T("10-literowe słowo-klucz SARNEG (wpisz własne lub wylosuj ze słownika PL/EN):", "10-letter SARNEG Key Word (type custom or draw from PL/EN dictionary):"),
                    model.SarnegKeyWord10,
                    v =>
                    {
                        model.SarnegKeyWord10 = v.ToUpperInvariant();
                        sarnegPreviewLabel.Text = FormatSarnegMatrixPreview(model.SarnegKeyWord10);
                        PersistModelToAppData(model);
                    });
                sarnegKeyTextBox = FindFirstDescendant<TextBox>(sarnegKeyBoxPanel);
                Grid.SetColumn(sarnegKeyBoxPanel, 0);
                sarnegInputRow.Children.Add(sarnegKeyBoxPanel);

                var btnDrawPlWord = new Button
                {
                    Content = BuilderI18n.T("🎲 Losuj słowo PL (10 liter)", "🎲 Draw Polish Word (10 letters)"),
                    Height = 28,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(4, 0, 4, 4),
                    Background = new SolidColorBrush(Color.FromRgb(22, 54, 66)),
                    Foreground = new SolidColorBrush(Color.FromRgb(250, 208, 44)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(250, 208, 44)),
                    BorderThickness = new Thickness(1.2),
                    FontWeight = FontWeights.Bold,
                    FontSize = 10.5,
                    Cursor = Cursors.Hand
                };
                btnDrawPlWord.Click += (_, __) =>
                {
                    string drawn = SarnegDictionaryService.DrawRandomWord(english: false);
                    model.SarnegKeyWord10 = drawn;
                    if (sarnegKeyTextBox != null) sarnegKeyTextBox.Text = drawn;
                    sarnegPreviewLabel.Text = FormatSarnegMatrixPreview(drawn);
                    PersistModelToAppData(model);
                };
                Grid.SetColumn(btnDrawPlWord, 1);
                sarnegInputRow.Children.Add(btnDrawPlWord);

                var btnDrawEnWord = new Button
                {
                    Content = BuilderI18n.T("🎲 Losuj słowo EN (10 letters)", "🎲 Draw English Word (10 letters)"),
                    Height = 28,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(4, 0, 0, 4),
                    Background = new SolidColorBrush(Color.FromRgb(22, 54, 66)),
                    Foreground = TacticalConfigPanel.BrushAccent,
                    BorderBrush = TacticalConfigPanel.BrushAccent,
                    BorderThickness = new Thickness(1.2),
                    FontWeight = FontWeights.Bold,
                    FontSize = 10.5,
                    Cursor = Cursors.Hand
                };
                btnDrawEnWord.Click += async (_, __) =>
                {
                    btnDrawEnWord.IsEnabled = false;
                    try
                    {
                        string drawn = await SarnegDictionaryService.DrawRandomWordWithOnlineSupportAsync(english: true, OpordMetocWeatherService.Http);
                        model.SarnegKeyWord10 = drawn;
                        if (sarnegKeyTextBox != null) sarnegKeyTextBox.Text = drawn;
                        sarnegPreviewLabel.Text = FormatSarnegMatrixPreview(drawn);
                        PersistModelToAppData(model);
                    }
                    finally
                    {
                        btnDrawEnWord.IsEnabled = true;
                    }
                };
                Grid.SetColumn(btnDrawEnWord, 2);
                sarnegInputRow.Children.Add(btnDrawEnWord);

                sarnegStack.Children.Add(sarnegInputRow);
                sarnegStack.Children.Add(sarnegPreviewLabel);
                sarnegCard.Child = sarnegStack;
                sec3Stack.Children.Add(sarnegCard);

                sec3Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("F.3. Hasła rozpoznawcze — a) werbalne (wywołanie - odpowiedź - potwierdzenie), b) niewerbalne (wywołanie - odzew):", "F.3. Challenge & Password — a) verbal (challenge - reply - confirmation), b) non-verbal:"),
                    model.ChallengeAndPassword, 48, v => { model.ChallengeAndPassword = v; PersistModelToAppData(model); }));

                sec3Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("G. Leczenie (sposób leczenia, ilość i działanie respów, zasady MEDEVAC / przycisk SOS MGRS):", "G. Medical & Respawn Rules (treatment method, respawn count & rules, SOS MGRS button):"),
                    model.MedevacRules, 50, v => { model.MedevacRules = v; PersistModelToAppData(model); }));

                mainStack.Children.Add(sec3);

                // -----------------------------------------------------------------
                // IV. LOGISTYKA + OPCJONALNY, EDYTOWALNY ZAŁĄCZNIK 1 (PODZIAŁ RÓL DRUŻYNOWYCH)
                // -----------------------------------------------------------------
                var sec4 = CreateSectionCard(BuilderI18n.T("IV. LOGISTYKA ORAZ ZAŁĄCZNIKI (STYL WYPOSAŻENIA, SZPEJLISTA, DOJAZD, OPCJONALNY ZAŁĄCZNIK 1 RÓL)", "IV. LOGISTICS & ANNEXES (LOADOUT, GEAR CHECKLIST, TRAVEL, OPTIONAL EDITABLE ROLES ANNEX 1)"));
                var sec4Stack = (StackPanel)sec4.Child;

                sec4Stack.Children.Add(CreateLabeledTextBox(
                    BuilderI18n.T("A. Styl wyposażenia (na lekko / na 12h w terenie / na nocleg w terenie / inne):", "A. Loadout Style (light / 12h field / overnight / other):"),
                    model.RespawnAndSupply, v => { model.RespawnAndSupply = v; PersistModelToAppData(model); }));

                sec4Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("B. Wyposażenie oddziału (SZPEJLISTA obowiązkowa):", "B. Squad Equipment Checklist (SZPEJLISTA):"),
                    model.SquadGearChecklist, 48, v => { model.SquadGearChecklist = v; PersistModelToAppData(model); }));

                sec4Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("C. Wyposażenie specjalne (np. RPG przydzielone ze sztabu, przedmioty fabularne do przygotowania):", "C. Special Equipment (e.g., HQ-assigned RPG, mission props):"),
                    model.SpecialEquipment, 44, v => { model.SpecialEquipment = v; PersistModelToAppData(model); }));

                sec4Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("D. Dojazd (1. Parking, 2. Odległość z parkingu na miejsce gry, 3. Transport: kto z kim):", "D. Travel & Parking (1. Parking location, 2. Distance to AO, 3. Vehicle allocation):"),
                    model.TravelAndParking, 50, v => { model.TravelAndParking = v; PersistModelToAppData(model); }));

                sec4Stack.Children.Add(CreateLabeledTextBox(
                    BuilderI18n.T("E. Regulamin gry (link):", "E. Game Rules (URL link):"),
                    model.GameRulesLink, v => { model.GameRulesLink = v; PersistModelToAppData(model); }));

                // -----------------------------------------------------------------
                // OPCJONALNE ROZSZERZENIE: ZAŁĄCZNIK 1. PODZIAŁ RÓL DRUŻYNOWYCH (EDYTOWALNY + / -)
                // -----------------------------------------------------------------
                var annex1Card = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(10, 24, 30)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                    BorderThickness = new Thickness(1.4),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 10, 12, 10),
                    Margin = new Thickness(0, 8, 0, 8)
                };
                var annex1Outer = new StackPanel();
                var annex1HdrDock = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };

                var annex1Check = new CheckBox
                {
                    IsChecked = model.EnableRolesAnnex,
                    Content = BuilderI18n.T(
                        "  Załącznik 1 (Opcjonalne rozszerzenie): Dołącz i edytuj tabelę opisu ról drużynowych (Rola | Opis obowiązków | Zajęcie)",
                        "  Annex 1 (Optional Extension): Include & edit Squad Role Definitions Table (Role | Duties Description | Mode)"),
                    Foreground = TacticalConfigPanel.BrushAccent,
                    FontWeight = FontWeights.Bold,
                    FontSize = 11.5,
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = Cursors.Hand
                };
                annex1HdrDock.Children.Add(annex1Check);

                var annex1Btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                DockPanel.SetDock(annex1Btns, Dock.Right);

                var btnAddRoleDefPlus = CreateTacticalPlusMinusButton(true, BuilderI18n.T("Dodaj nową definicję roli (+)", "Add new role definition (+)"), () =>
                {
                    model.EnableRolesAnnex = true;
                    model.CustomRolesAnnex.Add(new OpordRoleDefinitionRow
                    {
                        RoleName = "Nowa Rola",
                        Description = "Opis obowiązków w drużynie...",
                        DutyMode = "Sytuacyjne"
                    });
                    PersistModelToAppData(model);
                    RebuildOpordForm();
                });
                annex1Btns.Children.Add(btnAddRoleDefPlus);

                var btnRemoveRoleDefMinus = CreateTacticalPlusMinusButton(false, BuilderI18n.T("Usuń ostatnią definicję roli (-)", "Remove last role definition (-)"), () =>
                {
                    if (model.CustomRolesAnnex.Count > 0)
                    {
                        model.CustomRolesAnnex.RemoveAt(model.CustomRolesAnnex.Count - 1);
                        PersistModelToAppData(model);
                        RebuildOpordForm();
                    }
                });
                annex1Btns.Children.Add(btnRemoveRoleDefMinus);
                annex1HdrDock.Children.Add(annex1Btns);
                annex1Outer.Children.Add(annex1HdrDock);

                var annex1TableContainer = new StackPanel
                {
                    Visibility = model.EnableRolesAnnex ? Visibility.Visible : Visibility.Collapsed
                };

                annex1Check.Checked += (_, __) =>
                {
                    model.EnableRolesAnnex = true;
                    annex1TableContainer.Visibility = Visibility.Visible;
                    PersistModelToAppData(model);
                };
                annex1Check.Unchecked += (_, __) =>
                {
                    model.EnableRolesAnnex = false;
                    annex1TableContainer.Visibility = Visibility.Collapsed;
                    PersistModelToAppData(model);
                };

                var a1HdrGrid = new Grid { Margin = new Thickness(0, 2, 0, 4) };
                a1HdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
                a1HdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.6, GridUnitType.Star) });
                a1HdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
                a1HdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

                string[] a1Cols = new[]
                {
                    BuilderI18n.T("Rola drużynowa", "Squad Role"),
                    BuilderI18n.T("Opis obowiązków (edytowalny)", "Duties Description (editable)"),
                    BuilderI18n.T("Zajęcie (Tryb)", "Duty Mode"),
                    ""
                };
                for (int c = 0; c < a1Cols.Length; c++)
                {
                    var tb = new TextBlock { Text = a1Cols[c], Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 10, FontWeight = FontWeights.Bold };
                    Grid.SetColumn(tb, c);
                    a1HdrGrid.Children.Add(tb);
                }
                annex1TableContainer.Children.Add(a1HdrGrid);

                for (int i = 0; i < model.CustomRolesAnnex.Count; i++)
                {
                    int capIdx = i;
                    var roleDef = model.CustomRolesAnnex[i];

                    var rGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                    rGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
                    rGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.6, GridUnitType.Star) });
                    rGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
                    rGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

                    var tbRName = CreateCompactGridTextBox(roleDef.RoleName, v => { roleDef.RoleName = v; PersistModelToAppData(model); });
                    Grid.SetColumn(tbRName, 0);
                    rGrid.Children.Add(tbRName);

                    var tbRDesc = CreateCompactGridTextBox(roleDef.Description, v => { roleDef.Description = v; PersistModelToAppData(model); });
                    Grid.SetColumn(tbRDesc, 1);
                    rGrid.Children.Add(tbRDesc);

                    var tbRMode = CreateCompactGridTextBox(roleDef.DutyMode, v => { roleDef.DutyMode = v; PersistModelToAppData(model); });
                    Grid.SetColumn(tbRMode, 2);
                    rGrid.Children.Add(tbRMode);

                    var btnDelRole = CreateTacticalPlusMinusButton(false, BuilderI18n.T("Usuń tę rolę (-)", "Remove this role (-)"), () =>
                    {
                        model.CustomRolesAnnex.RemoveAt(capIdx);
                        PersistModelToAppData(model);
                        RebuildOpordForm();
                    });
                    Grid.SetColumn(btnDelRole, 3);
                    rGrid.Children.Add(btnDelRole);

                    annex1TableContainer.Children.Add(rGrid);
                }

                annex1Outer.Children.Add(annex1TableContainer);
                annex1Card.Child = annex1Outer;
                sec4Stack.Children.Add(annex1Card);

                sec4Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("Załącznik 2. Szczegółowe opracowanie zadań dodatkowych:", "Annex 2. Detailed Mission Tasks Breakdown:"),
                    model.Annex2TasksAnalysis, 50, v => { model.Annex2TasksAnalysis = v; PersistModelToAppData(model); }));

                mainStack.Children.Add(sec4);
            }

            RebuildOpordForm();
            BuilderI18n.LanguageChanged += _ => RebuildOpordForm();
            _cachedOpordOverlay = rootBorder;
            return rootBorder;
        }

        private static void SyncModelRadioChannelsFromFactionGenerator(Window win, string factionName, OpordParagraphModel model, bool forceOverwrite = false)
        {
            var facJson = TryGetFactionJsonObject(win, factionName);
            if (facJson == null) return;

            var vArr = (facJson["VoiceChannels"] ?? facJson["voiceChannels"]) as JsonArray;
            if (vArr != null && vArr.Count > 0)
            {
                for (int i = 0; i < vArr.Count; i++)
                {
                    if (vArr[i] is JsonObject vo)
                    {
                        string role = vo["roleName"]?.ToString() ?? vo["RoleName"]?.ToString() ?? "";
                        string band = vo["bandType"]?.ToString() ?? vo["BandType"]?.ToString() ?? "PMR";
                        string freq = vo["freqMhz"]?.ToString() ?? vo["FreqMhz"]?.ToString() ?? "";
                        string tone = vo["toneOrCode"]?.ToString() ?? vo["ToneOrCode"]?.ToString() ?? "";
                        string formatted = string.Format("{0} [{1}] ({2})", freq, band, tone);

                        if (i == 0 && (forceOverwrite || model.PrimaryRadioFreq.Contains("446.00625")))
                            model.PrimaryRadioFreq = role + ": " + formatted;
                        else if (i == 1 && (forceOverwrite || model.BackupRadioFreq.Contains("446.03125")))
                            model.BackupRadioFreq = role + ": " + formatted;
                        else if (i == 2 && (forceOverwrite || model.HqRadioFreq.Contains("446.09375")))
                            model.HqRadioFreq = role + ": " + formatted;
                    }
                }
            }
        }

        private static string FormatSarnegMatrixPreview(string key10)
        {
            string normalized = SarnegDictionaryService.NormalizePolishChars(key10 ?? "SZTURMOWCA").Trim();
            string padded = normalized.PadRight(10, 'X').Substring(0, 10);
            bool isIso = SarnegDictionaryService.Has10UniqueLetters(padded);

            var sbTop = new StringBuilder("LITERA SARNEG:  ");
            var sbBot = new StringBuilder("CYFRA (0 - 9):  ");
            for (int i = 0; i < 10; i++)
            {
                sbTop.Append("| ").Append(padded[i]).Append(' ');
                sbBot.Append("| ").Append(i).Append(' ');
            }
            sbTop.Append("|   ");
            sbTop.Append(isIso
                ? BuilderI18n.T("✓ [10 UNIKALNYCH LITER — KLUCZ PRAWIDŁOWY]", "✓ [10 UNIQUE LETTERS — VALID KEY]")
                : BuilderI18n.T("⚠️ [UWAGA: POWTÓRZONE LITERY W SŁOWIE!]", "⚠️ [WARNING: DUPLICATE LETTERS IN WORD!]"));
            sbBot.Append('|');
            return sbTop + "\n" + sbBot;
        }

        private static string BuildLiveSignalSummaryText(Window win, string factionName)
        {
            var facJson = TryGetFactionJsonObject(win, factionName);
            string chName = facJson?["ChannelName"]?.ToString() ?? (factionName + "-MESH");
            string region = facJson?["LoraRegion"]?.ToString() ?? "EU_868";
            string preset = facJson?["LoraModemPreset"]?.ToString() ?? "SHORT_FAST";
            string ots = facJson?["OtsHost"]?.ToString() ?? "";
            string phone = facJson?["EmergencyPhone"]?.ToString() ?? "";

            var sb = new StringBuilder();
            sb.AppendLine(BuilderI18n.T(
                "📡 [ZACIĄGNIĘTE Z GENERATORA STRON KANAŁY RADIOWE I MESHTASTIC DLA: " + factionName + "]",
                "📡 [SYNCED RADIO & MESHTASTIC CHANNELS FROM FACTION GENERATOR FOR: " + factionName + "]"));
            sb.AppendLine("• Meshtastic LoRa: Kanał '" + chName + "'  |  Region: " + region + "  |  Preset: " + preset + "  |  Szyfrowanie: AES-256 PSK");
            sb.AppendLine("• Serwer ATAK (OpenTAKServer): " + (string.IsNullOrWhiteSpace(ots) ? "Tryb lokalny / Off-Grid Mesh" : ots));
            sb.AppendLine("• Telefon alarmowy ORGA / MEDEVAC (SOS MGRS): " + (string.IsNullOrWhiteSpace(phone) ? "Brak / 112" : phone));

            var vArr = (facJson?["VoiceChannels"] ?? facJson?["voiceChannels"]) as JsonArray;
            if (vArr != null && vArr.Count > 0)
            {
                sb.AppendLine(BuilderI18n.T("• Wszystkie kanały głosowe zdefiniowane w Generatorze Stron:", "• All voice channels defined in Faction Generator:"));
                foreach (var vo in vArr.OfType<JsonObject>())
                {
                    string r = vo["roleName"]?.ToString() ?? vo["RoleName"]?.ToString() ?? "Kanał";
                    string b = vo["bandType"]?.ToString() ?? vo["BandType"]?.ToString() ?? "PMR";
                    string f = vo["freqMhz"]?.ToString() ?? vo["FreqMhz"]?.ToString() ?? "-";
                    string t = vo["toneOrCode"]?.ToString() ?? vo["ToneOrCode"]?.ToString() ?? "-";
                    sb.AppendLine("   - " + r + " [" + b + "]: " + f + "  (Ton/Kod: " + t + ")");
                }
            }
            return sb.ToString().TrimEnd();
        }

        // =========================================================================
        // 6. SERWER DYSTRYBUCJI OPORD PRZEZ HOTSPOT WI-FI / LAN (:8769)
        // =========================================================================
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

        private static void ShowOpordLanDistributionModal(Window owner, string factionName, OpordParagraphModel model)
        {
            string baseUrl = EnsureOpordLanServerStarted(owner);
            string safeFac = Uri.EscapeDataString(factionName);
            string htmlUrl = baseUrl + "/opord/" + safeFac + ".html";

            var dlg = new Window
            {
                Title = BuilderI18n.T(
                    "📡 Dystrybucja OPORD przez Wi-Fi / LAN (" + factionName + ")",
                    "📡 Wi-Fi / LAN OPORD Distribution (" + factionName + ")"),
                Width = 480,
                Height = 560,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                Background = TacticalConfigPanel.BrushCardBg,
                Foreground = Brushes.White,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow
            };

            var root = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };
            root.Children.Add(new TextBlock
            {
                Text = BuilderI18n.T(
                    "📋 UDOSTĘPNIANIE ROZKAZU OPORD (" + factionName + ") PO WI-FI / LAN",
                    "📋 SHARE OPORD (" + factionName + ") OVER WI-FI / LAN"),
                Foreground = TacticalConfigPanel.BrushAccent,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            root.Children.Add(new TextBlock
            {
                Text = BuilderI18n.T(
                    "Zeskanuj poniższy kod QR w aplikacji Field TAK Hub (lub aparatem telefonu), aby natychmiast otworzyć i zaimportować sam rozkaz OPORD przez Wi-Fi / LAN:",
                    "Scan this QR code in Field TAK Hub (or phone camera) to view and import this OPORD over Wi-Fi / LAN:"),
                Foreground = TacticalConfigPanel.BrushMutedText,
                FontSize = 10.5,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 6, 0, 10)
            });

            var qrFrame = new Border
            {
                Width = 270,
                Height = 270,
                Background = Brushes.White,
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(3),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };
            var qrImg = new Image { Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(qrImg, BitmapScalingMode.NearestNeighbor);
            qrFrame.Child = qrImg;
            root.Children.Add(qrFrame);

            var urlBox = new TextBox
            {
                Text = htmlUrl,
                IsReadOnly = true,
                Height = 26,
                FontSize = 10.5,
                FontFamily = new FontFamily("Consolas"),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = TacticalConfigPanel.BrushInputBg,
                Foreground = TacticalConfigPanel.BrushAccent,
                BorderBrush = TacticalConfigPanel.BrushInputBorder,
                Margin = new Thickness(0, 0, 0, 8)
            };
            root.Children.Add(urlBox);

            _ = Task.Run(async () =>
            {
                try
                {
                    string api = "https://api.qrserver.com/v1/create-qr-code/?size=400x400&margin=10&data=" + Uri.EscapeDataString(htmlUrl);
                    byte[] bytes = await GeoportalLidarEngine.FastHttp.GetByteArrayAsync(api);
                    if (GeoportalLidarEngine.IsValidImageBytes(bytes) && Application.Current != null)
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            var bmp = new BitmapImage();
                            using var ms = new MemoryStream(bytes);
                            bmp.BeginInit();
                            bmp.CacheOption = BitmapCacheOption.OnLoad;
                            bmp.StreamSource = ms;
                            bmp.EndInit();
                            bmp.Freeze();
                            qrImg.Source = bmp;
                        });
                    }
                }
                catch { }
            });

            var btns = new UniformGrid { Columns = 2 };
            var btnCopyLink = new Button
            {
                Content = BuilderI18n.T("📋 Kopiuj link LAN OPORD", "📋 Copy LAN OPORD Link"),
                Height = 32,
                Margin = new Thickness(0, 0, 5, 0),
                Background = TacticalConfigPanel.BrushButtonAccent,
                Foreground = TacticalConfigPanel.BrushDarkFg,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            btnCopyLink.Click += (_, __) => Clipboard.SetText(htmlUrl);

            var btnClose = new Button
            {
                Content = BuilderI18n.T("Zamknij", "Close"),
                Height = 32,
                Margin = new Thickness(5, 0, 0, 0),
                Background = TacticalConfigPanel.BrushInputBg,
                Foreground = Brushes.White,
                Cursor = Cursors.Hand
            };
            btnClose.Click += (_, __) => dlg.Close();

            btns.Children.Add(btnCopyLink);
            btns.Children.Add(btnClose);
            root.Children.Add(btns);

            dlg.Content = root;
            dlg.ShowDialog();
        }

        // =========================================================================
        // 7. GENERATOR DOKUMENTU A4 HTML / PDF ORAZ MARKDOWN NA DISCORDA
        // =========================================================================
        public static string GeneratePrintableOpordHtml(Window win, string factionName, OpordParagraphModel m)
        {
            EnsureDefaultCollectionsInitialized(m);
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
            radioRowsHtml.AppendLine("<tr><td><b>Kanał główny</b></td><td>PMR/VHF</td><td colspan='2'><b>" + E(m.PrimaryRadioFreq) + "</b></td></tr>");
            radioRowsHtml.AppendLine("<tr><td><b>Kanał zapasowy</b></td><td>PMR/VHF</td><td colspan='2'><b>" + E(m.BackupRadioFreq) + "</b></td></tr>");
            radioRowsHtml.AppendLine("<tr><td><b>Kanał HQ (Sztab)</b></td><td>PMR/VHF</td><td colspan='2'><b>" + E(m.HqRadioFreq) + "</b></td></tr>");

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

            string sarnegClean = SarnegDictionaryService.NormalizePolishChars(m.SarnegKeyWord10 ?? "SZTURMOWCA").Trim().PadRight(10, 'X').Substring(0, 10);
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
            html.AppendLine("<div class='no-print'><button class='btn-print' onclick='window.print()'>🖨️ Drukuj OPORD / Zapisz jako PDF (A4)</button></div>");
            html.AppendLine("<div class='classification'>" + E(m.Classification) + "</div>");
            html.AppendLine("<div class='header-box'><div>");
            html.AppendLine("<h1>OPORD " + E(eventName) + " — " + E(m.OperationNumber) + "</h1>");
            html.AppendLine("<div><b>Sztab (HQ):</b> " + E(m.IssuingHq) + " &nbsp;|&nbsp; <b>DTG:</b> " + E(m.DtgIssued) + "</div>");
            html.AppendLine("<div><b>Rejon AO:</b> " + E(m.LocationName) + " (" + E(m.LocationLat) + "°N, " + E(m.LocationLon) + "°E) &nbsp;|&nbsp; <b>Mapy:</b> " + E(m.MapReferences) + "</div>");
            html.AppendLine("</div><div class='faction-badge'>" + E(factionName) + "</div></div>");

            if (m.Days.Count > 0)
            {
                html.AppendLine("<table><thead><tr><th style='width:22%'>📅 Dzień operacji</th><th style='width:38%'>Pogoda METOC / Słońce / Księżyc</th><th>Harmonogram i zadania dnia</th></tr></thead><tbody>");
                foreach (var d in m.Days)
                {
                    html.AppendLine("<tr><td><b>" + E(d.DayTitle) + "</b><br/>" + E(d.DateIso) + "</td>" +
                                    "<td>" + E(d.MetocCondition) + "<br/>Temp: " + E(d.MetocTemperature) + "<br/>" + E(d.MetocSunTimes) + "<br/>" + E(d.MetocMoonPhase) + "</td>" +
                                    "<td>" + E(d.DaySpecificTasks) + "</td></tr>");
                }
                html.AppendLine("</tbody></table>");
            }

            html.AppendLine("<h2>1. SYTUACJA</h2>");
            html.AppendLine("<div class='sub-p'><b>A. Obszar zainteresowania (Mapa, POI, KML/GPX):</b><br/>" + E(m.AreaOfInterestPoiDesc) + "</div>");
            html.AppendLine("<div class='sub-p'><b>B. Obszar działań (1. Teren i 2. Pogoda METOC):</b><br/>" + E(m.TerrainAndWeather) + "</div>");
            html.AppendLine("<div class='sub-p'><b>C. Siły wroga (Skład, lokalizacje, COA):</b><br/>" + E(m.EnemyForces) + "</div>");
            html.AppendLine("<div class='sub-p'><b>D. Siły przyjazne (Misja HQ i sąsiadów):</b><br/>" + E(m.FriendlyForces) + "</div>");

            html.AppendLine("<h2>II. MISJA</h2>");
            html.AppendLine("<div class='sub-p'><b>A. Opis fabularny:</b> " + E(m.MissionLoreDesc) + "</div>");
            html.AppendLine("<div class='mission-box'>B. Główne zadanie: " + E(string.IsNullOrWhiteSpace(m.MissionFullStatement) ? Compose5WMissionText(m) : m.MissionFullStatement) + "</div>");
            html.AppendLine("<div class='sub-p'><b>C. Dodatkowe zadania:</b><br/>" + E(m.AdditionalTasksSummary) + "</div>");

            html.AppendLine("<h2>III. WYKONANIE</h2>");
            html.AppendLine("<div class='sub-p'><b>A. Zamiar dowódcy:</b><br/>" + E(m.CommandersIntent) + "</div>");
            html.AppendLine("<div class='sub-p'><b>B. Priorytety i styl działania:</b><br/>" + E(m.ConceptOfOperations) + "</div>");
            html.AppendLine("<div class='sub-p'><b>C. Łańcuch dowodzenia:</b> <b>" + E(m.SuccessionOfCommand) + "</b></div>");

            if (m.EnableSquadRosterTable && m.SquadRoster.Count > 0)
            {
                html.AppendLine("<div class='sub-p'><b>D. Skład oddziału i podział ról:</b></div>");
                html.AppendLine("<table><thead><tr><th>Ksywa (Operator / Zespół)</th><th>Rola 1</th><th>Rola 2 (opcjonalnie)</th><th>Typ broni / Sprzęt</th></tr></thead><tbody>");
                foreach (var r in m.SquadRoster)
                {
                    html.AppendLine("<tr><td><b>" + E(r.OperatorOrTeam) + "</b></td><td>" + E(r.PrimaryRole) + "</td><td>" + E(r.SecondaryRole) + "</td><td>" + E(r.WeaponAndGear) + "</td></tr>");
                }
                html.AppendLine("</tbody></table>");
            }

            html.AppendLine("<div class='sub-p'><b>E. Tabela łączności (LoRa Meshtastic: " + E(chName) + " [" + E(region) + "/" + E(preset) + "] | OTS: " + E(ots) + " | SOS ORGA: " + E(phone) + "):</b></div>");
            html.AppendLine("<table><thead><tr><th>Rola / Kanał</th><th>Pasmo</th><th>Częstotliwość / Kanał</th><th>Ton CTCSS / Kod DMR</th></tr></thead><tbody>" + radioRowsHtml + "</tbody></table>");

            html.AppendLine("<div class='sub-p'><b>F. Sygnały, słowa kodowe i rozpoznawcze:</b><br/><b>1. Kryptonimy celów:</b> " + E(m.TargetCodeNames) + "</div>");
            html.AppendLine("<table><thead><tr><th colspan='10' style='text-align:center;'>2. Tabela krypto SARNEG główna (Słowo-klucz: " + E(sarnegClean) + ")</th></tr><tr>" + sarnegLettersRow + "</tr></thead><tbody><tr>" + sarnegDigitsRow + "</tr></tbody></table>");
            html.AppendLine("<div class='sub-p'><b>3. Hasła rozpoznawcze (werbalne i niewerbalne):</b><br/>" + E(m.ChallengeAndPassword) + "</div>");
            html.AppendLine("<div class='sub-p'><b>G. Leczenie i zasady Respawn:</b><br/>" + E(m.MedevacRules) + "</div>");

            html.AppendLine("<h2>IV. LOGISTYKA</h2>");
            html.AppendLine("<div class='sub-p'><b>A. Styl wyposażenia:</b> " + E(m.RespawnAndSupply) + "</div>");
            html.AppendLine("<div class='sub-p'><b>B. Wyposażenie oddziału (SZPEJLISTA):</b><br/>" + E(m.SquadGearChecklist) + "</div>");
            html.AppendLine("<div class='sub-p'><b>C. Wyposażenie specjalne:</b><br/>" + E(m.SpecialEquipment) + "</div>");
            html.AppendLine("<div class='sub-p'><b>D. Dojazd (Parking, odległość, transport):</b><br/>" + E(m.TravelAndParking) + "</div>");
            html.AppendLine("<div class='sub-p'><b>E. Regulamin gry:</b> " + E(m.GameRulesLink) + "</div>");

            if (m.EnableRolesAnnex && m.CustomRolesAnnex.Count > 0)
            {
                html.AppendLine("<h2>ZAŁĄCZNIK 1. PODZIAŁ RÓL DRUŻYNOWYCH</h2>");
                html.AppendLine("<table><thead><tr><th style='width:22%'>Rola</th><th>Opis obowiązków</th><th style='width:15%'>Zajęcie</th></tr></thead><tbody>");
                foreach (var roleDef in m.CustomRolesAnnex)
                {
                    html.AppendLine("<tr><td><b>" + E(roleDef.RoleName) + "</b></td><td>" + E(roleDef.Description) + "</td><td>" + E(roleDef.DutyMode) + "</td></tr>");
                }
                html.AppendLine("</tbody></table>");
            }

            html.AppendLine("<h2>ZAŁĄCZNIK 2. OPRACOWANIE ZADAŃ</h2>");
            html.AppendLine("<div class='sub-p'>" + E(m.Annex2TasksAnalysis) + "</div>");
            html.AppendLine("<div class='classification' style='margin-top:12px;'>" + E(m.Classification) + " — Field TAK Hub v3.1</div>");
            html.AppendLine("</body></html>");
            return html.ToString();
        }

        public static string GenerateDiscordMarkdownOpord(Window win, string factionName, OpordParagraphModel m)
        {
            EnsureDefaultCollectionsInitialized(m);
            string eventName = win != null ? TacticalConfigPanel.GetEventNameFromWindow(win) : "OPERACJA";
            var sb = new StringBuilder();
            sb.AppendLine("# 📋 " + m.OperationNumber + " — " + eventName + " [" + factionName + "]");
            sb.AppendLine("`" + m.Classification + "` | **DTG:** `" + m.DtgIssued + "` | **HQ:** `" + m.IssuingHq + "`");
            sb.AppendLine("---");
            sb.AppendLine("## 1️⃣ SYTUACJA (SITUATION & METOC)");
            sb.AppendLine("- **A. Obszar zainteresowania (POI / KML / GPX):** " + m.AreaOfInterestPoiDesc);
            sb.AppendLine("- **B. Obszar działań (Teren i Pogoda METOC):**\n" + m.TerrainAndWeather);
            sb.AppendLine("- **C. Siły wroga (COA):** " + m.EnemyForces);
            sb.AppendLine("- **D. Siły przyjazne:** " + m.FriendlyForces);
            sb.AppendLine();
            sb.AppendLine("## 2️⃣ MISJA (MISSION)");
            sb.AppendLine("- **A. Opis fabularny:** " + m.MissionLoreDesc);
            sb.AppendLine("> 🎯 **B. Główne zadanie:** **" + Compose5WMissionText(m) + "**");
            sb.AppendLine("- **C. Dodatkowe zadania:**\n" + m.AdditionalTasksSummary);
            sb.AppendLine();
            sb.AppendLine("## 3️⃣ WYKONANIE (EXECUTION)");
            sb.AppendLine("- **A. Zamiar dowódcy:** " + m.CommandersIntent);
            sb.AppendLine("- **B. Priorytety i styl działania:** " + m.ConceptOfOperations);
            sb.AppendLine("- **C. Łańcuch dowodzenia:** `" + m.SuccessionOfCommand + "`");
            if (m.EnableSquadRosterTable && m.SquadRoster.Count > 0)
            {
                sb.AppendLine("**D. Skład oddziału i podział ról:**");
                sb.AppendLine("```text");
                foreach (var r in m.SquadRoster)
                {
                    sb.AppendLine(string.Format("{0,-12} | Rola 1: {1,-24} | Rola 2: {2,-20} | {3}", r.OperatorOrTeam, r.PrimaryRole, r.SecondaryRole, r.WeaponAndGear));
                }
                sb.AppendLine("```");
            }
            sb.AppendLine("**E. Tabela łączności:**");
            sb.AppendLine("```text");
            sb.AppendLine("• Kanał główny:   " + m.PrimaryRadioFreq);
            sb.AppendLine("• Kanał zapasowy: " + m.BackupRadioFreq);
            sb.AppendLine("• Kanał HQ:       " + m.HqRadioFreq);
            sb.AppendLine(BuildLiveSignalSummaryText(win, factionName));
            sb.AppendLine("```");
            sb.AppendLine("**F. Sygnały, SARNEG i Hasła rozpoznawcze:**");
            sb.AppendLine("- **Kryptonimy celów:** " + m.TargetCodeNames);
            sb.AppendLine("```text\n" + FormatSarnegMatrixPreview(m.SarnegKeyWord10) + "\n```");
            sb.AppendLine("- **Hasła rozpoznawcze:**\n" + m.ChallengeAndPassword);
            sb.AppendLine("- **G. Leczenie:** " + m.MedevacRules);
            sb.AppendLine();
            sb.AppendLine("## 4️⃣ LOGISTYKA (LOGISTICS)");
            sb.AppendLine("- **A. Styl wyposażenia:** " + m.RespawnAndSupply);
            sb.AppendLine("- **B. SZPEJLISTA:** " + m.SquadGearChecklist);
            sb.AppendLine("- **C. Wyposażenie specjalne:** " + m.SpecialEquipment);
            sb.AppendLine("- **D. Dojazd i parking:** " + m.TravelAndParking);
            sb.AppendLine("- **E. Regulamin gry:** " + m.GameRulesLink);
            if (m.EnableRolesAnnex && m.CustomRolesAnnex.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## 📎 ZAŁĄCZNIK 1. PODZIAŁ RÓL DRUŻYNOWYCH");
                foreach (var rd in m.CustomRolesAnnex)
                {
                    sb.AppendLine("- **" + rd.RoleName + "** (`" + rd.DutyMode + "`): " + rd.Description);
                }
            }
            return sb.ToString();
        }

        // =========================================================================
        // 8. WEKTOROWE PRZYCISKI ZIELONY "+" I CZERWONY "-" (CANVAS — 100% WIDOCZNOŚCI)
        // =========================================================================
        private static Button CreateTacticalPlusMinusButton(bool isPlusGreen, string toolTipText, Action onClick)
        {
            Color fillCol = isPlusGreen ? Color.FromRgb(18, 68, 42) : Color.FromRgb(78, 22, 22);
            Color strokeCol = isPlusGreen ? Color.FromRgb(46, 204, 113) : Color.FromRgb(255, 82, 82);
            var symbolBrush = new SolidColorBrush(Colors.White);

            var canvas = new Canvas
            {
                Width = 14,
                Height = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Pozioma kreska (zarówno dla "+" jak i "-")
            var hLine = new System.Windows.Shapes.Line
            {
                X1 = 2,
                Y1 = 7,
                X2 = 12,
                Y2 = 7,
                Stroke = symbolBrush,
                StrokeThickness = 2.6,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            canvas.Children.Add(hLine);

            // Pionowa kreska (tylko dla zielonego "+")
            if (isPlusGreen)
            {
                var vLine = new System.Windows.Shapes.Line
                {
                    X1 = 7,
                    Y1 = 2,
                    X2 = 7,
                    Y2 = 12,
                    Stroke = symbolBrush,
                    StrokeThickness = 2.6,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                };
                canvas.Children.Add(vLine);
            }

            var btn = new Button
            {
                Content = canvas,
                ToolTip = toolTipText,
                Width = 28,
                Height = 28,
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(3, 0, 3, 0),
                Background = new SolidColorBrush(fillCol),
                BorderBrush = new SolidColorBrush(strokeCol),
                BorderThickness = new Thickness(1.8),
                Cursor = Cursors.Hand
            };
            btn.Click += (_, __) => onClick?.Invoke();
            return btn;
        }

        private static TextBox CreateCompactGridTextBox(string initialValue, Action<string> onChanged)
        {
            var tb = new TextBox
            {
                Text = initialValue ?? "",
                Height = 28,
                Margin = new Thickness(0, 0, 5, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = TacticalConfigPanel.BrushInputBg,
                Foreground = Brushes.White,
                CaretBrush = TacticalConfigPanel.BrushAccent,
                BorderBrush = TacticalConfigPanel.BrushInputBorder,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 0, 6, 0),
                FontSize = 11
            };
            tb.TextChanged += (_, __) => onChanged?.Invoke(tb.Text);
            return tb;
        }

        private static ComboBox CreateEditableRoleComboBox(Window win, string[] options, string initialValue, Action<string> onChanged)
        {
            var cb = new ComboBox
            {
                Height = 28,
                Margin = new Thickness(0, 0, 5, 0),
                FontSize = 11,
                IsEditable = false,
                Tag = "INJECTED"
            };
            TacticalConfigPanel.ApplyUnifiedComboBoxTheme(cb, win);
            foreach (string opt in options)
                cb.Items.Add(opt);
            if (!string.IsNullOrWhiteSpace(initialValue) && !cb.Items.Contains(initialValue))
                cb.Items.Insert(0, initialValue);
            cb.SelectedItem = string.IsNullOrWhiteSpace(initialValue) ? options.FirstOrDefault() : initialValue;
            cb.SelectionChanged += (_, __) =>
            {
                if (cb.SelectedItem is string s) onChanged?.Invoke(s);
            };
            return cb;
        }

        private static Border CreateSectionCard(string title)
        {
            var border = new Border
            {
                Background = TacticalConfigPanel.BrushCardBg,
                BorderBrush = TacticalConfigPanel.BrushCardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 12),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = TacticalConfigPanel.BrushAccent,
                FontWeight = FontWeights.Bold,
                FontSize = 12.5,
                Margin = new Thickness(0, 0, 0, 8)
            });
            border.Child = stack;
            return border;
        }

        private static StackPanel CreateLabeledTextBox(string label, string initialValue, Action<string> onChanged)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 6, 4) };
            sp.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = TacticalConfigPanel.BrushMutedText,
                FontSize = 10,
                Margin = new Thickness(0, 0, 0, 3)
            });
            var tb = new TextBox
            {
                Text = initialValue ?? "",
                Height = 28,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = TacticalConfigPanel.BrushInputBg,
                Foreground = Brushes.White,
                CaretBrush = TacticalConfigPanel.BrushAccent,
                BorderBrush = TacticalConfigPanel.BrushInputBorder,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 0, 6, 0),
                FontSize = 11
            };
            tb.TextChanged += (_, __) => onChanged?.Invoke(tb.Text);
            sp.Children.Add(tb);
            return sp;
        }

        private static StackPanel CreateLabeledMultilineBox(string label, string initialValue, double height, Action<string> onChanged)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 7) };
            sp.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = TacticalConfigPanel.BrushMutedText,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 3)
            });
            var tb = new TextBox
            {
                Text = initialValue ?? "",
                Height = height,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = TacticalConfigPanel.BrushInputBg,
                Foreground = Brushes.White,
                CaretBrush = TacticalConfigPanel.BrushAccent,
                BorderBrush = TacticalConfigPanel.BrushInputBorder,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 5, 8, 5),
                FontSize = 11
            };
            tb.TextChanged += (_, __) => onChanged?.Invoke(tb.Text);
            sp.Children.Add(tb);
            return sp;
        }

        private static T FindFirstDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match) return match;
                var nested = FindFirstDescendant<T>(child);
                if (nested != null) return nested;
            }
            return null;
        }
    }
}