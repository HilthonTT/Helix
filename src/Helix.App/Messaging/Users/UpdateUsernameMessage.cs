using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Helix.App.Messaging.Users;

internal sealed class UpdateUsernameMessage(bool value, string username) : ValueChangedMessage<bool>(value)
{
    public string Username { get; } = username;
}
