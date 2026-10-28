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
using System.Text.RegularExpressions;
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
        public string CoordSystem { get; set; } = "MGRS";
        public List<ExtractedMapPoiItem> Pois { get; set; } = new List<ExtractedMapPoiItem>();
    }

    internal static class BuilderOpordV35Enhancements
    {
        private static readonly string[] ColSets = { "ABCDEFGH", "JKLMNPQR", "STUVWXYZ" };
        private static readonly string[] RowSets = { "ABCDEFGHJKLMNPQRSTUV", "FGHJKLMNPQRSTUVABCDE" };
        private static OpordParagraphModel _cachedLiveModel;
        private static MethodInfo _cachedRebuildMethod;
        public static bool IsRebuildingOpord { get; set; }

        static BuilderOpordV35Enhancements()
        {
            TacticalSharedState.OnOpordTabActivated = win => OnModuleSwitched(win);
        }

        public static void OnModuleSwitched(Window win)
        {
            if (win == null || IsRebuildingOpord) return;
            try
            {
                var m = FindOpordModelFast();
                var d = (m?.Days != null && m.Days.Count > 0) ? m.Days[Math.Clamp(m.ActiveDayIndex, 0, m.Days.Count - 1)] : null;
                if (m != null && d != null)
                {
                    SyncAllFromGenerators(win, m, d);
                    InvokeRebuildOpordForm(win);
                }
            }
            catch { }
        }

        private static void InvokeRebuildOpordForm(Window win)
        {
            try
            {
                if (_cachedRebuildMethod == null)
                    _cachedRebuildMethod = typeof(OpordBuilderManager).GetMethod("RebuildOpordForm", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                _cachedRebuildMethod?.Invoke(null, new object[] { win ?? Application.Current?.MainWindow });
            }
            catch { }
        }

        internal static OpordParagraphModel FindOpordModelFast()
        {
            if (_cachedLiveModel != null) return _cachedLiveModel;
            try
            {
                foreach (var f in typeof(OpordBuilderManager).GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (typeof(OpordParagraphModel).IsAssignableFrom(f.FieldType))
                    {
                        _cachedLiveModel = f.GetValue(null) as OpordParagraphModel;
                        if (_cachedLiveModel != null) return _cachedLiveModel;
                    }
                }
            }
            catch { }
            return null;
        }

        public static ExtractedMapSnapshot ExtractMapDataSafe(Window win)
        {
            var snap = new ExtractedMapSnapshot();
            try
            {
                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                var m = typeof(OpordBuilderManager).GetMethod("GetExtractedMapData", flags) ?? typeof(OpordBuilderManager).GetMethod("ExtractMapData", flags);
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

                // Sprawdź dodatkowo Canvas w Generatorze Map pod kątem wielokątów AO (Polygon / Polyline)
                if (!snap.HasAoPolygon && snap.Pois.Count == 0)
                {
                    var allElems = FactionLiveSyncBridge.GetAllRootVisualsAcrossTabs(win ?? Application.Current?.MainWindow);
                    foreach (var canvas in allElems.OfType<Canvas>())
                    {
                        foreach (UIElement ch in canvas.Children)
                        {
                            if (ch is System.Windows.Shapes.Polygon poly && poly.Points != null && poly.Points.Count >= 3 && poly.Visibility == Visibility.Visible)
                            { snap.HasAoPolygon = true; break; }
                            if (ch is System.Windows.Shapes.Polyline pline && pline.Points != null && pline.Points.Count >= 3 && pline.Visibility == Visibility.Visible)
                            { snap.HasAoPolygon = true; break; }
                        }
                        if (snap.HasAoPolygon) break;
                    }
                }
            }
            catch { }
            return snap;
        }

        public static bool CheckAndPrepareMetocBeforeFetch()
        {
            var win = Application.Current?.MainWindow;
            var model = FindOpordModelFast();
            if (model == null || model.Days == null || model.Days.Count == 0) return true;
            var activeDay = model.Days[Math.Clamp(model.ActiveDayIndex, 0, model.Days.Count - 1)];

            string locTyped = (activeDay.LocationName ?? "").Trim();
            var mapSnap = ExtractMapDataSafe(win);
            bool hasExplicitAo = mapSnap != null && (mapSnap.HasAoPolygon || mapSnap.Pois.Count > 0);

            if (string.IsNullOrWhiteSpace(locTyped) && !hasExplicitAo)
            {
                activeDay.LocationLat = "";
                activeDay.LocationLon = "";
                MessageBox.Show(
                    BuilderI18n.T(
                        "⚠️ Nie można pobrać danych METOC dla samego arkusza mapy!\n\n" +
                        "Proszę zaznaczyć obszar gry (AO) w Generatorze Map 1:1 LUB wpisać nazwę miejscowości w polu 'Miejscowość'.",
                        "⚠️ Cannot fetch METOC for an unmarked map sheet!\n\n" +
                        "Please mark an AO polygon in the 1:1 Map Generator OR enter a location name."),
                    BuilderI18n.T("METOC — Zaznacz AO lub wpisz miejscowość", "METOC — Select AO or Enter Location"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(locTyped) && hasExplicitAo)
            {
                activeDay.LocationLat = mapSnap.CenterLat.ToString("F4", CultureInfo.InvariantCulture);
                activeDay.LocationLon = mapSnap.CenterLon.ToString("F4", CultureInfo.InvariantCulture);
            }
            return true;
        }

        public static string LatLonToMgrs(double lat, double lon)
        {
            try
            {
                double cLat = Math.Clamp(lat, -80.0, 84.0);
                double nLon = ((lon + 180.0) % 360.0 + 360.0) % 360.0 - 180.0;
                int zone = (int)Math.Floor((nLon + 180.0) / 6.0) + 1;
                double latRad = cLat * Math.PI / 180.0, lonRad = nLon * Math.PI / 180.0;
                double lonOrig = ((zone - 1) * 6 - 180 + 3) * Math.PI / 180.0;
                double a = 6378137.0, eccSq = 0.00669438, k0 = 0.9996, eccP = eccSq / (1.0 - eccSq);
                double n = a / Math.Sqrt(1.0 - eccSq * Math.Sin(latRad) * Math.Sin(latRad));
                double t = Math.Tan(latRad) * Math.Tan(latRad), c = eccP * Math.Cos(latRad) * Math.Cos(latRad);
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

        public static void SyncAllFromGenerators(Window win, OpordParagraphModel model, OpordDayPlanModel activeDay)
        {
            if (model == null) model = FindOpordModelFast();
            if (model == null || activeDay == null) return;

            var allFactions = FactionLiveSyncBridge.ReadAllFactionsFromScreenAndMemory(win);
            if (allFactions.Count > 0)
            {
                var matched = allFactions.FirstOrDefault(x => string.Equals(x.FactionName, model.TargetFaction, StringComparison.OrdinalIgnoreCase)) ?? allFactions[0];
                model.TargetFaction = matched.FactionName;

                var sb = new StringBuilder();
                sb.AppendLine("📡 [ZACIĄGNIĘTE Z GENERATORA STRON KANAŁY RADIOWE I MESHTASTIC DLA: " + matched.FactionName + "]");
                sb.AppendLine("• Meshtastic LoRa: Kanał '" + matched.ChannelName + "' | Region: " + matched.Region + " | Preset: " + matched.ModemPreset + " | Szyfrowanie: AES-256 PSK");
                sb.AppendLine("• Serwer ATAK (OpenTAKServer): " + (string.IsNullOrWhiteSpace(matched.OtsHost) ? "Tryb lokalny / Off-Grid Mesh" : matched.OtsHost));
                sb.AppendLine("• Telefon alarmowy ORGA / MEDEVAC (SOS MGRS): " + (string.IsNullOrWhiteSpace(matched.EmergencyPhone) ? "Brak / 112" : matched.EmergencyPhone));
                sb.AppendLine("• Wszystkie kanały głosowe zdefiniowane w Generatorze Stron:");
                foreach (var vc in matched.VoiceChannels)
                    sb.AppendLine("   - " + vc.Role + " [" + vc.Band + "]: " + vc.Freq + " (" + vc.Tone + ")");
                activeDay.SyncedCommsMatrixSummary = sb.ToString().TrimEnd();

                foreach (var prop in activeDay.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (prop.PropertyType == typeof(string) && prop.CanWrite && prop.Name.Contains("Synced", StringComparison.OrdinalIgnoreCase))
                        prop.SetValue(activeDay, activeDay.SyncedCommsMatrixSummary);
                }
            }
        }

        public static void UpgradeOpordMainStack(StackPanel mainStack)
        {
            if (mainStack == null) return;
            var win = Application.Current?.MainWindow;
            var model = FindOpordModelFast();
            if (model == null || model.Days == null || model.Days.Count == 0) return;
            var activeDay = model.Days[Math.Clamp(model.ActiveDayIndex, 0, model.Days.Count - 1)];

            SyncAllFromGenerators(win, model, activeDay);

            var opordElements = BuilderPackageAndUiPatcher.GetLogicalElements(mainStack);

            // 1. Górny boks: dodaj przycisk "🔄 Pobierz z Generatora Stron" na samej górze i odśwież listę stron
            if (mainStack.Children.Count > 0 && mainStack.Children[0] is Border topBorder && topBorder.Child is StackPanel topHeaderStack)
            {
                var factionCombo = BuilderPackageAndUiPatcher.GetLogicalElements(topHeaderStack).OfType<ComboBox>().FirstOrDefault();
                if (factionCombo != null)
                {
                    var liveNames = FactionLiveSyncBridge.GetAllFactionNamesFromUi(win);
                    for (int i = factionCombo.Items.Count - 1; i >= 0; i--)
                    {
                        string s = factionCombo.Items[i]?.ToString() ?? "";
                        if (s.Equals("PLI", StringComparison.OrdinalIgnoreCase) || s.Equals("GeoChat", StringComparison.OrdinalIgnoreCase))
                            factionCombo.Items.RemoveAt(i);
                    }
                    foreach (string fn in liveNames)
                        if (!factionCombo.Items.Cast<object>().Any(x => string.Equals(x?.ToString(), fn, StringComparison.OrdinalIgnoreCase)))
                            factionCombo.Items.Add(fn);
                }

                var topSyncBtn = new Button
                {
                    Content = BuilderI18n.T(
                        "🔄 Pobierz z Generatora Stron (Odśwież strony, kanały radiowe i współrzędne AO)",
                        "🔄 Pull from Faction Generator (Refresh Factions, Radio Channels & AO Coords)"),
                    Height = 36,
                    Margin = new Thickness(0, 8, 0, 2),
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
                    InvokeRebuildOpordForm(win);
                };
                topHeaderStack.Children.Add(topSyncBtn);
            }

            // 2. Sekcja B.2 METOC: wyczyść domyślne Lat/Lon arkusza gdy brak AO i podepnij walidator
            var metocHeader = opordElements.OfType<TextBlock>().FirstOrDefault(t => (t.Text ?? "").Contains("B.2. PROGNOZA POGODY", StringComparison.OrdinalIgnoreCase));
            if (metocHeader != null)
            {
                DependencyObject mp = LogicalTreeHelper.GetParent(metocHeader);
                while (mp != null && !(mp is StackPanel)) mp = LogicalTreeHelper.GetParent(mp);
                if (mp is StackPanel metocStack)
                {
                    var mBoxes = BuilderPackageAndUiPatcher.GetLogicalElements(metocStack).OfType<TextBox>().ToList();
                    TextBox locBox = mBoxes.Count >= 2 ? mBoxes[1] : null;
                    TextBox latBox = mBoxes.Count >= 3 ? mBoxes[2] : null;
                    TextBox lonBox = mBoxes.Count >= 4 ? mBoxes[3] : null;

                    var md = ExtractMapDataSafe(win);
                    bool hasAo = md != null && (md.HasAoPolygon || md.Pois.Count > 0);
                    if (!hasAo && locBox != null && string.IsNullOrWhiteSpace(locBox.Text))
                    {
                        if (latBox != null) latBox.Text = "";
                        if (lonBox != null) lonBox.Text = "";
                        activeDay.LocationLat = "";
                        activeDay.LocationLon = "";
                    }
                    else if (hasAo)
                    {
                        string latS = md.CenterLat.ToString("F4", CultureInfo.InvariantCulture);
                        string lonS = md.CenterLon.ToString("F4", CultureInfo.InvariantCulture);
                        if (latBox != null) latBox.Text = latS;
                        if (lonBox != null) lonBox.Text = lonS;
                        activeDay.LocationLat = latS;
                        activeDay.LocationLon = lonS;
                    }
                }
            }

            // 3. Sekcja E: ukryj stary przycisk i 3 pola ręczne, wstaw opcjonalną tabelę na "ptaszek" z "+ Dodaj kanał"
            var secEHeader = opordElements.OfType<TextBlock>().FirstOrDefault(t => (t.Text ?? "").Contains("E. Tabela łączności", StringComparison.OrdinalIgnoreCase));
            if (secEHeader != null)
            {
                if (LogicalTreeHelper.GetParent(secEHeader) is Panel headerParent)
                {
                    foreach (var b in BuilderPackageAndUiPatcher.GetLogicalElements(headerParent).OfType<Button>())
                        if (BuilderPackageAndUiPatcher.ExtractButtonText(b).Contains("Pobierz z Generatora Stron", StringComparison.OrdinalIgnoreCase))
                        { b.Visibility = Visibility.Collapsed; b.Width = 0; b.Height = 0; }
                }

                DependencyObject secParent = LogicalTreeHelper.GetParent(secEHeader);
                while (secParent != null && !(secParent is StackPanel)) secParent = LogicalTreeHelper.GetParent(secParent);
                if (secParent is StackPanel secStack)
                {
                    foreach (var tb in BuilderPackageAndUiPatcher.GetLogicalElements(secStack).OfType<TextBox>())
                    {
                        if (LogicalTreeHelper.GetParent(tb) is Panel p)
                        {
                            string lbl = string.Join(" ", p.Children.OfType<TextBlock>().Select(x => x.Text ?? ""));
                            if (lbl.Contains("Kanał główny", StringComparison.OrdinalIgnoreCase) ||
                                lbl.Contains("Kanał zapasowy", StringComparison.OrdinalIgnoreCase) ||
                                lbl.Contains("Kanał HQ", StringComparison.OrdinalIgnoreCase))
                            {
                                p.Visibility = Visibility.Collapsed; p.Height = 0;
                                if (LogicalTreeHelper.GetParent(p) is Grid rg && rg.ColumnDefinitions.Count == 3)
                                { rg.Visibility = Visibility.Collapsed; rg.Height = 0; }
                            }
                        }
                    }

                    foreach (var txt in BuilderPackageAndUiPatcher.GetLogicalElements(secStack).OfType<TextBlock>())
                        if ((txt.Text ?? "").Contains("[ZACIĄGNIĘTE Z GENERATORA STRON", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(activeDay.SyncedCommsMatrixSummary))
                            txt.Text = activeDay.SyncedCommsMatrixSummary;

                    var customBox = BuildOptionalManualCommsTable(activeDay);
                    int insIdx = secStack.Children.Count;
                    for (int i = 0; i < secStack.Children.Count; i++)
                        if (secStack.Children[i] is TextBlock tb && (tb.Text ?? "").Contains("F.1.", StringComparison.OrdinalIgnoreCase))
                        { insIdx = i; break; }
                    secStack.Children.Insert(insIdx, customBox);
                }
            }

            // 4. Sekcja IV: dodaj Załącznik nr 3 — Mapa (na "ptaszek")
            var secIVHeader = opordElements.OfType<TextBlock>().FirstOrDefault(t => (t.Text ?? "").Contains("IV. LOGISTYKA ORAZ ZAŁĄCZNIKI", StringComparison.OrdinalIgnoreCase));
            if (secIVHeader != null)
            {
                DependencyObject p = LogicalTreeHelper.GetParent(secIVHeader);
                while (p != null && !(p is StackPanel)) p = LogicalTreeHelper.GetParent(p);
                if (p is StackPanel secIVStack)
                {
                    secIVStack.Children.Add(BuildAnnex3MapBox(win, activeDay));
                }
            }
        }

        private static Border BuildOptionalManualCommsTable(OpordDayPlanModel activeDay)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(8, 22, 28)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 8, 0, 12)
            };
            var st = new StackPanel();
            var topGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var chk = new CheckBox
            {
                Content = "☑️ Tabela dodatkowych kanałów i częstotliwości wpisywanych ręcznie w OPORD (" + activeDay.DayTitle + ")",
                IsChecked = activeDay.IncludeCustomChannelsTable,
                Foreground = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand
            };
            Grid.SetColumn(chk, 0);
            topGrid.Children.Add(chk);

            var addBtn = new Button
            {
                Content = "+ Dodaj kanał",
                Padding = new Thickness(12, 4, 12, 4),
                Background = new SolidColorBrush(Color.FromRgb(22, 130, 75)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(46, 204, 113)),
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                Visibility = activeDay.IncludeCustomChannelsTable ? Visibility.Visible : Visibility.Collapsed
            };
            Grid.SetColumn(addBtn, 1);
            topGrid.Children.Add(addBtn);
            st.Children.Add(topGrid);

            var rowsContainer = new StackPanel
            {
                Visibility = activeDay.IncludeCustomChannelsTable ? Visibility.Visible : Visibility.Collapsed
            };

            void SaveRows()
            {
                var lines = new List<string>();
                foreach (Grid g in rowsContainer.Children.OfType<Grid>())
                {
                    var tbs = g.Children.OfType<TextBox>().ToList();
                    if (tbs.Count >= 4) lines.Add("• " + tbs[0].Text.Trim() + " [" + tbs[1].Text.Trim() + "]: " + tbs[2].Text.Trim() + " (" + tbs[3].Text.Trim() + ")");
                }
                activeDay.CustomChannelsTableText = string.Join("\n", lines);
            }

            void AddRow(string r, string b, string f, string tn)
            {
                var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.7, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                TextBox Cell(string v, int c)
                {
                    var t = new TextBox { Text = v, Height = 30, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 0), Background = new SolidColorBrush(Color.FromRgb(13, 33, 41)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(34, 74, 89)), FontFamily = new FontFamily("Consolas"), FontSize = 11.5 };
                    t.TextChanged += (_, __) => SaveRows();
                    Grid.SetColumn(t, c);
                    return t;
                }
                g.Children.Add(Cell(r, 0)); g.Children.Add(Cell(b, 1)); g.Children.Add(Cell(f, 2)); g.Children.Add(Cell(tn, 3));

                var del = new Button { Content = "−", Width = 32, Height = 30, Padding = new Thickness(0), Background = new SolidColorBrush(Color.FromRgb(85, 22, 22)), Foreground = Brushes.White, FontWeight = FontWeights.Bold, Cursor = Cursors.Hand };
                del.Click += (_, __) => { rowsContainer.Children.Remove(g); SaveRows(); };
                Grid.SetColumn(del, 4);
                g.Children.Add(del);
                rowsContainer.Children.Add(g);
            }

            AddRow("Kanał główny (Ręczny)", "PMR", "CH 1 (446.00625 MHz)", "CTCSS 67.0 Hz");
            AddRow("Kanał zapasowy", "PMR", "CH 3 (446.03125 MHz)", "CTCSS 88.5 Hz");
            AddRow("Kanał HQ", "PMR", "CH 8 (446.09375 MHz)", "Brak tonu");

            addBtn.Click += (_, __) => { AddRow("Kanał dodatkowy", "PMR", "CH 5 (446.05625 MHz)", "CTCSS 110.9 Hz"); SaveRows(); };
            chk.Checked += (_, __) => { activeDay.IncludeCustomChannelsTable = true; rowsContainer.Visibility = Visibility.Visible; addBtn.Visibility = Visibility.Visible; SaveRows(); };
            chk.Unchecked += (_, __) => { activeDay.IncludeCustomChannelsTable = false; rowsContainer.Visibility = Visibility.Collapsed; addBtn.Visibility = Visibility.Collapsed; };

            st.Children.Add(rowsContainer);
            border.Child = st;
            return border;
        }

        private static Border BuildAnnex3MapBox(Window win, OpordDayPlanModel activeDay)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(8, 22, 28)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 10, 0, 4)
            };
            var st = new StackPanel();
            var chk = new CheckBox
            {
                Content = "☑️ Załącznik 3 (" + activeDay.DayTitle + "): Dołącz mapę operacyjną / szkic AO do rozkazu OPORD",
                IsChecked = activeDay.IncludeAnnex3Map,
                Foreground = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = Cursors.Hand
            };
            st.Children.Add(chk);

            var details = new Grid { Visibility = activeDay.IncludeAnnex3Map ? Visibility.Visible : Visibility.Collapsed };
            details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            details.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var pathBox = new TextBox { Text = activeDay.Annex3MapImagePath ?? "", Height = 30, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(8, 2, 8, 2), Background = new SolidColorBrush(Color.FromRgb(13, 33, 41)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(34, 74, 89)) };
            pathBox.TextChanged += (_, __) => activeDay.Annex3MapImagePath = pathBox.Text;
            Grid.SetColumn(pathBox, 0);
            details.Children.Add(pathBox);

            var pickBtn = new Button { Content = "📂 Wybierz plik mapy (.PNG / .JPG)", Height = 30, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0), Background = new SolidColorBrush(Color.FromRgb(22, 54, 66)), Foreground = new SolidColorBrush(Color.FromRgb(250, 208, 44)), BorderBrush = new SolidColorBrush(Color.FromRgb(250, 208, 44)), FontWeight = FontWeights.Bold, Cursor = Cursors.Hand };
            pickBtn.Click += (_, __) =>
            {
                var ofd = new OpenFileDialog { Filter = "Obrazy map (*.png;*.jpg;*.jpeg;*.webp;*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.bmp|Wszystkie (*.*)|*.*" };
                if (ofd.ShowDialog(win) == true) { pathBox.Text = ofd.FileName; activeDay.Annex3MapImagePath = ofd.FileName; }
            };
            Grid.SetColumn(pickBtn, 1);
            details.Children.Add(pickBtn);

            chk.Checked += (_, __) => { activeDay.IncludeAnnex3Map = true; details.Visibility = Visibility.Visible; };
            chk.Unchecked += (_, __) => { activeDay.IncludeAnnex3Map = false; details.Visibility = Visibility.Collapsed; };

            st.Children.Add(details);
            border.Child = st;
            return border;
        }

        public static string BuildAnnex3AndCustomCommsHtml(OpordDayPlanModel d)
        {
            if (d == null) return "";
            var sb = new StringBuilder();
            if (d.IncludeCustomChannelsTable && !string.IsNullOrWhiteSpace(d.CustomChannelsTableText))
                sb.AppendLine("<div class='sec'>DODATKOWE KANAŁY RADIOWE (OPORD)</div><div class='box'>" + WebUtility.HtmlEncode(d.CustomChannelsTableText) + "</div>");
            if (d.IncludeAnnex3Map)
            {
                sb.AppendLine("<div class='sec'>ZAŁĄCZNIK 3 — MAPA OPERACYJNA (" + WebUtility.HtmlEncode(d.DayTitle) + ")</div>");
                if (!string.IsNullOrWhiteSpace(d.Annex3MapImagePath) && File.Exists(d.Annex3MapImagePath))
                {
                    byte[] b = File.ReadAllBytes(d.Annex3MapImagePath);
                    sb.AppendLine("<div style='text-align:center;margin-top:8px;'><img src='data:image/png;base64," + Convert.ToBase64String(b) + "' style='max-width:100%;max-height:210mm;'/></div>");
                }
            }
            return sb.ToString();
        }

        public static string BuildCondensedDiscordMarkdown(OpordParagraphModel m)
        {
            if (m == null) m = FindOpordModelFast();
            if (m == null || m.Days == null) return "";
            var sb = new StringBuilder();
            foreach (var d in m.Days.Where(x => x.IncludeInExport))
            {
                sb.AppendLine("## 📋 " + d.OperationNumber + " | " + m.TargetFaction + " — " + d.DayTitle + " (" + d.DateIso + ")");
                sb.AppendLine("**AO:** " + d.LocationName + " | **HQ:** " + d.IssuingHq);
                sb.AppendLine("**🎯 MISJA:** " + d.MissionFullStatement);
                if (!string.IsNullOrWhiteSpace(d.SyncedCommsMatrixSummary))
                    sb.AppendLine("```text\n" + d.SyncedCommsMatrixSummary + "\n```");
                if (d.IncludeCustomChannelsTable && !string.IsNullOrWhiteSpace(d.CustomChannelsTableText))
                    sb.AppendLine("**Kanały dodatkowe:**\n" + d.CustomChannelsTableText);
                sb.AppendLine("**🔐 SARNEG:** `" + d.SarnegKeyWord10 + "` | **Hasła:** " + d.ChallengeAndPassword + "\n");
            }
            return sb.ToString().TrimEnd();
        }
    }
}
