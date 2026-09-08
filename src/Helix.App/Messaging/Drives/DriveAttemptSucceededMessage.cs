namespace Helix.App.Messaging.Drives;

internal sealed record DriveAttemptSucceededMessage(Guid DriveId, DateTime ConnectedOnUtc);
