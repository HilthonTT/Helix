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
    public RegisterViewModel()
    {
        // Partial properties cannot carry field initializers, so defaults are seeded here.
        Username = string.Empty;
        Password = string.Empty;
        ConfirmedPassword = string.Empty;
        SelectedLanguage = string.Empty;
        HidePassword = true;
        HideConfirmedPassword = true;

        Languages = new(CultureSwitcher.Languages);
    }

    [ObservableProperty]
    public partial string Username { get; set; }

    [ObservableProperty]
    public partial string Password { get; set; }

    [ObservableProperty]
    public partial string ConfirmedPassword { get; set; }

    [ObservableProperty]
    public partial bool HidePassword { get; set; }

    [ObservableProperty]
    public partial bool HideConfirmedPassword { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<string> Languages { get; set; }

    [ObservableProperty]
    public partial string SelectedLanguage { get; set; }
    partial void OnSelectedLanguageChanged(string value)
    {
        // "no selection yet" is not a language — StringToLanguage would throw on it.
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        Language language = CultureSwitcher.StringToLanguage(value);

        CultureSwitcher.SwitchCulture(language);
    }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

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

            Result<User> result = await ScopedHandler.HandleAsync((RegisterUser h) => h.Handle(request));
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
