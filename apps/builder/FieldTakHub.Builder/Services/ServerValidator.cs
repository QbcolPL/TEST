using System.IO;
using System.Net;
using FieldTakHub.Builder.Models;

namespace FieldTakHub.Builder.Services;

public static class ServerValidator
{
    public static void Validate(ServerProfile server)
    {
        var host = server.Host.Trim();
        if (string.IsNullOrWhiteSpace(host)) throw new InvalidDataException("FTH-SRV-001: Enter a TAK server host/domain, e.g. ggzstak.duckdns.org.");
        if (host.Contains("://", StringComparison.Ordinal) || host.Contains('/') || host.Contains('\\'))
            throw new InvalidDataException("FTH-SRV-002: Host must contain only a DNS name or IP address, without http://, https:// or a path.");
        if (host.Any(char.IsWhiteSpace)) throw new InvalidDataException("FTH-SRV-003: Host cannot contain spaces.");
        Port(server.CotPort, "CoT"); Port(server.ApiPort, "API/Enrollment"); Port(server.WebPort, "Web");
    }

    public static void ValidateProject(FieldTakProject project)
    {
        Validate(project.Server);
        if (string.IsNullOrWhiteSpace(project.PackageId)) throw new InvalidDataException("FTH-BLD-001: Package ID is required.");
        if (string.IsNullOrWhiteSpace(project.Name)) throw new InvalidDataException("FTH-BLD-002: Package name is required.");
        if (string.IsNullOrWhiteSpace(project.PackageVersion)) throw new InvalidDataException("FTH-BLD-003: Package version is required.");
        if (!Directory.Exists(project.SourceDirectory)) throw new DirectoryNotFoundException($"FTH-BLD-004: Source directory not found: {project.SourceDirectory}");
        if (string.IsNullOrWhiteSpace(project.OutputDirectory)) throw new InvalidDataException("FTH-BLD-005: Output directory is required.");
        if (project.Meshtastic.HopLimit is < 1 or > 7) throw new InvalidDataException("FTH-MESH-003: hopLimit must be between 1 and 7.");
        if (project.Meshtastic.Gateway.MqttPort is < 1 or > 65535) throw new InvalidDataException("FTH-MESH-005: gateway MQTT port must be in range 1-65535.");
        if (string.IsNullOrWhiteSpace(project.Meshtastic.Gateway.RootTopic)) throw new InvalidDataException("FTH-MESH-006: gateway Root Topic is required.");
        if (project.Meshtastic.Gateway.SecretsExternal != true) throw new InvalidDataException("FTH-MESH-007: gateway credentials/secrets must remain external to the .ftak package.");
    }

    private static void Port(int value, string label)
    {
        if (value is <= 0 or > 65535) throw new InvalidDataException($"FTH-SRV-004: {label} port must be in range 1-65535.");
    }
}
