using SharedKernel;

namespace Helix.Domain.Users;

public static class AuthenticationErrors
{
    public static readonly Error InvalidUsernameOrPassword = Error.Problem(
        "Authentication.InvalidUsernameOrPassword",
        "The specified username or password are incorrect.");
}
