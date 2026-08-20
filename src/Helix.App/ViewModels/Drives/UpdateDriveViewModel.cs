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
using System.Collections.ObjectModel;

namespace Helix.App.ViewModels.Drives;

internal sealed partial class UpdateDriveViewModel : BaseViewModel
{
    public UpdateDriveViewModel()
    {
        // Partial properties cannot carry field initializers, so defaults are seeded here.
        Drive = new();
        HideSecrets = true;
        AvailableLetters = [];

        RegisterMessages();
    }

    [ObservableProperty]
    public partial UpdateDriveModel Drive { get; set; }

    /// <summary>
    /// Free letters, plus this drive's own — see
    /// <see cref="GetAvailableDriveLetters.Request.ExcludeDriveId"/>.
    /// </summary>
    [ObservableProperty]
    public partial ObservableCollection<string> AvailableLetters { get; set; }

    /// <summary>
    /// Masks the address, share user and password by default; one reveal toggle covers
    /// all three so a typo in the address can still be checked.
    /// </summary>
    [ObservableProperty]
    public partial bool HideSecrets { get; set; }

    /// <summary>
    /// Hides the "remember at sign-in" switch on platforms that cannot honour it — see
    /// <see cref="DrivePlatform.SupportsPersistentMappings"/>.
    /// </summary>
    public bool SupportsPersistentMappings => DrivePlatform.SupportsPersistentMappings;

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
                Drive.Persistent);

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

    /// <summary>
    /// Verifies the details on screen against the server without saving them, so an
    /// edit that breaks the connection is caught before it replaces a working one.
    /// </summary>
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
                Drive.Password);

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

            var request = new GetDriveById.Request(m.DriveId);

            Result<Drive> result = await ScopedHandler.HandleAsync((GetDriveById h) => h.Handle(request));
            if (result.IsFailure)
            {
                Close();
                return;
            }

            // Letters first, then the drive. The picker's SelectedItem is bound two-way,
            // so assigning a drive whose letter is not yet in ItemsSource makes the
            // control fall back to "nothing selected" and write that emptiness straight
            // back into Drive.Letter — the modal opened blank and refused to save.
            await LoadAvailableLettersAsync(m.DriveId);

            Drive = new UpdateDriveModel(result.Value);
        });
    }
}
