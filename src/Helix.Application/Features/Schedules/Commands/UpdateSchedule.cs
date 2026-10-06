using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Domain.DriveGroups;
using Helix.Domain.Schedules;
using Helix.Domain.Users;

namespace Helix.Application.Features.Schedules.Commands;

public sealed class UpdateSchedule(
    IScheduleRepository scheduleRepository,
    IDriveGroupRepository driveGroupRepository,
    IUnitOfWork unitOfWork,
    ILoggedInUser loggedInUser) : IHandler
{
    public sealed record Request(
        Guid ScheduleId,
        Guid? DriveGroupId,
        ScheduleAction Action,
        TimeOnly TimeOfDay,
        ScheduleDays Days,
        bool IsEnabled);

    public async Task<Result<Schedule>> Handle(Request request, CancellationToken cancellationToken = default)
    {
        Result validationResult = CreateSchedule.Validate(request.Action, request.Days);
        if (validationResult.IsFailure)
        {
            return Result.Failure<Schedule>(validationResult.Error);
        }

        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure<Schedule>(AuthenticationErrors.InvalidPermissions);
        }

        Schedule? schedule = await scheduleRepository.GetByIdAsync(request.ScheduleId, cancellationToken);
        if (schedule is null)
        {
            return Result.Failure<Schedule>(ScheduleErrors.NotFound(request.ScheduleId));
        }

        if (schedule.UserId != loggedInUser.UserId)
        {
            return Result.Failure<Schedule>(AuthenticationErrors.InvalidPermissions);
        }

        if (request.DriveGroupId is Guid groupId &&
            !await CreateSchedule.IsOwnedGroupAsync(driveGroupRepository, loggedInUser, groupId, cancellationToken))
        {
            return Result.Failure<Schedule>(DriveGroupErrors.NotFound(groupId));
        }

        schedule.Update(request.DriveGroupId, request.Action, request.TimeOfDay, request.Days, request.IsEnabled);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return schedule;
    }
}
