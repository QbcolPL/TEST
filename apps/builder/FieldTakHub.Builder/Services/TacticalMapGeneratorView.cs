#pragma warning disable CS0414, CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625
global using System.Text.RegularExpressions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
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
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace FieldTakHub.Builder.Services
{
    public static partial class TacticalMapGeneratorView
    {
        private static double _dotMarkerSizePx = 14.0;
        private static double _natoMarkerScale = 1.0;
        private static string _savedSearchQuery = "Czechowice-Dziedzice";
        private static string _savedDefaultObjectName = "➤";
        private static ScrollViewer _leftControlScrollViewer;
        private static bool _isRebuildingMapControls;
        private static DispatcherTimer _wheelZoomDebounceTimer;

        private static void ExportExtraMapUiState(JsonObject root)
        {
            if (root == null) return;
            root["searchQuery"] = _searchPlaceBox != null ? _searchPlaceBox.Text : _savedSearchQuery;
            root["selectedObjectColorHex"] = _selectedObjectColorHex;
            root["selectedNatoSymbolCode"] = _selectedNatoSymbolCode;
            root["defaultObjectName"] = _objectNameInputBox != null ? _objectNameInputBox.Text : _savedDefaultObjectName;
            root["newObjectShowLabelDefault"] = _newObjectShowLabelDefault;
            root["dotMarkerSizePx"] = _dotMarkerSizePx;
            root["natoMarkerScale"] = _natoMarkerScale;
            root["northArrowPos"] = _northArrowPos;
            root["scaleBarPos"] = _scaleBarPos;
            root["legendPos"] = _legendPos;
            if (_northNormX.HasValue) root["northNormX"] = _northNormX.Value;
            if (_northNormY.HasValue) root["northNormY"] = _northNormY.Value;
            if (_scaleNormX.HasValue) root["scaleNormX"] = _scaleNormX.Value;
            if (_scaleNormY.HasValue) root["scaleNormY"] = _scaleNormY.Value;
            if (_legendNormX.HasValue) root["legendNormX"] = _legendNormX.Value;
            if (_legendNormY.HasValue) root["legendNormY"] = _legendNormY.Value;
            root["kmzGrgMode"] = _kmzGrgMode;
            root["offlineMinZoom"] = _offlineMinZoom;
            root["offlineMaxZoom"] = _offlineMaxZoom;
            root["offlineBoundsMode"] = _offlineBoundsMode;
            root["offlineDbFormat"] = _offlineDbFormat;
            root["bakeLidarInOffline"] = _bakeLidarInOffline;
            root["includeVectorsWithOffline"] = _includeVectorsWithOffline;
        }

        private static void ImportExtraMapUiState(JsonObject root)
        {
            if (root == null) return;
            if (root["searchQuery"] != null) _savedSearchQuery = root["searchQuery"].ToString();
            if (root["selectedObjectColorHex"] != null) _selectedObjectColorHex = root["selectedObjectColorHex"].ToString();
            if (root["selectedNatoSymbolCode"] != null) _selectedNatoSymbolCode = root["selectedNatoSymbolCode"].ToString();
            if (root["defaultObjectName"] != null) _savedDefaultObjectName = root["defaultObjectName"].ToString();
            if (bool.TryParse(root["newObjectShowLabelDefault"]?.ToString(), out bool nld)) _newObjectShowLabelDefault = nld;
            if (double.TryParse(root["dotMarkerSizePx"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double dms)) _dotMarkerSizePx = Math.Clamp(dms, 4.0, 48.0);
            if (double.TryParse(root["natoMarkerScale"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double nms)) _natoMarkerScale = Math.Clamp(nms, 0.4, 3.0);

            if (root["northArrowPos"] != null) _northArrowPos = root["northArrowPos"].ToString();
            if (root["scaleBarPos"] != null) _scaleBarPos = root["scaleBarPos"].ToString();
            if (root["legendPos"] != null) _legendPos = root["legendPos"].ToString();

            _northNormX = double.TryParse(root["northNormX"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double nnx) ? nnx : null;
            _northNormY = double.TryParse(root["northNormY"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double nny) ? nny : null;
            _scaleNormX = double.TryParse(root["scaleNormX"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double snx) ? snx : null;
            _scaleNormY = double.TryParse(root["scaleNormY"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double sny) ? sny : null;
            _legendNormX = double.TryParse(root["legendNormX"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double lnx) ? lnx : null;
            _legendNormY = double.TryParse(root["legendNormY"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double lny) ? lny : null;

            if (root["kmzGrgMode"] != null) _kmzGrgMode = root["kmzGrgMode"].ToString();
            if (int.TryParse(root["offlineMinZoom"]?.ToString(), out int omin)) _offlineMinZoom = Math.Clamp(omin, 10, 18);
            if (int.TryParse(root["offlineMaxZoom"]?.ToString(), out int omax)) _offlineMaxZoom = Math.Clamp(omax, _offlineMinZoom, 19);
            if (root["offlineBoundsMode"] != null) _offlineBoundsMode = root["offlineBoundsMode"].ToString();
            if (root["offlineDbFormat"] != null) _offlineDbFormat = root["offlineDbFormat"].ToString();
            if (bool.TryParse(root["bakeLidarInOffline"]?.ToString(), out bool bl)) _bakeLidarInOffline = bl;
            if (bool.TryParse(root["includeVectorsWithOffline"]?.ToString(), out bool iv)) _includeVectorsWithOffline = iv;

            if (_dimmerRect != null) _dimmerRect.Opacity = _mapDimmerOpacity;
            if (_lidarTileCanvas != null) _lidarTileCanvas.Opacity = _lidarOpacity;

            if (_hostWindow != null && _leftControlScrollViewer != null)
            {
                RebuildLeftControlsPanel(_hostWindow);
            }
        }

        private static void UpdateSelectedMarkerInspectorUI()
        {
            if (_inspectorTitleLabel == null || _inspectorMgrsLabel == null || _inspectorWgsUtmLabel == null)
                return;

            if (_inspectedObject == null || _inspectedObject.Points.Count == 0 || !_mapObjects.Contains(_inspectedObject))
            {
                _inspectorTitleLabel.Text = "📍 Kliknij dowolny znacznik na mapie, aby odczytać jego współrzędne";
                _inspectorMgrsLabel.Text = "MGRS: —   |   Keypad / Ćwiartka: —";
                _inspectorWgsUtmLabel.Text = "WGS84: —   |   UTM: —";
                return;
            }

            int idx = Math.Clamp(_inspectedVertexIndex, 0, _inspectedObject.Points.Count - 1);
            var pt = _inspectedObject.Points[idx];
            var (fullMgrs, _, kpInfo, utmTxt, wgsTxt) = FormatTacticalPointCoordinates(pt.Lat, pt.Lon);

            string ptSuffix = _inspectedObject.Points.Count > 1 ? $" (Punkt #{idx + 1})" : "";
            _inspectorTitleLabel.Text = $"🎯 {_inspectedObject.Name}{ptSuffix}  [{_inspectedObject.SymbolCode.Replace("NATO_", "").Replace("SHAPE_", "")}]";
            _inspectorMgrsLabel.Text = $"MGRS: {fullMgrs}   •   {kpInfo}";
            _inspectorWgsUtmLabel.Text = $"WGS84: {wgsTxt}   •   UTM: {utmTxt}";
        }

        private static void CopySelectedMarkerCoordsToClipboard()
        {
            if (_inspectedObject == null || _inspectedObject.Points.Count == 0) return;
            try
            {
                int idx = Math.Clamp(_inspectedVertexIndex, 0, _inspectedObject.Points.Count - 1);
                var pt = _inspectedObject.Points[idx];
                var (fullMgrs, _, kpInfo, utmTxt, wgsTxt) = FormatTacticalPointCoordinates(pt.Lat, pt.Lon);
                string report = $"{_inspectedObject.Name} | MGRS: {fullMgrs} ({kpInfo}) | WGS84: {wgsTxt} | UTM: {utmTxt}";
                Clipboard.SetText(report);
                if (_statusText != null)
                    _statusText.Text = $"✓ Skopiowano meldunek współrzędnych do schowka: {report}";
            }
            catch { }
        }

        private static void UpdateSheetViewportSize()
        {
            if (_viewportHostGrid == null || _sheetFrameBorder == null) return;
            double hostW = _viewportHostGrid.ActualWidth > 300
                ? _viewportHostGrid.ActualWidth
                : Math.Max(720, (_hostWindow?.ActualWidth ?? 1280) - 460);
            double hostH = _viewportHostGrid.ActualHeight > 250
                ? _viewportHostGrid.ActualHeight
                : Math.Max(540, (_hostWindow?.ActualHeight ?? 800) - 120);

            double availW = Math.Max(260, hostW - 28);
            double availH = Math.Max(260, hostH - 56);

            var (wMm, hMm) = GetActiveSheetDimensionsMm();
            double aspect = wMm / hMm;

            double targetW = availW;
            double targetH = targetW / aspect;
            if (targetH > availH)
            {
                targetH = availH;
                targetW = targetH * aspect;
            }

            _sheetFrameBorder.Width = Math.Round(targetW);
            _sheetFrameBorder.Height = Math.Round(targetH);

            double realWM = (wMm / 1000.0) * _targetScaleDenominator;
            double realHM = (hMm / 1000.0) * _targetScaleDenominator;
            if (_sheetInfoBadge != null)
                _sheetInfoBadge.Text = $"📄 Arkusz: {_selectedSheet.Name.Split('—')[0].Trim()} ({_sheetOrientation}: {wMm:0}×{hMm:0} mm)  •  Skala 1:{_targetScaleDenominator:N0}  •  Wycinek terenu: {realWM:0} m × {realHM:0} m";

            UpdateOfflineTileEstimateUI();
        }

        private static Border CreateRedMinusDeleteButton(string tooltip, Action onClick)
        {
            var border = new Border
            {
                Width = 24,
                Height = 22,
                CornerRadius = new CornerRadius(5),
                Background = TacticalConfigPanel.BrushRedMinus,
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = tooltip,
                Margin = new Thickness(4, 0, 0, 0),
                Child = new TextBlock
                {
                    Text = "−",
                    Foreground = Brushes.White,
                    FontSize = 15,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, -2, 0, 0)
                }
            };
            border.MouseLeftButtonDown += (s, e) => { e.Handled = true; onClick(); };
            return border;
        }

        private static void RebuildFactionCheckboxesUI()
        {
            if (_factionsCheckboxesWrap == null) return;
            var configured = TacticalConfigPanel.GetConfiguredFactions();
            if (_selectedMapFactions.Count == 0 && configured.Count > 0)
                _selectedMapFactions.Add(configured[0].Name);

            _factionsCheckboxesWrap.Children.Clear();
            foreach (var fac in configured)
            {
                string fName = fac.Name;
                bool isChecked = _selectedMapFactions.Contains(fName);

                var badgeStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                badgeStack.Children.Add(new Border
                {
                    Width = 10,
                    Height = 10,
                    CornerRadius = new CornerRadius(2),
                    Background = TacticalConfigPanel.BrushFromHex(fac.ColorHex, Color.FromRgb(38, 132, 255)),
                    BorderBrush = Brushes.White,
                    BorderThickness = new Thickness(0.8),
                    Margin = new Thickness(0, 0, 5, 0)
                });
                badgeStack.Children.Add(new TextBlock { Text = fName, Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.Bold });

                var chk = new CheckBox
                {
                    Content = badgeStack,
                    IsChecked = isChecked,
                    Margin = new Thickness(0, 2, 12, 2),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Cursor = Cursors.Hand
                };
                chk.Checked += (s, e) => { _selectedMapFactions.Add(fName); if (!_isRebuildingMapControls) TacticalConfigPanel.MarkDirty(); RedrawVectorsOnly(); };
                chk.Unchecked += (s, e) => { _selectedMapFactions.Remove(fName); if (!_isRebuildingMapControls) TacticalConfigPanel.MarkDirty(); RedrawVectorsOnly(); };
                _factionsCheckboxesWrap.Children.Add(chk);
            }
        }

        private static List<(string Name, string Channel, string ColorHex, string Psk)> GetActiveMapFactions()
        {
            var all = TacticalConfigPanel.GetConfiguredFactions();
            var filtered = all.Where(f => _selectedMapFactions.Contains(f.Name)).ToList();
            return filtered.Count > 0 ? filtered : all.Take(1).ToList();
        }

        public static void AttachFullScreenTab(Window win)
        {
            if (Application.Current == null || win == null) return;
            _hostWindow = win;
            TacticalConfigPanel.MapStateExportProvider = ExportMapStateToJson;
            TacticalConfigPanel.MapStateImportConsumer = ImportMapStateFromJson;

            TacticalConfigPanel.ConsumePendingMapStateIfAny();

            try
            {
                if (win.Content is FrameworkElement existingFe && existingFe.Tag?.ToString() == "FTH_MASTER_SHELL") return;
                if (win.Content is UIElement currentContent)
                {
                    var overlay = BuildFullScreenMapGeneratorUI(win);
                    overlay.Visibility = Visibility.Collapsed;

                    _originalWindowContent = currentContent;
                    win.Content = null;
                    _fullScreenOverlay = overlay;

                    var masterGrid = new Grid { Tag = "FTH_MASTER_SHELL", Background = TacticalConfigPanel.BrushCardBg };
                    masterGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });
                    masterGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                    var navBorder = new Border { Background = TacticalConfigPanel.BrushInputBg, BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(0, 0, 0, 1.5), Padding = new Thickness(16, 0, 16, 0) };
                    var navDock = new DockPanel();

                    var draftBtnsStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    var btnSaveDraft = MakeActionButton("💾 Zapisz projekt roboczy (.fthproj)", false, () =>
                    {
                        if (TacticalConfigPanel.SaveDraftProjectDialog(win) && _statusText != null)
                            _statusText.Text = $"✓ Zapisano wersję roboczą całego projektu (paczka .ftak + mapa): {TacticalConfigPanel.CurrentDraftProjectPath}";
                    });
                    btnSaveDraft.Height = 28; btnSaveDraft.Padding = new Thickness(12, 0, 12, 0); btnSaveDraft.Margin = new Thickness(0, 0, 6, 0); btnSaveDraft.BorderBrush = TacticalConfigPanel.BrushAccent;

                    var btnLoadDraft = MakeActionButton("📂 Wczytaj projekt (.fthproj)", false, () =>
                    {
                        if (TacticalConfigPanel.LoadDraftProjectDialog(win) && _statusText != null)
                            _statusText.Text = $"✓ Wczytano wersję roboczą projektu: {TacticalConfigPanel.CurrentDraftProjectPath}";
                    });
                    btnLoadDraft.Height = 28; btnLoadDraft.Padding = new Thickness(12, 0, 12, 0);
                    draftBtnsStack.Children.Add(btnSaveDraft);
                    draftBtnsStack.Children.Add(btnLoadDraft);
                    DockPanel.SetDock(draftBtnsStack, Dock.Right);
                    navDock.Children.Add(draftBtnsStack);

                    var tabsStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    _tabBuilderBtn = new Button { Content = "📦 Konfigurator Paczek (.ftak)", Height = 32, Padding = new Thickness(18, 0, 18, 0), Margin = new Thickness(0, 0, 10, 0), Background = TacticalConfigPanel.BrushButtonAccent, Foreground = TacticalConfigPanel.BrushDarkFg, BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1), FontWeight = FontWeights.Bold, FontSize = 12, Cursor = Cursors.Hand };
                    _tabMapBtn = new Button { Content = "🗺️ Generator Map Taktycznych 1:1 (Pełny Ekran)", Height = 32, Padding = new Thickness(18, 0, 18, 0), Background = TacticalConfigPanel.BrushItemBg, Foreground = TacticalConfigPanel.BrushAccent, BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1.5), FontWeight = FontWeights.Bold, FontSize = 12, Cursor = Cursors.Hand };

                    _tabBuilderBtn.Click += (s, e) => HideFullScreenMapTab();
                    _tabMapBtn.Click += (s, e) => ShowFullScreenMapTab(win);

                    tabsStack.Children.Add(_tabBuilderBtn); tabsStack.Children.Add(_tabMapBtn);
                    navDock.Children.Add(tabsStack); navBorder.Child = navDock;
                    Grid.SetRow(navBorder, 0); masterGrid.Children.Add(navBorder);

                    var contentHostGrid = new Grid();
                    contentHostGrid.Children.Add(_originalWindowContent);
                    contentHostGrid.Children.Add(_fullScreenOverlay);
                    MeshtasticUsbProvisionerView.AttachUsbTab(win, tabsStack, contentHostGrid, _tabBuilderBtn, _tabMapBtn, _originalWindowContent, _fullScreenOverlay);

                    Grid.SetRow(contentHostGrid, 1); masterGrid.Children.Add(contentHostGrid);
                    win.Content = masterGrid;
                }
            }
            catch { }
        }

        public static void ShowFullScreenMapTab(Window win)
        {
            if (_fullScreenOverlay == null && win != null) AttachFullScreenTab(win);
            if (_fullScreenOverlay != null)
            {
                MeshtasticUsbProvisionerView.HideUsbTabOnly();
                if (_originalWindowContent != null) _originalWindowContent.Visibility = Visibility.Collapsed;
                _fullScreenOverlay.Visibility = Visibility.Visible;
                if (_tabMapBtn != null) { _tabMapBtn.Background = TacticalConfigPanel.BrushButtonAccent; _tabMapBtn.Foreground = TacticalConfigPanel.BrushDarkFg; }
                if (_tabBuilderBtn != null) { _tabBuilderBtn.Background = TacticalConfigPanel.BrushItemBg; _tabBuilderBtn.Foreground = Brushes.White; }

                RebuildFactionCheckboxesUI();
                UpdateSheetViewportSize();
                ApplyScaleDenominator(_targetScaleDenominator);
                _ = _fullScreenOverlay.Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdateSheetViewportSize();
                    _ = RenderMapTilesAsync();
                }), DispatcherPriority.Loaded);
            }
        }

        public static void HideFullScreenMapTab()
        {
            MeshtasticUsbProvisionerView.HideUsbTabOnly();
            if (_fullScreenOverlay != null)
            {
                _wmsSheetCts?.Cancel();
                _isWmsSheetFetchRunning = false;
                _fullScreenOverlay.Visibility = Visibility.Collapsed;
                if (_originalWindowContent != null) _originalWindowContent.Visibility = Visibility.Visible;
                if (_tabBuilderBtn != null) { _tabBuilderBtn.Background = TacticalConfigPanel.BrushButtonAccent; _tabBuilderBtn.Foreground = TacticalConfigPanel.BrushDarkFg; }
                if (_tabMapBtn != null) { _tabMapBtn.Background = TacticalConfigPanel.BrushItemBg; _tabMapBtn.Foreground = TacticalConfigPanel.BrushAccent; }
                TacticalConfigPanel.SaveSessionStateAuto(_hostWindow);
            }
        }

        private static void UpdateScalePresetButtonHighlights()
        {
            foreach (var b in _scalePresetButtons)
            {
                bool isCurrent = b.Content?.ToString() == $"1:{_targetScaleDenominator}";
                b.Background = isCurrent ? TacticalConfigPanel.BrushButtonAccent : TacticalConfigPanel.BrushInputBg;
                b.Foreground = isCurrent ? TacticalConfigPanel.BrushDarkFg : Brushes.White;
                b.BorderBrush = isCurrent ? TacticalConfigPanel.BrushAccent : TacticalConfigPanel.BrushInputBorder;
            }
        }

        private static void ScheduleDebouncedTileRender(int delayMs = 220)
        {
            _wmsSheetCts?.Cancel();
            _isWmsSheetFetchRunning = false;

            if (_wheelZoomDebounceTimer == null)
            {
                _wheelZoomDebounceTimer = new DispatcherTimer();
                _wheelZoomDebounceTimer.Tick += (_, __) =>
                {
                    _wheelZoomDebounceTimer.Stop();
                    TacticalConfigPanel.MarkDirty();
                    _ = RenderMapTilesAsync();
                };
            }
            _wheelZoomDebounceTimer.Interval = TimeSpan.FromMilliseconds(delayMs);
            _wheelZoomDebounceTimer.Stop();
            _wheelZoomDebounceTimer.Start();
        }

        private static void ApplyScaleDenominator(int scaleDenom)
        {
            _targetScaleDenominator = Math.Clamp(scaleDenom, 500, 250000);
            if (_scaleInputBox != null && _scaleInputBox.Text != _targetScaleDenominator.ToString())
                _scaleInputBox.Text = _targetScaleDenominator.ToString();

            UpdateSheetViewportSize();
            double targetMetersPerPx = GetExactMetersPerPixel();
            double equatorMetersPerPx = 156543.03392 * Math.Cos(_centerLat * Math.PI / 180.0);
            _zoom = Math.Clamp((int)Math.Round(Math.Log(equatorMetersPerPx / targetMetersPerPx, 2.0)), 5, 19);

            UpdateScalePresetButtonHighlights();
            RefreshTileCanvasesOnly();
            RedrawVectorsOnly();
            ScheduleDebouncedTileRender(160);
        }

        private static void RebuildLeftControlsPanel(Window win)
        {
            if (_leftControlScrollViewer == null || win == null) return;
            _isRebuildingMapControls = true;
            try
            {
                var leftStack = new StackPanel { Margin = new Thickness(6) };

                // SEKCJA 1: STRONY KONFLIKTU, FORMAT ARKUSZA (A3–A6 / MAPNIK) & SKALA 1:X
                var sec1 = CreateSectionCard("1. STRONY KONFLIKTU, FORMAT ARKUSZA (A3–A6 / MAPNIK) & SKALA 1:X");

                var facSelectBorder = new Border { Background = TacticalConfigPanel.BrushInputBg, BorderBrush = TacticalConfigPanel.BrushInputBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 0, 6) };
                var facSelectStack = new StackPanel();
                var facHeaderDock = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                var btnSyncFacs = MakeActionButton("🔄 Odśwież strony", false, () => { RebuildFactionCheckboxesUI(); RedrawVectorsOnly(); });
                btnSyncFacs.Height = 20; btnSyncFacs.FontSize = 9; btnSyncFacs.Padding = new Thickness(6, 0, 6, 0);
                DockPanel.SetDock(btnSyncFacs, Dock.Right);
                facHeaderDock.Children.Add(btnSyncFacs);
                facHeaderDock.Children.Add(new TextBlock { Text = "Strony konfliktu, których dotyczy mapa (zaznacz):", Foreground = TacticalConfigPanel.BrushAccent, FontSize = 10, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
                facSelectStack.Children.Add(facHeaderDock);

                _factionsCheckboxesWrap = new WrapPanel();
                RebuildFactionCheckboxesUI();
                facSelectStack.Children.Add(_factionsCheckboxesWrap);
                facSelectBorder.Child = facSelectStack;
                sec1.Children.Add(facSelectBorder);

                var searchRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
                _searchPlaceBox = (TextBox)TacticalConfigPanel.TextCtrl(_savedSearchQuery, v => { _savedSearchQuery = v; });
                var btnFind = MakeActionButton("🔍 Szukaj", true, async () => await SearchLocationNominatimAsync());
                btnFind.Height = 25; btnFind.Margin = new Thickness(6, 0, 0, 0);
                Grid.SetColumn(_searchPlaceBox, 0); Grid.SetColumn(btnFind, 1);
                searchRow.Children.Add(_searchPlaceBox); searchRow.Children.Add(btnFind);
                sec1.Children.Add(TacticalConfigPanel.FieldBox("Wyszukaj miejscowość / teren gry:", searchRow));

                var fmtGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                fmtGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.65, GridUnitType.Star) });
                fmtGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                _fmtComboBox = new ComboBox { ItemsSource = SheetFormats, SelectedItem = _selectedSheet ?? SheetFormats[0], Height = 26, FontSize = 10, Tag = "INJECTED" };
                TacticalConfigPanel.ApplyUnifiedComboBoxTheme(_fmtComboBox, win);
                _fmtComboBox.SelectionChanged += (s, e) =>
                {
                    if (_fmtComboBox.SelectedItem is SheetFormatDef sf)
                    {
                        _selectedSheet = sf;
                        if (!_isRebuildingMapControls) TacticalConfigPanel.MarkDirty();
                        ApplyScaleDenominator(_targetScaleDenominator);
                    }
                };
                var fFmt = TacticalConfigPanel.FieldBox("Format arkusza / mapnika:", _fmtComboBox);
                _oriComboBox = (ComboBox)TacticalConfigPanel.ComboCtrl(new[] { "Poziomo", "Pionowo" }, _sheetOrientation, win, v =>
                {
                    _sheetOrientation = v;
                    if (!_isRebuildingMapControls) TacticalConfigPanel.MarkDirty();
                    ApplyScaleDenominator(_targetScaleDenominator);
                });
                var fOri = TacticalConfigPanel.FieldBox("Orientacja:", _oriComboBox);
                Grid.SetColumn(fFmt, 0); Grid.SetColumn(fOri, 1);
                fmtGrid.Children.Add(fFmt); fmtGrid.Children.Add(fOri);
                sec1.Children.Add(fmtGrid);

                sec1.Children.Add(new TextBlock { Text = "Skala mapy (wpisz dowolną 1:X lub wybierz poniżej):", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 10, Margin = new Thickness(0, 2, 0, 3) });
                var scaleCustomGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                scaleCustomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
                scaleCustomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                scaleCustomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
                scaleCustomGrid.Children.Add(new TextBlock { Text = "1 :", Foreground = TacticalConfigPanel.BrushAccent, FontWeight = FontWeights.Bold, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center });
                _scaleInputBox = (TextBox)TacticalConfigPanel.TextCtrl(_targetScaleDenominator.ToString(), v => { });
                Grid.SetColumn(_scaleInputBox, 1); scaleCustomGrid.Children.Add(_scaleInputBox);
                var btnSetScale = MakeActionButton("Ustaw skalę", true, () => { if (int.TryParse(_scaleInputBox.Text.Trim(), out int sc)) { TacticalConfigPanel.MarkDirty(); ApplyScaleDenominator(sc); } });
                btnSetScale.Height = 25; btnSetScale.Margin = new Thickness(6, 0, 0, 0);
                Grid.SetColumn(btnSetScale, 2); scaleCustomGrid.Children.Add(btnSetScale);
                sec1.Children.Add(scaleCustomGrid);

                _scalePresetButtons.Clear();
                var scalePresetsRow = new UniformGrid { Columns = 6, Margin = new Thickness(0, 0, 0, 4) };
                foreach (int p in new[] { 2500, 4000, 5000, 7500, 10000, 15000 })
                {
                    int cap = p;
                    var b = MakeActionButton($"1:{cap}", cap == _targetScaleDenominator, () => { TacticalConfigPanel.MarkDirty(); ApplyScaleDenominator(cap); });
                    b.Height = 24; b.Margin = new Thickness(1); b.FontSize = 9.5;
                    _scalePresetButtons.Add(b);
                    scalePresetsRow.Children.Add(b);
                }
                sec1.Children.Add(scalePresetsRow);
                var btnScale25k = MakeActionButton("1:25000", _targetScaleDenominator == 25000, () => { TacticalConfigPanel.MarkDirty(); ApplyScaleDenominator(25000); });
                btnScale25k.Height = 24; btnScale25k.Margin = new Thickness(1, 0, 1, 6); btnScale25k.FontSize = 10;
                _scalePresetButtons.Add(btnScale25k);
                sec1.Children.Add(btnScale25k);

                var layerRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                layerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                layerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                _layerComboBox = new ComboBox { ItemsSource = Layers, SelectedItem = _activeLayer ?? Layers[0], Height = 26, FontSize = 10.5, Tag = "INJECTED" };
                TacticalConfigPanel.ApplyUnifiedComboBoxTheme(_layerComboBox, win);
                _layerComboBox.SelectionChanged += (s, e) =>
                {
                    if (_layerComboBox.SelectedItem is TacticalMapLayerDef def)
                    {
                        _activeLayer = def;
                        _wmsLoadedScaleDenominator = -1;
                        if (!_isRebuildingMapControls) TacticalConfigPanel.MarkDirty();
                        RefreshTileCanvasesOnly();
                        ScheduleDebouncedTileRender(80);
                    }
                };
                Grid.SetColumn(_layerComboBox, 0);
                layerRow.Children.Add(_layerComboBox);

                var btnReloadTiles = MakeActionButton("🔄 Odśwież", false, () =>
                {
                    _wmsSheetCts?.Cancel();
                    _isWmsSheetFetchRunning = false;
                    PendingTileUrls.Clear();
                    DecodedImageCache.Clear();
                    GeoportalLidarEngine.ClearCache(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                    _geoAnchoredBaseSheets.Clear();
                    _geoAnchoredLidarSheets.Clear();
                    _wmsLoadedScaleDenominator = -1;
                    _wmsLoadedLidarFlag = false;
                    if (_statusText != null)
                        _statusText.Text = "🔄 Zresetowano pulę połączeń TCP HTTP/1.1 — pobieranie świeżego arkusza...";
                    RefreshTileCanvasesOnly();
                    _ = RenderMapTilesAsync();
                });
                btnReloadTiles.Height = 26;
                btnReloadTiles.Padding = new Thickness(8, 0, 8, 0);
                btnReloadTiles.Margin = new Thickness(6, 0, 0, 0);
                btnReloadTiles.ToolTip = "Zresetuj połączenia sieciowe i pobierz mapę na nowo (Z przytrzymanym SHIFT czyści też cache na dysku SSD)";
                Grid.SetColumn(btnReloadTiles, 1);
                layerRow.Children.Add(btnReloadTiles);

                sec1.Children.Add(TacticalConfigPanel.FieldBox("Podkład bazowy (Geoportal WMS HTTP/1.1 / LiDAR EVRF2007 / Google / OSM):", layerRow));

                var lidarBox = new Border { Background = TacticalConfigPanel.BrushInputBg, BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 6, 8, 6) };
                var lidarStack = new StackPanel();
                var chkLidar = new CheckBox { Content = "Nałóż rzeźbę LiDAR ISOK (Single-Shot EVRF2007/KRON86 + Auto-Kontrast 3D)", IsChecked = _showLidarSingleWms, Foreground = TacticalConfigPanel.BrushAccent, FontWeight = FontWeights.SemiBold, FontSize = 10 };
                chkLidar.Checked += (s, e) =>
                {
                    _showLidarSingleWms = true;
                    _wmsLoadedLidarFlag = false;
                    if (!_isRebuildingMapControls) TacticalConfigPanel.MarkDirty();
                    RefreshTileCanvasesOnly();
                    ScheduleDebouncedTileRender(80);
                };
                chkLidar.Unchecked += (s, e) =>
                {
                    _showLidarSingleWms = false;
                    _wmsLoadedLidarFlag = false;
                    if (!_isRebuildingMapControls) TacticalConfigPanel.MarkDirty();
                    _lidarTileCanvas?.Children.Clear();
                };
                lidarStack.Children.Add(chkLidar);
                lidarStack.Children.Add(MakeSliderRow("Przezroczystość rzeźby LiDAR ISOK:", 0.10, 0.95, _lidarOpacity, v => $"{(int)Math.Round(v * 100)}%", v =>
                {
                    _lidarOpacity = v;
                    if (_lidarTileCanvas != null) _lidarTileCanvas.Opacity = _lidarOpacity;
                }));
                lidarBox.Child = lidarStack;
                sec1.Children.Add(lidarBox);
                leftStack.Children.Add(WrapCard(sec1));

                // SEKCJA 2: SIATKA MGRS / WGS84 (NUMERACJA NA LINII) & KOLORY RGB
                leftStack.Children.Add(WrapCard(BuildSection2GridAndColorsUI(win)));

                // SEKCJA 3: ZNACZNIKI, INSPEKTOR WSPÓŁRZĘDNYCH PO KLIKNIĘCIU, OBRACANIE (↻) & STREFY (✥)
                var sec3 = CreateSectionCard("3. ZNACZNIKI, ODCZYT WSPÓŁRZĘDNYCH MARKERA & OBRACANIE (↻)");
                void UpdateToolButtonsVisual()
                {
                    void StyleTool(Button b, bool active)
                    {
                        if (b == null) return;
                        b.Background = active ? TacticalConfigPanel.BrushButtonAccent : TacticalConfigPanel.BrushInputBg;
                        b.Foreground = active ? TacticalConfigPanel.BrushDarkFg : Brushes.White;
                        b.BorderBrush = active ? TacticalConfigPanel.BrushAccent : TacticalConfigPanel.BrushInputBorder;
                    }
                    StyleTool(_btnToolPan, _activeTool == "PAN");
                    StyleTool(_btnToolMarker, _activeTool == "MARKER");
                    StyleTool(_btnToolDot, _activeTool == "DOT");
                    StyleTool(_btnToolAo, _activeTool == "AO");
                    StyleTool(_btnToolSector, _activeTool == "SECTOR");
                    StyleTool(_btnToolRoute, _activeTool == "ROUTE");
                }

                _btnToolPan = MakeActionButton("✋ Przesuwaj Mapę / Kliknij Marker = Odczytaj Współrzędne / Obracaj (↻)", true, () => { _activeTool = "PAN"; _activeDraftPoly = null; _fastOverlayRenderer?.InvalidateVisual(); UpdateToolButtonsVisual(); });
                _btnToolPan.Height = 30; _btnToolPan.Margin = new Thickness(0, 0, 0, 5);
                sec3.Children.Add(_btnToolPan);

                var toolsGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 6) };
                _btnToolMarker = MakeActionButton("➤ Kształt / Namiot / NATO", false, () => { _activeTool = "MARKER"; _activeDraftPoly = null; _fastOverlayRenderer?.InvalidateVisual(); UpdateToolButtonsVisual(); });
                _btnToolDot = MakeActionButton("⚫ Kropka (Kolor RGB)", false, () => { _activeTool = "DOT"; _activeDraftPoly = null; _fastOverlayRenderer?.InvalidateVisual(); UpdateToolButtonsVisual(); });
                _btnToolAo = MakeActionButton("🛡️ Teren Gry (Maska poza AO)", false, () => { _activeTool = "AO"; _activeDraftPoly = null; if (_objectNameInputBox != null) _objectNameInputBox.Text = "Granica Terenu Gry"; _fastOverlayRenderer?.InvalidateVisual(); UpdateToolButtonsVisual(); });
                _btnToolSector = MakeActionButton("⬠ Sektor / Strefa (Wypełniony)", false, () => { _activeTool = "SECTOR"; _activeDraftPoly = null; if (_objectNameInputBox != null) _objectNameInputBox.Text = $"Sektor-{_mapObjects.Count + 1}"; _fastOverlayRenderer?.InvalidateVisual(); UpdateToolButtonsVisual(); });

                _btnToolMarker.Height = 28; _btnToolMarker.Margin = new Thickness(0, 0, 3, 4);
                _btnToolDot.Height = 28; _btnToolDot.Margin = new Thickness(3, 0, 0, 4);
                _btnToolAo.Height = 28; _btnToolAo.Margin = new Thickness(0, 0, 3, 4);
                _btnToolSector.Height = 28; _btnToolSector.Margin = new Thickness(3, 0, 0, 4);

                toolsGrid.Children.Add(_btnToolMarker); toolsGrid.Children.Add(_btnToolDot);
                toolsGrid.Children.Add(_btnToolAo); toolsGrid.Children.Add(_btnToolSector);
                sec3.Children.Add(toolsGrid);

                _btnToolRoute = MakeActionButton("〰 Rysuj Trasę / Linię (Przytrzymaj SHIFT = kąt co 45° / 90°)", false, () => { _activeTool = "ROUTE"; _activeDraftPoly = null; if (_objectNameInputBox != null) _objectNameInputBox.Text = $"Trasa-{_mapObjects.Count + 1}"; _fastOverlayRenderer?.InvalidateVisual(); UpdateToolButtonsVisual(); });
                _btnToolRoute.Height = 26; _btnToolRoute.Margin = new Thickness(0, 0, 0, 6);
                sec3.Children.Add(_btnToolRoute);
                UpdateToolButtonsVisual();

                var inspectorBorder = new Border
                {
                    Background = TacticalConfigPanel.BrushInputBg,
                    BorderBrush = TacticalConfigPanel.BrushAccent,
                    BorderThickness = new Thickness(1.4),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 6, 8, 6),
                    Margin = new Thickness(0, 0, 0, 6)
                };
                var inspStack = new StackPanel();
                var inspTopDock = new DockPanel { Margin = new Thickness(0, 0, 0, 3) };
                var btnCopyCoords = MakeActionButton("📋 Kopiuj meldunek", false, CopySelectedMarkerCoordsToClipboard);
                btnCopyCoords.Height = 20; btnCopyCoords.FontSize = 9; btnCopyCoords.Padding = new Thickness(7, 0, 7, 0); btnCopyCoords.BorderBrush = TacticalConfigPanel.BrushAccent;
                DockPanel.SetDock(btnCopyCoords, Dock.Right);
                inspTopDock.Children.Add(btnCopyCoords);

                _inspectorTitleLabel = new TextBlock
                {
                    Text = "📍 Kliknij dowolny znacznik na mapie, aby odczytać jego współrzędne",
                    Foreground = TacticalConfigPanel.BrushAccent,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                inspTopDock.Children.Add(_inspectorTitleLabel);
                inspStack.Children.Add(inspTopDock);

                _inspectorMgrsLabel = new TextBlock
                {
                    Text = "MGRS: —   |   Keypad / Ćwiartka: —",
                    Foreground = Brushes.White,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 1, 0, 2)
                };
                _inspectorWgsUtmLabel = new TextBlock
                {
                    Text = "WGS84: —   |   UTM: —",
                    Foreground = TacticalConfigPanel.BrushMutedText,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 9.2
                };
                inspStack.Children.Add(_inspectorMgrsLabel);
                inspStack.Children.Add(_inspectorWgsUtmLabel);
                inspectorBorder.Child = inspStack;
                sec3.Children.Add(inspectorBorder);
                UpdateSelectedMarkerInspectorUI();

                var aoMaskRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                aoMaskRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
                aoMaskRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var fAoPat = TacticalConfigPanel.FieldBox("Oznaczenie strefy POZA Terenem Gry (AO):", TacticalConfigPanel.ComboCtrl(new[]
                {
                    "Skośne linie (Hatch ///)",
                    "Symbole X (Strefa No-Go)",
                    "Jednolite tło RGB poza AO",
                    "Tylko linia granicy AO"
                }, _aoOutPattern, win, v => { _aoOutPattern = v; _fastOverlayRenderer?.InvalidateVisual(); }));
                var fAoOp = MakeSliderRow("Krycie maski poza AO:", 0.10, 0.85, _aoOutMaskOpacity, v => $"{(int)Math.Round(v * 100)}%", v => { _aoOutMaskOpacity = Math.Round(v, 2); _fastOverlayRenderer?.InvalidateVisual(); });
                Grid.SetColumn(fAoPat, 0); Grid.SetColumn(fAoOp, 1);
                aoMaskRow.Children.Add(fAoPat); aoMaskRow.Children.Add(fAoOp);
                sec3.Children.Add(aoMaskRow);

                var matchedSymbol = NatoSymbols.FirstOrDefault(n => n.Code == _selectedNatoSymbolCode) ?? NatoSymbols[0];
                var natoCombo = new ComboBox { ItemsSource = NatoSymbols, SelectedItem = matchedSymbol, Height = 26, FontSize = 10.5, Margin = new Thickness(0, 0, 0, 4), Tag = "INJECTED" };
                TacticalConfigPanel.ApplyUnifiedComboBoxTheme(natoCombo, win);
                natoCombo.SelectionChanged += (s, e) =>
                {
                    if (natoCombo.SelectedItem is NatoSymbolOption opt)
                    {
                        _selectedNatoSymbolCode = opt.Code;
                        if (_objectNameInputBox != null && !_isRebuildingMapControls) _objectNameInputBox.Text = opt.ShortBadge;
                        if (!_isRebuildingMapControls) TacticalConfigPanel.MarkDirty();
                    }
                };
                sec3.Children.Add(TacticalConfigPanel.FieldBox("Wybierz kształt (Strzałka, Namiot, Krzyżyk, Koło, Kwadrat, Romb, Trójkąt lub NATO):", natoCombo));

                var objCfgRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                objCfgRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                objCfgRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(155) });
                _objectNameInputBox = (TextBox)TacticalConfigPanel.TextCtrl(_savedDefaultObjectName, v => { _savedDefaultObjectName = v; });

                var mapColorSwatch = new Border { Width = 13, Height = 13, CornerRadius = new CornerRadius(3), Background = TacticalConfigPanel.BrushFromHex(_selectedObjectColorHex, Color.FromRgb(38, 132, 255)), BorderBrush = Brushes.White, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
                var mapColorText = new TextBlock { Text = $"Paleta RGB ({_selectedObjectColorHex})", Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
                var mapColorStack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                mapColorStack.Children.Add(mapColorSwatch); mapColorStack.Children.Add(mapColorText);

                var btnPickMapRgb = new Button { Content = mapColorStack, Height = 25, Background = TacticalConfigPanel.BrushInputBg, BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
                btnPickMapRgb.Click += (s, e) =>
                {
                    string picked = TacticalConfigPanel.ShowRgbColorPickerDialog(win, "Kolor nowego kształtu / znacznika / strefy", _selectedObjectColorHex);
                    if (!string.IsNullOrEmpty(picked))
                    {
                        _selectedObjectColorHex = picked;
                        mapColorSwatch.Background = TacticalConfigPanel.BrushFromHex(picked, Color.FromRgb(38, 132, 255));
                        mapColorText.Text = $"Paleta RGB ({picked.ToUpperInvariant()})";
                        TacticalConfigPanel.MarkDirty();
                    }
                };

                var fNameBox = TacticalConfigPanel.FieldBox("Domyślna nazwa nowego obiektu:", _objectNameInputBox);
                var fRgbBtn = TacticalConfigPanel.FieldBox("Kolor kształtu / maski (RGB):", btnPickMapRgb);
                Grid.SetColumn(fNameBox, 0); Grid.SetColumn(fRgbBtn, 1);
                objCfgRow.Children.Add(fNameBox); objCfgRow.Children.Add(fRgbBtn);
                sec3.Children.Add(objCfgRow);

                var lblTogglesGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 6) };
                var chkGlobalLabels = new CheckBox
                {
                    Content = "Pokazuj etykiety nazw na mapie",
                    IsChecked = _showAllObjectLabels,
                    Foreground = TacticalConfigPanel.BrushAccent,
                    FontSize = 9.5,
                    FontWeight = FontWeights.Bold
                };
                chkGlobalLabels.Checked += (s, e) => { _showAllObjectLabels = true; if (!_isRebuildingMapControls) TacticalConfigPanel.MarkDirty(); RedrawVectorsOnly(); };
                chkGlobalLabels.Unchecked += (s, e) => { _showAllObjectLabels = false; if (!_isRebuildingMapControls) TacticalConfigPanel.MarkDirty(); RedrawVectorsOnly(); };

                var chkRotHandles = new CheckBox
                {
                    Content = "Uchwyty obrotu (↻) na mapie",
                    IsChecked = _showRotationHandlesOnMap,
                    Foreground = Brushes.White,
                    FontSize = 9.5
                };
                chkRotHandles.Checked += (s, e) => { _showRotationHandlesOnMap = true; if (!_isRebuildingMapControls) TacticalConfigPanel.MarkDirty(); RedrawVectorsOnly(); };
                chkRotHandles.Unchecked += (s, e) => { _showRotationHandlesOnMap = false; if (!_isRebuildingMapControls) TacticalConfigPanel.MarkDirty(); RedrawVectorsOnly(); };

                lblTogglesGrid.Children.Add(chkGlobalLabels);
                lblTogglesGrid.Children.Add(chkRotHandles);
                sec3.Children.Add(lblTogglesGrid);

                sec3.Children.Add(new TextBlock { Text = "Naniesione obiekty (kliknij 📍 lub marker na mapie, aby odczytać współrzędne):", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 9.5, Margin = new Thickness(0, 0, 0, 3) });
                var objListBorder = new Border { Background = TacticalConfigPanel.BrushInputBg, BorderBrush = TacticalConfigPanel.BrushInputBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), MinHeight = 68, MaxHeight = 230, Padding = new Thickness(6) };
                var objListScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                _objectsListStack = new StackPanel();
                objListScroll.Content = _objectsListStack; objListBorder.Child = objListScroll;
                sec3.Children.Add(objListBorder);
                RefreshObjectsListUI(win);
                leftStack.Children.Add(WrapCard(sec3));

                // SEKCJA 4: ELEMENTY MAPY & EKSPORT Z WYBOREM MIEJSCA I NAZWY PLIKU (SaveFileDialog)
                var sec4 = CreateSectionCard("4. ELEMENTY MAPY (DRAG & DROP) & EKSPORT Z WYBOREM PLIKU");
                var hudChkGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 6) };
                CheckBox MakeHudChk(string label, bool init, Action<bool> onChg)
                {
                    var c = new CheckBox { Content = label, IsChecked = init, Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 4, 4) };
                    c.Checked += (s, e) => { onChg(true); if (!_isRebuildingMapControls) TacticalConfigPanel.MarkDirty(); RedrawVectorsOnly(); };
                    c.Unchecked += (s, e) => { onChg(false); if (!_isRebuildingMapControls) TacticalConfigPanel.MarkDirty(); RedrawVectorsOnly(); };
                    return c;
                }
                hudChkGrid.Children.Add(MakeHudChk("🧭 Znacznik Północy (N)", _showNorthArrow, v => _showNorthArrow = v));
                hudChkGrid.Children.Add(MakeHudChk("📏 Podziałka Skali", _showScaleBar, v => _showScaleBar = v));
                hudChkGrid.Children.Add(MakeHudChk("📋 Legenda na mapie", _showLegend, v => _showLegend = v));
                hudChkGrid.Children.Add(MakeHudChk("🎖️ Opis symboli w legendzie", _showNatoSymbolsInLegend, v => _showNatoSymbolsInLegend = v));
                sec4.Children.Add(hudChkGrid);

                string[] corners = { "Lewy górny róg", "Prawy górny róg", "Lewy dolny róg", "Prawy dolny róg" };
                var posGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 8) };
                posGrid.Children.Add(TacticalConfigPanel.FieldBox("Pozycja Północy (lub przeciągnij):", TacticalConfigPanel.ComboCtrl(corners, _northArrowPos, win, v => { _northArrowPos = v; _northNormX = null; _northNormY = null; RedrawVectorsOnly(); })));
                posGrid.Children.Add(TacticalConfigPanel.FieldBox("Pozycja Podziałki (lub przeciągnij):", TacticalConfigPanel.ComboCtrl(corners, _scaleBarPos, win, v => { _scaleBarPos = v; _scaleNormX = null; _scaleNormY = null; RedrawVectorsOnly(); })));
                posGrid.Children.Add(TacticalConfigPanel.FieldBox("Pozycja Legendy (lub przeciągnij):", TacticalConfigPanel.ComboCtrl(corners, _legendPos, win, v => { _legendPos = v; _legendNormX = null; _legendNormY = null; RedrawVectorsOnly(); })));
                posGrid.Children.Add(TacticalConfigPanel.FieldBox("Tryb GRG w pliku .KMZ:", TacticalConfigPanel.ComboCtrl(new[] { "Przezroczysta siatka", "Pełny podkład + siatka", "Tylko podkład" }, _kmzGrgMode, win, v => _kmzGrgMode = v)));
                sec4.Children.Add(posGrid);

                var expRow1 = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 6) };
                var btnGpx = MakeActionButton("⌚ Zapisz .GPX jako...", false, async () => await ExportStandaloneFilesAsync(win, "gpx"));
                var btnKmz = MakeActionButton("📦 Zapisz .KMZ jako...", false, async () => await ExportStandaloneFilesAsync(win, "kmz"));
                btnGpx.Height = 30; btnGpx.Margin = new Thickness(0, 0, 3, 0); btnGpx.BorderBrush = TacticalConfigPanel.BrushAccent;
                btnKmz.Height = 30; btnKmz.Margin = new Thickness(3, 0, 0, 0); btnKmz.BorderBrush = TacticalConfigPanel.BrushAccent;
                expRow1.Children.Add(btnGpx); expRow1.Children.Add(btnKmz);
                sec4.Children.Add(expRow1);

                var expRow2 = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 6) };
                var btnPdf = MakeActionButton("🖨️ Zapisz PDF jako...", false, async () => await ExportTacticalMapPdfAsync(win));
                var btnSaveJson = MakeActionButton("💾 Zapisz projekt", false, () => SaveMapProjectJson(win));
                var btnLoadJson = MakeActionButton("📂 Wczytaj", false, () => LoadMapProjectJsonOrKml(win));
                btnPdf.Height = 28; btnPdf.Margin = new Thickness(0, 0, 3, 0); btnPdf.BorderBrush = TacticalConfigPanel.BrushAccent;
                btnSaveJson.Height = 28; btnSaveJson.Margin = new Thickness(2, 0, 2, 0);
                btnLoadJson.Height = 28; btnLoadJson.Margin = new Thickness(3, 0, 0, 0);
                expRow2.Children.Add(btnPdf); expRow2.Children.Add(btnSaveJson); expRow2.Children.Add(btnLoadJson);
                sec4.Children.Add(expRow2);

                var btnAddToFtak = MakeActionButton("⚡ Zapisz arkusz .KMZ / .GPX do paczki .ftak (dla zaznaczonych stron)", true, async () => await ExportMapToFtakSourceAsync(win));
                btnAddToFtak.Height = 30;
                sec4.Children.Add(btnAddToFtak);
                leftStack.Children.Add(WrapCard(sec4));

                // SEKCJA 5: GENERATOR MAP OFFLINE MULTI-ZOOM (.MBTILES / .SQLITE DLA ATAK)
                var sec5 = CreateSectionCard("5. GENERATOR MAP OFFLINE MULTI-ZOOM (.MBTILES / .SQLITE DLA ATAK)");
                sec5.Children.Add(new TextBlock
                {
                    Text = "Pobiera piramidę kafelków na wielu przybliżeniach (np. Zoom 13–17) i buduje bazę danych SQLite do pracy w 100% offline w lesie bez zasięgu LTE.",
                    Foreground = TacticalConfigPanel.BrushMutedText,
                    FontSize = 9.5,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 6)
                });

                var zoomRangeGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 4) };
                zoomRangeGrid.Children.Add(TacticalConfigPanel.FieldBox("Od Zoom (Min — ogólny):", TacticalConfigPanel.ComboCtrl(new[] { "11", "12", "13", "14", "15", "16" }, _offlineMinZoom.ToString(), win, v =>
                {
                    if (int.TryParse(v, out int z)) { _offlineMinZoom = z; if (_offlineMaxZoom < _offlineMinZoom) _offlineMaxZoom = _offlineMinZoom; UpdateOfflineTileEstimateUI(); }
                })));
                zoomRangeGrid.Children.Add(TacticalConfigPanel.FieldBox("Do Zoom (Max — detale):", TacticalConfigPanel.ComboCtrl(new[] { "14", "15", "16", "17", "18", "19" }, _offlineMaxZoom.ToString(), win, v =>
                {
                    if (int.TryParse(v, out int z)) { _offlineMaxZoom = Math.Max(_offlineMinZoom, z); UpdateOfflineTileEstimateUI(); }
                })));
                sec5.Children.Add(zoomRangeGrid);

                var fmtAndBoundsGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 4) };
                fmtAndBoundsGrid.Children.Add(TacticalConfigPanel.FieldBox("Obszar pobierania:", TacticalConfigPanel.ComboCtrl(new[] { "Widoczny arkusz mapy", "Granica Terenu Gry (AO)" }, _offlineBoundsMode, win, v => { _offlineBoundsMode = v; UpdateOfflineTileEstimateUI(); })));
                fmtAndBoundsGrid.Children.Add(TacticalConfigPanel.FieldBox("Format bazy danych ATAK:", TacticalConfigPanel.ComboCtrl(new[] { "MBTiles (.mbtiles — ATAK 5.x)", "ATAK / MOBAC (.sqlite)", "Oba (.mbtiles + .sqlite)" }, _offlineDbFormat, win, v => _offlineDbFormat = v)));
                sec5.Children.Add(fmtAndBoundsGrid);

                var chkBakeLidar = new CheckBox
                {
                    Content = "Wypal rzeźbę LiDAR ISOK bezpośrednio na kafelkach offline (jeśli LiDAR włączony)",
                    IsChecked = _bakeLidarInOffline,
                    Foreground = Brushes.White,
                    FontSize = 9.5,
                    Margin = new Thickness(0, 2, 0, 3)
                };
                chkBakeLidar.Checked += (s, e) => { _bakeLidarInOffline = true; if (!_isRebuildingMapControls) TacticalConfigPanel.MarkDirty(); };
                chkBakeLidar.Unchecked += (s, e) => { _bakeLidarInOffline = false; if (!_isRebuildingMapControls) TacticalConfigPanel.MarkDirty(); };
                sec5.Children.Add(chkBakeLidar);

                var chkIncludeVec = new CheckBox
                {
                    Content = "Dołącz automatycznie warstwy wektorowe (.KMZ / .GPX) obok bazy offline",
                    IsChecked = _includeVectorsWithOffline,
                    Foreground = Brushes.White,
                    FontSize = 9.5,
                    Margin = new Thickness(0, 0, 0, 6)
                };
                chkIncludeVec.Checked += (s, e) => { _includeVectorsWithOffline = true; if (!_isRebuildingMapControls) TacticalConfigPanel.MarkDirty(); };
                chkIncludeVec.Unchecked += (s, e) => { _includeVectorsWithOffline = false; if (!_isRebuildingMapControls) TacticalConfigPanel.MarkDirty(); };
                sec5.Children.Add(chkIncludeVec);

                var estBorder = new Border { Background = TacticalConfigPanel.BrushInputBg, BorderBrush = TacticalConfigPanel.BrushTileBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 0, 6) };
                var estStack = new StackPanel();
                _offlineEstimateLabel = new TextBlock { Text = "Do pobrania: wyliczanie...", Foreground = TacticalConfigPanel.BrushAccent, FontSize = 10, FontWeight = FontWeights.Bold };
                _offlineProgressLabel = new TextBlock { Text = "Gotowy do budowy bazy offline.", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 9.5, Margin = new Thickness(0, 2, 0, 4) };
                _offlineProgressBar = new ProgressBar { Minimum = 0, Maximum = 100, Value = 0, Height = 8, Background = TacticalConfigPanel.BrushItemBg, Foreground = TacticalConfigPanel.BrushButtonAccent, BorderThickness = new Thickness(0) };
                estStack.Children.Add(_offlineEstimateLabel);
                estStack.Children.Add(_offlineProgressLabel);
                estStack.Children.Add(_offlineProgressBar);
                estBorder.Child = estStack;
                sec5.Children.Add(estBorder);

                var offBtnsRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                offBtnsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                offBtnsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

                var btnDownloadStandaloneOffline = MakeActionButton("📥 Zapisz bazę Offline jako...", false, async () => await BuildMultiZoomOfflineDatabaseAsync(win, false));
                btnDownloadStandaloneOffline.Height = 30; btnDownloadStandaloneOffline.Margin = new Thickness(0, 0, 4, 0); btnDownloadStandaloneOffline.BorderBrush = TacticalConfigPanel.BrushAccent;

                var btnCancelOffline = MakeActionButton("⏹ Przerwij", false, () => _offlineDownloadCts?.Cancel());
                btnCancelOffline.Height = 30; btnCancelOffline.Background = TacticalConfigPanel.BrushRedMinus; btnCancelOffline.BorderBrush = Brushes.White;

                Grid.SetColumn(btnDownloadStandaloneOffline, 0); Grid.SetColumn(btnCancelOffline, 1);
                offBtnsRow.Children.Add(btnDownloadStandaloneOffline); offBtnsRow.Children.Add(btnCancelOffline);
                sec5.Children.Add(offBtnsRow);

                var btnBuildOfflineToFtak = MakeActionButton("⚡ Buduj bazę Offline (.mbtiles / .sqlite) prosto do paczki .ftak", true, async () => await BuildMultiZoomOfflineDatabaseAsync(win, true));
                btnBuildOfflineToFtak.Height = 32;
                sec5.Children.Add(btnBuildOfflineToFtak);

                leftStack.Children.Add(WrapCard(sec5));
                _leftControlScrollViewer.Content = leftStack;
                UpdateOfflineTileEstimateUI();
            }
            finally
            {
                _isRebuildingMapControls = false;
            }
        }

        private static Border BuildFullScreenMapGeneratorUI(Window win)
        {
            if (_activeLayer == null) _activeLayer = Layers[0];
            if (_selectedSheet == null) _selectedSheet = SheetFormats[0];

            var rootBorder = new Border { Background = TacticalConfigPanel.BrushCardBg, Tag = "INJECTED_MAP_OVERLAY" };
            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });

            var bodyGrid = new Grid();
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(440) });
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _leftControlScrollViewer = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = TacticalConfigPanel.BrushCardBg, Padding = new Thickness(10) };
            RebuildLeftControlsPanel(win);
            Grid.SetColumn(_leftControlScrollViewer, 0);
            bodyGrid.Children.Add(_leftControlScrollViewer);

            _viewportHostGrid = new Grid
            {
                Background = TacticalConfigPanel.BrushInputBg,
                ClipToBounds = true
            };

            _sheetFrameBorder = new Border
            {
                Width = 820,
                Height = 580,
                Background = TacticalConfigPanel.BrushCardBg,
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(1.5),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                ClipToBounds = true
            };

            _mapCompositeGrid = new Grid { ClipToBounds = true, Background = TacticalConfigPanel.BrushCardBg };
            _tileCanvas = new Canvas { ClipToBounds = true, IsHitTestVisible = false };
            _lidarTileCanvas = new Canvas { ClipToBounds = true, Opacity = _lidarOpacity, IsHitTestVisible = false };
            _dimmerRect = new Rectangle { Fill = Brushes.Black, Opacity = _mapDimmerOpacity, IsHitTestVisible = false };
            _fastOverlayRenderer = new FastTacticalOverlayRenderer(RenderFastGridAndMasksOnContext);
            _interactiveCanvas = new Canvas { Background = Brushes.Transparent, ClipToBounds = true, Cursor = Cursors.Cross };

            _mapCompositeGrid.Children.Add(_tileCanvas);
            _mapCompositeGrid.Children.Add(_lidarTileCanvas);
            _mapCompositeGrid.Children.Add(_dimmerRect);
            _mapCompositeGrid.Children.Add(_fastOverlayRenderer);
            _mapCompositeGrid.Children.Add(_interactiveCanvas);
            _sheetFrameBorder.Child = _mapCompositeGrid;
            _viewportHostGrid.Children.Add(_sheetFrameBorder);

            var topBadgesStack = new StackPanel { VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0), IsHitTestVisible = false };
            var sheetBadgeBorder = new Border { Background = new SolidColorBrush(Color.FromArgb(225, 8, 19, 22)), BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) };
            _sheetInfoBadge = new TextBlock { Text = "Arkusz A4 Poziomo", Foreground = TacticalConfigPanel.BrushAccent, FontSize = 10.5, FontWeight = FontWeights.Bold };
            sheetBadgeBorder.Child = _sheetInfoBadge;
            topBadgesStack.Children.Add(sheetBadgeBorder);

            var hudBorder = new Border { Background = new SolidColorBrush(Color.FromArgb(215, 8, 19, 22)), BorderBrush = TacticalConfigPanel.BrushTileBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Center };
            _coordsBadge = new TextBlock { Text = $"WGS84: {_centerLat:0.00000}, {_centerLon:0.00000}  |  Kliknij marker = Odczytaj współrzędne MGRS  |  ↻ = Obróć", Foreground = Brushes.White, FontSize = 10 };
            hudBorder.Child = _coordsBadge;
            topBadgesStack.Children.Add(hudBorder);
            _viewportHostGrid.Children.Add(topBadgesStack);

            _interactiveCanvas.MouseEnter += (s, e) => _isHoveringCanvas = true;
            _interactiveCanvas.MouseLeave += (s, e) => { _isHoveringCanvas = false; if (_activeDraftPoly != null) _fastOverlayRenderer?.InvalidateVisual(); };

            _interactiveCanvas.MouseLeftButtonDown += (s, e) =>
            {
                if (_draggedHudId != null || _draggedObject != null) return;
                // Natychmiast przerwij trwające w tle zapytanie WMS w chwili dotknięcia mapy myszą!
                _wheelZoomDebounceTimer?.Stop();
                _wmsSheetCts?.Cancel();
                _isWmsSheetFetchRunning = false;

                _isPanningMap = true;
                _mouseDownPoint = e.GetPosition(_interactiveCanvas);
                _dragStartLat = _centerLat;
                _dragStartLon = _centerLon;
                _interactiveCanvas.CaptureMouse();
            };

            _interactiveCanvas.LostMouseCapture += (s, e) =>
            {
                if (_isPanningMap)
                {
                    _isPanningMap = false;
                    RefreshTileCanvasesOnly();
                    RedrawVectorsOnly();
                    UpdateOfflineTileEstimateUI();
                    ScheduleDebouncedTileRender(120);
                }
            };

            _interactiveCanvas.MouseRightButtonDown += (s, e) =>
            {
                if (_activeDraftPoly != null)
                {
                    _activeDraftPoly = null;
                    RefreshObjectsListUI(win);
                    RedrawVectorsOnly();
                    UpdateOfflineTileEstimateUI();
                    TacticalConfigPanel.MarkDirty();
                    return;
                }
                var (w, h) = GetExactCanvasSize();
                Point p = e.GetPosition(_interactiveCanvas);
                var pt = ScreenToLatLon(p.X, p.Y, w, h);
                if (_rulerStart == null || (_rulerStart != null && _rulerEnd != null)) { _rulerStart = pt; _rulerEnd = null; }
                else _rulerEnd = pt;
                _fastOverlayRenderer?.InvalidateVisual();
            };

            _interactiveCanvas.MouseMove += (s, e) =>
            {
                var (w, h) = GetExactCanvasSize();
                Point p = e.GetPosition(_interactiveCanvas);
                _currentHoverScreenPt = p;
                var (hoverLat, hoverLon) = ScreenToLatLon(p.X, p.Y, w, h);
                if (_coordsBadge != null)
                    _coordsBadge.Text = $"WGS84: {hoverLat:0.00000}, {hoverLon:0.00000}  |  Skala 1:{_targetScaleDenominator:N0} (Kratka MGRS: {_effectiveMgrsStepMeters} m)  |  Kliknij marker = Współrzędne";

                if (_draggedHudId != null && _draggedHudElement != null)
                {
                    double nx = Math.Clamp(p.X - _hudStartOffset.X, 4, Math.Max(10, w - _draggedHudElement.Width - 4));
                    double ny = Math.Clamp(p.Y - _hudStartOffset.Y, 4, Math.Max(10, h - _draggedHudElement.Height - 4));
                    Canvas.SetLeft(_draggedHudElement, nx);
                    Canvas.SetTop(_draggedHudElement, ny);
                    if (_draggedHudId == "NORTH") { _northNormX = nx / w; _northNormY = ny / h; }
                    else if (_draggedHudId == "SCALE") { _scaleNormX = nx / w; _scaleNormY = ny / h; }
                    else if (_draggedHudId == "LEGEND") { _legendNormX = nx / w; _legendNormY = ny / h; }
                    TacticalConfigPanel.MarkDirty();
                    return;
                }

                if (_draggedObject != null && _draggedVertexIndex == -3)
                {
                    double rawAngle = Math.Atan2(p.Y - _rotationCenterScreenPt.Y, p.X - _rotationCenterScreenPt.X) * 180.0 / Math.PI + 90.0;
                    double normAngle = (rawAngle % 360.0 + 360.0) % 360.0;
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                        normAngle = (Math.Round(normAngle / 15.0) * 15.0) % 360.0;

                    _draggedObject.RotationDeg = Math.Round(normAngle);
                    TacticalConfigPanel.MarkDirty();
                    RedrawVectorsOnly();
                    return;
                }

                if (_draggedObject != null && _draggedVertexIndex == -2 && _wholeShapeOriginalPoints != null)
                {
                    double dLat = hoverLat - _wholeShapeDragStartLatLon.Lat;
                    double dLon = hoverLon - _wholeShapeDragStartLatLon.Lon;
                    for (int i = 0; i < _wholeShapeOriginalPoints.Count && i < _draggedObject.Points.Count; i++)
                    {
                        _draggedObject.Points[i] = (
                            Math.Clamp(_wholeShapeOriginalPoints[i].Lat + dLat, -85.0, 85.0),
                            Math.Clamp(_wholeShapeOriginalPoints[i].Lon + dLon, -180.0, 180.0));
                    }
                    TacticalConfigPanel.MarkDirty();
                    UpdateSelectedMarkerInspectorUI();
                    RedrawVectorsOnly();
                    return;
                }

                if (_draggedObject != null && _draggedVertexIndex >= 0 && _draggedVertexIndex < _draggedObject.Points.Count)
                {
                    Point targetScreenPt = new Point(p.X - _markerDragScreenOffset.X, p.Y - _markerDragScreenOffset.Y);

                    if (_draggedObject.Points.Count >= 2 && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                    {
                        int refIdx = _draggedVertexIndex > 0 ? _draggedVertexIndex - 1 : 1;
                        Point refScreenPt = LatLonToScreen(_draggedObject.Points[refIdx].Lat, _draggedObject.Points[refIdx].Lon, w, h);
                        targetScreenPt = ApplyShift45DegreeSnap(refScreenPt, targetScreenPt, out bool _, out int _);
                    }

                    _draggedObject.Points[_draggedVertexIndex] = ScreenToLatLon(targetScreenPt.X, targetScreenPt.Y, w, h);
                    TacticalConfigPanel.MarkDirty();
                    UpdateSelectedMarkerInspectorUI();
                    RedrawVectorsOnly();
                    return;
                }

                if (_activeDraftPoly != null && _activeDraftPoly.Points.Count > 0)
                {
                    _fastOverlayRenderer?.InvalidateVisual();
                }

                if (_isPanningMap && _activeTool == "PAN")
                {
                    double dx = p.X - _mouseDownPoint.X, dy = p.Y - _mouseDownPoint.Y;
                    double scaledWorldSize = GetScaledTileSizePx() * Math.Pow(2, _zoom);
                    _centerLon = Math.Clamp(_dragStartLon - dx * (360.0 / scaledWorldSize), -180.0, 180.0);
                    _centerLat = Math.Clamp(_dragStartLat + dy * ((360.0 / scaledWorldSize) * Math.Cos(_dragStartLat * Math.PI / 180.0)), -85.0, 85.0);

                    _fastOverlayRenderer?.InvalidateVisual();
                    RefreshTileCanvasesOnly();
                    if ((DateTime.UtcNow - _lastPanTileRefreshUtc).TotalMilliseconds >= 32)
                    {
                        _lastPanTileRefreshUtc = DateTime.UtcNow;
                        RedrawVectorsOnly();
                    }
                }
            };

            _interactiveCanvas.MouseLeftButtonUp += (s, e) =>
            {
                if (_draggedHudId != null)
                {
                    _draggedHudId = null;
                    _draggedHudElement = null;
                    _interactiveCanvas.ReleaseMouseCapture();
                    TacticalConfigPanel.MarkDirty();
                    return;
                }
                if (_draggedObject != null)
                {
                    bool wasRotating = (_draggedVertexIndex == -3);
                    _draggedObject = null;
                    _draggedVertexIndex = -1;
                    _wholeShapeOriginalPoints = null;
                    _interactiveCanvas.ReleaseMouseCapture();
                    if (wasRotating) RefreshObjectsListUI(win);
                    UpdateSelectedMarkerInspectorUI();
                    RedrawVectorsOnly();
                    UpdateOfflineTileEstimateUI();
                    TacticalConfigPanel.MarkDirty();
                    return;
                }
                if (!_isPanningMap) return;
                _isPanningMap = false;
                _interactiveCanvas.ReleaseMouseCapture();

                Point endPt = e.GetPosition(_interactiveCanvas);
                if ((endPt - _mouseDownPoint).Length < 5.0 && _activeTool != "PAN")
                {
                    HandleMapClickToolAction(win, _mouseDownPoint);
                    UpdateSelectedMarkerInspectorUI();
                    RedrawVectorsOnly();
                    UpdateOfflineTileEstimateUI();
                }
                else
                {
                    TacticalConfigPanel.MarkDirty();
                    RefreshTileCanvasesOnly();
                    RedrawVectorsOnly();
                    UpdateOfflineTileEstimateUI();
                    ScheduleDebouncedTileRender(90);
                }
            };

            _interactiveCanvas.MouseWheel += (s, e) =>
            {
                // Natychmiast anuluj poprzednie pobieranie i przeskaluj obraz na karcie graficznej (0 ms)
                _wmsSheetCts?.Cancel();
                _isWmsSheetFetchRunning = false;

                double factor = e.Delta > 0 ? 0.8 : 1.25;
                int newScale = (int)(Math.Round((_targetScaleDenominator * factor) / 100.0) * 100);
                newScale = Math.Clamp(newScale, 500, 100000);
                _targetScaleDenominator = newScale;

                if (_scaleInputBox != null)
                    _scaleInputBox.Text = _targetScaleDenominator.ToString();

                UpdateSheetViewportSize();
                double targetMetersPerPx = GetExactMetersPerPixel();
                double equatorMetersPerPx = 156543.03392 * Math.Cos(_centerLat * Math.PI / 180.0);
                _zoom = Math.Clamp((int)Math.Round(Math.Log(equatorMetersPerPx / targetMetersPerPx, 2.0)), 5, 19);

                UpdateScalePresetButtonHighlights();
                RefreshTileCanvasesOnly();
                RedrawVectorsOnly();

                // Wyślij zapytanie do Geoportalu dopiero gdy użytkownik przestanie kręcić rolką na 240 ms!
                ScheduleDebouncedTileRender(240);
            };

            _viewportHostGrid.SizeChanged += (s, e) =>
            {
                UpdateSheetViewportSize();
                ScheduleDebouncedTileRender(180);
            };

            Grid.SetColumn(_viewportHostGrid, 1);
            bodyGrid.Children.Add(_viewportHostGrid);
            Grid.SetRow(bodyGrid, 0);
            mainGrid.Children.Add(bodyGrid);

            var bottomBar = new Border { Background = TacticalConfigPanel.BrushInputBg, BorderBrush = TacticalConfigPanel.BrushCardBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(16, 0, 16, 0) };
            _statusText = new TextBlock { Text = "Gotowy. Czysty protokół HTTP/1.1 + Single-Shot WMS/LiDAR + Brak zapytań w trakcie ruchu myszą + Auto-Reset gniazd TCP.", Foreground = TacticalConfigPanel.BrushAccent, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            bottomBar.Child = _statusText; Grid.SetRow(bottomBar, 1); mainGrid.Children.Add(bottomBar);

            UpdateOfflineTileEstimateUI();
            rootBorder.Child = mainGrid;
            return rootBorder;
        }

        private static (double North, double South, double East, double West) GetTargetOfflineBoundsLatLon()
        {
            var aoPoly = _mapObjects.FirstOrDefault(o => o.Kind == "AO" && o.Points.Count >= 3);
            if (_offlineBoundsMode.Contains("AO") && aoPoly != null)
            {
                double n = aoPoly.Points.Max(p => p.Lat);
                double s = aoPoly.Points.Min(p => p.Lat);
                double e = aoPoly.Points.Max(p => p.Lon);
                double w = aoPoly.Points.Min(p => p.Lon);
                return (n, s, e, w);
            }

            var (vw, vh) = GetExactCanvasSize();
            var (nwLat, nwLon) = ScreenToLatLon(0, 0, vw, vh);
            var (seLat, seLon) = ScreenToLatLon(vw, vh, vw, vh);
            return (Math.Max(nwLat, seLat), Math.Min(nwLat, seLat), Math.Max(nwLon, seLon), Math.Min(nwLon, seLon));
        }

        private static List<(int Z, int X, int Y)> CalculateOfflineTilePyramidCoords(int minZ, int maxZ, out double north, out double south, out double east, out double west)
        {
            (north, south, east, west) = GetTargetOfflineBoundsLatLon();
            var list = new List<(int Z, int X, int Y)>();
            int zStart = Math.Min(minZ, maxZ);
            int zEnd = Math.Max(minZ, maxZ);

            for (int z = zStart; z <= zEnd; z++)
            {
                var (x1, y1) = LatLonToTileXY(north, west, z);
                var (x2, y2) = LatLonToTileXY(south, east, z);
                int maxIdx = (1 << z) - 1;
                int minTx = Math.Clamp((int)Math.Floor(Math.Min(x1, x2)), 0, maxIdx);
                int maxTx = Math.Clamp((int)Math.Floor(Math.Max(x1, x2)), 0, maxIdx);
                int minTy = Math.Clamp((int)Math.Floor(Math.Min(y1, y2)), 0, maxIdx);
                int maxTy = Math.Clamp((int)Math.Floor(Math.Max(y1, y2)), 0, maxIdx);

                for (int tx = minTx; tx <= maxTx; tx++)
                {
                    for (int ty = minTy; ty <= maxTy; ty++)
                    {
                        list.Add((z, tx, ty));
                        if (list.Count >= 4500) return list;
                    }
                }
            }
            return list;
        }

        private static void UpdateOfflineTileEstimateUI()
        {
            if (_offlineEstimateLabel == null) return;
            try
            {
                var coords = CalculateOfflineTilePyramidCoords(_offlineMinZoom, _offlineMaxZoom, out double _, out double _, out double _, out double _);
                double estMb = (coords.Count * 26.0) / 1024.0;
                string capNote = coords.Count >= 4500 ? " [Limit bezpieczeństwa 4500]" : "";
                _offlineEstimateLabel.Text = $"Do pobrania: {coords.Count} kafelków (Zoom {_offlineMinZoom}–{_offlineMaxZoom}){capNote}  •  ~{estMb:0.0} MB";
            }
            catch { }
        }

        private static byte[] BlendBaseAndLidarTilesOnUiThread(byte[] baseBytes, byte[] lidarBytes, double lidarAlpha)
        {
            try
            {
                BitmapImage bmpBase = new BitmapImage();
                using (var ms1 = new MemoryStream(baseBytes)) { bmpBase.BeginInit(); bmpBase.CacheOption = BitmapCacheOption.OnLoad; bmpBase.StreamSource = ms1; bmpBase.EndInit(); bmpBase.Freeze(); }

                BitmapImage bmpLidarRaw = new BitmapImage();
                using (var ms2 = new MemoryStream(lidarBytes)) { bmpLidarRaw.BeginInit(); bmpLidarRaw.CacheOption = BitmapCacheOption.OnLoad; bmpLidarRaw.StreamSource = ms2; bmpLidarRaw.EndInit(); bmpLidarRaw.Freeze(); }

                BitmapSource alphaLidar = GeoportalLidarEngine.ConvertLidarToAlphaOverlay(bmpLidarRaw) ?? bmpLidarRaw;

                var dv = new DrawingVisual();
                using (DrawingContext dc = dv.RenderOpen())
                {
                    dc.DrawImage(bmpBase, new Rect(0, 0, 256, 256));
                    dc.PushOpacity(lidarAlpha);
                    dc.DrawImage(alphaLidar, new Rect(0, 0, 256, 256));
                    dc.Pop();
                }
                var rtb = new RenderTargetBitmap(256, 256, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                var enc = new JpegBitmapEncoder { QualityLevel = 90 };
                enc.Frames.Add(BitmapFrame.Create(rtb));
                using var outMs = new MemoryStream();
                enc.Save(outMs);
                return outMs.ToArray();
            }
            catch { return baseBytes; }
        }

        private static async Task<byte[]> FetchSingleOfflineTileBytesAsync(TacticalMapLayerDef layer, int tx, int ty, int zoom, CancellationToken ct)
        {
            if (layer.Id == "geoportal_lidar")
            {
                var (latNorth, lonWest) = TileXYToLatLon(tx, ty, zoom);
                var (latSouth, lonEast) = TileXYToLatLon(tx + 1, ty + 1, zoom);
                int reqSz = int.TryParse(GetOptimalWmsTilePixelSize("geoportal_lidar", zoom), out int parsedSz) ? parsedSz : 512;
                string[] lidarUrls = GeoportalLidarEngine.BuildLidarCandidateUrls(latNorth, latSouth, lonEast, lonWest, reqSz, reqSz);

                string primaryKey = lidarUrls[0];
                if (RawTileCache.TryGetValue(primaryKey, out var cachedLidar) && IsValidImagePayload(cachedLidar))
                    return cachedLidar;

                var lidarBmp = await FetchDecodedImageAsync(
                    lidarUrls[0],
                    lidarUrls.Length > 1 ? lidarUrls[1] : null,
                    ct,
                    lidarUrls.Length > 2 ? lidarUrls[2] : null,
                    true);

                if (RawTileCache.TryGetValue(primaryKey, out var fetchedLidar) && IsValidImagePayload(fetchedLidar))
                    return fetchedLidar;

                if (lidarBmp != null)
                {
                    try
                    {
                        var enc = new JpegBitmapEncoder { QualityLevel = 90 };
                        enc.Frames.Add(BitmapFrame.Create(lidarBmp));
                        using var ms = new MemoryStream();
                        enc.Save(ms);
                        return ms.ToArray();
                    }
                    catch { }
                }
                return null;
            }

            string url = BuildTileUrl(layer, tx, ty, zoom);
            if (RawTileCache.TryGetValue(url, out var cached) && IsValidImagePayload(cached))
                return cached;

            string fallbackUrl = BuildFallbackTileUrl(layer, tx, ty, zoom);
            string secondFallbackUrl = null;
            if (layer.IsWmsEpsg3857 && !string.IsNullOrEmpty(layer.SecondFallbackUrlTemplate))
            {
                string b3857 = GetEpsg3857BBox(tx, ty, zoom);
                string b2180NE = GetEpsg2180TileBBoxNE(tx, ty, zoom);
                string b2180EN = GetEpsg2180TileBBox(tx, ty, zoom);
                string b4326 = GetEpsg4326TileBBox(tx, ty, zoom);
                string sz = GetOptimalWmsTilePixelSize(layer.Id, zoom);
                secondFallbackUrl = FillAllBBoxTokens(layer.SecondFallbackUrlTemplate, b3857, b2180NE, b2180EN, b4326, sz, sz);
            }

            var bmp = await FetchDecodedImageAsync(url, fallbackUrl, ct, secondFallbackUrl, true);
            if (RawTileCache.TryGetValue(url, out var fetchedBytes) && IsValidImagePayload(fetchedBytes))
                return fetchedBytes;

            if (bmp != null)
            {
                try
                {
                    var enc = new JpegBitmapEncoder { QualityLevel = 90 };
                    enc.Frames.Add(BitmapFrame.Create(bmp));
                    using var ms = new MemoryStream();
                    enc.Save(ms);
                    return ms.ToArray();
                }
                catch { }
            }
            return null;
        }

        private static async Task BuildMultiZoomOfflineDatabaseAsync(Window win, bool saveDirectlyToFtakSource)
        {
            _offlineDownloadCts?.Cancel();
            var cts = new CancellationTokenSource();
            _offlineDownloadCts = cts;
            var ct = cts.Token;

            try
            {
                var coords = CalculateOfflineTilePyramidCoords(_offlineMinZoom, _offlineMaxZoom, out double north, out double south, out double east, out double west);
                if (coords.Count == 0) return;

                string sourceDir = TacticalConfigPanel.GetPrimarySourceDirectory(win);
                string eventName = TacticalConfigPanel.GetEventNameFromWindow(win);
                var allConfigured = TacticalConfigPanel.GetConfiguredFactions();
                var targetFacs = GetActiveMapFactions();
                string facSuffix = string.Join("_", targetFacs.Select(f => TacticalConfigPanel.SanitizeFileName(f.Name)));
                string baseFileName = $"Offline_{eventName}_{facSuffix}_Z{_offlineMinZoom}_{_offlineMaxZoom}";

                bool writeMbtiles = _offlineDbFormat.Contains(".mbtiles") || _offlineDbFormat.StartsWith("Oba");
                bool writeSqlite = _offlineDbFormat.Contains(".sqlite") || _offlineDbFormat.StartsWith("Oba");

                string standaloneTargetFile = "";
                if (!saveDirectlyToFtakSource)
                {
                    string initDir = !string.IsNullOrWhiteSpace(TacticalConfigPanel.LastExportDirectory) && Directory.Exists(TacticalConfigPanel.LastExportDirectory)
                        ? TacticalConfigPanel.LastExportDirectory
                        : (System.IO.Path.GetDirectoryName(System.IO.Path.TrimEndingDirectorySeparator(sourceDir)) ?? AppDomain.CurrentDomain.BaseDirectory);

                    var dlg = new SaveFileDialog
                    {
                        Title = "Wybierz miejsce i nazwę zapisu bazy mapy Offline dla ATAK",
                        Filter = writeMbtiles && !writeSqlite
                            ? "Baza MBTiles (*.mbtiles)|*.mbtiles"
                            : (!writeMbtiles && writeSqlite ? "Baza ATAK SQLite (*.sqlite)|*.sqlite" : "Baza mapy Offline (*.mbtiles;*.sqlite)|*.mbtiles;*.sqlite"),
                        InitialDirectory = initDir,
                        FileName = writeMbtiles ? $"{baseFileName}.mbtiles" : $"{baseFileName}.sqlite"
                    };
                    if (dlg.ShowDialog(win) != true) return;
                    standaloneTargetFile = dlg.FileName;
                    TacticalConfigPanel.LastExportDirectory = System.IO.Path.GetDirectoryName(standaloneTargetFile) ?? initDir;
                    TacticalConfigPanel.SaveSessionStateAuto(win);
                }

                if (_offlineProgressBar != null) { _offlineProgressBar.Maximum = coords.Count; _offlineProgressBar.Value = 0; }
                if (_offlineProgressLabel != null) _offlineProgressLabel.Text = $"Pobieranie {coords.Count} kafelków (Zoom {_offlineMinZoom}–{_offlineMaxZoom})...";

                var lidarLayer = Layers.First(l => l.Id == "geoportal_lidar");
                bool doBakeLidar = _bakeLidarInOffline && _showLidarSingleWms && _activeLayer.Id != "geoportal_lidar";

                var downloadedTiles = new ConcurrentBag<(int Z, int X, int Y, byte[] Data)>();
                int completed = 0;

                var jobs = coords.Select(async coord =>
                {
                    if (ct.IsCancellationRequested) return;
                    byte[] baseData = await FetchSingleOfflineTileBytesAsync(_activeLayer, coord.X, coord.Y, coord.Z, ct);
                    if (baseData != null && doBakeLidar && !ct.IsCancellationRequested)
                    {
                        byte[] lidarData = await FetchSingleOfflineTileBytesAsync(lidarLayer, coord.X, coord.Y, coord.Z, ct);
                        if (lidarData != null && Application.Current != null)
                        {
                            baseData = await Application.Current.Dispatcher.InvokeAsync(
                                () => BlendBaseAndLidarTilesOnUiThread(baseData, lidarData, _lidarOpacity),
                                DispatcherPriority.Background);
                        }
                    }
                    if (baseData != null)
                        downloadedTiles.Add((coord.Z, coord.X, coord.Y, baseData));

                    int done = Interlocked.Increment(ref completed);
                    if (done % 5 == 0 || done == coords.Count)
                    {
                        _ = Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (_offlineProgressBar != null) _offlineProgressBar.Value = done;
                            if (_offlineProgressLabel != null) _offlineProgressLabel.Text = $"Pobrano {done} / {coords.Count} kafelków ({(done * 100) / Math.Max(1, coords.Count)}%)...";
                        }));
                    }
                });

                await Task.WhenAll(jobs);
                if (ct.IsCancellationRequested)
                {
                    if (_offlineProgressLabel != null) _offlineProgressLabel.Text = "⏹ Przerwano pobieranie mapy offline.";
                    return;
                }

                var finalTilesList = downloadedTiles.ToList();
                if (_offlineProgressLabel != null) _offlineProgressLabel.Text = $"Zapisywanie {finalTilesList.Count} kafelków do bazy SQLite...";

                if (saveDirectlyToFtakSource)
                {
                    await Task.Run(() =>
                    {
                        if (targetFacs.Count > 0 && targetFacs.Count < allConfigured.Count)
                        {
                            foreach (var fac in targetFacs)
                            {
                                string sl = TacticalConfigPanel.SanitizeFileName(fac.Name).ToLowerInvariant();
                                string facDir = System.IO.Path.Combine(sourceDir, "factions", sl);
                                Directory.CreateDirectory(facDir);
                                if (writeMbtiles)
                                    NativeSqliteWriter.WriteMbTilesDatabase(System.IO.Path.Combine(facDir, $"mapa_offline_{sl}.mbtiles"), $"{eventName}_{fac.Name}", _offlineMinZoom, _offlineMaxZoom, north, south, east, west, finalTilesList);
                                if (writeSqlite)
                                    NativeSqliteWriter.WriteAtakMobacSqliteDatabase(System.IO.Path.Combine(facDir, $"mapa_offline_{sl}.sqlite"), _offlineMinZoom, _offlineMaxZoom, finalTilesList);
                            }
                        }
                        else
                        {
                            string mapsDir = System.IO.Path.Combine(sourceDir, "maps");
                            Directory.CreateDirectory(mapsDir);
                            if (writeMbtiles)
                                NativeSqliteWriter.WriteMbTilesDatabase(System.IO.Path.Combine(mapsDir, $"{baseFileName}.mbtiles"), eventName, _offlineMinZoom, _offlineMaxZoom, north, south, east, west, finalTilesList);
                            if (writeSqlite)
                                NativeSqliteWriter.WriteAtakMobacSqliteDatabase(System.IO.Path.Combine(mapsDir, $"{baseFileName}.sqlite"), _offlineMinZoom, _offlineMaxZoom, finalTilesList);
                        }
                    });

                    if (_includeVectorsWithOffline)
                        await ExportMapToFtakSourceAsync(win);
                    else
                        await TacticalConfigPanel.RefreshSourceSizeFromExternalAsync(win);

                    if (_offlineProgressLabel != null) _offlineProgressLabel.Text = $"✓ Zapisano bazę offline ({finalTilesList.Count} kafelków) w folderze source paczki .ftak!";
                    if (_statusText != null) _statusText.Text = $"✓ Zbudowano wielowarstwową mapę offline ({finalTilesList.Count} kafelków, Zoom {_offlineMinZoom}–{_offlineMaxZoom}) bezpośrednio do paczki .ftak!";
                }
                else
                {
                    string outDir = System.IO.Path.GetDirectoryName(standaloneTargetFile) ?? AppDomain.CurrentDomain.BaseDirectory;
                    string stem = System.IO.Path.Combine(outDir, System.IO.Path.GetFileNameWithoutExtension(standaloneTargetFile));

                    await Task.Run(() =>
                    {
                        if (writeMbtiles)
                            NativeSqliteWriter.WriteMbTilesDatabase($"{stem}.mbtiles", System.IO.Path.GetFileNameWithoutExtension(standaloneTargetFile), _offlineMinZoom, _offlineMaxZoom, north, south, east, west, finalTilesList);
                        if (writeSqlite)
                            NativeSqliteWriter.WriteAtakMobacSqliteDatabase($"{stem}.sqlite", _offlineMinZoom, _offlineMaxZoom, finalTilesList);
                    });

                    TacticalConfigPanel.TryOpenFolderInExplorer(outDir);
                    if (_offlineProgressLabel != null) _offlineProgressLabel.Text = $"✓ Zapisano bazę offline ({finalTilesList.Count} kafelków): {standaloneTargetFile}";
                    if (_statusText != null) _statusText.Text = $"✓ Wyeksportowano bazę offline do: {standaloneTargetFile}";
                }
            }
            catch (Exception ex)
            {
                if (_offlineProgressLabel != null) _offlineProgressLabel.Text = $"Błąd zapisu: {ex.Message}";
            }
        }

        private static UIElement MakeSliderRow(string label, double min, double max, double initVal, Func<double, string> fmt, Action<double> onVal)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 2, 0, 4) };
            var dock = new DockPanel { Margin = new Thickness(0, 0, 0, 1) };
            var valTxt = new TextBlock { Text = fmt(initVal), Foreground = TacticalConfigPanel.BrushAccent, FontWeight = FontWeights.Bold, FontSize = 10 };
            DockPanel.SetDock(valTxt, Dock.Right);
            dock.Children.Add(valTxt);
            dock.Children.Add(new TextBlock { Text = label, Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 9.5, TextTrimming = TextTrimming.CharacterEllipsis });
            sp.Children.Add(dock);

            var sl = new Slider
            {
                Minimum = min,
                Maximum = max,
                Value = initVal,
                IsMoveToPointEnabled = true,
                SmallChange = (max - min) / 100.0,
                LargeChange = (max - min) / 20.0,
                Cursor = Cursors.Hand
            };

            void SetSliderFromMouse(MouseEventArgs e)
            {
                if (sl.ActualWidth <= 8) return;
                double ratio = Math.Clamp(e.GetPosition(sl).X / sl.ActualWidth, 0.0, 1.0);
                sl.Value = min + ratio * (max - min);
            }

            sl.PreviewMouseLeftButtonDown += (s, e) => SetSliderFromMouse(e);
            sl.PreviewMouseMove += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) SetSliderFromMouse(e); };
            sl.ValueChanged += (s, e) => { valTxt.Text = fmt(e.NewValue); if (!_isRebuildingMapControls) TacticalConfigPanel.MarkDirty(); onVal(e.NewValue); };
            sp.Children.Add(sl);
            return sp;
        }

        private static Button MakeActionButton(string text, bool isPrimaryAccent, Action onClick)
        {
            var b = new Button
            {
                Content = text,
                Background = isPrimaryAccent ? TacticalConfigPanel.BrushButtonAccent : TacticalConfigPanel.BrushInputBg,
                Foreground = isPrimaryAccent ? TacticalConfigPanel.BrushDarkFg : Brushes.White,
                BorderBrush = isPrimaryAccent ? TacticalConfigPanel.BrushAccent : TacticalConfigPanel.BrushInputBorder,
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.Bold,
                FontSize = 10.5,
                Cursor = Cursors.Hand
            };
            b.Click += (s, e) => onClick();
            return b;
        }

        private static void HandleMapClickToolAction(Window win, Point clickPt)
        {
            var (w, h) = GetExactCanvasSize();
            string objName = string.IsNullOrWhiteSpace(_objectNameInputBox?.Text) ? $"Punkt-{_mapObjects.Count + 1}" : _objectNameInputBox.Text.Trim();

            if (_activeTool == "MARKER" || _activeTool == "DOT")
            {
                var (clickLat, clickLon) = ScreenToLatLon(clickPt.X, clickPt.Y, w, h);
                var item = new TacticalMapObjectItem
                {
                    Kind = _activeTool == "MARKER" ? "Marker" : "Dot",
                    SymbolCode = _activeTool == "MARKER" ? _selectedNatoSymbolCode : "DOT",
                    Name = objName,
                    ColorHex = _selectedObjectColorHex,
                    ShowLabel = _newObjectShowLabelDefault,
                    SizeMultiplier = 1.0,
                    RotationDeg = 0.0
                };
                item.Points.Add((clickLat, clickLon));
                _mapObjects.Add(item);
                _inspectedObject = item;
                _inspectedVertexIndex = 0;
            }
            else if (_activeTool == "AO" || _activeTool == "SECTOR" || _activeTool == "ROUTE")
            {
                string kind = _activeTool == "AO" ? "AO" : (_activeTool == "SECTOR" ? "Sector" : "Route");
                if (_activeDraftPoly == null || _activeDraftPoly.Kind != kind)
                {
                    _activeDraftPoly = new TacticalMapObjectItem
                    {
                        Kind = kind,
                        SymbolCode = kind,
                        Name = kind == "AO" ? "Granica Terenu Gry (AO)" : objName,
                        ColorHex = _selectedObjectColorHex,
                        ShowLabel = _newObjectShowLabelDefault,
                        SizeMultiplier = 1.0,
                        RotationDeg = 0.0
                    };
                    _mapObjects.Add(_activeDraftPoly);
                }

                Point effectivePt = clickPt;
                if (_activeDraftPoly.Points.Count > 0)
                {
                    var lastLatLon = _activeDraftPoly.Points[_activeDraftPoly.Points.Count - 1];
                    Point lastScreenPt = LatLonToScreen(lastLatLon.Lat, lastLatLon.Lon, w, h);
                    effectivePt = ApplyShift45DegreeSnap(lastScreenPt, clickPt, out bool _, out int _);
                }

                var (ptLat, ptLon) = ScreenToLatLon(effectivePt.X, effectivePt.Y, w, h);
                _activeDraftPoly.Points.Add((ptLat, ptLon));
                _inspectedObject = _activeDraftPoly;
                _inspectedVertexIndex = _activeDraftPoly.Points.Count - 1;
            }
            TacticalConfigPanel.MarkDirty();
            UpdateSelectedMarkerInspectorUI();
            RefreshObjectsListUI(win);
        }

        private static string FormatObjectSizeLabel(TacticalMapObjectItem obj)
        {
            double m = obj.SizeMultiplier > 0.1 ? obj.SizeMultiplier : 1.0;
            if (obj.Kind == "Dot")
            {
                double px = Math.Round(_dotMarkerSizePx * m);
                return $"{px:0} px ({m:0.1}x)";
            }
            return $"{m:0.2}x";
        }

        private static void RefreshObjectsListUI(Window win)
        {
            if (_objectsListStack == null) return;
            _objectsListStack.Children.Clear();
            if (_mapObjects.Count == 0)
            {
                _objectsListStack.Children.Add(new TextBlock { Text = "Brak naniesionych obiektów", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 8) });
                return;
            }

            foreach (var obj in _mapObjects.ToList())
            {
                var captured = obj;
                if (captured.SizeMultiplier <= 0.1) captured.SizeMultiplier = 1.0;

                var itemCard = new Border
                {
                    Background = TacticalConfigPanel.BrushItemBg,
                    BorderBrush = (_inspectedObject == captured) ? TacticalConfigPanel.BrushAccent : TacticalConfigPanel.BrushTileBorder,
                    BorderThickness = new Thickness((_inspectedObject == captured) ? 1.5 : 1.0),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(6, 4, 6, 4),
                    Margin = new Thickness(0, 0, 0, 5)
                };

                var itemStack = new StackPanel();

                var topRow = new Grid { Margin = new Thickness(0, 0, 0, 3) };
                topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
                topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
                topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
                topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
                topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });

                var chkShowLbl = new CheckBox
                {
                    IsChecked = captured.ShowLabel,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Cursor = Cursors.Hand,
                    ToolTip = "Pokaż / ukryj etykietę tekstową tego obiektu na mapie"
                };
                chkShowLbl.Checked += (s, e) => { captured.ShowLabel = true; if (!_isRebuildingMapControls) TacticalConfigPanel.MarkDirty(); RedrawVectorsOnly(); };
                chkShowLbl.Unchecked += (s, e) => { captured.ShowLabel = false; if (!_isRebuildingMapControls) TacticalConfigPanel.MarkDirty(); RedrawVectorsOnly(); };
                Grid.SetColumn(chkShowLbl, 0);
                topRow.Children.Add(chkShowLbl);

                var btnCol = new Button
                {
                    Width = 18,
                    Height = 18,
                    Background = TacticalConfigPanel.BrushFromHex(captured.ColorHex, Color.FromRgb(38, 132, 255)),
                    BorderBrush = Brushes.White,
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    ToolTip = "Zmień kolor tego obiektu (Paleta RGB)"
                };
                btnCol.Click += (s, e) =>
                {
                    string picked = TacticalConfigPanel.ShowRgbColorPickerDialog(win, $"Kolor — {captured.Name}", captured.ColorHex);
                    if (!string.IsNullOrEmpty(picked))
                    {
                        captured.ColorHex = picked;
                        btnCol.Background = TacticalConfigPanel.BrushFromHex(picked, Color.FromRgb(38, 132, 255));
                        TacticalConfigPanel.MarkDirty();
                        RedrawVectorsOnly();
                    }
                };
                Grid.SetColumn(btnCol, 1);
                topRow.Children.Add(btnCol);

                var btnInspect = new Button
                {
                    Content = "📍",
                    Width = 22,
                    Height = 20,
                    Padding = new Thickness(0),
                    Background = TacticalConfigPanel.BrushInputBg,
                    Foreground = TacticalConfigPanel.BrushAccent,
                    BorderBrush = TacticalConfigPanel.BrushAccent,
                    BorderThickness = new Thickness(1),
                    FontSize = 10,
                    Cursor = Cursors.Hand,
                    ToolTip = "Pokaż współrzędne MGRS / Keypad / WGS84 tego znacznika na mapie"
                };
                btnInspect.Click += (s, e) =>
                {
                    _inspectedObject = captured;
                    _inspectedVertexIndex = 0;
                    UpdateSelectedMarkerInspectorUI();
                    RefreshObjectsListUI(win);
                    RedrawVectorsOnly();
                };
                Grid.SetColumn(btnInspect, 2);
                topRow.Children.Add(btnInspect);

                var natoOpt = NatoSymbols.FirstOrDefault(n => n.Code == captured.SymbolCode);
                string badgeTxt = captured.Kind == "AO" ? "[AO]" : (captured.Kind == "Sector" ? "[SEK]" : (natoOpt != null ? $"[{natoOpt.ShortBadge}]" : $"[{captured.SymbolCode}]"));
                var kindLbl = new TextBlock { Text = badgeTxt, Foreground = TacticalConfigPanel.BrushAccent, FontSize = 8.5, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 3, 0) };
                Grid.SetColumn(kindLbl, 3);
                topRow.Children.Add(kindLbl);

                var editNameBox = new TextBox
                {
                    Text = captured.Name,
                    Height = 22,
                    FontSize = 10,
                    Padding = new Thickness(4, 0, 4, 0),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Background = TacticalConfigPanel.BrushInputBg,
                    Foreground = Brushes.White,
                    BorderBrush = TacticalConfigPanel.BrushInputBorder,
                    BorderThickness = new Thickness(1)
                };
                editNameBox.TextChanged += (s, e) =>
                {
                    captured.Name = editNameBox.Text;
                    if (!_isRebuildingMapControls) TacticalConfigPanel.MarkDirty();
                    UpdateSelectedMarkerInspectorUI();
                    RedrawVectorsOnly();
                };
                Grid.SetColumn(editNameBox, 4);
                topRow.Children.Add(editNameBox);

                var btnMinusDel = CreateRedMinusDeleteButton($"Usuń obiekt: {captured.Name}", () =>
                {
                    _mapObjects.Remove(captured);
                    if (_activeDraftPoly == captured) _activeDraftPoly = null;
                    if (_inspectedObject == captured) _inspectedObject = null;
                    TacticalConfigPanel.MarkDirty();
                    UpdateSelectedMarkerInspectorUI();
                    RefreshObjectsListUI(win);
                    RedrawVectorsOnly();
                    UpdateOfflineTileEstimateUI();
                });
                Grid.SetColumn(btnMinusDel, 5);
                topRow.Children.Add(btnMinusDel);
                itemStack.Children.Add(topRow);

                var sliderRow = new Grid { Margin = new Thickness(0, 0, 0, captured.Kind == "Marker" ? 2 : 0) };
                sliderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
                sliderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                sliderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });

                var sizeTitle = new TextBlock { Text = "Wielkość:", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 9, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(sizeTitle, 0);
                sliderRow.Children.Add(sizeTitle);

                var sizeValTxt = new TextBlock { Text = FormatObjectSizeLabel(captured), Foreground = TacticalConfigPanel.BrushAccent, FontSize = 9, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(sizeValTxt, 2);
                sliderRow.Children.Add(sizeValTxt);

                const double minMult = 0.35, maxMult = 2.60;
                var sizeSlider = new Slider
                {
                    Minimum = minMult,
                    Maximum = maxMult,
                    Value = Math.Clamp(captured.SizeMultiplier, minMult, maxMult),
                    IsMoveToPointEnabled = true,
                    SmallChange = 0.05,
                    LargeChange = 0.2,
                    Cursor = Cursors.Hand,
                    VerticalAlignment = VerticalAlignment.Center
                };

                void SetItemSliderFromMouse(MouseEventArgs e)
                {
                    if (sizeSlider.ActualWidth <= 8) return;
                    double ratio = Math.Clamp(e.GetPosition(sizeSlider).X / sizeSlider.ActualWidth, 0.0, 1.0);
                    sizeSlider.Value = minMult + ratio * (maxMult - minMult);
                }

                sizeSlider.PreviewMouseLeftButtonDown += (s, e) => SetItemSliderFromMouse(e);
                sizeSlider.PreviewMouseMove += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) SetItemSliderFromMouse(e); };
                sizeSlider.ValueChanged += (s, e) =>
                {
                    captured.SizeMultiplier = Math.Round(e.NewValue, 2);
                    sizeValTxt.Text = FormatObjectSizeLabel(captured);
                    if (!_isRebuildingMapControls) TacticalConfigPanel.MarkDirty();
                    RedrawVectorsOnly();
                };
                Grid.SetColumn(sizeSlider, 1);
                sliderRow.Children.Add(sizeSlider);
                itemStack.Children.Add(sliderRow);

                if (captured.Kind == "Marker")
                {
                    var rotRow = new Grid();
                    rotRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
                    rotRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    rotRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });

                    var rotTitle = new TextBlock { Text = "Obrót (↻):", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 9, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(rotTitle, 0);
                    rotRow.Children.Add(rotTitle);

                    var rotValTxt = new TextBlock { Text = $"{captured.RotationDeg:0}°", Foreground = TacticalConfigPanel.BrushAccent, FontSize = 9, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(rotValTxt, 2);
                    rotRow.Children.Add(rotValTxt);

                    var rotSlider = new Slider
                    {
                        Minimum = 0.0,
                        Maximum = 360.0,
                        Value = Math.Clamp(captured.RotationDeg, 0.0, 360.0),
                        IsMoveToPointEnabled = true,
                        SmallChange = 5.0,
                        LargeChange = 15.0,
                        Cursor = Cursors.Hand,
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    void SetRotSliderFromMouse(MouseEventArgs e)
                    {
                        if (rotSlider.ActualWidth <= 8) return;
                        double ratio = Math.Clamp(e.GetPosition(rotSlider).X / rotSlider.ActualWidth, 0.0, 1.0);
                        rotSlider.Value = Math.Round(ratio * 360.0);
                    }

                    rotSlider.PreviewMouseLeftButtonDown += (s, e) => SetRotSliderFromMouse(e);
                    rotSlider.PreviewMouseMove += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) SetRotSliderFromMouse(e); };
                    rotSlider.ValueChanged += (s, e) =>
                    {
                        captured.RotationDeg = Math.Round(e.NewValue);
                        rotValTxt.Text = $"{captured.RotationDeg:0}°";
                        if (!_isRebuildingMapControls) TacticalConfigPanel.MarkDirty();
                        RedrawVectorsOnly();
                    };
                    Grid.SetColumn(rotSlider, 1);
                    rotRow.Children.Add(rotSlider);
                    itemStack.Children.Add(rotRow);
                }

                itemCard.Child = itemStack;
                _objectsListStack.Children.Add(itemCard);
            }
        }

        private static StackPanel CreateSectionCard(string title)
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = title, Foreground = TacticalConfigPanel.BrushAccent, FontWeight = FontWeights.Bold, FontSize = 11, Margin = new Thickness(0, 0, 0, 6) });
            return sp;
        }

        private static Border WrapCard(UIElement child) => new Border { Background = TacticalConfigPanel.BrushItemBg, BorderBrush = TacticalConfigPanel.BrushTileBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 10), Child = child };

        private static async Task SearchLocationNominatimAsync()
        {
            string q = _searchPlaceBox?.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(q)) return;
            _savedSearchQuery = q;
            try
            {
                string url = $"https://nominatim.openstreetmap.org/search?format=json&limit=1&q={Uri.EscapeDataString(q)}";
                var arr = JsonNode.Parse(await Http.GetStringAsync(url)) as JsonArray;
                if (arr != null && arr.Count > 0 && arr[0] is JsonObject first &&
                    double.TryParse(first["lat"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double la) &&
                    double.TryParse(first["lon"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double lo))
                {
                    _centerLat = la; _centerLon = lo;
                    TacticalConfigPanel.MarkDirty();
                    if (_statusText != null) _statusText.Text = $"✓ Znaleziono: {first["display_name"]}";
                    UpdateOfflineTileEstimateUI();
                    await RenderMapTilesAsync();
                }
            }
            catch { }
        }

        private static void RedrawVectorsOnly()
        {
            _fastOverlayRenderer?.InvalidateVisual();
            if (_interactiveCanvas == null || _fullScreenOverlay?.Visibility != Visibility.Visible) return;
            var (w, h) = GetExactCanvasSize();

            _interactiveCanvas.Children.Clear();

            if (!_isExportingCleanMap)
            {
                foreach (var polyObj in _mapObjects.Where(o => (o.Kind == "AO" || o.Kind == "Sector" || o.Kind == "Route") && o.Points.Count > 0))
                {
                    var strokeBrush = TacticalConfigPanel.BrushFromHex(polyObj.ColorHex, Color.FromRgb(38, 132, 255));
                    var screenPts = polyObj.Points.Select(pt => LatLonToScreen(pt.Lat, pt.Lon, w, h)).ToList();
                    DrawDraggableVertexAndWholeZoneHandles(polyObj, screenPts, strokeBrush, w, h);
                }
            }

            DrawTacticalMarkersOverlay(w, h);
            if (!_isExportingCleanMap)
            {
                DrawSelectedMarkerCoordsPopupOnCanvas(w, h);
            }
            DrawDraggableCornerElements(w, h);
        }

        private static void DrawSelectedMarkerCoordsPopupOnCanvas(double viewW, double viewH)
        {
            if (_inspectedObject == null || _inspectedObject.Points.Count == 0 || !_mapObjects.Contains(_inspectedObject))
                return;

            int idx = Math.Clamp(_inspectedVertexIndex, 0, _inspectedObject.Points.Count - 1);
            var latLon = _inspectedObject.Points[idx];
            Point screenPt = LatLonToScreen(latLon.Lat, latLon.Lon, viewW, viewH);
            if (screenPt.X < -20 || screenPt.X > viewW + 20 || screenPt.Y < -20 || screenPt.Y > viewH + 20)
                return;

            var (fullMgrs, _, kpInfo, utmTxt, wgsTxt) = FormatTacticalPointCoordinates(latLon.Lat, latLon.Lon);
            var strokeBrush = TacticalConfigPanel.BrushFromHex(_inspectedObject.ColorHex, Color.FromRgb(31, 224, 197));

            var highlightRing = new Ellipse
            {
                Width = 34,
                Height = 34,
                Stroke = TacticalConfigPanel.BrushAccent,
                StrokeThickness = 2.0,
                StrokeDashArray = new DoubleCollection { 3, 2 },
                IsHitTestVisible = false
            };
            Canvas.SetLeft(highlightRing, screenPt.X - 17);
            Canvas.SetTop(highlightRing, screenPt.Y - 17);
            _interactiveCanvas.Children.Add(highlightRing);

            const double popW = 292.0, popH = 74.0;
            double popX = Math.Clamp(screenPt.X - popW / 2.0, 8.0, Math.Max(10.0, viewW - popW - 8.0));
            double popY = screenPt.Y - popH - 26.0;
            if (popY < 8.0) popY = Math.Min(viewH - popH - 8.0, screenPt.Y + 24.0);

            var popBorder = new Border
            {
                Width = popW,
                Height = popH,
                Background = new SolidColorBrush(Color.FromArgb(238, 6, 15, 19)),
                BorderBrush = strokeBrush,
                BorderThickness = new Thickness(1.6),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 5, 8, 5)
            };

            var stack = new StackPanel();
            var hdrDock = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };

            var btnClosePop = new Border
            {
                Width = 18,
                Height = 16,
                CornerRadius = new CornerRadius(3),
                Background = TacticalConfigPanel.BrushRedMinus,
                Cursor = Cursors.Hand,
                Margin = new Thickness(4, 0, 0, 0),
                ToolTip = "Ukryj dymek współrzędnych",
                Child = new TextBlock { Text = "✕", Foreground = Brushes.White, FontSize = 9, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            btnClosePop.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                _inspectedObject = null;
                UpdateSelectedMarkerInspectorUI();
                RedrawVectorsOnly();
            };
            DockPanel.SetDock(btnClosePop, Dock.Right);
            hdrDock.Children.Add(btnClosePop);

            var btnCopyMini = new Border
            {
                Height = 16,
                Padding = new Thickness(5, 0, 5, 0),
                CornerRadius = new CornerRadius(3),
                Background = TacticalConfigPanel.BrushItemBg,
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = "Skopiuj współrzędne MGRS i WGS84 tego markera do schowka",
                Child = new TextBlock { Text = "📋 Kopiuj", Foreground = TacticalConfigPanel.BrushAccent, FontSize = 8.5, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center }
            };
            btnCopyMini.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                CopySelectedMarkerCoordsToClipboard();
            };
            DockPanel.SetDock(btnCopyMini, Dock.Right);
            hdrDock.Children.Add(btnCopyMini);

            string ptSuffix = _inspectedObject.Points.Count > 1 ? $" (#{idx + 1})" : "";
            hdrDock.Children.Add(new TextBlock
            {
                Text = $"🎯 {_inspectedObject.Name}{ptSuffix}",
                Foreground = strokeBrush,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            stack.Children.Add(hdrDock);

            stack.Children.Add(new TextBlock
            {
                Text = $"MGRS: {fullMgrs}",
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 10,
                FontWeight = FontWeights.Bold
            });
            stack.Children.Add(new TextBlock
            {
                Text = kpInfo,
                Foreground = TacticalConfigPanel.BrushAccent,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 9.2,
                FontWeight = FontWeights.SemiBold
            });
            stack.Children.Add(new TextBlock
            {
                Text = $"WGS84: {wgsTxt}",
                Foreground = TacticalConfigPanel.BrushMutedText,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 8.8
            });

            popBorder.Child = stack;
            Canvas.SetLeft(popBorder, popX);
            Canvas.SetTop(popBorder, popY);
            _interactiveCanvas.Children.Add(popBorder);
        }

        private static void DrawDraggableVertexAndWholeZoneHandles(TacticalMapObjectItem obj, List<Point> screenPts, Brush strokeBrush, double viewW, double viewH)
        {
            for (int i = 0; i < screenPts.Count; i++)
            {
                int vIdx = i;
                Point vPt = screenPts[i];
                var handle = new Ellipse
                {
                    Width = 11,
                    Height = 11,
                    Fill = Brushes.White,
                    Stroke = strokeBrush,
                    StrokeThickness = 2.2,
                    Cursor = Cursors.Hand,
                    ToolTip = $"Kliknij = Odczytaj współrzędne punktu #{vIdx + 1} ({obj.Name})  |  Przeciągnij = Zmień pozycję"
                };
                handle.MouseLeftButtonDown += (s, e) =>
                {
                    e.Handled = true;
                    _inspectedObject = obj;
                    _inspectedVertexIndex = vIdx;
                    UpdateSelectedMarkerInspectorUI();
                    _draggedObject = obj;
                    _draggedVertexIndex = vIdx;
                    Point mousePt = e.GetPosition(_interactiveCanvas);
                    _markerDragScreenOffset = new Point(mousePt.X - vPt.X, mousePt.Y - vPt.Y);
                    _interactiveCanvas.CaptureMouse();
                    RedrawVectorsOnly();
                };
                Canvas.SetLeft(handle, vPt.X - 5.5);
                Canvas.SetTop(handle, vPt.Y - 5.5);
                _interactiveCanvas.Children.Add(handle);
            }

            if (screenPts.Count >= 2)
            {
                double cx = screenPts.Average(p => p.X);
                double cy = screenPts.Average(p => p.Y);
                var moveAllBadge = new Border
                {
                    Width = 22,
                    Height = 22,
                    CornerRadius = new CornerRadius(11),
                    Background = new SolidColorBrush(Color.FromArgb(225, 8, 19, 22)),
                    BorderBrush = strokeBrush,
                    BorderThickness = new Thickness(1.8),
                    Cursor = Cursors.SizeAll,
                    ToolTip = $"Przeciągnij tutaj (✥), aby przesunąć CAŁY obiekt: {obj.Name}",
                    Child = new TextBlock
                    {
                        Text = "✥",
                        Foreground = Brushes.White,
                        FontSize = 12,
                        FontWeight = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, -1, 0, 0)
                    }
                };
                moveAllBadge.MouseLeftButtonDown += (s, e) =>
                {
                    e.Handled = true;
                    _inspectedObject = obj;
                    _inspectedVertexIndex = 0;
                    UpdateSelectedMarkerInspectorUI();
                    _draggedObject = obj;
                    _draggedVertexIndex = -2;
                    Point p = e.GetPosition(_interactiveCanvas);
                    _wholeShapeDragStartLatLon = ScreenToLatLon(p.X, p.Y, viewW, viewH);
                    _wholeShapeOriginalPoints = obj.Points.ToList();
                    _interactiveCanvas.CaptureMouse();
                };
                Canvas.SetLeft(moveAllBadge, cx - 11);
                Canvas.SetTop(moveAllBadge, cy - 11);
                _interactiveCanvas.Children.Add(moveAllBadge);
            }
        }

        private static void AddHaloLabel(string text, double x, double y, Brush fg, double fontSize)
        {
            var b = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(195, 5, 12, 15)),
                BorderBrush = fg,
                BorderThickness = new Thickness(0.8),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 1, 4, 1),
                IsHitTestVisible = false,
                Child = new TextBlock { Text = text, Foreground = fg, FontSize = fontSize, FontWeight = FontWeights.Bold }
            };
            Canvas.SetLeft(b, x); Canvas.SetTop(b, y); _interactiveCanvas.Children.Add(b);
        }

        private static void AttachCenteredMarkerDragHandler(UIElement el, TacticalMapObjectItem obj, Point exactMarkerAnchorScreenPt, string tooltipSuffix = "środek 1:1")
        {
            if (el is FrameworkElement fe)
            {
                fe.Cursor = Cursors.Hand;
                fe.ToolTip = $"Kliknij LPM = Odczytaj współrzędne MGRS/WGS84 ({obj.Name})  |  Przeciągnij = Przesuń ({tooltipSuffix})";
            }
            el.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                _inspectedObject = obj;
                _inspectedVertexIndex = 0;
                UpdateSelectedMarkerInspectorUI();
                _draggedObject = obj;
                _draggedVertexIndex = 0;
                Point mousePt = e.GetPosition(_interactiveCanvas);
                _markerDragScreenOffset = new Point(mousePt.X - exactMarkerAnchorScreenPt.X, mousePt.Y - exactMarkerAnchorScreenPt.Y);
                _interactiveCanvas.CaptureMouse();
                RedrawVectorsOnly();
            };
        }

        private static void DrawRotationDragHandleForMarker(Point anchorScreenPt, TacticalMapObjectItem obj, double scale, Brush strokeBrush)
        {
            if (_isExportingCleanMap || !_showRotationHandlesOnMap) return;

            double rad = (obj.RotationDeg - 90.0) * Math.PI / 180.0;
            double handleDist = Math.Max(22.0, 23.0 * scale);
            double hx = anchorScreenPt.X + handleDist * Math.Cos(rad);
            double hy = anchorScreenPt.Y + handleDist * Math.Sin(rad);

            var stemLine = new Line
            {
                X1 = anchorScreenPt.X,
                Y1 = anchorScreenPt.Y,
                X2 = hx,
                Y2 = hy,
                Stroke = TacticalConfigPanel.BrushAccent,
                StrokeThickness = 1.2,
                StrokeDashArray = new DoubleCollection { 2, 2 },
                IsHitTestVisible = false
            };
            _interactiveCanvas.Children.Add(stemLine);

            var rotBadge = new Border
            {
                Width = 17,
                Height = 17,
                CornerRadius = new CornerRadius(8.5),
                Background = new SolidColorBrush(Color.FromArgb(230, 8, 19, 22)),
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(1.3),
                Cursor = Cursors.Hand,
                ToolTip = $"Przeciągnij myszą (↻), aby obrócić: {obj.Name} (Aktualnie: {obj.RotationDeg:0}° | SHIFT = co 15°)",
                Child = new TextBlock
                {
                    Text = "↻",
                    Foreground = TacticalConfigPanel.BrushAccent,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, -1, 0, 0)
                }
            };
            rotBadge.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                _inspectedObject = obj;
                _inspectedVertexIndex = 0;
                UpdateSelectedMarkerInspectorUI();
                _draggedObject = obj;
                _draggedVertexIndex = -3;
                _rotationCenterScreenPt = anchorScreenPt;
                _interactiveCanvas.CaptureMouse();
            };
            Canvas.SetLeft(rotBadge, hx - 8.5);
            Canvas.SetTop(rotBadge, hy - 8.5);
            _interactiveCanvas.Children.Add(rotBadge);
        }

        private static void DrawNatoVectorSymbolOnCanvas(Point pt, TacticalMapObjectItem obj)
        {
            string symbolCode = obj.SymbolCode ?? "NATO_HQ";
            string name = obj.Name;
            string colorHex = obj.ColorHex;
            double objMult = obj.SizeMultiplier > 0.1 ? obj.SizeMultiplier : 1.0;
            double scale = Math.Clamp(_natoMarkerScale * objMult, 0.30, 3.5);
            double rotDeg = obj.RotationDeg;

            Color c = Color.FromRgb(38, 132, 255);
            try { c = (Color)ColorConverter.ConvertFromString(colorHex); } catch { }

            var strokeBrush = TacticalConfigPanel.CreateFrozenBrush(c.R, c.G, c.B);
            var fillBrush = new SolidColorBrush(Color.FromArgb(190, c.R, c.G, c.B)); fillBrush.Freeze();
            var darkBgBrush = new SolidColorBrush(Color.FromArgb(225, 8, 16, 20)); darkBgBrush.Freeze();

            if (symbolCode == "NATO_HQ")
            {
                const double flagBoxW = 36.0, flagBoxH = 36.0;
                const double poleX = 4.0;
                const double poleTopY = 2.0;
                const double poleBottomY = 32.0;
                const double flagW = 28.0, flagH = 17.0;

                var flagTg = new TransformGroup();
                flagTg.Children.Add(new ScaleTransform(scale, scale, poleX, poleBottomY));
                if (Math.Abs(rotDeg) > 0.1)
                    flagTg.Children.Add(new RotateTransform(rotDeg, poleX, poleBottomY));

                var flagCanvas = new Canvas
                {
                    Width = flagBoxW,
                    Height = flagBoxH,
                    Background = Brushes.Transparent,
                    RenderTransform = flagTg
                };
                AttachCenteredMarkerDragHandler(flagCanvas, obj, pt, "dolna część masztu flagi");

                flagCanvas.Children.Add(new Line { X1 = poleX, Y1 = poleTopY, X2 = poleX, Y2 = poleBottomY, Stroke = Brushes.Black, StrokeThickness = 4.4 });
                flagCanvas.Children.Add(new Line { X1 = poleX, Y1 = poleTopY, X2 = poleX, Y2 = poleBottomY, Stroke = strokeBrush, StrokeThickness = 2.4 });

                var baseDot = new Ellipse { Width = 6, Height = 6, Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1.2 };
                Canvas.SetLeft(baseDot, poleX - 3.0); Canvas.SetTop(baseDot, poleBottomY - 3.0);
                flagCanvas.Children.Add(baseDot);

                var hqHalo = new Rectangle { Width = flagW + 2, Height = flagH + 2, Stroke = Brushes.Black, StrokeThickness = 3.0, Fill = darkBgBrush };
                Canvas.SetLeft(hqHalo, poleX - 1); Canvas.SetTop(hqHalo, poleTopY - 1);
                flagCanvas.Children.Add(hqHalo);

                var hqRect = new Rectangle { Width = flagW, Height = flagH, Stroke = strokeBrush, StrokeThickness = 2.0, Fill = fillBrush };
                Canvas.SetLeft(hqRect, poleX); Canvas.SetTop(hqRect, poleTopY);
                flagCanvas.Children.Add(hqRect);

                var hqTxt = new TextBlock { Text = "HQ", Foreground = Brushes.White, FontSize = 9.5, FontWeight = FontWeights.Bold };
                Canvas.SetLeft(hqTxt, poleX + 6); Canvas.SetTop(hqTxt, poleTopY + 2);
                flagCanvas.Children.Add(hqTxt);

                Canvas.SetLeft(flagCanvas, pt.X - poleX);
                Canvas.SetTop(flagCanvas, pt.Y - poleBottomY);
                _interactiveCanvas.Children.Add(flagCanvas);

                DrawRotationDragHandleForMarker(pt, obj, scale, strokeBrush);

                if (_showAllObjectLabels && obj.ShowLabel && !string.IsNullOrWhiteSpace(name))
                    AddHaloLabel(name, pt.X + (flagW + 5) * scale, pt.Y - poleBottomY * scale + 1, strokeBrush, 10 * _fontScaleMultiplier);
                return;
            }

            const double boxW = 36.0, boxH = 32.0;
            const double cx = 18.0, cy = 16.0;
            const double fw = 28.0, fh = 18.0;
            double left = cx - fw / 2.0, top = cy - fh / 2.0;

            var iconTg = new TransformGroup();
            iconTg.Children.Add(new ScaleTransform(scale, scale, cx, cy));
            if (Math.Abs(rotDeg) > 0.1)
                iconTg.Children.Add(new RotateTransform(rotDeg, cx, cy));

            var iconCanvas = new Canvas
            {
                Width = boxW,
                Height = boxH,
                Background = Brushes.Transparent,
                RenderTransform = iconTg
            };
            AttachCenteredMarkerDragHandler(iconCanvas, obj, pt);

            void AddBoxToIconCanvas()
            {
                var halo = new Rectangle { Width = fw + 2, Height = fh + 2, Stroke = Brushes.Black, StrokeThickness = 3.0, Fill = darkBgBrush };
                Canvas.SetLeft(halo, left - 1); Canvas.SetTop(halo, top - 1); iconCanvas.Children.Add(halo);
                var rect = new Rectangle { Width = fw, Height = fh, Stroke = strokeBrush, StrokeThickness = 2.0, Fill = fillBrush };
                Canvas.SetLeft(rect, left); Canvas.SetTop(rect, top); iconCanvas.Children.Add(rect);
            }

            switch (symbolCode)
            {
                case "SHAPE_ARROW":
                    var arrPts = new PointCollection
                    {
                        new Point(cx, cy - 14),
                        new Point(cx + 11, cy + 1),
                        new Point(cx + 4.5, cy + 1),
                        new Point(cx + 4.5, cy + 14),
                        new Point(cx - 4.5, cy + 14),
                        new Point(cx - 4.5, cy + 1),
                        new Point(cx - 11, cy + 1)
                    };
                    iconCanvas.Children.Add(new Polygon { Stroke = Brushes.Black, StrokeThickness = 3.6, Fill = darkBgBrush, Points = arrPts, StrokeLineJoin = PenLineJoin.Round });
                    iconCanvas.Children.Add(new Polygon { Stroke = Brushes.White, StrokeThickness = 1.4, Fill = strokeBrush, Points = arrPts, StrokeLineJoin = PenLineJoin.Round });
                    break;

                case "SHAPE_TENT":
                    var tentOuter = new PointCollection { new Point(cx, cy - 12), new Point(cx + 14, cy + 11), new Point(cx - 14, cy + 11) };
                    var tentDoor = new PointCollection { new Point(cx, cy - 1), new Point(cx + 5.5, cy + 11), new Point(cx - 5.5, cy + 11) };
                    iconCanvas.Children.Add(new Polygon { Stroke = Brushes.Black, StrokeThickness = 3.6, Fill = darkBgBrush, Points = tentOuter, StrokeLineJoin = PenLineJoin.Round });
                    iconCanvas.Children.Add(new Polygon { Stroke = Brushes.White, StrokeThickness = 1.6, Fill = fillBrush, Points = tentOuter, StrokeLineJoin = PenLineJoin.Round });
                    iconCanvas.Children.Add(new Polygon { Stroke = strokeBrush, StrokeThickness = 1.2, Fill = darkBgBrush, Points = tentDoor });
                    iconCanvas.Children.Add(new Line { X1 = cx - 16, Y1 = cy + 11.5, X2 = cx + 16, Y2 = cy + 11.5, Stroke = Brushes.White, StrokeThickness = 2.0 });
                    break;

                case "SHAPE_CROSS":
                    iconCanvas.Children.Add(new Line { X1 = cx - 11, Y1 = cy - 11, X2 = cx + 11, Y2 = cy + 11, Stroke = Brushes.Black, StrokeThickness = 6.0, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
                    iconCanvas.Children.Add(new Line { X1 = cx - 11, Y1 = cy + 11, X2 = cx + 11, Y2 = cy - 11, Stroke = Brushes.Black, StrokeThickness = 6.0, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
                    iconCanvas.Children.Add(new Line { X1 = cx - 11, Y1 = cy - 11, X2 = cx + 11, Y2 = cy + 11, Stroke = strokeBrush, StrokeThickness = 3.4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
                    iconCanvas.Children.Add(new Line { X1 = cx - 11, Y1 = cy + 11, X2 = cx + 11, Y2 = cy - 11, Stroke = strokeBrush, StrokeThickness = 3.4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
                    var crossDot = new Ellipse { Width = 5, Height = 5, Fill = Brushes.White };
                    Canvas.SetLeft(crossDot, cx - 2.5); Canvas.SetTop(crossDot, cy - 2.5); iconCanvas.Children.Add(crossDot);
                    break;

                case "SHAPE_CIRCLE":
                    var cHalo = new Ellipse { Width = 24, Height = 24, Stroke = Brushes.Black, StrokeThickness = 3.4, Fill = darkBgBrush };
                    Canvas.SetLeft(cHalo, cx - 12); Canvas.SetTop(cHalo, cy - 12); iconCanvas.Children.Add(cHalo);
                    var cMain = new Ellipse { Width = 22, Height = 22, Stroke = strokeBrush, StrokeThickness = 2.4, Fill = fillBrush };
                    Canvas.SetLeft(cMain, cx - 11); Canvas.SetTop(cMain, cy - 11); iconCanvas.Children.Add(cMain);
                    var cCenter = new Ellipse { Width = 5, Height = 5, Fill = Brushes.White };
                    Canvas.SetLeft(cCenter, cx - 2.5); Canvas.SetTop(cCenter, cy - 2.5); iconCanvas.Children.Add(cCenter);
                    break;

                case "SHAPE_SQUARE":
                    var sqHalo = new Rectangle { Width = 22, Height = 22, Stroke = Brushes.Black, StrokeThickness = 3.4, Fill = darkBgBrush };
                    Canvas.SetLeft(sqHalo, cx - 11); Canvas.SetTop(sqHalo, cy - 11); iconCanvas.Children.Add(sqHalo);
                    var sqMain = new Rectangle { Width = 20, Height = 20, Stroke = strokeBrush, StrokeThickness = 2.2, Fill = fillBrush };
                    Canvas.SetLeft(sqMain, cx - 10); Canvas.SetTop(sqMain, cy - 10); iconCanvas.Children.Add(sqMain);
                    break;

                case "SHAPE_RECT":
                    AddBoxToIconCanvas();
                    break;

                case "SHAPE_DIAMOND":
                    var dmShapePts = new PointCollection { new Point(cx, cy - 13), new Point(cx + 13, cy), new Point(cx, cy + 13), new Point(cx - 13, cy) };
                    iconCanvas.Children.Add(new Polygon { Stroke = Brushes.Black, StrokeThickness = 3.4, Fill = darkBgBrush, Points = dmShapePts });
                    iconCanvas.Children.Add(new Polygon { Stroke = strokeBrush, StrokeThickness = 2.2, Fill = fillBrush, Points = dmShapePts });
                    break;

                case "SHAPE_TRIANGLE":
                    var trShapePts = new PointCollection { new Point(cx, cy - 12), new Point(cx - 13, cy + 11), new Point(cx + 13, cy + 11) };
                    iconCanvas.Children.Add(new Polygon { Stroke = Brushes.Black, StrokeThickness = 3.4, Fill = darkBgBrush, Points = trShapePts, StrokeLineJoin = PenLineJoin.Round });
                    iconCanvas.Children.Add(new Polygon { Stroke = strokeBrush, StrokeThickness = 2.2, Fill = fillBrush, Points = trShapePts, StrokeLineJoin = PenLineJoin.Round });
                    break;

                case "NATO_INF":
                    AddBoxToIconCanvas();
                    iconCanvas.Children.Add(new Line { X1 = left, Y1 = top, X2 = left + fw, Y2 = top + fh, Stroke = Brushes.White, StrokeThickness = 1.8 });
                    iconCanvas.Children.Add(new Line { X1 = left, Y1 = top + fh, X2 = left + fw, Y2 = top, Stroke = Brushes.White, StrokeThickness = 1.8 });
                    break;
                case "NATO_RECON":
                    AddBoxToIconCanvas();
                    iconCanvas.Children.Add(new Line { X1 = left, Y1 = top + fh, X2 = left + fw, Y2 = top, Stroke = Brushes.White, StrokeThickness = 2.0 });
                    break;
                case "NATO_MED":
                    AddBoxToIconCanvas();
                    iconCanvas.Children.Add(new Line { X1 = cx, Y1 = top, X2 = cx, Y2 = top + fh, Stroke = Brushes.White, StrokeThickness = 2.2 });
                    iconCanvas.Children.Add(new Line { X1 = left, Y1 = cy, X2 = left + fw, Y2 = cy, Stroke = Brushes.White, StrokeThickness = 2.2 });
                    break;
                case "NATO_COMMS":
                    AddBoxToIconCanvas();
                    var bolt = new Polyline { Stroke = Brushes.White, StrokeThickness = 2.0, Points = new PointCollection { new Point(left + 5, top + fh - 3), new Point(cx - 1, top + 3), new Point(cx + 2, top + fh - 4), new Point(left + fw - 5, top + 3) } };
                    iconCanvas.Children.Add(bolt);
                    break;
                case "NATO_SUPPLY":
                    AddBoxToIconCanvas();
                    iconCanvas.Children.Add(new Line { X1 = left, Y1 = top + fh - 4, X2 = left + fw, Y2 = top + fh - 4, Stroke = Brushes.White, StrokeThickness = 2.0 });
                    break;
                case "NATO_OP":
                    var triPts = new PointCollection { new Point(cx, cy - 11), new Point(cx - 12, cy + 10), new Point(cx + 12, cy + 10) };
                    iconCanvas.Children.Add(new Polygon { Stroke = Brushes.Black, StrokeThickness = 3.2, Fill = darkBgBrush, Points = triPts });
                    iconCanvas.Children.Add(new Polygon { Stroke = strokeBrush, StrokeThickness = 2.0, Fill = fillBrush, Points = triPts });
                    var opDot = new Ellipse { Width = 4, Height = 4, Fill = Brushes.White };
                    Canvas.SetLeft(opDot, cx - 2); Canvas.SetTop(opDot, cy); iconCanvas.Children.Add(opDot);
                    break;
                case "NATO_SNIPER":
                    var snHalo = new Ellipse { Width = 22, Height = 22, Stroke = Brushes.Black, StrokeThickness = 3.0, Fill = darkBgBrush };
                    Canvas.SetLeft(snHalo, cx - 11); Canvas.SetTop(snHalo, cy - 11); iconCanvas.Children.Add(snHalo);
                    var snCirc = new Ellipse { Width = 20, Height = 20, Stroke = strokeBrush, StrokeThickness = 2.0, Fill = fillBrush };
                    Canvas.SetLeft(snCirc, cx - 10); Canvas.SetTop(snCirc, cy - 10); iconCanvas.Children.Add(snCirc);
                    iconCanvas.Children.Add(new Line { X1 = cx - 13, Y1 = cy, X2 = cx + 13, Y2 = cy, Stroke = Brushes.White, StrokeThickness = 1.5 });
                    iconCanvas.Children.Add(new Line { X1 = cx, Y1 = cy - 13, X2 = cx, Y2 = cy + 13, Stroke = Brushes.White, StrokeThickness = 1.5 });
                    break;
                case "NATO_CP":
                    var shPts = new PointCollection { new Point(cx - 11, cy - 10), new Point(cx + 11, cy - 10), new Point(cx + 11, cy + 2), new Point(cx, cy + 11), new Point(cx - 11, cy + 2) };
                    iconCanvas.Children.Add(new Polygon { Stroke = Brushes.Black, StrokeThickness = 3.0, Fill = darkBgBrush, Points = shPts });
                    iconCanvas.Children.Add(new Polygon { Stroke = strokeBrush, StrokeThickness = 2.0, Fill = fillBrush, Points = shPts });
                    var cpTxt = new TextBlock { Text = "CP", Foreground = Brushes.White, FontSize = 8.5, FontWeight = FontWeights.Bold };
                    Canvas.SetLeft(cpTxt, cx - 6); Canvas.SetTop(cpTxt, cy - 6); iconCanvas.Children.Add(cpTxt);
                    break;
                case "NATO_LZ":
                    var lzHalo = new Ellipse { Width = 24, Height = 24, Stroke = Brushes.Black, StrokeThickness = 3.0, Fill = darkBgBrush };
                    Canvas.SetLeft(lzHalo, cx - 12); Canvas.SetTop(lzHalo, cy - 12); iconCanvas.Children.Add(lzHalo);
                    var lzCirc = new Ellipse { Width = 22, Height = 22, Stroke = strokeBrush, StrokeThickness = 2.0, Fill = fillBrush };
                    Canvas.SetLeft(lzCirc, cx - 11); Canvas.SetTop(lzCirc, cy - 11); iconCanvas.Children.Add(lzCirc);
                    var lzTxt = new TextBlock { Text = "LZ", Foreground = Brushes.White, FontSize = 8.5, FontWeight = FontWeights.Bold };
                    Canvas.SetLeft(lzTxt, cx - 5.5); Canvas.SetTop(lzTxt, cy - 5.5); iconCanvas.Children.Add(lzTxt);
                    break;
                case "NATO_OBJ":
                    var dmPts = new PointCollection { new Point(cx, cy - 12), new Point(cx + 12, cy), new Point(cx, cy + 12), new Point(cx - 12, cy) };
                    iconCanvas.Children.Add(new Polygon { Stroke = Brushes.Black, StrokeThickness = 3.0, Fill = darkBgBrush, Points = dmPts });
                    iconCanvas.Children.Add(new Polygon { Stroke = strokeBrush, StrokeThickness = 2.0, Fill = fillBrush, Points = dmPts });
                    iconCanvas.Children.Add(new Line { X1 = cx - 6, Y1 = cy, X2 = cx + 6, Y2 = cy, Stroke = Brushes.White, StrokeThickness = 1.6 });
                    iconCanvas.Children.Add(new Line { X1 = cx, Y1 = cy - 6, X2 = cx, Y2 = cy + 6, Stroke = Brushes.White, StrokeThickness = 1.6 });
                    break;
                case "NATO_HAZARD":
                    var hzPts = new PointCollection { new Point(cx, cy - 11), new Point(cx - 12, cy + 10), new Point(cx + 12, cy + 10) };
                    iconCanvas.Children.Add(new Polygon { Stroke = Brushes.Black, StrokeThickness = 3.2, Fill = darkBgBrush, Points = hzPts });
                    iconCanvas.Children.Add(new Polygon { Stroke = strokeBrush, StrokeThickness = 2.0, Fill = fillBrush, Points = hzPts });
                    var hzTxt = new TextBlock { Text = "!", Foreground = Brushes.White, FontSize = 10.5, FontWeight = FontWeights.Bold };
                    Canvas.SetLeft(hzTxt, cx - 2); Canvas.SetTop(hzTxt, cy - 4); iconCanvas.Children.Add(hzTxt);
                    break;
                default:
                    var dot = new Ellipse { Width = 14, Height = 14, Fill = strokeBrush, Stroke = Brushes.White, StrokeThickness = 2 };
                    Canvas.SetLeft(dot, cx - 7); Canvas.SetTop(dot, cy - 7); iconCanvas.Children.Add(dot);
                    break;
            }

            Canvas.SetLeft(iconCanvas, pt.X - cx);
            Canvas.SetTop(iconCanvas, pt.Y - cy);
            _interactiveCanvas.Children.Add(iconCanvas);

            DrawRotationDragHandleForMarker(pt, obj, scale, strokeBrush);

            if (_showAllObjectLabels && obj.ShowLabel && !string.IsNullOrWhiteSpace(name))
                AddHaloLabel(name, pt.X + 16 * scale, pt.Y - 9, strokeBrush, 10 * _fontScaleMultiplier);
        }

        private static void DrawTacticalMarkersOverlay(double w, double h)
        {
            foreach (var obj in _mapObjects.Where(o => o.Points.Count > 0 && (o.Kind == "Marker" || o.Kind == "Dot")))
            {
                var strokeBrush = TacticalConfigPanel.BrushFromHex(obj.ColorHex, Color.FromRgb(38, 132, 255));
                Point pt = LatLonToScreen(obj.Points[0].Lat, obj.Points[0].Lon, w, h);
                if (obj.Kind == "Dot")
                {
                    double mult = obj.SizeMultiplier > 0.1 ? obj.SizeMultiplier : 1.0;
                    double dotDia = Math.Clamp(_dotMarkerSizePx * mult, 4.0, 56.0);
                    double dotRadius = dotDia / 2.0;

                    var dot = new Ellipse
                    {
                        Width = dotDia,
                        Height = dotDia,
                        Fill = strokeBrush,
                        Stroke = Brushes.White,
                        StrokeThickness = Math.Clamp(dotDia * 0.14, 1.0, 3.2)
                    };
                    AttachCenteredMarkerDragHandler(dot, obj, pt);
                    Canvas.SetLeft(dot, pt.X - dotRadius);
                    Canvas.SetTop(dot, pt.Y - dotRadius);
                    _interactiveCanvas.Children.Add(dot);

                    if (_showAllObjectLabels && obj.ShowLabel && !string.IsNullOrWhiteSpace(obj.Name))
                        AddHaloLabel(obj.Name, pt.X + dotRadius + 4.0, pt.Y - 8, strokeBrush, 9.5 * _fontScaleMultiplier);
                }
                else
                {
                    DrawNatoVectorSymbolOnCanvas(pt, obj);
                }
            }
        }

        private static void PlaceDraggableHudElement(FrameworkElement el, string hudId, string cornerPreset, double? normX, double? normY, double viewW, double viewH, double elemW, double elemH)
        {
            double x = normX.HasValue
                ? Math.Clamp(normX.Value * viewW, 4, Math.Max(8, viewW - elemW - 4))
                : (cornerPreset.StartsWith("Lewy") ? 14 : Math.Max(14, viewW - elemW - 14));
            double y = normY.HasValue
                ? Math.Clamp(normY.Value * viewH, 4, Math.Max(8, viewH - elemH - 4))
                : (cornerPreset.Contains("górny") ? 14 : Math.Max(14, viewH - elemH - 14));

            el.Cursor = Cursors.SizeAll;
            el.ToolTip = "Przeciągnij myszą (Drag & Drop), aby ustawić w dowolnym miejscu na mapie";
            el.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                _draggedHudId = hudId;
                _draggedHudElement = el;
                Point p = e.GetPosition(_interactiveCanvas);
                _hudStartOffset = new Point(p.X - Canvas.GetLeft(el), p.Y - Canvas.GetTop(el));
                _interactiveCanvas.CaptureMouse();
            };

            Canvas.SetLeft(el, x);
            Canvas.SetTop(el, y);
            _interactiveCanvas.Children.Add(el);
        }

        private static void DrawDraggableCornerElements(double w, double h)
        {
            var (wMm, _) = GetActiveSheetDimensionsMm();
            bool isCompactWrist = wMm <= 160;

            if (_showNorthArrow)
            {
                double nw = isCompactWrist ? 42 : 52, nh = isCompactWrist ? 50 : 62;
                var compass = new Border { Width = nw, Height = nh, Background = new SolidColorBrush(Color.FromArgb(228, 8, 19, 22)), BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(6) };
                var cs = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                cs.Children.Add(new TextBlock { Text = "N", Foreground = TacticalConfigPanel.BrushAccent, FontWeight = FontWeights.Bold, FontSize = isCompactWrist ? 11 : 14, HorizontalAlignment = HorizontalAlignment.Center });
                cs.Children.Add(new TextBlock { Text = "▲", Foreground = Brushes.White, FontSize = isCompactWrist ? 14 : 18, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -4, 0, -2) });
                cs.Children.Add(new TextBlock { Text = "MGRS", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 7.5, HorizontalAlignment = HorizontalAlignment.Center });
                compass.Child = cs;
                PlaceDraggableHudElement(compass, "NORTH", _northArrowPos, _northNormX, _northNormY, w, h, nw, nh);
            }

            if (_showScaleBar)
            {
                double metersPerPixel = GetExactMetersPerPixel();
                double chosen = _coordSystem.StartsWith("MGRS") ? _effectiveMgrsStepMeters : 100;
                if (chosen / metersPerPixel < 50 || chosen / metersPerPixel > 240)
                {
                    foreach (var d in new[] { 20.0, 50.0, 100.0, 200.0, 500.0, 1000.0, 2000.0, 5000.0 })
                    {
                        double px = d / metersPerPixel;
                        if (px >= (isCompactWrist ? 55 : 70) && px <= (isCompactWrist ? 150 : 220)) { chosen = d; break; }
                    }
                }
                double barW = Math.Clamp(chosen / metersPerPixel, 45, 230);

                var sbBorder = new Border { Width = barW + 34, Height = 52, Background = new SolidColorBrush(Color.FromArgb(228, 8, 19, 22)), BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(6) };
                var cnv = new Canvas();
                var hdr = new TextBlock { Text = $"SKALA 1:{_targetScaleDenominator:N0}", Foreground = TacticalConfigPanel.BrushAccent, FontSize = 9.5, FontWeight = FontWeights.Bold };
                Canvas.SetLeft(hdr, 12); Canvas.SetTop(hdr, 4); cnv.Children.Add(hdr);
                double segW = barW / 4.0;
                for (int s = 0; s < 4; s++)
                {
                    var r = new Rectangle { Width = segW + 0.5, Height = 6.5, Fill = (s % 2 == 0) ? TacticalConfigPanel.BrushAccent : Brushes.White, Stroke = Brushes.Black, StrokeThickness = 0.7 };
                    Canvas.SetLeft(r, 12 + s * segW); Canvas.SetTop(r, 22); cnv.Children.Add(r);
                }
                var t0 = new TextBlock { Text = "0", Foreground = Brushes.White, FontSize = 8.5 }; Canvas.SetLeft(t0, 12); Canvas.SetTop(t0, 31); cnv.Children.Add(t0);
                var tHalf = new TextBlock { Text = $"{chosen / 2:0}m", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 8 }; Canvas.SetLeft(tHalf, 12 + barW / 2 - 10); Canvas.SetTop(tHalf, 31); cnv.Children.Add(tHalf);
                var t1 = new TextBlock { Text = $"{chosen:0} m", Foreground = Brushes.White, FontSize = 8.5, FontWeight = FontWeights.Bold }; Canvas.SetLeft(t1, 12 + barW - 22); Canvas.SetTop(t1, 31); cnv.Children.Add(t1);
                sbBorder.Child = cnv;
                PlaceDraggableHudElement(sbBorder, "SCALE", _scaleBarPos, _scaleNormX, _scaleNormY, w, h, barW + 34, 52);
            }

            if (_showLegend)
            {
                double lw = isCompactWrist ? 215 : 275;
                var ls = new StackPanel();
                ls.Children.Add(new TextBlock { Text = $"MAPA TAKTYCZNA ({_selectedSheet.Name.Split('—')[0].Trim()} | 1:{_targetScaleDenominator:N0})", Foreground = TacticalConfigPanel.BrushAccent, FontWeight = FontWeights.Bold, FontSize = isCompactWrist ? 9 : 10 });
                string gridSummary = _coordSystem.StartsWith("MGRS")
                    ? $"Siatka: MGRS ({_effectiveMgrsStepMeters} m) | {_subGridDivision}"
                    : $"Siatka: {_coordSystem.Split(' ')[0]}";
                ls.Children.Add(new TextBlock { Text = gridSummary, Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 8.5, Margin = new Thickness(0, 1, 0, 4) });

                var activeFacs = GetActiveMapFactions();
                if (activeFacs.Count > 0)
                {
                    var fw = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
                    fw.Children.Add(new TextBlock { Text = "Strona: ", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 8.5, Margin = new Thickness(0, 0, 4, 0) });
                    foreach (var f in activeFacs)
                    {
                        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0) };
                        sp.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(2), Background = TacticalConfigPanel.BrushFromHex(f.ColorHex, Color.FromRgb(38, 132, 255)), BorderBrush = Brushes.White, BorderThickness = new Thickness(0.6), Margin = new Thickness(0, 0, 4, 0) });
                        sp.Children.Add(new TextBlock { Text = f.Name, Foreground = Brushes.White, FontSize = 8.5, FontWeight = FontWeights.Bold });
                        fw.Children.Add(sp);
                    }
                    ls.Children.Add(fw);
                }

                int dynamicRows = 0;
                if (_showNatoSymbolsInLegend)
                {
                    ls.Children.Add(new Border { Height = 1, Background = TacticalConfigPanel.BrushTileBorder, Margin = new Thickness(0, 2, 0, 4) });
                    ls.Children.Add(new TextBlock { Text = "OZNACZENIA I SYMBOLE NA MAPIE:", Foreground = TacticalConfigPanel.BrushAccent, FontSize = 8.5, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 3) });

                    void AddLegendSymbolRow(string badge, string desc, string hex)
                    {
                        dynamicRows++;
                        var r = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2.5) };
                        var b = new Border
                        {
                            MinWidth = 28,
                            Height = 14,
                            CornerRadius = new CornerRadius(2),
                            Background = TacticalConfigPanel.BrushFromHex(hex, Color.FromRgb(38, 132, 255)),
                            BorderBrush = Brushes.White,
                            BorderThickness = new Thickness(0.8),
                            Margin = new Thickness(0, 0, 6, 0),
                            Padding = new Thickness(3, 0, 3, 0),
                            Child = new TextBlock { Text = badge, Foreground = Brushes.White, FontSize = 7.5, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                        };
                        r.Children.Add(b);
                        r.Children.Add(new TextBlock { Text = desc, Foreground = Brushes.White, FontSize = 8, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = lw - 52 });
                        ls.Children.Add(r);
                    }

                    if (_mapObjects.Count > 0)
                    {
                        foreach (var obj in _mapObjects.Take(isCompactWrist ? 5 : 9))
                        {
                            if (obj.Kind == "AO")
                                AddLegendSymbolRow("AO", $"{obj.Name} — Granica Terenu Gry", obj.ColorHex);
                            else if (obj.Kind == "Sector")
                                AddLegendSymbolRow("SEK", $"{obj.Name} — Sektor / Strefa taktyczna", obj.ColorHex);
                            else if (obj.Kind == "Route")
                                AddLegendSymbolRow("───", $"{obj.Name} — Trasa / Linia rozgraniczenia", obj.ColorHex);
                            else if (obj.Kind == "Dot")
                                AddLegendSymbolRow("●", $"{obj.Name} — Punkt orientacyjny", obj.ColorHex);
                            else
                            {
                                var natoDef = NatoSymbols.FirstOrDefault(n => n.Code == obj.SymbolCode) ?? NatoSymbols[0];
                                AddLegendSymbolRow(natoDef.ShortBadge, $"{obj.Name} — {natoDef.NatoMeaning}", obj.ColorHex);
                            }
                        }
                    }
                    else
                    {
                        AddLegendSymbolRow("HQ", "Sztab / Dowództwo strony", "#2684FF");
                        AddLegendSymbolRow("⛺", "Namiot / Baza polowa / Respawn", "#2ECC71");
                        AddLegendSymbolRow("➤", "Kierunek natarcia / Ruch", "#F5B82E");
                        AddLegendSymbolRow("AO", "Granica Terenu Gry (strefa poza grą)", "#EB4034");
                    }
                }

                double lh = 64 + (_showNatoSymbolsInLegend ? (22 + dynamicRows * 17) : 0);
                var legBorder = new Border
                {
                    Width = lw,
                    Height = lh,
                    Background = new SolidColorBrush(Color.FromArgb(228, 8, 19, 22)),
                    BorderBrush = TacticalConfigPanel.BrushAccent,
                    BorderThickness = new Thickness(1.2),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 6, 8, 6),
                    Child = ls
                };
                PlaceDraggableHudElement(legBorder, "LEGEND", _legendPos, _legendNormX, _legendNormY, w, h, lw, lh);
            }
        }

        private static byte[] CaptureMapCanvasPngBytes()
        {
            var (cw, ch) = GetExactCanvasSize();
            int w = (int)Math.Max(400, cw), h = (int)Math.Max(300, ch);
            _isExportingCleanMap = true;
            try
            {
                RedrawVectorsOnly();
                _mapCompositeGrid.UpdateLayout();
                var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(_mapCompositeGrid);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
                using var ms = new MemoryStream(); enc.Save(ms); return ms.ToArray();
            }
            finally
            {
                _isExportingCleanMap = false;
                RedrawVectorsOnly();
            }
        }

        private static void SaveMapProjectJson(Window win)
        {
            try
            {
                if (TacticalConfigPanel.SaveDraftProjectDialog(win) && _statusText != null)
                    _statusText.Text = $"✓ Zapisano wersję roboczą projektu: {TacticalConfigPanel.CurrentDraftProjectPath}";
            }
            catch { }
        }

        private static void LoadMapProjectJsonOrKml(Window win)
        {
            try
            {
                string initDir = !string.IsNullOrWhiteSpace(TacticalConfigPanel.LastExportDirectory) && Directory.Exists(TacticalConfigPanel.LastExportDirectory)
                    ? TacticalConfigPanel.LastExportDirectory
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                var dlg = new OpenFileDialog
                {
                    Title = "Wczytaj projekt roboczy (.fthproj / .json) lub punkty (.kml / .gpx)",
                    Filter = "Pliki projektu i mapy (*.fthproj;*.json;*.kml;*.gpx)|*.fthproj;*.json;*.kml;*.gpx",
                    InitialDirectory = initDir
                };
                if (dlg.ShowDialog(win) != true) return;
                TacticalConfigPanel.LastExportDirectory = System.IO.Path.GetDirectoryName(dlg.FileName) ?? initDir;

                string txt = File.ReadAllText(dlg.FileName, Encoding.UTF8);
                if ((dlg.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || dlg.FileName.EndsWith(".fthproj", StringComparison.OrdinalIgnoreCase)) &&
                    JsonNode.Parse(txt) is JsonObject root)
                {
                    if (root["mapState"] is JsonObject)
                        TacticalConfigPanel.ApplyFullWorkspaceStateJson(win, root);
                    else
                        ImportMapStateFromJson(root);

                    TacticalConfigPanel.SaveSessionStateAuto(win);
                    if (_statusText != null) _statusText.Text = $"✓ Wczytano projekt z pliku: {dlg.FileName}";
                }
                else
                {
                    foreach (Match m in Regex.Matches(txt, @"<wpt[^>]*lat=""([0-9\.\-]+)""[^>]*lon=""([0-9\.\-]+)""", RegexOptions.IgnoreCase))
                        if (double.TryParse(m.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double la) &&
                            double.TryParse(m.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double lo))
                        {
                            var it = new TacticalMapObjectItem { Kind = "Marker", SymbolCode = _selectedNatoSymbolCode, Name = $"GPX-{_mapObjects.Count + 1}", ColorHex = _selectedObjectColorHex, ShowLabel = _newObjectShowLabelDefault, SizeMultiplier = 1.0 };
                            it.Points.Add((la, lo));
                            _mapObjects.Add(it);
                        }
                    RefreshObjectsListUI(win);
                    ApplyScaleDenominator(_targetScaleDenominator);
                    TacticalConfigPanel.MarkDirty();
                }
            }
            catch { }
        }

        private static async Task ExportMapToFtakSourceAsync(Window win)
        {
            try
            {
                string sourceDir = TacticalConfigPanel.GetPrimarySourceDirectory(win);
                var allConfigured = TacticalConfigPanel.GetConfiguredFactions();
                var targetFacs = GetActiveMapFactions();

                bool grgTransparent = _kmzGrgMode.StartsWith("Przezroczysta");
                if (grgTransparent) await RenderMapTilesAsync(true);
                else await RenderMapTilesAsync(false);
                byte[] pngBytes = CaptureMapCanvasPngBytes();
                if (grgTransparent) await RenderMapTilesAsync(false);

                var (w, h) = GetExactCanvasSize();
                var (north, west) = ScreenToLatLon(0, 0, w, h); var (south, east) = ScreenToLatLon(w, h, w, h);

                await Task.Run(() =>
                {
                    if (targetFacs.Count > 0 && targetFacs.Count < allConfigured.Count)
                    {
                        foreach (var fac in targetFacs)
                        {
                            string sl = TacticalConfigPanel.SanitizeFileName(fac.Name).ToLowerInvariant();
                            string facDir = System.IO.Path.Combine(sourceDir, "factions", sl);
                            Directory.CreateDirectory(facDir);
                            WriteKmzGroundOverlayArchive(System.IO.Path.Combine(facDir, $"grg_map_{sl}.kmz"), $"Mapa_GRG_{fac.Name}", pngBytes, north, south, east, west);
                            WriteGpxFile(System.IO.Path.Combine(facDir, $"punkty_{sl}.gpx"));
                        }
                    }
                    else
                    {
                        string mapsDir = System.IO.Path.Combine(sourceDir, "maps"), atakDir = System.IO.Path.Combine(sourceDir, "atak");
                        Directory.CreateDirectory(mapsDir); Directory.CreateDirectory(atakDir);
                        WriteKmzGroundOverlayArchive(System.IO.Path.Combine(mapsDir, "grg_map_overlay.kmz"), "Mapa_GRG_ATAK", pngBytes, north, south, east, west);
                        WriteGpxFile(System.IO.Path.Combine(atakDir, "punkty_taktyczne.gpx"));
                    }
                });
                TacticalConfigPanel.SaveSessionStateAuto(win);
                await TacticalConfigPanel.RefreshSourceSizeFromExternalAsync(win);
                string targetNames = string.Join(", ", targetFacs.Select(f => f.Name));
                if (_statusText != null) _statusText.Text = $"✓ Zapisano mapę .KMZ oraz punkty .GPX w folderze source dla stron: {targetNames}!";
            }
            catch { }
        }

        private static async Task ExportStandaloneFilesAsync(Window win, string format)
        {
            try
            {
                string sourceDir = TacticalConfigPanel.GetPrimarySourceDirectory(win);
                string defaultDir = !string.IsNullOrWhiteSpace(TacticalConfigPanel.LastExportDirectory) && Directory.Exists(TacticalConfigPanel.LastExportDirectory)
                    ? TacticalConfigPanel.LastExportDirectory
                    : (System.IO.Path.GetDirectoryName(System.IO.Path.TrimEndingDirectorySeparator(sourceDir)) ?? AppDomain.CurrentDomain.BaseDirectory);

                string facSuffix = string.Join("_", GetActiveMapFactions().Select(f => TacticalConfigPanel.SanitizeFileName(f.Name)));
                var dlg = new SaveFileDialog
                {
                    Title = format == "kmz" ? "Zapisz warstwę mapy .KMZ (GRG ATAK) jako..." : "Zapisz punkty i trasy .GPX (Garmin / ATAK) jako...",
                    Filter = format == "kmz" ? "Archiwum mapy KMZ (*.kmz)|*.kmz" : "Plik nawigacyjny GPX (*.gpx)|*.gpx",
                    InitialDirectory = defaultDir,
                    FileName = format == "kmz" ? $"Mapa_Taktyczna_ATAK_{facSuffix}_1_{_targetScaleDenominator}.kmz" : $"Punkty_Taktyczne_Garmin_{facSuffix}.gpx"
                };
                if (dlg.ShowDialog(win) != true) return;

                string chosenPath = dlg.FileName;
                string chosenDir = System.IO.Path.GetDirectoryName(chosenPath) ?? defaultDir;
                TacticalConfigPanel.LastExportDirectory = chosenDir;
                TacticalConfigPanel.SaveSessionStateAuto(win);

                bool grgTransparent = (format == "kmz" && _kmzGrgMode.StartsWith("Przezroczysta"));
                if (grgTransparent) await RenderMapTilesAsync(true);
                else await RenderMapTilesAsync(false);
                byte[] pngBytes = CaptureMapCanvasPngBytes();
                if (grgTransparent) await RenderMapTilesAsync(false);

                var (w, h) = GetExactCanvasSize();
                var (north, west) = ScreenToLatLon(0, 0, w, h); var (south, east) = ScreenToLatLon(w, h, w, h);

                await Task.Run(() =>
                {
                    if (format == "kmz") WriteKmzGroundOverlayArchive(chosenPath, System.IO.Path.GetFileNameWithoutExtension(chosenPath), pngBytes, north, south, east, west);
                    else if (format == "gpx") WriteGpxFile(chosenPath);
                });
                if (_statusText != null) _statusText.Text = $"✓ Zapisano plik: {chosenPath}";
                TacticalConfigPanel.TryOpenFolderInExplorer(chosenDir);
            }
            catch { }
        }

        private static void WriteKmzGroundOverlayArchive(string kmzPath, string title, byte[] mapPngBytes, double north, double south, double east, double west)
        {
            if (File.Exists(kmzPath)) File.Delete(kmzPath);
            using var fs = new FileStream(kmzPath, FileMode.CreateNew, FileAccess.Write);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create, false, Encoding.UTF8);
            var imgEntry = zip.CreateEntry("files/grg_overlay.png", CompressionLevel.Optimal);
            using (var s = imgEntry.Open()) s.Write(mapPngBytes, 0, mapPngBytes.Length);

            var sb = new StringBuilder();
            sb.AppendLine(@"<?xml version=""1.0"" encoding=""UTF-8""?><kml xmlns=""http://www.opengis.net/kml/2.2""><Document>");
            sb.AppendLine($"  <name>{title}</name><GroundOverlay><name>{title}</name><Icon><href>files/grg_overlay.png</href></Icon>");
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"    <LatLonBox><north>{north:0.000000}</north><south>{south:0.000000}</south><east>{east:0.000000}</east><west>{west:0.000000}</west></LatLonBox></GroundOverlay>"));
            foreach (var o in _mapObjects.Where(x => x.Points.Count > 0))
            {
                var natoDef = NatoSymbols.FirstOrDefault(n => n.Code == o.SymbolCode);
                string desc = natoDef != null ? natoDef.NatoMeaning : o.Kind;
                string badge = natoDef != null ? natoDef.ShortBadge : o.SymbolCode;
                var (fullMgrs, _, kpInfo, _, _) = FormatTacticalPointCoordinates(o.Points[0].Lat, o.Points[0].Lon);
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  <Placemark><name>[{badge}] {o.Name}</name><description>{desc} | MGRS: {fullMgrs} ({kpInfo})</description><Point><coordinates>{o.Points[0].Lon:0.000000},{o.Points[0].Lat:0.000000},0</coordinates></Point></Placemark>"));
            }
            sb.AppendLine("</Document></kml>");
            var kmlEntry = zip.CreateEntry("doc.kml", CompressionLevel.Optimal);
            using var sw = new StreamWriter(kmlEntry.Open(), Encoding.UTF8); sw.Write(sb.ToString());
        }

        private static void WriteGpxFile(string gpxPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine(@"<?xml version=""1.0"" encoding=""UTF-8""?><gpx version=""1.1"" creator=""Field TAK Hub Builder 2.4"" xmlns=""http://www.topografix.com/GPX/1/1"">");
            foreach (var o in _mapObjects.Where(x => x.Points.Count > 0))
            {
                if (o.Points.Count == 1)
                {
                    var natoDef = NatoSymbols.FirstOrDefault(n => n.Code == o.SymbolCode);
                    string desc = natoDef != null ? natoDef.NatoMeaning : o.Kind;
                    string badge = natoDef != null ? natoDef.ShortBadge : o.SymbolCode;
                    var (fullMgrs, _, kpInfo, _, _) = FormatTacticalPointCoordinates(o.Points[0].Lat, o.Points[0].Lon);
                    sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  <wpt lat=\"{o.Points[0].Lat:0.000000}\" lon=\"{o.Points[0].Lon:0.000000}\"><name>[{badge}] {o.Name}</name><desc>{desc} | MGRS: {fullMgrs} ({kpInfo})</desc></wpt>"));
                }
                else
                {
                    sb.AppendLine($"  <rte><name>{o.Name}</name>");
                    foreach (var pt in o.Points) sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"    <rtept lat=\"{pt.Lat:0.000000}\" lon=\"{pt.Lon:0.000000}\" />"));
                    sb.AppendLine("  </rte>");
                }
            }
            sb.AppendLine("</gpx>");
            File.WriteAllText(gpxPath, sb.ToString(), Encoding.UTF8);
        }

        private static async Task ExportTacticalMapPdfAsync(Window win)
        {
            try
            {
                string sourceDir = TacticalConfigPanel.GetPrimarySourceDirectory(win);
                string defaultDir = !string.IsNullOrWhiteSpace(TacticalConfigPanel.LastExportDirectory) && Directory.Exists(TacticalConfigPanel.LastExportDirectory)
                    ? TacticalConfigPanel.LastExportDirectory
                    : (System.IO.Path.GetDirectoryName(System.IO.Path.TrimEndingDirectorySeparator(sourceDir)) ?? AppDomain.CurrentDomain.BaseDirectory);

                bool landscape = _sheetOrientation == "Poziomo";
                var (wMm, hMm) = GetActiveSheetDimensionsMm();
                string cleanFmt = TacticalConfigPanel.SanitizeFileName(_selectedSheet.Name.Split('—')[0].Trim());
                string facSuffix = string.Join("_", GetActiveMapFactions().Select(f => TacticalConfigPanel.SanitizeFileName(f.Name)));

                var dlg = new SaveFileDialog
                {
                    Title = "Wybierz miejsce i nazwę zapisu gotowej mapy do druku (.PDF)",
                    Filter = "Dokument PDF do druku (*.pdf)|*.pdf",
                    InitialDirectory = defaultDir,
                    FileName = $"Mapa_{cleanFmt}_{_sheetOrientation}_{facSuffix}_1_{_targetScaleDenominator}.pdf"
                };
                if (dlg.ShowDialog(win) != true) return;

                string pdfPath = dlg.FileName;
                string outDir = System.IO.Path.GetDirectoryName(pdfPath) ?? defaultDir;
                TacticalConfigPanel.LastExportDirectory = outDir;
                TacticalConfigPanel.SaveSessionStateAuto(win);

                await RenderMapTilesAsync(false);

                int pageW = landscape ? 1754 : 1240;
                int pageH = landscape ? 1240 : 1754;
                double hostPageWMm = landscape ? 297.0 : 210.0;
                double hostPageHMm = landscape ? 210.0 : 297.0;

                var (cw, ch) = GetExactCanvasSize();
                int mapW = (int)Math.Max(400, cw);
                int mapH = (int)Math.Max(300, ch);

                _isExportingCleanMap = true;
                RenderTargetBitmap mapRtb;
                try
                {
                    RedrawVectorsOnly();
                    _mapCompositeGrid.UpdateLayout();
                    mapRtb = new RenderTargetBitmap(mapW, mapH, 96, 96, PixelFormats.Pbgra32);
                    mapRtb.Render(_mapCompositeGrid);
                }
                finally
                {
                    _isExportingCleanMap = false;
                    RedrawVectorsOnly();
                }

                var pageVisual = new DrawingVisual();
                using (DrawingContext dc = pageVisual.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, pageW, pageH));

                    if (wMm < 285 && hMm < 285)
                    {
                        double drawW = (wMm / hostPageWMm) * pageW;
                        double drawH = (hMm / hostPageHMm) * pageH;
                        double ox = (pageW - drawW) / 2.0;
                        double oy = (pageH - drawH) / 2.0;

                        dc.DrawImage(mapRtb, new Rect(ox, oy, drawW, drawH));
                        var cropPen = new Pen(Brushes.Black, 1.5) { DashStyle = DashStyles.Dash };
                        dc.DrawRectangle(null, new Pen(Brushes.Black, 1.2), new Rect(ox, oy, drawW, drawH));

                        double cm = 28;
                        dc.DrawLine(cropPen, new Point(ox - cm, oy), new Point(ox + drawW + cm, oy));
                        dc.DrawLine(cropPen, new Point(ox - cm, oy + drawH), new Point(ox + drawW + cm, oy + drawH));
                        dc.DrawLine(cropPen, new Point(ox, oy - cm), new Point(ox, oy + drawH + cm));
                        dc.DrawLine(cropPen, new Point(ox + drawW, oy - cm), new Point(ox + drawW, oy + drawH + cm));

                        TacticalConfigPanel.DrawCenteredText(dc, $"✂️ LINIA CIĘCIA MAPNIKA: {_selectedSheet.Name} ({_sheetOrientation}: {wMm:0} × {hMm:0} mm)  |  Strona: {facSuffix}  |  Skala 1:{_targetScaleDenominator:N0}", 15, Brushes.Black, pageW / 2.0, oy - 34, true);
                    }
                    else
                    {
                        dc.DrawImage(mapRtb, new Rect(24, 24, pageW - 48, pageH - 48));
                    }
                }

                var fullRtb = new RenderTargetBitmap(pageW, pageH, 96, 96, PixelFormats.Pbgra32);
                fullRtb.Render(pageVisual);
                var jpgEnc = new JpegBitmapEncoder { QualityLevel = 94 };
                jpgEnc.Frames.Add(BitmapFrame.Create(fullRtb));
                using var jpgMs = new MemoryStream();
                jpgEnc.Save(jpgMs);
                byte[] jpegBytes = jpgMs.ToArray();

                await Task.Run(() => WriteSingleImagePdf(pdfPath, jpegBytes, pageW, pageH, landscape));
                if (_statusText != null) _statusText.Text = $"✓ Zapisano plik PDF: {pdfPath}";
                TacticalConfigPanel.TryOpenFolderInExplorer(outDir);
            }
            catch { }
        }

        private static void WriteSingleImagePdf(string pdfPath, byte[] jpegBytes, int pixelW, int pixelH, bool landscape)
        {
            using var fs = new FileStream(pdfPath, FileMode.Create, FileAccess.Write);
            void WriteAscii(string s) { byte[] b = Encoding.ASCII.GetBytes(s); fs.Write(b, 0, b.Length); }
            int pdfW = landscape ? 842 : 595, pdfH = landscape ? 595 : 842;
            WriteAscii("%PDF-1.4\n");
            long obj1 = fs.Position; WriteAscii("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
            long obj2 = fs.Position; WriteAscii("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
            long obj3 = fs.Position; WriteAscii($"3 0 obj\n<< /Type /Page /Parent 2 0 R /Resources << /XObject << /Im0 4 0 R >> >> /MediaBox [0 0 {pdfW} {pdfH}] /Contents 5 0 R >>\nendobj\n");
            long obj4 = fs.Position; WriteAscii($"4 0 obj\n<< /Type /XObject /Subtype /Image /Width {pixelW} /Height {pixelH} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpegBytes.Length} >>\nstream\n");
            fs.Write(jpegBytes, 0, jpegBytes.Length);
            WriteAscii("\nendstream\nendobj\n");
            string contentCmd = $"q\n{pdfW} 0 0 {pdfH} 0 0 cm\n/Im0 Do\nQ\n";
            long obj5 = fs.Position; WriteAscii($"5 0 obj\n<< /Length {contentCmd.Length} >>\nstream\n{contentCmd}endstream\nendobj\n");
            long xrefPos = fs.Position;
            WriteAscii($"xref\n0 6\n0000000000 65535 f \n{obj1:D10} 00000 n \n{obj2:D10} 00000 n \n{obj3:D10} 00000 n \n{obj4:D10} 00000 n \n{obj5:D10} 00000 n \ntrailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xrefPos}\n%%EOF\n");
        }
    }
}
