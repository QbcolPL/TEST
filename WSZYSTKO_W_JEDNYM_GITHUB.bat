@echo off
setlocal DisableDelayedExpansion
cd /d "%~dp0"

set "BAT_FILE=%~f0"

echo ============================================================================
echo [NAPRAWA / REVERT ETAPU 2] Przywracanie pojedynczego paska bocznego,
echo karty 3 przyciskow .ftak, nowego Generatora OPORD (przycisk u gory,
echo Tabela Lacznosci, METOC AO, Zalacznik nr 3) i wysylka na GitHub Actions...
echo ============================================================================
powershell -NoProfile -ExecutionPolicy Bypass -Command "Add-Type -TypeDefinition ([System.Text.RegularExpressions.Regex]::Split([System.IO.File]::ReadAllText([System.Environment]::GetEnvironmentVariable('BAT_FILE'), [System.Text.Encoding]::UTF8), '//===' + 'REVERT_STAGE2_AND_FIX_CS===')[1]); [FthRevertStage2AndFix]::Run()"

if errorlevel 1 (
    echo.
    echo [BLAD] Wystapil blad podczas przywracania plikow lub wysylki na GitHub!
    pause
    exit /b 1
)

echo.
echo ============================================================================
echo [SUKCES] Etap 2 zostal cofniety, a wszystkie moduly (Pasek boczny, Generator
echo Paczek .ftak, Generator Map i Generator OPORD z Zalacznikiem 3) przywrocone!
echo Kompilacja na GitHub Actions zostala uruchomiona.
echo ============================================================================
pause
exit /b 0

