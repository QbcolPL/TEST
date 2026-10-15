#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace FieldTakHub.Builder.Services
{
    public enum TacticalMapTool
    {
        Pan,
        AddMarker,
        DrawZone,
        DrawRoute
    }

    public class TacticalMapMarker
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string Name { get; set; } = "HQ BLUFOR";
        public string Category { get; set; } = "HQ BLUFOR";
        public string Faction { get; set; } = "BLUFOR";
        public double Lat { get; set; }
        public double Lon { get; set; }
        public string GridCallout { get; set; } = "A1 (KP5)";
    }

    public class TacticalMapPoly
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string Name { get; set; } = "Strefa Gry AO";
        public string Faction { get; set; } = "ALL";
        public bool IsClosedZone { get; set; } = true;
        public List<(double Lat, double Lon)> Vertices { get; set; } = new List<(double Lat, double Lon)>();
    }

    /// <summary>
    /// Host zakładki Generatora Map Taktycznych w oknie głównym Field TAK Hub Buildera.
    /// Zapewnia osobny ekran (odrębny od Trybu prostego i Trybu zaawansowanego) w jednolitej stylistyce 2.4.
    /// </summary>
    public static class TacticalMapGeneratorHost
    {
        private static Grid _hostWrapperGrid = null;
        private static UIElement _originalBuilderContent = null;
        private static TacticalMapGeneratorControl _mapGeneratorScreen = null;
        private static Button _headerTabButton = null;

        public static void AttachMapTabToMainWindow(Window win)
        {
            if (Application.Current == null || win == null) return;

            try
            {
                // 1. Owijamy zawartość okna w Grid nadrzędny pozwalający przełączać ekran Konfiguratora i ekran Generatora Map
                if (_hostWrapperGrid == null && win.Content is UIElement currentRoot && (currentRoot as FrameworkElement)?.Tag?.ToString() != "FTH_MAPGEN_ROOT")
                {
                    _originalBuilderContent = currentRoot;
                    _hostWrapperGrid = new Grid { Tag = "FTH_MAPGEN_ROOT", Background = TacticalConfigPanel.BrushCardBg };
                    win.Content = null;
                    _hostWrapperGrid.Children.Add(_originalBuilderContent);

                    _mapGeneratorScreen = new TacticalMapGeneratorControl(win)
                    {
                        Visibility = Visibility.Collapsed
                    };
                    _hostWrapperGrid.Children.Add(_mapGeneratorScreen);
                    win.Content = _hostWrapperGrid;
                }

                // 2. Wyszukujemy w nagłówku przyciski przełączania "Tryb prosty" / "Tryb zaawansowany" i dokładamy 3. zakładkę
                if (_originalBuilderContent != null)
                {
                    var buttons = new List<ButtonBase>();
                    TacticalConfigPanel.FindVisualChildren(_originalBuilderContent, buttons);

                    ButtonBase modeBtn = null;
                    foreach (var b in buttons)
                    {
                        string text = GetButtonText(b).ToLowerInvariant();
                        if (text.Contains("tryb prosty") || text.Contains("tryb zaawansowany") ||
                            text.Equals("prosty") || text.Equals("zaawansowany") ||
                            text.Contains("simple mode") || text.Contains("advanced mode"))
                        {
                            modeBtn = b;
                            // Podpinamy wyłączenie ekranu mapy, gdyby użytkownik kliknął natywny przycisk trybu
                            b.Click += (s, e) => CloseMapGeneratorTab();
                        }
                    }

                    if (modeBtn != null && VisualTreeHelper.GetParent(modeBtn) is Panel modePanel && !TacticalConfigPanel.HasInjectedTag(modePanel, "INJECTED_MAP_TAB_BTN"))
                    {
                        var btnMapTab = new Button
                        {
                            Content = "🗺️ Generator Map Taktycznych",
                            Height = double.IsNaN(modeBtn.Height) || modeBtn.Height <= 0 ? 32 : modeBtn.Height,
                            Padding = new Thickness(14, 0, 14, 0),
                            Margin = new Thickness(8, 0, 0, 0),
                            Background = TacticalConfigPanel.BrushItemBg,
                            Foreground = TacticalConfigPanel.BrushAccent,
                            BorderBrush = TacticalConfigPanel.BrushAccent,
                            BorderThickness = new Thickness(1),
                            FontWeight = FontWeights.Bold,
                            FontSize = 12,
                            Cursor = Cursors.Hand,
                            Tag = "INJECTED_MAP_TAB_BTN",
                            ToolTip = "Otwórz osobny ekran Generatora Map Taktycznych (KMZ / GPX / PDF A4, A3, Zeszytowy / Import do .ftak)"
                        };
                        btnMapTab.Click += (s, e) => OpenMapGeneratorTab(win);
                        _headerTabButton = btnMapTab;

                        if (modePanel is UniformGrid ug)
                        {
                            if (ug.Columns > 0) ug.Columns += 1;
                            ug.Children.Add(btnMapTab);
                        }
                        else if (modePanel is Grid g && g.ColumnDefinitions.Count > 0)
                        {
                            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                            Grid.SetColumn(btnMapTab, g.ColumnDefinitions.Count - 1);
                            g.Children.Add(btnMapTab);
                        }
                        else
                        {
                            modePanel.Children.Add(btnMapTab);
                        }
                    }
                }
            }
            catch { }
        }

        private static string GetButtonText(ButtonBase b)
        {
            if (b?.Content is string s) return s;
            var tbs = new List<TextBlock>();
            TacticalConfigPanel.FindVisualChildren(b, tbs);
            return string.Join(" ", tbs.Select(t => t.Text ?? ""));
        }

        public static void OpenMapGeneratorTab(Window win)
        {
            if (_hostWrapperGrid == null) AttachMapTabToMainWindow(win);
            if (_originalBuilderContent != null && _mapGeneratorScreen != null)
            {
                _mapGeneratorScreen.SyncMetadataFromBuilder();
                _originalBuilderContent.Visibility = Visibility.Collapsed;
                _mapGeneratorScreen.Visibility = Visibility.Visible;
            }
        }

        public static void CloseMapGeneratorTab()
        {
            if (_originalBuilderContent != null && _mapGeneratorScreen != null)
            {
                _mapGeneratorScreen.Visibility = Visibility.Collapsed;
                _originalBuilderContent.Visibility = Visibility.Visible;
            }
        }
    }

    /// <summary>
    /// Główny ekran zakładki "Generator Map Taktycznych" w stylistyce Field TAK Hub Buildera.
    /// </summary>
    public class TacticalMapGeneratorControl : Border
    {
        private readonly Window _parentWindow;
        private static readonly HttpClient SharedHttp = CreateHttpClient();
        private static readonly Dictionary<string, BitmapSource> MemoryTileCache = new Dictionary<string, BitmapSource>();

        // Parametry mapy (domyślnie Polska Południowa / Śląsk: 49.9135, 19.0238)
        private double _centerLat = 49.9135;
        private double _centerLon = 19.0238;
        private int _zoom = 15;
        private string _tileProvider = "OpenTopoMap (Topograficzna / Poziomice)";
        private string _mapFilter = "Przyciemniony Taktyczny (Kontrast Siatki)";

        // Siatka taktyczna
        private string _gridType = "Siatka Kwadratowa (A1, B2...) + Keypad 1-9";
        private int _gridStepMeters = 250;
        private string _gridColorName = "Turkusowy TAK (#1FE0C5)";
        private bool _showCompassAndScale = true;
        private bool _showCoordCollar = true;

        // Dane operacyjne
        private TextBox _txtOpName;
        private TextBox _txtSubtitle;
        private TextBox _txtRadioInfo;
        private TextBox _txtRoeNotes;
        private TextBox _txtSearchQuery;
        private TextBox _txtCenterCoords;
        private ComboBox _cbPrintFormat;

        // Narzędzia rysowania i obiekty
        private TacticalMapTool _activeTool = TacticalMapTool.Pan;
        private string _selectedPoiCategory = "HQ BLUFOR";
        private string _selectedFaction = "BLUFOR";
        private TextBox _txtNewObjectName;

        private readonly List<TacticalMapMarker> _markers = new List<TacticalMapMarker>();
        private readonly List<TacticalMapPoly> _polys = new List<TacticalMapPoly>();
        private TacticalMapPoly _activeDrawingPoly = null;

        // Interaktywne płótno
        private Canvas _mapCanvas;
        private TextBlock _statusText;
        private TextBlock _scaleAndGridInfoText;
        private StackPanel _objectsListPanel;
        private readonly List<Button> _toolButtons = new List<Button>();

        private bool _isDraggingMap = false;
        private Point _dragStartPoint;
        private double _dragStartLat;
        private double _dragStartLon;

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("FieldTakHubBuilder/2.4 (TacticalMapGenerator; +https://github.com/QbcolPL)");
            return client;
        }

        public TacticalMapGeneratorControl(Window win)
        {
            _parentWindow = win;
            Background = TacticalConfigPanel.BrushInputBg;
            Padding = new Thickness(14);

            // Domyślne przykładowe punkty startowe
            _markers.Add(new TacticalMapMarker { Name = "HQ BLUFOR", Category = "HQ BLUFOR", Faction = "BLUFOR", Lat = _centerLat + 0.0022, Lon = _centerLon - 0.0035 });
            _markers.Add(new TacticalMapMarker { Name = "HQ OPFOR", Category = "HQ OPFOR", Faction = "OPFOR", Lat = _centerLat - 0.0022, Lon = _centerLon + 0.0035 });
            _markers.Add(new TacticalMapMarker { Name = "SAFEZONE / PARKING", Category = "SAFEZONE / ORGA", Faction = "ORGA", Lat = _centerLat - 0.0032, Lon = _centerLon - 0.0040 });
            _markers.Add(new TacticalMapMarker { Name = "OBJ ALFA (Ratusz)", Category = "CEL MISJI (OBJECTIVE)", Faction = "ALL", Lat = _centerLat + 0.0004, Lon = _centerLon + 0.0005 });

            BuildLayout();
        }

        public void SyncMetadataFromBuilder()
        {
            try
            {
                string evName = TacticalConfigPanel.GetEventNameFromWindow(_parentWindow);
                if (!string.IsNullOrWhiteSpace(evName) && _txtOpName != null && _txtOpName.Text == "OPERACJA AEGIS")
                {
                    _txtOpName.Text = evName.Replace('_', ' ').ToUpperInvariant();
                }
            }
            catch { }
            RedrawInteractiveCanvas();
        }

        private void BuildLayout()
        {
            var rootGrid = new Grid();
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Pasek zakładek (Prosty / Zaawansowany / Generator Map)
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Główny obszar roboczy
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Dolny pasek statusu

            // 1. GÓRNY PASEK NAWIGACJI ZAKŁADEK (JEDNOLITY Z BUILDEREM)
            var topNavBorder = new Border
            {
                Background = TacticalConfigPanel.BrushCardBg,
                BorderBrush = TacticalConfigPanel.BrushCardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 12)
            };

            var topNavGrid = new Grid();
            topNavGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            topNavGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topNavGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleStack = new StackPanel { Orientation = Orientation.Vertical };
            titleStack.Children.Add(new TextBlock
            {
                Text = "🗺️ FIELD TAK HUB 2.4 — GENERATOR MAP TAKTYCZNYCH",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 16
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = "Twórz siatki meldunkowe (A1..Z9 + Keypad 1-9), nanosź sztaby, strefy i eksportuj bezpośrednio do paczki .ftak, pliku KMZ, GPX lub PDF (A4 / A3 / Zeszytowy).",
                Foreground = TacticalConfigPanel.BrushMutedText,
                FontSize = 11.5
            });
            Grid.SetColumn(titleStack, 0);
            topNavGrid.Children.Add(titleStack);

            var modeTabsStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            var btnBackToBuilder = new Button
            {
                Content = "⬅ Powrót do Konfiguratora Paczki (Tryb Prosty / Zaawansowany)",
                Height = 34,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(0, 0, 8, 0),
                Background = TacticalConfigPanel.BrushItemBg,
                Foreground = Brushes.White,
                BorderBrush = TacticalConfigPanel.BrushTileBorder,
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand
            };
            btnBackToBuilder.Click += (s, e) => TacticalMapGeneratorHost.CloseMapGeneratorTab();

            var btnActiveMapTab = new Button
            {
                Content = "🗺️ Generator Map Taktycznych (Aktywny)",
                Height = 34,
                Padding = new Thickness(14, 0, 14, 0),
                Background = TacticalConfigPanel.BrushButtonAccent,
                Foreground = TacticalConfigPanel.BrushDarkFg,
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.Bold
            };

            modeTabsStack.Children.Add(btnBackToBuilder);
            modeTabsStack.Children.Add(btnActiveMapTab);
            Grid.SetColumn(modeTabsStack, 2);
            topNavGrid.Children.Add(modeTabsStack);

            topNavBorder.Child = topNavGrid;
            Grid.SetRow(topNavBorder, 0);
            rootGrid.Children.Add(topNavBorder);

            // 2. GŁÓWNY OBSZAR ROBOCZY (LEWY PANEL KONTROLNY + PRAWE PŁÓTNO MAPY)
            var workspaceGrid = new Grid();
            workspaceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(430) });
            workspaceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            workspaceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var leftScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(0, 0, 4, 0)
            };
            leftScroll.Content = BuildLeftControlPanel();
            Grid.SetColumn(leftScroll, 0);
            workspaceGrid.Children.Add(leftScroll);

            var rightMapArea = BuildRightMapCanvasArea();
            Grid.SetColumn(rightMapArea, 2);
            workspaceGrid.Children.Add(rightMapArea);

            Grid.SetRow(workspaceGrid, 1);
            rootGrid.Children.Add(workspaceGrid);

            // 3. DOLNY PASEK STATUSU I SZYBKICH AKCJI EKSPORTU
            var bottomBar = new Border
            {
                Background = TacticalConfigPanel.BrushCardBg,
                BorderBrush = TacticalConfigPanel.BrushCardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 8, 14, 8),
                Margin = new Thickness(0, 10, 0, 0)
            };
            var bottomGrid = new Grid();
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _statusText = new TextBlock
            {
                Text = "✓ Gotowy. Przeciągnij mapę myszą (LPM), użyj rolki do przybliżenia lub wybierz narzędzie dodawania punktów taktycznych.",
                Foreground = TacticalConfigPanel.BrushAccent,
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_statusText, 0);
            bottomGrid.Children.Add(_statusText);

            _scaleAndGridInfoText = new TextBlock
            {
                Text = "Siatka: 250m | Zoom: 15 | WGS84",
                Foreground = TacticalConfigPanel.BrushMutedText,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_scaleAndGridInfoText, 1);
            bottomGrid.Children.Add(_scaleAndGridInfoText);

            bottomBar.Child = bottomGrid;
            Grid.SetRow(bottomBar, 2);
            rootGrid.Children.Add(bottomBar);

            Child = rootGrid;
        }

        private UIElement BuildLeftControlPanel()
        {
            var stack = new StackPanel();

            // KARTA 1: LOKALIZACJA, PODKŁAD MAPOWY I SIATKA TAKTYCZNA
            var cardMap = CreateSectionCard("1. Teren Działań, Podkład i Siatka Meldunkowa");
            var mapPanel = (StackPanel)cardMap.Child;

            var searchGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });

            _txtSearchQuery = (TextBox)TacticalConfigPanel.TextCtrl("Czechowice-Dziedzice / Poligon", _ => { });
            _txtSearchQuery.Margin = new Thickness(0, 0, 6, 0);
            Grid.SetColumn(_txtSearchQuery, 0);
            searchGrid.Children.Add(_txtSearchQuery);

            var btnSearch = CreateActionButton("🔍 Szukaj", false, async () => await SearchLocationByNameAsync(_txtSearchQuery.Text));
            btnSearch.Height = 28;
            Grid.SetColumn(btnSearch, 1);
            searchGrid.Children.Add(btnSearch);
            mapPanel.Children.Add(TacticalConfigPanel.FieldBox("Wyszukaj miejscowość / poligon (OSM Nominatim) lub wpisz Lat, Lon", searchGrid));

            var coordsGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            coordsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            coordsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(75) });
            coordsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            coordsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

            _txtCenterCoords = (TextBox)TacticalConfigPanel.TextCtrl(\$"{_centerLat.ToString("0.0000", CultureInfo.InvariantCulture)}, {_centerLon.ToString("0.0000", CultureInfo.InvariantCulture)}", _ => { });
            _txtCenterCoords.Margin = new Thickness(0, 0, 6, 0);
            Grid.SetColumn(_txtCenterCoords, 0);
            coordsGrid.Children.Add(_txtCenterCoords);

            var btnGoCoords = CreateActionButton("Idź", false, () =>
            {
                var parts = _txtCenterCoords.Text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 &&
                    double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double lat) &&
                    double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double lon))
                {
                    _centerLat = Math.Clamp(lat, -85.0, 85.0);
                    _centerLon = Math.Clamp(lon, -180.0, 180.0);
                    RedrawInteractiveCanvas();
                }
            });
            btnGoCoords.Height = 28;
            btnGoCoords.Margin = new Thickness(0, 0, 4, 0);
            Grid.SetColumn(btnGoCoords, 1);
            coordsGrid.Children.Add(btnGoCoords);

            var btnZoomIn = CreateActionButton("＋", false, () => { _zoom = Math.Clamp(_zoom + 1, 11, 18); RedrawInteractiveCanvas(); });
            btnZoomIn.Height = 28;
            btnZoomIn.Margin = new Thickness(0, 0, 4, 0);
            Grid.SetColumn(btnZoomIn, 2);
            coordsGrid.Children.Add(btnZoomIn);

            var btnZoomOut = CreateActionButton("－", false, () => { _zoom = Math.Clamp(_zoom - 1, 11, 18); RedrawInteractiveCanvas(); });
            btnZoomOut.Height = 28;
            Grid.SetColumn(btnZoomOut, 3);
            coordsGrid.Children.Add(btnZoomOut);
            mapPanel.Children.Add(TacticalConfigPanel