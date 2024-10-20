using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Helix.App.Modals.Drives.Create;

internal sealed class CreateDriveMessage(bool value) : ValueChangedMessage<bool>(value);
