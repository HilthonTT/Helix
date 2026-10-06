using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Domain.DriveGroups;
using Helix.Domain.Schedules;
using Helix.Domain.Users;

namespace Helix.Application.Features.Schedules.Commands;

public sealed class CreateSchedule(
    IScheduleRepository scheduleRepository,
    IDriveGroupRepository driveGroupRepository,
    IUnitOfWork unitOfWork,
    ILoggedInUser loggedInUser) : IHandler
{
    public sealed record Request(Guid? DriveGroupId, ScheduleAction Action, TimeOnly TimeOfDay, ScheduleDays Days);

    public async Task<Result<Schedule>> Handle(Request request, CancellationToken cancellationToken = default)
    {
        Result validationResult = Validate(request.Action, request.Days);
        if (validationResult.IsFailure)
        {
            return Result.Failure<Schedule>(validationResult.Error);
        }

        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure<Schedule>(AuthenticationErrors.InvalidPermissions);
        }

        if (request.DriveGroupId is Guid groupId &&
            !await IsOwnedGroupAsync(driveGroupRepository, loggedInUser, groupId, cancellationToken))
        {
            return Result.Failure<Schedule>(DriveGroupErrors.NotFound(groupId));
        }

        var schedule = Schedule.Create(
            loggedInUser.UserId,
            request.DriveGroupId,
            request.Action,
            request.TimeOfDay,
            request.Days);

        scheduleRepository.Insert(schedule);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return schedule;
    }

    internal static Result Validate(ScheduleAction action, ScheduleDays days)
    {
        if (!Enum.IsDefined(action))
        {
            return Result.Failure(ScheduleErrors.UnknownAction);
        }

        return (days & ScheduleDays.Everyday) == ScheduleDays.None
            ? Result.Failure(ScheduleErrors.NoDaysSelected)
            : Result.Success();
    }

    internal static async Task<bool> IsOwnedGroupAsync(
        IDriveGroupRepository driveGroupRepository,
        ILoggedInUser loggedInUser,
        Guid groupId,
        CancellationToken cancellationToken)
    {
        DriveGroup? group = await driveGroupRepository.GetByIdAsNoTrackingAsync(groupId, cancellationToken);

        return group is not null && group.UserId == loggedInUser.UserId;
    }
}
