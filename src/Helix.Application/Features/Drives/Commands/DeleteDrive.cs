using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Errors;
using Helix.Domain.Drives;
using Helix.Domain.Users;

namespace Helix.Application.Features.Drives.Commands;

public sealed class DeleteDrive(
    IDriveRepository driveRepository,
    IUnitOfWork unitOfWork,
    ILoggedInUser loggedInUser,
    INasConnector nasConnector) : IHandler
{
    public sealed record Request(Guid DriveId);

    public async Task<Result> Handle(Request request, CancellationToken cancellationToken = default)
    {
        Result validationResult = Validate(request);
        if (validationResult.IsFailure)
        {
            return Result.Failure(validationResult.Error);
        }

        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure(AuthenticationErrors.InvalidPermissions);
        }

        Drive? drive = await driveRepository.GetByIdAsync(request.DriveId, cancellationToken);
        if (drive is null)
        {
            return Result.Failure(DriveErrors.NotFound(request.DriveId));
        }

        if (drive.UserId != loggedInUser.UserId)
        {
            return Result.Failure(AuthenticationErrors.InvalidPermissions);
        }

        // A persistent mapping lives in the Windows user profile, not in this database.
        // Deleting the row without cancelling it leaves Explorer restoring a share at
        // every sign-in that Helix no longer knows anything about — and no way to be rid
        // of it from inside the app, since the drive it belonged to is gone.
        //
        // Best effort on purpose: the deletion is what the user asked for, and a share
        // that is unreachable right now must not stand in the way of it.
        if (drive.Persistent)
        {
            await nasConnector.DisconnectAsync(drive, cancellationToken);
        }

        driveRepository.Remove(drive);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
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
