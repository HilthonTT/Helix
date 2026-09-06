using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.Drives;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Features.Drives.Commands;
using Helix.Application.Features.Drives.Queries;
using Helix.Application.Features.Settings.Commands;
using Helix.Application.Features.Settings.Queries;
using Helix.Domain.Drives;
using Microsoft.Extensions.Logging;
using SettingsModel = Helix.Domain.Settings.Settings;

namespace Helix.App.Services;

/// <summary>
/// Reacts to what <see cref="IDriveMonitor"/> observes: refreshes the UI, and when
/// auto-connect is on, keeps trying to bring dropped drives back.
/// </summary>
/// <remarks>
/// The monitor lives in Infrastructure and only detects. Everything that needs a DI
/// scope, a use case or a UI message lives here, so no scoped service is ever captured
/// by a singleton.
///
/// Retries need their own loop rather than riding on the monitor's events. The monitor
/// reports edges — a drive that is still down at the next poll has not changed state,
/// so it raises nothing, and a reconnect scheduled after a failure would never be
/// attempted. The loop below is what actually drives the backoff.
/// </remarks>
internal sealed class DriveWatchdog
{
    /// <summary>
    /// Poll interval. Short enough that a dropped share is noticed while the user is
    /// still looking at the dashboard, long enough to stay off the radar.
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(20);

    /// <summary>How often pending reconnects are re-examined.</summary>
    private static readonly TimeSpan RetrySweepInterval = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long to wait before looking again at a drive whose NAS is not on this network.
    /// </summary>
    /// <remarks>
    /// Flat rather than escalating, and it deliberately does not count as a failure. The
    /// backoff exists to stop Helix hammering a share that is refusing it; a NAS that is
    /// simply somewhere else is not refusing anything, and the moment the user is back on
    /// its network the drive should come back in seconds rather than in the five minutes
    /// an escalated backoff would have reached by then.
    /// </remarks>
    private static readonly TimeSpan OfflineRetryInterval = TimeSpan.FromSeconds(30);

    private readonly IDriveMonitor _monitor;
    private readonly INasConnector _nasConnector;
    private readonly ILogger<DriveWatchdog> _logger;
    private readonly Lock _gate = new();

    /// <summary>Drives awaiting another reconnect attempt, keyed by drive id.</summary>
    private readonly Dictionary<Guid, PendingReconnect> _pending = [];

    /// <summary>Drives with a reconnect in flight, so the two paths cannot overlap.</summary>
    private readonly HashSet<Guid> _inFlight = [];

    private CancellationTokenSource? _retryCancellation;

    /// <summary>
    /// Bumped by <see cref="Stop"/>. An attempt that was already awaiting the reconnect
    /// when the user signed out compares this against what it captured, so it does not
    /// schedule a retry into the next user's queue.
    /// </summary>
    private int _generation;
    private bool _subscribed;

    public DriveWatchdog(IDriveMonitor monitor, INasConnector nasConnector, ILogger<DriveWatchdog> logger)
    {
        _monitor = monitor;
        _nasConnector = nasConnector;
        _logger = logger;
    }

    /// <summary>Begins watching. Safe to call on every dashboard appearance.</summary>
    public async Task StartAsync()
    {
        if (!_subscribed)
        {
            _monitor.ConnectivityChanged += OnConnectivityChanged;
            RegisterMessages();

            _subscribed = true;
        }

        await RefreshWatchedDrivesAsync();

        _monitor.Start(PollInterval);

        StartRetryLoop();
    }

    /// <summary>Stops watching. Called on sign-out so the next user starts clean.</summary>
    public void Stop()
    {
        _monitor.Stop();
        _monitor.Watch([]);

        CancellationTokenSource? retry = _retryCancellation;
        _retryCancellation = null;

        if (retry is not null)
        {
            try
            {
                retry.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already torn down by a concurrent Stop.
            }
            finally
            {
                retry.Dispose();
            }
        }

        lock (_gate)
        {
            _generation++;
            _pending.Clear();
            _inFlight.Clear();
        }
    }

