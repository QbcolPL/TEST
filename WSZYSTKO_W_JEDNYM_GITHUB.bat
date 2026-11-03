@echo off
setlocal DisableDelayedExpansion
cd /d "%~dp0"

set "BAT_FILE=%~f0"

echo ============================================================================
echo [ETAP 2] Konsolidacja potoku renderowania Generatora OPORD i usuniecie
echo          opoznionego przebudowywania klatek (Dispatcher.BeginInvoke)...
echo ============================================================================
powershell -NoProfile -ExecutionPolicy Bypass -Command "Add-Type -TypeDefinition ([System.Text.RegularExpressions.Regex]::Split([System.IO.File]::ReadAllText([System.Environment]::GetEnvironmentVariable('BAT_FILE'), [System.Text.Encoding]::UTF8), '//===' + 'ETAP2_OPORD_CLEAN_CS===')[1]); [FthEtap2OpordCleaner]::Run()"

if errorlevel 1 (
    echo.
    echo [BLAD] Wystapil blad podczas wykonywania Etapu 2 lub wysylki na GitHub!
    pause
    exit /b 1
)

echo.
echo ============================================================================
echo [SUKCES - ETAP 2] Kod Generatora OPORD i potoku startowego zostal
echo uporzadkowany i wyslany na GitHub (QbcolPL/TEST)!
echo Kompilacja na GitHub Actions zostala uruchomiona.
echo ============================================================================
pause
exit /b 0

