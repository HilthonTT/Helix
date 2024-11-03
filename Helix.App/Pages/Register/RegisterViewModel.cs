using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Helix.App.Constants;
using Helix.App.Helpers;
using Helix.Application.Users;
using Helix.Domain.Settings;
using Helix.Domain.Users;
using SharedKernel;
using System.Collections.ObjectModel;

namespace Helix.App.Pages.Register;

internal sealed partial class RegisterViewModel : BaseViewModel
{
    private readonly RegisterUser _registerUser;

    public RegisterViewModel()
    {
        _registerUser = App.ServiceProvider.GetRequiredService<RegisterUser>();

        Languages = new(CultureSwitcher.Languages);
    }

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

    [ObservableProperty]
    private ObservableCollection<string> _languages = [];

    [ObservableProperty]
    private string _selectedLanguage = string.Empty;
    partial void OnSelectedLanguageChanged(string value)
    {
        Language language = CultureSwitcher.StringToLanguage(value);

        CultureSwitcher.SwitchCulture(language);
    }

    [ObservableProperty]
    private bool _isLoading = false;

    [RelayCommand]
    private async Task RegisterAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsLoading = true;

            IsBusy = true;

            var request = new RegisterUser.Request(Username, Password, ConfirmedPassword);

            Result<User> result = await _registerUser.Handle(request);
            if (result.IsFailure)
            {
                IsLoading = false;
                await DisplayErrorAsync(result.Error);
                return;
            }

            await Task.Delay(100);

            await Shell.Current.GoToAsync($"//{PageNames.HomePage}", true);

            Clear();
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

    [RelayCommand]
    private void LoadCurrentLanguage()
    {
        SelectedLanguage = CultureSwitcher.LanguageToString(CultureSwitcher.GetCurrentLanguage());
    }

    [RelayCommand]
    private void SetLoadingToFalse()
    {
        IsLoading = false;
    }

    private void Clear()
    {
        Username = string.Empty;
        Password = string.Empty;
        ConfirmedPassword = string.Empty;
    }
}
