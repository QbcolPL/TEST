#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
using System.Windows.Shapes;
using System.Windows.Threading;

namespace FieldTakHub.Builder.Services
{
    public static class TacticalSharedState
    {
        private static readonly string[] TacticalRgbPalette = new[]
        {
            "#2684FF", "#EB4034", "#2ECC71", "#FAD02C",
            "#9B59B6", "#FF9F43", "#1FE0C5", "#E84393",
            "#A3CB38", "#00CEC9", "#FF6B6B", "#54A0FF",
            "#5F27CD", "#10AC84", "#F368E0", "#FF9FF3"
        };

        private static readonly HashSet<string> _usedColors = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "#2684FF"
        };

        private static int _factionCounter = 1;
        internal static List<FactionSnapshotData> CachedFactions { get; } = new List<FactionSnapshotData>();
        public static Action<Window> OnOpordTabActivated;

        public static string GetNextUniqueFactionName()
        {
            var existingNames = new HashSet<string>(
                CachedFactions.Select(f => f.FactionName ?? ""),
                StringComparer.OrdinalIgnoreCase);

            _factionCounter++;
            while (existingNames.Contains("STRONA-" + _factionCounter))
            {
                _factionCounter++;
            }
            return "STRONA-" + _factionCounter;
        }

        public static string GetNextUniqueColorHex()
        {
            foreach (var f in CachedFactions)
            {
                if (!string.IsNullOrWhiteSpace(f.ColorHex))
                    _usedColors.Add(f.ColorHex.Trim());
            }

            var rng = new Random(Guid.NewGuid().GetHashCode());
            var available = TacticalRgbPalette.Where(c => !_usedColors.Contains(c)).OrderBy(_ => rng.Next()).ToList();
            string chosen;
            if (available.Count > 0)
            {
                chosen = available[0];
            }
            else
            {
                do
                {
                    chosen = string.Format("#{0:X2}{1:X2}{2:X2}", rng.Next(45, 240), rng.Next(55, 240), rng.Next(45, 240));
                } while (_usedColors.Contains(chosen));
            }

            _usedColors.Add(chosen);
            return chosen;
        }

