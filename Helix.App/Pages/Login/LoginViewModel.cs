using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Helix.App.Constants;
using Helix.App.Helpers;
using Helix.Application.Users;
using Helix.Domain.Settings;
using Helix.Domain.Users;
using SharedKernel;
using System.Collections.ObjectModel;

namespace Helix.App.Pages.Login;

internal sealed partial class LoginViewModel : BaseViewModel
{
    private readonly LoginUser _loginUser;

    public LoginViewModel()
    {
        _loginUser = App.ServiceProvider.GetRequiredService<LoginUser>();

        Languages = new(CultureSwitcher.Languages);

        LanguageService.Instance.LanguageChanged += OnLanguageChanged;
        SelectedLanguage = CultureSwitcher.LanguageToString(LanguageService.Instance.CurrentLanguage);
    }

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _hidePassword = true;

    [ObservableProperty]
    private ObservableCollection<string> _languages = [];

    [ObservableProperty]
    private string _selectedLanguage = CultureSwitcher.LanguageToString(Language.English);
    partial void OnSelectedLanguageChanged(string value)
    {
        Language language = CultureSwitcher.StringToLanguage(value);

        CultureSwitcher.SwitchCulture(language);
    }

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
                await DisplayErrorAsync(result.Error);
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

    private void OnLanguageChanged(Language newLanguage)
    {
        SelectedLanguage = CultureSwitcher.LanguageToString(newLanguage);
    }

    private void Clear()
    {
        Username = string.Empty;
        Password = string.Empty;
    }
}
