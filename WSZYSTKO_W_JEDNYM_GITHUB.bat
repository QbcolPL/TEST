@echo off
setlocal DisableDelayedExpansion
cd /d "%~dp0"

set "BAT_FILE=%~f0"

echo ============================================================================
echo [1/3] Podmiana znacznika __REDRAW_CALL__ w TacticalMapGeneratorView.Wgs84.cs
echo       (naprawa bledu CS0103 w wierszu 439)...
echo ============================================================================
powershell -NoProfile -ExecutionPolicy Bypass -Command "Add-Type -TypeDefinition ([System.Text.RegularExpressions.Regex]::Split([System.IO.File]::ReadAllText([System.Environment]::GetEnvironmentVariable('BAT_FILE'), [System.Text.Encoding]::UTF8), '//===' + 'FIX_REDRAW_PLACEHOLDER_CS===')[1]); [FthFixRedrawPlaceholder]::Run()"

if errorlevel 1 (
    echo.
    echo [BLAD] Wystapil blad podczas aktualizacji pliku lub wysylki na GitHub!
    pause
    exit /b 1
)

echo.
echo ============================================================================
echo [SUKCES] Poprawka CS0103 zostala wyslana na GitHub (QbcolPL/TEST)!
echo Kompilacja na GitHub Actions zostala uruchomiona.
echo ============================================================================
pause
exit /b 0

//===FIX_REDRAW_PLACEHOLDER_CS===
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public static class FthFixRedrawPlaceholder
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

        string tmg = File.Exists(tmgPath) ? File.ReadAllText(tmgPath, Encoding.UTF8) : "";
        string eng = File.Exists(engPath) ? File.ReadAllText(engPath, Encoding.UTF8) : "";
        string combined = tmg + "\n" + eng;

        string latField = DetectLatFieldName(combined);
        string lonField = DetectLonFieldName(combined, latField);
        string gridCanvasField = DetectGridCanvasFieldName(combined);
        string redrawCallExpr = DetectCallerOfDrawDraggableCornerElements(combined);
        string gridColorField = DetectGridColorField(combined);
        string gridThicknessField = DetectGridThicknessField(combined);

        string wgs = File.ReadAllText(wgsPath, Encoding.UTF8);

        wgs = wgs
            .Replace("__REDRAW_CALL__", redrawCallExpr)
            .Replace("__LAT_FIELD__", latField)
            .Replace("__LON_FIELD__", lonField)
            .Replace("__GRID_CANVAS__", gridCanvasField)
            .Replace("__GRID_COLOR_EXPR__", gridColorField)
            .Replace("__GRID_THICKNESS_EXPR__", gridThicknessField);

        if (wgs.Contains("__REDRAW_CALL__") || wgs.Contains("__LAT_FIELD__") || wgs.Contains("__LON_FIELD__"))
            throw new Exception("W pliku TacticalMapGeneratorView.Wgs84.cs nadal znajduja sie niezastapione znaczniki!");

        int openWgs = wgs.Count(c => c == '{');
        int closeWgs = wgs.Count(c => c == '}');
        Console.WriteLine("[WERYFIKACJA] Nawiasy w TacticalMapGeneratorView.Wgs84.cs: { = " + openWgs + ", } = " + closeWgs);
        if (openWgs != closeWgs)
            throw new Exception("Niezgodnosc nawiasow klamrowych w TacticalMapGeneratorView.Wgs84.cs!");

        File.WriteAllText(wgsPath, wgs, utf8NoBom);
        Console.WriteLine("[OK] Zastapiono znacznik __REDRAW_CALL__ wywolaniem: " + redrawCallExpr);

        string triggerPath = Path.Combine(targetDir, ".fth_build_trigger.txt");
        File.WriteAllText(triggerPath, "Field TAK Hub Fix __REDRAW_CALL__ Placeholder UTC: " + DateTime.UtcNow.ToString("O") + " | RunId: " + Guid.NewGuid().ToString("N") + "\n", utf8NoBom);

        PushAndTriggerGitHubActions(targetDir);
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

    private static string DetectGridCanvasFieldName(string combined)
    {
        string[] candidates = new[] { "_gridCanvas", "_overlayCanvas", "_mapGridCanvas", "_interactiveCanvas" };
        foreach (string c in candidates)
            if (Regex.IsMatch(combined, @"\b" + Regex.Escape(c) + @"\b")) return c;
        return "_interactiveCanvas";
    }

    private static string DetectGridColorField(string combined)
    {
        string[] candidates = new[] { "_gridColorHex", "_mainGridColorHex", "_mgrsColorHex" };
        foreach (string c in candidates)
            if (Regex.IsMatch(combined, @"string\s+" + Regex.Escape(c) + @"\b")) return c;
        return "\"#FAD02C\"";
    }

    private static string DetectGridThicknessField(string combined)
    {
        string[] candidates = new[] { "_gridLineThickness", "_mainGridThickness", "_lineThickness" };
        foreach (string c in candidates)
            if (Regex.IsMatch(combined, @"double\s+" + Regex.Escape(c) + @"\b")) return c;
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
        if (File.Exists(lockFile)) { try { File.Delete(lockFile); } catch { } }

        RunProcess(gitExe, "init", targetDir);
        RunProcess(gitExe, "config user.name \"QbcolPL\"", targetDir);
        RunProcess(gitExe, "config user.email \"QbcolPL@users.noreply.github.com\"", targetDir);
        RunProcess(gitExe, "branch -M main", targetDir);
        RunProcess(gitExe, "remote add origin " + RepoRemoteUrl, targetDir);
        RunProcess(gitExe, "remote set-url origin " + RepoRemoteUrl, targetDir);

        RunProcess(gitExe, "add -A", targetDir);
        RunProcess(gitExe, "commit -m \"Fix CS0103 __REDRAW_CALL__ placeholder in TacticalMapGeneratorView.Wgs84.cs\" --allow-empty", targetDir);

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
//===FIX_REDRAW_PLACEHOLDER_CS===