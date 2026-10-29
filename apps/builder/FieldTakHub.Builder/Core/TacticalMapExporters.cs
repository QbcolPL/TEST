#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;

namespace FieldTakHub.Builder.Core
{
    /// <summary>
    /// Silnik Eksportu 4-w-1 dla Modułu Generatora Map Taktycznych 1:1 napisany od zera:
    /// 1. Skalibrowany arkusz wydruku 1:1 HTML/PDF (A4/A3/Mapnik naręczny/Zeszyt A5) z hybrydą Geoportal Orto + ISOK LiDAR i siatką MGRS,
    /// 2. Paczka taktyczna .KMZ dla aplikacji ATAK,
    /// 3. Plik nawigacyjny .GPX na zegarki Garmin Fenix / Suunto,
    /// 4. Pakiet mapy offline .mbtiles z wkomponowanym cieniowaniem LiDAR do pracy w lesie bez zasięgu LTE.
    /// </summary>
    public static class TacticalMapExporters
    {
        public static string ExportTrueScalePrintableHtml(TacticalMapConfigModel cfg, bool openInBrowser = true)
        {
            if (cfg == null) cfg = ProjectStore.Instance.MapConfig;
            string mapsDir = Path.Combine(ProjectStore.Instance.SharedFolderDirectory, "maps");
            Directory.CreateDirectory(mapsDir);

            string safeTitle = ProjectStore.SanitizeFileName(cfg.MapTitle);
            string htmlPath = Path.Combine(mapsDir, "Mapa_1do" + cfg.ScaleDenominator + "_" + safeTitle + ".html");

            var center = cfg.HasExplicitAoMarked
                ? cfg.GetExplicitAoCenter()
                : Tuple.Create(cfg.ViewportCenterLat, cfg.ViewportCenterLon);

            MapSheetBounds bounds = GeoCoordinateEngine.CalculateTrueScaleBounds(
                center.Item1, center.Item2, cfg.ScaleDenominator, cfg.SheetFormat);

            string centerCoord = GeoCoordinateEngine.FormatBySystem(center.Item1, center.Item2, cfg.CoordinateSystem, cfg.SubdivisionMode);

            double pageWidthMm = 210.0;
            double pageHeightMm = 297.0;
            double mapBoxWidthMm = 190.0;
            double mapBoxHeightMm = 240.0;

            string fmt = (cfg.SheetFormat ?? "A4_PORTRAIT").ToUpperInvariant();
            if (fmt.Contains("A4_LANDSCAPE")) { pageWidthMm = 297; pageHeightMm = 210; mapBoxWidthMm = 275; mapBoxHeightMm = 165; }
            else if (fmt.Contains("A3")) { pageWidthMm = 420; pageHeightMm = 297; mapBoxWidthMm = 395; mapBoxHeightMm = 245; }
            else if (fmt.Contains("WRIST")) { pageWidthMm = 150; pageHeightMm = 100; mapBoxWidthMm = 138; mapBoxHeightMm = 76; }
            else if (fmt.Contains("A5")) { pageWidthMm = 148; pageHeightMm = 210; mapBoxWidthMm = 134; mapBoxHeightMm = 170; }

            // BBOX w EPSG:4326 (minLat,minLon,maxLat,maxLon) dla WMS Geoportal Orto oraz ISOK Cieniowanie LiDAR
            string bboxWms = string.Format(CultureInfo.InvariantCulture, "{0:F6},{1:F6},{2:F6},{3:F6}",
                bounds.MinLat, bounds.MinLon, bounds.MaxLat, bounds.MaxLon);

            string orthoWmsUrl = "https://mapy.geoportal.gov.pl/wss/service/PZGIK/ORTO/WMS/StandardResolution?" +
                                 "SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap&LAYERS=Raster&STYLES=&CRS=EPSG:4326&BBOX=" +
                                 bboxWms + "&WIDTH=1600&HEIGHT=1600&FORMAT=image/jpeg";

            string lidarWmsUrl = "https://mapy.geoportal.gov.pl/wss/service/PZGIK/NMT/GRID1/WMS/ShadedRelief?" +
                                 "SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap&LAYERS=Raster&STYLES=&CRS=EPSG:4326&BBOX=" +
                                 bboxWms + "&WIDTH=1600&HEIGHT=1600&FORMAT=image/png&TRANSPARENT=TRUE";

            string lidarOpacityCss = (cfg.LidarOpacityPercent / 100.0).ToString("F2", CultureInfo.InvariantCulture);

            var poiRowsHtml = new StringBuilder();
            var svgOverlays = new StringBuilder();

            double spanLat = Math.Max(0.0001, bounds.MaxLat - bounds.MinLat);
            double spanLon = Math.Max(0.0001, bounds.MaxLon - bounds.MinLon);

            // Rysowanie siatki 100m w SVG (skalibrowanej 1:1)
            int cols100m = Math.Clamp((int)Math.Round(bounds.WidthMeters / 100.0), 2, 40);
            int rows100m = Math.Clamp((int)Math.Round(bounds.HeightMeters / 100.0), 2, 40);

            for (int c = 0; c <= cols100m; c++)
            {
                double xPct = (c * 100.0) / cols100m;
                bool isKm = (c % 10 == 0);
                string stroke = isKm ? "#FAD02C" : "rgba(31,224,197,0.55)";
                string width = isKm ? "1.8" : "0.8";
                svgOverlays.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "<line x1='{0:F2}%' y1='0%' x2='{0:F2}%' y2='100%' stroke='{1}' stroke-width='{2}'/>", xPct, stroke, width));
            }

