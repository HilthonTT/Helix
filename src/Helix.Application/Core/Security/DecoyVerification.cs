using Helix.Application.Abstractions.Cryptography;

namespace Helix.Application.Core.Security;

public static class DecoyVerification
{
    private static string? _decoy;

    public static void VerifyDecoy(this IPasswordHasher passwordHasher, string secret)
    {
        string decoy = _decoy ??= passwordHasher.Hash(Guid.NewGuid().ToString("N"));

        _ = passwordHasher.Verify(secret, decoy);
    }
}
