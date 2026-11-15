@echo off
setlocal DisableDelayedExpansion
cd /d "%~dp0"

set "BAT_FILE=%~f0"

echo ============================================================================
echo [1/3] Wdrazanie automatycznego wpisywania Sektorow Ognia do Legendy na
echo       mapie oraz eksportu wielokatow do pliku .KMZ / .KML dla ATAK...
echo ============================================================================
powershell -NoProfile -ExecutionPolicy Bypass -Command "Add-Type -TypeDefinition ([System.Text.RegularExpressions.Regex]::Split([System.IO.File]::ReadAllText([System.Environment]::GetEnvironmentVariable('BAT_FILE'), [System.Text.Encoding]::UTF8), '//===' + 'ADD_SECTOR_LEGEND_AND_KMZ_CS===')[1]); [FthAddSectorLegendAndKmz]::Run()"

if errorlevel 1 (
    echo.
    echo [BLAD] Wystapil blad podczas aktualizacji plikow lub wysylki na GitHub!
    pause
    exit /b 1
)

echo.
echo ============================================================================
echo [SUKCES] Integracja Sektorow Ognia z Legenda oraz eksport wielokatow .KMZ
echo dla ATAK zostaly wyslane na GitHub (QbcolPL/TEST)!
echo ============================================================================
pause
exit /b 0

