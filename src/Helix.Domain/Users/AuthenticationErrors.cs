namespace Helix.Domain.Users;

public static class AuthenticationErrors
{
    public static readonly Error InvalidUsernameOrPassword = Error.Problem(
        "Authentication.InvalidUsernameOrPassword",
        "The username or password is incorrect.");

    public static readonly Error PasswordsDoNotMatch = Error.Problem(
        "Authentication.PasswordsDoNotMatch",
        "The passwords do not match.");

    public static readonly Error PasswordTooShort = Error.Problem(
        "Authentication.PasswordTooShort",
        "The password must be at least 8 characters long.");

    public static readonly Error UsernameNotUnique = Error.Problem(
        "Authentication.UsernameNotUnique",
        "That username is already taken.");

    public static readonly Error InvalidPermissions = Error.Problem(
        "Authentication.InvalidPermissions",
        "You need to be signed in to do that.");

    public static readonly Error NewPasswordsDoNotMatch = Error.Problem(
        "Authentication.NewPasswordsDoNotMatch",
        "The new passwords do not match.");

    public static readonly Error InvalidUsernameOrRecoveryKey = Error.Problem(
        "Authentication.InvalidUsernameOrRecoveryKey",
        "The username or recovery key is incorrect.");
}
