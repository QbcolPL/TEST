using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using FieldTakHub.Builder.Services.MapEditor;

namespace FieldTakHub.Builder.Services
{
    /// <summary>
    /// Panel boczny generatora map w zakładkach — Mapa · Obiekty · Siatka · Legenda · Eksport (Ctrl+1…Ctrl+5) — zamiast
    /// jednej długiej kolumny pięciu sekcji. Zakładki stoją nad przewijaną zawartością, a pod nią jest stały przycisk
    /// „📤 Eksport…” (Ctrl+E): wszystkie formaty wyjściowe w jednym menu (<see cref="ExportCatalog"/>).
    /// </summary>
    public static partial class TacticalMapGeneratorView
    {
        private const string LeftTabMap = LeftPanelTabs.MapTab;
        private const string LeftTabObjects = LeftPanelTabs.ObjectsTab;
        private const string LeftTabGrid = LeftPanelTabs.GridTab;
        private const string LeftTabLegend = LeftPanelTabs.LegendTab;
        private const string LeftTabExport = LeftPanelTabs.ExportTab;

        private static string _activeLeftTab = LeftPanelTabs.DefaultTab;
        private static readonly Dictionary<string, Button> _leftTabButtons = new Dictionary<string, Button>();
        private static readonly Dictionary<string, StackPanel> _leftTabPages = new Dictionary<string, StackPanel>();
        private static readonly Dictionary<string, double> _leftTabScrollOffsets = new Dictionary<string, double>();
        private static Button? _exportMenuButton;
        private static Popup? _exportMenuPopup;

        /// <summary>Kolumna panelu: zakładki (stale u góry), przewijana zawartość aktywnej zakładki i przycisk „📤 Eksport…”.</summary>
        private static Grid BuildLeftPanelShell(Window win)
        {
            var column = new Grid { Background = TacticalConfigPanel.BrushCardBg };
            column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            column.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var strip = new UniformGrid { Columns = LeftPanelTabs.All.Count };
            _leftTabButtons.Clear();
            foreach (var tab in LeftPanelTabs.All)
            {
                string id = tab.Id;
                var button = MapEditorUi.TabButton(tab.Glyph, tab.Title, tab.Tooltip, () => SelectLeftTab(id));
                _leftTabButtons[id] = button;
                strip.Children.Add(button);
            }
            var stripHost = new Border
            {
                Background = TacticalConfigPanel.BrushInputBg,
                BorderBrush = TacticalConfigPanel.BrushTileBorder,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(8, 6, 8, 6),
                Child = strip
            };
            Grid.SetRow(stripHost, 0);
            column.Children.Add(stripHost);

            Grid.SetRow(_leftControlScrollViewer, 1);
            column.Children.Add(_leftControlScrollViewer);

            _exportMenuButton = MakeActionButton("📤 Eksport…", true, () => ShowExportMenu(win));
            _exportMenuButton.Height = 34;
            _exportMenuButton.FontSize = 11.5;
            _exportMenuButton.Margin = new Thickness(0);
            _exportMenuButton.ToolTip = "Wszystkie formaty w jednym miejscu: KMZ, GPX, PDF, wydruk z ramką, paczka .ftak, mapa offline (Ctrl+E)";
            var footer = new Border
            {
                Background = TacticalConfigPanel.BrushInputBg,
                BorderBrush = TacticalConfigPanel.BrushTileBorder,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(10, 8, 10, 8),
                Child = _exportMenuButton
            };
            Grid.SetRow(footer, 2);
            column.Children.Add(footer);

            RefreshLeftTabButtons();
            return column;
        }

        // ------------------------------------------------------------------ strony zakładek

        /// <summary>Nowe, puste strony zakładek — na początku każdej budowy panelu (pozycja przewinięcia zostaje zapamiętana).</summary>
        private static void BeginLeftTabPages()
        {
            RememberLeftTabScroll();
            _leftTabPages.Clear();
            foreach (var tab in LeftPanelTabs.All)
                _leftTabPages[tab.Id] = new StackPanel { Margin = new Thickness(6) };
        }

        private static void AddToLeftTab(string tabId, UIElement card)
        {
            if (_leftTabPages.TryGetValue(tabId, out var page)) page.Children.Add(card);
        }

        /// <summary>Koniec budowy panelu: strony w jednej komórce, widoczna tylko aktywna zakładka.</summary>
        private static void FinishLeftTabPages()
        {
            if (_leftControlScrollViewer == null) return;
            var host = new Grid();
            foreach (var page in _leftTabPages.Values) host.Children.Add(page);
            _leftControlScrollViewer.Content = host;
            ApplyLeftTab();
        }

