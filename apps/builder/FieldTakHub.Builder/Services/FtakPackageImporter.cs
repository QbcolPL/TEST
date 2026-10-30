using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FieldTakHub.Builder.Models;

namespace FieldTakHub.Builder.Services;

public sealed record FtakImportResult(FieldTakProject Project, string ProjectFile, int ImportedFiles, long ImportedBytes, string PackagePath);

/// <summary>Imports a previously built .ftak v2 into an editable Builder project.</summary>
public sealed class FtakPackageImporter
{
    public FtakImportResult Import(string ftakPath, WorkspaceService workspace, string publisherName)
    {
        if (!File.Exists(ftakPath)) throw new FileNotFoundException("FTAK package not found.", ftakPath);
        if (!Path.GetExtension(ftakPath).Equals(".ftak", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("FTH-IMP-001: select a .ftak package.");

        using var archive = ZipFile.OpenRead(ftakPath);
        var manifestEntry = archive.GetEntry("META-INF/fieldtak.json")
            ?? throw new InvalidDataException("FTH-IMP-002: META-INF/fieldtak.json is missing.");

        FieldTakManifest manifest;
        using (var stream = manifestEntry.Open())
            manifest = JsonSerializer.Deserialize<FieldTakManifest>(stream, JsonDefaults.Options)
                ?? throw new InvalidDataException("FTH-IMP-003: invalid fieldtak.json.");

        if (manifest.SchemaVersion != 2 || !string.Equals(manifest.Schema, "fieldtak.package", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"FTH-IMP-004: unsupported FTAK schema {manifest.SchemaVersion}.");

        var name = string.IsNullOrWhiteSpace(manifest.Package.Name) ? Path.GetFileNameWithoutExtension(ftakPath) : manifest.Package.Name;
        var layout = workspace.CreateNewProject(name + "-imported");
        var imported = 0;
        long bytes = 0;

        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase)) continue;
            if (!entry.FullName.StartsWith("payload/", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrEmpty(entry.Name)) continue;

            var relative = entry.FullName["payload/".Length..].Replace('/', Path.DirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(x => x == ".."))
                throw new InvalidDataException($"FTH-IMP-005: unsafe archive path: {entry.FullName}");

            var destination = Path.GetFullPath(Path.Combine(layout.SourceDirectory, relative));
            var sourceRoot = Path.GetFullPath(layout.SourceDirectory + Path.DirectorySeparatorChar);
            if (!destination.StartsWith(sourceRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"FTH-IMP-006: archive path escapes source: {entry.FullName}");

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var input = entry.Open();
            using var output = File.Create(destination);
            input.CopyTo(output);
            imported++;
            bytes += entry.Length;
        }

        // Keep the original package's deployment settings editable. Distribution expiry is
        // intentionally converted to a relative value from the import time.
        var expiryHours = Math.Max(1, (int)Math.Ceiling((manifest.Distribution.ExpiresUtc - DateTime.UtcNow).TotalHours));
        if (manifest.Distribution.ExpiresUtc <= DateTime.UtcNow) expiryHours = 24;

        var project = new FieldTakProject
        {
            PackageId = string.IsNullOrWhiteSpace(manifest.Package.Id) ? WorkspaceService.Slug(name).ToLowerInvariant() : manifest.Package.Id,
            Name = name,
            PackageVersion = string.IsNullOrWhiteSpace(manifest.Package.Version) ? "2.3.0" : manifest.Package.Version,
            PublisherName = string.IsNullOrWhiteSpace(manifest.Package.Publisher) ? publisherName : manifest.Package.Publisher,
            SourceDirectory = layout.SourceDirectory,
            OutputDirectory = layout.OutputDirectory,
            AtakMinVersion = manifest.Target.MinVersion,
            AtakMaxVersion = manifest.Target.MaxVersion,
            ExpiryHours = expiryHours,
            QrExpiryHours = 2,
            MaxDownloads = Math.Max(1, manifest.Distribution.MaxDownloads),
            BuilderMode = "simple",
            Server = manifest.Server,
            Enrollment = manifest.Enrollment,
            Meshtastic = manifest.Meshtastic
        };

        // Recover Meshtastic channel URL from payload if present. The URL is deliberately
        // not stored in the manifest itself, so importing an existing package preserves it.
        var channelEntry = archive.GetEntry("payload/meshtastic/channel.url");
        if (channelEntry != null)
        {
            var channelPath = Path.Combine(layout.SourceDirectory, "meshtastic", "channel.url");
            Directory.CreateDirectory(Path.GetDirectoryName(channelPath)!);
            using var input = channelEntry.Open();
            using var reader = new StreamReader(input, Encoding.UTF8, true);
            File.WriteAllText(channelPath, reader.ReadToEnd().Trim() + Environment.NewLine, new UTF8Encoding(false));
        }

        var enrollmentEntry = archive.GetEntry("payload/enrollment/enroll.url");
        if (enrollmentEntry != null)
        {
            var enrollmentPath = Path.Combine(layout.SourceDirectory, "enrollment", "enroll.url");
            Directory.CreateDirectory(Path.GetDirectoryName(enrollmentPath)!);
            using var input = enrollmentEntry.Open();
            using var reader = new StreamReader(input, Encoding.UTF8, true);
            project.Enrollment.TakUri = reader.ReadToEnd().Trim();
            project.Enrollment.Enabled = !string.IsNullOrWhiteSpace(project.Enrollment.TakUri);
            File.WriteAllText(enrollmentPath, project.Enrollment.TakUri + Environment.NewLine, new UTF8Encoding(false));
        }

        workspace.EnsureSourceTree(layout.SourceDirectory, layout.OutputDirectory);
        var projectFile = layout.ProjectFile;
        new ProjectService().Save(projectFile, project);
        File.WriteAllText(Path.Combine(layout.RootDirectory, "IMPORTED-FROM-FTAK.txt"),
            $"Imported from: {Path.GetFullPath(ftakPath)}\r\nImported UTC: {DateTime.UtcNow:O}\r\nFiles: {imported}\r\nBytes: {bytes}\r\nOriginal package SHA-256: {Sha256File(ftakPath)}\r\n",
            new UTF8Encoding(false));

        return new FtakImportResult(project, projectFile, imported, bytes, ftakPath);
    }

    private static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
    }
}
