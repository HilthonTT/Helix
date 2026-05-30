using Helix.Application.Abstractions.Security;

namespace Helix.App.Services;

/// <summary>
/// Default implementation of <see cref="IPassphrasePrompt"/> using
/// <see cref="Shell.Current"/> alert dialogs. Lives in the presentation layer
/// because it touches MAUI UI.
/// </summary>
internal sealed class PassphrasePromptService : IPassphrasePrompt
{
    private const int MaxLength = 256;

    public async Task<string?> PromptForExportPassphraseAsync(CancellationToken cancellationToken = default)
    {
        Shell? shell = Shell.Current;
        if (shell is null)
        {
            return null;
        }

        string? first = await shell.DisplayPromptAsync(
            title: "Encrypt export",
            message: "Enter a passphrase to protect this drive export.",
            accept: "Continue",
            cancel: "Cancel",
            placeholder: "Passphrase",
            maxLength: MaxLength,
            keyboard: Keyboard.Text);

        if (string.IsNullOrWhiteSpace(first))
        {
            return null;
        }

        string? second = await shell.DisplayPromptAsync(
            title: "Encrypt export",
            message: "Re-enter the same passphrase to confirm.",
            accept: "Encrypt",
            cancel: "Cancel",
            placeholder: "Confirm passphrase",
            maxLength: MaxLength,
            keyboard: Keyboard.Text);

        if (string.IsNullOrWhiteSpace(second))
        {
            return null;
        }

        if (!string.Equals(first, second, StringComparison.Ordinal))
        {
            await shell.DisplayAlert(
                "Passphrases don't match",
                "The two passphrases you entered are different. Please try again.",
                "Ok");
            return null;
        }

        return first;
    }

    public async Task<string?> PromptForImportPassphraseAsync(CancellationToken cancellationToken = default)
    {
        Shell? shell = Shell.Current;
        if (shell is null)
        {
            return null;
        }

        string? passphrase = await shell.DisplayPromptAsync(
            title: "Decrypt import",
            message: "Enter the passphrase used when this vault was exported.",
            accept: "Decrypt",
            cancel: "Cancel",
            placeholder: "Passphrase",
            maxLength: MaxLength,
            keyboard: Keyboard.Text);

        return string.IsNullOrWhiteSpace(passphrase) ? null : passphrase;
    }
}
