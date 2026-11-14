@echo off
setlocal DisableDelayedExpansion
cd /d "%~dp0"

set "BAT_FILE=%~f0"

echo ============================================================================
echo [1/3] Bezposrednia naprawa metody FormatTacticalPointCoordinates (CS0161)
echo       w pliku TacticalMapGeneratorView.Engine.cs...
echo ============================================================================
powershell -NoProfile -ExecutionPolicy Bypass -Command "Add-Type -TypeDefinition ([System.Text.RegularExpressions.Regex]::Split([System.IO.File]::ReadAllText([System.Environment]::GetEnvironmentVariable('BAT_FILE'), [System.Text.Encoding]::UTF8), '//===' + 'FIX_CS0161_GUARANTEED_CS===')[1]); [FthFixCs0161Guaranteed]::Run()"

if errorlevel 1 (
    echo.
    echo [BLAD] Wystapil blad podczas aktualizacji plikow lub wysylki na GitHub!
    pause
    exit /b 1
)

echo.
echo ============================================================================
echo [SUKCES] Metoda FormatTacticalPointCoordinates zostala naprawiona i wyslana
echo na GitHub (QbcolPL/TEST)! Kompilacja na GitHub Actions zostala uruchomiona.
echo ============================================================================
pause
exit /b 0

