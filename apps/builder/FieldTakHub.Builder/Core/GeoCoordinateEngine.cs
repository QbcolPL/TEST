#nullable disable
using System;
using System.Globalization;

namespace FieldTakHub.Builder.Core
{
    public struct UtmPoint
    {
        public int Zone;
        public char LatBand;
        public double Easting;
        public double Northing;
        public bool IsNorthernHemisphere;
    }

    public struct MapSheetBounds
    {
        public double MinLat;
        public double MaxLat;
        public double MinLon;
        public double MaxLon;
        public double WidthMeters;
        public double HeightMeters;
    }

    /// <summary>
    /// Wojskowy silnik geodezyjny napisany od podstaw.
    /// Obsługuje konwersje WGS84 <-> UTM <-> MGRS (z podziałem kwadratów 100 m na Keypad 1..9 oraz Ćwiartki A..D),
    /// format DMS oraz wyliczanie rzeczywistej skali 1:1 (np. 1:5000 / 1:10000) dla arkuszy A4/A3/mapników.
    /// </summary>
    public static class GeoCoordinateEngine
    {
        private static readonly string[] MgrsColSets = { "ABCDEFGH", "JKLMNPQR", "STUVWXYZ" };
        private static readonly string[] MgrsRowSets = { "ABCDEFGHJKLMNPQRSTUV", "FGHJKLMNPQRSTUVABCDE" };
        private const string MgrsLatBands = "CDEFGHJKLMNPQRSTUVWXX";

        public static UtmPoint LatLonToUtm(double lat, double lon)
        {
            double cLat = Math.Clamp(lat, -80.0, 84.0);
            double nLon = ((lon + 180.0) % 360.0 + 360.0) % 360.0 - 180.0;
            int zone = (int)Math.Floor((nLon + 180.0) / 6.0) + 1;

            double latRad = cLat * Math.PI / 180.0;
            double lonRad = nLon * Math.PI / 180.0;
            double lonOriginRad = ((zone - 1) * 6 - 180 + 3) * Math.PI / 180.0;

            const double a = 6378137.0;
            const double eccSq = 0.00669438;
            const double k0 = 0.9996;
            double eccPrimeSq = eccSq / (1.0 - eccSq);

            double n = a / Math.Sqrt(1.0 - eccSq * Math.Sin(latRad) * Math.Sin(latRad));
            double t = Math.Tan(latRad) * Math.Tan(latRad);
            double c = eccPrimeSq * Math.Cos(latRad) * Math.Cos(latRad);
            double A = Math.Cos(latRad) * (lonRad - lonOriginRad);

            double M = a * ((1.0 - eccSq / 4.0 - 3.0 * eccSq * eccSq / 64.0) * latRad
                          - (3.0 * eccSq / 8.0 + 3.0 * eccSq * eccSq / 32.0) * Math.Sin(2.0 * latRad)
                          + (15.0 * eccSq * eccSq / 256.0) * Math.Sin(4.0 * latRad));

            double easting = k0 * n * (A + (1.0 - t + c) * Math.Pow(A, 3) / 6.0
                           + (5.0 - 18.0 * t + t * t + 72.0 * c - 58.0 * eccPrimeSq) * Math.Pow(A, 5) / 120.0) + 500000.0;

            double northing = k0 * (M + n * Math.Tan(latRad) * (A * A / 2.0
                            + (5.0 - t + 9.0 * c + 4.0 * c * c) * Math.Pow(A, 4) / 24.0));

            if (cLat < 0) northing += 10000000.0;

            int bandIdx = Math.Clamp((int)Math.Floor((cLat + 80.0) / 8.0), 0, MgrsLatBands.Length - 1);

            return new UtmPoint
            {
                Zone = zone,
                LatBand = MgrsLatBands[bandIdx],
                Easting = easting,
                Northing = northing,
                IsNorthernHemisphere = cLat >= 0
            };
        }

