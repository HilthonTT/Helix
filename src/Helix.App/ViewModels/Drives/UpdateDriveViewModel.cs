using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.Drives;
using Helix.App.Models;
using Helix.App.Resources.Languages;
using Helix.App.ViewModels;
using Helix.Application.Features.Drives.Commands;
using Helix.Application.Features.Drives.Queries;
using Helix.Domain.Drives;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace Helix.App.ViewModels.Drives;

internal sealed partial class UpdateDriveViewModel : BaseViewModel
{
    public UpdateDriveViewModel()
    {
        Drive = new();
        HideSecrets = true;
        AvailableLetters = [];

        RegisterMessages();
    }

    [ObservableProperty]
    public partial UpdateDriveModel Drive { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<string> AvailableLetters { get; set; }

    [ObservableProperty]
    public partial bool HideSecrets { get; set; }

    public bool SupportsPersistentMappings => DrivePlatform.SupportsPersistentMappings;

    public bool SupportsHostnameConnect => DrivePlatform.SupportsHostnameConnect;

    [RelayCommand]
    private async Task UpdateAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;

            var request = new UpdateDrive.Request(
                Drive.Id,
                Drive.Letter,
                Drive.Host,
                Drive.Name,
                Drive.Username,
                Drive.Password,
                Drive.AutoConnect,
                Drive.Persistent,
                Drive.ConnectByHostname);

            Result result = await ScopedHandler.HandleAsync((UpdateDrive h) => h.Handle(request));
            if (result.IsFailure)
            {
                await DisplayErrorAsync(result.Error);
                return;
            }

            var driveDisplay = new DriveDisplay(Drive);
            WeakReferenceMessenger.Default.Send(new DriveUpdatedMessage(driveDisplay));

            Close();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;

            var request = new TestDriveConnection.Request(
                Drive.Letter,
                Drive.Host,
                Drive.Name,
                Drive.Username,
                Drive.Password,
                Drive.ConnectByHostname);

            Result result = await ScopedHandler.HandleAsync((TestDriveConnection h) => h.Handle(request));
            if (result.IsFailure)
            {
                await DisplayErrorAsync(result.Error);
                return;
            }

            await DisplaySuccessAsync(AppResources.ConnectionTestSucceeded);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private static void Close()
    {
        WeakReferenceMessenger.Default.Send(new UpdateDriveMessage(false, Guid.Empty));
    }

    private async Task LoadAvailableLettersAsync(Guid driveId)
    {
        var request = new GetAvailableDriveLetters.Request(driveId);

        Result<List<string>> result = await ScopedHandler.HandleAsync(
            (GetAvailableDriveLetters h) => h.Handle(request));
        if (result.IsFailure)
        {
            return;
        }

        AvailableLetters = new(result.Value);
    }

    private void RegisterMessages()
    {
        WeakReferenceMessenger.Default.Register<UpdateDriveMessage>(this, async (r, m) =>
        {
            if (m.DriveId == Guid.Empty)
            {
                return;
            }

            IsBusy = true;
            Drive = new UpdateDriveModel();

            try
            {
                var request = new GetDriveById.Request(m.DriveId);

                Result<Drive> result = await ScopedHandler.HandleAsync((GetDriveById h) => h.Handle(request));
                if (result.IsFailure)
                {
                    Close();
                    return;
                }

                Drive = new UpdateDriveModel(result.Value);

                await LoadAvailableLettersAsync(m.DriveId);
            }
            catch (Exception ex)
            {
                AppLog.For<UpdateDriveViewModel>().LogError(ex, "Could not open the drive for editing.");

                Close();
            }
            finally
            {
                IsBusy = false;
            }
        });
    }
}
