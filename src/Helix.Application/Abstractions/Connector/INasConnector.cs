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
    /// The filesystem path a drive letter is mounted at on this platform — <c>Z:\</c> on
    /// Windows, a directory under the mount root on macOS.
    /// </summary>
    /// <remarks>
    /// Asked of the connector because the connector is what put it there. Answers for any
    /// letter, mounted or not: it is where the drive <em>would</em> be, and callers that
    /// need to know whether anything is actually there ask
    /// <see cref="IsConnected"/> or look at the path.
    /// </remarks>
    string GetMountPath(string letter);

    /// <summary>
    /// Returns every currently-mapped drive letter (uppercase, no colon). Callers
    /// that need to check connection status for many drives should use this once
    /// rather than calling <see cref="IsConnected"/> in a loop.
    /// </summary>
    HashSet<string> GetConnectedLetters();

    /// <summary>
    /// Whether the drive's letter is currently mounted <em>and</em> what it is mounted from
    /// is this drive's own share.
    /// </summary>
    /// <remarks>
    /// Tells a letter that is genuinely taken — a USB stick, another account's mapping —
    /// from one that is already carrying the share being described. The second is the
    /// normal state of a machine whose mappings outlived Helix's database: a persistent
    /// mapping survives a reinstall, and a live one survives an update, so the drives
    /// come back mounted before the records that describe them are restored. Refusing
    /// those letters as "in use" made a backup impossible to import until every share
    /// had been disconnected by hand.
    /// </remarks>
    bool IsMountedFrom(Drive drive);
}
