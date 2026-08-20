using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Helix.App.Common;

/// <summary>
/// A logger for the presentation-layer types the container never constructs.
/// </summary>
/// <remarks>
/// Pages, viewmodels and attached behaviours are instantiated by MAUI and by XAML, not by
/// DI, so they cannot take an <see cref="ILogger{T}"/> in a constructor. They already
/// reach for singletons through <c>App.ServiceProvider</c>, and this follows that same
/// established route rather than inventing a second one.
///
/// Anything the container does build — <c>DriveWatchdog</c>, <c>TrayIconService</c>, and
/// everything in Infrastructure — takes its logger as a constructor dependency instead.
/// Use this only where that is genuinely not an option.
/// </remarks>
internal static class AppLog
{
    private static readonly ConcurrentDictionary<Type, ILogger> Loggers = new();

    public static ILogger For<T>() => For(typeof(T));

    /// <summary>
    /// For the static helpers — a static class cannot be a type argument, so those pass
    /// their own <c>typeof</c> instead.
    /// </summary>
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
            // Asked for before the container exists, or after it has gone — during
            // startup and shutdown both happen. A logger that throws would take down the
            // code that was only trying to report something.
            return NullLogger.Instance;
        }
    }

    /// <summary>Swallows everything, for the windows where no real logger exists.</summary>
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
