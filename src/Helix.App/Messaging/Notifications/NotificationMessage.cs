namespace Helix.App.Messaging.Notifications;

internal sealed class NotificationMessage(NotificationKind kind, string text)
{
    public NotificationKind Kind { get; } = kind;

    public string Text { get; } = text;

    public bool Handled { get; set; }
}
