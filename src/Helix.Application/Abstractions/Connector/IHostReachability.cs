namespace Helix.Application.Abstractions.Connector;

/// <summary>
/// Answers whether a NAS is on the other end of the network right now, before anything
/// tries to mount a share from it.
/// </summary>
/// <remarks>
/// The unattended reconnect loop used to retry blind. On the network the NAS lives on
/// that is fine — the mount fails in milliseconds and is retried. Away from it, every
/// attempt instead waits for the platform's own SMB timeout, and each one files a
/// warning naming a share that was never going to answer, so a laptop carried to a café
/// filled its log with failures that only ever meant "not on that network".
///
/// A drive whose host cannot be reached is not a drive that failed to connect, and this
/// is what lets the two be told apart.
/// </remarks>
public interface IHostReachability
{
    /// <summary>
    /// Whether <paramref name="host"/> is currently accepting SMB connections.
    /// </summary>
    /// <remarks>
    /// Answers false only for a host that positively did not answer — a refused
    /// connection, a name that does not resolve, a timeout. Anything unexpected reads as
    /// reachable, because the cost of being wrong is asymmetric: a false "unreachable"
    /// stops Helix reconnecting a drive that would have come back, which is the one
    /// thing this must never do.
    /// </remarks>
    Task<bool> IsReachableAsync(string host, CancellationToken cancellationToken = default);
}
