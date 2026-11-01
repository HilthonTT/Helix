using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.Drives;
using Helix.App.Models;
using Helix.App.Resources.Languages;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Desktop;
using Helix.Application.Features.Drives.Commands;
using Microsoft.Extensions.Logging;
using Helix.App.Services;
using Helix.App.Behaviors;

namespace Helix.App.Views.Drives;

public sealed partial class DriveTemplate : ContentView, IRowKeys
{
    private readonly INasConnector _nasConnector;
    private readonly IFileBrowser _fileBrowser;

    public DriveTemplate()
    {
        InitializeComponent();

        _nasConnector = App.ServiceProvider.GetRequiredService<INasConnector>();
        _fileBrowser = App.ServiceProvider.GetRequiredService<IFileBrowser>();
    }

    bool IRowKeys.OnRowKey(RowKey key)
    {
        if (BindingContext is not DriveDisplay drive || ModalHost.IsAnyOpen)
        {
            return false;
        }

        switch (key)
        {
            case RowKey.Activate:
                ToggleConnect();
                return true;

            case RowKey.Select:
                ToggleSelected();
                return true;

            case RowKey.Delete:
                Delete();
                return true;

            case RowKey.Edit:
                Edit();
                return true;

            case RowKey.Diagnose:
                Diagnose();
                return true;

            case RowKey.Duplicate:
                Duplicate();
                return true;

            case RowKey.Wake when drive.CanWake:
                Wake();
                return true;

            case RowKey.Open when drive.Connected:
                OpenFolder();
                return true;

            default:
                return false;
        }
    }

    private void ToggleConnect(object? sender, EventArgs e) => ToggleConnect();

    private async void ToggleConnect()
    {
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

        if (drive.Connected && !await DisconnectConfirmation.ConfirmAsync([drive.Letter], onlyWhenInUse: true))
        {
            return;
        }

        object request = drive.Connected
            ? new DisconnectDrive.Request(drive.Id)
            : new ConnectDrive.Request(drive.Id);

        drive.IsBusy = true;

        try
        {
            Result result = await HandleDriveConnection(request);
            if (result.IsFailure)
            {
                if (request is ConnectDrive.Request)
                {
                    MarkOffline(drive, result.Error);
                    WeakReferenceMessenger.Default.Send(new NotifyDriveConnectivityMessage(drive.Id));
                }

                Notifier.Error($"{drive.Letter}: {result.Error.Description}");
                return;
            }

            if (request is ConnectDrive.Request)
            {
                drive.LastConnectedOnUtc = DateTime.UtcNow;
            }

            drive.Connected = request is ConnectDrive.Request;

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

    private static void MarkOffline(DriveDisplay drive, Error error) => drive.MarkOffline(error);

    private void ToggleSelected(object? sender, TappedEventArgs e) => ToggleSelected();

    private void ToggleSelected()
    {
        if (BindingContext is DriveDisplay drive)
        {
            drive.IsSelected = !drive.IsSelected;
        }
    }

    private void HandleOpen(object? sender, EventArgs e) => OpenFolder();

    private void OpenFolder()
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

    private void HandleDiagnose(object? sender, EventArgs e) => Diagnose();

    private void Diagnose()
    {
        if (BindingContext is not DriveDisplay drive)
        {
            return;
        }

        WeakReferenceMessenger.Default.Send(new DiagnoseDriveMessage(true, drive));
    }

    private void HandleWake(object? sender, EventArgs e) => Wake();

    private async void Wake()
    {
        if (BindingContext is not DriveDisplay drive || !drive.CanWake)
        {
            return;
        }

        try
        {
            Result result = await ScopedHandler.HandleAsync(
                (WakeDrive h) => h.Handle(new WakeDrive.Request(drive.Id)));

            if (result.IsFailure)
            {
                Notifier.Error($"{drive.Letter}: {result.Error.Description}");
                return;
            }

            Notifier.Success(string.Format(AppResources.WakeSent, drive.Name));
        }
        catch (Exception ex)
        {
            AppLog.For<DriveTemplate>().LogWarning(ex, "Waking drive {Letter}: failed.", drive.Letter);
        }
    }

    private void HandleUpdate(object? sender, EventArgs e) => Edit();

    private void Edit()
    {
        if (BindingContext is not DriveDisplay drive)
        {
            return;
        }

        WeakReferenceMessenger.Default.Send(new UpdateDriveMessage(true, drive.Id));
    }

    private void HandleDuplicate(object? sender, EventArgs e) => Duplicate();

    private void Duplicate()
    {
        if (BindingContext is not DriveDisplay drive)
        {
            return;
        }

        WeakReferenceMessenger.Default.Send(new CreateDriveMessage(true, drive.Id));
    }

    private void HandleDelete(object? sender, EventArgs e) => Delete();

    private void Delete()
    {
        if (BindingContext is not DriveDisplay drive)
        {
            return;
        }

        WeakReferenceMessenger.Default.Send(new DeleteDriveMessage(true, drive));
    }
}
