using Helix.Domain.Drives;
using SharedKernel;

namespace Helix.Application.Abstractions.Connector;

public interface INasConnector
{
    Task<Result> ConnectAsync(Drive drive, CancellationToken cancellationToken = default);

    Task<Result> DisconnectAsync(Drive drive, CancellationToken cancellationToken = default);

    bool IsConnected(string letter);

    /// <summary>
    /// Returns every currently-mapped drive letter (uppercase, no colon). Callers
    /// that need to check connection status for many drives should use this once
    /// rather than calling <see cref="IsConnected"/> in a loop.
    /// </summary>
    HashSet<string> GetConnectedLetters();
}