    /// <summary>Re-reads the user's drives and hands the monitor the new watch set.</summary>
    public async Task RefreshWatchedDrivesAsync()
    {
        Result<List<Drive>> result = await ScopedHandler.HandleAsync((GetDrives h) => h.Handle());
        if (result.IsFailure)
        {
            return;
        }

        _monitor.Watch([.. result.Value.Select(drive => new WatchedDrive(drive.Id, drive.Letter, drive.AutoConnect))]);

        // A drive that was queued for reconnect and has since been deleted, or had its
        // own auto-connect switched off, is dropped from the queue here. Left in it, a
        // deleted drive was retried - and logged as a warning - every five minutes for
        // the rest of the session, since NotFound counted as one more failure.
        HashSet<Guid> stillWanted = [.. result.Value.Where(drive => drive.AutoConnect).Select(drive => drive.Id)];

        lock (_gate)
        {
            foreach (Guid driveId in _pending.Keys.Where(id => !stillWanted.Contains(id)).ToList())
            {
                _pending.Remove(driveId);
            }
        }
    }

    private void OnConnectivityChanged(object? sender, IReadOnlyList<DriveConnectivityChange> changes)
    {
        PublishToUi(changes.Select(change => change.DriveId));

        _ = HandleChangesAsync(changes);
    }

    private async Task HandleChangesAsync(IReadOnlyList<DriveConnectivityChange> changes)
    {
        try
        {
            bool autoConnectEnabled = await IsAutoConnectEnabledAsync();

            List<Task> attempts = [];

            foreach (DriveConnectivityChange change in changes)
            {
                if (change.IsConnected)
                {
                    // Back — by our doing or the user's. Either way, stop chasing it.
                    Forget(change.DriveId);
                    continue;
                }

                // Both switches have to be on: the user-level setting turns unattended
                // reconnecting on at all, the drive's own flag says whether this one
                // takes part. A drive excluded here still gets its drop recorded, which
                // is the other half of what this handler is for.
                bool reconnect = autoConnectEnabled && change.AutoConnect;

                // In parallel, as the connector's per-host gate serializes the mounts
                // anyway: one share hanging its mount used to hold the drop record and
                // the status pill of every drive behind it for up to a minute each.
                attempts.Add(AttemptAsync(change.DriveId, change.Letter, reconnect, recordDrop: true));
            }

            await Task.WhenAll(attempts);
        }
        catch (Exception ex)
        {
            // Fire-and-forget from an event handler: never let this reach the app.
            _logger.LogError(ex, "The drive watchdog failed to handle a connectivity change.");
        }
    }

    private void StartRetryLoop()
    {
        if (_retryCancellation is not null)
        {
            return;
        }

        _retryCancellation = new CancellationTokenSource();

        _ = RunRetryLoopAsync(_retryCancellation.Token);
    }

