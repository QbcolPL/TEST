@echo off
setlocal DisableDelayedExpansion
cd /d "%~dp0"

set "BAT_FILE=%~f0"

echo ============================================================================
echo [KROK 2/2] Wdrazanie ciemnego stylu UI ComboBoxow, mikro-kalibracji WGS84
echo            na zywo oraz 3-wierszowego ukladu tabeli obiektow na mapie...
echo ============================================================================
powershell -NoProfile -ExecutionPolicy Bypass -Command "Add-Type -TypeDefinition ([System.Text.RegularExpressions.Regex]::Split([System.IO.File]::ReadAllText([System.Environment]::GetEnvironmentVariable('BAT_FILE'), [System.Text.Encoding]::UTF8), '//===' + 'STEP2_UI_AND_TABLE_CS===')[1]); [FthStep2DarkUiAndObjectTable]::Run()"

if errorlevel 1 (
    echo.
    echo [BLAD] Wystapil blad podczas wykonywania Kroku 2 lub wysylki na GitHub!
    pause
    exit /b 1
)

echo.
echo ============================================================================
echo [SUKCES - KROK 2/2] Ciemny styl UI ComboBoxow, mikro-kalibracja WGS84 na
echo zywo oraz 3-wierszowa tabela obiektow zostaly wyslane na GitHub!
echo ============================================================================
pause
exit /b 0

