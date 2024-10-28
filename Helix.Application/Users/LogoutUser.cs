using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Caching;
using Helix.Application.Abstractions.Handlers;
using SharedKernel;

namespace Helix.Application.Users;

public sealed class LogoutUser(ILoggedInUser loggedInUser, ICacheService cacheService) : IHandler
{
    public async Task<Result> Handle(CancellationToken cancellationToken = default)
    {
        loggedInUser.Logout();

        await cacheService.RemoveAsync(CacheKeys.Drives.All, cancellationToken);

        await cacheService.RemoveByPrefixAsync(CacheKeys.Drives.MainPrefix, cancellationToken);

        return Result.Success();
    }
}