        private static void SelectLeftTab(string tabId)
        {
            if (!LeftPanelTabs.IsKnown(tabId)) return;
            if (tabId != _activeLeftTab)
            {
                RememberLeftTabScroll();
                // Ukrywana strona nie może zatrzymać fokusu — inaczej skróty klawiszowe nie docierałyby do generatora.
                if (_leftTabPages.TryGetValue(_activeLeftTab, out var oldPage) && oldPage.IsKeyboardFocusWithin) FocusMapCanvas();
                _activeLeftTab = tabId;
            }
            ApplyLeftTab();
        }

        /// <summary>Zakładka zapisana w projekcie (nieznana lub pusta — „Mapa”). Panel przełącza się przy najbliższej budowie.</summary>
        private static void RestoreLeftTab(string? saved) => _activeLeftTab = LeftPanelTabs.Normalize(saved);

        /// <summary>Zaznaczenie na mapie albo nowy element: szczegóły są w zakładce „Obiekty”.</summary>
        private static void ShowObjectsTabForSelection()
        {
            if (_activeLeftTab != LeftPanelTabs.ObjectsTab) SelectLeftTab(LeftPanelTabs.ObjectsTab);
        }

        private static void RememberLeftTabScroll()
        {
            if (_leftControlScrollViewer != null && _leftTabPages.ContainsKey(_activeLeftTab))
                _leftTabScrollOffsets[_activeLeftTab] = _leftControlScrollViewer.VerticalOffset;
        }

