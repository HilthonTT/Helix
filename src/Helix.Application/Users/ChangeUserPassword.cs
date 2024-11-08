using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Cryptography;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Errors;
using Helix.Domain.Drives;
using Helix.Domain.Users;
using SharedKernel;

namespace Helix.Application.Users;

public sealed class ChangeUserPassword(
    IUserRepository userRepository,
    IUnitOfWork unitOfWork,
    ILoggedInUser loggedInUser, 
    IPasswordHasher passwordHasher) : IHandler
{
    public sealed record Request(string CurrentPassword, string NewPassword, string ConfirmedNewPassword);

    public async Task<Result> Handle(Request request, CancellationToken cancellationToken = default)
    {
        Result validationResult = Validate(request);
        if (validationResult.IsFailure)
        {
            return Result.Failure<Drive>(validationResult.Error);
        }

        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure(AuthenticationErrors.InvalidPermissions);
        }

        if (request.NewPassword != request.ConfirmedNewPassword)
        {
            return Result.Failure(AuthenticationErrors.NewPasswordsDoNotMatch);
        }

        User? user = await userRepository.GetByIdAsync(loggedInUser.UserId, cancellationToken);
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

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private static Result Validate(Request request)
    {
        string[] properties = [request.CurrentPassword, request.NewPassword, request.ConfirmedNewPassword];

        return properties.Any(string.IsNullOrWhiteSpace)
            ? Result.Failure(ValidationErrors.MissingFields)
            : Result.Success();
    }
}
