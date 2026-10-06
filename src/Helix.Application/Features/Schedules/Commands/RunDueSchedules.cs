using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Abstractions.Time;
using Helix.Application.Core.Drives;
using Helix.Application.Features.Schedules.Contracts;
using Helix.Domain.DriveGroups;
using Helix.Domain.Drives;
using Helix.Domain.Schedules;
using Helix.Domain.Users;

namespace Helix.Application.Features.Schedules.Commands;

public sealed class RunDueSchedules(
    IScheduleRepository scheduleRepository,
    IDriveGroupRepository driveGroupRepository,
    IDriveRepository driveRepository,
    IUnitOfWork unitOfWork,
    ILoggedInUser loggedInUser,
    INasConnector nasConnector,
    IDriveMonitor driveMonitor,
    INetworkLocation networkLocation,
    IDateTimeProvider dateTimeProvider,
    ILocalTimeZone localTimeZone) : IHandler
{
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(10);

    public async Task<Result<List<ScheduleRun>>> Handle(CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure<List<ScheduleRun>>(AuthenticationErrors.InvalidPermissions);
        }

        List<Schedule> schedules = await scheduleRepository.GetAsync(loggedInUser.UserId, cancellationToken);

        DateTime utcNow = dateTimeProvider.UtcNow;
        TimeZoneInfo zone = localTimeZone.Current;

        List<(Schedule Schedule, DateTime Occurrence)> due = [];

        foreach (Schedule schedule in schedules)
        {
            if (schedule.DueOccurrence(utcNow, zone, Grace) is DateTime occurrence)
            {
                due.Add((schedule, occurrence));
            }
        }

        if (due.Count == 0)
        {
            return new List<ScheduleRun>();
        }

        due.Sort((a, b) => a.Occurrence.CompareTo(b.Occurrence));

        foreach ((Schedule schedule, DateTime occurrence) in due)
        {
            schedule.MarkRun(occurrence);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        List<Drive> drives = await driveRepository.GetAsync(loggedInUser.UserId, cancellationToken);

        Dictionary<Guid, DriveGroup> groups = due.Any(pair => pair.Schedule.DriveGroupId is not null)
            ? (await driveGroupRepository.GetAsNoTrackingAsync(loggedInUser.UserId, cancellationToken))
                .ToDictionary(group => group.Id)
            : [];

        NetworkLocation? here = null;
        bool locationRead = false;

        List<ScheduleRun> runs = [];

        foreach ((Schedule schedule, _) in due)
        {
            DriveGroup? group = null;

            if (schedule.DriveGroupId is Guid groupId && !groups.TryGetValue(groupId, out group))
            {
                runs.Add(new ScheduleRun(
                    schedule.Id,
                    schedule.Action,
                    null,
                    [],
                    Result.Failure(DriveGroupErrors.NotFound(groupId))));

                continue;
            }

            List<Drive> members = group is null
                ? drives
                : [.. drives.Where(drive => group.DriveIds.Contains(drive.Id))];

            if (!schedule.Disconnects && members.Any(IsPinnedWithoutAwayAddress))
            {
                if (!locationRead)
                {
                    here = await networkLocation.GetCurrentAsync(cancellationToken);
                    locationRead = true;
                }

                members = [.. members.Where(drive => drive.RemoteHost is not null || !drive.IsAwayFrom(here?.Id))];
            }

            Result outcome = await DriveMountBatch.RunAsync(
                members,
                schedule.Disconnects,
                nasConnector,
                driveMonitor,
                unitOfWork,
                dateTimeProvider,
                cancellationToken);

            runs.Add(new ScheduleRun(
                schedule.Id,
                schedule.Action,
                group?.Name,
                [.. members.Select(drive => drive.Id)],
                outcome));
        }

        return runs;
    }

    private static bool IsPinnedWithoutAwayAddress(Drive drive) =>
        drive.HomeNetworkId is not null && drive.RemoteHost is null;
}
