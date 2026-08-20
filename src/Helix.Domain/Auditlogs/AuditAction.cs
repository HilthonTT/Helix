namespace Helix.Domain.Auditlogs;

/// <summary>
/// What an audit entry records.
/// </summary>
/// <remarks>
/// Entries used to be a single English sentence composed in the Infrastructure layer.
/// That made three things impossible at once: the log could not be translated in an app
/// that ships six languages, it could not be filtered by what happened, and searching it
/// was substring matching against prose. Storing the action and the entity separately
/// fixes all three — the sentence is now composed at display time, in the user's
/// language.
/// </remarks>
public enum AuditAction
{
    /// <summary>
    /// An entry written before the log was structured. Its text is in
    /// <see cref="Auditlog.Message"/> and is shown verbatim, in whatever language it was
    /// written in — rewriting history to fit the new shape would be a lie about what the
    /// app recorded at the time.
    /// </summary>
    Legacy = 0,

    DriveCreated = 1,

    DriveUpdated = 2,

    DriveDeleted = 3,

    /// <summary>The watchdog saw a mounted share go away.</summary>
    DriveDisconnected = 4,

    /// <summary>An unattended reconnect succeeded.</summary>
    DriveReconnected = 5,

    /// <summary>An unattended reconnect failed; the reason is in the detail.</summary>
    DriveReconnectFailed = 6,
}
