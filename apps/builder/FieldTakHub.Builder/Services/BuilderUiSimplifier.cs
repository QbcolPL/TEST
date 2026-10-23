#pragma warning disable CS0414, CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace FieldTakHub.Builder.Services
{
    internal static class BuilderUiSimplifier
    {
        private static readonly HashSet<int> AttachedWindows = new HashSet<int>();
        private static bool _isApplyingSimplification;

        public static void Attach(Window win)
        {
            if (win == null) return;
            int hash = win.GetHashCode();
            if (!AttachedWindows.Add(hash))
            {
                EnforceSimplification(win);
                return;
            }

            win.Loaded += (_, __) => EnforceSimplification(win);
            win.ContentRendered += (_, __) => EnforceSimplification(win);
            win.LayoutUpdated += (_, __) =>
            {
                if (!_isApplyingSimplification)
                {
                    EnforceSimplification(win);
                }
            };

            EnforceSimplification(win);
            _ = win.Dispatcher.BeginInvoke(new Action(() => EnforceSimplification(win)), DispatcherPriority.Loaded);
            _ = win.Dispatcher.BeginInvoke(new Action(() => EnforceSimplification(win)), DispatcherPriority.Background);
        }

        public static void EnforceSimplification(Window win)
        {
            if (win == null || _isApplyingSimplification) return;
            _isApplyingSimplification = true;
            try
            {
                var allElements = GetAllVisualDescendants(win).ToList();

                // 1. Znajdź główny panel Generatora Stron Konfliktu (TacticalConfigPanel) i wymuś jego widoczność
                //    zarówno w Trybie Prostym, jak i w Trybie Zaawansowanym
                FrameworkElement factionGeneratorRoot = null;
                foreach (var fe in allElements.OfType<FrameworkElement>())
                {
                    if (fe is Border || fe is StackPanel || fe is Grid)
                    {
                        var texts = GetElementTexts(fe);
                        if (texts.Any(t => t.Contains("GENERATOR STRON", StringComparison.OrdinalIgnoreCase) ||
                                           t.Contains("STRONY KONFLIKTU", StringComparison.OrdinalIgnoreCase) ||
                                           t.Contains("Dodaj stronę", StringComparison.OrdinalIgnoreCase) ||
                                           t.Contains("Plakat A4 Safezone", StringComparison.OrdinalIgnoreCase)))
                        {
                            if (factionGeneratorRoot == null || IsAncestorOf(fe, factionGeneratorRoot))
                            {
                                // Wybierz bezpośrednią kartę sekcji generatora stron (nie cały ekran okna)
                                if (!texts.Any(t => t.Contains("Generator Map Taktycznych 1:1", StringComparison.OrdinalIgnoreCase)))
                                {
                                    factionGeneratorRoot = fe;
                                }
                            }
                        }
                    }
                }

                if (factionGeneratorRoot != null)
                {
                    EnsureVisibleUpToBuilderRoot(factionGeneratorRoot);
                }

                // 2. Ukryj całkowicie sekcję "3. Dodatkowe opcje" (nagłówek + pola Package ID, Wersja, Publisher, Max downloads, notatki)
                CollapseSection3AdditionalOptions(allElements, factionGeneratorRoot);

                // 3. Ukryj całkowicie stary przycisk "Generuj paczkę .FTAK" + przycisk rozwijany "v" / "Buduj paczkę" (poza generatorem stron)
                CollapseLegacyBuildButtons(allElements, factionGeneratorRoot);

                // 4. Podepnij przyciski "Tryb prosty" i "Tryb zaawansowany", aby nigdy nie przywracały sekcji 3 ani starego przycisku
                HookModeToggleButtons(win, allElements);
            }
            catch { }
            finally
            {
                _isApplyingSimplification = false;
            }
        }

        private static void CollapseSection3AdditionalOptions(List<DependencyObject> allElements, FrameworkElement protectedFactionRoot)
        {
            var textsBlocks = allElements.OfType<TextBlock>().ToList();

            foreach (var tb in textsBlocks)
            {
                string t = (tb.Text ?? "").Trim();
                if (string.IsNullOrEmpty(t)) continue;

                bool isSec3Header =
                    t.Contains("3. Dodatkowe opcje", StringComparison.OrdinalIgnoreCase) ||
                    t.Contains("_HIDDEN_3_DODATKOWE_OPCJE_", StringComparison.OrdinalIgnoreCase) ||
                    t.Contains("Podpis paczki, notatki, dodatkowe pliki", StringComparison.OrdinalIgnoreCase);

                bool isSec3Field =
                    t.Equals("Package ID", StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("Publisher", StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("Max downloads", StringComparison.OrdinalIgnoreCase) ||
                    t.Contains("Podpis paczki", StringComparison.OrdinalIgnoreCase) ||
                    t.Contains("Notatki do paczki", StringComparison.OrdinalIgnoreCase) ||
                    t.Contains("Dodatkowe pliki", StringComparison.OrdinalIgnoreCase);

                if (!isSec3Header && !isSec3Field) continue;

                var cardContainer = FindEnclosingCardOrSection(tb, protectedFactionRoot);
                if (cardContainer != null)
                {
                    CollapseElementCompletely(cardContainer);

                    // Jeśli to nagłówek sekcji "3. Dodatkowe opcje", ukryj również kolejny element w tym samym kontenerze (ciało sekcji 3)
                    if (isSec3Header && VisualTreeHelper.GetParent(cardContainer) is Panel parentPanel)
                    {
                        int idx = parentPanel.Children.IndexOf(cardContainer);
                        if (idx >= 0)
                        {
                            for (int next = idx + 1; next < parentPanel.Children.Count; next++)
                            {
                                if (parentPanel.Children[next] is FrameworkElement nextFe)
                                {
                                    if (IsProtectedContainer(nextFe, protectedFactionRoot)) break;
                                    var nextTexts = GetElementTexts(nextFe);
                                    bool looksLikeSec3Body = nextTexts.Any(nt =>
                                        nt.Contains("Package ID", StringComparison.OrdinalIgnoreCase) ||
                                        nt.Contains("Publisher", StringComparison.OrdinalIgnoreCase) ||
                                        nt.Contains("Max downloads", StringComparison.OrdinalIgnoreCase) ||
                                        nt.Contains("Wersja", StringComparison.OrdinalIgnoreCase) ||
                                        nt.Contains("Notatki", StringComparison.OrdinalIgnoreCase) ||
                                        nt.Contains("Podpis", StringComparison.OrdinalIgnoreCase));

                                    if (looksLikeSec3Body)
                                    {
                                        CollapseElementCompletely(nextFe);
                                    }
                                    else
                                    {
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // Sprawdź też kontenery Expander, jeśli sekcja 3 była opakowana w Expander
            foreach (var exp in allElements.OfType<Expander>())
            {
                if (IsProtectedContainer(exp, protectedFactionRoot)) continue;
                var texts = GetElementTexts(exp);
                if (texts.Any(x => x.Contains("3. Dodatkowe opcje", StringComparison.OrdinalIgnoreCase) ||
                                   x.Contains("Package ID", StringComparison.OrdinalIgnoreCase) ||
                                   x.Contains("Max downloads", StringComparison.OrdinalIgnoreCase)))
                {
                    CollapseElementCompletely(exp);
                }
            }
        }

        private static void CollapseLegacyBuildButtons(List<DependencyObject> allElements, FrameworkElement protectedFactionRoot)
        {
            var buttons = allElements.OfType<ButtonBase>().ToList();
            foreach (var btn in buttons)
            {
                var btnTexts = GetElementTexts(btn);
                string combined = string.Join(" ", btnTexts).Trim();
                if (string.IsNullOrEmpty(combined) && btn.Content is string s)
                    combined = s.Trim();

                // Sprawdź czy to stary przycisk "Generuj paczkę .FTAK" lub "Buduj paczkę" (niebędący przyciskiem generatora stron)
                bool isLegacyGenerateFtak =
                    combined.Contains("Generuj paczkę .FTAK", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("Generuj paczke .FTAK", StringComparison.OrdinalIgnoreCase) ||
                    combined.Equals("Buduj paczkę", StringComparison.OrdinalIgnoreCase) ||
                    combined.Equals("Buduj paczke", StringComparison.OrdinalIgnoreCase) ||
                    combined.Equals("Buduj paczkę .ftak", StringComparison.OrdinalIgnoreCase);

                // Przyciski generatora stron zawierają słowa "stron", "frakcj", "wszystkich" lub "Plakat" -> są chronione
                if (combined.Contains("stron", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("frakcj", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("wszystkich", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("Plakat", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!isLegacyGenerateFtak) continue;

                CollapseElementCompletely(btn);

                // Ukryj również sąsiedni mały przycisk rozwijany "v" / "▼" oraz ich wspólny kontener wiersza
                if (VisualTreeHelper.GetParent(btn) is Panel parentPanel)
                {
                    foreach (UIElement child in parentPanel.Children)
                    {
                        if (child is ButtonBase sibBtn && sibBtn != btn)
                        {
                            string sibTxt = string.Join("", GetElementTexts(sibBtn)).Trim();
                            if (sibBtn.Content is string ss) sibTxt += ss.Trim();
                            if (sibTxt.Equals("v", StringComparison.OrdinalIgnoreCase) ||
                                sibTxt.Equals("▼", StringComparison.OrdinalIgnoreCase) ||
                                sibTxt.Equals("▾", StringComparison.OrdinalIgnoreCase) ||
                                sibBtn.ActualWidth < 65)
                            {
                                CollapseElementCompletely(sibBtn);
                            }
                        }
                    }

                    if (!IsProtectedContainer(parentPanel, protectedFactionRoot) && parentPanel.Children.Count <= 4)
                    {
                        CollapseElementCompletely(parentPanel);
                    }
                }
            }
        }

        private static void HookModeToggleButtons(Window win, List<DependencyObject> allElements)
        {
            foreach (var btn in allElements.OfType<ButtonBase>())
            {
                if (btn.Tag?.ToString() == "FTH_MODE_HOOKED") continue;
                string txt = string.Join(" ", GetElementTexts(btn)).Trim();
                if (btn.Content is string s) txt += " " + s;

                if (txt.Contains("Tryb prosty", StringComparison.OrdinalIgnoreCase) ||
                    txt.Contains("Tryb zaawansowany", StringComparison.OrdinalIgnoreCase))
                {
                    btn.Tag = "FTH_MODE_HOOKED";
                    btn.Click += (_, __) =>
                    {
                        EnforceSimplification(win);
                        _ = win.Dispatcher.BeginInvoke(new Action(() => EnforceSimplification(win)), DispatcherPriority.Loaded);
                        _ = win.Dispatcher.BeginInvoke(new Action(() => EnforceSimplification(win)), DispatcherPriority.Background);
                    };
                }
            }
        }

        private static void EnsureVisibleUpToBuilderRoot(FrameworkElement element)
        {
            DependencyObject curr = element;
            while (curr != null && !(curr is Window))
            {
                if (curr is UIElement uie)
                {
                    // Nie odkrywaj całego kontenera zakładki Buildera, jeśli użytkownik aktualnie ogląda Pełnoekranowy Generator Map 1:1
                    if (curr is FrameworkElement fe && fe.Tag?.ToString() == "INJECTED_MAP_OVERLAY")
                        break;

                    if (uie.Visibility != Visibility.Visible)
                    {
                        // Sprawdź czy to nie jest główny kontener przełączany między zakładkami (Builder vs Mapa vs USB)
                        DependencyObject parent = VisualTreeHelper.GetParent(curr);
                        bool isTopTabContent = parent is Grid && ((Grid)parent).Children.Cast<UIElement>().Any(c => (c as FrameworkElement)?.Tag?.ToString() == "INJECTED_MAP_OVERLAY");
                        if (!isTopTabContent)
                        {
                            uie.Visibility = Visibility.Visible;
                        }
                    }
                }
                curr = VisualTreeHelper.GetParent(curr);
            }
        }

        private static FrameworkElement FindEnclosingCardOrSection(DependencyObject startNode, FrameworkElement protectedFactionRoot)
        {
            FrameworkElement bestCandidate = null;
            DependencyObject curr = startNode;
            while (curr != null && !(curr is Window) && !(curr is ScrollViewer))
            {
                if (curr is Border || curr is Expander)
                {
                    var fe = (FrameworkElement)curr;
                    if (IsProtectedContainer(fe, protectedFactionRoot))
                        break;
                    bestCandidate = fe;
                }
                curr = VisualTreeHelper.GetParent(curr);
            }
            return bestCandidate;
        }

        private static bool IsProtectedContainer(DependencyObject container, FrameworkElement protectedFactionRoot)
        {
            if (container == null) return false;
            if (protectedFactionRoot != null && (container == protectedFactionRoot || IsAncestorOf(container, protectedFactionRoot)))
                return true;

            var texts = GetElementTexts(container);
            return texts.Any(t =>
                t.Contains("1. ", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("2. ", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("Tryb prosty", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("Konfigurator Paczek (.ftak)", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("Generator Map Taktycznych 1:1", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("STRONY KONFLIKTU", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("Dodaj stronę", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("BLUFOR", StringComparison.OrdinalIgnoreCase));
        }

        private static void CollapseElementCompletely(FrameworkElement fe)
        {
            if (fe == null) return;
            fe.Visibility = Visibility.Collapsed;
            fe.Height = 0;
            fe.MinHeight = 0;
            fe.Margin = new Thickness(0);
            fe.IsEnabled = false;
        }

        private static bool IsAncestorOf(DependencyObject ancestor, DependencyObject descendant)
        {
            DependencyObject curr = descendant;
            while (curr != null)
            {
                if (curr == ancestor) return true;
                curr = VisualTreeHelper.GetParent(curr);
            }
            return false;
        }

        private static List<string> GetElementTexts(DependencyObject root)
        {
            var list = new List<string>();
            if (root == null) return list;
            foreach (var d in GetAllVisualDescendants(root))
            {
                if (d is TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text))
                    list.Add(tb.Text.Trim());
            }
            return list;
        }

        private static IEnumerable<DependencyObject> GetAllVisualDescendants(DependencyObject root)
        {
            if (root == null) yield break;
            var queue = new Queue<DependencyObject>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                int count = 0;
                try { count = VisualTreeHelper.GetChildrenCount(current); } catch { }
                for (int i = 0; i < count; i++)
                {
                    DependencyObject child = null;
                    try { child = VisualTreeHelper.GetChild(current, i); } catch { }
                    if (child != null)
                    {
                        yield return child;
                        queue.Enqueue(child);
                    }
                }
            }
        }
    }
}