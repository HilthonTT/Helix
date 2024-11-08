using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Caching;
using Helix.Application.Abstractions.Handlers;
using Helix.Domain.Drives;
using Helix.Domain.Users;
using SharedKernel;

namespace Helix.Application.Drives;

public sealed class GetDrives(
    IDriveRepository driveRepository,
    ILoggedInUser loggedInUser,
    ICacheService cacheService) : IHandler
{
    public async Task<Result<List<Drive>>> Handle(CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure<List<Drive>>(AuthenticationErrors.InvalidPermissions);
        }

        List<Drive>? drives = await cacheService.GetAsync<List<Drive>>(CacheKeys.Drives.All, cancellationToken);
        if (drives is null)
        {
            drives = await driveRepository.GetAsNoTrackingAsync(loggedInUser.UserId, cancellationToken);

            await cacheService.SetAsync(CacheKeys.Drives.All, drives, cancellationToken: cancellationToken);
        }

        return drives;
    }
}
