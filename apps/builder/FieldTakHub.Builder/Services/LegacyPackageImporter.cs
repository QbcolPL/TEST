using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FieldTakHub.Builder.Models;

namespace FieldTakHub.Builder.Services;

public sealed record LegacyImportResult(
    FieldTakProject Project,
    string ProjectFile,
    bool SignatureVerified,
    bool HashesVerified,
    string OriginalSha256,
    int ImportedFiles,
    string ReportFile);

/// <summary>
/// Imports TAK Field Hub 1.x signed ZIP deployments into a Builder 2.x project.
/// The legacy ZIP is never treated as a trusted 2.x bundle: its RSA signature and
/// per-file SHA-256 values are verified first, then payload files are copied into
/// a fresh 2.x workspace. Building the project creates a new Ed25519-signed .ftak.
/// </summary>
public sealed class LegacyPackageImporter
{
    public LegacyImportResult Import(
        string zipPath,
        WorkspaceService workspace,
        string publisherName,
        int expiryHours,
        int maxDownloads)
    {
        if (!File.Exists(zipPath)) throw new FileNotFoundException("FTH-LEG-001: Legacy ZIP not found.", zipPath);

        var originalSha = Sha256File(zipPath);
        using var archive = ZipFile.OpenRead(zipPath);
        var entries = archive.Entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Name))
            .ToDictionary(e => Normalize(e.FullName), e => e, StringComparer.OrdinalIgnoreCase);

        if (!entries.TryGetValue("manifest.json", out var manifestEntry))
            throw new InvalidDataException("FTH-LEG-002: This is not a supported Field TAK Hub 1.x ZIP: manifest.json is missing.");

        var manifestBytes = ReadBytes(manifestEntry);
        using var doc = JsonDocument.Parse(manifestBytes);
        var root = doc.RootElement;

        var signatureVerified = VerifyLegacySignature(entries, manifestBytes);
        var hashesVerified = VerifyLegacyHashes(entries, root);

        var title = Text(root, "title", "Imported Field TAK package");
        var bundleId = Text(root, "bundleId", WorkspaceService.Slug(title).ToLowerInvariant());
        var bundleVersion = Text(root, "bundleVersion", "1.0.0-legacy");
        var atakApi = Text(root, "atakApi", "5.6");

        var layout = workspace.CreateNewProject(title);
        var imported = 0;

        if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                var rel = Text(item, "file", "");
                if (string.IsNullOrWhiteSpace(rel)) continue;
                rel = Normalize(rel);
                EnsureSafeRelative(rel);
                if (!entries.TryGetValue(rel, out var source))
                    throw new InvalidDataException($"FTH-LEG-003: Legacy manifest references missing file: {rel}");

                var category = Text(item, "category", "file").ToLowerInvariant();
                var fileName = Path.GetFileName(rel);
                var target = TargetPath(layout.SourceDirectory, category, fileName, rel);
                if (target is null) continue; // legacy metadata/config replaced by signed 2.x metadata

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var input = source.Open();
                using var output = File.Create(UniquePath(target));
                input.CopyTo(output);
                imported++;
            }
        }

        var project = new FieldTakProject
        {
            PackageId = bundleId,
            Name = title,
            PackageVersion = bundleVersion,
            PublisherName = string.IsNullOrWhiteSpace(publisherName) ? Environment.UserName : publisherName.Trim(),
            SourceDirectory = layout.SourceDirectory,
            OutputDirectory = layout.OutputDirectory,
            AtakMinVersion = atakApi,
            AtakMaxVersion = atakApi,
            ExpiryHours = Math.Max(1, expiryHours),
            MaxDownloads = Math.Max(1, maxDownloads),
            Server = ReadServer(root)
        };

        var report = Path.Combine(layout.RootDirectory, "LEGACY-IMPORT-REPORT.txt");
        File.WriteAllText(report, BuildReport(zipPath, originalSha, signatureVerified, hashesVerified, imported, project), new UTF8Encoding(false));

        return new LegacyImportResult(project, layout.ProjectFile, signatureVerified, hashesVerified, originalSha, imported, report);
    }

    private static bool VerifyLegacySignature(Dictionary<string, ZipArchiveEntry> entries, byte[] manifestBytes)
    {
        var hasSig = entries.TryGetValue("manifest.sig", out var sigEntry);
        var hasPub = entries.TryGetValue("manifest.pub.pem", out var pubEntry);
        if (!hasSig && !hasPub) return false;
        if (!hasSig || !hasPub) throw new InvalidDataException("FTH-LEG-004: Legacy signature metadata is incomplete.");

        var signatureText = Encoding.ASCII.GetString(ReadBytes(sigEntry!)).Trim();
        byte[] signature;
        try { signature = Convert.FromBase64String(signatureText); }
        catch (FormatException ex) { throw new InvalidDataException("FTH-LEG-005: Legacy manifest.sig is not valid Base64.", ex); }

        using var rsa = RSA.Create();
        try { rsa.ImportFromPem(Encoding.ASCII.GetString(ReadBytes(pubEntry!))); }
        catch (Exception ex) { throw new InvalidDataException("FTH-LEG-006: Legacy manifest.pub.pem cannot be read.", ex); }

        if (!rsa.VerifyData(manifestBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw new InvalidDataException("FTH-LEG-007: Legacy RSA signature verification failed. Import aborted.");
        return true;
    }

    private static bool VerifyLegacyHashes(Dictionary<string, ZipArchiveEntry> entries, JsonElement root)
    {
        if (root.TryGetProperty("configFile", out var configFileEl) && root.TryGetProperty("configSha256", out var configHashEl))
        {
            var configRel = Normalize(configFileEl.GetString() ?? "");
            var expected = configHashEl.GetString() ?? "";
            if (!string.IsNullOrWhiteSpace(configRel) && !string.IsNullOrWhiteSpace(expected))
            {
                EnsureSafeRelative(configRel);
                if (!entries.TryGetValue(configRel, out var configEntry) || !Sha256(ReadBytes(configEntry)).Equals(expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"FTH-LEG-008: Legacy config SHA-256 mismatch: {configRel}");
            }
        }

        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return true;
        foreach (var item in items.EnumerateArray())
        {
            var rel = Normalize(Text(item, "file", ""));
            var expected = Text(item, "sha256", "");
            if (string.IsNullOrWhiteSpace(rel) || string.IsNullOrWhiteSpace(expected)) continue;
            EnsureSafeRelative(rel);
            if (!entries.TryGetValue(rel, out var entry)) throw new InvalidDataException($"FTH-LEG-009: Legacy file missing: {rel}");
            var actual = Sha256(ReadBytes(entry));
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"FTH-LEG-010: Legacy SHA-256 mismatch: {rel}");
        }
        return true;
    }

    private static ServerProfile ReadServer(JsonElement root)
    {
        if (!root.TryGetProperty("server", out var server) || server.ValueKind != JsonValueKind.Object) return new ServerProfile();
        return new ServerProfile
        {
            Type = "OpenTAK",
            Name = Text(server, "name", "TAK Server"),
            Host = Text(server, "host", ""),
            CotPort = Number(server, "sslPort", 8089),
            ApiPort = Number(server, "enrollmentPort", 8446),
            WebPort = Number(server, "apiPort", 8443)
        };
    }

    private static string? TargetPath(string sourceRoot, string category, string fileName, string rel)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (category == "atak" && ext == ".apk") return Path.Combine(sourceRoot, "atak", "atak.apk");
        if (category is "plugin" or "app" && ext == ".apk") return Path.Combine(sourceRoot, "plugins", fileName);
        if (category == "map") return Path.Combine(sourceRoot, "maps", fileName);
        if (category is "overlay") return Path.Combine(sourceRoot, "overlays", fileName);
        if (category is "package" or "atak_import") return Path.Combine(sourceRoot, "atak", fileName);
        if (category is "file" && (rel.Equals("config.txt", StringComparison.OrdinalIgnoreCase) || rel.EndsWith("/config.txt", StringComparison.OrdinalIgnoreCase))) return null;
        return Path.Combine(sourceRoot, "data", fileName);
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        var i = 2;
        string candidate;
        do candidate = Path.Combine(dir, $"{stem} ({i++}){ext}"); while (File.Exists(candidate));
        return candidate;
    }

    private static string BuildReport(string zip, string sha, bool sig, bool hashes, int imported, FieldTakProject project)
    {
        return "FIELD TAK HUB LEGACY 1.x IMPORT REPORT\n" +
            "======================================\n\n" +
            "Source ZIP: " + zip + "\n" +
            "Source SHA-256: " + sha + "\n" +
            "Legacy RSA signature verified: " + (sig ? "YES" : "NOT PRESENT") + "\n" +
            "Legacy item/config SHA-256 verified: " + (hashes ? "YES" : "NO") + "\n" +
            "Imported payload files: " + imported + "\n\n" +
            "New project:\n" +
            "  Name: " + project.Name + "\n" +
            "  ID: " + project.PackageId + "\n" +
            "  Version: " + project.PackageVersion + "\n" +
            "  ATAK: " + project.AtakMinVersion + " - " + project.AtakMaxVersion + "\n" +
            "  Server: " + project.Server.Host + "\n" +
            "  Ports: " + project.Server.CotPort + " / " + project.Server.ApiPort + " / " + project.Server.WebPort + "\n\n" +
            "IMPORTANT:\n" +
            "The original RSA signature authenticates only the legacy 1.x ZIP. When you click BUILD PACKAGE,\n" +
            "Builder 2.x creates a new .ftak v2 and signs it with your current Ed25519 Publisher key.\n";
    }

    private static byte[] ReadBytes(ZipArchiveEntry e)
    {
        using var input = e.Open();
        using var ms = new MemoryStream();
        input.CopyTo(ms);
        return ms.ToArray();
    }

    private static string Text(JsonElement e, string name, string fallback)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? fallback) : fallback;

    private static int Number(JsonElement e, string name, int fallback)
        => e.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : fallback;

    private static string Normalize(string value) => value.Replace('\\', '/').TrimStart('/');

    private static void EnsureSafeRelative(string rel)
    {
        if (string.IsNullOrWhiteSpace(rel) || Path.IsPathRooted(rel) || rel.Split('/').Any(x => x == ".."))
            throw new InvalidDataException($"FTH-LEG-011: Unsafe legacy path: {rel}");
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
