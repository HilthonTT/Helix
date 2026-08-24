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

    /// <summary>
    /// Marks a set of drive letters as being mounted or unmounted by Helix itself, so
    /// nothing that happens to them until the returned handle is disposed is reported
    /// as a change.
    /// </summary>
    /// <remarks>
    /// The monitor cannot tell a share that dropped from one the user just pressed
    /// disconnect on — both are a letter that stopped being there — and everything
    /// downstream reacts to the difference. Left unsuppressed, "disconnect all" is
    /// followed by a tray toast per drive and, with auto-connect on, by the watchdog
    /// putting every one of them straight back: the user disconnects their drives and
    /// is told seconds later that they reconnected.
    ///
    /// The handle covers the whole operation rather than being a note filed after it,
    /// because a poll landing between the unmount and the note would report the drop
    /// before anyone could say it was intended. On disposal the baseline is re-seeded
    /// from what is actually mounted, so whatever the caller did becomes the new normal
    /// and the next poll has nothing to say about it.
    ///
    /// Suppressions nest and are counted, so two overlapping callers cannot uncover
    /// each other's letters.
    /// </remarks>
    IDisposable Suppress(IEnumerable<string> letters);

    void Start(TimeSpan interval);

    void Stop();

    /// <summary>Runs one poll immediately rather than waiting for the next tick.</summary>
    Task PollAsync(CancellationToken cancellationToken = default);
}
