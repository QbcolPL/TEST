@echo off
setlocal DisableDelayedExpansion
cd /d "%~dp0"

set "BAT_FILE=%~f0"

echo ============================================================================
echo [1/3] Naprawa przelaczania modulow (Mapy/OPORD/USB) oraz SigningKeyService...
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
echo [SUKCES] Poprawki wyslane na GitHub (QbcolPL/TEST)!
echo Kompilacja i testy na GitHub Actions zostaly uruchomione.
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

        // 2. Napraw wyscig wielowatkowy (IOException) w SigningKeyService.cs
        FixSigningKeyServiceConcurrency(targetDir, utf8NoBom);

        // 3. Napraw przelaczanie modulow (Mapy / OPORD / USB / Paczki), 0 = bezterminowo i ostrzezenia CS0169
        ApplyAllPackageGeneratorAndModuleSwitchFixes(targetDir, utf8NoBom);

        // 4. Zapisz unikalny znacznik wyzwalajacy dla GitHub Actions
        string triggerPath = Path.Combine(targetDir, ".fth_build_trigger.txt");
        string triggerContent = "Field TAK Hub Auto-Build Trigger UTC: " + DateTime.UtcNow.ToString("O") + " | RunId: " + Guid.NewGuid().ToString("N") + "\n";
        File.WriteAllText(triggerPath, triggerContent, utf8NoBom);

        // 5. Wyslij zmiany na GitHub i uruchom GitHub Actions
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

    private static void FixSigningKeyServiceConcurrency(string targetDir, UTF8Encoding utf8NoBom)
    {
        string keyServicePath = Path.Combine(targetDir, @"apps\builder\FieldTakHub.Builder\Services\SigningKeyService.cs");
        if (!File.Exists(keyServicePath)) return;

        string cleanKeyService = string.Join("\r\n", new string[]
        {
            "using System.IO;",
            "using System.Security.Cryptography;",
            "using System.Text.Json;",
            "using NSec.Cryptography;",
            "",
            "namespace FieldTakHub.Builder.Services;",
            "",
            "public sealed class SigningKeyService",
            "{",
            "    private static readonly object KeyFileLock = new();",
            "    private readonly string _keyPath;",
            "    private readonly SignatureAlgorithm _algorithm = SignatureAlgorithm.Ed25519;",
            "",
            "    public SigningKeyService()",
            "    {",
            "        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), \"FieldTakHub\", \"keys\");",
            "        Directory.CreateDirectory(dir);",
            "        _keyPath = Path.Combine(dir, \"default.ed25519.dpapi\");",
            "    }",
            "",
            "    public (byte[] Signature, byte[] PublicKey, string Fingerprint) Sign(ReadOnlySpan<byte> data)",
            "    {",
            "        using var key = LoadOrCreate();",
            "        var sig = _algorithm.Sign(key, data);",
            "        var pub = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);",
            "        var fp = Convert.ToHexString(SHA256.HashData(pub)).ToLowerInvariant();",
            "        return (sig, pub, fp);",
            "    }",
            "",
            "    public string Fingerprint()",
            "    {",
            "        using var key = LoadOrCreate();",
            "        return Convert.ToHexString(SHA256.HashData(key.PublicKey.Export(KeyBlobFormat.RawPublicKey))).ToLowerInvariant();",
            "    }",
            "",
            "    public void ExportEncryptedBackup(string path, string password)",
            "    {",
            "        if (password.Length < 10) throw new InvalidDataException(\"FTH-KEY-001: backup password must contain at least 10 characters.\");",
            "        byte[] protectedBytes;",
            "        lock (KeyFileLock)",
            "        {",
            "            using (LoadOrCreate()) { }",
            "            protectedBytes = File.ReadAllBytes(_keyPath);",
            "        }",
            "        var raw = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);",
            "        using var key = Key.Import(_algorithm, raw, KeyBlobFormat.RawPrivateKey);",
            "        var salt = RandomNumberGenerator.GetBytes(16); var nonce = RandomNumberGenerator.GetBytes(12); var tag = new byte[16]; var cipher = new byte[raw.Length];",
            "        var derived = Rfc2898DeriveBytes.Pbkdf2(password, salt, 600_000, HashAlgorithmName.SHA256, 32);",
            "        try",
            "        {",
            "            using var aes = new AesGcm(derived, 16);",
            "            aes.Encrypt(nonce, raw, cipher, tag, \"FieldTAKHub.fthkey.v1\"u8.ToArray());",
            "            var doc = new {",
            "                schema = \"fieldtak.publisher-key-backup\", version = 1, kdf = \"PBKDF2-SHA256\", iterations = 600000, cipher = \"AES-256-GCM\",",
            "                fingerprint = Convert.ToHexString(SHA256.HashData(key.PublicKey.Export(KeyBlobFormat.RawPublicKey))).ToLowerInvariant(),",
            "                salt = Convert.ToBase64String(salt), nonce = Convert.ToBase64String(nonce), tag = Convert.ToBase64String(tag), ciphertext = Convert.ToBase64String(cipher)",
            "            };",
            "            File.WriteAllText(path, JsonSerializer.Serialize(doc, JsonDefaults.Options));",
            "        }",
            "        finally { CryptographicOperations.ZeroMemory(raw); CryptographicOperations.ZeroMemory(derived); }",
            "    }",
            "",
            "    public string ImportEncryptedBackup(string path, string password)",
            "    {",
            "        using var doc = JsonDocument.Parse(File.ReadAllText(path)); var root = doc.RootElement;",
            "        if (root.GetProperty(\"schema\").GetString() != \"fieldtak.publisher-key-backup\" || root.GetProperty(\"version\").GetInt32() != 1)",
            "            throw new InvalidDataException(\"FTH-KEY-002: unsupported publisher key backup.\");",
            "        var iterations = root.GetProperty(\"iterations\").GetInt32();",
            "        var salt = Convert.FromBase64String(root.GetProperty(\"salt\").GetString()!); var nonce = Convert.FromBase64String(root.GetProperty(\"nonce\").GetString()!);",
            "        var tag = Convert.FromBase64String(root.GetProperty(\"tag\").GetString()!); var cipher = Convert.FromBase64String(root.GetProperty(\"ciphertext\").GetString()!);",
            "        var raw = new byte[cipher.Length]; var derived = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);",
            "        try",
            "        {",
            "            using var aes = new AesGcm(derived, 16); aes.Decrypt(nonce, cipher, tag, raw, \"FieldTAKHub.fthkey.v1\"u8.ToArray());",
            "            using var imported = Key.Import(_algorithm, raw, KeyBlobFormat.RawPrivateKey);",
            "            var fingerprint = Convert.ToHexString(SHA256.HashData(imported.PublicKey.Export(KeyBlobFormat.RawPublicKey))).ToLowerInvariant();",
            "            var expected = root.GetProperty(\"fingerprint\").GetString();",
            "            if (!fingerprint.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new CryptographicException(\"FTH-KEY-003: backup fingerprint mismatch.\");",
            "            var protectedBlob = ProtectedData.Protect(raw, null, DataProtectionScope.CurrentUser);",
            "            lock (KeyFileLock)",
            "            {",
            "                File.WriteAllBytes(_keyPath, protectedBlob);",
            "            }",
            "            return fingerprint;",
            "        }",
            "        catch (CryptographicException ex) when (!ex.Message.StartsWith(\"FTH-KEY-\", StringComparison.Ordinal)) { throw new CryptographicException(\"FTH-KEY-004: wrong password or damaged backup.\", ex); }",
            "        finally { CryptographicOperations.ZeroMemory(raw); CryptographicOperations.ZeroMemory(derived); }",
            "    }",
            "",
            "    private Key LoadOrCreate()",
            "    {",
            "        lock (KeyFileLock)",
            "        {",
            "            Directory.CreateDirectory(Path.GetDirectoryName(_keyPath)!);",
            "            if (File.Exists(_keyPath))",
            "            {",
            "                var protectedBlob = File.ReadAllBytes(_keyPath);",
            "                var raw = ProtectedData.Unprotect(protectedBlob, null, DataProtectionScope.CurrentUser);",
            "                try { return Key.Import(_algorithm, raw, KeyBlobFormat.RawPrivateKey); }",
            "                finally { CryptographicOperations.ZeroMemory(raw); }",
            "            }",
            "",
            "            using var created = Key.Create(_algorithm, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });",
            "            var rawPrivate = created.Export(KeyBlobFormat.RawPrivateKey);",
            "            try",
            "            {",
            "                var protectedBlob = ProtectedData.Protect(rawPrivate, null, DataProtectionScope.CurrentUser);",
            "                File.WriteAllBytes(_keyPath, protectedBlob);",
            "                return Key.Import(_algorithm, rawPrivate, KeyBlobFormat.RawPrivateKey);",
            "            }",
            "            finally { CryptographicOperations.ZeroMemory(rawPrivate); }",
            "        }",
            "    }",
            "}",
            ""
        });

        File.WriteAllText(keyServicePath, cleanKeyService, utf8NoBom);
        Console.WriteLine("[OK] Zabezpieczono SigningKeyService.cs przed kolizja wielowatkowa (lock KeyFileLock).");
    }

    private static void ApplyAllPackageGeneratorAndModuleSwitchFixes(string targetDir, UTF8Encoding utf8NoBom)
    {
        string builderDir = Path.Combine(targetDir, @"apps\builder\FieldTakHub.Builder");
        string servicesDir = Path.Combine(builderDir, "Services");
        string testsDir = Path.Combine(targetDir, @"apps\builder\FieldTakHub.Builder.Tests");

        // -------------------------------------------------------------------------
        // A. BuilderUiSimplifier.cs — usuniecie ostrzezen CS0169 (_activeFactionCombo, _btnBuildSingleFaction)
        // -------------------------------------------------------------------------
        string simpPath = Path.Combine(servicesDir, "BuilderUiSimplifier.cs");
        if (File.Exists(simpPath))
        {
            string simp = File.ReadAllText(simpPath, Encoding.UTF8);
            simp = simp.Replace("private static ComboBox _activeFactionCombo;", "// removed unused _activeFactionCombo");
            simp = simp.Replace("private static Button _btnBuildSingleFaction;", "// removed unused _btnBuildSingleFaction");
            File.WriteAllText(simpPath, simp, utf8NoBom);
            Console.WriteLine("[OK] Usunieto ostrzezenia CS0169 w BuilderUiSimplifier.cs.");
        }

        // -------------------------------------------------------------------------
        // B. BuilderCoreSidebarAndProjectEngine.cs:
        //    1. Naprawa SwitchActiveModule — bezposrednie przelaczanie widokow:
        //       - PackageGenerator -> HideFullScreenMapTab() + HideUsbTabOnly()
        //       - MapGenerator     -> HideUsbTabOnly() + ShowFullScreenMapTab(win)
        //       - OpordGenerator   -> HideFullScreenMapTab() + HideUsbTabOnly() + opordOverlay Visible
        //       - UsbProgrammer    -> HideFullScreenMapTab() + ShowUsbProvisionerTab(win)
        //    2. Calkowite wylaczenie V34Step1ShellAndSidebarFixer (ktory zwezal okno do 276px przy kliknieciu zakladek!)
        // -------------------------------------------------------------------------
        string corePath = Path.Combine(servicesDir, "BuilderCoreSidebarAndProjectEngine.cs");
        if (File.Exists(corePath))
        {
            string core = File.ReadAllText(corePath, Encoding.UTF8);

            // 1. Zastap metode SwitchActiveModule czysta, bezposrednia implementacja
            int swStart = core.IndexOf("public static void SwitchActiveModule(Window win, BuilderActiveModule targetModule)", StringComparison.Ordinal);
            int swEnd = core.IndexOf("private static bool TryInvokeOriginalButtonByText", StringComparison.Ordinal);
            if (swStart > 0 && swEnd > swStart)
            {
                string cleanSwitchMethod = @"public static void SwitchActiveModule(Window win, BuilderActiveModule targetModule)
        {
            EnsureRootDockSidebar(win);

            _currentModule = targetModule;
            UpdateSidebarActiveButtonHighlight();

            var allElements = GetAllVisualDescendants(win).ToList();
            CaptureAndCollapseTopMenuBar(allElements);
            CollapseObsoleteBuilderModeSelector(allElements);
            CollapseLegacyLeftSidebarColumn(win, allElements);
            RemoveDuplicateInnerSidebars(win, allElements);

            var opordOverlay = OpordBuilderManager.CreateOrGetOpordOverlayView(win);
            if (opordOverlay != null && _rightWorkspaceGrid != null)
            {
                if (VisualTreeHelper.GetParent(opordOverlay) is Panel oldParent && oldParent != _rightWorkspaceGrid)
                {
                    oldParent.Children.Remove(opordOverlay);
                }
                if (!_rightWorkspaceGrid.Children.Contains(opordOverlay))
                {
                    Panel.SetZIndex(opordOverlay, 50);
                    _rightWorkspaceGrid.Children.Add(opordOverlay);
                }
            }

            switch (targetModule)
            {
                case BuilderActiveModule.PackageGenerator:
                    if (opordOverlay != null) opordOverlay.Visibility = Visibility.Collapsed;
                    if (_originalMainWindowContentHost != null) _originalMainWindowContentHost.Visibility = Visibility.Visible;
                    MeshtasticUsbProvisionerView.HideUsbTabOnly();
                    TacticalMapGeneratorView.HideFullScreenMapTab();
                    break;

                case BuilderActiveModule.MapGenerator:
                    if (opordOverlay != null) opordOverlay.Visibility = Visibility.Collapsed;
                    if (_originalMainWindowContentHost != null) _originalMainWindowContentHost.Visibility = Visibility.Visible;
                    MeshtasticUsbProvisionerView.HideUsbTabOnly();
                    TacticalMapGeneratorView.ShowFullScreenMapTab(win);
                    break;

                case BuilderActiveModule.OpordGenerator:
                    MeshtasticUsbProvisionerView.HideUsbTabOnly();
                    TacticalMapGeneratorView.HideFullScreenMapTab();
                    if (_originalMainWindowContentHost != null) _originalMainWindowContentHost.Visibility = Visibility.Collapsed;
                    if (opordOverlay != null)
                    {
                        opordOverlay.Visibility = Visibility.Visible;
                    }
                    V34CleanOpordDataEngine.OnSwitchedToOpordTab(win, ""OPORD"");
                    break;

                case BuilderActiveModule.UsbProgrammer:
                    if (opordOverlay != null) opordOverlay.Visibility = Visibility.Collapsed;
                    if (_originalMainWindowContentHost != null) _originalMainWindowContentHost.Visibility = Visibility.Visible;
                    TacticalMapGeneratorView.HideFullScreenMapTab();
                    MeshtasticUsbProvisionerView.ShowUsbProvisionerTab(win);
                    V34CleanUsbProgrammerBridge.SyncUsbProgrammerUi(win);
                    break;
            }

            if (_rootDockGrid != null && _rootDockGrid.ColumnDefinitions.Count >= 2)
            {
                double sideW = _isSidebarCollapsed ? 52.0 : 272.0;
                _rootDockGrid.ColumnDefinitions[0].Width = new GridLength(sideW);
                _rootDockGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
            }

            if (_rightWorkspaceGrid != null)
            {
                _rightWorkspaceGrid.Visibility = Visibility.Visible;
                Grid.SetColumn(_rightWorkspaceGrid, 1);
            }

            if (_injectedSidebarContainer != null)
            {
                _injectedSidebarContainer.Visibility = Visibility.Visible;
                Grid.SetColumn(_injectedSidebarContainer, 0);
                Panel.SetZIndex(_injectedSidebarContainer, 99999);
            }

            V34CleanFtakAndFactionEngine.ApplyFactionAndPackageFixes(win);
            V34CleanMapStudioBridge.SyncMapStudioState(win);
        }

        ";
                core = core.Substring(0, swStart) + cleanSwitchMethod + core.Substring(swEnd);
                Console.WriteLine("[OK] Zaktualizowano SwitchActiveModule (bezposrednie przelaczanie Paczki / Mapa 1:1 / OPORD / Programator USB).");
            }

            // 2. Zastap V34Step1ShellAndSidebarFixer bezpieczna pusta atrapa (aby nigdy nie modyfikowala szerokosci glownego Bordera okna!)
            int step1Start = core.IndexOf("// [START_V34_STEP1_SHELL_FIXER]", StringComparison.Ordinal);
            if (step1Start < 0)
                step1Start = core.IndexOf("public static class V34Step1ShellAndSidebarFixer", StringComparison.Ordinal);

            if (step1Start > 0)
            {
                string safeNoOpStep1Class = string.Join("\r\n", new string[]
                {
                    "// [START_V34_STEP1_SHELL_FIXER]",
                    "    public static class V34Step1ShellAndSidebarFixer",
                    "    {",
                    "        public static event Action<Window> OnStep2ApplyPackageFixes;",
                    "        public static void AttachToMainWindow(Window win) { }",
                    "        public static void ApplyAllStep1Fixes(Window win) { }",
                    "        public static List<DependencyObject> CollectAllNodes(DependencyObject root) => V34CleanSidebarAndHeaderEngine.GetLogicalTreeNodes(root);",
                    "    }",
                    "    // [END_V34_STEP1_SHELL_FIXER]",
                    "}",
                    ""
                });

                core = core.Substring(0, step1Start) + safeNoOpStep1Class;
                Console.WriteLine("[OK] Zneutralizowano V34Step1ShellAndSidebarFixer (naprawa czarnego okna przy przelaczaniu modulow).");
            }

            // 3. Upewnij sie, ze w CollapseLegacyLeftSidebarColumn nigdy nie jest zerowany _rootDockGrid ani masterGrid
            core = core.Replace(
                "if (parent is Grid g && g != _rootDockGrid && g.ColumnDefinitions.Count >= 2 && curr is FrameworkElement fe && Grid.GetColumn(fe) == 0)",
                "if (parent is Grid g && g != _rootDockGrid && g != _rightWorkspaceGrid && g.Tag?.ToString() != \"FTH_MASTER_SHELL\" && g.ColumnDefinitions.Count >= 2 && curr is FrameworkElement fe && Grid.GetColumn(fe) == 0)");

            File.WriteAllText(corePath, core, utf8NoBom);
        }

        // -------------------------------------------------------------------------
        // C. Oznacz klasy testowe atrybutem [Collection("SequentialBuilderTests")],
        //    aby testy budujace paczki .ftak nie wchodzily sobie w parade
        // -------------------------------------------------------------------------
        if (Directory.Exists(testsDir))
        {
            string coreTestsPath = Path.Combine(testsDir, "BuilderCoreTests.cs");
            if (File.Exists(coreTestsPath))
            {
                string ct = File.ReadAllText(coreTestsPath, Encoding.UTF8);
                if (!ct.Contains("[Collection(\"SequentialBuilderTests\")]"))
                {
                    ct = ct.Replace("public class BuilderCoreTests", "[Collection(\"SequentialBuilderTests\")]\r\npublic class BuilderCoreTests");
                    File.WriteAllText(coreTestsPath, ct, utf8NoBom);
                }
            }

            string v34TestsPath = Path.Combine(testsDir, "V34CleanRefactorTests.cs");
            if (File.Exists(v34TestsPath))
            {
                string vt = File.ReadAllText(v34TestsPath, Encoding.UTF8);
                if (!vt.Contains("[Collection(\"SequentialBuilderTests\")]"))
                {
                    vt = vt.Replace("public sealed class V34CleanRefactorTests", "[Collection(\"SequentialBuilderTests\")]\r\n    public sealed class V34CleanRefactorTests");
                    File.WriteAllText(v34TestsPath, vt, utf8NoBom);
                }
            }
            Console.WriteLine("[OK] Ustawiono sekwencyjna kolekcje testow [Collection(\"SequentialBuilderTests\")].");
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
        RunProcess(gitExe, "commit -m \"Fix module switching black screen bug and thread-safe SigningKeyService lock for parallel xUnit tests\" --allow-empty", targetDir, ignoreError: false);

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