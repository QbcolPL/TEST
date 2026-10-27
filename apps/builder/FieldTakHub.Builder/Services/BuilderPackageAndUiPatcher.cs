#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FieldTakHub.Builder.Services
{
    internal static class BuilderPackageAndUiPatcher
    {
        private static bool _sharedFoldersInitialized;
        private static HttpListener _hotspotListener;
        private static int _hotspotPort = 8769;
        public static string LatestHotspotJsonPayload = "{}";

        public static List<DependencyObject> GetVisualTreeElements(DependencyObject root)
        {
            var result = new List<DependencyObject>();
            if (root == null) return result;
            var q = new Queue<DependencyObject>();
            q.Enqueue(root);
            while (q.Count > 0)
            {
                var cur = q.Dequeue();
                result.Add(cur);
                int cnt = 0;
                try { cnt = VisualTreeHelper.GetChildrenCount(cur); } catch { }
                for (int i = 0; i < cnt; i++)
                {
                    DependencyObject ch = null;
                    try { ch = VisualTreeHelper.GetChild(cur, i); } catch { }
                    if (ch != null) q.Enqueue(ch);
                }
            }
            return result;
        }

        public static string GetWorkspaceRootDir()
        {
            string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FieldTakHub");
            Directory.CreateDirectory(d);
            return d;
        }

        public static string EnsureSharedFolderStructure()
        {
            string root = GetWorkspaceRootDir();
            string wspolne = Path.Combine(root, "wspolne");
            Directory.CreateDirectory(Path.Combine(wspolne, "apk"));
            Directory.CreateDirectory(Path.Combine(wspolne, "plugins"));
            Directory.CreateDirectory(Path.Combine(wspolne, "maps"));
            Directory.CreateDirectory(Path.Combine(root, "out"));
            if (!_sharedFoldersInitialized)
            {
                _sharedFoldersInitialized = true;
                string readme = Path.Combine(wspolne, "INSTRUKCJA_FOLDERU_WSPOLNE.txt");
                if (!File.Exists(readme))
                {
                    File.WriteAllText(readme,
                        "1. apk\\     -> Instalatory .apk aplikacji ATAK-CIV oraz Meshtastic.\r\n" +
                        "2. plugins\\ -> Wtyczki .apk do aplikacji ATAK.\r\n" +
                        "3. maps\\    -> Wspólne mapy (.kmz, .gpx, .mbtiles, .sqlitedb).\r\n", Encoding.UTF8);
                }
            }
            return wspolne;
        }

        public static string SanitizeFolderName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "BLUFOR";
            char[] inv = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (char c in name.Trim()) sb.Append(inv.Contains(c) ? '_' : c);
            string r = sb.ToString().Trim();
            return string.IsNullOrEmpty(r) ? "BLUFOR" : r;
        }

        public static string EnsureActiveFactionOutputFolder(Window win)
        {
            EnsureSharedFolderStructure();
            var snap = FactionLiveSyncBridge.CaptureCurrentFactionSnapshot(win, null);
            string outDir = Path.Combine(GetWorkspaceRootDir(), "out", SanitizeFolderName(snap.FactionName));
            Directory.CreateDirectory(outDir);
            return outDir;
        }

        public static void ApplyEnhancements(Window win, List<DependencyObject> allElements)
        {
            if (win == null || allElements == null) return;
            EnsureSharedFolderStructure();
            try
            {
                if (win.MinWidth > 900) win.MinWidth = 900;
                if (win.MinHeight > 580) win.MinHeight = 580;
                CollapseEmptyLegacyTopBars(allElements);
                PatchZeroExpirationInputs(allElements);
                PatchFactionFilesAndPosterButtons(win, allElements);
                PatchFtakGeneratorCardToThreeButtons(win, allElements);
            }
            catch { }
        }

        private static void CollapseEmptyLegacyTopBars(List<DependencyObject> allElements)
        {
            foreach (var border in allElements.OfType<Border>())
            {
                if (border.Visibility != Visibility.Visible) continue;
                if (border.BorderThickness.Top > 0 || border.BorderThickness.Bottom > 0)
                {
                    if (border.Child == null && border.ActualHeight > 0 && border.ActualHeight < 65 && border.ActualWidth > 300)
                    {
                        border.Visibility = Visibility.Collapsed;
                        border.Height = 0;
                        border.BorderThickness = new Thickness(0);
                    }
                    else if (border.Child is Panel p)
                    {
                        bool anyVisible = p.Children.Cast<UIElement>().Any(ch =>
                            ch.Visibility == Visibility.Visible &&
                            !(ch is Panel sp && sp.Children.Cast<UIElement>().All(x => x.Visibility != Visibility.Visible)) &&
                            !(ch is TextBlock tb && string.IsNullOrWhiteSpace(tb.Text)));
                        if (!anyVisible)
                        {
                            border.Visibility = Visibility.Collapsed;
                            border.Height = 0;
                            border.Margin = new Thickness(0);
                            border.Padding = new Thickness(0);
                            border.BorderThickness = new Thickness(0);
                        }
                    }
                }
            }
        }

        private static void PatchZeroExpirationInputs(List<DependencyObject> allElements)
        {
            foreach (var tb in allElements.OfType<TextBlock>())
            {
                string txt = (tb.Text ?? "").Trim();
                if ((txt.Contains("Ważność", StringComparison.OrdinalIgnoreCase) || txt.Contains("Expiration", StringComparison.OrdinalIgnoreCase)) &&
                    !txt.Contains("0 = bezterminowo", StringComparison.OrdinalIgnoreCase))
                {
                    tb.Text = txt.TrimEnd(':') + " (0 = bezterminowo):";
                }
            }
        }

        private static void PatchFactionFilesAndPosterButtons(Window win, List<DependencyObject> allElements)
        {
            foreach (var btn in allElements.OfType<Button>())
            {
                string txt = ExtractButtonText(btn);
                if (string.IsNullOrWhiteSpace(txt)) continue;

                if (txt.Contains("Pliki strony", StringComparison.OrdinalIgnoreCase) || txt.Contains("Faction files", StringComparison.OrdinalIgnoreCase))
                {
                    btn.IsEnabled = true;
                    btn.Visibility = Visibility.Visible;
                    if (btn.Tag?.ToString() != "FTH_FAC_FILES_OK")
                    {
                        btn.Tag = "FTH_FAC_FILES_OK";
                        btn.Content = BuilderI18n.T("📂 Pliki strony", "📂 Faction Files");
                        btn.PreviewMouseLeftButtonDown += (s, e) =>
                        {
                            e.Handled = true;
                            try
                            {
                                string dir = EnsureActiveFactionOutputFolder(win);
                                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true });
                            }
                            catch { }
                        };
                    }
                }

                if (txt.Contains("Plakat", StringComparison.OrdinalIgnoreCase) || txt.Contains("Safezone", StringComparison.OrdinalIgnoreCase))
                {
                    btn.IsEnabled = true;
                    if (btn.Tag?.ToString() != "FTH_POSTER_OK")
                    {
                        btn.Tag = "FTH_POSTER_OK";
                        btn.PreviewMouseLeftButtonDown += (s, e) =>
                        {
                            e.Handled = true;
                            GenerateAndOpenSafezonePosterHtml(win);
                        };
                    }
                }
            }
        }

        private static void PatchFtakGeneratorCardToThreeButtons(Window win, List<DependencyObject> allElements)
        {
            foreach (var tb in allElements.OfType<TextBlock>())
            {
                string t = (tb.Text ?? "").Trim();
                if (t.Contains("GENERATOR PACZEK .FTAK", StringComparison.OrdinalIgnoreCase) || t.Contains("PACKAGE BUILDER .FTAK", StringComparison.OrdinalIgnoreCase))
                {
                    DependencyObject p = VisualTreeHelper.GetParent(tb);
                    while (p != null && !(p is StackPanel || p is Grid)) p = VisualTreeHelper.GetParent(p);
                    if (p is Panel cardPanel && cardPanel.Tag?.ToString() != "FTH_3BTN_OK")
                    {
                        cardPanel.Tag = "FTH_3BTN_OK";
                        tb.Text = BuilderI18n.T(
                            "🎯 GENERATOR PACZEK .FTAK (FOLDER out\\<STRONA>\\ + WSPÓLNY FOLDER APK / PLUGINY / MAPY)",
                            "🎯 .FTAK PACKAGE GENERATOR (out\\<FACTION>\\ + SHARED APK / PLUGINS / MAPS)");

                        var desc = GetVisualTreeElements(cardPanel);
                        foreach (var cb in desc.OfType<ComboBox>()) { cb.Visibility = Visibility.Collapsed; cb.Width = 0; cb.Height = 0; }

                        Button origBuildAll = null;
                        foreach (var b in desc.OfType<Button>())
                        {
                            string bt = ExtractButtonText(b);
                            if (bt.Contains("WSZYSTKIE", StringComparison.OrdinalIgnoreCase) || bt.Contains("ALL", StringComparison.OrdinalIgnoreCase))
                                origBuildAll = b;
                            b.Visibility = Visibility.Collapsed;
                            b.Height = 0;
                            b.Margin = new Thickness(0);
                        }

                        var wrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 6) };

                        var b1 = new Button
                        {
                            Content = BuilderI18n.T("⚡ Buduj wszystkie aktywne strony", "⚡ Build All Active Factions"),
                            Height = 38, MinWidth = 230, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 4, 10, 4),
                            Background = new SolidColorBrush(Color.FromRgb(31, 224, 197)), Foreground = new SolidColorBrush(Color.FromRgb(5, 16, 20)),
                            FontWeight = FontWeights.Bold, Cursor = Cursors.Hand
                        };
                        b1.Click += (_, __) =>
                        {
                            EnsureSharedFolderStructure();
                            if (origBuildAll != null) origBuildAll.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            else BuildAllFactionsFallback(win);
                        };

                        var b2 = new Button
                        {
                            Content = BuilderI18n.T("📂 Otwórz wspólny folder (APK / Pluginy / Mapy)", "📂 Open Shared Folder (APK / Plugins / Maps)"),
                            Height = 38, MinWidth = 230, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 4, 10, 4),
                            Background = new SolidColorBrush(Color.FromRgb(42, 34, 12)), Foreground = new SolidColorBrush(Color.FromRgb(250, 208, 44)),
                            BorderBrush = new SolidColorBrush(Color.FromRgb(250, 208, 44)), BorderThickness = new Thickness(1.5),
                            FontWeight = FontWeights.Bold, Cursor = Cursors.Hand
                        };
                        b2.Click += (_, __) =>
                        {
                            string sh = EnsureSharedFolderStructure();
                            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + sh + "\"") { UseShellExecute = true });
                        };

                        var b3 = new Button
                        {
                            Content = BuilderI18n.T("📡 Dystrybucja przez HOTSPOT Wi-Fi (Paczki / OPORD)", "📡 Distribute via Wi-Fi HOTSPOT (Packages / OPORD)"),
                            Height = 38, MinWidth = 240, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 4, 0, 4),
                            Background = new SolidColorBrush(Color.FromRgb(14, 42, 54)), Foreground = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                            BorderBrush = new SolidColorBrush(Color.FromRgb(31, 224, 197)), BorderThickness = new Thickness(1.5),
                            FontWeight = FontWeights.Bold, Cursor = Cursors.Hand
                        };
                        b3.Click += (_, __) => ShowHotspotDistributionModal(win);

                        wrap.Children.Add(b1);
                        wrap.Children.Add(b2);
                        wrap.Children.Add(b3);
                        cardPanel.Children.Add(wrap);
                    }
                }
            }
        }

        private static void BuildAllFactionsFallback(Window win)
        {
            var snap = FactionLiveSyncBridge.CaptureCurrentFactionSnapshot(win, null);
            string facDir = EnsureActiveFactionOutputFolder(win);
            File.WriteAllText(Path.Combine(facDir, SanitizeFolderName(snap.FactionName) + "_ftak.json"), snap.ToQrJson(), Encoding.UTF8);
            MessageBox.Show($"Zbudowano paczkę w katalogu:\n{facDir}\nWażność: {snap.ExpirationLabel}", "Field TAK Hub", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public static void GenerateAndOpenSafezonePosterHtml(Window win)
        {
            try
            {
                var snap = FactionLiveSyncBridge.CaptureCurrentFactionSnapshot(win, null);
                string url = StartLocalHotspotServer(snap);
                string qrData = Uri.EscapeDataString(snap.ToQrJson());
                string outDir = EnsureActiveFactionOutputFolder(win);
                string htmlPath = Path.Combine(outDir, $"Plakat_Safezone_{SanitizeFolderName(snap.FactionName)}.html");

                var rows = new StringBuilder();
                foreach (var vc in snap.VoiceChannels)
                    rows.AppendLine($"<tr><td><b>{WebUtility.HtmlEncode(vc.Role)}</b></td><td>{WebUtility.HtmlEncode(vc.Band)}</td><td><b>{WebUtility.HtmlEncode(vc.Freq)}</b></td><td>{WebUtility.HtmlEncode(vc.Tone)}</td></tr>");

                string html = $"<!DOCTYPE html><html><head><meta charset='utf-8'/><title>Plakat Safezone — {WebUtility.HtmlEncode(snap.FactionName)}</title>" +
                    "<style>body{font-family:'Segoe UI',Arial,sans-serif;background:#071115;color:#fff;padding:18px;}.sheet{max-width:190mm;margin:0 auto;border:3px solid #1FE0C5;border-radius:12px;padding:18px;background:#0A181E;}table{width:100%;border-collapse:collapse;margin-top:12px;}th,td{border:1px solid #224A59;padding:7px;text-align:left;}th{background:#122C36;color:#1FE0C5;}</style></head><body>" +
                    $"<div class='sheet'><h1>🛡️ STRONA: {WebUtility.HtmlEncode(snap.FactionName)} ({WebUtility.HtmlEncode(snap.EventName)})</h1>" +
                    $"<p><b>Ważność paczki / QR:</b> {WebUtility.HtmlEncode(snap.ExpirationLabel)} &nbsp;|&nbsp; <b>Hotspot Wi-Fi:</b> {WebUtility.HtmlEncode(url)}</p>" +
                    $"<div style='background:#fff;color:#000;padding:14px;border-radius:8px;width:230px;text-align:center;margin:12px auto;'><img src='https://api.qrserver.com/v1/create-qr-code/?size=210x210&data={qrData}' width='210' height='210'/><br/><b>ZESKANUJ W FIELD TAK HUB</b></div>" +
                    $"<p><b>LoRa Meshtastic:</b> {WebUtility.HtmlEncode(snap.ChannelName)} ({WebUtility.HtmlEncode(snap.Region)} / {WebUtility.HtmlEncode(snap.ModemPreset)}) &nbsp;|&nbsp; <b>OTS:</b> {WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(snap.OtsHost) ? "Off-Grid" : snap.OtsHost)}</p>" +
                    $"<table><thead><tr><th>Rola</th><th>Pasmo</th><th>Częstotliwość / Kanał</th><th>Ton / Kod</th></tr></thead><tbody>{rows}</tbody></table></div></body></html>";

                File.WriteAllText(htmlPath, html, Encoding.UTF8);
                Process.Start(new ProcessStartInfo(htmlPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Błąd generowania plakatu: " + ex.Message, "Field TAK Hub", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        public static void ShowHotspotDistributionModal(Window win)
        {
            var snap = FactionLiveSyncBridge.CaptureCurrentFactionSnapshot(win, null);
            string url = StartLocalHotspotServer(snap);
            Clipboard.SetText(url);
            var res = MessageBox.Show(
                $"Aktywny serwer Hotspot Wi-Fi dla strony: {snap.FactionName}\n" +
                $"Ważność paczki: {snap.ExpirationLabel}\n\n" +
                $"Adres (skopiowano do schowka):\n{url}\n\n" +
                "Czy chcesz teraz otworzyć Plakat A4 z kodem QR do zeskanowania przez telefony?",
                "📡 Dystrybucja przez HOTSPOT Wi-Fi",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (res == MessageBoxResult.Yes)
                GenerateAndOpenSafezonePosterHtml(win);
        }

        private static string StartLocalHotspotServer(FactionSnapshotData snap)
        {
            string ip = "127.0.0.1";
            try
            {
                foreach (var a in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                    if (a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)) { ip = a.ToString(); break; }
            }
            catch { }
            try
            {
                if (_hotspotListener == null || !_hotspotListener.IsListening)
                {
                    _hotspotListener = new HttpListener();
                    _hotspotListener.Prefixes.Add($"http://localhost:{_hotspotPort}/");
                    _hotspotListener.Start();
                    _hotspotListener.BeginGetContext(OnHttpReq, null);
                }
            }
            catch { }
            LatestHotspotJsonPayload = snap.ToQrJson();
            return $"http://{ip}:{_hotspotPort}/ftak.json";
        }

        private static void OnHttpReq(IAsyncResult ar)
        {
            try
            {
                if (_hotspotListener == null || !_hotspotListener.IsListening) return;
                var ctx = _hotspotListener.EndGetContext(ar);
                _hotspotListener.BeginGetContext(OnHttpReq, null);
                byte[] b = Encoding.UTF8.GetBytes(LatestHotspotJsonPayload ?? "{}");
                ctx.Response.ContentType = "application/json; charset=utf-8";
                ctx.Response.ContentLength64 = b.Length;
                ctx.Response.OutputStream.Write(b, 0, b.Length);
                ctx.Response.OutputStream.Close();
            }
            catch { }
        }

        private static string ExtractButtonText(Button btn)
        {
            if (btn.Content is string s) return s;
            if (btn.Content is TextBlock tb) return tb.Text ?? "";
            if (btn.Content is Panel p) return string.Join(" ", p.Children.OfType<TextBlock>().Select(x => x.Text));
            return btn.Content?.ToString() ?? "";
        }
    }

    internal sealed class FactionVoiceRowData
    {
        public string Role { get; set; } = "Dowodzenie HQ";
        public string Band { get; set; } = "PMR";
        public string Freq { get; set; } = "CH 1 (446.00625 MHz)";
        public string Tone { get; set; } = "CTCSS 67.0 Hz";
    }

    internal sealed class FactionSnapshotData
    {
        public string EventName { get; set; } = "MILSIM 2026";
        public string FactionName { get; set; } = "BLUFOR";
        public string ColorHex { get; set; } = "#2684FF";
        public string ChannelName { get; set; } = "BLUFOR-MESH";
        public string Psk { get; set; } = "";
        public string Region { get; set; } = "EU_868";
        public string ModemPreset { get; set; } = "SHORT_FAST";
        public string OtsHost { get; set; } = "";
        public string EmergencyPhone { get; set; } = "+48 600 000 000";
        public int ExpirationHours { get; set; } = 0;
        public string ExpirationLabel => ExpirationHours <= 0 ? "Bezterminowo (0)" : $"{ExpirationHours} h";
        public List<FactionVoiceRowData> VoiceChannels { get; set; } = new List<FactionVoiceRowData>();

        public string ToQrJson()
        {
            var vc = new StringBuilder("[");
            for (int i = 0; i < VoiceChannels.Count; i++)
            {
                var v = VoiceChannels[i];
                if (i > 0) vc.Append(",");
                vc.Append($"{{\"roleName\":\"{Esc(v.Role)}\",\"bandType\":\"{Esc(v.Band)}\",\"freqMhz\":\"{Esc(v.Freq)}\",\"toneOrCode\":\"{Esc(v.Tone)}\"}}");
            }
            vc.Append("]");
            return $"{{\"schemaVersion\":\"3.5\",\"eventName\":\"{Esc(EventName)}\",\"Name\":\"{Esc(FactionName)}\",\"ColorHex\":\"{Esc(ColorHex)}\",\"ChannelName\":\"{Esc(ChannelName)}\",\"Psk\":\"{Esc(Psk)}\",\"LoraRegion\":\"{Esc(Region)}\",\"LoraModemPreset\":\"{Esc(ModemPreset)}\",\"OtsHost\":\"{Esc(OtsHost)}\",\"EmergencyPhone\":\"{Esc(EmergencyPhone)}\",\"ExpirationHours\":{ExpirationHours},\"IsPermanent\":{(ExpirationHours <= 0 ? "true" : "false")},\"VoiceChannels\":{vc}}}";
        }
        private static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    internal static class FactionLiveSyncBridge
    {
        public static List<string> GetAllFactionNamesFromUi(Window win)
        {
            var res = new List<string>();
            if (win == null) return new List<string> { "BLUFOR", "OPFOR" };
            try
            {
                var all = BuilderPackageAndUiPatcher.GetVisualTreeElements(win);
                foreach (var lb in all.OfType<ListBox>())
                {
                    foreach (var it in lb.Items)
                    {
                        string n = it?.GetType().GetProperty("Name")?.GetValue(it)?.ToString() ?? it?.ToString() ?? "";
                        if (!string.IsNullOrWhiteSpace(n) && !res.Contains(n, StringComparer.OrdinalIgnoreCase)) res.Add(n);
                    }
                }
                foreach (var tb in all.OfType<TextBox>())
                {
                    var p = VisualTreeHelper.GetParent(tb) as Panel;
                    if (p != null && p.Children.OfType<TextBlock>().Any(l => (l.Text ?? "").Contains("Nazwa strony", StringComparison.OrdinalIgnoreCase)))
                    {
                        string v = (tb.Text ?? "").Trim();
                        if (!string.IsNullOrWhiteSpace(v) && !res.Contains(v, StringComparer.OrdinalIgnoreCase)) res.Insert(0, v);
                    }
                }
            }
            catch { }
            if (res.Count == 0) { res.Add("BLUFOR"); res.Add("OPFOR"); }
            return res;
        }

        public static FactionSnapshotData CaptureCurrentFactionSnapshot(Window win, string preferredFactionName)
        {
            var snap = new FactionSnapshotData();
            if (!string.IsNullOrWhiteSpace(preferredFactionName)) snap.FactionName = preferredFactionName.Trim();
            if (win == null) return snap;

            try
            {
                var all = BuilderPackageAndUiPatcher.GetVisualTreeElements(win);
                foreach (var tb in all.OfType<TextBox>())
                {
                    string val = (tb.Text ?? "").Trim();
                    var p = VisualTreeHelper.GetParent(tb) as Panel;
                    string lbl = p != null ? string.Join(" ", p.Children.OfType<TextBlock>().Select(x => x.Text)) : "";
                    if (string.IsNullOrWhiteSpace(lbl)) continue;

                    if (lbl.Contains("wydarzenia", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(val)) snap.EventName = val;
                    else if (lbl.Contains("Nazwa strony", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(preferredFactionName) && !string.IsNullOrWhiteSpace(val)) snap.FactionName = val;
                    else if (lbl.Contains("kanału Meshtastic", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(val)) snap.ChannelName = val;
                    else if (lbl.Contains("OpenTAK", StringComparison.OrdinalIgnoreCase)) snap.OtsHost = val;
                    else if (lbl.Contains("alarmowy", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(val)) snap.EmergencyPhone = val;
                    else if ((lbl.Contains("Ważność", StringComparison.OrdinalIgnoreCase) || lbl.Contains("bezterminowo", StringComparison.OrdinalIgnoreCase)) && int.TryParse(val, out int h))
                        snap.ExpirationHours = h <= 0 ? 0 : h;
                }

                foreach (var dg in all.OfType<DataGrid>())
                {
                    foreach (var row in dg.Items)
                    {
                        if (row == null) continue;
                        var t = row.GetType();
                        string r = (t.GetProperty("Role") ?? t.GetProperty("RoleName") ?? t.GetProperty("Name"))?.GetValue(row)?.ToString() ?? "";
                        string b = (t.GetProperty("Band") ?? t.GetProperty("BandType"))?.GetValue(row)?.ToString() ?? "PMR";
                        string f = (t.GetProperty("Frequency") ?? t.GetProperty("FreqMhz") ?? t.GetProperty("Freq"))?.GetValue(row)?.ToString() ?? "";
                        string tn = (t.GetProperty("Tone") ?? t.GetProperty("ToneOrCode"))?.GetValue(row)?.ToString() ?? "-";
                        if (!string.IsNullOrWhiteSpace(r) || !string.IsNullOrWhiteSpace(f))
                            snap.VoiceChannels.Add(new FactionVoiceRowData { Role = r, Band = b, Freq = f, Tone = tn });
                    }
                }
            }
            catch { }

            if (snap.VoiceChannels.Count == 0)
            {
                snap.VoiceChannels.Add(new FactionVoiceRowData { Role = "Dowodzenie HQ", Band = "PMR", Freq = "CH 1 (446.00625 MHz)", Tone = "CTCSS 67.0 Hz" });
                snap.VoiceChannels.Add(new FactionVoiceRowData { Role = "Taktyczny (Zapasowy)", Band = "PMR", Freq = "CH 3 (446.03125 MHz)", Tone = "CTCSS 88.5 Hz" });
                snap.VoiceChannels.Add(new FactionVoiceRowData { Role = "Ratunkowy ORGA / HQ", Band = "PMR", Freq = "CH 8 (446.09375 MHz)", Tone = "Brak tonu" });
            }
            return snap;
        }
    }
}