    private async Task RunRetryLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(RetrySweepInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await RetryDueAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Stop() was called.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The drive watchdog retry loop faulted; drives will no longer be retried this session.");
        }
    }

    private async Task RetryDueAsync()
    {
        // Nothing is reachable, so there is nothing to try. Cheap enough to ask every
        // sweep, and it saves a probe and a mount attempt per pending drive while the
        // machine is on no network at all — asleep in a bag, or between two Wi-Fi points.
        if (!HasNetwork())
        {
            return;
        }

        List<PendingReconnect> due;

        lock (_gate)
        {
            DateTime now = DateTime.UtcNow;

            due = [.. _pending.Values.Where(p => now >= p.NextAttemptUtc && !_inFlight.Contains(p.DriveId))];
        }

        if (due.Count == 0)
        {
            return;
        }

        if (!await IsAutoConnectEnabledAsync())
        {
            // Auto-connect was switched off after the drop; stop chasing.
            lock (_gate)
            {
                _pending.Clear();
            }

            return;
        }

        foreach (PendingReconnect pending in due)
        {
            // The user may have reconnected it by hand in the meantime.
            if (_nasConnector.IsConnected(pending.Letter))
            {
                Forget(pending.DriveId);
                continue;
            }

            await AttemptAsync(pending.DriveId, pending.Letter, reconnect: true, recordDrop: false);
        }
    }

    private async Task AttemptAsync(Guid driveId, string letter, bool reconnect, bool recordDrop)
    {
        int generation;

        lock (_gate)
        {
            if (!_inFlight.Add(driveId))
            {
                return;
            }

            generation = _generation;
        }

        try
        {
            Result result = await ScopedHandler.HandleAsync(
                (ReconnectDrive h) => h.Handle(new ReconnectDrive.Request(driveId, reconnect, recordDrop)));

            if (!reconnect)
            {
                // Recorded only; nothing to retry.
                return;
            }

            if (IsStale(generation))
            {
                // Stop() ran while the reconnect was in flight. The outcome belongs to a
                // session that is over; scheduling anything from it would queue this
                // drive under whoever signs in next.
                return;
            }

            // Gone, not failing: the drive was deleted while it was queued. Nothing to
            // retry, and nothing worth a warning in the log the user is asked to send.
            if (result.IsFailure && result.Error.Code == DriveErrors.NotFound(driveId).Code)
            {
                Forget(driveId);

                return;
            }

            if (result.IsSuccess)
            {
                _logger.LogInformation("Reconnected drive {Letter}: automatically.", letter);

                Forget(driveId);

                // Re-poll so the monitor's baseline and the UI both catch up now rather
                // than at the end of the interval.
                await _monitor.PollAsync();

                PublishToUi([driveId]);

                return;
            }

            if (result.Error.Code == DriveErrors.HostUnreachableCode)
            {
                // Not a failure to reconnect — the NAS is not on this network. Debug
                // rather than Warning: on a laptop this is the ordinary state of affairs
                // for most of the day, and a warning per drive per sweep would bury the
                // real failures in the log the user is asked to send on.
                _logger.LogDebug("Drive {Letter}: is waiting for its NAS to be reachable again.", letter);

                PublishFailureToUi(driveId, result.Error);

                ScheduleOfflineRetry(driveId, letter);

                return;
            }

            // The one line that makes an unattended failure diagnosable after the fact:
            // the reason the share refused, and how many times it has now refused.
            _logger.LogWarning(
                "Could not reconnect drive {Letter}: — {Reason}",
                letter,
                result.Error.Description);

            PublishFailureToUi(driveId, result.Error);

            ScheduleRetry(driveId, letter);
        }
        finally
        {
            lock (_gate)
            {
                _inFlight.Remove(driveId);
            }
        }
    }

    private bool IsStale(int generation)
    {
        lock (_gate)
        {
            return generation != _generation;
        }
    }

    private void ScheduleRetry(Guid driveId, string letter)
    {
        lock (_gate)
        {
            int failures = _pending.TryGetValue(driveId, out PendingReconnect? existing)
                ? existing.Failures + 1
                : 1;

            // 5s, 15s, 45s, ... capped at 5 minutes.
            double seconds = FirstRetryDelay.TotalSeconds * Math.Pow(3, failures - 1);
            TimeSpan delay = TimeSpan.FromSeconds(Math.Min(seconds, MaximumRetryDelay.TotalSeconds));

            _pending[driveId] = new PendingReconnect(driveId, letter, failures, DateTime.UtcNow + delay);
        }
    }

    /// <summary>
    /// Puts a drive back in the queue at the flat offline interval, without counting the
    /// deferral as one of the failures the backoff is built from.
    /// </summary>
    private void ScheduleOfflineRetry(Guid driveId, string letter)
    {
        lock (_gate)
        {
            int failures = _pending.TryGetValue(driveId, out PendingReconnect? existing)
                ? existing.Failures
                : 0;

            _pending[driveId] = new PendingReconnect(
                driveId,
                letter,
                failures,
                DateTime.UtcNow + OfflineRetryInterval);
        }
    }

    private void Forget(Guid driveId)
    {
        lock (_gate)
        {
            _pending.Remove(driveId);
        }
    }

    /// <summary>
    /// Pushes a refresh at the UI. The monitor raises on a thread-pool thread and these
    /// messages end in bound property writes, which WinUI rejects off the UI thread.
    /// </summary>
    private static void PublishToUi(IEnumerable<Guid> driveIds)
    {
        Guid[] ids = [.. driveIds];

        MainThread.BeginInvokeOnMainThread(() =>
        {
            foreach (Guid id in ids)
            {
                WeakReferenceMessenger.Default.Send(new NotifyDriveConnectivityMessage(id));
            }

            WeakReferenceMessenger.Default.Send(new CheckDrivesStatusMessage());
        });
    }

    /// <summary>
    /// Tells the row why an attempt did not mount the drive.
    /// </summary>
    /// <remarks>
    /// The row can already see that the drive is down; what it cannot see is whether the
    /// NAS refused it or was never there. Sent on the same thread rule as
    /// <see cref="PublishToUi"/> — this ends in a bound property write, and the retry loop
    /// runs on the thread pool.
    /// </remarks>
    private static void PublishFailureToUi(Guid driveId, Error error)
    {
        MainThread.BeginInvokeOnMainThread(() =>
            WeakReferenceMessenger.Default.Send(
                new DriveAttemptFailedMessage(driveId, error.Code, error.Description)));
    }

    /// <summary>
    /// Whether the machine is on a network at all.
    /// </summary>
    /// <remarks>
    /// Answers true if the platform will not say, because this gates the retry loop: a
    /// fault in here has to cost an attempt that was going to fail anyway, never the
    /// reconnecting that is the whole point of the loop.
    /// </remarks>
    private bool HasNetwork()
    {
        try
        {
            return Connectivity.Current.NetworkAccess != NetworkAccess.None;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read the network state; assuming there is one.");

            return true;
        }
    }

    private static async Task<bool> IsAutoConnectEnabledAsync()
    {
        Result<SettingsModel> result = await ScopedHandler.HandleAsync((GetSettings h) => h.Handle());

        return result.IsSuccess && result.Value.AutoConnect;
    }

    private void RegisterMessages()
    {
        // The watched set is only as good as the drive list it came from.
        WeakReferenceMessenger.Default.Register<DriveCreatedMessage>(this, (r, m) => RefreshWatchedDrivesSafely());
        WeakReferenceMessenger.Default.Register<DriveDeletedMessage>(this, (r, m) => RefreshWatchedDrivesSafely());
        WeakReferenceMessenger.Default.Register<DriveUpdatedMessage>(this, (r, m) => RefreshWatchedDrivesSafely());
    }

    /// <summary>
    /// Re-reads the watch set without letting a failure escape as an unhandled exception.
    /// </summary>
    /// <remarks>
    /// The messenger's handler delegate returns void, so an <c>async</c> lambda here is
    /// an async void: anything thrown after the first await is rethrown on the thread
    /// pool, out of reach of <c>TaskScheduler.UnobservedTaskException</c>, and takes the
    /// process down. A locked database is enough to trigger it.
    /// </remarks>
    private void RefreshWatchedDrivesSafely()
    {
        _ = RefreshWatchedDrivesAsync().ContinueWith(
            task => _logger.LogError(task.Exception, "The drive watchdog failed to refresh its watch set."),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private sealed record PendingReconnect(Guid DriveId, string Letter, int Failures, DateTime NextAttemptUtc);
}
