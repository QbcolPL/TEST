@echo off
setlocal DisableDelayedExpansion
cd /d "%~dp0"

set "BAT_FILE=%~f0"

echo ============================================================================
echo [1/3] Usuwanie placeholdera siatki sektorowej z ComboBox oraz wdrazanie
echo       rzeczywistej, kalibrowanej siatki WGS84 w Generatorze Map...
echo ============================================================================
powershell -NoProfile -ExecutionPolicy Bypass -Command "Add-Type -TypeDefinition ([System.Text.RegularExpressions.Regex]::Split([System.IO.File]::ReadAllText([System.Environment]::GetEnvironmentVariable('BAT_FILE'), [System.Text.Encoding]::UTF8), '//===' + 'FIX_WGS84_AND_SECTOR_CS===')[1]); [FthFixWgs84AndSector]::Run()"

if errorlevel 1 (
    echo.
    echo [BLAD] Wystapil blad podczas aktualizacji plikow lub wysylki na GitHub!
    pause
    exit /b 1
)

echo.
echo ============================================================================
echo [SUKCES] Poprawki siatki WGS84 i menu rozwijanego zostaly wdrozone
echo i wyslane na GitHub (QbcolPL/TEST)! Kompilacja na GitHub Actions uruchomiona.
echo ============================================================================
pause
exit /b 0

