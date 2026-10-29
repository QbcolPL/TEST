#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FieldTakHub.Builder.Services
{
    public sealed class TacticalPoiMarker
    {
        public string Name { get; set; } = "PUNKT";
        public string Category { get; set; } = "POI"; // INFIL, RESPAWN, PARKING, KOD, HQ, OBJ
        public double Lat { get; set; }
        public double Lon { get; set; }
        public double RotationDeg { get; set; }
    }

    public sealed class TacticalMapStateSnapshot
    {
        /// <summary>
        /// True WYŁĄCZNIE wtedy, gdy na mapie faktycznie narysowano wielokąt AO (>= 3 punkty) lub postawiono punkty POI.
        /// Sam pusty arkusz mapy zwraca False!
        /// </summary>
        public bool HasExplicitAoOrPois { get; set; }
        public double AoCenterLat { get; set; } = 49.850568;
        public double AoCenterLon { get; set; } = 18.813347;
        public string CoordinateSystem { get; set; } = "MGRS";
        public int ScaleDenominator { get; set; } = 5000;
        public int LidarOpacityPercent { get; set; } = 45;
        public List<Tuple<double, double>> AoPolygonVertices { get; set; } = new List<Tuple<double, double>>();
        public List<TacticalPoiMarker> Pois { get; set; } = new List<TacticalPoiMarker>();
    }

    /// <summary>
    /// Czysty serwis obsługujący Moduł 1 (Generator Map Taktycznych 1:1 — MGRS, LiDAR, AO, Eksport 4-w-1)
    /// oraz Moduł 4 (Masowy Programator Płytek USB LoRa Meshtastic).
    /// </summary>
    public static class BuilderTacticalMapAndUsbService
    {
        private static readonly string[] ColSets = { "ABCDEFGH", "JKLMNPQR", "STUVWXYZ" };
        private static readonly string[] RowSets = { "ABCDEFGHJKLMNPQRSTUV", "FGHJKLMNPQRSTUVABCDE" };

        public static TacticalMapStateSnapshot LatestMapState { get; private set; } = new TacticalMapStateSnapshot();

        public static void SyncMapAndUsbState(Window win)
        {
            var w = win ?? Application.Current?.MainWindow;
            if (w == null) return;

            try
            {
                LatestMapState = InspectLiveMapStudio(w);
                SyncUsbProgrammerFactionSelector(w);
            }
            catch { }
        }

        /// <summary>
        /// Bada stan Generatora Map 1:1 i sprawdza, czy użytkownik faktycznie zaznaczył obszar gry (AO) lub postawił punkty POI.
        /// </summary>
        public static TacticalMapStateSnapshot InspectLiveMapStudio(Window win)
        {
            var snap = new TacticalMapStateSnapshot();
            var w = win ?? Application.Current?.MainWindow;
            if (w == null) return snap;

            try
            {
                var allElements = FactionLiveSyncBridge.GetAllRootVisualsAcrossTabs(w);

                // 1. Odczytaj wybrany układ współrzędnych (MGRS / UTM / DMS / WGS84) oraz skalę (1:5000 / 1:10000)
                foreach (var cb in allElements.OfType<ComboBox>())
                {
                    string sel = ((cb.SelectedItem is ComboBoxItem cbi ? cbi.Content?.ToString() : cb.SelectedItem?.ToString()) ?? cb.Text ?? "").Trim();
                    if (sel.Contains("MGRS", StringComparison.OrdinalIgnoreCase)) snap.CoordinateSystem = "MGRS";
                    else if (sel.Contains("DMS", StringComparison.OrdinalIgnoreCase) || sel.Contains("Stopnie", StringComparison.OrdinalIgnoreCase)) snap.CoordinateSystem = "DMS";
                    else if (sel.Contains("WGS84", StringComparison.OrdinalIgnoreCase) || sel.Contains("Dziesiętne", StringComparison.OrdinalIgnoreCase)) snap.CoordinateSystem = "WGS84";
                    else if (sel.Contains("UTM", StringComparison.OrdinalIgnoreCase) && !sel.Contains("MGRS", StringComparison.OrdinalIgnoreCase)) snap.CoordinateSystem = "UTM";

                    if (sel.Contains("1:5000") || sel.Contains("1:5 000")) snap.ScaleDenominator = 5000;
                    else if (sel.Contains("1:10000") || sel.Contains("1:10 000")) snap.ScaleDenominator = 10000;
                    else if (sel.Contains("1:2500") || sel.Contains("1:2 500")) snap.ScaleDenominator = 2500;
                }

                // 2. Sprawdź metodę ekstrakcji danych z mapy (jeśli moduł mapowy udostępnia GetExtractedMapData)
                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                var mExtract = typeof(OpordBuilderManager).GetMethod("GetExtractedMapData", flags)
                            ?? typeof(OpordBuilderManager).GetMethod("ExtractMapData", flags);

                if (mExtract != null && mExtract.GetParameters().Length == 0)
                {
                    var res = mExtract.Invoke(null, null);
                    if (res != null)
                    {
                        var rt = res.GetType();
                        bool hasAo = (bool)(rt.GetProperty("HasAoPolygon")?.GetValue(res) ?? false);
                        double cLat = Convert.ToDouble(rt.GetProperty("CenterLat")?.GetValue(res) ?? 49.850568);
                        double cLon = Convert.ToDouble(rt.GetProperty("CenterLon")?.GetValue(res) ?? 18.813347);

                        if (rt.GetProperty("Pois")?.GetValue(res) is IEnumerable pe)
                        {
                            foreach (var it in pe)
                            {
                                if (it == null) continue;
                                var itType = it.GetType();
                                snap.Pois.Add(new TacticalPoiMarker
                                {
                                    Name = itType.GetProperty("Name")?.GetValue(it)?.ToString() ?? "POI",
                                    Category = itType.GetProperty("Type")?.GetValue(it)?.ToString() ?? "POINT",
                                    Lat = Convert.ToDouble(itType.GetProperty("Lat")?.GetValue(it) ?? cLat),
                                    Lon = Convert.ToDouble(itType.GetProperty("Lon")?.GetValue(it) ?? cLon)
                                });
                            }
                        }

                        if (hasAo || snap.Pois.Count > 0)
                        {
                            snap.HasExplicitAoOrPois = true;
                            snap.AoCenterLat = cLat;
                            snap.AoCenterLon = cLon;
                        }
                    }
                }

                // 3. Sprawdź płótno Canvas w Generatorze Map pod kątem narysowanego wielokąta AO (Polygon / Polyline >= 3 punkty)
                if (!snap.HasExplicitAoOrPois)
                {
                    foreach (var canvas in allElements.OfType<Canvas>())
                    {
                        foreach (UIElement ch in canvas.Children)
                        {
                            if (ch is System.Windows.Shapes.Polygon poly && poly.Points != null && poly.Points.Count >= 3 && poly.Visibility == Visibility.Visible)
                            {
                                snap.HasExplicitAoOrPois = true;
                                break;
                            }
                            if (ch is System.Windows.Shapes.Polyline pline && pline.Points != null && pline.Points.Count >= 3 && pline.Visibility == Visibility.Visible)
                            {
                                snap.HasExplicitAoOrPois = true;
                                break;
                            }
                        }
                        if (snap.HasExplicitAoOrPois) break;
                    }

                    if (snap.HasExplicitAoOrPois)
                    {
                        foreach (var tb in allElements.OfType<TextBlock>())
                        {
                            string txt = tb.Text ?? "";
                            if (txt.Contains("WGS84:", StringComparison.OrdinalIgnoreCase))
                            {
                                var match = Regex.Match(txt, @"WGS84:\s*([0-9]+[\.,][0-9]+)\s*,\s*([0-9]+[\.,][0-9]+)");
                                if (match.Success &&
                                    double.TryParse(match.Groups[1].Value.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double lat) &&
                                    double.TryParse(match.Groups[2].Value.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double lon))
                                {
                                    snap.AoCenterLat = lat;
                                    snap.AoCenterLon = lon;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            LatestMapState = snap;
            return snap;
        }

        /// <summary>
        /// Synchronizuje listę stron konfliktu w module "Masowy Programator Płytek USB (LoRa Meshtastic)",
        /// usuwając śmieciowe wpisy (PLI / GeoChat) i podpinając czyste profile stron z Generatora Stron.
        /// </summary>
        public static void SyncUsbProgrammerFactionSelector(Window win)
        {
            try
            {
                var allElements = FactionLiveSyncBridge.GetAllRootVisualsAcrossTabs(win);
                var cleanFactions = FactionLiveSyncBridge.GetAllFactionNamesFromUi(win);

                foreach (var tb in allElements.OfType<TextBlock>())
                {
                    string t = tb.Text ?? "";
                    if (t.Contains("PROGRAMATOR PŁYTEK USB", StringComparison.OrdinalIgnoreCase) ||
                        t.Contains("MASOWY PROGRAMATOR", StringComparison.OrdinalIgnoreCase) ||
                        t.Contains("USB BOARD PROGRAMMER", StringComparison.OrdinalIgnoreCase))
                    {
                        DependencyObject root = LogicalTreeHelper.GetParent(tb) ?? VisualTreeHelper.GetParent(tb);
                        while (root != null && !(root is StackPanel || root is Grid))
                            root = LogicalTreeHelper.GetParent(root) ?? VisualTreeHelper.GetParent(root);

                        if (root != null)
                        {
                            var subElems = BuilderPackageAndUiPatcher.GetLogicalElements(root);
                            foreach (var cb in subElems.OfType<ComboBox>())
                            {
                                bool looksLikeFactionCombo = false;
                                foreach (var item in cb.Items)
                                {
                                    string s = item?.ToString() ?? "";
                                    if (s.Contains("ORGA", StringComparison.OrdinalIgnoreCase) ||
                                        s.Contains("BLUFOR", StringComparison.OrdinalIgnoreCase) ||
                                        s.Contains("STRONA", StringComparison.OrdinalIgnoreCase))
                                    {
                                        looksLikeFactionCombo = true;
                                        break;
                                    }
                                }

                                if (looksLikeFactionCombo)
                                {
                                    for (int i = cb.Items.Count - 1; i >= 0; i--)
                                    {
                                        string s = cb.Items[i]?.ToString() ?? "";
                                        if (s.Equals("PLI", StringComparison.OrdinalIgnoreCase) ||
                                            s.Equals("GeoChat", StringComparison.OrdinalIgnoreCase))
                                        {
                                            cb.Items.RemoveAt(i);
                                        }
                                    }
                                    foreach (string fn in cleanFactions)
                                    {
                                        if (!cb.Items.Cast<object>().Any(x => string.Equals(x?.ToString(), fn, StringComparison.OrdinalIgnoreCase)))
                                            cb.Items.Add(fn);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        // =========================================================================
        // SILNIK KONWERSJI WSPÓŁRZĘDNYCH (MGRS / UTM / DMS / WGS84) + KEYPAD 100m
        // =========================================================================

        public static string FormatCoordinate(double lat, double lon, string coordSystem = null)
        {
            string sys = (coordSystem ?? LatestMapState?.CoordinateSystem ?? "MGRS").ToUpperInvariant();
            if (sys.Contains("MGRS")) return LatLonToMgrs(lat, lon);
            if (sys.Contains("DMS")) return LatLonToDms(lat, lon);
            if (sys.Contains("UTM")) return LatLonToUtm(lat, lon);
            return string.Format(CultureInfo.InvariantCulture, "{0:F5}°N, {1:F5}°E", lat, lon);
        }

        public static string LatLonToMgrs(double lat, double lon)
        {
            try
            {
                double cLat = Math.Clamp(lat, -80.0, 84.0);
                double nLon = ((lon + 180.0) % 360.0 + 360.0) % 360.0 - 180.0;
                int zone = (int)Math.Floor((nLon + 180.0) / 6.0) + 1;
                double latRad = cLat * Math.PI / 180.0;
                double lonRad = nLon * Math.PI / 180.0;
                double lonOrig = ((zone - 1) * 6 - 180 + 3) * Math.PI / 180.0;
                double a = 6378137.0, eccSq = 0.00669438, k0 = 0.9996, eccP = eccSq / (1.0 - eccSq);
                double n = a / Math.Sqrt(1.0 - eccSq * Math.Sin(latRad) * Math.Sin(latRad));
                double t = Math.Tan(latRad) * Math.Tan(latRad);
                double c = eccP * Math.Cos(latRad) * Math.Cos(latRad);
                double A = Math.Cos(latRad) * (lonRad - lonOrig);
                double M = a * ((1.0 - eccSq / 4.0 - 3.0 * eccSq * eccSq / 64.0) * latRad - (3.0 * eccSq / 8.0 + 3.0 * eccSq * eccSq / 32.0) * Math.Sin(2.0 * latRad));
                double east = k0 * n * (A + (1.0 - t + c) * Math.Pow(A, 3) / 6.0) + 500000.0;
                double north = k0 * (M + n * Math.Tan(latRad) * (A * A / 2.0));
                if (cLat < 0) north += 10000000.0;
                string bands = "CDEFGHJKLMNPQRSTUVWXX";
                char bChar = bands[Math.Clamp((int)Math.Floor((cLat + 80.0) / 8.0), 0, bands.Length - 1)];
                char cChar = ColSets[(zone - 1) % 3][Math.Clamp((int)Math.Floor(east / 100000.0) - 1, 0, 7)];
                char rChar = RowSets[(zone - 1) % 2][Math.Clamp((int)Math.Floor(north / 100000.0) % 20, 0, 19)];
                int e5 = Math.Clamp((int)Math.Round(east) % 100000, 0, 99999);
                int n5 = Math.Clamp((int)Math.Round(north) % 100000, 0, 99999);
                return string.Format(CultureInfo.InvariantCulture, "{0:00}{1} {2}{3} {4:00000} {5:00000}", zone, bChar, cChar, rChar, e5, n5);
            }
            catch { return string.Format(CultureInfo.InvariantCulture, "{0:F5}, {1:F5}", lat, lon); }
        }

        public static string LatLonToUtm(double lat, double lon)
        {
            double cLat = Math.Clamp(lat, -80.0, 84.0);
            double nLon = ((lon + 180.0) % 360.0 + 360.0) % 360.0 - 180.0;
            int zone = (int)Math.Floor((nLon + 180.0) / 6.0) + 1;
            double latRad = cLat * Math.PI / 180.0, lonRad = nLon * Math.PI / 180.0;
            double lonOrig = ((zone - 1) * 6 - 180 + 3) * Math.PI / 180.0;
            double a = 6378137.0, eccSq = 0.00669438, k0 = 0.9996;
            double n = a / Math.Sqrt(1.0 - eccSq * Math.Sin(latRad) * Math.Sin(latRad));
            double A = Math.Cos(latRad) * (lonRad - lonOrig);
            double M = a * ((1.0 - eccSq / 4.0) * latRad - (3.0 * eccSq / 8.0) * Math.Sin(2.0 * latRad));
            double east = k0 * n * A + 500000.0;
            double north = k0 * (M + n * Math.Tan(latRad) * (A * A / 2.0));
            return string.Format(CultureInfo.InvariantCulture, "UTM {0}N {1:0}E {2:0}N", zone, east, north);
        }

        public static string LatLonToDms(double lat, double lon)
        {
            return ToDmsPart(lat, lat >= 0 ? 'N' : 'S') + " " + ToDmsPart(lon, lon >= 0 ? 'E' : 'W');
        }

        private static string ToDmsPart(double v, char h)
        {
            double a = Math.Abs(v);
            int d = (int)Math.Floor(a);
            double mf = (a - d) * 60.0;
            int m = (int)Math.Floor(mf);
            double s = (mf - m) * 60.0;
            return string.Format(CultureInfo.InvariantCulture, "{0}°{1:00}'{2:00.0}\"{3}", d, m, s, h);
        }

        // =========================================================================
        // EKSPORT 4-W-1 (.GPX NA GARMIN/SUUNTO ORAZ .KMZ DO ATAK)
        // =========================================================================

        public static string BuildGpxXmlForWatches(string operationName, IEnumerable<TacticalPoiMarker> pois)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<gpx version=\"1.1\" creator=\"Field TAK Hub v4.0 Tactical Map Studio\" xmlns=\"http://www.topografix.com/GPX/1/1\">");
            sb.AppendLine("  <metadata><name>" + WebUtility.HtmlEncode(operationName ?? "TAK-MAP") + "</name></metadata>");
            if (pois != null)
            {
                foreach (var p in pois)
                {
                    string mgrs = LatLonToMgrs(p.Lat, p.Lon);
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "  <wpt lat=\"{0:F6}\" lon=\"{1:F6}\"><name>{2}</name><desc>{3} | MGRS: {4}</desc><type>{3}</type></wpt>",
                        p.Lat, p.Lon, WebUtility.HtmlEncode(p.Name), WebUtility.HtmlEncode(p.Category), mgrs));
                }
            }
            sb.AppendLine("</gpx>");
            return sb.ToString();
        }

        public static void ExportKmzForAtak(string outputKmzPath, string operationName, IEnumerable<TacticalPoiMarker> pois)
        {
            var kml = new StringBuilder();
            kml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            kml.AppendLine("<kml xmlns=\"http://www.opengis.net/kml/2.2\"><Document>");
            kml.AppendLine("  <name>" + WebUtility.HtmlEncode(operationName ?? "FieldTakHub_AO") + "</name>");
            if (pois != null)
            {
                foreach (var p in pois)
                {
                    string mgrs = LatLonToMgrs(p.Lat, p.Lon);
                    kml.AppendLine("  <Placemark>");
                    kml.AppendLine("    <name>" + WebUtility.HtmlEncode(p.Name) + "</name>");
                    kml.AppendLine("    <description>[" + WebUtility.HtmlEncode(p.Category) + "] MGRS: " + mgrs + "</description>");
                    kml.AppendLine(string.Format(CultureInfo.InvariantCulture, "    <Point><coordinates>{0:F6},{1:F6},0</coordinates></Point>", p.Lon, p.Lat));
                    kml.AppendLine("  </Placemark>");
                }
            }
            kml.AppendLine("</Document></kml>");

            if (File.Exists(outputKmzPath)) File.Delete(outputKmzPath);
            using (var fs = new FileStream(outputKmzPath, FileMode.Create))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("doc.kml");
                using (var sw = new StreamWriter(entry.Open(), Encoding.UTF8))
                {
                    sw.Write(kml.ToString());
                }
            }
        }
    }
}
