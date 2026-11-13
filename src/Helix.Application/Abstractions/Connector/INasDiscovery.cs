namespace Helix.Application.Abstractions.Connector;

public interface INasDiscovery
{
    Task<IReadOnlyList<DiscoveredHost>> DiscoverAsync(CancellationToken cancellationToken = default);
}
