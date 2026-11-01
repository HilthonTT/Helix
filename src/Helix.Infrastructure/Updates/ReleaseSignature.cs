using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace Helix.Infrastructure.Updates;

internal static class ReleaseSignature
{
    public static bool IsRequired => !string.IsNullOrWhiteSpace(UpdateConfiguration.SigningPublicKey);

    public static bool Verify(byte[] archiveHash, string signature, ILogger logger) =>
        Verify(archiveHash, signature, UpdateConfiguration.SigningPublicKey, logger);

    public static string? FindSignature(string manifest, string assetName)
    {
        foreach (string line in manifest.Split('\n'))
        {
            string trimmed = line.Trim();

            if (trimmed.Length == 0 || trimmed[0] == '#')
            {
                continue;
            }

            int split = trimmed.LastIndexOf(' ');

            if (split <= 0)
            {
                continue;
            }

            if (string.Equals(trimmed[..split].Trim(), assetName, StringComparison.Ordinal))
            {
                return trimmed[(split + 1)..];
            }
        }

        return null;
    }

    internal static bool Verify(byte[] archiveHash, string signature, string key, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return true;
        }

        byte[] der;

        try
        {
            der = Convert.FromBase64String(signature.Trim());
        }
        catch (FormatException ex)
        {
            logger.LogError(ex, "The signature published for the update was not readable.");

            return false;
        }

        try
        {
            using var ecdsa = ECDsa.Create();

            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(key), out _);

            return ecdsa.VerifyHash(archiveHash, der, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or NotSupportedException)
        {
            logger.LogError(ex, "The signature on the update could not be checked.");

            return false;
        }
    }
}
