@echo off
setlocal DisableDelayedExpansion
cd /d "%~dp0"

set "BAT_FILE=%~f0"

echo ============================================================================
echo [1/3] Dopasowywanie przyciskow "-" w Tabeli Lacznosci do reszty UI oraz
echo       dodawanie opcjonalnego na "ptaszek" Zalacznika nr 3 (Mapa do OPORD)...
echo ============================================================================
powershell -NoProfile -ExecutionPolicy Bypass -Command "Add-Type -TypeDefinition ([System.Text.RegularExpressions.Regex]::Split([System.IO.File]::ReadAllText([System.Environment]::GetEnvironmentVariable('BAT_FILE'), [System.Text.Encoding]::UTF8), '//===' + 'OPORD_MINUS_AND_ANNEX3_CS===')[1]); [FthOpordMinusAndAnnex3Installer]::Run()"

if errorlevel 1 (
    echo.
    echo [BLAD] Wystapil blad podczas aktualizacji plikow lub wysylki na GitHub!
    pause
    exit /b 1
)

echo.
echo ============================================================================
echo [SUKCES] Przyciski "-" oraz Zalacznik nr 3 z mapa w OPORD zostaly wdrozone
echo i wyslane na GitHub (QbcolPL/TEST)! Kompilacja w GitHub Actions uruchomiona.
echo ============================================================================
pause
exit /b 0

