using Helix.Domain.Drives;

namespace Helix.Application.Abstractions.Connector;

public sealed record LateMountOutcome(string Letter, bool IsMounted, string Description);

public interface INasConnector
{
    event EventHandler<LateMountOutcome>? MountSettledLate;

    Task<Result> ConnectAsync(Drive drive, CancellationToken cancellationToken = default);

    Task<Result> DisconnectAsync(Drive drive, CancellationToken cancellationToken = default);

    Task<Result> TestAsync(Drive drive, CancellationToken cancellationToken = default);

    bool IsConnected(string letter);

    string GetMountPath(string letter);

    HashSet<string> GetConnectedLetters();

    bool IsMountedFrom(Drive drive);

    bool HasOtherMountsOn(Drive drive);
}
