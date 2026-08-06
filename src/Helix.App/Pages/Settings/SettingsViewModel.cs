using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Helpers;
using Helix.App.Modals.Users.UpdatePassword;
using Helix.App.Modals.Users.UpdateUsername;
using Helix.App.Models;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Settings;
using Helix.Domain.Settings;
using SharedKernel;
using System.Collections.ObjectModel;
using SettingsModel = Helix.Domain.Settings.Settings;

namespace Helix.App.Pages.Settings;

internal sealed partial class SettingsViewModel : BaseViewModel
{
    private const string AccountSection = "account";
    private const string PreferencesSection = "preferences";

    private readonly ILoggedInUser _loggedInUser;

    public SettingsViewModel()
    {
        _loggedInUser = App.ServiceProvider.GetRequiredService<ILoggedInUser>();

        // Partial properties cannot carry field initializers, so defaults are seeded here.
        Languages = [];
        SelectedLanguage = string.Empty;
        CurrentSection = AccountSection;

        Username = _loggedInUser.Username;

        LoadLanguages();
        RegisterMessages();
    }

    [ObservableProperty]
    public partial SettingsDisplay? Settings { get; set; }

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

        Language newSelectedLanguage = CultureSwitcher.StringToLanguage(value);
        Language = newSelectedLanguage;

        if (Settings is not null)
        {
            Settings.Language = newSelectedLanguage;
        }

        CultureSwitcher.SwitchCulture(newSelectedLanguage);
    }

    [ObservableProperty]
    public partial Language Language { get; set; }

    [ObservableProperty]
    public partial string Username { get; set; }

    [ObservableProperty]
    public partial string CurrentSection { get; set; }

    [RelayCommand]
    private void EditUsername()
    {
        WeakReferenceMessenger.Default.Send(new UpdateUsernameMessage(true, _loggedInUser.Username));
    }

    [RelayCommand]  
    private static void EditPassword()
    {
        WeakReferenceMessenger.Default.Send(new UpdatePasswordMessage(true));
    }

    public async Task LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // The page instance is cached by Shell across logins — re-read the
            // username so a different account doesn't see the previous one.
            Username = _loggedInUser.Username;

            Result<SettingsModel> result = await ScopedHandler.HandleAsync(
                (GetSettings h) => h.Handle(cancellationToken));
            if (result.IsFailure)
            {
                await DisplayErrorAsync(result.Error);
                return;
            }

            Settings = new SettingsDisplay(result.Value);
            SelectedLanguage = CultureSwitcher.LanguageToString(Settings.Language);
        }
        catch (Exception ex)
        {
            await DisplayErrorAsync(SharedKernel.Error.Failure("Settings.Load", ex.Message));
        }
    }

    private void LoadLanguages()
    {
        Languages = new(CultureSwitcher.Languages);
    }

    private void RegisterMessages()
    {
        WeakReferenceMessenger.Default.Register<UsernameUpdatedMessage>(this, (r, m) =>
        {
            Username = m.NewUsername;
        });
    }
}
