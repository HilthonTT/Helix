using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Drives;
using Helix.Domain.DriveGroups;
using Helix.Domain.Drives;
using Helix.Domain.Users;

namespace Helix.Application.Features.DriveGroups.Commands;

public sealed class ConnectDriveGroup(
    IDriveGroupRepository driveGroupRepository,
    IDriveRepository driveRepository,
    IUnitOfWork unitOfWork,
    ILoggedInUser loggedInUser,
    INasConnector nasConnector,
    IDriveMonitor driveMonitor,
    IDateTimeProvider dateTimeProvider) : IHandler
{
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

        List<Drive> drives = await driveRepository.GetAsync(loggedInUser.UserId, cancellationToken);

        Dictionary<Guid, Drive> byId = drives.ToDictionary(drive => drive.Id);

        Drive[] members = [.. driveGroup.DriveIds
            .Where(byId.ContainsKey)
            .Select(id => byId[id])];

        if (members.Length == 0)
        {
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
