namespace Helix.App.Messaging.Notifications;

/// <summary>
/// One line for the notification banner.
/// </summary>
/// <remarks>
/// A mutable <see cref="Handled"/> rather than a plain record because the sender has to
/// know whether anybody put it on screen. Every page keeps a
/// <see cref="Controls.NotificationHost"/>, but only the one the user is actually looking
/// at accepts a message — Shell caches the pages it has visited, so a plain broadcast
/// would leave a banner waiting on three pages the user is not on. When no host takes it,
/// <see cref="Services.Notifier"/> holds it until one does, which is also what carries a
/// failure raised mid-navigation across to the page that lands.
/// </remarks>
internal sealed class NotificationMessage(NotificationKind kind, string text)
{
    public NotificationKind Kind { get; } = kind;

    public string Text { get; } = text;

    /// <summary>Set by the host that displayed it.</summary>
    public bool Handled { get; set; }
}
