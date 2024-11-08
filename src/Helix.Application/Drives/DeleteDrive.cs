using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Caching;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Errors;
using Helix.Domain.Drives;
using Helix.Domain.Users;
using SharedKernel;

namespace Helix.Application.Drives;

public sealed class DeleteDrive(
    IDriveRepository driveRepository,
    IUnitOfWork unitOfWork,
    ILoggedInUser loggedInUser, 
    ICacheService cacheService) : IHandler
{
    public sealed record Request(Guid DriveId);

    public async Task<Result> Handle(Request request, CancellationToken cancellationToken = default)
    {
        Result validationResult = Validate(request);
        if (validationResult.IsFailure)
        {
            return Result.Failure(validationResult.Error);
        }

        Drive? drive = await driveRepository.GetByIdAsync(request.DriveId, cancellationToken);
        if (drive is null)
        {
            return Result.Failure(DriveErrors.NotFound(request.DriveId));
        }

        if (drive.UserId != loggedInUser.UserId)
        {
            return Result.Failure(AuthenticationErrors.InvalidPermissions);
        }

        driveRepository.Remove(drive);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await cacheService.RemoveAsync(CacheKeys.Drives.All, cancellationToken);

        await cacheService.RemoveAsync(CacheKeys.Drives.GetById(request.DriveId), cancellationToken);

        return Result.Success();
    }

    private static Result Validate(Request request)
    {
        if (request.DriveId == Guid.Empty)
        {
            return Result.Failure(ValidationErrors.MissingFields);
        }

        return Result.Success();
    }
}
