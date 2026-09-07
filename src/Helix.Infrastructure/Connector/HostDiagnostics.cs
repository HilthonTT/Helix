using Helix.Application.Abstractions.Connector;
using Microsoft.Extensions.Logging;
using System.Net.Sockets;

namespace Helix.Infrastructure.Connector;

internal sealed class HostDiagnostics(ILogger<HostDiagnostics> logger) : IHostDiagnostics
{
    private static readonly int[] SmbPorts = [445, 139];

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    public async Task<HostProbe> ProbeAsync(string host, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return new HostProbe(false, null, null);
        }

        string trimmed = host.Trim();
        string? alternate = await Task.Run(() => HostSpelling.AlternateOf(trimmed), cancellationToken);

        foreach (int port in SmbPorts)
        {
            if (await CanConnectAsync(host, port, cancellationToken))
            {
                return new HostProbe(true, port, alternate);
            }
        }

        return new HostProbe(false, null, alternate);
    }

    private async Task<bool> CanConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);

        using var client = new TcpClient();

        try
        {
            await client.ConnectAsync(host, port, timeout.Token);

            return true;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not probe a host on port {Port} while diagnosing a drive.", port);

            return false;
        }
    }
}
