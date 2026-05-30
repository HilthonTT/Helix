namespace Helix.Application.Abstractions.Cryptography;

public interface IPasswordHasher
{
    /// <summary>
    /// Hashes <paramref name="password"/> with the current KDF parameters.
    /// </summary>
    string Hash(string password);

    /// <summary>
    /// Verifies <paramref name="password"/> against <paramref name="passwordHash"/>.
    /// Accepts both the current versioned format and any legacy formats.
    /// Returns <c>false</c> on any parse failure — never throws.
    /// </summary>
    bool Verify(string password, string passwordHash);

    /// <summary>
    /// Returns <c>true</c> if <paramref name="passwordHash"/> was produced by an
    /// older format or weaker KDF parameters than the current configuration, so
    /// the caller should re-hash the user's password and persist the new value.
    /// </summary>
    bool NeedsRehash(string passwordHash);
}
