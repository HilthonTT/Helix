using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.Users;
using Helix.App.Models;
using Helix.App.Services;
using Helix.App.Resources.Languages;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Updates;
using Helix.Application.Features.Diagnostics.Commands;
using Helix.Application.Features.Settings.Commands;
using Helix.Application.Features.Settings.Queries;
using Helix.Application.Features.Updates.Commands;
using Helix.Application.Features.Updates.Queries;
using Helix.Domain.Settings;
using Microsoft.Extensions.Logging;
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

        Languages = [];
        SelectedLanguage = string.Empty;
        CurrentSection = AccountSection;
        UpdateStatus = string.Empty;

        Username = _loggedInUser.Username;

        LoadLanguages();
        RegisterMessages();
    }

    [ObservableProperty]
    public partial SettingsDisplay? Settings { get; set; }

    public bool SupportsTray =>
        App.ServiceProvider.GetRequiredService<TrayIconService>().IsSupported;

    public int MaximumStorageAlertThresholdPercent =>
        SettingsModel.MaximumStorageAlertThresholdPercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdateStatus))]
    public partial string UpdateStatus { get; set; }

    public bool HasUpdateStatus => !string.IsNullOrEmpty(UpdateStatus);

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
                await DisplayErrorAsync(result.Error);
                return;
            }

            await DisplaySuccessAsync($"{AppResources.DiagnosticsExported}{Environment.NewLine}{result.Value}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;

            Result<UpdateCheck> result = await ScopedHandler.HandleAsync((CheckForUpdates h) => h.Handle());
            if (result.IsFailure)
            {
                await DisplayErrorAsync(result.Error);
                return;
            }

            UpdateCheck check = result.Value;

            if (!check.IsUpdateAvailable)
            {
                await DisplaySuccessAsync(string.Format(AppResources.UpToDate, check.CurrentVersion));
                return;
            }

            string message = string.Format(
                AppResources.UpdateAvailable,
                check.LatestVersion,
                check.CurrentVersion);

            bool canInstall = check.CanInstall &&
                App.ServiceProvider.GetRequiredService<IUpdateInstaller>().IsSupported;

            if (!canInstall)
            {
                bool open = await Shell.Current.DisplayAlertAsync(
                    AppResources.Updates,
                    message,
                    AppResources.OpenReleasePage,
                    AppResources.Cancel);

                if (open)
                {
                    await OpenReleasePageAsync(check.ReleaseUrl);
                }

                return;
            }

            string choice = await Shell.Current.DisplayActionSheetAsync(
                message,
                AppResources.Cancel,
                null,
                AppResources.UpdateInstall,
                AppResources.OpenReleasePage);

            if (choice == AppResources.OpenReleasePage)
            {
                await OpenReleasePageAsync(check.ReleaseUrl);
                return;
            }

            if (choice == AppResources.UpdateInstall)
            {
                await InstallAsync(check);
            }
        }
        finally
        {
            IsBusy = false;
            UpdateStatus = string.Empty;
        }
    }

    private async Task InstallAsync(UpdateCheck check)
    {
        var progress = new Progress<double>(fraction =>
            UpdateStatus = string.Format(AppResources.UpdateDownloading, (int)(fraction * 100)));

        UpdateStatus = string.Format(AppResources.UpdateDownloading, 0);

        Result<string> staged = await ScopedHandler.HandleAsync(
            (StageUpdate h) => h.Handle(new StageUpdate.Request(check, progress)));

        if (staged.IsFailure)
        {
            UpdateStatus = string.Empty;

            await DisplayErrorAsync(staged.Error);
            return;
        }

        UpdateStatus = AppResources.UpdateReady;

        bool install = await Shell.Current.DisplayAlertAsync(
            AppResources.UpdateReady,
            string.Format(AppResources.UpdateReadyMessage, check.LatestVersion),
            AppResources.UpdateInstallNow,
            AppResources.Cancel);

        if (!install)
        {
            return;
        }

        Result applied = await ScopedHandler.HandleAsync(
            (ApplyUpdate h) => h.Handle(new ApplyUpdate.Request(staged.Value)));

        if (applied.IsFailure)
        {
            UpdateStatus = string.Empty;

            await DisplayErrorAsync(applied.Error);
            return;
        }

        AppLog.For<SettingsViewModel>().LogInformation(
            "Quitting to let the update to {Version} be applied.",
            check.LatestVersion);

        App.ServiceProvider.GetRequiredService<TrayIconService>().Stop();

        MainWindow.Exit();
    }

    private static async Task OpenReleasePageAsync(string releaseUrl)
    {
        try
        {
            await Launcher.Default.OpenAsync(releaseUrl);
        }
        catch (Exception ex)
        {
            AppLog.For<SettingsViewModel>().LogWarning(ex, "Could not open the release page.");
        }
    }

    public async Task LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
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
