using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.Drives;
using Helix.Application.Abstractions.Security;

namespace Helix.App.Services;

internal sealed class PassphrasePromptService : IPassphrasePrompt
{
    public Task<string?> PromptForExportPassphraseAsync(CancellationToken cancellationToken = default) =>
        AskAsync(confirm: true);

    public Task<string?> PromptForImportPassphraseAsync(CancellationToken cancellationToken = default) =>
        AskAsync(confirm: false);

    private static async Task<string?> AskAsync(bool confirm)
    {
        PassphraseRequestMessage request = await MainThread.InvokeOnMainThreadAsync(
            () => WeakReferenceMessenger.Default.Send(new PassphraseRequestMessage(confirm)));

        if (!request.HasReceivedResponse)
        {
            return null;
        }

        string? passphrase = await request.Response;

        return string.IsNullOrWhiteSpace(passphrase) || IdleLockService.ScreenLocked ? null : passphrase;
    }
}
