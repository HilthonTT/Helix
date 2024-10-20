using SharedKernel;

namespace Helix.Domain.Users;

public static class AuthenticationErrors
{
    public static readonly Error InvalidUsernameOrPassword = Error.Problem(
        "Authentication.InvalidUsernameOrPassword",
        "The specified username or password are incorrect.");

    public static readonly Error PasswordsDoNotMatch = Error.Problem(
        "Authentication.PasswordsDoNotMatch",
        "The specified passwords do not match.");

    public static readonly Error UsernameNotUnique = Error.Problem(
        "Authentication.UsernameNotUnique",
        "The specified username is not unique.");
}
