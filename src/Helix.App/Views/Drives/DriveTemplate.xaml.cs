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

        _nasConnector = App.ServiceProvider.GetRequiredService<INasConnector>();
        _fileBrowser = App.ServiceProvider.GetRequiredService<IFileBrowser>();
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();

        if (BindingContext is DriveDisplay drive)
        {
            RefreshStatus(drive);
            RegisterMessages();
        }
    }

    private void RefreshStatus(DriveDisplay drive)
    {
        drive.Connected = _nasConnector.IsConnected(drive.Letter);

        _ = RefreshStorageUsageAsync(drive);
    }

    private static async Task RefreshStorageUsageAsync(DriveDisplay drive)
    {
        string usage = await StorageUsageHelper.GetStorageUsageAsync(
            drive.Letter,
            AppResources.DriveNotReady,
            AppResources.InvalidDriveLetter,
            AppResources.StorageUsedOf);

        MainThread.BeginInvokeOnMainThread(() => drive.StorageUsage = usage);
    }

    private async void ToggleConnect(object? sender, TappedEventArgs e)
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
                    RefreshStatus(drive);
                }

                Notifier.Error($"{drive.Letter}: {result.Error.Description}");
                return;
            }

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

    private static void MarkOffline(DriveDisplay drive, Error error) => drive.MarkOffline(error);

    private void ToggleSelected(object? sender, TappedEventArgs e)
    {
        if (BindingContext is DriveDisplay drive)
        {
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

    private void HandleDiagnose(object? sender, TappedEventArgs e)
    {
        if (BindingContext is not DriveDisplay drive)
        {
            return;
        }

        WeakReferenceMessenger.Default.Send(new DiagnoseDriveMessage(true, drive));
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

    }
}
