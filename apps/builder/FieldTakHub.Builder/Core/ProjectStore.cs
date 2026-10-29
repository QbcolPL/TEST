#nullable disable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace FieldTakHub.Builder.Core
{
    /// <summary>
    /// Centralny Magazyn Stanu Projektu (MVVM Single Source of Truth) oraz Manager Zasobów Wspólnych i Pakietów .FTAK.
    /// Wszystkie 4 moduły aplikacji pracują bezpośrednio na tym obiekcie — bez przeszukiwania drzewa wizualnego.
    /// </summary>
    public sealed class ProjectStore : ObservableObject
    {
        public static ProjectStore Instance { get; } = new ProjectStore();

        private HttpListener _hotspotListener;
        private const int HotspotPort = 8769;
        private string _latestHotspotPayloadJson = "{}";

        public ObservableCollection<FactionProfileModel> Factions { get; } = new ObservableCollection<FactionProfileModel>();
        public TacticalMapConfigModel MapConfig { get; } = new TacticalMapConfigModel();
        public OpordDocumentModel OpordDocument { get; } = new OpordDocumentModel();
        public ObservableCollection<UsbBoardDeviceModel> UsbDevices { get; } = new ObservableCollection<UsbBoardDeviceModel>();

        private ProjectStore()
        {
            EnsureWorkspaceFolders();
            InitializeDefaultProjectState();
        }

        public string WorkspaceRootDirectory
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FieldTakHub");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public string SharedFolderDirectory
        {
            get
            {
                string shared = Path.Combine(WorkspaceRootDirectory, "wspolne");
                Directory.CreateDirectory(Path.Combine(shared, "apk"));
                Directory.CreateDirectory(Path.Combine(shared, "plugins"));
                Directory.CreateDirectory(Path.Combine(shared, "maps"));
                return shared;
            }
        }

        public string OutputRootDirectory
        {
            get
            {
                string outDir = Path.Combine(WorkspaceRootDirectory, "out");
                Directory.CreateDirectory(outDir);
                return outDir;
            }
        }

        public void EnsureWorkspaceFolders()
        {
            _ = SharedFolderDirectory;
            _ = OutputRootDirectory;
        }

        public string GetFactionOutputDirectory(string factionName)
        {
            string safeName = SanitizeFileName(string.IsNullOrWhiteSpace(factionName) ? "ORGA" : factionName);
            string dir = Path.Combine(OutputRootDirectory, safeName);
            Directory.CreateDirectory(dir);
            return dir;
        }

        public void OpenSharedFolderInExplorer()
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + SharedFolderDirectory + "\"") { UseShellExecute = true });
            }
            catch { }
        }

        public void OpenFactionFolderInExplorer(FactionProfileModel faction)
        {
            try
            {
                string dir = GetFactionOutputDirectory(faction != null ? faction.Name : "ORGA");
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true });
            }
            catch { }
        }

        private void InitializeDefaultProjectState()
        {
            // Strona 1: ORGA / BLUFOR
            var orga = new FactionProfileModel
            {
                Name = "ORGA",
                ColorHex = "#2684FF",
                ChannelName = "ORGA-MESH",
                PskBase64 = TacticalServices.GenerateAes256PskBase64(),
                LoraRegion = "EU_868",
                ModemPreset = "SHORT_FAST",
                OtsHost = "ggzstak.duckdns.org",
                EmergencyPhone = "+48 600 112 112",
                ExpirationHours = 0
            };
            orga.VoiceChannels.Add(new VoiceChannelModel { Role = "Dowodzenie HQ", Band = "PMR", Frequency = "CH 1 (446.00625 MHz)", Tone = "CTCSS 67.0 Hz" });
            orga.VoiceChannels.Add(new VoiceChannelModel { Role = "Kanał Szturmowy", Band = "PMR", Frequency = "CH 3 (446.03125 MHz)", Tone = "CTCSS 88.5 Hz" });
            orga.VoiceChannels.Add(new VoiceChannelModel { Role = "Kanał Ratunkowy SOS", Band = "VHF", Frequency = "145.500 MHz", Tone = "Brak tonu" });
            Factions.Add(orga);

            // Strona 2: STRONA-2 (z unikalnym kolorem z palety)
            var side2 = new FactionProfileModel
            {
                Name = "STRONA-2",
                ColorHex = "#EB4034",
                ChannelName = "STRONA2-MESH",
                PskBase64 = TacticalServices.GenerateAes256PskBase64(),
                LoraRegion = "EU_868",
                ModemPreset = "SHORT_FAST",
                OtsHost = "ggzstak.duckdns.org",
                EmergencyPhone = "+48 600 112 112",
                ExpirationHours = 0
            };
            side2.VoiceChannels.Add(new VoiceChannelModel { Role = "Dowodzenie Strony 2", Band = "PMR", Frequency = "CH 5 (446.05625 MHz)", Tone = "CTCSS 103.5 Hz" });
            side2.VoiceChannels.Add(new VoiceChannelModel { Role = "Zwiad / Flanka", Band = "PMR", Frequency = "CH 7 (446.08125 MHz)", Tone = "CTCSS 114.8 Hz" });
            Factions.Add(side2);

            // Domyślny Dzień 1 w rozkazie OPORD
            var day1 = CreateDefaultOpordDay(1);
            OpordDocument.Days.Add(day1);
            OpordDocument.TargetFaction = orga.Name;

            SyncGeneratorsToOpord();
        }

        public OpordDayPlanModel CreateDefaultOpordDay(int dayNumber)
        {
            var d = new OpordDayPlanModel
            {
                DayTitle = "DZIEŃ " + dayNumber,
                DateIso = DateTime.Now.AddDays(dayNumber - 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                OperationNumber = "OPORD 0" + dayNumber + "/2026",
                SarnegKeyWord10 = TacticalServices.GetRandomSarnegWord10()
            };

            d.SquadRoster.Add(new SquadMemberRoleModel { Callsign = "ALFA-1", PrimaryRole = "Lider (Dowódca Sekcji)", SecondaryRole = "Adiutant (2IC)", GearAndTasks = "ATAK + Heltec V3 + Radio HQ" });
            d.SquadRoster.Add(new SquadMemberRoleModel { Callsign = "ALFA-2", PrimaryRole = "Nawigator (Navi)", SecondaryRole = "Czujka / Szpica", GearAndTasks = "Mapa 1:5000 MGRS + Garmin GPX" });
            d.SquadRoster.Add(new SquadMemberRoleModel { Callsign = "ALFA-3", PrimaryRole = "RTO (Radiooperator)", SecondaryRole = "Fotograf / Dokumentalista", GearAndTasks = "Tabela Krypto SARNEG + Baofeng UV-5R" });
            d.SquadRoster.Add(new SquadMemberRoleModel { Callsign = "ALFA-4", PrimaryRole = "Medyk Polowy", SecondaryRole = "Organizator Obrony", GearAndTasks = "Apteczka zespołowa + Opatrunki MEDEVAC" });

            d.CustomOpordChannels.Add(new VoiceChannelModel { Role = "Kanał rezerwowy sekcji", Band = "PMR", Frequency = "CH 8 (446.09375 MHz)", Tone = "CTCSS 123.0 Hz" });
            return d;
        }

        /// <summary>
        /// Dodaje nową stronę konfliktu z automatycznie wygenerowaną nazwą i gwarantowanie unikalnym kolorem RGB.
        /// </summary>
        public FactionProfileModel AddNewFaction()
        {
            int nextNum = Factions.Count + 1;
            while (Factions.Any(f => string.Equals(f.Name, "STRONA-" + nextNum, StringComparison.OrdinalIgnoreCase)))
            {
                nextNum++;
            }

            string newName = "STRONA-" + nextNum;
            string uniqueHex = TacticalServices.GenerateUniqueFactionColorHex(Factions.Select(f => f.ColorHex));

            var newFac = new FactionProfileModel
            {
                Name = newName,
                ColorHex = uniqueHex,
                ChannelName = newName + "-MESH",
                PskBase64 = TacticalServices.GenerateAes256PskBase64(),
                LoraRegion = "EU_868",
                ModemPreset = "SHORT_FAST",
                OtsHost = "ggzstak.duckdns.org",
                EmergencyPhone = "+48 600 112 112",
                ExpirationHours = 0
            };

            int defaultPmrCh = ((nextNum * 2) % 16) + 1;
            newFac.VoiceChannels.Add(new VoiceChannelModel
            {
                Role = "Dowodzenie " + newName,
                Band = "PMR",
                Frequency = "CH " + defaultPmrCh + " (PMR 446)",
                Tone = "CTCSS 94.8 Hz"
            });

            Factions.Add(newFac);
            _ = GetFactionOutputDirectory(newFac.Name);
            return newFac;
        }

        /// <summary>
        /// Synchronizuje listę stron, kanały głosowe, parametry Meshtastic oraz punkty AO z Generatora Map do aktywnego rozkazu OPORD.
        /// </summary>
        public void SyncGeneratorsToOpord()
        {
            if (Factions.Count == 0) return;

            FactionProfileModel targetFac = Factions.FirstOrDefault(f =>
                string.Equals(f.Name, OpordDocument.TargetFaction, StringComparison.OrdinalIgnoreCase)) ?? Factions[0];

            OpordDocument.TargetFaction = targetFac.Name;

            var commsSb = new StringBuilder();
            commsSb.AppendLine("📡 [AUTOMATYCZNA MATRYCA ŁĄCZNOŚCI Z GENERATORA STRON — STRONA: " + targetFac.Name + "]");
            commsSb.AppendLine("• Sieć LoRa Meshtastic: Kanał '" + targetFac.ChannelName + "' | Region: " + targetFac.LoraRegion + " | Preset: " + targetFac.ModemPreset + " | Klucz: AES-256 PSK");
            commsSb.AppendLine("• Serwer ATAK (OpenTAKServer): " + (string.IsNullOrWhiteSpace(targetFac.OtsHost) ? "Tryb lokalny Off-Grid" : (targetFac.OtsHost + ":" + targetFac.OtsPort)));
            commsSb.AppendLine("• Telefon alarmowy ORGA / MEDEVAC (SOS MGRS): " + (string.IsNullOrWhiteSpace(targetFac.EmergencyPhone) ? "112" : targetFac.EmergencyPhone));
            commsSb.AppendLine("• Przypisane kanały głosowe (" + targetFac.VoiceChannels.Count + "):");

            foreach (var vc in targetFac.VoiceChannels)
            {
                commsSb.AppendLine("   ▸ " + vc.FormattedSummary);
            }

            string commsText = commsSb.ToString().TrimEnd();

            string mapPoisText = null;
            if (MapConfig.HasExplicitAoMarked)
            {
                var center = MapConfig.GetExplicitAoCenter();
                string centerCoord = GeoCoordinateEngine.FormatBySystem(center.Item1, center.Item2, MapConfig.CoordinateSystem, MapConfig.SubdivisionMode);
                var mapSb = new StringBuilder();
                mapSb.AppendLine("🗺️ Układ współrzędnych mapy: " + MapConfig.CoordinateSystem + " (Skala 1:" + MapConfig.ScaleDenominator + ") | Środek AO: " + centerCoord);
                foreach (var p in MapConfig.Pois)
                {
                    string pCoord = GeoCoordinateEngine.FormatBySystem(p.Lat, p.Lon, MapConfig.CoordinateSystem, MapConfig.SubdivisionMode);
                    mapSb.AppendLine("   📍 " + p.Name + " [" + p.Category + "]: " + pCoord + (string.IsNullOrWhiteSpace(p.Notes) ? "" : (" — " + p.Notes)));
                }
                mapPoisText = mapSb.ToString().TrimEnd();
            }

            foreach (var day in OpordDocument.Days)
            {
                day.SyncedCommsMatrixSummary = commsText;
                if (mapPoisText != null)
                {
                    var center = MapConfig.GetExplicitAoCenter();
                    day.LocationLat = center.Item1.ToString("F5", CultureInfo.InvariantCulture);
                    day.LocationLon = center.Item2.ToString("F5", CultureInfo.InvariantCulture);
                    day.AreaOfInterestPoiDesc = mapPoisText;
                }
            }
        }

        public string BuildFactionManifestJson(FactionProfileModel fac)
        {
            if (fac == null) fac = Factions.FirstOrDefault() ?? new FactionProfileModel();
            var vcJson = new StringBuilder("[");
            for (int i = 0; i < fac.VoiceChannels.Count; i++)
            {
                var v = fac.VoiceChannels[i];
                if (i > 0) vcJson.Append(",");
                vcJson.Append("{\"role\":\"" + EscapeJson(v.Role) + "\",\"band\":\"" + EscapeJson(v.Band) + "\",\"freq\":\"" + EscapeJson(v.Frequency) + "\",\"tone\":\"" + EscapeJson(v.Tone) + "\"}");
            }
            vcJson.Append("]");

            return "{" +
                   "\"schema\":\"FTAK-4.0\"," +
                   "\"event\":\"" + EscapeJson(OpordDocument.EventName) + "\"," +
                   "\"faction\":\"" + EscapeJson(fac.Name) + "\"," +
                   "\"colorHex\":\"" + EscapeJson(fac.ColorHex) + "\"," +
                   "\"loraChannel\":\"" + EscapeJson(fac.ChannelName) + "\"," +
                   "\"pskBase64\":\"" + EscapeJson(fac.PskBase64) + "\"," +
                   "\"loraRegion\":\"" + EscapeJson(fac.LoraRegion) + "\"," +
                   "\"modemPreset\":\"" + EscapeJson(fac.ModemPreset) + "\"," +
                   "\"otsHost\":\"" + EscapeJson(fac.OtsHost) + "\"," +
                   "\"otsPort\":" + fac.OtsPort + "," +
                   "\"emergencyPhone\":\"" + EscapeJson(fac.EmergencyPhone) + "\"," +
                   "\"expirationHours\":" + fac.ExpirationHours + "," +
                   "\"isPermanent\":" + (fac.IsPermanent ? "true" : "false") + "," +
                   "\"voiceChannels\":" + vcJson +
                   "}";
        }

        public int BuildAllActiveFactionPackages()
        {
            EnsureWorkspaceFolders();
            int builtCount = 0;

            foreach (var fac in Factions)
            {
                string outDir = GetFactionOutputDirectory(fac.Name);
                string manifestJson = BuildFactionManifestJson(fac);
                string jsonPath = Path.Combine(outDir, SanitizeFileName(fac.Name) + "_manifest.json");
                File.WriteAllText(jsonPath, manifestJson, Encoding.UTF8);

                string ftakZipPath = Path.Combine(outDir, SanitizeFileName(fac.Name) + "_package.ftak");
                if (File.Exists(ftakZipPath)) File.Delete(ftakZipPath);

                using (var fs = new FileStream(ftakZipPath, FileMode.Create))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    var entry = zip.CreateEntry("manifest.json");
                    using (var sw = new StreamWriter(entry.Open(), Encoding.UTF8))
                    {
                        sw.Write(manifestJson);
                    }
                }
                builtCount++;
            }

            return builtCount;
        }

        public string GenerateSafezonePosterHtml(FactionProfileModel fac, bool openInBrowser = true)
        {
            if (fac == null) fac = Factions.FirstOrDefault() ?? new FactionProfileModel();
            string outDir = GetFactionOutputDirectory(fac.Name);
            string htmlPath = Path.Combine(outDir, "Plakat_Safezone_A4_" + SanitizeFileName(fac.Name) + ".html");

            string manifestJson = BuildFactionManifestJson(fac);
            string qrEncoded = Uri.EscapeDataString(manifestJson);
            string hotspotUrl = StartOrUpdateHotspotServer(fac);

            var rows = new StringBuilder();
            foreach (var vc in fac.VoiceChannels)
            {
                rows.AppendLine("<tr><td><b>" + WebUtility.HtmlEncode(vc.Role) + "</b></td>" +
                                "<td>" + WebUtility.HtmlEncode(vc.Band) + "</td>" +
                                "<td style='color:#1FE0C5;font-family:Consolas,monospace;font-weight:bold;'>" + WebUtility.HtmlEncode(vc.Frequency) + "</td>" +
                                "<td>" + WebUtility.HtmlEncode(vc.Tone) + "</td></tr>");
            }

            string html = "<!DOCTYPE html><html><head><meta charset='utf-8'/>" +
                          "<title>Plakat Safezone A4 — " + WebUtility.HtmlEncode(fac.Name) + "</title>" +
                          "<style>" +
                          "body{font-family:'Segoe UI',Arial,sans-serif;background:#060D10;color:#E8F1F5;margin:0;padding:20px;}" +
                          ".sheet{max-width:190mm;margin:0 auto;background:#0A151B;border:3px solid " + WebUtility.HtmlEncode(fac.ColorHex) + ";border-radius:10px;padding:22px;}" +
                          ".badge{display:inline-block;padding:4px 12px;background:" + WebUtility.HtmlEncode(fac.ColorHex) + ";color:#000;font-weight:bold;border-radius:4px;}" +
                          "table{width:100%;border-collapse:collapse;margin-top:14px;}" +
                          "th,td{border:1px solid #1E3A46;padding:8px 10px;text-align:left;font-size:13px;}" +
                          "th{background:#102530;color:#1FE0C5;}" +
                          "@media print{body{background:#fff;color:#000;}.sheet{border-color:#000;background:#fff;}}" +
                          "</style></head><body>" +
                          "<div class='sheet'>" +
                          "<div style='display:flex;justify-content:space-between;align-items:center;'>" +
                          "<h1 style='margin:0;font-size:24px;'>🛡️ STRONA: " + WebUtility.HtmlEncode(fac.Name) + "</h1>" +
                          "<span class='badge'>" + WebUtility.HtmlEncode(OpordDocument.EventName) + "</span></div>" +
                          "<p style='margin-top:10px;'><b>Ważność konfiguracji:</b> " + WebUtility.HtmlEncode(fac.ExpirationDisplayText) +
                          " &nbsp;|&nbsp; <b>Telefon Alarmowy SOS (MGRS):</b> <span style='color:#FAD02C;font-weight:bold;'>" + WebUtility.HtmlEncode(fac.EmergencyPhone) + "</span></p>" +
                          "<div style='background:#fff;color:#000;padding:16px;border-radius:8px;width:230px;text-align:center;margin:16px auto;'>" +
                          "<img src='https://api.qrserver.com/v1/create-qr-code/?size=210x210&data=" + qrEncoded + "' width='210' height='210' alt='QR'/>" +
                          "<div style='font-weight:bold;margin-top:6px;font-size:12px;'>ZESKANUJ W TAK FIELD HUB</div></div>" +
                          "<p><b>LoRa Meshtastic:</b> Kanał <code>" + WebUtility.HtmlEncode(fac.ChannelName) + "</code> (" + WebUtility.HtmlEncode(fac.LoraRegion) + " / " + WebUtility.HtmlEncode(fac.ModemPreset) + ")" +
                          " &nbsp;|&nbsp; <b>Serwer OTS:</b> <code>" + WebUtility.HtmlEncode(fac.OtsHost) + "</code>" +
                          " &nbsp;|&nbsp; <b>Hotspot Wi-Fi:</b> <code>" + WebUtility.HtmlEncode(hotspotUrl) + "</code></p>" +
                          "<table><thead><tr><th>Rola / Przeznaczenie</th><th>Pasmo</th><th>Częstotliwość / Kanał</th><th>Ton CTCSS / Kod DMR</th></tr></thead>" +
                          "<tbody>" + rows + "</tbody></table>" +
                          "</div></body></html>";

            File.WriteAllText(htmlPath, html, Encoding.UTF8);
            if (openInBrowser)
            {
                try { Process.Start(new ProcessStartInfo(htmlPath) { UseShellExecute = true }); } catch { }
            }
            return htmlPath;
        }

        public string StartOrUpdateHotspotServer(FactionProfileModel fac)
        {
            _latestHotspotPayloadJson = BuildFactionManifestJson(fac);
            string localIp = "127.0.0.1";
            try
            {
                foreach (var ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    {
                        localIp = ip.ToString();
                        break;
                    }
                }
            }
            catch { }

            try
            {
                if (_hotspotListener == null || !_hotspotListener.IsListening)
                {
                    _hotspotListener = new HttpListener();
                    _hotspotListener.Prefixes.Add("http://localhost:" + HotspotPort + "/");
                    _hotspotListener.Start();
                    _hotspotListener.BeginGetContext(OnHotspotHttpRequest, null);
                }
            }
            catch { }

            return "http://" + localIp + ":" + HotspotPort + "/ftak.json";
        }

        private void OnHotspotHttpRequest(IAsyncResult ar)
        {
            try
            {
                if (_hotspotListener == null || !_hotspotListener.IsListening) return;
                var ctx = _hotspotListener.EndGetContext(ar);
                _hotspotListener.BeginGetContext(OnHotspotHttpRequest, null);

                byte[] payload = Encoding.UTF8.GetBytes(_latestHotspotPayloadJson ?? "{}");
                ctx.Response.ContentType = "application/json; charset=utf-8";
                ctx.Response.ContentLength64 = payload.Length;
                ctx.Response.OutputStream.Write(payload, 0, payload.Length);
                ctx.Response.OutputStream.Close();
            }
            catch { }
        }

        public static string SanitizeFileName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "ORGA";
            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (char c in raw.Trim())
            {
                sb.Append(invalid.Contains(c) ? '_' : c);
            }
            string res = sb.ToString().Trim();
            return string.IsNullOrEmpty(res) ? "ORGA" : res;
        }

        private static string EscapeJson(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
        }
    }
}
