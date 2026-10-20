#pragma warning disable CS0414, CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FieldTakHub.Builder.Services
{
    internal static class GeoportalLidarEngine
    {
        private static readonly ConcurrentDictionary<string, byte[]> RawBgraSectorCache = new ConcurrentDictionary<string, byte[]>();
        private static readonly string DiskCacheDir = InitializeDiskCacheDirectory();
        private static volatile int _preferredCandidateIndex = 0;

        public static HttpClient FastHttp { get; private set; } = CreateCleanHttp11Client();
        public static string LastLidarDiagnosticStatus { get; private set; } = "Gotowy";

        private static string InitializeDiskCacheDirectory()
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FieldTakHub",
                    "GeoCacheV24");
                Directory.CreateDirectory(dir);
                return dir;
            }
            catch
            {
                return "";
            }
        }

        // Czysty klient HTTP/1.1: brak zawieszających się strumieni HTTP/2 na serwerach GUGiK oraz szybki recykling połączeń (45 s)
        private static HttpClient CreateCleanHttp11Client()
        {
            try
            {
                var handler = new SocketsHttpHandler
                {
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                    PooledConnectionLifetime = TimeSpan.FromSeconds(45),
                    PooledConnectionIdleTimeout = TimeSpan.FromSeconds(20),
                    MaxConnectionsPerServer = 8,
                    EnableMultipleHttp2Connections = false,
                    ConnectTimeout = TimeSpan.FromSeconds(3.5)
                };
                handler.SslOptions.RemoteCertificateValidationCallback = (s, c, ch, e) => true;

                var client = new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromSeconds(8),
                    DefaultRequestVersion = HttpVersion.Version11,
                    DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
                };
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
                client.DefaultRequestHeaders.Referrer = new Uri("https://mapy.geoportal.gov.pl/");
                client.DefaultRequestHeaders.Accept.ParseAdd("image/jpeg,image/png,image/*;q=0.9,*/*;q=0.5");
                return client;
            }
            catch
            {
                return new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            }
        }

        // Wymusza natychmiastowe utworzenie nowej puli gniazd TCP (bez konieczności wyłączania programu!)
        public static void ResetHttpConnectionPool()
        {
            try
            {
                var old = FastHttp;
                FastHttp = CreateCleanHttp11Client();
                _ = Task.Run(async () =>
                {
                    await Task.Delay(5000);
                    try { old?.Dispose(); } catch { }
                });
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
            if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return true; // JPEG
            if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) return true; // PNG
            if (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46) return true; // GIF
            if (data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46) return true; // WEBP
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
            int grid = 14;
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

        private static void ApplyHillshadeContrastAndAlphaInPlace(byte[] bgraPixels, int width, int height, bool asTransparentOverlay)
        {
            if (bgraPixels == null || bgraPixels.Length < 16) return;

            int[] hist = new int[256];
            int validSamples = 0;
            int step = Math.Max(1, (bgraPixels.Length / 4) / 4000) * 4;

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
            if (validSamples > 40)
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

            for (int rawLum = 0; rawLum < 256; rawLum++)
            {
                int stretched = (int)Math.Clamp(((rawLum - lowClip) / span) * 255.0, 0.0, 255.0);
                if (!asTransparentOverlay)
                {
                    byte v = (byte)stretched;
                    lutB[rawLum] = v; lutG[rawLum] = v; lutR[rawLum] = v; lutA[rawLum] = 255;
                }
                else
                {
                    int deltaFromNeutral = stretched - 155;
                    if (deltaFromNeutral < -8)
                    {
                        int shadowStrength = -deltaFromNeutral;
                        lutB[rawLum] = 8;
                        lutG[rawLum] = 10;
                        lutR[rawLum] = 12;
                        lutA[rawLum] = (byte)Math.Clamp((int)(shadowStrength * 1.65) + 28, 30, 250);
                    }
                    else if (deltaFromNeutral > 12)
                    {
                        int lightStrength = deltaFromNeutral;
                        lutB[rawLum] = 242;
                        lutG[rawLum] = 248;
                        lutR[rawLum] = 255;
                        lutA[rawLum] = (byte)Math.Clamp((int)(lightStrength * 1.35) + 18, 20, 165);
                    }
                    else
                    {
                        byte gVal = (byte)stretched;
                        lutB[rawLum] = gVal;
                        lutG[rawLum] = gVal;
                        lutR[rawLum] = gVal;
                        lutA[rawLum] = 18;
                    }
                }
            }

            int len = bgraPixels.Length;
            for (int i = 0; i + 3 < len; i += 4)
            {
                if (bgraPixels[i + 3] < 15)
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
            string bbox3857   = string.Create(CultureInfo.InvariantCulture, $"{minX3857:0.00},{minY3857:0.00},{maxX3857:0.00},{maxY3857:0.00}");

            double spanMeters = Math.Max(80.0, maxE - minE);
            double aspect = Math.Max(0.3, (double)reqW / Math.Max(1, reqH));
            int wmsW = Math.Clamp((int)Math.Round(spanMeters / (3400.0 * 0.00028)), 380, 1180);
            int wmsH = Math.Clamp((int)Math.Round(wmsW / aspect), 300, 1180);

            // Dla dużych obszarów (> 3.6 km szerokości) najpierw odpytaj lekką warstwę GRID5, a dla zbliżeń taktycznych — GRID1
            if (spanMeters > 3600.0)
            {
                return new (string Label, string Url)[]
                {
                    ("EVRF2007 GRID5 (EPSG:2180)",
                     $"https://mapy.geoportal.gov.pl/wss/service/PZGIK/NMT/GRID5/WMS/ShadedRelief_EVRF2007?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=Raster&STYLES=&SRS=EPSG:2180&BBOX={bbox2180EN}&WIDTH={wmsW}&HEIGHT={wmsH}&FORMAT=image/jpeg"),
                    ("KRON86 GRID5 (EPSG:2180)",
                     $"https://mapy.geoportal.gov.pl/wss/service/PZGIK/NMT/GRID5/WMS/ShadedRelief?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=Raster&STYLES=&SRS=EPSG:2180&BBOX={bbox2180EN}&WIDTH={wmsW}&HEIGHT={wmsH}&FORMAT=image/jpeg"),
                    ("EVRF2007 GRID1 (EPSG:2180)",
                     $"https://mapy.geoportal.gov.pl/wss/service/PZGIK/NMT/GRID1/WMS/ShadedRelief_EVRF2007?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=Raster&STYLES=&SRS=EPSG:2180&BBOX={bbox2180EN}&WIDTH={wmsW}&HEIGHT={wmsH}&FORMAT=image/jpeg")
                };
            }

            return new (string Label, string Url)[]
            {
                ("EVRF2007 GRID1 (WMS 1.1.1 EPSG:2180)",
                 $"https://mapy.geoportal.gov.pl/wss/service/PZGIK/NMT/GRID1/WMS/ShadedRelief_EVRF2007?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=Raster&STYLES=&SRS=EPSG:2180&BBOX={bbox2180EN}&WIDTH={wmsW}&HEIGHT={wmsH}&FORMAT=image/jpeg"),

                ("KRON86 GRID1 (WMS 1.1.1 EPSG:2180)",
                 $"https://mapy.geoportal.gov.pl/wss/service/PZGIK/NMT/GRID1/WMS/ShadedRelief?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=Raster&STYLES=&SRS=EPSG:2180&BBOX={bbox2180EN}&WIDTH={wmsW}&HEIGHT={wmsH}&FORMAT=image/jpeg"),

                ("EVRF2007 GRID1 (WMS 1.3.0 EPSG:2180)",
                 $"https://mapy.geoportal.gov.pl/wss/service/PZGIK/NMT/GRID1/WMS/ShadedRelief_EVRF2007?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap&LAYERS=Raster&STYLES=default&CRS=EPSG:2180&BBOX={bbox2180NE}&WIDTH={wmsW}&HEIGHT={wmsH}&FORMAT=image/jpeg"),

                ("EVRF2007 GRID1 (EPSG:3857)",
                 $"https://mapy.geoportal.gov.pl/wss/service/PZGIK/NMT/GRID1/WMS/ShadedRelief_EVRF2007?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=Raster&STYLES=&SRS=EPSG:3857&BBOX={bbox3857}&WIDTH={wmsW}&HEIGHT={wmsH}&FORMAT=image/jpeg"),

                ("EVRF2007 GRID5 (EPSG:2180)",
                 $"https://mapy.geoportal.gov.pl/wss/service/PZGIK/NMT/GRID5/WMS/ShadedRelief_EVRF2007?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=Raster&STYLES=&SRS=EPSG:2180&BBOX={bbox2180EN}&WIDTH={wmsW}&HEIGHT={wmsH}&FORMAT=image/jpeg")
            };
        }

        public static string[] BuildLidarCandidateUrls(double northLat, double southLat, double eastLon, double westLon, int reqW, int reqH) =>
            BuildLidarCandidateUrlsWithLabels(northLat, southLat, eastLon, westLon, reqW, reqH).Select(x => x.Url).ToArray();

        // Pojedyncze, sekwencyjne pobranie z krótkim timeoutem 4.5s (ZERO równoległych wyścigów zapychających serwer GUGiK!)
        private static async Task<byte[]> FetchSingleLidarSheetPixelsAsync(double northLat, double southLat, double eastLon, double westLon, int targetW, int targetH, CancellationToken ct)
        {
            string cacheKey = string.Create(CultureInfo.InvariantCulture, $"LIDAR_SINGLE_{northLat:0.0000}_{southLat:0.0000}_{eastLon:0.0000}_{westLon:0.0000}_{targetW}x{targetH}");
            if (RawBgraSectorCache.TryGetValue(cacheKey, out var cachedPx))
                return cachedPx;

            byte[] diskRawBytes = TryReadDiskCache(cacheKey);
            if (diskRawBytes != null && TryDecodeToBgra32Pixels(diskRawBytes, targetW, targetH, out byte[] diskBgra) && HasValidHillshadeSignal(diskBgra, targetW, targetH))
            {
                LastLidarDiagnosticStatus = "SSD Cache";
                RawBgraSectorCache[cacheKey] = diskBgra;
                return diskBgra;
            }

            var rawCandidates = BuildLidarCandidateUrlsWithLabels(northLat, southLat, eastLon, westLon, targetW, targetH);

            int pref = Math.Clamp(_preferredCandidateIndex, 0, rawCandidates.Length - 1);
            var orderedIndices = new List<int> { pref };
            for (int i = 0; i < rawCandidates.Length; i++)
                if (i != pref) orderedIndices.Add(i);

            foreach (int cIdx in orderedIndices)
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    using var reqCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    reqCts.CancelAfter(4500); // Sztywny limit 4.5s na pojedynczy serwer — nigdy nie zawiesi aplikacji!

                    byte[] bytes = await FastHttp.GetByteArrayAsync(rawCandidates[cIdx].Url, reqCts.Token);
                    if (bytes != null && TryDecodeToBgra32Pixels(bytes, targetW, targetH, out byte[] bgraPx) && HasValidHillshadeSignal(bgraPx, targetW, targetH))
                    {
                        _preferredCandidateIndex = cIdx;
                        LastLidarDiagnosticStatus = rawCandidates[cIdx].Label;
                        if (RawBgraSectorCache.Count > 180) RawBgraSectorCache.Clear();
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

        // Zawsze 1 lekki arkusz (1x1) zamiast 4 lub 9 sektorów — 4-krotnie mniejsze obciążenie sieci i pamięci!
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
            int targetW = Math.Clamp((int)Math.Round(viewW * 0.92), 420, 960);
            int targetH = Math.Clamp((int)Math.Round(viewH * 0.92), 320, 960);

            byte[] rawPixels = await FetchSingleLidarSheetPixelsAsync(northLat, southLat, eastLon, westLon, targetW, targetH, ct);
            if (ct.IsCancellationRequested || rawPixels == null)
            {
                return null;
            }

            byte[] workingPixels = (byte[])rawPixels.Clone();
            ApplyHillshadeContrastAndAlphaInPlace(workingPixels, targetW, targetH, convertToTransparentOverlay);

            var finalBmp = BitmapSource.Create(targetW, targetH, 96, 96, PixelFormats.Bgra32, null, workingPixels, targetW * 4);
            finalBmp.Freeze();
            return finalBmp;
        }
    }
}
