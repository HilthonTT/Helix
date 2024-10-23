using SharedKernel;

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

    public static Error FailedToDisconnect(string message) => Error.Problem("Drive.FailedToDisconnect", message);

    public static Error LetterNotFound(string letter) => Error.NotFound(
        "Drive.LetterNotFound",
        $"The drive with the letter = '{letter}' was not found.");

    public static readonly Error NotALetter = Error.Problem(
        "Drive.NotALetter",
        "The 'letter' you've provided is not a single character.");
}
