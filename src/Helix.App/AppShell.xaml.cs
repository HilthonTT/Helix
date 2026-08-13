using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Constants;
using Helix.App.Messages;
using Helix.App.Modals.Users.UpdateUsername;
using Helix.App.Pages.Auditlogs;
using Helix.App.Pages.Home;
using Helix.App.Pages.Login;
using Helix.App.Pages.Register;
using Helix.App.Pages.Settings;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Users;
using SharedKernel;

namespace Helix.App;

public sealed partial class AppShell : Shell
{
    private readonly ILoggedInUser _loggedInUser;

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
            _selectedRoute = value;
            OnPropertyChanged();
        }
    }

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
        if (!string.IsNullOrWhiteSpace(_selectedRoute))
        {
            await Current.GoToAsync($"//{_selectedRoute}");
        }
    }

    private async void OnLogout(object? sender, TappedEventArgs e)
    {
        Result result = await ScopedHandler.HandleAsync((LogoutUser h) => h.Handle());
        if (result.IsFailure)
        {
            await Current.DisplayAlertAsync("Something went wrong!", result.Error.Description, "Ok");
            return;
        }

        await Current.GoToAsync($"//{PageNames.LoginPage}");
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

            // The shell outlives a sign-out, so refresh rather than caching once.
            Username = _loggedInUser.Username;
        }

        OnPropertyChanged();
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
