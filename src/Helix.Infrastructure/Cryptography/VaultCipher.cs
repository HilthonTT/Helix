using Helix.Application.Abstractions.Security;
using Helix.Application.Core.Errors;
using SharedKernel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Helix.Infrastructure.Cryptography;

internal sealed class VaultCipher : IVaultCipher
{
    private const int CurrentVersion = 1;
    private const int SaltSize = 16;
    private const int NonceSize = 12; // AES-GCM standard
    private const int TagSize = 16;
    private const int KeySize = 32;   // AES-256
    private const int CurrentIterations = 600_000;
    private const int MaxAllowedIterations = 5_000_000;
    private const string Kdf = "PBKDF2-SHA512";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Encrypt(string plaintext, string passphrase)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentException.ThrowIfNullOrWhiteSpace(passphrase);

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, CurrentIterations, HashAlgorithmName.SHA512, KeySize);

        byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] ciphertext = new byte[plaintextBytes.Length];
        byte[] tag = new byte[TagSize];

        try
        {
            using var gcm = new AesGcm(key, TagSize);
            gcm.Encrypt(nonce, plaintextBytes, ciphertext, tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }

        var envelope = new VaultEnvelope
        {
            Version = CurrentVersion,
            Kdf = Kdf,
            Iterations = CurrentIterations,
            Salt = Convert.ToBase64String(salt),
            Nonce = Convert.ToBase64String(nonce),
            Ciphertext = Convert.ToBase64String(ciphertext),
            Tag = Convert.ToBase64String(tag),
        };

        return JsonSerializer.Serialize(envelope, JsonOptions);
    }

    public Result<string> Decrypt(string vaultPayload, string passphrase)
    {
        if (string.IsNullOrWhiteSpace(vaultPayload) || string.IsNullOrWhiteSpace(passphrase))
        {
            return Result.Failure<string>(JsonErrors.VaultDecryptFailed);
        }

        VaultEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<VaultEnvelope>(vaultPayload, JsonOptions);
        }
        catch (JsonException)
        {
            return Result.Failure<string>(JsonErrors.VaultDecryptFailed);
        }

        if (envelope is null)
        {
            return Result.Failure<string>(JsonErrors.VaultDecryptFailed);
        }

        if (envelope.Version != CurrentVersion || !string.Equals(envelope.Kdf, Kdf, StringComparison.Ordinal))
        {
            return Result.Failure<string>(JsonErrors.VaultUnsupportedVersion);
        }

        if (envelope.Iterations < 1 || envelope.Iterations > MaxAllowedIterations)
        {
            return Result.Failure<string>(JsonErrors.VaultUnsupportedVersion);
        }

        byte[] salt, nonce, ciphertext, tag;
        try
        {
            salt = Convert.FromBase64String(envelope.Salt ?? string.Empty);
            nonce = Convert.FromBase64String(envelope.Nonce ?? string.Empty);
            ciphertext = Convert.FromBase64String(envelope.Ciphertext ?? string.Empty);
            tag = Convert.FromBase64String(envelope.Tag ?? string.Empty);
        }
        catch (FormatException)
        {
            return Result.Failure<string>(JsonErrors.VaultDecryptFailed);
        }

        if (salt.Length != SaltSize || nonce.Length != NonceSize || tag.Length != TagSize)
        {
            return Result.Failure<string>(JsonErrors.VaultDecryptFailed);
        }

        byte[] key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, envelope.Iterations, HashAlgorithmName.SHA512, KeySize);
        byte[] plaintextBytes = new byte[ciphertext.Length];

        try
        {
            using var gcm = new AesGcm(key, TagSize);
            gcm.Decrypt(nonce, ciphertext, tag, plaintextBytes);
        }
        catch (AuthenticationTagMismatchException)
        {
            CryptographicOperations.ZeroMemory(key);
            return Result.Failure<string>(JsonErrors.VaultDecryptFailed);
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(key);
            return Result.Failure<string>(JsonErrors.VaultDecryptFailed);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        string plaintext = Encoding.UTF8.GetString(plaintextBytes);
        CryptographicOperations.ZeroMemory(plaintextBytes);
        return plaintext;
    }

    private sealed class VaultEnvelope
    {
        [JsonPropertyName("v")] 
        public int Version { get; set; }

        [JsonPropertyName("kdf")] 
        public string? Kdf { get; set; }

        [JsonPropertyName("iters")]
        public int Iterations { get; set; }

        [JsonPropertyName("salt")] 
        public string? Salt { get; set; }

        [JsonPropertyName("nonce")]
        public string? Nonce { get; set; }

        [JsonPropertyName("ct")] 
        public string? Ciphertext { get; set; }

        [JsonPropertyName("tag")]
        public string? Tag { get; set; }
    }
}
