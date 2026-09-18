using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Helix.App.Messaging.Drives;

internal sealed class CreateDriveMessage(bool value, Guid? templateDriveId = null) : ValueChangedMessage<bool>(value)
{
    public Guid? TemplateDriveId { get; } = templateDriveId;
}
