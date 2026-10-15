#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace FieldTakHub.Builder.Services
{
    public class TacticalMapLayerDef
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public bool IsWmsEpsg3857 { get; set; }
        public string UrlTemplate { get; set; }
        public override string ToString() => Name;
    }

    public class TacticalMapPin
    {
        public string Name { get; set; }
        public string Faction { get; set; }
        public string ColorHex { get; set; }
        public double Lat { get; set; }
        public double Lon { get; set; }
        public string Type { get; set; }
    }

    public static class TacticalMapGeneratorView
    {
        private static readonly HttpClient Http = CreateHttpClient();
        private static readonly ConcurrentDictionary<string, byte[]> TileCache = new ConcurrentDictionary<string, byte[]>();

        private static Border _fullScreenOverlay = null;
        private static Button _headerTabButton = null;
        private static Canvas _mapCanvas = null;
        private static TextBlock _statusText = null;
        private static TextBlock _coordsBadge = null;
        private static ListBox _pinsListBox = null;

        // Domyślny środek: Polska Południowa / Śląsk (49.9135, 19.0238)
        private static double _centerLat = 49.9135;
        private static double _centerLon = 19.0238;
        private static int _zoom = 14;
        private static bool _showGrid = true;
        private static bool _showHybridLabels = false;
        private static bool _isDragging = false;
        private static Point _dragStartPoint;
        private static double _dragStartLat;
        private static double _dragStartLon;

        private static readonly List<TacticalMapPin> _pins = new List<TacticalMapPin>();
        private static TacticalMapLayerDef _activeLayer = null;
        private static TextBox _mapTitleBox = null;
        private static TextBox _latInputBox = null;
        private static TextBox _lonInputBox = null;
        private static ComboBox _pinFactionCombo = null;
        private static ComboBox _pinTypeCombo = null;
        private static TextBox _pinNameBox = null;

        public static readonly List<TacticalMapLayerDef> Layers = new List<TacticalMapLayerDef>
        {
            new TacticalMapLayerDef { Id = "google_hyb", Name = "Google Maps — Hybryda (Satelita + Drogi)", Category = "Google", IsWmsEpsg3857 = false, UrlTemplate = "https://mt1.google.com/vt/lyrs=y&x={x}&y={y}&z={z}" },
            new TacticalMapLayerDef { Id = "google_sat", Name = "Google Maps — Satelita (Orto)", Category = "Google", IsWmsEpsg3857 = false, UrlTemplate = "https://mt1.google.com/vt/lyrs=s&x={x}&y={y}&z={z}" },
            new TacticalMapLayerDef { Id = "google_ter", Name = "Google Maps — Teren / Topo", Category = "Google", IsWmsEpsg3857 = false, UrlTemplate = "https://mt1.google.com/vt/lyrs=p&x={x}&y={y}&z={z}" },
            new TacticalMapLayerDef { Id = "geoportal_hires", Name = "Geoportal PL — Ortofotomapa HIGH-RES (Wysoka rozdz.)", Category = "Geoportal", IsWmsEpsg3857 = true, UrlTemplate = "https://mapy.geoportal.gov.pl/wss/service/PZGIK/ORTO/WMS/HighResolution?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=Raster&STYLES=&SRS=EPSG:3857&BBOX={bbox}&WIDTH=256&HEIGHT=256&FORMAT=image/jpeg" },
            new TacticalMapLayerDef { Id = "geoportal_std", Name = "Geoportal PL — Ortofotomapa Standard", Category = "Geoportal", IsWmsEpsg3857 = true, UrlTemplate = "https://mapy.geoportal.gov.pl/wss/service/PZGIK/ORTO/WMS/StandardResolution?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=Raster&STYLES=&SRS=EPSG:3857&BBOX={bbox}&WIDTH=256&HEIGHT=256&FORMAT=image/jpeg" },
            new TacticalMapLayerDef { Id = "geoportal_lidar", Name = "Geoportal PL — LIDAR (ISOK Cieniowanie NMT)", Category = "Geoportal", IsWmsEpsg3857 = true, UrlTemplate = "https://mapy.geoportal.gov.pl/wss/service/PZGIK/NMT/GRID1/WMS/ShadedRelief?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=Raster&STYLES=&SRS=EPSG:3857&BBOX={bbox}&WIDTH=256&HEIGHT=256&FORMAT=image/jpeg" },
            new TacticalMapLayerDef { Id = "geoportal_topo", Name = "Geoportal PL — Mapa Topograficzna (BDOT10k / Topo)", Category = "Geoportal", IsWmsEpsg3857 = true, UrlTemplate = "https://mapy.geoportal.gov.pl/wss/ext/KrajowaIntegracjaMapTopograficznych?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=raster&STYLES=&SRS=EPSG:3857&BBOX={bbox}&WIDTH=256&HEIGHT=256&FORMAT=image/png" },
            new TacticalMapLayerDef { Id = "esri_sat", Name = "ESRI — World Imagery (Ortofotomapa)", Category = "ESRI", IsWmsEpsg3857 = false, UrlTemplate = "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}" },
            new TacticalMapLayerDef { Id = "esri_topo", Name = "ESRI — World Topographic Map", Category = "ESRI", IsWmsEpsg3857 = false, UrlTemplate = "https://server.arcgisonline.com/ArcGIS/rest/services/World_Topo_Map/MapServer/tile/{z}/{y}/{x}" },
            new TacticalMapLayerDef { Id = "osm_std", Name = "OpenStreetMap — OSM Standard", Category = "OSM", IsWmsEpsg3857 = false, UrlTemplate = "https://tile.openstreetmap.org/{z}/{x}/{y}.png" },
            new TacticalMapLayerDef { Id = "open_topomap", Name = "OpenTopoMap — Mapa Topograficzna (Warstwice)", Category = "OpenMap", IsWmsEpsg3857 = false, UrlTemplate = "https://a.tile.opentopomap.org/{z}/{x}/{y}.png" },
            new TacticalMapLayerDef { Id = "open_fotomap", Name = "OpenFotoMap / CyclOSM — Taktyczna Siatka Szlaków", Category = "OpenMap", IsWmsEpsg3857 = false, UrlTemplate = "https://a.tile-cyclosm.openstreetmap.fr/cyclosm/{z}/{x}/{y}.png" }
        };

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("FieldTakHubBuilder/2.4 (Windows NT 10.0; Win64; x64)");
            return client;
        }

        // Podpina osobną zakładkę obok Trybu Prostego i Zaawansowanego oraz pełnoekranowy widok Generatora Map
        public static void AttachFullScreenTab(Window win)
        {
            if (Application.Current == null || win == null) return;
            try
            {
                if (win.Content is not Grid rootGrid) return;

                // 1. Utworzenie pełnoekranowego kontenera zakładki Generatora Map
                if (_fullScreenOverlay == null || _fullScreenOverlay.Parent == null)
                {
                    _fullScreenOverlay = BuildFullScreenMapGeneratorUI(win);
                    Grid.SetRow(_fullScreenOverlay, 0);
                    Grid.SetColumn(_fullScreenOverlay, 0);
                    if (rootGrid.RowDefinitions.Count > 0) Grid.SetRowSpan(_fullScreenOverlay, rootGrid.RowDefinitions.Count);
                    if (rootGrid.ColumnDefinitions.Count > 0) Grid.SetColumnSpan(_fullScreenOverlay, rootGrid.ColumnDefinitions.Count);
                    Panel.SetZIndex(_fullScreenOverlay, 900);
                    _fullScreenOverlay.Visibility = Visibility.Collapsed;
                    rootGrid.Children.Add(_fullScreenOverlay);
                }

                // 2. Wstrzyknięcie przycisku zakładki "🗺️ Generator Map" obok przycisków Tryb Prosty / Zaawansowany
                var allButtons = new List<Button>();
                TacticalConfigPanel.FindVisualChildren(win, allButtons);
                Button modeBtn = allButtons.FirstOrDefault(b =>
                {
                    string c = b.Content?.ToString() ?? "";
                    return c.Contains("Zaawansowany", StringComparison.OrdinalIgnoreCase) || c.Contains("Advanced", StringComparison.OrdinalIgnoreCase) || c.Contains("Prosty", StringComparison.OrdinalIgnoreCase);
                });

                if (modeBtn != null && VisualTreeHelper.GetParent(modeBtn) is Panel modeBarPanel)
                {
                    bool alreadyAdded = false;
                    foreach (UIElement el in modeBarPanel.Children)
                    {
                        if (el is FrameworkElement fe && fe.Tag?.ToString() == "INJECTED_MAP_TAB_BTN")
                        {
                            alreadyAdded = true;
                            break;
                        }
                    }

                    if (!alreadyAdded)
                    {
                        _headerTabButton = new Button
                        {
                            Content = "🗺️ Generator Map (Pełny Ekran)",
                            Height = modeBtn.ActualHeight > 24 ? modeBtn.ActualHeight : 30,
                            Padding = new Thickness(14, 0, 14, 0),
                            Margin = new Thickness(8, 0, 0, 0),
                            Background = TacticalConfigPanel.BrushItemBg,
                            Foreground = TacticalConfigPanel.BrushAccent,
                            BorderBrush = TacticalConfigPanel.BrushAccent,
                            BorderThickness = new Thickness(1),
                            FontWeight = FontWeights.Bold,
                            Cursor = Cursors.Hand,
                            Tag = "INJECTED_MAP_TAB_BTN"
                        };
                        _headerTabButton.Click += (s, e) => ShowFullScreenMapTab(win);
                        modeBarPanel.Children.Add(_headerTabButton);

                        // Kliknięcie Tryb Prosty lub Zaawansowany automatycznie chowa zakładkę mapy
                        foreach (var b in allButtons)
                        {
                            string c = b.Content?.ToString() ?? "";
                            if (c.Contains("Prosty", StringComparison.OrdinalIgnoreCase) || c.Contains("Zaawansowany", StringComparison.OrdinalIgnoreCase) || c.Contains("Simple", StringComparison.OrdinalIgnoreCase) || c.Contains("Advanced", StringComparison.OrdinalIgnoreCase))
                            {
                                b.Click += (s, e) => HideFullScreenMapTab();
                            }
                        }
                    }
                }
            }
            catch { }
        }

        public static void ShowFullScreenMapTab(Window win)
        {
            if (_fullScreenOverlay == null && win != null) AttachFullScreenTab(win);
            if (_fullScreenOverlay != null)
            {
                RefreshFactionDropdown();
                _fullScreenOverlay.Visibility = Visibility.Visible;
                _ = RenderMapTilesAsync();
            }
        }

        public static void HideFullScreenMapTab()
        {
            if (_fullScreenOverlay != null)
            {
                _fullScreenOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private static void RefreshFactionDropdown()
        {
            if (_pinFactionCombo == null) return;
            var factions = TacticalConfigPanel.GetConfiguredFactions().Select(f => f.Name).ToArray();
            _pinFactionCombo.ItemsSource = factions;
            if (_pinFactionCombo.SelectedItem == null && factions.Length > 0)
                _pinFactionCombo.SelectedIndex = 0;
        }

        private static Border BuildFullScreenMapGeneratorUI(Window win)
        {
            _activeLayer = Layers[0];

            var rootBorder = new Border
            {
                Background = TacticalConfigPanel.BrushCardBg,
                BorderBrush = TacticalConfigPanel.BrushCardBorder,
                BorderThickness = new Thickness(0),
                Tag = "INJECTED_MAP_OVERLAY"
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(54) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });

            // 1. GÓRNY PASEK NAWIGACYJNY PEŁNOEKRANOWEJ ZAKŁADKI
            var topBar = new Border
            {
                Background = TacticalConfigPanel.BrushInputBg,
                BorderBrush = TacticalConfigPanel.BrushCardBorder,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, 0, 16, 0)
            };
            var topGrid = new Grid();
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            titleStack.Children.Add(new TextBlock
            {
                Text = "🗺️ GENERATOR MAP TAKTYCZNYCH 2.4",
                Foreground = TacticalConfigPanel.BrushAccent,
                FontWeight = FontWeights.Bold,
                FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 16, 0)
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = "Google • Geoportal Orto Hi-Res • LIDAR ISOK • Topo BDOT10k • ESRI • OSM • OpenTopoMap • Eksport FTAK / KMZ / GPX / PDF",
                Foreground = TacticalConfigPanel.BrushMutedText,
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center
            });
            Grid.SetColumn(titleStack, 0);
            topGrid.Children.Add(titleStack);

            var modeButtonsStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var btnBackToBuilder = new Button
            {
                Content = "⬅ Powrót do Buildera Paczek (Tryb Prosty / Zaawansowany)",
                Height = 32,
                Padding = new Thickness(16, 0, 16, 0),
                Background = TacticalConfigPanel.BrushItemBg,
                Foreground = Brushes.White,
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            btnBackToBuilder.Click += (s, e) => HideFullScreenMapTab();
            modeButtonsStack.Children.Add(btnBackToBuilder);
            Grid.SetColumn(modeButtonsStack, 2);
            topGrid.Children.Add(modeButtonsStack);
            topBar.Child = topGrid;
            Grid.SetRow(topBar, 0);
            mainGrid.Children.Add(topBar);

            // 2. GŁÓWNY OBSZAR ROBOCZY (LEWY PANEL KONTROLNY + INTERAKTYWNA MAPA PEŁNOEKRANOWA)
            var bodyGrid = new Grid();
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Lewy panel sterowania
            var leftScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = TacticalConfigPanel.BrushCardBg,
                Padding = new Thickness(14)
            };
            var leftStack = new StackPanel { Margin = new Thickness(12) };

            // Karta 1: Wybór podkładu mapowego
            var cardLayer = CreateSectionCard("1. Warstwa Podkładowa (Orto / LIDAR / Topo)");
            var layerCombo = new ComboBox
            {
                ItemsSource = Layers,
                SelectedIndex = 0,
                Height = 30,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 8),
                Tag = "INJECTED"
            };
            TacticalConfigPanel.ApplyUnifiedComboBoxTheme(layerCombo, win);
            layerCombo.SelectionChanged += (s, e) =>
            {
                if (layerCombo.SelectedItem is TacticalMapLayerDef def)
                {
                    _activeLayer = def;
                    _ = RenderMapTilesAsync();
                }
            };
            cardLayer.Children.Add(TacticalConfigPanel.FieldBox("Źródło kafelków / WMS:", layerCombo));

            var chkGrid = new CheckBox
            {
                Content = "Siatka taktyczna współrzędnych (WGS84 / MGRS)",
                IsChecked = true,
                Foreground = Brushes.White,
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 6)
            };
            chkGrid.Checked += (_, __) => { _showGrid = true; _ = RenderMapTilesAsync(); };
            chkGrid.Unchecked += (_, __) => { _showGrid = false; _ = RenderMapTilesAsync(); };
            cardLayer.Children.Add(chkGrid);

            var chkHybrid = new CheckBox
            {
                Content = "Nakładka hybrydowa dróg i nazw miejscowości",
                IsChecked = false,
                Foreground = Brushes.White,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 4)
            };
            chkHybrid.Checked += (_, __) => { _showHybridLabels = true; _ = RenderMapTilesAsync(); };
            chkHybrid.Unchecked += (_, __) => { _showHybridLabels = false; _ = RenderMapTilesAsync(); };
            cardLayer.Children.Add(chkHybrid);
            leftStack.Children.Add(WrapCard(cardLayer));

            // Karta 2: Nawigacja i Współrzędne
            var cardNav = CreateSectionCard("2. Obszar Operacji & Zoom");
            _mapTitleBox = (TextBox)TacticalConfigPanel.TextCtrl("Mapa_Taktyczna_AO", _ => { });
            cardNav.Children.Add(TacticalConfigPanel.FieldBox("Nazwa arkusza mapy / operacji:", _mapTitleBox));

            var coordsGrid = new UniformGrid { Columns = 2 };
            _latInputBox = (TextBox)TacticalConfigPanel.TextCtrl(_centerLat.ToString("0.0000", CultureInfo.InvariantCulture), _ => { });
            _lonInputBox = (TextBox)TacticalConfigPanel.TextCtrl(_centerLon.ToString("0.0000", CultureInfo.InvariantCulture), _ => { });
            coordsGrid.Children.Add(TacticalConfigPanel.FieldBox("Szerokość (Lat):", _latInputBox));
            coordsGrid.Children.Add(TacticalConfigPanel.FieldBox("Długość (Lon):", _lonInputBox));
            cardNav.Children.Add(coordsGrid);

            var zoomRow = new UniformGrid { Columns = 3, Margin = new Thickness(0, 4, 0, 0) };
            var btnZoomOut = MakeBtn("➖ Zoom -", false, () => { _zoom = Math.Max(5, _zoom - 1); _ = RenderMapTilesAsync(); });
            var btnJump = MakeBtn("🎯 Idź do Lat/Lon", true, () =>
            {
                if (double.TryParse(_latInputBox.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double la) &&
                    double.TryParse(_lonInputBox.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double lo))
                {
                    _centerLat = Math.Clamp(la, -85.0, 85.0);
                    _centerLon = Math.Clamp(lo, -180.0, 180.0);
                    _ = RenderMapTilesAsync();
                }
            });
            var btnZoomIn = MakeBtn("➕ Zoom +", false, () => { _zoom = Math.Min(19, _zoom + 1); _ = RenderMapTilesAsync(); });
            zoomRow.Children.Add(btnZoomOut);
            zoomRow.Children.Add(btnJump);
            zoomRow.Children.Add(btnZoomIn);
            cardNav.Children.Add(zoomRow);
            leftStack.Children.Add(WrapCard(cardNav));

            // Karta 3: Punkty Taktyczne (KML / GPX)
            var cardPins = CreateSectionCard("3. Punkty i Strefy Taktyczne (Kliknij na mapie)");
            var pinFormGrid = new UniformGrid { Columns = 2 };
            _pinFactionCombo = new ComboBox { Height = 28, FontSize = 11, Tag = "INJECTED" };
            TacticalConfigPanel.ApplyUnifiedComboBoxTheme(_pinFactionCombo, win);
            RefreshFactionDropdown();

            _pinTypeCombo = (ComboBox)TacticalConfigPanel.ComboCtrl(new[] { "HQ (Sztab)", "Respawn / Baza", "Punkt Medyczny", "Cel / Objective", "Strefa Zrzutu", "Punkt Kontrolny" }, "HQ (Sztab)", win, _ => { });
            pinFormGrid.Children.Add(TacticalConfigPanel.FieldBox("Strona konfliktu:", _pinFactionCombo));
            pinFormGrid.Children.Add(TacticalConfigPanel.FieldBox("Typ punktu:", _pinTypeCombo));
            cardPins.Children.Add(pinFormGrid);

            _pinNameBox = (TextBox)TacticalConfigPanel.TextCtrl("Punkt-1", _ => { });
            cardPins.Children.Add(TacticalConfigPanel.FieldBox("Etykieta nowego punktu (LPM na mapie dodaje punkt):", _pinNameBox));

            _pinsListBox = new ListBox
            {
                Height = 95,
                Background = TacticalConfigPanel.BrushInputBg,
                Foreground = Brushes.White,
                BorderBrush = TacticalConfigPanel.BrushInputBorder,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 6)
            };
            cardPins.Children.Add(_pinsListBox);

            var btnClearPins = MakeBtn("− Wyczyść punkty taktyczne", false, () =>
            {
                _pins.Clear();
                RefreshPinsList();
                _ = RenderMapTilesAsync();
            });
            cardPins.Children.Add(btnClearPins);
            leftStack.Children.Add(WrapCard(cardPins));

            // Karta 4: Eksport do Paczki .ftak, KMZ/GPX oraz PDF/Druk
            var cardExport = CreateSectionCard("4. Eksport do Paczki .ftak / KMZ / GPX / PDF");

            var btnAddToFtak = MakeBtn("📦 Zapisz mapę + warstwy do paczki .ftak (source/maps)", true, async () =>
            {
                await ExportMapToFtakSourceAsync(win);
            });
            btnAddToFtak.Height = 34;
            btnAddToFtak.Margin = new Thickness(0, 0, 0, 6);
            cardExport.Children.Add(btnAddToFtak);

            var expRow1 = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 6) };
            expRow1.Children.Add(MakeBtn("🌐 Eksportuj KMZ (ATAK)", false, async () => await ExportStandaloneKmzAndGpxAsync(win, true, false)));
            expRow1.Children.Add(MakeBtn("🧭 Eksportuj GPX (Garmin/ATAK)", false, async () => await ExportStandaloneKmzAndGpxAsync(win, false, true)));
            cardExport.Children.Add(expRow1);

            var expRow2 = new UniformGrid { Columns = 2 };
            expRow2.Children.Add(MakeBtn("📄 Zapisz mapę PDF (A4)", false, async () => await ExportTacticalMapPdfAsync(win)));
            expRow2.Children.Add(MakeBtn("🖨️ Drukuj mapę A4", false, () => PrintTacticalMap(win)));
            cardExport.Children.Add(expRow2);

            leftStack.Children.Add(WrapCard(cardExport));
            leftScroll.Content = leftStack;
            Grid.SetColumn(leftScroll, 0);
            bodyGrid.Children.Add(leftScroll);

            // Prawa część: Pełnoekranowe płótno mapy taktycznej
            var mapBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(5, 12, 15)),
                BorderBrush = TacticalConfigPanel.BrushCardBorder,
                BorderThickness = new Thickness(1, 0, 0, 0),
                ClipToBounds = true
            };

            var mapOverlayGrid = new Grid();
            _mapCanvas = new Canvas { Background = new SolidColorBrush(Color.FromRgb(6, 15, 18)), ClipToBounds = true, Cursor = Cursors.Cross };

            // Obsługa przeciągania myszą (Pan), kółka myszy (Zoom) oraz kliknięcia (Dodanie punktu)
            _mapCanvas.MouseLeftButtonDown += (s, e) =>
            {
                _isDragging = true;
                _dragStartPoint = e.GetPosition(_mapCanvas);
                _dragStartLat = _centerLat;
                _dragStartLon = _centerLon;
                _mapCanvas.CaptureMouse();
            };

            _mapCanvas.MouseMove += (s, e) =>
            {
                Point p = e.GetPosition(_mapCanvas);
                var (hoverLat, hoverLon) = ScreenToLatLon(p.X, p.Y, _mapCanvas.ActualWidth, _mapCanvas.ActualHeight);
                if (_coordsBadge != null)
                {
                    _coordsBadge.Text = $"WGS84: {hoverLat:0.00000}, {hoverLon:0.00000}  |  Zoom: {_zoom}  |  Warstwa: {_activeLayer?.Name}";
                }

                if (_isDragging)
                {
                    double dx = p.X - _dragStartPoint.X;
                    double dy = p.Y - _dragStartPoint.Y;
                    double worldSize = 256.0 * Math.Pow(2, _zoom);
                    double lonPerPixel = 360.0 / worldSize;
                    double latPerPixel = (360.0 / worldSize) * Math.Cos(_dragStartLat * Math.PI / 180.0);

                    _centerLon = Math.Clamp(_dragStartLon - dx * lonPerPixel, -180.0, 180.0);
                    _centerLat = Math.Clamp(_dragStartLat + dy * latPerPixel, -85.0, 85.0);
                    _latInputBox.Text = _centerLat.ToString("0.0000", CultureInfo.InvariantCulture);
                    _lonInputBox.Text = _centerLon.ToString("0.0000", CultureInfo.InvariantCulture);
                }
            };

            _mapCanvas.MouseLeftButtonUp += (s, e) =>
            {
                if (!_isDragging) return;
                _isDragging = false;
                _mapCanvas.ReleaseMouseCapture();
                Point endPt = e.GetPosition(_mapCanvas);
                double dist = (endPt - _dragStartPoint).Length;

                if (dist < 5.0)
                {
                    // Pojedyncze kliknięcie dodaje punkt taktyczny
                    var (clickLat, clickLon) = ScreenToLatLon(endPt.X, endPt.Y, _mapCanvas.ActualWidth, _mapCanvas.ActualHeight);
                    string facName = _pinFactionCombo?.SelectedItem?.ToString() ?? "BLUFOR";
                    var facInfo = TacticalConfigPanel.GetConfiguredFactions().FirstOrDefault(f => f.Name.Equals(facName, StringComparison.OrdinalIgnoreCase));
                    string hex = string.IsNullOrEmpty(facInfo.ColorHex) ? "#1FE0C5" : facInfo.ColorHex;
                    string pType = _pinTypeCombo?.SelectedItem?.ToString() ?? "HQ";
                    string pName = string.IsNullOrWhiteSpace(_pinNameBox?.Text) ? $"Punkt-{_pins.Count + 1}" : _pinNameBox.Text.Trim();

                    _pins.Add(new TacticalMapPin
                    {
                        Name = pName,
                        Faction = facName,
                        ColorHex = hex,
                        Lat = clickLat,
                        Lon = clickLon,
                        Type = pType
                    });
                    _pinNameBox.Text = $"Punkt-{_pins.Count + 1}";
                    RefreshPinsList();
                }

                _ = RenderMapTilesAsync();
            };

            _mapCanvas.MouseWheel += (s, e) =>
            {
                int oldZoom = _zoom;
                _zoom = e.Delta > 0 ? Math.Min(19, _zoom + 1) : Math.Max(5, _zoom - 1);
                if (_zoom != oldZoom) _ = RenderMapTilesAsync();
            };

            _mapCanvas.SizeChanged += (s, e) => { _ = RenderMapTilesAsync(); };

            mapOverlayGrid.Children.Add(_mapCanvas);

            // Pływający pasek współrzędnych na górze mapy
            var hudBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(215, 8, 19, 22)),
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(14),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false
            };
            _coordsBadge = new TextBlock
            {
                Text = $"WGS84: {_centerLat:0.00000}, {_centerLon:0.00000}  |  Zoom: {_zoom}  |  Przeciągnij LPM = Przesuń  |  Kliknij LPM = Dodaj punkt",
                Foreground = Brushes.White,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold
            };
            hudBorder.Child = _coordsBadge;
            mapOverlayGrid.Children.Add(hudBorder);

            mapBorder.Child = mapOverlayGrid;
            Grid.SetColumn(mapBorder, 1);
            bodyGrid.Children.Add(mapBorder);

            Grid.SetRow(bodyGrid, 1);
            mainGrid.Children.Add(bodyGrid);

            // 3. DOLNY PASEK STATUSU
            var bottomBar = new Border
            {
                Background = TacticalConfigPanel.BrushInputBg,
                BorderBrush = TacticalConfigPanel.BrushCardBorder,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(16, 0, 16, 0)
            };
            _statusText = new TextBlock
            {
                Text = "Gotowy. Wybierz warstwę (Google / Geoportal Hi-Res / LIDAR / Topo / ESRI / OSM), ustaw obszar i wyeksportuj do paczki .ftak, KMZ/GPX lub PDF.",
                Foreground = TacticalConfigPanel.BrushAccent,
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center
            };
            bottomBar.Child = _statusText;
            Grid.SetRow(bottomBar, 2);
            mainGrid.Children.Add(bottomBar);

            rootBorder.Child = mainGrid;
            return rootBorder;
        }

        private static StackPanel CreateSectionCard(string title)
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 12.5,
                Margin = new Thickness(0, 0, 0, 8)
            });
            return sp;
        }

        private static Border WrapCard(UIElement child)
        {
            return new Border
            {
                Background = TacticalConfigPanel.BrushItemBg,
                BorderBrush = TacticalConfigPanel.BrushTileBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 12),
                Child = child
            };
        }

        private static Button MakeBtn(string text, bool accent, Action onClick)
        {
            var b = new Button
            {
                Content = text,
                Height = 30,
                Margin = new Thickness(2),
                Padding = new Thickness(8, 0, 8, 0),
                Background = accent ? TacticalConfigPanel.BrushButtonAccent : TacticalConfigPanel.BrushInputBg,
                Foreground = accent ? TacticalConfigPanel.BrushDarkFg : Brushes.White,
                BorderBrush = accent ? TacticalConfigPanel.BrushAccent : TacticalConfigPanel.BrushInputBorder,
                BorderThickness = new Thickness(1),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            b.Click += (_, __) => onClick();
            return b;
        }

        private static void RefreshPinsList()
        {
            if (_pinsListBox == null) return;
            _pinsListBox.Items.Clear();
            foreach (var p in _pins)
            {
                _pinsListBox.Items.Add($"[{p.Faction}] {p.Name} ({p.Type}) — {p.Lat:0.0000}, {p.Lon:0.0000}");
            }
        }

        // =========================================================================
        // SILNIK KAFELKÓW SLIPPY MAP + WMS EPSG:3857 (GEOPORTAL ORTO HI-RES / LIDAR / TOPO)
        // =========================================================================
        private static (double X, double Y) LatLonToTileXY(double lat, double lon, int zoom)
        {
            double latRad = Math.Clamp(lat, -85.0511, 85.0511) * Math.PI / 180.0;
            double n = Math.Pow(2.0, zoom);
            double x = (lon + 180.0) / 360.0 * n;
            double y = (1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * n;
            return (x, y);
        }

        private static (double Lat, double Lon) TileXYToLatLon(double x, double y, int zoom)
        {
            double n = Math.Pow(2.0, zoom);
            double lon = x / n * 360.0 - 180.0;
            double latRad = Math.Atan(Math.Sinh(Math.PI * (1.0 - 2.0 * y / n)));
            double lat = latRad * 180.0 / Math.PI;
            return (lat, lon);
        }

        private static (double Lat, double Lon) ScreenToLatLon(double screenX, double screenY, double viewW, double viewH)
        {
            var (centerTileX, centerTileY) = LatLonToTileXY(_centerLat, _centerLon, _zoom);
            double tileX = centerTileX + (screenX - viewW / 2.0) / 256.0;
            double tileY = centerTileY + (screenY - viewH / 2.0) / 256.0;
            return TileXYToLatLon(tileX, tileY, _zoom);
        }

        private static Point LatLonToScreen(double lat, double lon, double viewW, double viewH)
        {
            var (centerTileX, centerTileY) = LatLonToTileXY(_centerLat, _centerLon, _zoom);
            var (ptTileX, ptTileY) = LatLonToTileXY(lat, lon, _zoom);
            double sx = viewW / 2.0 + (ptTileX - centerTileX) * 256.0;
            double sy = viewH / 2.0 + (ptTileY - centerTileY) * 256.0;
            return new Point(sx, sy);
        }

        private static string GetEpsg3857BBox(int tx, int ty, int zoom)
        {
            var (latNorth, lonWest) = TileXYToLatLon(tx, ty, zoom);
            var (latSouth, lonEast) = TileXYToLatLon(tx + 1, ty + 1, zoom);

            (double mx, double my) ToMercator(double lat, double lon)
            {
                double x = lon * 20037508.34 / 180.0;
                double y = Math.Log(Math.Tan((90.0 + lat) * Math.PI / 360.0)) / (Math.PI / 180.0);
                y = y * 20037508.34 / 180.0;
                return (x, y);
            }

            var (minX, minY) = ToMercator(latSouth, lonWest);
            var (maxX, maxY) = ToMercator(latNorth, lonEast);
            return string.Create(CultureInfo.InvariantCulture, $"{minX:0.00},{minY:0.00},{maxX:0.00},{maxY:0.00}");
        }

        private static string BuildTileUrl(TacticalMapLayerDef layer, int tx, int ty, int zoom)
        {
            if (layer.IsWmsEpsg3857)
            {
                string bbox = GetEpsg3857BBox(tx, ty, zoom);
                return layer.UrlTemplate.Replace("{bbox}", bbox);
            }
            return layer.UrlTemplate
                .Replace("{z}", zoom.ToString(CultureInfo.InvariantCulture))
                .Replace("{x}", tx.ToString(CultureInfo.InvariantCulture))
                .Replace("{y}", ty.ToString(CultureInfo.InvariantCulture));
        }

        private static async Task<byte[]> FetchTileBytesAsync(string url)
        {
            if (TileCache.TryGetValue(url, out var cached)) return cached;
            try
            {
                byte[] data = await Http.GetByteArrayAsync(url);
                if (data != null && data.Length > 100)
                {
                    TileCache[url] = data;
                    return data;
                }
            }
            catch { }
            return null;
        }

        private static async Task RenderMapTilesAsync()
        {
            if (_mapCanvas == null || _activeLayer == null || _fullScreenOverlay?.Visibility != Visibility.Visible) return;

            double w = _mapCanvas.ActualWidth > 100 ? _mapCanvas.ActualWidth : 900;
            double h = _mapCanvas.ActualHeight > 100 ? _mapCanvas.ActualHeight : 650;

            var (centerTileX, centerTileY) = LatLonToTileXY(_centerLat, _centerLon, _zoom);
            int minTx = (int)Math.Floor(centerTileX - (w / 2.0) / 256.0) - 1;
            int maxTx = (int)Math.Floor(centerTileX + (w / 2.0) / 256.0) + 1;
            int minTy = (int)Math.Floor(centerTileY - (h / 2.0) / 256.0) - 1;
            int maxTy = (int)Math.Floor(centerTileY + (h / 2.0) / 256.0) + 1;
            int maxIndex = (1 << _zoom) - 1;

            var tileJobs = new List<(int Tx, int Ty, double ScreenX, double ScreenY, string Url)>();
            for (int tx = minTx; tx <= maxTx; tx++)
            {
                for (int ty = minTy; ty <= maxTy; ty++)
                {
                    if (ty < 0 || ty > maxIndex) continue;
                    int wrappedTx = ((tx % (maxIndex + 1)) + (maxIndex + 1)) % (maxIndex + 1);
                    double sx = (w / 2.0) + (tx - centerTileX) * 256.0;
                    double sy = (h / 2.0) + (ty - centerTileY) * 256.0;
                    string url = BuildTileUrl(_activeLayer, wrappedTx, ty, _zoom);
                    tileJobs.Add((wrappedTx, ty, sx, sy, url));
                }
            }

            var fetchedTiles = await Task.WhenAll(tileJobs.Select(async job =>
            {
                byte[] bytes = await FetchTileBytesAsync(job.Url);
                byte[] hybBytes = null;
                if (_showHybridLabels && !_activeLayer.Id.Equals("google_hyb"))
                {
                    string hybUrl = $"https://mt1.google.com/vt/lyrs=h&x={job.Tx}&y={job.Ty}&z={_zoom}";
                    hybBytes = await FetchTileBytesAsync(hybUrl);
                }
                return (job.ScreenX, job.ScreenY, bytes, hybBytes);
            }));

            _mapCanvas.Children.Clear();

            foreach (var t in fetchedTiles)
            {
                if (t.bytes != null)
                {
                    var img = CreateImageFromBytes(t.bytes);
                    if (img != null)
                    {
                        Canvas.SetLeft(img, t.ScreenX);
                        Canvas.SetTop(img, t.ScreenY);
                        _mapCanvas.Children.Add(img);
                    }
                }
                if (t.hybBytes != null)
                {
                    var hImg = CreateImageFromBytes(t.hybBytes);
                    if (hImg != null)
                    {
                        Canvas.SetLeft(hImg, t.ScreenX);
                        Canvas.SetTop(hImg, t.ScreenY);
                        _mapCanvas.Children.Add(hImg);
                    }
                }
            }

            if (_showGrid) DrawTacticalGridOverlay(w, h);
            DrawTacticalPinsOverlay(w, h);
        }

        private static Image CreateImageFromBytes(byte[] bytes)
        {
            try
            {
                var bmp = new BitmapImage();
                using (var mem = new MemoryStream(bytes))
                {
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = mem;
                    bmp.EndInit();
                    bmp.Freeze();
                }
                return new Image { Source = bmp, Width = 256.5, Height = 256.5, Stretch = Stretch.Fill };
            }
            catch { return null; }
        }

        private static void DrawTacticalGridOverlay(double w, double h)
        {
            var gridBrush = new SolidColorBrush(Color.FromArgb(90, 31, 224, 197));
            gridBrush.Freeze();
            double step = 128.0;

            for (double x = (w / 2.0) % step; x < w; x += step)
            {
                _mapCanvas.Children.Add(new Line { X1 = x, Y1 = 0, X2 = x, Y2 = h, Stroke = gridBrush, StrokeThickness = 0.8 });
                var (lat, lon) = ScreenToLatLon(x, 20, w, h);
                var lbl = new TextBlock { Text = $"{lon:0.000}°E", Foreground = TacticalConfigPanel.BrushAccent, FontSize = 9.5 };
                Canvas.SetLeft(lbl, x + 3);
                Canvas.SetTop(lbl, 48);
                _mapCanvas.Children.Add(lbl);
            }

            for (double y = (h / 2.0) % step; y < h; y += step)
            {
                _mapCanvas.Children.Add(new Line { X1 = 0, Y1 = y, X2 = w, Y2 = y, Stroke = gridBrush, StrokeThickness = 0.8 });
                var (lat, lon) = ScreenToLatLon(20, y, w, h);
                var lbl = new TextBlock { Text = $"{lat:0.000}°N", Foreground = TacticalConfigPanel.BrushAccent, FontSize = 9.5 };
                Canvas.SetLeft(lbl, 8);
                Canvas.SetTop(lbl, y + 2);
                _mapCanvas.Children.Add(lbl);
            }

            // Celownik centralny
            double cx = w / 2.0, cy = h / 2.0;
            _mapCanvas.Children.Add(new Line { X1 = cx - 15, Y1 = cy, X2 = cx + 15, Y2 = cy, Stroke = TacticalConfigPanel.BrushAccent, StrokeThickness = 1.5 });
            _mapCanvas.Children.Add(new Line { X1 = cx, Y1 = cy - 15, X2 = cx, Y2 = cy + 15, Stroke = TacticalConfigPanel.BrushAccent, StrokeThickness = 1.5 });
        }

        private static void DrawTacticalPinsOverlay(double w, double h)
        {
            for (int i = 0; i < _pins.Count; i++)
            {
                var p = _pins[i];
                Point pt = LatLonToScreen(p.Lat, p.Lon, w, h);
                if (pt.X < -50 || pt.X > w + 50 || pt.Y < -50 || pt.Y > h + 50) continue;

                var pinBrush = TacticalConfigPanel.BrushFromHex(p.ColorHex, Color.FromRgb(31, 224, 197));
                var dot = new Ellipse
                {
                    Width = 14,
                    Height = 14,
                    Fill = pinBrush,
                    Stroke = Brushes.White,
                    StrokeThickness = 2
                };
                Canvas.SetLeft(dot, pt.X - 7);
                Canvas.SetTop(dot, pt.Y - 7);
                _mapCanvas.Children.Add(dot);

                var tagBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(220, 8, 19, 22)),
                    BorderBrush = pinBrush,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(5, 2, 5, 2)
                };
                tagBorder.Child = new TextBlock
                {
                    Text = $"[{p.Faction}] {p.Name}",
                    Foreground = Brushes.White,
                    FontSize = 10.5,
                    FontWeight = FontWeights.Bold
                };
                Canvas.SetLeft(tagBorder, pt.X + 10);
                Canvas.SetTop(tagBorder, pt.Y - 10);
                _mapCanvas.Children.Add(tagBorder);
            }
        }

        // =========================================================================
        // EKSPORT MAPY DO PACZKI .FTAK (SOURCE/MAPS + SOURCE/ATAK), KMZ, GPX ORAZ PDF
        // =========================================================================
        private static byte[] CaptureMapCanvasPngBytes()
        {
            int w = (int)Math.Max(800, _mapCanvas.ActualWidth);
            int h = (int)Math.Max(600, _mapCanvas.ActualHeight);
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(_mapCanvas);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return ms.ToArray();
        }

        private static byte[] CaptureMapCanvasJpegBytes()
        {
            int w = (int)Math.Max(800, _mapCanvas.ActualWidth);
            int h = (int)Math.Max(600, _mapCanvas.ActualHeight);
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(_mapCanvas);
            var enc = new JpegBitmapEncoder { QualityLevel = 92 };
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return ms.ToArray();
        }

        private static async Task ExportMapToFtakSourceAsync(Window win)
        {
            try
            {
                string sourceDir = TacticalConfigPanel.GetPrimarySourceDirectory(win);
                string mapsDir = System.IO.Path.Combine(sourceDir, "maps");
                string atakDir = System.IO.Path.Combine(sourceDir, "atak");
                Directory.CreateDirectory(mapsDir);
                Directory.CreateDirectory(atakDir);

                string title = TacticalConfigPanel.SanitizeFileName(_mapTitleBox?.Text ?? "Mapa_AO");
                byte[] pngBytes = CaptureMapCanvasPngBytes();

                double w = Math.Max(800, _mapCanvas.ActualWidth);
                double h = Math.Max(600, _mapCanvas.ActualHeight);
                var (north, west) = ScreenToLatLon(0, 0, w, h);
                var (south, east) = ScreenToLatLon(w, h, w, h);

                string kmzPath = System.IO.Path.Combine(mapsDir, $"{title}.kmz");
                string xmlPath = System.IO.Path.Combine(mapsDir, $"{_activeLayer.Id}_online.xml");
                string gpxPath = System.IO.Path.Combine(atakDir, $"{title}_punkty.gpx");

                await Task.Run(() =>
                {
                    WriteKmzGroundOverlayArchive(kmzPath, title, pngBytes, north, south, east, west);
                    WriteAtakTileSourceXml(xmlPath, _activeLayer);
                    WriteGpxFile(gpxPath, title);
                });

                await TacticalConfigPanel.RefreshSourceSizeFromExternalAsync(win);
                if (_statusText != null)
                {
                    _statusText.Text = $"✓ Zapisano mapę KMZ ({kmzPath}), źródło online XML oraz punkty GPX bezpośrednio do folderu source paczki .ftak!";
                }
            }
            catch (Exception ex)
            {
                if (_statusText != null) _statusText.Text = $"Błąd zapisu do paczki .ftak: {ex.Message}";
            }
        }

        private static async Task ExportStandaloneKmzAndGpxAsync(Window win, bool exportKmz, bool exportGpx)
        {
            try
            {
                string sourceDir = TacticalConfigPanel.GetPrimarySourceDirectory(win);
                string parentDir = System.IO.Path.GetDirectoryName(System.IO.Path.TrimEndingDirectorySeparator(sourceDir)) ?? AppDomain.CurrentDomain.BaseDirectory;
                string outDir = System.IO.Path.Combine(parentDir, "release_maps");
                Directory.CreateDirectory(outDir);

                string title = TacticalConfigPanel.SanitizeFileName(_mapTitleBox?.Text ?? "Mapa_AO");
                byte[] pngBytes = CaptureMapCanvasPngBytes();
                double w = Math.Max(800, _mapCanvas.ActualWidth);
                double h = Math.Max(600, _mapCanvas.ActualHeight);
                var (north, west) = ScreenToLatLon(0, 0, w, h);
                var (south, east) = ScreenToLatLon(w, h, w, h);

                await Task.Run(() =>
                {
                    if (exportKmz) WriteKmzGroundOverlayArchive(System.IO.Path.Combine(outDir, $"{title}.kmz"), title, pngBytes, north, south, east, west);
                    if (exportGpx) WriteGpxFile(System.IO.Path.Combine(outDir, $"{title}.gpx"), title);
                });

                if (_statusText != null) _statusText.Text = $"✓ Wyeksportowano pliki KMZ/GPX do folderu: {outDir}";
                TacticalConfigPanel.TryOpenFolderInExplorer(outDir);
            }
            catch { }
        }

        private static void WriteKmzGroundOverlayArchive(string kmzPath, string title, byte[] mapPngBytes, double north, double south, double east, double west)
        {
            if (File.Exists(kmzPath)) File.Delete(kmzPath);
            using var fs = new FileStream(kmzPath, FileMode.CreateNew, FileAccess.Write);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create, false, Encoding.UTF8);

            var imgEntry = zip.CreateEntry("files/map_overlay.png", CompressionLevel.Optimal);
            using (var s = imgEntry.Open()) s.Write(mapPngBytes, 0, mapPngBytes.Length);

            var sb = new StringBuilder();
            sb.AppendLine(@"<?xml version=""1.0"" encoding=""UTF-8""?>");
            sb.AppendLine(@"<kml xmlns=""http://www.opengis.net/kml/2.2""><Document>");
            sb.AppendLine($"  <name>{title}</name>");
            sb.AppendLine("  <GroundOverlay>");
            sb.AppendLine($"    <name>{title} ({_activeLayer?.Name})</name>");
            sb.AppendLine("    <Icon><href>files/map_overlay.png</href></Icon>");
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"    <LatLonBox><north>{north:0.000000}</north><south>{south:0.000000}</south><east>{east:0.000000}</east><west>{west:0.000000}</west></LatLonBox>"));
            sb.AppendLine("  </GroundOverlay>");

            foreach (var p in _pins)
            {
                sb.AppendLine("  <Placemark>");
                sb.AppendLine($"    <name>[{p.Faction}] {p.Name}</name>");
                sb.AppendLine($"    <description>Typ: {p.Type} | Kolor: {p.ColorHex}</description>");
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"    <Point><coordinates>{p.Lon:0.000000},{p.Lat:0.000000},0</coordinates></Point>"));
                sb.AppendLine("  </Placemark>");
            }
            sb.AppendLine("</Document></kml>");

            var kmlEntry = zip.CreateEntry("doc.kml", CompressionLevel.Optimal);
            using (var sw = new StreamWriter(kmlEntry.Open(), Encoding.UTF8)) sw.Write(sb.ToString());
        }

        private static void WriteAtakTileSourceXml(string xmlPath, TacticalMapLayerDef layer)
        {
            if (layer == null) return;
            string url = layer.UrlTemplate.Replace("&", "&amp;");
            string xml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<customMapSource>
    <name>FTH - {layer.Name}</name>
    <minZoom>5</minZoom>
    <maxZoom>19</maxZoom>
    <tileType>jpg</tileType>
    <tileUpdate>None</tileUpdate>
    <url>{url}</url>
</customMapSource>";
            File.WriteAllText(xmlPath, xml, Encoding.UTF8);
        }

        private static void WriteGpxFile(string gpxPath, string title)
        {
            var sb = new StringBuilder();
            sb.AppendLine(@"<?xml version=""1.0"" encoding=""UTF-8""?>");
            sb.AppendLine(@"<gpx version=""1.1"" creator=""Field TAK Hub Builder 2.4"" xmlns=""http://www.topografix.com/GPX/1/1"">");
            sb.AppendLine($"  <metadata><name>{title}</name><time>{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</time></metadata>");
            foreach (var p in _pins)
            {
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  <wpt lat=\"{p.Lat:0.000000}\" lon=\"{p.Lon:0.000000}\"><name>[{p.Faction}] {p.Name}</name><type>{p.Type}</type></wpt>"));
            }
            sb.AppendLine("</gpx>");
            File.WriteAllText(gpxPath, sb.ToString(), Encoding.UTF8);
        }

        // Natywny generator pliku PDF A4 (osadza strumień JPEG mapy taktycznej z ramką tytułową)
        private static async Task ExportTacticalMapPdfAsync(Window win)
        {
            try
            {
                string sourceDir = TacticalConfigPanel.GetPrimarySourceDirectory(win);
                string parentDir = System.IO.Path.GetDirectoryName(System.IO.Path.TrimEndingDirectorySeparator(sourceDir)) ?? AppDomain.CurrentDomain.BaseDirectory;
                string outDir = System.IO.Path.Combine(parentDir, "release_maps");
                Directory.CreateDirectory(outDir);

                string title = TacticalConfigPanel.SanitizeFileName(_mapTitleBox?.Text ?? "Mapa_AO");
                string pdfPath = System.IO.Path.Combine(outDir, $"{title}_A4.pdf");

                const int pageW = 1240;
                const int pageH = 1754;
                int mapW = (int)Math.Max(800, _mapCanvas.ActualWidth);
                int mapH = (int)Math.Max(600, _mapCanvas.ActualHeight);
                var mapRtb = new RenderTargetBitmap(mapW, mapH, 96, 96, PixelFormats.Pbgra32);
                mapRtb.Render(_mapCanvas);

                var pageVisual = new DrawingVisual();
                using (DrawingContext dc = pageVisual.RenderOpen())
                {
                    dc.DrawRectangle(TacticalConfigPanel.BrushCardBg, null, new Rect(0, 0, pageW, pageH));
                    dc.DrawRoundedRectangle(null, new Pen(TacticalConfigPanel.BrushAccent, 4), new Rect(30, 30, pageW - 60, pageH - 60), 14, 14);
                    TacticalConfigPanel.DrawCenteredText(dc, $"MAPA TAKTYCZNA OPERACJI: {title.ToUpperInvariant()}", 30, Brushes.White, pageW / 2.0, 60, true);
                    TacticalConfigPanel.DrawCenteredText(dc, $"Podkład: {_activeLayer?.Name}   |   Środek WGS84: {_centerLat:0.00000}, {_centerLon:0.00000}   |   Zoom: {_zoom}", 18, TacticalConfigPanel.BrushAccent, pageW / 2.0, 105, false);

                    dc.DrawRectangle(Brushes.White, new Pen(TacticalConfigPanel.BrushAccent, 3), new Rect(68, 150, pageW - 136, 1340));
                    dc.DrawImage(mapRtb, new Rect(70, 152, pageW - 140, 1336));

                    TacticalConfigPanel.DrawLeftText(dc, $"Punktów taktycznych na mapie: {_pins.Count}   |   Wygenerowano w Field TAK Hub Builder 2.4 ({DateTime.Now:yyyy-MM-dd HH:mm})", 17, TacticalConfigPanel.BrushMutedText, 70, 1525, false);
                }

                var fullRtb = new RenderTargetBitmap(pageW, pageH, 96, 96, PixelFormats.Pbgra32);
                fullRtb.Render(pageVisual);
                var jpgEnc = new JpegBitmapEncoder { QualityLevel = 92 };
                jpgEnc.Frames.Add(BitmapFrame.Create(fullRtb));
                using var jpgMs = new MemoryStream();
                jpgEnc.Save(jpgMs);
                byte[] jpegBytes = jpgMs.ToArray();

                await Task.Run(() => WriteSingleImagePdf(pdfPath, jpegBytes, pageW, pageH));

                if (_statusText != null) _statusText.Text = $"✓ Zapisano gotowy do druku plik PDF A4: {pdfPath}";
                TacticalConfigPanel.TryOpenFolderInExplorer(outDir);
            }
            catch (Exception ex)
            {
                if (_statusText != null) _statusText.Text = $"Błąd zapisu PDF: {ex.Message}";
            }
        }

        private static void WriteSingleImagePdf(string pdfPath, byte[] jpegBytes, int pixelW, int pixelH)
        {
            using var fs = new FileStream(pdfPath, FileMode.Create, FileAccess.Write);
            void WriteAscii(string s) { byte[] b = Encoding.ASCII.GetBytes(s); fs.Write(b, 0, b.Length); }

            WriteAscii("%PDF-1.4\n");
            long obj1 = fs.Position;
            WriteAscii("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
            long obj2 = fs.Position;
            WriteAscii("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
            long obj3 = fs.Position;
            WriteAscii("3 0 obj\n<< /Type /Page /Parent 2 0 R /Resources << /XObject << /Im0 4 0 R >> >> /MediaBox [0 0 595 842] /Contents 5 0 R >>\nendobj\n");
            long obj4 = fs.Position;
            WriteAscii($"4 0 obj\n<< /Type /XObject /Subtype /Image /Width {pixelW} /Height {pixelH} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpegBytes.Length} >>\nstream\n");
            fs.Write(jpegBytes, 0, jpegBytes.Length);
            WriteAscii("\nendstream\nendobj\n");

            string contentCmd = "q\n595 0 0 842 0 0 cm\n/Im0 Do\nQ\n";
            long obj5 = fs.Position;
            WriteAscii($"5 0 obj\n<< /Length {contentCmd.Length} >>\nstream\n{contentCmd}endstream\nendobj\n");

            long xrefPos = fs.Position;
            WriteAscii("xref\n0 6\n0000000000 65535 f \n");
            WriteAscii($"{obj1:D10} 00000 n \n");
            WriteAscii($"{obj2:D10} 00000 n \n");
            WriteAscii($"{obj3:D10} 00000 n \n");
            WriteAscii($"{obj4:D10} 00000 n \n");
            WriteAscii($"{obj5:D10} 00000 n \n");
            WriteAscii($"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xrefPos}\n%%EOF\n");
        }

        private static void PrintTacticalMap(Window win)
        {
            try
            {
                var dlg = new PrintDialog();
                if (dlg.ShowDialog() == true && _mapCanvas != null)
                {
                    dlg.PrintVisual(_mapCanvas, _mapTitleBox?.Text ?? "Mapa Taktyczna Field TAK Hub");
                }
            }
            catch { }
        }
    }
}
