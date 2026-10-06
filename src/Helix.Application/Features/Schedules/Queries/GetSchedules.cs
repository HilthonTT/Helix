using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Handlers;
using Helix.Domain.Schedules;
using Helix.Domain.Users;

namespace Helix.Application.Features.Schedules.Queries;

public sealed class GetSchedules(
    IScheduleRepository scheduleRepository,
    ILoggedInUser loggedInUser) : IHandler
{
    public async Task<Result<List<Schedule>>> Handle(CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure<List<Schedule>>(AuthenticationErrors.InvalidPermissions);
        }

        List<Schedule> schedules = await scheduleRepository.GetAsNoTrackingAsync(
            loggedInUser.UserId,
            cancellationToken);

        return schedules
            .OrderBy(schedule => schedule.TimeOfDay)
            .ThenBy(schedule => schedule.CreatedOnUtc)
            .ToList();
    }
}
