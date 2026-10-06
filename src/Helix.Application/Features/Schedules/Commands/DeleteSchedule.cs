using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Errors;
using Helix.Domain.Schedules;
using Helix.Domain.Users;

namespace Helix.Application.Features.Schedules.Commands;

public sealed class DeleteSchedule(
    IScheduleRepository scheduleRepository,
    IUnitOfWork unitOfWork,
    ILoggedInUser loggedInUser) : IHandler
{
    public sealed record Request(Guid ScheduleId);

    public async Task<Result> Handle(Request request, CancellationToken cancellationToken = default)
    {
        if (request.ScheduleId == Guid.Empty)
        {
            return Result.Failure(ValidationErrors.MissingFields);
        }

        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure(AuthenticationErrors.InvalidPermissions);
        }

        Schedule? schedule = await scheduleRepository.GetByIdAsync(request.ScheduleId, cancellationToken);
        if (schedule is null)
        {
            return Result.Failure(ScheduleErrors.NotFound(request.ScheduleId));
        }

        if (schedule.UserId != loggedInUser.UserId)
        {
            return Result.Failure(AuthenticationErrors.InvalidPermissions);
        }

        scheduleRepository.Remove(schedule);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
