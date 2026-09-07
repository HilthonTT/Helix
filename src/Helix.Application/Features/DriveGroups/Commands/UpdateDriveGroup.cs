using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Domain.DriveGroups;
using Helix.Domain.Drives;
using Helix.Domain.Users;

namespace Helix.Application.Features.DriveGroups.Commands;

public sealed class UpdateDriveGroup(
    IDriveGroupRepository driveGroupRepository,
    IDriveRepository driveRepository,
    IUnitOfWork unitOfWork,
    ILoggedInUser loggedInUser) : IHandler
{
    public sealed record Request(Guid DriveGroupId, string Name, IReadOnlyList<Guid> DriveIds);

    public async Task<Result<DriveGroup>> Handle(Request request, CancellationToken cancellationToken = default)
    {
        Result validationResult = Validate(request);
        if (validationResult.IsFailure)
        {
            return Result.Failure<DriveGroup>(validationResult.Error);
        }

        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure<DriveGroup>(AuthenticationErrors.InvalidPermissions);
        }

        DriveGroup? driveGroup = await driveGroupRepository.GetByIdAsync(request.DriveGroupId, cancellationToken);
        if (driveGroup is null)
        {
            return Result.Failure<DriveGroup>(DriveGroupErrors.NotFound(request.DriveGroupId));
        }

        if (driveGroup.UserId != loggedInUser.UserId)
        {
            return Result.Failure<DriveGroup>(AuthenticationErrors.InvalidPermissions);
        }

        string name = request.Name.Trim();

        if (!await driveGroupRepository.IsNameUniqueAsync(
            name,
            loggedInUser.UserId,
            driveGroup.Id,
            cancellationToken))
        {
            return Result.Failure<DriveGroup>(DriveGroupErrors.NameNotUnique(name));
        }

        List<Drive> drives = await driveRepository.GetAsNoTrackingAsync(loggedInUser.UserId, cancellationToken);

        HashSet<Guid> owned = [.. drives.Select(drive => drive.Id)];

        List<Guid> driveIds = [.. request.DriveIds.Where(owned.Contains)];
        if (driveIds.Count == 0)
        {
            return Result.Failure<DriveGroup>(DriveGroupErrors.NoDrivesSelected);
        }

        driveGroup.Update(name, driveIds);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return driveGroup;
    }

    private static Result Validate(Request request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure(DriveGroupErrors.NameMissing);
        }

        return request.DriveIds.Count == 0
            ? Result.Failure(DriveGroupErrors.NoDrivesSelected)
            : Result.Success();
    }
}
