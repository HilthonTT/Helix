using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.DriveGroups;
using Helix.App.Messaging.Drives;
using Helix.App.Messaging.Navigation;
using Helix.App.Messaging.Updates;
using Helix.App.Messaging.Users;
using Helix.App.Models;
using Helix.App.Resources.Languages;
using Helix.App.Services;
using Helix.App.ViewModels;
using Helix.App.Views.Auditlogs;
using Helix.App.Views.Drives;
using Helix.App.Views.Insights;
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

#if WINDOWS
        HandlerChanged += OnHandlerChanged;
#endif

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
            OnPropertyChanged(nameof(ShowUpdateBadge));
        }
    }

    private string _updateProgressStatus = string.Empty;

    public string UpdateProgressStatus
    {
        get { return _updateProgressStatus; }
        private set
        {
            if (_updateProgressStatus == value)
            {
                return;
            }

            _updateProgressStatus = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsUpdating));
            OnPropertyChanged(nameof(IsNotUpdating));
            OnPropertyChanged(nameof(ShowUpdateBadge));
        }
    }

    private double _updateProgress;

    public double UpdateProgress
    {
        get { return _updateProgress; }
        private set
        {
            if (_updateProgress == value)
            {
                return;
            }

            _updateProgress = value;
            OnPropertyChanged();
        }
    }

    public bool IsUpdating => !string.IsNullOrEmpty(UpdateProgressStatus);

    public bool IsNotUpdating => !IsUpdating;

    public bool ShowUpdateBadge => UpdateAvailable || IsUpdating;

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
        UpdateProgressStatus = string.Empty;
        UpdateProgress = 0;
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

    private void ApplyUpdateProgress(string status, double fraction)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            UpdateProgress = Math.Clamp(fraction, 0, 1);
            UpdateProgressStatus = status;
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

        try
        {
            await shell.GoToAsync($"//{_selectedRoute}");
        }
        catch (Exception ex)
        {
            AppLog.For<AppShell>().LogError(ex, "The sidebar could not navigate to {Route}.", _selectedRoute);

            Notifier.Error(AppResources.UnexpectedError);
        }
    }

    private async void OnLogout(object? sender, TappedEventArgs e)
    {
        try
        {
            await LogoutAsync();
        }
        catch (Exception ex)
        {
            AppLog.For<AppShell>().LogError(ex, "Signing out failed.");

            Notifier.Error(AppResources.UnexpectedError);
        }
    }

    private static async Task LogoutAsync()
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

        App.ServiceProvider.GetRequiredService<HotkeyService>().Stop();

        App.ServiceProvider.GetRequiredService<StorageAlertService>().Stop();

        App.ServiceProvider.GetRequiredService<ScheduleService>().Stop();

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

#if WINDOWS
    private void OnHandlerChanged(object? sender, EventArgs e)
    {
        if (Handler?.PlatformView is not Microsoft.UI.Xaml.FrameworkElement view)
        {
            return;
        }

        view.Loaded += OnPlatformViewLoaded;
    }

    private static void OnPlatformViewLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var view = (Microsoft.UI.Xaml.FrameworkElement)sender;
        view.Loaded -= OnPlatformViewLoaded;

        try
        {
            Microsoft.UI.Xaml.Controls.SplitView? splitView = FindDescendant<Microsoft.UI.Xaml.Controls.SplitView>(view);
            if (splitView is null || Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(splitView) == 0)
            {
                return;
            }

            if (Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(splitView, 0) is not Microsoft.UI.Xaml.FrameworkElement root)
            {
                return;
            }

            foreach (Microsoft.UI.Xaml.VisualStateGroup group in Microsoft.UI.Xaml.VisualStateManager.GetVisualStateGroups(root))
            {
                group.Transitions.Clear();
            }
        }
        catch (Exception ex)
        {
            AppLog.For<AppShell>().LogDebug(ex, "The sidebar's pane transitions could not be removed.");
        }
    }

    private static T? FindDescendant<T>(Microsoft.UI.Xaml.DependencyObject parent)
        where T : Microsoft.UI.Xaml.DependencyObject
    {
        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);

        for (int i = 0; i < count; i++)
        {
            Microsoft.UI.Xaml.DependencyObject child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);

            if (child is T match)
            {
                return match;
            }

            if (FindDescendant<T>(child) is T nested)
            {
                return nested;
            }
        }

        return null;
    }
#endif

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
        Routing.RegisterRoute(PageNames.InsightsPage, typeof(InsightsPage));
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

        WeakReferenceMessenger.Default.Register<UpdateProgressMessage>(
            this, (r, m) => ApplyUpdateProgress(m.Status, m.Fraction));
    }
}
