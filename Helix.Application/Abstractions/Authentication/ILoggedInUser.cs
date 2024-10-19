namespace Helix.Application.Abstractions.Authentication;

public interface ILoggedInUser
{
    public Guid UserId { get; }

    public string Username { get; }

    public bool IsLoggedIn { get; }

    void Login(Guid userId, string username);

    void Logout();
}
