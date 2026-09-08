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

internal sealed class DriveWatchdog
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan RetrySweepInterval = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan OfflineRetryInterval = TimeSpan.FromSeconds(30);

    private readonly IDriveMonitor _monitor;
    private readonly INasConnector _nasConnector;
    private readonly ILogger<DriveWatchdog> _logger;
    private readonly Lock _gate = new();

    private readonly Dictionary<Guid, PendingReconnect> _pending = [];

    private readonly HashSet<Guid> _inFlight = [];

    private CancellationTokenSource? _retryCancellation;

    private int _generation;
    private bool _subscribed;

    public DriveWatchdog(IDriveMonitor monitor, INasConnector nasConnector, ILogger<DriveWatchdog> logger)
    {
        _monitor = monitor;
        _nasConnector = nasConnector;
        _logger = logger;
    }

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

    public async Task RefreshWatchedDrivesAsync()
    {
        Result<List<Drive>> result = await ScopedHandler.HandleAsync((GetDrives h) => h.Handle());
        if (result.IsFailure)
        {
            return;
        }

        _monitor.Watch([.. result.Value.Select(drive => new WatchedDrive(drive.Id, drive.Letter, drive.AutoConnect))]);

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
                    Forget(change.DriveId);
                    continue;
                }

                bool reconnect = autoConnectEnabled && change.AutoConnect;

                attempts.Add(AttemptAsync(change.DriveId, change.Letter, reconnect, recordDrop: true));
            }

            await Task.WhenAll(attempts);
        }
        catch (Exception ex)
        {
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
                try
                {
                    await RetryDueAsync();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "A drive watchdog retry sweep faulted; the next sweep will run as scheduled.");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RetryDueAsync()
    {
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
            lock (_gate)
            {
                _pending.Clear();
            }

            return;
        }

        List<Task> attempts = [];

        foreach (PendingReconnect pending in due)
        {
            if (_nasConnector.IsConnected(pending.Letter))
            {
                Forget(pending.DriveId);
                continue;
            }

            attempts.Add(AttemptAsync(pending.DriveId, pending.Letter, reconnect: true, recordDrop: false));
        }

        await Task.WhenAll(attempts);
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
                return;
            }

            if (IsStale(generation))
            {
                return;
            }

            if (result.IsFailure && result.Error.Code == DriveErrors.NotFound(driveId).Code)
            {
                Forget(driveId);

                return;
            }

            if (result.IsSuccess)
            {
                _logger.LogInformation("Reconnected drive {Letter}: automatically.", letter);

                Forget(driveId);

                await _monitor.PollAsync();

                PublishToUi([driveId]);

                return;
            }

            if (result.Error.Code == DriveErrors.HostUnreachableCode)
            {
                _logger.LogDebug("Drive {Letter}: is waiting for its NAS to be reachable again.", letter);

                PublishFailureToUi(driveId, result.Error);

                ScheduleOfflineRetry(driveId, letter);

                return;
            }

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

            double seconds = FirstRetryDelay.TotalSeconds * Math.Pow(3, failures - 1);
            TimeSpan delay = TimeSpan.FromSeconds(Math.Min(seconds, MaximumRetryDelay.TotalSeconds));

            _pending[driveId] = new PendingReconnect(driveId, letter, failures, DateTime.UtcNow + delay);
        }
    }

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

    private static void PublishFailureToUi(Guid driveId, Error error)
    {
        MainThread.BeginInvokeOnMainThread(() =>
            WeakReferenceMessenger.Default.Send(
                new DriveAttemptFailedMessage(driveId, error.Code, error.Description)));
    }

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
        WeakReferenceMessenger.Default.Register<DriveCreatedMessage>(this, (r, m) => RefreshWatchedDrivesSafely());
        WeakReferenceMessenger.Default.Register<DriveDeletedMessage>(this, (r, m) => RefreshWatchedDrivesSafely());
        WeakReferenceMessenger.Default.Register<DriveUpdatedMessage>(this, (r, m) => RefreshWatchedDrivesSafely());
    }

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
