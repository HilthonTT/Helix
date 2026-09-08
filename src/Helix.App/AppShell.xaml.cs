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
using Microsoft.Extensions.Logging;

namespace Helix.App;

public sealed partial class AppShell : Shell
{
    private readonly ILoggedInUser _loggedInUser;

    public string RepositoryUrl => "https://github.com/HilthonTT/Helix";

    public string AppVersion => $"Helix v{VersionInfo.Display}";

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
        }
        else
        {
            FlyoutBehavior = FlyoutBehavior.Locked;

            SyncSelection(currentItem.Route);

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
    }
}
