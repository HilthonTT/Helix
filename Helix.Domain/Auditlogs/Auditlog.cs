using SharedKernel;

namespace Helix.Domain.Auditlogs;

public sealed class Auditlog : Entity, IAuditable
{
    private Auditlog(Guid id, Guid userId, string message) 
        : base(id)
    {
        Ensure.NotNull(id, nameof(id));
        Ensure.NotNull(userId, nameof(userId));

        UserId = userId;
        Message = message;

        DateTime utcNow = DateTime.UtcNow;

        CreatedOnUtc = utcNow;
        ModifiedOnUtc = utcNow;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Auditlog"/> class.
    /// </summary>
    /// <remarks>
    /// Required by EF Core.
    /// </remarks>
    private Auditlog()
    {
    }

    public Guid UserId { get; private set; }

    public string Message { get; private set; }

    public DateTime CreatedOnUtc { get; set; }

    public DateTime? ModifiedOnUtc { get; set; }

    public static Auditlog Create(Guid userId, string message)
    {
        return new(Guid.NewGuid(), userId, message);
    }
}
