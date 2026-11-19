using Microsoft.Extensions.Logging;

namespace Websiter.Workers;

/// <summary>
/// Minimal append-only file logger provider used by worker and application
/// processes. Writes plain-text lines; safe for concurrent processes appending
/// to the same file (FileShare.ReadWrite).
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _path;
    private readonly object _gate = new();
    private StreamWriter? _writer;

    public FileLoggerProvider(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(_path, _gate, this);

    internal void EnsureWriter()
    {
        if (_writer is not null) return;
        _writer = new StreamWriter(new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true,
        };
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly string _path;
        private readonly object _gate;
        private readonly FileLoggerProvider _provider;

        public FileLogger(string path, object gate, FileLoggerProvider provider)
        {
            _path = path;
            _gate = gate;
            _provider = provider;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var line = $"{DateTimeOffset.Now:O} [{logLevel}] {formatter(state, exception)}";
            if (exception is not null) line += $" {exception}";
            lock (_gate)
            {
                _provider.EnsureWriter();
                _provider._writer?.WriteLine(line);
            }
        }
    }
}
