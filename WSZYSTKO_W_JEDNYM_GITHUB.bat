@echo off
setlocal DisableDelayedExpansion
cd /d "%~dp0"

set "BAT_FILE=%~f0"

echo ============================================================================
echo [1/3] Naprawa bledu odlaczenia elementu podrzędnego (b.Child = null)
echo       w PlaceDraggableHudElement (TacticalMapGeneratorView.cs)...
echo ============================================================================
powershell -NoProfile -ExecutionPolicy Bypass -Command "Add-Type -TypeDefinition ([System.Text.RegularExpressions.Regex]::Split([System.IO.File]::ReadAllText([System.Environment]::GetEnvironmentVariable('BAT_FILE'), [System.Text.Encoding]::UTF8), '//===' + 'FIX_LOGICAL_CHILD_CS===')[1]); [FthFixLogicalChildCrash]::Run()"

if errorlevel 1 (
    echo.
    echo [BLAD] Wystapil blad podczas aktualizacji pliku lub wysylki na GitHub!
    pause
    exit /b 1
)

echo.
echo ============================================================================
echo [SUKCES] Poprawka wyslana na GitHub (QbcolPL/TEST)!
echo Kompilacja na GitHub Actions zostala uruchomiona.
echo ============================================================================
pause
exit /b 0

//===FIX_LOGICAL_CHILD_CS===
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public static class FthFixLogicalChildCrash
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

        string[] parts = Regex.Split(rawBat, "//===" + "PAYLOAD_SAFE_HUD_PLACE_CS===");
        if (parts.Length < 2)
            throw new Exception("Brak bloku PAYLOAD_SAFE_HUD_PLACE_CS w pliku .bat!");
        string[] endParts = Regex.Split(parts[1], "//===" + "END_SAFE_HUD_PLACE_CS===");
        string payloadSafeHudPlace = endParts[0].Trim();

        UTF8Encoding utf8NoBom = new UTF8Encoding(false);
        string tmgPath = Path.Combine(targetDir, @"apps\builder\FieldTakHub.Builder\Services\TacticalMapGeneratorView.cs");
        if (!File.Exists(tmgPath))
            throw new Exception("Nie znaleziono pliku TacticalMapGeneratorView.cs!");

        string tmg = File.ReadAllText(tmgPath, Encoding.UTF8);

        int startIdx = tmg.IndexOf("private static ControlTemplate _transparentThumbTemplate;", StringComparison.Ordinal);
        if (startIdx < 0)
            startIdx = tmg.IndexOf("private static void PlaceDraggableHudElement(", StringComparison.Ordinal);

        int endIdx = tmg.IndexOf("private static void DrawDraggableCornerElements(double w, double h)", StringComparison.Ordinal);
        if (startIdx < 0 || endIdx <= startIdx)
            throw new Exception("Nie znaleziono metody PlaceDraggableHudElement w TacticalMapGeneratorView.cs!");

        tmg = tmg.Substring(0, startIdx) + payloadSafeHudPlace + "\r\n\r\n        " + tmg.Substring(endIdx);

        int openB = tmg.Count(c => c == '{');
        int closeB = tmg.Count(c => c == '}');
        Console.WriteLine("[WERYFIKACJA] Nawiasy klamrowe w TacticalMapGeneratorView.cs: { = " + openB + ", } = " + closeB);
        if (openB != closeB)
            throw new Exception("Niezgodnosc nawiasow klamrowych w TacticalMapGeneratorView.cs!");

        File.WriteAllText(tmgPath, tmg, utf8NoBom);
        Console.WriteLine("[OK] Dodano odlaczenie b.Child = null oraz SafeDetachElement przed dodaniem do nowego kontenera Grid.");

        string triggerPath = Path.Combine(targetDir, ".fth_build_trigger.txt");
        File.WriteAllText(triggerPath, "Field TAK Hub Disconnect Logical Child Fix UTC: " + DateTime.UtcNow.ToString("O") + " | RunId: " + Guid.NewGuid().ToString("N") + "\n", utf8NoBom);

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
        RunProcess(gitExe, "commit -m \"Fix InvalidOperationException in PlaceDraggableHudElement by disconnecting b.Child before adding to hostGrid\" --allow-empty", targetDir, ignoreError: false);

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
//===FIX_LOGICAL_CHILD_CS===

