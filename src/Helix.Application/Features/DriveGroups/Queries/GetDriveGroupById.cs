using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Handlers;
using Helix.Domain.DriveGroups;
using Helix.Domain.Users;

namespace Helix.Application.Features.DriveGroups.Queries;

public sealed class GetDriveGroupById(
    IDriveGroupRepository driveGroupRepository,
    ILoggedInUser loggedInUser) : IHandler
{
    public sealed record Request(Guid DriveGroupId);

    public async Task<Result<DriveGroup>> Handle(Request request, CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure<DriveGroup>(AuthenticationErrors.InvalidPermissions);
        }

        DriveGroup? driveGroup = await driveGroupRepository.GetByIdAsNoTrackingAsync(
            request.DriveGroupId,
            cancellationToken);

        if (driveGroup is null)
        {
            return Result.Failure<DriveGroup>(DriveGroupErrors.NotFound(request.DriveGroupId));
        }

        return driveGroup.UserId != loggedInUser.UserId
            ? Result.Failure<DriveGroup>(AuthenticationErrors.InvalidPermissions)
            : driveGroup;
    }
}
