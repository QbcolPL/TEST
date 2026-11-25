using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FieldTakHub.Builder.Services.MapEditor;

namespace FieldTakHub.Builder.Services
{
    /// <summary>
    /// Praca bezpośrednio na mapie: pasek narzędzi nad mapą, pierścień menu pod prawym przyciskiem, menu obiektu,
    /// skróty klawiszowe, podpowiedź aktywnego narzędzia, narzędzia jednorazowe i pomiar odległości z azymutem.
    /// </summary>
    public static partial class TacticalMapGeneratorView
    {
        private sealed record MapToolDef(string Tool, string Glyph, string Name, Key Key);

        private static readonly MapToolDef[] MapTools =
        {
            new MapToolDef("PAN", "✋", "Zaznacz / przesuń mapę", Key.V),
            new MapToolDef("MARKER", "➤", "Znacznik NATO", Key.N),
            new MapToolDef("DOT", "●", "Kropka", Key.K),
            new MapToolDef("ROUTE", "〰", "Trasa / linia", Key.T),
            new MapToolDef("SECTOR", "⬠", "Sektor / strefa", Key.S),
            new MapToolDef("AO", "🛡", "Teren gry (AO)", Key.A),
            new MapToolDef("MEASURE", "📏", "Pomiar odległości i azymutu", Key.L)
        };

        private const string MapShortcutsHelp =
            "Skróty generatora map\n" +
            "V — zaznacz / przesuń mapę\n" +
            "N — znacznik NATO,  K — kropka\n" +
            "T — trasa,  S — sektor,  A — teren gry (AO)\n" +
            "L — pomiar odległości i azymutu\n" +
            "Ponowne naciśnięcie tej samej litery wraca do zaznaczania.\n\n" +
            "Prawy klik na mapie — szybkie menu w tym miejscu\n" +
            "    (przytrzymaj, przeciągnij w stronę pozycji i puść)\n" +
            "Prawy klik na obiekcie lub jego krawędzi — menu obiektu\n" +
            "Enter albo prawy klik — zakończ rysowany kształt\n" +
            "Shift — kąt co 45° przy rysowaniu; kilka znaczników naraz\n\n" +
            "Ctrl+Z — cofnij,  Ctrl+Y — ponów\n" +
            "    (w trakcie rysowania Ctrl+Z usuwa ostatni punkt)\n" +
            "Del — usuń zaznaczony element,  Ctrl+D — duplikuj\n" +
            "F2 — zmień nazwę,  F — pokaż zaznaczony element\n" +
            "Esc — anuluj / zakończ / odznacz";

        private static readonly Dictionary<string, Button> _mapToolButtons = new Dictionary<string, Button>();
        private static readonly Dictionary<TacticalMapObjectItem, TextBox> _objectNameBoxes = new Dictionary<TacticalMapObjectItem, TextBox>();
        private static Grid? _mapMenuLayer;
        private static Canvas? _mapMenuCanvas;
        private static MapRadialMenu? _ringMenu;
        private static bool _ringOpenedByPress;
        private static TextBlock? _toolHintText;
        private static ComboBox? _natoSymbolCombo;
        private static ScrollViewer? _objectsListScroll;

        private static bool IsMapMenuOpen => _mapMenuLayer != null && _mapMenuLayer.Visibility == Visibility.Visible;