//===REVERT_STAGE2_AND_FIX_CS===
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public static class FthRevertStage2AndFix
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

        // 1. Napraw w TacticalMapGeneratorView.cs podwojne opakowywanie win.Content (prawdziwe zrodlo zdublowanego paska i kiwania sie okna)
        FixMapGeneratorDoubleShellWrap(servicesDir, utf8NoBom);

        // 2. Przywroc pelny cykl zycia w BuilderCoreSidebarAndProjectEngine.cs (usuwanie duplikatow paska bocznego)
        RestoreSidebarEngineLifecycle(servicesDir, utf8NoBom);

        // 3. Przywroc pelny cykl zycia w BuilderUiSimplifier.cs oraz TacticalConfigPanel.cs (karta 3 przyciskow .ftak i ukrycie 3. Dodatkowe opcje)
        RestorePackageSimplifierLifecycle(servicesDir, utf8NoBom);

        // 4. Przywroc wywolanie V34CleanOpordUiTransformer.ApplyToOpordForm w BuilderLocalizationAndOpordEngine.UiPart.cs i .cs
        //    (przycisk Pobierz na gorze, nowa Tabela Lacznosci, walidacja METOC AO oraz Zalacznik nr 3 z mapa)
        RestoreOpordTransformerAndAnnex3(servicesDir, utf8NoBom);

        // 5. Zapisz znacznik wyzwalajacy dla GitHub Actions i wyslij zmiany
        string triggerPath = Path.Combine(targetDir, ".fth_build_trigger.txt");
        File.WriteAllText(triggerPath, "Field TAK Hub Revert Stage 2 & Full Restore UTC: " + DateTime.UtcNow.ToString("O") + " | RunId: " + Guid.NewGuid().ToString("N") + "\n", utf8NoBom);

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

    private static void FixMapGeneratorDoubleShellWrap(string servicesDir, UTF8Encoding utf8NoBom)
    {
        string tmgPath = Path.Combine(servicesDir, "TacticalMapGeneratorView.cs");
        if (!File.Exists(tmgPath))
            throw new Exception("Nie znaleziono pliku TacticalMapGeneratorView.cs!");

        string tmg = File.ReadAllText(tmgPath, Encoding.UTF8);

        // Jezeli _fullScreenOverlay zostal juz utworzony lub win.Content to FTH_ROOT_LAYOUT_GRID,
        // NIGDY nie opakowuj okna po raz drugi podczas wywolania ContextIdle!
        tmg = tmg.Replace(
            @"if (win.Content is FrameworkElement existingFe && existingFe.Tag?.ToString() == ""FTH_MASTER_SHELL"") return;",
            @"if (_fullScreenOverlay != null || (win.Content is FrameworkElement existingFe && (existingFe.Tag?.ToString() == ""FTH_MASTER_SHELL"" || existingFe.Tag?.ToString() == ""FTH_ROOT_LAYOUT_GRID""))) return;");

        File.WriteAllText(tmgPath, tmg, utf8NoBom);
        Console.WriteLine("[OK] Zablokowano podwojne opakowywanie okna w TacticalMapGeneratorView.cs (eliminacja drugiego paska bocznego i kiwania UI).");
    }

    private static void RestoreSidebarEngineLifecycle(string servicesDir, UTF8Encoding utf8NoBom)
    {
        string corePath = Path.Combine(servicesDir, "BuilderCoreSidebarAndProjectEngine.cs");
        if (!File.Exists(corePath)) return;

        string core = File.ReadAllText(corePath, Encoding.UTF8);

        core = core.Replace(
            "// win.LayoutUpdated += (_, __) => EnforceRootSidebarColumnBounds(win); // Eliminated jitter",
            "win.LayoutUpdated += (_, __) => EnforceRootSidebarColumnBounds(win);");

        core = core.Replace(
            "// [ETAP2_CLEAN] ContentRendered duplicate sidebar pass removed",
            "win.ContentRendered += (_, __) => EnsureRootDockSidebar(win);");

        File.WriteAllText(corePath, core, utf8NoBom);
        Console.WriteLine("[OK] Przywrocono cykl zycia paska bocznego w BuilderCoreSidebarAndProjectEngine.cs.");
    }

    private static void RestorePackageSimplifierLifecycle(string servicesDir, UTF8Encoding utf8NoBom)
    {
        string simpPath = Path.Combine(servicesDir, "BuilderUiSimplifier.cs");
        if (File.Exists(simpPath))
        {
            string simp = File.ReadAllText(simpPath, Encoding.UTF8);

            simp = simp.Replace(
                "// [ETAP2_CLEAN] ContentRendered pass removed to prevent multi-frame startup bounce",
                "win.ContentRendered += (_, __) => EnforceSimplification(win);");

            simp = simp.Replace(
                "// [ETAP2_CLEAN] Background pass removed to prevent multi-frame startup bounce",
                "_ = win.Dispatcher.BeginInvoke(new Action(() => EnforceSimplification(win)), DispatcherPriority.Background);");

            File.WriteAllText(simpPath, simp, utf8NoBom);
            Console.WriteLine("[OK] Przywrocono pelne wywolywanie EnforceSimplification w BuilderUiSimplifier.cs (karta 3 przyciskow .ftak i ukrycie sekcji 3).");
        }

        // Dodatkowo w TacticalConfigPanel.cs wywolaj BuilderUiSimplifier.EnforceSimplification(win) zaraz po wstawieniu _factionsCardElement,
        // aby karta 3 przyciskow "Generator paczek .ftak" pojawiala sie zawsze ze 100% pewnoscia
        string tcpPath = Path.Combine(servicesDir, "TacticalConfigPanel.cs");
        if (File.Exists(tcpPath))
        {
            string tcp = File.ReadAllText(tcpPath, Encoding.UTF8);
            if (!tcp.Contains("BuilderUiSimplifier.EnforceSimplification(win);"))
            {
                tcp = tcp.Replace(
                    "TacticalMapGeneratorView.AttachFullScreenTab(win);",
                    "TacticalMapGeneratorView.AttachFullScreenTab(win);\r\n                    BuilderUiSimplifier.EnforceSimplification(win);");
                File.WriteAllText(tcpPath, tcp, utf8NoBom);
            }
        }
    }

    private static void RestoreOpordTransformerAndAnnex3(string servicesDir, UTF8Encoding utf8NoBom)
    {
        string uiPartPath = Path.Combine(servicesDir, "BuilderLocalizationAndOpordEngine.UiPart.cs");
        if (!File.Exists(uiPartPath))
            throw new Exception("Nie znaleziono pliku BuilderLocalizationAndOpordEngine.UiPart.cs!");

        string uiPart = File.ReadAllText(uiPartPath, Encoding.UTF8);

        // 1. Przywroc wywolanie Dispatcher.BeginInvoke w RebuildOpordForm(), ktore wykonuje sie zaraz po dodaniu kontrolek do mainStack
        uiPart = uiPart.Replace(
            "// [ETAP2_SYNC_PIPELINE] ApplyToOpordForm runs synchronously at the end of RebuildOpordForm",
            "win?.Dispatcher.BeginInvoke(new Action(() => V34CleanOpordUiTransformer.ApplyToOpordForm(win, mainStack, _activeOpordFaction, RebuildOpordForm)), System.Windows.Threading.DispatcherPriority.Loaded);");

        // Usun ewentualny duplikat z konca RebuildOpordForm, jesli zostal dodany
        uiPart = uiPart.Replace(
            "// [ETAP2_END_OF_REBUILD_HOOK]\r\n                V34CleanOpordUiTransformer.ApplyToOpordForm(win, mainStack, _activeOpordFaction, RebuildOpordForm);",
            "");

        // 2. Upewnij sie, ze w ApplyToOpordForm wywolanie V34CleanAnnex3AndDiscordEngine.AttachToOpordForm jest bezwarunkowe i zawsze dodaje karte na dole formularza
        uiPart = Regex.Replace(
            uiPart,
            @"if\s*\(\s*model\s*!=\s*null\s*&&\s*activeDay\s*!=\s*null\s*\)\s*\{\s*V34CleanAnnex3AndDiscordEngine\.AttachToOpordForm\([^;]+;\s*\}",
            "V34CleanAnnex3AndDiscordEngine.AttachToOpordForm(w, mainOpordStack, model, activeDay, activeFactionName);");

        int openUi = uiPart.Count(c => c == '{');
        int closeUi = uiPart.Count(c => c == '}');
        Console.WriteLine("[WERYFIKACJA] Nawiasy klamrowe w BuilderLocalizationAndOpordEngine.UiPart.cs: { = " + openUi + ", } = " + closeUi);
        if (openUi != closeUi)
            throw new Exception("Niezgodnosc nawiasow klamrowych w BuilderLocalizationAndOpordEngine.UiPart.cs!");

        File.WriteAllText(uiPartPath, uiPart, utf8NoBom);
        Console.WriteLine("[OK] Przywrocono transformacje UI OPORD (przycisk Pobierz na gorze, nowa Tabela Lacznosci, METOC AO i Zalacznik nr 3) w BuilderLocalizationAndOpordEngine.UiPart.cs.");

        // 3. Przywroc w BuilderLocalizationAndOpordEngine.cs wywolanie w OnSwitchedToOpordTab na priorytecie Loaded
        string opEnginePath = Path.Combine(servicesDir, "BuilderLocalizationAndOpordEngine.cs");
        if (File.Exists(opEnginePath))
        {
            string opEng = File.ReadAllText(opEnginePath, Encoding.UTF8);

            int swStart = opEng.IndexOf("public static void OnSwitchedToOpordTab(Window win, string modeKey)", StringComparison.Ordinal);
            int gmStart = opEng.IndexOf("internal static OpordParagraphModel GetLiveOpordModel()", StringComparison.Ordinal);
            if (swStart > 0 && gmStart > swStart)
            {
                string restoredOnSwitch = @"public static void OnSwitchedToOpordTab(Window win, string modeKey)
        {
            if (string.IsNullOrWhiteSpace(modeKey) || !modeKey.Contains(""OPORD"", StringComparison.OrdinalIgnoreCase))
                return;

            var w = win ?? Application.Current?.MainWindow;
            if (w == null || _isRefreshingOpord) return;

            w.Dispatcher.BeginInvoke(new Action(() =>
            {
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
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        ";
                opEng = opEng.Substring(0, swStart) + restoredOnSwitch + opEng.Substring(gmStart);
                File.WriteAllText(opEnginePath, opEng, utf8NoBom);
                Console.WriteLine("[OK] Przywrocono OnSwitchedToOpordTab w BuilderLocalizationAndOpordEngine.cs.");
            }
        }
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
        RunProcess(gitExe, "commit -m \"Revert Stage 2 regressions, prevent double shell wrap in AttachFullScreenTab, and restore OPORD & Package Generator UI\" --allow-empty", targetDir, ignoreError: false);

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
//===REVERT_STAGE2_AND_FIX_CS===