        private static void ApplyLeftTab()
        {
            foreach (var pair in _leftTabPages)
                pair.Value.Visibility = pair.Key == _activeLeftTab ? Visibility.Visible : Visibility.Collapsed;
            RefreshLeftTabButtons();

            var scroll = _leftControlScrollViewer;
            if (scroll == null) return;
            double offset = _leftTabScrollOffsets.TryGetValue(_activeLeftTab, out double saved) ? saved : 0.0;
            scroll.ScrollToVerticalOffset(offset);
            // Wysokość nowej strony jest znana dopiero po układzie — wtedy jeszcze raz (bez tego długa strona wracałaby na górę).
            scroll.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                scroll.ScrollToVerticalOffset(offset);
                // Lista mogła zostać przebudowana, gdy zakładka była ukryta (np. cofnięcie zmiany) — zaznaczony wiersz ma być widać.
                if (_activeLeftTab == LeftPanelTabs.ObjectsTab && _listSelection != null && _listRows.TryGetValue(_listSelection, out var row))
                    ScrollListRowIntoView(row.Row);
            }));
        }

        private static void RefreshLeftTabButtons()
        {
            foreach (var pair in _leftTabButtons)
                MapEditorUi.SetTabActive(pair.Value, pair.Key == _activeLeftTab);
        }

        // ------------------------------------------------------------------ eksport

        /// <summary>„📤 Eksport…” (Ctrl+E): menu nad przyciskiem z każdym formatem wyjściowym; drugie kliknięcie je zamyka.</summary>
        private static void ShowExportMenu(Window win)
        {
            if (_exportMenuPopup != null && _exportMenuPopup.IsOpen)
            {
                _exportMenuPopup.IsOpen = false;
                return;
            }
            CloseMapMenus();

            Popup? popup = null;
            void Close()
            {
                if (popup != null) popup.IsOpen = false;
            }

            var items = new StackPanel();
            items.Children.Add(new TextBlock
            {
                Text = "Eksport — co chcesz otrzymać?",
                Foreground = TacticalConfigPanel.BrushAccent,
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(4, 2, 4, 6)
            });
            foreach (var group in ExportCatalog.Grouped(exportTabOnly: false))
            {
                items.Children.Add(MapEditorUi.GroupCaption(group.Key));
                foreach (var option in group)
                {
                    string id = option.Id;
                    items.Children.Add(MapEditorUi.ActionRow(option.Glyph, option.Title, option.Note, () =>
                    {
                        Close();
                        RunExportOption(win, id);
                    }));
                }
            }
            items.Children.Add(MapEditorUi.MenuSeparator());
            items.Children.Add(MapEditorUi.MenuItem("⚙", "Ustawienia eksportu — zakładka Eksport", "Ctrl+5", () =>
            {
                Close();
                SelectLeftTab(LeftPanelTabs.ExportTab);
            }));

            var panel = MapEditorUi.FloatingPanel(new ScrollViewer
            {
                Content = items,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 560
            }, 390);
            popup = new Popup
            {
                Child = panel,
                PlacementTarget = _exportMenuButton,
                Placement = PlacementMode.Top,
                VerticalOffset = -4,
                StaysOpen = false,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade
            };
            panel.PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.Escape) return;
                Close();
                e.Handled = true;
            };
            popup.Closed += (_, _) =>
            {
                if (ReferenceEquals(_exportMenuPopup, popup)) _exportMenuPopup = null;
                FocusMapCanvas();
            };
            _exportMenuPopup = popup;
            popup.IsOpen = true;
            items.Children.OfType<Button>().FirstOrDefault()?.Focus();
        }

        /// <summary>Uruchamia format z <see cref="ExportCatalog"/>. Budowa mapy offline pokazuje zakładkę Eksport — tam jest postęp.</summary>
        private static void RunExportOption(Window win, string id)
        {
            switch (id)
            {
                case ExportCatalog.Kmz:
                    RunExportSafely(ExportStandaloneFilesAsync(win, "kmz"));
                    break;
                case ExportCatalog.Gpx:
                    RunExportSafely(ExportStandaloneFilesAsync(win, "gpx"));
                    break;
                case ExportCatalog.Pdf:
                    RunExportSafely(ExportTacticalMapPdfAsync(win));
                    break;
                case ExportCatalog.FramedPrint:
                    ShowFramedPrintDialog(win);
                    break;
                case ExportCatalog.FireSectorsKmz:
                    ExportStandaloneRangeSectorsKmzDialog();
                    break;
                case ExportCatalog.Project:
                    SaveMapProjectJson(win);
                    break;
                case ExportCatalog.FtakSheet:
                    RunExportSafely(ExportMapToFtakSourceAsync(win));
                    break;
                case ExportCatalog.FtakOffline:
                    // Bez okna zapisu pobieranie ruszyłoby od razu — z menu najpierw krótkie potwierdzenie.
                    SelectLeftTab(LeftPanelTabs.ExportTab);
                    if (ConfirmOfflineBuildFromMenu(win)) RunExportSafely(BuildMultiZoomOfflineDatabaseAsync(win, true));
                    break;
                case ExportCatalog.OfflineFile:
                    SelectLeftTab(LeftPanelTabs.ExportTab);
                    RunExportSafely(BuildMultiZoomOfflineDatabaseAsync(win, false));
                    break;
            }
        }

        private static bool ConfirmOfflineBuildFromMenu(Window win)
        {
            string estimate = _offlineEstimateLabel?.Text ?? "";
            var answer = MessageBox.Show(win,
                $"Zbudować mapę offline (przybliżenia {_offlineMinZoom}–{_offlineMaxZoom}, {_offlineDbFormat}, obszar: {_offlineBoundsMode}) i dodać ją do paczki .ftak?\n\n{estimate}\n\nPostęp i przycisk „⏹ Przerwij” są w zakładce Eksport.",
                "Mapa offline do paczki .ftak", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);
            return answer == MessageBoxResult.Yes;
        }

        /// <summary>Czeka na eksport w tle; błąd trafia na pasek stanu zamiast zamykać program.</summary>
        private static async void RunExportSafely(Task export)
        {
            try
            {
                await export;
            }
            catch (Exception ex)
            {
                ShowMapStatus($"⚠️ Eksport nie powiódł się: {ex.Message}");
            }
        }

        /// <summary>Karta zakładki Eksport: te same pozycje co w menu „📤 Eksport…”, a przy .KMZ — tryb GRG.</summary>
        private static StackPanel BuildExportOptionsCard(Window win)
        {
            var card = CreateSectionCard("EKSPORT — WYBIERZ, CO CHCESZ OTRZYMAĆ");
            card.Children.Add(new TextBlock
            {
                Text = "Te same pozycje otwiera przycisk „📤 Eksport…” na dole panelu (Ctrl+E) — z każdej zakładki. Mapę offline ustawiasz i budujesz w karcie poniżej.",
                Foreground = TacticalConfigPanel.BrushMutedText,
                FontSize = 9.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            });

            bool firstGroup = true;
            foreach (var group in ExportCatalog.Grouped(exportTabOnly: true))
            {
                card.Children.Add(MapEditorUi.GroupCaption(group.Key, firstGroup));
                firstGroup = false;
                foreach (var option in group)
                {
                    string id = option.Id;
                    card.Children.Add(MapEditorUi.ActionRow(option.Glyph, option.Title, option.Note, () => RunExportOption(win, id)));
                    if (id == ExportCatalog.Kmz)
                    {
                        var grgMode = TacticalConfigPanel.FieldBox("Tryb GRG w pliku .KMZ:", TacticalConfigPanel.ComboCtrl(
                            new[] { "Przezroczysta siatka", "Pełny podkład + siatka", "Tylko podkład", "Tylko wektory KML (Bez obrazu)" },
                            _kmzGrgMode, win, v => _kmzGrgMode = v));
                        if (grgMode is FrameworkElement grgElement) grgElement.Margin = new Thickness(30, -2, 0, 6);
                        card.Children.Add(grgMode);
                    }
                }
            }
            return card;
        }
    }
}