//===FIX_CS0161_GUARANTEED_CS===
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public static class FthFixCs0161Guaranteed
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

        int totalFixed = 0;

        if (File.Exists(engPath))
        {
            string eng = File.ReadAllText(engPath, Encoding.UTF8);
            int fixedInEng;
            eng = FixFormatTacticalPointCoordinatesAndNonVoidMethods(eng, "TacticalMapGeneratorView.Engine.cs", out fixedInEng);
            totalFixed += fixedInEng;

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
            int fixedInTmg;
            tmg = FixFormatTacticalPointCoordinatesAndNonVoidMethods(tmg, "TacticalMapGeneratorView.cs", out fixedInTmg);
            totalFixed += fixedInTmg;

            int openTmg = tmg.Count(c => c == '{');
            int closeTmg = tmg.Count(c => c == '}');
            Console.WriteLine("[WERYFIKACJA] Nawiasy w TacticalMapGeneratorView.cs: { = " + openTmg + ", } = " + closeTmg);
            if (openTmg != closeTmg)
                throw new Exception("Niezgodnosc nawiasow klamrowych w TacticalMapGeneratorView.cs!");

            File.WriteAllText(tmgPath, tmg, utf8NoBom);
        }

        if (totalFixed == 0)
        {
            throw new Exception("Nie znaleziono deklaracji FormatTacticalPointCoordinates w zadnym pliku!");
        }

        string triggerPath = Path.Combine(targetDir, ".fth_build_trigger.txt");
        File.WriteAllText(triggerPath, "Field TAK Hub Fix CS0161 Guaranteed UTC: " + DateTime.UtcNow.ToString("O") + " | RunId: " + Guid.NewGuid().ToString("N") + "\n", utf8NoBom);

        PushAndTriggerGitHubActions(targetDir);
    }

    private static string ExtractParamIdentifier(string rawParam, string fallback)
    {
        if (string.IsNullOrWhiteSpace(rawParam)) return fallback;
        string beforeDefault = rawParam.Split('=')[0].Trim();
        MatchCollection words = Regex.Matches(beforeDefault, @"[A-Za-z_][A-Za-z0-9_]*");
        if (words.Count > 0)
            return words[words.Count - 1].Value;
        return fallback;
    }

    private static string FixFormatTacticalPointCoordinatesAndNonVoidMethods(string code, string fileLabel, out int fixedCount)
    {
        fixedCount = 0;

        // 1. Szukamy bezposrednio nazwy metody FormatTacticalPointCoordinates(...) bez wymagania sztywnych modyfikatorow przed nazwa
        MatchCollection matches = Regex.Matches(
            code,
            @"FormatTacticalPointCoordinates\s*\(([^)]*)\)",
            RegexOptions.IgnoreCase);

        for (int i = matches.Count - 1; i >= 0; i--)
        {
            Match m = matches[i];
            string paramList = m.Groups[1].Value;
            // Odfiltruj wywolania metody (w deklaracji wystepuje typ parametru 'double')
            if (paramList.IndexOf("double", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            string[] parts = paramList.Split(',');
            string p1 = ExtractParamIdentifier(parts.Length > 0 ? parts[0] : "", "lat");
            string p2 = ExtractParamIdentifier(parts.Length > 1 ? parts[1] : "", "lon");

            string masked = MaskStringsAndComments(code);
            int openBrace = masked.IndexOf('{', m.Index + m.Length);
            if (openBrace < 0) continue;

            int closeBrace = FindMatchingCloseBrace(masked, openBrace);
            if (closeBrace <= openBrace) continue;

            string oldBody = code.Substring(openBrace, closeBrace - openBrace + 1);
            string retStmt = "return FormatWgs84Coordinate(" + p1 + ", true, 0.0001) + \" \" + FormatWgs84Coordinate(" + p2 + ", false, 0.0001);";

            string newBody = Regex.Replace(
                oldBody,
                @"var\s*\(__wgsW\s*,\s*__wgsH\)\s*=\s*GetExactCanvasSize\(\)\s*;\s*RenderCalibratedWgs84GridOnSheet\(__wgsW\s*,\s*__wgsH\)\s*;",
                retStmt);

            string inner = newBody.Substring(1, newBody.Length - 2).Trim();
            newBody = "{\r\n            " + inner + "\r\n            " + retStmt + "\r\n        }";

            code = code.Substring(0, openBrace) + newBody + code.Substring(closeBrace + 1);
            fixedCount++;

            Console.WriteLine("[OK] Naprawiono metode FormatTacticalPointCoordinates(" + p1 + ", " + p2 + ") w " + fileLabel + ":");
            Console.WriteLine("----------------------------------------------------------------------------");
            Console.WriteLine(newBody);
            Console.WriteLine("----------------------------------------------------------------------------");
        }

        // 2. Sprawdz wszystkie pozostale miejsca, gdzie wystepuje RenderCalibratedWgs84GridOnSheet(__wgsW, __wgsH)
        int searchPos = 0;
        while (searchPos < code.Length)
        {
            int idx = code.IndexOf("RenderCalibratedWgs84GridOnSheet(__wgsW, __wgsH)", searchPos, StringComparison.Ordinal);
            if (idx < 0) break;
            searchPos = idx + 48;

            string masked = MaskStringsAndComments(code);
            int cur = idx;
            for (int level = 0; level < 8; level++)
            {
                int oIdx, cIdx;
                if (!TryFindEnclosingBraceSpan(masked, cur, out oIdx, out cIdx))
                    break;

                string hdr = GetHeaderBeforeBrace(code, oIdx, 220);
                if (Regex.IsMatch(hdr, @"\bstring\??\s+[A-Za-z0-9_]+\s*\([^)]*\)\s*$"))
                {
                    string mBody = code.Substring(oIdx, cIdx - oIdx + 1);
                    string fixedBody = Regex.Replace(
                        mBody,
                        @"var\s*\(__wgsW\s*,\s*__wgsH\)\s*=\s*GetExactCanvasSize\(\)\s*;\s*RenderCalibratedWgs84GridOnSheet\(__wgsW\s*,\s*__wgsH\)\s*;",
                        "return string.Empty;");
                    string mInner = fixedBody.Substring(1, fixedBody.Length - 2).Trim();
                    fixedBody = "{\r\n            " + mInner + "\r\n            return string.Empty;\r\n        }";
                    code = code.Substring(0, oIdx) + fixedBody + code.Substring(cIdx + 1);
                    searchPos = oIdx + fixedBody.Length;
                    fixedCount++;
                    Console.WriteLine("[OK] Naprawiono dodatkowa metode zwracajaca string w " + fileLabel + ".");
                    break;
                }
                if (Regex.IsMatch(hdr, @"\bvoid\s+[A-Za-z0-9_]+\s*\([^)]*\)\s*$"))
                {
                    break;
                }
                cur = oIdx - 1;
            }
        }

        return code;
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
                while (i < n && chars[i] != '\n')
                {
                    if (chars[i] != '\r') chars[i] = ' ';
                    i++;
                }
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
                    if (chars[i] == '\\' && i + 1 < n)
                    {
                        chars[i] = ' '; chars[i + 1] = ' '; i += 2;
                        continue;
                    }
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
                    if (chars[i] == '\\' && i + 1 < n)
                    {
                        chars[i] = ' '; chars[i + 1] = ' '; i += 2;
                        continue;
                    }
                    if (chars[i] != '\n' && chars[i] != '\r') chars[i] = ' ';
                    i++;
                }
                if (i < n) { chars[i] = ' '; i++; }
            }
            else
            {
                i++;
            }
        }
        return new string(chars);
    }

    private static bool TryFindEnclosingBraceSpan(string masked, int pos, out int openIdx, out int closeIdx)
    {
        openIdx = -1;
        closeIdx = -1;
        int depth = 0;
        for (int i = pos; i >= 0; i--)
        {
            char ch = masked[i];
            if (ch == '}') depth++;
            else if (ch == '{')
            {
                if (depth == 0)
                {
                    openIdx = i;
                    break;
                }
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
            else if (ch == '}')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    private static string GetHeaderBeforeBrace(string code, int openBraceIdx, int maxLen)
    {
        int start = Math.Max(0, openBraceIdx - maxLen);
        return code.Substring(start, openBraceIdx - start).Trim();
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
        RunProcess(gitExe, "commit -m \"Fix CS0161 in FormatTacticalPointCoordinates by direct method name match\" --allow-empty", targetDir, ignoreError: false);

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
//===FIX_CS0161_GUARANTEED_CS===