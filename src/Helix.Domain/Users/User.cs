using SharedKernel;

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

    /// <summary>
    /// Initializes a new instance of the <see cref="User"/> class.
    /// </summary>
    /// <remarks>
    /// Required by EF Core.
    /// </remarks>
    private User()
    {
    }

    public string Username { get; private set; }

    public string PasswordHash { get; private set; }

    public DateTime CreatedOnUtc { get; set; }

    public DateTime? ModifiedOnUtc { get; set; }

    public static User Create(string username, string passwordHash)
    {
        var user = new User(Guid.NewGuid(), username, passwordHash);

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
