@echo off
setlocal DisableDelayedExpansion
cd /d "%~dp0"

set "BAT_FILE=%~f0"

echo ============================================================================
echo [1/3] Wdrazanie odczytywania i rekonstrukcji Sektorow Ognia przy ponownym
echo       wczytywaniu plikow .KMZ / .KML do Generatora Map...
echo ============================================================================
powershell -NoProfile -ExecutionPolicy Bypass -Command "Add-Type -TypeDefinition ([System.Text.RegularExpressions.Regex]::Split([System.IO.File]::ReadAllText([System.Environment]::GetEnvironmentVariable('BAT_FILE'), [System.Text.Encoding]::UTF8), '//===' + 'ADD_SECTOR_KMZ_IMPORT_CS===')[1]); [FthAddSectorKmzImport]::Run()"

if errorlevel 1 (
    echo.
    echo [BLAD] Wystapil blad podczas aktualizacji plikow lub wysylki na GitHub!
    pause
    exit /b 1
)

echo.
echo ============================================================================
echo [SUKCES] Odczytywanie Sektorow Ognia z plikow .KMZ / .KML zostalo wdrozone
echo i wyslane na GitHub (QbcolPL/TEST)! Kompilacja na GitHub Actions uruchomiona.
echo ============================================================================
pause
exit /b 0

