using Helix.App.Models;

namespace Helix.App.Messaging.Drives;

internal sealed record DriveUpdatedMessage(DriveDisplay UpdatedDrive);
