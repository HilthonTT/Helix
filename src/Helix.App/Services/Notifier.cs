using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.Notifications;

namespace Helix.App.Services;

/// <summary>
/// Says something to the user without stopping them: the entry point every outcome that
/// is not a question goes through.
/// </summary>
/// <remarks>
/// It replaces the <c>DisplayAlert</c> that used to report every success and every
/// failure. Two things were wrong with that. A dialog for a success is a keystroke the
/// user owes the app for work they asked for and watched happen; and WinUI throws when a
/// second alert is raised while one is showing, which "connect all" over thirteen shares
/// walks straight into — <c>DriveTemplate</c> already carried a try/catch for exactly
/// that. Modal dialogs are kept for the cases that are genuinely a question: deleting a
/// group, installing an update, the export passphrase.
///
/// Everything is marshalled to the UI thread here rather than at each call site, because
/// most of the callers are background work — the watchdog, the storage sweep, an
/// <c>async void</c> handler — and none of them should have to know that.
/// </remarks>
internal static class Notifier
{
    /// <summary>
    /// How long a message with nowhere to go waits for a host to appear.
    /// </summary>
    /// <remarks>
    /// A failure raised while the app is navigating, or before the first page is up, has
    /// no banner to land in. Holding it means the user still sees it a moment later on
    /// the page that lands. Holding it indefinitely would mean an error from the sign-in
    /// page reappearing on the dashboard some minutes later, attached to nothing the user
    /// is doing, so it expires.
    /// </remarks>
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

    /// <summary>
    /// Hands over everything that found no host, newest last. Called by a host as it
    /// becomes the one on screen.
    /// </summary>
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
