using Xunit;
using WindowsMcpNet.Config;

namespace WindowsMcpNet.Tests.Config;

public class CliParserTests
{
    [Fact]
    public void Parse_NoArgs_ReturnsDefaults()
    {
        var result = CliParser.Parse([]);
        Assert.Null(result.Command);
        Assert.Null(result.Transport);
        Assert.Null(result.Port);
        Assert.False(result.ShowHelp);
        Assert.False(result.ShowVersion);
    }

    [Fact]
    public void Parse_SetupCommand_Detected()
    {
        var result = CliParser.Parse(["setup"]);
        Assert.Equal("setup", result.Command);
    }

    [Fact]
    public void Parse_SetupNewKey_Detected()
    {
        var result = CliParser.Parse(["setup", "--new-key"]);
        Assert.Equal("setup", result.Command);
        Assert.True(result.NewKey);
    }

    [Fact]
    public void Parse_InfoCommand_Detected()
    {
        var result = CliParser.Parse(["info"]);
        Assert.Equal("info", result.Command);
    }

    [Fact]
    public void Parse_TransportAndPort()
    {
        var result = CliParser.Parse(["--transport", "stdio", "--port", "9000"]);
        Assert.Equal("stdio", result.Transport);
        Assert.Equal(9000, result.Port);
    }

    [Fact]
    public void Parse_AllowIp_Multiple()
    {
        var result = CliParser.Parse(["--allow-ip", "10.0.0.1", "--allow-ip", "10.0.0.2"]);
        Assert.Equal(["10.0.0.1", "10.0.0.2"], result.AllowIps);
    }

    [Fact]
    public void Parse_Help()
    {
        var result = CliParser.Parse(["--help"]);
        Assert.True(result.ShowHelp);
    }

    [Fact]
    public void Parse_Version()
    {
        var result = CliParser.Parse(["--version"]);
        Assert.True(result.ShowVersion);
    }
}
