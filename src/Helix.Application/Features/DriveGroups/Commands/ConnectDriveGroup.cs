using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Drives;
using Helix.Domain.DriveGroups;
using Helix.Domain.Drives;
using Helix.Domain.Users;

namespace Helix.Application.Features.DriveGroups.Commands;

/// <summary>
/// Connects every drive in a group, or disconnects every one of them.
/// </summary>
/// <remarks>
/// One handler for both directions rather than two nearly identical ones: everything
/// around the mount is the same either way, and only the call in the middle differs. All
/// of that lives in <see cref="DriveMountBatch"/>, which an ad-hoc selection of rows hands
/// the same list to; what stays here is resolving the group to drives that exist and are
/// owned, in the order the user arranged them.
///
/// This is the explicit instruction the user pressed a button for, so a drive's own
/// <c>AutoConnect</c> flag does not apply: that flag holds a drive back from the
/// unattended passes, and putting a drive in a group is as deliberate as it gets.
/// </remarks>
public sealed class ConnectDriveGroup(
    IDriveGroupRepository driveGroupRepository,
    IDriveRepository driveRepository,
    IUnitOfWork unitOfWork,
    ILoggedInUser loggedInUser,
    INasConnector nasConnector,
    IDriveMonitor driveMonitor,
    IDateTimeProvider dateTimeProvider) : IHandler
{
    /// <param name="Disconnect">False connects the group, true takes it down.</param>
    public sealed record Request(Guid DriveGroupId, bool Disconnect = false);

    public async Task<Result> Handle(Request request, CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure(AuthenticationErrors.InvalidPermissions);
        }

        DriveGroup? driveGroup = await driveGroupRepository.GetByIdAsNoTrackingAsync(
            request.DriveGroupId,
            cancellationToken);

        if (driveGroup is null)
        {
            return Result.Failure(DriveGroupErrors.NotFound(request.DriveGroupId));
        }

        if (driveGroup.UserId != loggedInUser.UserId)
        {
            return Result.Failure(AuthenticationErrors.InvalidPermissions);
        }

        // Tracked: the drives that come up are stamped with the time, as they are on
        // every other path that mounts something.
        List<Drive> drives = await driveRepository.GetAsync(loggedInUser.UserId, cancellationToken);

        // The group's own order, so failures are reported in the order the user arranged
        // rather than in whatever order the database returned.
        Dictionary<Guid, Drive> byId = drives.ToDictionary(drive => drive.Id);

        Drive[] members = [.. driveGroup.DriveIds
            .Where(byId.ContainsKey)
            .Select(id => byId[id])];

        if (members.Length == 0)
        {
            // Every drive it named has since been deleted. Nothing to do, and nothing
            // the user did wrong.
            return Result.Success();
        }

        return await DriveMountBatch.RunAsync(
            members,
            request.Disconnect,
            nasConnector,
            driveMonitor,
            unitOfWork,
            dateTimeProvider,
            cancellationToken);
    }
}
