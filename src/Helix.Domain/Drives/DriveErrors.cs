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

    /// <summary>
    /// The NAS itself could not be reached, so nothing was attempted against the share.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="FailedToConnect"/> on purpose, and the code is what the
    /// unattended reconnect loop reads to tell "this share refused me" from "this network
    /// has no NAS on it" — the second is not a failure worth escalating a backoff over,
    /// or worth a warning in a log the user may be asked to send on.
    /// </remarks>
    public static Error HostUnreachable(string host) => Error.Problem(
        HostUnreachableCode,
        $"'{host}' could not be reached from this network.");

    /// <summary>
    /// Code carried by <see cref="HostUnreachable"/>, for callers that react to it rather
    /// than only reporting it.
    /// </summary>
    public const string HostUnreachableCode = "Drive.HostUnreachable";

    public static Error FailedToDisconnect(string message) => Error.Problem("Drive.FailedToDisconnect", message);

    /// <summary>
    /// The mount the user asked to open is not there — it was unmounted between the row
    /// being drawn and the click.
    /// </summary>
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
