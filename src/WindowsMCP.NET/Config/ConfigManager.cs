using System.Security.Cryptography;
using System.Text.Json;

namespace WindowsMcpNet.Config;

public sealed class ConfigManager
{
    private readonly string _configPath;

    public ConfigManager(string baseDirectory)
    {
        _configPath = Path.Combine(baseDirectory, "config.json");
    }

    public AppConfig Load()
    {
        if (!File.Exists(_configPath))
            return new AppConfig();

        var json = File.ReadAllText(_configPath);
        var config = JsonSerializer.Deserialize(json, AppConfigJsonContext.Default.AppConfig)
                     ?? new AppConfig();

        // Decrypt sensitive fields after loading
        config.ApiKey = Decrypt(config.ApiKey);
        config.Https.CertPassword = Decrypt(config.Https.CertPassword);

        return config;
    }

    public void Save(AppConfig config)
    {
        // Encrypt sensitive fields before saving
        var toSave = new AppConfig
        {
            Transport = config.Transport,
            Host = config.Host,
            Port = config.Port,
            AdvertiseHost = config.AdvertiseHost,
            ApiKey = Encrypt(config.ApiKey),
            Https = new HttpsConfig
            {
                Enabled = config.Https.Enabled,
                CertPath = config.Https.CertPath,
                CertPassword = Encrypt(config.Https.CertPassword),
            },
            AllowedIps = config.AllowedIps,
            LogLevel = config.LogLevel,
        };

        var json = JsonSerializer.Serialize(toSave, AppConfigJsonContext.Default.AppConfig);
        File.WriteAllText(_configPath, json);
    }

    public bool Exists => File.Exists(_configPath);

    private static string? Encrypt(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return "DPAPI:" + Convert.ToBase64String(encrypted);
    }

    private static string? Decrypt(string? value)
    {
        if (string.IsNullOrEmpty(value) || !value.StartsWith("DPAPI:")) return value;
        var encrypted = Convert.FromBase64String(value["DPAPI:".Length..]);
        var bytes = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }
}
