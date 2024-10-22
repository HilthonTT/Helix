using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messages;
using Helix.App.Models;
using Helix.App.Pages;
using Helix.Application.Drives;
using Helix.Domain.Drives;
using SharedKernel;

namespace Helix.App.Modals.Drives.Create;

internal sealed partial class CreateDriveViewModel : BaseViewModel
{
    private readonly CreateDrive _createDrive;

    public CreateDriveViewModel()
    {
        _createDrive = App.ServiceProvider.GetRequiredService<CreateDrive>();
    }

    [ObservableProperty]
    private CreateDriveModel _form = new();

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

            var request = new CreateDrive.Request(Form.Letter, Form.IpAddress, Form.Name, Form.Username, Form.Password);

            Result<Drive> result = await _createDrive.Handle(request);
            if (result.IsFailure)
            {
                await Shell.Current.DisplayAlert("Something went wrong!", result.Error.Description, "Ok");
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

    [RelayCommand]
    private void Close()
    {
        Form = new();
        WeakReferenceMessenger.Default.Send(new CreateDriveMessage(false));
    }
}
