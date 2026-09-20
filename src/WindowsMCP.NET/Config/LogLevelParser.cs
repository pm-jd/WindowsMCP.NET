namespace WindowsMcpNet.Config;

public static class LogLevelParser
{
    /// <summary>
    /// Parses a log level name from config.json / --log-level (case-insensitive).
    /// Unknown or missing values fall back to Information so a typo never silences logging.
    /// </summary>
    public static LogLevel Parse(string? value) =>
        Enum.TryParse<LogLevel>(value, ignoreCase: true, out var level) ? level : LogLevel.Information;
}
