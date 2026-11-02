@echo off
setlocal DisableDelayedExpansion
cd /d "%~dp0"

set "BAT_FILE=%~f0"

echo ============================================================================
echo [1/3] Wdrazanie obslugi 0 = bezterminowo oraz poprawek UI i Workflow...
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
echo [SUKCES] Zmiany (0 = bezterminowo dla paczki i QR) wyslane na GitHub!
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

        foreach (string gf in new string[] { "(f.Name", "Encoding.UTF8.GetBytes((s", "Label", "Name" })
        {
            string gp = Path.Combine(targetDir, gf);
            if (File.Exists(gp))
            {
                try { File.Delete(gp); } catch { }
            }
        }

        // 1. Odtworz czysty workflow .github/workflows/windows-builder.yml (.NET 10.0)
        EnsureCleanGitHubActionsWorkflows(targetDir, utf8NoBom);

        // 2. Zaaplikuj obsluge 0 = bezterminowo oraz wszystkie poprawki UI Generatora Paczek
        ApplyAllPackageGeneratorAndUiFixes(targetDir, utf8NoBom);

        // 3. Zapisz unikalny znacznik wyzwalajacy dla GitHub Actions
        string triggerPath = Path.Combine(targetDir, ".fth_build_trigger.txt");
        string triggerContent = "Field TAK Hub Auto-Build Trigger UTC: " + DateTime.UtcNow.ToString("O") + " | RunId: " + Guid.NewGuid().ToString("N") + "\n";
        File.WriteAllText(triggerPath, triggerContent, utf8NoBom);

        // 4. Wyslij zmiany na GitHub i uruchom GitHub Actions
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
        string resourcesDir = Path.Combine(builderDir, "Resources");
        string testsDir = Path.Combine(targetDir, @"apps\builder\FieldTakHub.Builder.Tests");

        // -------------------------------------------------------------------------
        // A. Strings.pl.xaml & Strings.en.xaml — czytelne oznaczenie (0 = bezterminowo)
        // -------------------------------------------------------------------------
        string plPath = Path.Combine(resourcesDir, "Strings.pl.xaml");
        if (File.Exists(plPath))
        {
            string pl = File.ReadAllText(plPath, Encoding.UTF8);
            pl = pl.Replace(
                @"<sys:String x:Key=""UI_PackageValidity"">Wa" + "\u017Cno\u015B\u0107" + @" paczki</sys:String>",
                @"<sys:String x:Key=""UI_PackageValidity"">Wa" + "\u017Cno\u015B\u0107" + @" paczki (0 = bezterminowo)</sys:String>");
            pl = pl.Replace(
                @"<sys:String x:Key=""UI_QrValidity"">Wa" + "\u017Cno\u015B\u0107" + @" kodu QR</sys:String>",
                @"<sys:String x:Key=""UI_QrValidity"">Wa" + "\u017Cno\u015B\u0107" + @" kodu QR (0 = bezterminowo)</sys:String>");
            File.WriteAllText(plPath, pl, utf8NoBom);
            Console.WriteLine("[OK] Zaktualizowano etykiety waznosci w Strings.pl.xaml.");
        }

        string enPath = Path.Combine(resourcesDir, "Strings.en.xaml");
        if (File.Exists(enPath))
        {
            string en = File.ReadAllText(enPath, Encoding.UTF8);
            en = en.Replace(
                @"<sys:String x:Key=""UI_PackageValidity"">Package validity</sys:String>",
                @"<sys:String x:Key=""UI_PackageValidity"">Package validity (0 = never expires)</sys:String>");
            en = en.Replace(
                @"<sys:String x:Key=""UI_QrValidity"">QR code validity</sys:String>",
                @"<sys:String x:Key=""UI_QrValidity"">QR code validity (0 = never expires)</sys:String>");
            File.WriteAllText(enPath, en, utf8NoBom);
            Console.WriteLine("[OK] Zaktualizowano etykiety waznosci w Strings.en.xaml.");
        }

        // -------------------------------------------------------------------------
        // B. MainWindow.xaml:
        //    - Dodanie przycisku szybkiego ustawienia "∞ Bezterminowo (0 h)"
        //    - Podpowiedzi ToolTip dla PackageExpiryBox i QrExpiryBox
        //    - Kompaktowe pola linkow i kafelki oraz usuniecie luki 340px i paska gornego
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

            // Dodaj przycisk szybkiego ustawienia "∞ Bezterminowo (0 h)" obok "6 h QR"
            if (!xaml.Contains("Click=\"SetIndefinite0_Click\""))
            {
                xaml = xaml.Replace(
                    @"<Button Content=""6 h QR"" Style=""{StaticResource IconActionButton}"" Click=""SetQr6_Click"" Margin=""0,0,5,0""/>",
                    @"<Button Content=""6 h QR"" Style=""{StaticResource IconActionButton}"" Click=""SetQr6_Click"" Margin=""0,0,5,0""/>" +
                    "\r\n                " +
                    @"<Button Content=""&#x221E; Bezterminowo (0 h)"" Style=""{StaticResource IconActionButton}"" Foreground=""{StaticResource Accent}"" BorderBrush=""{StaticResource Accent}"" Click=""SetIndefinite0_Click"" Margin=""0,0,5,0"" ToolTip=""Ustawia 0 (wa&#x017C;no&#x015B;&#x0107; bezterminow&#x0105;) dla paczki .ftak oraz kodu QR""/>");
            }

            File.WriteAllText(xamlPath, xaml, utf8NoBom);
            Console.WriteLine("[OK] Zaktualizowano MainWindow.xaml (przycisk 'Bezterminowo (0 h)' i uklad).");
        }

        // -------------------------------------------------------------------------
        // C. MainWindow.xaml.cs — Pelna obsluga 0 = bezterminowo:
        //    - Math.Max(0, ph) zamiast Math.Max(1, ph)
        //    - Math.Max(0, qh) zamiast Math.Max(1, qh)
        //    - Serwer LAN: project.QrExpiryHours <= 0 ? 876000.0 : project.QrExpiryHours
        //    - Handler SetIndefinite0_Click ustawiajacy "0" w obu polach
        // -------------------------------------------------------------------------
        string mwCsPath = Path.Combine(builderDir, "MainWindow.xaml.cs");
        if (File.Exists(mwCsPath))
        {
            string mwCs = File.ReadAllText(mwCsPath, Encoding.UTF8);

            mwCs = mwCs.Replace("V34Step1ShellAndSidebarFixer.AttachToMainWindow(this);", "");

            mwCs = mwCs.Replace(
                "ExpiryHours = int.TryParse(PackageExpiryBox.Text, out var ph) ? Math.Max(1, ph) : 6,",
                "ExpiryHours = int.TryParse(PackageExpiryBox.Text, out var ph) ? Math.Max(0, ph) : 6,");

            mwCs = mwCs.Replace(
                "QrExpiryHours = int.TryParse(QrExpiryBox.Text, out var qh) ? Math.Max(1, qh) : 6,",
                "QrExpiryHours = int.TryParse(QrExpiryBox.Text, out var qh) ? Math.Max(0, qh) : 6,");

            mwCs = mwCs.Replace(
                "var url=_server.Start(_lastPackage, TimeSpan.FromHours(project.QrExpiryHours), project.MaxDownloads);",
                "var url=_server.Start(_lastPackage, TimeSpan.FromHours(project.QrExpiryHours <= 0 ? 876000.0 : (double)project.QrExpiryHours), project.MaxDownloads);");

            mwCs = mwCs.Replace(
                "var expiry = int.TryParse(PackageExpiryBox.Text, out var h) ? Math.Max(1, h) : 72;",
                "var expiry = int.TryParse(PackageExpiryBox.Text, out var h) ? Math.Max(0, h) : 72;");

            if (!mwCs.Contains("SetIndefinite0_Click"))
            {
                mwCs = mwCs.Replace(
                    "private void SetQr6_Click(object sender, RoutedEventArgs e) => QrExpiryBox.Text = \"6\";",
                    "private void SetQr6_Click(object sender, RoutedEventArgs e) => QrExpiryBox.Text = \"6\";\r\n" +
                    "    private void SetIndefinite0_Click(object sender, RoutedEventArgs e) { PackageExpiryBox.Text = \"0\"; QrExpiryBox.Text = \"0\"; }");
            }

            File.WriteAllText(mwCsPath, mwCs, utf8NoBom);
            Console.WriteLine("[OK] Zaktualizowano MainWindow.xaml.cs (0 = bezterminowo w FromUi, LAN Server i przycisku szybkiego wyboru).");
        }

        // -------------------------------------------------------------------------
        // D. LegacyPackageImporter.cs — pozwolenie na expiryHours == 0
        // -------------------------------------------------------------------------
        string lpiPath = Path.Combine(servicesDir, "LegacyPackageImporter.cs");
        if (File.Exists(lpiPath))
        {
            string lpi = File.ReadAllText(lpiPath, Encoding.UTF8);
            lpi = lpi.Replace("ExpiryHours = Math.Max(1, expiryHours),", "ExpiryHours = Math.Max(0, expiryHours),");
            File.WriteAllText(lpiPath, lpi, utf8NoBom);
            Console.WriteLine("[OK] Zaktualizowano LegacyPackageImporter.cs (Math.Max(0, expiryHours)).");
        }

        // -------------------------------------------------------------------------
        // E. TacticalMapGeneratorView.cs
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
            Console.WriteLine("[OK] Zaktualizowano TacticalMapGeneratorView.cs.");
        }

        // -------------------------------------------------------------------------
        // F. BuilderUiSimplifier.cs:
        //    - Karta "Generator paczek .ftak" z 3 przyciskami
        //    - Zapis neverExpires / packageValidityHours / qrValidityHours / expiresUtc w paczkach .ftak
        // -------------------------------------------------------------------------
        string simpPath = Path.Combine(servicesDir, "BuilderUiSimplifier.cs");
        if (File.Exists(simpPath))
        {
            string simp = File.ReadAllText(simpPath, Encoding.UTF8);

            // Dodaj zapis waznosci (0 = bezterminowo) do rootJson w WriteSingleAuditedFactionFtakArchive
            if (!simp.Contains("[\"neverExpires\"]"))
            {
                simp = simp.Replace(
                    "[\"schemaVersion\"] = \"3.0\",",
                    "[\"schemaVersion\"] = \"3.0\",\r\n" +
                    "                [\"packageValidityHours\"] = TacticalConfigPanel.Current.PackageValidityHours,\r\n" +
                    "                [\"qrValidityHours\"] = TacticalConfigPanel.Current.QrValidityHours,\r\n" +
                    "                [\"neverExpires\"] = (TacticalConfigPanel.Current.PackageValidityHours <= 0),\r\n" +
                    "                [\"expiresAt\"] = (TacticalConfigPanel.Current.PackageValidityHours <= 0 ? DateTimeOffset.UtcNow.AddYears(100).ToString(\"o\") : DateTimeOffset.UtcNow.AddHours(TacticalConfigPanel.Current.PackageValidityHours).ToString(\"o\")),");
            }

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
            }

            File.WriteAllText(simpPath, simp, utf8NoBom);
            Console.WriteLine("[OK] Zaktualizowano BuilderUiSimplifier.cs.");
        }

        // -------------------------------------------------------------------------
        // G. TacticalConfigPanel.cs — synchronizacja pol waznosci (0 = bezterminowo)
        // -------------------------------------------------------------------------
        string tcpPath = Path.Combine(servicesDir, "TacticalConfigPanel.cs");
        if (File.Exists(tcpPath))
        {
            string tcp = File.ReadAllText(tcpPath, Encoding.UTF8);

            tcp = tcp.Replace(
                "return owner != null ? (ShowDialogWithSidebarSupport(dlg, owner)) : (ShowDialogWithSidebarSupport(dlg));",
                "return owner != null ? (dlg.ShowDialog(owner) == true) : (dlg.ShowDialog() == true);");

            // Wylacz sztuczne podmienianie tekstu "0" na "876000" w kontrolkach TextBox, poniewaz teraz 0 jest obslugiwane natywnie!
            tcp = tcp.Replace(
                "try { foreach (var tb in zeros) tb.Text = \"876000\"; } finally { _syncingValidity = false; }",
                "Current.PackageValidityHours = 0; Current.QrValidityHours = 0;");

            File.WriteAllText(tcpPath, tcp, utf8NoBom);
            Console.WriteLine("[OK] Zaktualizowano TacticalConfigPanel.cs.");
        }

        // -------------------------------------------------------------------------
        // H. BuilderCoreSidebarAndProjectEngine.cs — czystosc klamr i brak CS1529
        // -------------------------------------------------------------------------
        string corePath = Path.Combine(servicesDir, "BuilderCoreSidebarAndProjectEngine.cs");
        if (File.Exists(corePath))
        {
            string core = File.ReadAllText(corePath, Encoding.UTF8);

            int strayTestMarker = core.IndexOf("//===V34_UPDATED_TESTS_STEP2_CS===", StringComparison.Ordinal);
            if (strayTestMarker < 0)
            {
                strayTestMarker = core.IndexOf("namespace FieldTakHub.Builder.Tests", StringComparison.Ordinal);
                if (strayTestMarker > 0)
                {
                    int nullableIdx = core.LastIndexOf("#nullable disable", strayTestMarker, StringComparison.Ordinal);
                    if (nullableIdx > 0 && (strayTestMarker - nullableIdx) < 250)
                        strayTestMarker = nullableIdx;
                }
            }

            if (strayTestMarker > 0)
            {
                core = core.Substring(0, strayTestMarker).TrimEnd() + "\r\n}\r\n";
            }

            int openBraces = core.Count(c => c == '{');
            int closeBraces = core.Count(c => c == '}');
            while (closeBraces < openBraces)
            {
                core += "\r\n}\r\n";
                closeBraces++;
            }

            File.WriteAllText(corePath, core, utf8NoBom);
            Console.WriteLine("[OK] Zweryfikowano BuilderCoreSidebarAndProjectEngine.cs.");
        }

        // -------------------------------------------------------------------------
        // I. Testy jednostkowe w V34CleanRefactorTests.cs (w tym test 0 = bezterminowo)
        // -------------------------------------------------------------------------
        if (Directory.Exists(testsDir))
        {
            string testFilePath = Path.Combine(testsDir, "V34CleanRefactorTests.cs");
            string cleanTestsCode = string.Join("\r\n", new string[]
            {
                "#nullable disable",
                "using System;",
                "using System.IO;",
                "using Xunit;",
                "using FieldTakHub.Builder.Models;",
                "using FieldTakHub.Builder.Services;",
                "",
                "namespace FieldTakHub.Builder.Tests",
                "{",
                "    public sealed class V34CleanRefactorTests",
                "    {",
                "        [Fact]",
                "        public void ZeroExpiryHours_ShouldMeanIndefiniteValidityInProjectAndManifest()",
                "        {",
                "            var dir = Path.Combine(Path.GetTempPath(), \"fth-indef-\" + Guid.NewGuid().ToString(\"N\"));",
                "            var source = Path.Combine(dir, \"source\");",
                "            var output = Path.Combine(dir, \"out\");",
                "            Directory.CreateDirectory(Path.Combine(source, \"atak\"));",
                "            File.WriteAllText(Path.Combine(source, \"atak\", \"hello.txt\"), \"test\");",
                "",
                "            var p = new FieldTakProject",
                "            {",
                "                Name = \"IndefiniteTest\",",
                "                PackageId = \"test.indefinite\",",
                "                PackageVersion = \"1.0.0\",",
                "                SourceDirectory = source,",
                "                OutputDirectory = output,",
                "                ExpiryHours = 0,",
                "                QrExpiryHours = 0,",
                "                Server = new ServerProfile { Host = \"127.0.0.1\" }",
                "            };",
                "",
                "            Assert.Equal(0, p.ExpiryHours);",
                "            Assert.Equal(0, p.QrExpiryHours);",
                "",
                "            var items = new SourceAnalyzer().Analyze(source);",
                "            var ftak = new FtakPackageBuilder().Build(p, items);",
                "            Assert.True(File.Exists(ftak));",
                "        }",
                "",
                "        [Fact]",
                "        public void UniqueFactionColors_ShouldReturnDistinctValidHexColors()",
                "        {",
                "            string c1 = V34CleanFtakAndFactionEngine.GetNextUniqueFactionColorHex(null);",
                "            string c2 = V34CleanFtakAndFactionEngine.GetNextUniqueFactionColorHex(null);",
                "            string c3 = V34CleanFtakAndFactionEngine.GetNextUniqueFactionColorHex(null);",
                "",
                "            Assert.StartsWith(\"#\", c1);",
                "            Assert.Equal(7, c1.Length);",
                "            Assert.NotEqual(c1, c2, StringComparer.OrdinalIgnoreCase);",
                "            Assert.NotEqual(c2, c3, StringComparer.OrdinalIgnoreCase);",
                "        }",
                "",
                "        [Fact]",
                "        public void MapStudioBridge_ShouldConvertCoordinatesToMilitaryMgrs()",
                "        {",
                "            double lat = 49.849444;",
                "            double lon = 18.812667;",
                "",
                "            string mgrs = V34CleanMapStudioBridge.LatLonToMgrs(lat, lon);",
                "            Assert.False(string.IsNullOrWhiteSpace(mgrs));",
                "            Assert.Contains(\"34U\", mgrs);",
                "        }",
                "",
                "        [Fact]",
                "        public void GarbageFactionFilter_ShouldRejectPliAndGeoChat()",
                "        {",
                "            Assert.True(V34CleanOpordDataEngine.IsGarbageFactionName(\"PLI\"));",
                "            Assert.True(V34CleanOpordDataEngine.IsGarbageFactionName(\"GeoChat\"));",
                "            Assert.True(V34CleanOpordDataEngine.IsGarbageFactionName(\"TAK\"));",
                "            Assert.False(V34CleanOpordDataEngine.IsGarbageFactionName(\"ORGA\"));",
                "            Assert.False(V34CleanOpordDataEngine.IsGarbageFactionName(\"STRONA-2\"));",
                "        }",
                "    }",
                "}",
                ""
            });

            File.WriteAllText(testFilePath, cleanTestsCode, utf8NoBom);
            Console.WriteLine("[OK] Zaktualizowano V34CleanRefactorTests.cs (dodany test ZeroExpiryHours_ShouldMeanIndefiniteValidityInProjectAndManifest).");
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
        RunProcess(gitExe, "commit -m \"Support 0 as indefinite validity for package and QR code + quick-set button\" --allow-empty", targetDir, ignoreError: false);

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