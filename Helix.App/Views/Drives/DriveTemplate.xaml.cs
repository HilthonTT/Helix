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
    private readonly ConnectDrive _connectDrive;
    private readonly DisconnectDrive _disconnectDrive;

    public DriveTemplate()
    {
        InitializeComponent();

        _nasConnector = App.ServiceProvider.GetRequiredService<INasConnector>();
        _connectDrive = App.ServiceProvider.GetRequiredService<ConnectDrive>();
        _disconnectDrive = App.ServiceProvider.GetRequiredService<DisconnectDrive>();
    }

    protected override void OnBindingContextChanged()
    {
        if (BindingContext is DriveDisplay drive)
        {
            UpdateDriveDetails(drive);
            UpdateStatusButtonColor(drive.Letter);
        }
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
        if (BindingContext is not DriveDisplay drive)
        {
            return;
        }

        if (drive.IsBusy)
        {
            return;
        }

        try
        {
            drive.IsBusy = true;

            object request = _nasConnector.IsConnected(drive.Letter)
                ? new DisconnectDrive.Request(drive.Letter)
                : new ConnectDrive.Request(drive.Letter);

            Result result = await HandleDriveConnection(request);
            if (result.IsSuccess)
            {
                UpdateStatusButtonColor(drive.Letter);
            }
            else
            {
                await ShowErrorAlert(result.Error.Description);
            }
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
            ConnectDrive.Request connect => await _connectDrive.Handle(connect),
            DisconnectDrive.Request disconnect => await _disconnectDrive.Handle(disconnect),
            _ => Result.Failure(Error.NullValue)
        };
    }

    private static Task ShowErrorAlert(string message)
    {
        return Shell.Current.DisplayAlert("Something went wrong!", message, "Ok");
    }

    private void HandleUpdate(object? sender, TappedEventArgs e)
    {
        // Handle update logic here
    }

    private void HandleDelete(object? sender, TappedEventArgs e)
    {
        // Handle delete logic here
    }
}
