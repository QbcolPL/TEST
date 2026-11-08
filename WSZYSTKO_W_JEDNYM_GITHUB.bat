@echo off
setlocal DisableDelayedExpansion
cd /d "%~dp0"

set "BAT_FILE=%~f0"

echo ============================================================================
echo [1/3] Przebudowa przeciagania Legendy na natywny Thumb WPF (4 warstwy)...
echo ============================================================================
powershell -NoProfile -ExecutionPolicy Bypass -Command "Add-Type -TypeDefinition ([System.Text.RegularExpressions.Regex]::Split([System.IO.File]::ReadAllText([System.Environment]::GetEnvironmentVariable('BAT_FILE'), [System.Text.Encoding]::UTF8), '//===' + 'FIX_LEGEND_THUMB_DRAG_CS===')[1]); [FthFixLegendThumbDrag]::Run()"

if errorlevel 1 (
    echo.
    echo [BLAD] Wystapil blad podczas aktualizacji pliku lub wysylki na GitHub!
    pause
    exit /b 1
)

echo.
echo ============================================================================
echo [SUKCES] Natywne przeciaganie i skalowanie Legendy zostalo wdrozone
echo i wyslane na GitHub (QbcolPL/TEST)! Kompilacja w GitHub Actions uruchomiona.
echo ============================================================================
pause
exit /b 0

