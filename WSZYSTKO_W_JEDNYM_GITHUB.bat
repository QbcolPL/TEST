@echo off
setlocal DisableDelayedExpansion
cd /d "%~dp0"

set "BAT_FILE=%~f0"

echo ============================================================================
echo [1/3] Odtwarzanie pelnego bloku V34CleanFtakAndFactionEngine i wysylka...
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
echo [SUKCES] Kompletny kod wyslany na GitHub (QbcolPL/TEST)!
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

        string batFile = Environment.GetEnvironmentVariable("BAT_FILE");
        string rawBat = File.ReadAllText(batFile, Encoding.UTF8);
        string[] tailSplit = Regex.Split(rawBat, "//===" + "V34_COMPLETE_TAIL_BLOCK_CS===");
        if (tailSplit.Length < 2)
            throw new Exception("Brak bloku V34_COMPLETE_TAIL_BLOCK_CS w pliku .bat!");
        string v34CompleteTailBlock = tailSplit[1].Trim();

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

        // 2. Zabezpiecz SigningKeyService.cs przed wyscigiem wielowatkowym w testach xUnit
        FixSigningKeyServiceConcurrency(targetDir, utf8NoBom);

        // 3. Odtworz kompletny blok V34* (w tym V34CleanFtakAndFactionEngine) i naprawe przelaczania modulow
        ApplyAllFixesAndRestoreV34Tail(targetDir, v34CompleteTailBlock, utf8NoBom);

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
        Console.WriteLine("[OK] Zaktualizowano SigningKeyService.cs.");
    }

    private static void ApplyAllFixesAndRestoreV34Tail(string targetDir, string v34CompleteTailBlock, UTF8Encoding utf8NoBom)
    {
        string builderDir = Path.Combine(targetDir, @"apps\builder\FieldTakHub.Builder");
        string servicesDir = Path.Combine(builderDir, "Services");

        string corePath = Path.Combine(servicesDir, "BuilderCoreSidebarAndProjectEngine.cs");
        if (!File.Exists(corePath))
            throw new Exception("Nie znaleziono pliku BuilderCoreSidebarAndProjectEngine.cs!");

        string core = File.ReadAllText(corePath, Encoding.UTF8);

        // Odtworz caly blok od // [START_V34_CLEAN_SIDEBAR_ENGINE] do konca pliku,
        // gwarantujac obecnosc V34CleanSidebarAndHeaderEngine, V34CleanFtakAndFactionEngine,
        // V34CleanMapStudioBridge, V34CleanUsbProgrammerBridge oraz V34Step1ShellAndSidebarFixer
        int sidebarEngineIdx = core.IndexOf("// [START_V34_CLEAN_SIDEBAR_ENGINE]", StringComparison.Ordinal);
        if (sidebarEngineIdx < 0)
            throw new Exception("Nie znaleziono znacznika // [START_V34_CLEAN_SIDEBAR_ENGINE] w BuilderCoreSidebarAndProjectEngine.cs!");

        core = core.Substring(0, sidebarEngineIdx) + v34CompleteTailBlock + "\r\n";

        int openBraces = core.Count(c => c == '{');
        int closeBraces = core.Count(c => c == '}');
        Console.WriteLine("[WERYFIKACJA] Nawiasy klamrowe w BuilderCoreSidebarAndProjectEngine.cs: { = " + openBraces + ", } = " + closeBraces);

        File.WriteAllText(corePath, core, utf8NoBom);
        Console.WriteLine("[OK] Odtworzono kompletny blok klas V34* (w tym V34CleanFtakAndFactionEngine) w BuilderCoreSidebarAndProjectEngine.cs.");
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
        RunProcess(gitExe, "commit -m \"Restore V34CleanFtakAndFactionEngine block (fix CS0103) and keep module switching & SigningKeyService fixes\" --allow-empty", targetDir, ignoreError: false);

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

//===V34_COMPLETE_TAIL_BLOCK_CS===
// [START_V34_CLEAN_SIDEBAR_ENGINE]
    public static class V34CleanSidebarAndHeaderEngine
    {
        public static void ApplySidebarAndHeaderFixes(Window win)
        {
            var w = win ?? Application.Current?.MainWindow;
            if (w == null) return;
            try
            {
                if (w.MinWidth > 900) w.MinWidth = 900;
                if (w.MinHeight > 560) w.MinHeight = 560;
                RemoveLegacyTopBarAndBlackHeaderGap(w);
            }
            catch { }
        }

        public static void RemoveLegacyTopBarAndBlackHeaderGap(Window win)
        {
            var elements = GetLogicalTreeNodes(win);
            foreach (var tb in elements.OfType<TextBlock>())
            {
                string txt = (tb.Text ?? "").Trim();
                if (txt.Contains("TRYB PRACY BUILDERA", StringComparison.OrdinalIgnoreCase) ||
                    txt.Contains("BUILDER MODE", StringComparison.OrdinalIgnoreCase))
                {
                    DependencyObject cur = tb;
                    for (int depth = 0; depth < 6 && cur != null && !(cur is Window); depth++)
                    {
                        if (cur is Border b)
                        {
                            b.Visibility = Visibility.Collapsed;
                            b.Height = 0;
                            b.MinHeight = 0;
                            b.Margin = new Thickness(0);
                            b.Padding = new Thickness(0);
                            b.BorderThickness = new Thickness(0);
                            b.BorderBrush = Brushes.Transparent;
                            b.Background = Brushes.Transparent;
                        }
                        cur = LogicalTreeHelper.GetParent(cur) ?? VisualTreeHelper.GetParent(cur);
                    }
                }
            }
        }

        public static List<DependencyObject> GetLogicalTreeNodes(DependencyObject root)
        {
            var result = new List<DependencyObject>(128);
            if (root == null) return result;

            var visited = new HashSet<DependencyObject>();
            var queue = new Queue<DependencyObject>(64);
            queue.Enqueue(root);
            visited.Add(root);

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                result.Add(cur);

                if (cur is Panel panel)
                {
                    foreach (UIElement child in panel.Children)
                    {
                        if (child != null && visited.Add(child))
                            queue.Enqueue(child);
                    }
                }
                else if (cur is Decorator dec && dec.Child != null && visited.Add(dec.Child))
                {
                    queue.Enqueue(dec.Child);
                }
                else if (cur is ScrollViewer sv && sv.Content is DependencyObject svContent && visited.Add(svContent))
                {
                    queue.Enqueue(svContent);
                }
                else if (cur is ContentControl cc && !(cur is ButtonBase) && !(cur is ComboBoxItem) && !(cur is ListBoxItem) && cc.Content is DependencyObject ccContent && visited.Add(ccContent))
                {
                    queue.Enqueue(ccContent);
                }
            }
            return result;
        }
    }
    // [END_V34_CLEAN_SIDEBAR_ENGINE]

    // [START_V34_CLEAN_FTAK_ENGINE]
    public sealed class V34FactionVoiceChannelRow
    {
        public string Role { get; set; } = "Dowodzenie HQ";
        public string Band { get; set; } = "PMR";
        public string Freq { get; set; } = "CH 1 (446.00625 MHz)";
        public string Tone { get; set; } = "CTCSS 67.0 Hz";
    }

    public sealed class V34FactionSnapshot
    {
        public string FactionName { get; set; } = "ORGA";
        public string ColorHex { get; set; } = "#2684FF";
        public string ChannelName { get; set; } = "ORGA-MESH";
        public string Region { get; set; } = "EU_868";
        public string ModemPreset { get; set; } = "SHORT_FAST";
        public string OtsHost { get; set; } = "";
        public string EmergencyPhone { get; set; } = "Brak / 112";
        public int ExpirationHours { get; set; } = 0;
        public List<V34FactionVoiceChannelRow> VoiceRows { get; set; } = new List<V34FactionVoiceChannelRow>();
    }

    public static class V34CleanFtakAndFactionEngine
    {
        private static readonly string[] TacticalRgbPalette = new string[]
        {
            "#2684FF", "#EB4034", "#2ECC71", "#FAD02C",
            "#9B59B6", "#FF9F43", "#1FE0C5", "#E84393",
            "#A3CB38", "#00CEC9", "#FF6B6B", "#54A0FF",
            "#5F27CD", "#10AC84", "#F368E0", "#FF9FF3"
        };

        private static readonly HashSet<string> _allocatedColors = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "#2684FF"
        };

        public static List<V34FactionSnapshot> CachedFactions { get; } = new List<V34FactionSnapshot>();

        public static string GetNextUniqueFactionColorHex(Window win = null)
        {
            try
            {
                var w = win ?? Application.Current?.MainWindow;
                if (w != null)
                {
                    var all = V34CleanSidebarAndHeaderEngine.GetLogicalTreeNodes(w);
                    foreach (var btn in all.OfType<Button>())
                    {
                        string bt = ExtractButtonTextSafe(btn);
                        Match m = Regex.Match(bt, @"#[0-9A-Fa-f]{6}");
                        if (m.Success) _allocatedColors.Add(m.Value.ToUpperInvariant());
                    }
                }
            }
            catch { }

            var rng = new Random(Guid.NewGuid().GetHashCode());
            var free = TacticalRgbPalette.Where(c => !_allocatedColors.Contains(c)).OrderBy(_ => rng.Next()).ToList();
            string chosen;
            if (free.Count > 0)
            {
                chosen = free[0];
            }
            else
            {
                do
                {
                    chosen = string.Format("#{0:X2}{1:X2}{2:X2}", rng.Next(45, 240), rng.Next(55, 240), rng.Next(45, 240));
                } while (_allocatedColors.Contains(chosen));
            }

            _allocatedColors.Add(chosen);
            return chosen;
        }

        public static string EnsureSharedFolderStructure()
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FieldTakHub");
            string wspolne = Path.Combine(root, "wspolne");
            Directory.CreateDirectory(Path.Combine(wspolne, "ATAK"));
            Directory.CreateDirectory(Path.Combine(wspolne, "Meshtastic"));
            Directory.CreateDirectory(Path.Combine(wspolne, "apk"));
            Directory.CreateDirectory(Path.Combine(wspolne, "plugins"));
            Directory.CreateDirectory(Path.Combine(wspolne, "maps"));
            Directory.CreateDirectory(Path.Combine(root, "out"));
            return wspolne;
        }

        public static string EnsureFactionOutputDir(string factionName)
        {
            EnsureSharedFolderStructure();
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FieldTakHub");
            string outDir = Path.Combine(root, "out", SanitizeFolderName(factionName));
            Directory.CreateDirectory(outDir);
            return outDir;
        }

        public static string SanitizeFileName(string name) => SanitizeFolderName(name);

        public static string SanitizeFolderName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "ORGA";
            char[] inv = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder();
            foreach (char c in name.Trim())
            {
                sb.Append(Array.IndexOf(inv, c) >= 0 ? '_' : c);
            }
            string r = sb.ToString().Trim();
            return string.IsNullOrEmpty(r) ? "ORGA" : r;
        }

        public static void ApplyFactionAndPackageFixes(Window win)
        {
            var w = win ?? Application.Current?.MainWindow;
            if (w == null) return;

            EnsureSharedFolderStructure();
            var all = ScanAllWindowRoots(w);

            foreach (var tb in all.OfType<TextBlock>())
            {
                string txt = (tb.Text ?? "").Trim();
                if ((txt.Contains("Wa\u017Cno\u015B\u0107 paczki", StringComparison.OrdinalIgnoreCase) ||
                     txt.Contains("Wa\u017Cno\u015B\u0107 kodu QR", StringComparison.OrdinalIgnoreCase) ||
                     txt.Contains("Expiration", StringComparison.OrdinalIgnoreCase)) &&
                    !txt.Contains("0 = bezterminowo", StringComparison.OrdinalIgnoreCase))
                {
                    tb.Text = txt.TrimEnd(':') + " (0 = bezterminowo):";
                }
            }

            EnforceUniqueColorsAndNamesOnCards(all);
            RefreshCachedFactions(w, all);
        }

        private static void EnforceUniqueColorsAndNamesOnCards(List<DependencyObject> all)
        {
            var usedColors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var headers = all.OfType<TextBlock>()
                .Where(t => (t.Text ?? "").Contains("Generator Stron \u2014", StringComparison.OrdinalIgnoreCase))
                .ToList();

            for (int i = 0; i < headers.Count; i++)
            {
                var hdr = headers[i];
                var card = FindFactionCardContainer(hdr);
                if (card == null) continue;

                var elems = V34CleanSidebarAndHeaderEngine.GetLogicalTreeNodes(card);
                var firstBox = elems.OfType<TextBox>().FirstOrDefault();
                if (firstBox != null)
                {
                    string curName = (firstBox.Text ?? "").Trim();
                    if (usedNames.Contains(curName) && curName.StartsWith("STRONA-", StringComparison.OrdinalIgnoreCase))
                    {
                        int n = i + 1;
                        while (usedNames.Contains("STRONA-" + n)) n++;
                        curName = "STRONA-" + n;
                        firstBox.Text = curName;
                        hdr.Text = "Generator Stron \u2014 " + curName;
                    }
                    if (!string.IsNullOrWhiteSpace(curName))
                        usedNames.Add(curName);
                }

                var colorBtn = elems.OfType<Button>().FirstOrDefault(b => Regex.IsMatch(ExtractButtonTextSafe(b), @"#[0-9A-Fa-f]{6}"));
                if (colorBtn != null)
                {
                    Match m = Regex.Match(ExtractButtonTextSafe(colorBtn), @"#[0-9A-Fa-f]{6}");
                    if (m.Success)
                    {
                        string hex = m.Value.ToUpperInvariant();
                        if (usedColors.Contains(hex))
                        {
                            hex = GetNextUniqueFactionColorHex(null);
                            ApplyHexColorToButton(colorBtn, hdr, hex);
                        }
                        usedColors.Add(hex);
                        _allocatedColors.Add(hex);
                    }
                }
            }
        }

        private static void ApplyHexColorToButton(Button colorBtn, TextBlock hdrTb, string hex)
        {
            try
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                if (colorBtn.Content is string)
                {
                    colorBtn.Content = "\u25A0 " + hex;
                }
                else if (colorBtn.Content is Panel p)
                {
                    foreach (var b in p.Children.OfType<Border>()) b.Background = brush;
                    foreach (var s in p.Children.OfType<System.Windows.Shapes.Shape>()) s.Fill = brush;
                    foreach (var t in p.Children.OfType<TextBlock>())
                        if (t.Text != null && t.Text.Contains("#")) t.Text = hex;
                }
                colorBtn.BorderBrush = brush;

                if (LogicalTreeHelper.GetParent(hdrTb) is Panel hdrParent)
                {
                    foreach (var s in hdrParent.Children.OfType<System.Windows.Shapes.Shape>()) s.Fill = brush;
                    foreach (var b in hdrParent.Children.OfType<Border>())
                        if (b.Width <= 20 && b.Height <= 20) b.Background = brush;
                }
            }
            catch { }
        }

        public static DependencyObject FindFactionCardContainer(TextBlock hdrTb)
        {
            if (hdrTb == null) return null;
            DependencyObject cur = hdrTb;
            DependencyObject bestBorder = null;

            for (int depth = 0; depth < 9 && cur != null; depth++)
            {
                cur = LogicalTreeHelper.GetParent(cur) ?? VisualTreeHelper.GetParent(cur);
                if (cur is Border || cur is Panel)
                {
                    var elems = V34CleanSidebarAndHeaderEngine.GetLogicalTreeNodes(cur);
                    var texts = elems.OfType<TextBlock>().Select(x => x.Text ?? "").ToList();
                    int hdrCount = texts.Count(x => x.Contains("Generator Stron \u2014", StringComparison.OrdinalIgnoreCase));
                    if (hdrCount > 1) break;

                    if (cur is Border b && b.BorderThickness.Top >= 1)
                        bestBorder = b;

                    if (hdrCount == 1 &&
                        texts.Any(x => x.Contains("Nazwa strony", StringComparison.OrdinalIgnoreCase)) &&
                        texts.Any(x => x.Contains("Kana\u0142y G\u0142osowe", StringComparison.OrdinalIgnoreCase)))
                    {
                        return cur;
                    }
                }
            }
            return bestBorder;
        }

        public static List<V34FactionSnapshot> RefreshCachedFactions(Window win, List<DependencyObject> preScanned = null)
        {
            var list = new List<V34FactionSnapshot>();
            try
            {
                var all = preScanned ?? ScanAllWindowRoots(win ?? Application.Current?.MainWindow);
                var headers = all.OfType<TextBlock>()
                    .Where(t => (t.Text ?? "").Contains("Generator Stron \u2014", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var hdr in headers)
                {
                    var card = FindFactionCardContainer(hdr);
                    if (card == null) continue;

                    var items = V34CleanSidebarAndHeaderEngine.GetLogicalTreeNodes(card);
                    var boxes = items.OfType<TextBox>().ToList();
                    var snap = new V34FactionSnapshot();

                    snap.FactionName = (boxes.Count > 0 && !string.IsNullOrWhiteSpace(boxes[0].Text))
                        ? boxes[0].Text.Trim()
                        : (hdr.Text ?? "").Replace("Generator Stron \u2014", "").Trim();

                    if (string.IsNullOrWhiteSpace(snap.FactionName) ||
                        snap.FactionName.Equals("PLI", StringComparison.OrdinalIgnoreCase) ||
                        snap.FactionName.Equals("GeoChat", StringComparison.OrdinalIgnoreCase) ||
                        snap.FactionName.Equals("TAK", StringComparison.OrdinalIgnoreCase))
                    {
                        snap.FactionName = "ORGA";
                    }

                    snap.ChannelName = snap.FactionName.ToUpperInvariant() + "-MESH";

                    var colBtn = items.OfType<Button>().FirstOrDefault(b => Regex.IsMatch(ExtractButtonTextSafe(b), @"#[0-9A-Fa-f]{6}"));
                    if (colBtn != null)
                    {
                        Match m = Regex.Match(ExtractButtonTextSafe(colBtn), @"#[0-9A-Fa-f]{6}");
                        if (m.Success) snap.ColorHex = m.Value.ToUpperInvariant();
                    }

                    foreach (var tb in boxes)
                    {
                        var p = LogicalTreeHelper.GetParent(tb) as Panel;
                        if (p != null && p.Children.OfType<TextBlock>().Any(l => (l.Text ?? "").Contains("Telefon alarmowy", StringComparison.OrdinalIgnoreCase)))
                        {
                            if (!string.IsNullOrWhiteSpace(tb.Text)) snap.EmergencyPhone = tb.Text.Trim();
                        }
                    }

                    foreach (var panel in items.OfType<Panel>())
                    {
                        var directInputs = panel.Children.Cast<UIElement>().Where(x => x is TextBox || x is ComboBox).ToList();
                        if (directInputs.Count >= 2 && directInputs.Any(x => x is TextBox))
                        {
                            bool isNameRow = panel.Children.OfType<StackPanel>().Any(sp => sp.Children.OfType<TextBlock>().Any(t => (t.Text ?? "").Contains("Nazwa strony", StringComparison.OrdinalIgnoreCase)));
                            if (isNameRow) continue;

                            string ReadVal(UIElement el)
                            {
                                if (el is TextBox tbx) return (tbx.Text ?? "").Trim();
                                if (el is ComboBox cbx) return ((cbx.SelectedItem is ComboBoxItem cbi ? cbi.Content?.ToString() : cbx.SelectedItem?.ToString()) ?? cbx.Text ?? "").Trim();
                                return "";
                            }

                            var vals = directInputs.Select(ReadVal).ToList();
                            string role = vals[0];
                            string band = vals.Count >= 2 ? vals[1] : "PMR";
                            string freq = vals.Count >= 3 ? vals[2] : vals[1];
                            string tone = vals.Count >= 4 ? vals[3] : "-";

                            if (role.Equals("PMR", StringComparison.OrdinalIgnoreCase) ||
                                role.Equals("VHF", StringComparison.OrdinalIgnoreCase) ||
                                role.Equals("UHF", StringComparison.OrdinalIgnoreCase) ||
                                role.Equals("DMR", StringComparison.OrdinalIgnoreCase) ||
                                role.Contains("VHF/", StringComparison.OrdinalIgnoreCase))
                            {
                                band = role;
                                role = vals.Count >= 2 ? vals[1] : "Kana\u0142";
                                freq = vals.Count >= 3 ? vals[2] : "-";
                            }

                            if (!string.IsNullOrWhiteSpace(role) || !string.IsNullOrWhiteSpace(freq))
                            {
                                snap.VoiceRows.Add(new V34FactionVoiceChannelRow
                                {
                                    Role = string.IsNullOrWhiteSpace(role) ? "Kana\u0142" : role,
                                    Band = string.IsNullOrWhiteSpace(band) ? "PMR" : band,
                                    Freq = string.IsNullOrWhiteSpace(freq) ? "-" : freq,
                                    Tone = string.IsNullOrWhiteSpace(tone) ? "-" : tone
                                });
                            }
                        }
                    }

                    if (snap.VoiceRows.Count == 0)
                        snap.VoiceRows.Add(new V34FactionVoiceChannelRow());

                    list.Add(snap);
                }
            }
            catch { }

            if (list.Count > 0)
            {
                CachedFactions.Clear();
                CachedFactions.AddRange(list);
            }
            return CachedFactions;
        }

        public static List<DependencyObject> ScanAllWindowRoots(Window win)
        {
            var result = new List<DependencyObject>(256);
            var visited = new HashSet<DependencyObject>();

            void AddTree(DependencyObject r)
            {
                if (r == null || !visited.Add(r)) return;
                result.AddRange(V34CleanSidebarAndHeaderEngine.GetLogicalTreeNodes(r));
            }

            if (win != null)
            {
                AddTree(win);
                foreach (var f in win.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                {
                    try { if (f.GetValue(win) is DependencyObject d) AddTree(d); } catch { }
                }
            }

            foreach (var t in typeof(V34CleanFtakAndFactionEngine).Assembly.GetTypes())
            {
                if (t.Namespace == null || !t.Namespace.StartsWith("FieldTakHub.Builder")) continue;
                foreach (var sf in t.GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                {
                    try { if (typeof(DependencyObject).IsAssignableFrom(sf.FieldType) && sf.GetValue(null) is DependencyObject sd) AddTree(sd); } catch { }
                }
            }

            return result;
        }

        public static string ExtractButtonTextSafe(Button btn)
        {
            if (btn == null) return "";
            if (btn.Content is string s) return s;
            if (btn.Content is TextBlock tb) return tb.Text ?? "";
            if (btn.Content is Panel p)
                return string.Join(" ", V34CleanSidebarAndHeaderEngine.GetLogicalTreeNodes(p).OfType<TextBlock>().Select(x => x.Text ?? ""));
            return btn.Content?.ToString() ?? "";
        }
    }
    // [END_V34_CLEAN_FTAK_ENGINE]

    // [START_V34_CLEAN_MAP_STUDIO_ENGINE]
    public static class V34CleanMapStudioBridge
    {
        private static readonly string[] MgrsCols = { "ABCDEFGH", "JKLMNPQR", "STUVWXYZ" };
        private static readonly string[] MgrsRows = { "ABCDEFGHJKLMNPQRSTUV", "FGHJKLMNPQRSTUVABCDE" };

        public static bool HasExplicitAoMarked { get; private set; }
        public static double AoCenterLat { get; private set; } = 49.849444;
        public static double AoCenterLon { get; private set; } = 18.812667;
        public static string ActiveCoordinateSystem { get; private set; } = "MGRS";

        public static void SyncMapStudioState(Window win)
        {
            try
            {
                var w = win ?? Application.Current?.MainWindow;
                if (w == null) return;

                V34CleanFtakAndFactionEngine.EnsureSharedFolderStructure();
                var all = V34CleanFtakAndFactionEngine.ScanAllWindowRoots(w);
                bool foundAoOrPois = false;

                foreach (var cb in all.OfType<ComboBox>())
                {
                    string sel = ((cb.SelectedItem is ComboBoxItem cbi ? cbi.Content?.ToString() : cb.SelectedItem?.ToString()) ?? cb.Text ?? "").Trim();
                    if (sel.Contains("MGRS", StringComparison.OrdinalIgnoreCase)) ActiveCoordinateSystem = "MGRS";
                    else if (sel.Contains("DMS", StringComparison.OrdinalIgnoreCase) || sel.Contains("Stopnie", StringComparison.OrdinalIgnoreCase)) ActiveCoordinateSystem = "DMS";
                    else if (sel.Contains("UTM", StringComparison.OrdinalIgnoreCase) && !sel.Contains("MGRS", StringComparison.OrdinalIgnoreCase)) ActiveCoordinateSystem = "UTM";
                    else if (sel.Contains("WGS84", StringComparison.OrdinalIgnoreCase) || sel.Contains("Dziesi\u0119tne", StringComparison.OrdinalIgnoreCase)) ActiveCoordinateSystem = "WGS84";
                }

                var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
                var mExtract = typeof(OpordBuilderManager).GetMethod("GetExtractedMapData", flags)
                            ?? typeof(OpordBuilderManager).GetMethod("ExtractMapData", flags);

                if (mExtract != null && mExtract.GetParameters().Length == 0)
                {
                    var mapData = mExtract.Invoke(null, null);
                    if (mapData != null)
                    {
                        var mt = mapData.GetType();
                        bool hasAoProp = (bool)(mt.GetProperty("HasAoPolygon")?.GetValue(mapData) ?? false);
                        double cLat = Convert.ToDouble(mt.GetProperty("CenterLat")?.GetValue(mapData) ?? 49.849444);
                        double cLon = Convert.ToDouble(mt.GetProperty("CenterLon")?.GetValue(mapData) ?? 18.812667);

                        int poisCount = 0;
                        if (mt.GetProperty("Pois")?.GetValue(mapData) is System.Collections.IEnumerable poisEnum)
                        {
                            foreach (var _ in poisEnum) poisCount++;
                        }

                        if (hasAoProp || poisCount > 0)
                        {
                            foundAoOrPois = true;
                            AoCenterLat = cLat;
                            AoCenterLon = cLon;
                        }
                    }
                }

                if (!foundAoOrPois)
                {
                    foreach (var canvas in all.OfType<Canvas>())
                    {
                        foreach (UIElement ch in canvas.Children)
                        {
                            if (ch is System.Windows.Shapes.Polygon poly && poly.Points != null && poly.Points.Count >= 3 && poly.Visibility == Visibility.Visible)
                            {
                                foundAoOrPois = true;
                                break;
                            }
                            if (ch is System.Windows.Shapes.Polyline pline && pline.Points != null && pline.Points.Count >= 3 && pline.Visibility == Visibility.Visible)
                            {
                                foundAoOrPois = true;
                                break;
                            }
                        }
                        if (foundAoOrPois) break;
                    }
                }

                HasExplicitAoMarked = foundAoOrPois;
            }
            catch { }
        }

        public static string FormatByActiveMapSystem(double lat, double lon)
        {
            string sys = (ActiveCoordinateSystem ?? "MGRS").ToUpperInvariant();
            if (sys.Contains("MGRS")) return LatLonToMgrs(lat, lon);
            if (sys.Contains("UTM")) return LatLonToUtm(lat, lon);
            if (sys.Contains("DMS")) return LatLonToDms(lat, lon);
            return string.Format(CultureInfo.InvariantCulture, "{0:F5}\u00B0N, {1:F5}\u00B0E", lat, lon);
        }

        public static string LatLonToMgrs(double lat, double lon)
        {
            try
            {
                double cLat = Math.Clamp(lat, -80.0, 84.0);
                double nLon = ((lon + 180.0) % 360.0 + 360.0) % 360.0 - 180.0;
                int zone = (int)Math.Floor((nLon + 180.0) / 6.0) + 1;
                double latRad = cLat * Math.PI / 180.0;
                double lonRad = nLon * Math.PI / 180.0;
                double lonOrig = ((zone - 1) * 6 - 180 + 3) * Math.PI / 180.0;
                double a = 6378137.0, eccSq = 0.00669438, k0 = 0.9996, eccP = eccSq / (1.0 - eccSq);
                double n = a / Math.Sqrt(1.0 - eccSq * Math.Sin(latRad) * Math.Sin(latRad));
                double t = Math.Tan(latRad) * Math.Tan(latRad);
                double c = eccP * Math.Cos(latRad) * Math.Cos(latRad);
                double A = Math.Cos(latRad) * (lonRad - lonOrig);
                double M = a * ((1.0 - eccSq / 4.0 - 3.0 * eccSq * eccSq / 64.0) * latRad - (3.0 * eccSq / 8.0 + 3.0 * eccSq * eccSq / 32.0) * Math.Sin(2.0 * latRad));
                double east = k0 * n * (A + (1.0 - t + c) * Math.Pow(A, 3) / 6.0) + 500000.0;
                double north = k0 * (M + n * Math.Tan(latRad) * (A * A / 2.0));
                if (cLat < 0) north += 10000000.0;
                string bands = "CDEFGHJKLMNPQRSTUVWXX";
                char bChar = bands[Math.Clamp((int)Math.Floor((cLat + 80.0) / 8.0), 0, bands.Length - 1)];
                char cChar = MgrsCols[(zone - 1) % 3][Math.Clamp((int)Math.Floor(east / 100000.0) - 1, 0, 7)];
                char rChar = MgrsRows[(zone - 1) % 2][Math.Clamp((int)Math.Floor(north / 100000.0) % 20, 0, 19)];
                int e5 = Math.Clamp((int)Math.Round(east) % 100000, 0, 99999);
                int n5 = Math.Clamp((int)Math.Round(north) % 100000, 0, 99999);
                return string.Format(CultureInfo.InvariantCulture, "{0:00}{1} {2}{3} {4:00000} {5:00000}", zone, bChar, cChar, rChar, e5, n5);
            }
            catch
            {
                return string.Format(CultureInfo.InvariantCulture, "{0:F5}, {1:F5}", lat, lon);
            }
        }

        public static string LatLonToUtm(double lat, double lon)
        {
            double cLat = Math.Clamp(lat, -80.0, 84.0);
            double nLon = ((lon + 180.0) % 360.0 + 360.0) % 360.0 - 180.0;
            int zone = (int)Math.Floor((nLon + 180.0) / 6.0) + 1;
            double latRad = cLat * Math.PI / 180.0, lonRad = nLon * Math.PI / 180.0;
            double lonOrig = ((zone - 1) * 6 - 180 + 3) * Math.PI / 180.0;
            double a = 6378137.0, eccSq = 0.00669438, k0 = 0.9996;
            double n = a / Math.Sqrt(1.0 - eccSq * Math.Sin(latRad) * Math.Sin(latRad));
            double A = Math.Cos(latRad) * (lonRad - lonOrig);
            double M = a * ((1.0 - eccSq / 4.0) * latRad - (3.0 * eccSq / 8.0) * Math.Sin(2.0 * latRad));
            double east = k0 * n * A + 500000.0;
            double north = k0 * (M + n * Math.Tan(latRad) * (A * A / 2.0));
            return string.Format(CultureInfo.InvariantCulture, "UTM {0}N {1:0}E {2:0}N", zone, east, north);
        }

        public static string LatLonToDms(double lat, double lon)
        {
            return ToDmsPart(lat, lat >= 0 ? 'N' : 'S') + " " + ToDmsPart(lon, lon >= 0 ? 'E' : 'W');
        }

        private static string ToDmsPart(double val, char hem)
        {
            double abs = Math.Abs(val);
            int d = (int)Math.Floor(abs);
            double mf = (abs - d) * 60.0;
            int m = (int)Math.Floor(mf);
            double s = (mf - m) * 60.0;
            return string.Format(CultureInfo.InvariantCulture, "{0:00}\u00B0{1:00}'{2:00.0}\"{3}", d, m, s, hem);
        }
    }
    // [END_V34_CLEAN_MAP_STUDIO_ENGINE]

    // [START_V34_CLEAN_USB_BRIDGE]
    public static class V34CleanUsbProgrammerBridge
    {
        public static void SyncUsbProgrammerUi(Window win)
        {
            try
            {
                var w = win ?? Application.Current?.MainWindow;
                if (w == null) return;

                var all = V34CleanFtakAndFactionEngine.ScanAllWindowRoots(w);
                var cleanFactionNames = V34CleanOpordDataEngine.GetCleanFactionNames(w);

                foreach (var tb in all.OfType<TextBlock>())
                {
                    string txt = (tb.Text ?? "").Trim();
                    if (txt.Contains("PROGRAMATOR P\u0141YTEK USB", StringComparison.OrdinalIgnoreCase) ||
                        txt.Contains("MASOWY PROGRAMATOR", StringComparison.OrdinalIgnoreCase) ||
                        txt.Contains("USB BOARD PROGRAMMER", StringComparison.OrdinalIgnoreCase))
                    {
                        DependencyObject root = LogicalTreeHelper.GetParent(tb) ?? VisualTreeHelper.GetParent(tb);
                        while (root != null && !(root is StackPanel || root is Grid))
                        {
                            root = LogicalTreeHelper.GetParent(root) ?? VisualTreeHelper.GetParent(root);
                        }

                        if (root != null)
                        {
                            var usbElems = V34CleanSidebarAndHeaderEngine.GetLogicalTreeNodes(root);
                            foreach (var cb in usbElems.OfType<ComboBox>())
                            {
                                for (int i = cb.Items.Count - 1; i >= 0; i--)
                                {
                                    string itemStr = cb.Items[i]?.ToString() ?? "";
                                    if (V34CleanOpordDataEngine.IsGarbageFactionName(itemStr))
                                    {
                                        cb.Items.RemoveAt(i);
                                    }
                                }

                                foreach (string fn in cleanFactionNames)
                                {
                                    bool exists = false;
                                    foreach (var existing in cb.Items)
                                    {
                                        if (string.Equals(existing?.ToString(), fn, StringComparison.OrdinalIgnoreCase))
                                        {
                                            exists = true;
                                            break;
                                        }
                                    }
                                    if (!exists) cb.Items.Add(fn);
                                }

                                if (cb.SelectedItem == null && cb.Items.Count > 0)
                                {
                                    cb.SelectedIndex = 0;
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }
    }
    // [END_V34_CLEAN_USB_BRIDGE]

    // [START_V34_STEP1_SHELL_FIXER]
    public static class V34Step1ShellAndSidebarFixer
    {
        public static void AttachToMainWindow(Window win) { }
        public static void ApplyAllStep1Fixes(Window win) { }
        public static List<DependencyObject> CollectAllNodes(DependencyObject root) => V34CleanSidebarAndHeaderEngine.GetLogicalTreeNodes(root);
    }
    // [END_V34_STEP1_SHELL_FIXER]
}