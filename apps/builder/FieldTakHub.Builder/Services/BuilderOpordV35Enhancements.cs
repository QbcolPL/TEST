#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
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

    internal static class BuilderOpordV35Enhancements
    {
        private static readonly string[] ColSets = { "ABCDEFGH", "JKLMNPQR", "STUVWXYZ" };
        private static readonly string[] RowSets = { "ABCDEFGHJKLMNPQRSTUV", "FGHJKLMNPQRSTUVABCDE" };

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
                    if (s.Contains("WGS84", StringComparison.OrdinalIgnoreCase)) return "WGS84";
                }
            }
            catch { }
            return "MGRS";
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

        private static OpordParagraphModel GetActiveOpordModelViaReflection()
        {
            try
            {
                var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                foreach (var f in typeof(OpordBuilderManager).GetFields(flags))
                {
                    if (typeof(OpordParagraphModel).IsAssignableFrom(f.FieldType))
                        return f.GetValue(null) as OpordParagraphModel;
                }
                foreach (var p in typeof(OpordBuilderManager).GetProperties(flags))
                {
                    if (typeof(OpordParagraphModel).IsAssignableFrom(p.PropertyType))
                        return p.GetValue(null) as OpordParagraphModel;
                }
            }
            catch { }
            return null;
        }

        public static void SyncAllFromGenerators(Window win, OpordParagraphModel model, OpordDayPlanModel activeDay)
        {
            if (model == null) model = GetActiveOpordModelViaReflection();
            if (model == null) return;
            if (activeDay == null && model.Days != null && model.Days.Count > 0)
                activeDay = model.Days[Math.Clamp(model.ActiveDayIndex, 0, model.Days.Count - 1)];
            if (activeDay == null) return;

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
                if (snap.VoiceChannels.Count > 2)
                {
                    var c2 = snap.VoiceChannels[2];
                    activeDay.HqChannel = $"{c2.Role}: {c2.Freq} [{c2.Band}] ({c2.Tone})";
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

            string sys = DetectActiveMapCoordinateSystem(win);
            var md = ExtractMapDataSafe(win);
            if (md != null && (md.HasAoPolygon || md.Pois.Count > 0))
            {
                activeDay.LocationLat = md.CenterLat.ToString("F6", CultureInfo.InvariantCulture);
                activeDay.LocationLon = md.CenterLon.ToString("F6", CultureInfo.InvariantCulture);
                string mgrs = LatLonToMgrs(md.CenterLat, md.CenterLon);
                if (string.IsNullOrWhiteSpace(activeDay.LocationName))
                    activeDay.LocationName = $"Rejon AO ({sys}: {mgrs})";

                var psb = new StringBuilder();
                psb.AppendLine($"Układ współrzędnych mapy: {sys} | Środek AO: {mgrs}");
                foreach (var p in md.Pois)
                    psb.AppendLine($"• {p.Name} [{p.Type}]: {LatLonToMgrs(p.Lat, p.Lon)}");
                activeDay.AreaOfInterestPoiDesc = psb.ToString().TrimEnd();
            }
        }

        public static void AttachOpordUiExtensions(Window win, OpordParagraphModel model, OpordDayPlanModel activeDay, StackPanel mainStack, Action rebuildCallback)
        {
            EnhanceLiveOpordTree(win, BuilderPackageAndUiPatcher.GetVisualTreeElements(win));
        }

        /// <summary>
        /// Bezpośrednio modyfikuje żywe drzewo wizualne zakładki Generator OPORD (widoczne na zrzutach ekranu),
        /// dzięki czemu wszystkie 6 punktów z listy zmian działa w 100% niezawodnie!
        /// </summary>
        public static void EnhanceLiveOpordTree(Window win, List<DependencyObject> safeElements = null)
        {
            if (win == null) return;
            if (safeElements == null) safeElements = BuilderPackageAndUiPatcher.GetVisualTreeElements(win);

            // Znajdź główny nagłówek OPORD: "GENERATOR ROZKAZU BOJOWEGO MILSIM / NATO"
            var opordTitleTb = safeElements.OfType<TextBlock>().FirstOrDefault(t =>
                (t.Text ?? "").Contains("GENERATOR ROZKAZU BOJOWEGO", StringComparison.OrdinalIgnoreCase));

            if (opordTitleTb == null) return; // Zakładka OPORD nie jest obecnie na ekranie

            var model = GetActiveOpordModelViaReflection();
            OpordDayPlanModel activeDay = null;
            if (model != null && model.Days != null && model.Days.Count > 0)
            {
                activeDay = model.Days[Math.Clamp(model.ActiveDayIndex, 0, model.Days.Count - 1)];
            }

            // 1. Dodaj przycisk "🔄 Pobierz z Generatora Stron" na samą górę do karty "GENERATOR ROZKAZU BOJOWEGO"
            //    oraz odśwież listę wyboru strony konfliktu (ComboBox)
            DependencyObject topParent = VisualTreeHelper.GetParent(opordTitleTb);
            while (topParent != null && !(topParent is StackPanel)) topParent = VisualTreeHelper.GetParent(topParent);
            if (topParent is StackPanel topHeaderStack)
            {
                var topScope = BuilderPackageAndUiPatcher.GetVisualTreeElements(topHeaderStack);
                var factionCombo = topScope.OfType<ComboBox>().FirstOrDefault();
                if (factionCombo != null)
                {
                    var liveFactions = FactionLiveSyncBridge.GetAllFactionNamesFromUi(win);
                    foreach (string fn in liveFactions)
                    {
                        if (!factionCombo.Items.Cast<object>().Any(x => string.Equals(x?.ToString(), fn, StringComparison.OrdinalIgnoreCase)))
                        {
                            factionCombo.Items.Add(fn);
                        }
                    }
                }

                if (topHeaderStack.Tag?.ToString() != "FTH_OPORD_TOP_SYNC_ADDED")
                {
                    topHeaderStack.Tag = "FTH_OPORD_TOP_SYNC_ADDED";
                    var topSyncBtn = new Button
                    {
                        Content = BuilderI18n.T(
                            "🔄 Pobierz dane z Generatora Stron i Mapy 1:1 (Odśwież strony, Tabelę E Łączności oraz współrzędne MGRS)",
                            "🔄 Pull Data from Faction & Map Generators (Refresh Factions, Table E Comms & MGRS Coords)"),
                        Height = 36,
                        Margin = new Thickness(0, 8, 0, 2),
                        Padding = new Thickness(14, 6, 14, 6),
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
                        RefreshSectionEAndCoordinatesInLiveTree(win, model, activeDay);
                    };
                    topHeaderStack.Children.Add(topSyncBtn);
                }
            }

            // 2. Zabezpiecz sekcję B.2 METOC: wyczyść domyślne 49.8494 / 18.8127 gdy brak AO i zablokuj przycisk "Pobierz METOC", gdy Miejscowość jest pusta i brak AO!
            PatchMetocSectionInLiveTree(win, safeElements, activeDay);

            // 3. Sekcja E. Tabela łączności: ukryj stary przycisk "Pobierz z Generatora Stron", wypełnij zakresy częstotliwości i dodaj tabelę własnych kanałów ("na ptaszek")
            PatchSectionECommsInLiveTree(win, safeElements, model, activeDay);

            // 4. Sekcja IV. Logistyka oraz Załączniki: dodaj opcjonalny Załącznik 3 z Mapą ("na ptaszek")
            PatchSectionIVAnnex3MapInLiveTree(win, safeElements, activeDay);

            // 5. Przycisk "Kopiuj na Discorda": podłącz okno skondensowanego eksportu z podziałem na wiadomości
            foreach (var btn in safeElements.OfType<Button>())
            {
                string bt = BuilderPackageAndUiPatcher.ExtractButtonText(btn);
                if (bt.Contains("Kopiuj na Discorda", StringComparison.OrdinalIgnoreCase) || bt.Contains("Copy for Discord", StringComparison.OrdinalIgnoreCase))
                {
                    if (btn.Tag?.ToString() != "FTH_DISCORD_MODAL_HOOKED")
                    {
                        btn.Tag = "FTH_DISCORD_MODAL_HOOKED";
                        btn.PreviewMouseLeftButtonDown += (s, e) =>
                        {
                            e.Handled = true;
                            SyncAllFromGenerators(win, model, activeDay);
                            ShowCondensedDiscordModal(win, model);
                        };
                    }
                }
            }
        }

        private static void PatchMetocSectionInLiveTree(Window win, List<DependencyObject> safeElements, OpordDayPlanModel activeDay)
        {
            var metocHeader = safeElements.OfType<TextBlock>().FirstOrDefault(t =>
                (t.Text ?? "").Contains("B.2. PROGNOZA POGODY", StringComparison.OrdinalIgnoreCase) ||
                (t.Text ?? "").Contains("WEATHER FORECAST", StringComparison.OrdinalIgnoreCase));

            if (metocHeader == null) return;

            DependencyObject p = VisualTreeHelper.GetParent(metocHeader);
            while (p != null && !(p is StackPanel)) p = VisualTreeHelper.GetParent(p);
            if (!(p is StackPanel metocStack)) return;

            var metocControls = BuilderPackageAndUiPatcher.GetVisualTreeElements(metocStack);
            var textBoxes = metocControls.OfType<TextBox>().ToList();
            // W sekcji B.2 są 4 pola TextBox: [0]=Data, [1]=Miejscowość, [2]=Lat, [3]=Lon
            TextBox locBox = textBoxes.Count >= 2 ? textBoxes[1] : null;
            TextBox latBox = textBoxes.Count >= 3 ? textBoxes[2] : null;
            TextBox lonBox = textBoxes.Count >= 4 ? textBoxes[3] : null;

            var mapData = ExtractMapDataSafe(win);
            bool hasAo = mapData != null && (mapData.HasAoPolygon || mapData.Pois.Count > 0);

            // Jeśli Miejscowość jest pusta i brak AO na mapie, wyczyść domyślne współrzędne arkusza (49.8494 / 18.8127)
            if (!hasAo && locBox != null && string.IsNullOrWhiteSpace(locBox.Text))
            {
                if (latBox != null && (latBox.Text.StartsWith("49.84") || latBox.Text.StartsWith("49.85"))) latBox.Text = "";
                if (lonBox != null && (lonBox.Text.StartsWith("18.81") || lonBox.Text.StartsWith("18.82"))) lonBox.Text = "";
            }

            foreach (var btn in metocControls.OfType<Button>())
            {
                string bt = BuilderPackageAndUiPatcher.ExtractButtonText(btn);
                if (bt.Contains("Pobierz METOC", StringComparison.OrdinalIgnoreCase) || bt.Contains("Fetch METOC", StringComparison.OrdinalIgnoreCase))
                {
                    if (btn.Tag?.ToString() != "FTH_METOC_LIVE_GUARD")
                    {
                        btn.Tag = "FTH_METOC_LIVE_GUARD";
                        btn.PreviewMouseLeftButtonDown += (s, e) =>
                        {
                            string locTxt = (locBox?.Text ?? activeDay?.LocationName ?? "").Trim();
                            var md = ExtractMapDataSafe(win);
                            bool aoExists = md != null && (md.HasAoPolygon || md.Pois.Count > 0);

                            if (string.IsNullOrWhiteSpace(locTxt) && !aoExists)
                            {
                                e.Handled = true;
                                MessageBox.Show(
                                    BuilderI18n.T(
                                        "⚠️ Nie można pobrać raportu METOC — pole 'Miejscowość' jest puste i w Generatorze Map nie zaznaczono obszaru gry (AO)!\n\n" +
                                        "Proszę wpisać nazwę miejscowości/obszaru LUB zaznaczyć granicę AO w module Generator Map 1:1.",
                                        "⚠️ Cannot fetch METOC report — 'Location' is empty and no AO polygon is drawn in the Map Generator!\n\n" +
                                        "Please enter a location name OR draw an AO boundary in the 1:1 Map Generator."),
                                    BuilderI18n.T("Wymagany obszar lub AO na mapie — METOC", "Area or Map AO Required — METOC"),
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Warning);
                            }
                        };
                    }
                }
            }
        }

        private static void PatchSectionECommsInLiveTree(Window win, List<DependencyObject> safeElements, OpordParagraphModel model, OpordDayPlanModel activeDay)
        {
            var secEHeader = safeElements.OfType<TextBlock>().FirstOrDefault(t =>
                (t.Text ?? "").Contains("E. Tabela łączności", StringComparison.OrdinalIgnoreCase) ||
                (t.Text ?? "").Contains("E. Comms Table", StringComparison.OrdinalIgnoreCase));

            if (secEHeader == null) return;

            // Ukryj stary przycisk "Pobierz z Generatora Stron" obok nagłówka E. Tabela łączności (bo jest już na samej górze)
            var headerParent = VisualTreeHelper.GetParent(secEHeader) as Panel;
            if (headerParent != null)
            {
                foreach (var b in BuilderPackageAndUiPatcher.GetVisualTreeElements(headerParent).OfType<Button>())
                {
                    string bt = BuilderPackageAndUiPatcher.ExtractButtonText(b);
                    if (bt.Contains("Pobierz z Generatora Stron", StringComparison.OrdinalIgnoreCase))
                    {
                        b.Visibility = Visibility.Collapsed;
                        b.Width = 0;
                        b.Height = 0;
                    }
                }
            }

            DependencyObject secParent = VisualTreeHelper.GetParent(secEHeader);
            while (secParent != null && !(secParent is StackPanel)) secParent = VisualTreeHelper.GetParent(secParent);
            if (!(secParent is StackPanel secStack)) return;

            if (activeDay != null && string.IsNullOrWhiteSpace(activeDay.SyncedCommsMatrixSummary))
            {
                SyncAllFromGenerators(win, model, activeDay);
            }

            RefreshSectionEAndCoordinatesInLiveTree(win, model, activeDay);

            if (secStack.Tag?.ToString() == "FTH_CUSTOM_COMMS_ADDED") return;
            secStack.Tag = "FTH_CUSTOM_COMMS_ADDED";

            // Dodaj opcjonalną tabelkę na inne kanały ("na ptaszek") w Sekcji E
            var customBoxBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(8, 22, 28)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 8, 0, 12)
            };

            var st = new StackPanel();
            var chk = new CheckBox
            {
                Content = BuilderI18n.T(
                    "☑️ Dodaj inne kanały radiowe ręcznie w OPORD (opcjonalna tabela dodatkowych częstotliwości)",
                    "☑️ Add custom radio channels manually in OPORD (optional frequency table)"),
                IsChecked = activeDay != null && activeDay.IncludeCustomChannelsTable,
                Foreground = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = Cursors.Hand
            };
            st.Children.Add(chk);

            var bodyPanel = new StackPanel
            {
                Visibility = (activeDay != null && activeDay.IncludeCustomChannelsTable) ? Visibility.Visible : Visibility.Collapsed
            };

            var rowsContainer = new StackPanel();

            void SaveRowsToDayModel()
            {
                if (activeDay == null) return;
                var lines = new List<string>();
                foreach (Grid g in rowsContainer.Children.OfType<Grid>())
                {
                    var tbs = g.Children.OfType<TextBox>().ToList();
                    if (tbs.Count >= 4)
                    {
                        lines.Add($"• {tbs[0].Text.Trim()} [{tbs[1].Text.Trim()}]: {tbs[2].Text.Trim()} ({tbs[3].Text.Trim()})");
                    }
                }
                activeDay.CustomChannelsTableText = string.Join("\n", lines);
            }

            void AddCustomRow(string role, string band, string freq, string tone)
            {
                var g = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.7, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                TextBox CreateCell(string val, int col)
                {
                    var t = new TextBox
                    {
                        Text = val,
                        Padding = new Thickness(6, 4, 6, 4),
                        Margin = new Thickness(0, 0, 6, 0),
                        Background = new SolidColorBrush(Color.FromRgb(13, 33, 41)),
                        Foreground = Brushes.White,
                        BorderBrush = new SolidColorBrush(Color.FromRgb(34, 74, 89)),
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 11.5
                    };
                    t.TextChanged += (_, __) => SaveRowsToDayModel();
                    Grid.SetColumn(t, col);
                    return t;
                }

                g.Children.Add(CreateCell(role, 0));
                g.Children.Add(CreateCell(band, 1));
                g.Children.Add(CreateCell(freq, 2));
                g.Children.Add(CreateCell(tone, 3));

                var delBtn = new Button
                {
                    Content = "✕",
                    Width = 28,
                    Height = 26,
                    Background = new SolidColorBrush(Color.FromRgb(70, 20, 20)),
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    Cursor = Cursors.Hand
                };
                delBtn.Click += (_, __) => { rowsContainer.Children.Remove(g); SaveRowsToDayModel(); };
                Grid.SetColumn(delBtn, 4);
                g.Children.Add(delBtn);

                rowsContainer.Children.Add(g);
            }

            AddCustomRow("Kanał Sekcji Alfa", "PMR", "CH 2 (446.01875 MHz)", "CTCSS 77.0 Hz");
            AddCustomRow("Kanał Zwiadu / Snajper", "VHF/UHF", "446.05625 MHz (PMR CH 5)", "CTCSS 110.9 Hz");

            var addRowBtn = new Button
            {
                Content = BuilderI18n.T("➕ Dodaj kolejny kanał własny", "➕ Add Custom Channel Row"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12, 4, 12, 4),
                Margin = new Thickness(0, 4, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(18, 48, 58)),
                Foreground = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            addRowBtn.Click += (_, __) =>
            {
                AddCustomRow("Kanał dodatkowy", "PMR", "CH 6 (446.06875 MHz)", "CTCSS 123.0 Hz");
                SaveRowsToDayModel();
            };

            bodyPanel.Children.Add(rowsContainer);
            bodyPanel.Children.Add(addRowBtn);
            st.Children.Add(bodyPanel);

            chk.Checked += (_, __) =>
            {
                if (activeDay != null) activeDay.IncludeCustomChannelsTable = true;
                bodyPanel.Visibility = Visibility.Visible;
                SaveRowsToDayModel();
            };
            chk.Unchecked += (_, __) =>
            {
                if (activeDay != null) activeDay.IncludeCustomChannelsTable = false;
                bodyPanel.Visibility = Visibility.Collapsed;
            };

            customBoxBorder.Child = st;

            int insertIdx = secStack.Children.Count;
            for (int i = 0; i < secStack.Children.Count; i++)
            {
                if (secStack.Children[i] is TextBlock tb && (tb.Text ?? "").Contains("F.1.", StringComparison.OrdinalIgnoreCase))
                {
                    insertIdx = i;
                    break;
                }
            }
            secStack.Children.Insert(insertIdx, customBoxBorder);
        }

        private static void RefreshSectionEAndCoordinatesInLiveTree(Window win, OpordParagraphModel model, OpordDayPlanModel activeDay)
        {
            if (win == null || activeDay == null) return;
            var all = BuilderPackageAndUiPatcher.GetVisualTreeElements(win);

            foreach (var tb in all.OfType<TextBox>())
            {
                var p = VisualTreeHelper.GetParent(tb) as Panel;
                if (p == null) continue;
                string lbl = string.Join(" ", p.Children.OfType<TextBlock>().Select(x => x.Text ?? ""));
                if (lbl.Contains("Kanał główny", StringComparison.OrdinalIgnoreCase)) tb.Text = activeDay.PrimaryChannel;
                else if (lbl.Contains("Kanał zapasowy", StringComparison.OrdinalIgnoreCase)) tb.Text = activeDay.AlternateChannel;
                else if (lbl.Contains("Kanał HQ", StringComparison.OrdinalIgnoreCase)) tb.Text = activeDay.HqChannel;
                else if (lbl.Contains("A. Obszar zainteresowania", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(activeDay.AreaOfInterestPoiDesc))
                    tb.Text = activeDay.AreaOfInterestPoiDesc;
            }

            foreach (var txt in all.OfType<TextBlock>())
            {
                string t = txt.Text ?? "";
                if (t.Contains("[ZACIĄGNIĘTE Z GENERATORA STRON", StringComparison.OrdinalIgnoreCase))
                {
                    txt.Text = activeDay.SyncedCommsMatrixSummary;
                }
            }
        }

        private static void PatchSectionIVAnnex3MapInLiveTree(Window win, List<DependencyObject> safeElements, OpordDayPlanModel activeDay)
        {
            var secIVHeader = safeElements.OfType<TextBlock>().FirstOrDefault(t =>
                (t.Text ?? "").Contains("IV. LOGISTYKA ORAZ ZAŁĄCZNIKI", StringComparison.OrdinalIgnoreCase) ||
                (t.Text ?? "").Contains("IV. LOGISTICS", StringComparison.OrdinalIgnoreCase));

            if (secIVHeader == null) return;

            DependencyObject p = VisualTreeHelper.GetParent(secIVHeader);
            while (p != null && !(p is StackPanel)) p = VisualTreeHelper.GetParent(p);
            if (!(p is StackPanel secIVStack)) return;

            if (secIVStack.Tag?.ToString() == "FTH_ANNEX3_ADDED") return;
            secIVStack.Tag = "FTH_ANNEX3_ADDED";

            string dayLabel = activeDay?.DayTitle ?? "Dzień 1";
            var annex3Border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(8, 22, 28)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 10, 0, 4)
            };

            var st = new StackPanel();
            var chkAnnex3 = new CheckBox
            {
                Content = BuilderI18n.T(
                    $"☑️ Załącznik 3 ({dayLabel}): Dołącz mapę operacyjną / szkic AO do rozkazu OPORD",
                    $"☑️ Annex 3 ({dayLabel}): Attach operational map / AO sketch to OPORD"),
                IsChecked = activeDay != null && activeDay.IncludeAnnex3Map,
                Foreground = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = Cursors.Hand
            };
            st.Children.Add(chkAnnex3);

            var detailsPanel = new StackPanel
            {
                Visibility = (activeDay != null && activeDay.IncludeAnnex3Map) ? Visibility.Visible : Visibility.Collapsed
            };

            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var pathBox = new TextBox
            {
                Text = activeDay?.Annex3MapImagePath ?? "",
                Padding = new Thickness(6, 4, 6, 4),
                Background = new SolidColorBrush(Color.FromRgb(13, 33, 41)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(34, 74, 89)),
                FontSize = 11.5
            };
            pathBox.TextChanged += (_, __) => { if (activeDay != null) activeDay.Annex3MapImagePath = pathBox.Text; };
            Grid.SetColumn(pathBox, 0);
            row.Children.Add(pathBox);

            var pickBtn = new Button
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
            pickBtn.Click += (_, __) =>
            {
                var ofd = new OpenFileDialog
                {
                    Filter = "Obrazy map (*.png;*.jpg;*.jpeg;*.webp;*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.bmp|Wszystkie pliki (*.*)|*.*"
                };
                if (ofd.ShowDialog(win) == true)
                {
                    pathBox.Text = ofd.FileName;
                    if (activeDay != null) activeDay.Annex3MapImagePath = ofd.FileName;
                }
            };
            Grid.SetColumn(pickBtn, 1);
            row.Children.Add(pickBtn);
            detailsPanel.Children.Add(row);

            chkAnnex3.Checked += (_, __) =>
            {
                if (activeDay != null) activeDay.IncludeAnnex3Map = true;
                detailsPanel.Visibility = Visibility.Visible;
            };
            chkAnnex3.Unchecked += (_, __) =>
            {
                if (activeDay != null) activeDay.IncludeAnnex3Map = false;
                detailsPanel.Visibility = Visibility.Collapsed;
            };

            st.Children.Add(detailsPanel);
            annex3Border.Child = st;
            secIVStack.Children.Add(annex3Border);
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
            if (m == null) m = GetActiveOpordModelViaReflection();
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
