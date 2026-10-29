#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace FieldTakHub.Builder.Core
{
    public enum AppLanguage
    {
        PL,
        EN
    }

    /// <summary>
    /// Centralny, reaktywny silnik tłumaczeń interfejsu (PL / EN) napisany od podstaw.
    /// Wszystkie elementy UI są tłumaczone dynamicznie, natomiast słowa kryptograficzne SARNEG
    /// pozostają zawsze w oryginale polskim zgodnie ze specyfikacją taktyczną.
    /// </summary>
    public sealed class LocalizationManager : INotifyPropertyChanged
    {
        public static LocalizationManager Instance { get; } = new LocalizationManager();

        private AppLanguage _currentLanguage = AppLanguage.PL;

        public event PropertyChangedEventHandler PropertyChanged;
        public event Action LanguageChanged;

        public AppLanguage CurrentLanguage
        {
            get => _currentLanguage;
            set
            {
                if (_currentLanguage != value)
                {
                    _currentLanguage = value;
                    OnPropertyChanged(nameof(CurrentLanguage));
                    OnPropertyChanged(nameof(IsPolish));
                    OnPropertyChanged(nameof(IsEnglish));
                    OnPropertyChanged(nameof(LanguageButtonLabel));
                    OnPropertyChanged("Item[]");
                    LanguageChanged?.Invoke();
                }
            }
        }

        public bool IsPolish => _currentLanguage == AppLanguage.PL;
        public bool IsEnglish => _currentLanguage == AppLanguage.EN;
        public string LanguageButtonLabel => _currentLanguage == AppLanguage.PL ? "🇵🇱 JĘZYK: PL (ZMIEŃ NA EN)" : "🇬🇧 LANG: EN (SWITCH TO PL)";

        public void ToggleLanguage()
        {
            CurrentLanguage = (_currentLanguage == AppLanguage.PL) ? AppLanguage.EN : AppLanguage.PL;
        }

        public static string T(string pl, string en)
        {
            return Instance.CurrentLanguage == AppLanguage.PL ? pl : en;
        }

        public string this[string key] => Get(key);

        public static string Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            if (DictionaryMap.TryGetValue(key, out Tuple<string, string> pair))
            {
                return Instance.CurrentLanguage == AppLanguage.PL ? pair.Item1 : pair.Item2;
            }
            return key;
        }

        private static readonly Dictionary<string, Tuple<string, string>> DictionaryMap =
            new Dictionary<string, Tuple<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                // Nawigacja i Pasek Boczny (Sidebar)
                { "APP_TITLE", Tuple.Create("FIELD TAK HUB — CENTRUM DOWODZENIA C2 v4.0", "FIELD TAK HUB — C2 COMMAND CENTER v4.0") },
                { "NAV_PACKAGES", Tuple.Create("Konfigurator Stron & .FTAK", "Faction & .FTAK Builder") },
                { "NAV_MAP", Tuple.Create("Generator Map 1:1 (MGRS/LiDAR)", "1:1 Tactical Map Studio") },
                { "NAV_OPORD", Tuple.Create("Generator Rozkazu (OPORD)", "OPORD Order Generator") },
                { "NAV_USB", Tuple.Create("Programator Płytek USB (LoRa)", "USB Board Programmer (LoRa)") },
                { "SIDEBAR_COLLAPSE", Tuple.Create("Zwiń panel boczny", "Collapse sidebar") },
                { "SIDEBAR_EXPAND", Tuple.Create("Rozwiń panel boczny", "Expand sidebar") },

                // Moduł 3: Generator Stron i Paczek (.ftak)
                { "PKG_HEADER", Tuple.Create("🎯 GENERATOR PACZEK .FTAK (FOLDER out\\<STRONA>\\ + WSPÓLNY FOLDER APK / PLUGINY / MAPY)", "🎯 .FTAK PACKAGE GENERATOR (out\\<FACTION>\\ + SHARED APK / PLUGINS / MAPS)") },
                { "BTN_BUILD_ALL", Tuple.Create("⚡ Buduj wszystkie aktywne strony", "⚡ Build All Active Factions") },
                { "BTN_OPEN_SHARED", Tuple.Create("📂 Otwórz wspólny folder (APK / Pluginy / Mapy)", "📂 Open Shared Folder (APK / Plugins / Maps)") },
                { "BTN_WIFI_HOTSPOT", Tuple.Create("📡 Dystrybucja przez HOTSPOT Wi-Fi", "📡 Distribute via Wi-Fi HOTSPOT") },
                { "FACTION_ADD", Tuple.Create("➕ Dodaj nową stronę konfliktu (Losuj unikalny kolor RGB)", "➕ Add New Faction (Randomize Unique RGB Color)") },
                { "FACTION_FILES", Tuple.Create("📂 Pliki strony", "📂 Faction Files") },
                { "FACTION_POSTER_QR", Tuple.Create("🖨️ Plakat Safezone A4 + Kod QR", "🖨️ Safezone A4 Poster + QR Code") },
                { "EXPIRATION_LABEL", Tuple.Create("Ważność paczki i kodu QR w godzinach (0 = bezterminowo):", "Package & QR expiration in hours (0 = permanent):") },
                { "VOICE_TABLE_HEADER", Tuple.Create("📻 KANAŁY GŁOSOWE (PMR / VHF / UHF / DMR) & TELEFON ALARMOWY SOS", "📻 VOICE CHANNELS (PMR / VHF / UHF / DMR) & SOS EMERGENCY PHONE") },
                { "BTN_ADD_CHANNEL", Tuple.Create("➕ Dodaj kanał", "➕ Add Channel") },

                // Moduł 1: Generator Map 1:1 (Tactical Map Studio)
                { "MAP_HEADER", Tuple.Create("🗺️ GENERATOR MAP TAKTYCZNYCH 1:1 (HYBRYDA ORTO + TURBO LiDAR ISOK / SIATKA MGRS)", "🗺️ 1:1 TACTICAL MAP STUDIO (ORTHO + TURBO LiDAR ISOK HYBRID / MGRS GRID)") },
                { "MAP_LIDAR_SLIDER", Tuple.Create("Natężenie rzeźby terenu Turbo LiDAR (ISOK):", "Turbo LiDAR (ISOK) Hillshade Opacity:") },
                { "MAP_SCALE_LABEL", Tuple.Create("Skala wydruku 1:1 na arkuszu:", "True 1:1 Print Scale:") },
                { "MAP_COORD_SYS", Tuple.Create("Układ współrzędnych siatki i meldunków:", "Grid & Reporting Coordinate System:") },
                { "MAP_SUBDIV_LABEL", Tuple.Create("Podział wewnętrzny kwadratów 100 m:", "100m Square Internal Subdivision:") },
                { "MAP_DRAW_AO", Tuple.Create("📐 Rysuj / Edytuj granicę gry (AO)", "📐 Draw / Edit Game Boundary (AO)") },
                { "MAP_CLEAR_AO", Tuple.Create("🗑️ Wyczyść AO", "🗑️ Clear AO") },
                { "MAP_ADD_POI", Tuple.Create("📍 Dodaj punkt taktyczny POI / Znak NATO APP-6", "📍 Add Tactical POI / NATO APP-6 Symbol") },
                { "MAP_EXPORT_A4", Tuple.Create("🖨️ Eksportuj Arkusz 1:1 (HTML / PDF A4 / Mapnik)", "🖨️ Export 1:1 Sheet (HTML / PDF A4 / Wristband)") },
                { "MAP_EXPORT_KMZ", Tuple.Create("📦 Eksportuj .KMZ (ATAK)", "📦 Export .KMZ (ATAK)") },
                { "MAP_EXPORT_GPX", Tuple.Create("⌚ Eksportuj .GPX (Garmin / Suunto)", "⌚ Export .GPX (Garmin / Suunto)") },
                { "MAP_EXPORT_MBTILES", Tuple.Create("💾 Generuj Mapę Offline .mbtiles (z LiDAR)", "💾 Build Offline .mbtiles Map (with LiDAR)") },

                // Moduł 2: Generator Rozkazu Bojowego (OPORD v4.0)
                { "OPORD_HEADER", Tuple.Create("📋 GENERATOR ROZKAZU BOJOWEGO NATO / MILSIM (OPORD v4.0)", "📋 NATO / MILSIM OPERATION ORDER GENERATOR (OPORD v4.0)") },
                { "OPORD_SYNC_TOP_BTN", Tuple.Create("🔄 Pobierz z Generatora Stron (Odśwież strony, kanały radiowe i współrzędne AO)", "🔄 Pull from Faction Generator (Refresh Factions, Radio Channels & AO Coords)") },
                { "OPORD_PRINT_A4", Tuple.Create("🖨️ Generuj OPORD do druku A4 (HTML / PDF)", "🖨️ Generate Printable A4 OPORD (HTML / PDF)") },
                { "OPORD_COPY_DISCORD", Tuple.Create("💬 Kopiuj na Discorda (Skondensowany Markdown)", "💬 Copy for Discord (Condensed Markdown)") },
                { "OPORD_SAVE_FTAK", Tuple.Create("💾 Zapisz rozkaz w paczce .FTAK", "💾 Save Order to .FTAK Package") },
                { "OPORD_ALL_DAYS_BTN", Tuple.Create("☑️ Generuj wszystkie dni", "☑️ Generate all days") },
                { "OPORD_ACTIVE_DAY_BTN", Tuple.Create("☑️ Generuj tylko aktywny dzień", "☑️ Generate active day only") },
                { "METOC_FETCH_BTN", Tuple.Create("🌦️ Pobierz METOC dla aktywnego dnia", "🌦️ Fetch METOC for Active Day") },
                { "METOC_ERR_NO_AO", Tuple.Create("⚠️ Nie można pobrać danych METOC dla samego arkusza mapy!\n\nZaznacz obszar AO w Generatorze Map 1:1 lub wpisz miejscowość.", "⚠️ Cannot fetch METOC data for an unmarked map sheet!\n\nPlease mark an AO in the 1:1 Map Generator or enter a location name.") },
                { "SEC_E_HEADER", Tuple.Create("E. Tabela łączności (Automatycznie z Generatora Stron + opcjonalna tabela ręczna):", "E. Communications Matrix (Auto-synced from Faction Generator + optional manual table):") },
                { "SEC_E_CUSTOM_CHK", Tuple.Create("☑️ Dodaj ręcznie dodatkowe kanały radiowe w OPORD (Tabela opcjonalna)", "☑️ Manually add extra radio channels in OPORD (Optional table)") },
                { "ANNEX_3_CHK", Tuple.Create("☑️ Załącznik 3: Dołącz mapę operacyjną / szkic AO do rozkazu OPORD", "☑️ Annex 3: Attach operational map / AO sketch to OPORD") },

                // Moduł 4: Masowy Programator Płytek USB
                { "USB_HEADER", Tuple.Create("🔌 MASOWY PROGRAMATOR PŁYTEK USB (LoRa MESHTASTIC — HELTEC V3 / T-BEAM)", "🔌 MASS USB BOARD PROGRAMMER (LoRa MESHTASTIC — HELTEC V3 / T-BEAM)") },
                { "USB_SCAN_BTN", Tuple.Create("🔍 Skanuj porty COM", "🔍 Scan COM Ports") },
                { "USB_FLASH_ALL_BTN", Tuple.Create("⚡ Wgraj profil wybranej strony na wszystkie płytki USB", "⚡ Flash Selected Faction Profile to All USB Boards") }
            };

        private void OnPropertyChanged(string propName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
    }
}
