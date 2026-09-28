using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Helix.App.Messaging.Users;

internal sealed class CreateRecoveryKeyMessage(bool value) : ValueChangedMessage<bool>(value);
