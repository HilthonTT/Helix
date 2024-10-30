using Helix.Domain.Drives;

namespace Helix.App.Modals.Drives.Search;

internal sealed record DriveSearchedMessage(List<Drive> SearchedDrives);
