using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.Drives;
using Helix.App.Models;
using Helix.App.Resources.Languages;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Desktop;
using Helix.Application.Features.Drives.Commands;
using Helix.Application.Features.Drives.Queries;
using Helix.Domain.Drives;
using Microsoft.Extensions.Logging;
using Helix.App.Services;

namespace Helix.App.Views.Drives;

public sealed partial class DriveTemplate : ContentView
{
    private readonly INasConnector _nasConnector;
    private readonly IFileBrowser _fileBrowser;

    public DriveTemplate()
    {
        InitializeComponent();

        // Both singletons, so holding them in fields is allowed where a scoped handler
        // would not be.
        _nasConnector = App.ServiceProvider.GetRequiredService<INasConnector>();
        _fileBrowser = App.ServiceProvider.GetRequiredService<IFileBrowser>();
    }

    protected override void OnBindingContextChanged()
    {
        // Without the base call the BindingContext is never propagated to Content,
        // leaving every {Binding} in the template unresolved.
        base.OnBindingContextChanged();

        if (BindingContext is DriveDisplay drive)
        {
            RefreshStatus(drive);
            RegisterMessages();
        }
    }

    /// <summary>
    /// Pushes live connectivity onto the bound model. The row's status pill, capacity
    /// line and enabled state are all data-bound, so refreshing the model is all the
    /// view needs — no imperative control updates.
    /// </summary>
    /// <remarks>
    /// Connectivity is a cheap logical-drive lookup and stays inline, but capacity does
    /// I/O against the share and can block for seconds on an unreachable NAS. Since the
    /// monitor now refreshes rows on a timer, that probe is pushed off the UI thread —
    /// leaving it inline would stall the app on every poll while a drive was down.
    /// </remarks>
    private void RefreshStatus(DriveDisplay drive)
    {
        drive.Connected = _nasConnector.IsConnected(drive.Letter);

        _ = RefreshStorageUsageAsync(drive);
    }

    private static async Task RefreshStorageUsageAsync(DriveDisplay drive)
    {
        string usage = await StorageUsageHelper.GetStorageUsageAsync(drive.Letter);

        // The row may have been rebound to another drive while the probe ran.
        MainThread.BeginInvokeOnMainThread(() => drive.StorageUsage = usage);
    }

    private async void ToggleConnect(object? sender, TappedEventArgs e)
    {
        // Event handler is `async void`: an escaping exception would tear down the
        // whole app. Guard it so connection problems always surface in the banner.
        try
        {
            await ToggleConnectInternalAsync();
        }
        catch (Exception ex)
        {
            AppLog.For<DriveTemplate>().LogWarning(ex, "Toggling the drive connection failed.");

            Notifier.Error(ex.Message);
        }
    }

    private async Task ToggleConnectInternalAsync()
    {
        if (BindingContext is not DriveDisplay drive || drive.IsBusy)
        {
            return;
        }

        object request = drive.Connected
            ? new DisconnectDrive.Request(drive.Id)
            : new ConnectDrive.Request(drive.Id);

        // Blocks a second click while the mount is in flight — the row binds its
        // IsEnabled to this.
        drive.IsBusy = true;

        try
        {
            Result result = await HandleDriveConnection(request);
            if (result.IsFailure)
            {
                // The row keeps the reason as well as reporting it: the banner is gone in
                // a few seconds and the pill is what is still there next time the user
                // looks at the page.
                if (request is ConnectDrive.Request)
                {
                    MarkOffline(drive, result.Error);
                }

                Notifier.Error(result.Error);
                return;
            }

            // Mirrors what ConnectDrive just persisted, so the row's "last connected"
            // line is right without refetching the drive to read it back.
            if (request is ConnectDrive.Request)
            {
                drive.LastConnectedOnUtc = DateTime.UtcNow;
            }

            RefreshStatus(drive);

            WeakReferenceMessenger.Default.Send(new CheckDrivesStatusMessage());
        }
        finally
        {
            drive.IsBusy = false;
        }
    }

