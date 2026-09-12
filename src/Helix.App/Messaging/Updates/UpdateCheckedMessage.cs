namespace Helix.App.Messaging.Updates;

internal sealed record UpdateCheckedMessage(bool IsUpdateAvailable, string LatestVersion);
