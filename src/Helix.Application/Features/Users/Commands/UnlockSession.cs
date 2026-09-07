using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Cryptography;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Errors;
using Helix.Domain.Users;

namespace Helix.Application.Features.Users.Commands;

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

        return passwordHasher.Verify(request.Password, user.PasswordHash)
            ? Result.Success()
            : Result.Failure(AuthenticationErrors.InvalidUsernameOrPassword);
    }
}
