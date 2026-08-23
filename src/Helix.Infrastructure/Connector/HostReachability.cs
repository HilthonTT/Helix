using Helix.Application.Abstractions.Connector;
using Microsoft.Extensions.Logging;
using System.Net.Sockets;

namespace Helix.Infrastructure.Connector;

/// <summary>
/// Decides whether a NAS is reachable by opening a TCP connection to the ports SMB
/// actually listens on.
/// </summary>
/// <remarks>
/// A TCP handshake rather than a ping: ICMP is blocked or deprioritised by a good number
/// of NAS firmwares and by most consumer routers between subnets, so a ping proves less
/// than the connection Helix is about to make anyway. Port 445 is SMB proper; 139 is
/// tried after it for the older devices that still front NetBIOS, so a working share is
/// never written off because one port was closed.
///
/// Readings are cached for a few seconds and shared, because the caller is a loop over
/// drives and several mapped drives are usually several shares of one NAS. Without it,
/// thirteen shares of one pool meant thirteen probes of one host every sweep. The cache
/// holds the in-flight task, not just the finished answer, so drives probing the same
/// host at the same moment wait on one connection between them.
/// </remarks>
internal class HostReachability : IHostReachability
{
    /// <summary>Ports tried, in order. 445 answers on anything made this century.</summary>
    private static readonly int[] SmbPorts = [445, 139];

    /// <summary>
    /// How long a host may take to answer before it is treated as absent.
    /// </summary>
    /// <remarks>
    /// Short on purpose: this runs to avoid a wait, so it must not become one. A NAS on
    /// the same LAN completes the handshake in single-digit milliseconds, and one that
    /// needs longer than this is not in a state where mounting it would have gone well.
    /// </remarks>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long a reading stands before the host is probed again.
    /// </summary>
    /// <remarks>
    /// Longer than the watchdog's sweep, so one sweep over many shares of one NAS costs
    /// one probe, and short enough that a NAS coming back is noticed within a sweep or
    /// two of it happening.
    /// </remarks>
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(10);

    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<HostReachability> _logger;
    private readonly Lock _gate = new();

    private readonly Dictionary<string, CachedProbe> _cache = new(StringComparer.OrdinalIgnoreCase);

    public HostReachability(IDateTimeProvider dateTimeProvider, ILogger<HostReachability> logger)
    {
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
    }

    public Task<bool> IsReachableAsync(string host, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            // Nothing to probe, and nothing this class can usefully say about it. The
            // connector will reject it with a message that names the real problem.
            return Task.FromResult(true);
        }

        DateTime now = _dateTimeProvider.UtcNow;

        lock (_gate)
        {
            if (_cache.TryGetValue(host, out CachedProbe? cached) && now < cached.ExpiresAtUtc)
            {
                return cached.Probe;
            }

            // Deliberately not passing the caller's token into the shared task: the probe
            // is handed to whoever asks next, and one caller giving up must not cancel it
            // out from under the others.
            Task<bool> probe = ProbeAsync(host);

            _cache[host] = new CachedProbe(probe, now + CacheDuration);

            return probe;
        }
    }

    /// <summary>
    /// Whether a TCP connection to <paramref name="host"/> on <paramref name="port"/>
    /// can be established inside the timeout.
    /// </summary>
    /// <remarks>
    /// Virtual so the caching and the port fallback above can be tested without a NAS,
    /// or a listener on a privileged port, on the other end.
    /// </remarks>
    protected virtual async Task<bool> CanConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);

        using var client = new TcpClient();

        await client.ConnectAsync(host, port, timeout.Token);

        return true;
    }

    private async Task<bool> ProbeAsync(string host)
    {
        foreach (int port in SmbPorts)
        {
            try
            {
                if (await CanConnectAsync(host, port, CancellationToken.None))
                {
                    return true;
                }
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                // Refused, unresolvable or silent. Expected — try the next port.
            }
            catch (Exception ex)
            {
                // Something other than the host being absent. Reported as reachable so a
                // fault in here can never be what stops a drive reconnecting.
                _logger.LogDebug(ex, "Could not probe a host on port {Port}; treating it as reachable.", port);

                return true;
            }
        }

        return false;
    }

    /// <summary>A probe in flight or recently finished, and when it stops being trusted.</summary>
    private sealed record CachedProbe(Task<bool> Probe, DateTime ExpiresAtUtc);
}
