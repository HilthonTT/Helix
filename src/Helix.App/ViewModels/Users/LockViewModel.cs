using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Helix.App.Resources.Languages;
using Helix.App.Services;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Features.Users.Commands;

namespace Helix.App.ViewModels.Users;

/// <summary>
/// The lock screen: one password box between the user and the dashboard they left open.
/// </summary>
/// <remarks>
/// Not the sign-in page with the username removed. Signing out stops the watchdog and the
/// tray, because both act as the signed-in user; locking must not, or a machine left alone
/// overnight would stop reconnecting its drives at exactly the point nobody is watching.
/// So the session is still live behind this, and all this asks is who is at the keyboard.
/// </remarks>
internal sealed partial class LockViewModel : BaseViewModel
{
    private readonly ILoggedInUser _loggedInUser;

    public LockViewModel()
    {
        _loggedInUser = App.ServiceProvider.GetRequiredService<ILoggedInUser>();

        // Partial properties cannot carry field initializers, so defaults are seeded here.
        Password = string.Empty;
        Error = string.Empty;
    }

    [ObservableProperty]
    public partial string Password { get; set; }

    /// <summary>
    /// The wrong-password message, shown in the page rather than in an alert.
    /// </summary>
    /// <remarks>
    /// A modal alert over a lock screen is a dialog someone has to dismiss before they can
    /// try again, and a mistyped password is the ordinary case here rather than an
    /// exceptional one.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string Error { get; set; }

    public bool HasError => !string.IsNullOrEmpty(Error);

    /// <summary>Who is being asked, so the screen names the account it will unlock.</summary>
    public string Username => _loggedInUser.Username;

    [RelayCommand]
    private async Task UnlockAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            Error = string.Empty;

            Result result = await ScopedHandler.HandleAsync(
                (UnlockSession h) => h.Handle(new UnlockSession.Request(Password)));

            if (result.IsFailure)
            {
                Error = AppResources.LockWrongPassword;
                Password = string.Empty;

                return;
            }

            Password = string.Empty;

            await App.ServiceProvider.GetRequiredService<IdleLockService>().UnlockAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Ends the session outright, for the user who is done rather than away.</summary>
    [RelayCommand]
    private async Task SignOutAsync()
    {
        await App.ServiceProvider.GetRequiredService<IdleLockService>().SignOutAsync();
    }

    /// <summary>
    /// Re-reads the account name and clears the previous attempt's error each time the
    /// screen appears.
    /// </summary>
    public void Refresh()
    {
        Error = string.Empty;
        OnPropertyChanged(nameof(Username));
    }
}
