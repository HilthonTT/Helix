using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Cryptography;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Errors;
using Helix.Domain.Users;

namespace Helix.Application.Features.Users.Commands;

/// <summary>
/// Checks the signed-in user's password, to let them back in after an idle lock.
/// </summary>
/// <remarks>
/// Not a sign-in: the session was never ended, so nothing is established here and
/// <see cref="ILoggedInUser"/> is only read. That is the whole difference between locking
/// and signing out, and it is deliberate — the drives stay mounted and the watchdog keeps
/// reconnecting them while the screen is locked, which is what an unattended tool is for.
///
/// It also means the check is one the user can only pass, never fail into somewhere else:
/// a wrong password leaves them exactly where they were, with no route back to the
/// dashboard other than this one.
/// </remarks>
public sealed class UnlockSession(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    ILoggedInUser loggedInUser) : IHandler
{
    public sealed record Request(string Password);

    public async Task<Result> Handle(Request request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Password))
        {
            return Result.Failure(ValidationErrors.MissingFields);
        }

        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure(AuthenticationErrors.InvalidPermissions);
        }

        User? user = await userRepository.GetByIdAsync(loggedInUser.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure(AuthenticationErrors.InvalidPermissions);
        }

        // The same error as a failed sign-in, and for the same reason: a message that
        // distinguished "wrong password" from anything else would be telling whoever is
        // at the machine something about the account.
        return passwordHasher.Verify(request.Password, user.PasswordHash)
            ? Result.Success()
            : Result.Failure(AuthenticationErrors.InvalidUsernameOrPassword);
    }
}
