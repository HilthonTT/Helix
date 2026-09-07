namespace Helix.Domain.Users;

public sealed class User : Entity, IAuditable
{
    private User(Guid id, string username, string passwordHash)
        : base(id)
    {
        Ensure.NotNullOrEmpty(id, nameof(id));
        Ensure.NotNullOrEmpty(username, nameof(username));
        Ensure.NotNullOrEmpty(passwordHash, nameof(passwordHash));

        Username = username;
        PasswordHash = passwordHash;
    }

    private User()
    {
        Username = null!;
        PasswordHash = null!;
    }

    public string Username { get; private set; }

    public string PasswordHash { get; private set; }

    public DateTime CreatedOnUtc { get; set; }

    public DateTime? ModifiedOnUtc { get; set; }

    public static User Create(string username, string passwordHash)
    {
        var user = new User(Guid.CreateVersion7(), username, passwordHash);

        return user;
    }

    public void ChangePassword(string passwordHash)
    {
        PasswordHash = passwordHash;
    }

    public void Update(string username)
    {
        Username = username;
    }
}