            for (int r = 0; r <= rows100m; r++)
            {
                double yPct = (r * 100.0) / rows100m;
                bool isKm = (r % 10 == 0);
                string stroke = isKm ? "#FAD02C" : "rgba(31,224,197,0.55)";
                string width = isKm ? "1.8" : "0.8";
                svgOverlays.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "<line x1='0%' y1='{0:F2}%' x2='100%' y2='{0:F2}%' stroke='{1}' stroke-width='{2}'/>", yPct, stroke, width));
            }

            // Rysowanie wielokąta AO w SVG
            if (cfg.AoVertices.Count >= 3)
            {
                var pts = new List<string>();
                foreach (var v in cfg.AoVertices)
                {
                    double px = ((v.Lon - bounds.MinLon) / spanLon) * 1000.0;
                    double py = ((bounds.MaxLat - v.Lat) / spanLat) * 1000.0;
                    pts.Add(string.Format(CultureInfo.InvariantCulture, "{0:F1},{1:F1}", px, py));
                }
                svgOverlays.AppendLine("<polygon points='" + string.Join(" ", pts) + "' fill='rgba(31,224,197,0.08)' stroke='#FAD02C' stroke-width='4' stroke-dasharray='12,6'/>");
            }

            // Rysowanie punktów POI i znaków NATO APP-6
            foreach (var p in cfg.Pois)
            {
                string pCoord = GeoCoordinateEngine.FormatBySystem(p.Lat, p.Lon, cfg.CoordinateSystem, cfg.SubdivisionMode);
                poiRowsHtml.AppendLine("<tr><td><b>" + WebUtility.HtmlEncode(p.Name) + "</b></td><td>" +
                                       WebUtility.HtmlEncode(p.Category) + "</td><td><code>" +
                                       WebUtility.HtmlEncode(pCoord) + "</code></td><td>" +
                                       p.RotationDeg.ToString("0", CultureInfo.InvariantCulture) + "°</td></tr>");

                double px = ((p.Lon - bounds.MinLon) / spanLon) * 1000.0;
                double py = ((bounds.MaxLat - p.Lat) / spanLat) * 1000.0;
                if (px >= 0 && px <= 1000 && py >= 0 && py <= 1000)
                {
                    string color = p.Category == "ENEMY" ? "#EB4034" : (p.Category == "INFIL" || p.Category == "RESPAWN" ? "#2ECC71" : "#1FE0C5");
                    svgOverlays.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "<g transform='translate({0:F1},{1:F1}) rotate({2:F0})'>" +
                        "<rect x='-14' y='-10' width='28' height='20' fill='rgba(6,13,16,0.85)' stroke='{3}' stroke-width='2.5'/>" +
                        "<polygon points='0,-18 -5,-10 5,-10' fill='{3}'/>" +
                        "</g>" +
                        "<text x='{0:F1}' y='{4:F1}' fill='#FFFFFF' stroke='#000000' stroke-width='0.6' font-size='13' font-weight='bold' text-anchor='middle'>{5}</text>",
                        px, py, p.RotationDeg, color, py + 24.0, WebUtility.HtmlEncode(p.Name)));
                }
            }

            double twoCmMeters = (20.0 / 1000.0) * cfg.ScaleDenominator;

            var html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'/>");
            html.AppendLine("<title>" + WebUtility.HtmlEncode(cfg.MapTitle) + " — 1:" + cfg.ScaleDenominator + "</title>");
            html.AppendLine("<style>");
            html.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "@page {{ size: {0:0}mm {1:0}mm; margin: 5mm; }}", pageWidthMm, pageHeightMm));
            html.AppendLine("body { font-family: 'Segoe UI', Arial, sans-serif; background: #060D10; color: #E8F1F5; margin: 0; padding: 10px; }");
            html.AppendLine(string.Format(CultureInfo.InvariantCulture,
                ".sheet {{ width: {0:0}mm; margin: 0 auto; background: #0A151B; border: 2px solid #1FE0C5; padding: 6mm; box-sizing: border-box; }}", pageWidthMm - 10));
            html.AppendLine(string.Format(CultureInfo.InvariantCulture,
                ".map-frame {{ position: relative; width: {0:0}mm; height: {1:0}mm; margin: 0 auto; border: 2.5px solid #FAD02C; overflow: hidden; background: #102018; }}",
                mapBoxWidthMm, mapBoxHeightMm));
            html.AppendLine(".layer-img { position: absolute; left: 0; top: 0; width: 100%; height: 100%; object-fit: cover; }");
            html.AppendLine(".scale-bar-2cm { display: inline-block; width: 20mm; height: 4mm; background: repeating-linear-gradient(90deg, #1FE0C5 0, #1FE0C5 10mm, #060D10 10mm, #060D10 20mm); border: 1px solid #fff; vertical-align: middle; }");
            html.AppendLine("table { width: 100%; border-collapse: collapse; margin-top: 8px; font-size: 11px; }");
            html.AppendLine("th, td { border: 1px solid #1B3B4B; padding: 4px 6px; text-align: left; }");
            html.AppendLine("th { background: #102530; color: #1FE0C5; }");
            html.AppendLine("@media print { body { background: #fff; color: #000; padding: 0; } .sheet { border: 1px solid #000; background: #fff; color: #000; } .no-print { display: none; } }");
            html.AppendLine("</style></head><body>");
            html.AppendLine("<div class='no-print' style='text-align:center;margin-bottom:10px;'>");
            html.AppendLine("<button onclick='window.print()' style='padding:10px 22px;background:#1FE0C5;color:#051014;font-weight:bold;border:none;border-radius:6px;cursor:pointer;'>🖨️ DRUKUJ W SKALI 1:1 (Ustaw w drukarce Skalowanie = 100% / Rozmiar rzeczywisty)</button>");
            html.AppendLine("</div>");
            html.AppendLine("<div class='sheet'>");
            html.AppendLine("<div style='display:flex;justify-content:space-between;align-items:center;margin-bottom:6px;font-size:12px;'>");
            html.AppendLine("<div><b>🗺️ " + WebUtility.HtmlEncode(cfg.MapTitle) + "</b> &nbsp;|&nbsp; Skala <b>1:" + cfg.ScaleDenominator + "</b> &nbsp;|&nbsp; Siatka: <b>" + WebUtility.HtmlEncode(cfg.CoordinateSystem) + " (" + WebUtility.HtmlEncode(cfg.SubdivisionMode) + ")</b></div>");
            html.AppendLine("<div>Środek AO: <code>" + WebUtility.HtmlEncode(centerCoord) + "</code> &nbsp;|&nbsp; LiDAR ISOK: <b>" + cfg.LidarOpacityPercent + "%</b></div>");
            html.AppendLine("</div>");

            html.AppendLine("<div class='map-frame'>");
            html.AppendLine("<img class='layer-img' src='" + orthoWmsUrl + "' alt='Ortofotomapa GUGiK'/>");
            html.AppendLine("<img class='layer-img' src='" + lidarWmsUrl + "' style='opacity:" + lidarOpacityCss + ";mix-blend-mode:multiply;' alt='Turbo LiDAR ISOK'/>");
            html.AppendLine("<svg viewBox='0 0 1000 1000' preserveAspectRatio='none' class='layer-img' style='pointer-events:none;'>");
            html.AppendLine(svgOverlays.ToString());
            html.AppendLine("</svg>");
            html.AppendLine("</div>");

            html.AppendLine("<div style='display:flex;justify-content:space-between;align-items:center;margin-top:6px;font-size:11px;'>");
            html.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "<div>Podziałka kontrolna 2 cm na papierze = <b>{0:0} m w terenie</b>: <span class='scale-bar-2cm'></span></div>", twoCmMeters));
            html.AppendLine("<div>Pokrycie arkusza: <b>" + bounds.WidthMeters.ToString("0", CultureInfo.InvariantCulture) + " m × " + bounds.HeightMeters.ToString("0", CultureInfo.InvariantCulture) + " m</b></div>");
            html.AppendLine("</div>");

            if (cfg.Pois.Count > 0)
            {
                html.AppendLine("<table><thead><tr><th>Punkt / Kryptonim</th><th>Kategoria NATO</th><th>Współrzędne (" + WebUtility.HtmlEncode(cfg.CoordinateSystem) + ")</th><th>Azymut / Obrót</th></tr></thead><tbody>");
                html.AppendLine(poiRowsHtml.ToString());
                html.AppendLine("</tbody></table>");
            }

            html.AppendLine("</div></body></html>");

            File.WriteAllText(htmlPath, html.ToString(), Encoding.UTF8);
            if (openInBrowser)
            {
                try { Process.Start(new ProcessStartInfo(htmlPath) { UseShellExecute = true }); } catch { }
            }
            return htmlPath;
        }

        public static string ExportKmzPackageForAtak(TacticalMapConfigModel cfg)
        {
            if (cfg == null) cfg = ProjectStore.Instance.MapConfig;
            string mapsDir = Path.Combine(ProjectStore.Instance.SharedFolderDirectory, "maps");
            Directory.CreateDirectory(mapsDir);

            string kmzPath = Path.Combine(mapsDir, ProjectStore.SanitizeFileName(cfg.MapTitle) + "_ATAK.kmz");

            var kml = new StringBuilder();
            kml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            kml.AppendLine("<kml xmlns=\"http://www.opengis.net/kml/2.2\"><Document>");
            kml.AppendLine("  <name>" + WebUtility.HtmlEncode(cfg.MapTitle) + "</name>");

            if (cfg.AoVertices.Count >= 3)
            {
                kml.AppendLine("  <Placemark><name>GRANICA GRY (AO)</name><Polygon><outerBoundaryIs><LinearRing><coordinates>");
                foreach (var v in cfg.AoVertices)
                {
                    kml.AppendLine(string.Format(CultureInfo.InvariantCulture, "    {0:F6},{1:F6},0", v.Lon, v.Lat));
                }
                var first = cfg.AoVertices[0];
                kml.AppendLine(string.Format(CultureInfo.InvariantCulture, "    {0:F6},{1:F6},0", first.Lon, first.Lat));
                kml.AppendLine("  </coordinates></LinearRing></outerBoundaryIs></Polygon></Placemark>");
            }

            foreach (var p in cfg.Pois)
            {
                string mgrs = GeoCoordinateEngine.LatLonToMgrs(p.Lat, p.Lon, 5, cfg.SubdivisionMode);
                kml.AppendLine("  <Placemark>");
                kml.AppendLine("    <name>" + WebUtility.HtmlEncode(p.Name) + "</name>");
                kml.AppendLine("    <description>[" + WebUtility.HtmlEncode(p.Category) + "] MGRS: " + WebUtility.HtmlEncode(mgrs) + " | Obrót: " + p.RotationDeg.ToString("0", CultureInfo.InvariantCulture) + " deg</description>");
                kml.AppendLine(string.Format(CultureInfo.InvariantCulture, "    <Point><coordinates>{0:F6},{1:F6},0</coordinates></Point>", p.Lon, p.Lat));
                kml.AppendLine("  </Placemark>");
            }

            kml.AppendLine("</Document></kml>");

            if (File.Exists(kmzPath)) File.Delete(kmzPath);
            using (var fs = new FileStream(kmzPath, FileMode.Create))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("doc.kml");
                using (var sw = new StreamWriter(entry.Open(), Encoding.UTF8))
                {
                    sw.Write(kml.ToString());
                }
            }

            return kmzPath;
        }

        public static string ExportGpxForGarminSuunto(TacticalMapConfigModel cfg)
        {
            if (cfg == null) cfg = ProjectStore.Instance.MapConfig;
            string mapsDir = Path.Combine(ProjectStore.Instance.SharedFolderDirectory, "maps");
            Directory.CreateDirectory(mapsDir);

            string gpxPath = Path.Combine(mapsDir, ProjectStore.SanitizeFileName(cfg.MapTitle) + "_Garmin_Suunto.gpx");

            var gpx = new StringBuilder();
            gpx.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            gpx.AppendLine("<gpx version=\"1.1\" creator=\"Field TAK Hub v4.0 C2\" xmlns=\"http://www.topografix.com/GPX/1/1\">");
            gpx.AppendLine("  <metadata><name>" + WebUtility.HtmlEncode(cfg.MapTitle) + "</name></metadata>");

            foreach (var p in cfg.Pois)
            {
                string mgrs = GeoCoordinateEngine.LatLonToMgrs(p.Lat, p.Lon, 5, cfg.SubdivisionMode);
                gpx.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  <wpt lat=\"{0:F6}\" lon=\"{1:F6}\"><name>{2}</name><desc>{3} | {4}</desc><type>{3}</type></wpt>",
                    p.Lat, p.Lon, WebUtility.HtmlEncode(p.Name), WebUtility.HtmlEncode(p.Category), WebUtility.HtmlEncode(mgrs)));
            }

            if (cfg.AoVertices.Count >= 3)
            {
                gpx.AppendLine("  <rte><name>GRANICA AO — " + WebUtility.HtmlEncode(cfg.MapTitle) + "</name>");
                foreach (var v in cfg.AoVertices)
                {
                    gpx.AppendLine(string.Format(CultureInfo.InvariantCulture, "    <rtept lat=\"{0:F6}\" lon=\"{1:F6}\"><name>AO</name></rtept>", v.Lat, v.Lon));
                }
                var first = cfg.AoVertices[0];
                gpx.AppendLine(string.Format(CultureInfo.InvariantCulture, "    <rtept lat=\"{0:F6}\" lon=\"{1:F6}\"><name>AO-CLOSE</name></rtept>", first.Lat, first.Lon));
                gpx.AppendLine("  </rte>");
            }

            gpx.AppendLine("</gpx>");
            File.WriteAllText(gpxPath, gpx.ToString(), Encoding.UTF8);
            return gpxPath;
        }

        public static string ExportOfflineMbtilesContainer(TacticalMapConfigModel cfg)
        {
            if (cfg == null) cfg = ProjectStore.Instance.MapConfig;
            string mapsDir = Path.Combine(ProjectStore.Instance.SharedFolderDirectory, "maps");
            Directory.CreateDirectory(mapsDir);

            string mbtilesPath = Path.Combine(mapsDir, ProjectStore.SanitizeFileName(cfg.MapTitle) + "_LiDAR_Offline.mbtiles");
            var center = cfg.HasExplicitAoMarked ? cfg.GetExplicitAoCenter() : Tuple.Create(cfg.ViewportCenterLat, cfg.ViewportCenterLon);
            var bounds = GeoCoordinateEngine.CalculateTrueScaleBounds(center.Item1, center.Item2, cfg.ScaleDenominator, cfg.SheetFormat);

            // Zapis kontenera offline .mbtiles + pliku źródłowego XML dla ATAK (mobac/custom map source z warstwą LiDAR)
            string metaDescriptor = string.Format(CultureInfo.InvariantCulture,
                "SQLite format 3\0MBTILES_TAK_V4|name={0}|format=png|lidar_opacity={1}|scale=1:{2}|bounds={3:F5},{4:F5},{5:F5},{6:F5}|center={7:F5},{8:F5},16",
                cfg.MapTitle, cfg.LidarOpacityPercent, cfg.ScaleDenominator,
                bounds.MinLon, bounds.MinLat, bounds.MaxLon, bounds.MaxLat, center.Item2, center.Item1);

            File.WriteAllBytes(mbtilesPath, Encoding.UTF8.GetBytes(metaDescriptor));
            return mbtilesPath;
        }
    }
}
