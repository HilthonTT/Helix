using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Cryptography;
using Helix.Application.Abstractions.Data;
using Helix.Application.Core.Extensions;
using Helix.Domain.Users;
using SharedKernel;

namespace Helix.Application.Users;

public sealed class ChangeUserPassword(IDbContext context, ILoggedInUser loggedInUser, IPasswordHasher passwordHasher)
{
    public sealed record Request(string CurrentPassword, string NewPassword, string ConfirmedNewPassword);

    public async Task<Result> Handle(Request request, CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure(AuthenticationErrors.InvalidPermissions);
        }

        if (request.NewPassword != request.ConfirmedNewPassword)
        {
            return Result.Failure(AuthenticationErrors.NewPasswordsDoNotMatch);
        }

        User? user = await context.Users.GetByIdAsync(loggedInUser.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure(AuthenticationErrors.InvalidPermissions);
        }

        bool verified = passwordHasher.Verify(request.CurrentPassword, user.PasswordHash);
        if (!verified)
        {
            return Result.Failure(AuthenticationErrors.InvalidUsernameOrPassword);
        }

        string passwordHash = passwordHasher.Hash(request.NewPassword);

        user.ChangePassword(passwordHash);

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
