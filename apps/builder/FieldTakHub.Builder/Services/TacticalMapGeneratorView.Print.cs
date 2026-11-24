using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FieldTakHub.Builder.Services.MapImagery;
using FieldTakHub.Builder.Services.MapPrint;
using Microsoft.Win32;

namespace FieldTakHub.Builder.Services
{
    /// <summary>
    /// Wydruk z ramką kartograficzną (styl arkuszy T.REX 1:25 000): okno ustawień z podglądem, dopasowanie arkusza
    /// w edytorze do obszaru mapy, pobranie podkładu w rozdzielczości wydruku, przechwycenie obiektów z edytora
    /// i zapis jedno- lub wielostronicowego PDF.
    /// </summary>
    public static partial class TacticalMapGeneratorView
    {
        private const string PrintSheetFormatPrefix = "Wydruk z ramką";
        private const string PrintLogoPackUri = "pack://application:,,,/Assets/field-tak-hub-2.3-icon.png";

        private static FramedPrintDialog _framedPrintWindow;
        private static SheetFormatDef _printSheetFormat;

        /// <summary>Stan edytora potrzebny do przeliczenia ekranu na stronę (zapamiętany na początku wydruku).</summary>
        private sealed record PrintBasis(double CanvasW, double CanvasH, int Zoom, double CenterLat, double CenterLon, int Scale, double PxPerMm, Int32Rect MapPx, double MetersPerPrintPx);

        private sealed record PrintPagePlan(int Index, int Count, double CenterLat, double CenterLon, MercatorViewport View, string Note);

        private static void ShowFramedPrintDialog(Window owner)
        {
            if (_framedPrintWindow != null)
            {
                _framedPrintWindow.Activate();
                return;
            }
            var dialog = new FramedPrintDialog(owner, MapPrintSettings.Load());
            _framedPrintWindow = dialog;
            dialog.Show();
        }

        /// <summary>Ustawia arkusz edytora na obszar mapy wydruku (ekran = papier). Wywołanie bez zmian niczego nie przerysowuje.</summary>
        private static void EnsurePrintSheetApplied(MapSheetLayout layout)
        {
            if (_sheetFrameBorder == null) return;
            double w = Math.Round(layout.MapRect.Width, 1), h = Math.Round(layout.MapRect.Height, 1);
            string orientation = h > w ? "Pionowo" : "Poziomo";
            double longSide = Math.Max(w, h), shortSide = Math.Min(w, h);
            if (_printSheetFormat != null && ReferenceEquals(_selectedSheet, _printSheetFormat) && _sheetOrientation == orientation &&
                Math.Abs(_printSheetFormat.WidthMm - longSide) < 0.05 && Math.Abs(_printSheetFormat.HeightMm - shortSide) < 0.05)
                return;

            if (_printSheetFormat == null)
            {
                _printSheetFormat = new SheetFormatDef();
                SheetFormats.Add(_printSheetFormat);
            }
            _printSheetFormat.Name = string.Format(CultureInfo.InvariantCulture, "{0} — obszar mapy {1:0.#} × {2:0.#} mm", PrintSheetFormatPrefix, w, h);
            _printSheetFormat.WidthMm = longSide;
            _printSheetFormat.HeightMm = shortSide;
            _selectedSheet = _printSheetFormat;
            _sheetOrientation = orientation;

            bool wasRebuilding = _isRebuildingMapControls;
            _isRebuildingMapControls = true;
            try
            {
                if (_fmtComboBox != null) { _fmtComboBox.Items.Refresh(); _fmtComboBox.SelectedItem = _printSheetFormat; }
                if (_oriComboBox != null) _oriComboBox.SelectedItem = orientation;
            }
            finally { _isRebuildingMapControls = wasRebuilding; }

            UpdateSheetViewportSize();
            _sheetFrameBorder.UpdateLayout();
            ApplyScaleDenominator(_targetScaleDenominator);
        }

        private static int EffectivePrintDpi(MapSheetLayout layout, int requestedDpi)
        {
            double squareInches = layout.PageWidthMm / 25.4 * (layout.PageHeightMm / 25.4);
            int maxDpi = (int)Math.Floor(Math.Sqrt(60_000_000.0 / Math.Max(1.0, squareInches)));
            return Math.Clamp(Math.Min(requestedDpi, maxDpi), 30, 600);
        }

        private static PrintBasis CapturePrintBasis(MapSheetLayout layout, int dpi)
        {
            _sheetFrameBorder?.UpdateLayout();
            var (canvasW, canvasH) = GetExactCanvasSize();
            double pxPerMm = dpi / 25.4;
            Int32Rect mapPx = MapFrameRenderer.MapPixelRect(layout, pxPerMm);
            double metersPerPrintPx = layout.MapRect.Width / 1000.0 * _targetScaleDenominator / mapPx.Width;
            return new PrintBasis(canvasW, canvasH, _zoom, _centerLat, _centerLon, _targetScaleDenominator, pxPerMm, mapPx, metersPerPrintPx);
        }

        /// <summary>Pikseli strony na kafelek poziomu <c>Zoom</c> przy danej szerokości geograficznej — ten sam wzór co na ekranie.</summary>
        private static double PrintTilePx(PrintBasis b, double lat) =>
            256.0 * (156543.03392 * Math.Cos(lat * Math.PI / 180.0) / Math.Pow(2.0, b.Zoom)) / b.MetersPerPrintPx;

