using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Cryptography;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Errors;
using Helix.Domain.Users;

namespace Helix.Application.Features.Users.Commands;

public sealed class CreateRecoveryKey(
    IUserRepository userRepository,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    IRecoveryKeyGenerator recoveryKeyGenerator,
    ILoggedInUser loggedInUser) : IHandler
{
    public sealed record Request(string CurrentPassword);

    public async Task<Result<string>> Handle(Request request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.CurrentPassword))
        {
            return Result.Failure<string>(ValidationErrors.MissingFields);
        }

        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure<string>(AuthenticationErrors.InvalidPermissions);
        }

        User? user = await userRepository.GetByIdAsync(loggedInUser.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<string>(AuthenticationErrors.InvalidPermissions);
        }

        if (!passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            return Result.Failure<string>(AuthenticationErrors.InvalidUsernameOrPassword);
        }

        string recoveryKey = recoveryKeyGenerator.Generate();

        user.SetRecoveryKey(passwordHasher.Hash(recoveryKey));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return recoveryKey;
    }
}
