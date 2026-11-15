@echo off
setlocal DisableDelayedExpansion
cd /d "%~dp0"

set "BAT_FILE=%~f0"

echo ============================================================================
echo [1/3] Naprawa ukladu kart w tabeli obiektow (rozpoznawanie czerwonego Bordera
echo       [-], blokada duplikacji kontrolek w kartach Infil/Kod i wiersz Geometrii)...
echo ============================================================================
powershell -NoProfile -ExecutionPolicy Bypass -Command "Add-Type -TypeDefinition ([System.Text.RegularExpressions.Regex]::Split([System.IO.File]::ReadAllText([System.Environment]::GetEnvironmentVariable('BAT_FILE'), [System.Text.Encoding]::UTF8), '//===' + 'FIX_OBJECT_TABLE_V2_CS===')[1]); [FthFixObjectTableCardsV2]::Run()"

if errorlevel 1 (
    echo.
    echo [BLAD] Wystapil blad podczas aktualizacji pliku lub wysylki na GitHub!
    pause
    exit /b 1
)

echo.
echo ============================================================================
echo [SUKCES] Poprawiona tabela obiektow (V2) zostala wyslana na GitHub!
echo Kompilacja na GitHub Actions zostala uruchomiona.
echo ============================================================================
pause
exit /b 0

//===FIX_OBJECT_TABLE_V2_CS===
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public static class FthFixObjectTableCardsV2
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
        string wgsPath = Path.Combine(servicesDir, "TacticalMapGeneratorView.Wgs84.cs");

        if (!File.Exists(wgsPath))
            throw new Exception("Nie znaleziono pliku TacticalMapGeneratorView.Wgs84.cs!");

        string batFile = Environment.GetEnvironmentVariable("BAT_FILE");
        string rawBat = File.ReadAllText(batFile, Encoding.UTF8);

        string[] parts = Regex.Split(rawBat, "//===" + "PAYLOAD_OBJECT_TABLE_V2_CS===");
        if (parts.Length < 2)
            throw new Exception("Brak bloku PAYLOAD_OBJECT_TABLE_V2_CS w pliku .bat!");
        string[] endParts = Regex.Split(parts[1], "//===" + "END_OBJECT_TABLE_V2_CS===");
        string newTableCode = endParts[0].Trim();

        string wgs = File.ReadAllText(wgsPath, Encoding.UTF8);

        string startMarker = "private static bool _isRestylingObjectsTable = false;";
        if (!wgs.Contains(startMarker))
            startMarker = "public static void RestyleAndAlignMapObjectsTable(Panel listPanel)";

        string endMarker = "private static bool IsDescendantFast(DependencyObject node, HashSet<DependencyObject> targets)";

        int startIdx = wgs.IndexOf(startMarker, StringComparison.Ordinal);
        int endIdx = wgs.IndexOf(endMarker, StringComparison.Ordinal);
        if (startIdx < 0 || endIdx <= startIdx)
            throw new Exception("Nie znaleziono sekcji RestyleAndAlignMapObjectsTable w TacticalMapGeneratorView.Wgs84.cs!");

        wgs = wgs.Substring(0, startIdx) + newTableCode + "\r\n\r\n        " + wgs.Substring(endIdx);

        int openWgs = wgs.Count(c => c == '{');
        int closeWgs = wgs.Count(c => c == '}');
        Console.WriteLine("[WERYFIKACJA] Nawiasy w TacticalMapGeneratorView.Wgs84.cs: { = " + openWgs + ", } = " + closeWgs);
        if (openWgs != closeWgs)
            throw new Exception("Niezgodnosc nawiasow klamrowych w TacticalMapGeneratorView.Wgs84.cs!");

        File.WriteAllText(wgsPath, wgs, utf8NoBom);
        Console.WriteLine("[OK] Zaktualizowano uklad kart obiektow (V2) w TacticalMapGeneratorView.Wgs84.cs.");

        string triggerPath = Path.Combine(targetDir, ".fth_build_trigger.txt");
        File.WriteAllText(triggerPath, "Field TAK Hub Fix Map Objects Table Cards V2 UTC: " + DateTime.UtcNow.ToString("O") + " | RunId: " + Guid.NewGuid().ToString("N") + "\n", utf8NoBom);

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
        RunProcess(gitExe, "commit -m \"Fix Map Objects Table cards V2 (handle red Border delete button, MinHeight reset, and distinct pin/delete refs)\" --allow-empty", targetDir);

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
//===FIX_OBJECT_TABLE_V2_CS===

