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
using System.Windows.Threading;
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
        private static OpordParagraphModel _cachedLiveModel;
        private static OpordDayPlanModel _cachedLiveDay;
        private static MethodInfo _cachedExtractMapMethod;
        private static MethodInfo _cachedOrigSyncMethod;
        private static MethodInfo _cachedRebuildMethod;
        private static FieldInfo _cachedHostField;
        private static bool _methodsSearched;
        public static bool IsRebuildingOpord;

        private static void EnsureReflectionCached()
        {
            if (_methodsSearched) return;
            _methodsSearched = true;
            var mgrType = typeof(OpordBuilderManager);
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            _cachedExtractMapMethod = mgrType.GetMethod("GetExtractedMapData", flags) ?? mgrType.GetMethod("ExtractMapData", flags);
            _cachedOrigSyncMethod = mgrType.GetMethod("SyncCommsFromFactionGenerator", flags);
            _cachedRebuildMethod = mgrType.GetMethod("RebuildOpordForm", flags);

            foreach (var f in mgrType.GetFields(flags))
            {
                if (typeof(Panel).IsAssignableFrom(f.FieldType))
                {
                    _cachedHostField = f;
                    break;
                }
            }
        }

        /// <summary>
        /// Wywoływane automatycznie w chwili przełączenia zakładki w lewym menu bocznym.
        /// Jeśli użytkownik przełączył na Generator OPORD -> automatycznie odświeża listę stron i kanały z Generatora Stron!
        /// </summary>
        public static void OnModuleSwitched(Window win)
        {
            if (win == null) return;
            win.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    EnsureReflectionCached();
                    var hostPanel = _cachedHostField?.GetValue(null) as Panel;
                    if (hostPanel != null && hostPanel.IsVisible)
                    {
                        var model = FindOpordModelFast();
                        var day = (model?.Days != null && model.Days.Count > 0)
                            ? model.Days[Math.Clamp(model.ActiveDayIndex, 0, model.Days.Count - 1)]
                            : null;
                        if (model != null && day != null)
                        {
                            try { _cachedOrigSyncMethod?.Invoke(null, new object[] { win, model, day }); } catch { }
                            SyncAllFromGenerators(win, model, day);
                            _cachedRebuildMethod?.Invoke(null, new object[] { win });
                        }
                    }
                }
                catch { }
            }), DispatcherPriority.Loaded);
        }

        public static ExtractedMapSnapshot ExtractMapDataSafe(Window win)
        {
            var snap = new ExtractedMapSnapshot();
            try
            {
                EnsureReflectionCached();
                if (_cachedExtractMapMethod != null && _cachedExtractMapMethod.GetParameters().Length == 0)
                {
                    var res = _cachedExtractMapMethod.Invoke(null, null);
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

        private static OpordParagraphModel FindOpordModelFast()
        {
            if (_cachedLiveModel != null) return _cachedLiveModel;
            try
            {
                var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                foreach (var f in typeof(OpordBuilderManager).GetFields(flags))
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

        public static void SyncAllFromGenerators(Window win, OpordParagraphModel model, OpordDayPlanModel activeDay)
        {
            EnsureReflectionCached();
            if (model != null) _cachedLiveModel = model;
            else model = FindOpordModelFast();

            if (activeDay != null) _cachedLiveDay = activeDay;
            else if (model != null && model.Days != null && model.Days.Count > 0)
                activeDay = model.Days[Math.Clamp(model.ActiveDayIndex, 0, model.Days.Count - 1)];

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

            // Zapisz również do właściwości wewnętrznych klasy dnia (np. SyncedFactionCommsSummary)
            foreach (var prop in activeDay.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (prop.PropertyType == typeof(string) && prop.CanWrite && prop.Name.Contains("Synced", StringComparison.OrdinalIgnoreCase))
                {
                    prop.SetValue(activeDay, activeDay.SyncedCommsMatrixSummary);
                }
            }

            var md = ExtractMapDataSafe(win);
            if (md != null && (md.HasAoPolygon || md.Pois.Count > 0))
            {
                activeDay.LocationLat = md.CenterLat.ToString("F6", CultureInfo.InvariantCulture);
                activeDay.LocationLon = md.CenterLon.ToString("F6", CultureInfo.InvariantCulture);
                string mgrs = LatLonToMgrs(md.CenterLat, md.CenterLon);
                if (string.IsNullOrWhiteSpace(activeDay.LocationName))
                    activeDay.LocationName = $"Rejon AO (MGRS: {mgrs})";

                var psb = new StringBuilder();
                psb.AppendLine($"Układ współrzędnych mapy: MGRS | Środek AO: {mgrs}");
                foreach (var p in md.Pois)
                    psb.AppendLine($"• {p.Name} [{p.Type}]: {LatLonToMgrs(p.Lat, p.Lon)}");
                activeDay.AreaOfInterestPoiDesc = psb.ToString().TrimEnd();
            }
        }

        public static void EnhanceLiveOpordTree(Window win, List<DependencyObject> safeElements = null) { }
        public static void AttachOpordUiExtensions(Window win, OpordParagraphModel model, OpordDayPlanModel activeDay, StackPanel mainStack, Action rebuildCallback) { }

        /// <summary>
        /// Wywoływane gwarantowanie bezpośrednio na końcu metody RebuildOpordForm(Window win).
        /// Modyfikuje zbudowany kontener OPORD (_cachedHostField) przed wyświetleniem!
        /// </summary>
        public static void EnhanceOpordDirectAfterBuild(Window win)
        {
            try
            {
                EnsureReflectionCached();
                var hostPanel = _cachedHostField?.GetValue(null) as Panel;
                if (hostPanel == null) return;

                var model = FindOpordModelFast();
                if (model == null || model.Days == null || model.Days.Count == 0) return;
                var activeDay = model.Days[Math.Clamp(model.ActiveDayIndex, 0, model.Days.Count - 1)];
                _cachedLiveDay = activeDay;

                SyncAllFromGenerators(win, model, activeDay);

                var opordElements = BuilderPackageAndUiPatcher.GetVisualTreeElements(hostPanel, includeCollapsed: true);

                // 1. Górna karta OPORD: odśwież ComboBox stron konfliktu (usuń PLI/GeoChat) i dodaj przycisk "🔄 Pobierz z Generatora Stron" na samej górze
                var opordTitleTb = opordElements.OfType<TextBlock>().FirstOrDefault(t =>
                    (t.Text ?? "").Contains("GENERATOR ROZKAZU BOJOWEGO", StringComparison.OrdinalIgnoreCase));

                if (opordTitleTb != null)
                {
                    DependencyObject topParent = VisualTreeHelper.GetParent(opordTitleTb);
                    while (topParent != null && !(topParent is StackPanel)) topParent = VisualTreeHelper.GetParent(topParent);
                    if (topParent is StackPanel topHeaderStack)
                    {
                        var factionCombo = BuilderPackageAndUiPatcher.GetVisualTreeElements(topHeaderStack, true).OfType<ComboBox>().FirstOrDefault();
                        if (factionCombo != null)
                        {
                            var cleanFactions = FactionLiveSyncBridge.GetAllFactionNamesFromUi(win);
                            for (int i = factionCombo.Items.Count - 1; i >= 0; i--)
                            {
                                string s = factionCombo.Items[i]?.ToString() ?? "";
                                if (s.Equals("PLI", StringComparison.OrdinalIgnoreCase) ||
                                    s.Equals("GeoChat", StringComparison.OrdinalIgnoreCase) ||
                                    s.Equals("TAK", StringComparison.OrdinalIgnoreCase))
                                {
                                    factionCombo.Items.RemoveAt(i);
                                }
                            }
                            foreach (string fn in cleanFactions)
                            {
                                if (!factionCombo.Items.Cast<object>().Any(x => string.Equals(x?.ToString(), fn, StringComparison.OrdinalIgnoreCase)))
                                {
                                    factionCombo.Items.Add(fn);
                                }
                            }
                        }

                        var topSyncBtn = new Button
                        {
                            Content = BuilderI18n.T(
                                "🔄 Pobierz z Generatora Stron i Mapy 1:1 (Odśwież strony, kanały i częstotliwości oraz współrzędne MGRS)",
                                "🔄 Pull from Faction & Map Generators (Refresh Factions, Channels/Frequencies & MGRS Coords)"),
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
                            try { _cachedOrigSyncMethod?.Invoke(null, new object[] { win, model, activeDay }); } catch { }
                            SyncAllFromGenerators(win, model, activeDay);
                            _cachedRebuildMethod?.Invoke(null, new object[] { win });
                        };
                        topHeaderStack.Children.Add(topSyncBtn);
                    }
                }

                // 2. Napraw wygląd i działanie przycisku "☑️ Generuj tylko aktywny dzień"
                PatchActiveDayOnlyButton(win, opordElements, model);

                // 3. Walidacja METOC (komunikat "Zaznacz AO lub wpisz miejscowość" zamiast pobierania dla arkusza mapy)
                PatchMetocSection(win, opordElements, activeDay);

                // 4. Sekcja E: ukryj 3 sztywne pola ręczne, wstaw automatyczne częstotliwości z Generatora Stron oraz opcjonalną tabelę na "ptaszek"
                PatchSectionEComms(opordElements, activeDay);

                // 5. Sekcja IV: dodaj Załącznik nr 3 — Mapa na "ptaszek"
                PatchSectionIVAnnex3Map(win, opordElements, activeDay);
            }
            catch { }
        }

        private static void PatchActiveDayOnlyButton(Window win, List<DependencyObject> opordElements, OpordParagraphModel model)
        {
            Button generateAllDaysBtn = null;
            Button activeDayOnlyBtn = null;

            foreach (var btn in opordElements.OfType<Button>())
            {
                string txt = BuilderPackageAndUiPatcher.ExtractButtonText(btn);
                if (txt.Contains("Generuj wszystkie dni", StringComparison.OrdinalIgnoreCase) || txt.Contains("Generate all days", StringComparison.OrdinalIgnoreCase))
                    generateAllDaysBtn = btn;
                else if (txt.Contains("aktywny dzień", StringComparison.OrdinalIgnoreCase) || txt.Contains("active day", StringComparison.OrdinalIgnoreCase))
                    activeDayOnlyBtn = btn;
            }

            if (activeDayOnlyBtn != null)
            {
                activeDayOnlyBtn.Content = BuilderI18n.T("☑️ Generuj tylko aktywny dzień", "☑️ Generate active day only");
                if (generateAllDaysBtn != null)
                {
                    activeDayOnlyBtn.Background = generateAllDaysBtn.Background;
                    activeDayOnlyBtn.Foreground = generateAllDaysBtn.Foreground;
                    activeDayOnlyBtn.BorderBrush = generateAllDaysBtn.BorderBrush;
                    activeDayOnlyBtn.BorderThickness = generateAllDaysBtn.BorderThickness;
                    activeDayOnlyBtn.Padding = generateAllDaysBtn.Padding;
                    activeDayOnlyBtn.Height = generateAllDaysBtn.Height;
                    activeDayOnlyBtn.FontWeight = generateAllDaysBtn.FontWeight;
                }

                activeDayOnlyBtn.PreviewMouseLeftButtonDown += (s, e) =>
                {
                    e.Handled = true;
                    if (model?.Days != null && model.Days.Count > 0)
                    {
                        int activeIdx = Math.Clamp(model.ActiveDayIndex, 0, model.Days.Count - 1);
                        for (int i = 0; i < model.Days.Count; i++)
                            model.Days[i].IncludeInExport = (i == activeIdx);
                        _cachedRebuildMethod?.Invoke(null, new object[] { win });
                    }
                };
            }
        }

        private static void PatchMetocSection(Window win, List<DependencyObject> opordElements, OpordDayPlanModel activeDay)
        {
            var metocHeader = opordElements.OfType<TextBlock>().FirstOrDefault(t =>
                (t.Text ?? "").Contains("B.2. PROGNOZA POGODY", StringComparison.OrdinalIgnoreCase) ||
                (t.Text ?? "").Contains("WEATHER FORECAST", StringComparison.OrdinalIgnoreCase));

            if (metocHeader == null) return;

            DependencyObject p = VisualTreeHelper.GetParent(metocHeader);
            while (p != null && !(p is StackPanel)) p = VisualTreeHelper.GetParent(p);
            if (!(p is StackPanel metocStack)) return;

            var metocControls = BuilderPackageAndUiPatcher.GetVisualTreeElements(metocStack, includeCollapsed: true);
            var textBoxes = metocControls.OfType<TextBox>().ToList();
            TextBox locBox = textBoxes.Count >= 2 ? textBoxes[1] : null;
            TextBox latBox = textBoxes.Count >= 3 ? textBoxes[2] : null;
            TextBox lonBox = textBoxes.Count >= 4 ? textBoxes[3] : null;

            var mapData = ExtractMapDataSafe(win);
            bool hasAo = mapData != null && (mapData.HasAoPolygon || mapData.Pois.Count > 0);

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
                                    "⚠️ Proszę zaznaczyć obszar AO w Generatorze Map 1:1 lub wpisać miejscowość w polu 'Miejscowość' przed pobraniem danych METOC!",
                                    "⚠️ Please draw an AO polygon in the 1:1 Map Generator or enter a location name before fetching METOC data!"),
                                BuilderI18n.T("METOC — Zaznacz AO lub wpisz miejscowość", "METOC — Select AO or Enter Location"),
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        }
                    };
                }
            }
        }

        private static void PatchSectionEComms(List<DependencyObject> opordElements, OpordDayPlanModel activeDay)
        {
            var secEHeader = opordElements.OfType<TextBlock>().FirstOrDefault(t =>
                (t.Text ?? "").Contains("E. Tabela łączności", StringComparison.OrdinalIgnoreCase) ||
                (t.Text ?? "").Contains("E. Comms Table", StringComparison.OrdinalIgnoreCase));

            if (secEHeader == null) return;

            // Ukryj stary przycisk "Pobierz z Generatora Stron" w Sekcji E (jest przeniesiony na samą górę)
            var headerParent = VisualTreeHelper.GetParent(secEHeader) as Panel;
            if (headerParent != null)
            {
                foreach (var b in BuilderPackageAndUiPatcher.GetVisualTreeElements(headerParent, includeCollapsed: true).OfType<Button>())
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

            // Ukryj 3 sztywne pola ręczne (Kanał główny, Kanał zapasowy, Kanał HQ) pod nagłówkiem E
            foreach (var tb in BuilderPackageAndUiPatcher.GetVisualTreeElements(secStack, includeCollapsed: true).OfType<TextBox>())
            {
                var p = VisualTreeHelper.GetParent(tb) as Panel;
                if (p == null) continue;
                string lbl = string.Join(" ", p.Children.OfType<TextBlock>().Select(x => x.Text ?? ""));
                if (lbl.Contains("Kanał główny", StringComparison.OrdinalIgnoreCase) ||
                    lbl.Contains("Kanał zapasowy", StringComparison.OrdinalIgnoreCase) ||
                    lbl.Contains("Kanał HQ", StringComparison.OrdinalIgnoreCase) ||
                    lbl.Contains("Primary channel", StringComparison.OrdinalIgnoreCase) ||
                    lbl.Contains("Alternate channel", StringComparison.OrdinalIgnoreCase) ||
                    lbl.Contains("HQ channel", StringComparison.OrdinalIgnoreCase))
                {
                    p.Visibility = Visibility.Collapsed;
                    p.Height = 0;
                    p.Margin = new Thickness(0);
                    if (VisualTreeHelper.GetParent(p) is Grid rowGrid && rowGrid.ColumnDefinitions.Count == 3)
                    {
                        rowGrid.Visibility = Visibility.Collapsed;
                        rowGrid.Height = 0;
                        rowGrid.Margin = new Thickness(0);
                    }
                }
            }

            // Zaktualizuj zielone pole z automatycznie zaciągniętymi kanałami i częstotliwościami z Generatora Stron
            foreach (var txt in BuilderPackageAndUiPatcher.GetVisualTreeElements(secStack, includeCollapsed: true).OfType<TextBlock>())
            {
                string t = txt.Text ?? "";
                if (t.Contains("[ZACIĄGNIĘTE Z GENERATORA STRON", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(activeDay.SyncedCommsMatrixSummary))
                {
                    txt.Text = activeDay.SyncedCommsMatrixSummary;
                }
            }

            // Opcjonalna tabela na "ptaszek" z możliwością dodawania kolejnych kanałów ręcznie
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
                    "☑️ Dodaj ręcznie dodatkowe kanały i częstotliwości w OPORD (opcjonalna tabela)",
                    "☑️ Add custom radio channels & frequencies manually in OPORD (optional table)"),
                IsChecked = activeDay.IncludeCustomChannelsTable,
                Foreground = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = Cursors.Hand
            };
            st.Children.Add(chk);

            var bodyPanel = new StackPanel
            {
                Visibility = activeDay.IncludeCustomChannelsTable ? Visibility.Visible : Visibility.Collapsed
            };

            // Nagłówki kolumn opcjonalnej tabeli częstotliwości
            var hdrGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            hdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            hdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.7, GridUnitType.Star) });
            hdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            hdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            hdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });

            string[] colNames = { "Kanał / Przeznaczenie", "Pasmo", "Częstotliwość (MHz / CH)", "Ton CTCSS / Kod", "" };
            for (int cIdx = 0; cIdx < 4; cIdx++)
            {
                var hTb = new TextBlock
                {
                    Text = colNames[cIdx],
                    Foreground = new SolidColorBrush(Color.FromRgb(120, 155, 168)),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(2, 0, 6, 2)
                };
                Grid.SetColumn(hTb, cIdx);
                hdrGrid.Children.Add(hTb);
            }
            bodyPanel.Children.Add(hdrGrid);

            var rowsContainer = new StackPanel();

            void SaveRowsToDayModel()
            {
                var lines = new List<string>();
                foreach (Grid g in rowsContainer.Children.OfType<Grid>())
                {
                    var tbs = g.Children.OfType<TextBox>().ToList();
                    if (tbs.Count >= 4)
                        lines.Add($"• {tbs[0].Text.Trim()} [{tbs[1].Text.Trim()}]: {tbs[2].Text.Trim()} ({tbs[3].Text.Trim()})");
                }
                activeDay.CustomChannelsTableText = string.Join("\n", lines);
            }

            void AddCustomRow(string role, string band, string freq, string tone)
            {
                var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
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
                        Height = 30,
                        VerticalContentAlignment = VerticalAlignment.Center,
                        Padding = new Thickness(8, 2, 8, 2),
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
                    Content = "−",
                    Width = 32,
                    Height = 30,
                    Padding = new Thickness(0),
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Background = new SolidColorBrush(Color.FromRgb(85, 22, 22)),
                    Foreground = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(235, 64, 52)),
                    BorderThickness = new Thickness(1.2),
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Cursor = Cursors.Hand
                };
                delBtn.Click += (_, __) => { rowsContainer.Children.Remove(g); SaveRowsToDayModel(); };
                Grid.SetColumn(delBtn, 4);
                g.Children.Add(delBtn);

                rowsContainer.Children.Add(g);
            }

            AddCustomRow("Kanał główny (Ręczny)", "PMR", "CH 1 (446.00625 MHz)", "CTCSS 67.0 Hz");
            AddCustomRow("Kanał zapasowy", "PMR", "CH 3 (446.03125 MHz)", "CTCSS 88.5 Hz");
            AddCustomRow("Kanał HQ / Specjalny", "VHF/UHF", "446.09375 MHz (PMR CH 8)", "Brak tonu");

            var addRowBtn = new Button
            {
                Content = BuilderI18n.T("➕ Dodaj kolejny kanał ręczny", "➕ Add Another Custom Channel"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12, 5, 12, 5),
                Margin = new Thickness(0, 4, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(18, 48, 58)),
                Foreground = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            addRowBtn.Click += (_, __) =>
            {
                AddCustomRow("Kanał sekcyjny", "PMR", "CH 5 (446.05625 MHz)", "CTCSS 110.9 Hz");
                SaveRowsToDayModel();
            };

            bodyPanel.Children.Add(rowsContainer);
            bodyPanel.Children.Add(addRowBtn);
            st.Children.Add(bodyPanel);

            chk.Checked += (_, __) =>
            {
                activeDay.IncludeCustomChannelsTable = true;
                bodyPanel.Visibility = Visibility.Visible;
                SaveRowsToDayModel();
            };
            chk.Unchecked += (_, __) =>
            {
                activeDay.IncludeCustomChannelsTable = false;
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

        private static void PatchSectionIVAnnex3Map(Window win, List<DependencyObject> opordElements, OpordDayPlanModel activeDay)
        {
            var secIVHeader = opordElements.OfType<TextBlock>().FirstOrDefault(t =>
                (t.Text ?? "").Contains("IV. LOGISTYKA ORAZ ZAŁĄCZNIKI", StringComparison.OrdinalIgnoreCase) ||
                (t.Text ?? "").Contains("IV. LOGISTICS", StringComparison.OrdinalIgnoreCase));

            if (secIVHeader == null) return;

            DependencyObject p = VisualTreeHelper.GetParent(secIVHeader);
            while (p != null && !(p is StackPanel)) p = VisualTreeHelper.GetParent(p);
            if (!(p is StackPanel secIVStack)) return;

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
                    $"☑️ Załącznik 3 ({activeDay.DayTitle}): Dołącz mapę operacyjną / szkic AO do rozkazu OPORD",
                    $"☑️ Annex 3 ({activeDay.DayTitle}): Attach operational map / AO sketch to OPORD"),
                IsChecked = activeDay.IncludeAnnex3Map,
                Foreground = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = Cursors.Hand
            };
            st.Children.Add(chkAnnex3);

            var detailsPanel = new StackPanel
            {
                Visibility = activeDay.IncludeAnnex3Map ? Visibility.Visible : Visibility.Collapsed
            };

            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var pathBox = new TextBox
            {
                Text = activeDay.Annex3MapImagePath ?? "",
                Padding = new Thickness(6, 4, 6, 4),
                Background = new SolidColorBrush(Color.FromRgb(13, 33, 41)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(34, 74, 89)),
                FontSize = 11.5
            };
            pathBox.TextChanged += (_, __) => activeDay.Annex3MapImagePath = pathBox.Text;
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
                    activeDay.Annex3MapImagePath = ofd.FileName;
                }
            };
            Grid.SetColumn(pickBtn, 1);
            row.Children.Add(pickBtn);
            detailsPanel.Children.Add(row);

            chkAnnex3.Checked += (_, __) =>
            {
                activeDay.IncludeAnnex3Map = true;
                detailsPanel.Visibility = Visibility.Visible;
            };
            chkAnnex3.Unchecked += (_, __) =>
            {
                activeDay.IncludeAnnex3Map = false;
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
                sb.AppendLine("<div class='sec'>DODATKOWE KANAŁY RADIOWE (WPISANE RĘCZNIE W OPORD)</div>");
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
            if (m == null) m = FindOpordModelFast();
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
            if (m == null) m = FindOpordModelFast();
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
                if (!string.IsNullOrWhiteSpace(d.SyncedCommsMatrixSummary))
                    b2.AppendLine($"```text\n{d.SyncedCommsMatrixSummary}\n```");
                if (d.IncludeCustomChannelsTable && !string.IsNullOrWhiteSpace(d.CustomChannelsTableText))
                    b2.AppendLine($"• **Kanały dodatkowe (OPORD):**\n{d.CustomChannelsTableText.Trim()}");

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
    }
}
