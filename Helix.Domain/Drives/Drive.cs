using SharedKernel;
using System.Text.Json.Serialization;

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
        string password,
        bool validateUserId = true)
        : base(id)
    {
        Ensure.NotNullOrEmpty(id, nameof(id));

        if (validateUserId)
        {
            Ensure.NotNullOrEmpty(userId, nameof(userId));
        }

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
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Drive"/> class.
    /// </summary>
    /// <remarks>
    /// Required by EF Core.
    /// </remarks>
    private Drive()
    {
    }

    [JsonConstructor]
    public Drive(
        Guid id,
        Guid userId,
        string letter,
        string ipAddress,
        string name,
        string username,
        string password,
        DateTime createdOnUtc,
        DateTime? modifiedOnUtc) : base(id)
    {
        UserId = userId;
        Letter = letter;
        IpAddress = ipAddress;
        Name = name;
        Username = username;
        Password = password;
        CreatedOnUtc = createdOnUtc;
        ModifiedOnUtc = modifiedOnUtc;
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
            letter.ToUpper(),
            ipAddress,
            name,
            username,
            password);

        return drive;
    }

    public static Drive MapWithoutUserId(Drive drive)
    {
        var driveWithoutUserId = new Drive(
            drive.Id,
            Guid.Empty,
            drive.Letter,
            drive.IpAddress,
            drive.Name,
            drive.Username,
            drive.Password,
            validateUserId: false);

        return driveWithoutUserId;
    }

    public void Update(string letter, string ipAddress, string name, string username, string password)
    {
        Letter = letter;
        IpAddress = ipAddress;
        Name = name;
        Username = username;
        Password = password;
    }
}
