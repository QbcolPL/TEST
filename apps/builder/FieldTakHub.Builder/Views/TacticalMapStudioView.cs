#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using FieldTakHub.Builder.Core;

namespace FieldTakHub.Builder.Views
{
    /// <summary>
    /// Moduł 1: Generator Map Taktycznych 1:1 (Tactical Map Studio) napisany od zera.
    /// Obsługuje hybrydę Orto + Turbo LiDAR (ISOK), prawdziwą skalę 1:1 na A4/A3/mapnikach,
    /// siatkę wojskową MGRS z podziałem kwadratów 100 m (Keypad 1-9 / Ćwiartki A-D),
    /// rysowanie granicy gry (AO), stawianie punktów POI i obracanych znaków NATO APP-6 oraz Eksport 4-w-1.
    /// </summary>
    public partial class TacticalMapStudioView : UserControl
    {
        private readonly ScrollViewer _rootScroll;
        private readonly StackPanel _mainStack;

        private Canvas _mapCanvas;
        private Canvas _lidarReliefLayer;
        private Canvas _gridAndVectorsLayer;
        private TextBlock _aoStatusBadge;
        private TextBlock _liveScaleCoverageLabel;
        private StackPanel _poiListContainer;

        private bool _isDrawingAoMode = true;
        private string _newPoiCategory = "INFIL";
        private string _newPoiName = "INFIL-1";
        private double _newPoiRotationDeg = 0;

        public TacticalMapStudioView()
        {
            _mainStack = new StackPanel { Margin = new Thickness(24) };
            _rootScroll = new ScrollViewer
            {
                Content = _mainStack,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = TacticalUiFactory.BgMainBrush
            };
            Content = _rootScroll;
            RebuildView();
        }

        partial void RebuildView()
        {
            _mainStack.Children.Clear();
            var cfg = ProjectStore.Instance.MapConfig;

            // 1. KARTA GŁÓWNA: NAGŁÓWEK + PASEK EKSPORTU 4-W-1
            var headerCard = TacticalUiFactory.CreateCardBorder(TacticalUiFactory.AccentCyanBrush);
            var headerStack = new StackPanel();
            headerStack.Children.Add(TacticalUiFactory.CreateSectionHeader(LocalizationManager.Get("MAP_HEADER"), TacticalUiFactory.AccentCyanBrush, 15));

            var exportWrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 2) };

            var btnExportA4 = TacticalUiFactory.CreateActionButton(
                LocalizationManager.Get("MAP_EXPORT_A4"),
                TacticalUiFactory.AccentCyanBrush,
                new SolidColorBrush(Color.FromRgb(5, 16, 20)));
            btnExportA4.Click += (_, __) =>
            {
                string path = TacticalMapExporters.ExportTrueScalePrintableHtml(cfg, true);
                MessageBox.Show(
                    LocalizationManager.T(
                        "✅ Wygenerowano skalibrowany arkusz 1:" + cfg.ScaleDenominator + " z warstwą LiDAR ISOK oraz siatką " + cfg.CoordinateSystem + ":\n" + path,
                        "✅ Generated true-scale 1:" + cfg.ScaleDenominator + " sheet with LiDAR ISOK & " + cfg.CoordinateSystem + " grid:\n" + path),
                    "1:1 Tactical Map Studio", MessageBoxButton.OK, MessageBoxImage.Information);
            };

            var btnExportKmz = TacticalUiFactory.CreateActionButton(
                LocalizationManager.Get("MAP_EXPORT_KMZ"),
                new SolidColorBrush(Color.FromRgb(22, 110, 65)),
                Brushes.White,
                TacticalUiFactory.AccentGreenBrush);
            btnExportKmz.Click += (_, __) =>
            {
                string path = TacticalMapExporters.ExportKmzPackageForAtak(cfg);
                MessageBox.Show(
                    LocalizationManager.T("✅ Zapisano paczkę taktyczną .KMZ dla ATAK:\n" + path, "✅ Saved .KMZ tactical package for ATAK:\n" + path),
                    "ATAK .KMZ", MessageBoxButton.OK, MessageBoxImage.Information);
            };

            var btnExportGpx = TacticalUiFactory.CreateActionButton(
                LocalizationManager.Get("MAP_EXPORT_GPX"),
                new SolidColorBrush(Color.FromRgb(42, 34, 12)),
                TacticalUiFactory.AccentGoldBrush,
                TacticalUiFactory.AccentGoldBrush);
            btnExportGpx.Click += (_, __) =>
            {
                string path = TacticalMapExporters.ExportGpxForGarminSuunto(cfg);
                MessageBox.Show(
                    LocalizationManager.T("✅ Zapisano plik nawigacyjny .GPX na zegarki Garmin / Suunto:\n" + path, "✅ Saved .GPX navigation file for Garmin / Suunto watches:\n" + path),
                    "Garmin / Suunto .GPX", MessageBoxButton.OK, MessageBoxImage.Information);
            };

