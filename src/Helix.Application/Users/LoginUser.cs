using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Cryptography;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Errors;
using Helix.Domain.Users;
using SharedKernel;

namespace Helix.Application.Users;

public sealed class LoginUser(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    ILoggedInUser loggedInUser,
    IUnitOfWork unitOfWork) : IHandler
{
    public sealed record Request(string Username, string Password);

    public async Task<Result<User>> Handle(Request request, CancellationToken cancellationToken = default)
    {
        Result validationResult = Validate(request);
        if (validationResult.IsFailure)
        {
            return Result.Failure<User>(validationResult.Error);
        }

        User? user = await userRepository.GetByUsernameAsync(request.Username, cancellationToken);
        if (user is null)
        {
            return Result.Failure<User>(AuthenticationErrors.InvalidUsernameOrPassword);
        }

        bool verified = passwordHasher.Verify(request.Password, user.PasswordHash);
        if (!verified)
        {
            return Result.Failure<User>(AuthenticationErrors.InvalidUsernameOrPassword);
        }

        // Transparent migration: if the stored hash uses an older format or weaker
        // KDF parameters than the current configuration, rehash with the verified
        // plaintext and persist. The user keeps the same credentials.
        if (passwordHasher.NeedsRehash(user.PasswordHash))
        {
            user.ChangePassword(passwordHasher.Hash(request.Password));
            await unitOfWork.SaveChangesAsync(cancellationToken);
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
