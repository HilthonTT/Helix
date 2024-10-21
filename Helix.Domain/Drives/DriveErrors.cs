using SharedKernel;

namespace Helix.Domain.Drives;

public static class DriveErrors
{
    public static Error LetterNotUnique(string letter) => Error.Problem(
        "Drive.LetterNotUnique",
        $"A drive with the letter = '{letter}' already exists.");

    public static Error FailedToConnect(string message) => Error.Problem("Drive.FailedToConnect", message);

    public static Error FailedToDisconnect(string message) => Error.Problem("Drive.FailedToDisconnect", message);

    public static Error LetterNotFound(string letter) => Error.Problem(
        "Drive.LetterNotFound",
        $"The drive with the letter = '{letter}' was not found.");
}
