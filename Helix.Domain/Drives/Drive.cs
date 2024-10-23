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
        string password)
        : base(id)
    {
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

    public void Update(string letter, string ipAddress, string name, string username, string password)
    {
        Letter = letter;
        IpAddress = ipAddress;
        Name = name;
        Username = username;
        Password = password;
    }
}
