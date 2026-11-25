using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FieldTakHub.Builder.Services.MapEditor;

namespace FieldTakHub.Builder.Services
{
    /// <summary>
    /// Cofanie i ponawianie (Ctrl+Z / Ctrl+Y, ↶ ↷ nad mapą) wszystkich zmian obiektów i sektorów ognia w generatorze map.
    /// Zamiast wpinać się w każde miejsce, które zmienia obiekty, historia porównuje stan po każdej serii zmian:
    /// edytor zgłasza zmianę przez <see cref="TacticalConfigPanel.MarkDirty"/>, a każde puszczenie przycisku myszy
    /// czy klawisza w generatorze też zleca sprawdzenie. Po chwili ciszy (koniec przeciągania, pisania, przesuwania
    /// suwaka) powstaje jeden krok cofania — tylko jeśli treść naprawdę się zmieniła.
    /// </summary>
    public static partial class TacticalMapGeneratorView
    {
        private static readonly EditHistory<MapEditState> _editHistory = MapEditSnapshots.CreateHistory();
        private static DispatcherTimer? _undoCheckpointTimer;
        private static bool _isApplyingUndo;
        private static bool _undoTrackingHooked;
        private static Button? _undoButton;
        private static Button? _redoButton;

        /// <summary>Raz, przy budowie generatora: śledzenie zmian i historia od bieżącego stanu.</summary>
        private static void InitUndoTracking(UIElement root)
        {
            if (!_undoTrackingHooked)
            {
                TacticalConfigPanel.ContentChanged += OnEditorContentChanged;
                // Każda zmiana zaczyna się od myszy albo klawiatury — także ta, która nie woła MarkDirty.
                root.AddHandler(UIElement.PreviewMouseUpEvent, new MouseButtonEventHandler((_, _) => ScheduleUndoCheckpoint()), true);
                root.AddHandler(UIElement.PreviewKeyUpEvent, new KeyEventHandler((_, _) => ScheduleUndoCheckpoint()), true);
                root.AddHandler(UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler((_, _) => ScheduleUndoCheckpoint()), true);
                _undoTrackingHooked = true;
            }
            ResetUndoHistory();
        }

        /// <summary>Nowy dokument (wczytany projekt, przywrócona sesja, nowy projekt): historia od zera.</summary>
        private static void ResetUndoHistory()
        {
            _undoCheckpointTimer?.Stop();
            _editHistory.Reset(MapEditSnapshots.Capture(_mapObjects, _weaponRangeSectors));
            RefreshUndoButtons();
        }

        private static void OnEditorContentChanged()
        {
            var overlay = _fullScreenOverlay;
            if (_isApplyingUndo || overlay == null) return;
            if (!overlay.Dispatcher.CheckAccess())
            {
                overlay.Dispatcher.BeginInvoke(new Action(OnEditorContentChanged));
                return;
            }
            ScheduleUndoCheckpoint();
        }

        private static void ScheduleUndoCheckpoint()
        {
            if (_isApplyingUndo) return;
            if (_undoCheckpointTimer == null)
            {
                _undoCheckpointTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(450) };
                _undoCheckpointTimer.Tick += (_, _) => CommitUndoCheckpoint();
            }
            _undoCheckpointTimer.Stop();
            _undoCheckpointTimer.Start();
        }

        /// <summary>Przeciąganie, rysowanie kształtu albo wciśnięty przycisk myszy — krok cofania powstanie po ich końcu.</summary>
        private static bool IsMapGestureInProgress =>
            _isPanningMap || _draggedObject != null || _isInteractingWithSectorOnMap || _activeDraftPoly != null
            || Mouse.LeftButton == MouseButtonState.Pressed || Mouse.RightButton == MouseButtonState.Pressed;

        /// <summary>Zapisuje krok cofania po serii zmian. W trakcie gestu zegar tyka dalej i spróbuje ponownie.</summary>
        private static void CommitUndoCheckpoint()
        {
            if (_isApplyingUndo || IsMapGestureInProgress) return;
            _undoCheckpointTimer?.Stop();
            if (_editHistory.Commit(MapEditSnapshots.Capture(_mapObjects, _weaponRangeSectors))) RefreshUndoButtons();
        }

        private static void UndoMapEdit()
        {
            if (_activeDraftPoly != null)
            {
                UndoDraftPoint();
                return;
            }
            StepEditHistory(undo: true);
        }

        private static void RedoMapEdit()
        {
            if (_activeDraftPoly != null)
            {
                ShowMapStatus("Ponawianie działa po zakończeniu rysowanego kształtu — Enter albo prawy klik.");
                return;
            }
            StepEditHistory(undo: false);
        }

