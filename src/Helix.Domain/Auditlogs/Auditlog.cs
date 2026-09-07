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

    private Auditlog()
    {
    }

    public Guid UserId { get; private set; }

    public AuditAction Action { get; private set; }

    public Guid? EntityId { get; private set; }

    public string? EntityName { get; private set; }

    public string? EntityLetter { get; private set; }

    public string? Detail { get; private set; }

    public string? Message { get; private set; }

    public DateTime CreatedOnUtc { get; set; }

    public DateTime? ModifiedOnUtc { get; set; }

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
