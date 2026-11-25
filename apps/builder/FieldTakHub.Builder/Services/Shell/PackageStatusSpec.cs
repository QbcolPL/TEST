using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FieldTakHub.Builder.Services.Shell;

/// <summary>Ocena wiersza karty „Status paczki”: jest / brak, ale to nic złego / uwaga.</summary>
internal enum StatusLevel { Ok, Info, Warning }

/// <summary>Wiersz karty: etykieta, krótka wartość i pełny opis do podpowiedzi.</summary>
internal sealed record PackageStatusRow(StatusLevel Level, string Label, string Value, string Detail);

internal sealed record PackageStatus(StatusLevel Level, string Headline, string Hint, IReadOnlyList<PackageStatusRow> Rows);

/// <summary>Plik z folderu source (ścieżka względna, ukośniki dowolne).</summary>
internal sealed record SourceFileEntry(string RelativePath, long Size);

/// <summary>
/// Strona konfliktu i adres jej serwera TAK (pusty — tylko sieć mesh). Pozostałe pola (etap 1) służą do sprawdzenia
/// ustawień strony tymi samymi regułami co budowanie paczki; null — pole nieznane, bez sprawdzania.
/// </summary>
internal sealed record FactionStatusEntry(
    string Name,
    string ServerHost,
    string? ChannelKey = null,
    string? EnrollmentLink = null,
    string? ChannelLink = null,
    string? ChannelName = null);

/// <summary>Zbudowana paczka .ftak z folderu out\.</summary>
internal sealed record BuiltPackageEntry(string FullPath, long Size, DateTime LastWriteLocal);

internal enum ApkRole { Atak, Meshtastic, Plugin }

