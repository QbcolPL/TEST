#nullable disable
using System;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace FieldTakHub.Builder.Core
{
    public sealed class MetocValidationResult
    {
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string SummaryReport { get; set; } = "";
    }

    /// <summary>
    /// Inteligentny moduł pogodowo-astronomiczny METOC napisany od zera.
    /// Posiada twardą walidację: blokuje pobieranie pogody dla domyślnego arkusza mapy,
    /// wymagając faktycznego zaznaczenia obszaru gry (AO) na mapie lub wpisania miejscowości.
    /// </summary>
    public static class MetocWeatherService
    {
        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("FieldTakHub-C2-Builder/4.0");
            return c;
        }

        public static async Task<MetocValidationResult> FetchAndApplyMetocAsync(
            OpordDayPlanModel dayPlan,
            TacticalMapConfigModel mapConfig)
        {
            if (dayPlan == null)
            {
                return new MetocValidationResult { IsSuccess = false, ErrorMessage = "Brak aktywnego dnia OPORD." };
            }

            string typedLocation = (dayPlan.LocationName ?? "").Trim();
            bool isAutoAoLabel = typedLocation.StartsWith("Obszar AO", StringComparison.OrdinalIgnoreCase) ||
                                 typedLocation.StartsWith("AO (", StringComparison.OrdinalIgnoreCase);

            bool hasUserTypedCity = !string.IsNullOrWhiteSpace(typedLocation) && !isAutoAoLabel;
            bool hasExplicitMapAo = mapConfig != null && mapConfig.HasExplicitAoMarked;

            // TWARDA WALIDACJA: Jeżeli nie wpisano miejscowości i nie zaznaczono wielokąta AO / punktów na mapie -> ZABLOKUJ!
            if (!hasUserTypedCity && !hasExplicitMapAo)
            {
                dayPlan.LocationLat = "";
                dayPlan.LocationLon = "";
                return new MetocValidationResult
                {
                    IsSuccess = false,
                    ErrorMessage = LocalizationManager.Get("METOC_ERR_NO_AO")
                };
            }

            double targetLat = 0;
            double targetLon = 0;
            string resolvedName = typedLocation;

            try
            {
                if (hasUserTypedCity)
                {
                    string geoUrl = "https://geocoding-api.open-meteo.com/v1/search?name=" +
                                    Uri.EscapeDataString(typedLocation) + "&count=1&language=pl&format=json";
                    string geoJson = await Http.GetStringAsync(geoUrl);

                    Match mLat = Regex.Match(geoJson, "\"latitude\"\\s*:\\s*([-0-9.]+)");
                    Match mLon = Regex.Match(geoJson, "\"longitude\"\\s*:\\s*([-0-9.]+)");
                    Match mName = Regex.Match(geoJson, "\"name\"\\s*:\\s*\"([^\"]+)\"");

                    if (mLat.Success && mLon.Success &&
                        double.TryParse(mLat.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out targetLat) &&
                        double.TryParse(mLon.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out targetLon))
                    {
                        if (mName.Success) resolvedName = mName.Groups[1].Value;
                    }
                    else if (hasExplicitMapAo)
                    {
                        var aoCenter = mapConfig.GetExplicitAoCenter();
                        targetLat = aoCenter.Item1;
                        targetLon = aoCenter.Item2;
                    }
                    else
                    {
                        return new MetocValidationResult
                        {
                            IsSuccess = false,
                            ErrorMessage = LocalizationManager.T(
                                "Nie znaleziono miejscowości '" + typedLocation + "'. Sprawdź pisownię lub zaznacz obszar AO w Generatorze Map 1:1.",
                                "Location '" + typedLocation + "' not found. Check spelling or draw an AO polygon in the 1:1 Map Generator.")
                        };
                    }
                }
                else if (hasExplicitMapAo)
                {
                    var aoCenter = mapConfig.GetExplicitAoCenter();
                    targetLat = aoCenter.Item1;
                    targetLon = aoCenter.Item2;
                    string coordStr = GeoCoordinateEngine.FormatBySystem(targetLat, targetLon, mapConfig.CoordinateSystem, mapConfig.SubdivisionMode);
                    resolvedName = "Obszar AO (" + mapConfig.CoordinateSystem + ": " + coordStr + ")";
                    dayPlan.LocationName = resolvedName;
                }

                string latIso = targetLat.ToString("F5", CultureInfo.InvariantCulture);
                string lonIso = targetLon.ToString("F5", CultureInfo.InvariantCulture);
                dayPlan.LocationLat = latIso;
                dayPlan.LocationLon = lonIso;

                string wxUrl = "https://api.open-meteo.com/v1/forecast?latitude=" + latIso +
                               "&longitude=" + lonIso +
                               "&daily=temperature_2m_max,temperature_2m_min,apparent_temperature_max,apparent_temperature_min,precipitation_sum,precipitation_probability_max,wind_speed_10m_max,wind_gusts_10m_max,wind_direction_10m_dominant,sunrise,sunset&timezone=auto";

                string wxJson = await Http.GetStringAsync(wxUrl);

                string sunrise = ExtractJsonTime(wxJson, "sunrise", "06:00");
                string sunset = ExtractJsonTime(wxJson, "sunset", "18:00");
                string bmnt = AddMinutesToTime(sunrise, -35);
                string eent = AddMinutesToTime(sunset, +35);

                double tMin = ExtractJsonNumber(wxJson, "temperature_2m_min", 7.0);
                double tMax = ExtractJsonNumber(wxJson, "temperature_2m_max", 16.0);
                double appMin = ExtractJsonNumber(wxJson, "apparent_temperature_min", tMin - 1.5);
                double appMax = ExtractJsonNumber(wxJson, "apparent_temperature_max", tMax - 0.5);
                double precipMm = ExtractJsonNumber(wxJson, "precipitation_sum", 0.0);
                double precipProb = ExtractJsonNumber(wxJson, "precipitation_probability_max", 10.0);
                double windKmh = ExtractJsonNumber(wxJson, "wind_speed_10m_max", 11.0);
                double gustKmh = ExtractJsonNumber(wxJson, "wind_gusts_10m_max", 18.0);
                double windDeg = ExtractJsonNumber(wxJson, "wind_direction_10m_dominant", 250.0);

                DateTime targetDate = DateTime.TryParse(dayPlan.DateIso, out DateTime parsedDt) ? parsedDt : DateTime.Now;
                var moon = ComputeMoonPhaseAndIllumination(targetDate);
                string windCardinal = DegreesToCardinal(windDeg);
                string activeCoordSys = mapConfig != null ? mapConfig.CoordinateSystem : "MGRS";
                string activeSubdiv = mapConfig != null ? mapConfig.SubdivisionMode : "NONE";
                string formattedCoords = GeoCoordinateEngine.FormatBySystem(targetLat, targetLon, activeCoordSys, activeSubdiv);

                string nvgNote = moon.Item2 >= 45
                    ? LocalizationManager.T("Dobra widoczność pasywna NVG", "Good passive NVG visibility")
                    : LocalizationManager.T("Niska iluminacja — wymagane doświetlacze IR!", "Low illumination — IR illuminators required!");

                var sb = new StringBuilder();
                sb.AppendLine("📍 " + resolvedName + "  |  " + activeCoordSys + ": " + formattedCoords);
                sb.AppendLine("☀️ BMNT: " + bmnt + "  |  🌅 SR: " + sunrise + "  |  🌇 SS: " + sunset + "  |  🌌 EENT: " + eent);
                sb.AppendLine("🌙 " + moon.Item1 + " (" + moon.Item2 + "%)  —  " + nvgNote);
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "🌡️ Temp: {0:F1}°C .. {1:F1}°C (Odczuwalna: {2:F1}°C .. {3:F1}°C)  |  🌧️ Opady: {4:F1} mm ({5:0}%)",
                    tMin, tMax, appMin, appMax, precipMm, precipProb));
                sb.Append(string.Format(CultureInfo.InvariantCulture,
                    "💨 Wiatr: {0:F1} km/h ({1:F1} m/s)  |  Porywy: {2:F1} km/h  |  Kierunek: {3:0}° ({4})",
                    windKmh, windKmh / 3.6, gustKmh, windDeg, windCardinal));

                string report = sb.ToString();
                dayPlan.MetocQuickSummary = report;
                dayPlan.TerrainAndWeather = "[METOC " + dayPlan.DateIso + "]\r\n" + report;

                return new MetocValidationResult
                {
                    IsSuccess = true,
                    SummaryReport = report
                };
            }
            catch (Exception ex)
            {
                return new MetocValidationResult
                {
                    IsSuccess = false,
                    ErrorMessage = LocalizationManager.T("Błąd komunikacji z serwisem pogodowym METOC: ", "METOC weather service error: ") + ex.Message
                };
            }
        }

        private static string ExtractJsonTime(string json, string field, string fallback)
        {
            Match m = Regex.Match(json, "\"" + field + "\"\\s*:\\s*\\[\\s*\"[^\"]*T([0-9]{2}:[0-9]{2})\"");
            return m.Success ? m.Groups[1].Value : fallback;
        }

        private static double ExtractJsonNumber(string json, string field, double fallback)
        {
            Match m = Regex.Match(json, "\"" + field + "\"\\s*:\\s*\\[\\s*([-0-9.]+)");
            if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
                return val;
            return fallback;
        }

        private static string AddMinutesToTime(string hhmm, int deltaMinutes)
        {
            if (TimeSpan.TryParse(hhmm, out TimeSpan ts))
            {
                TimeSpan shifted = ts.Add(TimeSpan.FromMinutes(deltaMinutes));
                if (shifted.TotalMinutes < 0) shifted = shifted.Add(TimeSpan.FromHours(24));
                return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", (int)shifted.TotalHours % 24, shifted.Minutes);
            }
            return hhmm;
        }

        public static Tuple<string, int> ComputeMoonPhaseAndIllumination(DateTime date)
        {
            const double synodicPeriodSeconds = 2551443.0;
            DateTime referenceNewMoon = new DateTime(1970, 1, 7, 20, 35, 0, DateTimeKind.Utc);
            double phaseSeconds = ((date.ToUniversalTime() - referenceNewMoon).TotalSeconds) % synodicPeriodSeconds;
            if (phaseSeconds < 0) phaseSeconds += synodicPeriodSeconds;

            double ratio = phaseSeconds / synodicPeriodSeconds;
            int illuminationPercent = (int)Math.Round((1.0 - Math.Cos(ratio * 2.0 * Math.PI)) * 50.0);

            string phaseName;
            if (ratio < 0.06 || ratio > 0.94) phaseName = LocalizationManager.T("Nów", "New Moon");
            else if (ratio < 0.22) phaseName = LocalizationManager.T("Przybywający Sierp", "Waxing Crescent");
            else if (ratio < 0.30) phaseName = LocalizationManager.T("Pierwsza Kwadra", "First Quarter");
            else if (ratio < 0.46) phaseName = LocalizationManager.T("Przybywający Garb", "Waxing Gibbous");
            else if (ratio < 0.55) phaseName = LocalizationManager.T("Pełnia", "Full Moon");
            else if (ratio < 0.70) phaseName = LocalizationManager.T("Ubywający Garb", "Waning Gibbous");
            else if (ratio < 0.79) phaseName = LocalizationManager.T("Ostatnia Kwadra", "Last Quarter");
            else phaseName = LocalizationManager.T("Ubywający Sierp", "Waning Crescent");

            return Tuple.Create(phaseName, illuminationPercent);
        }

        private static string DegreesToCardinal(double deg)
        {
            string[] cardinals = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            int idx = (int)Math.Round(((deg % 360.0) + 360.0) % 360.0 / 45.0) % 8;
            return cardinals[idx];
        }
    }
}
