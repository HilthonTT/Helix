using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Helix.App.Modals.Users.UpdateUsername;

internal sealed class UpdateUsernameMessage(bool value, string username) : ValueChangedMessage<bool>(value)
{
    public string Username { get; } = username;
}
