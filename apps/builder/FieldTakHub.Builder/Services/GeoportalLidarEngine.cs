#pragma warning disable CS0414, CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Path = System.IO.Path;

namespace FieldTakHub.Builder.Services
{
    // =========================================================================
    // MODUŁ DWUKIERUNKOWEJ EDYCJI PLIKÓW .KMZ / .KML / .GPX + DOUGLAS-PEUCKER
    // =========================================================================
    internal static class TacticalVectorFileIoEngine
    {
        // Algorytm Ramer-Douglas-Peucker redukujący gęste ślady GPS (np. z zegarka Garmin / ATAK) do kluczowych wierzchołków edycyjnych
        public static List<(double Lat, double Lon)> SimplifyTrackPoints(List<(double Lat, double Lon)> points, double toleranceMeters = 8.0)
        {
            if (points == null || points.Count <= 4 || toleranceMeters <= 0.1)
                return points != null ? new List<(double Lat, double Lon)>(points) : new List<(double Lat, double Lon)>();

            int n = points.Count;
            bool[] keep = new bool[n];
            keep[0] = true;
            keep[n - 1] = true;

            double midLatRad = points[n / 2].Lat * Math.PI / 180.0;
            double metersPerDegLat = 111132.95;
            double metersPerDegLon = 111319.49 * Math.Cos(midLatRad);

            var stack = new Stack<(int First, int Last)>();
            stack.Push((0, n - 1));

            while (stack.Count > 0)
            {
                var (first, last) = stack.Pop();
                if (last - first <= 1) continue;

                double x1 = points[first].Lon * metersPerDegLon;
                double y1 = points[first].Lat * metersPerDegLat;
                double x2 = points[last].Lon * metersPerDegLon;
                double y2 = points[last].Lat * metersPerDegLat;

                double dx = x2 - x1;
                double dy = y2 - y1;
                double lenSq = dx * dx + dy * dy;

                double maxDist = -1.0;
                int index = first;

                for (int i = first + 1; i < last; i++)
                {
                    double px = points[i].Lon * metersPerDegLon;
                    double py = points[i].Lat * metersPerDegLat;
                    double dist;

                    if (lenSq < 1e-6)
                    {
                        double ex = px - x1, ey = py - y1;
                        dist = Math.Sqrt(ex * ex + ey * ey);
                    }
                    else
                    {
                        double t = Math.Clamp(((px - x1) * dx + (py - y1) * dy) / lenSq, 0.0, 1.0);
                        double projX = x1 + t * dx;
                        double projY = y1 + t * dy;
                        double ex = px - projX, ey = py - projY;
                        dist = Math.Sqrt(ex * ex + ey * ey);
                    }

                    if (dist > maxDist)
                    {
                        maxDist = dist;
                        index = i;
                    }
                }

                if (maxDist > toleranceMeters && index != first && index != last)
                {
                    keep[index] = true;
                    stack.Push((first, index));
                    stack.Push((index, last));
                }
            }

            var simplified = new List<(double Lat, double Lon)>();
            for (int i = 0; i < n; i++)
                if (keep[i]) simplified.Add(points[i]);

            return simplified;
        }

        // Konwertuje format koloru KML (aabbggrr) na standardowy #RRGGBB
        private static string KmlColorToHexRgb(string kmlColor, string defaultHex = "#2684FF")
        {
            if (string.IsNullOrWhiteSpace(kmlColor)) return defaultHex;
            string clean = kmlColor.Trim().TrimStart('#');
            if (clean.Length == 8)
            {
                string bb = clean.Substring(2, 2);
                string gg = clean.Substring(4, 2);
                string rr = clean.Substring(6, 2);
                if (rr.Equals("ff", StringComparison.OrdinalIgnoreCase) &&
                    gg.Equals("ff", StringComparison.OrdinalIgnoreCase) &&
                    bb.Equals("ff", StringComparison.OrdinalIgnoreCase))
                    return defaultHex;
                return $"#{rr}{gg}{bb}".ToUpperInvariant();
            }
            if (clean.Length == 6)
                return $"#{clean}".ToUpperInvariant();
            return defaultHex;
        }

        // Konwertuje #RRGGBB na format KML aabbggrr
        public static string HexRgbToKmlColor(string hexRgb, byte alpha = 235)
        {
            Color c = Color.FromRgb(38, 132, 255);
            try { if (!string.IsNullOrWhiteSpace(hexRgb)) c = (Color)ColorConverter.ConvertFromString(hexRgb.Trim()); } catch { }
            return $"{alpha:X2}{c.B:X2}{c.G:X2}{c.R:X2}";
        }

