using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Handlers;
using Helix.Domain.Drives;
using Helix.Domain.Users;

namespace Helix.Application.Features.Drives.Commands;

public sealed class WakeDrive(
    IDriveRepository driveRepository,
    ILoggedInUser loggedInUser,
    IWakeOnLan wakeOnLan) : IHandler
{
    public sealed record Request(Guid DriveId);

    public async Task<Result> Handle(Request request, CancellationToken cancellationToken = default)
    {
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

        if (drive.MacAddress is null)
        {
            return Result.Failure(DriveErrors.NoMacAddress);
        }

        return await wakeOnLan.WakeNowAsync(drive.MacAddress, cancellationToken)
            ? Result.Success()
            : Result.Failure(DriveErrors.WakeNotSent);
    }
}
