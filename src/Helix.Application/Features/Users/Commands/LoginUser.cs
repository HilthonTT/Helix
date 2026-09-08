using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Cryptography;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Errors;
using Helix.Domain.Users;

using Microsoft.Extensions.Logging;

namespace Helix.Application.Features.Users.Commands;

public sealed class LoginUser(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    ILoggedInUser loggedInUser,
    IUnitOfWork unitOfWork,
    ILogger<LoginUser> logger) : IHandler
{
    public sealed record Request(string Username, string Password);

    public async Task<Result<User>> Handle(Request request, CancellationToken cancellationToken = default)
    {
        Result validationResult = Validate(request);
        if (validationResult.IsFailure)
        {
            return Result.Failure<User>(validationResult.Error);
        }

        User? user = await userRepository.GetByUsernameAsync(request.Username.Trim(), cancellationToken);
        if (user is null)
        {
            return Result.Failure<User>(AuthenticationErrors.InvalidUsernameOrPassword);
        }

        bool verified = passwordHasher.Verify(request.Password, user.PasswordHash);
        if (!verified)
        {
            return Result.Failure<User>(AuthenticationErrors.InvalidUsernameOrPassword);
        }

        if (passwordHasher.NeedsRehash(user.PasswordHash))
        {
            try
            {
                user.ChangePassword(passwordHasher.Hash(request.Password));
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not store the upgraded password hash; the sign-in continues on the existing one.");
            }
        }

        loggedInUser.Login(user.Id, user.Username);

        return user;
    }

    private static Result Validate(Request request)
    {
        string[] properties = [request.Username, request.Password];

        return properties.Any(string.IsNullOrWhiteSpace)
            ? Result.Failure(ValidationErrors.MissingFields)
            : Result.Success();
    }
}
