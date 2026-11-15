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
                ToolTip = "Dodaj na środek mapy interaktywny zasięg repliki / sektor ostrzału ('kawałek pizzy') z przeliczaniem przewagi wysokości ΔH"
            };
            btnAddSector.Click += (s, e) => AddNewWeaponRangeSector();
            legContainer.Children.Add(btnAddSector);
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

            _weaponRangeSectors.Add(item);
            _selectedRangeSector = item;
            RebuildRangeSectorsListUi();
            PopulateRangeSectorEditorFromSelected();
            RefreshRangeSectorsNow();
        }

        private static void RefreshRangeSectorsNow()
        {
            var (cw, ch) = GetExactCanvasSize();
            if (cw > 20 && ch > 20)
            {
                RenderAllWeaponRangeSectorsOnSheet(cw, ch);
            }
            UpdateBallisticSummaryBanner();
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

            sp.Children.Add(new TextBlock
            {
                Text = "Przesuwaj środek sektora myszą po mapie, a uchwytem na łuku obracaj kierunek ostrzału.",
                Foreground = TacticalConfigPanel.BrushMutedText,
                FontSize = 9.0,
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
                RebuildRangeSectorsListUi();
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
                    RebuildRangeSectorsListUi();
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
                RebuildRangeSectorsListUi();
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
                    RebuildRangeSectorsListUi();
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
                RebuildRangeSectorsListUi();
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
                    RebuildRangeSectorsListUi();
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

                    RebuildRangeSectorsListUi();
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
                chk.Checked += (s, e) => { sec.IsVisible = true; RefreshRangeSectorsNow(); };
                chk.Unchecked += (s, e) => { sec.IsVisible = false; RefreshRangeSectorsNow(); };
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
                    RebuildRangeSectorsListUi();
                    PopulateRangeSectorEditorFromSelected();
                    RefreshRangeSectorsNow();
                };
                Grid.SetColumn(btnDel, 3);
                g.Children.Add(btnDel);

                rowBorder.MouseLeftButtonDown += (s, e) =>
                {
                    _selectedRangeSector = sec;
                    RebuildRangeSectorsListUi();
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
        // RENDERER STREF RAZENIA I SEKTOROW OGNIA ("PIZZA") NA ARKUSZU MAPY
        // ====================================================================
        public static void RenderAllWeaponRangeSectorsOnSheet(double w, double h)
        {
            if (_interactiveCanvas == null || w <= 20 || h <= 20 || _isRenderingRangeSectors) return;

            _isRenderingRangeSectors = true;
            try
            {
                var toRemove = new List<UIElement>();
                foreach (UIElement el in _interactiveCanvas.Children)
                {
                    if (el is FrameworkElement fe && fe.Tag is string t && t == "FTH_RANGE_SECTOR_EL")
                        toRemove.Add(el);
                }
                foreach (var rem in toRemove)
                    _interactiveCanvas.Children.Remove(rem);

                if (_weaponRangeSectors.Count == 0) return;

                double mpp = GetExactMetersPerPixel();
                if (mpp <= 0.0001 || double.IsNaN(mpp) || double.IsInfinity(mpp)) mpp = 1.0;

                double halfW = w / 2.0;
                double halfH = h / 2.0;

                foreach (var sec in _weaponRangeSectors)
                {
                    if (!sec.IsVisible) continue;

                    double cx = halfW + (sec.OffsetEastMeters / mpp);
                    double cy = halfH - (sec.OffsetNorthMeters / mpp);

                    double baseRadiusPx = Math.Max(6.0, sec.BaseRadiusMeters / mpp);
                    double effRadiusPx = Math.Max(6.0, sec.GetEffectiveRadiusMeters() / mpp);

                    Color mainColor = Colors.Cyan;
                    try { mainColor = (Color)ColorConverter.ConvertFromString(sec.ColorHex); } catch { }

                    byte fillAlpha = (byte)Math.Clamp((int)Math.Round(sec.FillOpacity * 255.0), 15, 200);
                    Brush fillBrush = new SolidColorBrush(Color.FromArgb(fillAlpha, mainColor.R, mainColor.G, mainColor.B));
                    Brush strokeBrush = new SolidColorBrush(mainColor);
                    Brush baseRefBrush = new SolidColorBrush(Color.FromArgb(190, 255, 255, 255));

                    bool isFullCircle = sec.SpreadAngleDeg >= 359.5;
                    double azRad = sec.AzimuthDeg * Math.PI / 180.0;

                    Path sectorPath = new Path
                    {
                        Fill = fillBrush,
                        Stroke = strokeBrush,
                        StrokeThickness = ReferenceEquals(sec, _selectedRangeSector) ? 2.2 : 1.6,
                        Data = BuildSectorOrCircleGeometry(cx, cy, effRadiusPx, sec.AzimuthDeg, sec.SpreadAngleDeg, isClosedSlice: true),
                        Tag = "FTH_RANGE_SECTOR_EL",
                        Cursor = Cursors.SizeAll,
                        ToolTip = $"{sec.Name} — Przeciągnij myszą, aby przesunąć stanowisko"
                    };
                    AttachDragMoveSectorBehavior(sectorPath, sec, mpp);
                    _interactiveCanvas.Children.Add(sectorPath);

                    if (Math.Abs(sec.ElevationDeltaMeters) >= 0.5)
                    {
                        Brush deltaArcBrush = sec.ElevationDeltaMeters > 0
                            ? new SolidColorBrush(Color.FromArgb(220, 46, 204, 113))
                            : new SolidColorBrush(Color.FromArgb(220, 250, 208, 44));

                        Path baseArcPath = new Path
                        {
                            Stroke = baseRefBrush,
                            StrokeThickness = 1.2,
                            StrokeDashArray = new DoubleCollection { 4, 3 },
                            Data = BuildSectorOrCircleGeometry(cx, cy, baseRadiusPx, sec.AzimuthDeg, sec.SpreadAngleDeg, isClosedSlice: false),
                            IsHitTestVisible = false,
                            Tag = "FTH_RANGE_SECTOR_EL"
                        };
                        _interactiveCanvas.Children.Add(baseArcPath);

                        Path effArcHighlight = new Path
                        {
                            Stroke = deltaArcBrush,
                            StrokeThickness = 2.6,
                            Data = BuildSectorOrCircleGeometry(cx, cy, effRadiusPx, sec.AzimuthDeg, sec.SpreadAngleDeg, isClosedSlice: false),
                            IsHitTestVisible = false,
                            Tag = "FTH_RANGE_SECTOR_EL"
                        };
                        _interactiveCanvas.Children.Add(effArcHighlight);
                    }

                    for (double ringM = 25.0; ringM < sec.GetEffectiveRadiusMeters() - 5.0; ringM += 25.0)
                    {
                        double ringPx = ringM / mpp;
                        var ringArc = new Path
                        {
                            Stroke = new SolidColorBrush(Color.FromArgb(85, mainColor.R, mainColor.G, mainColor.B)),
                            StrokeThickness = 0.8,
                            StrokeDashArray = new DoubleCollection { 2, 3 },
                            Data = BuildSectorOrCircleGeometry(cx, cy, ringPx, sec.AzimuthDeg, sec.SpreadAngleDeg, isClosedSlice: false),
                            IsHitTestVisible = false,
                            Tag = "FTH_RANGE_SECTOR_EL"
                        };
                        _interactiveCanvas.Children.Add(ringArc);
                    }

                    double tipX = cx + effRadiusPx * Math.Sin(azRad);
                    double tipY = cy - effRadiusPx * Math.Cos(azRad);

                    var aimLine = new Line
                    {
                        X1 = cx, Y1 = cy, X2 = tipX, Y2 = tipY,
                        Stroke = strokeBrush,
                        StrokeThickness = 1.2,
                        StrokeDashArray = new DoubleCollection { 3, 2 },
                        IsHitTestVisible = false,
                        Tag = "FTH_RANGE_SECTOR_EL"
                    };
                    _interactiveCanvas.Children.Add(aimLine);

                    var aimHandle = new Border
                    {
                        Width = 12,
                        Height = 12,
                        CornerRadius = new CornerRadius(6),
                        Background = new SolidColorBrush(Color.FromRgb(6, 14, 18)),
                        BorderBrush = strokeBrush,
                        BorderThickness = new Thickness(2),
                        Cursor = Cursors.Hand,
                        Tag = "FTH_RANGE_SECTOR_EL",
                        ToolTip = "Przeciągnij ten punkt, aby obrócić kierunek ostrzału (azymut) bezpośrednio na mapie"
                    };
                    Canvas.SetLeft(aimHandle, tipX - 6);
                    Canvas.SetTop(aimHandle, tipY - 6);
                    AttachDragRotateAzimuthBehavior(aimHandle, sec, cx, cy);
                    _interactiveCanvas.Children.Add(aimHandle);

                    var originPin = new Border
                    {
                        Width = 14,
                        Height = 14,
                        CornerRadius = new CornerRadius(7),
                        Background = strokeBrush,
                        BorderBrush = new SolidColorBrush(Color.FromRgb(6, 14, 18)),
                        BorderThickness = new Thickness(2),
                        Cursor = Cursors.SizeAll,
                        Tag = "FTH_RANGE_SECTOR_EL",
                        ToolTip = $"{sec.Name} — Przeciągnij, aby zmienić pozycję stanowiska"
                    };
                    Canvas.SetLeft(originPin, cx - 7);
                    Canvas.SetTop(originPin, cy - 7);
                    AttachDragMoveSectorBehavior(originPin, sec, mpp);
                    _interactiveCanvas.Children.Add(originPin);

                    double effR = sec.GetEffectiveRadiusMeters();
                    string dhBadgeText = Math.Abs(sec.ElevationDeltaMeters) >= 0.5
                        ? $" ➔ {effR:0}m (ΔH:{sec.ElevationDeltaMeters:+0.#;-0.#}m / {sec.GetRangeBonusPercent():+0;-0}%)"
                        : "";
                    string angleBadgeText = isFullCircle ? "360°" : $"{sec.SpreadAngleDeg:0}°";

                    var labelBadge = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(225, 6, 14, 18)),
                        BorderBrush = strokeBrush,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(5, 1.5, 5, 1.5),
                        Cursor = Cursors.SizeAll,
                        Tag = "FTH_RANGE_SECTOR_EL",
                        Child = new TextBlock
                        {
                            Text = $"🎯 {sec.Name}: {sec.BaseRadiusMeters:0}m{dhBadgeText} [{angleBadgeText}]",
                            Foreground = Brushes.White,
                            FontSize = 8.5,
                            FontWeight = FontWeights.Bold,
                            FontFamily = new FontFamily("Consolas")
                        }
                    };
                    Canvas.SetLeft(labelBadge, cx + 10);
                    Canvas.SetTop(labelBadge, cy - 10);
                    AttachDragMoveSectorBehavior(labelBadge, sec, mpp);
                    _interactiveCanvas.Children.Add(labelBadge);
                }
            }
            finally
            {
                _isRenderingRangeSectors = false;
            }
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

        private static void AttachDragMoveSectorBehavior(FrameworkElement el, TacticalRangeSectorItem sec, double mpp)
        {
            bool isDragging = false;
            Point lastMousePos = new Point();

            el.MouseLeftButtonDown += (s, e) =>
            {
                if (_interactiveCanvas == null) return;
                _selectedRangeSector = sec;
                RebuildRangeSectorsListUi();
                PopulateRangeSectorEditorFromSelected();

                isDragging = true;
                lastMousePos = e.GetPosition(_interactiveCanvas);
                el.CaptureMouse();
                e.Handled = true;
            };

            el.MouseMove += (s, e) =>
            {
                if (!isDragging || _interactiveCanvas == null) return;
                Point curPos = e.GetPosition(_interactiveCanvas);
                double dxPx = curPos.X - lastMousePos.X;
                double dyPx = curPos.Y - lastMousePos.Y;
                lastMousePos = curPos;

                sec.OffsetEastMeters += dxPx * mpp;
                sec.OffsetNorthMeters -= dyPx * mpp;

                var (cw, ch) = GetExactCanvasSize();
                RenderAllWeaponRangeSectorsOnSheet(cw, ch);
                e.Handled = true;
            };

            el.MouseLeftButtonUp += (s, e) =>
            {
                if (!isDragging) return;
                isDragging = false;
                el.ReleaseMouseCapture();
                e.Handled = true;
            };
        }

        private static void AttachDragRotateAzimuthBehavior(FrameworkElement handleEl, TacticalRangeSectorItem sec, double cx, double cy)
        {
            bool isRotating = false;

            handleEl.MouseLeftButtonDown += (s, e) =>
            {
                if (_interactiveCanvas == null) return;
                _selectedRangeSector = sec;
                RebuildRangeSectorsListUi();
                PopulateRangeSectorEditorFromSelected();

                isRotating = true;
                handleEl.CaptureMouse();
                e.Handled = true;
            };

            handleEl.MouseMove += (s, e) =>
            {
                if (!isRotating || _interactiveCanvas == null) return;
                Point p = e.GetPosition(_interactiveCanvas);
                double dx = p.X - cx;
                double dy = cy - p.Y;
                if (Math.Abs(dx) > 1.0 || Math.Abs(dy) > 1.0)
                {
                    double angleDeg = Math.Atan2(dx, dy) * 180.0 / Math.PI;
                    if (angleDeg < 0) angleDeg += 360.0;
                    sec.AzimuthDeg = Math.Round(angleDeg);

                    _suppressRangeSectorUiEvents = true;
                    if (_rsAzimuthSlider != null && ReferenceEquals(_selectedRangeSector, sec))
                        _rsAzimuthSlider.Value = sec.AzimuthDeg;
                    if (_rsAzimuthValText != null && ReferenceEquals(_selectedRangeSector, sec))
                        _rsAzimuthValText.Text = FormatAzimuthWithCardinal(sec.AzimuthDeg);
                    _suppressRangeSectorUiEvents = false;

                    var (cw, ch) = GetExactCanvasSize();
                    RenderAllWeaponRangeSectorsOnSheet(cw, ch);
                }
                e.Handled = true;
            };

            handleEl.MouseLeftButtonUp += (s, e) =>
            {
                if (!isRotating) return;
                isRotating = false;
                handleEl.ReleaseMouseCapture();
                e.Handled = true;
            };
        }
    }
}
