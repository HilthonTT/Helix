using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Abstractions.Updates;
using Helix.Domain.Users;

namespace Helix.Application.Features.Updates.Queries;

/// <summary>
/// Reports whether a newer Helix has been released.
/// </summary>
/// <remarks>
/// A thin pass-through to <see cref="IUpdateChecker"/>: there is no domain rule to apply
/// here, only the authorization check every handler makes and a use case for the
/// presentation layer to invoke through <c>ScopedHandler</c> like everything else.
/// </remarks>
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
