using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FieldTakHub.Builder.Services.MapEditor;

namespace FieldTakHub.Builder.Services
{
    /// <summary>
    /// Jedna lista wszystkiego, co jest na mapie: znaczniki NATO, kropki, teren gry (AO), sektory, trasy i sektory ognia.
    /// Wiersz listy jest zwarty — [Aa / 👁] [● kolor] [symbol] [nazwa · opis] [− usuń] — a zaznaczony element rozwija się
    /// w panelu szczegółów pod listą: pełna karta obiektu (<see cref="MapObjectCardSpec"/>) albo edytor sektora ognia.
    /// Zaznaczenie jest wspólne z mapą: kliknięcie na mapie zaznacza wiersz, a kliknięcie wiersza — element na mapie.
    /// </summary>
    public static partial class TacticalMapGeneratorView
    {
        private sealed class MapListRow
        {
            public MapListRow(Border row, CheckBox toggle, Button swatch, Border badgeHost, TextBlock text, string badgeKey)
            {
                Row = row;
                Toggle = toggle;
                Swatch = swatch;
                BadgeHost = badgeHost;
                Text = text;
                BadgeKey = badgeKey;
            }

            public Border Row { get; }
            public CheckBox Toggle { get; }
            public Button Swatch { get; }
            public Border BadgeHost { get; }
            public TextBlock Text { get; }
            public string BadgeKey { get; set; }
        }

        private static readonly Dictionary<object, MapListRow> _listRows = new Dictionary<object, MapListRow>();
        private static bool _objectsListRefreshQueued;
        private static bool _isUpdatingListRows;

        /// <summary>Zaznaczony element listy: <see cref="TacticalMapObjectItem"/> albo <see cref="TacticalRangeSectorItem"/> (null — nic).</summary>
        private static object? _listSelection;
        private static object? _detailsItem;
        private static object? _listScrolledTo;
        private static Border? _mapItemDetailsHost;
        private static TextBlock? _mapItemsCountText;
        private static CheckBox? _detailsSectorToggle;
        private static Button? _detailsSectorSwatch;
        private static Border? _detailsSectorBadgeHost;
        private static string? _detailsSectorBadgeKey;
        private static TextBlock? _detailsSectorCaption;
        private static bool _redrawVectorsAfterSectorGesture;
        private static bool _restyleSectorsAfterSectorGesture;

        // ------------------------------------------------------------------ lista

        private static void RefreshObjectsListUI(Window win)
        {
            if (_objectsListStack == null) return;
            // Np. po kliknięciu „−” fokus jest na przycisku, który za chwilę zniknie — bez tego Ctrl+Z i inne
            // skróty nie działałyby, dopóki użytkownik nie kliknie w mapę.
            if (_objectsListStack.IsKeyboardFocusWithin) FocusMapCanvas();
            _objectsListStack.Children.Clear();
            _listRows.Clear();
            DropStaleListSelection();
            UpdateMapItemsCount();

            if (_mapObjects.Count == 0 && _weaponRangeSectors.Count == 0)
            {
                _objectsListStack.Children.Add(new TextBlock
                {
                    Text = "Brak obiektów na mapie — narysuj je narzędziami nad mapą (albo prawym klikiem) lub wczytaj plik .KMZ / .GPX",
                    Foreground = TacticalConfigPanel.BrushMutedText,
                    FontSize = 9.5,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 8, 0, 8)
                });
            }
            else
            {
                foreach (var obj in _mapObjects.ToList())
                    _objectsListStack.Children.Add(BuildObjectListRow(win, obj));
                if (_weaponRangeSectors.Count > 0)
                {
                    _objectsListStack.Children.Add(new TextBlock
                    {
                        Text = "🎯 Sektory ognia",
                        Foreground = TacticalConfigPanel.BrushAccent,
                        FontSize = 9.5,
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(2, _mapObjects.Count > 0 ? 6 : 0, 0, 3)
                    });
                    foreach (var sec in _weaponRangeSectors.ToList())
                        _objectsListStack.Children.Add(BuildRangeSectorListRow(sec));
                }
            }
            ShowListSelectionDetails(win, force: true);
        }

