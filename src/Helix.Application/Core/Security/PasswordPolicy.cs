namespace Helix.Application.Core.Security;

public static class PasswordPolicy
{
    public const int MinimumLength = 8;

    public static bool IsLongEnough(string? secret) => secret is not null && secret.Length >= MinimumLength;
}
