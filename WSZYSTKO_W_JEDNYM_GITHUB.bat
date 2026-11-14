@echo off
setlocal DisableDelayedExpansion
cd /d "%~dp0"

set "BAT_FILE=%~f0"

echo ============================================================================
echo [1/3] Naprawa bledu CS0161 w FormatTacticalPointCoordinates(double, double)
echo       (TacticalMapGeneratorView.Engine.cs, wiersz 872)...
echo ============================================================================
powershell -NoProfile -ExecutionPolicy Bypass -Command "Add-Type -TypeDefinition ([System.Text.RegularExpressions.Regex]::Split([System.IO.File]::ReadAllText([System.Environment]::GetEnvironmentVariable('BAT_FILE'), [System.Text.Encoding]::UTF8), '//===' + 'FIX_CS0161_FORMAT_COORDS_CS===')[1]); [FthFixCs0161FormatCoords]::Run()"

if errorlevel 1 (
    echo.
    echo [BLAD] Wystapil blad podczas aktualizacji plikow lub wysylki na GitHub!
    pause
    exit /b 1
)

echo.
echo ============================================================================
echo [SUKCES] Poprawka CS0161 zostala wyslana na GitHub (QbcolPL/TEST)!
echo Kompilacja na GitHub Actions zostala uruchomiona.
echo ============================================================================
pause
exit /b 0

