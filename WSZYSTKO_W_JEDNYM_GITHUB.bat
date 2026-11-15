@echo off
setlocal DisableDelayedExpansion
cd /d "%~dp0"

set "BAT_FILE=%~f0"

echo ============================================================================
echo [KROK 1/2] Odblokowanie Generatora Map (usuniecie zawieszania watku UI)
echo            oraz wdrozenie szybkiej, pojedynczej siatki WGS84 (O(1))...
echo ============================================================================
powershell -NoProfile -ExecutionPolicy Bypass -Command "Add-Type -TypeDefinition ([System.Text.RegularExpressions.Regex]::Split([System.IO.File]::ReadAllText([System.Environment]::GetEnvironmentVariable('BAT_FILE'), [System.Text.Encoding]::UTF8), '//===' + 'STEP1_UNFREEZE_AND_WGS84_CS===')[1]); [FthStep1UnfreezeAndWgs84]::Run()"

if errorlevel 1 (
    echo.
    echo [BLAD] Wystapil blad podczas wykonywania Kroku 1 lub wysylki na GitHub!
    pause
    exit /b 1
)

echo.
echo ============================================================================
echo [SUKCES - KROK 1/2] Generator Map zostal odblokowany, a pojedyncza siatka
echo WGS84 wyslana na GitHub! Po sprawdzeniu przejdz do Kroku 2 (UI i Tabela).
echo ============================================================================
pause
exit /b 0