//===ADD_SECTOR_KMZ_IMPORT_CS===
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public static class FthAddSectorKmzImport
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
        string rngPath = Path.Combine(servicesDir, "TacticalMapGeneratorView.RangeSectors.cs");

        if (!File.Exists(tmgPath) || !File.Exists(rngPath))
            throw new Exception("Nie znaleziono plikow TacticalMapGeneratorView.cs lub RangeSectors.cs!");

        string tmg = File.ReadAllText(tmgPath, Encoding.UTF8);
        string eng = File.Exists(engPath) ? File.ReadAllText(engPath, Encoding.UTF8) : "";

        // 1. Podpiecie automatycznego odczytywania sektorow ognia przy wczytywaniu pliku .KMZ / .KML
        tmg = InjectRangeSectorsIntoKmlImportCode(tmg, "TacticalMapGeneratorView.cs");
        if (!string.IsNullOrEmpty(eng))
        {
            eng = InjectRangeSectorsIntoKmlImportCode(eng, "TacticalMapGeneratorView.Engine.cs");
        }

        int openTmg = tmg.Count(c => c == '{');
        int closeTmg = tmg.Count(c => c == '}');
        if (openTmg != closeTmg)
            throw new Exception("Niezgodnosc nawiasow klamrowych w TacticalMapGeneratorView.cs!");
        File.WriteAllText(tmgPath, tmg, utf8NoBom);

        if (!string.IsNullOrEmpty(eng))
        {
            int openEng = eng.Count(c => c == '{');
            int closeEng = eng.Count(c => c == '}');
            if (openEng != closeEng)
                throw new Exception("Niezgodnosc nawiasow klamrowych w TacticalMapGeneratorView.Engine.cs!");
            File.WriteAllText(engPath, eng, utf8NoBom);
        }

        // 2. Rozbudowa TacticalMapGeneratorView.RangeSectors.cs o kotwiczenie geograficzne WGS84
        //    oraz odczytywanie i odtwarzanie sektorow ognia z plikow .KMZ / .KML
        string combined = tmg + "\n" + eng;
        string latField = DetectLatFieldName(combined);
        string lonField = DetectLonFieldName(combined, latField);

        string batFile = Environment.GetEnvironmentVariable("BAT_FILE");
        string rawBat = File.ReadAllText(batFile, Encoding.UTF8);

        string[] parts = Regex.Split(rawBat, "//===" + "PAYLOAD_IMPORT_KMZ_SECTORS_CS===");
        if (parts.Length < 2)
            throw new Exception("Brak bloku PAYLOAD_IMPORT_KMZ_SECTORS_CS w pliku .bat!");
        string[] endParts = Regex.Split(parts[1], "//===" + "END_IMPORT_KMZ_SECTORS_CS===");
        string importMethods = endParts[0].Trim()
            .Replace("__LAT_FIELD__", latField)
            .Replace("__LON_FIELD__", lonField);

        string rng = File.ReadAllText(rngPath, Encoding.UTF8);

        int existingIdx = rng.IndexOf("private static void SyncSectorGeoAnchorFromOffsets", StringComparison.Ordinal);
        if (existingIdx >= 0)
        {
            rng = rng.Substring(0, existingIdx).TrimEnd() + "\r\n    }\r\n}";
        }

        // A) Dodaj pola HasGeoAnchor, ShooterLat, ShooterLon do TacticalRangeSectorItem
        if (!rng.Contains("public bool HasGeoAnchor"))
        {
            rng = rng.Replace(
                "public double OffsetNorthMeters { get; set; } = 0.0;",
                "public double OffsetNorthMeters { get; set; } = 0.0;\r\n            public bool HasGeoAnchor { get; set; } = false;\r\n            public double ShooterLat { get; set; } = 0.0;\r\n            public double ShooterLon { get; set; } = 0.0;");
        }

        // B) Aktualizuj kotwice geograficzna przy tworzeniu nowego sektora i przy przesuwaniu myszka na mapie
        if (!rng.Contains("SyncSectorGeoAnchorFromOffsets(item);"))
        {
            rng = rng.Replace(
                "_weaponRangeSectors.Add(item);",
                "SyncSectorGeoAnchorFromOffsets(item);\r\n            _weaponRangeSectors.Add(item);");
        }

        if (!rng.Contains("SyncSectorGeoAnchorFromOffsets(sec);"))
        {
            rng = rng.Replace(
                "sec.OffsetNorthMeters = startNorthM - (cur.Y - startMouse.Y) * mpp;",
                "sec.OffsetNorthMeters = startNorthM - (cur.Y - startMouse.Y) * mpp;\r\n                    SyncSectorGeoAnchorFromOffsets(sec);");
        }

        // C) Synchronizuj przesuniecie w metrach ze wspolrzednymi GPS podczas renderowania arkusza
        if (!rng.Contains("if (sec.HasGeoAnchor) SyncSectorOffsetsFromGeoAnchor(sec);"))
        {
            string renderAnchor = "if (_isInteractingWithSectorOnMap)";
            rng = rng.Replace(
                renderAnchor,
                "if (!_isInteractingWithSectorOnMap) { foreach (var sec in _weaponRangeSectors) { if (sec.HasGeoAnchor) SyncSectorOffsetsFromGeoAnchor(sec); } }\r\n                " + renderAnchor);
        }

        // D) Dodaj przycisk '📂 Wczytaj z .KMZ / .KML' obok przycisku eksportu w panelu Sektorow Ognia
        if (!rng.Contains("ImportStandaloneRangeSectorsKmzDialog()"))
        {
            string oldBtnBlock = @"var btnExportKmz = new Button
            {
                Content = ""📦 Eksportuj Sektory Ognia do pliku .KMZ / .KML dla ATAK"",
                Height = 24,
                Margin = new Thickness(0, 0, 0, 6),
                Background = new SolidColorBrush(Color.FromRgb(7, 17, 21)),
                Foreground = new SolidColorBrush(Color.FromRgb(46, 204, 113)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(46, 204, 113)),
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.Bold,
                FontSize = 9.5,
                Cursor = Cursors.Hand,
                ToolTip = ""Zapisz wyrysowane sektory ognia jako wielokąty KML/KMZ gotowe do zaimportowania w ATAK (sektory są też automatycznie dołączane do głównego eksportu mapy .KMZ)""
            };
            btnExportKmz.Click += (s, e) => ExportStandaloneRangeSectorsKmzDialog();
            sp.Children.Add(btnExportKmz);";

            string newBtnBlock = @"var kmzActionGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 6) };
            var btnImportKmz = new Button
            {
                Content = ""📂 Wczytaj z .KMZ / .KML"",
                Height = 24,
                Margin = new Thickness(0, 0, 3, 0),
                Background = new SolidColorBrush(Color.FromRgb(7, 17, 21)),
                Foreground = TacticalConfigPanel.BrushAccent,
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.Bold,
                FontSize = 9.2,
                Cursor = Cursors.Hand,
                ToolTip = ""Wczytaj wyrysowane wcześniej sektory ognia z pliku .KMZ lub .KML (obsługiwany jest także główny przycisk importu KMZ Generatora Map)""
            };
            btnImportKmz.Click += (s, e) => ImportStandaloneRangeSectorsKmzDialog();
            kmzActionGrid.Children.Add(btnImportKmz);

            var btnExportKmz = new Button
            {
                Content = ""📦 Eksportuj .KMZ (ATAK)"",
                Height = 24,
                Margin = new Thickness(3, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(7, 17, 21)),
                Foreground = new SolidColorBrush(Color.FromRgb(46, 204, 113)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(46, 204, 113)),
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.Bold,
                FontSize = 9.2,
                Cursor = Cursors.Hand,
                ToolTip = ""Zapisz wyrysowane sektory ognia jako wielokąty KML/KMZ dla ATAK""
            };
            btnExportKmz.Click += (s, e) => ExportStandaloneRangeSectorsKmzDialog();
            kmzActionGrid.Children.Add(btnExportKmz);
            sp.Children.Add(kmzActionGrid);";

            if (rng.Contains("btnExportKmz"))
            {
                rng = rng.Replace(oldBtnBlock, newBtnBlock);
            }
            else
            {
                rng = rng.Replace("sp.Children.Add(hdrGrid);", "sp.Children.Add(hdrGrid);\r\n            " + newBtnBlock);
            }
        }

        // E) Wstaw metody importu KML/KMZ oraz synchronizacji kotwic geograficznych przed zamknieciem klasy
        int lastBrace = rng.LastIndexOf('}');
        int secondLastBrace = rng.LastIndexOf('}', lastBrace - 1);
        if (secondLastBrace < 0)
            throw new Exception("Nieprawidlowa struktura nawiasow w TacticalMapGeneratorView.RangeSectors.cs!");

        rng = rng.Substring(0, secondLastBrace) + "\r\n        " + importMethods + "\r\n    }\r\n}\r\n";

        int openRng = rng.Count(c => c == '{');
        int closeRng = rng.Count(c => c == '}');
        Console.WriteLine("[WERYFIKACJA] Nawiasy w TacticalMapGeneratorView.RangeSectors.cs: { = " + openRng + ", } = " + closeRng);
        if (openRng != closeRng)
            throw new Exception("Niezgodnosc nawiasow klamrowych w TacticalMapGeneratorView.RangeSectors.cs!");

        File.WriteAllText(rngPath, rng, utf8NoBom);
        Console.WriteLine("[OK] Zaktualizowano TacticalMapGeneratorView.RangeSectors.cs o odczytywanie sektorow z .KMZ / .KML.");

        string triggerPath = Path.Combine(targetDir, ".fth_build_trigger.txt");
        File.WriteAllText(triggerPath, "Field TAK Hub Import Weapon Range Sectors from KMZ/KML UTC: " + DateTime.UtcNow.ToString("O") + " | RunId: " + Guid.NewGuid().ToString("N") + "\n", utf8NoBom);

        PushAndTriggerGitHubActions(targetDir);
    }

    private static string InjectRangeSectorsIntoKmlImportCode(string code, string fileLabel)
    {
        if (code.Contains("ExtractAndImportRangeSectorsFromKml("))
            return code;

        int count = 0;

        // 1. Owin pierwszy argument w wywolaniach XDocument.Parse(...)
        code = WrapFirstArgBalanced(code, "XDocument.Parse", "ExtractAndImportRangeSectorsFromKml", ref count);

        // 2. Owin wywolania .ReadToEnd() ze StreamReadera (przy rozpakowywaniu doc.kml z archiwum .KMZ)
        code = Regex.Replace(
            code,
            @"([a-zA-Z0-9_]+\.ReadToEnd\s*\(\s*\))",
            m =>
            {
                count++;
                return "ExtractAndImportRangeSectorsFromKml(" + m.Groups[1].Value + ")";
            });

        // 3. Owin zbalansowane wywolania File.ReadAllText(...) (przy wczytywaniu pliku .KML)
        code = WrapCallBalanced(code, "File.ReadAllText", "ExtractAndImportRangeSectorsFromKml", ref count);

        if (count > 0)
        {
            Console.WriteLine("[OK] Podpieto automatyczny odczyt sektorow ognia z KML/KMZ w " + fileLabel + " (" + count + " miejsc).");
        }
        return code;
    }

    private static string WrapFirstArgBalanced(string code, string methodToken, string wrapperName, ref int count)
    {
        int idx = 0;
        while (idx < code.Length)
        {
            int pos = code.IndexOf(methodToken, idx, StringComparison.Ordinal);
            if (pos < 0) break;

            int openP = pos + methodToken.Length;
            while (openP < code.Length && char.IsWhiteSpace(code[openP])) openP++;
            if (openP >= code.Length || code[openP] != '(')
            {
                idx = openP;
                continue;
            }

            int depth = 0;
            int argEnd = -1;
            for (int i = openP; i < code.Length; i++)
            {
                char ch = code[i];
                if (ch == '(') depth++;
                else if (ch == ')')
                {
                    depth--;
                    if (depth == 0) { argEnd = i; break; }
                }
                else if (ch == ',' && depth == 1)
                {
                    argEnd = i;
                    break;
                }
            }
            if (argEnd < 0) break;

            string firstArg = code.Substring(openP + 1, argEnd - (openP + 1)).Trim();
            if (firstArg.Contains(wrapperName))
            {
                idx = argEnd + 1;
                continue;
            }

            string wrappedArg = wrapperName + "(" + firstArg + ")";
            code = code.Substring(0, openP + 1) + wrappedArg + code.Substring(argEnd);
            count++;
            idx = openP + 1 + wrappedArg.Length + 1;
        }
        return code;
    }

    private static string WrapCallBalanced(string code, string targetToken, string wrapperName, ref int count)
    {
        int idx = 0;
        while (idx < code.Length)
        {
            int pos = code.IndexOf(targetToken, idx, StringComparison.Ordinal);
            if (pos < 0) break;

            int openP = pos + targetToken.Length;
            while (openP < code.Length && char.IsWhiteSpace(code[openP])) openP++;
            if (openP >= code.Length || code[openP] != '(')
            {
                idx = openP;
                continue;
            }

            int depth = 0;
            int closeP = -1;
            for (int i = openP; i < code.Length; i++)
            {
                if (code[i] == '(') depth++;
                else if (code[i] == ')')
                {
                    depth--;
                    if (depth == 0) { closeP = i; break; }
                }
            }
            if (closeP < 0) break;

            string before = code.Substring(Math.Max(0, pos - wrapperName.Length - 4), Math.Min(wrapperName.Length + 4, pos));
            if (before.Contains(wrapperName))
            {
                idx = closeP + 1;
                continue;
            }

            string callExpr = code.Substring(pos, closeP - pos + 1);
            string wrapped = wrapperName + "(" + callExpr + ")";
            code = code.Substring(0, pos) + wrapped + code.Substring(closeP + 1);
            count++;
            idx = pos + wrapped.Length;
        }
        return code;
    }

    private static string DetectLatFieldName(string combined)
    {
        int mppIdx = combined.IndexOf("GetExactMetersPerPixel", StringComparison.Ordinal);
        if (mppIdx >= 0)
        {
            string snippet = combined.Substring(mppIdx, Math.Min(600, combined.Length - mppIdx));
            Match m = Regex.Match(snippet, @"(_[a-zA-Z0-9_]*Lat[a-zA-Z0-9_]*)\b");
            if (m.Success) return m.Groups[1].Value;
        }
        string[] candidates = new[] { "_centerLat", "_currentLat", "_mapCenterLat", "_targetLat", "_lat" };
        foreach (string c in candidates)
            if (Regex.IsMatch(combined, @"\b" + Regex.Escape(c) + @"\b")) return c;
        return "_centerLat";
    }

    private static string DetectLonFieldName(string combined, string latField)
    {
        string guessedLon = latField.Replace("Lat", "Lon").Replace("lat", "lon");
        if (Regex.IsMatch(combined, @"\b" + Regex.Escape(guessedLon) + @"\b")) return guessedLon;
        string[] candidates = new[] { "_centerLon", "_currentLon", "_mapCenterLon", "_targetLon", "_lon", "_centerLng" };
        foreach (string c in candidates)
            if (Regex.IsMatch(combined, @"\b" + Regex.Escape(c) + @"\b")) return c;
        return "_centerLon";
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
        string gitExe = "git";
        string g1 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Git\cmd\git.exe");
        string g2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Git\cmd\git.exe");
        if (File.Exists(g1)) gitExe = g1;
        else if (File.Exists(g2)) gitExe = g2;

        string lockFile = Path.Combine(targetDir, ".git", "index.lock");
        if (File.Exists(lockFile)) { try { File.Delete(lockFile); } catch { } }

        RunProcess(gitExe, "init", targetDir);
        RunProcess(gitExe, "config user.name \"QbcolPL\"", targetDir);
        RunProcess(gitExe, "config user.email \"QbcolPL@users.noreply.github.com\"", targetDir);
        RunProcess(gitExe, "branch -M main", targetDir);
        RunProcess(gitExe, "remote add origin " + RepoRemoteUrl, targetDir);
        RunProcess(gitExe, "remote set-url origin " + RepoRemoteUrl, targetDir);

        RunProcess(gitExe, "add -A", targetDir);
        RunProcess(gitExe, "commit -m \"Add automatic import and reconstruction of weapon range sectors from .KMZ / .KML files\" --allow-empty", targetDir);

        int pushExitCode = RunProcess(gitExe, "push -u origin main --force", targetDir);
        if (pushExitCode != 0)
            throw new Exception("Polecenie 'git push -u origin main --force' zwrocilo kod bledu: " + pushExitCode);

        try { Process.Start(new ProcessStartInfo { FileName = RepoActionsUrl, UseShellExecute = true }); } catch { }
    }

    private static int RunProcess(string fileName, string arguments, string workingDir)
    {
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
            return proc.ExitCode;
        }
    }
}
//===ADD_SECTOR_KMZ_IMPORT_CS===

