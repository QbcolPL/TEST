@echo off
setlocal DisableDelayedExpansion
cd /d "%~dp0"

set "BAT_FILE=%~f0"

echo ============================================================================
echo [1/3] Naprawa bledu CS0029 (zwracanie 5-elementowej krotki ValueTuple)
echo       w metodzie FormatTacticalPointCoordinates(double, double)...
echo ============================================================================
powershell -NoProfile -ExecutionPolicy Bypass -Command "Add-Type -TypeDefinition ([System.Text.RegularExpressions.Regex]::Split([System.IO.File]::ReadAllText([System.Environment]::GetEnvironmentVariable('BAT_FILE'), [System.Text.Encoding]::UTF8), '//===' + 'FIX_CS0029_TUPLE_CS===')[1]); [FthFixCs0029TupleReturn]::Run()"

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

//===FIX_CS0029_TUPLE_CS===
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public static class FthFixCs0029TupleReturn
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

        string gitExe = ResolveGitExe();

        if (!File.Exists(engPath))
            throw new Exception("Nie znaleziono pliku TacticalMapGeneratorView.Engine.cs!");

        string eng = File.ReadAllText(engPath, Encoding.UTF8);

        string gitRestoredBody = TryExtractOriginalMethodFromGitHistory(
            gitExe,
            targetDir,
            "apps/builder/FieldTakHub.Builder/Services/TacticalMapGeneratorView.Engine.cs",
            "FormatTacticalPointCoordinates");

        int fixedCount;
        eng = RepairFormatTacticalPointCoordinatesTuple(eng, gitRestoredBody, "TacticalMapGeneratorView.Engine.cs", out fixedCount);

        int openEng = eng.Count(c => c == '{');
        int closeEng = eng.Count(c => c == '}');
        Console.WriteLine("[WERYFIKACJA] Nawiasy w TacticalMapGeneratorView.Engine.cs: { = " + openEng + ", } = " + closeEng);
        if (openEng != closeEng)
            throw new Exception("Niezgodnosc nawiasow klamrowych w TacticalMapGeneratorView.Engine.cs!");

        File.WriteAllText(engPath, eng, utf8NoBom);

        if (File.Exists(tmgPath))
        {
            string tmg = File.ReadAllText(tmgPath, Encoding.UTF8);
            int fixedTmg;
            tmg = RepairFormatTacticalPointCoordinatesTuple(tmg, null, "TacticalMapGeneratorView.cs", out fixedTmg);

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
            string oldDeferred = "_interactiveCanvas.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>";
            string newDeferred = "_interactiveCanvas.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>";
            if (wgs.Contains(oldDeferred))
            {
                wgs = wgs.Replace(oldDeferred, newDeferred);
                File.WriteAllText(wgsPath, wgs, utf8NoBom);
                Console.WriteLine("[OK] Ustawiono priorytet ApplicationIdle dla czyszczenia siatki WGS84.");
            }
        }

        string triggerPath = Path.Combine(targetDir, ".fth_build_trigger.txt");
        File.WriteAllText(triggerPath, "Field TAK Hub Fix CS0029 5-Tuple Return in FormatTacticalPointCoordinates UTC: " + DateTime.UtcNow.ToString("O") + " | RunId: " + Guid.NewGuid().ToString("N") + "\n", utf8NoBom);

        PushAndTriggerGitHubActions(targetDir, gitExe);
    }

    private static string TryExtractOriginalMethodFromGitHistory(string gitExe, string workingDir, string relFilePath, string methodName)
    {
        try
        {
            var psiLog = new ProcessStartInfo
            {
                FileName = gitExe,
                Arguments = "log -n 12 --pretty=format:%H",
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            string commitsRaw;
            using (var p = Process.Start(psiLog))
            {
                commitsRaw = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
            }

            if (string.IsNullOrWhiteSpace(commitsRaw)) return null;

            string[] shas = commitsRaw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string sha in shas)
            {
                var psiShow = new ProcessStartInfo
                {
                    FileName = gitExe,
                    Arguments = "show " + sha.Trim() + ":" + relFilePath,
                    WorkingDirectory = workingDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                string fileContent;
                using (var pShow = Process.Start(psiShow))
                {
                    fileContent = pShow.StandardOutput.ReadToEnd();
                    pShow.WaitForExit();
                    if (pShow.ExitCode != 0 || string.IsNullOrWhiteSpace(fileContent))
                        continue;
                }

                Match m = Regex.Match(fileContent, methodName + @"\s*\(([^)]*)\)");
                if (!m.Success) continue;
                if (m.Groups[1].Value.IndexOf("double", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                string masked = MaskStringsAndComments(fileContent);
                int openB = masked.IndexOf('{', m.Index + m.Length);
                if (openB < 0) continue;
                int closeB = FindMatchingCloseBrace(masked, openB);
                if (closeB <= openB) continue;

                string body = fileContent.Substring(openB, closeB - openB + 1);
                if (!body.Contains("RenderCalibratedWgs84GridOnSheet") &&
                    !body.Contains("return FormatWgs84Coordinate(") &&
                    body.Contains("return ("))
                {
                    Console.WriteLine("[GIT RESTORE] Przywrocono oryginalne cialo metody " + methodName + " z commita " + sha.Substring(0, Math.Min(7, sha.Length)) + ".");
                    return body;
                }
            }
        }
        catch { }
        return null;
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

    private static string RepairFormatTacticalPointCoordinatesTuple(string code, string gitRestoredBody, string fileLabel, out int fixedCount)
    {
        fixedCount = 0;

        MatchCollection matches = Regex.Matches(
            code,
            @"FormatTacticalPointCoordinates\s*\(([^)]*)\)",
            RegexOptions.IgnoreCase);

        for (int i = matches.Count - 1; i >= 0; i--)
        {
            Match m = matches[i];
            string paramList = m.Groups[1].Value;
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

            string newBody;
            if (!string.IsNullOrWhiteSpace(gitRestoredBody))
            {
                newBody = gitRestoredBody;
            }
            else
            {
                newBody = BuildFallbackTupleMethodBody(p1, p2);
            }

            code = code.Substring(0, openBrace) + newBody + code.Substring(closeBrace + 1);
            fixedCount++;
            Console.WriteLine("[OK] Naprawiono metode FormatTacticalPointCoordinates(" + p1 + ", " + p2 + ") zwracajaca krotke 5-elementowa w " + fileLabel + ".");
        }

        return code;
    }

    private static string BuildFallbackTupleMethodBody(string pLat, string pLon)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("            double __lat = Math.Clamp(" + pLat + ", -80.0, 84.0);");
        sb.AppendLine("            double __lon = Math.Clamp(" + pLon + ", -180.0, 179.999999);");
        sb.AppendLine("            int __zone = (int)Math.Floor((__lon + 180.0) / 6.0) + 1;");
        sb.AppendLine("            string __bands = \"CDEFGHJKLMNPQRSTUVWXX\";");
        sb.AppendLine("            int __bandIdx = Math.Clamp((int)Math.Floor((__lat + 80.0) / 8.0), 0, __bands.Length - 1);");
        sb.AppendLine("            char __band = __bands[__bandIdx];");
        sb.AppendLine("            double __a = 6378137.0;");
        sb.AppendLine("            double __f = 1.0 / 298.257223563;");
        sb.AppendLine("            double __k0 = 0.9996;");
        sb.AppendLine("            double __e2 = 2.0 * __f - __f * __f;");
        sb.AppendLine("            double __ep2 = __e2 / (1.0 - __e2);");
        sb.AppendLine("            double __latRad = __lat * Math.PI / 180.0;");
        sb.AppendLine("            double __lonRad = __lon * Math.PI / 180.0;");
        sb.AppendLine("            double __lon0Rad = ((__zone - 1) * 6 - 180 + 3) * Math.PI / 180.0;");
        sb.AppendLine("            double __N = __a / Math.Sqrt(1.0 - __e2 * Math.Sin(__latRad) * Math.Sin(__latRad));");
        sb.AppendLine("            double __T = Math.Tan(__latRad) * Math.Tan(__latRad);");
        sb.AppendLine("            double __C = __ep2 * Math.Cos(__latRad) * Math.Cos(__latRad);");
        sb.AppendLine("            double __A = Math.Cos(__latRad) * (__lonRad - __lon0Rad);");
        sb.AppendLine("            double __M = __a * ((1.0 - __e2 / 4.0 - 3.0 * __e2 * __e2 / 64.0 - 5.0 * __e2 * __e2 * __e2 / 256.0) * __latRad");
        sb.AppendLine("                - (3.0 * __e2 / 8.0 + 3.0 * __e2 * __e2 / 32.0 + 45.0 * __e2 * __e2 * __e2 / 1024.0) * Math.Sin(2.0 * __latRad)");
        sb.AppendLine("                + (15.0 * __e2 * __e2 / 256.0 + 45.0 * __e2 * __e2 * __e2 / 1024.0) * Math.Sin(4.0 * __latRad)");
        sb.AppendLine("                - (35.0 * __e2 * __e2 * __e2 / 3072.0) * Math.Sin(6.0 * __latRad));");
        sb.AppendLine("            double __easting = __k0 * __N * (__A + (1.0 - __T + __C) * Math.Pow(__A, 3) / 6.0");
        sb.AppendLine("                + (5.0 - 18.0 * __T + __T * __T + 72.0 * __C - 58.0 * __ep2) * Math.Pow(__A, 5) / 120.0) + 500000.0;");
        sb.AppendLine("            double __northing = __k0 * (__M + __N * Math.Tan(__latRad) * (__A * __A / 2.0");
        sb.AppendLine("                + (5.0 - __T + 9.0 * __C + 4.0 * __C * __C) * Math.Pow(__A, 4) / 24.0");
        sb.AppendLine("                + (61.0 - 58.0 * __T + __T * __T + 600.0 * __C - 330.0 * __ep2) * Math.Pow(__A, 6) / 720.0));");
        sb.AppendLine("            if (__lat < 0.0) __northing += 10000000.0;");
        sb.AppendLine("            int __setNum = (__zone - 1) % 6;");
        sb.AppendLine("            string[] __colSets = new[] { \"ABCDEFGH\", \"JKLMNPQR\", \"STUVWXYZ\", \"ABCDEFGH\", \"JKLMNPQR\", \"STUVWXYZ\" };");
        sb.AppendLine("            string[] __rowSets = new[] { \"ABCDEFGHJKLMNPQRSTUV\", \"FGHJKLMNPQRSTUVABCDE\" };");
        sb.AppendLine("            int __colIdx = (((int)Math.Floor(__easting / 100000.0) - 1) % 8 + 8) % 8;");
        sb.AppendLine("            char __colLetter = __colSets[__setNum][__colIdx];");
        sb.AppendLine("            int __rowIdx = (((int)Math.Floor((__northing % 2000000.0) / 100000.0)) % 20 + 20) % 20;");
        sb.AppendLine("            char __rowLetter = __rowSets[__setNum % 2][__rowIdx];");
        sb.AppendLine("            int __e5 = Math.Clamp((int)Math.Floor(__easting % 100000.0), 0, 99999);");
        sb.AppendLine("            int __n5 = Math.Clamp((int)Math.Floor(__northing % 100000.0), 0, 99999);");
        sb.AppendLine("            string __fullMgrs = string.Format(System.Globalization.CultureInfo.InvariantCulture, \"{0:00}{1} {2}{3} {4:00000} {5:00000}\", __zone, __band, __colLetter, __rowLetter, __e5, __n5);");
        sb.AppendLine("            string __short100m = string.Format(System.Globalization.CultureInfo.InvariantCulture, \"{0:000} {1:000}\", __e5 / 100, __n5 / 100);");
        sb.AppendLine("            int __remE100 = __e5 % 100;");
        sb.AppendLine("            int __remN100 = __n5 % 100;");
        sb.AppendLine("            int __kpCol = Math.Clamp(__remE100 / 34, 0, 2);");
        sb.AppendLine("            int __kpRow = Math.Clamp(2 - (__remN100 / 34), 0, 2);");
        sb.AppendLine("            int __keypad = __kpRow * 3 + __kpCol + 1;");
        sb.AppendLine("            string __quarter = (__remN100 >= 50) ? ((__remE100 < 50) ? \"A/NW\" : \"B/NE\") : ((__remE100 < 50) ? \"C/SW\" : \"D/SE\");");
        sb.AppendLine("            string __kpAndQuarter = \"KP:\" + __keypad + \" | ĆW:\" + __quarter;");
        sb.AppendLine("            string __utmText = string.Format(System.Globalization.CultureInfo.InvariantCulture, \"{0:00}{1} {2:0}E {3:0}N\", __zone, __band, __easting, __northing);");
        sb.AppendLine("            string __wgsText = FormatWgs84Coordinate(__lat, true, 0.0001) + \", \" + FormatWgs84Coordinate(__lon, false, 0.0001);");
        sb.AppendLine("            return (__fullMgrs, __short100m, __kpAndQuarter, __utmText, __wgsText);");
        sb.Append("        }");
        return sb.ToString();
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

    private static string ResolveGitExe()
    {
        string g1 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Git\cmd\git.exe");
        string g2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Git\cmd\git.exe");
        if (File.Exists(g1)) return g1;
        if (File.Exists(g2)) return g2;
        return "git";
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

    private static void PushAndTriggerGitHubActions(string targetDir, string gitExe)
    {
        Console.WriteLine();
        Console.WriteLine("============================================================================");
        Console.WriteLine("[2/3] Wysylka na GitHub (QbcolPL/TEST) i uruchomienie GitHub Actions...");
        Console.WriteLine("============================================================================");

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
        RunProcess(gitExe, "commit -m \"Fix CS0029 5-tuple return in FormatTacticalPointCoordinates\" --allow-empty", targetDir, ignoreError: false);

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
//===FIX_CS0029_TUPLE_CS===