//===ETAP2_OPORD_CLEAN_CS===
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public static class FthEtap2OpordCleaner
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

        // 1. Konsolidacja synchronicznego budowania widoku w BuilderLocalizationAndOpordEngine.UiPart.cs
        CleanOpordUiPartPipeline(servicesDir, utf8NoBom);

        // 2. Uporzadkowanie V34CleanOpordDataEngine w BuilderLocalizationAndOpordEngine.cs (brak opoznienia BeginInvoke i dokladne rozwiazywanie aktywnej strony)
        CleanOpordDataEnginePipeline(servicesDir, utf8NoBom);

        // 3. Usuniecie nadmiarowych przebiegow startowych w BuilderUiSimplifier.cs i BuilderCoreSidebarAndProjectEngine.cs
        CleanRedundantStartupPasses(servicesDir, utf8NoBom);

        // 4. Znacznik wyzwalajacy dla GitHub Actions i wysylka
        string triggerPath = Path.Combine(targetDir, ".fth_build_trigger.txt");
        File.WriteAllText(triggerPath, "Field TAK Hub Etap 2 Clean OPORD Pipeline UTC: " + DateTime.UtcNow.ToString("O") + " | RunId: " + Guid.NewGuid().ToString("N") + "\n", utf8NoBom);

        PushAndTriggerGitHubActions(targetDir);
    }

    private static void CleanOpordUiPartPipeline(string servicesDir, UTF8Encoding utf8NoBom)
    {
        string uiPartPath = Path.Combine(servicesDir, "BuilderLocalizationAndOpordEngine.UiPart.cs");
        if (!File.Exists(uiPartPath))
            throw new Exception("Nie znaleziono pliku BuilderLocalizationAndOpordEngine.UiPart.cs!");

        string uiPart = File.ReadAllText(uiPartPath, Encoding.UTF8);

        // A. Usun asynchroniczny Dispatcher.BeginInvoke z poczatku RebuildOpordForm(), ktory powodowal migniecie formularza w drugiej klatce
        uiPart = uiPart.Replace(
            "win?.Dispatcher.BeginInvoke(new Action(() => V34CleanOpordUiTransformer.ApplyToOpordForm(win, mainStack, _activeOpordFaction, RebuildOpordForm)), System.Windows.Threading.DispatcherPriority.Loaded);",
            "// [ETAP2_SYNC_PIPELINE] ApplyToOpordForm runs synchronously at the end of RebuildOpordForm");

        // B. Upewnij sie, ze ActiveOpordFactionName jest dostepne dla silnika danych
        if (!uiPart.Contains("internal static string ActiveOpordFactionName => _activeOpordFaction;"))
        {
            uiPart = uiPart.Replace(
                "public static Action RebuildActiveOpordViewAction;",
                "public static Action RebuildActiveOpordViewAction;\r\n        internal static string ActiveOpordFactionName => _activeOpordFaction;");
        }

        // C. Wywoluj V34CleanOpordUiTransformer.ApplyToOpordForm synchronicznie bezposrednio na koncu RebuildOpordForm()
        //    (przed zamknieciem funkcji lokalnej RebuildOpordForm i przed pierwszym jej wywolaniem)
        uiPart = uiPart.Replace(
            "RebuildOpordForm();\r\n            V34CleanOpordUiTransformer.ApplyToOpordForm(win, mainStack, _activeOpordFaction, RebuildOpordForm);\r\n            return rootBorder;",
            "RebuildOpordForm();\r\n            return rootBorder;");

        uiPart = uiPart.Replace(
            "RebuildOpordForm();\n            V34CleanOpordUiTransformer.ApplyToOpordForm(win, mainStack, _activeOpordFaction, RebuildOpordForm);\n            return rootBorder;",
            "RebuildOpordForm();\n            return rootBorder;");

        if (!uiPart.Contains("// [ETAP2_END_OF_REBUILD_HOOK]"))
        {
            uiPart = Regex.Replace(
                uiPart,
                @"(\}\s*)(RebuildOpordForm\(\);\s*return\s+rootBorder;)",
                "    // [ETAP2_END_OF_REBUILD_HOOK]\r\n                V34CleanOpordUiTransformer.ApplyToOpordForm(win, mainStack, _activeOpordFaction, RebuildOpordForm);\r\n            $1$2");
        }

        int openB = uiPart.Count(c => c == '{');
        int closeB = uiPart.Count(c => c == '}');
        Console.WriteLine("[WERYFIKACJA] Nawiasy klamrowe w BuilderLocalizationAndOpordEngine.UiPart.cs: { = " + openB + ", } = " + closeB);
        if (openB != closeB)
            throw new Exception("Niezgodnosc nawiasow klamrowych w BuilderLocalizationAndOpordEngine.UiPart.cs!");

        File.WriteAllText(uiPartPath, uiPart, utf8NoBom);
        Console.WriteLine("[OK] Skonsolidowano synchroniczny potok RebuildOpordForm w BuilderLocalizationAndOpordEngine.UiPart.cs.");
    }

    private static void CleanOpordDataEnginePipeline(string servicesDir, UTF8Encoding utf8NoBom)
    {
        string opEnginePath = Path.Combine(servicesDir, "BuilderLocalizationAndOpordEngine.cs");
        if (!File.Exists(opEnginePath))
            throw new Exception("Nie znaleziono pliku BuilderLocalizationAndOpordEngine.cs!");

        string opEng = File.ReadAllText(opEnginePath, Encoding.UTF8);

        // Zastap metode OnSwitchedToOpordTab oraz GetLiveOpordModel wersja synchroniczna (bez Dispatcher.BeginInvoke)
        int swStart = opEng.IndexOf("public static void OnSwitchedToOpordTab(Window win, string modeKey)", StringComparison.Ordinal);
        int isGarbIdx = opEng.IndexOf("public static bool IsGarbageFactionName(string name)", StringComparison.Ordinal);
        if (swStart > 0 && isGarbIdx > swStart)
        {
            string cleanDataEngineMethods = @"public static void OnSwitchedToOpordTab(Window win, string modeKey)
        {
            if (string.IsNullOrWhiteSpace(modeKey) || !modeKey.Contains(""OPORD"", StringComparison.OrdinalIgnoreCase))
                return;

            var w = win ?? Application.Current?.MainWindow;
            if (w == null || _isRefreshingOpord) return;

            try
            {
                _isRefreshingOpord = true;
                SyncFactionsAndMapToOpordModel(w);
                OpordBuilderManager.RebuildActiveOpordViewAction?.Invoke();
            }
            catch { }
            finally
            {
                _isRefreshingOpord = false;
            }
        }

        internal static OpordParagraphModel GetLiveOpordModel()
        {
            try
            {
                string activeFac = OpordBuilderManager.ActiveOpordFactionName;
                if (string.IsNullOrWhiteSpace(activeFac) || IsGarbageFactionName(activeFac))
                {
                    var factions = TacticalConfigPanel.GetInternalFactionsList();
                    activeFac = (factions != null && factions.Count > 0 && !string.IsNullOrWhiteSpace(factions[0].Name))
                        ? factions[0].Name
                        : ""BLUFOR"";
                }
                return OpordBuilderManager.GetOrCreateModel(activeFac);
            }
            catch
            {
                return null;
            }
        }

        ";
            opEng = opEng.Substring(0, swStart) + cleanDataEngineMethods + opEng.Substring(isGarbIdx);
        }

        int openB = opEng.Count(c => c == '{');
        int closeB = opEng.Count(c => c == '}');
        Console.WriteLine("[WERYFIKACJA] Nawiasy klamrowe w BuilderLocalizationAndOpordEngine.cs: { = " + openB + ", } = " + closeB);
        if (openB != closeB)
            throw new Exception("Niezgodnosc nawiasow klamrowych w BuilderLocalizationAndOpordEngine.cs!");

        File.WriteAllText(opEnginePath, opEng, utf8NoBom);
        Console.WriteLine("[OK] Uporzadkowano V34CleanOpordDataEngine w BuilderLocalizationAndOpordEngine.cs.");
    }

    private static void CleanRedundantStartupPasses(string servicesDir, UTF8Encoding utf8NoBom)
    {
        // A. BuilderUiSimplifier.cs — usuniecie dublujacych sie przebiegow ContentRendered i Background przy starcie
        string simpPath = Path.Combine(servicesDir, "BuilderUiSimplifier.cs");
        if (File.Exists(simpPath))
        {
            string simp = File.ReadAllText(simpPath, Encoding.UTF8);
            simp = simp.Replace(
                "win.ContentRendered += (_, __) => EnforceSimplification(win);",
                "// [ETAP2_CLEAN] ContentRendered pass removed to prevent multi-frame startup bounce");
            simp = simp.Replace(
                "_ = win.Dispatcher.BeginInvoke(new Action(() => EnforceSimplification(win)), DispatcherPriority.Background);",
                "// [ETAP2_CLEAN] Background pass removed to prevent multi-frame startup bounce");
            File.WriteAllText(simpPath, simp, utf8NoBom);
            Console.WriteLine("[OK] Usunieto nadmiarowe przebiegi startowe w BuilderUiSimplifier.cs.");
        }

        // B. BuilderCoreSidebarAndProjectEngine.cs — usuniecie dublujacego ContentRendered
        string corePath = Path.Combine(servicesDir, "BuilderCoreSidebarAndProjectEngine.cs");
        if (File.Exists(corePath))
        {
            string core = File.ReadAllText(corePath, Encoding.UTF8);
            core = core.Replace(
                "win.ContentRendered += (_, __) => EnsureRootDockSidebar(win);",
                "// [ETAP2_CLEAN] ContentRendered duplicate sidebar pass removed");
            File.WriteAllText(corePath, core, utf8NoBom);
            Console.WriteLine("[OK] Usunieto nadmiarowy przebieg ContentRendered w BuilderCoreSidebarAndProjectEngine.cs.");
        }
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
        RunProcess(gitExe, "commit -m \"Stage 2 Refactor: Consolidate synchronous OPORD rendering pipeline and remove duplicate startup passes\" --allow-empty", targetDir, ignoreError: false);

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
//===ETAP2_OPORD_CLEAN_CS===