//===PAYLOAD_IMPORT_KMZ_SECTORS_CS===
        // ====================================================================
        // KOTWICZENIE GEOGRAFICZNE WGS84 (LAT/LON <-> OFFSET METERS)
        // Zapewnia, ze wczytane z .KMZ/.KML sektory trafiaja dokladnie w swoje
        // wspolrzedne GPS na mapie niezaleznie od przesuniecia srodka arkusza!
        // ====================================================================
        private static void SyncSectorGeoAnchorFromOffsets(TacticalRangeSectorItem sec)
        {
            if (sec == null) return;
            double centerLat = __LAT_FIELD__;
            double centerLon = __LON_FIELD__;
            double latRad = centerLat * Math.PI / 180.0;
            double mPerDegLat = 111132.92 - 559.82 * Math.Cos(2.0 * latRad) + 1.175 * Math.Cos(4.0 * latRad);
            double mPerDegLon = Math.Max(1000.0, 111412.84 * Math.Cos(latRad) - 93.5 * Math.Cos(3.0 * latRad));

            sec.ShooterLat = centerLat + (sec.OffsetNorthMeters / mPerDegLat);
            sec.ShooterLon = centerLon + (sec.OffsetEastMeters / mPerDegLon);
            sec.HasGeoAnchor = true;
        }

        private static void SyncSectorOffsetsFromGeoAnchor(TacticalRangeSectorItem sec)
        {
            if (sec == null || !sec.HasGeoAnchor) return;
            double centerLat = __LAT_FIELD__;
            double centerLon = __LON_FIELD__;
            double latRad = centerLat * Math.PI / 180.0;
            double mPerDegLat = 111132.92 - 559.82 * Math.Cos(2.0 * latRad) + 1.175 * Math.Cos(4.0 * latRad);
            double mPerDegLon = Math.Max(1000.0, 111412.84 * Math.Cos(latRad) - 93.5 * Math.Cos(3.0 * latRad));

            sec.OffsetNorthMeters = (sec.ShooterLat - centerLat) * mPerDegLat;
            sec.OffsetEastMeters = (sec.ShooterLon - centerLon) * mPerDegLon;
        }

        // ====================================================================
        // ODCZYTYWANIE WYRYSOWANYCH SEKTOROW OGNIA PRZY IMPORCIE .KMZ / .KML
        // ====================================================================
        public static string ExtractAndImportRangeSectorsFromKml(string kmlContent)
        {
            if (string.IsNullOrEmpty(kmlContent)) return kmlContent;

            if (kmlContent.IndexOf("Sektory Ognia i Zasięg Replik", StringComparison.OrdinalIgnoreCase) < 0 &&
                kmlContent.IndexOf("fth_sector_", StringComparison.OrdinalIgnoreCase) < 0 &&
                kmlContent.IndexOf("Zasięg płaski:", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return kmlContent;
            }

            try
            {
                int importedCount = ParseAndRegisterRangeSectorsFromKmlText(kmlContent, centerMapIfStandalone: false);
                if (importedCount > 0)
                {
                    string cleanedKml = Regex.Replace(
                        kmlContent,
                        @"<Folder>\s*<name>\s*Sektory Ognia i Zasięg Replik\s*</name>[\s\S]*?</Folder>",
                        string.Empty,
                        RegexOptions.IgnoreCase);

                    bool hasOtherPlacemarks = cleanedKml.IndexOf("<Placemark", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!hasOtherPlacemarks)
                    {
                        CenterMapOnImportedSectorsIfNeeded();
                    }

                    SchedulePostImportRangeSectorRefresh();
                    return hasOtherPlacemarks ? cleanedKml : kmlContent;
                }
            }
            catch { }

            return kmlContent;
        }

        private static void SchedulePostImportRangeSectorRefresh()
        {
            if (_interactiveCanvas == null) return;
            _interactiveCanvas.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            {
                foreach (var sec in _weaponRangeSectors)
                {
                    if (sec.HasGeoAnchor)
                        SyncSectorOffsetsFromGeoAnchor(sec);
                }
                RebuildRangeSectorsListUi();
                PopulateRangeSectorEditorFromSelected();
                RefreshRangeSectorsNow(forceFullRebuild: true);
            }));
        }

        private static void CenterMapOnImportedSectorsIfNeeded()
        {
            var anchored = _weaponRangeSectors.Where(s => s.HasGeoAnchor).ToList();
            if (anchored.Count == 0) return;

            double avgLat = anchored.Average(s => s.ShooterLat);
            double avgLon = anchored.Average(s => s.ShooterLon);

            if (Math.Abs(__LAT_FIELD__ - avgLat) > 0.018 || Math.Abs(__LON_FIELD__ - avgLon) > 0.025)
            {
                __LAT_FIELD__ = avgLat;
                __LON_FIELD__ = avgLon;
            }

            foreach (var sec in anchored)
            {
                SyncSectorOffsetsFromGeoAnchor(sec);
            }
        }

        private static int ParseAndRegisterRangeSectorsFromKmlText(string kmlContent, bool centerMapIfStandalone)
        {
            if (string.IsNullOrEmpty(kmlContent)) return 0;

            string scopeText = kmlContent;
            Match mFolder = Regex.Match(
                kmlContent,
                @"<Folder>\s*<name>\s*Sektory Ognia i Zasięg Replik\s*</name>([\s\S]*?)</Folder>",
                RegexOptions.IgnoreCase);
            if (mFolder.Success)
            {
                scopeText = mFolder.Groups[1].Value;
            }

            var styleColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var styleOpacities = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            foreach (Match sm in Regex.Matches(scopeText, @"<Style\s+id=""([^""]+)""\s*>([\s\S]*?)</Style>", RegexOptions.IgnoreCase))
            {
                string styleId = sm.Groups[1].Value.Trim();
                string styleBody = sm.Groups[2].Value;

                Match polyColorMatch = Regex.Match(styleBody, @"<PolyStyle>[\s\S]*?<color>\s*([0-9a-fA-F]{8})\s*</color>", RegexOptions.IgnoreCase);
                if (polyColorMatch.Success)
                {
                    string aabbggrr = polyColorMatch.Groups[1].Value;
                    int alphaInt = Convert.ToInt32(aabbggrr.Substring(0, 2), 16);
                    string bb = aabbggrr.Substring(2, 2);
                    string gg = aabbggrr.Substring(4, 2);
                    string rr = aabbggrr.Substring(6, 2);

                    styleColors[styleId] = "#" + rr.ToUpperInvariant() + gg.ToUpperInvariant() + bb.ToUpperInvariant();
                    styleOpacities[styleId] = Math.Clamp(Math.Round(alphaInt / 255.0, 2), 0.05, 0.65);
                }
                else
                {
                    Match lineColorMatch = Regex.Match(styleBody, @"<LineStyle>[\s\S]*?<color>\s*([0-9a-fA-F]{8})\s*</color>", RegexOptions.IgnoreCase);
                    if (lineColorMatch.Success)
                    {
                        string aabbggrr = lineColorMatch.Groups[1].Value;
                        string bb = aabbggrr.Substring(2, 2);
                        string gg = aabbggrr.Substring(4, 2);
                        string rr = aabbggrr.Substring(6, 2);
                        styleColors[styleId] = "#" + rr.ToUpperInvariant() + gg.ToUpperInvariant() + bb.ToUpperInvariant();
                    }
                }
            }

            var pointCoordsByStyle = new Dictionary<string, (double lat, double lon)>(StringComparer.OrdinalIgnoreCase);
            var pointCoordsByName = new Dictionary<string, (double lat, double lon)>(StringComparer.OrdinalIgnoreCase);

            foreach (Match pm in Regex.Matches(scopeText, @"<Placemark>([\s\S]*?)</Placemark>", RegexOptions.IgnoreCase))
            {
                string pBody = pm.Groups[1].Value;
                if (pBody.IndexOf("<Point>", StringComparison.OrdinalIgnoreCase) < 0) continue;

                Match coordMatch = Regex.Match(pBody, @"<Point>[\s\S]*?<coordinates>\s*([-\d.]+)\s*,\s*([-\d.]+)", RegexOptions.IgnoreCase);
                if (!coordMatch.Success) continue;

                if (double.TryParse(coordMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double ptLon) &&
                    double.TryParse(coordMatch.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double ptLat))
                {
                    Match stMatch = Regex.Match(pBody, @"<styleUrl>\s*#?([^<\s]+)\s*</styleUrl>", RegexOptions.IgnoreCase);
                    if (stMatch.Success)
                    {
                        pointCoordsByStyle[stMatch.Groups[1].Value.Trim()] = (ptLat, ptLon);
                    }

                    Match nmMatch = Regex.Match(pBody, @"<name>\s*Stanowisko:\s*(.*?)\s*</name>", RegexOptions.IgnoreCase);
                    if (nmMatch.Success)
                    {
                        pointCoordsByName[UnescapeKmlXml(nmMatch.Groups[1].Value.Trim())] = (ptLat, ptLon);
                    }
                }
            }

            int addedOrUpdated = 0;

            foreach (Match pm in Regex.Matches(scopeText, @"<Placemark>([\s\S]*?)</Placemark>", RegexOptions.IgnoreCase))
            {
                string pBody = pm.Groups[1].Value;
                if (pBody.IndexOf("<Polygon>", StringComparison.OrdinalIgnoreCase) < 0) continue;

                string desc = "";
                Match descMatch = Regex.Match(pBody, @"<description>([\s\S]*?)</description>", RegexOptions.IgnoreCase);
                if (descMatch.Success) desc = UnescapeKmlXml(descMatch.Groups[1].Value.Trim());

                string styleId = "";
                Match stMatch = Regex.Match(pBody, @"<styleUrl>\s*#?([^<\s]+)\s*</styleUrl>", RegexOptions.IgnoreCase);
                if (stMatch.Success) styleId = stMatch.Groups[1].Value.Trim();

                bool isRangeSector = styleId.StartsWith("fth_sector_", StringComparison.OrdinalIgnoreCase) ||
                                     desc.IndexOf("Zasięg płaski:", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isRangeSector) continue;

                string secId = styleId.StartsWith("fth_sector_", StringComparison.OrdinalIgnoreCase)
                    ? styleId.Substring("fth_sector_".Length)
                    : Guid.NewGuid().ToString("N");

                string rawName = "Stanowisko";
                Match nameTagMatch = Regex.Match(pBody, @"<name>([\s\S]*?)</name>", RegexOptions.IgnoreCase);
                if (nameTagMatch.Success)
                {
                    rawName = UnescapeKmlXml(nameTagMatch.Groups[1].Value.Trim());
                    int bracketIdx = rawName.IndexOf(" [", StringComparison.Ordinal);
                    if (bracketIdx > 0) rawName = rawName.Substring(0, bracketIdx).Trim();
                }

                string weaponPreset = "Karabinek Szturmowy (60 m)";
                double baseRadiusM = 60.0;
                double deltaHM = 0.0;
                double azimuthDeg = 45.0;
                double spreadDeg = 75.0;

                if (!string.IsNullOrEmpty(desc))
                {
                    string[] parts = desc.Split('|');
                    foreach (string rawPart in parts)
                    {
                        string part = rawPart.Trim();
                        if (part.StartsWith("Stanowisko:", StringComparison.OrdinalIgnoreCase))
                        {
                            string v = part.Substring("Stanowisko:".Length).Trim();
                            if (!string.IsNullOrEmpty(v)) rawName = v;
                        }
                        else if (part.StartsWith("Klasa:", StringComparison.OrdinalIgnoreCase))
                        {
                            string v = part.Substring("Klasa:".Length).Trim();
                            if (!string.IsNullOrEmpty(v)) weaponPreset = v;
                        }
                        else if (part.StartsWith("Zasięg płaski:", StringComparison.OrdinalIgnoreCase))
                        {
                            Match mNum = Regex.Match(part, @"([-\d.,]+)");
                            if (mNum.Success && double.TryParse(mNum.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double rVal))
                                baseRadiusM = Math.Clamp(rVal, 5.0, 500.0);
                        }
                        else if (part.IndexOf("dH:", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            int dhIdx = part.IndexOf("dH:", StringComparison.OrdinalIgnoreCase);
                            string afterDh = part.Substring(dhIdx + 3);
                            Match mNum = Regex.Match(afterDh, @"([+-]?\d+(?:[.,]\d+)?)");
                            if (mNum.Success && double.TryParse(mNum.Groups[1].Value.Replace("+", "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double dhVal))
                                deltaHM = Math.Clamp(dhVal, -150.0, 150.0);
                        }
                        else if (part.StartsWith("Azymut:", StringComparison.OrdinalIgnoreCase))
                        {
                            Match mNum = Regex.Match(part, @"([-\d.,]+)");
                            if (mNum.Success && double.TryParse(mNum.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double azVal))
                                azimuthDeg = ((azVal % 360.0) + 360.0) % 360.0;
                        }
                        else if (part.StartsWith("Kąt sektora:", StringComparison.OrdinalIgnoreCase))
                        {
                            Match mNum = Regex.Match(part, @"([-\d.,]+)");
                            if (mNum.Success && double.TryParse(mNum.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double spVal))
                                spreadDeg = Math.Clamp(spVal, 5.0, 360.0);
                        }
                    }
                }

                double shooterLat = __LAT_FIELD__;
                double shooterLon = __LON_FIELD__;
                bool foundCoords = false;

                if (!string.IsNullOrEmpty(styleId) && pointCoordsByStyle.TryGetValue(styleId, out var ptByStyle))
                {
                    shooterLat = ptByStyle.lat;
                    shooterLon = ptByStyle.lon;
                    foundCoords = true;
                }
                else if (pointCoordsByName.TryGetValue(rawName, out var ptByName))
                {
                    shooterLat = ptByName.lat;
                    shooterLon = ptByName.lon;
                    foundCoords = true;
                }
                else
                {
                    Match polyCoordsMatch = Regex.Match(pBody, @"<coordinates>([\s\S]*?)</coordinates>", RegexOptions.IgnoreCase);
                    if (polyCoordsMatch.Success)
                    {
                        var coordPairs = new List<(double lon, double lat)>();
                        foreach (Match cm in Regex.Matches(polyCoordsMatch.Groups[1].Value, @"([-\d.]+)\s*,\s*([-\d.]+)"))
                        {
                            if (double.TryParse(cm.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double cLon) &&
                                double.TryParse(cm.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double cLat))
                            {
                                coordPairs.Add((cLon, cLat));
                            }
                        }

                        if (coordPairs.Count > 0)
                        {
                            if (spreadDeg < 359.5)
                            {
                                shooterLon = coordPairs[0].lon;
                                shooterLat = coordPairs[0].lat;
                            }
                            else
                            {
                                shooterLon = coordPairs.Average(p => p.lon);
                                shooterLat = coordPairs.Average(p => p.lat);
                            }
                            foundCoords = true;
                        }
                    }
                }

                string colorHex = (!string.IsNullOrEmpty(styleId) && styleColors.TryGetValue(styleId, out string sc)) ? sc : "#1FE0C5";
                double fillOpacity = (!string.IsNullOrEmpty(styleId) && styleOpacities.TryGetValue(styleId, out double so)) ? so : 0.24;

                var existing = _weaponRangeSectors.FirstOrDefault(x => x.Id == secId);
                if (existing == null)
                {
                    existing = new TacticalRangeSectorItem { Id = secId };
                    _weaponRangeSectors.Add(existing);
                }

                existing.Name = rawName;
                existing.WeaponPreset = weaponPreset;
                existing.BaseRadiusMeters = baseRadiusM;
                existing.ElevationDeltaMeters = deltaHM;
                existing.AzimuthDeg = azimuthDeg;
                existing.SpreadAngleDeg = spreadDeg;
                existing.ColorHex = colorHex;
                existing.FillOpacity = fillOpacity;
                existing.IsVisible = true;

                if (foundCoords)
                {
                    existing.ShooterLat = shooterLat;
                    existing.ShooterLon = shooterLon;
                    existing.HasGeoAnchor = true;
                    SyncSectorOffsetsFromGeoAnchor(existing);
                }

                _selectedRangeSector = existing;
                addedOrUpdated++;
            }

            if (addedOrUpdated > 0)
            {
                if (centerMapIfStandalone)
                {
                    CenterMapOnImportedSectorsIfNeeded();
                }
                RebuildRangeSectorsListUi();
                PopulateRangeSectorEditorFromSelected();
                RefreshRangeSectorsNow(forceFullRebuild: true);
            }

            return addedOrUpdated;
        }

        private static void ImportStandaloneRangeSectorsKmzDialog()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Wczytaj Sektory Ognia z pliku .KMZ lub .KML",
                Filter = "Paczki i pliki mapowe ATAK (*.kmz;*.kml)|*.kmz;*.kml|Pliki KMZ (*.kmz)|*.kmz|Pliki KML (*.kml)|*.kml"
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                string kmlContent = ReadKmlTextFromKmzOrKmlFile(dlg.FileName);
                int count = ParseAndRegisterRangeSectorsFromKmlText(kmlContent, centerMapIfStandalone: true);
                if (count > 0)
                {
                    MessageBox.Show(
                        $"Pomyślnie wczytano i odtworzono na mapie {count} sektor(ów) ognia wraz z parametrami zasięgu, kąta oraz przewagi wysokości ΔH.",
                        "Import Sektorów Ognia z .KMZ / .KML",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(
                        "W wybranym pliku nie znaleziono zapisanych sektorów ognia (szukano folderu 'Sektory Ognia i Zasięg Replik').",
                        "Import Sektorów Ognia",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Błąd podczas wczytywania pliku .KMZ / .KML:\n" + ex.Message, "Błąd importu", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string ReadKmlTextFromKmzOrKmlFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
                return string.Empty;

            if (filePath.EndsWith(".kmz", StringComparison.OrdinalIgnoreCase))
            {
                using (var fs = new System.IO.FileStream(filePath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                using (var za = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Read))
                {
                    var kmlEntry = za.Entries.FirstOrDefault(e => e.FullName.EndsWith(".kml", StringComparison.OrdinalIgnoreCase));
                    if (kmlEntry != null)
                    {
                        using (var sr = new System.IO.StreamReader(kmlEntry.Open(), System.Text.Encoding.UTF8))
                        {
                            return sr.ReadToEnd();
                        }
                    }
                }
            }

            return System.IO.File.ReadAllText(filePath, System.Text.Encoding.UTF8);
        }

        private static string UnescapeKmlXml(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("&lt;", "<")
                    .Replace("&gt;", ">")
                    .Replace("&quot;", "\"")
                    .Replace("&apos;", "'")
                    .Replace("&amp;", "&");
        }
//===END_IMPORT_KMZ_SECTORS_CS===