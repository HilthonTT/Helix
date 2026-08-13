using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Helpers;
using Helix.App.Messages;
using Helix.App.Modals.Drives.Delete;
using Helix.App.Modals.Drives.Update;
using Helix.App.Models;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Drives;
using SharedKernel;

namespace Helix.App.Views.Drives;

public sealed partial class DriveTemplate : ContentView
{
    private readonly INasConnector _nasConnector;

    public DriveTemplate()
    {
        InitializeComponent();

        _nasConnector = App.ServiceProvider.GetRequiredService<INasConnector>();
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
    private void RefreshStatus(DriveDisplay drive)
    {
        drive.Connected = _nasConnector.IsConnected(drive.Letter);
        drive.StorageUsage = StorageUsageHelper.GetStorageUsage(drive.Letter);
    }

    private async void ToggleConnect(object? sender, TappedEventArgs e)
    {
        // Event handler is `async void`: an escaping exception would tear down the
        // whole app. Guard it so connection problems always surface as an alert.
        try
        {
            await ToggleConnectInternalAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAlert(ex.Message);
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
                await ShowErrorAlert(result.Error.Description);
                return;
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

    private static Task ShowErrorAlert(string message)
    {
        // Marshal to the UI thread and swallow failures from stacked alerts: on WinUI
        // a DisplayAlert raised while another is showing throws, which would otherwise
        // bubble out of an async void handler and crash the app.
        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                Page? page = Shell.Current;
                if (page is not null)
                {
                    await page.DisplayAlertAsync("Something went wrong!", message, "Ok");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Helix: failed to show error alert: {ex}");
            }
        });
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
    }
}
