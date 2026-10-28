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
        private static readonly string[] DistinctRgbPalette = new[]
        {
            "#2684FF", "#EB4034", "#2ECC71", "#FAD02C",
            "#9B59B6", "#FF9F43", "#1FE0C5", "#E84393",
            "#A3CB38", "#00CEC9", "#FF6B6B", "#54A0FF",
            "#5F27CD", "#10AC84", "#F368E0", "#FF9FF3"
        };

        private static readonly HashSet<string> _usedColors = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "#2684FF" };
        internal static List<FactionSnapshotData> CachedFactions { get; } = new List<FactionSnapshotData>();
        public static Action<Window> OnOpordTabActivated;

        public static string GetNextUniqueColorHex()
        {
            foreach (var f in CachedFactions)
                if (!string.IsNullOrWhiteSpace(f.ColorHex)) _usedColors.Add(f.ColorHex.Trim());

            var rng = new Random(Guid.NewGuid().GetHashCode());
            var free = DistinctRgbPalette.Where(c => !_usedColors.Contains(c)).OrderBy(_ => rng.Next()).ToList();
            string chosen = free.Count > 0
                ? free[0]
                : string.Format("#{0:X2}{1:X2}{2:X2}", rng.Next(45, 240), rng.Next(55, 240), rng.Next(45, 240));

            _usedColors.Add(chosen);
            return chosen;
        }

        public static void RegisterUsedColor(string hex)
        {
            if (!string.IsNullOrWhiteSpace(hex)) _usedColors.Add(hex.Trim());
        }

        public static void CaptureLiveStateFromWindow(Window win)
        {
            try
            {
                var fresh = FactionLiveSyncBridge.ScanFactionsFromVisualsAndMemory(win ?? Application.Current?.MainWindow);
                if (fresh.Count > 0)
                {
                    CachedFactions.Clear();
                    CachedFactions.AddRange(fresh);
                }
            }
            catch { }
        }

        public static void NotifyModuleSwitched(Window win, string modeKey)
        {
            CaptureLiveStateFromWindow(win);
            if (!string.IsNullOrWhiteSpace(modeKey) && modeKey.Contains("OPORD", StringComparison.OrdinalIgnoreCase))
            {
                win?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    CaptureLiveStateFromWindow(win);
                    OnOpordTabActivated?.Invoke(win);
                }), DispatcherPriority.Loaded);
            }
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
            if (win == null) return;
            EnsureSharedFolderStructure();
            try
            {
                var all = FactionLiveSyncBridge.GetAllRootVisualsAcrossTabs(win);
                CollapseLegacyTopModeBarPrecisely(all);
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
                    for (int i = 0; i < 5 && cur != null && !(cur is Window); i++)
                    {
                        if (cur is Border b)
                        {
                            b.Visibility = Visibility.Collapsed;
                            b.Height = 0;
                            b.MinHeight = 0;
                            b.Margin = new Thickness(0);
                            b.Padding = new Thickness(0);
                            b.BorderThickness = new Thickness(0);
                        }
                        cur = LogicalTreeHelper.GetParent(cur) ?? VisualTreeHelper.GetParent(cur);
                    }
                }
            }
        }

        public static void HookFactionCardsAndAddButtons(Window win, List<DependencyObject> elements)
        {
            if (elements == null) return;
            EnsureDistinctCardColors(elements);

            foreach (var btn in elements.OfType<Button>())
            {
                string txt = ExtractButtonText(btn).Trim();
                if ((txt == "+" || txt.Contains("Dodaj kanał", StringComparison.OrdinalIgnoreCase)) && btn.Tag?.ToString() != "FTH_PLUS_HOOK")
                {
                    btn.Tag = "FTH_PLUS_HOOK";
                    btn.Click += (_, __) =>
                    {
                        win?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            var cur = FactionLiveSyncBridge.GetAllRootVisualsAcrossTabs(win);
                            EnsureDistinctCardColors(cur);
                            HookFactionCardsAndAddButtons(win, cur);
                            TacticalSharedState.CaptureLiveStateFromWindow(win);
                        }), DispatcherPriority.Loaded);
                    };
                }
            }
        }

        private static void EnsureDistinctCardColors(List<DependencyObject> elements)
        {
            var seenColors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hdrs = elements.OfType<TextBlock>().Where(t => (t.Text ?? "").Contains("Generator Stron —", StringComparison.OrdinalIgnoreCase)).ToList();

            for (int i = 0; i < hdrs.Count; i++)
            {
                var hdr = hdrs[i];
                DependencyObject card = LogicalTreeHelper.GetParent(hdr) ?? VisualTreeHelper.GetParent(hdr);
                while (card != null && !(card is Border b && b.BorderThickness.Top >= 1))
                    card = LogicalTreeHelper.GetParent(card) ?? VisualTreeHelper.GetParent(card);
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
                        nameBox.Text = "STRONA-" + num;
                        hdr.Text = "Generator Stron — STRONA-" + num;
                        n = nameBox.Text;
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
                            UpdateColorBtn(colBtn, hex);
                        }
                        seenColors.Add(hex);
                        TacticalSharedState.RegisterUsedColor(hex);
                    }
                }
            }
        }

        private static void UpdateColorBtn(Button colBtn, string hex)
        {
            try
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                if (colBtn.Content is string) colBtn.Content = "■ " + hex;
                else if (colBtn.Content is Panel p)
                {
                    foreach (var b in p.Children.OfType<Border>()) b.Background = brush;
                    foreach (var s in p.Children.OfType<Shape>()) s.Fill = brush;
                    foreach (var t in p.Children.OfType<TextBlock>()) if (t.Text != null && t.Text.Contains("#")) t.Text = hex;
                }
                colBtn.BorderBrush = brush;
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

        public static void ShowHotspotDistributionModal(Window win)
        {
            var snap = FactionLiveSyncBridge.CaptureCurrentFactionSnapshot(win, null);
            string url = "http://127.0.0.1:" + _hotspotPort + "/ftak.json";
            Clipboard.SetText(url);
            MessageBox.Show("Aktywny Hotspot Wi-Fi dla strony: " + snap.FactionName + "\nSkopiowano adres:\n" + url, "Hotspot Wi-Fi", MessageBoxButton.OK, MessageBoxImage.Information);
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
                var hdrs = all.OfType<TextBlock>().Where(t => (t.Text ?? "").Contains("Generator Stron —", StringComparison.OrdinalIgnoreCase)).ToList();

                foreach (var hdr in hdrs)
                {
                    DependencyObject card = LogicalTreeHelper.GetParent(hdr) ?? VisualTreeHelper.GetParent(hdr);
                    while (card != null && !(card is Border b && b.BorderThickness.Top >= 1))
                        card = LogicalTreeHelper.GetParent(card) ?? VisualTreeHelper.GetParent(card);
                    if (card == null) continue;

                    var items = BuilderPackageAndUiPatcher.GetLogicalElements(card);
                    var boxes = items.OfType<TextBox>().ToList();
                    var snap = new FactionSnapshotData();
                    snap.FactionName = (boxes.Count > 0 && !string.IsNullOrWhiteSpace(boxes[0].Text))
                        ? boxes[0].Text.Trim()
                        : (hdr.Text ?? "").Replace("Generator Stron —", "").Trim();
                    if (string.IsNullOrWhiteSpace(snap.FactionName)) snap.FactionName = "ORGA";
                    snap.ChannelName = snap.FactionName.ToUpperInvariant() + "-MESH";

                    foreach (var g in items.OfType<Grid>())
                    {
                        if (g.ColumnDefinitions.Count >= 3)
                        {
                            var inputs = g.Children.Cast<UIElement>().Where(x => x is TextBox || x is ComboBox).ToList();
                            if (inputs.Count >= 2 && inputs.Any(x => x is TextBox))
                            {
                                bool isNameRow = g.Children.OfType<StackPanel>().Any(sp => sp.Children.OfType<TextBlock>().Any(t => (t.Text ?? "").Contains("Nazwa strony", StringComparison.OrdinalIgnoreCase)));
                                if (isNameRow) continue;

                                string Val(UIElement e) => e is TextBox tb ? (tb.Text ?? "").Trim() : (e is ComboBox cb ? ((cb.SelectedItem is ComboBoxItem ci ? ci.Content?.ToString() : cb.SelectedItem?.ToString()) ?? cb.Text ?? "").Trim() : "");
                                var v = inputs.Select(Val).ToList();
                                string r = v[0], b = v.Count >= 2 ? v[1] : "PMR", f = v.Count >= 3 ? v[2] : v[1], tn = v.Count >= 4 ? v[3] : "-";
                                if (r.Equals("PMR", StringComparison.OrdinalIgnoreCase) || r.Equals("VHF", StringComparison.OrdinalIgnoreCase) || r.Equals("UHF", StringComparison.OrdinalIgnoreCase) || r.Equals("DMR", StringComparison.OrdinalIgnoreCase))
                                { b = r; r = v.Count >= 2 ? v[1] : "Kanał"; f = v.Count >= 3 ? v[2] : "-"; }
                                if (!string.IsNullOrWhiteSpace(r) || !string.IsNullOrWhiteSpace(f))
                                    snap.VoiceChannels.Add(new FactionVoiceRowData { Role = r, Band = b, Freq = f, Tone = tn });
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
            var names = snaps.Select(s => s.FactionName).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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
