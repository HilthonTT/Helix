using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Helix.App.Messaging.Users;

internal sealed class UpdatePasswordMessage(bool value) : ValueChangedMessage<bool>(value);
