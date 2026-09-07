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
        string name = Dns.GetHostEntry(address).HostName;

        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        int dot = name.IndexOf('.', StringComparison.Ordinal);

        return dot > 0 ? name[..dot] : name;
    }

    private static string? AddressOf(string name)
    {
        IPAddress[] addresses = Dns.GetHostAddresses(name);

        IPAddress? preferred =
            Array.Find(addresses, a => a.AddressFamily == AddressFamily.InterNetwork) ??
            Array.Find(addresses, a => a.AddressFamily == AddressFamily.InterNetworkV6);

        return preferred is null ? null : WindowsNasConnector.ToUncHost(preferred.ToString());
    }

    private static (bool Answered, string? Spelling) Within(Func<string?> lookup)
    {
        try
        {
            Task<string?> task = Task.Run(lookup);

            return task.Wait(LookupTimeoutMilliseconds) ? (true, task.Result) : (false, null);
        }
        catch (Exception)
        {
            return (true, null);
        }
    }
}
