using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Drives;
using Helix.Application.Features.Drives.Contracts;
using Helix.Domain.Auditlogs;
using Helix.Domain.Drives;
using Helix.Domain.Users;

namespace Helix.Application.Features.Drives.Queries;

public sealed class GetDriveReliability(
    IDriveRepository driveRepository,
    IAuditlogRepository auditlogRepository,
    ILoggedInUser loggedInUser,
    IDateTimeProvider dateTimeProvider) : IHandler
{
    public const int WindowDays = 30;

    public sealed record Request(Guid DriveId);

    public async Task<Result<DriveReliability>> Handle(Request request, CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure<DriveReliability>(AuthenticationErrors.InvalidPermissions);
        }

        Drive? drive = await driveRepository.GetByIdAsNoTrackingAsync(request.DriveId, cancellationToken);
        if (drive is null)
        {
            return Result.Failure<DriveReliability>(DriveErrors.NotFound(request.DriveId));
        }

        if (drive.UserId != loggedInUser.UserId)
        {
            return Result.Failure<DriveReliability>(AuthenticationErrors.InvalidPermissions);
        }

        DateTime utcNow = dateTimeProvider.UtcNow;

        List<Auditlog> history = await auditlogRepository.GetForEntitySinceAsNoTrackingAsync(
            loggedInUser.UserId,
            drive.Id,
            utcNow.AddDays(-WindowDays),
            cancellationToken);

        return ReliabilitySummary.Summarize(history, utcNow, WindowDays);
    }
}
