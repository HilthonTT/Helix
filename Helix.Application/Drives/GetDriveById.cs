using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Data;
using Helix.Application.Extensions;
using Helix.Domain.Drives;
using Helix.Domain.Users;
using SharedKernel;

namespace Helix.Application.Drives;

public sealed class GetDriveById(IDbContext context, ILoggedInUser loggedInUser)
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

        Drive? drive = await context.Drives.GetByIdAsNoTrackingAsync(request.DriveId, cancellationToken);
        if (drive is null)
        {
            return Result.Failure<Drive>(DriveErrors.NotFound(request.DriveId));
        }

        if (drive.UserId != loggedInUser.UserId)
        {
            return Result.Failure<Drive>(AuthenticationErrors.InvalidPermissions);
        }

        return drive;
    }

    private static Result Validate(Request request)
    {
        if (request.DriveId == Guid.Empty)
        {
            return Result.Failure(Error.NullValue);
        }

        return Result.Success();
    }
}
