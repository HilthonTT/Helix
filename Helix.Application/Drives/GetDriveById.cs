using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Caching;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Extensions;
using Helix.Domain.Drives;
using Helix.Domain.Users;
using SharedKernel;

namespace Helix.Application.Drives;

public sealed class GetDriveById(IDbContext context, ILoggedInUser loggedInUser, ICacheService cacheService) : IHandler
{
    public sealed record Request(Guid DriveId);

    public async Task<Result<Drive>> Handle(Request request, CancellationToken cancellationToken = default)
    {
        Result validationResult = Validate(request);
        if (validationResult.IsFailure)
        {
            return Result.Failure<Drive>(validationResult.Error);
        }

        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure<Drive>(AuthenticationErrors.InvalidPermissions);
        }

        string cacheKey = CacheKeys.Drives.GetById(request.DriveId);
        Drive? drive = await GetCachedOrDbDriveAsync(request.DriveId, cacheKey, cancellationToken);

        return drive switch
        {
            null => Result.Failure<Drive>(DriveErrors.NotFound(request.DriveId)),
            _ when drive.UserId != loggedInUser.UserId => Result.Failure<Drive>(AuthenticationErrors.InvalidPermissions),
            _ => Result.Success(drive)
        };
    }

    private async Task<Drive?> GetCachedOrDbDriveAsync(Guid driveId, string cacheKey, CancellationToken cancellationToken)
    {
        Drive? drive = await cacheService.GetAsync<Drive>(cacheKey, cancellationToken);
        if (drive is not null)
        {
            return drive;
        }

        drive = await context.Drives.GetByIdAsNoTrackingAsync(driveId, cancellationToken);
        if (drive is not null)
        {
            await cacheService.SetAsync(cacheKey, drive, cancellationToken: cancellationToken);
        }

        return drive;
    }

    private static Result Validate(Request request)
    {
        if (request.DriveId == Guid.Empty)
        {
            return Result.Failure(Error.NullValue);
        }

        return Result.Success();
    }
}
