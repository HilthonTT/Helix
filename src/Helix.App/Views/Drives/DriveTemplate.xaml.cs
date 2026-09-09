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

    private async Task RefreshStorageUsageAsync(DriveDisplay drive)
    {
        string usage = await StorageUsageHelper.GetStorageUsageAsync(
            _nasConnector.GetMountPath(drive.Letter),
            AppResources.DriveNotReady,
            AppResources.InvalidDriveLetter,
            AppResources.StorageUsedOf);

        MainThread.BeginInvokeOnMainThread(() => drive.StorageUsage = usage);
    }

    /// <summary>
    /// The keys the row answers to. Every one of them is the same work the mouse reaches
    /// through a pill or a chip, so a row does the same thing either way.
    /// </summary>
    bool IRowKeys.OnRowKey(RowKey key)
    {
        if (BindingContext is not DriveDisplay drive)
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

            // The chip is not there when the drive is down, so neither is the shortcut:
            // swallowing the key would make Ctrl+O look broken rather than inapplicable.
            case RowKey.Open when drive.Connected:
                OpenFolder();
                return true;

            default:
                return false;
        }
    }

    private void ToggleConnect(object? sender, TappedEventArgs e) => ToggleConnect();

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

    private void ToggleSelected(object? sender, TappedEventArgs e) => ToggleSelected();

    private void ToggleSelected()
    {
        if (BindingContext is DriveDisplay drive)
        {
            drive.IsSelected = !drive.IsSelected;
        }
    }

    private void HandleOpen(object? sender, TappedEventArgs e) => OpenFolder();

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

    private void HandleDiagnose(object? sender, TappedEventArgs e) => Diagnose();

    private void Diagnose()
    {
        if (BindingContext is not DriveDisplay drive)
        {
            return;
        }

        WeakReferenceMessenger.Default.Send(new DiagnoseDriveMessage(true, drive));
    }

    private void HandleUpdate(object? sender, TappedEventArgs e) => Edit();

    private void Edit()
    {
        if (BindingContext is not DriveDisplay drive)
        {
            return;
        }

        WeakReferenceMessenger.Default.Send(new UpdateDriveMessage(true, drive.Id));
    }

    private void HandleDelete(object? sender, TappedEventArgs e) => Delete();

    private void Delete()
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
