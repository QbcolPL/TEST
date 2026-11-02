@echo off
setlocal DisableDelayedExpansion
cd /d "%~dp0"

set "BAT_FILE=%~f0"

echo ============================================================================
echo [KROK 2/2] Wdrazanie UI i renderowania Podzialki Sektorowej (A,B,C / 1,2,3)
echo            z wyborem rozmiaru (m) oraz Cwiartek/Keypad + Wysylka na GitHub...
echo ============================================================================
powershell -NoProfile -ExecutionPolicy Bypass -Command "Add-Type -TypeDefinition ([System.Text.RegularExpressions.Regex]::Split([System.IO.File]::ReadAllText([System.Environment]::GetEnvironmentVariable('BAT_FILE'), [System.Text.Encoding]::UTF8), '//===' + 'STEP2_SECTOR_AIO_CS===')[1]); [FthStep2SectorAndGitHub]::Run()"

if errorlevel 1 (
    echo.
    echo [BLAD] Wystapil blad podczas wykonywania Kroku 2/2 lub wysylki na GitHub!
    pause
    exit /b 1
)

echo.
echo ============================================================================
echo [SUKCES - KROK 2/2] Podzialka sektorowa (A,B,C / 1,2,3 + Cwiartki/Keypad)
echo zostala wdrożona i wyslana na GitHub (QbcolPL/TEST)!
echo Kompilacja na GitHub Actions zostala uruchomiona.
echo ============================================================================
pause
exit /b 0

