namespace Helix.Domain.Auditlogs;

public enum AuditAction
{
    Legacy = 0,

    DriveCreated = 1,

    DriveUpdated = 2,

    DriveDeleted = 3,

    DriveDisconnected = 4,

    DriveReconnected = 5,

    DriveReconnectFailed = 6,
}
