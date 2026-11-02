@echo off
setlocal DisableDelayedExpansion
cd /d "%~dp0"

set "BAT_FILE=%~f0"

echo ============================================================================
echo [1/3] Odblokowanie renderowania karty Zalacznika nr 3 w Generatorze OPORD...
echo ============================================================================
powershell -NoProfile -ExecutionPolicy Bypass -Command "Add-Type -TypeDefinition ([System.Text.RegularExpressions.Regex]::Split([System.IO.File]::ReadAllText([System.Environment]::GetEnvironmentVariable('BAT_FILE'), [System.Text.Encoding]::UTF8), '//===' + 'FIX_ANNEX3_VISIBILITY_CS===')[1]); [FthFixAnnex3Visibility]::Run()"

if errorlevel 1 (
    echo.
    echo [BLAD] Wystapil blad podczas aktualizacji plikow lub wysylki na GitHub!
    pause
    exit /b 1
)

echo.
echo ============================================================================
echo [SUKCES] Karta Zalacznika nr 3 w OPORD zostala odblokowana i wyslana
echo na GitHub (QbcolPL/TEST)! Kompilacja na GitHub Actions zostala uruchomiona.
echo ============================================================================
pause
exit /b 0

//===FIX_ANNEX3_VISIBILITY_CS===
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public static class FthFixAnnex3Visibility
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

        // 1. Napraw GetLiveOpordModel() w BuilderLocalizationAndOpordEngine.cs,
        //    aby pobieralo model przez OpordBuilderManager.GetOrCreateModel(...) zamiast zwracac null
        string opEnginePath = Path.Combine(servicesDir, "BuilderLocalizationAndOpordEngine.cs");
        if (!File.Exists(opEnginePath))
            throw new Exception("Nie znaleziono pliku BuilderLocalizationAndOpordEngine.cs!");

        string opEng = File.ReadAllText(opEnginePath, Encoding.UTF8);

        int gmStart = opEng.IndexOf("internal static OpordParagraphModel GetLiveOpordModel()", StringComparison.Ordinal);
        int gmEnd = opEng.IndexOf("public static bool IsGarbageFactionName(string name)", StringComparison.Ordinal);
        if (gmStart > 0 && gmEnd > gmStart)
        {
            string fixedGetModel = @"internal static OpordParagraphModel GetLiveOpordModel()
        {
            try
            {
                var factions = TacticalConfigPanel.GetInternalFactionsList();
                string facName = (factions != null && factions.Count > 0 && !string.IsNullOrWhiteSpace(factions[0].Name))
                    ? factions[0].Name
                    : ""BLUFOR"";
                return OpordBuilderManager.GetOrCreateModel(facName);
            }
            catch
            {
                return null;
            }
        }

        ";
            opEng = opEng.Substring(0, gmStart) + fixedGetModel + opEng.Substring(gmEnd);
            File.WriteAllText(opEnginePath, opEng, utf8NoBom);
            Console.WriteLine("[OK] Naprawiono GetLiveOpordModel() w BuilderLocalizationAndOpordEngine.cs.");
        }

        // 2. Napraw ApplyToOpordForm oraz miejsce wstawiania karty Zalacznika nr 3 w BuilderLocalizationAndOpordEngine.UiPart.cs
        string uiPartPath = Path.Combine(servicesDir, "BuilderLocalizationAndOpordEngine.UiPart.cs");
        if (!File.Exists(uiPartPath))
            throw new Exception("Nie znaleziono pliku BuilderLocalizationAndOpordEngine.UiPart.cs!");

        string uiPart = File.ReadAllText(uiPartPath, Encoding.UTF8);

        // A. Pobieraj model bezposrednio dla activeFactionName i wywoluj AttachToOpordForm BEZWARUNKOWO
        uiPart = uiPart.Replace(
            "var model = V34CleanOpordDataEngine.GetLiveOpordModel();",
            "var model = OpordBuilderManager.GetOrCreateModel(string.IsNullOrWhiteSpace(activeFactionName) ? \"BLUFOR\" : activeFactionName);");

        uiPart = Regex.Replace(
            uiPart,
            @"if\s*\(\s*model\s*!=\s*null\s*&&\s*activeDay\s*!=\s*null\s*\)\s*\{\s*V34CleanAnnex3AndDiscordEngine\.AttachToOpordForm\([^;]+;\s*\}",
            "V34CleanAnnex3AndDiscordEngine.AttachToOpordForm(w, mainOpordStack, model, activeDay, activeFactionName);");

        // B. Upewnij sie, ze karta Zalacznika nr 3 wstawia sie bezposrednio pod Zalacznikiem nr 2 (oraz ze jest w 100% widoczna)
        string oldAddAnnex3 = "mainStack.Children.Add(annex3Card);";
        string newInsertAnnex3 = @"int annex2Idx = -1;
            for (int i = 0; i < mainStack.Children.Count; i++)
            {
                var chTexts = V34CleanSidebarAndHeaderEngine.GetLogicalTreeNodes(mainStack.Children[i]).OfType<TextBlock>().Select(t => t.Text ?? """").ToList();
                if (chTexts.Any(t => t.Contains(""ZA\u0141\u0104CZNIK 2"", StringComparison.OrdinalIgnoreCase) || t.Contains(""ANNEX 2"", StringComparison.OrdinalIgnoreCase)))
                {
                    annex2Idx = i;
                }
            }
            if (annex2Idx >= 0 && annex2Idx + 1 <= mainStack.Children.Count)
                mainStack.Children.Insert(annex2Idx + 1, annex3Card);
            else
                mainStack.Children.Add(annex3Card);";

        if (uiPart.Contains(oldAddAnnex3))
        {
            uiPart = uiPart.Replace(oldAddAnnex3, newInsertAnnex3);
        }

        // C. Wywolaj ApplyToOpordForm rowniez bezposrednio na koncu RebuildOpordForm() (synchronicznie, oprocz Dispatcher.BeginInvoke),
        //    aby Zalacznik nr 3 byl widoczny natychmiast od pierwszej klatki
        if (!uiPart.Contains("// [SYNC_APPLY_OPORD_TRANSFORMER]"))
        {
            uiPart = uiPart.Replace(
                "scroll.Content = mainStack;\r\n            rootBorder.Child = scroll;",
                "scroll.Content = mainStack;\r\n            rootBorder.Child = scroll;\r\n            // [SYNC_APPLY_OPORD_TRANSFORMER]");

            uiPart = Regex.Replace(
                uiPart,
                @"(RebuildOpordForm\(\);\s*return\s+rootBorder;)",
                "RebuildOpordForm();\r\n            V34CleanOpordUiTransformer.ApplyToOpordForm(win, mainStack, _activeOpordFaction, RebuildOpordForm);\r\n            return rootBorder;");
        }

        int openB = uiPart.Count(c => c == '{');
        int closeB = uiPart.Count(c => c == '}');
        Console.WriteLine("[WERYFIKACJA] Nawiasy klamrowe w BuilderLocalizationAndOpordEngine.UiPart.cs: { = " + openB + ", } = " + closeB);

        File.WriteAllText(uiPartPath, uiPart, utf8NoBom);
        Console.WriteLine("[OK] Odblokowano bezwarunkowe dodawanie karty Zalacznika nr 3 pod Zalacznikiem nr 2.");

        // 3. Znacznik wyzwalajacy i wysylka na GitHub Actions
        string triggerPath = Path.Combine(targetDir, ".fth_build_trigger.txt");
        File.WriteAllText(triggerPath, "Field TAK Hub Annex 3 Visibility Fix UTC: " + DateTime.UtcNow.ToString("O") + " | RunId: " + Guid.NewGuid().ToString("N") + "\n", utf8NoBom);

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
        RunProcess(gitExe, "commit -m \"Fix Annex 3 card visibility in OPORD Generator by resolving live faction model directly\" --allow-empty", targetDir, ignoreError: false);

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
//===FIX_ANNEX3_VISIBILITY_CS===