//===OPORD_MINUS_AND_ANNEX3_CS===
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public static class FthOpordMinusAndAnnex3Installer
{
    private const string RepoRemoteUrl = "https://github.com/QbcolPL/TEST.git";
    private const string RepoActionsUrl = "https://github.com/QbcolPL/TEST/actions";

    public static void Run()
    {
        string currentDir = Directory.GetCurrentDirectory();
        string targetDir = ResolveTargetProjectDirectory(currentDir);
        Console.WriteLine("[OK] Katalog roboczy projektu: " + targetDir);

        string batFile = Environment.GetEnvironmentVariable("BAT_FILE");
        string rawBat = File.ReadAllText(batFile, Encoding.UTF8);

        string[] p1 = Regex.Split(rawBat, "//===" + "PAYLOAD_COMMS_ROW_DELETE_CS===");
        if (p1.Length < 2) throw new Exception("Brak bloku PAYLOAD_COMMS_ROW_DELETE_CS!");
        string[] p2 = Regex.Split(p1[1], "//===" + "PAYLOAD_ANNEX3_ENGINE_CS===");
        if (p2.Length < 2) throw new Exception("Brak bloku PAYLOAD_ANNEX3_ENGINE_CS!");
        string[] p3 = Regex.Split(p2[1], "//===" + "END_ANNEX3_PAYLOADS===");

        string payloadCommsBlock = p2[0].Trim();
        string payloadAnnex3Engine = p3[0].Trim();

        UTF8Encoding utf8NoBom = new UTF8Encoding(false);
        string servicesDir = Path.Combine(targetDir, @"apps\builder\FieldTakHub.Builder\Services");

        // 1. Dodaj w TacticalMapGeneratorView.cs metode CaptureCurrentMapSheetForOpordAnnex3Png(Window win)
        PatchMapGeneratorCaptureHelper(servicesDir, utf8NoBom);

        // 2. Zaktualizuj w BuilderLocalizationAndOpordEngine.UiPart.cs:
        //    - Wyglad przyciskow "-" i "+" w Tabeli Lacznosci (identyczne jak w Sekcji D Tabela Skladu Oddzialu)
        //    - Pelny opcjonalny na "ptaszek" Zalacznik nr 3 z dodaniem mapy do OPORDu
        PatchOpordUiPartFile(servicesDir, payloadCommsBlock, payloadAnnex3Engine, utf8NoBom);

        // 3. Zaktualizuj w BuilderLocalizationAndOpordEngine.cs generowanie HTML i Markdown,
        //    aby wlaczony Zalacznik nr 3 z mapa dolaczal sie automatycznie do drukowanego/eksportowanego OPORDu
        PatchOpordHtmlAndMarkdownGenerator(servicesDir, utf8NoBom);

        // 4. Zapisz znacznik wyzwalajacy dla GitHub Actions i wyslij zmiany
        string triggerPath = Path.Combine(targetDir, ".fth_build_trigger.txt");
        File.WriteAllText(triggerPath, "Field TAK Hub OPORD Minus UI & Annex 3 Map Trigger UTC: " + DateTime.UtcNow.ToString("O") + " | RunId: " + Guid.NewGuid().ToString("N") + "\n", utf8NoBom);

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

    private static void PatchMapGeneratorCaptureHelper(string servicesDir, UTF8Encoding utf8NoBom)
    {
        string tmgPath = Path.Combine(servicesDir, "TacticalMapGeneratorView.cs");
        if (!File.Exists(tmgPath))
            throw new Exception("Nie znaleziono pliku TacticalMapGeneratorView.cs!");

        string tmg = File.ReadAllText(tmgPath, Encoding.UTF8);

        if (!tmg.Contains("public static byte[] CaptureCurrentMapSheetForOpordAnnex3Png"))
        {
            string helperCode = @"        public static byte[] CaptureCurrentMapSheetForOpordAnnex3Png(Window win = null)
        {
            try
            {
                var w = win ?? _hostWindow ?? Application.Current?.MainWindow;
                if (_mapCompositeGrid == null && w != null)
                {
                    AttachFullScreenTab(w);
                }
                if (_mapCompositeGrid != null)
                {
                    var (cw, ch) = GetExactCanvasSize();
                    if (_mapCompositeGrid.ActualWidth < 100 || _mapCompositeGrid.ActualHeight < 100)
                    {
                        _mapCompositeGrid.Measure(new Size(cw, ch));
                        _mapCompositeGrid.Arrange(new Rect(0, 0, cw, ch));
                    }
                    return CaptureMapCanvasPngBytes();
                }
            }
            catch { }
            return null;
        }

        ";
            int capIdx = tmg.IndexOf("private static byte[] CaptureMapCanvasPngBytes()", StringComparison.Ordinal);
            if (capIdx > 0)
            {
                tmg = tmg.Substring(0, capIdx) + helperCode + tmg.Substring(capIdx);
                File.WriteAllText(tmgPath, tmg, utf8NoBom);
            }
        }
        Console.WriteLine("[OK] Dodano CaptureCurrentMapSheetForOpordAnnex3Png w TacticalMapGeneratorView.cs.");
    }

    private static void PatchOpordUiPartFile(string servicesDir, string payloadCommsBlock, string payloadAnnex3Engine, UTF8Encoding utf8NoBom)
    {
        string uiPartPath = Path.Combine(servicesDir, "BuilderLocalizationAndOpordEngine.UiPart.cs");
        if (!File.Exists(uiPartPath))
            throw new Exception("Nie znaleziono pliku BuilderLocalizationAndOpordEngine.UiPart.cs!");

        string uiPart = File.ReadAllText(uiPartPath, Encoding.UTF8);

        // 1. Podmien metode RebuildCommunicationsTableSection na wersje z przyciskami "-" i "+" identycznymi jak w Sekcji D
        int commsStart = uiPart.IndexOf("private static void RebuildCommunicationsTableSection(", StringComparison.Ordinal);
        int commsEnd = uiPart.IndexOf("private static bool IsDescendantOf(DependencyObject ancestor, DependencyObject descendant)", StringComparison.Ordinal);
        if (commsStart > 0 && commsEnd > commsStart)
        {
            uiPart = uiPart.Substring(0, commsStart) + payloadCommsBlock + "\r\n\r\n        " + uiPart.Substring(commsEnd);
        }

        // Przekaz activeFactionName do AttachToOpordForm
        uiPart = uiPart.Replace(
            "V34CleanAnnex3AndDiscordEngine.AttachToOpordForm(w, mainOpordStack, model, activeDay);",
            "V34CleanAnnex3AndDiscordEngine.AttachToOpordForm(w, mainOpordStack, model, activeDay, activeFactionName);");

        // 2. Podmien blok V34CleanAnnex3AndDiscordEngine na kompletny modul Zalacznika nr 3 na "ptaszek"
        int a3Start = uiPart.IndexOf("// [START_V34_CLEAN_ANNEX3_DISCORD]", StringComparison.Ordinal);
        int a3End = uiPart.IndexOf("// [END_V34_CLEAN_ANNEX3_DISCORD]", StringComparison.Ordinal);
        if (a3Start > 0 && a3End > a3Start)
        {
            int afterA3End = a3End + "// [END_V34_CLEAN_ANNEX3_DISCORD]".Length;
            uiPart = uiPart.Substring(0, a3Start) + payloadAnnex3Engine + uiPart.Substring(afterA3End);
        }

        int openB = uiPart.Count(c => c == '{');
        int closeB = uiPart.Count(c => c == '}');
        Console.WriteLine("[WERYFIKACJA] Nawiasy klamrowe w BuilderLocalizationAndOpordEngine.UiPart.cs: { = " + openB + ", } = " + closeB);

        File.WriteAllText(uiPartPath, uiPart, utf8NoBom);
        Console.WriteLine("[OK] Zaktualizowano przyciski '-' w Tabeli Lacznosci oraz Zalacznik nr 3 w BuilderLocalizationAndOpordEngine.UiPart.cs.");
    }

    private static void PatchOpordHtmlAndMarkdownGenerator(string servicesDir, UTF8Encoding utf8NoBom)
    {
        string opEnginePath = Path.Combine(servicesDir, "BuilderLocalizationAndOpordEngine.cs");
        if (!File.Exists(opEnginePath)) return;

        string opEng = File.ReadAllText(opEnginePath, Encoding.UTF8);

        // Dolacz Zalacznik nr 3 (jesli zaznaczony "ptaszek") do generowanego dokumentu HTML / Druku A4 OPORD
        if (!opEng.Contains("V34CleanAnnex3AndDiscordEngine.BuildAnnex3HtmlSection"))
        {
            opEng = opEng.Replace(
                @"html.AppendLine(""<div class='sub-p'>"" + E(d.Annex2TasksAnalysis) + ""</div>"");",
                @"html.AppendLine(""<div class='sub-p'>"" + E(d.Annex2TasksAnalysis) + ""</div>"");
                string annex3Html = V34CleanAnnex3AndDiscordEngine.BuildAnnex3HtmlSection(win, factionName, d.DayTitle);
                if (!string.IsNullOrWhiteSpace(annex3Html))
                {
                    html.AppendLine(annex3Html);
                }");
        }

        // Dolacz Zalacznik nr 3 do generowanego Markdownu Discord
        if (!opEng.Contains("V34CleanAnnex3AndDiscordEngine.BuildAnnex3DiscordMarkdown"))
        {
            opEng = opEng.Replace(
                "sb.AppendLine(d.Annex2TasksAnalysis);",
                "sb.AppendLine(d.Annex2TasksAnalysis);\r\n" +
                "                string annex3Md = V34CleanAnnex3AndDiscordEngine.BuildAnnex3DiscordMarkdown(factionName, d.DayTitle);\r\n" +
                "                if (!string.IsNullOrWhiteSpace(annex3Md))\r\n" +
                "                {\r\n" +
                "                    sb.AppendLine();\r\n" +
                "                    sb.AppendLine(annex3Md);\r\n" +
                "                }");
        }

        File.WriteAllText(opEnginePath, opEng, utf8NoBom);
        Console.WriteLine("[OK] Zaktualizowano eksport Zalacznika nr 3 w BuilderLocalizationAndOpordEngine.cs.");
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
        RunProcess(gitExe, "commit -m \"Match minus buttons in OPORD Comms Table to Section D style and add optional checkbox Annex 3 (Tactical Map attachment)\" --allow-empty", targetDir, ignoreError: false);

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
//===OPORD_MINUS_AND_ANNEX3_CS===

//===PAYLOAD_COMMS_ROW_DELETE_CS===
        private static Border CreateConsistentOpordSquareActionBtn(string symbol, bool isPlus, string toolTipText, Brush refMinusBg, Brush refMinusBorder, Action onClick)
        {
            Brush bgBrush = isPlus
                ? new SolidColorBrush(Color.FromRgb(16, 52, 34))
                : (refMinusBg ?? new SolidColorBrush(Color.FromRgb(55, 18, 22)));
            Brush borderBrush = isPlus
                ? new SolidColorBrush(Color.FromRgb(46, 204, 113))
                : (refMinusBorder ?? new SolidColorBrush(Color.FromRgb(235, 64, 52)));

            var box = new Border
            {
                Width = 28,
                Height = 26,
                CornerRadius = new CornerRadius(4),
                Background = bgBrush,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1.3),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                ToolTip = toolTipText,
                Child = new TextBlock
                {
                    Text = symbol,
                    Foreground = Brushes.White,
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, -1, 0, 0)
                }
            };
            box.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                onClick?.Invoke();
            };
            return box;
        }

        private static void RebuildCommunicationsTableSection(Window win, StackPanel mainOpordStack, string activeFactionName, OpordDayPlanModel activeDay, Action rebuildCallback)
        {
            SyncChannelsIntoDayModel(activeFactionName, activeDay);

            // Pobierz dokladny pedzel tla i ramki przycisku "-" z Sekcji D (Tabela Skladu Oddzialu), aby zachowac 100% spojnosci UI
            Brush sectionDMinusBg = new SolidColorBrush(Color.FromRgb(55, 18, 22));
            Brush sectionDMinusBorder = new SolidColorBrush(Color.FromRgb(235, 64, 52));
            foreach (var existingBtn in V34CleanSidebarAndHeaderEngine.GetLogicalTreeNodes(mainOpordStack).OfType<Button>())
            {
                string cTxt = (existingBtn.Content?.ToString() ?? "").Trim();
                if ((cTxt == "-" || cTxt == "\u2212") && existingBtn.Background != null && existingBtn.BorderBrush != null)
                {
                    sectionDMinusBg = existingBtn.Background;
                    sectionDMinusBorder = existingBtn.BorderBrush;
                    break;
                }
            }

            foreach (var border in mainOpordStack.Children.OfType<Border>())
            {
                var nodes = V34CleanSidebarAndHeaderEngine.GetLogicalTreeNodes(border);
                var texts = nodes.OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
                bool isExecSection = texts.Any(t => t.Contains("III. WYKONANIE", StringComparison.OrdinalIgnoreCase) || t.Contains("III. EXECUTION", StringComparison.OrdinalIgnoreCase) || t.Contains("Tabela \u0142\u0105czno\u015Bci", StringComparison.OrdinalIgnoreCase));
                if (!isExecSection) continue;

                var secStack = border.Child as StackPanel;
                if (secStack == null) continue;

                int insertIndex = -1;
                for (int i = 0; i < secStack.Children.Count; i++)
                {
                    var child = secStack.Children[i];
                    var childTexts = V34CleanSidebarAndHeaderEngine.GetLogicalTreeNodes(child).OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
                    if (childTexts.Any(t => t.Contains("E. Tabela \u0142\u0105czno\u015Bci", StringComparison.OrdinalIgnoreCase) || t.Contains("E. Communications", StringComparison.OrdinalIgnoreCase) || t.Contains("Kana\u0142 g\u0142\u00F3wny", StringComparison.OrdinalIgnoreCase)))
                    {
                        if (insertIndex < 0) insertIndex = i;
                        if (child is FrameworkElement fe && fe.Tag?.ToString() != "REMODELED_OPORD_COMMS_CARD")
                        {
                            fe.Visibility = Visibility.Collapsed;
                            fe.Height = 0;
                            fe.Margin = new Thickness(0);
                        }
                    }
                }

                for (int i = secStack.Children.Count - 1; i >= 0; i--)
                {
                    if ((secStack.Children[i] as FrameworkElement)?.Tag?.ToString() == "REMODELED_OPORD_COMMS_CARD")
                        secStack.Children.RemoveAt(i);
                }

                var liveFactions = TacticalConfigPanel.GetInternalFactionsList();
                var facModel = liveFactions.FirstOrDefault(f => string.Equals(f.Name, activeFactionName, StringComparison.OrdinalIgnoreCase)) ?? liveFactions.FirstOrDefault();
                string meshCh = facModel?.ChannelName ?? (activeFactionName + "-MESH");
                string meshReg = facModel?.LoraRegion ?? "EU_868";
                string meshPreset = facModel?.LoraModemPreset ?? "SHORT_FAST";
                string otsHost = string.IsNullOrWhiteSpace(facModel?.OtsHost) ? "Off-Grid Mesh (Lokalnie)" : facModel.OtsHost;
                string sosPhone = string.IsNullOrWhiteSpace(facModel?.EmergencyPhone) ? "Brak / 112" : facModel.EmergencyPhone;

                bool isManual = V34CleanOpordCommsStore.IsManualOverrideEnabled(activeFactionName);
                var channels = V34CleanOpordCommsStore.GetEffectiveChannelsForFaction(activeFactionName);

                var commsCard = new Border
                {
                    Tag = "REMODELED_OPORD_COMMS_CARD",
                    Background = new SolidColorBrush(Color.FromRgb(10, 24, 30)),
                    BorderBrush = TacticalConfigPanel.BrushAccent,
                    BorderThickness = new Thickness(1.3),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 10, 12, 10),
                    Margin = new Thickness(0, 6, 0, 10)
                };

                var cStack = new StackPanel();

                var hdrDock = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var rightHeaderControls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

                var chkManual = new CheckBox
                {
                    Content = "\u270F\uFE0F Wprowad\u017A / edytuj cz\u0119stotliwo\u015Bci r\u0119cznie",
                    IsChecked = isManual,
                    Foreground = isManual ? new SolidColorBrush(Color.FromRgb(250, 208, 44)) : Brushes.White,
                    FontWeight = FontWeights.Bold,
                    FontSize = 10.5,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, isManual ? 10 : 0, 0),
                    Cursor = Cursors.Hand,
                    ToolTip = "Zaznacz ten ptaszek, je\u015Bli chcesz r\u0119cznie edytowa\u0107 lub doda\u0107 cz\u0119stotliwo\u015Bci w OPORD. Gdy odznaczony, tabela pobiera wszystkie kana\u0142y automatycznie z Generatora Stron."
                };
                chkManual.Checked += (_, __) =>
                {
                    V34CleanOpordCommsStore.SetManualOverrideEnabled(activeFactionName, true);
                    SyncChannelsIntoDayModel(activeFactionName, activeDay);
                    rebuildCallback?.Invoke();
                };
                chkManual.Unchecked += (_, __) =>
                {
                    V34CleanOpordCommsStore.SetManualOverrideEnabled(activeFactionName, false);
                    SyncChannelsIntoDayModel(activeFactionName, activeDay);
                    rebuildCallback?.Invoke();
                };
                rightHeaderControls.Children.Add(chkManual);

                if (isManual)
                {
                    var btnTopPlus = CreateConsistentOpordSquareActionBtn("+", true, "Dodaj nowy wiersz kana\u0142u", sectionDMinusBg, sectionDMinusBorder, () =>
                    {
                        channels.Add(new OpordManualChannelItem
                        {
                            RoleName = $"Kana\u0142 {channels.Count + 1}",
                            BandType = "PMR",
                            FreqMhz = $"CH {Math.Clamp(channels.Count + 1, 1, 16)}",
                            ToneOrCode = "CTCSS 67.0 Hz"
                        });
                        SyncChannelsIntoDayModel(activeFactionName, activeDay);
                        rebuildCallback?.Invoke();
                    });
                    btnTopPlus.Margin = new Thickness(0, 0, 4, 0);

                    var btnTopMinus = CreateConsistentOpordSquareActionBtn("\u2212", false, "Usu\u0144 ostatni wiersz kana\u0142u", sectionDMinusBg, sectionDMinusBorder, () =>
                    {
                        if (channels.Count > 1)
                        {
                            channels.RemoveAt(channels.Count - 1);
                            SyncChannelsIntoDayModel(activeFactionName, activeDay);
                            rebuildCallback?.Invoke();
                        }
                    });

                    rightHeaderControls.Children.Add(btnTopPlus);
                    rightHeaderControls.Children.Add(btnTopMinus);
                }

                DockPanel.SetDock(rightHeaderControls, Dock.Right);
                hdrDock.Children.Add(rightHeaderControls);

                hdrDock.Children.Add(new TextBlock
                {
                    Text = $"\uD83D\uDCFB E. TABELA \u0141\u0104CZNO\u015ACI \u2014 STRONA: {activeFactionName.ToUpperInvariant()} " + (isManual ? "[TRYB EDYCJI R\u0118CZNEJ]" : "[AUTO Z GENERATORA STRON]"),
                    Foreground = TacticalConfigPanel.BrushAccent,
                    FontWeight = FontWeights.Bold,
                    FontSize = 11.5,
                    VerticalAlignment = VerticalAlignment.Center
                });
                cStack.Children.Add(hdrDock);

                var digitalInfoBorder = new Border
                {
                    Background = TacticalConfigPanel.BrushInputBg,
                    BorderBrush = TacticalConfigPanel.BrushTileBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(8, 5, 8, 5),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                digitalInfoBorder.Child = new TextBlock
                {
                    Text = $"\uD83D\uDCE1 LoRa Meshtastic: {meshCh} ({meshReg} / {meshPreset})   |   \uD83D\uDDA5\uFE0F Serwer OTS: {otsHost}   |   \uD83D\uDEA8 Alarmowy SOS / ORGA: {sosPhone}",
                    Foreground = new SolidColorBrush(Color.FromRgb(210, 232, 240)),
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                };
                cStack.Children.Add(digitalInfoBorder);

                var tblHdr = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                tblHdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
                tblHdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
                tblHdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
                tblHdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
                tblHdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
                tblHdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(isManual ? 36 : 0) });

                void AddColTitle(string txt, int col)
                {
                    var t = new TextBlock { Text = txt, Foreground = TacticalConfigPanel.BrushMutedText, FontSize = 9.5, FontWeight = FontWeights.Bold, Margin = new Thickness(4, 0, 4, 0) };
                    Grid.SetColumn(t, col);
                    tblHdr.Children.Add(t);
                }
                AddColTitle("Lp.", 0);
                AddColTitle("Rola / Przeznaczenie kana\u0142u", 1);
                AddColTitle("Pasmo", 2);
                AddColTitle("Cz\u0119stotliwo\u015B\u0107 / Kana\u0142", 3);
                AddColTitle("Ton CTCSS / DCS / Kod DMR", 4);
                cStack.Children.Add(tblHdr);

                for (int r = 0; r < channels.Count; r++)
                {
                    var chItem = channels[r];
                    int rowNum = r + 1;

                    var rowGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(isManual ? 36 : 0) });

                    var lpBadge = new Border
                    {
                        Background = TacticalConfigPanel.BrushInputBg,
                        BorderBrush = TacticalConfigPanel.BrushTileBorder,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Height = 26,
                        Margin = new Thickness(0, 0, 4, 0),
                        Child = new TextBlock { Text = $"#{rowNum}", Foreground = TacticalConfigPanel.BrushAccent, FontSize = 9.5, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                    };
                    Grid.SetColumn(lpBadge, 0);
                    rowGrid.Children.Add(lpBadge);

                    var roleBox = new TextBox
                    {
                        Text = chItem.RoleName,
                        IsReadOnly = !isManual,
                        Height = 26,
                        FontSize = 10,
                        FontWeight = FontWeights.Bold,
                        Padding = new Thickness(6, 0, 6, 0),
                        Margin = new Thickness(0, 0, 4, 0),
                        VerticalContentAlignment = VerticalAlignment.Center,
                        Background = isManual ? TacticalConfigPanel.BrushInputBg : new SolidColorBrush(Color.FromRgb(7, 17, 21)),
                        Foreground = Brushes.White,
                        BorderBrush = isManual ? TacticalConfigPanel.BrushAccent : TacticalConfigPanel.BrushTileBorder
                    };
                    roleBox.TextChanged += (_, __) => { if (isManual) { chItem.RoleName = roleBox.Text; SyncChannelsIntoDayModel(activeFactionName, activeDay); } };
                    Grid.SetColumn(roleBox, 1);
                    rowGrid.Children.Add(roleBox);

                    var bandBox = new TextBox
                    {
                        Text = chItem.BandType,
                        IsReadOnly = !isManual,
                        Height = 26,
                        FontSize = 10,
                        FontWeight = FontWeights.SemiBold,
                        Padding = new Thickness(6, 0, 6, 0),
                        Margin = new Thickness(0, 0, 4, 0),
                        VerticalContentAlignment = VerticalAlignment.Center,
                        Background = isManual ? TacticalConfigPanel.BrushInputBg : new SolidColorBrush(Color.FromRgb(7, 17, 21)),
                        Foreground = TacticalConfigPanel.BrushAccent,
                        BorderBrush = isManual ? TacticalConfigPanel.BrushAccent : TacticalConfigPanel.BrushTileBorder
                    };
                    bandBox.TextChanged += (_, __) => { if (isManual) { chItem.BandType = bandBox.Text; SyncChannelsIntoDayModel(activeFactionName, activeDay); } };
                    Grid.SetColumn(bandBox, 2);
                    rowGrid.Children.Add(bandBox);

                    var freqBox = new TextBox
                    {
                        Text = chItem.FreqMhz,
                        IsReadOnly = !isManual,
                        Height = 26,
                        FontSize = 10,
                        FontWeight = FontWeights.Bold,
                        Padding = new Thickness(6, 0, 6, 0),
                        Margin = new Thickness(0, 0, 4, 0),
                        VerticalContentAlignment = VerticalAlignment.Center,
                        Background = isManual ? TacticalConfigPanel.BrushInputBg : new SolidColorBrush(Color.FromRgb(7, 17, 21)),
                        Foreground = Brushes.White,
                        BorderBrush = isManual ? TacticalConfigPanel.BrushAccent : TacticalConfigPanel.BrushTileBorder
                    };
                    freqBox.TextChanged += (_, __) => { if (isManual) { chItem.FreqMhz = freqBox.Text; SyncChannelsIntoDayModel(activeFactionName, activeDay); } };
                    Grid.SetColumn(freqBox, 3);
                    rowGrid.Children.Add(freqBox);

                    var toneBox = new TextBox
                    {
                        Text = chItem.ToneOrCode,
                        IsReadOnly = !isManual,
                        Height = 26,
                        FontSize = 10,
                        Padding = new Thickness(6, 0, 6, 0),
                        Margin = new Thickness(0, 0, isManual ? 4 : 0, 0),
                        VerticalContentAlignment = VerticalAlignment.Center,
                        Background = isManual ? TacticalConfigPanel.BrushInputBg : new SolidColorBrush(Color.FromRgb(7, 17, 21)),
                        Foreground = new SolidColorBrush(Color.FromRgb(200, 220, 228)),
                        BorderBrush = isManual ? TacticalConfigPanel.BrushAccent : TacticalConfigPanel.BrushTileBorder
                    };
                    toneBox.TextChanged += (_, __) => { if (isManual) { chItem.ToneOrCode = toneBox.Text; SyncChannelsIntoDayModel(activeFactionName, activeDay); } };
                    Grid.SetColumn(toneBox, 4);
                    rowGrid.Children.Add(toneBox);

                    if (isManual)
                    {
                        var btnDel = CreateConsistentOpordSquareActionBtn("\u2212", false, $"Usu\u0144 kana\u0142: {chItem.RoleName}", sectionDMinusBg, sectionDMinusBorder, () =>
                        {
                            if (channels.Count > 1)
                            {
                                channels.Remove(chItem);
                                SyncChannelsIntoDayModel(activeFactionName, activeDay);
                                rebuildCallback?.Invoke();
                            }
                        });
                        btnDel.HorizontalAlignment = HorizontalAlignment.Right;
                        Grid.SetColumn(btnDel, 5);
                        rowGrid.Children.Add(btnDel);
                    }

                    cStack.Children.Add(rowGrid);
                }

                if (isManual)
                {
                    var manualBtnsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
                    var btnAddManualCh = new Button
                    {
                        Content = "+ Dodaj kana\u0142 r\u0119czny",
                        Height = 26,
                        Padding = new Thickness(12, 0, 12, 0),
                        Margin = new Thickness(0, 0, 8, 0),
                        Background = new SolidColorBrush(Color.FromRgb(16, 52, 34)),
                        Foreground = Brushes.White,
                        BorderBrush = new SolidColorBrush(Color.FromRgb(46, 204, 113)),
                        BorderThickness = new Thickness(1.2),
                        FontSize = 10,
                        FontWeight = FontWeights.Bold,
                        Cursor = Cursors.Hand
                    };
                    btnAddManualCh.Click += (_, __) =>
                    {
                        channels.Add(new OpordManualChannelItem
                        {
                            RoleName = $"Kana\u0142 {channels.Count + 1}",
                            BandType = "PMR",
                            FreqMhz = $"CH {Math.Clamp(channels.Count + 1, 1, 16)}",
                            ToneOrCode = "CTCSS 67.0 Hz"
                        });
                        SyncChannelsIntoDayModel(activeFactionName, activeDay);
                        rebuildCallback?.Invoke();
                    };

                    var btnResetFromGen = new Button
                    {
                        Content = "\uD83D\uDD04 Przywr\u00F3\u0107 kana\u0142y z Generatora Stron",
                        Height = 26,
                        Padding = new Thickness(12, 0, 12, 0),
                        Background = TacticalConfigPanel.BrushInputBg,
                        Foreground = TacticalConfigPanel.BrushAccent,
                        BorderBrush = TacticalConfigPanel.BrushAccent,
                        BorderThickness = new Thickness(1),
                        FontSize = 10,
                        FontWeight = FontWeights.Bold,
                        Cursor = Cursors.Hand
                    };
                    btnResetFromGen.Click += (_, __) =>
                    {
                        V34CleanOpordCommsStore.ForceResyncFromFactionGenerator(activeFactionName);
                        SyncChannelsIntoDayModel(activeFactionName, activeDay);
                        rebuildCallback?.Invoke();
                    };

                    manualBtnsRow.Children.Add(btnAddManualCh);
                    manualBtnsRow.Children.Add(btnResetFromGen);
                    cStack.Children.Add(manualBtnsRow);
                }

                commsCard.Child = cStack;
                if (insertIndex >= 0 && insertIndex <= secStack.Children.Count)
                    secStack.Children.Insert(insertIndex, commsCard);
                else
                    secStack.Children.Add(commsCard);
                break;
            }
        }