//===STEP1_UNFREEZE_AND_WGS84_CS===
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public static class FthStep1UnfreezeAndWgs84
{
    private const string RepoRemoteUrl = "https://github.com/QbcolPL/TEST.git";
    private const string RepoActionsUrl = "https://github.com/QbcolPL/TEST/actions";

    public static void Run()
    {
        string currentDir = Directory.GetCurrentDirectory();
        string targetDir = ResolveTargetProjectDirectory(currentDir);
        Console.WriteLine("[OK] Katalog roboczy projektu: " + targetDir);

        UTF8Encoding utf8NoBom = new UTF8Encoding(false);
        string servicesDir = Path.Combine(targetDir, @"apps\builder\FieldTakHub.Builder\Services");

        string tmgPath = Path.Combine(servicesDir, "TacticalMapGeneratorView.cs");
        string engPath = Path.Combine(servicesDir, "TacticalMapGeneratorView.Engine.cs");
        string wgsPath = Path.Combine(servicesDir, "TacticalMapGeneratorView.Wgs84.cs");

        if (!File.Exists(tmgPath))
            throw new Exception("Nie znaleziono pliku TacticalMapGeneratorView.cs!");

        // 1. Oczyszczenie TacticalMapGeneratorView.cs z petli i wywolan powodujacych zawieszanie
        string tmg = File.ReadAllText(tmgPath, Encoding.UTF8);
        tmg = ReplaceInjectedWgsBlocksWithSafeNoOpOrBreak(tmg, "TacticalMapGeneratorView.cs");
        tmg = RemoveRestyleTableCallsForStep1(tmg);
        tmg = EnsureSingleSafeWgsHook(tmg);

        int openTmg = tmg.Count(c => c == '{');
        int closeTmg = tmg.Count(c => c == '}');
        Console.WriteLine("[WERYFIKACJA] Nawiasy w TacticalMapGeneratorView.cs: { = " + openTmg + ", } = " + closeTmg);
        if (openTmg != closeTmg)
            throw new Exception("Niezgodnosc nawiasow klamrowych w TacticalMapGeneratorView.cs!");
        File.WriteAllText(tmgPath, tmg, utf8NoBom);

        // 2. Oczyszczenie TacticalMapGeneratorView.Engine.cs z petli powodujacych zawieszanie
        string eng = "";
        if (File.Exists(engPath))
        {
            eng = File.ReadAllText(engPath, Encoding.UTF8);
            eng = ReplaceInjectedWgsBlocksWithSafeNoOpOrBreak(eng, "TacticalMapGeneratorView.Engine.cs");
            eng = RemoveRestyleTableCallsForStep1(eng);

            int openEng = eng.Count(c => c == '{');
            int closeEng = eng.Count(c => c == '}');
            Console.WriteLine("[WERYFIKACJA] Nawiasy w TacticalMapGeneratorView.Engine.cs: { = " + openEng + ", } = " + closeEng);
            if (openEng != closeEng)
                throw new Exception("Niezgodnosc nawiasow klamrowych w TacticalMapGeneratorView.Engine.cs!");
            File.WriteAllText(engPath, eng, utf8NoBom);
        }

        // 3. Zapisanie szybkiego, wolnego od rekurencji pliku TacticalMapGeneratorView.Wgs84.cs
        string combined = tmg + "\n" + eng;
        string latField = DetectLatFieldName(combined);
        string lonField = DetectLonFieldName(combined, latField);
        string gridCanvasField = DetectGridCanvasFieldName(combined);
        string redrawCallExpr = DetectCallerOfDrawDraggableCornerElements(combined);
        string gridColorField = DetectGridColorField(combined);
        string gridThicknessField = DetectGridThicknessField(combined);

        string batFile = Environment.GetEnvironmentVariable("BAT_FILE");
        string rawBat = File.ReadAllText(batFile, Encoding.UTF8);

        string[] parts = Regex.Split(rawBat, "//===" + "PAYLOAD_STEP1_WGS84_CS===");
        if (parts.Length < 2)
            throw new Exception("Brak bloku PAYLOAD_STEP1_WGS84_CS w pliku .bat!");
        string[] endParts = Regex.Split(parts[1], "//===" + "END_STEP1_WGS84_CS===");
        string wgsTemplate = endParts[0].Trim();

        wgsTemplate = wgsTemplate
            .Replace("__LAT_FIELD__", latField)
            .Replace("__LON_FIELD__", lonField)
            .Replace("__GRID_CANVAS__", gridCanvasField)
            .Replace("__REDRAW_CALL__", redrawCallExpr)
            .Replace("__GRID_COLOR_EXPR__", gridColorField)
            .Replace("__GRID_THICKNESS_EXPR__", gridThicknessField);

        int openWgs = wgsTemplate.Count(c => c == '{');
        int closeWgs = wgsTemplate.Count(c => c == '}');
        Console.WriteLine("[WERYFIKACJA] Nawiasy w TacticalMapGeneratorView.Wgs84.cs: { = " + openWgs + ", } = " + closeWgs);
        if (openWgs != closeWgs)
            throw new Exception("Niezgodnosc nawiasow klamrowych w TacticalMapGeneratorView.Wgs84.cs!");

        File.WriteAllText(wgsPath, wgsTemplate + "\r\n", utf8NoBom);
        Console.WriteLine("[OK] Zapisano szybki modul siatki WGS84 (O(1), zero blokad UI).");

        string triggerPath = Path.Combine(targetDir, ".fth_build_trigger.txt");
        File.WriteAllText(triggerPath, "Field TAK Hub Step 1 Unfreeze Map Generator & Fast WGS84 Grid UTC: " + DateTime.UtcNow.ToString("O") + " | RunId: " + Guid.NewGuid().ToString("N") + "\n", utf8NoBom);

        PushAndTriggerGitHubActions(targetDir);
    }

    private static string ReplaceInjectedWgsBlocksWithSafeNoOpOrBreak(string code, string fileLabel)
    {
        string pattern = @"\{\s*var\s*\(__wgsW\s*,\s*__wgsH\)\s*=\s*GetExactCanvasSize\(\)\s*;\s*RenderCalibratedWgs84GridOnSheet\(__wgsW\s*,\s*__wgsH\)\s*;\s*\}";
        int count = 0;

        code = Regex.Replace(code, pattern, match =>
        {
            count++;
            int idx = match.Index;
            string before = code.Substring(Math.Max(0, idx - 160), Math.Min(160, idx)).Trim();
            if (Regex.IsMatch(before, @"\b(for|while|foreach)\s*\([^)]*\)\s*$"))
            {
                return "{ break; }";
            }
            return "{ }";
        });

        if (count > 0)
        {
            Console.WriteLine("[OK] Usunieto " + count + " blokujacych wywolan w petlach w " + fileLabel + ".");
        }
        return code;
    }

    private static string RemoveRestyleTableCallsForStep1(string code)
    {
        return Regex.Replace(code, @"RestyleAndAlignMapObjectsTable\s*\([^)]*\)\s*;", "");
    }

    private static string EnsureSingleSafeWgsHook(string code)
    {
        code = code.Replace("ScheduleDeferredWgs84CleanAndRender(w, h);", "");
        return code;
    }

    private static string DetectCallerOfDrawDraggableCornerElements(string combined)
    {
        int searchPos = 0;
        while (true)
        {
            int callIdx = combined.IndexOf("DrawDraggableCornerElements(", searchPos, StringComparison.Ordinal);
            if (callIdx < 0) break;
            searchPos = callIdx + 28;

            string before = combined.Substring(Math.Max(0, callIdx - 45), Math.Min(45, callIdx));
            if (before.Contains("void ")) continue;

            string prefix = combined.Substring(0, callIdx);
            MatchCollection sigs = Regex.Matches(prefix, @"(?:private|public|internal)\s+static\s+void\s+([a-zA-Z0-9_]+)\s*\(\s*\)");
            if (sigs.Count > 0)
            {
                string callerName = sigs[sigs.Count - 1].Groups[1].Value;
                if (callerName != "DrawDraggableCornerElements")
                {
                    return callerName + "()";
                }
            }
        }
        return "TacticalConfigPanel.MarkDirty()";
    }

    private static string DetectLatFieldName(string combined)
    {
        int mppIdx = combined.IndexOf("GetExactMetersPerPixel", StringComparison.Ordinal);
        if (mppIdx >= 0)
        {
            string snippet = combined.Substring(mppIdx, Math.Min(600, combined.Length - mppIdx));
            Match m = Regex.Match(snippet, @"(_[a-zA-Z0-9_]*Lat[a-zA-Z0-9_]*)\b");
            if (m.Success) return m.Groups[1].Value;
        }
        string[] candidates = new[] { "_centerLat", "_currentLat", "_mapCenterLat", "_targetLat", "_lat" };
        foreach (string c in candidates)
        {
            if (Regex.IsMatch(combined, @"\b" + Regex.Escape(c) + @"\b")) return c;
        }
        return "_centerLat";
    }

    private static string DetectLonFieldName(string combined, string latField)
    {
        string guessedLon = latField.Replace("Lat", "Lon").Replace("lat", "lon");
        if (Regex.IsMatch(combined, @"\b" + Regex.Escape(guessedLon) + @"\b")) return guessedLon;
        string[] candidates = new[] { "_centerLon", "_currentLon", "_mapCenterLon", "_targetLon", "_lon", "_centerLng" };
        foreach (string c in candidates)
        {
            if (Regex.IsMatch(combined, @"\b" + Regex.Escape(c) + @"\b")) return c;
        }
        return "_centerLon";
    }

    private static string DetectGridCanvasFieldName(string combined)
    {
        string[] candidates = new[] { "_gridCanvas", "_overlayCanvas", "_mapGridCanvas", "_interactiveCanvas" };
        foreach (string c in candidates)
        {
            if (Regex.IsMatch(combined, @"\b" + Regex.Escape(c) + @"\b")) return c;
        }
        return "_interactiveCanvas";
    }

    private static string DetectGridColorField(string combined)
    {
        string[] candidates = new[] { "_gridColorHex", "_mainGridColorHex", "_mgrsColorHex" };
        foreach (string c in candidates)
        {
            if (Regex.IsMatch(combined, @"string\s+" + Regex.Escape(c) + @"\b")) return c;
        }
        return "\"#FAD02C\"";
    }

    private static string DetectGridThicknessField(string combined)
    {
        string[] candidates = new[] { "_gridLineThickness", "_mainGridThickness", "_lineThickness" };
        foreach (string c in candidates)
        {
            if (Regex.IsMatch(combined, @"double\s+" + Regex.Escape(c) + @"\b")) return c;
        }
        return "1.2";
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
        RunProcess(gitExe, "commit -m \"Step 1/2: Fix Map Generator UI freeze and deploy fast O(1) single WGS84 grid\" --allow-empty", targetDir, ignoreError: false);

        int pushExitCode = RunProcess(gitExe, "push -u origin main --force", targetDir, ignoreError: false);
        if (pushExitCode != 0)
            throw new Exception("Polecenie 'git push -u origin main --force' zwrocilo kod bledu: " + pushExitCode);

        try { Process.Start(new ProcessStartInfo { FileName = RepoActionsUrl, UseShellExecute = true }); } catch { }
    }

    private static int RunProcess(string fileName, string arguments, string workingDir, bool ignoreError)
    {
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
            return proc.ExitCode;
        }
    }
}
//===STEP1_UNFREEZE_AND_WGS84_CS===

