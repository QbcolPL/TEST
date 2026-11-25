using System.Collections.Generic;
using System.Linq;

namespace FieldTakHub.Builder.Services.MapEditor;

/// <summary>Zakładka panelu bocznego generatora map.</summary>
internal sealed record LeftPanelTab(string Id, string Glyph, string Title, string Tooltip);

/// <summary>
/// Zakładki panelu bocznego w kolejności pracy nad mapą: Mapa → Obiekty → Siatka → Legenda → Eksport.
/// Ctrl+1…Ctrl+5 (także z klawiatury numerycznej) przełącza je w tej kolejności.
/// </summary>
internal static class LeftPanelTabs
{
    public const string MapTab = "MAP";
    public const string ObjectsTab = "OBJECTS";
    public const string GridTab = "GRID";
    public const string LegendTab = "LEGEND";
    public const string ExportTab = "EXPORT";

    /// <summary>Pierwsze uruchomienie: zaczyna się od wyboru terenu i skali.</summary>
    public const string DefaultTab = MapTab;

    public static readonly IReadOnlyList<LeftPanelTab> All = new[]
    {
        new LeftPanelTab(MapTab, "🗺", "Mapa", "Mapa: teren gry, strony konfliktu, arkusz, skala i podkład (Ctrl+1)"),
        new LeftPanelTab(ObjectsTab, "📍", "Obiekty", "Obiekty: nowe znaczniki, lista wszystkiego na mapie i szczegóły zaznaczonego elementu (Ctrl+2)"),
        new LeftPanelTab(GridTab, "▦", "Siatka", "Siatka: MGRS, sektorowa albo WGS84, podziałka sektorowa, kolory i opisy (Ctrl+3)"),
        new LeftPanelTab(LegendTab, "📋", "Legenda", "Legenda, znacznik Północy i podziałka skali na mapie (Ctrl+4)"),
        new LeftPanelTab(ExportTab, "📤", "Eksport", "Eksport: KMZ, GPX, PDF, wydruk z ramką, paczka .ftak, mapa offline (Ctrl+5)")
    };

    public static bool IsKnown(string? id) => All.Any(t => t.Id == id);

    /// <summary>Zapisana zakładka albo domyślna, gdy zapis jest pusty lub pochodzi z innej wersji.</summary>
    public static string Normalize(string? id) => IsKnown(id) ? id! : DefaultTab;

    /// <summary>Zakładka dla Ctrl+cyfra (1–5); null dla każdej innej cyfry.</summary>
    public static string? FromDigit(int digit) => digit >= 1 && digit <= All.Count ? All[digit - 1].Id : null;
}

/// <summary>Jeden format wyjściowy w menu „📤 Eksport…” i w zakładce Eksport.</summary>
internal sealed record ExportOption(string Id, string Group, string Glyph, string Title, string Note, bool ListedInExportTab);

/// <summary>
/// Wszystkie formaty wyjściowe generatora map w jednym miejscu — wcześniej siedem przycisków w dwóch sekcjach panelu.
/// Mapa offline ma w zakładce Eksport własną kartę z ustawieniami i postępem, więc tam nie powtarza się jej pozycji.
/// </summary>
internal static class ExportCatalog
{
    public const string FilesGroup = "Pliki";
    public const string PackageGroup = "Do paczki .ftak (strony zaznaczone w zakładce Mapa)";
    public const string OfflineGroup = "Mapa offline";

    public const string Kmz = "kmz";
    public const string Gpx = "gpx";
    public const string Pdf = "pdf";
    public const string FramedPrint = "framed-print";
    public const string FireSectorsKmz = "fire-sectors-kmz";
    public const string Project = "project";
    public const string FtakSheet = "ftak-sheet";
    public const string FtakOffline = "ftak-offline";
    public const string OfflineFile = "offline-file";

    public static readonly IReadOnlyList<ExportOption> All = new[]
    {
        new ExportOption(Kmz, FilesGroup, "📦", "Mapa .KMZ…", "ATAK i Google Earth: siatka GRG, obiekty i sektory ognia", true),
        new ExportOption(Gpx, FilesGroup, "⌚", "Trasy i punkty .GPX…", "Garmin i aplikacje GPS w telefonie", true),
        new ExportOption(Pdf, FilesGroup, "🖨️", "PDF arkusza…", "Arkusz tak, jak widać go na ekranie", true),
        new ExportOption(FramedPrint, FilesGroup, "🗺️", "Wydruk z ramką kartograficzną…", "PDF jak mapa taktyczna: siatka MGRS, podziałki, PT/PM, QR, seria arkuszy", true),
        new ExportOption(FireSectorsKmz, FilesGroup, "🎯", "Sektory ognia .KMZ…", "Same sektory ognia jako wielokąty dla ATAK", true),
        new ExportOption(Project, FilesGroup, "💾", "Projekt roboczy (.fthproj)…", "Cały projekt — paczka .ftak i mapa — do dalszej edycji", true),
        new ExportOption(FtakSheet, PackageGroup, "⚡", "Arkusz .KMZ / .GPX do paczki .ftak", "Mapa trafia prosto do źródeł paczki", true),
        new ExportOption(FtakOffline, PackageGroup, "⚡", "Mapa offline do paczki .ftak", "Baza .mbtiles / .sqlite według ustawień z karty „Mapa offline”", false),
        new ExportOption(OfflineFile, OfflineGroup, "📥", "Mapa offline jako plik…", "Baza .mbtiles / .sqlite do skopiowania na telefon", false)
    };

    /// <summary>Pozycje w grupach, w kolejności listy (grupy w kolejności pierwszego wystąpienia).</summary>
    public static IReadOnlyList<IGrouping<string, ExportOption>> Grouped(bool exportTabOnly) =>
        All.Where(o => !exportTabOnly || o.ListedInExportTab).GroupBy(o => o.Group).ToList();
}
