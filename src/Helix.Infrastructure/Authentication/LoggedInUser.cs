using CommunityToolkit.Mvvm.ComponentModel;
using Helix.Application.Abstractions.Authentication;

namespace Helix.Infrastructure.Authentication;

internal sealed partial class LoggedInUser : ObservableObject, ILoggedInUser
{
    public LoggedInUser()
    {
        // Partial properties cannot carry field initializers, so defaults are seeded here.
        Username = string.Empty;
    }

    [ObservableProperty]
    public partial Guid UserId { get; set; }

    [ObservableProperty]
    public partial string Username { get; set; }

    public bool IsLoggedIn { get; private set; }

    public void Login(Guid userId, string username)
    {
        UserId = userId;
        Username = username;

        IsLoggedIn = true;

        OnPropertyChanged(nameof(IsLoggedIn));
    }

    public void Logout()
    {
        IsLoggedIn = false;

        UserId = Guid.Empty;
        Username = string.Empty;

        OnPropertyChanged(nameof(IsLoggedIn));
    }

    public void Update(string username)
    {
        Username = username;
    }
}
