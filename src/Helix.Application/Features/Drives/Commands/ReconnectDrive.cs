using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Domain.Auditlogs;
using Helix.Domain.Drives;
using Helix.Domain.Users;

namespace Helix.Application.Features.Drives.Commands;

/// <summary>
/// Handles a drive that the monitor observed dropping: records the loss and, when
/// asked, tries to bring it back.
/// </summary>
/// <remarks>
/// This is separate from <see cref="ConnectDrive"/> because the two are different
/// events. A user pressing connect needs no audit entry — they know what they did.
/// An unattended drop and recovery is the only trace of what happened while the app
/// sat minimised, so it is written to the log.
///
/// <see cref="Request.RecordDrop"/> keeps the log readable while a NAS is down: the
/// loss and the first failed attempt are recorded once, and the silent retries that
/// follow only write again if one of them succeeds.
/// </remarks>
public sealed class ReconnectDrive(
    IDriveRepository driveRepository,
    IAuditlogRepository auditlogRepository,
    IUnitOfWork unitOfWork,
    ILoggedInUser loggedInUser,
    INasConnector nasConnector,
    IDateTimeProvider dateTimeProvider) : IHandler
{
    /// <param name="DriveId">The drive observed dropping.</param>
    /// <param name="AttemptReconnect">False when auto-connect is off — record only.</param>
    /// <param name="RecordDrop">
    /// True on the first handling of a drop, false for the retries that follow.
    /// </param>
    public sealed record Request(Guid DriveId, bool AttemptReconnect, bool RecordDrop = true);

    public async Task<Result> Handle(Request request, CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure(AuthenticationErrors.InvalidPermissions);
        }

        // Tracked: a successful reconnect stamps the drive alongside its audit entry.
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

        // Saved whichever way it went: the audit entry is the point of this handler.
        // A retry that failed silently writes nothing, so this is a no-op there.
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