        private static List<(double Lat, double Lon)> ParseKmlCoordinatesString(string rawCoords)
        {
            var list = new List<(double Lat, double Lon)>();
            if (string.IsNullOrWhiteSpace(rawCoords)) return list;

            string[] tokens = rawCoords.Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string tok in tokens)
            {
                string[] parts = tok.Split(',');
                if (parts.Length >= 2 &&
                    double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double lon) &&
                    double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double lat))
                {
                    if (lat >= -85.5 && lat <= 85.5 && lon >= -180.5 && lon <= 180.5)
                        list.Add((lat, lon));
                }
            }

            // Usuń duplikat ostatniego punktu zamykającego wielokąt KML (edytor automatycznie domyka wielokąty AO i Sector)
            if (list.Count > 3)
            {
                var first = list[0];
                var last = list[list.Count - 1];
                if (Math.Abs(first.Lat - last.Lat) < 1e-6 && Math.Abs(first.Lon - last.Lon) < 1e-6)
                    list.RemoveAt(list.Count - 1);
            }
            return list;
        }

        private static (string CleanName, string SymbolCode, string Kind, string SuggestedColor) ClassifyImportedPointName(string rawName, string existingColor)
        {
            string name = string.IsNullOrWhiteSpace(rawName) ? "Punkt" : rawName.Trim();
            string symCode = "DOT";
            string kind = "Dot";
            string color = existingColor;

            // Sprawdź czy nazwa ma prefiks [BADGE] (np. [HQ] Sztab, [⛺] Respawn)
            var m = Regex.Match(name, @"^\[([^\]]+)\]\s*(.+)$");
            if (m.Success)
            {
                string badge = m.Groups[1].Value.Trim();
                name = m.Groups[2].Value.Trim();

                var foundSym = TacticalMapGeneratorView.NatoSymbols.FirstOrDefault(s =>
                    s.ShortBadge.Equals(badge, StringComparison.OrdinalIgnoreCase) ||
                    s.Code.Equals(badge, StringComparison.OrdinalIgnoreCase));

                if (foundSym != null)
                {
                    kind = "Marker";
                    symCode = foundSym.Code;
                }
                else if (badge.Equals("DOT", StringComparison.OrdinalIgnoreCase) || badge == "●")
                {
                    kind = "Dot";
                    symCode = "DOT";
                }
            }

            if (string.IsNullOrEmpty(color) || color == "#2684FF")
            {
                if (name.Contains("Infil", StringComparison.OrdinalIgnoreCase) || name.Contains("Resp", StringComparison.OrdinalIgnoreCase))
                    color = "#2ECC71";
                else if (name.Contains("Kod", StringComparison.OrdinalIgnoreCase) || name.Contains("Cel", StringComparison.OrdinalIgnoreCase) || name.Contains("OBJ", StringComparison.OrdinalIgnoreCase))
                    color = "#EB4034";
                else if (name.Contains("Update", StringComparison.OrdinalIgnoreCase) || name.Contains("CP", StringComparison.OrdinalIgnoreCase))
                    color = "#F5B82E";
                else
                    color = "#2684FF";
            }

            return (name, symCode, kind, color);
        }

        public static List<TacticalMapObjectItem> ParseKmlContentToObjects(string kmlXml, double simplifyToleranceMeters = 6.0)
        {
            var results = new List<TacticalMapObjectItem>();
            if (string.IsNullOrWhiteSpace(kmlXml)) return results;

            try
            {
                var doc = XDocument.Parse(kmlXml);
                var styleColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var styleMaps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var st in doc.Descendants().Where(e => e.Name.LocalName == "Style"))
                {
                    string id = st.Attribute("id")?.Value;
                    if (string.IsNullOrEmpty(id)) continue;

                    var colEl = st.Descendants().FirstOrDefault(e => e.Name.LocalName == "color");
                    if (colEl != null && !string.IsNullOrWhiteSpace(colEl.Value))
                        styleColors["#" + id] = KmlColorToHexRgb(colEl.Value);
                }

                foreach (var sm in doc.Descendants().Where(e => e.Name.LocalName == "StyleMap"))
                {
                    string id = sm.Attribute("id")?.Value;
                    if (string.IsNullOrEmpty(id)) continue;
                    var urlEl = sm.Descendants().FirstOrDefault(e => e.Name.LocalName == "styleUrl");
                    if (urlEl != null && !string.IsNullOrWhiteSpace(urlEl.Value))
                        styleMaps["#" + id] = urlEl.Value.Trim();
                }

                bool hasAoAlready = false;

                foreach (var pm in doc.Descendants().Where(e => e.Name.LocalName == "Placemark"))
                {
                    string rawName = pm.Elements().FirstOrDefault(e => e.Name.LocalName == "name")?.Value?.Trim() ?? $"Obiekt-{results.Count + 1}";
                    string styleUrl = pm.Elements().FirstOrDefault(e => e.Name.LocalName == "styleUrl")?.Value?.Trim() ?? "";

                    string resolvedColor = "";
                    if (!string.IsNullOrEmpty(styleUrl))
                    {
                        if (styleMaps.TryGetValue(styleUrl, out string mappedUrl))
                            styleUrl = mappedUrl;
                        if (styleColors.TryGetValue(styleUrl, out string sc))
                            resolvedColor = sc;
                    }

                    var inlineCol = pm.Descendants().FirstOrDefault(e => e.Name.LocalName == "color");
                    if (inlineCol != null && !string.IsNullOrWhiteSpace(inlineCol.Value))
                        resolvedColor = KmlColorToHexRgb(inlineCol.Value, resolvedColor);

                    // 1. Wielokąty (<Polygon>) -> AO lub Sector
                    var polyEls = pm.Descendants().Where(e => e.Name.LocalName == "Polygon").ToList();
                    if (polyEls.Count > 0)
                    {
                        foreach (var poly in polyEls)
                        {
                            var coordEl = poly.Descendants().FirstOrDefault(e => e.Name.LocalName == "coordinates");
                            var pts = ParseKmlCoordinatesString(coordEl?.Value);
                            if (pts.Count >= 3)
                            {
                                if (pts.Count > 60) pts = SimplifyTrackPoints(pts, simplifyToleranceMeters);

                                bool isAo = !hasAoAlready && (
                                    rawName.Contains("AO", StringComparison.OrdinalIgnoreCase) ||
                                    rawName.Contains("Teren", StringComparison.OrdinalIgnoreCase) ||
                                    rawName.Contains("Granica", StringComparison.OrdinalIgnoreCase) ||
                                    rawName.Contains("Border", StringComparison.OrdinalIgnoreCase));

                                if (isAo) hasAoAlready = true;

                                var item = new TacticalMapObjectItem
                                {
                                    Kind = isAo ? "AO" : "Sector",
                                    SymbolCode = isAo ? "AO" : "Sector",
                                    Name = rawName.Replace("[AO]", "").Replace("[SEK]", "").Trim(),
                                    ColorHex = !string.IsNullOrEmpty(resolvedColor) ? resolvedColor : (isAo ? "#FFFFFF" : "#F5B82E"),
                                    ShowLabel = !isAo,
                                    SizeMultiplier = 1.0
                                };
                                item.Points.AddRange(pts);
                                results.Add(item);
                            }
                        }
                        continue;
                    }

                    // 2. Linie i trasy (<LineString> / <gx:Track>) -> Route lub AO
                    var lineEls = pm.Descendants().Where(e => e.Name.LocalName == "LineString" || e.Name.LocalName == "Track").ToList();
                    if (lineEls.Count > 0)
                    {
                        foreach (var ln in lineEls)
                        {
                            List<(double Lat, double Lon)> pts;
                            var coordEl = ln.Descendants().FirstOrDefault(e => e.Name.LocalName == "coordinates");
                            if (coordEl != null)
                            {
                                pts = ParseKmlCoordinatesString(coordEl.Value);
                            }
                            else
                            {
                                pts = new List<(double Lat, double Lon)>();
                                foreach (var gxCoord in ln.Descendants().Where(e => e.Name.LocalName == "coord"))
                                {
                                    string[] p = gxCoord.Value.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                                    if (p.Length >= 2 &&
                                        double.TryParse(p[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double lo) &&
                                        double.TryParse(p[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double la))
                                        pts.Add((la, lo));
                                }
                            }

                            if (pts.Count >= 2)
                            {
                                if (pts.Count > 45) pts = SimplifyTrackPoints(pts, simplifyToleranceMeters);

                                bool isAoLine = !hasAoAlready && pts.Count >= 3 && (
                                    rawName.Contains("AO", StringComparison.OrdinalIgnoreCase) ||
                                    rawName.Contains("Granica Terenu", StringComparison.OrdinalIgnoreCase));
                                if (isAoLine) hasAoAlready = true;

                                var item = new TacticalMapObjectItem
                                {
                                    Kind = isAoLine ? "AO" : "Route",
                                    SymbolCode = isAoLine ? "AO" : "Route",
                                    Name = rawName.Replace("[AO]", "").Replace("[───]", "").Trim(),
                                    ColorHex = !string.IsNullOrEmpty(resolvedColor) ? resolvedColor : (isAoLine ? "#FFFFFF" : "#1FE0C5"),
                                    ShowLabel = !isAoLine,
                                    SizeMultiplier = 1.0
                                };
                                item.Points.AddRange(pts);
                                results.Add(item);
                            }
                        }
                        continue;
                    }

                    // 3. Punkty (<Point>) -> Marker lub Dot
                    var ptEl = pm.Descendants().FirstOrDefault(e => e.Name.LocalName == "Point");
                    if (ptEl != null)
                    {
                        var coordEl = ptEl.Descendants().FirstOrDefault(e => e.Name.LocalName == "coordinates");
                        var pts = ParseKmlCoordinatesString(coordEl?.Value);
                        if (pts.Count > 0)
                        {
                            var (cleanName, symCode, kind, suggestedCol) = ClassifyImportedPointName(rawName, resolvedColor);
                            var item = new TacticalMapObjectItem
                            {
                                Kind = kind,
                                SymbolCode = symCode,
                                Name = cleanName,
                                ColorHex = suggestedCol,
                                ShowLabel = true,
                                SizeMultiplier = cleanName.Contains("Infil", StringComparison.OrdinalIgnoreCase) || cleanName.Contains("Parking", StringComparison.OrdinalIgnoreCase) ? 1.38 : 1.0
                            };
                            item.Points.Add(pts[0]);
                            results.Add(item);
                        }
                    }
                }
            }
            catch { }

            return results;
        }

        public static List<TacticalMapObjectItem> ParseGpxContentToObjects(string gpxXml, double simplifyToleranceMeters = 8.0)
        {
            var results = new List<TacticalMapObjectItem>();
            if (string.IsNullOrWhiteSpace(gpxXml)) return results;

            try
            {
                var doc = XDocument.Parse(gpxXml);

                // 1. Punkty (<wpt>)
                foreach (var wpt in doc.Descendants().Where(e => e.Name.LocalName == "wpt"))
                {
                    if (double.TryParse(wpt.Attribute("lat")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double lat) &&
                        double.TryParse(wpt.Attribute("lon")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double lon))
                    {
                        string rawName = wpt.Elements().FirstOrDefault(e => e.Name.LocalName == "name")?.Value?.Trim() ?? $"WPT-{results.Count + 1}";
                        string colorAttr = wpt.Descendants().FirstOrDefault(e => e.Name.LocalName == "color")?.Value?.Trim() ?? "";
                        var (cleanName, symCode, kind, suggestedCol) = ClassifyImportedPointName(rawName, colorAttr);

                        var item = new TacticalMapObjectItem
                        {
                            Kind = kind,
                            SymbolCode = symCode,
                            Name = cleanName,
                            ColorHex = suggestedCol,
                            ShowLabel = true,
                            SizeMultiplier = cleanName.Contains("Infil", StringComparison.OrdinalIgnoreCase) || cleanName.Contains("Parking", StringComparison.OrdinalIgnoreCase) ? 1.38 : 1.0
                        };
                        item.Points.Add((lat, lon));
                        results.Add(item);
                    }
                }

                // 2. Trasy (<rte>) i zapisane w GPX strefy (<type>AO</type> / <type>Sector</type>)
                foreach (var rte in doc.Descendants().Where(e => e.Name.LocalName == "rte"))
                {
                    string rawName = rte.Elements().FirstOrDefault(e => e.Name.LocalName == "name")?.Value?.Trim() ?? $"Trasa-{results.Count + 1}";
                    string rteType = rte.Elements().FirstOrDefault(e => e.Name.LocalName == "type")?.Value?.Trim() ?? "";

                    var pts = new List<(double Lat, double Lon)>();
                    foreach (var rpt in rte.Elements().Where(e => e.Name.LocalName == "rtept"))
                    {
                        if (double.TryParse(rpt.Attribute("lat")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double lat) &&
                            double.TryParse(rpt.Attribute("lon")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double lon))
                            pts.Add((lat, lon));
                    }

                    if (pts.Count >= 2)
                    {
                        if (pts.Count > 50) pts = SimplifyTrackPoints(pts, simplifyToleranceMeters);

                        bool isAo = rteType.Equals("AO", StringComparison.OrdinalIgnoreCase) ||
                                    rawName.Contains("Granica Terenu", StringComparison.OrdinalIgnoreCase) ||
                                    rawName.StartsWith("[AO]", StringComparison.OrdinalIgnoreCase);
                        bool isSector = rteType.Equals("Sector", StringComparison.OrdinalIgnoreCase) ||
                                        rawName.StartsWith("[SEK]", StringComparison.OrdinalIgnoreCase);

                        string kind = isAo ? "AO" : (isSector ? "Sector" : "Route");
                        var item = new TacticalMapObjectItem
                        {
                            Kind = kind,
                            SymbolCode = kind,
                            Name = rawName.Replace("[AO]", "").Replace("[SEK]", "").Trim(),
                            ColorHex = isAo ? "#FFFFFF" : (isSector ? "#F5B82E" : "#1FE0C5"),
                            ShowLabel = !isAo,
                            SizeMultiplier = 1.0
                        };
                        item.Points.AddRange(pts);
                        results.Add(item);
                    }
                }

                // 3. Ślady GPS z zegarków Garmin / ATAK (<trk>) -> automatyczna redukcja punktów Douglas-Peucker
                foreach (var trk in doc.Descendants().Where(e => e.Name.LocalName == "trk"))
                {
                    string rawName = trk.Elements().FirstOrDefault(e => e.Name.LocalName == "name")?.Value?.Trim() ?? $"Ślad-GPS-{results.Count + 1}";
                    var pts = new List<(double Lat, double Lon)>();
                    foreach (var tpt in trk.Descendants().Where(e => e.Name.LocalName == "trkpt"))
                    {
                        if (double.TryParse(tpt.Attribute("lat")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double lat) &&
                            double.TryParse(tpt.Attribute("lon")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double lon))
                            pts.Add((lat, lon));
                    }

                    if (pts.Count >= 2)
                    {
                        var simplified = SimplifyTrackPoints(pts, simplifyToleranceMeters);
                        var item = new TacticalMapObjectItem
                        {
                            Kind = "Route",
                            SymbolCode = "Route",
                            Name = rawName,
                            ColorHex = "#2ECC71",
                            ShowLabel = true,
                            SizeMultiplier = 1.0
                        };
                        item.Points.AddRange(simplified);
                        results.Add(item);
                    }
                }
            }
            catch { }

            return results;
        }

        public static List<TacticalMapObjectItem> ImportAnyVectorFile(string filePath, double simplifyToleranceMeters, out string summaryText)
        {
            summaryText = "";
            var imported = new List<TacticalMapObjectItem>();
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return imported;

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            try
            {
                if (ext == ".kmz")
                {
                    using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
                    foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".kml", StringComparison.OrdinalIgnoreCase)))
                    {
                        using var sr = new StreamReader(entry.Open(), Encoding.UTF8);
                        string kmlTxt = sr.ReadToEnd();
                        imported.AddRange(ParseKmlContentToObjects(kmlTxt, simplifyToleranceMeters));
                    }
                }
                else if (ext == ".kml")
                {
                    string kmlTxt = File.ReadAllText(filePath, Encoding.UTF8);
                    imported.AddRange(ParseKmlContentToObjects(kmlTxt, simplifyToleranceMeters));
                }
                else if (ext == ".gpx")
                {
                    string gpxTxt = File.ReadAllText(filePath, Encoding.UTF8);
                    imported.AddRange(ParseGpxContentToObjects(gpxTxt, simplifyToleranceMeters));
                }

                int cntAo = imported.Count(o => o.Kind == "AO");
                int cntSec = imported.Count(o => o.Kind == "Sector");
                int cntRt = imported.Count(o => o.Kind == "Route");
                int cntPt = imported.Count(o => o.Kind == "Marker" || o.Kind == "Dot");
                summaryText = $"Wczytano z {Path.GetFileName(filePath)}: {imported.Count} obiektów (Strefy AO: {cntAo}, Sektory: {cntSec}, Trasy/Ślady: {cntRt}, Punkty: {cntPt})";
            }
            catch (Exception ex)
            {
                summaryText = $"Błąd odczytu pliku {Path.GetFileName(filePath)}: {ex.Message}";
            }

            return imported;
        }

        // Pełny zapis archiwum .KMZ zachowujący GroundOverlay + wszystkie wielokąty AO/Sektory + Trasy + Punkty
        public static void WriteFullKmzArchive(
            string kmzPath,
            string title,
            byte[] mapPngBytes,
            double north,
            double south,
            double east,
            double west,
            IEnumerable<TacticalMapObjectItem> objects)
        {
            if (File.Exists(kmzPath)) File.Delete(kmzPath);
            using var fs = new FileStream(kmzPath, FileMode.CreateNew, FileAccess.Write);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create, false, Encoding.UTF8);

            if (mapPngBytes != null && mapPngBytes.Length > 0)
            {
                var imgEntry = zip.CreateEntry("files/grg_overlay.png", CompressionLevel.Optimal);
                using var s = imgEntry.Open();
                s.Write(mapPngBytes, 0, mapPngBytes.Length);
            }

            var sb = new StringBuilder();
            sb.AppendLine(@"<?xml version=""1.0"" encoding=""UTF-8""?>");
            sb.AppendLine(@"<kml xmlns=""http://www.opengis.net/kml/2.2""><Document>");
            sb.AppendLine($"  <name>{SecurityElementText(title)}</name>");

            if (mapPngBytes != null && mapPngBytes.Length > 0)
            {
                sb.AppendLine($"  <GroundOverlay><name>{SecurityElementText(title)}</name><Icon><href>files/grg_overlay.png</href></Icon>");
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"    <LatLonBox><north>{north:0.000000}</north><south>{south:0.000000}</south><east>{east:0.000000}</east><west>{west:0.000000}</west></LatLonBox></GroundOverlay>"));
            }

            if (objects != null)
            {
                foreach (var o in objects.Where(x => x.Points != null && x.Points.Count > 0))
                {
                    string safeName = SecurityElementText(o.Name);
                    string kmlStroke = HexRgbToKmlColor(o.ColorHex, 240);
                    string kmlFill = HexRgbToKmlColor(o.ColorHex, o.Kind == "AO" ? (byte)35 : (byte)85);

                    if ((o.Kind == "AO" || o.Kind == "Sector") && o.Points.Count >= 3)
                    {
                        string prefix = o.Kind == "AO" ? "[AO] " : "[SEK] ";
                        sb.AppendLine("  <Placemark>");
                        sb.AppendLine($"    <name>{prefix}{safeName}</name>");
                        sb.AppendLine($"    <Style><LineStyle><color>{kmlStroke}</color><width>3</width></LineStyle><PolyStyle><color>{kmlFill}</color></PolyStyle></Style>");
                        sb.AppendLine("    <Polygon><outerBoundaryIs><LinearRing><coordinates>");
                        var coordBuilder = new StringBuilder("      ");
                        foreach (var pt in o.Points)
                            coordBuilder.Append(string.Create(CultureInfo.InvariantCulture, $"{pt.Lon:0.000000},{pt.Lat:0.000000},0 "));
                        var first = o.Points[0];
                        coordBuilder.Append(string.Create(CultureInfo.InvariantCulture, $"{first.Lon:0.000000},{first.Lat:0.000000},0"));
                        sb.AppendLine(coordBuilder.ToString());
                        sb.AppendLine("    </coordinates></LinearRing></outerBoundaryIs></Polygon>");
                        sb.AppendLine("  </Placemark>");
                    }
                    else if (o.Kind == "Route" && o.Points.Count >= 2)
                    {
                        sb.AppendLine("  <Placemark>");
                        sb.AppendLine($"    <name>{safeName}</name>");
                        sb.AppendLine($"    <Style><LineStyle><color>{kmlStroke}</color><width>3.2</width></LineStyle></Style>");
                        sb.AppendLine("    <LineString><tessellate>1</tessellate><coordinates>");
                        var coordBuilder = new StringBuilder("      ");
                        foreach (var pt in o.Points)
                            coordBuilder.Append(string.Create(CultureInfo.InvariantCulture, $"{pt.Lon:0.000000},{pt.Lat:0.000000},0 "));
                        sb.AppendLine(coordBuilder.ToString().TrimEnd());
                        sb.AppendLine("    </coordinates></LineString>");
                        sb.AppendLine("  </Placemark>");
                    }
                    else
                    {
                        var natoDef = TacticalMapGeneratorView.NatoSymbols.FirstOrDefault(n => n.Code == o.SymbolCode);
                        string desc = natoDef != null ? natoDef.NatoMeaning : o.Kind;
                        string badge = o.Kind == "Dot" ? "DOT" : (natoDef != null ? natoDef.ShortBadge : o.SymbolCode);
                        var (fullMgrs, _, kpInfo, _, _) = TacticalMapGeneratorView.FormatTacticalPointCoordinates(o.Points[0].Lat, o.Points[0].Lon);

                        sb.AppendLine("  <Placemark>");
                        sb.AppendLine($"    <name>[{SecurityElementText(badge)}] {safeName}</name>");
                        sb.AppendLine($"    <description>{SecurityElementText(desc)} | MGRS: {fullMgrs} ({kpInfo})</description>");
                        sb.AppendLine($"    <Style><IconStyle><color>{kmlStroke}</color></IconStyle><LabelStyle><color>{kmlStroke}</color></LabelStyle></Style>");
                        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"    <Point><coordinates>{o.Points[0].Lon:0.000000},{o.Points[0].Lat:0.000000},0</coordinates></Point>"));
                        sb.AppendLine("  </Placemark>");
                    }
                }
            }

            sb.AppendLine("</Document></kml>");
            var kmlEntry = zip.CreateEntry("doc.kml", CompressionLevel.Optimal);
            using var sw = new StreamWriter(kmlEntry.Open(), Encoding.UTF8);
            sw.Write(sb.ToString());
        }

        // Pełny zapis pliku .GPX zachowujący punkty (<wpt>), trasy (<rte>) oraz wielokąty AO/Sektory (<rte><type>AO</type>)
        public static void WriteFullGpxFile(string gpxPath, IEnumerable<TacticalMapObjectItem> objects)
        {
            var sb = new StringBuilder();
            sb.AppendLine(@"<?xml version=""1.0"" encoding=""UTF-8""?>");
            sb.AppendLine(@"<gpx version=""1.1"" creator=""Field TAK Hub Builder 2.4"" xmlns=""http://www.topografix.com/GPX/1/1"">");

            if (objects != null)
            {
                foreach (var o in objects.Where(x => x.Points != null && x.Points.Count > 0))
                {
                    string safeName = SecurityElementText(o.Name);
                    if (o.Points.Count == 1 && o.Kind != "AO" && o.Kind != "Sector" && o.Kind != "Route")
                    {
                        var natoDef = TacticalMapGeneratorView.NatoSymbols.FirstOrDefault(n => n.Code == o.SymbolCode);
                        string desc = natoDef != null ? natoDef.NatoMeaning : o.Kind;
                        string badge = o.Kind == "Dot" ? "DOT" : (natoDef != null ? natoDef.ShortBadge : o.SymbolCode);
                        var (fullMgrs, _, kpInfo, _, _) = TacticalMapGeneratorView.FormatTacticalPointCoordinates(o.Points[0].Lat, o.Points[0].Lon);
                        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  <wpt lat=\"{o.Points[0].Lat:0.000000}\" lon=\"{o.Points[0].Lon:0.000000}\"><name>[{SecurityElementText(badge)}] {safeName}</name><desc>{SecurityElementText(desc)} | MGRS: {fullMgrs} ({kpInfo})</desc><extensions><color>{o.ColorHex}</color></extensions></wpt>"));
                    }
                    else
                    {
                        sb.AppendLine($"  <rte><name>{safeName}</name><type>{o.Kind}</type>");
                        foreach (var pt in o.Points)
                            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"    <rtept lat=\"{pt.Lat:0.000000}\" lon=\"{pt.Lon:0.000000}\" />"));
                        sb.AppendLine("  </rte>");
                    }
                }
            }

            sb.AppendLine("</gpx>");
            File.WriteAllText(gpxPath, sb.ToString(), Encoding.UTF8);
        }

        private static string SecurityElementText(string raw) =>
            (raw ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }

    // =========================================================================
    // OKNO WSPARCIA TWÓRCY APLIKACJI (KOD QR + LINK BUYCOFFEE.TO JAK W ANDROID)
    // =========================================================================
    internal static class CreatorSupportModal
    {
        public static string BuyCoffeeUrl { get; set; } = "https://buycoffee.to/qbcol";

        private static string GetCustomUrlConfigPath()
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FieldTakHub");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "creator_support_url.txt");
            }
            catch { return ""; }
        }

        public static string GetEffectiveBuyCoffeeUrl()
        {
            try
            {
                string cfg = GetCustomUrlConfigPath();
                if (!string.IsNullOrEmpty(cfg) && File.Exists(cfg))
                {
                    string saved = File.ReadAllText(cfg, Encoding.UTF8).Trim();
                    if (saved.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        return saved;
                }
            }
            catch { }
            return string.IsNullOrWhiteSpace(BuyCoffeeUrl) || BuyCoffeeUrl.Contains("__BUYCOFFEE")
                ? "https://buycoffee.to/qbcol"
                : BuyCoffeeUrl;
        }

        public static void ShowCreatorSupportDialog(Window owner)
        {
            string activeUrl = GetEffectiveBuyCoffeeUrl();

            var dlg = new Window
            {
                Title = "☕ Wesprzyj twórcę — Field TAK Hub (BuyCoffee.to)",
                Width = 450,
                Height = 560,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                Background = TacticalConfigPanel.BrushCardBg,
                Foreground = Brushes.White,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow
            };

            var root = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };

            var badgeBorder = new Border
            {
                Background = TacticalConfigPanel.BrushItemBg,
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 12)
            };
            var hdrStack = new StackPanel();
            hdrStack.Children.Add(new TextBlock
            {
                Text = "☕ POSTAW KAWĘ TWÓRCY FIELD TAK HUB",
                Foreground = TacticalConfigPanel.BrushAccent,
                FontSize = 13.5,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            hdrStack.Children.Add(new TextBlock
            {
                Text = "Narzędzia Field TAK Hub (Windows Builder, Generator Map MGRS 1:1 oraz aplikacja Android) rozwijam z pasji dla społeczności MilSim i ASG. Jeśli program ułatwia Ci przygotowanie operacji, możesz wesprzeć dalszy rozwój projektu!",
                Foreground = TacticalConfigPanel.BrushMutedText,
                FontSize = 10.2,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0)
            });
            badgeBorder.Child = hdrStack;
            root.Children.Add(badgeBorder);

            var qrFrame = new Border
            {
                Width = 250,
                Height = 250,
                Background = Brushes.White,
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(2.5),
                CornerRadius = new CornerRadius(12),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 12),
                Padding = new Thickness(10)
            };

            var qrImage = new Image { Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(qrImage, BitmapScalingMode.NearestNeighbor);
            qrFrame.Child = qrImage;
            root.Children.Add(qrFrame);

            void UpdateQrForUrl(string targetUrl)
            {
                qrImage.Source = RenderFallbackQrBitmap(targetUrl);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        string cacheKey = $"QR_COFFEE_{targetUrl}";
                        byte[] cached = GeoportalLidarEngine.TryReadDiskCache(cacheKey);
                        if (cached == null)
                        {
                            string api = $"https://api.qrserver.com/v1/create-qr-code/?size=360x360&margin=8&data={Uri.EscapeDataString(targetUrl)}";
                            cached = await GeoportalLidarEngine.FastHttp.GetByteArrayAsync(api);
                            if (GeoportalLidarEngine.IsValidImageBytes(cached))
                                GeoportalLidarEngine.SaveDiskCacheAsync(cacheKey, cached);
                        }
                        if (GeoportalLidarEngine.IsValidImageBytes(cached) && Application.Current != null)
                        {
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                try
                                {
                                    var bmp = new BitmapImage();
                                    using var ms = new MemoryStream(cached);
                                    bmp.BeginInit();
                                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                                    bmp.StreamSource = ms;
                                    bmp.EndInit();
                                    bmp.Freeze();
                                    qrImage.Source = bmp;
                                }
                                catch { }
                            });
                        }
                    }
                    catch { }
                });
            }

            UpdateQrForUrl(activeUrl);

            root.Children.Add(new TextBlock
            {
                Text = "Zeskanuj kod QR aparatem telefonu lub kliknij poniżej:",
                Foreground = TacticalConfigPanel.BrushMutedText,
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6)
            });

            var urlBox = new TextBox
            {
                Text = activeUrl,
                Height = 26,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = TacticalConfigPanel.BrushInputBg,
                Foreground = TacticalConfigPanel.BrushAccent,
                BorderBrush = TacticalConfigPanel.BrushInputBorder,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 10)
            };
            urlBox.TextChanged += (_, __) =>
            {
                string trimmed = urlBox.Text.Trim();
                if (trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    activeUrl = trimmed;
                    BuyCoffeeUrl = trimmed;
                    try
                    {
                        string cfg = GetCustomUrlConfigPath();
                        if (!string.IsNullOrEmpty(cfg)) File.WriteAllText(cfg, trimmed, Encoding.UTF8);
                    }
                    catch { }
                    UpdateQrForUrl(trimmed);
                }
            };
            root.Children.Add(urlBox);

            var btnOpenBrowser = new Button
            {
                Content = "☕ Otwórz buycoffee.to w przeglądarce",
                Height = 34,
                Background = new SolidColorBrush(Color.FromRgb(245, 184, 46)),
                Foreground = TacticalConfigPanel.BrushDarkFg,
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.Bold,
                FontSize = 11.5,
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 0, 8)
            };
            btnOpenBrowser.Click += (_, __) =>
            {
                try { Process.Start(new ProcessStartInfo { FileName = activeUrl, UseShellExecute = true }); } catch { }
            };
            root.Children.Add(btnOpenBrowser);

            var bottomRow = new Grid();
            bottomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var btnCopy = new Button
            {
                Content = "📋 Kopiuj link do schowka",
                Height = 28,
                Margin = new Thickness(0, 0, 5, 0),
                Background = TacticalConfigPanel.BrushItemBg,
                Foreground = Brushes.White,
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(1),
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand
            };
            btnCopy.Click += (_, __) =>
            {
                try
                {
                    Clipboard.SetText(activeUrl);
                    btnCopy.Content = "✓ Skopiowano link!";
                }
                catch { }
            };

            var btnClose = new Button
            {
                Content = "Zamknij",
                Height = 28,
                Margin = new Thickness(5, 0, 0, 0),
                Background = TacticalConfigPanel.BrushInputBg,
                Foreground = Brushes.White,
                BorderBrush = TacticalConfigPanel.BrushTileBorder,
                BorderThickness = new Thickness(1),
                FontSize = 10.5,
                Cursor = Cursors.Hand
            };
            btnClose.Click += (_, __) => dlg.Close();

            Grid.SetColumn(btnCopy, 0);
            Grid.SetColumn(btnClose, 1);
            bottomRow.Children.Add(btnCopy);
            bottomRow.Children.Add(btnClose);
            root.Children.Add(bottomRow);

            dlg.Content = root;
            dlg.ShowDialog();
        }

        private static BitmapSource RenderFallbackQrBitmap(string payload)
        {
            const int sz = 300;
            const int modules = 29;
            double cell = (double)sz / modules;
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload ?? "https://buycoffee.to/qbcol"));

            var dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, sz, sz));
                bool IsFinder(int r, int c) => (r < 7 && c < 7) || (r < 7 && c >= modules - 7) || (r >= modules - 7 && c < 7);
                void DrawFinder(int rOff, int cOff)
                {
                    for (int r = 0; r < 7; r++)
                        for (int c = 0; c < 7; c++)
                            if ((r == 0 || r == 6 || c == 0 || c == 6) || (r >= 2 && r <= 4 && c >= 2 && c <= 4))
                                dc.DrawRectangle(Brushes.Black, null, new Rect((cOff + c) * cell, (rOff + r) * cell, cell + 0.4, cell + 0.4));
                }
                DrawFinder(0, 0); DrawFinder(0, modules - 7); DrawFinder(modules - 7, 0);
                int bitIdx = 0;
                for (int r = 0; r < modules; r++)
                    for (int c = 0; c < modules; c++)
                        if (!IsFinder(r, c) && r != 7 && c != 7)
                        {
                            if (((hash[(bitIdx / 8) % hash.Length] >> (bitIdx % 8)) & 1) == 1)
                                dc.DrawRectangle(Brushes.Black, null, new Rect(c * cell, r * cell, cell + 0.4, cell + 0.4));
                            bitIdx++;
                        }
            }
            var rtb = new RenderTargetBitmap(sz, sz, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }
    }

    internal static class UtmGrid2DProjector
    {
        public static void LatLonToUtm(double lat, double lon, int forcedZone, out double easting, out double northing)
        {
            const double a = 6378137.0;
            const double eccSquared = 0.00669438;
            const double k0 = 0.9996;

            double latRad = lat * Math.PI / 180.0;
            double lonRad = lon * Math.PI / 180.0;
            double lonOriginRad = ((forcedZone - 1) * 6 - 180 + 3) * Math.PI / 180.0;

            double eccPrimeSquared = eccSquared / (1.0 - eccSquared);
            double N = a / Math.Sqrt(1.0 - eccSquared * Math.Sin(latRad) * Math.Sin(latRad));
            double T = Math.Tan(latRad) * Math.Tan(latRad);
            double C = eccPrimeSquared * Math.Cos(latRad) * Math.Cos(latRad);
            double A = Math.Cos(latRad) * (lonRad - lonOriginRad);

            double M = a * ((1.0 - eccSquared / 4.0 - 3.0 * eccSquared * eccSquared / 64.0) * latRad
                - (3.0 * eccSquared / 8.0 + 3.0 * eccSquared * eccSquared / 32.0) * Math.Sin(2.0 * latRad)
                + (15.0 * eccSquared * eccSquared / 256.0) * Math.Sin(4.0 * latRad));

            easting = k0 * N * (A + (1.0 - T + C) * A * A * A / 6.0
                + (5.0 - 18.0 * T + T * T + 72.0 * C - 58.0 * eccPrimeSquared) * Math.Pow(A, 5) / 120.0) + 500000.0;

            northing = k0 * (M + N * Math.Tan(latRad) * (A * A / 2.0
                + (5.0 - T + 9.0 * C + 4.0 * C * C) * Math.Pow(A, 4) / 24.0
                + (61.0 - 58.0 * T + T * T + 600.0 * C - 330.0 * eccPrimeSquared) * Math.Pow(A, 6) / 720.0));
            if (lat < 0) northing += 10000000.0;
        }

        public static void UtmToLatLon(double easting, double northing, int zone, out double lat, out double lon)
        {
            const double a = 6378137.0;
            const double eccSquared = 0.00669438;
            const double k0 = 0.9996;

            double eccPrimeSquared = eccSquared / (1.0 - eccSquared);
            double e1 = (1.0 - Math.Sqrt(1.0 - eccSquared)) / (1.0 + Math.Sqrt(1.0 - eccSquared));

            double x = easting - 500000.0;
            double y = northing;

            double lonOrigin = (zone - 1) * 6 - 180 + 3;
            double M = y / k0;
            double mu = M / (a * (1.0 - eccSquared / 4.0 - 3.0 * eccSquared * eccSquared / 64.0 - 5.0 * Math.Pow(eccSquared, 3) / 256.0));

            double phi1Rad = mu
                + (3.0 * e1 / 2.0 - 27.0 * Math.Pow(e1, 3) / 32.0) * Math.Sin(2.0 * mu)
                + (21.0 * e1 * e1 / 16.0 - 55.0 * Math.Pow(e1, 4) / 32.0) * Math.Sin(4.0 * mu)
                + (151.0 * Math.Pow(e1, 3) / 96.0) * Math.Sin(6.0 * mu);

            double N1 = a / Math.Sqrt(1.0 - eccSquared * Math.Sin(phi1Rad) * Math.Sin(phi1Rad));
            double T1 = Math.Tan(phi1Rad) * Math.Tan(phi1Rad);
            double C1 = eccPrimeSquared * Math.Cos(phi1Rad) * Math.Cos(phi1Rad);
            double R1 = a * (1.0 - eccSquared) / Math.Pow(1.0 - eccSquared * Math.Sin(phi1Rad) * Math.Sin(phi1Rad), 1.5);
            double D = x / (N1 * k0);

            double latRad = phi1Rad - (N1 * Math.Tan(phi1Rad) / R1) * (D * D / 2.0
                - (5.0 + 3.0 * T1 + 10.0 * C1 - 4.0 * C1 * C1 - 9.0 * eccPrimeSquared) * Math.Pow(D, 4) / 24.0
                + (61.0 + 90.0 * T1 + 298.0 * C1 + 45.0 * T1 * T1 - 252.0 * eccPrimeSquared - 3.0 * C1 * C1) * Math.Pow(D, 6) / 720.0);

            double lonRad = (D
                - (1.0 + 2.0 * T1 + C1) * Math.Pow(D, 3) / 6.0
                + (5.0 - 2.0 * C1 + 28.0 * T1 - 3.0 * C1 * C1 + 8.0 * eccPrimeSquared + 24.0 * T1 * T1) * Math.Pow(D, 5) / 120.0) / Math.Cos(phi1Rad);

            lat = latRad * 180.0 / Math.PI;
            lon = lonOrigin + lonRad * 180.0 / Math.PI;
        }

        public static void ComputeSheetUtmEnvelope(
            Func<double, double, (double Lat, double Lon)> screenToLatLon,
            double viewW, double viewH, int utmZone, int stepMeters,
            out double firstEasting, out double lastEasting, out double firstNorthing, out double lastNorthing)
        {
            var nw = screenToLatLon(0, 0);
            var ne = screenToLatLon(viewW, 0);
            var sw = screenToLatLon(0, viewH);
            var se = screenToLatLon(viewW, viewH);

            LatLonToUtm(nw.Lat, nw.Lon, utmZone, out double eNW, out double nNW);
            LatLonToUtm(ne.Lat, ne.Lon, utmZone, out double eNE, out double nNE);
            LatLonToUtm(sw.Lat, sw.Lon, utmZone, out double eSW, out double nSW);
            LatLonToUtm(se.Lat, se.Lon, utmZone, out double eSE, out double nSE);

            double minE = Math.Min(Math.Min(eNW, eNE), Math.Min(eSW, eSE));
            double maxE = Math.Max(Math.Max(eNW, eNE), Math.Max(eSW, eSE));
            double minN = Math.Min(Math.Min(nNW, nNE), Math.Min(nSW, nSE));
            double maxN = Math.Max(Math.Max(nNW, nNE), Math.Max(nSW, nSE));

            int pad = Math.Max(200, stepMeters * 5);
            firstEasting  = Math.Floor((minE - pad) / stepMeters) * stepMeters;
            lastEasting   = Math.Ceiling((maxE + pad) / stepMeters) * stepMeters;
            firstNorthing = Math.Floor((minN - pad) / stepMeters) * stepMeters;
            lastNorthing  = Math.Ceiling((maxN + pad) / stepMeters) * stepMeters;
        }

        public static bool TryGetVerticalLineLabelPoint(Point pTop, Point pBot, double targetY, double canvasW, out double intersectX)
        {
            intersectX = pTop.X;
            double dy = pBot.Y - pTop.Y;
            if (Math.Abs(dy) > 0.01) intersectX = pTop.X + ((targetY - pTop.Y) / dy) * (pBot.X - pTop.X);
            return intersectX >= 10.0 && intersectX <= canvasW - 10.0;
        }

        public static bool TryGetHorizontalLineLabelPoint(Point pLeft, Point pRight, double targetX, double canvasH, out double intersectY)
        {
            intersectY = pLeft.Y;
            double dx = pRight.X - pLeft.X;
            if (Math.Abs(dx) > 0.01) intersectY = pLeft.Y + ((targetX - pLeft.X) / dx) * (pRight.Y - pLeft.Y);
            return intersectY >= 10.0 && intersectY <= canvasH - 10.0;
        }
    }

    internal static class GeoportalLidarEngine
    {
        private static readonly ConcurrentDictionary<string, byte[]> RawBgraSectorCache = new ConcurrentDictionary<string, byte[]>();
        private static readonly SemaphoreSlim LidarThrottle = new SemaphoreSlim(4, 4);
        private static readonly string DiskCacheDir = InitializeDiskCacheDirectory();
        private static volatile int _preferredLidarCandidateIdx = 0;

        public static HttpClient FastHttp { get; private set; } = CreateDedicatedHttpClient(9);
        public static HttpClient LidarHttp { get; private set; } = CreateDedicatedHttpClient(8);
        public static string LastLidarDiagnosticStatus { get; private set; } = "Gotowy";
        public static double ActiveBrightnessBoost { get; set; } = 0.0;

        private static string InitializeDiskCacheDirectory()
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FieldTakHub",
                    "GeoCacheV26");
                Directory.CreateDirectory(dir);
                return dir;
            }
            catch { return ""; }
        }

        private static HttpClient CreateDedicatedHttpClient(int timeoutSeconds)
        {
            try
            {
                var handler = new SocketsHttpHandler
                {
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                    PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                    PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
                    MaxConnectionsPerServer = 12,
                    EnableMultipleHttp2Connections = false,
                    ConnectTimeout = TimeSpan.FromSeconds(3.5)
                };
                handler.SslOptions.RemoteCertificateValidationCallback = (s, c, ch, e) => true;

                var c = new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromSeconds(timeoutSeconds),
                    DefaultRequestVersion = HttpVersion.Version11,
                    DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
                };
                c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
                c.DefaultRequestHeaders.Referrer = new Uri("https://mapy.geoportal.gov.pl/");
                c.DefaultRequestHeaders.Accept.ParseAdd("image/png,image/jpeg,image/*;q=0.9,*/*;q=0.5");
                return c;
            }
            catch
            {
                return new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
            }
        }

        public static void ResetHttpConnectionPool()
        {
            try
            {
                FastHttp = CreateDedicatedHttpClient(9);
                LidarHttp = CreateDedicatedHttpClient(8);
            }
            catch { }
        }

        public static byte[] TryReadDiskCache(string urlOrKey)
        {
            if (string.IsNullOrEmpty(DiskCacheDir) || string.IsNullOrEmpty(urlOrKey)) return null;
            try
            {
                string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(urlOrKey))).Substring(0, 28);
                string path = Path.Combine(DiskCacheDir, hash + ".bin");
                if (File.Exists(path))
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    if (IsValidImageBytes(bytes)) return bytes;
                }
            }
            catch { }
            return null;
        }

        public static void SaveDiskCacheAsync(string urlOrKey, byte[] validImageBytes)
        {
            if (string.IsNullOrEmpty(DiskCacheDir) || string.IsNullOrEmpty(urlOrKey) || !IsValidImageBytes(validImageBytes))
                return;

            _ = Task.Run(() =>
            {
                try
                {
                    string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(urlOrKey))).Substring(0, 28);
                    string path = Path.Combine(DiskCacheDir, hash + ".bin");
                    if (!File.Exists(path))
                        File.WriteAllBytes(path, validImageBytes);
                }
                catch { }
            });
        }

        public static void ClearCache(bool clearDiskCacheToo = false)
        {
            RawBgraSectorCache.Clear();
            ResetHttpConnectionPool();
            if (clearDiskCacheToo && !string.IsNullOrEmpty(DiskCacheDir) && Directory.Exists(DiskCacheDir))
            {
                _ = Task.Run(() =>
                {
                    try
                    {
                        foreach (var f in Directory.GetFiles(DiskCacheDir, "*.bin"))
                        {
                            try { File.Delete(f); } catch { }
                        }
                    }
                    catch { }
                });
            }
        }

        public static BitmapSource ApplyGammaAndBrightnessToBitmap(BitmapSource srcBmp, double boostAmount)
        {
            if (srcBmp == null || boostAmount <= 0.01) return srcBmp;
            try
            {
                BitmapSource frame = srcBmp;
                if (frame.Format != PixelFormats.Bgra32 && frame.Format != PixelFormats.Pbgra32)
                {
                    var conv = new FormatConvertedBitmap();
                    conv.BeginInit();
                    conv.Source = frame;
                    conv.DestinationFormat = PixelFormats.Bgra32;
                    conv.EndInit();
                    conv.Freeze();
                    frame = conv;
                }

                int w = frame.PixelWidth, h = frame.PixelHeight, stride = w * 4;
                byte[] px = new byte[h * stride];
                frame.CopyPixels(px, stride, 0);

                byte[] lut = BuildGammaShadowLiftLut(boostAmount);
                int len = px.Length;
                for (int i = 0; i + 3 < len; i += 4)
                {
                    if (px[i + 3] > 0)
                    {
                        px[i]     = lut[px[i]];
                        px[i + 1] = lut[px[i + 1]];
                        px[i + 2] = lut[px[i + 2]];
                    }
                }

                var outBmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, stride);
                outBmp.Freeze();
                return outBmp;
            }
            catch { return srcBmp; }
        }

        public static byte[] BuildGammaShadowLiftLut(double boostAmount)
        {
            double clamped = Math.Clamp(boostAmount, 0.0, 1.0);
            double gamma = 1.0 / (1.0 + clamped * 1.35);
            double blackLift = clamped * 36.0;

            byte[] lut = new byte[256];
            for (int v = 0; v < 256; v++)
            {
                double norm = v / 255.0;
                double lifted = Math.Pow(norm, gamma) * (255.0 - blackLift) + blackLift;
                lut[v] = (byte)Math.Clamp((int)Math.Round(lifted), 0, 255);
            }
            return lut;
        }

        public static (double Easting, double Northing) LatLonToEpsg2180(double lat, double lon)
        {
            const double a = 6378137.0;
            const double eccSquared = 0.00669438002290;
            const double k0 = 0.9993;
            const double lonOriginRad = 19.0 * Math.PI / 180.0;

            double latRad = lat * Math.PI / 180.0;
            double lonRad = lon * Math.PI / 180.0;

            double eccPrimeSquared = eccSquared / (1.0 - eccSquared);
            double N = a / Math.Sqrt(1.0 - eccSquared * Math.Sin(latRad) * Math.Sin(latRad));
            double T = Math.Tan(latRad) * Math.Tan(latRad);
            double C = eccPrimeSquared * Math.Cos(latRad) * Math.Cos(latRad);
            double A = Math.Cos(latRad) * (lonRad - lonOriginRad);

            double M = a * ((1.0 - eccSquared / 4.0 - 3.0 * eccSquared * eccSquared / 64.0) * latRad
                - (3.0 * eccSquared / 8.0 + 3.0 * eccSquared * eccSquared / 32.0) * Math.Sin(2.0 * latRad)
                + (15.0 * eccSquared * eccSquared / 256.0) * Math.Sin(4.0 * latRad));

            double easting = k0 * N * (A + (1.0 - T + C) * A * A * A / 6.0
                + (5.0 - 18.0 * T + T * T + 72.0 * C - 58.0 * eccPrimeSquared) * Math.Pow(A, 5) / 120.0) + 500000.0;

            double northing = k0 * (M + N * Math.Tan(latRad) * (A * A / 2.0
                + (5.0 - T + 9.0 * C + 4.0 * C * C) * Math.Pow(A, 4) / 24.0
                + (61.0 - 58.0 * T + T * T + 600.0 * C - 330.0 * eccPrimeSquared) * Math.Pow(A, 6) / 720.0)) - 5300000.0;

            return (easting, northing);
        }

        public static (double X, double Y) LatLonToEpsg3857(double lat, double lon) =>
            (lon * 20037508.34 / 180.0, Math.Log(Math.Tan((90.0 + lat) * Math.PI / 360.0)) / (Math.PI / 180.0) * 20037508.34 / 180.0);

        public static bool IsValidImageBytes(byte[] data)
        {
            if (data == null || data.Length < 128) return false;
            if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return true;
            if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) return true;
            if (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46) return true;
            if (data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46) return true;
            return false;
        }

        private static bool TryDecodeToBgra32Pixels(byte[] rawBytes, int targetW, int targetH, out byte[] bgraPixels)
        {
            bgraPixels = null;
            if (!IsValidImageBytes(rawBytes)) return false;

            try
            {
                using var ms = new MemoryStream(rawBytes);
                var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count == 0) return false;

                BitmapSource frame = decoder.Frames[0];
                if (frame.PixelWidth < 8 || frame.PixelHeight < 8) return false;

                if (frame.Format != PixelFormats.Bgra32 && frame.Format != PixelFormats.Pbgra32)
                {
                    var conv = new FormatConvertedBitmap();
                    conv.BeginInit();
                    conv.Source = frame;
                    conv.DestinationFormat = PixelFormats.Bgra32;
                    conv.EndInit();
                    conv.Freeze();
                    frame = conv;
                }

                if (frame.PixelWidth != targetW || frame.PixelHeight != targetH)
                {
                    double sx = (double)targetW / frame.PixelWidth;
                    double sy = (double)targetH / frame.PixelHeight;
                    var tb = new TransformedBitmap();
                    tb.BeginInit();
                    tb.Source = frame;
                    tb.Transform = new ScaleTransform(sx, sy);
                    tb.EndInit();
                    tb.Freeze();
                    frame = tb;
                }

                int stride = targetW * 4;
                bgraPixels = new byte[targetH * stride];
                frame.CopyPixels(bgraPixels, stride, 0);
                return true;
            }
            catch { return false; }
        }

        private static bool HasValidHillshadeSignal(byte[] bgraPixels, int width, int height)
        {
            if (bgraPixels == null || width < 8 || height < 8) return false;
            int stride = width * 4;
            int grid = 18;
            int emptyOrWhiteCount = 0, total = 0;
            double sumLum = 0, sumLumSq = 0;
            byte minLum = 255, maxLum = 0;

            for (int r = 1; r < grid; r++)
            {
                int y = (height * r) / grid;
                for (int c = 1; c < grid; c++)
                {
                    int x = (width * c) / grid;
                    int idx = y * stride + x * 4;
                    if (idx + 3 < bgraPixels.Length)
                    {
                        byte b = bgraPixels[idx], g = bgraPixels[idx + 1], red = bgraPixels[idx + 2], a = bgraPixels[idx + 3];
                        total++;
                        if (a < 10 || (red >= 253 && g >= 253 && b >= 253))
                            emptyOrWhiteCount++;

                        byte lum = (a < 10) ? (byte)255 : (byte)((red * 77 + g * 150 + b * 29) >> 8);
                        if (lum < minLum) minLum = lum;
                        if (lum > maxLum) maxLum = lum;
                        sumLum += lum;
                        sumLumSq += lum * lum;
                    }
                }
            }

            if (total == 0) return false;
            double emptyRatio = (double)emptyOrWhiteCount / total;
            double mean = sumLum / total;
            double variance = Math.Max(0.0, (sumLumSq / total) - (mean * mean));

            if (emptyRatio >= 0.98) return false;
            if ((maxLum - minLum) < 2 && mean > 245.0) return false;
            if (variance < 0.15 && mean > 245.0) return false;
            return true;
        }

        public static bool IsEmptyOrFlatLidarBitmap(BitmapSource bmp)
        {
            if (bmp == null || bmp.PixelWidth < 8 || bmp.PixelHeight < 8) return true;
            try
            {
                BitmapSource src = bmp;
                if (src.Format != PixelFormats.Bgra32 && src.Format != PixelFormats.Pbgra32)
                {
                    var conv = new FormatConvertedBitmap();
                    conv.BeginInit();
                    conv.Source = src;
                    conv.DestinationFormat = PixelFormats.Bgra32;
                    conv.EndInit();
                    conv.Freeze();
                    src = conv;
                }
                int w = src.PixelWidth, h = src.PixelHeight;
                byte[] px = new byte[h * w * 4];
                src.CopyPixels(px, w * 4, 0);
                return !HasValidHillshadeSignal(px, w, h);
            }
            catch { return false; }
        }

        public static bool HasPartialWhiteNoDataZone(BitmapSource bmp, double whiteRatioThreshold = 0.08)
        {
            if (bmp == null || bmp.PixelWidth < 8 || bmp.PixelHeight < 8) return true;
            try
            {
                BitmapSource src = bmp;
                if (src.Format != PixelFormats.Bgra32 && src.Format != PixelFormats.Pbgra32)
                {
                    var conv = new FormatConvertedBitmap();
                    conv.BeginInit();
                    conv.Source = src;
                    conv.DestinationFormat = PixelFormats.Bgra32;
                    conv.EndInit();
                    conv.Freeze();
                    src = conv;
                }
                int w = src.PixelWidth, h = src.PixelHeight, stride = w * 4;
                byte[] px = new byte[h * stride];
                src.CopyPixels(px, stride, 0);

                int grid = 22, whiteOrEmpty = 0, total = 0;
                for (int r = 1; r < grid; r++)
                {
                    int y = (h * r) / grid;
                    for (int c = 1; c < grid; c++)
                    {
                        int x = (w * c) / grid;
                        int idx = y * stride + x * 4;
                        if (idx + 3 < px.Length)
                        {
                            total++;
                            byte b = px[idx], g = px[idx + 1], red = px[idx + 2], a = px[idx + 3];
                            if (a < 15 || (red >= 252 && g >= 252 && b >= 252))
                                whiteOrEmpty++;
                        }
                    }
                }
                return total == 0 || ((double)whiteOrEmpty / total) >= whiteRatioThreshold;
            }
            catch { return true; }
        }

        public static BitmapSource MergeHighResWithStandardFallback(BitmapSource hiResBmp, BitmapSource stdBmp)
        {
            if (hiResBmp == null) return stdBmp;
            if (stdBmp == null) return hiResBmp;
            try
            {
                int w = stdBmp.PixelWidth, h = stdBmp.PixelHeight, stride = w * 4;
                BitmapSource sFrame = stdBmp;
                if (sFrame.Format != PixelFormats.Bgra32)
                {
                    var c = new FormatConvertedBitmap(); c.BeginInit(); c.Source = sFrame; c.DestinationFormat = PixelFormats.Bgra32; c.EndInit(); c.Freeze(); sFrame = c;
                }

                BitmapSource hFrame = hiResBmp;
                if (hFrame.PixelWidth != w || hFrame.PixelHeight != h)
                {
                    var tb = new TransformedBitmap(); tb.BeginInit(); tb.Source = hFrame; tb.Transform = new ScaleTransform((double)w / hFrame.PixelWidth, (double)h / hFrame.PixelHeight); tb.EndInit(); tb.Freeze(); hFrame = tb;
                }
                if (hFrame.Format != PixelFormats.Bgra32)
                {
                    var c = new FormatConvertedBitmap(); c.BeginInit(); c.Source = hFrame; c.DestinationFormat = PixelFormats.Bgra32; c.EndInit(); c.Freeze(); hFrame = c;
                }

                byte[] stdPx = new byte[h * stride];
                byte[] hiPx = new byte[h * stride];
                sFrame.CopyPixels(stdPx, stride, 0);
                hFrame.CopyPixels(hiPx, stride, 0);

                for (int i = 0; i + 3 < hiPx.Length; i += 4)
                {
                    byte b = hiPx[i], g = hiPx[i + 1], r = hiPx[i + 2], a = hiPx[i + 3];
                    if (a >= 20 && !(r >= 250 && g >= 250 && b >= 250))
                    {
                        stdPx[i] = b;
                        stdPx[i + 1] = g;
                        stdPx[i + 2] = r;
                        stdPx[i + 3] = 255;
                    }
                }

                var merged = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, stdPx, stride);
                merged.Freeze();
                return merged;
            }
            catch { return stdBmp; }
        }

        private static void ApplyHillshadeContrastAndAlphaInPlace(byte[] bgraPixels, int width, int height, bool asTransparentOverlay)
        {
            if (bgraPixels == null || bgraPixels.Length < 16) return;

            int[] hist = new int[256];
            int validSamples = 0;
            int step = Math.Max(1, (bgraPixels.Length / 4) / 6000) * 4;

            for (int i = 0; i + 3 < bgraPixels.Length; i += step)
            {
                if (bgraPixels[i + 3] >= 15)
                {
                    int l = (bgraPixels[i + 2] * 77 + bgraPixels[i + 1] * 150 + bgraPixels[i] * 29) >> 8;
                    if (l < 254)
                    {
                        hist[l]++;
                        validSamples++;
                    }
                }
            }

            int lowClip = 40, highClip = 235, medianLum = 195;
            if (validSamples > 30)
            {
                int acc = 0;
                int lowTarget = (int)(validSamples * 0.015);
                int medTarget = (int)(validSamples * 0.50);
                int highTarget = (int)(validSamples * 0.985);

                for (int l = 0; l < 256; l++)
                {
                    acc += hist[l];
                    if (acc >= lowTarget && lowClip == 40) lowClip = l;
                    if (acc >= medTarget && medianLum == 195) medianLum = l;
                    if (acc >= highTarget) { highClip = l; break; }
                }
            }

            if (highClip - lowClip < 18)
            {
                lowClip = Math.Max(0, medianLum - 18);
                highClip = Math.Min(255, medianLum + 18);
            }
            double span = Math.Max(10.0, highClip - lowClip);

            byte[] lutB = new byte[256];
            byte[] lutG = new byte[256];
            byte[] lutR = new byte[256];
            byte[] lutA = new byte[256];

            byte[] gammaLift = ActiveBrightnessBoost > 0.01 ? BuildGammaShadowLiftLut(ActiveBrightnessBoost) : null;

            for (int rawLum = 0; rawLum < 256; rawLum++)
            {
                int stretched = (int)Math.Clamp(((rawLum - lowClip) / span) * 255.0, 0.0, 255.0);
                if (!asTransparentOverlay)
                {
                    byte v = (byte)stretched;
                    if (gammaLift != null) v = gammaLift[v];
                    lutB[rawLum] = v; lutG[rawLum] = v; lutR[rawLum] = v; lutA[rawLum] = 255;
                }
                else
                {
                    int deltaFromNeutral = stretched - 152;
                    if (deltaFromNeutral < -8)
                    {
                        int shadowStrength = -deltaFromNeutral;
                        lutB[rawLum] = 8;
                        lutG[rawLum] = 10;
                        lutR[rawLum] = 12;
                        lutA[rawLum] = (byte)Math.Clamp((int)(shadowStrength * 1.45) + 22, 24, 215);
                    }
                    else if (deltaFromNeutral > 8)
                    {
                        int lightStrength = deltaFromNeutral;
                        lutB[rawLum] = 248;
                        lutG[rawLum] = 252;
                        lutR[rawLum] = 255;
                        lutA[rawLum] = (byte)Math.Clamp((int)(lightStrength * 1.65) + 26, 28, 195);
                    }
                    else
                    {
                        lutB[rawLum] = 0;
                        lutG[rawLum] = 0;
                        lutR[rawLum] = 0;
                        lutA[rawLum] = 0;
                    }
                }
            }

            int len = bgraPixels.Length;
            for (int i = 0; i + 3 < len; i += 4)
            {
                if (bgraPixels[i + 3] < 10)
                {
                    bgraPixels[i] = 0; bgraPixels[i + 1] = 0; bgraPixels[i + 2] = 0; bgraPixels[i + 3] = 0;
                    continue;
                }
                int lum = (bgraPixels[i + 2] * 77 + bgraPixels[i + 1] * 150 + bgraPixels[i] * 29) >> 8;
                bgraPixels[i]     = lutB[lum];
                bgraPixels[i + 1] = lutG[lum];
                bgraPixels[i + 2] = lutR[lum];
                bgraPixels[i + 3] = lutA[lum];
            }
        }

        public static BitmapSource ConvertLidarToAlphaOverlay(BitmapSource srcBmp)
        {
            if (srcBmp == null) return null;
            try
            {
                BitmapSource frame = srcBmp;
                if (frame.Format != PixelFormats.Bgra32 && frame.Format != PixelFormats.Pbgra32)
                {
                    var conv = new FormatConvertedBitmap();
                    conv.BeginInit();
                    conv.Source = frame;
                    conv.DestinationFormat = PixelFormats.Bgra32;
                    conv.EndInit();
                    conv.Freeze();
                    frame = conv;
                }

                int w = frame.PixelWidth, h = frame.PixelHeight;
                int stride = w * 4;
                byte[] px = new byte[h * stride];
                frame.CopyPixels(px, stride, 0);

                ApplyHillshadeContrastAndAlphaInPlace(px, w, h, true);

                var outBmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, stride);
                outBmp.Freeze();
                return outBmp;
            }
            catch { return srcBmp; }
        }

        public static (string Label, string Url)[] BuildLidarCandidateUrlsWithLabels(double northLat, double southLat, double eastLon, double westLon, int reqW, int reqH)
        {
            var (e1, n1) = LatLonToEpsg2180(southLat, westLon);
            var (e2, n2) = LatLonToEpsg2180(northLat, eastLon);
            double minE = Math.Min(e1, e2), maxE = Math.Max(e1, e2);
            double minN = Math.Min(n1, n2), maxN = Math.Max(n1, n2);

            var (minX3857, minY3857) = LatLonToEpsg3857(southLat, westLon);
            var (maxX3857, maxY3857) = LatLonToEpsg3857(northLat, eastLon);

            string bbox2180NE = string.Create(CultureInfo.InvariantCulture, $"{minN:0.00},{minE:0.00},{maxN:0.00},{maxE:0.00}");
            string bbox2180EN = string.Create(CultureInfo.InvariantCulture, $"{minE:0.00},{minN:0.00},{maxE:0.00},{maxN:0.00}");
            string bboxCrs84  = string.Create(CultureInfo.InvariantCulture, $"{westLon:0.000000},{southLat:0.000000},{eastLon:0.000000},{northLat:0.000000}");
            string bbox3857   = string.Create(CultureInfo.InvariantCulture, $"{minX3857:0.00},{minY3857:0.00},{maxX3857:0.00},{maxY3857:0.00}");

            double spanE = Math.Max(50.0, maxE - minE);
            double spanN = Math.Max(50.0, maxN - minN);
            double boxAspect = Math.Clamp(spanE / spanN, 0.35, 2.8);

            int wmsW = Math.Clamp((int)Math.Round(spanE / (3200.0 * 0.00028)), 512, 1650);
            int wmsH = Math.Clamp((int)Math.Round(wmsW / boxAspect), 380, 1650);

            return new (string Label, string Url)[]
            {
                ("GUGiK NMT GRID1 (EPSG:2180 PNG)",
                 $"https://mapy.geoportal.gov.pl/wss/service/PZGIK/NMT/GRID1/WMS/ShadedRelief?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap&FORMAT=image/png&TRANSPARENT=TRUE&LAYERS=Raster&STYLES=&CRS=EPSG:2180&BBOX={bbox2180NE}&WIDTH={wmsW}&HEIGHT={wmsH}"),

                ("GUGiK NMT GRID1 (CRS:84 PNG)",
                 $"https://mapy.geoportal.gov.pl/wss/service/PZGIK/NMT/GRID1/WMS/ShadedRelief?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap&FORMAT=image/png&TRANSPARENT=TRUE&LAYERS=Raster&STYLES=&CRS=CRS:84&BBOX={bboxCrs84}&WIDTH={wmsW}&HEIGHT={wmsH}"),

                ("GUGiK NMT GRID1 (WMS 1.1.1 2180)",
                 $"https://mapy.geoportal.gov.pl/wss/service/PZGIK/NMT/GRID1/WMS/ShadedRelief?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&FORMAT=image/png&TRANSPARENT=TRUE&LAYERS=Raster&STYLES=&SRS=EPSG:2180&BBOX={bbox2180EN}&WIDTH={wmsW}&HEIGHT={wmsH}"),

                ("GUGiK NMT GRID1 (EPSG:3857)",
                 $"https://mapy.geoportal.gov.pl/wss/service/PZGIK/NMT/GRID1/WMS/ShadedRelief?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&FORMAT=image/png&TRANSPARENT=TRUE&LAYERS=Raster&STYLES=&SRS=EPSG:3857&BBOX={bbox3857}&WIDTH={wmsW}&HEIGHT={wmsH}"),

                ("World Hillshade (Fallback)",
                 $"https://ibasemaps-api.arcgis.com/arcgis/rest/services/Elevation/World_Hillshade/MapServer/export?bbox={bbox3857}&bboxSR=3857&imageSR=3857&size={reqW},{reqH}&format=png&transparent=true&f=image"),

                ("ArcGIS Elevation Hillshade",
                 $"https://services.arcgisonline.com/arcgis/rest/services/Elevation/World_Hillshade/MapServer/export?bbox={bbox3857}&bboxSR=3857&imageSR=3857&size={reqW},{reqH}&format=jpg&f=image")
            };
        }

        public static string[] BuildLidarCandidateUrls(double northLat, double southLat, double eastLon, double westLon, int reqW, int reqH) =>
            BuildLidarCandidateUrlsWithLabels(northLat, southLat, eastLon, westLon, reqW, reqH).Select(x => x.Url).ToArray();

        private static async Task<byte[]> FetchSingleLidarSectorPixelsAsync(double northLat, double southLat, double eastLon, double westLon, int targetW, int targetH, CancellationToken ct)
        {
            string cacheKey = string.Create(CultureInfo.InvariantCulture, $"LIDAR_V26_{northLat:0.0000}_{southLat:0.0000}_{eastLon:0.0000}_{westLon:0.0000}_{targetW}x{targetH}");
            if (RawBgraSectorCache.TryGetValue(cacheKey, out var cachedPx))
                return cachedPx;

            byte[] diskBytes = TryReadDiskCache(cacheKey);
            if (diskBytes != null && TryDecodeToBgra32Pixels(diskBytes, targetW, targetH, out byte[] diskBgra) && HasValidHillshadeSignal(diskBgra, targetW, targetH))
            {
                LastLidarDiagnosticStatus = "SSD Cache";
                RawBgraSectorCache[cacheKey] = diskBgra;
                return diskBgra;
            }

            var candidates = BuildLidarCandidateUrlsWithLabels(northLat, southLat, eastLon, westLon, targetW, targetH);

            int pref = Math.Clamp(_preferredLidarCandidateIdx, 0, candidates.Length - 1);
            var orderedIndices = new List<int> { pref };
            for (int i = 0; i < candidates.Length; i++)
                if (i != pref) orderedIndices.Add(i);

            await LidarThrottle.WaitAsync(ct);
            try
            {
                if (RawBgraSectorCache.TryGetValue(cacheKey, out var cachedPx2))
                    return cachedPx2;

                foreach (int idx in orderedIndices)
                {
                    if (ct.IsCancellationRequested) break;
                    var cand = candidates[idx];
                    try
                    {
                        using var reqCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        reqCts.CancelAfter(3800);

                        byte[] bytes = await LidarHttp.GetByteArrayAsync(cand.Url, reqCts.Token);
                        if (TryDecodeToBgra32Pixels(bytes, targetW, targetH, out byte[] bgraPx) &&
                            HasValidHillshadeSignal(bgraPx, targetW, targetH))
                        {
                            if (idx < 4) _preferredLidarCandidateIdx = idx;
                            LastLidarDiagnosticStatus = cand.Label;
                            if (RawBgraSectorCache.Count > 240) RawBgraSectorCache.Clear();
                            RawBgraSectorCache[cacheKey] = bgraPx;
                            SaveDiskCacheAsync(cacheKey, bytes);
                            return bgraPx;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        if (ct.IsCancellationRequested) break;
                    }
                    catch { }
                }
                return null;
            }
            finally
            {
                LidarThrottle.Release();
            }
        }

        public static async Task<BitmapSource> FetchCompositeLidarSheetAsync(
            HttpClient http,
            double northLat,
            double southLat,
            double eastLon,
            double westLon,
            double viewW,
            double viewH,
            bool convertToTransparentOverlay,
            CancellationToken ct)
        {
            var (e1, _) = LatLonToEpsg2180(southLat, westLon);
            var (e2, _) = LatLonToEpsg2180(northLat, eastLon);
            double totalWidthMeters = Math.Abs(e2 - e1);

            int gridN = (totalWidthMeters > 2200 && totalWidthMeters <= 5500) ? 2 : 1;

            int sectorPxW = Math.Clamp((int)Math.Round(viewW / gridN), 420, 820);
            int sectorPxH = Math.Clamp((int)Math.Round(viewH / gridN), 340, 820);

            double dLat = (northLat - southLat) / gridN;
            double dLon = (eastLon - westLon) / gridN;

            var tasks = new List<Task<(int Row, int Col, byte[] Px)>>();
            for (int r = 0; r < gridN; r++)
            {
                double secNorth = northLat - r * dLat;
                double secSouth = northLat - (r + 1) * dLat;
                for (int c = 0; c < gridN; c++)
                {
                    double secWest = westLon + c * dLon;
                    double secEast = westLon + (c + 1) * dLon;
                    int capR = r, capC = c;
                    tasks.Add(Task.Run(async () =>
                    {
                        byte[] px = await FetchSingleLidarSectorPixelsAsync(secNorth, secSouth, secEast, secWest, sectorPxW, sectorPxH, ct);
                        return (capR, capC, px);
                    }, ct));
                }
            }

            var results = await Task.WhenAll(tasks);
            if (ct.IsCancellationRequested || results.All(x => x.Px == null))
            {
                LastLidarDiagnosticStatus = "Brak odpowiedzi NMT";
                return null;
            }

            int totalW = sectorPxW * gridN;
            int totalH = sectorPxH * gridN;
            int totalStride = totalW * 4;
            int sectorStride = sectorPxW * 4;

            byte[] compositePixels;
            if (gridN == 1 && results[0].Px != null)
            {
                compositePixels = (byte[])results[0].Px.Clone();
            }
            else
            {
                compositePixels = new byte[totalH * totalStride];
                foreach (var item in results)
                {
                    if (item.Px == null) continue;
                    int destXOffsetBytes = item.Col * sectorStride;
                    int destYStart = item.Row * sectorPxH;
                    for (int rowY = 0; rowY < sectorPxH; rowY++)
                    {
                        int srcOffset = rowY * sectorStride;
                        int dstOffset = (destYStart + rowY) * totalStride + destXOffsetBytes;
                        if (srcOffset + sectorStride <= item.Px.Length && dstOffset + sectorStride <= compositePixels.Length)
                        {
                            Buffer.BlockCopy(item.Px, srcOffset, compositePixels, dstOffset, sectorStride);
                        }
                    }
                }
            }

            ApplyHillshadeContrastAndAlphaInPlace(compositePixels, totalW, totalH, convertToTransparentOverlay);

            var finalBmp = BitmapSource.Create(totalW, totalH, 96, 96, PixelFormats.Bgra32, null, compositePixels, totalStride);
            finalBmp.Freeze();
            return finalBmp;
        }
    }
}
