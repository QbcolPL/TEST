#pragma warning disable CS0414, CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FieldTakHub.Builder.Services
{
    internal static class GeoportalLidarEngine
    {
        private static readonly ConcurrentDictionary<string, BitmapSource> LidarSectorCache = new ConcurrentDictionary<string, BitmapSource>();
        private static readonly SemaphoreSlim LidarThrottle = new SemaphoreSlim(6, 6);

        // Dokładna transformacja WGS84 (Lat, Lon) -> Państwowy Układ Współrzędnych Geodezyjnych PUWG 1992 (EPSG:2180)
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

        public static void ClearCache() => LidarSectorCache.Clear();

        private static bool IsValidImageBytes(byte[] data)
        {
            if (data == null || data.Length < 128) return false;
            if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return true; // JPEG
            if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) return true; // PNG
            return false;
        }

        private static BitmapSource DecodeFrozen(byte[] rawBytes)
        {
            if (!IsValidImageBytes(rawBytes)) return null;
            try
            {
                var bmp = new BitmapImage();
                using (var ms = new MemoryStream(rawBytes))
                {
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();
                }
                return bmp;
            }
            catch { return null; }
        }

        // Rygorystyczny detektor pustych odpowiedzi WMS: odrzuca obrazy w 96%+ przezroczyste, czysto białe lub o zerowej wariancji (brak cieniowania)
        public static bool IsEmptyOrFlatLidarBitmap(BitmapSource bmp)
        {
            if (bmp == null || bmp.PixelWidth < 8 || bmp.PixelHeight < 8) return true;
            try
            {
                var conv = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
                int w = conv.PixelWidth, h = conv.PixelHeight;
                int stride = w * 4;
                byte[] px = new byte[h * stride];
                conv.CopyPixels(px, stride, 0);

                int grid = 18;
                int transparentOrPureWhite = 0, total = 0;
                double sumLum = 0, sumLumSq = 0;

                for (int r = 1; r < grid; r++)
                {
                    int y = (h * r) / grid;
                    for (int c = 1; c < grid; c++)
                    {
                        int x = (w * c) / grid;
                        int idx = y * stride + x * 4;
                        if (idx + 3 < px.Length)
                        {
                            byte b = px[idx], g = px[idx + 1], red = px[idx + 2], a = px[idx + 3];
                            total++;
                            if (a < 20 || (red >= 251 && g >= 251 && b >= 251))
                                transparentOrPureWhite++;

                            double lum = (a < 20) ? 255.0 : (0.299 * red + 0.587 * g + 0.114 * b);
                            sumLum += lum;
                            sumLumSq += lum * lum;
                        }
                    }
                }

                if (total == 0) return true;
                double emptyRatio = (double)transparentOrPureWhite / total;
                double mean = sumLum / total;
                double variance = Math.Max(0.0, (sumLumSq / total) - (mean * mean));

                // Jeśli obraz jest w >= 96% pusty/przezroczysty/biały LUB ma zerową wariancję na jasnym tle -> serwer WMS nie wyrenderował rzeźby!
                if (emptyRatio >= 0.96) return true;
                if (variance < 0.8 && mean > 238.0) return true;
                return false;
            }
            catch { return true; }
        }

        // Konwertuje cieniowanie LiDAR na czystą warstwę alfa (Luminance-to-Alpha) dla nałożenia na ortofotomapę (usuwa białe tło JPEG/PNG, wzmacniając kontrast skarp i okopów)
        public static BitmapSource ConvertLidarToAlphaOverlay(BitmapSource srcBmp)
        {
            if (srcBmp == null) return null;
            try
            {
                var conv = new FormatConvertedBitmap(srcBmp, PixelFormats.Bgra32, null, 0);
                int w = conv.PixelWidth, h = conv.PixelHeight;
                int stride = w * 4;
                byte[] px = new byte[h * stride];
                conv.CopyPixels(px, stride, 0);

                for (int i = 0; i + 3 < px.Length; i += 4)
                {
                    byte b = px[i], g = px[i + 1], r = px[i + 2], a = px[i + 3];
                    if (a < 15)
                    {
                        px[i] = 0; px[i + 1] = 0; px[i + 2] = 0; px[i + 3] = 0;
                        continue;
                    }

                    int lum = (r * 77 + g * 150 + b * 29) >> 8;
                    if (lum >= 246)
                    {
                        // Płaski jasny teren staje się całkowicie przezroczysty, aby nie zamglić ortofotomapy pod spodem
                        px[i] = 0; px[i + 1] = 0; px[i + 2] = 0; px[i + 3] = 0;
                    }
                    else
                    {
                        // Cienie skarp, wąwozów i okopów otrzymują proporcjonalny kanał alfa z podbiciem kontrastu 1.35x
                        int darkness = 255 - lum;
                        int alpha = Math.Clamp((int)(darkness * 1.45), 18, 245);
                        byte shade = (byte)Math.Clamp(lum - 25, 0, 180);
                        px[i] = shade;
                        px[i + 1] = shade;
                        px[i + 2] = shade;
                        px[i + 3] = (byte)alpha;
                    }
                }

                var outBmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, stride);
                outBmp.Freeze();
                return outBmp;
            }
            catch { return srcBmp; }
        }

        // Buduje 5 kandydatów URL dla pojedynczego sektora LiDAR (zapewnia kompatybilność z każdym serwerem GUGiK w Polsce)
        public static string[] BuildLidarCandidateUrls(double northLat, double southLat, double eastLon, double westLon, int reqW, int reqH)
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

            return new[]
            {
                // 1. WMS 1.3.0 EPSG:2180 (Northing, Easting) JPEG z STYLES=default (oficjalny format z Editor Imagery Index)
                $"https://mapy.geoportal.gov.pl/wss/service/PZGIK/NMT/GRID1/WMS/ShadedRelief?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap&LAYERS=Raster&STYLES=default&CRS=EPSG:2180&BBOX={bbox2180NE}&WIDTH={reqW}&HEIGHT={reqH}&FORMAT=image/jpeg",

                // 2. WMS 1.3.0 CRS:84 (Lon, Lat) PNG z pustym STYLES= (sprawdzony wzorzec OsmAnd)
                $"https://mapy.geoportal.gov.pl/wss/service/PZGIK/NMT/GRID1/WMS/ShadedRelief?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap&LAYERS=Raster&STYLES=&CRS=CRS:84&BBOX={bboxCrs84}&WIDTH={reqW}&HEIGHT={reqH}&FORMAT=image/png&TRANSPARENT=TRUE",

                // 3. WMS 1.1.1 EPSG:2180 (Easting, Northing) PNG
                $"https://mapy.geoportal.gov.pl/wss/service/PZGIK/NMT/GRID1/WMS/ShadedRelief?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=Raster&STYLES=&SRS=EPSG:2180&BBOX={bbox2180EN}&WIDTH={reqW}&HEIGHT={reqH}&FORMAT=image/png&TRANSPARENT=TRUE",

                // 4. WMS 1.1.1 EPSG:3857 JPEG
                $"https://mapy.geoportal.gov.pl/wss/service/PZGIK/NMT/GRID1/WMS/ShadedRelief?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=Raster&STYLES=&SRS=EPSG:3857&BBOX={bbox3857}&WIDTH={reqW}&HEIGHT={reqH}&FORMAT=image/jpeg",

                // 5. Usługa zapasowa NMT 5m (dla mniejszych skal przeglądowych)
                $"https://mapy.geoportal.gov.pl/wss/service/PZGIK/NMT/GRID5/WMS/ShadedRelief?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap&LAYERS=Raster&STYLES=default&CRS=EPSG:2180&BBOX={bbox2180NE}&WIDTH={reqW}&HEIGHT={reqH}&FORMAT=image/jpeg"
            };
        }

        private static async Task<BitmapSource> FetchSingleLidarSectorAsync(HttpClient http, double northLat, double southLat, double eastLon, double westLon, int reqW, int reqH, CancellationToken ct)
        {
            string cacheKey = string.Create(CultureInfo.InvariantCulture, $"LIDAR_{northLat:0.0000}_{southLat:0.0000}_{eastLon:0.0000}_{westLon:0.0000}_{reqW}x{reqH}");
            if (LidarSectorCache.TryGetValue(cacheKey, out var cached))
                return cached;

            string[] candidates = BuildLidarCandidateUrls(northLat, southLat, eastLon, westLon, reqW, reqH);

            await LidarThrottle.WaitAsync(ct);
            try
            {
                if (LidarSectorCache.TryGetValue(cacheKey, out var cached2))
                    return cached2;

                foreach (string url in candidates)
                {
                    if (ct.IsCancellationRequested) break;
                    try
                    {
                        byte[] bytes = await http.GetByteArrayAsync(url, ct);
                        var bmp = DecodeFrozen(bytes);
                        if (bmp != null && !IsEmptyOrFlatLidarBitmap(bmp))
                        {
                            if (LidarSectorCache.Count > 300) LidarSectorCache.Clear();
                            LidarSectorCache[cacheKey] = bmp;
                            return bmp;
                        }
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

        // Pobiera pełny arkusz LiDAR (automatycznie dzieląc większe obszary na siatkę 2x2 lub 3x3 sektorów, aby pojedyncze zapytanie nigdy nie przekroczyło limitu skali GUGiK!)
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

            // Podział na mniejsze sektory (każdy <= 1450 m szerokości), aby serwer GUGiK zawsze widział zapytanie w skali ~1:2500 i nigdy nie wyłączał warstwy LiDAR!
            int gridN = totalWidthMeters > 2900 ? 3 : (totalWidthMeters > 1350 ? 2 : 1);

            int sectorPxW = Math.Clamp((int)Math.Round((viewW * 1.15) / gridN), 480, 960);
            int sectorPxH = Math.Clamp((int)Math.Round((viewH * 1.15) / gridN), 480, 960);

            double dLat = (northLat - southLat) / gridN;
            double dLon = (eastLon - westLon) / gridN;

            var tasks = new List<Task<(int Row, int Col, BitmapSource Bmp)>>();
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
                        var b = await FetchSingleLidarSectorAsync(http, secNorth, secSouth, secEast, secWest, sectorPxW, sectorPxH, ct);
                        return (capR, capC, b);
                    }, ct));
                }
            }

            var results = await Task.WhenAll(tasks);
            if (ct.IsCancellationRequested || results.All(x => x.Bmp == null))
                return null;

            BitmapSource rawComposite;
            if (gridN == 1)
            {
                rawComposite = results[0].Bmp;
            }
            else
            {
                int totalW = sectorPxW * gridN;
                int totalH = sectorPxH * gridN;
                var dv = new DrawingVisual();
                using (DrawingContext dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, totalW, totalH));
                    foreach (var item in results)
                    {
                        if (item.Bmp != null)
                        {
                            dc.DrawImage(item.Bmp, new Rect(item.Col * sectorPxW, item.Row * sectorPxH, sectorPxW + 1.0, sectorPxH + 1.0));
                        }
                    }
                }
                var rtb = new RenderTargetBitmap(totalW, totalH, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                rtb.Freeze();
                rawComposite = rtb;
            }

            return convertToTransparentOverlay
                ? ConvertLidarToAlphaOverlay(rawComposite)
                : rawComposite;
        }
    }
}
