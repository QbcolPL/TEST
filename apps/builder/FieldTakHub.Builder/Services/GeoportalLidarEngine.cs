#pragma warning disable CS0414, CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FieldTakHub.Builder.Services
{
    internal static class GeoportalLidarEngine
    {
        private static readonly ConcurrentDictionary<string, byte[]> RawBgraSectorCache = new ConcurrentDictionary<string, byte[]>();
        private static readonly SemaphoreSlim LidarThrottle = new SemaphoreSlim(6, 6);
        public static string LastLidarDiagnosticStatus { get; private set; } = "Gotowy";

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

        public static void ClearCache() => RawBgraSectorCache.Clear();

        private static bool IsValidImageBytes(byte[] data)
        {
            if (data == null || data.Length < 128) return false;
            if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return true; // JPEG
            if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) return true; // PNG
            return false;
        }

        // Dekoduje dowolny strumień JPEG/PNG i dopasowuje wymiary do (targetW, targetH) w formacie BGRA32 (bezpieczne w wątkach tła)
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

                int stride = targetW * 4;
                bgraPixels = new byte[targetH * stride];
                frame.CopyPixels(bgraPixels, stride, 0);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool HasValidHillshadeSignal(byte[] bgraPixels, int width, int height)
        {
            if (bgraPixels == null || width < 8 || height < 8) return false;
            int stride = width * 4;
            int grid = 20;
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
                        if (a < 15 || (red >= 252 && g >= 252 && b >= 252))
                            emptyOrWhiteCount++;

                        byte lum = (a < 15) ? (byte)255 : (byte)((red * 77 + g * 150 + b * 29) >> 8);
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

            if (emptyRatio >= 0.97) return false;
            if ((maxLum - minLum) < 2 && mean > 240.0) return false;
            if (variance < 0.25 && mean > 242.0) return false;
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

        // Rozciąga histogram kontrastu (Auto-Levels) i buduje trójwymiarową mapę cieni i świateł (widoczną zarówno na ciemnych lasach/torach, jak i na jasnych polach!)
        private static void ApplyHillshadeContrastAndAlphaInPlace(byte[] bgraPixels, int width, int height, bool asTransparentOverlay)
        {
            if (bgraPixels == null || bgraPixels.Length < 16) return;

            int[] hist = new int[256];
            int validSamples = 0;
            int step = Math.Max(1, (bgraPixels.Length / 4) / 8000) * 4;

            for (int i = 0; i + 3 < bgraPixels.Length; i += step)
            {
                if (bgraPixels[i + 3] >= 20)
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
            if (validSamples > 50)
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

            for (int i = 0; i + 3 < bgraPixels.Length; i += 4)
            {
                byte a = bgraPixels[i + 3];
                if (a < 15)
                {
                    bgraPixels[i] = 0; bgraPixels[i + 1] = 0; bgraPixels[i + 2] = 0; bgraPixels[i + 3] = 0;
                    continue;
                }

                int rawLum = (bgraPixels[i + 2] * 77 + bgraPixels[i + 1] * 150 + bgraPixels[i] * 29) >> 8;
                int stretched = (int)Math.Clamp(((rawLum - lowClip) / span) * 255.0, 0.0, 255.0);

                if (!asTransparentOverlay)
                {
                    // Jako samodzielna mapa bazowa: kontrastowy rysunek rzeźby terenu 3D
                    byte v = (byte)stretched;
                    bgraPixels[i] = v;
                    bgraPixels[i + 1] = v;
                    bgraPixels[i + 2] = v;
                    bgraPixels[i + 3] = 255;
                }
                else
                {
                    // Jako nakładka na ortofotomapę / Google Sat:
                    // Ciemne zbocza -> głębokie czarne cienie; Jasne zbocza -> jasny blask 3D (dzięki czemu nasypy i okopy są widoczne na ciemnym lesie i torowisku!)
                    int deltaFromNeutral = stretched - 155;
                    if (deltaFromNeutral < -8)
                    {
                        int shadowStrength = -deltaFromNeutral; // 9 .. 155
                        int alpha = Math.Clamp((int)(shadowStrength * 1.65) + 28, 30, 250);
                        bgraPixels[i] = 8;
                        bgraPixels[i + 1] = 10;
                        bgraPixels[i + 2] = 12;
                        bgraPixels[i + 3] = (byte)alpha;
                    }
                    else if (deltaFromNeutral > 12)
                    {
                        int lightStrength = deltaFromNeutral; // 13 .. 100
                        int alpha = Math.Clamp((int)(lightStrength * 1.35) + 18, 20, 165);
                        bgraPixels[i] = 242;
                        bgraPixels[i + 1] = 248;
                        bgraPixels[i + 2] = 255;
                        bgraPixels[i + 3] = (byte)alpha;
                    }
                    else
                    {
                        // Płaski grunt: delikatna mgiełka reliefowa (alpha=18), niezasłaniająca ortofotomapy
                        byte gVal = (byte)stretched;
                        bgraPixels[i] = gVal;
                        bgraPixels[i + 1] = gVal;
                        bgraPixels[i + 2] = gVal;
                        bgraPixels[i + 3] = 18;
                    }
                }
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
            string bbox4326EN = string.Create(CultureInfo.InvariantCulture, $"{westLon:0.000000},{southLat:0.000000},{eastLon:0.000000},{northLat:0.000000}");
            string bbox3857   = string.Create(CultureInfo.InvariantCulture, $"{minX3857:0.00},{minY3857:0.00},{maxX3857:0.00},{maxY3857:0.00}");

            // Dobierz rozmiar zapytania WMS tak, aby wewnętrzna skala serwera GUGiK ZAWSZE wynosiła ok. 1:3000 (gwarantuje 100% widoczności warstwy Raster przy 1:1400 jak i 1:15000!)
            double spanMeters = Math.Max(80.0, maxE - minE);
            double aspect = Math.Max(0.3, (double)reqW / Math.Max(1, reqH));
            int wmsW = Math.Clamp((int)Math.Round(spanMeters / (3000.0 * 0.00028)), 420, 1500);
            int wmsH = Math.Clamp((int)Math.Round(wmsW / aspect), 320, 1500);

            return new (string Label, string Url)[]
            {
                // 1. WMS 1.1.1 EPSG:2180 (Easting, Northing) EVRF2007 — jednoznaczna kolejność osi X,Y!
                ("EVRF2007 GRID1 (WMS 1.1.1 EPSG:2180)",
                 $"https://mapy.geoportal.gov.pl/wss/service/PZGIK/NMT/GRID1/WMS/ShadedRelief_EVRF2007?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=Raster&STYLES=&SRS=EPSG:2180&BBOX={bbox2180EN}&WIDTH={wmsW}&HEIGHT={wmsH}&FORMAT=image/jpeg"),

                // 2. WMS 1.1.1 EPSG:2180 (Easting, Northing) KRON86
                ("KRON86 GRID1 (WMS 1.1.1 EPSG:2180)",
                 $"https://mapy.geoportal.gov.pl/wss/service/PZGIK/NMT/GRID1/WMS/ShadedRelief?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=Raster&STYLES=&SRS=EPSG:2180&BBOX={bbox2180EN}&WIDTH={wmsW}&HEIGHT={wmsH}&FORMAT=image/jpeg"),

                // 3. WMS 1.3.0 EPSG:2180 (Northing, Easting) EVRF2007
                ("EVRF2007 GRID1 (WMS 1.3.0 EPSG:2180)",
                 $"https://mapy.geoportal.gov.pl/wss/service/PZGIK/NMT/GRID1/WMS/ShadedRelief_EVRF2007?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap&LAYERS=Raster&STYLES=default&CRS=EPSG:2180&BBOX={bbox2180NE}&WIDTH={wmsW}&HEIGHT={wmsH}&FORMAT=image/jpeg"),

                // 4. WMS 1.3.0 EPSG:2180 (Northing, Easting) KRON86
                ("KRON86 GRID1 (WMS 1.3.0 EPSG:2180)",
                 $"https://mapy.geoportal.gov.pl/wss/service/PZGIK/NMT/GRID1/WMS/ShadedRelief?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap&LAYERS=Raster&STYLES=default&CRS=EPSG:2180&BBOX={bbox2180NE}&WIDTH={wmsW}&HEIGHT={wmsH}&FORMAT=image/jpeg"),

                // 5. WMS 1.1.1 EPSG:4326 (Lon, Lat) EVRF2007
                ("EVRF2007 GRID1 (EPSG:4326)",
                 $"https://mapy.geoportal.gov.pl/wss/service/PZGIK/NMT/GRID1/WMS/ShadedRelief_EVRF2007?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=Raster&STYLES=&SRS=EPSG:4326&BBOX={bbox4326EN}&WIDTH={wmsW}&HEIGHT={wmsH}&FORMAT=image/jpeg"),

                // 6. WMS 1.1.1 EPSG:4326 (Lon, Lat) KRON86
                ("KRON86 GRID1 (EPSG:4326)",
                 $"https://mapy.geoportal.gov.pl/wss/service/PZGIK/NMT/GRID1/WMS/ShadedRelief?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=Raster&STYLES=&SRS=EPSG:4326&BBOX={bbox4326EN}&WIDTH={wmsW}&HEIGHT={wmsH}&FORMAT=image/jpeg"),

                // 7. WMS 1.1.1 EPSG:3857 EVRF2007
                ("EVRF2007 GRID1 (EPSG:3857)",
                 $"https://mapy.geoportal.gov.pl/wss/service/PZGIK/NMT/GRID1/WMS/ShadedRelief_EVRF2007?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=Raster&STYLES=&SRS=EPSG:3857&BBOX={bbox3857}&WIDTH={wmsW}&HEIGHT={wmsH}&FORMAT=image/jpeg"),

                // 8. GRID5 EVRF2007 (WMS 1.1.1 EPSG:2180)
                ("EVRF2007 GRID5 (EPSG:2180)",
                 $"https://mapy.geoportal.gov.pl/wss/service/PZGIK/NMT/GRID5/WMS/ShadedRelief_EVRF2007?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=Raster&STYLES=&SRS=EPSG:2180&BBOX={bbox2180EN}&WIDTH={wmsW}&HEIGHT={wmsH}&FORMAT=image/jpeg")
            };
        }

        public static string[] BuildLidarCandidateUrls(double northLat, double southLat, double eastLon, double westLon, int reqW, int reqH) =>
            BuildLidarCandidateUrlsWithLabels(northLat, southLat, eastLon, westLon, reqW, reqH).Select(x => x.Url).ToArray();

        private static async Task<byte[]> FetchSingleLidarSectorPixelsAsync(HttpClient http, double northLat, double southLat, double eastLon, double westLon, int targetW, int targetH, CancellationToken ct)
        {
            string cacheKey = string.Create(CultureInfo.InvariantCulture, $"LIDAR_V3_{northLat:0.0000}_{southLat:0.0000}_{eastLon:0.0000}_{westLon:0.0000}_{targetW}x{targetH}");
            if (RawBgraSectorCache.TryGetValue(cacheKey, out var cachedPx))
                return cachedPx;

            var candidates = BuildLidarCandidateUrlsWithLabels(northLat, southLat, eastLon, westLon, targetW, targetH);

            await LidarThrottle.WaitAsync(ct);
            try
            {
                if (RawBgraSectorCache.TryGetValue(cacheKey, out var cachedPx2))
                    return cachedPx2;

                foreach (var cand in candidates)
                {
                    if (ct.IsCancellationRequested) break;
                    try
                    {
                        byte[] bytes = await http.GetByteArrayAsync(cand.Url, ct);
                        if (TryDecodeToBgra32Pixels(bytes, targetW, targetH, out byte[] bgraPx) &&
                            HasValidHillshadeSignal(bgraPx, targetW, targetH))
                        {
                            LastLidarDiagnosticStatus = cand.Label;
                            if (RawBgraSectorCache.Count > 240) RawBgraSectorCache.Clear();
                            RawBgraSectorCache[cacheKey] = bgraPx;
                            return bgraPx;
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

            int gridN = totalWidthMeters > 3200 ? 3 : (totalWidthMeters > 1550 ? 2 : 1);

            int sectorPxW = Math.Clamp((int)Math.Round((viewW * 1.05) / gridN), 420, 860);
            int sectorPxH = Math.Clamp((int)Math.Round((viewH * 1.05) / gridN), 360, 860);

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
                        byte[] px = await FetchSingleLidarSectorPixelsAsync(http, secNorth, secSouth, secEast, secWest, sectorPxW, sectorPxH, ct);
                        return (capR, capC, px);
                    }, ct));
                }
            }

            var results = await Task.WhenAll(tasks);
            if (ct.IsCancellationRequested || results.All(x => x.Px == null))
            {
                LastLidarDiagnosticStatus = "Brak odpowiedzi NMT z GUGiK";
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

