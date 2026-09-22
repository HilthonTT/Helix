using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Domain.Auditlogs;
using Helix.Domain.Drives;
using Helix.Domain.Users;

namespace Helix.Application.Features.Drives.Commands;

public sealed class ReconnectDrive(
    IDriveRepository driveRepository,
    IAuditlogRepository auditlogRepository,
    IUnitOfWork unitOfWork,
    ILoggedInUser loggedInUser,
    INasConnector nasConnector,
    IDriveRouter driveRouter,
    INetworkLocation networkLocation,
    IDateTimeProvider dateTimeProvider) : IHandler
{
    public sealed record Request(Guid DriveId, bool AttemptReconnect, bool RecordDrop = true);

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

        if (request.RecordDrop)
        {
            Log(AuditAction.DriveDisconnected);
        }

        if (!request.AttemptReconnect)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }

        if (drive.HomeNetworkId is not null && drive.RemoteHost is null)
        {
            NetworkLocation? here = await networkLocation.GetCurrentAsync(cancellationToken);

            if (drive.IsAwayFrom(here?.Id))
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);

                return Result.Failure(DriveErrors.AwayFromHomeNetwork(drive.HomeNetworkName));
            }
        }

        Result<DriveRoute> route = await driveRouter.RouteAsync(drive, cancellationToken: cancellationToken);
        if (route.IsFailure)
        {
            if (request.RecordDrop)
            {
                Log(AuditAction.DriveReconnectFailed, route.Error.Description);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Failure(route.Error);
        }

        Result result = await nasConnector.ConnectAsync(drive, cancellationToken);

        if (result.IsSuccess)
        {
            drive.MarkConnected(dateTimeProvider.UtcNow);

            Log(AuditAction.DriveReconnected);
        }
        else if (request.RecordDrop)
        {
            Log(AuditAction.DriveReconnectFailed, result.Error.Description);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return result;

        void Log(AuditAction action, string? detail = null) =>
            auditlogRepository.Insert(Auditlog.ForDrive(
                loggedInUser.UserId,
                action,
                drive.Id,
                drive.Name,
                drive.Letter,
                detail));
    }
}
