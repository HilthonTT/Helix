namespace Helix.Domain.Drives;

public sealed class Drive : Entity, IAuditable
{
    private Drive(
        Guid id,
        Guid userId,
        string letter,
        string host,
        string name,
        string username,
        string password,
        bool autoConnect,
        bool persistent,
        bool connectByHostname)
        : base(id)
    {
        Ensure.NotNullOrEmpty(id, nameof(id));
        Ensure.NotNullOrEmpty(userId, nameof(userId));
        Ensure.NotNullOrEmpty(letter, nameof(letter));
        Ensure.MustBeOneChar(letter, nameof(letter));
        Ensure.NotNullOrEmpty(host, nameof(host));
        Ensure.NotNullOrEmpty(name, nameof(name));
        Ensure.NotNullOrEmpty(username, nameof(username));
        Ensure.NotNullOrEmpty(password, nameof(password));

        UserId = userId;
        Letter = letter;
        Host = host.Trim();
        Name = name;
        Username = username;
        Password = password;
        AutoConnect = autoConnect;
        Persistent = persistent;
        ConnectByHostname = connectByHostname;

        DateTime utcNow = DateTime.UtcNow;

        CreatedOnUtc = utcNow;
        ModifiedOnUtc = utcNow;
    }

    private Drive()
    {
        Letter = null!;
        Host = null!;
        Name = null!;
        Username = null!;
        Password = null!;
    }

    public Guid UserId { get; private set; }

    public string Letter { get; private set; }

    public string Host { get; private set; }

    public string Name { get; private set; }

    public string Username { get; private set; }

    public string Password { get; private set; }

    public bool AutoConnect { get; private set; }

    public bool Persistent { get; private set; }

    public bool ConnectByHostname { get; private set; }

    public DateTime? LastConnectedOnUtc { get; private set; }

    public DateTime CreatedOnUtc { get; set; }

    public DateTime? ModifiedOnUtc { get; set; }

    public void MarkConnected(DateTime utcNow)
    {
        LastConnectedOnUtc = utcNow;
    }

    public static Drive Create(
        Guid userId,
        string letter,
        string host,
        string name,
        string username,
        string password,
        bool autoConnect = true,
        bool persistent = false,
        bool connectByHostname = false)
    {
        var drive = new Drive(
            Guid.CreateVersion7(),
            userId,
            letter.ToUpperInvariant(),
            host,
            name,
            username,
            password,
            autoConnect,
            persistent,
            connectByHostname);

        return drive;
    }

    public void Update(
        string letter,
        string host,
        string name,
        string username,
        string password,
        bool autoConnect,
        bool persistent,
        bool connectByHostname)
    {
        Ensure.NotNullOrEmpty(letter, nameof(letter));
        Ensure.MustBeOneChar(letter, nameof(letter));
        Ensure.NotNullOrEmpty(host, nameof(host));
        Ensure.NotNullOrEmpty(name, nameof(name));
        Ensure.NotNullOrEmpty(username, nameof(username));
        Ensure.NotNullOrEmpty(password, nameof(password));

        Letter = letter.ToUpperInvariant();
        Host = host.Trim();
        Name = name;
        Username = username;
        Password = password;
        AutoConnect = autoConnect;
        Persistent = persistent;
        ConnectByHostname = connectByHostname;
    }
}
