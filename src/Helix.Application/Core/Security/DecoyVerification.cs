using Helix.Application.Abstractions.Cryptography;

namespace Helix.Application.Core.Security;

/// <summary>
/// Spends what checking a real hash would, so a sign-in or a recovery for an account that does
/// not exist answers no faster than one for an account that does — the shared error message is
/// no use if the time it takes to arrive gives the answer away.
/// </summary>
public static class DecoyVerification
{
    private static string? _decoy;

    public static void VerifyDecoy(this IPasswordHasher passwordHasher, string secret)
    {
        string decoy = _decoy ??= passwordHasher.Hash(Guid.NewGuid().ToString("N"));

        _ = passwordHasher.Verify(secret, decoy);
    }
}
