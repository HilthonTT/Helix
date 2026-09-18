namespace Helix.Domain.Drives;

public static class DriveErrors
{
    public static Error NotFound(Guid id) => Error.NotFound(
        "Drive.NotFound",
        $"The drive with the specified Id = '{id}' was not found.");

    public static Error LetterNotUnique(string letter) => Error.Conflict(
        "Drive.LetterNotUnique",
        $"A drive with the letter = '{letter}' already exists.");

    public static Error FailedToConnect(string message) => Error.Problem("Drive.FailedToConnect", message);

    public static Error HostUnreachable(string host) => Error.Problem(
        HostUnreachableCode,
        $"'{host}' could not be reached from this network.");

    public const string HostUnreachableCode = "Drive.HostUnreachable";

    public static Error AwayFromHomeNetwork(string? networkName) => Error.Problem(
        AwayFromHomeNetworkCode,
        networkName is null
            ? "This drive only reconnects on its home network, and this is not it."
            : $"This drive only reconnects on '{networkName}', and this is not it.");

    public const string AwayFromHomeNetworkCode = "Drive.AwayFromHomeNetwork";

    public static readonly Error ShareListingUnsupported = Error.Problem(
        "Drive.ShareListingUnsupported",
        "Listing a server's shares is not supported on this platform.");

    public static Error SharesNotListed(string message) => Error.Problem("Drive.SharesNotListed", message);

    public static readonly Error NothingToCreate = Error.Problem(
        "Drive.NothingToCreate",
        "Choose at least one share to add.");

    public static Error DuplicateLetter(string letter) => Error.Conflict(
        "Drive.DuplicateLetter",
        $"The drive letter '{letter}' was given to more than one share.");

    public static Error SessionConflict(string message) => Error.Conflict(SessionConflictCode, message);

    public const string SessionConflictCode = "Drive.SessionConflict";

    public static Error FailedToDisconnect(string message) => Error.Problem("Drive.FailedToDisconnect", message);

    public static readonly Error ConnectionTimedOut = FailedToConnect("Connection timed out.");

    public static readonly Error DisconnectionTimedOut = FailedToDisconnect("Disconnection timed out.");

    public static readonly Error MountNotAvailable = Error.NotFound(
        "Drive.MountNotAvailable",
        "That drive is not mounted, so there is nothing to open.");

    public static Error MountNotOpened(string message) => Error.Problem("Drive.MountNotOpened", message);

    public static Error LetterNotFound(string letter) => Error.NotFound(
        "Drive.LetterNotFound",
        $"The drive with the letter = '{letter}' was not found.");

    public static Error LetterInUse(string letter) => Error.Conflict(
        "Drive.LetterInUse",
        $"The drive letter '{letter}' is already in use on this computer. Choose a free one.");

    public static readonly Error NotALetter = Error.Problem(
        "Drive.NotALetter",
        "The 'letter' you've provided is not a single character.");

    public static readonly Error NoDrivesFound = Error.NotFound(
        "Drive.NoDrivesFound",
        "No drives have been found, please create some first.");
}
