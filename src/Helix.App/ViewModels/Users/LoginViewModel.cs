using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Helix.App.Resources.Languages;
using Helix.App.Services;
using Helix.Application.Features.Users.Commands;
using Helix.Domain.Settings;
using Helix.Domain.Users;
using System.Collections.ObjectModel;

namespace Helix.App.ViewModels.Users;

internal sealed partial class LoginViewModel : BaseViewModel
{
    public LoginViewModel()
    {
        Username = string.Empty;
        Password = string.Empty;
        SelectedLanguage = string.Empty;
        HidePassword = true;
        RecoveryKeyInput = string.Empty;
        NewPassword = string.Empty;
        ConfirmedNewPassword = string.Empty;
        RecoveryKey = string.Empty;

        Languages = new(CultureSwitcher.Languages);
    }

    [ObservableProperty]
    public partial string Username { get; set; }

    [ObservableProperty]
    public partial string Password { get; set; }

    [ObservableProperty]
    public partial bool HidePassword { get; set; }

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
    [NotifyPropertyChangedFor(nameof(IsSigningIn))]
    public partial bool IsResetting { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSigningIn))]
    public partial bool IsShowingRecoveryKey { get; set; }

    public bool IsSigningIn => !IsResetting && !IsShowingRecoveryKey;

    [ObservableProperty]
    public partial string RecoveryKeyInput { get; set; }

    [ObservableProperty]
    public partial string NewPassword { get; set; }

    [ObservableProperty]
    public partial string ConfirmedNewPassword { get; set; }

    [ObservableProperty]
    public partial string RecoveryKey { get; set; }

    [RelayCommand]
    private Task SubmitAsync()
    {
        if (IsResetting)
        {
            return ResetPasswordAsync();
        }

        return IsSigningIn ? LoginAsync() : Task.CompletedTask;
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
            IsLoading = true;

            IsBusy = true;

            var request = new LoginUser.Request(Username, Password);

            Result<User> result = await Task.Run(() => ScopedHandler.HandleAsync((LoginUser h) => h.Handle(request)));
            if (result.IsFailure)
            {
                IsLoading = false;
                await DisplayErrorAsync(result.Error);
                return;
            }

            await Task.Delay(100);

            await Shell.Current.GoToAsync($"//{PageNames.HomePage}", true);

            Clear();

            if (!result.Value.HasRecoveryKey)
            {
                Notifier.Info(AppResources.NoRecoveryKeyNudge);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ResetPasswordAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;

            var request = new ResetPasswordWithRecoveryKey.Request(Username, RecoveryKeyInput, NewPassword, ConfirmedNewPassword);

            Result<ResetPasswordWithRecoveryKey.Response> result = await Task.Run(
                () => ScopedHandler.HandleAsync((ResetPasswordWithRecoveryKey h) => h.Handle(request)));
            if (result.IsFailure)
            {
                await DisplayErrorAsync(result.Error);
                return;
            }

            Clear();

            RecoveryKey = result.Value.RecoveryKey;
            IsResetting = false;
            IsShowingRecoveryKey = true;

            await DisplaySuccessAsync(AppResources.PasswordUpdated);
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
    private void ForgotPassword()
    {
        Password = string.Empty;
        IsResetting = true;
    }

    [RelayCommand]
    private void BackToLogin()
    {
        ClearReset();
        IsResetting = false;
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
        ClearReset();
    }

    private void ClearReset()
    {
        RecoveryKeyInput = string.Empty;
        NewPassword = string.Empty;
        ConfirmedNewPassword = string.Empty;
    }
}
