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

    /// <summary>
    /// The letters that are up: <see cref="GetConnectedLetters"/> without a network letter whose
    /// mapping is only remembered, as a persistent one whose restore failed or whose server went away.
    /// </summary>
    HashSet<string> GetLiveLetters();

    bool IsMountedFrom(Drive drive);

    bool IsLiveFrom(Drive drive);

    bool HasOtherMountsOn(Drive drive);

    int? CountOpenHandles(string letter);

    Task<Result<IReadOnlyList<string>>> ListSharesAsync(
        string host,
        string username,
        string password,
        CancellationToken cancellationToken = default);

    IReadOnlyList<MappedShare> GetMappedShares();
}
