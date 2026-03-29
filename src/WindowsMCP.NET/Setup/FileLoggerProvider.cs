#nullable enable
namespace WindowsMcpNet.Setup;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logPath;
    private readonly long _maxFileSize;
    private readonly object _lock = new();
    private StreamWriter? _writer;

    public FileLoggerProvider(string logPath, long maxFileSize = 10 * 1024 * 1024) // 10 MB default
    {
        _logPath = logPath;
        _maxFileSize = maxFileSize;
        _writer = new StreamWriter(new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            AutoFlush = true,
        };
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    internal void WriteLog(string categoryName, LogLevel level, string message)
    {
        lock (_lock)
        {
            if (_writer is null) return;

            try
            {
                // Rotate if needed
                if (new FileInfo(_logPath).Length > _maxFileSize)
                {
                    _writer.Dispose();
                    var backupPath = _logPath + ".1";
                    if (File.Exists(backupPath)) File.Delete(backupPath);
                    File.Move(_logPath, backupPath);
                    _writer = new StreamWriter(new FileStream(_logPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        AutoFlush = true,
                    };
                }

                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var levelStr = level switch
                {
                    LogLevel.Trace => "TRC",
                    LogLevel.Debug => "DBG",
                    LogLevel.Information => "INF",
                    LogLevel.Warning => "WRN",
                    LogLevel.Error => "ERR",
                    LogLevel.Critical => "CRT",
                    _ => "???",
                };

                // Shorten category: "WindowsMcpNet.Services.DesktopService" -> "DesktopService"
                var shortCategory = categoryName.Contains('.')
                    ? categoryName[(categoryName.LastIndexOf('.') + 1)..]
                    : categoryName;

                _writer.WriteLine($"{timestamp} [{levelStr}] {shortCategory}: {message}");
            }
            catch
            {
                // Don't throw from logging
            }
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _provider;
        private readonly string _categoryName;

        public FileLogger(FileLoggerProvider provider, string categoryName)
        {
            _provider = provider;
            _categoryName = categoryName;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var message = formatter(state, exception);
            if (exception is not null)
                message += $"\n  {exception.GetType().Name}: {exception.Message}";
            _provider.WriteLog(_categoryName, logLevel, message);
        }
    }
}
