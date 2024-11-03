using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Helix.App.Modals.Users.UpdatePassword;

internal sealed class UpdatePasswordMessage(bool value) : ValueChangedMessage<bool>(value);
