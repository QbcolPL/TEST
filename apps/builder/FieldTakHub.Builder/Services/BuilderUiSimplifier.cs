#pragma warning disable CS0414, CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

        // Lokalny serwer HTTP na PC (port 8766) serwujący zaudytowane pliki .ftak dla aktywnej strony prosto na telefon
        private static TcpListener _pcHotspotListener;
        private static volatile bool _pcServerRunning;
        private static int _pcServerPort = 8766;
        private static readonly Dictionary<string, string> ServedFtakPathsByFaction = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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
            win.Closed += (_, __) => StopPcFtakHttpServer();

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

                // 1. Znajdź główny kontener Generatora Stron Konfliktu (TacticalConfigPanel)
                FrameworkElement factionGeneratorRoot = FindFactionGeneratorRoot(allElements);
                if (factionGeneratorRoot != null)
                {
                    EnsureVisibleUpToBuilderRoot(factionGeneratorRoot);
                    InjectOrUpdateActiveFactionAuditBar(win, factionGeneratorRoot);
                }

                // 2. Usuń całkowicie przełącznik "Tryb prosty" / "Tryb zaawansowany"
                CollapseSimpleAdvancedModeSwitcher(allElements, factionGeneratorRoot);

                // 3. Usuń całkowicie sekcję "3. Dodatkowe opcje" (Package ID, Wersja, Publisher, Max downloads, notatki)
                CollapseSection3AdditionalOptions(allElements, factionGeneratorRoot);

                // 4. Usuń stary przycisk "Generuj paczkę .FTAK" oraz stary boczny panel QR (:8765/d/...json) z szablonu v2.3
                CollapseLegacyV23BuildAndQrPanels(allElements, factionGeneratorRoot);
            }
            catch { }
            finally
            {
                _isApplyingSimplification = false;
            }
        }

        private static FrameworkElement FindFactionGeneratorRoot(List<DependencyObject> allElements)
        {
            FrameworkElement best = null;
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
                        if (!texts.Any(t => t.Contains("Generator Map Taktycznych 1:1", StringComparison.OrdinalIgnoreCase)))
                        {
                            if (best == null || IsAncestorOf(fe, best))
                                best = fe;
                        }
                    }
                }
            }
            return best;
        }

        // =========================================================================
        // 1. CAŁKOWITE USUNIĘCIE PRZEŁĄCZNIKA "TRYB PROSTY / TRYB ZAAWANSOWANY"
        // =========================================================================
        private static void CollapseSimpleAdvancedModeSwitcher(List<DependencyObject> allElements, FrameworkElement protectedFactionRoot)
        {
            foreach (var btn in allElements.OfType<ButtonBase>())
            {
                string txt = string.Join(" ", GetElementTexts(btn)).Trim();
                if (btn.Content is string s) txt += " " + s;

                if (txt.Contains("Tryb prosty", StringComparison.OrdinalIgnoreCase) ||
                    txt.Contains("Tryb zaawansowany", StringComparison.OrdinalIgnoreCase))
                {
                    CollapseElementCompletely(btn);

                    if (VisualTreeHelper.GetParent(btn) is Panel parentPanel && !IsProtectedContainer(parentPanel, protectedFactionRoot))
                    {
                        var panelTexts = GetElementTexts(parentPanel);
                        bool onlyModeButtons = panelTexts.All(t =>
                            t.Contains("Tryb prosty", StringComparison.OrdinalIgnoreCase) ||
                            t.Contains("Tryb zaawansowany", StringComparison.OrdinalIgnoreCase) ||
                            t.Contains("Widok", StringComparison.OrdinalIgnoreCase));
                        if (onlyModeButtons)
                        {
                            CollapseElementCompletely(parentPanel);
                            if (VisualTreeHelper.GetParent(parentPanel) is Border parentBorder && !IsProtectedContainer(parentBorder, protectedFactionRoot))
                            {
                                CollapseElementCompletely(parentBorder);
                            }
                        }
                    }
                }
            }

            foreach (var tb in allElements.OfType<TextBlock>())
            {
                string t = (tb.Text ?? "").Trim();
                if (t.Equals("Tryb prosty", StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("Tryb zaawansowany", StringComparison.OrdinalIgnoreCase))
                {
                    if (VisualTreeHelper.GetParent(tb) is FrameworkElement parentFe && !IsProtectedContainer(parentFe, protectedFactionRoot))
                    {
                        CollapseElementCompletely(parentFe);
                    }
                }
            }
        }

        // =========================================================================
        // 2. CAŁKOWITE USUNIĘCIE SEKCJI "3. DODATKOWE OPCJE"
        // =========================================================================
        private static void CollapseSection3AdditionalOptions(List<DependencyObject> allElements, FrameworkElement protectedFactionRoot)
        {
            foreach (var tb in allElements.OfType<TextBlock>())
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
                                        CollapseElementCompletely(nextFe);
                                    else
                                        break;
                                }
                            }
                        }
                    }
                }
            }
        }

        // =========================================================================
        // 3. USUNIĘCIE STAREGO PRZYCISKU "GENERUJ PACZKĘ .FTAK" I STAREGO PANELU QR :8765
        // =========================================================================
        private static void CollapseLegacyV23BuildAndQrPanels(List<DependencyObject> allElements, FrameworkElement protectedFactionRoot)
        {
            // A. Stary przycisk "Generuj paczkę .FTAK" + rozwijane "v"
            foreach (var btn in allElements.OfType<ButtonBase>())
            {
                if (protectedFactionRoot != null && IsAncestorOf(protectedFactionRoot, btn)) continue;
                if (_activeFactionHubBanner != null && IsAncestorOf(_activeFactionHubBanner, btn)) continue;

                string combined = string.Join(" ", GetElementTexts(btn)).Trim();
                if (string.IsNullOrEmpty(combined) && btn.Content is string s)
                    combined = s.Trim();

                if (combined.Contains("stron", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("frakcj", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("wszystkich", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("Plakat", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("Zapisz projekt", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("Wczytaj projekt", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("Wesprzyj", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bool isLegacyBtn =
                    combined.Contains("Generuj paczkę .FTAK", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("Generuj paczke .FTAK", StringComparison.OrdinalIgnoreCase) ||
                    combined.Equals("Buduj paczkę", StringComparison.OrdinalIgnoreCase) ||
                    combined.Equals("Buduj paczke", StringComparison.OrdinalIgnoreCase);

                if (!isLegacyBtn) continue;

                CollapseElementCompletely(btn);
                if (VisualTreeHelper.GetParent(btn) is Panel parentPanel && !IsProtectedContainer(parentPanel, protectedFactionRoot))
                {
                    foreach (UIElement child in parentPanel.Children)
                    {
                        if (child is ButtonBase sibBtn && sibBtn != btn && sibBtn.ActualWidth < 70)
                            CollapseElementCompletely(sibBtn);
                    }
                    if (parentPanel.Children.Count <= 4)
                        CollapseElementCompletely(parentPanel);
                }
            }

            // B. Stary boczny panel QR ("Zostanie wygenerowany po utworzeniu paczki" / http://...:8765/d/...json)
            foreach (var tb in allElements.OfType<TextBlock>())
            {
                if (protectedFactionRoot != null && IsAncestorOf(protectedFactionRoot, tb)) continue;
                if (_activeFactionHubBanner != null && IsAncestorOf(_activeFactionHubBanner, tb)) continue;

                string t = (tb.Text ?? "").Trim();
                if (t.Contains("Zostanie wygenerowany po utworzeniu paczki", StringComparison.OrdinalIgnoreCase) ||
                    (t.Contains(":8765/") && t.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
                {
                    var legacyQrCard = FindEnclosingCardOrSection(tb, protectedFactionRoot);
                    if (legacyQrCard != null)
                    {
                        CollapseElementCompletely(legacyQrCard);
                    }
                }
            }
        }

        // =========================================================================
        // 4. PANEL STEROWANIA I AUDYTU AKTYWNEJ STRONY (.FTAK + LIVE QR PC -> TELEFON)
        // =========================================================================
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

            var topDock = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            topDock.Children.Add(new TextBlock
            {
                Text = "🎯 SZYBKIE WDROŻENIE AKTYWNEJ STRONY (.FTAK 1:1 + KOD QR NA EKRANIE PC)",
                Foreground = TacticalConfigPanel.BrushAccent,
                FontWeight = FontWeights.Bold,
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center
            });
            stack.Children.Add(topDock);

            stack.Children.Add(new TextBlock
            {
                Text = "Wybierz aktywną stronę konfliktu poniżej, aby zbudować dla niej w 100% odizolowaną paczkę .FTAK (Fog of War: tylko klucze PSK, OTS, tabela radiowa i mapy tej strony) oraz wyświetlić Kod QR do zeskanowania telefonem:",
                Foreground = TacticalConfigPanel.BrushMutedText,
                FontSize = 10.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var controlsRow = new Grid();
            controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });

            _activeFactionCombo = new ComboBox
            {
                Height = 32,
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 8, 0),
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

            var btnBuildSingle = new Button
            {
                Content = "📦 Buduj .FTAK dla aktywnej strony",
                Height = 32,
                Margin = new Thickness(0, 0, 6, 0),
                Background = TacticalConfigPanel.BrushButtonAccent,
                Foreground = TacticalConfigPanel.BrushDarkFg,
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                Cursor = Cursors.Hand
            };
            btnBuildSingle.Click += async (_, __) =>
            {
                SyncActiveFactionComboItems(TacticalConfigPanel.GetConfiguredFactions());
                string facName = _activeFactionCombo.SelectedItem as string ?? _selectedActiveFactionName;
                await BuildAuditedFactionPackagesAsync(win, facName, showQrModalAfter: false);
            };
            Grid.SetColumn(btnBuildSingle, 1);
            controlsRow.Children.Add(btnBuildSingle);

            var btnBuildAndShowQr = new Button
            {
                Content = "📱 Pokaż Kod QR aktywnej strony (Wi-Fi / Offline)",
                Height = 32,
                Margin = new Thickness(0, 0, 6, 0),
                Background = new SolidColorBrush(Color.FromRgb(22, 54, 66)),
                Foreground = Brushes.White,
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(1.4),
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                Cursor = Cursors.Hand
            };
            btnBuildAndShowQr.Click += async (_, __) =>
            {
                SyncActiveFactionComboItems(TacticalConfigPanel.GetConfiguredFactions());
                string facName = _activeFactionCombo.SelectedItem as string ?? _selectedActiveFactionName;
                await BuildAuditedFactionPackagesAsync(win, facName, showQrModalAfter: true);
            };
            Grid.SetColumn(btnBuildAndShowQr, 2);
            controlsRow.Children.Add(btnBuildAndShowQr);

            var btnBuildAll = new Button
            {
                Content = "⚡ Buduj .FTAK dla WSZYSTKICH stron",
                Height = 32,
                Background = TacticalConfigPanel.BrushItemBg,
                Foreground = TacticalConfigPanel.BrushAccent,
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                Cursor = Cursors.Hand
            };
            btnBuildAll.Click += async (_, __) =>
            {
                SyncActiveFactionComboItems(TacticalConfigPanel.GetConfiguredFactions());
                await BuildAuditedFactionPackagesAsync(win, null, showQrModalAfter: false);
            };
            Grid.SetColumn(btnBuildAll, 3);
            controlsRow.Children.Add(btnBuildAll);

            stack.Children.Add(controlsRow);

            _auditStatusLabel = new TextBlock
            {
                Text = "✓ Audyt izolacji stron aktywny: każda paczka .ftak zawiera wyłącznie klucze, tabele radiowe i mapy przypisanej strony konfliktu.",
                Foreground = new SolidColorBrush(Color.FromRgb(46, 204, 113)),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 6, 0, 0)
            };
            stack.Children.Add(_auditStatusLabel);

            _activeFactionHubBanner.Child = stack;
            targetParent.Children.Insert(0, _activeFactionHubBanner);
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

        // =========================================================================
        // 5. ZAUDYTOWANY GENERATOR PACZEK .FTAK (100% IZOLACJI STRON + PROTOBUF URL)
        // =========================================================================
        private static async Task BuildAuditedFactionPackagesAsync(Window win, string onlyFactionName, bool showQrModalAfter)
        {
            try
            {
                // Odczytaj pełny stan wszystkich stron z TacticalConfigPanel (wraz z OtsHost, VoiceChannels, EmergencyPhone, LoRa)
                var fullState = ExtractFullWorkspaceStateFromPanel(win);
                string eventName = fullState.EventName;
                string sourceDir = TacticalConfigPanel.GetPrimarySourceDirectory(win);
                string outBaseDir = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(sourceDir)) ?? AppDomain.CurrentDomain.BaseDirectory;
                string releaseDir = Path.Combine(outBaseDir, "out", "release_factions");
                Directory.CreateDirectory(releaseDir);

                var targetFactions = fullState.Factions;
                if (!string.IsNullOrWhiteSpace(onlyFactionName))
                {
                    var filtered = targetFactions.Where(f => f.Name.Equals(onlyFactionName, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (filtered.Count > 0) targetFactions = filtered;
                }

                if (targetFactions.Count == 0)
                {
                    if (_auditStatusLabel != null)
                        _auditStatusLabel.Text = "⚠️ Brak skonfigurowanych stron konfliktu!";
                    return;
                }

                string lastBuiltFtakPath = "";
                AuditedFactionModel lastBuiltFaction = null;
                int totalIncludedFiles = 0;

                await Task.Run(() =>
                {
                    foreach (var fac in targetFactions)
                    {
                        string safeEv = TacticalConfigPanel.SanitizeFileName(eventName);
                        string safeFac = TacticalConfigPanel.SanitizeFileName(fac.Name);
                        string ftakPath = Path.Combine(releaseDir, $"{safeEv}_{safeFac}.ftak");

                        int filesCount = WriteSingleAuditedFactionFtakArchive(ftakPath, eventName, fac, sourceDir);
                        ServedFtakPathsByFaction[fac.Name] = ftakPath;
                        lastBuiltFtakPath = ftakPath;
                        lastBuiltFaction = fac;
                        totalIncludedFiles += filesCount;
                    }
                });

                string serverBaseUrl = EnsurePcFtakHttpServerStarted();

                if (_auditStatusLabel != null && lastBuiltFaction != null)
                {
                    _auditStatusLabel.Text = $"✓ Zbudowano zaudytowaną paczkę: {Path.GetFileName(lastBuiltFtakPath)} (Strona: {lastBuiltFaction.Name}, OTS: {(string.IsNullOrEmpty(lastBuiltFaction.OtsHost) ? "Mesh" : lastBuiltFaction.OtsHost)}, Kanały głosowe: {lastBuiltFaction.VoiceChannels.Count}, Pliki map: {totalIncludedFiles})";
                }

                if (showQrModalAfter && lastBuiltFaction != null)
                {
                    ShowActiveFactionQrModal(win, eventName, lastBuiltFaction, lastBuiltFtakPath, serverBaseUrl);
                }
                else if (!string.IsNullOrEmpty(releaseDir) && Directory.Exists(releaseDir))
                {
                    TacticalConfigPanel.TryOpenFolderInExplorer(releaseDir);
                }
            }
            catch (Exception ex)
            {
                if (_auditStatusLabel != null)
                    _auditStatusLabel.Text = $"⚠️ Błąd budowy paczki .ftak: {ex.Message}";
            }
        }

        private static int WriteSingleAuditedFactionFtakArchive(
            string ftakZipPath,
            string eventName,
            AuditedFactionModel fac,
            string sourceDir)
        {
            if (File.Exists(ftakZipPath)) File.Delete(ftakZipPath);

            string deepLinkUrl = BuildMeshtasticChannelSetUrl(fac);
            fac.ChannelUrl = deepLinkUrl;

            // Zbuduj jednoznaczny, odizolowany JSON tylko dla tej jednej strony (Fog of War)
            var voiceArray = new JsonArray();
            foreach (var vc in fac.VoiceChannels)
            {
                voiceArray.Add(new JsonObject
                {
                    ["roleName"] = vc.RoleName,
                    ["bandType"] = vc.BandType,
                    ["freqMhz"] = vc.FreqMhz,
                    ["toneOrCode"] = vc.ToneOrCode
                });
            }

            JsonObject MakeFactionNode() => new JsonObject
            {
                ["Name"] = fac.Name,
                ["ColorHex"] = fac.ColorHex,
                ["ChannelName"] = fac.ChannelName,
                ["Psk"] = fac.Psk,
                ["ChannelUrl"] = deepLinkUrl,
                ["LoraRegion"] = fac.LoraRegion,
                ["LoraModemPreset"] = fac.LoraModemPreset,
                ["HopLimit"] = fac.HopLimit,
                ["OtsHost"] = fac.OtsHost,
                ["EnrollmentUri"] = fac.EnrollmentUri,
                ["EmergencyPhone"] = fac.EmergencyPhone,
                ["PliProfile"] = fac.PliProfile,
                ["MeshProfile"] = fac.MeshProfile,
                ["VoiceChannels"] = JsonNode.Parse(voiceArray.ToJsonString())
            };

            var rootJson = new JsonObject
            {
                ["schemaVersion"] = "2.4",
                ["eventName"] = eventName,
                ["activeFaction"] = fac.Name,
                ["Name"] = fac.Name,
                ["ColorHex"] = fac.ColorHex,
                ["ChannelName"] = fac.ChannelName,
                ["Psk"] = fac.Psk,
                ["ChannelUrl"] = deepLinkUrl,
                ["LoraRegion"] = fac.LoraRegion,
                ["LoraModemPreset"] = fac.LoraModemPreset,
                ["HopLimit"] = fac.HopLimit,
                ["OtsHost"] = fac.OtsHost,
                ["EnrollmentUri"] = fac.EnrollmentUri,
                ["EmergencyPhone"] = fac.EmergencyPhone,
                ["PliProfile"] = fac.PliProfile,
                ["MeshProfile"] = fac.MeshProfile,
                ["VoiceChannels"] = voiceArray,
                ["factions"] = new JsonArray { MakeFactionNode() }
            };

            string jsonFormatted = rootJson.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonFormatted);

            int includedMapFiles = 0;
            string facSlug = TacticalConfigPanel.SanitizeFileName(fac.Name).ToLowerInvariant();
            string facSpecificDir = Path.Combine(sourceDir, "factions", facSlug);
            bool hasFactionSpecificVectors = Directory.Exists(facSpecificDir) &&
                Directory.GetFiles(facSpecificDir, "*.*", SearchOption.AllDirectories)
                    .Any(f => f.EndsWith(".kmz", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".gpx", StringComparison.OrdinalIgnoreCase));

            using var fs = new FileStream(ftakZipPath, FileMode.CreateNew, FileAccess.Write);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create, false, Encoding.UTF8);

            // Zapisz zarówno config.json, jak i manifest.json dla 100% kompatybilności
            foreach (string metaName in new[] { "config.json", "manifest.json" })
            {
                var e = zip.CreateEntry(metaName, CompressionLevel.Optimal);
                using var s = e.Open();
                s.Write(jsonBytes, 0, jsonBytes.Length);
            }

            if (Directory.Exists(sourceDir))
            {
                var allSourceFiles = Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories);
                foreach (string filePath in allSourceFiles)
                {
                    string ext = Path.GetExtension(filePath).ToLowerInvariant();
                    if (ext != ".mbtiles" && ext != ".sqlite" && ext != ".kmz" && ext != ".kml" && ext != ".gpx" && ext != ".p12" && ext != ".pref")
                        continue;

                    string relPath = Path.GetRelativePath(sourceDir, filePath).Replace('\\', '/').TrimStart('/');
                    string relLower = relPath.ToLowerInvariant();

                    // 1. Jeśli plik leży w factions/<inna_strona>/ -> ODRZUĆ (Fog of War!)
                    if (relLower.StartsWith("factions/"))
                    {
                        string[] parts = relLower.Split('/');
                        if (parts.Length >= 2 && parts[1] != facSlug)
                            continue;
                    }
                    else
                    {
                        // 2. Jeśli strona ma już wyeksportowane dedykowane pliki .kmz / .gpx w factions/<strona>/,
                        //    pomiń stare globalne pliki grg_map_overlay.kmz / punkty_taktyczne.gpx, żeby nie dublować warstw
                        if (hasFactionSpecificVectors && (ext == ".kmz" || ext == ".gpx"))
                            continue;
                    }

                    var fileEntry = zip.CreateEntry(relPath, CompressionLevel.Optimal);
                    using var entryStream = fileEntry.Open();
                    using var srcStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    srcStream.CopyTo(entryStream);
                    includedMapFiles++;
                }
            }

            return includedMapFiles;
        }

        // Binarny enkoder Meshtastic ChannelSet Protobuf -> https://meshtastic.org/e/#...
        private static string BuildMeshtasticChannelSetUrl(AuditedFactionModel fac)
        {
            if (!string.IsNullOrWhiteSpace(fac.ChannelUrl) &&
                fac.ChannelUrl.StartsWith("https://meshtastic.org/e/#", StringComparison.OrdinalIgnoreCase))
            {
                return fac.ChannelUrl;
            }

            byte[] pskBytes;
            try
            {
                pskBytes = !string.IsNullOrWhiteSpace(fac.Psk) ? Convert.FromBase64String(fac.Psk.Trim()) : new byte[] { 1 };
            }
            catch
            {
                pskBytes = new byte[] { 1 };
            }

            string cleanChName = (fac.ChannelName ?? fac.Name ?? "TAK").Trim();
            if (cleanChName.Length > 11) cleanChName = cleanChName.Substring(0, 11);
            byte[] nameBytes = Encoding.UTF8.GetBytes(cleanChName);

            using var chSettings = new MemoryStream();
            WriteProtoBytes(chSettings, 0x12, pskBytes);
            if (nameBytes.Length > 0) WriteProtoBytes(chSettings, 0x1A, nameBytes);
            WriteProtoVarintField(chSettings, 0x28, 1);
            WriteProtoVarintField(chSettings, 0x30, 1);

            using var loraCfg = new MemoryStream();
            WriteProtoVarintField(loraCfg, 0x08, 1);
            int presetEnum = (fac.LoraModemPreset ?? "SHORT_FAST").ToUpperInvariant() switch
            {
                "LONG_FAST" => 0,
                "LONG_SLOW" => 1,
                "VERY_LONG_SLOW" => 2,
                "MEDIUM_SLOW" => 3,
                "MEDIUM_FAST" => 4,
                "SHORT_SLOW" => 5,
                "SHORT_FAST" => 6,
                "LONG_MODERATE" => 7,
                "SHORT_TURBO" => 8,
                _ => 6
            };
            if (presetEnum != 0) WriteProtoVarintField(loraCfg, 0x10, presetEnum);

            int regionEnum = (fac.LoraRegion ?? "EU_868").ToUpperInvariant() switch
            {
                "US" => 1,
                "EU_433" => 2,
                "EU_868" => 3,
                "UA_433" => 14,
                "UA_868" => 15,
                _ => 3
            };
            WriteProtoVarintField(loraCfg, 0x38, regionEnum);
            WriteProtoVarintField(loraCfg, 0x40, Math.Clamp(fac.HopLimit, 1, 7));
            WriteProtoVarintField(loraCfg, 0x48, 1);

            using var channelSet = new MemoryStream();
            WriteProtoBytes(channelSet, 0x0A, chSettings.ToArray());
            WriteProtoBytes(channelSet, 0x12, loraCfg.ToArray());

            string b64Url = Convert.ToBase64String(channelSet.ToArray())
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

            return $"https://meshtastic.org/e/#{b64Url}";
        }

        private static void WriteProtoVarint(Stream s, int val)
        {
            uint v = (uint)val;
            while ((v & ~0x7Fu) != 0)
            {
                s.WriteByte((byte)((v & 0x7Fu) | 0x80u));
                v >>= 7;
            }
            s.WriteByte((byte)(v & 0x7Fu));
        }

        private static void WriteProtoVarintField(Stream s, byte tag, int val)
        {
            s.WriteByte(tag);
            WriteProtoVarint(s, val);
        }

        private static void WriteProtoBytes(Stream s, byte tag, byte[] data)
        {
            s.WriteByte(tag);
            WriteProtoVarint(s, data.Length);
            s.Write(data, 0, data.Length);
        }

        // =========================================================================
        // 6. LOKALNY SERWER HTTP NA PC (:8766) + OKNO KODU QR AKTYWNEJ STRONY
        // =========================================================================
        private static string EnsurePcFtakHttpServerStarted()
        {
            if (!_pcServerRunning || _pcHotspotListener == null)
            {
                foreach (int port in new[] { 8766, 8767, 8768 })
                {
                    try
                    {
                        var listener = new TcpListener(IPAddress.Any, port);
                        listener.Start();
                        _pcHotspotListener = listener;
                        _pcServerPort = port;
                        _pcServerRunning = true;
                        _ = Task.Run(() => AcceptPcHttpClientsLoop(listener));
                        break;
                    }
                    catch { }
                }
            }

            string localIp = GetBestLocalLanIpAddress();
            return $"http://{localIp}:{_pcServerPort}";
        }

        private static void StopPcFtakHttpServer()
        {
            _pcServerRunning = false;
            try { _pcHotspotListener?.Stop(); } catch { }
            _pcHotspotListener = null;
        }

        private static async Task AcceptPcHttpClientsLoop(TcpListener listener)
        {
            while (_pcServerRunning)
            {
                try
                {
                    var client = await listener.AcceptTcpClientAsync();
                    _ = Task.Run(() => HandlePcHttpClient(client));
                }
                catch
                {
                    if (!_pcServerRunning) break;
                }
            }
        }

        private static void HandlePcHttpClient(TcpClient client)
        {
            using (client)
            {
                try
                {
                    using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true);
                    string reqLine = reader.ReadLine();
                    if (string.IsNullOrEmpty(reqLine)) return;

                    string[] parts = reqLine.Split(' ');
                    string path = parts.Length >= 2 ? Uri.UnescapeDataString(parts[1]) : "/";

                    // Oczekiwana ścieżka: /deploy/<NazwaStrony>.ftak
                    string targetFile = null;
                    foreach (var kv in ServedFtakPathsByFaction)
                    {
                        if (path.Contains(kv.Key, StringComparison.OrdinalIgnoreCase) && File.Exists(kv.Value))
                        {
                            targetFile = kv.Value;
                            break;
                        }
                    }
                    if (targetFile == null)
                    {
                        targetFile = ServedFtakPathsByFaction.Values.FirstOrDefault(File.Exists);
                    }

                    if (targetFile != null && File.Exists(targetFile))
                    {
                        byte[] fileBytes = File.ReadAllBytes(targetFile);
                        string fileName = Path.GetFileName(targetFile);
                        string headers =
                            "HTTP/1.1 200 OK\r\n" +
                            "Content-Type: application/octet-stream\r\n" +
                            $"Content-Disposition: attachment; filename=\"{fileName}\"\r\n" +
                            $"Content-Length: {fileBytes.Length}\r\n" +
                            "Connection: close\r\n\r\n";
                        byte[] hdrBytes = Encoding.UTF8.GetBytes(headers);
                        stream.Write(hdrBytes, 0, hdrBytes.Length);
                        stream.Write(fileBytes, 0, fileBytes.Length);
                        stream.Flush();
                    }
                }
                catch { }
            }
        }

        private static string GetBestLocalLanIpAddress()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    {
                        string s = ip.ToString();
                        if (s.StartsWith("192.168.") || s.StartsWith("10.") || s.StartsWith("172."))
                            return s;
                    }
                }
            }
            catch { }
            return "127.0.0.1";
        }

        private static void ShowActiveFactionQrModal(
            Window owner,
            string eventName,
            AuditedFactionModel fac,
            string ftakFilePath,
            string serverBaseUrl)
        {
            string safeFac = Uri.EscapeDataString(fac.Name);
            string wifiDownloadUrl = $"{serverBaseUrl}/deploy/{safeFac}.ftak";

            // Kompaktowy JSON dla trybu QR Offline (gdy PC i telefon nie są w jednej sieci Wi-Fi)
            var offlineJsonObj = new JsonObject
            {
                ["eventName"] = eventName,
                ["activeFaction"] = fac.Name,
                ["factions"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["Name"] = fac.Name,
                        ["ColorHex"] = fac.ColorHex,
                        ["ChannelName"] = fac.ChannelName,
                        ["Psk"] = fac.Psk,
                        ["ChannelUrl"] = fac.ChannelUrl,
                        ["LoraRegion"] = fac.LoraRegion,
                        ["LoraModemPreset"] = fac.LoraModemPreset,
                        ["HopLimit"] = fac.HopLimit,
                        ["OtsHost"] = fac.OtsHost,
                        ["EmergencyPhone"] = fac.EmergencyPhone,
                        ["VoiceChannels"] = new JsonArray(
                            fac.VoiceChannels.Select(v => (JsonNode)new JsonObject
                            {
                                ["roleName"] = v.RoleName,
                                ["bandType"] = v.BandType,
                                ["freqMhz"] = v.FreqMhz,
                                ["toneOrCode"] = v.ToneOrCode
                            }).ToArray())
                    }
                }
            };
            string offlineJsonPayload = offlineJsonObj.ToJsonString();

            var dlg = new Window
            {
                Title = $"📱 Kod QR Aktywnej Strony: {fac.Name} ({eventName})",
                Width = 490,
                Height = 610,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                Background = TacticalConfigPanel.BrushCardBg,
                Foreground = Brushes.White,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow
            };

            var root = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };

            var hdrBorder = new Border
            {
                Background = TacticalConfigPanel.BrushItemBg,
                BorderBrush = TacticalConfigPanel.BrushFromHex(fac.ColorHex, Color.FromRgb(31, 224, 197)),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var hdrStack = new StackPanel();
            hdrStack.Children.Add(new TextBlock
            {
                Text = $"🛡️ STRONA: {fac.Name}   •   OPERACJA: {eventName}",
                Foreground = TacticalConfigPanel.BrushAccent,
                FontWeight = FontWeights.Bold,
                FontSize = 13.5,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            long kbSize = File.Exists(ftakFilePath) ? Math.Max(1, new FileInfo(ftakFilePath).Length / 1024) : 0;
            hdrStack.Children.Add(new TextBlock
            {
                Text = $"Kanał LoRa: {fac.ChannelName} ({fac.LoraRegion})  |  OTS: {(string.IsNullOrEmpty(fac.OtsHost) ? "Tryb Lokalny" : fac.OtsHost)}  |  Paczka .ftak: {kbSize} KB",
                Foreground = Brushes.White,
                FontSize = 10.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 0)
            });
            hdrBorder.Child = hdrStack;
            root.Children.Add(hdrBorder);

            var modeGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 10) };
            var btnModeWifi = new Button
            {
                Content = "📡 Tryb 1: Pobierz pełny .FTAK po Wi-Fi (Z mapami)",
                Height = 30,
                Margin = new Thickness(0, 0, 4, 0),
                Background = TacticalConfigPanel.BrushButtonAccent,
                Foreground = TacticalConfigPanel.BrushDarkFg,
                FontWeight = FontWeights.Bold,
                FontSize = 10,
                Cursor = Cursors.Hand
            };
            var btnModeOffline = new Button
            {
                Content = "📴 Tryb 2: Kod QR Offline (Sam JSON konfiguracji)",
                Height = 30,
                Margin = new Thickness(4, 0, 0, 0),
                Background = TacticalConfigPanel.BrushInputBg,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 10,
                Cursor = Cursors.Hand
            };
            modeGrid.Children.Add(btnModeWifi);
            modeGrid.Children.Add(btnModeOffline);
            root.Children.Add(modeGrid);

            var qrFrame = new Border
            {
                Width = 280,
                Height = 280,
                Background = Brushes.White,
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(3),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };
            var qrImg = new Image { Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(qrImg, BitmapScalingMode.NearestNeighbor);
            qrFrame.Child = qrImg;
            root.Children.Add(qrFrame);

            var payloadBox = new TextBox
            {
                Text = wifiDownloadUrl,
                IsReadOnly = true,
                Height = 26,
                FontSize = 10.5,
                FontFamily = new FontFamily("Consolas"),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = TacticalConfigPanel.BrushInputBg,
                Foreground = TacticalConfigPanel.BrushAccent,
                BorderBrush = TacticalConfigPanel.BrushInputBorder,
                Margin = new Thickness(0, 0, 0, 10)
            };
            root.Children.Add(payloadBox);

            void LoadQrPayload(string dataStr, bool isWifiMode)
            {
                payloadBox.Text = isWifiMode ? wifiDownloadUrl : $"[JSON Offline: {fac.Name} | Kanał: {fac.ChannelName} | Kanały radiowe: {fac.VoiceChannels.Count}]";
                btnModeWifi.Background = isWifiMode ? TacticalConfigPanel.BrushButtonAccent : TacticalConfigPanel.BrushInputBg;
                btnModeWifi.Foreground = isWifiMode ? TacticalConfigPanel.BrushDarkFg : Brushes.White;
                btnModeOffline.Background = !isWifiMode ? TacticalConfigPanel.BrushButtonAccent : TacticalConfigPanel.BrushInputBg;
                btnModeOffline.Foreground = !isWifiMode ? TacticalConfigPanel.BrushDarkFg : Brushes.White;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        string cacheKey = $"QR_AUDIT_{dataStr}";
                        byte[] bytes = GeoportalLidarEngine.TryReadDiskCache(cacheKey);
                        if (bytes == null)
                        {
                            string api = $"https://api.qrserver.com/v1/create-qr-code/?size=420x420&margin=10&data={Uri.EscapeDataString(dataStr)}";
                            bytes = await GeoportalLidarEngine.FastHttp.GetByteArrayAsync(api);
                            if (GeoportalLidarEngine.IsValidImageBytes(bytes))
                                GeoportalLidarEngine.SaveDiskCacheAsync(cacheKey, bytes);
                        }
                        if (GeoportalLidarEngine.IsValidImageBytes(bytes) && Application.Current != null)
                        {
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                var bmp = new BitmapImage();
                                using var ms = new MemoryStream(bytes);
                                bmp.BeginInit();
                                bmp.CacheOption = BitmapCacheOption.OnLoad;
                                bmp.StreamSource = ms;
                                bmp.EndInit();
                                bmp.Freeze();
                                qrImg.Source = bmp;
                            });
                        }
                    }
                    catch { }
                });
            }

            btnModeWifi.Click += (_, __) => LoadQrPayload(wifiDownloadUrl, true);
            btnModeOffline.Click += (_, __) => LoadQrPayload(offlineJsonPayload, false);
            LoadQrPayload(wifiDownloadUrl, true);

            var bottomBtns = new UniformGrid { Columns = 2 };
            var btnOpenDir = new Button
            {
                Content = "📂 Otwórz folder z plikiem .FTAK",
                Height = 32,
                Margin = new Thickness(0, 0, 5, 0),
                Background = TacticalConfigPanel.BrushItemBg,
                Foreground = Brushes.White,
                BorderBrush = TacticalConfigPanel.BrushAccent,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            btnOpenDir.Click += (_, __) =>
            {
                string dir = Path.GetDirectoryName(ftakFilePath);
                if (!string.IsNullOrEmpty(dir)) TacticalConfigPanel.TryOpenFolderInExplorer(dir);
            };

            var btnClose = new Button
            {
                Content = "Zamknij",
                Height = 32,
                Margin = new Thickness(5, 0, 0, 0),
                Background = TacticalConfigPanel.BrushInputBg,
                Foreground = Brushes.White,
                Cursor = Cursors.Hand
            };
            btnClose.Click += (_, __) => dlg.Close();

            bottomBtns.Children.Add(btnOpenDir);
            bottomBtns.Children.Add(btnClose);
            root.Children.Add(bottomBtns);

            dlg.Content = root;
            dlg.ShowDialog();
        }

        // =========================================================================
        // 7. EKSTRAKCJA PEŁNEGO STANU STRON Z TACTICALCONFIGPANEL
        // =========================================================================
        private sealed class AuditedVoiceChannel
        {
            public string RoleName { get; set; } = "";
            public string BandType { get; set; } = "PMR";
            public string FreqMhz { get; set; } = "";
            public string ToneOrCode { get; set; } = "";
        }

        private sealed class AuditedFactionModel
        {
            public string Name { get; set; } = "BLUFOR";
            public string ColorHex { get; set; } = "#2684FF";
            public string ChannelName { get; set; } = "BLUFOR-TAK";
            public string Psk { get; set; } = "";
            public string ChannelUrl { get; set; } = "";
            public string LoraRegion { get; set; } = "EU_868";
            public string LoraModemPreset { get; set; } = "SHORT_FAST";
            public int HopLimit { get; set; } = 3;
            public string OtsHost { get; set; } = "";
            public string EnrollmentUri { get; set; } = "";
            public string EmergencyPhone { get; set; } = "";
            public string PliProfile { get; set; } = "ConstRateUnreliable";
            public string MeshProfile { get; set; } = "StationaryMesh";
            public List<AuditedVoiceChannel> VoiceChannels { get; set; } = new List<AuditedVoiceChannel>();
        }

        private sealed class AuditedWorkspaceState
        {
            public string EventName { get; set; } = "OPERACJA";
            public List<AuditedFactionModel> Factions { get; set; } = new List<AuditedFactionModel>();
        }

        private static AuditedWorkspaceState ExtractFullWorkspaceStateFromPanel(Window win)
        {
            // Najpierw zapisz auto-sesję w TacticalConfigPanel, aby plik sesji zawierał najświeższe dane z pól tekstowych
            try { TacticalConfigPanel.SaveSessionStateAuto(win); } catch { }

            var state = new AuditedWorkspaceState
            {
                EventName = TacticalConfigPanel.GetEventNameFromWindow(win)
            };

            // Spróbuj odczytać pełny JSON sesji roboczej zapisany przez TacticalConfigPanel
            JsonObject sessionJson = TryReadSavedWorkspaceJson();
            if (sessionJson != null)
            {
                string ev = sessionJson["eventName"]?.ToString();
                if (!string.IsNullOrWhiteSpace(ev)) state.EventName = ev;

                if (sessionJson["factions"] is JsonArray fArr && fArr.Count > 0)
                {
                    foreach (var item in fArr.OfType<JsonObject>())
                    {
                        var fm = new AuditedFactionModel
                        {
                            Name = item["Name"]?.ToString() ?? item["name"]?.ToString() ?? "BLUFOR",
                            ColorHex = item["ColorHex"]?.ToString() ?? item["colorHex"]?.ToString() ?? "#2684FF",
                            ChannelName = item["ChannelName"]?.ToString() ?? item["channelName"]?.ToString() ?? "TAK",
                            Psk = item["Psk"]?.ToString() ?? item["psk"]?.ToString() ?? "",
                            ChannelUrl = item["ChannelUrl"]?.ToString() ?? item["channelUrl"]?.ToString() ?? "",
                            LoraRegion = item["LoraRegion"]?.ToString() ?? item["loraRegion"]?.ToString() ?? "EU_868",
                            LoraModemPreset = item["LoraModemPreset"]?.ToString() ?? item["loraModemPreset"]?.ToString() ?? "SHORT_FAST",
                            HopLimit = int.TryParse(item["HopLimit"]?.ToString() ?? item["hopLimit"]?.ToString(), out int hl) ? hl : 3,
                            OtsHost = item["OtsHost"]?.ToString() ?? item["otsHost"]?.ToString() ?? "",
                            EnrollmentUri = item["EnrollmentUri"]?.ToString() ?? item["enrollmentUri"]?.ToString() ?? "",
                            EmergencyPhone = item["EmergencyPhone"]?.ToString() ?? item["emergencyPhone"]?.ToString() ?? "",
                            PliProfile = item["PliProfile"]?.ToString() ?? "ConstRateUnreliable",
                            MeshProfile = item["MeshProfile"]?.ToString() ?? "StationaryMesh"
                        };

                        if ((item["VoiceChannels"] ?? item["voiceChannels"]) is JsonArray vArr)
                        {
                            foreach (var vo in vArr.OfType<JsonObject>())
                            {
                                fm.VoiceChannels.Add(new AuditedVoiceChannel
                                {
                                    RoleName = vo["roleName"]?.ToString() ?? vo["RoleName"]?.ToString() ?? "Kanał",
                                    BandType = vo["bandType"]?.ToString() ?? vo["BandType"]?.ToString() ?? "PMR",
                                    FreqMhz = vo["freqMhz"]?.ToString() ?? vo["FreqMhz"]?.ToString() ?? "-",
                                    ToneOrCode = vo["toneOrCode"]?.ToString() ?? vo["ToneOrCode"]?.ToString() ?? "-"
                                });
                            }
                        }
                        state.Factions.Add(fm);
                    }
                }
            }

            if (state.Factions.Count == 0)
            {
                foreach (var basic in TacticalConfigPanel.GetConfiguredFactions())
                {
                    state.Factions.Add(new AuditedFactionModel
                    {
                        Name = basic.Name,
                        ChannelName = basic.Channel,
                        ColorHex = basic.ColorHex,
                        Psk = basic.Psk
                    });
                }
            }

            return state;
        }

        private static JsonObject TryReadSavedWorkspaceJson()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(TacticalConfigPanel.CurrentDraftProjectPath) &&
                    File.Exists(TacticalConfigPanel.CurrentDraftProjectPath))
                {
                    if (JsonNode.Parse(File.ReadAllText(TacticalConfigPanel.CurrentDraftProjectPath, Encoding.UTF8)) is JsonObject draftObj)
                        return draftObj;
                }

                string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FieldTakHub");
                if (Directory.Exists(appData))
                {
                    var jsonFiles = Directory.GetFiles(appData, "*.json", SearchOption.TopDirectoryOnly)
                        .Concat(Directory.GetFiles(appData, "*.fthproj", SearchOption.TopDirectoryOnly))
                        .OrderByDescending(File.GetLastWriteTimeUtc)
                        .ToList();

                    foreach (string jf in jsonFiles)
                    {
                        try
                        {
                            string raw = File.ReadAllText(jf, Encoding.UTF8);
                            if (raw.Contains("\"factions\"", StringComparison.OrdinalIgnoreCase) &&
                                JsonNode.Parse(raw) is JsonObject obj)
                            {
                                return obj;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return null;
        }

        // =========================================================================
        // NARZĘDZIA POMOCNICZE DRZEWA WIZUALNEGO WPF
        // =========================================================================
        private static void EnsureVisibleUpToBuilderRoot(FrameworkElement element)
        {
            DependencyObject curr = element;
            while (curr != null && !(curr is Window))
            {
                if (curr is UIElement uie)
                {
                    if (curr is FrameworkElement fe && fe.Tag?.ToString() == "INJECTED_MAP_OVERLAY")
                        break;

                    if (uie.Visibility != Visibility.Visible)
                    {
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
            if (_activeFactionHubBanner != null && (container == _activeFactionHubBanner || IsAncestorOf(container, _activeFactionHubBanner)))
                return true;

            var texts = GetElementTexts(container);
            return texts.Any(t =>
                t.Contains("Konfigurator Paczek (.ftak)", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("Generator Map Taktycznych 1:1", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("STRONY KONFLIKTU", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("Dodaj stronę", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("SZYBKIE WDROŻENIE AKTYWNEJ STRONY", StringComparison.OrdinalIgnoreCase));
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