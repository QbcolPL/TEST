using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace FieldTakHub.Builder.Services.MapPrint;

/// <summary>Jedna strona PDF: obraz JPEG wypełniający całą stronę o podanym rozmiarze w milimetrach.</summary>
internal sealed record PdfImagePage(byte[] Jpeg, int PixelWidth, int PixelHeight, double WidthMm, double HeightMm);

/// <summary>
/// Minimalny zapis wielostronicowego PDF 1.4, w którym każda strona to jeden obraz JPEG (DCTDecode).
/// Bez zewnętrznych bibliotek; tablica xref jest liczona z rzeczywistych pozycji obiektów.
/// </summary>
internal static class PdfImageDocumentWriter
{
    private const double PointsPerMm = 72.0 / 25.4;

    public static void WriteFile(string path, IReadOnlyList<PdfImagePage> pages, string title, string creator)
    {
        string tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            Write(fs, pages, title, creator, DateTime.Now);
        File.Move(tmp, path, overwrite: true);
    }

    public static void Write(Stream output, IReadOnlyList<PdfImagePage> pages, string title, string creator, DateTime created)
    {
        if (output == null) throw new ArgumentNullException(nameof(output));
        if (pages == null || pages.Count == 0) throw new ArgumentException("PDF musi mieć co najmniej jedną stronę.", nameof(pages));

        // Numeracja: 1 = katalog, 2 = drzewo stron, 3 = Info, potem po 3 obiekty na stronę (strona, obraz, treść).
        int objectCount = 3 + pages.Count * 3;
        var offsets = new long[objectCount + 1];
        long start = output.Position;

        void Ascii(string s)
        {
            byte[] b = Encoding.ASCII.GetBytes(s);
            output.Write(b, 0, b.Length);
        }

        void BeginObject(int number)
        {
            offsets[number] = output.Position - start;
            Ascii(number.ToString(CultureInfo.InvariantCulture) + " 0 obj\n");
        }

        Ascii("%PDF-1.4\n");
        output.Write(new byte[] { (byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n' }, 0, 6);

        BeginObject(1);
        Ascii("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var kids = new StringBuilder();
        for (int i = 0; i < pages.Count; i++)
            kids.Append(PageObject(i)).Append(" 0 R ");
        BeginObject(2);
        Ascii($"<< /Type /Pages /Kids [{kids.ToString().TrimEnd()}] /Count {pages.Count} >>\nendobj\n");

        BeginObject(3);
        Ascii("<< /Title " + PdfTextString(title) + " /Creator " + PdfTextString(creator) +
              " /Producer " + PdfTextString("Field TAK Hub Builder") +
              " /CreationDate (D:" + created.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + ") >>\nendobj\n");

        for (int i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            if (page.Jpeg == null || page.Jpeg.Length < 4 || page.Jpeg[0] != 0xFF || page.Jpeg[1] != 0xD8)
                throw new ArgumentException($"Strona {i + 1} nie zawiera obrazu JPEG.", nameof(pages));

            string w = Num(page.WidthMm * PointsPerMm), h = Num(page.HeightMm * PointsPerMm);
            int pageObj = PageObject(i), imageObj = pageObj + 1, contentObj = pageObj + 2;

            BeginObject(pageObj);
            Ascii($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {w} {h}] /Resources << /XObject << /Im0 {imageObj} 0 R >> /ProcSet [/PDF /ImageC] >> /Contents {contentObj} 0 R >>\nendobj\n");

            BeginObject(imageObj);
            Ascii($"<< /Type /XObject /Subtype /Image /Width {page.PixelWidth} /Height {page.PixelHeight} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {page.Jpeg.Length} >>\nstream\n");
            output.Write(page.Jpeg, 0, page.Jpeg.Length);
            Ascii("\nendstream\nendobj\n");

            string content = $"q\n{w} 0 0 {h} 0 0 cm\n/Im0 Do\nQ\n";
            BeginObject(contentObj);
            Ascii($"<< /Length {content.Length} >>\nstream\n{content}endstream\nendobj\n");
        }

        long xref = output.Position - start;
        var sb = new StringBuilder();
        sb.Append("xref\n0 ").Append(objectCount + 1).Append('\n');
        sb.Append("0000000000 65535 f \n");
        for (int n = 1; n <= objectCount; n++)
            sb.Append(offsets[n].ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        sb.Append("trailer\n<< /Size ").Append(objectCount + 1).Append(" /Root 1 0 R /Info 3 0 R >>\n");
        sb.Append("startxref\n").Append(xref.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF\n");
        Ascii(sb.ToString());
    }

    private static int PageObject(int pageIndex) => 4 + pageIndex * 3;

    private static string Num(double v) => Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Tekst PDF w UTF-16BE (z BOM) zapisany szesnastkowo — działa z polskimi znakami.</summary>
    internal static string PdfTextString(string text)
    {
        byte[] utf16 = Encoding.BigEndianUnicode.GetBytes(text ?? "");
        var sb = new StringBuilder("<FEFF", 6 + utf16.Length * 2);
        foreach (byte b in utf16) sb.Append(b.ToString("X2", CultureInfo.InvariantCulture));
        return sb.Append('>').ToString();
    }
}
