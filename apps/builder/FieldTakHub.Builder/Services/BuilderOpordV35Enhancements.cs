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
                    if (s.Contains("DMS", StringComparison.OrdinalIgnoreCase)) return "DMS";
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
                if (snap.VoiceChannels.Count > 2)
                {
                    var c2 = snap.VoiceChannels[2];
                    activeDay.HqChannel = $"{c2.Role}: {c2.Freq} [{c2.Band}] ({c2.Tone})";
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"📡 [ZACIĄGNIĘTE Z GENERATORA STRON KANAŁY RADIOWE I MESHTASTIC DLA: {snap.FactionName}]");
            sb.AppendLine($"• Meshtastic LoRa: Kanał '{snap.ChannelName}' | Region: {snap.Region} | Preset: {snap.ModemPreset}");
            sb.AppendLine($"• Serwer ATAK (OpenTAKServer): {(string.IsNullOrWhiteSpace(snap.OtsHost) ? "Tryb lokalny / Off-Grid Mesh" : snap.OtsHost)}");
            sb.AppendLine($"• Telefon alarmowy ORGA / MEDEVAC (SOS MGRS): {snap.EmergencyPhone}");
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
                    activeDay.LocationName = $"Obszar AO ({sys}: {mgrs})";

                var psb = new StringBuilder();
                psb.AppendLine($"Układ współrzędnych mapy: {sys} | Środek AO: {mgrs}");
                foreach (var p in md.Pois)
                    psb.AppendLine($"• {p.Name} [{p.Type}]: {LatLonToMgrs(p.Lat, p.Lon)}");
                activeDay.AreaOfInterestPoiDesc = psb.ToString().TrimEnd();
            }
        }

        public static void AttachOpordUiExtensions(Window win, OpordParagraphModel model, OpordDayPlanModel activeDay, StackPanel mainStack, Action rebuildCallback)
        {
            if (mainStack == null || model == null || activeDay == null) return;
            if (string.IsNullOrWhiteSpace(activeDay.SyncedCommsMatrixSummary))
                SyncAllFromGenerators(win, model, activeDay);

            var all = BuilderPackageAndUiPatcher.GetVisualTreeElements(mainStack);
            var facNames = FactionLiveSyncBridge.GetAllFactionNamesFromUi(win);
            foreach (var cb in all.OfType<ComboBox>())
            {
                if (cb.Items.Cast<object>().Any(x => x?.ToString()?.Contains("BLUFOR", StringComparison.OrdinalIgnoreCase) == true || x?.ToString() == model.TargetFaction))
                {
                    foreach (string fn in facNames)
                        if (!cb.Items.Cast<object>().Any(x => string.Equals(x?.ToString(), fn, StringComparison.OrdinalIgnoreCase)))
                            cb.Items.Add(fn);
                    break;
                }
            }

            if (mainStack.Children.Count > 0 && mainStack.Children[0] is Border tb && tb.Child is StackPanel ts)
            {
                var syncBtn = new Button
                {
                    Content = BuilderI18n.T("🔄 Pobierz dane z Generatora Stron i Mapy 1:1 (Nazwy stron, Tabela E, Współrzędne MGRS)", "🔄 Pull Data from Faction & Map Generators"),
                    Height = 34, Margin = new Thickness(0, 8, 0, 2), Padding = new Thickness(14, 4, 14, 4),
                    Background = new SolidColorBrush(Color.FromRgb(14, 48, 58)), Foreground = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(31, 224, 197)), BorderThickness = new Thickness(1.5),
                    FontWeight = FontWeights.Bold, Cursor = Cursors.Hand
                };
                syncBtn.Click += (_, __) => { SyncAllFromGenerators(win, model, activeDay); rebuildCallback?.Invoke(); };
                ts.Children.Add(syncBtn);
            }

            foreach (var btn in all.OfType<Button>())
            {
                string bt = (btn.Content ?? "").ToString();
                if (bt.Contains("METOC", StringComparison.OrdinalIgnoreCase))
                {
                    btn.PreviewMouseLeftButtonDown += (s, e) =>
                    {
                        var md = ExtractMapDataSafe(win);
                        bool hasAo = md != null && (md.HasAoPolygon || md.Pois.Count > 0);
                        string loc = (activeDay.LocationName ?? "").Trim();
                        if ((string.IsNullOrWhiteSpace(loc) || loc.Contains("Czechowice-Dziedzice / Rejon AO")) && !hasAo)
                        {
                            e.Handled = true;
                            MessageBox.Show(
                                "Proszę wpisać nazwę obszaru w polu Lokalizacja lub zaznaczyć granicę AO w Generatorze Map 1:1 przed pobraniem METOC!",
                                "Wymagany obszar AO — METOC", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    };
                }
                if (bt.Contains("Discord", StringComparison.OrdinalIgnoreCase))
                {
                    btn.PreviewMouseLeftButtonDown += (s, e) =>
                    {
                        e.Handled = true;
                        string md = BuildCondensedDiscordMarkdown(model);
                        Clipboard.SetText(md);
                        MessageBox.Show("Skopiowano skondensowany rozkaz OPORD na Discorda do schowka!", "Discord OPORD", MessageBoxButton.OK, MessageBoxImage.Information);
                    };
                }
            }

            var extraCard = BuildExtraCard(win, activeDay);
            mainStack.Children.Insert(Math.Max(0, mainStack.Children.Count - 1), extraCard);
        }

        private static Border BuildExtraCard(Window win, OpordDayPlanModel d)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(10, 24, 30)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(25, 56, 68)),
                BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 12)
            };
            var st = new StackPanel();

            var cbCh = new CheckBox
            {
                Content = $"📻 Dołącz opcjonalną tabelę dodatkowych kanałów własnych ({d.DayTitle})",
                IsChecked = d.IncludeCustomChannelsTable, Foreground = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6)
            };
            var tbCh = new TextBox
            {
                Text = d.CustomChannelsTableText, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60,
                Background = new SolidColorBrush(Color.FromRgb(13, 33, 41)), Foreground = Brushes.White,
                Visibility = d.IncludeCustomChannelsTable ? Visibility.Visible : Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 10)
            };
            tbCh.TextChanged += (_, __) => d.CustomChannelsTableText = tbCh.Text;
            cbCh.Checked += (_, __) => { d.IncludeCustomChannelsTable = true; tbCh.Visibility = Visibility.Visible; };
            cbCh.Unchecked += (_, __) => { d.IncludeCustomChannelsTable = false; tbCh.Visibility = Visibility.Collapsed; };
            st.Children.Add(cbCh);
            st.Children.Add(tbCh);

            var cbMap = new CheckBox
            {
                Content = $"🗺️ Dołącz ZAŁĄCZNIK 3 — Mapa Taktyczna / Szkic AO do OPORD ({d.DayTitle})",
                IsChecked = d.IncludeAnnex3Map, Foreground = new SolidColorBrush(Color.FromRgb(250, 208, 44)),
                FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 6)
            };
            var mapBtn = new Button
            {
                Content = "📂 Wybierz plik obrazu mapy dla Załącznika 3 (.PNG / .JPG)",
                Height = 30, Margin = new Thickness(0, 0, 0, 6),
                Visibility = d.IncludeAnnex3Map ? Visibility.Visible : Visibility.Collapsed
            };
            mapBtn.Click += (_, __) =>
            {
                var ofd = new OpenFileDialog { Filter = "Obrazy (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|Wszystkie (*.*)|*.*" };
                if (ofd.ShowDialog(win) == true) d.Annex3MapImagePath = ofd.FileName;
            };
            cbMap.Checked += (_, __) => { d.IncludeAnnex3Map = true; mapBtn.Visibility = Visibility.Visible; };
            cbMap.Unchecked += (_, __) => { d.IncludeAnnex3Map = false; mapBtn.Visibility = Visibility.Collapsed; };
            st.Children.Add(cbMap);
            st.Children.Add(mapBtn);

            card.Child = st;
            return card;
        }

        public static string BuildAnnex3AndCustomCommsHtml(OpordDayPlanModel d)
        {
            if (d == null) return "";
            var sb = new StringBuilder();
            if (d.IncludeCustomChannelsTable && !string.IsNullOrWhiteSpace(d.CustomChannelsTableText))
                sb.AppendLine($"<div class='sec'>DODATKOWE KANAŁY RADIOWE</div><div class='box'>{WebUtility.HtmlEncode(d.CustomChannelsTableText)}</div>");
            if (d.IncludeAnnex3Map)
            {
                sb.AppendLine($"<div class='sec'>ZAŁĄCZNIK 3 — MAPA TAKTYCZNA AO ({WebUtility.HtmlEncode(d.DayTitle)})</div>");
                if (!string.IsNullOrWhiteSpace(d.Annex3MapImagePath) && File.Exists(d.Annex3MapImagePath))
                {
                    byte[] b = File.ReadAllBytes(d.Annex3MapImagePath);
                    sb.AppendLine($"<div style='text-align:center;'><img src='data:image/png;base64,{Convert.ToBase64String(b)}' style='max-width:100%;max-height:210mm;'/></div>");
                }
                else sb.AppendLine($"<div class='box'>{WebUtility.HtmlEncode(d.AreaOfInterestPoiDesc)}</div>");
            }
            return sb.ToString();
        }

        public static string BuildCondensedDiscordMarkdown(OpordParagraphModel m)
        {
            if (m == null || m.Days == null || m.Days.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var d in m.Days.Where(x => x.IncludeInExport))
            {
                sb.AppendLine($"# 📋 {d.OperationNumber} — {m.EventName} ({m.TargetFaction}) | 📅 {d.DayTitle} ({d.DateIso})");
                sb.AppendLine($"`{d.Classification}` | **HQ:** {d.IssuingHq} | **AO:** {d.LocationName}");
                sb.AppendLine($"**🎯 MISJA (5W):** {d.MissionFullStatement}");
                sb.AppendLine($"**⚡ ZAMIAR:** {d.CommandersIntent} | **Dowodzenie:** `{d.SuccessionOfCommand}`");
                sb.AppendLine($"**📡 ŁĄCZNOŚĆ:** Główny: `{d.PrimaryChannel}` | Zapas: `{d.AlternateChannel}` | HQ: `{d.HqChannel}`");
                if (d.IncludeCustomChannelsTable && !string.IsNullOrWhiteSpace(d.CustomChannelsTableText))
                    sb.AppendLine($"**📻 Kanały własne:**\n{d.CustomChannelsTableText}");
                sb.AppendLine($"**🔐 SARNEG ({d.DayTitle}):** `{d.SarnegKeyWord10}` | **Hasła:** {d.ChallengeAndPassword}");
                sb.AppendLine($"**🚑 MEDEVAC:** {d.MedevacRules} | **🎒 Szpej:** {d.SquadGearChecklist}\n");
            }
            return sb.ToString().TrimEnd();
        }
    }
}