//===FIX_LEGEND_THUMB_DRAG_CS===
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public static class FthFixLegendThumbDrag
{
    private const string RepoRemoteUrl = "https://github.com/QbcolPL/TEST.git";
    private const string RepoActionsUrl = "https://github.com/QbcolPL/TEST/actions";

    public static void Run()
    {
        string currentDir = Directory.GetCurrentDirectory();
        string targetDir = ResolveTargetProjectDirectory(currentDir);
        Console.WriteLine("[OK] Katalog roboczy projektu: " + targetDir);

        string batFile = Environment.GetEnvironmentVariable("BAT_FILE");
        string rawBat = File.ReadAllText(batFile, Encoding.UTF8);

        string[] parts = Regex.Split(rawBat, "//===" + "PAYLOAD_THUMB_HUD_ENGINE_CS===");
        if (parts.Length < 2)
            throw new Exception("Brak bloku PAYLOAD_THUMB_HUD_ENGINE_CS w pliku .bat!");
        string[] endParts = Regex.Split(parts[1], "//===" + "END_THUMB_HUD_ENGINE_CS===");
        string payloadThumbHud = endParts[0].Trim();

        UTF8Encoding utf8NoBom = new UTF8Encoding(false);
        string tmgPath = Path.Combine(targetDir, @"apps\builder\FieldTakHub.Builder\Services\TacticalMapGeneratorView.cs");
        if (!File.Exists(tmgPath))
            throw new Exception("Nie znaleziono pliku TacticalMapGeneratorView.cs!");

        string tmg = File.ReadAllText(tmgPath, Encoding.UTF8);

        // Znajdz poczatek sekcji HUD (IsLegendSubControlClick, _transparentThumbTemplate lub PlaceDraggableHudElement)
        int startIdx = tmg.IndexOf("private static ControlTemplate _transparentThumbTemplate;", StringComparison.Ordinal);
        if (startIdx < 0)
            startIdx = tmg.IndexOf("private static bool IsLegendSubControlClick(", StringComparison.Ordinal);
        if (startIdx < 0)
            startIdx = tmg.IndexOf("private static void PlaceDraggableHudElement(", StringComparison.Ordinal);

        // Znajdz koniec sekcji HUD (przed CaptureCurrentMapSheetForOpordAnnex3Png lub CaptureMapCanvasPngBytes)
        int endIdx = tmg.IndexOf("public static byte[] CaptureCurrentMapSheetForOpordAnnex3Png", StringComparison.Ordinal);
        if (endIdx < 0)
            endIdx = tmg.IndexOf("private static byte[] CaptureMapCanvasPngBytes()", StringComparison.Ordinal);

        if (startIdx < 0 || endIdx <= startIdx)
            throw new Exception("Nie znaleziono granic sekcji PlaceDraggableHudElement / DrawDraggableCornerElements w TacticalMapGeneratorView.cs!");

        tmg = tmg.Substring(0, startIdx) + payloadThumbHud + "\r\n\r\n        " + tmg.Substring(endIdx);

        int openB = tmg.Count(c => c == '{');
        int closeB = tmg.Count(c => c == '}');
        Console.WriteLine("[WERYFIKACJA] Nawiasy klamrowe w TacticalMapGeneratorView.cs: { = " + openB + ", } = " + closeB);
        if (openB != closeB)
            throw new Exception("Niezgodnosc nawiasow klamrowych w TacticalMapGeneratorView.cs!");

        File.WriteAllText(tmgPath, tmg, utf8NoBom);
        Console.WriteLine("[OK] Zastapiono mechanizm przesuwania Legendy natywnym komponentem Thumb (4-warstwowy Grid).");

        string triggerPath = Path.Combine(targetDir, ".fth_build_trigger.txt");
        File.WriteAllText(triggerPath, "Field TAK Hub Native Thumb Legend Drag Fix UTC: " + DateTime.UtcNow.ToString("O") + " | RunId: " + Guid.NewGuid().ToString("N") + "\n", utf8NoBom);

        PushAndTriggerGitHubActions(targetDir);
    }

    private static string ResolveTargetProjectDirectory(string baseDir)
    {
        string csprojRel = @"apps\builder\FieldTakHub.Builder\FieldTakHub.Builder.csproj";
        if (File.Exists(Path.Combine(baseDir, csprojRel))) return baseDir;
        string subTestMain = Path.Combine(baseDir, "TEST-main");
        if (File.Exists(Path.Combine(subTestMain, csprojRel))) return subTestMain;
        string subGotowe = Path.Combine(baseDir, "TEST-main-Gotowe");
        if (File.Exists(Path.Combine(subGotowe, csprojRel))) return subGotowe;
        string parentDir = Path.GetDirectoryName(baseDir);
        if (!string.IsNullOrEmpty(parentDir))
        {
            if (File.Exists(Path.Combine(parentDir, csprojRel))) return parentDir;
            string parentTestMain = Path.Combine(parentDir, "TEST-main");
            if (File.Exists(Path.Combine(parentTestMain, csprojRel))) return parentTestMain;
        }
        throw new Exception("Nie znaleziono projektu FieldTakHub.Builder w katalogu: " + baseDir);
    }

    private static void PushAndTriggerGitHubActions(string targetDir)
    {
        Console.WriteLine();
        Console.WriteLine("============================================================================");
        Console.WriteLine("[2/3] Wysylka na GitHub (QbcolPL/TEST) i uruchomienie GitHub Actions...");
        Console.WriteLine("============================================================================");

        string gitExe = "git";
        string g1 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Git\cmd\git.exe");
        string g2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Git\cmd\git.exe");
        if (File.Exists(g1)) gitExe = g1;
        else if (File.Exists(g2)) gitExe = g2;

        string lockFile = Path.Combine(targetDir, ".git", "index.lock");
        if (File.Exists(lockFile))
        {
            try { File.Delete(lockFile); } catch { }
        }

        RunProcess(gitExe, "init", targetDir, ignoreError: false);
        RunProcess(gitExe, "config user.name \"QbcolPL\"", targetDir, ignoreError: true);
        RunProcess(gitExe, "config user.email \"QbcolPL@users.noreply.github.com\"", targetDir, ignoreError: true);
        RunProcess(gitExe, "branch -M main", targetDir, ignoreError: true);
        RunProcess(gitExe, "remote add origin " + RepoRemoteUrl, targetDir, ignoreError: true);
        RunProcess(gitExe, "remote set-url origin " + RepoRemoteUrl, targetDir, ignoreError: false);

        RunProcess(gitExe, "add -A", targetDir, ignoreError: false);
        RunProcess(gitExe, "commit -m \"Replace Legend drag mechanism with native WPF Thumb overlay and hit-test-transparent content layer\" --allow-empty", targetDir, ignoreError: false);

        int pushExitCode = RunProcess(gitExe, "push -u origin main --force", targetDir, ignoreError: false);
        if (pushExitCode != 0)
        {
            throw new Exception("Polecenie 'git push -u origin main --force' zwrocilo kod bledu: " + pushExitCode);
        }

        Console.WriteLine("[3/3] Otwieranie zakladki GitHub Actions...");
        try
        {
            Process.Start(new ProcessStartInfo { FileName = RepoActionsUrl, UseShellExecute = true });
        }
        catch { }
    }

    private static int RunProcess(string fileName, string arguments, string workingDir, bool ignoreError)
    {
        Console.WriteLine("> " + Path.GetFileName(fileName) + " " + arguments);
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            CreateNoWindow = false
        };
        using (var proc = Process.Start(psi))
        {
            proc.WaitForExit();
            if (proc.ExitCode != 0 && !ignoreError)
            {
                Console.WriteLine("[UWAGA] Kod wyjscia procesu: " + proc.ExitCode);
            }
            return proc.ExitCode;
        }
    }
}
//===FIX_LEGEND_THUMB_DRAG_CS===

