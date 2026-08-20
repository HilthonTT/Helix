namespace Helix.Application.Abstractions.Connector;

/// <summary>A drive whose connectivity is being watched.</summary>
/// <param name="AutoConnect">
/// Carried through the monitor so the reaction to a drop can tell a drive that wants
/// chasing from one the user asked to leave alone, without another database round trip
/// on a background thread.
/// </param>
public sealed record WatchedDrive(Guid Id, string Letter, bool AutoConnect = true);

/// <summary>A watched drive that has changed connectivity since the last poll.</summary>
public sealed record DriveConnectivityChange(
    Guid DriveId,
    string Letter,
    bool IsConnected,
    bool AutoConnect = true);

/// <summary>
/// Polls the connectivity of the watched drives in the background and reports
/// transitions.
/// </summary>
/// <remarks>
/// Connection state used to be read only when a drive row was bound or when the user
/// clicked something, so a share that dropped while the dashboard was open kept
/// rendering as connected until the user interacted. This watches for that instead of
/// waiting to be asked.
///
/// The monitor only detects; it never reconnects, writes audit entries or touches the
/// database. Reacting to a change is the caller's job, which keeps this side of the
/// boundary free of scoped services.
/// </remarks>
public interface IDriveMonitor
{
    /// <summary>
    /// Raised on a background thread when one or more watched drives change state.
    /// Handlers that touch bound properties must marshal to the UI thread themselves.
    /// </summary>
    event EventHandler<IReadOnlyList<DriveConnectivityChange>>? ConnectivityChanged;

    bool IsRunning { get; }

    /// <summary>
    /// Replaces the watched set and re-seeds the baseline from current connectivity.
    /// Re-seeding means drives already offline are not reported as fresh drops, so a
    /// caller cannot mistake "never connected" for "just disconnected".
    /// </summary>
    void Watch(IReadOnlyCollection<WatchedDrive> drives);

    void Start(TimeSpan interval);

    void Stop();

    /// <summary>Runs one poll immediately rather than waiting for the next tick.</summary>
    Task PollAsync(CancellationToken cancellationToken = default);
}
