using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FieldTakHub.Builder.Services.MapImagery;

namespace FieldTakHub.Builder.Services
{
    /// <summary>
    /// Pobieranie podkładów kafelkami: klucze pamięci podręcznej, łączenie ortofotomapy HR ze standardową,
    /// mozaika cieniowania LiDAR (jeden kontrast dla całego widoku) oraz obraz podkładu do wydruku.
    /// </summary>
    public static partial class TacticalMapGeneratorView
    {
        /// <summary>Od tego poziomu warstwa „HIGH-RES” pobiera nalot 5–10 cm (niżej i tak nie widać różnicy).</summary>
        private const int HiresMinZoom = 17;
        /// <summary>Zapas mozaiki LiDAR wokół widoku — przesuwanie w jego obrębie nie wymaga przebudowy.</summary>
        private const double LidarSheetOverscan = 1.4;
        private const long DecodedCacheBudgetBytes = 384L * 1024 * 1024;
        private const int MaxTilesPerImage = 1600;

        private static readonly ConcurrentQueue<string> DecodedCacheOrder = new ConcurrentQueue<string>();
        private static long _decodedCacheBytes;
        private static int _tileRefreshQueued;
        private static string _lidarReadyKind = "";
        private static double _lidarReadyLat = double.NaN, _lidarReadyLon = double.NaN;
        private static int _lidarReadyScale = -1;

        private static long ApproxBitmapBytes(BitmapSource bmp) => bmp == null ? 0 : (long)bmp.PixelWidth * bmp.PixelHeight * 4;

        /// <summary>Zapamiętuje zdekodowany kafelek; przy przekroczeniu budżetu pamięci usuwa najstarsze (zamiast czyścić wszystko naraz).</summary>
        private static void RememberDecodedImage(string key, BitmapSource bmp)
        {
            if (bmp == null || string.IsNullOrEmpty(key)) return;
            if (DecodedImageCache.TryAdd(key, bmp))
            {
                DecodedCacheOrder.Enqueue(key);
                Interlocked.Add(ref _decodedCacheBytes, ApproxBitmapBytes(bmp));
            }
            else
            {
                DecodedImageCache[key] = bmp;
            }

            while (Interlocked.Read(ref _decodedCacheBytes) > DecodedCacheBudgetBytes)
            {
                if (!DecodedCacheOrder.TryDequeue(out string oldest))
                {
                    Interlocked.Exchange(ref _decodedCacheBytes, DecodedImageCache.Values.Sum(b => ApproxBitmapBytes(b)));
                    break;
                }
                if (DecodedImageCache.TryRemove(oldest, out var removed))
                    Interlocked.Add(ref _decodedCacheBytes, -ApproxBitmapBytes(removed));
            }
        }

        private static void ClearDecodedImageCache()
        {
            DecodedImageCache.Clear();
            while (DecodedCacheOrder.TryDequeue(out _)) { }
            Interlocked.Exchange(ref _decodedCacheBytes, 0);
        }

        /// <summary>Wiele kafelków przychodzi naraz — odświeżamy płótno raz na cykl dyspozytora, a nie po każdym.</summary>
        private static void QueueTileCanvasRefresh()
        {
            var app = Application.Current;
            if (app == null || Interlocked.Exchange(ref _tileRefreshQueued, 1) == 1) return;
            _ = app.Dispatcher.BeginInvoke(new Action(() =>
            {
                Interlocked.Exchange(ref _tileRefreshQueued, 0);
                if (_fullScreenOverlay?.Visibility == Visibility.Visible && _activeLayer != null && !_activeLayer.IsViewMosaic)
                    RefreshTileCanvasesOnly();
            }), DispatcherPriority.Background);
        }

        private static TacticalMapLayerDef LayerById(string id) => Layers.FirstOrDefault(l => l.Id == id);

        /// <summary>Poniżej <see cref="HiresMinZoom"/> warstwa HR korzysta z kafelków ortofotomapy standardowej (WMTS).</summary>
        private static TacticalMapLayerDef ResolveTileLayer(TacticalMapLayerDef layer, int zoom) =>
            layer != null && layer.Id == "geoportal_hires" && zoom < HiresMinZoom ? (LayerById("geoportal_std") ?? layer) : layer;

