using CommunityToolkit.Mvvm.Messaging.Messages;
using Helix.App.Models;

namespace Helix.App.Messaging.Drives;

internal sealed class DeleteDriveMessage(bool value, DriveDisplay? drive) : ValueChangedMessage<bool>(value)
{
    public DriveDisplay? Drive { get; } = drive;
}
