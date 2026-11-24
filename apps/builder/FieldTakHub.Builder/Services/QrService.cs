using System;
using System.Windows;
using System.Windows.Media;
using QRCoder;

namespace FieldTakHub.Builder.Services;

/// <summary>
/// Lokalny generator kodów QR (QRCoder). Wszystkie kody QR Buildera powstają tutaj:
/// bez wysyłania treści (adresów, kluczy PSK, JSON offline) do zewnętrznych usług
/// i bez „atrap” rysowanych z hasha SHA-256, których nie da się zeskanować.
/// </summary>
public sealed class QrService
{
    public byte[] CreatePng(string value)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(value, QRCodeGenerator.ECCLevel.Q);
        using var png = new PngByteQRCode(data);
        return png.GetGraphic(12);
    }

    /// <summary>
    /// Koduje treść z korekcją błędów Q, a gdy dane są za długie (np. offline JSON z kanałami),
    /// schodzi do M i L. Rzuca wyjątek z czytelnym opisem, gdy treść nie mieści się w żadnym kodzie.
    /// </summary>
    internal static QRCodeData CreateDataFitting(string value)
    {
        string text = value ?? string.Empty;
        Exception? last = null;
        foreach (var ecc in new[] { QRCodeGenerator.ECCLevel.Q, QRCodeGenerator.ECCLevel.M, QRCodeGenerator.ECCLevel.L })
        {
            try
            {
                using var generator = new QRCodeGenerator();
                return generator.CreateQrCode(text, ecc);
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }

        throw new InvalidOperationException(
            $"Treść ({text.Length} znaków) nie mieści się w jednym kodzie QR. Użyj pobierania przez Wi-Fi/LAN albo skróć dane.",
            last);
    }

    /// <summary>Rysuje skanowalny kod QR (z marginesem ciszy) w kwadracie o boku <paramref name="size"/>.</summary>
    internal static void DrawQr(DrawingContext dc, string value, double x, double y, double size)
    {
        using QRCodeData data = CreateDataFitting(value);
        Geometry modules = BuildModuleGeometry(data, x, y, size);
        dc.DrawRectangle(Brushes.White, null, new Rect(x, y, size, size));
        dc.DrawGeometry(Brushes.Black, null, modules);
    }

    /// <summary>Obraz kodu QR dla kontrolki Image: wektorowy, zamrożony, ostry w każdym rozmiarze.</summary>
    internal static ImageSource CreateImageSource(string value)
    {
        using QRCodeData data = CreateDataFitting(value);
        int count = data.ModuleMatrix.Count;
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, count, count))));
        group.Children.Add(new GeometryDrawing(Brushes.Black, null, BuildModuleGeometry(data, 0, 0, count)));
        group.Freeze();

        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    private static Geometry BuildModuleGeometry(QRCodeData data, double x, double y, double size)
    {
        var matrix = data.ModuleMatrix;
        int count = matrix.Count;
        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        if (count == 0)
        {
            geometry.Freeze();
            return geometry;
        }

        double cell = size / count;
        using (StreamGeometryContext ctx = geometry.Open())
        {
            for (int row = 0; row < count; row++)
            {
                var bits = matrix[row];
                int col = 0;
                while (col < bits.Length)
                {
                    if (!bits[col])
                    {
                        col++;
                        continue;
                    }

                    // Jeden prostokąt na poziomy ciąg ciemnych modułów: mniej figur, brak szczelin przy skalowaniu.
                    int start = col;
                    while (col < bits.Length && bits[col]) col++;
                    double left = x + start * cell;
                    double right = x + col * cell;
                    double top = y + row * cell;
                    double bottom = y + (row + 1) * cell;
                    ctx.BeginFigure(new Point(left, top), true, true);
                    ctx.LineTo(new Point(right, top), false, false);
                    ctx.LineTo(new Point(right, bottom), false, false);
                    ctx.LineTo(new Point(left, bottom), false, false);
                }
            }
        }

        geometry.Freeze();
        return geometry;
    }
}