        /// <summary>Pasek narzędzi, podpowiedź i warstwa menu nad mapą. Elementy leżą poza arkuszem, więc nie trafiają do wydruku ani eksportu.</summary>
        private static void AttachMapInteractionLayer()
        {
            if (_viewportHostGrid == null || _interactiveCanvas == null) return;
            _mapToolButtons.Clear();

            _interactiveCanvas.Focusable = true;
            _interactiveCanvas.FocusVisualStyle = null;
            _interactiveCanvas.PreviewMouseDown += (_, _) => FocusMapCanvas();

            var toolbar = BuildMapToolbar();
            Panel.SetZIndex(toolbar, 900);
            _viewportHostGrid.Children.Add(toolbar);

            _toolHintText = new TextBlock { Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 10.5, TextWrapping = TextWrapping.Wrap };
            var hint = new Border
            {
                Background = MapEditorUi.FloatingBackground,
                BorderBrush = TacticalConfigPanel.BrushTileBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(8, 3, 8, 3),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(10, 0, 10, 10),
                MaxWidth = 760,
                IsHitTestVisible = false,
                Child = _toolHintText
            };
            Panel.SetZIndex(hint, 900);
            _viewportHostGrid.Children.Add(hint);

            _mapMenuCanvas = new Canvas();
            _mapMenuLayer = new Grid { Visibility = Visibility.Collapsed, Background = Brushes.Transparent };
            _mapMenuLayer.Children.Add(_mapMenuCanvas);
            _mapMenuLayer.MouseMove += (_, e) => OnMenuLayerMouseMove(e);
            _mapMenuLayer.MouseLeftButtonDown += (_, e) => OnMenuLayerLeftButtonDown(e);
            _mapMenuLayer.MouseRightButtonUp += (_, e) => OnMenuLayerRightButtonUp(e);
            _mapMenuLayer.MouseRightButtonDown += (_, e) => { e.Handled = true; CloseMapMenus(); };
            _mapMenuLayer.MouseWheel += (_, e) => { e.Handled = true; CloseMapMenus(); };
            Panel.SetZIndex(_mapMenuLayer, 1000);
            _viewportHostGrid.Children.Add(_mapMenuLayer);

            RefreshToolButtonsVisual();
            UpdateToolHint();
            ApplyToolCursor();
        }

        private static Border BuildMapToolbar()
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var tool in MapTools)
            {
                var def = tool;
                var button = MapEditorUi.ToolButton(def.Glyph, $"{def.Name} ({def.Key})", () => ToggleTool(def.Tool));
                _mapToolButtons[def.Tool] = button;
                row.Children.Add(button);
            }
            row.Children.Add(ToolbarSeparator());
            _undoButton = MapEditorUi.ToolButton("↶", "Cofnij (Ctrl+Z)", UndoMapEdit);
            _redoButton = MapEditorUi.ToolButton("↷", "Ponów (Ctrl+Y)", RedoMapEdit);
            ToolTipService.SetShowOnDisabled(_undoButton, true);
            ToolTipService.SetShowOnDisabled(_redoButton, true);
            row.Children.Add(_undoButton);
            row.Children.Add(_redoButton);
            RefreshUndoButtons();
            row.Children.Add(ToolbarSeparator());
            var help = MapEditorUi.ToolButton("?", MapShortcutsHelp, ShowMapShortcutsPanel);
            help.FontWeight = FontWeights.Bold;
            ToolTipService.SetShowDuration(help, 60000);
            row.Children.Add(help);