//===FIX_WGS84_AND_SECTOR_CS===
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public static class FthFixWgs84AndSector
{
    private const string RepoRemoteUrl = "https://github.com/QbcolPL/TEST.git";
    private const string RepoActionsUrl = "https://github.com/QbcolPL/TEST/actions";

    public static void Run()
    {
        string currentDir = Directory.GetCurrentDirectory();
        string targetDir = ResolveTargetProjectDirectory(currentDir);
        Console.WriteLine("[OK] Katalog roboczy projektu: " + targetDir);

        string servicesDir = Path.Combine(targetDir, @"apps\builder\FieldTakHub.Builder\Services");
        string tmgPath = Path.Combine(servicesDir, "TacticalMapGeneratorView.cs");
        string engPath = Path.Combine(servicesDir, "TacticalMapGeneratorView.Engine.cs");
        string wgsPath = Path.Combine(servicesDir, "TacticalMapGeneratorView.Wgs84.cs");

        if (!File.Exists(tmgPath))
            throw new Exception("Nie znaleziono pliku TacticalMapGeneratorView.cs w: " + servicesDir);

        UTF8Encoding utf8NoBom = new UTF8Encoding(false);
        string tmg = File.ReadAllText(tmgPath, Encoding.UTF8);
        string eng = File.Exists(engPath) ? File.ReadAllText(engPath, Encoding.UTF8) : "";

        // --------------------------------------------------------------------
        // KROK 1: Usuniecie placeholdera siatki sektorowej z rozwijanego menu
        // --------------------------------------------------------------------
        tmg = RemoveSectorPlaceholderFromDropdowns(tmg);
        if (!string.IsNullOrEmpty(eng))
        {
            eng = RemoveSectorPlaceholderFromDropdowns(eng);
        }

        // Jezeli w jakimkolwiek innym pliku w Services byl placeholder siatki sektorowej w ComboBox:
        foreach (string csFile in Directory.GetFiles(servicesDir, "*.cs"))
        {
            string fn = Path.GetFileName(csFile);
            if (fn.Equals("TacticalMapGeneratorView.cs", StringComparison.OrdinalIgnoreCase) ||
                fn.Equals("TacticalMapGeneratorView.Engine.cs", StringComparison.OrdinalIgnoreCase) ||
                fn.Equals("TacticalMapGeneratorView.Wgs84.cs", StringComparison.OrdinalIgnoreCase))
                continue;

            string content = File.ReadAllText(csFile, Encoding.UTF8);
            string cleaned = RemoveSectorPlaceholderFromDropdowns(content);
            if (cleaned != content)
            {
                File.WriteAllText(csFile, cleaned, utf8NoBom);
                Console.WriteLine("[OK] Usunieto placeholder siatki sektorowej w: " + fn);
            }
        }

        // --------------------------------------------------------------------
        // KROK 2: Wykrycie nazw pol Lat/Lon, Canvasu siatki i metody odrysowania
        // --------------------------------------------------------------------
        string combined = tmg + "\n" + eng;

        string latField = DetectLatFieldName(combined);
        string lonField = DetectLonFieldName(combined, latField);
        string gridCanvasField = DetectGridCanvasFieldName(combined);
        string redrawCallExpr = DetectRedrawCallExpression(combined);
        string gridColorField = DetectGridColorField(combined);
        string gridThicknessField = DetectGridThicknessField(combined);

        Console.WriteLine("[INFO] Wykryte pola mapy: Lat=" + latField + ", Lon=" + lonField +
                          ", Canvas=" + gridCanvasField + ", Redraw=" + redrawCallExpr);

        // --------------------------------------------------------------------
        // KROK 3: Podpiecie silnika WGS84 i panelu customizacji w TacticalMapGeneratorView
        // --------------------------------------------------------------------
        // A) Upewnij sie, ze klasa w TacticalMapGeneratorView.cs jest oznaczona jako partial
        if (!Regex.IsMatch(tmg, @"partial\s+class\s+TacticalMapGeneratorView\b"))
        {
            tmg = Regex.Replace(tmg, @"(\bclass\s+TacticalMapGeneratorView\b)", "partial $1");
        }

        // B) Wstrzyknij wywolanie RenderCalibratedWgs84GridOnSheet(w, h) na poczatku DrawDraggableCornerElements
        string hookCall = "if (_coordSystem != null && _coordSystem.StartsWith(\"WGS84\", StringComparison.OrdinalIgnoreCase)) { RenderCalibratedWgs84GridOnSheet(w, h); }";
        if (!tmg.Contains("RenderCalibratedWgs84GridOnSheet(w, h)"))
        {
            string targetSig = "private static void DrawDraggableCornerElements(double w, double h)";
            int sigIdx = tmg.IndexOf(targetSig, StringComparison.Ordinal);
            if (sigIdx >= 0)
            {
                int openBrace = tmg.IndexOf('{', sigIdx);
                if (openBrace >= 0)
                {
                    tmg = tmg.Substring(0, openBrace + 1) +
                          "\r\n            " + hookCall +
                          tmg.Substring(openBrace + 1);
                    Console.WriteLine("[OK] Podpieto RenderCalibratedWgs84GridOnSheet(w, h) w DrawDraggableCornerElements.");
                }
            }
        }

        // C) Dodaj przycisk 'Kalibruj WGS84' obok przycisku 'Edytuj' na Legendzie (gdy aktywny jest WGS84)
        string legBtnAnchor = "legContainer.Children.Add(btnEditLeg);";
        string wgsBtnHook = "legContainer.Children.Add(btnEditLeg);\r\n                    AttachWgs84QuickCalibrateButtonIfActive(legContainer);";
        if (tmg.Contains(legBtnAnchor) && !tmg.Contains("AttachWgs84QuickCalibrateButtonIfActive"))
        {
            tmg = tmg.Replace(legBtnAnchor, wgsBtnHook);
            Console.WriteLine("[OK] Dodano przycisk szybkiej kalibracji WGS84 na pasku Legendy.");
        }

        // D) Podpiecie panelu customizacji WGS84 w lewym panelu konfiguracji pod wyborem _coordSystem
        tmg = InjectWgs84PanelIntoConfigUi(tmg);
        if (!string.IsNullOrEmpty(eng))
        {
            eng = InjectWgs84PanelIntoConfigUi(eng);
        }

        // E) Zaktualizuj opis podtytulu w Legendzie dla WGS84, aby pokazywal wybrany format i krok WGS84
        string oldAutoSub = "string autoSub = (_coordSystem.StartsWith(\"MGRS\") ? $\"Siatka: MGRS ({_effectiveMgrsStepMeters} m) | {_subGridDivision}\" : $\"Siatka: {_coordSystem.Split(' ')[0]}\") + secLegSuffix;";
        string newAutoSub = "string autoSub = (_coordSystem.StartsWith(\"MGRS\") ? $\"Siatka: MGRS ({_effectiveMgrsStepMeters} m) | {_subGridDivision}\" : GetWgs84LegendSubtitleSummary()) + secLegSuffix;";
        if (tmg.Contains(oldAutoSub))
        {
            tmg = tmg.Replace(oldAutoSub, newAutoSub);
        }

        // Weryfikacja nawiasow w TacticalMapGeneratorView.cs
        int openTmg = tmg.Count(c => c == '{');
        int closeTmg = tmg.Count(c => c == '}');
        Console.WriteLine("[WERYFIKACJA] Nawiasy w TacticalMapGeneratorView.cs: { = " + openTmg + ", } = " + closeTmg);
        if (openTmg != closeTmg)
            throw new Exception("Niezgodnosc nawiasow klamrowych w TacticalMapGeneratorView.cs!");

        File.WriteAllText(tmgPath, tmg, utf8NoBom);
        if (!string.IsNullOrEmpty(eng))
        {
            int openEng = eng.Count(c => c == '{');
            int closeEng = eng.Count(c => c == '}');
            Console.WriteLine("[WERYFIKACJA] Nawiasy w TacticalMapGeneratorView.Engine.cs: { = " + openEng + ", } = " + closeEng);
            if (openEng != closeEng)
                throw new Exception("Niezgodnosc nawiasow klamrowych w TacticalMapGeneratorView.Engine.cs!");
            File.WriteAllText(engPath, eng, utf8NoBom);
        }

        // --------------------------------------------------------------------
        // KROK 4: Wygenerowanie pliku TacticalMapGeneratorView.Wgs84.cs
        // --------------------------------------------------------------------
        string batFile = Environment.GetEnvironmentVariable("BAT_FILE");
        string rawBat = File.ReadAllText(batFile, Encoding.UTF8);

        string[] parts = Regex.Split(rawBat, "//===" + "PAYLOAD_WGS84_PARTIAL_CS===");
        if (parts.Length < 2)
            throw new Exception("Brak bloku PAYLOAD_WGS84_PARTIAL_CS w pliku .bat!");
        string[] endParts = Regex.Split(parts[1], "//===" + "END_WGS84_PARTIAL_CS===");
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
        Console.WriteLine("[OK] Zapisano nowy modul kalibracji i renderowania WGS84: TacticalMapGeneratorView.Wgs84.cs");

        string triggerPath = Path.Combine(targetDir, ".fth_build_trigger.txt");
        File.WriteAllText(triggerPath, "Field TAK Hub WGS84 Calibration & Sector Menu Cleanup UTC: " + DateTime.UtcNow.ToString("O") + " | RunId: " + Guid.NewGuid().ToString("N") + "\n", utf8NoBom);

        PushAndTriggerGitHubActions(targetDir);
    }

    private static string RemoveSectorPlaceholderFromDropdowns(string code)
    {
        // 1. Usun element tablicy/listy zaczynajacy sie od "Sektorowa..." (np. w new[] { "MGRS...", "WGS84...", "Sektorowa..." })
        code = Regex.Replace(
            code,
            @",\s*""Sektorowa[^""]*""",
            "",
            RegexOptions.IgnoreCase);

        code = Regex.Replace(
            code,
            @"""Sektorowa[^""]*""\s*,\s*",
            "",
            RegexOptions.IgnoreCase);

        // 2. Usun bezposrednie dodawanie pozycji "Sektorowa..." do ComboBoxa (.Items.Add("Sektorowa..."))
        code = Regex.Replace(
            code,
            @"[^\r\n;]*\.Items\.Add\(\s*""Sektorowa[^""]*""\s*\)\s*;\r?\n?",
            "",
            RegexOptions.IgnoreCase);

        // 3. Usun ewentualne ComboBoxItem z napisem "Sektorowa..." lub "Placeholder"
        code = Regex.Replace(
            code,
            @"[^\r\n;]*\.Items\.Add\(\s*new\s+ComboBoxItem\s*\{[^}]*""Sektorowa[^""]*""[^}]*\}\s*\)\s*;\r?\n?",
            "",
            RegexOptions.IgnoreCase);

        code = Regex.Replace(
            code,
            @",\s*""[^""]*placeholder[^""]*""",
            "",
            RegexOptions.IgnoreCase);

        return code;
    }

    private static string InjectWgs84PanelIntoConfigUi(string code)
    {
        if (code.Contains("AttachWgs84ConfigPanelToParent"))
            return code;

        // Znajdz miejsce, gdzie _coordSystem jest przypisywany po zmianie ComboBoxa
        // i odswiezaj widocznosc panelu WGS84
        string pattern = @"(_coordSystem\s*=\s*[^;]+;)";
        if (Regex.IsMatch(code, pattern))
        {
            code = Regex.Replace(
                code,
                pattern,
                "$1 UpdateWgs84CustomizationPanelVisibility();");
        }

        // Znajdz miejsce dodania ComboBoxa systemu wspolrzednych lub sekcji siatki i wstrzyknij panel WGS84
        // Szukamy lancucha tworzacego ComboBox z "WGS84"
        int wgsIdx = code.IndexOf("\"WGS84", StringComparison.OrdinalIgnoreCase);
        if (wgsIdx >= 0)
        {
            // Szukamy najblizszego .Children.Add(...) po zdefiniowaniu ComboBoxa z "WGS84"
            int searchEnd = Math.Min(code.Length, wgsIdx + 1800);
            string window = code.Substring(wgsIdx, searchEnd - wgsIdx);
            Match mAdd = Regex.Match(window, @"([a-zA-Z0-9_]+)\.Children\.Add\(([a-zA-Z0-9_]+)\)\s*;");
            if (mAdd.Success)
            {
                string parentPanelVar = mAdd.Groups[1].Value;
                int insertPos = wgsIdx + mAdd.Index + mAdd.Length;
                string injection = "\r\n            AttachWgs84ConfigPanelToParent(" + parentPanelVar + ");";
                code = code.Substring(0, insertPos) + injection + code.Substring(insertPos);
                Console.WriteLine("[OK] Wstrzyknieto panel customizacji WGS84 do kontenera: " + parentPanelVar);
            }
        }

        return code;
    }

    private static string DetectLatFieldName(string combined)
    {
        // Najpierw sprawdz w GetExactMetersPerPixel()
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
            if (Regex.IsMatch(combined, @"\b" + Regex.Escape(c) + @"\b"))
                return c;
        }

        Match mAny = Regex.Match(combined, @"private\s+static\s+double\s+(_[a-zA-Z0-9_]*Lat[a-zA-Z0-9_]*)\b");
        if (mAny.Success) return mAny.Groups[1].Value;

        return "_centerLat";
    }

    private static string DetectLonFieldName(string combined, string latField)
    {
        string guessedLon = latField.Replace("Lat", "Lon").Replace("lat", "lon");
        if (Regex.IsMatch(combined, @"\b" + Regex.Escape(guessedLon) + @"\b"))
            return guessedLon;

        string guessedLng = latField.Replace("Lat", "Lng").Replace("lat", "lng");
        if (Regex.IsMatch(combined, @"\b" + Regex.Escape(guessedLng) + @"\b"))
            return guessedLng;

        string[] candidates = new[] { "_centerLon", "_currentLon", "_mapCenterLon", "_targetLon", "_lon", "_centerLng" };
        foreach (string c in candidates)
        {
            if (Regex.IsMatch(combined, @"\b" + Regex.Escape(c) + @"\b"))
                return c;
        }

        return "_centerLon";
    }

    private static string DetectGridCanvasFieldName(string combined)
    {
        string[] candidates = new[] { "_gridCanvas", "_overlayCanvas", "_mapGridCanvas", "_interactiveCanvas" };
        foreach (string c in candidates)
        {
            if (Regex.IsMatch(combined, @"\b" + Regex.Escape(c) + @"\b"))
                return c;
        }
        return "_interactiveCanvas";
    }

    private static string DetectRedrawCallExpression(string combined)
    {
        // Szukamy bezparametrowej metody odrysowujacej mape/siatke
        string[] candidates = new[]
        {
            "RedrawMapAndGrid()",
            "RedrawGridAndOverlays()",
            "UpdateMapPreview()",
            "RefreshMapCanvas()",
            "RenderMapSheet()",
            "RedrawAll()",
            "RedrawGrid()",
            "UpdateMap()"
        };

        foreach (string cand in candidates)
        {
            string name = cand.Substring(0, cand.Length - 2);
            if (Regex.IsMatch(combined, @"private\s+static\s+void\s+" + Regex.Escape(name) + @"\s*\(\s*\)"))
                return cand;
        }

        // Szukamy metody void BezParametrow(), ktora wywoluje DrawDraggableCornerElements
        MatchCollection methods = Regex.Matches(combined, @"private\s+static\s+void\s+([a-zA-Z0-9_]+)\s*\(\s*\)");
        foreach (Match m in methods)
        {
            string mName = m.Groups[1].Value;
            if (mName.Contains("Render") || mName.Contains("Redraw") || mName.Contains("Update") || mName.Contains("Refresh") || mName.Contains("Draw"))
            {
                return mName + "()";
            }
        }

        return "TacticalConfigPanel.MarkDirty()";
    }

    private static string DetectGridColorField(string combined)
    {
        string[] candidates = new[] { "_gridColorHex", "_mainGridColorHex", "_mgrsColorHex" };
        foreach (string c in candidates)
        {
            if (Regex.IsMatch(combined, @"string\s+" + Regex.Escape(c) + @"\b"))
                return c;
        }
        return "\"#FAD02C\"";
    }

    private static string DetectGridThicknessField(string combined)
    {
        string[] candidates = new[] { "_gridLineThickness", "_mainGridThickness", "_lineThickness" };
        foreach (string c in candidates)
        {
            if (Regex.IsMatch(combined, @"double\s+" + Regex.Escape(c) + @"\b"))
                return c;
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
        RunProcess(gitExe, "commit -m \"Fix WGS84 real coordinates & grid calibration panel and remove Sector Grid placeholder from dropdown\" --allow-empty", targetDir, ignoreError: false);

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
//===FIX_WGS84_AND_SECTOR_CS===

//===PAYLOAD_WGS84_PARTIAL_CS===
#pragma warning disable CS0414, CS0649, CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625, CS8629
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FieldTakHub.Builder.Services
{
    public static partial class TacticalMapGeneratorView
    {
        // Parametry customizacji i kalibracji siatki WGS84
        private static string _wgs84StepPreset = "Auto (wg skali)";
        private static string _wgs84FormatPreset = "DMS (49°50'58.0\"N)";
        private static string _wgs84SubGridPreset = "Krzyżyki w środku (+)";
        private static bool _wgs84SnapToTrueGeo = true;
        private static double _wgs84OffsetNorthMeters = 0.0;
        private static double _wgs84OffsetEastMeters = 0.0;
        private static double _wgs84ScaleXPercent = 100.0;
        private static double _wgs84ScaleYPercent = 100.0;
        private static double _wgs84CustomStepSeconds = 5.0;

        private static Border _wgs84ConfigPanelBorder;
        private static TextBox _wgs84OffsetNBox;
        private static TextBox _wgs84OffsetEBox;
        private static TextBox _wgs84ScaleXBox;
        private static TextBox _wgs84ScaleYBox;
        private static TextBox _wgs84CenterLatOverrideBox;
        private static TextBox _wgs84CenterLonOverrideBox;

        private static string GetWgs84LegendSubtitleSummary()
        {
            string fmtShort = _wgs84FormatPreset.Split(' ')[0];
            string stepShort = _wgs84StepPreset.StartsWith("Auto") ? "Auto" : _wgs84StepPreset.Split(' ')[0];
            string calInfo = (Math.Abs(_wgs84OffsetNorthMeters) > 0.05 || Math.Abs(_wgs84OffsetEastMeters) > 0.05)
                ? $" | Kalibracja: dN={_wgs84OffsetNorthMeters:+0.#;-0.#;0}m, dE={_wgs84OffsetEastMeters:+0.#;-0.#;0}m"
                : "";
            return $"Siatka: WGS84 ({fmtShort} | Krok: {stepShort} | {_wgs84SubGridPreset.Split(' ')[0]}){calInfo}";
        }

        private static void TriggerWgs84Redraw()
        {
            try
            {
                __REDRAW_CALL__;
            }
            catch { }
            try
            {
                TacticalConfigPanel.MarkDirty();
            }
            catch { }
        }

        public static void UpdateWgs84CustomizationPanelVisibility()
        {
            if (_wgs84ConfigPanelBorder != null)
            {
                bool isWgs = _coordSystem != null && _coordSystem.StartsWith("WGS84", StringComparison.OrdinalIgnoreCase);
                _wgs84ConfigPanelBorder.Visibility = isWgs ? Visibility.Visible : Visibility.Collapsed;
                if (isWgs)
                {
                    if (_wgs84CenterLatOverrideBox != null)
                        _wgs84CenterLatOverrideBox.Text = __LAT_FIELD__.ToString("F6", CultureInfo.InvariantCulture);
                    if (_wgs84CenterLonOverrideBox != null)
                        _wgs84CenterLonOverrideBox.Text = __LON_FIELD__.ToString("F6", CultureInfo.InvariantCulture);
                }
            }
        }

        private static void AttachWgs84ConfigPanelToParent(Panel parentPanel)
        {
            if (parentPanel == null) return;
            var panel = BuildWgs84CustomizationPanel(isModalDialog: false);
            _wgs84ConfigPanelBorder = panel;
            bool isWgs = _coordSystem != null && _coordSystem.StartsWith("WGS84", StringComparison.OrdinalIgnoreCase);
            panel.Visibility = isWgs ? Visibility.Visible : Visibility.Collapsed;
            parentPanel.Children.Add(panel);
        }

        private static void AttachWgs84QuickCalibrateButtonIfActive(Grid legContainer)
        {
            if (legContainer == null) return;
            if (_coordSystem == null || !_coordSystem.StartsWith("WGS84", StringComparison.OrdinalIgnoreCase))
                return;

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
                Cursor = Cursors.Hand,
                ToolTip = "Dostosuj gęstość, format (DMS/DM/DD) oraz przesunięcie i kalibrację siatki WGS84 do mapy"
            };
            btnCal.Click += (s, e) => ShowWgs84CalibrationDialog(_hostWindow);
            legContainer.Children.Add(btnCal);
        }

        private static void ShowWgs84CalibrationDialog(Window owner)
        {
            var win = new Window
            {
                Title = "Customizacja i Kalibracja Siatki WGS84 (Lat / Lon)",
                Width = 460,
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

            var closeBtn = new Button
            {
                Content = "✓ ZAMKNIJ OKNO KALIBRACJI WGS84",
                Height = 34,
                Margin = new Thickness(0, 10, 0, 0),
                Background = TacticalConfigPanel.BrushAccent,
                Foreground = new SolidColorBrush(Color.FromRgb(6, 14, 18)),
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            closeBtn.Click += (s, e) => win.Close();
            root.Children.Add(closeBtn);

            win.Content = root;
            win.ShowDialog();
        }

        private static Border BuildWgs84CustomizationPanel(bool isModalDialog)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(210, 10, 24, 30)),
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(1.1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 6, 0, 6)
            };

            var sp = new StackPanel();

            sp.Children.Add(new TextBlock
            {
                Text = "⚙️ CUSTOMIZACJA I KALIBRACJA SIATKI WGS84",
                Foreground = TacticalConfigPanel.BrushAccent,
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 6)
            });

            // 1. Format zapisu wspolrzednych WGS84
            sp.Children.Add(new TextBlock
            {
                Text = "Format współrzędnych WGS84 na ramce:",
                Foreground = TacticalConfigPanel.BrushMutedText,
                FontSize = 10,
                Margin = new Thickness(0, 2, 0, 2)
            });

            var cbFormat = new ComboBox { Height = 26, Margin = new Thickness(0, 0, 0, 6), FontSize = 11 };
            string[] formats = new[]
            {
                "DMS (49°50'58.0\"N)",
                "DM (49°50.967'N)",
                "DD (49.84944°N)"
            };
            foreach (var f in formats) cbFormat.Items.Add(f);
            cbFormat.SelectedItem = formats.FirstOrDefault(x => x == _wgs84FormatPreset) ?? formats[0];
            cbFormat.SelectionChanged += (s, e) =>
            {
                if (cbFormat.SelectedItem is string val)
                {
                    _wgs84FormatPreset = val;
                    TriggerWgs84Redraw();
                }
            };
            sp.Children.Add(cbFormat);

            // 2. Krok / Gestosc oczka siatki WGS84
            sp.Children.Add(new TextBlock
            {
                Text = "Gęstość / Krok oczka siatki WGS84:",
                Foreground = TacticalConfigPanel.BrushMutedText,
                FontSize = 10,
                Margin = new Thickness(0, 2, 0, 2)
            });

            var cbStep = new ComboBox { Height = 26, Margin = new Thickness(0, 0, 0, 6), FontSize = 11 };
            string[] steps = new[]
            {
                "Auto (wg skali)",
                "1\" (co 1 sekundę kątową ~30m)",
                "2\" (co 2 sekundy kątowe ~60m)",
                "5\" (co 5 sekund kątowych ~150m)",
                "10\" (co 10 sekund kątowych ~300m)",
                "15\" (co 15 sekund kątowych ~450m)",
                "30\" (co 30 sekund kątowych ~900m)",
                "1' (co 1 minutę kątową ~1.8km)",
                "50 m (Kwadrat metryczny 50×50m z WGS84)",
                "100 m (Kwadrat metryczny 100×100m z WGS84)",
                "200 m (Kwadrat metryczny 200×200m z WGS84)",
                "500 m (Kwadrat metryczny 500×500m z WGS84)",
                "1000 m (Kwadrat metryczny 1×1km z WGS84)"
            };
            foreach (var st in steps) cbStep.Items.Add(st);
            cbStep.SelectedItem = steps.FirstOrDefault(x => x == _wgs84StepPreset) ?? steps[0];
            cbStep.SelectionChanged += (s, e) =>
            {
                if (cbStep.SelectedItem is string val)
                {
                    _wgs84StepPreset = val;
                    TriggerWgs84Redraw();
                }
            };
            sp.Children.Add(cbStep);

            // 3. Podzial wewnetrzny oczka WGS84
            sp.Children.Add(new TextBlock
            {
                Text = "Podział wewnętrzny oczka WGS84:",
                Foreground = TacticalConfigPanel.BrushMutedText,
                FontSize = 10,
                Margin = new Thickness(0, 2, 0, 2)
            });

            var cbSub = new ComboBox { Height = 26, Margin = new Thickness(0, 0, 0, 6), FontSize = 11 };
            string[] subs = new[]
            {
                "Brak",
                "Krzyżyki w środku (+)",
                "Podsiatka 2x2 (4 ćwiartki)",
                "Podsiatka 5x5 (25 pól)"
            };
            foreach (var sb in subs) cbSub.Items.Add(sb);
            cbSub.SelectedItem = subs.FirstOrDefault(x => x == _wgs84SubGridPreset) ?? subs[1];
            cbSub.SelectionChanged += (s, e) =>
            {
                if (cbSub.SelectedItem is string val)
                {
                    _wgs84SubGridPreset = val;
                    TriggerWgs84Redraw();
                }
            };
            sp.Children.Add(cbSub);

            // 4. Kotwiczenie siatki do pelnych sekund/minut GPS vs od srodka mapy
            var chkSnap = new CheckBox
            {
                Content = "Przyciągaj linie do pełnych sekund/minut GPS",
                IsChecked = _wgs84SnapToTrueGeo,
                Foreground = Brushes.White,
                FontSize = 10.5,
                Margin = new Thickness(0, 2, 0, 6)
            };
            chkSnap.Checked += (s, e) => { _wgs84SnapToTrueGeo = true; TriggerWgs84Redraw(); };
            chkSnap.Unchecked += (s, e) => { _wgs84SnapToTrueGeo = false; TriggerWgs84Redraw(); };
            sp.Children.Add(chkSnap);

            // 5. Mikro-kalibracja przesuniecia siatki wzgledem mapy (N/S oraz E/W w metrach)
            sp.Children.Add(new TextBlock
            {
                Text = "Kalibracja położenia siatki (Przesunięcie N/S oraz E/W w m):",
                Foreground = TacticalConfigPanel.BrushAccent,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 4, 0, 3)
            });

            var offsetGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            offsetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            offsetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            offsetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var spN = new StackPanel();
            spN.Children.Add(new TextBlock { Text = "Przesunięcie Północ/Południe (m):", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 9.5 });
            var tbN = new TextBox
            {
                Text = _wgs84OffsetNorthMeters.ToString("0.#", CultureInfo.InvariantCulture),
                Height = 24,
                Background = TacticalConfigPanel.BrushItemBg,
                Foreground = Brushes.White,
                BorderBrush = TacticalConfigPanel.BrushTileBorder,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(4, 0, 4, 0)
            };
            if (!isModalDialog) _wgs84OffsetNBox = tbN;
            spN.Children.Add(tbN);

            var nudgeN = new WrapPanel { Margin = new Thickness(0, 3, 0, 0) };
            foreach (double delta in new[] { -25.0, -5.0, 5.0, 25.0 })
            {
                double d = delta;
                var b = CreateMiniNudgeButton(d > 0 ? $"+{d:0}m" : $"{d:0}m", () =>
                {
                    _wgs84OffsetNorthMeters += d;
                    tbN.Text = _wgs84OffsetNorthMeters.ToString("0.#", CultureInfo.InvariantCulture);
                    if (_wgs84OffsetNBox != null) _wgs84OffsetNBox.Text = tbN.Text;
                    TriggerWgs84Redraw();
                });
                nudgeN.Children.Add(b);
            }
            spN.Children.Add(nudgeN);
            Grid.SetColumn(spN, 0);
            offsetGrid.Children.Add(spN);

            var spE = new StackPanel();
            spE.Children.Add(new TextBlock { Text = "Przesunięcie Wschód/Zachód (m):", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 9.5 });
            var tbE = new TextBox
            {
                Text = _wgs84OffsetEastMeters.ToString("0.#", CultureInfo.InvariantCulture),
                Height = 24,
                Background = TacticalConfigPanel.BrushItemBg,
                Foreground = Brushes.White,
                BorderBrush = TacticalConfigPanel.BrushTileBorder,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(4, 0, 4, 0)
            };
            if (!isModalDialog) _wgs84OffsetEBox = tbE;
            spE.Children.Add(tbE);

            var nudgeE = new WrapPanel { Margin = new Thickness(0, 3, 0, 0) };
            foreach (double delta in new[] { -25.0, -5.0, 5.0, 25.0 })
            {
                double d = delta;
                var b = CreateMiniNudgeButton(d > 0 ? $"+{d:0}m" : $"{d:0}m", () =>
                {
                    _wgs84OffsetEastMeters += d;
                    tbE.Text = _wgs84OffsetEastMeters.ToString("0.#", CultureInfo.InvariantCulture);
                    if (_wgs84OffsetEBox != null) _wgs84OffsetEBox.Text = tbE.Text;
                    TriggerWgs84Redraw();
                });
                nudgeE.Children.Add(b);
            }
            spE.Children.Add(nudgeE);
            Grid.SetColumn(spE, 2);
            offsetGrid.Children.Add(spE);

            sp.Children.Add(offsetGrid);

            // 6. Korekta skali oczka X% / Y%
            var scaleGrid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            scaleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            scaleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            scaleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var spSx = new StackPanel();
            spSx.Children.Add(new TextBlock { Text = "Skala szerokości oczka X (%):", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 9.5 });
            var tbSx = new TextBox
            {
                Text = _wgs84ScaleXPercent.ToString("0.#", CultureInfo.InvariantCulture),
                Height = 24,
                Background = TacticalConfigPanel.BrushItemBg,
                Foreground = Brushes.White,
                BorderBrush = TacticalConfigPanel.BrushTileBorder,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(4, 0, 4, 0)
            };
            if (!isModalDialog) _wgs84ScaleXBox = tbSx;
            spSx.Children.Add(tbSx);
            Grid.SetColumn(spSx, 0);
            scaleGrid.Children.Add(spSx);

            var spSy = new StackPanel();
            spSy.Children.Add(new TextBlock { Text = "Skala wysokości oczka Y (%):", Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 9.5 });
            var tbSy = new TextBox
            {
                Text = _wgs84ScaleYPercent.ToString("0.#", CultureInfo.InvariantCulture),
                Height = 24,
                Background = TacticalConfigPanel.BrushItemBg,
                Foreground = Brushes.White,
                BorderBrush = TacticalConfigPanel.BrushTileBorder,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(4, 0, 4, 0)
            };
            if (!isModalDialog) _wgs84ScaleYBox = tbSy;
            spSy.Children.Add(tbSy);
            Grid.SetColumn(spSy, 2);
            scaleGrid.Children.Add(spSy);

            sp.Children.Add(scaleGrid);

            // 7. Wspolrzedne odniesienia srodka arkusza (Lat / Lon)
            sp.Children.Add(new TextBlock
            {
                Text = "Współrzędne odniesienia środka mapy (WGS84 Lat / Lon):",
                Foreground = TacticalConfigPanel.BrushMutedText,
                FontSize = 9.5,
                Margin = new Thickness(0, 4, 0, 2)
            });

            var centerGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            centerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            centerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            centerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var tbLat = new TextBox
            {
                Text = __LAT_FIELD__.ToString("F6", CultureInfo.InvariantCulture),
                Height = 24,
                Background = TacticalConfigPanel.BrushItemBg,
                Foreground = TacticalConfigPanel.BrushAccent,
                BorderBrush = TacticalConfigPanel.BrushTileBorder,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(4, 0, 4, 0),
                ToolTip = "Szerokość geograficzna N środka mapy (np. 49.849444)"
            };
            if (!isModalDialog) _wgs84CenterLatOverrideBox = tbLat;
            Grid.SetColumn(tbLat, 0);
            centerGrid.Children.Add(tbLat);

            var tbLon = new TextBox
            {
                Text = __LON_FIELD__.ToString("F6", CultureInfo.InvariantCulture),
                Height = 24,
                Background = TacticalConfigPanel.BrushItemBg,
                Foreground = TacticalConfigPanel.BrushAccent,
                BorderBrush = TacticalConfigPanel.BrushTileBorder,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(4, 0, 4, 0),
                ToolTip = "Długość geograficzna E środka mapy (np. 18.812667)"
            };
            if (!isModalDialog) _wgs84CenterLonOverrideBox = tbLon;
            Grid.SetColumn(tbLon, 2);
            centerGrid.Children.Add(tbLon);

            sp.Children.Add(centerGrid);

            // Przyciski Zastosuj kalibracje + Reset
            var btnRow = new Grid();
            btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
            btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var btnApply = new Button
            {
                Content = "✓ ZASTOSUJ KALIBRACJĘ",
                Height = 28,
                Background = TacticalConfigPanel.BrushAccent,
                Foreground = new SolidColorBrush(Color.FromRgb(6, 14, 18)),
                FontWeight = FontWeights.Bold,
                FontSize = 10,
                Cursor = Cursors.Hand
            };
            btnApply.Click += (s, e) =>
            {
                if (double.TryParse(tbN.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double vN))
                    _wgs84OffsetNorthMeters = Math.Clamp(vN, -50000.0, 50000.0);
                if (double.TryParse(tbE.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double vE))
                    _wgs84OffsetEastMeters = Math.Clamp(vE, -50000.0, 50000.0);
                if (double.TryParse(tbSx.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double vSx))
                    _wgs84ScaleXPercent = Math.Clamp(vSx, 20.0, 500.0);
                if (double.TryParse(tbSy.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double vSy))
                    _wgs84ScaleYPercent = Math.Clamp(vSy, 20.0, 500.0);

                if (double.TryParse(tbLat.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double nLat) &&
                    nLat >= -85.0 && nLat <= 85.0)
                {
                    __LAT_FIELD__ = nLat;
                }
                if (double.TryParse(tbLon.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double nLon) &&
                    nLon >= -180.0 && nLon <= 180.0)
                {
                    __LON_FIELD__ = nLon;
                }

                TriggerWgs84Redraw();
            };
            Grid.SetColumn(btnApply, 0);
            btnRow.Children.Add(btnApply);

            var btnReset = new Button
            {
                Content = "↺ Reset (0m / 100%)",
                Height = 28,
                Background = TacticalConfigPanel.BrushItemBg,
                Foreground = Brushes.White,
                BorderBrush = TacticalConfigPanel.BrushTileBorder,
                FontSize = 9.5,
                Cursor = Cursors.Hand
            };
            btnReset.Click += (s, e) =>
            {
                _wgs84OffsetNorthMeters = 0.0;
                _wgs84OffsetEastMeters = 0.0;
                _wgs84ScaleXPercent = 100.0;
                _wgs84ScaleYPercent = 100.0;
                tbN.Text = "0";
                tbE.Text = "0";
                tbSx.Text = "100";
                tbSy.Text = "100";
                TriggerWgs84Redraw();
            };
            Grid.SetColumn(btnReset, 2);
            btnRow.Children.Add(btnReset);

            sp.Children.Add(btnRow);

            card.Child = sp;
            return card;
        }

        private static Button CreateMiniNudgeButton(string label, Action onClick)
        {
            var b = new Button
            {
                Content = label,
                Height = 20,
                Padding = new Thickness(5, 0, 5, 0),
                Margin = new Thickness(0, 0, 3, 2),
                Background = TacticalConfigPanel.BrushItemBg,
                Foreground = TacticalConfigPanel.BrushAccent,
                BorderBrush = TacticalConfigPanel.BrushTileBorder,
                BorderThickness = new Thickness(1),
                FontSize = 9,
                Cursor = Cursors.Hand
            };
            b.Click += (s, e) => onClick();
            return b;
        }

        // ====================================================================
        // SILNIK GEODEZYJNY I RENDERER SIATKI WGS84 (EPSG:4326 / EPSG:3857)
        // ====================================================================
        private static void RenderCalibratedWgs84GridOnSheet(double w, double h)
        {
            if (w <= 20 || h <= 20) return;

            Canvas targetCanvas = __GRID_CANVAS__ ?? _interactiveCanvas;
            if (targetCanvas == null) return;

            // Jezeli korzystamy z dedykowanego _gridCanvas, czyscimy stara nieprawidlowa siatke WGS84.
            // Jezeli jest to _interactiveCanvas, usuwamy wylacznie linie i etykiety starej siatki (zachowujac obiekty mapy).
            if (!ReferenceEquals(targetCanvas, _interactiveCanvas))
            {
                targetCanvas.Children.Clear();
            }
            else
            {
                var toRemove = new List<UIElement>();
                foreach (UIElement child in targetCanvas.Children)
                {
                    if (child is FrameworkElement fe && fe.Tag is string tagStr && tagStr == "FTH_WGS84_GRID_EL")
                    {
                        toRemove.Add(child);
                    }
                }
                foreach (var rem in toRemove)
                    targetCanvas.Children.Remove(rem);
            }

            // Usun takze z _interactiveCanvas ewentualne poprzednie etykiety WGS84
            if (_interactiveCanvas != null && !ReferenceEquals(targetCanvas, _interactiveCanvas))
            {
                var oldLabels = new List<UIElement>();
                foreach (UIElement child in _interactiveCanvas.Children)
                {
                    if (child is FrameworkElement fe && fe.Tag is string tagStr && tagStr == "FTH_WGS84_GRID_EL")
                    {
                        oldLabels.Add(child);
                    }
                }
                foreach (var rem in oldLabels)
                    _interactiveCanvas.Children.Remove(rem);
            }

            double centerLat = __LAT_FIELD__;
            double centerLon = __LON_FIELD__;
            double mpp = GetExactMetersPerPixel();
            if (mpp <= 0.0001 || double.IsNaN(mpp) || double.IsInfinity(mpp))
                mpp = 1.0;

            // Dokladne dlugosci luku 1 stopnia na elipsoidzie WGS84 dla szerokosci centerLat
            double latRad = centerLat * Math.PI / 180.0;
            double metersPerDegLat = (111132.92 - 559.82 * Math.Cos(2.0 * latRad) + 1.175 * Math.Cos(4.0 * latRad))
                                     * Math.Max(0.2, _wgs84ScaleYPercent / 100.0);
            double metersPerDegLon = (111412.84 * Math.Cos(latRad) - 93.5 * Math.Cos(3.0 * latRad))
                                     * Math.Max(0.2, _wgs84ScaleXPercent / 100.0);

            if (metersPerDegLon < 1000.0) metersPerDegLon = 1000.0;

            // Przesuniecie kalibracyjne N/S i E/W przeliczone na stopnie WGS84
            double calCenterLat = centerLat - (_wgs84OffsetNorthMeters / metersPerDegLat);
            double calCenterLon = centerLon - (_wgs84OffsetEastMeters / metersPerDegLon);

            // Wyznaczenie kroku siatki w stopniach dla osi Lat i Lon
            var (stepDegLat, stepDegLon) = ResolveWgs84AngularSteps(mpp, metersPerDegLat, metersPerDegLon);

            // Zakres geograficzny arkusza mapy
            double halfW = w / 2.0;
            double halfH = h / 2.0;

            double minLon = calCenterLon - (halfW * mpp) / metersPerDegLon;
            double maxLon = calCenterLon + (halfW * mpp) / metersPerDegLon;
            double minLat = calCenterLat - (halfH * mpp) / metersPerDegLat;
            double maxLat = calCenterLat + (halfH * mpp) / metersPerDegLat;

            string mainColorHex = (__GRID_COLOR_EXPR__) ?? "#FAD02C";
            double mainThickness = Math.Clamp((double)(__GRID_THICKNESS_EXPR__), 0.5, 4.0);
            Brush mainStroke = TacticalConfigPanel.BrushFromHex(mainColorHex, Color.FromRgb(250, 208, 44));
            Brush subStroke = new SolidColorBrush(Color.FromArgb(120, 250, 208, 44));

            // Lista wspolrzednych poludnikow (X) i rownoleznikow (Y)
            var lonLines = new List<double>();
            var latLines = new List<double>();

            if (_wgs84SnapToTrueGeo)
            {
                double startLon = Math.Floor(minLon / stepDegLon) * stepDegLon;
                int guardX = 0;
                for (double lon = startLon; lon <= maxLon + stepDegLon * 0.5 && guardX < 120; lon += stepDegLon, guardX++)
                {
                    lonLines.Add(lon);
                }

                double startLat = Math.Floor(minLat / stepDegLat) * stepDegLat;
                int guardY = 0;
                for (double lat = startLat; lat <= maxLat + stepDegLat * 0.5 && guardY < 120; lat += stepDegLat, guardY++)
                {
                    latLines.Add(lat);
                }
            }
            else
            {
                // Kotwiczenie symetrycznie od srodka arkusza
                int countX = (int)Math.Ceiling((maxLon - minLon) / stepDegLon) + 2;
                for (int i = -countX; i <= countX; i++)
                    lonLines.Add(calCenterLon + i * stepDegLon);

                int countY = (int)Math.Ceiling((maxLat - minLat) / stepDegLat) + 2;
                for (int j = -countY; j <= countY; j++)
                    latLines.Add(calCenterLat + j * stepDegLat);
            }

            // 1. Rysowanie podsiatki wewnatrz oczek (2x2, 5x5 lub krzyzyki)
            int subDivs = _wgs84SubGridPreset.Contains("5x5") ? 5 : (_wgs84SubGridPreset.Contains("2x2") ? 2 : 0);
            bool drawCrosshairs = _wgs84SubGridPreset.Contains("Krzyżyki");

            if (subDivs > 1)
            {
                foreach (double lon in lonLines)
                {
                    for (int s = 1; s < subDivs; s++)
                    {
                        double subLon = lon + (stepDegLon * s) / subDivs;
                        double x = halfW + ((subLon - calCenterLon) * metersPerDegLon) / mpp;
                        if (x >= 0 && x <= w)
                        {
                            var sl = new Line
                            {
                                X1 = x, Y1 = 0, X2 = x, Y2 = h,
                                Stroke = subStroke,
                                StrokeThickness = Math.Max(0.5, mainThickness * 0.5),
                                StrokeDashArray = new DoubleCollection { 3, 3 },
                                IsHitTestVisible = false,
                                Tag = "FTH_WGS84_GRID_EL"
                            };
                            targetCanvas.Children.Add(sl);
                        }
                    }
                }

                foreach (double lat in latLines)
                {
                    for (int s = 1; s < subDivs; s++)
                    {
                        double subLat = lat + (stepDegLat * s) / subDivs;
                        double y = halfH - ((subLat - calCenterLat) * metersPerDegLat) / mpp;
                        if (y >= 0 && y <= h)
                        {
                            var sl = new Line
                            {
                                X1 = 0, Y1 = y, X2 = w, Y2 = y,
                                Stroke = subStroke,
                                StrokeThickness = Math.Max(0.5, mainThickness * 0.5),
                                StrokeDashArray = new DoubleCollection { 3, 3 },
                                IsHitTestVisible = false,
                                Tag = "FTH_WGS84_GRID_EL"
                            };
                            targetCanvas.Children.Add(sl);
                        }
                    }
                }
            }
            else if (drawCrosshairs)
            {
                foreach (double lon in lonLines)
                {
                    double midLon = lon + stepDegLon * 0.5;
                    double cx = halfW + ((midLon - calCenterLon) * metersPerDegLon) / mpp;
                    if (cx < 6 || cx > w - 6) continue;

                    foreach (double lat in latLines)
                    {
                        double midLat = lat + stepDegLat * 0.5;
                        double cy = halfH - ((midLat - calCenterLat) * metersPerDegLat) / mpp;
                        if (cy < 6 || cy > h - 6) continue;

                        var chH = new Line
                        {
                            X1 = cx - 5, Y1 = cy, X2 = cx + 5, Y2 = cy,
                            Stroke = subStroke, StrokeThickness = 1.0, IsHitTestVisible = false, Tag = "FTH_WGS84_GRID_EL"
                        };
                        var chV = new Line
                        {
                            X1 = cx, Y1 = cy - 5, X2 = cx, Y2 = cy + 5,
                            Stroke = subStroke, StrokeThickness = 1.0, IsHitTestVisible = false, Tag = "FTH_WGS84_GRID_EL"
                        };
                        targetCanvas.Children.Add(chH);
                        targetCanvas.Children.Add(chV);
                    }
                }
            }

            // 2. Rysowanie glownych poludnikow (E/W) oraz etykiet dlugosci geograficznej na gorze i dole
            foreach (double lon in lonLines)
            {
                double x = halfW + ((lon - calCenterLon) * metersPerDegLon) / mpp;
                if (x < 0 || x > w) continue;

                var line = new Line
                {
                    X1 = x, Y1 = 0, X2 = x, Y2 = h,
                    Stroke = mainStroke,
                    StrokeThickness = mainThickness,
                    IsHitTestVisible = false,
                    Tag = "FTH_WGS84_GRID_EL"
                };
                targetCanvas.Children.Add(line);

                if (x >= 28 && x <= w - 58)
                {
                    string lblText = FormatWgs84Coordinate(lon, isLat: false);
                    var topBadge = CreateWgs84EdgeBadge(lblText, mainStroke);
                    Canvas.SetLeft(topBadge, Math.Clamp(x - 30, 2, w - 68));
                    Canvas.SetTop(topBadge, 3);
                    targetCanvas.Children.Add(topBadge);

                    var botBadge = CreateWgs84EdgeBadge(lblText, mainStroke);
                    Canvas.SetLeft(botBadge, Math.Clamp(x - 30, 2, w - 68));
                    Canvas.SetTop(botBadge, Math.Max(3, h - 19));
                    targetCanvas.Children.Add(botBadge);
                }
            }

            // 3. Rysowanie glownych rownoleznikow (N/S) oraz etykiet szerokosci geograficznej po lewej i prawej
            foreach (double lat in latLines)
            {
                double y = halfH - ((lat - calCenterLat) * metersPerDegLat) / mpp;
                if (y < 0 || y > h) continue;

                var line = new Line
                {
                    X1 = 0, Y1 = y, X2 = w, Y2 = y,
                    Stroke = mainStroke,
                    StrokeThickness = mainThickness,
                    IsHitTestVisible = false,
                    Tag = "FTH_WGS84_GRID_EL"
                };
                targetCanvas.Children.Add(line);

                if (y >= 18 && y <= h - 18)
                {
                    string lblText = FormatWgs84Coordinate(lat, isLat: true);
                    var leftBadge = CreateWgs84EdgeBadge(lblText, mainStroke);
                    Canvas.SetLeft(leftBadge, 3);
                    Canvas.SetTop(leftBadge, Math.Clamp(y - 8, 2, h - 18));
                    targetCanvas.Children.Add(leftBadge);

                    var rightBadge = CreateWgs84EdgeBadge(lblText, mainStroke);
                    Canvas.SetLeft(rightBadge, Math.Max(3, w - 76));
                    Canvas.SetTop(rightBadge, Math.Clamp(y - 8, 2, h - 18));
                    targetCanvas.Children.Add(rightBadge);
                }
            }
        }

        private static (double stepDegLat, double stepDegLon) ResolveWgs84AngularSteps(
            double mpp,
            double metersPerDegLat,
            double metersPerDegLon)
        {
            // Jezeli uzytkownik wybral siatke kwadratowa w metrach (np. "100 m (...)"):
            if (_wgs84StepPreset.Contains(" m "))
            {
                string numPart = _wgs84StepPreset.Split(' ')[0];
                if (double.TryParse(numPart, NumberStyles.Float, CultureInfo.InvariantCulture, out double stepMeters) && stepMeters >= 10)
                {
                    return (stepMeters / metersPerDegLat, stepMeters / metersPerDegLon);
                }
            }

            // Jezeli uzytkownik wybral konkretny krok w sekundach lub minutach katowych:
            double arcSeconds = 0;
            if (_wgs84StepPreset.StartsWith("1\"")) arcSeconds = 1.0;
            else if (_wgs84StepPreset.StartsWith("2\"")) arcSeconds = 2.0;
            else if (_wgs84StepPreset.StartsWith("5\"")) arcSeconds = 5.0;
            else if (_wgs84StepPreset.StartsWith("10\"")) arcSeconds = 10.0;
            else if (_wgs84StepPreset.StartsWith("15\"")) arcSeconds = 15.0;
            else if (_wgs84StepPreset.StartsWith("30\"")) arcSeconds = 30.0;
            else if (_wgs84StepPreset.StartsWith("1'")) arcSeconds = 60.0;

            if (arcSeconds <= 0)
            {
                // Tryb Auto: dobierz krok katowy tak, aby odstep linii na arkuszu wynosil ok. 85-170 px
                double targetMeters = mpp * 115.0;
                double targetSec = (targetMeters / metersPerDegLat) * 3600.0;
                double[] niceSecs = new[] { 1.0, 2.0, 5.0, 10.0, 15.0, 30.0, 60.0, 120.0, 300.0, 600.0 };
                arcSeconds = 5.0;
                double bestDiff = double.MaxValue;
                foreach (double ns in niceSecs)
                {
                    double diff = Math.Abs(ns - targetSec);
                    if (diff < bestDiff)
                    {
                        bestDiff = diff;
                        arcSeconds = ns;
                    }
                }
            }

            double degStep = arcSeconds / 3600.0;
            return (degStep, degStep);
        }

        private static string FormatWgs84Coordinate(double valueDeg, bool isLat)
        {
            char hemi = isLat ? (valueDeg >= 0 ? 'N' : 'S') : (valueDeg >= 0 ? 'E' : 'W');
            double absVal = Math.Abs(valueDeg);

            if (_wgs84FormatPreset.StartsWith("DD "))
            {
                return $"{absVal.ToString("F5", CultureInfo.InvariantCulture)}°{hemi}";
            }

            int deg = (int)Math.Floor(absVal + 1e-9);
            double remMin = (absVal - deg) * 60.0;

            if (_wgs84FormatPreset.StartsWith("DM "))
            {
                return $"{deg:00}°{remMin.ToString("00.000", CultureInfo.InvariantCulture)}'{hemi}";
            }

            int min = (int)Math.Floor(remMin + 1e-7);
            double sec = (remMin - min) * 60.0;
            if (sec >= 59.95)
            {
                sec = 0.0;
                min++;
                if (min >= 60) { min = 0; deg++; }
            }

            return $"{deg:00}°{min:00}'{sec.ToString("00.0", CultureInfo.InvariantCulture)}\"{hemi}";
        }

        private static Border CreateWgs84EdgeBadge(string text, Brush borderBrush)
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(220, 6, 14, 18)),
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(0.9),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(3, 1, 3, 1),
                IsHitTestVisible = false,
                Tag = "FTH_WGS84_GRID_EL",
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = borderBrush,
                    FontSize = 8.2,
                    FontWeight = FontWeights.Bold,
                    FontFamily = new FontFamily("Consolas")
                }
            };
        }
    }
}
//===END_WGS84_PARTIAL_CS===