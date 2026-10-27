#pragma warning disable CS0414, CS0649, CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FieldTakHub.Builder.Services
{
    // =========================================================================
    // 1. PEŁNY SŁOWNIK LOKALIZACJI INTERFEJSU (PL <-> ENG)
    // =========================================================================
    internal static class BuilderI18n
    {
        public static string CurrentLang { get; private set; } = LoadSavedLanguage();
        public static event Action<string> LanguageChanged;

        private static readonly DependencyProperty OriginalPlTextProperty =
            DependencyProperty.RegisterAttached("OriginalPlText", typeof(string), typeof(BuilderI18n), new PropertyMetadata(null));

        private static readonly DependencyProperty LastAppliedI18nTextProperty =
            DependencyProperty.RegisterAttached("LastAppliedI18nText", typeof(string), typeof(BuilderI18n), new PropertyMetadata(null));

        private static readonly DependencyProperty OriginalPlToolTipProperty =
            DependencyProperty.RegisterAttached("OriginalPlToolTip", typeof(string), typeof(BuilderI18n), new PropertyMetadata(null));

        private static readonly Dictionary<string, string> PlToEn = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Pasek boczny i nawigacja
            ["MODUŁY OPERACYJNE"] = "OPERATIONAL MODULES",
            ["ZARZĄDZANIE PROJEKTEM"] = "PROJECT WORKSPACE",
            ["Generator Paczek (.ftak)"] = "Package Builder (.ftak)",
            ["Generator Map 1:1"] = "1:1 Map Generator",
            ["Generator OPORD"] = "OPORD Builder",
            ["Programator Płytek USB"] = "USB Board Programmer",
            ["Nowy projekt (Stan zerowy)"] = "New Project (Zero-State)",
            ["Otwórz projekt (.fthproj)"] = "Open Project (.fthproj)",
            ["Zapisz projekt (.fthproj)"] = "Save Project (.fthproj)",
            ["Folder Source (APK / Mapy)"] = "Source Folder (APK / Maps)",
            ["☕ Wesprzyj twórcę"] = "☕ Support Creator",
            ["Zamknij"] = "Close",
            ["Anuluj"] = "Cancel",
            ["Zapisz"] = "Save",
            ["Wczytaj"] = "Load",
            ["Odśwież"] = "Refresh",
            ["Kopiuj"] = "Copy",
            ["Usuń"] = "Delete",
            ["Dodaj"] = "Add",

            // Generator Paczek (.ftak)
            ["➕ Dodaj stronę"] = "➕ Add Faction",
            ["🗑 Usuń stronę"] = "🗑 Remove Faction",
            ["🗑️ Usuń stronę"] = "🗑️ Remove Faction",
            ["🖨 Plakat A4 Safezone (HTML)"] = "🖨 A4 Safezone Poster (HTML)",
            ["🖨️ Plakat A4 Safezone (HTML)"] = "🖨️ A4 Safezone Poster (HTML)",
            ["🔑 Generuj nowy PSK (AES-256)"] = "🔑 Generate New PSK (AES-256)",
            ["🔑 Generuj PSK"] = "🔑 Generate PSK",
            ["➕ Dodaj kanał"] = "➕ Add Channel",
            ["➕ Dodaj kanał głosowy"] = "➕ Add Voice Channel",
            ["⚡ Buduj .FTAK dla WSZYSTKICH stron"] = "⚡ Build .FTAK for ALL Factions",
            ["📦 Buduj .FTAK dla aktywnej strony"] = "📦 Build .FTAK for Active Faction",
            ["📱 Pokaż Kod QR aktywnej strony (Wi-Fi / Offline)"] = "📱 Show Active Faction QR (Wi-Fi / Offline)",
            ["📂 Otwórz folder z plikiem .FTAK"] = "📂 Open .FTAK Output Folder",
            ["Nazwa wydarzenia / operacji"] = "Event / Operation Name",
            ["Nazwa wydarzenia / operacji:"] = "Event / Operation Name:",
            ["Nazwa strony konfliktu"] = "Faction / Side Name",
            ["Kolor taktyczny (HEX)"] = "Tactical Color (HEX)",
            ["Nazwa kanału Meshtastic"] = "Meshtastic Channel Name",
            ["Klucz szyfrowania PSK (Base64)"] = "Encryption Key PSK (Base64)",
            ["Region LoRa"] = "LoRa Region",
            ["Preset modemu LoRa"] = "LoRa Modem Preset",
            ["Limit skoków (Hop Limit)"] = "Hop Limit",
            ["Adres serwera OpenTAK (OTS Host)"] = "OpenTAKServer Address (OTS Host)",
            ["Telefon alarmowy ORGA / MEDEVAC"] = "Emergency Phone (ORGA / MEDEVAC)",
            ["Kanały Głosowe (PMR / VHF / UHF / DMR) — Ściąga Operacyjna"] = "Voice Radio Channels (PMR / VHF / UHF / DMR) — Field Cheat Sheet",
            ["Rola / Przeznaczenie"] = "Role / Purpose",
            ["Pasmo"] = "Band",
            ["Częstotliwość / Kanał"] = "Frequency / Channel",
            ["Ton CTCSS / DCS / Kod DMR"] = "CTCSS / DCS Tone / DMR Code",

            // Generator Map 1:1
            ["Szukaj miejscowości lub współrzędnych"] = "Search town or coordinates",
            ["🔍 Szukaj"] = "🔍 Search",
            ["🎯 Wyśrodkuj na AO"] = "🎯 Center on AO",
            ["Skala mapy"] = "Map Scale",
            ["Orientacja arkusza"] = "Sheet Orientation",
            ["Format arkusza"] = "Sheet Format",
            ["Warstwa podkładowa"] = "Base Map Layer",
            ["Pokaż cieniowanie LiDAR (ISOK)"] = "Show LiDAR Hillshade (ISOK)",
            ["Przezroczystość LiDAR"] = "LiDAR Opacity",
            ["System współrzędnych siatki"] = "Grid Coordinate System",
            ["Eksportuj Mapę A4 (HTML / Druk PDF)"] = "Export A4 Map (HTML / Print PDF)",
            ["Eksportuj KMZ (ATAK Overlay)"] = "Export KMZ (ATAK Overlay)",
            ["Eksportuj GPX (Garmin / Suunto)"] = "Export GPX (Garmin / Suunto)",
            ["Buduj Mapę Offline (.mbtiles / .sqlitedb)"] = "Build Offline Map (.mbtiles / .sqlitedb)"
        };

        // Kompleksowy słownik fraz (od najdłuższych do najkrótszych) tłumaczący całe widoki Generatora Paczek, Map 1:1, USB i OPORD
        private static readonly Dictionary<string, string> PhraseReplacementsPlToEn = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Kanały Głosowe (PMR / VHF / UHF / DMR) — Ściąga Operacyjna"] = "Voice Radio Channels (PMR / VHF / UHF / DMR) — Field Cheat Sheet",
            ["Pokaż Kod QR aktywnej strony (Wi-Fi / Offline)"] = "Show Active Faction QR (Wi-Fi / Offline)",
            ["Buduj .FTAK dla WSZYSTKICH stron"] = "Build .FTAK for ALL Factions",
            ["Buduj .FTAK dla aktywnej strony"] = "Build .FTAK for Active Faction",
            ["Otwórz folder z plikiem .FTAK"] = "Open .FTAK Output Folder",
            ["Generuj nowy PSK (AES-256)"] = "Generate New PSK (AES-256)",
            ["Plakat A4 Safezone (HTML)"] = "A4 Safezone Poster (HTML)",
            ["Konfigurator Paczek (.ftak)"] = "Faction & Package Builder (.ftak)",
            ["Generator Map Taktycznych 1:1"] = "1:1 Tactical Map Studio",
            ["Generator Map 1:1"] = "1:1 Map Generator",
            ["Generator Rozkazu (OPORD)"] = "NATO / MilSim OPORD Builder",
            ["Programator Płytek USB"] = "USB Board Programmer",
            ["Masowy Programator USB"] = "Bulk USB Programmer",
            ["Nowy projekt (Stan zerowy)"] = "New Project (Zero-State)",
            ["Projekt: Nowy (Stan zerowy)"] = "Project: New (Zero-State)",
            ["Projekt: "] = "Project: ",
            ["Otwórz projekt"] = "Open Project",
            ["Zapisz projekt roboczy"] = "Save Workspace Project",
            ["Zapisz projekt"] = "Save Project",
            ["Wczytaj projekt"] = "Load Project",
            ["Folder Source (APK / Mapy)"] = "Source Folder (APK / Maps)",
            ["Wesprzyj twórcę"] = "Support Creator",

            // Etykiety i sekcje Generatora Paczek (.ftak)
            ["Nazwa wydarzenia / operacji"] = "Event / Operation Name",
            ["Nazwa wydarzenia"] = "Event Name",
            ["Nazwa operacji"] = "Operation Name",
            ["Strony konfliktu i konfiguracja frakcji"] = "Conflict Sides & Faction Configuration",
            ["Strony konfliktu"] = "Conflict Sides",
            ["Nazwa strony konfliktu"] = "Faction / Side Name",
            ["Nazwa strony"] = "Faction Name",
            ["Aktywna strona"] = "Active Faction",
            ["Dodaj stronę"] = "Add Faction",
            ["Usuń stronę"] = "Remove Faction",
            ["Kolor taktyczny (HEX)"] = "Tactical Color (HEX)",
            ["Kolor taktyczny"] = "Tactical Color",
            ["Kolor strony"] = "Faction Color",
            ["Nazwa kanału Meshtastic"] = "Meshtastic Channel Name",
            ["Nazwa kanału"] = "Channel Name",
            ["Klucz szyfrowania PSK (Base64)"] = "Encryption Key PSK (Base64)",
            ["Klucz szyfrowania PSK"] = "PSK Encryption Key",
            ["Klucz szyfrowania"] = "Encryption Key",
            ["URL kanału Meshtastic"] = "Meshtastic Channel URL",
            ["Region LoRa"] = "LoRa Region",
            ["Preset modemu LoRa"] = "LoRa Modem Preset",
            ["Preset modemu"] = "Modem Preset",
            ["Limit skoków (Hop Limit)"] = "Hop Limit",
            ["Limit skoków"] = "Hop Limit",
            ["Adres serwera OpenTAK (OTS Host)"] = "OpenTAKServer Address (OTS Host)",
            ["Adres serwera OpenTAK"] = "OpenTAKServer Address",
            ["Serwer OpenTAK"] = "OpenTAKServer",
            ["Adres serwera"] = "Server Address",
            ["URI subskrypcji / Enrollment"] = "Enrollment / Subscription URI",
            ["Telefon alarmowy ORGA / MEDEVAC"] = "Emergency Phone (ORGA / MEDEVAC)",
            ["Telefon alarmowy"] = "Emergency Phone",
            ["Profil raportowania PLI"] = "PLI Reporting Profile",
            ["Strategia PLI"] = "PLI Strategy",
            ["Profil sieci Mesh"] = "Mesh Network Profile",
            ["Tryb pracy Mesh"] = "Mesh Mode",
            ["Rola urządzenia"] = "Device Role",
            ["Model sprzętowy"] = "Hardware Model",
            ["Dodaj kanał"] = "Add Channel",
            ["Usuń kanał"] = "Remove Channel",
            ["Kanały Głosowe"] = "Voice Channels",
            ["Ściąga Operacyjna"] = "Field Cheat Sheet",
            ["Rola / Kanał"] = "Role / Channel",
            ["Rola / Przeznaczenie"] = "Role / Purpose",
            ["Częstotliwość / Kanał"] = "Frequency / Channel",
            ["Częstotliwość"] = "Frequency",
            ["Ton CTCSS / Kod DMR"] = "CTCSS Tone / DMR Code",
            ["Ton / Kod"] = "Tone / Code",
            ["Brak tonu"] = "No Tone",
            ["Dowodzenie HQ"] = "HQ Command",
            ["Ratunkowy ORGA"] = "ORGA Emergency",
            ["Taktyczny"] = "Tactical",
            ["Pliki źródłowe"] = "Source Files",
            ["Katalog źródłowy"] = "Source Directory",
            ["Katalog wyjściowy"] = "Output Directory",
            ["Otwórz folder"] = "Open Folder",

            // Etykiety i sekcje Generatora Map Taktycznych 1:1
            ["Generator Map Taktycznych"] = "Tactical Map Generator",
            ["Szukaj miejscowości lub współrzędnych"] = "Search town or coordinates",
            ["Wyszukaj miejscowość"] = "Search town",
            ["Szukaj miejscowości"] = "Search town",
            ["Szukaj"] = "Search",
            ["Wyśrodkuj na AO"] = "Center on AO",
            ["Wyśrodkuj"] = "Center",
            ["Skala mapy"] = "Map Scale",
            ["Skala arkusza"] = "Sheet Scale",
            ["Skala"] = "Scale",
            ["Orientacja arkusza"] = "Sheet Orientation",
            ["Orientacja"] = "Orientation",
            ["Format arkusza"] = "Sheet Format",
            ["Format papieru"] = "Paper Format",
            ["Warstwa podkładowa"] = "Base Map Layer",
            ["Podkład mapowy"] = "Base Map Layer",
            ["Pokaż cieniowanie LiDAR (ISOK)"] = "Show LiDAR Hillshade (ISOK)",
            ["Pokaż cieniowanie LiDAR"] = "Show LiDAR Hillshade",
            ["Cieniowanie LiDAR"] = "LiDAR Hillshade",
            ["Nakładka LiDAR"] = "LiDAR Overlay",
            ["Przezroczystość LiDAR"] = "LiDAR Opacity",
            ["Przezroczystość głównej siatki"] = "Main Grid Opacity",
            ["Przezroczystość podsiatki"] = "Sub-grid Opacity",
            ["Przezroczystość tła etykiet"] = "Label Box Opacity",
            ["Przezroczystość maski poza AO"] = "Outside AO Mask Opacity",
            ["Przyciemnienie mapy"] = "Map Dimmer Opacity",
            ["Przezroczystość"] = "Opacity",
            ["System współrzędnych siatki"] = "Grid Coordinate System",
            ["Układ współrzędnych"] = "Coordinate System",
            ["Podział kwadratów 100 m"] = "100 m Square Subdivision",
            ["Podział kwadratów"] = "Square Subdivision",
            ["Tryb etykiet siatki"] = "Grid Labels Mode",
            ["Styl tła etykiet"] = "Label Background Style",
            ["Grubość linii siatki"] = "Grid Line Thickness",
            ["Grubość siatki"] = "Grid Thickness",
            ["Skala czcionki"] = "Font Scale",
            ["Kolor głównej siatki"] = "Main Grid Color",
            ["Kolor podsiatki"] = "Sub-grid Color",
            ["Kolor tekstu etykiet"] = "Label Text Color",
            ["Kolor tła etykiet"] = "Label Background Color",
            ["Obrys tekstu (Halo)"] = "Text Halo / Outline",
            ["Maska poza AO"] = "Outside AO Mask",
            ["Pokaż wszystkie etykiety obiektów"] = "Show all object labels",
            ["Pokaż etykiety"] = "Show labels",
            ["Pokaż uchwyty obrotu"] = "Show rotation handles",
            ["Pokaż strzałkę północy"] = "Show North Arrow",
            ["Strzałka północy"] = "North Arrow",
            ["Pokaż podziałkę skali"] = "Show Scale Bar",
            ["Podziałka skali"] = "Scale Bar",
            ["Pokaż legendę"] = "Show Legend",
            ["Legenda taktyczna"] = "Tactical Legend",
            ["Pokaż znaki NATO w legendzie"] = "Show NATO symbols in legend",
            ["Granica Terenu Gry (AO)"] = "Game Area Boundary (AO)",
            ["Granica AO"] = "AO Boundary",
            ["Rysuj granicę AO"] = "Draw AO Boundary",
            ["Zakończ rysowanie"] = "Finish Drawing",
            ["Obiekty na mapie"] = "Map Objects",
            ["Obiekty taktyczne"] = "Tactical Objects",
            ["Dodaj punkt"] = "Add Point",
            ["Dodaj obiekt"] = "Add Object",
            ["Usuń obiekt"] = "Delete Object",
            ["Wyczyść obiekty"] = "Clear Objects",
            ["Wyczyść wszystko"] = "Clear All",
            ["Nazwa obiektu"] = "Object Name",
            ["Domyślna nazwa"] = "Default Name",
            ["Rozmiar punktu"] = "Dot Size",
            ["Skala znaku NATO"] = "NATO Symbol Scale",
            ["Obrót"] = "Rotation",
            ["Eksportuj Mapę A4 (HTML / Druk PDF)"] = "Export A4 Map (HTML / Print PDF)",
            ["Eksportuj Mapę"] = "Export Map",
            ["Drukuj Mapę"] = "Print Map",
            ["Eksportuj KMZ"] = "Export KMZ",
            ["Eksportuj GPX"] = "Export GPX",
            ["Buduj Mapę Offline"] = "Build Offline Map",
            ["Generuj Mapę Offline"] = "Generate Offline Map",
            ["Mapa Offline"] = "Offline Map",
            ["Minimalny zoom"] = "Min Zoom",
            ["Maksymalny zoom"] = "Max Zoom",
            ["Wypal LiDAR w mapie offline"] = "Bake LiDAR into offline map",
            ["Dołącz wektory (KMZ/GPX)"] = "Include vectors (KMZ/GPX)",

            // Programator USB
            ["Masowy Programator Płytek USB"] = "Bulk USB Board Programmer",
            ["Skanuj porty COM"] = "Scan COM Ports",
            ["Wykryte urządzenia"] = "Detected Devices",
            ["Programuj wybraną płytkę"] = "Flash Selected Board",
            ["Programuj wszystkie"] = "Flash All Boards"
        };

        private static readonly List<KeyValuePair<string, string>> SortedPhrasesPlToEn = PhraseReplacementsPlToEn
            .OrderByDescending(kv => kv.Key.Length)
            .ToList();

        public static string T(string plText, string enText) =>
            string.Equals(CurrentLang, "EN", StringComparison.OrdinalIgnoreCase) ? enText : plText;

        public static void ToggleLanguage(Window win)
        {
            SetLanguage(string.Equals(CurrentLang, "EN", StringComparison.OrdinalIgnoreCase) ? "PL" : "EN", win);
        }

        public static void SetLanguage(string langCode, Window win)
        {
            CurrentLang = string.Equals(langCode, "EN", StringComparison.OrdinalIgnoreCase) ? "EN" : "PL";
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FieldTakHub");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "ui_lang.txt"), CurrentLang, Encoding.UTF8);
            }
            catch { }

            if (win != null)
            {
                ApplyLanguageToVisualTree(win);
            }
            LanguageChanged?.Invoke(CurrentLang);
        }

        public static string TranslateTextSmart(string inputPlText, bool toEn)
        {
            if (string.IsNullOrWhiteSpace(inputPlText)) return inputPlText;
            if (!toEn) return inputPlText;

            string trimmed = inputPlText.Trim();
            if (PlToEn.TryGetValue(trimmed, out string exact))
            {
                return exact;
            }

            string result = inputPlText;
            foreach (var kv in SortedPhrasesPlToEn)
            {
                if (result.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result = Regex.Replace(result, Regex.Escape(kv.Key), kv.Value, RegexOptions.IgnoreCase);
                }
            }
            return result;
        }

        public static void ApplyLanguageToVisualTree(DependencyObject root)
        {
            if (root == null) return;
            bool toEn = string.Equals(CurrentLang, "EN", StringComparison.OrdinalIgnoreCase);

            var queue = new Queue<DependencyObject>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var curr = queue.Dequeue();

                if (curr is FrameworkElement fe && fe.ToolTip is string tipStr && !string.IsNullOrWhiteSpace(tipStr))
                {
                    string origTip = fe.GetValue(OriginalPlToolTipProperty) as string;
                    if (origTip == null)
                    {
                        origTip = tipStr;
                        fe.SetValue(OriginalPlToolTipProperty, origTip);
                    }
                    string targetTip = toEn ? TranslateTextSmart(origTip, true) : origTip;
                    if (!string.Equals(tipStr, targetTip, StringComparison.Ordinal))
                    {
                        fe.ToolTip = targetTip;
                    }
                }

                if (curr is TextBlock tb)
                {
                    if (tb.Inlines != null && tb.Inlines.Count > 1)
                    {
                        foreach (var inline in tb.Inlines)
                        {
                            if (inline is System.Windows.Documents.Run run && !string.IsNullOrWhiteSpace(run.Text))
                            {
                                string origRun = run.GetValue(OriginalPlTextProperty) as string;
                                if (origRun == null)
                                {
                                    origRun = run.Text;
                                    run.SetValue(OriginalPlTextProperty, origRun);
                                }
                                string targetRun = toEn ? TranslateTextSmart(origRun, true) : origRun;
                                if (!string.Equals(run.Text, targetRun, StringComparison.Ordinal))
                                {
                                    run.Text = targetRun;
                                }
                            }
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(tb.Text) && !string.Equals(tb.Tag?.ToString(), "DYNAMIC_NO_I18N", StringComparison.Ordinal))
                    {
                        string origTb = tb.GetValue(OriginalPlTextProperty) as string;
                        string lastApplied = tb.GetValue(LastAppliedI18nTextProperty) as string;
                        if (origTb == null || (lastApplied != null && !string.Equals(tb.Text, lastApplied, StringComparison.Ordinal)))
                        {
                            origTb = tb.Text;
                            tb.SetValue(OriginalPlTextProperty, origTb);
                        }
                        string targetTb = toEn ? TranslateTextSmart(origTb, true) : origTb;
                        tb.SetValue(LastAppliedI18nTextProperty, targetTb);
                        if (!string.Equals(tb.Text, targetTb, StringComparison.Ordinal))
                        {
                            tb.Text = targetTb;
                        }
                    }
                }
                else if (curr is HeaderedContentControl hcc && hcc.Header is string hdrStr && !string.IsNullOrWhiteSpace(hdrStr))
                {
                    string origHdr = hcc.GetValue(OriginalPlTextProperty) as string;
                    if (origHdr == null)
                    {
                        origHdr = hdrStr;
                        hcc.SetValue(OriginalPlTextProperty, origHdr);
                    }
                    string targetHdr = toEn ? TranslateTextSmart(origHdr, true) : origHdr;
                    if (!string.Equals(hdrStr, targetHdr, StringComparison.Ordinal))
                    {
                        hcc.Header = targetHdr;
                    }
                }
                else if (curr is ContentControl cc && !(curr is ComboBoxItem) && !(curr is ComboBox) && cc.Content is string s && !string.IsNullOrWhiteSpace(s))
                {
                    string origContent = cc.GetValue(OriginalPlTextProperty) as string;
                    if (origContent == null)
                    {
                        origContent = s;
                        cc.SetValue(OriginalPlTextProperty, origContent);
                    }
                    string targetContent = toEn ? TranslateTextSmart(origContent, true) : origContent;
                    if (!string.Equals(s, targetContent, StringComparison.Ordinal))
                    {
                        cc.Content = targetContent;
                    }
                }

                int count = 0;
                try { count = VisualTreeHelper.GetChildrenCount(curr); } catch { }
                for (int i = 0; i < count; i++)
                {
                    DependencyObject child = null;
                    try { child = VisualTreeHelper.GetChild(curr, i); } catch { }
                    if (child != null) queue.Enqueue(child);
                }
            }
        }

        private static string LoadSavedLanguage()
        {
            try
            {
                string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FieldTakHub", "ui_lang.txt");
                if (File.Exists(p))
                {
                    string raw = File.ReadAllText(p, Encoding.UTF8).Trim().ToUpperInvariant();
                    if (raw == "EN" || raw == "PL") return raw;
                }
            }
            catch { }
            return "PL";
        }
    }

    internal static class SarnegDictionaryService
    {
        private static readonly Random Rng = new Random();
        private static int _lastWordIndex = -1;

        public static readonly string[] AttachedSarnegWordsList = new string[]
        {
            "absolutnie", "dostaniemy", "dwukrotnie", "faktycznie", "informacje",
            "instrukcje", "miasteczko", "miasteczku", "najgorszym", "najlepszym",
            "obrzydliwe", "obserwacji", "pierwszych", "podejrzany", "porucznika",
            "potwierdza", "prowadzimy", "prysznicem", "przeciwnym", "przedmioty",
            "przekonamy", "przyjaciel", "przystanek", "publicznej", "spodziewam",
            "sprawdzimy", "stworzenia", "stypendium", "szacunkiem", "tajemniczy",
            "trzymajcie", "utrzymanie", "zachodniej", "zostaniemy",
            "aftershock", "algorithms", "artichokes", "background", "bankruptcy",
            "beachfront", "benchmarks", "binoculars", "birthplace", "blacksmith",
            "blockheads", "blueprints", "boyfriends", "breakdowns", "campground",
            "compatible", "complaints", "cornfields", "decorating", "downstream",
            "earthlings", "exhausting", "flavorings", "flowcharts", "fruitcakes",
            "harvesting", "headstrong", "hyperlinks", "hypocrites", "importance",
            "journalism", "journalist", "lifeguards", "lumberjack", "manuscript",
            "matchboxes", "methodical", "microwaves", "misfortune", "nightclubs",
            "nightmares", "overtaking", "paintbrush", "pathfinder", "playground",
            "playthings", "podcasting", "precaution", "previously", "productive",
            "profitable", "purchasing", "regulation", "scheduling", "scrambling",
            "smartphone", "soundtrack", "stampeding", "stockpiled", "superwoman",
            "tambourine", "timberland", "trampoline", "truckloads", "wanderlust",
            "workplaces"
        };

        public static string NormalizePolishChars(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            return input.ToUpperInvariant()
                .Replace('Ą', 'A').Replace('Ć', 'C').Replace('Ę', 'E')
                .Replace('Ł', 'L').Replace('Ń', 'N').Replace('Ó', 'O')
                .Replace('Ś', 'S').Replace('Ź', 'Z').Replace('Ż', 'Z');
        }

        public static bool Has10UniqueLetters(string word)
        {
            string clean = NormalizePolishChars(word).Trim();
            if (clean.Length != 10) return false;
            return clean.Distinct().Count() == 10 && clean.All(char.IsLetter);
        }

        public static string DrawRandomSarnegWord(string avoidWord = null)
        {
            string[] pool = AttachedSarnegWordsList;
            if (pool.Length == 0) return "PRZEDMIOTY";

            for (int attempt = 0; attempt < 12; attempt++)
            {
                int idx = Rng.Next(pool.Length);
                if (pool.Length > 1 && idx == _lastWordIndex)
                {
                    idx = (idx + 1) % pool.Length;
                }
                _lastWordIndex = idx;
                string candidate = NormalizePolishChars(pool[idx]);
                if (string.IsNullOrEmpty(avoidWord) || !string.Equals(candidate, NormalizePolishChars(avoidWord), StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
            return NormalizePolishChars(pool[Rng.Next(pool.Length)]);
        }

        public static string DrawRandomWord(bool english) => DrawRandomSarnegWord();
    }

    // =========================================================================
    // 3. SERWIS POGODOWO-ASTRONOMICZNY METOC + EKSTRAKCJA OBSZARU AO Z MAPY 1:1
    // =========================================================================
    internal sealed class MetocReportData
    {
        public string DateIso { get; set; } = "";
        public string LocationLabel { get; set; } = "";
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string ConditionText { get; set; } = "";
        public string TempRangeText { get; set; } = "";
        public string WindText { get; set; } = "";
        public string HumidityAndPrecipText { get; set; } = "";
        public string SunTimesText { get; set; } = "";
        public string MoonPhaseText { get; set; } = "";
        public string FullTacticalSummary { get; set; } = "";
    }

    internal static class OpordMetocWeatherService
    {
        public static readonly HttpClient Http = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "FieldTakHub-OPORD-METOC/3.3");
            return c;
        }

        public static (bool Found, double Lat, double Lon, string SourceDescription, string ExtractedPoiList) TryExtractAoCenterFromMapStudio(Window win)
        {
            var lats = new List<double>();
            var lons = new List<double>();
            var poiNames = new List<string>();
            string srcDesc = BuilderI18n.T("Obszar AO z Generatora Map 1:1", "AO from 1:1 Map Generator");

            try
            {
                string sourceDir = TacticalConfigPanel.GetPrimarySourceDirectory(win);
                if (Directory.Exists(sourceDir))
                {
                    var mapFiles = Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories)
                        .Where(f => f.EndsWith(".gpx", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".kml", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith("map_state.json", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".fthproj", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(File.GetLastWriteTimeUtc)
                        .ToList();

                    if (!string.IsNullOrWhiteSpace(TacticalConfigPanel.CurrentDraftProjectPath) && File.Exists(TacticalConfigPanel.CurrentDraftProjectPath))
                    {
                        mapFiles.Insert(0, TacticalConfigPanel.CurrentDraftProjectPath);
                    }

                    foreach (string mf in mapFiles.Distinct())
                    {
                        string raw = File.ReadAllText(mf, Encoding.UTF8);

                        var wptMatches = Regex.Matches(raw, @"<wpt\s+lat=""([0-9\.\-]+)""\s+lon=""([0-9\.\-]+)""[^>]*>(.*?)</wpt>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                        foreach (Match m in wptMatches)
                        {
                            if (double.TryParse(m.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double plat) &&
                                double.TryParse(m.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double plon))
                            {
                                lats.Add(plat);
                                lons.Add(plon);
                                var nameMatch = Regex.Match(m.Groups[3].Value, @"<name>(.*?)</name>", RegexOptions.IgnoreCase);
                                if (nameMatch.Success)
                                {
                                    string pName = WebUtility.HtmlDecode(nameMatch.Groups[1].Value.Trim());
                                    if (!string.IsNullOrWhiteSpace(pName) && !poiNames.Contains(pName))
                                        poiNames.Add("• " + pName + string.Format(CultureInfo.InvariantCulture, " ({0:0.0000}°N, {1:0.0000}°E)", plat, plon));
                                }
                            }
                        }

                        var jsonLatLon = Regex.Matches(raw, @"""lat""\s*:\s*([0-9\.\-]+)\s*,\s*""lon""\s*:\s*([0-9\.\-]+)", RegexOptions.IgnoreCase);
                        foreach (Match jm in jsonLatLon)
                        {
                            if (double.TryParse(jm.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double jlat) &&
                                double.TryParse(jm.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double jlon))
                            {
                                lats.Add(jlat);
                                lons.Add(jlon);
                            }
                        }

                        if (lats.Count > 0)
                        {
                            srcDesc = BuilderI18n.T("Obszar AO z Mapy (", "Map AO (") + Path.GetFileName(mf) + ")";
                            break;
                        }
                    }
                }
            }
            catch { }

            if (lats.Count > 0 && lons.Count > 0)
            {
                double avgLat = (lats.Min() + lats.Max()) / 2.0;
                double avgLon = (lons.Min() + lons.Max()) / 2.0;
                string poiSummary = poiNames.Count > 0 ? string.Join("\n", poiNames) : "";
                return (true, avgLat, avgLon, srcDesc, poiSummary);
            }

            return (false, 49.8494, 18.8127, "", "");
        }

        public static async Task<(bool Ok, double Lat, double Lon, string DisplayName)> GeocodeLocationAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return (false, 0, 0, "");
            try
            {
                string langParam = BuilderI18n.CurrentLang == "EN" ? "en" : "pl";
                string url = "https://geocoding-api.open-meteo.com/v1/search?name=" +
                             Uri.EscapeDataString(query.Trim()) + "&count=1&language=" + langParam + "&format=json";
                string json = await Http.GetStringAsync(url);
                if (JsonNode.Parse(json) is JsonObject root &&
                    root["results"] is JsonArray arr && arr.Count > 0 &&
                    arr[0] is JsonObject first)
                {
                    double lat = first["latitude"]?.GetValue<double>() ?? 0;
                    double lon = first["longitude"]?.GetValue<double>() ?? 0;
                    string name = first["name"]?.ToString() ?? query;
                    string admin = first["admin1"]?.ToString() ?? "";
                    string country = first["country_code"]?.ToString() ?? "";
                    string label = string.IsNullOrWhiteSpace(admin) ? name : (name + " (" + admin + ", " + country + ")");
                    return (true, lat, lon, label);
                }
            }
            catch { }
            return (false, 0, 0, "");
        }

        public static async Task<MetocReportData> FetchMetocReportAsync(double lat, double lon, string dateIso, string locationLabel)
        {
            DateTime targetDate;
            if (!DateTime.TryParseExact(dateIso?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out targetDate))
            {
                targetDate = DateTime.Today;
            }
            string cleanDate = targetDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            var moonInfo = CalculateMoonPhaseAndIllumination(targetDate);
            var fallbackSun = CalculateAstronomicalSunTimes(targetDate, lat, lon);

            string condText = BuilderI18n.T("Umiarkowane zachmurzenie, dobra widoczność", "Moderate cloud cover, good visibility");
            string tempText = "-- °C";
            string windText = "-- km/h";
            string humPrecipText = "-- % / 0.0 mm";
            string bmntStr = fallbackSun.Bmnt;
            string srStr = fallbackSun.Sunrise;
            string ssStr = fallbackSun.Sunset;
            string eentStr = fallbackSun.Eent;

            try
            {
                string latS = lat.ToString("0.0000", CultureInfo.InvariantCulture);
                string lonS = lon.ToString("0.0000", CultureInfo.InvariantCulture);

                double daysDiff = (targetDate.Date - DateTime.Today).TotalDays;
                string baseUrl = (daysDiff < -2)
                    ? "https://archive-api.open-meteo.com/v1/archive"
                    : "https://api.open-meteo.com/v1/forecast";

                string url = baseUrl +
                    "?latitude=" + latS +
                    "&longitude=" + lonS +
                    "&daily=weathercode,temperature_2m_max,temperature_2m_min,apparent_temperature_max,apparent_temperature_min,sunrise,sunset,precipitation_sum,precipitation_probability_max,windspeed_10m_max,windgusts_10m_max,winddirection_10m_dominant" +
                    "&hourly=relativehumidity_2m,cloudcover,surface_pressure" +
                    "&timezone=auto&start_date=" + cleanDate + "&end_date=" + cleanDate;

                string rawJson = await Http.GetStringAsync(url);
                if (JsonNode.Parse(rawJson) is JsonObject root && root["daily"] is JsonObject daily)
                {
                    int wmoCode = GetFirstInt(daily["weathercode"], 2);
                    double tMax = GetFirstDouble(daily["temperature_2m_max"], 15.0);
                    double tMin = GetFirstDouble(daily["temperature_2m_min"], 6.0);
                    double appMax = GetFirstDouble(daily["apparent_temperature_max"], tMax);
                    double appMin = GetFirstDouble(daily["apparent_temperature_min"], tMin);
                    double precipMm = GetFirstDouble(daily["precipitation_sum"], 0.0);
                    int precipProb = GetFirstInt(daily["precipitation_probability_max"], precipMm > 0.2 ? 65 : 10);
                    double windMaxKmh = GetFirstDouble(daily["windspeed_10m_max"], 12.0);
                    double gustsMaxKmh = GetFirstDouble(daily["windgusts_10m_max"], windMaxKmh * 1.4);
                    int windDeg = GetFirstInt(daily["winddirection_10m_dominant"], 270);

                    string apiSr = GetFirstString(daily["sunrise"], "");
                    string apiSs = GetFirstString(daily["sunset"], "");
                    if (DateTime.TryParse(apiSr, out DateTime parsedSr))
                    {
                        srStr = parsedSr.ToString("HH:mm");
                        bmntStr = parsedSr.AddMinutes(-34).ToString("HH:mm");
                    }
                    if (DateTime.TryParse(apiSs, out DateTime parsedSs))
                    {
                        ssStr = parsedSs.ToString("HH:mm");
                        eentStr = parsedSs.AddMinutes(34).ToString("HH:mm");
                    }

                    int avgHumidity = 72;
                    int avgClouds = 50;
                    double avgPressure = 1013.0;
                    if (root["hourly"] is JsonObject hourly)
                    {
                        avgHumidity = (int)Math.Round(GetAverageDouble(hourly["relativehumidity_2m"], 72.0));
                        avgClouds = (int)Math.Round(GetAverageDouble(hourly["cloudcover"], 50.0));
                        avgPressure = Math.Round(GetAverageDouble(hourly["surface_pressure"], 1013.0), 0);
                    }

                    string visEst = (wmoCode == 45 || wmoCode == 48)
                        ? BuilderI18n.T("Ograniczona (mgła < 300m)", "Reduced (fog < 300m)")
                        : (precipMm > 5.0 ? BuilderI18n.T("Umiarkowana (opady 1-3 km)", "Moderate (rain 1-3 km)") : BuilderI18n.T("Dobra (> 5 km)", "Good (> 5 km)"));

                    condText = DecodeWmoWeatherCode(wmoCode) + string.Format(
                        CultureInfo.InvariantCulture,
                        BuilderI18n.T(" | Zachmurzenie: {0}% | Widoczność: {1} | QNH: {2:0} hPa", " | Clouds: {0}% | Visibility: {1} | QNH: {2:0} hPa"),
                        avgClouds, visEst, avgPressure);

                    tempText = string.Format(
                        CultureInfo.InvariantCulture,
                        BuilderI18n.T("{0:0.0}°C .. {1:0.0}°C  (Odczuwalna: {2:0.0}°C .. {3:0.0}°C)", "{0:0.0}°C .. {1:0.0}°C  (Feels like: {2:0.0}°C .. {3:0.0}°C)"),
                        tMin, tMax, appMin, appMax);

                    double windMs = windMaxKmh / 3.6;
                    double gustsMs = gustsMaxKmh / 3.6;
                    string dirCardinal = DegreesToCardinal(windDeg);
                    windText = string.Format(
                        CultureInfo.InvariantCulture,
                        BuilderI18n.T(
                            "{0:0.0} km/h ({1:0.0} m/s)  |  Porywy: {2:0.0} km/h ({3:0.0} m/s)  |  Kierunek: {4}° ({5})",
                            "{0:0.0} km/h ({1:0.0} m/s)  |  Gusts: {2:0.0} km/h ({3:0.0} m/s)  |  Dir: {4}° ({5})"),
                        windMaxKmh, windMs, gustsMaxKmh, gustsMs, windDeg, dirCardinal);

                    humPrecipText = string.Format(
                        CultureInfo.InvariantCulture,
                        BuilderI18n.T(
                            "Wilgotność: ~{0}%  |  Opady: {1:0.0} mm (Szansa: {2}%)",
                            "Humidity: ~{0}%  |  Precip: {1:0.0} mm (Chance: {2}%)"),
                        avgHumidity, precipMm, precipProb);
                }
            }
            catch { }

            string sunLine = string.Format(
                BuilderI18n.T(
                    "BMNT (Świt): {0}  |  ☀️ Wschód (SR): {1}  |  🌇 Zachód (SS): {2}  |  EENT (Zmierzch NVG): {3}",
                    "BMNT (Dawn): {0}  |  ☀️ Sunrise (SR): {1}  |  🌇 Sunset (SS): {2}  |  EENT (Dusk NVG): {3}"),
                bmntStr, srStr, ssStr, eentStr);

            string locDisplay = string.IsNullOrWhiteSpace(locationLabel)
                ? string.Format(CultureInfo.InvariantCulture, "{0:0.0000}°N, {1:0.0000}°E", lat, lon)
                : string.Format(CultureInfo.InvariantCulture, "{0} ({1:0.0000}°N, {2:0.0000}°E)", locationLabel, lat, lon);

            var sb = new StringBuilder();
            sb.AppendLine(BuilderI18n.T(
                "🌦️ [PROGNOZA POGODY I DANE ŚWIETLNE METOC — " + cleanDate + "]",
                "🌦️ [METOC WEATHER & LIGHT REPORT — " + cleanDate + "]"));
            sb.AppendLine(BuilderI18n.T("• Rejon AO: ", "• AO Region: ") + locDisplay);
            sb.AppendLine(BuilderI18n.T("• Zjawiska i widoczność: ", "• Conditions & Visibility: ") + condText);
            sb.AppendLine(BuilderI18n.T("• Temperatura: ", "• Temperature: ") + tempText);
            sb.AppendLine(BuilderI18n.T("• Wiatr: ", "• Wind: ") + windText);
            sb.AppendLine(BuilderI18n.T("• Wilgotność i opady: ", "• Humidity & Precip: ") + humPrecipText);
            sb.AppendLine(BuilderI18n.T("• Słońce (BMNT / Wschód / Zachód / EENT): ", "• Sun (BMNT / SR / SS / EENT): ") + sunLine);
            sb.AppendLine(BuilderI18n.T("• Faza Księżyca i NVG: ", "• Moon Phase & NVG: ") + moonInfo.FullText);

            return new MetocReportData
            {
                DateIso = cleanDate,
                LocationLabel = locDisplay,
                Latitude = lat,
                Longitude = lon,
                ConditionText = condText,
                TempRangeText = tempText,
                WindText = windText,
                HumidityAndPrecipText = humPrecipText,
                SunTimesText = sunLine,
                MoonPhaseText = moonInfo.FullText,
                FullTacticalSummary = sb.ToString().TrimEnd()
            };
        }

        private static (string PhaseName, int IlluminationPct, string FullText) CalculateMoonPhaseAndIllumination(DateTime date)
        {
            DateTime knownNewMoon = new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);
            DateTime sampleUtc = new DateTime(date.Year, date.Month, date.Day, 22, 0, 0, DateTimeKind.Utc);
            double synodicMonth = 29.53058867;
            double daysSince = (sampleUtc - knownNewMoon).TotalDays;
            double cyclePos = ((daysSince % synodicMonth) + synodicMonth) % synodicMonth;
            double phaseFraction = cyclePos / synodicMonth;

            double illumFraction = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * phaseFraction));
            int illumPct = (int)Math.Round(illumFraction * 100.0);

            string phasePl;
            string phaseEn;
            string icon;
            if (phaseFraction < 0.04 || phaseFraction >= 0.96) { icon = "🌑"; phasePl = "Nów (New Moon)"; phaseEn = "New Moon"; }
            else if (phaseFraction < 0.21) { icon = "🌒"; phasePl = "Przybywający Sierp"; phaseEn = "Waxing Crescent"; }
            else if (phaseFraction < 0.29) { icon = "🌓"; phasePl = "Pierwsza Kwadra"; phaseEn = "First Quarter"; }
            else if (phaseFraction < 0.46) { icon = "🌔"; phasePl = "Przybywający Garb"; phaseEn = "Waxing Gibbous"; }
            else if (phaseFraction < 0.54) { icon = "🌕"; phasePl = "Pełnia (Full Moon)"; phaseEn = "Full Moon"; }
            else if (phaseFraction < 0.71) { icon = "🌖"; phasePl = "Ubywający Garb"; phaseEn = "Waning Gibbous"; }
            else if (phaseFraction < 0.79) { icon = "🌗"; phasePl = "Trzecia Kwadra"; phaseEn = "Last Quarter"; }
            else { icon = "🌘"; phasePl = "Ubywający Sierp"; phaseEn = "Waning Crescent"; }

            string nvgRating = illumPct >= 55
                ? BuilderI18n.T("Dobra widoczność pasywna NVG", "Good passive NVG visibility")
                : (illumPct >= 20 ? BuilderI18n.T("Umiarkowana jasność nocna", "Moderate ambient light") : BuilderI18n.T("Ciemna noc (zalecane IR)", "Dark night (IR recommended)"));

            string phaseLabel = BuilderI18n.T(phasePl, phaseEn);
            string full = string.Format(
                CultureInfo.InvariantCulture,
                BuilderI18n.T("{0} {1}  |  Iluminacja: {2}%  |  {3}", "{0} {1}  |  Illumination: {2}%  |  {3}"),
                icon, phaseLabel, illumPct, nvgRating);
            return (phaseLabel, illumPct, full);
        }

        private static (string Bmnt, string Sunrise, string Sunset, string Eent) CalculateAstronomicalSunTimes(DateTime date, double lat, double lon)
        {
            try
            {
                int dayOfYear = date.DayOfYear;
                double gamma = 2.0 * Math.PI / 365.0 * (dayOfYear - 1);
                double eqTime = 229.18 * (0.000075 + 0.001868 * Math.Cos(gamma) - 0.032077 * Math.Sin(gamma) - 0.014615 * Math.Cos(2 * gamma) - 0.040849 * Math.Sin(2 * gamma));
                double decl = 0.006918 - 0.399912 * Math.Cos(gamma) + 0.070257 * Math.Sin(gamma) - 0.006758 * Math.Cos(2 * gamma) + 0.000907 * Math.Sin(2 * gamma);
                double latRad = lat * Math.PI / 180.0;
                double zenithRad = 90.833 * Math.PI / 180.0;
                double cosHa = Math.Clamp((Math.Cos(zenithRad) / (Math.Cos(latRad) * Math.Cos(decl))) - Math.Tan(latRad) * Math.Tan(decl), -1.0, 1.0);
                double haDeg = Math.Acos(cosHa) * 180.0 / Math.PI;
                double tzOffsetHours = TimeZoneInfo.Local.GetUtcOffset(date).TotalHours;
                double srMinutes = 720.0 - 4.0 * (lon + haDeg) - eqTime + tzOffsetHours * 60.0;
                double ssMinutes = 720.0 - 4.0 * (lon - haDeg) - eqTime + tzOffsetHours * 60.0;

                TimeSpan srTs = TimeSpan.FromMinutes(Math.Clamp(srMinutes, 0, 1439));
                TimeSpan ssTs = TimeSpan.FromMinutes(Math.Clamp(ssMinutes, 0, 1439));
                TimeSpan bmntTs = TimeSpan.FromMinutes(Math.Clamp(srMinutes - 34, 0, 1439));
                TimeSpan eentTs = TimeSpan.FromMinutes(Math.Clamp(ssMinutes + 34, 0, 1439));

                return (
                    string.Format("{0:D2}:{1:D2}", bmntTs.Hours, bmntTs.Minutes),
                    string.Format("{0:D2}:{1:D2}", srTs.Hours, srTs.Minutes),
                    string.Format("{0:D2}:{1:D2}", ssTs.Hours, ssTs.Minutes),
                    string.Format("{0:D2}:{1:D2}", eentTs.Hours, eentTs.Minutes)
                );
            }
            catch { return ("05:45", "06:20", "18:10", "18:45"); }
        }

        private static string DecodeWmoWeatherCode(int code) => code switch
        {
            0 => BuilderI18n.T("☀️ Bezchmurnie", "☀️ Clear sky"),
            1 => BuilderI18n.T("🌤️ Przeważnie słonecznie", "🌤️ Mainly clear"),
            2 => BuilderI18n.T("⛅ Częściowe zachmurzenie", "⛅ Partly cloudy"),
            3 => BuilderI18n.T("☁️ Pochmurno", "☁️ Overcast"),
            45 or 48 => BuilderI18n.T("🌫️ Mgła", "🌫️ Fog"),
            51 or 53 or 55 => BuilderI18n.T("🌦️ Mżawka", "🌦️ Drizzle"),
            61 or 63 or 65 => BuilderI18n.T("🌧️ Opady deszczu", "🌧️ Rain"),
            71 or 73 or 75 or 77 => BuilderI18n.T("❄️ Opady śniegu", "❄️ Snowfall"),
            80 or 81 or 82 => BuilderI18n.T("🌧️ Przelotny deszcz", "🌧️ Rain showers"),
            95 or 96 or 99 => BuilderI18n.T("⛈️ Burza", "⛈️ Thunderstorm"),
            _ => BuilderI18n.T("⛅ Zmienne zachmurzenie", "⛅ Variable cloudiness")
        };

        private static string DegreesToCardinal(int deg)
        {
            string[] dirs = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            return dirs[(int)Math.Round(((deg % 360) + 360) % 360 / 45.0) % 8];
        }

        private static double GetFirstDouble(JsonNode n, double fb) { try { if (n is JsonArray a && a.Count > 0 && a[0] != null) return Convert.ToDouble(a[0].ToString(), CultureInfo.InvariantCulture); } catch { } return fb; }
        private static int GetFirstInt(JsonNode n, int fb) { try { if (n is JsonArray a && a.Count > 0 && a[0] != null) return (int)Math.Round(Convert.ToDouble(a[0].ToString(), CultureInfo.InvariantCulture)); } catch { } return fb; }
        private static string GetFirstString(JsonNode n, string fb) { try { if (n is JsonArray a && a.Count > 0 && a[0] != null) return a[0].ToString(); } catch { } return fb; }
        private static double GetAverageDouble(JsonNode n, double fb)
        {
            try
            {
                if (n is JsonArray a && a.Count > 0)
                {
                    var v = new List<double>();
                    foreach (var x in a) if (x != null && double.TryParse(x.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double d)) v.Add(d);
                    if (v.Count > 0) return v.Average();
                }
            }
            catch { }
            return fb;
        }
    }

    // =========================================================================
    // 4. MODELE DANYCH OPORD v3.3 — 100% NIEZALEŻNOŚCI KAŻDEGO DNIA OPERACJI
    // =========================================================================
    internal sealed class OpordRosterRow
    {
        public string OperatorOrTeam { get; set; } = "ALFA-1";
        public string PrimaryRole { get; set; } = "Lider (Dowódca polowy)";
        public string SecondaryRole { get; set; } = "RTO (Radiooperator)";
        public string WeaponAndGear { get; set; } = "Szturm (Karabinek) + ATAK + Radio VHF";
    }

    internal sealed class OpordRoleDefinitionRow
    {
        public string RoleName { get; set; } = "Lider (Dowódca polowy)";
        public string Description { get; set; } = "Dowodzi zespołem w trakcie działań, podejmuje kluczowe decyzje, odpowiada za wykonanie zadań.";
        public string DutyMode { get; set; } = "Full-time";
    }

    internal sealed class OpordCodewordRow
    {
        public string CodeWord { get; set; } = "ZAMEK";
        public string CodeLetter { get; set; } = "A";
        public string CodeDigit { get; set; } = "1";
        public string DuressWord { get; set; } = "SPALONY";
    }

    // Każdy dzień operacji posiada własny, w 100% niezależny zestaw wszystkich sekcji rozkazu OPORD!
    internal sealed class OpordDayPlanModel
    {
        public string PrimaryChannel { get; set; } = "Dowodzenie HQ: CH 1 (446.00625 MHz) [PMR] (CTCSS 67.0 Hz)";
        public string AlternateChannel { get; set; } = "Taktyczny: CH 3 (446.03125 MHz) [PMR] (CTCSS 88.5 Hz)";
        public string HqChannel { get; set; } = "Ratunkowy ORGA: CH 8 (446.09375 MHz) [PMR] (Brak tonu)";
        public string SyncedCommsMatrixSummary { get; set; } = "";
        public bool IncludeCustomChannelsTable { get; set; } = false;
        public string CustomChannelsTableText { get; set; } = "• Kanał Sekcji Alfa: PMR CH 2 (446.01875 MHz) | CTCSS 77.0 Hz\n• Kanał Zwiadu / Snajper: PMR CH 5 (446.05625 MHz) | CTCSS 110.9 Hz";
        public bool IncludeAnnex3Map { get; set; } = false;
        public string Annex3MapImagePath { get; set; } = "";
        public string Annex3MapNotes { get; set; } = "Mapa taktyczna z siatką współrzędnych, granicą AO oraz punktami kluczowymi.";
        public string DayTitle { get; set; } = "Dzień 1";
        public bool IncludeInExport { get; set; } = true;
        public string DateIso { get; set; } = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // 0. Nagłówek i klauzula dnia
        public bool IsSecretClassification { get; set; } = false;
        public string Classification { get; set; } = "JAWNE / UNCLASSIFIED — MILSIM DRUŻYNOWY OPORD";
        public string OperationNumber { get; set; } = "OPORD 01-2026";
        public string DtgIssued { get; set; } = DateTime.Now.ToString("ddHHmm'Z' MMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();
        public string IssuingHq { get; set; } = "TOC / SZTAB DRUŻYNY";
        public string MapReferences { get; set; } = "Mapa Taktyczna MGRS 1:5000 (W załączeniu .FTAK / .KMZ / .GPX)";
        public string DaySpecificTasks { get; set; } = "• 08:30 — Odprawa sztabowa i sprawdzenie łączności\n• 09:00 — Wejście w rejon działań\n• 14:00 — Rotacja i realizacja zadań głównych";

        // I. Sytuacja i METOC dnia
        public string LocationName { get; set; } = "";
        public string LocationLat { get; set; } = "49.8494";
        public string LocationLon { get; set; } = "18.8127";
        public string AreaOfInterestPoiDesc { get; set; } = "Punkty kluczowe (FOB, Respawn, OBJ ALPHA, LZ) zdefiniowane w załączonym pliku .KMZ (ATAK) oraz .GPX (Garmin/Suunto).";
        public string TerrainAndWeather { get; set; } = "Wielkość AO: ok. 2x2 km. Rodzaj: las mieszany, polany, zabudowania leśne. Ukształtowanie: pagórkowate ze skarpami.";
        public string MetocCondition { get; set; } = "Brak pobranego raportu (kliknij przycisk METOC dla tego dnia)";
        public string MetocTemperature { get; set; } = "-";
        public string MetocWind { get; set; } = "-";
        public string MetocHumidityPrecip { get; set; } = "-";
        public string MetocSunTimes { get; set; } = "-";
        public string MetocMoonPhase { get; set; } = "-";
        public string EnemyForces { get; set; } = "1. Skład, dyspozycja i siła: Pluton lekki przeciwnika podzielony na patrole 4-6 osobowe.\n2. Lokalizacje i możliwości: Posterunki na skrzyżowaniach dróg.\n3. Kursy działań wroga (COA): Obrona rejonu centralnego i zasadzki.";
        public string FriendlyForces { get; set; } = "1. Misja i zamiar wyższego dowództwa (HQ): Opanowanie i kontrola sektora przed zmrokiem.\n2. Misja sąsiednich jednostek: Wsparcie na skrzydłach.";

        // II. Misja (5W) dnia
        public string MissionLoreDesc { get; set; } = "Opis fabularny (opcjonalnie): Działania rozpoznawczo-dywersyjne na zapleczu ugrupowania przeciwnika.";
        public string MissionWho { get; set; } = "Drużyna zadaniowa BLUFOR";
        public string MissionWhat { get; set; } = "zlokalizuje i zabezpieczy kody sektorowe oraz utrzyma obiekt OBJ ALPHA";
        public string MissionWhen { get; set; } = "w godzinach operacyjnych 09:00 - 17:00";
        public string MissionWhere { get; set; } = "w wyznaczonym obszarze AO (siatka MGRS)";
        public string MissionWhy { get; set; } = "w celu uzyskania przewagi punktowej i odcięcia zaopatrzenia przeciwnika.";
        public string MissionFullStatement { get; set; } = "";
        public string AdditionalTasksSummary { get; set; } = "• Zadanie poboczne 1: Rozpoznanie stanowisk dowodzenia wroga (Foto).\n• Zadanie poboczne 2: Przejęcie pojemnika fabularnego (szczegóły w Załączniku 2).";

        // III. Wykonanie, Skład, Łączność, SARNEG i Hasła dnia
        public string CommandersIntent { get; set; } = "Jedziemy działać zespołowo, przećwiczyć nawigację MGRS/ATAK i zrealizować 100% zadań głównych bez zbędnych strat.";
        public string ConceptOfOperations { get; set; } = "Priorytety i styl działania: Skryte przemieszczanie czujką z przodu. Unikamy niepotrzebnego kontaktu ogniowego podczas marszu do celu.";
        public string TasksToSubordinateUnits { get; set; } = "• Sekcja Szturmowa: Wejście na obiekt i czyszczenie przedpola.\n• Wsparcie / DMR: Osłona skrzydeł.";
        public string CoordinatingAndRoe { get; set; } = "• Obowiązkowa ochrona wzroku przez cały czas trwania gry.\n• Weryfikacja pozycji sojuszników w ATAK.";
        public string SuccessionOfCommand { get; set; } = "Dowódca (Lider)  ->  1. Zastępca (Navi / RTO)  ->  2. Zastępca (Dowódca sekcji)";

        public bool EnableSquadRosterTable { get; set; } = true;
        public List<OpordRosterRow> SquadRoster { get; set; } = new List<OpordRosterRow>();

        public string PrimaryRadioFreq { get; set; } = "446.00625 MHz (PMR CH 1)";
        public string BackupRadioFreq { get; set; } = "446.03125 MHz (PMR CH 3)";
        public string HqRadioFreq { get; set; } = "446.09375 MHz (PMR CH 8)";

        public string TargetCodeNames { get; set; } = "• OBJ ALPHA = 'ZAMEK'   |   • Punkt Zbiórki (RP) = 'KUŹNIA'   |   • FOB / Respawn = 'BAZA'";
        public string SarnegKeyWord10 { get; set; } = "PRZEDMIOTY";
        public List<OpordCodewordRow> CodewordsTable { get; set; } = new List<OpordCodewordRow>();
        public string ChallengeAndPassword { get; set; } = "a) Werbalne (Wywołanie - Odpowiedź - Potwierdzenie): GROM - BŁYSK - TARCZA (Suma cyfr: 7)\nb) Niewerbalne (Wywołanie - Odzew): 2 błyski czerwonym światłem - 1 błysk + podniesienie lewej ręki";
        public string MedevacRules { get; set; } = "Pierwsze trafienie: opatrunek przez medyka lub członka drużyny (czas 2 min). Drugie trafienie: odejście na Respawn. W razie wypadku: przycisk 🚨 SOS MGRS w Field TAK Hub.";

        // IV. Logistyka i Załączniki dnia
        public string RespawnAndSupply { get; set; } = "Styl wyposażenia: Na 12h w terenie (woda min. 2L, racja żywnościowa, zapasowe baterie do radia i powerbank do ATAK).";
        public string SquadGearChecklist { get; set; } = "SZPEJLISTA: Okulary ochronne, czerwona szmatka (dead-rag), latarka czerwona, radio PMR/VHF, telefon z Field TAK Hub & ATAK-CIV, kompas.";
        public string SpecialEquipment { get; set; } = "Wyposażenie specjalne: Rekwizyty misyjne ze sztabu, znaczniki chemiczne (lightstick), aparat/telefon dla Fotografa.";
        public string TravelAndParking { get; set; } = "1. Parking: Zgodnie z pinezką zbiórki w paczce .GPX / .KMZ.\n2. Odległość z parkingu na miejsce gry: ok. 300 m.\n3. Transport: Auto 1, Auto 2.";
        public string GameRulesLink { get; set; } = "https://";

        public bool EnableRolesAnnex { get; set; } = false;
        public List<OpordRoleDefinitionRow> CustomRolesAnnex { get; set; } = new List<OpordRoleDefinitionRow>();
        public string Annex2TasksAnalysis { get; set; } = "ZAŁĄCZNIK 2 — OPRACOWANIE ZADAŃ:\n• Analiza priorytetów punktowych, okna czasowe na wykonanie misji specjalnych oraz podział zespołów na zadania równoległe.";
    }

    internal sealed class OpordParagraphModel
    {
        public string EventName { get; set; } = "MILSIM 2026";
        public string TargetFaction { get; set; } = "BLUFOR";
        public List<OpordDayPlanModel> Days { get; set; } = new List<OpordDayPlanModel>();
        public int ActiveDayIndex { get; set; } = 0;
        public string MetocCondition { get; set; } = "-";
        public string MetocTemperature { get; set; } = "-";
        public string MetocWind { get; set; } = "-";
        public string MetocHumidityPrecip { get; set; } = "-";
        public string MetocSunTimes { get; set; } = "-";
        public string MetocMoonPhase { get; set; } = "-";
        public string TasksToSubordinateUnits { get; set; } = "";
        public string CoordinatingAndRoe { get; set; } = "";
        public List<OpordCodewordRow> CodewordsTable { get; set; } = new List<OpordCodewordRow>();

        // Pola zachowane dla kompatybilności wstecznej przy odczycie starszych plików JSON
        public bool IsSecretClassification { get; set; } = false;
        public string Classification { get; set; } = "JAWNE / UNCLASSIFIED — MILSIM DRUŻYNOWY OPORD";
        public string OperationNumber { get; set; } = "OPORD 01-2026";
        public string OperationDateIso { get; set; } = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        public string LocationName { get; set; } = "";
        public string LocationLat { get; set; } = "49.8494";
        public string LocationLon { get; set; } = "18.8127";
        public string DtgIssued { get; set; } = DateTime.Now.ToString("ddHHmm'Z' MMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();
        public string IssuingHq { get; set; } = "TOC / SZTAB DRUŻYNY";
        public string MapReferences { get; set; } = "Mapa Taktyczna MGRS 1:5000 (W załączeniu .FTAK / .KMZ / .GPX)";
        public string AreaOfInterestPoiDesc { get; set; } = "";
        public string TerrainAndWeather { get; set; } = "";
        public string EnemyForces { get; set; } = "";
        public string FriendlyForces { get; set; } = "";
        public string MissionLoreDesc { get; set; } = "";
        public string MissionWho { get; set; } = "Drużyna zadaniowa BLUFOR";
        public string MissionWhat { get; set; } = "zlokalizuje i zabezpieczy kody sektorowe oraz utrzyma obiekt OBJ ALPHA";
        public string MissionWhen { get; set; } = "w godzinach operacyjnych 09:00 - 17:00";
        public string MissionWhere { get; set; } = "w wyznaczonym obszarze AO (siatka MGRS)";
        public string MissionWhy { get; set; } = "w celu uzyskania przewagi punktowej i odcięcia zaopatrzenia przeciwnika.";
        public string MissionFullStatement { get; set; } = "";
        public string AdditionalTasksSummary { get; set; } = "";
        public string CommandersIntent { get; set; } = "";
        public string ConceptOfOperations { get; set; } = "";
        public string SuccessionOfCommand { get; set; } = "";
        public bool EnableSquadRosterTable { get; set; } = true;
        public List<OpordRosterRow> SquadRoster { get; set; } = new List<OpordRosterRow>();
        public string PrimaryRadioFreq { get; set; } = "446.00625 MHz (PMR CH 1)";
        public string BackupRadioFreq { get; set; } = "446.03125 MHz (PMR CH 3)";
        public string HqRadioFreq { get; set; } = "446.09375 MHz (PMR CH 8)";
        public string TargetCodeNames { get; set; } = "";
        public string SarnegKeyWord10 { get; set; } = "PRZEDMIOTY";
        public string ChallengeAndPassword { get; set; } = "";
        public string MedevacRules { get; set; } = "";
        public string RespawnAndSupply { get; set; } = "";
        public string SquadGearChecklist { get; set; } = "";
        public string SpecialEquipment { get; set; } = "";
        public string TravelAndParking { get; set; } = "";
        public string GameRulesLink { get; set; } = "https://";
        public bool EnableRolesAnnex { get; set; } = false;
        public List<OpordRoleDefinitionRow> CustomRolesAnnex { get; set; } = new List<OpordRoleDefinitionRow>();
        public string Annex2TasksAnalysis { get; set; } = "";
    }

    // =========================================================================
    // 5. ZARZĄDCA OPORD v3.3 — CZĘŚĆ 1 (LOGIKA NIEZALEŻNYCH DNI, KLONOWANIE DNIA, HTML A4, DISCORD)
    // =========================================================================
    internal static partial class OpordBuilderManager
    {
        private static readonly Dictionary<string, OpordParagraphModel> ModelsByFaction = new Dictionary<string, OpordParagraphModel>(StringComparer.OrdinalIgnoreCase);
        private static string _activeOpordFaction = "BLUFOR";
        private static Border _cachedOpordOverlay;

        private static TcpListener _opordLanListener;
        private static volatile bool _opordLanServerRunning;
        private static int _opordLanPort = 8769;
        private static Window _lastActiveWindowRef;

        public static readonly (string RoleName, string Mode, string Description)[] DefaultMilsimRolesCatalog = new[]
        {
            ("Lider (Dowódca polowy)", "Full-time", "Dowodzi zespołem w trakcie działań, podejmuje kluczowe decyzje, odpowiada za wykonanie zadań."),
            ("Navi (Nawigator)", "Full-time", "Nadzoruje mapę, informacje o POI, wyznacza trasy, nadzoruje kierunek marszu."),
            ("RTO (Radiooperator)", "Full-time", "Odpowiada za kontakt ze sztabem, odbieranie rozkazów i podawanie kluczowych informacji, zna szyfrowanie."),
            ("Med (Medyk)", "Sytuacyjne", "Odpowiada za sprawy medyczne: leczenie, opatrunki, ilość dostępnych respów, karty ran itp."),
            ("Adiutant", "Sytuacyjne", "Szczegółowo analizuje treść zadań oraz dodatkowych informacji, spisuje kluczowe dane i punkty, przekazuje dowódcy."),
            ("LARP (Aktor)", "Sytuacyjne", "Osoba do larpowania, kontaktu z NPC oraz innymi osobami larpującymi, znająca fabułę gry."),
            ("Foto (Fotograf)", "Sytuacyjne", "Osoba odpowiedzialna za dokumentowanie działań (np. zdjęcia celów, kodów), wypełnianie dokumentów dot. misji."),
            ("Tragarz", "Sytuacyjne", "Osoba odpowiedzialna za przedmioty wymagane do zadań: transport i przygotowanie przedmiotów fabularnych."),
            ("O-O (Organizator Obrony)", "Sytuacyjne", "Odpowiada za ustanawianie obrony w trakcie postoju: rozstawienie na sektory, wysunięcie czujki, obstawianie wzgórz."),
            ("Czujka", "Przechodnie", "Przewodzi podczas marszu, koryguje trasę, szuka przejścia, wypatruje zagrożenia z przodu.")
        };

        public static void ResetAllOpordsToZeroState(Window win)
        {
            try
            {
                ModelsByFaction.Clear();
                _activeOpordFaction = "BLUFOR";

                string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FieldTakHub");
                if (Directory.Exists(appData))
                {
                    foreach (string f in Directory.GetFiles(appData, "opord_*.json", SearchOption.TopDirectoryOnly))
                    {
                        try { File.Delete(f); } catch { }
                    }
                }

                if (win != null)
                {
                    string sourceDir = TacticalConfigPanel.GetPrimarySourceDirectory(win);
                    string factionsDir = Path.Combine(sourceDir, "factions");
                    if (Directory.Exists(factionsDir))
                    {
                        foreach (string f in Directory.GetFiles(factionsDir, "*opord*", SearchOption.AllDirectories))
                        {
                            try { File.Delete(f); } catch { }
                        }
                    }
                }

                if (_cachedOpordOverlay != null)
                {
                    if (VisualTreeHelper.GetParent(_cachedOpordOverlay) is Panel parentPanel)
                    {
                        parentPanel.Children.Remove(_cachedOpordOverlay);
                    }
                    _cachedOpordOverlay = null;
                }
            }
            catch { }
        }

        private static readonly (string Pl, string En)[] OpordBilingualPairs = new (string Pl, string En)[]
        {
            // Nazwy ról drużynowych i trybów pracy
            ("Lider (Dowódca polowy)", "Leader (Field Commander)"),
            ("Navi (Nawigator)", "Navi (Navigator)"),
            ("RTO (Radiooperator)", "RTO (Radio Operator)"),
            ("Med (Medyk)", "Med (Medic)"),
            ("Adiutant", "Adjutant"),
            ("LARP (Aktor)", "LARP (Roleplayer)"),
            ("Foto (Fotograf)", "Photo (Photographer)"),
            ("Tragarz", "Carrier (Mission Props)"),
            ("O-O (Organizator Obrony)", "D-O (Defense Organizer)"),
            ("Czujka", "Pointman (Scout)"),
            ("Szturmowiec", "Rifleman (Assault)"),
            ("Strzelec wyborowy (DMR)", "Designated Marksman (DMR)"),
            ("Snajper", "Sniper"),
            ("Wsparcie (KM)", "Support (LMG)"),
            ("Brak", "None"),
            ("Sytuacyjne", "Situational"),
            ("Przechodnie", "Rotational"),

            // Opisy ról w Załączniku 1
            ("Dowodzi zespołem w trakcie działań, podejmuje kluczowe decyzje, odpowiada za wykonanie zadań.", "Commands the squad during operations, makes key tactical decisions, responsible for mission accomplishment."),
            ("Nadzoruje mapę, informacje o POI, wyznacza trasy, nadzoruje kierunek marszu.", "Monitors the map and POI data, plots routes, and maintains marching azimuth."),
            ("Odpowiada za kontakt ze sztabem, odbieranie rozkazów i podawanie kluczowych informacji, zna szyfrowanie.", "Maintains comms with HQ, receives orders, relays key sitreps, handles SARNEG encryption."),
            ("Odpowiada za sprawy medyczne: leczenie, opatrunki, ilość dostępnych respów, karty ran itp.", "Handles squad medical procedures: bandages, respawn counters, and MEDEVAC rules."),
            ("Szczegółowo analizuje treść zadań oraz dodatkowych informacji, spisuje kluczowe dane i punkty, przekazuje dowódcy.", "Analyzes mission briefs and intel, logs key points and time windows, advises the squad leader."),
            ("Osoba do larpowania, kontaktu z NPC oraz innymi osobami larpującymi, znająca fabułę gry.", "Handles in-character roleplay, interacts with NPCs, and tracks game lore."),
            ("Osoba odpowiedzialna za dokumentowanie działań (np. zdjęcia celów, kodów), wypełnianie dokumentów dot. misji.", "Documents mission objectives (photos of targets/codes) and completes field paperwork."),
            ("Osoba odpowiedzialna za przedmioty wymagane do zadań: transport i przygotowanie przedmiotów fabularnych.", "Carries, secures, and prepares mission props and special equipment."),
            ("Odpowiada za ustanawianie obrony w trakcie postoju: rozstawienie na sektory, wysunięcie czujki, obstawianie wzgórz.", "Establishes 360° perimeter defense during halts, assigns sectors and outposts."),
            ("Przewodzi podczas marszu, koryguje trasę, szuka przejścia, wypatruje zagrożenia z przodu.", "Leads the formation on the move, picks safe terrain paths, and spots forward threats."),

            // Domyślny sprzęt w Tabeli Składu
            ("Karabinek szturmowy + ATAK", "Assault Rifle + ATAK"),
            ("Karabinek szturmowy + Garmin/ATAK", "Assault Rifle + Garmin/ATAK"),
            ("Karabinek szturmowy + Radio DualBand + LoRa", "Assault Rifle + DualBand Radio + LoRa"),
            ("DMR / Wsparcie + Pakiet Medyczny", "DMR / Support + Medic Kit"),
            ("Karabinek szturmowy + Radio", "Assault Rifle + Radio"),

            // Domyślne teksty sekcji OPORD
            ("JAWNE / UNCLASSIFIED — MILSIM DRUŻYNOWY OPORD", "UNCLASSIFIED / JAWNE — MILSIM SQUAD OPORD"),
            ("TOC / SZTAB DRUŻYNY", "TOC / SQUAD HQ"),
            ("Mapa Taktyczna MGRS 1:5000 (W załączeniu .FTAK / .KMZ / .GPX)", "1:5000 MGRS Tactical Map (Attached in .FTAK / .KMZ / .GPX)"),
            ("Brak pobranego raportu (kliknij przycisk METOC dla tego dnia)", "No METOC report fetched yet (click METOC button for this day)"),
            ("• 08:30 — Odprawa sztabowa i sprawdzenie łączności\n• 09:00 — Wejście w rejon działań\n• 14:00 — Rotacja i realizacja zadań głównych", "• 08:30 — HQ briefing and radio check\n• 09:00 — Infiltration into AO\n• 14:00 — Rotation and primary mission execution"),
            ("Punkty kluczowe (FOB, Respawn, OBJ ALPHA, LZ) zdefiniowane w załączonym pliku .KMZ (ATAK) oraz .GPX (Garmin/Suunto).", "Key waypoints (FOB, Respawn, OBJ ALPHA, LZ) defined in the attached .KMZ (ATAK) and .GPX (Garmin/Suunto) files."),
            ("Wielkość AO: ok. 2x2 km. Rodzaj: las mieszany, polany, zabudowania leśne. Ukształtowanie: pagórkowate ze skarpami.", "AO Size: approx. 2x2 km. Terrain: mixed forest, clearings, forest structures. Relief: hilly with ridges."),
            ("1. Skład, dyspozycja i siła: Pluton lekki przeciwnika podzielony na patrole 4-6 osobowe.\n2. Lokalizacje i możliwości: Posterunki na skrzyżowaniach dróg.\n3. Kursy działań wroga (COA): Obrona rejonu centralnego i zasadzki.", "1. Composition & strength: Enemy light platoon split into 4-6 man patrols.\n2. Locations & capabilities: Outposts at road intersections.\n3. Enemy COA: Defense of central sector and ambushes."),
            ("1. Misja i zamiar wyższego dowództwa (HQ): Opanowanie i kontrola sektora przed zmrokiem.\n2. Misja sąsiednich jednostek: Wsparcie na skrzydłach.", "1. Higher HQ mission & intent: Secure and control the sector before nightfall.\n2. Adjacent units mission: Flank support."),
            ("Opis fabularny (opcjonalnie): Działania rozpoznawczo-dywersyjne na zapleczu ugrupowania przeciwnika.", "Story / Lore (optional): Reconnaissance and sabotage operations behind enemy lines."),
            ("zlokalizuje i zabezpieczy kody sektorowe oraz utrzyma obiekt OBJ ALPHA", "will locate and secure sector codes and hold OBJ ALPHA"),
            ("w godzinach operacyjnych 09:00 - 17:00", "during operational hours 09:00 - 17:00"),
            ("w wyznaczonym obszarze AO (siatka MGRS)", "within the designated AO (MGRS grid)"),
            ("w celu uzyskania przewagi punktowej i odcięcia zaopatrzenia przeciwnika.", "in order to gain tactical score superiority and cut off enemy supply lines."),
            ("• Zadanie poboczne 1: Rozpoznanie stanowisk dowodzenia wroga (Foto).\n• Zadanie poboczne 2: Przejęcie pojemnika fabularnego (szczegóły w Załączniku 2).", "• Secondary Task 1: Photographic recon of enemy command posts (Photo).\n• Secondary Task 2: Secure mission container (details in Annex 2)."),
            ("Jedziemy działać zespołowo, przećwiczyć nawigację MGRS/ATAK i zrealizować 100% zadań głównych bez zbędnych strat.", "Operate as a cohesive squad, practice MGRS/ATAK navigation, and accomplish 100% of primary objectives with minimal casualties."),
            ("Priorytety i styl działania: Skryte przemieszczanie czujką z przodu. Unikamy niepotrzebnego kontaktu ogniowego podczas marszu do celu.", "Priorities & style of action: Stealth movement with pointman ahead. Avoid unnecessary firefights while moving to the objective."),
            ("Dowódca (Lider)  ->  1. Zastępca (Navi / RTO)  ->  2. Zastępca (Dowódca sekcji)", "Squad Leader  ->  1st Deputy (Navi / RTO)  ->  2nd Deputy (Fireteam Leader)"),
            ("• OBJ ALPHA = 'ZAMEK'   |   • Punkt Zbiórki (RP) = 'KUŹNIA'   |   • FOB / Respawn = 'BAZA'", "• OBJ ALPHA = 'CASTLE'   |   • Rally Point (RP) = 'FORGE'   |   • FOB / Respawn = 'BASE'"),
            ("a) Werbalne (Wywołanie - Odpowiedź - Potwierdzenie): GROM - BŁYSK - TARCZA (Suma cyfr: 7)\nb) Niewerbalne (Wywołanie - Odzew): 2 błyski czerwonym światłem - 1 błysk + podniesienie lewej ręki", "a) Verbal (Challenge - Reply - Confirm): THUNDER - FLASH - SHIELD (Running number: 7)\nb) Non-verbal (Challenge - Reply): 2 red light flashes - 1 flash + raised left hand"),
            ("Pierwsze trafienie: opatrunek przez medyka lub członka drużyny (czas 2 min). Drugie trafienie: odejście na Respawn. W razie wypadku: przycisk 🚨 SOS MGRS w Field TAK Hub.", "First hit: bandage applied by medic or teammate (2 min). Second hit: bleed out to Respawn. Real emergency: use 🚨 SOS MGRS button in Field TAK Hub."),
            ("Styl wyposażenia: Na 12h w terenie (woda min. 2L, racja żywnościowa, zapasowe baterie do radia i powerbank do ATAK).", "Loadout style: 12h self-sustained field gear (min. 2L water, field ration, spare radio batteries, powerbank for ATAK)."),
            ("SZPEJLISTA: Okulary ochronne, czerwona szmatka (dead-rag), latarka czerwona, radio PMR/VHF, telefon z Field TAK Hub & ATAK-CIV, kompas.", "GEAR CHECKLIST: Eyepro, red dead-rag, red flashlight, PMR/VHF radio, phone with Field TAK Hub & ATAK-CIV, compass."),
            ("Wyposażenie specjalne: Rekwizyty misyjne ze sztabu, znaczniki chemiczne (lightstick), aparat/telefon dla Fotografa.", "Special equipment: HQ mission props, chemlights, camera/phone for Photographer."),
            ("1. Parking: Zgodnie z pinezką zbiórki w paczce .GPX / .KMZ.\n2. Odległość z parkingu na miejsce gry: ok. 300 m.\n3. Transport: Auto 1, Auto 2.", "1. Parking: At the rally pin in the .GPX / .KMZ package.\n2. Distance from parking to AO: approx. 300 m.\n3. Vehicles: Car 1, Car 2."),
            ("ZAŁĄCZNIK 2 — OPRACOWANIE ZADAŃ:\n• Analiza priorytetów punktowych, okna czasowe na wykonanie misji specjalnych oraz podział zespołów na zadania równoległe.", "ANNEX 2 — TASKS BREAKDOWN:\n• Objective scoring priority analysis, time windows for special missions, and fireteam split for parallel tasks.")
        };

        private static string MapBilingualString(string currentVal, bool toEn)
        {
            if (string.IsNullOrWhiteSpace(currentVal)) return currentVal;
            string trimmed = currentVal.Trim();

            foreach (var pair in OpordBilingualPairs)
            {
                if (toEn && string.Equals(trimmed, pair.Pl, StringComparison.OrdinalIgnoreCase))
                    return pair.En;
                if (!toEn && string.Equals(trimmed, pair.En, StringComparison.OrdinalIgnoreCase))
                    return pair.Pl;
            }

            if (toEn && trimmed.StartsWith("Drużyna ", StringComparison.OrdinalIgnoreCase))
                return "Squad " + trimmed.Substring("Drużyna ".Length);
            if (!toEn && trimmed.StartsWith("Squad ", StringComparison.OrdinalIgnoreCase))
                return "Drużyna " + trimmed.Substring("Squad ".Length);

            if (toEn && Regex.IsMatch(trimmed, @"^Dzień\s+(\d+)$", RegexOptions.IgnoreCase))
                return Regex.Replace(trimmed, @"^Dzień\s+(\d+)$", "Day $1", RegexOptions.IgnoreCase);
            if (!toEn && Regex.IsMatch(trimmed, @"^Day\s+(\d+)$", RegexOptions.IgnoreCase))
                return Regex.Replace(trimmed, @"^Day\s+(\d+)$", "Dzień $1", RegexOptions.IgnoreCase);

            return currentVal;
        }

        public static void TranslateOpordModelToCurrentLanguage(OpordParagraphModel m)
        {
            if (m == null || m.Days == null) return;
            bool toEn = string.Equals(BuilderI18n.CurrentLang, "EN", StringComparison.OrdinalIgnoreCase);

            foreach (var d in m.Days)
            {
                d.DayTitle = MapBilingualString(d.DayTitle, toEn);
                d.Classification = MapBilingualString(d.Classification, toEn);
                d.IssuingHq = MapBilingualString(d.IssuingHq, toEn);
                d.MapReferences = MapBilingualString(d.MapReferences, toEn);
                d.DaySpecificTasks = MapBilingualString(d.DaySpecificTasks, toEn);
                d.AreaOfInterestPoiDesc = MapBilingualString(d.AreaOfInterestPoiDesc, toEn);
                d.TerrainAndWeather = MapBilingualString(d.TerrainAndWeather, toEn);
                d.MetocCondition = MapBilingualString(d.MetocCondition, toEn);
                d.EnemyForces = MapBilingualString(d.EnemyForces, toEn);
                d.FriendlyForces = MapBilingualString(d.FriendlyForces, toEn);
                d.MissionLoreDesc = MapBilingualString(d.MissionLoreDesc, toEn);
                d.MissionWho = MapBilingualString(d.MissionWho, toEn);
                d.MissionWhat = MapBilingualString(d.MissionWhat, toEn);
                d.MissionWhen = MapBilingualString(d.MissionWhen, toEn);
                d.MissionWhere = MapBilingualString(d.MissionWhere, toEn);
                d.MissionWhy = MapBilingualString(d.MissionWhy, toEn);
                d.MissionFullStatement = Compose5WMissionTextForDay(d);
                d.AdditionalTasksSummary = MapBilingualString(d.AdditionalTasksSummary, toEn);
                d.CommandersIntent = MapBilingualString(d.CommandersIntent, toEn);
                d.ConceptOfOperations = MapBilingualString(d.ConceptOfOperations, toEn);
                d.SuccessionOfCommand = MapBilingualString(d.SuccessionOfCommand, toEn);
                d.TargetCodeNames = MapBilingualString(d.TargetCodeNames, toEn);
                d.ChallengeAndPassword = MapBilingualString(d.ChallengeAndPassword, toEn);
                d.MedevacRules = MapBilingualString(d.MedevacRules, toEn);
                d.RespawnAndSupply = MapBilingualString(d.RespawnAndSupply, toEn);
                d.SquadGearChecklist = MapBilingualString(d.SquadGearChecklist, toEn);
                d.SpecialEquipment = MapBilingualString(d.SpecialEquipment, toEn);
                d.TravelAndParking = MapBilingualString(d.TravelAndParking, toEn);
                d.Annex2TasksAnalysis = MapBilingualString(d.Annex2TasksAnalysis, toEn);

                if (d.SquadRoster != null)
                {
                    foreach (var r in d.SquadRoster)
                    {
                        r.PrimaryRole = MapBilingualString(r.PrimaryRole, toEn);
                        r.SecondaryRole = MapBilingualString(r.SecondaryRole, toEn);
                        r.WeaponAndGear = MapBilingualString(r.WeaponAndGear, toEn);
                    }
                }

                if (d.CustomRolesAnnex != null)
                {
                    foreach (var rd in d.CustomRolesAnnex)
                    {
                        rd.RoleName = MapBilingualString(rd.RoleName, toEn);
                        rd.Description = MapBilingualString(rd.Description, toEn);
                        rd.DutyMode = MapBilingualString(rd.DutyMode, toEn);
                    }
                }
            }
        }

        public static OpordParagraphModel GetOrCreateModel(string factionName)
        {
            string key = string.IsNullOrWhiteSpace(factionName) ? "BLUFOR" : factionName.Trim();
            if (!ModelsByFaction.TryGetValue(key, out var model))
            {
                model = TryLoadSavedOpordFromDisk(key) ?? new OpordParagraphModel
                {
                    TargetFaction = key,
                    MissionWho = "Drużyna " + key
                };
                EnsureDefaultCollectionsInitialized(model);
                ModelsByFaction[key] = model;
            }
            else
            {
                EnsureDefaultCollectionsInitialized(model);
            }
            return model;
        }

        private static void EnsureDefaultCollectionsInitialized(OpordParagraphModel m)
        {
            if (m.Days == null || m.Days.Count == 0)
            {
                var firstDay = new OpordDayPlanModel
                {
                    DayTitle = "Dzień 1",
                    IncludeInExport = true,
                    DateIso = string.IsNullOrWhiteSpace(m.OperationDateIso)
                        ? DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                        : m.OperationDateIso,
                    MissionWho = string.IsNullOrWhiteSpace(m.MissionWho) ? ("Drużyna " + m.TargetFaction) : m.MissionWho,
                    SarnegKeyWord10 = string.IsNullOrWhiteSpace(m.SarnegKeyWord10) ? SarnegDictionaryService.DrawRandomSarnegWord() : m.SarnegKeyWord10
                };
                EnsureDayDefaultsInitialized(firstDay, m.TargetFaction);
                m.Days = new List<OpordDayPlanModel> { firstDay };
                m.ActiveDayIndex = 0;
            }
            else
            {
                foreach (var d in m.Days)
                {
                    EnsureDayDefaultsInitialized(d, m.TargetFaction);
                }
            }
        }

        public static void EnsureDayDefaultsInitialized(OpordDayPlanModel d, string factionName)
        {
            if (d == null) return;

            if (string.IsNullOrWhiteSpace(d.MissionWho))
                d.MissionWho = "Drużyna " + (string.IsNullOrWhiteSpace(factionName) ? "BLUFOR" : factionName);

            if (string.IsNullOrWhiteSpace(d.MissionFullStatement))
                d.MissionFullStatement = Compose5WMissionTextForDay(d);

            if (string.IsNullOrWhiteSpace(d.SarnegKeyWord10))
                d.SarnegKeyWord10 = SarnegDictionaryService.DrawRandomSarnegWord();

            if (d.SquadRoster == null || d.SquadRoster.Count == 0)
            {
                d.SquadRoster = new List<OpordRosterRow>
                {
                    new OpordRosterRow { OperatorOrTeam = "ALFA-1", PrimaryRole = "Lider (Dowódca polowy)", SecondaryRole = "Adiutant", WeaponAndGear = "Karabinek szturmowy + ATAK" },
                    new OpordRosterRow { OperatorOrTeam = "ALFA-2", PrimaryRole = "Navi (Nawigator)", SecondaryRole = "Czujka", WeaponAndGear = "Karabinek szturmowy + Garmin/ATAK" },
                    new OpordRosterRow { OperatorOrTeam = "ALFA-3", PrimaryRole = "RTO (Radiooperator)", SecondaryRole = "Foto (Fotograf)", WeaponAndGear = "Karabinek szturmowy + Radio DualBand + LoRa" },
                    new OpordRosterRow { OperatorOrTeam = "ALFA-4", PrimaryRole = "Med (Medyk)", SecondaryRole = "O-O (Organizator Obrony)", WeaponAndGear = "DMR / Wsparcie + Pakiet Medyczny" }
                };
            }

            if (d.CustomRolesAnnex == null || d.CustomRolesAnnex.Count == 0)
            {
                d.CustomRolesAnnex = DefaultMilsimRolesCatalog.Select(r => new OpordRoleDefinitionRow
                {
                    RoleName = r.RoleName,
                    DutyMode = r.Mode,
                    Description = r.Description
                }).ToList();
            }

            if (d.CodewordsTable == null || d.CodewordsTable.Count == 0)
            {
                d.CodewordsTable = new List<OpordCodewordRow>
                {
                    new OpordCodewordRow { CodeWord = "KONTAKT / WRÓG", CodeLetter = "K", CodeDigit = "1", DuressWord = "CZERWONY" },
                    new OpordCodewordRow { CodeWord = "ODWRÓT / ZERWANIE", CodeLetter = "Z", CodeDigit = "2", DuressWord = "MGŁA" },
                    new OpordCodewordRow { CodeWord = "ZBIÓRKA NA FOB", CodeLetter = "B", CodeDigit = "5", DuressWord = "BURZA" }
                };
            }
        }

        // Tworzy całkowicie niezależną instancję kolejnego dnia (z nowym słowem SARNEG i głęboką kopią tabel)
        public static OpordDayPlanModel CreateIndependentNextDay(OpordDayPlanModel sourceDay, int nextDayNumber, string factionName)
        {
            DateTime nextDate = DateTime.Today.AddDays(nextDayNumber - 1);
            if (sourceDay != null && DateTime.TryParseExact(sourceDay.DateIso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
            {
                nextDate = parsed.AddDays(1);
            }

            string newSarneg = SarnegDictionaryService.DrawRandomSarnegWord(sourceDay?.SarnegKeyWord10);

            var nextDay = new OpordDayPlanModel
            {
                DayTitle = BuilderI18n.T("Dzień " + nextDayNumber, "Day " + nextDayNumber),
                IncludeInExport = true,
                DateIso = nextDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                IsSecretClassification = sourceDay?.IsSecretClassification ?? false,
                Classification = sourceDay?.Classification ?? "JAWNE / UNCLASSIFIED — MILSIM DRUŻYNOWY OPORD",
                OperationNumber = sourceDay?.OperationNumber ?? ("OPORD 0" + nextDayNumber + "-2026"),
                DtgIssued = DateTime.Now.ToString("ddHHmm'Z' MMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant(),
                IssuingHq = sourceDay?.IssuingHq ?? "TOC / SZTAB DRUŻYNY",
                MapReferences = sourceDay?.MapReferences ?? "Mapa Taktyczna MGRS 1:5000 (W załączeniu .FTAK / .KMZ / .GPX)",
                DaySpecificTasks = BuilderI18n.T(
                    "• 08:30 — Odprawa sztabowa na Dzień " + nextDayNumber + "\n• 09:00 — Wyjście w rejon działań (Dzień " + nextDayNumber + ")\n• 14:00 — Realizacja zadań głównych Dnia " + nextDayNumber,
                    "• 08:30 — HQ Briefing for Day " + nextDayNumber + "\n• 09:00 — Deploy to AO (Day " + nextDayNumber + ")\n• 14:00 — Primary tasks for Day " + nextDayNumber),
                LocationName = sourceDay?.LocationName ?? "",
                LocationLat = sourceDay?.LocationLat ?? "49.8494",
                LocationLon = sourceDay?.LocationLon ?? "18.8127",
                AreaOfInterestPoiDesc = sourceDay?.AreaOfInterestPoiDesc ?? "Punkty kluczowe (FOB, Respawn, OBJ ALPHA, LZ) zdefiniowane w załączonym pliku .KMZ (ATAK) oraz .GPX (Garmin/Suunto).",
                TerrainAndWeather = BuilderI18n.T(
                    "Analiza terenu i pogody dla Dnia " + nextDayNumber + " (kliknij przycisk METOC, aby pobrać prognozę na ten dzień).",
                    "Terrain & weather analysis for Day " + nextDayNumber + " (click METOC button to fetch forecast for this day)."),
                MetocCondition = BuilderI18n.T("Brak pobranego raportu dla Dnia " + nextDayNumber, "No METOC report fetched for Day " + nextDayNumber),
                EnemyForces = sourceDay?.EnemyForces ?? "1. Skład, dyspozycja i siła: Patrole przeciwnika w sektorze.\n2. Kursy działań wroga (COA): Kontratak i zasadzki.",
                FriendlyForces = sourceDay?.FriendlyForces ?? "1. Misja wyższego dowództwa (HQ): Kontrola sektora w Dniu " + nextDayNumber + ".",
                MissionLoreDesc = sourceDay?.MissionLoreDesc ?? "",
                MissionWho = sourceDay?.MissionWho ?? ("Drużyna " + factionName),
                MissionWhat = BuilderI18n.T("zrealizuje zadania operacyjne wyznaczone na Dzień " + nextDayNumber, "will execute operational tasks assigned for Day " + nextDayNumber),
                MissionWhen = BuilderI18n.T("w godzinach operacyjnych Dnia " + nextDayNumber, "during operational hours of Day " + nextDayNumber),
                MissionWhere = sourceDay?.MissionWhere ?? "w wyznaczonym obszarze AO (siatka MGRS)",
                MissionWhy = sourceDay?.MissionWhy ?? "w celu utrzymania inicjatywy taktycznej.",
                AdditionalTasksSummary = BuilderI18n.T(
                    "• Zadania dodatkowe wyznaczone na Dzień " + nextDayNumber + "...",
                    "• Additional tasks assigned for Day " + nextDayNumber + "..."),
                CommandersIntent = sourceDay?.CommandersIntent ?? "Skuteczna realizacja zadań Dnia " + nextDayNumber + " z zachowaniem pełnej łączności.",
                ConceptOfOperations = sourceDay?.ConceptOfOperations ?? "Priorytety i styl działania na Dzień " + nextDayNumber + ".",
                SuccessionOfCommand = sourceDay?.SuccessionOfCommand ?? "Dowódca (Lider)  ->  1. Zastępca (Navi / RTO)  ->  2. Zastępca (Dowódca sekcji)",
                EnableSquadRosterTable = sourceDay?.EnableSquadRosterTable ?? true,
                SquadRoster = sourceDay?.SquadRoster != null
                    ? sourceDay.SquadRoster.Select(r => new OpordRosterRow
                    {
                        OperatorOrTeam = r.OperatorOrTeam,
                        PrimaryRole = r.PrimaryRole,
                        SecondaryRole = r.SecondaryRole,
                        WeaponAndGear = r.WeaponAndGear
                    }).ToList()
                    : new List<OpordRosterRow>(),
                PrimaryRadioFreq = sourceDay?.PrimaryRadioFreq ?? "446.00625 MHz (PMR CH 1)",
                BackupRadioFreq = sourceDay?.BackupRadioFreq ?? "446.03125 MHz (PMR CH 3)",
                HqRadioFreq = sourceDay?.HqRadioFreq ?? "446.09375 MHz (PMR CH 8)",
                TargetCodeNames = BuilderI18n.T(
                    "• Kryptonimy celów na Dzień " + nextDayNumber + ": OBJ ALPHA = 'ZAMEK-" + nextDayNumber + "' | FOB = 'BAZA-" + nextDayNumber + "'",
                    "• Target Code Names for Day " + nextDayNumber + ": OBJ ALPHA = 'CASTLE-" + nextDayNumber + "' | FOB = 'BASE-" + nextDayNumber + "'"),
                SarnegKeyWord10 = newSarneg,
                ChallengeAndPassword = BuilderI18n.T(
                    "a) Werbalne (Dzień " + nextDayNumber + " — Wywołanie - Odpowiedź - Potwierdzenie): WILK - KIEŁ - WATAHA\nb) Niewerbalne: 2 błyski czerwonym światłem - 1 błysk",
                    "a) Verbal (Day " + nextDayNumber + " — Challenge - Reply - Confirm): WOLF - FANG - PACK\nb) Non-verbal: 2 red flashes - 1 flash"),
                MedevacRules = sourceDay?.MedevacRules ?? "Pierwsze trafienie: opatrunek (2 min). Drugie trafienie: odejście na Respawn.",
                RespawnAndSupply = sourceDay?.RespawnAndSupply ?? "Styl wyposażenia: Na 12h w terenie.",
                SquadGearChecklist = sourceDay?.SquadGearChecklist ?? "SZPEJLISTA: Okulary ochronne, czerwona szmatka (dead-rag), latarka czerwona, radio PMR/VHF, telefon z Field TAK Hub & ATAK-CIV.",
                SpecialEquipment = sourceDay?.SpecialEquipment ?? "Wyposażenie specjalne na Dzień " + nextDayNumber + ".",
                TravelAndParking = sourceDay?.TravelAndParking ?? "1. Parking: Zgodnie z pinezką zbiórki w paczce .GPX / .KMZ.",
                GameRulesLink = sourceDay?.GameRulesLink ?? "https://",
                EnableRolesAnnex = sourceDay?.EnableRolesAnnex ?? false,
                CustomRolesAnnex = sourceDay?.CustomRolesAnnex != null
                    ? sourceDay.CustomRolesAnnex.Select(rd => new OpordRoleDefinitionRow
                    {
                        RoleName = rd.RoleName,
                        Description = rd.Description,
                        DutyMode = rd.DutyMode
                    }).ToList()
                    : new List<OpordRoleDefinitionRow>(),
                Annex2TasksAnalysis = BuilderI18n.T(
                    "ZAŁĄCZNIK 2 — OPRACOWANIE ZADAŃ (" + "DZIEŃ " + nextDayNumber + "):\n• Analiza priorytetów i podział zespołów na zadania w Dniu " + nextDayNumber + ".",
                    "ANNEX 2 — TASKS BREAKDOWN (DAY " + nextDayNumber + "):\n• Priority analysis and team assignments for Day " + nextDayNumber + ".")
            };

            nextDay.MissionFullStatement = Compose5WMissionTextForDay(nextDay);
            EnsureDayDefaultsInitialized(nextDay, factionName);
            return nextDay;
        }

        public static string Compose5WMissionTextForDay(OpordDayPlanModel d)
        {
            if (d == null) return "";
            if (BuilderI18n.CurrentLang == "EN")
            {
                return (d.MissionWho + " will " + d.MissionWhat + " " + d.MissionWhen + " at " + d.MissionWhere + " in order to " + d.MissionWhy).Trim();
            }
            return (d.MissionWho + " " + d.MissionWhat + " " + d.MissionWhen + " w rejonie " + d.MissionWhere + ", " + d.MissionWhy).Trim();
        }

        public static string Compose5WMissionText(OpordParagraphModel m)
        {
            EnsureDefaultCollectionsInitialized(m);
            var activeDay = m.Days[Math.Clamp(m.ActiveDayIndex, 0, m.Days.Count - 1)];
            return Compose5WMissionTextForDay(activeDay);
        }

        public static void SaveAllOpordsToDisk(Window win)
        {
            try
            {
                string sourceDir = TacticalConfigPanel.GetPrimarySourceDirectory(win);
                foreach (var kv in ModelsByFaction)
                {
                    string facSlug = TacticalConfigPanel.SanitizeFileName(kv.Key).ToLowerInvariant();
                    string facDir = Path.Combine(sourceDir, "factions", facSlug);
                    Directory.CreateDirectory(facDir);

                    string json = JsonSerializer.Serialize(kv.Value, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(Path.Combine(facDir, "opord_" + facSlug + ".json"), json, Encoding.UTF8);

                    string html = GeneratePrintableOpordHtml(win, kv.Key, kv.Value);
                    File.WriteAllText(Path.Combine(facDir, "OPORD_" + facSlug.ToUpperInvariant() + ".html"), html, Encoding.UTF8);
                }
            }
            catch { }
        }

        private static OpordParagraphModel TryLoadSavedOpordFromDisk(string factionName)
        {
            try
            {
                string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FieldTakHub");
                string facSlug = TacticalConfigPanel.SanitizeFileName(factionName).ToLowerInvariant();
                string p = Path.Combine(appData, "opord_" + facSlug + ".json");
                if (File.Exists(p))
                {
                    return JsonSerializer.Deserialize<OpordParagraphModel>(File.ReadAllText(p, Encoding.UTF8));
                }
            }
            catch { }
            return null;
        }

        private static void PersistModelToAppData(OpordParagraphModel m)
        {
            try
            {
                string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FieldTakHub");
                Directory.CreateDirectory(appData);
                string facSlug = TacticalConfigPanel.SanitizeFileName(m.TargetFaction).ToLowerInvariant();
                string p = Path.Combine(appData, "opord_" + facSlug + ".json");
                File.WriteAllText(p, JsonSerializer.Serialize(m, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
            }
            catch { }
        }

        public static JsonObject TryGetFactionJsonObject(Window win, string factionName)
        {
            try
            {
                if (win != null)
                {
                    try { TacticalConfigPanel.SaveSessionStateAuto(win); } catch { }
                }

                var candidatePaths = new List<string>();
                if (!string.IsNullOrWhiteSpace(TacticalConfigPanel.CurrentDraftProjectPath) && File.Exists(TacticalConfigPanel.CurrentDraftProjectPath))
                {
                    candidatePaths.Add(TacticalConfigPanel.CurrentDraftProjectPath);
                }
                if (!string.IsNullOrWhiteSpace(BuilderCoreSidebarAndProjectEngine.CurrentProjectFilePath) && File.Exists(BuilderCoreSidebarAndProjectEngine.CurrentProjectFilePath))
                {
                    candidatePaths.Add(BuilderCoreSidebarAndProjectEngine.CurrentProjectFilePath);
                }

                string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FieldTakHub");
                if (Directory.Exists(appData))
                {
                    var recentFiles = Directory.GetFiles(appData, "*.*", SearchOption.TopDirectoryOnly)
                        .Where(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".fthproj", StringComparison.OrdinalIgnoreCase))
                        .Where(f => !Path.GetFileName(f).StartsWith("opord_", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(File.GetLastWriteTimeUtc);
                    candidatePaths.AddRange(recentFiles);
                }

                foreach (string path in candidatePaths.Distinct())
                {
                    try
                    {
                        string raw = File.ReadAllText(path, Encoding.UTF8);
                        if (JsonNode.Parse(raw) is JsonObject root)
                        {
                            var fArr = (root["factions"] ?? root["Factions"]) as JsonArray;
                            if (fArr != null && fArr.Count > 0)
                            {
                                foreach (var item in fArr.OfType<JsonObject>())
                                {
                                    string n = item["Name"]?.ToString() ?? item["name"]?.ToString() ?? "";
                                    if (n.Equals(factionName, StringComparison.OrdinalIgnoreCase))
                                        return item;
                                }
                                return fArr.OfType<JsonObject>().FirstOrDefault();
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }

        public static string EnsureOpordLanServerStarted(Window win)
        {
            _lastActiveWindowRef = win;
            if (!_opordLanServerRunning || _opordLanListener == null)
            {
                foreach (int port in new[] { 8769, 8770, 8771 })
                {
                    try
                    {
                        var listener = new TcpListener(IPAddress.Any, port);
                        listener.Start();
                        _opordLanListener = listener;
                        _opordLanPort = port;
                        _opordLanServerRunning = true;
                        _ = Task.Run(() => AcceptOpordLanClientsLoop(listener));
                        break;
                    }
                    catch { }
                }
            }

            string ip = GetLocalLanIp();
            return "http://" + ip + ":" + _opordLanPort;
        }

        private static async Task AcceptOpordLanClientsLoop(TcpListener listener)
        {
            while (_opordLanServerRunning)
            {
                try
                {
                    var client = await listener.AcceptTcpClientAsync();
                    _ = Task.Run(() => HandleOpordLanHttpClient(client));
                }
                catch
                {
                    if (!_opordLanServerRunning) break;
                }
            }
        }

        private static void HandleOpordLanHttpClient(TcpClient client)
        {
            using (client)
            {
                try
                {
                    using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true);
                    string reqLine = reader.ReadLine();
                    if (string.IsNullOrEmpty(reqLine)) return;

                    string[] parts = reqLine.Split(' ');
                    string path = parts.Length >= 2 ? Uri.UnescapeDataString(parts[1]) : "/";

                    string facName = _activeOpordFaction;
                    foreach (var kv in ModelsByFaction)
                    {
                        if (path.Contains(kv.Key, StringComparison.OrdinalIgnoreCase))
                        {
                            facName = kv.Key;
                            break;
                        }
                    }

                    var model = GetOrCreateModel(facName);
                    bool wantsJson = path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

                    byte[] bodyBytes;
                    string contentType;
                    if (wantsJson)
                    {
                        string json = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
                        bodyBytes = Encoding.UTF8.GetBytes(json);
                        contentType = "application/json; charset=utf-8";
                    }
                    else
                    {
                        string html = GeneratePrintableOpordHtml(_lastActiveWindowRef, facName, model);
                        bodyBytes = Encoding.UTF8.GetBytes(html);
                        contentType = "text/html; charset=utf-8";
                    }

                    string headers =
                        "HTTP/1.1 200 OK\r\n" +
                        "Content-Type: " + contentType + "\r\n" +
                        "Content-Length: " + bodyBytes.Length + "\r\n" +
                        "Connection: close\r\n\r\n";
                    byte[] hdrBytes = Encoding.UTF8.GetBytes(headers);
                    stream.Write(hdrBytes, 0, hdrBytes.Length);
                    stream.Write(bodyBytes, 0, bodyBytes.Length);
                    stream.Flush();
                }
                catch { }
            }
        }

        private static string GetLocalLanIp()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    {
                        string s = ip.ToString();
                        if (s.StartsWith("192.168.") || s.StartsWith("10.") || s.StartsWith("172."))
                            return s;
                    }
                }
            }
            catch { }
            return "127.0.0.1";
        }

        public static List<OpordDayPlanModel> GetSelectedDaysForExport(OpordParagraphModel m)
        {
            EnsureDefaultCollectionsInitialized(m);
            var selected = m.Days.Where(d => d.IncludeInExport).ToList();
            if (selected.Count == 0 && m.Days.Count > 0)
            {
                selected.Add(m.Days[Math.Clamp(m.ActiveDayIndex, 0, m.Days.Count - 1)]);
            }
            return selected;
        }

        // Generuje kompletny wydruk A4 HTML, w którym każdy zaznaczony dzień ma własną Sytuację, Misję, Skład, SARNEG, Hasła i Logistykę!
        public static string GeneratePrintableOpordHtml(Window win, string factionName, OpordParagraphModel m)
        {
            EnsureDefaultCollectionsInitialized(m);
            var exportedDays = GetSelectedDaysForExport(m);

            string eventName = win != null ? TacticalConfigPanel.GetEventNameFromWindow(win) : "OPERACJA";
            var facJson = TryGetFactionJsonObject(win, factionName);
            string colorHex = facJson?["ColorHex"]?.ToString() ?? "#2684FF";
            string chName = facJson?["ChannelName"]?.ToString() ?? (factionName + "-MESH");
            string region = facJson?["LoraRegion"]?.ToString() ?? "EU_868";
            string preset = facJson?["LoraModemPreset"]?.ToString() ?? "SHORT_FAST";
            string ots = facJson?["OtsHost"]?.ToString() ?? "Off-Grid Mesh";
            string phone = facJson?["EmergencyPhone"]?.ToString() ?? "112";

            string E(string s) => WebUtility.HtmlEncode(s ?? "").Replace("\n", "<br/>");

            var html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html><html lang='pl'><head><meta charset='utf-8'/>");
            html.AppendLine("<title>OPORD " + E(eventName) + " (" + E(factionName) + ")</title>");
            html.AppendLine("<style>");
            html.AppendLine("@page { size: A4 portrait; margin: 10mm; }");
            html.AppendLine("body { font-family: 'Segoe UI', Arial, sans-serif; color: #111; background: #fff; margin: 0; padding: 14px; font-size: 11.5px; line-height: 1.4; }");
            html.AppendLine(".header-box { border: 2px solid #111; padding: 10px 14px; margin-bottom: 12px; display: flex; justify-content: space-between; align-items: center; }");
            html.AppendLine(".faction-badge { background: " + colorHex + "; color: #fff; font-weight: bold; padding: 6px 14px; border-radius: 4px; font-size: 15px; }");
            html.AppendLine("h1 { margin: 0 0 4px 0; font-size: 17px; text-transform: uppercase; }");
            html.AppendLine("h2 { background: #102229; color: #fff; padding: 5px 10px; font-size: 12.5px; margin: 12px 0 6px 0; text-transform: uppercase; }");
            html.AppendLine(".sub-p { margin: 4px 0 6px 8px; }");
            html.AppendLine(".mission-box { border: 2px solid " + colorHex + "; background: #f0f7ff; padding: 8px 12px; font-weight: bold; font-size: 12.5px; margin: 6px 0; }");
            html.AppendLine("table { width: 100%; border-collapse: collapse; margin-top: 5px; margin-bottom: 8px; font-size: 11px; }");
            html.AppendLine("th, td { border: 1px solid #333; padding: 4px 6px; text-align: left; }");
            html.AppendLine("th { background: #e6ecef; }");
            html.AppendLine(".day-page-break { page-break-before: always; margin-top: 24px; border-top: 3px dashed #666; padding-top: 16px; }");
            html.AppendLine(".no-print { margin-bottom: 12px; text-align: right; }");
            html.AppendLine(".btn-print { background: #102229; color: #1FE0C5; border: 2px solid #1FE0C5; padding: 8px 16px; font-weight: bold; cursor: pointer; border-radius: 6px; }");
            html.AppendLine("@media print { .no-print { display: none; } .day-page-break { border-top: none; margin-top: 0; padding-top: 0; } }");
            html.AppendLine("</style></head><body>");
            html.AppendLine("<div class='no-print'><button class='btn-print' onclick='window.print()'>" + BuilderI18n.T("🖨️ Drukuj OPORD / Zapisz jako PDF (A4)", "🖨️ Print OPORD / Save as PDF (A4)") + "</button></div>");

            for (int dayIdx = 0; dayIdx < exportedDays.Count; dayIdx++)
            {
                var d = exportedDays[dayIdx];
                EnsureDayDefaultsInitialized(d, factionName);

                if (dayIdx > 0)
                {
                    html.AppendLine("<div class='day-page-break'></div>");
                }

                string classBannerBg = d.IsSecretClassification ? "#ffebee" : "#f4f6f8";
                string classBannerBorder = d.IsSecretClassification ? "#d32f2f" : "#111";
                string classBannerColor = d.IsSecretClassification ? "#b71c1c" : "#111";
                string classStyle = "text-align:center;font-weight:bold;font-size:12px;letter-spacing:1.2px;border-top:2px solid " + classBannerBorder + ";border-bottom:2px solid " + classBannerBorder + ";color:" + classBannerColor + ";padding:4px;margin-bottom:10px;background:" + classBannerBg + ";";

                var radioRowsHtml = new StringBuilder();
                radioRowsHtml.AppendLine("<tr><td><b>" + BuilderI18n.T("Kanał główny (" + d.DayTitle + ")", "Primary Channel (" + d.DayTitle + ")") + "</b></td><td>PMR/VHF</td><td colspan='2'><b>" + E(d.PrimaryRadioFreq) + "</b></td></tr>");
                radioRowsHtml.AppendLine("<tr><td><b>" + BuilderI18n.T("Kanał zapasowy (" + d.DayTitle + ")", "Backup Channel (" + d.DayTitle + ")") + "</b></td><td>PMR/VHF</td><td colspan='2'><b>" + E(d.BackupRadioFreq) + "</b></td></tr>");
                radioRowsHtml.AppendLine("<tr><td><b>" + BuilderI18n.T("Kanał HQ (" + d.DayTitle + ")", "HQ Channel (" + d.DayTitle + ")") + "</b></td><td>PMR/VHF</td><td colspan='2'><b>" + E(d.HqRadioFreq) + "</b></td></tr>");

                var vArr = (facJson?["VoiceChannels"] ?? facJson?["voiceChannels"]) as JsonArray;
                if (vArr != null && vArr.Count > 0)
                {
                    foreach (var vo in vArr.OfType<JsonObject>())
                    {
                        radioRowsHtml.AppendLine(
                            "<tr><td><b>" + E(vo["roleName"]?.ToString() ?? vo["RoleName"]?.ToString()) + "</b></td>" +
                            "<td>" + E(vo["bandType"]?.ToString() ?? vo["BandType"]?.ToString()) + "</td>" +
                            "<td><b>" + E(vo["freqMhz"]?.ToString() ?? vo["FreqMhz"]?.ToString()) + "</b></td>" +
                            "<td>" + E(vo["toneOrCode"]?.ToString() ?? vo["ToneOrCode"]?.ToString()) + "</td></tr>");
                    }
                }

                string sarnegClean = SarnegDictionaryService.NormalizePolishChars(d.SarnegKeyWord10 ?? "PRZEDMIOTY").Trim().PadRight(10, 'X').Substring(0, 10);
                var sarnegLettersRow = new StringBuilder();
                var sarnegDigitsRow = new StringBuilder();
                for (int i = 0; i < 10; i++)
                {
                    sarnegLettersRow.Append("<th style='text-align:center;font-size:13px;'>").Append(sarnegClean[i]).Append("</th>");
                    sarnegDigitsRow.Append("<td style='text-align:center;font-weight:bold;'>").Append(i).Append("</td>");
                }

                html.AppendLine("<div style='" + classStyle + "'>" + E(d.Classification) + " — " + E(d.DayTitle.ToUpperInvariant()) + " (" + E(d.DateIso) + ")</div>");
                html.AppendLine("<div class='header-box'><div>");
                html.AppendLine("<h1>OPORD " + E(eventName) + " — " + E(d.OperationNumber) + " [" + E(d.DayTitle) + " / " + E(d.DateIso) + "]</h1>");
                html.AppendLine("<div><b>" + BuilderI18n.T("Sztab (HQ):", "Issuing HQ:") + "</b> " + E(d.IssuingHq) + " &nbsp;|&nbsp; <b>DTG:</b> " + E(d.DtgIssued) + "</div>");
                html.AppendLine("<div><b>" + BuilderI18n.T("Rejon AO:", "AO Region:") + "</b> " + E(d.LocationName) + " (" + E(d.LocationLat) + "°N, " + E(d.LocationLon) + "°E) &nbsp;|&nbsp; <b>" + BuilderI18n.T("Mapy:", "Maps:") + "</b> " + E(d.MapReferences) + "</div>");
                html.AppendLine("</div><div class='faction-badge'>" + E(factionName) + "<br/><span style='font-size:11px;'>" + E(d.DayTitle) + "</span></div></div>");

                html.AppendLine("<table><thead><tr><th style='width:22%'>" + BuilderI18n.T("📅 Dzień i Data", "📅 Day & Date") + "</th><th style='width:38%'>" + BuilderI18n.T("Pogoda METOC / Słońce / Księżyc", "METOC Weather / Sun / Moon") + "</th><th>" + BuilderI18n.T("Harmonogram dnia", "Day Schedule") + "</th></tr></thead><tbody>");
                html.AppendLine("<tr><td><b>" + E(d.DayTitle) + "</b><br/>" + E(d.DateIso) + "</td>" +
                                "<td>" + E(d.MetocCondition) + "<br/>Temp: " + E(d.MetocTemperature) + "<br/>" + E(d.MetocSunTimes) + "<br/>" + E(d.MetocMoonPhase) + "</td>" +
                                "<td>" + E(d.DaySpecificTasks) + "</td></tr>");
                html.AppendLine("</tbody></table>");

                html.AppendLine("<h2>" + BuilderI18n.T("1. SYTUACJA (" + d.DayTitle.ToUpperInvariant() + ")", "1. SITUATION (" + d.DayTitle.ToUpperInvariant() + ")") + "</h2>");
                html.AppendLine("<div class='sub-p'><b>" + BuilderI18n.T("A. Obszar zainteresowania (Mapa, POI, KML/GPX):", "A. Area of Interest (Map, POI, KML/GPX):") + "</b><br/>" + E(d.AreaOfInterestPoiDesc) + "</div>");
                html.AppendLine("<div class='sub-p'><b>" + BuilderI18n.T("B. Obszar działań (Teren i Pogoda METOC):", "B. Area of Operations (Terrain & METOC):") + "</b><br/>" + E(d.TerrainAndWeather) + "</div>");
                html.AppendLine("<div class='sub-p'><b>" + BuilderI18n.T("C. Siły wroga (Skład, lokalizacje, COA):", "C. Enemy Forces (Composition, locations, COA):") + "</b><br/>" + E(d.EnemyForces) + "</div>");
                html.AppendLine("<div class='sub-p'><b>" + BuilderI18n.T("D. Siły przyjazne (Misja HQ i sąsiadów):", "D. Friendly Forces (HQ & adjacent units):") + "</b><br/>" + E(d.FriendlyForces) + "</div>");

                html.AppendLine("<h2>" + BuilderI18n.T("II. MISJA (" + d.DayTitle.ToUpperInvariant() + ")", "II. MISSION (" + d.DayTitle.ToUpperInvariant() + ")") + "</h2>");
                html.AppendLine("<div class='sub-p'><b>" + BuilderI18n.T("A. Opis fabularny:", "A. Story / Lore:") + "</b> " + E(d.MissionLoreDesc) + "</div>");
                html.AppendLine("<div class='mission-box'>" + BuilderI18n.T("B. Główne zadanie (" + d.DayTitle + "): ", "B. Primary Mission (" + d.DayTitle + "): ") + E(string.IsNullOrWhiteSpace(d.MissionFullStatement) ? Compose5WMissionTextForDay(d) : d.MissionFullStatement) + "</div>");
                html.AppendLine("<div class='sub-p'><b>" + BuilderI18n.T("C. Dodatkowe zadania (" + d.DayTitle + "):", "C. Additional Tasks (" + d.DayTitle + "):") + "</b><br/>" + E(d.AdditionalTasksSummary) + "</div>");

                html.AppendLine("<h2>" + BuilderI18n.T("III. WYKONANIE (" + d.DayTitle.ToUpperInvariant() + ")", "III. EXECUTION (" + d.DayTitle.ToUpperInvariant() + ")") + "</h2>");
                html.AppendLine("<div class='sub-p'><b>" + BuilderI18n.T("A. Zamiar dowódcy:", "A. Commander's Intent:") + "</b><br/>" + E(d.CommandersIntent) + "</div>");
                html.AppendLine("<div class='sub-p'><b>" + BuilderI18n.T("B. Priorytety i styl działania:", "B. Priorities & Style of Action:") + "</b><br/>" + E(d.ConceptOfOperations) + "</div>");
                html.AppendLine("<div class='sub-p'><b>" + BuilderI18n.T("C. Łańcuch dowodzenia:", "C. Chain of Command:") + "</b> <b>" + E(d.SuccessionOfCommand) + "</b></div>");

                if (d.EnableSquadRosterTable && d.SquadRoster.Count > 0)
                {
                    html.AppendLine("<div class='sub-p'><b>" + BuilderI18n.T("D. Skład oddziału i podział ról (" + d.DayTitle + "):", "D. Squad Roster & Roles (" + d.DayTitle + "):") + "</b></div>");
                    html.AppendLine("<table><thead><tr><th>" + BuilderI18n.T("Ksywa (Operator / Zespół)", "Callsign (Operator / Team)") + "</th><th>" + BuilderI18n.T("Rola 1", "Primary Role") + "</th><th>" + BuilderI18n.T("Rola 2 (opcjonalnie)", "Secondary Role") + "</th><th>" + BuilderI18n.T("Typ broni / Sprzęt", "Weapon / Equipment") + "</th></tr></thead><tbody>");
                    foreach (var r in d.SquadRoster)
                    {
                        html.AppendLine("<tr><td><b>" + E(r.OperatorOrTeam) + "</b></td><td>" + E(r.PrimaryRole) + "</td><td>" + E(r.SecondaryRole) + "</td><td>" + E(r.WeaponAndGear) + "</td></tr>");
                    }
                    html.AppendLine("</tbody></table>");
                }

                html.AppendLine("<div class='sub-p'><b>" + BuilderI18n.T("E. Tabela łączności (" + d.DayTitle + ")", "E. Communications Table (" + d.DayTitle + ")") + " (LoRa Meshtastic: " + E(chName) + " [" + E(region) + "/" + E(preset) + "] | OTS: " + E(ots) + " | SOS ORGA: " + E(phone) + "):</b></div>");
                html.AppendLine("<table><thead><tr><th>" + BuilderI18n.T("Rola / Kanał", "Role / Channel") + "</th><th>" + BuilderI18n.T("Pasmo", "Band") + "</th><th>" + BuilderI18n.T("Częstotliwość / Kanał", "Frequency / Channel") + "</th><th>" + BuilderI18n.T("Ton CTCSS / Kod DMR", "CTCSS Tone / DMR Code") + "</th></tr></thead><tbody>" + radioRowsHtml + "</tbody></table>");

                html.AppendLine("<div class='sub-p'><b>" + BuilderI18n.T("F. Sygnały, słowa kodowe i rozpoznawcze (" + d.DayTitle + "):", "F. Signals, Codewords & Challenge (" + d.DayTitle + "):") + "</b><br/><b>" + BuilderI18n.T("1. Kryptonimy celów:", "1. Objective Code Names:") + "</b> " + E(d.TargetCodeNames) + "</div>");
                html.AppendLine("<table><thead><tr><th colspan='10' style='text-align:center;'>" + BuilderI18n.T("2. Tabela krypto SARNEG (" + d.DayTitle + " — Słowo-klucz: ", "2. SARNEG Crypto Table (" + d.DayTitle + " — Keyword: ") + E(sarnegClean) + ")</th></tr><tr>" + sarnegLettersRow + "</tr></thead><tbody><tr>" + sarnegDigitsRow + "</tr></tbody></table>");
                html.AppendLine("<div class='sub-p'><b>" + BuilderI18n.T("3. Hasła rozpoznawcze (" + d.DayTitle + " — werbalne i niewerbalne):", "3. Challenge & Password (" + d.DayTitle + " — verbal & non-verbal):") + "</b><br/>" + E(d.ChallengeAndPassword) + "</div>");
                html.AppendLine("<div class='sub-p'><b>" + BuilderI18n.T("G. Leczenie i zasady Respawn:", "G. Medical & Respawn Rules:") + "</b><br/>" + E(d.MedevacRules) + "</div>");

                html.AppendLine("<h2>" + BuilderI18n.T("IV. LOGISTYKA (" + d.DayTitle.ToUpperInvariant() + ")", "IV. LOGISTICS (" + d.DayTitle.ToUpperInvariant() + ")") + "</h2>");
                html.AppendLine("<div class='sub-p'><b>" + BuilderI18n.T("A. Styl wyposażenia:", "A. Loadout Style:") + "</b> " + E(d.RespawnAndSupply) + "</div>");
                html.AppendLine("<div class='sub-p'><b>" + BuilderI18n.T("B. Wyposażenie oddziału (SZPEJLISTA):", "B. Squad Equipment Checklist:") + "</b><br/>" + E(d.SquadGearChecklist) + "</div>");
                html.AppendLine("<div class='sub-p'><b>" + BuilderI18n.T("C. Wyposażenie specjalne:", "C. Special Equipment:") + "</b><br/>" + E(d.SpecialEquipment) + "</div>");
                html.AppendLine("<div class='sub-p'><b>" + BuilderI18n.T("D. Dojazd (Parking, odległość, transport):", "D. Travel & Parking:") + "</b><br/>" + E(d.TravelAndParking) + "</div>");
                html.AppendLine("<div class='sub-p'><b>" + BuilderI18n.T("E. Regulamin gry:", "E. Game Rules:") + "</b> " + E(d.GameRulesLink) + "</div>");

                if (d.EnableRolesAnnex && d.CustomRolesAnnex.Count > 0)
                {
                    html.AppendLine("<h2>" + BuilderI18n.T("ZAŁĄCZNIK 1. PODZIAŁ RÓL DRUŻYNOWYCH (" + d.DayTitle.ToUpperInvariant() + ")", "ANNEX 1. SQUAD ROLE DEFINITIONS (" + d.DayTitle.ToUpperInvariant() + ")") + "</h2>");
                    html.AppendLine("<table><thead><tr><th style='width:22%'>" + BuilderI18n.T("Rola", "Role") + "</th><th>" + BuilderI18n.T("Opis obowiązków", "Duties Description") + "</th><th style='width:15%'>" + BuilderI18n.T("Zajęcie", "Duty Mode") + "</th></tr></thead><tbody>");
                    foreach (var roleDef in d.CustomRolesAnnex)
                    {
                        html.AppendLine("<tr><td><b>" + E(roleDef.RoleName) + "</b></td><td>" + E(roleDef.Description) + "</td><td>" + E(roleDef.DutyMode) + "</td></tr>");
                    }
                    html.AppendLine("</tbody></table>");
                }

                html.AppendLine("<h2>" + BuilderI18n.T("ZAŁĄCZNIK 2. OPRACOWANIE ZADAŃ (" + d.DayTitle.ToUpperInvariant() + ")", "ANNEX 2. TASKS BREAKDOWN (" + d.DayTitle.ToUpperInvariant() + ")") + "</h2>");
                html.AppendLine("<div class='sub-p'>" + E(d.Annex2TasksAnalysis) + "</div>");
                html.AppendLine("<div style='" + classStyle + "margin-top:12px;'>" + E(d.Classification) + " — " + E(d.DayTitle) + " — Field TAK Hub v3.3</div>");
            }

            html.AppendLine("</body></html>");
            return html.ToString();
        }

        public static string GenerateDiscordMarkdownOpord(Window win, string factionName, OpordParagraphModel m)
        {
            EnsureDefaultCollectionsInitialized(m);
            var exportedDays = GetSelectedDaysForExport(m);
            string eventName = win != null ? TacticalConfigPanel.GetEventNameFromWindow(win) : "OPERACJA";
            var sb = new StringBuilder();

            for (int dayIdx = 0; dayIdx < exportedDays.Count; dayIdx++)
            {
                var d = exportedDays[dayIdx];
                EnsureDayDefaultsInitialized(d, factionName);

                if (dayIdx > 0)
                {
                    sb.AppendLine("\n========================================\n");
                }

                sb.AppendLine("# 📋 " + d.OperationNumber + " — " + eventName + " [" + factionName + " — " + d.DayTitle + " (" + d.DateIso + ")]");
                sb.AppendLine("`" + d.Classification + "` | **DTG:** `" + d.DtgIssued + "` | **HQ:** `" + d.IssuingHq + "`");
                sb.AppendLine("**Harmonogram dnia (" + d.DayTitle + "):**\n" + d.DaySpecificTasks);
                sb.AppendLine("---");
                sb.AppendLine("## 1️⃣ " + BuilderI18n.T("SYTUACJA (" + d.DayTitle + ")", "SITUATION (" + d.DayTitle + ")"));
                sb.AppendLine("- **A. AOI / POI:** " + d.AreaOfInterestPoiDesc);
                sb.AppendLine("- **B. Teren i METOC (" + d.DayTitle + "):**\n" + d.TerrainAndWeather);
                sb.AppendLine("- **C. Siły wroga (COA):** " + d.EnemyForces);
                sb.AppendLine("- **D. Siły przyjazne:** " + d.FriendlyForces);
                sb.AppendLine();
                sb.AppendLine("## 2️⃣ " + BuilderI18n.T("MISJA (" + d.DayTitle + ")", "MISSION (" + d.DayTitle + ")"));
                sb.AppendLine("- **A. Opis fabularny:** " + d.MissionLoreDesc);
                sb.AppendLine("> 🎯 **B. Główne zadanie (" + d.DayTitle + "):** **" + (string.IsNullOrWhiteSpace(d.MissionFullStatement) ? Compose5WMissionTextForDay(d) : d.MissionFullStatement) + "**");
                sb.AppendLine("- **C. Dodatkowe zadania:**\n" + d.AdditionalTasksSummary);
                sb.AppendLine();
                sb.AppendLine("## 3️⃣ " + BuilderI18n.T("WYKONANIE (" + d.DayTitle + ")", "EXECUTION (" + d.DayTitle + ")"));
                sb.AppendLine("- **A. Zamiar dowódcy:** " + d.CommandersIntent);
                sb.AppendLine("- **B. Styl działania:** " + d.ConceptOfOperations);
                sb.AppendLine("- **C. Łańcuch dowodzenia:** `" + d.SuccessionOfCommand + "`");
                if (d.EnableSquadRosterTable && d.SquadRoster.Count > 0)
                {
                    sb.AppendLine("**D. Skład oddziału (" + d.DayTitle + "):**");
                    sb.AppendLine("```text");
                    foreach (var r in d.SquadRoster)
                    {
                        sb.AppendLine(string.Format("{0,-12} | {1,-24} | {2,-20} | {3}", r.OperatorOrTeam, r.PrimaryRole, r.SecondaryRole, r.WeaponAndGear));
                    }
                    sb.AppendLine("```");
                }
                sb.AppendLine("**E. Tabela łączności (" + d.DayTitle + "):**");
                sb.AppendLine("```text");
                sb.AppendLine("• Kanał główny:   " + d.PrimaryRadioFreq);
                sb.AppendLine("• Kanał zapasowy: " + d.BackupRadioFreq);
                sb.AppendLine("• Kanał HQ:       " + d.HqRadioFreq);
                sb.AppendLine(BuildLiveSignalSummaryText(win, factionName));
                sb.AppendLine("```");
                sb.AppendLine("**F. Sygnały, SARNEG i Hasła (" + d.DayTitle + "):**");
                sb.AppendLine("- **Kryptonimy celów:** " + d.TargetCodeNames);
                sb.AppendLine("```text\n" + FormatSarnegMatrixPreview(d.SarnegKeyWord10) + "\n```");
                sb.AppendLine("- **Hasła rozpoznawcze (" + d.DayTitle + "):**\n" + d.ChallengeAndPassword);
                sb.AppendLine("- **G. MEDEVAC / Respawn:** " + d.MedevacRules);
                sb.AppendLine();
                sb.AppendLine("## 4️⃣ " + BuilderI18n.T("LOGISTYKA (" + d.DayTitle + ")", "LOGISTICS (" + d.DayTitle + ")"));
                sb.AppendLine("- **A. Styl wyposażenia:** " + d.RespawnAndSupply);
                sb.AppendLine("- **B. SZPEJLISTA:** " + d.SquadGearChecklist);
                sb.AppendLine("- **C. Wyposażenie specjalne:** " + d.SpecialEquipment);
                sb.AppendLine("- **D. Dojazd i parking:** " + d.TravelAndParking);
                sb.AppendLine("- **E. Regulamin gry:** " + d.GameRulesLink);
                if (d.EnableRolesAnnex && d.CustomRolesAnnex.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("## 📎 " + BuilderI18n.T("ZAŁĄCZNIK 1. PODZIAŁ RÓL DRUŻYNOWYCH (" + d.DayTitle + ")", "ANNEX 1. SQUAD ROLE DEFINITIONS (" + d.DayTitle + ")"));
                    foreach (var rd in d.CustomRolesAnnex)
                    {
                        sb.AppendLine("- **" + rd.RoleName + "** (`" + rd.DutyMode + "`): " + rd.Description);
                    }
                }
                sb.AppendLine();
                sb.AppendLine("## 📎 " + BuilderI18n.T("ZAŁĄCZNIK 2. OPRACOWANIE ZADAŃ (" + d.DayTitle + ")", "ANNEX 2. TASKS BREAKDOWN (" + d.DayTitle + ")"));
                sb.AppendLine(d.Annex2TasksAnalysis);
            }

            return sb.ToString();
        }
    }
}
