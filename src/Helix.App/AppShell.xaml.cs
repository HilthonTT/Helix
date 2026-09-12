using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.DriveGroups;
using Helix.App.Messaging.Drives;
using Helix.App.Messaging.Navigation;
using Helix.App.Messaging.Updates;
using Helix.App.Messaging.Users;
using Helix.App.Models;
using Helix.App.Services;
using Helix.App.ViewModels;
using Helix.App.Views.Auditlogs;
using Helix.App.Views.Drives;
using Helix.App.Views.Settings;
using Helix.App.Views.Users;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Updates;
using Helix.Application.Features.DriveGroups.Queries;
using Helix.Application.Features.Drives.Queries;
using Helix.Application.Features.Updates.Queries;
using Helix.Application.Features.Users.Commands;
using Helix.Domain.DriveGroups;
using Helix.Domain.Drives;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace Helix.App;

public sealed partial class AppShell : Shell
{
    private readonly ILoggedInUser _loggedInUser;
    private readonly EstateStatus _estate;

    private bool _sessionStarted;

    public string RepositoryUrl => "https://github.com/HilthonTT/Helix";

    public string AppVersion => $"Helix v{VersionInfo.Display}";

    public string Author => "by Hilthon";

    public AppShell()
    {
        InitializeComponent();

        _loggedInUser = App.ServiceProvider.GetRequiredService<ILoggedInUser>();
        _estate = App.ServiceProvider.GetRequiredService<EstateStatus>();

        Groups = [];

        BindingContext = this;
        Navigated += OnNavigated;
        FlyoutBehavior = FlyoutBehavior.Disabled;

        InitRoutes();
        RegisterMessages();
    }

    private string? _selectedRoute;

    public string? SelectedRoute
    {
        get { return _selectedRoute; }
        set
        {
            if (_selectedRoute == value)
            {
                return;
            }

            _selectedRoute = value;
            OnPropertyChanged();
        }
    }

    private bool _syncingSelection;

    private string _username = string.Empty;

    public string Username
    {
        get { return _username; }
        set
        {
            _username = value;
            OnPropertyChanged();
        }
    }

    public EstateStatus Estate => _estate;

    public ObservableCollection<DriveGroupDisplay> Groups { get; }

    public bool HasGroups => Groups.Count > 0;

    private bool _updateAvailable;

    public bool UpdateAvailable
    {
        get { return _updateAvailable; }
        private set
        {
            if (_updateAvailable == value)
            {
                return;
            }

            _updateAvailable = value;
            OnPropertyChanged();
        }
    }

    private string _updateVersion = string.Empty;

    public string UpdateVersion
    {
        get { return _updateVersion; }
        private set
        {
            if (_updateVersion == value)
            {
                return;
            }

            _updateVersion = value;
            OnPropertyChanged();
        }
    }

    private void BeginSession()
    {
        if (_sessionStarted)
        {
            return;
        }

        _sessionStarted = true;

        _estate.Start();

        _ = RefreshGroupsAsync();
        _ = CheckForUpdatesAsync();
    }

    private void EndSession()
    {
        if (!_sessionStarted)
        {
            return;
        }

        _sessionStarted = false;

        _estate.Stop();

        Groups.Clear();
        OnPropertyChanged(nameof(HasGroups));

        UpdateAvailable = false;
        UpdateVersion = string.Empty;
    }

