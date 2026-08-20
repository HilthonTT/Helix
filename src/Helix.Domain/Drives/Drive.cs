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
        bool persistent)
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
        Host = host;
        Name = name;
        Username = username;
        Password = password;
        AutoConnect = autoConnect;
        Persistent = persistent;

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
        Host = null!;
        Name = null!;
        Username = null!;
        Password = null!;
    }

    public Guid UserId { get; private set; }

    public string Letter { get; private set; }

    /// <summary>
    /// Where the share lives: an IPv4 address, an IPv6 address, or a hostname —
    /// <c>192.168.0.10</c>, <c>fd00::5</c>, <c>nas.local</c> or plain <c>MYNAS</c>.
    /// </summary>
    /// <remarks>
    /// This was <c>IpAddress</c> and accepted a dotted quad only, which meant a NAS
    /// reached by name — the normal case on a home network, and the only stable one
    /// when the server's lease moves — could not be entered at all. The connectors
    /// each render this into their platform's own form.
    /// </remarks>
    public string Host { get; private set; }

    public string Name { get; private set; }

    public string Username { get; private set; }

    public string Password { get; private set; }

    /// <summary>
    /// Whether this drive takes part in unattended connecting: the startup pass and the
    /// watchdog's reconnect-after-a-drop. Off means the drive is only ever connected
    /// when the user asks for it by name.
    /// </summary>
    /// <remarks>
    /// The user-level <c>Settings.AutoConnect</c> stays the master switch; this narrows
    /// it per drive. Both must be on for a drive to be connected unattended. Pressing
    /// "connect all" is an explicit request and ignores this flag entirely.
    /// </remarks>
    public bool AutoConnect { get; private set; }

    /// <summary>
    /// Whether the mapping is written into the Windows user profile, so Explorer
    /// restores it at sign-in without Helix running. Ignored on macOS, which has no
    /// equivalent of a remembered mapping.
    /// </summary>
    public bool Persistent { get; private set; }

    public DateTime CreatedOnUtc { get; set; }

    public DateTime? ModifiedOnUtc { get; set; }

    public static Drive Create(
        Guid userId,
        string letter,
        string host,
        string name,
        string username,
        string password,
        bool autoConnect = true,
        bool persistent = false)
    {
        var drive = new Drive(
            Guid.NewGuid(),
            userId,
            letter.ToUpperInvariant(),
            host,
            name,
            username,
            password,
            autoConnect,
            persistent);

        return drive;
    }

    /// <remarks>
    /// Every field is required, deliberately — no defaults on the two flags. An update
    /// replaces the whole drive, so a caller that forgot to pass them would silently
    /// reset the user's choices rather than leave them alone.
    /// </remarks>
    public void Update(
        string letter,
        string host,
        string name,
        string username,
        string password,
        bool autoConnect,
        bool persistent)
    {
        Ensure.NotNullOrEmpty(letter, nameof(letter));
        Ensure.MustBeOneChar(letter, nameof(letter));
        Ensure.NotNullOrEmpty(host, nameof(host));
        Ensure.NotNullOrEmpty(name, nameof(name));
        Ensure.NotNullOrEmpty(username, nameof(username));
        Ensure.NotNullOrEmpty(password, nameof(password));

        Letter = letter.ToUpperInvariant();
        Host = host;
        Name = name;
        Username = username;
        Password = password;
        AutoConnect = autoConnect;
        Persistent = persistent;
    }
}
