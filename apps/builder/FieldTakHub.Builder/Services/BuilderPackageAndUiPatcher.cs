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
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace FieldTakHub.Builder.Services
{
    internal static class BuilderPackageAndUiPatcher
    {
        private static bool _sharedFoldersInitialized;
        private static bool _initialPassDone;
        private static HttpListener _hotspotListener;
        private static int _hotspotPort = 8769;
        public static string LatestHotspotJsonPayload = "{}";
        private static Window _mainWindowRef;

        private static readonly string[] TacticalColorPalette = new[]
        {
            "#2684FF", "#EB4034", "#2ECC71", "#FAD02C",
            "#9B59B6", "#FF9F43", "#1FE0C5", "#E84393",
            "#A3CB38", "#00CEC9", "#FF6B6B", "#54A0FF",
            "#5F27CD", "#10AC84", "#F368E0", "#FF9FF3"
        };

        private static readonly HashSet<string> _assignedColorsSession = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "#2684FF"
        };

        /// <summary>
        /// Błyskawiczne przejście po Drzewie Logicznym (Panel.Children, Decorator.Child, ScrollViewer.Content).
        /// Nie wchodzi w szablony ControlTemplate i działa również na zakładkach w tle.
        /// </summary>
        public static List<DependencyObject> GetLogicalElements(DependencyObject root)
        {
            var result = new List<DependencyObject>(128);
            if (root == null) return result;

            var visited = new HashSet<DependencyObject>();
            var q = new Queue<DependencyObject>(64);
            q.Enqueue(root);
            visited.Add(root);

            while (q.Count > 0)
            {
                var cur = q.Dequeue();
                result.Add(cur);

                if (cur is Panel panel)
                {
                    foreach (UIElement ch in panel.Children)
                    {
                        if (ch != null && visited.Add(ch)) q.Enqueue(ch);
                    }
                }
                else if (cur is Decorator dec)
                {
                    if (dec.Child != null && visited.Add(dec.Child)) q.Enqueue(dec.Child);
                }
                else if (cur is ScrollViewer sv)
                {
                    if (sv.Content is DependencyObject svContent && visited.Add(svContent)) q.Enqueue(svContent);
                }
                else if (cur is ContentControl cc && !(cur is ButtonBase) && !(cur is ListBoxItem) && !(cur is ComboBoxItem))
                {
                    if (cc.Content is DependencyObject ccContent && visited.Add(ccContent)) q.Enqueue(ccContent);
                }
            }
            return result;
        }

        public static List<DependencyObject> GetVisualTreeElements(DependencyObject root, bool includeCollapsed = false)
        {
            return GetLogicalElements(root);
        }

        public static string GetNextUniqueFactionColorHex(Window win = null)
        {
            var w = win ?? _mainWindowRef ?? Application.Current?.MainWindow;
            var usedOnScreen = new HashSet<string>(_assignedColorsSession, StringComparer.OrdinalIgnoreCase);

            try
            {
                if (w != null)
                {
                    foreach (var snap in FactionLiveSyncBridge.ReadAllFactionsFromScreenAndMemory(w))
                    {
                        if (!string.IsNullOrWhiteSpace(snap.ColorHex))
                            usedOnScreen.Add(snap.ColorHex.Trim());
                    }
                }
            }
            catch { }

            var rng = new Random(Guid.NewGuid().GetHashCode());
            var available = TacticalColorPalette.Where(c => !usedOnScreen.Contains(c)).OrderBy(_ => rng.Next()).ToList();
            string chosen;
            if (available.Count > 0)
            {
                chosen = available[0];
            }
            else
            {
                do
                {
                    chosen = string.Format("#{0:X2}{1:X2}{2:X2}", rng.Next(50, 240), rng.Next(60, 240), rng.Next(50, 240));
                } while (usedOnScreen.Contains(chosen));
            }

            _assignedColorsSession.Add(chosen);
            return chosen;
        }

        public static void RequestSingleLayoutPass() { }
        public static bool AllowSingleLayoutPass() => false;
        public static void ApplyEnhancementsThrottled(Window win) { }

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
            if (!_sharedFoldersInitialized)
            {
                _sharedFoldersInitialized = true;
                Directory.CreateDirectory(Path.Combine(wspolne, "apk"));
                Directory.CreateDirectory(Path.Combine(wspolne, "plugins"));
                Directory.CreateDirectory(Path.Combine(wspolne, "maps"));
                Directory.CreateDirectory(Path.Combine(root, "out"));
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

        public static string EnsureActiveFactionOutputFolder(Window win, string specificFaction = null)
        {
            EnsureSharedFolderStructure();
            var snap = FactionLiveSyncBridge.CaptureCurrentFactionSnapshot(win, specificFaction);
            string outDir = Path.Combine(GetWorkspaceRootDir(), "out", SanitizeFolderName(snap.FactionName));
            Directory.CreateDirectory(outDir);
            return outDir;
        }

        public static void ApplyEnhancements(Window win, List<DependencyObject> ignoredElements)
        {
            if (win == null) return;
            _mainWindowRef = win;
            EnsureSharedFolderStructure();

            try
            {
                if (win.MinWidth > 900) win.MinWidth = 900;
                if (win.MinHeight > 580) win.MinHeight = 580;

                var elements = GetLogicalElements(win);
                if (!_initialPassDone)
                {
                    _initialPassDone = true;
                    CollapseLegacyTopModeBarPrecisely(elements);
                    PatchZeroExpirationInputs(elements);
                }

                HookSidebarCollapseAndIcons(win, elements);
                HookFactionCardsAndAddButtons(win, elements);
                PatchFtakGeneratorCardToThreeButtons(win, elements);
            }
            catch { }
        }

        private static void HookSidebarCollapseAndIcons(Window win, List<DependencyObject> elements)
        {
            try
            {
                foreach (var border in elements.OfType<Border>())
                {
                    if (Grid.GetColumn(border) == 0 && (border.Width >= 44 && border.Width <= 290 || border.ActualWidth >= 44 && border.ActualWidth <= 290))
                    {
                        double w = border.ActualWidth > 0 ? border.ActualWidth : border.Width;
                        bool isCollapsed = w > 0 && w < 95;
                        var sideButtons = GetLogicalElements(border).OfType<Button>().ToList();

                        for (int idx = 0; idx < sideButtons.Count; idx++)
                        {
                            var btn = sideButtons[idx];
                            if (idx == 0 && !(btn.Content is Panel))
                            {
                                btn.Padding = new Thickness(0);
                                btn.HorizontalContentAlignment = HorizontalAlignment.Center;
                                btn.VerticalContentAlignment = VerticalAlignment.Center;
                                btn.Foreground = new SolidColorBrush(Color.FromRgb(31, 224, 197));
                                btn.FontSize = 14;
                                btn.FontWeight = FontWeights.Bold;
                                btn.Content = isCollapsed ? "❯" : "❮";

                                if (btn.Tag?.ToString() != "FTH_SIDEBAR_TOGGLE_BTN")
                                {
                                    btn.Tag = "FTH_SIDEBAR_TOGGLE_BTN";
                                    btn.Click += (_, __) =>
                                    {
                                        win.Dispatcher.BeginInvoke(new Action(() =>
                                        {
                                            HookSidebarCollapseAndIcons(win, GetLogicalElements(win));
                                        }), DispatcherPriority.Loaded);
                                    };
                                }
                                continue;
                            }

                            if (btn.Content is Panel p)
                            {
                                var tbs = p.Children.OfType<TextBlock>().ToList();
                                if (isCollapsed)
                                {
                                    btn.Padding = new Thickness(0);
                                    btn.HorizontalContentAlignment = HorizontalAlignment.Center;
                                    p.HorizontalAlignment = HorizontalAlignment.Center;
                                    p.Margin = new Thickness(0);
                                    if (tbs.Count >= 1)
                                    {
                                        tbs[0].Margin = new Thickness(0);
                                        tbs[0].Padding = new Thickness(0);
                                        tbs[0].Width = double.NaN;
                                        tbs[0].FontSize = 16;
                                        tbs[0].TextAlignment = TextAlignment.Center;
                                        tbs[0].HorizontalAlignment = HorizontalAlignment.Center;
                                        tbs[0].Visibility = Visibility.Visible;
                                    }
                                    for (int i = 1; i < tbs.Count; i++) tbs[i].Visibility = Visibility.Collapsed;
                                }
                                else
                                {
                                    btn.Padding = new Thickness(10, 6, 10, 6);
                                    btn.HorizontalContentAlignment = HorizontalAlignment.Left;
                                    p.HorizontalAlignment = HorizontalAlignment.Left;
                                    if (tbs.Count >= 1)
                                    {
                                        tbs[0].Margin = new Thickness(0, 0, 8, 0);
                                        tbs[0].FontSize = 14;
                                        tbs[0].Visibility = Visibility.Visible;
                                    }
                                    for (int i = 1; i < tbs.Count; i++) tbs[i].Visibility = Visibility.Visible;
                                }
                            }
                        }
                        break;
                    }
                }
            }
            catch { }
        }

        public static void HookFactionCardsAndAddButtons(Window win, List<DependencyObject> elements)
        {
            if (elements == null) return;
            EnsureDistinctFactionCardColorsOnScreen(win, elements);

            foreach (var btn in elements.OfType<Button>())
            {
                string txt = ExtractButtonText(btn).Trim();

                if (txt == "+" && btn.Tag?.ToString() != "FTH_PLUS_FACTION_HOOKED")
                {
                    btn.Tag = "FTH_PLUS_FACTION_HOOKED";
                    btn.Click += (_, __) =>
                    {
                        win.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            var currentElems = FactionLiveSyncBridge.GetAllRootVisualsAcrossTabs(win);
                            EnsureDistinctFactionCardColorsOnScreen(win, currentElems);
                            HookFactionCardsAndAddButtons(win, currentElems);
                        }), DispatcherPriority.Loaded);
                    };
                }

                if ((txt.Contains("Pliki str", StringComparison.OrdinalIgnoreCase) || txt.StartsWith("factions/", StringComparison.OrdinalIgnoreCase)) &&
                    btn.Tag?.ToString() != "FTH_FAC_FILES_ACTIVE")
                {
                    btn.Tag = "FTH_FAC_FILES_ACTIVE";
                    btn.IsEnabled = true;
                    btn.Cursor = Cursors.Hand;
                    btn.PreviewMouseLeftButtonDown += (s, e) =>
                    {
                        e.Handled = true;
                        try
                        {
                            string facName = null;
                            if (txt.Contains("/")) facName = txt.Split('/').Last().Trim().ToUpperInvariant();
                            string dir = EnsureActiveFactionOutputFolder(win, facName);
                            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true });
                        }
                        catch { }
                    };
                }
            }
        }

        private static void EnsureDistinctFactionCardColorsOnScreen(Window win, List<DependencyObject> elements)
        {
            try
            {
                var seenColors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var headerBlocks = elements.OfType<TextBlock>()
                    .Where(t => (t.Text ?? "").Contains("Generator Stron —", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                for (int idx = 0; idx < headerBlocks.Count; idx++)
                {
                    var hdrTb = headerBlocks[idx];
                    DependencyObject cardRoot = LogicalTreeHelper.GetParent(hdrTb) ?? VisualTreeHelper.GetParent(hdrTb);
                    while (cardRoot != null && !(cardRoot is Border b && b.BorderThickness.Top >= 1))
                    {
                        cardRoot = LogicalTreeHelper.GetParent(cardRoot) ?? VisualTreeHelper.GetParent(cardRoot);
                    }
                    if (cardRoot == null) continue;

                    var cardElems = GetLogicalElements(cardRoot);
                    TextBox nameBox = cardElems.OfType<TextBox>().FirstOrDefault();
                    Button colorBtn = cardElems.OfType<Button>().FirstOrDefault(b =>
                        Regex.IsMatch(ExtractButtonText(b), @"#[0-9A-Fa-f]{6}"));

                    if (nameBox != null)
                    {
                        string curName = (nameBox.Text ?? "").Trim();
                        if (seenNames.Contains(curName) && curName.StartsWith("STRONA-", StringComparison.OrdinalIgnoreCase))
                        {
                            int nextNum = idx + 1;
                            while (seenNames.Contains("STRONA-" + nextNum)) nextNum++;
                            string newName = "STRONA-" + nextNum;
                            nameBox.Text = newName;
                            hdrTb.Text = "Generator Stron — " + newName;
                            curName = newName;
                        }
                        if (!string.IsNullOrWhiteSpace(curName))
                            seenNames.Add(curName);
                    }

                    if (colorBtn != null)
                    {
                        string btnText = ExtractButtonText(colorBtn);
                        Match hexMatch = Regex.Match(btnText, @"#[0-9A-Fa-f]{6}");
                        if (hexMatch.Success)
                        {
                            string curHex = hexMatch.Value.ToUpperInvariant();
                            if (seenColors.Contains(curHex))
                            {
                                string uniqueHex = GetNextUniqueFactionColorHex(win);
                                ApplyNewHexColorToCard(cardRoot as Border, colorBtn, uniqueHex);
                                curHex = uniqueHex;
                            }
                            seenColors.Add(curHex);
                            _assignedColorsSession.Add(curHex);
                        }
                    }
                }
            }
            catch { }
        }

        private static void ApplyNewHexColorToCard(Border cardBorder, Button colorBtn, string newHex)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(newHex);
                var brush = new SolidColorBrush(color);

                if (colorBtn.Content is string)
                {
                    colorBtn.Content = "■ " + newHex;
                }
                else if (colorBtn.Content is Panel p)
                {
                    foreach (var b in p.Children.OfType<Border>()) b.Background = brush;
                    foreach (var r in p.Children.OfType<System.Windows.Shapes.Shape>()) r.Fill = brush;
                    foreach (var tb in p.Children.OfType<TextBlock>())
                    {
                        if (tb.Text != null && tb.Text.Contains("#")) tb.Text = newHex;
                    }
                }
                colorBtn.BorderBrush = brush;

                object dc = colorBtn.DataContext ?? cardBorder?.DataContext ?? colorBtn.Tag;
                if (dc != null && !(dc is string))
                {
                    var prop = dc.GetType().GetProperty("ColorHex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase)
                            ?? dc.GetType().GetProperty("Color", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
                    if (prop != null && prop.CanWrite) prop.SetValue(dc, newHex);
                }
            }
            catch { }
        }

        public static void CollapseLegacyTopModeBarPrecisely(List<DependencyObject> elements)
        {
            if (elements == null) return;
            foreach (var el in elements)
            {
                if (el is TextBlock tb)
                {
                    string txt = (tb.Text ?? "").Trim();
                    if (txt.Contains("TRYB PRACY BUILDERA", StringComparison.OrdinalIgnoreCase) ||
                        txt.Contains("BUILDER MODE", StringComparison.OrdinalIgnoreCase))
                    {
                        tb.Visibility = Visibility.Collapsed;
                        DependencyObject cur = tb;
                        while (cur != null && !(cur is Window))
                        {
                            if (cur is Border b)
                            {
                                b.Visibility = Visibility.Collapsed;
                                b.Height = 0;
                                b.Margin = new Thickness(0);
                                b.Padding = new Thickness(0);
                                b.BorderThickness = new Thickness(0);
                                break;
                            }
                            cur = LogicalTreeHelper.GetParent(cur) ?? VisualTreeHelper.GetParent(cur);
                        }
                    }
                    else if (txt.StartsWith("BUILDER v2.", StringComparison.OrdinalIgnoreCase))
                    {
                        tb.Text = "BUILDER v3.5";
                    }
                }
            }
        }

        private static void PatchZeroExpirationInputs(List<DependencyObject> safeElements)
        {
            foreach (var tb in safeElements.OfType<TextBlock>())
            {
                string txt = (tb.Text ?? "").Trim();
                if ((txt.Contains("Ważność", StringComparison.OrdinalIgnoreCase) || txt.Contains("Expiration", StringComparison.OrdinalIgnoreCase)) &&
                    !txt.Contains("0 = bezterminowo", StringComparison.OrdinalIgnoreCase))
                {
                    tb.Text = txt.TrimEnd(':') + " (0 = bezterminowo):";
                }
            }
        }

        private static void PatchFtakGeneratorCardToThreeButtons(Window win, List<DependencyObject> safeElements)
        {
            foreach (var tb in safeElements.OfType<TextBlock>())
            {
                string t = (tb.Text ?? "").Trim();
                if (t.Contains("GENERATOR PACZEK .FTAK", StringComparison.OrdinalIgnoreCase) || t.Contains("PACKAGE BUILDER .FTAK", StringComparison.OrdinalIgnoreCase))
                {
                    DependencyObject p = LogicalTreeHelper.GetParent(tb) ?? VisualTreeHelper.GetParent(tb);
                    while (p != null && !(p is StackPanel || p is Grid))
                        p = LogicalTreeHelper.GetParent(p) ?? VisualTreeHelper.GetParent(p);

                    if (p is Panel cardPanel && cardPanel.Tag?.ToString() != "FTH_3BTN_OK")
                    {
                        cardPanel.Tag = "FTH_3BTN_OK";
                        tb.Text = BuilderI18n.T(
                            "🎯 GENERATOR PACZEK .FTAK (FOLDER out\\<STRONA>\\ + WSPÓLNY FOLDER APK / PLUGINY / MAPY)",
                            "🎯 .FTAK PACKAGE GENERATOR (out\\<FACTION>\\ + SHARED APK / PLUGINS / MAPS)");

                        var desc = GetLogicalElements(cardPanel);
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
            var allFactions = FactionLiveSyncBridge.ReadAllFactionsFromScreenAndMemory(win);
            foreach (var snap in allFactions)
            {
                string facDir = Path.Combine(GetWorkspaceRootDir(), "out", SanitizeFolderName(snap.FactionName));
                Directory.CreateDirectory(facDir);
                File.WriteAllText(Path.Combine(facDir, SanitizeFolderName(snap.FactionName) + "_ftak.json"), snap.ToQrJson(), Encoding.UTF8);
            }
            MessageBox.Show($"Zbudowano paczki dla wszystkich aktywnych stron ({allFactions.Count}) w katalogu:\n{Path.Combine(GetWorkspaceRootDir(), "out")}", "Field TAK Hub", MessageBoxButton.OK, MessageBoxImage.Information);
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

        public static string ExtractButtonText(Button btn)
        {
            if (btn == null) return "";
            if (btn.Content is string s) return s;
            if (btn.Content is TextBlock tb) return tb.Text ?? "";
            if (btn.Content is Panel p)
            {
                var allTbs = GetLogicalElements(p).OfType<TextBlock>().Select(x => x.Text);
                return string.Join(" ", allTbs);
            }
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
        public string FactionName { get; set; } = "ORGA";
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
        private static List<FieldInfo> _cachedStaticCollectionFields;
        private static List<FieldInfo> _cachedStaticDependencyFields;

        public static List<DependencyObject> GetAllRootVisualsAcrossTabs(Window win)
        {
            var combined = new List<DependencyObject>(256);
            var visitedRoots = new HashSet<DependencyObject>();

            void AddFromRoot(DependencyObject r)
            {
                if (r == null || !visitedRoots.Add(r)) return;
                combined.AddRange(BuilderPackageAndUiPatcher.GetLogicalElements(r));
            }

            if (win != null)
            {
                AddFromRoot(win);
                var instFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                foreach (var f in win.GetType().GetFields(instFlags))
                {
                    try { if (f.GetValue(win) is DependencyObject dobj) AddFromRoot(dobj); } catch { }
                }
            }

            if (_cachedStaticDependencyFields == null)
            {
                _cachedStaticDependencyFields = new List<FieldInfo>();
                var staticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                foreach (var t in typeof(BuilderPackageAndUiPatcher).Assembly.GetTypes())
                {
                    if (t.Namespace == null || !t.Namespace.StartsWith("FieldTakHub.Builder")) continue;
                    foreach (var sf in t.GetFields(staticFlags))
                    {
                        if (typeof(DependencyObject).IsAssignableFrom(sf.FieldType))
                            _cachedStaticDependencyFields.Add(sf);
                    }
                }
            }

            foreach (var sf in _cachedStaticDependencyFields)
            {
                try { if (sf.GetValue(null) is DependencyObject sd) AddFromRoot(sd); } catch { }
            }

            return combined;
        }

        public static List<FactionSnapshotData> ReadAllFactionsFromScreenAndMemory(Window win)
        {
            var list = new List<FactionSnapshotData>();
            if (win == null) win = Application.Current?.MainWindow;

            try
            {
                var allElems = GetAllRootVisualsAcrossTabs(win);
                var cardHeaders = allElems.OfType<TextBlock>()
                    .Where(t => (t.Text ?? "").Contains("Generator Stron —", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var hdr in cardHeaders)
                {
                    DependencyObject cardRoot = LogicalTreeHelper.GetParent(hdr) ?? VisualTreeHelper.GetParent(hdr);
                    while (cardRoot != null && !(cardRoot is Border b && b.BorderThickness.Top >= 1))
                        cardRoot = LogicalTreeHelper.GetParent(cardRoot) ?? VisualTreeHelper.GetParent(cardRoot);

                    if (cardRoot == null) continue;

                    var cardItems = BuilderPackageAndUiPatcher.GetLogicalElements(cardRoot);
                    var textBoxes = cardItems.OfType<TextBox>().ToList();
                    if (textBoxes.Count == 0) continue;

                    var snap = new FactionSnapshotData();
                    string facName = (textBoxes[0].Text ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(facName))
                        facName = (hdr.Text ?? "").Replace("Generator Stron —", "").Trim();
                    snap.FactionName = string.IsNullOrWhiteSpace(facName) ? "ORGA" : facName;
                    snap.ChannelName = snap.FactionName.ToUpperInvariant() + "-MESH";

                    var colorBtn = cardItems.OfType<Button>().FirstOrDefault(b =>
                        Regex.IsMatch(BuilderPackageAndUiPatcher.ExtractButtonText(b), @"#[0-9A-Fa-f]{6}"));
                    if (colorBtn != null)
                    {
                        var m = Regex.Match(BuilderPackageAndUiPatcher.ExtractButtonText(colorBtn), @"#[0-9A-Fa-f]{6}");
                        if (m.Success) snap.ColorHex = m.Value.ToUpperInvariant();
                    }

                    foreach (var rowGrid in cardItems.OfType<Grid>())
                    {
                        if (rowGrid.ColumnDefinitions.Count >= 3)
                        {
                            var rowBoxes = rowGrid.Children.OfType<TextBox>().ToList();
                            var rowCombos = rowGrid.Children.OfType<ComboBox>().ToList();

                            if (rowBoxes.Count >= 1 && (rowBoxes.Count + rowCombos.Count) >= 3)
                            {
                                var allInputs = rowGrid.Children.Cast<UIElement>()
                                    .Where(el => el is TextBox || el is ComboBox)
                                    .ToList();

                                string GetVal(UIElement el)
                                {
                                    if (el is TextBox tb) return (tb.Text ?? "").Trim();
                                    if (el is ComboBox cb)
                                    {
                                        if (cb.SelectedItem is ComboBoxItem cbi) return (cbi.Content?.ToString() ?? "").Trim();
                                        return (cb.SelectedItem?.ToString() ?? cb.Text ?? "").Trim();
                                    }
                                    return "";
                                }

                                if (allInputs.Count >= 3)
                                {
                                    string c0 = GetVal(allInputs[0]);
                                    string c1 = GetVal(allInputs[1]);
                                    string c2 = GetVal(allInputs[2]);
                                    string c3 = allInputs.Count >= 4 ? GetVal(allInputs[3]) : "-";

                                    string role = c0;
                                    string band = c1;
                                    string freq = c2;
                                    string tone = c3;

                                    if (c0.Equals("PMR", StringComparison.OrdinalIgnoreCase) || c0.Equals("VHF", StringComparison.OrdinalIgnoreCase) ||
                                        c0.Equals("UHF", StringComparison.OrdinalIgnoreCase) || c0.Equals("DMR", StringComparison.OrdinalIgnoreCase))
                                    {
                                        band = c0;
                                        role = c1;
                                        freq = c2;
                                    }

                                    if (!string.IsNullOrWhiteSpace(role) || !string.IsNullOrWhiteSpace(freq))
                                    {
                                        snap.VoiceChannels.Add(new FactionVoiceRowData
                                        {
                                            Role = string.IsNullOrWhiteSpace(role) ? "Kanał" : role,
                                            Band = string.IsNullOrWhiteSpace(band) ? "PMR" : band,
                                            Freq = string.IsNullOrWhiteSpace(freq) ? "-" : freq,
                                            Tone = string.IsNullOrWhiteSpace(tone) ? "-" : tone
                                        });
                                    }
                                }
                            }
                        }
                    }

                    list.Add(snap);
                }

                foreach (var memObj in EnumerateAllInMemoryFactions(win))
                {
                    string n = GetPropStr(memObj, "Name", "FactionName", "SideName");
                    if (string.IsNullOrWhiteSpace(n)) continue;
                    var existing = list.FirstOrDefault(x => string.Equals(x.FactionName, n.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (existing == null)
                    {
                        existing = new FactionSnapshotData { FactionName = n.Trim() };
                        list.Add(existing);
                    }

                    existing.ColorHex = GetPropStr(memObj, "ColorHex", "Color") ?? existing.ColorHex;
                    existing.ChannelName = GetPropStr(memObj, "ChannelName", "MeshChannel") ?? existing.ChannelName;
                    existing.OtsHost = GetPropStr(memObj, "OtsHost", "ServerHost") ?? existing.OtsHost;
                    existing.EmergencyPhone = GetPropStr(memObj, "EmergencyPhone", "SosPhone") ?? existing.EmergencyPhone;

                    if (existing.VoiceChannels.Count == 0)
                    {
                        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
                        foreach (var prop in memObj.GetType().GetProperties(flags))
                        {
                            if (typeof(IEnumerable).IsAssignableFrom(prop.PropertyType) && prop.PropertyType != typeof(string))
                            {
                                if (prop.GetValue(memObj) is IEnumerable vcList)
                                {
                                    foreach (var vi in vcList)
                                    {
                                        if (vi == null || vi is string) continue;
                                        string r = GetPropStr(vi, "RoleName", "Role", "Name", "Title");
                                        string b = GetPropStr(vi, "BandType", "Band", "Type") ?? "PMR";
                                        string f = GetPropStr(vi, "FreqMhz", "Frequency", "Freq", "Channel");
                                        string tn = GetPropStr(vi, "ToneOrCode", "Tone", "Ctcss") ?? "-";
                                        if (!string.IsNullOrWhiteSpace(f) || !string.IsNullOrWhiteSpace(r))
                                        {
                                            existing.VoiceChannels.Add(new FactionVoiceRowData
                                            {
                                                Role = string.IsNullOrWhiteSpace(r) ? "Kanał" : r,
                                                Band = b,
                                                Freq = string.IsNullOrWhiteSpace(f) ? "-" : f,
                                                Tone = tn
                                            });
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            return list;
        }

        public static List<string> GetAllFactionNamesFromUi(Window win)
        {
            var snaps = ReadAllFactionsFromScreenAndMemory(win);
            var names = snaps.Select(s => s.FactionName)
                             .Where(n => !string.IsNullOrWhiteSpace(n))
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .ToList();

            if (names.Count == 0)
            {
                names.Add("ORGA");
                names.Add("STRONA-2");
            }
            return names;
        }

        public static List<object> EnumerateAllInMemoryFactions(Window win)
        {
            var list = new List<object>();
            if (win == null) return list;

            try
            {
                if (_cachedStaticCollectionFields == null)
                {
                    _cachedStaticCollectionFields = new List<FieldInfo>();
                    var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                    foreach (var t in typeof(BuilderPackageAndUiPatcher).Assembly.GetTypes())
                    {
                        if (t.Namespace == null || !t.Namespace.StartsWith("FieldTakHub.Builder")) continue;
                        foreach (var sf in t.GetFields(flags))
                        {
                            if (typeof(IEnumerable).IsAssignableFrom(sf.FieldType) && sf.FieldType != typeof(string))
                                _cachedStaticCollectionFields.Add(sf);
                        }
                    }
                }

                foreach (var sf in _cachedStaticCollectionFields)
                {
                    try { CollectFactions(sf.GetValue(null), list); } catch { }
                }
            }
            catch { }
            return list;
        }

        private static void CollectFactions(object val, List<object> list)
        {
            if (val == null || val is string || val is DependencyObject) return;
            if (val is IEnumerable en)
            {
                foreach (var item in en)
                {
                    if (item == null || item is string) continue;
                    var it = item.GetType();
                    if ((it.GetProperty("ChannelName") != null || it.GetProperty("VoiceChannels") != null || it.GetProperty("Psk") != null || it.GetProperty("ColorHex") != null) &&
                        (it.GetProperty("Name") != null || it.GetProperty("FactionName") != null))
                    {
                        if (!list.Contains(item)) list.Add(item);
                    }
                }
            }
        }

        public static string GetPropStr(object obj, params string[] names)
        {
            if (obj == null) return null;
            var t = obj.GetType();
            foreach (string n in names)
            {
                var p = t.GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
                if (p != null)
                {
                    string v = p.GetValue(obj)?.ToString();
                    if (!string.IsNullOrWhiteSpace(v)) return v;
                }
                var f = t.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
                if (f != null)
                {
                    string v = f.GetValue(obj)?.ToString();
                    if (!string.IsNullOrWhiteSpace(v)) return v;
                }
            }
            return null;
        }

        public static FactionSnapshotData CaptureCurrentFactionSnapshot(Window win, string preferredFactionName)
        {
            var all = ReadAllFactionsFromScreenAndMemory(win);
            FactionSnapshotData snap = null;

            if (!string.IsNullOrWhiteSpace(preferredFactionName))
            {
                snap = all.FirstOrDefault(x => string.Equals(x.FactionName, preferredFactionName.Trim(), StringComparison.OrdinalIgnoreCase));
            }
            if (snap == null && all.Count > 0)
            {
                snap = all[0];
            }
            if (snap == null)
            {
                snap = new FactionSnapshotData { FactionName = string.IsNullOrWhiteSpace(preferredFactionName) ? "ORGA" : preferredFactionName.Trim() };
            }

            if (snap.VoiceChannels.Count == 0)
            {
                snap.VoiceChannels.Add(new FactionVoiceRowData { Role = "Dowodzenie HQ", Band = "PMR", Freq = "CH 1 (446.00625 MHz)", Tone = "CTCSS 67.0 Hz" });
            }
            return snap;
        }
    }
}
