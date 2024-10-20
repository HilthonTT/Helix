using SharedKernel;

namespace Helix.Domain.Drives;

public static class DriveErrors
{
    public static Error LetterNotUnique(string letter) => Error.Problem(
        "Drive.LetterNotUnique",
        $"A drive with the letter = '{letter}' already exists.");
}
