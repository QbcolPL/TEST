#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace FieldTakHub.Builder.Services
{
    internal sealed class ExtractedMapPoiItem
    {
        public string Name { get; set; } = "POI";
        public string Type { get; set; } = "POINT";
        public double Lat { get; set; }
        public double Lon { get; set; }
    }

    internal sealed class ExtractedMapSnapshot
    {
        public bool HasAoPolygon { get; set; }
        public double CenterLat { get; set; } = 49.850568;
        public double CenterLon { get; set; } = 18.813347;
        public List<ExtractedMapPoiItem> Pois { get; set; } = new List<ExtractedMapPoiItem>();
    }

    internal sealed class CustomOpordChannelRow
    {
        public string Role { get; set; } = "Sekcja / Zwiad";
        public string Band { get; set; } = "PMR";
        public string Freq { get; set; } = "CH 2 (446.01875 MHz)";
        public string Tone { get; set; } = "CTCSS 77.0 Hz";
    }

    internal static class BuilderOpordV35Enhancements
    {
        private static readonly string[] ColSets = { "ABCDEFGH", "JKLMNPQR", "STUVWXYZ" };
        private static readonly string[] RowSets = { "ABCDEFGHJKLMNPQRSTUV", "FGHJKLMNPQRSTUVABCDE" };

        // =====================================================================
        // 1. POBIERANIE DANYCH Z GENERATORA MAP ORAZ SYSTEMU WSPÓŁRZĘDNYCH
        // =====================================================================
        public static ExtractedMapSnapshot ExtractMapDataSafe(Window win)
        {
            var snap = new ExtractedMapSnapshot();
            try
            {
                var mgrType = typeof(OpordBuilderManager);
                var m = mgrType.GetMethod("GetExtractedMapData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                     ?? mgrType.GetMethod("ExtractMapData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (m != null && m.GetParameters().Length == 0)
                {
                    var res = m.Invoke(null, null);
                    if (res != null)
                    {
                        var rt = res.GetType();
                        snap.HasAoPolygon = (bool)(rt.GetProperty("HasAoPolygon")?.GetValue(res) ?? false);
                        snap.CenterLat = Convert.ToDouble(rt.GetProperty("CenterLat")?.GetValue(res) ?? 49.850568);
                        snap.CenterLon = Convert.ToDouble(rt.GetProperty("CenterLon")?.GetValue(res) ?? 18.813347);
                        if (rt.GetProperty("Pois")?.GetValue(res) is IEnumerable pe)
                        {
                            foreach (var it in pe)
                            {
                                if (it == null) continue;
                                var t = it.GetType();
                                snap.Pois.Add(new ExtractedMapPoiItem
                                {
                                    Name = t.GetProperty("Name")?.GetValue(it)?.ToString() ?? "POI",
                                    Type = t.GetProperty("Type")?.GetValue(it)?.ToString() ?? "POINT",
                                    Lat = Convert.ToDouble(t.GetProperty("Lat")?.GetValue(it) ?? snap.CenterLat),
                                    Lon = Convert.ToDouble(t.GetProperty("Lon")?.GetValue(it) ?? snap.CenterLon)
                                });
                            }
                        }
                    }
                }
            }
            catch { }
            return snap;
        }

        public static string DetectActiveMapCoordinateSystem(Window win)
        {
            try
            {
                foreach (var cb in BuilderPackageAndUiPatcher.GetVisualTreeElements(win).OfType<ComboBox>())
                {
                    string s = (cb.SelectedItem is ComboBoxItem cbi ? cbi.Content?.ToString() : cb.SelectedItem?.ToString()) ?? "";
                    if (s.Contains("MGRS", StringComparison.OrdinalIgnoreCase)) return "MGRS";
                    if (s.Contains("UTM", StringComparison.OrdinalIgnoreCase)) return "UTM";
                    if (s.Contains("DMS", StringComparison.OrdinalIgnoreCase) || s.Contains("Stopnie", StringComparison.OrdinalIgnoreCase)) return "DMS";
                    if (s.Contains("WGS84", StringComparison.OrdinalIgnoreCase) || s.Contains("DD", StringComparison.OrdinalIgnoreCase)) return "WGS84";
                }
            }
            catch { }
            return "MGRS";
        }

        public static string FormatCoordBySystem(double lat, double lon, string coordSys)
        {
            string sys = (coordSys ?? "MGRS").ToUpperInvariant();
            if (sys.Contains("MGRS")) return LatLonToMgrs(lat, lon);
            if (sys.Contains("UTM")) return LatLonToUtm(lat, lon);
            if (sys.Contains("DMS")) return LatLonToDms(lat, lon);
            return string.Format(CultureInfo.InvariantCulture, "{0:F5}°N, {1:F5}°E", lat, lon);
        }

        public static string LatLonToMgrs(double lat, double lon)
        {
            try
            {
                double cLat = Math.Clamp(lat, -80.0, 84.0);
                double nLon = ((lon + 180.0) % 360.0 + 360.0) % 360.0 - 180.0;
                int zone = (int)Math.Floor((nLon + 180.0) / 6.0) + 1;
                double latRad = cLat * Math.PI / 180.0;
                double lonRad = nLon * Math.PI / 180.0;
                double lonOrig = ((zone - 1) * 6 - 180 + 3) * Math.PI / 180.0;
                double a = 6378137.0, eccSq = 0.00669438, k0 = 0.9996, eccP = eccSq / (1.0 - eccSq);
                double n = a / Math.Sqrt(1.0 - eccSq * Math.Sin(latRad) * Math.Sin(latRad));
                double t = Math.Tan(latRad) * Math.Tan(latRad);
                double c = eccP * Math.Cos(latRad) * Math.Cos(latRad);
                double A = Math.Cos(latRad) * (lonRad - lonOrig);
                double M = a * ((1.0 - eccSq / 4.0 - 3.0 * eccSq * eccSq / 64.0) * latRad - (3.0 * eccSq / 8.0 + 3.0 * eccSq * eccSq / 32.0) * Math.Sin(2.0 * latRad));
                double east = k0 * n * (A + (1.0 - t + c) * Math.Pow(A, 3) / 6.0) + 500000.0;
                double north = k0 * (M + n * Math.Tan(latRad) * (A * A / 2.0));
                if (cLat < 0) north += 10000000.0;
                string bands = "CDEFGHJKLMNPQRSTUVWXX";
                char bChar = bands[Math.Clamp((int)Math.Floor((cLat + 80.0) / 8.0), 0, bands.Length - 1)];
                char cChar = ColSets[(zone - 1) % 3][Math.Clamp((int)Math.Floor(east / 100000.0) - 1, 0, 7)];
                char rChar = RowSets[(zone - 1) % 2][Math.Clamp((int)Math.Floor(north / 100000.0) % 20, 0, 19)];
                int e5 = Math.Clamp((int)Math.Round(east) % 100000, 0, 99999);
                int n5 = Math.Clamp((int)Math.Round(north) % 100000, 0, 99999);
                return string.Format(CultureInfo.InvariantCulture, "{0:00}{1} {2}{3} {4:00000} {5:00000}", zone, bChar, cChar, rChar, e5, n5);
            }
            catch { return string.Format(CultureInfo.InvariantCulture, "{0:F5}, {1:F5}", lat, lon); }
        }

        private static string LatLonToUtm(double lat, double lon)
        {
            double cLat = Math.Clamp(lat, -80.0, 84.0);
            double nLon = ((lon + 180.0) % 360.0 + 360.0) % 360.0 - 180.0;
            int zone = (int)Math.Floor((nLon + 180.0) / 6.0) + 1;
            double latRad = cLat * Math.PI / 180.0, lonRad = nLon * Math.PI / 180.0;
            double lonOrig = ((zone - 1) * 6 - 180 + 3) * Math.PI / 180.0;
            double a = 6378137.0, eccSq = 0.00669438, k0 = 0.9996;
            double n = a / Math.Sqrt(1.0 - eccSq * Math.Sin(latRad) * Math.Sin(latRad));
            double A = Math.Cos(latRad) * (lonRad - lonOrig);
            double M = a * ((1.0 - eccSq / 4.0) * latRad - (3.0 * eccSq / 8.0) * Math.Sin(2.0 * latRad));
            double east = k0 * n * A + 500000.0;
            double north = k0 * (M + n * Math.Tan(latRad) * (A * A / 2.0));
            return string.Format(CultureInfo.InvariantCulture, "UTM {0}N {1:0}E {2:0}N", zone, east, north);
        }

        private static string LatLonToDms(double lat, double lon)
        {
            return ToDmsPart(lat, lat >= 0 ? 'N' : 'S') + " " + ToDmsPart(lon, lon >= 0 ? 'E' : 'W');
        }

        private static string ToDmsPart(double v, char h)
        {
            double a = Math.Abs(v);
            int d = (int)Math.Floor(a);
            double mf = (a - d) * 60.0;
            int m = (int)Math.Floor(mf);
            double s = (mf - m) * 60.0;
            return string.Format(CultureInfo.InvariantCulture, "{0}°{1:00}'{2:00.0}\"{3}", d, m, s, h);
        }

        // =====================================================================
        // 2. PEŁNA SYNCHRONIZACJA Z GENERATORA STRON I GENERATORA MAP
        // =====================================================================
        public static void SyncAllFromGenerators(Window win, OpordParagraphModel model, OpordDayPlanModel activeDay)
        {
            if (model == null || activeDay == null) return;

            var snap = FactionLiveSyncBridge.CaptureCurrentFactionSnapshot(win, model.TargetFaction);
            if (!string.IsNullOrWhiteSpace(snap.FactionName)) model.TargetFaction = snap.FactionName;
            if (!string.IsNullOrWhiteSpace(snap.EventName)) model.EventName = snap.EventName;

            if (snap.VoiceChannels.Count > 0)
            {
                var c0 = snap.VoiceChannels[0];
                activeDay.PrimaryChannel = $"{c0.Role}: {c0.Freq} [{c0.Band}] ({c0.Tone})";
                if (snap.VoiceChannels.Count > 1)
                {
                    var c1 = snap.VoiceChannels[1];
                    activeDay.AlternateChannel = $"{c1.Role}: {c1.Freq} [{c1.Band}] ({c1.Tone})";
                }
                else
                {
                    activeDay.AlternateChannel = activeDay.PrimaryChannel;
                }

                if (snap.VoiceChannels.Count > 2)
                {
                    var c2 = snap.VoiceChannels[2];
                    activeDay.HqChannel = $"{c2.Role}: {c2.Freq} [{c2.Band}] ({c2.Tone})";
                }
                else
                {
                    activeDay.HqChannel = activeDay.PrimaryChannel;
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"📡 [ZACIĄGNIĘTE Z GENERATORA STRON KANAŁY RADIOWE I MESHTASTIC DLA: {snap.FactionName}]");
            sb.AppendLine($"• Meshtastic LoRa: Kanał '{snap.ChannelName}' | Region: {snap.Region} | Preset: {snap.ModemPreset} | Szyfrowanie: AES-256 PSK");
            sb.AppendLine($"• Serwer ATAK (OpenTAKServer): {(string.IsNullOrWhiteSpace(snap.OtsHost) ? "Tryb lokalny / Off-Grid Mesh" : snap.OtsHost)}");
            sb.AppendLine($"• Telefon alarmowy ORGA / MEDEVAC (SOS MGRS): {snap.EmergencyPhone}");
            sb.AppendLine("• Wszystkie kanały głosowe zdefiniowane w Generatorze Stron:");
            foreach (var vc in snap.VoiceChannels)
                sb.AppendLine($"   - {vc.Role} [{vc.Band}]: {vc.Freq} ({vc.Tone})");
            activeDay.SyncedCommsMatrixSummary = sb.ToString().TrimEnd();

            // Zsynchronizuj wszystkie właściwości tekstowe łączności w klasie OpordDayPlanModel (przez refleksję)
            foreach (var prop in activeDay.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (prop.PropertyType != typeof(string) || !prop.CanWrite) continue;
                string pn = prop.Name;
                if (pn.Contains("Primary", StringComparison.OrdinalIgnoreCase) && (pn.Contains("Chan", StringComparison.OrdinalIgnoreCase) || pn.Contains("Freq", StringComparison.OrdinalIgnoreCase)))
                    prop.SetValue(activeDay, activeDay.PrimaryChannel);
                else if ((pn.Contains("Alternate", StringComparison.OrdinalIgnoreCase) || pn.Contains("Backup", StringComparison.OrdinalIgnoreCase)) && (pn.Contains("Chan", StringComparison.OrdinalIgnoreCase) || pn.Contains("Freq", StringComparison.OrdinalIgnoreCase)))
                    prop.SetValue(activeDay, activeDay.AlternateChannel);
                else if (pn.Contains("Hq", StringComparison.OrdinalIgnoreCase) && (pn.Contains("Chan", StringComparison.OrdinalIgnoreCase) || pn.Contains("Freq", StringComparison.OrdinalIgnoreCase)))
                    prop.SetValue(activeDay, activeDay.HqChannel);
                else if (pn.Contains("Synced", StringComparison.OrdinalIgnoreCase) && pn.Contains("Comms", StringComparison.OrdinalIgnoreCase))
                    prop.SetValue(activeDay, activeDay.SyncedCommsMatrixSummary);
            }

            // Współrzędne z Generatora Map w wybranym układzie (np. MGRS)
            string coordSys = DetectActiveMapCoordinateSystem(win);
            var md = ExtractMapDataSafe(win);
            if (md != null && (md.HasAoPolygon || md.Pois.Count > 0))
            {
                activeDay.LocationLat = md.CenterLat.ToString("F6", CultureInfo.InvariantCulture);
                activeDay.LocationLon = md.CenterLon.ToString("F6", CultureInfo.InvariantCulture);
                string centerCoord = FormatCoordBySystem(md.CenterLat, md.CenterLon, coordSys);

                if (string.IsNullOrWhiteSpace(activeDay.LocationName))
                {
                    activeDay.LocationName = $"Rejon AO ({coordSys}: {centerCoord})";
                }

                var psb = new StringBuilder();
                psb.AppendLine($"Układ współrzędnych z Generatora Map: {coordSys} | Środek AO: {centerCoord}");
                foreach (var p in md.Pois)
                {
                    psb.AppendLine($"• {p.Name} [{p.Type}]: {FormatCoordBySystem(p.Lat, p.Lon, coordSys)}");
                }
                activeDay.AreaOfInterestPoiDesc = psb.ToString().TrimEnd();
            }
        }

        // =====================================================================
        // 3. PRZEBUDOWA INTERFEJSU FORMULARZA OPORD NA ŻYWO
        // =====================================================================
        public static void AttachOpordUiExtensions(
            Window win,
            OpordParagraphModel model,
            OpordDayPlanModel activeDay,
            StackPanel mainStack,
            Action rebuildCallback)
        {
            if (mainStack == null || model == null || activeDay == null) return;

            // Zawsze odświeżaj dane z Generatora Stron na żywo przy budowaniu widoku OPORD!
            SyncAllFromGenerators(win, model, activeDay);

            var all = BuilderPackageAndUiPatcher.GetVisualTreeElements(mainStack);

            // A. Odśwież listę wyboru strony konfliktu (ComboBox) bez konieczności rozwijania menu
            var liveFactionNames = FactionLiveSyncBridge.GetAllFactionNamesFromUi(win);
            foreach (var cb in all.OfType<ComboBox>())
            {
                bool isFacCombo = cb.Items.Cast<object>().Any(x =>
                    x?.ToString()?.Contains("BLUFOR", StringComparison.OrdinalIgnoreCase) == true ||
                    x?.ToString()?.Contains("OPFOR", StringComparison.OrdinalIgnoreCase) == true ||
                    string.Equals(x?.ToString(), model.TargetFaction, StringComparison.OrdinalIgnoreCase));

                if (isFacCombo)
                {
                    string currentSel = model.TargetFaction;
                    cb.Items.Clear();
                    foreach (string fn in liveFactionNames)
                    {
                        cb.Items.Add(fn);
                    }
                    if (!string.IsNullOrWhiteSpace(currentSel) && !liveFactionNames.Contains(currentSel, StringComparer.OrdinalIgnoreCase))
                    {
                        cb.Items.Insert(0, currentSel);
                    }
                    cb.SelectedItem = currentSel;
                    cb.SelectionChanged += (_, __) =>
                    {
                        if (cb.SelectedItem != null)
                        {
                            model.TargetFaction = cb.SelectedItem.ToString();
                            SyncAllFromGenerators(win, model, activeDay);
                            UpdateSectionEControlsInPlace(mainStack, activeDay);
                        }
                    };
                    break;
                }
            }

            // B. Dodaj przycisk "🔄 Pobierz z Generatora Stron" na samą górę do karty "GENERATOR ROZKAZU BOJOWEGO (OPORD)"
            if (mainStack.Children.Count > 0 && mainStack.Children[0] is Border topCard && topCard.Child is StackPanel topStack)
            {
                var topSyncBtn = new Button
                {
                    Content = BuilderI18n.T(
                        "🔄 Pobierz z Generatora Stron i Mapy 1:1 (Odśwież nazwy stron, Tabelę E Łączności i współrzędne MGRS)",
                        "🔄 Pull from Faction & Map Generators (Refresh Factions, Table E Comms & MGRS Coords)"),
                    Height = 36,
                    Margin = new Thickness(0, 10, 0, 2),
                    Padding = new Thickness(16, 6, 16, 6),
                    Background = new SolidColorBrush(Color.FromRgb(14, 52, 64)),
                    Foreground = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                    BorderThickness = new Thickness(1.5),
                    FontWeight = FontWeights.Bold,
                    Cursor = Cursors.Hand
                };
                topSyncBtn.Click += (_, __) =>
                {
                    SyncAllFromGenerators(win, model, activeDay);
                    rebuildCallback?.Invoke();
                };
                topStack.Children.Add(topSyncBtn);
            }

            // C. Zaktualizuj kontrolki w Sekcji E (Kanał główny, zapasowy, HQ + zielone podsumowanie),
            //    ukryj stary przycisk "Pobierz z Generatora Stron" z Sekcji E i wstaw pod Tabelą E opcjonalną tabelę własnych kanałów ("na ptaszek")!
            UpdateSectionEControlsInPlace(mainStack, activeDay);
            InjectCustomCommsTableIntoSectionE(mainStack, activeDay);

            // D. Podłącz twardą walidację METOC (zdejmujemy stary Click i nakładamy walidację pustego pola + braku AO)
            //    oraz podłącz nowe okno skondensowanego eksportu Discord
            PatchMetocAndDiscordButtons(win, model, activeDay, all);

            // E. Wstaw opcjonalny Załącznik nr 3 — Mapa Taktyczna / Szkic AO ("na ptaszek")
            InjectAnnex3MapCard(win, activeDay, mainStack);
        }

        private static void UpdateSectionEControlsInPlace(StackPanel mainStack, OpordDayPlanModel activeDay)
        {
            var all = BuilderPackageAndUiPatcher.GetVisualTreeElements(mainStack);

            foreach (var tb in all.OfType<TextBox>())
            {
                var parent = VisualTreeHelper.GetParent(tb) as Panel;
                if (parent == null) continue;
                string lbl = string.Join(" ", parent.Children.OfType<TextBlock>().Select(x => x.Text ?? ""));
                if (lbl.Contains("Kanał główny", StringComparison.OrdinalIgnoreCase) || lbl.Contains("Primary channel", StringComparison.OrdinalIgnoreCase))
                {
                    tb.Text = activeDay.PrimaryChannel;
                }
                else if (lbl.Contains("Kanał zapasowy", StringComparison.OrdinalIgnoreCase) || lbl.Contains("Alternate channel", StringComparison.OrdinalIgnoreCase))
                {
                    tb.Text = activeDay.AlternateChannel;
                }
                else if (lbl.Contains("Kanał HQ", StringComparison.OrdinalIgnoreCase) || lbl.Contains("HQ channel", StringComparison.OrdinalIgnoreCase))
                {
                    tb.Text = activeDay.HqChannel;
                }
            }

            foreach (var txtBlock in all.OfType<TextBlock>())
            {
                string t = txtBlock.Text ?? "";
                if (t.Contains("[ZACIĄGNIĘTE Z GENERATORA STRON", StringComparison.OrdinalIgnoreCase) ||
                    t.Contains("Meshtastic LoRa:", StringComparison.OrdinalIgnoreCase))
                {
                    txtBlock.Text = activeDay.SyncedCommsMatrixSummary;
                }
            }

            // Ukryj stary przycisk "Pobierz z Generatora Stron" znajdujący się w Sekcji E (ponieważ został przeniesiony na samą górę)
            bool firstTopSyncSeen = false;
            foreach (var btn in all.OfType<Button>())
            {
                string bt = (btn.Content ?? "").ToString();
                if (bt.Contains("Pobierz z Generatora Stron", StringComparison.OrdinalIgnoreCase) ||
                    bt.Contains("Pull from Faction", StringComparison.OrdinalIgnoreCase))
                {
                    if (!firstTopSyncSeen)
                    {
                        firstTopSyncSeen = true; // Pierwszy to nasz nowy przycisk na samej górze
                    }
                    else
                    {
                        btn.Visibility = Visibility.Collapsed;
                        btn.Width = 0;
                        btn.Height = 0;
                    }
                }
            }
        }

        private static void InjectCustomCommsTableIntoSectionE(StackPanel mainStack, OpordDayPlanModel activeDay)
        {
            var all = BuilderPackageAndUiPatcher.GetVisualTreeElements(mainStack);
            TextBlock sectionEHeader = all.OfType<TextBlock>().FirstOrDefault(t =>
                (t.Text ?? "").Contains("E. Tabela łączności", StringComparison.OrdinalIgnoreCase) ||
                (t.Text ?? "").Contains("E. Comms Table", StringComparison.OrdinalIgnoreCase));

            Panel targetPanel = null;
            if (sectionEHeader != null)
            {
                DependencyObject p = VisualTreeHelper.GetParent(sectionEHeader);
                while (p != null && !(p is StackPanel)) p = VisualTreeHelper.GetParent(p);
                targetPanel = p as StackPanel;
            }
            if (targetPanel == null) return;
            if (targetPanel.Tag?.ToString() == "FTH_SEC_E_EXTENDED") return;
            targetPanel.Tag = "FTH_SEC_E_EXTENDED";

            var customCommsContainer = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(8, 22, 28)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 8, 0, 10)
            };

            var st = new StackPanel();
            var chkCustom = new CheckBox
            {
                Content = BuilderI18n.T(
                    "☑️ Dodaj inne kanały ręcznie (opcjonalna tabela dodatkowych kanałów w OPORD)",
                    "☑️ Add other channels manually (optional custom channels table in OPORD)"),
                IsChecked = activeDay.IncludeCustomChannelsTable,
                Foreground = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = Cursors.Hand
            };
            st.Children.Add(chkCustom);

            var tableBodyPanel = new StackPanel
            {
                Visibility = activeDay.IncludeCustomChannelsTable ? Visibility.Visible : Visibility.Collapsed
            };

            var rowsHost = new StackPanel();
            var parsedRows = ParseCustomChannelRows(activeDay.CustomChannelsTableText);

            void SyncRowsBackToModel()
            {
                var lines = new List<string>();
                foreach (Grid rowGrid in rowsHost.Children.OfType<Grid>())
                {
                    var boxes = rowGrid.Children.OfType<TextBox>().ToList();
                    if (boxes.Count >= 4)
                    {
                        string r = boxes[0].Text.Trim();
                        string b = boxes[1].Text.Trim();
                        string f = boxes[2].Text.Trim();
                        string tn = boxes[3].Text.Trim();
                        if (!string.IsNullOrWhiteSpace(r) || !string.IsNullOrWhiteSpace(f))
                        {
                            lines.Add($"• {r} [{b}]: {f} ({tn})");
                        }
                    }
                }
                activeDay.CustomChannelsTableText = string.Join("\n", lines);
            }

            void AddVisualChannelRow(CustomOpordChannelRow rowData)
            {
                var g = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                TextBox MakeCell(string text, int col, string tip)
                {
                    var tb = new TextBox
                    {
                        Text = text,
                        ToolTip = tip,
                        Padding = new Thickness(6, 4, 6, 4),
                        Margin = new Thickness(0, 0, 6, 0),
                        Background = new SolidColorBrush(Color.FromRgb(13, 33, 41)),
                        Foreground = Brushes.White,
                        BorderBrush = new SolidColorBrush(Color.FromRgb(34, 74, 89)),
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 11.5
                    };
                    tb.TextChanged += (_, __) => SyncRowsBackToModel();
                    Grid.SetColumn(tb, col);
                    return tb;
                }

                g.Children.Add(MakeCell(rowData.Role, 0, "Rola / Przeznaczenie kanału (np. Zwiad / Alfa)"));
                g.Children.Add(MakeCell(rowData.Band, 1, "Pasmo (np. PMR / VHF / UHF / DMR)"));
                g.Children.Add(MakeCell(rowData.Freq, 2, "Częstotliwość / Kanał (np. CH 2 / 446.01875 MHz)"));
                g.Children.Add(MakeCell(rowData.Tone, 3, "Ton CTCSS / Kod DMR"));

                var btnDel = new Button
                {
                    Content = "✕",
                    Width = 28,
                    Height = 26,
                    Background = new SolidColorBrush(Color.FromRgb(64, 18, 18)),
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    Cursor = Cursors.Hand
                };
                btnDel.Click += (_, __) =>
                {
                    rowsHost.Children.Remove(g);
                    SyncRowsBackToModel();
                };
                Grid.SetColumn(btnDel, 4);
                g.Children.Add(btnDel);

                rowsHost.Children.Add(g);
            }

            foreach (var r in parsedRows) AddVisualChannelRow(r);

            var btnAddRow = new Button
            {
                Content = BuilderI18n.T("➕ Dodaj kolejny kanał do tabeli", "➕ Add Custom Channel Row"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12, 4, 12, 4),
                Margin = new Thickness(0, 4, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(18, 48, 58)),
                Foreground = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            btnAddRow.Click += (_, __) =>
            {
                AddVisualChannelRow(new CustomOpordChannelRow { Role = "Kanał dodatkowy", Band = "PMR", Freq = "CH 5 (446.05625 MHz)", Tone = "CTCSS 110.9 Hz" });
                SyncRowsBackToModel();
            };

            tableBodyPanel.Children.Add(rowsHost);
            tableBodyPanel.Children.Add(btnAddRow);
            st.Children.Add(tableBodyPanel);

            chkCustom.Checked += (_, __) =>
            {
                activeDay.IncludeCustomChannelsTable = true;
                tableBodyPanel.Visibility = Visibility.Visible;
                SyncRowsBackToModel();
            };
            chkCustom.Unchecked += (_, __) =>
            {
                activeDay.IncludeCustomChannelsTable = false;
                tableBodyPanel.Visibility = Visibility.Collapsed;
            };

            customCommsContainer.Child = st;

            // Wstaw kontener zaraz pod zielonym podsumowaniem łączności w Sekcji E
            int insertPos = targetPanel.Children.Count;
            for (int i = 0; i < targetPanel.Children.Count; i++)
            {
                if (targetPanel.Children[i] is TextBlock tb && (tb.Text ?? "").Contains("F.1.", StringComparison.OrdinalIgnoreCase))
                {
                    insertPos = i;
                    break;
                }
            }
            targetPanel.Children.Insert(insertPos, customCommsContainer);
        }

        private static List<CustomOpordChannelRow> ParseCustomChannelRows(string rawText)
        {
            var list = new List<CustomOpordChannelRow>();
            if (!string.IsNullOrWhiteSpace(rawText))
            {
                var lines = rawText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string line in lines)
                {
                    string clean = line.Trim().TrimStart('•', '-', '*').Trim();
                    if (string.IsNullOrWhiteSpace(clean)) continue;
                    list.Add(new CustomOpordChannelRow
                    {
                        Role = clean,
                        Band = "PMR/UHF",
                        Freq = "Wg ustaleń",
                        Tone = "-"
                    });
                }
            }
            if (list.Count == 0)
            {
                list.Add(new CustomOpordChannelRow { Role = "Kanał Sekcji Alfa", Band = "PMR", Freq = "CH 2 (446.01875 MHz)", Tone = "CTCSS 77.0 Hz" });
                list.Add(new CustomOpordChannelRow { Role = "Kanał Zwiadu / Snajper", Band = "PMR", Freq = "CH 5 (446.05625 MHz)", Tone = "CTCSS 110.9 Hz" });
            }
            return list;
        }

        private static void PatchMetocAndDiscordButtons(
            Window win,
            OpordParagraphModel model,
            OpordDayPlanModel activeDay,
            List<DependencyObject> allElements)
        {
            // Znajdź pole tekstowe lokalizacji w I. SYTUACJA, aby sprawdzać na żywo co wpisał użytkownik
            TextBox locationTextBox = null;
            foreach (var tb in allElements.OfType<TextBox>())
            {
                var p = VisualTreeHelper.GetParent(tb) as Panel;
                if (p != null && p.Children.OfType<TextBlock>().Any(l =>
                    (l.Text ?? "").Contains("Lokalizacja", StringComparison.OrdinalIgnoreCase) ||
                    (l.Text ?? "").Contains("Obszar gry", StringComparison.OrdinalIgnoreCase)))
                {
                    locationTextBox = tb;
                    break;
                }
            }

            foreach (var btn in allElements.OfType<Button>())
            {
                string bt = (btn.Content ?? "").ToString();

                // 1. Przycisk METOC — zastąp zachowanie, gdy pole lokalizacji jest puste i brak AO w Generatorze Map
                if (bt.Contains("METOC", StringComparison.OrdinalIgnoreCase))
                {
                    // Zastąp przycisk w drzewie wizualnym klonem kontrolowanym przez walidator
                    var parent = VisualTreeHelper.GetParent(btn) as Panel;
                    if (parent != null && btn.Tag?.ToString() != "FTH_METOC_GUARDED")
                    {
                        btn.Tag = "FTH_METOC_GUARDED";
                        Button origMetocBtn = btn;

                        var guardedBtn = new Button
                        {
                            Content = origMetocBtn.Content,
                            Height = origMetocBtn.Height,
                            Margin = origMetocBtn.Margin,
                            Padding = origMetocBtn.Padding,
                            Background = origMetocBtn.Background,
                            Foreground = origMetocBtn.Foreground,
                            BorderBrush = origMetocBtn.BorderBrush,
                            BorderThickness = origMetocBtn.BorderThickness,
                            FontWeight = origMetocBtn.FontWeight,
                            Cursor = Cursors.Hand,
                            Tag = "FTH_METOC_GUARDED"
                        };

                        if (parent is Grid)
                        {
                            Grid.SetRow(guardedBtn, Grid.GetRow(origMetocBtn));
                            Grid.SetColumn(guardedBtn, Grid.GetColumn(origMetocBtn));
                        }

                        origMetocBtn.Visibility = Visibility.Collapsed;
                        origMetocBtn.Width = 0;

                        guardedBtn.Click += (_, __) =>
                        {
                            string currentLocText = (locationTextBox != null ? locationTextBox.Text : activeDay.LocationName) ?? "";
                            currentLocText = currentLocText.Trim();

                            var mapData = ExtractMapDataSafe(win);
                            bool hasMapAo = mapData != null && (mapData.HasAoPolygon || mapData.Pois.Count > 0);

                            bool isLocEmptyOrDefault =
                                string.IsNullOrWhiteSpace(currentLocText) ||
                                currentLocText.Equals("Czechowice-Dziedzice / Rejon AO", StringComparison.OrdinalIgnoreCase) ||
                                currentLocText.Equals("Rejon AO", StringComparison.OrdinalIgnoreCase);

                            if (isLocEmptyOrDefault && !hasMapAo)
                            {
                                MessageBox.Show(
                                    BuilderI18n.T(
                                        "⚠️ Nie można pobrać prognozy METOC z pustego obszaru!\n\n" +
                                        "Proszę wpisać nazwę obszaru/miejscowości w polu 'Lokalizacja / Obszar gry (AO)' LUB zaznaczyć granicę terenu gry (AO) w module Generator Map 1:1.",
                                        "⚠️ Cannot fetch METOC report without a defined area!\n\n" +
                                        "Please enter an area name in the 'Location / Game Area (AO)' field OR draw an AO polygon in the 1:1 Map Generator."),
                                    BuilderI18n.T("Błąd METOC — Brak wpisanego obszaru i brak AO na mapie", "METOC Error — Missing Area & Map AO"),
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Error);
                                return;
                            }

                            origMetocBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        };

                        parent.Children.Add(guardedBtn);
                    }
                }

                // 2. Przycisk Kopiuj na Discorda — otwórz okno ze skondensowanym formatem i podziałem na wiadomości
                if (bt.Contains("Discord", StringComparison.OrdinalIgnoreCase))
                {
                    var parent = VisualTreeHelper.GetParent(btn) as Panel;
                    if (parent != null && btn.Tag?.ToString() != "FTH_DISCORD_GUARDED")
                    {
                        btn.Tag = "FTH_DISCORD_GUARDED";
                        Button origDiscordBtn = btn;

                        var newDiscordBtn = new Button
                        {
                            Content = BuilderI18n.T("💬 Kopiuj na Discorda (Skondensowany + Podział)", "💬 Copy for Discord (Condensed + Split)"),
                            Height = origDiscordBtn.Height,
                            Margin = origDiscordBtn.Margin,
                            Padding = origDiscordBtn.Padding,
                            Background = origDiscordBtn.Background,
                            Foreground = origDiscordBtn.Foreground,
                            BorderBrush = origDiscordBtn.BorderBrush,
                            BorderThickness = origDiscordBtn.BorderThickness,
                            FontWeight = origDiscordBtn.FontWeight,
                            Cursor = Cursors.Hand,
                            Tag = "FTH_DISCORD_GUARDED"
                        };

                        origDiscordBtn.Visibility = Visibility.Collapsed;
                        origDiscordBtn.Width = 0;

                        newDiscordBtn.Click += (_, __) =>
                        {
                            SyncAllFromGenerators(win, model, activeDay);
                            ShowCondensedDiscordModal(win, model);
                        };

                        parent.Children.Add(newDiscordBtn);
                    }
                }
            }
        }

        private static void InjectAnnex3MapCard(Window win, OpordDayPlanModel d, StackPanel mainStack)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(10, 24, 30)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(250, 208, 44)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 12)
            };

            var st = new StackPanel();
            var cbMap = new CheckBox
            {
                Content = BuilderI18n.T(
                    $"🗺️ Dołącz ZAŁĄCZNIK 3 — Mapa Operacyjna / Szkic AO do rozkazu OPORD ({d.DayTitle})",
                    $"🗺️ Include ANNEX 3 — Operational Map / AO Sketch in OPORD ({d.DayTitle})"),
                IsChecked = d.IncludeAnnex3Map,
                Foreground = new SolidColorBrush(Color.FromRgb(250, 208, 44)),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 6),
                Cursor = Cursors.Hand
            };
            st.Children.Add(cbMap);

            var detailsPanel = new StackPanel
            {
                Visibility = d.IncludeAnnex3Map ? Visibility.Visible : Visibility.Collapsed
            };

            var pathGrid = new Grid { Margin = new Thickness(0, 4, 0, 6) };
            pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var pathBox = new TextBox
            {
                Text = d.Annex3MapImagePath,
                Padding = new Thickness(6),
                Background = new SolidColorBrush(Color.FromRgb(13, 33, 41)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(34, 74, 89)),
                FontSize = 11.5
            };
            pathBox.TextChanged += (_, __) => d.Annex3MapImagePath = pathBox.Text;
            Grid.SetColumn(pathBox, 0);
            pathGrid.Children.Add(pathBox);

            var btnPick = new Button
            {
                Content = BuilderI18n.T("📂 Wybierz plik mapy (.PNG / .JPG)", "📂 Select Map Image (.PNG / .JPG)"),
                Padding = new Thickness(12, 4, 12, 4),
                Margin = new Thickness(8, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(22, 54, 66)),
                Foreground = new SolidColorBrush(Color.FromRgb(250, 208, 44)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(250, 208, 44)),
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            btnPick.Click += (_, __) =>
            {
                var ofd = new OpenFileDialog
                {
                    Filter = "Obrazy map (*.png;*.jpg;*.jpeg;*.webp;*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.bmp|Wszystkie pliki (*.*)|*.*"
                };
                if (ofd.ShowDialog(win) == true)
                {
                    d.Annex3MapImagePath = ofd.FileName;
                    pathBox.Text = ofd.FileName;
                }
            };
            Grid.SetColumn(btnPick, 1);
            pathGrid.Children.Add(btnPick);
            detailsPanel.Children.Add(pathGrid);

            var notesBox = new TextBox
            {
                Text = d.Annex3MapNotes,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 44,
                Padding = new Thickness(6),
                Background = new SolidColorBrush(Color.FromRgb(13, 33, 41)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(34, 74, 89)),
                FontSize = 11.5
            };
            notesBox.TextChanged += (_, __) => d.Annex3MapNotes = notesBox.Text;
            detailsPanel.Children.Add(notesBox);

            cbMap.Checked += (_, __) => { d.IncludeAnnex3Map = true; detailsPanel.Visibility = Visibility.Visible; };
            cbMap.Unchecked += (_, __) => { d.IncludeAnnex3Map = false; detailsPanel.Visibility = Visibility.Collapsed; };

            st.Children.Add(detailsPanel);
            card.Child = st;

            int insertIdx = Math.Max(0, mainStack.Children.Count - 1);
            mainStack.Children.Insert(insertIdx, card);
        }

        public static string BuildAnnex3AndCustomCommsHtml(OpordDayPlanModel d)
        {
            if (d == null) return "";
            var sb = new StringBuilder();
            if (d.IncludeCustomChannelsTable && !string.IsNullOrWhiteSpace(d.CustomChannelsTableText))
            {
                sb.AppendLine("<div class='sec'>DODATKOWE KANAŁY RADIOWE (OPCJA RĘCZNA OPORD)</div>");
                sb.AppendLine($"<div class='box'>{WebUtility.HtmlEncode(d.CustomChannelsTableText)}</div>");
            }
            if (d.IncludeAnnex3Map)
            {
                sb.AppendLine($"<div class='sec'>ZAŁĄCZNIK 3 — MAPA OPERACYJNA / SZKIC AO ({WebUtility.HtmlEncode(d.DayTitle)})</div>");
                sb.AppendLine($"<div class='box'>{WebUtility.HtmlEncode(d.Annex3MapNotes)}</div>");
                if (!string.IsNullOrWhiteSpace(d.Annex3MapImagePath) && File.Exists(d.Annex3MapImagePath))
                {
                    try
                    {
                        byte[] b = File.ReadAllBytes(d.Annex3MapImagePath);
                        string ext = Path.GetExtension(d.Annex3MapImagePath).ToLowerInvariant();
                        string mime = (ext == ".jpg" || ext == ".jpeg") ? "image/jpeg" : "image/png";
                        sb.AppendLine($"<div style='text-align:center;margin-top:8px;'><img src='data:{mime};base64,{Convert.ToBase64String(b)}' style='max-width:100%;max-height:210mm;border:2px solid #1e3a46;border-radius:6px;'/></div>");
                    }
                    catch { }
                }
                else
                {
                    sb.AppendLine($"<div class='box'>{WebUtility.HtmlEncode(d.AreaOfInterestPoiDesc)}</div>");
                }
            }
            return sb.ToString();
        }

        // =====================================================================
        // 4. SKONDENSOWANY EKSPORT NA DISCORDA Z PODZIAŁEM NA CZĘŚCI (<1900 ZN.)
        // =====================================================================
        public static string BuildCondensedDiscordMarkdown(OpordParagraphModel m)
        {
            var chunks = BuildCondensedDiscordChunks(m);
            if (chunks.Count == 0) return "";
            if (chunks.Count == 1) return chunks[0];

            var sb = new StringBuilder();
            for (int i = 0; i < chunks.Count; i++)
            {
                if (i > 0) sb.AppendLine($"\n--- ✂️ [WIADOMOŚĆ {i + 1}/{chunks.Count} — KOPIUJ OSOBNO PRZY LIMICIE DISCORDA] ✂️ ---\n");
                sb.AppendLine(chunks[i]);
            }
            return sb.ToString().TrimEnd();
        }

        public static List<string> BuildCondensedDiscordChunks(OpordParagraphModel m)
        {
            var chunks = new List<string>();
            if (m == null || m.Days == null || m.Days.Count == 0) return chunks;

            var days = m.Days.Where(x => x.IncludeInExport).ToList();
            if (days.Count == 0) days.Add(m.Days[Math.Clamp(m.ActiveDayIndex, 0, m.Days.Count - 1)]);

            foreach (var d in days)
            {
                string mgrsInfo = "";
                if (double.TryParse(d.LocationLat, NumberStyles.Any, CultureInfo.InvariantCulture, out double lat) &&
                    double.TryParse(d.LocationLon, NumberStyles.Any, CultureInfo.InvariantCulture, out double lon))
                {
                    mgrsInfo = $" | **MGRS:** `{LatLonToMgrs(lat, lon)}`";
                }

                var b1 = new StringBuilder();
                b1.AppendLine($"## 📋 {d.OperationNumber} | {m.EventName} ({m.TargetFaction}) — {d.DayTitle} ({d.DateIso})");
                b1.AppendLine($"`{d.Classification}` | **HQ:** {d.IssuingHq} | **AO:** {d.LocationName}{mgrsInfo}");
                b1.AppendLine($"**⏱️ Harmonogram:** {CompactInline(d.DaySpecificTasks)}");
                b1.AppendLine($"**🌦️ METOC:** {CompactInline(d.TerrainAndWeather)}");
                b1.AppendLine($"**🎯 II. MISJA (5W):**\n> {d.MissionFullStatement}");
                if (!string.IsNullOrWhiteSpace(d.AdditionalTasksSummary))
                    b1.AppendLine($"**Zadania dodatkowe:** {CompactInline(d.AdditionalTasksSummary)}");
                b1.AppendLine($"**⚡ III. ZAMIAR:** {d.CommandersIntent} | **Dowodzenie:** `{d.SuccessionOfCommand}`");

                var b2 = new StringBuilder();
                b2.AppendLine($"### 📡 ŁĄCZNOŚĆ, SARNEG & LOGISTYKA — {d.DayTitle} ({m.TargetFaction})");
                b2.AppendLine($"• **Główny:** `{d.PrimaryChannel}`\n• **Zapas:** `{d.AlternateChannel}`\n• **HQ:** `{d.HqChannel}`");
                if (d.IncludeCustomChannelsTable && !string.IsNullOrWhiteSpace(d.CustomChannelsTableText))
                    b2.AppendLine($"• **Kanały dodatkowe:**\n{d.CustomChannelsTableText.Trim()}");

                string sWord = SarnegDictionaryService.NormalizePolishChars(d.SarnegKeyWord10).PadRight(10, 'X').Substring(0, 10);
                var lRow = new StringBuilder();
                var dRow = new StringBuilder();
                for (int i = 0; i < 10; i++) { lRow.Append(sWord[i]).Append(' '); dRow.Append(i).Append(' '); }
                b2.AppendLine($"**🔐 SARNEG ({d.DayTitle}): `{sWord}`**\n```text\nLIT: {lRow.ToString().TrimEnd()}\nCYF: {dRow.ToString().TrimEnd()}\n```");
                b2.AppendLine($"**🎯 Kryptonimy:** {d.TargetCodeNames} | **🔑 Hasła:** {CompactInline(d.ChallengeAndPassword)}");
                b2.AppendLine($"**🚑 MEDEVAC:** {d.MedevacRules}");
                b2.AppendLine($"**🎒 Szpej:** {d.SquadGearChecklist}");

                string s1 = b1.ToString().Trim();
                string s2 = b2.ToString().Trim();
                if (s1.Length + s2.Length + 2 <= 1850)
                    chunks.Add(s1 + "\n\n" + s2);
                else
                {
                    chunks.Add(s1);
                    chunks.Add(s2);
                }
            }
            return chunks;
        }

        private static string CompactInline(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "-";
            var parts = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(x => x.Trim())
                            .Where(x => !string.IsNullOrEmpty(x));
            return string.Join(" • ", parts);
        }

        public static void ShowCondensedDiscordModal(Window win, OpordParagraphModel model)
        {
            var chunks = BuildCondensedDiscordChunks(model);
            string full = BuildCondensedDiscordMarkdown(model);
            Clipboard.SetText(full);

            var dlg = new Window
            {
                Title = BuilderI18n.T("💬 Skondensowany Eksport na Discorda (Gotowy do wklejenia / podziału)", "💬 Condensed Discord Export"),
                Width = 660,
                Height = 500,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = win,
                Background = new SolidColorBrush(Color.FromRgb(10, 24, 30)),
                Foreground = Brushes.White
            };

            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            grid.Children.Add(new TextBlock
            {
                Text = $"✅ Skopiowano skondensowany rozkaz do schowka! Liczba wiadomości (<1900 zn.): {chunks.Count}",
                Foreground = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            Grid.SetRow(wrap, 1);

            var box = new TextBox
            {
                Text = full,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Padding = new Thickness(10),
                Background = new SolidColorBrush(Color.FromRgb(7, 17, 21)),
                Foreground = Brushes.White
            };
            Grid.SetRow(box, 2);

            var bAll = new Button
            {
                Content = "📋 Kopiuj CAŁOŚĆ",
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 0, 8, 6),
                Background = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                Foreground = new SolidColorBrush(Color.FromRgb(5, 16, 20)),
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            bAll.Click += (_, __) => { Clipboard.SetText(full); box.Text = full; };
            wrap.Children.Add(bAll);

            for (int i = 0; i < chunks.Count; i++)
            {
                int idx = i;
                string cTxt = chunks[i];
                var bPart = new Button
                {
                    Content = $"💬 Kopiuj Wiadomość {idx + 1}/{chunks.Count} ({cTxt.Length} zn.)",
                    Padding = new Thickness(12, 6, 12, 6),
                    Margin = new Thickness(0, 0, 8, 6),
                    Background = new SolidColorBrush(Color.FromRgb(22, 54, 66)),
                    Foreground = new SolidColorBrush(Color.FromRgb(250, 208, 44)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(250, 208, 44)),
                    FontWeight = FontWeights.Bold,
                    Cursor = Cursors.Hand
                };
                bPart.Click += (_, __) => { Clipboard.SetText(cTxt); box.Text = cTxt; };
                wrap.Children.Add(bPart);
            }

            grid.Children.Add(wrap);
            grid.Children.Add(box);
            dlg.Content = grid;
            dlg.ShowDialog();
        }
    }
}