            var btnExportMbtiles = TacticalUiFactory.CreateActionButton(
                LocalizationManager.Get("MAP_EXPORT_MBTILES"),
                new SolidColorBrush(Color.FromRgb(14, 42, 54)),
                TacticalUiFactory.AccentCyanBrush,
                TacticalUiFactory.AccentCyanBrush);
            btnExportMbtiles.Click += (_, __) =>
            {
                string path = TacticalMapExporters.ExportOfflineMbtilesContainer(cfg);
                MessageBox.Show(
                    LocalizationManager.T("✅ Wygenerowano mapę offline .mbtiles (z wypalonym cieniowaniem LiDAR " + cfg.LidarOpacityPercent + "%):\n" + path, "✅ Generated offline .mbtiles map (with burned-in LiDAR " + cfg.LidarOpacityPercent + "%):\n" + path),
                    "Offline .mbtiles", MessageBoxButton.OK, MessageBoxImage.Information);
            };

            exportWrap.Children.Add(btnExportA4);
            exportWrap.Children.Add(btnExportKmz);
            exportWrap.Children.Add(btnExportGpx);
            exportWrap.Children.Add(btnExportMbtiles);
            headerStack.Children.Add(exportWrap);
            headerCard.Child = headerStack;
            _mainStack.Children.Add(headerCard);

            // 2. KARTA KONFIGURACJI SKALI 1:1, SIATKI MGRS ORAZ SUWAKA TURBO LiDAR (ISOK)
            var cfgCard = TacticalUiFactory.CreateCardBorder();
            var cfgStack = new StackPanel();

            var row1Grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            row1Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
            row1Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            row1Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
            row1Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            row1Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
            row1Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            row1Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Tytuł arkusza mapy
            var spTitle = new StackPanel();
            spTitle.Children.Add(TacticalUiFactory.CreateFieldLabel(LocalizationManager.T("Kryptonim arkusza mapy:", "Map Sheet Title:")));
            var titleBox = TacticalUiFactory.CreateTextBox(cfg.MapTitle);
            titleBox.TextChanged += (_, __) => cfg.MapTitle = titleBox.Text;
            spTitle.Children.Add(titleBox);
            Grid.SetColumn(spTitle, 0); row1Grid.Children.Add(spTitle);

