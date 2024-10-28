using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Caching;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Extensions;
using Helix.Domain.Drives;
using Helix.Domain.Users;
using SharedKernel;

namespace Helix.Application.Drives;

public sealed class GetDrives(IDbContext context, ILoggedInUser loggedInUser, ICacheService cacheService) : IHandler
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
            drives = await context.Drives.GetAsNoTrackingAsync(loggedInUser.UserId, cancellationToken);

            await cacheService.SetAsync(CacheKeys.Drives.All, drives, cancellationToken: cancellationToken);
        }

        return drives;
    }
}