            return new Border
            {
                Background = MapEditorUi.FloatingBackground,
                BorderBrush = TacticalConfigPanel.BrushTileBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(3),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(10, 10, 0, 0),
                Child = row
            };
        }

        private static Border ToolbarSeparator() => new Border
        {
            Width = 1,
            Height = 18,
            Margin = new Thickness(4, 0, 4, 0),
            Background = TacticalConfigPanel.BrushTileBorder,
            VerticalAlignment = VerticalAlignment.Center
        };

        private static void ShowMapShortcutsPanel()
        {
            var text = new TextBlock { Text = MapShortcutsHelp, Foreground = Brushes.White, FontSize = 11, LineHeight = 17, Margin = new Thickness(8, 6, 8, 6) };
            ShowFloatingPanelAtHost(MapEditorUi.FloatingPanel(text, 380), new Point(10, 46));
        }

        // ------------------------------------------------------------------ narzędzia

        /// <summary>Kliknięcie narzędzia (pasek, klawisz): ponowne kliknięcie aktywnego wraca do zaznaczania.</summary>
        private static void ToggleTool(string tool) => SelectTool(tool == _activeTool && tool != "PAN" ? "PAN" : tool);

        private static void SelectTool(string tool)
        {
            CloseMapMenus();
            bool changed = tool != _activeTool;
            bool committed = changed && CommitDraftShape();
            if (changed && tool == "MEASURE")
            {
                _rulerStart = null;
                _rulerEnd = null;
            }
            _activeTool = tool;
            RefreshToolButtonsVisual();
            UpdateToolHint();
            ApplyToolCursor();
            _fastOverlayRenderer?.InvalidateVisual();
            if (committed && _hostWindow != null)
            {
                RefreshObjectsListUI(_hostWindow);
                RedrawVectorsOnly();
                UpdateOfflineTileEstimateUI();
            }
        }

        private static void RefreshToolButtonsVisual()
        {
            foreach (var pair in _mapToolButtons)
                MapEditorUi.SetToolActive(pair.Value, pair.Key == _activeTool);
        }

        private static void ApplyToolCursor()
        {
            if (_interactiveCanvas != null) _interactiveCanvas.Cursor = _activeTool == "PAN" ? Cursors.Arrow : Cursors.Cross;
        }

        private static void UpdateToolHint()
        {
            if (_toolHintText == null) return;
            _toolHintText.Text = _activeTool switch
            {
                "MARKER" => $"Znacznik NATO „{CurrentNatoSymbolLabel()}”: kliknij na mapie, aby postawić · Shift — kilka naraz · Esc — koniec",
                "DOT" => "Kropka: kliknij na mapie, aby postawić · Shift — kilka naraz · Esc — koniec",
                "ROUTE" => "Trasa: klikaj kolejne punkty · Shift — kąt co 45° · Enter lub prawy klik — zakończ · Esc — koniec",
                "SECTOR" => "Sektor: klikaj kolejne wierzchołki · Shift — kąt co 45° · Enter lub prawy klik — zakończ · Esc — koniec",
                "AO" => "Teren gry: klikaj kolejne wierzchołki · Shift — kąt co 45° · Enter lub prawy klik — zakończ · Esc — koniec",
                "MEASURE" => "Pomiar: kliknij początek i koniec — odległość i azymut · Esc lub prawy klik — koniec",
                _ => "Przeciągnij, aby przesunąć mapę · kliknij obiekt, aby go zaznaczyć · prawy klik — szybkie menu · ? — skróty"
            };
        }

        private static string CurrentNatoSymbolLabel() =>
            (NatoSymbols.FirstOrDefault(n => n.Code == _selectedNatoSymbolCode) ?? NatoSymbols[0]).Label;

        private static void FocusMapCanvas()
        {
            if (_interactiveCanvas != null && !_interactiveCanvas.IsKeyboardFocused) Keyboard.Focus(_interactiveCanvas);
        }

        private static void ShowMapStatus(string text)
        {
            if (_statusText != null) _statusText.Text = text;
        }

        /// <summary>Zamyka rysowany kształt. Kształt z za małą liczbą punktów (np. AO z dwóch kliknięć) jest usuwany.</summary>
        private static bool CommitDraftShape()
        {
            var draft = _activeDraftPoly;
            if (draft == null) return false;
            _activeDraftPoly = null;
            int minimum = MapObjectKinds.MinimumPoints(draft.Kind);
            if (draft.Points.Count < minimum)
            {
                _mapObjects.Remove(draft);
                if (ReferenceEquals(_inspectedObject, draft)) _inspectedObject = null;
                ShowMapStatus($"Za mało punktów — „{draft.Name}” nie został dodany (potrzeba co najmniej {minimum}).");
                UpdateSelectedMarkerInspectorUI();
            }
            else
            {
                ShowMapStatus($"Gotowe: „{draft.Name}” — {MapObjectCardSpec.FormatPointCount(draft.Points.Count)}.");
            }
            TacticalConfigPanel.MarkDirty();
            return true;
        }

        /// <summary>Kończy kształt i wraca do zaznaczania (narzędzia działają jednorazowo).</summary>
        private static void FinishDraftShape(Window win)
        {
            if (!CommitDraftShape()) return;
            SelectTool("PAN");
            RefreshObjectsListUI(win);
            RedrawVectorsOnly();
            UpdateOfflineTileEstimateUI();
        }

        /// <summary>Krótkie kliknięcie lewym przyciskiem w mapę (bez przeciągania).</summary>
        private static void HandleToolClick(Window win, Point canvasPt)
        {
            switch (_activeTool)
            {
                case "PAN":
                    if (_inspectedObject != null || _listSelection != null) ClearListSelection();
                    return;

                case "MEASURE":
                {
                    var (w, h) = GetExactCanvasSize();
                    var point = ScreenToLatLon(canvasPt.X, canvasPt.Y, w, h);
                    if (_rulerStart == null || _rulerEnd != null)
                    {
                        _rulerStart = point;
                        _rulerEnd = null;
                    }
                    else
                    {
                        _rulerEnd = point;
                        ShowMapStatus($"📏 Pomiar: {RulerLabel(_rulerStart.Value, point)}");
                    }
                    _fastOverlayRenderer?.InvalidateVisual();
                    return;
                }
            }

            bool pointTool = _activeTool == "MARKER" || _activeTool == "DOT";
            HandleMapClickToolAction(win, canvasPt);
            if (pointTool)
            {
                AnnounceNewObject();
                if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) SelectTool("PAN");
            }
            UpdateSelectedMarkerInspectorUI();
            RedrawVectorsOnly();
            UpdateOfflineTileEstimateUI();
        }

        private static string RulerLabel((double Lat, double Lon) start, (double Lat, double Lon) end) =>
            MapMeasure.Label(start.Lat, start.Lon, end.Lat, end.Lon);

        // ------------------------------------------------------------------ prawy przycisk

        private static void OnMapRightButtonDown(Window win, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (_interactiveCanvas == null) return;
            FocusMapCanvas();
            if (_activeDraftPoly != null)
            {
                FinishDraftShape(win);
                return;
            }
            if (_activeTool == "MEASURE")
            {
                SelectTool("PAN");
                return;
            }

            Point p = e.GetPosition(_interactiveCanvas);
            var hit = HitTestShapeOutline(p);
            if (hit != null) ShowObjectMenu(win, hit, p);
            else OpenRingMenu(win, p);
        }

        /// <summary>Prawy klik na znaczniku, kropce albo uchwycie ✥ kształtu.</summary>
        private static void OnObjectRightClick(TacticalMapObjectItem obj, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (_hostWindow == null || _interactiveCanvas == null) return;
            FocusMapCanvas();
            if (_activeDraftPoly != null)
            {
                FinishDraftShape(_hostWindow);
                return;
            }
            ShowObjectMenu(_hostWindow, obj, e.GetPosition(_interactiveCanvas));
        }

        /// <summary>Kształt (AO, sektor, trasa), którego linia przebiega pod kursorem. Wnętrze obszaru się nie liczy.</summary>
        private static TacticalMapObjectItem? HitTestShapeOutline(Point canvasPt)
        {
            var (w, h) = GetExactCanvasSize();
            var candidates = _mapObjects.Where(o => MapObjectKinds.IsMultiPoint(o.Kind) && o.Points.Count >= 2).ToList();
            var outlines = candidates
                .Select(o => new HitOutline(
                    o.Points.Select(p =>
                    {
                        Point s = LatLonToScreen(p.Lat, p.Lon, w, h);
                        return new ScreenPoint(s.X, s.Y);
                    }).ToList(),
                    Closed: o.Kind != MapObjectKinds.Route))
                .ToList();
            int index = MapHitTest.FindClosestOutline(new ScreenPoint(canvasPt.X, canvasPt.Y), outlines, 7.0);
            return index >= 0 ? candidates[index] : null;
        }

        // ------------------------------------------------------------------ pierścień

        private static void OpenRingMenu(Window win, Point canvasPt)
        {
            if (_mapMenuLayer == null || _mapMenuCanvas == null || _interactiveCanvas == null || _viewportHostGrid == null) return;
            CloseMapMenus();
            var (w, h) = GetExactCanvasSize();
            var (lat, lon) = ScreenToLatLon(canvasPt.X, canvasPt.Y, w, h);

            var entries = new List<RadialMenuEntry>
            {
                new RadialMenuEntry("Znacznik NATO", "Postaw znacznik — wybierz symbol (N)", () => CreateMiniSymbolVectorBadge(_selectedNatoSymbolCode, _selectedObjectColorHex), () => ShowNatoSymbolPicker(win, canvasPt)),
                new RadialMenuEntry("Kropka", "Postaw kropkę w kolorze nowego obiektu (K)", RingDotIcon, () => PlacePointObjectAt(win, "DOT", canvasPt)),
                new RadialMenuEntry("Trasa", "Zacznij trasę w tym miejscu (T)", () => RingGlyph("〰"), () => StartShapeAt(win, "ROUTE", canvasPt)),
                new RadialMenuEntry("Sektor", "Zacznij sektor / strefę w tym miejscu (S)", () => RingGlyph("⬠"), () => StartShapeAt(win, "SECTOR", canvasPt)),
                new RadialMenuEntry("Teren gry (AO)", "Zacznij granicę terenu gry w tym miejscu (A)", () => RingGlyph("🛡"), () => StartShapeAt(win, "AO", canvasPt)),
                new RadialMenuEntry("Sektor ognia", "Dodaj sektor ognia / zasięg repliki w tym miejscu", () => RingGlyph("🎯"), () => AddWeaponRangeSectorAt(canvasPt)),
                new RadialMenuEntry("Pomiar", "Zmierz odległość i azymut od tego miejsca (L)", () => RingGlyph("📏"), () => StartMeasureAt(lat, lon)),
                new RadialMenuEntry("Kopiuj MGRS", "Skopiuj współrzędne tego miejsca do schowka", () => RingGlyph("📋"), () => CopyPointReport("Punkt", lat, lon))
            };

            var ring = new MapRadialMenu(entries);
            Point at = _interactiveCanvas.TranslatePoint(canvasPt, _viewportHostGrid);
            double margin = MapRadialMenu.OuterRadius + 4;
            double cx = Math.Clamp(at.X, margin, Math.Max(margin, _viewportHostGrid.ActualWidth - margin));
            double cy = Math.Clamp(at.Y, margin, Math.Max(margin, _viewportHostGrid.ActualHeight - margin));
            Canvas.SetLeft(ring.View, cx - ring.Width / 2.0);
            Canvas.SetTop(ring.View, cy - ring.Height / 2.0);
            _mapMenuCanvas.Children.Add(ring.View);
            _ringMenu = ring;
            _ringOpenedByPress = Mouse.RightButton == MouseButtonState.Pressed;
            _mapMenuLayer.Visibility = Visibility.Visible;
        }

        private static FrameworkElement RingGlyph(string glyph) =>
            new TextBlock { Text = glyph, Foreground = Brushes.White, FontSize = 17 };

        private static FrameworkElement RingDotIcon() => new System.Windows.Shapes.Ellipse
        {
            Width = 14,
            Height = 14,
            Fill = TacticalConfigPanel.BrushFromHex(_selectedObjectColorHex, Color.FromRgb(38, 132, 255)),
            Stroke = Brushes.White,
            StrokeThickness = 1.5
        };

        private static void OnMenuLayerMouseMove(MouseEventArgs e)
        {
            if (_ringMenu == null) return;
            double reach = _ringOpenedByPress ? double.MaxValue : MapRadialMenu.ClickReach;
            _ringMenu.SetHover(_ringMenu.IndexAt(e.GetPosition(_ringMenu.View), reach));
        }

        private static void OnMenuLayerLeftButtonDown(MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (_ringMenu != null)
            {
                int index = _ringMenu.IndexAt(e.GetPosition(_ringMenu.View), MapRadialMenu.ClickReach);
                if (index >= 0)
                {
                    InvokeRingEntry(index);
                    return;
                }
            }
            CloseMapMenus();
        }

        /// <summary>Gest: prawy przycisk przytrzymany od otwarcia pierścienia i puszczony nad pozycją ją wybiera.</summary>
        private static void OnMenuLayerRightButtonUp(MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (_ringMenu == null || !_ringOpenedByPress) return;
            _ringOpenedByPress = false;
            int index = _ringMenu.IndexAt(e.GetPosition(_ringMenu.View), double.MaxValue);
            if (index >= 0) InvokeRingEntry(index);
        }

        private static void InvokeRingEntry(int index)
        {
            var entry = _ringMenu?.Entry(index);
            CloseMapMenus();
            entry?.Invoke();
        }

        private static void CloseMapMenus()
        {
            _ringMenu = null;
            _ringOpenedByPress = false;
            _mapMenuCanvas?.Children.Clear();
            if (_mapMenuLayer != null) _mapMenuLayer.Visibility = Visibility.Collapsed;
        }

        private static void ShowFloatingPanel(FrameworkElement panel, Point canvasPt)
        {
            if (_interactiveCanvas == null || _viewportHostGrid == null) return;
            ShowFloatingPanelAtHost(panel, _interactiveCanvas.TranslatePoint(canvasPt, _viewportHostGrid));
        }

        private static void ShowFloatingPanelAtHost(FrameworkElement panel, Point at)
        {
            if (_mapMenuLayer == null || _mapMenuCanvas == null || _viewportHostGrid == null) return;
            CloseMapMenus();
            panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double width = panel.DesiredSize.Width, height = panel.DesiredSize.Height;
            double x = Math.Max(4, Math.Min(at.X + 6, _viewportHostGrid.ActualWidth - width - 4));
            double y = Math.Max(4, Math.Min(at.Y + 6, _viewportHostGrid.ActualHeight - height - 4));
            Canvas.SetLeft(panel, x);
            Canvas.SetTop(panel, y);
            _mapMenuCanvas.Children.Add(panel);
            _mapMenuLayer.Visibility = Visibility.Visible;
        }

        // ------------------------------------------------------------------ akcje pierścienia

        private static void ShowNatoSymbolPicker(Window win, Point canvasPt)
        {
            const int columns = 6;
            var tiles = new WrapPanel { Width = columns * 42 };
            foreach (var option in NatoSymbols)
            {
                string code = option.Code;
                tiles.Children.Add(MapEditorUi.TileButton(CreateMiniSymbolVectorBadge(code, _selectedObjectColorHex), option.Label, code == _selectedNatoSymbolCode, () =>
                {
                    CloseMapMenus();
                    _selectedNatoSymbolCode = code;
                    SyncNatoSymbolCombo();
                    UpdateToolHint();
                    PlacePointObjectAt(win, "MARKER", canvasPt);
                }));
            }

            var content = new StackPanel();
            content.Children.Add(new TextBlock
            {
                Text = "Znacznik NATO — wybierz symbol",
                Foreground = TacticalConfigPanel.BrushAccent,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(4, 2, 4, 6)
            });
            content.Children.Add(tiles);
            ShowFloatingPanel(MapEditorUi.FloatingPanel(content, columns * 42 + 10), canvasPt);
        }

        private static void SyncNatoSymbolCombo()
        {
            if (_natoSymbolCombo == null) return;
            var option = NatoSymbols.FirstOrDefault(n => n.Code == _selectedNatoSymbolCode);
            if (option != null && !ReferenceEquals(_natoSymbolCombo.SelectedItem, option)) _natoSymbolCombo.SelectedItem = option;
        }

        private static void PlacePointObjectAt(Window win, string tool, Point canvasPt)
        {
            PlaceWithTool(win, tool, canvasPt);
            AnnounceNewObject();
            RedrawVectorsOnly();
            UpdateOfflineTileEstimateUI();
        }

        private static void AnnounceNewObject()
        {
            if (_inspectedObject != null)
                ShowMapStatus($"Dodano „{_inspectedObject.Name}” — F2 zmienia nazwę, prawy klik na obiekcie otwiera jego menu.");
        }

        private static void StartShapeAt(Window win, string tool, Point canvasPt)
        {
            SelectTool(tool);
            PlaceWithTool(win, tool, canvasPt);
            RedrawVectorsOnly();
            UpdateOfflineTileEstimateUI();
        }

        private static void StartMeasureAt(double lat, double lon)
        {
            SelectTool("MEASURE");
            _rulerStart = (lat, lon);
            _rulerEnd = null;
            _fastOverlayRenderer?.InvalidateVisual();
        }

        private static void CopyPointReport(string title, double lat, double lon)
        {
            var (vw, vh) = GetExactCanvasSize();
            string sector = FormatSectorCoordinateForLatLon(lat, lon, vw, vh);
            var (fullMgrs, _, keypad, utm, wgs) = FormatTacticalPointCoordinates(lat, lon);
            string report = $"{title} | {sector} | MGRS: {fullMgrs} ({keypad}) | WGS84: {wgs} | UTM: {utm}";
            try
            {
                Clipboard.SetText(report);
                ShowMapStatus($"✓ Skopiowano do schowka: {report}");
            }
            catch (ExternalException)
            {
                ShowMapStatus("Schowek jest zajęty przez inny program — spróbuj jeszcze raz.");
            }
        }

        // ------------------------------------------------------------------ menu obiektu

        private static void ShowObjectMenu(Window win, TacticalMapObjectItem obj, Point canvasPt)
        {
            if (!_mapObjects.Contains(obj)) return;
            SelectObjectFromList(obj, centerMap: false);

            var items = new StackPanel();
            var header = new Grid { Margin = new Thickness(4, 2, 4, 4) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var badge = CreateObjectBadge(obj);
            badge.Margin = new Thickness(0, 0, 8, 0);
            badge.VerticalAlignment = VerticalAlignment.Center;
            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(obj.Name) ? "(bez nazwy)" : obj.Name,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 11.5,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            titles.Children.Add(new TextBlock { Text = MapObjectKinds.DisplayName(obj.Kind), Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 10 });
            Grid.SetColumn(badge, 0);
            Grid.SetColumn(titles, 1);
            header.Children.Add(badge);
            header.Children.Add(titles);
            items.Children.Add(header);
            items.Children.Add(MapEditorUi.MenuSeparator());

            void Add(string glyph, string text, string? keys, Action action, bool danger = false) =>
                items.Children.Add(MapEditorUi.MenuItem(glyph, text, keys, () =>
                {
                    CloseMapMenus();
                    action();
                }, danger));

            Add("✏", "Zmień nazwę", null, () => FocusObjectNameBox(obj));
            Add("●", "Zmień kolor…", null, () => PickObjectColor(win, obj));
            if (MapObjectKinds.IsMultiPoint(obj.Kind))
            {
                foreach (var kind in MapObjectKinds.AvailableGeometryKinds(obj.Kind, obj.Points.Count).Where(k => k != obj.Kind))
                {
                    string target = kind;
                    Add("⇄", $"Zamień na: {MapObjectKinds.DisplayName(target)}", null, () => ChangeObjectGeometryKind(win, obj, target));
                }
            }
            Add("❏", "Duplikuj", "Ctrl+D", () => DuplicateMapObject(win, obj));
            if (MapObjectCardSpec.For(obj.Kind, obj.Points.Count).CanSimplify)
                Add("✂", $"Uprość kształt ({obj.Points.Count} pkt)", null, () => SimplifyObjectGeometry(win, obj));
            Add("📋", "Kopiuj współrzędne", null, () =>
            {
                var (lat, lon) = ObjectAnchor(obj);
                CopyPointReport(string.IsNullOrWhiteSpace(obj.Name) ? "Obiekt" : obj.Name, lat, lon);
            });
            Add("☰", "Pokaż na liście", null, () => RevealListRow(obj));
            items.Children.Add(MapEditorUi.MenuSeparator());
            Add("−", "Usuń z mapy", "Del", () => DeleteMapObject(win, obj), danger: true);

            ShowFloatingPanel(MapEditorUi.FloatingPanel(items, 240), canvasPt);
        }

        /// <summary>Punkt obiektu do współrzędnych: znacznik — jego położenie, kształt — środek wierzchołków.</summary>
        private static (double Lat, double Lon) ObjectAnchor(TacticalMapObjectItem obj)
        {
            if (obj.Points.Count == 0) return (_centerLat, _centerLon);
            if (obj.Points.Count == 1 || !MapObjectKinds.IsMultiPoint(obj.Kind)) return obj.Points[0];
            return (obj.Points.Average(p => p.Lat), obj.Points.Average(p => p.Lon));
        }

        private static void PickObjectColor(Window win, TacticalMapObjectItem obj)
        {
            string picked = TacticalConfigPanel.ShowRgbColorPickerDialog(win, $"Kolor — {obj.Name}", obj.ColorHex);
            if (string.IsNullOrEmpty(picked)) return;
            obj.ColorHex = picked;
            TacticalConfigPanel.MarkDirty();
            QueueObjectsListRefresh(win);
            RedrawVectorsOnly();
        }

        /// <summary>Zaznacza obiekt (jego karta trafia do panelu szczegółów) i ustawia kursor w polu nazwy.</summary>
        private static void FocusObjectNameBox(TacticalMapObjectItem obj)
        {
            SelectObjectFromList(obj, centerMap: false);
            if (_objectNameBoxes.TryGetValue(obj, out var box)) FocusTextBoxSoon(box);
        }

        /// <summary>Kopia obiektu przesunięta o kilkanaście pikseli w prawo i w dół, wstawiona zaraz za oryginałem.</summary>
        private static void DuplicateMapObject(Window win, TacticalMapObjectItem obj)
        {
            if (!_mapObjects.Contains(obj)) return;
            var (w, h) = GetExactCanvasSize();
            var copy = new TacticalMapObjectItem
            {
                Kind = obj.Kind,
                SymbolCode = obj.SymbolCode,
                Name = string.IsNullOrWhiteSpace(obj.Name) ? "Kopia" : obj.Name + " (kopia)",
                LegendDescription = obj.LegendDescription,
                ColorHex = obj.ColorHex,
                ShowLabel = obj.ShowLabel,
                SizeMultiplier = obj.SizeMultiplier,
                RotationDeg = obj.RotationDeg
            };
            foreach (var p in obj.Points)
            {
                Point s = LatLonToScreen(p.Lat, p.Lon, w, h);
                copy.Points.Add(ScreenToLatLon(s.X + 18, s.Y + 18, w, h));
            }

            int index = _mapObjects.IndexOf(obj);
            _mapObjects.Insert(index >= 0 ? index + 1 : _mapObjects.Count, copy);
            _inspectedObject = copy;
            _inspectedVertexIndex = 0;
            TacticalConfigPanel.MarkDirty();
            ShowMapStatus($"Zduplikowano „{obj.Name}”.");
            UpdateSelectedMarkerInspectorUI();
            QueueObjectsListRefresh(win);
            RedrawVectorsOnly();
            UpdateOfflineTileEstimateUI();
        }

        // ------------------------------------------------------------------ klawiatura

        private static void OnMapPreviewKeyDown(Window win, KeyEventArgs e)
        {
            if (_fullScreenOverlay == null || _fullScreenOverlay.Visibility != Visibility.Visible) return;
            var focused = Keyboard.FocusedElement;
            bool inText = focused is TextBoxBase || focused is PasswordBox;
            bool inCombo = focused is ComboBox || focused is ComboBoxItem;

            if (e.Key == Key.Escape)
            {
                if (IsMapMenuOpen)
                {
                    CloseMapMenus();
                    e.Handled = true;
                    return;
                }
                if (inCombo) return;
                if (inText)
                {
                    FocusMapCanvas();
                    e.Handled = true;
                    return;
                }
                e.Handled = HandleMapEscape(win);
                return;
            }

            // W polu tekstowym Ctrl+Z cofa pisanie w tym polu (zwykłe cofanie Windows), a nie zmiany na mapie.
            if (inText || inCombo || IsMapMenuOpen) return;
            var modifiers = Keyboard.Modifiers;
            DropStaleListSelection();
            SyncListSelectionWithMap();
            var selected = _listSelection; // obiekt mapy albo sektor ognia — wspólne zaznaczenie listy i mapy

            if (modifiers == ModifierKeys.Control && e.Key == Key.Z)
            {
                UndoMapEdit();
                e.Handled = true;
                return;
            }
            if ((modifiers == ModifierKeys.Control && e.Key == Key.Y) || (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Z))
            {
                RedoMapEdit();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Enter && _activeDraftPoly != null)
            {
                FinishDraftShape(win);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Delete && selected != null)
            {
                DeleteListItem(win, selected);
                e.Handled = true;
                return;
            }
            if (modifiers == ModifierKeys.Control && e.Key == Key.D && selected != null)
            {
                DuplicateListItem(win, selected);
                e.Handled = true;
                return;
            }
            if (modifiers != ModifierKeys.None) return;
            if (e.Key == Key.F2 && selected != null)
            {
                FocusListItemName(selected);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.F && selected != null)
            {
                SelectListItem(selected, centerMap: true);
                e.Handled = true;
                return;
            }
            var tool = MapTools.FirstOrDefault(t => t.Key == e.Key);
            if (tool != null)
            {
                ToggleTool(tool.Tool);
                e.Handled = true;
            }
        }

        /// <summary>Esc po kolei: kończy kształt, wyłącza narzędzie, czyści pomiar, odznacza obiekt.</summary>
        private static bool HandleMapEscape(Window win)
        {
            if (_activeDraftPoly != null)
            {
                FinishDraftShape(win);
                return true;
            }
            if (_activeTool != "PAN")
            {
                if (_activeTool == "MEASURE")
                {
                    _rulerStart = null;
                    _rulerEnd = null;
                }
                SelectTool("PAN");
                return true;
            }
            if (_rulerStart != null)
            {
                _rulerStart = null;
                _rulerEnd = null;
                _fastOverlayRenderer?.InvalidateVisual();
                return true;
            }
            if (_inspectedObject != null || _listSelection != null)
            {
                ClearListSelection();
                return true;
            }
            return false;
        }
    }
}