//===STEP2_UI_AND_TABLE_CS===
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public static class FthStep2DarkUiAndObjectTable
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

        // 1. Podpiecie RestyleAndAlignMapObjectsTable na koncu metody budujacej liste obiektow ("W legendzie:")
        string tmg = File.ReadAllText(tmgPath, Encoding.UTF8);
        tmg = InjectObjectTableRestylerHook(tmg, "TacticalMapGeneratorView.cs");

        int openTmg = tmg.Count(c => c == '{');
        int closeTmg = tmg.Count(c => c == '}');
        Console.WriteLine("[WERYFIKACJA] Nawiasy w TacticalMapGeneratorView.cs: { = " + openTmg + ", } = " + closeTmg);
        if (openTmg != closeTmg)
            throw new Exception("Niezgodnosc nawiasow klamrowych w TacticalMapGeneratorView.cs!");
        File.WriteAllText(tmgPath, tmg, utf8NoBom);

        string eng = "";
        if (File.Exists(engPath))
        {
            eng = File.ReadAllText(engPath, Encoding.UTF8);
            eng = InjectObjectTableRestylerHook(eng, "TacticalMapGeneratorView.Engine.cs");

            int openEng = eng.Count(c => c == '{');
            int closeEng = eng.Count(c => c == '}');
            Console.WriteLine("[WERYFIKACJA] Nawiasy w TacticalMapGeneratorView.Engine.cs: { = " + openEng + ", } = " + closeEng);
            if (openEng != closeEng)
                throw new Exception("Niezgodnosc nawiasow klamrowych w TacticalMapGeneratorView.Engine.cs!");
            File.WriteAllText(engPath, eng, utf8NoBom);
        }

        // 2. Zaktualizowanie pliku TacticalMapGeneratorView.Wgs84.cs o ciemny styl ComboBoxow,
        //    pola mikro-kalibracji na zywo oraz 3-wierszowy uklad tabeli obiektow
        string combined = tmg + "\n" + eng;
        string latField = DetectLatFieldName(combined);
        string lonField = DetectLonFieldName(combined, latField);

        string batFile = Environment.GetEnvironmentVariable("BAT_FILE");
        string rawBat = File.ReadAllText(batFile, Encoding.UTF8);

        string[] parts = Regex.Split(rawBat, "//===" + "PAYLOAD_STEP2_UI_AND_TABLE_CS===");
        if (parts.Length < 2)
            throw new Exception("Brak bloku PAYLOAD_STEP2_UI_AND_TABLE_CS w pliku .bat!");
        string[] endParts = Regex.Split(parts[1], "//===" + "END_STEP2_UI_AND_TABLE_CS===");
        string uiSection = endParts[0].Trim()
            .Replace("__LAT_FIELD__", latField)
            .Replace("__LON_FIELD__", lonField);

        if (!File.Exists(wgsPath))
            throw new Exception("Nie znaleziono pliku TacticalMapGeneratorView.Wgs84.cs! Uruchom najpierw Krok 1.");

        string wgs = File.ReadAllText(wgsPath, Encoding.UTF8);

        if (!wgs.Contains("using System.Windows.Controls.Primitives;"))
        {
            wgs = wgs.Replace(
                "using System.Windows.Controls;",
                "using System.Text;\r\nusing System.Text.RegularExpressions;\r\nusing System.Windows.Controls;\r\nusing System.Windows.Controls.Primitives;");
        }

        string startMarker = "public static void RestyleAndAlignMapObjectsTable(Panel listPanel)";
        if (wgs.Contains("private static bool _isRestylingObjectsTable"))
            startMarker = "private static bool _isRestylingObjectsTable";

        string endMarker = "private static List<Canvas> GetAllMapSheetCanvasesFast()";

        int startIdx = wgs.IndexOf(startMarker, StringComparison.Ordinal);
        int endIdx = wgs.IndexOf(endMarker, StringComparison.Ordinal);
        if (startIdx < 0 || endIdx <= startIdx)
            throw new Exception("Nie znaleziono sekcji UI w TacticalMapGeneratorView.Wgs84.cs!");

        wgs = wgs.Substring(0, startIdx) + uiSection + "\r\n\r\n        " + wgs.Substring(endIdx);

        int openWgs = wgs.Count(c => c == '{');
        int closeWgs = wgs.Count(c => c == '}');
        Console.WriteLine("[WERYFIKACJA] Nawiasy w TacticalMapGeneratorView.Wgs84.cs: { = " + openWgs + ", } = " + closeWgs);
        if (openWgs != closeWgs)
            throw new Exception("Niezgodnosc nawiasow klamrowych w TacticalMapGeneratorView.Wgs84.cs!");

        File.WriteAllText(wgsPath, wgs, utf8NoBom);
        Console.WriteLine("[OK] Zaktualizowano TacticalMapGeneratorView.Wgs84.cs (ciemne ComboBoxy + Live Kalibracja + 3-wierszowa Tabela).");

        string triggerPath = Path.Combine(targetDir, ".fth_build_trigger.txt");
        File.WriteAllText(triggerPath, "Field TAK Hub Step 2 Dark ComboBox UI, Live WGS84 Calibration & 3-Row Object Table UTC: " + DateTime.UtcNow.ToString("O") + " | RunId: " + Guid.NewGuid().ToString("N") + "\n", utf8NoBom);

        PushAndTriggerGitHubActions(targetDir);
    }

    private static string InjectObjectTableRestylerHook(string code, string fileLabel)
    {
        if (code.Contains("RestyleAndAlignMapObjectsTable("))
            return code;

        int legIdx = code.IndexOf("\"W legendzie:\"", StringComparison.Ordinal);
        if (legIdx < 0)
            legIdx = code.IndexOf("Edytuj opis tego punktu", StringComparison.Ordinal);
        if (legIdx < 0)
            return code;

        string masked = MaskStringsAndComments(code);
        int curPos = legIdx;
        int methodOpen = -1;
        int methodClose = -1;

        for (int level = 0; level < 8; level++)
        {
            int oIdx, cIdx;
            if (!TryFindEnclosingBraceSpan(masked, curPos, out oIdx, out cIdx))
                break;

            string hdr = GetImmediateStatementHeader(code, masked, oIdx);
            if (Regex.IsMatch(hdr, @"\bvoid\s+[A-Za-z0-9_]+\s*\([^)]*\)\s*$"))
            {
                methodOpen = oIdx;
                methodClose = cIdx;
                break;
            }
            curPos = oIdx - 1;
        }

        if (methodOpen >= 0 && methodClose > methodOpen)
        {
            string methodBody = code.Substring(methodOpen, methodClose - methodOpen + 1);
            Match mPanel = Regex.Match(methodBody, @"(_[a-zA-Z0-9_]+)\.Children\.Clear\s*\(\s*\)");
            if (!mPanel.Success)
                mPanel = Regex.Match(methodBody, @"(_[a-zA-Z0-9_]*Object[a-zA-Z0-9_]*)\.Children\.Add\s*\(");

            if (mPanel.Success)
            {
                string panelFieldName = mPanel.Groups[1].Value;
                string hook = "\r\n            RestyleAndAlignMapObjectsTable(" + panelFieldName + ");\r\n        ";
                code = code.Substring(0, methodClose) + hook + code.Substring(methodClose);
                Console.WriteLine("[OK] Podpieto RestyleAndAlignMapObjectsTable(" + panelFieldName + ") w " + fileLabel + ".");
            }
        }

        return code;
    }

    private static string GetImmediateStatementHeader(string code, string masked, int openBraceIdx)
    {
        int parenDepth = 0;
        int start = 0;
        for (int i = openBraceIdx - 1; i >= 0; i--)
        {
            char ch = masked[i];
            if (ch == ')') parenDepth++;
            else if (ch == '(') { if (parenDepth > 0) parenDepth--; }
            else if (parenDepth == 0 && (ch == '{' || ch == '}' || ch == ';'))
            {
                start = i + 1;
                break;
            }
        }
        return code.Substring(start, openBraceIdx - start).Trim();
    }

    private static string MaskStringsAndComments(string code)
    {
        char[] chars = code.ToCharArray();
        int n = chars.Length;
        int i = 0;
        while (i < n)
        {
            if (i + 1 < n && chars[i] == '/' && chars[i + 1] == '/')
            {
                while (i < n && chars[i] != '\n') { if (chars[i] != '\r') chars[i] = ' '; i++; }
            }
            else if (i + 1 < n && chars[i] == '/' && chars[i + 1] == '*')
            {
                chars[i] = ' '; chars[i + 1] = ' '; i += 2;
                while (i + 1 < n && !(chars[i] == '*' && chars[i + 1] == '/'))
                {
                    if (chars[i] != '\n' && chars[i] != '\r') chars[i] = ' ';
                    i++;
                }
                if (i + 1 < n) { chars[i] = ' '; chars[i + 1] = ' '; i += 2; }
            }
            else if (chars[i] == '"')
            {
                chars[i] = ' '; i++;
                while (i < n && chars[i] != '"')
                {
                    if (chars[i] == '\\' && i + 1 < n) { chars[i] = ' '; chars[i + 1] = ' '; i += 2; continue; }
                    if (chars[i] != '\n' && chars[i] != '\r') chars[i] = ' ';
                    i++;
                }
                if (i < n) { chars[i] = ' '; i++; }
            }
            else if (chars[i] == '\'')
            {
                chars[i] = ' '; i++;
                while (i < n && chars[i] != '\'')
                {
                    if (chars[i] == '\\' && i + 1 < n) { chars[i] = ' '; chars[i + 1] = ' '; i += 2; continue; }
                    if (chars[i] != '\n' && chars[i] != '\r') chars[i] = ' ';
                    i++;
                }
                if (i < n) { chars[i] = ' '; i++; }
            }
            else i++;
        }
        return new string(chars);
    }

    private static bool TryFindEnclosingBraceSpan(string masked, int pos, out int openIdx, out int closeIdx)
    {
        openIdx = -1; closeIdx = -1;
        int depth = 0;
        for (int i = pos; i >= 0; i--)
        {
            char ch = masked[i];
            if (ch == '}') depth++;
            else if (ch == '{')
            {
                if (depth == 0) { openIdx = i; break; }
                depth--;
            }
        }
        if (openIdx < 0) return false;
        closeIdx = FindMatchingCloseBrace(masked, openIdx);
        return closeIdx > openIdx;
    }

    private static int FindMatchingCloseBrace(string masked, int openIdx)
    {
        int depth = 0;
        for (int i = openIdx; i < masked.Length; i++)
        {
            char ch = masked[i];
            if (ch == '{') depth++;
            else if (ch == '}') { depth--; if (depth == 0) return i; }
        }
        return -1;
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
            if (Regex.IsMatch(combined, @"\b" + Regex.Escape(c) + @"\b")) return c;
        return "_centerLat";
    }

    private static string DetectLonFieldName(string combined, string latField)
    {
        string guessedLon = latField.Replace("Lat", "Lon").Replace("lat", "lon");
        if (Regex.IsMatch(combined, @"\b" + Regex.Escape(guessedLon) + @"\b")) return guessedLon;
        string[] candidates = new[] { "_centerLon", "_currentLon", "_mapCenterLon", "_targetLon", "_lon", "_centerLng" };
        foreach (string c in candidates)
            if (Regex.IsMatch(combined, @"\b" + Regex.Escape(c) + @"\b")) return c;
        return "_centerLon";
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
        if (File.Exists(lockFile)) { try { File.Delete(lockFile); } catch { } }

        RunProcess(gitExe, "init", targetDir);
        RunProcess(gitExe, "config user.name \"QbcolPL\"", targetDir);
        RunProcess(gitExe, "config user.email \"QbcolPL@users.noreply.github.com\"", targetDir);
        RunProcess(gitExe, "branch -M main", targetDir);
        RunProcess(gitExe, "remote add origin " + RepoRemoteUrl, targetDir);
        RunProcess(gitExe, "remote set-url origin " + RepoRemoteUrl, targetDir);

        RunProcess(gitExe, "add -A", targetDir);
        RunProcess(gitExe, "commit -m \"Step 2/2: Dark ComboBox UI, live WGS84 micro-calibration, and 3-row Map Objects Table\" --allow-empty", targetDir);

        int pushExitCode = RunProcess(gitExe, "push -u origin main --force", targetDir);
        if (pushExitCode != 0)
            throw new Exception("Polecenie 'git push -u origin main --force' zwrocilo kod bledu: " + pushExitCode);

        try { Process.Start(new ProcessStartInfo { FileName = RepoActionsUrl, UseShellExecute = true }); } catch { }
    }

    private static int RunProcess(string fileName, string arguments, string workingDir)
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
//===STEP2_UI_AND_TABLE_CS===

