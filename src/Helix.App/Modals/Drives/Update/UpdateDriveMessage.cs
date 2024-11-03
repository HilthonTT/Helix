using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Helix.App.Modals.Drives.Update;

internal sealed class UpdateDriveMessage(bool value, Guid driveId) : ValueChangedMessage<bool>(value)
{
    public Guid DriveId { get; init; } = driveId;
}
