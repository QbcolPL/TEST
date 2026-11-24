using System;
using System.Threading.Tasks;

namespace FieldTakHub.Builder.Services.MapImagery;

/// <summary>Obraz BGRA32 (kanał alfa bez premultiplikacji), wiersze bez wyrównania.</summary>
internal sealed class BgraImage
{
    public BgraImage(int width, int height, byte[]? pixels = null)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        Width = width;
        Height = height;
        Pixels = pixels ?? new byte[checked(width * height * 4)];
        if (Pixels.Length < width * height * 4) throw new ArgumentException("Bufor pikseli jest za mały.", nameof(pixels));
    }

    public int Width { get; }
    public int Height { get; }
    public int Stride => Width * 4;
    public byte[] Pixels { get; }
}

/// <summary>
/// Widok w odwzorowaniu Web Mercator: piksel (x, y) obrazu ↔ współrzędne kafelków XYZ na poziomie <see cref="Zoom"/>.
/// To ten sam model co podgląd mapy w edytorze (środek obrazu = CenterTileX/Y, TilePx pikseli na kafelek),
/// więc ekran, mozaika LiDAR i wydruk liczą położenie identycznie.
/// </summary>
internal readonly record struct MercatorViewport(int Zoom, double CenterTileX, double CenterTileY, double TilePx, double Width, double Height)
{
    public static MercatorViewport FromCenter(double lat, double lon, int zoom, double tilePx, double width, double height)
    {
        var (tx, ty) = LatLonToTile(lat, lon, zoom);
        return new MercatorViewport(zoom, tx, ty, tilePx, width, height);
    }

    public static (double X, double Y) LatLonToTile(double lat, double lon, int zoom)
    {
        double latRad = Math.Clamp(lat, -85.0511, 85.0511) * Math.PI / 180.0, n = Math.Pow(2.0, zoom);
        return ((lon + 180.0) / 360.0 * n, (1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * n);
    }

    public static (double Lat, double Lon) TileToLatLon(double x, double y, int zoom)
    {
        double n = Math.Pow(2.0, zoom);
        return (Math.Atan(Math.Sinh(Math.PI * (1.0 - 2.0 * y / n))) * 180.0 / Math.PI, x / n * 360.0 - 180.0);
    }

    public double TileX(double x) => CenterTileX + (x - Width / 2.0) / TilePx;
    public double TileY(double y) => CenterTileY + (y - Height / 2.0) / TilePx;
    public double PixelX(double tileX) => Width / 2.0 + (tileX - CenterTileX) * TilePx;
    public double PixelY(double tileY) => Height / 2.0 + (tileY - CenterTileY) * TilePx;

    public (double Lat, double Lon) ToLatLon(double x, double y) => TileToLatLon(TileX(x), TileY(y), Zoom);

    public (double X, double Y) ToPixel(double lat, double lon)
    {
        var (tx, ty) = LatLonToTile(lat, lon, Zoom);
        return (PixelX(tx), PixelY(ty));
    }

    public (double Lat, double Lon) Center => TileToLatLon(CenterTileX, CenterTileY, Zoom);

    /// <summary>Ten sam środek i skala, inny rozmiar obrazu (np. zapas wokół widoku).</summary>
    public MercatorViewport Resized(double width, double height) => this with { Width = width, Height = height };

    /// <summary>Ten sam teren w innej rozdzielczości: <paramref name="factor"/> pikseli wyniku na piksel widoku.</summary>
    public MercatorViewport Scaled(double factor) => this with { TilePx = TilePx * factor, Width = Width * factor, Height = Height * factor };

    /// <summary>Widok przesunięty o (dx, dy) pikseli.</summary>
    public MercatorViewport Shifted(double dx, double dy) => this with { CenterTileX = CenterTileX + dx / TilePx, CenterTileY = CenterTileY + dy / TilePx };

    /// <summary>
    /// Poziom kafelków o boku <paramref name="tileImagePx"/> pikseli dający rozdzielczość zbliżoną do obrazu
    /// (<paramref name="roundUp"/>: co najmniej piksel kafelka na piksel obrazu — do wydruku).
    /// </summary>
    public int TileZoomFor(int tileImagePx, int minZoom, int maxZoom, bool roundUp)
    {
        double exact = Zoom + Math.Log(TilePx / Math.Max(1, tileImagePx), 2.0);
        int z = roundUp ? (int)Math.Ceiling(exact - 1e-6) : (int)Math.Round(exact);
        return Math.Clamp(z, minZoom, maxZoom);
    }

    /// <summary>Zakres kafelków (włącznie) poziomu <paramref name="z"/> przykrywający cały obraz. X może wyjść poza [0, 2^z) — zawija wywołujący.</summary>
    public (int X0, int Y0, int X1, int Y1) TileRange(int z)
    {
        double f = Math.Pow(2.0, z - Zoom);
        int max = (1 << z) - 1;
        int x0 = (int)Math.Floor(TileX(0) * f), x1 = (int)Math.Floor(TileX(Width) * f - 1e-9);
        int y0 = (int)Math.Floor(TileY(0) * f), y1 = (int)Math.Floor(TileY(Height) * f - 1e-9);
        return (x0, Math.Clamp(y0, 0, max), Math.Max(x0, x1), Math.Clamp(Math.Max(y0, y1), 0, max));
    }
}

/// <summary>Kafelki jednego poziomu w prostokącie kolumn/wierszy; wszystkie przeskalowane do jednego rozmiaru.</summary>
internal sealed class TileGrid
{
    private readonly BgraImage?[] _tiles;

    public TileGrid(int zoom, int x0, int y0, int x1, int y1, int tileSize)
    {
        if (x1 < x0 || y1 < y0) throw new ArgumentException("Pusty zakres kafelków.");
        if (tileSize < 16) throw new ArgumentOutOfRangeException(nameof(tileSize));
        Zoom = zoom; X0 = x0; Y0 = y0;
        Columns = x1 - x0 + 1;
        Rows = y1 - y0 + 1;
        TileSize = tileSize;
        _tiles = new BgraImage?[checked(Columns * Rows)];
    }

    public int Zoom { get; }
    public int X0 { get; }
    public int Y0 { get; }
    public int Columns { get; }
    public int Rows { get; }
    public int TileSize { get; }

    public int LoadedCount
    {
        get
        {
            int n = 0;
            foreach (var t in _tiles) if (t != null) n++;
            return n;
        }
    }

    /// <summary>Wstawia kafelek (x, y) w numeracji XYZ bez zawijania; inny rozmiar jest przeskalowywany.</summary>
    public void Set(int x, int y, BgraImage? tile)
    {
        int col = x - X0, row = y - Y0;
        if (tile == null || col < 0 || row < 0 || col >= Columns || row >= Rows) return;
        if (tile.Width != TileSize || tile.Height != TileSize) tile = MercatorTileMosaic.Resize(tile, TileSize, TileSize);
        _tiles[row * Columns + col] = tile;
    }

    public BgraImage? Get(int col, int row) =>
        col < 0 || row < 0 || col >= Columns || row >= Rows ? null : _tiles[row * Columns + col];
}

/// <summary>
/// Składanie obrazu widoku z kafelków XYZ (interpolacja dwuliniowa, przy zmniejszaniu nadpróbkowanie 2×2)
/// oraz proste operacje na pikselach używane przez podgląd LiDAR i wydruk. Bez WPF — testowane jednostkowo.
/// </summary>
internal static class MercatorTileMosaic
{
    /// <summary>Renderuje obraz o rozmiarze widoku (zaokrąglonym) z kafelków siatki.</summary>
    public static BgraImage Render(MercatorViewport view, TileGrid grid)
    {
        int outW = Math.Max(1, (int)Math.Round(view.Width)), outH = Math.Max(1, (int)Math.Round(view.Height));
        var output = new BgraImage(outW, outH);
        double f = Math.Pow(2.0, grid.Zoom - view.Zoom);
        int size = grid.TileSize;
        double ratio = f * size / view.TilePx;                       // piksele kafelka na piksel obrazu
        double gx0 = (view.TileX(0.0) * f - grid.X0) * size;          // współrzędna kafelkowa lewej krawędzi obrazu
        double gy0 = (view.TileY(0.0) * f - grid.Y0) * size;
        int maxGx = grid.Columns * size - 1, maxGy = grid.Rows * size - 1;
        bool supersample = ratio > 1.3;
        double[] offsets = supersample ? new[] { 0.25, 0.75 } : new[] { 0.5 };
        int samples = offsets.Length * offsets.Length;
        byte[] dst = output.Pixels;

        Parallel.For(0, outH, y =>
        {
            int rowBase = y * outW * 4;
            for (int x = 0; x < outW; x++)
            {
                double b = 0, g = 0, r = 0, a = 0;
                foreach (double oy in offsets)
                {
                    double sy = gy0 + (y + oy) * ratio - 0.5;
                    foreach (double ox in offsets)
                    {
                        double sx = gx0 + (x + ox) * ratio - 0.5;
                        SampleBilinear(grid, sx, sy, maxGx, maxGy, ref b, ref g, ref r, ref a);
                    }
                }
                int o = rowBase + x * 4;
                if (a <= 1e-6) { dst[o] = 0; dst[o + 1] = 0; dst[o + 2] = 0; dst[o + 3] = 0; continue; }
                dst[o] = ToByte(b / a);
                dst[o + 1] = ToByte(g / a);
                dst[o + 2] = ToByte(r / a);
                dst[o + 3] = ToByte(a * 255.0 / samples);
            }
        });
        return output;
    }

    private static void SampleBilinear(TileGrid grid, double sx, double sy, int maxGx, int maxGy, ref double b, ref double g, ref double r, ref double a)
    {
        int ix = (int)Math.Floor(sx), iy = (int)Math.Floor(sy);
        double fx = sx - ix, fy = sy - iy;
        Accumulate(grid, ix, iy, (1 - fx) * (1 - fy), maxGx, maxGy, ref b, ref g, ref r, ref a);
        Accumulate(grid, ix + 1, iy, fx * (1 - fy), maxGx, maxGy, ref b, ref g, ref r, ref a);
        Accumulate(grid, ix, iy + 1, (1 - fx) * fy, maxGx, maxGy, ref b, ref g, ref r, ref a);
        Accumulate(grid, ix + 1, iy + 1, fx * fy, maxGx, maxGy, ref b, ref g, ref r, ref a);
    }

    private static void Accumulate(TileGrid grid, int gx, int gy, double weight, int maxGx, int maxGy, ref double b, ref double g, ref double r, ref double a)
    {
        if (weight <= 0) return;
        gx = Math.Clamp(gx, 0, maxGx);
        gy = Math.Clamp(gy, 0, maxGy);
        int size = grid.TileSize;
        int col = gx / size, row = gy / size;
        var tile = grid.Get(col, row);
        if (tile == null) return;
        int idx = ((gy - row * size) * size + (gx - col * size)) * 4;
        byte[] p = tile.Pixels;
        double wa = weight * p[idx + 3] / 255.0;      // waga z kryciem (interpolacja z premultiplikacją)
        b += wa * p[idx];
        g += wa * p[idx + 1];
        r += wa * p[idx + 2];
        a += wa;
    }

    private static byte ToByte(double v) => v <= 0 ? (byte)0 : v >= 255 ? (byte)255 : (byte)(v + 0.5);

    /// <summary>Skalowanie dwuliniowe (do wyrównania rozmiarów kafelków z różnych źródeł).</summary>
    public static BgraImage Resize(BgraImage src, int width, int height)
    {
        if (src.Width == width && src.Height == height) return src;
        var dst = new BgraImage(width, height);
        double sxRatio = (double)src.Width / width, syRatio = (double)src.Height / height;
        for (int y = 0; y < height; y++)
        {
            double sy = (y + 0.5) * syRatio - 0.5;
            int y0 = Math.Clamp((int)Math.Floor(sy), 0, src.Height - 1), y1 = Math.Min(y0 + 1, src.Height - 1);
            double fy = Math.Clamp(sy - y0, 0, 1);
            for (int x = 0; x < width; x++)
            {
                double sx = (x + 0.5) * sxRatio - 0.5;
                int x0 = Math.Clamp((int)Math.Floor(sx), 0, src.Width - 1), x1 = Math.Min(x0 + 1, src.Width - 1);
                double fx = Math.Clamp(sx - x0, 0, 1);
                int o = (y * width + x) * 4;
                for (int c = 0; c < 4; c++)
                {
                    double top = src.Pixels[(y0 * src.Width + x0) * 4 + c] * (1 - fx) + src.Pixels[(y0 * src.Width + x1) * 4 + c] * fx;
                    double bottom = src.Pixels[(y1 * src.Width + x0) * 4 + c] * (1 - fx) + src.Pixels[(y1 * src.Width + x1) * 4 + c] * fx;
                    dst.Pixels[o + c] = ToByte(top * (1 - fy) + bottom * fy);
                }
            }
        }
        return dst;
    }

    /// <summary>
    /// Ortofotomapa wysokiej rozdzielczości ma białe/puste pola tam, gdzie GUGiK nie ma nalotu —
    /// te piksele uzupełniamy kafelkiem standardowym (przeskalowanym do rozmiaru kafelka HR).
    /// </summary>
    public static BgraImage FillNoDataFrom(BgraImage highRes, BgraImage standard)
    {
        var std = Resize(standard, highRes.Width, highRes.Height);
        var result = new BgraImage(highRes.Width, highRes.Height, (byte[])highRes.Pixels.Clone());
        byte[] p = result.Pixels, s = std.Pixels;
        for (int i = 0; i + 3 < p.Length; i += 4)
        {
            bool noData = p[i + 3] < 20 || (p[i] >= 250 && p[i + 1] >= 250 && p[i + 2] >= 250);
            if (!noData) continue;
            p[i] = s[i]; p[i + 1] = s[i + 1]; p[i + 2] = s[i + 2]; p[i + 3] = s[i + 3];
        }
        return result;
    }

    /// <summary>Udział pikseli pustych lub białych (próbkowanie co ~1/24 boku).</summary>
    public static double NoDataRatio(BgraImage img)
    {
        int total = 0, empty = 0;
        for (int r = 1; r < 24; r++)
        {
            int y = img.Height * r / 24;
            for (int c = 1; c < 24; c++)
            {
                int x = img.Width * c / 24, i = (y * img.Width + x) * 4;
                total++;
                byte[] p = img.Pixels;
                if (p[i + 3] < 20 || (p[i] >= 250 && p[i + 1] >= 250 && p[i + 2] >= 250)) empty++;
            }
        }
        return total == 0 ? 1.0 : (double)empty / total;
    }

    /// <summary>Tablica LUT na kanały RGB (rozjaśnienie cieni podkładu).</summary>
    public static void ApplyLut(BgraImage img, byte[] lut)
    {
        if (lut == null || lut.Length < 256) return;
        byte[] p = img.Pixels;
        for (int i = 0; i + 3 < p.Length; i += 4)
        {
            p[i] = lut[p[i]];
            p[i + 1] = lut[p[i + 1]];
            p[i + 2] = lut[p[i + 2]];
        }
    }

    /// <summary>Nakłada <paramref name="overlay"/> (ten sam rozmiar) z kryciem <paramref name="opacity"/> na <paramref name="target"/>.</summary>
    public static void BlendOver(BgraImage target, BgraImage overlay, double opacity)
    {
        if (overlay.Width != target.Width || overlay.Height != target.Height) overlay = Resize(overlay, target.Width, target.Height);
        double k = Math.Clamp(opacity, 0.0, 1.0);
        byte[] t = target.Pixels, o = overlay.Pixels;
        for (int i = 0; i + 3 < t.Length; i += 4)
        {
            double a = o[i + 3] / 255.0 * k;
            if (a <= 0.0) continue;
            double ta = t[i + 3] / 255.0;
            double outA = a + ta * (1 - a);
            if (outA <= 1e-6) continue;
            for (int c = 0; c < 3; c++)
                t[i + c] = ToByte((o[i + c] * a + t[i + c] * ta * (1 - a)) / outA);
            t[i + 3] = ToByte(outA * 255.0);
        }
    }

    /// <summary>Przyciemnienie (czarna nakładka o kryciu <paramref name="amount"/>), jak suwak „przyciemnij podkład”.</summary>
    public static void Darken(BgraImage img, double amount)
    {
        double k = 1.0 - Math.Clamp(amount, 0.0, 1.0);
        if (k >= 0.999) return;
        byte[] p = img.Pixels;
        for (int i = 0; i + 3 < p.Length; i += 4)
        {
            p[i] = ToByte(p[i] * k);
            p[i + 1] = ToByte(p[i + 1] * k);
            p[i + 2] = ToByte(p[i + 2] * k);
        }
    }
}
