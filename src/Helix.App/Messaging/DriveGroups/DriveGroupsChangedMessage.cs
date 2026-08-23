namespace Helix.App.Messaging.DriveGroups;

/// <summary>
/// A group was created, renamed, re-membered or deleted.
/// </summary>
/// <remarks>
/// Carries nothing: every listener — the dashboard's strip and the tray's menu — rebuilds
/// its whole list from the store rather than patching one entry, which is the same shape
/// the drive messages settled on and for the same reason.
/// </remarks>
internal sealed record DriveGroupsChangedMessage;
