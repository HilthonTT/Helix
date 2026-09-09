using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace Helix.Infrastructure.Updates;

/// <summary>
/// The half the digest could not prove.
///
/// GitHub's published SHA-256 says the bytes that came down are the bytes the API
/// described; it says nothing about who built them, because both come from the same
/// place. A detached signature over that same hash, checked against a key compiled into
/// this build, is what answers that — and the private half of it lives in the release
/// workflow's secrets rather than in the repository.
///
/// ECDSA over P-256 rather than Ed25519 because the BCL has it: an update path is the
/// last place to take a dependency on a third-party crypto library.
/// </summary>
internal static class ReleaseSignature
{
    /// <summary>
    /// Whether this build checks signatures at all. Empty means it does not, which is
    /// what every build before the release workflow started signing has to keep doing —
    /// a build that demanded a signature no published release carries could not update
    /// itself at all. Once a key is set, a missing or wrong signature is fatal: leniency
    /// there would make the check decorative.
    /// </summary>
    public static bool IsRequired => !string.IsNullOrWhiteSpace(UpdateConfiguration.SigningPublicKey);

    public static bool Verify(byte[] archiveHash, string signature, ILogger logger) =>
        Verify(archiveHash, signature, UpdateConfiguration.SigningPublicKey, logger);

    /// <summary>
    /// Pulls one archive's signature out of the release manifest, whose lines are
    /// `&lt;asset name&gt; &lt;base64 signature&gt;`. Keyed by name so the wrong
    /// architecture's signature can never be the one checked, and blank or malformed
    /// lines are skipped rather than failing the file — the answer that matters is
    /// whether *this* asset is in it.
    /// </summary>
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
