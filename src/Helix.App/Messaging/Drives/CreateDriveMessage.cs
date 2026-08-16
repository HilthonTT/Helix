using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Helix.App.Messaging.Drives;

internal sealed class CreateDriveMessage(bool value) : ValueChangedMessage<bool>(value);
