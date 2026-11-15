@echo off
setlocal DisableDelayedExpansion
cd /d "%~dp0"

set "BAT_FILE=%~f0"

echo ============================================================================
echo [ETAP 1/2] Wdrazanie automatycznego ukrywania pol specyficznych dla MGRS
echo            ('Podzial oczek MGRS' i 'Opisy kratek') po przelaczeniu na WGS84...
echo ============================================================================
powershell -NoProfile -ExecutionPolicy Bypass -Command "Add-Type -TypeDefinition ([System.Text.RegularExpressions.Regex]::Split([System.IO.File]::ReadAllText([System.Environment]::GetEnvironmentVariable('BAT_FILE'), [System.Text.Encoding]::UTF8), '//===' + 'STAGE1_HIDE_MGRS_ON_WGS84_CS===')[1]); [FthStage1HideMgrsFieldsOnWgs84]::Run()"

if errorlevel 1 (
    echo.
    echo [BLAD] Wystapil blad podczas wykonywania Etapu 1 lub wysylki na GitHub!
    pause
    exit /b 1
)

echo.
echo ============================================================================
echo [SUKCES - ETAP 1/2] Ukrywanie pol MGRS dla systemu WGS84 zostalo wdrozone
echo i wyslane na GitHub (QbcolPL/TEST)! Teraz mozna przejsc do Etapu 2.
echo ============================================================================
pause
exit /b 0