        public static Color GetNextUniqueWpfColor()
        {
            string hex = GetNextUniqueColorHex();
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return Color.FromRgb(235, 64, 52); }
        }

        public static void RegisterUsedColor(string hex)
        {
            if (!string.IsNullOrWhiteSpace(hex))
                _usedColors.Add(hex.Trim());
        }

        public static void CaptureLiveStateFromWindow(Window win)
        {
            try
            {
                var w = win ?? Application.Current?.MainWindow;
                if (w == null) return;
                var fresh = FactionLiveSyncBridge.ScanFactionsFromVisualsAndMemory(w);
                if (fresh.Count > 0)
                {
                    CachedFactions.Clear();
                    CachedFactions.AddRange(fresh);
                }
            }
            catch { }
        }

        public static void OnTabSwitched(Window win, string modeKey)
        {
            var w = win ?? Application.Current?.MainWindow;
            if (w == null) return;

            CaptureLiveStateFromWindow(w);

            w.Dispatcher.BeginInvoke(new Action(() =>
            {
                BuilderPackageAndUiPatcher.ApplyCleanShellAndPackageFixes(w);
                BuilderTacticalMapAndUsbService.SyncMapAndUsbState(w);
                FixSidebarVisuals(w);

                if (!string.IsNullOrWhiteSpace(modeKey) && modeKey.Contains("OPORD", StringComparison.OrdinalIgnoreCase))
                {
                    CaptureLiveStateFromWindow(w);
                    OnOpordTabActivated?.Invoke(w);
                }
            }), DispatcherPriority.Loaded);
        }

        public static void NotifyModuleSwitched(Window win, string modeKey) => OnTabSwitched(win, modeKey);

        public static void FixSidebarVisuals(Window win)
        {
            try
            {
                var w = win ?? Application.Current?.MainWindow;
                if (w == null) return;

                var topLogical = BuilderPackageAndUiPatcher.GetLogicalElements(w);
                foreach (var border in topLogical.OfType<Border>())
                {
                    if (Grid.GetColumn(border) == 0 && (border.Width >= 44 && border.Width <= 290 || border.ActualWidth >= 44 && border.ActualWidth <= 290))
                    {
                        double currentWidth = border.ActualWidth > 0 ? border.ActualWidth : border.Width;
                        bool isCollapsed = currentWidth > 0 && currentWidth < 95;
                        var sideButtons = BuilderPackageAndUiPatcher.GetLogicalElements(border).OfType<Button>().ToList();

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

                                if (btn.Tag?.ToString() != "FTH_CLEAN_SIDEBAR_TOGGLE")
                                {
                                    btn.Tag = "FTH_CLEAN_SIDEBAR_TOGGLE";
                                    btn.Click += (_, __) =>
                                    {
                                        w.Dispatcher.BeginInvoke(new Action(() => FixSidebarVisuals(w)), DispatcherPriority.Loaded);
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
                                    btn.VerticalContentAlignment = VerticalAlignment.Center;
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
                                    for (int i = 1; i < tbs.Count; i++)
                                        tbs[i].Visibility = Visibility.Collapsed;
                                }
                                else
                                {
                                    btn.Padding = new Thickness(10, 6, 10, 6);
                                    btn.HorizontalContentAlignment = HorizontalAlignment.Left;
                                    btn.VerticalContentAlignment = VerticalAlignment.Center;
                                    p.HorizontalAlignment = HorizontalAlignment.Left;

                                    if (tbs.Count >= 1)
                                    {
                                        tbs[0].Margin = new Thickness(0, 0, 8, 0);
                                        tbs[0].FontSize = 14;
                                        tbs[0].Visibility = Visibility.Visible;
                                    }
                                    for (int i = 1; i < tbs.Count; i++)
                                        tbs[i].Visibility = Visibility.Visible;
                                }
                            }
                        }
                        break;
                    }
                }
            }
            catch { }
        }
    }

    internal static class BuilderPackageAndUiPatcher
    {
        private static bool _sharedFoldersInitialized;
        private static int _hotspotPort = 8769;
        public static string LatestHotspotJsonPayload = "{}";

        public static string GetNextUniqueFactionColorHex(Window win = null) => TacticalSharedState.GetNextUniqueColorHex();
        public static void RequestSingleLayoutPass() { }
        public static bool AllowSingleLayoutPass() => false;
        public static void ApplyEnhancementsThrottled(Window win) { }

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
                        if (ch != null && visited.Add(ch)) q.Enqueue(ch);
                }
                else if (cur is Decorator dec && dec.Child != null && visited.Add(dec.Child))
                    q.Enqueue(dec.Child);
                else if (cur is ScrollViewer sv && sv.Content is DependencyObject svc && visited.Add(svc))
                    q.Enqueue(svc);
                else if (cur is ContentControl cc && !(cur is ButtonBase) && !(cur is ListBoxItem) && !(cur is ComboBoxItem) && cc.Content is DependencyObject ccc && visited.Add(ccc))
                    q.Enqueue(ccc);
            }
            return result;
        }

        public static List<DependencyObject> GetVisualTreeElements(DependencyObject root, bool includeCollapsed = false) => GetLogicalElements(root);

        /// <summary>
        /// Znajduje zewnętrzny kontener pojedynczej karty "Generator Stron — ...", który zawiera zarówno
        /// pole "Nazwa strony:", przycisk "Kolor (RGB):", jak i tabelę "Kanały Głosowe (PMR / VHF / DMR) & SOS".
        /// </summary>
        public static DependencyObject FindCompleteFactionCardContainer(TextBlock hdrTb)
        {
            if (hdrTb == null) return null;
            DependencyObject cur = hdrTb;
            DependencyObject fallbackBorder = null;

            for (int depth = 0; depth < 9 && cur != null; depth++)
            {
                cur = LogicalTreeHelper.GetParent(cur) ?? VisualTreeHelper.GetParent(cur);
                if (cur is Border || cur is Panel)
                {
                    var elems = GetLogicalElements(cur);
                    var texts = elems.OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
                    int hdrCount = texts.Count(t => t.Contains("Generator Stron —", StringComparison.OrdinalIgnoreCase));
                    if (hdrCount > 1) break; // Wyszliśmy za wysoko (do kontenera wszystkich kart)

                    if (cur is Border b && b.BorderThickness.Top >= 1)
                        fallbackBorder = b;

                    bool hasName = texts.Any(t => t.Contains("Nazwa strony", StringComparison.OrdinalIgnoreCase));
                    bool hasVoice = texts.Any(t => t.Contains("Kanały Głosowe", StringComparison.OrdinalIgnoreCase));
                    if (hasName && hasVoice && hdrCount == 1)
                    {
                        return cur;
                    }
                }
            }
            return fallbackBorder;
        }

        public static string GetWorkspaceRootDir()
        {
            string d = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FieldTakHub");
            Directory.CreateDirectory(d);
            return d;
        }

        public static string EnsureSharedFolderStructure()
        {
            string root = GetWorkspaceRootDir();
            string wspolne = System.IO.Path.Combine(root, "wspolne");
            if (!_sharedFoldersInitialized)
            {
                _sharedFoldersInitialized = true;
                Directory.CreateDirectory(System.IO.Path.Combine(wspolne, "apk"));
                Directory.CreateDirectory(System.IO.Path.Combine(wspolne, "plugins"));
                Directory.CreateDirectory(System.IO.Path.Combine(wspolne, "maps"));
                Directory.CreateDirectory(System.IO.Path.Combine(root, "out"));
            }
            return wspolne;
        }

        public static string SanitizeFolderName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "BLUFOR";
            char[] inv = System.IO.Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (char c in name.Trim()) sb.Append(inv.Contains(c) ? '_' : c);
            string r = sb.ToString().Trim();
            return string.IsNullOrEmpty(r) ? "BLUFOR" : r;
        }

        public static string EnsureActiveFactionOutputFolder(Window win, string specificFaction = null)
        {
            EnsureSharedFolderStructure();
            var snap = FactionLiveSyncBridge.CaptureCurrentFactionSnapshot(win, specificFaction);
            string outDir = System.IO.Path.Combine(GetWorkspaceRootDir(), "out", SanitizeFolderName(snap.FactionName));
            Directory.CreateDirectory(outDir);
            return outDir;
        }

        public static void ApplyEnhancements(Window win, List<DependencyObject> ignored)
        {
            ApplyCleanShellAndPackageFixes(win);
            BuilderTacticalMapAndUsbService.SyncMapAndUsbState(win);
            TacticalSharedState.FixSidebarVisuals(win);
        }

        public static void ApplyCleanShellAndPackageFixes(Window win)
        {
            if (win == null) return;
            EnsureSharedFolderStructure();
            try
            {
                if (win.MinWidth > 900) win.MinWidth = 900;
                if (win.MinHeight > 580) win.MinHeight = 580;

                var all = FactionLiveSyncBridge.GetAllRootVisualsAcrossTabs(win);
                CollapseLegacyTopModeBarPrecisely(all);
                PatchZeroExpirationLabels(all);
                HookFactionCardsAndAddButtons(win, all);
                PatchFtakGeneratorCardToThreeButtons(win, all);
                TacticalSharedState.CaptureLiveStateFromWindow(win);
            }
            catch { }
        }

        public static void CollapseLegacyTopModeBarPrecisely(List<DependencyObject> elements)
        {
            if (elements == null) return;
            foreach (var tb in elements.OfType<TextBlock>().ToList())
            {
                string txt = (tb.Text ?? "").Trim();
                if (txt.Contains("TRYB PRACY BUILDERA", StringComparison.OrdinalIgnoreCase) ||
                    txt.Contains("BUILDER MODE", StringComparison.OrdinalIgnoreCase))
                {
                    DependencyObject cur = tb;
                    for (int i = 0; i < 6 && cur != null && !(cur is Window); i++)
                    {
                        if (cur is Border b)
                        {
                            b.Visibility = Visibility.Collapsed;
                            b.Height = 0;
                            b.MinHeight = 0;
                            b.Margin = new Thickness(0);
                            b.Padding = new Thickness(0);
                            b.BorderThickness = new Thickness(0);
                            var parent = LogicalTreeHelper.GetParent(b) ?? VisualTreeHelper.GetParent(b);
                            if (parent is Grid g)
                            {
                                int r = Grid.GetRow(b);
                                if (r >= 0 && r < g.RowDefinitions.Count)
                                {
                                    g.RowDefinitions[r].Height = new GridLength(0);
                                    g.RowDefinitions[r].MinHeight = 0;
                                }
                            }
                        }
                        cur = LogicalTreeHelper.GetParent(cur) ?? VisualTreeHelper.GetParent(cur);
                    }
                }
                else if (txt.StartsWith("BUILDER v2.", StringComparison.OrdinalIgnoreCase) || txt.StartsWith("BUILDER v3.", StringComparison.OrdinalIgnoreCase))
                {
                    tb.Text = "BUILDER v4.0";
                }
            }
        }

        private static void PatchZeroExpirationLabels(List<DependencyObject> elements)
        {
            foreach (var tb in elements.OfType<TextBlock>())
            {
                string txt = (tb.Text ?? "").Trim();
                if ((txt.Contains("Ważność paczki", StringComparison.OrdinalIgnoreCase) ||
                     txt.Contains("Ważność kodu QR", StringComparison.OrdinalIgnoreCase) ||
                     txt.Contains("Expiration", StringComparison.OrdinalIgnoreCase)) &&
                    !txt.Contains("0 = bezterminowo", StringComparison.OrdinalIgnoreCase))
                {
                    tb.Text = txt.TrimEnd(':') + " (0 = bezterminowo):";
                }
            }
        }

        public static void HookFactionCardsAndAddButtons(Window win, List<DependencyObject> elements)
        {
            if (elements == null) return;
            EnsureDistinctCardColorsAndNames(elements);

            foreach (var btn in elements.OfType<Button>())
            {
                string txt = ExtractButtonText(btn).Trim();

                // Napraw Padding małych przycisków ("+", "−", "-"), aby nigdy nie wyświetlały uciętych kropek ":"
                if ((btn.Width >= 20 && btn.Width <= 42) || txt == "+" || txt == "-" || txt == "−" || txt == "✖")
                {
                    btn.Padding = new Thickness(0);
                    btn.HorizontalContentAlignment = HorizontalAlignment.Center;
                    btn.VerticalContentAlignment = VerticalAlignment.Center;
                }

                if ((txt == "+" || txt.Contains("Dodaj stronę", StringComparison.OrdinalIgnoreCase) || txt.Contains("Dodaj kanał", StringComparison.OrdinalIgnoreCase)) &&
                    btn.Tag?.ToString() != "FTH_PLUS_HOOK")
                {
                    btn.Tag = "FTH_PLUS_HOOK";
                    btn.Click += (_, __) =>
                    {
                        win?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            var cur = FactionLiveSyncBridge.GetAllRootVisualsAcrossTabs(win);
                            EnsureDistinctCardColorsAndNames(cur);
                            HookFactionCardsAndAddButtons(win, cur);
                            TacticalSharedState.CaptureLiveStateFromWindow(win);
                        }), DispatcherPriority.Loaded);
                    };
                }

                if ((txt.Contains("Pliki str", StringComparison.OrdinalIgnoreCase) || txt.StartsWith("factions/", StringComparison.OrdinalIgnoreCase)) &&
                    btn.Tag?.ToString() != "FTH_FAC_FILES_OK")
                {
                    btn.Tag = "FTH_FAC_FILES_OK";
                    btn.IsEnabled = true;
                    btn.Visibility = Visibility.Visible;
                    btn.Cursor = Cursors.Hand;
                    if (txt.Contains("Pliki str", StringComparison.OrdinalIgnoreCase))
                        btn.Content = "📂 Pliki strony";

                    btn.IsEnabledChanged += (s, e) => { if (s is Button b && !b.IsEnabled) b.IsEnabled = true; };
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

                if ((txt.Contains("Plakat", StringComparison.OrdinalIgnoreCase) || txt.Contains("Safezone", StringComparison.OrdinalIgnoreCase)) &&
                    btn.Tag?.ToString() != "FTH_POSTER_OK")
                {
                    btn.Tag = "FTH_POSTER_OK";
                    btn.IsEnabled = true;
                    btn.PreviewMouseLeftButtonDown += (s, e) =>
                    {
                        e.Handled = true;
                        GenerateAndOpenSafezonePosterHtml(win);
                    };
                }
            }
        }

        private static void EnsureDistinctCardColorsAndNames(List<DependencyObject> elements)
        {
            var seenColors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hdrs = elements.OfType<TextBlock>()
                .Where(t => (t.Text ?? "").Contains("Generator Stron —", StringComparison.OrdinalIgnoreCase))
                .ToList();

            for (int i = 0; i < hdrs.Count; i++)
            {
                var hdr = hdrs[i];
                var card = FindCompleteFactionCardContainer(hdr);
                if (card == null) continue;

                var items = GetLogicalElements(card);
                var nameBox = items.OfType<TextBox>().FirstOrDefault();
                if (nameBox != null)
                {
                    string n = (nameBox.Text ?? "").Trim();
                    if (seenNames.Contains(n) && n.StartsWith("STRONA-", StringComparison.OrdinalIgnoreCase))
                    {
                        int num = i + 1;
                        while (seenNames.Contains("STRONA-" + num)) num++;
                        string newName = "STRONA-" + num;
                        nameBox.Text = newName;
                        hdr.Text = "Generator Stron — " + newName;
                        n = newName;

                        var kmlBtn = items.OfType<Button>().FirstOrDefault(b => ExtractButtonText(b).Contains("factions/", StringComparison.OrdinalIgnoreCase));
                        if (kmlBtn != null && kmlBtn.Content is string)
                            kmlBtn.Content = "📁 factions/" + newName.ToLowerInvariant();
                    }
                    if (!string.IsNullOrWhiteSpace(n)) seenNames.Add(n);
                }

                var colBtn = items.OfType<Button>().FirstOrDefault(b => Regex.IsMatch(ExtractButtonText(b), @"#[0-9A-Fa-f]{6}"));
                if (colBtn != null)
                {
                    var m = Regex.Match(ExtractButtonText(colBtn), @"#[0-9A-Fa-f]{6}");
                    if (m.Success)
                    {
                        string hex = m.Value.ToUpperInvariant();
                        if (seenColors.Contains(hex))
                        {
                            hex = TacticalSharedState.GetNextUniqueColorHex();
                            UpdateColorBtnVisual(colBtn, hdr, hex);
                        }
                        seenColors.Add(hex);
                        TacticalSharedState.RegisterUsedColor(hex);
                    }
                }
            }
        }

        private static void UpdateColorBtnVisual(Button colBtn, TextBlock hdrTb, string hex)
        {
            try
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                if (colBtn.Content is string)
                {
                    colBtn.Content = "■ " + hex;
                }
                else if (colBtn.Content is Panel p)
                {
                    foreach (var b in p.Children.OfType<Border>()) b.Background = brush;
                    foreach (var s in p.Children.OfType<Shape>()) s.Fill = brush;
                    foreach (var t in p.Children.OfType<TextBlock>())
                        if (t.Text != null && t.Text.Contains("#")) t.Text = hex;
                }
                colBtn.BorderBrush = brush;

                var hdrParent = LogicalTreeHelper.GetParent(hdrTb) as Panel;
                if (hdrParent != null)
                {
                    foreach (var s in hdrParent.Children.OfType<Shape>()) s.Fill = brush;
                    foreach (var b in hdrParent.Children.OfType<Border>())
                        if (b.Width <= 20 && b.Height <= 20) b.Background = brush;
                }
            }
            catch { }
        }

        public static void PatchFtakGeneratorCardToThreeButtons(Window win, List<DependencyObject> safeElements)
        {
            foreach (var tb in safeElements.OfType<TextBlock>())
            {
                string t = (tb.Text ?? "").Trim();
                if (t.Contains("GENERATOR PACZEK .FTAK", StringComparison.OrdinalIgnoreCase))
                {
                    DependencyObject p = LogicalTreeHelper.GetParent(tb) ?? VisualTreeHelper.GetParent(tb);
                    while (p != null && !(p is StackPanel || p is Grid)) p = LogicalTreeHelper.GetParent(p) ?? VisualTreeHelper.GetParent(p);
                    if (p is Panel cardPanel && cardPanel.Tag?.ToString() != "FTH_3BTN_OK")
                    {
                        cardPanel.Tag = "FTH_3BTN_OK";
                        tb.Text = "🎯 GENERATOR PACZEK .FTAK (FOLDER out\\<STRONA>\\ + WSPÓLNY FOLDER APK / PLUGINY / MAPY)";
                        var desc = GetLogicalElements(cardPanel);
                        foreach (var cb in desc.OfType<ComboBox>()) { cb.Visibility = Visibility.Collapsed; cb.Width = 0; cb.Height = 0; }
                        Button origBuildAll = null;
                        foreach (var b in desc.OfType<Button>())
                        {
                            if (ExtractButtonText(b).Contains("WSZYSTKIE", StringComparison.OrdinalIgnoreCase)) origBuildAll = b;
                            b.Visibility = Visibility.Collapsed; b.Height = 0; b.Margin = new Thickness(0);
                        }
                        var wrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 6) };
                        var b1 = new Button { Content = "⚡ Buduj wszystkie aktywne strony", Height = 38, MinWidth = 220, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 4, 10, 4), Background = new SolidColorBrush(Color.FromRgb(31, 224, 197)), Foreground = new SolidColorBrush(Color.FromRgb(5, 16, 20)), FontWeight = FontWeights.Bold, Cursor = Cursors.Hand };
                        b1.Click += (_, __) => { EnsureSharedFolderStructure(); origBuildAll?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };
                        var b2 = new Button { Content = "📂 Otwórz wspólny folder (APK / Pluginy / Mapy)", Height = 38, MinWidth = 220, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 4, 10, 4), Background = new SolidColorBrush(Color.FromRgb(42, 34, 12)), Foreground = new SolidColorBrush(Color.FromRgb(250, 208, 44)), BorderBrush = new SolidColorBrush(Color.FromRgb(250, 208, 44)), BorderThickness = new Thickness(1.5), FontWeight = FontWeights.Bold, Cursor = Cursors.Hand };
                        b2.Click += (_, __) => Process.Start(new ProcessStartInfo("explorer.exe", "\"" + EnsureSharedFolderStructure() + "\"") { UseShellExecute = true });
                        var b3 = new Button { Content = "📡 Dystrybucja przez HOTSPOT Wi-Fi", Height = 38, MinWidth = 220, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 4, 0, 4), Background = new SolidColorBrush(Color.FromRgb(14, 42, 54)), Foreground = new SolidColorBrush(Color.FromRgb(31, 224, 197)), BorderBrush = new SolidColorBrush(Color.FromRgb(31, 224, 197)), BorderThickness = new Thickness(1.5), FontWeight = FontWeights.Bold, Cursor = Cursors.Hand };
                        b3.Click += (_, __) => ShowHotspotDistributionModal(win);
                        wrap.Children.Add(b1); wrap.Children.Add(b2); wrap.Children.Add(b3);
                        cardPanel.Children.Add(wrap);
                    }
                }
            }
        }

        public static void GenerateAndOpenSafezonePosterHtml(Window win)
        {
            try
            {
                var snap = FactionLiveSyncBridge.CaptureCurrentFactionSnapshot(win, null);
                string qrData = Uri.EscapeDataString(snap.ToQrJson());
                string outDir = EnsureActiveFactionOutputFolder(win, snap.FactionName);
                string htmlPath = System.IO.Path.Combine(outDir, "Plakat_Safezone_" + SanitizeFolderName(snap.FactionName) + ".html");

                var rows = new StringBuilder();
                foreach (var vc in snap.VoiceChannels)
                    rows.AppendLine("<tr><td><b>" + WebUtility.HtmlEncode(vc.Role) + "</b></td><td>" + WebUtility.HtmlEncode(vc.Band) + "</td><td><b>" + WebUtility.HtmlEncode(vc.Freq) + "</b></td><td>" + WebUtility.HtmlEncode(vc.Tone) + "</td></tr>");

                string html = "<!DOCTYPE html><html><head><meta charset='utf-8'/><title>Plakat Safezone A4 — " + WebUtility.HtmlEncode(snap.FactionName) + "</title>" +
                    "<style>body{font-family:'Segoe UI',Arial,sans-serif;background:#071115;color:#fff;padding:18px;}.sheet{max-width:190mm;margin:0 auto;border:3px solid " + WebUtility.HtmlEncode(snap.ColorHex) + ";border-radius:12px;padding:18px;background:#0A181E;}table{width:100%;border-collapse:collapse;margin-top:12px;}th,td{border:1px solid #224A59;padding:7px;text-align:left;}th{background:#122C36;color:#1FE0C5;}</style></head><body>" +
                    "<div class='sheet'><h1>🛡️ STRONA: " + WebUtility.HtmlEncode(snap.FactionName) + "</h1>" +
                    "<p><b>Ważność paczki / QR:</b> " + WebUtility.HtmlEncode(snap.ExpirationLabel) + " &nbsp;|&nbsp; <b>Telefon ORGA / SOS:</b> " + WebUtility.HtmlEncode(snap.EmergencyPhone) + "</p>" +
                    "<div style='background:#fff;color:#000;padding:14px;border-radius:8px;width:230px;text-align:center;margin:12px auto;'><img src='https://api.qrserver.com/v1/create-qr-code/?size=210x210&data=" + qrData + "' width='210' height='210'/><br/><b>ZESKANUJ W FIELD TAK HUB</b></div>" +
                    "<p><b>LoRa Meshtastic:</b> " + WebUtility.HtmlEncode(snap.ChannelName) + " (" + WebUtility.HtmlEncode(snap.Region) + " / " + WebUtility.HtmlEncode(snap.ModemPreset) + ")</p>" +
                    "<table><thead><tr><th>Rola</th><th>Pasmo</th><th>Częstotliwość / Kanał</th><th>Ton / Kod</th></tr></thead><tbody>" + rows + "</tbody></table></div></body></html>";

                File.WriteAllText(htmlPath, html, Encoding.UTF8);
                Process.Start(new ProcessStartInfo(htmlPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Błąd generowania plakatu Safezone: " + ex.Message, "Field TAK Hub", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        public static void ShowHotspotDistributionModal(Window win)
        {
            var snap = FactionLiveSyncBridge.CaptureCurrentFactionSnapshot(win, null);
            string ip = "127.0.0.1";
            try
            {
                foreach (var a in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                    if (a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)) { ip = a.ToString(); break; }
            }
            catch { }
            string url = "http://" + ip + ":" + _hotspotPort + "/ftak.json";
            Clipboard.SetText(url);
            var res = MessageBox.Show(
                "Aktywny Hotspot Wi-Fi dla strony: " + snap.FactionName + "\nSkopiowano adres do schowka:\n" + url + "\n\nCzy otworzyć Plakat A4 Safezone z kodem QR?",
                "📡 Dystrybucja przez HOTSPOT Wi-Fi",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (res == MessageBoxResult.Yes)
                GenerateAndOpenSafezonePosterHtml(win);
        }

        public static string ExtractButtonText(Button btn)
        {
            if (btn == null) return "";
            if (btn.Content is string s) return s;
            if (btn.Content is TextBlock tb) return tb.Text ?? "";
            if (btn.Content is Panel p) return string.Join(" ", GetLogicalElements(p).OfType<TextBlock>().Select(x => x.Text));
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
        public string EmergencyPhone { get; set; } = "Brak / 112";
        public int ExpirationHours { get; set; } = 0;
        public string ExpirationLabel => ExpirationHours <= 0 ? "Bezterminowo (0)" : (ExpirationHours + " h");
        public List<FactionVoiceRowData> VoiceChannels { get; set; } = new List<FactionVoiceRowData>();

        public string ToQrJson() => "{\"Name\":\"" + FactionName + "\",\"ColorHex\":\"" + ColorHex + "\",\"ChannelName\":\"" + ChannelName + "\"}";
    }

    internal static class FactionLiveSyncBridge
    {
        public static List<DependencyObject> GetAllRootVisualsAcrossTabs(Window win)
        {
            var combined = new List<DependencyObject>(256);
            var visited = new HashSet<DependencyObject>();
            void AddRoot(DependencyObject r)
            {
                if (r == null || !visited.Add(r)) return;
                combined.AddRange(BuilderPackageAndUiPatcher.GetLogicalElements(r));
            }
            if (win != null)
            {
                AddRoot(win);
                foreach (var f in win.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    try { if (f.GetValue(win) is DependencyObject d) AddRoot(d); } catch { }
            }
            foreach (var t in typeof(BuilderPackageAndUiPatcher).Assembly.GetTypes())
            {
                if (t.Namespace == null || !t.Namespace.StartsWith("FieldTakHub.Builder")) continue;
                foreach (var sf in t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    try { if (typeof(DependencyObject).IsAssignableFrom(sf.FieldType) && sf.GetValue(null) is DependencyObject sd) AddRoot(sd); } catch { }
            }
            return combined;
        }

        public static List<FactionSnapshotData> ReadAllFactionsFromScreenAndMemory(Window win)
        {
            var fresh = ScanFactionsFromVisualsAndMemory(win ?? Application.Current?.MainWindow);
            if (fresh.Count > 0)
            {
                TacticalSharedState.CachedFactions.Clear();
                TacticalSharedState.CachedFactions.AddRange(fresh);
                return fresh;
            }
            return TacticalSharedState.CachedFactions.ToList();
        }

        public static List<FactionSnapshotData> ScanFactionsFromVisualsAndMemory(Window win)
        {
            var list = new List<FactionSnapshotData>();
            try
            {
                var all = GetAllRootVisualsAcrossTabs(win);
                var hdrs = all.OfType<TextBlock>()
                    .Where(t => (t.Text ?? "").Contains("Generator Stron —", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var hdr in hdrs)
                {
                    var card = BuilderPackageAndUiPatcher.FindCompleteFactionCardContainer(hdr);
                    if (card == null) continue;

                    var items = BuilderPackageAndUiPatcher.GetLogicalElements(card);
                    var boxes = items.OfType<TextBox>().ToList();
                    var snap = new FactionSnapshotData();
                    snap.FactionName = (boxes.Count > 0 && !string.IsNullOrWhiteSpace(boxes[0].Text))
                        ? boxes[0].Text.Trim()
                        : (hdr.Text ?? "").Replace("Generator Stron —", "").Trim();
                    if (string.IsNullOrWhiteSpace(snap.FactionName)) snap.FactionName = "ORGA";
                    snap.ChannelName = snap.FactionName.ToUpperInvariant() + "-MESH";

                    var colBtn = items.OfType<Button>().FirstOrDefault(b => Regex.IsMatch(BuilderPackageAndUiPatcher.ExtractButtonText(b), @"#[0-9A-Fa-f]{6}"));
                    if (colBtn != null)
                    {
                        var m = Regex.Match(BuilderPackageAndUiPatcher.ExtractButtonText(colBtn), @"#[0-9A-Fa-f]{6}");
                        if (m.Success) snap.ColorHex = m.Value.ToUpperInvariant();
                    }

                    foreach (var tb in boxes)
                    {
                        var p = LogicalTreeHelper.GetParent(tb) as Panel;
                        if (p != null && p.Children.OfType<TextBlock>().Any(l => (l.Text ?? "").Contains("Telefon alarmowy", StringComparison.OrdinalIgnoreCase)))
                        {
                            if (!string.IsNullOrWhiteSpace(tb.Text)) snap.EmergencyPhone = tb.Text.Trim();
                        }
                    }

                    // Znajdź wszystkie wiersze kanałów głosowych wewnątrz sekcji "Kanały Głosowe (PMR / VHF / DMR) & SOS"
                    foreach (var panel in items.OfType<Panel>())
                    {
                        var directInputs = panel.Children.Cast<UIElement>().Where(x => x is TextBox || x is ComboBox).ToList();
                        if (directInputs.Count >= 2 && directInputs.Any(x => x is TextBox))
                        {
                            bool isNameRow = panel.Children.OfType<StackPanel>().Any(sp => sp.Children.OfType<TextBlock>().Any(t => (t.Text ?? "").Contains("Nazwa strony", StringComparison.OrdinalIgnoreCase)));
                            if (isNameRow) continue;

                            string Val(UIElement e) => e is TextBox tb
                                ? (tb.Text ?? "").Trim()
                                : (e is ComboBox cb ? ((cb.SelectedItem is ComboBoxItem ci ? ci.Content?.ToString() : cb.SelectedItem?.ToString()) ?? cb.Text ?? "").Trim() : "");

                            var v = directInputs.Select(Val).ToList();
                            string r = v[0];
                            string b = v.Count >= 2 ? v[1] : "PMR";
                            string f = v.Count >= 3 ? v[2] : v[1];
                            string tn = v.Count >= 4 ? v[3] : "-";

                            if (r.Equals("PMR", StringComparison.OrdinalIgnoreCase) ||
                                r.Equals("VHF", StringComparison.OrdinalIgnoreCase) ||
                                r.Equals("UHF", StringComparison.OrdinalIgnoreCase) ||
                                r.Equals("DMR", StringComparison.OrdinalIgnoreCase) ||
                                r.Contains("VHF/", StringComparison.OrdinalIgnoreCase))
                            {
                                b = r;
                                r = v.Count >= 2 ? v[1] : "Kanał";
                                f = v.Count >= 3 ? v[2] : "-";
                            }

                            if (!string.IsNullOrWhiteSpace(r) || !string.IsNullOrWhiteSpace(f))
                            {
                                snap.VoiceChannels.Add(new FactionVoiceRowData
                                {
                                    Role = string.IsNullOrWhiteSpace(r) ? "Kanał" : r,
                                    Band = string.IsNullOrWhiteSpace(b) ? "PMR" : b,
                                    Freq = string.IsNullOrWhiteSpace(f) ? "-" : f,
                                    Tone = string.IsNullOrWhiteSpace(tn) ? "-" : tn
                                });
                            }
                        }
                    }
                    list.Add(snap);
                }
            }
            catch { }
            return list;
        }

        public static List<string> GetAllFactionNamesFromUi(Window win)
        {
            var snaps = ReadAllFactionsFromScreenAndMemory(win);
            var names = snaps.Select(s => s.FactionName)
                             .Where(n => !string.IsNullOrWhiteSpace(n) &&
                                         !n.Equals("PLI", StringComparison.OrdinalIgnoreCase) &&
                                         !n.Equals("GeoChat", StringComparison.OrdinalIgnoreCase))
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .ToList();
            if (names.Count == 0) { names.Add("ORGA"); names.Add("STRONA-2"); }
            return names;
        }

        public static FactionSnapshotData CaptureCurrentFactionSnapshot(Window win, string preferredFactionName)
        {
            var all = ReadAllFactionsFromScreenAndMemory(win);
            var snap = (!string.IsNullOrWhiteSpace(preferredFactionName)
                ? all.FirstOrDefault(x => string.Equals(x.FactionName, preferredFactionName.Trim(), StringComparison.OrdinalIgnoreCase))
                : null) ?? all.FirstOrDefault() ?? new FactionSnapshotData { FactionName = preferredFactionName ?? "ORGA" };

            if (snap.VoiceChannels.Count == 0)
                snap.VoiceChannels.Add(new FactionVoiceRowData());
            return snap;
        }
    }
}