/// <summary>
/// Karta „Status paczki” w generatorze paczek: co z folderu source trafi do paczek .ftak.
/// Te same reguły co budowanie paczki: lista rozszerzeń z WriteSingleAuditedFactionFtakArchive
/// i rozpoznawanie APK z ScanSourceApksAndBuildCompatibilityManifest (BuilderUiSimplifier.cs).
/// </summary>
internal static class PackageStatusSpec
{
    /// <summary>Rozszerzenia, które budowanie paczki bierze z folderu source; inne pliki są pomijane.</summary>
    public static readonly IReadOnlySet<string> PackagedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".apk", ".zip", ".mbtiles", ".sqlite", ".kmz", ".kml", ".gpx", ".p12", ".pref", ".html"
    };

    /// <summary>
    /// Pliki robocze Buildera i notatki (np. server.txt, OPORD w .json) — też nie trafiają do paczki,
    /// ale nikt ich tam nie oczekuje, więc karta ich nie zgłasza jako pominiętych.
    /// </summary>
    public static readonly IReadOnlySet<string> InternalExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".json", ".txt", ".md", ".log", ".cfg", ".ini", ".xml", ".bak", ".tmp", ".lock"
    };

    private static readonly HashSet<string> MapExtensions = new(StringComparer.OrdinalIgnoreCase) { ".mbtiles", ".sqlite" };
    private static readonly HashSet<string> OverlayExtensions = new(StringComparer.OrdinalIgnoreCase) { ".kmz", ".kml", ".gpx" };
    private static readonly HashSet<string> SettingsExtensions = new(StringComparer.OrdinalIgnoreCase) { ".p12", ".pref" };

    public static ApkRole ClassifyApk(string? fileName)
    {
        string lower = ProjectTitleText.FileName(fileName).ToLowerInvariant();
        if (lower.Contains("atak") && !lower.Contains("plugin") && !lower.Contains("meshtastic")) return ApkRole.Atak;
        if (lower.Contains("meshtastic") && !lower.Contains("plugin") && !lower.Contains("atak")) return ApkRole.Meshtastic;
        return ApkRole.Plugin;
    }

    public static bool IsPackaged(string? relativePath) =>
        !string.IsNullOrWhiteSpace(relativePath) && PackagedExtensions.Contains(Path.GetExtension(relativePath));

    public static PackageStatus Build(
        IEnumerable<SourceFileEntry>? files,
        IReadOnlyList<FactionStatusEntry>? factions,
        bool english,
        CultureInfo? culture = null)
    {
        string L(string pl, string en) => english ? en : pl;

        var atakApks = new List<string>();
        var meshtasticApks = new List<string>();
        int plugins = 0, maps = 0, overlays = 0, settings = 0, otherPackaged = 0, skipped = 0;
        long mapBytes = 0;
        var skippedKinds = new List<string>();

        foreach (var file in files ?? Enumerable.Empty<SourceFileEntry>())
        {
            if (file is null || string.IsNullOrWhiteSpace(file.RelativePath)) continue;
            string name = ProjectTitleText.FileName(file.RelativePath);
            string ext = Path.GetExtension(name).ToLowerInvariant();
            if (!PackagedExtensions.Contains(ext))
            {
                if (InternalExtensions.Contains(ext)) continue;
                skipped++;
                string kind = ext.Length > 0 ? ext : L("bez rozszerzenia", "no extension");
                if (!skippedKinds.Contains(kind)) skippedKinds.Add(kind);
                continue;
            }

            if (ext == ".apk")
            {
                switch (ClassifyApk(name))
                {
                    case ApkRole.Atak: atakApks.Add(name); break;
                    case ApkRole.Meshtastic: meshtasticApks.Add(name); break;
                    default: plugins++; break;
                }
            }
            else if (MapExtensions.Contains(ext)) { maps++; mapBytes += Math.Max(0, file.Size); }
            else if (OverlayExtensions.Contains(ext)) overlays++;
            else if (SettingsExtensions.Contains(ext)) settings++;
            else otherPackaged++;
        }

        string Files(int n) => CountText(n, "plik", "pliki", "plików", "file", "files", english);
        string none = L("brak", "none");
        var rows = new List<PackageStatusRow>();

        var sides = (factions ?? Array.Empty<FactionStatusEntry>())
            .Where(f => f != null && !string.IsNullOrWhiteSpace(f.Name))
            .ToList();
        rows.Add(sides.Count == 0
            ? new PackageStatusRow(StatusLevel.Warning, L("Strony konfliktu", "Factions"), none,
                L("Dodaj co najmniej jedną stronę — każda dostaje własną paczkę.", "Add at least one faction — each one gets its own package."))
            : new PackageStatusRow(StatusLevel.Ok, L("Strony konfliktu", "Factions"),
                sides.Count.ToString(CultureInfo.InvariantCulture) + " · " + JoinNames(sides.Select(s => s.Name.Trim()), 3),
                string.Join(", ", sides.Select(s => s.Name.Trim()))));

        rows.Add(atakApks.Count > 0
            ? new PackageStatusRow(StatusLevel.Ok, "ATAK (APK)", FirstAndMore(atakApks), string.Join("\n", atakApks))
            : new PackageStatusRow(StatusLevel.Info, "ATAK (APK)", none,
                L("Bez pliku APK telefony muszą mieć już zainstalowany ATAK.", "Without the APK the phones must already have ATAK installed.")));

        rows.Add(meshtasticApks.Count > 0
            ? new PackageStatusRow(StatusLevel.Ok, "Meshtastic (APK)", FirstAndMore(meshtasticApks), string.Join("\n", meshtasticApks))
            : new PackageStatusRow(StatusLevel.Info, "Meshtastic (APK)", none,
                L("Bez pliku APK aplikację Meshtastic trzeba zainstalować osobno.", "Without the APK the Meshtastic app has to be installed separately.")));

        rows.Add(new PackageStatusRow(plugins > 0 ? StatusLevel.Ok : StatusLevel.Info, L("Pluginy ATAK", "ATAK plugins"),
            plugins > 0 ? Files(plugins) + " .apk" : none,
            L("Pozostałe pliki .apk z folderu source.", "Other .apk files from the source folder.")));

        rows.Add(new PackageStatusRow(maps > 0 ? StatusLevel.Ok : StatusLevel.Info, L("Mapy offline", "Offline maps"),
            maps > 0 ? Files(maps) + " · " + SizeText(mapBytes, culture) : none,
            L("Bazy map .mbtiles i .sqlite, np. z generatora map (zakładka Eksport → Mapa offline).",
              ".mbtiles and .sqlite map databases, e.g. from the map generator (Export tab → offline map).")));

        rows.Add(new PackageStatusRow(overlays > 0 ? StatusLevel.Ok : StatusLevel.Info, L("Nakładki i trasy", "Overlays and routes"),
            overlays > 0 ? Files(overlays) : none,
            L("Pliki KMZ, KML i GPX, np. z generatora map („Do paczki .ftak”).", "KMZ, KML and GPX files, e.g. from the map generator (“To .ftak package”).")));

        if (settings > 0)
        {
            rows.Add(new PackageStatusRow(StatusLevel.Ok, L("Certyfikaty i ustawienia", "Certificates and settings"), Files(settings),
                L("Pliki .p12 i .pref.", ".p12 and .pref files.")));
        }
        if (otherPackaged > 0)
        {
            rows.Add(new PackageStatusRow(StatusLevel.Ok, L("Inne dołączane", "Other included"), Files(otherPackaged),
                L("Pliki .zip i .html.", ".zip and .html files.")));
        }

        if (sides.Count > 0)
        {
            var withServer = sides.Where(s => !string.IsNullOrWhiteSpace(s.ServerHost)).ToList();
            string detail = string.Join("\n", sides.Select(s =>
                s.Name.Trim() + ": " + (string.IsNullOrWhiteSpace(s.ServerHost) ? L("bez serwera", "no server") : s.ServerHost.Trim())));
            if (withServer.Count == 0)
            {
                rows.Add(new PackageStatusRow(StatusLevel.Info, L("Serwer TAK", "TAK server"), L("brak — tylko mesh", "none — mesh only"),
                    L("Bez adresu serwera telefony łączą się tylko przez sieć Meshtastic.", "Without a server address the phones only use the Meshtastic network.")
                    + "\n" + detail));
            }
            else if (withServer.Count == sides.Count)
            {
                var hosts = withServer.Select(s => s.ServerHost.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                rows.Add(new PackageStatusRow(StatusLevel.Ok, L("Serwer TAK", "TAK server"),
                    hosts.Count == 1 ? hosts[0] : L("ustawiony dla każdej strony", "set for every faction"), detail));
            }
            else
            {
                rows.Add(new PackageStatusRow(StatusLevel.Info, L("Serwer TAK", "TAK server"),
                    english
                        ? $"{withServer.Count} of {sides.Count} factions"
                        : $"{withServer.Count} z {sides.Count} " + PolishForm(sides.Count, "strony", "stron", "stron"),
                    detail));
            }
        }

        // Uwagi do ustawień stron — te same reguły co sprawdzenie przed budowaniem (SideSettingsSpec).
        var problems = SideSettingsSpec.Check(sides.Select(s => new SideSettings(
            s.Name, s.ChannelKey, s.ServerHost, s.EnrollmentLink, s.ChannelLink, s.ChannelName)), english);
        if (problems.Count > 0)
        {
            rows.Add(new PackageStatusRow(StatusLevel.Warning, L("Do poprawienia", "To fix"),
                problems.Count.ToString(CultureInfo.InvariantCulture) + " · "
                + JoinNames(problems.Select(p => p.Side).Distinct(StringComparer.OrdinalIgnoreCase), 3),
                string.Join("\n", problems.Select(p => p.Side + ": " + p.Message))));
        }

        if (skipped > 0)
        {
            rows.Add(new PackageStatusRow(StatusLevel.Warning, L("Pominięte pliki", "Skipped files"),
                skipped.ToString(CultureInfo.InvariantCulture) + " · " + JoinNames(skippedKinds, 3),
                L("Te pliki nie trafią do paczki. Paczka bierze tylko: ", "These files will not go into the package. It only takes: ")
                + string.Join(", ", PackagedExtensions.OrderBy(e => e, StringComparer.Ordinal)) + "."));
        }

        if (sides.Count == 0)
        {
            return new PackageStatus(StatusLevel.Warning, L("Dodaj stronę konfliktu", "Add a faction"),
                L("Bez strony konfliktu nie powstanie żadna paczka.", "No package can be built without a faction."), rows);
        }
        var blocking = problems.FirstOrDefault(p => p.Blocking);
        if (blocking != null)
        {
            return new PackageStatus(StatusLevel.Warning, L("Popraw przed budowaniem", "Fix before building"), blocking.Message, rows);
        }
        return new PackageStatus(StatusLevel.Ok, L("Gotowa do generowania", "Ready to build"),
            skipped > 0
                ? L("Część plików z folderu source zostanie pominięta — szczegóły niżej.", "Some files in the source folder will be skipped — see below.")
                : problems.Count > 0
                    ? L("Są uwagi do ustawień stron — szczegóły niżej.", "Some faction settings need attention — see below.")
                    : L("Każda strona dostanie własną paczkę w folderze out\\<STRONA>\\.", "Each faction gets its own package in out\\<FACTION>\\."),
            rows);
    }

    /// <summary>Liczba z rzeczownikiem: 1 plik, 2–4 pliki, 5 plików, 12 plików, 22 pliki (po angielsku: 1 file, 2 files).</summary>
    public static string CountText(int count, string one, string few, string many, string enOne, string enMany, bool english)
    {
        string n = count.ToString(CultureInfo.InvariantCulture);
        if (english) return n + " " + (count == 1 ? enOne : enMany);
        return n + " " + PolishForm(count, one, few, many);
    }

    /// <summary>Polska forma rzeczownika po liczebniku.</summary>
    public static string PolishForm(int count, string one, string few, string many)
    {
        int abs = Math.Abs(count);
        if (abs == 1) return one;
        int lastDigit = abs % 10, lastTwo = abs % 100;
        return lastDigit >= 2 && lastDigit <= 4 && (lastTwo < 12 || lastTwo > 14) ? few : many;
    }

    /// <summary>Rozmiar pliku jak w karcie „Podgląd zawartości” (1024 B = 1 KB).</summary>
    public static string SizeText(long bytes, CultureInfo? culture = null)
    {
        if (bytes <= 0) return "0 B";
        var format = culture ?? CultureInfo.CurrentCulture;
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0
            ? bytes.ToString(CultureInfo.InvariantCulture) + " B"
            : value.ToString("0.#", format) + " " + units[unit];
    }

    /// <summary>Pierwsze nazwy i wielokropek, gdy jest ich więcej.</summary>
    public static string JoinNames(IEnumerable<string> names, int max)
    {
        var list = names.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        if (list.Count <= max) return string.Join(", ", list);
        return string.Join(", ", list.Take(max)) + ", …";
    }

    private static string FirstAndMore(IReadOnlyList<string> names) =>
        names.Count <= 1 ? (names.Count == 1 ? names[0] : "") : names[0] + " +" + (names.Count - 1).ToString(CultureInfo.InvariantCulture);
}

