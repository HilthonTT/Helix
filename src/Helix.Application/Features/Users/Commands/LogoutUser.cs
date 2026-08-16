using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Handlers;

namespace Helix.Application.Features.Users.Commands;

public sealed class LogoutUser(ILoggedInUser loggedInUser) : IHandler
{
    public async Task<Result> Handle(CancellationToken cancellationToken = default)
    {
        loggedInUser.Logout();

        return Result.Success();
    }
}
