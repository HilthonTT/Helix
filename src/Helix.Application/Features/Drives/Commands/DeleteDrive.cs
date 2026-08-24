using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Errors;
using Helix.Domain.DriveGroups;
using Helix.Domain.Drives;
using Helix.Domain.Users;

namespace Helix.Application.Features.Drives.Commands;

public sealed class DeleteDrive(
    IDriveRepository driveRepository,
    IDriveGroupRepository driveGroupRepository,
    IUnitOfWork unitOfWork,
    ILoggedInUser loggedInUser,
    INasConnector nasConnector,
    IDriveMonitor driveMonitor) : IHandler
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
            // Announced like every other unmount Helix performs. The watchdog drops this
            // drive from its watch set when the deletion is broadcast, but that happens
            // after the fact, and a poll landing in between would file a lost connection
            // for a drive that is on its way out and then try to reconnect it.
            using IDisposable suppression = driveMonitor.Suppress([drive.Letter]);

            await nasConnector.DisconnectAsync(drive, cancellationToken);
        }

        driveRepository.Remove(drive);

        // Groups hold drive ids rather than a foreign key, so nothing else would clear
        // this. A group would still read correctly — an id naming nothing resolves to
        // nothing — but an install rearranged over a year should not accumulate a list of
        // ids that point at drives the user deleted.
        List<DriveGroup> groups = await driveGroupRepository.GetAsync(loggedInUser.UserId, cancellationToken);

        foreach (DriveGroup group in groups)
        {
            group.Remove(drive.Id);
        }

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