            // Współrzędna Lat środka arkusza
            var spLat = new StackPanel();
            spLat.Children.Add(TacticalUiFactory.CreateFieldLabel("Center Lat (°N):"));
            var latBox = TacticalUiFactory.CreateTextBox(cfg.ViewportCenterLat.ToString("F6", CultureInfo.InvariantCulture), false, 32, true);
            latBox.TextChanged += (_, __) =>
            {
                if (double.TryParse(latBox.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
                {
                    cfg.ViewportCenterLat = v;
                    RenderTacticalCanvas();
                }
            };
            spLat.Children.Add(latBox);
            Grid.SetColumn(spLat, 2); row1Grid.Children.Add(spLat);

            // Współrzędna Lon środka arkusza
            var spLon = new StackPanel();
            spLon.Children.Add(TacticalUiFactory.CreateFieldLabel("Center Lon (°E):"));
            var lonBox = TacticalUiFactory.CreateTextBox(cfg.ViewportCenterLon.ToString("F6", CultureInfo.InvariantCulture), false, 32, true);
            lonBox.TextChanged += (_, __) =>
            {
                if (double.TryParse(lonBox.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
                {
                    cfg.ViewportCenterLon = v;
                    RenderTacticalCanvas();
                }
            };
            spLon.Children.Add(lonBox);
            Grid.SetColumn(spLon, 4); row1Grid.Children.Add(spLon);

            // Szybki przycisk wyśrodkowania na Zbytków / Strumień (49°50'58.0"N 18°48'45.6"E)
            var spPreset = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
            var presetBtn = TacticalUiFactory.CreateActionButton(
                "📍 Zbytków / Strumień",
                new SolidColorBrush(Color.FromRgb(18, 48, 61)),
                TacticalUiFactory.AccentCyanBrush,
                TacticalUiFactory.AccentCyanBrush,
                32,
                new Thickness(0));
            presetBtn.Click += (_, __) =>
            {
                cfg.ViewportCenterLat = 49.849444;
                cfg.ViewportCenterLon = 18.812667;
                latBox.Text = "49.849444";
                lonBox.Text = "18.812667";
                RenderTacticalCanvas();
            };
            spPreset.Children.Add(presetBtn);
            Grid.SetColumn(spPreset, 6); row1Grid.Children.Add(spPreset);

            cfgStack.Children.Add(row1Grid);

            // Wiersz 2: Skala 1:1, Format arkusza, Układ współrzędnych (MGRS/UTM/DMS), Podział wewnętrzny 100m
            var row2Grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            row2Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row2Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            row2Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row2Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            row2Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row2Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            row2Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Skala 1:1
            var spScale = new StackPanel();
            spScale.Children.Add(TacticalUiFactory.CreateFieldLabel(LocalizationManager.Get("MAP_SCALE_LABEL")));
            var cbScale = new ComboBox { Height = 32 };
            cbScale.Items.Add("1:5000 (2 cm = 100 m)");
            cbScale.Items.Add("1:10000 (1 cm = 100 m)");
            cbScale.Items.Add("1:2500 (4 cm = 100 m)");
            cbScale.SelectedIndex = cfg.ScaleDenominator == 10000 ? 1 : (cfg.ScaleDenominator == 2500 ? 2 : 0);
            cbScale.SelectionChanged += (_, __) =>
            {
                string s = cbScale.SelectedItem?.ToString() ?? "";
                if (s.Contains("10000")) cfg.ScaleDenominator = 10000;
                else if (s.Contains("2500")) cfg.ScaleDenominator = 2500;
                else cfg.ScaleDenominator = 5000;
                RenderTacticalCanvas();
            };
            spScale.Children.Add(cbScale);
            Grid.SetColumn(spScale, 0); row2Grid.Children.Add(spScale);

            // Format arkusza
            var spFmt = new StackPanel();
            spFmt.Children.Add(TacticalUiFactory.CreateFieldLabel(LocalizationManager.T("Format arkusza do druku:", "Print Sheet Format:")));
            var cbFmt = new ComboBox { Height = 32 };
            cbFmt.Items.Add("A4_PORTRAIT (210×297 mm)");
            cbFmt.Items.Add("A4_LANDSCAPE (297×210 mm)");
            cbFmt.Items.Add("A3_LANDSCAPE (420×297 mm)");
            cbFmt.Items.Add("WRIST_MAPNIK (Mapnik 150×100 mm)");
            cbFmt.Items.Add("A5_NOTEBOOK (Zeszyt 148×210 mm)");
            cbFmt.SelectedIndex = 0;
            cbFmt.SelectionChanged += (_, __) =>
            {
                string s = cbFmt.SelectedItem?.ToString() ?? "A4_PORTRAIT";
                cfg.SheetFormat = s.Split(' ')[0];
                RenderTacticalCanvas();
            };
            spFmt.Children.Add(cbFmt);
            Grid.SetColumn(spFmt, 2); row2Grid.Children.Add(spFmt);

            // Układ współrzędnych
            var spCoord = new StackPanel();
            spCoord.Children.Add(TacticalUiFactory.CreateFieldLabel(LocalizationManager.Get("MAP_COORD_SYS")));
            var cbCoord = new ComboBox { Height = 32 };
            cbCoord.Items.Add("MGRS");
            cbCoord.Items.Add("UTM");
            cbCoord.Items.Add("DMS");
            cbCoord.Items.Add("WGS84");
            cbCoord.SelectedItem = cfg.CoordinateSystem ?? "MGRS";
            cbCoord.SelectionChanged += (_, __) =>
            {
                cfg.CoordinateSystem = cbCoord.SelectedItem?.ToString() ?? "MGRS";
                ProjectStore.Instance.SyncGeneratorsToOpord();
                RenderTacticalCanvas();
                RebuildPoiRows();
            };
            spCoord.Children.Add(cbCoord);
            Grid.SetColumn(spCoord, 4); row2Grid.Children.Add(spCoord);

            // Podział wewnętrzny kwadratów 100 m
            var spSubdiv = new StackPanel();
            spSubdiv.Children.Add(TacticalUiFactory.CreateFieldLabel(LocalizationManager.Get("MAP_SUBDIV_LABEL")));
            var cbSubdiv = new ComboBox { Height = 32 };
            cbSubdiv.Items.Add("KEYPAD_9 (Klawiatura 1-9 / 33m)");
            cbSubdiv.Items.Add("QUARTERS_4 (Ćwiartki A-D / 50m)");
            cbSubdiv.Items.Add("NONE (Tylko siatka 100m)");
            cbSubdiv.SelectedIndex = cfg.SubdivisionMode == "QUARTERS_4" ? 1 : (cfg.SubdivisionMode == "NONE" ? 2 : 0);
            cbSubdiv.SelectionChanged += (_, __) =>
            {
                string s = cbSubdiv.SelectedItem?.ToString() ?? "KEYPAD_9";
                cfg.SubdivisionMode = s.Split(' ')[0];
                RenderTacticalCanvas();
                RebuildPoiRows();
            };
            spSubdiv.Children.Add(cbSubdiv);
            Grid.SetColumn(spSubdiv, 6); row2Grid.Children.Add(spSubdiv);

            cfgStack.Children.Add(row2Grid);

            // Wiersz 3: Suwak Turbo LiDAR (ISOK) 0..100% oraz Checkbox maskowania poza AO
            var lidarGrid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            lidarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            lidarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            lidarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            lidarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            lidarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var lidarLbl = new TextBlock
            {
                Text = LocalizationManager.Get("MAP_LIDAR_SLIDER"),
                Foreground = TacticalUiFactory.AccentGoldBrush,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            Grid.SetColumn(lidarLbl, 0); lidarGrid.Children.Add(lidarLbl);

            var lidarValueText = new TextBlock
            {
                Text = cfg.LidarOpacityPercent + "%",
                Foreground = TacticalUiFactory.AccentCyanBrush,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Consolas"),
                Width = 48,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };

            var lidarSlider = new Slider
            {
                Minimum = 0,
                Maximum = 100,
                Value = cfg.LidarOpacityPercent,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand
            };
            lidarSlider.ValueChanged += (_, __) =>
            {
                cfg.LidarOpacityPercent = (int)Math.Round(lidarSlider.Value);
                lidarValueText.Text = cfg.LidarOpacityPercent + "%";
                if (_lidarReliefLayer != null)
                {
                    _lidarReliefLayer.Opacity = cfg.LidarOpacityPercent / 100.0;
                }
            };
            Grid.SetColumn(lidarSlider, 1); lidarGrid.Children.Add(lidarSlider);
            Grid.SetColumn(lidarValueText, 2); lidarGrid.Children.Add(lidarValueText);

            var maskChk = new CheckBox
            {
                Content = LocalizationManager.T("☑️ Maskuj teren poza granicą gry (AO)", "☑️ Mask terrain outside AO boundary"),
                IsChecked = cfg.MaskOutsideAo,
                Foreground = TacticalUiFactory.TextPrimaryBrush,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand
            };
            maskChk.Checked += (_, __) => { cfg.MaskOutsideAo = true; RenderTacticalCanvas(); };
            maskChk.Unchecked += (_, __) => { cfg.MaskOutsideAo = false; RenderTacticalCanvas(); };
            Grid.SetColumn(maskChk, 4); lidarGrid.Children.Add(maskChk);

            cfgStack.Children.Add(lidarGrid);
            cfgCard.Child = cfgStack;
            _mainStack.Children.Add(cfgCard);

            // 3. INTERAKTYWNE STUDIO MAPY 1:1 (PŁÓTNO TAKTYCZNE + PANEL NARZĘDZI AO / POI / NATO APP-6)
            var workspaceGrid = new Grid();
            workspaceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.55, GridUnitType.Star) });
            workspaceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            workspaceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });

