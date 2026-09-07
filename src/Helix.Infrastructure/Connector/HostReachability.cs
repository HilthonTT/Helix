using Helix.Application.Abstractions.Connector;
using Microsoft.Extensions.Logging;
using System.Net.Sockets;

namespace Helix.Infrastructure.Connector;

internal class HostReachability : IHostReachability
{
    private static readonly int[] SmbPorts = [445, 139];

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

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
            return Task.FromResult(true);
        }

        DateTime now = _dateTimeProvider.UtcNow;

        lock (_gate)
        {
            if (_cache.TryGetValue(host, out CachedProbe? cached) && now < cached.ExpiresAtUtc)
            {
                return cached.Probe;
            }

            Task<bool> probe = ProbeAsync(host);

            _cache[host] = new CachedProbe(probe, now + CacheDuration);

            return probe;
        }
    }

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
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not probe a host on port {Port}; treating it as reachable.", port);

                return true;
            }
        }

        return false;
    }

    private sealed record CachedProbe(Task<bool> Probe, DateTime ExpiresAtUtc);
}
