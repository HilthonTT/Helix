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
    private static readonly Color ConnectedColor = Color.FromArgb("#00FF00");
    private static readonly Color DisconnectedColor = Color.FromArgb("#FF0000");

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
            UpdateDriveDetails(drive);
            UpdateStorageUsage(drive);
            UpdateStatusButtonColor(drive.Letter);

            RegisterMessages();
        }
    }

    private void UpdateStorageUsage(DriveDisplay drive)
    {
        StorageUsage.Text = StorageUsageHelper.GetStorageUsage(drive.Letter);
    }

    private void UpdateDriveDetails(DriveDisplay drive)
    {
        Name.Text = drive.Name;
        Letter.Text = drive.Letter;
    }

    private void UpdateStatusButtonColor(string driveLetter)
    {
        StatusButton.BackgroundColor = _nasConnector.IsConnected(driveLetter)
            ? ConnectedColor
            : DisconnectedColor;
    }

    private async void ToggleConnect(object? sender, EventArgs e)
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

    private void CheckConnectivityAndUpdateUI()
    {
        if (BindingContext is DriveDisplay drive)
        {
            UpdateDriveDetails(drive);
            UpdateStorageUsage(drive);
            UpdateStatusButtonColor(drive.Letter);

            OnPropertyChanged();
        }
    }

    private async Task ToggleConnectInternalAsync()
    {
        if (BindingContext is not DriveDisplay drive)
        {
            return;
        }

        object request = _nasConnector.IsConnected(drive.Letter)
            ? new DisconnectDrive.Request(drive.Id)
            : new ConnectDrive.Request(drive.Id);

        Result result = await HandleDriveConnection(request);
        if (result.IsSuccess)
        {
            UpdateStatusButtonColor(drive.Letter);
            UpdateStorageUsage(drive);

            WeakReferenceMessenger.Default.Send(new CheckDrivesStatusMessage());
        }
        else
        {
            await ShowErrorAlert(result.Error.Description);
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
                    await page.DisplayAlert("Something went wrong!", message, "Ok");
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
            // and leaving it stale makes later connectivity refreshes revert the tile
            // to the old letter/name.
            drive.Letter = m.UpdatedDrive.Letter;
            drive.Name = m.UpdatedDrive.Name;

            UpdateDriveDetails(drive);
            UpdateStorageUsage(drive);
            UpdateStatusButtonColor(drive.Letter);

            OnPropertyChanged();
        });

        WeakReferenceMessenger.Default.Unregister<ToggleConnectDriveMessage>(this);

        WeakReferenceMessenger.Default.Register<ToggleConnectDriveMessage>(this, async (r, m) =>
        {
            if (BindingContext is not DriveDisplay drive || drive.Id != m.DriveId)
            {
                return;
            }

            // `async void` messenger callback — never let an exception escape.
            try
            {
                await ToggleConnectInternalAsync();
            }
            catch (Exception ex)
            {
                await ShowErrorAlert(ex.Message);
            }
        });

        WeakReferenceMessenger.Default.Unregister<NotifyDriveConnectivityMessage>(this);

        WeakReferenceMessenger.Default.Register<NotifyDriveConnectivityMessage>(this, (r, m) =>
        {
            if (BindingContext is not DriveDisplay drive || drive.Id != m.DriveId)
            {
                return;
            }

            CheckConnectivityAndUpdateUI();
        });
    }
}