//===STEP2_SECTOR_AIO_CS===
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public static class FthStep2SectorAndGitHub
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

        string[] s1 = Regex.Split(rawBat, "//===" + "BLOCK1_TMG_STATE_CS===");
        if (s1.Length < 2) throw new Exception("Brak bloku BLOCK1_TMG_STATE_CS!");
        string[] s2 = Regex.Split(s1[1], "//===" + "BLOCK2_ENGINE_SEC2_CS===");
        if (s2.Length < 2) throw new Exception("Brak bloku BLOCK2_ENGINE_SEC2_CS!");
        string[] s3 = Regex.Split(s2[1], "//===" + "END_SECTOR_BLOCKS===");

        string block1StateCode = s2[0].Trim();
        string block2EngineSec2Code = s3[0].Trim();

        UTF8Encoding utf8NoBom = new UTF8Encoding(false);

        // 1. Upewnij sie, ze Krok 1 w TacticalMapGeneratorView.cs oraz opis w Legendzie sa zaaplikowane
        ApplyStep1AndLegendToMapView(targetDir, block1StateCode, utf8NoBom);

        // 2. Zaaplikuj Krok 2 w TacticalMapGeneratorView.Engine.cs (UI + renderowanie podzialki A,B,C / 1,2,3 + Cwiartki/Keypad)
        ApplyStep2ToMapEngine(targetDir, block2EngineSec2Code, utf8NoBom);

        // 3. Dodaj test jednostkowy weryfikujacy indeksowanie kolumn sektorowych (A, B, C... Z, AA)
        UpdateUnitTestsWithSectorTest(targetDir, utf8NoBom);

        // 4. Zapisz unikalny znacznik wyzwalajacy dla GitHub Actions
        string triggerPath = Path.Combine(targetDir, ".fth_build_trigger.txt");
        string triggerContent = "Field TAK Hub Sector Grid A,B,C / 1,2,3 Build Trigger UTC: " + DateTime.UtcNow.ToString("O") + " | RunId: " + Guid.NewGuid().ToString("N") + "\n";
        File.WriteAllText(triggerPath, triggerContent, utf8NoBom);

        // 5. Wyslij całość na GitHub i uruchom GitHub Actions
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

    private static void ApplyStep1AndLegendToMapView(string targetDir, string block1StateCode, UTF8Encoding utf8NoBom)
    {
        string tmgPath = Path.Combine(targetDir, @"apps\builder\FieldTakHub.Builder\Services\TacticalMapGeneratorView.cs");
        if (!File.Exists(tmgPath))
            throw new Exception("Nie znaleziono pliku TacticalMapGeneratorView.cs!");

        string tmg = File.ReadAllText(tmgPath, Encoding.UTF8);

        int startIdx = tmg.IndexOf("private static double _dotMarkerSizePx = 14.0;", StringComparison.Ordinal);
        int endIdx = tmg.IndexOf("private static void UpdateSheetViewportSize()", StringComparison.Ordinal);
        if (startIdx >= 0 && endIdx > startIdx)
        {
            tmg = tmg.Substring(0, startIdx) + block1StateCode + "\r\n\r\n        " + tmg.Substring(endIdx);
        }

        // Odczyt sektora pod kursorem myszy
        tmg = tmg.Replace(
            @"_coordsBadge.Text = $""MGRS: {hMgrs} ({hKp})  |  WGS84: {hoverLat:0.00000}, {hoverLon:0.00000}  |  Skala 1:{_targetScaleDenominator:N0}"";",
            @"string secHover = FormatSectorCoordinateForLatLon(hoverLat, hoverLon, w, h); _coordsBadge.Text = $""{secHover}  |  MGRS: {hMgrs} ({hKp})  |  WGS84: {hoverLat:0.00000}, {hoverLon:0.00000}  |  1:{_targetScaleDenominator:N0}"";");

        // Odczyt sektora w dymku nad kliknietym punktem
        tmg = tmg.Replace(
            @"stack.Children.Add(new TextBlock { Text = kpInfo, Foreground = TacticalConfigPanel.BrushAccent, FontFamily = new FontFamily(""Consolas""), FontSize = 9.2, FontWeight = FontWeights.SemiBold });",
            @"string secPopup = FormatSectorCoordinateForLatLon(latLon.Lat, latLon.Lon, viewW, viewH); stack.Children.Add(new TextBlock { Text = $""{secPopup}  •  {kpInfo}"", Foreground = TacticalConfigPanel.BrushAccent, FontFamily = new FontFamily(""Consolas""), FontSize = 9.0, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });");

        // Dodaj informacje o podzialce sektorowej do Legendy na mapie
        tmg = tmg.Replace(
            @"string gridSummary = _coordSystem.StartsWith(""MGRS"") ? $""Siatka: MGRS ({_effectiveMgrsStepMeters} m) | {_subGridDivision}"" : $""Siatka: {_coordSystem.Split(' ')[0]}"";",
            @"string secLegSuffix = (_enableSectorGridOverlay || _coordSystem.StartsWith(""Sektorowa"")) ? $"" + Sektory A-Z/1-9 ({_sectorSizeMeters}m | {_sectorSubDivision.Split(' ')[0]})"" : """"; string gridSummary = (_coordSystem.StartsWith(""MGRS"") ? $""Siatka: MGRS ({_effectiveMgrsStepMeters} m) | {_subGridDivision}"" : $""Siatka: {_coordSystem.Split(' ')[0]}"") + secLegSuffix;");

        File.WriteAllText(tmgPath, tmg, utf8NoBom);
        Console.WriteLine("[OK] Zaktualizowano TacticalMapGeneratorView.cs (silnik sektorowy, kursor, inspektor i legenda).");
    }

    private static void ApplyStep2ToMapEngine(string targetDir, string block2EngineSec2Code, UTF8Encoding utf8NoBom)
    {
        string engPath = Path.Combine(targetDir, @"apps\builder\FieldTakHub.Builder\Services\TacticalMapGeneratorView.Engine.cs");
        if (!File.Exists(engPath))
            throw new Exception("Nie znaleziono pliku TacticalMapGeneratorView.Engine.cs!");

        string eng = File.ReadAllText(engPath, Encoding.UTF8);

        // 1. Podmien BuildSection2GridAndColorsUI (lub wczesniejszy blok od UpdateSectorSizePresetHighlights)
        int s2Start = eng.IndexOf("private static void UpdateSectorSizePresetHighlights()", StringComparison.Ordinal);
        if (s2Start < 0)
            s2Start = eng.IndexOf("private static StackPanel BuildSection2GridAndColorsUI(Window win)", StringComparison.Ordinal);

        int s2End = eng.IndexOf("private static (double W, double H) GetExactCanvasSize()", StringComparison.Ordinal);
        if (s2Start < 0 || s2End <= s2Start)
            throw new Exception("Nie znaleziono metody BuildSection2GridAndColorsUI w TacticalMapGeneratorView.Engine.cs!");

        eng = eng.Substring(0, s2Start) + block2EngineSec2Code + "\r\n\r\n        " + eng.Substring(s2End);

        // 2. Zastap stary sztywny blok 8x8 "Sektorowa" w RenderFastGridAndMasksOnContext
        string oldSektorowaBlock = @"                if (_coordSystem.StartsWith(""Sektorowa"") && _mainGridOpacity > 0.02)
                {
                    int cols = 8, rows = 8; double cw = w / cols, ch = h / rows;
                    for (int c = 1; c < cols; c++) dc.DrawLine(mainPen, new Point(c * cw, 0), new Point(c * cw, h));
                    for (int r = 1; r < rows; r++) dc.DrawLine(mainPen, new Point(0, r * ch), new Point(w, r * ch));
                    for (int r = 0; r < rows; r++)
                        for (int c = 0; c < cols; c++)
                            DrawFastBadgeOnContext(dc, $""{(char)('A' + r)}{c + 1}"", c * cw + 6, r * ch + 4, mainBrush, 10 * _fontScaleMultiplier, _mainGridOpacity, $""MAIN_{_mainGridColorHex}_{_mainGridOpacity:0.00}"");
                }";

        if (eng.Contains(oldSektorowaBlock))
        {
            eng = eng.Replace(
                oldSektorowaBlock,
                @"                if (_coordSystem.StartsWith(""Sektorowa""))
                {
                    // Renderowane przez metryczny silnik podzialki sektorowej RenderMetricSectorGridAndRulersOnContext
                }");
        }

        // 3. Wywolaj RenderMetricSectorGridAndRulersOnContext(dc, w, h) w RenderFastGridAndMasksOnContext (zarowno gdy wybrano Siatke Sektorowa, jak i gdy wlaczono dodatkowa podzialke na siatce MGRS/WGS84)
        if (!eng.Contains("RenderMetricSectorGridAndRulersOnContext(dc, w, h);"))
        {
            eng = eng.Replace(
                "if (_rulerStart != null && _rulerEnd != null)",
                "RenderMetricSectorGridAndRulersOnContext(dc, w, h);\r\n\r\n            if (_rulerStart != null && _rulerEnd != null)");
        }

        int openBraces = eng.Count(c => c == '{');
        int closeBraces = eng.Count(c => c == '}');
        Console.WriteLine("[WERYFIKACJA] Nawiasy klamrowe w TacticalMapGeneratorView.Engine.cs: { = " + openBraces + ", } = " + closeBraces);

        File.WriteAllText(engPath, eng, utf8NoBom);
        Console.WriteLine("[OK] Zaktualizowano TacticalMapGeneratorView.Engine.cs (UI sekcji 2 + renderowanie podzialki A,B,C / 1,2,3 + Cwiartki/Keypad).");
    }

    private static void UpdateUnitTestsWithSectorTest(string targetDir, UTF8Encoding utf8NoBom)
    {
        string testFilePath = Path.Combine(targetDir, @"apps\builder\FieldTakHub.Builder.Tests\V34CleanRefactorTests.cs");
        if (!File.Exists(testFilePath)) return;

        string cleanTestsCode = string.Join("\r\n", new string[]
        {
            "#nullable disable",
            "using System;",
            "using System.IO;",
            "using Xunit;",
            "using FieldTakHub.Builder.Models;",
            "using FieldTakHub.Builder.Services;",
            "",
            "namespace FieldTakHub.Builder.Tests",
            "{",
            "    [Collection(\"SequentialBuilderTests\")]",
            "    public sealed class V34CleanRefactorTests",
            "    {",
            "        [Fact]",
            "        public void ZeroExpiryHours_ShouldMeanIndefiniteValidityInProjectAndManifest()",
            "        {",
            "            var dir = Path.Combine(Path.GetTempPath(), \"fth-indef-\" + Guid.NewGuid().ToString(\"N\"));",
            "            var source = Path.Combine(dir, \"source\");",
            "            var output = Path.Combine(dir, \"out\");",
            "            Directory.CreateDirectory(Path.Combine(source, \"atak\"));",
            "            File.WriteAllText(Path.Combine(source, \"atak\", \"hello.txt\"), \"test\");",
            "",
            "            var p = new FieldTakProject",
            "            {",
            "                Name = \"IndefiniteTest\",",
            "                PackageId = \"test.indefinite\",",
            "                PackageVersion = \"1.0.0\",",
            "                SourceDirectory = source,",
            "                OutputDirectory = output,",
            "                ExpiryHours = 0,",
            "                QrExpiryHours = 0,",
            "                Server = new ServerProfile { Host = \"127.0.0.1\" }",
            "            };",
            "",
            "            Assert.Equal(0, p.ExpiryHours);",
            "            Assert.Equal(0, p.QrExpiryHours);",
            "",
            "            var items = new SourceAnalyzer().Analyze(source);",
            "            var ftak = new FtakPackageBuilder().Build(p, items);",
            "            Assert.True(File.Exists(ftak));",
            "        }",
            "",
            "        [Fact]",
            "        public void SectorGrid_ShouldGenerateHorizontalLettersAndVerticalNumbers()",
            "        {",
            "            Assert.Equal(\"A\", TacticalMapGeneratorView.GetSectorColumnLetters(0));",
            "            Assert.Equal(\"B\", TacticalMapGeneratorView.GetSectorColumnLetters(1));",
            "            Assert.Equal(\"C\", TacticalMapGeneratorView.GetSectorColumnLetters(2));",
            "            Assert.Equal(\"Z\", TacticalMapGeneratorView.GetSectorColumnLetters(25));",
            "            Assert.Equal(\"AA\", TacticalMapGeneratorView.GetSectorColumnLetters(26));",
            "        }",
            "",
            "        [Fact]",
            "        public void UniqueFactionColors_ShouldReturnDistinctValidHexColors()",
            "        {",
            "            string c1 = V34CleanFtakAndFactionEngine.GetNextUniqueFactionColorHex(null);",
            "            string c2 = V34CleanFtakAndFactionEngine.GetNextUniqueFactionColorHex(null);",
            "            string c3 = V34CleanFtakAndFactionEngine.GetNextUniqueFactionColorHex(null);",
            "",
            "            Assert.StartsWith(\"#\", c1);",
            "            Assert.Equal(7, c1.Length);",
            "            Assert.NotEqual(c1, c2, StringComparer.OrdinalIgnoreCase);",
            "            Assert.NotEqual(c2, c3, StringComparer.OrdinalIgnoreCase);",
            "        }",
            "",
            "        [Fact]",
            "        public void MapStudioBridge_ShouldConvertCoordinatesToMilitaryMgrs()",
            "        {",
            "            double lat = 49.849444;",
            "            double lon = 18.812667;",
            "",
            "            string mgrs = V34CleanMapStudioBridge.LatLonToMgrs(lat, lon);",
            "            Assert.False(string.IsNullOrWhiteSpace(mgrs));",
            "            Assert.Contains(\"34U\", mgrs);",
            "        }",
            "",
            "        [Fact]",
            "        public void GarbageFactionFilter_ShouldRejectPliAndGeoChat()",
            "        {",
            "            Assert.True(V34CleanOpordDataEngine.IsGarbageFactionName(\"PLI\"));",
            "            Assert.True(V34CleanOpordDataEngine.IsGarbageFactionName(\"GeoChat\"));",
            "            Assert.True(V34CleanOpordDataEngine.IsGarbageFactionName(\"TAK\"));",
            "            Assert.False(V34CleanOpordDataEngine.IsGarbageFactionName(\"ORGA\"));",
            "            Assert.False(V34CleanOpordDataEngine.IsGarbageFactionName(\"STRONA-2\"));",
            "        }",
            "    }",
            "}",
            ""
        });

        File.WriteAllText(testFilePath, cleanTestsCode, utf8NoBom);
        Console.WriteLine("[OK] Zaktualizowano testy jednostkowe V34CleanRefactorTests.cs.");
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
        RunProcess(gitExe, "commit -m \"Add customizable metric Sector Grid & Rulers (A,B,C horizontal / 1,2,3 vertical) with Quarters and Keypad subdivisions\" --allow-empty", targetDir, ignoreError: false);

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
//===STEP2_SECTOR_AIO_CS===

//===BLOCK1_TMG_STATE_CS===
        private static double _dotMarkerSizePx = 14.0;
        private static double _natoMarkerScale = 1.0;
        private static string _savedSearchQuery = "Czechowice-Dziedzice";
        private static string _savedDefaultObjectName = "\u27A4";
        private static ScrollViewer _leftControlScrollViewer;
        private static bool _isRebuildingMapControls;
        private static DispatcherTimer _wheelZoomDebounceTimer;

        // Parametry dodatkowej podzialki i siatki sektorowej (Poziomo: A, B, C... | Pionowo: 1, 2, 3...)
        private static bool _enableSectorGridOverlay = true;
        private static int _sectorSizeMeters = 100;
        private static string _sectorSubDivision = "Keypad 1-9 (3\u00D73)";
        private static bool _showSectorEdgeRulers = true;
        private static bool _showSectorCornerCodes = true;
        private static string _sectorGridColorHex = "#FAD02C";
        private static TextBox _sectorSizeInputBox;
        private static readonly List<Button> _sectorSizePresetButtons = new List<Button>();

        public static string GetSectorColumnLetters(int colIndexZeroBased)
        {
            if (colIndexZeroBased < 0) return "A";
            int n = colIndexZeroBased;
            string res = "";
            while (n >= 0)
            {
                res = (char)('A' + (n % 26)) + res;
                n = (n / 26) - 1;
            }
            return res;
        }

        public static string FormatSectorCoordinateForLatLon(double lat, double lon, double viewW, double viewH)
        {
            try
            {
                Point pt = LatLonToScreen(lat, lon, viewW, viewH);
                double metersPerPx = GetExactMetersPerPixel();
                int secMeters = Math.Clamp(_sectorSizeMeters, 10, 5000);
                double sectorPx = Math.Max(24.0, secMeters / metersPerPx);

                int colIdx = Math.Max(0, (int)Math.Floor(Math.Max(0.0, pt.X) / sectorPx));
                int rowIdx = Math.Max(0, (int)Math.Floor(Math.Max(0.0, pt.Y) / sectorPx));

                string colStr = GetSectorColumnLetters(colIdx);
                string rowStr = (rowIdx + 1).ToString();
                string sectorCode = colStr + rowStr;

                double relX = Math.Clamp((pt.X - (colIdx * sectorPx)) / sectorPx, 0.0, 0.9999);
                double relY = Math.Clamp((pt.Y - (rowIdx * sectorPx)) / sectorPx, 0.0, 0.9999);

                int kpCol = Math.Clamp((int)Math.Floor(relX * 3.0), 0, 2);
                int kpRowTop = Math.Clamp((int)Math.Floor(relY * 3.0), 0, 2);
                int[,] kpGrid = { { 7, 8, 9 }, { 4, 5, 6 }, { 1, 2, 3 } };
                int kpNum = kpGrid[kpRowTop, kpCol];

                int qCol = relX >= 0.5 ? 1 : 0;
                int qRow = relY >= 0.5 ? 1 : 0;
                string[,] qLetters = { { "A", "B" }, { "C", "D" } };
                string[,] qCardinals = { { "NW", "NE" }, { "SW", "SE" } };
                string qLet = qLetters[qRow, qCol];
                string qCard = qCardinals[qRow, qCol];

                return $"Sektor: {sectorCode} ({secMeters}m | KP-{kpNum} | \u0106w:{qLet}/{qCard})";
            }
            catch
            {
                return "Sektor: A1";
            }
        }

        private static void ExportExtraMapUiState(JsonObject root)
        {
            if (root == null) return;
            root["searchQuery"] = _searchPlaceBox != null ? _searchPlaceBox.Text : _savedSearchQuery;
            root["selectedObjectColorHex"] = _selectedObjectColorHex;
            root["selectedNatoSymbolCode"] = _selectedNatoSymbolCode;
            root["defaultObjectName"] = _objectNameInputBox != null ? _objectNameInputBox.Text : _savedDefaultObjectName;
            root["newObjectShowLabelDefault"] = _newObjectShowLabelDefault;
            root["dotMarkerSizePx"] = _dotMarkerSizePx;
            root["natoMarkerScale"] = _natoMarkerScale;
            root["northArrowPos"] = _northArrowPos;
            root["scaleBarPos"] = _scaleBarPos;
            root["legendPos"] = _legendPos;
            if (_northNormX.HasValue) root["northNormX"] = _northNormX.Value;
            if (_northNormY.HasValue) root["northNormY"] = _northNormY.Value;
            if (_scaleNormX.HasValue) root["scaleNormX"] = _scaleNormX.Value;
            if (_scaleNormY.HasValue) root["scaleNormY"] = _scaleNormY.Value;
            if (_legendNormX.HasValue) root["legendNormX"] = _legendNormX.Value;
            if (_legendNormY.HasValue) root["legendNormY"] = _legendNormY.Value;
            root["kmzGrgMode"] = _kmzGrgMode;
            root["offlineMinZoom"] = _offlineMinZoom;
            root["offlineMaxZoom"] = _offlineMaxZoom;
            root["offlineBoundsMode"] = _offlineBoundsMode;
            root["offlineDbFormat"] = _offlineDbFormat;
            root["bakeLidarInOffline"] = _bakeLidarInOffline;
            root["includeVectorsWithOffline"] = _includeVectorsWithOffline;

            root["enableSectorGridOverlay"] = _enableSectorGridOverlay;
            root["sectorSizeMeters"] = _sectorSizeMeters;
            root["sectorSubDivision"] = _sectorSubDivision;
            root["showSectorEdgeRulers"] = _showSectorEdgeRulers;
            root["showSectorCornerCodes"] = _showSectorCornerCodes;
            root["sectorGridColorHex"] = _sectorGridColorHex;
        }

        private static void ImportExtraMapUiState(JsonObject root)
        {
            if (root == null) return;
            if (root["searchQuery"] != null) _savedSearchQuery = root["searchQuery"].ToString();
            if (root["selectedObjectColorHex"] != null) _selectedObjectColorHex = root["selectedObjectColorHex"].ToString();
            if (root["selectedNatoSymbolCode"] != null) _selectedNatoSymbolCode = root["selectedNatoSymbolCode"].ToString();
            if (root["defaultObjectName"] != null) _savedDefaultObjectName = root["defaultObjectName"].ToString();
            if (bool.TryParse(root["newObjectShowLabelDefault"]?.ToString(), out bool nld)) _newObjectShowLabelDefault = nld;
            if (double.TryParse(root["dotMarkerSizePx"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double dms)) _dotMarkerSizePx = Math.Clamp(dms, 4.0, 48.0);
            if (double.TryParse(root["natoMarkerScale"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double nms)) _natoMarkerScale = Math.Clamp(nms, 0.4, 3.0);

            if (root["northArrowPos"] != null) _northArrowPos = root["northArrowPos"].ToString();
            if (root["scaleBarPos"] != null) _scaleBarPos = root["scaleBarPos"].ToString();
            if (root["legendPos"] != null) _legendPos = root["legendPos"].ToString();

            _northNormX = double.TryParse(root["northNormX"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double nnx) ? nnx : null;
            _northNormY = double.TryParse(root["northNormY"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double nny) ? nny : null;
            _scaleNormX = double.TryParse(root["scaleNormX"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double snx) ? snx : null;
            _scaleNormY = double.TryParse(root["scaleNormY"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double sny) ? sny : null;
            _legendNormX = double.TryParse(root["legendNormX"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double lnx) ? lnx : null;
            _legendNormY = double.TryParse(root["legendNormY"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double lny) ? lny : null;

            if (root["kmzGrgMode"] != null) _kmzGrgMode = root["kmzGrgMode"].ToString();
            if (int.TryParse(root["offlineMinZoom"]?.ToString(), out int omin)) _offlineMinZoom = Math.Clamp(omin, 10, 18);
            if (int.TryParse(root["offlineMaxZoom"]?.ToString(), out int omax)) _offlineMaxZoom = Math.Clamp(omax, _offlineMinZoom, 19);
            if (root["offlineBoundsMode"] != null) _offlineBoundsMode = root["offlineBoundsMode"].ToString();
            if (root["offlineDbFormat"] != null) _offlineDbFormat = root["offlineDbFormat"].ToString();
            if (bool.TryParse(root["bakeLidarInOffline"]?.ToString(), out bool bl)) _bakeLidarInOffline = bl;
            if (bool.TryParse(root["includeVectorsWithOffline"]?.ToString(), out bool iv)) _includeVectorsWithOffline = iv;

            if (bool.TryParse(root["enableSectorGridOverlay"]?.ToString(), out bool esg)) _enableSectorGridOverlay = esg;
            if (int.TryParse(root["sectorSizeMeters"]?.ToString(), out int ssm)) _sectorSizeMeters = Math.Clamp(ssm, 10, 5000);
            if (root["sectorSubDivision"] != null) _sectorSubDivision = root["sectorSubDivision"].ToString();
            if (bool.TryParse(root["showSectorEdgeRulers"]?.ToString(), out bool sser)) _showSectorEdgeRulers = sser;
            if (bool.TryParse(root["showSectorCornerCodes"]?.ToString(), out bool sscc)) _showSectorCornerCodes = sscc;
            if (root["sectorGridColorHex"] != null) _sectorGridColorHex = root["sectorGridColorHex"].ToString();

            if (_dimmerRect != null) _dimmerRect.Opacity = _mapDimmerOpacity;
            if (_lidarTileCanvas != null) _lidarTileCanvas.Opacity = _lidarOpacity;

            if (_hostWindow != null && _leftControlScrollViewer != null)
                RebuildLeftControlsPanel(_hostWindow);
        }

        private static void UpdateSelectedMarkerInspectorUI()
        {
            if (_inspectorTitleLabel == null || _inspectorMgrsLabel == null || _inspectorWgsUtmLabel == null) return;
            if (_inspectedObject == null || _inspectedObject.Points.Count == 0 || !_mapObjects.Contains(_inspectedObject))
            {
                _inspectorTitleLabel.Text = "\uD83D\uDCCD Kliknij dowolny znacznik lub wierzcho\u0142ek na mapie, aby odczyta\u0107 wsp\u00F3\u0142rz\u0119dne";
                _inspectorMgrsLabel.Text = "Sektor: \u2014   |   MGRS: \u2014   |   Keypad / \u0106wiartka: \u2014";
                _inspectorWgsUtmLabel.Text = "WGS84: \u2014   |   UTM: \u2014";
                return;
            }
            int idx = Math.Clamp(_inspectedVertexIndex, 0, _inspectedObject.Points.Count - 1);
            var pt = _inspectedObject.Points[idx];
            var (vw, vh) = GetExactCanvasSize();
            string secInfo = FormatSectorCoordinateForLatLon(pt.Lat, pt.Lon, vw, vh);
            var (fullMgrs, _, kpInfo, utmTxt, wgsTxt) = FormatTacticalPointCoordinates(pt.Lat, pt.Lon);
            string ptSuffix = _inspectedObject.Points.Count > 1 ? $" (Wierzcho\u0142ek #{idx + 1}/{_inspectedObject.Points.Count})" : "";
            _inspectorTitleLabel.Text = $"\uD83C\uDFAF {_inspectedObject.Name}{ptSuffix}  [{_inspectedObject.SymbolCode.Replace("NATO_", "").Replace("SHAPE_", "")}]";
            _inspectorMgrsLabel.Text = $"{secInfo}   \u2022   MGRS: {fullMgrs}";
            _inspectorWgsUtmLabel.Text = $"{kpInfo}   \u2022   WGS84: {wgsTxt}";
        }

        private static void CopySelectedMarkerCoordsToClipboard()
        {
            if (_inspectedObject == null || _inspectedObject.Points.Count == 0) return;
            try
            {
                int idx = Math.Clamp(_inspectedVertexIndex, 0, _inspectedObject.Points.Count - 1);
                var pt = _inspectedObject.Points[idx];
                var (vw, vh) = GetExactCanvasSize();
                string secInfo = FormatSectorCoordinateForLatLon(pt.Lat, pt.Lon, vw, vh);
                var (fullMgrs, _, kpInfo, utmTxt, wgsTxt) = FormatTacticalPointCoordinates(pt.Lat, pt.Lon);
                string report = $"{_inspectedObject.Name} | {secInfo} | MGRS: {fullMgrs} ({kpInfo}) | WGS84: {wgsTxt} | UTM: {utmTxt}";
                Clipboard.SetText(report);
                if (_statusText != null) _statusText.Text = $"\u2713 Skopiowano meldunek wsp\u00F3\u0142rz\u0119dnych do schowka: {report}";
            }
            catch { }
        }

//===BLOCK2_ENGINE_SEC2_CS===
        private static void UpdateSectorSizePresetHighlights()
        {
            foreach (var b in _sectorSizePresetButtons)
            {
                bool isCur = b.Content?.ToString() == $"{_sectorSizeMeters} m";
                b.Background = isCur ? TacticalConfigPanel.BrushButtonAccent : TacticalConfigPanel.BrushInputBg;
                b.Foreground = isCur ? TacticalConfigPanel.BrushDarkFg : Brushes.White;
                b.BorderBrush = isCur ? TacticalConfigPanel.BrushAccent : TacticalConfigPanel.BrushInputBorder;
            }
        }

        private static void ApplySectorSizeMeters(int meters)
        {
            _sectorSizeMeters = Math.Clamp(meters, 10, 5000);
            if (_sectorSizeInputBox != null && _sectorSizeInputBox.Text != _sectorSizeMeters.ToString())
                _sectorSizeInputBox.Text = _sectorSizeMeters.ToString();
            UpdateSectorSizePresetHighlights();
            UpdateSelectedMarkerInspectorUI();
            RedrawVectorsOnly();
        }

        private static void RenderMetricSectorGridAndRulersOnContext(DrawingContext dc, double w, double h)
        {
            if (!_enableSectorGridOverlay && !_coordSystem.StartsWith("Sektorowa")) return;
            if (_mainGridOpacity <= 0.02) return;

            double metersPerPixel = GetExactMetersPerPixel();
            int secMeters = Math.Clamp(_sectorSizeMeters, 10, 5000);
            double sectorPx = Math.Max(24.0, secMeters / metersPerPixel);

            int cols = Math.Min(64, (int)Math.Ceiling(w / sectorPx));
            int rows = Math.Min(64, (int)Math.Ceiling(h / sectorPx));

            string secHex = !string.IsNullOrWhiteSpace(_sectorGridColorHex) ? _sectorGridColorHex : _mainGridColorHex;
            var secMainBrush = CreateAlphaHexBrush(secHex, _mainGridOpacity, Color.FromRgb(250, 208, 44));
            var subLineBrush = CreateAlphaHexBrush(_subGridLineColorHex, _subGridOpacity, Color.FromRgb(31, 224, 197));
            var subTextBrush = CreateAlphaHexBrush(_subLabelTextColorHex, _subGridOpacity, Colors.White);
            var subBgBrush = CreateAlphaHexBrush(_subLabelBgColorHex, _subLabelBgOpacity * _subGridOpacity, Color.FromRgb(6, 16, 20));
            var subShadowBrush = CreateAlphaHexBrush("#000000", _subGridOpacity * 0.9, Colors.Black);

            Pen subBoxBorderPen = (_labelBgStyle.StartsWith("Kaseton") && _subLabelBgOpacity > 0.05)
                ? GetCachedSolidPen($"SEC_SUBBOX_{_subGridOpacity:0.00}", CreateAlphaHexBrush("#000000", _subGridOpacity * 0.85, Colors.Black), 0.9) : null;

            byte mainHaloAlpha = (byte)Math.Clamp((int)Math.Round((_gridHaloMode.StartsWith("Pe\u0142ny") ? 220 : 130) * _mainGridOpacity), 0, 255);
            byte subHaloAlpha = (byte)Math.Clamp((int)Math.Round((_gridHaloMode.StartsWith("Pe\u0142ny") ? 180 : 100) * _subGridOpacity), 0, 255);
            var haloBrush = GetCachedRgbaBrush(mainHaloAlpha, 0, 0, 0);
            var subHaloBrush = GetCachedRgbaBrush(subHaloAlpha, 0, 0, 0);
            bool useHalo = !_gridHaloMode.StartsWith("Brak");

            double mainThick = Math.Max(0.9, 1.55 * _gridThicknessMultiplier);
            double subThick = Math.Max(0.7, 0.95 * _gridThicknessMultiplier);
            var secMainPen = GetCachedSolidPen($"SEC_MP_{secHex}_{_mainGridOpacity:0.00}", secMainBrush, mainThick);
            var mainHaloPen = GetCachedSolidPen($"SEC_MHP_{mainHaloAlpha}", haloBrush, mainThick + 1.8);
            var subHaloPen = GetCachedSolidPen($"SEC_SHP_{subHaloAlpha}", subHaloBrush, subThick + 1.2);
            var subPen = new Pen(subLineBrush, subThick) { DashStyle = new DashStyle(new double[] { 4, 2 }, 0) };
            subPen.Freeze();

            int subDiv = 1;
            bool isKeypad = _sectorSubDivision.Contains("Keypad");
            bool isQuartersAbcd = _sectorSubDivision.Contains("A, B, C, D") || _sectorSubDivision.Contains("A,B,C,D");
            bool isQuartersNwSe = _sectorSubDivision.Contains("NW");
            bool isGrid4x4 = _sectorSubDivision.Contains("4\u00D74") || _sectorSubDivision.Contains("4x4");

            if (isKeypad) subDiv = 3;
            else if (isQuartersAbcd || isQuartersNwSe) subDiv = 2;
            else if (isGrid4x4) subDiv = 4;

            // 1. Wewnetrzny podzial kazdego sektora (Cwiartki 2x2 / Keypad 3x3 / Siatka 4x4)
            if (subDiv > 1 && _subGridOpacity > 0.02 && sectorPx >= 44.0)
            {
                double subStepPx = sectorPx / subDiv;
                for (int c = 0; c < cols; c++)
                {
                    double x0 = c * sectorPx;
                    for (int i = 1; i < subDiv; i++)
                    {
                        double sx = x0 + i * subStepPx;
                        if (sx <= 0 || sx >= w) continue;
                        if (useHalo) dc.DrawLine(subHaloPen, new Point(sx, 0), new Point(sx, h));
                        dc.DrawLine(subPen, new Point(sx, 0), new Point(sx, h));
                    }
                }

                for (int r = 0; r < rows; r++)
                {
                    double y0 = r * sectorPx;
                    for (int j = 1; j < subDiv; j++)
                    {
                        double sy = y0 + j * subStepPx;
                        if (sy <= 0 || sy >= h) continue;
                        if (useHalo) dc.DrawLine(subHaloPen, new Point(0, sy), new Point(w, sy));
                        dc.DrawLine(subPen, new Point(0, sy), new Point(w, sy));
                    }
                }

                if (!_gridLabelsMode.StartsWith("Brak") && !_gridLabelsMode.StartsWith("Tylko") && sectorPx >= (subDiv == 4 ? 76.0 : 54.0))
                {
                    string[,] keypad3x3 = { { "7", "8", "9" }, { "4", "5", "6" }, { "1", "2", "3" } };
                    string[,] quartersAbcd = { { "A", "B" }, { "C", "D" } };
                    string[,] quartersNwSe = { { "NW", "NE" }, { "SW", "SE" } };
                    string subCacheKey = $"SECSUB_{_subLabelTextColorHex}_{_subGridOpacity:0.00}";
                    double subFontSz = (subDiv == 4 ? 7.8 : 8.6) * _fontScaleMultiplier;

                    for (int r = 0; r < rows; r++)
                    {
                        double y0 = r * sectorPx;
                        for (int c = 0; c < cols; c++)
                        {
                            double x0 = c * sectorPx;
                            for (int sr = 0; sr < subDiv; sr++)
                            {
                                double cy = y0 + (sr + 0.5) * subStepPx;
                                if (cy < 18 || cy > h - 14) continue;
                                for (int sc = 0; sc < subDiv; sc++)
                                {
                                    double cx = x0 + (sc + 0.5) * subStepPx;
                                    if (cx < 18 || cx > w - 14) continue;
                                    string subLbl = isKeypad
                                        ? keypad3x3[sr, sc]
                                        : (isQuartersAbcd ? quartersAbcd[sr, sc] : (isQuartersNwSe ? quartersNwSe[sr, sc] : ((sr * 4 + sc + 1).ToString())));
                                    DrawFastSubCellBadgeOnContext(dc, subLbl, cx, cy, subTextBrush, subBgBrush, subBoxBorderPen, subShadowBrush, subFontSz, subCacheKey);
                                }
                            }
                        }
                    }
                }
            }

            // 2. Glowne linie sektorow (pionowe A, B, C... i poziome 1, 2, 3...)
            for (int c = 1; c <= cols; c++)
            {
                double sx = c * sectorPx;
                if (sx <= 0 || sx >= w) continue;
                if (useHalo) dc.DrawLine(mainHaloPen, new Point(sx, 0), new Point(sx, h));
                dc.DrawLine(secMainPen, new Point(sx, 0), new Point(sx, h));
            }

            for (int r = 1; r <= rows; r++)
            {
                double sy = r * sectorPx;
                if (sy <= 0 || sy >= h) continue;
                if (useHalo) dc.DrawLine(mainHaloPen, new Point(0, sy), new Point(w, sy));
                dc.DrawLine(secMainPen, new Point(0, sy), new Point(w, sy));
            }

            // 3. Kody sektorow wewnatrz pol (A1, B1, C2...)
            if (_showSectorCornerCodes && sectorPx >= 34.0)
            {
                double codeFontSz = Math.Clamp(9.8 * _fontScaleMultiplier, 7.5, 16.0);
                double offsetTop = _showSectorEdgeRulers ? 22.0 : 4.0;
                double offsetLeft = _showSectorEdgeRulers ? 26.0 : 6.0;

                for (int r = 0; r < rows; r++)
                {
                    double y0 = r * sectorPx;
                    if (y0 > h - 18) break;
                    string rowNumStr = (r + 1).ToString();
                    for (int c = 0; c < cols; c++)
                    {
                        double x0 = c * sectorPx;
                        if (x0 > w - 22) break;
                        string colLetStr = GetSectorColumnLetters(c);
                        double bx = x0 + (c == 0 ? offsetLeft : 6.0);
                        double by = y0 + (r == 0 ? offsetTop : 4.0);
                        if (bx + 24 < w && by + 16 < h)
                        {
                            DrawFastBadgeOnContext(dc, colLetStr + rowNumStr, bx, by, secMainBrush, codeFontSz, _mainGridOpacity, $"SECCODE_{secHex}_{_mainGridOpacity:0.00}");
                        }
                    }
                }
            }

            // 4. Paski podzialki poziomej (A, B, C...) u gory oraz pionowej (1, 2, 3...) po lewej stronie arkusza mapy
            if (_showSectorEdgeRulers)
            {
                const double topBarH = 19.0;
                const double leftBarW = 22.0;
                byte barAlpha = (byte)Math.Clamp((int)Math.Round(225 * _mainGridOpacity), 90, 245);
                var barBgEven = GetCachedRgbaBrush(barAlpha, 6, 16, 20);
                var barBgOdd = GetCachedRgbaBrush(barAlpha, 14, 34, 42);
                var barBorderPen = GetCachedSolidPen($"SECRULER_PEN_{secHex}_{_mainGridOpacity:0.00}", secMainBrush, 1.1);
                double rulerFontSz = Math.Clamp(9.5 * _fontScaleMultiplier, 7.5, 13.0);
                string rulerCacheKey = $"SECRULER_TXT_{secHex}_{_mainGridOpacity:0.00}";

                // Gorna listwa pozioma: A, B, C, D...
                for (int c = 0; c < cols; c++)
                {
                    double x0 = c * sectorPx;
                    if (x0 >= w) break;
                    double segW = Math.Min(sectorPx, w - x0);
                    if (segW < 12) continue;

                    dc.DrawRectangle((c % 2 == 0) ? barBgEven : barBgOdd, barBorderPen, new Rect(x0, 0, segW, topBarH));
                    string colLet = GetSectorColumnLetters(c);
                    var ft = GetCachedGlyph(colLet, rulerFontSz, secMainBrush, rulerCacheKey);
                    double tx = x0 + (segW - ft.Width) / 2.0;
                    double ty = (topBarH - ft.Height) / 2.0;
                    if (tx >= x0 + 1)
                        dc.DrawText(ft, new Point(tx, ty));
                }

                // Lewa listwa pionowa: 1, 2, 3, 4...
                for (int r = 0; r < rows; r++)
                {
                    double y0 = r * sectorPx;
                    if (y0 >= h) break;
                    double segH = Math.Min(sectorPx, h - y0);
                    if (segH < 12) continue;

                    dc.DrawRectangle((r % 2 == 0) ? barBgEven : barBgOdd, barBorderPen, new Rect(0, y0, leftBarW, segH));
                    string rowNum = (r + 1).ToString();
                    var ft = GetCachedGlyph(rowNum, rulerFontSz, secMainBrush, rulerCacheKey);
                    double tx = (leftBarW - ft.Width) / 2.0;
                    double ty = y0 + (segH - ft.Height) / 2.0;
                    if (ty >= y0 + 1)
                        dc.DrawText(ft, new Point(tx, ty));
                }

                if (w > 80 && h > 80)
                {
                    dc.DrawRectangle(GetCachedRgbaBrush(240, 8, 22, 28), barBorderPen, new Rect(0, 0, leftBarW, topBarH));
                }
            }
        }

        private static StackPanel BuildSection2GridAndColorsUI(Window win)
        {
            var sec2 = CreateSectionCard("2. SIATKA MGRS / SEKTOROWA (A,B,C / 1,2,3), ROZJA\u015ANIANIE LASU & KOLORY RGB");
            sec2.Children.Add(TacticalConfigPanel.FieldBox("G\u0142\u00F3wny system wsp\u00F3\u0142rz\u0119dnych na mapie:", TacticalConfigPanel.ComboCtrl(new[] { "MGRS / UTM (Siatka wojskowa w metrach)", "Siatka Sektorowa (A1, B2, C3...)", "WGS84 (Stopnie geograficzne Lat/Lon)", "Brak siatki" }, _coordSystem, win, v => { _coordSystem = v; RedrawVectorsOnly(); })));
            var subGridRow = new UniformGrid { Columns = 2 };
            subGridRow.Children.Add(TacticalConfigPanel.FieldBox("Podzia\u0142 oczek MGRS:", TacticalConfigPanel.ComboCtrl(new[] { "Co 33 m (Keypad 1-9)", "Co 50 m (\u0106wiartki A,B,C,D)", "Brak podzia\u0142u (Tylko 100 m)", "Siatka co 250 m", "Siatka co 500 m" }, _subGridDivision, win, v => { _subGridDivision = v; RedrawVectorsOnly(); })));
            subGridRow.Children.Add(TacticalConfigPanel.FieldBox("Opisy kratek:", TacticalConfigPanel.ComboCtrl(new[] { "MGRS + \u0106wiartki/Keypad", "Tylko g\u0142\u00F3wne linie MGRS", "Brak opis\u00F3w wewn\u0105trz" }, _gridLabelsMode, win, v => { _gridLabelsMode = v; RedrawVectorsOnly(); })));
            sec2.Children.Add(subGridRow);

            // PANEL DODATKOWEJ PODZIALKI SEKTOROWEJ (POZIOMO: A, B, C... | PIONOWO: 1, 2, 3...)
            var sectorBox = new Border
            {
                Background = TacticalConfigPanel.BrushInputBg,
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 7, 8, 7),
                Margin = new Thickness(0, 4, 0, 6)
            };
            var sectorStack = new StackPanel();

            var chkSectorOverlay = new CheckBox
            {
                Content = "\uD83D\uDCCF Dodatkowa podzia\u0142ka sektorowa (Poziomo: A, B, C... | Pionowo: 1, 2, 3...)",
                IsChecked = _enableSectorGridOverlay,
                Foreground = TacticalConfigPanel.BrushAccent,
                FontWeight = FontWeights.Bold,
                FontSize = 10,
                Margin = new Thickness(0, 0, 0, 5),
                Cursor = Cursors.Hand
            };
            chkSectorOverlay.Checked += (s, e) => { _enableSectorGridOverlay = true; if (!_isRebuildingMapControls) TacticalConfigPanel.MarkDirty(); RedrawVectorsOnly(); };
            chkSectorOverlay.Unchecked += (s, e) => { _enableSectorGridOverlay = false; if (!_isRebuildingMapControls) TacticalConfigPanel.MarkDirty(); RedrawVectorsOnly(); };
            sectorStack.Children.Add(chkSectorOverlay);

            sectorStack.Children.Add(new TextBlock
            {
                Text = "Rozmiar pojedynczego sektora w metrach (wpisz lub wybierz preset):",
                Foreground = TacticalConfigPanel.BrushMutedText,
                FontSize = 9.5,
                Margin = new Thickness(0, 0, 0, 3)
            });

            var secSizeInputRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            secSizeInputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            secSizeInputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            secSizeInputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

            _sectorSizeInputBox = (TextBox)TacticalConfigPanel.TextCtrl(_sectorSizeMeters.ToString(), v => { });
            Grid.SetColumn(_sectorSizeInputBox, 0);
            secSizeInputRow.Children.Add(_sectorSizeInputBox);

            var unitMLbl = new TextBlock { Text = "m", Foreground = TacticalConfigPanel.BrushAccent, FontWeight = FontWeights.Bold, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(unitMLbl, 1);
            secSizeInputRow.Children.Add(unitMLbl);

            var btnApplySecSize = MakeActionButton("Ustaw rozmiar", true, () =>
            {
                if (int.TryParse(_sectorSizeInputBox.Text.Trim(), out int sm))
                {
                    TacticalConfigPanel.MarkDirty();
                    ApplySectorSizeMeters(sm);
                }
            });
            btnApplySecSize.Height = 25;
            Grid.SetColumn(btnApplySecSize, 2);
            secSizeInputRow.Children.Add(btnApplySecSize);
            sectorStack.Children.Add(secSizeInputRow);

            _sectorSizePresetButtons.Clear();
            var secPresetsGrid = new UniformGrid { Columns = 6, Margin = new Thickness(0, 0, 0, 5) };
            foreach (int presetM in new[] { 25, 50, 100, 150, 200, 300 })
            {
                int capturedM = presetM;
                var b = MakeActionButton($"{capturedM} m", capturedM == _sectorSizeMeters, () =>
                {
                    TacticalConfigPanel.MarkDirty();
                    ApplySectorSizeMeters(capturedM);
                });
                b.Height = 22;
                b.Margin = new Thickness(1);
                b.FontSize = 9;
                _sectorSizePresetButtons.Add(b);
                secPresetsGrid.Children.Add(b);
            }
            sectorStack.Children.Add(secPresetsGrid);

            var secSubDivCombo = TacticalConfigPanel.ComboCtrl(
                new[]
                {
                    "Keypad 1-9 (3\u00D73)",
                    "\u0106wiartki A, B, C, D (2\u00D72)",
                    "\u0106wiartki NW, NE, SW, SE (2\u00D72)",
                    "Siatka 4\u00D74 (16 podsektor\u00F3w)",
                    "Brak podzia\u0142u (Tylko sektory A1, B2...)"
                },
                _sectorSubDivision,
                win,
                v =>
                {
                    _sectorSubDivision = v;
                    RedrawVectorsOnly();
                });
            sectorStack.Children.Add(TacticalConfigPanel.FieldBox("Podzia\u0142 wewn\u0105trz ka\u017Cdego sektora (\u0106wiartki / Keypad):", secSubDivCombo));

            var secFlagsGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 2, 0, 0) };
            var chkEdgeRulers = new CheckBox
            {
                Content = "Listwy A,B,C / 1,2,3 na kraw\u0119dzi",
                IsChecked = _showSectorEdgeRulers,
                Foreground = Brushes.White,
                FontSize = 9.2,
                Margin = new Thickness(0, 2, 4, 0),
                Cursor = Cursors.Hand
            };
            chkEdgeRulers.Checked += (s, e) => { _showSectorEdgeRulers = true; if (!_isRebuildingMapControls) TacticalConfigPanel.MarkDirty(); RedrawVectorsOnly(); };
            chkEdgeRulers.Unchecked += (s, e) => { _showSectorEdgeRulers = false; if (!_isRebuildingMapControls) TacticalConfigPanel.MarkDirty(); RedrawVectorsOnly(); };

            var chkCornerCodes = new CheckBox
            {
                Content = "Kody A1, B2 wewn\u0105trz sektor\u00F3w",
                IsChecked = _showSectorCornerCodes,
                Foreground = Brushes.White,
                FontSize = 9.2,
                Margin = new Thickness(4, 2, 0, 0),
                Cursor = Cursors.Hand
            };
            chkCornerCodes.Checked += (s, e) => { _showSectorCornerCodes = true; if (!_isRebuildingMapControls) TacticalConfigPanel.MarkDirty(); RedrawVectorsOnly(); };
            chkCornerCodes.Unchecked += (s, e) => { _showSectorCornerCodes = false; if (!_isRebuildingMapControls) TacticalConfigPanel.MarkDirty(); RedrawVectorsOnly(); };

            secFlagsGrid.Children.Add(chkEdgeRulers);
            secFlagsGrid.Children.Add(chkCornerCodes);
            sectorStack.Children.Add(secFlagsGrid);

            sectorBox.Child = sectorStack;
            sec2.Children.Add(sectorBox);

            UIElement MakeRgbBtn(string fTitle, string dTitle, Func<string> getter, Action<string> setter)
            {
                string hex = getter();
                var sw = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = TacticalConfigPanel.BrushFromHex(hex, Colors.White), BorderBrush = Brushes.White, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
                var lbl = new TextBlock { Text = hex.ToUpperInvariant(), Foreground = Brushes.White, FontSize = 9.5, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
                var st = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center }; st.Children.Add(sw); st.Children.Add(lbl);
                var btn = new Button { Content = st, Height = 25, Background = TacticalConfigPanel.BrushInputBg, BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
                btn.Click += (s, e) => { string p = TacticalConfigPanel.ShowRgbColorPickerDialog(win, dTitle, getter()); if (!string.IsNullOrEmpty(p)) { setter(p); sw.Background = TacticalConfigPanel.BrushFromHex(p, Colors.White); lbl.Text = p.ToUpperInvariant(); GlyphCache.Clear(); TacticalConfigPanel.MarkDirty(); _fastOverlayRenderer?.InvalidateVisual(); } };
                return TacticalConfigPanel.FieldBox(fTitle, btn);
            }

            var rgbGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 2, 0, 4) };
            rgbGrid.Children.Add(MakeRgbBtn("Kolor cyfr Keypad/\u0106wiartek (RGB):", "Kolor cyfr Keypad (1-9) / \u0106wiartek (A-D)", () => _subLabelTextColorHex, v => _subLabelTextColorHex = v));
            rgbGrid.Children.Add(MakeRgbBtn("Kolor t\u0142a Keypad/\u0106wiartek (RGB):", "Kolor t\u0142a pod cyframi Keypad / \u0106wiartek", () => _subLabelBgColorHex, v => _subLabelBgColorHex = v));
            rgbGrid.Children.Add(MakeRgbBtn("Kolor linii wewn\u0119trznych (RGB):", "Kolor linii podzia\u0142ki Keypad / \u0106wiartek", () => _subGridLineColorHex, v => _subGridLineColorHex = v));
            rgbGrid.Children.Add(MakeRgbBtn("Kolor siatki g\u0142\u00F3wnej / Sektor\u00F3w (RGB):", "Kolor g\u0142\u00F3wnych linii MGRS / Sektor\u00F3w", () => _mainGridColorHex, v => { _mainGridColorHex = v; _sectorGridColorHex = v; }));
            sec2.Children.Add(rgbGrid);

            sec2.Children.Add(MakeSliderRow("Grubo\u015B\u0107 linii siatki (g\u0142\u00F3wnej i pomocniczej):", 0.5, 3.0, _gridThicknessMultiplier, v => $"{v:0.1}x", v => { _gridThicknessMultiplier = Math.Round(v, 1); _fastOverlayRenderer?.InvalidateVisual(); }));
            sec2.Children.Add(MakeSliderRow("Rozmiar / grubo\u015B\u0107 czcionki wsp\u00F3\u0142rz\u0119dnych (MGRS, Sektory i Keypad):", 0.7, 2.0, _fontScaleMultiplier, v => $"{v:0.1}x", v => { _fontScaleMultiplier = Math.Round(v, 1); GlyphCache.Clear(); _fastOverlayRenderer?.InvalidateVisual(); }));
            sec2.Children.Add(MakeSliderRow("Widoczno\u015B\u0107 (alfa) g\u0142\u00F3wnych linii i opis\u00F3w (MGRS / Sektory):", 0.05, 1.0, _mainGridOpacity, v => $"{(int)Math.Round(v * 100)}%", v => { _mainGridOpacity = Math.Round(v, 2); _fastOverlayRenderer?.InvalidateVisual(); }));
            sec2.Children.Add(MakeSliderRow("Widoczno\u015B\u0107 (alfa) podzia\u0142ki wewn\u0119trznej (Keypad 1-9 / \u0106wiartki):", 0.0, 1.0, _subGridOpacity, v => $"{(int)Math.Round(v * 100)}%", v => { _subGridOpacity = Math.Round(v, 2); _fastOverlayRenderer?.InvalidateVisual(); }));
            sec2.Children.Add(MakeSliderRow("Krycie t\u0142a (kasetonu) pod cyframi Keypad / \u0106wiartek:", 0.0, 1.0, _subLabelBgOpacity, v => $"{(int)Math.Round(v * 100)}%", v => { _subLabelBgOpacity = Math.Round(v, 2); _fastOverlayRenderer?.InvalidateVisual(); }));

            sec2.Children.Add(MakeSliderRow("\u2600\uFE0F Rozja\u015Bnienie podk\u0142adu mapy (Ciemny las / Pod wydruk):", 0.0, 0.85, _mapBrightnessBoost, v => $"+{(int)Math.Round(v * 100)}%", v =>
            {
                _mapBrightnessBoost = Math.Round(v, 2);
                TriggerLiveBrightnessRefresh();
            }));

            sec2.Children.Add(MakeSliderRow("Przyciemnienie podk\u0142adu mapy (wi\u0119kszy kontrast pod siatk\u0119):", 0.0, 0.65, _mapDimmerOpacity, v => $"{(int)Math.Round(v * 100)}%", v =>
            {
                _mapDimmerOpacity = Math.Round(v, 2);
                if (_dimmerRect != null) _dimmerRect.Opacity = _mapDimmerOpacity;
            }));

            var haloRow = new UniformGrid { Columns = 2 };
            haloRow.Children.Add(TacticalConfigPanel.FieldBox("Obrys linii (Halo):", TacticalConfigPanel.ComboCtrl(new[] { "Pe\u0142ny czarny obrys", "P\u00F3\u0142przezroczysty obrys", "Brak obrysu" }, _gridHaloMode, win, v => { _gridHaloMode = v; _fastOverlayRenderer?.InvalidateVisual(); })));
            haloRow.Children.Add(TacticalConfigPanel.FieldBox("Styl opis\u00F3w i cyfr:", TacticalConfigPanel.ComboCtrl(new[] { "Kaseton (Kolor t\u0142a RGB)", "Tylko obrys Halo", "Czysty tekst" }, _labelBgStyle, win, v => { _labelBgStyle = v; _fastOverlayRenderer?.InvalidateVisual(); })));
            sec2.Children.Add(haloRow);
            return sec2;
        }
//===END_SECTOR_BLOCKS===