using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Models;
using Helix.App.Pages;
using Helix.Application.Drives;
using Helix.Domain.Drives;
using SharedKernel;

namespace Helix.App.Modals.Drives.Update;

internal sealed partial class UpdateDriveViewModel : BaseViewModel
{
    public UpdateDriveViewModel()
    {
        RegisterMessages();
    }

    [ObservableProperty]
    private UpdateDriveModel _drive = new();

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

            var request = new UpdateDrive.Request(Drive.Id, Drive.Letter, Drive.IpAddress, Drive.Name, Drive.Username, Drive.Password);

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
    private static void Close()
    {
        WeakReferenceMessenger.Default.Send(new UpdateDriveMessage(false, Guid.Empty));
    }

    private void RegisterMessages()
    {
        WeakReferenceMessenger.Default.Register<UpdateDriveMessage>(this, async (r, m) =>
        {
            if (m.DriveId == Guid.Empty)
            {
                return;
            }

            var request = new GetDriveById.Request(m.DriveId);

            Result<Drive> result = await ScopedHandler.HandleAsync((GetDriveById h) => h.Handle(request));
            if (result.IsFailure)
            {
                Close();
                return;
            }

            Drive = new UpdateDriveModel(result.Value);;
        });
    }
}