//===ADD_SECTOR_LEGEND_AND_KMZ_CS===
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public static class FthAddSectorLegendAndKmz
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

        // 1. Podpiecie wstrzykiwania sektorow ognia do kazdego eksportu KML/KMZ w TacticalMapGeneratorView.cs i Engine.cs
        tmg = InjectRangeSectorsIntoKmlExportCode(tmg, "TacticalMapGeneratorView.cs");
        if (!string.IsNullOrEmpty(eng))
        {
            eng = InjectRangeSectorsIntoKmlExportCode(eng, "TacticalMapGeneratorView.Engine.cs");
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

        // 2. Rozbudowa TacticalMapGeneratorView.RangeSectors.cs o Legende na arkuszu mapy i Eksport .KMZ dla ATAK
        string combined = tmg + "\n" + eng;
        string latField = DetectLatFieldName(combined);
        string lonField = DetectLonFieldName(combined, latField);

        string batFile = Environment.GetEnvironmentVariable("BAT_FILE");
        string rawBat = File.ReadAllText(batFile, Encoding.UTF8);

        string[] parts = Regex.Split(rawBat, "//===" + "PAYLOAD_EXTRA_LEGEND_KMZ_CS===");
        if (parts.Length < 2)
            throw new Exception("Brak bloku PAYLOAD_EXTRA_LEGEND_KMZ_CS w pliku .bat!");
        string[] endParts = Regex.Split(parts[1], "//===" + "END_EXTRA_LEGEND_KMZ_CS===");
        string extraMethods = endParts[0].Trim()
            .Replace("__LAT_FIELD__", latField)
            .Replace("__LON_FIELD__", lonField);

        string rng = File.ReadAllText(rngPath, Encoding.UTF8);

        // Jesli metody byly juz dodane wczesniej, usun stara sekcje przed ponownym wstawieniem
        int existingIdx = rng.IndexOf("private static Grid _lastLegendContainerGrid", StringComparison.Ordinal);
        if (existingIdx >= 0)
        {
            rng = rng.Substring(0, existingIdx).TrimEnd() + "\r\n    }\r\n}";
        }

        // A) Wywolanie UpdateRangeSectorsInLegendBox(legContainer) przy budowaniu Legendy
        if (!rng.Contains("UpdateRangeSectorsInLegendBox(legContainer);"))
        {
            rng = rng.Replace(
                "legContainer.Children.Add(btnAddSector);",
                "legContainer.Children.Add(btnAddSector);\r\n            UpdateRangeSectorsInLegendBox(legContainer);");
        }

        // B) Aktualizacja Legendy na zywo w RefreshRangeSectorsNow oraz po zakonczeniu przeciagania/obracania myszka
        if (!rng.Contains("UpdateRangeSectorsInLegendBox(_lastLegendContainerGrid);"))
        {
            rng = rng.Replace(
                "UpdateBallisticSummaryBanner();",
                "UpdateBallisticSummaryBanner();\r\n            if (_lastLegendContainerGrid != null) UpdateRangeSectorsInLegendBox(_lastLegendContainerGrid);");

            rng = rng.Replace(
                "RebuildRangeSectorsListUi();",
                "RebuildRangeSectorsListUi(); if (_lastLegendContainerGrid != null) UpdateRangeSectorsInLegendBox(_lastLegendContainerGrid);");
        }

        // C) Dodanie przycisku bezposredniego eksportu .KMZ / .KML w panelu bocznym Sektorow Ognia
        if (!rng.Contains("ExportStandaloneRangeSectorsKmzDialog()"))
        {
            string hdrTarget = "sp.Children.Add(hdrGrid);";
            string kmzButtonSnippet = @"sp.Children.Add(hdrGrid);
            var btnExportKmz = new Button
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

            rng = rng.Replace(hdrTarget, kmzButtonSnippet);
        }

        // D) Wstawienie metod UpdateRangeSectorsInLegendBox oraz BuildWeaponRangeSectorsKmlFragment przed zamknieciem klasy
        int lastBrace = rng.LastIndexOf('}');
        int secondLastBrace = rng.LastIndexOf('}', lastBrace - 1);
        if (secondLastBrace < 0)
            throw new Exception("Nieprawidlowa struktura nawiasow w TacticalMapGeneratorView.RangeSectors.cs!");

        rng = rng.Substring(0, secondLastBrace) + "\r\n        " + extraMethods + "\r\n    }\r\n}\r\n";

        int openRng = rng.Count(c => c == '{');
        int closeRng = rng.Count(c => c == '}');
        Console.WriteLine("[WERYFIKACJA] Nawiasy w TacticalMapGeneratorView.RangeSectors.cs: { = " + openRng + ", } = " + closeRng);
        if (openRng != closeRng)
            throw new Exception("Niezgodnosc nawiasow klamrowych w TacticalMapGeneratorView.RangeSectors.cs!");

        File.WriteAllText(rngPath, rng, utf8NoBom);
        Console.WriteLine("[OK] Zapisano TacticalMapGeneratorView.RangeSectors.cs z integracja Legendy i eksportem .KMZ dla ATAK.");

        string triggerPath = Path.Combine(targetDir, ".fth_build_trigger.txt");
        File.WriteAllText(triggerPath, "Field TAK Hub Range Sectors Legend & ATAK KMZ Polygon Export UTC: " + DateTime.UtcNow.ToString("O") + " | RunId: " + Guid.NewGuid().ToString("N") + "\n", utf8NoBom);

        PushAndTriggerGitHubActions(targetDir);
    }

    private static string InjectRangeSectorsIntoKmlExportCode(string code, string fileLabel)
    {
        if (code.Contains("BuildWeaponRangeSectorsKmlFragment()"))
            return code;

        int count = 0;

        // 1. Dopisz wielokaty sektorow ognia przed kazdym .AppendLine("</Document>") oraz .Append("</Document>")
        string patternAppend = @"\.(AppendLine|Append)\s*\(\s*""</Document>""\s*\)";
        code = Regex.Replace(code, patternAppend, m =>
        {
            count++;
            string method = m.Groups[1].Value;
            return "." + method + "(BuildWeaponRangeSectorsKmlFragment() + \"</Document>\")";
        });

        // 2. Jezeli KML jest konwertowany na bajty do archiwum .KMZ przez Encoding.UTF8.GetBytes(...)
        string patternBytes = @"Encoding\.UTF8\.GetBytes\s*\(\s*([a-zA-Z0-9_]+(?:\.ToString\(\))?)\s*\)";
        code = Regex.Replace(code, patternBytes, m =>
        {
            string expr = m.Groups[1].Value;
            if (expr.Contains("InjectRangeSectorsIntoKmlDocument")) return m.Value;
            count++;
            return "Encoding.UTF8.GetBytes(InjectRangeSectorsIntoKmlDocument(" + expr + "))";
        });

        if (count > 0)
        {
            Console.WriteLine("[OK] Podpieto automatyczny eksport sektorow ognia do KML/KMZ w " + fileLabel + " (" + count + " miejsc).");
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
        RunProcess(gitExe, "commit -m \"Add weapon range sectors to Map Legend and export as KML/KMZ polygons for ATAK\" --allow-empty", targetDir);

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
//===ADD_SECTOR_LEGEND_AND_KMZ_CS===

//===PAYLOAD_EXTRA_LEGEND_KMZ_CS===
private static Grid _lastLegendContainerGrid = null;

        private static void UpdateRangeSectorsInLegendBox(Grid legContainer)
        {
            if (legContainer == null) return;
            _lastLegendContainerGrid = legContainer;

            StackPanel legStack = null;
            foreach (UIElement child in legContainer.Children)
            {
                if (child is Border legBorder && legBorder.Child is StackPanel sp)
                {
                    legStack = sp;
                    break;
                }
            }
            if (legStack == null) return;

            var oldPanels = new List<UIElement>();
            foreach (UIElement el in legStack.Children)
            {
                if (el is FrameworkElement fe && fe.Tag is string t && t == "FTH_RANGE_SECTOR_LEGEND_PANEL")
                    oldPanels.Add(el);
            }
            foreach (var rem in oldPanels)
                legStack.Children.Remove(rem);

            var visibleSectors = _weaponRangeSectors.Where(s => s.IsVisible).ToList();
            if (visibleSectors.Count == 0) return;

            var wrap = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 4, 0, 2),
                Tag = "FTH_RANGE_SECTOR_LEGEND_PANEL"
            };

            foreach (var sec in visibleSectors)
            {
                Color mainColor = Colors.Cyan;
                try { mainColor = (Color)ColorConverter.ConvertFromString(sec.ColorHex); } catch { }
                Brush strokeBrush = new SolidColorBrush(mainColor);
                Brush fillBrush = new SolidColorBrush(Color.FromArgb(145, mainColor.R, mainColor.G, mainColor.B));

                var itemPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 2, 14, 2),
                    VerticalAlignment = VerticalAlignment.Center
                };

                var iconCanvas = new Canvas
                {
                    Width = 16,
                    Height = 16,
                    Margin = new Thickness(0, 0, 5, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };

                var sliceIcon = new Path
                {
                    Fill = fillBrush,
                    Stroke = strokeBrush,
                    StrokeThickness = 1.2,
                    Data = BuildSectorOrCircleGeometry(8.0, 8.0, 7.0, sec.AzimuthDeg, sec.SpreadAngleDeg, isClosedSlice: true)
                };
                iconCanvas.Children.Add(sliceIcon);
                itemPanel.Children.Add(iconCanvas);

                double effR = sec.GetEffectiveRadiusMeters();
                string rangeInfo = Math.Abs(sec.ElevationDeltaMeters) >= 0.5
                    ? $"{sec.BaseRadiusMeters:0}m ➔ {effR:0}m (ΔH:{sec.ElevationDeltaMeters:+0.#;-0.#}m)"
                    : $"{sec.BaseRadiusMeters:0}m";
                string shapeInfo = sec.SpreadAngleDeg >= 359.5
                    ? "Okrąg 360°"
                    : $"Sektor {sec.SpreadAngleDeg:0}° (Az:{sec.AzimuthDeg:0}°)";

                var lbl = new TextBlock
                {
                    Text = $"{sec.Name}: {rangeInfo} [{shapeInfo}]",
                    Foreground = Brushes.White,
                    FontSize = 9.0,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                };
                itemPanel.Children.Add(lbl);
                wrap.Children.Add(itemPanel);
            }

            legStack.Children.Add(wrap);
        }

        // ====================================================================
        // EKSPORT SEKTOROW OGNIA ("PIZZA" + dH) DO .KMZ / .KML DLA ATAK
        // ====================================================================
        public static string InjectRangeSectorsIntoKmlDocument(string kmlContent)
        {
            if (string.IsNullOrEmpty(kmlContent)) return kmlContent;
            if (kmlContent.Contains("<name>Sektory Ognia i Zasięg Replik</name>"))
                return kmlContent;

            string fragment = BuildWeaponRangeSectorsKmlFragment();
            if (string.IsNullOrEmpty(fragment))
                return kmlContent;

            int docCloseIdx = kmlContent.LastIndexOf("</Document>", StringComparison.OrdinalIgnoreCase);
            if (docCloseIdx >= 0)
            {
                return kmlContent.Substring(0, docCloseIdx) + fragment + kmlContent.Substring(docCloseIdx);
            }
            return kmlContent;
        }

        public static string BuildWeaponRangeSectorsKmlFragment()
        {
            var visibleSectors = _weaponRangeSectors.Where(s => s.IsVisible).ToList();
            if (visibleSectors.Count == 0) return string.Empty;

            double centerLat = __LAT_FIELD__;
            double centerLon = __LON_FIELD__;
            double latRad = centerLat * Math.PI / 180.0;
            double mPerDegLat = 111132.92 - 559.82 * Math.Cos(2.0 * latRad) + 1.175 * Math.Cos(4.0 * latRad);
            double mPerDegLon = Math.Max(1000.0, 111412.84 * Math.Cos(latRad) - 93.5 * Math.Cos(3.0 * latRad));

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("    <Folder>");
            sb.AppendLine("      <name>Sektory Ognia i Zasięg Replik</name>");

            foreach (var sec in visibleSectors)
            {
                Color c = Colors.Cyan;
                try { c = (Color)ColorConverter.ConvertFromString(sec.ColorHex); } catch { }

                byte fillAlpha = (byte)Math.Clamp((int)Math.Round(sec.FillOpacity * 255.0), 30, 210);
                // Format KML to aabbggrr (Alpha, Blue, Green, Red)
                string kmlLineColor = string.Format(CultureInfo.InvariantCulture, "ff{0:x2}{1:x2}{2:x2}", c.B, c.G, c.R);
                string kmlFillColor = string.Format(CultureInfo.InvariantCulture, "{0:x2}{1:x2}{2:x2}{3:x2}", fillAlpha, c.B, c.G, c.R);
                string styleId = "fth_sector_" + sec.Id;
                string baseArcStyleId = "fth_sector_base_" + sec.Id;

                sb.AppendLine($"      <Style id=\"{styleId}\">");
                sb.AppendLine("        <LineStyle>");
                sb.AppendLine($"          <color>{kmlLineColor}</color>");
                sb.AppendLine("          <width>2.5</width>");
                sb.AppendLine("        </LineStyle>");
                sb.AppendLine("        <PolyStyle>");
                sb.AppendLine($"          <color>{kmlFillColor}</color>");
                sb.AppendLine("          <fill>1</fill>");
                sb.AppendLine("          <outline>1</outline>");
                sb.AppendLine("        </PolyStyle>");
                sb.AppendLine("      </Style>");

                double shooterLat = centerLat + (sec.OffsetNorthMeters / mPerDegLat);
                double shooterLon = centerLon + (sec.OffsetEastMeters / mPerDegLon);
                double effR = sec.GetEffectiveRadiusMeters();
                bool isFullCircle = sec.SpreadAngleDeg >= 359.5;

                // 1. Wielokat (Polygon) zasiegu efektywnego (pelne kolo lub wycinek "Kawalek Pizzy")
                var coordsSb = new System.Text.StringBuilder();
                if (isFullCircle)
                {
                    int steps = 64;
                    for (int i = 0; i <= steps; i++)
                    {
                        double angRad = (i * 360.0 / steps) * Math.PI / 180.0;
                        double pEastM = sec.OffsetEastMeters + effR * Math.Sin(angRad);
                        double pNorthM = sec.OffsetNorthMeters + effR * Math.Cos(angRad);
                        double pLon = centerLon + (pEastM / mPerDegLon);
                        double pLat = centerLat + (pNorthM / mPerDegLat);
                        coordsSb.AppendFormat(CultureInfo.InvariantCulture, "{0:F6},{1:F6},0 ", pLon, pLat);
                    }
                }
                else
                {
                    coordsSb.AppendFormat(CultureInfo.InvariantCulture, "{0:F6},{1:F6},0 ", shooterLon, shooterLat);

                    double halfSpread = sec.SpreadAngleDeg / 2.0;
                    double startDeg = sec.AzimuthDeg - halfSpread;
                    int steps = 36;
                    for (int i = 0; i <= steps; i++)
                    {
                        double deg = startDeg + (sec.SpreadAngleDeg * i) / steps;
                        double angRad = deg * Math.PI / 180.0;
                        double pEastM = sec.OffsetEastMeters + effR * Math.Sin(angRad);
                        double pNorthM = sec.OffsetNorthMeters + effR * Math.Cos(angRad);
                        double pLon = centerLon + (pEastM / mPerDegLon);
                        double pLat = centerLat + (pNorthM / mPerDegLat);
                        coordsSb.AppendFormat(CultureInfo.InvariantCulture, "{0:F6},{1:F6},0 ", pLon, pLat);
                    }

                    coordsSb.AppendFormat(CultureInfo.InvariantCulture, "{0:F6},{1:F6},0", shooterLon, shooterLat);
                }

                string dhSuffix = Math.Abs(sec.ElevationDeltaMeters) >= 0.5
                    ? $" -> {effR:0}m (dH:{sec.ElevationDeltaMeters:+0.#;-0.#}m)"
                    : "";
                string polyTitle = EscapeKmlXml($"{sec.Name} [{sec.BaseRadiusMeters:0}m{dhSuffix}]");
                string polyDesc = EscapeKmlXml(
                    $"Stanowisko: {sec.Name} | Klasa: {sec.WeaponPreset} | Zasięg płaski: {sec.BaseRadiusMeters:0} m | " +
                    $"Zasięg efektywny: {effR:0.0} m | Przewaga wysokości dH: {sec.ElevationDeltaMeters:+0.#;-0.#;0} m | " +
                    $"Azymut: {sec.AzimuthDeg:0} st. | Kąt sektora: {sec.SpreadAngleDeg:0} st.");

                sb.AppendLine("      <Placemark>");
                sb.AppendLine($"        <name>{polyTitle}</name>");
                sb.AppendLine($"        <description>{polyDesc}</description>");
                sb.AppendLine($"        <styleUrl>#{styleId}</styleUrl>");
                sb.AppendLine("        <Polygon>");
                sb.AppendLine("          <tessellate>1</tessellate>");
                sb.AppendLine("          <altitudeMode>clampToGround</altitudeMode>");
                sb.AppendLine("          <outerBoundaryIs>");
                sb.AppendLine("            <LinearRing>");
                sb.AppendLine($"              <coordinates>{coordsSb.ToString().Trim()}</coordinates>");
                sb.AppendLine("            </LinearRing>");
                sb.AppendLine("          </outerBoundaryIs>");
                sb.AppendLine("        </Polygon>");
                sb.AppendLine("      </Placemark>");

                // 2. Luk zasiegu plaskiego (BaseRadiusMeters), jezeli wystepuje przewaga wysokosci (dH != 0)
                if (Math.Abs(sec.ElevationDeltaMeters) >= 0.5)
                {
                    sb.AppendLine($"      <Style id=\"{baseArcStyleId}\">");
                    sb.AppendLine("        <LineStyle>");
                    sb.AppendLine("          <color>dcffffff</color>");
                    sb.AppendLine("          <width>1.8</width>");
                    sb.AppendLine("        </LineStyle>");
                    sb.AppendLine("      </Style>");

                    var baseArcSb = new System.Text.StringBuilder();
                    double halfSpread = isFullCircle ? 180.0 : (sec.SpreadAngleDeg / 2.0);
                    double startDeg = sec.AzimuthDeg - halfSpread;
                    double totalSweep = isFullCircle ? 360.0 : sec.SpreadAngleDeg;
                    int steps = 36;
                    for (int i = 0; i <= steps; i++)
                    {
                        double deg = startDeg + (totalSweep * i) / steps;
                        double angRad = deg * Math.PI / 180.0;
                        double pEastM = sec.OffsetEastMeters + sec.BaseRadiusMeters * Math.Sin(angRad);
                        double pNorthM = sec.OffsetNorthMeters + sec.BaseRadiusMeters * Math.Cos(angRad);
                        double pLon = centerLon + (pEastM / mPerDegLon);
                        double pLat = centerLat + (pNorthM / mPerDegLat);
                        baseArcSb.AppendFormat(CultureInfo.InvariantCulture, "{0:F6},{1:F6},0 ", pLon, pLat);
                    }

                    sb.AppendLine("      <Placemark>");
                    sb.AppendLine($"        <name>{EscapeKmlXml(sec.Name + " (Zasięg płaski " + sec.BaseRadiusMeters.ToString("0", CultureInfo.InvariantCulture) + "m)")}</name>");
                    sb.AppendLine($"        <styleUrl>#{baseArcStyleId}</styleUrl>");
                    sb.AppendLine("        <LineString>");
                    sb.AppendLine("          <tessellate>1</tessellate>");
                    sb.AppendLine($"          <coordinates>{baseArcSb.ToString().Trim()}</coordinates>");
                    sb.AppendLine("        </LineString>");
                    sb.AppendLine("      </Placemark>");
                }

                // 3. Punkt centralny stanowiska ogniowego dla ATAK
                sb.AppendLine("      <Placemark>");
                sb.AppendLine($"        <name>{EscapeKmlXml("Stanowisko: " + sec.Name)}</name>");
                sb.AppendLine($"        <description>{polyDesc}</description>");
                sb.AppendLine($"        <styleUrl>#{styleId}</styleUrl>");
                sb.AppendLine("        <Point>");
                sb.AppendLine($"          <coordinates>{shooterLon.ToString("F6", CultureInfo.InvariantCulture)},{shooterLat.ToString("F6", CultureInfo.InvariantCulture)},0</coordinates>");
                sb.AppendLine("        </Point>");
                sb.AppendLine("      </Placemark>");
            }

            sb.AppendLine("    </Folder>");
            return sb.ToString();
        }

        private static void ExportStandaloneRangeSectorsKmzDialog()
        {
            var visibleSectors = _weaponRangeSectors.Where(s => s.IsVisible).ToList();
            if (visibleSectors.Count == 0)
            {
                MessageBox.Show("Brak aktywnych sektorów ognia na mapie. Dodaj najpierw przynajmniej jeden sektor.", "Eksport KMZ dla ATAK", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Eksportuj Sektory Ognia do pliku .KMZ dla ATAK",
                Filter = "Paczka mapowa ATAK KMZ (*.kmz)|*.kmz|Plik KML (*.kml)|*.kml",
                FileName = "Sektory_Ognia_ATAK.kmz"
            };

            if (dlg.ShowDialog() != true) return;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<kml xmlns=\"http://www.opengis.net/kml/2.2\">");
            sb.AppendLine("  <Document>");
            sb.AppendLine("    <name>Sektory Ognia i Zasięg Replik (Field TAK Hub)</name>");
            sb.Append(BuildWeaponRangeSectorsKmlFragment());
            sb.AppendLine("  </Document>");
            sb.AppendLine("</kml>");

            string kmlText = sb.ToString();
            byte[] kmlBytes = System.Text.Encoding.UTF8.GetBytes(kmlText);

            if (dlg.FileName.EndsWith(".kml", StringComparison.OrdinalIgnoreCase))
            {
                System.IO.File.WriteAllText(dlg.FileName, kmlText, new System.Text.UTF8Encoding(false));
            }
            else
            {
                using (var fs = new System.IO.FileStream(dlg.FileName, System.IO.FileMode.Create, System.IO.FileAccess.Write))
                using (var zos = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create))
                {
                    var entry = zos.CreateEntry("doc.kml", System.IO.Compression.CompressionLevel.Optimal);
                    using (var es = entry.Open())
                    {
                        es.Write(kmlBytes, 0, kmlBytes.Length);
                    }
                }
            }
        }

        private static string EscapeKmlXml(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("&", "&amp;")
                    .Replace("<", "&lt;")
                    .Replace(">", "&gt;")
                    .Replace("\"", "&quot;")
                    .Replace("'", "&apos;");
        }
//===END_EXTRA_LEGEND_KMZ_CS===