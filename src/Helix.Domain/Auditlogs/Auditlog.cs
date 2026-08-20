namespace Helix.Domain.Auditlogs;

public sealed class Auditlog : Entity, IAuditable
{
    private Auditlog(
        Guid id,
        Guid userId,
        AuditAction action,
        Guid? entityId,
        string? entityName,
        string? entityLetter,
        string? detail,
        string? message)
        : base(id)
    {
        Ensure.NotNull(id, nameof(id));
        Ensure.NotNull(userId, nameof(userId));

        UserId = userId;
        Action = action;
        EntityId = entityId;
        EntityName = entityName;
        EntityLetter = entityLetter;
        Detail = detail;
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

    public AuditAction Action { get; private set; }

    /// <summary>The drive this entry is about, where there is one.</summary>
    /// <remarks>
    /// Not a foreign key. A deletion entry has to outlive the row it describes, and the
    /// whole point of the log is that it still reads correctly afterwards.
    /// </remarks>
    public Guid? EntityId { get; private set; }

    /// <summary>
    /// The drive's name as it was when this happened.
    /// </summary>
    /// <remarks>
    /// Copied rather than looked up. Renaming a drive must not rewrite what the log says
    /// happened to it last week, and a deleted drive has no name left to read.
    /// </remarks>
    public string? EntityName { get; private set; }

    /// <summary>The drive's letter at the time, for the same reason as the name.</summary>
    public string? EntityLetter { get; private set; }

    /// <summary>
    /// Extra context the action alone does not carry — the reason a reconnect failed,
    /// typically as the operating system reported it.
    /// </summary>
    public string? Detail { get; private set; }

    /// <summary>
    /// The full sentence, for entries written before the log was structured. Null on
    /// everything recorded since; those are composed at display time from
    /// <see cref="Action"/> and the entity fields.
    /// </summary>
    public string? Message { get; private set; }

    public DateTime CreatedOnUtc { get; set; }

    public DateTime? ModifiedOnUtc { get; set; }

    /// <summary>Records something that happened to a drive.</summary>
    public static Auditlog ForDrive(
        Guid userId,
        AuditAction action,
        Guid driveId,
        string driveName,
        string driveLetter,
        string? detail = null)
    {
        return new(
            Guid.CreateVersion7(),
            userId,
            action,
            driveId,
            driveName,
            driveLetter,
            detail,
            message: null);
    }
}
