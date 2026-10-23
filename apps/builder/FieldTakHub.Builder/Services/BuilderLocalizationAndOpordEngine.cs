#pragma warning disable CS0414, CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace FieldTakHub.Builder.Services
{
    // =========================================================================
    // 1. DWUJĘZYCZNY SILNIK LOKALIZACJI INTERFEJSU (PL <-> EN)
    // =========================================================================
    internal static class BuilderI18n
    {
        public static string CurrentLang { get; private set; } = LoadSavedLanguage();
        public static event Action<string> LanguageChanged;

        private static readonly Dictionary<string, string> PlToEn = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["📦 Konfigurator Paczek (.ftak)"] = "📦 Faction & Package Builder (.ftak)",
            ["📋 Generator Rozkazu (OPORD)"] = "📋 NATO OPORD Builder",
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
    // 2. SERWIS POGODOWO-ASTRONOMICZNY METOC (POGODA, SŁOŃCE, FAZA KSIĘŻYCA)
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
        private static readonly HttpClient Http = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "FieldTakHub-OPORD-METOC/2.5");
            return c;
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

            // 1. Wylicz dokładną Fazę Księżyca i % oświetlenia tarczy (algorytm synodyczny)
            var moonInfo = CalculateMoonPhaseAndIllumination(targetDate);

            // 2. Wylicz rezerwowe godziny wschodu/zachodu słońca oraz świtu/zmierzchu cywilnego (BMNT / EENT)
            var fallbackSun = CalculateAstronomicalSunTimes(targetDate, lat, lon);

            string condText = BuilderI18n.T("Umiarkowane zachmurzenie (dane astronomiczne obliczone lokalnie)", "Moderate cloud cover (astronomical data calculated locally)");
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

                    condText = DecodeWmoWeatherCode(wmoCode) + string.Format(CultureInfo.InvariantCulture, " (Zachmurzenie / Cloud cover: {0}%, Ciśnienie / QNH: {1:0} hPa)", avgClouds, avgPressure);
                    tempText = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:0.0}°C .. {1:0.0}°C  (Odczuwalna / Feels like: {2:0.0}°C .. {3:0.0}°C)",
                        tMin, tMax, appMin, appMax);

                    double windMs = windMaxKmh / 3.6;
                    double gustsMs = gustsMaxKmh / 3.6;
                    string dirCardinal = DegreesToCardinal(windDeg);
                    windText = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:0.0} km/h ({1:0.0} m/s)  |  Porywy / Gusts: {2:0.0} km/h ({3:0.0} m/s)  |  Kierunek / Dir: {4}° ({5})",
                        windMaxKmh, windMs, gustsMaxKmh, gustsMs, windDeg, dirCardinal);

                    humPrecipText = string.Format(
                        CultureInfo.InvariantCulture,
                        "Wilgotność / Humidity: ~{0}%  |  Opady / Precip: {1:0.0} mm (Szansa / Prob: {2}%)",
                        avgHumidity, precipMm, precipProb);
                }
            }
            catch
            {
                // W przypadku braku internetu lub daty spoza zasięgu API zachowaj obliczenia astronomiczne Słońca i Księżyca
            }

            string sunLine = string.Format(
                "BMNT (Świt): {0}  |  ☀️ Wschód (SR): {1}  |  🌇 Zachód (SS): {2}  |  EENT (Zmierzch NVG): {3}",
                bmntStr, srStr, ssStr, eentStr);

            string locDisplay = string.IsNullOrWhiteSpace(locationLabel)
                ? string.Format(CultureInfo.InvariantCulture, "{0:0.0000}°N, {1:0.0000}°E", lat, lon)
                : string.Format(CultureInfo.InvariantCulture, "{0} ({1:0.0000}°N, {2:0.0000}°E)", locationLabel, lat, lon);

            var sb = new StringBuilder();
            sb.AppendLine(BuilderI18n.T(
                "🌦️ [RAPORT METOC — POGODA, WIATR, SŁOŃCE I FAZA KSIĘŻYCA NA DZIEŃ: " + cleanDate + "]",
                "🌦️ [METOC REPORT — WEATHER, WIND, SUN & MOON PHASE FOR DATE: " + cleanDate + "]"));
            sb.AppendLine("• Rejon AO / Lokalizacja: " + locDisplay);
            sb.AppendLine("• Warunki ogólne: " + condText);
            sb.AppendLine("• Temperatura: " + tempText);
            sb.AppendLine("• Wiatr (10m): " + windText);
            sb.AppendLine("• Wilgotność i opady: " + humPrecipText);
            sb.AppendLine("• Słońce i światło (BMNT / SR / SS / EENT): " + sunLine);
            sb.AppendLine("• Księżyc i Noktowizja (NVG): " + moonInfo.FullText);

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
            // Referencyjny Nów Księżyca: 2000-01-06 18:14 UTC, okres synodyczny: 29.53058867 doby
            DateTime knownNewMoon = new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);
            DateTime sampleUtc = new DateTime(date.Year, date.Month, date.Day, 22, 0, 0, DateTimeKind.Utc);
            double synodicMonth = 29.53058867;
            double daysSince = (sampleUtc - knownNewMoon).TotalDays;
            double cyclePos = ((daysSince % synodicMonth) + synodicMonth) % synodicMonth;
            double phaseFraction = cyclePos / synodicMonth; // 0.0 = Nów, 0.5 = Pełnia

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
                ? BuilderI18n.T("Wysoka jasność nocna (dobra widoczność pasywna NVG)", "High ambient light (good passive NVG visibility)")
                : (illumPct >= 20
                    ? BuilderI18n.T("Umiarkowana jasność nocna (zalecana ostrożność w lesie)", "Moderate ambient light (reduced canopy visibility)")
                    : BuilderI18n.T("Ciemna noc (w gęstym lesie wymagane doświetlenie IR dla NVG)", "Dark night (active IR illumination required under canopy)"));

            string phaseLabel = BuilderI18n.T(phasePl, phaseEn);
            string full = string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1}  |  Iluminacja tarczy: {2}%  |  {3}",
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
                0 => BuilderI18n.T("☀️ Bezchmurnie / Czyste niebo", "☀️ Clear sky"),
                1 => BuilderI18n.T("🌤️ Przeważnie słonecznie", "🌤️ Mainly clear"),
                2 => BuilderI18n.T("⛅ Częściowe zachmurzenie", "⛅ Partly cloudy"),
                3 => BuilderI18n.T("☁️ Pochmurno (pełne zachmurzenie)", "☁️ Overcast"),
                45 or 48 => BuilderI18n.T("🌫️ Mgła / Ograniczona widoczność", "🌫️ Fog / Reduced visibility"),
                51 or 53 or 55 => BuilderI18n.T("🌦️ Mżawka", "🌦️ Drizzle"),
                61 or 63 or 65 => BuilderI18n.T("🌧️ Opady deszczu", "🌧️ Rain"),
                71 or 73 or 75 or 77 => BuilderI18n.T("❄️ Opady śniegu", "❄️ Snowfall"),
                80 or 81 or 82 => BuilderI18n.T("🌧️ Przelotne opady deszczu", "🌧️ Rain showers"),
                95 or 96 or 99 => BuilderI18n.T("⛈️ Burza z piorunami", "⛈️ Thunderstorm"),
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
    // 3. MODEL DANYCH I GENERATOR ROZKAZU BOJOWEGO NATO (5-PARAGRAPH OPORD)
    // =========================================================================
    internal sealed class OpordParagraphModel
    {
        public string TargetFaction { get; set; } = "BLUFOR";
        public string Classification { get; set; } = "JAWNE / UNCLASSIFIED — MILSIM OPORD";
        public string OperationNumber { get; set; } = "OPORD 01-2026";
        public string OperationDateIso { get; set; } = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        public string LocationName { get; set; } = "Rejon Operacji (AO)";
        public string LocationLat { get; set; } = "49.8494";
        public string LocationLon { get; set; } = "18.8127";
        public string DtgIssued { get; set; } = DateTime.Now.ToString("ddHHmm'Z' MMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();
        public string IssuingHq { get; set; } = "TOC / SZTAB GŁÓWNY";
        public string MapReferences { get; set; } = "Mapa Taktyczna MGRS 1:5000 (W załączeniu .FTAK / .KMZ)";

        // Dane pogodowe METOC
        public string MetocCondition { get; set; } = "Brak pobranego raportu (kliknij '🌦️ Pobierz prognozę METOC' w sekcji 1c)";
        public string MetocTemperature { get; set; } = "-";
        public string MetocWind { get; set; } = "-";
        public string MetocHumidityPrecip { get; set; } = "-";
        public string MetocSunTimes { get; set; } = "-";
        public string MetocMoonPhase { get; set; } = "-";

        // 1. SITUATION
        public string EnemyForces { get; set; } = "Siły OPFOR w sile plutonu piechoty lekkiej prowadzą działania obronno-dywersyjne w rejonie AO. Wyposażenie: łączność radiowa, dymy, repliki szturmowe i wsparcia.";
        public string FriendlyForces { get; set; } = "Siły własne (BLUFOR) w składzie 2 sekcji szturmowych (ALFA, BRAVO) oraz elementu rozpoznawczego (RECON). Sztab (HQ) rozwinięty w pobliżu punktu startowego.";
        public string TerrainAndWeather { get; set; } = "Teren leśno-pagórkowaty z gęstym podszyciem i siecią dróg leśnych. Widoczność w koronach drzew ograniczona, miejscami skarpy oraz cieki wodne.";

        // 2. MISSION (5W)
        public string MissionWho { get; set; } = "Zgrupowanie zadaniowe BLUFOR";
        public string MissionWhat { get; set; } = "opanuje i utrzyma kluczowy węzeł terenowy OBJ ALPHA";
        public string MissionWhen { get; set; } = "w dniu operacji od godz. H (09:00) do godz. 15:00";
        public string MissionWhere { get; set; } = "w centralnym sektorze AO (siatka MGRS z mapy .KMZ)";
        public string MissionWhy { get; set; } = "w celu uniemożliwienia siłom przeciwnika kontroli nad szlakiem zaopatrzeniowym.";
        public string MissionFullStatement { get; set; } = "";

        // 3. EXECUTION
        public string CommandersIntent { get; set; } = "Kluczem do powodzenia jest skryte podejście, utrzymanie łączności w sieci Meshtastic/ATAK oraz jednoczesne uderzenie z dwóch kierunków. Stan końcowy: obiekt zabezpieczony, siły własne gotowe do obrony okrężnej.";
        public string ConceptOfOperations { get; set; } = "FAZA I (Infiltracja): Wyjście z FOB i zajęcie punktów wyjściowych.\nFAZA II (Szturm): Jednoczesne wejście w strefę obiektu po sygnale sztabu.\nFAZA III (Konsolidacja): Obrona okrężna i ewakuacja rannych.";
        public string TasksToSubordinateUnits { get; set; } = "• Sekcja ALFA: Główne uderzenie od kierunku północno-zachodniego.\n• Sekcja BRAVO: Ubezpieczenie skrzydła i blokada dróg podejścia od południa.\n• Sekcja RECON: Obserwacja przedpola i naprowadzanie w ATAK-CIV.";
        public string CoordinatingAndRoe { get; set; } = "• Obowiązkowa ochrona wzroku przez cały czas trwania gry.\n• Identyfikacja swój-obcy przed oddaniem strzału (sprawdź pozycję PLI w ATAK).\n• Zakaz używania pirotechniki poza wyznaczonymi strefami.";

        // 4. SUSTAINMENT
        public string MedevacRules { get; set; } = "Po trafieniu gracz oznacza się czerwoną szmatką i wzywa medyka. W przypadku realnego urazu natychmiast użyć czerwonego przycisku 🚨 SOS MGRS w aplikacji Field TAK Hub.";
        public string RespawnAndSupply { get; set; } = "Punkt Respawn / FOB zgodny z markerem na mapie .GPX / .KMZ. Wyjście z Respawnu co pełne 20 minut lub w grupie min. 3 operatorów.";

        // 5. COMMAND & SIGNAL
        public string SuccessionOfCommand { get; set; } = "1. Dowódca Operacji (HQ-1)  ->  2. Dowódca Sekcji ALFA (ALFA-1)  ->  3. Dowódca Sekcji BRAVO (BRAVO-1)";
        public string ChallengeAndPassword { get; set; } = "Hasło (Challenge): GROM   /   Odzew (Password): BŁYSK   |   Kombinacja liczbowa: 7";
    }

    internal static class OpordBuilderManager
    {
        private static readonly Dictionary<string, OpordParagraphModel> ModelsByFaction = new Dictionary<string, OpordParagraphModel>(StringComparer.OrdinalIgnoreCase);
        private static string _activeOpordFaction = "BLUFOR";
        private static Border _cachedOpordOverlay;

        public static OpordParagraphModel GetOrCreateModel(string factionName)
        {
            string key = string.IsNullOrWhiteSpace(factionName) ? "BLUFOR" : factionName.Trim();
            if (!ModelsByFaction.TryGetValue(key, out var model))
            {
                model = TryLoadSavedOpordFromDisk(key) ?? new OpordParagraphModel
                {
                    TargetFaction = key,
                    MissionWho = "Zgrupowanie " + key
                };
                if (string.IsNullOrWhiteSpace(model.MissionFullStatement))
                {
                    model.MissionFullStatement = Compose5WMissionText(model);
                }
                ModelsByFaction[key] = model;
            }
            return model;
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

        public static Border CreateOrGetOpordOverlayView(Window win)
        {
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
                string eventName = TacticalConfigPanel.GetEventNameFromWindow(win);

                // PASEK NAGŁÓWKOWY GENERATORA OPORD
                var topCard = new Border
                {
                    Background = TacticalConfigPanel.BrushCardBg,
                    BorderBrush = TacticalConfigPanel.BrushAccent,
                    BorderThickness = new Thickness(2),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(16, 12, 16, 12),
                    Margin = new Thickness(0, 0, 0, 12)
                };

                var topStack = new StackPanel();
                topStack.Children.Add(new TextBlock
                {
                    Text = BuilderI18n.T(
                        "📋 GENERATOR ROZKAZU BOJOWEGO NATO (5-PARAGRAPH OPORD + METOC) — " + eventName,
                        "📋 NATO 5-PARAGRAPH OPORD & METOC BUILDER — " + eventName),
                    Foreground = TacticalConfigPanel.BrushAccent,
                    FontWeight = FontWeights.Bold,
                    FontSize = 15,
                    Margin = new Thickness(0, 0, 0, 6)
                });

                topStack.Children.Add(new TextBlock
                {
                    Text = BuilderI18n.T(
                        "Przygotuj rozkaz operacyjny NATO (1. Sytuacja + Automatyczna Pogoda/Słońce/Księżyc METOC, 2. Zadanie 5W, 3. Realizacja, 4. Logistyka, 5. Dowodzenie i Łączność). Sekcja 5 synchronizuje automatycznie tabelę radiową, klucze LoRa i adres serwera OTS z wybranej strony.",
                        "Build a structured NATO 5-Paragraph Operational Order with live METOC Weather, Sunrise/Sunset & Moon Phase intelligence and automatic radio/LoRa/OTS sync."),
                    Foreground = TacticalConfigPanel.BrushMutedText,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 10)
                });

                var actionBar = new Grid();
                actionBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
                actionBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                actionBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                actionBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var facCombo = new ComboBox
                {
                    Height = 32,
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 8, 0),
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
                    Content = BuilderI18n.T("🖨️ Otwórz OPORD do druku A4 (HTML / PDF)", "🖨️ Open Printable A4 OPORD (HTML / PDF)"),
                    Height = 32,
                    Margin = new Thickness(0, 0, 6, 0),
                    Background = TacticalConfigPanel.BrushButtonAccent,
                    Foreground = TacticalConfigPanel.BrushDarkFg,
                    FontWeight = FontWeights.Bold,
                    FontSize = 11,
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

                var btnCopyDiscord = new Button
                {
                    Content = BuilderI18n.T("💬 Kopiuj OPORD na Discorda (Markdown)", "💬 Copy OPORD for Discord (Markdown)"),
                    Height = 32,
                    Margin = new Thickness(0, 0, 6, 0),
                    Background = new SolidColorBrush(Color.FromRgb(22, 54, 66)),
                    Foreground = Brushes.White,
                    BorderBrush = TacticalConfigPanel.BrushAccent,
                    BorderThickness = new Thickness(1),
                    FontWeight = FontWeights.Bold,
                    FontSize = 11,
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
                            "Skopiowano sformatowany rozkaz OPORD strony " + _activeOpordFaction + " (wraz z danymi METOC: pogoda, słońce, księżyc) do schowka! Możesz wkleić go na Discordzie (Ctrl+V).",
                            "Copied formatted OPORD for " + _activeOpordFaction + " (including METOC weather, sun & moon data) to clipboard!"),
                        "OPORD -> Discord Markdown",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                };
                Grid.SetColumn(btnCopyDiscord, 2);
                actionBar.Children.Add(btnCopyDiscord);

                var btnSaveToFtak = new Button
                {
                    Content = BuilderI18n.T("📦 Zapisz OPORD w paczce .FTAK", "📦 Embed OPORD into .FTAK"),
                    Height = 32,
                    Background = TacticalConfigPanel.BrushItemBg,
                    Foreground = TacticalConfigPanel.BrushAccent,
                    BorderBrush = TacticalConfigPanel.BrushAccent,
                    BorderThickness = new Thickness(1),
                    FontWeight = FontWeights.Bold,
                    FontSize = 11,
                    Cursor = Cursors.Hand
                };
                btnSaveToFtak.Click += (_, __) =>
                {
                    PersistModelToAppData(model);
                    SaveAllOpordsToDisk(win);
                    MessageBox.Show(
                        win,
                        BuilderI18n.T(
                            "Zapisano rozkaz OPORD_" + _activeOpordFaction + " w zasobach frakcji! Zostanie on automatycznie dołączony do pliku .FTAK.",
                            "Saved OPORD_" + _activeOpordFaction + " to faction assets! It will be automatically bundled into the .FTAK package."),
                        "OPORD Saved",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                };
                Grid.SetColumn(btnSaveToFtak, 3);
                actionBar.Children.Add(btnSaveToFtak);

                topStack.Children.Add(actionBar);
                topCard.Child = topStack;
                mainStack.Children.Add(topCard);

                // 0. METADANE NAGŁÓWKA ROZKAZU
                var metaCard = CreateSectionCard(BuilderI18n.T("0. METADANE ROZKAZU (NAGŁÓWEK NATO)", "0. ORDER METADATA (NATO HEADER)"));
                var metaGrid = new UniformGrid { Columns = 4 };
                metaGrid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("Klasyfikacja", "Classification"), model.Classification, v => { model.Classification = v; PersistModelToAppData(model); }));
                metaGrid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("Nr Rozkazu / Operacji", "Order / Operation No."), model.OperationNumber, v => { model.OperationNumber = v; PersistModelToAppData(model); }));
                metaGrid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("Grupa Data-Czas (DTG)", "Date-Time Group (DTG)"), model.DtgIssued, v => { model.DtgIssued = v; PersistModelToAppData(model); }));
                metaGrid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("Sztab wydający (HQ)", "Issuing HQ"), model.IssuingHq, v => { model.IssuingHq = v; PersistModelToAppData(model); }));
                ((StackPanel)metaCard.Child).Children.Add(metaGrid);
                mainStack.Children.Add(metaCard);

                // 1. SYTUACJA (SITUATION) + AUTOMATYCZNY MODUŁ POGODOWY METOC
                var sec1 = CreateSectionCard(BuilderI18n.T("1. SYTUACJA + DANE POGODOWE I ASTRONOMICZNE METOC (1. SITUATION)", "1. SITUATION & METOC WEATHER / LIGHT INTELLIGENCE"));
                var sec1Stack = (StackPanel)sec1.Child;

                sec1Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("1a. Siły Przeciwnika (Enemy Forces — skład, rozmieszczenie, prawdopodobny zamiar)", "1a. Enemy Forces (Composition, disposition, most likely course of action)"),
                    model.EnemyForces, 54, v => { model.EnemyForces = v; PersistModelToAppData(model); }));

                sec1Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("1b. Siły Własne (Friendly Forces — zadanie wyższego szczebla, sąsiedzi, wsparcie)", "1b. Friendly Forces (Higher HQ mission, adjacent units, support)"),
                    model.FriendlyForces, 54, v => { model.FriendlyForces = v; PersistModelToAppData(model); }));

                // PASEK POBIERANIA DANYCH POGODOWYCH I ASTRONOMICZNYCH METOC (LIVE INTERNET + KALKULATOR SŁOŃCA I KSIĘŻYCA)
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
                        "🌦️ AUTOMATYCZNY POBIERAK DANYCH METOC (POGODA, WIATR, WILGOTNOŚĆ, OPADY, WSCHÓD/ZACHÓD SŁOŃCA, FAZA KSIĘŻYCA)",
                        "🌦️ AUTOMATIC METOC FETCHER (WEATHER, WIND, HUMIDITY, PRECIPITATION, SUNRISE/SUNSET, MOON PHASE)"),
                    Foreground = TacticalConfigPanel.BrushAccent,
                    FontWeight = FontWeights.Bold,
                    FontSize = 11.5,
                    Margin = new Thickness(0, 0, 0, 6)
                });

                var metocInputsGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                metocInputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
                metocInputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
                metocInputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                metocInputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                metocInputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });

                var dateBoxPanel = CreateLabeledTextBox(BuilderI18n.T("Data operacji (RRRR-MM-DD)", "Date (YYYY-MM-DD)"), model.OperationDateIso, v => { model.OperationDateIso = v; PersistModelToAppData(model); });
                Grid.SetColumn(dateBoxPanel, 0);
                metocInputsGrid.Children.Add(dateBoxPanel);

                var cityBoxPanel = CreateLabeledTextBox(BuilderI18n.T("Miejscowość / Rejon AO", "Town / AO Location"), model.LocationName, v => { model.LocationName = v; PersistModelToAppData(model); });
                var cityTextBox = FindFirstDescendant<TextBox>(cityBoxPanel);
                Grid.SetColumn(cityBoxPanel, 1);
                metocInputsGrid.Children.Add(cityBoxPanel);

                var latBoxPanel = CreateLabeledTextBox(BuilderI18n.T("Szer. (Lat °N)", "Latitude (°N)"), model.LocationLat, v => { model.LocationLat = v; PersistModelToAppData(model); });
                var latTextBox = FindFirstDescendant<TextBox>(latBoxPanel);
                Grid.SetColumn(latBoxPanel, 2);
                metocInputsGrid.Children.Add(latBoxPanel);

                var lonBoxPanel = CreateLabeledTextBox(BuilderI18n.T("Dług. (Lon °E)", "Longitude (°E)"), model.LocationLon, v => { model.LocationLon = v; PersistModelToAppData(model); });
                var lonTextBox = FindFirstDescendant<TextBox>(lonBoxPanel);
                Grid.SetColumn(lonBoxPanel, 3);
                metocInputsGrid.Children.Add(lonBoxPanel);

                TextBox terrainWeatherTextBox = null;
                var metocStatusText = new TextBlock
                {
                    Text = "☀️ Słońce: " + model.MetocSunTimes + "   |   🌙 Księżyc: " + model.MetocMoonPhase + "\n🌡️ Pogoda: " + model.MetocCondition + "   |   💨 Wiatr: " + model.MetocWind,
                    Foreground = Brushes.White,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 10.5,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0)
                };

                var btnFetchMetoc = new Button
                {
                    Content = BuilderI18n.T("🌦️ Pobierz prognozę METOC, Słońce i Księżyc", "🌦️ Fetch Live METOC, Sun & Moon"),
                    Height = 28,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 0, 4),
                    Background = TacticalConfigPanel.BrushButtonAccent,
                    Foreground = TacticalConfigPanel.BrushDarkFg,
                    FontWeight = FontWeights.Bold,
                    FontSize = 10.5,
                    Cursor = Cursors.Hand
                };

                btnFetchMetoc.Click += async (_, __) =>
                {
                    btnFetchMetoc.IsEnabled = false;
                    btnFetchMetoc.Content = BuilderI18n.T("⏳ Pobieranie METOC...", "⏳ Fetching METOC...");
                    try
                    {
                        double lat = 49.8494;
                        double lon = 18.8127;
                        bool hasValidCoords =
                            double.TryParse((model.LocationLat ?? "").Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out lat) &&
                            double.TryParse((model.LocationLon ?? "").Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out lon);

                        // Jeśli wpisano nazwę miejscowości (inną niż domyślna), spróbuj ją zgeokodować
                        if (!string.IsNullOrWhiteSpace(model.LocationName) &&
                            !model.LocationName.Equals("Rejon Operacji (AO)", StringComparison.OrdinalIgnoreCase))
                        {
                            var geo = await OpordMetocWeatherService.GeocodeLocationAsync(model.LocationName);
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

                        var rep = await OpordMetocWeatherService.FetchMetocReportAsync(lat, lon, model.OperationDateIso, model.LocationName);
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

                        // Dopisz lub zaktualizuj blok METOC w polu 1c. Teren i Pogoda
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
                    catch (Exception ex)
                    {
                        metocStatusText.Text = "⚠️ Błąd METOC: " + ex.Message;
                    }
                    finally
                    {
                        btnFetchMetoc.IsEnabled = true;
                        btnFetchMetoc.Content = BuilderI18n.T("🌦️ Pobierz prognozę METOC, Słońce i Księżyc", "🌦️ Fetch Live METOC, Sun & Moon");
                    }
                };
                Grid.SetColumn(btnFetchMetoc, 4);
                metocInputsGrid.Children.Add(btnFetchMetoc);

                metocStack.Children.Add(metocInputsGrid);
                metocStack.Children.Add(metocStatusText);
                metocBar.Child = metocStack;
                sec1Stack.Children.Add(metocBar);

                var terrainBoxPanel = CreateLabeledMultilineBox(
                    BuilderI18n.T("1c. Teren i Warunki Atmosferyczne (Terrain & Weather — OAKOC + automatyczny raport METOC)", "1c. Terrain & Weather (OAKOC + automatic METOC report)"),
                    model.TerrainAndWeather, 95, v => { model.TerrainAndWeather = v; PersistModelToAppData(model); });
                terrainWeatherTextBox = FindFirstDescendant<TextBox>(terrainBoxPanel);
                sec1Stack.Children.Add(terrainBoxPanel);
                mainStack.Children.Add(sec1);

                // 2. ZADANIE (MISSION — KREATOR 5W)
                var sec2 = CreateSectionCard(BuilderI18n.T("2. ZADANIE BOJOWE — KREATOR NATO 5W (2. MISSION)", "2. MISSION — NATO 5W STATEMENT BUILDER"));
                var sec2Stack = (StackPanel)sec2.Child;

                var w5Grid = new UniformGrid { Columns = 5, Margin = new Thickness(0, 0, 0, 6) };
                TextBox fullMissionBox = null;

                void AutoUpdate5W()
                {
                    model.MissionFullStatement = Compose5WMissionText(model);
                    if (fullMissionBox != null) fullMissionBox.Text = model.MissionFullStatement;
                    PersistModelToAppData(model);
                }

                w5Grid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("KTO (Who)", "WHO (Unit)"), model.MissionWho, v => { model.MissionWho = v; AutoUpdate5W(); }));
                w5Grid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("CO (What — działanie)", "WHAT (Tactical Task)"), model.MissionWhat, v => { model.MissionWhat = v; AutoUpdate5W(); }));
                w5Grid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("KIEDY (When — Czas H)", "WHEN (Time / NLT)"), model.MissionWhen, v => { model.MissionWhen = v; AutoUpdate5W(); }));
                w5Grid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("GDZIE (Where — MGRS/AO)", "WHERE (Grid / Objective)"), model.MissionWhere, v => { model.MissionWhere = v; AutoUpdate5W(); }));
                w5Grid.Children.Add(CreateLabeledTextBox(BuilderI18n.T("DLACZEGO (Why — Cel)", "WHY (Purpose / In order to)"), model.MissionWhy, v => { model.MissionWhy = v; AutoUpdate5W(); }));
                sec2Stack.Children.Add(w5Grid);

                var fullMissionPanel = CreateLabeledMultilineBox(
                    BuilderI18n.T("Pełne brzmienie zadania (2. MISSION — generowane automatycznie z pól 5W lub edytowalne ręcznie):", "Full Mission Statement (2. MISSION — auto-generated from 5W or manually editable):"),
                    model.MissionFullStatement, 48, v => { model.MissionFullStatement = v; PersistModelToAppData(model); });
                fullMissionBox = FindFirstDescendant<TextBox>(fullMissionPanel);
                sec2Stack.Children.Add(fullMissionPanel);
                mainStack.Children.Add(sec2);

                // 3. REALIZACJA (EXECUTION)
                var sec3 = CreateSectionCard(BuilderI18n.T("3. REALIZACJA (3. EXECUTION)", "3. EXECUTION"));
                var sec3Stack = (StackPanel)sec3.Child;
                sec3Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("3a. Zamysł Dowódcy (Commander's Intent — Cel, Kluczowe Zadania, Stan Końcowy / End State)", "3a. Commander's Intent (Purpose, Key Tasks, End State)"),
                    model.CommandersIntent, 58, v => { model.CommandersIntent = v; PersistModelToAppData(model); }));
                sec3Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("3b. Koncepcja Działań — Fazy Operacji (Concept of Operations)", "3b. Concept of Operations (Phases of the Operation)"),
                    model.ConceptOfOperations, 68, v => { model.ConceptOfOperations = v; PersistModelToAppData(model); }));
                sec3Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("3c. Zadania dla Pododdziałów (Tasks to Subordinate Units — ALFA, BRAVO, RECON, QRF)", "3c. Tasks to Subordinate Units (ALFA, BRAVO, RECON, QRF)"),
                    model.TasksToSubordinateUnits, 68, v => { model.TasksToSubordinateUnits = v; PersistModelToAppData(model); }));
                sec3Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("3d. Wytyczne Koordynacyjne i Zasady Użycia Siły (Coordinating Instructions & ROE)", "3d. Coordinating Instructions & Rules of Engagement (ROE)"),
                    model.CoordinatingAndRoe, 58, v => { model.CoordinatingAndRoe = v; PersistModelToAppData(model); }));
                mainStack.Children.Add(sec3);

                // 4. ZABEZPIECZENIE LOGISTYCZNE (SUSTAINMENT)
                var sec4 = CreateSectionCard(BuilderI18n.T("4. ZABEZPIECZENIE LOGISTYCZNE I MEDYCZNE (4. SUSTAINMENT)", "4. SUSTAINMENT (SERVICE & SUPPORT)"));
                var sec4Stack = (StackPanel)sec4.Child;
                sec4Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("4a. Ewakuacja Medyczna (MEDEVAC / Zasady Medyka / Przycisk SOS MGRS)", "4a. Medical Evacuation (MEDEVAC / Medic Rules / SOS MGRS Button)"),
                    model.MedevacRules, 52, v => { model.MedevacRules = v; PersistModelToAppData(model); }));
                sec4Stack.Children.Add(CreateLabeledMultilineBox(
                    BuilderI18n.T("4b. Respawn / FOB, Zaopatrzenie, Limity Amunicji i Pirotechniki", "4b. Respawn / FOB, Resupply, Ammo & Pyro Limits"),
                    model.RespawnAndSupply, 52, v => { model.RespawnAndSupply = v; PersistModelToAppData(model); }));
                mainStack.Children.Add(sec4);

                // 5. DOWODZENIE I ŁĄCZNOŚĆ (COMMAND & SIGNAL)
                var sec5 = CreateSectionCard(BuilderI18n.T("5. DOWODZENIE I ŁĄCZNOŚĆ (5. COMMAND & SIGNAL — AUTO-SYNC Z GENERATORA STRON)", "5. COMMAND & SIGNAL (AUTO-SYNCED FROM FACTION GENERATOR)"));
                var sec5Stack = (StackPanel)sec5.Child;

                var cmdGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 6) };
                cmdGrid.Children.Add(CreateLabeledTextBox(
                    BuilderI18n.T("5a. Łańcuch Dowodzenia (Succession of Command)", "5a. Succession of Command"),
                    model.SuccessionOfCommand, v => { model.SuccessionOfCommand = v; PersistModelToAppData(model); }));
                cmdGrid.Children.Add(CreateLabeledTextBox(
                    BuilderI18n.T("5b. Hasło i Odzew (Challenge & Password / Kombinacja)", "5b. Challenge & Password / Number Combination"),
                    model.ChallengeAndPassword, v => { model.ChallengeAndPassword = v; PersistModelToAppData(model); }));
                sec5Stack.Children.Add(cmdGrid);

                var liveSignalInfo = new Border
                {
                    Background = TacticalConfigPanel.BrushInputBg,
                    BorderBrush = TacticalConfigPanel.BrushAccent,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 10, 12, 10)
                };
                liveSignalInfo.Child = new TextBlock
                {
                    Text = BuildLiveSignalSummaryText(win, _activeOpordFaction),
                    Foreground = TacticalConfigPanel.BrushAccent,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap
                };
                sec5Stack.Children.Add(liveSignalInfo);
                mainStack.Children.Add(sec5);
            }

            RebuildOpordForm();
            BuilderI18n.LanguageChanged += _ => RebuildOpordForm();
            _cachedOpordOverlay = rootBorder;
            return rootBorder;
        }

        private static string BuildLiveSignalSummaryText(Window win, string factionName)
        {
            var facJson = TryGetFactionJsonObject(win, factionName);
            string chName = facJson?["ChannelName"]?.ToString() ?? (factionName + "-TAK");
            string region = facJson?["LoraRegion"]?.ToString() ?? "EU_868";
            string preset = facJson?["LoraModemPreset"]?.ToString() ?? "SHORT_FAST";
            string ots = facJson?["OtsHost"]?.ToString() ?? "";
            string phone = facJson?["EmergencyPhone"]?.ToString() ?? "";

            var sb = new StringBuilder();
            sb.AppendLine(BuilderI18n.T(
                "📡 [AUTOMATYCZNIE POBRANE DANE ŁĄCZNOŚCI STRONY: " + factionName + "]",
                "📡 [AUTO-SYNCED SIGNAL DATA FOR FACTION: " + factionName + "]"));
            sb.AppendLine("• Meshtastic LoRa: Kanał '" + chName + "'  |  Region: " + region + "  |  Preset: " + preset + "  |  Szyfrowanie: AES-256 PSK");
            sb.AppendLine("• Serwer ATAK (OpenTAKServer): " + (string.IsNullOrWhiteSpace(ots) ? "Tryb lokalny / Off-Grid Mesh" : ots));
            sb.AppendLine("• Telefon alarmowy ORGA / MEDEVAC (Przycisk SOS MGRS): " + (string.IsNullOrWhiteSpace(phone) ? "Brak / 112" : phone));

            if (facJson?["VoiceChannels"] is JsonArray vArr && vArr.Count > 0)
            {
                sb.AppendLine(BuilderI18n.T("• Tabela Kanałów Głosowych (Ściąga Operacyjna):", "• Voice Radio Channels (Field Cheat Sheet):"));
                foreach (var vo in vArr.OfType<JsonObject>())
                {
                    string r = vo["roleName"]?.ToString() ?? "Kanał";
                    string b = vo["bandType"]?.ToString() ?? "PMR";
                    string f = vo["freqMhz"]?.ToString() ?? "-";
                    string t = vo["toneOrCode"]?.ToString() ?? "-";
                    sb.AppendLine("   - " + r + " [" + b + "]: " + f + "  (Ton/Kod: " + t + ")");
                }
            }
            return sb.ToString().TrimEnd();
        }

        private static JsonObject TryGetFactionJsonObject(Window win, string factionName)
        {
            try
            {
                TacticalConfigPanel.SaveSessionStateAuto(win);
                if (!string.IsNullOrWhiteSpace(TacticalConfigPanel.CurrentDraftProjectPath) &&
                    File.Exists(TacticalConfigPanel.CurrentDraftProjectPath))
                {
                    if (JsonNode.Parse(File.ReadAllText(TacticalConfigPanel.CurrentDraftProjectPath, Encoding.UTF8)) is JsonObject root &&
                        root["factions"] is JsonArray arr)
                    {
                        foreach (var item in arr.OfType<JsonObject>())
                        {
                            string n = item["Name"]?.ToString() ?? item["name"]?.ToString() ?? "";
                            if (n.Equals(factionName, StringComparison.OrdinalIgnoreCase))
                                return item;
                        }
                        return arr.OfType<JsonObject>().FirstOrDefault();
                    }
                }
            }
            catch { }
            return null;
        }

        public static string GeneratePrintableOpordHtml(Window win, string factionName, OpordParagraphModel m)
        {
            string eventName = TacticalConfigPanel.GetEventNameFromWindow(win);
            var facJson = TryGetFactionJsonObject(win, factionName);
            string colorHex = facJson?["ColorHex"]?.ToString() ?? "#2684FF";
            string chName = facJson?["ChannelName"]?.ToString() ?? (factionName + "-TAK");
            string region = facJson?["LoraRegion"]?.ToString() ?? "EU_868";
            string preset = facJson?["LoraModemPreset"]?.ToString() ?? "SHORT_FAST";
            string ots = facJson?["OtsHost"]?.ToString() ?? "Off-Grid Mesh";
            string phone = facJson?["EmergencyPhone"]?.ToString() ?? "112";

            var radioRowsHtml = new StringBuilder();
            if (facJson?["VoiceChannels"] is JsonArray vArr && vArr.Count > 0)
            {
                foreach (var vo in vArr.OfType<JsonObject>())
                {
                    radioRowsHtml.AppendLine(
                        "<tr><td><b>" + WebUtility.HtmlEncode(vo["roleName"]?.ToString()) + "</b></td>" +
                        "<td>" + WebUtility.HtmlEncode(vo["bandType"]?.ToString()) + "</td>" +
                        "<td><b>" + WebUtility.HtmlEncode(vo["freqMhz"]?.ToString()) + "</b></td>" +
                        "<td>" + WebUtility.HtmlEncode(vo["toneOrCode"]?.ToString()) + "</td></tr>");
                }
            }
            else
            {
                radioRowsHtml.AppendLine("<tr><td colspan='4'>Zgodnie z konfiguracją w paczce .FTAK</td></tr>");
            }

            string E(string s) => WebUtility.HtmlEncode(s ?? "").Replace("\n", "<br/>");
            string missionText = string.IsNullOrWhiteSpace(m.MissionFullStatement) ? Compose5WMissionText(m) : m.MissionFullStatement;

            var html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html><html lang='pl'><head><meta charset='utf-8'/>");
            html.AppendLine("<title>" + E(m.OperationNumber) + " - " + E(eventName) + " (" + E(factionName) + ")</title>");
            html.AppendLine("<style>");
            html.AppendLine("@page { size: A4 portrait; margin: 11mm; }");
            html.AppendLine("body { font-family: 'Segoe UI', Arial, sans-serif; color: #111; background: #fff; margin: 0; padding: 14px; font-size: 12px; line-height: 1.42; }");
            html.AppendLine(".classification { text-align: center; font-weight: bold; font-size: 12.5px; letter-spacing: 1.5px; border-top: 2px solid #111; border-bottom: 2px solid #111; padding: 4px; margin-bottom: 10px; background: #f4f6f8; }");
            html.AppendLine(".header-box { border: 2px solid #111; padding: 10px 14px; margin-bottom: 12px; display: flex; justify-content: space-between; align-items: center; }");
            html.AppendLine(".faction-badge { background: " + colorHex + "; color: #fff; font-weight: bold; padding: 6px 14px; border-radius: 4px; font-size: 15px; }");
            html.AppendLine("h1 { margin: 0 0 4px 0; font-size: 17px; text-transform: uppercase; }");
            html.AppendLine("h2 { background: #102229; color: #fff; padding: 5px 10px; font-size: 13px; margin: 12px 0 6px 0; text-transform: uppercase; letter-spacing: 0.5px; }");
            html.AppendLine(".sub-p { margin: 5px 0 7px 8px; }");
            html.AppendLine(".mission-box { border: 2px solid " + colorHex + "; background: #f0f7ff; padding: 9px 12px; font-weight: bold; font-size: 13px; margin: 6px 0; }");
            html.AppendLine("table { width: 100%; border-collapse: collapse; margin-top: 6px; font-size: 11.5px; }");
            html.AppendLine("th, td { border: 1px solid #333; padding: 4px 7px; text-align: left; }");
            html.AppendLine("th { background: #e6ecef; }");
            html.AppendLine(".no-print { margin-bottom: 12px; text-align: right; }");
            html.AppendLine(".btn-print { background: #102229; color: #1FE0C5; border: 2px solid #1FE0C5; padding: 8px 16px; font-weight: bold; cursor: pointer; border-radius: 6px; }");
            html.AppendLine("@media print { .no-print { display: none; } }");
            html.AppendLine("</style></head><body>");
            html.AppendLine("<div class='no-print'><button class='btn-print' onclick='window.print()'>🖨️ Drukuj Rozkaz OPORD / Zapisz jako PDF (A4)</button></div>");
            html.AppendLine("<div class='classification'>" + E(m.Classification) + "</div>");
            html.AppendLine("<div class='header-box'><div>");
            html.AppendLine("<h1>" + E(m.OperationNumber) + " — " + E(eventName) + "</h1>");
            html.AppendLine("<div><b>Sztab (HQ):</b> " + E(m.IssuingHq) + " &nbsp;|&nbsp; <b>Data operacji:</b> " + E(m.OperationDateIso) + " &nbsp;|&nbsp; <b>DTG:</b> " + E(m.DtgIssued) + "</div>");
            html.AppendLine("<div><b>Rejon AO:</b> " + E(m.LocationName) + " (" + E(m.LocationLat) + "°N, " + E(m.LocationLon) + "°E) &nbsp;|&nbsp; <b>Mapy:</b> " + E(m.MapReferences) + "</div>");
            html.AppendLine("</div><div class='faction-badge'>" + E(factionName) + "</div></div>");

            html.AppendLine("<h2>1. SYTUACJA I WARUNKI METOC (1. SITUATION &amp; METOC)</h2>");
            html.AppendLine("<div class='sub-p'><b>a. Siły Przeciwnika (Enemy Forces):</b><br/>" + E(m.EnemyForces) + "</div>");
            html.AppendLine("<div class='sub-p'><b>b. Siły Własne (Friendly Forces):</b><br/>" + E(m.FriendlyForces) + "</div>");
            html.AppendLine("<div class='sub-p'><b>c. Teren i Pogoda (Terrain &amp; Weather):</b><br/>" + E(m.TerrainAndWeather) + "</div>");

            html.AppendLine("<table><thead><tr><th colspan='2'>🌦️ TABELA DANYCH METEOROLOGICZNYCH I ASTRONOMICZNYCH (METOC — " + E(m.OperationDateIso) + ")</th></tr></thead><tbody>");
            html.AppendLine("<tr><td style='width:28%'><b>Warunki i zachmurzenie</b></td><td>" + E(m.MetocCondition) + "</td></tr>");
            html.AppendLine("<tr><td><b>Temperatura</b></td><td>" + E(m.MetocTemperature) + "</td></tr>");
            html.AppendLine("<tr><td><b>Wiatr (prędkość / kierunek)</b></td><td>" + E(m.MetocWind) + "</td></tr>");
            html.AppendLine("<tr><td><b>Wilgotność i opady</b></td><td>" + E(m.MetocHumidityPrecip) + "</td></tr>");
            html.AppendLine("<tr><td><b>Słońce (BMNT / SR / SS / EENT)</b></td><td><b>" + E(m.MetocSunTimes) + "</b></td></tr>");
            html.AppendLine("<tr><td><b>Faza Księżyca i NVG</b></td><td><b>" + E(m.MetocMoonPhase) + "</b></td></tr>");
            html.AppendLine("</tbody></table>");

            html.AppendLine("<h2>2. ZADANIE (2. MISSION)</h2>");
            html.AppendLine("<div class='mission-box'>" + E(missionText) + "</div>");

            html.AppendLine("<h2>3. REALIZACJA (3. EXECUTION)</h2>");
            html.AppendLine("<div class='sub-p'><b>a. Zamysł Dowódcy (Commander's Intent):</b><br/>" + E(m.CommandersIntent) + "</div>");
            html.AppendLine("<div class='sub-p'><b>b. Koncepcja Działań (Concept of Operations):</b><br/>" + E(m.ConceptOfOperations) + "</div>");
            html.AppendLine("<div class='sub-p'><b>c. Zadania dla Pododdziałów (Tasks to Subordinate Units):</b><br/>" + E(m.TasksToSubordinateUnits) + "</div>");
            html.AppendLine("<div class='sub-p'><b>d. Wytyczne Koordynacyjne i ROE (Coordinating Instructions):</b><br/>" + E(m.CoordinatingAndRoe) + "</div>");

            html.AppendLine("<h2>4. ZABEZPIECZENIE LOGISTYCZNE I MEDYCZNE (4. SUSTAINMENT)</h2>");
            html.AppendLine("<div class='sub-p'><b>a. Ewakuacja Medyczna (MEDEVAC / SOS MGRS):</b><br/>" + E(m.MedevacRules) + "</div>");
            html.AppendLine("<div class='sub-p'><b>b. Respawn / FOB i Zaopatrzenie:</b><br/>" + E(m.RespawnAndSupply) + "</div>");

            html.AppendLine("<h2>5. DOWODZENIE I ŁĄCZNOŚĆ (5. COMMAND &amp; SIGNAL)</h2>");
            html.AppendLine("<div class='sub-p'><b>a. Łańcuch Dowodzenia (Succession of Command):</b> " + E(m.SuccessionOfCommand) + "</div>");
            html.AppendLine("<div class='sub-p'><b>b. Hasło i Odzew (Challenge &amp; Password):</b> <b>" + E(m.ChallengeAndPassword) + "</b></div>");
            html.AppendLine("<div class='sub-p'><b>c. Łączność Cyfrowa ATAK &amp; Meshtastic LoRa:</b> Kanał <b>" + E(chName) + "</b> (" + E(region) + " / " + E(preset) + ") &nbsp;|&nbsp; OTS: <b>" + E(ots) + "</b> &nbsp;|&nbsp; Alarm ORGA/MEDEVAC: <b>" + E(phone) + "</b></div>");
            html.AppendLine("<table><thead><tr><th>Rola / Kanał</th><th>Pasmo</th><th>Częstotliwość / Kanał</th><th>Ton CTCSS / Kod DMR</th></tr></thead><tbody>" + radioRowsHtml + "</tbody></table>");
            html.AppendLine("<div class='classification' style='margin-top:14px;'>" + E(m.Classification) + " — Field TAK Hub v2.5</div>");
            html.AppendLine("</body></html>");
            return html.ToString();
        }

        public static string GenerateDiscordMarkdownOpord(Window win, string factionName, OpordParagraphModel m)
        {
            string eventName = TacticalConfigPanel.GetEventNameFromWindow(win);
            var sb = new StringBuilder();
            sb.AppendLine("# 📋 " + m.OperationNumber + " — " + eventName + " [" + factionName + "]");
            sb.AppendLine("`" + m.Classification + "` | **Data:** `" + m.OperationDateIso + "` | **DTG:** `" + m.DtgIssued + "` | **HQ:** `" + m.IssuingHq + "`");
            sb.AppendLine("---");
            sb.AppendLine("## 1️⃣ SYTUACJA I WARUNKI METOC (SITUATION & WEATHER)");
            sb.AppendLine("- **Siły Przeciwnika (Enemy):** " + m.EnemyForces);
            sb.AppendLine("- **Siły Własne (Friendly):** " + m.FriendlyForces);
            sb.AppendLine("- **Teren i Pogoda:** " + m.TerrainAndWeather);
            sb.AppendLine("```text");
            sb.AppendLine("🌦️ METOC (" + m.OperationDateIso + " — " + m.LocationName + "):");
            sb.AppendLine("• Warunki: " + m.MetocCondition);
            sb.AppendLine("• Temperatura: " + m.MetocTemperature);
            sb.AppendLine("• Wiatr: " + m.MetocWind);
            sb.AppendLine("• Wilgotność / Opady: " + m.MetocHumidityPrecip);
            sb.AppendLine("• Słońce: " + m.MetocSunTimes);
            sb.AppendLine("• Księżyc / NVG: " + m.MetocMoonPhase);
            sb.AppendLine("```");
            sb.AppendLine("## 2️⃣ ZADANIE BOJOWE (MISSION)");
            sb.AppendLine("> 🎯 **" + Compose5WMissionText(m) + "**");
            sb.AppendLine();
            sb.AppendLine("## 3️⃣ REALIZACJA (EXECUTION)");
            sb.AppendLine("**a. Zamysł Dowódcy (Commander's Intent):**\n" + m.CommandersIntent);
            sb.AppendLine("**b. Koncepcja Działań (Fazy):**\n" + m.ConceptOfOperations);
            sb.AppendLine("**c. Zadania dla Pododdziałów:**\n" + m.TasksToSubordinateUnits);
            sb.AppendLine("**d. Wytyczne Koordynacyjne i ROE:**\n" + m.CoordinatingAndRoe);
            sb.AppendLine();
            sb.AppendLine("## 4️⃣ ZABEZPIECZENIE I MEDEVAC (SUSTAINMENT)");
            sb.AppendLine("- **MEDEVAC / SOS MGRS:** " + m.MedevacRules);
            sb.AppendLine("- **Respawn / Zaopatrzenie:** " + m.RespawnAndSupply);
            sb.AppendLine();
            sb.AppendLine("## 5️⃣ DOWODZENIE I ŁĄCZNOŚĆ (COMMAND & SIGNAL)");
            sb.AppendLine("- **Łańcuch Dowodzenia:** `" + m.SuccessionOfCommand + "`");
            sb.AppendLine("- **Hasło / Odzew (Challenge / Password):** `" + m.ChallengeAndPassword + "`");
            sb.AppendLine("```text");
            sb.AppendLine(BuildLiveSignalSummaryText(win, factionName));
            sb.AppendLine("```");
            return sb.ToString();
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
            var sp = new StackPanel { Margin = new Thickness(0, 0, 8, 4) };
            sp.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = TacticalConfigPanel.BrushMutedText,
                FontSize = 10.5,
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
                FontSize = 11.5
            };
            tb.TextChanged += (_, __) => onChanged?.Invoke(tb.Text);
            sp.Children.Add(tb);
            return sp;
        }

        private static StackPanel CreateLabeledMultilineBox(string label, string initialValue, double height, Action<string> onChanged)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
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
                Padding = new Thickness(8, 6, 8, 6),
                FontSize = 11.5
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