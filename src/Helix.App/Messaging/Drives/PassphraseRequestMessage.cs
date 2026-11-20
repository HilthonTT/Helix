using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Helix.App.Messaging.Drives;

internal sealed class PassphraseRequestMessage(bool confirm) : AsyncRequestMessage<string?>
{
    public bool Confirm { get; } = confirm;
}