        /// <summary>Przebudowa listy po bieżącym zdarzeniu — gdy zmiana pochodzi z kontrolki, która sama jest na tej liście.</summary>
        private static void QueueObjectsListRefresh(Window win)
        {
            if (_objectsListRefreshQueued) return;
            _objectsListRefreshQueued = true;
            win.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                _objectsListRefreshQueued = false;
                RefreshObjectsListUI(win);
            }));
        }

        private static void QueueObjectsListRefresh()
        {
            if (_hostWindow != null) QueueObjectsListRefresh(_hostWindow);
        }

        /// <summary>
        /// Odświeża wiersze sektorów ognia w miejscu (np. po każdej literze nazwy w edytorze — pole nie traci fokusu).
        /// Gdy zmienił się skład listy (doszły albo zniknęły elementy), przebudowuje ją po bieżącym zdarzeniu.
        /// </summary>
        private static void RefreshRangeSectorRows()
        {
            if (_objectsListStack == null) return;
            if (!MapListMatchesContent())
            {
                QueueObjectsListRefresh();
                return;
            }
            foreach (var sec in _weaponRangeSectors) UpdateListRow(sec);
            UpdateRangeSectorDetailsHeader();
        }

        private static bool MapListMatchesContent() =>
            _listRows.Count == _mapObjects.Count + _weaponRangeSectors.Count
            && _mapObjects.All(o => _listRows.ContainsKey(o))
            && _weaponRangeSectors.All(s => _listRows.ContainsKey(s));

        private static void UpdateMapItemsCount()
        {
            if (_mapItemsCountText != null)
                _mapItemsCountText.Text = MapListText.CountSummary(_mapObjects.Count, _weaponRangeSectors.Count);
        }

        private static Border BuildObjectListRow(Window win, TacticalMapObjectItem obj)
        {
            NormalizeObjectForEditing(obj);
            var row = MapEditorUi.ListRow(obj, ReferenceEquals(_listSelection, obj), () => ReferenceEquals(_listSelection, obj));
            row.PreviewMouseLeftButtonDown += (_, e) => SelectListItem(obj, centerMap: e.ClickCount >= 2);

            var toggle = MapEditorUi.Toggle("Aa", obj.ShowLabel, "Etykieta z nazwą tego obiektu na mapie (wł./wył.). Działa, gdy zaznaczone jest „Pokazuj etykiety nazw na mapie”.", on =>
            {
                if (_isUpdatingListRows) return;
                obj.ShowLabel = on;
                MarkDirtyUnlessRebuilding();
                RedrawVectorsOnly();
                RefreshDetailsIfShowing(win, obj);
            });
            var swatch = MapEditorUi.ColorSwatch(obj.ColorHex, ObjectColorTooltip(obj.ColorHex), _ => PickObjectColor(win, obj), size: 16);
            var badgeHost = new Border { Child = CreateObjectBadge(obj), ToolTip = MapObjectKinds.DisplayName(obj.Kind) };
            var text = RowTextBlock();
            SetRowText(text, obj.Name, ObjectRowInfo(obj));
            var delete = MapEditorUi.DeleteButton("Usuń ten obiekt z mapy (Del) — Ctrl+Z cofa", () => DeleteMapObject(win, obj));

            row.Child = MapEditorUi.HeaderRow(toggle, swatch, new UIElement[] { badgeHost }, text, delete);
            _listRows[obj] = new MapListRow(row, toggle, swatch, badgeHost, text, ObjectBadgeKey(obj));
            ScrollRowIntoViewWhenLoaded(obj, row);
            return row;
        }

        private static Border BuildRangeSectorListRow(TacticalRangeSectorItem sec)
        {
            var row = MapEditorUi.ListRow(sec, ReferenceEquals(_listSelection, sec), () => ReferenceEquals(_listSelection, sec));
            row.PreviewMouseLeftButtonDown += (_, e) => SelectListItem(sec, centerMap: e.ClickCount >= 2);

            var toggle = MapEditorUi.Toggle("👁", sec.IsVisible, "Pokaż ten sektor ognia na mapie (wł./wył.)", on => SetRangeSectorVisible(sec, on));
            var swatch = MapEditorUi.ColorSwatch(sec.ColorHex, RangeSectorColorTooltip(sec.ColorHex), _ => PickRangeSectorColor(sec), size: 16);
            var badgeHost = new Border { Child = CreateRangeSectorBadge(sec), ToolTip = "Sektor ognia / zasięg repliki" };
            var text = RowTextBlock();
            SetRowText(text, sec.Name, RangeSectorRowInfo(sec));
            var delete = MapEditorUi.DeleteButton("Usuń ten sektor ognia z mapy (Del) — Ctrl+Z cofa", () => DeleteRangeSector(sec));

            row.Child = MapEditorUi.HeaderRow(toggle, swatch, new UIElement[] { badgeHost }, text, delete);
            _listRows[sec] = new MapListRow(row, toggle, swatch, badgeHost, text, RangeSectorBadgeKey(sec));
            ScrollRowIntoViewWhenLoaded(sec, row);
            return row;
        }

        private static TextBlock RowTextBlock() => new TextBlock
        {
            FontSize = 10.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

        private static void SetRowText(TextBlock text, string? name, string info)
        {
            string shown = string.IsNullOrWhiteSpace(name) ? "(bez nazwy)" : name!;
            text.Inlines.Clear();
            text.Inlines.Add(new System.Windows.Documents.Run(shown) { Foreground = Brushes.White, FontWeight = FontWeights.SemiBold });
            text.Inlines.Add(new System.Windows.Documents.Run("   " + info) { Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 9.5 });
            text.ToolTip = shown + " — " + info;
        }

        /// <summary>Uaktualnia jeden wiersz (nazwa, opis, kolor, przełącznik) bez przebudowy listy.</summary>
        private static void UpdateListRow(object item)
        {
            if (!_listRows.TryGetValue(item, out var row)) return;
            _isUpdatingListRows = true;
            try
            {
                if (item is TacticalMapObjectItem obj)
                {
                    row.Toggle.IsChecked = obj.ShowLabel;
                    MapEditorUi.SetSwatchColor(row.Swatch, obj.ColorHex, ObjectColorTooltip(obj.ColorHex));
                    string key = ObjectBadgeKey(obj);
                    if (key != row.BadgeKey)
                    {
                        row.BadgeHost.Child = CreateObjectBadge(obj);
                        row.BadgeKey = key;
                    }
                    SetRowText(row.Text, obj.Name, ObjectRowInfo(obj));
                }
                else if (item is TacticalRangeSectorItem sec)
                {
                    row.Toggle.IsChecked = sec.IsVisible;
                    MapEditorUi.SetSwatchColor(row.Swatch, sec.ColorHex, RangeSectorColorTooltip(sec.ColorHex));
                    string key = RangeSectorBadgeKey(sec);
                    if (key != row.BadgeKey)
                    {
                        row.BadgeHost.Child = CreateRangeSectorBadge(sec);
                        row.BadgeKey = key;
                    }
                    SetRowText(row.Text, sec.Name, RangeSectorRowInfo(sec));
                }
            }
            finally
            {
                _isUpdatingListRows = false;
            }
        }

        private static string ObjectRowInfo(TacticalMapObjectItem obj)
        {
            string? symbolLabel = obj.Kind == MapObjectKinds.Marker ? NatoSymbols.FirstOrDefault(n => n.Code == obj.SymbolCode)?.Label : null;
            return MapListText.ObjectInfo(obj.Kind, symbolLabel, obj.Points.Count);
        }

        private static string RangeSectorRowInfo(TacticalRangeSectorItem sec) =>
            MapListText.FireSectorInfo(sec.BaseRadiusMeters, sec.GetEffectiveRadiusMeters(), sec.AzimuthDeg, sec.SpreadAngleDeg, sec.ElevationDeltaMeters)
            + (sec.IsVisible ? "" : " · ukryty");

        private static string ObjectBadgeKey(TacticalMapObjectItem obj) => obj.Kind + "|" + obj.SymbolCode + "|" + obj.ColorHex;

        private static string RangeSectorBadgeKey(TacticalRangeSectorItem sec) =>
            sec.ColorHex + "|" + Math.Round(Math.Clamp(sec.SpreadAngleDeg, 20.0, 360.0) / 10.0);

        /// <summary>Ikona sektora ognia w tym samym formacie co symbole obiektów: wycinek (albo okrąg) w kolorze sektora.</summary>
        private static Border CreateRangeSectorBadge(TacticalRangeSectorItem sec)
        {
            Color c = Color.FromRgb(31, 224, 197);
            try { if (!string.IsNullOrWhiteSpace(sec.ColorHex)) c = (Color)ColorConverter.ConvertFromString(sec.ColorHex); } catch { }
            var stroke = new SolidColorBrush(c);
            var fill = new SolidColorBrush(Color.FromArgb(150, c.R, c.G, c.B));
            stroke.Freeze();
            fill.Freeze();

            bool fullCircle = sec.SpreadAngleDeg >= 359.5;
            var canvas = new Canvas { Width = 36.0, Height = 32.0 };
            if (fullCircle)
            {
                var ring = new System.Windows.Shapes.Ellipse { Width = 24, Height = 24, Fill = fill, Stroke = stroke, StrokeThickness = 1.8 };
                Canvas.SetLeft(ring, 6);
                Canvas.SetTop(ring, 4);
                canvas.Children.Add(ring);
            }
            else
            {
                const double cx = 18.0, cy = 27.0, r = 20.0;
                double half = Math.Clamp(sec.SpreadAngleDeg, 20.0, 120.0) / 2.0 * Math.PI / 180.0;
                var figure = new PathFigure { StartPoint = new Point(cx, cy), IsClosed = true, IsFilled = true };
                figure.Segments.Add(new LineSegment(new Point(cx - r * Math.Sin(half), cy - r * Math.Cos(half)), true));
                figure.Segments.Add(new ArcSegment(new Point(cx + r * Math.Sin(half), cy - r * Math.Cos(half)), new Size(r, r), 0.0, false, SweepDirection.Clockwise, true));
                var geometry = new PathGeometry();
                geometry.Figures.Add(figure);
                canvas.Children.Add(new System.Windows.Shapes.Path { Data = geometry, Fill = fill, Stroke = stroke, StrokeThickness = 1.8 });
            }
            var shooter = new System.Windows.Shapes.Ellipse { Width = 6, Height = 6, Fill = Brushes.White };
            Canvas.SetLeft(shooter, 15);
            Canvas.SetTop(shooter, fullCircle ? 13 : 24);
            canvas.Children.Add(shooter);

            return new Border
            {
                Width = 32,
                Height = 22,
                Background = new SolidColorBrush(Color.FromArgb(180, 6, 14, 18)),
                BorderBrush = TacticalConfigPanel.BrushTileBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Child = new Viewbox { Width = 28, Height = 20, Stretch = Stretch.Uniform, Child = canvas }
            };
        }

        // ------------------------------------------------------------------ panel szczegółów

        /// <summary>Panel pod listą: pełna karta zaznaczonego obiektu, edytor zaznaczonego sektora ognia albo podpowiedź.</summary>
        private static void ShowListSelectionDetails(Window win, bool force)
        {
            if (_mapItemDetailsHost == null) return;
            DropStaleListSelection();
            object? item = _listSelection;
            if (!force && ReferenceEquals(item, _detailsItem) && _mapItemDetailsHost.Child != null) return;
            // Zamiana karty zabrałaby fokus z jej pola — wtedy klawisze skrótów przestałyby działać do kliknięcia w mapę.
            if (_mapItemDetailsHost.IsKeyboardFocusWithin) FocusMapCanvas();

            _detailsItem = item;
            DetachRangeSectorEditor();
            _objectNameBoxes.Clear();
            _detailsSectorToggle = null;
            _detailsSectorSwatch = null;
            _detailsSectorBadgeHost = null;
            _detailsSectorBadgeKey = null;
            _detailsSectorCaption = null;

            if (item is TacticalMapObjectItem obj) _mapItemDetailsHost.Child = BuildObjectCard(win, obj);
            else if (item is TacticalRangeSectorItem sec) _mapItemDetailsHost.Child = BuildRangeSectorDetailsCard(sec);
            else _mapItemDetailsHost.Child = _mapObjects.Count + _weaponRangeSectors.Count > 0 ? DetailsHint() : null;
        }

        private static Border DetailsHint() => new Border
        {
            BorderBrush = TacticalConfigPanel.BrushTileBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Child = new TextBlock
            {
                Text = "Zaznacz element na liście albo kliknij go na mapie — tutaj pojawią się jego ustawienia: nazwa, kolor, wielkość i kształt, a dla sektora ognia zasięg, kierunek i ΔH.",
                Foreground = TacticalConfigPanel.BrushMutedText,
                FontSize = 9.5,
                TextWrapping = TextWrapping.Wrap
            }
        };

        /// <summary>Edytor sektora ognia jest jeden — przed pokazaniem w nowej karcie trzeba go zdjąć z poprzedniej.</summary>
        private static void DetachRangeSectorEditor()
        {
            if (_rangeSectorEditorBox == null) return;
            switch (_rangeSectorEditorBox.Parent)
            {
                case Panel panel:
                    panel.Children.Remove(_rangeSectorEditorBox);
                    break;
                case Decorator decorator:
                    decorator.Child = null;
                    break;
                case ContentControl content:
                    content.Content = null;
                    break;
            }
        }

        /// <summary>Karta sektora ognia: ten sam nagłówek co w karcie obiektu [👁] [●] [📍] [ikona] [opis] [−], a pod nim edytor.</summary>
        private static Border BuildRangeSectorDetailsCard(TacticalRangeSectorItem sec)
        {
            _selectedRangeSector = sec;
            PopulateRangeSectorEditorFromSelected();

            var card = MapEditorUi.Card(sec, selected: true);
            var stack = new StackPanel();

            var visible = MapEditorUi.Toggle("👁", sec.IsVisible, "Pokaż ten sektor ognia na mapie (wł./wył.)", on => SetRangeSectorVisible(sec, on));
            var swatch = MapEditorUi.ColorSwatch(sec.ColorHex, RangeSectorColorTooltip(sec.ColorHex), _ => PickRangeSectorColor(sec));
            var locate = MapEditorUi.IconButton("📍", "Pokaż na mapie: wyśrodkuj widok na stanowisku i odczytaj jego współrzędne", () => SelectListItem(sec, centerMap: true));
            var badgeHost = new Border { ToolTip = "Sektor ognia / zasięg repliki" };
            var caption = new TextBlock
            {
                FontSize = 10,
                Foreground = TacticalConfigPanel.BrushMutedText,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            var delete = MapEditorUi.DeleteButton("Usuń ten sektor ognia z mapy (Del) — Ctrl+Z cofa", () => DeleteRangeSector(sec));
            stack.Children.Add(MapEditorUi.HeaderRow(visible, swatch, new UIElement[] { locate, badgeHost }, caption, delete));

            _detailsSectorToggle = visible;
            _detailsSectorSwatch = swatch;
            _detailsSectorBadgeHost = badgeHost;
            _detailsSectorCaption = caption;
            UpdateRangeSectorDetailsHeader();

            stack.Children.Add(new TextBlock
            {
                Text = "Na mapie: przeciągnij 🎯 albo wycinek — przesunięcie · uchwyt ↻ lub prawy przycisk — kierunek · boczne ◆ — szerokość · kółko myszy — obrót co 5° (Shift — kąt, Ctrl — promień)",
                Foreground = TacticalConfigPanel.BrushMutedText,
                FontSize = 9,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 5, 0, 6)
            });

            if (_rangeSectorEditorBox != null)
            {
                _rangeSectorEditorBox.Visibility = Visibility.Visible;
                stack.Children.Add(_rangeSectorEditorBox);
            }
            card.Child = stack;
            return card;
        }

        private static void UpdateRangeSectorDetailsHeader()
        {
            if (_detailsItem is not TacticalRangeSectorItem sec || _detailsSectorCaption == null) return;
            _isUpdatingListRows = true;
            try
            {
                if (_detailsSectorToggle != null) _detailsSectorToggle.IsChecked = sec.IsVisible;
                if (_detailsSectorSwatch != null) MapEditorUi.SetSwatchColor(_detailsSectorSwatch, sec.ColorHex, RangeSectorColorTooltip(sec.ColorHex));
                string key = RangeSectorBadgeKey(sec);
                if (_detailsSectorBadgeHost != null && key != _detailsSectorBadgeKey)
                {
                    _detailsSectorBadgeHost.Child = CreateRangeSectorBadge(sec);
                    _detailsSectorBadgeKey = key;
                }
                _detailsSectorCaption.Text = "Sektor ognia · " + RangeSectorRowInfo(sec);
            }
            finally
            {
                _isUpdatingListRows = false;
            }
        }

        private static void RefreshDetailsIfShowing(Window win, object item)
        {
            if (ReferenceEquals(_detailsItem, item)) ShowListSelectionDetails(win, force: true);
        }

        /// <summary>
        /// Pełna karta obiektu w panelu szczegółów. Każdy obiekt — znacznik NATO, kropka, teren gry (AO), sektor, trasa —
        /// ma ten sam układ (<see cref="MapObjectCardSpec"/>): [Aa etykieta] [● kolor] [📍] [symbol] [nazwa] [− usuń],
        /// „W legendzie”, „Wielkość”, a dalej „Obrót” albo „Geometria”.
        /// </summary>
        private static Border BuildObjectCard(Window win, TacticalMapObjectItem obj)
        {
            NormalizeObjectForEditing(obj);
            var layout = MapObjectCardSpec.For(obj.Kind, obj.Points.Count);

            var card = MapEditorUi.Card(obj, selected: true);
            // Każde kliknięcie w karcie (także w pole nazwy czy suwak) zaznacza obiekt — bez przebudowy listy.
            card.PreviewMouseLeftButtonDown += (_, _) => SelectObjectFromList(obj, centerMap: false);
            var stack = new StackPanel();

            // Wiersz 1: [Aa] [● kolor] [📍] [symbol] [nazwa] [− usuń] — ten sam dla każdego rodzaju obiektu.
            var labelToggle = MapEditorUi.Toggle("Aa", obj.ShowLabel, "Etykieta z nazwą tego obiektu na mapie (wł./wył.). Działa, gdy zaznaczone jest „Pokazuj etykiety nazw na mapie”.", on =>
            {
                obj.ShowLabel = on;
                MarkDirtyUnlessRebuilding();
                UpdateListRow(obj);
                RedrawVectorsOnly();
            });

            var badgeHost = new Border { Child = CreateObjectBadge(obj), ToolTip = MapObjectKinds.DisplayName(obj.Kind) };

            var swatch = MapEditorUi.ColorSwatch(obj.ColorHex, ObjectColorTooltip(obj.ColorHex), button =>
            {
                string? picked = TacticalConfigPanel.ShowRgbColorPickerDialog(win, $"Kolor — {obj.Name}", obj.ColorHex);
                if (string.IsNullOrEmpty(picked)) return;
                obj.ColorHex = picked;
                MapEditorUi.SetSwatchColor(button, picked, ObjectColorTooltip(picked));
                badgeHost.Child = CreateObjectBadge(obj);
                TacticalConfigPanel.MarkDirty();
                UpdateListRow(obj);
                RedrawVectorsOnly();
            });

            var locate = MapEditorUi.IconButton("📍", "Pokaż na mapie: wyśrodkuj widok i odczytaj współrzędne (MGRS, sektor)", () => SelectObjectFromList(obj, centerMap: true));

            var nameBox = MapEditorUi.TextField(obj.Name ?? "", "Nazwa obiektu — na mapie i w legendzie (F2)", emphasized: true);
            _objectNameBoxes[obj] = nameBox;
            nameBox.TextChanged += (_, _) =>
            {
                obj.Name = nameBox.Text;
                MarkDirtyUnlessRebuilding();
                UpdateListRow(obj);
                UpdateSelectedMarkerInspectorUI();
                RedrawVectorsOnly();
            };

            var delete = MapEditorUi.DeleteButton("Usuń ten obiekt z mapy (Del) — Ctrl+Z cofa", () => DeleteMapObject(win, obj));
            stack.Children.Add(MapEditorUi.HeaderRow(labelToggle, swatch, new UIElement[] { locate, badgeHost }, nameBox, delete));

            // Wiersz 2: opis w legendzie.
            var legendBox = MapEditorUi.TextField(obj.LegendDescription ?? "", "Opis tego obiektu w legendzie mapy", emphasized: false);
            legendBox.TextChanged += (_, _) =>
            {
                obj.LegendDescription = legendBox.Text;
                MarkDirtyUnlessRebuilding();
                RedrawVectorsOnly();
            };
            stack.Children.Add(MapEditorUi.FieldRow("W legendzie:", legendBox));

            // Wiersz 3: wielkość — ten sam suwak i ten sam format wartości dla każdego rodzaju.
            var sizeValue = MapEditorUi.ValueText(FormatObjectSizeLabel(obj));
            var sizeSlider = MapEditorUi.ValueSlider(MapObjectCardSpec.MinSizeMultiplier, MapObjectCardSpec.MaxSizeMultiplier,
                MapObjectCardSpec.NormalizeSize(obj.SizeMultiplier), 0.05, value =>
                {
                    obj.SizeMultiplier = Math.Round(value, 2);
                    sizeValue.Text = FormatObjectSizeLabel(obj);
                    MarkDirtyUnlessRebuilding();
                    RedrawVectorsOnly();
                });
            stack.Children.Add(MapEditorUi.FieldRow("Wielkość:", sizeSlider, sizeValue));

            // Wiersz 4: obrót (znacznik NATO) albo geometria (AO, sektor, trasa).
            if (layout.HasRotation)
            {
                double rotation = MapObjectCardSpec.NormalizeRotation(obj.RotationDeg);
                var rotationValue = MapEditorUi.ValueText(MapObjectCardSpec.FormatRotation(rotation));
                var rotationSlider = MapEditorUi.ValueSlider(0.0, 360.0, rotation, 5.0, value =>
                {
                    obj.RotationDeg = Math.Round(value);
                    rotationValue.Text = MapObjectCardSpec.FormatRotation(obj.RotationDeg);
                    MarkDirtyUnlessRebuilding();
                    RedrawVectorsOnly();
                });
                stack.Children.Add(MapEditorUi.FieldRow("Obrót (↻):", rotationSlider, rotationValue));
            }

            if (layout.HasGeometryTools)
                stack.Children.Add(MapEditorUi.FieldRow("Geometria:", BuildGeometryTools(win, obj, layout)));

            card.Child = stack;
            return card;
        }

        /// <summary>„Typ” (AO / sektor / trasa) i „✂️ Uprość” — zawsze w tym samym miejscu karty obiektu wielopunktowego.</summary>
        private static Grid BuildGeometryTools(Window win, TacticalMapObjectItem obj, MapObjectCardLayout layout)
        {
            var available = MapObjectKinds.AvailableGeometryKinds(obj.Kind, obj.Points.Count);
            var typeCombo = CreateDarkTacticalComboBox(available.Select(k => MapObjectKinds.DisplayName(k)), MapObjectKinds.DisplayName(obj.Kind), picked =>
            {
                string? target = MapObjectKinds.FromDisplayName(picked);
                if (target != null && target != obj.Kind) ChangeObjectGeometryKind(win, obj, target);
            });
            typeCombo.Height = MapEditorUi.ControlHeight + 2;
            typeCombo.MinHeight = 0; // App.xaml daje listom MinHeight 30 — tu wiersz ma mieć wysokość reszty karty
            typeCombo.FontSize = 10;
            typeCombo.Margin = new Thickness(0, 0, 6, 0);
            typeCombo.ToolTip = "Rodzaj obiektu: teren gry (AO, maska poza obszarem), sektor (wypełniony) albo trasa (linia)";
            if (available.Count < 2)
            {
                typeCombo.IsEnabled = false;
                typeCombo.Opacity = 0.6;
                typeCombo.ToolTip = $"Do zamiany na teren gry (AO) albo sektor potrzeba co najmniej {MapObjectKinds.MinimumPoints(MapObjectKinds.Ao)} punktów";
                ToolTipService.SetShowOnDisabled(typeCombo, true);
            }

            var simplify = MapEditorUi.TextButton(
                $"✂️ Uprość ({obj.Points.Count} pkt)",
                layout.CanSimplify
                    ? $"Kształt ma {MapObjectCardSpec.FormatPointCount(obj.Points.Count)}. Usuń zbędne wierzchołki (tolerancja {MapObjectCardSpec.SimplifyToleranceMeters:0} m), np. z gęstego śladu GPS"
                    : $"Upraszczanie ma sens od {MapObjectCardSpec.SimplifyMinPoints} wierzchołków",
                layout.CanSimplify,
                () => SimplifyObjectGeometry(win, obj));

            var tools = new Grid();
            tools.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tools.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(typeCombo, 0);
            Grid.SetColumn(simplify, 1);
            tools.Children.Add(typeCombo);
            tools.Children.Add(simplify);
            return tools;
        }

        private static Border CreateObjectBadge(TacticalMapObjectItem obj)
        {
            string code = obj.Kind == MapObjectKinds.Dot ? "DOT" : (MapObjectKinds.IsMultiPoint(obj.Kind) ? obj.Kind : obj.SymbolCode);
            return CreateMiniSymbolVectorBadge(code, obj.ColorHex);
        }

        private static string ObjectColorTooltip(string? hex) => $"Kolor {(hex ?? "").ToUpperInvariant()} — kliknij, aby zmienić";

        private static string FormatObjectSizeLabel(TacticalMapObjectItem obj) =>
            MapObjectCardSpec.FormatSize(obj.Kind, obj.SizeMultiplier, _dotMarkerSizePx);

        /// <summary>Domyślny opis w legendzie dla rodzaju i symbolu obiektu (bez opisu wpisanego przez użytkownika).</summary>
        private static string StandardLegendDescription(TacticalMapObjectItem obj)
        {
            string? kindText = MapObjectKinds.DefaultLegendText(obj.Kind);
            if (kindText != null) return kindText;
            var nato = NatoSymbols.FirstOrDefault(n => n.Code == obj.SymbolCode) ?? NatoSymbols[0];
            return nato.NatoMeaning;
        }

        /// <summary>
        /// Uzupełnia brakujące wartości (np. po imporcie .KML) już przy budowie listy — gdyby robiło to dopiero
        /// zaznaczenie obiektu, samo kliknięcie zmieniałoby projekt i tworzyło krok cofania.
        /// </summary>
        private static void NormalizeObjectForEditing(TacticalMapObjectItem obj)
        {
            if (obj.SizeMultiplier <= 0.1) obj.SizeMultiplier = 1.0;
            if (string.IsNullOrWhiteSpace(obj.LegendDescription)) obj.LegendDescription = StandardLegendDescription(obj);
        }

        private static void MarkDirtyUnlessRebuilding()
        {
            if (!_isRebuildingMapControls) TacticalConfigPanel.MarkDirty();
        }

        // ------------------------------------------------------------------ zaznaczenie (wspólne z mapą)

        private static void DropStaleListSelection()
        {
            if (_listSelection is TacticalMapObjectItem obj && !_mapObjects.Contains(obj)) _listSelection = null;
            else if (_listSelection is TacticalRangeSectorItem sec && !_weaponRangeSectors.Contains(sec)) _listSelection = null;
        }

        /// <summary>
        /// Obiekt zaznaczony na mapie (znacznik, wierzchołek, uchwyt ✥) staje się zaznaczeniem listy. Wołane z
        /// <see cref="UpdateSelectedMarkerInspectorUI"/>, przez które przechodzi każde zaznaczenie obiektu na mapie.
        /// </summary>
        private static void SyncListSelectionWithMap()
        {
            object? desired = _listSelection;
            if (_inspectedObject != null && _mapObjects.Contains(_inspectedObject)) desired = _inspectedObject;
            else if (_listSelection is TacticalMapObjectItem) desired = null;
            if (ReferenceEquals(desired, _listSelection)) return;
            _listSelection = desired;
            ApplyListSelectionToUi(scrollIntoView: true);
        }

        private static void ApplyListSelectionToUi(bool scrollIntoView)
        {
            foreach (var pair in _listRows)
                MapEditorUi.SetCardSelected(pair.Value.Row, ReferenceEquals(pair.Key, _listSelection));
            if (_hostWindow != null) ShowListSelectionDetails(_hostWindow, force: false);
            if (scrollIntoView && _listSelection != null && _listRows.TryGetValue(_listSelection, out var row))
            {
                _listScrolledTo = _listSelection;
                ScrollListRowIntoView(row.Row);
            }
        }

        /// <summary>Przewija tylko listę (nie cały panel), żeby zaznaczony wiersz był widoczny.</summary>
        private static void ScrollListRowIntoView(FrameworkElement row)
        {
            var scroll = _objectsListScroll;
            if (scroll == null || !row.IsLoaded || row.ActualHeight <= 0) return;
            try
            {
                double top = row.TransformToAncestor(scroll).Transform(new Point(0, 0)).Y;
                if (top < 0)
                    scroll.ScrollToVerticalOffset(scroll.VerticalOffset + top - 2);
                else if (top + row.ActualHeight > scroll.ViewportHeight)
                    scroll.ScrollToVerticalOffset(scroll.VerticalOffset + top + row.ActualHeight - scroll.ViewportHeight + 2);
            }
            catch (InvalidOperationException)
            {
                // Wiersz nie jest (już) w tej liście.
            }
        }

        /// <summary>Nowo zaznaczony element (np. właśnie postawiony) — lista przewinie się do jego wiersza, gdy ten się pojawi.</summary>
        private static void ScrollRowIntoViewWhenLoaded(object item, Border row)
        {
            if (!ReferenceEquals(item, _listSelection) || ReferenceEquals(item, _listScrolledTo)) return;
            _listScrolledTo = item;
            RoutedEventHandler? onLoaded = null;
            onLoaded = (_, _) =>
            {
                row.Loaded -= onLoaded;
                ScrollListRowIntoView(row);
            };
            row.Loaded += onLoaded;
        }

        /// <summary>„Pokaż na liście” z menu obiektu: przewija panel do wiersza.</summary>
        private static void RevealListRow(object item)
        {
            if (_listRows.TryGetValue(item, out var row)) row.Row.BringIntoView();
        }

        /// <summary>Kliknięcie wiersza (dwuklik — także wyśrodkowanie mapy), 📍, klawisz F.</summary>
        private static void SelectListItem(object? item, bool centerMap)
        {
            if (item is TacticalMapObjectItem obj) SelectObjectFromList(obj, centerMap);
            else if (item is TacticalRangeSectorItem sec) SelectRangeSectorFromList(sec, centerMap);
            else ClearListSelection();
        }

        /// <summary>Zaznacza obiekt (współrzędne w inspektorze, wiersz listy, karta szczegółów); z <paramref name="centerMap"/> także wyśrodkowuje mapę.</summary>
        private static void SelectObjectFromList(TacticalMapObjectItem obj, bool centerMap)
        {
            if (!_mapObjects.Contains(obj)) return;
            bool changed = !ReferenceEquals(_inspectedObject, obj) || !ReferenceEquals(_listSelection, obj);
            if (!changed && !centerMap) return;

            if (!ReferenceEquals(_inspectedObject, obj)) _inspectedVertexIndex = 0;
            _inspectedObject = obj;
            if (centerMap && obj.Points.Count > 0)
            {
                _centerLat = obj.Points.Average(p => p.Lat);
                _centerLon = obj.Points.Average(p => p.Lon);
                _ = RenderMapTilesAsync();
            }
            UpdateSelectedMarkerInspectorUI(); // → zaznaczenie listy i karta szczegółów
            RedrawVectorsOnly();                // także obrys sektorów ognia (żaden nie jest już zaznaczony)
        }

        private static void SelectRangeSectorFromList(TacticalRangeSectorItem sec, bool centerMap)
        {
            if (!_weaponRangeSectors.Contains(sec)) return;
            bool changed = !ReferenceEquals(_listSelection, sec);
            if (!changed && !centerMap) return;

            bool objectWasInspected = _inspectedObject != null;
            _inspectedObject = null;
            _selectedRangeSector = sec;
            _listSelection = sec;
            PopulateRangeSectorEditorFromSelected();
            UpdateSelectedMarkerInspectorUI();
            ApplyListSelectionToUi(scrollIntoView: false);
            if (centerMap) CenterMapOnRangeSector(sec);
            if (objectWasInspected) RedrawVectorsOnly();
            else RedrawRangeSectorsOnly();
        }

        /// <summary>
        /// Sektor złapany na mapie (przesuwanie, obrót, kółko myszy) — zaznacza go też na liście. Mapę przerysowuje
        /// dopiero <see cref="FlushMapRedrawAfterSectorGesture"/> po puszczeniu przycisku: pełne przerysowanie w trakcie
        /// gestu zdjęłoby z mapy element, który właśnie trzyma mysz.
        /// </summary>
        private static void SelectRangeSectorOnMap(TacticalRangeSectorItem sec)
        {
            _selectedRangeSector = sec;
            PopulateRangeSectorEditorFromSelected();
            if (ReferenceEquals(_listSelection, sec)) return;
            if (_inspectedObject != null)
            {
                _inspectedObject = null;
                _redrawVectorsAfterSectorGesture = true;
            }
            _restyleSectorsAfterSectorGesture = true;
            _listSelection = sec;
            UpdateSelectedMarkerInspectorUI();
            ApplyListSelectionToUi(scrollIntoView: true);
        }

        private static void FlushMapRedrawAfterSectorGesture()
        {
            bool fullRedraw = _redrawVectorsAfterSectorGesture, restyle = _restyleSectorsAfterSectorGesture;
            _redrawVectorsAfterSectorGesture = false;
            _restyleSectorsAfterSectorGesture = false;
            if (fullRedraw) RedrawVectorsOnly();
            else if (restyle) RedrawRangeSectorsOnly();
            UpdateSelectedMarkerInspectorUI();
        }

        private static void CenterMapOnRangeSector(TacticalRangeSectorItem sec)
        {
            if (!sec.HasGeoAnchor) SyncSectorGeoAnchorFromOffsets(sec);
            _centerLat = sec.ShooterLat;
            _centerLon = sec.ShooterLon;
            _ = RenderMapTilesAsync();
        }

        /// <summary>Klik w puste miejsce mapy, Esc: nic nie jest zaznaczone.</summary>
        private static void ClearListSelection()
        {
            bool hadObject = _inspectedObject != null;
            bool hadSector = _listSelection is TacticalRangeSectorItem;
            _inspectedObject = null;
            _listSelection = null;
            UpdateSelectedMarkerInspectorUI();
            ApplyListSelectionToUi(scrollIntoView: false);
            if (hadObject) RedrawVectorsOnly();
            else if (hadSector) RedrawRangeSectorsOnly();
        }

        // ------------------------------------------------------------------ działania na elementach listy

        private static void DeleteListItem(Window win, object item)
        {
            if (item is TacticalMapObjectItem obj) DeleteMapObject(win, obj);
            else if (item is TacticalRangeSectorItem sec) DeleteRangeSector(sec);
        }

        private static void DuplicateListItem(Window win, object item)
        {
            if (item is TacticalMapObjectItem obj) DuplicateMapObject(win, obj);
            else if (item is TacticalRangeSectorItem sec) DuplicateRangeSector(sec);
        }

        /// <summary>F2 / „Zmień nazwę”: zaznacza element i ustawia kursor w polu nazwy w panelu szczegółów.</summary>
        private static void FocusListItemName(object item)
        {
            if (item is TacticalMapObjectItem obj)
            {
                FocusObjectNameBox(obj);
            }
            else if (item is TacticalRangeSectorItem sec)
            {
                SelectRangeSectorFromList(sec, centerMap: false);
                if (_rsNameBox != null) FocusTextBoxSoon(_rsNameBox);
            }
        }

        /// <summary>Pole świeżo zbudowanej karty dostaje fokus dopiero, gdy jest już w oknie.</summary>
        private static void FocusTextBoxSoon(TextBox box)
        {
            void Apply()
            {
                box.BringIntoView();
                box.Focus();
                box.SelectAll();
            }

            if (box.IsLoaded) Apply();
            else box.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(Apply));
        }

        private static void DeleteMapObject(Window win, TacticalMapObjectItem obj)
        {
            if (!_mapObjects.Remove(obj)) return;
            if (ReferenceEquals(_activeDraftPoly, obj)) _activeDraftPoly = null;
            if (ReferenceEquals(_inspectedObject, obj)) _inspectedObject = null;
            if (ReferenceEquals(_listSelection, obj)) _listSelection = null;
            TacticalConfigPanel.MarkDirty();
            ShowMapStatus($"Usunięto obiekt „{obj.Name}” z mapy · Ctrl+Z — cofnij.");
            UpdateSelectedMarkerInspectorUI();
            QueueObjectsListRefresh(win);
            RedrawVectorsOnly();
            UpdateOfflineTileEstimateUI();
        }

        private static void DeleteRangeSector(TacticalRangeSectorItem sec)
        {
            if (!_weaponRangeSectors.Remove(sec)) return;
            if (ReferenceEquals(_selectedRangeSector, sec)) _selectedRangeSector = _weaponRangeSectors.FirstOrDefault();
            if (ReferenceEquals(_listSelection, sec)) _listSelection = null;
            ShowMapStatus($"Usunięto sektor ognia „{sec.Name}” · Ctrl+Z — cofnij.");
            PopulateRangeSectorEditorFromSelected();
            UpdateSelectedMarkerInspectorUI();
            QueueObjectsListRefresh();
            // Pełne przerysowanie zdejmuje z mapy elementy usuniętego sektora (także gdy był ostatni).
            RedrawVectorsOnly();
            RefreshRangeSectorsNow();
        }

        /// <summary>Kopia sektora ognia przesunięta o kilkanaście pikseli w prawo i w dół, wstawiona zaraz za oryginałem.</summary>
        private static void DuplicateRangeSector(TacticalRangeSectorItem sec)
        {
            int index = _weaponRangeSectors.IndexOf(sec);
            if (index < 0) return;
            var copy = new RangeSectorSnapshot(sec).ToItem();
            copy.Id = Guid.NewGuid().ToString("N");
            copy.Name = string.IsNullOrWhiteSpace(sec.Name) ? "Kopia" : sec.Name + " (kopia)";
            double mpp = GetExactMetersPerPixel();
            if (mpp <= 0.0001 || double.IsNaN(mpp) || double.IsInfinity(mpp)) mpp = 1.0;
            copy.OffsetEastMeters = sec.OffsetEastMeters + 18.0 * mpp;
            copy.OffsetNorthMeters = sec.OffsetNorthMeters - 18.0 * mpp;
            SyncSectorGeoAnchorFromOffsets(copy);
            _weaponRangeSectors.Insert(index + 1, copy);

            bool objectWasInspected = _inspectedObject != null;
            _inspectedObject = null;
            _selectedRangeSector = copy;
            _listSelection = copy;
            PopulateRangeSectorEditorFromSelected();
            UpdateSelectedMarkerInspectorUI();
            ShowMapStatus($"Zduplikowano sektor ognia „{sec.Name}”.");
            QueueObjectsListRefresh();
            if (objectWasInspected) RedrawVectorsOnly();
            RefreshRangeSectorsNow(forceFullRebuild: true);
        }

        private static void SetRangeSectorVisible(TacticalRangeSectorItem sec, bool visible)
        {
            if (_isUpdatingListRows || sec.IsVisible == visible) return;
            sec.IsVisible = visible;
            UpdateListRow(sec);
            UpdateRangeSectorDetailsHeader();
            RefreshRangeSectorsNow(forceFullRebuild: true);
        }

        private static void PickRangeSectorColor(TacticalRangeSectorItem sec)
        {
            string? picked = TacticalConfigPanel.ShowRgbColorPickerDialog(_hostWindow, $"Kolor — {sec.Name}", sec.ColorHex);
            if (string.IsNullOrEmpty(picked)) return;
            sec.ColorHex = picked;
            UpdateListRow(sec);
            UpdateRangeSectorDetailsHeader();
            RefreshRangeSectorsNow();
        }

        private static void ChangeObjectGeometryKind(Window win, TacticalMapObjectItem obj, string targetKind)
        {
            if (!MapObjectKinds.CanConvert(obj.Kind, targetKind, obj.Points.Count)) return;
            string previousDefault = StandardLegendDescription(obj);
            obj.Kind = targetKind;
            obj.SymbolCode = targetKind;
            obj.LegendDescription = MapObjectKinds.LegendTextAfterKindChange(obj.LegendDescription, previousDefault, StandardLegendDescription(obj));
            TacticalConfigPanel.MarkDirty();
            ShowMapStatus($"Obiekt „{obj.Name}” to teraz: {MapObjectKinds.DisplayName(targetKind)}.");
            QueueObjectsListRefresh(win);
            RedrawVectorsOnly();
            UpdateOfflineTileEstimateUI();
        }

        private static void SimplifyObjectGeometry(Window win, TacticalMapObjectItem obj)
        {
            int before = obj.Points.Count;
            var simplified = TacticalVectorFileIoEngine.SimplifyTrackPoints(obj.Points, MapObjectCardSpec.SimplifyToleranceMeters);
            if (simplified.Count < MapObjectKinds.MinimumPoints(obj.Kind) || simplified.Count >= before)
            {
                ShowMapStatus($"Kształtu „{obj.Name}” nie da się bardziej uprościć ({MapObjectCardSpec.FormatPointCount(before)}).");
                return;
            }

            obj.Points = simplified;
            if (ReferenceEquals(_inspectedObject, obj)) _inspectedVertexIndex = Math.Min(_inspectedVertexIndex, simplified.Count - 1);
            TacticalConfigPanel.MarkDirty();
            ShowMapStatus($"✂️ Uproszczono kształt „{obj.Name}”: {before} → {simplified.Count} wierzchołków · Ctrl+Z — cofnij.");
            UpdateSelectedMarkerInspectorUI();
            QueueObjectsListRefresh(win);
            RedrawVectorsOnly();
        }

        /// <summary>„🧹 Wyczyść listę” — wszystko z listy: obiekty i sektory ognia (z potwierdzeniem; Ctrl+Z przywraca).</summary>
        private static void ConfirmAndClearAllObjects(Window win)
        {
            int objects = _mapObjects.Count, sectors = _weaponRangeSectors.Count;
            if (objects + sectors == 0) return;
            var answer = MessageBox.Show(win,
                $"Usunąć z mapy wszystko z listy ({MapListText.CountList(objects, sectors)})?\n\nMożna to cofnąć: Ctrl+Z albo przycisk ↶ nad mapą.",
                "Wyczyść listę", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;

            _mapObjects.Clear();
            _weaponRangeSectors.Clear();
            _activeDraftPoly = null;
            _inspectedObject = null;
            _selectedRangeSector = null;
            _listSelection = null;
            TacticalConfigPanel.MarkDirty();
            ShowMapStatus("Wyczyszczono listę obiektów na mapie · Ctrl+Z — cofnij.");
            UpdateSelectedMarkerInspectorUI();
            if (_lastLegendContainerGrid != null) UpdateRangeSectorsInLegendBox(_lastLegendContainerGrid);
            RefreshObjectsListUI(win);
            RedrawVectorsOnly();
            UpdateOfflineTileEstimateUI();
        }
    }
}
