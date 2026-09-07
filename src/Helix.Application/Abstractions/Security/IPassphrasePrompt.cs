namespace Helix.Application.Abstractions.Security;

public interface IPassphrasePrompt
{
    Task<string?> PromptForExportPassphraseAsync(CancellationToken cancellationToken = default);

    Task<string?> PromptForImportPassphraseAsync(CancellationToken cancellationToken = default);
}
