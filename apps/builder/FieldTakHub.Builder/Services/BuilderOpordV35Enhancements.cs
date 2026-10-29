#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace FieldTakHub.Builder.Services
{
    internal static class BuilderOpordV35Enhancements
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        private static OpordParagraphModel _cachedLiveModel;
        private static MethodInfo _cachedRebuildMethod;
        public static bool IsRebuildingOpord { get; set; }

        static BuilderOpordV35Enhancements()
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("FieldTakHubBuilder/4.0");
            TacticalSharedState.OnOpordTabActivated = win => OnModuleSwitched(win);
        }

        public static void OnModuleSwitched(Window win)
        {
            var w = win ?? Application.Current?.MainWindow;
            if (w == null || IsRebuildingOpord) return;
            try
            {
                TacticalSharedState.CaptureLiveStateFromWindow(w);
                var m = FindOpordModelFast();
                var d = (m?.Days != null && m.Days.Count > 0) ? m.Days[Math.Clamp(m.ActiveDayIndex, 0, m.Days.Count - 1)] : null;
                if (m != null && d != null)
                {
                    SyncAllFromGenerators(w, m, d);
                    InvokeRebuildOpordForm(w);
                }
            }
            catch { }
        }

        private static void InvokeRebuildOpordForm(Window win)
        {
            if (IsRebuildingOpord) return;
            try
            {
                IsRebuildingOpord = true;
                if (_cachedRebuildMethod == null)
                    _cachedRebuildMethod = typeof(OpordBuilderManager).GetMethod("RebuildOpordForm", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                _cachedRebuildMethod?.Invoke(null, new object[] { win ?? Application.Current?.MainWindow });
            }
            catch { }
            finally { IsRebuildingOpord = false; }
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

        public static string LatLonToMgrs(double lat, double lon)
            => BuilderTacticalMapAndUsbService.LatLonToMgrs(lat, lon);

        public static void SyncAllFromGenerators(Window win, OpordParagraphModel model, OpordDayPlanModel activeDay)
        {
            var w = win ?? Application.Current?.MainWindow;
            if (model != null) _cachedLiveModel = model;
            else model = FindOpordModelFast();

            if (model == null) return;
            if (activeDay == null && model.Days != null && model.Days.Count > 0)
                activeDay = model.Days[Math.Clamp(model.ActiveDayIndex, 0, model.Days.Count - 1)];
            if (activeDay == null) return;

            var allFactions = FactionLiveSyncBridge.ReadAllFactionsFromScreenAndMemory(w);
            if (allFactions.Count > 0)
            {
                var matched = allFactions.FirstOrDefault(x => string.Equals(x.FactionName, model.TargetFaction, StringComparison.OrdinalIgnoreCase)) ?? allFactions[0];
                model.TargetFaction = matched.FactionName;

                if (matched.VoiceChannels.Count > 0)
                {
                    var c0 = matched.VoiceChannels[0];
                    activeDay.PrimaryChannel = c0.Role + ": " + c0.Freq + " [" + c0.Band + "] (" + c0.Tone + ")";
                    if (matched.VoiceChannels.Count > 1)
                    {
                        var c1 = matched.VoiceChannels[1];
                        activeDay.AlternateChannel = c1.Role + ": " + c1.Freq + " [" + c1.Band + "] (" + c1.Tone + ")";
                    }
                    if (matched.VoiceChannels.Count > 2)
                    {
                        var c2 = matched.VoiceChannels[2];
                        activeDay.HqChannel = c2.Role + ": " + c2.Freq + " [" + c2.Band + "] (" + c2.Tone + ")";
                    }
                }

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

            var mapState = BuilderTacticalMapAndUsbService.InspectLiveMapStudio(w);
            if (mapState != null && mapState.HasExplicitAoOrPois)
            {
                activeDay.LocationLat = mapState.AoCenterLat.ToString("F4", CultureInfo.InvariantCulture);
                activeDay.LocationLon = mapState.AoCenterLon.ToString("F4", CultureInfo.InvariantCulture);
                string formattedCoord = BuilderTacticalMapAndUsbService.FormatCoordinate(mapState.AoCenterLat, mapState.AoCenterLon, mapState.CoordinateSystem);

                var psb = new StringBuilder();
                psb.AppendLine("Układ współrzędnych mapy: " + mapState.CoordinateSystem + " | Środek AO: " + formattedCoord);
                foreach (var p in mapState.Pois)
                    psb.AppendLine("• " + p.Name + " [" + p.Category + "]: " + BuilderTacticalMapAndUsbService.FormatCoordinate(p.Lat, p.Lon, mapState.CoordinateSystem));
                activeDay.AreaOfInterestPoiDesc = psb.ToString().TrimEnd();
            }
        }

        public static void EnhanceLiveOpordTree(Window win, List<DependencyObject> safeElements = null) => EnhanceOpordDirectAfterBuild(win);
        public static void AttachOpordUiExtensions(Window win, OpordParagraphModel model, OpordDayPlanModel activeDay, StackPanel mainStack, Action rebuildCallback) => UpgradeOpordMainStack(mainStack);
        public static void TransformBuiltOpordStack(Window win, OpordParagraphModel model, OpordDayPlanModel activeDay, StackPanel mainStack, Action rebuildCallback) => UpgradeOpordMainStack(mainStack);

        /// <summary>
        /// Wywoływane automatycznie na końcu RebuildOpordForm(Window win).
        /// Znajduje główny StackPanel formularza OPORD i przebudowuje go do standardu v4.0.
        /// </summary>
        public static void EnhanceOpordDirectAfterBuild(Window win)
        {
            var w = win ?? Application.Current?.MainWindow;
            try
            {
                var all = FactionLiveSyncBridge.GetAllRootVisualsAcrossTabs(w);
                var titleTb = all.OfType<TextBlock>().FirstOrDefault(t =>
                    (t.Text ?? "").Contains("GENERATOR ROZKAZU BOJOWEGO", StringComparison.OrdinalIgnoreCase));

                if (titleTb == null) return;

                DependencyObject cur = titleTb;
                StackPanel mainOpordStack = null;
                while (cur != null && !(cur is Window))
                {
                    if (cur is StackPanel sp && sp.Children.Count >= 3)
                    {
                        mainOpordStack = sp;
                        break;
                    }
                    cur = LogicalTreeHelper.GetParent(cur) ?? VisualTreeHelper.GetParent(cur);
                }

                if (mainOpordStack != null)
                {
                    UpgradeOpordMainStack(mainOpordStack);
                }
            }
            catch { }
        }

        public static void UpgradeOpordMainStack(StackPanel mainStack)
        {
            if (mainStack == null || mainStack.Tag?.ToString() == "FTH_V4_UPGRADED") return;
            mainStack.Tag = "FTH_V4_UPGRADED";

            var win = Application.Current?.MainWindow;
            var model = FindOpordModelFast();
            if (model == null || model.Days == null || model.Days.Count == 0) return;
            var activeDay = model.Days[Math.Clamp(model.ActiveDayIndex, 0, model.Days.Count - 1)];

            SyncAllFromGenerators(win, model, activeDay);

            var opordElements = BuilderPackageAndUiPatcher.GetLogicalElements(mainStack);

            // Napraw wszystkie małe przyciski usuwania wiersza ("−"), aby miały Padding = 0 i nigdy nie pokazywały ":"
            foreach (var b in opordElements.OfType<Button>())
            {
                string bt = BuilderPackageAndUiPatcher.ExtractButtonText(b).Trim();
                if ((b.Width >= 20 && b.Width <= 42) || bt == "-" || bt == "−" || bt == "✖" || bt == ":")
                {
                    b.Padding = new Thickness(0);
                    b.HorizontalContentAlignment = HorizontalAlignment.Center;
                    b.VerticalContentAlignment = VerticalAlignment.Center;
                    if (bt == "-" || bt == ":") b.Content = "−";
                }
            }

            // 1. GÓRNY BOKS NAGŁÓWKOWY OPORD:
            //    - Wyczyść ComboBox stron konfliktu z pozycji PLI / GeoChat i dodaj strony z Generatora Stron
            //    - Podłącz przycisk "🔄 Pobierz z Generatora Stron" na samej górze
            //    - Podłącz bezpośrednie kopiowanie na Discorda bez pustego okna
            if (mainStack.Children.Count > 0 && mainStack.Children[0] is Border topBorder && topBorder.Child is StackPanel topHeaderStack)
            {
                var topElems = BuilderPackageAndUiPatcher.GetLogicalElements(topHeaderStack);
                var factionCombo = topElems.OfType<ComboBox>().FirstOrDefault();
                if (factionCombo != null)
                {
                    var liveNames = FactionLiveSyncBridge.GetAllFactionNamesFromUi(win);
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
                    foreach (string fn in liveNames)
                    {
                        if (!factionCombo.Items.Cast<object>().Any(x => string.Equals(x?.ToString(), fn, StringComparison.OrdinalIgnoreCase)))
                            factionCombo.Items.Add(fn);
                    }
                    foreach (var item in factionCombo.Items)
                    {
                        if (string.Equals(item?.ToString(), model.TargetFaction, StringComparison.OrdinalIgnoreCase))
                        {
                            factionCombo.SelectedItem = item;
                            break;
                        }
                    }
                }

                // Podłącz bezpośredni eksport na Discorda (bez pustego okna!)
                foreach (var btn in topElems.OfType<Button>())
                {
                    string bt = BuilderPackageAndUiPatcher.ExtractButtonText(btn);
                    if (bt.Contains("Discord", StringComparison.OrdinalIgnoreCase) && btn.Tag?.ToString() != "FTH_DISCORD_DIRECT")
                    {
                        btn.Tag = "FTH_DISCORD_DIRECT";
                        btn.PreviewMouseLeftButtonDown += (s, e) =>
                        {
                            e.Handled = true;
                            SyncAllFromGenerators(win, model, activeDay);
                            string md = BuildCondensedDiscordMarkdown(model);
                            if (!string.IsNullOrWhiteSpace(md))
                            {
                                Clipboard.SetText(md);
                                var chunks = BuildCondensedDiscordChunks(model);
                                MessageBox.Show(
                                    "✅ Skopiowano skondensowany rozkaz OPORD bezpośrednio do schowka!\n" +
                                    "Liczba wiadomości Discord (<1900 zn.): " + chunks.Count + "\n\n" +
                                    "Możesz teraz wkleić rozkaz na kanał Discord kombinacją Ctrl + V.",
                                    "💬 Kopiuj na Discorda",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Information);
                            }
                        };
                    }
                    else if (bt.Contains("Pobierz dane z Gen", StringComparison.OrdinalIgnoreCase) ||
                             bt.Contains("Pobierz z Generatora Stron", StringComparison.OrdinalIgnoreCase))
                    {
                        btn.PreviewMouseLeftButtonDown += (s, e) =>
                        {
                            e.Handled = true;
                            TacticalSharedState.CaptureLiveStateFromWindow(win);
                            SyncAllFromGenerators(win, model, activeDay);
                            InvokeRebuildOpordForm(win);
                        };
                    }
                }

                bool hasTopSync = topElems.OfType<Button>().Any(b =>
                    BuilderPackageAndUiPatcher.ExtractButtonText(b).Contains("Pobierz", StringComparison.OrdinalIgnoreCase) &&
                    BuilderPackageAndUiPatcher.ExtractButtonText(b).Contains("Generatora Stron", StringComparison.OrdinalIgnoreCase));

                if (!hasTopSync)
                {
                    var topSyncBtn = new Button
                    {
                        Content = "🔄 Pobierz z Generatora Stron (Odśwież strony, kanały radiowe i współrzędne AO)",
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
                        TacticalSharedState.CaptureLiveStateFromWindow(win);
                        SyncAllFromGenerators(win, model, activeDay);
                        InvokeRebuildOpordForm(win);
                    };
                    topHeaderStack.Children.Add(topSyncBtn);
                }
            }

            // 2. PRZYCISK "☑️ Generuj tylko aktywny dzień":
            Button genAllDaysBtn = null;
            Button activeDayOnlyBtn = null;
            foreach (var btn in opordElements.OfType<Button>())
            {
                string txt = BuilderPackageAndUiPatcher.ExtractButtonText(btn);
                if (txt.Contains("Generuj wszystkie dni", StringComparison.OrdinalIgnoreCase)) genAllDaysBtn = btn;
                else if (txt.Contains("aktywny dzień", StringComparison.OrdinalIgnoreCase) || txt.Contains("active day", StringComparison.OrdinalIgnoreCase)) activeDayOnlyBtn = btn;
            }
            if (activeDayOnlyBtn != null)
            {
                activeDayOnlyBtn.Content = "☑️ Generuj tylko aktywny dzień";
                if (genAllDaysBtn != null)
                {
                    activeDayOnlyBtn.Background = genAllDaysBtn.Background;
                    activeDayOnlyBtn.Foreground = genAllDaysBtn.Foreground;
                    activeDayOnlyBtn.BorderBrush = genAllDaysBtn.BorderBrush;
                    activeDayOnlyBtn.BorderThickness = genAllDaysBtn.BorderThickness;
                    activeDayOnlyBtn.Padding = genAllDaysBtn.Padding;
                    activeDayOnlyBtn.Height = genAllDaysBtn.Height;
                    activeDayOnlyBtn.FontWeight = genAllDaysBtn.FontWeight;
                }
                activeDayOnlyBtn.PreviewMouseLeftButtonDown += (s, e) =>
                {
                    e.Handled = true;
                    int activeIdx = Math.Clamp(model.ActiveDayIndex, 0, model.Days.Count - 1);
                    for (int i = 0; i < model.Days.Count; i++)
                        model.Days[i].IncludeInExport = (i == activeIdx);
                    InvokeRebuildOpordForm(win);
                };
            }

            // 3. SEKCJA B.2 (METOC): Pełna obsługa zaznaczonego AO z Mapy 1:1 i komunikat przy pustym polu
            UpgradeMetocSection(win, opordElements, activeDay);

            // 4. SEKCJA E (TABELA ŁĄCZNOŚCI): Ukrycie 3 starych pól, automatyczne kanały z Generatora Stron + opcjonalna tabela na "ptaszek"
            UpgradeSectionEComms(opordElements, activeDay);

            // 5. SEKCJA IV (LOGISTYKA I ZAŁĄCZNIKI): Dodanie Załącznika nr 3 — Mapa (na "ptaszek")
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

        private static void UpgradeMetocSection(Window win, List<DependencyObject> opordElements, OpordDayPlanModel activeDay)
        {
            var metocHeader = opordElements.OfType<TextBlock>().FirstOrDefault(t =>
                (t.Text ?? "").Contains("B.2. PROGNOZA POGODY", StringComparison.OrdinalIgnoreCase));

            if (metocHeader == null) return;

            DependencyObject p = LogicalTreeHelper.GetParent(metocHeader);
            while (p != null && !(p is StackPanel)) p = LogicalTreeHelper.GetParent(p);
            if (!(p is StackPanel metocStack)) return;

            var metocElems = BuilderPackageAndUiPatcher.GetLogicalElements(metocStack);
            var boxes = metocElems.OfType<TextBox>().ToList();
            TextBox dateBox = boxes.Count >= 1 ? boxes[0] : null;
            TextBox locBox = boxes.Count >= 2 ? boxes[1] : null;
            TextBox latBox = boxes.Count >= 3 ? boxes[2] : null;
            TextBox lonBox = boxes.Count >= 4 ? boxes[3] : null;

            TextBlock summaryTb = metocElems.OfType<TextBlock>().FirstOrDefault(t =>
                (t.Text ?? "").Contains("BMNT", StringComparison.OrdinalIgnoreCase) ||
                (t.Text ?? "").Contains("METOC", StringComparison.OrdinalIgnoreCase) && t != metocHeader);

            var mapState = BuilderTacticalMapAndUsbService.InspectLiveMapStudio(win);
            bool hasAo = mapState != null && mapState.HasExplicitAoOrPois;

            if (hasAo)
            {
                string latS = mapState.AoCenterLat.ToString("F4", CultureInfo.InvariantCulture);
                string lonS = mapState.AoCenterLon.ToString("F4", CultureInfo.InvariantCulture);
                if (latBox != null) latBox.Text = latS;
                if (lonBox != null) lonBox.Text = lonS;
                activeDay.LocationLat = latS;
                activeDay.LocationLon = lonS;
            }
            else if (locBox != null && string.IsNullOrWhiteSpace(locBox.Text))
            {
                if (latBox != null) latBox.Text = "";
                if (lonBox != null) lonBox.Text = "";
                activeDay.LocationLat = "";
                activeDay.LocationLon = "";
            }

            foreach (var btn in metocElems.OfType<Button>())
            {
                string bt = BuilderPackageAndUiPatcher.ExtractButtonText(btn);
                if (bt.Contains("Pobierz METOC", StringComparison.OrdinalIgnoreCase) || bt.Contains("Fetch METOC", StringComparison.OrdinalIgnoreCase))
                {
                    btn.PreviewMouseLeftButtonDown += async (s, e) =>
                    {
                        e.Handled = true;
                        await ExecuteMetocFetchWithValidationAsync(win, activeDay, dateBox, locBox, latBox, lonBox, summaryTb, opordElements);
                    };
                }
            }
        }

        public static async Task ExecuteMetocFetchWithValidationAsync(
            Window win,
            OpordDayPlanModel activeDay,
            TextBox dateBox,
            TextBox locBox,
            TextBox latBox,
            TextBox lonBox,
            TextBlock summaryBlock,
            List<DependencyObject> opordElements)
        {
            if (activeDay == null) return;

            string locTyped = (locBox?.Text ?? activeDay.LocationName ?? "").Trim();
            string dateIso = (dateBox?.Text ?? activeDay.DateIso ?? DateTime.Now.ToString("yyyy-MM-dd")).Trim();

            var mapState = BuilderTacticalMapAndUsbService.InspectLiveMapStudio(win);
            bool hasExplicitAo = mapState != null && mapState.HasExplicitAoOrPois;

            if (string.IsNullOrWhiteSpace(locTyped) && !hasExplicitAo)
            {
                if (latBox != null) latBox.Text = "";
                if (lonBox != null) lonBox.Text = "";
                activeDay.LocationLat = "";
                activeDay.LocationLon = "";

                MessageBox.Show(
                    "⚠️ Nie można pobrać danych METOC dla samego arkusza mapy!\n\n" +
                    "Proszę zaznaczyć obszar gry (AO) w Generatorze Map 1:1 LUB wpisać nazwę miejscowości w polu 'Miejscowość'.",
                    "METOC — Zaznacz AO lub wpisz miejscowość",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            double targetLat = 0;
            double targetLon = 0;
            string resolvedLocation = locTyped;

            try
            {
                if (!string.IsNullOrWhiteSpace(locTyped) && !locTyped.StartsWith("Obszar AO", StringComparison.OrdinalIgnoreCase))
                {
                    string geoUrl = "https://geocoding-api.open-meteo.com/v1/search?name=" + Uri.EscapeDataString(locTyped) + "&count=1&language=pl&format=json";
                    string geoJson = await _http.GetStringAsync(geoUrl);
                    var mLat = Regex.Match(geoJson, "\"latitude\"\\s*:\\s*([-0-9.]+)");
                    var mLon = Regex.Match(geoJson, "\"longitude\"\\s*:\\s*([-0-9.]+)");
                    var mName = Regex.Match(geoJson, "\"name\"\\s*:\\s*\"([^\"]+)\"");

                    if (mLat.Success && mLon.Success &&
                        double.TryParse(mLat.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out targetLat) &&
                        double.TryParse(mLon.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out targetLon))
                    {
                        if (mName.Success) resolvedLocation = mName.Groups[1].Value;
                    }
                    else if (hasExplicitAo)
                    {
                        targetLat = mapState.AoCenterLat;
                        targetLon = mapState.AoCenterLon;
                    }
                    else
                    {
                        MessageBox.Show("Nie znaleziono miejscowości: '" + locTyped + "'. Sprawdź pisownię lub zaznacz AO w Generatorze Map 1:1.", "METOC", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
                else if (hasExplicitAo)
                {
                    targetLat = mapState.AoCenterLat;
                    targetLon = mapState.AoCenterLon;
                    string mgrs = BuilderTacticalMapAndUsbService.FormatCoordinate(targetLat, targetLon, mapState.CoordinateSystem);
                    resolvedLocation = "Obszar AO (" + mapState.CoordinateSystem + ": " + mgrs + ")";
                    if (locBox != null) locBox.Text = resolvedLocation;
                }

                string latFormatted = targetLat.ToString("F4", CultureInfo.InvariantCulture);
                string lonFormatted = targetLon.ToString("F4", CultureInfo.InvariantCulture);
                if (latBox != null) latBox.Text = latFormatted;
                if (lonBox != null) lonBox.Text = lonFormatted;
                activeDay.LocationLat = latFormatted;
                activeDay.LocationLon = lonFormatted;
                activeDay.LocationName = resolvedLocation;

                string wxUrl = "https://api.open-meteo.com/v1/forecast?latitude=" + latFormatted + "&longitude=" + lonFormatted + "&daily=temperature_2m_max,temperature_2m_min,apparent_temperature_max,apparent_temperature_min,precipitation_sum,precipitation_probability_max,wind_speed_10m_max,wind_gusts_10m_max,wind_direction_10m_dominant,sunrise,sunset&timezone=auto";
                string wxJson = await _http.GetStringAsync(wxUrl);

                string sr = ExtractTime(wxJson, "sunrise", "06:00");
                string ss = ExtractTime(wxJson, "sunset", "18:00");
                string bmnt = ShiftMinutes(sr, -34);
                string eent = ShiftMinutes(ss, +34);

                double tMin = ExtractDouble(wxJson, "temperature_2m_min", 8.0);
                double tMax = ExtractDouble(wxJson, "temperature_2m_max", 16.0);
                double aMin = ExtractDouble(wxJson, "apparent_temperature_min", tMin - 1.5);
                double aMax = ExtractDouble(wxJson, "apparent_temperature_max", tMax - 0.5);
                double pMm = ExtractDouble(wxJson, "precipitation_sum", 0.0);
                double pProb = ExtractDouble(wxJson, "precipitation_probability_max", 15.0);
                double wSpd = ExtractDouble(wxJson, "wind_speed_10m_max", 12.0);
                double wGst = ExtractDouble(wxJson, "wind_gusts_10m_max", 19.0);
                double wDeg = ExtractDouble(wxJson, "wind_direction_10m_dominant", 240.0);

                DateTime dt = DateTime.TryParse(dateIso, out DateTime parsedDt) ? parsedDt : DateTime.Now;
                var moon = CalcMoon(dt);
                string card = DegToCardinal(wDeg);

                var sb = new StringBuilder();
                sb.AppendLine("☀️ BMNT (Świt): " + bmnt + "  |  🌅 Wschód (SR): " + sr + "  |  🌇 Zachód (SS): " + ss + "  |  🌌 EENT (Zmierzch NVG): " + eent);
                sb.AppendLine("🌙 " + moon.Item1 + "  |  Iluminacja: " + moon.Item2 + "%  |  " + (moon.Item2 >= 45 ? "Dobra widoczność pasywna NVG" : "Niska iluminacja — zalecane oświetlacze IR"));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "🌡️ Temp: {0:F1}°C .. {1:F1}°C  (Odczuwalna: {2:F1}°C .. {3:F1}°C)  |  Opady: {4:F1} mm (Szansa: {5:0}%)", tMin, tMax, aMin, aMax, pMm, pProb));
                sb.Append(string.Format(CultureInfo.InvariantCulture, "💨 {0:F1} km/h ({1:F1} m/s)  |  Porywy: {2:F1} km/h ({3:F1} m/s)  |  Kierunek: {4:0}° ({5})", wSpd, wSpd / 3.6, wGst, wGst / 3.6, wDeg, card));

                string summaryStr = sb.ToString();
                if (summaryBlock != null) summaryBlock.Text = summaryStr;

                string fullReport = "[METOC " + dateIso + " — " + resolvedLocation + "]\r\n" + summaryStr;
                activeDay.TerrainAndWeather = fullReport;

                // Zaktualizuj również pole tekstowe "B. Teren i Pogoda (METOC)" poniżej
                if (opordElements != null)
                {
                    foreach (var tb in opordElements.OfType<TextBox>())
                    {
                        var parent = LogicalTreeHelper.GetParent(tb) as Panel;
                        if (parent != null && parent.Children.OfType<TextBlock>().Any(l => (l.Text ?? "").Contains("B. Teren i Pogoda", StringComparison.OrdinalIgnoreCase)))
                        {
                            tb.Text = fullReport;
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Błąd podczas pobierania METOC: " + ex.Message, "METOC", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static string ExtractTime(string json, string key, string fallback)
        {
            var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\\[\\s*\"[^\"]*T([0-9]{2}:[0-9]{2})\"");
            return m.Success ? m.Groups[1].Value : fallback;
        }

        private static double ExtractDouble(string json, string key, double fallback)
        {
            var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\\[\\s*([-0-9.]+)");
            return (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double v)) ? v : fallback;
        }

        private static string ShiftMinutes(string hhmm, int mins)
        {
            if (TimeSpan.TryParse(hhmm, out TimeSpan ts))
            {
                var r = ts.Add(TimeSpan.FromMinutes(mins));
                if (r.TotalMinutes < 0) r = r.Add(TimeSpan.FromHours(24));
                return string.Format("{0:00}:{1:00}", (int)r.TotalHours % 24, r.Minutes);
            }
            return hhmm;
        }

        private static Tuple<string, int> CalcMoon(DateTime dt)
        {
            double lp = 2551443.0;
            DateTime refMoon = new DateTime(1970, 1, 7, 20, 35, 0, DateTimeKind.Utc);
            double secs = ((dt.ToUniversalTime() - refMoon).TotalSeconds) % lp;
            if (secs < 0) secs += lp;
            double r = secs / lp;
            int illum = (int)Math.Round((1.0 - Math.Cos(r * 2.0 * Math.PI)) * 50.0);
            string n = r < 0.06 || r > 0.94 ? "🌑 Nów" : r < 0.22 ? "🌒 Przybywający Sierp" : r < 0.30 ? "🌓 Pierwsza Kwadra" : r < 0.46 ? "🌔 Przybywający Garb" : r < 0.55 ? "🌕 Pełnia" : r < 0.70 ? "🌖 Ubywający Garb" : r < 0.79 ? "🌗 Ostatnia Kwadra" : "🌘 Ubywający Sierp";
            return Tuple.Create(n, illum);
        }

        private static string DegToCardinal(double deg)
        {
            string[] d = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            return d[(int)Math.Round(((deg % 360) + 360) % 360 / 45.0) % 8];
        }

        private static void UpgradeSectionEComms(List<DependencyObject> opordElements, OpordDayPlanModel activeDay)
        {
            var secEHeader = opordElements.OfType<TextBlock>().FirstOrDefault(t =>
                (t.Text ?? "").Contains("E. Tabela łączności", StringComparison.OrdinalIgnoreCase));

            if (secEHeader == null) return;
            secEHeader.Text = "E. Tabela łączności (" + activeDay.DayTitle + " — Automatycznie z Generatora Stron + opcjonalna tabela ręczna):";

            if (LogicalTreeHelper.GetParent(secEHeader) is Panel headerParent)
            {
                foreach (var b in BuilderPackageAndUiPatcher.GetLogicalElements(headerParent).OfType<Button>())
                {
                    if (BuilderPackageAndUiPatcher.ExtractButtonText(b).Contains("Pobierz z Generatora Stron", StringComparison.OrdinalIgnoreCase))
                    {
                        b.Visibility = Visibility.Collapsed; b.Width = 0; b.Height = 0;
                    }
                }
            }

            DependencyObject secParent = LogicalTreeHelper.GetParent(secEHeader);
            while (secParent != null && !(secParent is StackPanel)) secParent = LogicalTreeHelper.GetParent(secParent);
            if (!(secParent is StackPanel secStack)) return;

            foreach (var tb in BuilderPackageAndUiPatcher.GetLogicalElements(secStack).OfType<TextBox>())
            {
                if (LogicalTreeHelper.GetParent(tb) is Panel p)
                {
                    string lbl = string.Join(" ", p.Children.OfType<TextBlock>().Select(x => x.Text ?? ""));
                    if (lbl.Contains("Kanał główny", StringComparison.OrdinalIgnoreCase) ||
                        lbl.Contains("Kanał zapasowy", StringComparison.OrdinalIgnoreCase) ||
                        lbl.Contains("Kanał HQ", StringComparison.OrdinalIgnoreCase))
                    {
                        p.Visibility = Visibility.Collapsed; p.Height = 0; p.Margin = new Thickness(0);
                        if (LogicalTreeHelper.GetParent(p) is Grid rg && rg.ColumnDefinitions.Count == 3)
                        { rg.Visibility = Visibility.Collapsed; rg.Height = 0; rg.Margin = new Thickness(0); }
                    }
                }
            }

            foreach (var txt in BuilderPackageAndUiPatcher.GetLogicalElements(secStack).OfType<TextBlock>())
            {
                if ((txt.Text ?? "").Contains("[ZACIĄGNIĘTE Z GENERATORA STRON", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(activeDay.SyncedCommsMatrixSummary))
                    txt.Text = activeDay.SyncedCommsMatrixSummary;
            }

            var customBox = BuildOptionalManualCommsTable(activeDay);
            int insIdx = secStack.Children.Count;
            for (int i = 0; i < secStack.Children.Count; i++)
            {
                if (secStack.Children[i] is TextBlock tb && (tb.Text ?? "").Contains("F.1.", StringComparison.OrdinalIgnoreCase))
                { insIdx = i; break; }
            }
            secStack.Children.Insert(insIdx, customBox);
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

                var del = new Button { Content = "−", Width = 32, Height = 30, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Background = new SolidColorBrush(Color.FromRgb(85, 22, 22)), Foreground = Brushes.White, FontWeight = FontWeights.Bold, Cursor = Cursors.Hand };
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
            var chunks = BuildCondensedDiscordChunks(m);
            if (chunks.Count == 0) return "";
            if (chunks.Count == 1) return chunks[0];
            var sb = new StringBuilder();
            for (int i = 0; i < chunks.Count; i++)
            {
                if (i > 0) sb.AppendLine("\n--- ✂️ [WIADOMOŚĆ " + (i + 1) + "/" + chunks.Count + "] ✂️ ---\n");
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
                var b1 = new StringBuilder();
                b1.AppendLine("## 📋 " + d.OperationNumber + " | " + m.TargetFaction + " — " + d.DayTitle + " (" + d.DateIso + ")");
                b1.AppendLine("`" + d.Classification + "` | **HQ:** " + d.IssuingHq + " | **AO:** " + d.LocationName);
                b1.AppendLine("**⏱️ Harmonogram:** " + CompactInline(d.DaySpecificTasks));
                b1.AppendLine("**🌦️ METOC:** " + CompactInline(d.TerrainAndWeather));
                b1.AppendLine("**🎯 II. MISJA (5W):**\n> " + d.MissionFullStatement);
                b1.AppendLine("**⚡ III. ZAMIAR:** " + d.CommandersIntent + " | **Dowodzenie:** `" + d.SuccessionOfCommand + "`");

                var b2 = new StringBuilder();
                b2.AppendLine("### 📡 ŁĄCZNOŚĆ, SARNEG & LOGISTYKA — " + d.DayTitle + " (" + m.TargetFaction + ")");
                if (!string.IsNullOrWhiteSpace(d.SyncedCommsMatrixSummary))
                    b2.AppendLine("```text\n" + d.SyncedCommsMatrixSummary + "\n```");
                if (d.IncludeCustomChannelsTable && !string.IsNullOrWhiteSpace(d.CustomChannelsTableText))
                    b2.AppendLine("• **Kanały dodatkowe (OPORD):**\n" + d.CustomChannelsTableText.Trim());

                b2.AppendLine("**🔐 SARNEG (" + d.DayTitle + "):** `" + d.SarnegKeyWord10 + "` | **Hasła:** " + CompactInline(d.ChallengeAndPassword));
                b2.AppendLine("**🚑 MEDEVAC:** " + d.MedevacRules);
                b2.AppendLine("**🎒 Szpej:** " + d.SquadGearChecklist);

                string s1 = b1.ToString().Trim();
                string s2 = b2.ToString().Trim();
                if (s1.Length + s2.Length + 2 <= 1850) chunks.Add(s1 + "\n\n" + s2);
                else { chunks.Add(s1); chunks.Add(s2); }
            }
            return chunks;
        }

        private static string CompactInline(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "-";
            return string.Join(" • ", text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0));
        }
    }
}
