using Helix.Application.Abstractions.Authentication;
using SharedKernel;

namespace Helix.Application.Users;

public sealed class LogoutUser(ILoggedInUser loggedInUser)
{
    public Result Handle()
    {
        loggedInUser.Logout();

        return Result.Success();
    }
}
