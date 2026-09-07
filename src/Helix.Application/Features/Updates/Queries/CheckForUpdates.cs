using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Abstractions.Updates;
using Helix.Domain.Users;

namespace Helix.Application.Features.Updates.Queries;

public sealed class CheckForUpdates(
    ILoggedInUser loggedInUser,
    IUpdateChecker updateChecker) : IHandler
{
    public async Task<Result<UpdateCheck>> Handle(CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure<UpdateCheck>(AuthenticationErrors.InvalidPermissions);
        }

        return await updateChecker.CheckAsync(cancellationToken);
    }
}
