using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Domain.DriveGroups;
using Helix.Domain.Drives;
using Helix.Domain.Users;

namespace Helix.Application.Features.DriveGroups.Commands;

public sealed class CreateDriveGroup(
    IDriveGroupRepository driveGroupRepository,
    IDriveRepository driveRepository,
    IUnitOfWork unitOfWork,
    ILoggedInUser loggedInUser) : IHandler
{
    public sealed record Request(string Name, IReadOnlyList<Guid> DriveIds);

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

        string name = request.Name.Trim();

        if (!await driveGroupRepository.IsNameUniqueAsync(name, loggedInUser.UserId, null, cancellationToken))
        {
            return Result.Failure<DriveGroup>(DriveGroupErrors.NameNotUnique(name));
        }

        List<Guid> owned = await OwnedDriveIdsAsync(request.DriveIds, cancellationToken);
        if (owned.Count == 0)
        {
            return Result.Failure<DriveGroup>(DriveGroupErrors.NoDrivesSelected);
        }

        var driveGroup = DriveGroup.Create(loggedInUser.UserId, name, owned);

        driveGroupRepository.Insert(driveGroup);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return driveGroup;
    }

    private async Task<List<Guid>> OwnedDriveIdsAsync(
        IReadOnlyList<Guid> requested,
        CancellationToken cancellationToken)
    {
        List<Drive> drives = await driveRepository.GetAsNoTrackingAsync(loggedInUser.UserId, cancellationToken);

        HashSet<Guid> owned = [.. drives.Select(drive => drive.Id)];

        return [.. requested.Where(owned.Contains)];
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
