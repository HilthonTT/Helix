namespace Helix.Application.Abstractions.Security;

/// <summary>
/// Prompts the user for an encryption passphrase. Implementations live in the
/// presentation layer because they need UI access.
/// </summary>
public interface IPassphrasePrompt
{
    /// <summary>
    /// Prompts the user twice (entry + confirmation) for a passphrase to encrypt an export.
    /// Returns <c>null</c> if the user cancels or the two entries do not match
    /// (the implementation is responsible for explaining the mismatch in-prompt).
    /// </summary>
    Task<string?> PromptForExportPassphraseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Prompts the user once for a passphrase to decrypt an import. Returns
    /// <c>null</c> if the user cancels.
    /// </summary>
    Task<string?> PromptForImportPassphraseAsync(CancellationToken cancellationToken = default);
}