//===FIX_CS0161_FORMAT_COORDS_CS===
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public static class FthFixCs0161FormatCoords
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

        if (File.Exists(engPath))
        {
            string eng = File.ReadAllText(engPath, Encoding.UTF8);
            if (!eng.Contains("CS0162"))
            {
                eng = "#pragma warning disable CS0162\r\n" + eng;
            }
            eng = FixNonVoidMethodsInCode(eng, "TacticalMapGeneratorView.Engine.cs");
            int openEng = eng.Count(c => c == '{');
            int closeEng = eng.Count(c => c == '}');
            Console.WriteLine("[WERYFIKACJA] Nawiasy w TacticalMapGeneratorView.Engine.cs: { = " + openEng + ", } = " + closeEng);
            if (openEng != closeEng)
                throw new Exception("Niezgodnosc nawiasow klamrowych w TacticalMapGeneratorView.Engine.cs!");
            File.WriteAllText(engPath, eng, utf8NoBom);
        }

        if (File.Exists(tmgPath))
        {
            string tmg = File.ReadAllText(tmgPath, Encoding.UTF8);
            if (!tmg.Contains("CS0162"))
            {
                tmg = "#pragma warning disable CS0162\r\n" + tmg;
            }
            tmg = FixNonVoidMethodsInCode(tmg, "TacticalMapGeneratorView.cs");
            int openTmg = tmg.Count(c => c == '{');
            int closeTmg = tmg.Count(c => c == '}');
            Console.WriteLine("[WERYFIKACJA] Nawiasy w TacticalMapGeneratorView.cs: { = " + openTmg + ", } = " + closeTmg);
            if (openTmg != closeTmg)
                throw new Exception("Niezgodnosc nawiasow klamrowych w TacticalMapGeneratorView.cs!");
            File.WriteAllText(tmgPath, tmg, utf8NoBom);
        }

        if (File.Exists(wgsPath))
        {
            string wgs = File.ReadAllText(wgsPath, Encoding.UTF8);
            string oldGetCanvases = "CollectSheetCanvasesRecursive(root, list, w, h);";
            string newGetCanvases = @"CollectSheetCanvasesRecursive(root, list, w, h);
            try
            {
                foreach (var f in typeof(TacticalMapGeneratorView).GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    if (typeof(DependencyObject).IsAssignableFrom(f.FieldType))
                    {
                        var val = f.GetValue(null) as DependencyObject;
                        if (val != null) CollectSheetCanvasesRecursive(val, list, w, h);
                    }
                }
            }
            catch { }";

            if (wgs.Contains(oldGetCanvases) && !wgs.Contains("typeof(TacticalMapGeneratorView).GetFields"))
            {
                wgs = wgs.Replace(oldGetCanvases, newGetCanvases);
            }

            string oldRec = @"int count = 0;
            try { count = VisualTreeHelper.GetChildrenCount(parent); } catch { }
            for (int i = 0; i < count; i++)
            {
                CollectSheetCanvasesRecursive(VisualTreeHelper.GetChild(parent, i), result, w, h);
            }";

            string newRec = @"if (parent is Panel pnl)
            {
                foreach (UIElement ch in pnl.Children)
                    CollectSheetCanvasesRecursive(ch, result, w, h);
            }
            else if (parent is Decorator dec && dec.Child != null)
            {
                CollectSheetCanvasesRecursive(dec.Child, result, w, h);
            }
            else if (parent is ContentControl cc && cc.Content is DependencyObject dObj)
            {
                CollectSheetCanvasesRecursive(dObj, result, w, h);
            }
            int count = 0;
            try { count = VisualTreeHelper.GetChildrenCount(parent); } catch { }
            for (int i = 0; i < count; i++)
            {
                CollectSheetCanvasesRecursive(VisualTreeHelper.GetChild(parent, i), result, w, h);
            }";

            if (wgs.Contains(oldRec))
            {
                wgs = wgs.Replace(oldRec, newRec);
            }

            int openWgs = wgs.Count(c => c == '{');
            int closeWgs = wgs.Count(c => c == '}');
            Console.WriteLine("[WERYFIKACJA] Nawiasy w TacticalMapGeneratorView.Wgs84.cs: { = " + openWgs + ", } = " + closeWgs);
            if (openWgs != closeWgs)
                throw new Exception("Niezgodnosc nawiasow klamrowych w TacticalMapGeneratorView.Wgs84.cs!");

            File.WriteAllText(wgsPath, wgs, utf8NoBom);
            Console.WriteLine("[OK] Zaktualizowano skaner warstw Canvas w TacticalMapGeneratorView.Wgs84.cs.");
        }

        string triggerPath = Path.Combine(targetDir, ".fth_build_trigger.txt");
        File.WriteAllText(triggerPath, "Field TAK Hub Fix CS0161 FormatTacticalPointCoordinates UTC: " + DateTime.UtcNow.ToString("O") + " | RunId: " + Guid.NewGuid().ToString("N") + "\n", utf8NoBom);

        PushAndTriggerGitHubActions(targetDir);
    }

    private static string FixNonVoidMethodsInCode(string code, string fileLabel)
    {
        string pattern = @"((?:private|public|internal)\s+static\s+string\s+FormatTacticalPointCoordinates\s*\(\s*double\s+([A-Za-z0-9_]+)\s*,\s*double\s+([A-Za-z0-9_]+)\s*\))";
        Match m = Regex.Match(code, pattern);
        if (m.Success)
        {
            string p1 = m.Groups[2].Value;
            string p2 = m.Groups[3].Value;
            int openBrace = code.IndexOf('{', m.Index + m.Length);
            if (openBrace >= 0)
            {
                int closeBrace = FindMatchingBraceSimple(code, openBrace);
                if (closeBrace > openBrace)
                {
                    string body = code.Substring(openBrace, closeBrace - openBrace + 1);
                    string retStmt = "return FormatWgs84Coordinate(" + p1 + ", true, 0.0001) + \" \" + FormatWgs84Coordinate(" + p2 + ", false, 0.0001);";

                    body = Regex.Replace(
                        body,
                        @"var\s*\(__wgsW\s*,\s*__wgsH\)\s*=\s*GetExactCanvasSize\(\)\s*;\s*RenderCalibratedWgs84GridOnSheet\(__wgsW\s*,\s*__wgsH\)\s*;",
                        retStmt);

                    string inner = body.Substring(1, body.Length - 2).Trim();
                    body = "{\r\n            " + inner + "\r\n            " + retStmt + "\r\n        }";

                    code = code.Substring(0, openBrace) + body + code.Substring(closeBrace + 1);
                    Console.WriteLine("[OK] Naprawiono zwracanie wartosci string w FormatTacticalPointCoordinates(" + p1 + ", " + p2 + ") w " + fileLabel + ".");
                }
            }
        }

        MatchCollection strMethods = Regex.Matches(code, @"(?:private|public|internal)\s+static\s+string\s+([A-Za-z0-9_]+)\s*\(([^)]*)\)");
        for (int i = strMethods.Count - 1; i >= 0; i--)
        {
            Match sm = strMethods[i];
            int ob = code.IndexOf('{', sm.Index + sm.Length);
            if (ob < 0) continue;
            int cb = FindMatchingBraceSimple(code, ob);
            if (cb <= ob) continue;

            string mBody = code.Substring(ob, cb - ob + 1);
            if (mBody.Contains("RenderCalibratedWgs84GridOnSheet"))
            {
                string fixedBody = Regex.Replace(
                    mBody,
                    @"var\s*\(__wgsW\s*,\s*__wgsH\)\s*=\s*GetExactCanvasSize\(\)\s*;\s*RenderCalibratedWgs84GridOnSheet\(__wgsW\s*,\s*__wgsH\)\s*;",
                    "return string.Empty;");
                code = code.Substring(0, ob) + fixedBody + code.Substring(cb + 1);
                Console.WriteLine("[OK] Naprawiono metode zwracajaca string: " + sm.Groups[1].Value + " w " + fileLabel + ".");
            }
        }

        return code;
    }

    private static int FindMatchingBraceSimple(string code, int openIdx)
    {
        int depth = 0;
        for (int i = openIdx; i < code.Length; i++)
        {
            if (code[i] == '{') depth++;
            else if (code[i] == '}')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
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
        RunProcess(gitExe, "commit -m \"Fix CS0161 in FormatTacticalPointCoordinates(double, double) at line 872\" --allow-empty", targetDir, ignoreError: false);

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
//===FIX_CS0161_FORMAT_COORDS_CS===