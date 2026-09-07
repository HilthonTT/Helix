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

    public static Error FailedToDisconnect(string message) => Error.Problem("Drive.FailedToDisconnect", message);

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
