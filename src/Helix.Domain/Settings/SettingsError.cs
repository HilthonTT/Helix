using SharedKernel;

namespace Helix.Domain.Settings;

public static class SettingsError
{
    public static readonly Error NotFound = Error.NotFound(
        "Settings.NotFound",
        "We've found no settings for you.");

    public static readonly Error TimerCountMustBePositive = Error.Problem(
        "Settings.TimerCountMustBePositive",
        "The timer count must a positive number.");
}
