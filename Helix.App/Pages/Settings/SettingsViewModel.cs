using CommunityToolkit.Maui.Core.Extensions;
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

    private readonly GetSettings _getSettings;
    private readonly ILoggedInUser _loggedInUser;

    public SettingsViewModel()
    {
        _getSettings = App.ServiceProvider.GetRequiredService<GetSettings>();
        _loggedInUser = App.ServiceProvider.GetRequiredService<ILoggedInUser>();

        Username = _loggedInUser.Username;

        LoadLanguages();
        LoadSettings();
        RegisterMessages();
    }

    [ObservableProperty]
    private SettingsDisplay? _settings;

    [ObservableProperty]
    private ObservableCollection<string> _languages = [];

    [ObservableProperty]
    private string _selectedLanguage = string.Empty;
    partial void OnSelectedLanguageChanged(string value)
    {
        Language newSelectedLanguage = StringToLanguage(value);
        Language = newSelectedLanguage;

        if (Settings is not null)
        {
            Settings.Language = newSelectedLanguage;
        }

        CultureSwitcher.SwitchCulture(newSelectedLanguage);
    }

    [ObservableProperty]
    private Language _language;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _currentSection = AccountSection;

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

    private static Language StringToLanguage(string languageString)
    {
        return languageString switch
        {
            "English" => Language.English,
            "French" => Language.French,
            "German" => Language.German,
            "Indonesian" => Language.Indonesian,
            "Japanese" => Language.Japanese,
            "Dutch" => Language.Dutch,
            _ => throw new ArgumentException($"Unknown language: {languageString}")
        };
    }

    private static string LanguageToString(Language language)
    {
        return language switch
        {
            Language.English => "English",
            Language.French => "French",
            Language.German => "German",
            Language.Indonesian => "Indonesian",
            Language.Japanese => "Japanese",
            Language.Dutch => "Dutch",
            _ => "Unknown Language"
        };
    }

    private void LoadSettings()
    {
        Task.Run(async () =>
        {
            Result<SettingsModel> result = await _getSettings.Handle();
            if (result.IsFailure)
            {
                await DisplayErrorAsync(result.Error);
            }
            else
            {
                Settings = new SettingsDisplay(result.Value);
                SelectedLanguage = LanguageToString(Settings.Language);
            }
        });
    }

    private void LoadLanguages()
    {
        Languages = Enum.GetNames(typeof(Language)).ToObservableCollection();
    }

    private void RegisterMessages()
    {
        WeakReferenceMessenger.Default.Register<UsernameUpdatedMessage>(this, (r, m) =>
        {
            Username = m.NewUsername;
        });
    }
}
