using System.IO;
using FieldTakHub.Builder.Models;
namespace FieldTakHub.Builder.Services;

public sealed class SourceAnalyzer
{
    public IReadOnlyList<ContentItem> Analyze(string sourceDirectory)
    {
        if (!Directory.Exists(sourceDirectory)) throw new DirectoryNotFoundException(sourceDirectory);
        return SourceFileFilter.EnumerateFilteredFiles(sourceDirectory, "*", SearchOption.AllDirectories)
            .Where(p => !Path.GetFileName(p).StartsWith('.') && !Path.GetFileName(p).Equals("README-WRZUC-PLIKI-TUTAJ.txt", StringComparison.OrdinalIgnoreCase))
            .Select(p => new ContentItem(p, Path.GetRelativePath(sourceDirectory, p).Replace('\\','/'), Category(p, sourceDirectory), new FileInfo(p).Length))
            .OrderBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string Category(string path, string root)
    {
        var rel = Path.GetRelativePath(root, path).Replace('\\','/').ToLowerInvariant();
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (rel.StartsWith("atak/") && ext == ".apk") return "ATAK APK";
        if (rel.StartsWith("atak/")) return "ATAK Mission Package";
        if (rel.StartsWith("plugins/") || ext == ".apk") return "Plugin APK";
        if (rel.StartsWith("maps/") || ext is ".mbtiles" or ".gpkg" or ".sqlite") return "Map";
        if (rel.StartsWith("overlays/") || ext is ".kml" or ".kmz") return "Overlay";
        if (rel.StartsWith("meshtastic/")) return "Meshtastic";
        if (rel.StartsWith("apps/")) return "Apps";
        if (ext is ".p12" or ".pfx" or ".pem" or ".crt" or ".cer") return "Certificate";
        if (ext is ".pref" or ".xml" or ".json") return "Configuration";
        return "Data";
    }
}
