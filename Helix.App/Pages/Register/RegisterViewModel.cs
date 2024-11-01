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


        LanguageService.Instance.LanguageChanged += OnLanguageChanged;
        SelectedLanguage = CultureSwitcher.LanguageToString(LanguageService.Instance.CurrentLanguage);
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
    private string _selectedLanguage = CultureSwitcher.LanguageToString(Language.English);
    partial void OnSelectedLanguageChanged(string value)
    {
        Language language = CultureSwitcher.StringToLanguage(value);

        CultureSwitcher.SwitchCulture(language);
    }

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

            var request = new RegisterUser.Request(Username, Password, ConfirmedPassword);

            Result<User> result = await _registerUser.Handle(request);
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

    private void OnLanguageChanged(Language newLanguage)
    {
        SelectedLanguage = CultureSwitcher.LanguageToString(newLanguage);
    }

    private void Clear()
    {
        Username = string.Empty;
        Password = string.Empty;
        ConfirmedPassword = string.Empty;
    }
}