//===STAGE1_HIDE_MGRS_ON_WGS84_CS===
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public static class FthStage1HideMgrsFieldsOnWgs84
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

        if (!File.Exists(wgsPath))
            throw new Exception("Nie znaleziono pliku TacticalMapGeneratorView.Wgs84.cs!");

        // 1. Podpiecie natychmiastowego przelaczania widocznosci kontrolek MGRS/WGS84 przy zmianie _coordSystem
        if (File.Exists(tmgPath))
        {
            string tmg = File.ReadAllText(tmgPath, Encoding.UTF8);
            tmg = InjectCoordSystemSelectionCallback(tmg, "TacticalMapGeneratorView.cs");
            int openTmg = tmg.Count(c => c == '{');
            int closeTmg = tmg.Count(c => c == '}');
            if (openTmg != closeTmg)
                throw new Exception("Niezgodnosc nawiasow klamrowych w TacticalMapGeneratorView.cs!");
            File.WriteAllText(tmgPath, tmg, utf8NoBom);
        }

        if (File.Exists(engPath))
        {
            string eng = File.ReadAllText(engPath, Encoding.UTF8);
            eng = InjectCoordSystemSelectionCallback(eng, "TacticalMapGeneratorView.Engine.cs");
            int openEng = eng.Count(c => c == '{');
            int closeEng = eng.Count(c => c == '}');
            if (openEng != closeEng)
                throw new Exception("Niezgodnosc nawiasow klamrowych w TacticalMapGeneratorView.Engine.cs!");
            File.WriteAllText(engPath, eng, utf8NoBom);
        }

        // 2. Dodanie metody DiscoverAndToggleMgrsSpecificControls do TacticalMapGeneratorView.Wgs84.cs
        string batFile = Environment.GetEnvironmentVariable("BAT_FILE");
        string rawBat = File.ReadAllText(batFile, Encoding.UTF8);

        string[] parts = Regex.Split(rawBat, "//===" + "PAYLOAD_ETAP1_HIDE_MGRS_CS===");
        if (parts.Length < 2)
            throw new Exception("Brak bloku PAYLOAD_ETAP1_HIDE_MGRS_CS w pliku .bat!");
        string[] endParts = Regex.Split(parts[1], "//===" + "END_ETAP1_HIDE_MGRS_CS===");
        string hideMgrsSnippet = endParts[0].Trim();

        string wgs = File.ReadAllText(wgsPath, Encoding.UTF8);

        if (!wgs.Contains("DiscoverAndToggleMgrsSpecificControls"))
        {
            string targetAnchor = "public static void UpdateWgs84CustomizationPanelVisibility()";
            int anchorIdx = wgs.IndexOf(targetAnchor, StringComparison.Ordinal);
            if (anchorIdx < 0)
                throw new Exception("Nie znaleziono metody UpdateWgs84CustomizationPanelVisibility w TacticalMapGeneratorView.Wgs84.cs!");

            wgs = wgs.Substring(0, anchorIdx) + hideMgrsSnippet + "\r\n\r\n        " + wgs.Substring(anchorIdx);

            string oldCheck = "bool isWgs = IsWgs84ModeActive();";
            string newCheck = "bool isWgs = IsWgs84ModeActive();\r\n            DiscoverAndToggleMgrsSpecificControls(isWgs);";
            int updIdx = wgs.IndexOf(targetAnchor, StringComparison.Ordinal);
            int chkIdx = wgs.IndexOf(oldCheck, updIdx, StringComparison.Ordinal);
            if (chkIdx >= 0)
            {
                wgs = wgs.Substring(0, chkIdx) + newCheck + wgs.Substring(chkIdx + oldCheck.Length);
            }

            string oldAttach = "parentPanel.Children.Add(panel);";
            string newAttach = "parentPanel.Children.Add(panel);\r\n            panel.Loaded += (s, e) => UpdateWgs84CustomizationPanelVisibility();";
            if (wgs.Contains(oldAttach) && !wgs.Contains("panel.Loaded +="))
            {
                wgs = wgs.Replace(oldAttach, newAttach);
            }
        }

        int openWgs = wgs.Count(c => c == '{');
        int closeWgs = wgs.Count(c => c == '}');
        Console.WriteLine("[WERYFIKACJA] Nawiasy w TacticalMapGeneratorView.Wgs84.cs: { = " + openWgs + ", } = " + closeWgs);
        if (openWgs != closeWgs)
            throw new Exception("Niezgodnosc nawiasow klamrowych w TacticalMapGeneratorView.Wgs84.cs!");

        File.WriteAllText(wgsPath, wgs, utf8NoBom);
        Console.WriteLine("[OK] Wdrozono automatyczne ukrywanie pol 'Podzial oczek MGRS' i 'Opisy kratek' dla systemu WGS84.");

        string triggerPath = Path.Combine(targetDir, ".fth_build_trigger.txt");
        File.WriteAllText(triggerPath, "Field TAK Hub Stage 1 Hide MGRS Controls on WGS84 UTC: " + DateTime.UtcNow.ToString("O") + " | RunId: " + Guid.NewGuid().ToString("N") + "\n", utf8NoBom);

        PushAndTriggerGitHubActions(targetDir);
    }

    private static string InjectCoordSystemSelectionCallback(string code, string fileLabel)
    {
        // Dopisz UpdateWgs84CustomizationPanelVisibility() po przypisaniu _coordSystem = ... wewnatrz metod (z pominieciem deklaracji pola statycznego)
        string pattern = @"(?<!static\s+string\s+)(_coordSystem\s*=\s*[^;]+;)";
        int count = 0;
        code = Regex.Replace(code, pattern, m =>
        {
            string fullMatch = m.Groups[1].Value;
            int pos = m.Index;
            string before = code.Substring(Math.Max(0, pos - 40), Math.Min(40, pos));
            if (before.Contains("string ")) return fullMatch;

            string after = code.Substring(pos + fullMatch.Length, Math.Min(80, code.Length - (pos + fullMatch.Length)));
            if (after.Contains("UpdateWgs84CustomizationPanelVisibility"))
                return fullMatch;
            count++;
            return fullMatch + " UpdateWgs84CustomizationPanelVisibility();";
        });

        if (count > 0)
        {
            Console.WriteLine("[OK] Podpieto natychmiastowe przelaczanie widocznosci MGRS/WGS84 w " + fileLabel + " (" + count + " miejsc).");
        }
        return code;
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
        RunProcess(gitExe, "commit -m \"Stage 1/2: Automatically hide MGRS-specific controls when WGS84 is selected\" --allow-empty", targetDir);

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
//===STAGE1_HIDE_MGRS_ON_WGS84_CS===

//===PAYLOAD_ETAP1_HIDE_MGRS_CS===
private static readonly List<UIElement> _mgrsSpecificUiElements = new List<UIElement>();

        private static void DiscoverAndToggleMgrsSpecificControls(bool hideMgrsControls)
        {
            if (_wgs84ConfigPanelBorder == null) return;

            if (_mgrsSpecificUiElements.Count == 0)
            {
                DependencyObject sectionRoot = LogicalTreeHelper.GetParent(_wgs84ConfigPanelBorder)
                                               ?? VisualTreeHelper.GetParent(_wgs84ConfigPanelBorder);
                if (sectionRoot != null)
                {
                    var allNodes = new List<DependencyObject>();
                    var visited = new HashSet<DependencyObject>();
                    CollectLogicalChildrenBounded(sectionRoot, allNodes, visited, 0);

                    foreach (var tb in allNodes.OfType<TextBlock>())
                    {
                        string txt = (tb.Text ?? "").Trim();
                        if (txt.IndexOf("Podział oczek MGRS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            txt.IndexOf("Opisy kratek", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            txt.IndexOf("Rozmiar oczka MGRS", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            DependencyObject tbParent = LogicalTreeHelper.GetParent(tb) ?? VisualTreeHelper.GetParent(tb);
                            if (tbParent is Panel parentPnl && !ReferenceEquals(parentPnl, sectionRoot))
                            {
                                if (!_mgrsSpecificUiElements.Contains(parentPnl))
                                    _mgrsSpecificUiElements.Add(parentPnl);

                                DependencyObject grandParent = LogicalTreeHelper.GetParent(parentPnl) ?? VisualTreeHelper.GetParent(parentPnl);
                                if (grandParent is Grid parentGrid && !ReferenceEquals(grandParent, sectionRoot))
                                {
                                    if (!_mgrsSpecificUiElements.Contains(parentGrid))
                                        _mgrsSpecificUiElements.Add(parentGrid);
                                }
                            }
                            else if (tbParent is Panel mainPnl)
                            {
                                if (!_mgrsSpecificUiElements.Contains(tb))
                                    _mgrsSpecificUiElements.Add(tb);

                                int idx = mainPnl.Children.IndexOf(tb);
                                if (idx >= 0 && idx + 1 < mainPnl.Children.Count)
                                {
                                    UIElement nextEl = mainPnl.Children[idx + 1];
                                    if ((nextEl is ComboBox || nextEl is Grid || nextEl is StackPanel) &&
                                        !ReferenceEquals(nextEl, _wgs84ConfigPanelBorder) &&
                                        !_mgrsSpecificUiElements.Contains(nextEl))
                                    {
                                        _mgrsSpecificUiElements.Add(nextEl);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            Visibility targetVis = hideMgrsControls ? Visibility.Collapsed : Visibility.Visible;
            foreach (var el in _mgrsSpecificUiElements)
            {
                if (el != null)
                    el.Visibility = targetVis;
            }
        }
//===END_ETAP1_HIDE_MGRS_CS===