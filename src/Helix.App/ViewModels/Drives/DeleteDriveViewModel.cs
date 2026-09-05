using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.Drives;
using Helix.App.Models;
using Helix.App.Resources.Languages;
using Helix.App.ViewModels;
using Helix.Application.Features.Drives.Commands;
using Helix.Application.Features.Drives.Queries;

namespace Helix.App.ViewModels.Drives;

internal sealed partial class DeleteDriveViewModel : BaseViewModel
{
    public DeleteDriveViewModel()
    {
        RegisterMessages();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Description))]
    public partial DriveDisplay? Drive { get; set; }

    /// <summary>
    /// The question the sheet asks, in the user's language — it was the one string on the
    /// dashboard still written in English.
    /// </summary>
    public string Description => Drive is null
        ? AppResources.DeleteDriveConfirmGeneric
        : string.Format(AppResources.DeleteDriveConfirm, Drive.Name);

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
