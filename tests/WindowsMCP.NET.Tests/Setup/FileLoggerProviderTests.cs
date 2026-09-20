using Microsoft.Extensions.Logging;
using WindowsMcpNet.Setup;
using Xunit;

namespace WindowsMcpNet.Tests.Setup;

public class FileLoggerProviderTests
{
    [Fact]
    public void CreateLogger_HonoursConfiguredMinimumLevel()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wmcp_logtest_{Guid.NewGuid():N}.log");
        try
        {
            using var provider = new FileLoggerProvider(path, minLevel: LogLevel.Debug);
            var logger = provider.CreateLogger("Test");

            Assert.True(logger.IsEnabled(LogLevel.Debug));
            Assert.False(logger.IsEnabled(LogLevel.Trace));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CreateLogger_DefaultMinimumLevel_IsInformation()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wmcp_logtest_{Guid.NewGuid():N}.log");
        try
        {
            using var provider = new FileLoggerProvider(path);
            var logger = provider.CreateLogger("Test");

            Assert.False(logger.IsEnabled(LogLevel.Debug));
            Assert.True(logger.IsEnabled(LogLevel.Information));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Log_BelowMinimumLevel_WritesNothing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wmcp_logtest_{Guid.NewGuid():N}.log");
        try
        {
            using (var provider = new FileLoggerProvider(path, minLevel: LogLevel.Warning))
            {
                var logger = provider.CreateLogger("Test");
                logger.LogInformation("hidden");
                logger.LogWarning("visible");
            }

            var content = File.ReadAllText(path);
            Assert.DoesNotContain("hidden", content);
            Assert.Contains("visible", content);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
