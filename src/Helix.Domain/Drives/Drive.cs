namespace Helix.Domain.Drives;

public sealed class Drive : Entity, IAuditable
{
    private Drive(
        Guid id,
        Guid userId,
        string letter,
        string ipAddress,
        string name,
        string username,
        string password)
        : base(id)
    {
        Ensure.NotNullOrEmpty(id, nameof(id));
        Ensure.NotNullOrEmpty(userId, nameof(userId));
        Ensure.NotNullOrEmpty(letter, nameof(letter));
        Ensure.MustBeOneChar(letter, nameof(letter));
        Ensure.NotNullOrEmpty(ipAddress, nameof(ipAddress));
        Ensure.NotNullOrEmpty(name, nameof(name));
        Ensure.NotNullOrEmpty(username, nameof(username));
        Ensure.NotNullOrEmpty(password, nameof(password));

        UserId = userId;
        Letter = letter;
        IpAddress = ipAddress;
        Name = name;
        Username = username;
        Password = password;

        DateTime utcNow = DateTime.UtcNow;

        CreatedOnUtc = utcNow;
        ModifiedOnUtc = utcNow;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Drive"/> class.
    /// </summary>
    /// <remarks>
    /// Required by EF Core.
    /// </remarks>
    private Drive()
    {
        // EF Core materializes these from the database right after this constructor runs.
        Letter = null!;
        IpAddress = null!;
        Name = null!;
        Username = null!;
        Password = null!;
    }

    public Guid UserId { get; private set; }

    public string Letter { get; private set; }

    public string IpAddress { get; private set; }

    public string Name { get; private set; }

    public string Username { get; private set; }

    public string Password { get; private set; }

    public DateTime CreatedOnUtc { get; set; }

    public DateTime? ModifiedOnUtc { get; set; }

    public static Drive Create(
        Guid userId,
        string letter,
        string ipAddress,
        string name,
        string username,
        string password)
    {
        var drive = new Drive(
            Guid.NewGuid(),
            userId,
            letter.ToUpperInvariant(),
            ipAddress,
            name,
            username,
            password);

        return drive;
    }

    public void Update(string letter, string ipAddress, string name, string username, string password)
    {
        Ensure.NotNullOrEmpty(letter, nameof(letter));
        Ensure.MustBeOneChar(letter, nameof(letter));
        Ensure.NotNullOrEmpty(ipAddress, nameof(ipAddress));
        Ensure.NotNullOrEmpty(name, nameof(name));
        Ensure.NotNullOrEmpty(username, nameof(username));
        Ensure.NotNullOrEmpty(password, nameof(password));

        Letter = letter.ToUpperInvariant();
        IpAddress = ipAddress;
        Name = name;
        Username = username;
        Password = password;
    }
}