    private async Task RefreshGroupsAsync()
    {
        if (!_sessionStarted)
        {
            return;
        }

        try
        {
            Result<List<DriveGroup>> groups = await ScopedHandler.HandleAsync((GetDriveGroups h) => h.Handle());
            if (groups.IsFailure)
            {
                return;
            }

            Result<List<Drive>> drives = await ScopedHandler.HandleAsync((GetDrives h) => h.Handle());

            HashSet<Guid> existing = drives.IsSuccess
                ? [.. drives.Value.Select(drive => drive.Id)]
                : [];

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Groups.Clear();

                foreach (DriveGroup group in groups.Value)
                {
                    Groups.Add(new DriveGroupDisplay(group, existing));
                }

                OnPropertyChanged(nameof(HasGroups));
            });
        }
        catch (Exception ex)
        {
            AppLog.For<AppShell>().LogWarning(ex, "The sidebar groups could not be refreshed.");
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            Result<UpdateCheck> result = await ScopedHandler.HandleAsync((CheckForUpdates h) => h.Handle());
            if (result.IsFailure)
            {
                AppLog.For<AppShell>().LogDebug(
                    "The update check at sign-in did not answer: {Reason}",
                    result.Error.Description);

                return;
            }

            ApplyUpdateCheck(result.Value.IsUpdateAvailable, result.Value.LatestVersion);
        }
        catch (Exception ex)
        {
            AppLog.For<AppShell>().LogDebug(ex, "The update check at sign-in faulted.");
        }
    }

    private void ApplyUpdateCheck(bool isAvailable, string latestVersion)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            UpdateVersion = isAvailable ? $"v{latestVersion}" : string.Empty;
            UpdateAvailable = isAvailable;
        });
    }

    private async void OnLockNow(object? sender, TappedEventArgs e)
    {
        try
        {
            await App.ServiceProvider.GetRequiredService<IdleLockService>().LockNowAsync();
        }
        catch (Exception ex)
        {
            AppLog.For<AppShell>().LogError(ex, "The session could not be locked.");
        }
    }

    private async void OnShowUpdates(object? sender, TappedEventArgs e)
    {
        try
        {
            await Current.GoToAsync($"//{PageNames.SettingsPage}");

            WeakReferenceMessenger.Default.Send(new ShowUpdatesMessage());
        }
        catch (Exception ex)
        {
            AppLog.For<AppShell>().LogError(ex, "The update could not be opened from the sidebar.");
        }
    }

    private async void OnMenuItemChanged(object? sender, CheckedChangedEventArgs e)
    {
        if (_syncingSelection)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_selectedRoute))
        {
            return;
        }

        Shell? shell = Current;

        if (shell is null || shell.CurrentItem?.Route == _selectedRoute)
        {
            return;
        }

        await shell.GoToAsync($"//{_selectedRoute}");
    }

    private async void OnLogout(object? sender, TappedEventArgs e)
    {
        Result result = await ScopedHandler.HandleAsync((LogoutUser h) => h.Handle());
        if (result.IsFailure)
        {
            Notifier.Error(result.Error);
            return;
        }

        App.ServiceProvider.GetRequiredService<DriveWatchdog>().Stop();

        App.ServiceProvider.GetRequiredService<MountReconciler>().Stop();

        App.ServiceProvider.GetRequiredService<TrayIconService>().Stop();

        App.ServiceProvider.GetRequiredService<StorageAlertService>().Stop();

        App.ServiceProvider.GetRequiredService<IdleLockService>().Stop();

        BaseViewModel.ResetCountdown();
        HomePage.ResetSessionState();

        await Current.GoToAsync($"//{PageNames.LoginPage}");
    }

    private async void OnOpenRepository(object? sender, TappedEventArgs e)
    {
        try
        {
            await Launcher.Default.OpenAsync(RepositoryUrl);
        }
        catch (Exception ex)
        {
            AppLog.For<AppShell>().LogWarning(ex, "Could not open the repository URL.");
        }
    }

    private void OnNavigated(object? sender, ShellNavigatedEventArgs e)
    {
        if (Current?.CurrentItem?.CurrentItem is null)
        {
            return;
        }

        ShellItem currentItem = Current.CurrentItem;

        if (currentItem.Route is PageNames.LoginPage or PageNames.RegisterPage or PageNames.LockPage)
        {
            FlyoutBehavior = FlyoutBehavior.Disabled;

            if (currentItem.Route is not PageNames.LockPage)
            {
                EndSession();
            }
        }
        else
        {
            FlyoutBehavior = FlyoutBehavior.Locked;

            SyncSelection(currentItem.Route);

            Username = _loggedInUser.Username;

            BeginSession();
        }

        OnPropertyChanged();
    }

    private void SyncSelection(string route)
    {
        if (_selectedRoute == route)
        {
            return;
        }

        _syncingSelection = true;

        try
        {
            SelectedRoute = route;
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private static void InitRoutes()
    {
        Routing.RegisterRoute(PageNames.LoginPage, typeof(LoginPage));
        Routing.RegisterRoute(PageNames.RegisterPage, typeof(RegisterPage));
        Routing.RegisterRoute(PageNames.LockPage, typeof(LockPage));
        Routing.RegisterRoute(PageNames.HomePage, typeof(HomePage));
        Routing.RegisterRoute(PageNames.SettingsPage, typeof(SettingsPage));
        Routing.RegisterRoute(PageNames.AuditlogsPage, typeof(AuditlogsPage));
    }

    private void RegisterMessages()
    {
        WeakReferenceMessenger.Default.Register<PageChangedMessage>(this, (r, m) =>
        {
            SelectedRoute = m.PageName;
        });

        WeakReferenceMessenger.Default.Register<UsernameUpdatedMessage>(this, (r, m) =>
        {
            Username = m.NewUsername;
        });

        WeakReferenceMessenger.Default.Register<DriveGroupsChangedMessage>(
            this, (r, m) => _ = RefreshGroupsAsync());

        WeakReferenceMessenger.Default.Register<DriveCreatedMessage>(
            this, (r, m) => _ = RefreshGroupsAsync());

        WeakReferenceMessenger.Default.Register<DriveDeletedMessage>(
            this, (r, m) => _ = RefreshGroupsAsync());

        WeakReferenceMessenger.Default.Register<UpdateCheckedMessage>(
            this, (r, m) => ApplyUpdateCheck(m.IsUpdateAvailable, m.LatestVersion));
    }
}
