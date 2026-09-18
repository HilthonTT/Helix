namespace Helix.Application.Abstractions.Connector;

public sealed record NetworkLocation(string Id, string Name);

public interface INetworkLocation
{
    bool IsSupported { get; }

    Task<NetworkLocation?> GetCurrentAsync(CancellationToken cancellationToken = default);
}
