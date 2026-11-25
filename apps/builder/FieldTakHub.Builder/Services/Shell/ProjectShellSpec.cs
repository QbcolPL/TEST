using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace FieldTakHub.Builder.Services.Shell;

/// <summary>Pola autozapisu sesji, które wiążą przywrócony stan z plikiem projektu.</summary>
internal static class SessionKeys
{
    /// <summary>Plik .fthproj, nad którym pracowano (pusty — projekt jeszcze bez pliku).</summary>
    public const string ProjectPath = "sessionProjectPath";

    /// <summary>Odcisk stanu zapisanego w tym pliku (<see cref="ProjectSignature"/>).</summary>
    public const string ProjectBaseline = "sessionProjectBaseline";
}

/// <summary>
/// Odcisk treści projektu (SHA-256 z JSON-a stanu): ten sam stan daje ten sam odcisk, więc znacznik
/// niezapisanych zmian znika także wtedy, gdy użytkownik cofnie zmiany do stanu z pliku.
/// Pomija pola, które zmieniają się bez udziału użytkownika: czas zapisu, ostatnio użyte foldery,
/// otwartą zakładkę panelu mapy, tekst wyszukiwania i poziom kafelków (wynika ze skali i rozmiaru okna).
/// </summary>
internal static class ProjectSignature
{
    private static readonly string[] VolatileRootKeys =
    {
        "savedAt", "savedAtUtc", "lastSourceDir", "lastExportDir", SessionKeys.ProjectPath, SessionKeys.ProjectBaseline
    };

    private static readonly string[] VolatileMapKeys = { "leftPanelTab", "searchQuery", "zoom" };

    public static string Compute(JsonObject? workspace)
    {
        if (workspace is null) return "";
        if (workspace.DeepClone() is not JsonObject copy) return "";

        foreach (string key in VolatileRootKeys) copy.Remove(key);

        // Ważność 0 (bez limitu) znaczy to samo co brak pola — projekty sprzed etapu 1 modułu paczek go nie mają,
        // więc nie mogą przez to wyglądać na zmienione.
        if (copy.ContainsKey(PackageValiditySpec.JsonKey) && PackageValiditySpec.FromJson(copy[PackageValiditySpec.JsonKey]) == 0)
            copy.Remove(PackageValiditySpec.JsonKey);

        // Strony zaznaczone na mapie to zbiór — kolejność nie jest zmianą.
        string selectedFactions = "";
        if (copy["mapState"] is JsonObject map)
        {
            foreach (string key in VolatileMapKeys) map.Remove(key);
            if (map["selectedFactions"] is JsonArray factions)
            {
                selectedFactions = string.Join("\n", factions
                    .Select(f => f?.ToJsonString() ?? "null")
                    .OrderBy(s => s, StringComparer.Ordinal));
                map.Remove("selectedFactions");
            }
        }

        string text = copy.ToJsonString() + "\n#selectedFactions\n" + selectedFactions;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}

/// <summary>Nazwa projektu w pasku bocznym i na pasku tytułu okna, ze znacznikiem niezapisanych zmian.</summary>
internal static class ProjectTitleText
{
    /// <summary>Znacznik niezapisanych zmian w pasku bocznym (na pasku tytułu — gwiazdka, jak w Notatniku).</summary>
    public const string UnsavedMark = "●";

    /// <summary>Nazwa pliku z pełnej ścieżki (oba rodzaje ukośników; pusty tekst dla braku pliku).</summary>
    public static string FileName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        string trimmed = path.Trim();
        int cut = Math.Max(trimmed.LastIndexOf('\\'), trimmed.LastIndexOf('/'));
        return cut >= 0 ? trimmed[(cut + 1)..] : trimmed;
    }

    public static string SidebarLabel(string? projectPath, bool unsaved, bool english)
    {
        string name = FileName(projectPath);
        string text = name.Length > 0
            ? (english ? "Project: " : "Projekt: ") + name
            : (english ? "Project: new (no file yet)" : "Projekt: nowy (jeszcze bez pliku)");
        return unsaved ? UnsavedMark + " " + text : text;
    }

