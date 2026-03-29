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
        return JsonSerializer.Deserialize(json, AppConfigJsonContext.Default.AppConfig)
               ?? new AppConfig();
    }

    public void Save(AppConfig config)
    {
        var json = JsonSerializer.Serialize(config, AppConfigJsonContext.Default.AppConfig);
        File.WriteAllText(_configPath, json);
    }

    public bool Exists => File.Exists(_configPath);
}