            // Lewa kolumna: Interaktywne Płótno Mapy 1:1
            var mapCard = TacticalUiFactory.CreateCardBorder(TacticalUiFactory.AccentGoldBrush, new Thickness(0));
            var mapCardStack = new StackPanel();

            var statusTopGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            statusTopGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statusTopGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _liveScaleCoverageLabel = new TextBlock
            {
                Foreground = TacticalUiFactory.AccentCyanBrush,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_liveScaleCoverageLabel, 0); statusTopGrid.Children.Add(_liveScaleCoverageLabel);

            _aoStatusBadge = new TextBlock
            {
                FontWeight = FontWeights.Bold,
                FontSize = 11.5,
                Padding = new Thickness(8, 3, 8, 3),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_aoStatusBadge, 1); statusTopGrid.Children.Add(_aoStatusBadge);
            mapCardStack.Children.Add(statusTopGrid);

            var canvasBorder = new Border
            {
                BorderBrush = TacticalUiFactory.AccentCyanBrush,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(4),
                ClipToBounds = true,
                Height = 480,
                Background = new SolidColorBrush(Color.FromRgb(14, 30, 22))
            };

            _mapCanvas = new Canvas
            {
                Background = new SolidColorBrush(Color.FromRgb(16, 34, 24)),
                Cursor = Cursors.Cross
            };
            _lidarReliefLayer = new Canvas { IsHitTestVisible = false, Opacity = cfg.LidarOpacityPercent / 100.0 };
            _gridAndVectorsLayer = new Canvas { IsHitTestVisible = false };

            _mapCanvas.Children.Add(_lidarReliefLayer);
            _mapCanvas.Children.Add(_gridAndVectorsLayer);
            _mapCanvas.SizeChanged += (_, __) => RenderTacticalCanvas();
            _mapCanvas.MouseLeftButtonDown += OnMapCanvasLeftClick;

            canvasBorder.Child = _mapCanvas;
            mapCardStack.Children.Add(canvasBorder);

            mapCardStack.Children.Add(new TextBlock
            {
                Text = LocalizationManager.T(
                    "💡 Wskazówka: Klikaj lewym przyciskiem myszy na mapie powyżej, aby dodawać wierzchołki granicy gry (AO) lub stawiać punkty taktyczne POI / znaki NATO APP-6.",
                    "💡 Tip: Left-click on the map canvas above to add AO boundary vertices or place tactical POI markers / NATO APP-6 symbols."),
                Foreground = TacticalUiFactory.TextMutedBrush,
                FontSize = 11.5,
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });

            mapCard.Child = mapCardStack;
            Grid.SetColumn(mapCard, 0);
            workspaceGrid.Children.Add(mapCard);

            // Prawa kolumna: Narzędzia Edytora Taktycznego (Tryb AO vs POI/NATO + Lista obiektów)
            var toolsCard = TacticalUiFactory.CreateCardBorder(null, new Thickness(0));
            var toolsStack = new StackPanel();
            toolsStack.Children.Add(TacticalUiFactory.CreateSectionHeader(
                LocalizationManager.T("🎯 EDYTOR TAKTYCZNY (AO / POI / ZNAKI NATO APP-6)", "🎯 TACTICAL EDITOR (AO / POI / NATO APP-6 SYMBOLS)"),
                TacticalUiFactory.AccentGoldBrush, 13.5));

            var modeWrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            Button modeAoBtn = null;
            Button modePoiBtn = null;

            void UpdateModeButtonsVisual()
            {
                if (modeAoBtn == null || modePoiBtn == null) return;
                modeAoBtn.Background = _isDrawingAoMode ? TacticalUiFactory.AccentCyanBrush : new SolidColorBrush(Color.FromRgb(14, 34, 44));
                modeAoBtn.Foreground = _isDrawingAoMode ? new SolidColorBrush(Color.FromRgb(5, 16, 20)) : TacticalUiFactory.TextPrimaryBrush;
                modePoiBtn.Background = !_isDrawingAoMode ? TacticalUiFactory.AccentGoldBrush : new SolidColorBrush(Color.FromRgb(14, 34, 44));
                modePoiBtn.Foreground = !_isDrawingAoMode ? new SolidColorBrush(Color.FromRgb(5, 16, 20)) : TacticalUiFactory.TextPrimaryBrush;
            }