//===PAYLOAD_THUMB_HUD_ENGINE_CS===
        private static ControlTemplate _transparentThumbTemplate;

        private static ControlTemplate GetTransparentThumbTemplate()
        {
            if (_transparentThumbTemplate != null) return _transparentThumbTemplate;
            var fef = new FrameworkElementFactory(typeof(Border));
            fef.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(2, 0, 0, 0)));
            var tpl = new ControlTemplate(typeof(Thumb)) { VisualTree = fef };
            tpl.Freeze();
            _transparentThumbTemplate = tpl;
            return tpl;
        }

        private static void PlaceDraggableHudElement(FrameworkElement el, string hudId, string cornerPreset, double? normX, double? normY, double viewW, double viewH, double elemW, double elemH)
        {
            bool validNormX = normX.HasValue && !double.IsNaN(normX.Value) && !double.IsInfinity(normX.Value) && normX.Value >= -0.2 && normX.Value <= 1.2;
            bool validNormY = normY.HasValue && !double.IsNaN(normY.Value) && !double.IsInfinity(normY.Value) && normY.Value >= -0.2 && normY.Value <= 1.2;

            double maxInitX = Math.Max(8.0, viewW - Math.Min(elemW, viewW - 40.0) - 4.0);
            double maxInitY = Math.Max(8.0, viewH - Math.Min(elemH, viewH - 40.0) - 4.0);

            double x = validNormX
                ? Math.Clamp(normX.Value * viewW, 4.0, maxInitX)
                : (cornerPreset.StartsWith("Lewy") ? 14.0 : Math.Max(14.0, viewW - elemW - 14.0));
            double y = validNormY
                ? Math.Clamp(normY.Value * viewH, 4.0, maxInitY)
                : (cornerPreset.Contains("g\u00F3rny") ? 14.0 : Math.Max(14.0, viewH - elemH - 14.0));

            Canvas.SetLeft(el, x);
            Canvas.SetTop(el, y);

            if (hudId != "LEGEND" && el is Border b && b.Child != null && !(b.Child is Grid && ((Grid)b.Child).Tag?.ToString() == "HUD_THUMB_HOST"))
            {
                UIElement innerChild = b.Child;
                innerChild.IsHitTestVisible = false;
                var hostGrid = new Grid { Tag = "HUD_THUMB_HOST" };
                hostGrid.Children.Add(innerChild);

                var moveThumb = new Thumb
                {
                    Template = GetTransparentThumbTemplate(),
                    Cursor = Cursors.SizeAll,
                    ToolTip = "Przeci\u0105gnij mysz\u0105 (Drag & Drop), aby ustawi\u0107 w dowolnym miejscu na mapie"
                };

                Point startMousePt = new Point();
                double startLeft = x, startTop = y;

                moveThumb.DragStarted += (s, e) =>
                {
                    e.Handled = true;
                    startMousePt = Mouse.GetPosition(_interactiveCanvas);
                    startLeft = Canvas.GetLeft(el);
                    startTop = Canvas.GetTop(el);
                    if (double.IsNaN(startLeft)) startLeft = x;
                    if (double.IsNaN(startTop)) startTop = y;
                };

                moveThumb.DragDelta += (s, e) =>
                {
                    e.Handled = true;
                    var (cw, ch) = GetExactCanvasSize();
                    Point curPt = Mouse.GetPosition(_interactiveCanvas);
                    double actualW = el.ActualWidth > 10 ? el.ActualWidth : elemW;
                    double actualH = el.ActualHeight > 10 ? el.ActualHeight : elemH;
                    double limX = Math.Max(4.0, cw - Math.Min(actualW, cw - 30.0) - 4.0);
                    double limY = Math.Max(4.0, ch - Math.Min(actualH, ch - 30.0) - 4.0);
                    double nx = Math.Clamp(startLeft + (curPt.X - startMousePt.X), 4.0, limX);
                    double ny = Math.Clamp(startTop + (curPt.Y - startMousePt.Y), 4.0, limY);
                    Canvas.SetLeft(el, nx);
                    Canvas.SetTop(el, ny);
                    if (hudId == "NORTH") { _northNormX = nx / Math.Max(1.0, cw); _northNormY = ny / Math.Max(1.0, ch); }
                    else if (hudId == "SCALE") { _scaleNormX = nx / Math.Max(1.0, cw); _scaleNormY = ny / Math.Max(1.0, ch); }
                };

                moveThumb.DragCompleted += (s, e) =>
                {
                    e.Handled = true;
                    TacticalConfigPanel.MarkDirty();
                };

                hostGrid.Children.Add(moveThumb);
                b.Child = hostGrid;
            }

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
                cs.Children.Add(new TextBlock { Text = "\u25B2", Foreground = Brushes.White, FontSize = isCompactWrist ? 14 : 18, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -4, 0, -2) });
                cs.Children.Add(new TextBlock { Text = "MGRS", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 7.5, HorizontalAlignment = HorizontalAlignment.Center });
                compass.Child = cs;
                PlaceDraggableHudElement(compass, "NORTH", _northArrowPos, _northNormX, _northNormY, w, h, nw, nh);
            }

            if (_showScaleBar)
            {
                double metersPerPixel = GetExactMetersPerPixel(), chosen = _coordSystem.StartsWith("MGRS") ? _effectiveMgrsStepMeters : 100;
                if (chosen / metersPerPixel < 50 || chosen / metersPerPixel > 240)
                    foreach (var d in new[] { 20.0, 50.0, 100.0, 200.0, 500.0, 1000.0, 2000.0, 5000.0 })
                    {
                        double px = d / metersPerPixel;
                        if (px >= (isCompactWrist ? 55 : 70) && px <= (isCompactWrist ? 150 : 220)) { chosen = d; break; }
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
                double defaultLw = isCompactWrist ? 220 : 288;
                double lw = _legendCustomWidth.HasValue && !double.IsNaN(_legendCustomWidth.Value)
                    ? Math.Clamp(_legendCustomWidth.Value, 160, Math.Max(180, w - 16))
                    : defaultLw;

                var ls = new StackPanel { IsHitTestVisible = false };

                string autoTitle = $"MAPA TAKTYCZNA ({_selectedSheet.Name.Split('\u2014')[0].Trim()} | 1:{_targetScaleDenominator:N0})";
                string displayTitle = string.IsNullOrWhiteSpace(_customLegendTitle) ? autoTitle : _customLegendTitle;

                ls.Children.Add(new TextBlock
                {
                    Text = displayTitle,
                    Foreground = TacticalConfigPanel.BrushAccent,
                    FontWeight = FontWeights.Bold,
                    FontSize = isCompactWrist ? 9 : 10,
                    Margin = new Thickness(0, 0, _isExportingCleanMap ? 0 : 54, 0),
                    TextWrapping = TextWrapping.Wrap
                });

                string secLegSuffix = (_enableSectorGridOverlay || _coordSystem.StartsWith("Sektorowa")) ? $" + Sektory A-Z/1-9 ({_sectorSizeMeters}m | {_sectorSubDivision.Split(' ')[0]})" : "";
                string autoSub = (_coordSystem.StartsWith("MGRS") ? $"Siatka: MGRS ({_effectiveMgrsStepMeters} m) | {_subGridDivision}" : $"Siatka: {_coordSystem.Split(' ')[0]}") + secLegSuffix;
                string displaySub = string.IsNullOrWhiteSpace(_customLegendSubtitle) ? autoSub : _customLegendSubtitle;
                ls.Children.Add(new TextBlock { Text = displaySub, Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 8.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 4) });

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

                    void AddVectorLegendRow(string symCode, string desc, string hex)
                    {
                        dynamicRows++;
                        var r = new Grid { Margin = new Thickness(0, 0, 0, 3), IsHitTestVisible = false };
                        r.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
                        r.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                        var iconEl = CreateMiniSymbolVectorBadge(symCode, hex);
                        iconEl.Margin = new Thickness(0, 0, 6, 0);
                        iconEl.IsHitTestVisible = false;
                        Grid.SetColumn(iconEl, 0);
                        r.Children.Add(iconEl);

                        var descTb = new TextBlock
                        {
                            Text = desc,
                            Foreground = Brushes.White,
                            FontSize = 8.4,
                            VerticalAlignment = VerticalAlignment.Center,
                            TextWrapping = TextWrapping.Wrap,
                            IsHitTestVisible = false
                        };
                        Grid.SetColumn(descTb, 1);
                        r.Children.Add(descTb);
                        ls.Children.Add(r);
                    }

                    if (_mapObjects.Count > 0)
                    {
                        var sourceList = _mapObjects.AsEnumerable();
                        if (_groupIdenticalLegendItems)
                        {
                            var grouped = _mapObjects
                                .GroupBy(o => $"{o.Kind}|{(o.Kind == "Dot" ? "DOT" : o.SymbolCode)}|{o.ColorHex.ToUpperInvariant()}|{(o.Name ?? "").Trim()}|{GetDefaultLegendDescriptionForObject(o).Trim()}")
                                .Take(isCompactWrist ? 8 : 16);

                            foreach (var grp in grouped)
                            {
                                var obj = grp.First();
                                int cnt = grp.Count();
                                string sym = obj.Kind == "Dot" ? "DOT" : ((obj.Kind == "AO" || obj.Kind == "Sector" || obj.Kind == "Route") ? obj.Kind : obj.SymbolCode);
                                string legDesc = GetDefaultLegendDescriptionForObject(obj);
                                string countBadge = cnt > 1 ? $" (\u00D7{cnt})" : "";
                                string combinedText = string.Equals(obj.Name?.Trim(), legDesc?.Trim(), StringComparison.OrdinalIgnoreCase)
                                    ? $"{obj.Name}{countBadge}"
                                    : $"{obj.Name}{countBadge} \u2014 {legDesc}";
                                AddVectorLegendRow(sym, combinedText, obj.ColorHex);
                            }
                        }
                        else
                        {
                            foreach (var obj in sourceList.Take(isCompactWrist ? 8 : 14))
                            {
                                string sym = obj.Kind == "Dot" ? "DOT" : ((obj.Kind == "AO" || obj.Kind == "Sector" || obj.Kind == "Route") ? obj.Kind : obj.SymbolCode);
                                string legDesc = GetDefaultLegendDescriptionForObject(obj);
                                string combinedText = string.Equals(obj.Name?.Trim(), legDesc?.Trim(), StringComparison.OrdinalIgnoreCase)
                                    ? obj.Name
                                    : $"{obj.Name} \u2014 {legDesc}";
                                AddVectorLegendRow(sym, combinedText, obj.ColorHex);
                            }
                        }
                    }
                    else
                    {
                        AddVectorLegendRow("NATO_HQ", "HQ \u2014 Sztab / Dow\u00F3dztwo strony", "#2684FF");
                        AddVectorLegendRow("SHAPE_TENT", "Respawn \u2014 Namiot / Baza polowa", "#2ECC71");
                        AddVectorLegendRow("SHAPE_ARROW", "O\u015B natarcia \u2014 Kierunek ruchu", "#F5B82E");
                        AddVectorLegendRow("AO", "Granica Terenu Gry (strefa poza gr\u0105)", "#EB4034");
                    }
                }

                if (!string.IsNullOrWhiteSpace(_customLegendExtraNotes))
                {
                    ls.Children.Add(new Border { Height = 1, Background = TacticalConfigPanel.BrushTileBorder, Margin = new Thickness(0, 3, 0, 3) });
                    ls.Children.Add(new TextBlock
                    {
                        Text = _customLegendExtraNotes,
                        Foreground = TacticalConfigPanel.BrushAccent,
                        FontSize = 8.4,
                        TextWrapping = TextWrapping.Wrap,
                        IsHitTestVisible = false
                    });
                }

                double autoLh = 72 + (_showNatoSymbolsInLegend ? (24 + dynamicRows * 25) : 0) + (string.IsNullOrWhiteSpace(_customLegendExtraNotes) ? 0 : 28);
                double lh = _legendCustomHeight.HasValue && !double.IsNaN(_legendCustomHeight.Value)
                    ? Math.Clamp(_legendCustomHeight.Value, 75, Math.Max(90, h - 16))
                    : autoLh;

                var legContainer = new Grid { Width = lw, Height = lh };

                // Warstwa 0: Wizualna ramka legendy (IsHitTestVisible = false, aby nie blokowala przeciagania)
                var legBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(230, 8, 19, 22)),
                    BorderBrush = TacticalConfigPanel.BrushAccent,
                    BorderThickness = new Thickness(1.3),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(9, 7, 9, 7),
                    ClipToBounds = true,
                    IsHitTestVisible = false,
                    Child = ls
                };
                legContainer.Children.Add(legBorder);

                PlaceDraggableHudElement(legContainer, "LEGEND", _legendPos, _legendNormX, _legendNormY, w, h, lw, lh);

                if (!_isExportingCleanMap)
                {
                    // Warstwa 1: Natywny Thumb do swobodnego przesuwania calej Legendy po mapie
                    var dragCenterThumb = new Thumb
                    {
                        Template = GetTransparentThumbTemplate(),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch,
                        Margin = new Thickness(7),
                        Cursor = Cursors.SizeAll,
                        ToolTip = "Przeci\u0105gnij LPM = przesu\u0144 Legend\u0119 w dowolne miejsce  |  Przeci\u0105gnij kraw\u0119d\u017A/naro\u017Cnik = zmie\u0144 rozmiar"
                    };

                    Point moveStartMousePt = new Point();
                    double moveStartLeft = 0, moveStartTop = 0;

                    dragCenterThumb.DragStarted += (s, e) =>
                    {
                        e.Handled = true;
                        moveStartMousePt = Mouse.GetPosition(_interactiveCanvas);
                        moveStartLeft = Canvas.GetLeft(legContainer);
                        moveStartTop = Canvas.GetTop(legContainer);
                        if (double.IsNaN(moveStartLeft)) moveStartLeft = 14.0;
                        if (double.IsNaN(moveStartTop)) moveStartTop = 14.0;
                    };

                    dragCenterThumb.DragDelta += (s, e) =>
                    {
                        e.Handled = true;
                        var (cw, ch) = GetExactCanvasSize();
                        Point curMousePt = Mouse.GetPosition(_interactiveCanvas);
                        double actualW = legContainer.ActualWidth > 20 ? legContainer.ActualWidth : legContainer.Width;
                        double actualH = legContainer.ActualHeight > 20 ? legContainer.ActualHeight : legContainer.Height;
                        double maxX = Math.Max(4.0, cw - Math.Min(actualW, cw - 40.0) - 4.0);
                        double maxY = Math.Max(4.0, ch - Math.Min(actualH, ch - 40.0) - 4.0);
                        double nx = Math.Clamp(moveStartLeft + (curMousePt.X - moveStartMousePt.X), 4.0, maxX);
                        double ny = Math.Clamp(moveStartTop + (curMousePt.Y - moveStartMousePt.Y), 4.0, maxY);
                        Canvas.SetLeft(legContainer, nx);
                        Canvas.SetTop(legContainer, ny);
                        _legendNormX = nx / Math.Max(1.0, cw);
                        _legendNormY = ny / Math.Max(1.0, ch);
                    };

                    dragCenterThumb.DragCompleted += (s, e) =>
                    {
                        e.Handled = true;
                        TacticalConfigPanel.MarkDirty();
                    };

                    legContainer.Children.Add(dragCenterThumb);

                    // Warstwa 2: Przycisk 'Edytuj' na wierzchu w prawym gornym rogu Legendy
                    var btnEditLeg = new Button
                    {
                        Content = "\u270F\uFE0F Edytuj",
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(0, 7, 9, 0),
                        Height = 18,
                        Padding = new Thickness(6, 0, 6, 0),
                        Background = TacticalConfigPanel.BrushItemBg,
                        Foreground = TacticalConfigPanel.BrushAccent,
                        BorderBrush = TacticalConfigPanel.BrushAccent,
                        BorderThickness = new Thickness(1),
                        FontSize = 8.2,
                        FontWeight = FontWeights.Bold,
                        Cursor = Cursors.Hand,
                        ToolTip = "Edytuj tre\u015B\u0107, opisy i wymiary Legendy"
                    };
                    btnEditLeg.Click += (s, e) =>
                    {
                        if (_hostWindow != null) ShowLegendContentEditorDialog(_hostWindow);
                    };
                    legContainer.Children.Add(btnEditLeg);

                    // Warstwa 3: 8 uchwytow zmiany rozmiaru (4 boki + 4 narozniki)
                    AttachFourSidedResizeThumbsToLegend(legContainer, w, h);
                }
            }
        }

        private static void AttachFourSidedResizeThumbsToLegend(Grid legContainer, double viewW, double viewH)
        {
            void AddResizeThumb(HorizontalAlignment ha, VerticalAlignment va, double thW, double thH, Cursor cur, int dirX, int dirY)
            {
                var thumb = new Thumb
                {
                    Template = GetTransparentThumbTemplate(),
                    HorizontalAlignment = ha,
                    VerticalAlignment = va,
                    Width = double.IsNaN(thW) ? double.NaN : thW,
                    Height = double.IsNaN(thH) ? double.NaN : thH,
                    Cursor = cur,
                    ToolTip = "Przeci\u0105gnij kraw\u0119d\u017A / naro\u017Cnik, aby zmieni\u0107 rozmiar Legendy"
                };

                if (double.IsNaN(thW)) thumb.Margin = new Thickness(9, 0, 9, 0);
                if (double.IsNaN(thH)) thumb.Margin = new Thickness(0, 9, 0, 9);

                Point resizeStartMousePt = new Point();
                double startLeft = 0, startTop = 0, startW = 0, startH = 0;

                thumb.DragStarted += (s, e) =>
                {
                    e.Handled = true;
                    resizeStartMousePt = Mouse.GetPosition(_interactiveCanvas);
                    startLeft = Canvas.GetLeft(legContainer);
                    startTop = Canvas.GetTop(legContainer);
                    if (double.IsNaN(startLeft)) startLeft = 14.0;
                    if (double.IsNaN(startTop)) startTop = 14.0;
                    startW = legContainer.ActualWidth > 20 ? legContainer.ActualWidth : legContainer.Width;
                    startH = legContainer.ActualHeight > 20 ? legContainer.ActualHeight : legContainer.Height;
                };

                thumb.DragDelta += (s, e) =>
                {
                    e.Handled = true;
                    var (cw, ch) = GetExactCanvasSize();
                    Point curPt = Mouse.GetPosition(_interactiveCanvas);
                    double dx = curPt.X - resizeStartMousePt.X;
                    double dy = curPt.Y - resizeStartMousePt.Y;

                    if (dirX == 1)
                    {
                        double newW = Math.Clamp(startW + dx, 160.0, Math.Max(160.0, cw - startLeft - 4.0));
                        legContainer.Width = newW;
                        _legendCustomWidth = newW;
                    }
                    else if (dirX == -1)
                    {
                        double rightEdge = startLeft + startW;
                        double newLeft = Math.Clamp(startLeft + dx, 4.0, Math.Max(4.0, rightEdge - 160.0));
                        double newW = rightEdge - newLeft;
                        Canvas.SetLeft(legContainer, newLeft);
                        legContainer.Width = newW;
                        _legendCustomWidth = newW;
                        _legendNormX = newLeft / Math.Max(1.0, cw);
                    }

                    if (dirY == 1)
                    {
                        double newH = Math.Clamp(startH + dy, 75.0, Math.Max(75.0, ch - startTop - 4.0));
                        legContainer.Height = newH;
                        _legendCustomHeight = newH;
                    }
                    else if (dirY == -1)
                    {
                        double bottomEdge = startTop + startH;
                        double newTop = Math.Clamp(startTop + dy, 4.0, Math.Max(4.0, bottomEdge - 75.0));
                        double newH = bottomEdge - newTop;
                        Canvas.SetTop(legContainer, newTop);
                        legContainer.Height = newH;
                        _legendCustomHeight = newH;
                        _legendNormY = newTop / Math.Max(1.0, ch);
                    }
                };

                thumb.DragCompleted += (s, e) =>
                {
                    e.Handled = true;
                    TacticalConfigPanel.MarkDirty();
                };

                legContainer.Children.Add(thumb);
            }

            // 4 boki (Lewy, Prawy, Gorny, Dolny)
            AddResizeThumb(HorizontalAlignment.Left, VerticalAlignment.Stretch, 7, double.NaN, Cursors.SizeWE, -1, 0);
            AddResizeThumb(HorizontalAlignment.Right, VerticalAlignment.Stretch, 7, double.NaN, Cursors.SizeWE, 1, 0);
            AddResizeThumb(HorizontalAlignment.Stretch, VerticalAlignment.Top, double.NaN, 7, Cursors.SizeNS, 0, -1);
            AddResizeThumb(HorizontalAlignment.Stretch, VerticalAlignment.Bottom, double.NaN, 7, Cursors.SizeNS, 0, 1);

            // 4 narozniki (Lewy-Gorny, Prawy-Gorny, Lewy-Dolny, Prawy-Dolny)
            AddResizeThumb(HorizontalAlignment.Left, VerticalAlignment.Top, 12, 12, Cursors.SizeNWSE, -1, -1);
            AddResizeThumb(HorizontalAlignment.Right, VerticalAlignment.Top, 12, 12, Cursors.SizeNESW, 1, -1);
            AddResizeThumb(HorizontalAlignment.Left, VerticalAlignment.Bottom, 12, 12, Cursors.SizeNESW, -1, 1);
            AddResizeThumb(HorizontalAlignment.Right, VerticalAlignment.Bottom, 12, 12, Cursors.SizeNWSE, 1, 1);
        }
//===END_THUMB_HUD_ENGINE_CS===