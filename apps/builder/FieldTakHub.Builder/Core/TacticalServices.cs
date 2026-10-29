#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FieldTakHub.Builder.Core
{
    /// <summary>
    /// Generator unikalnych, kontrastowych kolorów RGB dla stron konfliktu
    /// oraz kluczy kryptograficznych AES-256 PSK i słownika SARNEG.
    /// </summary>
    public static class TacticalServices
    {
        private static readonly string[] TacticalDistinctPalette = new[]
        {
            "#2684FF", // BLUFOR Blue
            "#EB4034", // OPFOR Crimson
            "#2ECC71", // GREENFOR Emerald
            "#FAD02C", // Tactical Gold
            "#9B59B6", // SpecOps Violet
            "#FF9F43", // Recon Orange
            "#1FE0C5", // C2 Cyan
            "#E84393", // Signal Magenta
            "#A3CB38", // Olive Lime
            "#00CEC9", // Deep Teal
            "#FF6B6B", // Strike Coral
            "#54A0FF", // Sky Command
            "#5F27CD", // Night Indigo
            "#10AC84", // Jungle Green
            "#F368E0", // Neon Rose
            "#FF9FF3"  // Light Orchid
        };

        /// <summary>
        /// Losuje wolny, unikalny kolor z palety taktycznej RGB inny niż wszystkie aktualnie używane kolory stron.
        /// </summary>
        public static string GenerateUniqueFactionColorHex(IEnumerable<string> currentlyUsedHexColors)
        {
            var used = new HashSet<string>(
                (currentlyUsedHexColors ?? Enumerable.Empty<string>())
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Select(c => c.Trim()),
                StringComparer.OrdinalIgnoreCase);

            var rng = new Random(Guid.NewGuid().GetHashCode());
            var freePalette = TacticalDistinctPalette
                .Where(c => !used.Contains(c))
                .OrderBy(_ => rng.Next())
                .ToList();

            if (freePalette.Count > 0)
            {
                return freePalette[0];
            }

            string candidate;
            do
            {
                candidate = string.Format("#{0:X2}{1:X2}{2:X2}", rng.Next(45, 245), rng.Next(55, 245), rng.Next(45, 245));
            } while (used.Contains(candidate));

            return candidate;
        }

        /// <summary>
        /// Generuje bezpieczny, 256-bitowy klucz szyfrowania AES-256 PSK (Base64) dla kanału LoRa Meshtastic.
        /// </summary>
        public static string GenerateAes256PskBase64()
        {
            byte[] keyBytes = new byte[32];
            RandomNumberGenerator.Fill(keyBytes);
            return Convert.ToBase64String(keyBytes);
        }

        // Baza 10-literowych polskich słów taktycznych bez powtarzających się liter (0..9) dla tabeli SARNEG.
        // Zgodnie ze specyfikacją słowa SARNEG nie są tłumaczone na język angielski.
        private static readonly string[] ValidSarnegWords10 = new[]
        {
            "PRZEDMIOTY", "SKRZYZOWAN", "WSTRZASNIJ", "BIEGUNOWym", "CHMURZASTY",
            "CZYSTOKREW", "DOKUMENTAC", "EKSPLORACJ", "FABRYKOWAC", "GARNCARSTW",
            "HARMONOGRF", "INFORMACJE", "JASKOLCZYM", "Kierownica", "KOMBINATOR",
            "KONTROLUJE", "LADOWISkiem", "MAGNETOWID", "MELancholi", "NADZORCZYM",
            "NIEPOJETYM", "ODKSZTALCI", "ORGANIZMUJ", "PANCERNIKU", "PIASKOWNIC",
            "PLATFUSOWY", "PODRĘCZNIK", "POJEDYNCZY", "POLIGRAFIE", "PRACOWNIKU",
            "PROJEKTANT", "PRZEKupion", "PRZEMYSlow", "PRZEWODNIK", "PUBLIKACJE",
            "ROZBIERANY", "ROZMAITYCH", "SCHEMATYCZ", "SKORUPeczk", "SPADOCHRON",
            "STEROWNIKU", "SZTUKMISTR", "TANCERKAMI", "TORNISTREM", "TROJKATNYM",
            "URZEDNIKOM", "WARTOWNICY", "WIATROPYLN", "WODOCIAGiem", "WSKAZNIKOM",
            "WYKSZTALCI", "ZABEZPIECZ", "ZAKODOWANY", "ZBIORNIKEM", "ZDOBYWCAMI"
        };

        public static string NormalizePolishAscii10(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "PRZEDMIOTY";
            string s = raw.Trim().ToUpperInvariant()
                .Replace('Ą', 'A').Replace('Ć', 'C').Replace('Ę', 'E')
                .Replace('Ł', 'L').Replace('Ń', 'N').Replace('Ó', 'O')
                .Replace('Ś', 'S').Replace('Ź', 'Z').Replace('Ż', 'Z');

            var distinctChars = new List<char>();
            foreach (char c in s)
            {
                if (c >= 'A' && c <= 'Z' && !distinctChars.Contains(c))
                    distinctChars.Add(c);
            }

            string fallbackAlphabet = "PRZEDMIOTYABCDEFGHJKLNSUWXZ";
            foreach (char c in fallbackAlphabet)
            {
                if (distinctChars.Count >= 10) break;
                if (!distinctChars.Contains(c)) distinctChars.Add(c);
            }

            return new string(distinctChars.Take(10).ToArray());
        }

        public static string GetRandomSarnegWord10(string currentWordToAvoid = null)
        {
            var rng = new Random(Guid.NewGuid().GetHashCode());
            var normalizedPool = ValidSarnegWords10
                .Select(NormalizePolishAscii10)
                .Where(w => w.Length == 10 && w.Distinct().Count() == 10)
                .Distinct()
                .ToList();

            var candidates = normalizedPool
                .Where(w => !string.Equals(w, currentWordToAvoid, StringComparison.OrdinalIgnoreCase))
                .OrderBy(_ => rng.Next())
                .ToList();

            return candidates.Count > 0 ? candidates[0] : "PRZEDMIOTY";
        }

        /// <summary>
        /// Katalog ról MilSimowych do Tabeli D oraz Załącznika nr 1 w rozkazie OPORD.
        /// </summary>
        public static List<Tuple<string, string>> GetMilsimRolesCatalog()
        {
            return new List<Tuple<string, string>>
            {
                Tuple.Create(
                    LocalizationManager.T("Lider (Dowódca Sekcji)", "Squad Leader (SL)"),
                    LocalizationManager.T("Dowodzenie oddziałem, podejmowanie decyzji taktycznych, utrzymanie zamiaru dowódcy.", "Overall squad command, tactical decision-making, maintaining commander's intent.")),
                Tuple.Create(
                    LocalizationManager.T("Nawigator (Navi)", "Pointman / Navigator (Navi)"),
                    LocalizationManager.T("Prowadzenie po azymucie i siatce MGRS, kontrola granic AO, analiza rzeźby LiDAR, zegarek GPX.", "MGRS grid & azimuth navigation, AO boundary control, LiDAR terrain analysis, GPX watch.")),
                Tuple.Create(
                    LocalizationManager.T("RTO (Radiooperator)", "RTO (Radiotelephone Operator)"),
                    LocalizationManager.T("Obsługa łączności z HQ, szyfrowanie meldunków tabelą SARNEG, nadzór nad bramką LoRa Meshtastic.", "HQ radio comms, SARNEG coordinate encryption, LoRa Meshtastic gateway supervision.")),
                Tuple.Create(
                    LocalizationManager.T("Medyk Polowy", "Combat Medic"),
                    LocalizationManager.T("Ewakuacja trafionych z linii ognia, zakładanie opatrunków, kontrola czasu krwawienia i procedur MEDEVAC.", "Casualty extraction, applying bandages, bleed-out timer control and MEDEVAC procedures.")),
                Tuple.Create(
                    LocalizationManager.T("Adiutant (Zastępca Dowódcy / 2IC)", "Executive Officer (XO / 2IC)"),
                    LocalizationManager.T("Kontrola stanów osobowych, amunicji i wody (ACE), przejęcie dowodzenia w razie wyeliminowania Lidera.", "Personnel, ammo & water checks (ACE), assuming command if Squad Leader is hit.")),
                Tuple.Create(
                    LocalizationManager.T("Operator LARP / Negocjator", "LARP / Civil Affairs Specialist"),
                    LocalizationManager.T("Interakcja z postaciami fabularnymi (NPC), przesłuchania, weryfikacja dokumentów i zadań specjalnych.", "Interaction with in-game NPCs, interrogations, document verification and special tasks.")),
                Tuple.Create(
                    LocalizationManager.T("Fotograf / Dokumentalista Rozpoznania", "Recon Photographer / Intel"),
                    LocalizationManager.T("Wykonywanie zdjęć kodów terenowych, obiektów wroga oraz przesyłanie meldunków ze współrzędnymi MGRS.", "Photographing field codes and enemy installations, logging MGRS intel reports.")),
                Tuple.Create(
                    LocalizationManager.T("Tragarz / Wsparcie Ogniowe (RKM)", "Support Gunner / Breacher"),
                    LocalizationManager.T("Transport ciężkiego wyposażenia zespołowego, ogień zaporowy, wsparcie szturmu.", "Carrying heavy squad equipment, suppressive fire, breaching support.")),
                Tuple.Create(
                    LocalizationManager.T("Organizator Obrony (Inżynier)", "Defense & Perimeter Coordinator"),
                    LocalizationManager.T("Wyznaczanie sektorów ognia na postoju (360°), organizacja obrony okrężnej i punktów zbiórki (ORP).", "Assigning 360° fire sectors during halts, perimeter defense and ORP setup.")),
                Tuple.Create(
                    LocalizationManager.T("Czujka / Szpica (Zwiadowca)", "Scout / Rear Security"),
                    LocalizationManager.T("Ubezpieczenie marszu z przodu lub z tyłu kolumny, wczesne wykrywanie zasadzek przeciwnika.", "Front point or rear guard security, early ambush detection."))
            };
        }
    }
}
