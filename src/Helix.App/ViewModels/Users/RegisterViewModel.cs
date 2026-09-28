using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Helix.Application.Features.Users.Commands;
using Helix.Domain.Settings;
using System.Collections.ObjectModel;

namespace Helix.App.ViewModels.Users;

internal sealed partial class RegisterViewModel : BaseViewModel
{
    public RegisterViewModel()
    {
        Username = string.Empty;
        Password = string.Empty;
        ConfirmedPassword = string.Empty;
        SelectedLanguage = string.Empty;
        HidePassword = true;
        HideConfirmedPassword = true;
        RecoveryKey = string.Empty;

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
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        Language language = CultureSwitcher.StringToLanguage(value);

        CultureSwitcher.SwitchCulture(language);
    }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string RecoveryKey { get; set; }

    [ObservableProperty]
    public partial bool IsShowingRecoveryKey { get; set; }

    [RelayCommand]
    private async Task RegisterAsync()
    {
        if (IsBusy || IsShowingRecoveryKey)
        {
            return;
        }

        try
        {
            IsLoading = true;

            IsBusy = true;

            var request = new RegisterUser.Request(Username, Password, ConfirmedPassword);

            Result<RegisterUser.Response> result = await Task.Run(() => ScopedHandler.HandleAsync((RegisterUser h) => h.Handle(request)));
            if (result.IsFailure)
            {
                IsLoading = false;
                await DisplayErrorAsync(result.Error);
                return;
            }

            Clear();

            RecoveryKey = result.Value.RecoveryKey;
            IsShowingRecoveryKey = true;
            IsLoading = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ContinueAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;

            await Shell.Current.GoToAsync($"//{PageNames.HomePage}", true);

            RecoveryKey = string.Empty;
            IsShowingRecoveryKey = false;
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
