using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text;

namespace Helix.Infrastructure.Diagnostics;

[ProviderAlias("HelixFile")]
internal sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly LogFileWriter _writer;
    private readonly LogLevel _minimumLevel;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new(StringComparer.Ordinal);

    public FileLoggerProvider(LogFileWriter writer, LogLevel minimumLevel)
    {
        _writer = writer;
        _minimumLevel = minimumLevel;
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new FileLogger(_writer, name, _minimumLevel));

    public void Dispose()
    {
        _loggers.Clear();

        _writer.Flush();
    }
}

internal sealed class FileLogger : ILogger
{
    private readonly LogFileWriter _writer;
    private readonly string _category;
    private readonly LogLevel _minimumLevel;

    public FileLogger(LogFileWriter writer, string category, LogLevel minimumLevel)
    {
        _writer = writer;

        int lastDot = category.LastIndexOf('.');
        _category = lastDot >= 0 && lastDot < category.Length - 1 ? category[(lastDot + 1)..] : category;

        _minimumLevel = minimumLevel;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= _minimumLevel && logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var line = new StringBuilder()
            .Append(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture))
            .Append(" [")
            .Append(Describe(logLevel))
            .Append("] ")
            .Append(_category)
            .Append(": ")
            .Append(formatter(state, exception));

        if (exception is not null)
        {
            line.Append(Environment.NewLine).Append(exception);
        }

        _writer.Write(line.ToString());
    }

    private static string Describe(LogLevel level) => level switch
    {
        LogLevel.Trace => "trc",
        LogLevel.Debug => "dbg",
        LogLevel.Information => "inf",
        LogLevel.Warning => "wrn",
        LogLevel.Error => "err",
        LogLevel.Critical => "crt",
        _ => "___",
    };
}