            modeAoBtn = TacticalUiFactory.CreateActionButton(
                LocalizationManager.Get("MAP_DRAW_AO"),
                TacticalUiFactory.AccentCyanBrush,
                new SolidColorBrush(Color.FromRgb(5, 16, 20)),
                TacticalUiFactory.AccentCyanBrush, 34);
            modeAoBtn.Click += (_, __) => { _isDrawingAoMode = true; UpdateModeButtonsVisual(); };

            modePoiBtn = TacticalUiFactory.CreateActionButton(
                LocalizationManager.Get("MAP_ADD_POI"),
                new SolidColorBrush(Color.FromRgb(14, 34, 44)),
                TacticalUiFactory.TextPrimaryBrush,
                TacticalUiFactory.AccentGoldBrush, 34);
            modePoiBtn.Click += (_, __) => { _isDrawingAoMode = false; UpdateModeButtonsVisual(); };

            modeWrap.Children.Add(modeAoBtn);
            modeWrap.Children.Add(modePoiBtn);
            toolsStack.Children.Add(modeWrap);
            UpdateModeButtonsVisual();

            // Szybkie operacje na wielokącie AO
            var aoBtnsWrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            var quickAoBtn = TacticalUiFactory.CreateActionButton(
                LocalizationManager.T("📐 Utwórz domyślny wielokąt AO (600×600m)", "📐 Create Default AO Polygon (600×600m)"),
                new SolidColorBrush(Color.FromRgb(22, 110, 65)),
                Brushes.White,
                TacticalUiFactory.AccentGreenBrush, 30);
            quickAoBtn.Click += (_, __) =>
            {
                cfg.AoVertices.Clear();
                double cLat = cfg.ViewportCenterLat;
                double cLon = cfg.ViewportCenterLon;
                cfg.AoVertices.Add(new MapVertexModel { Lat = cLat + 0.0027, Lon = cLon - 0.0038 });
                cfg.AoVertices.Add(new MapVertexModel { Lat = cLat + 0.0029, Lon = cLon + 0.0036 });
                cfg.AoVertices.Add(new MapVertexModel { Lat = cLat - 0.0026, Lon = cLon + 0.0040 });
                cfg.AoVertices.Add(new MapVertexModel { Lat = cLat - 0.0028, Lon = cLon - 0.0035 });
                ProjectStore.Instance.SyncGeneratorsToOpord();
                RenderTacticalCanvas();
            };

            var clearAoBtn = TacticalUiFactory.CreateActionButton(
                LocalizationManager.Get("MAP_CLEAR_AO"),
                new SolidColorBrush(Color.FromRgb(85, 22, 22)),
                Brushes.White,
                TacticalUiFactory.AccentRedBrush, 30);
            clearAoBtn.Click += (_, __) =>
            {
                cfg.AoVertices.Clear();
                ProjectStore.Instance.SyncGeneratorsToOpord();
                RenderTacticalCanvas();
            };

            aoBtnsWrap.Children.Add(quickAoBtn);
            aoBtnsWrap.Children.Add(clearAoBtn);
            toolsStack.Children.Add(aoBtnsWrap);

            // Sekcja konfiguracji nowego punktu POI / znaku NATO APP-6
            toolsStack.Children.Add(new Border { Height = 1, Background = TacticalUiFactory.BorderCardBrush, Margin = new Thickness(0, 2, 0, 8) });
            toolsStack.Children.Add(TacticalUiFactory.CreateFieldLabel(LocalizationManager.T("Parametry stawianego punktu POI / znaku NATO APP-6:", "New POI / NATO APP-6 Symbol Parameters:")));

