using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.Drives;
using Helix.App.Resources.Languages;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Features.Drives.Commands;
using Helix.Domain.Drives;
using Microsoft.Extensions.Logging;

namespace Helix.App.Services;

internal sealed class MountReconciler
{
    private readonly INasConnector _nasConnector;
    private readonly ILogger<MountReconciler> _logger;

    private bool _subscribed;

    public MountReconciler(INasConnector nasConnector, ILogger<MountReconciler> logger)
    {
        _nasConnector = nasConnector;
        _logger = logger;
    }

    public void Start()
    {
        if (_subscribed)
        {
            return;
        }

        _nasConnector.MountSettledLate += OnMountSettledLate;

        _subscribed = true;
    }

    public void Stop()
    {
        if (!_subscribed)
        {
            return;
        }

        _nasConnector.MountSettledLate -= OnMountSettledLate;

        _subscribed = false;
    }

    private void OnMountSettledLate(object? sender, LateMountOutcome outcome)
    {
        if (!outcome.IsMounted)
        {
            return;
        }

        _ = ReconcileAsync(outcome);
    }

    private async Task ReconcileAsync(LateMountOutcome outcome)
    {
        try
        {
            Result<Drive> result = await ScopedHandler.HandleAsync(
                (MarkDriveConnected h) => h.Handle(new MarkDriveConnected.Request(outcome.Letter)));

            if (result.IsFailure)
            {
                _logger.LogWarning(
                    "Drive {Letter}: mounted after its timeout but could not be reconciled - {Reason}",
                    outcome.Letter,
                    result.Error.Description);

                return;
            }

            Notifier.Retract($"{outcome.Letter}: {DriveErrors.ConnectionTimedOut.Description}");

            Notifier.Success(string.Format(AppResources.DriveConnectedLate, outcome.Letter));

            MainThread.BeginInvokeOnMainThread(() =>
            {
                WeakReferenceMessenger.Default.Send(
                    new DriveAttemptSucceededMessage(result.Value.Id, result.Value.LastConnectedOnUtc ?? DateTime.UtcNow));

                WeakReferenceMessenger.Default.Send(new NotifyDriveConnectivityMessage(result.Value.Id));

                WeakReferenceMessenger.Default.Send(new CheckDrivesStatusMessage());
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reconciling the late mount of drive {Letter}: failed.", outcome.Letter);
        }
    }
}
