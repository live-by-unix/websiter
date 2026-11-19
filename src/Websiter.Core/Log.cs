using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Websiter;

/// <summary>
/// Central logging facade for Websiter applications.
/// Writes structured, colorized console output and can be bridged to
/// any <see cref="ILogger"/> pipeline by the runtime.
/// </summary>
public static class Log
{
    private static ILogger? _logger;
    private static readonly ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> _pending = new();
    private static volatile bool _initialized;

    internal static void Initialize(ILogger logger)
    {
        _logger = logger;
        _initialized = true;
        while (_pending.TryDequeue(out var entry))
            Write(entry.Level, entry.Message, entry.Exception);
    }

    /// <summary>Gets whether the logging facade has been initialized by a running application.</summary>
    public static bool IsInitialized => _initialized;

    public static void Debug(string message) => Write(LogLevel.Debug, message, null);
    public static void Info(string message) => Write(LogLevel.Information, message, null);
    public static void Warn(string message) => Write(LogLevel.Warning, message, null);
    public static void Error(string message) => Write(LogLevel.Error, message, null);
    public static void Error(string message, Exception exception) => Write(LogLevel.Error, message, exception);
    public static void Fatal(string message) => Write(LogLevel.Critical, message, null);
    public static void Fatal(string message, Exception exception) => Write(LogLevel.Critical, message, exception);

    private static void Write(LogLevel level, string message, Exception? exception)
    {
        var logger = _logger;
        if (logger is null)
        {
            if (!_initialized)
            {
                _pending.Enqueue((level, message, exception));
                return;
            }
            ConsoleWriter.Write(level, message, exception);
            return;
        }

        var category = "Websiter";
        switch (level)
        {
            case LogLevel.Debug: logger.LogDebug("[{Category}] {Message}", category, message); break;
            case LogLevel.Information: logger.LogInformation("[{Category}] {Message}", category, message); break;
            case LogLevel.Warning: logger.LogWarning("[{Category}] {Message}", category, message); break;
            case LogLevel.Error: logger.LogError(exception, "[{Category}] {Message}", category, message); break;
            case LogLevel.Critical: logger.LogCritical(exception, "[{Category}] {Message}", category, message); break;
            default: logger.Log(level, exception, "[{Category}] {Message}", category, message); break;
        }
    }
}

/// <summary>
/// Colorized console writer used before an <see cref="ILogger"/> is attached.
/// Detects terminal color support and degrades gracefully for pipes/CI.
/// </summary>
public static class ConsoleWriter
{
    private static readonly bool _useColor = DetectColorSupport();

    private static bool DetectColorSupport()
    {
        if (Environment.GetEnvironmentVariable("NO_COLOR") is not null) return false;
        if (Environment.GetEnvironmentVariable("TERM") == "dumb") return false;
        try
        {
            return !Console.IsOutputRedirected && !Console.IsErrorRedirected;
        }
        catch
        {
            return false;
        }
    }

    public static bool ColorsEnabled => _useColor;

    public static void Write(LogLevel level, string message, Exception? exception = null)
    {
        var (label, color) = level switch
        {
            LogLevel.Debug => ("DBG", "\u001b[90m"),
            LogLevel.Information => ("INF", "\u001b[36m"),
            LogLevel.Warning => ("WRN", "\u001b[33m"),
            LogLevel.Error => ("ERR", "\u001b[31m"),
            LogLevel.Critical => ("FTL", "\u001b[1;31m"),
            _ => ("LOG", "\u001b[37m"),
        };

        var timestamp = DateTimeOffset.Now.ToString("HH:mm:ss.fff");
        var line = $"{timestamp} {label} {message}";

        if (_useColor)
        {
            Console.Error.Write($"{color}{line}\u001b[0m");
        }
        else
        {
            Console.Error.Write(line);
        }
        Console.Error.WriteLine();

        if (exception is not null)
            Console.Error.WriteLine(exception);
    }
}