//===PAYLOAD_STEP2_UI_AND_TABLE_CS===
private static bool _isRestylingObjectsTable = false;

        // ====================================================================
        // 3-WIERSZOWA TABELA OBIEKTOW NA MAPIE (SZYBKA O(N), ZERO ZAWIESZANIA)
        // ====================================================================
        public static void RestyleAndAlignMapObjectsTable(Panel listPanel)
        {
            if (listPanel == null || _isRestylingObjectsTable) return;
            _isRestylingObjectsTable = true;
            try
            {
                var cards = listPanel.Children.OfType<Border>().ToList();
                foreach (var card in cards)
                {
                    if (card.Tag is string t && t == "FTH_RESTYLED_OBJ_CARD")
                        continue;

                    try
                    {
                        RestyleSingleMapObjectCard(card);
                        card.Tag = "FTH_RESTYLED_OBJ_CARD";
                    }
                    catch { }
                }
            }
            finally
            {
                _isRestylingObjectsTable = false;
            }
        }

        private static void RestyleSingleMapObjectCard(Border card)
        {
            var allControls = new List<DependencyObject>();
            var visited = new HashSet<DependencyObject>();
            CollectLogicalChildrenBounded(card, allControls, visited, 0);

            var checkBox = allControls.OfType<CheckBox>().FirstOrDefault();
            var textBoxes = allControls.OfType<TextBox>().ToList();
            if (checkBox == null || textBoxes.Count < 1) return;

            TextBox nameBox = textBoxes[0];
            TextBox legendBox = textBoxes.Count > 1 ? textBoxes[1] : null;
            Slider sizeSlider = allControls.OfType<Slider>().FirstOrDefault();

            var buttons = allControls.OfType<Button>().ToList();
            if (buttons.Count < 2) return;

            Button colorBtn = buttons[0];
            Button pinBtn = buttons.Count > 1 ? buttons[1] : null;
            Button deleteBtn = buttons[buttons.Count - 1];

            Button typeBtn = null;
            Button simplifyBtn = null;
            if (buttons.Count >= 4)
            {
                for (int i = 2; i < buttons.Count - 1; i++)
                {
                    var b = buttons[i];
                    string bTxt = ExtractPlainButtonText(b);
                    if (bTxt.IndexOf("Typ", StringComparison.OrdinalIgnoreCase) >= 0 || typeBtn == null)
                        typeBtn = b;
                    else
                        simplifyBtn = b;
                }
            }

            var btnAndTbSet = new HashSet<DependencyObject>(buttons.Cast<DependencyObject>().Concat(textBoxes.Cast<DependencyObject>()));
            Border symbolBadge = null;
            foreach (var brd in allControls.OfType<Border>())
            {
                if (ReferenceEquals(brd, card)) continue;
                if (IsDescendantFast(brd, btnAndTbSet)) continue;
                symbolBadge = brd;
                break;
            }

            var btnSet = new HashSet<DependencyObject>(buttons.Cast<DependencyObject>());
            TextBlock counterTb = null;
            foreach (var tb in allControls.OfType<TextBlock>())
            {
                if (IsDescendantFast(tb, btnSet)) continue;
                string txt = (tb.Text ?? "").Trim();
                if (txt.EndsWith("x", StringComparison.OrdinalIgnoreCase) || txt.Contains("px") || Regex.IsMatch(txt, @"^\d+\s*x$"))
                    counterTb = tb;
            }

            SafeDetachElement(checkBox);
            SafeDetachElement(colorBtn);
            if (pinBtn != null) SafeDetachElement(pinBtn);
            if (symbolBadge != null) SafeDetachElement(symbolBadge);
            SafeDetachElement(nameBox);
            SafeDetachElement(deleteBtn);
            if (legendBox != null) SafeDetachElement(legendBox);
            if (sizeSlider != null) SafeDetachElement(sizeSlider);
            if (typeBtn != null) SafeDetachElement(typeBtn);
            if (simplifyBtn != null) SafeDetachElement(simplifyBtn);
            if (counterTb != null) SafeDetachElement(counterTb);

            card.Background = new SolidColorBrush(Color.FromRgb(10, 24, 30));
            card.BorderBrush = new SolidColorBrush(Color.FromRgb(24, 62, 74));
            card.BorderThickness = new Thickness(1);
            card.CornerRadius = new CornerRadius(6);
            card.Padding = new Thickness(9, 7, 9, 7);
            card.Margin = new Thickness(0, 0, 0, 6);

            var mainStack = new StackPanel();

            // WIERSZ 1: [Checkbox] [Kolor 22x22] [Pinezka 26x22] [Symbol] [Nazwa *] [Usun -]
            var row1 = new Grid { Height = 26 };
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            checkBox.VerticalAlignment = VerticalAlignment.Center;
            checkBox.Margin = new Thickness(0, 0, 6, 0);
            Grid.SetColumn(checkBox, 0);
            row1.Children.Add(checkBox);

            colorBtn.Width = 22;
            colorBtn.Height = 22;
            colorBtn.VerticalAlignment = VerticalAlignment.Center;
            colorBtn.Margin = new Thickness(0, 0, 5, 0);
            colorBtn.BorderBrush = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255));
            colorBtn.BorderThickness = new Thickness(1);
            colorBtn.Cursor = Cursors.Hand;
            Grid.SetColumn(colorBtn, 1);
            row1.Children.Add(colorBtn);

            if (pinBtn != null)
            {
                pinBtn.Width = 26;
                pinBtn.Height = 22;
                pinBtn.Padding = new Thickness(0);
                pinBtn.Margin = new Thickness(0, 0, 5, 0);
                pinBtn.VerticalAlignment = VerticalAlignment.Center;
                pinBtn.HorizontalContentAlignment = HorizontalAlignment.Center;
                pinBtn.VerticalContentAlignment = VerticalAlignment.Center;
                pinBtn.Background = new SolidColorBrush(Color.FromRgb(7, 17, 21));
                pinBtn.BorderBrush = TacticalConfigPanel.BrushAccent;
                pinBtn.BorderThickness = new Thickness(1);
                pinBtn.Foreground = TacticalConfigPanel.BrushAccent;
                pinBtn.FontSize = 11;
                if (string.IsNullOrWhiteSpace(ExtractPlainButtonText(pinBtn))) pinBtn.Content = "📍";
                Grid.SetColumn(pinBtn, 2);
                row1.Children.Add(pinBtn);
            }

            if (symbolBadge != null)
            {
                symbolBadge.Height = 22;
                symbolBadge.MinWidth = 32;
                symbolBadge.VerticalAlignment = VerticalAlignment.Center;
                symbolBadge.Margin = new Thickness(0, 0, 6, 0);
                Grid.SetColumn(symbolBadge, 3);
                row1.Children.Add(symbolBadge);
            }

            nameBox.Height = 24;
            nameBox.Width = double.NaN;
            nameBox.MinWidth = 80;
            nameBox.HorizontalAlignment = HorizontalAlignment.Stretch;
            nameBox.VerticalAlignment = VerticalAlignment.Center;
            nameBox.VerticalContentAlignment = VerticalAlignment.Center;
            nameBox.Padding = new Thickness(6, 0, 6, 0);
            nameBox.Margin = new Thickness(0, 0, 6, 0);
            nameBox.Background = new SolidColorBrush(Color.FromRgb(7, 17, 21));
            nameBox.Foreground = Brushes.White;
            nameBox.CaretBrush = TacticalConfigPanel.BrushAccent;
            nameBox.BorderBrush = TacticalConfigPanel.BrushAccent;
            nameBox.BorderThickness = new Thickness(1);
            nameBox.FontSize = 11;
            nameBox.FontWeight = FontWeights.Bold;
            Grid.SetColumn(nameBox, 4);
            row1.Children.Add(nameBox);

            deleteBtn.Width = 24;
            deleteBtn.Height = 22;
            deleteBtn.Padding = new Thickness(0);
            deleteBtn.Margin = new Thickness(0);
            deleteBtn.VerticalAlignment = VerticalAlignment.Center;
            deleteBtn.HorizontalContentAlignment = HorizontalAlignment.Center;
            deleteBtn.VerticalContentAlignment = VerticalAlignment.Center;
            deleteBtn.Cursor = Cursors.Hand;
            Grid.SetColumn(deleteBtn, 5);
            row1.Children.Add(deleteBtn);

            mainStack.Children.Add(row1);

            // WIERSZ 2: W legendzie: [ Pole opisu w legendzie * ]
            if (legendBox != null)
            {
                var row2 = new Grid { Margin = new Thickness(0, 5, 0, 0), Height = 24 };
                row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });
                row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var lblLeg = new TextBlock
                {
                    Text = "W legendzie:",
                    Foreground = TacticalConfigPanel.BrushMutedText,
                    FontSize = 9.5,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(lblLeg, 0);
                row2.Children.Add(lblLeg);

                legendBox.Height = 23;
                legendBox.Width = double.NaN;
                legendBox.HorizontalAlignment = HorizontalAlignment.Stretch;
                legendBox.VerticalAlignment = VerticalAlignment.Center;
                legendBox.VerticalContentAlignment = VerticalAlignment.Center;
                legendBox.Padding = new Thickness(6, 0, 6, 0);
                legendBox.Margin = new Thickness(0);
                legendBox.Background = new SolidColorBrush(Color.FromRgb(7, 17, 21));
                legendBox.Foreground = new SolidColorBrush(Color.FromRgb(215, 228, 232));
                legendBox.CaretBrush = TacticalConfigPanel.BrushAccent;
                legendBox.BorderBrush = new SolidColorBrush(Color.FromRgb(22, 56, 68));
                legendBox.BorderThickness = new Thickness(1);
                legendBox.FontSize = 10.5;
                legendBox.ToolTip = null;
                Grid.SetColumn(legendBox, 1);
                row2.Children.Add(legendBox);

                mainStack.Children.Add(row2);
            }

            // WIERSZ 3: Wielkosc (Slider) LUB Narzedzia Geometrii (Typ + Uprosc + 12x)
            if (sizeSlider != null || typeBtn != null || simplifyBtn != null || counterTb != null)
            {
                var row3 = new Grid { Margin = new Thickness(0, 5, 0, 0), Height = 24 };
                row3.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });
                row3.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row3.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var lblRow3 = new TextBlock
                {
                    Text = sizeSlider != null ? "Wielkość:" : "Geometria:",
                    Foreground = TacticalConfigPanel.BrushMutedText,
                    FontSize = 9.5,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(lblRow3, 0);
                row3.Children.Add(lblRow3);

                if (sizeSlider != null)
                {
                    sizeSlider.VerticalAlignment = VerticalAlignment.Center;
                    sizeSlider.Margin = new Thickness(0, 0, 8, 0);
                    Grid.SetColumn(sizeSlider, 1);
                    row3.Children.Add(sizeSlider);
                }
                else
                {
                    var actionsBar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    if (typeBtn != null)
                    {
                        typeBtn.Width = double.NaN;
                        typeBtn.Height = 22;
                        typeBtn.Padding = new Thickness(8, 0, 8, 0);
                        typeBtn.Margin = new Thickness(0, 0, 6, 0);
                        typeBtn.Background = new SolidColorBrush(Color.FromRgb(7, 17, 21));
                        typeBtn.Foreground = TacticalConfigPanel.BrushAccent;
                        typeBtn.BorderBrush = TacticalConfigPanel.BrushAccent;
                        typeBtn.BorderThickness = new Thickness(1);
                        typeBtn.FontSize = 9.5;
                        typeBtn.FontWeight = FontWeights.SemiBold;
                        typeBtn.Content = "🔁 Zmień typ (AO/Sektor/Trasa)";
                        typeBtn.Cursor = Cursors.Hand;
                        actionsBar.Children.Add(typeBtn);
                    }
                    if (simplifyBtn != null)
                    {
                        simplifyBtn.Width = double.NaN;
                        simplifyBtn.Height = 22;
                        simplifyBtn.Padding = new Thickness(8, 0, 8, 0);
                        simplifyBtn.Margin = new Thickness(0, 0, 6, 0);
                        simplifyBtn.Background = new SolidColorBrush(Color.FromRgb(7, 17, 21));
                        simplifyBtn.Foreground = Brushes.White;
                        simplifyBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(28, 74, 88));
                        simplifyBtn.BorderThickness = new Thickness(1);
                        simplifyBtn.FontSize = 9.5;
                        simplifyBtn.Content = "✂️ Uprość kształt";
                        simplifyBtn.Cursor = Cursors.Hand;
                        actionsBar.Children.Add(simplifyBtn);
                    }
                    Grid.SetColumn(actionsBar, 1);
                    row3.Children.Add(actionsBar);
                }

                if (counterTb != null)
                {
                    counterTb.VerticalAlignment = VerticalAlignment.Center;
                    counterTb.Foreground = TacticalConfigPanel.BrushAccent;
                    counterTb.FontSize = 10;
                    counterTb.FontWeight = FontWeights.Bold;
                    counterTb.FontFamily = new FontFamily("Consolas");
                    counterTb.Margin = new Thickness(4, 0, 0, 0);
                    Grid.SetColumn(counterTb, 2);
                    row3.Children.Add(counterTb);
                }

                mainStack.Children.Add(row3);
            }

            card.Child = mainStack;
        }

        private static void CollectLogicalChildrenBounded(DependencyObject parent, List<DependencyObject> list, HashSet<DependencyObject> visited, int depth)
        {
            if (parent == null || depth > 5 || !visited.Add(parent)) return;
            if (parent is Panel pnl)
            {
                foreach (UIElement ch in pnl.Children)
                {
                    if (ch != null)
                    {
                        list.Add(ch);
                        CollectLogicalChildrenBounded(ch, list, visited, depth + 1);
                    }
                }
            }
            else if (parent is Decorator dec && dec.Child != null)
            {
                list.Add(dec.Child);
                CollectLogicalChildrenBounded(dec.Child, list, visited, depth + 1);
            }
        }

        private static bool IsDescendantFast(DependencyObject node, HashSet<DependencyObject> targets)
        {
            DependencyObject cur = node;
            for (int i = 0; i < 8 && cur != null; i++)
            {
                if (targets.Contains(cur)) return true;
                cur = LogicalTreeHelper.GetParent(cur);
            }
            return false;
        }

        private static string ExtractPlainButtonText(Button b)
        {
            if (b == null || b.Content == null) return "";
            if (b.Content is string s) return s;
            if (b.Content is TextBlock tb) return tb.Text ?? "";
            return b.Content.ToString() ?? "";
        }

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
                            if (ls.Children[1] is TextBlock subTb) subTb.Text = expectedSub;
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
                Height = 32,
                Margin = new Thickness(0, 10, 0, 0),
                Background = TacticalConfigPanel.BrushAccent,
                Foreground = new SolidColorBrush(Color.FromRgb(6, 14, 18)),
                BorderThickness = new Thickness(0),
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

            sp.Children.Add(CreateFieldLabel("Format zapisu współrzędnych na ramce:"));
            string[] formats = new[]
            {
                "DMS (49°50'58\"N) — Stopnie Minuty Sekundy",
                "DM (49°50.97'N) — Stopnie i Minuty dziesiętne",
                "DD (49.8494°N) — Stopnie dziesiętne"
            };
            sp.Children.Add(CreateDarkTacticalComboBox(formats, _wgs84FormatPreset, val => { _wgs84FormatPreset = val; TriggerWgs84Redraw(); }));

            var row2Grid = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            row2Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
            row2Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            row2Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });

            var colStep = new StackPanel();
            colStep.Children.Add(CreateFieldLabel("Gęstość linii (Krok WGS84):"));
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
            colStep.Children.Add(CreateDarkTacticalComboBox(steps, _wgs84StepPreset, val => { _wgs84StepPreset = val; TriggerWgs84Redraw(); }));
            Grid.SetColumn(colStep, 0);
            row2Grid.Children.Add(colStep);

            var colSub = new StackPanel();
            colSub.Children.Add(CreateFieldLabel("Podział wewnętrzny oczka:"));
            string[] subs = new[] { "Brak podziału", "Krzyżyki w środku (+)", "Podsiatka 2x2 (4 ćwiartki)", "Podsiatka 5x5 (25 pól)" };
            colSub.Children.Add(CreateDarkTacticalComboBox(subs, _wgs84SubGridPreset, val => { _wgs84SubGridPreset = val; TriggerWgs84Redraw(); }));
            Grid.SetColumn(colSub, 2);
            row2Grid.Children.Add(colSub);
            sp.Children.Add(row2Grid);

            var chkSnap = new CheckBox
            {
                Content = "Kotwicz linie do pełnych sekund/stopni GPS (rzeczywista siatka)",
                IsChecked = _wgs84SnapToTrueGeo,
                Foreground = Brushes.White,
                FontSize = 10,
                Margin = new Thickness(0, 2, 0, 6)
            };
            chkSnap.Checked += (s, e) => { _wgs84SnapToTrueGeo = true; TriggerWgs84Redraw(); };
            chkSnap.Unchecked += (s, e) => { _wgs84SnapToTrueGeo = false; TriggerWgs84Redraw(); };
            sp.Children.Add(chkSnap);

            sp.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(22, 56, 68)), Margin = new Thickness(0, 2, 0, 6) });
            sp.Children.Add(new TextBlock
            {
                Text = "Kalibracja na żywo (Przesunięcie N/S, E/W oraz Skala %):",
                Foreground = TacticalConfigPanel.BrushAccent,
                FontSize = 9.8,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            });

            var calTable = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            calTable.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            calTable.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            calTable.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var spN = new StackPanel();
            spN.Children.Add(CreateFieldLabel("Przesunięcie Północ/Południe (m):"));
            var tbN = CreateDarkTextBox(_wgs84OffsetNorthMeters.ToString("0.#", CultureInfo.InvariantCulture));
            if (!isModalDialog) _wgs84OffsetNBox = tbN;
            tbN.TextChanged += (s, e) =>
            {
                if (_suppressWgs84LiveTextEvents) return;
                if (double.TryParse(tbN.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double vN))
                {
                    _wgs84OffsetNorthMeters = Math.Clamp(vN, -50000.0, 50000.0);
                    TriggerWgs84Redraw();
                }
            };
            spN.Children.Add(tbN);

            var nudgeN = new UniformGrid { Columns = 4, Margin = new Thickness(0, 3, 0, 0) };
            foreach (double delta in new[] { -25.0, -5.0, 5.0, 25.0 })
            {
                double d = delta;
                nudgeN.Children.Add(CreateMiniNudgeButton(d > 0 ? $"+{d:0}m" : $"{d:0}m", () =>
                {
                    _suppressWgs84LiveTextEvents = true;
                    _wgs84OffsetNorthMeters += d;
                    string sVal = _wgs84OffsetNorthMeters.ToString("0.#", CultureInfo.InvariantCulture);
                    tbN.Text = sVal;
                    if (_wgs84OffsetNBox != null) _wgs84OffsetNBox.Text = sVal;
                    _suppressWgs84LiveTextEvents = false;
                    TriggerWgs84Redraw();
                }));
            }
            spN.Children.Add(nudgeN);
            Grid.SetColumn(spN, 0);
            calTable.Children.Add(spN);

            var spE = new StackPanel();
            spE.Children.Add(CreateFieldLabel("Przesunięcie Wschód/Zachód (m):"));
            var tbE = CreateDarkTextBox(_wgs84OffsetEastMeters.ToString("0.#", CultureInfo.InvariantCulture));
            if (!isModalDialog) _wgs84OffsetEBox = tbE;
            tbE.TextChanged += (s, e) =>
            {
                if (_suppressWgs84LiveTextEvents) return;
                if (double.TryParse(tbE.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double vE))
                {
                    _wgs84OffsetEastMeters = Math.Clamp(vE, -50000.0, 50000.0);
                    TriggerWgs84Redraw();
                }
            };
            spE.Children.Add(tbE);

            var nudgeE = new UniformGrid { Columns = 4, Margin = new Thickness(0, 3, 0, 0) };
            foreach (double delta in new[] { -25.0, -5.0, 5.0, 25.0 })
            {
                double d = delta;
                nudgeE.Children.Add(CreateMiniNudgeButton(d > 0 ? $"+{d:0}m" : $"{d:0}m", () =>
                {
                    _suppressWgs84LiveTextEvents = true;
                    _wgs84OffsetEastMeters += d;
                    string sVal = _wgs84OffsetEastMeters.ToString("0.#", CultureInfo.InvariantCulture);
                    tbE.Text = sVal;
                    if (_wgs84OffsetEBox != null) _wgs84OffsetEBox.Text = sVal;
                    _suppressWgs84LiveTextEvents = false;
                    TriggerWgs84Redraw();
                }));
            }
            spE.Children.Add(nudgeE);
            Grid.SetColumn(spE, 2);
            calTable.Children.Add(spE);
            sp.Children.Add(calTable);

            var scaleGrid = new Grid { Margin = new Thickness(0, 2, 0, 4) };
            scaleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            scaleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            scaleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var spSx = new StackPanel();
            spSx.Children.Add(CreateFieldLabel("Korekta szerokości X (%):"));
            var tbSx = CreateDarkTextBox(_wgs84ScaleXPercent.ToString("0.#", CultureInfo.InvariantCulture));
            if (!isModalDialog) _wgs84ScaleXBox = tbSx;
            tbSx.TextChanged += (s, e) =>
            {
                if (_suppressWgs84LiveTextEvents) return;
                if (double.TryParse(tbSx.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double vSx) && vSx >= 20.0 && vSx <= 500.0)
                {
                    _wgs84ScaleXPercent = vSx;
                    TriggerWgs84Redraw();
                }
            };
            spSx.Children.Add(tbSx);
            Grid.SetColumn(spSx, 0);
            scaleGrid.Children.Add(spSx);

            var spSy = new StackPanel();
            spSy.Children.Add(CreateFieldLabel("Korekta wysokości Y (%):"));
            var tbSy = CreateDarkTextBox(_wgs84ScaleYPercent.ToString("0.#", CultureInfo.InvariantCulture));
            if (!isModalDialog) _wgs84ScaleYBox = tbSy;
            tbSy.TextChanged += (s, e) =>
            {
                if (_suppressWgs84LiveTextEvents) return;
                if (double.TryParse(tbSy.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double vSy) && vSy >= 20.0 && vSy <= 500.0)
                {
                    _wgs84ScaleYPercent = vSy;
                    TriggerWgs84Redraw();
                }
            };
            spSy.Children.Add(tbSy);
            Grid.SetColumn(spSy, 2);
            scaleGrid.Children.Add(spSy);
            sp.Children.Add(scaleGrid);

            sp.Children.Add(CreateFieldLabel("Współrzędne środka arkusza (WGS84 Lat N / Lon E):"));
            var centerGrid = new Grid { Margin = new Thickness(0, 2, 0, 6) };
            centerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            centerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            centerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var tbLat = CreateDarkTextBox(__LAT_FIELD__.ToString("F6", CultureInfo.InvariantCulture), isAccentText: true);
            if (!isModalDialog) _wgs84CenterLatOverrideBox = tbLat;
            Grid.SetColumn(tbLat, 0);
            centerGrid.Children.Add(tbLat);

            var tbLon = CreateDarkTextBox(__LON_FIELD__.ToString("F6", CultureInfo.InvariantCulture), isAccentText: true);
            if (!isModalDialog) _wgs84CenterLonOverrideBox = tbLon;
            Grid.SetColumn(tbLon, 2);
            centerGrid.Children.Add(tbLon);
            sp.Children.Add(centerGrid);

            var btnRow = new Grid();
            btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
            btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var btnApply = new Button
            {
                Content = "✓ ZASTOSUJ WSPÓŁRZĘDNE ŚRODKA",
                Height = 28,
                Background = TacticalConfigPanel.BrushAccent,
                Foreground = new SolidColorBrush(Color.FromRgb(6, 14, 18)),
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.Bold,
                FontSize = 9.5,
                Cursor = Cursors.Hand
            };
            btnApply.Click += (s, e) =>
            {
                if (double.TryParse(tbLat.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double nLat) && nLat >= -85.0 && nLat <= 85.0)
                    __LAT_FIELD__ = nLat;
                if (double.TryParse(tbLon.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double nLon) && nLon >= -180.0 && nLon <= 180.0)
                    __LON_FIELD__ = nLon;
                TriggerWgs84Redraw();
            };
            Grid.SetColumn(btnApply, 0);
            btnRow.Children.Add(btnApply);

            var btnReset = new Button
            {
                Content = "↺ Reset (0m / 100%)",
                Height = 28,
                Background = new SolidColorBrush(Color.FromRgb(7, 17, 21)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(22, 56, 68)),
                BorderThickness = new Thickness(1),
                FontSize = 9.5,
                Cursor = Cursors.Hand
            };
            btnReset.Click += (s, e) =>
            {
                _suppressWgs84LiveTextEvents = true;
                _wgs84OffsetNorthMeters = 0.0;
                _wgs84OffsetEastMeters = 0.0;
                _wgs84ScaleXPercent = 100.0;
                _wgs84ScaleYPercent = 100.0;
                tbN.Text = "0"; tbE.Text = "0"; tbSx.Text = "100"; tbSy.Text = "100";
                if (_wgs84OffsetNBox != null) _wgs84OffsetNBox.Text = "0";
                if (_wgs84OffsetEBox != null) _wgs84OffsetEBox.Text = "0";
                if (_wgs84ScaleXBox != null) _wgs84ScaleXBox.Text = "100";
                if (_wgs84ScaleYBox != null) _wgs84ScaleYBox.Text = "100";
                _suppressWgs84LiveTextEvents = false;
                TriggerWgs84Redraw();
            };
            Grid.SetColumn(btnReset, 2);
            btnRow.Children.Add(btnReset);
            sp.Children.Add(btnRow);

            card.Child = sp;
            return card;
        }

        private static TextBlock CreateFieldLabel(string text)
        {
            return new TextBlock { Text = text, Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 9.5, Margin = new Thickness(0, 1, 0, 2) };
        }

        private static TextBox CreateDarkTextBox(string initialText, bool isAccentText = false)
        {
            return new TextBox
            {
                Text = initialText,
                Height = 25,
                Background = new SolidColorBrush(Color.FromRgb(7, 17, 21)),
                Foreground = isAccentText ? TacticalConfigPanel.BrushAccent : Brushes.White,
                CaretBrush = TacticalConfigPanel.BrushAccent,
                BorderBrush = new SolidColorBrush(Color.FromRgb(22, 56, 68)),
                BorderThickness = new Thickness(1),
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(6, 0, 6, 0),
                FontSize = 10.5,
                FontWeight = isAccentText ? FontWeights.SemiBold : FontWeights.Normal
            };
        }

        private static ComboBox CreateDarkTacticalComboBox(IEnumerable<string> items, string selectedItem, Action<string> onChanged)
        {
            var cb = new ComboBox { Height = 27, FontSize = 10.5, Margin = new Thickness(0, 0, 0, 6) };
            ApplyDarkTacticalComboBoxTemplate(cb);

            string matched = null;
            foreach (var item in items) { cb.Items.Add(item); if (item == selectedItem) matched = item; }
            cb.SelectedItem = matched ?? cb.Items.Cast<object>().FirstOrDefault();
            cb.SelectionChanged += (s, e) => { if (cb.SelectedItem is string val) onChanged?.Invoke(val); };
            return cb;
        }

        private static void ApplyDarkTacticalComboBoxTemplate(ComboBox cb)
        {
            var bgBrush = new SolidColorBrush(Color.FromRgb(7, 17, 21));
            var popupBgBrush = new SolidColorBrush(Color.FromRgb(10, 24, 30));
            var borderBrush = new SolidColorBrush(Color.FromRgb(22, 56, 68));
            var accentBrush = TacticalConfigPanel.BrushAccent;

            cb.Background = bgBrush;
            cb.Foreground = Brushes.White;
            cb.BorderBrush = borderBrush;
            cb.BorderThickness = new Thickness(1);

            var itemStyle = new Style(typeof(ComboBoxItem));
            itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, popupBgBrush));
            itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            itemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5)));

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(18, 48, 58))));
            hoverTrigger.Setters.Add(new Setter(Control.ForegroundProperty, accentBrush));
            itemStyle.Triggers.Add(hoverTrigger);

            var selTrigger = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selTrigger.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(14, 40, 48))));
            selTrigger.Setters.Add(new Setter(Control.ForegroundProperty, accentBrush));
            itemStyle.Triggers.Add(selTrigger);
            cb.ItemContainerStyle = itemStyle;

            var template = new ControlTemplate(typeof(ComboBox));
            var rootGrid = new FrameworkElementFactory(typeof(Grid));

            var toggleBtn = new FrameworkElementFactory(typeof(ToggleButton));
            toggleBtn.SetValue(Control.BackgroundProperty, bgBrush);
            toggleBtn.SetValue(Control.BorderBrushProperty, borderBrush);
            toggleBtn.SetValue(Control.BorderThicknessProperty, new Thickness(1));
            toggleBtn.SetValue(ToggleButton.FocusableProperty, false);
            toggleBtn.SetValue(ToggleButton.ClickModeProperty, ClickMode.Press);
            toggleBtn.SetBinding(ToggleButton.IsCheckedProperty, new System.Windows.Data.Binding("IsDropDownOpen")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent),
                Mode = System.Windows.Data.BindingMode.TwoWay
            });

            var tbTemplate = new ControlTemplate(typeof(ToggleButton));
            var tbBorder = new FrameworkElementFactory(typeof(Border));
            tbBorder.SetValue(Border.BackgroundProperty, bgBrush);
            tbBorder.SetValue(Border.BorderBrushProperty, borderBrush);
            tbBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            tbBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));

            var arrowText = new FrameworkElementFactory(typeof(TextBlock));
            arrowText.SetValue(TextBlock.TextProperty, "▾");
            arrowText.SetValue(TextBlock.ForegroundProperty, accentBrush);
            arrowText.SetValue(TextBlock.FontSizeProperty, 11.0);
            arrowText.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            arrowText.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            arrowText.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            tbBorder.AppendChild(arrowText);
            tbTemplate.VisualTree = tbBorder;
            toggleBtn.SetValue(Control.TemplateProperty, tbTemplate);
            rootGrid.AppendChild(toggleBtn);

            var contentPres = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPres.SetValue(FrameworkElement.NameProperty, "ContentSite");
            contentPres.SetValue(UIElement.IsHitTestVisibleProperty, false);
            contentPres.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemProperty));
            contentPres.SetValue(ContentPresenter.ContentTemplateProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemTemplateProperty));
            contentPres.SetValue(ContentPresenter.ContentStringFormatProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemStringFormatProperty));
            contentPres.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 3, 24, 3));
            contentPres.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            contentPres.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            rootGrid.AppendChild(contentPres);

            var popup = new FrameworkElementFactory(typeof(Popup));
            popup.SetValue(FrameworkElement.NameProperty, "Popup");
            popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
            popup.SetValue(Popup.AllowsTransparencyProperty, true);
            popup.SetValue(Popup.FocusableProperty, false);
            popup.SetValue(Popup.PopupAnimationProperty, PopupAnimation.Slide);
            popup.SetBinding(Popup.IsOpenProperty, new System.Windows.Data.Binding("IsDropDownOpen")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });

            var dropBorder = new FrameworkElementFactory(typeof(Border));
            dropBorder.SetValue(Border.BackgroundProperty, popupBgBrush);
            dropBorder.SetValue(Border.BorderBrushProperty, accentBrush);
            dropBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            dropBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            dropBorder.SetBinding(FrameworkElement.MinWidthProperty, new System.Windows.Data.Binding("ActualWidth")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });
            dropBorder.SetValue(FrameworkElement.MaxHeightProperty, 260.0);

            var scrollViewer = new FrameworkElementFactory(typeof(ScrollViewer));
            scrollViewer.SetValue(ScrollViewer.CanContentScrollProperty, true);
            scrollViewer.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
            dropBorder.AppendChild(scrollViewer);
            popup.AppendChild(dropBorder);
            rootGrid.AppendChild(popup);

            template.VisualTree = rootGrid;
            template.Seal();
            cb.Template = template;
        }

        private static Button CreateMiniNudgeButton(string label, Action onClick)
        {
            var b = new Button
            {
                Content = label,
                Height = 20,
                Margin = new Thickness(1, 0, 1, 0),
                Background = new SolidColorBrush(Color.FromRgb(7, 17, 21)),
                Foreground = TacticalConfigPanel.BrushAccent,
                BorderBrush = new SolidColorBrush(Color.FromRgb(22, 56, 68)),
                BorderThickness = new Thickness(1),
                FontSize = 9,
                Cursor = Cursors.Hand
            };
            b.Click += (s, e) => onClick();
            return b;
        }
//===END_STEP2_UI_AND_TABLE_CS===