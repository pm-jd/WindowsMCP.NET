using WindowsMcpNet.Config;
using Xunit;

namespace WindowsMcpNet.Tests.Config;

public class CliOptionsTests
{
    [Fact]
    public void ApplyTo_CertPath_EnablesHttpsAndSetsPathAndPassword()
    {
        var config = new AppConfig();
        var options = new CliOptions { CertPath = @"C:\certs\server.pfx", CertPassword = "secret" };

        options.ApplyTo(config);

        Assert.True(config.Https.Enabled);
        Assert.Equal(@"C:\certs\server.pfx", config.Https.CertPath);
        Assert.Equal("secret", config.Https.CertPassword);
    }

    [Fact]
    public void ApplyTo_LogLevel_OverridesConfig()
    {
        var config = new AppConfig();

        new CliOptions { LogLevel = "Debug" }.ApplyTo(config);

        Assert.Equal("Debug", config.LogLevel);
    }

    [Fact]
    public void ApplyTo_NetworkAndAuthOptions_OverrideConfig()
    {
        var config = new AppConfig { AllowedIps = ["192.168.0.1"] };
        var options = new CliOptions
        {
            Transport = "stdio",
            Host = "127.0.0.1",
            Port = 9000,
            AdvertiseHost = "build-pc",
            ApiKey = "wmcp_cli",
            AllowIps = ["10.0.0.1", "10.0.0.2"],
        };

        options.ApplyTo(config);

        Assert.Equal("stdio", config.Transport);
        Assert.Equal("127.0.0.1", config.Host);
        Assert.Equal(9000, config.Port);
        Assert.Equal("build-pc", config.AdvertiseHost);
        Assert.Equal("wmcp_cli", config.ApiKey);
        Assert.Equal(["10.0.0.1", "10.0.0.2"], config.AllowedIps);
    }

    [Fact]
    public void ApplyTo_EmptyOptions_LeavesConfigUntouched()
    {
        var config = new AppConfig
        {
            Transport = "http",
            Port = 8123,
            ApiKey = "wmcp_file",
            LogLevel = "Warning",
            AllowedIps = ["192.168.0.1"],
        };

        new CliOptions().ApplyTo(config);

        Assert.Equal("http", config.Transport);
        Assert.Equal(8123, config.Port);
        Assert.Equal("wmcp_file", config.ApiKey);
        Assert.Equal("Warning", config.LogLevel);
        Assert.Equal(["192.168.0.1"], config.AllowedIps);
        Assert.False(config.Https.Enabled);
    }
}
