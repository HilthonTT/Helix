using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Handlers;
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

        return Summarize(history, utcNow);
    }

    private static DriveReliability Summarize(List<Auditlog> history, DateTime utcNow)
    {
        DateTime weekAgo = utcNow.AddDays(-7);

        int drops = 0;
        int dropsLastWeek = 0;
        int failedReconnects = 0;
        DateTime? lastDrop = null;
        DateTime? pendingDrop = null;
        List<TimeSpan> recoveries = [];

        foreach (Auditlog row in history)
        {
            switch (row.Action)
            {
                case AuditAction.DriveDisconnected:
                    drops++;

                    if (row.CreatedOnUtc >= weekAgo)
                    {
                        dropsLastWeek++;
                    }

                    lastDrop = row.CreatedOnUtc;
                    pendingDrop = row.CreatedOnUtc;
                    break;

                case AuditAction.DriveReconnectFailed:
                    failedReconnects++;
                    break;

                case AuditAction.DriveReconnected when pendingDrop is DateTime droppedAt:
                    recoveries.Add(row.CreatedOnUtc - droppedAt);
                    pendingDrop = null;
                    break;
            }
        }

        return new DriveReliability(
            WindowDays,
            drops,
            dropsLastWeek,
            recoveries.Count,
            failedReconnects,
            Median(recoveries),
            lastDrop);
    }

    private static TimeSpan? Median(List<TimeSpan> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        values.Sort();

        int middle = values.Count / 2;

        return values.Count % 2 == 1
            ? values[middle]
            : (values[middle - 1] + values[middle]) / 2;
    }
}