        /// <summary>Klucz kafelka w pamięci: adres główny (dla HR — z dopiskiem, bo obraz jest łączony z dwóch źródeł).</summary>
        private static string TileCacheKey(TacticalMapLayerDef layer, int x, int y, int z)
        {
            var effective = ResolveTileLayer(layer, z);
            string url = BuildTileUrl(effective, x, y, z);
            return effective.Id == "geoportal_hires" ? "HR+STD|" + url : url;
        }

        private static async Task<BitmapSource> FetchLayerTileAsync(TacticalMapLayerDef layer, int x, int y, int z, CancellationToken ct)
        {
            var effective = ResolveTileLayer(layer, z);
            if (effective == null) return null;
            string key = TileCacheKey(effective, x, y, z);
            if (DecodedImageCache.TryGetValue(key, out var ready)) return ready;

            if (effective.Id != "geoportal_hires")
            {
                return await FetchDecodedImageAsync(
                    BuildTileUrl(effective, x, y, z), BuildFallbackTileUrl(effective, x, y, z), ct,
                    BuildSecondFallbackTileUrl(effective, x, y, z), effective.BlankMeansNoData);
            }

            // Nalot HR istnieje głównie dla miast — puste (białe) pola uzupełniamy ortofotomapą standardową.
            var hiRes = await FetchDecodedImageAsync(BuildTileUrl(effective, x, y, z), null, ct, null, blankMeansNoData: true);
            BitmapSource result = hiRes;
            if (hiRes == null || MercatorTileMosaic.NoDataRatio(ToBgraImage(hiRes)) > 0.02)
            {
                var standard = await FetchLayerTileAsync(LayerById("geoportal_std"), x, y, z, ct);
                if (hiRes != null && standard != null)
                    result = ToFrozenBitmap(MercatorTileMosaic.FillNoDataFrom(ToBgraImage(hiRes), ToBgraImage(standard)));
                else
                    result = standard ?? hiRes;
            }
            if (result != null) RememberDecodedImage(key, result);
            return result;
        }

        internal static BgraImage ToBgraImage(BitmapSource source)
        {
            BitmapSource src = source;
            if (src.Format != PixelFormats.Bgra32)
            {
                var converted = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
                converted.Freeze();
                src = converted;
            }
            var img = new BgraImage(src.PixelWidth, src.PixelHeight);
            src.CopyPixels(img.Pixels, img.Stride, 0);
            return img;
        }

        internal static BitmapSource ToFrozenBitmap(BgraImage img)
        {
            var bmp = BitmapSource.Create(img.Width, img.Height, 96, 96, PixelFormats.Bgra32, null, img.Pixels, img.Stride);
            bmp.Freeze();
            return bmp;
        }

        /// <summary>
        /// Składa obraz widoku z kafelków poziomu <paramref name="z"/> (pobieranych równolegle, z pamięci podręcznej gdy są).
        /// Zwraca null, gdy nie udało się pobrać żadnego kafelka.
        /// </summary>
        private static async Task<BgraImage> ComposeLayerImageAsync(TacticalMapLayerDef layer, MercatorViewport view, int z, IProgress<string> progress, string progressLabel, CancellationToken ct)
        {
            var range = view.TileRange(z);
            while ((long)(range.X1 - range.X0 + 1) * (range.Y1 - range.Y0 + 1) > MaxTilesPerImage && z > 1)
                range = view.TileRange(--z);

            var effective = ResolveTileLayer(layer, z);
            var grid = new TileGrid(z, range.X0, range.Y0, range.X1, range.Y1, effective.TilePixelSize);
            int total = grid.Columns * grid.Rows, done = 0, n = 1 << z;
            var tasks = new List<Task>(total);
            for (int ty = range.Y0; ty <= range.Y1; ty++)
            {
                for (int tx = range.X0; tx <= range.X1; tx++)
                {
                    int cx = tx, cy = ty, wrapped = ((tx % n) + n) % n;
                    tasks.Add(Task.Run(async () =>
                    {
                        var bmp = await FetchLayerTileAsync(effective, wrapped, cy, z, ct);
                        if (bmp != null)
                        {
                            try
                            {
                                var tile = ToBgraImage(bmp);
                                lock (grid) grid.Set(cx, cy, tile);
                            }
                            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is NotSupportedException)
                            {
                                // Uszkodzony obraz kafelka — zostaje puste pole, reszta mozaiki się składa.
                            }
                        }
                        int finished = Interlocked.Increment(ref done);
                        if (progress != null && (finished % 8 == 0 || finished == total))
                            progress.Report($"⏳ {progressLabel}: {finished}/{total} kafelków");
                    }, ct));
                }
            }
            await Task.WhenAll(tasks);
            ct.ThrowIfCancellationRequested();
            if (grid.LoadedCount == 0) return null;
            return await Task.Run(() => MercatorTileMosaic.Render(view, grid), ct);
        }

