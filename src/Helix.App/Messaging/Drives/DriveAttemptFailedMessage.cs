namespace Helix.App.Messaging.Drives;

internal sealed record DriveAttemptFailedMessage(Guid DriveId, Error Error);