//===PAYLOAD_OBJECT_TABLE_V2_CS===
private static bool _isRestylingObjectsTable = false;

        // ====================================================================
        // CZYSTY UKLAD TABELI OBIEKTOW NA MAPIE (3/4 WIERSZE, BEZ UTRATY KONTROLEK)
        // ====================================================================
        public static void RestyleAndAlignMapObjectsTable(Panel listPanel)
        {
            if (listPanel == null || _isRestylingObjectsTable) return;
            _isRestylingObjectsTable = true;
            try
            {
                var cards = listPanel.Children.OfType<Border>().ToList();
                foreach (var card in cards)
                {
                    if (card.Tag is string t && t == "FTH_RESTYLED_OBJ_CARD_V2")
                        continue;

                    try
                    {
                        RestyleSingleMapObjectCard(card);
                        card.Tag = "FTH_RESTYLED_OBJ_CARD_V2";
                    }
                    catch { }
                }
            }
            finally
            {
                _isRestylingObjectsTable = false;
            }
        }

        private static void DetachElementFromAnyParent(UIElement el)
        {
            if (el == null) return;

            DependencyObject parent = null;
            if (el is FrameworkElement fe && fe.Parent != null)
                parent = fe.Parent;
            if (parent == null)
            {
                try { parent = LogicalTreeHelper.GetParent(el); } catch { }
            }
            if (parent == null)
            {
                try { parent = VisualTreeHelper.GetParent(el); } catch { }
            }

            if (parent is Panel pnl)
            {
                pnl.Children.Remove(el);
            }
            else if (parent is Decorator dec && ReferenceEquals(dec.Child, el))
            {
                dec.Child = null;
            }
            else if (parent is ContentControl cc && ReferenceEquals(cc.Content, el))
            {
                cc.Content = null;
            }

            Grid.SetRow(el, 0);
            Grid.SetColumn(el, 0);
            Grid.SetRowSpan(el, 1);
            Grid.SetColumnSpan(el, 1);
        }

        private static bool IsRedDeleteElement(DependencyObject obj)
        {
            if (obj is Border b)
            {
                if (b.Child is TextBlock tb)
                {
                    string t = (tb.Text ?? "").Trim();
                    if (t == "−" || t == "-" || t == "✕" || t == "×" || t == "X")
                        return true;
                }
                if (b.Background is SolidColorBrush scb)
                {
                    Color c = scb.Color;
                    if (c.R > 180 && c.G < 95 && c.B < 95 && b.Child is TextBlock)
                        return true;
                }
            }
            else if (obj is Button btn)
            {
                string t = ExtractPlainButtonText(btn).Trim();
                if (t == "−" || t == "-" || t == "✕" || t == "×")
                    return true;
                if (btn.Background is SolidColorBrush scb)
                {
                    Color c = scb.Color;
                    if (c.R > 180 && c.G < 95 && c.B < 95)
                        return true;
                }
            }
            return false;
        }

        private static void RestyleSingleMapObjectCard(Border card)
        {
            var allControls = new List<DependencyObject>();
            var visited = new HashSet<DependencyObject>();
            CollectLogicalChildrenBounded(card, allControls, visited, 0);

            var checkBox = allControls.OfType<CheckBox>().FirstOrDefault();
            var textBoxes = allControls.OfType<TextBox>().ToList();
            if (checkBox == null || textBoxes.Count < 1) return;

            TextBox nameBox = textBoxes[0];
            TextBox legendBox = textBoxes.Count > 1 ? textBoxes[1] : null;
            Slider sizeSlider = allControls.OfType<Slider>().FirstOrDefault();

            // 1. Znajdz czerwony przycisk usuwania [-] (zarowno gdy jest Borderem, jak i gdy jest Buttonem)
            FrameworkElement deleteElement = null;
            foreach (var d in allControls)
            {
                if (ReferenceEquals(d, card)) continue;
                if (d is FrameworkElement fe && IsRedDeleteElement(fe))
                {
                    deleteElement = fe;
                    break;
                }
            }

            // 2. Zbierz przyciski Button (z pominieciem deleteElement, jezeli byl Buttonem)
            var buttons = allControls.OfType<Button>()
                .Where(b => !ReferenceEquals(b, deleteElement))
                .ToList();

            if (buttons.Count < 1) return;

            Button colorBtn = buttons[0];
            Button pinBtn = buttons.Count > 1 ? buttons[1] : null;
            Button typeBtn = null;
            Button simplifyBtn = null;

            for (int i = 2; i < buttons.Count; i++)
            {
                var b = buttons[i];
                string bTxt = ExtractPlainButtonText(b);
                if (bTxt.IndexOf("Typ", StringComparison.OrdinalIgnoreCase) >= 0 || typeBtn == null)
                    typeBtn = b;
                else if (simplifyBtn == null)
                    simplifyBtn = b;
            }

            // 3. Znajdz plakietke symbolu [AO] / [●] (Border w wierszu naglowka, ktory nie jest karta, przyciskiem ani deleteElement)
            var excludedRoots = new HashSet<DependencyObject>(
                buttons.Cast<DependencyObject>()
                .Concat(textBoxes.Cast<DependencyObject>()));
            if (deleteElement != null) excludedRoots.Add(deleteElement);

            Border symbolBadge = null;
            foreach (var brd in allControls.OfType<Border>())
            {
                if (ReferenceEquals(brd, card) || ReferenceEquals(brd, deleteElement)) continue;
                if (IsDescendantFast(brd, excludedRoots)) continue;
                if (IsDescendantFast(checkBox, new HashSet<DependencyObject> { brd })) continue;
                symbolBadge = brd;
                break;
            }

            // 4. Znajdz etykiete licznika punktow / rozmiaru ("12x" lub "20 px (11x)")
            var btnAndDelSet = new HashSet<DependencyObject>(buttons.Cast<DependencyObject>());
            if (deleteElement != null) btnAndDelSet.Add(deleteElement);
            if (symbolBadge != null) btnAndDelSet.Add(symbolBadge);

            TextBlock counterTb = null;
            foreach (var tb in allControls.OfType<TextBlock>())
            {
                if (IsDescendantFast(tb, btnAndDelSet)) continue;
                string txt = (tb.Text ?? "").Trim();
                if (txt.EndsWith("x", StringComparison.OrdinalIgnoreCase) || txt.Contains("px") || Regex.IsMatch(txt, @"^\d+\s*x$"))
                    counterTb = tb;
            }

            // 5. Bezpieczne odlaczenie wszystkich wyodrebnionych kontrolek od dotychczasowych kontenerow
            DetachElementFromAnyParent(checkBox);
            DetachElementFromAnyParent(colorBtn);
            if (pinBtn != null) DetachElementFromAnyParent(pinBtn);
            if (symbolBadge != null) DetachElementFromAnyParent(symbolBadge);
            DetachElementFromAnyParent(nameBox);
            if (deleteElement != null) DetachElementFromAnyParent(deleteElement);
            if (legendBox != null) DetachElementFromAnyParent(legendBox);
            if (sizeSlider != null) DetachElementFromAnyParent(sizeSlider);
            if (typeBtn != null) DetachElementFromAnyParent(typeBtn);
            if (simplifyBtn != null) DetachElementFromAnyParent(simplifyBtn);
            if (counterTb != null) DetachElementFromAnyParent(counterTb);

            // Stylizacja karty zewnetrznej
            card.Background = new SolidColorBrush(Color.FromRgb(10, 24, 30));
            card.BorderBrush = new SolidColorBrush(Color.FromRgb(24, 62, 74));
            card.BorderThickness = new Thickness(1);
            card.CornerRadius = new CornerRadius(6);
            card.Padding = new Thickness(9, 7, 9, 7);
            card.Margin = new Thickness(0, 0, 0, 6);

            var mainStack = new StackPanel();
            var usedElements = new HashSet<UIElement>();

            // WIERSZ 1: [Checkbox] [Kolor 22x22] [Pinezka 26x22] [Symbol] [Nazwa *] [Usun -]
            var row1 = new Grid { Height = 26 };
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            if (usedElements.Add(checkBox))
            {
                checkBox.VerticalAlignment = VerticalAlignment.Center;
                checkBox.Margin = new Thickness(0, 0, 6, 0);
                Grid.SetColumn(checkBox, 0);
                row1.Children.Add(checkBox);
            }

            if (usedElements.Add(colorBtn))
            {
                colorBtn.MinWidth = 22;
                colorBtn.Width = 22;
                colorBtn.MaxWidth = 22;
                colorBtn.MinHeight = 22;
                colorBtn.Height = 22;
                colorBtn.MaxHeight = 22;
                colorBtn.Padding = new Thickness(0);
                colorBtn.VerticalAlignment = VerticalAlignment.Center;
                colorBtn.Margin = new Thickness(0, 0, 5, 0);
                colorBtn.BorderBrush = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255));
                colorBtn.BorderThickness = new Thickness(1);
                colorBtn.Cursor = Cursors.Hand;
                Grid.SetColumn(colorBtn, 1);
                row1.Children.Add(colorBtn);
            }

            if (pinBtn != null && usedElements.Add(pinBtn))
            {
                pinBtn.MinWidth = 26;
                pinBtn.Width = 26;
                pinBtn.MaxWidth = 26;
                pinBtn.MinHeight = 22;
                pinBtn.Height = 22;
                pinBtn.MaxHeight = 22;
                pinBtn.Padding = new Thickness(0);
                pinBtn.Margin = new Thickness(0, 0, 5, 0);
                pinBtn.VerticalAlignment = VerticalAlignment.Center;
                pinBtn.HorizontalContentAlignment = HorizontalAlignment.Center;
                pinBtn.VerticalContentAlignment = VerticalAlignment.Center;
                pinBtn.Background = new SolidColorBrush(Color.FromRgb(7, 17, 21));
                pinBtn.BorderBrush = TacticalConfigPanel.BrushAccent;
                pinBtn.BorderThickness = new Thickness(1);
                pinBtn.Foreground = TacticalConfigPanel.BrushAccent;
                pinBtn.FontSize = 11;
                if (string.IsNullOrWhiteSpace(ExtractPlainButtonText(pinBtn))) pinBtn.Content = "📍";
                Grid.SetColumn(pinBtn, 2);
                row1.Children.Add(pinBtn);
            }

            if (symbolBadge != null && usedElements.Add(symbolBadge))
            {
                symbolBadge.MinHeight = 22;
                symbolBadge.Height = 22;
                symbolBadge.MaxHeight = 22;
                symbolBadge.MinWidth = 30;
                symbolBadge.VerticalAlignment = VerticalAlignment.Center;
                symbolBadge.Margin = new Thickness(0, 0, 6, 0);
                Grid.SetColumn(symbolBadge, 3);
                row1.Children.Add(symbolBadge);
            }

            if (usedElements.Add(nameBox))
            {
                nameBox.MinHeight = 24;
                nameBox.Height = 24;
                nameBox.MaxHeight = 24;
                nameBox.Width = double.NaN;
                nameBox.MinWidth = 80;
                nameBox.HorizontalAlignment = HorizontalAlignment.Stretch;
                nameBox.VerticalAlignment = VerticalAlignment.Center;
                nameBox.VerticalContentAlignment = VerticalAlignment.Center;
                nameBox.Padding = new Thickness(6, 0, 6, 0);
                nameBox.Margin = new Thickness(0, 0, 6, 0);
                nameBox.Background = new SolidColorBrush(Color.FromRgb(7, 17, 21));
                nameBox.Foreground = Brushes.White;
                nameBox.CaretBrush = TacticalConfigPanel.BrushAccent;
                nameBox.BorderBrush = TacticalConfigPanel.BrushAccent;
                nameBox.BorderThickness = new Thickness(1);
                nameBox.FontSize = 11;
                nameBox.FontWeight = FontWeights.Bold;
                Grid.SetColumn(nameBox, 4);
                row1.Children.Add(nameBox);
            }

            if (deleteElement != null && usedElements.Add(deleteElement))
            {
                deleteElement.MinWidth = 24;
                deleteElement.Width = 24;
                deleteElement.MaxWidth = 24;
                deleteElement.MinHeight = 22;
                deleteElement.Height = 22;
                deleteElement.MaxHeight = 22;
                deleteElement.Margin = new Thickness(0);
                deleteElement.VerticalAlignment = VerticalAlignment.Center;
                deleteElement.Cursor = Cursors.Hand;
                Grid.SetColumn(deleteElement, 5);
                row1.Children.Add(deleteElement);
            }

            mainStack.Children.Add(row1);

            // WIERSZ 2: W legendzie: [ Pole opisu w legendzie * ]
            if (legendBox != null && usedElements.Add(legendBox))
            {
                var row2 = new Grid { Margin = new Thickness(0, 5, 0, 0), Height = 24 };
                row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });
                row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var lblLeg = new TextBlock
                {
                    Text = "W legendzie:",
                    Foreground = TacticalConfigPanel.BrushMutedText,
                    FontSize = 9.5,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(lblLeg, 0);
                row2.Children.Add(lblLeg);

                legendBox.MinHeight = 23;
                legendBox.Height = 23;
                legendBox.MaxHeight = 23;
                legendBox.Width = double.NaN;
                legendBox.HorizontalAlignment = HorizontalAlignment.Stretch;
                legendBox.VerticalAlignment = VerticalAlignment.Center;
                legendBox.VerticalContentAlignment = VerticalAlignment.Center;
                legendBox.Padding = new Thickness(6, 0, 6, 0);
                legendBox.Margin = new Thickness(0);
                legendBox.Background = new SolidColorBrush(Color.FromRgb(7, 17, 21));
                legendBox.Foreground = new SolidColorBrush(Color.FromRgb(215, 228, 232));
                legendBox.CaretBrush = TacticalConfigPanel.BrushAccent;
                legendBox.BorderBrush = new SolidColorBrush(Color.FromRgb(22, 56, 68));
                legendBox.BorderThickness = new Thickness(1);
                legendBox.FontSize = 10.5;
                legendBox.ToolTip = null;
                Grid.SetColumn(legendBox, 1);
                row2.Children.Add(legendBox);

                mainStack.Children.Add(row2);
            }

            // WIERSZ 3: Wielkosc: [ Slider * ] [ 12x / 20 px (11x) ]
            if (sizeSlider != null && usedElements.Add(sizeSlider))
            {
                var row3 = new Grid { Margin = new Thickness(0, 5, 0, 0), Height = 24 };
                row3.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });
                row3.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row3.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var lblSize = new TextBlock
                {
                    Text = "Wielkość:",
                    Foreground = TacticalConfigPanel.BrushMutedText,
                    FontSize = 9.5,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(lblSize, 0);
                row3.Children.Add(lblSize);

                sizeSlider.VerticalAlignment = VerticalAlignment.Center;
                sizeSlider.Margin = new Thickness(0, 0, 8, 0);
                Grid.SetColumn(sizeSlider, 1);
                row3.Children.Add(sizeSlider);

                if (counterTb != null && usedElements.Add(counterTb))
                {
                    counterTb.VerticalAlignment = VerticalAlignment.Center;
                    counterTb.Foreground = TacticalConfigPanel.BrushAccent;
                    counterTb.FontSize = 10;
                    counterTb.FontWeight = FontWeights.Bold;
                    counterTb.FontFamily = new FontFamily("Consolas");
                    counterTb.Margin = new Thickness(4, 0, 0, 0);
                    Grid.SetColumn(counterTb, 2);
                    row3.Children.Add(counterTb);
                }

                mainStack.Children.Add(row3);
            }

            // WIERSZ 4 (dla Poligonow / Tras): Geometria: [ 🔁 Zmień typ ] [ ✂️ Uprość kształt ]
            if (typeBtn != null || simplifyBtn != null)
            {
                var row4 = new Grid { Margin = new Thickness(0, 5, 0, 0), Height = 24 };
                row4.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });
                row4.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row4.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var lblGeo = new TextBlock
                {
                    Text = "Geometria:",
                    Foreground = TacticalConfigPanel.BrushMutedText,
                    FontSize = 9.5,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(lblGeo, 0);
                row4.Children.Add(lblGeo);

                var actionsBar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

                if (typeBtn != null && usedElements.Add(typeBtn))
                {
                    typeBtn.MinWidth = 0;
                    typeBtn.Width = double.NaN;
                    typeBtn.MaxWidth = double.PositiveInfinity;
                    typeBtn.MinHeight = 22;
                    typeBtn.Height = 22;
                    typeBtn.MaxHeight = 22;
                    typeBtn.Padding = new Thickness(8, 0, 8, 0);
                    typeBtn.Margin = new Thickness(0, 0, 6, 0);
                    typeBtn.Background = new SolidColorBrush(Color.FromRgb(7, 17, 21));
                    typeBtn.Foreground = TacticalConfigPanel.BrushAccent;
                    typeBtn.BorderBrush = TacticalConfigPanel.BrushAccent;
                    typeBtn.BorderThickness = new Thickness(1);
                    typeBtn.FontSize = 9.5;
                    typeBtn.FontWeight = FontWeights.SemiBold;
                    typeBtn.Content = "🔁 Zmień typ (AO/Sektor/Trasa)";
                    typeBtn.Cursor = Cursors.Hand;
                    actionsBar.Children.Add(typeBtn);
                }

                if (simplifyBtn != null && usedElements.Add(simplifyBtn))
                {
                    simplifyBtn.MinWidth = 0;
                    simplifyBtn.Width = double.NaN;
                    simplifyBtn.MaxWidth = double.PositiveInfinity;
                    simplifyBtn.MinHeight = 22;
                    simplifyBtn.Height = 22;
                    simplifyBtn.MaxHeight = 22;
                    simplifyBtn.Padding = new Thickness(8, 0, 8, 0);
                    simplifyBtn.Margin = new Thickness(0, 0, 6, 0);
                    simplifyBtn.Background = new SolidColorBrush(Color.FromRgb(7, 17, 21));
                    simplifyBtn.Foreground = Brushes.White;
                    simplifyBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(28, 74, 88));
                    simplifyBtn.BorderThickness = new Thickness(1);
                    simplifyBtn.FontSize = 9.5;
                    simplifyBtn.Content = "✂️ Uprość kształt";
                    simplifyBtn.Cursor = Cursors.Hand;
                    actionsBar.Children.Add(simplifyBtn);
                }

                Grid.SetColumn(actionsBar, 1);
                row4.Children.Add(actionsBar);

                if (counterTb != null && usedElements.Add(counterTb))
                {
                    counterTb.VerticalAlignment = VerticalAlignment.Center;
                    counterTb.Foreground = TacticalConfigPanel.BrushAccent;
                    counterTb.FontSize = 10;
                    counterTb.FontWeight = FontWeights.Bold;
                    counterTb.FontFamily = new FontFamily("Consolas");
                    counterTb.Margin = new Thickness(4, 0, 0, 0);
                    Grid.SetColumn(counterTb, 2);
                    row4.Children.Add(counterTb);
                }

                mainStack.Children.Add(row4);
            }

            card.Child = mainStack;
        }

        private static void CollectLogicalChildrenBounded(DependencyObject parent, List<DependencyObject> list, HashSet<DependencyObject> visited, int depth)
        {
            if (parent == null || depth > 7 || !visited.Add(parent)) return;
            if (parent is Panel pnl)
            {
                foreach (UIElement ch in pnl.Children)
                {
                    if (ch != null)
                    {
                        list.Add(ch);
                        CollectLogicalChildrenBounded(ch, list, visited, depth + 1);
                    }
                }
            }
            else if (parent is Decorator dec && dec.Child != null)
            {
                list.Add(dec.Child);
                CollectLogicalChildrenBounded(dec.Child, list, visited, depth + 1);
            }
            else if (parent is ContentControl cc && cc.Content is DependencyObject dObj)
            {
                list.Add(dObj);
                CollectLogicalChildrenBounded(dObj, list, visited, depth + 1);
            }
        }
//===END_OBJECT_TABLE_V2_CS===