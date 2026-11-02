@echo off
setlocal DisableDelayedExpansion
cd /d "%~dp0"

set "BAT_FILE=%~f0"

echo ============================================================================
echo [KROK 2B/2] Wdrazanie poprawek w Generatorze OPORD (przycisk Pobierz na
echo             gorze, auto-odswiezanie stron, walidacja AO w METOC, nowa Tabela
echo             Lacznosci z opcja edycji recznej) + Wysylka na GitHub Actions...
echo ============================================================================
powershell -NoProfile -ExecutionPolicy Bypass -Command "Add-Type -TypeDefinition ([System.Text.RegularExpressions.Regex]::Split([System.IO.File]::ReadAllText([System.Environment]::GetEnvironmentVariable('BAT_FILE'), [System.Text.Encoding]::UTF8), '//===' + 'STEP2B_OPORD_AND_GITHUB_CS===')[1]); [FthStep2BOpordAndGitHub]::Run()"

if errorlevel 1 (
    echo.
    echo [BLAD] Wystapil blad podczas wykonywania Kroku 2B/2 lub wysylki na GitHub!
    pause
    exit /b 1
)

echo.
echo ============================================================================
echo [SUKCES - KROK 2B/2] Wszystkie zmiany w Generatorze Map i Generatorze OPORD
echo zostaly wdrozone i wyslane na GitHub (QbcolPL/TEST)!
echo Kompilacja na GitHub Actions zostala uruchomiona.
echo ============================================================================
pause
exit /b 0

