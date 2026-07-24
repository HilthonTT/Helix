using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Models;
using Helix.App.Pages;
using Helix.Application.Drives;
using SharedKernel;

namespace Helix.App.Modals.Drives.Delete;

internal sealed partial class DeleteDriveViewModel : BaseViewModel
{
    public DeleteDriveViewModel()
    {
        RegisterMessages();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Description))]
    private DriveDisplay? _drive;

    public string Description => Drive is null 
        ? "Are you sure you want to delete this drive?"
        : $"Are you sure you want to delete '{Drive?.Name}'?";

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Drive is null || IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;

            var request = new DeleteDrive.Request(Drive.Id);

            Result result = await ScopedHandler.HandleAsync((DeleteDrive h) => h.Handle(request));
            if (result.IsFailure)
            {
                await DisplayErrorAsync(result.Error);
                return;
            }

            WeakReferenceMessenger.Default.Send(new DriveDeletedMessage(Drive.Id));
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
        WeakReferenceMessenger.Default.Send(new DeleteDriveMessage(false, null));
    }

    private void RegisterMessages()
    {
        WeakReferenceMessenger.Default.Register<DeleteDriveMessage>(this, (r, m) =>
        {
            if (m.Drive is not null)
            {
                Drive = m.Drive;
            }
        });
    }
}
