#pragma warning disable CS0414, CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8619, CS8625
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FieldTakHub.Builder.Services
{
    internal sealed class CleanVoiceChannelItem
    {
        public string RoleName { get; set; } = "";
        public string BandType { get; set; } = "PMR";
        public string FreqMhz { get; set; } = "";
        public string ToneOrCode { get; set; } = "";
    }

    internal sealed class RequiredApkRequirementModel
    {
        public string Role { get; set; } = "ATAK-CIV"; // ATAK-CIV | MESHTASTIC | PLUGIN
        public string PackageName { get; set; } = "com.atakmap.app.civ";
        public string DisplayName { get; set; } = "ATAK-CIV";
        public string RequiredVersion { get; set; } = "";
        public string BundledZipPath { get; set; } = "";
        public string Sha256Short { get; set; } = "";
        public long FileSizeBytes { get; set; }
    }

    internal sealed class CleanFactionProfileModel
    {
        public string Name { get; set; } = "BLUFOR";
        public string ColorHex { get; set; } = "#2684FF";
        public string ChannelName { get; set; } = "BLUFOR-MESH";
        public string Psk { get; set; } = "";
        public string ChannelUrl { get; set; } = "";
        public string LoraRegion { get; set; } = "EU_868";
        public string LoraModemPreset { get; set; } = "SHORT_FAST";
        public int HopLimit { get; set; } = 3;
        public string OtsHost { get; set; } = "";
        public string EnrollmentUri { get; set; } = "";
        public string EmergencyPhone { get; set; } = "";
        public string PliProfile { get; set; } = "ConstRateUnreliable";
        public string MeshProfile { get; set; } = "StationaryMesh";
        public List<CleanVoiceChannelItem> VoiceChannels { get; set; } = new List<CleanVoiceChannelItem>();
    }

    internal sealed class CleanWorkspaceSnapshot
    {
        public string EventName { get; set; } = "NOWA OPERACJA";
        public List<CleanFactionProfileModel> Factions { get; set; } = new List<CleanFactionProfileModel>();
    }

    internal static class CleanFactionPackageEngine
    {
        private static TcpListener _pcHotspotListener;
        private static volatile bool _pcServerRunning;
        private static int _pcServerPort = 8766;
        private static readonly Dictionary<string, string> ServedFtakPathsByFaction = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static Window _lastWinRef;

        // =========================================================================
        // 1. BUDOWANIE PACZEK .FTAK v3.0 (JEDNA STRONA = JEDEN FOLDER out/<STRONA>/)
        // =========================================================================
        public static async Task<string> BuildIsolatedFactionPackagesAsync(
            Window win,
            string onlyFactionName,
            bool showQrModalAfter,
            Action<string> onStatusUpdated = null)
        {
            _lastWinRef = win;
            try
            {
                // Upewnij się, że wspólny folder source/ ma strukturę (apk, plugins, datapackage, maps, certs, factions)
                BuilderCoreSidebarAndProjectEngine.EnsureUnifiedSourceDirectoryStructure(win);

                // Zapisz aktualny stan wszystkich rozkazów OPORD do folderów frakcji
                OpordBuilderManager.SaveAllOpordsToDisk(win);

                var snapshot = ExtractWorkspaceSnapshot(win);
                string eventName = snapshot.EventName;
                string sourceDir = TacticalConfigPanel.GetPrimarySourceDirectory(win);
                string outBaseDir = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(sourceDir)) ?? AppDomain.CurrentDomain.BaseDirectory;
                string outRootDir = Path.Combine(outBaseDir, "out");
                Directory.CreateDirectory(outRootDir);

                // Wyczyść wszystkie stare, niepotrzebne podfoldery w out/ (np. release_factions, temp, staging, work)
                PurgeLegacyOutputFolders(outRootDir, snapshot.Factions.Select(f => TacticalConfigPanel.SanitizeFileName(f.Name)).ToList());

                var targetFactions = snapshot.Factions;
                if (!string.IsNullOrWhiteSpace(onlyFactionName))
                {
                    var filtered = targetFactions
                        .Where(f => f.Name.Equals(onlyFactionName.Trim(), StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    if (filtered.Count > 0) targetFactions = filtered;
                }

                if (targetFactions.Count == 0)
                {
                    onStatusUpdated?.Invoke(BuilderI18n.T("⚠️ Brak skonfigurowanych stron konfliktu!", "⚠️ No factions configured!"));
                    return "";
                }

                string lastBuiltFtakPath = "";
                string lastBuiltFactionFolder = "";
                CleanFactionProfileModel lastBuiltFaction = null;
                int totalIncludedFiles = 0;
                int totalScannedApks = 0;

                await Task.Run(() =>
                {
                    foreach (var fac in targetFactions)
                    {
                        string safeEv = TacticalConfigPanel.SanitizeFileName(eventName);
                        string safeFac = TacticalConfigPanel.SanitizeFileName(fac.Name).ToUpperInvariant();

                        // ZASADA: JEDNA STRONA = JEDEN FOLDER WYJŚCIOWY -> out/<STRONA>/
                        string factionOutDir = Path.Combine(outRootDir, safeFac);
                        Directory.CreateDirectory(factionOutDir);

                        // Usuń stare pliki .ftak w folderze tej strony przed zapisem nowej wersji
                        foreach (string oldFtak in Directory.GetFiles(factionOutDir, "*.ftak", SearchOption.TopDirectoryOnly))
                        {
                            try { File.Delete(oldFtak); } catch { }
                        }

                        string ftakPath = Path.Combine(factionOutDir, safeEv + "_" + safeFac + ".ftak");

                        // Zapisz w tym samym folderze wyjściowym strony również samodzielny rozkaz OPORD (HTML + JSON)
                        var opordModel = OpordBuilderManager.GetOrCreateModel(fac.Name);
                        string opordHtml = OpordBuilderManager.GeneratePrintableOpordHtml(win, fac.Name, opordModel);
                        string opordJson = JsonSerializer.Serialize(opordModel, new JsonSerializerOptions { WriteIndented = true });
                        File.WriteAllText(Path.Combine(factionOutDir, "OPORD_" + safeFac + ".html"), opordHtml, Encoding.UTF8);
                        File.WriteAllText(Path.Combine(factionOutDir, "opord_" + safeFac.ToLowerInvariant() + ".json"), opordJson, Encoding.UTF8);

                        var buildRes = WriteSingleAuditedFactionFtakArchive(ftakPath, eventName, fac, sourceDir, opordModel, opordHtml, opordJson);
                        ServedFtakPathsByFaction[fac.Name] = ftakPath;
                        lastBuiltFtakPath = ftakPath;
                        lastBuiltFactionFolder = factionOutDir;
                        lastBuiltFaction = fac;
                        totalIncludedFiles += buildRes.AssetsCount;
                        totalScannedApks = buildRes.ApkRequirementsCount;
                    }
                });

                string serverBaseUrl = EnsurePcFtakHttpServerStarted();

                if (lastBuiltFaction != null)
                {
                    string otsLabel = string.IsNullOrWhiteSpace(lastBuiltFaction.OtsHost) ? "Off-Grid Mesh" : lastBuiltFaction.OtsHost;
                    string msg = BuilderI18n.T(
                        "✓ Zbudowano: out\\" + TacticalConfigPanel.SanitizeFileName(lastBuiltFaction.Name).ToUpperInvariant() + "\\" + Path.GetFileName(lastBuiltFtakPath) +
                        "  |  OTS: " + otsLabel + "  |  Kanały radiowe: " + lastBuiltFaction.VoiceChannels.Count +
                        "  |  Zasoby (Mapy/OPORD/DataPkg): " + totalIncludedFiles + "  |  Profil wersji APK: " + totalScannedApks,
                        "✓ Built: out\\" + TacticalConfigPanel.SanitizeFileName(lastBuiltFaction.Name).ToUpperInvariant() + "\\" + Path.GetFileName(lastBuiltFtakPath) +
                        "  |  OTS: " + otsLabel + "  |  Voice channels: " + lastBuiltFaction.VoiceChannels.Count +
                        "  |  Assets (Maps/OPORD/DataPkg): " + totalIncludedFiles + "  |  APK version profiles: " + totalScannedApks);
                    onStatusUpdated?.Invoke(msg);
                }

                if (showQrModalAfter && lastBuiltFaction != null)
                {
                    ShowActiveFactionQrModal(win, eventName, lastBuiltFaction, lastBuiltFtakPath, serverBaseUrl);
                }
                else if (!string.IsNullOrEmpty(lastBuiltFactionFolder) && Directory.Exists(lastBuiltFactionFolder))
                {
                    TacticalConfigPanel.TryOpenFolderInExplorer(lastBuiltFactionFolder);
                }

                return lastBuiltFtakPath;
            }
            catch (Exception ex)
            {
                onStatusUpdated?.Invoke("⚠️ Błąd budowy .FTAK: " + ex.Message);
                return "";
            }
        }

        private static void PurgeLegacyOutputFolders(string outRootDir, List<string> validFactionFolderNames)
        {
            try
            {
                if (!Directory.Exists(outRootDir)) return;
                string[] legacyNames = new string[]
                {
                    "release_factions", "temp", "staging", "work", "cache", "legacy", "bundles", "packages", "default"
                };

                foreach (string subDir in Directory.GetDirectories(outRootDir, "*", SearchOption.TopDirectoryOnly))
                {
                    string dirName = Path.GetFileName(subDir);
                    if (legacyNames.Any(ln => ln.Equals(dirName, StringComparison.OrdinalIgnoreCase)))
                    {
                        try { Directory.Delete(subDir, true); } catch { }
                    }
                }
            }
            catch { }
        }

        // =========================================================================
        // 2. SKANER WSPÓLNEGO FOLDERU SOURCE/ I MANIFESTU KOMPATYBILNOŚCI APK
        // =========================================================================
        public static List<RequiredApkRequirementModel> ScanSourceApksAndBuildCompatibilityManifest(string sourceDir)
        {
            var list = new List<RequiredApkRequirementModel>();
            var seenPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (Directory.Exists(sourceDir))
                {
                    var apkFiles = Directory.GetFiles(sourceDir, "*.apk", SearchOption.AllDirectories)
                        .OrderByDescending(File.GetLastWriteTimeUtc)
                        .ToList();

                    foreach (string apkPath in apkFiles)
                    {
                        string fileName = Path.GetFileName(apkPath);
                        string relPath = Path.GetRelativePath(sourceDir, apkPath).Replace('\\', '/').TrimStart('/');
                        string lowerName = fileName.ToLowerInvariant();

                        string role = "PLUGIN";
                        string pkgName = "com.atakmap.android.plugin." + Regex.Replace(Path.GetFileNameWithoutExtension(lowerName), @"[^a-z0-9]", "");
                        string displayName = Path.GetFileNameWithoutExtension(fileName);

                        if (lowerName.Contains("atak") && !lowerName.Contains("plugin") && !lowerName.Contains("meshtastic"))
                        {
                            role = "ATAK-CIV";
                            pkgName = "com.atakmap.app.civ";
                            displayName = "ATAK-CIV";
                        }
                        else if (lowerName.Contains("meshtastic") && !lowerName.Contains("plugin") && !lowerName.Contains("atak"))
                        {
                            role = "MESHTASTIC";
                            pkgName = "com.geeksville.mesh";
                            displayName = "Meshtastic";
                        }
                        else if (lowerName.Contains("meshtastic") && (lowerName.Contains("plugin") || lowerName.Contains("atak")))
                        {
                            role = "PLUGIN";
                            pkgName = "com.atakmap.android.meshtastic.plugin";
                            displayName = "Meshtastic ATAK Plugin";
                        }
                        else if (lowerName.Contains("datasync"))
                        {
                            role = "PLUGIN";
                            pkgName = "com.atakmap.android.datasync.plugin";
                            displayName = "ATAK DataSync Plugin";
                        }

                        if (!seenPackages.Add(pkgName)) continue;

                        string versionStr = ExtractVersionFromFilename(fileName);
                        string shaShort = ComputeFileSha256Short(apkPath);
                        long fSize = new FileInfo(apkPath).Length;

                        list.Add(new RequiredApkRequirementModel
                        {
                            Role = role,
                            PackageName = pkgName,
                            DisplayName = displayName,
                            RequiredVersion = versionStr,
                            BundledZipPath = relPath,
                            Sha256Short = shaShort,
                            FileSizeBytes = fSize
                        });
                    }
                }
            }
            catch { }

            // Jeśli w folderze source/apk/ nie umieszczono jeszcze plików .apk, dodaj wpisy referencyjne ATAK-CIV i Meshtastic,
            // aby aplikacja Android zawsze weryfikowała ich obecność i wersję pod kątem kompatybilności z OpenTAKServer!
            if (!list.Any(x => x.Role == "ATAK-CIV"))
            {
                list.Insert(0, new RequiredApkRequirementModel
                {
                    Role = "ATAK-CIV",
                    PackageName = "com.atakmap.app.civ",
                    DisplayName = "ATAK-CIV (OpenTAKServer Ready)",
                    RequiredVersion = "5.0+",
                    BundledZipPath = "",
                    Sha256Short = "",
                    FileSizeBytes = 0
                });
            }
            if (!list.Any(x => x.Role == "MESHTASTIC"))
            {
                list.Add(new RequiredApkRequirementModel
                {
                    Role = "MESHTASTIC",
                    PackageName = "com.geeksville.mesh",
                    DisplayName = "Meshtastic Android",
                    RequiredVersion = "2.5+",
                    BundledZipPath = "",
                    Sha256Short = "",
                    FileSizeBytes = 0
                });
            }

            return list;
        }

        private static string ExtractVersionFromFilename(string fileName)
        {
            string nameNoExt = Path.GetFileNameWithoutExtension(fileName);
            var m = Regex.Match(nameNoExt, @"v?([0-9]+\.[0-9]+(?:\.[0-9]+)?(?:\.[0-9]+)?)", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : "Zgodna z paczką .FTAK";
        }

        private static string ComputeFileSha256Short(string filePath)
        {
            try
            {
                using var sha = SHA256.Create();
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                byte[] hash = sha.ComputeHash(fs);
                return BitConverter.ToString(hash).Replace("-", "").Substring(0, 12).ToLowerInvariant();
            }
            catch
            {
                return "";
            }
        }

        private static (int AssetsCount, int ApkRequirementsCount) WriteSingleAuditedFactionFtakArchive(
            string ftakZipPath,
            string eventName,
            CleanFactionProfileModel fac,
            string sourceDir,
            OpordParagraphModel opordModel,
            string opordHtml,
            string opordJson)
        {
            if (File.Exists(ftakZipPath)) File.Delete(ftakZipPath);

            string deepLinkUrl = BuildMeshtasticChannelSetUrl(fac);
            fac.ChannelUrl = deepLinkUrl;

            var voiceArray = new JsonArray();
            foreach (var vc in fac.VoiceChannels)
            {
                voiceArray.Add(new JsonObject
                {
                    ["roleName"] = vc.RoleName,
                    ["bandType"] = vc.BandType,
                    ["freqMhz"] = vc.FreqMhz,
                    ["toneOrCode"] = vc.ToneOrCode
                });
            }

            // Zbuduj manifest wymaganych wersji APK (ATAK-CIV, Meshtastic, Pluginy) pod serwer OpenTAKServer
            var apkReqs = ScanSourceApksAndBuildCompatibilityManifest(sourceDir);
            var reqApksArray = new JsonArray();
            foreach (var req in apkReqs)
            {
                reqApksArray.Add(new JsonObject
                {
                    ["role"] = req.Role,
                    ["packageName"] = req.PackageName,
                    ["displayName"] = req.DisplayName,
                    ["requiredVersion"] = req.RequiredVersion,
                    ["bundledZipPath"] = req.BundledZipPath,
                    ["sha256Short"] = req.Sha256Short,
                    ["fileSizeBytes"] = req.FileSizeBytes
                });
            }

            var opordSummaryNode = new JsonObject
            {
                ["operationNumber"] = opordModel.OperationNumber,
                ["operationDate"] = opordModel.OperationDateIso,
                ["locationName"] = opordModel.LocationName,
                ["locationLat"] = opordModel.LocationLat,
                ["locationLon"] = opordModel.LocationLon,
                ["missionStatement"] = string.IsNullOrWhiteSpace(opordModel.MissionFullStatement)
                    ? OpordBuilderManager.Compose5WMissionText(opordModel)
                    : opordModel.MissionFullStatement,
                ["weatherCondition"] = opordModel.MetocCondition,
                ["temperature"] = opordModel.MetocTemperature,
                ["wind"] = opordModel.MetocWind,
                ["sunTimes"] = opordModel.MetocSunTimes,
                ["moonPhase"] = opordModel.MetocMoonPhase,
                ["sarnegKey"] = opordModel.SarnegKeyWord10,
                ["challengePassword"] = opordModel.ChallengeAndPassword
            };

            JsonObject MakeFactionNode() => new JsonObject
            {
                ["Name"] = fac.Name,
                ["ColorHex"] = fac.ColorHex,
                ["ChannelName"] = fac.ChannelName,
                ["Psk"] = fac.Psk,
                ["ChannelUrl"] = deepLinkUrl,
                ["LoraRegion"] = fac.LoraRegion,
                ["LoraModemPreset"] = fac.LoraModemPreset,
                ["HopLimit"] = fac.HopLimit,
                ["OtsHost"] = fac.OtsHost,
                ["EnrollmentUri"] = fac.EnrollmentUri,
                ["EmergencyPhone"] = fac.EmergencyPhone,
                ["PliProfile"] = fac.PliProfile,
                ["MeshProfile"] = fac.MeshProfile,
                ["VoiceChannels"] = JsonNode.Parse(voiceArray.ToJsonString()),
                ["RequiredPackages"] = JsonNode.Parse(reqApksArray.ToJsonString()),
                ["OpordSummary"] = JsonNode.Parse(opordSummaryNode.ToJsonString())
            };

            var rootJson = new JsonObject
            {
                ["schemaVersion"] = "3.0",
                ["eventName"] = eventName,
                ["activeFaction"] = fac.Name,
                ["Name"] = fac.Name,
                ["ColorHex"] = fac.ColorHex,
                ["ChannelName"] = fac.ChannelName,
                ["Psk"] = fac.Psk,
                ["ChannelUrl"] = deepLinkUrl,
                ["LoraRegion"] = fac.LoraRegion,
                ["LoraModemPreset"] = fac.LoraModemPreset,
                ["HopLimit"] = fac.HopLimit,
                ["OtsHost"] = fac.OtsHost,
                ["EnrollmentUri"] = fac.EnrollmentUri,
                ["EmergencyPhone"] = fac.EmergencyPhone,
                ["PliProfile"] = fac.PliProfile,
                ["MeshProfile"] = fac.MeshProfile,
                ["VoiceChannels"] = voiceArray,
                ["RequiredPackages"] = reqApksArray,
                ["OpordSummary"] = opordSummaryNode,
                ["factions"] = new JsonArray { MakeFactionNode() }
            };

            string jsonFormatted = rootJson.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonFormatted);

            int includedAssetsCount = 0;
            string facSlug = TacticalConfigPanel.SanitizeFileName(fac.Name).ToLowerInvariant();
            string facSpecificDir = Path.Combine(sourceDir, "factions", facSlug);
            bool hasFactionSpecificVectors = Directory.Exists(facSpecificDir) &&
                Directory.GetFiles(facSpecificDir, "*.*", SearchOption.AllDirectories)
                    .Any(f => f.EndsWith(".kmz", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".gpx", StringComparison.OrdinalIgnoreCase));

            using var fs = new FileStream(ftakZipPath, FileMode.CreateNew, FileAccess.Write);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create, false, Encoding.UTF8);

            // 1. Zapisz config.json oraz manifest.json
            foreach (string metaName in new[] { "config.json", "manifest.json" })
            {
                var e = zip.CreateEntry(metaName, CompressionLevel.Optimal);
                using var s = e.Open();
                s.Write(jsonBytes, 0, jsonBytes.Length);
            }

            // 2. Zapisz bezpośrednio w głównym katalogu paczki .ftak pełny rozkaz OPORD (HTML oraz JSON) dla aplikacji Android
            byte[] opordHtmlBytes = Encoding.UTF8.GetBytes(opordHtml);
            var opordHtmlEntry = zip.CreateEntry("opord.html", CompressionLevel.Optimal);
            using (var s = opordHtmlEntry.Open()) { s.Write(opordHtmlBytes, 0, opordHtmlBytes.Length); }

            byte[] opordJsonBytes = Encoding.UTF8.GetBytes(opordJson);
            var opordJsonEntry = zip.CreateEntry("opord.json", CompressionLevel.Optimal);
            using (var s = opordJsonEntry.Open()) { s.Write(opordJsonBytes, 0, opordJsonBytes.Length); }
            includedAssetsCount += 2;

            // 3. Spakuj pliki ze wspólnego folderu source/ (apk, plugins, datapackage, maps, certs) oraz dedykowanego source/factions/<strona>/
            if (Directory.Exists(sourceDir))
            {
                var allSourceFiles = Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories);
                foreach (string filePath in allSourceFiles)
                {
                    string ext = Path.GetExtension(filePath).ToLowerInvariant();
                    bool isAllowedAsset =
                        ext == ".apk" || ext == ".zip" ||
                        ext == ".mbtiles" || ext == ".sqlite" ||
                        ext == ".kmz" || ext == ".kml" || ext == ".gpx" ||
                        ext == ".p12" || ext == ".pref" || ext == ".html";

                    if (!isAllowedAsset) continue;

                    string relPath = Path.GetRelativePath(sourceDir, filePath).Replace('\\', '/').TrimStart('/');
                    string relLower = relPath.ToLowerInvariant();

                    // Izolacja Fog of War: pomiń zasoby innych stron konfliktu!
                    if (relLower.StartsWith("factions/"))
                    {
                        string[] parts = relLower.Split('/');
                        if (parts.Length >= 2 && parts[1] != facSlug)
                            continue;
                    }
                    else
                    {
                        if (hasFactionSpecificVectors && (ext == ".kmz" || ext == ".gpx") && !relLower.StartsWith("maps/") && !relLower.StartsWith("datapackage/"))
                            continue;
                    }

                    // Dla plików binarnych (.apk, .zip, .kmz) użyj FastCompression
                    var compLevel = (ext == ".apk" || ext == ".zip" || ext == ".kmz")
                        ? CompressionLevel.Fastest
                        : CompressionLevel.Optimal;

                    var fileEntry = zip.CreateEntry(relPath, compLevel);
                    using var entryStream = fileEntry.Open();
                    using var srcStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    srcStream.CopyTo(entryStream);
                    includedAssetsCount++;
                }
            }

            return (includedAssetsCount, apkReqs.Count);
        }

        // =========================================================================
        // 3. KODER BINARNY MESHTASTIC PROTOBUF (CHANNELSET -> DEEP LINK URL)
        // =========================================================================
        public static string BuildMeshtasticChannelSetUrl(CleanFactionProfileModel fac)
        {
            if (!string.IsNullOrWhiteSpace(fac.ChannelUrl) &&
                fac.ChannelUrl.StartsWith("https://meshtastic.org/e/#", StringComparison.OrdinalIgnoreCase))
            {
                return fac.ChannelUrl;
            }

            byte[] pskBytes;
            try
            {
                pskBytes = !string.IsNullOrWhiteSpace(fac.Psk) ? Convert.FromBase64String(fac.Psk.Trim()) : new byte[] { 1 };
            }
            catch
            {
                pskBytes = new byte[] { 1 };
            }

            string cleanChName = (fac.ChannelName ?? fac.Name ?? "TAK").Trim();
            if (cleanChName.Length > 11) cleanChName = cleanChName.Substring(0, 11);
            byte[] nameBytes = Encoding.UTF8.GetBytes(cleanChName);

            using var chSettings = new MemoryStream();
            WriteProtoBytes(chSettings, 0x12, pskBytes);
            if (nameBytes.Length > 0) WriteProtoBytes(chSettings, 0x1A, nameBytes);
            WriteProtoVarintField(chSettings, 0x28, 1);
            WriteProtoVarintField(chSettings, 0x30, 1);

            using var loraCfg = new MemoryStream();
            WriteProtoVarintField(loraCfg, 0x08, 1);
            int presetEnum = (fac.LoraModemPreset ?? "SHORT_FAST").ToUpperInvariant() switch
            {
                "LONG_FAST" => 0,
                "LONG_SLOW" => 1,
                "VERY_LONG_SLOW" => 2,
                "MEDIUM_SLOW" => 3,
                "MEDIUM_FAST" => 4,
                "SHORT_SLOW" => 5,
                "SHORT_FAST" => 6,
                "LONG_MODERATE" => 7,
                "SHORT_TURBO" => 8,
                _ => 6
            };
            if (presetEnum != 0) WriteProtoVarintField(loraCfg, 0x10, presetEnum);

            int regionEnum = (fac.LoraRegion ?? "EU_868").ToUpperInvariant() switch
            {
                "US" => 1,
                "EU_433" => 2,
                "EU_868" => 3,
                "UA_433" => 14,
                "UA_868" => 15,
                _ => 3
            };
            WriteProtoVarintField(loraCfg, 0x38, regionEnum);
            WriteProtoVarintField(loraCfg, 0x40, Math.Clamp(fac.HopLimit, 1, 7));
            WriteProtoVarintField(loraCfg, 0x48, 1);

            using var channelSet = new MemoryStream();
            WriteProtoBytes(channelSet, 0x0A, chSettings.ToArray());
            WriteProtoBytes(channelSet, 0x12, loraCfg.ToArray());

            string b64Url = Convert.ToBase64String(channelSet.ToArray())
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

            return "https://meshtastic.org/e/#" + b64Url;
        }

        private static void WriteProtoVarint(Stream s, int val)
        {
            uint v = (uint)val;
            while ((v & ~0x7Fu) != 0)
            {
                s.WriteByte((byte)((v & 0x7Fu) | 0x80u));
                v >>= 7;
            }
            s.WriteByte((byte)(v & 0x7Fu));
        }

        private static void WriteProtoVarintField(Stream s, byte tag, int val)
        {
            s.WriteByte(tag);
            WriteProtoVarint(s, val);
        }

        private static void WriteProtoBytes(Stream s, byte tag, byte[] data)
        {
            s.WriteByte(tag);
            WriteProtoVarint(s, data.Length);
            s.Write(data, 0, data.Length);
        }

        // =========================================================================
        // 4. LOKALNY SERWER HTTP WI-FI (:8766) DLA PACZEK .FTAK ORAZ OPORD
        // =========================================================================
        public static string EnsurePcFtakHttpServerStarted()
        {
            if (!_pcServerRunning || _pcHotspotListener == null)
            {
                foreach (int port in new[] { 8766, 8767, 8768 })
                {
                    try
                    {
                        var listener = new TcpListener(IPAddress.Any, port);
                        listener.Start();
                        _pcHotspotListener = listener;
                        _pcServerPort = port;
                        _pcServerRunning = true;
                        _ = Task.Run(() => AcceptPcHttpClientsLoop(listener));
                        break;
                    }
                    catch { }
                }
            }

            string localIp = GetBestLocalLanIpAddress();
            return "http://" + localIp + ":" + _pcServerPort;
        }

        public static void StopPcFtakHttpServer()
        {
            _pcServerRunning = false;
            try { _pcHotspotListener?.Stop(); } catch { }
            _pcHotspotListener = null;
        }

        private static async Task AcceptPcHttpClientsLoop(TcpListener listener)
        {
            while (_pcServerRunning)
            {
                try
                {
                    var client = await listener.AcceptTcpClientAsync();
                    _ = Task.Run(() => HandlePcHttpClient(client));
                }
                catch
                {
                    if (!_pcServerRunning) break;
                }
            }
        }

        private static void HandlePcHttpClient(TcpClient client)
        {
            using (client)
            {
                try
                {
                    using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true);
                    string reqLine = reader.ReadLine();
                    if (string.IsNullOrEmpty(reqLine)) return;

                    string[] parts = reqLine.Split(' ');
                    string path = parts.Length >= 2 ? Uri.UnescapeDataString(parts[1]) : "/";

                    // Obsługa pobierania samego rozkazu OPORD przez Wi-Fi / LAN (/opord/<Strona>.json lub .html)
                    if (path.StartsWith("/opord/", StringComparison.OrdinalIgnoreCase))
                    {
                        string facKey = "BLUFOR";
                        foreach (var kv in ServedFtakPathsByFaction)
                        {
                            if (path.Contains(kv.Key, StringComparison.OrdinalIgnoreCase))
                            {
                                facKey = kv.Key;
                                break;
                            }
                        }
                        var opModel = OpordBuilderManager.GetOrCreateModel(facKey);
                        bool isJson = path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
                        string payload = isJson
                            ? JsonSerializer.Serialize(opModel, new JsonSerializerOptions { WriteIndented = true })
                            : OpordBuilderManager.GeneratePrintableOpordHtml(_lastWinRef, facKey, opModel);

                        byte[] opBytes = Encoding.UTF8.GetBytes(payload);
                        string cType = isJson ? "application/json; charset=utf-8" : "text/html; charset=utf-8";
                        string opHdr =
                            "HTTP/1.1 200 OK\r\n" +
                            "Content-Type: " + cType + "\r\n" +
                            "Content-Length: " + opBytes.Length + "\r\n" +
                            "Connection: close\r\n\r\n";
                        byte[] opHdrBytes = Encoding.UTF8.GetBytes(opHdr);
                        stream.Write(opHdrBytes, 0, opHdrBytes.Length);
                        stream.Write(opBytes, 0, opBytes.Length);
                        stream.Flush();
                        return;
                    }

                    string targetFile = null;
                    foreach (var kv in ServedFtakPathsByFaction)
                    {
                        if (path.Contains(kv.Key, StringComparison.OrdinalIgnoreCase) && File.Exists(kv.Value))
                        {
                            targetFile = kv.Value;
                            break;
                        }
                    }
                    if (targetFile == null)
                    {
                        targetFile = ServedFtakPathsByFaction.Values.FirstOrDefault(File.Exists);
                    }

                    if (targetFile != null && File.Exists(targetFile))
                    {
                        byte[] fileBytes = File.ReadAllBytes(targetFile);
                        string fileName = Path.GetFileName(targetFile);
                        string headers =
                            "HTTP/1.1 200 OK\r\n" +
                            "Content-Type: application/octet-stream\r\n" +
                            "Content-Disposition: attachment; filename=\"" + fileName + "\"\r\n" +
                            "Content-Length: " + fileBytes.Length + "\r\n" +
                            "Connection: close\r\n\r\n";
                        byte[] hdrBytes = Encoding.UTF8.GetBytes(headers);
                        stream.Write(hdrBytes, 0, hdrBytes.Length);
                        stream.Write(fileBytes, 0, fileBytes.Length);
                        stream.Flush();
                    }
                }
                catch { }
            }
        }

        private static string GetBestLocalLanIpAddress()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    {
                        string s = ip.ToString();
                        if (s.StartsWith("192.168.") || s.StartsWith("10.") || s.StartsWith("172."))
                            return s;
                    }
                }
            }
            catch { }
            return "127.0.0.1";
        }

        public static void ShowActiveFactionQrModal(
            Window owner,
            string eventName,
            CleanFactionProfileModel fac,
            string ftakFilePath,
            string serverBaseUrl)
        {
            string safeFac = Uri.EscapeDataString(fac.Name);
            string wifiDownloadUrl = serverBaseUrl + "/deploy/" + safeFac + ".ftak";
            string opordLanUrl = serverBaseUrl + "/opord/" + safeFac + ".html";

            var offlineJsonObj = new JsonObject
            {
                ["schemaVersion"] = "3.0",
                ["eventName"] = eventName,
                ["activeFaction"] = fac.Name,
                ["factions"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["Name"] = fac.Name,
                        ["ColorHex"] = fac.ColorHex,
                        ["ChannelName"] = fac.ChannelName,
                        ["Psk"] = fac.Psk,
                        ["ChannelUrl"] = fac.ChannelUrl,
                        ["LoraRegion"] = fac.LoraRegion,
                        ["LoraModemPreset"] = fac.LoraModemPreset,
                        ["HopLimit"] = fac.HopLimit,
                        ["OtsHost"] = fac.OtsHost,
                        ["EmergencyPhone"] = fac.EmergencyPhone,
                        ["VoiceChannels"] = new JsonArray(
                            fac.VoiceChannels.Select(v => (JsonNode)new JsonObject
                            {
                                ["roleName"] = v.RoleName,
                                ["bandType"] = v.BandType,
                                ["freqMhz"] = v.FreqMhz,
                                ["toneOrCode"] = v.ToneOrCode
                            }).ToArray())
                    }
                }
            };
            string offlineJsonPayload = offlineJsonObj.ToJsonString();

            var dlg = new Window
            {
                Title = BuilderI18n.T(
                    "📱 Dystrybucja QR Aktywnej Strony: " + fac.Name + " (" + eventName + ")",
                    "📱 Active Faction QR Distribution: " + fac.Name + " (" + eventName + ")"),
                Width = 540,
                Height = 640,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                Background = TacticalConfigPanel.BrushCardBg,
                Foreground = Brushes.White,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow
            };

            var root = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };

            var hdrBorder = new Border
            {
                Background = TacticalConfigPanel.BrushItemBg,
                BorderBrush = TacticalConfigPanel.BrushFromHex(fac.ColorHex, Color.FromRgb(31, 224, 197)),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var hdrStack = new StackPanel();
            hdrStack.Children.Add(new TextBlock
            {
                Text = "🛡️ " + fac.Name + "   •   " + eventName,
                Foreground = TacticalConfigPanel.BrushAccent,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            long kbSize = File.Exists(ftakFilePath) ? Math.Max(1, new FileInfo(ftakFilePath).Length / 1024) : 0;
            string otsInfo = string.IsNullOrEmpty(fac.OtsHost) ? "Off-Grid Mesh" : fac.OtsHost;
            hdrStack.Children.Add(new TextBlock
            {
                Text = "Folder: out\\" + TacticalConfigPanel.SanitizeFileName(fac.Name).ToUpperInvariant() + "\\  |  LoRa: " + fac.ChannelName + "  |  OTS: " + otsInfo + "  |  .FTAK: " + kbSize + " KB",
                Foreground = Brushes.White,
                FontSize = 10.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 0)
            });
            hdrBorder.Child = hdrStack;
            root.Children.Add(hdrBorder);

            var modeGrid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 10) };
            var btnModeWifi = new Button
            {
                Content = BuilderI18n.T("📡 1. Pełny .FTAK (Wi-Fi)", "📡 1. Full .FTAK (Wi-Fi)"),
                Height = 30,
                Margin = new Thickness(0, 0, 3, 0),
                Background = TacticalConfigPanel.BrushButtonAccent,
                Foreground = TacticalConfigPanel.BrushDarkFg,
                FontWeight = FontWeights.Bold,
                FontSize = 10,
                Cursor = Cursors.Hand
            };
            var btnModeOpordLan = new Button
            {
                Content = BuilderI18n.T("📋 2. Sam OPORD (LAN)", "📋 2. OPORD Only (LAN)"),
                Height = 30,
                Margin = new Thickness(3, 0, 3, 0),
                Background = TacticalConfigPanel.BrushInputBg,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 10,
                Cursor = Cursors.Hand
            };
            var btnModeOffline = new Button
            {
                Content = BuilderI18n.T("📴 3. QR Offline (JSON)", "📴 3. Offline QR (JSON)"),
                Height = 30,
                Margin = new Thickness(3, 0, 0, 0),
                Background = TacticalConfigPanel.BrushInputBg,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 10,
                Cursor = Cursors.Hand
            };
            modeGrid.Children.Add(btnModeWifi);
            modeGrid.Children.Add(btnModeOpordLan);
            modeGrid.Children.Add(btnModeOffline);
            root.Children.Add(modeGrid);

            var qrFrame = new Border
            {
                Width = 280,
                Height = 280,
                Background = Brushes.White,
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(3),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };
            var qrImg = new Image { Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(qrImg, BitmapScalingMode.NearestNeighbor);
            qrFrame.Child = qrImg;
            root.Children.Add(qrFrame);

            var payloadBox = new TextBox
            {
                Text = wifiDownloadUrl,
                IsReadOnly = true,
                Height = 26,
                FontSize = 10.5,
                FontFamily = new FontFamily("Consolas"),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = TacticalConfigPanel.BrushInputBg,
                Foreground = TacticalConfigPanel.BrushAccent,
                BorderBrush = TacticalConfigPanel.BrushInputBorder,
                Margin = new Thickness(0, 0, 0, 10)
            };
            root.Children.Add(payloadBox);

            void LoadQrPayload(string dataStr, int modeIdx)
            {
                payloadBox.Text = modeIdx == 0
                    ? wifiDownloadUrl
                    : (modeIdx == 1 ? opordLanUrl : ("[Offline JSON: " + fac.Name + " | LoRa: " + fac.ChannelName + " | Voice: " + fac.VoiceChannels.Count + "]"));

                btnModeWifi.Background = modeIdx == 0 ? TacticalConfigPanel.BrushButtonAccent : TacticalConfigPanel.BrushInputBg;
                btnModeWifi.Foreground = modeIdx == 0 ? TacticalConfigPanel.BrushDarkFg : Brushes.White;

                btnModeOpordLan.Background = modeIdx == 1 ? TacticalConfigPanel.BrushButtonAccent : TacticalConfigPanel.BrushInputBg;
                btnModeOpordLan.Foreground = modeIdx == 1 ? TacticalConfigPanel.BrushDarkFg : Brushes.White;

                btnModeOffline.Background = modeIdx == 2 ? TacticalConfigPanel.BrushButtonAccent : TacticalConfigPanel.BrushInputBg;
                btnModeOffline.Foreground = modeIdx == 2 ? TacticalConfigPanel.BrushDarkFg : Brushes.White;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        string cacheKey = "QR_V3_" + dataStr;
                        byte[] bytes = GeoportalLidarEngine.TryReadDiskCache(cacheKey);
                        if (bytes == null)
                        {
                            string api = "https://api.qrserver.com/v1/create-qr-code/?size=420x420&margin=10&data=" + Uri.EscapeDataString(dataStr);
                            bytes = await GeoportalLidarEngine.FastHttp.GetByteArrayAsync(api);
                            if (GeoportalLidarEngine.IsValidImageBytes(bytes))
                                GeoportalLidarEngine.SaveDiskCacheAsync(cacheKey, bytes);
                        }
                        if (GeoportalLidarEngine.IsValidImageBytes(bytes) && Application.Current != null)
                        {
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                var bmp = new BitmapImage();
                                using var ms = new MemoryStream(bytes);
                                bmp.BeginInit();
                                bmp.CacheOption = BitmapCacheOption.OnLoad;
                                bmp.StreamSource = ms;
                                bmp.EndInit();
                                bmp.Freeze();
                                qrImg.Source = bmp;
                            });
                        }
                    }
                    catch { }
                });
            }

            btnModeWifi.Click += (_, __) => LoadQrPayload(wifiDownloadUrl, 0);
            btnModeOpordLan.Click += (_, __) => LoadQrPayload(opordLanUrl, 1);
            btnModeOffline.Click += (_, __) => LoadQrPayload(offlineJsonPayload, 2);
            LoadQrPayload(wifiDownloadUrl, 0);

            var bottomBtns = new UniformGrid { Columns = 2 };
            var btnOpenDir = new Button
            {
                Content = BuilderI18n.T("📂 Otwórz folder out\\" + TacticalConfigPanel.SanitizeFileName(fac.Name).ToUpperInvariant(), "📂 Open out\\" + TacticalConfigPanel.SanitizeFileName(fac.Name).ToUpperInvariant() + " Folder"),
                Height = 32,
                Margin = new Thickness(0, 0, 5, 0),
                Background = TacticalConfigPanel.BrushItemBg,
                Foreground = Brushes.White,
                BorderBrush = TacticalConfigPanel.BrushAccent,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            btnOpenDir.Click += (_, __) =>
            {
                string dir = Path.GetDirectoryName(ftakFilePath);
                if (!string.IsNullOrEmpty(dir)) TacticalConfigPanel.TryOpenFolderInExplorer(dir);
            };

            var btnClose = new Button
            {
                Content = BuilderI18n.T("Zamknij", "Close"),
                Height = 32,
                Margin = new Thickness(5, 0, 0, 0),
                Background = TacticalConfigPanel.BrushInputBg,
                Foreground = Brushes.White,
                Cursor = Cursors.Hand
            };
            btnClose.Click += (_, __) => dlg.Close();

            bottomBtns.Children.Add(btnOpenDir);
            bottomBtns.Children.Add(btnClose);
            root.Children.Add(bottomBtns);

            dlg.Content = root;
            dlg.ShowDialog();
        }

        public static CleanWorkspaceSnapshot ExtractWorkspaceSnapshot(Window win)
        {
            try { TacticalConfigPanel.SaveSessionStateAuto(win); } catch { }

            var state = new CleanWorkspaceSnapshot
            {
                EventName = TacticalConfigPanel.GetEventNameFromWindow(win)
            };

            JsonObject sessionJson = TryReadSavedWorkspaceJson();
            if (sessionJson != null)
            {
                string ev = sessionJson["eventName"]?.ToString();
                if (!string.IsNullOrWhiteSpace(ev)) state.EventName = ev;

                if (sessionJson["factions"] is JsonArray fArr && fArr.Count > 0)
                {
                    foreach (var item in fArr.OfType<JsonObject>())
                    {
                        var fm = new CleanFactionProfileModel
                        {
                            Name = item["Name"]?.ToString() ?? item["name"]?.ToString() ?? "BLUFOR",
                            ColorHex = item["ColorHex"]?.ToString() ?? item["colorHex"]?.ToString() ?? "#2684FF",
                            ChannelName = item["ChannelName"]?.ToString() ?? item["channelName"]?.ToString() ?? "BLUFOR-MESH",
                            Psk = item["Psk"]?.ToString() ?? item["psk"]?.ToString() ?? "",
                            ChannelUrl = item["ChannelUrl"]?.ToString() ?? item["channelUrl"]?.ToString() ?? "",
                            LoraRegion = item["LoraRegion"]?.ToString() ?? item["loraRegion"]?.ToString() ?? "EU_868",
                            LoraModemPreset = item["LoraModemPreset"]?.ToString() ?? item["loraModemPreset"]?.ToString() ?? "SHORT_FAST",
                            HopLimit = int.TryParse(item["HopLimit"]?.ToString() ?? item["hopLimit"]?.ToString(), out int hl) ? hl : 3,
                            OtsHost = item["OtsHost"]?.ToString() ?? item["otsHost"]?.ToString() ?? "",
                            EnrollmentUri = item["EnrollmentUri"]?.ToString() ?? item["enrollmentUri"]?.ToString() ?? "",
                            EmergencyPhone = item["EmergencyPhone"]?.ToString() ?? item["emergencyPhone"]?.ToString() ?? "",
                            PliProfile = item["PliProfile"]?.ToString() ?? "ConstRateUnreliable",
                            MeshProfile = item["MeshProfile"]?.ToString() ?? "StationaryMesh"
                        };

                        if ((item["VoiceChannels"] ?? item["voiceChannels"]) is JsonArray vArr)
                        {
                            foreach (var vo in vArr.OfType<JsonObject>())
                            {
                                fm.VoiceChannels.Add(new CleanVoiceChannelItem
                                {
                                    RoleName = vo["roleName"]?.ToString() ?? vo["RoleName"]?.ToString() ?? "Kanał",
                                    BandType = vo["bandType"]?.ToString() ?? vo["BandType"]?.ToString() ?? "PMR",
                                    FreqMhz = vo["freqMhz"]?.ToString() ?? vo["FreqMhz"]?.ToString() ?? "-",
                                    ToneOrCode = vo["toneOrCode"]?.ToString() ?? vo["ToneOrCode"]?.ToString() ?? "-"
                                });
                            }
                        }
                        state.Factions.Add(fm);
                    }
                }
            }

            if (state.Factions.Count == 0)
            {
                foreach (var basic in TacticalConfigPanel.GetConfiguredFactions())
                {
                    state.Factions.Add(new CleanFactionProfileModel
                    {
                        Name = basic.Name,
                        ChannelName = basic.Channel,
                        ColorHex = basic.ColorHex,
                        Psk = basic.Psk
                    });
                }
            }

            return state;
        }

        private static JsonObject TryReadSavedWorkspaceJson()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(TacticalConfigPanel.CurrentDraftProjectPath) &&
                    File.Exists(TacticalConfigPanel.CurrentDraftProjectPath))
                {
                    if (JsonNode.Parse(File.ReadAllText(TacticalConfigPanel.CurrentDraftProjectPath, Encoding.UTF8)) is JsonObject draftObj)
                        return draftObj;
                }

                string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FieldTakHub");
                if (Directory.Exists(appData))
                {
                    var jsonFiles = Directory.GetFiles(appData, "*.json", SearchOption.TopDirectoryOnly)
                        .Concat(Directory.GetFiles(appData, "*.fthproj", SearchOption.TopDirectoryOnly))
                        .Where(f => !Path.GetFileName(f).StartsWith("opord_", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(File.GetLastWriteTimeUtc)
                        .ToList();

                    foreach (string jf in jsonFiles)
                    {
                        try
                        {
                            string raw = File.ReadAllText(jf, Encoding.UTF8);
                            if (raw.Contains("\"factions\"", StringComparison.OrdinalIgnoreCase) &&
                                JsonNode.Parse(raw) is JsonObject obj)
                            {
                                return obj;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return null;
        }
    }

    // =========================================================================
    // 5. POWŁOKA GŁÓWNA BUILDERA v3.0 (POŁĄCZENIE Z SIDEBAREM I OPORD v3.0)
    // =========================================================================
    internal static class BuilderUiSimplifier
    {
        private static readonly HashSet<int> AttachedWindows = new HashSet<int>();
        private static bool _isApplyingSimplification;

        private static Border _activeFactionHubBanner;
        private static ComboBox _activeFactionCombo;
        private static TextBlock _auditStatusLabel;
        private static string _selectedActiveFactionName = "";

        private static Button _topTabOpordBtn;
        private static Button _topLangToggleBtn;

        public static void Attach(Window win)
        {
            if (win == null) return;
            int hash = win.GetHashCode();
            if (!AttachedWindows.Add(hash))
            {
                EnforceSimplification(win);
                return;
            }

            win.Loaded += (_, __) => EnforceSimplification(win);
            win.ContentRendered += (_, __) => EnforceSimplification(win);
            win.LayoutUpdated += (_, __) =>
            {
                if (!_isApplyingSimplification)
                {
                    EnforceSimplification(win);
                }
            };
            win.Closed += (_, __) => CleanFactionPackageEngine.StopPcFtakHttpServer();

            BuilderI18n.LanguageChanged += _ =>
            {
                if (_topLangToggleBtn != null)
                {
                    _topLangToggleBtn.Content = "🌐 " + BuilderI18n.CurrentLang;
                }
                if (_topTabOpordBtn != null)
                {
                    _topTabOpordBtn.Content = BuilderI18n.T("📋 Generator Rozkazu (OPORD)", "📋 NATO OPORD Builder");
                }
                RebuildActiveFactionBannerTexts();
            };

            _ = win.Dispatcher.BeginInvoke(new Action(() => EnforceSimplification(win)), DispatcherPriority.Loaded);
            _ = win.Dispatcher.BeginInvoke(new Action(() => EnforceSimplification(win)), DispatcherPriority.Background);
        }

        public static void EnforceSimplification(Window win)
        {
            if (win == null || _isApplyingSimplification) return;
            _isApplyingSimplification = true;
            try
            {
                BuilderCoreSidebarAndProjectEngine.Attach(win);

                var allElements = GetAllVisualDescendants(win).ToList();

                FrameworkElement factionGeneratorRoot = FindFactionGeneratorRoot(allElements);
                if (factionGeneratorRoot == null)
                {
                    return;
                }

                EnsureFactionGeneratorVisible(factionGeneratorRoot);
                InjectOpordTabAndLanguageToggle(win, allElements);
                InjectOrUpdateActiveFactionAuditBar(win, factionGeneratorRoot);
                CollapseSimpleAdvancedModeButtonsOnly(allElements, factionGeneratorRoot);
                CollapseSection3LeafCardsOnly(allElements, factionGeneratorRoot);
                CollapseLegacyV23ButtonsAndQrOnly(allElements, factionGeneratorRoot);

                if (string.Equals(BuilderI18n.CurrentLang, "EN", StringComparison.OrdinalIgnoreCase))
                {
                    BuilderI18n.ApplyLanguageToVisualTree(win);
                }
            }
            catch { }
            finally
            {
                _isApplyingSimplification = false;
            }
        }

        private static void InjectOpordTabAndLanguageToggle(Window win, List<DependencyObject> allElements)
        {
            var opordOverlay = OpordBuilderManager.CreateOrGetOpordOverlayView(win);

            FrameworkElement mapOverlay = allElements
                .OfType<FrameworkElement>()
                .FirstOrDefault(fe => string.Equals(fe.Tag?.ToString(), "INJECTED_MAP_OVERLAY", StringComparison.Ordinal));

            if (mapOverlay != null && VisualTreeHelper.GetParent(mapOverlay) is Grid hostGrid)
            {
                if (!hostGrid.Children.Contains(opordOverlay))
                {
                    Grid.SetRow(opordOverlay, Grid.GetRow(mapOverlay));
                    Grid.SetColumn(opordOverlay, Grid.GetColumn(mapOverlay));
                    Grid.SetRowSpan(opordOverlay, Math.Max(1, Grid.GetRowSpan(mapOverlay)));
                    Grid.SetColumnSpan(opordOverlay, Math.Max(1, Grid.GetColumnSpan(mapOverlay)));
                    Panel.SetZIndex(opordOverlay, 95);
                    hostGrid.Children.Add(opordOverlay);
                }
            }
            else if (win.Content is Grid rootGrid && !rootGrid.Children.Contains(opordOverlay))
            {
                if (rootGrid.RowDefinitions.Count > 1)
                {
                    Grid.SetRow(opordOverlay, 1);
                    Grid.SetRowSpan(opordOverlay, rootGrid.RowDefinitions.Count - 1);
                }
                Panel.SetZIndex(opordOverlay, 95);
                rootGrid.Children.Add(opordOverlay);
            }
        }

        public static void ShowOpordView(Window win, bool showOpord)
        {
            var opordOverlay = OpordBuilderManager.CreateOrGetOpordOverlayView(win);
            if (showOpord)
            {
                foreach (var fe in GetAllVisualDescendants(win).OfType<FrameworkElement>())
                {
                    if (string.Equals(fe.Tag?.ToString(), "INJECTED_MAP_OVERLAY", StringComparison.Ordinal) ||
                        string.Equals(fe.Tag?.ToString(), "INJECTED_USB_OVERLAY", StringComparison.Ordinal))
                    {
                        fe.Visibility = Visibility.Collapsed;
                    }
                }
                opordOverlay.Visibility = Visibility.Visible;
            }
            else
            {
                opordOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private static FrameworkElement FindFactionGeneratorRoot(List<DependencyObject> allElements)
        {
            foreach (var fe in allElements.OfType<FrameworkElement>())
            {
                string tag = fe.Tag?.ToString() ?? "";
                if (tag.Contains("TACTICAL_CONFIG", StringComparison.OrdinalIgnoreCase) ||
                    tag.Contains("FACTION", StringComparison.OrdinalIgnoreCase) ||
                    tag.Contains("INJECTED_PANEL", StringComparison.OrdinalIgnoreCase))
                {
                    return fe;
                }
            }

            FrameworkElement smallestMatch = null;
            int smallestDescendantCount = int.MaxValue;

            foreach (var fe in allElements.OfType<FrameworkElement>())
            {
                if (!(fe is Border || fe is StackPanel || fe is Grid)) continue;
                var texts = GetElementTexts(fe);

                bool hasFactionUi =
                    texts.Any(t => t.Contains("Dodaj stronę", StringComparison.OrdinalIgnoreCase) ||
                                   t.Contains("Add Faction", StringComparison.OrdinalIgnoreCase) ||
                                   t.Contains("Plakat A4 Safezone", StringComparison.OrdinalIgnoreCase) ||
                                   t.Contains("A4 Safezone Poster", StringComparison.OrdinalIgnoreCase) ||
                                   t.Contains("STRONY KONFLIKTU", StringComparison.OrdinalIgnoreCase));

                if (!hasFactionUi) continue;

                if (texts.Any(t => t.Contains("Generator Map Taktycznych 1:1", StringComparison.OrdinalIgnoreCase) ||
                                   t.Contains("1:1 Tactical Map", StringComparison.OrdinalIgnoreCase) ||
                                   t.Contains("Zapisz projekt roboczy", StringComparison.OrdinalIgnoreCase) ||
                                   t.Contains("Save Workspace", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                int descCount = GetAllVisualDescendants(fe).Count();
                if (descCount < smallestDescendantCount && descCount >= 8)
                {
                    smallestDescendantCount = descCount;
                    smallestMatch = fe;
                }
            }
            return smallestMatch;
        }

        private static void EnsureFactionGeneratorVisible(FrameworkElement factionRoot)
        {
            DependencyObject curr = factionRoot;
            while (curr != null && !(curr is Window))
            {
                if (curr is FrameworkElement fe)
                {
                    string tag = fe.Tag?.ToString() ?? "";
                    if (tag == "INJECTED_MAP_OVERLAY" || tag == "INJECTED_OPORD_OVERLAY" || tag == "INJECTED_USB_OVERLAY") break;

                    if (fe.Visibility != Visibility.Visible)
                    {
                        DependencyObject parent = VisualTreeHelper.GetParent(fe);
                        bool isMasterTabSwitch = parent is Grid g && g.Children.Cast<UIElement>().Any(c =>
                            (c as FrameworkElement)?.Tag?.ToString() == "INJECTED_MAP_OVERLAY" ||
                            (c as FrameworkElement)?.Tag?.ToString() == "INJECTED_OPORD_OVERLAY" ||
                            (c as FrameworkElement)?.Tag?.ToString() == "INJECTED_USB_OVERLAY");
                        if (!isMasterTabSwitch)
                        {
                            fe.Visibility = Visibility.Visible;
                        }
                    }
                    if (!double.IsNaN(fe.Height) && fe.Height == 0)
                    {
                        fe.Height = double.NaN;
                    }
                    fe.IsEnabled = true;
                }
                curr = VisualTreeHelper.GetParent(curr);
            }
        }

        private static void CollapseSimpleAdvancedModeButtonsOnly(List<DependencyObject> allElements, FrameworkElement factionRoot)
        {
            foreach (var btn in allElements.OfType<ButtonBase>())
            {
                if (IsInsideOrAncestorOf(btn, factionRoot)) continue;

                string txt = string.Join(" ", GetElementTexts(btn)).Trim();
                if (btn.Content is string s) txt += " " + s;

                if (txt.Contains("Tryb prosty", StringComparison.OrdinalIgnoreCase) ||
                    txt.Contains("Tryb zaawansowany", StringComparison.OrdinalIgnoreCase))
                {
                    CollapseLeafElement(btn);
                }
            }
        }

        private static void CollapseSection3LeafCardsOnly(List<DependencyObject> allElements, FrameworkElement factionRoot)
        {
            foreach (var tb in allElements.OfType<TextBlock>())
            {
                if (IsInsideOrAncestorOf(tb, factionRoot)) continue;
                string t = (tb.Text ?? "").Trim();
                if (string.IsNullOrEmpty(t)) continue;

                bool isSec3Header =
                    t.Equals("3. Dodatkowe opcje", StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("Podpis paczki, notatki, dodatkowe pliki", StringComparison.OrdinalIgnoreCase);

                bool isSec3Field =
                    t.Equals("Package ID", StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("Publisher", StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("Max downloads", StringComparison.OrdinalIgnoreCase);

                if (!isSec3Header && !isSec3Field) continue;

                Border innermostBorder = FindInnermostBorder(tb);
                if (innermostBorder != null && !IsInsideOrAncestorOf(innermostBorder, factionRoot))
                {
                    int totalTextBlocksInside = GetElementTexts(innermostBorder).Count;
                    if (totalTextBlocksInside <= 12)
                    {
                        CollapseLeafElement(innermostBorder);
                    }
                }
            }
        }

        private static void CollapseLegacyV23ButtonsAndQrOnly(List<DependencyObject> allElements, FrameworkElement factionRoot)
        {
            foreach (var btn in allElements.OfType<ButtonBase>())
            {
                if (IsInsideOrAncestorOf(btn, factionRoot)) continue;
                if (_activeFactionHubBanner != null && IsInsideOrAncestorOf(btn, _activeFactionHubBanner)) continue;

                string combined = string.Join(" ", GetElementTexts(btn)).Trim();
                if (string.IsNullOrEmpty(combined) && btn.Content is string s)
                    combined = s.Trim();

                if (combined.Contains("stron", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("Faction", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("wszystkich", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("Plakat", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("Poster", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("OPORD", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("Source", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("Zapisz", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("Save", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("Wczytaj", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("Load", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("Wesprzyj", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("Support", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bool isLegacyBtn =
                    combined.Contains("Generuj paczkę .FTAK", StringComparison.OrdinalIgnoreCase) ||
                    combined.Contains("Generuj paczke .FTAK", StringComparison.OrdinalIgnoreCase) ||
                    combined.Equals("Buduj paczkę", StringComparison.OrdinalIgnoreCase) ||
                    combined.Equals("Buduj paczke", StringComparison.OrdinalIgnoreCase);

                if (!isLegacyBtn) continue;

                CollapseLeafElement(btn);

                if (VisualTreeHelper.GetParent(btn) is Panel parentPanel && !IsInsideOrAncestorOf(parentPanel, factionRoot))
                {
                    foreach (UIElement child in parentPanel.Children)
                    {
                        if (child is ButtonBase sibBtn && sibBtn != btn && sibBtn.ActualWidth < 70)
                        {
                            CollapseLeafElement(sibBtn);
                        }
                    }
                }
            }

            foreach (var tb in allElements.OfType<TextBlock>())
            {
                if (IsInsideOrAncestorOf(tb, factionRoot)) continue;
                if (_activeFactionHubBanner != null && IsInsideOrAncestorOf(tb, _activeFactionHubBanner)) continue;

                string t = (tb.Text ?? "").Trim();
                if (t.Contains("Zostanie wygenerowany po utworzeniu paczki", StringComparison.OrdinalIgnoreCase) ||
                    (t.Contains(":8765/") && t.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
                {
                    Border qrBorder = FindInnermostBorder(tb);
                    if (qrBorder != null && !IsInsideOrAncestorOf(qrBorder, factionRoot))
                    {
                        if (GetElementTexts(qrBorder).Count <= 10)
                        {
                            CollapseLeafElement(qrBorder);
                        }
                    }
                }
            }
        }

        private static Border FindInnermostBorder(DependencyObject startNode)
        {
            DependencyObject curr = VisualTreeHelper.GetParent(startNode);
            while (curr != null && !(curr is Window) && !(curr is ScrollViewer))
            {
                if (curr is Border b) return b;
                curr = VisualTreeHelper.GetParent(curr);
            }
            return null;
        }

        private static bool IsInsideOrAncestorOf(DependencyObject candidate, FrameworkElement protectedRoot)
        {
            if (candidate == null || protectedRoot == null) return false;
            if (candidate == protectedRoot) return true;
            if (IsAncestorOf(protectedRoot, candidate)) return true;
            if (IsAncestorOf(candidate, protectedRoot)) return true;
            return false;
        }

        private static void CollapseLeafElement(FrameworkElement fe)
        {
            if (fe == null) return;
            fe.Visibility = Visibility.Collapsed;
            fe.Height = 0;
            fe.MinHeight = 0;
            fe.Margin = new Thickness(0);
            fe.IsEnabled = false;
        }

        // =========================================================================
        // 6. ZINTEGROWANY PASEK AKTYWNEJ STRONY (.FTAK v3.0 + SOURCE/APK + OPORD)
        // =========================================================================
        private static TextBlock _bannerTitleBlock;
        private static TextBlock _bannerDescBlock;
        private static Button _btnBuildSingleFaction;
        private static Button _btnShowActiveQr;
        private static Button _btnOpenSourceDir;
        private static Button _btnBuildAllFactions;

        private static void InjectOrUpdateActiveFactionAuditBar(Window win, FrameworkElement factionGeneratorRoot)
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
                FontSize = 12.5,
                Margin = new Thickness(0, 0, 0, 4)
            };
            stack.Children.Add(_bannerTitleBlock);

            _bannerDescBlock = new TextBlock
            {
                Foreground = TacticalConfigPanel.BrushMutedText,
                FontSize = 10.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            stack.Children.Add(_bannerDescBlock);

            var controlsRow = new Grid();
            controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(145) });
            controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
            controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
            controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star) });
            controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star) });

            _activeFactionCombo = new ComboBox
            {
                Height = 32,
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 6, 0),
                Tag = "INJECTED"
            };
            TacticalConfigPanel.ApplyUnifiedComboBoxTheme(_activeFactionCombo, win);
            _activeFactionCombo.SelectionChanged += (_, __) =>
            {
                if (_activeFactionCombo.SelectedItem is string s && !string.IsNullOrWhiteSpace(s))
                    _selectedActiveFactionName = s;
            };
            SyncActiveFactionComboItems(configuredFactions);
            Grid.SetColumn(_activeFactionCombo, 0);
            controlsRow.Children.Add(_activeFactionCombo);

            _btnBuildSingleFaction = new Button
            {
                Height = 32,
                Margin = new Thickness(0, 0, 6, 0),
                Background = TacticalConfigPanel.BrushButtonAccent,
                Foreground = TacticalConfigPanel.BrushDarkFg,
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.Bold,
                FontSize = 10.5,
                Cursor = Cursors.Hand
            };
            _btnBuildSingleFaction.Click += async (_, __) =>
            {
                SyncActiveFactionComboItems(TacticalConfigPanel.GetConfiguredFactions());
                string facName = _activeFactionCombo.SelectedItem as string ?? _selectedActiveFactionName;
                await CleanFactionPackageEngine.BuildIsolatedFactionPackagesAsync(
                    win, facName, showQrModalAfter: false,
                    msg => { if (_auditStatusLabel != null) _auditStatusLabel.Text = msg; });
            };
            Grid.SetColumn(_btnBuildSingleFaction, 1);
            controlsRow.Children.Add(_btnBuildSingleFaction);

            _btnShowActiveQr = new Button
            {
                Height = 32,
                Margin = new Thickness(0, 0, 6, 0),
                Background = new SolidColorBrush(Color.FromRgb(22, 54, 66)),
                Foreground = Brushes.White,
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(1.4),
                FontWeight = FontWeights.Bold,
                FontSize = 10.5,
                Cursor = Cursors.Hand
            };
            _btnShowActiveQr.Click += async (_, __) =>
            {
                SyncActiveFactionComboItems(TacticalConfigPanel.GetConfiguredFactions());
                string facName = _activeFactionCombo.SelectedItem as string ?? _selectedActiveFactionName;
                await CleanFactionPackageEngine.BuildIsolatedFactionPackagesAsync(
                    win, facName, showQrModalAfter: true,
                    msg => { if (_auditStatusLabel != null) _auditStatusLabel.Text = msg; });
            };
            Grid.SetColumn(_btnShowActiveQr, 2);
            controlsRow.Children.Add(_btnShowActiveQr);

            _btnOpenSourceDir = new Button
            {
                Height = 32,
                Margin = new Thickness(0, 0, 6, 0),
                Background = new SolidColorBrush(Color.FromRgb(38, 34, 16)),
                Foreground = new SolidColorBrush(Color.FromRgb(250, 208, 44)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(250, 208, 44)),
                BorderThickness = new Thickness(1.2),
                FontWeight = FontWeights.Bold,
                FontSize = 10.5,
                Cursor = Cursors.Hand
            };
            _btnOpenSourceDir.Click += (_, __) =>
            {
                BuilderCoreSidebarAndProjectEngine.EnsureUnifiedSourceDirectoryStructure(win);
                string src = TacticalConfigPanel.GetPrimarySourceDirectory(win);
                TacticalConfigPanel.TryOpenFolderInExplorer(src);
            };
            Grid.SetColumn(_btnOpenSourceDir, 3);
            controlsRow.Children.Add(_btnOpenSourceDir);

            _btnBuildAllFactions = new Button
            {
                Height = 32,
                Background = TacticalConfigPanel.BrushItemBg,
                Foreground = TacticalConfigPanel.BrushAccent,
                BorderBrush = TacticalConfigPanel.BrushAccent,
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.Bold,
                FontSize = 10.5,
                Cursor = Cursors.Hand
            };
            _btnBuildAllFactions.Click += async (_, __) =>
            {
                SyncActiveFactionComboItems(TacticalConfigPanel.GetConfiguredFactions());
                await CleanFactionPackageEngine.BuildIsolatedFactionPackagesAsync(
                    win, null, showQrModalAfter: false,
                    msg => { if (_auditStatusLabel != null) _auditStatusLabel.Text = msg; });
            };
            Grid.SetColumn(_btnBuildAllFactions, 4);
            controlsRow.Children.Add(_btnBuildAllFactions);

            stack.Children.Add(controlsRow);

            _auditStatusLabel = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(46, 204, 113)),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 6, 0, 0)
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
                    "🎯 GENERATOR PACZEK .FTAK v3.0 (JEDNA STRONA = JEDEN FOLDER out\\<STRONA>\\ + WSPÓLNY SOURCE/ APK & MAPY)",
                    "🎯 .FTAK PACKAGE BUILDER v3.0 (ONE FACTION = ONE OUTPUT FOLDER out\\<FACTION>\\ + SHARED SOURCE/ APK & MAPS)");
            }
            if (_bannerDescBlock != null)
            {
                _bannerDescBlock.Text = BuilderI18n.T(
                    "Każda strona konfliktu zapisywana jest do własnego, czystego katalogu out\\<STRONA>\\. Wspólne pliki .apk (ATAK, Meshtastic, pluginy), DataPackage i mapy .kmz z folderu source/ są automatycznie weryfikowane pod kątem kompatybilności wersji z OpenTAKServer:",
                    "Each faction outputs to its own clean directory out\\<FACTION>\\. Shared .apk files (ATAK, Meshtastic, plugins), DataPackages, and .kmz maps from source/ are automatically indexed for OpenTAKServer version compatibility:");
            }
            if (_btnBuildSingleFaction != null)
                _btnBuildSingleFaction.Content = BuilderI18n.T("📦 Buduj out\\<STRONA>\\.ftak", "📦 Build out\\<FACTION>\\.ftak");
            if (_btnShowActiveQr != null)
                _btnShowActiveQr.Content = BuilderI18n.T("📱 Kod QR (.FTAK / OPORD LAN)", "📱 QR Code (.FTAK / OPORD LAN)");
            if (_btnOpenSourceDir != null)
                _btnOpenSourceDir.Content = BuilderI18n.T("📂 Wspólny folder source/ (APK/KMZ)", "📂 Shared source/ Folder (APK/KMZ)");
            if (_btnBuildAllFactions != null)
                _btnBuildAllFactions.Content = BuilderI18n.T("⚡ Buduj WSZYSTKIE strony", "⚡ Build ALL Factions");
            if (_auditStatusLabel != null && string.IsNullOrWhiteSpace(_auditStatusLabel.Text))
            {
                _auditStatusLabel.Text = BuilderI18n.T(
                    "✓ Tryb czystego zapisu aktywny: jedna strona = jeden folder wyjściowy out\\<STRONA>\\ (brak zbędnych folderów tymczasowych).",
                    "✓ Clean output mode active: one faction = one output folder out\\<FACTION>\\ (no redundant temporary folders).");
            }
        }

        private static void SyncActiveFactionComboItems(List<(string Name, string Channel, string ColorHex, string Psk)> configured)
        {
            if (_activeFactionCombo == null || configured == null || configured.Count == 0) return;
            var names = configured.Select(c => c.Name).ToList();
            bool changed = _activeFactionCombo.Items.Count != names.Count;
            if (!changed)
            {
                for (int i = 0; i < names.Count; i++)
                {
                    if (!string.Equals(_activeFactionCombo.Items[i]?.ToString(), names[i], StringComparison.Ordinal))
                    {
                        changed = true;
                        break;
                    }
                }
            }

            if (changed)
            {
                string prev = _selectedActiveFactionName;
                _activeFactionCombo.ItemsSource = names;
                if (!string.IsNullOrEmpty(prev) && names.Contains(prev))
                    _activeFactionCombo.SelectedItem = prev;
                else
                    _activeFactionCombo.SelectedIndex = 0;
            }
        }

        private static bool IsAncestorOf(DependencyObject ancestor, DependencyObject descendant)
        {
            DependencyObject curr = descendant;
            while (curr != null)
            {
                if (curr == ancestor) return true;
                curr = VisualTreeHelper.GetParent(curr);
            }
            return false;
        }

        private static List<string> GetElementTexts(DependencyObject root)
        {
            var list = new List<string>();
            if (root == null) return list;
            foreach (var d in GetAllVisualDescendants(root))
            {
                if (d is TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text))
                    list.Add(tb.Text.Trim());
            }
            return list;
        }

        private static IEnumerable<DependencyObject> GetAllVisualDescendants(DependencyObject root)
        {
            if (root == null) yield break;
            var queue = new Queue<DependencyObject>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                int count = 0;
                try { count = VisualTreeHelper.GetChildrenCount(current); } catch { }
                for (int i = 0; i < count; i++)
                {
                    DependencyObject child = null;
                    try { child = VisualTreeHelper.GetChild(current, i); } catch { }
                    if (child != null)
                    {
                        yield return child;
                        queue.Enqueue(child);
                    }
                }
            }
        }
    }
}