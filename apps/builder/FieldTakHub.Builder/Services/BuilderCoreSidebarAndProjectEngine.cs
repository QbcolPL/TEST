#pragma warning disable CS0414, CS0649, CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625
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
}
