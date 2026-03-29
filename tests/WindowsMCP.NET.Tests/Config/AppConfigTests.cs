using System.Text.Json;
using WindowsMcpNet.Config;
using Xunit;

namespace WindowsMcpNet.Tests.Config;

public class AppConfigTests
{
    [Fact]
    public void DefaultConfig_HasExpectedValues()
    {
        var config = new AppConfig();

        Assert.Equal("http", config.Transport);
        Assert.Equal("0.0.0.0", config.Host);
        Assert.Equal(8000, config.Port);
        Assert.Null(config.ApiKey);
        Assert.False(config.Https.Enabled);
        Assert.Empty(config.AllowedIps);
    }

    [Fact]
    public void Config_SerializesAndDeserializes()
    {
        var config = new AppConfig
        {
            Transport = "stdio",
            Port = 9000,
            ApiKey = "wmcp_testkey123"
        };

        var json = JsonSerializer.Serialize(config, AppConfigJsonContext.Default.AppConfig);
        var deserialized = JsonSerializer.Deserialize(json, AppConfigJsonContext.Default.AppConfig);

        Assert.NotNull(deserialized);
        Assert.Equal("stdio", deserialized.Transport);
        Assert.Equal(9000, deserialized.Port);
        Assert.Equal("wmcp_testkey123", deserialized.ApiKey);
    }
}

public class ConfigManagerTests
{
    [Fact]
    public void LoadConfig_ReturnsDefault_WhenFileNotExists()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var manager = new ConfigManager(tempDir);
            var config = manager.Load();

            Assert.Equal("http", config.Transport);
            Assert.Equal(8000, config.Port);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var manager = new ConfigManager(tempDir);
            var config = new AppConfig { Port = 9999, ApiKey = "wmcp_test" };
            manager.Save(config);

            var loaded = manager.Load();
            Assert.Equal(9999, loaded.Port);
            Assert.Equal("wmcp_test", loaded.ApiKey);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
