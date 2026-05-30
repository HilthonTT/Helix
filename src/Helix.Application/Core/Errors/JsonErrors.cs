using SharedKernel;

namespace Helix.Application.Core.Errors;

public static class JsonErrors
{
    public static readonly Error Invalid = Error.Problem(
        "Json.Invalid", "Your json format is invalid.");

    public static readonly Error PassphraseMissing = Error.Problem(
        "Vault.PassphraseMissing", "A passphrase is required to encrypt or decrypt this export.");

    public static readonly Error PassphraseMismatch = Error.Problem(
        "Vault.PassphraseMismatch", "The two passphrase entries did not match.");

    public static readonly Error VaultDecryptFailed = Error.Problem(
        "Vault.DecryptFailed", "Invalid passphrase or corrupt vault file.");

    public static readonly Error VaultUnsupportedVersion = Error.Problem(
        "Vault.UnsupportedVersion", "This vault file was written by an unsupported version of Helix.");

    public static readonly Error VaultInvalidDriveData = Error.Problem(
        "Vault.InvalidDriveData", "The vault contains an invalid drive entry and cannot be imported.");
}
