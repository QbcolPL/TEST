#pragma warning disable CS0414, CS0649, CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace FieldTakHub.Builder.Services
{
    internal enum BuilderActiveModule
    {
        PackageGenerator = 0,
        MapGenerator = 1,
        OpordGenerator = 2,
        UsbProgrammer = 3
    }

    internal static class BuilderCoreSidebarAndProjectEngine
    {
        private static readonly HashSet<int> InitializedWindows = new HashSet<int>();
        private static bool _zeroStateAppliedOnBoot;
        private static bool _isUpdatingNav;

        private static Border _injectedSidebarContainer;
        private static BuilderActiveModule _currentModule = BuilderActiveModule.PackageGenerator;

        // Referencje do oryginalnych przycisków z górnego paska (ukrytych wizualnie, ale wywoływanych z nowego menu bocznego)
        private static ButtonBase _originalBuilderTabButton;
        private static ButtonBase _originalMapTabButton;
        private static ButtonBase _originalUsbProgrammerButton;

        private static Button _navBtnPackages;
        private static Button _navBtnMaps;
        private static Button _navBtnOpord;
        private static Button _navBtnUsb;

        private static Button _projBtnNew;
        private static Button _projBtnOpen;
        private static Button _projBtnSave;
        private static Button _projBtnSourceDir;
        private static Button _langSwitchBtn;

        private static TextBlock _sidebarTitleBlock;
        private static TextBlock _sidebarSubtitleBlock;
        private static TextBlock _navGroupHeader;
        private static TextBlock _projGroupHeader;
        private static TextBlock _activeProjectFileLabel;

        public static string CurrentProjectFilePath { get; private set; } = "";

        public static void Attach(Window win)
        {
            if (win == null) return;

            if (!_zeroStateAppliedOnBoot)
            {
                _zeroStateAppliedOnBoot = true;
                ApplyZeroStateOnApplicationStartup(win);
            }

            int hash = win.GetHashCode();
            if (InitializedWindows.Add(hash))
            {
                BuilderI18n.LanguageChanged += _ => RefreshSidebarLocalization(win);
                win.Loaded += (_, __) => EnsureSidebarAndCleanLayout(win);
                win.ContentRendered += (_, __) => EnsureSidebarAndCleanLayout(win);
            }

            EnsureSidebarAndCleanLayout(win);
        }

        // =========================================================================
        // 1. START ZEROWY (ZERO-STATE) + ZARZĄDZANIE PROJEKTEM (NOWY / OTWÓRZ / ZAPISZ)
        // =========================================================================
        private static void ApplyZeroStateOnApplicationStartup(Window win)
        {
            try
            {
                string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FieldTakHub");
                if (Directory.Exists(appData))
                {
                    string[] staleDrafts = Directory.GetFiles(appData, "*.json", SearchOption.TopDirectoryOnly);
                    for (int i = 0; i < staleDrafts.Length; i++)
                    {
                        try { File.Delete(staleDrafts[i]); } catch { }
                    }
                }

                WriteCleanZeroStateDraftFile();
                EnsureUnifiedSourceDirectoryStructure(win);
            }
            catch { }
        }

        public static void EnsureUnifiedSourceDirectoryStructure(Window win)
        {
            try
            {
                string sourceDir = TacticalConfigPanel.GetPrimarySourceDirectory(win);
                Directory.CreateDirectory(sourceDir);
                Directory.CreateDirectory(Path.Combine(sourceDir, "apk"));
                Directory.CreateDirectory(Path.Combine(sourceDir, "plugins"));
                Directory.CreateDirectory(Path.Combine(sourceDir, "datapackage"));
                Directory.CreateDirectory(Path.Combine(sourceDir, "maps"));
                Directory.CreateDirectory(Path.Combine(sourceDir, "certs"));
                Directory.CreateDirectory(Path.Combine(sourceDir, "factions"));

                string outBaseDir = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(sourceDir)) ?? AppDomain.CurrentDomain.BaseDirectory;
                string outDir = Path.Combine(outBaseDir, "out");
                if (Directory.Exists(outDir))
                {
                    string[] legacyDirs = new string[] { "release_factions", "temp", "staging", "work", "cache", "legacy" };
                    for (int i = 0; i < legacyDirs.Length; i++)
                    {
                        string ld = Path.Combine(outDir, legacyDirs[i]);
                        if (Directory.Exists(ld))
                        {
                            try { Directory.Delete(ld, true); } catch { }
                        }
                    }
                }
            }
            catch { }
        }

        private static string WriteCleanZeroStateDraftFile()
        {
            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FieldTakHub");
            Directory.CreateDirectory(appData);
            string zeroDraftPath = Path.Combine(appData, "zero_state_workspace.fthproj");

            var zeroJson = new JsonObject
            {
                ["schemaVersion"] = "3.1",
                ["eventName"] = "NOWA OPERACJA",
                ["factions"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["Name"] = "BLUFOR",
                        ["ColorHex"] = "#2684FF",
                        ["ChannelName"] = "BLUFOR-MESH",
                        ["Psk"] = "",
                        ["ChannelUrl"] = "",
                        ["LoraRegion"] = "EU_868",
                        ["LoraModemPreset"] = "SHORT_FAST",
                        ["HopLimit"] = 3,
                        ["OtsHost"] = "",
                        ["EnrollmentUri"] = "",
                        ["EmergencyPhone"] = "",
                        ["PliProfile"] = "ConstRateUnreliable",
                        ["MeshProfile"] = "StationaryMesh",
                        ["VoiceChannels"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["roleName"] = "Dowodzenie HQ",
                                ["bandType"] = "PMR",
                                ["freqMhz"] = "CH 1 (446.00625)",
                                ["toneOrCode"] = "CTCSS 67.0 Hz"
                            }
                        }
                    },
                    new JsonObject
                    {
                        ["Name"] = "OPFOR",
                        ["ColorHex"] = "#EB4034",
                        ["ChannelName"] = "OPFOR-MESH",
                        ["Psk"] = "",
                        ["ChannelUrl"] = "",
                        ["LoraRegion"] = "EU_868",
                        ["LoraModemPreset"] = "SHORT_FAST",
                        ["HopLimit"] = 3,
                        ["OtsHost"] = "",
                        ["EnrollmentUri"] = "",
                        ["EmergencyPhone"] = "",
                        ["PliProfile"] = "ConstRateUnreliable",
                        ["MeshProfile"] = "StationaryMesh",
                        ["VoiceChannels"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["roleName"] = "Dowodzenie OPFOR",
                                ["bandType"] = "PMR",
                                ["freqMhz"] = "CH 5 (446.05625)",
                                ["toneOrCode"] = "CTCSS 100.0 Hz"
                            }
                        }
                    }
                }
            };

            File.WriteAllText(zeroDraftPath, zeroJson.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
            TacticalConfigPanel.CurrentDraftProjectPath = zeroDraftPath;
            return zeroDraftPath;
        }

        public static void CreateNewZeroProjectInteractive(Window win)
        {
            var res = MessageBox.Show(
                win,
                BuilderI18n.T(
                    "Czy na pewno chcesz utworzyć całkowicie NOWY PROJEKT?\nWszystkie niezapisane zmiany w bieżącym projekcie zostaną wyzerowane do ustawień początkowych.",
                    "Are you sure you want to start a completely NEW PROJECT?\nAll unsaved changes in the current workspace will be reset to zero-state defaults."),
                BuilderI18n.T("Nowy Projekt — Reset do stanu zerowego", "New Project — Reset to Zero-State"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res != MessageBoxResult.Yes) return;

            CurrentProjectFilePath = "";
            string zeroPath = WriteCleanZeroStateDraftFile();
            TryReloadProjectIntoPanel(win, zeroPath);
            ResetMainWindowTextBoxesToZeroState(win);

            if (_activeProjectFileLabel != null)
            {
                _activeProjectFileLabel.Text = BuilderI18n.T("Projekt: Nowy (Stan zerowy)", "Project: New (Zero-State)");
            }

            SwitchActiveModule(win, BuilderActiveModule.PackageGenerator);
        }

        public static void OpenProjectInteractive(Window win)
        {
            var dlg = new OpenFileDialog
            {
                Title = BuilderI18n.T("Otwórz projekt Field TAK Hub (.fthproj / .json)", "Open Field TAK Hub Project (.fthproj / .json)"),
                Filter = "Field TAK Hub Project (*.fthproj;*.json)|*.fthproj;*.json|All Files (*.*)|*.*"
            };

            if (dlg.ShowDialog(win) == true && File.Exists(dlg.FileName))
            {
                CurrentProjectFilePath = dlg.FileName;
                TacticalConfigPanel.CurrentDraftProjectPath = dlg.FileName;
                TryReloadProjectIntoPanel(win, dlg.FileName);

                if (_activeProjectFileLabel != null)
                {
                    _activeProjectFileLabel.Text = BuilderI18n.T("Projekt: ", "Project: ") + Path.GetFileName(dlg.FileName);
                }
            }
        }

        public static void SaveProjectInteractive(Window win)
        {
            try { TacticalConfigPanel.SaveSessionStateAuto(win); } catch { }
            try { OpordBuilderManager.SaveAllOpordsToDisk(win); } catch { }

            string evName = TacticalConfigPanel.GetEventNameFromWindow(win);
            string safeEv = TacticalConfigPanel.SanitizeFileName(string.IsNullOrWhiteSpace(evName) ? "operacja" : evName);

            var dlg = new SaveFileDialog
            {
                Title = BuilderI18n.T("Zapisz projekt roboczy (.fthproj)", "Save Workspace Project (.fthproj)"),
                Filter = "Field TAK Hub Project (*.fthproj)|*.fthproj|JSON (*.json)|*.json",
                FileName = safeEv + ".fthproj"
            };

            if (dlg.ShowDialog(win) == true)
            {
                CurrentProjectFilePath = dlg.FileName;
                var snap = CleanFactionPackageEngine.ExtractWorkspaceSnapshot(win);

                var fArr = new JsonArray();
                foreach (var fac in snap.Factions)
                {
                    var vArr = new JsonArray();
                    foreach (var vc in fac.VoiceChannels)
                    {
                        vArr.Add(new JsonObject
                        {
                            ["roleName"] = vc.RoleName,
                            ["bandType"] = vc.BandType,
                            ["freqMhz"] = vc.FreqMhz,
                            ["toneOrCode"] = vc.ToneOrCode
                        });
                    }

                    fArr.Add(new JsonObject
                    {
                        ["Name"] = fac.Name,
                        ["ColorHex"] = fac.ColorHex,
                        ["ChannelName"] = fac.ChannelName,
                        ["Psk"] = fac.Psk,
                        ["ChannelUrl"] = CleanFactionPackageEngine.BuildMeshtasticChannelSetUrl(fac),
                        ["LoraRegion"] = fac.LoraRegion,
                        ["LoraModemPreset"] = fac.LoraModemPreset,
                        ["HopLimit"] = fac.HopLimit,
                        ["OtsHost"] = fac.OtsHost,
                        ["EnrollmentUri"] = fac.EnrollmentUri,
                        ["EmergencyPhone"] = fac.EmergencyPhone,
                        ["PliProfile"] = fac.PliProfile,
                        ["MeshProfile"] = fac.MeshProfile,
                        ["VoiceChannels"] = vArr
                    });
                }

                var rootObj = new JsonObject
                {
                    ["schemaVersion"] = "3.1",
                    ["savedAtUtc"] = DateTime.UtcNow.ToString("o"),
                    ["eventName"] = snap.EventName,
                    ["factions"] = fArr
                };

                File.WriteAllText(dlg.FileName, rootObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
                TacticalConfigPanel.CurrentDraftProjectPath = dlg.FileName;

                if (_activeProjectFileLabel != null)
                {
                    _activeProjectFileLabel.Text = BuilderI18n.T("Projekt: ", "Project: ") + Path.GetFileName(dlg.FileName);
                }

                MessageBox.Show(
                    win,
                    BuilderI18n.T("Pomyślnie zapisano projekt:\n" + dlg.FileName, "Project saved successfully:\n" + dlg.FileName),
                    BuilderI18n.T("Zapisano projekt", "Project Saved"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private static void TryReloadProjectIntoPanel(Window win, string filePath)
        {
            try
            {
                var panelType = typeof(TacticalConfigPanel);
                var methods = panelType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                for (int i = 0; i < methods.Length; i++)
                {
                    var m = methods[i];
                    if (m.Name.Contains("Load", StringComparison.OrdinalIgnoreCase) && m.GetParameters().Length == 2)
                    {
                        var p0 = m.GetParameters()[0].ParameterType;
                        var p1 = m.GetParameters()[1].ParameterType;
                        if (p0 == typeof(Window) && p1 == typeof(string))
                        {
                            m.Invoke(null, new object[] { win, filePath });
                            return;
                        }
                        if (p0 == typeof(string) && p1 == typeof(Window))
                        {
                            m.Invoke(null, new object[] { filePath, win });
                            return;
                        }
                    }
                }
            }
            catch { }
        }

        private static void ResetMainWindowTextBoxesToZeroState(Window win)
        {
            foreach (var tb in GetAllVisualDescendants(win).OfType<TextBox>())
            {
                string val = (tb.Text ?? "").Trim();
                if (val.Contains("ggzstak", StringComparison.OrdinalIgnoreCase) ||
                    val.Contains("GGZS", StringComparison.OrdinalIgnoreCase))
                {
                    tb.Text = "";
                }
            }
        }

        // =========================================================================
        // 2. BEZPOŚREDNIA PODMIANA STAREGO PASKA BOCZNEGO (IN-PLACE) I USUNIĘCIE GÓRNEGO PASKA USB
        // =========================================================================
        private static void EnsureSidebarAndCleanLayout(Window win)
        {
            if (_isUpdatingNav) return;
            _isUpdatingNav = true;
            try
            {
                ResetMainWindowTextBoxesToZeroState(win);

                var allElements = GetAllVisualDescendants(win).ToList();

                // KROK A: Zapisz referencje do oryginalnych przycisków górnych i ukryj cały górny pasek (w tym "Masowy Programator USB (LoRa)")
                CaptureAndCollapseTopMenuBar(allElements);

                // KROK B: Znajdź stary pasek boczny (Paczka .FTAK / Serwer / Meshtastic / ATAK / Certyfikaty TAK Server / Ustawienia)
                //         i podmień jego zawartość bezpośrednio w miejscu (in-place)!
                ReplaceLegacySidebarInPlace(win, allElements);
            }
            catch { }
            finally
            {
                _isUpdatingNav = false;
            }
        }

        private static void CaptureAndCollapseTopMenuBar(List<DependencyObject> allElements)
        {
            foreach (var btn in allElements.OfType<ButtonBase>())
            {
                if (_injectedSidebarContainer != null && IsDescendantOf(_injectedSidebarContainer, btn))
                    continue;

                string txt = GetCombinedText(btn);

                if (txt.Contains("Konfigurator Paczek", StringComparison.OrdinalIgnoreCase) ||
                    txt.Contains("Faction & Package Builder", StringComparison.OrdinalIgnoreCase))
                {
                    _originalBuilderTabButton = btn;
                }
                else if (txt.Contains("Generator Map Taktycznych", StringComparison.OrdinalIgnoreCase) ||
                         txt.Contains("1:1 Tactical Map", StringComparison.OrdinalIgnoreCase))
                {
                    _originalMapTabButton = btn;
                }
                else if (txt.Contains("Masowy Programator USB", StringComparison.OrdinalIgnoreCase) ||
                         txt.Contains("Programator USB", StringComparison.OrdinalIgnoreCase) ||
                         txt.Contains("Bulk USB LoRa", StringComparison.OrdinalIgnoreCase))
                {
                    _originalUsbProgrammerButton = btn;
                }

                bool isTopBarButton =
                    txt.Contains("Masowy Programator USB", StringComparison.OrdinalIgnoreCase) ||
                    txt.Contains("Programator USB", StringComparison.OrdinalIgnoreCase) ||
                    txt.Contains("Bulk USB LoRa", StringComparison.OrdinalIgnoreCase) ||
                    txt.Contains("Konfigurator Paczek", StringComparison.OrdinalIgnoreCase) ||
                    txt.Contains("Faction & Package Builder", StringComparison.OrdinalIgnoreCase) ||
                    txt.Contains("Generator Map Taktycznych", StringComparison.OrdinalIgnoreCase) ||
                    txt.Contains("1:1 Tactical Map Studio", StringComparison.OrdinalIgnoreCase) ||
                    txt.Contains("Generator Rozkazu (OPORD)", StringComparison.OrdinalIgnoreCase) ||
                    txt.Contains("NATO OPORD Builder", StringComparison.OrdinalIgnoreCase) ||
                    txt.Contains("Zapisz projekt roboczy", StringComparison.OrdinalIgnoreCase) ||
                    txt.Contains("Wczytaj projekt", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(btn.Tag?.ToString(), "INJECTED_OPORD_TAB_BTN", StringComparison.Ordinal) ||
                    string.Equals(btn.Tag?.ToString(), "INJECTED_LANG_BTN", StringComparison.Ordinal);

                if (isTopBarButton)
                {
                    // Ważne: zachowaj IsEnabled = true, aby wywołanie RaiseEvent(ClickEvent) z nowego paska bocznego działało!
                    btn.Visibility = Visibility.Collapsed;
                    btn.Width = 0;
                    btn.Height = 0;
                    btn.Margin = new Thickness(0);
                    btn.Padding = new Thickness(0);

                    // Jeśli przycisk leży w poziomym pasku górnym (np. Border/StackPanel/DockPanel nad głównym widokiem), zwiń też sam pasek
                    DependencyObject parent = VisualTreeHelper.GetParent(btn);
                    if (parent is Panel p && (_injectedSidebarContainer == null || !IsDescendantOf(p, _injectedSidebarContainer)))
                    {
                        bool allChildrenAreTopBarButtons = true;
                        foreach (UIElement ch in p.Children)
                        {
                            if (ch is FrameworkElement fe && fe.Visibility == Visibility.Visible && fe.ActualHeight > 5)
                            {
                                string cTxt = GetCombinedText(fe);
                                if (!cTxt.Contains("Programator USB", StringComparison.OrdinalIgnoreCase) &&
                                    !cTxt.Contains("Konfigurator Paczek", StringComparison.OrdinalIgnoreCase) &&
                                    !cTxt.Contains("Generator Map", StringComparison.OrdinalIgnoreCase) &&
                                    !cTxt.Contains("OPORD", StringComparison.OrdinalIgnoreCase) &&
                                    !cTxt.Contains("projekt", StringComparison.OrdinalIgnoreCase) &&
                                    !cTxt.Contains("Wesprzyj", StringComparison.OrdinalIgnoreCase))
                                {
                                    allChildrenAreTopBarButtons = false;
                                    break;
                                }
                            }
                        }
                        if (allChildrenAreTopBarButtons)
                        {
                            p.Visibility = Visibility.Collapsed;
                            p.Height = 0;
                            p.Margin = new Thickness(0);
                            if (VisualTreeHelper.GetParent(p) is Border topBarBorder && topBarBorder.ActualHeight < 90)
                            {
                                topBarBorder.Visibility = Visibility.Collapsed;
                                topBarBorder.Height = 0;
                                topBarBorder.Margin = new Thickness(0);
                                topBarBorder.Padding = new Thickness(0);
                            }
                        }
                    }
                }
            }
        }

        private static void ReplaceLegacySidebarInPlace(Window win, List<DependencyObject> allElements)
        {
            if (_injectedSidebarContainer != null && IsDescendantOf(win, _injectedSidebarContainer))
            {
                HideRemainingLegacySidebarItems(allElements);
                return;
            }

            // 1. Znajdź TextBlock należący do starego paska bocznego (ze zrzutu ekranu):
            //    "Certyfikaty TAK Server", "Konfiguracja radia i sieci", "Utwórz paczkę dla uczestników", "Preferencje aplikacji"
            TextBlock anchorTb = null;
            string[] sidebarAnchors = new string[]
            {
                "Certyfikaty TAK Server",
                "Konfiguracja radia i sieci",
                "Utwórz paczkę dla uczestników",
                "Konfiguracja połączenia i Data Packages",
                "Profile i ustawienia",
                "Preferencje aplikacji"
            };

            foreach (var tb in allElements.OfType<TextBlock>())
            {
                string t = (tb.Text ?? "").Trim();
                for (int i = 0; i < sidebarAnchors.Length; i++)
                {
                    if (t.Contains(sidebarAnchors[i], StringComparison.OrdinalIgnoreCase))
                    {
                        anchorTb = tb;
                        break;
                    }
                }
                if (anchorTb != null) break;
            }

            Border newSidebar = BuildTacticalSidebarControl(win);
            _injectedSidebarContainer = newSidebar;

            if (anchorTb != null)
            {
                // Wędruj w górę od napisu "Certyfikaty TAK Server" do głównego kontenera lewego paska bocznego (Border lub Panel w Kolumnie 0)
                Border outerSidebarBorder = null;
                Panel outerSidebarPanel = null;
                Grid parentTwoColumnGrid = null;
                FrameworkElement columnZeroChild = null;

                DependencyObject curr = anchorTb;
                while (curr != null && curr != win)
                {
                    DependencyObject parent = VisualTreeHelper.GetParent(curr);

                    if (curr is Border b && b.ActualWidth <= 360)
                    {
                        outerSidebarBorder = b;
                    }
                    if (curr is Panel pnl && pnl.ActualWidth <= 360)
                    {
                        outerSidebarPanel = pnl;
                    }
                    if (parent is Grid g && g.ColumnDefinitions.Count >= 2 && curr is FrameworkElement fe && Grid.GetColumn(fe) == 0)
                    {
                        parentTwoColumnGrid = g;
                        columnZeroChild = fe;
                        break;
                    }
                    curr = parent;
                }

                // Opcja 1: Mamy Grid z 2 kolumnami, w którym kolumna 0 to lewy pasek boczny
                if (parentTwoColumnGrid != null && columnZeroChild != null)
                {
                    foreach (UIElement child in parentTwoColumnGrid.Children)
                    {
                        if (Grid.GetColumn(child) == 0)
                        {
                            child.Visibility = Visibility.Collapsed;
                        }
                    }
                    parentTwoColumnGrid.ColumnDefinitions[0].Width = new GridLength(255);
                    Grid.SetColumn(newSidebar, 0);
                    Grid.SetRow(newSidebar, 0);
                    Grid.SetRowSpan(newSidebar, Math.Max(1, parentTwoColumnGrid.RowDefinitions.Count));
                    Panel.SetZIndex(newSidebar, 250);
                    parentTwoColumnGrid.Children.Add(newSidebar);
                }
                // Opcja 2: Mamy główny Border lewego paska bocznego -> podmień jego Child bezpośrednio!
                else if (outerSidebarBorder != null)
                {
                    outerSidebarBorder.Padding = new Thickness(0);
                    outerSidebarBorder.Child = newSidebar;
                }
                // Opcja 3: Mamy główny StackPanel/DockPanel lewego paska bocznego -> wyczyść stare pozycje i wstaw nowy pasek!
                else if (outerSidebarPanel != null)
                {
                    outerSidebarPanel.Children.Clear();
                    outerSidebarPanel.Children.Add(newSidebar);
                }
            }

            HideRemainingLegacySidebarItems(allElements);
            RefreshSidebarLocalization(win);
            UpdateSidebarActiveButtonHighlight();
        }

        private static void HideRemainingLegacySidebarItems(List<DependencyObject> allElements)
        {
            string[] legacyLabels = new string[]
            {
                "Paczka .FTAK",
                "Utwórz paczkę dla uczestników",
                "Konfiguracja połączenia i Data Packages",
                "Konfiguracja radia i sieci",
                "Profile i ustawienia",
                "dostępne wkrótce",
                "Certyfikaty TAK Server",
                "Preferencje aplikacji"
            };

            foreach (var tb in allElements.OfType<TextBlock>())
            {
                if (_injectedSidebarContainer != null && IsDescendantOf(_injectedSidebarContainer, tb))
                    continue;

                string t = (tb.Text ?? "").Trim();
                for (int i = 0; i < legacyLabels.Length; i++)
                {
                    if (t.Contains(legacyLabels[i], StringComparison.OrdinalIgnoreCase))
                    {
                        DependencyObject curr = tb;
                        while (curr != null && !(curr is Grid) && !(curr is Window))
                        {
                            if ((curr is ButtonBase || curr is Border || curr is StackPanel) && curr is FrameworkElement fe && fe.ActualWidth <= 350)
                            {
                                fe.Visibility = Visibility.Collapsed;
                                fe.Height = 0;
                                fe.Margin = new Thickness(0);
                            }
                            curr = VisualTreeHelper.GetParent(curr);
                        }
                        break;
                    }
                }
            }
        }

        // =========================================================================
        // 3. BUDOWA NOWEGO TAKTYCZNEGO PASKA BOCZNEGO (Z IKONAMI WEKTOROWYMI)
        // =========================================================================
        private static Border BuildTacticalSidebarControl(Window win)
        {
            var sidebarBorder = new Border
            {
                Tag = "FTH_TACTICAL_SIDEBAR_V31",
                Width = 252,
                Background = new SolidColorBrush(Color.FromRgb(6, 15, 19)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(25, 56, 68)),
                BorderThickness = new Thickness(0, 0, 1.5, 0),
                Padding = new Thickness(12, 16, 12, 14)
            };

            var mainDock = new DockPanel { LastChildFill = false };

            var topStack = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            DockPanel.SetDock(topStack, Dock.Top);

            // NAGŁÓWEK MARKI (FIELD TAK HUB)
            var brandCard = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(10, 25, 32)),
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 16)
            };

            var brandRow = new DockPanel();
            var radarIcon = CreateTacticalVectorIcon(
                "M12,2A10,10 0 1,0 22,12A10,10 0 0,0 12,2M12,4A8,8 0 0,1 19.93,11H17.91A6,6 0 0,0 13,6.09V4.07C12.67,4.03 12.34,4 12,4M11,4.07V6.09A6,6 0 0,0 6.09,11H4.07A8,8 0 0,1 11,4.07M12,8A4,4 0 0,0 8,12A4,4 0 0,0 12,16A4,4 0 0,0 16,12A4,4 0 0,0 12,8M12,10A2,2 0 0,1 14,12A2,2 0 0,1 12,14A2,2 0 0,1 10,12A2,2 0 0,1 12,10M4.07,13H6.09A6,6 0 0,0 11,17.91V19.93A8,8 0 0,1 4.07,13M17.91,13H19.93A8,8 0 0,1 13,19.93V17.91A6,6 0 0,0 17.91,13Z",
                TacticalConfigPanel.BrushAccent,
                28);
            radarIcon.Margin = new Thickness(0, 0, 10, 0);
            DockPanel.SetDock(radarIcon, Dock.Left);
            brandRow.Children.Add(radarIcon);

            var brandTextStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            _sidebarTitleBlock = new TextBlock
            {
                Text = "FIELD TAK HUB",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 14.5
            };
            _sidebarSubtitleBlock = new TextBlock
            {
                Text = "connect · deploy · operate",
                Foreground = TacticalConfigPanel.BrushAccent,
                FontWeight = FontWeights.SemiBold,
                FontSize = 10
            };
            brandTextStack.Children.Add(_sidebarTitleBlock);
            brandTextStack.Children.Add(_sidebarSubtitleBlock);
            brandRow.Children.Add(brandTextStack);
            brandCard.Child = brandRow;
            topStack.Children.Add(brandCard);

            // SEKCJA 1: 4 GŁÓWNE MODUŁY OPERACYJNE
            _navGroupHeader = new TextBlock
            {
                Text = "MODUŁY OPERACYJNE",
                Foreground = new SolidColorBrush(Color.FromRgb(120, 155, 168)),
                FontWeight = FontWeights.Bold,
                FontSize = 10,
                Margin = new Thickness(4, 0, 0, 6)
            };
            topStack.Children.Add(_navGroupHeader);

            _navBtnPackages = CreateSidebarNavButton(
                "M12,1L3,5V11C3,16.55 6.84,21.74 12,23C17.16,21.74 21,16.55 21,11V5L12,1M12,5.3L18,7.9V11.2C18,15.1 15.4,18.7 12,20C8.6,18.7 6,15.1 6,11.2V7.9L12,5.3M11,8V11H8V13H11V16H13V13H16V11H13V8H11Z",
                "Generator Paczek (.ftak)",
                () => SwitchActiveModule(win, BuilderActiveModule.PackageGenerator));
            topStack.Children.Add(_navBtnPackages);

            _navBtnMaps = CreateSidebarNavButton(
                "M15,19L9,16.89V5L15,7.11M20.5,3C20.44,3 20.39,3 20.34,3L15,5.1L9,3L3.36,4.9C3.15,4.97 3,5.15 3,5.38V20.5A0.5,0.5 0 0,0 3.5,21C3.55,21 3.61,21 3.66,20.97L9,18.9L15,21L20.64,19.1C20.85,19 21,18.85 21,18.62V3.5A0.5,0.5 0 0,0 20.5,3Z",
                "Generator Map 1:1",
                () => SwitchActiveModule(win, BuilderActiveModule.MapGenerator));
            topStack.Children.Add(_navBtnMaps);

            _navBtnOpord = CreateSidebarNavButton(
                "M19,3H14.82C14.4,1.84 13.3,1 12,1C10.7,1 9.6,1.84 9.18,3H5A2,2 0 0,0 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19V5A2,2 0 0,0 19,3M12,3A1,1 0 0,1 13,4A1,1 0 0,1 12,5A1,1 0 0,1 11,4A1,1 0 0,1 12,3M7,7H17V9H7V7M7,11H17V13H7V11M7,15H14V17H7V15Z",
                "Generator OPORD",
                () => SwitchActiveModule(win, BuilderActiveModule.OpordGenerator));
            topStack.Children.Add(_navBtnOpord);

            _navBtnUsb = CreateSidebarNavButton(
                "M9,3V5H7A2,2 0 0,0 5,7V9H3V11H5V13H3V15H5V17A2,2 0 0,0 7,19H9V21H11V19H13V21H15V19H17A2,2 0 0,0 19,17V15H21V13H19V11H21V9H19V7A2,2 0 0,0 17,5H15V3H13V5H11V3H9M7,7H17V17H7V7M9,9V15H15V9H9Z",
                "Programator Płytek USB",
                () => SwitchActiveModule(win, BuilderActiveModule.UsbProgrammer));
            topStack.Children.Add(_navBtnUsb);

            topStack.Children.Add(new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromRgb(25, 56, 68)),
                Margin = new Thickness(2, 12, 2, 12)
            });

            // SEKCJA 2: ZARZĄDZANIE PROJEKTEM
            _projGroupHeader = new TextBlock
            {
                Text = "ZARZĄDZANIE PROJEKTEM",
                Foreground = new SolidColorBrush(Color.FromRgb(120, 155, 168)),
                FontWeight = FontWeights.Bold,
                FontSize = 10,
                Margin = new Thickness(4, 0, 0, 6)
            };
            topStack.Children.Add(_projGroupHeader);

            _activeProjectFileLabel = new TextBlock
            {
                Text = "Projekt: Nowy (Stan zerowy)",
                Foreground = TacticalConfigPanel.BrushAccent,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(4, 0, 4, 8)
            };
            topStack.Children.Add(_activeProjectFileLabel);

            _projBtnNew = CreateSidebarActionButton(
                "M14,2H6A2,2 0 0,0 4,4V20A2,2 0 0,0 6,22H18A2,2 0 0,0 20,20V8L14,2M18,20H6V4H13V9H18V20M11,11V14H8V16H11V19H13V16H16V14H13V11H11Z",
                "Nowy projekt (Stan zerowy)",
                "#2ECC71",
                () => CreateNewZeroProjectInteractive(win));
            topStack.Children.Add(_projBtnNew);

            _projBtnOpen = CreateSidebarActionButton(
                "M19,20H4C2.89,20 2,19.1 2,18V6C2,4.89 2.89,4 4,4H10L12,6H19A2,2 0 0,1 21,8H21L4,8V18L6.14,10H23.21L20.93,18.5C20.7,19.37 19.92,20 19,20Z",
                "Otwórz projekt (.fthproj)",
                "#1FE0C5",
                () => OpenProjectInteractive(win));
            topStack.Children.Add(_projBtnOpen);

            _projBtnSave = CreateSidebarActionButton(
                "M15,9H5V5H15M12,19A3,3 0 0,1 9,16A3,3 0 0,1 12,13A3,3 0 0,1 15,16A3,3 0 0,1 12,19M17,3H5C3.89,3 3,3.9 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19V7L17,3Z",
                "Zapisz projekt (.fthproj)",
                "#2684FF",
                () => SaveProjectInteractive(win));
            topStack.Children.Add(_projBtnSave);

            _projBtnSourceDir = CreateSidebarActionButton(
                "M10,4H4C2.89,4 2,4.89 2,6V18A2,2 0 0,0 4,20H20A2,2 0 0,0 22,18V8C22,6.89 21.1,6 20,6H12L10,4Z",
                "Folder Source (APK / Mapy)",
                "#FAD02C",
                () =>
                {
                    EnsureUnifiedSourceDirectoryStructure(win);
                    string src = TacticalConfigPanel.GetPrimarySourceDirectory(win);
                    TacticalConfigPanel.TryOpenFolderInExplorer(src);
                });
            topStack.Children.Add(_projBtnSourceDir);

            mainDock.Children.Add(topStack);

            var bottomStack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
            DockPanel.SetDock(bottomStack, Dock.Bottom);

            _langSwitchBtn = CreateSidebarActionButton(
                "M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M11,19.93C7.05,19.44 4,16.08 4,12C4,11.38 4.08,10.78 4.21,10.21L9,15V16A2,2 0 0,0 11,18M18.9,17.39C18.64,16.58 17.9,16 17,16H16V13A1,1 0 0,0 15,12H9V10H11A1,1 0 0,0 12,9V7H14A2,2 0 0,0 16,5V4.59C18.93,5.77 21,8.64 21,12C21,14.08 20.2,15.97 18.9,17.39Z",
                "Język UI: PL / ENG",
                "#1FE0C5",
                () => BuilderI18n.ToggleLanguage(win));
            bottomStack.Children.Add(_langSwitchBtn);

            mainDock.Children.Add(bottomStack);
            sidebarBorder.Child = mainDock;
            return sidebarBorder;
        }

        public static void SwitchActiveModule(Window win, BuilderActiveModule targetModule)
        {
            _currentModule = targetModule;
            UpdateSidebarActiveButtonHighlight();

            var allElements = GetAllVisualDescendants(win).ToList();
            var mapOverlay = allElements.OfType<FrameworkElement>().FirstOrDefault(fe => string.Equals(fe.Tag?.ToString(), "INJECTED_MAP_OVERLAY", StringComparison.Ordinal));
            var opordOverlay = OpordBuilderManager.CreateOrGetOpordOverlayView(win);

            if (mapOverlay != null) mapOverlay.Visibility = Visibility.Collapsed;
            if (opordOverlay != null) opordOverlay.Visibility = Visibility.Collapsed;

            switch (targetModule)
            {
                case BuilderActiveModule.PackageGenerator:
                    BuilderUiSimplifier.ShowOpordView(win, false);
                    if (_originalBuilderTabButton != null)
                    {
                        _originalBuilderTabButton.IsEnabled = true;
                        _originalBuilderTabButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    }
                    break;

                case BuilderActiveModule.MapGenerator:
                    BuilderUiSimplifier.ShowOpordView(win, false);
                    if (mapOverlay != null)
                    {
                        mapOverlay.Visibility = Visibility.Visible;
                    }
                    else if (_originalMapTabButton != null)
                    {
                        _originalMapTabButton.IsEnabled = true;
                        _originalMapTabButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    }
                    break;

                case BuilderActiveModule.OpordGenerator:
                    BuilderUiSimplifier.ShowOpordView(win, true);
                    break;

                case BuilderActiveModule.UsbProgrammer:
                    BuilderUiSimplifier.ShowOpordView(win, false);
                    if (_originalUsbProgrammerButton != null)
                    {
                        _originalUsbProgrammerButton.IsEnabled = true;
                        _originalUsbProgrammerButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    }
                    else
                    {
                        TryInvokeOriginalButtonByText(allElements, "Programator USB");
                    }
                    break;
            }
        }

        private static bool TryInvokeOriginalButtonByText(List<DependencyObject> allElements, string searchText)
        {
            foreach (var btn in allElements.OfType<ButtonBase>())
            {
                if (_injectedSidebarContainer != null && IsDescendantOf(_injectedSidebarContainer, btn))
                    continue;

                string txt = GetCombinedText(btn);
                if (txt.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                {
                    btn.IsEnabled = true;
                    btn.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    return true;
                }
            }
            return false;
        }

        private static void UpdateSidebarActiveButtonHighlight()
        {
            SetNavButtonActiveState(_navBtnPackages, _currentModule == BuilderActiveModule.PackageGenerator);
            SetNavButtonActiveState(_navBtnMaps, _currentModule == BuilderActiveModule.MapGenerator);
            SetNavButtonActiveState(_navBtnOpord, _currentModule == BuilderActiveModule.OpordGenerator);
            SetNavButtonActiveState(_navBtnUsb, _currentModule == BuilderActiveModule.UsbProgrammer);
        }

        private static void SetNavButtonActiveState(Button btn, bool isActive)
        {
            if (btn == null) return;
            btn.Background = isActive
                ? new SolidColorBrush(Color.FromRgb(18, 52, 64))
                : new SolidColorBrush(Color.FromRgb(10, 24, 30));
            btn.BorderBrush = isActive
                ? TacticalConfigPanel.BrushAccent
                : new SolidColorBrush(Color.FromRgb(24, 50, 62));
            btn.BorderThickness = isActive ? new Thickness(2.5, 1.2, 1.2, 1.2) : new Thickness(1);

            foreach (var tb in GetAllVisualDescendants(btn).OfType<TextBlock>())
            {
                tb.Foreground = isActive ? TacticalConfigPanel.BrushAccent : Brushes.White;
            }
        }

        private static void RefreshSidebarLocalization(Window win)
        {
            if (_navGroupHeader != null)
                _navGroupHeader.Text = BuilderI18n.T("MODUŁY OPERACYJNE", "OPERATIONAL MODULES");
            if (_projGroupHeader != null)
                _projGroupHeader.Text = BuilderI18n.T("ZARZĄDZANIE PROJEKTEM", "PROJECT WORKSPACE");

            SetButtonLabel(_navBtnPackages, BuilderI18n.T("Generator Paczek (.ftak)", "Package Builder (.ftak)"));
            SetButtonLabel(_navBtnMaps, BuilderI18n.T("Generator Map 1:1", "1:1 Map Generator"));
            SetButtonLabel(_navBtnOpord, BuilderI18n.T("Generator OPORD", "OPORD Builder"));
            SetButtonLabel(_navBtnUsb, BuilderI18n.T("Programator Płytek USB", "USB Board Programmer"));

            SetButtonLabel(_projBtnNew, BuilderI18n.T("Nowy projekt (Stan zerowy)", "New Project (Zero-State)"));
            SetButtonLabel(_projBtnOpen, BuilderI18n.T("Otwórz projekt (.fthproj)", "Open Project (.fthproj)"));
            SetButtonLabel(_projBtnSave, BuilderI18n.T("Zapisz projekt (.fthproj)", "Save Project (.fthproj)"));
            SetButtonLabel(_projBtnSourceDir, BuilderI18n.T("Folder Source (APK / Mapy)", "Source Folder (APK / Maps)"));
            SetButtonLabel(_langSwitchBtn, BuilderI18n.T("Język UI: Polski (PL -> ENG)", "UI Language: English (ENG -> PL)"));

            if (_activeProjectFileLabel != null && string.IsNullOrEmpty(CurrentProjectFilePath))
            {
                _activeProjectFileLabel.Text = BuilderI18n.T("Projekt: Nowy (Stan zerowy)", "Project: New (Zero-State)");
            }
        }

        private static void SetButtonLabel(Button btn, string text)
        {
            if (btn == null) return;
            var tb = GetAllVisualDescendants(btn).OfType<TextBlock>().FirstOrDefault();
            if (tb != null) tb.Text = text;
        }

        private static Button CreateSidebarNavButton(string svgPathData, string labelText, Action onClick)
        {
            var btn = new Button
            {
                Height = 42,
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(10, 6, 10, 6),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Color.FromRgb(10, 24, 30)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(24, 50, 62)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };

            var row = new DockPanel { LastChildFill = true };
            var icon = CreateTacticalVectorIcon(svgPathData, TacticalConfigPanel.BrushAccent, 18);
            icon.Margin = new Thickness(0, 0, 10, 0);
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);

            row.Children.Add(new TextBlock
            {
                Text = labelText,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center
            });

            btn.Content = row;
            btn.Click += (_, __) => onClick?.Invoke();
            return btn;
        }

        private static Button CreateSidebarActionButton(string svgPathData, string labelText, string iconHexColor, Action onClick)
        {
            var iconBrush = TacticalConfigPanel.BrushFromHex(iconHexColor, Color.FromRgb(31, 224, 197));
            var btn = new Button
            {
                Height = 35,
                Margin = new Thickness(0, 0, 0, 5),
                Padding = new Thickness(10, 4, 10, 4),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Color.FromRgb(9, 20, 25)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(22, 46, 56)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };

            var row = new DockPanel { LastChildFill = true };
            var icon = CreateTacticalVectorIcon(svgPathData, iconBrush, 15);
            icon.Margin = new Thickness(0, 0, 9, 0);
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);

            row.Children.Add(new TextBlock
            {
                Text = labelText,
                Foreground = new SolidColorBrush(Color.FromRgb(210, 228, 235)),
                FontWeight = FontWeights.SemiBold,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            });

            btn.Content = row;
            btn.Click += (_, __) => onClick?.Invoke();
            return btn;
        }

        public static Viewbox CreateTacticalVectorIcon(string pathMarkup, Brush fillBrush, double sizePx)
        {
            var vb = new Viewbox
            {
                Width = sizePx,
                Height = sizePx,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center
            };

            var canvas = new Canvas { Width = 24, Height = 24 };
            try
            {
                var path = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse(pathMarkup),
                    Fill = fillBrush
                };
                canvas.Children.Add(path);
            }
            catch { }

            vb.Child = canvas;
            return vb;
        }

        private static string GetCombinedText(DependencyObject root)
        {
            var sb = new StringBuilder();
            if (root is ContentControl cc && cc.Content is string s)
                sb.Append(s).Append(' ');
            foreach (var tb in GetAllVisualDescendants(root).OfType<TextBlock>())
            {
                if (!string.IsNullOrWhiteSpace(tb.Text))
                    sb.Append(tb.Text.Trim()).Append(' ');
            }
            return sb.ToString().Trim();
        }

        private static bool IsDescendantOf(DependencyObject ancestor, DependencyObject descendant)
        {
            DependencyObject curr = descendant;
            while (curr != null)
            {
                if (curr == ancestor) return true;
                curr = VisualTreeHelper.GetParent(curr);
            }
            return false;
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