@echo off
setlocal DisableDelayedExpansion
cd /d "%~dp0"

set "BAT_FILE=%~f0"

echo ============================================================================
echo [1/3] Ujednolicanie wygladu przyciskow "-" w Tabeli Lacznosci OPORD z UI...
echo ============================================================================
powershell -NoProfile -ExecutionPolicy Bypass -Command "Add-Type -TypeDefinition ([System.Text.RegularExpressions.Regex]::Split([System.IO.File]::ReadAllText([System.Environment]::GetEnvironmentVariable('BAT_FILE'), [System.Text.Encoding]::UTF8), '//===' + 'FIX_COMMS_MINUS_CS===')[1]); [FthFixCommsMinusButtons]::Run()"

if errorlevel 1 (
    echo.
    echo [BLAD] Wystapil blad podczas aktualizacji pliku lub wysylki na GitHub!
    pause
    exit /b 1
)

echo.
echo ============================================================================
echo [SUKCES] Przyciski "-" w Tabeli Lacznosci OPORD zostaly ujednolicone z UI
echo i wyslane na GitHub (QbcolPL/TEST)! Kompilacja w GitHub Actions uruchomiona.
echo ============================================================================
pause
exit /b 0

//===FIX_COMMS_MINUS_CS===
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public static class FthFixCommsMinusButtons
{
    private const string RepoRemoteUrl = "https://github.com/QbcolPL/TEST.git";
    private const string RepoActionsUrl = "https://github.com/QbcolPL/TEST/actions";

    public static void Run()
    {
        string currentDir = Directory.GetCurrentDirectory();
        string targetDir = ResolveTargetProjectDirectory(currentDir);
        Console.WriteLine("[OK] Katalog roboczy projektu: " + targetDir);

        UTF8Encoding utf8NoBom = new UTF8Encoding(false);
        string uiPartPath = Path.Combine(targetDir, @"apps\builder\FieldTakHub.Builder\Services\BuilderLocalizationAndOpordEngine.UiPart.cs");
        if (!File.Exists(uiPartPath))
            throw new Exception("Nie znaleziono pliku BuilderLocalizationAndOpordEngine.UiPart.cs!");

        string uiPart = File.ReadAllText(uiPartPath, Encoding.UTF8);

        // 1. Podmien przycisk usuwania w Tabeli Lacznosci (RebuildCommunicationsTableSection)
        //    na zaokraglony Border z biala obwodka i wycentrowanym znakiem "−" (identyczny jak w Generatorze Stron i Generatorze Map)
        string oldBtnDelBlock = @"                    if (isManual)
                    {
                        var btnDel = new Button
                        {
                            Content = ""\u2212"",
                            Width = 26,
                            Height = 26,
                            Background = TacticalConfigPanel.BrushRedMinus,
                            Foreground = Brushes.White,
                            BorderBrush = Brushes.White,
                            BorderThickness = new Thickness(1),
                            FontWeight = FontWeights.Bold,
                            Cursor = Cursors.Hand,
                            ToolTip = ""Usu\u0144 ten kana\u0142""
                        };
                        btnDel.Click += (_, __) =>
                        {
                            if (channels.Count > 1)
                            {
                                channels.Remove(chItem);
                                SyncChannelsIntoDayModel(activeFactionName, activeDay);
                                rebuildCallback?.Invoke();
                            }
                        };
                        Grid.SetColumn(btnDel, 5);
                        rowGrid.Children.Add(btnDel);
                    }";

        string newBtnDelBlock = @"                    if (isManual)
                    {
                        var btnDel = new Border
                        {
                            Width = 26,
                            Height = 24,
                            CornerRadius = new CornerRadius(5),
                            Background = TacticalConfigPanel.BrushRedMinus,
                            BorderBrush = Brushes.White,
                            BorderThickness = new Thickness(1),
                            Cursor = Cursors.Hand,
                            VerticalAlignment = VerticalAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            ToolTip = ""Usu\u0144 ten kana\u0142"",
                            Child = new TextBlock
                            {
                                Text = ""\u2212"",
                                Foreground = Brushes.White,
                                FontSize = 15,
                                FontWeight = FontWeights.Bold,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                VerticalAlignment = VerticalAlignment.Center,
                                Margin = new Thickness(0, -2, 0, 0)
                            }
                        };
                        btnDel.MouseLeftButtonDown += (_, e) =>
                        {
                            e.Handled = true;
                            if (channels.Count > 1)
                            {
                                channels.Remove(chItem);
                                SyncChannelsIntoDayModel(activeFactionName, activeDay);
                                rebuildCallback?.Invoke();
                            }
                        };
                        Grid.SetColumn(btnDel, 5);
                        rowGrid.Children.Add(btnDel);
                    }";

        if (uiPart.Contains("var btnDel = new Button"))
        {
            uiPart = uiPart.Replace(oldBtnDelBlock, newBtnDelBlock);
            // Fallback regex gdyby konce linii roznily sie (CRLF vs LF)
            uiPart = Regex.Replace(
                uiPart,
                @"var\s+btnDel\s*=\s*new\s+Button\s*\{[\s\S]*?Grid\.SetColumn\(btnDel,\s*5\);\s*rowGrid\.Children\.Add\(btnDel\);",
                newBtnDelBlock.Trim().Substring("if (isManual)\r\n                    {\r\n                        ".Length).TrimEnd('}').Trim());
        }

        // 2. Dodatkowo upewnij sie, ze wszystkie inne male przyciski usuwania wierszy w OPORD (np. Sklad oddzialu, Zalacznik 1)
        //    maja wyzerowany Padding i wycentrowany znak "−"
        string normalizeSmallMinusLoop = @"
                foreach (var smBtn in V34CleanSidebarAndHeaderEngine.GetLogicalTreeNodes(mainOpordStack).OfType<Button>())
                {
                    string cStr = (smBtn.Content?.ToString() ?? """").Trim();
                    if (cStr == ""-"" || cStr == ""\u2212"" || cStr == ""\u2715"" || cStr == ""\u2716"" || (smBtn.Width >= 20 && smBtn.Width <= 38))
                    {
                        smBtn.Padding = new Thickness(0);
                        smBtn.HorizontalContentAlignment = HorizontalAlignment.Center;
                        smBtn.VerticalContentAlignment = VerticalAlignment.Center;
                        if (cStr == ""-"") smBtn.Content = ""\u2212"";
                    }
                }";

        if (!uiPart.Contains("foreach (var smBtn in V34CleanSidebarAndHeaderEngine.GetLogicalTreeNodes(mainOpordStack).OfType<Button>())"))
        {
            uiPart = uiPart.Replace(
                "RebuildCommunicationsTableSection(w, mainOpordStack, activeFactionName, activeDay, rebuildCallback);",
                "RebuildCommunicationsTableSection(w, mainOpordStack, activeFactionName, activeDay, rebuildCallback);" + normalizeSmallMinusLoop);
        }

        int openUi = uiPart.Count(c => c == '{');
        int closeUi = uiPart.Count(c => c == '}');
        Console.WriteLine("[WERYFIKACJA] Nawiasy klamrowe w BuilderLocalizationAndOpordEngine.UiPart.cs: { = " + openUi + ", } = " + closeUi);

        File.WriteAllText(uiPartPath, uiPart, utf8NoBom);
        Console.WriteLine("[OK] Zaktualizowano wyglad przyciskow '−' w BuilderLocalizationAndOpordEngine.UiPart.cs.");

        string triggerPath = Path.Combine(targetDir, ".fth_build_trigger.txt");
        File.WriteAllText(triggerPath, "Field TAK Hub OPORD Minus Button UI Fix UTC: " + DateTime.UtcNow.ToString("O") + "\n", utf8NoBom);

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
        RunProcess(gitExe, "commit -m \"Fix minus delete buttons in OPORD Communications Table to match tactical UI style\" --allow-empty", targetDir, ignoreError: false);

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
//===FIX_COMMS_MINUS_CS===