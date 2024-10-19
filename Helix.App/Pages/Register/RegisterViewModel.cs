using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Helix.App.Constants;

namespace Helix.App.Pages.Register;

internal sealed partial class RegisterViewModel : BaseViewModel
{
    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _confirmedPassword = string.Empty;

    [ObservableProperty]
    private bool _hidePassword = true;

    [ObservableProperty]
    private bool _hideConfirmedPassword = true;

    [RelayCommand]
    private async Task RegisterAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;

            await Task.Delay(TimeSpan.FromSeconds(2));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private static Task GoToLoginAsync()
    {
        return Shell.Current.GoToAsync($"//{PageNames.LoginPage}", true);
    }

    [RelayCommand]
    private void TogglePasswordHidden()
    {
        HidePassword = !HidePassword;
    }

    [RelayCommand]
    private void ToggleConfirmedPasswordHidden()
    {
        HideConfirmedPassword = !HideConfirmedPassword;
    }
}