    private async Task<Result> HandleDriveConnection(object request)
    {
        return request switch
        {
            ConnectDrive.Request connect =>
                await ScopedHandler.HandleAsync((ConnectDrive h) => h.Handle(connect)),
            DisconnectDrive.Request disconnect =>
                await ScopedHandler.HandleAsync((DisconnectDrive h) => h.Handle(disconnect)),
            _ => Result.Failure(Error.NullValue)
        };
    }

    /// <summary>
    /// Files why an attempt did not mount the drive, from whichever path made it.
    /// </summary>
    /// <remarks>
    /// The unreachable case gets the localized sentence rather than the error's own
    /// description, because it is the one the user sees daily and the one there is
    /// something to say about — the domain's descriptions are not translated. A refusal is
    /// reported in the share's own words, which is the whole value of it.
    /// </remarks>
    private static void MarkOffline(DriveDisplay drive, Error error)
    {
        if (error.Code == DriveErrors.HostUnreachableCode)
        {
            drive.MarkOffline(DriveOfflineReason.HostUnreachable, AppResources.StatusUnreachableHint);

            return;
        }

        drive.MarkOffline(DriveOfflineReason.Refused, error.Description);
    }

    private void ToggleSelected(object? sender, TappedEventArgs e)
    {
        if (BindingContext is DriveDisplay drive)
        {
            // The row is disabled while a mount is in flight, which takes the tick with
            // it, so there is no guard needed here beyond the cast.
            drive.IsSelected = !drive.IsSelected;
        }
    }

    private void HandleOpen(object? sender, TappedEventArgs e)
    {
        if (BindingContext is not DriveDisplay drive)
        {
            return;
        }

        Result result = _fileBrowser.Open(_nasConnector.GetMountPath(drive.Letter));
        if (result.IsFailure)
        {
            Notifier.Error(result.Error);
        }
    }

    private void HandleUpdate(object? sender, TappedEventArgs e)
    {
        if (BindingContext is not DriveDisplay drive)
        {
            return;
        }

        WeakReferenceMessenger.Default.Send(new UpdateDriveMessage(true, drive.Id));
    }

    private void HandleDelete(object? sender, TappedEventArgs e)
    {
        if (BindingContext is not DriveDisplay drive)
        {
            return;
        }

        WeakReferenceMessenger.Default.Send(new DeleteDriveMessage(true, drive));
    }

    private void RegisterMessages()
    {
        WeakReferenceMessenger.Default.Unregister<DriveUpdatedMessage>(this);

        WeakReferenceMessenger.Default.Register<DriveUpdatedMessage>(this, (r, m) =>
        {
            if (BindingContext is not DriveDisplay drive || drive.Id != m.UpdatedDrive.Id)
            {
                return;
            }

            // Mutate the bound DriveDisplay too — it lives in HomeViewModel.Drives,
            // and leaving it stale makes later connectivity refreshes revert the row
            // to the old letter/name.
            drive.Letter = m.UpdatedDrive.Letter;
            drive.Name = m.UpdatedDrive.Name;
            drive.Host = m.UpdatedDrive.Host;

            RefreshStatus(drive);
        });

        WeakReferenceMessenger.Default.Unregister<NotifyDriveConnectivityMessage>(this);

        WeakReferenceMessenger.Default.Register<NotifyDriveConnectivityMessage>(this, (r, m) =>
        {
            if (BindingContext is not DriveDisplay drive || drive.Id != m.DriveId)
            {
                return;
            }

            RefreshStatus(drive);
        });

        WeakReferenceMessenger.Default.Unregister<DriveAttemptFailedMessage>(this);

        // What the watchdog learned on the user's behalf while nobody was looking. Without
        // it the row can only say the drive is down, which is the same thing it says about
        // a drive the user disconnected themselves.
        WeakReferenceMessenger.Default.Register<DriveAttemptFailedMessage>(this, (r, m) =>
        {
            if (BindingContext is not DriveDisplay drive || drive.Id != m.DriveId)
            {
                return;
            }

            MarkOffline(drive, Error.Problem(m.ErrorCode, m.Description));
        });
    }
}
