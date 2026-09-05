namespace Helix.App.Messaging.Notifications;

/// <summary>
/// What a notification is telling the user, which is all the banner needs to pick its
/// glyph and colour.
/// </summary>
internal enum NotificationKind
{
    /// <summary>Something the user asked for happened.</summary>
    Success,

    /// <summary>Neutral information — nothing went wrong and nothing is required.</summary>
    Info,

    /// <summary>Something is not right but nothing failed outright.</summary>
    Warning,

    /// <summary>An operation failed.</summary>
    Error
}
