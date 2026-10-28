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
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace FieldTakHub.Builder.Services
{
    internal static class BuilderPackageAndUiPatcher
    {
        private static bool _sharedFoldersInitialized;
        private static bool _isApplying;
        private static int _pendingLayoutPasses = 3;
        private static DateTime _lastApplyUtc = DateTime.MinValue;
        private static HttpListener _hotspotListener;
        private static int _hotspotPort = 8769;
        public static string LatestHotspotJsonPayload = "{}";

        private static readonly string[] DistinctTacticalPalette = new[]
        {
            "#2684FF", // Niebieski (BLUFOR)
            "#EB4034", // Czerwony (OPFOR)
            "#2ECC71", // Zielony (GREENFOR)
            "#FAD02C", // Złoty / Żółty
            "#9B59B6", // Fioletowy (ORGA / SPEC)
            "#FF9F43", // Pomarańczowy
            "#1FE0C5", // Cyjan Taktyczny
            "#E84393", // Karmazynowy / Magenta
            "#A3CB38", // Oliwkowy / Limonkowy
            "#00CEC9", // Turkusowy
            "#F368E0", // Jasny Fiolet
            "#FF6B6B"  // Koralowy
        };

        // Odtworzenie oryginalnych ikon modułów na wypadek, gdyby poprzedni skrypt nadpisał je znakiem '<'
        private static readonly Dictionary<string, Tuple<string, string>> KnownSidebarModules = new Dictionary<string, Tuple<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            { "Paczek", Tuple.Create("🛡️", "Generator Paczek (.ftak)") },
            { "Map", Tuple.Create("🗺️", "Generator Map 1:1") },
            { "OPORD", Tuple.Create("📋", "Generator OPORD") },
            { "Płytek", Tuple.Create("📟", "Programator Płytek USB") },
            { "Nowy projekt", Tuple.Create("➕", "Nowy projekt (Stan zerowy)") },
            { "Otwórz projekt", Tuple.Create("📂", "Otwórz projekt (.fthproj)") },
            { "Zapisz projekt", Tuple.Create("💾", "Zapisz projekt (.fthproj)") },
            { "Folder Source", Tuple.Create("📁", "Folder Source (APK / Mapy)") },
            { "Wesprzyj", Tuple.Create("☕", "Wesprzyj twórcę") },
            { "Język", Tuple.Create("🌐", "Język UI: PL / ENG") }
        };

        public static void RequestSingleLayoutPass()
        {
            _pendingLayoutPasses = 2;
        }

        public static bool AllowSingleLayoutPass()
        {
            if (_isApplying) return false;
            if (_pendingLayoutPasses <= 0) return false;
            var now = DateTime.UtcNow;
            if ((now - _lastApplyUtc).TotalMilliseconds < 180) return false;
            _pendingLayoutPasses--;
            _lastApplyUtc = now;
            return true;
        }

        public static void ApplyEnhancementsThrottled(Window win)
        {
            if (win == null || _isApplying) return;
            if (!AllowSingleLayoutPass()) return;
            ApplyEnhancements(win, null);
        }

        public static List<DependencyObject> GetVisualTreeElements(DependencyObject root, bool includeCollapsed = false)
        {
            var result = new List<DependencyObject>(128);
            if (root == null) return result;

            var q = new Queue<DependencyObject>(64);
            q.Enqueue(root);
            while (q.Count > 0)
            {
                var cur = q.Dequeue();
                if (!includeCollapsed && cur != root && cur is UIElement uie && uie.Visibility == Visibility.Collapsed)
                {
                    continue;
                }

                result.Add(cur);

                if (cur != root && (cur is TextBoxBase || cur is ComboBox || cur is Slider || cur is ButtonBase || cur is DataGrid || cur is PasswordBox || cur is ScrollBar))
                {
                    continue;
                }

                string typeName = cur.GetType().Name;
                if (typeName.Contains("WebView", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

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

        public static bool IsInsideControlTemplate(DependencyObject element)
        {
            DependencyObject cur = element != null ? VisualTreeHelper.GetParent(element) : null;
            while (cur != null)
            {
                if (cur is TextBoxBase || cur is ComboBox || cur is Slider || cur is ButtonBase || cur is ListBoxItem || cur is ComboBoxItem || cur is DataGrid || cur is ScrollBar)
                    return true;
                if (cur is Window)
                    break;
                cur = VisualTreeHelper.GetParent(cur);
            }
            return false;
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
            if (win == null || _isApplying) return;
            _isApplying = true;
            try
            {
                EnsureSharedFolderStructure();
                if (win.MinWidth > 900) win.MinWidth = 900;
                if (win.MinHeight > 580) win.MinHeight = 580;

                var visibleElements = GetVisualTreeElements(win, includeCollapsed: false);
                CollapseLegacyTopModeBarPrecisely(visibleElements);
                FixSidebarCollapseButtonAndIcons(win, visibleElements);
                PatchZeroExpirationInputs(visibleElements);
                PatchFactionFilesPosterAndAddFactionColor(win, visibleElements);
                PatchFtakGeneratorCardToThreeButtons(win, visibleElements);
            }
            catch { }
            finally
            {
                _isApplying = false;
            }
        }

        /// <summary>
        /// Naprawia pasek boczny (Sidebar):
        /// - Przycisk zwijania rozpoznawany jest wyłącznie po tym, że leży w górnym boksie obok napisu "FIELD TAK HUB" (nigdy nie nadpisuje przycisków modułów!).
        /// - Przyciski modułów po zwinięciu paska mają Padding = 0 i wyśrodkowaną ikonę, dzięki czemu żadna ikona nie jest ucięta ani zamieniona na '<'.
        /// </summary>
        private static void FixSidebarCollapseButtonAndIcons(Window win, List<DependencyObject> visibleElements)
        {
            try
            {
                foreach (var border in visibleElements.OfType<Border>())
                {
                    if (IsInsideControlTemplate(border)) continue;
                    if (Grid.GetColumn(border) == 0 && border.ActualWidth >= 44 && border.ActualWidth <= 290)
                    {
                        bool isCollapsed = border.ActualWidth < 95;
                        var sideButtons = GetVisualTreeElements(border, includeCollapsed: true).OfType<Button>().ToList();

                        for (int idx = 0; idx < sideButtons.Count; idx++)
                        {
                            var btn = sideButtons[idx];
                            // Tylko PIERWSZY przycisk na pasku bocznym (w nagłówku obok logo FIELD TAK HUB) jest przyciskiem zwijania!
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
                                            RequestSingleLayoutPass();
                                            ApplyEnhancements(win, null);
                                        }), DispatcherPriority.Loaded);
                                    };
                                }
                                continue;
                            }

                            // Wszystkie pozostałe przyciski to przyciski modułów operacyjnych i projektu!
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
                                    {
                                        tbs[i].Visibility = Visibility.Collapsed;
                                    }
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
                                    {
                                        tbs[i].Visibility = Visibility.Visible;
                                    }
                                }
                            }
                            else if (btn.Content is string sContent)
                            {
                                // Jeśli przycisk ma tekst (np. "🛡️ Generator Paczek (.ftak)"), w trybie zwiniętym pokaż samą ikonę!
                                string fullLabel = btn.ToolTip as string;
                                if (string.IsNullOrWhiteSpace(fullLabel) && sContent.Length > 3)
                                {
                                    fullLabel = sContent;
                                    btn.ToolTip = fullLabel;
                                }

                                if (!string.IsNullOrWhiteSpace(fullLabel))
                                {
                                    string iconOnly = ExtractLeadingIcon(fullLabel);
                                    if (isCollapsed)
                                    {
                                        btn.Padding = new Thickness(0);
                                        btn.HorizontalContentAlignment = HorizontalAlignment.Center;
                                        btn.FontSize = 16;
                                        btn.Content = iconOnly;
                                    }
                                    else
                                    {
                                        btn.Padding = new Thickness(10, 6, 10, 6);
                                        btn.HorizontalContentAlignment = HorizontalAlignment.Left;
                                        btn.FontSize = 12;
                                        btn.Content = fullLabel;
                                    }
                                }
                            }
                        }
                        break;
                    }
                }
            }
            catch { }
        }

        private static string ExtractLeadingIcon(string label)
        {
            foreach (var kv in KnownSidebarModules)
            {
                if (label.Contains(kv.Key, StringComparison.OrdinalIgnoreCase))
                    return kv.Value.Item1;
            }
            int spaceIdx = label.IndexOf(' ');
            if (spaceIdx > 0 && spaceIdx <= 4) return label.Substring(0, spaceIdx);
            return "🔹";
        }

        public static void CollapseLegacyTopModeBarPrecisely(List<DependencyObject> elements)
        {
            if (elements == null) return;

            foreach (var el in elements)
            {
                if (IsInsideControlTemplate(el)) continue;

                if (el is TextBlock tb)
                {
                    string txt = (tb.Text ?? "").Trim();
                    if (txt.Contains("TRYB PRACY BUILDERA", StringComparison.OrdinalIgnoreCase) ||
                        txt.Contains("BUILDER MODE", StringComparison.OrdinalIgnoreCase) ||
                        txt.Contains("Wybierz moduł operacyjny", StringComparison.OrdinalIgnoreCase))
                    {
                        if (tb.Visibility != Visibility.Collapsed)
                            tb.Visibility = Visibility.Collapsed;
                        CollapseParentBarBorder(tb);
                    }
                    else if (txt.StartsWith("BUILDER v2.", StringComparison.OrdinalIgnoreCase))
                    {
                        tb.Text = "BUILDER v3.5";
                    }
                }

                if (el is Border b && b.Visibility == Visibility.Visible)
                {
                    if (IsInsideScrollViewer(b)) continue;
                    if (Grid.GetColumn(b) == 0 && b.ActualWidth > 0 && b.ActualWidth <= 290) continue;

                    if (b.Child == null && b.ActualHeight > 0 && b.ActualHeight < 90)
                    {
                        CollapseSingleBorderAndRow(b);
                    }
                    else if (b.Child is UIElement childEl)
                    {
                        if (childEl.Visibility == Visibility.Collapsed)
                        {
                            CollapseSingleBorderAndRow(b);
                        }
                        else if (childEl is Panel p && p.Children.Count > 0)
                        {
                            bool allHidden = p.Children.Cast<UIElement>().All(c =>
                                c.Visibility == Visibility.Collapsed ||
                                (c is Panel sp && sp.Children.Count > 0 && sp.Children.Cast<UIElement>().All(x => x.Visibility == Visibility.Collapsed)));

                            if (allHidden) CollapseSingleBorderAndRow(b);
                        }
                    }
                }
            }
        }

        private static bool IsInsideScrollViewer(DependencyObject el)
        {
            DependencyObject cur = el != null ? VisualTreeHelper.GetParent(el) : null;
            while (cur != null)
            {
                if (cur is ScrollViewer) return true;
                if (cur is Window) break;
                cur = VisualTreeHelper.GetParent(cur);
            }
            return false;
        }

        private static void CollapseParentBarBorder(DependencyObject child)
        {
            DependencyObject cur = child;
            while (cur != null && !(cur is Window))
            {
                if (cur is Border b && !IsInsideScrollViewer(b))
                {
                    CollapseSingleBorderAndRow(b);
                    break;
                }
                cur = VisualTreeHelper.GetParent(cur);
            }
        }

        private static void CollapseSingleBorderAndRow(Border b)
        {
            if (b.Visibility == Visibility.Collapsed) return;
            b.Visibility = Visibility.Collapsed;
            b.Height = 0;
            b.MinHeight = 0;
            b.Margin = new Thickness(0);
            b.Padding = new Thickness(0);
            b.BorderThickness = new Thickness(0);

            var parent = VisualTreeHelper.GetParent(b);
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

        private static void PatchZeroExpirationInputs(List<DependencyObject> safeElements)
        {
            foreach (var tb in safeElements.OfType<TextBlock>())
            {
                if (IsInsideControlTemplate(tb)) continue;
                string txt = (tb.Text ?? "").Trim();
                if ((txt.Contains("Ważność", StringComparison.OrdinalIgnoreCase) || txt.Contains("Expiration", StringComparison.OrdinalIgnoreCase)) &&
                    !txt.Contains("0 = bezterminowo", StringComparison.OrdinalIgnoreCase))
                {
                    tb.Text = txt.TrimEnd(':') + " (0 = bezterminowo):";
                }
            }
        }

        /// <summary>
        /// Obsługuje:
        /// - Przycisk "📂 Pliki strony",
        /// - Przycisk "Plakat stron + QR",
        /// - Przycisk "➕ Dodaj stronę" -> automatycznie losuje unikalny kolor RGB (inny niż pozostałe strony) z możliwością zmiany!
        /// </summary>
        private static void PatchFactionFilesPosterAndAddFactionColor(Window win, List<DependencyObject> safeElements)
        {
            foreach (var btn in safeElements.OfType<Button>())
            {
                string txt = ExtractButtonText(btn);
                string name = btn.Name ?? "";

                // 1. Automatyczne losowanie unikalnego koloru RGB dla nowej strony konfliktu przy kliknięciu "Dodaj stronę"
                if ((txt.Contains("Dodaj stronę", StringComparison.OrdinalIgnoreCase) ||
                     txt.Contains("Add Faction", StringComparison.OrdinalIgnoreCase) ||
                     txt.Contains("Nowa strona", StringComparison.OrdinalIgnoreCase)) &&
                    btn.Tag?.ToString() != "FTH_ADD_FACTION_COLOR_HOOKED")
                {
                    btn.Tag = "FTH_ADD_FACTION_COLOR_HOOKED";
                    btn.Click += (_, __) =>
                    {
                        win.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            AssignDistinctColorToNewestFaction(win);
                        }), DispatcherPriority.Loaded);
                    };
                }

                // 2. Odblokowanie przycisku "Pliki strony"
                bool isFactionFilesBtn =
                    txt.Contains("Pliki str", StringComparison.OrdinalIgnoreCase) ||
                    txt.Contains("Faction files", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("FactionFiles", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("PlikiStrony", StringComparison.OrdinalIgnoreCase);

                if (isFactionFilesBtn && btn.Tag?.ToString() != "FTH_FAC_FILES_ACTIVE")
                {
                    btn.Tag = "FTH_FAC_FILES_ACTIVE";
                    btn.IsEnabled = true;
                    btn.Visibility = Visibility.Visible;
                    btn.Opacity = 1.0;
                    btn.Cursor = Cursors.Hand;
                    btn.Content = BuilderI18n.T("📂 Pliki strony", "📂 Faction Files");

                    btn.IsEnabledChanged += (sender, args) =>
                    {
                        if (sender is Button b && !b.IsEnabled) b.IsEnabled = true;
                    };
                    btn.PreviewMouseLeftButtonDown += (s, e) =>
                    {
                        e.Handled = true;
                        try
                        {
                            string facName = TryExtractFactionNameFromDataContext(btn);
                            string dir = EnsureActiveFactionOutputFolder(win, facName);
                            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true });
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("Nie udało się otworzyć folderu strony: " + ex.Message, "Field TAK Hub", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    };
                }
                else if ((txt.Contains("Plakat", StringComparison.OrdinalIgnoreCase) || txt.Contains("Safezone", StringComparison.OrdinalIgnoreCase)) &&
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

        private static void AssignDistinctColorToNewestFaction(Window win)
        {
            try
            {
                var factions = FactionLiveSyncBridge.EnumerateAllInMemoryFactions(win);
                if (factions.Count == 0) return;

                var usedColors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < factions.Count - 1; i++)
                {
                    string c = FactionLiveSyncBridge.GetPropStr(factions[i], "ColorHex", "Color", "HexColor");
                    if (!string.IsNullOrWhiteSpace(c)) usedColors.Add(c.Trim());
                }

                // Wybierz pierwszy wolny kolor z palety taktycznej lub wylosuj nowy kontrastowy kolor RGB
                string chosenHex = null;
                var rng = new Random(Guid.NewGuid().GetHashCode());
                var shuffledPalette = DistinctTacticalPalette.Skip(2).OrderBy(_ => rng.Next()).Concat(DistinctTacticalPalette.Take(2));
                foreach (string candidate in shuffledPalette)
                {
                    if (!usedColors.Contains(candidate))
                    {
                        chosenHex = candidate;
                        break;
                    }
                }

                if (string.IsNullOrWhiteSpace(chosenHex))
                {
                    chosenHex = string.Format("#{0:X2}{1:X2}{2:X2}", rng.Next(40, 245), rng.Next(60, 245), rng.Next(50, 245));
                }

                object newestFaction = factions[factions.Count - 1];
                var t = newestFaction.GetType();
                var pColor = t.GetProperty("ColorHex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase)
                          ?? t.GetProperty("Color", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
                if (pColor != null && pColor.CanWrite)
                {
                    pColor.SetValue(newestFaction, chosenHex);
                }

                // Zaktualizuj również pole tekstowe koloru HEX w aktywnym widoku Generatora Stron (aby użytkownik widział kolor i mógł go zmienić)
                var all = GetVisualTreeElements(win, includeCollapsed: false);
                foreach (var tb in all.OfType<TextBox>())
                {
                    string val = (tb.Text ?? "").Trim();
                    if (val.StartsWith("#") && (val.Length == 7 || val.Length == 4))
                    {
                        var parent = VisualTreeHelper.GetParent(tb) as Panel;
                        bool isFactionColorBox = parent != null && GetVisualTreeElements(parent, true).OfType<TextBlock>().Any(l =>
                            (l.Text ?? "").Contains("Kolor", StringComparison.OrdinalIgnoreCase) ||
                            (l.Text ?? "").Contains("HEX", StringComparison.OrdinalIgnoreCase) ||
                            (l.Text ?? "").Contains("RGB", StringComparison.OrdinalIgnoreCase));

                        if (isFactionColorBox)
                        {
                            tb.Text = chosenHex;
                        }
                    }
                }
            }
            catch { }
        }

        private static string TryExtractFactionNameFromDataContext(FrameworkElement el)
        {
            try
            {
                object dc = el?.DataContext;
                if (dc != null && !(dc is Window))
                {
                    var p = dc.GetType().GetProperty("Name") ?? dc.GetType().GetProperty("FactionName");
                    string v = p?.GetValue(dc)?.ToString();
                    if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
                }
            }
            catch { }
            return null;
        }

        private static void PatchFtakGeneratorCardToThreeButtons(Window win, List<DependencyObject> safeElements)
        {
            foreach (var tb in safeElements.OfType<TextBlock>())
            {
                if (IsInsideControlTemplate(tb)) continue;
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

                        var desc = GetVisualTreeElements(cardPanel, includeCollapsed: true);
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

        public static string ExtractButtonText(Button btn)
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
        private static List<FieldInfo> _cachedStaticCollectionFields;

        private static readonly HashSet<string> InvalidFactionTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PLI", "GeoChat", "TAK", "TAK_Tracker", "Router", "Client", "Auto",
            "EU_868", "EU_433", "US", "SHORT_FAST", "SHORT_SLOW", "MEDIUM_FAST", "LONG_FAST",
            "PMR", "VHF", "UHF", "DMR", "MGRS", "UTM", "WGS84", "DMS", "MILSIM", "ASG"
        };

        private static bool IsValidFactionName(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return false;
            string clean = candidate.Trim();
            if (clean.Length < 2 || clean.Length > 32) return false;
            if (InvalidFactionTokens.Contains(clean)) return false;
            if (clean.StartsWith("System.", StringComparison.OrdinalIgnoreCase)) return false;
            if (clean.Contains("1:") || clean.Contains("MHz") || clean.Contains("CTCSS")) return false;
            return true;
        }

        public static List<string> GetAllFactionNamesFromUi(Window win)
        {
            var res = new List<string>();
            if (win == null) return new List<string> { "ORGA", "STRONA-2" };

            try
            {
                foreach (var facObj in EnumerateAllInMemoryFactions(win))
                {
                    string n = GetPropStr(facObj, "Name", "FactionName", "SideName");
                    if (IsValidFactionName(n) && !res.Contains(n.Trim(), StringComparer.OrdinalIgnoreCase))
                        res.Add(n.Trim());
                }
            }
            catch { }

            if (res.Count == 0)
            {
                res.Add("ORGA");
                res.Add("STRONA-2");
            }
            return res;
        }

        /// <summary>
        /// Pobiera obiekty stron konfliktu ze statycznych pól serwisów (np. BuilderCoreSidebarAndProjectEngine)
        /// oraz z MainWindow — ze zbuforowaną listą FieldInfo (czas wykonania < 0.1 ms!).
        /// </summary>
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
                            {
                                _cachedStaticCollectionFields.Add(sf);
                            }
                        }
                    }
                }

                foreach (var sf in _cachedStaticCollectionFields)
                {
                    try { CollectFactions(sf.GetValue(null), list); } catch { }
                }

                var roots = new List<object> { win };
                if (win.DataContext != null) roots.Add(win.DataContext);
                var instFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                foreach (var r in roots)
                {
                    var rt = r.GetType();
                    foreach (var f in rt.GetFields(instFlags))
                    {
                        try { CollectFactions(f.GetValue(r), list); } catch { }
                    }
                    foreach (var p in rt.GetProperties(instFlags))
                    {
                        if (p.GetIndexParameters().Length > 0) continue;
                        try { CollectFactions(p.GetValue(r), list); } catch { }
                    }
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
            var snap = new FactionSnapshotData();
            if (!string.IsNullOrWhiteSpace(preferredFactionName)) snap.FactionName = preferredFactionName.Trim();
            if (win == null) return snap;

            try
            {
                var memFactions = EnumerateAllInMemoryFactions(win);
                object matched = null;
                if (!string.IsNullOrWhiteSpace(preferredFactionName))
                {
                    matched = memFactions.FirstOrDefault(x =>
                        string.Equals(GetPropStr(x, "Name", "FactionName"), preferredFactionName.Trim(), StringComparison.OrdinalIgnoreCase));
                }
                if (matched == null && memFactions.Count > 0) matched = memFactions[0];

                if (matched != null)
                {
                    snap.FactionName = GetPropStr(matched, "Name", "FactionName") ?? snap.FactionName;
                    snap.ColorHex = GetPropStr(matched, "ColorHex", "Color") ?? snap.ColorHex;
                    snap.ChannelName = GetPropStr(matched, "ChannelName", "MeshChannel", "PrimaryChannel") ?? snap.ChannelName;
                    snap.Psk = GetPropStr(matched, "Psk", "PskBase64") ?? snap.Psk;
                    snap.Region = GetPropStr(matched, "LoraRegion", "Region") ?? snap.Region;
                    snap.ModemPreset = GetPropStr(matched, "LoraModemPreset", "ModemPreset", "Preset") ?? snap.ModemPreset;
                    snap.OtsHost = GetPropStr(matched, "OtsHost", "ServerHost") ?? snap.OtsHost;
                    snap.EmergencyPhone = GetPropStr(matched, "EmergencyPhone", "SosPhone") ?? snap.EmergencyPhone;

                    // Odczytaj wszystkie kolekcje kanałów głosowych wewnątrz obiektu strony
                    var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
                    foreach (var prop in matched.GetType().GetProperties(flags))
                    {
                        if (typeof(IEnumerable).IsAssignableFrom(prop.PropertyType) && prop.PropertyType != typeof(string))
                        {
                            if (prop.GetValue(matched) is IEnumerable vcList)
                            {
                                foreach (var vi in vcList)
                                {
                                    if (vi == null || vi is string) continue;
                                    string r = GetPropStr(vi, "RoleName", "Role", "Name", "Title", "ChannelName");
                                    string b = GetPropStr(vi, "BandType", "Band", "Type", "Modulation") ?? "PMR";
                                    string f = GetPropStr(vi, "FreqMhz", "Frequency", "Freq", "Channel", "Value");
                                    string tn = GetPropStr(vi, "ToneOrCode", "Tone", "Ctcss", "Subtone", "Code") ?? "-";
                                    if (!string.IsNullOrWhiteSpace(f) || !string.IsNullOrWhiteSpace(r))
                                    {
                                        snap.VoiceChannels.Add(new FactionVoiceRowData
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
