using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FieldTakHub.Builder.Models;

namespace FieldTakHub.Builder.Services;

public sealed class MissionPackageBuilder
{
    private sealed record Entry(string ZipEntry, string? SourcePath = null, byte[]? Bytes = null);

    public bool Build(string sourceAtakDirectory, string outputZip, string packageName, ServerProfile? server = null)
    {
        var files = Directory.Exists(sourceAtakDirectory)
            ? SourceFileFilter.EnumerateFilteredFiles(sourceAtakDirectory, "*", SearchOption.AllDirectories)
                .Where(f => !IsUserManifest(sourceAtakDirectory, f) && !Path.GetExtension(f).Equals(".apk", StringComparison.OrdinalIgnoreCase)).ToList()
            : new List<string>();

        var entries = files.Select(file => new Entry(
            Path.GetRelativePath(sourceAtakDirectory, file).Replace('\\', '/'), file)).ToList();

        // Generate a minimal connection/enrollment hint only when the operator did not provide a custom .pref.
        if (server is { Host.Length: > 0 } && !files.Any(f => Path.GetExtension(f).Equals(".pref", StringComparison.OrdinalIgnoreCase)))
            entries.Add(new Entry("server.pref", Bytes: BuildServerPreference(server)));

        if (entries.Count == 0) return false;

        Directory.CreateDirectory(Path.GetDirectoryName(outputZip)!);
        if (File.Exists(outputZip)) File.Delete(outputZip);

        var manifest = new XElement("MissionPackageManifest", new XAttribute("version", "2"),
            new XElement("Configuration",
                new XElement("Parameter", new XAttribute("name", "uid"), new XAttribute("value", Guid.NewGuid().ToString())),
                new XElement("Parameter", new XAttribute("name", "name"), new XAttribute("value", packageName)),
                new XElement("Parameter", new XAttribute("name", "onReceiveImport"), new XAttribute("value", "true"))),
            new XElement("Contents", entries.Select(e => ContentElement(e.ZipEntry))));

        using var fs = File.Create(outputZip);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        var me = zip.CreateEntry("MANIFEST/manifest.xml", CompressionLevel.Optimal);
        using (var sw = new StreamWriter(me.Open(), new UTF8Encoding(false)))
            new XDocument(new XDeclaration("1.0", "UTF-8", null), manifest).Save(sw);

        foreach (var e in entries)
        {
            var ze = zip.CreateEntry(e.ZipEntry, CompressionLevel.Optimal);
            using var dst = ze.Open();
            if (e.SourcePath is not null)
            {
                using var src = File.OpenRead(e.SourcePath);
                src.CopyTo(dst);
            }
            else if (e.Bytes is not null) dst.Write(e.Bytes, 0, e.Bytes.Length);
        }
        return true;
    }

    private static bool IsUserManifest(string root, string file)
    {
        var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
        return rel.Equals("MANIFEST/manifest.xml", StringComparison.OrdinalIgnoreCase)
            || rel.Equals("manifest.xml", StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] BuildServerPreference(ServerProfile server)
    {
        var preferences = new XElement("preferences",
            new XElement("preference", new XAttribute("version", "1"), new XAttribute("name", "cot_streams"),
                PrefEntry("count", "class java.lang.Integer", "1"),
                PrefEntry("description0", "class java.lang.String", string.IsNullOrWhiteSpace(server.Name) ? "TAK Server" : server.Name),
                PrefEntry("enabled0", "class java.lang.Boolean", "true"),
                PrefEntry("connectString0", "class java.lang.String", $"{server.Host}:{server.CotPort}:ssl"),
                PrefEntry("useAuth0", "class java.lang.Boolean", "true"),
                PrefEntry("enrollForCertificateWithTrust0", "class java.lang.Boolean", "true")));

        using var ms = new MemoryStream();
        using (var sw = new StreamWriter(ms, new UTF8Encoding(false), 1024, leaveOpen: true))
            new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), preferences).Save(sw);
        return ms.ToArray();
    }

    private static XElement PrefEntry(string key, string @class, string value) =>
        new("entry", new XAttribute("key", key), new XAttribute("class", @class), value);

    private static XElement ContentElement(string zipEntry)
    {
        var content = new XElement("Content", new XAttribute("ignore", "false"), new XAttribute("zipEntry", zipEntry));
        var ext = Path.GetExtension(zipEntry).ToLowerInvariant();
        content.Add(new XElement("Parameter", new XAttribute("name", "name"), new XAttribute("value", Path.GetFileName(zipEntry))));
        if (ext is ".kml" or ".kmz")
        {
            content.Add(new XElement("Parameter", new XAttribute("name", "contentType"), new XAttribute("value", "KML")));
            content.Add(new XElement("Parameter", new XAttribute("name", "visible"), new XAttribute("value", "true")));
        }
        return content;
    }
}
