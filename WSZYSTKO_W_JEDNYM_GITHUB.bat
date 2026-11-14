@echo off
setlocal DisableDelayedExpansion
cd /d "%~dp0"

set "BAT_FILE=%~f0"

echo ============================================================================
echo [1/3] Naprawa bledu CS1520/CS0501 w TacticalMapGeneratorView.Engine.cs (wiersz 319)...
echo ============================================================================
powershell -NoProfile -ExecutionPolicy Bypass -Command "Add-Type -TypeDefinition ([System.Text.RegularExpressions.Regex]::Split([System.IO.File]::ReadAllText([System.Environment]::GetEnvironmentVariable('BAT_FILE'), [System.Text.Encoding]::UTF8), '//===' + 'FIX_CS1520_ENGINE_CS===')[1]); [FthFixCs1520Engine]::Run()"

if errorlevel 1 (
    echo.
    echo [BLAD] Wystapil blad podczas aktualizacji plikow lub wysylki na GitHub!
    pause
    exit /b 1
)

echo.
echo ============================================================================
echo [SUKCES] Poprawka wyslana na GitHub (QbcolPL/TEST)!
echo Kompilacja na GitHub Actions zostala uruchomiona.
echo ============================================================================
pause
exit /b 0

//===FIX_CS1520_ENGINE_CS===
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

public static class FthFixCs1520Engine
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

        // 1. Oczyszczenie TacticalMapGeneratorView.Engine.cs z wywolania wstrzyknietego w deklaracje pola (wiersz 319)
        if (File.Exists(engPath))
        {
            string eng = File.ReadAllText(engPath, Encoding.UTF8);
            eng = eng.Replace(" UpdateWgs84CustomizationPanelVisibility();", "");
            eng = eng.Replace("UpdateWgs84CustomizationPanelVisibility();", "");
            File.WriteAllText(engPath, eng, utf8NoBom);
            Console.WriteLine("[OK] Oczyszczono deklaracje pola _coordSystem w TacticalMapGeneratorView.Engine.cs.");
        }

        // 2. Oczyszczenie TacticalMapGeneratorView.cs i umieszczenie UpdateWgs84CustomizationPanelVisibility() wewnatrz metody DrawDraggableCornerElements
        string tmg = File.ReadAllText(tmgPath, Encoding.UTF8);
        tmg = tmg.Replace(" UpdateWgs84CustomizationPanelVisibility();", "");
        tmg = tmg.Replace("UpdateWgs84CustomizationPanelVisibility();", "");

        string oldHook = "if (_coordSystem != null && _coordSystem.StartsWith(\"WGS84\", StringComparison.OrdinalIgnoreCase)) { RenderCalibratedWgs84GridOnSheet(w, h); }";
        string newHook = "UpdateWgs84CustomizationPanelVisibility(); if (_coordSystem != null && _coordSystem.IndexOf(\"WGS84\", StringComparison.OrdinalIgnoreCase) >= 0) { RenderCalibratedWgs84GridOnSheet(w, h); }";

        if (tmg.Contains(oldHook))
        {
            tmg = tmg.Replace(oldHook, newHook);
        }
        else if (!tmg.Contains("RenderCalibratedWgs84GridOnSheet(w, h)"))
        {
            string targetSig = "private static void DrawDraggableCornerElements(double w, double h)";
            int sigIdx = tmg.IndexOf(targetSig, StringComparison.Ordinal);
            if (sigIdx >= 0)
            {
                int openBrace = tmg.IndexOf('{', sigIdx);
                if (openBrace >= 0)
                {
                    tmg = tmg.Substring(0, openBrace + 1) +
                          "\r\n            " + newHook +
                          tmg.Substring(openBrace + 1);
                }
            }
        }

        int openB = tmg.Count(c => c == '{');
        int closeB = tmg.Count(c => c == '}');
        Console.WriteLine("[WERYFIKACJA] Nawiasy w TacticalMapGeneratorView.cs: { = " + openB + ", } = " + closeB);
        if (openB != closeB)
            throw new Exception("Niezgodnosc nawiasow klamrowych w TacticalMapGeneratorView.cs!");

        File.WriteAllText(tmgPath, tmg, utf8NoBom);
        Console.WriteLine("[OK] Zaktualizowano TacticalMapGeneratorView.cs.");

        // 3. Uelastycznienie sprawdzania "WGS84" w TacticalMapGeneratorView.Wgs84.cs
        if (File.Exists(wgsPath))
        {
            string wgs = File.ReadAllText(wgsPath, Encoding.UTF8);
            wgs = wgs.Replace(
                "_coordSystem.StartsWith(\"WGS84\", StringComparison.OrdinalIgnoreCase)",
                "(_coordSystem.IndexOf(\"WGS84\", StringComparison.OrdinalIgnoreCase) >= 0)");
            File.WriteAllText(wgsPath, wgs, utf8NoBom);
            Console.WriteLine("[OK] Zaktualizowano warunek rozpoznawania WGS84 w TacticalMapGeneratorView.Wgs84.cs.");
        }

        string triggerPath = Path.Combine(targetDir, ".fth_build_trigger.txt");
        File.WriteAllText(triggerPath, "Field TAK Hub Fix CS1520 Engine Line 319 UTC: " + DateTime.UtcNow.ToString("O") + " | RunId: " + Guid.NewGuid().ToString("N") + "\n", utf8NoBom);

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
        RunProcess(gitExe, "commit -m \"Fix CS1520/CS0501 at line 319 in TacticalMapGeneratorView.Engine.cs\" --allow-empty", targetDir, ignoreError: false);

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
//===FIX_CS1520_ENGINE_CS===