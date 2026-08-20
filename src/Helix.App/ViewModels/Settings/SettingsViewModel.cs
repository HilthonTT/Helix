using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.Users;
using Helix.App.Models;
using Helix.App.Resources.Languages;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Features.Diagnostics.Commands;
using Helix.Application.Features.Settings.Commands;
using Helix.Application.Features.Settings.Queries;
using Helix.Domain.Settings;
using System.Collections.ObjectModel;
using SettingsModel = Helix.Domain.Settings.Settings;

namespace Helix.App.ViewModels.Settings;

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

    /// <summary>
    /// Writes the log files to a folder the user picks, so they have something concrete
    /// to attach to a bug report.
    /// </summary>
    [RelayCommand]
    private async Task ExportDiagnosticsAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;

            Result<string> result = await ScopedHandler.HandleAsync((ExportDiagnostics h) => h.Handle());
            if (result.IsFailure)
            {
                // Cancelling the folder picker reports itself as a failure here, the same
                // way the drive export does; both surface it as a plain message.
                await DisplayErrorAsync(result.Error);
                return;
            }

            // The path is the useful part — the user has to go and find the file.
            await DisplaySuccessAsync($"{AppResources.DiagnosticsExported}{Environment.NewLine}{result.Value}");
        }
        finally
        {
            IsBusy = false;
        }
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
            await DisplayErrorAsync(Error.Failure("Settings.Load", ex.Message));
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
