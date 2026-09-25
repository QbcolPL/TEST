using System.IO;
using System.Text.Json;
using FieldTakHub.Builder.Models;
namespace FieldTakHub.Builder.Services;

public sealed class ProjectService
{
    public FieldTakProject Load(string path)
    {
        var p = JsonSerializer.Deserialize<FieldTakProject>(File.ReadAllText(path), JsonDefaults.Options)
            ?? throw new InvalidDataException("Invalid project file.");
        if (p.SchemaVersion != 2) throw new InvalidDataException($"Unsupported project schema {p.SchemaVersion}.");
        // Backward compatibility: Builder 2.3 previously stored the abstract FIELD NODE role as field_node.
        // It now maps to the native Meshtastic TAK role while keeping old .fthproj files readable.
        if (string.Equals(p.Meshtastic.DeviceRole, "field_node", StringComparison.OrdinalIgnoreCase))
            p.Meshtastic.DeviceRole = "tak";
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        p.SourceDirectory = Resolve(baseDir, p.SourceDirectory);
        p.OutputDirectory = Resolve(baseDir, p.OutputDirectory);
        return p;
    }

    public void Save(string path, FieldTakProject project)
    {
        var full = Path.GetFullPath(path);
        var baseDir = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(baseDir);
        // Keep project files portable: paths are serialized relative to the .fthproj location when possible.
        var portable = new FieldTakProject
        {
            SchemaVersion = project.SchemaVersion,
            PackageId = project.PackageId,
            Name = project.Name,
            PackageVersion = project.PackageVersion,
            PublisherName = project.PublisherName,
            SourceDirectory = PortablePath(baseDir, project.SourceDirectory),
            OutputDirectory = PortablePath(baseDir, project.OutputDirectory),
            AtakMinVersion = project.AtakMinVersion,
            AtakMaxVersion = project.AtakMaxVersion,
            ExpiryHours = project.ExpiryHours,
            QrExpiryHours = project.QrExpiryHours,
            BuilderMode = project.BuilderMode,
            MaxDownloads = project.MaxDownloads,
            Enrollment = new EnrollmentProfile { Enabled=project.Enrollment.Enabled, TakUri=project.Enrollment.TakUri, Label=project.Enrollment.Label },
            Meshtastic = new MeshtasticProfile { Enabled=project.Meshtastic.Enabled, Mode=project.Meshtastic.Mode, DeviceRole=project.Meshtastic.DeviceRole, DeviceModel=project.Meshtastic.DeviceModel, ChannelName=project.Meshtastic.ChannelName, Region=project.Meshtastic.Region, ModemPreset=project.Meshtastic.ModemPreset, HopLimit=project.Meshtastic.HopLimit, Pli=project.Meshtastic.Pli, GeoChat=project.Meshtastic.GeoChat, OtsRelay=project.Meshtastic.OtsRelay, FileTransfer=project.Meshtastic.FileTransfer, Gateway=new MeshtasticGatewayProfile { Enabled=project.Meshtastic.Gateway.Enabled, Type=project.Meshtastic.Gateway.Type, Role=project.Meshtastic.Gateway.Role, Transport=project.Meshtastic.Gateway.Transport, MqttPort=project.Meshtastic.Gateway.MqttPort, RootTopic=project.Meshtastic.Gateway.RootTopic, SecretsExternal=project.Meshtastic.Gateway.SecretsExternal } },
            Server = new ServerProfile
            {
                Type=project.Server.Type, Name=project.Server.Name, Host=project.Server.Host,
                CotPort=project.Server.CotPort, ApiPort=project.Server.ApiPort, WebPort=project.Server.WebPort
            }
        };
        File.WriteAllText(full, JsonSerializer.Serialize(portable, JsonDefaults.Options));
    }

    private static string Resolve(string baseDir, string path) =>
        string.IsNullOrWhiteSpace(path) ? baseDir : Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(baseDir, path));

    private static string PortablePath(string baseDir,string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return ".";
        try
        {
            var full=Path.GetFullPath(value);
            var rel=Path.GetRelativePath(baseDir,full);
            return rel.Replace('\\','/');
        }
        catch { return value; }
    }
}
