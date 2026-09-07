namespace Helix.Application.Abstractions.Security;

public interface IVaultCipher
{
    string Encrypt(string plaintext, string passphrase);

    Result<string> Decrypt(string vaultPayload, string passphrase);
}
