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

namespace Helix.App.ViewModels.Drives;

internal sealed partial class CreateDriveViewModel : BaseViewModel
{
    public CreateDriveViewModel()
    {
        // Partial properties cannot carry field initializers, so defaults are seeded here.
        Form = new();
        HideSecrets = true;
    }

    [ObservableProperty]
    public partial CreateDriveModel Form { get; set; }

    /// <summary>
    /// The address, share user and password are masked by default so the form is safe
    /// to fill in while sharing a screen. One reveal toggle covers all three, because
    /// checking a typo in the address is otherwise impossible.
    /// </summary>
    [ObservableProperty]
    public partial bool HideSecrets { get; set; }

    /// <summary>
    /// Hides the "remember at sign-in" switch on platforms that cannot honour it.
    /// </summary>
    /// <remarks>
    /// macOS has no equivalent of a remembered drive mapping, so the switch is not shown
    /// there at all rather than offered and quietly ignored.
    /// </remarks>
    public bool SupportsPersistentMappings => DrivePlatform.SupportsPersistentMappings;

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;

            var request = new CreateDrive.Request(
                Form.Letter,
                Form.Host,
                Form.Name,
                Form.Username,
                Form.Password,
                Form.AutoConnect,
                Form.Persistent);

            Result<Drive> result = await ScopedHandler.HandleAsync((CreateDrive h) => h.Handle(request));
            if (result.IsFailure)
            {
                await DisplayErrorAsync(result.Error);
                return;
            }

            WeakReferenceMessenger.Default.Send(new DriveCreatedMessage(result.Value));
            WeakReferenceMessenger.Default.Send(new CheckDrivesStatusMessage());

            Close();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Verifies the details against the server without saving them, so a wrong password
    /// is reported here rather than at the first connect long after the modal is gone.
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
                Form.Letter,
                Form.Host,
                Form.Name,
                Form.Username,
                Form.Password);

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
    private void Close()
    {
        Form = new();
        WeakReferenceMessenger.Default.Send(new CreateDriveMessage(false));
    }
}
