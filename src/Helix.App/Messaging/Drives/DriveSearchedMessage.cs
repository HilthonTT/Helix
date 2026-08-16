using Helix.Domain.Drives;

namespace Helix.App.Messaging.Drives;

internal sealed record DriveSearchedMessage(List<Drive> SearchedDrives);
