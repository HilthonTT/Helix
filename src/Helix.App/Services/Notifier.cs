using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.Notifications;

namespace Helix.App.Services;

internal static class Notifier
{
    private static readonly TimeSpan HoldFor = TimeSpan.FromSeconds(30);

    private static readonly Lock Gate = new();
    private static readonly List<Held> Undelivered = [];

    public static void Success(string text) => Send(NotificationKind.Success, text);

    public static void Info(string text) => Send(NotificationKind.Info, text);

    public static void Warning(string text) => Send(NotificationKind.Warning, text);

    public static void Error(string text) => Send(NotificationKind.Error, text);

    public static void Error(Error error) => Send(NotificationKind.Error, error.Description);

    public static void Send(NotificationKind kind, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var message = new NotificationMessage(kind, text);

            WeakReferenceMessenger.Default.Send(message);

            if (message.Handled)
            {
                return;
            }

            lock (Gate)
            {
                Undelivered.Add(new Held(message, DateTime.UtcNow));
            }
        });
    }

    public static IReadOnlyList<NotificationMessage> DrainHeld()
    {
        lock (Gate)
        {
            if (Undelivered.Count == 0)
            {
                return [];
            }

            DateTime cutoff = DateTime.UtcNow - HoldFor;

            List<NotificationMessage> live = [.. Undelivered
                .Where(held => held.HeldSinceUtc >= cutoff)
                .Select(held => held.Message)];

            Undelivered.Clear();

            return live;
        }
    }

    private readonly record struct Held(NotificationMessage Message, DateTime HeldSinceUtc);
}