        /// <summary>Arkusze serii: środkowy = widok edytora, sąsiednie przesunięte o rozmiar obszaru mapy pomniejszony o zakładkę.</summary>
        private static List<PrintPagePlan> PlanPrintPages(PrintBasis b, MapPrintSettings s, bool centerOnly)
        {
            var texts = MapFrameTexts.For(s.PolishLabels);
            var center = MercatorViewport.FromCenter(b.CenterLat, b.CenterLon, b.Zoom, PrintTilePx(b, b.CenterLat), b.MapPx.Width, b.MapPx.Height);
            int cols = centerOnly ? 1 : Math.Clamp(s.SeriesColumns, 1, 6), rows = centerOnly ? 1 : Math.Clamp(s.SeriesRows, 1, 6);
            double keep = 1.0 - Math.Clamp(s.SeriesOverlapPercent, 0.0, 30.0) / 100.0;
            int count = cols * rows;
            var pages = new List<PrintPagePlan>(count);
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    double dx = (c - (cols - 1) / 2.0) * b.MapPx.Width * keep;
                    double dy = (r - (rows - 1) / 2.0) * b.MapPx.Height * keep;
                    var (lat, lon) = center.Shifted(dx, dy).Center;
                    var view = MercatorViewport.FromCenter(lat, lon, b.Zoom, PrintTilePx(b, lat), b.MapPx.Width, b.MapPx.Height);
                    int index = r * cols + c + 1;
                    string note = count > 1 ? string.Format(CultureInfo.InvariantCulture, texts.SheetOfFormat, index, count) : "";
                    pages.Add(new PrintPagePlan(index, count, lat, lon, view, note));
                }
            }
            return pages;
        }

        /// <summary>Przesuwa mapę tak, by lewy dolny róg obszaru mapy wypadł na przecięciu linii siatki (jak na arkuszach T.REX).</summary>
        private static void SnapPrintSheetToGrid(MapPrintSettings s)
        {
            var layout = MapSheetLayout.Compute(s);
            EnsurePrintSheetApplied(layout);
            var basis = CapturePrintBasis(layout, 100);
            var view = PlanPrintPages(basis, s, centerOnly: true)[0].View;
            int zone = MapGridMath.UtmZone(view.Center.Lon);
            var (swLat, swLon) = view.ToLatLon(0.0, view.Height);
            UtmGrid2DProjector.LatLonToUtm(swLat, swLon, zone, out double e, out double n);
            double step = s.GridIntervalMeters > 0 ? s.GridIntervalMeters : MapGridMath.AutoGridInterval(basis.Scale);
            UtmGrid2DProjector.UtmToLatLon(Math.Round(e / step) * step, Math.Round(n / step) * step, zone, out double targetLat, out double targetLon);
            var (px, py) = view.ToPixel(targetLat, targetLon);
            var (newLat, newLon) = view.Shifted(px, py - view.Height).Center;
            _centerLat = newLat;
            _centerLon = newLon;
            ApplyScaleDenominator(_targetScaleDenominator);
        }

        /// <summary>
        /// Obiekty z edytora dla arkusza: na chwilę ustawia środek mapy arkusza, ukrywa podkład i (opcjonalnie) siatki
        /// oraz elementy HUD, renderuje warstwy wektorowe w rozdzielczości wydruku i przywraca stan. Całość synchronicznie.
        /// </summary>
        private static BitmapSource CaptureEditorOverlayForPrint(PrintBasis b, PrintPagePlan page, MapPrintSettings s)
        {
            if (!s.IncludeMapObjects || _mapCompositeGrid == null || _tileCanvas == null || _dimmerRect == null || b.CanvasW < 2 || b.CanvasH < 2) return null;
            double k = b.MapPx.Width / b.CanvasW;

            double savedLat = _centerLat, savedLon = _centerLon, savedMainOpacity = _mainGridOpacity, savedSubOpacity = _subGridOpacity;
            string savedCoordSystem = _coordSystem;
            bool savedNorth = _showNorthArrow, savedScaleBar = _showScaleBar, savedLegend = _showLegend, savedClean = _isExportingCleanMap;
            var savedRulerStart = _rulerStart;
            var savedRulerEnd = _rulerEnd;
            double savedTileOpacity = _tileCanvas.Opacity, savedLidarOpacity = _lidarTileCanvas?.Opacity ?? 1.0, savedDimmerOpacity = _dimmerRect.Opacity;
            Brush savedBackground = _mapCompositeGrid.Background;
            try
            {
                _centerLat = page.CenterLat;
                _centerLon = page.CenterLon;
                if (!s.IncludeScreenGrids)
                {
                    _mainGridOpacity = 0.0;
                    _subGridOpacity = 0.0;
                    if (IsWgs84ModeActive()) _coordSystem = "Brak siatki";
                }
                _showNorthArrow = s.IncludeScreenNorthArrow;
                _showScaleBar = s.IncludeScreenScaleBar;
                _showLegend = s.IncludeScreenLegend;
                _rulerStart = null;
                _rulerEnd = null;
                _isExportingCleanMap = true;
                _tileCanvas.Opacity = 0.0;
                if (_lidarTileCanvas != null) _lidarTileCanvas.Opacity = 0.0;
                _dimmerRect.Opacity = 0.0;
                _mapCompositeGrid.Background = Brushes.Transparent;

                RedrawVectorsOnly();
                _mapCompositeGrid.UpdateLayout();
                return RenderElementAtScale(_mapCompositeGrid, b.CanvasW, b.CanvasH, k);
            }
            finally
            {
                _centerLat = savedLat;
                _centerLon = savedLon;
                _mainGridOpacity = savedMainOpacity;
                _subGridOpacity = savedSubOpacity;
                _coordSystem = savedCoordSystem;
                _showNorthArrow = savedNorth;
                _showScaleBar = savedScaleBar;
                _showLegend = savedLegend;
                _rulerStart = savedRulerStart;
                _rulerEnd = savedRulerEnd;
                _isExportingCleanMap = savedClean;
                _tileCanvas.Opacity = savedTileOpacity;
                if (_lidarTileCanvas != null) _lidarTileCanvas.Opacity = savedLidarOpacity;
                _dimmerRect.Opacity = savedDimmerOpacity;
                _mapCompositeGrid.Background = savedBackground;
                RedrawVectorsOnly();
            }
        }

        /// <summary>
        /// Render elementu w skali <paramref name="k"/> (DPI 96·k — wektory pozostają ostre). RenderTargetBitmap uwzględnia
        /// przesunięcie elementu w rodzicu (tu: grubość ramki arkusza), dlatego bufor jest powiększony i wycinany.
        /// </summary>
        private static BitmapSource RenderElementAtScale(FrameworkElement element, double width, double height, double k)
        {
            Vector offset = VisualTreeHelper.GetOffset(element);
            double ox = Math.Max(0.0, offset.X), oy = Math.Max(0.0, offset.Y);
            int fullW = Math.Max(1, (int)Math.Ceiling((width + ox) * k)), fullH = Math.Max(1, (int)Math.Ceiling((height + oy) * k));
            var rtb = new RenderTargetBitmap(fullW, fullH, 96.0 * k, 96.0 * k, PixelFormats.Pbgra32);
            rtb.Render(element);
            int cx = Math.Clamp((int)Math.Round(ox * k), 0, fullW - 1), cy = Math.Clamp((int)Math.Round(oy * k), 0, fullH - 1);
            int cw = Math.Clamp((int)Math.Round(width * k), 1, fullW - cx), ch = Math.Clamp((int)Math.Round(height * k), 1, fullH - cy);
            var crop = new CroppedBitmap(rtb, new Int32Rect(cx, cy, cw, ch));
            crop.Freeze();
            return crop;
        }

        private static async Task<(BitmapSource Page, MapSheetFacts Facts)> RenderFramedPageAsync(
            MapPrintSettings s, MapSheetLayout layout, PrintBasis basis, PrintPagePlan page, bool preview, IProgress<string> progress, CancellationToken ct)
        {
            var imagery = await RenderPrintImageryAsync(page.View, preview, progress, ct);
            ct.ThrowIfCancellationRequested();
            var objects = CaptureEditorOverlayForPrint(basis, page, s);
            var facts = MapSheetFacts.Compute(page.View, basis.Scale, s.GridIntervalMeters, DateTime.Now);
            var texts = MapFrameTexts.For(s.PolishLabels);
            bool customTitle = s.TitleMode == MapPrintSettings.TitleModes[1] && !string.IsNullOrWhiteSpace(s.CustomTitle);
            var frame = new MapFramePage
            {
                Settings = s,
                Layout = layout,
                Facts = facts,
                PxPerMm = basis.PxPerMm,
                MapView = page.View,
                Imagery = imagery,
                Objects = objects,
                Logo = LoadPrintLogo(s),
                Title = customTitle ? s.CustomTitle.Trim() : facts.SheetId,
                Subtitle = ResolvePrintSubtitle(s),
                BasemapName = ActiveBasemapShortName(),
                LeftMarginText = string.IsNullOrWhiteSpace(s.LeftMarginText) ? BuildPrintAttribution(texts) : s.LeftMarginText.Trim(),
                RightMarginText = string.IsNullOrWhiteSpace(s.RightMarginText) ? texts.DefaultRightNote : s.RightMarginText.Trim(),
                PageNote = page.Note,
                QrPayload = facts.QrPayload(s.QrMode, s.QrCustomText)
            };
            return (MapFrameRenderer.Render(frame), facts);
        }

        private static ImageSource LoadPrintLogo(MapPrintSettings s)
        {
            Uri uri = null;
            if (s.LogoMode == MapPrintSettings.LogoModes[0]) uri = new Uri(PrintLogoPackUri, UriKind.Absolute);
            else if (s.LogoMode == MapPrintSettings.LogoModes[1] && !string.IsNullOrWhiteSpace(s.LogoFilePath) && File.Exists(s.LogoFilePath))
                uri = new Uri(Path.GetFullPath(s.LogoFilePath), UriKind.Absolute);
            if (uri == null) return null;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = uri;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch (Exception ex) when (ex is IOException || ex is NotSupportedException || ex is UriFormatException || ex is ArgumentException || ex is InvalidOperationException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static string ResolvePrintSubtitle(MapPrintSettings s)
        {
            if (!string.IsNullOrWhiteSpace(s.Subtitle)) return s.Subtitle.Trim();
            if (string.IsNullOrWhiteSpace(_savedSearchQuery)) return "";
            var culture = s.PolishLabels ? CultureInfo.GetCultureInfo("pl-PL") : CultureInfo.InvariantCulture;
            return (s.PolishLabels ? "REJON: " : "AREA: ") + _savedSearchQuery.Trim().ToUpper(culture);
        }

        private static string ActiveBasemapShortName()
        {
            if (_activeLayer == null) return "";
            string name = string.IsNullOrWhiteSpace(_activeLayer.ShortName) ? _activeLayer.Name : _activeLayer.ShortName;
            return _showLidarSingleWms && !_activeLayer.IsViewMosaic ? name + " + LiDAR" : name;
        }

        private static string BuildPrintAttribution(MapFrameTexts texts)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(_activeLayer?.Attribution)) parts.Add(_activeLayer.Attribution);
            if (_showLidarSingleWms && _activeLayer != null && !_activeLayer.IsViewMosaic)
            {
                string lidar = LayerById("geoportal_lidar")?.Attribution;
                if (!string.IsNullOrWhiteSpace(lidar)) parts.Add(lidar);
            }
            parts.Add(texts.GridAndModelCredit);
            parts.Add("Field TAK Hub Builder");
            return string.Join(" · ", parts);
        }

        /// <summary>Najbliższa miejscowość z OpenStreetMap (Nominatim) jako podtytuł: „W POBLIŻU X” albo „12 KM NA PD OD X”.</summary>
        private static async Task<string> ReverseGeocodeSubtitleAsync(double lat, double lon, bool polish, CancellationToken ct)
        {
            string url = string.Create(CultureInfo.InvariantCulture,
                $"https://nominatim.openstreetmap.org/reverse?format=jsonv2&zoom=10&accept-language={(polish ? "pl" : "en")}&lat={lat:0.000000}&lon={lon:0.000000}");
            using var reqCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            reqCts.CancelAfter(10000);
            var node = JsonNode.Parse(await Http.GetStringAsync(url, reqCts.Token));
            string name = node?["name"]?.ToString();
            if (string.IsNullOrWhiteSpace(name))
            {
                var address = node?["address"];
                name = address?["city"]?.ToString() ?? address?["town"]?.ToString() ?? address?["village"]?.ToString() ?? address?["municipality"]?.ToString();
            }
            if (string.IsNullOrWhiteSpace(name)) return "";

            var texts = MapFrameTexts.For(polish);
            var culture = polish ? CultureInfo.GetCultureInfo("pl-PL") : CultureInfo.InvariantCulture;
            string upper = name.Trim().ToUpper(culture);
            if (!double.TryParse(node?["lat"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double placeLat) ||
                !double.TryParse(node?["lon"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double placeLon))
                return string.Format(CultureInfo.InvariantCulture, texts.NearFormat, upper);

            double km = MapGridMath.HaversineMeters(placeLat, placeLon, lat, lon) / 1000.0;
            if (km < 2.0) return string.Format(CultureInfo.InvariantCulture, texts.NearFormat, upper);
            string direction = MapGridMath.CompassPoint(MapGridMath.InitialBearingDeg(placeLat, placeLon, lat, lon), polish);
            return string.Format(CultureInfo.InvariantCulture, texts.DistanceFormat, Math.Round(km), direction, upper);
        }

        // ================================================================== okno ustawień

        private sealed class FramedPrintDialog : Window
        {
            private static readonly string[] IntervalNames = { "Automatycznie", "100 m", "200 m", "500 m", "1 km", "2 km", "5 km", "10 km" };
            private static readonly string[] SeriesCounts = { "1", "2", "3", "4", "5", "6" };

            private readonly MapPrintSettings _s;
            private readonly Image _preview = new Image { Stretch = Stretch.Uniform };
            private readonly TextBlock _status = new TextBlock { Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 10.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
            private readonly TextBlock _areaInfo = new TextBlock { Foreground = TacticalConfigPanel.BrushAccent, FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 6) };
            private readonly TextBlock _seriesInfo = new TextBlock { Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 9.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            private readonly DispatcherTimer _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            private readonly List<Button> _idleOnlyButtons = new List<Button>();
            private Button _cancelButton;
            private ComboBox _logoModeCombo;
            private TextBlock _logoFileLabel;
            private TextBox _subtitleBox;
            private CancellationTokenSource _previewCts, _exportCts;
            private bool _isExporting;

            public FramedPrintDialog(Window owner, MapPrintSettings settings)
            {
                _s = settings;
                Owner = owner;
                Title = "Wydruk z ramką kartograficzną — PDF";
                Width = 1260;
                Height = 880;
                MinWidth = 960;
                MinHeight = 600;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
                ShowInTaskbar = false;
                Background = TacticalConfigPanel.BrushCardBg;
                Foreground = Brushes.White;

                var root = new Grid { Margin = new Thickness(12) };
                root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(480) });
                root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var left = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
                var scroll = new ScrollViewer { Content = left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
                Grid.SetColumn(scroll, 0);
                root.Children.Add(scroll);
                BuildSettingsPanel(left);

                var right = new DockPanel();
                Grid.SetColumn(right, 1);
                root.Children.Add(right);

                var bottom = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
                var buttons = new UniformGrid { Columns = 4 };
                var refresh = MakeActionButton("🔄 Odśwież podgląd", false, async () => await RefreshPreviewAsync());
                var export = MakeActionButton("💾 Zapisz PDF…", true, async () => await ExportAsync());
                _cancelButton = MakeActionButton("✖ Przerwij zapis", false, () => _exportCts?.Cancel());
                var close = MakeActionButton("Zamknij", false, Close);
                foreach (var b in new[] { refresh, export, _cancelButton, close })
                {
                    b.Height = 32;
                    b.Margin = new Thickness(3, 0, 3, 0);
                    buttons.Children.Add(b);
                }
                _cancelButton.IsEnabled = false;
                _idleOnlyButtons.Add(refresh);
                _idleOnlyButtons.Add(export);
                bottom.Children.Add(buttons);
                bottom.Children.Add(_status);
                DockPanel.SetDock(bottom, Dock.Bottom);
                right.Children.Add(bottom);

                right.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(52, 56, 58)),
                    BorderBrush = TacticalConfigPanel.BrushTileBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(14),
                    Child = _preview
                });
                Content = root;

                _debounce.Tick += async (_, __) =>
                {
                    _debounce.Stop();
                    await RefreshPreviewAsync();
                };
                Loaded += async (_, __) =>
                {
                    ApplyLayout();
                    await RefreshPreviewAsync();
                };
                Closed += (_, __) =>
                {
                    _debounce.Stop();
                    _previewCts?.Cancel();
                    _exportCts?.Cancel();
                    string error = _s.Save();
                    if (error != null && _statusText != null) _statusText.Text = "⚠️ Nie zapisano ustawień wydruku: " + error;
                    _framedPrintWindow = null;
                };
            }

            // ---------------------------------------------------------- budowa panelu

            private void BuildSettingsPanel(StackPanel left)
            {
                var s = _s;
                left.Children.Add(new TextBlock
                {
                    Text = "Ramka, siatka i stopka jak na arkuszach taktycznych 1:25 000. Obszar mapy wydruku jest od razu ustawiany jako arkusz w edytorze — przesuwaj mapę i obiekty w edytorze, a podgląd odświeży się po zmianie ustawień albo przyciskiem.",
                    Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
                });

                var page = CreateSectionCard("1. STRONA I ARKUSZ");
                page.Children.Add(Row(2,
                    TacticalConfigPanel.FieldBox("Papier:", Combo(MapPrintSettings.PaperSizes, s.PaperSize, v => s.PaperSize = v, layout: true)),
                    TacticalConfigPanel.FieldBox("Orientacja:", Combo(new[] { "Pionowa", "Pozioma" }, s.Landscape ? "Pozioma" : "Pionowa", v => s.Landscape = v == "Pozioma", layout: true))));
                page.Children.Add(Row(2,
                    TacticalConfigPanel.FieldBox("Własny papier — szerokość (mm):", Number(s.CustomPaperWidthMm, v => s.CustomPaperWidthMm = v, layout: true)),
                    TacticalConfigPanel.FieldBox("Własny papier — wysokość (mm):", Number(s.CustomPaperHeightMm, v => s.CustomPaperHeightMm = v, layout: true))));
                page.Children.Add(Row(4,
                    TacticalConfigPanel.FieldBox("Margines lewy:", Number(s.MarginLeftMm, v => s.MarginLeftMm = v, layout: true)),
                    TacticalConfigPanel.FieldBox("prawy:", Number(s.MarginRightMm, v => s.MarginRightMm = v, layout: true)),
                    TacticalConfigPanel.FieldBox("górny:", Number(s.MarginTopMm, v => s.MarginTopMm = v, layout: true)),
                    TacticalConfigPanel.FieldBox("dolny (mm):", Number(s.MarginBottomMm, v => s.MarginBottomMm = v, layout: true))));
                page.Children.Add(Row(2,
                    Check("Stopka z opisem arkusza", s.ShowFooter, v => s.ShowFooter = v, layout: true),
                    TacticalConfigPanel.FieldBox("Wysokość stopki (mm):", Number(s.FooterHeightMm, v => s.FooterHeightMm = v, layout: true))));
                page.Children.Add(Row(3,
                    TacticalConfigPanel.FieldBox("Rozdzielczość PDF:", Combo(new[] { "150 DPI", "200 DPI", "300 DPI" }, s.Dpi.ToString(CultureInfo.InvariantCulture) + " DPI", v => s.Dpi = int.Parse(v.Split(' ')[0], CultureInfo.InvariantCulture))),
                    TacticalConfigPanel.FieldBox("Jakość JPEG:", Combo(new[] { "80", "85", "90", "95" }, s.JpegQuality.ToString(CultureInfo.InvariantCulture), v => s.JpegQuality = int.Parse(v, CultureInfo.InvariantCulture))),
                    TacticalConfigPanel.FieldBox("Język opisów:", Combo(new[] { "Polski", "English" }, s.PolishLabels ? "Polski" : "English", v => s.PolishLabels = v == "Polski"))));
                page.Children.Add(_areaInfo);
                var snap = MakeActionButton("🎯 Wyrównaj róg SW arkusza do przecięcia linii siatki", false, () => { SnapPrintSheetToGrid(_s); Changed(false); });
                snap.Height = 27;
                page.Children.Add(snap);
                left.Children.Add(WrapCard(page));

                var grid = CreateSectionCard("2. RAMKA I SIATKA KARTOGRAFICZNA UTM/MGRS");
                grid.Children.Add(Row(2,
                    Check("Linia ramki", s.DrawNeatLine, v => s.DrawNeatLine = v),
                    Check("Siatka UTM/MGRS", s.DrawGrid, v => s.DrawGrid = v)));
                grid.Children.Add(Row(2,
                    Check("Kreski co 1/10 oczka na liniach", s.GridSubdivisionTicks, v => s.GridSubdivisionTicks = v),
                    Check("Podziałka na linii ramki", s.EdgeTicks, v => s.EdgeTicks = v)));
                grid.Children.Add(Row(2,
                    Check("Opisy linii wewnątrz ramki", s.GridLabelsInside, v => s.GridLabelsInside = v),
                    Check("Opisy linii na marginesie", s.GridLabelsOutside, v => s.GridLabelsOutside = v)));
                int intervalIndex = Math.Max(0, Array.IndexOf(MapPrintSettings.GridIntervals, s.GridIntervalMeters));
                grid.Children.Add(Row(2,
                    TacticalConfigPanel.FieldBox("Oczko siatki:", Combo(IntervalNames, IntervalNames[intervalIndex], v => s.GridIntervalMeters = MapPrintSettings.GridIntervals[Math.Max(0, Array.IndexOf(IntervalNames, v))])),
                    TacticalConfigPanel.FieldBox("Opisy linii:", Combo(MapPrintSettings.GridLabelStyles, s.GridLabelStyle, v => s.GridLabelStyle = v))));
                grid.Children.Add(Row(3,
                    TacticalConfigPanel.FieldBox("Kolor siatki (#RRGGBB):", Input(s.GridColorHex, v => s.GridColorHex = v)),
                    TacticalConfigPanel.FieldBox("Grubość linii (mm):", Number(s.GridLineWidthMm, v => s.GridLineWidthMm = v)),
                    TacticalConfigPanel.FieldBox("Wielkość opisów (mm):", Number(s.GridLabelSizeMm, v => s.GridLabelSizeMm = v))));
                left.Children.Add(WrapCard(grid));

                var footer = CreateSectionCard("3. STOPKA: TYTUŁ, POLA, PODZIAŁKI, PT/PM, QR");
                _logoModeCombo = Combo(MapPrintSettings.LogoModes, s.LogoMode, v => s.LogoMode = v);
                _logoFileLabel = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(s.LogoFilePath) ? "(nie wybrano pliku)" : Path.GetFileName(s.LogoFilePath),
                    Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 9.5, TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0)
                };
                var pickLogo = MakeActionButton("Wybierz plik…", false, PickLogoFile);
                pickLogo.Height = 25;
                pickLogo.Padding = new Thickness(6, 0, 6, 0);
                var logoRow = new DockPanel();
                DockPanel.SetDock(pickLogo, Dock.Left);
                logoRow.Children.Add(pickLogo);
                logoRow.Children.Add(_logoFileLabel);
                footer.Children.Add(Row(2, TacticalConfigPanel.FieldBox("Logo:", _logoModeCombo), TacticalConfigPanel.FieldBox("Własne logo (PNG/JPG):", logoRow)));
                footer.Children.Add(Row(2,
                    TacticalConfigPanel.FieldBox("Tytuł arkusza:", Combo(MapPrintSettings.TitleModes, s.TitleMode, v => s.TitleMode = v)),
                    TacticalConfigPanel.FieldBox("Własny tytuł:", Input(s.CustomTitle, v => s.CustomTitle = v))));
                _subtitleBox = Input(s.Subtitle, v => s.Subtitle = v);
                var osm = MakeActionButton("📍 Z OSM", false, async () => await FillSubtitleFromOsmAsync());
                osm.Height = 25;
                osm.Padding = new Thickness(6, 0, 6, 0);
                osm.Margin = new Thickness(4, 0, 0, 0);
                osm.ToolTip = "Najbliższa miejscowość z OpenStreetMap (Nominatim) — np. „12 KM NA PD OD …”";
                var subtitleRow = new DockPanel();
                DockPanel.SetDock(osm, Dock.Right);
                subtitleRow.Children.Add(osm);
                subtitleRow.Children.Add(_subtitleBox);
                footer.Children.Add(TacticalConfigPanel.FieldBox("Podtytuł (puste = „REJON:” + nazwa z wyszukiwarki):", subtitleRow));
                footer.Children.Add(TacticalConfigPanel.FieldBox("Adres / strona pod tytułem (opcjonalnie):", Input(s.UrlLine, v => s.UrlLine = v)));
                footer.Children.Add(Row(2,
                    Check("Linia „Deklinacja WMM2025 · data”", s.ShowGeneratedLine, v => s.ShowGeneratedLine = v),
                    Check("Pole SKALA", s.ShowInfoScale, v => s.ShowInfoScale = v)));
                footer.Children.Add(Row(2,
                    Check("Pole UKŁAD (WGS84)", s.ShowInfoCrs, v => s.ShowInfoCrs = v),
                    Check("Pola narożnika SW", s.ShowInfoSouthWest, v => s.ShowInfoSouthWest = v)));
                footer.Children.Add(Row(2,
                    TacticalConfigPanel.FieldBox("Pole 1 — etykieta:", Input(s.Custom1Label, v => s.Custom1Label = v)),
                    TacticalConfigPanel.FieldBox("Pole 1 — wartość (puste = podkład):", Input(s.Custom1Value, v => s.Custom1Value = v))));
                footer.Children.Add(Row(2,
                    TacticalConfigPanel.FieldBox("Pole 2 — etykieta (np. POZIOMICE):", Input(s.Custom2Label, v => s.Custom2Label = v)),
                    TacticalConfigPanel.FieldBox("Pole 2 — wartość (np. 10 m):", Input(s.Custom2Value, v => s.Custom2Value = v))));
                footer.Children.Add(Row(2,
                    Check("Podziałka w km", s.ShowKmBar, v => s.ShowKmBar = v),
                    Check("Podziałka w milach", s.ShowMileBar, v => s.ShowMileBar = v)));
                footer.Children.Add(Row(2,
                    Check("Notka kontrolna długości podziałki", s.ShowScaleCheckNote, v => s.ShowScaleCheckNote = v),
                    Check("Kąt PT–PM i diagram (WMM2025)", s.ShowDeclination, v => s.ShowDeclination = v)));
                footer.Children.Add(Row(2,
                    TacticalConfigPanel.FieldBox("Kod QR:", Combo(MapPrintSettings.QrModes, s.QrMode, v => s.QrMode = v)),
                    TacticalConfigPanel.FieldBox("Własny tekst QR:", Input(s.QrCustomText, v => s.QrCustomText = v))));
                left.Children.Add(WrapCard(footer));

                var margins = CreateSectionCard("4. PIONOWE OPISY NA MARGINESACH");
                margins.Children.Add(Check("Lewy margines: prawa do danych", s.ShowLeftMarginText, v => s.ShowLeftMarginText = v));
                margins.Children.Add(TacticalConfigPanel.FieldBox("Treść (puste = automatycznie wg podkładu):", Input(s.LeftMarginText, v => s.LeftMarginText = v)));
                margins.Children.Add(Check("Prawy margines: uwagi", s.ShowRightMarginText, v => s.ShowRightMarginText = v));
                margins.Children.Add(TacticalConfigPanel.FieldBox("Treść (puste = uwaga o odwzorowaniu i siatce):", Input(s.RightMarginText, v => s.RightMarginText = v)));
                left.Children.Add(WrapCard(margins));

                var editor = CreateSectionCard("5. ELEMENTY Z EDYTORA MAPY");
                editor.Children.Add(Check("Obiekty taktyczne (markery, AO, trasy, sektory ostrzału)", s.IncludeMapObjects, v => s.IncludeMapObjects = v));
                editor.Children.Add(Row(2,
                    Check("Siatki z ekranu (MGRS/WGS84/sektory)", s.IncludeScreenGrids, v => s.IncludeScreenGrids = v),
                    Check("Legenda z ekranu", s.IncludeScreenLegend, v => s.IncludeScreenLegend = v)));
                editor.Children.Add(Row(2,
                    Check("Strzałka północy z ekranu", s.IncludeScreenNorthArrow, v => s.IncludeScreenNorthArrow = v),
                    Check("Podziałka z ekranu", s.IncludeScreenScaleBar, v => s.IncludeScreenScaleBar = v)));
                left.Children.Add(WrapCard(editor));

                var series = CreateSectionCard("6. SERIA ARKUSZY (WIELOSTRONICOWY PDF)");
                series.Children.Add(Row(3,
                    TacticalConfigPanel.FieldBox("Kolumny:", Combo(SeriesCounts, s.SeriesColumns.ToString(CultureInfo.InvariantCulture), v => s.SeriesColumns = int.Parse(v, CultureInfo.InvariantCulture))),
                    TacticalConfigPanel.FieldBox("Wiersze:", Combo(SeriesCounts, s.SeriesRows.ToString(CultureInfo.InvariantCulture), v => s.SeriesRows = int.Parse(v, CultureInfo.InvariantCulture))),
                    TacticalConfigPanel.FieldBox("Zakładka (%):", Number(s.SeriesOverlapPercent, v => s.SeriesOverlapPercent = v))));
                series.Children.Add(_seriesInfo);
                left.Children.Add(WrapCard(series));
                UpdateSeriesInfo();
            }

            private static UniformGrid Row(int columns, params UIElement[] children)
            {
                var g = new UniformGrid { Columns = columns, Margin = new Thickness(0, 0, 0, 2) };
                foreach (var child in children) g.Children.Add(child);
                return g;
            }

            private CheckBox Check(string text, bool value, Action<bool> set, bool layout = false)
            {
                var cb = new CheckBox
                {
                    Content = text, IsChecked = value, Foreground = Brushes.White, FontSize = 10.5,
                    Margin = new Thickness(0, 3, 8, 5), VerticalContentAlignment = VerticalAlignment.Center
                };
                cb.Checked += (_, __) => { set(true); Changed(layout); };
                cb.Unchecked += (_, __) => { set(false); Changed(layout); };
                return cb;
            }

            private ComboBox Combo(IReadOnlyList<string> items, string value, Action<string> set, bool layout = false)
            {
                var cb = new ComboBox
                {
                    ItemsSource = items, SelectedItem = items.Contains(value) ? value : items[0],
                    Height = 25, FontSize = 10.5, VerticalContentAlignment = VerticalAlignment.Center
                };
                TacticalConfigPanel.ApplyUnifiedComboBoxTheme(cb, this);
                cb.SelectionChanged += (_, __) =>
                {
                    if (cb.SelectedItem is string selected)
                    {
                        set(selected);
                        Changed(layout);
                    }
                };
                return cb;
            }

            private TextBox Input(string value, Action<string> set, bool layout = false)
            {
                var tb = new TextBox
                {
                    Text = value ?? "", Height = 25, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(5, 0, 5, 0),
                    FontSize = 10.5, Background = TacticalConfigPanel.BrushInputBg, Foreground = Brushes.White, CaretBrush = Brushes.White,
                    BorderBrush = TacticalConfigPanel.BrushInputBorder, BorderThickness = new Thickness(1)
                };
                tb.TextChanged += (_, __) =>
                {
                    set(tb.Text);
                    Changed(layout);
                };
                return tb;
            }

            private TextBox Number(double value, Action<double> set, bool layout = false) =>
                Input(value.ToString("0.##", CultureInfo.InvariantCulture), text =>
                {
                    if (double.TryParse((text ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)) set(parsed);
                }, layout);

            // ---------------------------------------------------------- zachowanie

            private void Changed(bool layout)
            {
                if (layout) ApplyLayout();
                UpdateSeriesInfo();
                _debounce.Stop();
                _debounce.Start();
            }

            private void ApplyLayout()
            {
                _s.Normalize();
                var layout = MapSheetLayout.Compute(_s);
                EnsurePrintSheetApplied(layout);
                var (wm, hm) = layout.GroundSize(_targetScaleDenominator);
                _areaInfo.Text = string.Format(CultureInfo.GetCultureInfo("pl-PL"),
                    "Strona {0:0.#} × {1:0.#} mm · obszar mapy {2:0.#} × {3:0.#} mm = teren {4:0.00} × {5:0.00} km w skali 1:{6:N0}. Arkusz w edytorze pokazuje dokładnie ten wycinek (poprzedni format wybierzesz z listy „Format arkusza”).",
                    layout.PageWidthMm, layout.PageHeightMm, layout.MapRect.Width, layout.MapRect.Height, wm / 1000.0, hm / 1000.0, _targetScaleDenominator);
            }

            private void UpdateSeriesInfo()
            {
                int n = Math.Clamp(_s.SeriesColumns, 1, 6) * Math.Clamp(_s.SeriesRows, 1, 6);
                _seriesInfo.Text = n == 1
                    ? "Jeden arkusz — dokładnie widok z edytora."
                    : string.Format(CultureInfo.GetCultureInfo("pl-PL"), "{0} stron: arkusze sąsiadujące wokół widoku z edytora (środek serii), zakładka {1:0.#}%. Każdy arkusz ma własną nazwę MGRS, narożnik SW i diagram PT/PM.", n, _s.SeriesOverlapPercent);
            }

            private void SetBusy(bool busy)
            {
                foreach (var b in _idleOnlyButtons) b.IsEnabled = !busy;
                _cancelButton.IsEnabled = busy;
            }

            private void PickLogoFile()
            {
                var dlg = new OpenFileDialog { Title = "Wybierz logo do stopki wydruku", Filter = "Obrazy (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg" };
                if (dlg.ShowDialog(this) != true) return;
                _s.LogoFilePath = dlg.FileName;
                _logoFileLabel.Text = Path.GetFileName(dlg.FileName);
                if (_logoModeCombo != null) _logoModeCombo.SelectedItem = MapPrintSettings.LogoModes[1];
                _s.LogoMode = MapPrintSettings.LogoModes[1];
                Changed(false);
            }

            private async Task FillSubtitleFromOsmAsync()
            {
                try
                {
                    _status.Text = "⏳ Szukam najbliższej miejscowości w OpenStreetMap (Nominatim)…";
                    string text = await ReverseGeocodeSubtitleAsync(_centerLat, _centerLon, _s.PolishLabels, CancellationToken.None);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        _status.Text = "⚠️ OpenStreetMap nie zwrócił nazwy miejscowości dla tego miejsca.";
                        return;
                    }
                    _subtitleBox.Text = text;
                    _status.Text = "✓ Podtytuł: " + text;
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException || ex is JsonException || ex is InvalidOperationException || ex is FormatException)
                {
                    _status.Text = "⚠️ Nie udało się pobrać nazwy z OpenStreetMap: " + ex.Message;
                }
            }

            private bool EditorReady()
            {
                if (_fullScreenOverlay?.Visibility == Visibility.Visible && _mapCompositeGrid != null && _sheetFrameBorder != null) return true;
                _status.Text = "Otwórz edytor mapy (sekcja z arkuszem), aby przygotować wydruk.";
                return false;
            }

            private async Task RefreshPreviewAsync()
            {
                if (_isExporting || !IsLoaded || !EditorReady()) return;
                _previewCts?.Cancel();
                var cts = new CancellationTokenSource();
                _previewCts = cts;
                try
                {
                    _s.Normalize();
                    var layout = MapSheetLayout.Compute(_s);
                    EnsurePrintSheetApplied(layout);
                    int dpi = (int)Math.Clamp(Math.Round(900.0 / (layout.PageHeightMm / 25.4)), 30.0, 72.0);
                    var basis = CapturePrintBasis(layout, dpi);
                    var plan = PlanPrintPages(basis, _s, centerOnly: true)[0];
                    var progress = new Progress<string>(m => { if (!cts.IsCancellationRequested) _status.Text = "Podgląd — " + m; });
                    var (bmp, facts) = await RenderFramedPageAsync(_s, layout, basis, plan, true, progress, cts.Token);
                    if (cts.IsCancellationRequested) return;
                    _preview.Source = bmp;
                    _status.Text = string.Format(CultureInfo.GetCultureInfo("pl-PL"),
                        "Podgląd: arkusz {0} · strefa UTM {1} · PT {2} · PM {3} ({4}) · oczko siatki {5:N0} m. Podgląd korzysta z kafelków ekranu; PDF pobierze podkład w pełnej rozdzielczości.",
                        facts.SheetId, facts.UtmZone, MapGridMath.FormatEastWest(facts.ConvergenceDeg), MapGridMath.FormatEastWest(facts.Magnetic.DeclinationDeg),
                        GeoMagneticModel.ModelName, facts.GridIntervalMeters);
                }
                catch (OperationCanceledException)
                {
                    // Nowszy podgląd zastąpił ten.
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is OutOfMemoryException || ex is NotSupportedException)
                {
                    _status.Text = "⚠️ Nie udało się przygotować podglądu: " + ex.Message;
                }
            }

            private async Task ExportAsync()
            {
                if (_isExporting || !EditorReady()) return;
                _s.Normalize();
                var layout = MapSheetLayout.Compute(_s);
                EnsurePrintSheetApplied(layout);
                int dpi = EffectivePrintDpi(layout, _s.Dpi);
                var basis = CapturePrintBasis(layout, dpi);
                var plans = PlanPrintPages(basis, _s, centerOnly: false);
                var firstFacts = MapSheetFacts.Compute(plans[0].View, basis.Scale, _s.GridIntervalMeters, DateTime.Now);

                string initialDir = !string.IsNullOrWhiteSpace(TacticalConfigPanel.LastExportDirectory) && Directory.Exists(TacticalConfigPanel.LastExportDirectory)
                    ? TacticalConfigPanel.LastExportDirectory
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string baseName = $"Mapa_{firstFacts.SheetId.Replace(' ', '_')}_1-{basis.Scale}" + (plans.Count > 1 ? $"_seria{plans.Count}" : "");
                var dlg = new SaveFileDialog
                {
                    Title = "Zapisz mapę z ramką jako PDF",
                    Filter = "Dokument PDF (*.pdf)|*.pdf",
                    InitialDirectory = initialDir,
                    FileName = TacticalConfigPanel.SanitizeFileName(baseName) + ".pdf"
                };
                if (dlg.ShowDialog(this) != true) return;
                string path = dlg.FileName;

                _isExporting = true;
                SetBusy(true);
                _previewCts?.Cancel();
                var cts = new CancellationTokenSource();
                _exportCts = cts;
                var pages = new List<PdfImagePage>(plans.Count);
                try
                {
                    foreach (var plan in plans)
                    {
                        string prefix = plans.Count > 1 ? $"Strona {plan.Index}/{plan.Count}: " : "";
                        var progress = new Progress<string>(m => _status.Text = prefix + m);
                        var (bmp, _) = await RenderFramedPageAsync(_s, layout, basis, plan, false, progress, cts.Token);
                        _status.Text = prefix + "kompresja strony…";
                        int quality = _s.JpegQuality;
                        byte[] jpeg = await Task.Run(() => EncodeJpegBytes(bmp, quality), cts.Token);
                        if (jpeg == null) throw new InvalidOperationException("Nie udało się zakodować strony do JPEG.");
                        pages.Add(new PdfImagePage(jpeg, bmp.PixelWidth, bmp.PixelHeight, layout.PageWidthMm, layout.PageHeightMm));
                    }

                    string title = plans.Count > 1 ? $"Seria {plans.Count} arkuszy — {firstFacts.SheetId}" : $"Arkusz {firstFacts.SheetId}";
                    _status.Text = "Zapisywanie pliku PDF…";
                    await Task.Run(() => PdfImageDocumentWriter.WriteFile(path, pages, title, "Field TAK Hub Builder"), cts.Token);
                    TacticalConfigPanel.LastExportDirectory = Path.GetDirectoryName(path) ?? TacticalConfigPanel.LastExportDirectory;
                    _status.Text = $"✓ Zapisano PDF: {pages.Count} str., {dpi} DPI → {path}";
                    TacticalConfigPanel.TryOpenFolderInExplorer(Path.GetDirectoryName(path));
                }
                catch (OperationCanceledException)
                {
                    _status.Text = "Przerwano — plik PDF nie został zapisany.";
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is OutOfMemoryException || ex is ArgumentException || ex is NotSupportedException)
                {
                    _status.Text = "⚠️ Błąd zapisu PDF: " + ex.Message;
                }
                finally
                {
                    _isExporting = false;
                    SetBusy(false);
                }
            }
        }
    }
}
