using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Cryptography;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Errors;
using Helix.Application.Core.Security;
using Helix.Domain.Users;
using Microsoft.Extensions.Logging;

namespace Helix.Application.Features.Users.Commands;

public sealed class ResetPasswordWithRecoveryKey(
    IUserRepository userRepository,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    IRecoveryKeyGenerator recoveryKeyGenerator,
    ILoggedInUser loggedInUser,
    ILogger<ResetPasswordWithRecoveryKey> logger) : IHandler
{
    public sealed record Request(string Username, string RecoveryKey, string NewPassword, string ConfirmedNewPassword);

    public sealed record Response(User User, string RecoveryKey);

    public async Task<Result<Response>> Handle(Request request, CancellationToken cancellationToken = default)
    {
        Result validationResult = Validate(request);
        if (validationResult.IsFailure)
        {
            return Result.Failure<Response>(validationResult.Error);
        }

        if (request.NewPassword != request.ConfirmedNewPassword)
        {
            return Result.Failure<Response>(AuthenticationErrors.NewPasswordsDoNotMatch);
        }

        string? recoveryKey = RecoveryKeyFormat.Normalize(request.RecoveryKey);
        if (recoveryKey is null)
        {
            return Result.Failure<Response>(AuthenticationErrors.InvalidUsernameOrRecoveryKey);
        }

        User? user = await userRepository.GetByUsernameAsync(request.Username.Trim(), cancellationToken);
        if (user?.RecoveryKeyHash is null || !passwordHasher.Verify(recoveryKey, user.RecoveryKeyHash))
        {
            return Result.Failure<Response>(AuthenticationErrors.InvalidUsernameOrRecoveryKey);
        }

        string newRecoveryKey = recoveryKeyGenerator.Generate();

        user.ChangePassword(passwordHasher.Hash(request.NewPassword));
        user.SetRecoveryKey(passwordHasher.Hash(newRecoveryKey));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("A password was reset with a recovery key, and a new recovery key was issued.");

        loggedInUser.Login(user.Id, user.Username);

        return new Response(user, newRecoveryKey);
    }

    private static Result Validate(Request request)
    {
        string[] properties = [request.Username, request.RecoveryKey, request.NewPassword, request.ConfirmedNewPassword];

        return properties.Any(string.IsNullOrWhiteSpace)
            ? Result.Failure(ValidationErrors.MissingFields)
            : Result.Success();
    }
}
