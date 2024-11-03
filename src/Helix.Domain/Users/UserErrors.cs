using SharedKernel;

namespace Helix.Domain.Users;

public static class UserErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "User.NotFound", "The user has not been found.");
}