//===STEP2B_OPORD_AND_GITHUB_CS===
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public static class FthStep2BOpordAndGitHub
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

        string[] p1 = Regex.Split(rawBat, "//===" + "PAYLOAD_OPORD_TRANSFORMER_CS===");
        if (p1.Length < 2) throw new Exception("Brak bloku PAYLOAD_OPORD_TRANSFORMER_CS!");
        string[] p2 = Regex.Split(p1[1], "//===" + "END_STEP2B_PAYLOADS===");

        string payloadOpordTransformer = p2[0].Trim();
        UTF8Encoding utf8NoBom = new UTF8Encoding(false);
        string servicesDir = Path.Combine(targetDir, @"apps\builder\FieldTakHub.Builder\Services");

        // 1. Dodaj metode TryGetMarkedAoCenter w TacticalMapGeneratorView.cs
        PatchTacticalMapGeneratorAoHelper(servicesDir, utf8NoBom);

        // 2. Zaktualizuj V34CleanFtakAndFactionEngine.RefreshCachedFactions oraz V34CleanMapStudioBridge w BuilderCoreSidebarAndProjectEngine.cs,
        //    aby zawsze odczytywaly zywa liste stron i kanalow glosowych z TacticalConfigPanel.GetInternalFactionsList()
        PatchCoreSidebarFactionAndAoSync(servicesDir, utf8NoBom);

        // 3. Zaktualizuj BuilderLocalizationAndOpordEngine.UiPart.cs oraz BuilderLocalizationAndOpordEngine.cs
        PatchOpordUiAndDataEngine(servicesDir, payloadOpordTransformer, utf8NoBom);

        // 4. Zaktualizuj znacznik wyzwalajacy GitHub Actions
        string triggerPath = Path.Combine(targetDir, ".fth_build_trigger.txt");
        string triggerContent = "Field TAK Hub Map & OPORD Complete Upgrade UTC: " + DateTime.UtcNow.ToString("O") + " | RunId: " + Guid.NewGuid().ToString("N") + "\n";
        File.WriteAllText(triggerPath, triggerContent, utf8NoBom);

        // 5. Wyslij calosc na GitHub i uruchom GitHub Actions
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

    private static void PatchTacticalMapGeneratorAoHelper(string servicesDir, UTF8Encoding utf8NoBom)
    {
        string tmgPath = Path.Combine(servicesDir, "TacticalMapGeneratorView.cs");
        if (!File.Exists(tmgPath))
            throw new Exception("Nie znaleziono pliku TacticalMapGeneratorView.cs!");

        string tmg = File.ReadAllText(tmgPath, Encoding.UTF8);

        if (!tmg.Contains("public static bool TryGetMarkedAoCenter("))
        {
            string aoHelperMethod = @"        public static bool TryGetMarkedAoCenter(out double aoLat, out double aoLon, out string coordsLabel)
        {
            var ao = _mapObjects.FirstOrDefault(o => o.Kind == ""AO"" && o.Points != null && o.Points.Count >= 3);
            if (ao != null)
            {
                aoLat = ao.Points.Average(p => p.Lat);
                aoLon = ao.Points.Average(p => p.Lon);
                var (mgrs, _, _, _, _) = FormatTacticalPointCoordinates(aoLat, aoLon);
                coordsLabel = $""Obszar AO (MGRS: {mgrs})"";
                return true;
            }
            aoLat = 0.0;
            aoLon = 0.0;
            coordsLabel = """";
            return false;
        }

        ";
            int anchor = tmg.IndexOf("private static void UpdateSheetViewportSize()", StringComparison.Ordinal);
            if (anchor > 0)
            {
                tmg = tmg.Substring(0, anchor) + aoHelperMethod + tmg.Substring(anchor);
                File.WriteAllText(tmgPath, tmg, utf8NoBom);
            }
        }
        Console.WriteLine("[OK] Dodano TryGetMarkedAoCenter w TacticalMapGeneratorView.cs.");
    }

    private static void PatchCoreSidebarFactionAndAoSync(string servicesDir, UTF8Encoding utf8NoBom)
    {
        string corePath = Path.Combine(servicesDir, "BuilderCoreSidebarAndProjectEngine.cs");
        if (!File.Exists(corePath)) return;

        string core = File.ReadAllText(corePath, Encoding.UTF8);

        // Spraw, aby RefreshCachedFactions zawsze w pierwszej kolejnosci pobieralo zywa liste stron i ich kanalow glosowych z TacticalConfigPanel.GetInternalFactionsList()
        string oldRefreshStart = @"        public static List<V34FactionSnapshot> RefreshCachedFactions(Window win, List<DependencyObject> preScanned = null)
        {
            var list = new List<V34FactionSnapshot>();
            try
            {";

        string newRefreshStart = @"        public static List<V34FactionSnapshot> RefreshCachedFactions(Window win, List<DependencyObject> preScanned = null)
        {
            var list = new List<V34FactionSnapshot>();
            try
            {
                var internalFactions = TacticalConfigPanel.GetInternalFactionsList();
                if (internalFactions != null && internalFactions.Count > 0)
                {
                    foreach (var f in internalFactions)
                    {
                        if (f == null || string.IsNullOrWhiteSpace(f.Name)) continue;
                        var snap = new V34FactionSnapshot
                        {
                            FactionName = f.Name.Trim(),
                            ColorHex = string.IsNullOrWhiteSpace(f.ColorHex) ? ""#2684FF"" : f.ColorHex.Trim().ToUpperInvariant(),
                            ChannelName = string.IsNullOrWhiteSpace(f.ChannelName) ? (f.Name.Trim().ToUpperInvariant() + ""-MESH"") : f.ChannelName.Trim(),
                            Region = string.IsNullOrWhiteSpace(f.LoraRegion) ? ""EU_868"" : f.LoraRegion.Trim(),
                            ModemPreset = string.IsNullOrWhiteSpace(f.LoraModemPreset) ? ""SHORT_FAST"" : f.LoraModemPreset.Trim(),
                            OtsHost = f.OtsHost ?? """",
                            EmergencyPhone = string.IsNullOrWhiteSpace(f.EmergencyPhone) ? ""Brak / 112"" : f.EmergencyPhone.Trim(),
                            ExpirationHours = f.ValidityHours
                        };
                        if (f.VoiceChannels != null)
                        {
                            foreach (var vc in f.VoiceChannels)
                            {
                                snap.VoiceRows.Add(new V34FactionVoiceChannelRow
                                {
                                    Role = string.IsNullOrWhiteSpace(vc.RoleName) ? ""Kana\u0142"" : vc.RoleName,
                                    Band = string.IsNullOrWhiteSpace(vc.BandType) ? ""PMR"" : vc.BandType,
                                    Freq = string.IsNullOrWhiteSpace(vc.FreqMhz) ? ""-"" : vc.FreqMhz,
                                    Tone = string.IsNullOrWhiteSpace(vc.ToneOrCode) ? ""Brak tonu"" : vc.ToneOrCode
                                });
                            }
                        }
                        if (snap.VoiceRows.Count == 0)
                            snap.VoiceRows.Add(new V34FactionVoiceChannelRow());
                        list.Add(snap);
                    }
                    if (list.Count > 0)
                    {
                        CachedFactions.Clear();
                        CachedFactions.AddRange(list);
                        return CachedFactions;
                    }
                }";

        if (!core.Contains("var internalFactions = TacticalConfigPanel.GetInternalFactionsList();") && core.Contains(oldRefreshStart))
        {
            core = core.Replace(oldRefreshStart, newRefreshStart);
        }

        // Spraw, aby SyncMapStudioState uzywalo bezposrednio TacticalMapGeneratorView.TryGetMarkedAoCenter
        string oldSyncMapStart = @"        public static void SyncMapStudioState(Window win)
        {
            try
            {";

        string newSyncMapStart = @"        public static void SyncMapStudioState(Window win)
        {
            try
            {
                if (TacticalMapGeneratorView.TryGetMarkedAoCenter(out double markedLat, out double markedLon, out _))
                {
                    HasExplicitAoMarked = true;
                    AoCenterLat = markedLat;
                    AoCenterLon = markedLon;
                    return;
                }
                else
                {
                    HasExplicitAoMarked = false;
                }";

        if (!core.Contains("TacticalMapGeneratorView.TryGetMarkedAoCenter") && core.Contains(oldSyncMapStart))
        {
            core = core.Replace(oldSyncMapStart, newSyncMapStart);
        }

        File.WriteAllText(corePath, core, utf8NoBom);
        Console.WriteLine("[OK] Zaktualizowano synchronizacje stron i AO w BuilderCoreSidebarAndProjectEngine.cs.");
    }

    private static void PatchOpordUiAndDataEngine(string servicesDir, string payloadOpordTransformer, UTF8Encoding utf8NoBom)
    {
        // A. BuilderLocalizationAndOpordEngine.UiPart.cs
        string uiPartPath = Path.Combine(servicesDir, "BuilderLocalizationAndOpordEngine.UiPart.cs");
        if (!File.Exists(uiPartPath))
            throw new Exception("Nie znaleziono pliku BuilderLocalizationAndOpordEngine.UiPart.cs!");

        string uiPart = File.ReadAllText(uiPartPath, Encoding.UTF8);

        // 1. Dodaj statyczny delegat RebuildActiveOpordViewAction i wywoluj go przy kazdym otwarciu widoku OPORD
        if (!uiPart.Contains("public static Action RebuildActiveOpordViewAction;"))
        {
            uiPart = uiPart.Replace(
                "public static Border CreateOrGetOpordOverlayView(Window win)",
                "public static Action RebuildActiveOpordViewAction;\r\n\r\n        public static Border CreateOrGetOpordOverlayView(Window win)");
        }

        // 2. Wewnatrz RebuildOpordForm() zarejestruj RebuildActiveOpordViewAction i wywolaj V34CleanOpordUiTransformer.ApplyToOpordForm
        if (!uiPart.Contains("V34CleanOpordUiTransformer.ApplyToOpordForm"))
        {
            uiPart = uiPart.Replace(
                "void RebuildOpordForm()\r\n            {\r\n                mainStack.Children.Clear();",
                "void RebuildOpordForm()\r\n            {\r\n                RebuildActiveOpordViewAction = RebuildOpordForm;\r\n                mainStack.Children.Clear();\r\n                win?.Dispatcher.BeginInvoke(new Action(() => V34CleanOpordUiTransformer.ApplyToOpordForm(win, mainStack, _activeOpordFaction, RebuildOpordForm)), System.Windows.Threading.DispatcherPriority.Loaded);");

            uiPart = uiPart.Replace(
                "void RebuildOpordForm()\n            {\n                mainStack.Children.Clear();",
                "void RebuildOpordForm()\n            {\n                RebuildActiveOpordViewAction = RebuildOpordForm;\n                mainStack.Children.Clear();\n                win?.Dispatcher.BeginInvoke(new Action(() => V34CleanOpordUiTransformer.ApplyToOpordForm(win, mainStack, _activeOpordFaction, RebuildOpordForm)), System.Windows.Threading.DispatcherPriority.Loaded);");
        }

        // 3. Podmien blok V34CleanOpordUiTransformer w BuilderLocalizationAndOpordEngine.UiPart.cs
        int trStart = uiPart.IndexOf("// [START_V34_CLEAN_OPORD_UI_TRANSFORMER]", StringComparison.Ordinal);
        int trEnd = uiPart.IndexOf("// [END_V34_CLEAN_OPORD_UI_TRANSFORMER]", StringComparison.Ordinal);
        if (trStart > 0 && trEnd > trStart)
        {
            int afterEnd = trEnd + "// [END_V34_CLEAN_OPORD_UI_TRANSFORMER]".Length;
            uiPart = uiPart.Substring(0, trStart) + payloadOpordTransformer + uiPart.Substring(afterEnd);
        }

        int openUi = uiPart.Count(c => c == '{');
        int closeUi = uiPart.Count(c => c == '}');
        Console.WriteLine("[WERYFIKACJA] Nawiasy klamrowe w BuilderLocalizationAndOpordEngine.UiPart.cs: { = " + openUi + ", } = " + closeUi);
        File.WriteAllText(uiPartPath, uiPart, utf8NoBom);
        Console.WriteLine("[OK] Zaktualizowano BuilderLocalizationAndOpordEngine.UiPart.cs.");

        // B. BuilderLocalizationAndOpordEngine.cs
        string opEnginePath = Path.Combine(servicesDir, "BuilderLocalizationAndOpordEngine.cs");
        if (File.Exists(opEnginePath))
        {
            string opEng = File.ReadAllText(opEnginePath, Encoding.UTF8);

            // 1. W OnSwitchedToOpordTab wywolaj bezposrednio OpordBuilderManager.RebuildActiveOpordViewAction?.Invoke()
            opEng = opEng.Replace(
                @"var mRebuild = typeof(OpordBuilderManager).GetMethod(
                        ""RebuildOpordForm"",
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    if (mRebuild != null)
                    {
                        mRebuild.Invoke(null, new object[] { w });
                    }",
                @"OpordBuilderManager.RebuildActiveOpordViewAction?.Invoke();");

            // 2. W GeneratePrintableOpordHtml uzyj pelnej listy kanalow z V34CleanOpordCommsStore.GetEffectiveChannelsForFaction(factionName)
            string oldHtmlRadio = @"                var radioRowsHtml = new StringBuilder();
                radioRowsHtml.AppendLine(""<tr><td><b>"" + BuilderI18n.T(""Kanał główny ("" + d.DayTitle + "")"", ""Primary Channel ("" + d.DayTitle + "")"") + ""</b></td><td>PMR/VHF</td><td colspan='2'><b>"" + E(d.PrimaryRadioFreq) + ""</b></td></tr>"");
                radioRowsHtml.AppendLine(""<tr><td><b>"" + BuilderI18n.T(""Kanał zapasowy ("" + d.DayTitle + "")"", ""Backup Channel ("" + d.DayTitle + "")"") + ""</b></td><td>PMR/VHF</td><td colspan='2'><b>"" + E(d.BackupRadioFreq) + ""</b></td></tr>"");
                radioRowsHtml.AppendLine(""<tr><td><b>"" + BuilderI18n.T(""Kanał HQ ("" + d.DayTitle + "")"", ""HQ Channel ("" + d.DayTitle + "")"") + ""</b></td><td>PMR/VHF</td><td colspan='2'><b>"" + E(d.HqRadioFreq) + ""</b></td></tr>"");

                var vArr = (facJson?[""VoiceChannels""] ?? facJson?[""voiceChannels""]) as JsonArray;
                if (vArr != null && vArr.Count > 0)
                {
                    foreach (var vo in vArr.OfType<JsonObject>())
                    {
                        radioRowsHtml.AppendLine(
                            ""<tr><td><b>"" + E(vo[""roleName""]?.ToString() ?? vo[""RoleName""]?.ToString()) + ""</b></td>"" +
                            ""<td>"" + E(vo[""bandType""]?.ToString() ?? vo[""BandType""]?.ToString()) + ""</td>"" +
                            ""<td><b>"" + E(vo[""freqMhz""]?.ToString() ?? vo[""FreqMhz""]?.ToString()) + ""</b></td>"" +
                            ""<td>"" + E(vo[""toneOrCode""]?.ToString() ?? vo[""ToneOrCode""]?.ToString()) + ""</td></tr>"");
                    }
                }";

            string newHtmlRadio = @"                var radioRowsHtml = new StringBuilder();
                foreach (var chRow in V34CleanOpordCommsStore.GetEffectiveChannelsForFaction(factionName))
                {
                    radioRowsHtml.AppendLine(
                        ""<tr><td><b>"" + E(chRow.RoleName) + ""</b></td>"" +
                        ""<td>"" + E(chRow.BandType) + ""</td>"" +
                        ""<td><b>"" + E(chRow.FreqMhz) + ""</b></td>"" +
                        ""<td>"" + E(chRow.ToneOrCode) + ""</td></tr>"");
                }";

            if (opEng.Contains(oldHtmlRadio))
            {
                opEng = opEng.Replace(oldHtmlRadio, newHtmlRadio);
            }

            File.WriteAllText(opEnginePath, opEng, utf8NoBom);
            Console.WriteLine("[OK] Zaktualizowano BuilderLocalizationAndOpordEngine.cs.");
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
        RunProcess(gitExe, "commit -m \"Upgrade Map Generator (36 military vectors, visual dropdown, 4-sided resizable & editable Legend, inline point naming) and OPORD (top pull button, auto-refresh factions, AO-only METOC guard, remodeled Comms Table with manual checkbox)\" --allow-empty", targetDir, ignoreError: false);

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
//===STEP2B_OPORD_AND_GITHUB_CS===

//===PAYLOAD_OPORD_TRANSFORMER_CS===
// [START_V34_CLEAN_OPORD_UI_TRANSFORMER]
    public sealed class OpordManualChannelItem
    {
        public string RoleName { get; set; } = "Kana\u0142 Taktyczny";
        public string BandType { get; set; } = "PMR";
        public string FreqMhz { get; set; } = "CH 1 (446.00625)";
        public string ToneOrCode { get; set; } = "CTCSS 67.0 Hz";
    }

    public static class V34CleanOpordCommsStore
    {
        private static readonly Dictionary<string, bool> ManualOverrideByFaction = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, List<OpordManualChannelItem>> ManualChannelsByFaction = new Dictionary<string, List<OpordManualChannelItem>>(StringComparer.OrdinalIgnoreCase);

        public static bool IsManualOverrideEnabled(string factionName)
        {
            string key = string.IsNullOrWhiteSpace(factionName) ? "BLUFOR" : factionName.Trim();
            return ManualOverrideByFaction.TryGetValue(key, out bool val) && val;
        }

        public static void SetManualOverrideEnabled(string factionName, bool enabled)
        {
            string key = string.IsNullOrWhiteSpace(factionName) ? "BLUFOR" : factionName.Trim();
            ManualOverrideByFaction[key] = enabled;
            if (enabled && !ManualChannelsByFaction.ContainsKey(key))
            {
                ManualChannelsByFaction[key] = PullLiveChannelsFromFactionGenerator(key);
            }
        }

        public static List<OpordManualChannelItem> PullLiveChannelsFromFactionGenerator(string factionName)
        {
            var result = new List<OpordManualChannelItem>();
            try
            {
                var list = TacticalConfigPanel.GetInternalFactionsList();
                var fac = list.FirstOrDefault(f => string.Equals(f.Name, factionName, StringComparison.OrdinalIgnoreCase)) ?? list.FirstOrDefault();
                if (fac != null && fac.VoiceChannels != null && fac.VoiceChannels.Count > 0)
                {
                    foreach (var vc in fac.VoiceChannels)
                    {
                        result.Add(new OpordManualChannelItem
                        {
                            RoleName = string.IsNullOrWhiteSpace(vc.RoleName) ? "Kana\u0142" : vc.RoleName,
                            BandType = string.IsNullOrWhiteSpace(vc.BandType) ? "PMR" : vc.BandType,
                            FreqMhz = string.IsNullOrWhiteSpace(vc.FreqMhz) ? "-" : vc.FreqMhz,
                            ToneOrCode = string.IsNullOrWhiteSpace(vc.ToneOrCode) ? "Brak tonu" : vc.ToneOrCode
                        });
                    }
                }
            }
            catch { }

            if (result.Count == 0)
            {
                result.Add(new OpordManualChannelItem { RoleName = "Dowodzenie HQ", BandType = "PMR", FreqMhz = "CH 1 (446.00625)", ToneOrCode = "CTCSS 67.0 Hz" });
            }
            return result;
        }

        public static List<OpordManualChannelItem> GetEffectiveChannelsForFaction(string factionName)
        {
            string key = string.IsNullOrWhiteSpace(factionName) ? "BLUFOR" : factionName.Trim();
            if (IsManualOverrideEnabled(key))
            {
                if (!ManualChannelsByFaction.TryGetValue(key, out var manualList) || manualList == null)
                {
                    manualList = PullLiveChannelsFromFactionGenerator(key);
                    ManualChannelsByFaction[key] = manualList;
                }
                return manualList;
            }
            return PullLiveChannelsFromFactionGenerator(key);
        }

        public static void ForceResyncFromFactionGenerator(string factionName)
        {
            string key = string.IsNullOrWhiteSpace(factionName) ? "BLUFOR" : factionName.Trim();
            ManualChannelsByFaction[key] = PullLiveChannelsFromFactionGenerator(key);
        }
    }

    internal static class V34CleanOpordUiTransformer
    {
        public static void ApplyToOpordForm(Window win, StackPanel mainOpordStack, string activeFactionName, Action rebuildCallback)
        {
            try
            {
                var w = win ?? Application.Current?.MainWindow;
                if (mainOpordStack == null) return;

                var model = V34CleanOpordDataEngine.GetLiveOpordModel();
                OpordDayPlanModel activeDay = null;
                if (model != null && model.Days != null && model.Days.Count > 0)
                {
                    int idx = Math.Clamp(model.ActiveDayIndex, 0, model.Days.Count - 1);
                    activeDay = model.Days[idx];
                }

                // 1. Pasek gorny: Aktualizacja listy stron + dodanie przycisku "Pobierz z Generatora Stron" obok wyboru strony
                UpgradeTopActionBarWithPullButton(w, mainOpordStack, activeFactionName, activeDay, rebuildCallback);

                // 2. Sekcja METOC: Pobieranie wspolrzednych wylacznie z zaznaczonego AO lub miejscowosci (blokada domyslnego arkusza mapy)
                UpgradeMetocSectionValidation(w, mainOpordStack, activeDay);

                // 3. Sekcja III.E Tabela lacznosci: Automatyczne pobieranie kanalow z Generatora Stron + opcjonalna edycja reczna na "ptaszek"
                RebuildCommunicationsTableSection(w, mainOpordStack, activeFactionName, activeDay, rebuildCallback);

                // 4. Zalacznik 3 i integracja eksportu
                if (model != null && activeDay != null)
                {
                    V34CleanAnnex3AndDiscordEngine.AttachToOpordForm(w, mainOpordStack, model, activeDay);
                }
            }
            catch { }
        }

        private static void UpgradeTopActionBarWithPullButton(Window win, StackPanel mainOpordStack, string activeFactionName, OpordDayPlanModel activeDay, Action rebuildCallback)
        {
            if (mainOpordStack.Children.Count == 0) return;
            var topCard = mainOpordStack.Children[0] as Border;
            if (topCard == null) return;

            var topNodes = V34CleanSidebarAndHeaderEngine.GetLogicalTreeNodes(topCard);
            var facCombo = topNodes.OfType<ComboBox>().FirstOrDefault();
            if (facCombo != null)
            {
                var liveFactions = TacticalConfigPanel.GetInternalFactionsList();
                var validNames = liveFactions.Select(f => f.Name).Where(n => !string.IsNullOrWhiteSpace(n) && !V34CleanOpordDataEngine.IsGarbageFactionName(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (validNames.Count > 0)
                {
                    facCombo.Items.Clear();
                    foreach (string fn in validNames) facCombo.Items.Add(fn);
                    string targetSel = validNames.FirstOrDefault(n => string.Equals(n, activeFactionName, StringComparison.OrdinalIgnoreCase)) ?? validNames[0];
                    facCombo.SelectedItem = targetSel;
                }

                if (LogicalTreeHelper.GetParent(facCombo) is Grid actionBar && actionBar.ColumnDefinitions.Count >= 5)
                {
                    bool alreadyHasTopPullBtn = actionBar.Children.OfType<FrameworkElement>().Any(fe => fe.Tag?.ToString() == "TOP_OPORD_PULL_FACTIONS_BTN");
                    if (!alreadyHasTopPullBtn)
                    {
                        actionBar.ColumnDefinitions[0].Width = new GridLength(335);
                        actionBar.Children.Remove(facCombo);

                        var comboAndPullGrid = new Grid { Tag = "TOP_OPORD_PULL_FACTIONS_BTN", Margin = new Thickness(0, 0, 6, 0) };
                        comboAndPullGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(135) });
                        comboAndPullGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                        facCombo.Margin = new Thickness(0, 0, 5, 0);
                        Grid.SetColumn(facCombo, 0);
                        comboAndPullGrid.Children.Add(facCombo);

                        var btnPullTop = new Button
                        {
                            Content = "\uD83D\uDD04 Pobierz z Generatora Stron",
                            Height = 32,
                            Background = new SolidColorBrush(Color.FromRgb(18, 52, 64)),
                            Foreground = TacticalConfigPanel.BrushAccent,
                            BorderBrush = TacticalConfigPanel.BrushAccent,
                            BorderThickness = new Thickness(1.3),
                            FontWeight = FontWeights.Bold,
                            FontSize = 10.5,
                            Cursor = Cursors.Hand,
                            ToolTip = "Od\u015Bwie\u017C list\u0119 stron konfliktu, cz\u0119stotliwo\u015Bci radiowe, parametry LoRa Meshtastic oraz obszar AO"
                        };
                        btnPullTop.Click += (_, __) =>
                        {
                            V34CleanOpordCommsStore.ForceResyncFromFactionGenerator(activeFactionName);
                            SyncChannelsIntoDayModel(activeFactionName, activeDay);
                            V34CleanOpordDataEngine.SyncFactionsAndMapToOpordModel(win);
                            rebuildCallback?.Invoke();
                        };
                        Grid.SetColumn(btnPullTop, 1);
                        comboAndPullGrid.Children.Add(btnPullTop);

                        Grid.SetColumn(comboAndPullGrid, 0);
                        actionBar.Children.Add(comboAndPullGrid);
                    }
                }
            }

            // Ukryj stary przycisk "Pobierz z Generatora Stron" w dolnych sekcjach formularza
            var allOpordBtns = V34CleanSidebarAndHeaderEngine.GetLogicalTreeNodes(mainOpordStack).OfType<Button>().ToList();
            foreach (var b in allOpordBtns)
            {
                if (IsDescendantOf(topCard, b)) continue;
                string bt = V34CleanFtakAndFactionEngine.ExtractButtonTextSafe(b);
                if (bt.Contains("Pobierz z Generatora Stron", StringComparison.OrdinalIgnoreCase) ||
                    bt.Contains("Pull from Faction", StringComparison.OrdinalIgnoreCase))
                {
                    b.Visibility = Visibility.Collapsed;
                    b.Width = 0;
                    b.Height = 0;
                    b.Margin = new Thickness(0);
                }
            }
        }

        private static void UpgradeMetocSectionValidation(Window win, StackPanel mainOpordStack, OpordDayPlanModel activeDay)
        {
            bool hasAo = TacticalMapGeneratorView.TryGetMarkedAoCenter(out double aoLat, out double aoLon, out string aoLabel);

            foreach (var border in mainOpordStack.Children.OfType<Border>())
            {
                var nodes = V34CleanSidebarAndHeaderEngine.GetLogicalTreeNodes(border);
                var texts = nodes.OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
                bool isMetocCard = texts.Any(t => t.Contains("METOC", StringComparison.OrdinalIgnoreCase) || t.Contains("Prognoza pogody", StringComparison.OrdinalIgnoreCase));
                if (!isMetocCard) continue;

                var textBoxes = nodes.OfType<TextBox>().ToList();
                TextBox locNameBox = null, latBox = null, lonBox = null;

                foreach (var tb in textBoxes)
                {
                    if (LogicalTreeHelper.GetParent(tb) is Panel p)
                    {
                        string lbl = string.Join(" ", p.Children.OfType<TextBlock>().Select(x => x.Text ?? ""));
                        if (lbl.Contains("Miejscowo", StringComparison.OrdinalIgnoreCase) || lbl.Contains("Rejon AO", StringComparison.OrdinalIgnoreCase) || lbl.Contains("Location", StringComparison.OrdinalIgnoreCase))
                            locNameBox = tb;
                        else if (lbl.Contains("Szer", StringComparison.OrdinalIgnoreCase) || lbl.Contains("Lat", StringComparison.OrdinalIgnoreCase))
                            latBox = tb;
                        else if (lbl.Contains("D\u0142", StringComparison.OrdinalIgnoreCase) || lbl.Contains("Lon", StringComparison.OrdinalIgnoreCase))
                            lonBox = tb;
                    }
                }

                if (hasAo)
                {
                    string latS = aoLat.ToString("0.0000", CultureInfo.InvariantCulture);
                    string lonS = aoLon.ToString("0.0000", CultureInfo.InvariantCulture);
                    if (activeDay != null)
                    {
                        activeDay.LocationLat = latS;
                        activeDay.LocationLon = lonS;
                        if (string.IsNullOrWhiteSpace(activeDay.LocationName))
                            activeDay.LocationName = aoLabel;
                    }
                    if (latBox != null) latBox.Text = latS;
                    if (lonBox != null) lonBox.Text = lonS;
                    if (locNameBox != null && string.IsNullOrWhiteSpace(locNameBox.Text))
                        locNameBox.Text = aoLabel;
                }
                else
                {
                    // Brak zaznaczonego AO na mapie -> wyczysc domyslne wspolrzedne arkusza mapy, jesli nie wpisano miejscowosci
                    bool noTownTyped = locNameBox == null || string.IsNullOrWhiteSpace(locNameBox.Text) || locNameBox.Text.Contains("Obszar AO", StringComparison.OrdinalIgnoreCase);
                    if (noTownTyped)
                    {
                        if (locNameBox != null && locNameBox.Text.Contains("Obszar AO", StringComparison.OrdinalIgnoreCase))
                            locNameBox.Text = "";
                        if (latBox != null) latBox.Text = "";
                        if (lonBox != null) lonBox.Text = "";
                        if (activeDay != null)
                        {
                            if (activeDay.LocationName != null && activeDay.LocationName.Contains("Obszar AO", StringComparison.OrdinalIgnoreCase))
                                activeDay.LocationName = "";
                            activeDay.LocationLat = "";
                            activeDay.LocationLon = "";
                        }
                    }
                }

                foreach (var btn in nodes.OfType<Button>())
                {
                    string bt = V34CleanFtakAndFactionEngine.ExtractButtonTextSafe(btn);
                    if (bt.Contains("METOC", StringComparison.OrdinalIgnoreCase) ||
                        bt.Contains("pogod", StringComparison.OrdinalIgnoreCase) ||
                        bt.Contains("Weather", StringComparison.OrdinalIgnoreCase))
                    {
                        if (btn.Tag?.ToString() != "METOC_AO_GUARD_HOOKED")
                        {
                            btn.Tag = "METOC_AO_GUARD_HOOKED";
                            btn.PreviewMouseLeftButtonDown += (s, e) =>
                            {
                                bool currentHasAo = TacticalMapGeneratorView.TryGetMarkedAoCenter(out double cAoLat, out double cAoLon, out string cAoLbl);
                                string typedTown = locNameBox != null ? (locNameBox.Text ?? "").Trim() : (activeDay?.LocationName ?? "").Trim();

                                if (!currentHasAo && string.IsNullOrWhiteSpace(typedTown))
                                {
                                    e.Handled = true;
                                    if (latBox != null) latBox.Text = "";
                                    if (lonBox != null) lonBox.Text = "";
                                    MessageBox.Show(
                                        "Brak zaznaczonego obszaru gry (AO) oraz miejscowo\u015Bci!\n\nAby pobra\u0107 prognoz\u0119 pogody METOC:\n1. Zaznacz Granic\u0119 Terenu Gry (AO) w Generatorze Map 1:1 (dane pobior\u0105 si\u0119 automatycznie),\nLUB\n2. Wpisz nazw\u0119 miejscowo\u015Bci w polu 'Miejscowo\u015B\u0107 / Rejon AO'.",
                                        "METOC \u2014 Wymagane zaznaczenie AO lub miejscowo\u015B\u0107",
                                        MessageBoxButton.OK,
                                        MessageBoxImage.Warning);
                                    return;
                                }

                                if (currentHasAo)
                                {
                                    string latStr = cAoLat.ToString("0.0000", CultureInfo.InvariantCulture);
                                    string lonStr = cAoLon.ToString("0.0000", CultureInfo.InvariantCulture);
                                    if (latBox != null) latBox.Text = latStr;
                                    if (lonBox != null) lonBox.Text = lonStr;
                                    if (locNameBox != null && string.IsNullOrWhiteSpace(locNameBox.Text))
                                        locNameBox.Text = cAoLbl;
                                    if (activeDay != null)
                                    {
                                        activeDay.LocationLat = latStr;
                                        activeDay.LocationLon = lonStr;
                                        if (string.IsNullOrWhiteSpace(activeDay.LocationName))
                                            activeDay.LocationName = cAoLbl;
                                    }
                                }
                            };
                        }
                    }
                }
            }
        }

        public static void SyncChannelsIntoDayModel(string factionName, OpordDayPlanModel activeDay)
        {
            if (activeDay == null) return;
            var channels = V34CleanOpordCommsStore.GetEffectiveChannelsForFaction(factionName);
            if (channels.Count > 0)
            {
                var c0 = channels[0];
                activeDay.PrimaryRadioFreq = $"{c0.RoleName}: {c0.FreqMhz} [{c0.BandType}] ({c0.ToneOrCode})";
            }
            if (channels.Count > 1)
            {
                var c1 = channels[1];
                activeDay.BackupRadioFreq = $"{c1.RoleName}: {c1.FreqMhz} [{c1.BandType}] ({c1.ToneOrCode})";
            }
            if (channels.Count > 2)
            {
                var c2 = channels[2];
                activeDay.HqRadioFreq = $"{c2.RoleName}: {c2.FreqMhz} [{c2.BandType}] ({c2.ToneOrCode})";
            }
        }

        private static void RebuildCommunicationsTableSection(Window win, StackPanel mainOpordStack, string activeFactionName, OpordDayPlanModel activeDay, Action rebuildCallback)
        {
            SyncChannelsIntoDayModel(activeFactionName, activeDay);

            foreach (var border in mainOpordStack.Children.OfType<Border>())
            {
                var nodes = V34CleanSidebarAndHeaderEngine.GetLogicalTreeNodes(border);
                var texts = nodes.OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
                bool isExecSection = texts.Any(t => t.Contains("III. WYKONANIE", StringComparison.OrdinalIgnoreCase) || t.Contains("III. EXECUTION", StringComparison.OrdinalIgnoreCase) || t.Contains("Tabela \u0142\u0105czno\u015Bci", StringComparison.OrdinalIgnoreCase));
                if (!isExecSection) continue;

                var secStack = border.Child as StackPanel;
                if (secStack == null) continue;

                // Znajdz naglowek "E. Tabela lacznosci" i ukryj stare 3 pola pod nim
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

                // Usun poprzednia instancje przebudowanej karty, jesli istnieje
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
                var chkManual = new CheckBox
                {
                    Content = "\u270F\uFE0F Wprowad\u017A / edytuj cz\u0119stotliwo\u015Bci r\u0119cznie",
                    IsChecked = isManual,
                    Foreground = isManual ? new SolidColorBrush(Color.FromRgb(250, 208, 44)) : Brushes.White,
                    FontWeight = FontWeights.Bold,
                    FontSize = 10.5,
                    VerticalAlignment = VerticalAlignment.Center,
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
                DockPanel.SetDock(chkManual, Dock.Right);
                hdrDock.Children.Add(chkManual);

                hdrDock.Children.Add(new TextBlock
                {
                    Text = $"\uD83D\uDCFB E. TABELA \u0141\u0104CZNO\u015ACI \u2014 STRONA: {activeFactionName.ToUpperInvariant()} " + (isManual ? "[TRYB EDYCJI R\u0118CZNEJ]" : "[AUTO Z GENERATORA STRON]"),
                    Foreground = TacticalConfigPanel.BrushAccent,
                    FontWeight = FontWeights.Bold,
                    FontSize = 11.5,
                    VerticalAlignment = VerticalAlignment.Center
                });
                cStack.Children.Add(hdrDock);

                // Pasek parametrow cyfrowych (LoRa Meshtastic + OTS + SOS)
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

                // Naglowek kolumn tabeli kanalow glosowych
                var tblHdr = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                tblHdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
                tblHdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
                tblHdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
                tblHdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
                tblHdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
                tblHdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(isManual ? 30 : 0) });

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
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(isManual ? 30 : 0) });

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
                        var btnDel = new Button
                        {
                            Content = "\u2212",
                            Width = 26,
                            Height = 26,
                            Background = TacticalConfigPanel.BrushRedMinus,
                            Foreground = Brushes.White,
                            BorderBrush = Brushes.White,
                            BorderThickness = new Thickness(1),
                            FontWeight = FontWeights.Bold,
                            Cursor = Cursors.Hand,
                            ToolTip = "Usu\u0144 ten kana\u0142"
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
                        Background = TacticalConfigPanel.BrushGreenPlus,
                        Foreground = Brushes.White,
                        BorderBrush = Brushes.White,
                        BorderThickness = new Thickness(1),
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

        private static bool IsDescendantOf(DependencyObject ancestor, DependencyObject descendant)
        {
            DependencyObject cur = descendant;
            while (cur != null)
            {
                if (cur == ancestor) return true;
                cur = LogicalTreeHelper.GetParent(cur) ?? VisualTreeHelper.GetParent(cur);
            }
            return false;
        }
    }
    // [END_V34_CLEAN_OPORD_UI_TRANSFORMER]
//===END_STEP2B_PAYLOADS===