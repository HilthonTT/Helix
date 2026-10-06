using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.Drives;
using Helix.App.Resources.Languages;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Features.Schedules.Commands;
using Helix.Application.Features.Schedules.Contracts;
using Microsoft.Extensions.Logging;

namespace Helix.App.Services;

internal sealed class ScheduleService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(20);

    private readonly IDriveMonitor _monitor;
    private readonly TrayIconService _tray;
    private readonly ILogger<ScheduleService> _logger;

    private CancellationTokenSource? _cancellation;

    public ScheduleService(IDriveMonitor monitor, TrayIconService tray, ILogger<ScheduleService> logger)
    {
        _monitor = monitor;
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

        CancellationToken token = _cancellation.Token;

        _ = Task.Run(() => RunAsync(token));
    }

    public void Stop()
    {
        CancellationTokenSource? cancellation = _cancellation;
        _cancellation = null;

        if (cancellation is null)
        {
            return;
        }

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

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(CheckInterval);

            do
            {
                await CheckGuardedAsync(cancellationToken);
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task CheckGuardedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await CheckAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A schedule check faulted; the next check will run as usual.");
        }
    }

    private async Task CheckAsync(CancellationToken cancellationToken)
    {
        Result<List<ScheduleRun>> result = await ScopedHandler.HandleAsync(
            (RunDueSchedules h) => h.Handle(cancellationToken));

        if (result.IsFailure)
        {
            _logger.LogDebug("The schedule check could not run: {Reason}", result.Error.Description);

            return;
        }

        if (result.Value.Count == 0)
        {
            return;
        }

        await _monitor.PollAsync();

        List<Guid> touched = [.. result.Value.SelectMany(run => run.DriveIds).Distinct()];

        MainThread.BeginInvokeOnMainThread(() =>
        {
            WeakReferenceMessenger.Default.Send(new CheckDrivesStatusMessage());

            foreach (Guid driveId in touched)
            {
                WeakReferenceMessenger.Default.Send(new NotifyDriveConnectivityMessage(driveId));
            }
        });

        await _tray.RefreshAsync();

        foreach (ScheduleRun run in result.Value)
        {
            Report(run);
        }
    }

    private void Report(ScheduleRun run)
    {
        if (run.Outcome.IsSuccess)
        {
            _logger.LogInformation(
                "A schedule ran: {Action} on {Count} drive(s).",
                run.Action,
                run.DriveIds.Count);

            Tell(ScheduleText.Ran(run.Action, run.GroupName), failed: false);

            return;
        }

        _logger.LogWarning(
            "A schedule did not fully succeed: {Action} — {Reason}",
            run.Action,
            run.Outcome.Error.Description);

        string title = string.Format(AppResources.ScheduleRunFailed, ScheduleText.Title(run.Action, run.GroupName));

        Tell($"{title}{Environment.NewLine}{run.Outcome.Error.Description}", failed: true);
    }

    private void Tell(string message, bool failed)
    {
        if (_tray.IsRunning)
        {
            _tray.Notify(AppResources.ScheduleNotification, message);
            return;
        }

        if (failed)
        {
            Notifier.Error(message);
        }
        else
        {
            Notifier.Success(message);
        }
    }
}
