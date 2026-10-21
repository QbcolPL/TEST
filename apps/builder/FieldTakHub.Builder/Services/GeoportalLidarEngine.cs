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
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Path = System.IO.Path;

namespace FieldTakHub.Builder.Services
{
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

        // Globalny współczynnik rozjaśniania cieni/gammy (ustawiany suwakiem w Sekcji 2)
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

        // Rozjaśnia ciemne partie mapy (szczególnie gęste lasy i cienie) za pomocą nieliniowej krzywej Gamma Shadow-Lift bez przepalania jasnych pól!
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
                        px[i]     = lut[px[i]];     // B
                        px[i + 1] = lut[px[i + 1]]; // G
                        px[i + 2] = lut[px[i + 2]]; // R
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
            // Gamma < 1.0 wyciąga szczegóły z ciemnego lasu, a delikatny offset podnosi najgłębsze czernie pod wydruk
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

        // Jaśniejszy balans światłocienia 3D dla LiDARu: mocniejsze białe podświetlenie stoków i brak zaczerniania ciemnych lasów!
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
                        // Wzmocnione jasne zbocza 3D, aby rzeźba terenu była wyraźnie widoczna na ciemnym lesie!
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
