using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Helix.App.Constants;

namespace Helix.App.Pages.Login;

internal sealed partial class LoginViewModel : BaseViewModel
{
    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _hidePassword = true;

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;

            // Simulate API call with a 2-second delay
            await Task.Delay(TimeSpan.FromSeconds(2));

            // Here, you can add logic for actual login validation
            // Example: Check username and password or call an authentication service.
        }
        finally
        {
            IsBusy = false;
            Clear();
        }
    }

    [RelayCommand]
    private static Task GoToRegisterAsync()
    {
        return Shell.Current.GoToAsync($"//{PageNames.RegisterPage}", true);
    }

    [RelayCommand]
    private void ToggleHidePassword()
    {
        HidePassword = !HidePassword;
    }

    private void Clear()
    {
        Username = string.Empty;
        Password = string.Empty;
    }
}
