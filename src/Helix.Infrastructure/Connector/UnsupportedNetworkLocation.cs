using Helix.Application.Abstractions.Connector;

namespace Helix.Infrastructure.Connector;

internal sealed class UnsupportedNetworkLocation : INetworkLocation
{
    public bool IsSupported => false;

    public Task<NetworkLocation?> GetCurrentAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<NetworkLocation?>(null);
}
