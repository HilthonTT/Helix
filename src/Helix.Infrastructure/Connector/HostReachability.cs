using Helix.Application.Abstractions.Connector;
using Helix.Domain.Drives;
using Microsoft.Extensions.Logging;
using System.Net.Sockets;

namespace Helix.Infrastructure.Connector;

internal class HostReachability : IHostReachability
{
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

    public Task<bool> IsReachableAsync(
        string host,
        CancellationToken cancellationToken = default,
        IReadOnlyList<int>? ports = null)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return Task.FromResult(true);
        }

        DateTime now = _dateTimeProvider.UtcNow;

        lock (_gate)
        {
            string key = KeyFor(host, ports);

            if (_cache.TryGetValue(key, out CachedProbe? cached) && now < cached.ExpiresAtUtc)
            {
                return cached.Probe;
            }

            Task<bool> probe = ProbeAsync(host, ports ?? DriveProtocols.SmbPorts);

            _cache[key] = new CachedProbe(probe, now + CacheDuration);

            return probe;
        }
    }

    public Task<bool> ProbeNowAsync(
        string host,
        CancellationToken cancellationToken = default,
        IReadOnlyList<int>? ports = null)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return Task.FromResult(true);
        }

        DateTime now = _dateTimeProvider.UtcNow;

        lock (_gate)
        {
            Task<bool> probe = ProbeAsync(host, ports ?? DriveProtocols.SmbPorts);

            _cache[KeyFor(host, ports)] = new CachedProbe(probe, now + CacheDuration);

            return probe;
        }
    }

    private static string KeyFor(string host, IReadOnlyList<int>? ports) =>
        ports is null ? host : $"{host}|{string.Join(',', ports)}";

    protected virtual async Task<bool> CanConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);

        using var client = new TcpClient();

        await client.ConnectAsync(host, port, timeout.Token);

        return true;
    }

    private async Task<bool> ProbeAsync(string host, IReadOnlyList<int> ports)
    {
        foreach (int port in ports)
        {
            try
            {
                if (await CanConnectAsync(host, port, CancellationToken.None))
                {
                    return true;
                }
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AccessDenied)
            {
                // A local firewall refusing this process says nothing about the host, and the
                // kernel's SMB redirector is not bound by a per-app rule.
                _logger.LogDebug("A local policy refused the probe on port {Port}; treating the host as reachable.", port);

                return true;
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
