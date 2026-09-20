using Microsoft.Extensions.Logging;
using WindowsMcpNet.Config;
using Xunit;

namespace WindowsMcpNet.Tests.Config;

public class LogLevelParserTests
{
    [Theory]
    [InlineData("Debug", LogLevel.Debug)]
    [InlineData("debug", LogLevel.Debug)]
    [InlineData("WARNING", LogLevel.Warning)]
    [InlineData("Trace", LogLevel.Trace)]
    [InlineData("Error", LogLevel.Error)]
    public void Parse_KnownName_ReturnsLevel(string input, LogLevel expected)
    {
        Assert.Equal(expected, LogLevelParser.Parse(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("verbose")]
    public void Parse_UnknownOrMissing_DefaultsToInformation(string? input)
    {
        Assert.Equal(LogLevel.Information, LogLevelParser.Parse(input));
    }
}
