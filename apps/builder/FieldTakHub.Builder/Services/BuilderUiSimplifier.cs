#pragma warning disable CS0414, CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8625
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    internal sealed class CleanFactionProfileModel
    {
        public string Name { get; set; } = "BLUFOR";
        public string ColorHex { get; set; } = "#2684FF";
        public string ChannelName { get; set; } = "BLUFOR-TAK";
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
        public string EventName { get; set; } = "OPERACJA";
        public List<CleanFactionProfileModel> Factions { get; set; } = new List<CleanFactionProfileModel>();
    }

    internal static class CleanFactionPackageEngine
    {
        private static TcpListener _pcHotspotListener;
        private static volatile bool _pcServerRunning;
        private static int _pcServerPort = 8766;
        private static readonly Dictionary<string, string> ServedFtakPathsByFaction = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static async Task<string> BuildIsolatedFactionPackagesAsync(
            Window win,
            string onlyFactionName,
            bool showQrModalAfter,
            Action<string> onStatusUpdated = null)
        {
            try
            {
                OpordBuilderManager.SaveAllOpordsToDisk(win);

                var snapshot = ExtractWorkspaceSnapshot(win);
                string eventName = snapshot.EventName;
                string sourceDir = TacticalConfigPanel.GetPrimarySourceDirectory(win);
                string outBaseDir = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(sourceDir)) ?? AppDomain.CurrentDomain.BaseDirectory;
                string releaseDir = Path.Combine(outBaseDir, "out", "release_factions");
                Directory.CreateDirectory(releaseDir);

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
                CleanFactionProfileModel lastBuiltFaction = null;
                int totalIncludedFiles = 0;

                await Task.Run(() =>
                {
                    foreach (var fac in targetFactions)
                    {
                        string safeEv = TacticalConfigPanel.SanitizeFileName(eventName);
                        string safeFac = TacticalConfigPanel.SanitizeFileName(fac.Name);
                        string ftakPath = Path.Combine(releaseDir, safeEv + "_" + safeFac + ".ftak");

                        int filesCount = WriteSingleAuditedFactionFtakArchive(win, ftakPath, eventName, fac, sourceDir);
                        ServedFtakPathsByFaction[fac.Name] = ftakPath;
                        lastBuiltFtakPath = ftakPath;
                        lastBuiltFaction = fac;
                        totalIncludedFiles += filesCount;
                    }
                });

                string serverBaseUrl = EnsurePcFtakHttpServerStarted();

                if (lastBuiltFaction != null)
                {
                    string otsLabel = string.IsNullOrWhiteSpace(lastBuiltFaction.OtsHost) ? "Mesh" : lastBuiltFaction.OtsHost;
                    string msg = BuilderI18n.T(
                        "✓ Zbudowano paczkę .FTAK: " + Path.GetFileName(lastBuiltFtakPath) + "  |  Strona: " + lastBuiltFaction.Name + "  |  OTS: " + otsLabel + "  |  Kanały radiowe: " + lastBuiltFaction.VoiceChannels.Count + "  |  Załączniki (Mapy + OPORD): " + totalIncludedFiles,
                        "✓ Built .FTAK package: " + Path.GetFileName(lastBuiltFtakPath) + "  |  Faction: " + lastBuiltFaction.Name + "  |  OTS: " + otsLabel + "  |  Voice channels: " + lastBuiltFaction.VoiceChannels.Count + "  |  Assets (Maps + OPORD): " + totalIncludedFiles);
                    onStatusUpdated?.Invoke(msg);
                }

                if (showQrModalAfter && lastBuiltFaction != null)
                {
                    ShowActiveFactionQrModal(win, eventName, lastBuiltFaction, lastBuiltFtakPath, serverBaseUrl);
                }
                else if (!string.IsNullOrEmpty(releaseDir) && Directory.Exists(releaseDir))
                {
                    TacticalConfigPanel.TryOpenFolderInExplorer(releaseDir);
                }

                return lastBuiltFtakPath;
            }
            catch (Exception ex)
            {
                onStatusUpdated?.Invoke("⚠️ Błąd budowy .FTAK: " + ex.Message);
                return "";
            }
        }

        private static int WriteSingleAuditedFactionFtakArchive(
            Window win,
            string ftakZipPath,
            string eventName,
            CleanFactionProfileModel fac,
            string sourceDir)
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

            var opordModel = OpordBuilderManager.GetOrCreateModel(fac.Name);
            var opordSummaryNode = new JsonObject
            {
                ["operationNumber"] = opordModel.OperationNumber,
                ["operationDate"] = opordModel.OperationDateIso,
                ["locationName"] = opordModel.LocationName,
                ["missionStatement"] = string.IsNullOrWhiteSpace(opordModel.MissionFullStatement)
                    ? OpordBuilderManager.Compose5WMissionText(opordModel)
                    : opordModel.MissionFullStatement,
                ["weatherCondition"] = opordModel.MetocCondition,
                ["temperature"] = opordModel.MetocTemperature,
                ["wind"] = opordModel.MetocWind,
                ["sunTimes"] = opordModel.MetocSunTimes,
                ["moonPhase"] = opordModel.MetocMoonPhase,
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
                ["OpordSummary"] = JsonNode.Parse(opordSummaryNode.ToJsonString())
            };

            var rootJson = new JsonObject
            {
                ["schemaVersion"] = "2.5",
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

            foreach (string metaName in new[] { "config.json", "manifest.json" })
            {
                var e = zip.CreateEntry(metaName, CompressionLevel.Optimal);
                using var s = e.Open();
                s.Write(jsonBytes, 0, jsonBytes.Length);
            }

            if (Directory.Exists(sourceDir))
            {
                var allSourceFiles = Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories);
                foreach (string filePath in allSourceFiles)
                {
                    string ext = Path.GetExtension(filePath).ToLowerInvariant();
                    bool isAllowedAsset =
                        ext == ".mbtiles" || ext == ".sqlite" ||
                        ext == ".kmz" || ext == ".kml" || ext == ".gpx" ||
                        ext == ".p12" || ext == ".pref" || ext == ".html";

                    if (!isAllowedAsset) continue;

                    string relPath = Path.GetRelativePath(sourceDir, filePath).Replace('\\', '/').TrimStart('/');
                    string relLower = relPath.ToLowerInvariant();

                    if (relLower.StartsWith("factions/"))
                    {
                        string[] parts = relLower.Split('/');
                        if (parts.Length >= 2 && parts[1] != facSlug)
                            continue;
                    }
                    else
                    {
                        if (hasFactionSpecificVectors && (ext == ".kmz" || ext == ".gpx"))
                            continue;
                    }

                    var fileEntry = zip.CreateEntry(relPath, CompressionLevel.Optimal);
                    using var entryStream = fileEntry.Open();
                    using var srcStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    srcStream.CopyTo(entryStream);
                    includedAssetsCount++;
                }
            }

            return includedAssetsCount;
        }

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

            var offlineJsonObj = new JsonObject
            {
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
                    "📱 Kod QR Aktywnej Strony: " + fac.Name + " (" + eventName + ")",
                    "📱 Active Faction QR Code: " + fac.Name + " (" + eventName + ")"),
                Width = 500,
                Height = 620,
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
                Text = "LoRa: " + fac.ChannelName + " (" + fac.LoraRegion + ")  |  OTS: " + otsInfo + "  |  .FTAK: " + kbSize + " KB",
                Foreground = Brushes.White,
                FontSize = 10.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 0)
            });
            hdrBorder.Child = hdrStack;
            root.Children.Add(hdrBorder);

            var modeGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 10) };
            var btnModeWifi = new Button
            {
                Content = BuilderI18n.T("📡 Tryb 1: Pobierz pełny .FTAK po Wi-Fi", "📡 Mode 1: Full .FTAK via Wi-Fi"),
                Height = 30,
                Margin = new Thickness(0, 0, 4, 0),
                Background = TacticalConfigPanel.BrushButtonAccent,
                Foreground = TacticalConfigPanel.BrushDarkFg,
                FontWeight = FontWeights.Bold,
                FontSize = 10.5,
                Cursor = Cursors.Hand
            };
            var btnModeOffline = new Button
            {
                Content = BuilderI18n.T("📴 Tryb 2: Kod QR Offline (Sam JSON)", "📴 Mode 2: Offline Config QR (JSON)"),
                Height = 30,
                Margin = new Thickness(4, 0, 0, 0),
                Background = TacticalConfigPanel.BrushInputBg,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 10.5,
                Cursor = Cursors.Hand
            };
            modeGrid.Children.Add(btnModeWifi);
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

            void LoadQrPayload(string dataStr, bool isWifiMode)
            {
                payloadBox.Text = isWifiMode
                    ? wifiDownloadUrl
                    : ("[Offline JSON: " + fac.Name + " | LoRa: " + fac.ChannelName + " | Voice: " + fac.VoiceChannels.Count + "]");
                btnModeWifi.Background = isWifiMode ? TacticalConfigPanel.BrushButtonAccent : TacticalConfigPanel.BrushInputBg;
                btnModeWifi.Foreground = isWifiMode ? TacticalConfigPanel.BrushDarkFg : Brushes.White;
                btnModeOffline.Background = !isWifiMode ? TacticalConfigPanel.BrushButtonAccent : TacticalConfigPanel.BrushInputBg;
                btnModeOffline.Foreground = !isWifiMode ? TacticalConfigPanel.BrushDarkFg : Brushes.White;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        string cacheKey = "QR_CLEAN_" + dataStr;
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

            btnModeWifi.Click += (_, __) => LoadQrPayload(wifiDownloadUrl, true);
            btnModeOffline.Click += (_, __) => LoadQrPayload(offlineJsonPayload, false);
            LoadQrPayload(wifiDownloadUrl, true);

            var bottomBtns = new UniformGrid { Columns = 2 };
            var btnOpenDir = new Button
            {
                Content = BuilderI18n.T("📂 Otwórz folder z plikiem .FTAK", "📂 Open .FTAK Output Folder"),
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
                            ChannelName = item["ChannelName"]?.ToString() ?? item["channelName"]?.ToString() ?? "TAK",
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

            Button mapTabBtn = null;
            Button builderTabBtn = null;

            foreach (var btn in allElements.OfType<Button>())
            {
                string txt = string.Join(" ", GetElementTexts(btn)).Trim();
                if (btn.Content is string s) txt = (txt + " " + s).Trim();

                if (txt.Contains("Generator Map Taktycznych 1:1", StringComparison.OrdinalIgnoreCase) ||
                    txt.Contains("1:1 Tactical Map", StringComparison.OrdinalIgnoreCase))
                {
                    mapTabBtn = btn;
                }
                else if (txt.Contains("Konfigurator Paczek", StringComparison.OrdinalIgnoreCase) ||
                         txt.Contains("Faction & Package Builder", StringComparison.OrdinalIgnoreCase))
                {
                    builderTabBtn = btn;
                }
            }

            if (mapTabBtn != null && VisualTreeHelper.GetParent(mapTabBtn) is Panel topNavPanel)
            {
                if (_topTabOpordBtn == null || !topNavPanel.Children.Contains(_topTabOpordBtn))
                {
                    _topTabOpordBtn = new Button
                    {
                        Content = BuilderI18n.T("📋 Generator Rozkazu (OPORD)", "📋 NATO OPORD Builder"),
                        Height = mapTabBtn.Height > 0 ? mapTabBtn.Height : 32,
                        Padding = new Thickness(12, 4, 12, 4),
                        Margin = new Thickness(4, 0, 4, 0),
                        Background = TacticalConfigPanel.BrushItemBg,
                        Foreground = Brushes.White,
                        BorderBrush = TacticalConfigPanel.BrushAccent,
                        BorderThickness = new Thickness(1.2),
                        FontWeight = FontWeights.Bold,
                        FontSize = 11.5,
                        Cursor = Cursors.Hand,
                        Tag = "INJECTED_OPORD_TAB_BTN"
                    };

                    _topTabOpordBtn.Click += (_, __) => ShowOpordView(win, true);

                    int mapIdx = topNavPanel.Children.IndexOf(mapTabBtn);
                    if (mapIdx >= 0)
                        topNavPanel.Children.Insert(mapIdx + 1, _topTabOpordBtn);
                    else
                        topNavPanel.Children.Add(_topTabOpordBtn);
                }

                if (_topLangToggleBtn == null || !topNavPanel.Children.Contains(_topLangToggleBtn))
                {
                    _topLangToggleBtn = new Button
                    {
                        Content = "🌐 " + BuilderI18n.CurrentLang,
                        ToolTip = "Przełącz język interfejsu: Polski (PL) / Angielski (EN)",
                        Height = mapTabBtn.Height > 0 ? mapTabBtn.Height : 32,
                        Padding = new Thickness(10, 4, 10, 4),
                        Margin = new Thickness(6, 0, 4, 0),
                        Background = new SolidColorBrush(Color.FromRgb(18, 44, 54)),
                        Foreground = TacticalConfigPanel.BrushAccent,
                        BorderBrush = TacticalConfigPanel.BrushAccent,
                        BorderThickness = new Thickness(1.2),
                        FontWeight = FontWeights.Bold,
                        FontSize = 11.5,
                        Cursor = Cursors.Hand,
                        Tag = "INJECTED_LANG_BTN"
                    };

                    _topLangToggleBtn.Click += (_, __) => BuilderI18n.ToggleLanguage(win);
                    topNavPanel.Children.Add(_topLangToggleBtn);
                }

                if (builderTabBtn != null && builderTabBtn.Tag?.ToString() != "OPORD_HOOKED")
                {
                    builderTabBtn.Tag = "OPORD_HOOKED";
                    builderTabBtn.Click += (_, __) => ShowOpordView(win, false);
                }
                if (mapTabBtn.Tag?.ToString() != "OPORD_HOOKED")
                {
                    mapTabBtn.Tag = "OPORD_HOOKED";
                    mapTabBtn.Click += (_, __) => ShowOpordView(win, false);
                }
            }
        }

        public static void ShowOpordView(Window win, bool showOpord)
        {
            var opordOverlay = OpordBuilderManager.CreateOrGetOpordOverlayView(win);
            if (showOpord)
            {
                foreach (var fe in GetAllVisualDescendants(win).OfType<FrameworkElement>())
                {
                    if (string.Equals(fe.Tag?.ToString(), "INJECTED_MAP_OVERLAY", StringComparison.Ordinal))
                    {
                        fe.Visibility = Visibility.Collapsed;
                    }
                }
                opordOverlay.Visibility = Visibility.Visible;
                if (_topTabOpordBtn != null)
                {
                    _topTabOpordBtn.Background = TacticalConfigPanel.BrushButtonAccent;
                    _topTabOpordBtn.Foreground = TacticalConfigPanel.BrushDarkFg;
                }
            }
            else
            {
                opordOverlay.Visibility = Visibility.Collapsed;
                if (_topTabOpordBtn != null)
                {
                    _topTabOpordBtn.Background = TacticalConfigPanel.BrushItemBg;
                    _topTabOpordBtn.Foreground = Brushes.White;
                }
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
                    if (tag == "INJECTED_MAP_OVERLAY" || tag == "INJECTED_OPORD_OVERLAY") break;

                    if (fe.Visibility != Visibility.Visible)
                    {
                        DependencyObject parent = VisualTreeHelper.GetParent(fe);
                        bool isMasterTabSwitch = parent is Grid g && g.Children.Cast<UIElement>().Any(c =>
                            (c as FrameworkElement)?.Tag?.ToString() == "INJECTED_MAP_OVERLAY" ||
                            (c as FrameworkElement)?.Tag?.ToString() == "INJECTED_OPORD_OVERLAY");
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

        private static TextBlock _bannerTitleBlock;
        private static TextBlock _bannerDescBlock;
        private static Button _btnBuildSingleFaction;
        private static Button _btnShowActiveQr;
        private static Button _btnOpenOpordQuick;
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
            controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
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

            _btnOpenOpordQuick = new Button
            {
                Height = 32,
                Margin = new Thickness(0, 0, 6, 0),
                Background = new SolidColorBrush(Color.FromRgb(28, 48, 36)),
                Foreground = new SolidColorBrush(Color.FromRgb(46, 204, 113)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(46, 204, 113)),
                BorderThickness = new Thickness(1.2),
                FontWeight = FontWeights.Bold,
                FontSize = 10.5,
                Cursor = Cursors.Hand
            };
            _btnOpenOpordQuick.Click += (_, __) => ShowOpordView(win, true);
            Grid.SetColumn(_btnOpenOpordQuick, 3);
            controlsRow.Children.Add(_btnOpenOpordQuick);

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
                    "🎯 CENTRUM WDROŻENIA AKTYWNEJ STRONY (.FTAK 1:1 + ROZKAZ OPORD + KOD QR NA EKRANIE PC)",
                    "🎯 ACTIVE FACTION DEPLOYMENT HUB (.FTAK 1:1 + NATO OPORD + PC SCREEN QR CODE)");
            }
            if (_bannerDescBlock != null)
            {
                _bannerDescBlock.Text = BuilderI18n.T(
                    "Wybierz aktywną stronę konfliktu poniżej, aby zbudować dla niej w 100% odizolowaną paczkę .FTAK (Fog of War: tylko klucze PSK, serwer OTS, tabela radiowa, mapy i rozkaz OPORD tej strony) lub wyświetlić Kod QR do zeskanowania telefonem:",
                    "Select the active faction below to build a 100% isolated .FTAK bundle (Fog of War: only PSK keys, OTS server, voice channels, maps, and OPORD for that faction) or display the QR Code for mobile scanning:");
            }
            if (_btnBuildSingleFaction != null)
                _btnBuildSingleFaction.Content = BuilderI18n.T("📦 Buduj .FTAK aktywnej strony", "📦 Build Active Faction .FTAK");
            if (_btnShowActiveQr != null)
                _btnShowActiveQr.Content = BuilderI18n.T("📱 Pokaż Kod QR aktywnej strony", "📱 Show Active Faction QR");
            if (_btnOpenOpordQuick != null)
                _btnOpenOpordQuick.Content = BuilderI18n.T("📋 Edytuj Rozkaz OPORD + METOC", "📋 Edit OPORD + METOC Weather");
            if (_btnBuildAllFactions != null)
                _btnBuildAllFactions.Content = BuilderI18n.T("⚡ Buduj .FTAK WSZYSTKICH stron", "⚡ Build ALL Factions .FTAK");
            if (_auditStatusLabel != null && string.IsNullOrWhiteSpace(_auditStatusLabel.Text))
            {
                _auditStatusLabel.Text = BuilderI18n.T(
                    "✓ Izolacja stron (Fog of War) aktywna: każda paczka .ftak zawiera wyłącznie zasoby, mapy i rozkaz OPORD wybranej strony.",
                    "✓ Faction isolation (Fog of War) active: each .ftak bundle contains only assets, maps, and OPORD for the selected faction.");
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