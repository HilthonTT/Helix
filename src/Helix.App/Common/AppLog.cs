using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Helix.App.Common;

internal static class AppLog
{
    private static readonly ConcurrentDictionary<Type, ILogger> Loggers = new();

    public static ILogger For<T>() => For(typeof(T));

    public static ILogger For(Type type) => Loggers.GetOrAdd(type, Create);

    private static ILogger Create(Type type)
    {
        try
        {
            return App.ServiceProvider
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger(type.FullName ?? type.Name);
        }
        catch (Exception)
        {
            return NullLogger.Instance;
        }
    }

    private sealed class NullLogger : ILogger
    {
        public static readonly NullLogger Instance = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }
    }
}