    public static string SidebarTooltip(string? projectPath, bool unsaved, bool english)
    {
        string full = string.IsNullOrWhiteSpace(projectPath) ? "" : projectPath.Trim();
        if (full.Length == 0)
        {
            return english
                ? "The project has no file yet. Ctrl+S saves it as .fthproj."
                : "Projekt nie ma jeszcze pliku. Ctrl+S zapisze go jako .fthproj.";
        }
        if (unsaved)
        {
            return english
                ? "Unsaved changes. Ctrl+S saves them to:\n" + full
                : "Niezapisane zmiany. Ctrl+S zapisze je do pliku:\n" + full;
        }
        return english ? "Saved in:\n" + full : "Zapisany w pliku:\n" + full;
    }

    public static string WindowTitle(string? projectPath, bool unsaved, string? appTitle, bool english)
    {
        string name = FileName(projectPath);
        string document = name.Length > 0 ? name : (english ? "New project" : "Nowy projekt");
        string app = string.IsNullOrWhiteSpace(appTitle) ? "Field TAK Hub Builder" : appTitle.Trim();
        return (unsaved ? "*" : "") + document + " — " + app;
    }
}

/// <summary>Skrót klawiszowy okna głównego (ściągawka F1).</summary>
internal sealed record ShortcutEntry(string Keys, string Pl, string En)
{
    public string Text(bool english) => english ? En : Pl;
}

/// <summary>
/// Skróty całego okna. Działają w każdym module, jeśli nie przejmie ich pole tekstowe ani generator map;
/// generator map ma własne Ctrl+1…5, więc moduły przełącza Alt+cyfra.
/// </summary>
internal static class ShellShortcuts
{
    public const int ModuleCount = 4;
    public const string SaveKeys = "Ctrl+S";
    public const string SaveAsKeys = "Ctrl+Shift+S";
    public const string OpenKeys = "Ctrl+O";
    public const string NewKeys = "Ctrl+N";
    public const string HelpKeys = "F1";

    /// <summary>Alt+1…4 — moduł w kolejności z paska bocznego (0 = paczki … 3 = programator USB); inne cyfry nic nie robią.</summary>
    public static int? ModuleIndexForDigit(int digit) => digit >= 1 && digit <= ModuleCount ? digit - 1 : null;

    public static string ModuleKeys(int moduleIndex) => "Alt+" + (moduleIndex + 1).ToString(CultureInfo.InvariantCulture);

    public static readonly IReadOnlyList<ShortcutEntry> WindowShortcuts = new[]
    {
        new ShortcutEntry(SaveKeys, "Zapisz projekt (do bieżącego pliku .fthproj)", "Save the project (to its current .fthproj file)"),
        new ShortcutEntry(SaveAsKeys, "Zapisz projekt jako… (nowy plik)", "Save the project as… (new file)"),
        new ShortcutEntry(OpenKeys, "Otwórz projekt", "Open a project"),
        new ShortcutEntry(NewKeys, "Nowy projekt", "New project"),
        new ShortcutEntry(ModuleKeys(0), "Generator paczek (.ftak)", "Package builder (.ftak)"),
        new ShortcutEntry(ModuleKeys(1), "Generator map 1:1", "1:1 map generator"),
        new ShortcutEntry(ModuleKeys(2), "Generator OPORD", "OPORD builder"),
        new ShortcutEntry(ModuleKeys(3), "Programator płytek USB", "USB board programmer"),
        new ShortcutEntry(HelpKeys, "Ta ściągawka", "This cheat sheet")
    };
}

/// <summary>
/// „Nowy projekt” nie kasuje plików z source\maps: przenosi je do archiwum obok folderu source
/// (poza nim, więc nie trafiają do kolejnych paczek), np. …\archiwum\maps_20261125_071500.
/// </summary>
internal static class MapsArchive
{
    public const string ArchiveFolderName = "archiwum";

    /// <returns>Folder archiwum albo null, gdy source nie ma folderu nadrzędnego (np. jest katalogiem głównym dysku).</returns>
    public static string? FolderFor(string? sourceDir, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(sourceDir)) return null;
        string trimmed = Path.TrimEndingDirectorySeparator(sourceDir.Trim());
        string? parent = Path.GetDirectoryName(trimmed);
        if (string.IsNullOrEmpty(parent)) return null;
        return Path.Combine(parent, ArchiveFolderName, "maps_" + now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
    }
}
