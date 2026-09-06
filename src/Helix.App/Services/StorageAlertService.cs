using Helix.App.Resources.Languages;
using Helix.Application.Features.Storage.Contracts;
using Helix.Application.Features.Storage.Queries;
using Microsoft.Extensions.Logging;

namespace Helix.App.Services;

/// <summary>
/// Watches how much room is left on the NAS and says something before it runs out.
/// </summary>
/// <remarks>
/// The third of the background services, beside <see cref="DriveWatchdog"/> and
/// <see cref="TrayIconService"/>, and here for the same reason both of those are: it
/// needs a DI scope per check and a use case to call, neither of which a singleton in
/// Infrastructure may hold.
///
/// A pool fills up over weeks, so this is not something the dashboard can be relied on to
/// report — the dashboard only says anything while somebody is looking at it, and nobody
/// is, which is rather the point of an app that lives in the tray. The check runs on its
/// own slow timer instead.
/// </remarks>
internal sealed class StorageAlertService
{
    /// <summary>
    /// How often the volumes are measured.
    /// </summary>
    /// <remarks>
    /// Deliberately nothing like the watchdog's twenty seconds. Measuring a volume means
    /// a blocking call against a network share, and free space is a figure that moves
    /// over days — a quarter of an hour is far more often than it needs to be asked, and
    /// still catches a pool filling up long before anything breaks.
    /// </remarks>
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How long after starting the first check runs.
    /// </summary>
    /// <remarks>
    /// Not immediately: the dashboard's own first pass is connecting drives at that
    /// moment, and a share measured before it is mounted is a share left out.
    /// </remarks>
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(1);

    private readonly TrayIconService _tray;
    private readonly ILogger<StorageAlertService> _logger;
    private readonly Lock _gate = new();

    /// <summary>
    /// Volumes already warned about, so a pool that stays full is reported once rather
    /// than every quarter of an hour until somebody deletes something.
    /// </summary>
    private readonly HashSet<string> _warned = [];

    private CancellationTokenSource? _cancellation;

    /// <summary>
    /// Bumped by <see cref="Stop"/>. A check blocked on a share probe when the user
    /// signed out compares this against what it captured, so its result does not land
    /// in the next user's set of already-warned volumes.
    /// </summary>
    private int _generation;

    public StorageAlertService(TrayIconService tray, ILogger<StorageAlertService> logger)
    {
        _tray = tray;
        _logger = logger;
    }

    /// <summary>
    /// Begins checking. Safe to call on every dashboard appearance, like the two services
    /// it sits beside.
    /// </summary>
    public void Start()
    {
        if (_cancellation is not null)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();

        _ = RunAsync(_cancellation.Token);
    }

    /// <summary>
    /// Stops checking. Called on sign-out: the threshold and the drives behind it belong
    /// to one user.
    /// </summary>
    public void Stop()
    {
        CancellationTokenSource? cancellation = _cancellation;
        _cancellation = null;

        if (cancellation is not null)
        {
            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already torn down by a concurrent Stop.
            }
            finally
            {
                cancellation.Dispose();
            }
        }

        lock (_gate)
        {
            _generation++;

            // The next user is told about their own full pools from scratch.
            _warned.Clear();
        }
    }

    /// <summary>
    /// Measures now and reports anything below the threshold. Public so a check can be
    /// forced rather than waited for.
    /// </summary>
    public async Task CheckAsync()
    {
        int generation;

        lock (_gate)
        {
            generation = _generation;
        }

        Result<StorageAlertReport> result = await ScopedHandler.HandleAsync((GetStorageAlerts h) => h.Handle());
        if (result.IsFailure)
        {
            _logger.LogDebug("The storage check could not run: {Reason}", result.Error.Description);

            return;
        }

        IReadOnlyList<StorageAlert> alerts = result.Value.Alerts;
        IReadOnlySet<string> measured = result.Value.MeasuredVolumeIds;

        List<StorageAlert> fresh = [];

        lock (_gate)
        {
            if (generation != _generation)
            {
                // Stop() ran while the probe was blocking; the reading belongs to a
                // session that is over.
                return;
            }

            HashSet<string> current = [.. alerts.Select(alert => alert.VolumeId)];

            // A volume that is no longer short of space is forgotten, so that if it fills
            // up again months later the user hears about it again. Only one that was
            // measured and found fine, though: a pool whose drives were unmounted for one
            // check, or whose probe ran past the timeout, was forgotten too and warned
            // about again a quarter of an hour later.
            _warned.RemoveWhere(volumeId => measured.Contains(volumeId) && !current.Contains(volumeId));

            foreach (StorageAlert alert in alerts)
            {
                if (_warned.Add(alert.VolumeId))
                {
                    fresh.Add(alert);
                }
            }
        }

        foreach (StorageAlert alert in fresh)
        {
            string names = string.Join(", ", alert.DriveNames);

            // Named at Warning: this is the log line that explains a backup that started
            // failing, and it is worth having in the file the user sends on.
            _logger.LogWarning(
                "A volume is low on space: {Drives} — {FreePercent}% free.",
                names,
                alert.FreePercent);

            _tray.Notify(
                AppResources.TrayStorageLow,
                string.Format(AppResources.TrayStorageLowMessage, names, alert.FreePercent));
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(FirstCheckDelay, cancellationToken);

            await CheckAsync();

            using var timer = new PeriodicTimer(CheckInterval);

            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await CheckAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Stop() was called.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The storage check faulted; low-space warnings are off for this session.");
        }
    }
}
