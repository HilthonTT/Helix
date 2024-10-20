using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Helix.App.Constants;
using Helix.Application.Users;
using Helix.Domain.Users;
using SharedKernel;

namespace Helix.App.Pages.Login;

internal sealed partial class LoginViewModel : BaseViewModel
{
    private readonly LoginUser _loginUser;

    public LoginViewModel()
    {
        _loginUser = App.ServiceProvider.GetRequiredService<LoginUser>();
    }

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

            var request = new LoginUser.Request(Username, Password);

            Result<User> result = await _loginUser.Handle(request);
            if (result.IsFailure)
            {
                await Shell.Current.DisplayAlert("Something went wrong!", result.Error.Description, "Ok");
                return;
            }

            await Shell.Current.GoToAsync($"//{PageNames.HomePage}", true);

            Clear();
        }
        finally
        {
            IsBusy = false;
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