        /// <summary>Cieniowanie NMT dla widoku: mozaika kafelków + jeden wspólny kontrast (bez szwów między kafelkami).</summary>
        private static async Task<BgraImage> BuildLidarImageAsync(MercatorViewport view, bool asOverlay, int? forcedZoom, bool forPrint, IProgress<string> progress, CancellationToken ct)
        {
            var lidar = LayerById("geoportal_lidar");
            if (lidar == null) return null;
            int z = forcedZoom ?? view.TileZoomFor(lidar.TilePixelSize, 6, lidar.MaxZoom, roundUp: forPrint);
            var img = await ComposeLayerImageAsync(lidar, view, Math.Clamp(z, 6, lidar.MaxZoom), progress, "Cieniowanie LiDAR", ct);
            if (img == null) return null;
            await Task.Run(() => GeoportalLidarEngine.ApplyHillshadeContrastAndAlphaInPlace(img.Pixels, img.Width, img.Height, asOverlay), ct);
            return img;
        }

        /// <summary>Widok mozaiki LiDAR dla ekranu: obszar arkusza z zapasem, ok. 1 piksel obrazu na piksel ekranu.</summary>
        private static MercatorViewport BuildLidarSheetViewport(double centerLat, double centerLon)
        {
            var (w, h) = GetExactCanvasSize();
            var view = MercatorViewport.FromCenter(centerLat, centerLon, _zoom, GetScaledTileSizePx(), w, h)
                .Resized(w * LidarSheetOverscan, h * LidarSheetOverscan);
            double factor = Math.Min(1.0, 2600.0 / Math.Max(1.0, Math.Max(view.Width, view.Height)));
            return view.Scaled(factor);
        }

        private static async Task<GeoAnchoredWmsSheet> BuildLidarSheetAsync(MercatorViewport view, bool asOverlay, double centerLat, double centerLon, int scale, IProgress<string> progress, CancellationToken ct)
        {
            var img = await BuildLidarImageAsync(view, asOverlay, null, false, progress, ct);
            if (img == null) return null;
            var (north, west) = view.ToLatLon(0, 0);
            var (south, east) = view.ToLatLon(img.Width, img.Height);
            return new GeoAnchoredWmsSheet
            {
                LayerId = "geoportal_lidar",
                NorthLat = north, SouthLat = south, EastLon = east, WestLon = west,
                CenterLat = centerLat, CenterLon = centerLon, ScaleDenominator = scale,
                Bitmap = ToFrozenBitmap(img)
            };
        }

        /// <summary>True, gdy widok wyszedł poza zapas ostatnio zamówionej mozaiki LiDAR albo zmieniła się skala.</summary>
        private static bool IsLoadedLidarMosaicStale() =>
            _wmsLoadedScaleDenominator != _targetScaleDenominator || !IsViewWithinMosaicMargin(_wmsLoadedCenterLat, _wmsLoadedCenterLon);

        /// <summary>True, gdy gotowa (wczytana) mozaika danego rodzaju nadal przykrywa widok — nie trzeba jej składać od nowa.</summary>
        private static bool IsReadyLidarMosaicCovering(string kind) =>
            _lidarReadyKind == kind && _lidarReadyScale == _targetScaleDenominator && IsViewWithinMosaicMargin(_lidarReadyLat, _lidarReadyLon);

