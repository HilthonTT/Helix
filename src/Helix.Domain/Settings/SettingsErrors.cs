namespace Helix.Domain.Settings;

public static class SettingsErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "Settings.NotFound",
        "We've found no settings for you.");

    public static readonly Error TimerCountMustBePositive = Error.Problem(
        "Settings.TimerCountMustBePositive",
        "The timer count must a positive number.");

    public static readonly Error RetentionMustNotBeNegative = Error.Problem(
        "Settings.RetentionMustNotBeNegative",
        "The audit log retention must be zero or more days. Zero keeps every entry.");

    public static Error ShortcutUpdateFailed(string message) => Error.Problem(
        "Settings.ShortcutUpdateFailed",
        $"Failed to update the startup/desktop shortcut: {message}");
}
