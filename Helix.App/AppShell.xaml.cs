using Helix.App.Constants;
using Helix.App.Pages.Login;
using Helix.App.Pages.Register;

namespace Helix.App;

public sealed partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        BindingContext = this;
        Navigated += OnNavigated;
        FlyoutBehavior = FlyoutBehavior.Disabled;

        InitRoutes();
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

    private async void OnMenuItemChanged(object? sender, CheckedChangedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_selectedRoute))
        {
            await Current.GoToAsync($"//{_selectedRoute}");
        }
    }

    private void OnLogout(object? sender, EventArgs e)
    { 

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
        }

        OnPropertyChanged();
    }

    private static void InitRoutes()
    {
        Routing.RegisterRoute(PageNames.LoginPage, typeof(LoginPage));
        Routing.RegisterRoute(PageNames.RegisterPage, typeof(RegisterPage));
    }
}
