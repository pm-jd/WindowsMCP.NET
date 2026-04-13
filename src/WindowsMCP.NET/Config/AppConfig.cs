using System.Text.Json.Serialization;

namespace WindowsMcpNet.Config;

public sealed class AppConfig
{
    public string Transport { get; set; } = "http";
    public string Host { get; set; } = "0.0.0.0";
    public int Port { get; set; } = 8000;
    public string? AdvertiseHost { get; set; }
    public string? ApiKey { get; set; }
    public HttpsConfig Https { get; set; } = new();
    public List<string> AllowedIps { get; set; } = [];
    public string LogLevel { get; set; } = "Information";
    public bool Autostart { get; set; } = false;
}

public sealed class HttpsConfig
{
    public bool Enabled { get; set; } = false;
    public string CertPath { get; set; } = "cert.pfx";
    public string? CertPassword { get; set; }
}

[JsonSerializable(typeof(AppConfig))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class AppConfigJsonContext : JsonSerializerContext;