        private static void MarkLidarMosaicReady(string kind, double lat, double lon, int scale)
        {
            _lidarReadyKind = kind;
            _lidarReadyLat = lat;
            _lidarReadyLon = lon;
            _lidarReadyScale = scale;
        }

        private static void ForgetReadyLidarMosaic() => _lidarReadyKind = "";

        private static bool IsViewWithinMosaicMargin(double mosaicCenterLat, double mosaicCenterLon)
        {
            if (double.IsNaN(mosaicCenterLat) || mosaicCenterLat < -90.0 || mosaicCenterLat > 90.0) return false;
            var (w, h) = GetExactCanvasSize();
            if (w < 2 || h < 2) return true;
            Point mosaicCenter = LatLonToScreen(mosaicCenterLat, mosaicCenterLon, w, h);
            double marginX = (LidarSheetOverscan - 1.0) / 2.0 * w * 0.8, marginY = (LidarSheetOverscan - 1.0) / 2.0 * h * 0.8;
            return Math.Abs(mosaicCenter.X - w / 2.0) <= marginX && Math.Abs(mosaicCenter.Y - h / 2.0) <= marginY;
        }

        /// <summary>
        /// Podkład do wydruku dla widoku <paramref name="view"/> (piksele strony): kafelki w rozdzielczości wydruku,
        /// rozjaśnienie, cieniowanie LiDAR z kryciem i przyciemnienie — w tej samej kolejności co na ekranie.
        /// W podglądzie używa poziomu kafelków z ekranu, więc korzysta z tego, co już jest w pamięci.
        /// </summary>
        private static async Task<BitmapSource> RenderPrintImageryAsync(MercatorViewport view, bool preview, IProgress<string> progress, CancellationToken ct)
        {
            var layer = _activeLayer ?? Layers[0];
            double brightness = _mapBrightnessBoost, dimmer = _mapDimmerOpacity, lidarOpacity = _lidarOpacity;
            bool lidarOverlay = _showLidarSingleWms && !layer.IsViewMosaic;
            int width = Math.Max(1, (int)Math.Round(view.Width)), height = Math.Max(1, (int)Math.Round(view.Height));

            // Podgląd: te same poziomy kafelków co ekran (są już w pamięci). PDF: poziom dobrany do DPI wydruku.
            int? lidarZoom = null;
            var lidarDef = LayerById("geoportal_lidar");
            if (preview && lidarDef != null)
                lidarZoom = BuildLidarSheetViewport(_centerLat, _centerLon).TileZoomFor(lidarDef.TilePixelSize, 6, lidarDef.MaxZoom, roundUp: false);

            BgraImage image;
            if (layer.IsViewMosaic)
            {
                image = await BuildLidarImageAsync(view, false, lidarZoom, !preview, progress, ct);
            }
            else
            {
                int z;
                if (preview) z = Math.Min(_zoom, layer.MaxZoom);
                else
                {
                    var sized = (layer.Id == "geoportal_hires" && view.TileZoomFor(512, 1, 22, true) < HiresMinZoom) ? (LayerById("geoportal_std") ?? layer) : layer;
                    z = view.TileZoomFor(sized.TilePixelSize, 1, layer.MaxZoom, roundUp: true);
                }
                image = await ComposeLayerImageAsync(layer, view, z, progress, "Podkład", ct);
            }

            BgraImage shade = lidarOverlay ? await BuildLidarImageAsync(view, true, lidarZoom, !preview, progress, ct) : null;

            return await Task.Run(() =>
            {
                var result = image ?? new BgraImage(width, height);
                if (brightness > 0.01) MercatorTileMosaic.ApplyLut(result, GeoportalLidarEngine.BuildGammaShadowLiftLut(brightness));
                if (shade != null) MercatorTileMosaic.BlendOver(result, shade, lidarOpacity);
                if (dimmer > 0.01) MercatorTileMosaic.Darken(result, dimmer);
                return ToFrozenBitmap(result);
            }, ct);
        }
    }
}
