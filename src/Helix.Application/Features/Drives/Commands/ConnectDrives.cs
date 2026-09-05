using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Drives;
using Helix.Domain.Drives;
using Helix.Domain.Users;

namespace Helix.Application.Features.Drives.Commands;

/// <summary>
/// Connects the drives the user picked out, or disconnects them.
/// </summary>
/// <remarks>
/// The middle ground the dashboard was missing. Everything was all-or-nothing — every
/// drive, or one row at a time — with saved groups as the only way to act on a subset, and
/// a group is a thing you set up in advance for a set you use repeatedly. Ticking three
/// rows is the answer to "these three, now", and it should not require creating a group
/// that is then left behind.
///
/// Like a group, and unlike the unattended passes, this ignores each drive's own
/// <c>AutoConnect</c> flag: that flag holds a drive back from what Helix does on its own,
/// and ticking a row is as deliberate as it gets.
/// </remarks>
public sealed class ConnectDrives(
    IDriveRepository driveRepository,
    IUnitOfWork unitOfWork,
    ILoggedInUser loggedInUser,
    INasConnector nasConnector,
    IDriveMonitor driveMonitor,
    IDateTimeProvider dateTimeProvider) : IHandler
{
    /// <param name="DriveIds">
    /// What the user ticked. Ids that name nothing are skipped rather than failing the
    /// batch — a row can be deleted from another window while a selection is held.
    /// </param>
    /// <param name="Disconnect">False mounts them, true takes them down.</param>
    public sealed record Request(IReadOnlyList<Guid> DriveIds, bool Disconnect = false);

    public async Task<Result> Handle(Request request, CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure(AuthenticationErrors.InvalidPermissions);
        }

        if (request.DriveIds.Count == 0)
        {
            return Result.Success();
        }

        // Tracked: the drives that come up are stamped with the time, as they are on
        // every other path that mounts something.
        List<Drive> drives = await driveRepository.GetAsync(loggedInUser.UserId, cancellationToken);

        // Read from the signed-in user's own drives, so an id belonging to somebody else
        // resolves to nothing here rather than being checked for ownership afterwards.
        Dictionary<Guid, Drive> byId = drives.ToDictionary(drive => drive.Id);

        // The order the ids arrived in, which is the order of the rows on screen, so
        // failures read down the list the user was looking at. Distinct, because a
        // repeated id would otherwise be mounted twice.
        Drive[] targets = [.. request.DriveIds
            .Distinct()
            .Where(byId.ContainsKey)
            .Select(id => byId[id])];

        if (targets.Length == 0)
        {
            return Result.Success();
        }

        return await DriveMountBatch.RunAsync(
            targets,
            request.Disconnect,
            nasConnector,
            driveMonitor,
            unitOfWork,
            dateTimeProvider,
            cancellationToken);
    }
}
