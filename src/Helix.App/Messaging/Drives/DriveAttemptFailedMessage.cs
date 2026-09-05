namespace Helix.App.Messaging.Drives;

/// <summary>
/// An unattended reconnect attempt failed, and why.
/// </summary>
/// <remarks>
/// Separate from <see cref="NotifyDriveConnectivityMessage"/>, which only says "look
/// again": the row can see for itself that a drive is not mounted, and what it cannot see
/// is the reason. Carries the error code rather than a decided-on reason so the mapping
/// lives with the row that renders it, next to the same mapping the manual connect path
/// does.
/// </remarks>
/// <param name="ErrorCode">The failing <see cref="Error.Code"/>.</param>
/// <param name="Description">What to show the user when they ask what happened.</param>
internal sealed record DriveAttemptFailedMessage(Guid DriveId, string ErrorCode, string Description);
