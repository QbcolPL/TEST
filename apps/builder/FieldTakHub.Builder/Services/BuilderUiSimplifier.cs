#pragma warning disable CS0414, CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace FieldTakHub.Builder.Services
{
    internal static class BuilderUiSimplifier
    {
        private static readonly HashSet<int> AttachedWindows = new HashSet<int>();
        private static bool _isApplyingSimplification;

        private static Border _activeFactionHubBanner;
        private static ComboBox _activeFactionCombo;
        private static TextBlock _auditStatusLabel;
        private static string _selectedActiveFactionName = "";

        private static Button _topTabOpordBtn;
        private static Button _topLangToggleBtn;

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
            win.Closed += (_, __) => CleanFactionPackageEngine.StopPcFtakHttpServer();

            BuilderI18n.LanguageChanged += _ =>
            {
                if (_topLangToggleBtn != null)
                {
                    _topLangToggleBtn.Content = "🌐 " + BuilderI18n.CurrentLang;
                }
                if (_topTabOpordBtn != null)
                {
                    _topTabOpordBtn.Content = BuilderI18n.T("📋 Generator Rozkazu (OPORD)", "📋 NATO OPORD Builder");
                }
                RebuildActiveFactionBannerTexts();
            };

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

                // 1. Poczekaj, aż TacticalConfigPanel wstrzyknie główny moduł Generatora Stron Konfliktu
                FrameworkElement factionGeneratorRoot = FindFactionGeneratorRoot(allElements);
                if (factionGeneratorRoot == null)
                {
                    return;
                }

                // 2. Upewnij się, że Generator Stron jest w 100% widoczny (gdy nie jesteśmy w zakładce Mapy lub OPORD)
                EnsureFactionGeneratorVisible(factionGeneratorRoot);

                // 3. Wstrzyknij widok OPORD oraz przyciski górnego paska (📋 Generator Rozkazu OPORD + 🌐 PL/EN)
                InjectOpordTabAndLanguageToggle(win, allElements);

                // 4. Wstrzyknij na górze Generatora Stron zintegrowany pasek Aktywnej Strony (.FTAK 1:1 + OPORD + Kod QR)
                InjectOrUpdateActiveFactionAuditBar(win, factionGeneratorRoot);

                // 5. Ukryj przyciski przełącznika "Tryb prosty" / "Tryb zaawansowany"
                CollapseSimpleAdvancedModeButtonsOnly(allElements, factionGeneratorRoot);

                // 6. Ukryj wyłącznie małe karty sekcji "3. Dodatkowe opcje" (Package ID, Wersja, Publisher, Max downloads)
                CollapseSection3LeafCardsOnly(allElements, factionGeneratorRoot);

                // 7. Ukryj stary przycisk "Generuj paczkę .FTAK" + "v" oraz stary panel QR (:8765)
                CollapseLegacyV23ButtonsAndQrOnly(allElements, factionGeneratorRoot);

                // 8. Zastosuj wybrany język interfejsu (PL / EN)
                if (string.Equals(BuilderI18n.CurrentLang, "EN", StringComparison.OrdinalIgnoreCase))
                {
                    BuilderI18n.ApplyLanguageToVisualTree(win);
                }
            }
            catch { }
            finally
            {
                _isApplyingSimplification = false;
            }
        }

        // =========================================================================
        // 1. ZAKŁADKA GENERATORA OPORD + PRZEŁĄCZNIK JĘZYKA PL/EN W PASKU GÓRNYM
        // =========================================================================
        private static void InjectOpordTabAndLanguageToggle(Window win, List<DependencyObject> allElements)
        {
            var opordOverlay = OpordBuilderManager.CreateOrGetOpordOverlayView(win);

            // Znajdź istniejącą nakładkę Generatora Map 1:1, aby umieścić nakładkę OPORD w tym samym kontenerze Grid
            FrameworkElement mapOverlay = allElements
                .OfType<FrameworkElement>()
                .FirstOrDefault(fe => string.Equals(fe.Tag?.ToString(), "INJECTED_MAP_OVERLAY", StringComparison.Ordinal));

            if (mapOverlay != null && VisualTreeHelper.GetParent(mapOverlay) is Grid hostGrid)
            {
                if (!hostGrid.Children.Contains(opordOverlay))
                {
                    Grid.SetRow(opordOverlay, Grid.GetRow(mapOverlay));
                    Grid.SetColumn(opordOverlay, Grid.GetColumn(mapOverlay));
                    Grid.SetRowSpan(opordOverlay, Math.Max(1, Grid.GetRowSpan(mapOverlay)));
                    Grid.SetColumnSpan(opordOverlay, Math.Max(1, Grid.GetColumnSpan(mapOverlay)));
                    Panel.SetZIndex(opordOverlay, 95);
                    hostGrid.Children.Add(opordOverlay);
                }
            }
            else if (win.Content is Grid rootGrid && !rootGrid.Children.Contains(opordOverlay))
            {
                if (rootGrid.RowDefinitions.Count > 1)
                {
                    Grid.SetRow(opordOverlay, 1);
                    Grid.SetRowSpan(opordOverlay, rootGrid.RowDefinitions.Count - 1);
                }
                Panel.SetZIndex(opordOverlay, 95);
                rootGrid.Children.Add(opordOverlay);
            }

            // Znajdź górny pasek nawigacji z przyciskami zakładek ("Konfigurator Paczek" / "Generator Map Taktycznych 1:1")
            Button mapTabBtn = null;
            Button builderTabBtn = null;

            foreach (var btn in allElements.OfType<Button>())
            {
                string txt = string.Join(" ", GetElementTexts(btn)).Trim();
                if (btn.Content is string s) txt = (txt + " " + s).Trim();

                if (txt.Contains("Generator Map Taktycznych 1:1", StringComparison.OrdinalIgnoreCase) ||
                    txt.Contains("1:1 Tactical Map", StringComparison.OrdinalIgnoreCase))
                {
                    mapTabBtn = btn;
                }
                else if (txt.Contains("Konfigurator Paczek", StringComparison.OrdinalIgnoreCase) ||
                         txt.Contains("Faction & Package Builder", StringComparison.OrdinalIgnoreCase))
                {
                    builderTabBtn = btn;
                }
            }

            if (mapTabBtn != null && VisualTreeHelper.GetParent(mapTabBtn) is Panel topNavPanel)
            {
                if (_topTabOpordBtn == null || !topNavPanel.Children.Contains(_topTabOpordBtn))
                {
                    _topTabOpordBtn = new Button
                    {
                        Content = BuilderI18n.T("📋 Generator Rozkazu (OPORD)", "📋 NATO OPORD Builder"),
                        Height = mapTabBtn.Height > 0 ? mapTabBtn.Height : 32,
                        Padding = new Thickness(12, 4, 12, 4),
                        Margin = new Thickness(4, 0, 4, 0),
                        Background = TacticalConfigPanel.BrushItemBg,
                        Foreground = Brushes.White,
                        BorderBrush = TacticalConfigPanel.BrushAccent,
                        BorderThickness = new Thickness(1.2),
                        FontWeight = FontWeights.Bold,
                        FontSize = 11.5,
                        Cursor = Cursors.Hand,
                        Tag = "INJECTED_OPORD_TAB_BTN"
                    };

                    _topTabOpordBtn.Click += (_, __) => ShowOpordView(win, true);

                    int mapIdx = topNavPanel.Children.IndexOf(mapTabBtn);
                    if (mapIdx >= 0)
                        topNavPanel.Children.Insert(mapIdx + 1, _topTabOpordBtn);
                    else
                        topNavPanel.Children.Add(_topTabOpordBtn);
                }

                if (_topLangToggleBtn == null || !topNavPanel.Children.Contains(_topLangToggleBtn))
                {
                    _topLangToggleBtn = new Button
                    {
                        Content = "🌐 " + BuilderI18n.CurrentLang,
                        ToolTip = "Przełącz język interfejsu: Polski (PL) / Angielski (EN)",
                        Height = mapTabBtn.Height > 0 ? mapTabBtn.Height : 32,
                        Padding = new Thickness(10, 4, 10, 4),
                        Margin = new Thickness(6, 0, 4, 0),
                        Background = new SolidColorBrush(Color.FromRgb(18, 44, 54)),
                        Foreground = TacticalConfigPanel.BrushAccent,
                        BorderBrush = TacticalConfigPanel.BrushAccent,
                        BorderThickness = new Thickness(1.2),
                        FontWeight = FontWeights.Bold,
                        FontSize = 11.5,
                        Cursor = Cursors.Hand,
                        Tag = "INJECTED_LANG_BTN"
                    };

                    _topLangToggleBtn.Click += (_, __) => BuilderI18n.ToggleLanguage(win);
                    topNavPanel.Children.Add(_topLangToggleBtn);
                }

                // Gdy użytkownik kliknie zakładkę "Konfigurator Paczek (.ftak)" lub "Generator Map 1:1", zamknij widok OPORD
                if (builderTabBtn != null && builderTabBtn.Tag?.ToString() != "OPORD_HOOKED")
                {
                    builderTabBtn.Tag = "OPORD_HOOKED";
                    builderTabBtn.Click += (_, __) => ShowOpordView(win, false);
                }
                if (mapTabBtn.Tag?.ToString() != "OPORD_HOOKED")
                {
                    mapTabBtn.Tag = "OPORD_HOOKED";
                    mapTabBtn.Click += (_, __) => ShowOpordView(win, false);
                }
            }
        }

        public static void ShowOpordView(Window win, bool showOpord)
        {
            var opordOverlay = OpordBuilderManager.CreateOrGetOpordOverlayView(win);
            if (showOpord)
            {
                // Ukryj nakładkę mapy, jeśli była otwarta
                foreach (var fe in GetAllVisualDescendants(win).OfType<FrameworkElement>())
                {
                    if (string.Equals(fe.Tag?.ToString(), "INJECTED_MAP_OVERLAY", StringComparison.Ordinal))
                    {
                        fe.Visibility = Visibility.Collapsed;
                    }
                }
                opordOverlay.Visibility = Visibility.Visible;
                if (_topTabOpordBtn != null)
                {
                    _topTabOpordBtn.Background = TacticalConfigPanel.BrushButtonAccent;
                    _topTabOpordBtn.Foreground = TacticalConfigPanel.BrushDarkFg;
                }
            }
            else
            {
                opordOverlay.Visibility = Visibility.Collapsed;
                if (_topTabOpordBtn != null)
                {
                    _topTabOpordBtn.Background = TacticalConfigPanel.BrushItemBg;
                    _topTabOpordBtn.Foreground = Brushes.White;
                }
            }
        }

        // =========================================================================
        // 2. WYSZUKIWANIE I OCHRONA GENERATORA STRON KONFLIKTU
        // =========================================================================
        private static FrameworkElement FindFactionGeneratorRoot(List<DependencyObject> allElements)
        {
            foreach (var fe in allElements.OfType<FrameworkElement>())
            {
                string tag = fe.Tag?.ToString() ?? "";
                if (tag.Contains("TACTICAL_CONFIG", StringComparison.OrdinalIgnoreCase) ||
                    tag.Contains("FACTION", StringComparison.OrdinalIgnoreCase) ||
                    tag.Contains("INJECTED_PANEL", StringComparison.OrdinalIgnoreCase))
                {
                    return fe;
                }
            }

            FrameworkElement smallestMatch = null;
            int smallestDescendantCount = int.MaxValue;

            foreach (var fe in allElements.OfType<FrameworkElement>())
            {
                if (!(fe is Border || fe is StackPanel || fe is Grid)) continue;
                var texts = GetElementTexts(fe);

                bool hasFactionUi =
                    texts.Any(t => t.Contains("Dodaj stronę", StringComparison.OrdinalIgnoreCase) ||
                                   t.Contains("Add Faction", StringComparison.OrdinalIgnoreCase) ||
                                   t.Contains("Plakat A4 Safezone", StringComparison.OrdinalIgnoreCase) ||
                                   t.Contains("A4 Safezone Poster", StringComparison.OrdinalIgnoreCase) ||
                                   t.Contains("STRONY KONFLIKTU", StringComparison.OrdinalIgnoreCase));

                if (!hasFactionUi) continue;

                if (texts.Any(t => t.Contains("Generator Map Taktycznych 1:1", StringComparison.OrdinalIgnoreCase) ||
                                   t.Contains("1:1 Tactical Map", StringComparison.OrdinalIgnoreCase) ||
                                   t.Contains("Zapisz projekt roboczy", StringComparison.OrdinalIgnoreCase) ||
                                   t.Contains("Save Workspace", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                int descCount = GetAllVisualDescendants(fe).Count();
                if (descCount < smallestDescendantCount && descCount >= 8)
                {
                    smallestDescendantCount = descCount;
                    smallestMatch = fe;
                }
            }
            return smallestMatch;
        }

        private static void EnsureFactionGeneratorVisible(FrameworkElement factionRoot)
        {
            DependencyObject curr = factionRoot;
            while (curr != null && !(curr is Window))
            {
                if (curr is FrameworkElement fe)
                {
                    string tag = fe.Tag?.ToString() ?? "";
                    if (tag == "INJECTED_MAP_OVERLAY" || tag == "INJECTED_OPORD_OVERLAY") break;

                    if (fe.Visibility != Visibility.Visible)
                    {
                        DependencyObject parent = VisualTreeHelper.GetParent(fe);
                        bool isMasterTabSwitch = parent is Grid g && g.Children.Cast<UIElement>().Any(c =>
                            (c as FrameworkElement)?.Tag?.ToString() == "INJECTED_MAP_OVERLAY" ||
                            (c as FrameworkElement)?.Tag?.ToString() == "INJECTED_OPORD_OVERLAY");
                        if (!isMasterTabSwitch)
                        {
                            fe.Visibility = Visibility.Visible;
                        }
                    }
                    if (!double.IsNaN(fe.Height) && fe.Height == 0)
                    {
                        fe.Height = double.NaN;
                    }
                    fe.IsEnabled = true;
                }
                curr = VisualTreeHelper.GetParent(curr);
            }
        }

        // =========================================================================
        // 3. CHIRURGICZNE UKRYWANIE POZOSTAŁOŚCI V2.3 (SEKCJA 3, TRYBY, PRZYCISK .FTAK)
        // =========================================================================
        private static void CollapseSimpleAdvancedModeButtonsOnly(List<DependencyObject> allElements, FrameworkElement factionRoot)
        {
            foreach (var btn in allElements.OfType<ButtonBase>())
            {
                if (IsInsideOrAncestorOf(btn, factionRoot)) continue;

                string txt = string.Join(" ", GetElementTexts(btn)).Trim();
                if (btn.Content is string s) txt += " " + s;

                if (txt.Contains("Tryb prosty", StringComparison.OrdinalIgnoreCase) ||
                    txt.Contains("Tryb zaawansowany", StringComparison.OrdinalIgnoreCase))
                {
                    CollapseLeafElement(btn);
                }
            }
        }

        private static void CollapseSection3LeafCardsOnly(List<DependencyObject> allElements, FrameworkElement factionRoot)
        {
            foreach (var tb in allElements.OfType<TextBlock>())
            {
                if (IsInsideOrAncestorOf(tb, factionRoot)) continue;
                string t = (tb.Text ?? "").Trim();
                if (string.IsNullOrEmpty(t)) continue;

                bool isSec3Header =
                    t.Equals("3. Dodatkowe opcje", StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("Podpis paczki, notatki, dodatkowe pliki", StringComparison.OrdinalIgnoreCase);

                bool isSec3Field =
                    t.Equals("Package ID", StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("Publisher", StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("Max downloads", StringComparison.OrdinalIgnoreCase);

                if (!isSec3Header && !isSec3Field) continue;

                Border innermostBorder = FindInnermostBorder(tb);
                if (innermostBorder != null && !IsInsideOrAncestorOf(innermostBorder, factionRoot))
                {
                    int totalTextBlocksInside = GetElementTexts(innermostBorder).Count;
                    if (totalTextBlocksInside <= 12)
                    {
                        CollapseLeafElement(innermostBorder);
                    }
                }
            }
        }

        private static void CollapseLegacyV23ButtonsAndQrOnly(List<DependencyObject> allElements, FrameworkElement factionRoot)
        {
            foreach (var btn in allElements.OfType<ButtonBase>())
            {
                if (IsInsideOrAncestorOf(btn, factionRoot)) continue;
                if (_activeFactionHubBanner != null && IsInsideOrAncestorOf(btn, _activeFactionHubBanner)) continue;

                string combined = string.Join(" ", GetElementTexts(btn)).Trim();
                if (string.IsNullOrEmpty(combined) && btn.Content is string s)
                    combined = s.Trim();

                if (combined.Contains("stron", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("Faction", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("wszystkich", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("Plakat", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("Poster", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("OPORD", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("Zapisz", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("Save", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("Wczytaj", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("Load", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("Wesprzyj", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("Support", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bool isLegacyBtn =
                    combined.Contains("Generuj paczkę .FTAK", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("Generuj paczke .FTAK", StringComparison.OrdinalIgnoreCase) ||
                    combined.Equals("Buduj paczkę", StringComparison.OrdinalIgnoreCase) ||
                    combined.Equals("Buduj paczke", StringComparison.OrdinalIgnoreCase);

                if (!isLegacyBtn) continue;

                CollapseLeafElement(btn);

                if (VisualTreeHelper.GetParent(btn) is Panel parentPanel && !IsInsideOrAncestorOf(parentPanel, factionRoot))
                {
                    foreach (UIElement child in parentPanel.Children)
                    {
                        if (child is ButtonBase sibBtn && sibBtn != btn && sibBtn.ActualWidth < 70)
                        {
                            CollapseLeafElement(sibBtn);
                        }
                    }
                }
            }

            foreach (var tb in allElements.OfType<TextBlock>())
            {
                if (IsInsideOrAncestorOf(tb, factionRoot)) continue;
                if (_activeFactionHubBanner != null && IsInsideOrAncestorOf(tb, _activeFactionHubBanner)) continue;

                string t = (tb.Text ?? "").Trim();
                if (t.Contains("Zostanie wygenerowany po utworzeniu paczki", StringComparison.OrdinalIgnoreCase) ||
                    (t.Contains(":8765/") && t.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
                {
                    Border qrBorder = FindInnermostBorder(tb);
                    if (qrBorder != null && !IsInsideOrAncestorOf(qrBorder, factionRoot))
                    {
                        if (GetElementTexts(qrBorder).Count <= 10)
                        {
                            CollapseLeafElement(qrBorder);
                        }
                    }
                }
            }
        }

        private static Border FindInnermostBorder(DependencyObject startNode)
        {
            DependencyObject curr = VisualTreeHelper.GetParent(startNode);
            while (curr != null && !(curr is Window) && !(curr is ScrollViewer))
            {
                if (curr is Border b) return b;
                curr = VisualTreeHelper.GetParent(curr);
            }
            return null;
        }

        private static bool IsInsideOrAncestorOf(DependencyObject candidate, FrameworkElement protectedRoot)
        {
            if (candidate == null || protectedRoot == null) return false;
            if (candidate == protectedRoot) return true;
            if (IsAncestorOf(protectedRoot, candidate)) return true;
            if (IsAncestorOf(candidate, protectedRoot)) return true;
            return false;
        }

        private static void CollapseLeafElement(FrameworkElement fe)
        {
            if (fe == null) return;
            fe.Visibility = Visibility.Collapsed;
            fe.Height = 0;
            fe.MinHeight = 0;
            fe.Margin = new Thickness(0);
            fe.IsEnabled = false;
        }

        // =========================================================================
        // 4. ZINTEGROWANY PASEK AKTYWNEJ STRONY (.FTAK 1:1 + OPORD + KOD QR)
        // =========================================================================
        private static TextBlock _bannerTitleBlock;
        private static TextBlock _bannerDescBlock;
        private static Button _btnBuildSingleFaction;
        private static Button _btnShowActiveQr;
        private static Button _btnOpenOpordQuick;
        private static Button _btnBuildAllFactions;

        private static void InjectOrUpdateActiveFactionAuditBar(Window win, FrameworkElement factionGeneratorRoot)
        {
            Panel targetParent = factionGeneratorRoot as Panel;
            if (targetParent == null && factionGeneratorRoot is Border b && b.Child is Panel p)
                targetParent = p;
            if (targetParent == null)
                targetParent = VisualTreeHelper.GetParent(factionGeneratorRoot) as Panel;
            if (targetParent == null) return;

            var configuredFactions = TacticalConfigPanel.GetConfiguredFactions();
            if (_activeFactionHubBanner != null && targetParent.Children.Contains(_activeFactionHubBanner))
            {
                SyncActiveFactionComboItems(configuredFactions);
                return;
            }

            _activeFactionHubBanner = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(11, 27, 34)),
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 0, 12)
            };

            var stack = new StackPanel();

            _bannerTitleBlock = new TextBlock
            {
                Foreground = TacticalConfigPanel.BrushAccent,
                FontWeight = FontWeights.Bold,
                FontSize = 12.5,
                Margin = new Thickness(0, 0, 0, 4)
            };
            stack.Children.Add(_bannerTitleBlock);

            _bannerDescBlock = new TextBlock
            {
                Foreground = TacticalConfigPanel.BrushMutedText,
                FontSize = 10.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            stack.Children.Add(_bannerDescBlock);

            var controlsRow = new Grid();
            controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
            controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
            controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star) });
            controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star) });

            _activeFactionCombo = new ComboBox
            {
                Height = 32,
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 6, 0),
                Tag = "INJECTED"
            };
            TacticalConfigPanel.ApplyUnifiedComboBoxTheme(_activeFactionCombo, win);
            _activeFactionCombo.SelectionChanged += (_, __) =>
            {
                if (_activeFactionCombo.SelectedItem is string s && !string.IsNullOrWhiteSpace(s))
                    _selectedActiveFactionName = s;
            };
            SyncActiveFactionComboItems(configuredFactions);
            Grid.SetColumn(_activeFactionCombo, 0);
            controlsRow.Children.Add(_activeFactionCombo);

            _btnBuildSingleFaction = new Button
            {
                Height = 32,
                Margin = new Thickness(0, 0, 6, 0),
                Background = TacticalConfigPanel.BrushButtonAccent,
                Foreground = TacticalConfigPanel.BrushDarkFg,
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.Bold,
                FontSize = 10.5,
                Cursor = Cursors.Hand
            };
            _btnBuildSingleFaction.Click += async (_, __) =>
            {
                SyncActiveFactionComboItems(TacticalConfigPanel.GetConfiguredFactions());
                string facName = _activeFactionCombo.SelectedItem as string ?? _selectedActiveFactionName;
                await CleanFactionPackageEngine.BuildIsolatedFactionPackagesAsync(
                    win, facName, showQrModalAfter: false,
                    msg => { if (_auditStatusLabel != null) _auditStatusLabel.Text = msg; });
            };
            Grid.SetColumn(_btnBuildSingleFaction, 1);
            controlsRow.Children.Add(_btnBuildSingleFaction);

            _btnShowActiveQr = new Button
            {
                Height = 32,
                Margin = new Thickness(0, 0, 6, 0),
                Background = new SolidColorBrush(Color.FromRgb(22, 54, 66)),
                Foreground = Brushes.White,
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(1.4),
                FontWeight = FontWeights.Bold,
                FontSize = 10.5,
                Cursor = Cursors.Hand
            };
            _btnShowActiveQr.Click += async (_, __) =>
            {
                SyncActiveFactionComboItems(TacticalConfigPanel.GetConfiguredFactions());
                string facName = _activeFactionCombo.SelectedItem as string ?? _selectedActiveFactionName;
                await CleanFactionPackageEngine.BuildIsolatedFactionPackagesAsync(
                    win, facName, showQrModalAfter: true,
                    msg => { if (_auditStatusLabel != null) _auditStatusLabel.Text = msg; });
            };
            Grid.SetColumn(_btnShowActiveQr, 2);
            controlsRow.Children.Add(_btnShowActiveQr);

            _btnOpenOpordQuick = new Button
            {
                Height = 32,
                Margin = new Thickness(0, 0, 6, 0),
                Background = new SolidColorBrush(Color.FromRgb(28, 48, 36)),
                Foreground = new SolidColorBrush(Color.FromRgb(46, 204, 113)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(46, 204, 113)),
                BorderThickness = new Thickness(1.2),
                FontWeight = FontWeights.Bold,
                FontSize = 10.5,
                Cursor = Cursors.Hand
            };
            _btnOpenOpordQuick.Click += (_, __) => ShowOpordView(win, true);
            Grid.SetColumn(_btnOpenOpordQuick, 3);
            controlsRow.Children.Add(_btnOpenOpordQuick);

            _btnBuildAllFactions = new Button
            {
                Height = 32,
                Background = TacticalConfigPanel.BrushItemBg,
                Foreground = TacticalConfigPanel.BrushAccent,
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.Bold,
                FontSize = 10.5,
                Cursor = Cursors.Hand
            };
            _btnBuildAllFactions.Click += async (_, __) =>
            {
                SyncActiveFactionComboItems(TacticalConfigPanel.GetConfiguredFactions());
                await CleanFactionPackageEngine.BuildIsolatedFactionPackagesAsync(
                    win, null, showQrModalAfter: false,
                    msg => { if (_auditStatusLabel != null) _auditStatusLabel.Text = msg; });
            };
            Grid.SetColumn(_btnBuildAllFactions, 4);
            controlsRow.Children.Add(_btnBuildAllFactions);

            stack.Children.Add(controlsRow);

            _auditStatusLabel = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(46, 204, 113)),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 6, 0, 0)
            };
            stack.Children.Add(_auditStatusLabel);

            RebuildActiveFactionBannerTexts();

            _activeFactionHubBanner.Child = stack;
            targetParent.Children.Insert(0, _activeFactionHubBanner);
        }

        private static void RebuildActiveFactionBannerTexts()
        {
            if (_bannerTitleBlock != null)
            {
                _bannerTitleBlock.Text = BuilderI18n.T(
                    "🎯 CENTRUM WDROŻENIA AKTYWNEJ STRONY (.FTAK 1:1 + ROZKAZ OPORD + KOD QR NA EKRANIE PC)",
                    "🎯 ACTIVE FACTION DEPLOYMENT HUB (.FTAK 1:1 + NATO OPORD + PC SCREEN QR CODE)");
            }
            if (_bannerDescBlock != null)
            {
                _bannerDescBlock.Text = BuilderI18n.T(
                    "Wybierz aktywną stronę konfliktu poniżej, aby zbudować dla niej w 100% odizolowaną paczkę .FTAK (Fog of War: tylko klucze PSK, serwer OTS, tabela radiowa, mapy i rozkaz OPORD tej strony) lub wyświetlić Kod QR do zeskanowania telefonem:",
                    "Select the active faction below to build a 100% isolated .FTAK bundle (Fog of War: only PSK keys, OTS server, voice channels, maps, and OPORD for that faction) or display the QR Code for mobile scanning:");
            }
            if (_btnBuildSingleFaction != null)
                _btnBuildSingleFaction.Content = BuilderI18n.T("📦 Buduj .FTAK aktywnej strony", "📦 Build Active Faction .FTAK");
            if (_btnShowActiveQr != null)
                _btnShowActiveQr.Content = BuilderI18n.T("📱 Pokaż Kod QR aktywnej strony", "📱 Show Active Faction QR");
            if (_btnOpenOpordQuick != null)
                _btnOpenOpordQuick.Content = BuilderI18n.T("📋 Edytuj Rozkaz OPORD + METOC", "📋 Edit OPORD + METOC Weather");
            if (_btnBuildAllFactions != null)
                _btnBuildAllFactions.Content = BuilderI18n.T("⚡ Buduj .FTAK WSZYSTKICH stron", "⚡ Build ALL Factions .FTAK");
            if (_auditStatusLabel != null && string.IsNullOrWhiteSpace(_auditStatusLabel.Text))
            {
                _auditStatusLabel.Text = BuilderI18n.T(
                    "✓ Izolacja stron (Fog of War) aktywna: każda paczka .ftak zawiera wyłącznie zasoby, mapy i rozkaz OPORD wybranej strony.",
                    "✓ Faction isolation (Fog of War) active: each .ftak bundle contains only assets, maps, and OPORD for the selected faction.");
            }
        }

        private static void SyncActiveFactionComboItems(List<(string Name, string Channel, string ColorHex, string Psk)> configured)
        {
            if (_activeFactionCombo == null || configured == null || configured.Count == 0) return;
            var names = configured.Select(c => c.Name).ToList();
            bool changed = _activeFactionCombo.Items.Count != names.Count;
            if (!changed)
            {
                for (int i = 0; i < names.Count; i++)
                {
                    if (!string.Equals(_activeFactionCombo.Items[i]?.ToString(), names[i], StringComparison.Ordinal))
                    {
                        changed = true;
                        break;
                    }
                }
            }

            if (changed)
            {
                string prev = _selectedActiveFactionName;
                _activeFactionCombo.ItemsSource = names;
                if (!string.IsNullOrEmpty(prev) && names.Contains(prev))
                    _activeFactionCombo.SelectedItem = prev;
                else
                    _activeFactionCombo.SelectedIndex = 0;
            }
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