        public static string LatLonToMgrs(double lat, double lon, int digitsPerCoord = 5, string subdivisionMode = "NONE")
        {
            try
            {
                UtmPoint utm = LatLonToUtm(lat, lon);
                int setColIdx = (utm.Zone - 1) % 3;
                int setRowIdx = (utm.Zone - 1) % 2;

                int colIdx = Math.Clamp((int)Math.Floor(utm.Easting / 100000.0) - 1, 0, 7);
                int rowIdx = Math.Clamp((int)Math.Floor(utm.Northing / 100000.0) % 20, 0, 19);

                char colLetter = MgrsColSets[setColIdx][colIdx];
                char rowLetter = MgrsRowSets[setRowIdx][rowIdx];

                int east100k = Math.Clamp((int)Math.Floor(utm.Easting % 100000.0), 0, 99999);
                int north100k = Math.Clamp((int)Math.Floor(utm.Northing % 100000.0), 0, 99999);

                string baseMgrs;
                if (digitsPerCoord == 3)
                {
                    baseMgrs = string.Format(CultureInfo.InvariantCulture, "{0:00}{1} {2}{3} {4:000} {5:000}",
                        utm.Zone, utm.LatBand, colLetter, rowLetter, east100k / 100, north100k / 100);
                }
                else if (digitsPerCoord == 4)
                {
                    baseMgrs = string.Format(CultureInfo.InvariantCulture, "{0:00}{1} {2}{3} {4:0000} {5:0000}",
                        utm.Zone, utm.LatBand, colLetter, rowLetter, east100k / 10, north100k / 10);
                }
                else
                {
                    baseMgrs = string.Format(CultureInfo.InvariantCulture, "{0:00}{1} {2}{3} {4:00000} {5:00000}",
                        utm.Zone, utm.LatBand, colLetter, rowLetter, east100k, north100k);
                }

                if (string.Equals(subdivisionMode, "KEYPAD_9", StringComparison.OrdinalIgnoreCase))
                {
                    double remE = east100k % 100.0;
                    double remN = north100k % 100.0;
                    int kx = Math.Clamp((int)Math.Floor(remE / 33.3334), 0, 2);
                    int ky = Math.Clamp((int)Math.Floor((100.0 - remN) / 33.3334), 0, 2);
                    int keypad = ky * 3 + kx + 1;
                    return baseMgrs + " [K" + keypad + "]";
                }

                if (string.Equals(subdivisionMode, "QUARTERS_4", StringComparison.OrdinalIgnoreCase))
                {
                    double remE = east100k % 100.0;
                    double remN = north100k % 100.0;
                    string q = (remN >= 50.0)
                        ? (remE < 50.0 ? "A" : "B")
                        : (remE < 50.0 ? "C" : "D");
                    return baseMgrs + " [" + q + "]";
                }

                return baseMgrs;
            }
            catch
            {
                return string.Format(CultureInfo.InvariantCulture, "{0:F5}, {1:F5}", lat, lon);
            }
        }

        public static string LatLonToUtmString(double lat, double lon)
        {
            UtmPoint u = LatLonToUtm(lat, lon);
            return string.Format(CultureInfo.InvariantCulture, "UTM {0}{1} {2:0}E {3:0}N",
                u.Zone, u.IsNorthernHemisphere ? "N" : "S", u.Easting, u.Northing);
        }

        public static string LatLonToDmsString(double lat, double lon)
        {
            return ToDmsPart(lat, lat >= 0 ? 'N' : 'S') + " " + ToDmsPart(lon, lon >= 0 ? 'E' : 'W');
        }

        private static string ToDmsPart(double val, char hemisphere)
        {
            double abs = Math.Abs(val);
            int deg = (int)Math.Floor(abs);
            double minFull = (abs - deg) * 60.0;
            int min = (int)Math.Floor(minFull);
            double sec = (minFull - min) * 60.0;
            return string.Format(CultureInfo.InvariantCulture, "{0:00}°{1:00}'{2:00.0}\"{3}", deg, min, sec, hemisphere);
        }

        public static string FormatBySystem(double lat, double lon, string coordSystem, string subdivisionMode = "NONE")
        {
            string sys = (coordSystem ?? "MGRS").Trim().ToUpperInvariant();
            if (sys.Contains("MGRS")) return LatLonToMgrs(lat, lon, 5, subdivisionMode);
            if (sys.Contains("UTM")) return LatLonToUtmString(lat, lon);
            if (sys.Contains("DMS")) return LatLonToDmsString(lat, lon);
            return string.Format(CultureInfo.InvariantCulture, "{0:F5}°N, {1:F5}°E", lat, lon);
        }

        /// <summary>
        /// Wylicza rzeczywiste pokrycie terenu (w metrach oraz stopniach WGS84) dla wybranego formatu arkusza
        /// i skali 1:1 (np. dla A4 w skali 1:5000 użyteczny obszar mapy 190x260 mm = 950 x 1300 m w terenie).
        /// </summary>
        public static MapSheetBounds CalculateTrueScaleBounds(double centerLat, double centerLon, int scaleDenominator, string sheetFormat)
        {
            double printableWidthMm = 190.0;
            double printableHeightMm = 260.0;

            string fmt = (sheetFormat ?? "A4_PORTRAIT").ToUpperInvariant();
            if (fmt.Contains("A4_LANDSCAPE")) { printableWidthMm = 270.0; printableHeightMm = 185.0; }
            else if (fmt.Contains("A3")) { printableWidthMm = 390.0; printableHeightMm = 270.0; }
            else if (fmt.Contains("WRIST")) { printableWidthMm = 135.0; printableHeightMm = 85.0; }
            else if (fmt.Contains("A5")) { printableWidthMm = 130.0; printableHeightMm = 190.0; }

            int scale = Math.Max(500, scaleDenominator);
            double widthMeters = (printableWidthMm / 1000.0) * scale;
            double heightMeters = (printableHeightMm / 1000.0) * scale;

            double metersPerDegLat = 111132.954;
            double metersPerDegLon = 111319.491 * Math.Cos(centerLat * Math.PI / 180.0);
            if (metersPerDegLon < 1000.0) metersPerDegLon = 1000.0;

            double halfLatDeg = (heightMeters / 2.0) / metersPerDegLat;
            double halfLonDeg = (widthMeters / 2.0) / metersPerDegLon;

            return new MapSheetBounds
            {
                MinLat = centerLat - halfLatDeg,
                MaxLat = centerLat + halfLatDeg,
                MinLon = centerLon - halfLonDeg,
                MaxLon = centerLon + halfLonDeg,
                WidthMeters = widthMeters,
                HeightMeters = heightMeters
            };
        }
    }
}
