using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Modals.Drives.Create;
using Helix.App.Models;
using Helix.Application.Drives;
using Helix.Domain.Drives;
using SharedKernel;
using System.Collections.ObjectModel;

namespace Helix.App.Pages.Home;

internal sealed partial class HomeViewModel : BaseViewModel
{
    private readonly GetDrives _getDrives;

    public HomeViewModel()
    {
        _getDrives = App.ServiceProvider.GetRequiredService<GetDrives>();

        FetchDrives();
    }

    [ObservableProperty]
    private ObservableCollection<DriveDisplay> _drives = [];

    [RelayCommand]
    private static void OpenCreateDriveModal()
    {
        WeakReferenceMessenger.Default.Send(new CreateDriveMessage(true));
    }

    private void FetchDrives()
    {
        Task.Run(async () =>
        {
            Result<List<Drive>> result = await _getDrives.Handle();
            if (result.IsFailure)
            {
                return;
            }

            Drives = new(result.Value.Select(d => new DriveDisplay(d)));
        });
    }
}
