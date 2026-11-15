#pragma warning disable CS0414, CS0649, CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625, CS8629
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FieldTakHub.Builder.Services
{
    public static partial class TacticalMapGeneratorView
    {
        public sealed class TacticalRangeSectorVisuals
        {
            public Path SectorPath;
            public Path BaseArcPath;
            public Path EffArcHighlight;
            public readonly List<Path> RingPaths = new List<Path>();
            public Line AimLine;
            public Border OriginMarkerPin;
            public Border LabelBadge;
            public TextBlock LabelTextBlock;
            public Border RotateHandle;
            public Border LeftWingHandle;
            public Border RightWingHandle;
        }

        public sealed class TacticalRangeSectorItem
        {
            public string Id { get; set; } = Guid.NewGuid().ToString("N");
            public string Name { get; set; } = "KM-1 (Wsparcie)";
            public string WeaponPreset { get; set; } = "KM / Wsparcie (70 m)";
            public double BaseRadiusMeters { get; set; } = 70.0;
            public double AzimuthDeg { get; set; } = 45.0;       // 0 = Polnoc (N), 90 = Wschod (E)
            public double SpreadAngleDeg { get; set; } = 60.0;   // 360 = Pelny okrag, <360 = Wycinek "Pizza"
            public double FillOpacity { get; set; } = 0.24;
            public string ColorHex { get; set; } = "#1FE0C5";
            public double ElevationDeltaMeters { get; set; } = 0.0; // dH = H_strzelca - H_celu (m)
            public double ShooterAltMasl { get; set; } = 0.0;
            public double TargetAltMasl { get; set; } = 0.0;
            public bool IsVisible { get; set; } = true;

            public double OffsetEastMeters { get; set; } = 0.0;
            public double OffsetNorthMeters { get; set; } = 0.0;
            public bool HasGeoAnchor { get; set; } = false;
            public double ShooterLat { get; set; } = 0.0;
            public double ShooterLon { get; set; } = 0.0;

            public TacticalRangeSectorVisuals Visuals { get; set; }

            public double GetEffectiveRadiusMeters()
            {
                double bonusOrPenalty;
                if (ElevationDeltaMeters >= 0)
                {
                    bonusOrPenalty = Math.Min(BaseRadiusMeters * 0.40, ElevationDeltaMeters * 0.85);
                }
                else
                {
                    bonusOrPenalty = Math.Max(-BaseRadiusMeters * 0.45, ElevationDeltaMeters * 1.10);
                }
                return Math.Clamp(BaseRadiusMeters + bonusOrPenalty, 5.0, 500.0);
            }

            public double GetRangeBonusMeters()
            {
                return GetEffectiveRadiusMeters() - BaseRadiusMeters;
            }

            public double GetRangeBonusPercent()
            {
                if (BaseRadiusMeters <= 0.1) return 0.0;
                return (GetRangeBonusMeters() / BaseRadiusMeters) * 100.0;
            }
        }

        private static readonly List<TacticalRangeSectorItem> _weaponRangeSectors = new List<TacticalRangeSectorItem>();
        private static TacticalRangeSectorItem _selectedRangeSector = null;
        private static bool _isRenderingRangeSectors = false;
        private static bool _isInteractingWithSectorOnMap = false;
        private static bool _suppressRangeSectorUiEvents = false;
        private static readonly HttpClient _demHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

        private static StackPanel _rangeSectorsListContainer;
        private static Border _rangeSectorEditorBox;
        private static TextBox _rsNameBox;
        private static ComboBox _rsWeaponPresetCombo;
        private static TextBox _rsBaseRadiusBox;
        private static Slider _rsAzimuthSlider;
        private static TextBlock _rsAzimuthValText;
        private static Slider _rsSpreadSlider;
        private static TextBlock _rsSpreadValText;
        private static Slider _rsDeltaHSlider;
        private static TextBox _rsDeltaHBox;
        private static TextBlock _rsBallisticSummaryText;
        private static Slider _rsOpacitySlider;

        private static void AttachRangeSectorQuickButton(Grid legContainer)
        {
            if (legContainer == null) return;
            var btnAddSector = new Button
            {
                Content = "🎯 + Sektor Ognia",
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 7, 162, 0),
                Height = 18,
                Padding = new Thickness(6, 0, 6, 0),
                Background = new SolidColorBrush(Color.FromRgb(7, 17, 21)),
                Foreground = new SolidColorBrush(Color.FromRgb(46, 204, 113)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(46, 204, 113)),
                BorderThickness = new Thickness(1),
                FontSize = 8.0,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                ToolTip = "Dodaj na środek mapy interaktywny znacznik zasięgu repliki / sektora ognia (przesuwany i obracany myszką)"
            };
            btnAddSector.Click += (s, e) => AddNewWeaponRangeSector();
            legContainer.Children.Add(btnAddSector);
            UpdateRangeSectorsInLegendBox(legContainer);
        }

        private static void AttachWeaponRangeSectorsPanelToParent(Panel parentPanel)
        {
            if (parentPanel == null) return;
            var panel = BuildWeaponRangeSectorsPanel();
            parentPanel.Children.Add(panel);
        }

        private static void AddNewWeaponRangeSector()
        {
            string[] defaultColors = new[] { "#1FE0C5", "#2ECC71", "#FAD02C", "#EB4034", "#A55EEA", "#FF7F50" };
            int idx = _weaponRangeSectors.Count + 1;
            string col = defaultColors[(idx - 1) % defaultColors.Length];

            var item = new TacticalRangeSectorItem
            {
                Name = $"Stanowisko #{idx}",
                WeaponPreset = "Karabinek Szturmowy (60 m)",
                BaseRadiusMeters = 60.0,
                AzimuthDeg = 45.0,
                SpreadAngleDeg = 75.0,
                FillOpacity = 0.24,
                ColorHex = col,
                ElevationDeltaMeters = 0.0,
                OffsetEastMeters = ((idx - 1) % 3 - 1) * 18.0,
                OffsetNorthMeters = ((idx - 1) % 2 == 0 ? 12.0 : -12.0)
            };

            SyncSectorGeoAnchorFromOffsets(item);
            _weaponRangeSectors.Add(item);
            _selectedRangeSector = item;
            RebuildRangeSectorsListUi(); if (_lastLegendContainerGrid != null) UpdateRangeSectorsInLegendBox(_lastLegendContainerGrid);
            PopulateRangeSectorEditorFromSelected();
            RefreshRangeSectorsNow(forceFullRebuild: true);
        }

        private static void RefreshRangeSectorsNow(bool forceFullRebuild = false)
        {
            var (cw, ch) = GetExactCanvasSize();
            if (cw > 20 && ch > 20)
            {
                if (forceFullRebuild)
                {
                    foreach (var s in _weaponRangeSectors) s.Visuals = null;
                }
                RenderAllWeaponRangeSectorsOnSheet(cw, ch);
            }
            UpdateBallisticSummaryBanner();
            if (_lastLegendContainerGrid != null) UpdateRangeSectorsInLegendBox(_lastLegendContainerGrid);
            try { TacticalConfigPanel.MarkDirty(); } catch { }
        }

        private static Border BuildWeaponRangeSectorsPanel()
        {
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(8, 20, 25)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(22, 62, 74)),
                BorderThickness = new Thickness(1.1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8, 10, 10),
                Margin = new Thickness(0, 6, 0, 8)
            };

            var sp = new StackPanel();

            var hdrGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            hdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleTb = new TextBlock
            {
                Text = "🎯 ZASIĘG REPLIK & SEKTORY OGNIA (PIZZA + ΔH)",
                Foreground = TacticalConfigPanel.BrushAccent,
                FontWeight = FontWeights.Bold,
                FontSize = 10.5,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(titleTb, 0);
            hdrGrid.Children.Add(titleTb);

            var btnAdd = new Button
            {
                Content = "+ Dodaj Sektor / Okrąg",
                Height = 23,
                Padding = new Thickness(8, 0, 8, 0),
                Background = TacticalConfigPanel.BrushAccent,
                Foreground = new SolidColorBrush(Color.FromRgb(6, 14, 18)),
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.Bold,
                FontSize = 9.5,
                Cursor = Cursors.Hand
            };
            btnAdd.Click += (s, e) => AddNewWeaponRangeSector();
            Grid.SetColumn(btnAdd, 1);
            hdrGrid.Children.Add(btnAdd);
            sp.Children.Add(hdrGrid);
            var kmzActionGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 6) };
            var btnImportKmz = new Button
            {
                Content = "📂 Wczytaj z .KMZ / .KML",
                Height = 24,
                Margin = new Thickness(0, 0, 3, 0),
                Background = new SolidColorBrush(Color.FromRgb(7, 17, 21)),
                Foreground = TacticalConfigPanel.BrushAccent,
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.Bold,
                FontSize = 9.2,
                Cursor = Cursors.Hand,
                ToolTip = "Wczytaj wyrysowane wcześniej sektory ognia z pliku .KMZ lub .KML (obsługiwany jest także główny przycisk importu KMZ Generatora Map)"
            };
            btnImportKmz.Click += (s, e) => ImportStandaloneRangeSectorsKmzDialog();
            kmzActionGrid.Children.Add(btnImportKmz);

            var btnExportKmz = new Button
            {
                Content = "📦 Eksportuj .KMZ (ATAK)",
                Height = 24,
                Margin = new Thickness(3, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(7, 17, 21)),
                Foreground = new SolidColorBrush(Color.FromRgb(46, 204, 113)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(46, 204, 113)),
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.Bold,
                FontSize = 9.2,
                Cursor = Cursors.Hand,
                ToolTip = "Zapisz wyrysowane sektory ognia jako wielokąty KML/KMZ dla ATAK"
            };
            btnExportKmz.Click += (s, e) => ExportStandaloneRangeSectorsKmzDialog();
            kmzActionGrid.Children.Add(btnExportKmz);
            sp.Children.Add(kmzActionGrid);

            sp.Children.Add(new TextBlock
            {
                Text = "• Lewy przycisk myszy (na znaczniku 🎯 lub wycinku) = przesuwanie po mapie\n• Uchwyt ↻ (lub Prawy przycisk myszy / Scroll) = obracanie kierunku na mapie\n• Boczne uchwyty ◆ na krawędziach = zwężanie/rozszerzanie 'pizzy' myszką",
                Foreground = TacticalConfigPanel.BrushMutedText,
                FontSize = 8.8,
                LineHeight = 13.0,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            });

            _rangeSectorsListContainer = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
            sp.Children.Add(_rangeSectorsListContainer);

            _rangeSectorEditorBox = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(6, 15, 19)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(20, 52, 62)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(8),
                Visibility = Visibility.Collapsed
            };

            var edStack = new StackPanel();

            edStack.Children.Add(CreateFieldLabel("Nazwa stanowiska / repliki oraz kolor sektora:"));
            var nameColorGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            nameColorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            nameColorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            nameColorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _rsNameBox = CreateDarkTextBox("KM-1 (Wsparcie)");
            _rsNameBox.TextChanged += (s, e) =>
            {
                if (_suppressRangeSectorUiEvents || _selectedRangeSector == null) return;
                _selectedRangeSector.Name = _rsNameBox.Text;
                RebuildRangeSectorsListUi(); if (_lastLegendContainerGrid != null) UpdateRangeSectorsInLegendBox(_lastLegendContainerGrid);
                RefreshRangeSectorsNow();
            };
            Grid.SetColumn(_rsNameBox, 0);
            nameColorGrid.Children.Add(_rsNameBox);

            var colorPalette = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            string[] swatchHexes = new[] { "#1FE0C5", "#2ECC71", "#FAD02C", "#EB4034", "#A55EEA", "#FF7F50" };
            foreach (string hex in swatchHexes)
            {
                string h = hex;
                var swBtn = new Button
                {
                    Width = 18,
                    Height = 20,
                    Margin = new Thickness(1, 0, 1, 0),
                    Background = TacticalConfigPanel.BrushFromHex(h, Colors.Cyan),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    ToolTip = "Kolor sektora: " + h
                };
                swBtn.Click += (s, e) =>
                {
                    if (_selectedRangeSector == null) return;
                    _selectedRangeSector.ColorHex = h;
                    RebuildRangeSectorsListUi(); if (_lastLegendContainerGrid != null) UpdateRangeSectorsInLegendBox(_lastLegendContainerGrid);
                    RefreshRangeSectorsNow();
                };
                colorPalette.Children.Add(swBtn);
            }
            Grid.SetColumn(colorPalette, 2);
            nameColorGrid.Children.Add(colorPalette);
            edStack.Children.Add(nameColorGrid);

            var radiusGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            radiusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            radiusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            radiusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var spPreset = new StackPanel();
            spPreset.Children.Add(CreateFieldLabel("Klasa repliki / broni (Preset):"));
            string[] weaponPresets = new[]
            {
                "Pistolet / CQB (40 m)",
                "Karabinek Szturmowy (60 m)",
                "KM / Wsparcie (70 m)",
                "DMR / Wyborowa (80 m)",
                "Snajper Bolt-Action (95 m)",
                "Granatnik / Moździerz (120 m)",
                "Własny zasięg (Custom)"
            };
            _rsWeaponPresetCombo = CreateDarkTacticalComboBox(weaponPresets, weaponPresets[1], val =>
            {
                if (_suppressRangeSectorUiEvents || _selectedRangeSector == null) return;
                _selectedRangeSector.WeaponPreset = val;
                double? presetMeters = null;
                if (val.Contains("(40 m)")) presetMeters = 40;
                else if (val.Contains("(60 m)")) presetMeters = 60;
                else if (val.Contains("(70 m)")) presetMeters = 70;
                else if (val.Contains("(80 m)")) presetMeters = 80;
                else if (val.Contains("(95 m)")) presetMeters = 95;
                else if (val.Contains("(120 m)")) presetMeters = 120;

                if (presetMeters.HasValue)
                {
                    _selectedRangeSector.BaseRadiusMeters = presetMeters.Value;
                    _suppressRangeSectorUiEvents = true;
                    if (_rsBaseRadiusBox != null)
                        _rsBaseRadiusBox.Text = presetMeters.Value.ToString("0", CultureInfo.InvariantCulture);
                    _suppressRangeSectorUiEvents = false;
                }
                RebuildRangeSectorsListUi(); if (_lastLegendContainerGrid != null) UpdateRangeSectorsInLegendBox(_lastLegendContainerGrid);
                RefreshRangeSectorsNow();
            });
            spPreset.Children.Add(_rsWeaponPresetCombo);
            Grid.SetColumn(spPreset, 0);
            radiusGrid.Children.Add(spPreset);

            var spRad = new StackPanel();
            spRad.Children.Add(CreateFieldLabel("Promień płaski R (m):"));
            _rsBaseRadiusBox = CreateDarkTextBox("60", isAccentText: true);
            _rsBaseRadiusBox.TextChanged += (s, e) =>
            {
                if (_suppressRangeSectorUiEvents || _selectedRangeSector == null) return;
                if (double.TryParse(_rsBaseRadiusBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double rVal) && rVal >= 5 && rVal <= 500)
                {
                    _selectedRangeSector.BaseRadiusMeters = rVal;
                    RebuildRangeSectorsListUi(); if (_lastLegendContainerGrid != null) UpdateRangeSectorsInLegendBox(_lastLegendContainerGrid);
                    RefreshRangeSectorsNow();
                }
            };
            spRad.Children.Add(_rsBaseRadiusBox);
            Grid.SetColumn(spRad, 2);
            radiusGrid.Children.Add(spRad);
            edStack.Children.Add(radiusGrid);

            var azHeader = new Grid();
            azHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            azHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            azHeader.Children.Add(CreateFieldLabel("Azymut osi ostrzału (0°=N, 90°=E, 180°=S, 270°=W):"));
            _rsAzimuthValText = new TextBlock { Text = "45° (NE)", Foreground = TacticalConfigPanel.BrushAccent, FontSize = 9.5, FontWeight = FontWeights.Bold };
            Grid.SetColumn(_rsAzimuthValText, 1);
            azHeader.Children.Add(_rsAzimuthValText);
            edStack.Children.Add(azHeader);

            _rsAzimuthSlider = new Slider { Minimum = 0, Maximum = 360, Value = 45, Margin = new Thickness(0, 0, 0, 5) };
            _rsAzimuthSlider.ValueChanged += (s, e) =>
            {
                if (_suppressRangeSectorUiEvents || _selectedRangeSector == null) return;
                _selectedRangeSector.AzimuthDeg = Math.Round(_rsAzimuthSlider.Value);
                _rsAzimuthValText.Text = FormatAzimuthWithCardinal(_selectedRangeSector.AzimuthDeg);
                RefreshRangeSectorsNow();
            };
            edStack.Children.Add(_rsAzimuthSlider);

            var spHeader = new Grid();
            spHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            spHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            spHeader.Children.Add(CreateFieldLabel("Kąt wycinka 'Kawałek Pizzy' (5°–360° pełny okrąg):"));
            _rsSpreadValText = new TextBlock { Text = "75° (Wycinek)", Foreground = TacticalConfigPanel.BrushAccent, FontSize = 9.5, FontWeight = FontWeights.Bold };
            Grid.SetColumn(_rsSpreadValText, 1);
            spHeader.Children.Add(_rsSpreadValText);
            edStack.Children.Add(spHeader);

            _rsSpreadSlider = new Slider { Minimum = 5, Maximum = 360, Value = 75, Margin = new Thickness(0, 0, 0, 4) };
            _rsSpreadSlider.ValueChanged += (s, e) =>
            {
                if (_suppressRangeSectorUiEvents || _selectedRangeSector == null) return;
                _selectedRangeSector.SpreadAngleDeg = Math.Round(_rsSpreadSlider.Value);
                _rsSpreadValText.Text = _selectedRangeSector.SpreadAngleDeg >= 359
                    ? "360° (Pełny okrąg)"
                    : $"{_selectedRangeSector.SpreadAngleDeg:0}° (Wycinek pizzy)";
                RefreshRangeSectorsNow();
            };
            edStack.Children.Add(_rsSpreadSlider);

            var anglePresetsRow = new UniformGrid { Columns = 5, Margin = new Thickness(0, 0, 0, 6) };
            foreach (int ang in new[] { 30, 60, 90, 180, 360 })
            {
                int a = ang;
                anglePresetsRow.Children.Add(CreateMiniNudgeButton(a == 360 ? "360° Okrąg" : $"{a}° Pizza", () =>
                {
                    if (_selectedRangeSector == null) return;
                    _selectedRangeSector.SpreadAngleDeg = a;
                    _rsSpreadSlider.Value = a;
                    RefreshRangeSectorsNow();
                }));
            }
            edStack.Children.Add(anglePresetsRow);

            edStack.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(22, 56, 68)), Margin = new Thickness(0, 2, 0, 5) });

            var dhHeader = new Grid { Margin = new Thickness(0, 0, 0, 3) };
            dhHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dhHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            dhHeader.Children.Add(new TextBlock
            {
                Text = "⛰️ Przewaga wysokości ΔH (H strzelca − H celu):",
                Foreground = TacticalConfigPanel.BrushAccent,
                FontSize = 9.5,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });

            var btnAutoDem = new Button
            {
                Content = "📡 Auto-DEM z mapy",
                Height = 20,
                Padding = new Thickness(6, 0, 6, 0),
                Background = new SolidColorBrush(Color.FromRgb(7, 17, 21)),
                Foreground = new SolidColorBrush(Color.FromRgb(46, 204, 113)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(46, 204, 113)),
                BorderThickness = new Thickness(1),
                FontSize = 8.8,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                ToolTip = "Pobierz rzeczywistą wysokość n.p.m. stanowiska strzelca i rubieży celu z modelu wysokościowego terenu (DEM)"
            };
            btnAutoDem.Click += async (s, e) => await FetchAutoDemElevationForSelectedSectorAsync(btnAutoDem);
            Grid.SetColumn(btnAutoDem, 1);
            dhHeader.Children.Add(btnAutoDem);
            edStack.Children.Add(dhHeader);

            var dhControlGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            dhControlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dhControlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            dhControlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });

            _rsDeltaHSlider = new Slider { Minimum = -40, Maximum = 40, Value = 0, VerticalAlignment = VerticalAlignment.Center };
            _rsDeltaHSlider.ValueChanged += (s, e) =>
            {
                if (_suppressRangeSectorUiEvents || _selectedRangeSector == null) return;
                _selectedRangeSector.ElevationDeltaMeters = Math.Round(_rsDeltaHSlider.Value, 1);
                _suppressRangeSectorUiEvents = true;
                if (_rsDeltaHBox != null)
                    _rsDeltaHBox.Text = _selectedRangeSector.ElevationDeltaMeters.ToString("+0.#;-0.#;0", CultureInfo.InvariantCulture);
                _suppressRangeSectorUiEvents = false;
                RebuildRangeSectorsListUi(); if (_lastLegendContainerGrid != null) UpdateRangeSectorsInLegendBox(_lastLegendContainerGrid);
                RefreshRangeSectorsNow();
            };
            Grid.SetColumn(_rsDeltaHSlider, 0);
            dhControlGrid.Children.Add(_rsDeltaHSlider);

            _rsDeltaHBox = CreateDarkTextBox("0", isAccentText: true);
            _rsDeltaHBox.TextChanged += (s, e) =>
            {
                if (_suppressRangeSectorUiEvents || _selectedRangeSector == null) return;
                if (double.TryParse(_rsDeltaHBox.Text.Replace("+", "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double dhVal))
                {
                    _selectedRangeSector.ElevationDeltaMeters = Math.Clamp(dhVal, -150.0, 150.0);
                    _suppressRangeSectorUiEvents = true;
                    _rsDeltaHSlider.Value = Math.Clamp(_selectedRangeSector.ElevationDeltaMeters, -40.0, 40.0);
                    _suppressRangeSectorUiEvents = false;
                    RebuildRangeSectorsListUi(); if (_lastLegendContainerGrid != null) UpdateRangeSectorsInLegendBox(_lastLegendContainerGrid);
                    RefreshRangeSectorsNow();
                }
            };
            Grid.SetColumn(_rsDeltaHBox, 2);
            dhControlGrid.Children.Add(_rsDeltaHBox);
            edStack.Children.Add(dhControlGrid);

            _rsBallisticSummaryText = new TextBlock
            {
                Text = "Zasięg efektywny: 60.0 m (Brak korekty wysokości ΔH = 0 m)",
                Foreground = new SolidColorBrush(Color.FromRgb(46, 204, 113)),
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 5)
            };
            edStack.Children.Add(_rsBallisticSummaryText);

            edStack.Children.Add(CreateFieldLabel("Przezroczystość wypełnienia sektora:"));
            _rsOpacitySlider = new Slider { Minimum = 0.05, Maximum = 0.65, Value = 0.24, Margin = new Thickness(0, 0, 0, 2) };
            _rsOpacitySlider.ValueChanged += (s, e) =>
            {
                if (_suppressRangeSectorUiEvents || _selectedRangeSector == null) return;
                _selectedRangeSector.FillOpacity = _rsOpacitySlider.Value;
                RefreshRangeSectorsNow();
            };
            edStack.Children.Add(_rsOpacitySlider);

            _rangeSectorEditorBox.Child = edStack;
            sp.Children.Add(_rangeSectorEditorBox);

            card.Child = sp;
            return card;
        }

        private static async Task FetchAutoDemElevationForSelectedSectorAsync(Button btn)
        {
            if (_selectedRangeSector == null) return;
            var sec = _selectedRangeSector;
            string origText = btn.Content?.ToString() ?? "📡 Auto-DEM z mapy";
            btn.Content = "⏳ Pobieranie NMT...";
            btn.IsEnabled = false;

            try
            {
                double centerLat = _centerLat;
                double centerLon = _centerLon;
                double latRad = centerLat * Math.PI / 180.0;
                double mPerDegLat = 111132.92;
                double mPerDegLon = Math.Max(1000.0, 111412.84 * Math.Cos(latRad));

                double shooterLat = centerLat + (sec.OffsetNorthMeters / mPerDegLat);
                double shooterLon = centerLon + (sec.OffsetEastMeters / mPerDegLon);

                double azRad = sec.AzimuthDeg * Math.PI / 180.0;
                double targetEastM = sec.OffsetEastMeters + sec.BaseRadiusMeters * Math.Sin(azRad);
                double targetNorthM = sec.OffsetNorthMeters + sec.BaseRadiusMeters * Math.Cos(azRad);
                double targetLat = centerLat + (targetNorthM / mPerDegLat);
                double targetLon = centerLon + (targetEastM / mPerDegLon);

                string url = string.Format(
                    CultureInfo.InvariantCulture,
                    "https://api.open-meteo.com/v1/elevation?latitude={0:F6},{1:F6}&longitude={2:F6},{3:F6}",
                    shooterLat, targetLat, shooterLon, targetLon);

                string json = await _demHttpClient.GetStringAsync(url);
                Match m = Regex.Match(json, "\"elevation\"\\s*:\\s*\\[\\s*([-\\d.]+)\\s*,\\s*([-\\d.]+)\\s*\\]");
                if (m.Success &&
                    double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double hShooter) &&
                    double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double hTarget))
                {
                    sec.ShooterAltMasl = hShooter;
                    sec.TargetAltMasl = hTarget;
                    sec.ElevationDeltaMeters = Math.Round(hShooter - hTarget, 1);

                    _suppressRangeSectorUiEvents = true;
                    if (_rsDeltaHSlider != null) _rsDeltaHSlider.Value = Math.Clamp(sec.ElevationDeltaMeters, -40.0, 40.0);
                    if (_rsDeltaHBox != null) _rsDeltaHBox.Text = sec.ElevationDeltaMeters.ToString("+0.#;-0.#;0", CultureInfo.InvariantCulture);
                    _suppressRangeSectorUiEvents = false;

                    RebuildRangeSectorsListUi(); if (_lastLegendContainerGrid != null) UpdateRangeSectorsInLegendBox(_lastLegendContainerGrid);
                    RefreshRangeSectorsNow();
                }
            }
            catch { }
            finally
            {
                btn.Content = origText;
                btn.IsEnabled = true;
            }
        }

        private static void RebuildRangeSectorsListUi()
        {
            if (_rangeSectorsListContainer == null) return;
            _rangeSectorsListContainer.Children.Clear();

            if (_weaponRangeSectors.Count == 0)
            {
                if (_rangeSectorEditorBox != null)
                    _rangeSectorEditorBox.Visibility = Visibility.Collapsed;
                return;
            }

            if (_selectedRangeSector == null || !_weaponRangeSectors.Contains(_selectedRangeSector))
                _selectedRangeSector = _weaponRangeSectors[0];

            if (_rangeSectorEditorBox != null)
                _rangeSectorEditorBox.Visibility = Visibility.Visible;

            foreach (var sec in _weaponRangeSectors.ToList())
            {
                bool isSel = ReferenceEquals(sec, _selectedRangeSector);
                var rowBorder = new Border
                {
                    Background = isSel ? new SolidColorBrush(Color.FromRgb(14, 38, 46)) : new SolidColorBrush(Color.FromRgb(7, 17, 21)),
                    BorderBrush = isSel ? TacticalConfigPanel.BrushAccent : new SolidColorBrush(Color.FromRgb(22, 56, 68)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 4, 6, 4),
                    Margin = new Thickness(0, 0, 0, 4),
                    Cursor = Cursors.Hand
                };

                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var chk = new CheckBox
                {
                    IsChecked = sec.IsVisible,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0)
                };
                chk.Checked += (s, e) => { sec.IsVisible = true; RefreshRangeSectorsNow(forceFullRebuild: true); };
                chk.Unchecked += (s, e) => { sec.IsVisible = false; RefreshRangeSectorsNow(forceFullRebuild: true); };
                Grid.SetColumn(chk, 0);
                g.Children.Add(chk);

                var swatch = new Border
                {
                    Width = 12,
                    Height = 12,
                    CornerRadius = new CornerRadius(2),
                    Background = TacticalConfigPanel.BrushFromHex(sec.ColorHex, Colors.Cyan),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0)
                };
                Grid.SetColumn(swatch, 1);
                g.Children.Add(swatch);

                double effR = sec.GetEffectiveRadiusMeters();
                string dhTag = Math.Abs(sec.ElevationDeltaMeters) > 0.1
                    ? $" | ΔH:{sec.ElevationDeltaMeters:+0.#;-0.#}m"
                    : "";
                var infoTb = new TextBlock
                {
                    Text = $"{sec.Name}  [{sec.BaseRadiusMeters:0}m ➔ {effR:0.0}m{dhTag}]",
                    Foreground = Brushes.White,
                    FontSize = 10,
                    FontWeight = isSel ? FontWeights.Bold : FontWeights.Normal,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(infoTb, 2);
                g.Children.Add(infoTb);

                var btnDel = new Button
                {
                    Content = "−",
                    Width = 20,
                    Height = 18,
                    Padding = new Thickness(0),
                    Background = new SolidColorBrush(Color.FromRgb(185, 45, 45)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    FontWeight = FontWeights.Bold,
                    FontSize = 11,
                    Cursor = Cursors.Hand
                };
                btnDel.Click += (s, e) =>
                {
                    e.Handled = true;
                    _weaponRangeSectors.Remove(sec);
                    if (ReferenceEquals(_selectedRangeSector, sec))
                        _selectedRangeSector = _weaponRangeSectors.FirstOrDefault();
                    RebuildRangeSectorsListUi(); if (_lastLegendContainerGrid != null) UpdateRangeSectorsInLegendBox(_lastLegendContainerGrid);
                    PopulateRangeSectorEditorFromSelected();
                    RefreshRangeSectorsNow(forceFullRebuild: true);
                };
                Grid.SetColumn(btnDel, 3);
                g.Children.Add(btnDel);

                rowBorder.MouseLeftButtonDown += (s, e) =>
                {
                    _selectedRangeSector = sec;
                    RebuildRangeSectorsListUi(); if (_lastLegendContainerGrid != null) UpdateRangeSectorsInLegendBox(_lastLegendContainerGrid);
                    PopulateRangeSectorEditorFromSelected();
                    RefreshRangeSectorsNow();
                };

                rowBorder.Child = g;
                _rangeSectorsListContainer.Children.Add(rowBorder);
            }
        }

        private static void PopulateRangeSectorEditorFromSelected()
        {
            if (_selectedRangeSector == null) return;
            _suppressRangeSectorUiEvents = true;
            try
            {
                var s = _selectedRangeSector;
                if (_rsNameBox != null) _rsNameBox.Text = s.Name;
                if (_rsWeaponPresetCombo != null) _rsWeaponPresetCombo.SelectedItem = s.WeaponPreset;
                if (_rsBaseRadiusBox != null) _rsBaseRadiusBox.Text = s.BaseRadiusMeters.ToString("0.#", CultureInfo.InvariantCulture);
                if (_rsAzimuthSlider != null) _rsAzimuthSlider.Value = s.AzimuthDeg;
                if (_rsAzimuthValText != null) _rsAzimuthValText.Text = FormatAzimuthWithCardinal(s.AzimuthDeg);
                if (_rsSpreadSlider != null) _rsSpreadSlider.Value = s.SpreadAngleDeg;
                if (_rsSpreadValText != null)
                    _rsSpreadValText.Text = s.SpreadAngleDeg >= 359 ? "360° (Pełny okrąg)" : $"{s.SpreadAngleDeg:0}° (Wycinek pizzy)";
                if (_rsDeltaHSlider != null) _rsDeltaHSlider.Value = Math.Clamp(s.ElevationDeltaMeters, -40.0, 40.0);
                if (_rsDeltaHBox != null) _rsDeltaHBox.Text = s.ElevationDeltaMeters.ToString("+0.#;-0.#;0", CultureInfo.InvariantCulture);
                if (_rsOpacitySlider != null) _rsOpacitySlider.Value = s.FillOpacity;
                UpdateBallisticSummaryBanner();
            if (_lastLegendContainerGrid != null) UpdateRangeSectorsInLegendBox(_lastLegendContainerGrid);
            }
            finally
            {
                _suppressRangeSectorUiEvents = false;
            }
        }

        private static void UpdateBallisticSummaryBanner()
        {
            if (_rsBallisticSummaryText == null || _selectedRangeSector == null) return;
            var s = _selectedRangeSector;
            double effR = s.GetEffectiveRadiusMeters();
            double diffM = s.GetRangeBonusMeters();
            double diffPct = s.GetRangeBonusPercent();

            string altInfo = (s.ShooterAltMasl > 0.1 || s.TargetAltMasl > 0.1)
                ? $" [NMT: {s.ShooterAltMasl:0}m ➔ {s.TargetAltMasl:0}m]"
                : "";

            if (Math.Abs(s.ElevationDeltaMeters) < 0.1)
            {
                _rsBallisticSummaryText.Text = $"Zasięg efektywny: {effR:0.0} m (Teren płaski ΔH = 0 m){altInfo}";
                _rsBallisticSummaryText.Foreground = TacticalConfigPanel.BrushAccent;
            }
            else if (s.ElevationDeltaMeters > 0)
            {
                _rsBallisticSummaryText.Text = $"Przewaga wysokości (ΔH {s.ElevationDeltaMeters:+0.#}m): {s.BaseRadiusMeters:0}m ➔ {effR:0.0}m ({diffM:+0.0}m / {diffPct:+0}%){altInfo}";
                _rsBallisticSummaryText.Foreground = new SolidColorBrush(Color.FromRgb(46, 204, 113));
            }
            else
            {
                _rsBallisticSummaryText.Text = $"Strzał pod górę (ΔH {s.ElevationDeltaMeters:0.#}m): {s.BaseRadiusMeters:0}m ➔ {effR:0.0}m ({diffM:0.0}m / {diffPct:0}%){altInfo}";
                _rsBallisticSummaryText.Foreground = new SolidColorBrush(Color.FromRgb(250, 208, 44));
            }
        }

        private static string FormatAzimuthWithCardinal(double deg)
        {
            double norm = ((deg % 360.0) + 360.0) % 360.0;
            string[] dirs = new[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            int idx = (int)Math.Round(norm / 45.0) % 8;
            return $"{norm:0}° ({dirs[idx]})";
        }

        // ====================================================================
        // RENDERER TRWALYCH ELEMENTOW WIZUALNYCH (IN-PLACE UPDATE 60 FPS)
        // Eliminuje utrate MouseCapture podczas przesuwania i obracania myszka!
        // ====================================================================
        public static void RenderAllWeaponRangeSectorsOnSheet(double w, double h)
        {
            if (_interactiveCanvas == null || w <= 20 || h <= 20 || _isRenderingRangeSectors) return;

            _isRenderingRangeSectors = true;
            try
            {
                double mpp = GetExactMetersPerPixel();
                if (mpp <= 0.0001 || double.IsNaN(mpp) || double.IsInfinity(mpp)) mpp = 1.0;

                if (!_isInteractingWithSectorOnMap) { foreach (var sec in _weaponRangeSectors) { if (sec.HasGeoAnchor) SyncSectorOffsetsFromGeoAnchor(sec); } }
                if (_isInteractingWithSectorOnMap)
                {
                    foreach (var sec in _weaponRangeSectors)
                    {
                        if (sec.IsVisible && sec.Visuals != null)
                        {
                            UpdateSingleSectorVisualsInPlace(sec, w, h, mpp);
                        }
                    }
                    return;
                }

                bool allPresent = true;
                foreach (var sec in _weaponRangeSectors)
                {
                    if (!sec.IsVisible) continue;
                    if (sec.Visuals == null || sec.Visuals.OriginMarkerPin == null || !_interactiveCanvas.Children.Contains(sec.Visuals.OriginMarkerPin))
                    {
                        allPresent = false;
                        break;
                    }
                }

                if (!allPresent || _weaponRangeSectors.Any(s => !s.IsVisible && s.Visuals != null))
                {
                    var toRemove = new List<UIElement>();
                    foreach (UIElement el in _interactiveCanvas.Children)
                    {
                        if (el is FrameworkElement fe && fe.Tag is string t && t == "FTH_RANGE_SECTOR_EL")
                            toRemove.Add(el);
                    }
                    foreach (var rem in toRemove)
                        _interactiveCanvas.Children.Remove(rem);

                    foreach (var sec in _weaponRangeSectors)
                    {
                        sec.Visuals = null;
                        if (!sec.IsVisible) continue;
                        CreateSectorVisualsOnCanvas(sec, w, h, mpp);
                    }
                }
                else
                {
                    foreach (var sec in _weaponRangeSectors)
                    {
                        if (sec.IsVisible && sec.Visuals != null)
                        {
                            UpdateSingleSectorVisualsInPlace(sec, w, h, mpp);
                        }
                    }
                }
            }
            finally
            {
                _isRenderingRangeSectors = false;
            }
        }

        private static void CreateSectorVisualsOnCanvas(TacticalRangeSectorItem sec, double w, double h, double mpp)
        {
            var vis = new TacticalRangeSectorVisuals();
            sec.Visuals = vis;

            vis.SectorPath = new Path
            {
                Tag = "FTH_RANGE_SECTOR_EL",
                Cursor = Cursors.SizeAll,
                ToolTip = $"{sec.Name} — LPM: przesuwaj po mapie | PPM + ruch: obracaj kierunek | Scroll: obrót co 5°"
            };
            Panel.SetZIndex(vis.SectorPath, 910);
            AttachInteractiveMarkerMouseBehaviors(vis.SectorPath, sec);
            _interactiveCanvas.Children.Add(vis.SectorPath);

            vis.BaseArcPath = new Path
            {
                StrokeThickness = 1.2,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                IsHitTestVisible = false,
                Tag = "FTH_RANGE_SECTOR_EL"
            };
            Panel.SetZIndex(vis.BaseArcPath, 912);
            _interactiveCanvas.Children.Add(vis.BaseArcPath);

            vis.EffArcHighlight = new Path
            {
                StrokeThickness = 2.6,
                IsHitTestVisible = false,
                Tag = "FTH_RANGE_SECTOR_EL"
            };
            Panel.SetZIndex(vis.EffArcHighlight, 913);
            _interactiveCanvas.Children.Add(vis.EffArcHighlight);

            for (int i = 0; i < 16; i++)
            {
                var ringArc = new Path
                {
                    StrokeThickness = 0.8,
                    StrokeDashArray = new DoubleCollection { 2, 3 },
                    IsHitTestVisible = false,
                    Visibility = Visibility.Collapsed,
                    Tag = "FTH_RANGE_SECTOR_EL"
                };
                Panel.SetZIndex(ringArc, 911);
                vis.RingPaths.Add(ringArc);
                _interactiveCanvas.Children.Add(ringArc);
            }

            vis.AimLine = new Line
            {
                StrokeThickness = 1.4,
                StrokeDashArray = new DoubleCollection { 3, 2 },
                IsHitTestVisible = false,
                Tag = "FTH_RANGE_SECTOR_EL"
            };
            Panel.SetZIndex(vis.AimLine, 915);
            _interactiveCanvas.Children.Add(vis.AimLine);

            // Boczne uchwyty zmiany kata rozwarcia "Kawalka Pizzy" (Left / Right Wing Handles)
            vis.LeftWingHandle = CreateWingAngleHandle(sec);
            Panel.SetZIndex(vis.LeftWingHandle, 945);
            _interactiveCanvas.Children.Add(vis.LeftWingHandle);

            vis.RightWingHandle = CreateWingAngleHandle(sec);
            Panel.SetZIndex(vis.RightWingHandle, 945);
            _interactiveCanvas.Children.Add(vis.RightWingHandle);

            // Uchwyt obrotu kierunku (i zmiany zasiegu z Ctrl) na czole osi strzalu — jak znacznik obrotu na mapie
            vis.RotateHandle = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush(Color.FromRgb(6, 14, 18)),
                BorderThickness = new Thickness(2),
                Cursor = Cursors.Hand,
                Tag = "FTH_RANGE_SECTOR_EL",
                ToolTip = "Chwyć myszką (LPM) i obracaj kierunek ostrzału wokół stanowiska (przytrzymaj Ctrl, aby jednocześnie zmieniać zasięg w metrach)",
                Child = new TextBlock
                {
                    Text = "↻",
                    Foreground = Brushes.White,
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, -1, 0, 0)
                }
            };
            Panel.SetZIndex(vis.RotateHandle, 955);
            AttachRotateGizmoMouseBehavior(vis.RotateHandle, sec);
            _interactiveCanvas.Children.Add(vis.RotateHandle);

            // Centralny znacznik stanowiska ogniowego (w stylu znacznikow taktycznych na mapie)
            vis.OriginMarkerPin = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                BorderBrush = new SolidColorBrush(Color.FromRgb(6, 14, 18)),
                BorderThickness = new Thickness(2),
                Cursor = Cursors.SizeAll,
                Tag = "FTH_RANGE_SECTOR_EL",
                ToolTip = $"{sec.Name} — Chwyć LPM, aby przesunąć znacznik w dowolne miejsce na mapie (PPM + ruch myszką = obróć sektor)",
                Child = new TextBlock
                {
                    Text = "🎯",
                    FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Panel.SetZIndex(vis.OriginMarkerPin, 960);
            AttachInteractiveMarkerMouseBehaviors(vis.OriginMarkerPin, sec);
            _interactiveCanvas.Children.Add(vis.OriginMarkerPin);

            vis.LabelTextBlock = new TextBlock
            {
                Foreground = Brushes.White,
                FontSize = 8.8,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Consolas")
            };

            vis.LabelBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(232, 6, 14, 18)),
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 2, 5, 2),
                Cursor = Cursors.SizeAll,
                Tag = "FTH_RANGE_SECTOR_EL",
                ToolTip = "Chwyć LPM, aby przesunąć stanowisko po mapie | PPM + ruch myszką = obróć sektor",
                Child = vis.LabelTextBlock
            };
            Panel.SetZIndex(vis.LabelBadge, 960);
            AttachInteractiveMarkerMouseBehaviors(vis.LabelBadge, sec);
            _interactiveCanvas.Children.Add(vis.LabelBadge);

            UpdateSingleSectorVisualsInPlace(sec, w, h, mpp);
        }

        private static Border CreateWingAngleHandle(TacticalRangeSectorItem sec)
        {
            var wing = new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(Color.FromRgb(6, 14, 18)),
                BorderThickness = new Thickness(1.8),
                Cursor = Cursors.SizeWE,
                Tag = "FTH_RANGE_SECTOR_EL",
                ToolTip = "Przeciągnij myszką, aby rozszerzyć lub zwęzić kąt 'kawałka pizzy' (5°–360°)",
                Child = new TextBlock
                {
                    Text = "◆",
                    Foreground = Brushes.White,
                    FontSize = 7.5,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            AttachSpreadWingMouseBehavior(wing, sec);
            return wing;
        }

        private static void UpdateSingleSectorVisualsInPlace(TacticalRangeSectorItem sec, double w, double h, double mpp)
        {
            var vis = sec.Visuals;
            if (vis == null) return;

            double halfW = w / 2.0;
            double halfH = h / 2.0;

            double cx = halfW + (sec.OffsetEastMeters / mpp);
            double cy = halfH - (sec.OffsetNorthMeters / mpp);

            double baseRadiusPx = Math.Max(8.0, sec.BaseRadiusMeters / mpp);
            double effRadiusPx = Math.Max(8.0, sec.GetEffectiveRadiusMeters() / mpp);

            Color mainColor = Colors.Cyan;
            try { mainColor = (Color)ColorConverter.ConvertFromString(sec.ColorHex); } catch { }

            byte fillAlpha = (byte)Math.Clamp((int)Math.Round(sec.FillOpacity * 255.0), 15, 200);
            Brush fillBrush = new SolidColorBrush(Color.FromArgb(fillAlpha, mainColor.R, mainColor.G, mainColor.B));
            Brush strokeBrush = new SolidColorBrush(mainColor);
            Brush baseRefBrush = new SolidColorBrush(Color.FromArgb(190, 255, 255, 255));

            bool isFullCircle = sec.SpreadAngleDeg >= 359.5;
            double azRad = sec.AzimuthDeg * Math.PI / 180.0;

            vis.SectorPath.Fill = fillBrush;
            vis.SectorPath.Stroke = strokeBrush;
            vis.SectorPath.StrokeThickness = ReferenceEquals(sec, _selectedRangeSector) ? 2.2 : 1.6;
            vis.SectorPath.Data = BuildSectorOrCircleGeometry(cx, cy, effRadiusPx, sec.AzimuthDeg, sec.SpreadAngleDeg, isClosedSlice: true);

            if (Math.Abs(sec.ElevationDeltaMeters) >= 0.5)
            {
                Brush deltaArcBrush = sec.ElevationDeltaMeters > 0
                    ? new SolidColorBrush(Color.FromArgb(230, 46, 204, 113))
                    : new SolidColorBrush(Color.FromArgb(230, 250, 208, 44));

                vis.BaseArcPath.Visibility = Visibility.Visible;
                vis.BaseArcPath.Stroke = baseRefBrush;
                vis.BaseArcPath.Data = BuildSectorOrCircleGeometry(cx, cy, baseRadiusPx, sec.AzimuthDeg, sec.SpreadAngleDeg, isClosedSlice: false);

                vis.EffArcHighlight.Visibility = Visibility.Visible;
                vis.EffArcHighlight.Stroke = deltaArcBrush;
                vis.EffArcHighlight.Data = BuildSectorOrCircleGeometry(cx, cy, effRadiusPx, sec.AzimuthDeg, sec.SpreadAngleDeg, isClosedSlice: false);
            }
            else
            {
                vis.BaseArcPath.Visibility = Visibility.Collapsed;
                vis.EffArcHighlight.Visibility = Visibility.Collapsed;
            }

            double effMeters = sec.GetEffectiveRadiusMeters();
            int ringIdx = 0;
            for (double ringM = 25.0; ringM < effMeters - 5.0 && ringIdx < vis.RingPaths.Count; ringM += 25.0, ringIdx++)
            {
                var rp = vis.RingPaths[ringIdx];
                rp.Visibility = Visibility.Visible;
                rp.Stroke = new SolidColorBrush(Color.FromArgb(90, mainColor.R, mainColor.G, mainColor.B));
                rp.Data = BuildSectorOrCircleGeometry(cx, cy, ringM / mpp, sec.AzimuthDeg, sec.SpreadAngleDeg, isClosedSlice: false);
            }
            for (int k = ringIdx; k < vis.RingPaths.Count; k++)
            {
                vis.RingPaths[k].Visibility = Visibility.Collapsed;
            }

            double tipX = cx + effRadiusPx * Math.Sin(azRad);
            double tipY = cy - effRadiusPx * Math.Cos(azRad);

            vis.AimLine.X1 = cx;
            vis.AimLine.Y1 = cy;
            vis.AimLine.X2 = tipX;
            vis.AimLine.Y2 = tipY;
            vis.AimLine.Stroke = strokeBrush;

            vis.RotateHandle.BorderBrush = strokeBrush;
            Canvas.SetLeft(vis.RotateHandle, tipX - 11);
            Canvas.SetTop(vis.RotateHandle, tipY - 11);

            double halfSpread = sec.SpreadAngleDeg / 2.0;
            double leftRad = (sec.AzimuthDeg - halfSpread) * Math.PI / 180.0;
            double rightRad = (sec.AzimuthDeg + halfSpread) * Math.PI / 180.0;
            double wingDistPx = effRadiusPx * 0.72;

            vis.LeftWingHandle.BorderBrush = strokeBrush;
            Canvas.SetLeft(vis.LeftWingHandle, cx + wingDistPx * Math.Sin(leftRad) - 7);
            Canvas.SetTop(vis.LeftWingHandle, cy - wingDistPx * Math.Cos(leftRad) - 7);

            vis.RightWingHandle.BorderBrush = strokeBrush;
            Canvas.SetLeft(vis.RightWingHandle, cx + wingDistPx * Math.Sin(rightRad) - 7);
            Canvas.SetTop(vis.RightWingHandle, cy - wingDistPx * Math.Cos(rightRad) - 7);

            vis.OriginMarkerPin.Background = strokeBrush;
            Canvas.SetLeft(vis.OriginMarkerPin, cx - 11);
            Canvas.SetTop(vis.OriginMarkerPin, cy - 11);

            string dhBadgeText = Math.Abs(sec.ElevationDeltaMeters) >= 0.5
                ? $" ➔ {effMeters:0}m (ΔH:{sec.ElevationDeltaMeters:+0.#;-0.#}m)"
                : "";
            string angleBadgeText = isFullCircle ? "360°" : $"{sec.SpreadAngleDeg:0}° | Az:{sec.AzimuthDeg:0}°";

            vis.LabelBadge.BorderBrush = strokeBrush;
            vis.LabelTextBlock.Text = $"{sec.Name}: {sec.BaseRadiusMeters:0}m{dhBadgeText} [{angleBadgeText}]";
            Canvas.SetLeft(vis.LabelBadge, cx + 14);
            Canvas.SetTop(vis.LabelBadge, cy - 11);
        }

        private static Geometry BuildSectorOrCircleGeometry(
            double cx,
            double cy,
            double radiusPx,
            double azimuthDeg,
            double spreadDeg,
            bool isClosedSlice)
        {
            if (spreadDeg >= 359.5)
            {
                return new EllipseGeometry(new Point(cx, cy), radiusPx, radiusPx);
            }

            double halfSpread = spreadDeg / 2.0;
            double startDeg = azimuthDeg - halfSpread;
            double endDeg = azimuthDeg + halfSpread;

            double startRad = startDeg * Math.PI / 180.0;
            double endRad = endDeg * Math.PI / 180.0;

            Point pStart = new Point(cx + radiusPx * Math.Sin(startRad), cy - radiusPx * Math.Cos(startRad));
            Point pEnd = new Point(cx + radiusPx * Math.Sin(endRad), cy - radiusPx * Math.Cos(endRad));

            var fig = new PathFigure();
            if (isClosedSlice)
            {
                fig.StartPoint = new Point(cx, cy);
                fig.IsClosed = true;
                fig.IsFilled = true;
                fig.Segments.Add(new LineSegment(pStart, isStroked: true));
                fig.Segments.Add(new ArcSegment(
                    pEnd,
                    new Size(radiusPx, radiusPx),
                    rotationAngle: 0,
                    isLargeArc: spreadDeg > 180.0,
                    sweepDirection: SweepDirection.Clockwise,
                    isStroked: true));
            }
            else
            {
                fig.StartPoint = pStart;
                fig.IsClosed = false;
                fig.IsFilled = false;
                fig.Segments.Add(new ArcSegment(
                    pEnd,
                    new Size(radiusPx, radiusPx),
                    rotationAngle: 0,
                    isLargeArc: spreadDeg > 180.0,
                    sweepDirection: SweepDirection.Clockwise,
                    isStroked: true));
            }

            var geom = new PathGeometry();
            geom.Figures.Add(fig);
            return geom;
        }

        // ====================================================================
        // INTERAKCJA MYSZKA NA MAPIE (LPM = PRZESUWANIE, PPM / SCROLL = OBROT)
        // ====================================================================
        private static void AttachInteractiveMarkerMouseBehaviors(FrameworkElement el, TacticalRangeSectorItem sec)
        {
            bool isDraggingMove = false;
            bool isRightRotating = false;
            Point startMouse = new Point();
            double startEastM = 0;
            double startNorthM = 0;

            // 1. Lewy przycisk myszy (LPM) -> Plynne przesuwanie znacznika i sektora po calej mapie
            el.MouseLeftButtonDown += (s, e) =>
            {
                if (_interactiveCanvas == null) return;
                _selectedRangeSector = sec;
                PopulateRangeSectorEditorFromSelected();

                isDraggingMove = true;
                _isInteractingWithSectorOnMap = true;
                startMouse = e.GetPosition(_interactiveCanvas);
                startEastM = sec.OffsetEastMeters;
                startNorthM = sec.OffsetNorthMeters;

                el.CaptureMouse();
                e.Handled = true;
            };

            // 2. Prawy przycisk myszy (PPM) -> Bezposrednie obracanie kierunku sektora myszka wokol znacznika!
            el.MouseRightButtonDown += (s, e) =>
            {
                if (_interactiveCanvas == null) return;
                _selectedRangeSector = sec;
                PopulateRangeSectorEditorFromSelected();

                isRightRotating = true;
                _isInteractingWithSectorOnMap = true;
                el.CaptureMouse();
                e.Handled = true;
            };

            el.MouseMove += (s, e) =>
            {
                if (_interactiveCanvas == null) return;

                if (isDraggingMove)
                {
                    Point cur = e.GetPosition(_interactiveCanvas);
                    double mpp = GetExactMetersPerPixel();
                    if (mpp <= 0.0001 || double.IsNaN(mpp) || double.IsInfinity(mpp)) mpp = 1.0;

                    sec.OffsetEastMeters = startEastM + (cur.X - startMouse.X) * mpp;
                    sec.OffsetNorthMeters = startNorthM - (cur.Y - startMouse.Y) * mpp;
                    SyncSectorGeoAnchorFromOffsets(sec);

                    var (cw, ch) = GetExactCanvasSize();
                    UpdateSingleSectorVisualsInPlace(sec, cw, ch, mpp);
                    e.Handled = true;
                }
                else if (isRightRotating)
                {
                    Point cur = e.GetPosition(_interactiveCanvas);
                    var (cw, ch) = GetExactCanvasSize();
                    double mpp = GetExactMetersPerPixel();
                    if (mpp <= 0.0001 || double.IsNaN(mpp) || double.IsInfinity(mpp)) mpp = 1.0;

                    double cx = (cw / 2.0) + (sec.OffsetEastMeters / mpp);
                    double cy = (ch / 2.0) - (sec.OffsetNorthMeters / mpp);

                    double dx = cur.X - cx;
                    double dy = cy - cur.Y;
                    if (Math.Abs(dx) > 1.0 || Math.Abs(dy) > 1.0)
                    {
                        double angleDeg = Math.Atan2(dx, dy) * 180.0 / Math.PI;
                        if (angleDeg < 0) angleDeg += 360.0;
                        sec.AzimuthDeg = Math.Round(angleDeg);

                        SyncSelectedSectorSlidersFast(sec);
                        UpdateSingleSectorVisualsInPlace(sec, cw, ch, mpp);
                    }
                    e.Handled = true;
                }
            };

            el.MouseLeftButtonUp += (s, e) =>
            {
                if (!isDraggingMove) return;
                isDraggingMove = false;
                _isInteractingWithSectorOnMap = false;
                el.ReleaseMouseCapture();
                RebuildRangeSectorsListUi(); if (_lastLegendContainerGrid != null) UpdateRangeSectorsInLegendBox(_lastLegendContainerGrid);
                try { TacticalConfigPanel.MarkDirty(); } catch { }
                e.Handled = true;
            };

            el.MouseRightButtonUp += (s, e) =>
            {
                if (!isRightRotating) return;
                isRightRotating = false;
                _isInteractingWithSectorOnMap = false;
                el.ReleaseMouseCapture();
                RebuildRangeSectorsListUi(); if (_lastLegendContainerGrid != null) UpdateRangeSectorsInLegendBox(_lastLegendContainerGrid);
                try { TacticalConfigPanel.MarkDirty(); } catch { }
                e.Handled = true;
            };

            // 3. Kolko myszy (Scroll) nad znacznikiem -> obrot o 5° (z Shift = zmiana kata pizzy, z Ctrl = zmiana promienia)
            el.MouseWheel += (s, e) =>
            {
                _selectedRangeSector = sec;
                double step = e.Delta > 0 ? 5.0 : -5.0;

                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
                {
                    sec.SpreadAngleDeg = Math.Clamp(sec.SpreadAngleDeg + step, 5.0, 360.0);
                }
                else if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                {
                    sec.BaseRadiusMeters = Math.Clamp(sec.BaseRadiusMeters + step, 5.0, 500.0);
                }
                else
                {
                    sec.AzimuthDeg = ((sec.AzimuthDeg + step) % 360.0 + 360.0) % 360.0;
                }

                PopulateRangeSectorEditorFromSelected();
                RebuildRangeSectorsListUi(); if (_lastLegendContainerGrid != null) UpdateRangeSectorsInLegendBox(_lastLegendContainerGrid);
                var (cw, ch) = GetExactCanvasSize();
                double mpp = GetExactMetersPerPixel();
                if (mpp <= 0.0001 || double.IsNaN(mpp) || double.IsInfinity(mpp)) mpp = 1.0;
                UpdateSingleSectorVisualsInPlace(sec, cw, ch, mpp);
                e.Handled = true;
            };
        }

        private static void AttachRotateGizmoMouseBehavior(FrameworkElement handleEl, TacticalRangeSectorItem sec)
        {
            bool isRotating = false;

            handleEl.MouseLeftButtonDown += (s, e) =>
            {
                if (_interactiveCanvas == null) return;
                _selectedRangeSector = sec;
                PopulateRangeSectorEditorFromSelected();

                isRotating = true;
                _isInteractingWithSectorOnMap = true;
                handleEl.CaptureMouse();
                e.Handled = true;
            };

            handleEl.MouseMove += (s, e) =>
            {
                if (!isRotating || _interactiveCanvas == null) return;
                Point p = e.GetPosition(_interactiveCanvas);
                var (cw, ch) = GetExactCanvasSize();
                double mpp = GetExactMetersPerPixel();
                if (mpp <= 0.0001 || double.IsNaN(mpp) || double.IsInfinity(mpp)) mpp = 1.0;

                double cx = (cw / 2.0) + (sec.OffsetEastMeters / mpp);
                double cy = (ch / 2.0) - (sec.OffsetNorthMeters / mpp);

                double dx = p.X - cx;
                double dy = cy - p.Y;
                if (Math.Abs(dx) > 1.0 || Math.Abs(dy) > 1.0)
                {
                    double angleDeg = Math.Atan2(dx, dy) * 180.0 / Math.PI;
                    if (angleDeg < 0) angleDeg += 360.0;
                    sec.AzimuthDeg = Math.Round(angleDeg);

                    if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                    {
                        double distPx = Math.Sqrt(dx * dx + dy * dy);
                        sec.BaseRadiusMeters = Math.Clamp(Math.Round(distPx * mpp), 5.0, 500.0);
                    }

                    SyncSelectedSectorSlidersFast(sec);
                    UpdateSingleSectorVisualsInPlace(sec, cw, ch, mpp);
                }
                e.Handled = true;
            };

            handleEl.MouseLeftButtonUp += (s, e) =>
            {
                if (!isRotating) return;
                isRotating = false;
                _isInteractingWithSectorOnMap = false;
                handleEl.ReleaseMouseCapture();
                RebuildRangeSectorsListUi(); if (_lastLegendContainerGrid != null) UpdateRangeSectorsInLegendBox(_lastLegendContainerGrid);
                try { TacticalConfigPanel.MarkDirty(); } catch { }
                e.Handled = true;
            };
        }

        private static void AttachSpreadWingMouseBehavior(FrameworkElement wingEl, TacticalRangeSectorItem sec)
        {
            bool isSpreading = false;

            wingEl.MouseLeftButtonDown += (s, e) =>
            {
                if (_interactiveCanvas == null) return;
                _selectedRangeSector = sec;
                PopulateRangeSectorEditorFromSelected();

                isSpreading = true;
                _isInteractingWithSectorOnMap = true;
                wingEl.CaptureMouse();
                e.Handled = true;
            };

            wingEl.MouseMove += (s, e) =>
            {
                if (!isSpreading || _interactiveCanvas == null) return;
                Point p = e.GetPosition(_interactiveCanvas);
                var (cw, ch) = GetExactCanvasSize();
                double mpp = GetExactMetersPerPixel();
                if (mpp <= 0.0001 || double.IsNaN(mpp) || double.IsInfinity(mpp)) mpp = 1.0;

                double cx = (cw / 2.0) + (sec.OffsetEastMeters / mpp);
                double cy = (ch / 2.0) - (sec.OffsetNorthMeters / mpp);

                double dx = p.X - cx;
                double dy = cy - p.Y;
                if (Math.Abs(dx) > 1.0 || Math.Abs(dy) > 1.0)
                {
                    double cursorAngle = Math.Atan2(dx, dy) * 180.0 / Math.PI;
                    if (cursorAngle < 0) cursorAngle += 360.0;

                    double diff = Math.Abs(((cursorAngle - sec.AzimuthDeg + 540.0) % 360.0) - 180.0);
                    sec.SpreadAngleDeg = Math.Clamp(Math.Round(diff * 2.0), 5.0, 360.0);

                    SyncSelectedSectorSlidersFast(sec);
                    UpdateSingleSectorVisualsInPlace(sec, cw, ch, mpp);
                }
                e.Handled = true;
            };

            wingEl.MouseLeftButtonUp += (s, e) =>
            {
                if (!isSpreading) return;
                isSpreading = false;
                _isInteractingWithSectorOnMap = false;
                wingEl.ReleaseMouseCapture();
                RebuildRangeSectorsListUi(); if (_lastLegendContainerGrid != null) UpdateRangeSectorsInLegendBox(_lastLegendContainerGrid);
                try { TacticalConfigPanel.MarkDirty(); } catch { }
                e.Handled = true;
            };
        }

        private static void SyncSelectedSectorSlidersFast(TacticalRangeSectorItem sec)
        {
            if (!ReferenceEquals(_selectedRangeSector, sec)) return;
            _suppressRangeSectorUiEvents = true;
            try
            {
                if (_rsAzimuthSlider != null) _rsAzimuthSlider.Value = sec.AzimuthDeg;
                if (_rsAzimuthValText != null) _rsAzimuthValText.Text = FormatAzimuthWithCardinal(sec.AzimuthDeg);
                if (_rsSpreadSlider != null) _rsSpreadSlider.Value = sec.SpreadAngleDeg;
                if (_rsSpreadValText != null)
                    _rsSpreadValText.Text = sec.SpreadAngleDeg >= 359 ? "360° (Pełny okrąg)" : $"{sec.SpreadAngleDeg:0}° (Wycinek pizzy)";
                if (_rsBaseRadiusBox != null && !_rsBaseRadiusBox.IsKeyboardFocused)
                    _rsBaseRadiusBox.Text = sec.BaseRadiusMeters.ToString("0.#", CultureInfo.InvariantCulture);
            }
            finally
            {
                _suppressRangeSectorUiEvents = false;
            }
        }
    
        private static Grid _lastLegendContainerGrid = null;

        private static void UpdateRangeSectorsInLegendBox(Grid legContainer)
        {
            if (legContainer == null) return;
            _lastLegendContainerGrid = legContainer;

            StackPanel legStack = null;
            foreach (UIElement child in legContainer.Children)
            {
                if (child is Border legBorder && legBorder.Child is StackPanel sp)
                {
                    legStack = sp;
                    break;
                }
            }
            if (legStack == null) return;

            var oldPanels = new List<UIElement>();
            foreach (UIElement el in legStack.Children)
            {
                if (el is FrameworkElement fe && fe.Tag is string t && t == "FTH_RANGE_SECTOR_LEGEND_PANEL")
                    oldPanels.Add(el);
            }
            foreach (var rem in oldPanels)
                legStack.Children.Remove(rem);

            var visibleSectors = _weaponRangeSectors.Where(s => s.IsVisible).ToList();
            if (visibleSectors.Count == 0) return;

            var wrap = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 4, 0, 2),
                Tag = "FTH_RANGE_SECTOR_LEGEND_PANEL"
            };

            foreach (var sec in visibleSectors)
            {
                Color mainColor = Colors.Cyan;
                try { mainColor = (Color)ColorConverter.ConvertFromString(sec.ColorHex); } catch { }
                Brush strokeBrush = new SolidColorBrush(mainColor);
                Brush fillBrush = new SolidColorBrush(Color.FromArgb(145, mainColor.R, mainColor.G, mainColor.B));

                var itemPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 2, 14, 2),
                    VerticalAlignment = VerticalAlignment.Center
                };

                var iconCanvas = new Canvas
                {
                    Width = 16,
                    Height = 16,
                    Margin = new Thickness(0, 0, 5, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };

                var sliceIcon = new Path
                {
                    Fill = fillBrush,
                    Stroke = strokeBrush,
                    StrokeThickness = 1.2,
                    Data = BuildSectorOrCircleGeometry(8.0, 8.0, 7.0, sec.AzimuthDeg, sec.SpreadAngleDeg, isClosedSlice: true)
                };
                iconCanvas.Children.Add(sliceIcon);
                itemPanel.Children.Add(iconCanvas);

                double effR = sec.GetEffectiveRadiusMeters();
                string rangeInfo = Math.Abs(sec.ElevationDeltaMeters) >= 0.5
                    ? $"{sec.BaseRadiusMeters:0}m ➔ {effR:0}m (ΔH:{sec.ElevationDeltaMeters:+0.#;-0.#}m)"
                    : $"{sec.BaseRadiusMeters:0}m";
                string shapeInfo = sec.SpreadAngleDeg >= 359.5
                    ? "Okrąg 360°"
                    : $"Sektor {sec.SpreadAngleDeg:0}° (Az:{sec.AzimuthDeg:0}°)";

                var lbl = new TextBlock
                {
                    Text = $"{sec.Name}: {rangeInfo} [{shapeInfo}]",
                    Foreground = Brushes.White,
                    FontSize = 9.0,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                };
                itemPanel.Children.Add(lbl);
                wrap.Children.Add(itemPanel);
            }

            legStack.Children.Add(wrap);
        }

        // ====================================================================
        // EKSPORT SEKTOROW OGNIA ("PIZZA" + dH) DO .KMZ / .KML DLA ATAK
        // ====================================================================
        public static string InjectRangeSectorsIntoKmlDocument(string kmlContent)
        {
            if (string.IsNullOrEmpty(kmlContent)) return kmlContent;
            if (kmlContent.Contains("<name>Sektory Ognia i Zasięg Replik</name>"))
                return kmlContent;

            string fragment = BuildWeaponRangeSectorsKmlFragment();
            if (string.IsNullOrEmpty(fragment))
                return kmlContent;

            int docCloseIdx = kmlContent.LastIndexOf("</Document>", StringComparison.OrdinalIgnoreCase);
            if (docCloseIdx >= 0)
            {
                return kmlContent.Substring(0, docCloseIdx) + fragment + kmlContent.Substring(docCloseIdx);
            }
            return kmlContent;
        }

        public static string BuildWeaponRangeSectorsKmlFragment()
        {
            var visibleSectors = _weaponRangeSectors.Where(s => s.IsVisible).ToList();
            if (visibleSectors.Count == 0) return string.Empty;

            double centerLat = _centerLat;
            double centerLon = _centerLon;
            double latRad = centerLat * Math.PI / 180.0;
            double mPerDegLat = 111132.92 - 559.82 * Math.Cos(2.0 * latRad) + 1.175 * Math.Cos(4.0 * latRad);
            double mPerDegLon = Math.Max(1000.0, 111412.84 * Math.Cos(latRad) - 93.5 * Math.Cos(3.0 * latRad));

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("    <Folder>");
            sb.AppendLine("      <name>Sektory Ognia i Zasięg Replik</name>");

            foreach (var sec in visibleSectors)
            {
                Color c = Colors.Cyan;
                try { c = (Color)ColorConverter.ConvertFromString(sec.ColorHex); } catch { }

                byte fillAlpha = (byte)Math.Clamp((int)Math.Round(sec.FillOpacity * 255.0), 30, 210);
                // Format KML to aabbggrr (Alpha, Blue, Green, Red)
                string kmlLineColor = string.Format(CultureInfo.InvariantCulture, "ff{0:x2}{1:x2}{2:x2}", c.B, c.G, c.R);
                string kmlFillColor = string.Format(CultureInfo.InvariantCulture, "{0:x2}{1:x2}{2:x2}{3:x2}", fillAlpha, c.B, c.G, c.R);
                string styleId = "fth_sector_" + sec.Id;
                string baseArcStyleId = "fth_sector_base_" + sec.Id;

                sb.AppendLine($"      <Style id=\"{styleId}\">");
                sb.AppendLine("        <LineStyle>");
                sb.AppendLine($"          <color>{kmlLineColor}</color>");
                sb.AppendLine("          <width>2.5</width>");
                sb.AppendLine("        </LineStyle>");
                sb.AppendLine("        <PolyStyle>");
                sb.AppendLine($"          <color>{kmlFillColor}</color>");
                sb.AppendLine("          <fill>1</fill>");
                sb.AppendLine("          <outline>1</outline>");
                sb.AppendLine("        </PolyStyle>");
                sb.AppendLine("      </Style>");

                double shooterLat = centerLat + (sec.OffsetNorthMeters / mPerDegLat);
                double shooterLon = centerLon + (sec.OffsetEastMeters / mPerDegLon);
                double effR = sec.GetEffectiveRadiusMeters();
                bool isFullCircle = sec.SpreadAngleDeg >= 359.5;

                // 1. Wielokat (Polygon) zasiegu efektywnego (pelne kolo lub wycinek "Kawalek Pizzy")
                var coordsSb = new System.Text.StringBuilder();
                if (isFullCircle)
                {
                    int steps = 64;
                    for (int i = 0; i <= steps; i++)
                    {
                        double angRad = (i * 360.0 / steps) * Math.PI / 180.0;
                        double pEastM = sec.OffsetEastMeters + effR * Math.Sin(angRad);
                        double pNorthM = sec.OffsetNorthMeters + effR * Math.Cos(angRad);
                        double pLon = centerLon + (pEastM / mPerDegLon);
                        double pLat = centerLat + (pNorthM / mPerDegLat);
                        coordsSb.AppendFormat(CultureInfo.InvariantCulture, "{0:F6},{1:F6},0 ", pLon, pLat);
                    }
                }
                else
                {
                    coordsSb.AppendFormat(CultureInfo.InvariantCulture, "{0:F6},{1:F6},0 ", shooterLon, shooterLat);

                    double halfSpread = sec.SpreadAngleDeg / 2.0;
                    double startDeg = sec.AzimuthDeg - halfSpread;
                    int steps = 36;
                    for (int i = 0; i <= steps; i++)
                    {
                        double deg = startDeg + (sec.SpreadAngleDeg * i) / steps;
                        double angRad = deg * Math.PI / 180.0;
                        double pEastM = sec.OffsetEastMeters + effR * Math.Sin(angRad);
                        double pNorthM = sec.OffsetNorthMeters + effR * Math.Cos(angRad);
                        double pLon = centerLon + (pEastM / mPerDegLon);
                        double pLat = centerLat + (pNorthM / mPerDegLat);
                        coordsSb.AppendFormat(CultureInfo.InvariantCulture, "{0:F6},{1:F6},0 ", pLon, pLat);
                    }

                    coordsSb.AppendFormat(CultureInfo.InvariantCulture, "{0:F6},{1:F6},0", shooterLon, shooterLat);
                }

                string dhSuffix = Math.Abs(sec.ElevationDeltaMeters) >= 0.5
                    ? $" -> {effR:0}m (dH:{sec.ElevationDeltaMeters:+0.#;-0.#}m)"
                    : "";
                string polyTitle = EscapeKmlXml($"{sec.Name} [{sec.BaseRadiusMeters:0}m{dhSuffix}]");
                string polyDesc = EscapeKmlXml(
                    $"Stanowisko: {sec.Name} | Klasa: {sec.WeaponPreset} | Zasięg płaski: {sec.BaseRadiusMeters:0} m | " +
                    $"Zasięg efektywny: {effR:0.0} m | Przewaga wysokości dH: {sec.ElevationDeltaMeters:+0.#;-0.#;0} m | " +
                    $"Azymut: {sec.AzimuthDeg:0} st. | Kąt sektora: {sec.SpreadAngleDeg:0} st.");

                sb.AppendLine("      <Placemark>");
                sb.AppendLine($"        <name>{polyTitle}</name>");
                sb.AppendLine($"        <description>{polyDesc}</description>");
                sb.AppendLine($"        <styleUrl>#{styleId}</styleUrl>");
                sb.AppendLine("        <Polygon>");
                sb.AppendLine("          <tessellate>1</tessellate>");
                sb.AppendLine("          <altitudeMode>clampToGround</altitudeMode>");
                sb.AppendLine("          <outerBoundaryIs>");
                sb.AppendLine("            <LinearRing>");
                sb.AppendLine($"              <coordinates>{coordsSb.ToString().Trim()}</coordinates>");
                sb.AppendLine("            </LinearRing>");
                sb.AppendLine("          </outerBoundaryIs>");
                sb.AppendLine("        </Polygon>");
                sb.AppendLine("      </Placemark>");

                // 2. Luk zasiegu plaskiego (BaseRadiusMeters), jezeli wystepuje przewaga wysokosci (dH != 0)
                if (Math.Abs(sec.ElevationDeltaMeters) >= 0.5)
                {
                    sb.AppendLine($"      <Style id=\"{baseArcStyleId}\">");
                    sb.AppendLine("        <LineStyle>");
                    sb.AppendLine("          <color>dcffffff</color>");
                    sb.AppendLine("          <width>1.8</width>");
                    sb.AppendLine("        </LineStyle>");
                    sb.AppendLine("      </Style>");

                    var baseArcSb = new System.Text.StringBuilder();
                    double halfSpread = isFullCircle ? 180.0 : (sec.SpreadAngleDeg / 2.0);
                    double startDeg = sec.AzimuthDeg - halfSpread;
                    double totalSweep = isFullCircle ? 360.0 : sec.SpreadAngleDeg;
                    int steps = 36;
                    for (int i = 0; i <= steps; i++)
                    {
                        double deg = startDeg + (totalSweep * i) / steps;
                        double angRad = deg * Math.PI / 180.0;
                        double pEastM = sec.OffsetEastMeters + sec.BaseRadiusMeters * Math.Sin(angRad);
                        double pNorthM = sec.OffsetNorthMeters + sec.BaseRadiusMeters * Math.Cos(angRad);
                        double pLon = centerLon + (pEastM / mPerDegLon);
                        double pLat = centerLat + (pNorthM / mPerDegLat);
                        baseArcSb.AppendFormat(CultureInfo.InvariantCulture, "{0:F6},{1:F6},0 ", pLon, pLat);
                    }

                    sb.AppendLine("      <Placemark>");
                    sb.AppendLine($"        <name>{EscapeKmlXml(sec.Name + " (Zasięg płaski " + sec.BaseRadiusMeters.ToString("0", CultureInfo.InvariantCulture) + "m)")}</name>");
                    sb.AppendLine($"        <styleUrl>#{baseArcStyleId}</styleUrl>");
                    sb.AppendLine("        <LineString>");
                    sb.AppendLine("          <tessellate>1</tessellate>");
                    sb.AppendLine($"          <coordinates>{baseArcSb.ToString().Trim()}</coordinates>");
                    sb.AppendLine("        </LineString>");
                    sb.AppendLine("      </Placemark>");
                }

                // 3. Punkt centralny stanowiska ogniowego dla ATAK
                sb.AppendLine("      <Placemark>");
                sb.AppendLine($"        <name>{EscapeKmlXml("Stanowisko: " + sec.Name)}</name>");
                sb.AppendLine($"        <description>{polyDesc}</description>");
                sb.AppendLine($"        <styleUrl>#{styleId}</styleUrl>");
                sb.AppendLine("        <Point>");
                sb.AppendLine($"          <coordinates>{shooterLon.ToString("F6", CultureInfo.InvariantCulture)},{shooterLat.ToString("F6", CultureInfo.InvariantCulture)},0</coordinates>");
                sb.AppendLine("        </Point>");
                sb.AppendLine("      </Placemark>");
            }

            sb.AppendLine("    </Folder>");
            return sb.ToString();
        }

        private static void ExportStandaloneRangeSectorsKmzDialog()
        {
            var visibleSectors = _weaponRangeSectors.Where(s => s.IsVisible).ToList();
            if (visibleSectors.Count == 0)
            {
                MessageBox.Show("Brak aktywnych sektorów ognia na mapie. Dodaj najpierw przynajmniej jeden sektor.", "Eksport KMZ dla ATAK", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Eksportuj Sektory Ognia do pliku .KMZ dla ATAK",
                Filter = "Paczka mapowa ATAK KMZ (*.kmz)|*.kmz|Plik KML (*.kml)|*.kml",
                FileName = "Sektory_Ognia_ATAK.kmz"
            };

            if (dlg.ShowDialog() != true) return;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<kml xmlns=\"http://www.opengis.net/kml/2.2\">");
            sb.AppendLine("  <Document>");
            sb.AppendLine("    <name>Sektory Ognia i Zasięg Replik (Field TAK Hub)</name>");
            sb.Append(BuildWeaponRangeSectorsKmlFragment());
            sb.AppendLine("  </Document>");
            sb.AppendLine("</kml>");

            string kmlText = sb.ToString();
            byte[] kmlBytes = System.Text.Encoding.UTF8.GetBytes(kmlText);

            if (dlg.FileName.EndsWith(".kml", StringComparison.OrdinalIgnoreCase))
            {
                System.IO.File.WriteAllText(dlg.FileName, kmlText, new System.Text.UTF8Encoding(false));
            }
            else
            {
                using (var fs = new System.IO.FileStream(dlg.FileName, System.IO.FileMode.Create, System.IO.FileAccess.Write))
                using (var zos = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create))
                {
                    var entry = zos.CreateEntry("doc.kml", System.IO.Compression.CompressionLevel.Optimal);
                    using (var es = entry.Open())
                    {
                        es.Write(kmlBytes, 0, kmlBytes.Length);
                    }
                }
            }
        }

        private static string EscapeKmlXml(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("&", "&amp;")
                    .Replace("<", "&lt;")
                    .Replace(">", "&gt;")
                    .Replace("\"", "&quot;")
                    .Replace("'", "&apos;");
        }
    
        // ====================================================================
        // KOTWICZENIE GEOGRAFICZNE WGS84 (LAT/LON <-> OFFSET METERS)
        // Zapewnia, ze wczytane z .KMZ/.KML sektory trafiaja dokladnie w swoje
        // wspolrzedne GPS na mapie niezaleznie od przesuniecia srodka arkusza!
        // ====================================================================
        private static void SyncSectorGeoAnchorFromOffsets(TacticalRangeSectorItem sec)
        {
            if (sec == null) return;
            double centerLat = _centerLat;
            double centerLon = _centerLon;
            double latRad = centerLat * Math.PI / 180.0;
            double mPerDegLat = 111132.92 - 559.82 * Math.Cos(2.0 * latRad) + 1.175 * Math.Cos(4.0 * latRad);
            double mPerDegLon = Math.Max(1000.0, 111412.84 * Math.Cos(latRad) - 93.5 * Math.Cos(3.0 * latRad));

            sec.ShooterLat = centerLat + (sec.OffsetNorthMeters / mPerDegLat);
            sec.ShooterLon = centerLon + (sec.OffsetEastMeters / mPerDegLon);
            sec.HasGeoAnchor = true;
        }

        private static void SyncSectorOffsetsFromGeoAnchor(TacticalRangeSectorItem sec)
        {
            if (sec == null || !sec.HasGeoAnchor) return;
            double centerLat = _centerLat;
            double centerLon = _centerLon;
            double latRad = centerLat * Math.PI / 180.0;
            double mPerDegLat = 111132.92 - 559.82 * Math.Cos(2.0 * latRad) + 1.175 * Math.Cos(4.0 * latRad);
            double mPerDegLon = Math.Max(1000.0, 111412.84 * Math.Cos(latRad) - 93.5 * Math.Cos(3.0 * latRad));

            sec.OffsetNorthMeters = (sec.ShooterLat - centerLat) * mPerDegLat;
            sec.OffsetEastMeters = (sec.ShooterLon - centerLon) * mPerDegLon;
        }

        // ====================================================================
        // ODCZYTYWANIE WYRYSOWANYCH SEKTOROW OGNIA PRZY IMPORCIE .KMZ / .KML
        // ====================================================================
        public static string ExtractAndImportRangeSectorsFromKml(string kmlContent)
        {
            if (string.IsNullOrEmpty(kmlContent)) return kmlContent;

            if (kmlContent.IndexOf("Sektory Ognia i Zasięg Replik", StringComparison.OrdinalIgnoreCase) < 0 &&
                kmlContent.IndexOf("fth_sector_", StringComparison.OrdinalIgnoreCase) < 0 &&
                kmlContent.IndexOf("Zasięg płaski:", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return kmlContent;
            }

            try
            {
                int importedCount = ParseAndRegisterRangeSectorsFromKmlText(kmlContent, centerMapIfStandalone: false);
                if (importedCount > 0)
                {
                    string cleanedKml = Regex.Replace(
                        kmlContent,
                        @"<Folder>\s*<name>\s*Sektory Ognia i Zasięg Replik\s*</name>[\s\S]*?</Folder>",
                        string.Empty,
                        RegexOptions.IgnoreCase);

                    bool hasOtherPlacemarks = cleanedKml.IndexOf("<Placemark", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!hasOtherPlacemarks)
                    {
                        CenterMapOnImportedSectorsIfNeeded();
                    }

                    SchedulePostImportRangeSectorRefresh();
                    return hasOtherPlacemarks ? cleanedKml : kmlContent;
                }
            }
            catch { }

            return kmlContent;
        }

        private static void SchedulePostImportRangeSectorRefresh()
        {
            if (_interactiveCanvas == null) return;
            _interactiveCanvas.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            {
                foreach (var sec in _weaponRangeSectors)
                {
                    if (sec.HasGeoAnchor)
                        SyncSectorOffsetsFromGeoAnchor(sec);
                }
                RebuildRangeSectorsListUi();
                PopulateRangeSectorEditorFromSelected();
                RefreshRangeSectorsNow(forceFullRebuild: true);
            }));
        }

        private static void CenterMapOnImportedSectorsIfNeeded()
        {
            var anchored = _weaponRangeSectors.Where(s => s.HasGeoAnchor).ToList();
            if (anchored.Count == 0) return;

            double avgLat = anchored.Average(s => s.ShooterLat);
            double avgLon = anchored.Average(s => s.ShooterLon);

            if (Math.Abs(_centerLat - avgLat) > 0.018 || Math.Abs(_centerLon - avgLon) > 0.025)
            {
                _centerLat = avgLat;
                _centerLon = avgLon;
            }

            foreach (var sec in anchored)
            {
                SyncSectorOffsetsFromGeoAnchor(sec);
            }
        }

        private static int ParseAndRegisterRangeSectorsFromKmlText(string kmlContent, bool centerMapIfStandalone)
        {
            if (string.IsNullOrEmpty(kmlContent)) return 0;

            string scopeText = kmlContent;
            Match mFolder = Regex.Match(
                kmlContent,
                @"<Folder>\s*<name>\s*Sektory Ognia i Zasięg Replik\s*</name>([\s\S]*?)</Folder>",
                RegexOptions.IgnoreCase);
            if (mFolder.Success)
            {
                scopeText = mFolder.Groups[1].Value;
            }

            var styleColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var styleOpacities = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            foreach (Match sm in Regex.Matches(scopeText, @"<Style\s+id=""([^""]+)""\s*>([\s\S]*?)</Style>", RegexOptions.IgnoreCase))
            {
                string styleId = sm.Groups[1].Value.Trim();
                string styleBody = sm.Groups[2].Value;

                Match polyColorMatch = Regex.Match(styleBody, @"<PolyStyle>[\s\S]*?<color>\s*([0-9a-fA-F]{8})\s*</color>", RegexOptions.IgnoreCase);
                if (polyColorMatch.Success)
                {
                    string aabbggrr = polyColorMatch.Groups[1].Value;
                    int alphaInt = Convert.ToInt32(aabbggrr.Substring(0, 2), 16);
                    string bb = aabbggrr.Substring(2, 2);
                    string gg = aabbggrr.Substring(4, 2);
                    string rr = aabbggrr.Substring(6, 2);

                    styleColors[styleId] = "#" + rr.ToUpperInvariant() + gg.ToUpperInvariant() + bb.ToUpperInvariant();
                    styleOpacities[styleId] = Math.Clamp(Math.Round(alphaInt / 255.0, 2), 0.05, 0.65);
                }
                else
                {
                    Match lineColorMatch = Regex.Match(styleBody, @"<LineStyle>[\s\S]*?<color>\s*([0-9a-fA-F]{8})\s*</color>", RegexOptions.IgnoreCase);
                    if (lineColorMatch.Success)
                    {
                        string aabbggrr = lineColorMatch.Groups[1].Value;
                        string bb = aabbggrr.Substring(2, 2);
                        string gg = aabbggrr.Substring(4, 2);
                        string rr = aabbggrr.Substring(6, 2);
                        styleColors[styleId] = "#" + rr.ToUpperInvariant() + gg.ToUpperInvariant() + bb.ToUpperInvariant();
                    }
                }
            }

            var pointCoordsByStyle = new Dictionary<string, (double lat, double lon)>(StringComparer.OrdinalIgnoreCase);
            var pointCoordsByName = new Dictionary<string, (double lat, double lon)>(StringComparer.OrdinalIgnoreCase);

            foreach (Match pm in Regex.Matches(scopeText, @"<Placemark>([\s\S]*?)</Placemark>", RegexOptions.IgnoreCase))
            {
                string pBody = pm.Groups[1].Value;
                if (pBody.IndexOf("<Point>", StringComparison.OrdinalIgnoreCase) < 0) continue;

                Match coordMatch = Regex.Match(pBody, @"<Point>[\s\S]*?<coordinates>\s*([-\d.]+)\s*,\s*([-\d.]+)", RegexOptions.IgnoreCase);
                if (!coordMatch.Success) continue;

                if (double.TryParse(coordMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double ptLon) &&
                    double.TryParse(coordMatch.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double ptLat))
                {
                    Match stMatch = Regex.Match(pBody, @"<styleUrl>\s*#?([^<\s]+)\s*</styleUrl>", RegexOptions.IgnoreCase);
                    if (stMatch.Success)
                    {
                        pointCoordsByStyle[stMatch.Groups[1].Value.Trim()] = (ptLat, ptLon);
                    }

                    Match nmMatch = Regex.Match(pBody, @"<name>\s*Stanowisko:\s*(.*?)\s*</name>", RegexOptions.IgnoreCase);
                    if (nmMatch.Success)
                    {
                        pointCoordsByName[UnescapeKmlXml(nmMatch.Groups[1].Value.Trim())] = (ptLat, ptLon);
                    }
                }
            }

            int addedOrUpdated = 0;

            foreach (Match pm in Regex.Matches(scopeText, @"<Placemark>([\s\S]*?)</Placemark>", RegexOptions.IgnoreCase))
            {
                string pBody = pm.Groups[1].Value;
                if (pBody.IndexOf("<Polygon>", StringComparison.OrdinalIgnoreCase) < 0) continue;

                string desc = "";
                Match descMatch = Regex.Match(pBody, @"<description>([\s\S]*?)</description>", RegexOptions.IgnoreCase);
                if (descMatch.Success) desc = UnescapeKmlXml(descMatch.Groups[1].Value.Trim());

                string styleId = "";
                Match stMatch = Regex.Match(pBody, @"<styleUrl>\s*#?([^<\s]+)\s*</styleUrl>", RegexOptions.IgnoreCase);
                if (stMatch.Success) styleId = stMatch.Groups[1].Value.Trim();

                bool isRangeSector = styleId.StartsWith("fth_sector_", StringComparison.OrdinalIgnoreCase) ||
                                     desc.IndexOf("Zasięg płaski:", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isRangeSector) continue;

                string secId = styleId.StartsWith("fth_sector_", StringComparison.OrdinalIgnoreCase)
                    ? styleId.Substring("fth_sector_".Length)
                    : Guid.NewGuid().ToString("N");

                string rawName = "Stanowisko";
                Match nameTagMatch = Regex.Match(pBody, @"<name>([\s\S]*?)</name>", RegexOptions.IgnoreCase);
                if (nameTagMatch.Success)
                {
                    rawName = UnescapeKmlXml(nameTagMatch.Groups[1].Value.Trim());
                    int bracketIdx = rawName.IndexOf(" [", StringComparison.Ordinal);
                    if (bracketIdx > 0) rawName = rawName.Substring(0, bracketIdx).Trim();
                }

                string weaponPreset = "Karabinek Szturmowy (60 m)";
                double baseRadiusM = 60.0;
                double deltaHM = 0.0;
                double azimuthDeg = 45.0;
                double spreadDeg = 75.0;

                if (!string.IsNullOrEmpty(desc))
                {
                    string[] parts = desc.Split('|');
                    foreach (string rawPart in parts)
                    {
                        string part = rawPart.Trim();
                        if (part.StartsWith("Stanowisko:", StringComparison.OrdinalIgnoreCase))
                        {
                            string v = part.Substring("Stanowisko:".Length).Trim();
                            if (!string.IsNullOrEmpty(v)) rawName = v;
                        }
                        else if (part.StartsWith("Klasa:", StringComparison.OrdinalIgnoreCase))
                        {
                            string v = part.Substring("Klasa:".Length).Trim();
                            if (!string.IsNullOrEmpty(v)) weaponPreset = v;
                        }
                        else if (part.StartsWith("Zasięg płaski:", StringComparison.OrdinalIgnoreCase))
                        {
                            Match mNum = Regex.Match(part, @"([-\d.,]+)");
                            if (mNum.Success && double.TryParse(mNum.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double rVal))
                                baseRadiusM = Math.Clamp(rVal, 5.0, 500.0);
                        }
                        else if (part.IndexOf("dH:", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            int dhIdx = part.IndexOf("dH:", StringComparison.OrdinalIgnoreCase);
                            string afterDh = part.Substring(dhIdx + 3);
                            Match mNum = Regex.Match(afterDh, @"([+-]?\d+(?:[.,]\d+)?)");
                            if (mNum.Success && double.TryParse(mNum.Groups[1].Value.Replace("+", "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double dhVal))
                                deltaHM = Math.Clamp(dhVal, -150.0, 150.0);
                        }
                        else if (part.StartsWith("Azymut:", StringComparison.OrdinalIgnoreCase))
                        {
                            Match mNum = Regex.Match(part, @"([-\d.,]+)");
                            if (mNum.Success && double.TryParse(mNum.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double azVal))
                                azimuthDeg = ((azVal % 360.0) + 360.0) % 360.0;
                        }
                        else if (part.StartsWith("Kąt sektora:", StringComparison.OrdinalIgnoreCase))
                        {
                            Match mNum = Regex.Match(part, @"([-\d.,]+)");
                            if (mNum.Success && double.TryParse(mNum.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double spVal))
                                spreadDeg = Math.Clamp(spVal, 5.0, 360.0);
                        }
                    }
                }

                double shooterLat = _centerLat;
                double shooterLon = _centerLon;
                bool foundCoords = false;

                if (!string.IsNullOrEmpty(styleId) && pointCoordsByStyle.TryGetValue(styleId, out var ptByStyle))
                {
                    shooterLat = ptByStyle.lat;
                    shooterLon = ptByStyle.lon;
                    foundCoords = true;
                }
                else if (pointCoordsByName.TryGetValue(rawName, out var ptByName))
                {
                    shooterLat = ptByName.lat;
                    shooterLon = ptByName.lon;
                    foundCoords = true;
                }
                else
                {
                    Match polyCoordsMatch = Regex.Match(pBody, @"<coordinates>([\s\S]*?)</coordinates>", RegexOptions.IgnoreCase);
                    if (polyCoordsMatch.Success)
                    {
                        var coordPairs = new List<(double lon, double lat)>();
                        foreach (Match cm in Regex.Matches(polyCoordsMatch.Groups[1].Value, @"([-\d.]+)\s*,\s*([-\d.]+)"))
                        {
                            if (double.TryParse(cm.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double cLon) &&
                                double.TryParse(cm.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double cLat))
                            {
                                coordPairs.Add((cLon, cLat));
                            }
                        }

                        if (coordPairs.Count > 0)
                        {
                            if (spreadDeg < 359.5)
                            {
                                shooterLon = coordPairs[0].lon;
                                shooterLat = coordPairs[0].lat;
                            }
                            else
                            {
                                shooterLon = coordPairs.Average(p => p.lon);
                                shooterLat = coordPairs.Average(p => p.lat);
                            }
                            foundCoords = true;
                        }
                    }
                }

                string colorHex = (!string.IsNullOrEmpty(styleId) && styleColors.TryGetValue(styleId, out string sc)) ? sc : "#1FE0C5";
                double fillOpacity = (!string.IsNullOrEmpty(styleId) && styleOpacities.TryGetValue(styleId, out double so)) ? so : 0.24;

                var existing = _weaponRangeSectors.FirstOrDefault(x => x.Id == secId);
                if (existing == null)
                {
                    existing = new TacticalRangeSectorItem { Id = secId };
                    _weaponRangeSectors.Add(existing);
                }

                existing.Name = rawName;
                existing.WeaponPreset = weaponPreset;
                existing.BaseRadiusMeters = baseRadiusM;
                existing.ElevationDeltaMeters = deltaHM;
                existing.AzimuthDeg = azimuthDeg;
                existing.SpreadAngleDeg = spreadDeg;
                existing.ColorHex = colorHex;
                existing.FillOpacity = fillOpacity;
                existing.IsVisible = true;

                if (foundCoords)
                {
                    existing.ShooterLat = shooterLat;
                    existing.ShooterLon = shooterLon;
                    existing.HasGeoAnchor = true;
                    SyncSectorOffsetsFromGeoAnchor(existing);
                }

                _selectedRangeSector = existing;
                addedOrUpdated++;
            }

            if (addedOrUpdated > 0)
            {
                if (centerMapIfStandalone)
                {
                    CenterMapOnImportedSectorsIfNeeded();
                }
                RebuildRangeSectorsListUi();
                PopulateRangeSectorEditorFromSelected();
                RefreshRangeSectorsNow(forceFullRebuild: true);
            }

            return addedOrUpdated;
        }

        private static void ImportStandaloneRangeSectorsKmzDialog()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Wczytaj Sektory Ognia z pliku .KMZ lub .KML",
                Filter = "Paczki i pliki mapowe ATAK (*.kmz;*.kml)|*.kmz;*.kml|Pliki KMZ (*.kmz)|*.kmz|Pliki KML (*.kml)|*.kml"
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                string kmlContent = ReadKmlTextFromKmzOrKmlFile(dlg.FileName);
                int count = ParseAndRegisterRangeSectorsFromKmlText(kmlContent, centerMapIfStandalone: true);
                if (count > 0)
                {
                    MessageBox.Show(
                        $"Pomyślnie wczytano i odtworzono na mapie {count} sektor(ów) ognia wraz z parametrami zasięgu, kąta oraz przewagi wysokości ΔH.",
                        "Import Sektorów Ognia z .KMZ / .KML",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(
                        "W wybranym pliku nie znaleziono zapisanych sektorów ognia (szukano folderu 'Sektory Ognia i Zasięg Replik').",
                        "Import Sektorów Ognia",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Błąd podczas wczytywania pliku .KMZ / .KML:\n" + ex.Message, "Błąd importu", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string ReadKmlTextFromKmzOrKmlFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
                return string.Empty;

            if (filePath.EndsWith(".kmz", StringComparison.OrdinalIgnoreCase))
            {
                using (var fs = new System.IO.FileStream(filePath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                using (var za = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Read))
                {
                    var kmlEntry = za.Entries.FirstOrDefault(e => e.FullName.EndsWith(".kml", StringComparison.OrdinalIgnoreCase));
                    if (kmlEntry != null)
                    {
                        using (var sr = new System.IO.StreamReader(kmlEntry.Open(), System.Text.Encoding.UTF8))
                        {
                            return sr.ReadToEnd();
                        }
                    }
                }
            }

            return System.IO.File.ReadAllText(filePath, System.Text.Encoding.UTF8);
        }

        private static string UnescapeKmlXml(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("&lt;", "<")
                    .Replace("&gt;", ">")
                    .Replace("&quot;", "\"")
                    .Replace("&apos;", "'")
                    .Replace("&amp;", "&");
        }
    }
}
