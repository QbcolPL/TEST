using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FieldTakHub.Builder.Services.MapEditor;

namespace FieldTakHub.Builder.Services
{
    /// <summary>
    /// Lista obiektów generatora map. Każdy obiekt — znacznik NATO, kropka, teren gry (AO), sektor, trasa — ma kartę
    /// o tym samym układzie (<see cref="MapObjectCardSpec"/>): [Aa etykieta] [● kolor] [📍] [symbol] [nazwa] [− usuń],
    /// „W legendzie”, „Wielkość”, a dalej „Obrót” albo „Geometria”. Karta jest budowana od razu w docelowym układzie.
    /// </summary>
    public static partial class TacticalMapGeneratorView
    {
        private static bool _objectsListRefreshQueued;

        private static void RefreshObjectsListUI(Window win)
        {
            if (_objectsListStack == null) return;
            _objectsListStack.Children.Clear();
            _objectNameBoxes.Clear();
            if (_mapObjects.Count == 0)
            {
                _objectsListStack.Children.Add(new TextBlock
                {
                    Text = "Brak naniesionych obiektów — kliknij na mapie lub wczytaj plik .KMZ / .GPX",
                    Foreground = TacticalConfigPanel.BrushMutedText,
                    FontSize = 9.5,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 8, 0, 8)
                });
                return;
            }

            foreach (var obj in _mapObjects.ToList())
                _objectsListStack.Children.Add(BuildObjectCard(win, obj));
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

