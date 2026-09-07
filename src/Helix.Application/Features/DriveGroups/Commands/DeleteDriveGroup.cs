using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Errors;
using Helix.Domain.DriveGroups;
using Helix.Domain.Users;

namespace Helix.Application.Features.DriveGroups.Commands;

public sealed class DeleteDriveGroup(
    IDriveGroupRepository driveGroupRepository,
    IUnitOfWork unitOfWork,
    ILoggedInUser loggedInUser) : IHandler
{
    public sealed record Request(Guid DriveGroupId);

    public async Task<Result> Handle(Request request, CancellationToken cancellationToken = default)
    {
        if (request.DriveGroupId == Guid.Empty)
        {
            return Result.Failure(ValidationErrors.MissingFields);
        }

        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure(AuthenticationErrors.InvalidPermissions);
        }

        DriveGroup? driveGroup = await driveGroupRepository.GetByIdAsync(request.DriveGroupId, cancellationToken);
        if (driveGroup is null)
        {
            return Result.Failure(DriveGroupErrors.NotFound(request.DriveGroupId));
        }

        if (driveGroup.UserId != loggedInUser.UserId)
        {
            return Result.Failure(AuthenticationErrors.InvalidPermissions);
        }

        driveGroupRepository.Remove(driveGroup);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
