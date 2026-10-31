using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Drives;
using Helix.Application.Features.Drives.Contracts;
using Helix.Domain.Auditlogs;
using Helix.Domain.Drives;
using Helix.Domain.Users;

namespace Helix.Application.Features.Drives.Queries;

public sealed class GetEstateReliability(
    IDriveRepository driveRepository,
    IAuditlogRepository auditlogRepository,
    ILoggedInUser loggedInUser,
    IDateTimeProvider dateTimeProvider) : IHandler
{
    public const int WindowDays = GetDriveReliability.WindowDays;

    public async Task<Result<List<DriveReliabilityEntry>>> Handle(CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure<List<DriveReliabilityEntry>>(AuthenticationErrors.InvalidPermissions);
        }

        List<Drive> drives = await driveRepository.GetAsNoTrackingAsync(loggedInUser.UserId, cancellationToken);
        if (drives.Count == 0)
        {
            return new List<DriveReliabilityEntry>();
        }

        DateTime utcNow = dateTimeProvider.UtcNow;

        List<Auditlog> history = await auditlogRepository.GetSinceAsNoTrackingAsync(
            loggedInUser.UserId,
            utcNow.AddDays(-WindowDays),
            cancellationToken);

        ILookup<Guid, Auditlog> byDrive = history
            .Where(row => row.EntityId is not null)
            .ToLookup(row => row.EntityId!.Value);

        return drives
            .Select(drive => new DriveReliabilityEntry(
                drive.Id,
                drive.Letter,
                drive.Name,
                ReliabilitySummary.Summarize(byDrive[drive.Id], utcNow, WindowDays)))
            .OrderByDescending(entry => entry.Reliability.Drops)
            .ThenByDescending(entry => entry.Reliability.FailedReconnects)
            .ThenBy(entry => entry.Letter, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