//===PAYLOAD_SAFE_HUD_PLACE_CS===
        private static ControlTemplate _transparentThumbTemplate;

        private static ControlTemplate GetTransparentThumbTemplate()
        {
            if (_transparentThumbTemplate != null) return _transparentThumbTemplate;
            var fef = new FrameworkElementFactory(typeof(Border));
            fef.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(2, 0, 0, 0)));
            var tpl = new ControlTemplate(typeof(Thumb)) { VisualTree = fef };
            tpl.Seal();
            _transparentThumbTemplate = tpl;
            return tpl;
        }

        private static void SafeDetachElement(UIElement el)
        {
            if (el == null) return;
            try
            {
                var parent = LogicalTreeHelper.GetParent(el) ?? VisualTreeHelper.GetParent(el);
                if (parent is Panel panel) panel.Children.Remove(el);
                else if (parent is Decorator dec && dec.Child == el) dec.Child = null;
                else if (parent is ContentControl cc && ReferenceEquals(cc.Content, el)) cc.Content = null;
            }
            catch { }
        }

        private static void PlaceDraggableHudElement(FrameworkElement el, string hudId, string cornerPreset, double? normX, double? normY, double viewW, double viewH, double elemW, double elemH)
        {
            SafeDetachElement(el);

            bool validNormX = normX.HasValue && !double.IsNaN(normX.GetValueOrDefault()) && !double.IsInfinity(normX.GetValueOrDefault()) && normX.GetValueOrDefault() >= -0.2 && normX.GetValueOrDefault() <= 1.2;
            bool validNormY = normY.HasValue && !double.IsNaN(normY.GetValueOrDefault()) && !double.IsInfinity(normY.GetValueOrDefault()) && normY.GetValueOrDefault() >= -0.2 && normY.GetValueOrDefault() <= 1.2;

            double maxInitX = Math.Max(8.0, viewW - Math.Min(elemW, viewW - 40.0) - 4.0);
            double maxInitY = Math.Max(8.0, viewH - Math.Min(elemH, viewH - 40.0) - 4.0);

            double x = validNormX
                ? Math.Clamp(normX.GetValueOrDefault() * viewW, 4.0, maxInitX)
                : (cornerPreset.StartsWith("Lewy") ? 14.0 : Math.Max(14.0, viewW - elemW - 14.0));
            double y = validNormY
                ? Math.Clamp(normY.GetValueOrDefault() * viewH, 4.0, maxInitY)
                : (cornerPreset.Contains("g\u00F3rny") ? 14.0 : Math.Max(14.0, viewH - elemH - 14.0));

            Canvas.SetLeft(el, x);
            Canvas.SetTop(el, y);

            if (hudId != "LEGEND" && el is Border b && b.Child != null && !(b.Child is Grid && ((Grid)b.Child).Tag?.ToString() == "HUD_THUMB_HOST"))
            {
                UIElement innerChild = b.Child;
                b.Child = null;
                SafeDetachElement(innerChild);
                innerChild.IsHitTestVisible = false;

                var hostGrid = new Grid { Tag = "HUD_THUMB_HOST" };
                hostGrid.Children.Add(innerChild);

                var moveThumb = new Thumb
                {
                    Template = GetTransparentThumbTemplate(),
                    Cursor = Cursors.SizeAll,
                    ToolTip = "Przeci\u0105gnij mysz\u0105 (Drag & Drop), aby ustawi\u0107 w dowolnym miejscu na mapie"
                };

                Point startMousePt = new Point();
                double startLeft = x, startTop = y;

                moveThumb.DragStarted += (s, e) =>
                {
                    e.Handled = true;
                    startMousePt = Mouse.GetPosition(_interactiveCanvas);
                    startLeft = Canvas.GetLeft(el);
                    startTop = Canvas.GetTop(el);
                    if (double.IsNaN(startLeft)) startLeft = x;
                    if (double.IsNaN(startTop)) startTop = y;
                };

                moveThumb.DragDelta += (s, e) =>
                {
                    e.Handled = true;
                    var (cw, ch) = GetExactCanvasSize();
                    Point curPt = Mouse.GetPosition(_interactiveCanvas);
                    double actualW = el.ActualWidth > 10 ? el.ActualWidth : elemW;
                    double actualH = el.ActualHeight > 10 ? el.ActualHeight : elemH;
                    double limX = Math.Max(4.0, cw - Math.Min(actualW, cw - 30.0) - 4.0);
                    double limY = Math.Max(4.0, ch - Math.Min(actualH, ch - 30.0) - 4.0);
                    double nx = Math.Clamp(startLeft + (curPt.X - startMousePt.X), 4.0, limX);
                    double ny = Math.Clamp(startTop + (curPt.Y - startMousePt.Y), 4.0, limY);
                    Canvas.SetLeft(el, nx);
                    Canvas.SetTop(el, ny);
                    if (hudId == "NORTH") { _northNormX = nx / Math.Max(1.0, cw); _northNormY = ny / Math.Max(1.0, ch); }
                    else if (hudId == "SCALE") { _scaleNormX = nx / Math.Max(1.0, cw); _scaleNormY = ny / Math.Max(1.0, ch); }
                };

                moveThumb.DragCompleted += (s, e) =>
                {
                    e.Handled = true;
                    TacticalConfigPanel.MarkDirty();
                };

                hostGrid.Children.Add(moveThumb);
                b.Child = hostGrid;
            }

            _interactiveCanvas.Children.Add(el);
        }
//===END_SAFE_HUD_PLACE_CS===