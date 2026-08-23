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

        // Partial properties cannot carry field initializers, so defaults are seeded here.
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

    /// <summary>
    /// What the updater is doing, or empty while it is doing nothing.
    /// </summary>
    /// <remarks>
    /// A release is a couple of hundred megabytes, which is long enough that a button
    /// that merely goes disabled reads as a button that did nothing.
    /// </remarks>
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

    /// <summary>
    /// Asks GitHub whether a newer Helix has been released, and offers to install it.
    /// </summary>
    /// <remarks>
    /// Manual on purpose: replacing the app is not something to do behind the user's
    /// back, and the check itself is one HTTP call they can make when it suits them.
    ///
    /// Opening the release page stays as the other option, and is the only one where a
    /// release carries no build for this machine, where the app was put somewhere the
    /// user cannot write to, or where they would simply rather do it themselves.
    /// </remarks>
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

            // Only offered where it can actually be carried out: an install button that
            // fails on the last step is worse than not offering one.
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

    /// <summary>
    /// Downloads the release, asks once more, and hands the swap over.
    /// </summary>
    /// <remarks>
    /// The confirmation comes after the download rather than before it, so what the user
    /// is agreeing to is a file that is already on disk and already looks like Helix.
    /// Everything up to that point leaves the install untouched and can be abandoned at
    /// no cost; everything after it happens in a helper process that outlives this one.
    /// </remarks>
    private async Task InstallAsync(UpdateCheck check)
    {
        // Created here so its callbacks land on the UI thread, which is where the bound
        // status text has to be written.
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
            // The staged copy is left where it is: they may come back to it, and the next
            // attempt at this version clears the folder before using it again.
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

        // Taken down first: an icon whose process has gone stays in the tray until the
        // user happens to mouse over it, and this one would sit there through the swap.
        App.ServiceProvider.GetRequiredService<TrayIconService>().Stop();

        // The helper is waiting on this process to exit before it moves anything, so
        // there is nothing to do here but go — and go without the close button putting
        // the window away instead of closing it.
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
            // A missing browser association is not worth an alert over — the same
            // treatment the repository link in the sidebar gets.
            AppLog.For<SettingsViewModel>().LogWarning(ex, "Could not open the release page.");
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
