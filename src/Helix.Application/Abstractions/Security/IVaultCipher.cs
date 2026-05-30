using SharedKernel;

namespace Helix.Application.Abstractions.Security;

/// <summary>
/// Encrypts and decrypts text payloads with a user-supplied passphrase.
/// Implementations must use an authenticated cipher (e.g. AES-GCM) so that
/// tampering and wrong passphrases are indistinguishable to the caller.
/// </summary>
public interface IVaultCipher
{
    /// <summary>
    /// Encrypts <paramref name="plaintext"/> with <paramref name="passphrase"/>.
    /// Returns a self-describing string envelope (algorithm / iteration / nonce / etc.).
    /// </summary>
    string Encrypt(string plaintext, string passphrase);

    /// <summary>
    /// Decrypts a payload produced by <see cref="Encrypt"/>. A wrong passphrase or
    /// tampered payload returns a failure result — no exception is thrown and no
    /// distinguishing information is leaked.
    /// </summary>
    Result<string> Decrypt(string vaultPayload, string passphrase);
}
