using System.IO;
using System.Text;
using FieldTakHub.Builder.Models;

namespace FieldTakHub.Builder.Services;

/// <summary>
/// Human-editable TAK server profile. It deliberately contains no passwords,
/// private keys or client certificates.
/// </summary>
public sealed class ServerTextConfigService
{
    public string Serialize(ServerProfile server)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Field TAK Hub server configuration");
        sb.AppendLine("# You may edit this file in Notepad/Notepad++ and load it back into Builder.");
        sb.AppendLine("# Do not put passwords, private keys or certificate secrets here.");
        sb.AppendLine();
        sb.AppendLine($"SERVER_TYPE={Clean(server.Type, "OpenTAK")}");
        sb.AppendLine($"SERVER_NAME={Clean(server.Name, "TAK Server")}");
        sb.AppendLine($"HOST={Clean(server.Host, "ggzstak.duckdns.org")}");
        sb.AppendLine($"COT_PORT={server.CotPort}");
        sb.AppendLine($"API_PORT={server.ApiPort}");
        sb.AppendLine($"WEB_PORT={server.WebPort}");
        return sb.ToString();
    }

    public void Save(string path, ServerProfile server)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, Serialize(server), new UTF8Encoding(false));
    }

    public ServerProfile Load(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("server.txt not found.", path);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
            var split = line.IndexOf('=');
            if (split <= 0) continue;
            values[line[..split].Trim()] = line[(split + 1)..].Trim();
        }

        return new ServerProfile
        {
            Type = Get(values, "SERVER_TYPE", "OpenTAK"),
            Name = Get(values, "SERVER_NAME", "TAK Server"),
            Host = Get(values, "HOST", string.Empty),
            CotPort = Port(values, "COT_PORT", 8089),
            ApiPort = Port(values, "API_PORT", 8446),
            WebPort = Port(values, "WEB_PORT", 8443)
        };
    }

    public static string SidecarPath(string ftakPath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(ftakPath))!, Path.GetFileNameWithoutExtension(ftakPath) + "-server.txt");

    private static string Get(Dictionary<string, string> values, string key, string fallback) =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static int Port(Dictionary<string, string> values, string key, int fallback) =>
        values.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) && parsed is > 0 and <= 65535 ? parsed : fallback;

    private static string Clean(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Replace("\r", " ").Replace("\n", " ").Trim();
}
