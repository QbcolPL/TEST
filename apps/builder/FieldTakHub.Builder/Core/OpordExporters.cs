#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace FieldTakHub.Builder.Core
{
    /// <summary>
    /// Silnik eksportu rozkazu bojowego OPORD v4.0 napisany od zera:
    /// 1. Wielostronicowy wydruk A4 (HTML / PDF) z obsługą wybranych dni, Tabeli D, Tabeli E (automatycznej + ręcznej na ptaszek),
    ///    Załącznika 1 (Katalog ról), Załącznika 2 (Tabela Krypto SARNEG) oraz Załącznika 3 (Mapa operacyjna / szkic AO),
    /// 2. Skondensowany eksport Discord Markdown z automatycznym podziałem na wiadomości poniżej 1900 znaków,
    /// 3. Zapis rozkazu bezpośrednio do katalogu wyjściowego strony konfliktu out\<STRONA>\.
    /// </summary>
    public static class OpordExporters
    {
        public static List<OpordDayPlanModel> GetDaysForExport(OpordDocumentModel doc)
        {
            if (doc == null) doc = ProjectStore.Instance.OpordDocument;
            var selected = doc.Days.Where(d => d.IncludeInExport).ToList();
            if (selected.Count == 0 && doc.Days.Count > 0)
            {
                int idx = Math.Clamp(doc.ActiveDayIndex, 0, doc.Days.Count - 1);
                selected.Add(doc.Days[idx]);
            }
            return selected;
        }

        public static string ExportPrintableA4Html(OpordDocumentModel doc, bool openInBrowser = true)
        {
            if (doc == null) doc = ProjectStore.Instance.OpordDocument;
            ProjectStore.Instance.SyncGeneratorsToOpord();

            string outDir = ProjectStore.Instance.GetFactionOutputDirectory(doc.TargetFaction);
            string htmlPath = Path.Combine(outDir, "OPORD_A4_" + ProjectStore.SanitizeFileName(doc.TargetFaction) + ".html");

            var daysToExport = GetDaysForExport(doc);
            var sb = new StringBuilder();

            sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'/>");
            sb.AppendLine("<title>OPORD — " + WebUtility.HtmlEncode(doc.EventName) + " (" + WebUtility.HtmlEncode(doc.TargetFaction) + ")</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("@page { size: A4 portrait; margin: 12mm; }");
            sb.AppendLine("body { font-family: 'Segoe UI', Arial, sans-serif; background: #060D10; color: #E8F1F5; margin: 0; padding: 16px; font-size: 12px; line-height: 1.45; }");
            sb.AppendLine(".page { max-width: 190mm; margin: 0 auto 24px auto; background: #0A151B; border: 2px solid #1FE0C5; border-radius: 8px; padding: 16px 20px; page-break-after: always; }");
            sb.AppendLine(".banner { text-align: center; padding: 6px; font-weight: bold; background: #122B36; color: #FAD02C; border: 1px solid #FAD02C; margin-bottom: 12px; letter-spacing: 1px; }");
            sb.AppendLine("h1 { margin: 0 0 6px 0; font-size: 20px; color: #1FE0C5; }");
            sb.AppendLine("h2 { margin: 14px 0 6px 0; font-size: 14px; color: #FAD02C; border-bottom: 1px solid #1B3B4B; padding-bottom: 3px; text-transform: uppercase; }");
            sb.AppendLine("h3 { margin: 10px 0 4px 0; font-size: 12.5px; color: #1FE0C5; }");
            sb.AppendLine(".box { background: #081218; border: 1px solid #1B3B4B; border-radius: 5px; padding: 8px 10px; margin-bottom: 8px; white-space: pre-wrap; font-family: Consolas, monospace; font-size: 11.5px; }");
            sb.AppendLine(".prose-box { background: #081218; border: 1px solid #1B3B4B; border-radius: 5px; padding: 8px 10px; margin-bottom: 8px; white-space: pre-wrap; }");
            sb.AppendLine("table { width: 100%; border-collapse: collapse; margin: 6px 0 10px 0; font-size: 11.5px; }");
            sb.AppendLine("th, td { border: 1px solid #1B3B4B; padding: 5px 8px; text-align: left; }");
            sb.AppendLine("th { background: #102530; color: #1FE0C5; }");
            sb.AppendLine(".sarneg-tbl td, .sarneg-tbl th { text-align: center; font-family: Consolas, monospace; font-size: 14px; font-weight: bold; }");
            sb.AppendLine("@media print { body { background: #fff; color: #000; padding: 0; } .page { background: #fff; color: #000; border: 1px solid #000; margin: 0; } .box, .prose-box { background: #f8f8f8; color: #000; border-color: #888; } th { background: #eee; color: #000; } th, td { border-color: #666; } h1, h2, h3 { color: #000; } .no-print { display: none; } }");
            sb.AppendLine("</style></head><body>");

            sb.AppendLine("<div class='no-print' style='text-align:center;margin-bottom:14px;'>");
            sb.AppendLine("<button onclick='window.print()' style='padding:10px 24px;background:#1FE0C5;color:#051014;font-weight:bold;border:none;border-radius:6px;cursor:pointer;'>🖨️ DRUKUJ ROZKAZ OPORD A4 / ZAPISZ JAKO PDF</button>");
            sb.AppendLine("</div>");

            foreach (var day in daysToExport)
            {
                sb.AppendLine("<div class='page'>");
                sb.AppendLine("<div class='banner'>" + WebUtility.HtmlEncode(day.Classification) + "</div>");
                sb.AppendLine("<h1>📋 " + WebUtility.HtmlEncode(day.OperationNumber) + " — " + WebUtility.HtmlEncode(doc.EventName) + "</h1>");
                sb.AppendLine("<p style='margin:2px 0 10px 0;'><b>STRONA:</b> " + WebUtility.HtmlEncode(doc.TargetFaction) +
                              " &nbsp;|&nbsp; <b>DZIEŃ OPERACJI:</b> " + WebUtility.HtmlEncode(day.DayTitle) + " (" + WebUtility.HtmlEncode(day.DateIso) + ")" +
                              " &nbsp;|&nbsp; <b>DOWÓDZTWO (HQ):</b> " + WebUtility.HtmlEncode(day.IssuingHq) + "</p>");

                // I. SYTUACJA
                sb.AppendLine("<h2>I. SYTUACJA (SITUATION)</h2>");
                sb.AppendLine("<h3>A. Siły Przeciwnika (Enemy Forces):</h3><div class='prose-box'>" + WebUtility.HtmlEncode(day.EnemyForces) + "</div>");
                sb.AppendLine("<h3>B. Siły Własne (Friendly Forces):</h3><div class='prose-box'>" + WebUtility.HtmlEncode(day.FriendlyForces) + "</div>");
                sb.AppendLine("<h3>B.1. Teren i Pogoda (METOC):</h3><div class='box'>" + WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(day.TerrainAndWeather) ? day.MetocQuickSummary : day.TerrainAndWeather) + "</div>");
                sb.AppendLine("<h3>C. Obszar Operacji (AO) i Punkty Taktyczne POI:</h3><div class='box'>" + WebUtility.HtmlEncode(day.AreaOfInterestPoiDesc) + "</div>");

                // II. MISJA
                sb.AppendLine("<h2>II. MISJA (5W — MISSION)</h2>");
                sb.AppendLine("<div class='prose-box' style='border-color:#FAD02C;font-weight:bold;'>" + WebUtility.HtmlEncode(day.MissionFullStatement) + "</div>");

                // III. WYKONANIE
                sb.AppendLine("<h2>III. WYKONANIE (EXECUTION)</h2>");
                sb.AppendLine("<h3>A. Zamiar Dowódcy (Commander's Intent):</h3><div class='prose-box'>" + WebUtility.HtmlEncode(day.CommandersIntent) + "</div>");
                sb.AppendLine("<h3>B. Harmonogram i Zadania Dnia (" + WebUtility.HtmlEncode(day.DayTitle) + "):</h3><div class='box'>" + WebUtility.HtmlEncode(day.DaySpecificTasks) + "</div>");
                sb.AppendLine("<h3>C. Wytyczne Koordynacyjne:</h3><div class='prose-box'>" + WebUtility.HtmlEncode(day.CoordinationMeasures) + "</div>");

                // Tabela D Składu i Ról
                sb.AppendLine("<h3>D. Tabela Składu Oddziału i Przydziału Ról MilSimowych:</h3>");
                sb.AppendLine("<table><thead><tr><th>Kryptonim (Callsign)</th><th>Rola Główna</th><th>Rola Zapasowa</th><th>Wyposażenie i Zadania</th></tr></thead><tbody>");
                foreach (var m in day.SquadRoster)
                {
                    sb.AppendLine("<tr><td><b>" + WebUtility.HtmlEncode(m.Callsign) + "</b></td><td>" +
                                  WebUtility.HtmlEncode(m.PrimaryRole) + "</td><td>" +
                                  WebUtility.HtmlEncode(m.SecondaryRole) + "</td><td>" +
                                  WebUtility.HtmlEncode(m.GearAndTasks) + "</td></tr>");
                }
                sb.AppendLine("</tbody></table>");

                // V. DOWODZENIE I ŁĄCZNOŚĆ (TABELA E + KRYPTO SARNEG)
                sb.AppendLine("<h2>V. DOWODZENIE I ŁĄCZNOŚĆ (COMMAND &amp; SIGNAL)</h2>");
                sb.AppendLine("<h3>E. Tabela Łączności (Zaciągnięta z Generatora Stron):</h3>");
                sb.AppendLine("<div class='box'>" + WebUtility.HtmlEncode(day.SyncedCommsMatrixSummary) + "</div>");

                if (day.IncludeCustomChannelsTable && day.CustomOpordChannels.Count > 0)
                {
                    sb.AppendLine("<h3>E.1. Dodatkowe Kanały i Częstotliwości Wpisane Ręcznie w OPORD:</h3>");
                    sb.AppendLine("<table><thead><tr><th>Rola / Przeznaczenie</th><th>Pasmo</th><th>Częstotliwość / Kanał</th><th>Ton CTCSS / Kod</th></tr></thead><tbody>");
                    foreach (var vc in day.CustomOpordChannels)
                    {
                        sb.AppendLine("<tr><td><b>" + WebUtility.HtmlEncode(vc.Role) + "</b></td><td>" +
                                      WebUtility.HtmlEncode(vc.Band) + "</td><td><code>" +
                                      WebUtility.HtmlEncode(vc.Frequency) + "</code></td><td>" +
                                      WebUtility.HtmlEncode(vc.Tone) + "</td></tr>");
                    }
                    sb.AppendLine("</tbody></table>");
                }

                sb.AppendLine("<h3>F.1. Szyfrowanie SARNEG, Hasła Rozpoznawcze i Łańcuch Dowodzenia:</h3>");
                sb.AppendLine("<div class='box'><b>Słowo kluczowe SARNEG (" + WebUtility.HtmlEncode(day.DayTitle) + "):</b> " + WebUtility.HtmlEncode(day.SarnegKeyWord10) +
                              "\n<b>Hasła rozpoznawcze (Challenge / Password):</b> " + WebUtility.HtmlEncode(day.ChallengeAndPassword) +
                              "\n<b>Kryptonimy obiektów:</b> " + WebUtility.HtmlEncode(day.TargetCodeNames) +
                              "\n<b>Łańcuch dowodzenia (Succession of Command):</b> " + WebUtility.HtmlEncode(day.SuccessionOfCommand) + "</div>");

                // IV. LOGISTYKA ORAZ ZAŁĄCZNIKI
                sb.AppendLine("<h2>IV. ZABEZPIECZENIE LOGISTYCZNE, MEDEVAC ORAZ ZAŁĄCZNIKI</h2>");
                sb.AppendLine("<h3>A. Zasady MEDEVAC i Ewakuacji:</h3><div class='prose-box'>" + WebUtility.HtmlEncode(day.MedevacRules) + "</div>");
                sb.AppendLine("<h3>B. Obowiązkowe Wyposażenie (Szpejlista):</h3><div class='prose-box'>" + WebUtility.HtmlEncode(day.SquadGearChecklist) + "</div>");

                // Załącznik 1: Katalog ról MilSimowych
                if (day.IncludeAnnex1Roles)
                {
                    sb.AppendLine("<h2>ZAŁĄCZNIK 1 — KATALOG RÓL MILSIMOWYCH I OBOWIĄZKÓW</h2>");
                    sb.AppendLine("<table><thead><tr><th>Rola Funkcyjna</th><th>Szczegółowy Zakres Obowiązków w Polu</th></tr></thead><tbody>");
                    foreach (var rolePair in TacticalServices.GetMilsimRolesCatalog())
                    {
                        sb.AppendLine("<tr><td><b>" + WebUtility.HtmlEncode(rolePair.Item1) + "</b></td><td>" + WebUtility.HtmlEncode(rolePair.Item2) + "</td></tr>");
                    }
                    sb.AppendLine("</tbody></table>");
                }

                // Załącznik 2: Tabela Krypto SARNEG (0..9)
                if (day.IncludeAnnex2Sarneg)
                {
                    string word10 = TacticalServices.NormalizePolishAscii10(day.SarnegKeyWord10);
                    sb.AppendLine("<h2>ZAŁĄCZNIK 2 — TABELA SZYFROWANIA WSPÓŁRZĘDNYCH SARNEG (" + WebUtility.HtmlEncode(day.DayTitle) + ")</h2>");
                    sb.AppendLine("<table class='sarneg-tbl'><thead><tr>");
                    for (int d = 0; d < 10; d++) sb.Append("<th>" + d + "</th>");
                    sb.AppendLine("</tr></thead><tbody><tr>");
                    for (int d = 0; d < 10; d++) sb.Append("<td style='color:#FAD02C;'>" + word10[d] + "</td>");
                    sb.AppendLine("</tr></tbody></table>");
                }

                // Załącznik 3: Mapa operacyjna / szkic AO ("na ptaszek")
                if (day.IncludeAnnex3Map)
                {
                    sb.AppendLine("<h2>ZAŁĄCZNIK 3 — MAPA OPERACYJNA / SZKIC AO (" + WebUtility.HtmlEncode(day.DayTitle) + ")</h2>");
                    if (!string.IsNullOrWhiteSpace(day.Annex3MapNotes))
                    {
                        sb.AppendLine("<div class='prose-box'>" + WebUtility.HtmlEncode(day.Annex3MapNotes) + "</div>");
                    }
                    if (!string.IsNullOrWhiteSpace(day.Annex3MapImagePath) && File.Exists(day.Annex3MapImagePath))
                    {
                        try
                        {
                            byte[] imgBytes = File.ReadAllBytes(day.Annex3MapImagePath);
                            string ext = Path.GetExtension(day.Annex3MapImagePath).ToLowerInvariant();
                            string mime = (ext == ".jpg" || ext == ".jpeg") ? "image/jpeg" : "image/png";
                            sb.AppendLine("<div style='text-align:center;margin-top:8px;'><img src='data:" + mime + ";base64," +
                                          Convert.ToBase64String(imgBytes) + "' style='max-width:100%;max-height:205mm;border:2px solid #1FE0C5;'/></div>");
                        }
                        catch { }
                    }
                }

                sb.AppendLine("<div class='banner' style='margin-top:14px;'>" + WebUtility.HtmlEncode(day.Classification) + "</div>");
                sb.AppendLine("</div>");
            }

            sb.AppendLine("</body></html>");
            File.WriteAllText(htmlPath, sb.ToString(), Encoding.UTF8);

            if (openInBrowser)
            {
                try { Process.Start(new ProcessStartInfo(htmlPath) { UseShellExecute = true }); } catch { }
            }

            return htmlPath;
        }

        public static List<string> BuildCondensedDiscordChunks(OpordDocumentModel doc)
        {
            if (doc == null) doc = ProjectStore.Instance.OpordDocument;
            ProjectStore.Instance.SyncGeneratorsToOpord();

            var chunks = new List<string>();
            var days = GetDaysForExport(doc);

            foreach (var d in days)
            {
                var part1 = new StringBuilder();
                part1.AppendLine("## 📋 " + d.OperationNumber + " | " + doc.TargetFaction + " — " + d.DayTitle + " (" + d.DateIso + ")");
                part1.AppendLine("`" + d.Classification + "` | **HQ:** " + d.IssuingHq + " | **AO:** " + (string.IsNullOrWhiteSpace(d.LocationName) ? "Według mapy AO" : d.LocationName));
                part1.AppendLine("**🌦️ METOC:** " + CompactSingleLine(string.IsNullOrWhiteSpace(d.TerrainAndWeather) ? d.MetocQuickSummary : d.TerrainAndWeather));
                part1.AppendLine("**🎯 II. MISJA (5W):**\n> " + d.MissionFullStatement);
                part1.AppendLine("**⚡ III. ZAMIAR:** " + d.CommandersIntent);
                part1.AppendLine("**⏱️ HARMONOGRAM:** " + CompactSingleLine(d.DaySpecificTasks));

                if (d.SquadRoster.Count > 0)
                {
                    part1.AppendLine("**👥 SKŁAD I ROLE (" + d.DayTitle + "):**");
                    foreach (var m in d.SquadRoster)
                    {
                        part1.AppendLine("• `" + m.Callsign + "` — **" + m.PrimaryRole + "** (Zapas: " + m.SecondaryRole + ") | *" + m.GearAndTasks + "*");
                    }
                }

                var part2 = new StringBuilder();
                part2.AppendLine("### 📡 ŁĄCZNOŚĆ, SARNEG & LOGISTYKA — " + d.DayTitle + " (" + doc.TargetFaction + ")");
                if (!string.IsNullOrWhiteSpace(d.SyncedCommsMatrixSummary))
                {
                    part2.AppendLine("```text\n" + d.SyncedCommsMatrixSummary + "\n```");
                }

                if (d.IncludeCustomChannelsTable && d.CustomOpordChannels.Count > 0)
                {
                    part2.AppendLine("**➕ Dodatkowe kanały radiowe (OPORD):**");
                    foreach (var vc in d.CustomOpordChannels)
                    {
                        part2.AppendLine("• " + vc.FormattedSummary);
                    }
                }

                string word10 = TacticalServices.NormalizePolishAscii10(d.SarnegKeyWord10);
                var sarnegMap = new StringBuilder();
                for (int i = 0; i < 10; i++)
                {
                    if (i > 0) sarnegMap.Append(" | ");
                    sarnegMap.Append(i + "=" + word10[i]);
                }

                part2.AppendLine("**🔐 SARNEG (" + d.DayTitle + "):** `" + word10 + "` (`" + sarnegMap + "`)");
                part2.AppendLine("**🗣️ Hasła:** " + d.ChallengeAndPassword + " | **Kryptonimy:** " + d.TargetCodeNames);
                part2.AppendLine("**⛓️ Dowodzenie:** `" + d.SuccessionOfCommand + "`");
                part2.AppendLine("**🚑 MEDEVAC:** " + d.MedevacRules);
                part2.AppendLine("**🎒 Szpejlista:** " + d.SquadGearChecklist);
                if (d.IncludeAnnex3Map)
                {
                    part2.AppendLine("**🗺️ Załącznik 3 (Mapa):** Dołączono mapę operacyjną AO (" + d.Annex3MapNotes + ")");
                }

                string s1 = part1.ToString().Trim();
                string s2 = part2.ToString().Trim();

                if (s1.Length + s2.Length + 2 <= 1850)
                {
                    chunks.Add(s1 + "\n\n" + s2);
                }
                else
                {
                    chunks.Add(s1);
                    chunks.Add(s2);
                }
            }

            return chunks;
        }

        public static string BuildCombinedDiscordMarkdown(OpordDocumentModel doc)
        {
            var chunks = BuildCondensedDiscordChunks(doc);
            if (chunks.Count == 0) return "";
            if (chunks.Count == 1) return chunks[0];

            var sb = new StringBuilder();
            for (int i = 0; i < chunks.Count; i++)
            {
                if (i > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("--- ✂️ [WIADOMOŚĆ DISCORD " + (i + 1) + "/" + chunks.Count + "] ✂️ ---");
                    sb.AppendLine();
                }
                sb.AppendLine(chunks[i]);
            }
            return sb.ToString().TrimEnd();
        }

        private static string CompactSingleLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "-";
            return string.Join(" • ", text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0));
        }
    }
}
