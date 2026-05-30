using Helix.Application.Abstractions.Cryptography;
using System.Security.Cryptography;

namespace Helix.Infrastructure.Cryptography;

internal sealed class PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int CurrentIterations = 600_000; // OWASP 2023 recommendation for PBKDF2-SHA512
    private const int LegacyIterations = 100_000;
    private const string V2Prefix = "v2$";
    private const char V2Separator = '$';
    private const char LegacySeparator = '-';

    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA512;

    public string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, CurrentIterations, Algorithm, HashSize);

        return $"{V2Prefix}{CurrentIterations}{V2Separator}{Convert.ToHexString(hash)}{V2Separator}{Convert.ToHexString(salt)}";
    }

    public bool Verify(string password, string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            return false;
        }

        if (!TryParse(passwordHash, out int iterations, out byte[]? expectedHash, out byte[]? salt))
        {
            return false;
        }

        byte[] inputHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, Algorithm, HashSize);

        return CryptographicOperations.FixedTimeEquals(expectedHash, inputHash);
    }

    public bool NeedsRehash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            // Caller will fail verification anyway; nothing to migrate.
            return false;
        }

        if (!passwordHash.StartsWith(V2Prefix, StringComparison.Ordinal))
        {
            // Legacy format — always rehash.
            return true;
        }

        return !TryParse(passwordHash, out int iterations, out _, out _) || iterations < CurrentIterations;
    }

    private static bool TryParse(string passwordHash, out int iterations, out byte[] hash, out byte[] salt)
    {
        iterations = 0;
        hash = [];
        salt = [];

        try
        {
            if (passwordHash.StartsWith(V2Prefix, StringComparison.Ordinal))
            {
                // v2$<iters>$<hexhash>$<hexsalt>
                string[] parts = passwordHash[V2Prefix.Length..].Split(V2Separator);
                if (parts.Length != 3)
                {
                    return false;
                }

                if (!int.TryParse(parts[0], out iterations) || iterations < 1)
                {
                    return false;
                }

                hash = Convert.FromHexString(parts[1]);
                salt = Convert.FromHexString(parts[2]);
            }
            else
            {
                // Legacy format: <hexhash>-<hexsalt>
                string[] parts = passwordHash.Split(LegacySeparator);
                if (parts.Length != 2)
                {
                    return false;
                }

                iterations = LegacyIterations;
                hash = Convert.FromHexString(parts[0]);
                salt = Convert.FromHexString(parts[1]);
            }

            return hash.Length == HashSize && salt.Length == SaltSize;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
