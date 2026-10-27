using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace FieldTakHub.Builder.Services
{
    internal static class BuilderOpordV35Enhancements
    {
        private static readonly string[] ColSets = { "ABCDEFGH", "JKLMNPQR", "STUVWXYZ" };
        private static readonly string[] RowSets = { "ABCDEFGHJKLMNPQRSTUV", "FGHJKLMNPQRSTUVABCDE" };

        // =====================================================================
        // 1. KONWERSJA WSPÓŁRZĘDNYCH ZGODNIE Z SYSTEMEM WYBRANYM W MAP STUDIO
        //    (MGRS / UTM / DMS / WGS84)
        // =====================================================================
        public static string DetectActiveMapCoordinateSystem(Window win)
        {
            try
            {
                if (win != null)
                {
                    var all = BuilderCoreSidebarAndProjectEngine.GetAllVisualDescendants(win);
                    foreach (var cb in all.OfType<ComboBox>())
                    {
                        string sel = (cb.SelectedItem is ComboBoxItem cbi ? cbi.Content?.ToString() : cb.SelectedItem?.ToString()) ?? "";
                        if (sel.Contains("MGRS", StringComparison.OrdinalIgnoreCase)) return "MGRS";
                        if (sel.Contains("UTM", StringComparison.OrdinalIgnoreCase)) return "UTM";
                        if (sel.Contains("DMS", StringComparison.OrdinalIgnoreCase) || sel.Contains("Stopnie", StringComparison.OrdinalIgnoreCase)) return "DMS";
                        if (sel.Contains("WGS84", StringComparison.OrdinalIgnoreCase) || sel.Contains("DD", StringComparison.OrdinalIgnoreCase)) return "WGS84";
                    }
                }
            }
            catch { }
            return "MGRS"; // Domyślny standard taktyczny
        }

        public static string FormatCoordinate(double lat, double lon, string coordSystem)
        {
            string sys = (coordSystem ?? "MGRS").ToUpperInvariant();
            if (sys.Contains("MGRS"))
                return LatLonToMgrs(lat, lon);
            if (sys.Contains("UTM"))
                return LatLonToUtmString(lat, lon);
            if (sys.Contains("DMS"))
                return LatLonToDmsString(lat, lon);

            return string.Format(CultureInfo.InvariantCulture, "{0:F5}°N, {1:F5}°E", lat, lon);
        }

        public static string LatLonToMgrs(double lat, double lon)
        {
            try
            {
                double clampedLat = Math.Clamp(lat, -80.0, 84.0);
                double normLon = ((lon + 180.0) % 360.0 + 360.0) % 360.0 - 180.0;
                int zoneNumber = (int)Math.Floor((normLon + 180.0) / 6.0) + 1;

                double latRad = clampedLat * Math.PI / 180.0;
                double lonRad = normLon * Math.PI / 180.0;
                double lonOriginRad = ((zoneNumber - 1) * 6 - 180 + 3) * Math.PI / 180.0;

                double a = 6378137.0;
                double eccSq = 0.00669438;
                double k0 = 0.9996;
                double eccPrimeSq = eccSq / (1.0 - eccSq);

                double n = a / Math.Sqrt(1.0 - eccSq * Math.Sin(latRad) * Math.Sin(latRad));
                double t = Math.Tan(latRad) * Math.Tan(latRad);
                double c = eccPrimeSq * Math.Cos(latRad) * Math.Cos(latRad);
                double aCoeff = Math.Cos(latRad) * (lonRad - lonOriginRad);

                double m = a * ((1.0 - eccSq / 4.0 - 3.0 * eccSq * eccSq / 64.0) * latRad
                    - (3.0 * eccSq / 8.0 + 3.0 * eccSq * eccSq / 32.0) * Math.Sin(2.0 * latRad)
                    + (15.0 * eccSq * eccSq / 256.0) * Math.Sin(4.0 * latRad));

                double utmEasting = k0 * n * (aCoeff + (1.0 - t + c) * Math.Pow(aCoeff, 3) / 6.0
                    + (5.0 - 18.0 * t + t * t + 72.0 * c - 58.0 * eccPrimeSq) * Math.Pow(aCoeff, 5) / 120.0) + 500000.0;

                double utmNorthing = k0 * (m + n * Math.Tan(latRad) * (aCoeff * aCoeff / 2.0
                    + (5.0 - t + 9.0 * c + 4.0 * c * c) * Math.Pow(aCoeff, 4) / 24.0));
                if (clampedLat < 0) utmNorthing += 10000000.0;

                string bandLetters = "CDEFGHJKLMNPQRSTUVWXX";
                int bandIdx = Math.Clamp((int)Math.Floor((clampedLat + 80.0) / 8.0), 0, bandLetters.Length - 1);
                char bandChar = bandLetters[bandIdx];

                int colIdx = Math.Clamp((int)Math.Floor(utmEasting / 100000.0) - 1, 0, 7);
                char colChar = ColSets[(zoneNumber - 1) % 3][colIdx];

                int rowIdx = Math.Clamp((int)Math.Floor(utmNorthing / 100000.0) % 20, 0, 19);
                char rowChar = RowSets[(zoneNumber - 1) % 2][rowIdx];

                int east5 = Math.Clamp((int)Math.Round(utmEasting) % 100000, 0, 99999);
                int north5 = Math.Clamp((int)Math.Round(utmNorthing) % 100000, 0, 99999);

                return string.Format(CultureInfo.InvariantCulture, "{0:00}{1} {2}{3} {4:00000} {5:00000}",
                    zoneNumber, bandChar, colChar, rowChar, east5, north5);
            }
            catch
            {
                return string.Format(CultureInfo.InvariantCulture, "{0:F5}, {1:F5}", lat, lon);
            }
        }

        private static string LatLonToUtmString(double lat, double lon)
        {
            double clampedLat = Math.Clamp(lat, -80.0, 84.0);
            double normLon = ((lon + 180.0) % 360.0 + 360.0) % 360.0 - 180.0;
            int zoneNumber = (int)Math.Floor((normLon + 180.0) / 6.0) + 1;
            double latRad = clampedLat * Math.PI / 180.0;
            double lonRad = normLon * Math.PI / 180.0;
            double lonOriginRad = ((zoneNumber - 1) * 6 - 180 + 3) * Math.PI / 180.0;
            double a = 6378137.0, eccSq = 0.00669438, k0 = 0.9996;
            double eccPrimeSq = eccSq / (1.0 - eccSq);
            double n = a / Math.Sqrt(1.0 - eccSq * Math.Sin(latRad) * Math.Sin(latRad));
            double t = Math.Tan(latRad) * Math.Tan(latRad);
            double c = eccPrimeSq * Math.Cos(latRad) * Math.Cos(latRad);
            double aCoeff = Math.Cos(latRad) * (lonRad - lonOriginRad);
            double m = a * ((1.0 - eccSq / 4.0 - 3.0 * eccSq * eccSq / 64.0) * latRad - (3.0 * eccSq / 8.0 + 3.0 * eccSq * eccSq / 32.0) * Math.Sin(2.0 * latRad));
            double easting = k0 * n * (aCoeff + (1.0 - t + c) * Math.Pow(aCoeff, 3) / 6.0) + 500000.0;
            double northing = k0 * (m + n * Math.Tan(latRad) * (aCoeff * aCoeff / 2.0));
            return string.Format(CultureInfo.InvariantCulture, "UTM {0}N E:{1:0} N:{2:0}", zoneNumber, easting, northing);
        }

        private static string LatLonToDmsString(double lat, double lon)
        {
            return FormatSingleDms(lat, lat >= 0 ? 'N' : 'S') + " " + FormatSingleDms(lon, lon >= 0 ? 'E' : 'W');
        }

        private static string FormatSingleDms(double val, char hemi)
        {
            double abs = Math.Abs(val);
            int deg = (int)Math.Floor(abs);
            double minFull = (abs - deg) * 60.0;
            int min = (int)Math.Floor(minFull);
            double sec = (minFull - min) * 60.0;
            return string.Format(CultureInfo.InvariantCulture, "{0}°{1:00}'{2:00.0}\"{3}", deg, min, sec, hemi);
        }

        // =====================================================================
        // 2. SYNCHRONIZACJA DANYCH Z GENERATORA STRON I MAPY (MGRS + TABELA E)
        // =====================================================================
        public static void SyncAllFromGenerators(Window win, OpordParagraphModel model, OpordDayPlanModel activeDay)
        {
            if (model == null || activeDay == null) return;

            var snap = FactionLiveSyncBridge.CaptureCurrentFactionSnapshot(win, model.TargetFaction);
            if (!string.IsNullOrWhiteSpace(snap.FactionName))
            {
                model.TargetFaction = snap.FactionName;
            }
            if (!string.IsNullOrWhiteSpace(snap.EventName))
            {
                model.EventName = snap.EventName;
            }

            // Automatyczne wypełnienie Tabeli E Łączności na podstawie kanałów z Generatora Stron
            if (snap.VoiceChannels.Count > 0)
            {
                var ch0 = snap.VoiceChannels[0];
                activeDay.PrimaryChannel = $"{ch0.Role}: {ch0.Freq} [{ch0.Band}] ({ch0.Tone})";

                if (snap.VoiceChannels.Count > 1)
                {
                    var ch1 = snap.VoiceChannels[1];
                    activeDay.AlternateChannel = $"{ch1.Role}: {ch1.Freq} [{ch1.Band}] ({ch1.Tone})";
                }
                if (snap.VoiceChannels.Count > 2)
                {
                    var ch2 = snap.VoiceChannels[2];
                    activeDay.HqChannel = $"{ch2.Role}: {ch2.Freq} [{ch2.Band}] ({ch2.Tone})";
                }
                else
                {
                    activeDay.HqChannel = activeDay.PrimaryChannel;
                }
            }

            var sbComms = new StringBuilder();
            sbComms.AppendLine($"📡 [ZACIĄGNIĘTE Z GENERATORA STRON KANAŁY RADIOWE I MESHTASTIC DLA: {snap.FactionName}]");
            sbComms.AppendLine($"• Meshtastic LoRa: Kanał '{snap.ChannelName}' | Region: {snap.Region} | Preset: {snap.ModemPreset} | Ważność: {snap.ExpirationLabel}");
            sbComms.AppendLine($"• Serwer ATAK (OpenTAKServer): {(string.IsNullOrWhiteSpace(snap.OtsHost) ? "Tryb lokalny / Off-Grid Mesh" : snap.OtsHost)}");
            sbComms.AppendLine($"• Telefon alarmowy ORGA / MEDEVAC (SOS MGRS): {snap.EmergencyPhone}");
            sbComms.AppendLine("• Wszystkie kanały głosowe zdefiniowane w Generatorze Stron:");
            foreach (var vc in snap.VoiceChannels)
            {
                sbComms.AppendLine($"   - {vc.Role} [{vc.Band}]: {vc.Freq} ({vc.Tone})");
            }
            activeDay.SyncedCommsMatrixSummary = sbComms.ToString().TrimEnd();

            // Synchronizacja współrzędnych z Generatora Map 1:1 w wybranym systemie (np. MGRS)
            string coordSys = DetectActiveMapCoordinateSystem(win);
            var mapData = OpordBuilderManager.GetExtractedMapData();
            if (mapData != null && (mapData.HasAoPolygon || mapData.Pois.Count > 0))
            {
                activeDay.LocationLat = mapData.CenterLat.ToString("F6", CultureInfo.InvariantCulture);
                activeDay.LocationLon = mapData.CenterLon.ToString("F6", CultureInfo.InvariantCulture);
                string centerFormatted = FormatCoordinate(mapData.CenterLat, mapData.CenterLon, coordSys);

                if (string.IsNullOrWhiteSpace(activeDay.LocationName))
                {
                    activeDay.LocationName = $"Obszar AO ({coordSys}: {centerFormatted})";
                }

                var poiSb = new StringBuilder();
                poiSb.AppendLine($"Układ współrzędnych mapy: {coordSys} | Środek AO: {centerFormatted}");
                if (mapData.Pois.Count > 0)
                {
                    poiSb.AppendLine("Punkty kluczowe (POI) z Generatora Map 1:1:");
                    foreach (var p in mapData.Pois)
                    {
                        string pCoord = FormatCoordinate(p.Lat, p.Lon, coordSys);
                        poiSb.AppendLine($"• {p.Name} [{p.Type}]: {pCoord}");
                    }
                }
                activeDay.AreaOfInterestPoiDesc = poiSb.ToString().TrimEnd();
            }
        }

        // =====================================================================
        // 3. ROZSZERZENIE INTERFEJSU UI GENERATORA OPORD:
        //    - Przycisk "🔄 Pobierz z Generatora Stron" na samej górze
        //    - Walidacja METOC (błąd gdy puste pole i brak AO na mapie)
        //    - Opcjonalna Tabela Dodatkowych Kanałów Własnych ("na ptaszek")
        //    - Opcjonalny Załącznik nr 3 — Mapa Taktyczna ("na ptaszek")
        //    - Skondensowany pomocnik kopiowania na Discorda
        // =====================================================================
        public static void AttachOpordUiExtensions(
            Window win,
            OpordParagraphModel model,
            OpordDayPlanModel activeDay,
            StackPanel mainStack,
            Action rebuildCallback)
        {
            if (mainStack == null || model == null || activeDay == null) return;

            var allDescendants = BuilderCoreSidebarAndProjectEngine.GetAllVisualDescendants(mainStack).ToList();

            // A. Odświeżanie listy nazw stron w ComboBox na żywo
            var factionNames = FactionLiveSyncBridge.GetAllFactionNamesFromUi(win);
            foreach (var cb in allDescendants.OfType<ComboBox>())
            {
                bool isFactionCombo = cb.Items.Cast<object>().Any(x =>
                    x?.ToString()?.Contains("BLUFOR", StringComparison.OrdinalIgnoreCase) == true ||
                    x?.ToString()?.Contains("OPFOR", StringComparison.OrdinalIgnoreCase) == true ||
                    x?.ToString() == model.TargetFaction);

                if (isFactionCombo)
                {
                    foreach (string fn in factionNames)
                    {
                        if (!cb.Items.Cast<object>().Any(x => string.Equals(x?.ToString(), fn, StringComparison.OrdinalIgnoreCase)))
                        {
                            cb.Items.Add(fn);
                        }
                    }
                    cb.DropDownOpened += (_, __) =>
                    {
                        var liveNames = FactionLiveSyncBridge.GetAllFactionNamesFromUi(win);
                        foreach (string lfn in liveNames)
                        {
                            if (!cb.Items.Cast<object>().Any(x => string.Equals(x?.ToString(), lfn, StringComparison.OrdinalIgnoreCase)))
                            {
                                cb.Items.Add(lfn);
                            }
                        }
                    };
                    break;
                }
            }

            // B. Dodaj przycisk "🔄 Pobierz z Generatora Stron" bezpośrednio do górnej karty nagłówkowej OPORD
            if (mainStack.Children.Count > 0 && mainStack.Children[0] is Border topBorder && topBorder.Child is StackPanel topStack)
            {
                var syncBannerBtn = new Button
                {
                    Content = BuilderI18n.T(
                        "🔄 Pobierz aktualne dane z Generatora Stron i Mapy 1:1 (Nazwy stron, Kanały Tabeli E, Współrzędne MGRS)",
                        "🔄 Pull Live Data from Faction & 1:1 Map Generators (Faction Names, Table E Channels, MGRS Coords)"),
                    Height = 34,
                    Margin = new Thickness(0, 8, 0, 2),
                    Padding = new Thickness(14, 4, 14, 4),
                    Background = new SolidColorBrush(Color.FromRgb(14, 48, 58)),
                    Foreground = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                    BorderThickness = new Thickness(1.5),
                    FontWeight = FontWeights.Bold,
                    Cursor = Cursors.Hand
                };
                syncBannerBtn.Click += (_, __) =>
                {
                    SyncAllFromGenerators(win, model, activeDay);
                    rebuildCallback?.Invoke();
                };
                topStack.Children.Add(syncBannerBtn);
            }

            // C. Zabezpiecz przycisk METOC: jeśli pole lokalizacji jest puste ORAZ brak AO w Generatorze Map -> pokaż czytelny błąd!
            foreach (var btn in allDescendants.OfType<Button>())
            {
                string bTxt = (btn.Content ?? "").ToString();
                if (bTxt.Contains("METOC", StringComparison.OrdinalIgnoreCase))
                {
                    btn.PreviewMouseLeftButtonDown += (s, e) =>
                    {
                        var mapData = OpordBuilderManager.GetExtractedMapData();
                        bool hasMapAoOrPoi = mapData != null && (mapData.HasAoPolygon || mapData.Pois.Count > 0);
                        string typedLoc = (activeDay.LocationName ?? "").Trim();
                        bool isPlaceholderLoc = string.IsNullOrWhiteSpace(typedLoc) ||
                                                typedLoc.Equals("Czechowice-Dziedzice / Rejon AO", StringComparison.OrdinalIgnoreCase) ||
                                                typedLoc.Equals("Rejon AO", StringComparison.OrdinalIgnoreCase);

                        if (isPlaceholderLoc && !hasMapAoOrPoi)
                        {
                            e.Handled = true;
                            MessageBox.Show(
                                BuilderI18n.T(
                                    "⚠️ Brak zdefiniowanego obszaru dla raportu pogodowego METOC!\n\n" +
                                    "Proszę wykonać jedną z czynności:\n" +
                                    "1. Wpisać nazwę miejscowości lub współrzędne w polu 'Lokalizacja / Obszar gry (AO)', LUB\n" +
                                    "2. Zaznaczyć granicę terenu gry (AO) / punkty w module Generator Map 1:1.",
                                    "⚠️ No area defined for METOC weather report!\n\n" +
                                    "Please either:\n" +
                                    "1. Enter a town name or coordinates in the 'Location / Game Area (AO)' field, OR\n" +
                                    "2. Draw an AO boundary / points in the 1:1 Map Generator."),
                                BuilderI18n.T("Wymagany obszar AO lub lokalizacja — METOC", "AO or Location Required — METOC"),
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        }
                    };
                }

                // Podłącz okno skondensowanego eksportu Discord pod przycisk Kopiuj na Discorda
                if (bTxt.Contains("Discord", StringComparison.OrdinalIgnoreCase))
                {
                    btn.PreviewMouseLeftButtonDown += (s, e) =>
                    {
                        e.Handled = true;
                        ShowCondensedDiscordModal(win, model);
                    };
                }
            }

            // D. Wstrzyknij opcjonalną tabelę dodatkowych kanałów własnych ("na ptaszek") oraz Załącznik nr 3 z Mapą ("na ptaszek")
            var extraCard = BuildCustomCommsAndAnnex3Card(win, model, activeDay, rebuildCallback);
            int insertIdx = Math.Max(0, mainStack.Children.Count - 1);
            mainStack.Children.Insert(insertIdx, extraCard);
        }

        private static Border BuildCustomCommsAndAnnex3Card(
            Window win,
            OpordParagraphModel model,
            OpordDayPlanModel activeDay,
            Action rebuildCallback)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(10, 24, 30)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(25, 56, 68)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 12)
            };

            var stack = new StackPanel();

            // 1. Opcjonalna Tabela Dodatkowych Kanałów Własnych ("na ptaszek")
            var cbCustomChannels = new CheckBox
            {
                Content = BuilderI18n.T(
                    $"📻 Dołącz opcjonalną tabelę dodatkowych kanałów radiowych wpisanych ręcznie ({activeDay.DayTitle})",
                    $"📻 Include optional custom radio channels table ({activeDay.DayTitle})"),
                IsChecked = activeDay.IncludeCustomChannelsTable,
                Foreground = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 6)
            };
            stack.Children.Add(cbCustomChannels);

            var customChannelsBox = new TextBox
            {
                Text = activeDay.CustomChannelsTableText,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 68,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Padding = new Thickness(8),
                Background = new SolidColorBrush(Color.FromRgb(13, 33, 41)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(34, 74, 89)),
                Visibility = activeDay.IncludeCustomChannelsTable ? Visibility.Visible : Visibility.Collapsed,
                Margin = new Thickness(0, 0, 0, 12)
            };
            customChannelsBox.TextChanged += (_, __) =>
            {
                activeDay.CustomChannelsTableText = customChannelsBox.Text;
            };
            cbCustomChannels.Checked += (_, __) =>
            {
                activeDay.IncludeCustomChannelsTable = true;
                customChannelsBox.Visibility = Visibility.Visible;
            };
            cbCustomChannels.Unchecked += (_, __) =>
            {
                activeDay.IncludeCustomChannelsTable = false;
                customChannelsBox.Visibility = Visibility.Collapsed;
            };
            stack.Children.Add(customChannelsBox);

            // 2. Opcjonalny Załącznik nr 3 — Mapa Operacyjna / Szkic AO ("na ptaszek")
            var cbAnnex3Map = new CheckBox
            {
                Content = BuilderI18n.T(
                    $"🗺️ Dołącz ZAŁĄCZNIK 3 — Mapa Taktyczna / Szkic AO do wydruku OPORD ({activeDay.DayTitle})",
                    $"🗺️ Include ANNEX 3 — Tactical Map / AO Sketch in OPORD print ({activeDay.DayTitle})"),
                IsChecked = activeDay.IncludeAnnex3Map,
                Foreground = new SolidColorBrush(Color.FromRgb(250, 208, 44)),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 4, 0, 6)
            };
            stack.Children.Add(cbAnnex3Map);

            var annex3Panel = new StackPanel
            {
                Visibility = activeDay.IncludeAnnex3Map ? Visibility.Visible : Visibility.Collapsed,
                Margin = new Thickness(0, 4, 0, 0)
            };

            var pathRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var mapPathBox = new TextBox
            {
                Text = activeDay.Annex3MapImagePath,
                FontSize = 11.5,
                Padding = new Thickness(6),
                Background = new SolidColorBrush(Color.FromRgb(13, 33, 41)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(34, 74, 89)),
                ToolTip = "Ścieżka do pliku graficznego mapy (.png / .jpg), który zostanie dołączony jako Załącznik 3."
            };
            mapPathBox.TextChanged += (_, __) => activeDay.Annex3MapImagePath = mapPathBox.Text;
            Grid.SetColumn(mapPathBox, 0);
            pathRow.Children.Add(mapPathBox);

            var btnPickMapImg = new Button
            {
                Content = BuilderI18n.T("📂 Wybierz obraz mapy (.PNG/.JPG)", "📂 Select Map Image (.PNG/.JPG)"),
                Padding = new Thickness(12, 4, 12, 4),
                Margin = new Thickness(8, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(22, 54, 66)),
                Foreground = new SolidColorBrush(Color.FromRgb(250, 208, 44)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(250, 208, 44)),
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            btnPickMapImg.Click += (_, __) =>
            {
                var ofd = new OpenFileDialog
                {
                    Title = BuilderI18n.T("Wybierz plik obrazu mapy dla Załącznika 3", "Select Map Image for Annex 3"),
                    Filter = "Obrazy map (*.png;*.jpg;*.jpeg;*.webp;*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.bmp|Wszystkie pliki (*.*)|*.*"
                };
                if (ofd.ShowDialog(win) == true)
                {
                    activeDay.Annex3MapImagePath = ofd.FileName;
                    mapPathBox.Text = ofd.FileName;
                }
            };
            Grid.SetColumn(btnPickMapImg, 1);
            pathRow.Children.Add(btnPickMapImg);
            annex3Panel.Children.Add(pathRow);

            var notesBox = new TextBox
            {
                Text = activeDay.Annex3MapNotes,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 46,
                FontSize = 11.5,
                Padding = new Thickness(6),
                Background = new SolidColorBrush(Color.FromRgb(13, 33, 41)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(34, 74, 89))
            };
            notesBox.TextChanged += (_, __) => activeDay.Annex3MapNotes = notesBox.Text;
            annex3Panel.Children.Add(notesBox);

            cbAnnex3Map.Checked += (_, __) =>
            {
                activeDay.IncludeAnnex3Map = true;
                annex3Panel.Visibility = Visibility.Visible;
            };
            cbAnnex3Map.Unchecked += (_, __) =>
            {
                activeDay.IncludeAnnex3Map = false;
                annex3Panel.Visibility = Visibility.Collapsed;
            };

            stack.Children.Add(annex3Panel);
            card.Child = stack;
            return card;
        }

        public static string BuildAnnex3AndCustomCommsHtml(OpordDayPlanModel d)
        {
            if (d == null) return "";
            var sb = new StringBuilder();

            if (d.IncludeCustomChannelsTable && !string.IsNullOrWhiteSpace(d.CustomChannelsTableText))
            {
                sb.AppendLine("<div class='sec'>DODATKOWE KANAŁY RADIOWE (WEWNĘTRZNE / SPECJALNE)</div>");
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
                        byte[] imgBytes = File.ReadAllBytes(d.Annex3MapImagePath);
                        string ext = Path.GetExtension(d.Annex3MapImagePath).ToLowerInvariant();
                        string mime = ext == ".jpg" || ext == ".jpeg" ? "image/jpeg" : "image/png";
                        string b64 = Convert.ToBase64String(imgBytes);
                        sb.AppendLine($"<div style='text-align:center;margin-top:10px;'><img src='data:{mime};base64,{b64}' style='max-width:100%;max-height:220mm;border:2px solid #1e3a46;border-radius:6px;' alt='Mapa AO'/></div>");
                    }
                    catch { }
                }
                else
                {
                    string coordInfo = (!string.IsNullOrWhiteSpace(d.LocationLat) && double.TryParse(d.LocationLat, NumberStyles.Any, CultureInfo.InvariantCulture, out double lat) &&
                                        double.TryParse(d.LocationLon, NumberStyles.Any, CultureInfo.InvariantCulture, out double lon))
                        ? $"Środek AO (MGRS): {LatLonToMgrs(lat, lon)}  |  WGS84: {lat:F5}°N, {lon:F5}°E"
                        : d.LocationName;
                    sb.AppendLine($"<div class='box'><b>🗺️ Szkic orientacyjny AO:</b> {WebUtility.HtmlEncode(coordInfo)}<br/>{WebUtility.HtmlEncode(d.AreaOfInterestPoiDesc)}</div>");
                }
            }

            return sb.ToString();
        }

        // =====================================================================
        // 4. SKONDENSOWANY EKSPORT NA DISCORDA (Z AUTOMATYCZNYM PODZIAŁEM <1900 ZNAKÓW)
        // =====================================================================
        public static string BuildCondensedDiscordMarkdown(OpordParagraphModel m)
        {
            var chunks = BuildCondensedDiscordChunks(m);
            if (chunks.Count == 1) return chunks[0];

            var sb = new StringBuilder();
            for (int i = 0; i < chunks.Count; i++)
            {
                if (i > 0) sb.AppendLine($"\n--- ✂️ [CZĘŚĆ {i + 1}/{chunks.Count} — KOPIUJ OSOBNO DO DISCORDA] ✂️ ---\n");
                sb.AppendLine(chunks[i]);
            }
            return sb.ToString().TrimEnd();
        }

        public static List<string> BuildCondensedDiscordChunks(OpordParagraphModel m)
        {
            var chunks = new List<string>();
            if (m == null) return chunks;

            var daysToExport = (m.Days != null && m.Days.Count > 0)
                ? m.Days.Where(x => x.IncludeInExport).ToList()
                : new List<OpordDayPlanModel>();
            if (daysToExport.Count == 0 && m.Days != null && m.Days.Count > 0)
                daysToExport.Add(m.Days[Math.Clamp(m.ActiveDayIndex, 0, m.Days.Count - 1)]);

            foreach (var d in daysToExport)
            {
                string mgrsCenter = "";
                if (double.TryParse(d.LocationLat, NumberStyles.Any, CultureInfo.InvariantCulture, out double lat) &&
                    double.TryParse(d.LocationLon, NumberStyles.Any, CultureInfo.InvariantCulture, out double lon))
                {
                    mgrsCenter = $" | MGRS: `{LatLonToMgrs(lat, lon)}`";
                }

                var part1 = new StringBuilder();
                string secIcon = d.IsSecretClassification ? "🔴 **[TAJNE]**" : "🟢 **[JAWNE]**";
                part1.AppendLine($"# 📋 {d.OperationNumber} — {m.EventName} ({m.TargetFaction}) | 📅 {d.DayTitle} ({d.DateIso})");
                part1.AppendLine($"{secIcon} `{d.Classification}` | **HQ:** {d.IssuingHq} | **AO:** {d.LocationName}{mgrsCenter}");
                part1.AppendLine($"**🌦️ METOC:** {FirstLine(d.TerrainAndWeather)}");
                part1.AppendLine($"**⏱️ Harmonogram:**\n{CompactLines(d.DaySpecificTasks)}");
                part1.AppendLine($"**🎯 II. MISJA (5W):**\n> {d.MissionFullStatement}");
                if (!string.IsNullOrWhiteSpace(d.AdditionalTasksSummary))
                    part1.AppendLine($"**Zadania dodatkowe:**\n{CompactLines(d.AdditionalTasksSummary)}");
                part1.AppendLine($"**⚡ III. WYKONANIE & ZAMIAR:** {d.CommandersIntent}");
                part1.AppendLine($"**🎖️ Dowodzenie:** `{d.SuccessionOfCommand}`");

                if (d.SquadRoster != null && d.SquadRoster.Count > 0)
                {
                    part1.AppendLine("**👥 Skład i Role:**");
                    foreach (var r in d.SquadRoster)
                        part1.AppendLine($"• **{r.OperatorOrTeam}**: {r.PrimaryRole} / {r.SecondaryRole} *({r.WeaponAndGear})*");
                }

                var part2 = new StringBuilder();
                part2.AppendLine($"## 📡 V. ŁĄCZNOŚĆ, SARNEG & LOGISTYKA — {d.DayTitle} ({m.TargetFaction})");
                part2.AppendLine($"• **Główny:** `{d.PrimaryChannel}` | **Zapas:** `{d.AlternateChannel}` | **HQ:** `{d.HqChannel}`");
                if (d.IncludeCustomChannelsTable && !string.IsNullOrWhiteSpace(d.CustomChannelsTableText))
                    part2.AppendLine($"• **Kanały dodatkowe:**\n{CompactLines(d.CustomChannelsTableText)}");

                string normSarneg = SarnegDictionaryService.NormalizePolishChars(d.SarnegKeyWord10).PadRight(10, 'X').Substring(0, 10);
                var letRow = new StringBuilder();
                var digRow = new StringBuilder();
                for (int i = 0; i < 10; i++)
                {
                    letRow.Append(normSarneg[i]).Append(" ");
                    digRow.Append(i).Append(" ");
                }
                part2.AppendLine($"**🔐 SARNEG ({d.DayTitle}): `{normSarneg}`**");
                part2.AppendLine($"```text\nLITERA: {letRow.ToString().TrimEnd()}\nCYFRA : {digRow.ToString().TrimEnd()}\n```");
                part2.AppendLine($"**🎯 Kryptonimy:** {d.TargetCodeNames}");
                part2.AppendLine($"**🔑 Hasła:** {CompactLines(d.ChallengeAndPassword)}");
                part2.AppendLine($"**🚑 MEDEVAC:** {d.MedevacRules}");
                part2.AppendLine($"**🎒 Szpej / Logistyka:** {d.SquadGearChecklist}");

                string p1Str = part1.ToString().Trim();
                string p2Str = part2.ToString().Trim();

                if (p1Str.Length + p2Str.Length + 2 <= 1900)
                {
                    chunks.Add(p1Str + "\n\n" + p2Str);
                }
                else
                {
                    chunks.Add(p1Str);
                    chunks.Add(p2Str);
                }
            }

            return chunks;
        }

        private static string FirstLine(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "-";
            var lines = s.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" | ", lines.Take(2));
        }

        private static string CompactLines(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "-";
            var lines = s.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                         .Select(x => x.Trim())
                         .Where(x => !string.IsNullOrEmpty(x));
            return string.Join("\n", lines);
        }

        public static void ShowCondensedDiscordModal(Window win, OpordParagraphModel model)
        {
            var chunks = BuildCondensedDiscordChunks(model);
            string fullText = BuildCondensedDiscordMarkdown(model);
            Clipboard.SetText(fullText);

            var dlg = new Window
            {
                Title = BuilderI18n.T("💬 Skondensowany Eksport OPORD na Discorda (Podział na wiadomości <1900 znaków)", "💬 Condensed Discord OPORD Export (<1900 chars per message)"),
                Width = 680,
                Height = 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = win,
                Background = new SolidColorBrush(Color.FromRgb(10, 24, 30)),
                Foreground = Brushes.White
            };

            var root = new Grid { Margin = new Thickness(16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var title = new TextBlock
            {
                Text = BuilderI18n.T(
                    $"✅ Skopiowano cały rozkaz do schowka! Liczba części (poniżej limitu 2000 znaków Discorda): {chunks.Count}",
                    $"✅ Copied full OPORD to clipboard! Number of Discord-ready parts (<2000 chars): {chunks.Count}"),
                Foreground = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                FontWeight = FontWeights.Bold,
                FontSize = 13.5,
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetRow(title, 0);
            root.Children.Add(title);

            var btnsWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            var btnCopyAll = new Button
            {
                Content = BuilderI18n.T("📋 Kopiuj CAŁOŚĆ", "📋 Copy ALL"),
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 0, 8, 6),
                Background = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                Foreground = new SolidColorBrush(Color.FromRgb(5, 16, 20)),
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };

            var previewBox = new TextBox
            {
                Text = fullText,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Padding = new Thickness(10),
                Background = new SolidColorBrush(Color.FromRgb(7, 17, 21)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(34, 74, 89))
            };

            btnCopyAll.Click += (_, __) =>
            {
                Clipboard.SetText(fullText);
                previewBox.Text = fullText;
            };
            btnsWrap.Children.Add(btnCopyAll);

            for (int i = 0; i < chunks.Count; i++)
            {
                int partIdx = i;
                string chunkText = chunks[i];
                var btnPart = new Button
                {
                    Content = BuilderI18n.T($"💬 Kopiuj Część {partIdx + 1}/{chunks.Count} ({chunkText.Length} zn.)", $"💬 Copy Part {partIdx + 1}/{chunks.Count} ({chunkText.Length} ch.)"),
                    Padding = new Thickness(12, 6, 12, 6),
                    Margin = new Thickness(0, 0, 8, 6),
                    Background = new SolidColorBrush(Color.FromRgb(22, 54, 66)),
                    Foreground = new SolidColorBrush(Color.FromRgb(250, 208, 44)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(250, 208, 44)),
                    BorderThickness = new Thickness(1.2),
                    FontWeight = FontWeights.Bold,
                    Cursor = Cursors.Hand
                };
                btnPart.Click += (_, __) =>
                {
                    Clipboard.SetText(chunkText);
                    previewBox.Text = chunkText;
                };
                btnsWrap.Children.Add(btnPart);
            }

            Grid.SetRow(btnsWrap, 1);
            root.Children.Add(btnsWrap);

            Grid.SetRow(previewBox, 2);
            root.Children.Add(previewBox);

            dlg.Content = root;
            dlg.ShowDialog();
        }
    }
}
