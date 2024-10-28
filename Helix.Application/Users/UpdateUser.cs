using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Extensions;
using Helix.Domain.Users;
using SharedKernel;

namespace Helix.Application.Users;

public sealed class UpdateUser(IDbContext context, ILoggedInUser loggedInUser) : IHandler
{
    public sealed record Request(string Username);

    public async Task<Result> Handle(Request request, CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure(AuthenticationErrors.InvalidPermissions);
        }

        User? user = await context.Users.GetByIdAsync(loggedInUser.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure(UserErrors.NotFound);
        }

        user.Update(request.Username);

        await context.SaveChangesAsync(cancellationToken);

        loggedInUser.Update(request.Username);

        return Result.Success();
    }
}
