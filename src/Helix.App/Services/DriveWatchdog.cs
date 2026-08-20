using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.Drives;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Features.Drives.Commands;
using Helix.Application.Features.Drives.Queries;
using Helix.Application.Features.Settings.Commands;
using Helix.Application.Features.Settings.Queries;
using Helix.Domain.Drives;
using System.Diagnostics;
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

    private readonly IDriveMonitor _monitor;
    private readonly INasConnector _nasConnector;
    private readonly Lock _gate = new();

    /// <summary>Drives awaiting another reconnect attempt, keyed by drive id.</summary>
    private readonly Dictionary<Guid, PendingReconnect> _pending = [];

    /// <summary>Drives with a reconnect in flight, so the two paths cannot overlap.</summary>
    private readonly HashSet<Guid> _inFlight = [];

    private CancellationTokenSource? _retryCancellation;
    private bool _subscribed;

    public DriveWatchdog(IDriveMonitor monitor, INasConnector nasConnector)
    {
        _monitor = monitor;
        _nasConnector = nasConnector;
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

                await AttemptAsync(change.DriveId, change.Letter, reconnect, recordDrop: true);
            }
        }
        catch (Exception ex)
        {
            // Fire-and-forget from an event handler: never let this reach the app.
            Debug.WriteLine($"Helix: drive watchdog failed to handle a change: {ex}");
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
            Debug.WriteLine($"Helix: drive watchdog retry loop faulted: {ex}");
        }
    }

    private async Task RetryDueAsync()
    {
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
        lock (_gate)
        {
            if (!_inFlight.Add(driveId))
            {
                return;
            }
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

            if (result.IsSuccess)
            {
                Forget(driveId);

                // Re-poll so the monitor's baseline and the UI both catch up now rather
                // than at the end of the interval.
                await _monitor.PollAsync();

                PublishToUi([driveId]);

                return;
            }

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

    private static async Task<bool> IsAutoConnectEnabledAsync()
    {
        Result<SettingsModel> result = await ScopedHandler.HandleAsync((GetSettings h) => h.Handle());

        return result.IsSuccess && result.Value.AutoConnect;
    }

    private void RegisterMessages()
    {
        // The watched set is only as good as the drive list it came from.
        WeakReferenceMessenger.Default.Register<DriveCreatedMessage>(
            this, async (r, m) => await RefreshWatchedDrivesAsync());

        WeakReferenceMessenger.Default.Register<DriveDeletedMessage>(
            this, async (r, m) => await RefreshWatchedDrivesAsync());

        WeakReferenceMessenger.Default.Register<DriveUpdatedMessage>(
            this, async (r, m) => await RefreshWatchedDrivesAsync());
    }

    private sealed record PendingReconnect(Guid DriveId, string Letter, int Failures, DateTime NextAttemptUtc);
}
