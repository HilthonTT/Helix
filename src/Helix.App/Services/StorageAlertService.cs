using Helix.App.Resources.Languages;
using Helix.Application.Features.Storage.Contracts;
using Helix.Application.Features.Storage.Queries;
using Microsoft.Extensions.Logging;

namespace Helix.App.Services;

internal sealed class StorageAlertService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(15);

    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(1);

    private readonly TrayIconService _tray;
    private readonly ILogger<StorageAlertService> _logger;
    private readonly Lock _gate = new();

    private readonly HashSet<string> _warned = [];

    private CancellationTokenSource? _cancellation;

    private int _generation;

    public StorageAlertService(TrayIconService tray, ILogger<StorageAlertService> logger)
    {
        _tray = tray;
        _logger = logger;
    }

    public void Start()
    {
        if (_cancellation is not null)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();

        _ = RunAsync(_cancellation.Token);
    }

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
            }
            finally
            {
                cancellation.Dispose();
            }
        }

        lock (_gate)
        {
            _generation++;

            _warned.Clear();
        }
    }

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
                return;
            }

            HashSet<string> current = [.. alerts.Select(alert => alert.VolumeId)];

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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The storage check faulted; low-space warnings are off for this session.");
        }
    }
}
