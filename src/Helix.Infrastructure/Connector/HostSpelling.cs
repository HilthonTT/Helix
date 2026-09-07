using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Helix.Infrastructure.Connector;

internal static class HostSpelling
{
    private const int LookupTimeoutMilliseconds = 1_500;

    private static readonly ConcurrentDictionary<string, string?> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    internal static bool IsAddress(string host) => IPAddress.TryParse(host, out _);

    internal static string? AlternateOf(string uncHost)
    {
        if (Cache.TryGetValue(uncHost, out string? cached))
        {
            return cached;
        }

        (bool answered, string? resolved) = Lookup(uncHost);

        if (answered)
        {
            Cache.TryAdd(uncHost, resolved);
        }

        return resolved;
    }

    private static (bool Answered, string? Spelling) Lookup(string uncHost)
    {
        (bool answered, string? resolved) = Within(() => IPAddress.TryParse(uncHost, out IPAddress? address)
            ? NameOf(address)
            : AddressOf(uncHost));

        return string.Equals(resolved, uncHost, StringComparison.OrdinalIgnoreCase)
            ? (answered, null)
            : (answered, resolved);
    }

    private static string? NameOf(IPAddress address)
    {
        string? name = Answered(() => Dns.GetHostEntry(address).HostName);

        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        int dot = name.IndexOf('.', StringComparison.Ordinal);

        return dot > 0 ? name[..dot] : name;
    }

    private static string? AddressOf(string name)
    {
        IPAddress[]? addresses = Answered(() => Dns.GetHostAddresses(name));

        if (addresses is null)
        {
            return null;
        }

        IPAddress? preferred =
            Array.Find(addresses, a => a.AddressFamily == AddressFamily.InterNetwork) ??
            Array.Find(addresses, a => a.AddressFamily == AddressFamily.InterNetworkV6);

        return preferred is null ? null : WindowsNasConnector.ToUncHost(preferred.ToString());
    }

    private static T? Answered<T>(Func<T> lookup) where T : class
    {
        try
        {
            return lookup();
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            return null;
        }
    }

    private static (bool Answered, string? Spelling) Within(Func<string?> lookup)
    {
        try
        {
            Task<string?> task = Task.Run(lookup);

            if (task.Wait(LookupTimeoutMilliseconds))
            {
                return (true, task.Result);
            }

            Abandon(task);

            return (false, null);
        }
        catch (Exception)
        {
            return (true, null);
        }
    }

    private static void Abandon(Task task) => _ = task.ContinueWith(
        static finished => _ = finished.Exception,
        CancellationToken.None,
        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
        TaskScheduler.Default);
}
