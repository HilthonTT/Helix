using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Errors;
using Helix.Domain.Drives;
using Helix.Domain.Users;
using SharedKernel;

namespace Helix.Application.Drives;

public sealed class GetDriveById(
    IDriveRepository driveRepository,
    ILoggedInUser loggedInUser) : IHandler
{
    public sealed record Request(Guid DriveId);

    public async Task<Result<Drive>> Handle(Request request, CancellationToken cancellationToken = default)
    {
        Result validationResult = Validate(request);
        if (validationResult.IsFailure)
        {
            return Result.Failure<Drive>(validationResult.Error);
        }

        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure<Drive>(AuthenticationErrors.InvalidPermissions);
        }

        Drive? drive = await driveRepository.GetByIdAsNoTrackingAsync(request.DriveId, cancellationToken);

        return drive switch
        {
            null => Result.Failure<Drive>(DriveErrors.NotFound(request.DriveId)),
            _ when drive.UserId != loggedInUser.UserId => Result.Failure<Drive>(AuthenticationErrors.InvalidPermissions),
            _ => Result.Success(drive)
        };
    }

    private static Result Validate(Request request)
    {
        if (request.DriveId == Guid.Empty)
        {
            return Result.Failure(ValidationErrors.MissingFields);
        }

        return Result.Success();
    }
}
