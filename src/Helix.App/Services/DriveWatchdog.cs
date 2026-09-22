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

    private static readonly TimeSpan ResumeGap = TimeSpan.FromSeconds(60);

    private readonly IDriveMonitor _monitor;
    private readonly INasConnector _nasConnector;
    private readonly INetworkLocation _networkLocation;
    private readonly ILogger<DriveWatchdog> _logger;
    private readonly Lock _gate = new();

    private readonly Dictionary<Guid, PendingReconnect> _pending = [];

    private readonly HashSet<Guid> _inFlight = [];

    private CancellationTokenSource? _retryCancellation;

    private int _generation;
    private bool _subscribed;

    private bool _networkSeen;
    private string? _networkId;

    private bool _watchingConnectivity;
    private DateTime _lastTickUtc;
    private int _nudging;
    private int _offline;

    public DriveWatchdog(
        IDriveMonitor monitor,
        INasConnector nasConnector,
        INetworkLocation networkLocation,
        ILogger<DriveWatchdog> logger)
    {
        _monitor = monitor;
        _nasConnector = nasConnector;
        _networkLocation = networkLocation;
        _logger = logger;
    }

    public async Task StartAsync()
    {
        if (!_subscribed)
        {
            _monitor.ConnectivityChanged += OnConnectivityChanged;
            _monitor.TakenDown += OnTakenDown;
            RegisterMessages();

            _subscribed = true;
        }

        WatchConnectivity();

        _lastTickUtc = DateTime.UtcNow;

        await RefreshWatchedDrivesAsync();

        _monitor.Start(PollInterval);

        StartRetryLoop();
    }

    public void Stop()
    {
        UnwatchConnectivity();

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

        _networkSeen = false;
        _networkId = null;
        _lastTickUtc = default;
    }

    private void WatchConnectivity()
    {
        if (_watchingConnectivity)
        {
            return;
        }

        try
        {
            _offline = Connectivity.Current.NetworkAccess == NetworkAccess.None ? 1 : 0;

            Connectivity.Current.ConnectivityChanged += OnNetworkAccessChanged;

            _watchingConnectivity = true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not watch for network changes; the retry sweep is the only trigger.");
        }
    }

    private void UnwatchConnectivity()
    {
        if (!_watchingConnectivity)
        {
            return;
        }

        _watchingConnectivity = false;

        try
        {
            Connectivity.Current.ConnectivityChanged -= OnNetworkAccessChanged;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not stop watching for network changes.");
        }
    }

    private void OnNetworkAccessChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        bool wasOffline = Interlocked.Exchange(ref _offline, e.NetworkAccess == NetworkAccess.None ? 1 : 0) == 1;

        if (e.NetworkAccess == NetworkAccess.None || !wasOffline)
        {
            return;
        }

        _ = NudgeAsync("the network came back");
    }

    private async Task NudgeAsync(string reason)
    {
        if (Interlocked.Exchange(ref _nudging, 1) == 1)
        {
            return;
        }

        try
        {
            if (_retryCancellation is null)
            {
                return;
            }

            _logger.LogInformation("Rechecking every drive because {Reason}.", reason);

            await _monitor.PollAsync();

            BringRetriesForward();

            NetworkLocation? here = _networkLocation.IsSupported
                ? await _networkLocation.GetCurrentAsync()
                : null;

            await ConnectDownDrivesAsync(here, pinnedOnly: false, reason);

            await RetryDueAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The drive watchdog failed to recheck the drives after {Reason}.", reason);
        }
        finally
        {
            Interlocked.Exchange(ref _nudging, 0);
        }
    }

    private void BringRetriesForward()
    {
        lock (_gate)
        {
            DateTime now = DateTime.UtcNow;

            foreach (PendingReconnect pending in _pending.Values.ToList())
            {
                _pending[pending.DriveId] = pending with { NextAttemptUtc = now };
            }
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

    private void OnTakenDown(object? sender, IReadOnlyList<string> letters)
    {
        lock (_gate)
        {
            foreach (PendingReconnect pending in _pending.Values
                .Where(p => letters.Contains(p.Letter, StringComparer.OrdinalIgnoreCase))
                .ToList())
            {
                _pending.Remove(pending.DriveId);
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
                    if (SleptThroughATick())
                    {
                        await NudgeAsync("the machine came back from sleep");

                        continue;
                    }

                    await CheckNetworkArrivalAsync();

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
                finally
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        _lastTickUtc = DateTime.UtcNow;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private bool SleptThroughATick()
    {
        DateTime now = DateTime.UtcNow;
        DateTime previous = _lastTickUtc;

        _lastTickUtc = now;

        return previous != default && now - previous > ResumeGap;
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

    private async Task CheckNetworkArrivalAsync()
    {
        if (!_networkLocation.IsSupported)
        {
            return;
        }

        NetworkLocation? here = await _networkLocation.GetCurrentAsync();

        bool firstReading = !_networkSeen;

        _networkSeen = true;

        if (here is null)
        {
            return;
        }

        bool moved = !string.Equals(here.Id, _networkId, StringComparison.OrdinalIgnoreCase);

        _networkId = here.Id;

        if (firstReading || !moved)
        {
            return;
        }

        await ConnectDownDrivesAsync(here, pinnedOnly: true, "it arrived on a drive's home network");
    }

    private async Task ConnectDownDrivesAsync(NetworkLocation? here, bool pinnedOnly, string reason)
    {
        if (!await IsAutoConnectEnabledAsync())
        {
            return;
        }

        Result<List<Drive>> drives = await ScopedHandler.HandleAsync((GetDrives h) => h.Handle());
        if (drives.IsFailure)
        {
            return;
        }

        Guid[] arriving = [.. drives.Value
            .Where(drive => drive.AutoConnect &&
                            (!pinnedOnly || drive.HomeNetworkId is not null) &&
                            !drive.IsAwayFrom(here?.Id) &&
                            !_nasConnector.IsMountedFrom(drive))
            .Select(drive => drive.Id)];

        if (arriving.Length == 0)
        {
            return;
        }

        _logger.LogInformation(
            "Connecting the {Count} drive(s) that are down because {Reason}.",
            arriving.Length,
            reason);

        Result result = await ScopedHandler.HandleAsync(
            (ConnectDrives h) => h.Handle(new ConnectDrives.Request(arriving)));

        if (result.IsFailure)
        {
            _logger.LogWarning(
                "Some drives did not connect after {Reason} — {Error}",
                reason,
                result.Error.Description);
        }

        foreach (Guid driveId in arriving)
        {
            Forget(driveId);
        }

        await _monitor.PollAsync();

        PublishToUi(arriving);
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

            if (result.Error.Code is DriveErrors.HostUnreachableCode or DriveErrors.AwayFromHomeNetworkCode)
            {
                if (result.Error.Code == DriveErrors.HostUnreachableCode)
                {
                    _logger.LogDebug("Drive {Letter}: is waiting for its NAS to be reachable again.", letter);
                }
                else
                {
                    _logger.LogDebug("Drive {Letter}: is waiting to be back on its home network.", letter);
                }

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
        _ = Task.Run(RefreshWatchedDrivesAsync).ContinueWith(
            task => _logger.LogError(task.Exception, "The drive watchdog failed to refresh its watch set."),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private sealed record PendingReconnect(Guid DriveId, string Letter, int Failures, DateTime NextAttemptUtc);
}
