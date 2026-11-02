@echo off
setlocal DisableDelayedExpansion
cd /d "%~dp0"

set "BAT_FILE=%~f0"

echo ============================================================================
echo [1/3] Wyszukiwanie projektu, aplikowanie poprawek UI i odtwarzanie Workflow...
echo ============================================================================
powershell -NoProfile -ExecutionPolicy Bypass -Command "Add-Type -TypeDefinition ([System.Text.RegularExpressions.Regex]::Split([System.IO.File]::ReadAllText([System.Environment]::GetEnvironmentVariable('BAT_FILE'), [System.Text.Encoding]::UTF8), '//===' + 'AIO_GITHUB_ENGINE_CS===')[1]); [FthAllInOneGitHubEngine]::Run()"

if errorlevel 1 (
    echo.
    echo [BLAD] Wystapil blad podczas przygotowywania plikow lub wysylki na GitHub!
    pause
    exit /b 1
)

echo.
echo ============================================================================
echo [SUKCES] Kod oraz plik workflow zostaly wyslane na GitHub (QbcolPL/TEST)!
echo Kompilacja na GitHub Actions zostala uruchomiona.
echo ============================================================================
pause
exit /b 0

//===AIO_GITHUB_ENGINE_CS===
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public static class FthAllInOneGitHubEngine
{
    private const string RepoRemoteUrl = "https://github.com/QbcolPL/TEST.git";
    private const string RepoActionsUrl = "https://github.com/QbcolPL/TEST/actions";

    public static void Run()
    {
        string currentDir = Directory.GetCurrentDirectory();
        string targetDir = ResolveTargetProjectDirectory(currentDir);
        Console.WriteLine("[OK] Katalog roboczy projektu: " + targetDir);

        UTF8Encoding utf8NoBom = new UTF8Encoding(false);

        // 0. Usun ewentualne pliki tymczasowe z katalogu glownego
        foreach (string gf in new string[] { "(f.Name", "Encoding.UTF8.GetBytes((s", "Label", "Name" })
        {
            string gp = Path.Combine(targetDir, gf);
            if (File.Exists(gp))
            {
                try { File.Delete(gp); } catch { }
            }
        }

        // 1. Zawsze odtworz czysty, poprawny plik .github/workflows/windows-builder.yml (.NET 10.0)
        EnsureCleanGitHubActionsWorkflows(targetDir, utf8NoBom);

        // 2. Zaaplikuj wszystkie poprawki UI i logiki Generatora Paczek .ftak
        ApplyAllPackageGeneratorAndUiFixes(targetDir, utf8NoBom);

        // 3. Zapisz unikalny znacznik wyzwalajacy, aby kazde uruchomienie skryptu tworzylo nowy commit dla GitHub Actions
        string triggerPath = Path.Combine(targetDir, ".fth_build_trigger.txt");
        string triggerContent = "Field TAK Hub Auto-Build Trigger UTC: " + DateTime.UtcNow.ToString("O") + " | RunId: " + Guid.NewGuid().ToString("N") + "\n";
        File.WriteAllText(triggerPath, triggerContent, utf8NoBom);

        // 4. Zainicjalizuj repozytorium Git (jesli brak .git), podepnij remote origin i wypchnij na GitHub Actions
        PushAndTriggerGitHubActions(targetDir);
    }

    private static string ResolveTargetProjectDirectory(string baseDir)
    {
        string csprojRel = @"apps\builder\FieldTakHub.Builder\FieldTakHub.Builder.csproj";

        if (File.Exists(Path.Combine(baseDir, csprojRel)))
            return baseDir;

        string subTestMain = Path.Combine(baseDir, "TEST-main");
        if (File.Exists(Path.Combine(subTestMain, csprojRel)))
            return subTestMain;

        string subGotowe = Path.Combine(baseDir, "TEST-main-Gotowe");
        if (File.Exists(Path.Combine(subGotowe, csprojRel)))
            return subGotowe;

        string parentDir = Path.GetDirectoryName(baseDir);
        if (!string.IsNullOrEmpty(parentDir))
        {
            if (File.Exists(Path.Combine(parentDir, csprojRel)))
                return parentDir;
            string parentTestMain = Path.Combine(parentDir, "TEST-main");
            if (File.Exists(Path.Combine(parentTestMain, csprojRel)))
                return parentTestMain;
        }

        throw new Exception("Nie znaleziono projektu FieldTakHub.Builder w katalogu: " + baseDir);
    }

    private static void EnsureCleanGitHubActionsWorkflows(string targetDir, UTF8Encoding utf8NoBom)
    {
        string workflowsDir = Path.Combine(targetDir, ".github", "workflows");
        Directory.CreateDirectory(workflowsDir);

        string windowsBuilderYml = string.Join("\n", new string[]
        {
            "name: Windows Builder",
            "on:",
            "  workflow_dispatch:",
            "  push:",
            "    branches: [main, master, 'release/**']",
            "  pull_request:",
            "jobs:",
            "  build:",
            "    runs-on: windows-latest",
            "    steps:",
            "      - uses: actions/checkout@v4",
            "      - uses: actions/setup-python@v5",
            "        with: { python-version: '3.12' }",
            "      - run: python tests/source_gate.py",
            "      - uses: actions/setup-dotnet@v4",
            "        with:",
            "          dotnet-version: '10.0.x'",
            "      - run: dotnet restore apps/builder/FieldTakHub.Builder.Tests/FieldTakHub.Builder.Tests.csproj",
            "      - run: dotnet test apps/builder/FieldTakHub.Builder.Tests/FieldTakHub.Builder.Tests.csproj -c Release --no-restore",
            "      - run: dotnet publish apps/builder/FieldTakHub.Builder/FieldTakHub.Builder.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=false -p:PublishTrimmed=false -p:PublishReadyToRun=false -o artifacts/windows",
            "      - uses: actions/upload-artifact@v4",
            "        with:",
            "          name: FieldTakHub-Builder-win-x64",
            "          path: artifacts/windows",
            ""
        });

        File.WriteAllText(Path.Combine(workflowsDir, "windows-builder.yml"), windowsBuilderYml, utf8NoBom);
        Console.WriteLine("[OK] Zapisano czysty workflow .github/workflows/windows-builder.yml (.NET 10.0).");
    }

    private static void ApplyAllPackageGeneratorAndUiFixes(string targetDir, UTF8Encoding utf8NoBom)
    {
        string builderDir = Path.Combine(targetDir, @"apps\builder\FieldTakHub.Builder");
        string servicesDir = Path.Combine(builderDir, "Services");

        // -------------------------------------------------------------------------
        // A. MainWindow.xaml:
        //    - Usuniecie luki 340px po lewej (ColumnDefinition Width="0")
        //    - Usuniecie paska gornego (RowDefinition Height="0", Visibility="Collapsed")
        //    - Kompaktowa wysokosc 6 kafelkow (MinHeight="110")
        //    - Automatyczne rozszerzanie pol CloudPackageBox, CloudUrlBox, UrlText (MinHeight="36" Height="Auto" MaxHeight="140")
        // -------------------------------------------------------------------------
        string xamlPath = Path.Combine(builderDir, "MainWindow.xaml");
        if (File.Exists(xamlPath))
        {
            string xaml = File.ReadAllText(xamlPath, Encoding.UTF8);

            xaml = Regex.Replace(
                xaml,
                @"(<Grid\s+x:Name=""AppLayout""[\s\S]*?<Grid\.ColumnDefinitions>\s*)<ColumnDefinition\s+Width=""[0-9]+""\s*/>",
                "${1}<ColumnDefinition Width=\"0\"/>");

            xaml = Regex.Replace(
                xaml,
                @"(<Grid\s+x:Name=""AppLayout""[\s\S]*?<Grid\.RowDefinitions>\s*)<RowDefinition\s+Height=""[0-9]+""\s*/>",
                "${1}<RowDefinition Height=\"0\"/>");

            xaml = xaml.Replace(
                @"<Border Grid.RowSpan=""2"" Grid.Column=""0"" Background=""{StaticResource Sidebar}"" BorderBrush=""#123638"" BorderThickness=""0,0,1,0"">",
                @"<Border Grid.RowSpan=""2"" Grid.Column=""0"" Width=""0"" Visibility=""Collapsed"" Background=""{StaticResource Sidebar}"" BorderBrush=""Transparent"" BorderThickness=""0"">");

            xaml = xaml.Replace(
                @"<Border Grid.Column=""1"" Grid.ColumnSpan=""2"" Grid.Row=""0"" Background=""{StaticResource Topbar}"" BorderBrush=""#123638"" BorderThickness=""0,0,0,1"">",
                @"<Border Grid.Column=""1"" Grid.ColumnSpan=""2"" Grid.Row=""0"" Height=""0"" Visibility=""Collapsed"" Background=""Transparent"" BorderBrush=""Transparent"" BorderThickness=""0"">");

            xaml = Regex.Replace(
                xaml,
                @"(<Border\s+Grid\.Row=""[01]""\s+Grid\.Column=""[012]""\s+Style=""\{StaticResource Card\}""[^>]*?)MinHeight=""560""",
                "${1}MinHeight=\"110\"");

            xaml = xaml.Replace(
                @"<TextBox x:Name=""CloudPackageBox"" TextWrapping=""Wrap"" MinHeight=""560""/>",
                @"<TextBox x:Name=""CloudPackageBox"" TextWrapping=""Wrap"" MinHeight=""36"" Height=""Auto"" MaxHeight=""140"" VerticalScrollBarVisibility=""Auto""/>");

            xaml = xaml.Replace(
                @"<TextBox x:Name=""CloudUrlBox"" TextWrapping=""Wrap"" MinHeight=""560""/>",
                @"<TextBox x:Name=""CloudUrlBox"" TextWrapping=""Wrap"" MinHeight=""36"" Height=""Auto"" MaxHeight=""140"" VerticalScrollBarVisibility=""Auto""/>");

            xaml = xaml.Replace(
                @"<TextBox x:Name=""UrlText"" IsReadOnly=""True"" TextWrapping=""Wrap"" MinHeight=""560"" Margin=""0,0,0,7""/>",
                @"<TextBox x:Name=""UrlText"" IsReadOnly=""True"" TextWrapping=""Wrap"" MinHeight=""36"" Height=""Auto"" MaxHeight=""120"" VerticalScrollBarVisibility=""Auto"" Margin=""0,0,0,7""/>");

            xaml = xaml.Replace(
                @"<TextBox x:Name=""MeshtasticUrlBox"" MinHeight=""560"" TextWrapping=""Wrap""",
                @"<TextBox x:Name=""MeshtasticUrlBox"" MinHeight=""36"" Height=""Auto"" MaxHeight=""120"" TextWrapping=""Wrap""");

            xaml = xaml.Replace(
                @"<TextBox x:Name=""EnrollmentUriBox"" MinHeight=""560"" TextWrapping=""Wrap""",
                @"<TextBox x:Name=""EnrollmentUriBox"" MinHeight=""44"" Height=""Auto"" MaxHeight=""120"" TextWrapping=""Wrap""");

            xaml = xaml.Replace(@"MinWidth=""900"" Height=""48"" Click=""Build_Click""", @"MinWidth=""220"" Height=""48"" Click=""Build_Click""");

            File.WriteAllText(xamlPath, xaml, utf8NoBom);
            Console.WriteLine("[OK] Zaktualizowano MainWindow.xaml.");
        }

        // -------------------------------------------------------------------------
        // B. TacticalMapGeneratorView.cs:
        //    Usuniecie 46px paska gornego i cyjanowej linii (masterGrid.RowDefinitions[0] = 0, navBorder Height=0, Opacity=0)
        // -------------------------------------------------------------------------
        string mapGenPath = Path.Combine(servicesDir, "TacticalMapGeneratorView.cs");
        if (File.Exists(mapGenPath))
        {
            string mg = File.ReadAllText(mapGenPath, Encoding.UTF8);

            mg = mg.Replace(
                "TacticalConfigPanel.ConsumePendingMapStateIfAny(); BuilderUiSimplifier.Attach(win);",
                "TacticalConfigPanel.ConsumePendingMapStateIfAny();");

            mg = mg.Replace(
                "masterGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });",
                "masterGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0) });");

            mg = mg.Replace(
                "var navBorder = new Border { Background = TacticalConfigPanel.BrushInputBg, BorderBrush = TacticalConfigPanel.BrushAccent, BorderThickness = new Thickness(0, 0, 0, 1.5), Padding = new Thickness(16, 0, 16, 0) };",
                "var navBorder = new Border { Height = 0, MaxHeight = 0, Opacity = 0, IsHitTestVisible = false, Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0) };");

            if (!mg.Contains("win.Content = masterGrid;\r\n                    BuilderUiSimplifier.Attach(win);") &&
                !mg.Contains("win.Content = masterGrid;\n                    BuilderUiSimplifier.Attach(win);"))
            {
                mg = mg.Replace(
                    "win.Content = masterGrid;",
                    "win.Content = masterGrid;\r\n                    BuilderUiSimplifier.Attach(win);");
            }

            File.WriteAllText(mapGenPath, mg, utf8NoBom);
            Console.WriteLine("[OK] Usunieto gorny pasek startowy i linie cyjanowa w TacticalMapGeneratorView.cs.");
        }

        // -------------------------------------------------------------------------
        // C. BuilderUiSimplifier.cs:
        //    Karta "Generator paczek .ftak" z dokladnie 3 przyciskami:
        //    1. Dystrybucja LAN/WIFI Hotspot
        //    2. Wspolny folder Stron
        //    3. Buduj Wszystkie Strony
        // -------------------------------------------------------------------------
        string simpPath = Path.Combine(servicesDir, "BuilderUiSimplifier.cs");
        if (File.Exists(simpPath))
        {
            string simp = File.ReadAllText(simpPath, Encoding.UTF8);
            int injStart = simp.IndexOf("private static void InjectOrUpdateActiveFactionAuditBar", StringComparison.Ordinal);
            int syncEnd = simp.IndexOf("private static bool IsAncestorOf", StringComparison.Ordinal);

            if (injStart > 0 && syncEnd > injStart)
            {
                string newAuditBarMethods = @"private static void InjectOrUpdateActiveFactionAuditBar(Window win, FrameworkElement factionGeneratorRoot)
        {
            Panel targetParent = factionGeneratorRoot as Panel;
            if (targetParent == null && factionGeneratorRoot is Border b && b.Child is Panel p)
                targetParent = p;
            if (targetParent == null)
                targetParent = VisualTreeHelper.GetParent(factionGeneratorRoot) as Panel;
            if (targetParent == null) return;

            var configuredFactions = TacticalConfigPanel.GetConfiguredFactions();
            if (_activeFactionHubBanner != null && targetParent.Children.Contains(_activeFactionHubBanner))
            {
                SyncActiveFactionComboItems(configuredFactions);
                return;
            }

            _activeFactionHubBanner = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(11, 27, 34)),
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 0, 12)
            };

            var stack = new StackPanel();

            _bannerTitleBlock = new TextBlock
            {
                Foreground = TacticalConfigPanel.BrushAccent,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 4)
            };
            stack.Children.Add(_bannerTitleBlock);

            _bannerDescBlock = new TextBlock
            {
                Foreground = TacticalConfigPanel.BrushMutedText,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            stack.Children.Add(_bannerDescBlock);

            var controlsRow = new Grid();
            controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _btnShowActiveQr = new Button
            {
                Height = 36,
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(Color.FromRgb(22, 54, 66)),
                Foreground = Brushes.White,
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(1.4),
                FontWeight = FontWeights.Bold,
                FontSize = 11.5,
                Cursor = Cursors.Hand
            };
            _btnShowActiveQr.Click += async (_, __) =>
            {
                await CleanFactionPackageEngine.BuildIsolatedFactionPackagesAsync(
                    win, null, showQrModalAfter: true,
                    msg => { if (_auditStatusLabel != null) _auditStatusLabel.Text = msg; });
            };
            Grid.SetColumn(_btnShowActiveQr, 0);
            controlsRow.Children.Add(_btnShowActiveQr);

            _btnOpenSourceDir = new Button
            {
                Height = 36,
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(Color.FromRgb(38, 34, 16)),
                Foreground = new SolidColorBrush(Color.FromRgb(250, 208, 44)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(250, 208, 44)),
                BorderThickness = new Thickness(1.4),
                FontWeight = FontWeights.Bold,
                FontSize = 11.5,
                Cursor = Cursors.Hand
            };
            _btnOpenSourceDir.Click += (_, __) =>
            {
                BuilderCoreSidebarAndProjectEngine.EnsureUnifiedSourceDirectoryStructure(win);
                string src = TacticalConfigPanel.GetPrimarySourceDirectory(win);
                TacticalConfigPanel.TryOpenFolderInExplorer(src);
            };
            Grid.SetColumn(_btnOpenSourceDir, 1);
            controlsRow.Children.Add(_btnOpenSourceDir);

            _btnBuildAllFactions = new Button
            {
                Height = 36,
                Margin = new Thickness(0),
                Background = TacticalConfigPanel.BrushButtonAccent,
                Foreground = TacticalConfigPanel.BrushDarkFg,
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(1.4),
                FontWeight = FontWeights.Bold,
                FontSize = 11.5,
                Cursor = Cursors.Hand
            };
            _btnBuildAllFactions.Click += async (_, __) =>
            {
                await CleanFactionPackageEngine.BuildIsolatedFactionPackagesAsync(
                    win, null, showQrModalAfter: false,
                    msg => { if (_auditStatusLabel != null) _auditStatusLabel.Text = msg; });
            };
            Grid.SetColumn(_btnBuildAllFactions, 2);
            controlsRow.Children.Add(_btnBuildAllFactions);

            stack.Children.Add(controlsRow);

            _auditStatusLabel = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(46, 204, 113)),
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 8, 0, 0)
            };
            stack.Children.Add(_auditStatusLabel);

            RebuildActiveFactionBannerTexts();

            _activeFactionHubBanner.Child = stack;
            targetParent.Children.Insert(0, _activeFactionHubBanner);
        }

        private static void RebuildActiveFactionBannerTexts()
        {
            if (_bannerTitleBlock != null)
            {
                _bannerTitleBlock.Text = BuilderI18n.T(
                    ""\uD83C\uDFAF Generator paczek .ftak"",
                    ""\uD83C\uDFAF .ftak Package Generator"");
            }
            if (_bannerDescBlock != null)
            {
                _bannerDescBlock.Text = BuilderI18n.T(
                    ""Ka\u017Cda strona konfliktu zapisywana jest do w\u0142asnego katalogu out\\<STRONA>\\. Wsp\u00F3lne pliki instalacyjne .apk (ATAK, Meshtastic, plugins) oraz mapy (maps) pobierane s\u0105 ze Wsp\u00F3lnego folderu Stron:"",
                    ""Each faction outputs to its own clean directory out\\<FACTION>\\. Shared .apk files (ATAK, Meshtastic, plugins) and maps are loaded from the Shared Factions Folder:"");
            }
            if (_btnShowActiveQr != null)
                _btnShowActiveQr.Content = BuilderI18n.T(""\uD83D\uDCE1 Dystrybucja LAN/WIFI Hotspot"", ""\uD83D\uDCE1 LAN/WIFI Hotspot Distribution"");
            if (_btnOpenSourceDir != null)
                _btnOpenSourceDir.Content = BuilderI18n.T(""\uD83D\uDCC2 Wsp\u00F3lny folder Stron"", ""\uD83D\uDCC2 Shared Factions Folder"");
            if (_btnBuildAllFactions != null)
                _btnBuildAllFactions.Content = BuilderI18n.T(""\u26A1 Buduj Wszystkie Strony"", ""\u26A1 Build All Factions"");
            if (_auditStatusLabel != null && string.IsNullOrWhiteSpace(_auditStatusLabel.Text))
            {
                _auditStatusLabel.Text = BuilderI18n.T(
                    ""\u2713 Struktura gotowa: out\\<STRONA>\\ (paczki .ftak, Plakat QR, OPORD) + wsp\u00F3lny folder source\\ (ATAK, Meshtastic, plugins, maps)."",
                    ""\u2713 Structure ready: out\\<FACTION>\\ (.ftak, QR Poster, OPORD) + shared source\\ (ATAK, Meshtastic, plugins, maps)."");
            }
        }

        private static void SyncActiveFactionComboItems(List<(string Name, string Channel, string ColorHex, string Psk)> configured)
        {
            if (configured != null && configured.Count > 0 && string.IsNullOrWhiteSpace(_selectedActiveFactionName))
                _selectedActiveFactionName = configured[0].Name;
        }

        ";
                simp = simp.Substring(0, injStart) + newAuditBarMethods + simp.Substring(syncEnd);
                File.WriteAllText(simpPath, simp, utf8NoBom);
                Console.WriteLine("[OK] Zaktualizowano karte 'Generator paczek .ftak' (3 przyciski) w BuilderUiSimplifier.cs.");
            }
        }

        // -------------------------------------------------------------------------
        // D. TacticalConfigPanel.cs:
        //    - Naprawa nieskonczonej rekurencji ShowDialogWithSidebarSupport
        //    - Aktywny przycisk "Pliki Strony" i bezawaryjny "Plakat + QR"
        //    - Unikalne kolory RGB dla nowych stron konfliktu
        // -------------------------------------------------------------------------
        string tcpPath = Path.Combine(servicesDir, "TacticalConfigPanel.cs");
        if (File.Exists(tcpPath))
        {
            string tcp = File.ReadAllText(tcpPath, Encoding.UTF8);

            tcp = tcp.Replace(
                "return owner != null ? (ShowDialogWithSidebarSupport(dlg, owner)) : (ShowDialogWithSidebarSupport(dlg));",
                "return owner != null ? (dlg.ShowDialog(owner) == true) : (dlg.ShowDialog() == true);");

            tcp = Regex.Replace(
                tcp,
                @"string\s+hex\s*=\s*palette\[\(nextNum\s*-\s*1\)\s*%\s*palette\.Length\];",
                @"var usedHexes = new HashSet<string>(_factions.Select(f => (f.ColorHex ?? """").Trim().ToUpperInvariant()), StringComparer.OrdinalIgnoreCase);
                string[] fullPalette = { ""#2ECC71"", ""#F5B82E"", ""#9B59B6"", ""#FF9F43"", ""#1FE0C5"", ""#E84393"", ""#A3CB38"", ""#00CEC9"", ""#54A0FF"", ""#FF6B6B"", ""#5F27CD"", ""#10AC84"" };
                string hex = fullPalette.FirstOrDefault(p => !usedHexes.Contains(p.ToUpperInvariant())) ?? $""#{(nextNum * 67) % 200 + 40:X2}{(nextNum * 113) % 200 + 40:X2}{(nextNum * 179) % 200 + 40:X2}"";");

            int bottomBtnsStart = tcp.IndexOf("var bottomBtnsGrid = new UniformGrid { Columns = 3 };", StringComparison.Ordinal);
            int bottomBtnsEnd = tcp.IndexOf("outerCard.Child = mainStack;", StringComparison.Ordinal);
            if (bottomBtnsStart > 0 && bottomBtnsEnd > bottomBtnsStart)
            {
                string newBottomBtnsCode = @"var bottomBtnsGrid = new UniformGrid { Columns = 3 };
            var btnFolders = new Button
            {
                Content = ""\uD83D\uDCC2 Pliki Strony"",
                Height = 30,
                Margin = new Thickness(0, 0, 4, 0),
                Background = new SolidColorBrush(Color.FromRgb(18, 46, 56)),
                Foreground = Brushes.White,
                BorderBrush = BrushAccent,
                BorderThickness = new Thickness(1.2),
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                IsEnabled = true,
                Cursor = Cursors.Hand
            };
            var btnPoster = new Button
            {
                Content = ""\uD83D\uDDA8\uFE0F Plakat + QR"",
                Height = 30,
                Margin = new Thickness(2, 0, 2, 0),
                Background = new SolidColorBrush(Color.FromRgb(22, 54, 66)),
                Foreground = Brushes.White,
                BorderBrush = BrushAccent,
                BorderThickness = new Thickness(1.2),
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                IsEnabled = true,
                Cursor = Cursors.Hand
            };
            var btnBuildAll = new Button
            {
                Content = ""\u26A1 Buduj strony (.ftak)"",
                Height = 30,
                Margin = new Thickness(4, 0, 0, 0),
                Background = BrushButtonAccent,
                Foreground = BrushDarkFg,
                BorderThickness = new Thickness(0),
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                IsEnabled = true,
                Cursor = Cursors.Hand
            };

            btnFolders.Click += async (_, __) =>
            {
                try
                {
                    BuilderCoreSidebarAndProjectEngine.EnsureUnifiedSourceDirectoryStructure(win);
                    string srcDir = GetPrimarySourceDirectory(win);
                    string parentDir = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(srcDir)) ?? AppDomain.CurrentDomain.BaseDirectory;
                    string safeFac = SanitizeFileName(fac.Name).ToUpperInvariant();
                    string facOutDir = Path.Combine(parentDir, ""out"", safeFac);
                    Directory.CreateDirectory(facOutDir);

                    var sides = _factions.Select(f => (SanitizeFileName(f.Name).ToLowerInvariant(), f.ColorHex)).ToList();
                    await Task.Run(() => { EnsureFactionFoldersInitialized(srcDir, sides); WriteConfigsToDirectory(srcDir, fac); });
                    await ForceSizeUpdateAsync(win);
                    SaveSessionStateBackground(win);

                    meshStatusTxt.Text = $""\u2713 Otwarto folder plik\u00F3w strony {fac.Name}: out\\{safeFac}\\"";
                    meshStatusTxt.Foreground = BrushAccent;
                    TryOpenFolderInExplorer(facOutDir);
                }
                catch { }
            };
            btnPoster.Click += async (_, __) =>
            {
                try
                {
                    btnPoster.IsEnabled = false;
                    await ExportSafezonePosterForFactionAsync(win, fac);
                }
                catch { }
                finally
                {
                    btnPoster.IsEnabled = true;
                }
            };
            btnBuildAll.Click += async (_, __) =>
            {
                try
                {
                    btnBuildAll.IsEnabled = false;
                    await CleanFactionPackageEngine.BuildIsolatedFactionPackagesAsync(win, fac.Name, showQrModalAfter: false);
                }
                catch { }
                finally
                {
                    btnBuildAll.IsEnabled = true;
                }
            };

            bottomBtnsGrid.Children.Add(btnFolders);
            bottomBtnsGrid.Children.Add(btnPoster);
            bottomBtnsGrid.Children.Add(btnBuildAll);
            mainStack.Children.Add(bottomBtnsGrid);

            ";
                tcp = tcp.Substring(0, bottomBtnsStart) + newBottomBtnsCode + tcp.Substring(bottomBtnsEnd);
            }

            int expPosterStart = tcp.IndexOf("private static async Task ExportSafezonePosterForFactionAsync", StringComparison.Ordinal);
            int renderQrStart = tcp.IndexOf("private static void RenderStandaloneQrPng", StringComparison.Ordinal);
            if (expPosterStart > 0 && renderQrStart > expPosterStart)
            {
                string safeExportPosterMethod = @"private static async Task ExportSafezonePosterForFactionAsync(Window win, FactionProfileModel fac)
        {
            try
            {
                SaveSessionStateBackground(win);
                BuilderCoreSidebarAndProjectEngine.EnsureUnifiedSourceDirectoryStructure(win);
                string sourceDir = GetPrimarySourceDirectory(win);
                string eventName = GetEventNameFromWindow(win);
                string parentDir = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(sourceDir)) ?? AppDomain.CurrentDomain.BaseDirectory;
                string cleanName = SanitizeFileName(fac.Name).ToUpperInvariant();
                string outDir = Path.Combine(parentDir, ""out"", cleanName);
                Directory.CreateDirectory(outDir);

                string posterOutPath = Path.Combine(outDir, $""Plakat_Safezone_A4_{eventName}_{cleanName}.png"");
                string ftakPath = Path.Combine(outDir, $""{eventName}_{cleanName}.ftak"");

                string sha256 = await Task.Run(() =>
                {
                    WriteConfigsToDirectory(sourceDir, fac);
                    BuildFilteredFactionFtakArchive(sourceDir, ftakPath, fac, eventName);
                    using var sha = SHA256.Create();
                    using var stream = File.OpenRead(ftakPath);
                    return Convert.ToHexString(sha.ComputeHash(stream));
                });

                string qrPayload = !string.IsNullOrWhiteSpace(fac.EnrollmentUri)
                    ? fac.EnrollmentUri
                    : $""ftak://provision?event={Uri.EscapeDataString(eventName)}&faction={Uri.EscapeDataString(cleanName)}&channel={Uri.EscapeDataString(fac.ChannelName)}&sha256={sha256.Substring(0, Math.Min(16, sha256.Length))}"";

                RenderStandaloneQrPng(Path.Combine(outDir, $""QR_{eventName}_{cleanName}.png""), qrPayload);
                RenderSafezoneA4PosterPng(posterOutPath, eventName, fac, qrPayload, sha256);

                if (File.Exists(posterOutPath))
                {
                    try { Process.Start(new ProcessStartInfo { FileName = posterOutPath, UseShellExecute = true }); } catch { }
                }
                TryOpenFolderInExplorer(outDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show(""B\u0142\u0105d generowania Plakatu + QR: "" + ex.Message, ""Field TAK Hub"", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        ";
                tcp = tcp.Substring(0, expPosterStart) + safeExportPosterMethod + tcp.Substring(renderQrStart);
            }

            File.WriteAllText(tcpPath, tcp, utf8NoBom);
            Console.WriteLine("[OK] Zaktualizowano TacticalConfigPanel.cs ('Pliki Strony', 'Plakat + QR', unikalne kolory RGB).");
        }

        // -------------------------------------------------------------------------
        // E. BuilderCoreSidebarAndProjectEngine.cs & MainWindow.xaml.cs:
        //    - Poszerzenie paska bocznego z 255 do 272 px (brak ucinania napisow)
        //    - Wylaczenie UpdateSidebarIconsAndCollapseButton (zachowanie oryginalnych ikon SVG po minimalizacji)
        //    - Oczyszczenie ApplyFactionAndPackageFixes z dublowania przyciskow Pliki Strony / Plakat + QR
        // -------------------------------------------------------------------------
        string corePath = Path.Combine(servicesDir, "BuilderCoreSidebarAndProjectEngine.cs");
        if (File.Exists(corePath))
        {
            string core = File.ReadAllText(corePath, Encoding.UTF8);

            // Szerokosc paska bocznego 272px w trybie rozwinietym i 52px w trybie zwinietym
            core = core.Replace("double targetWidth = _isSidebarCollapsed ? 42.0 : 255.0;", "double targetWidth = _isSidebarCollapsed ? 52.0 : 272.0;");
            core = core.Replace("_rootSidebarColumnDef = new ColumnDefinition { Width = new GridLength(255) };", "_rootSidebarColumnDef = new ColumnDefinition { Width = new GridLength(272) };");
            core = core.Replace("Tag = \"FTH_TACTICAL_SIDEBAR_V31\",\r\n                Width = 255,", "Tag = \"FTH_TACTICAL_SIDEBAR_V31\",\r\n                Width = 272,");
            core = core.Replace("Tag = \"FTH_TACTICAL_SIDEBAR_V31\",\n                Width = 255,", "Tag = \"FTH_TACTICAL_SIDEBAR_V31\",\n                Width = 272,");

            // Wylacz UpdateSidebarIconsAndCollapseButton(w), ktore zamienialo ikony SVG po minimalizacji na znak '<'
            core = core.Replace("UpdateSidebarIconsAndCollapseButton(w);", "// UpdateSidebarIconsAndCollapseButton(w);");

            // Dodaj podfoldery ATAK i Meshtastic we wspolnym katalogu
            if (!core.Contains(@"Directory.CreateDirectory(Path.Combine(sourceDir, ""ATAK""));"))
            {
                core = core.Replace(
                    @"Directory.CreateDirectory(Path.Combine(sourceDir, ""apk""));",
                    @"Directory.CreateDirectory(Path.Combine(sourceDir, ""ATAK""));
                Directory.CreateDirectory(Path.Combine(sourceDir, ""Meshtastic""));
                Directory.CreateDirectory(Path.Combine(sourceDir, ""apk""));");
            }

            if (!core.Contains(@"Directory.CreateDirectory(Path.Combine(wspolne, ""ATAK""));"))
            {
                core = core.Replace(
                    @"Directory.CreateDirectory(Path.Combine(wspolne, ""apk""));",
                    @"Directory.CreateDirectory(Path.Combine(wspolne, ""ATAK""));
            Directory.CreateDirectory(Path.Combine(wspolne, ""Meshtastic""));
            Directory.CreateDirectory(Path.Combine(wspolne, ""apk""));");
            }

            // Oczysc ApplyFactionAndPackageFixes, aby nie nadpisywal "Pliki Strony", "Plakat + QR" ani karty 3 przyciskow
            int afStart = core.IndexOf("public static void ApplyFactionAndPackageFixes(Window win)", StringComparison.Ordinal);
            int afEnd = core.IndexOf("private static void EnforceUniqueColorsAndNamesOnCards", StringComparison.Ordinal);
            if (afStart > 0 && afEnd > afStart)
            {
                string cleanApplyFactionMethod = @"public static void ApplyFactionAndPackageFixes(Window win)
        {
            var w = win ?? Application.Current?.MainWindow;
            if (w == null) return;

            EnsureSharedFolderStructure();
            var all = ScanAllWindowRoots(w);

            foreach (var tb in all.OfType<TextBlock>())
            {
                string txt = (tb.Text ?? """").Trim();
                if ((txt.Contains(""Wa\u017Cno\u015B\u0107 paczki"", StringComparison.OrdinalIgnoreCase) ||
                     txt.Contains(""Wa\u017Cno\u015B\u0107 kodu QR"", StringComparison.OrdinalIgnoreCase) ||
                     txt.Contains(""Expiration"", StringComparison.OrdinalIgnoreCase)) &&
                    !txt.Contains(""0 = bezterminowo"", StringComparison.OrdinalIgnoreCase))
                {
                    tb.Text = txt.TrimEnd(':') + "" (0 = bezterminowo):"";
                }
            }

            EnforceUniqueColorsAndNamesOnCards(all);
            RefreshCachedFactions(w, all);
        }

        ";
                core = core.Substring(0, afStart) + cleanApplyFactionMethod + core.Substring(afEnd);
            }

            File.WriteAllText(corePath, core, utf8NoBom);
            Console.WriteLine("[OK] Zaktualizowano BuilderCoreSidebarAndProjectEngine.cs.");
        }

        string mwCsPath = Path.Combine(builderDir, "MainWindow.xaml.cs");
        if (File.Exists(mwCsPath))
        {
            string mwCs = File.ReadAllText(mwCsPath, Encoding.UTF8);
            mwCs = mwCs.Replace("V34Step1ShellAndSidebarFixer.AttachToMainWindow(this);", "");
            File.WriteAllText(mwCsPath, mwCs, utf8NoBom);
        }
    }

    private static void PushAndTriggerGitHubActions(string targetDir)
    {
        Console.WriteLine();
        Console.WriteLine("============================================================================");
        Console.WriteLine("[2/3] Inicjalizacja repozytorium Git i wysylka na GitHub (QbcolPL/TEST)...");
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
        RunProcess(gitExe, "commit -m \"Field TAK Hub v3.4: UI layout fix, 3-button Generator paczek .ftak, Pliki Strony & Plakat+QR fix, unique RGB colors\" --allow-empty", targetDir, ignoreError: false);

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
//===AIO_GITHUB_ENGINE_CS===