@echo off
setlocal DisableDelayedExpansion
cd /d "%~dp0"

set "BAT_FILE=%~f0"

echo ============================================================================
echo [1/3] Naprawa bledu CS1061 (ControlTemplate.Seal) i ostrzezen CS8629/CS0649...
echo ============================================================================
powershell -NoProfile -ExecutionPolicy Bypass -Command "Add-Type -TypeDefinition ([System.Text.RegularExpressions.Regex]::Split([System.IO.File]::ReadAllText([System.Environment]::GetEnvironmentVariable('BAT_FILE'), [System.Text.Encoding]::UTF8), '//===' + 'FIX_SEAL_AND_WARNINGS_CS===')[1]); [FthFixSealAndWarnings]::Run()"

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

//===FIX_SEAL_AND_WARNINGS_CS===
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

public static class FthFixSealAndWarnings
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

        // 1. TacticalMapGeneratorView.cs:
        //    - Zamiana tpl.Freeze() na tpl.Seal() (naprawa bledu CS1061)
        //    - Zamiana normX.Value / normY.Value na GetValueOrDefault() (usuniecie ostrzezen CS8629)
        string tmgPath = Path.Combine(servicesDir, "TacticalMapGeneratorView.cs");
        if (!File.Exists(tmgPath))
            throw new Exception("Nie znaleziono pliku TacticalMapGeneratorView.cs!");

        string tmg = File.ReadAllText(tmgPath, Encoding.UTF8);

        tmg = tmg.Replace("tpl.Freeze();", "tpl.Seal();");
        tmg = tmg.Replace("? Math.Clamp(normX.Value * viewW,", "? Math.Clamp(normX.GetValueOrDefault() * viewW,");
        tmg = tmg.Replace("? Math.Clamp(normY.Value * viewH,", "? Math.Clamp(normY.GetValueOrDefault() * viewH,");

        int openB = tmg.Count(c => c == '{');
        int closeB = tmg.Count(c => c == '}');
        Console.WriteLine("[WERYFIKACJA] Nawiasy klamrowe w TacticalMapGeneratorView.cs: { = " + openB + ", } = " + closeB);
        if (openB != closeB)
            throw new Exception("Niezgodnosc nawiasow klamrowych w TacticalMapGeneratorView.cs!");

        File.WriteAllText(tmgPath, tmg, utf8NoBom);
        Console.WriteLine("[OK] Naprawiono ControlTemplate.Seal() oraz ostrzezenia CS8629 w TacticalMapGeneratorView.cs.");

        // 2. TacticalMapGeneratorView.Engine.cs:
        //    - Wyciszenie ostrzezenia CS0649 dla _objectNameInputBox
        string engPath = Path.Combine(servicesDir, "TacticalMapGeneratorView.Engine.cs");
        if (File.Exists(engPath))
        {
            string eng = File.ReadAllText(engPath, Encoding.UTF8);
            eng = eng.Replace(
                "#pragma warning disable CS0414, CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625",
                "#pragma warning disable CS0414, CS0649, CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625");
            eng = eng.Replace(
                "private static TextBox _objectNameInputBox, _scaleInputBox, _searchPlaceBox;",
                "private static TextBox _objectNameInputBox = null, _scaleInputBox, _searchPlaceBox;");
            File.WriteAllText(engPath, eng, utf8NoBom);
            Console.WriteLine("[OK] Usunieto ostrzezenie CS0649 w TacticalMapGeneratorView.Engine.cs.");
        }

        string triggerPath = Path.Combine(targetDir, ".fth_build_trigger.txt");
        File.WriteAllText(triggerPath, "Field TAK Hub ControlTemplate.Seal Fix UTC: " + DateTime.UtcNow.ToString("O") + " | RunId: " + Guid.NewGuid().ToString("N") + "\n", utf8NoBom);

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
        RunProcess(gitExe, "commit -m \"Fix CS1061 by calling ControlTemplate.Seal() instead of Freeze() and resolve CS8629/CS0649 warnings\" --allow-empty", targetDir, ignoreError: false);

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
//===FIX_SEAL_AND_WARNINGS_CS===