using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Handlers;
using SharedKernel;

namespace Helix.Application.Users;

public sealed class LogoutUser(ILoggedInUser loggedInUser) : IHandler
{
    public async Task<Result> Handle(CancellationToken cancellationToken = default)
    {
        loggedInUser.Logout();

        return Result.Success();
    }
}