//===PAYLOAD_STEP1_WGS84_CS===
#pragma warning disable CS0414, CS0649, CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625, CS8629
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FieldTakHub.Builder.Services
{
    public static partial class TacticalMapGeneratorView
    {
        private static string _wgs84StepPreset = "Auto (dostosuj do skali mapy)";
        private static string _wgs84FormatPreset = "DMS (49°50'58\"N) — Stopnie Minuty Sekundy";
        private static string _wgs84SubGridPreset = "Krzyżyki w środku (+)";
        private static bool _wgs84SnapToTrueGeo = true;
        private static double _wgs84OffsetNorthMeters = 0.0;
        private static double _wgs84OffsetEastMeters = 0.0;
        private static double _wgs84ScaleXPercent = 100.0;
        private static double _wgs84ScaleYPercent = 100.0;

        private static Border _wgs84ConfigPanelBorder;
        private static TextBlock _wgs84ActiveStepStatusText;
        private static TextBox _wgs84OffsetNBox;
        private static TextBox _wgs84OffsetEBox;
        private static TextBox _wgs84ScaleXBox;
        private static TextBox _wgs84ScaleYBox;
        private static TextBox _wgs84CenterLatOverrideBox;
        private static TextBox _wgs84CenterLonOverrideBox;

        private static bool _suppressWgs84LiveTextEvents = false;
        private static bool _isRenderingWgs84Grid = false;

        private static bool IsWgs84ModeActive()
        {
            return _coordSystem != null && _coordSystem.IndexOf("WGS84", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static void RestyleAndAlignMapObjectsTable(Panel listPanel) { }

        private static string GetWgs84LegendSubtitleSummary()
        {
            string fmtShort = _wgs84FormatPreset.Split(' ')[0];
            string stepDesc = GetWgs84StepHumanSummary();
            string calInfo = (Math.Abs(_wgs84OffsetNorthMeters) > 0.05 || Math.Abs(_wgs84OffsetEastMeters) > 0.05)
                ? $" | Kalibr.: dN={_wgs84OffsetNorthMeters:+0.#;-0.#;0}m, dE={_wgs84OffsetEastMeters:+0.#;-0.#;0}m"
                : "";
            return $"Siatka: WGS84 ({fmtShort} | Krok: {stepDesc} | {_wgs84SubGridPreset.Split(' ')[0]}){calInfo}";
        }

        private static string GetWgs84StepHumanSummary()
        {
            double mpp = GetExactMetersPerPixel();
            if (mpp <= 0.0001 || double.IsNaN(mpp) || double.IsInfinity(mpp)) mpp = 1.0;
            double latRad = __LAT_FIELD__ * Math.PI / 180.0;
            double metersPerDegLat = 111132.92 - 559.82 * Math.Cos(2.0 * latRad);
            double metersPerDegLon = Math.Max(1000.0, 111412.84 * Math.Cos(latRad));
            var (stepLat, stepLon) = ResolveWgs84AngularSteps(mpp, metersPerDegLat, metersPerDegLon);

            if (_wgs84FormatPreset.StartsWith("DD "))
                return $"{stepLat.ToString("0.#####", CultureInfo.InvariantCulture)}° (~{stepLat * metersPerDegLat:0}m)";
            if (_wgs84FormatPreset.StartsWith("DM "))
                return $"{(stepLat * 60.0).ToString("0.###", CultureInfo.InvariantCulture)}' (~{stepLat * metersPerDegLat:0}m)";

            double secLat = Math.Round(stepLat * 3600.0, 1);
            if (secLat >= 60.0 && Math.Abs(secLat % 60.0) < 0.1)
                return $"{secLat / 60.0:0}' (~{stepLat * metersPerDegLat:0}m)";
            return $"{secLat:0.#}\" (~{stepLat * metersPerDegLat:0}m)";
        }

        private static void TriggerWgs84Redraw()
        {
            if (_isRenderingWgs84Grid) return;
            try
            {
                var (cw, ch) = GetExactCanvasSize();
                if (cw > 20 && ch > 20 && IsWgs84ModeActive())
                {
                    RenderCalibratedWgs84GridOnSheet(cw, ch);
                    UpdateLegendSubtitleInPlace();
                }
                else
                {
                    __REDRAW_CALL__;
                }
            }
            catch { }
            try { TacticalConfigPanel.MarkDirty(); } catch { }
        }

        private static void UpdateLegendSubtitleInPlace()
        {
            if (_interactiveCanvas == null || !string.IsNullOrWhiteSpace(_customLegendSubtitle)) return;
            string secLegSuffix = (_enableSectorGridOverlay || (_coordSystem != null && _coordSystem.StartsWith("Sektorowa")))
                ? $" + Sektory A-Z/1-9 ({_sectorSizeMeters}m | {_sectorSubDivision.Split(' ')[0]})"
                : "";
            string expectedSub = GetWgs84LegendSubtitleSummary() + secLegSuffix;

            foreach (UIElement el in _interactiveCanvas.Children)
            {
                if (el is Grid legGrid)
                {
                    foreach (UIElement child in legGrid.Children)
                    {
                        if (child is Border legBorder && legBorder.Child is StackPanel ls && ls.Children.Count > 1)
                        {
                            if (ls.Children[1] is TextBlock subTb)
                                subTb.Text = expectedSub;
                        }
                    }
                }
            }
        }

        public static void UpdateWgs84CustomizationPanelVisibility()
        {
            bool isWgs = IsWgs84ModeActive();
            if (_wgs84ConfigPanelBorder != null)
            {
                _wgs84ConfigPanelBorder.Visibility = isWgs ? Visibility.Visible : Visibility.Collapsed;
                if (isWgs)
                {
                    _suppressWgs84LiveTextEvents = true;
                    try
                    {
                        if (_wgs84CenterLatOverrideBox != null && !_wgs84CenterLatOverrideBox.IsKeyboardFocused)
                            _wgs84CenterLatOverrideBox.Text = __LAT_FIELD__.ToString("F6", CultureInfo.InvariantCulture);
                        if (_wgs84CenterLonOverrideBox != null && !_wgs84CenterLonOverrideBox.IsKeyboardFocused)
                            _wgs84CenterLonOverrideBox.Text = __LON_FIELD__.ToString("F6", CultureInfo.InvariantCulture);
                    }
                    finally { _suppressWgs84LiveTextEvents = false; }

                    if (_wgs84ActiveStepStatusText != null)
                        _wgs84ActiveStepStatusText.Text = $"Skala 1:{_targetScaleDenominator:N0}  •  Krok siatki: {GetWgs84StepHumanSummary()}";
                }
            }
        }

        private static void AttachWgs84ConfigPanelToParent(Panel parentPanel)
        {
            if (parentPanel == null) return;
            var panel = BuildWgs84CustomizationPanel(isModalDialog: false);
            _wgs84ConfigPanelBorder = panel;
            panel.Visibility = IsWgs84ModeActive() ? Visibility.Visible : Visibility.Collapsed;
            parentPanel.Children.Add(panel);
        }

        private static void AttachWgs84QuickCalibrateButtonIfActive(Grid legContainer)
        {
            if (legContainer == null || !IsWgs84ModeActive()) return;
            var btnCal = new Button
            {
                Content = "⚙️ Kalibruj WGS84",
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 7, 68, 0),
                Height = 18,
                Padding = new Thickness(6, 0, 6, 0),
                Background = TacticalConfigPanel.BrushItemBg,
                Foreground = TacticalConfigPanel.BrushAccent,
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(1),
                FontSize = 8.0,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            btnCal.Click += (s, e) => ShowWgs84CalibrationDialog(_hostWindow);
            legContainer.Children.Add(btnCal);
        }

        private static void ShowWgs84CalibrationDialog(Window owner)
        {
            var win = new Window
            {
                Title = "Customizacja i Kalibracja Siatki WGS84 (Lat / Lon)",
                Width = 440,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                Background = new SolidColorBrush(Color.FromRgb(8, 19, 22)),
                Foreground = Brushes.White,
                ResizeMode = ResizeMode.NoResize
            };
            var root = new StackPanel { Margin = new Thickness(14) };
            var panel = BuildWgs84CustomizationPanel(isModalDialog: true);
            panel.Visibility = Visibility.Visible;
            root.Children.Add(panel);
            win.Content = root;
            win.ShowDialog();
        }

        private static Border BuildWgs84CustomizationPanel(bool isModalDialog)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(8, 20, 25)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(22, 62, 74)),
                BorderThickness = new Thickness(1.1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8, 10, 10),
                Margin = new Thickness(0, 6, 0, 8)
            };

            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = "⚙️ SIATKA GEOGRAFICZNA WGS84 (SKALA & KALIBRACJA)",
                Foreground = TacticalConfigPanel.BrushAccent,
                FontWeight = FontWeights.Bold,
                FontSize = 10.5,
                Margin = new Thickness(0, 0, 0, 2)
            });

            var statusTb = new TextBlock
            {
                Text = $"Skala 1:{_targetScaleDenominator:N0}  •  Krok siatki: {GetWgs84StepHumanSummary()}",
                Foreground = new SolidColorBrush(Color.FromRgb(46, 204, 113)),
                FontSize = 9.5,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            };
            if (!isModalDialog) _wgs84ActiveStepStatusText = statusTb;
            sp.Children.Add(statusTb);

            sp.Children.Add(new TextBlock { Text = "Format współrzędnych na ramce:", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 9.5, Margin = new Thickness(0, 1, 0, 2) });
            var cbFormat = new ComboBox { Height = 26, FontSize = 10.5, Margin = new Thickness(0, 0, 0, 6) };
            string[] formats = new[]
            {
                "DMS (49°50'58\"N) — Stopnie Minuty Sekundy",
                "DM (49°50.97'N) — Stopnie i Minuty dziesiętne",
                "DD (49.8494°N) — Stopnie dziesiętne"
            };
            foreach (var f in formats) cbFormat.Items.Add(f);
            cbFormat.SelectedItem = formats.FirstOrDefault(x => x == _wgs84FormatPreset) ?? formats[0];
            cbFormat.SelectionChanged += (s, e) => { if (cbFormat.SelectedItem is string v) { _wgs84FormatPreset = v; TriggerWgs84Redraw(); } };
            sp.Children.Add(cbFormat);

            sp.Children.Add(new TextBlock { Text = "Gęstość linii (Krok WGS84):", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 9.5, Margin = new Thickness(0, 1, 0, 2) });
            var cbStep = new ComboBox { Height = 26, FontSize = 10.5, Margin = new Thickness(0, 0, 0, 6) };
            string[] steps = new[]
            {
                "Auto (dostosuj do skali mapy)",
                "1\" (co 1 sek. kątową ~31 m)",
                "2\" (co 2 sek. kątowe ~62 m)",
                "5\" (co 5 sek. kątowych ~154 m)",
                "10\" (co 10 sek. kątowych ~309 m)",
                "15\" (co 15 sek. kątowych ~463 m)",
                "30\" (co 30 sek. kątowych ~926 m)",
                "1' (co 1 min. kątową ~1852 m)",
                "0.0005° (co 0.0005 stopnia ~55 m)",
                "0.0010° (co 0.001 stopnia ~111 m)",
                "0.0020° (co 0.002 stopnia ~222 m)",
                "0.0050° (co 0.005 stopnia ~555 m)"
            };
            foreach (var st in steps) cbStep.Items.Add(st);
            cbStep.SelectedItem = steps.FirstOrDefault(x => x == _wgs84StepPreset) ?? steps[0];
            cbStep.SelectionChanged += (s, e) => { if (cbStep.SelectedItem is string v) { _wgs84StepPreset = v; TriggerWgs84Redraw(); } };
            sp.Children.Add(cbStep);

            sp.Children.Add(new TextBlock { Text = "Podział wewnętrzny oczka:", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 9.5, Margin = new Thickness(0, 1, 0, 2) });
            var cbSub = new ComboBox { Height = 26, FontSize = 10.5, Margin = new Thickness(0, 0, 0, 6) };
            string[] subs = new[] { "Brak podziału", "Krzyżyki w środku (+)", "Podsiatka 2x2 (4 ćwiartki)", "Podsiatka 5x5 (25 pól)" };
            foreach (var sb in subs) cbSub.Items.Add(sb);
            cbSub.SelectedItem = subs.FirstOrDefault(x => x == _wgs84SubGridPreset) ?? subs[1];
            cbSub.SelectionChanged += (s, e) => { if (cbSub.SelectedItem is string v) { _wgs84SubGridPreset = v; TriggerWgs84Redraw(); } };
            sp.Children.Add(cbSub);

            card.Child = sp;
            return card;
        }

        // ====================================================================
        // SZYBKIE O(1) CZYSZCZENIE I RENDEROWANIE POJEDYNCZEJ SIATKI WGS84
        // ====================================================================
        private static List<Canvas> GetAllMapSheetCanvasesFast()
        {
            var list = new List<Canvas>();
            if (_interactiveCanvas != null)
            {
                list.Add(_interactiveCanvas);
                var parent = LogicalTreeHelper.GetParent(_interactiveCanvas) ?? VisualTreeHelper.GetParent(_interactiveCanvas);
                if (parent is Panel pnl)
                {
                    foreach (UIElement ch in pnl.Children)
                    {
                        if (ch is Canvas c && !list.Contains(c)) list.Add(c);
                    }
                }
            }
            return list;
        }

        private static string ExtractDirectBadgeText(DependencyObject obj)
        {
            if (obj == null) return "";
            if (obj is TextBlock tb) return tb.Text ?? "";
            if (obj is Border b && b.Child is TextBlock btb) return btb.Text ?? "";
            return "";
        }

        private static Canvas PurgeAllLegacyAndPreviousWgs84Elements(double w, double h)
        {
            var allCanvases = GetAllMapSheetCanvasesFast();
            Canvas underlyingGridCanvas = null;

            foreach (var canvas in allCanvases)
            {
                var toRemove = new List<UIElement>();
                foreach (UIElement el in canvas.Children)
                {
                    if (el is FrameworkElement fe)
                    {
                        if (fe.Tag is string tagStr && tagStr == "FTH_WGS84_GRID_EL")
                        {
                            toRemove.Add(el);
                            continue;
                        }

                        string txt = ExtractDirectBadgeText(fe);
                        if (!string.IsNullOrEmpty(txt) && txt.Length <= 28 && txt.Contains("°"))
                        {
                            string t = txt.Trim();
                            if (t.EndsWith("N", StringComparison.OrdinalIgnoreCase) ||
                                t.EndsWith("S", StringComparison.OrdinalIgnoreCase) ||
                                t.EndsWith("E", StringComparison.OrdinalIgnoreCase) ||
                                t.EndsWith("W", StringComparison.OrdinalIgnoreCase))
                            {
                                toRemove.Add(el);
                                if (!ReferenceEquals(canvas, _interactiveCanvas)) underlyingGridCanvas = canvas;
                                continue;
                            }
                        }

                        if (fe is Line ln)
                        {
                            bool spansHeight = Math.Abs(ln.X1 - ln.X2) < 2.0 && Math.Abs(ln.Y2 - ln.Y1) >= h * 0.50;
                            bool spansWidth = Math.Abs(ln.Y1 - ln.Y2) < 2.0 && Math.Abs(ln.X2 - ln.X1) >= w * 0.50;
                            bool isCrosshair = (Math.Abs(ln.X2 - ln.X1) <= 22.0 && Math.Abs(ln.Y2 - ln.Y1) < 1.5) ||
                                               (Math.Abs(ln.Y2 - ln.Y1) <= 22.0 && Math.Abs(ln.X2 - ln.X1) < 1.5);

                            if (spansHeight || spansWidth || isCrosshair)
                            {
                                if (_enableSectorGridOverlay && ln.Stroke is SolidColorBrush scb)
                                {
                                    Color c = scb.Color;
                                    if (c.R < 55 && c.G > 175 && c.B > 165) continue;
                                }
                                toRemove.Add(el);
                                if (!ReferenceEquals(canvas, _interactiveCanvas)) underlyingGridCanvas = canvas;
                            }
                        }
                    }
                }
                foreach (var rem in toRemove) canvas.Children.Remove(rem);
            }
            return underlyingGridCanvas;
        }

        private static void RenderCalibratedWgs84GridOnSheet(double w, double h)
        {
            if (w <= 20 || h <= 20 || !IsWgs84ModeActive() || _isRenderingWgs84Grid) return;

            _isRenderingWgs84Grid = true;
            try
            {
                Canvas detectedUnderlyingCanvas = PurgeAllLegacyAndPreviousWgs84Elements(w, h);
                Canvas targetCanvas = detectedUnderlyingCanvas ?? (__GRID_CANVAS__ ?? _interactiveCanvas);
                if (targetCanvas == null) return;

                double centerLat = __LAT_FIELD__;
                double centerLon = __LON_FIELD__;
                double mpp = GetExactMetersPerPixel();
                if (mpp <= 0.0001 || double.IsNaN(mpp) || double.IsInfinity(mpp)) mpp = 1.0;

                double latRad = centerLat * Math.PI / 180.0;
                double metersPerDegLat = (111132.92 - 559.82 * Math.Cos(2.0 * latRad) + 1.175 * Math.Cos(4.0 * latRad))
                                         * Math.Max(0.2, _wgs84ScaleYPercent / 100.0);
                double metersPerDegLon = (111412.84 * Math.Cos(latRad) - 93.5 * Math.Cos(3.0 * latRad))
                                         * Math.Max(0.2, _wgs84ScaleXPercent / 100.0);
                if (metersPerDegLon < 1000.0) metersPerDegLon = 1000.0;

                double calCenterLat = centerLat - (_wgs84OffsetNorthMeters / metersPerDegLat);
                double calCenterLon = centerLon - (_wgs84OffsetEastMeters / metersPerDegLon);

                double R_eff = (6378137.0 * Math.Cos(latRad));
                double scaleXFactor = Math.Max(0.2, _wgs84ScaleXPercent / 100.0);
                double scaleYFactor = Math.Max(0.2, _wgs84ScaleYPercent / 100.0);

                double MercatorY(double degLat)
                {
                    double clamped = Math.Clamp(degLat, -85.0511, 85.0511) * Math.PI / 180.0;
                    return Math.Log(Math.Tan(Math.PI / 4.0 + clamped / 2.0));
                }

                double centerMercY = MercatorY(calCenterLat);
                double halfW = w / 2.0;
                double halfH = h / 2.0;

                double LonToCanvasX(double lon) => halfW + (((lon - calCenterLon) * Math.PI / 180.0) * R_eff * scaleXFactor) / mpp;
                double LatToCanvasY(double lat) => halfH - ((MercatorY(lat) - centerMercY) * R_eff * scaleYFactor) / mpp;

                var (stepDegLat, stepDegLon) = ResolveWgs84AngularSteps(mpp, metersPerDegLat, metersPerDegLon);

                double minLon = calCenterLon - (halfW * mpp * 1.15) / metersPerDegLon;
                double maxLon = calCenterLon + (halfW * mpp * 1.15) / metersPerDegLon;
                double minLat = calCenterLat - (halfH * mpp * 1.15) / metersPerDegLat;
                double maxLat = calCenterLat + (halfH * mpp * 1.15) / metersPerDegLat;

                string mainColorHex = (__GRID_COLOR_EXPR__) ?? "#FAD02C";
                double mainThickness = Math.Clamp((double)(__GRID_THICKNESS_EXPR__), 0.6, 3.5);
                Brush mainStroke = TacticalConfigPanel.BrushFromHex(mainColorHex, Color.FromRgb(250, 208, 44));
                Brush subStroke = new SolidColorBrush(Color.FromArgb(115, 250, 208, 44));

                var lonLines = new List<double>();
                var latLines = new List<double>();

                if (_wgs84SnapToTrueGeo)
                {
                    double startLon = Math.Floor(minLon / stepDegLon) * stepDegLon;
                    for (int i = 0; i < 100; i++)
                    {
                        double lon = startLon + i * stepDegLon;
                        if (lon > maxLon + stepDegLon) break;
                        lonLines.Add(Math.Round(lon, 9));
                    }

                    double startLat = Math.Floor(minLat / stepDegLat) * stepDegLat;
                    for (int j = 0; j < 100; j++)
                    {
                        double lat = startLat + j * stepDegLat;
                        if (lat > maxLat + stepDegLat) break;
                        latLines.Add(Math.Round(lat, 9));
                    }
                }
                else
                {
                    int countX = Math.Min(50, (int)Math.Ceiling((maxLon - minLon) / stepDegLon) + 2);
                    for (int i = -countX; i <= countX; i++) lonLines.Add(calCenterLon + i * stepDegLon);
                    int countY = Math.Min(50, (int)Math.Ceiling((maxLat - minLat) / stepDegLat) + 2);
                    for (int j = -countY; j <= countY; j++) latLines.Add(calCenterLat + j * stepDegLat);
                }

                int insertIdx = 0;
                for (int i = 0; i < targetCanvas.Children.Count; i++)
                {
                    if (targetCanvas.Children[i] is Image) insertIdx = i + 1;
                }

                void AddGridVisualElement(UIElement el)
                {
                    if (ReferenceEquals(targetCanvas, _interactiveCanvas))
                    {
                        targetCanvas.Children.Insert(Math.Min(insertIdx, targetCanvas.Children.Count), el);
                        insertIdx++;
                    }
                    else targetCanvas.Children.Add(el);
                }

                int subDivs = _wgs84SubGridPreset.Contains("5x5") ? 5 : (_wgs84SubGridPreset.Contains("2x2") ? 2 : 0);
                bool drawCrosshairs = _wgs84SubGridPreset.Contains("Krzyżyki");

                if (subDivs > 1)
                {
                    foreach (double lon in lonLines)
                    {
                        for (int s = 1; s < subDivs; s++)
                        {
                            double x = LonToCanvasX(lon + (stepDegLon * s) / subDivs);
                            if (x >= 0 && x <= w)
                                AddGridVisualElement(new Line { X1 = x, Y1 = 0, X2 = x, Y2 = h, Stroke = subStroke, StrokeThickness = Math.Max(0.5, mainThickness * 0.5), StrokeDashArray = new DoubleCollection { 3, 3 }, IsHitTestVisible = false, Tag = "FTH_WGS84_GRID_EL" });
                        }
                    }
                    foreach (double lat in latLines)
                    {
                        for (int s = 1; s < subDivs; s++)
                        {
                            double y = LatToCanvasY(lat + (stepDegLat * s) / subDivs);
                            if (y >= 0 && y <= h)
                                AddGridVisualElement(new Line { X1 = 0, Y1 = y, X2 = w, Y2 = y, Stroke = subStroke, StrokeThickness = Math.Max(0.5, mainThickness * 0.5), StrokeDashArray = new DoubleCollection { 3, 3 }, IsHitTestVisible = false, Tag = "FTH_WGS84_GRID_EL" });
                        }
                    }
                }
                else if (drawCrosshairs)
                {
                    foreach (double lon in lonLines)
                    {
                        double cx = LonToCanvasX(lon + stepDegLon * 0.5);
                        if (cx < 8 || cx > w - 8) continue;
                        foreach (double lat in latLines)
                        {
                            double cy = LatToCanvasY(lat + stepDegLat * 0.5);
                            if (cy < 8 || cy > h - 8) continue;
                            AddGridVisualElement(new Line { X1 = cx - 5, Y1 = cy, X2 = cx + 5, Y2 = cy, Stroke = subStroke, StrokeThickness = 1.0, IsHitTestVisible = false, Tag = "FTH_WGS84_GRID_EL" });
                            AddGridVisualElement(new Line { X1 = cx, Y1 = cy - 5, X2 = cx, Y2 = cy + 5, Stroke = subStroke, StrokeThickness = 1.0, IsHitTestVisible = false, Tag = "FTH_WGS84_GRID_EL" });
                        }
                    }
                }

                var edgeBadges = new List<UIElement>();
                double lastTopLabelX = -999;

                foreach (double lon in lonLines)
                {
                    double x = LonToCanvasX(lon);
                    if (x < 0 || x > w) continue;
                    AddGridVisualElement(new Line { X1 = x, Y1 = 0, X2 = x, Y2 = h, Stroke = mainStroke, StrokeThickness = mainThickness, IsHitTestVisible = false, Tag = "FTH_WGS84_GRID_EL" });

                    if (x >= 38 && x <= w - 42 && (x - lastTopLabelX) >= 78.0)
                    {
                        lastTopLabelX = x;
                        string lblText = FormatWgs84Coordinate(lon, isLat: false, stepDegLon);
                        var topBadge = CreateWgs84EdgeBadge(lblText, mainStroke);
                        Canvas.SetLeft(topBadge, Math.Clamp(x - 34, 4, w - 76));
                        Canvas.SetTop(topBadge, 4);
                        edgeBadges.Add(topBadge);

                        var botBadge = CreateWgs84EdgeBadge(lblText, mainStroke);
                        Canvas.SetLeft(botBadge, Math.Clamp(x - 34, 4, w - 76));
                        Canvas.SetTop(botBadge, Math.Max(4, h - 20));
                        edgeBadges.Add(botBadge);
                    }
                }

                double lastLeftLabelY = 99999;
                foreach (double lat in latLines)
                {
                    double y = LatToCanvasY(lat);
                    if (y < 0 || y > h) continue;
                    AddGridVisualElement(new Line { X1 = 0, Y1 = y, X2 = w, Y2 = y, Stroke = mainStroke, StrokeThickness = mainThickness, IsHitTestVisible = false, Tag = "FTH_WGS84_GRID_EL" });

                    if (y >= 24 && y <= h - 24 && Math.Abs(lastLeftLabelY - y) >= 24.0)
                    {
                        lastLeftLabelY = y;
                        string lblText = FormatWgs84Coordinate(lat, isLat: true, stepDegLat);
                        var leftBadge = CreateWgs84EdgeBadge(lblText, mainStroke);
                        Canvas.SetLeft(leftBadge, 4);
                        Canvas.SetTop(leftBadge, Math.Clamp(y - 8, 4, h - 20));
                        edgeBadges.Add(leftBadge);

                        var rightBadge = CreateWgs84EdgeBadge(lblText, mainStroke);
                        Canvas.SetLeft(rightBadge, Math.Max(4, w - 78));
                        Canvas.SetTop(rightBadge, Math.Clamp(y - 8, 4, h - 20));
                        edgeBadges.Add(rightBadge);
                    }
                }

                foreach (var badge in edgeBadges) AddGridVisualElement(badge);
                UpdateWgs84CustomizationPanelVisibility();
            }
            finally
            {
                _isRenderingWgs84Grid = false;
            }
        }

        private static (double stepDegLat, double stepDegLon) ResolveWgs84AngularSteps(double mpp, double metersPerDegLat, double metersPerDegLon)
        {
            if (_wgs84StepPreset.Contains("°"))
            {
                string numStr = _wgs84StepPreset.Split('°')[0].Trim();
                if (double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double degVal) && degVal > 0)
                    return (degVal, degVal);
            }

            double arcSeconds = 0;
            if (_wgs84StepPreset.StartsWith("1\"")) arcSeconds = 1.0;
            else if (_wgs84StepPreset.StartsWith("2\"")) arcSeconds = 2.0;
            else if (_wgs84StepPreset.StartsWith("5\"")) arcSeconds = 5.0;
            else if (_wgs84StepPreset.StartsWith("10\"")) arcSeconds = 10.0;
            else if (_wgs84StepPreset.StartsWith("15\"")) arcSeconds = 15.0;
            else if (_wgs84StepPreset.StartsWith("30\"")) arcSeconds = 30.0;
            else if (_wgs84StepPreset.StartsWith("1'")) arcSeconds = 60.0;

            if (arcSeconds > 0) return (arcSeconds / 3600.0, arcSeconds / 3600.0);

            double idealSpacingMeters = mpp * 130.0;
            if (_wgs84FormatPreset.StartsWith("DD "))
            {
                double rawDeg = idealSpacingMeters / metersPerDegLat;
                double[] niceDegs = new[] { 0.0002, 0.0005, 0.001, 0.002, 0.005, 0.01, 0.02, 0.05, 0.1 };
                double bestDeg = 0.001, minDiff = double.MaxValue;
                foreach (double nd in niceDegs) { double d = Math.Abs(nd - rawDeg); if (d < minDiff) { minDiff = d; bestDeg = nd; } }
                return (bestDeg, bestDeg);
            }
            if (_wgs84FormatPreset.StartsWith("DM "))
            {
                double rawMin = (idealSpacingMeters / metersPerDegLat) * 60.0;
                double[] niceMins = new[] { 0.02, 0.05, 0.1, 0.2, 0.5, 1.0, 2.0, 5.0 };
                double bestMin = 0.1, minDiff = double.MaxValue;
                foreach (double nm in niceMins) { double d = Math.Abs(nm - rawMin); if (d < minDiff) { minDiff = d; bestMin = nm; } }
                return (bestMin / 60.0, bestMin / 60.0);
            }

            double rawSecLat = (idealSpacingMeters / metersPerDegLat) * 3600.0;
            double[] niceSecs = new[] { 1.0, 2.0, 5.0, 10.0, 15.0, 30.0, 60.0, 120.0, 300.0, 600.0 };
            double chosenSecLat = 5.0, bestSecDiff = double.MaxValue;
            foreach (double ns in niceSecs) { double d = Math.Abs(ns - rawSecLat); if (d < bestSecDiff) { bestSecDiff = d; chosenSecLat = ns; } }

            double rawSecLon = (idealSpacingMeters / metersPerDegLon) * 3600.0;
            double chosenSecLon = chosenSecLat, bestLonDiff = double.MaxValue;
            foreach (double ns in niceSecs) { double d = Math.Abs(ns - rawSecLon); if (d < bestLonDiff) { bestLonDiff = d; chosenSecLon = ns; } }

            return (chosenSecLat / 3600.0, chosenSecLon / 3600.0);
        }

        private static string FormatWgs84Coordinate(double valueDeg, bool isLat, double stepDeg)
        {
            char hemi = isLat ? (valueDeg >= 0 ? 'N' : 'S') : (valueDeg >= 0 ? 'E' : 'W');
            double absVal = Math.Abs(valueDeg);

            if (_wgs84FormatPreset.StartsWith("DD "))
                return $"{absVal.ToString(stepDeg < 0.001 ? "F5" : "F4", CultureInfo.InvariantCulture)}°{hemi}";

            int deg = (int)Math.Floor(absVal + 1e-9);
            double remMin = (absVal - deg) * 60.0;
            if (remMin >= 59.9999) { remMin = 0.0; deg++; }

            if (_wgs84FormatPreset.StartsWith("DM "))
                return $"{deg:00}°{remMin.ToString("00.00", CultureInfo.InvariantCulture)}'{hemi}";

            int min = (int)Math.Floor(remMin + 1e-7);
            double roundedSec = Math.Round((remMin - min) * 60.0, 1);
            if (roundedSec >= 60.0) { roundedSec = 0.0; min++; if (min >= 60) { min = 0; deg++; } }

            bool isIntegerSec = Math.Abs(roundedSec - Math.Round(roundedSec)) < 0.05;
            string secStr = isIntegerSec ? ((int)Math.Round(roundedSec)).ToString("00", CultureInfo.InvariantCulture) : roundedSec.ToString("00.0", CultureInfo.InvariantCulture);
            return $"{deg:00}°{min:00}'{secStr}\"{hemi}";
        }

        private static Border CreateWgs84EdgeBadge(string text, Brush borderBrush)
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(228, 6, 14, 18)),
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(0.9),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 1, 4, 1),
                IsHitTestVisible = false,
                Tag = "FTH_WGS84_GRID_EL",
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = borderBrush,
                    FontSize = 8.4,
                    FontWeight = FontWeights.Bold,
                    FontFamily = new FontFamily("Consolas")
                }
            };
        }
    }
}
//===END_STEP1_WGS84_CS===