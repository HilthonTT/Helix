namespace Helix.App.Messaging.Notifications;

internal static class NotificationText
{
    public static bool TryRemove(string text, string line, out string? remaining)
    {
        remaining = null;

        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        string[] lines = text.Split('\n');

        List<string> kept = [.. lines.Where(candidate =>
            !string.Equals(candidate.Trim('\r'), line, StringComparison.Ordinal))];

        if (kept.Count == lines.Length)
        {
            return false;
        }

        remaining = kept.Count == 0 ? null : string.Join(Environment.NewLine, kept.Select(l => l.Trim('\r')));

        return true;
    }
}
