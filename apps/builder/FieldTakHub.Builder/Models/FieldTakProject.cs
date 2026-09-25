namespace FieldTakHub.Builder.Models;

public sealed class FieldTakProject
{
    public int SchemaVersion { get; set; } = 2;
    public string PackageId { get; set; } = "local.fieldtak.package";
    public string Name { get; set; } = "Field TAK Package";
    public string PackageVersion { get; set; } = "2.0.0";
    public string PublisherName { get; set; } = Environment.UserName;
    public string SourceDirectory { get; set; } = string.Empty;
    public string OutputDirectory { get; set; } = string.Empty;
    public string AtakMinVersion { get; set; } = "5.6";
    public string AtakMaxVersion { get; set; } = "5.8";
    public int ExpiryHours { get; set; } = 6;
    public int QrExpiryHours { get; set; } = 6;
    public string BuilderMode { get; set; } = "simple";
    public int MaxDownloads { get; set; } = 50;
    public ServerProfile Server { get; set; } = new();
    public EnrollmentProfile Enrollment { get; set; } = new();
    public MeshtasticProfile Meshtastic { get; set; } = new();
}

public sealed class EnrollmentProfile
{
    public bool Enabled { get; set; }
    public string TakUri { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public sealed class ServerProfile
{
    public string Type { get; set; } = "OpenTAK";
    public string Name { get; set; } = "GGZS OpenTAK";
    public string Host { get; set; } = "ggzstak.duckdns.org";
    public int CotPort { get; set; } = 8089;
    public int ApiPort { get; set; } = 8446;
    public int WebPort { get; set; } = 8443;
}

public sealed class MeshtasticProfile
{
    public bool Enabled { get; set; } = true;
    public string Mode { get; set; } = "hybrid";
    public string DeviceRole { get; set; } = "auto";
    public string DeviceModel { get; set; } = "generic_meshtastic";
    public string ChannelName { get; set; } = "GGZS-TAK";
    public string Region { get; set; } = "EU_868";
    public string ModemPreset { get; set; } = "SHORT_FAST";
    public int HopLimit { get; set; } = 3;
    public bool Pli { get; set; } = true;
    public bool GeoChat { get; set; } = true;
    public bool OtsRelay { get; set; } = true;
    public bool FileTransfer { get; set; } = false;
    public MeshtasticGatewayProfile Gateway { get; set; } = new();
}

public sealed class MeshtasticGatewayProfile
{
    public bool Enabled { get; set; } = false;
    public string Type { get; set; } = "generic_meshtastic";
    public string Role { get; set; } = "ots_mqtt";
    public string Transport { get; set; } = "mqtt_tls";
    public int MqttPort { get; set; } = 8883;
    public string RootTopic { get; set; } = "opentakserver";
    public bool SecretsExternal { get; set; } = true;
}
