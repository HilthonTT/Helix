using Helix.Domain.Drives;

namespace Helix.Application.Abstractions.Connector;

public interface INasConnector
{
    Task<Result> ConnectAsync(Drive drive, CancellationToken cancellationToken = default);

    Task<Result> DisconnectAsync(Drive drive, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks that the host, share and credentials work, without leaving the share
    /// mounted or claiming the drive's letter.
    /// </summary>
    /// <remarks>
    /// Exists so a drive can be verified while it is still being typed in. The drive
    /// passed here is usually a transient one built from the form and never saved, so
    /// implementations must not assume it exists in the database — or that its letter
    /// is free.
    /// </remarks>
    Task<Result> TestAsync(Drive drive, CancellationToken cancellationToken = default);

    bool IsConnected(string letter);

    /// <summary>
    /// Returns every currently-mapped drive letter (uppercase, no colon). Callers
    /// that need to check connection status for many drives should use this once
    /// rather than calling <see cref="IsConnected"/> in a loop.
    /// </summary>
    HashSet<string> GetConnectedLetters();
}
