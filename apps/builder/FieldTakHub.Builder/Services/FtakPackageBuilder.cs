using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FieldTakHub.Builder.Models;

namespace FieldTakHub.Builder.Services;

public sealed class FtakPackageBuilder
{
    private readonly SigningKeyService _signing = new();
    private readonly MissionPackageBuilder _missionBuilder = new();
    private readonly ServerTextConfigService _serverText = new();

    public string Build(FieldTakProject project, IReadOnlyList<ContentItem> items)
    {
        if (!Directory.Exists(project.SourceDirectory)) throw new DirectoryNotFoundException(project.SourceDirectory);
        Directory.CreateDirectory(project.OutputDirectory);

        var staging = Path.Combine(Path.GetTempPath(), "fieldtak-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var payload = Path.Combine(staging, "payload");
            Directory.CreateDirectory(payload);
            var atakRoot = Path.Combine(project.SourceDirectory, "atak");
            var atakApks = Directory.Exists(atakRoot)
                ? SourceFileFilter.EnumerateFilteredFiles(atakRoot, "*.apk", SearchOption.AllDirectories).ToList()
                : new List<string>();
            if (atakApks.Count > 1)
                throw new InvalidDataException("FTH-BLD-006: source/atak may contain only one ATAK APK. Remove older/duplicate ATAK APK files before building.");
            if (atakApks.Count == 1)
            {
                var atakDest = Path.Combine(payload, "atak", "atak.apk");
                Directory.CreateDirectory(Path.GetDirectoryName(atakDest)!);
                File.Copy(atakApks[0], atakDest, true);
            }

            // Optional bundled Meshtastic Android app. Field TAK Hub installs/updates it on the participant phone.
            var appsRoot = Path.Combine(project.SourceDirectory, "apps");
            var meshtasticApk = Path.Combine(appsRoot, "meshtastic.apk");
            if (File.Exists(meshtasticApk))
            {
                var meshDest = Path.Combine(payload, "apps", "meshtastic.apk");
                Directory.CreateDirectory(Path.GetDirectoryName(meshDest)!);
                File.Copy(meshtasticApk, meshDest, true);
            }

            var missionOut = Path.Combine(payload, "atak", "mission-package.zip");
            var missionCreated = _missionBuilder.Build(atakRoot, missionOut, project.Name, project.Server);

            int pluginCount = 0;
            foreach (var item in items.Where(i => !i.RelativePath.StartsWith("atak/", StringComparison.OrdinalIgnoreCase)))
            {
                var destRel = DestinationPath(item);
                var dest = Path.Combine(payload, destRel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(item.SourcePath, dest, true);
                if (destRel.StartsWith("plugins/", StringComparison.OrdinalIgnoreCase) && Path.GetExtension(destRel).Equals(".apk", StringComparison.OrdinalIgnoreCase)) pluginCount++;
            }

            // source/meshtastic is optional for backwards compatibility.
            // Projects created before Meshtastic support (or tests/minimal projects)
            // may not have the folder at all. Only validate channel.url when the
            // optional Meshtastic folder exists.
            var enrollmentRoot = Path.Combine(project.SourceDirectory, "enrollment");
            var enrollmentFile = Directory.Exists(enrollmentRoot)
                ? SourceFileFilter.EnumerateFilteredFiles(enrollmentRoot, "enroll.url", SearchOption.TopDirectoryOnly).FirstOrDefault()
                : null;
            if (enrollmentFile != null)
            {
                var enrollment = File.ReadAllText(enrollmentFile, new UTF8Encoding(false)).Trim();
                if (!IsEnrollmentUri(enrollment))
                    throw new InvalidDataException("FTH-ENROLL-001: source/enrollment/enroll.url must contain a tak://com.atakmap.app/enroll URI with host, username and token.");
                var dest = Path.Combine(payload, "enrollment", "enroll.url");
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.WriteAllText(dest, enrollment + Environment.NewLine, new UTF8Encoding(false));
            }

            var meshRoot = Path.Combine(project.SourceDirectory, "meshtastic");
            var meshChannel = Directory.Exists(meshRoot)
                ? SourceFileFilter.EnumerateFilteredFiles(meshRoot, "channel.url", SearchOption.TopDirectoryOnly).FirstOrDefault()
                : null;
            if (meshChannel != null)
            {
                var channel = File.ReadAllText(meshChannel, new UTF8Encoding(false)).Trim();
                if (!channel.StartsWith("https://meshtastic.org/e/", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("FTH-MESH-001: source/meshtastic/channel.url must contain an https://meshtastic.org/e/#... channel link.");
            }

            var meshProfile = Directory.Exists(meshRoot)
                ? SourceFileFilter.EnumerateFilteredFiles(meshRoot, "profile.json", SearchOption.TopDirectoryOnly).FirstOrDefault()
                : null;
            if (meshProfile != null)
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(meshProfile, new UTF8Encoding(false)));
                    var root = doc.RootElement;
                    var mode = root.TryGetProperty("mode", out var modeEl) ? modeEl.GetString() : "hybrid";
                    if (mode is not ("hybrid" or "compatible" or "direct"))
                        throw new InvalidDataException("FTH-MESH-002: profile.json mode must be hybrid, compatible or direct.");
                    if (root.TryGetProperty("hopLimit", out var hop) && (hop.GetInt32() < 1 || hop.GetInt32() > 7))
                        throw new InvalidDataException("FTH-MESH-003: hopLimit must be between 1 and 7.");
                }
                catch (JsonException ex) { throw new InvalidDataException("FTH-MESH-004: invalid source/meshtastic/profile.json.", ex); }
            }
            if (project.Meshtastic.Enabled)
            {
                var destProfile = Path.Combine(payload, "meshtastic", "profile.json");
                Directory.CreateDirectory(Path.GetDirectoryName(destProfile)!);
                var canonicalProfile = new
                {
                    version = 2, mode = project.Meshtastic.Mode, deviceRole = project.Meshtastic.DeviceRole, deviceModel = project.Meshtastic.DeviceModel, channelName = project.Meshtastic.ChannelName,
                    region = project.Meshtastic.Region, modemPreset = project.Meshtastic.ModemPreset, hopLimit = project.Meshtastic.HopLimit,
                    pli = project.Meshtastic.Pli, geoChat = project.Meshtastic.GeoChat, otsRelay = project.Meshtastic.OtsRelay, fileTransfer = project.Meshtastic.FileTransfer,
                    gateway = new
                    {
                        enabled = project.Meshtastic.Gateway.Enabled, type = project.Meshtastic.Gateway.Type, role = project.Meshtastic.Gateway.Role,
                        transport = project.Meshtastic.Gateway.Transport, mqttPort = project.Meshtastic.Gateway.MqttPort,
                        rootTopic = project.Meshtastic.Gateway.RootTopic, secretsExternal = project.Meshtastic.Gateway.SecretsExternal
                    }
                };
                File.WriteAllText(destProfile, JsonSerializer.Serialize(canonicalProfile, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine, new UTF8Encoding(false));
            }

            var payloadBytes = SourceFileFilter.EnumerateFilteredFiles(payload, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
            var metaEstimate = 512L * 1024L;
            var uncompressedEstimate = payloadBytes + metaEstimate;
            // Keep enough room for the downloaded .ftak, extraction and temporary files.
            var recommendedFree = Math.Max(512L * 1024L * 1024L, payloadBytes * 2 + 256L * 1024L * 1024L);

            var preliminary = new FieldTakManifest
            {
                Package = new PackageInfo { Id = project.PackageId, Name = project.Name, Version = project.PackageVersion, Publisher = project.PublisherName, CreatedUtc = DateTime.UtcNow },
                Target = new TargetInfo { MinVersion = project.AtakMinVersion, MaxVersion = project.AtakMaxVersion },
                Server = project.Server,
                Enrollment = project.Enrollment,
                Meshtastic = project.Meshtastic,
                Components = new ComponentInfo { MissionPackage = missionCreated, Plugins = pluginCount, Files = SourceFileFilter.EnumerateFilteredFiles(payload, "*", SearchOption.AllDirectories).Count(), Enrollment = enrollmentFile != null, Meshtastic = project.Meshtastic.Enabled || (Directory.Exists(Path.Combine(project.SourceDirectory, "meshtastic")) && SourceFileFilter.EnumerateFilteredFiles(project.SourceDirectory + Path.DirectorySeparatorChar + "meshtastic", "*", SearchOption.AllDirectories).Any()) },
                Distribution = new DistributionInfo { ExpiresUtc = DateTime.UtcNow.AddHours(project.ExpiryHours), MaxDownloads = project.MaxDownloads },
                Size = new PackageSizeInfo { PayloadBytes = payloadBytes, UncompressedBytes = uncompressedEstimate, RecommendedFreeBytes = recommendedFree }
            };

            var meta = Path.Combine(staging, "META-INF");
            Directory.CreateDirectory(meta);
            var manifestPath = Path.Combine(meta, "fieldtak.json");
            // Signed, human-readable copy of the server profile for diagnostics/audit.
            _serverText.Save(Path.Combine(meta, "server.txt"), project.Server);
            // fingerprint depends only on pubkey; get via signing a temporary zero-length message
            var pubInfo = _signing.Sign(ReadOnlySpan<byte>.Empty);
            preliminary.Security.PublisherFingerprintSha256 = pubInfo.Fingerprint;
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(preliminary, JsonDefaults.Options), new UTF8Encoding(false));

            var checksumLines = new List<string>();
            foreach (var file in SourceFileFilter.EnumerateFilteredFiles(staging, "*", SearchOption.AllDirectories)
                         .Where(f => !f.EndsWith("checksums.sha256", StringComparison.OrdinalIgnoreCase) && !f.EndsWith("signature.ed25519", StringComparison.OrdinalIgnoreCase) && !f.EndsWith("publisher.pub", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(f => Path.GetRelativePath(staging, f).Replace('\\','/'), StringComparer.Ordinal))
            {
                var rel = Path.GetRelativePath(staging, file).Replace('\\','/');
                using var hashStream = File.OpenRead(file);
                var hash = Convert.ToHexString(SHA256.HashData(hashStream)).ToLowerInvariant();
                checksumLines.Add($"{hash}  {rel}");
            }
            var checksumBytes = new UTF8Encoding(false).GetBytes(string.Join("\n", checksumLines) + "\n");
            File.WriteAllBytes(Path.Combine(meta, "checksums.sha256"), checksumBytes);
            var signed = _signing.Sign(checksumBytes);
            File.WriteAllText(Path.Combine(meta, "signature.ed25519"), Convert.ToBase64String(signed.Signature), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(meta, "publisher.pub"), Convert.ToBase64String(signed.PublicKey), new UTF8Encoding(false));

            var safeName = string.Concat(project.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Replace(' ', '-');
            var outPath = Path.Combine(project.OutputDirectory, $"{safeName}-{project.PackageVersion}.ftak");
            if (File.Exists(outPath)) File.Delete(outPath);
            ZipFile.CreateFromDirectory(staging, outPath, CompressionLevel.Optimal, false);
            // Also write an easy-to-edit sidecar next to the .ftak. Editing it does not alter an already-built bundle; load it into Builder and rebuild.
            _serverText.Save(ServerTextConfigService.SidecarPath(outPath), project.Server);
            return outPath;
        }
        finally
        {
            try { Directory.Delete(staging, true); } catch { }
        }
    }

    private static bool IsEnrollmentUri(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)) return false;
        if (!uri.Scheme.Equals("tak", StringComparison.OrdinalIgnoreCase) || !uri.Host.Equals("com.atakmap.app", StringComparison.OrdinalIgnoreCase)) return false;
        var action = uri.AbsolutePath.Trim('/').Split('/')[0];
        if (!action.Equals("enroll", StringComparison.OrdinalIgnoreCase)) return false;
        var q = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Split('=', 2)).Where(x => x.Length == 2)
            .ToDictionary(x => Uri.UnescapeDataString(x[0]), x => Uri.UnescapeDataString(x[1]), StringComparer.OrdinalIgnoreCase);
        return q.TryGetValue("host", out var host) && !string.IsNullOrWhiteSpace(host)
            && q.TryGetValue("username", out var user) && !string.IsNullOrWhiteSpace(user)
            && q.TryGetValue("token", out var token) && !string.IsNullOrWhiteSpace(token);
    }

    private static string DestinationPath(ContentItem item)
    {
        var rel = item.RelativePath.Replace('\\','/');
        if (rel.StartsWith("plugins/", StringComparison.OrdinalIgnoreCase) || rel.StartsWith("maps/", StringComparison.OrdinalIgnoreCase) ||
            rel.StartsWith("overlays/", StringComparison.OrdinalIgnoreCase) || rel.StartsWith("config/", StringComparison.OrdinalIgnoreCase) ||
            rel.StartsWith("data/", StringComparison.OrdinalIgnoreCase) || rel.StartsWith("meshtastic/", StringComparison.OrdinalIgnoreCase) || rel.StartsWith("enrollment/", StringComparison.OrdinalIgnoreCase)) return rel;
        if (Path.GetExtension(rel).Equals(".apk", StringComparison.OrdinalIgnoreCase)) return "plugins/" + Path.GetFileName(rel);
        return "data/" + rel;
    }
}