        private static Border BuildObjectCard(Window win, TacticalMapObjectItem obj)
        {
            if (obj.SizeMultiplier <= 0.1) obj.SizeMultiplier = 1.0;
            if (string.IsNullOrWhiteSpace(obj.LegendDescription)) obj.LegendDescription = StandardLegendDescription(obj);
            var layout = MapObjectCardSpec.For(obj.Kind, obj.Points.Count);

            var card = MapEditorUi.Card(obj, ReferenceEquals(_inspectedObject, obj));
            // Każde kliknięcie w karcie (także w pole nazwy czy suwak) zaznacza obiekt — bez przebudowy listy.
            card.PreviewMouseLeftButtonDown += (_, _) => SelectObjectFromList(obj, centerMap: false);
            var stack = new StackPanel();

            // Wiersz 1: [Aa] [● kolor] [📍] [symbol] [nazwa] [− usuń] — ten sam dla każdego rodzaju obiektu.
            var labelToggle = MapEditorUi.Toggle("Aa", obj.ShowLabel, "Etykieta z nazwą tego obiektu na mapie (wł./wył.). Działa, gdy zaznaczone jest „Pokazuj etykiety nazw na mapie”.", on =>
            {
                obj.ShowLabel = on;
                MarkDirtyUnlessRebuilding();
                RedrawVectorsOnly();
            });

            var badgeHost = new Border { Child = CreateObjectBadge(obj), ToolTip = MapObjectKinds.DisplayName(obj.Kind) };

            var swatch = MapEditorUi.ColorSwatch(obj.ColorHex, ObjectColorTooltip(obj.ColorHex), button =>
            {
                string picked = TacticalConfigPanel.ShowRgbColorPickerDialog(win, $"Kolor — {obj.Name}", obj.ColorHex);
                if (string.IsNullOrEmpty(picked)) return;
                obj.ColorHex = picked;
                MapEditorUi.SetSwatchColor(button, picked, ObjectColorTooltip(picked));
                badgeHost.Child = CreateObjectBadge(obj);
                TacticalConfigPanel.MarkDirty();
                RedrawVectorsOnly();
            });

            var locate = MapEditorUi.IconButton("📍", "Pokaż na mapie: wyśrodkuj widok i odczytaj współrzędne (MGRS, sektor)", () => SelectObjectFromList(obj, centerMap: true));

            var nameBox = MapEditorUi.TextField(obj.Name ?? "", "Nazwa obiektu — na mapie i w legendzie", emphasized: true);
            _objectNameBoxes[obj] = nameBox;
            nameBox.TextChanged += (_, _) =>
            {
                obj.Name = nameBox.Text;
                MarkDirtyUnlessRebuilding();
                UpdateSelectedMarkerInspectorUI();
                RedrawVectorsOnly();
            };
            if (ReferenceEquals(_newlyAddedObjectToFocus, obj))
            {
                // Nowy obiekt: lista przewija się do jego karty, ale fokus zostaje na mapie, żeby działały skróty.
                // Nazwę zmienia się klawiszem F2 albo z menu obiektu (prawy klik).
                _newlyAddedObjectToFocus = null;
                card.Loaded += (_, _) => _objectsListScroll?.ScrollToEnd();
            }

            var delete = MapEditorUi.DeleteButton("Usuń ten obiekt z mapy", () => DeleteMapObject(win, obj));
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

        private static void MarkDirtyUnlessRebuilding()
        {
            if (!_isRebuildingMapControls) TacticalConfigPanel.MarkDirty();
        }

        /// <summary>Zaznacza obiekt (współrzędne w inspektorze, ramka karty); z <paramref name="centerMap"/> także wyśrodkowuje mapę.</summary>
        private static void SelectObjectFromList(TacticalMapObjectItem obj, bool centerMap)
        {
            if (!_mapObjects.Contains(obj)) return;
            bool changed = !ReferenceEquals(_inspectedObject, obj);
            if (!changed && !centerMap) return;

            _inspectedObject = obj;
            if (changed) _inspectedVertexIndex = 0;
            if (centerMap && obj.Points.Count > 0)
            {
                _centerLat = obj.Points.Average(p => p.Lat);
                _centerLon = obj.Points.Average(p => p.Lon);
                _ = RenderMapTilesAsync();
            }
            UpdateSelectedMarkerInspectorUI();
            UpdateObjectCardSelection();
            RedrawVectorsOnly();
        }

        private static void UpdateObjectCardSelection()
        {
            if (_objectsListStack == null) return;
            foreach (var card in _objectsListStack.Children.OfType<Border>())
                MapEditorUi.SetCardSelected(card, card.Tag != null && ReferenceEquals(card.Tag, _inspectedObject));
        }

        private static void DeleteMapObject(Window win, TacticalMapObjectItem obj)
        {
            if (!_mapObjects.Remove(obj)) return;
            if (ReferenceEquals(_activeDraftPoly, obj)) _activeDraftPoly = null;
            if (ReferenceEquals(_inspectedObject, obj)) _inspectedObject = null;
            TacticalConfigPanel.MarkDirty();
            if (_statusText != null) _statusText.Text = $"Usunięto obiekt „{obj.Name}” z mapy.";
            UpdateSelectedMarkerInspectorUI();
            QueueObjectsListRefresh(win);
            RedrawVectorsOnly();
            UpdateOfflineTileEstimateUI();
        }

        private static void ChangeObjectGeometryKind(Window win, TacticalMapObjectItem obj, string targetKind)
        {
            if (!MapObjectKinds.CanConvert(obj.Kind, targetKind, obj.Points.Count)) return;
            string previousDefault = StandardLegendDescription(obj);
            obj.Kind = targetKind;
            obj.SymbolCode = targetKind;
            obj.LegendDescription = MapObjectKinds.LegendTextAfterKindChange(obj.LegendDescription, previousDefault, StandardLegendDescription(obj));
            TacticalConfigPanel.MarkDirty();
            if (_statusText != null) _statusText.Text = $"Obiekt „{obj.Name}” to teraz: {MapObjectKinds.DisplayName(targetKind)}.";
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
                if (_statusText != null) _statusText.Text = $"Kształtu „{obj.Name}” nie da się bardziej uprościć ({MapObjectCardSpec.FormatPointCount(before)}).";
                return;
            }

            obj.Points = simplified;
            if (ReferenceEquals(_inspectedObject, obj)) _inspectedVertexIndex = Math.Min(_inspectedVertexIndex, simplified.Count - 1);
            TacticalConfigPanel.MarkDirty();
            if (_statusText != null) _statusText.Text = $"✂️ Uproszczono kształt „{obj.Name}”: {before} → {simplified.Count} wierzchołków.";
            UpdateSelectedMarkerInspectorUI();
            QueueObjectsListRefresh(win);
            RedrawVectorsOnly();
        }

        private static void ConfirmAndClearAllObjects(Window win)
        {
            if (_mapObjects.Count == 0) return;
            var answer = MessageBox.Show(win,
                $"Usunąć z mapy wszystkie obiekty ({_mapObjects.Count})?\n\nTej operacji nie można cofnąć.",
                "Wyczyść listę obiektów", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;

            _mapObjects.Clear();
            _activeDraftPoly = null;
            _inspectedObject = null;
            TacticalConfigPanel.MarkDirty();
            if (_statusText != null) _statusText.Text = "Wyczyszczono listę obiektów na mapie.";
            UpdateSelectedMarkerInspectorUI();
            RefreshObjectsListUI(win);
            RedrawVectorsOnly();
            UpdateOfflineTileEstimateUI();
        }
    }
}
