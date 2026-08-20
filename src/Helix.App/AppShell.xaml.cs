using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.Navigation;
using Helix.App.Messaging.Users;
using Helix.App.Services;
using Helix.App.ViewModels;
using Helix.App.Views.Auditlogs;
using Helix.App.Views.Drives;
using Helix.App.Views.Settings;
using Helix.App.Views.Users;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Features.Users.Commands;
using System.Diagnostics;

namespace Helix.App;

public sealed partial class AppShell : Shell
{
    private readonly ILoggedInUser _loggedInUser;

    /// <summary>Public source, linked from the sidebar footer.</summary>
    public string RepositoryUrl => "https://github.com/HilthonTT/Helix";

    /// <summary>
    /// Shipping version, read from the build rather than hard-coded here. Windows reports
    /// it as four parts ("2.0.0.0") for an unpackaged app, so it is trimmed back to the
    /// major.minor pair the release is actually named after.
    /// </summary>
    public string AppVersion => $"Helix v{FormatVersion(AppInfo.Current.VersionString)}";

    public string Author => "by Hilthon";

    public AppShell()
    {
        InitializeComponent();

        _loggedInUser = App.ServiceProvider.GetRequiredService<ILoggedInUser>();

        BindingContext = this;
        Navigated += OnNavigated;
        FlyoutBehavior = FlyoutBehavior.Disabled;

        InitRoutes();
        RegisterMessages();
    }

    private string? _selectedRoute;

    /// <summary>
    /// Route backing the sidebar's radio group. It is the group's selected value, so it
    /// has to be set for an item to look active — an <c>IsChecked</c> in XAML is cleared
    /// by the group as soon as the binding applies.
    /// </summary>
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

    /// <summary>
    /// Set while the sidebar selection is being brought in line with a navigation that
    /// already happened, so the resulting <c>CheckedChanged</c> does not navigate again.
    /// </summary>
    private bool _syncingSelection;

    private string _username = string.Empty;

    /// <summary>Name shown on the sidebar's account card.</summary>
    public string Username
    {
        get { return _username; }
        set
        {
            _username = value;
            OnPropertyChanged();
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

        // The rail is built lazily the first time the flyout unlocks, and the group
        // checks the matching item as it appears — navigating again for a page we are
        // already on would only reload it.
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
            await Current.DisplayAlertAsync("Something went wrong!", result.Error.Description, "Ok");
            return;
        }

        // Stop watching before the user is gone: every poll and reconnect runs against
        // the signed-in user, so leaving it running would work on the next one's behalf.
        App.ServiceProvider.GetRequiredService<DriveWatchdog>().Stop();

        // The tray menu lists that user's drives and its commands run as them, so it
        // comes down with the session rather than lingering over the login page.
        App.ServiceProvider.GetRequiredService<TrayIconService>().Stop();

        // Same reasoning for the two pieces of once-per-session state: the countdown
        // would otherwise keep running and minimize the window over the login page, and
        // the dashboard's startup pass would never run again for the next user.
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
            // A missing browser association is not worth an alert over.
            Debug.WriteLine($"Helix: failed to open the repository: {ex}");
        }
    }

    private void OnNavigated(object? sender, ShellNavigatedEventArgs e)
    {
        if (Current?.CurrentItem?.CurrentItem is null)
        {
            return;
        }

        ShellItem currentItem = Current.CurrentItem;

        if (currentItem.Route == PageNames.LoginPage || currentItem.Route == PageNames.RegisterPage)
        {
            FlyoutBehavior = FlyoutBehavior.Disabled;
        }
        else
        {
            FlyoutBehavior = FlyoutBehavior.Locked;

            // The sidebar follows navigation rather than the other way round, so the
            // item for the page we landed on is highlighted even when we got there
            // without clicking it — signing in, for instance, which used to leave the
            // rail with nothing selected until the user clicked Dashboard.
            SyncSelection(currentItem.Route);

            // The shell outlives a sign-out, so refresh rather than caching once.
            Username = _loggedInUser.Username;
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

    private static string FormatVersion(string versionString)
    {
        return Version.TryParse(versionString, out Version? version)
            ? $"{version.Major}.{version.Minor}"
            : versionString;
    }

    private static void InitRoutes()
    {
        Routing.RegisterRoute(PageNames.LoginPage, typeof(LoginPage));
        Routing.RegisterRoute(PageNames.RegisterPage, typeof(RegisterPage));
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
    }
}