//===PAYLOAD_ANNEX3_ENGINE_CS===
// [START_V34_CLEAN_ANNEX3_DISCORD]
    public sealed class OpordAnnex3State
    {
        public bool Enabled { get; set; } = false;
        public string MapTitle { get; set; } = "Mapa taktyczna rejonu dzia\u0142a\u0144 (AO) \u2014 Arkusz sztabowy";
        public string MapNotes { get; set; } = "Siatka wsp\u00F3\u0142rz\u0119dnych, granica terenu gry (AO) oraz punkty charakterystyczne zgodnie z legend\u0105 mapy.";
        public string ImageBase64 { get; set; } = "";
        public string ImageMimeType { get; set; } = "image/png";
        public string SourceLabel { get; set; } = "Brak wczytanej mapy (kliknij 'Pobierz aktualn\u0105 map\u0119 z Generatora Map 1:1' lub 'Wczytaj plik mapy z dysku')";
        public int PixelWidth { get; set; }
        public int PixelHeight { get; set; }
    }

    internal static class V34CleanAnnex3AndDiscordEngine
    {
        private static readonly Dictionary<string, OpordAnnex3State> Annex3ByFaction = new Dictionary<string, OpordAnnex3State>(StringComparer.OrdinalIgnoreCase);

        public static OpordAnnex3State GetOrCreateState(string factionName)
        {
            string key = string.IsNullOrWhiteSpace(factionName) ? "BLUFOR" : factionName.Trim();
            if (!Annex3ByFaction.TryGetValue(key, out var st) || st == null)
            {
                st = new OpordAnnex3State();
                Annex3ByFaction[key] = st;
            }
            return st;
        }

        public static void AttachToOpordForm(Window win, StackPanel mainStack, OpordParagraphModel model, OpordDayPlanModel activeDay, string activeFactionName = null)
        {
            if (mainStack == null) return;
            string facName = !string.IsNullOrWhiteSpace(activeFactionName)
                ? activeFactionName
                : (!string.IsNullOrWhiteSpace(model?.TargetFaction) ? model.TargetFaction : "BLUFOR");

            var state = GetOrCreateState(facName);

            for (int i = mainStack.Children.Count - 1; i >= 0; i--)
            {
                if (mainStack.Children[i] is FrameworkElement fe && fe.Tag?.ToString() == "V34_ANNEX3_MAP_BOX")
                {
                    mainStack.Children.RemoveAt(i);
                }
            }

            var annex3Card = new Border
            {
                Tag = "V34_ANNEX3_MAP_BOX",
                Background = TacticalConfigPanel.BrushCardBg,
                BorderBrush = state.Enabled ? TacticalConfigPanel.BrushAccent : TacticalConfigPanel.BrushTileBorder,
                BorderThickness = new Thickness(state.Enabled ? 1.5 : 1.0),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 12)
            };

            var outerStack = new StackPanel();
            var bodyStack = new StackPanel
            {
                Visibility = state.Enabled ? Visibility.Visible : Visibility.Collapsed,
                Margin = new Thickness(0, 8, 0, 0)
            };

            var chkEnableAnnex3 = new CheckBox
            {
                Content = $"\uD83D\uDDFA\uFE0F ZA\u0141\u0104CZNIK 3. MAPA TAKTYCZNA OPERACJI / SZKIC SYTUACYJNY \u2014 STRONA: {facName.ToUpperInvariant()} (Opcjonalny \u2014 zaznacz, aby do\u0142\u0105czy\u0107 map\u0119 do OPORD)",
                IsChecked = state.Enabled,
                Foreground = TacticalConfigPanel.BrushAccent,
                FontWeight = FontWeights.Bold,
                FontSize = 11.5,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };

            var previewImg = new Image
            {
                MaxHeight = 320,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var previewContainer = new Border
            {
                Background = TacticalConfigPanel.BrushInputBg,
                BorderBrush = TacticalConfigPanel.BrushTileBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 6, 0, 6),
                Visibility = string.IsNullOrWhiteSpace(state.ImageBase64) ? Visibility.Collapsed : Visibility.Visible,
                Child = previewImg
            };

            var statusLabel = new TextBlock
            {
                Text = state.SourceLabel,
                Foreground = string.IsNullOrWhiteSpace(state.ImageBase64) ? TacticalConfigPanel.BrushMutedText : TacticalConfigPanel.BrushAccent,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 2, 0, 6),
                TextWrapping = TextWrapping.Wrap
            };

            void RefreshPreviewFromBase64()
            {
                if (string.IsNullOrWhiteSpace(state.ImageBase64))
                {
                    previewImg.Source = null;
                    previewContainer.Visibility = Visibility.Collapsed;
                    statusLabel.Text = state.SourceLabel;
                    statusLabel.Foreground = TacticalConfigPanel.BrushMutedText;
                    return;
                }
                try
                {
                    byte[] bytes = Convert.FromBase64String(state.ImageBase64);
                    var bmp = new BitmapImage();
                    using (var ms = new MemoryStream(bytes))
                    {
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.StreamSource = ms;
                        bmp.EndInit();
                        bmp.Freeze();
                    }
                    state.PixelWidth = bmp.PixelWidth;
                    state.PixelHeight = bmp.PixelHeight;
                    previewImg.Source = bmp;
                    previewContainer.Visibility = Visibility.Visible;
                    statusLabel.Text = state.SourceLabel + $" ({bmp.PixelWidth}\u00D7{bmp.PixelHeight} px | {Math.Max(1, bytes.Length / 1024)} KB)";
                    statusLabel.Foreground = TacticalConfigPanel.BrushAccent;
                }
                catch
                {
                    previewImg.Source = null;
                    previewContainer.Visibility = Visibility.Collapsed;
                }
            }

            void CaptureFromMapGeneratorNow(bool silentIfEmpty)
            {
                try
                {
                    byte[] pngBytes = TacticalMapGeneratorView.CaptureCurrentMapSheetForOpordAnnex3Png(win);
                    if (pngBytes != null && pngBytes.Length > 100)
                    {
                        state.ImageBase64 = Convert.ToBase64String(pngBytes);
                        state.ImageMimeType = "image/png";
                        state.SourceLabel = "\u2713 Pobrano aktualny arkusz mapy z Generatora Map 1:1";
                        RefreshPreviewFromBase64();
                    }
                    else if (!silentIfEmpty)
                    {
                        MessageBox.Show(
                            "Nie uda\u0142o si\u0119 pobra\u0107 obrazu z Generatora Map 1:1. Otw\u00F3rz na chwil\u0119 modu\u0142 Generator Map 1:1 lub wczytaj gotowy plik mapy (.PNG / .JPG) z dysku.",
                            "Za\u0142\u0105cznik 3 \u2014 Mapa OPORD",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                }
                catch { }
            }

            chkEnableAnnex3.Checked += (_, __) =>
            {
                state.Enabled = true;
                bodyStack.Visibility = Visibility.Visible;
                annex3Card.BorderBrush = TacticalConfigPanel.BrushAccent;
                annex3Card.BorderThickness = new Thickness(1.5);
                if (string.IsNullOrWhiteSpace(state.ImageBase64))
                {
                    CaptureFromMapGeneratorNow(silentIfEmpty: true);
                }
            };
            chkEnableAnnex3.Unchecked += (_, __) =>
            {
                state.Enabled = false;
                bodyStack.Visibility = Visibility.Collapsed;
                annex3Card.BorderBrush = TacticalConfigPanel.BrushTileBorder;
                annex3Card.BorderThickness = new Thickness(1.0);
            };
            outerStack.Children.Add(chkEnableAnnex3);

            var btnsRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            btnsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
            btnsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
            btnsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.7, GridUnitType.Star) });

            var btnCaptureMap = new Button
            {
                Content = "\uD83D\uDDFA\uFE0F Pobierz aktualn\u0105 map\u0119 z Generatora Map 1:1",
                Height = 30,
                Margin = new Thickness(0, 0, 6, 0),
                Background = TacticalConfigPanel.BrushButtonAccent,
                Foreground = TacticalConfigPanel.BrushDarkFg,
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.Bold,
                FontSize = 10.5,
                Cursor = Cursors.Hand
            };
            btnCaptureMap.Click += (_, __) => CaptureFromMapGeneratorNow(silentIfEmpty: false);

            var btnLoadDiskMap = new Button
            {
                Content = "\uD83D\uDCC2 Wczytaj plik mapy z dysku (.PNG / .JPG)...",
                Height = 30,
                Margin = new Thickness(0, 0, 6, 0),
                Background = new SolidColorBrush(Color.FromRgb(18, 48, 60)),
                Foreground = Brushes.White,
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(1.2),
                FontWeight = FontWeights.Bold,
                FontSize = 10.5,
                Cursor = Cursors.Hand
            };
            btnLoadDiskMap.Click += (_, __) =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Wybierz plik obrazu mapy dla Za\u0142\u0105cznika nr 3 OPORD",
                    Filter = "Obrazy map (*.png;*.jpg;*.jpeg;*.bmp;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.webp|Wszystkie pliki (*.*)|*.*"
                };
                if (dlg.ShowDialog(win) == true && File.Exists(dlg.FileName))
                {
                    try
                    {
                        byte[] raw = File.ReadAllBytes(dlg.FileName);
                        string ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
                        state.ImageMimeType = (ext == ".jpg" || ext == ".jpeg") ? "image/jpeg" : (ext == ".webp" ? "image/webp" : (ext == ".bmp" ? "image/bmp" : "image/png"));
                        state.ImageBase64 = Convert.ToBase64String(raw);
                        state.SourceLabel = "\u2713 Wczytano plik mapy z dysku: " + Path.GetFileName(dlg.FileName);
                        RefreshPreviewFromBase64();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("B\u0142\u0105d wczytywania obrazu mapy: " + ex.Message, "Za\u0142\u0105cznik 3", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            };

            var btnClearMap = new Button
            {
                Content = "\uD83D\uDDD1\uFE0F Usu\u0144 map\u0119",
                Height = 30,
                Background = new SolidColorBrush(Color.FromRgb(55, 18, 22)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(235, 64, 52)),
                BorderThickness = new Thickness(1.2),
                FontWeight = FontWeights.Bold,
                FontSize = 10.5,
                Cursor = Cursors.Hand
            };
            btnClearMap.Click += (_, __) =>
            {
                state.ImageBase64 = "";
                state.SourceLabel = "Usuni\u0119to map\u0119 z Za\u0142\u0105cznika nr 3.";
                RefreshPreviewFromBase64();
            };

            Grid.SetColumn(btnCaptureMap, 0);
            Grid.SetColumn(btnLoadDiskMap, 1);
            Grid.SetColumn(btnClearMap, 2);
            btnsRow.Children.Add(btnCaptureMap);
            btnsRow.Children.Add(btnLoadDiskMap);
            btnsRow.Children.Add(btnClearMap);
            bodyStack.Children.Add(btnsRow);

            bodyStack.Children.Add(statusLabel);

            var titleInputsGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            titleInputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleInputsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });

            var mapTitleBox = (TextBox)TacticalConfigPanel.TextCtrl(state.MapTitle, v => state.MapTitle = v);
            mapTitleBox.Margin = new Thickness(0, 0, 6, 0);
            var mapNotesBox = (TextBox)TacticalConfigPanel.TextCtrl(state.MapNotes, v => state.MapNotes = v);

            var fTitle = TacticalConfigPanel.FieldBox("Tytu\u0142 mapy w Za\u0142\u0105czniku nr 3:", mapTitleBox);
            var fNotes = TacticalConfigPanel.FieldBox("Uwagi / opis pod map\u0105 w Za\u0142\u0105czniku nr 3:", mapNotesBox);
            Grid.SetColumn(fTitle, 0);
            Grid.SetColumn(fNotes, 1);
            titleInputsGrid.Children.Add(fTitle);
            titleInputsGrid.Children.Add(fNotes);
            bodyStack.Children.Add(titleInputsGrid);

            RefreshPreviewFromBase64();
            bodyStack.Children.Add(previewContainer);

            outerStack.Children.Add(bodyStack);
            annex3Card.Child = outerStack;
            mainStack.Children.Add(annex3Card);
        }

        public static string BuildAnnex3HtmlSection(Window win, string factionName, string dayTitle)
        {
            var st = GetOrCreateState(factionName);
            if (!st.Enabled) return "";

            if (string.IsNullOrWhiteSpace(st.ImageBase64))
            {
                try
                {
                    byte[] autoBytes = TacticalMapGeneratorView.CaptureCurrentMapSheetForOpordAnnex3Png(win);
                    if (autoBytes != null && autoBytes.Length > 100)
                    {
                        st.ImageBase64 = Convert.ToBase64String(autoBytes);
                        st.ImageMimeType = "image/png";
                    }
                }
                catch { }
            }

            string E(string s) => System.Net.WebUtility.HtmlEncode(s ?? "");
            var sb = new StringBuilder();
            sb.AppendLine("<div style='page-break-before:always;margin-top:16px;'>");
            sb.AppendLine("<h2>" + BuilderI18n.T("ZA\u0141\u0104CZNIK 3. MAPA TAKTYCZNA OPERACJI (" + (dayTitle ?? "DZIE\u0143 1").ToUpperInvariant() + ")", "ANNEX 3. TACTICAL OPERATION MAP (" + (dayTitle ?? "DAY 1").ToUpperInvariant() + ")") + "</h2>");
            if (!string.IsNullOrWhiteSpace(st.MapTitle))
            {
                sb.AppendLine("<div class='sub-p'><b>" + E(st.MapTitle) + "</b></div>");
            }
            if (!string.IsNullOrWhiteSpace(st.ImageBase64))
            {
                string mime = string.IsNullOrWhiteSpace(st.ImageMimeType) ? "image/png" : st.ImageMimeType;
                sb.AppendLine("<div style='text-align:center;margin:10px 0;'><img src='data:" + mime + ";base64," + st.ImageBase64 + "' style='max-width:100%;max-height:230mm;height:auto;border:2px solid #111;border-radius:4px;'/></div>");
            }
            else
            {
                sb.AppendLine("<div class='sub-p'><i>[Brak wczytanego obrazu mapy w Za\u0142\u0105czniku nr 3]</i></div>");
            }
            if (!string.IsNullOrWhiteSpace(st.MapNotes))
            {
                sb.AppendLine("<div class='sub-p'>" + E(st.MapNotes) + "</div>");
            }
            sb.AppendLine("</div>");
            return sb.ToString();
        }

        public static string BuildAnnex3DiscordMarkdown(string factionName, string dayTitle)
        {
            var st = GetOrCreateState(factionName);
            if (!st.Enabled) return "";

            var sb = new StringBuilder();
            sb.AppendLine("## \uD83D\uDDFA\uFE0F " + BuilderI18n.T("ZA\u0141\u0104CZNIK 3. MAPA TAKTYCZNA OPERACJI (" + dayTitle + ")", "ANNEX 3. TACTICAL OPERATION MAP (" + dayTitle + ")"));
            if (!string.IsNullOrWhiteSpace(st.MapTitle))
                sb.AppendLine("- **Mapa:** " + st.MapTitle);
            if (!string.IsNullOrWhiteSpace(st.MapNotes))
                sb.AppendLine("- **Uwagi do mapy:** " + st.MapNotes);
            return sb.ToString().TrimEnd();
        }
    }
    // [END_V34_CLEAN_ANNEX3_DISCORD]
//===END_ANNEX3_PAYLOADS===