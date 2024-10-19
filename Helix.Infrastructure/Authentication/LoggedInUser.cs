using CommunityToolkit.Mvvm.ComponentModel;
using Helix.Application.Abstractions.Authentication;

namespace Helix.Infrastructure.Authentication;

internal sealed partial class LoggedInUser : ObservableObject, ILoggedInUser
{
    [ObservableProperty]
    private Guid _userId;

    [ObservableProperty]
    private string _username = string.Empty;

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
}
