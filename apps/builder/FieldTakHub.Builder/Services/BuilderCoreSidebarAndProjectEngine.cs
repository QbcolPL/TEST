#pragma warning disable CS0414, CS0649, CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625
using System.Globalization;
using System.Text.RegularExpressions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
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
        private static long _lastAutoI18nTick;
        private static bool _hasUserLoadedProject;

        private static Grid _rootDockGrid;
        private static ColumnDefinition _rootSidebarColumnDef;
        private static Grid _rightWorkspaceGrid;
        private static Border _injectedSidebarContainer;
        private static DockPanel _expandedSidebarContent;
        private static StackPanel _collapsedSidebarStrip;
        private static FrameworkElement _originalMainWindowContentHost;

        private static bool _isSidebarCollapsed;
        private static BuilderActiveModule _currentModule = BuilderActiveModule.PackageGenerator;

        private static ButtonBase _originalBuilderTabButton;
        private static ButtonBase _originalMapTabButton;
        private static ButtonBase _originalUsbProgrammerButton;
        private static ButtonBase _originalLoadProjectButton;
        private static ButtonBase _originalSaveProjectButton;
        private static ButtonBase _originalSupportCreatorButton;

        private static Button _navBtnPackages;
        private static Button _navBtnMaps;
        private static Button _navBtnOpord;
        private static Button _navBtnUsb;

        private static Button _miniBtnPackages;
        private static Button _miniBtnMaps;
        private static Button _miniBtnOpord;
        private static Button _miniBtnUsb;
        private static Button _miniBtnSupport;

        private static Button _projBtnNew;
        private static Button _projBtnOpen;
        private static Button _projBtnSave;
        private static Button _projBtnSourceDir;
        private static Button _supportCreatorBtn;
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
                win.Loaded += (_, __) =>
                {
                    EnsureRootDockSidebar(win);
                    if (!_hasUserLoadedProject)
                    {
                        ResetMainWindowTextBoxesToZeroState(win);
                    }
                };
                win.ContentRendered += (_, __) => EnsureRootDockSidebar(win);
                win.LayoutUpdated += (_, __) => EnforceRootSidebarColumnBounds(win);
            }

            EnsureRootDockSidebar(win);
        }

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

        // =========================================================================
        // 1. CZYSTY STAN ZEROWY OBEJMUJĄCY PACZKI .FTAK ORAZ GENERATOR MAP (mapState)
        // =========================================================================
        private static string WriteCleanZeroStateDraftFile()
        {
            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FieldTakHub");
            Directory.CreateDirectory(appData);
            string zeroDraftPath = Path.Combine(appData, "zero_state_workspace.fthproj");

            var zeroJson = new JsonObject
            {
                ["schemaVersion"] = "3.2",
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
                },
                ["mapState"] = new JsonObject
                {
                    ["centerLat"] = 52.0693,
                    ["centerLon"] = 19.4803,
                    ["zoom"] = 15,
                    ["scale"] = 5000,
                    ["orientation"] = "Poziomo",
                    ["sheetFormat"] = "A4 — Sztabowy (297 × 210 mm)",
                    ["layerId"] = "google_sat",
                    ["showLidar"] = false,
                    ["lidarOpacity"] = 0.45,
                    ["coordSystem"] = "MGRS / UTM (Siatka wojskowa w metrach)",
                    ["subGridDivision"] = "Brak podziału (Tylko 100 m)",
                    ["gridLabelsMode"] = "Tylko główne linie MGRS",
                    ["labelBgStyle"] = "Kaseton (Kolor tła RGB)",
                    ["gridThickness"] = 0.8,
                    ["fontScale"] = 1.2,
                    ["mainGridOpacity"] = 0.8,
                    ["subGridOpacity"] = 0.75,
                    ["subLabelBgOpacity"] = 0.82,
                    ["mapDimmerOpacity"] = 0.0,
                    ["mainGridColorHex"] = "#FAD02C",
                    ["subGridLineColorHex"] = "#1FE0C5",
                    ["subLabelTextColorHex"] = "#FFFFFF",
                    ["subLabelBgColorHex"] = "#061014",
                    ["gridHaloMode"] = "Pełny czarny obrys",
                    ["aoOutPattern"] = "Jednolite tło RGB poza AO",
                    ["aoOutMaskOpacity"] = 0.1,
                    ["showAllObjectLabels"] = true,
                    ["showRotationHandlesOnMap"] = true,
                    ["showNorthArrow"] = true,
                    ["showScaleBar"] = true,
                    ["showLegend"] = false,
                    ["showNatoSymbolsInLegend"] = true,
                    ["selectedFactions"] = new JsonArray { "BLUFOR", "OPFOR" },
                    ["objects"] = new JsonArray(),
                    ["searchQuery"] = "",
                    ["selectedObjectColorHex"] = "#2684FF",
                    ["selectedNatoSymbolCode"] = "INFANTRY",
                    ["defaultObjectName"] = "Punkt",
                    ["newObjectShowLabelDefault"] = true,
                    ["dotMarkerSizePx"] = 14,
                    ["natoMarkerScale"] = 1.0
                }
            };

            string jsonText = zeroJson.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(zeroDraftPath, jsonText, Encoding.UTF8);
            File.WriteAllText(Path.Combine(appData, "draft_session.json"), jsonText, Encoding.UTF8);
            File.WriteAllText(Path.Combine(appData, "map_state.json"), zeroJson["mapState"]!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
            TacticalConfigPanel.CurrentDraftProjectPath = zeroDraftPath;
            return zeroDraftPath;
        }

        // =========================================================================
        // 2. OKNO POTWIERDZENIA NOWEGO PROJEKTU (Z OPCJĄ ZAPISU OBECNEGO PROJEKTU)
        //    ORAZ PEŁNY RESET PACZEK .FTAK, GENERATORA MAP 1:1 I GENERATORA OPORD
        // =========================================================================
        private static int ShowNewProjectConfirmationDialog(Window owner)
        {
            // Zwraca: 1 = Zapisz obecny i utwórz nowy, 2 = Utwórz nowy bez zapisywania, 0 = Anuluj
            int choice = 0;

            var dlg = new Window
            {
                Title = BuilderI18n.T("Nowy Projekt — Ostrzeżenie o niezapisanych danych", "New Project — Unsaved Data Warning"),
                Width = 540,
                Height = 250,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                Background = new SolidColorBrush(Color.FromRgb(10, 24, 30)),
                Foreground = Brushes.White,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow
            };

            var rootBorder = new Border
            {
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(1.5),
                Padding = new Thickness(18, 16, 18, 16)
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = BuilderI18n.T(
                    "⚠️ CZY NA PEWNO CHCESZ UTWORZYĆ NOWY PROJEKT?",
                    "⚠️ ARE YOU SURE YOU WANT TO CREATE A NEW PROJECT?"),
                Foreground = new SolidColorBrush(Color.FromRgb(250, 208, 44)),
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 8)
            });

            stack.Children.Add(new TextBlock
            {
                Text = BuilderI18n.T(
                    "Wszystkie niezapisane dane we wszystkich modułach:\n• Generator Paczek (.ftak) — strony konfliktu, klucze PSK, częstotliwości\n• Generator Map 1:1 — granica terenu AO, punkty POI, trasy i ustawienia siatki\n• Generator OPORD — rozkazy dzienne, pogoda METOC, tabela składu i kryptonimy\nzostaną bezpowrotnie utracone i wyzerowane do stanu początkowego.",
                    "All unsaved data across all modules:\n• Package Builder (.ftak) — factions, PSK keys, radio channels\n• 1:1 Map Generator — AO boundary, POI markers, routes and grid settings\n• OPORD Builder — daily plans, METOC weather, squad roster and codewords\nwill be permanently lost and reset to initial zero-state."),
                Foreground = new SolidColorBrush(Color.FromRgb(215, 230, 238)),
                FontSize = 11.5,
                LineHeight = 17,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 18)
            });

            var btnGrid = new Grid();
            btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) });
            btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
            btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.75, GridUnitType.Star) });

            var btnSaveAndNew = new Button
            {
                Content = BuilderI18n.T("💾 Zapisz obecny i utwórz nowy", "💾 Save Current & Create New"),
                Height = 36,
                Margin = new Thickness(0, 0, 6, 0),
                Background = new SolidColorBrush(Color.FromRgb(20, 88, 52)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(46, 204, 113)),
                BorderThickness = new Thickness(1.5),
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                Cursor = Cursors.Hand
            };
            btnSaveAndNew.Click += (_, __) => { choice = 1; dlg.Close(); };
            Grid.SetColumn(btnSaveAndNew, 0);
            btnGrid.Children.Add(btnSaveAndNew);

            var btnDiscardAndNew = new Button
            {
                Content = BuilderI18n.T("🗑️ Utwórz nowy bez zapisywania", "🗑️ Create New Without Saving"),
                Height = 36,
                Margin = new Thickness(0, 0, 6, 0),
                Background = new SolidColorBrush(Color.FromRgb(98, 24, 24)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(235, 64, 52)),
                BorderThickness = new Thickness(1.5),
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                Cursor = Cursors.Hand
            };
            btnDiscardAndNew.Click += (_, __) => { choice = 2; dlg.Close(); };
            Grid.SetColumn(btnDiscardAndNew, 1);
            btnGrid.Children.Add(btnDiscardAndNew);

            var btnCancel = new Button
            {
                Content = BuilderI18n.T("Anuluj", "Cancel"),
                Height = 36,
                Background = TacticalConfigPanel.BrushInputBg,
                Foreground = Brushes.White,
                BorderBrush = TacticalConfigPanel.BrushInputBorder,
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.SemiBold,
                FontSize = 11,
                Cursor = Cursors.Hand
            };
            btnCancel.Click += (_, __) => { choice = 0; dlg.Close(); };
            Grid.SetColumn(btnCancel, 2);
            btnGrid.Children.Add(btnCancel);

            stack.Children.Add(btnGrid);
            rootBorder.Child = stack;
            dlg.Content = rootBorder;
            dlg.ShowDialog();

            return choice;
        }

        public static void CreateNewZeroProjectInteractive(Window win)
        {
            int userAction = ShowNewProjectConfirmationDialog(win);
            if (userAction == 0) return;

            if (userAction == 1)
            {
                // Użytkownik wybrał "Zapisz obecny i utwórz nowy" — jeśli anuluje okno zapisu, przerywamy reset
                bool savedOk = SaveProjectInteractiveWithResult(win);
                if (!savedOk) return;
            }

            _hasUserLoadedProject = false;
            CurrentProjectFilePath = "";

            // 1. Zapisz czysty stan zerowy (obejmujący zarówno Factions jak i puste mapState)
            string zeroPath = WriteCleanZeroStateDraftFile();

            // 2. Wyczyść pliki tymczasowe mapy (GPX / KML / map_state.json) w source\maps
            try
            {
                string sourceDir = TacticalConfigPanel.GetPrimarySourceDirectory(win);
                string mapsDir = Path.Combine(sourceDir, "maps");
                if (Directory.Exists(mapsDir))
                {
                    foreach (string mf in Directory.GetFiles(mapsDir, "*.*", SearchOption.AllDirectories))
                    {
                        try { File.Delete(mf); } catch { }
                    }
                }
            }
            catch { }

            // 3. Zresetuj całkowicie Generator OPORD (pamięć, pliki opord_*.json oraz widok UI)
            OpordBuilderManager.ResetAllOpordsToZeroState(win);

            // 4. Przeładuj stan zerowy we wszystkich modułach (w tym wywołaj natywny loader z zeroPath, aby wyczyścić obiekty na Mapie 1:1)
            ReloadProjectAndMapStateEverywhere(win, zeroPath);

            var allElements = GetAllVisualDescendants(win).ToList();
            CaptureAndCollapseTopMenuBar(allElements);
                CollapseObsoleteBuilderModeSelector(allElements);

            SetPendingSidebarProjectPath(zeroPath);
            try
            {
                if (_originalLoadProjectButton != null)
                {
                    _originalLoadProjectButton.IsEnabled = true;
                    _originalLoadProjectButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                }
            }
            catch { }
            finally
            {
                SetPendingSidebarProjectPath("");
            }

            ResetMainWindowTextBoxesToZeroState(win);

            if (_activeProjectFileLabel != null)
            {
                _activeProjectFileLabel.Text = BuilderI18n.T("Projekt: Nowy (Stan zerowy)", "Project: New (Zero-State)");
            }

            // 5. Odśwież aktualnie otwarty moduł (np. jeśli użytkownik był na Mapie lub OPORD, od razu zobaczy wyzerowany ekran)
            SwitchActiveModule(win, _currentModule);
        }

        public static void OpenProjectInteractive(Window win)
        {
            var dlg = new OpenFileDialog
            {
                Title = BuilderI18n.T("Otwórz projekt Field TAK Hub (.fthproj / .json)", "Open Field TAK Hub Project (.fthproj / .json)"),
                Filter = "Field TAK Hub Project (*.fthproj;*.json)|*.fthproj;*.json|All Files (*.*)|*.*"
            };

            if (dlg.ShowDialog(win) != true || !File.Exists(dlg.FileName))
                return;

            _hasUserLoadedProject = true;
            CurrentProjectFilePath = dlg.FileName;

            ReloadProjectAndMapStateEverywhere(win, dlg.FileName);

            var allElements = GetAllVisualDescendants(win).ToList();
            CaptureAndCollapseTopMenuBar(allElements);
                CollapseObsoleteBuilderModeSelector(allElements);

            SetPendingSidebarProjectPath(dlg.FileName);
            try
            {
                if (_originalLoadProjectButton != null)
                {
                    _originalLoadProjectButton.IsEnabled = true;
                    _originalLoadProjectButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                }
            }
            catch { }
            finally
            {
                SetPendingSidebarProjectPath("");
            }

            if (_activeProjectFileLabel != null)
            {
                _activeProjectFileLabel.Text = BuilderI18n.T("Projekt: ", "Project: ") + Path.GetFileName(dlg.FileName);
            }

            if (_currentModule == BuilderActiveModule.MapGenerator && _originalMapTabButton != null)
            {
                try
                {
                    _originalMapTabButton.IsEnabled = true;
                    _originalMapTabButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                }
                catch { }
            }

            var updatedDescendants = GetAllVisualDescendants(win).ToList();
            CollapseObsoleteBuilderModeSelector(updatedDescendants);
            RemoveDuplicateInnerSidebars(win, updatedDescendants);
            BuilderI18n.ApplyLanguageToVisualTree(win);
        }

        private static void SetPendingSidebarProjectPath(string value)
        {
            try
            {
                var field = typeof(TacticalConfigPanel).GetField("PendingSidebarProjectPath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (field != null)
                {
                    field.SetValue(null, value ?? "");
                }
            }
            catch { }
        }

        public static void InvokeSupportCreatorAction(Window win)
        {
            var allElements = GetAllVisualDescendants(win).ToList();
            CaptureAndCollapseTopMenuBar(allElements);
                CollapseObsoleteBuilderModeSelector(allElements);

            if (_originalSupportCreatorButton != null)
            {
                try
                {
                    _originalSupportCreatorButton.IsEnabled = true;
                    _originalSupportCreatorButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    return;
                }
                catch { }
            }

            foreach (var btn in allElements.OfType<ButtonBase>())
            {
                if (_injectedSidebarContainer != null && IsDescendantOf(_injectedSidebarContainer, btn))
                    continue;

                string txt = GetCombinedText(btn);
                if (txt.Contains("Wesprzyj", StringComparison.OrdinalIgnoreCase) ||
                    txt.Contains("Support Creator", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        btn.IsEnabled = true;
                        btn.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                        return;
                    }
                    catch { }
                }
            }

            try
            {
                Process.Start(new ProcessStartInfo("https://buycoffee.to") { UseShellExecute = true });
            }
            catch { }
        }

        public static void SaveProjectInteractive(Window win)
        {
            SaveProjectInteractiveWithResult(win);
        }

        public static bool SaveProjectInteractiveWithResult(Window win)
        {
            try { TacticalConfigPanel.SaveSessionStateAuto(win); } catch { }
            try { OpordBuilderManager.SaveAllOpordsToDisk(win); } catch { }

            CaptureAndCollapseTopMenuBar(GetAllVisualDescendants(win).ToList());

            if (_originalSaveProjectButton != null)
            {
                DateTime beforeWrite = DateTime.MinValue;
                string beforePath = TacticalConfigPanel.CurrentDraftProjectPath ?? "";
                if (!string.IsNullOrWhiteSpace(beforePath) && File.Exists(beforePath))
                {
                    try { beforeWrite = File.GetLastWriteTimeUtc(beforePath); } catch { }
                }

                _originalSaveProjectButton.IsEnabled = true;
                _originalSaveProjectButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

                string savedPath = TacticalConfigPanel.CurrentDraftProjectPath ?? "";
                if (!string.IsNullOrWhiteSpace(savedPath) && File.Exists(savedPath))
                {
                    DateTime afterWrite = File.GetLastWriteTimeUtc(savedPath);
                    if (savedPath != beforePath || afterWrite > beforeWrite)
                    {
                        CurrentProjectFilePath = savedPath;
                        if (_activeProjectFileLabel != null)
                        {
                            _activeProjectFileLabel.Text = BuilderI18n.T("Projekt: ", "Project: ") + Path.GetFileName(savedPath);
                        }
                        return true;
                    }
                }
                return false;
            }

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

                JsonNode existingMapState = null;
                try
                {
                    if (!string.IsNullOrWhiteSpace(TacticalConfigPanel.CurrentDraftProjectPath) && File.Exists(TacticalConfigPanel.CurrentDraftProjectPath))
                    {
                        var draftNode = JsonNode.Parse(File.ReadAllText(TacticalConfigPanel.CurrentDraftProjectPath, Encoding.UTF8)) as JsonObject;
                        if (draftNode != null && draftNode["mapState"] != null)
                        {
                            existingMapState = draftNode["mapState"].DeepClone();
                        }
                    }
                }
                catch { }

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
                    ["schemaVersion"] = "3.2",
                    ["savedAtUtc"] = DateTime.UtcNow.ToString("o"),
                    ["eventName"] = snap.EventName,
                    ["factions"] = fArr
                };
                if (existingMapState != null)
                {
                    rootObj["mapState"] = existingMapState;
                }

                File.WriteAllText(dlg.FileName, rootObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
                TacticalConfigPanel.CurrentDraftProjectPath = dlg.FileName;

                if (_activeProjectFileLabel != null)
                {
                    _activeProjectFileLabel.Text = BuilderI18n.T("Projekt: ", "Project: ") + Path.GetFileName(dlg.FileName);
                }
                return true;
            }

            return false;
        }

        private static void ReloadProjectAndMapStateEverywhere(Window win, string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;

            try
            {
                string rawJson = File.ReadAllText(filePath, Encoding.UTF8).Replace("\u200B", "");
                File.WriteAllText(filePath, rawJson, Encoding.UTF8);

                string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FieldTakHub");
                Directory.CreateDirectory(appData);
                File.WriteAllText(Path.Combine(appData, "draft_session.json"), rawJson, Encoding.UTF8);
                File.WriteAllText(Path.Combine(appData, "zero_state_workspace.fthproj"), rawJson, Encoding.UTF8);

                foreach (string existingJson in Directory.GetFiles(appData, "*.json", SearchOption.TopDirectoryOnly))
                {
                    if (!Path.GetFileName(existingJson).StartsWith("opord_", StringComparison.OrdinalIgnoreCase))
                    {
                        try { File.WriteAllText(existingJson, rawJson, Encoding.UTF8); } catch { }
                    }
                }

                TacticalConfigPanel.CurrentDraftProjectPath = filePath;

                JsonObject parsedRoot = null;
                JsonObject parsedMapState = null;
                try
                {
                    parsedRoot = JsonNode.Parse(rawJson) as JsonObject;
                    parsedMapState = parsedRoot?["mapState"] as JsonObject;
                }
                catch { }

                var panelType = typeof(TacticalConfigPanel);
                var methods = panelType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

                foreach (var m in methods)
                {
                    string name = m.Name;
                    bool isLoadLike =
                        name.Contains("Load", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Restore", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Import", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Apply", StringComparison.OrdinalIgnoreCase);

                    if (!isLoadLike) continue;
                    if (name.Contains("Interactive", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Dialog", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var pars = m.GetParameters();
                    try
                    {
                        if (pars.Length == 2)
                        {
                            if (pars[0].ParameterType == typeof(Window) && pars[1].ParameterType == typeof(string))
                                m.Invoke(null, new object[] { win, filePath });
                            else if (pars[0].ParameterType == typeof(string) && pars[1].ParameterType == typeof(Window))
                                m.Invoke(null, new object[] { filePath, win });
                            else if (pars[0].ParameterType == typeof(Window) && pars[1].ParameterType == typeof(JsonObject) && parsedRoot != null)
                                m.Invoke(null, new object[] { win, name.Contains("Map", StringComparison.OrdinalIgnoreCase) ? (parsedMapState ?? parsedRoot) : parsedRoot });
                        }
                        else if (pars.Length == 1)
                        {
                            if (pars[0].ParameterType == typeof(string))
                                m.Invoke(null, new object[] { filePath });
                            else if (pars[0].ParameterType == typeof(Window))
                                m.Invoke(null, new object[] { win });
                            else if (pars[0].ParameterType == typeof(JsonObject) && parsedRoot != null)
                                m.Invoke(null, new object[] { name.Contains("Map", StringComparison.OrdinalIgnoreCase) ? (parsedMapState ?? parsedRoot) : parsedRoot });
                        }
                    }
                    catch { }
                }

                var fields = panelType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                foreach (var f in fields)
                {
                    if (f.Name.Contains("MapOverlay", StringComparison.OrdinalIgnoreCase) && typeof(UIElement).IsAssignableFrom(f.FieldType))
                    {
                        if (f.GetValue(null) is UIElement oldOverlay && VisualTreeHelper.GetParent(oldOverlay) is Panel parentPanel)
                        {
                            parentPanel.Children.Remove(oldOverlay);
                        }
                        f.SetValue(null, null);
                    }
                }
            }
            catch { }
        }

        private static void ResetMainWindowTextBoxesToZeroState(Window win)
        {
            if (_hasUserLoadedProject || !string.IsNullOrEmpty(CurrentProjectFilePath))
                return;

            foreach (var tb in GetAllVisualDescendants(win).OfType<TextBox>())
            {
                string val = (tb.Text ?? "").Trim();
                if (val.Contains("ggzstak", StringComparison.OrdinalIgnoreCase) ||
                    val.Contains("GGZS", StringComparison.OrdinalIgnoreCase) ||
                    val.Contains("MILSIM", StringComparison.OrdinalIgnoreCase))
                {
                    tb.Text = "";
                }
            }
        }

        public static void ToggleSidebarCollapsed()
        {
            _isSidebarCollapsed = !_isSidebarCollapsed;
            ApplySidebarCollapseState();
        }

        private static void ApplySidebarCollapseState()
        {
            double targetWidth = _isSidebarCollapsed ? 42.0 : 255.0;

            if (_rootSidebarColumnDef != null)
            {
                _rootSidebarColumnDef.Width = new GridLength(targetWidth);
            }

            if (_injectedSidebarContainer != null)
            {
                _injectedSidebarContainer.Width = targetWidth;
                _injectedSidebarContainer.Padding = _isSidebarCollapsed
                    ? new Thickness(4, 12, 4, 12)
                    : new Thickness(12, 16, 12, 14);
            }

            if (_expandedSidebarContent != null)
            {
                _expandedSidebarContent.Visibility = _isSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
            }

            if (_collapsedSidebarStrip != null)
            {
                _collapsedSidebarStrip.Visibility = _isSidebarCollapsed ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private static void EnsureRootDockSidebar(Window win)
        {
            if (_isUpdatingNav) return;
            _isUpdatingNav = true;
            try
            {
                var allElements = GetAllVisualDescendants(win).ToList();

                CaptureAndCollapseTopMenuBar(allElements);
                CollapseObsoleteBuilderModeSelector(allElements);
                CollapseLegacyLeftSidebarColumn(win, allElements);

                if (win.Content is Grid currentTopGrid && string.Equals(currentTopGrid.Tag?.ToString(), "FTH_ROOT_LAYOUT_GRID", StringComparison.Ordinal))
                {
                    _rootDockGrid = currentTopGrid;
                    RemoveDuplicateInnerSidebars(win, allElements);
                    ApplySidebarCollapseState();
                    return;
                }

                foreach (var oldSide in allElements.OfType<Border>().Where(b => string.Equals(b.Tag?.ToString(), "FTH_TACTICAL_SIDEBAR_V31", StringComparison.Ordinal)).ToList())
                {
                    oldSide.Visibility = Visibility.Collapsed;
                    oldSide.Width = 0;
                    if (VisualTreeHelper.GetParent(oldSide) is Grid oldParentGrid && oldParentGrid.ColumnDefinitions.Count >= 2)
                    {
                        oldParentGrid.ColumnDefinitions[0].Width = new GridLength(0);
                        oldParentGrid.ColumnDefinitions[0].MinWidth = 0;
                        oldParentGrid.Children.Remove(oldSide);
                    }
                }

                UIElement previousWindowContent = win.Content as UIElement;
                if (previousWindowContent != null)
                {
                    win.Content = null;
                }

                _rootDockGrid = new Grid
                {
                    Tag = "FTH_ROOT_LAYOUT_GRID",
                    Background = new SolidColorBrush(Color.FromRgb(7, 17, 21))
                };
                _rootSidebarColumnDef = new ColumnDefinition { Width = new GridLength(255) };
                _rootDockGrid.ColumnDefinitions.Add(_rootSidebarColumnDef);
                _rootDockGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                _injectedSidebarContainer = BuildTacticalSidebarControl(win);
                Panel.SetZIndex(_injectedSidebarContainer, 99999);
                Grid.SetColumn(_injectedSidebarContainer, 0);
                _rootDockGrid.Children.Add(_injectedSidebarContainer);

                _rightWorkspaceGrid = new Grid
                {
                    Tag = "FTH_RIGHT_WORKSPACE_GRID",
                    Background = new SolidColorBrush(Color.FromRgb(7, 17, 21)),
                    ClipToBounds = true
                };
                Grid.SetColumn(_rightWorkspaceGrid, 1);

                if (previousWindowContent != null)
                {
                    _originalMainWindowContentHost = previousWindowContent as FrameworkElement;
                    _rightWorkspaceGrid.Children.Add(previousWindowContent);
                }

                _rootDockGrid.Children.Add(_rightWorkspaceGrid);
                win.Content = _rootDockGrid;

                var updatedDescendants = GetAllVisualDescendants(win).ToList();
            CollapseObsoleteBuilderModeSelector(updatedDescendants);
            RemoveDuplicateInnerSidebars(win, updatedDescendants);
            BuilderI18n.ApplyLanguageToVisualTree(win);
                ApplySidebarCollapseState();
                RefreshSidebarLocalization(win);
                UpdateSidebarActiveButtonHighlight();
            }
            catch { }
            finally
            {
                _isUpdatingNav = false;
            }
        }

        private static void RemoveDuplicateInnerSidebars(Window win, List<DependencyObject> allElements)
        {
            foreach (var b in allElements.OfType<Border>())
            {
                if (b == _injectedSidebarContainer) continue;

                string tagStr = b.Tag?.ToString() ?? "";
                if (tagStr.StartsWith("FTH_TACTICAL_SIDEBAR", StringComparison.OrdinalIgnoreCase))
                {
                    b.Visibility = Visibility.Collapsed;
                    b.Width = 0;
                    if (VisualTreeHelper.GetParent(b) is Grid parentGrid && parentGrid != _rootDockGrid && parentGrid.ColumnDefinitions.Count >= 2)
                    {
                        parentGrid.ColumnDefinitions[0].Width = new GridLength(0);
                        parentGrid.ColumnDefinitions[0].MinWidth = 0;
                    }
                }
            }

            foreach (var tb in allElements.OfType<TextBlock>())
            {
                if (_injectedSidebarContainer != null && IsDescendantOf(_injectedSidebarContainer, tb))
                    continue;

                string t = (tb.Text ?? "").Trim();
                if (string.Equals(t, "MODUŁY OPERACYJNE", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(t, "OPERATIONAL MODULES", StringComparison.OrdinalIgnoreCase))
                {
                    DependencyObject curr = tb;
                    while (curr != null && curr != win && curr != _rootDockGrid && curr != _rightWorkspaceGrid)
                    {
                        DependencyObject parent = VisualTreeHelper.GetParent(curr);
                        if (curr is Border dupBorder && dupBorder != _injectedSidebarContainer && dupBorder.ActualWidth <= 320)
                        {
                            dupBorder.Visibility = Visibility.Collapsed;
                            dupBorder.Width = 0;
                        }
                        if (parent is Grid g && g != _rootDockGrid && g.ColumnDefinitions.Count >= 2 && curr is FrameworkElement fe && Grid.GetColumn(fe) == 0)
                        {
                            g.ColumnDefinitions[0].Width = new GridLength(0);
                            g.ColumnDefinitions[0].MinWidth = 0;
                            fe.Visibility = Visibility.Collapsed;
                            fe.Width = 0;
                            break;
                        }
                        curr = parent;
                    }
                }
            }
        }

        private static void EnforceRootSidebarColumnBounds(Window win)
        {
            if (_isUpdatingNav || _rootDockGrid == null || _injectedSidebarContainer == null) return;
            try
            {
                CollapseObsoleteBuilderModeSelector(GetAllVisualDescendants(win).ToList());
                long nowTick = Environment.TickCount64;
                if (nowTick - _lastAutoI18nTick > 250)
                {
                    _lastAutoI18nTick = nowTick;
                    var currentDescendants = GetAllVisualDescendants(win).ToList();
                    CollapseObsoleteBuilderModeSelector(currentDescendants);
                    // I18n runs only on language toggle or module switch
                }
                if (_injectedSidebarContainer.Visibility != Visibility.Visible)
                    _injectedSidebarContainer.Visibility = Visibility.Visible;
                if (Grid.GetColumn(_injectedSidebarContainer) != 0)
                    Grid.SetColumn(_injectedSidebarContainer, 0);
                if (Panel.GetZIndex(_injectedSidebarContainer) != 99999)
                    Panel.SetZIndex(_injectedSidebarContainer, 99999);

                for (int i = 0; i < _rootDockGrid.Children.Count; i++)
                {
                    UIElement ch = _rootDockGrid.Children[i];
                    if (ch != null && ch != _injectedSidebarContainer)
                    {
                        if (Grid.GetColumn(ch) != 1)
                            Grid.SetColumn(ch, 1);
                        if (Grid.GetColumnSpan(ch) > 1)
                            Grid.SetColumnSpan(ch, 1);
                    }
                }
            }
            catch { }
        }

        
        private static void CollapseObsoleteBuilderModeSelector(List<DependencyObject> allElements)
        {
            try
            {
                foreach (var tb in allElements.OfType<TextBlock>())
                {
                    string t = (tb.Text ?? "").Trim();
                    if (t.Equals("Tryb Buildera", StringComparison.OrdinalIgnoreCase) ||
                        t.Equals("Tryb Buildera:", StringComparison.OrdinalIgnoreCase) ||
                        t.Equals("Builder Mode", StringComparison.OrdinalIgnoreCase) ||
                        t.Equals("Builder Mode:", StringComparison.OrdinalIgnoreCase))
                    {
                        tb.Visibility = Visibility.Collapsed;
                        tb.Width = 0;
                        tb.Height = 0;
                        tb.Margin = new Thickness(0);

                        DependencyObject parent = VisualTreeHelper.GetParent(tb);
                        if (parent is Panel p)
                        {
                            foreach (UIElement ch in p.Children)
                            {
                                if (ch is ComboBox cb)
                                {
                                    cb.Visibility = Visibility.Collapsed;
                                    cb.Width = 0;
                                    cb.Height = 0;
                                    cb.Margin = new Thickness(0);
                                }
                            }
                            if (p.Children.Count <= 3)
                            {
                                p.Visibility = Visibility.Collapsed;
                                p.Width = 0;
                                p.Height = 0;
                                p.Margin = new Thickness(0);
                                if (VisualTreeHelper.GetParent(p) is Border b && b.ActualHeight < 80 && b.ActualWidth < 450)
                                {
                                    b.Visibility = Visibility.Collapsed;
                                    b.Width = 0;
                                    b.Height = 0;
                                    b.Margin = new Thickness(0);
                                    b.Padding = new Thickness(0);
                                }
                            }
                        }
                    }
                }

                foreach (var cb in allElements.OfType<ComboBox>())
                {
                    if (_injectedSidebarContainer != null && IsDescendantOf(_injectedSidebarContainer, cb))
                        continue;

                    bool isModeCombo = false;
                    foreach (var item in cb.Items)
                    {
                        string itemStr = (item is ComboBoxItem cbi ? cbi.Content?.ToString() : item?.ToString()) ?? "";
                        if (itemStr.Contains("Tryb prosty", StringComparison.OrdinalIgnoreCase) ||
                            itemStr.Contains("Tryb zaawansowany", StringComparison.OrdinalIgnoreCase) ||
                            itemStr.Contains("Simple Mode", StringComparison.OrdinalIgnoreCase) ||
                            itemStr.Contains("Advanced Mode", StringComparison.OrdinalIgnoreCase))
                        {
                            isModeCombo = true;
                            break;
                        }
                    }

                    if (isModeCombo)
                    {
                        cb.Visibility = Visibility.Collapsed;
                        cb.Width = 0;
                        cb.Height = 0;
                        cb.Margin = new Thickness(0);

                        if (VisualTreeHelper.GetParent(cb) is Panel p && p.Children.Count <= 3)
                        {
                            p.Visibility = Visibility.Collapsed;
                            p.Width = 0;
                            p.Height = 0;
                            p.Margin = new Thickness(0);
                        }
                    }
                }
            }
            catch { }
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
                else if (txt.Contains("Wczytaj projekt", StringComparison.OrdinalIgnoreCase) ||
                         txt.Contains("Load Workspace", StringComparison.OrdinalIgnoreCase))
                {
                    if (_originalLoadProjectButton == null)
                        _originalLoadProjectButton = btn;
                }
                else if (txt.Contains("Zapisz projekt roboczy", StringComparison.OrdinalIgnoreCase) ||
                         txt.Contains("Save Workspace", StringComparison.OrdinalIgnoreCase))
                {
                    if (_originalSaveProjectButton == null)
                        _originalSaveProjectButton = btn;
                }
                else if (txt.Contains("Wesprzyj twórcę", StringComparison.OrdinalIgnoreCase) ||
                         txt.Contains("Support Creator", StringComparison.OrdinalIgnoreCase))
                {
                    if (_originalSupportCreatorButton == null)
                        _originalSupportCreatorButton = btn;
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
                    txt.Contains("Wesprzyj twórcę", StringComparison.OrdinalIgnoreCase) ||
                    txt.Contains("Support Creator", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(btn.Tag?.ToString(), "INJECTED_OPORD_TAB_BTN", StringComparison.Ordinal) ||
                    string.Equals(btn.Tag?.ToString(), "INJECTED_LANG_BTN", StringComparison.Ordinal);

                if (isTopBarButton)
                {
                    btn.Visibility = Visibility.Collapsed;
                    btn.Width = 0;
                    btn.Height = 0;
                    btn.Margin = new Thickness(0);
                    btn.Padding = new Thickness(0);

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
                                    !cTxt.Contains("Wesprzyj", StringComparison.OrdinalIgnoreCase) &&
                                    !cTxt.Contains("Support Creator", StringComparison.OrdinalIgnoreCase))
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

        private static void CollapseLegacyLeftSidebarColumn(Window win, List<DependencyObject> allElements)
        {
            string[] legacySidebarTexts = new string[]
            {
                "Certyfikaty TAK Server",
                "Konfiguracja połączenia i Data Packages",
                "Konfiguracja radia i sieci",
                "Profile i ustawienia",
                "Preferencje aplikacji",
                "Utwórz paczkę dla uczestników"
            };

            foreach (var tb in allElements.OfType<TextBlock>())
            {
                if (_injectedSidebarContainer != null && IsDescendantOf(_injectedSidebarContainer, tb))
                    continue;

                string t = (tb.Text ?? "").Trim();
                bool matched = false;
                for (int i = 0; i < legacySidebarTexts.Length; i++)
                {
                    if (t.Contains(legacySidebarTexts[i], StringComparison.OrdinalIgnoreCase))
                    {
                        matched = true;
                        break;
                    }
                }
                if (!matched) continue;

                DependencyObject curr = tb;
                while (curr != null && curr != win && curr != _rootDockGrid && curr != _rightWorkspaceGrid)
                {
                    DependencyObject parent = VisualTreeHelper.GetParent(curr);
                    if (parent is Grid g && g != _rootDockGrid && g.ColumnDefinitions.Count >= 2 && curr is FrameworkElement fe && Grid.GetColumn(fe) == 0)
                    {
                        g.ColumnDefinitions[0].Width = new GridLength(0);
                        g.ColumnDefinitions[0].MinWidth = 0;
                        fe.Visibility = Visibility.Collapsed;
                        fe.Width = 0;
                        break;
                    }
                    curr = parent;
                }
            }
        }

        private static Border BuildTacticalSidebarControl(Window win)
        {
            var sidebarBorder = new Border
            {
                Tag = "FTH_TACTICAL_SIDEBAR_V31",
                Width = 255,
                Background = new SolidColorBrush(Color.FromRgb(6, 15, 19)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(25, 56, 68)),
                BorderThickness = new Thickness(0, 0, 1.5, 0),
                Padding = new Thickness(12, 16, 12, 14)
            };

            var rootContainerGrid = new Grid();

            _expandedSidebarContent = new DockPanel { LastChildFill = false };

            var topStack = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            DockPanel.SetDock(topStack, Dock.Top);

            var brandCard = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(10, 25, 32)),
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 8, 8, 8),
                Margin = new Thickness(0, 0, 0, 16)
            };

            var brandRow = new DockPanel { LastChildFill = true };

            var collapseArrowBtn = CreateArrowToggleButton(
                isCollapseLeft: true,
                BuilderI18n.T("Zwiń pasek boczny (◀)", "Collapse sidebar (◀)"),
                ToggleSidebarCollapsed);
            DockPanel.SetDock(collapseArrowBtn, Dock.Right);
            brandRow.Children.Add(collapseArrowBtn);

            var radarIcon = CreateTacticalVectorIcon(
                "M12,2A10,10 0 1,0 22,12A10,10 0 0,0 12,2M12,4A8,8 0 0,1 19.93,11H17.91A6,6 0 0,0 13,6.09V4.07C12.67,4.03 12.34,4 12,4M11,4.07V6.09A6,6 0 0,0 6.09,11H4.07A8,8 0 0,1 11,4.07M12,8A4,4 0 0,0 8,12A4,4 0 0,0 12,16A4,4 0 0,0 16,12A4,4 0 0,0 12,8M12,10A2,2 0 0,1 14,12A2,2 0 0,1 12,14A2,2 0 0,1 10,12A2,2 0 0,1 12,10M4.07,13H6.09A6,6 0 0,0 11,17.91V19.93A8,8 0 0,1 4.07,13M17.91,13H19.93A8,8 0 0,1 13,19.93V17.91A6,6 0 0,0 17.91,13Z",
                TacticalConfigPanel.BrushAccent,
                24);
            radarIcon.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(radarIcon, Dock.Left);
            brandRow.Children.Add(radarIcon);

            var brandTextStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            _sidebarTitleBlock = new TextBlock
            {
                Text = "FIELD TAK HUB",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 13
            };
            _sidebarSubtitleBlock = new TextBlock
            {
                Text = "connect · deploy · operate",
                Foreground = TacticalConfigPanel.BrushAccent,
                FontWeight = FontWeights.SemiBold,
                FontSize = 9.5
            };
            brandTextStack.Children.Add(_sidebarTitleBlock);
            brandTextStack.Children.Add(_sidebarSubtitleBlock);
            brandRow.Children.Add(brandTextStack);
            brandCard.Child = brandRow;
            topStack.Children.Add(brandCard);

            _navGroupHeader = new TextBlock
            {
                Text = "MODUŁY OPERACYJNE",
                Foreground = new SolidColorBrush(Color.FromRgb(120, 155, 168)),
                FontWeight = FontWeights.Bold,
                FontSize = 10,
                Margin = new Thickness(4, 0, 0, 6)
            };
            topStack.Children.Add(_navGroupHeader);

            string iconPkg = "M12,1L3,5V11C3,16.55 6.84,21.74 12,23C17.16,21.74 21,16.55 21,11V5L12,1M12,5.3L18,7.9V11.2C18,15.1 15.4,18.7 12,20C8.6,18.7 6,15.1 6,11.2V7.9L12,5.3M11,8V11H8V13H11V16H13V13H16V11H13V8H11Z";
            string iconMap = "M15,19L9,16.89V5L15,7.11M20.5,3C20.44,3 20.39,3 20.34,3L15,5.1L9,3L3.36,4.9C3.15,4.97 3,5.15 3,5.38V20.5A0.5,0.5 0 0,0 3.5,21C3.55,21 3.61,21 3.66,20.97L9,18.9L15,21L20.64,19.1C20.85,19 21,18.85 21,18.62V3.5A0.5,0.5 0 0,0 20.5,3Z";
            string iconOpord = "M19,3H14.82C14.4,1.84 13.3,1 12,1C10.7,1 9.6,1.84 9.18,3H5A2,2 0 0,0 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19V5A2,2 0 0,0 19,3M12,3A1,1 0 0,1 13,4A1,1 0 0,1 12,5A1,1 0 0,1 11,4A1,1 0 0,1 12,3M7,7H17V9H7V7M7,11H17V13H7V11M7,15H14V17H7V15Z";
            string iconUsb = "M9,3V5H7A2,2 0 0,0 5,7V9H3V11H5V13H3V15H5V17A2,2 0 0,0 7,19H9V21H11V19H13V21H15V19H17A2,2 0 0,0 19,17V15H21V13H19V11H21V9H19V7A2,2 0 0,0 17,5H15V3H13V5H11V3H9M7,7H17V17H7V7M9,9V15H15V9H9Z";
            string iconCoffee = "M2,21V19H20V21H2M20,8V5H18V8H20M20,3A2,2 0 0,1 22,5V8A2,2 0 0,1 20,10H18V13A4,4 0 0,1 14,17H8A4,4 0 0,1 4,13V3H20M16,5H6V13A2,2 0 0,0 8,15H14A2,2 0 0,0 16,13V5Z";

            _navBtnPackages = CreateSidebarNavButton(iconPkg, "Generator Paczek (.ftak)", () => SwitchActiveModule(win, BuilderActiveModule.PackageGenerator));
            topStack.Children.Add(_navBtnPackages);

            _navBtnMaps = CreateSidebarNavButton(iconMap, "Generator Map 1:1", () => SwitchActiveModule(win, BuilderActiveModule.MapGenerator));
            topStack.Children.Add(_navBtnMaps);

            _navBtnOpord = CreateSidebarNavButton(iconOpord, "Generator OPORD", () => SwitchActiveModule(win, BuilderActiveModule.OpordGenerator));
            topStack.Children.Add(_navBtnOpord);

            _navBtnUsb = CreateSidebarNavButton(iconUsb, "Programator Płytek USB", () => SwitchActiveModule(win, BuilderActiveModule.UsbProgrammer));
            topStack.Children.Add(_navBtnUsb);

            topStack.Children.Add(new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromRgb(25, 56, 68)),
                Margin = new Thickness(2, 12, 2, 12)
            });

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
                Text = string.IsNullOrEmpty(CurrentProjectFilePath)
                    ? "Projekt: Nowy (Stan zerowy)"
                    : ("Projekt: " + Path.GetFileName(CurrentProjectFilePath)),
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
                V34CleanFtakAndFactionEngine.GetNextUniqueFactionColorHex(null),
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

            _expandedSidebarContent.Children.Add(topStack);

            var bottomStack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
            DockPanel.SetDock(bottomStack, Dock.Bottom);

            _supportCreatorBtn = CreateSidebarActionButton(
                iconCoffee,
                "☕ Wesprzyj twórcę",
                "#FF9F43",
                () => InvokeSupportCreatorAction(win));
            _supportCreatorBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(180, 110, 35));
            bottomStack.Children.Add(_supportCreatorBtn);

            _langSwitchBtn = CreateSidebarActionButton(
                "M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M11,19.93C7.05,19.44 4,16.08 4,12C4,11.38 4.08,10.78 4.21,10.21L9,15V16A2,2 0 0,0 11,18M18.9,17.39C18.64,16.58 17.9,16 17,16H16V13A1,1 0 0,0 15,12H9V10H11A1,1 0 0,0 12,9V7H14A2,2 0 0,0 16,5V4.59C18.93,5.77 21,8.64 21,12C21,14.08 20.2,15.97 18.9,17.39Z",
                "Język UI: PL / ENG",
                "#1FE0C5",
                () => BuilderI18n.ToggleLanguage(win));
            bottomStack.Children.Add(_langSwitchBtn);

            _expandedSidebarContent.Children.Add(bottomStack);
            rootContainerGrid.Children.Add(_expandedSidebarContent);

            _collapsedSidebarStrip = new StackPanel
            {
                Visibility = Visibility.Collapsed,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var expandArrowBtn = CreateArrowToggleButton(
                isCollapseLeft: false,
                BuilderI18n.T("Rozwiń pasek boczny (▶)", "Expand sidebar (▶)"),
                ToggleSidebarCollapsed);
            expandArrowBtn.Margin = new Thickness(0, 0, 0, 14);
            _collapsedSidebarStrip.Children.Add(expandArrowBtn);

            _miniBtnPackages = CreateCollapsedIconOnlyButton(iconPkg, "Generator Paczek (.ftak)", () => SwitchActiveModule(win, BuilderActiveModule.PackageGenerator));
            _miniBtnMaps = CreateCollapsedIconOnlyButton(iconMap, "Generator Map 1:1", () => SwitchActiveModule(win, BuilderActiveModule.MapGenerator));
            _miniBtnOpord = CreateCollapsedIconOnlyButton(iconOpord, "Generator OPORD", () => SwitchActiveModule(win, BuilderActiveModule.OpordGenerator));
            _miniBtnUsb = CreateCollapsedIconOnlyButton(iconUsb, "Programator Płytek USB", () => SwitchActiveModule(win, BuilderActiveModule.UsbProgrammer));
            _miniBtnSupport = CreateCollapsedIconOnlyButton(iconCoffee, "☕ Wesprzyj twórcę", () => InvokeSupportCreatorAction(win));

            _collapsedSidebarStrip.Children.Add(_miniBtnPackages);
            _collapsedSidebarStrip.Children.Add(_miniBtnMaps);
            _collapsedSidebarStrip.Children.Add(_miniBtnOpord);
            _collapsedSidebarStrip.Children.Add(_miniBtnUsb);
            _collapsedSidebarStrip.Children.Add(_miniBtnSupport);

            rootContainerGrid.Children.Add(_collapsedSidebarStrip);
            sidebarBorder.Child = rootContainerGrid;
            return sidebarBorder;
        }

        private static Button CreateArrowToggleButton(bool isCollapseLeft, string toolTipText, Action onClick)
        {
            string arrowPath = isCollapseLeft
                ? "M15.41,16.58L10.83,12L15.41,7.41L14,6L8,12L14,18L15.41,16.58Z"
                : "M8.59,16.58L13.17,12L8.59,7.41L10,6L16,12L10,18L8.59,16.58Z";

            var btn = new Button
            {
                Width = 28,
                Height = 28,
                Padding = new Thickness(0),
                ToolTip = toolTipText,
                Background = new SolidColorBrush(Color.FromRgb(14, 36, 45)),
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(1.2),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            btn.Content = CreateTacticalVectorIcon(arrowPath, TacticalConfigPanel.BrushAccent, 16);
            btn.Click += (_, __) => onClick?.Invoke();
            return btn;
        }

        private static Button CreateCollapsedIconOnlyButton(string svgPathData, string toolTipText, Action onClick)
        {
            var btn = new Button
            {
                Width = 32,
                Height = 36,
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(0),
                ToolTip = toolTipText,
                Background = new SolidColorBrush(Color.FromRgb(10, 24, 30)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(24, 50, 62)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            btn.Content = CreateTacticalVectorIcon(svgPathData, TacticalConfigPanel.BrushAccent, 17);
            btn.Click += (_, __) => onClick?.Invoke();
            return btn;
        }

        public static void SwitchActiveModule(Window win, BuilderActiveModule targetModule)
        {
            EnsureRootDockSidebar(win);

            _currentModule = targetModule;
            UpdateSidebarActiveButtonHighlight();

            var allElements = GetAllVisualDescendants(win).ToList();
            CaptureAndCollapseTopMenuBar(allElements);
                CollapseObsoleteBuilderModeSelector(allElements);
            CollapseLegacyLeftSidebarColumn(win, allElements);
            RemoveDuplicateInnerSidebars(win, allElements);

            var opordOverlay = OpordBuilderManager.CreateOrGetOpordOverlayView(win);
            if (opordOverlay != null && _rightWorkspaceGrid != null)
            {
                if (VisualTreeHelper.GetParent(opordOverlay) is Panel oldParent && oldParent != _rightWorkspaceGrid)
                {
                    oldParent.Children.Remove(opordOverlay);
                }
                if (!_rightWorkspaceGrid.Children.Contains(opordOverlay))
                {
                    Panel.SetZIndex(opordOverlay, 50);
                    _rightWorkspaceGrid.Children.Add(opordOverlay);
                }
            }

            var mapOverlay = allElements
                .OfType<FrameworkElement>()
                .FirstOrDefault(fe => string.Equals(fe.Tag?.ToString(), "INJECTED_MAP_OVERLAY", StringComparison.Ordinal));

            switch (targetModule)
            {
                case BuilderActiveModule.PackageGenerator:
                    if (opordOverlay != null) opordOverlay.Visibility = Visibility.Collapsed;
                    if (mapOverlay != null) mapOverlay.Visibility = Visibility.Collapsed;
                    if (_originalMainWindowContentHost != null) _originalMainWindowContentHost.Visibility = Visibility.Visible;

                    if (_originalBuilderTabButton != null)
                    {
                        _originalBuilderTabButton.IsEnabled = true;
                        _originalBuilderTabButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    }
                    break;

                case BuilderActiveModule.MapGenerator:
                    if (opordOverlay != null) opordOverlay.Visibility = Visibility.Collapsed;
                    if (_originalMainWindowContentHost != null) _originalMainWindowContentHost.Visibility = Visibility.Visible;

                    if (_originalMapTabButton != null)
                    {
                        _originalMapTabButton.IsEnabled = true;
                        _originalMapTabButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    }

                    mapOverlay = GetAllVisualDescendants(win)
                        .OfType<FrameworkElement>()
                        .FirstOrDefault(fe => string.Equals(fe.Tag?.ToString(), "INJECTED_MAP_OVERLAY", StringComparison.Ordinal));
                    if (mapOverlay != null)
                    {
                        mapOverlay.Visibility = Visibility.Visible;
                    }
                    break;

                case BuilderActiveModule.OpordGenerator:
                    if (mapOverlay != null) mapOverlay.Visibility = Visibility.Collapsed;
                    if (_originalMainWindowContentHost != null) _originalMainWindowContentHost.Visibility = Visibility.Collapsed;
                    if (opordOverlay != null)
                    {
                        opordOverlay.Visibility = Visibility.Visible;
                    }
                    break;

                case BuilderActiveModule.UsbProgrammer:
                    if (opordOverlay != null) opordOverlay.Visibility = Visibility.Collapsed;
                    if (mapOverlay != null) mapOverlay.Visibility = Visibility.Collapsed;
                    if (_originalMainWindowContentHost != null) _originalMainWindowContentHost.Visibility = Visibility.Visible;

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

            var updatedDescendants = GetAllVisualDescendants(win).ToList();
            CollapseObsoleteBuilderModeSelector(updatedDescendants);
            RemoveDuplicateInnerSidebars(win, updatedDescendants);
            BuilderI18n.ApplyLanguageToVisualTree(win);
            if (_injectedSidebarContainer != null)
            {
                _injectedSidebarContainer.Visibility = Visibility.Visible;
                Panel.SetZIndex(_injectedSidebarContainer, 99999);
            }
        
            
            
        
            V34CleanSidebarAndHeaderEngine.ApplySidebarAndHeaderFixes(Application.Current?.MainWindow);
        
            V34CleanFtakAndFactionEngine.ApplyFactionAndPackageFixes(Application.Current?.MainWindow);
            V34CleanMapStudioBridge.SyncMapStudioState(Application.Current?.MainWindow);
            V34CleanUsbProgrammerBridge.SyncUsbProgrammerUi(Application.Current?.MainWindow);
            V34CleanOpordDataEngine.OnSwitchedToOpordTab(Application.Current?.MainWindow, "OPORD");
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

            SetNavButtonActiveState(_miniBtnPackages, _currentModule == BuilderActiveModule.PackageGenerator);
            SetNavButtonActiveState(_miniBtnMaps, _currentModule == BuilderActiveModule.MapGenerator);
            SetNavButtonActiveState(_miniBtnOpord, _currentModule == BuilderActiveModule.OpordGenerator);
            SetNavButtonActiveState(_miniBtnUsb, _currentModule == BuilderActiveModule.UsbProgrammer);
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
            SetButtonLabel(_supportCreatorBtn, BuilderI18n.T("☕ Wesprzyj twórcę", "☕ Support Creator"));
            SetButtonLabel(_langSwitchBtn, BuilderI18n.T("Język UI: Polski (PL -> ENG)", "UI Language: English (ENG -> PL)"));

            if (_activeProjectFileLabel != null)
            {
                _activeProjectFileLabel.Text = string.IsNullOrEmpty(CurrentProjectFilePath)
                    ? BuilderI18n.T("Projekt: Nowy (Stan zerowy)", "Project: New (Zero-State)")
                    : (BuilderI18n.T("Projekt: ", "Project: ") + Path.GetFileName(CurrentProjectFilePath));
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









// [START_V34_CLEAN_SIDEBAR_ENGINE]
    public static class V34CleanSidebarAndHeaderEngine
    {
        public static event Action<Window> OnApplyAdditionalModuleFixes;

        public static void ApplySidebarAndHeaderFixes(Window win)
        {
            var w = win ?? Application.Current?.MainWindow;
            if (w == null) return;

            try
            {
                if (w.MinWidth > 900) w.MinWidth = 900;
                if (w.MinHeight > 560) w.MinHeight = 560;

                RemoveLegacyTopBarAndBlackHeaderGap(w);
                UpdateSidebarIconsAndCollapseButton(w);

                OnApplyAdditionalModuleFixes?.Invoke(w);
            }
            catch { }
        }

        /// <summary>
        /// Usuwa pozostalosci po starym gornym pasku (dwie poziome linie cyjanowe TRYB PRACY BUILDERA)
        /// oraz czarna przerwe nad naglowkiem BUILDER (zeruje stary RowDefinition i pusty Border).
        /// </summary>
        public static void RemoveLegacyTopBarAndBlackHeaderGap(Window win)
        {
            var elements = GetLogicalTreeNodes(win);

            foreach (var tb in elements.OfType<TextBlock>())
            {
                string txt = (tb.Text ?? "").Trim();

                // 1. Całkowite usunięcie starego paska "TRYB PRACY BUILDERA" / "BUILDER MODE" i jego cyjanowych linii
                if (txt.Contains("TRYB PRACY BUILDERA", StringComparison.OrdinalIgnoreCase) ||
                    txt.Contains("BUILDER MODE", StringComparison.OrdinalIgnoreCase))
                {
                    DependencyObject cur = tb;
                    for (int depth = 0; depth < 6 && cur != null && !(cur is Window); depth++)
                    {
                        if (cur is Border b)
                        {
                            b.Visibility = Visibility.Collapsed;
                            b.Height = 0;
                            b.MinHeight = 0;
                            b.Margin = new Thickness(0);
                            b.Padding = new Thickness(0);
                            b.BorderThickness = new Thickness(0);
                            b.BorderBrush = Brushes.Transparent;
                            b.Background = Brushes.Transparent;

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

                // 2. Usunięcie czarnej przerwy nad nagłówkiem BUILDER
                if (txt.StartsWith("BUILDER", StringComparison.OrdinalIgnoreCase) && tb.FontSize >= 15)
                {
                    tb.Margin = new Thickness(tb.Margin.Left, 0, tb.Margin.Right, tb.Margin.Bottom);

                    DependencyObject p = LogicalTreeHelper.GetParent(tb) ?? VisualTreeHelper.GetParent(tb);
                    for (int step = 0; step < 5 && p != null && !(p is Window); step++)
                    {
                        if (p is FrameworkElement fe && fe.Margin.Top > 4)
                        {
                            fe.Margin = new Thickness(fe.Margin.Left, 0, fe.Margin.Right, fe.Margin.Bottom);
                        }
                        if (p is Border pb && pb.Padding.Top > 6)
                        {
                            pb.Padding = new Thickness(pb.Padding.Left, 4, pb.Padding.Right, pb.Padding.Bottom);
                        }
                        if (p is Grid g && g.RowDefinitions.Count >= 2)
                        {
                            bool row0HasRealContent = false;
                            foreach (UIElement ch in g.Children)
                            {
                                if (ch != null && Grid.GetRow(ch) == 0 && ch.Visibility == Visibility.Visible)
                                {
                                    if (ch is Border rb && (rb.Height == 0 || rb.Child == null))
                                        continue;
                                    row0HasRealContent = true;
                                    break;
                                }
                            }
                            if (!row0HasRealContent)
                            {
                                g.RowDefinitions[0].Height = new GridLength(0);
                                g.RowDefinitions[0].MinHeight = 0;
                            }
                        }
                        p = LogicalTreeHelper.GetParent(p) ?? VisualTreeHelper.GetParent(p);
                    }
                }
            }

            // 3. Wyzerowanie pustych ramek z poziomymi liniami podziału
            foreach (var b in elements.OfType<Border>())
            {
                if (b.Child == null && b.Height <= 4 && (b.BorderThickness.Top >= 1 || b.BorderThickness.Bottom >= 1))
                {
                    b.BorderThickness = new Thickness(0);
                    b.Height = 0;
                    b.Visibility = Visibility.Collapsed;
                }
            }
        }

        /// <summary>
        /// Pasek boczny (Sidebar): Rozróżnia przycisk zwijania (❮/❯) od przycisków modułów
        /// (sprawdza, czy zawartością przycisku jest Panel z ikoną), dzięki czemu po schowaniu
        /// paska bocznego wszystkie ikony modułów są w pełni widoczne i wyśrodkowane.
        /// </summary>
        public static void UpdateSidebarIconsAndCollapseButton(Window win)
        {
            var elements = GetLogicalTreeNodes(win);

            Border sidebarBorder = null;
            foreach (var b in elements.OfType<Border>())
            {
                if (Grid.GetColumn(b) == 0 && (b.Width >= 44 && b.Width <= 290 || b.ActualWidth >= 44 && b.ActualWidth <= 290))
                {
                    var btns = GetLogicalTreeNodes(b).OfType<Button>().ToList();
                    if (btns.Count >= 4)
                    {
                        sidebarBorder = b;
                        break;
                    }
                }
            }

            if (sidebarBorder == null) return;

            double currentWidth = sidebarBorder.ActualWidth > 0 ? sidebarBorder.ActualWidth : sidebarBorder.Width;
            bool isCollapsed = currentWidth > 0 && currentWidth < 95;

            var sideButtons = GetLogicalTreeNodes(sidebarBorder).OfType<Button>().ToList();
            for (int i = 0; i < sideButtons.Count; i++)
            {
                var btn = sideButtons[i];

                // A. Przycisk modułu (jego zawartością jest Panel zawierający ikonę i tytuł)
                if (btn.Content is Panel modulePanel)
                {
                    var textBlocks = modulePanel.Children.OfType<TextBlock>().ToList();
                    if (isCollapsed)
                    {
                        btn.Padding = new Thickness(0);
                        btn.HorizontalContentAlignment = HorizontalAlignment.Center;
                        btn.VerticalContentAlignment = VerticalAlignment.Center;

                        modulePanel.HorizontalAlignment = HorizontalAlignment.Center;
                        modulePanel.VerticalAlignment = VerticalAlignment.Center;
                        modulePanel.Margin = new Thickness(0);

                        if (textBlocks.Count >= 1)
                        {
                            textBlocks[0].Visibility = Visibility.Visible;
                            textBlocks[0].Margin = new Thickness(0);
                            textBlocks[0].Padding = new Thickness(0);
                            textBlocks[0].Width = double.NaN;
                            textBlocks[0].FontSize = 18;
                            textBlocks[0].TextAlignment = TextAlignment.Center;
                            textBlocks[0].HorizontalAlignment = HorizontalAlignment.Center;
                            textBlocks[0].VerticalAlignment = VerticalAlignment.Center;
                        }

                        for (int t = 1; t < textBlocks.Count; t++)
                        {
                            textBlocks[t].Visibility = Visibility.Collapsed;
                        }
                    }
                    else
                    {
                        btn.Padding = new Thickness(12, 8, 12, 8);
                        btn.HorizontalContentAlignment = HorizontalAlignment.Left;
                        btn.VerticalContentAlignment = VerticalAlignment.Center;

                        modulePanel.HorizontalAlignment = HorizontalAlignment.Left;

                        if (textBlocks.Count >= 1)
                        {
                            textBlocks[0].Visibility = Visibility.Visible;
                            textBlocks[0].Margin = new Thickness(0, 0, 10, 0);
                            textBlocks[0].FontSize = 15;
                        }

                        for (int t = 1; t < textBlocks.Count; t++)
                        {
                            textBlocks[t].Visibility = Visibility.Visible;
                        }
                    }
                    continue;
                }

                // B. Przycisk zwijania / rozwijania paska bocznego (nie jest Panelem z ikoną modułu)
                string rawText = (btn.Content?.ToString() ?? "").Trim();
                if (i == 0 || rawText == "<" || rawText == ">" || rawText == "❮" || rawText == "❯" || rawText == "«" || rawText == "»" || btn.Width <= 42)
                {
                    btn.Padding = new Thickness(0);
                    btn.HorizontalContentAlignment = HorizontalAlignment.Center;
                    btn.VerticalContentAlignment = VerticalAlignment.Center;
                    btn.FontSize = 14;
                    btn.FontWeight = FontWeights.Bold;
                    btn.Content = isCollapsed ? "❯" : "❮";

                    if (btn.Tag?.ToString() != "V34_SIDEBAR_TOGGLE_HOOKED")
                    {
                        btn.Tag = "V34_SIDEBAR_TOGGLE_HOOKED";
                        btn.Click += (_, __) =>
                        {
                            win.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                UpdateSidebarIconsAndCollapseButton(win);
                            }), System.Windows.Threading.DispatcherPriority.Loaded);
                        };
                    }
                }
            }
        }

        public static List<DependencyObject> GetLogicalTreeNodes(DependencyObject root)
        {
            var result = new List<DependencyObject>(128);
            if (root == null) return result;

            var visited = new HashSet<DependencyObject>();
            var queue = new Queue<DependencyObject>(64);
            queue.Enqueue(root);
            visited.Add(root);

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                result.Add(cur);

                if (cur is Panel panel)
                {
                    foreach (UIElement child in panel.Children)
                    {
                        if (child != null && visited.Add(child))
                            queue.Enqueue(child);
                    }
                }
                else if (cur is Decorator dec && dec.Child != null && visited.Add(dec.Child))
                {
                    queue.Enqueue(dec.Child);
                }
                else if (cur is ScrollViewer sv && sv.Content is DependencyObject svContent && visited.Add(svContent))
                {
                    queue.Enqueue(svContent);
                }
                else if (cur is ContentControl cc && !(cur is ButtonBase) && !(cur is ComboBoxItem) && !(cur is ListBoxItem) && cc.Content is DependencyObject ccContent && visited.Add(ccContent))
                {
                    queue.Enqueue(ccContent);
                }
            }

            return result;
        }
    }
    // [END_V34_CLEAN_SIDEBAR_ENGINE]



// [START_V34_CLEAN_FTAK_ENGINE]
    public sealed class V34FactionVoiceChannelRow
    {
        public string Role { get; set; } = "Dowodzenie HQ";
        public string Band { get; set; } = "PMR";
        public string Freq { get; set; } = "CH 1 (446.00625 MHz)";
        public string Tone { get; set; } = "CTCSS 67.0 Hz";
    }

    public sealed class V34FactionSnapshot
    {
        public string FactionName { get; set; } = "ORGA";
        public string ColorHex { get; set; } = "#2684FF";
        public string ChannelName { get; set; } = "ORGA-MESH";
        public string Region { get; set; } = "EU_868";
        public string ModemPreset { get; set; } = "SHORT_FAST";
        public string OtsHost { get; set; } = "";
        public string EmergencyPhone { get; set; } = "Brak / 112";
        public int ExpirationHours { get; set; } = 0;
        public List<V34FactionVoiceChannelRow> VoiceRows { get; set; } = new List<V34FactionVoiceChannelRow>();
    }

    public static class V34CleanFtakAndFactionEngine
    {
        private static readonly string[] TacticalRgbPalette = new string[]
        {
            "#2684FF", "#EB4034", "#2ECC71", "#FAD02C",
            "#9B59B6", "#FF9F43", "#1FE0C5", "#E84393",
            "#A3CB38", "#00CEC9", "#FF6B6B", "#54A0FF",
            "#5F27CD", "#10AC84", "#F368E0", "#FF9FF3"
        };

        private static readonly HashSet<string> _allocatedColors = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "#2684FF"
        };

        public static List<V34FactionSnapshot> CachedFactions { get; } = new List<V34FactionSnapshot>();

        public static string GetNextUniqueFactionColorHex(Window win = null)
        {
            try
            {
                var w = win ?? Application.Current?.MainWindow;
                if (w != null)
                {
                    var all = V34CleanSidebarAndHeaderEngine.GetLogicalTreeNodes(w);
                    foreach (var btn in all.OfType<Button>())
                    {
                        string bt = ExtractButtonTextSafe(btn);
                        Match m = Regex.Match(bt, @"#[0-9A-Fa-f]{6}");
                        if (m.Success) _allocatedColors.Add(m.Value.ToUpperInvariant());
                    }
                }
            }
            catch { }

            var rng = new Random(Guid.NewGuid().GetHashCode());
            var free = TacticalRgbPalette.Where(c => !_allocatedColors.Contains(c)).OrderBy(_ => rng.Next()).ToList();
            string chosen;
            if (free.Count > 0)
            {
                chosen = free[0];
            }
            else
            {
                do
                {
                    chosen = string.Format("#{0:X2}{1:X2}{2:X2}", rng.Next(45, 240), rng.Next(55, 240), rng.Next(45, 240));
                } while (_allocatedColors.Contains(chosen));
            }

            _allocatedColors.Add(chosen);
            return chosen;
        }

        public static string EnsureSharedFolderStructure()
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FieldTakHub");
            string wspolne = Path.Combine(root, "wspolne");
            Directory.CreateDirectory(Path.Combine(wspolne, "apk"));
            Directory.CreateDirectory(Path.Combine(wspolne, "plugins"));
            Directory.CreateDirectory(Path.Combine(wspolne, "maps"));
            Directory.CreateDirectory(Path.Combine(root, "out"));
            return wspolne;
        }

        public static string EnsureFactionOutputDir(string factionName)
        {
            EnsureSharedFolderStructure();
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FieldTakHub");
            string outDir = Path.Combine(root, "out", SanitizeFolderName(factionName));
            Directory.CreateDirectory(outDir);
            return outDir;
        }

        public static string SanitizeFileName(string name) => SanitizeFolderName(name);
        public static string SanitizeFolderName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "ORGA";
            char[] inv = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder();
            foreach (char c in name.Trim())
            {
                sb.Append(Array.IndexOf(inv, c) >= 0 ? '_' : c);
            }
            string r = sb.ToString().Trim();
            return string.IsNullOrEmpty(r) ? "ORGA" : r;
        }

        public static void ApplyFactionAndPackageFixes(Window win)
        {
            var w = win ?? Application.Current?.MainWindow;
            if (w == null) return;

            EnsureSharedFolderStructure();
            var all = ScanAllWindowRoots(w);

            // 1. Etykieta "(0 = bezterminowo)" w polach ważności paczek i kodów QR
            foreach (var tb in all.OfType<TextBlock>())
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

            // 2. Wymuszenie unikalnych kolorów RGB i nazw STRONA-X dla nowo dodanych kart stron
            EnforceUniqueColorsAndNamesOnCards(all);

            // 3. Odblokowanie przycisku "📂 Pliki strony", naprawa małych przycisków ("+", "−") oraz Plakat Safezone QR
            foreach (var btn in all.OfType<Button>())
            {
                string txt = ExtractButtonTextSafe(btn).Trim();

                if ((btn.Width >= 20 && btn.Width <= 42) || txt == "+" || txt == "-" || txt == "−" || txt == "✖" || txt == ":")
                {
                    btn.Padding = new Thickness(0);
                    btn.HorizontalContentAlignment = HorizontalAlignment.Center;
                    btn.VerticalContentAlignment = VerticalAlignment.Center;
                    if (txt == "-" || txt == ":") btn.Content = "−";
                }

                if ((txt == "+" || txt.Contains("Dodaj stronę", StringComparison.OrdinalIgnoreCase) || txt.Contains("Dodaj kanał", StringComparison.OrdinalIgnoreCase)) &&
                    btn.Tag?.ToString() != "V34_ADD_BTN_HOOKED")
                {
                    btn.Tag = "V34_ADD_BTN_HOOKED";
                    btn.Click += (_, __) =>
                    {
                        w.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            ApplyFactionAndPackageFixes(w);
                        }), System.Windows.Threading.DispatcherPriority.Loaded);
                    };
                }

                if ((txt.Contains("Pliki str", StringComparison.OrdinalIgnoreCase) || txt.StartsWith("factions/", StringComparison.OrdinalIgnoreCase)) &&
                    btn.Tag?.ToString() != "V34_FILES_BTN_OK")
                {
                    btn.Tag = "V34_FILES_BTN_OK";
                    btn.IsEnabled = true;
                    btn.Visibility = Visibility.Visible;
                    if (txt.Contains("Pliki str", StringComparison.OrdinalIgnoreCase))
                        btn.Content = "📂 Pliki strony";

                    btn.IsEnabledChanged += (s, e) =>
                    {
                        if (s is Button b && !b.IsEnabled) b.IsEnabled = true;
                    };

                    btn.PreviewMouseLeftButtonDown += (s, e) =>
                    {
                        e.Handled = true;
                        try
                        {
                            RefreshCachedFactions(w, null);
                            string facName = "ORGA";
                            if (txt.Contains("/"))
                                facName = txt.Split('/').Last().Trim().ToUpperInvariant();
                            else if (CachedFactions.Count > 0)
                                facName = CachedFactions[0].FactionName;

                            string dir = EnsureFactionOutputDir(facName);
                            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true });
                        }
                        catch { }
                    };
                }

                if ((txt.Contains("Plakat", StringComparison.OrdinalIgnoreCase) || txt.Contains("Safezone", StringComparison.OrdinalIgnoreCase)) &&
                    btn.Tag?.ToString() != "V34_POSTER_SAFE_OK")
                {
                    btn.Tag = "V34_POSTER_SAFE_OK";
                    btn.IsEnabled = true;
                    btn.PreviewMouseLeftButtonDown += (s, e) =>
                    {
                        e.Handled = true;
                        GenerateSafezonePosterWithoutCrash(w);
                    };
                }
            }

            // 4. Przebudowa karty Generator Paczek .FTAK v3.0 do dokładnie 3 przycisków
            UpgradeFtakCardToThreeButtons(w, all);

            // 5. Odświeżenie migawki stron konfliktu i kanałów głosowych
            RefreshCachedFactions(w, all);
        }

        private static void EnforceUniqueColorsAndNamesOnCards(List<DependencyObject> all)
        {
            var usedColors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var headers = all.OfType<TextBlock>()
                .Where(t => (t.Text ?? "").Contains("Generator Stron —", StringComparison.OrdinalIgnoreCase))
                .ToList();

            for (int i = 0; i < headers.Count; i++)
            {
                var hdr = headers[i];
                var card = FindFactionCardContainer(hdr);
                if (card == null) continue;

                var elems = V34CleanSidebarAndHeaderEngine.GetLogicalTreeNodes(card);
                var firstBox = elems.OfType<TextBox>().FirstOrDefault();
                if (firstBox != null)
                {
                    string curName = (firstBox.Text ?? "").Trim();
                    if (usedNames.Contains(curName) && curName.StartsWith("STRONA-", StringComparison.OrdinalIgnoreCase))
                    {
                        int n = i + 1;
                        while (usedNames.Contains("STRONA-" + n)) n++;
                        curName = "STRONA-" + n;
                        firstBox.Text = curName;
                        hdr.Text = "Generator Stron — " + curName;
                    }
                    if (!string.IsNullOrWhiteSpace(curName))
                        usedNames.Add(curName);
                }

                var colorBtn = elems.OfType<Button>().FirstOrDefault(b => Regex.IsMatch(ExtractButtonTextSafe(b), @"#[0-9A-Fa-f]{6}"));
                if (colorBtn != null)
                {
                    Match m = Regex.Match(ExtractButtonTextSafe(colorBtn), @"#[0-9A-Fa-f]{6}");
                    if (m.Success)
                    {
                        string hex = m.Value.ToUpperInvariant();
                        if (usedColors.Contains(hex))
                        {
                            hex = GetNextUniqueFactionColorHex(null);
                            ApplyHexColorToButton(colorBtn, hdr, hex);
                        }
                        usedColors.Add(hex);
                        _allocatedColors.Add(hex);
                    }
                }
            }
        }

        private static void ApplyHexColorToButton(Button colorBtn, TextBlock hdrTb, string hex)
        {
            try
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                if (colorBtn.Content is string)
                {
                    colorBtn.Content = "■ " + hex;
                }
                else if (colorBtn.Content is Panel p)
                {
                    foreach (var b in p.Children.OfType<Border>()) b.Background = brush;
                    foreach (var s in p.Children.OfType<System.Windows.Shapes.Shape>()) s.Fill = brush;
                    foreach (var t in p.Children.OfType<TextBlock>())
                        if (t.Text != null && t.Text.Contains("#")) t.Text = hex;
                }
                colorBtn.BorderBrush = brush;

                if (LogicalTreeHelper.GetParent(hdrTb) is Panel hdrParent)
                {
                    foreach (var s in hdrParent.Children.OfType<System.Windows.Shapes.Shape>()) s.Fill = brush;
                    foreach (var b in hdrParent.Children.OfType<Border>())
                        if (b.Width <= 20 && b.Height <= 20) b.Background = brush;
                }
            }
            catch { }
        }

        private static void UpgradeFtakCardToThreeButtons(Window win, List<DependencyObject> all)
        {
            foreach (var tb in all.OfType<TextBlock>())
            {
                string t = (tb.Text ?? "").Trim();
                if (t.Contains("GENERATOR PACZEK .FTAK", StringComparison.OrdinalIgnoreCase))
                {
                    DependencyObject p = LogicalTreeHelper.GetParent(tb) ?? VisualTreeHelper.GetParent(tb);
                    while (p != null && !(p is StackPanel || p is Grid))
                    {
                        p = LogicalTreeHelper.GetParent(p) ?? VisualTreeHelper.GetParent(p);
                    }

                    if (p is Panel cardPanel && cardPanel.Tag?.ToString() != "V34_FTAK_3BTNS_READY")
                    {
                        cardPanel.Tag = "V34_FTAK_3BTNS_READY";
                        tb.Text = "🎯 GENERATOR PACZEK .FTAK (FOLDER out\\<STRONA>\\ + WSPÓLNY FOLDER APK / PLUGINY / MAPY)";

                        var sub = V34CleanSidebarAndHeaderEngine.GetLogicalTreeNodes(cardPanel);
                        foreach (var cb in sub.OfType<ComboBox>())
                        {
                            cb.Visibility = Visibility.Collapsed;
                            cb.Width = 0;
                            cb.Height = 0;
                        }

                        Button origBuildAll = null;
                        foreach (var b in sub.OfType<Button>())
                        {
                            if (ExtractButtonTextSafe(b).Contains("WSZYSTKIE", StringComparison.OrdinalIgnoreCase))
                                origBuildAll = b;
                            b.Visibility = Visibility.Collapsed;
                            b.Height = 0;
                            b.Margin = new Thickness(0);
                        }

                        var wrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 6) };

                        var b1 = new Button
                        {
                            Content = "⚡ Buduj wszystkie aktywne strony",
                            Height = 38,
                            MinWidth = 220,
                            Padding = new Thickness(14, 6, 14, 6),
                            Margin = new Thickness(0, 4, 10, 4),
                            Background = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                            Foreground = new SolidColorBrush(Color.FromRgb(5, 16, 20)),
                            FontWeight = FontWeights.Bold,
                            Cursor = System.Windows.Input.Cursors.Hand
                        };
                        b1.Click += (_, __) =>
                        {
                            EnsureSharedFolderStructure();
                            foreach (var f in CachedFactions) EnsureFactionOutputDir(f.FactionName);
                            if (origBuildAll != null)
                                origBuildAll.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        };

                        var b2 = new Button
                        {
                            Content = "📂 Otwórz wspólny folder (APK / Pluginy / Mapy)",
                            Height = 38,
                            MinWidth = 220,
                            Padding = new Thickness(14, 6, 14, 6),
                            Margin = new Thickness(0, 4, 10, 4),
                            Background = new SolidColorBrush(Color.FromRgb(42, 34, 12)),
                            Foreground = new SolidColorBrush(Color.FromRgb(250, 208, 44)),
                            BorderBrush = new SolidColorBrush(Color.FromRgb(250, 208, 44)),
                            BorderThickness = new Thickness(1.5),
                            FontWeight = FontWeights.Bold,
                            Cursor = System.Windows.Input.Cursors.Hand
                        };
                        b2.Click += (_, __) =>
                        {
                            string wsp = EnsureSharedFolderStructure();
                            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + wsp + "\"") { UseShellExecute = true });
                        };

                        var b3 = new Button
                        {
                            Content = "📡 Dystrybucja przez HOTSPOT Wi-Fi",
                            Height = 38,
                            MinWidth = 220,
                            Padding = new Thickness(14, 6, 14, 6),
                            Margin = new Thickness(0, 4, 0, 4),
                            Background = new SolidColorBrush(Color.FromRgb(14, 42, 54)),
                            Foreground = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                            BorderBrush = new SolidColorBrush(Color.FromRgb(31, 224, 197)),
                            BorderThickness = new Thickness(1.5),
                            FontWeight = FontWeights.Bold,
                            Cursor = System.Windows.Input.Cursors.Hand
                        };
                        b3.Click += (_, __) =>
                        {
                            GenerateSafezonePosterWithoutCrash(win);
                        };

                        wrap.Children.Add(b1);
                        wrap.Children.Add(b2);
                        wrap.Children.Add(b3);
                        cardPanel.Children.Add(wrap);
                    }
                }
            }
        }

        public static void GenerateSafezonePosterWithoutCrash(Window win)
        {
            try
            {
                RefreshCachedFactions(win, null);
                var snap = CachedFactions.FirstOrDefault() ?? new V34FactionSnapshot();
                string outDir = EnsureFactionOutputDir(snap.FactionName);
                string htmlPath = Path.Combine(outDir, "Plakat_Safezone_" + SanitizeFolderName(snap.FactionName) + ".html");

                string qrPayload = Uri.EscapeDataString("{\"Faction\":\"" + snap.FactionName + "\",\"Channel\":\"" + snap.ChannelName + "\",\"Region\":\"" + snap.Region + "\",\"ExpirationHours\":" + snap.ExpirationHours + "}");

                StringBuilder rows = new StringBuilder();
                foreach (var r in snap.VoiceRows)
                {
                    rows.AppendLine("<tr><td><b>" + System.Net.WebUtility.HtmlEncode(r.Role) + "</b></td><td>" +
                                    System.Net.WebUtility.HtmlEncode(r.Band) + "</td><td><b>" +
                                    System.Net.WebUtility.HtmlEncode(r.Freq) + "</b></td><td>" +
                                    System.Net.WebUtility.HtmlEncode(r.Tone) + "</td></tr>");
                }

                string expLabel = snap.ExpirationHours <= 0 ? "Bezterminowo (0)" : (snap.ExpirationHours + " h");

                string html = "<!DOCTYPE html><html><head><meta charset='utf-8'/><title>Plakat Safezone A4 — " + System.Net.WebUtility.HtmlEncode(snap.FactionName) + "</title>" +
                              "<style>body{font-family:'Segoe UI',Arial,sans-serif;background:#071115;color:#fff;padding:18px;}.sheet{max-width:190mm;margin:0 auto;border:3px solid " + System.Net.WebUtility.HtmlEncode(snap.ColorHex) + ";border-radius:12px;padding:18px;background:#0A181E;}table{width:100%;border-collapse:collapse;margin-top:12px;}th,td{border:1px solid #224A59;padding:7px;text-align:left;}th{background:#122C36;color:#1FE0C5;}</style></head><body>" +
                              "<div class='sheet'><h1>🛡️ STRONA: " + System.Net.WebUtility.HtmlEncode(snap.FactionName) + "</h1>" +
                              "<p><b>Ważność paczki / QR:</b> " + System.Net.WebUtility.HtmlEncode(expLabel) + " &nbsp;|&nbsp; <b>Telefon ORGA / SOS:</b> " + System.Net.WebUtility.HtmlEncode(snap.EmergencyPhone) + "</p>" +
                              "<div style='background:#fff;color:#000;padding:14px;border-radius:8px;width:230px;text-align:center;margin:12px auto;'><img src='https://api.qrserver.com/v1/create-qr-code/?size=210x210&data=" + qrPayload + "' width='210' height='210'/><br/><b>ZESKANUJ W FIELD TAK HUB</b></div>" +
                              "<p><b>LoRa Meshtastic:</b> " + System.Net.WebUtility.HtmlEncode(snap.ChannelName) + " (" + System.Net.WebUtility.HtmlEncode(snap.Region) + " / " + System.Net.WebUtility.HtmlEncode(snap.ModemPreset) + ")</p>" +
                              "<table><thead><tr><th>Rola</th><th>Pasmo</th><th>Częstotliwość / Kanał</th><th>Ton / Kod</th></tr></thead><tbody>" + rows + "</tbody></table></div></body></html>";

                File.WriteAllText(htmlPath, html, Encoding.UTF8);
                Process.Start(new ProcessStartInfo(htmlPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Błąd generowania plakatu Safezone: " + ex.Message, "Field TAK Hub", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        public static DependencyObject FindFactionCardContainer(TextBlock hdrTb)
        {
            if (hdrTb == null) return null;
            DependencyObject cur = hdrTb;
            DependencyObject bestBorder = null;

            for (int depth = 0; depth < 9 && cur != null; depth++)
            {
                cur = LogicalTreeHelper.GetParent(cur) ?? VisualTreeHelper.GetParent(cur);
                if (cur is Border || cur is Panel)
                {
                    var elems = V34CleanSidebarAndHeaderEngine.GetLogicalTreeNodes(cur);
                    var texts = elems.OfType<TextBlock>().Select(x => x.Text ?? "").ToList();
                    int hdrCount = texts.Count(x => x.Contains("Generator Stron —", StringComparison.OrdinalIgnoreCase));
                    if (hdrCount > 1) break;

                    if (cur is Border b && b.BorderThickness.Top >= 1)
                        bestBorder = b;

                    if (hdrCount == 1 &&
                        texts.Any(x => x.Contains("Nazwa strony", StringComparison.OrdinalIgnoreCase)) &&
                        texts.Any(x => x.Contains("Kanały Głosowe", StringComparison.OrdinalIgnoreCase)))
                    {
                        return cur;
                    }
                }
            }
            return bestBorder;
        }

        public static List<V34FactionSnapshot> RefreshCachedFactions(Window win, List<DependencyObject> preScanned = null)
        {
            var list = new List<V34FactionSnapshot>();
            try
            {
                var all = preScanned ?? ScanAllWindowRoots(win ?? Application.Current?.MainWindow);
                var headers = all.OfType<TextBlock>()
                    .Where(t => (t.Text ?? "").Contains("Generator Stron —", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var hdr in headers)
                {
                    var card = FindFactionCardContainer(hdr);
                    if (card == null) continue;

                    var items = V34CleanSidebarAndHeaderEngine.GetLogicalTreeNodes(card);
                    var boxes = items.OfType<TextBox>().ToList();
                    var snap = new V34FactionSnapshot();

                    snap.FactionName = (boxes.Count > 0 && !string.IsNullOrWhiteSpace(boxes[0].Text))
                        ? boxes[0].Text.Trim()
                        : (hdr.Text ?? "").Replace("Generator Stron —", "").Trim();

                    if (string.IsNullOrWhiteSpace(snap.FactionName) ||
                        snap.FactionName.Equals("PLI", StringComparison.OrdinalIgnoreCase) ||
                        snap.FactionName.Equals("GeoChat", StringComparison.OrdinalIgnoreCase) ||
                        snap.FactionName.Equals("TAK", StringComparison.OrdinalIgnoreCase))
                    {
                        snap.FactionName = "ORGA";
                    }

                    snap.ChannelName = snap.FactionName.ToUpperInvariant() + "-MESH";

                    var colBtn = items.OfType<Button>().FirstOrDefault(b => Regex.IsMatch(ExtractButtonTextSafe(b), @"#[0-9A-Fa-f]{6}"));
                    if (colBtn != null)
                    {
                        Match m = Regex.Match(ExtractButtonTextSafe(colBtn), @"#[0-9A-Fa-f]{6}");
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

                    foreach (var panel in items.OfType<Panel>())
                    {
                        var directInputs = panel.Children.Cast<UIElement>().Where(x => x is TextBox || x is ComboBox).ToList();
                        if (directInputs.Count >= 2 && directInputs.Any(x => x is TextBox))
                        {
                            bool isNameRow = panel.Children.OfType<StackPanel>().Any(sp => sp.Children.OfType<TextBlock>().Any(t => (t.Text ?? "").Contains("Nazwa strony", StringComparison.OrdinalIgnoreCase)));
                            if (isNameRow) continue;

                            string ReadVal(UIElement el)
                            {
                                if (el is TextBox tbx) return (tbx.Text ?? "").Trim();
                                if (el is ComboBox cbx) return ((cbx.SelectedItem is ComboBoxItem cbi ? cbi.Content?.ToString() : cbx.SelectedItem?.ToString()) ?? cbx.Text ?? "").Trim();
                                return "";
                            }

                            var vals = directInputs.Select(ReadVal).ToList();
                            string role = vals[0];
                            string band = vals.Count >= 2 ? vals[1] : "PMR";
                            string freq = vals.Count >= 3 ? vals[2] : vals[1];
                            string tone = vals.Count >= 4 ? vals[3] : "-";

                            if (role.Equals("PMR", StringComparison.OrdinalIgnoreCase) ||
                                role.Equals("VHF", StringComparison.OrdinalIgnoreCase) ||
                                role.Equals("UHF", StringComparison.OrdinalIgnoreCase) ||
                                role.Equals("DMR", StringComparison.OrdinalIgnoreCase) ||
                                role.Contains("VHF/", StringComparison.OrdinalIgnoreCase))
                            {
                                band = role;
                                role = vals.Count >= 2 ? vals[1] : "Kanał";
                                freq = vals.Count >= 3 ? vals[2] : "-";
                            }

                            if (!string.IsNullOrWhiteSpace(role) || !string.IsNullOrWhiteSpace(freq))
                            {
                                snap.VoiceRows.Add(new V34FactionVoiceChannelRow
                                {
                                    Role = string.IsNullOrWhiteSpace(role) ? "Kanał" : role,
                                    Band = string.IsNullOrWhiteSpace(band) ? "PMR" : band,
                                    Freq = string.IsNullOrWhiteSpace(freq) ? "-" : freq,
                                    Tone = string.IsNullOrWhiteSpace(tone) ? "-" : tone
                                });
                            }
                        }
                    }

                    if (snap.VoiceRows.Count == 0)
                        snap.VoiceRows.Add(new V34FactionVoiceChannelRow());

                    list.Add(snap);
                }
            }
            catch { }

            if (list.Count > 0)
            {
                CachedFactions.Clear();
                CachedFactions.AddRange(list);
            }
            return CachedFactions;
        }

        public static List<DependencyObject> ScanAllWindowRoots(Window win)
        {
            var result = new List<DependencyObject>(256);
            var visited = new HashSet<DependencyObject>();

            void AddTree(DependencyObject r)
            {
                if (r == null || !visited.Add(r)) return;
                result.AddRange(V34CleanSidebarAndHeaderEngine.GetLogicalTreeNodes(r));
            }

            if (win != null)
            {
                AddTree(win);
                foreach (var f in win.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                {
                    try { if (f.GetValue(win) is DependencyObject d) AddTree(d); } catch { }
                }
            }

            foreach (var t in typeof(V34CleanFtakAndFactionEngine).Assembly.GetTypes())
            {
                if (t.Namespace == null || !t.Namespace.StartsWith("FieldTakHub.Builder")) continue;
                foreach (var sf in t.GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                {
                    try { if (typeof(DependencyObject).IsAssignableFrom(sf.FieldType) && sf.GetValue(null) is DependencyObject sd) AddTree(sd); } catch { }
                }
            }

            return result;
        }

        public static string ExtractButtonTextSafe(Button btn)
        {
            if (btn == null) return "";
            if (btn.Content is string s) return s;
            if (btn.Content is TextBlock tb) return tb.Text ?? "";
            if (btn.Content is Panel p)
                return string.Join(" ", V34CleanSidebarAndHeaderEngine.GetLogicalTreeNodes(p).OfType<TextBlock>().Select(x => x.Text ?? ""));
            return btn.Content?.ToString() ?? "";
        }
    }
    // [END_V34_CLEAN_FTAK_ENGINE]

    // [START_V34_CLEAN_MAP_STUDIO_ENGINE]
    /// <summary>
    /// Krok 4: Most geodezyjny dla Modułu 1 (Generator Map Taktycznych 1:1 — Hybryda Orto + Turbo LiDAR ISOK / Siatka MGRS).
    /// Weryfikuje, czy użytkownik faktycznie wyznaczył wielokąt AO lub punkty POI na mapie (blokując pobieranie METOC
    /// dla samego pustego arkusza mapy) oraz konwertuje współrzędne na wybrany w Studio Map układ (MGRS / UTM / DMS / WGS84).
    /// </summary>
    public static class V34CleanMapStudioBridge
    {
        private static readonly string[] MgrsCols = { "ABCDEFGH", "JKLMNPQR", "STUVWXYZ" };
        private static readonly string[] MgrsRows = { "ABCDEFGHJKLMNPQRSTUV", "FGHJKLMNPQRSTUVABCDE" };

        public static bool HasExplicitAoMarked { get; private set; }
        public static double AoCenterLat { get; private set; } = 49.849444;
        public static double AoCenterLon { get; private set; } = 18.812667;
        public static string ActiveCoordinateSystem { get; private set; } = "MGRS";

        public static void SyncMapStudioState(Window win)
        {
            try
            {
                var w = win ?? Application.Current?.MainWindow;
                if (w == null) return;

                V34CleanFtakAndFactionEngine.EnsureSharedFolderStructure();
                var all = V34CleanFtakAndFactionEngine.ScanAllWindowRoots(w);
                bool foundAoOrPois = false;

                // 1. Odczyt aktywnego układu współrzędnych wybranego w Studio Map 1:1
                foreach (var cb in all.OfType<ComboBox>())
                {
                    string sel = ((cb.SelectedItem is ComboBoxItem cbi ? cbi.Content?.ToString() : cb.SelectedItem?.ToString()) ?? cb.Text ?? "").Trim();
                    if (sel.Contains("MGRS", StringComparison.OrdinalIgnoreCase)) ActiveCoordinateSystem = "MGRS";
                    else if (sel.Contains("DMS", StringComparison.OrdinalIgnoreCase) || sel.Contains("Stopnie", StringComparison.OrdinalIgnoreCase)) ActiveCoordinateSystem = "DMS";
                    else if (sel.Contains("UTM", StringComparison.OrdinalIgnoreCase) && !sel.Contains("MGRS", StringComparison.OrdinalIgnoreCase)) ActiveCoordinateSystem = "UTM";
                    else if (sel.Contains("WGS84", StringComparison.OrdinalIgnoreCase) || sel.Contains("Dziesiętne", StringComparison.OrdinalIgnoreCase)) ActiveCoordinateSystem = "WGS84";
                }

                // 2. Sprawdzenie natywnego obiektu danych mapy (GetExtractedMapData / ExtractMapData)
                var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
                var mExtract = typeof(OpordBuilderManager).GetMethod("GetExtractedMapData", flags)
                            ?? typeof(OpordBuilderManager).GetMethod("ExtractMapData", flags);

                if (mExtract != null && mExtract.GetParameters().Length == 0)
                {
                    var mapData = mExtract.Invoke(null, null);
                    if (mapData != null)
                    {
                        var mt = mapData.GetType();
                        bool hasAoProp = (bool)(mt.GetProperty("HasAoPolygon")?.GetValue(mapData) ?? false);
                        double cLat = Convert.ToDouble(mt.GetProperty("CenterLat")?.GetValue(mapData) ?? 49.849444);
                        double cLon = Convert.ToDouble(mt.GetProperty("CenterLon")?.GetValue(mapData) ?? 18.812667);

                        int poisCount = 0;
                        if (mt.GetProperty("Pois")?.GetValue(mapData) is System.Collections.IEnumerable poisEnum)
                        {
                            foreach (var _ in poisEnum) poisCount++;
                        }

                        if (hasAoProp || poisCount > 0)
                        {
                            foundAoOrPois = true;
                            AoCenterLat = cLat;
                            AoCenterLon = cLon;
                        }
                    }
                }

                // 3. Sprawdzenie wielokąta AO bezpośrednio na płótnie Canvas w Studio Map 1:1
                if (!foundAoOrPois)
                {
                    foreach (var canvas in all.OfType<Canvas>())
                    {
                        foreach (UIElement ch in canvas.Children)
                        {
                            if (ch is System.Windows.Shapes.Polygon poly && poly.Points != null && poly.Points.Count >= 3 && poly.Visibility == Visibility.Visible)
                            {
                                foundAoOrPois = true;
                                break;
                            }
                            if (ch is System.Windows.Shapes.Polyline pline && pline.Points != null && pline.Points.Count >= 3 && pline.Visibility == Visibility.Visible)
                            {
                                foundAoOrPois = true;
                                break;
                            }
                        }
                        if (foundAoOrPois) break;
                    }
                }

                HasExplicitAoMarked = foundAoOrPois;
            }
            catch { }
        }

        public static string FormatByActiveMapSystem(double lat, double lon)
        {
            string sys = (ActiveCoordinateSystem ?? "MGRS").ToUpperInvariant();
            if (sys.Contains("MGRS")) return LatLonToMgrs(lat, lon);
            if (sys.Contains("UTM")) return LatLonToUtm(lat, lon);
            if (sys.Contains("DMS")) return LatLonToDms(lat, lon);
            return string.Format(CultureInfo.InvariantCulture, "{0:F5}°N, {1:F5}°E", lat, lon);
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
                char cChar = MgrsCols[(zone - 1) % 3][Math.Clamp((int)Math.Floor(east / 100000.0) - 1, 0, 7)];
                char rChar = MgrsRows[(zone - 1) % 2][Math.Clamp((int)Math.Floor(north / 100000.0) % 20, 0, 19)];
                int e5 = Math.Clamp((int)Math.Round(east) % 100000, 0, 99999);
                int n5 = Math.Clamp((int)Math.Round(north) % 100000, 0, 99999);
                return string.Format(CultureInfo.InvariantCulture, "{0:00}{1} {2}{3} {4:00000} {5:00000}", zone, bChar, cChar, rChar, e5, n5);
            }
            catch
            {
                return string.Format(CultureInfo.InvariantCulture, "{0:F5}, {1:F5}", lat, lon);
            }
        }

        public static string LatLonToUtm(double lat, double lon)
        {
            double cLat = Math.Clamp(lat, -80.0, 84.0);
            double nLon = ((lon + 180.0) % 360.0 + 360.0) % 360.0 - 180.0;
            int zone = (int)Math.Floor((nLon + 180.0) / 6.0) + 1;
            double latRad = cLat * Math.PI / 180.0, lonRad = nLon * Math.PI / 180.0;
            double lonOrig = ((zone - 1) * 6 - 180 + 3) * Math.PI / 180.0;
            double a = 6378137.0, eccSq = 0.00669438, k0 = 0.9996;
            double n = a / Math.Sqrt(1.0 - eccSq * Math.Sin(latRad) * Math.Sin(latRad));
            double A = Math.Cos(latRad) * (lonRad - lonOrig);
            double M = a * ((1.0 - eccSq / 4.0) * latRad - (3.0 * eccSq / 8.0) * Math.Sin(2.0 * latRad));
            double east = k0 * n * A + 500000.0;
            double north = k0 * (M + n * Math.Tan(latRad) * (A * A / 2.0));
            return string.Format(CultureInfo.InvariantCulture, "UTM {0}N {1:0}E {2:0}N", zone, east, north);
        }

        public static string LatLonToDms(double lat, double lon)
        {
            return ToDmsPart(lat, lat >= 0 ? 'N' : 'S') + " " + ToDmsPart(lon, lon >= 0 ? 'E' : 'W');
        }

        private static string ToDmsPart(double val, char hem)
        {
            double abs = Math.Abs(val);
            int d = (int)Math.Floor(abs);
            double mf = (abs - d) * 60.0;
            int m = (int)Math.Floor(mf);
            double s = (mf - m) * 60.0;
            return string.Format(CultureInfo.InvariantCulture, "{0:00}°{1:00}'{2:00.0}\"{3}", d, m, s, hem);
        }
    }
    // [END_V34_CLEAN_MAP_STUDIO_ENGINE]

// [START_V34_CLEAN_USB_BRIDGE]
    /// <summary>
    /// Krok 8: Czysta synchronizacja Modułu 4 (Masowy Programator Płytek USB LoRa Meshtastic).
    /// Synchronizuje selektor stron konfliktu z kartami Generatora Stron (odrzucając śmieciowe wpisy PLI / GeoChat)
    /// oraz dba o prawidłowy wygląd przycisków portów COM dla płytek Heltec V3 / T-Beam.
    /// </summary>
    public static class V34CleanUsbProgrammerBridge
    {
        public static void SyncUsbProgrammerUi(Window win)
        {
            try
            {
                var w = win ?? Application.Current?.MainWindow;
                if (w == null) return;

                var all = V34CleanFtakAndFactionEngine.ScanAllWindowRoots(w);
                var cleanFactionNames = V34CleanOpordDataEngine.GetCleanFactionNames(w);

                foreach (var tb in all.OfType<TextBlock>())
                {
                    string txt = (tb.Text ?? "").Trim();
                    if (txt.Contains("PROGRAMATOR PŁYTEK USB", StringComparison.OrdinalIgnoreCase) ||
                        txt.Contains("MASOWY PROGRAMATOR", StringComparison.OrdinalIgnoreCase) ||
                        txt.Contains("USB BOARD PROGRAMMER", StringComparison.OrdinalIgnoreCase))
                    {
                        DependencyObject root = LogicalTreeHelper.GetParent(tb) ?? VisualTreeHelper.GetParent(tb);
                        while (root != null && !(root is StackPanel || root is Grid))
                        {
                            root = LogicalTreeHelper.GetParent(root) ?? VisualTreeHelper.GetParent(root);
                        }

                        if (root != null)
                        {
                            var usbElems = V34CleanSidebarAndHeaderEngine.GetLogicalTreeNodes(root);
                            foreach (var cb in usbElems.OfType<ComboBox>())
                            {
                                for (int i = cb.Items.Count - 1; i >= 0; i--)
                                {
                                    string itemStr = cb.Items[i]?.ToString() ?? "";
                                    if (V34CleanOpordDataEngine.IsGarbageFactionName(itemStr))
                                    {
                                        cb.Items.RemoveAt(i);
                                    }
                                }

                                foreach (string fn in cleanFactionNames)
                                {
                                    bool exists = false;
                                    foreach (var existing in cb.Items)
                                    {
                                        if (string.Equals(existing?.ToString(), fn, StringComparison.OrdinalIgnoreCase))
                                        {
                                            exists = true;
                                            break;
                                        }
                                    }
                                    if (!exists) cb.Items.Add(fn);
                                }

                                if (cb.SelectedItem == null && cb.Items.Count > 0)
                                {
                                    cb.SelectedIndex = 0;
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }
    }
    // [END_V34_CLEAN_USB_BRIDGE]
}
