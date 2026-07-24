using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Pages;
using Helix.Application.Users;
using SharedKernel;

namespace Helix.App.Modals.Users.UpdatePassword;

internal sealed partial class UpdatePasswordViewModel : BaseViewModel
{
    public UpdatePasswordViewModel()
    {
    }

    [ObservableProperty]
    private string _currentPassword = string.Empty;

    [ObservableProperty]
    private string _newPassword = string.Empty;

    [ObservableProperty]
    private string _confirmedNewPassword = string.Empty;

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
