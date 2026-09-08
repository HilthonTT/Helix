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

        bool mounted = nasConnector.IsMountedFrom(drive);

        if (mounted || drive.Persistent)
        {
            using IDisposable suppression = driveMonitor.Suppress([drive.Letter]);

            Result unmount = await nasConnector.DisconnectAsync(drive, cancellationToken);
            if (unmount.IsFailure && mounted)
            {
                return unmount;
            }
        }

        driveRepository.Remove(drive);

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
