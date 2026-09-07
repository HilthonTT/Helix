using Helix.App.Resources.Languages;
using Helix.Application.Abstractions.Security;

namespace Helix.App.Services;

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
            title: AppResources.EncryptExport,
            message: AppResources.EncryptExportMessage,
            accept: AppResources.Continue,
            cancel: AppResources.Cancel,
            placeholder: AppResources.Passphrase,
            maxLength: MaxLength,
            keyboard: Keyboard.Text);

        if (string.IsNullOrWhiteSpace(first))
        {
            return null;
        }

        string? second = await shell.DisplayPromptAsync(
            title: AppResources.EncryptExport,
            message: AppResources.EncryptExportConfirmMessage,
            accept: AppResources.Encrypt,
            cancel: AppResources.Cancel,
            placeholder: AppResources.ConfirmPassphrase,
            maxLength: MaxLength,
            keyboard: Keyboard.Text);

        if (string.IsNullOrWhiteSpace(second))
        {
            return null;
        }

        if (!string.Equals(first, second, StringComparison.Ordinal))
        {
            await shell.DisplayAlertAsync(
                AppResources.PassphrasesDontMatch,
                AppResources.PassphrasesDontMatchMessage,
                AppResources.Ok);
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
            title: AppResources.DecryptImport,
            message: AppResources.DecryptImportMessage,
            accept: AppResources.Decrypt,
            cancel: AppResources.Cancel,
            placeholder: AppResources.Passphrase,
            maxLength: MaxLength,
            keyboard: Keyboard.Text);

        return string.IsNullOrWhiteSpace(passphrase) ? null : passphrase;
    }
}
