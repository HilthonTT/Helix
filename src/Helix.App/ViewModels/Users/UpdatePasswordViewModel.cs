using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.Users;
using Helix.App.ViewModels;
using Helix.Application.Features.Users.Commands;

namespace Helix.App.ViewModels.Users;

internal sealed partial class UpdatePasswordViewModel : BaseViewModel
{
    public UpdatePasswordViewModel()
    {
        // Partial properties cannot carry field initializers, so defaults are seeded here.
        CurrentPassword = string.Empty;
        NewPassword = string.Empty;
        ConfirmedNewPassword = string.Empty;
    }

    [ObservableProperty]
    public partial string CurrentPassword { get; set; }

    [ObservableProperty]
    public partial string NewPassword { get; set; }

    [ObservableProperty]
    public partial string ConfirmedNewPassword { get; set; }

    [RelayCommand]
    private async Task UpdatePasswordAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;

            var request = new ChangeUserPassword.Request(CurrentPassword, NewPassword, ConfirmedNewPassword);

            Result result = await ScopedHandler.HandleAsync((ChangeUserPassword h) => h.Handle(request));
            if (result.IsFailure)
            {
                await DisplayErrorAsync(result.Error);

                return;
            }

            await DisplaySuccessAsync("You've updated your password");

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
        WeakReferenceMessenger.Default.Send(new UpdatePasswordMessage(false));
        Clear();
    }

    private void Clear()
    {
        CurrentPassword = string.Empty;
        NewPassword = string.Empty;
        ConfirmedNewPassword = string.Empty;
    }
}