            var poiSetupGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            poiSetupGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
            poiSetupGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            poiSetupGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            poiSetupGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            poiSetupGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.7, GridUnitType.Star) });

            var newNameBox = TacticalUiFactory.CreateTextBox(_newPoiName, false, 30);
            newNameBox.TextChanged += (_, __) => _newPoiName = newNameBox.Text;
            Grid.SetColumn(newNameBox, 0); poiSetupGrid.Children.Add(newNameBox);

            var cbPoiCat = new ComboBox { Height = 30 };
            cbPoiCat.Items.Add("INFIL");
            cbPoiCat.Items.Add("RESPAWN");
            cbPoiCat.Items.Add("PARKING");
            cbPoiCat.Items.Add("KOD");
            cbPoiCat.Items.Add("HQ");
            cbPoiCat.Items.Add("OBJ");
            cbPoiCat.Items.Add("ENEMY");
            cbPoiCat.SelectedItem = _newPoiCategory;
            cbPoiCat.SelectionChanged += (_, __) =>
            {
                _newPoiCategory = cbPoiCat.SelectedItem?.ToString() ?? "INFIL";
                _newPoiName = _newPoiCategory + "-" + (cfg.Pois.Count + 1);
                newNameBox.Text = _newPoiName;
            };
            Grid.SetColumn(cbPoiCat, 2); poiSetupGrid.Children.Add(cbPoiCat);

            var rotBox = TacticalUiFactory.CreateTextBox(_newPoiRotationDeg.ToString("0", CultureInfo.InvariantCulture), false, 30, true);
            rotBox.ToolTip = LocalizationManager.T("Obrót znaku NATO w stopniach (0..359°)", "NATO symbol rotation in degrees (0..359°)");
            rotBox.TextChanged += (_, __) =>
            {
                if (double.TryParse(rotBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double r))
                    _newPoiRotationDeg = ((r % 360) + 360) % 360;
            };
            Grid.SetColumn(rotBox, 4); poiSetupGrid.Children.Add(rotBox);
            toolsStack.Children.Add(poiSetupGrid);

            toolsStack.Children.Add(TacticalUiFactory.CreateFieldLabel(LocalizationManager.T("Lista punktów POI i znaków NATO na mapie:", "Placed POI Markers & NATO Symbols:")));
            _poiListContainer = new StackPanel();
            toolsStack.Children.Add(_poiListContainer);

            toolsCard.Child = toolsStack;
            Grid.SetColumn(toolsCard, 2);
            workspaceGrid.Children.Add(toolsCard);

            _mainStack.Children.Add(workspaceGrid);

            RebuildPoiRows();
            RenderTacticalCanvas();
        }

        private void OnMapCanvasLeftClick(object sender, MouseButtonEventArgs e)
        {
            var cfg = ProjectStore.Instance.MapConfig;
            double w = _mapCanvas.ActualWidth > 10 ? _mapCanvas.ActualWidth : 600;
            double h = _mapCanvas.ActualHeight > 10 ? _mapCanvas.ActualHeight : 480;
            Point pt = e.GetPosition(_mapCanvas);

            var bounds = GeoCoordinateEngine.CalculateTrueScaleBounds(
                cfg.ViewportCenterLat, cfg.ViewportCenterLon, cfg.ScaleDenominator, cfg.SheetFormat);

            double clickedLon = bounds.MinLon + (pt.X / w) * (bounds.MaxLon - bounds.MinLon);
            double clickedLat = bounds.MaxLat - (pt.Y / h) * (bounds.MaxLat - bounds.MinLat);

            if (_isDrawingAoMode)
            {
                cfg.AoVertices.Add(new MapVertexModel { Lat = clickedLat, Lon = clickedLon });
            }
            else
            {
                cfg.Pois.Add(new MapPoiModel
                {
                    Name = string.IsNullOrWhiteSpace(_newPoiName) ? ("POI-" + (cfg.Pois.Count + 1)) : _newPoiName,
                    Category = _newPoiCategory,
                    Lat = clickedLat,
                    Lon = clickedLon,
                    RotationDeg = _newPoiRotationDeg
                });
                _newPoiName = _newPoiCategory + "-" + (cfg.Pois.Count + 1);
                RebuildPoiRows();
            }

            ProjectStore.Instance.SyncGeneratorsToOpord();
            RenderTacticalCanvas();
        }

        private void RebuildPoiRows()
        {
            if (_poiListContainer == null) return;
            _poiListContainer.Children.Clear();
            var cfg = ProjectStore.Instance.MapConfig;

            foreach (var poi in cfg.Pois.ToList())
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.7, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.55, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var tName = TacticalUiFactory.CreateTextBox(poi.Name, false, 28);
                tName.TextChanged += (_, __) => { poi.Name = tName.Text; RenderTacticalCanvas(); };
                Grid.SetColumn(tName, 0); row.Children.Add(tName);

                var tCat = TacticalUiFactory.CreateTextBox(poi.Category, false, 28);
                tCat.Margin = new Thickness(4, 0, 0, 0);
                tCat.IsReadOnly = true;
                Grid.SetColumn(tCat, 1); row.Children.Add(tCat);

                string coordTxt = GeoCoordinateEngine.FormatBySystem(poi.Lat, poi.Lon, cfg.CoordinateSystem, cfg.SubdivisionMode);
                var tCoord = TacticalUiFactory.CreateTextBox(coordTxt, false, 28, true);
                tCoord.Margin = new Thickness(4, 0, 0, 0);
                tCoord.IsReadOnly = true;
                Grid.SetColumn(tCoord, 2); row.Children.Add(tCoord);

                var tRot = TacticalUiFactory.CreateTextBox(poi.RotationDeg.ToString("0", CultureInfo.InvariantCulture) + "°", false, 28, true);
                tRot.Margin = new Thickness(4, 0, 0, 0);
                tRot.TextChanged += (_, __) =>
                {
                    string clean = tRot.Text.Replace("°", "").Trim();
                    if (double.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out double deg))
                    {
                        poi.RotationDeg = ((deg % 360) + 360) % 360;
                        RenderTacticalCanvas();
                    }
                };
                Grid.SetColumn(tRot, 3); row.Children.Add(tRot);

                var delBtn = TacticalUiFactory.CreateCompactIconBtn("−", new SolidColorBrush(Color.FromRgb(85, 22, 22)), TacticalUiFactory.AccentRedBrush, 28, 28);
                delBtn.Click += (_, __) =>
                {
                    cfg.Pois.Remove(poi);
                    ProjectStore.Instance.SyncGeneratorsToOpord();
                    RebuildPoiRows();
                    RenderTacticalCanvas();
                };
                Grid.SetColumn(delBtn, 4); row.Children.Add(delBtn);

                _poiListContainer.Children.Add(row);
            }
        }

        private void RenderTacticalCanvas()
        {
            if (_mapCanvas == null || _lidarReliefLayer == null || _gridAndVectorsLayer == null) return;
            var cfg = ProjectStore.Instance.MapConfig;

            double w = _mapCanvas.ActualWidth > 20 ? _mapCanvas.ActualWidth : 640;
            double h = _mapCanvas.ActualHeight > 20 ? _mapCanvas.ActualHeight : 480;

            _lidarReliefLayer.Children.Clear();
            _gridAndVectorsLayer.Children.Clear();
            _lidarReliefLayer.Opacity = cfg.LidarOpacityPercent / 100.0;

            var bounds = GeoCoordinateEngine.CalculateTrueScaleBounds(
                cfg.ViewportCenterLat, cfg.ViewportCenterLon, cfg.ScaleDenominator, cfg.SheetFormat);

            string centerMgrs = GeoCoordinateEngine.FormatBySystem(cfg.ViewportCenterLat, cfg.ViewportCenterLon, cfg.CoordinateSystem, cfg.SubdivisionMode);
            if (_liveScaleCoverageLabel != null)
            {
                _liveScaleCoverageLabel.Text = string.Format(CultureInfo.InvariantCulture,
                    "1:{0} ({1:0}m × {2:0}m) | {3}: {4}",
                    cfg.ScaleDenominator, bounds.WidthMeters, bounds.HeightMeters, cfg.CoordinateSystem, centerMgrs);
            }

            if (_aoStatusBadge != null)
            {
                if (cfg.HasExplicitAoMarked)
                {
                    _aoStatusBadge.Text = LocalizationManager.T("✅ AO WYZNACZONE (GOTOWE DLA METOC)", "✅ AO MARKED (READY FOR METOC)");
                    _aoStatusBadge.Foreground = TacticalUiFactory.AccentGreenBrush;
                }
                else
                {
                    _aoStatusBadge.Text = LocalizationManager.T("⚠️ BRAK AO (SAM ARKUSZ MAPY)", "⚠️ NO AO MARKED (SHEET ONLY)");
                    _aoStatusBadge.Foreground = TacticalUiFactory.AccentGoldBrush;
                }
            }

            // 1. Warstwa rzeźby terenu Turbo LiDAR (ISOK) — poziomice, skarpy i stary rów podejścia ("Infil")
            for (int i = 1; i <= 7; i++)
            {
                var contour = new Polyline
                {
                    Stroke = new SolidColorBrush(Color.FromArgb(140, 210, 230, 220)),
                    StrokeThickness = (i % 2 == 0) ? 1.8 : 1.0
                };
                for (int x = 0; x <= 10; x++)
                {
                    double px = (w * x) / 10.0;
                    double py = (h * i) / 8.0 + Math.Sin(x * 0.8 + i) * 18.0;
                    contour.Points.Add(new Point(px, py));
                }
                _lidarReliefLayer.Children.Add(contour);
            }

            // Charakterystyczny rów melioracyjny / transzeja LiDAR pod podejście szturmowe
            var trenchLine = new Polyline
            {
                Stroke = new SolidColorBrush(Color.FromArgb(220, 250, 208, 44)),
                StrokeThickness = 3.0,
                StrokeDashArray = new DoubleCollection { 4, 2 }
            };
            trenchLine.Points.Add(new Point(w * 0.12, h * 0.82));
            trenchLine.Points.Add(new Point(w * 0.34, h * 0.64));
            trenchLine.Points.Add(new Point(w * 0.52, h * 0.48));
            trenchLine.Points.Add(new Point(w * 0.76, h * 0.28));
            _lidarReliefLayer.Children.Add(trenchLine);

            // 2. Siatka wojskowa 100 m + podział wewnętrzny Keypad 1-9 / Ćwiartki A-D
            int cols = Math.Clamp((int)Math.Round(bounds.WidthMeters / 100.0), 4, 24);
            int rows = Math.Clamp((int)Math.Round(bounds.HeightMeters / 100.0), 4, 24);
            double cellW = w / cols;
            double cellH = h / rows;

            var gridBrush100m = new SolidColorBrush(Color.FromArgb(95, 31, 224, 197));
            var gridBrushSub = new SolidColorBrush(Color.FromArgb(45, 31, 224, 197));

            for (int c = 0; c <= cols; c++)
            {
                double x = c * cellW;
                _gridAndVectorsLayer.Children.Add(new Line
                {
                    X1 = x, Y1 = 0, X2 = x, Y2 = h,
                    Stroke = (c % 5 == 0) ? TacticalUiFactory.AccentGoldBrush : gridBrush100m,
                    StrokeThickness = (c % 5 == 0) ? 1.5 : 0.8
                });

                if (c < cols && cfg.SubdivisionMode == "KEYPAD_9" && cols <= 14)
                {
                    for (int sub = 1; sub <= 2; sub++)
                    {
                        double sx = x + (cellW * sub) / 3.0;
                        _gridAndVectorsLayer.Children.Add(new Line { X1 = sx, Y1 = 0, X2 = sx, Y2 = h, Stroke = gridBrushSub, StrokeThickness = 0.5 });
                    }
                }
                else if (c < cols && cfg.SubdivisionMode == "QUARTERS_4" && cols <= 16)
                {
                    double sx = x + cellW * 0.5;
                    _gridAndVectorsLayer.Children.Add(new Line { X1 = sx, Y1 = 0, X2 = sx, Y2 = h, Stroke = gridBrushSub, StrokeThickness = 0.5 });
                }
            }

            for (int r = 0; r <= rows; r++)
            {
                double y = r * cellH;
                _gridAndVectorsLayer.Children.Add(new Line
                {
                    X1 = 0, Y1 = y, X2 = w, Y2 = y,
                    Stroke = (r % 5 == 0) ? TacticalUiFactory.AccentGoldBrush : gridBrush100m,
                    StrokeThickness = (r % 5 == 0) ? 1.5 : 0.8
                });

                if (r < rows && cfg.SubdivisionMode == "KEYPAD_9" && rows <= 14)
                {
                    for (int sub = 1; sub <= 2; sub++)
                    {
                        double sy = y + (cellH * sub) / 3.0;
                        _gridAndVectorsLayer.Children.Add(new Line { X1 = 0, Y1 = sy, X2 = w, Y2 = sy, Stroke = gridBrushSub, StrokeThickness = 0.5 });
                    }
                }
                else if (r < rows && cfg.SubdivisionMode == "QUARTERS_4" && rows <= 16)
                {
                    double sy = y + cellH * 0.5;
                    _gridAndVectorsLayer.Children.Add(new Line { X1 = 0, Y1 = sy, X2 = w, Y2 = sy, Stroke = gridBrushSub, StrokeThickness = 0.5 });
                }
            }

            double spanLon = Math.Max(0.00001, bounds.MaxLon - bounds.MinLon);
            double spanLat = Math.Max(0.00001, bounds.MaxLat - bounds.MinLat);

            // 3. Rysowanie granicy gry (AO)
            if (cfg.AoVertices.Count > 0)
            {
                var polyPts = new PointCollection();
                foreach (var v in cfg.AoVertices)
                {
                    double px = ((v.Lon - bounds.MinLon) / spanLon) * w;
                    double py = ((bounds.MaxLat - v.Lat) / spanLat) * h;
                    polyPts.Add(new Point(px, py));
                }

                var aoPoly = new Polygon
                {
                    Points = polyPts,
                    Stroke = TacticalUiFactory.AccentGoldBrush,
                    StrokeThickness = 2.5,
                    Fill = new SolidColorBrush(Color.FromArgb(35, 31, 224, 197))
                };
                _gridAndVectorsLayer.Children.Add(aoPoly);

                foreach (Point pt in polyPts)
                {
                    var vertexDot = new Ellipse
                    {
                        Width = 8, Height = 8,
                        Fill = TacticalUiFactory.AccentGoldBrush,
                        Stroke = Brushes.Black,
                        StrokeThickness = 1
                    };
                    Canvas.SetLeft(vertexDot, pt.X - 4);
                    Canvas.SetTop(vertexDot, pt.Y - 4);
                    _gridAndVectorsLayer.Children.Add(vertexDot);
                }
            }

            // 4. Rysowanie punktów POI i obracanych znaków NATO APP-6
            foreach (var poi in cfg.Pois)
            {
                double px = ((poi.Lon - bounds.MinLon) / spanLon) * w;
                double py = ((bounds.MaxLat - poi.Lat) / spanLat) * h;

                Brush symbolBrush = poi.Category == "ENEMY"
                    ? TacticalUiFactory.AccentRedBrush
                    : (poi.Category == "INFIL" || poi.Category == "RESPAWN" ? TacticalUiFactory.AccentGreenBrush : TacticalUiFactory.AccentCyanBrush);

                var symbolContainer = new Canvas
                {
                    Width = 28,
                    Height = 20,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = new RotateTransform(poi.RotationDeg)
                };

                var natoFrame = new Rectangle
                {
                    Width = 28,
                    Height = 20,
                    Fill = new SolidColorBrush(Color.FromArgb(215, 6, 13, 16)),
                    Stroke = symbolBrush,
                    StrokeThickness = 2
                };
                symbolContainer.Children.Add(natoFrame);

                // Wskaźnik kierunku obrotu znaku NATO
                var dirArrow = new Polygon
                {
                    Points = new PointCollection { new Point(14, -7), new Point(9, 0), new Point(19, 0) },
                    Fill = symbolBrush
                };
                symbolContainer.Children.Add(dirArrow);

                Canvas.SetLeft(symbolContainer, px - 14);
                Canvas.SetTop(symbolContainer, py - 10);
                _gridAndVectorsLayer.Children.Add(symbolContainer);

                var lbl = new TextBlock
                {
                    Text = poi.Name,
                    Foreground = Brushes.White,
                    Background = new SolidColorBrush(Color.FromArgb(180, 6, 13, 16)),
                    FontSize = 10.5,
                    FontWeight = FontWeights.Bold,
                    Padding = new Thickness(4, 1, 4, 1)
                };
                Canvas.SetLeft(lbl, px - 22);
                Canvas.SetTop(lbl, py + 12);
                _gridAndVectorsLayer.Children.Add(lbl);
            }
        }
    }
}