/// <summary>Teksty karty „Ostatnie paczki .ftak”.</summary>
internal sealed record LatestPackagesText(string Title, string Folder, string Detail);

/// <summary>Najnowsza paczka .ftak z folderu out\ (nazwa, folder, rozmiar, kiedy) i liczba pozostałych.</summary>
internal static class LatestPackagesSpec
{
    public static LatestPackagesText Describe(IEnumerable<BuiltPackageEntry>? packages, DateTime now, bool english, CultureInfo? culture = null)
    {
        var list = (packages ?? Enumerable.Empty<BuiltPackageEntry>())
            .Where(p => p != null && !string.IsNullOrWhiteSpace(p.FullPath))
            .OrderByDescending(p => p.LastWriteLocal)
            .ToList();
        if (list.Count == 0)
        {
            return new LatestPackagesText(
                english ? "— none built yet —" : "— jeszcze nie zbudowano —",
                english ? "Packages go to out\\<FACTION>\\ next to the source folder." : "Paczki trafiają do folderu out\\<STRONA>\\ obok folderu source.",
                "");
        }

        var newest = list[0];
        string detail = PackageStatusSpec.SizeText(newest.Size, culture) + " · " + WhenText(newest.LastWriteLocal, now, english);
        int others = list.Count - 1;
        if (others > 0)
        {
            detail += english
                ? $" · +{others} more"
                : " · " + PackageStatusSpec.CountText(others, "inna paczka", "inne paczki", "innych paczek", "", "", false);
        }
        return new LatestPackagesText(ProjectTitleText.FileName(newest.FullPath), DirectoryOf(newest.FullPath), detail);
    }

    /// <summary>„dziś 14:32”, „wczoraj 09:10”, starsze — pełna data.</summary>
    public static string WhenText(DateTime written, DateTime now, bool english)
    {
        string time = written.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (written.Date == now.Date) return (english ? "today " : "dziś ") + time;
        if (written.Date == now.Date.AddDays(-1)) return (english ? "yesterday " : "wczoraj ") + time;
        return written.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
    }

    private static string DirectoryOf(string path)
    {
        int cut = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
        return cut > 0 ? path[..cut] : "";
    }
}