        /// <summary>Ctrl+Z w trakcie rysowania kształtu: usuwa ostatni punkt, a przy pierwszym — cały rysowany kształt.</summary>
        private static void UndoDraftPoint()
        {
            var draft = _activeDraftPoly;
            if (draft == null) return;
            if (draft.Points.Count > 1)
            {
                draft.Points.RemoveAt(draft.Points.Count - 1);
                if (ReferenceEquals(_inspectedObject, draft)) _inspectedVertexIndex = draft.Points.Count - 1;
                ShowMapStatus($"↶ Usunięto ostatni punkt — „{draft.Name}”: {MapObjectCardSpec.FormatPointCount(draft.Points.Count)} · Enter — zakończ");
            }
            else
            {
                _mapObjects.Remove(draft);
                _activeDraftPoly = null;
                if (ReferenceEquals(_inspectedObject, draft)) _inspectedObject = null;
                ShowMapStatus($"↶ Anulowano rysowanie „{draft.Name}”.");
            }
            TacticalConfigPanel.MarkDirty();
            UpdateSelectedMarkerInspectorUI();
            if (_hostWindow != null) RefreshObjectsListUI(_hostWindow);
            RedrawVectorsOnly();
            UpdateOfflineTileEstimateUI();
        }

        private static void StepEditHistory(bool undo)
        {
            if (_draggedObject != null || _isInteractingWithSectorOnMap || Mouse.LeftButton == MouseButtonState.Pressed) return;
            CloseMapMenus();
            _undoCheckpointTimer?.Stop();
            // Zmiana, której zegar jeszcze nie zapisał (np. sprzed pół sekundy), też jest krokiem do cofnięcia.
            _editHistory.Commit(MapEditSnapshots.Capture(_mapObjects, _weaponRangeSectors));

            var state = undo ? _editHistory.Undo() : _editHistory.Redo();
            if (state == null)
            {
                ShowMapStatus(undo ? "Nie ma czego cofnąć." : "Nie ma czego ponowić.");
                RefreshUndoButtons();
                return;
            }
            ApplyEditState(state);
            ShowMapStatus(undo
                ? $"↶ Cofnięto zmianę · w historii zostało kroków: {_editHistory.UndoCount} · Ctrl+Y — ponów"
                : $"↷ Ponowiono zmianę · do ponowienia zostało kroków: {_editHistory.RedoCount} · Ctrl+Z — cofnij");
        }

        /// <summary>Podmienia obiekty i sektory na kopie z historii; zaznaczenie zostaje na elemencie o tym samym identyfikatorze.</summary>
        private static void ApplyEditState(MapEditState state)
        {
            string? inspectedId = _inspectedObject?.Id;
            string? sectorId = _selectedRangeSector?.Id;
            bool listIsSector = _listSelection is TacticalRangeSectorItem;
            string? listId = (_listSelection as TacticalMapObjectItem)?.Id ?? (_listSelection as TacticalRangeSectorItem)?.Id;

            _isApplyingUndo = true;
            try
            {
                _mapObjects.Clear();
                _mapObjects.AddRange(state.Objects.Select(o => o.ToItem()));
                _weaponRangeSectors.Clear();
                _weaponRangeSectors.AddRange(state.Sectors.Select(s => s.ToItem()));

                _activeDraftPoly = null;
                _draggedObject = null;
                _inspectedObject = inspectedId == null ? null : _mapObjects.FirstOrDefault(o => o.Id == inspectedId);
                if (_inspectedObject != null)
                    _inspectedVertexIndex = Math.Clamp(_inspectedVertexIndex, 0, Math.Max(0, _inspectedObject.Points.Count - 1));
                _selectedRangeSector = _weaponRangeSectors.FirstOrDefault(s => s.Id == sectorId) ?? _weaponRangeSectors.FirstOrDefault();
                object? listItem = null;
                if (listId != null)
                    listItem = listIsSector
                        ? (object?)_weaponRangeSectors.FirstOrDefault(s => s.Id == listId)
                        : _mapObjects.FirstOrDefault(o => o.Id == listId);
                _listSelection = listItem;

                TacticalConfigPanel.MarkDirty();
                PopulateRangeSectorEditorFromSelected();
                UpdateSelectedMarkerInspectorUI();
                if (_hostWindow != null) RefreshObjectsListUI(_hostWindow);
                RedrawVectorsOnly();
                RefreshRangeSectorsNow();
                UpdateOfflineTileEstimateUI();
            }
            finally
            {
                _isApplyingUndo = false;
            }
            // Widok mógł uzupełnić przywrócony stan (np. domyślny opis w legendzie) — to nie jest nowy krok.
            _editHistory.Rebase(MapEditSnapshots.Capture(_mapObjects, _weaponRangeSectors));
            RefreshUndoButtons();
        }

        private static void RefreshUndoButtons()
        {
            static void Apply(Button? button, bool enabled, string tooltip)
            {
                if (button == null) return;
                button.IsEnabled = enabled;
                button.Opacity = enabled ? 1.0 : 0.35;
                button.ToolTip = tooltip;
            }

            Apply(_undoButton, _editHistory.CanUndo,
                _editHistory.CanUndo ? $"Cofnij (Ctrl+Z) — kroków w historii: {_editHistory.UndoCount}" : "Cofnij (Ctrl+Z) — nie ma czego cofnąć");
            Apply(_redoButton, _editHistory.CanRedo,
                _editHistory.CanRedo ? $"Ponów (Ctrl+Y) — kroków do ponowienia: {_editHistory.RedoCount}" : "Ponów (Ctrl+Y) — nie ma czego ponowić");
        }
    }
}
