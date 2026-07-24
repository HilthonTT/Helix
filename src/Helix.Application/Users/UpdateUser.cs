using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Errors;
using Helix.Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Helix.Application.Users;

public sealed class UpdateUser(
    IUserRepository userRepository,
    IUnitOfWork unitOfWork,
    ILoggedInUser loggedInUser) : IHandler
{
    public sealed record Request(string Username);

    public async Task<Result> Handle(Request request, CancellationToken cancellationToken = default)
    {
        Result validationResult = Validate(request);
        if (validationResult.IsFailure)
        {
            return validationResult;
        }

        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure(AuthenticationErrors.InvalidPermissions);
        }

        User? user = await userRepository.GetByIdAsync(loggedInUser.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure(UserErrors.NotFound);
        }

        if (!string.Equals(user.Username, request.Username, StringComparison.Ordinal) &&
            !await userRepository.IsUsernameUniqueAsync(request.Username, cancellationToken))
        {
            return Result.Failure(AuthenticationErrors.UsernameNotUnique);
        }

        user.Update(request.Username);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Result.Failure(AuthenticationErrors.UsernameNotUnique);
        }

        loggedInUser.Update(request.Username);

        return Result.Success();
    }

    private static Result Validate(Request request)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            return Result.Failure(ValidationErrors.MissingFields);
        }

        return Result.Success();
    }
}
