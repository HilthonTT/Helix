using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Helix.Infrastructure.Connector;

/// <summary>
/// The other way of writing the same server: its name when given its address, its
/// address when given its name.
/// </summary>
/// <remarks>
/// Windows keeps one credential context per server <i>name string</i>, not per machine,
/// so <c>\192.168.1.6</c> and <c>\NAS</c> are handed a slot each even though they
/// resolve to the same NAS. That is normally a trap — two spellings of one server in the
/// drive list quietly double-count it — but it is also the only way past a conflict when
/// something outside Helix is holding the server under credentials of its own, which is
/// what <see cref="WindowsNasConnector"/> uses this for.
///
/// Answers are cached for the life of the process, including the failures. A home network
/// that cannot answer a reverse lookup will not start answering an hour later, and a
/// lookup per drive per sweep would put thirteen of them on the critical path of every
/// reconnect. The cost of the cache being stale is one mount attempt under a name that
/// has moved, which fails the way any wrong host fails.
/// </remarks>
internal static class HostSpelling
{
    /// <summary>
    /// How long a lookup is allowed before it is treated as unanswered.
    /// </summary>
    /// <remarks>
    /// Short on purpose. This only ever runs on a path that has already failed, or ahead
    /// of a mount the user is waiting on, and a home router with no PTR records for its
    /// DHCP leases is the normal case rather than the exception — that is a lookup which
    /// does not fail so much as never answer. The mount timeout is five seconds and this
    /// has to fit inside it with room for the attempt it exists to enable.
    /// </remarks>
    private const int LookupTimeoutMilliseconds = 1_500;

    private static readonly ConcurrentDictionary<string, string?> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the host is written as an IP literal rather than a name.</summary>
    internal static bool IsAddress(string host) => IPAddress.TryParse(host, out _);

    /// <summary>
    /// The alternate spelling of <paramref name="uncHost"/>, or null when DNS has nothing
    /// to say about it — which is not an error, just an option this machine does not have.
    /// </summary>
    internal static string? AlternateOf(string uncHost) => Cache.GetOrAdd(uncHost, Lookup);

    private static string? Lookup(string uncHost)
    {
        string? resolved = Within(() => IPAddress.TryParse(uncHost, out IPAddress? address)
            ? NameOf(address)
            : AddressOf(uncHost));

        // A lookup that hands back what it was given is not an alternate spelling, and
        // mounting under it a second time would repeat the conflict rather than dodge it.
        return string.Equals(resolved, uncHost, StringComparison.OrdinalIgnoreCase)
            ? null
            : resolved;
    }

    /// <summary>Reverse lookup: the address's name, unqualified if it has one.</summary>
    /// <remarks>
    /// The short name is preferred over the FQDN because it is the spelling SMB itself
    /// uses and the one the user would have typed. Either would work as a UNC host and
    /// either gets its own credential slot; this is the one that will look right in a log.
    /// </remarks>
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

    /// <summary>Forward lookup: the name's address, IPv4 for preference.</summary>
    /// <remarks>
    /// IPv4 first because a UNC path cannot hold a colon, so an IPv6 answer has to go
    /// through the <c>ipv6-literal.net</c> encoding and is the more fragile of the two.
    /// <see cref="WindowsNasConnector.ToUncHost"/> renders whichever comes back.
    /// </remarks>
    private static string? AddressOf(string name)
    {
        IPAddress[] addresses = Dns.GetHostAddresses(name);

        IPAddress? preferred =
            Array.Find(addresses, a => a.AddressFamily == AddressFamily.InterNetwork) ??
            Array.Find(addresses, a => a.AddressFamily == AddressFamily.InterNetworkV6);

        return preferred is null ? null : WindowsNasConnector.ToUncHost(preferred.ToString());
    }

    /// <summary>
    /// Runs a lookup under a deadline, answering null for anything that does not come
    /// back in time or comes back as an error.
    /// </summary>
    /// <remarks>
    /// The task is abandoned rather than cancelled, because the BCL's synchronous
    /// resolver takes no cancellation token and the async one honours it only on some
    /// platforms. Abandoning costs a thread-pool thread until the resolver gives up, which
    /// is bounded by the OS resolver's own timeout and happens at most once per host.
    /// </remarks>
    private static string? Within(Func<string?> lookup)
    {
        try
        {
            Task<string?> task = Task.Run(lookup);

            return task.Wait(LookupTimeoutMilliseconds) ? task.Result : null;
        }
        catch (Exception)
        {
            // Every failure here means the same thing to the caller — there is no other
            // spelling to try — so a socket error, a bad name and a resolver that is not
            // running are not worth telling apart.
            return null;
        }
    }
}
