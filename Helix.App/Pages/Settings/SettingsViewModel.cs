using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Helix.App.Models;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Settings;
using SharedKernel;
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

        LoadSettings();
    }

    [ObservableProperty]
    private SettingsDisplay? _settings;

    [ObservableProperty]
    private string _username;

    [ObservableProperty]
    private string _currentSection = AccountSection;


    [RelayCommand]
    private void EditUsername()
    {

    }

    [RelayCommand]  
    private void EditPassword()
    {

    }

    [RelayCommand]
    private void SaveTimerCount()
    {

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
            }
        });
    }
}
